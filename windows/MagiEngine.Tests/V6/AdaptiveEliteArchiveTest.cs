using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5d (piece 2): <see cref="AdaptiveEliteArchive"/> — the thread-safe bounded elite pool
/// consumed by <see cref="V6NativeOptimizer.RunAdaptivePortfolio"/> (next, highest-risk piece of
/// phase 5d). Tests are pure over <c>int[][]</c>/<see cref="ViolationReport"/> — no
/// <c>MagiState</c>/<c>Problem</c> needed, since none of this class's logic touches them.
/// </summary>
public class AdaptiveEliteArchiveTest
{
    private static ViolationReport MakeReport(int hard, int total, double weighted, int soft = 0) =>
        new(
            Violations: new Dictionary<string, string>(),
            NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(),
            Breakdown: new Dictionary<string, int>(),
            Total: total,
            Hard: hard,
            Soft: soft,
            WeightedScore: weighted);

    private static int[][] Board(params int[][] rows) => rows;

    // ── ScheduleDistance ──

    [Fact]
    public void ScheduleDistance_IdenticalBoardsAreZero()
    {
        var a = Board(new[] { 1, 2, 3 }, new[] { 4, 5, 6 });
        var b = Board(new[] { 1, 2, 3 }, new[] { 4, 5, 6 });
        Assert.Equal(0, AdaptiveEliteArchive.ScheduleDistance(a, b));
    }

    [Fact]
    public void ScheduleDistance_CountsDifferingCells()
    {
        var a = Board(new[] { 1, 2, 3 }, new[] { 4, 5, 6 });
        var b = Board(new[] { 1, 9, 3 }, new[] { 4, 5, 9 });
        Assert.Equal(2, AdaptiveEliteArchive.ScheduleDistance(a, b));
    }

    [Fact]
    public void ScheduleDistance_RaggedRowsAddTheLengthDifference()
    {
        var a = Board(new[] { 1, 2, 3, 4 });
        var b = Board(new[] { 1, 2 });
        // 2 overlapping cells match (0 diffs) + |4-2| length diff = 2.
        Assert.Equal(2, AdaptiveEliteArchive.ScheduleDistance(a, b));
    }

    [Fact]
    public void ScheduleDistance_ExtraRowsOnEitherSideCountAllTheirCells()
    {
        var a = Board(new[] { 1, 2 }, new[] { 3, 4 });
        var b = Board(new[] { 1, 2 });
        // row 0 matches (0) + a's extra row (2 cells) = 2.
        Assert.Equal(2, AdaptiveEliteArchive.ScheduleDistance(a, b));
        // Symmetric.
        Assert.Equal(2, AdaptiveEliteArchive.ScheduleDistance(b, a));
    }

    // ── SameSchedule / ScheduleHash ──

    [Fact]
    public void SameSchedule_TrueForIdenticalContentFalseOtherwise()
    {
        var a = Board(new[] { 1, 2 }, new[] { 3, 4 });
        var bSame = Board(new[] { 1, 2 }, new[] { 3, 4 });
        var bDiffCell = Board(new[] { 1, 9 }, new[] { 3, 4 });
        var bDiffRows = Board(new[] { 1, 2 });

        Assert.True(AdaptiveEliteArchive.SameSchedule(a, bSame));
        Assert.False(AdaptiveEliteArchive.SameSchedule(a, bDiffCell));
        Assert.False(AdaptiveEliteArchive.SameSchedule(a, bDiffRows));
    }

    [Fact]
    public void ScheduleHash_IsDeterministicAndDistinguishesDifferentBoards()
    {
        var a = Board(new[] { 1, 2, 3 }, new[] { 4, 5, 6 });
        var aAgain = Board(new[] { 1, 2, 3 }, new[] { 4, 5, 6 });
        var b = Board(new[] { 1, 2, 9 }, new[] { 4, 5, 6 });
        var c = Board(new[] { 4, 5, 6 }, new[] { 1, 2, 3 }); // same cells, rows swapped.

        Assert.Equal(AdaptiveEliteArchive.ScheduleHash(a), AdaptiveEliteArchive.ScheduleHash(aAgain));
        Assert.NotEqual(AdaptiveEliteArchive.ScheduleHash(a), AdaptiveEliteArchive.ScheduleHash(b));
        Assert.NotEqual(AdaptiveEliteArchive.ScheduleHash(a), AdaptiveEliteArchive.ScheduleHash(c));
    }

