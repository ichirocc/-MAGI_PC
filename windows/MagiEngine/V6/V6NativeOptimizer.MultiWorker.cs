using System.Threading;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>runMultiWorker</c> (phase 5c: "runAlns系/runRsi系"). This is the
/// shared multi-hypothesis coordinator used by <c>runRsi</c>/<c>runRsiPlus</c> (not itself tied to
/// one algorithm — that's why it lives in its own file rather than <c>Alns.cs</c>): given a caller
/// that already knows how to run ONE hypothesis (bound via the <paramref name="run"/> delegate,
/// e.g. a closure over <c>RunV5</c> or <c>RunAlns</c>), it spawns several differently-seeded/
/// differently-profiled hypotheses in parallel and keep-best-selects the winner.
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>
    /// C# translation of Kotlin's higher-order <c>run</c> parameter type:
    /// <c>suspend (Int, V6OptimizerOptions, (String, ViolationReport?, Long, Long) -&gt; Unit) -&gt; V6OptimizerResult</c>.
    /// Deliberately carries NO <see cref="CancellationToken"/> of its own — exactly like the Kotlin
    /// suspend-lambda type, cancellation is expected to be captured by whatever closure the caller
    /// binds here (e.g. <c>(i, opts, prog) =&gt; RunV5(state, initial, opts, budgetSec, shouldStop, prog, cancellationToken)</c>),
    /// not threaded through this delegate's own parameter list. <see cref="RunMultiWorker"/> itself
    /// still takes its own <see cref="CancellationToken"/>, used only for the post-collection
    /// <c>ensureActive()</c>-equivalent check below (distinguishing "a sibling failed and was
    /// locally handled" from "the ambient scope itself was cancelled").
    /// </summary>
    internal delegate Task<V6OptimizerResult> RunOneHypothesis(
        int index, V6OptimizerOptions options, Action<string, ViolationReport?, long, long> onProgress);

    /// <summary>[HypothesisStartMode → 診断ログ用の短いラベル] Kotlin's <c>sp.mode.name.removeSuffix("_REPAIR")</c> — the enum's UPPER_SNAKE Kotlin name minus a trailing "_REPAIR", reproduced by direct mapping since C# enum names are PascalCase without underscores.</summary>
    private static string HypothesisModeLabel(HypothesisStartMode mode) => mode switch
    {
        HypothesisStartMode.DayRepair => "DAY",
        HypothesisStartMode.StaffRepair => "STAFF",
        HypothesisStartMode.MixedRepair => "MIXED",
        _ => "BASELINE",
    };

    /// <summary>
    /// Faithful port of Kotlin's <c>runMultiWorker</c>.
    ///
    /// [C#移植上の判断・eager Task.Run] Kotlin原本は各ジョブを <c>CoroutineStart.LAZY</c> で生成して
    /// から別ループで一斉 <c>start()</c> する。<see cref="RunAlnsChains"/> ではこの二段構成が守っていた
    /// レース（「早期winnerが未生成のジョブをすり抜けてキャンセルを逃れる」）自体が 3.376.0 で
    /// 撤廃済みだったため、生成と同時に実行が始まる <see cref="Task.Run{TResult}(Func{TResult})"/> の
    /// 単一ループへ単純化した。<b>ここでは同じ単純化を適用しつつ、下記の別の最適化は保持する</b>:
    /// 各ジョブ本体の先頭にある「開始時点で既に勝者が確定していれば何もせず抜ける」チェック
    /// （Kotlin: <c>if (winner.get() &gt;= 0 &amp;&amp; winner.get() != i) return@async null</c>）は、
    /// LAZY/eager のどちらでも意味を持つ別物の最適化（スレッドプールが埋まっていれば eager
    /// <c>Task.Run</c> でも本体の実際の実行開始が遅延しうるため）——よって Kotlin 原本にある以上、
    /// そのまま保持する。
    /// </summary>
    internal static async Task<V6OptimizerResult> RunMultiWorker(
        int w,
        V6OptimizerOptions options,
        Action<string, ViolationReport?, long, long>? onProgress,
        RunOneHypothesis run,
        CancellationToken cancellationToken = default)
    {
        // [3.371.0/並列SA本格再有効化, Kotlin原本] spawn数×チェーン内訳は HypothesisSpawnPlan（単一ソース）から。
        var (hSpawn, plan) = HypothesisSpawnPlan(options.EffectiveWorkers, w);
        if (hSpawn <= 1)
        {
            // [レビュー第7弾 2026-09-04] workers=1 経路も多仮説経路と同じく、停止は**例外で**返す。
            //   旧: run の戻り値をそのまま返していたため、停止したのに正常終了扱いで途中盤面が「完了」として
            //   採用され得た（多仮説経路は収集後に ThrowIfCancellationRequested していた＝非対称）。
            var single = await run(0, options with { Workers = plan[0] }, onProgress ?? ((_, _, _, _) => { })).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return single;
        }

        var baseSeed = ActualSeed(options.Seed);
        var completed = 0;
        var winner = -1;
        // [レビュー#2 3.213.0, Kotlin原本] 全ワーカー横断の最良(hard→weighted→total)を追跡し、どの
        //   ワーカーの改善も外側へ転送する（改善時のみ＝非改善レポートを転送すると phase 文字列が
        //   交互に振れ、外側のフェーズ遷移リセットを偽発火させるため）。
        ViolationReport? sharedBest = null;
        bool ImprovesShared(ViolationReport r)
        {
            while (true)
            {
                var cur = Volatile.Read(ref sharedBest);
                if (cur != null && !Better(r, cur)) return false;
                if (Interlocked.CompareExchange(ref sharedBest, r, cur) == cur) return true;
            }
        }
        // [敵対的レビュー修正・#4例外隔離, Kotlin原本] 仮説ごとの try/catch で、1仮説の通常例外が他仮説を
        //   道連れにしない。firstError は全滅時のみ使用。
        Exception? firstError = null;

        var tasks = new Task<V6OptimizerResult?>[hSpawn];
        for (var i = 0; i < hSpawn; i++)
        {
            var localI = i;
            tasks[localI] = Task.Run<V6OptimizerResult?>(async () =>
            {
                // [レビュー第7弾 2026-09-04] 旧: 「開始時点で既に勝者が確定していれば何もせず抜ける」事前チェックが
                //   残っていた。3.376.0 相当で「HARD=0 到達で残りを即キャンセル」を撤廃し winner を記録専用にしたのに、
                //   この1行だけが**仮説の起動を黙って省く**経路として生き残り、スレッドプールの起動順しだいで
                //   仕様（全本継続）と違う本数しか走らなかった。撤去（Android と同時）。
                try
                {
                    // [HF290 役割分担＋論文活用, Kotlin原本] 各仮説に探索/精製プロファイル＋受理基準
                    //   (SA/GD/Lam)＋演算子選択を割当てて多様化（W0=ベースライン）。
                    return await run(localI,
                        options with
                        {
                            Workers = plan[localI],
                            Seed = baseSeed + (localI + 1) * 0x9E3779B1L,
                            Explore = RoleExploreFor(localI),
                            Accept = RoleAcceptFor(localI),
                            OpSelect = RoleOpSelectFor(localI),
                        },
                        (phase, report, iters, elapsed) =>
                        {
                            var improved = report != null && ImprovesShared(report);
                            if (localI == 0 || improved)
                                onProgress?.Invoke(
                                    $"仮説{Math.Max(1, hSpawn - Volatile.Read(ref completed))}本探索中 / {phase}",
                                    report, iters, elapsed);
                            // 絶対評価: 合格ライン(HARD=0)に最初に到達した仮説を記録する。[3.376.0, Kotlin原本]
                            //   「到達で残りを即キャンセル」は撤廃済み＝winner は記録専用（下のログ表記に使う）。
                            if (report != null && report.Hard == 0) Interlocked.CompareExchange(ref winner, localI, -1);
                        }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref firstError, e, null);
                    return null;
                }
                finally
                {
                    Interlocked.Increment(ref completed);
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
        // 兄弟キャンセル(自己)とユーザー停止(外部)を区別: 外部停止ならここで伝播させる。
        cancellationToken.ThrowIfCancellationRequested();

        var best = results.Count == 0
            ? await run(0, options with { Workers = plan[0] }, onProgress ?? ((_, _, _, _) => { })).ConfigureAwait(false)
            : results.Aggregate((a, b) => Better(b.Report, a.Report) ? b : a);

        // 「他の案」: 採用案以外の仮説結果を品質順に保持（重複schedule除外、最大3件）
        var alts = results
            .Where(r => !ReferenceEquals(r, best))
            .OrderBy(r => r.Report, UnifiedViolationChecker.ReportComparer)
            .Select(r => r.Schedule)
            .DistinctBy(sch => string.Join("|", sch.Select(row => string.Join(",", row))))
            .Take(3)
            .ToList();
        var slot = GetRunSlot();
        if (slot != null) slot.Alternatives = alts;                 // [3.335.0] この実行の「他の案」
        if (OwnsStatics(slot)) _lastAlternatives = alts;

        var totalIters = results.Sum(r => r.Iterations);
        var mode = Volatile.Read(ref winner) >= 0 ? "合格あり(全本継続)" : "時間内最良採用";
        var chainNote = plan.Max() > 1
            ? $"・仮説内{plan.Min()}〜{plan.Max()}並列(SA/ALNS多チェーン、設定{options.EffectiveWorkers}がコア数を超えるため仮説数を絞り並列SAへ配分)"
            : "";
        var failNote = results.Count < hSpawn
            ? $"・失敗{hSpawn - results.Count}本(例外/キャンセル{(firstError != null ? $"・{firstError.Message}" : "")})"
            : "";
        // [3.266.0/hypothesis basin diversity, Kotlin原本] 各仮説の入口が実際にどう多様化されたかをログに残す。
        var entryRoles = string.Join(" ", Enumerable.Range(0, hSpawn).Select(i =>
        {
            var sp = HypothesisDiversityPolicy.StartPlanFor(i);
            return $"W{i}={HypothesisModeLabel(sp.Mode)}{(sp.Intensity > 0 ? $"x{sp.Intensity}" : "")}";
        }));
        var hypNote = hSpawn < w ? $"{hSpawn}本(設定仮説数{w}をコア数まで縮小)" : $"{hSpawn}本";
        var extra = new MirrorLog(tag: "MultiWorker",
            message: $"仮説 {hypNote} ({mode}・役割分担:探索/精製＋受理SA/GreatDeluge多様化{chainNote}{failNote}) → 採用 HARD={best.Report.Hard} total={best.Report.Total} 合計iter={totalIters} / 入口役割 {entryRoles}");

        // [過程検証, Kotlin原本] 各仮説の個別結果・多様性（相異なる解の数）・保持した他の案数をログ化し、
        //   探索過程を後から検証できるようにする。
        var perHyp = string.Join("  ", results
            .OrderBy(r => r.Report, UnifiedViolationChecker.ReportComparer)
            .Select(r => $"[必須{r.Report.Hard}/合計{r.Report.Total}{(ReferenceEquals(r, best) ? "★採用" : "")}]"));
        var distinctSols = results
            .Select(r => string.Join("|", r.Schedule.Select(row => string.Join(",", row))))
            .Distinct().Count();
        var pairDistances = new List<int>();
        for (var a = 0; a < results.Count; a++)
            for (var b = a + 1; b < results.Count; b++)
                pairDistances.Add(AdaptiveEliteArchive.ScheduleDistance(results[a].Schedule, results[b].Schedule));
        var distanceNote = pairDistances.Count == 0
            ? "解間距離=対象外"
            : $"解間距離={pairDistances.Min()}..{pairDistances.Max()}セル";
        // [Kotlin原本] 「他の案として保持」件数は上で計算した *この呼出の* alts ではなく、
        //   OwnsStatics 判定を経た static (LastAlternatives) を読む — 所有権を持たない実行では
        //   これは他の(より新しい)実行の値を指しうる。挙動を1:1で保存する。
        var verifyLog = new MirrorLog(tag: "仮説検証",
            message: $"各仮説 {results.Count} 本の結果: {perHyp} / 相異なる解={distinctSols}件 / {distanceNote} / 他の案として保持={LastAlternatives.Count}件");

        return best with { PhaseLogs = best.PhaseLogs.Concat(new[] { extra, verifyLog }).ToList(), Iterations = totalIters };
    }
}
