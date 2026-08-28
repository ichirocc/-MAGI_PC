using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5c gate: <see cref="V6NativeOptimizer.RunRsi"/> and its complete hypothesis-generation
/// dependency chain (<see cref="V6NativeOptimizer.MaxViolatedFamily"/>,
/// <see cref="V6NativeOptimizer.RsiGenerateHypothesis"/>, <see cref="V6NativeOptimizer.ApplyCovOFree"/>,
/// <see cref="V6NativeOptimizer.ApplyC41Free"/>, <see cref="V6NativeOptimizer.ApplyC42Free"/>).
///
/// [キャンセルの第3の型] Unlike RunV5/RunAlnsSingle/Hf80PostPolish (which unify <c>shouldStop()</c> and
/// the <see cref="CancellationToken"/> into ONE non-throwing poll) and unlike RunAlnsChains/
/// RunMultiWorker (whose individual parallel units never throw — only the outer coordinator does,
/// once, after collection), <c>RunRsi</c>'s own per-round loop calls <c>if (stop()) break;</c>
/// (non-throwing) FIRST, then <c>cancellationToken.ThrowIfCancellationRequested()</c> (throwing)
/// SECOND — reproducing Kotlin's own <c>if (shouldStop()) break</c> followed by
/// <c>coroutineContext.ensureActive()</c>. This means a pre-cancelled token DOES throw from
/// <c>RunRsi</c> (at round 0, before any real work runs), which is asserted explicitly below as the
/// key behavioural distinction from its non-throwing siblings. This is not in tension with the
/// plan's "実行中キャンセルでクリーンに最良解を返すこと" criterion: <c>RunRsi</c> already publishes
/// its running best to the live-best side-channel (<c>PublishLiveBest</c>) at every round boundary
/// before the next round's cancellation check, so a caller that observes the thrown
/// <see cref="OperationCanceledException"/> can still recover the best-so-far solution from there —
/// exactly the same architecture already established (and tested) for RunAlnsChains/RunMultiWorker.
/// </summary>
public class V6NativeOptimizerRsiTest
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

    /// <summary>The exact "never worse than input" invariant RunRsi's own keep-best rounds enforce:
    /// the baseline (input, normalized) must never be *strictly* better than the returned report.</summary>
    private static void AssertNeverWorsensInput(MagiState state, Problem p, int[][] initial, ViolationReport resultReport)
    {
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            $"Result must never be worse than input (input hard={baseReport.Hard}/total={baseReport.Total}, " +
            $"result hard={resultReport.Hard}/total={resultReport.Total}).");
    }

    // ================================== RunRsi ==================================

    [Fact]
    public async Task RunRsi_NeverWorsensInputAndReturnsValidSchedule()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var result = await V6NativeOptimizer.RunRsi(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 42L), budgetSec: 2);

        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.Rsi, result.Algorithm);
        // rounds = max(2, min(8, budgetSec/30+2)) is always >= 2, and the final round is always
        // logged unconditionally (win or not) — so a completed run must carry at least one entry.
        Assert.Contains(result.PhaseLogs, l => l.Tag == "RunMAGI_RSI");
        Assert.True(result.Iterations > 0, "A 2s budget on a tiny fixture should complete at least one round's worth of SA/ALNS iterations.");
    }

    [Fact]
    public async Task RunRsi_ShouldStopTrueReturnsQuicklyWithInputReportUnchanged()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);

        var sw = Stopwatch.StartNew();
        // Large budget: if ShouldStop weren't honoured at the very first round-loop check, this
        // would ride out several rounds x 5s/round.
        var result = await V6NativeOptimizer.RunRsi(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, shouldStop: () => true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000,
            $"ShouldStop=true must break out of the round loop at its very first check (took {sw.ElapsedMilliseconds}ms).");
        Assert.Equal(baseReport.Hard, result.Report.Hard);
        Assert.Equal(baseReport.Total, result.Report.Total);
        Assert.Equal(baseReport.WeightedScore, result.Report.WeightedScore);
        Assert.Equal(0L, result.Iterations);
        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public async Task RunRsi_PreCancelledTokenThrowsOperationCanceledAtRoundZero()
    {
        // Unlike RunV5/RunAlnsSingle/Hf80PostPolish (non-throwing) — RunRsi's own round loop
        // explicitly rethrows via ThrowIfCancellationRequested() right after the non-throwing
        // shouldStop() check, mirroring Kotlin's `if (shouldStop()) break` + `ensureActive()` pair.
        // With no shouldStop supplied (defaults to a false-returning poll), the token check is
        // reached and fires on the very first round.
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            V6NativeOptimizer.RunRsi(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, cancellationToken: cts.Token));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000,
            $"Cancellation must propagate at round 0, not ride out the 10s budget (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task RunRsi_ShouldStopTrueWithoutCancelledTokenDoesNotThrow()
    {
        // shouldStop and the CancellationToken are independent signals (same contract as
        // RunAlnsChains/RunAlns): shouldStop=true breaks the round loop before the
        // ThrowIfCancellationRequested() check is even reached, so with a token that was never
        // cancelled, nothing throws.
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var result = await V6NativeOptimizer.RunRsi(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, shouldStop: () => true);

        AssertValidShape(p, result.Schedule);
    }

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public async Task RunRsi_CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunRsi(state, initial, new V6OptimizerOptions(Workers: 2, Seed: 7L), budgetSec: 2);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 10_000,
            $"[{fixtureName}] RunRsi (2 rounds x RunAlns/RunV5 + late-operators) should complete within a few seconds of its 2s budget (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public async Task RunRsi_SharedHf63IsAcceptedAndReusableAcrossCalls()
    {
        // sharedHf63 lets a caller (the phase 5d/5e adaptive-portfolio driver) thread stall-learning
        // across multiple RunRsi invocations for the same worker. Here we just confirm the
        // parameter is honoured without crashing and that the same instance survives being reused
        // (and further updated) across two sequential calls.
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var hf63 = new Hf63Infeasibility();

        var r1 = await V6NativeOptimizer.RunRsi(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 1, sharedHf63: hf63);
        var r2 = await V6NativeOptimizer.RunRsi(state, r1.Schedule, new V6OptimizerOptions(Workers: 1, Seed: 2L), budgetSec: 1, sharedHf63: hf63);

        AssertValidShape(p, r2.Schedule);
    }

    // ============================= MaxViolatedFamily =============================

    private static ViolationReport ReportWith(IReadOnlyDictionary<string, int> breakdown) =>
        new(
            Violations: new Dictionary<string, string>(),
            NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(),
            Breakdown: breakdown,
            Total: 0,
            Hard: 0,
            Soft: 0,
            WeightedScore: 0.0);

    [Fact]
    public void MaxViolatedFamily_HardFamilyIsChosenOverAMuchLargerSoftCount()
    {
        var report = ReportWith(new Dictionary<string, int> { ["low"] = 1000, ["c3n"] = 1 });

        Assert.Equal("c3n", V6NativeOptimizer.MaxViolatedFamily(report));
    }

    [Fact]
    public void MaxViolatedFamily_HardFamilyPriorityFollowsTheDeclaredOrderAmongMultipleHardFamilies()
    {
        // "order" lists groupViol, covU, pref, c3n — covU precedes c3n, so it wins even though
        // it has the smaller count.
        var report = ReportWith(new Dictionary<string, int> { ["covU"] = 5, ["c3n"] = 100 });

        Assert.Equal("covU", V6NativeOptimizer.MaxViolatedFamily(report));
    }

    [Fact]
    public void MaxViolatedFamily_AvoidSetSkipsAnExcludedHardFamilyAndFallsThroughToTheNextOne()
    {
        var report = ReportWith(new Dictionary<string, int> { ["covU"] = 5, ["pref"] = 3 });

        Assert.Equal("pref", V6NativeOptimizer.MaxViolatedFamily(report, avoid: new HashSet<string> { "covU" }));
    }

    [Fact]
    public void MaxViolatedFamily_AptPeriodicReservationOverridesALargerSoftCountOnItsEligibleRound()
    {
        var report = ReportWith(new Dictionary<string, int> { ["weekly"] = 100, ["apt"] = 1 });

        // round % 3 == 1, not the final round: apt's periodic reservation window.
        Assert.Equal("apt", V6NativeOptimizer.MaxViolatedFamily(report, round: 1, roundsTotal: 10));
    }

    [Fact]
    public void MaxViolatedFamily_CovOPeriodicReservationOverridesALargerSoftCountOnItsEligibleRound()
    {
        var report = ReportWith(new Dictionary<string, int> { ["weekly"] = 100, ["covO"] = 1 });

        // round % 3 == 2, not the final round: covO's periodic reservation window.
        Assert.Equal("covO", V6NativeOptimizer.MaxViolatedFamily(report, round: 2, roundsTotal: 10));
    }

    [Fact]
    public void MaxViolatedFamily_FinalRoundPicksTheSmallerOfAptAndCovOWhenBothAreEligible()
    {
        // Final round makes both apt and covO eligible regardless of round%3. The smaller one (the
        // one most structurally disadvantaged against a plain max-count race) is preferred.
        var reportAptSmaller = ReportWith(new Dictionary<string, int> { ["apt"] = 5, ["covO"] = 2 });
        Assert.Equal("covO", V6NativeOptimizer.MaxViolatedFamily(reportAptSmaller, round: 9, roundsTotal: 10));

        var reportCovOSmaller = ReportWith(new Dictionary<string, int> { ["apt"] = 2, ["covO"] = 5 });
        Assert.Equal("apt", V6NativeOptimizer.MaxViolatedFamily(reportCovOSmaller, round: 9, roundsTotal: 10));
    }

    [Fact]
    public void MaxViolatedFamily_HardFamilyTakesPriorityEvenOnTheFinalRoundWithAptAndCovOPresent()
    {
        var report = ReportWith(new Dictionary<string, int> { ["c3n"] = 1, ["apt"] = 100, ["covO"] = 100 });

        Assert.Equal("c3n", V6NativeOptimizer.MaxViolatedFamily(report, round: 9, roundsTotal: 10));
    }

    [Fact]
    public void MaxViolatedFamily_PeriodicReservationDoesNotApplyOnTheDefaultRound()
    {
        // round omitted (defaults to -1): the `round >= 0` gate keeps apt/covO's periodic
        // reservation from applying at all, so plain max-count selection governs (weekly excluded
        // here specifically so the unconditional weekly→apt override below can't confound this case).
        var report = ReportWith(new Dictionary<string, int> { ["c2"] = 100, ["apt"] = 1, ["covO"] = 1 });

        Assert.Equal("c2", V6NativeOptimizer.MaxViolatedFamily(report));
    }

    [Fact]
    public void MaxViolatedFamily_WeeklySelectionIsOverriddenByAptWheneverAptHasAnyRemaining()
    {
        // This override is unconditional on round (it fires whenever plain max-count selection
        // would have returned "weekly" and apt is non-zero and not avoided) — round is left at its
        // default (-1) to confirm it's independent of the periodic-reservation machinery above.
        var report = ReportWith(new Dictionary<string, int> { ["weekly"] = 50, ["apt"] = 1 });

        Assert.Equal("apt", V6NativeOptimizer.MaxViolatedFamily(report));
    }

    [Fact]
    public void MaxViolatedFamily_WeeklySelectionIsNotOverriddenWhenAptIsZeroOrAvoided()
    {
        var reportAptZero = ReportWith(new Dictionary<string, int> { ["weekly"] = 50, ["apt"] = 0 });
        Assert.Equal("weekly", V6NativeOptimizer.MaxViolatedFamily(reportAptZero));

        var reportAptAvoided = ReportWith(new Dictionary<string, int> { ["weekly"] = 50, ["apt"] = 1 });
        Assert.Equal("weekly", V6NativeOptimizer.MaxViolatedFamily(reportAptAvoided, avoid: new HashSet<string> { "apt" }));
    }

    [Fact]
    public void MaxViolatedFamily_ReturnsTotalWhenAllFamiliesAreZeroOrAvoided()
    {
        Assert.Equal("total", V6NativeOptimizer.MaxViolatedFamily(ReportWith(new Dictionary<string, int>())));

        var report = ReportWith(new Dictionary<string, int> { ["c3n"] = 5 });
        Assert.Equal("total", V6NativeOptimizer.MaxViolatedFamily(report, avoid: new HashSet<string> { "c3n" }));
    }

    // =========================== RsiGenerateHypothesis ===========================

    public static TheoryData<string> Foci => new()
    {
        "covU", "c41", "c41s", "c42", "c42s",
        "low", "high", "c2", "apt", "weekly", "fair",
        "covO", "groupViol", "pref",
        "c1", "total",   // any unrecognised focus falls through to the default (destroyRepairViolations) branch
    };

    [Theory]
    [MemberData(nameof(Foci))]
    public void RsiGenerateHypothesis_ReturnsAValidCopyForEveryFocusBranchWithoutMutatingTheInput(string focus)
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var baseSched = p.InitialAssignment();
        var baseSnapshot = baseSched.Select(row => (int[])row.Clone()).ToArray();
        var report = UnifiedViolationChecker.Check(state, baseSched);

        var result = V6NativeOptimizer.RsiGenerateHypothesis(state, baseSched, report, focus, new JavaRandom(1));

        AssertValidShape(p, result);
        for (int i = 0; i < p.S; i++)
            Assert.Equal(baseSnapshot[i], baseSched[i]);
    }

    // ================================ ApplyCovOFree ================================

    // shift 0="休"(no need), shift 1="X"(need2=1 only) — matches V6SearchOperatorsTest's FindCovOFix fixture.
    private static MagiState CovOState(IReadOnlyList<IReadOnlyList<int>> schedule) => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-01",
        shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "1") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        use2Patterns: true,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        schedule: schedule);

    [Fact]
    public void ApplyCovOFree_RelievesFreelyMovableOverCoverage()
    {
        // Both staff on X (need2=1) -> covO=1, and neither has a wish pinning them there.
        var state = CovOState(new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } });
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var before = UnifiedViolationChecker.Check(state, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covO", 0) > 0);

        var applied = V6NativeOptimizer.ApplyCovOFree(state, sched, new JavaRandom(1));

        Assert.True(applied > 0, "A freely-relievable overstaffing must be found and fixed.");
        var after = UnifiedViolationChecker.Check(state, sched);
        Assert.False(UnifiedViolationChecker.BetterReport(before, after), "Result must never be worse than before the repair.");
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covO", 0));
        AssertValidShape(p, sched);
    }

    [Fact]
    public void ApplyCovOFree_IsNoOpWhenNoOverCoveragePresent()
    {
        var state = CovOState(new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 0 } }); // only s0 on X
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var snapshot = sched.Select(row => (int[])row.Clone()).ToArray();

        var applied = V6NativeOptimizer.ApplyCovOFree(state, sched, new JavaRandom(1));

        Assert.Equal(0, applied);
        for (int i = 0; i < p.S; i++) Assert.Equal(snapshot[i], sched[i]);
    }

    // ================================ ApplyC41Free ================================

    private static MagiState C41State(string l, string u, IReadOnlyList<IReadOnlyList<int>> schedule) => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-01",
        shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        cons41: new List<C41Row> { new("G", "X", l, u) },
        schedule: schedule);

    [Fact]
    public void ApplyC41Free_RelievesGroupOverStaffing()
    {
        // u=0: any staff on shift X (index 1) violates the group's upper bound.
        var state = C41State("", "0", new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 0 } });
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var before = UnifiedViolationChecker.Check(state, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c41", 0) > 0);

        var applied = V6NativeOptimizer.ApplyC41Free(state, sched, new JavaRandom(1), skill: false);

        Assert.True(applied > 0);
        var after = UnifiedViolationChecker.Check(state, sched);
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c41", 0));
        AssertValidShape(p, sched);
    }

    [Fact]
    public void ApplyC41Free_RelievesGroupUnderStaffing()
    {
        // l=1: shift X (index 1) needs at least 1 staff from group G; nobody is on it.
        //
        // [教訓#30] A 2-staff-same-group fixture is a confound here: moving *either* symmetric
        // candidate onto X relieves c41 (weight 1) but simultaneously creates an equal-weight
        // "fair" imbalance within that same 2-member group (X: 1 vs 0), so no candidate is a
        // *strict* improvement and CommitBestMove's Better()-gated CommitBestMove correctly
        // declines to act (applied stays 0) — this is a fixture design flaw, not a production bug
        // (confirmed by running it and inspecting the breakdown before reaching for a "fix" here).
        // A single-member group sidesteps "fair" altogether (m&lt;2 groups are excluded from it).
        var state = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-01",
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("s0", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            cons41: new List<C41Row> { new("G", "X", "1", "") },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0 } });
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var before = UnifiedViolationChecker.Check(state, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c41", 0) > 0);

        var applied = V6NativeOptimizer.ApplyC41Free(state, sched, new JavaRandom(1), skill: false);

        Assert.True(applied > 0);
        var after = UnifiedViolationChecker.Check(state, sched);
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c41", 0));
        AssertValidShape(p, sched);
    }

    // ================================ ApplyC42Free ================================

    private static MagiState C42State(IReadOnlyList<IReadOnlyList<int>> schedule) => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-01",
        shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
        groups: new List<Group> { new("G1", "G1"), new("G2", "G2") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 1) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 1 } },
        cons42: new List<C42Row> { new("G1", "G2", "X", "X") },
        schedule: schedule);

    [Fact]
    public void ApplyC42Free_RelievesForbiddenSameDayPair()
    {
        // s0 (G1) and s1 (G2) both on X the same day -> forbidden pair (X x X across G1/G2).
        var state = C42State(new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } });
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var before = UnifiedViolationChecker.Check(state, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c42", 0) > 0);

        var applied = V6NativeOptimizer.ApplyC42Free(state, sched, new JavaRandom(1), skill: false);

        Assert.True(applied > 0);
        var after = UnifiedViolationChecker.Check(state, sched);
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c42", 0));
        AssertValidShape(p, sched);
    }

    [Fact]
    public void ApplyC42Free_IsNoOpWhenNoForbiddenPairPresent()
    {
        var state = C42State(new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 0 } }); // only s0 on X
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var snapshot = sched.Select(row => (int[])row.Clone()).ToArray();

        var applied = V6NativeOptimizer.ApplyC42Free(state, sched, new JavaRandom(1), skill: false);

        Assert.Equal(0, applied);
        for (int i = 0; i < p.S; i++) Assert.Equal(snapshot[i], sched[i]);
    }
}
