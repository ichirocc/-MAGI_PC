using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5c gate: <see cref="V6NativeOptimizer.RunV5"/> / <see cref="V6NativeOptimizer.RunAlnsChains"/> /
/// <see cref="V6NativeOptimizer.RunAlns"/> / <see cref="V6NativeOptimizer.RunAlnsSingle"/>.
///
/// Same non-bit-exactness caveat as <see cref="SaOptimizerTest"/> applies here, doubly so — these
/// functions layer restarts/destroy-repair/GLS on top of an already wall-clock-bounded inner SA
/// (RunV5) or their own wall-clock-bounded restart loop (the ALNS family). Per the plan's own
/// verification criteria for this sub-phase, these tests assert: (1) never-worse-than-input +
/// valid schedule shape, (2) cancellation soundness, (3) a modest bounded-execution/quality
/// spot-check against real fixtures — not bit-exact reproducibility.
///
/// (2) is where the two families genuinely diverge, and that divergence is itself asserted here
/// (not just documented): the tight single-chain loops (RunV5 via <see cref="SaOptimizer.Run"/>,
/// RunAlnsSingle via its own <c>TimeUp()</c> poll) return CLEANLY on cancellation — no throw, best
/// solution so far. The multi-chain coordinator (RunAlnsChains, and RunAlns when Workers&gt;1)
/// explicitly rethrows via <c>cancellationToken.ThrowIfCancellationRequested()</c> once all chains
/// have been collected — a deliberate TPL-idiomatic outer-coordinator fault-propagation point,
/// distinct from the inner tight-loop non-throw style (see the doc comments on
/// <c>V6NativeOptimizer.RunAlnsSingle</c>/<c>RunAlnsChains</c> for the design rationale).
/// </summary>
public class V6NativeOptimizerAlnsTest
{
    private static MagiState LoadFixture(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static void AssertValidShape(Problem p, int[][] schedule)
    {
        Assert.Equal(p.S, schedule.Length);
        for (int i = 0; i < p.S; i++)
        {
            Assert.Equal(p.T, schedule[i].Length);
            for (int j = 0; j < p.T; j++)
                Assert.InRange(schedule[i][j], 0, p.K - 1);
        }
    }

    /// <summary>The exact "never worse than input" invariant each function's own degradation
    /// sentinel enforces: the baseline (input, normalized) must never be *strictly* better than
    /// the returned report.</summary>
    private static void AssertNeverWorsensInput(MagiState state, Problem p, int[][] initial, ViolationReport resultReport)
    {
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            $"Result must never be worse than input (input hard={baseReport.Hard}/total={baseReport.Total}, " +
            $"result hard={resultReport.Hard}/total={resultReport.Total}).");
    }

    // ================================== RunV5 ==================================

