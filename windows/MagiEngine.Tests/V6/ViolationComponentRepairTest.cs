using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Kotlin <c>ViolationComponentRepairTest</c>（3.505.0）の忠実な移植。材料は各パスの拒否候補、採用は正式チェッカー＋厳密ピン検査。
/// 盤面は CombinatorialRepairTest と同じ最小盤面（X の P 超過と Y の D 不足は単独ではタイで不採用、束ねると apt が 2 件消える）。
/// </summary>
public class ViolationComponentRepairTest
{
    private static bool IsBetterLocal(ViolationReport a, ViolationReport b)
    {
        if (a.Hard != b.Hard) return a.Hard < b.Hard;
        if (a.Total != b.Total) return a.Total < b.Total;
        return a.WeightedScore < b.WeightedScore;
    }

    private static MagiState CombineTwoRejectedState() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-01",
        shifts: new List<Shift> { new("休", "休", "", ""), new("P", "P", "", ""), new("Qres", "Qres", "", ""), new("D", "D", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("X", 0), new("Y", 0), new("W1", 0), new("W2", 0) }, use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "0", "", "1" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 2 }, new List<int> { 0 }, new List<int> { 0 } },
        wishes: new Dictionary<string, int>(),
        staffRange: new Dictionary<string, Range> { ["0,3"] = new("", "0"), ["2,3"] = new("", "0"), ["3,3"] = new("", "0") },
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
        cons41: new List<C41Row> { new("G0", "Qres", "1", "1") }, cons42: new List<C42Row>());

    private static int[][] Work(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();

    [Fact]
    public void CombinesCandidatesRejectedByDifferentPassesIntoOneTransaction()
    {
        var st = CombineTwoRejectedState();
        var work = Work(st);
        var before = UnifiedViolationChecker.Check(st, work);
        Assert.Equal(2, before.Breakdown.GetValueOrDefault("apt"));
        var candX = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 2 } }, "AptChain", "X");
        var candY = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 1, 0, 3 } }, "tryRelocate", "Y");
        {
            var w = Work(st); w[0][0] = 2;
            Assert.False(IsBetterLocal(UnifiedViolationChecker.Check(st, w), before), "単独は不採用(タイ)");
        }
        var r = ViolationComponentRepair.Repair(st, work, new[] { candX, candY });
        var after = UnifiedViolationChecker.Check(st, r.NewSchedule);
        Assert.Equal(1, r.Applied);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("apt", -1));
        Assert.Equal(0, after.Hard);
        Assert.Equal(2, r.NewSchedule[0][0]); Assert.Equal(3, r.NewSchedule[1][0]);
        Assert.Contains("採用1件", r.Logs[0].Message);
        Assert.Contains("k=2", r.Logs[0].Message);
    }

    [Fact]
    public void NeverWorsensAndLeavesBoardUntouchedWhenNoTransactionImproves()
    {
        var st = CombineTwoRejectedState();
        var work = Work(st);
        var snapshot = work.Copy2D();
        // 単独でも束ねても悪化する候補（X と W1 を D へ＝staffRange hi=0 を破り high が増える）。
        var bad = new[]
        {
            new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 3 } }, "a", "X→D"),
            new CombinatorialRepair.Candidate(new List<int[]> { new[] { 2, 0, 3 } }, "c", "W1→D"),
        };
        var r = ViolationComponentRepair.Repair(st, work, bad);
        Assert.Equal(0, r.Applied);
        for (var i = 0; i < snapshot.Length; i++) Assert.Equal(snapshot[i], r.NewSchedule[i]);
        Assert.Equal(r.BeforeTotal, r.AfterTotal);
    }

    [Fact]
    public void AnchorSetsPutPatchesTouchingTheViolationFirstAndHelpersSharingAStaffOrDayAfter()
    {
        var st = CombineTwoRejectedState();
        var rep = UnifiedViolationChecker.Check(st, Work(st));
        var anchors = ViolationComponentRepair.Anchors(rep);
        Assert.Contains(anchors, a => a.Family.StartsWith("apt", StringComparison.Ordinal) && a.Staff == 0);
        static ViolationComponentRepair.Patch P(params int[][] cells) => new(cells, "m", "");
        var x = P(new[] { 0, 0, 2 });        // X を Qres へ＝起点 apt(X) を触る主候補
        var y = P(new[] { 1, 0, 3 });        // Y を D へ＝X と日 0 を共有する助候補
        var far = P(new[] { 3, 0, 1 });      // W2＝日 0 を共有するので助候補
        var sets = ViolationComponentRepair.AnchorSets(anchors.Where(a => a.Staff == 0 && a.Day < 0).ToList(), new[] { x, y, far }, cap: 2);
        Assert.Single(sets);
        Assert.Equal(new[] { 0, 1 }, sets[0].Ids);   // 主候補が先、助候補は cap まで
        Assert.True(x.Overlaps(P(new[] { 0, 0, 3 })));
        Assert.False(x.Overlaps(y));
    }

    [Fact]
    public void QualityVectorOrdersHardCountBeforeAnyWeight()
    {
        var st = CombineTwoRejectedState();
        var rep = UnifiedViolationChecker.Check(st, Work(st));
        var qv = ViolationComponentRepair.QualityVector.Of(rep, changedCells: 3);
        Assert.Equal(rep.Hard, qv.HardCount);
        Assert.Equal(0.0, qv.HardWeighted);
        Assert.True(qv.SoftWeighted > 0.0);
        Assert.Equal(3, qv.ChangedCells);
        var worseHard = qv with { HardCount = 1, SoftWeighted = 0.0, ChangedCells = 0 };
        Assert.True(qv.CompareTo(worseHard) < 0);
        var fewerChanges = qv with { ChangedCells = 1 };
        Assert.True(fewerChanges.CompareTo(qv) < 0);
    }

    [Fact]
    public void CombineAndApplyHandsUnusedCandidatesToTheLeftoverSink()
    {
        var st = CombineTwoRejectedState();
        var work = Work(st);
        var before = UnifiedViolationChecker.Check(st, work);
        // 同じセルを触る 2 候補＝重複セルで結合されず、どちらも残る。
        var dup = new List<CombinatorialRepair.Candidate>
        {
            new(new List<int[]> { new[] { 0, 0, 1 } }, "dup", "1"),
            new(new List<int[]> { new[] { 0, 0, 3 } }, "dup", "2"),
        };
        var leftover = new List<CombinatorialRepair.Candidate>();
        CombinatorialRepair.CombineAndApply(st, work, before, dup, IsBetterLocal, leftover: leftover);
        Assert.Equal(2, leftover.Count);
    }

    [Fact]
    public void PostOptimizationWithComponentRepairEnabledRunsAndNeverWorsensTheBoard()
    {
        var st = CombineTwoRejectedState();
        var sched = Work(st);
        var before = UnifiedViolationChecker.Check(st, sched);
        var prm = new V6HotfixPasses.PostOptimizationParams(ComponentRepairEnabled: true, MaxRounds: 1);
        var r = V6HotfixPasses.RunPostOptimization(st, sched.Copy2D(), "t", seed: 7L, parameters: prm);
        Assert.True(r.Report.Hard <= before.Hard);
        Assert.False(UnifiedViolationChecker.BetterReport(before, r.Report));
    }
}
