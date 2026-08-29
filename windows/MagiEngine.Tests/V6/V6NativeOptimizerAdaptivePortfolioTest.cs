using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5d (final piece, highest concurrency risk): <see cref="V6NativeOptimizer.RunAdaptivePortfolio"/>
/// — the async W0..W(workers-1) island-model coordinator underlying <see cref="V6Algorithm.Portfolio"/>.
/// All of its direct building blocks (<see cref="V6NativeOptimizer.HypothesisStartFor"/>,
/// <see cref="V6NativeOptimizer.ForceDiverseKick"/>, <see cref="V6NativeOptimizer.ForceMaxDistanceKick"/>,
/// <see cref="V6NativeOptimizer.ElitePathRelink"/>, <see cref="V6NativeOptimizer.ConfirmStop"/>,
/// <see cref="V6NativeOptimizer.AdaptiveEpochStart"/>) are already independently verified in
/// <see cref="V6NativeOptimizerPortfolioTest"/>, and the algorithms each epoch actually dispatches to
/// (<see cref="V6NativeOptimizer.RunAlns"/>/<see cref="V6NativeOptimizer.RunRsi"/>/
/// <see cref="V6NativeOptimizer.RunRsiPlus"/>) are verified in their own test files. This file therefore
/// tests the GLUE unique to the coordinator itself: worker sizing, the shouldStop/stopIsFinal/
/// CancellationToken interplay it wires through <c>ConfirmStop</c>, keep-best convergence of the shared
/// global best across parallel workers, and the shape/content of its aggregate summary log.
///
/// [役割割当・重要な事実] <c>AdaptiveHypothesisEpochPolicy.AssignmentFor(index: 0, reassignments: 0)</c>
/// is ALWAYS <see cref="HypothesisEpochRole.BaselineRefine"/>, which maps to <see cref="V6Algorithm.RsiPlus"/>
/// — so worker 0 always dispatches through <see cref="V6NativeOptimizer.RunRsiPlus"/> at epoch 0,
/// regardless of <c>w</c>. Since every per-role call forces <c>roleOptions = options with { Workers = 1 }</c>,
/// that RunRsiPlus call's own Phase2 (RunRsi, Workers=1) uses the SAME "throws OperationCanceledException
/// at round 0 on a pre-cancelled token" flavor verified directly in <c>V6NativeOptimizerRsiTest</c> — this
/// is what the cancellation test below relies on to deterministically observe a throw regardless of which
/// role the OTHER workers happen to draw.
///
/// [コスト管理] The single genuinely-slow test below accepts real wall-clock cost (a couple of seconds,
/// following the precedent set by <c>V6NativeOptimizerRsiPlusTest</c>'s own "accept the cost for one real
/// run" test) to get genuine non-zero <c>Iterations</c> coverage. A second test exercises the
/// <c>ConfirmStop</c> stagnation-signal integration point directly, which costs one real
/// <see cref="V6NativeOptimizer.StopConfirmMs"/> (5s) window — bounded to a single worker (<c>w: 1</c>)
/// to keep that one test's real cost to roughly one window, not <c>workers</c> windows in series (workers
/// run in parallel, so this is a belt-and-suspenders choice for a tighter, more predictable ceiling).
/// Every other test uses <c>shouldStop: () =&gt; true, stopIsFinal: () =&gt; true</c> so <c>ConfirmStop</c>
/// short-circuits immediately (verified directly in <c>V6NativeOptimizerPortfolioTest</c>), keeping this
/// file's remaining tests fast and deterministic.
/// </summary>
public class V6NativeOptimizerAdaptivePortfolioTest
{
    private static MagiState LoadFixture(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static void AssertValidShape(Problem p, int[][] schedule)
    {
        Assert.Equal(p.S, schedule.Length);
        for (var i = 0; i < p.S; i++)
        {
            Assert.Equal(p.T, schedule[i].Length);
            for (var j = 0; j < p.T; j++)
                Assert.InRange(schedule[i][j], 0, p.K - 1);
        }
    }

    private static void AssertNeverWorsensInput(MagiState state, Problem p, int[][] initial, ViolationReport resultReport)
    {
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            $"Result must never be worse than input (input hard={baseReport.Hard}/total={baseReport.Total}, " +
            $"result hard={resultReport.Hard}/total={resultReport.Total}).");
    }

    /// <summary>A no-op progress callback: <c>RunAdaptivePortfolio</c>'s <c>onProgress</c> parameter is
    /// non-nullable (a faithful port of Kotlin's non-nullable <c>(String, ViolationReport?, Long, Long)
    /// -&gt; Unit</c>), unlike sibling coordinators such as <c>RunMultiWorker</c> whose <c>onProgress</c>
    /// IS nullable — passing <c>null</c> here would throw the first time any progress event fires.</summary>
    private static void NoOpProgress(string phase, ViolationReport? rep, long iters, long elapsed) { }