    // ── CompareReports / Better / SameObjective ──

    [Fact]
    public void Better_DelegatesToTheSharedCheckerComparerHardFirst()
    {
        var worseHard = MakeReport(hard: 2, total: 1, weighted: 1);
        var betterHard = MakeReport(hard: 1, total: 999, weighted: 999);
        Assert.True(AdaptiveEliteArchive.Better(betterHard, worseHard));
        Assert.False(AdaptiveEliteArchive.Better(worseHard, betterHard));
        Assert.Equal(UnifiedViolationChecker.BetterReport(betterHard, worseHard), AdaptiveEliteArchive.Better(betterHard, worseHard));
    }

    [Fact]
    public void SameObjective_TrueOnlyWhenHardTotalAndWeightedAllMatch()
    {
        var a = MakeReport(hard: 1, total: 10, weighted: 5.5);
        var same = MakeReport(hard: 1, total: 10, weighted: 5.5);
        var diffWeighted = MakeReport(hard: 1, total: 10, weighted: 5.6);
        Assert.True(AdaptiveEliteArchive.SameObjective(a, same));
        Assert.False(AdaptiveEliteArchive.SameObjective(a, diffWeighted));
    }

    // ── AdaptiveElite.Create tier default ──

    [Fact]
    public void AdaptiveElite_CreateDefaultsTierFromBridgeFlag()
    {
        var sched = Board(new[] { 0 });
        var report = MakeReport(0, 0, 0);
        var quality = AdaptiveElite.Create(sched, report, HypothesisEpochRole.BaselineRefine, worker: 0, epoch: 0, bridge: false);
        var bridge = AdaptiveElite.Create(sched, report, HypothesisEpochRole.BaselineRefine, worker: 0, epoch: 0, bridge: true);
        Assert.Equal(AdaptiveEliteTier.Quality, quality.Tier);
        Assert.Equal(AdaptiveEliteTier.Bridge, bridge.Tier);
    }

    // ── Register / Size / Clear / AllForTest ──

    [Fact]
    public void Register_NewDistinctScheduleIsAdded()
    {
        var archive = new AdaptiveEliteArchive();
        var sched = Board(new[] { 1, 2 });
        var report = MakeReport(hard: 0, total: 5, weighted: 5);

        archive.Register(sched, report, HypothesisEpochRole.DayBlockAlns, worker: 3, epoch: 1, bridge: false);

        Assert.Equal(1, archive.Size());
        var e = Assert.Single(archive.AllForTest());
        Assert.True(AdaptiveEliteArchive.SameSchedule(sched, e.Schedule));
        Assert.Equal(HypothesisEpochRole.DayBlockAlns, e.Role);
        Assert.Equal(3, e.Worker);
        Assert.Equal(1, e.Epoch);
        Assert.False(e.Bridge);
        Assert.Equal(AdaptiveEliteTier.Quality, e.Tier);
    }

    [Fact]
    public void Register_IdenticalScheduleWithWorseReportDoesNotReplace()
    {
        var archive = new AdaptiveEliteArchive();
        var sched1 = Board(new[] { 1, 2 });
        var sched2 = Board(new[] { 1, 2 }); // same content, different array instance.
        var good = MakeReport(hard: 0, total: 5, weighted: 5);
        var worse = MakeReport(hard: 1, total: 5, weighted: 5);

        archive.Register(sched1, good, HypothesisEpochRole.BaselineRefine, 0, 0, bridge: false);
        archive.Register(sched2, worse, HypothesisEpochRole.BaselineRefine, 0, 1, bridge: false);

        Assert.Equal(1, archive.Size());
        Assert.Equal(0, Assert.Single(archive.AllForTest()).Report.Hard); // still the good report.
    }