    [Fact]
    public async Task RunV5_NeverWorsensInputAndReturnsValidSchedule()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var result = await V6NativeOptimizer.RunV5(state, initial, new V6OptimizerOptions(Workers: 2, Seed: 42L), budgetSec: 1);

        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.V5, result.Algorithm);
        Assert.Contains(result.PhaseLogs, l => l.Tag == "RunMAGI_V5");
        Assert.True(result.Iterations > 0, "A 1s budget on a tiny fixture should complete at least one SA iteration.");
    }

    [Fact]
    public async Task RunV5_ShouldStopTrueReturnsQuicklyWithoutThrowing()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        // Large budget: if ShouldStop weren't honoured, this would ride out the full 10s.
        var result = await V6NativeOptimizer.RunV5(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, shouldStop: () => true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000,
            $"ShouldStop=true must short-circuit, not ride out the 10s budget (took {sw.ElapsedMilliseconds}ms).");
        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public async Task RunV5_PreCancelledTokenReturnsQuicklyWithoutThrowing()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        // Matches SaOptimizer.Run's established contract: a pre-cancelled token short-circuits
        // the wrapped SA run cleanly (no throw), same as SaOptimizerTest.PreCancelledTokenReturnsImmediatelyWithInputScoreUnchanged.
        var result = await V6NativeOptimizer.RunV5(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, cancellationToken: cts.Token);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000, $"Pre-cancelled token must short-circuit (took {sw.ElapsedMilliseconds}ms).");
        AssertValidShape(p, result.Schedule);
    }

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public async Task RunV5_CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunV5(state, initial, new V6OptimizerOptions(Workers: 2, Seed: 7L), budgetSec: 1);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 4_000,
            $"[{fixtureName}] RunV5 should complete within a few seconds of its 1s budget (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
    }

    // =============================== RunAlnsSingle ===============================

    [Fact]
    public void RunAlnsSingle_NeverWorsensInputAndReturnsValidSchedule()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var result = V6NativeOptimizer.RunAlnsSingle(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 42L), budgetSec: 1);

        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.Alns, result.Algorithm);
        Assert.Contains(result.PhaseLogs, l => l.Tag == "RunMAGI_ALNS");
        Assert.True(result.Iterations > 0, "A 1s budget on a tiny fixture should complete at least one ALNS iteration.");
    }

    [Fact]
    public void RunAlnsSingle_ShouldStopTrueReturnsQuicklyWithInputReportUnchanged()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);

        var sw = Stopwatch.StartNew();
        var result = V6NativeOptimizer.RunAlnsSingle(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, shouldStop: () => true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000,
            $"ShouldStop=true must break out of the restart loop at its very first check (took {sw.ElapsedMilliseconds}ms).");
        Assert.Equal(baseReport.Hard, result.Report.Hard);
        Assert.Equal(baseReport.Total, result.Report.Total);
        Assert.Equal(baseReport.WeightedScore, result.Report.WeightedScore);
        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public void RunAlnsSingle_PreCancelledTokenReturnsQuicklyWithoutThrowing()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        // RunAlnsSingle unifies both cancellation sources (shouldStop + CancellationToken) into
        // one non-throwing TimeUp() poll (matching SaOptimizer.RunWorker's precedent) — so this
        // must return cleanly, not throw, even though the token itself is cancelled.
        var result = V6NativeOptimizer.RunAlnsSingle(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, cancellationToken: cts.Token);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000, $"Pre-cancelled token must short-circuit (took {sw.ElapsedMilliseconds}ms).");
        AssertValidShape(p, result.Schedule);
    }

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public void RunAlnsSingle_CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = V6NativeOptimizer.RunAlnsSingle(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 7L), budgetSec: 1);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 5_000,
            $"[{fixtureName}] RunAlnsSingle (2 restarts x 1s) should complete within a few seconds (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
    }

    // =============================== RunAlnsChains ===============================

    [Fact]
    public async Task RunAlnsChains_NeverWorsensInputAndRunsMultipleChains()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var result = await V6NativeOptimizer.RunAlnsChains(state, initial, new V6OptimizerOptions(Workers: 3, Seed: 42L), budgetSec: 1, shouldStop: null, onProgress: null);

        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        // Confirms the multi-chain code path (not a silent single-chain fallback) actually ran
        // and its own summary log made it into the returned result's PhaseLogs.
        var summary = Assert.Single(result.PhaseLogs, l => l.Tag == "AlnsChains");
        Assert.Contains("3並列", summary.Message);
    }

    [Fact]
    public async Task RunAlnsChains_PreCancelledTokenThrowsOperationCanceled()
    {
        var state = MinimalState.Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        // Unlike RunAlnsSingle/RunV5, the coordinator explicitly rethrows once all (individually
        // clean-returning) chains have been collected — an intentional, tested asymmetry.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            V6NativeOptimizer.RunAlnsChains(state, initial, new V6OptimizerOptions(Workers: 2, Seed: 1L), budgetSec: 10, shouldStop: null, onProgress: null, cancellationToken: cts.Token));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000, $"Cancellation must propagate quickly, not ride out the 10s budget (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task RunAlnsChains_ShouldStopTrueWithoutCancelledTokenReturnsQuicklyWithoutThrowing()
    {
        // shouldStop and the CancellationToken are independent signals: shouldStop=true makes
        // every chain return quickly, but with a token that was never cancelled the coordinator's
        // final ThrowIfCancellationRequested() must NOT fire.
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunAlnsChains(state, initial, new V6OptimizerOptions(Workers: 2, Seed: 1L), budgetSec: 10, shouldStop: () => true, onProgress: null);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000, $"shouldStop=true must short-circuit every chain (took {sw.ElapsedMilliseconds}ms).");
        AssertValidShape(p, result.Schedule);
    }

    // ==================== RunAlns (Workers-based dispatcher) ====================

    [Fact]
    public async Task RunAlns_SingleWorkerDispatchesDirectlyToRunAlnsSingle()
    {
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();

        var result = await V6NativeOptimizer.RunAlns(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 1);

        Assert.DoesNotContain(result.PhaseLogs, l => l.Tag == "AlnsChains");
    }

    [Fact]
    public async Task RunAlns_MultipleWorkersDispatchesToRunAlnsChains()
    {
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();

        var result = await V6NativeOptimizer.RunAlns(state, initial, new V6OptimizerOptions(Workers: 2, Seed: 1L), budgetSec: 1);

        Assert.Contains(result.PhaseLogs, l => l.Tag == "AlnsChains");
    }

    [Fact]
    public async Task RunAlns_MultipleWorkersPreCancelledTokenThrowsOperationCanceled()
    {
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // RunAlns(Workers>1) delegates to RunAlnsChains, so it inherits that coordinator's throw
        // contract — asserted here at the dispatcher's own public surface, not just on RunAlnsChains directly.
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            V6NativeOptimizer.RunAlns(state, initial, new V6OptimizerOptions(Workers: 2, Seed: 1L), budgetSec: 10, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task RunAlns_SingleWorkerPreCancelledTokenReturnsQuicklyWithoutThrowing()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await V6NativeOptimizer.RunAlns(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, cancellationToken: cts.Token);

        AssertValidShape(p, result.Schedule);
    }
}
