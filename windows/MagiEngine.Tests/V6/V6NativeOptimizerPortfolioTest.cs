using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range collides by simple name with MagiEngine.Model.Range — same alias MinimalState.cs uses.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5d (piece 4): the direct prerequisites of <see cref="V6NativeOptimizer.RunAdaptivePortfolio"/>
/// itself (not yet ported) — <see cref="V6NativeOptimizer.HypothesisStartFor"/>/<see cref="V6NativeOptimizer.ForceDiverseKick"/>,
/// <see cref="V6NativeOptimizer.ForceMaxDistanceKick"/>, <see cref="V6NativeOptimizer.ElitePathRelink"/>,
/// <see cref="V6NativeOptimizer.ConfirmStop"/>, and <see cref="V6NativeOptimizer.AdaptiveEpochStart"/>.
///
/// [実時間コストの注記] <see cref="V6NativeOptimizer.StopConfirmMs"/>（5秒）は Kotlin 原本どおり固定
/// 定数のため注入できない。"停滞シグナルが窓いっぱい真のまま続く" という本物のケースを直接検証する
/// テスト1件だけは、既存の他フェーズのテストクラスが認めている実時間コストの前例（例:
/// <c>V6NativeOptimizerRsiPlusTest</c> のクラス doc comment）にならい、約5秒の実待機を許容する。
/// </summary>
public class V6NativeOptimizerPortfolioTest
{
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

    private static int[][] Snapshot(int[][] schedule) => schedule.Select(row => (int[])row.Clone()).ToArray();

    private static bool SameSchedule(int[][] a, int[][] b) =>
        a.Length == b.Length && a.Zip(b, (ra, rb) => ra.SequenceEqual(rb)).All(x => x);

    // ============================== HypothesisStartFor ==============================

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void HypothesisStartFor_BaselineIndicesReturnAnUnchangedCopy(int index)
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var baseSched = p.InitialAssignment();

        var result = V6NativeOptimizer.HypothesisStartFor(state, baseSched, index, seed: 1L);