    [Fact]
    public void Register_IdenticalScheduleWithBetterReportReplaces()
    {
        var archive = new AdaptiveEliteArchive();
        var sched1 = Board(new[] { 1, 2 });
        var sched2 = Board(new[] { 1, 2 });
        var worse = MakeReport(hard: 1, total: 5, weighted: 5);
        var better = MakeReport(hard: 0, total: 5, weighted: 5);

        archive.Register(sched1, worse, HypothesisEpochRole.BaselineRefine, 0, 0, bridge: false);
        archive.Register(sched2, better, HypothesisEpochRole.BaselineRefine, 0, 1, bridge: true);

        Assert.Equal(1, archive.Size());
        var e = Assert.Single(archive.AllForTest());
        Assert.Equal(0, e.Report.Hard);
        Assert.True(e.Bridge); // replacement carries the newly-registered entry's own bridge flag.
    }

    [Fact]
    public void Register_SameObjectiveUpgradesFromBridgeToNonBridgeButNotTheReverse()
    {
        var archive = new AdaptiveEliteArchive();
        var sched1 = Board(new[] { 1, 2 });
        var sched2 = Board(new[] { 1, 2 });
        var sched3 = Board(new[] { 1, 2 });
        var report = MakeReport(hard: 0, total: 5, weighted: 5);

        archive.Register(sched1, report, HypothesisEpochRole.BaselineRefine, 0, 0, bridge: true);
        archive.Register(sched2, report, HypothesisEpochRole.BaselineRefine, 0, 1, bridge: false); // upgrades.
        Assert.False(Assert.Single(archive.AllForTest()).Bridge);

        archive.Register(sched3, report, HypothesisEpochRole.BaselineRefine, 0, 2, bridge: true); // must NOT downgrade.
        Assert.False(Assert.Single(archive.AllForTest()).Bridge);
    }

    [Fact]
    public void Clear_EmptiesTheArchive()
    {
        var archive = new AdaptiveEliteArchive();
        archive.Register(Board(new[] { 1 }), MakeReport(0, 0, 0), HypothesisEpochRole.BaselineRefine, 0, 0, false);
        Assert.Equal(1, archive.Size());
        archive.Clear();
        Assert.Equal(0, archive.Size());
        Assert.Empty(archive.AllForTest());
    }

    [Fact]
    public void Register_CompactsWhenRawCapacityIsExceededButKeepsTheBestEntry()
    {
        var archive = new AdaptiveEliteArchive(rawCapacity: 8);
        for (var i = 0; i < 20; i++)
        {
            var sched = Board(new[] { i });
            // Give each entry a distinct hard/total so quality ranking is unambiguous; i=0 is best (hard=0).
            var report = MakeReport(hard: i, total: i, weighted: i);
            archive.Register(sched, report, HypothesisEpochRole.BaselineRefine, worker: i, epoch: 0, bridge: false);
        }

        Assert.True(archive.Size() <= 8, $"Archive should have compacted down to <= rawCapacity, got {archive.Size()}.");
        Assert.Contains(archive.AllForTest(), e => e.Report.Hard == 0);
    }

    // ── Snapshot ──

    [Fact]
    public void Snapshot_EmptyArchiveReturnsEmptyList()
    {
        var archive = new AdaptiveEliteArchive();
        var result = archive.Snapshot(Board(new[] { 0 }), MakeReport(0, 0, 0));
        Assert.Empty(result);
    }

    [Fact]
    public void Snapshot_QualityTierPicksBestNonBridgeEntriesWithinReferenceHard()
    {
        var archive = new AdaptiveEliteArchive();
        var reference = MakeReport(hard: 1, total: 100, weighted: 100);
        // Two eligible (hard<=1), one ineligible (hard=2 > reference.Hard+0... actually quality filter is hard<=reference.hard).
        archive.Register(Board(new[] { 1 }), MakeReport(hard: 0, total: 10, weighted: 10), HypothesisEpochRole.BaselineRefine, 0, 0, false);
        archive.Register(Board(new[] { 2 }), MakeReport(hard: 1, total: 20, weighted: 20), HypothesisEpochRole.BaselineRefine, 1, 0, false);
        archive.Register(Board(new[] { 3 }), MakeReport(hard: 5, total: 5, weighted: 5), HypothesisEpochRole.BaselineRefine, 2, 0, false);

        var snap = archive.Snapshot(Board(new[] { 99 }), reference, maxQuality: 4, maxDiversity: 0, maxBridge: 0);

        Assert.Equal(2, snap.Count);
        Assert.All(snap, e => Assert.True(e.Report.Hard <= reference.Hard));
        Assert.All(snap, e => Assert.Equal(AdaptiveEliteTier.Quality, e.Tier));
        // Best (hard=0) sorts first.
        Assert.Equal(0, snap[0].Report.Hard);
    }

