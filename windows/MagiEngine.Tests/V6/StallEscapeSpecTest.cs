using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[Kotlin原本 <c>StallEscapeSpecTest</c> 1:1] Android <c>docs/stall_escape.md</c> の規則と数値をそのまま固定する。
/// 既存の <c>V6FinalPortWatchdogTest</c>・<c>AdaptiveHypothesisEpochPolicyTest</c>・RSI テストが持たない表の値と境界だけを扱う。</summary>
public class StallEscapeSpecTest
{
    [Fact]
    public void BudgetTableFor300Seconds()
    {
        var b = V6FinalPort.WatchdogBudgetOf(300_000L, startMs: 0L, hardDeadlineMs: 300_000L, fraction: 0.9);
        Assert.Equal(45_000L, b.MinRunMs);
        Assert.Equal(25_000L, b.PostReserveMs);
        Assert.Equal(275_000L, b.SearchDeadlineMs);
        Assert.Equal(275_000L, b.SearchWindowMs);
        Assert.Equal(270_000L, b.StallMs);
        Assert.Equal(37_500L, b.StallHardMs);
        Assert.Equal(7_500L, b.PhaseGraceMs);
    }

    [Fact]
    public void BudgetTableFor60SecondsFallsBackToWindowFraction()
    {
        var b = V6FinalPort.WatchdogBudgetOf(60_000L, startMs: 0L, hardDeadlineMs: 60_000L, fraction: 0.9);
        Assert.Equal(10_000L, b.MinRunMs);
        Assert.Equal(8_000L, b.PostReserveMs);
        Assert.Equal(52_000L, b.SearchWindowMs);
        Assert.Equal(46_800L, b.StallMs);
        Assert.Equal(15_000L, b.StallHardMs);
        Assert.Equal(2_000L, b.PhaseGraceMs);
    }

    [Fact]
    public void TwentySecondBudgetCannotFireTheNormalWatchdog()
    {
        var b = V6FinalPort.WatchdogBudgetOf(20_000L, startMs: 0L, hardDeadlineMs: 20_000L, fraction: 0.9);
        Assert.Equal(8_000L, b.MinRunMs);
        Assert.Equal(8_000L, b.PostReserveMs);
        Assert.Equal(12_000L, b.SearchWindowMs);
        Assert.True(b.StallMs >= b.SearchWindowMs);
        Assert.True(b.StallHardMs >= b.SearchWindowMs);
    }

    [Fact]
    public void BudgetUsesStartAndHardDeadlineNotZero()
    {
        var b = V6FinalPort.WatchdogBudgetOf(300_000L, startMs: 1_000_000L, hardDeadlineMs: 1_300_000L, fraction: 0.9);
        Assert.Equal(1_275_000L, b.SearchDeadlineMs);
        Assert.Equal(275_000L, b.SearchWindowMs);
    }

