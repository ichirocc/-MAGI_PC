using System.Threading;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>hf80PostPolish</c>/<c>softPolishOnly</c> (phase 5c scope — called
/// directly by <c>runRsiPlus</c>, not deferred). Per this port's standing scope decision,
/// <c>magi_native.cpp</c> is out of scope; Kotlin's native-JNI polish-chunk dispatch
/// (<c>runPolishChunksNative</c>/<c>NativePolishRun</c>, the <c>nat.completed</c> early-return
/// branch, and the <c>nat.best == null</c>-driven <c>lastImproveMs</c> branch) is entirely
/// OMITTED, not stubbed — the ported code proceeds directly to what was the Kotlin-fallback body,
/// matching the <see cref="SaOptimizer.RunWorker"/>/<see cref="RunAlnsSingle"/> precedent exactly
/// (the omission collapses to what the Kotlin original does when <c>nat.best == null</c>: the
/// stall clock starts at <c>started</c>, unconditionally).
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>Faithful port of Kotlin's private <c>PolishResult</c> data class.</summary>
    internal sealed record PolishResult(int[][] Schedule, IReadOnlyList<MirrorLog> Logs, long Iterations);

    /// <summary>
    /// [ソフト研磨専用, Kotlin原本] 現在の盤面をHARDガード付きで局所研磨し、SOFTのみ削減する公開エントリ。
    /// 破壊/多様化フェーズは行わず、<see cref="Hf80PostPolish"/> の keep-best＋退化防止により入力以上の
    /// 盤面のみ返す（HARD=0 は壊さない）。最適化(もう一度つくる)と違い、必須が一時的に増えることはない。
    ///
    /// [C#移植上の判断] <see cref="Hf80PostPolish"/> 自体は（<see cref="RunAlnsSingle"/> と同じ理由で）
    /// 純粋な同期メソッドだが、この公開エントリは呼出側（UI/ViewModel）から素直に await できる必要が
    /// あるため <see cref="Task.Run{TResult}(Func{TResult})"/> で包む（<see cref="RunAlnsChains"/> が
    /// <see cref="RunAlnsSingle"/> を並列化のために包むのと同じ設計判断＝CPUバウンドな同期処理を呼出側
    /// スレッドから切り離す）。<paramref name="cancellationToken"/> は <c>Task.Run</c> 自身の
    /// スケジューリング側キャンセル（既に取消済みなら実行自体をスキップし即 Canceled task を返す）へは
    /// 渡さない — それだと事前取消トークンで <see cref="Hf80PostPolish"/> の「クリーンに即返す」契約
    /// （入力を変更せず返す）が壊れ、代わりに <c>TaskCanceledException</c> を投げてしまう
    /// （<see cref="RunAlns"/> の Workers&lt;=1 経路と同じ「内側の非throwポーリングに任せる」判断）。
    /// </summary>
    public static Task<int[][]> SoftPolishOnly(
        MagiState state,
        int[][] schedule,
        int seconds,
        long seed = 0x50F11L,
        Func<bool>? shouldStop = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Hf80PostPolish(state, schedule, Math.Max(1, seconds), seed, shouldStop, cancellationToken).Schedule);

    /// <summary>
    /// [差分化移植, Kotlin原本] 最終研磨フェーズ。DeltaEvaluator を生スコア源にして直接評価で回す
    /// （copy2D / 全件 check() を毎反復行わない）。op0-2 は単一/二セル直接評価、op3-8 は
    /// findTargetedFix（シャッフル付きフォールバック）、op9-10 は copy 系の destroy/repair を
    /// 変更セルだけ eval へ反映する。受理は hard 非悪化(best基準)＋ SA。
    /// 不変条件: eval.at(i,j) == cur[i][j]。生スコアと weightedScore は目的が異なるため、
    /// 入力(best)を baseline として保持し最後に番兵比較して退化を防ぐ。
    ///
    /// [C#移植上の判断] <see cref="RunAlnsSingle"/> と同じ設計判断: Kotlin原本の
    /// <c>coroutineContext.ensureActive()</c>（throw）＋<c>shouldStop()</c>（非throw）は1つの
    /// 非throwな <c>TimeUp()</c> ポーリングへ統合し、<c>yield()</c>（120反復ごと、ではなくここでは
    /// 150反復ごと）は .NET ThreadPool が協調スケジューリングを要さないため省略する。純粋な同期
    /// アルゴリズムのため <c>async</c> 修飾子は付けない（呼出側の <see cref="SoftPolishOnly"/> が
    /// 必要に応じて <c>Task.Run</c> で包む）。
    /// </summary>
    internal static PolishResult Hf80PostPolish(
        MagiState state,
        int[][] initial,
        int seconds,
        long seed,
        Func<bool>? shouldStop = null,
        CancellationToken cancellationToken = default)
    {
        var stop = shouldStop ?? (() => false);
        bool TimeUp() => stop() || cancellationToken.IsCancellationRequested;

        var started = NowMs();
        var rng = new JavaRandom(seed);
        var p = ScheduleUtil.CachedProblem(state);
        var best = initial.Copy2D();
        var bestReport = UnifiedViolationChecker.Check(state, best);
        // 入力スナップショット（best は改善時に別配列へ差し替わる）。
        var baseSched = best;
        var baseReport = bestReport;
        var iters = 0L;
        var deadline = started + seconds * 1000L;
        // [E10/停滞早期終了, Kotlin原本コメント] 重研磨済み盤面ではプラトー後の期待値が低い。best が
        //   枠の1/5(下限3s)無改善なら早期に返す。keep-best＋末尾の入力比番兵のため品質は不変＝時間/電池
        //   だけ節約。
        var stallMs = Math.Max(3000L, seconds * 1000L / 5);
        var stalled = false;

        var cur = best.Copy2D();
        var eval = new DeltaEvaluator(p);
        eval.Reset(cur);
        var curScore = eval.Score();
        var bestScore = curScore;
        var diffBuf = new int[p.S * p.T];
        // [C#移植上の判断=native経路省略の帰結] Kotlin原本は native 経路が一度も改善しなかった場合
        //   （nat.best==null）だけ lastImproveMs を started から始める。native 経路自体を省略した
        //   ここでは、それは常に成り立つ条件＝常に started。
        var lastImproveMs = started;
        var lastBestMark = bestScore;
        var stallDurationMs = -1L; // 停滞発火の瞬間の無改善経過(ms)。ログ表示のみ。

        while (!TimeUp())
        {
            var nowLoop = NowMs();
            if (nowLoop >= deadline) break;
            if (bestScore < lastBestMark) { lastBestMark = bestScore; lastImproveMs = nowLoop; }
            else if (nowLoop - lastImproveMs >= stallMs) { stalled = true; stallDurationMs = nowLoop - lastImproveMs; break; }

            var curHard = curScore / Evaluator.SCORE_HARD_UNIT;
            var bestHard = bestScore / Evaluator.SCORE_HARD_UNIT;
            var opChoice = rng.NextInt(11);

            if (opChoice == 0)
            {
                // random allowed single cell (direct-eval)
                if (p.S > 0 && p.T > 0)
                {
                    var i = rng.NextInt(p.S);
                    var j = rng.NextInt(p.T);
                    if (!p.WishLocked(i, j))
                    {
                        var allowed = p.AllowedShiftsForStaff(i);
                        if (allowed.Length > 0)
                        {
                            var oldK = eval.At(i, j);
                            var nw = allowed[rng.NextInt(allowed.Length)];
                            if (nw != oldK)
                            {
                                eval.Apply(i, j, nw);
                                var ns = eval.Score();
                                if (ns / Evaluator.SCORE_HARD_UNIT <= bestHard &&
                                    (V6SearchOperators.BetterScore(ns, curScore) || V6SearchOperators.AcceptWorseScore(ns, curScore, 0.15, rng)))
                                {
                                    cur[i][j] = nw;
                                    curScore = ns;
                                    if (V6SearchOperators.BetterScore(ns, bestScore))
                                    { best = cur.Copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.Check(state, cur); }
                                }
                                else eval.Apply(i, j, oldK);
                            }
                        }
                    }
                }
            }
            else if (opChoice == 1)
            {
                // swap two days within one staff row (direct-eval)
                if (p.S > 0 && p.T >= 2)
                {
                    var i = rng.NextInt(p.S);
                    var ja = rng.NextInt(p.T);
                    var jb = rng.NextInt(p.T);
                    if (ja == jb) jb = (jb + 1) % p.T;
                    if (!p.WishLocked(i, ja) && !p.WishLocked(i, jb))
                    {
                        var ka = eval.At(i, ja);
                        var kb = eval.At(i, jb);
                        if (ka != kb)
                        {
                            eval.Apply(i, ja, kb);
                            eval.Apply(i, jb, ka);
                            var ns = eval.Score();
                            if (ns / Evaluator.SCORE_HARD_UNIT <= bestHard &&
                                (V6SearchOperators.BetterScore(ns, curScore) || V6SearchOperators.AcceptWorseScore(ns, curScore, 0.15, rng)))
                            {
                                cur[i][ja] = kb;
                                cur[i][jb] = ka;
                                curScore = ns;
                                if (V6SearchOperators.BetterScore(ns, bestScore))
                                { best = cur.Copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.Check(state, cur); }
                            }
                            else { eval.Apply(i, ja, ka); eval.Apply(i, jb, kb); }
                        }
                    }
                }
            }
            else if (opChoice == 2)
            {
                // swap two staff on same day (direct-eval, coverage-neutral)
                if (p.S >= 2 && p.T > 0)
                {
                    var j = rng.NextInt(p.T);
                    var i1 = rng.NextInt(p.S);
                    var i2 = rng.NextInt(p.S);
                    if (i2 == i1) i2 = (i2 + 1) % p.S;
                    if (!p.WishLocked(i1, j) && !p.WishLocked(i2, j))
                    {
                        var k1 = eval.At(i1, j);
                        var k2 = eval.At(i2, j);
                        if (k1 != k2 && p.CanDo(i1, k2) && p.CanDo(i2, k1))
                        {
                            eval.Apply(i1, j, k2);
                            eval.Apply(i2, j, k1);
                            var ns = eval.Score();
                            if (ns / Evaluator.SCORE_HARD_UNIT <= bestHard &&
                                (V6SearchOperators.BetterScore(ns, curScore) || V6SearchOperators.AcceptWorseScore(ns, curScore, 0.15, rng)))
                            {
                                cur[i1][j] = k2;
                                cur[i2][j] = k1;
                                curScore = ns;
                                if (V6SearchOperators.BetterScore(ns, bestScore))
                                { best = cur.Copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.Check(state, cur); }
                            }
                            else { eval.Apply(i1, j, k1); eval.Apply(i2, j, k2); }
                        }
                    }
                }
            }
            else if (opChoice is >= 3 and <= 8)
            {
                // targeted single-cell fix with shuffled fallback (direct-eval)
                var fix = V6SearchOperators.FindTargetedFix(p, eval, rng);
                if (fix != null)
                {
                    var oldK = eval.At(fix[0], fix[1]);
                    if (fix[2] != oldK)
                    {
                        eval.Apply(fix[0], fix[1], fix[2]);
                        var ns = eval.Score();
                        if (ns / Evaluator.SCORE_HARD_UNIT <= bestHard &&
                            (V6SearchOperators.BetterScore(ns, curScore) || V6SearchOperators.AcceptWorseScore(ns, curScore, 0.15, rng)))
                        {
                            cur[fix[0]][fix[1]] = fix[2];
                            curScore = ns;
                            if (V6SearchOperators.BetterScore(ns, bestScore))
                            { best = cur.Copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.Check(state, cur); }
                        }
                        else eval.Apply(fix[0], fix[1], oldK);
                    }
                }
            }
            else
            {
                // copy-based multi-cell destroy/repair (ops 9,10)
                var cand = cur.Copy2D();
                int drDay2;
                if (rng.NextBoolean())
                {
                    DestroyRepairViolations(state, cand, bestReport, rng);
                    drDay2 = -1;
                }
                else
                {
                    var j = p.T > 0 ? rng.NextInt(p.T) : -1;
                    if (j >= 0) DestroyRepairDayAt(state, cand, j, rng);
                    drDay2 = j;
                }
                // hard-feasible のときは hf67 を省略（DeltaEvaluator が hard 退化を弾く）。
                var repairedCell = curHard > 0L ? Hf67HardRepair(state, cand, rng).Schedule : cand;
                int nDiffs;
                if (drDay2 >= 0 && ReferenceEquals(repairedCell, cand))
                {
                    var n = 0;
                    for (var i = 0; i < p.S; i++) if (cur[i][drDay2] != repairedCell[i][drDay2]) diffBuf[n++] = i * p.T + drDay2;
                    nDiffs = n;
                }
                else nDiffs = V6SearchOperators.DiffInto(p.T, cur, repairedCell, diffBuf);

                for (var idx = 0; idx < nDiffs; idx++)
                {
                    var flat = diffBuf[idx];
                    eval.Apply(flat / p.T, flat % p.T, repairedCell[flat / p.T][flat % p.T]);
                }
                var ns2 = eval.Score();
                if (ns2 / Evaluator.SCORE_HARD_UNIT <= bestHard &&
                    (V6SearchOperators.BetterScore(ns2, curScore) || V6SearchOperators.AcceptWorseScore(ns2, curScore, 0.15, rng)))
                {
                    cur = repairedCell;
                    curScore = ns2;
                    if (V6SearchOperators.BetterScore(ns2, bestScore))
                    { best = repairedCell.Copy2D(); bestScore = ns2; bestReport = UnifiedViolationChecker.Check(state, repairedCell); }
                }
                else
                {
                    for (var idx = 0; idx < nDiffs; idx++)
                    {
                        var flat = diffBuf[idx];
                        eval.Apply(flat / p.T, flat % p.T, cur[flat / p.T][flat % p.T]);
                    }
                }
            }

            iters++;
        }

        // [退化防止, Kotlin原本コメント] 生スコア最良が weightedScore 辞書順で入力より悪い場合は入力へ戻す。
        if (UnifiedViolationChecker.BetterReport(baseReport, bestReport)) { best = baseSched; bestReport = baseReport; }
        var stallNote = stalled ? $"（停滞早期終了 枠{seconds}s・停滞{stallDurationMs}ms無改善）" : "";
        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "HF80", message: $"PostPolish {NowMs() - started}ms HARD={bestReport.Hard} total={bestReport.Total}{stallNote}", iter: iters),
        };
        return new PolishResult(best, logs, iters);
    }
}
