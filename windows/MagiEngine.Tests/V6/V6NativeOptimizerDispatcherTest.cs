using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5e (final piece of phase 5): <see cref="V6NativeOptimizer.Optimize"/>/
/// <see cref="V6NativeOptimizer.OptimizeInSlot"/> — the top-level dispatcher that
/// <c>V6FinalPort.HandleOptimize</c> (phase 7, not yet ported) will call. Exercises:
/// algorithm dispatch across all five explicit <see cref="V6Algorithm"/> values plus AUTO's
/// budget-based resolution (already-ported <see cref="V6NativeOptimizer.ChooseAlgorithm"/>);
/// the HF67 entry-repair adopt/reject decision; the PostPolish epilogue toggle; and the
/// <see cref="V6NativeOptimizer.RunSlot"/>-scoped Alternatives/FusionElites propagation to both
/// the per-run result and the "newest run" static mirrors (already wired inside the ported
/// <c>RunAdaptivePortfolio</c>/<c>RunMultiWorker</c> — this test exercises that wiring end-to-end
/// through the entry point that actually creates and scopes the <see cref="V6NativeOptimizer.RunSlot"/>).
/// </summary>
public class V6NativeOptimizerDispatcherTest
{
    private static MagiState LoadFixture(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static void AssertValidShape(Problem p, int[][] schedule)
    {
        Assert.Equal(p.S, schedule.Length);
        foreach (var row in schedule) Assert.Equal(p.T, row.Length);
    }

    private static void AssertNeverWorsensInput(MagiState state, Problem p, int[][] initial, ViolationReport resultReport)
    {
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            "The optimizer's output must never be strictly worse than its input (keep-best across the whole pipeline).");
    }

    private static void NoOpProgress(string phase, ViolationReport? rep, long iters, long elapsed) { }

    public static TheoryData<V6Algorithm> ExplicitAlgorithms => new()
    {
        V6Algorithm.V5, V6Algorithm.Alns, V6Algorithm.Rsi, V6Algorithm.RsiPlus, V6Algorithm.Portfolio,
    };