    [Fact]
    public void ProgressImprovedFollowsHardThenWeightedThenTotal()
    {
        Assert.True(V6FinalPort.ProgressImproved(h: 0, wgt: 1000.0, t: 50, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.True(V6FinalPort.ProgressImproved(h: 1, wgt: 9.0, t: 50, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.True(V6FinalPort.ProgressImproved(h: 1, wgt: 10.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.False(V6FinalPort.ProgressImproved(h: 1, wgt: 11.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.False(V6FinalPort.ProgressImproved(h: 2, wgt: 0.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
    }

    [Fact]
    public void ProgressImprovedIgnoresFloatingNoiseButBetterReportIsStrict()
    {
        Assert.False(V6FinalPort.ProgressImproved(h: 1, wgt: 10.0 - 1e-9, t: 1, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.True(V6FinalPort.ProgressImproved(h: 1, wgt: 10.0 + 1e-9, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.False(V6FinalPort.ProgressImproved(h: 1, wgt: 11.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
    }

    [Fact]
    public void WeightsAreIntegersSoToleranceNeverHidesARealDifference()
    {
        Assert.All(MirrorKeys.Weights, kv => Assert.True(kv.Weight == Math.Floor(kv.Weight) && kv.Weight >= 2.0));
        Assert.Equal(new HashSet<string> { "groupViol", "c3n", "covU", "pref", "c3w" }, MirrorKeys.Hard.ToHashSet());
    }

    [Fact]
    public void FiringBoundariesAreStrict()
    {
        const long start = 0L, minRun = 45_000L, grace = 7_500L, eff = 270_000L;
        bool Fired(long now, long lastImprove, long lastPhase) =>
            V6FinalPort.WatchdogStagnationFired(now, start, minRun, lastPhase, grace, lastImprove, eff);
        Assert.False(Fired(now: 300_000L, lastImprove: 30_000L, lastPhase: 0L));
        Assert.True(Fired(now: 300_001L, lastImprove: 30_000L, lastPhase: 0L));
        Assert.False(Fired(now: 540_000L, lastImprove: 0L, lastPhase: 540_000L));
        Assert.True(Fired(now: 540_001L, lastImprove: 0L, lastPhase: 540_001L));
        Assert.False(Fired(now: 45_000L, lastImprove: -300_000L, lastPhase: -100_000L));
    }

    [Fact]
    public void StopConfirmWindowIsFiveSeconds() => Assert.Equal(5_000L, V6NativeOptimizer.StopConfirmMs);

    [Fact]
    public void Hf63TracksExactlyThirteenFamilies()
    {
        Assert.Equal(
            new HashSet<string> { "c1", "c2", "c3", "c3n", "c3m", "c3mn", "c41", "c42", "covU", "covO", "pref", "low", "high" },
            Hf63Infeasibility.KeyToIndex.Keys.ToHashSet());
        foreach (var k in new[] { "groupViol", "c3w", "c41s", "c42s", "apt", "weekly", "fair" }) Assert.False(Hf63Infeasibility.KeyToIndex.ContainsKey(k), k);
    }

    [Fact]
    public void EffortItersFollowsTheAttemptsTargetFormula()
    {
        Assert.Equal(2_500, V6NativeOptimizer.RsiHf63EffortIters(2));
        Assert.Equal(2_500, V6NativeOptimizer.RsiHf63EffortIters(5));
        Assert.Equal(1_667, V6NativeOptimizer.RsiHf63EffortIters(8));
        Assert.Equal(1_250, V6NativeOptimizer.RsiHf63EffortIters(10));
    }

    [Fact]
    public void PortfolioQuantaAndDuplicateDistance()
    {
        Assert.Equal(5, AdaptiveHypothesisEpochPolicy.BASE_QUANTUM_SEC);
        Assert.Equal(8, AdaptiveHypothesisEpochPolicy.IMPROVING_QUANTUM_SEC);
        Assert.Equal(35, AdaptiveHypothesisEpochPolicy.RSI_PLUS_BASE_QUANTUM_SEC);
        Assert.Equal(45, AdaptiveHypothesisEpochPolicy.RSI_PLUS_IMPROVING_QUANTUM_SEC);
        Assert.Equal(2, AdaptiveHypothesisEpochPolicy.DUPLICATE_DISTANCE_CELLS);
        Assert.Equal(1, AdaptiveHypothesisEpochPolicy.NextStagnantEpochs(0, improvedThisEpoch: false));
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.NextStagnantEpochs(3, improvedThisEpoch: true));
    }

    private static ViolationReport Rep(int hard, double weighted, int total, params (string Family, int Count)[] fams)
    {
        var bd = fams.ToDictionary(f => f.Family, f => f.Count);
        return new ViolationReport(
            Violations: new Dictionary<string, string>(), NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(), Breakdown: bd,
            Total: total, Hard: hard, Soft: total - hard, WeightedScore: weighted);
    }

    [Fact]
    public void StagnationLatchClearsOnImprovementAndStaysOnNonImprovement()
    {
        var wd = new V6FinalPort.WatchdogBest(startMs: 0L);
        Assert.True(wd.Observe(Rep(1, 100.0, 5), nowMs: 1_000L, observedIters: 10L, beatsInput: () => true, wishC3wProven: 0));
        Assert.Equal(1_000L, wd.LastBeatInputMs);
        wd.Fire(nowMs: 300_000L, observedIters: 500L, byOverride: true);
        Assert.True(wd.StagnationFired); Assert.Equal(299_000L, wd.StagnationDurationMs); Assert.Equal(500L, wd.StagnationIters);
        Assert.True(wd.StagnationByOverride);
        Assert.False(wd.Observe(Rep(1, 101.0, 5), 301_000L, 600L, () => false, 0));
        Assert.True(wd.StagnationFired);
        Assert.True(wd.Observe(Rep(1, 99.0, 5), 302_000L, 700L, () => false, 0));
        Assert.False(wd.StagnationFired); Assert.Equal(-1L, wd.StagnationDurationMs); Assert.Equal(-1L, wd.StagnationIters);
        Assert.False(wd.StagnationByOverride);
        Assert.Equal(302_000L, wd.LastBestImproveMs); Assert.Equal(700L, wd.LastBestImproveIters);
        Assert.Equal(1_000L, wd.LastBeatInputMs);
        Assert.Equal(2, wd.BestVersion);
    }

    [Fact]
    public void ObserveTracksNonCovUHardAndC3nOnlyFlag()
    {
        var wd = new V6FinalPort.WatchdogBest(startMs: 0L);
        var beatsAsked = 0;
        Assert.False(wd.Observe(Rep(int.MaxValue, double.MaxValue, int.MaxValue), 1L, 0L, () => { beatsAsked++; return true; }, 0));
        Assert.Equal(0, beatsAsked);
        wd.Observe(Rep(3, 9000.0 * 3, 3, ("c3n", 2), ("covU", 1)), 10L, 1L, () => true, 0);
        Assert.Equal(2, wd.BestNonCovUHard); Assert.True(wd.BestNonCovUAllC3n);
        wd.Observe(Rep(2, 9000.0 * 2, 2, ("c3n", 1), ("pref", 1)), 20L, 2L, () => true, 0);
        Assert.Equal(2, wd.BestNonCovUHard); Assert.False(wd.BestNonCovUAllC3n);
        wd.Observe(Rep(1, 9000.0, 1, ("c3w", 1)), 30L, 3L, () => true, 1);
        Assert.False(wd.BestNonCovUAllC3n);
        Assert.Equal(3, wd.BestVersion);
    }

    [Fact]
    public void AvoidSetsKeepSoftFocusableAndSeparateCooldown()
    {
        var (avoid, focusAvoid) = V6NativeOptimizer.AvoidSets(new HashSet<string> { "c1", "low", "c3n", "covO" }, covU: 3, covUFloor: 0, cooldownFocus: null);
        Assert.Equal(new HashSet<string> { "c3n" }, avoid.ToHashSet()); Assert.Equal(new HashSet<string> { "c3n" }, focusAvoid.ToHashSet());
        var (a2, f2) = V6NativeOptimizer.AvoidSets(new HashSet<string> { "pref" }, covU: 2, covUFloor: 2, cooldownFocus: "c1");
        Assert.Equal(new HashSet<string> { "pref", "covU" }, a2.ToHashSet()); Assert.Equal(new HashSet<string> { "pref", "covU", "c1" }, f2.ToHashSet());
        var (a3, _) = V6NativeOptimizer.AvoidSets(new HashSet<string>(), covU: 3, covUFloor: 2, cooldownFocus: null);
        Assert.Empty(a3);
        var (a4, _) = V6NativeOptimizer.AvoidSets(new HashSet<string>(), covU: 0, covUFloor: 0, cooldownFocus: null);
        Assert.Empty(a4);
    }

    // Kotlin 原本の defaultsMatchTheSpec から、C# に無い StallPolishInjection（移植対象外）を除く。
    [Fact]
    public void DefaultsMatchTheSpec()
    {
        Assert.Equal(0.9, PolishGate.NormalStallFraction);
        Assert.False(PolishGate.PostChainRollbackCountsZero);
        Assert.Equal(WishFloorMode.Off, PolishGate.WishConflictFloorMode);
        Assert.Equal(2, V6FinalPort.StallOverrideFactor);
        Assert.Equal(5000, Hf63Infeasibility.INFEAS_STALL_ITERS);
    }
}
