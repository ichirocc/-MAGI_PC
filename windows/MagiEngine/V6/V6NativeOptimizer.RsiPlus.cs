using System.Threading;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>runRsiPlus</c> — this closes out phase 5c ("runAlns系/runRsi系").
/// RSI++ is a fixed 4-phase sequential pipeline composed entirely from already-ported building
/// blocks: Seed (<see cref="RunV5"/>) → Hypothesis (<see cref="RunRsi"/>, budget-skippable) →
/// Refine (<see cref="RunAlns"/>, budget-skippable) → an inline "EarlyChain" hook
/// (<see cref="V6LateOperators.Improve"/>, gated by <see cref="Better"/>) → Polish
/// (<see cref="Hf80PostPolish"/>). Every phase-to-phase promotion decision uses
/// <see cref="Better"/> (hard→weightedScore→total) — no new acceptance logic is introduced here.
///
/// [C#移植上の判断・可視性] Kotlin 原本の <c>runRsiPlus</c>（<c>private suspend fun</c>）も、
/// <c>runRsi</c> で確立済みの前例（<c>InternalsVisibleTo("MagiEngine.Tests")</c> 経由での直接単体
/// テスト）にそのまま倣い <c>internal static</c> へ格上げする。
///
/// [C#移植上の判断・CancellationToken] Kotlin 原本の <c>runRsiPlus</c> 自身は
/// <c>coroutineContext.ensureActive()</c> を一度も直接呼ばない——構造的キャンセルは全て
/// <c>runV5</c>/<c>runRsi</c>/<c>runAlns</c> への suspend 呼出（アンビエントな coroutine context
/// 経由）にのみ依存する。C# 側もこれをそのまま反映し、<see cref="RunRsiPlus"/> 自身は
/// <c>cancellationToken</c> を一度も自前でチェックせず、各子呼出（<see cref="RunV5"/>/
/// <see cref="RunRsi"/>/<see cref="RunAlns"/>/<see cref="Hf80PostPolish"/>）へそのまま伝播するだけに
/// 留める（Kotlin に無い明示パラメータだが、既に <c>RunRsi</c> 自身が確立した「flavor 3」の踏襲——
/// 個々の子呼出のキャンセル処理へ委ねる、という設計をここでも一段上で繰り返しているだけ）。
///
/// これにより、事前キャンセル済みトークンを渡した場合の挙動は Kotlin 原本の構造と一致する：
/// Phase1 Seed（<see cref="RunV5"/>→内部の <see cref="SaOptimizer"/> は「flavor 1」＝非スロー）は
/// 通常どおり完了してから、Phase2 Hypothesis（<see cref="RunRsi"/>、既に「flavor 3」で
/// ラウンド境界にて明示的にスローする）で初めて <see cref="System.OperationCanceledException"/> が
/// 投げられる——<see cref="RunRsi"/> 単体（ラウンド0で即スロー）とは異なり、<see cref="RunRsiPlus"/>
/// は Phase1 の実作業を必ず1回終えてから投げる、という点に注意。
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>Faithful port of Kotlin's <c>runRsiPlus</c>.</summary>
    internal static async Task<V6OptimizerResult> RunRsiPlus(
        MagiState state,
        int[][] initial,
        V6OptimizerOptions options,
        int budgetSec,
        Func<bool>? shouldStop = null,
        Action<string, ViolationReport?, long, long>? onProgress = null,
        Hf63Infeasibility? sharedHf63 = null,
        CancellationToken cancellationToken = default)
    {
        var started = NowMs();
        var stop = shouldStop ?? (() => false);
        var seedSec = Math.Max(10, (int)(budgetSec * 0.20));
        var rsiSec = Math.Max(10, (int)(budgetSec * 0.35));
        var alnsSec = Math.Max(10, (int)(budgetSec * 0.30));
        var polishSec = Math.Max(5, budgetSec - seedSec - rsiSec - alnsSec);
        var logs = new List<MirrorLog>();

        var seed = await RunV5(state, initial, options, seedSec, stop, onProgress, cancellationToken).ConfigureAwait(false);
        logs.Add(new MirrorLog(tag: "RSIPlus", message: $"Phase1 Seed: HARD={seed.Report.Hard} total={seed.Report.Total}"));

        var rsi = stop()
            ? seed
            : await RunRsi(state, seed.Schedule, options, rsiSec, stop, onProgress, sharedHf63, cancellationToken).ConfigureAwait(false);
        var baseResult = Better(rsi.Report, seed.Report) ? rsi : seed;
        logs.Add(new MirrorLog(tag: "RSIPlus", message: $"Phase2 Hypothesis: HARD={baseResult.Report.Hard} total={baseResult.Report.Total}"));

        var refine = stop()
            ? baseResult
            : await RunAlns(state, baseResult.Schedule, options with { Restarts = Math.Max(1, options.Restarts) }, alnsSec, stop, onProgress, cancellationToken).ConfigureAwait(false);
        var best = Better(refine.Report, baseResult.Report) ? refine : baseResult;
        var bestSched = best.Schedule;

        // [HF361/528/541移植, Kotlin原本] EarlyChain: Refine 確定後の停滞境界で Chain3/4(常時)+Rect/BlkN(rectSwap)を発火
        {
            var lr = V6LateOperators.Improve(state, bestSched, best.Report,
                new JavaRandom(ActualSeed(options.Seed) ^ 0x528L), started + budgetSec * 1000L, rectEnabled: options.RectSwap);
            var fired = lr.Chain3 + lr.Chain4 + lr.Rect + lr.BlkN > 0;
            // [監査#1, Kotlin原本コメント] Chain3/4の受理(gateW)はweighted単層でHARD増を相殺受理し得るため、
            //   採用は runRsi と同じ Better（hard→weighted→total）でゲートする（素通しでHARD悪化を最終出力しない）。
            if (fired)
            {
                if (Better(lr.Report, best.Report))
                {
                    bestSched = lr.Schedule;
                    logs.Add(new MirrorLog(tag: "EarlyChain",
                        message: $"早期循環フック改善 (Chain3={lr.Chain3} Chain4={lr.Chain4} Rect={lr.Rect} BlkN={lr.BlkN}) HARD={lr.Report.Hard} total={lr.Report.Total}"));
                    logs.AddRange(lr.Logs);
                }
                else
                {
                    logs.Add(new MirrorLog(tag: "EarlyChain",
                        message: $"採用見送り（hard/total非改善ガード） HARD={lr.Report.Hard} total={lr.Report.Total}"));
                }
            }
        }

        var polish = Hf80PostPolish(state, bestSched, polishSec, ActualSeed(options.Seed) ^ 0x555L, stop, cancellationToken);
        var report = UnifiedViolationChecker.Check(state, polish.Schedule);
        logs.Add(new MirrorLog(tag: "RSIPlus", message: $"Phase3/4 Refine+Polish: HARD={report.Hard} total={report.Total}"));

        return new V6OptimizerResult(
            polish.Schedule,
            report with
            {
                Logs = logs.Concat(seed.PhaseLogs).Concat(rsi.PhaseLogs).Concat(refine.PhaseLogs).Concat(polish.Logs).Concat(report.Logs).ToList()
            },
            V6Algorithm.RsiPlus,
            logs.Concat(seed.PhaseLogs).Concat(rsi.PhaseLogs).Concat(refine.PhaseLogs).Concat(polish.Logs).ToList(),
            seed.Iterations + rsi.Iterations + refine.Iterations + polish.Iterations,
            NowMs() - started);
    }
}