    [Theory]
    [MemberData(nameof(ExplicitAlgorithms))]
    public async Task Optimize_ImmediateStopDispatchesTheRequestedAlgorithmAndNeverWorsensInput(V6Algorithm algorithm)
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: algorithm, Workers: 2, Seed: 1L),
            shouldStop: () => true, onProgressRaw: NoOpProgress, stopIsFinal: () => true);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"Immediate-stop dispatch took {sw.ElapsedMilliseconds}ms — should exit promptly.");
        Assert.Equal(algorithm, result.Algorithm);
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Contains(result.PhaseLogs, l => l.Tag == "V6Dispatcher" && l.Message.StartsWith("algorithm="));
        Assert.Contains(result.PhaseLogs, l => l.Tag == "V6Dispatcher" && l.Message.StartsWith("完了"));
    }

    [Fact]
    public async Task Optimize_AutoResolvesByBudgetToV5ForAShortBudgetAndCompletes()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        // ChooseAlgorithm(Auto, budgetSec<=30) resolves to V5 (HypothesisDiversityPolicy.AutoAlgorithmForBudget).
        var result = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: V6Algorithm.Auto, TotalBudgetSec: 10, Workers: 2, Seed: 1L),
            shouldStop: () => true, onProgressRaw: NoOpProgress, stopIsFinal: () => true);
        Assert.Equal(V6Algorithm.V5, result.Algorithm);
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public async Task Optimize_AdoptsTheHf67EntryRepairWhenItStrictlyImprovesTheAllRestEntryBoard()
    {
        // Coverage demand ("A" needed on days 0 and 1) against an all-rest schedule: HF67's
        // coverage-fill pass should place someone onto "A" on those days, strictly reducing HARD.
        var state = MinimalState.Build(needDay1: new Dictionary<string, string> { ["1,0"] = "1", ["1,1"] = "1" });
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var result = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: V6Algorithm.V5, Workers: 1, Seed: 1L),
            shouldStop: () => true, onProgressRaw: NoOpProgress, stopIsFinal: () => true);
        var hf67Log = Assert.Single(result.PhaseLogs, l => l.Tag == "HF67");
        Assert.StartsWith("入口修復を採用", hf67Log.Message);
        AssertNeverWorsensInput(state, p, initial, result.Report);
    }

    [Fact]
    public async Task Optimize_SkipsTheHf67EntryRepairWhenTheInputIsAlreadyViolationFree()
    {
        // MinimalState's default (all-rest, no needs/wishes/constraints) has zero violations to
        // begin with, so the repair pass cannot strictly improve on it.
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var result = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: V6Algorithm.V5, Workers: 1, Seed: 1L),
            shouldStop: () => true, onProgressRaw: NoOpProgress, stopIsFinal: () => true);
        var hf67Log = Assert.Single(result.PhaseLogs, l => l.Tag == "HF67");
        Assert.StartsWith("入口修復を見送り", hf67Log.Message);
    }

    [Fact]
    public async Task Optimize_PostPolishTrueAddsAnHf80EpilogueLogAndFalseOmitsIt()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        // shouldStop must stay false (not immediate-stop) so the `!shouldStop()` gate on both the
        // ChainFill and PostPolish epilogues actually lets them run; budget is tiny so this still
        // completes almost instantly against MinimalState's trivial 2-staff/7-day/2-shift shape.
        var withPolish = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: V6Algorithm.V5, TotalBudgetSec: 1, Workers: 1, Seed: 1L, PostPolish: true),
            shouldStop: () => false, onProgressRaw: NoOpProgress, stopIsFinal: () => false);
        Assert.Contains(withPolish.PhaseLogs, l => l.Tag == "HF80");

        var withoutPolish = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: V6Algorithm.V5, TotalBudgetSec: 1, Workers: 1, Seed: 1L, PostPolish: false),
            shouldStop: () => false, onProgressRaw: NoOpProgress, stopIsFinal: () => false);
        Assert.DoesNotContain(withoutPolish.PhaseLogs, l => l.Tag == "HF80");

        AssertNeverWorsensInput(state, p, initial, withPolish.Report);
        AssertNeverWorsensInput(state, p, initial, withoutPolish.Report);
    }

    [Fact]
    public async Task Optimize_PortfolioRunPropagatesAlternativesAndFusionElitesToBothTheResultAndTheNewestRunStaticMirrors()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var result = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: V6Algorithm.Portfolio, TotalBudgetSec: 2, Workers: 2, Seed: 5L),
            shouldStop: () => false, onProgressRaw: NoOpProgress, stopIsFinal: () => false);

        AssertNeverWorsensInput(state, p, initial, result.Report);
        Assert.True(result.Alternatives.Count <= 3, "Alternatives are capped at 3 (RunAdaptivePortfolio/RunMultiWorker's `.Take(3)`).");
        // [3.335.0, Kotlin原本の再現] `runSlot()?.let { it.fusionElites = ...; it.alternatives = ... }`
        // に続けて `if (ownsStatics(runSlot())) { lastFusionElites = ...; lastAlternatives = ... }` — both
        // writes use the *same* local values, so for a solitary (non-overlapping) run that is
        // necessarily the newest, the per-run result and the "newest run" static mirrors must be the
        // exact same object.
        Assert.Same(V6NativeOptimizer.LastAlternatives, result.Alternatives);
        Assert.Same(V6NativeOptimizer.LastFusionElites, result.FusionElites);
    }

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public async Task Optimize_CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.Optimize(
            state, initial, new V6OptimizerOptions(Algorithm: V6Algorithm.Rsi, TotalBudgetSec: 5, Workers: 2, Seed: 3L),
            shouldStop: () => true, onProgressRaw: NoOpProgress, stopIsFinal: () => true);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 15_000, $"Real-fixture immediate-stop run took {sw.ElapsedMilliseconds}ms.");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.Rsi, result.Algorithm);
    }
}
