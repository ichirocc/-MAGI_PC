using System.Threading;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>runV5</c>/<c>runAlnsChains</c>/<c>runAlns</c>/<c>runAlnsSingle</c>
/// (phase 5c: "runAlns系/runRsi系"). Per this port's standing scope decision, <c>magi_native.cpp</c>
/// (the JNI/C++ acceleration mirror) is out of scope; Kotlin's native-JNI ALNS-chunk dispatch block
/// inside <c>runAlnsSingle</c> (<c>nativeProblem</c>/<c>fullEvaluator</c>/<c>bestFlat</c>/
/// <c>runRestartNative</c>/the <c>usedNative</c> gate) is entirely OMITTED, not stubbed — the ported
/// code proceeds directly to what was the Kotlin-fallback (<c>if (!usedNative) { ... }</c>) body, now
/// unconditional. This matches the <see cref="SaOptimizer.RunWorker"/> precedent exactly.
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>[GLS移植, Kotlin原本] 停滞判定の閾値（直近の最良更新からの反復数）。</summary>
    private const long GLS_TRIGGER = 200L;

    /// <summary>[GLS aging, Kotlin原本] この kick 数ごとに penalty を減衰し肥大化を防ぐ。</summary>
    private const int GLS_DECAY_EVERY = 256;

    /// <summary>Faithful port of Kotlin's <c>runV5</c> (parallel SA + hard repair + input-vs-result keep-best sentinel).</summary>
    internal static async Task<V6OptimizerResult> RunV5(
        MagiState state,
        int[][] initial,
        V6OptimizerOptions options,
        int budgetSec,
        Func<bool>? shouldStop = null,
        Action<string, ViolationReport?, long, long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var t0 = NowMs();
        var p = new Problem(state.WithSchedule(initial));
        var ev = new Evaluator(p);
        // [Kotlin原本] `lastReport` はコールバック内で読まれるが、SaOptimizer.Run が完了するまで
        // 再代入されない（このメソッド内の代入は Run の呼出の**後**）＝コールバックからは常に null に
        // 見える。これは Kotlin 原本でも同じ挙動（変数の再代入タイミングがコールバック実行より後）
        // なので、そのまま忠実に移植する（「改善」しない＝HF77の翻訳作業自体への適用）。
        ViolationReport? lastReport = null;
        // [HF290 役割分担] explore 倍率で初期温度を調整（探索=高温/精製=低温）。explore=1.0 は従来と同一。
        var saT0 = Math.Clamp(10.0 * options.Explore, 2.0, 40.0);
        var stop = shouldStop ?? (() => false);
        var res = await new SaOptimizer(p, ev).Run(
            new SaParams(T0: saT0, Workers: ClampWorkersToCores(options.EffectiveWorkers), BudgetMs: budgetSec * 1000L,
                SoftPolish: options.SoftPolish, ShouldStop: stop, Seed: options.Seed),
            pr =>
            {
                if (pr.ElapsedMs % 1000L < 220L) onProgress?.Invoke("V5 SA", lastReport, pr.TotalIters, pr.ElapsedMs);
            },
            cancellationToken).ConfigureAwait(false);
        var repaired = Hf67HardRepair(state, res.Schedule, new JavaRandom(ActualSeed(options.Seed) ^ 0x5L));
        var outSched = repaired.Schedule;
        var report = UnifiedViolationChecker.Check(state, outSched);
        // [退化防止番兵 / 実機ログ起因, Kotlin原本コメント] runAlns と同じ入力比keep-best。従来 runV5 だけ
        //   番兵が無く、SA+修復が入力より悪化した結果をそのまま返していた。RSI++ は Phase1 Seed に runV5
        //   を使い、以降の各段は前段比 keep-best のため、Phase1 の劣化が全チェーンへ伝播し、最後に
        //   ディスパッチャ番兵が入力へ復帰＝予算全体が無駄になっていた。入力を品質床にすることで以降の
        //   全フェーズが「入力以上」から積み上がる。SA が入力より良い解を見つけた場合は素通し＝多様化は
        //   維持。スコアリング不変(選択のみ・better()=hard→weighted→total)。
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);
        var keptInput = UnifiedViolationChecker.BetterReport(baseReport, report);
        if (keptInput) { outSched = baseSched; report = baseReport; }
        lastReport = report;

        var chainNote = "";
        if (res.ChainWins.Length > 0)
        {
            var wins = res.ChainWins.Count(w => w > 0);
            chainNote = $" SAチェーン{res.ChainWins.Length}本(最良を更新した本数={wins}" +
                (wins <= 1 && res.ChainWins.Length > 1 ? "＝並列を増やした効果は出ていません" : "") + ")";
        }
        var message = $"高速SA完了 HARD={report.Hard} total={report.Total} iter={res.TotalIters}" + chainNote +
            (keptInput ? "（SA結果が入力より悪化のため入力を維持=番兵）" : "");
        var logs = new List<MirrorLog> { new MirrorLog(tag: "RunMAGI_V5", message: message) };
        logs.AddRange(repaired.Logs);
        return new V6OptimizerResult(
            outSched,
            report with { Logs = logs.Concat(report.Logs).ToList() },
            V6Algorithm.V5, logs, res.TotalIters, NowMs() - t0);
    }

    /// <summary>
    /// [余剰ワーカー活用/多チェーンALNS, Kotlin原本] runAlns を chains 本、異なるシードで並列実行し
    /// keep-best で最良を採用する（SaOptimizer の多チェーンSAと同型の考え方をALNSへ拡張）。各チェーンは
    /// <see cref="RunAlnsSingle"/>（単一チェーン本体）を直接呼ぶ＝再帰は構造的に不可能
    /// （[敵対的レビュー3.212.0, Kotlin原本コメント] 旧実装は runAlns 経由のガード再帰で、無限再帰防止が
    /// options.copy(workers=1) 1引数とコメントのみに依存していた）。restarts・GLS・destroy-repair 等の
    /// 内部ロジックは一切変更しない。最終選択は全チェーン共通の betterReport（hard→weighted→total辞書式）
    /// でゲートするため退化不能。
    ///
    /// [3.410.0/E-03, Kotlin原本コメント] 旧: HARD=0 へ最初に到達したチェーンが兄弟を即キャンセルして
    /// いた。3.376.0 が runAdaptivePortfolio/runMultiWorker の同じ機構を撤廃したとき、この3つ目だけが
    /// 取り残されていた。HARD=0 到達時点で残る仕事は全部 SOFT なので、勝者1本に絞ると指定した並列度の
    /// 1/N しか使われない。採否は全段 keep-best なので走らせ続けても品質は退化しない。<c>passed</c> は
    /// 「誰が最初に到達したか」の記録としてのみ残す（その報告を一度だけ外側へ転送する）。
    ///
    /// [C#移植上の判断] Kotlin原本は各ジョブを <c>CoroutineStart.LAZY</c> で生成してから別ループで一斉
    /// <c>start()</c> する（「早期winnerが未生成のジョブをすり抜けてキャンセルを逃れる」レースへの防御
    /// だった、と原本コメントに明記）。上記のとおりそのレース自体（早期キャンセル）は既に撤廃済みなので、
    /// この二段構成は現在は名目上の名残りに過ぎない。<see cref="Task.Run{TResult}(Func{TResult})"/> は
    /// 生成と同時に実行を開始するため、単一ループでの生成＝即開始へ単純化した（結果は同一 — 開始を
    /// 遅らせて得られていた安全性は、その安全性自体が既に不要になっているため失われない）。
    /// </summary>
    internal static async Task<V6OptimizerResult> RunAlnsChains(
        MagiState state,
        int[][] initial,
        V6OptimizerOptions options,
        int budgetSec,
        Func<bool>? shouldStop,
        Action<string, ViolationReport?, long, long>? onProgress,
        CancellationToken cancellationToken = default)
    {
        var chains = Math.Max(1, options.EffectiveWorkers);
        var baseSeed = ActualSeed(options.Seed);
        var passed = -1;
        Exception? firstError = null;
        ViolationReport? sharedBest = null;

        // [レビュー#2 3.213.0, Kotlin原本コメント] RunMultiWorker と同型: チェーン横断の改善も外側へ
        //   転送（停滞時計の集約）。
        bool ImprovesShared(ViolationReport r)
        {
            while (true)
            {
                var cur = Volatile.Read(ref sharedBest);
                if (cur != null && !UnifiedViolationChecker.BetterReport(r, cur)) return false;
                if (Interlocked.CompareExchange(ref sharedBest, r, cur) == cur) return true;
            }
        }

        var tasks = new Task<V6OptimizerResult?>[chains];
        for (var c = 0; c < chains; c++)
        {
            var localC = c;
            tasks[localC] = Task.Run<V6OptimizerResult?>(() =>
            {
                try
                {
                    return RunAlnsSingle(
                        state, initial.Copy2D(),
                        options with { Workers = 1, Seed = baseSeed + (localC + 1) * 0x2545F4914F6CDD1DL },
                        budgetSec, shouldStop,
                        (phase, report, iters, elapsed) =>
                        {
                            var won = report != null && report.Hard == 0 && Interlocked.CompareExchange(ref passed, localC, -1) == -1;
                            // 先頭チェーンは常時、非先頭は合格時＋チェーン横断改善時に転送
                            //（合格の可視化が絶対評価に、改善の可視化が外側停滞時計に必要）。
                            var improved = report != null && ImprovesShared(report);
                            if (localC == 0 || won || improved) onProgress?.Invoke(phase, report, iters, elapsed);
                        },
                        cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref firstError, e, null);
                    return null;
                }
            });
        }

        var results = new List<V6OptimizerResult>();
        foreach (var t in tasks)
        {
            try
            {
                var r = await t.ConfigureAwait(false);
                if (r != null) results.Add(r);
            }
            catch (OperationCanceledException) { /* skip — matches Kotlin's mapNotNull-with-catch */ }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (results.Count == 0)
        {
            // 全チェーン失敗（キャンセル起因は上の ThrowIfCancellationRequested で伝播済＝ここは例外全滅のみ）。
            // 旧単一チェーンと同じ失敗面へ縮退: 最初の例外を再送出（黙って空成功にしない）。
            throw firstError ?? new InvalidOperationException("RunAlnsChains: no chain produced a result");
        }

        var best = results.Aggregate((a, b) => UnifiedViolationChecker.BetterReport(b.Report, a.Report) ? b : a);
        var totalIters = results.Sum(r => r.Iterations);
        var chain0Iters = results.Count > 0 ? results[0].Iterations : 0L;
        var perChain = string.Join("  ", results
            .OrderBy(r => r.Report, UnifiedViolationChecker.ReportComparer)
            .Select(r => $"[必須{r.Report.Hard}/合計{r.Report.Total}{(ReferenceEquals(r, best) ? "★採用" : "")}]"));
        var distinctSols = results
            .Select(r => string.Join("|", r.Schedule.Select(row => string.Join(",", row))))
            .Distinct().Count();
        var failNote = results.Count < chains ? $"・失敗{chains - results.Count}本(例外/キャンセル)" : "";
        var extra = new MirrorLog(tag: "AlnsChains",
            message: $"ALNS多チェーン({chains}並列{failNote}) → 採用 HARD={best.Report.Hard} total={best.Report.Total}" +
                $" 合計iter={totalIters}(先頭chain={chain0Iters}) / 各チェーン: {perChain} / 相異なる解={distinctSols}件");
        return best with { PhaseLogs = best.PhaseLogs.Concat(new[] { extra }).ToList(), Iterations = totalIters };
    }

    /// <summary>
    /// [敵対的レビュー3.212.0/構造分割, Kotlin原本コメント] workers の意味過重（設定値/仮説内チェーン数/
    /// チェーン内=1）をディスパッチャ3行に閉じ込める。本体 RunAlnsSingle は workers を一切読まない＝
    /// 再帰・誤fan-outが構造的に不可能。既存呼出元のシグネチャは不変。
    /// </summary>
    internal static async Task<V6OptimizerResult> RunAlns(
        MagiState state,
        int[][] initial,
        V6OptimizerOptions options,
        int budgetSec,
        Func<bool>? shouldStop = null,
        Action<string, ViolationReport?, long, long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (options.EffectiveWorkers > 1)
            return await RunAlnsChains(state, initial, options, budgetSec, shouldStop, onProgress, cancellationToken)
                .ConfigureAwait(false);
        return RunAlnsSingle(state, initial, options, budgetSec, shouldStop, onProgress, cancellationToken);
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>runAlnsSingle</c> — the single-chain ALNS body (destroy/repair,
    /// GLS-guided acceptance, adaptive operator weights). This is the algorithmic core that both
    /// <see cref="RunAlns"/> (directly, when <c>workers&lt;=1</c>) and <see cref="RunAlnsChains"/>
    /// (wrapped in <see cref="Task.Run{TResult}(Func{TResult})"/>, for <c>workers&gt;1</c>) invoke.
    ///
    /// [C#移植上の判断] Kotlin原本の <c>coroutineContext.ensureActive()</c>（構造化並行性からの協調
    /// キャンセル検出、throw する）と <c>shouldStop()</c>（明示的な外部停止コールバック、非throw）という
    /// 2つの独立した仕組みは、<see cref="SaOptimizer.RunWorker"/> と同じ設計判断で**1つの非throwチェック
    /// へ統合**した（<c>TimeUp()</c> が両方を非throwな真偽判定として扱う）。この統合によりこのメソッド
    /// 内では例外によるキャンセルが一切発生しない（アルゴリズムは必ず自分のループを正常終了し、その
    /// 時点の最良解を返す）。あわせて、このメソッドは実際の <c>await</c> を一切必要としない純粋な同期
    /// アルゴリズムであるため（<see cref="RunWorker"/> と同じ理由）、<c>async</c> 修飾子は付けず
    /// <see cref="V6OptimizerResult"/> を直接返す（呼出側が <see cref="Task.Run{TResult}(Func{TResult})"/>
    /// で包む場合に真の並列実行が得られ、直接呼出（<see cref="RunAlns"/> の workers&lt;=1 経路）では
    /// 呼出スレッド上でそのまま完了する）。Kotlin原本の <c>yield()</c>（進捗更新120反復ごと）は、C#の
    /// スレッドプール上の専用ワーカースレッドではコルーチンディスパッチャのような協調スケジューリングの
    /// 必要が無いため（.NET ThreadPool 自身がCPUバウンドな作業をスケジュールする）省略した。
    /// </summary>
    internal static V6OptimizerResult RunAlnsSingle(
        MagiState state,
        int[][] initial,
        V6OptimizerOptions options,
        int budgetSec,
        Func<bool>? shouldStop = null,
        Action<string, ViolationReport?, long, long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var stop = shouldStop ?? (() => false);
        bool TimeUp() => stop() || cancellationToken.IsCancellationRequested;

        var started = NowMs();
        var rng = new JavaRandom(ActualSeed(options.Seed) ^ 0xA17A5L);
        var p = ScheduleUtil.CachedProblem(state);
        var restarts = Math.Max(1, options.Restarts);
        var per = Math.Max(1, budgetSec / restarts);
        var globalBest = ScheduleUtil.NormalizeSchedule(initial, p);
        var globalReport = UnifiedViolationChecker.Check(state, globalBest);
        // [退化防止, Kotlin原本コメント] hot-loop は生スコア(DeltaEvaluator)で最良を追うが、生スコアと
        //   weightedScore は目的が異なる。最終結果が入力(best)より hard→weighted→total の辞書順で
        //   悪化しないよう、開始時の盤面を baseline として保持し最後に番兵比較する。
        var baseBest = globalBest.Copy2D();
        var baseReport = globalReport;
        var itersTotal = 0L;
        var logs = new List<MirrorLog>();
        // [GLS移植, Kotlin原本コメント] Guided Local Search: 受理(accept-worse)を penalty で誘導し
        //   局所最適から脱出。グローバル最良は生スコアで別管理するので、GLSで真の最良を失うことはない。
        var gls = new GlsPenalty(p.S, p.T, p.K);
        var lastImproveIter = 0L;
        var eval = new DeltaEvaluator(p);
        eval.Reset(globalBest);
        var globalScore = eval.Score();
        // [高速化/零アロケ, Kotlin原本コメント] op0-2(copy系)は毎反復コピーを新規確保していた。
        //   使い回しのスクラッチ盤面へ arraycopy し、採用時は cur とスワップする。
        var scratchBuf = new int[p.S][];
        for (var si = 0; si < p.S; si++) scratchBuf[si] = new int[p.T];
        var diffBuf = new int[p.S * p.T];

        for (var r = 0; r < restarts; r++)
        {
            if (TimeUp()) break;
            // [restart 摂動, Kotlin原本コメント] 一律 strength=0.18（3.310.0/2.51 の非線形スケジュールは
            //   nsp_bench --real の final 品質で +101% 悪化と実測されたため revert 済み）。
            var cur = r == 0 ? globalBest.Copy2D() : Perturb(state, globalBest, rng, Math.Clamp(0.18 * options.Explore, 0.05, 0.6));
            cur = Hf67HardRepair(state, cur, rng).Schedule;
            var deadline = NowMs() + per * 1000L;

            var curReport = UnifiedViolationChecker.Check(state, cur);
            eval.Reset(cur);
            var curScore = eval.Score();
            var curAug = gls.Augment(cur);
            // [論文活用, Kotlin原本コメント] Great Deluge の初期水位＝このリスタート開始時のスコア。
            var gdInitial = (double)curScore;
            var iter = 0L;
            // [Adaptive LNS, Kotlin原本コメント] learned operator weights (roulette-wheel selection +
            //   reaction-factor update)。均一演算子選択を置き換える。
            var opW = new double[7];
            for (var oi = 0; oi < opW.Length; oi++) opW[oi] = 1.0;
            var opScore = new double[7];
            var opCnt = new int[7];
            var sinceUpdate = 0;
            // [Lam適応冷却, Kotlin原本コメント] W3 (accept==LAM_ADAPTIVE) のみ使用。観測受理率 lamAcc を
            //   Lam-Delosme の目標受理率に追従させ、温度 lamTemp を乗算的に自己調整する。
            var lamTemp = Math.Max(1.0, options.Explore);
            var lamAcc = 0.44;
            void LamUpdate(bool accepted)
            {
                lamAcc = 0.97 * lamAcc + 0.03 * (accepted ? 1.0 : 0.0);
                var f = Math.Clamp((double)(deadline - NowMs()) / Math.Max(1.0, per * 1000.0), 0.0, 1.0);
                var target = f > 0.85 ? 0.44 : f > 0.15 ? 0.44 * (f - 0.15) / 0.70 : 0.0;
                lamTemp = Math.Clamp(lamTemp * (lamAcc > target ? 0.998 : 1.002), 0.03, 4.0);
            }

            while (NowMs() < deadline && !TimeUp())
            {
                var op = options.OpSelect == OpSelectMode.Thompson ? ThompsonSelect(opW, iter, rng) : RouletteSelect(opW, rng);
                // [賢いsoft集中, Kotlin原本コメント] HARD が最良水準に到達したら残り探索を soft 修復へ寄せる。
                var softFocusProb = globalScore / Evaluator.SCORE_HARD_UNIT == 0L ? 0.30 : 0.15;
                if (curScore / Evaluator.SCORE_HARD_UNIT <= globalScore / Evaluator.SCORE_HARD_UNIT && rng.NextDouble() < softFocusProb) op = 5;
                // [HF290 役割分担, Kotlin原本コメント] explore 倍率で受理温度を調整。LAM_ADAPTIVE は
                //   受理率追従の適応温度 lamTemp を使う。
                var temp = options.Accept == AcceptMode.LamAdaptive
                    ? lamTemp
                    : Math.Max(0.03, (double)(deadline - NowMs()) / Math.Max(1.0, per * 1000.0) * options.Explore);
                var curHard = curScore / Evaluator.SCORE_HARD_UNIT;
                var gdLevel = options.Accept == AcceptMode.GreatDeluge
                    ? GreatDelugeLevel(gdInitial, globalScore, Math.Clamp((double)(deadline - NowMs()) / Math.Max(1.0, per * 1000.0), 0.0, 1.0))
                    : 0.0;
                var reward = 0.2; // default: rejected / no-op

                if (op is >= 3 and <= 6)
                {
                    // ── 直接評価パス(op3-6): copy2D なし。eval+cur に直接適用し、不採択は反転 ──
                    var moved = false;
                    var ns = curScore;
                    var moveAug = 0.0;
                    var c0i = -1; var c0j = -1; var c0old = -1;
                    var c1i = -1; var c1j = -1; var c1old = -1;

                    if (op == 3 && p.S > 0 && p.T >= 2)
                    {
                        // 同一職員の2日入替
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
                                c0i = i; c0j = ja; c0old = ka; c1i = i; c1j = jb; c1old = kb;
                                moveAug = V6SearchOperators.GlsMoveAug(gls, i, ja, ka, kb) + V6SearchOperators.GlsMoveAug(gls, i, jb, kb, ka);
                                ns = eval.Score(); moved = true;
                            }
                        }
                    }
                    else if (op == 4 && p.S > 0 && p.T > 0)
                    {
                        // randomAllowedCell
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
                                    c0i = i; c0j = j; c0old = oldK;
                                    moveAug = V6SearchOperators.GlsMoveAug(gls, i, j, oldK, nw);
                                    ns = eval.Score(); moved = true;
                                }
                            }
                        }
                    }
                    else if (op == 5)
                    {
                        // targeted single-cell repair (direct-eval)
                        var fix = V6SearchOperators.FindTargetedFix(p, eval, rng);
                        if (fix != null)
                        {
                            var oldK = eval.At(fix[0], fix[1]);
                            if (fix[2] != oldK)
                            {
                                eval.Apply(fix[0], fix[1], fix[2]);
                                c0i = fix[0]; c0j = fix[1]; c0old = oldK;
                                moveAug = V6SearchOperators.GlsMoveAug(gls, fix[0], fix[1], oldK, fix[2]);
                                ns = eval.Score(); moved = true;
                            }
                        }
                    }
                    else if (op == 6 && p.S >= 2 && p.T > 0)
                    {
                        // swapTwoStaffSameDay (coverage-neutral)
                        var j = rng.NextInt(p.T);
                        var i1 = rng.NextInt(p.S);
                        var i2 = rng.NextInt(p.S);
                        if (i2 == i1) i2 = (i2 + 1) % p.S;
                        if (!p.WishLocked(i1, j) && !p.WishLocked(i2, j))
                        {
                            var k1 = eval.At(i1, j);
                            var k2 = eval.At(i2, j);
                            if (k1 != k2 && p.MayPlace(i1, k2) && p.MayPlace(i2, k1))
                            {
                                eval.Apply(i1, j, k2);
                                eval.Apply(i2, j, k1);
                                c0i = i1; c0j = j; c0old = k1; c1i = i2; c1j = j; c1old = k2;
                                moveAug = V6SearchOperators.GlsMoveAug(gls, i1, j, k1, k2) + V6SearchOperators.GlsMoveAug(gls, i2, j, k2, k1);
                                ns = eval.Score(); moved = true;
                            }
                        }
                    }

                    if (moved)
                    {
                        var improvedCur = ns < curScore;
                        var accepted = improvedCur || V6SearchOperators.GlsAccept(ns, curScore, moveAug, curAug, options.Accept, temp, gdLevel, rng);
                        if (options.Accept == AcceptMode.LamAdaptive) LamUpdate(accepted);
                        if (accepted)
                        {
                            cur[c0i][c0j] = eval.At(c0i, c0j);
                            if (c1i >= 0) cur[c1i][c1j] = eval.At(c1i, c1j);
                            curScore = ns; curAug += moveAug;
                            if (ns < globalScore)
                            {
                                globalBest = cur.Copy2D(); globalScore = ns;
                                globalReport = UnifiedViolationChecker.Check(state, cur);
                                lastImproveIter = itersTotal;
                                reward = 4.0;
                            }
                            else reward = improvedCur ? 2.0 : 1.0;
                        }
                        else
                        {
                            if (c1i >= 0) eval.Apply(c1i, c1j, c1old); // revert eval; cur was never mutated
                            eval.Apply(c0i, c0j, c0old);
                        }
                        opScore[op] += reward; opCnt[op]++;
                    }
                }
                else
                {
                    // ── copy系パス(op0-2): 変更セルだけ eval へ反映（targeted O(S)/O(T) 差分） ──
                    var cand = scratchBuf;
                    for (var i2 = 0; i2 < p.S; i2++) Array.Copy(cur[i2], cand[i2], p.T);
                    var drDay = op == 0 && p.T > 0 ? rng.NextInt(p.T) : -1;
                    var drStaff = op == 1 && p.S > 0 ? rng.NextInt(p.S) : -1;
                    switch (op)
                    {
                        case 0: if (drDay >= 0) DestroyRepairDayAt(state, cand, drDay, rng); break;
                        case 1: if (drStaff >= 0) DestroyRepairStaffAt(state, cand, drStaff, rng); break;
                        default: DestroyRepairViolations(state, cand, curReport, rng); break;
                    }
                    // hf67 は hard 違反がある時のみ必要。
                    var repairedCell = iter % 7L == 0L && curHard > 0L ? Hf67HardRepair(state, cand, rng).Schedule : cand;
                    int nDiffs;
                    if (op == 0 && drDay >= 0 && ReferenceEquals(repairedCell, cand))
                    {
                        var n = 0;
                        for (var i = 0; i < p.S; i++) if (cur[i][drDay] != repairedCell[i][drDay]) diffBuf[n++] = i * p.T + drDay;
                        nDiffs = n;
                    }
                    else if (op == 1 && drStaff >= 0 && ReferenceEquals(repairedCell, cand))
                    {
                        var n = 0;
                        var row = repairedCell[drStaff];
                        var curRow = cur[drStaff];
                        for (var j = 0; j < p.T; j++) if (curRow[j] != row[j]) diffBuf[n++] = drStaff * p.T + j;
                        nDiffs = n;
                    }
                    else
                    {
                        nDiffs = V6SearchOperators.DiffInto(p.T, cur, repairedCell, diffBuf);
                    }
                    var moveAug = 0.0;
                    for (var idx = 0; idx < nDiffs; idx++)
                    {
                        var flat = diffBuf[idx];
                        var i = flat / p.T;
                        var j = flat % p.T;
                        moveAug += V6SearchOperators.GlsMoveAug(gls, i, j, cur[i][j], repairedCell[i][j]);
                        eval.Apply(i, j, repairedCell[i][j]);
                    }
                    var ns = eval.Score();
                    var improvedCur = ns < curScore;
                    var accepted = improvedCur || V6SearchOperators.GlsAccept(ns, curScore, moveAug, curAug, options.Accept, temp, gdLevel, rng);
                    if (options.Accept == AcceptMode.LamAdaptive) LamUpdate(accepted);
                    if (accepted)
                    {
                        // [零アロケ, Kotlin原本コメント] スクラッチ採用時は cur とスワップ（旧 cur を次のスクラッチへ）。
                        if (ReferenceEquals(repairedCell, scratchBuf)) { var t = cur; cur = repairedCell; scratchBuf = t; }
                        else cur = repairedCell;
                        curScore = ns; curAug += moveAug;
                        if (ns < globalScore)
                        {
                            globalBest = repairedCell.Copy2D(); globalScore = ns;
                            globalReport = UnifiedViolationChecker.Check(state, repairedCell);
                            lastImproveIter = itersTotal;
                            reward = 4.0;
                        }
                        else reward = improvedCur ? 2.0 : 1.0;
                    }
                    else
                    {
                        for (var idx = 0; idx < nDiffs; idx++)
                        {
                            var flat = diffBuf[idx];
                            eval.Apply(flat / p.T, flat % p.T, cur[flat / p.T][flat % p.T]);
                        }
                    }
                    opScore[op] += reward; opCnt[op]++;
                }

                // [GLS, Kotlin原本コメント] 停滞時(直近の最良更新から GLS_TRIGGER 反復超)に、違反セルの
                //   最大util割当を強化。
                if (itersTotal - lastImproveIter > GLS_TRIGGER && iter % 50L == 0L)
                {
                    var cells = new List<(int I, int J)>(curReport.Violations.Count);
                    foreach (var vkey in curReport.Violations.Keys)
                    {
                        var parts = vkey.Split(',');
                        var ci = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
                        var cj = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
                        if (ci is int civ && cj is int cjv) cells.Add((civ, cjv));
                    }
                    if (gls.PenalizeWorst(cur, cells))
                    {
                        curAug += gls.Lambda; // penalized a current cell -> augment(cur) += lambda
                        // [GLS aging, Kotlin原本コメント] 一定 kick ごとに penalty を減衰し肥大化を防ぐ。
                        //   penalty集合が変わるので curAug を augment(cur) で再同期。
                        if (gls.KickCount() % GLS_DECAY_EVERY == 0) { gls.Decay(); curAug = gls.Augment(cur); }
                    }
                }
                // destroyRepairViolations 用に curReport を周期更新（hint の鮮度確保）。
                if (iter % 200L == 0L) curReport = UnifiedViolationChecker.Check(state, cur);
                if (++sinceUpdate >= 64)
                {
                    for (var k = 0; k < opW.Length; k++)
                    {
                        if (opCnt[k] > 0) opW[k] = Math.Max(0.05, 0.8 * opW[k] + 0.2 * (opScore[k] / opCnt[k]));
                        opScore[k] = 0.0; opCnt[k] = 0;
                    }
                    sinceUpdate = 0;
                }
                iter++;
                itersTotal++;
                if (iter % 120L == 0L)
                {
                    // [3.335.0, Kotlin原本コメント] 置き換えられた古い実行が新しい実行のライブ盤面を
                    //   上書きしないようにする。
                    if (OwnsStatics(GetRunSlot())) PublishLiveBest(globalReport, globalBest);
                    onProgress?.Invoke($"ALNS restart {r + 1}/{restarts}", globalReport, itersTotal, NowMs() - started);
                }
            }

            logs.Add(new MirrorLog(iter: itersTotal, tag: "RunMAGI_ALNS",
                message: $"restart={r + 1}/{restarts} best HARD={globalReport.Hard} total={globalReport.Total} GLS={gls.KickCount()}"));
        }

        // [退化防止, Kotlin原本コメント] 生スコア最良が weightedScore 辞書順では入力より悪い可能性が
        //   あるため番兵で保証。
        if (UnifiedViolationChecker.BetterReport(baseReport, globalReport)) { globalBest = baseBest; globalReport = baseReport; }
        return new V6OptimizerResult(
            globalBest,
            globalReport with { Logs = logs.Concat(globalReport.Logs).ToList() },
            V6Algorithm.Alns, logs, itersTotal, NowMs() - started);
    }
}
