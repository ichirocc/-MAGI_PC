using System.Threading;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Phase 5e (final piece, closing out phase 5 entirely): the top-level dispatcher
/// (Kotlin's <c>optimize()</c>/<c>optimizeInSlot()</c>) that <c>V6FinalPort.HandleOptimize</c>
/// (phase 7, not yet ported) will call. Chooses the algorithm (<see cref="ChooseAlgorithm"/>,
/// already ported — this <i>is</i> the "AUTO選択" piece the plan names for 5e), repairs/hardens the
/// entry board, fans out to the chosen algorithm (<see cref="RunV5"/> / <see cref="RunMultiWorker"/>
/// wrapping ALNS/RSI/RSI++ / <see cref="RunAdaptivePortfolio"/>), then runs the ChainFill and HF80
/// epilogues and the inner "N1c" sentinel.
///
/// [TuningTelemetry, Kotlin原本の正確な呼び出し位置・ピース5で訂正] 旧いこの doc comment は
/// 「Kotlin の <c>optimize()</c> が入口で <c>beginTelemetry()</c> を呼ぶ」と書いていたが、これは
/// **現在の Kotlin 原本(3.388.0以降)に対して誤り**（フェーズ5a/5b 当時の理解を後日訂正）。
/// 実際には <c>beginTelemetry()</c> は <c>optimize()</c> からは一度も呼ばれておらず、
/// <c>V6FinalPort.handleOptimize</c>（フェーズ7・未移植）の入口から**一度だけ**呼ばれる
/// （3.388.0「利用者の1回の「つくる」ぶんの計測をゼロから始める」——`handleOptimize` は AUTO の
/// 31〜210秒帯で <c>optimize()</c> を最大3回呼ぶため、`optimize()` 側で毎回リセットすると
/// 最後の pass 以外の計測が失われる、という回帰の修正）。この C# 移植でも同じ構造を踏襲し、
/// <see cref="Optimize"/> のこの入口には <c>TuningTelemetry.Reset()</c> を**意図的に追加していない**
/// （追加すると 3.388.0 が直した回帰を再導入する）。liveBest 競合カウンタのリセットも
/// <c>beginTelemetry()</c> ではなく <see cref="Optimize"/> 自身の入口が担当する（下記
/// <c>_liveBestRef.Value = null;</c>、Kotlin原本と同じ役割分担）。<c>TuningTelemetry.BeginTelemetry()</c>
/// 相当の公開静的メソッドは <c>V6NativeOptimizer.RunSlot.cs</c> の <see cref="BeginTelemetry"/> として
/// 独立に存在し、フェーズ18で <c>HandleOptimize</c> を移植する際にその入口から一度だけ呼ぶ。
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>
    /// [3.335.0/外部レビュー P1, Kotlin原本] この実行だけの成果物入れ（<see cref="RunSlot"/>）を作り、
    /// <see cref="AsyncLocal{T}"/> で呼び出し木の隅々まで運ぶ。結果は返り値に載せて返すので、実行が
    /// 重なっても呼び出し側は自分の実行の値だけを読む。static は「いちばん新しい実行のライブ表示」
    /// として残し、置き換えられた古い実行は書き込まない。
    /// </summary>
    public static async Task<V6OptimizerResult> Optimize(
        MagiState state,
        int[][]? initial = null,
        V6OptimizerOptions? options = null,
        Func<bool>? shouldStop = null,
        Action<string, ViolationReport?, long, long>? onProgressRaw = null,
        // [3.346.1, Kotlin原本] stopIsFinal: shouldStop が真のとき、それが単調な停止（探索締切・
        // キャンセル）かを返す。停滞シグナルは単調でない（改善が届けば偽に戻る）ので、適応ポートフォリオ
        // はそれだけを確認窓で再確認する。既定は「常に単調」＝確認せず即離脱。
        Func<bool>? stopIsFinal = null,
        CancellationToken cancellationToken = default)
    {
        var initSched = initial ?? state.Schedule.ToIntArray2D();
        var opts = options ?? new V6OptimizerOptions();
        var stop = shouldStop ?? (() => false);
        var onProgress = onProgressRaw ?? ((_, _, _, _) => { });
        var finalCheck = stopIsFinal ?? (() => true);

        var slot = new RunSlot(NextRunId());
        SetNewestRunId(slot.Id);
        _lastAlternatives = Array.Empty<int[][]>();
        _lastFusionElites = Array.Empty<AdaptiveElite>();
        ClearInfeasible();
        _liveBestRef.Value = null;

        CurrentRunSlot.Value = slot;
        var r = await OptimizeInSlot(state, initSched, opts, stop, onProgress, finalCheck, cancellationToken)
            .ConfigureAwait(false);

        var result = r with { Alternatives = slot.Alternatives, InfeasibleFamilies = slot.Infeasible };
        // [Kotlin原本] `.also { it.fusionElites = slot.fusionElites }` — FusionElites は record の外側の
        //   mutable プロパティなので `with` 式のプロパティコピーには含まれない（意図的、Types.cs 参照）。
        result.FusionElites = slot.FusionElites;
        return result;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>private suspend fun optimizeInSlot(...)</c>. Promoted to
    /// <c>internal static</c> per the established precedent (<see cref="RunV5"/>/
    /// <see cref="RunAlnsSingle"/>/<see cref="Hf80PostPolish"/>/<see cref="RunRsi"/> were all
    /// similarly promoted from Kotlin <c>private</c>) so it is directly unit-testable via
    /// <c>InternalsVisibleTo("MagiEngine.Tests")</c> without needing the <see cref="RunSlot"/>
    /// scaffolding that only <see cref="Optimize"/> sets up.
    /// </summary>
    internal static async Task<V6OptimizerResult> OptimizeInSlot(
        MagiState state,
        int[][] initial,
        V6OptimizerOptions options,
        Func<bool> shouldStop,
        Action<string, ViolationReport?, long, long> onProgressRaw,
        Func<bool> stopIsFinal,
        CancellationToken cancellationToken = default)
    {
        var started = NowMs();
        // [敵対的レビュー: 進捗コールバックの直列化, Kotlin原本] RunMultiWorker(仮説横断)・
        //   RunAlnsChains(チェーン横断)は複数の並列タスクから同じ onProgress を並行呼出しうる。この
        //   最上位の入口1箇所でロックすれば、内側の多層fan-out（仮説×チェーン）を経ても最終的に
        //   呼び出し元コールバックへは必ず直列で届く。
        var progressLock = new object();
        void OnProgress(string phase, ViolationReport? report, long iters, long elapsed)
        {
            lock (progressLock) { onProgressRaw(phase, report, iters, elapsed); }
        }

        var chosen = ChooseAlgorithm(options.Algorithm, options.TotalBudgetSec);
        var p = ScheduleUtil.CachedProblem(state);
        var schedule = Hf66DataHardening(state, ScheduleUtil.NormalizeSchedule(initial, p), "pre");
        // [N1b, Kotlin原本] 入口修復(hf67)は better(hard→weighted→total) 改善時のみ採用。既に良好な
        //   入力（前回結果の再最適化など）を破壊し、探索を劣化seedに係留する事故を防ぐ（運用ログ実例:
        //   入力214 → 修復後HARD4/250 → 275秒が回復に浪費）。hf66(群内正規化)は無条件維持。
        var entryReport = UnifiedViolationChecker.Check(state, schedule);
        var repaired = Hf67HardRepair(state, schedule, new JavaRandom(ActualSeed(options.Seed) ^ 0x67L)).Schedule;
        var repairedReport = UnifiedViolationChecker.Check(state, repaired);
        var hf67Adopted = Better(repairedReport, entryReport);
        if (hf67Adopted) schedule = repaired;
        var entryBoard = schedule.Copy2D();   // [N1c, Kotlin原本] 内側番兵用に入力の勤務表を保持
        var entryBoardReport = hf67Adopted ? repairedReport : entryReport;

        // [仮説数上限撤廃, Kotlin原本] 仮説数(w)をワーカー設定にそのまま連動させる（多様性>深さ。V5だけは
        //   仮説の概念を使わずworkersをそのままSAチェーン数とする＝対象外）。
        var w = HypothesisCount(options.EffectiveWorkers);
        var (spawnHyp, plan) = HypothesisSpawnPlan(options.EffectiveWorkers, w);
        var planMin = plan.Min();
        var planMax = plan.Max();
        var planNote = planMin == planMax ? $"仮説内{planMin}並列" : $"仮説内{planMin}〜{planMax}並列";
        var workersNote = chosen switch
        {
            V6Algorithm.V5 => $"workers={options.EffectiveWorkers}（SAチェーン）",
            V6Algorithm.Portfolio => $"workers={options.EffectiveWorkers}（適応ポートフォリオ仮説{w}・各ロール単一チェーン）",
            _ => $"workers={options.EffectiveWorkers}（実効仮説{spawnHyp}{(spawnHyp < w ? $"＝設定{w}をコア数まで縮小" : "")}・{planNote}）",
        };

        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "V6Dispatcher", message: $"algorithm={chosen} budget={options.TotalBudgetSec}s {workersNote}"),
            new MirrorLog(tag: "HF67", message: hf67Adopted
                ? $"入口修復を採用 HARD {entryReport.Hard}->{repairedReport.Hard} / total {entryReport.Total}->{repairedReport.Total}"
                : $"入口修復を見送り（入力の方が良好: HARD {entryReport.Hard}/total {entryReport.Total} ≦ 修復後 HARD {repairedReport.Hard}/total {repairedReport.Total}）"),
        };

        var full = Math.Max(1, options.TotalBudgetSec);
        V6OptimizerResult result = chosen switch
        {
            // V5 already runs `workers` parallel SA chains inside SaOptimizer.
            V6Algorithm.V5 => await RunV5(state, schedule, options, full, shouldStop, OnProgress, cancellationToken)
                .ConfigureAwait(false),
            // ALNS/RSI/RSI++ are run as up to `w` parallel hypotheses with hybrid early-cancel.
            // [3.266.0/hypothesis basin diversity, Kotlin原本] 各仮説の入口盤面を HypothesisStartFor で
            //   多様化（W0/W4のみ現行盤面のコピー=安全フロア維持）。
            V6Algorithm.Alns => await RunMultiWorker(w, options, OnProgress,
                (i, o, prog) => RunAlns(state, HypothesisStartFor(state, schedule, i, o.Seed), o, full, shouldStop, prog, cancellationToken),
                cancellationToken).ConfigureAwait(false),
            V6Algorithm.Rsi => await RunMultiWorker(w, options, OnProgress,
                (i, o, prog) => RunRsi(state, HypothesisStartFor(state, schedule, i, o.Seed), o, full, shouldStop, prog, cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false),
            V6Algorithm.RsiPlus => await RunMultiWorker(w, options, OnProgress,
                (i, o, prog) => RunRsiPlus(state, HypothesisStartFor(state, schedule, i, o.Seed), o, full, shouldStop, prog, cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false),
            // [3.267.0/adaptive hypothesis epochs, Kotlin原本] 停滞/basin重複を検知し、エリートを保存
            //   しながら役割を再配属する非同期適応ポートフォリオ。
            V6Algorithm.Portfolio => await RunAdaptivePortfolio(state, schedule, w, options, full, shouldStop, stopIsFinal, OnProgress, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException("AUTO must be resolved"),
        };
        logs.AddRange(result.PhaseLogs);

        // [E11/多人数ブロック移動, Kotlin原本] エピローグで残 covU を「勤務→勤務」連鎖で充填（ALNS単独や
        //   covU を focus しなかった経路でも走る保険）。keep-best 照合＝退化不能。
        var resultSched = result.Schedule;
        {
            var preRep = UnifiedViolationChecker.Check(state, resultSched);
            if (preRep.Hard > 0 && preRep.Breakdown.GetValueOrDefault("covU", 0) > 0 && !shouldStop())
            {
                var cand = resultSched.Copy2D();
                var n = ApplyCovUChains(state, cand, new JavaRandom(ActualSeed(options.Seed) ^ 0xC0FFEEL));
                if (n > 0)
                {
                    var candRep = UnifiedViolationChecker.Check(state, cand);
                    if (Better(candRep, preRep))
                    {
                        resultSched = cand;
                        logs.Add(new MirrorLog(tag: "ChainFill",
                            message: $"多人数ブロック移動で covU 充填: HARD {preRep.Hard}→{candRep.Hard} / total {preRep.Total}→{candRep.Total}（連鎖{n}件）"));
                    }
                }
            }
        }

        // [review #3, Kotlin原本] Final epilogue polish only when the caller isn't running its own post chain.
        var polished = options.PostPolish && !shouldStop()
            ? Hf80PostPolish(state, resultSched, Math.Max(1, Math.Min(30, options.TotalBudgetSec / 20)), ActualSeed(options.Seed) ^ 0x80L, shouldStop, cancellationToken)
            : new PolishResult(resultSched, Array.Empty<MirrorLog>(), 0);
        var finalReport = UnifiedViolationChecker.Check(state, polished.Schedule);
        logs.AddRange(polished.Logs);
        logs.Add(new MirrorLog(tag: "V6Dispatcher",
            message: $"完了 algorithm={chosen} HARD={finalReport.Hard} total={finalReport.Total} elapsed={NowMs() - started}ms"));

        // [N1c, Kotlin原本] 内側番兵: 最終結果が入力の勤務表より劣るなら入力の勤務表へ復帰
        //   （FinalPortの外側Sentinelと二重化）。全段keep-bestのため通常は発火しない。発火時は
        //   「予算が改善に寄与しなかった」ことの可視化を兼ねる。
        if (Better(entryBoardReport, finalReport))
        {
            logs.Add(new MirrorLog(level: "W", tag: "V6Dispatcher",
                message: $"内側番兵: 結果(HARD={finalReport.Hard}/total={finalReport.Total})が入力の勤務表(HARD={entryBoardReport.Hard}/total={entryBoardReport.Total})より劣化のため入力の勤務表を採用"));
            var mergedEntryLogs = new List<MirrorLog>(logs);
            mergedEntryLogs.AddRange(entryBoardReport.Logs);
            return new V6OptimizerResult(entryBoard, entryBoardReport with { Logs = mergedEntryLogs }, chosen, logs,
                result.Iterations + polished.Iterations, NowMs() - started);
        }
        var mergedFinalLogs = new List<MirrorLog>(logs);
        mergedFinalLogs.AddRange(finalReport.Logs);
        return new V6OptimizerResult(polished.Schedule, finalReport with { Logs = mergedFinalLogs }, chosen, logs,
            result.Iterations + polished.Iterations, NowMs() - started);
    }
}