        Assert.True(SameSchedule(baseSched, result));
        Assert.NotSame(baseSched, result); // still a genuine copy, not the same array reference.
    }

    public static TheoryData<int> NonBaselineIndices => new() { 1, 2, 3, 5, 6, 7 };

    [Theory]
    [MemberData(nameof(NonBaselineIndices))]
    public void HypothesisStartFor_NonBaselineIndicesReturnAValidScheduleWithoutMutatingTheInput(int index)
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var baseSched = p.InitialAssignment();
        var baseSnapshot = Snapshot(baseSched);

        var result = V6NativeOptimizer.HypothesisStartFor(state, baseSched, index, seed: 3L);

        AssertValidShape(p, result);
        Assert.True(SameSchedule(baseSnapshot, baseSched), "Input schedule must not be mutated.");
    }

    [Fact]
    public void HypothesisStartFor_TerminatesQuicklyWhenNoAlternativeShiftsExist()
    {
        // A single-shift fixture: every AllowedShiftsForStaff() bucket has exactly one entry, so
        // ForceDiverseKick's fallback can never find a differing alternative — this exercises its
        // bounded-attempts loop (not an infinite loop) via HypothesisStartFor's collapse check.
        var state = MinimalState.Build(shifts: new List<Shift> { new("休", "休", "", "") });
        var p = new Problem(state);
        var baseSched = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = V6NativeOptimizer.HypothesisStartFor(state, baseSched, index: 1, seed: 5L);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2_000, $"Bounded-attempts loop must terminate quickly (took {sw.ElapsedMilliseconds}ms).");
        AssertValidShape(p, result);
    }

    // ================================ ForceDiverseKick ================================

    [Fact]
    public void ForceDiverseKick_NeverTouchesWishLockedCells()
    {
        var wishes = new Dictionary<string, int>();
        for (var i = 0; i < 2; i++)
            for (var j = 0; j < 2; j++)
                wishes[$"{i},{j}"] = 1; // shift "A" — achievable for every staff in the default G0 group.
        var state = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-02", wishes: wishes,
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0 }, new List<int> { 0, 0 } });
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var snapshot = Snapshot(sched);

        V6NativeOptimizer.ForceDiverseKick(p, sched, new JavaRandom(1), target: 2);

        Assert.True(SameSchedule(snapshot, sched), "Every cell is wish-locked; none may be touched.");
    }

    [Fact]
    public void ForceDiverseKick_ChangesExactlyTargetCellsWhenUnlockedAlternativesAreAbundant()
    {
        var state = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-03",
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0 }, new List<int> { 0, 0, 0 } });
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var snapshot = Snapshot(sched);

        V6NativeOptimizer.ForceDiverseKick(p, sched, new JavaRandom(1), target: 1);

        var diffCells = 0;
        for (var i = 0; i < p.S; i++)
            for (var j = 0; j < p.T; j++)
                if (sched[i][j] != snapshot[i][j]) diffCells++;
        Assert.Equal(1, diffCells);
        AssertValidShape(p, sched);
    }

    // ============================== ForceMaxDistanceKick ==============================

    private static MagiState ThreeShiftState(IReadOnlyDictionary<string, int>? wishes = null) => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-01",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("s0", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        wishes: wishes,
        schedule: new List<IReadOnlyList<int>> { new List<int> { 0 } });

    [Fact]
    public void ForceMaxDistanceKick_NeverTouchesWishLockedCells()
    {
        var state = ThreeShiftState(wishes: new Dictionary<string, int> { ["0,0"] = 1 });
        var p = new Problem(state);
        var sched = p.InitialAssignment();
        var snapshot = Snapshot(sched);
        var peers = new[] { new[] { new[] { 1 } }, new[] { new[] { 1 } } };

        V6NativeOptimizer.ForceMaxDistanceKick(p, sched, peers, new JavaRandom(1), target: 1);

        Assert.True(SameSchedule(snapshot, sched), "The wish-locked cell must never be touched.");
    }

    [Fact]
    public void ForceMaxDistanceKick_PicksTheLeastFrequentShiftAmongPeers()
    {
        // Both peer boards sit on "A" (index 1) at (0,0); "B" (index 2) is unused by any peer.
        var state = ThreeShiftState();
        var p = new Problem(state);
        var sched = p.InitialAssignment(); // starts on "休" (index 0)
        var peers = new[] { new[] { new[] { 1 } }, new[] { new[] { 1 } } };

        V6NativeOptimizer.ForceMaxDistanceKick(p, sched, peers, new JavaRandom(1), target: 1);

        Assert.Equal(2, sched[0][0]); // must steer toward "B", the strictly least-frequent peer choice.
    }

    // ================================ ElitePathRelink ================================

    private static MagiState PrefWishState(string wishKey, IReadOnlyList<IReadOnlyList<int>> schedule) => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-02",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("s0", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        wishes: new Dictionary<string, int> { [wishKey] = 1 }, // wants shift "A" (index 1) on the given cell.
        schedule: schedule);

    [Fact]
    public void ElitePathRelink_ReturnsInputUnchangedWhenNoAlternativesGiven()
    {
        var state = MinimalState.Build();
        var best = new Problem(state).InitialAssignment();

        var (schedule, report) = V6NativeOptimizer.ElitePathRelink(state, best, Array.Empty<int[][]>(), () => false);

        Assert.True(SameSchedule(best, schedule));
        Assert.Equal(UnifiedViolationChecker.Check(state, best).Hard, report.Hard);
    }

    [Fact]
    public void ElitePathRelink_NeverRegressesBelowTheOriginalBestReport()
    {
        // "alt" differs at a cell but does not resolve the wish; the origin best must be preserved
        // exactly (relinking always re-marches FROM the current best, never chaining regressions in).
        var state = PrefWishState("0,0", new List<IReadOnlyList<int>> { new List<int> { 1, 0 } }); // wish already honored
        var best = new Problem(state).InitialAssignment();
        var bestReport = UnifiedViolationChecker.Check(state, best);
        var alt = new int[][] { new[] { 0, 0 } }; // would UN-honor the wish — strictly worse.

        var (schedule, report) = V6NativeOptimizer.ElitePathRelink(state, best, new[] { alt }, () => false);

        Assert.False(UnifiedViolationChecker.BetterReport(bestReport, report),
            "The result must never be strictly worse than the original best's own report.");
        Assert.True(SameSchedule(best, schedule), "A strictly-worsening alternative must never be adopted.");
    }

    [Fact]
    public void ElitePathRelink_MovesTowardAnImprovingAlternativeAndAdoptsItsBetterReport()
    {
        var state = PrefWishState("0,0", new List<IReadOnlyList<int>> { new List<int> { 0, 0 } }); // wish unmet -> pref violation.
        // Deliberately NOT p.InitialAssignment(): it auto-honors achievable wishes ([3.419.0] `if (w
        // >= 0 && ...) k = w`), which would silently resolve the very violation this fixture needs.
        var best = new int[][] { new[] { 0, 0 } };
        var bestReport = UnifiedViolationChecker.Check(state, best);
        Assert.True(bestReport.Hard > 0, "Fixture sanity: the unmet wish must register as a HARD (pref) violation.");
        var alt = new int[][] { new[] { 1, 0 } }; // honors the wish.

        var (schedule, report) = V6NativeOptimizer.ElitePathRelink(state, best, new[] { alt }, () => false);

        Assert.Equal(0, report.Hard);
        Assert.True(UnifiedViolationChecker.BetterReport(report, bestReport), "The relinked report must be strictly better than the original best's.");
        Assert.Equal(1, schedule[0][0]);
    }

    [Fact]
    public void ElitePathRelink_PrioritizesViolationCellsOverNonViolationCellsWhenMarchingTowardAnAlternative()
    {
        // The wish (and thus the sole violation cell) sits on day 1, which the natural (i, j)
        // enumeration order would visit SECOND. Only the violation-cells-first stable sort makes it
        // get applied before ShouldStop cuts the march off after exactly one inner move.
        var state = PrefWishState("0,1", new List<IReadOnlyList<int>> { new List<int> { 0, 0 } });
        // Deliberately NOT p.InitialAssignment() — see the comment in the test above.
        var best = new int[][] { new[] { 0, 0 } };
        var bestReport = UnifiedViolationChecker.Check(state, best);
        Assert.True(bestReport.Hard > 0, "Fixture sanity: the unmet day-1 wish must register as a HARD (pref) violation.");
        var alt = new int[][] { new[] { 1, 1 } }; // differs at BOTH cells; only day 1 matters for pref.

        var calls = 0;
        bool ShouldStop() => ++calls > 2; // false, false, true, ... : exactly one inner move is allowed.

        var (schedule, report) = V6NativeOptimizer.ElitePathRelink(state, best, new[] { alt }, ShouldStop);

        Assert.Equal(0, report.Hard);
        Assert.Equal(1, schedule[0][1]); // the violation cell (day 1) must have been the one move applied.
        Assert.Equal(0, schedule[0][0]); // day 0 (non-violation, lower priority) must have been left for later.
    }

    [Fact]
    public void ElitePathRelink_StopsAtTheFirstShouldStopCheckAndReturnsTheOriginalBest()
    {
        var state = PrefWishState("0,0", new List<IReadOnlyList<int>> { new List<int> { 0, 0 } });
        var best = new Problem(state).InitialAssignment();
        var alt = new int[][] { new[] { 1, 0 } }; // would improve, but ShouldStop fires before any march.

        var (schedule, report) = V6NativeOptimizer.ElitePathRelink(state, best, new[] { alt }, () => true);

        Assert.True(SameSchedule(best, schedule));
        Assert.Equal(UnifiedViolationChecker.Check(state, best).Hard, report.Hard);
    }

    // =================================== ConfirmStop ===================================

    [Fact]
    public async Task ConfirmStop_StopIsFinalTrueReturnsImmediately()
    {
        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.ConfirmStop(() => true, deadline: long.MaxValue, stopIsFinal: () => true);
        sw.Stop();

        Assert.True(result);
        Assert.True(sw.ElapsedMilliseconds < 500, $"stopIsFinal=true must short-circuit with no waiting (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task ConfirmStop_PastDeadlineReturnsTrueWithoutWaitingTheFullWindow()
    {
        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.ConfirmStop(() => true, deadline: long.MinValue, stopIsFinal: () => false);
        sw.Stop();

        Assert.True(result);
        Assert.True(sw.ElapsedMilliseconds < 500, $"An already-exceeded deadline must return before the first poll (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task ConfirmStop_ShouldStopFlipsFalsePartwayThroughReturnsFalse()
    {
        // The core reason this function exists: a stagnation signal that flips back false partway
        // through the confirmation window must be treated as a transient blip, not a real stall.
        var calls = 0;
        bool ShouldStop() { calls++; return calls <= 1; } // true once, then false from the second poll on.

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.ConfirmStop(ShouldStop, deadline: long.MaxValue, stopIsFinal: () => false);
        sw.Stop();

        Assert.False(result);
        Assert.True(sw.ElapsedMilliseconds < 3_000, $"A blip must resolve within a couple of poll intervals, not the full window (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task ConfirmStop_ShouldStopStaysTrueForTheFullWindowReturnsTrue()
    {
        // [実時間コスト] StopConfirmMs=5秒は Kotlin 原本どおり固定定数のため注入できない。この本物の
        // 「停滞シグナルが窓いっぱい真のまま続く」経路だけは実待機を伴う。
        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.ConfirmStop(() => true, deadline: long.MaxValue, stopIsFinal: () => false);
        sw.Stop();

        Assert.True(result);
        Assert.True(sw.ElapsedMilliseconds >= V6NativeOptimizer.StopConfirmMs,
            $"A genuine stall must wait out the full confirmation window (took {sw.ElapsedMilliseconds}ms, window={V6NativeOptimizer.StopConfirmMs}ms).");
    }

    [Fact]
    public async Task ConfirmStop_CancellationDuringTheDelayReturnsTrueWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50)); // fires mid-poll, not before the loop starts.

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.ConfirmStop(() => true, deadline: long.MaxValue, stopIsFinal: () => false, cts.Token);
        sw.Stop();

        Assert.True(result);
        Assert.True(sw.ElapsedMilliseconds < 2_000, $"Cancellation must be treated as a genuine (monotonic) stop without riding out the full window (took {sw.ElapsedMilliseconds}ms).");
    }

    // ================================ AdaptiveEpochStart ================================

    [Fact]
    public void AdaptiveEpochStart_BaselineRefineReturnsACopyOfLocalTrajectoryNotGlobalBest()
    {
        var state = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-01",
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0 }, new List<int> { 0 } });
        var globalBest = new int[][] { new[] { 0 }, new[] { 0 } };
        var localTrajectory = new int[][] { new[] { 1 }, new[] { 1 } };
        var assignment = new HypothesisEpochAssignment(HypothesisEpochRole.BaselineRefine, V6Algorithm.V5, 1);

        var result = V6NativeOptimizer.AdaptiveEpochStart(state, globalBest, localTrajectory, Array.Empty<int[][]>(), assignment, seed: 1L, () => false);

        Assert.True(SameSchedule(localTrajectory, result));
        Assert.False(SameSchedule(globalBest, result));
    }

    [Fact]
    public void AdaptiveEpochStart_EliteRelinkMovesTowardAFartherPeerWhenOneImproves()
    {
        var state = PrefWishState("0,0", new List<IReadOnlyList<int>> { new List<int> { 0, 0 } });
        var globalBest = new int[][] { new[] { 0, 0 } };
        var peers = new[] { new int[][] { new[] { 1, 0 } } }; // honors the wish -> farther and better.
        var assignment = new HypothesisEpochAssignment(HypothesisEpochRole.EliteRelink, V6Algorithm.Portfolio, 1);

        var result = V6NativeOptimizer.AdaptiveEpochStart(state, globalBest, globalBest, peers, assignment, seed: 1L, () => false);

        Assert.Equal(1, result[0][0]);
        Assert.Equal(0, UnifiedViolationChecker.Check(state, result).Hard);
    }

    [Fact]
    public void AdaptiveEpochStart_EliteRelinkFallsBackToHypothesisStartWhenNoPeerIsFartherThanGlobalBest()
    {
        var state = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-03",
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0 }, new List<int> { 0, 0, 0 } });
        var p = new Problem(state);
        var globalBest = p.InitialAssignment();
        // Every peer is identical to globalBest -> ScheduleDistance is 0 for all -> no alternatives
        // survive the `> 0` filter -> ElitePathRelink is called with an empty list -> relinked
        // equals globalBest exactly -> falls through to HypothesisStartFor(index: 7).
        var peers = new[] { Snapshot(globalBest), Snapshot(globalBest) };
        var assignment = new HypothesisEpochAssignment(HypothesisEpochRole.EliteRelink, V6Algorithm.Portfolio, 1);

        var result = V6NativeOptimizer.AdaptiveEpochStart(state, globalBest, globalBest, peers, assignment, seed: 9L, () => false);

        AssertValidShape(p, result);
    }

    public static TheoryData<HypothesisEpochRole> AllRoles => new()
    {
        HypothesisEpochRole.BaselineRefine, HypothesisEpochRole.EliteRelink, HypothesisEpochRole.DayBlockAlns,
        HypothesisEpochRole.HardFamilyRsi, HypothesisEpochRole.HardDebtRsiPlus, HypothesisEpochRole.LargeDestroyAlns,
        HypothesisEpochRole.PersonalRsi, HypothesisEpochRole.MaxDistanceRsiPlus,
    };

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void AdaptiveEpochStart_EveryRoleReturnsAValidScheduleWithoutMutatingInputs(HypothesisEpochRole role)
    {
        var state = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-03",
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0 }, new List<int> { 0, 0, 0 } });
        var p = new Problem(state);
        var globalBest = p.InitialAssignment();
        var globalBestSnapshot = Snapshot(globalBest);
        var localTrajectory = Snapshot(globalBest);
        var peers = new[] { Snapshot(globalBest), Snapshot(globalBest) };
        var assignment = new HypothesisEpochAssignment(role, V6Algorithm.Portfolio, 1);

        var result = V6NativeOptimizer.AdaptiveEpochStart(state, globalBest, localTrajectory, peers, assignment, seed: 11L, () => false);

        AssertValidShape(p, result);
        Assert.True(SameSchedule(globalBestSnapshot, globalBest), "globalBest must not be mutated.");
    }
}