    [Fact]
    public async Task ImmediateStopIsFinalExitsQuicklyWithoutRunningAnyEpochAndNeverWorsensInput()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunAdaptivePortfolio(
            state, initial, w: 2, new V6OptimizerOptions(Workers: 2, Seed: 1L), budgetSec: 10,
            shouldStop: () => true, stopIsFinal: () => true, onProgress: NoOpProgress);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5_000,
            $"stopIsFinal=true must short-circuit ConfirmStop immediately for every worker (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.Portfolio, result.Algorithm);
        var summary = Assert.Single(result.PhaseLogs, l => l.Tag == "AdaptivePortfolio" && l.Message.Contains("採用 HARD="));
        Assert.Contains("合計iter=", summary.Message);
        Assert.Contains("全体最良更新=", summary.Message);
        Assert.Contains("ワーカー解=2本", summary.Message);
        Assert.Contains("ワーカー離脱=全て締切まで実行", summary.Message);
    }

    [Fact]
    public async Task ShouldStopWithoutStopIsFinalRidesOutTheConfirmationWindowThenExitsWithStagnationReason()
    {
        // [実時間コスト] StopConfirmMs=5秒はKotlin原本どおり固定定数のため注入できない。w=1で単一
        // ワーカーに絞り、実コストを概ね1回ぶんの確認窓に留める（8並列で8回分待つことを避ける）。
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunAdaptivePortfolio(
            state, initial, w: 1, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 30,
            shouldStop: () => true, stopIsFinal: () => false, onProgress: NoOpProgress);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= V6NativeOptimizer.StopConfirmMs,
            $"A genuine stall (shouldStop stays true, stopIsFinal false) must ride out ConfirmStop's full window (took {sw.ElapsedMilliseconds}ms).");
        Assert.True(sw.ElapsedMilliseconds < V6NativeOptimizer.StopConfirmMs + 5_000,
            $"...but must not run any real search after that (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        var summary = Assert.Single(result.PhaseLogs, l => l.Tag == "AdaptivePortfolio" && l.Message.Contains("採用 HARD="));
        Assert.Contains("停滞シグナル", summary.Message);
    }

    [Fact]
    public async Task PreCancelledTokenThrowsOperationCanceledOnceTheDispatchedAlgorithmObservesIt()
    {
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            V6NativeOptimizer.RunAdaptivePortfolio(
                state, initial, w: 2, new V6OptimizerOptions(Workers: 2, Seed: 1L), budgetSec: 30,
                shouldStop: () => false, stopIsFinal: () => false, onProgress: NoOpProgress, cts.Token));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 10_000,
            $"Cancellation must propagate once worker 0's RunRsiPlus->RunRsi observes it, not ride out the 30s budget (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task CompletesAGenuineShortRunAndNeverWorsensInput()
    {
        // [教訓#30に照らした注記] 唯一「本物の反復が実際に起こる」ことを検証する費用として受け入れる
        // （V6NativeOptimizerRsiPlusTest の同種テストと同じ規律）。roleDeadline は
        // min(deadline, now+quantum*1000) で常に外側の deadline にクランプされるため、budgetSec を
        // 小さく保てば各役割(RunAlns/RunRsi/RunRsiPlus)の内部フロア（RunRsiPlusのみ最大35秒）に
        // 律速されず、実コストは budgetSec 近辺に収まる。
        // [CI フレーク対応] budgetSec=2 は共有CIランナー(windows-latest)のスレッドプール起動/JITウォーム
        // アップの遅延次第で Iterations=0 のまま締切に達しうることを実機で観測（result.Iterations > 0 が
        // 間欠的に失敗）。budgetSec=5 へ緩めて実反復が起きる余地を広げる（教訓#30の規律どおり実コストを
        // 払う判断の延長で、アサーション自体を弱めるのではなく実行時間側に余裕を持たせる）。
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunAdaptivePortfolio(
            state, initial, w: 2, new V6OptimizerOptions(Workers: 2, Seed: 7L), budgetSec: 5,
            shouldStop: () => false, stopIsFinal: () => false, onProgress: NoOpProgress);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 30_000,
            $"Sanity ceiling only — roleDeadline is clamped by the outer deadline (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.Portfolio, result.Algorithm);
        Assert.True(result.Iterations > 0, "A genuine (non-shortcut) run should execute at least some search iterations.");
        var summary = Assert.Single(result.PhaseLogs, l => l.Tag == "AdaptivePortfolio" && l.Message.Contains("採用 HARD="));
        Assert.Contains("役割別worker秒", summary.Message);
        // Both workers must have actually run and reported into the aggregate — not just worker 0.
        Assert.Contains("W0:", summary.Message);
        Assert.Contains("W1:", summary.Message);
    }

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public async Task CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunAdaptivePortfolio(
            state, initial, w: 2, new V6OptimizerOptions(Workers: 2, Seed: 3L), budgetSec: 10,
            shouldStop: () => true, stopIsFinal: () => true, onProgress: NoOpProgress);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8_000,
            $"[{fixtureName}] shouldStop=true/stopIsFinal=true must short-circuit quickly even on a real-size fixture (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.Portfolio, result.Algorithm);
    }
}