    [Fact]
    public void Snapshot_BridgeTierIncludesFlaggedOrExactlyOneAboveReferenceHard()
    {
        var archive = new AdaptiveEliteArchive();
        var reference = MakeReport(hard: 0, total: 100, weighted: 100);
        // Explicitly flagged bridge, hard equal to reference (still counts via the bridge flag).
        archive.Register(Board(new[] { 1 }), MakeReport(hard: 0, total: 10, weighted: 10), HypothesisEpochRole.BaselineRefine, 0, 0, bridge: true);
        // Not flagged, but exactly hard+1 above reference -> eligible as bridge material.
        archive.Register(Board(new[] { 2 }), MakeReport(hard: 1, total: 5, weighted: 5), HypothesisEpochRole.BaselineRefine, 1, 0, bridge: false);
        // Two above reference hard -> must be excluded even though not flagged bridge.
        archive.Register(Board(new[] { 3 }), MakeReport(hard: 2, total: 1, weighted: 1), HypothesisEpochRole.BaselineRefine, 2, 0, bridge: false);

        var snap = archive.Snapshot(Board(new[] { 99 }), reference, maxQuality: 0, maxDiversity: 0, maxBridge: 4);

        Assert.Equal(2, snap.Count);
        Assert.All(snap, e => Assert.Equal(AdaptiveEliteTier.Bridge, e.Tier));
        Assert.DoesNotContain(snap, e => e.Report.Hard == 2);
    }

    [Fact]
    public void Snapshot_DiversityTierExcludesSchedulesAlreadySelectedInQuality()
    {
        var archive = new AdaptiveEliteArchive();
        var reference = MakeReport(hard: 0, total: 100, weighted: 100);
        var sharedSchedule = Board(new[] { 1, 1 });
        // This entry will be picked by quality (hard=0, best) — must not also be picked by diversity.
        archive.Register(sharedSchedule, MakeReport(hard: 0, total: 10, weighted: 10), HypothesisEpochRole.BaselineRefine, 0, 0, false);
        // A second, distinct-schedule entry eligible for diversity (hard<=reference.hard+1).
        archive.Register(Board(new[] { 9, 9 }), MakeReport(hard: 1, total: 20, weighted: 20), HypothesisEpochRole.BaselineRefine, 1, 0, false);

        var snap = archive.Snapshot(Board(new[] { 99, 99 }), reference, maxQuality: 4, maxDiversity: 4, maxBridge: 0);

        // Both entries appear exactly once overall (no duplicate schedule across tiers).
        Assert.Equal(2, snap.Count);
        var distinctCount = snap.Select(e => string.Join(",", e.Schedule.SelectMany(r => r))).Distinct().Count();
        Assert.Equal(2, distinctCount);
    }

    [Fact]
    public void Snapshot_ReturnsIndependentScheduleCopiesNotAliasedToInternalState()
    {
        var archive = new AdaptiveEliteArchive();
        var sched = Board(new[] { 1, 2 });
        archive.Register(sched, MakeReport(0, 0, 0), HypothesisEpochRole.BaselineRefine, 0, 0, false);

        var snap = archive.Snapshot(Board(new[] { 9, 9 }), MakeReport(0, 0, 0));
        snap[0].Schedule[0][0] = 999;

        // Internal archive state must be unaffected by mutating the snapshot's copy.
        var after = archive.AllForTest();
        Assert.Equal(1, after[0].Schedule[0][0]);
    }
}
