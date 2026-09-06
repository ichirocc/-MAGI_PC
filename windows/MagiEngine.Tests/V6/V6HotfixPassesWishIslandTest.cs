using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[3.496.0 移植元] 希望島研磨の検証（Kotlin <c>WishIslandPolishTest</c> の3件＋3.498.0 の2件）。</summary>
public class V6HotfixPassesWishIslandTest
{
    private static MagiState Build(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyDictionary<string, int> wishes,
        IReadOnlyDictionary<string, MagiEngine.Model.Range> staffRange) => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-06",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("甲", 0), new("乙", 0), new("丙", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: schedule, wishes: wishes, staffRange: staffRange);
    private static int[][] Sched(MagiState s) => s.Schedule.Select(r => r.ToArray()).ToArray();

    [Fact]
    public void SameDaySwapNextToAWishFixesTheViolationAndKeepsTheWish()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 2, 1, 0, 0, 0 }, new List<int> { 0, 1, 0, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 0, 0 } },
            new Dictionary<string, int> { ["0,2"] = 1 }, new Dictionary<string, MagiEngine.Model.Range> { ["0,2"] = new("0", "0") });
        var before = UnifiedViolationChecker.Check(s, Sched(s));
        Assert.Equal(1, before.Breakdown["high"]);
        var r = V6HotfixPasses.ApplyWishIslandPolish(s, Sched(s));
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(r.Applied >= 1, r.Logs[0].Message);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("high"));
        Assert.Equal(1, r.NewSchedule[0][2]);
        Assert.True(UnifiedViolationChecker.BetterReport(after, before));
        Assert.Contains("同日", r.Logs[0].Message);
    }

    [Fact]
    public void IslandsWithoutNearbyViolationsDoNotActivate()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0, 0, 0 }, new List<int> { 0, 1, 0, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 0, 0 } },
            new Dictionary<string, int> { ["0,2"] = 1 }, new Dictionary<string, MagiEngine.Model.Range>());
        var r = V6HotfixPasses.ApplyWishIslandPolish(s, Sched(s));
        Assert.Equal(0, r.Applied);
        Assert.Contains("起動0件", r.Logs[0].Message);
    }

    [Fact]
    public void MovesThatDoNotImproveTheWishNeighbourhoodAreNotTaken()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0, 0, 0 }, new List<int> { 0, 1, 0, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 0, 2 } },
            new Dictionary<string, int> { ["0,2"] = 1 }, new Dictionary<string, MagiEngine.Model.Range> { ["2,2"] = new("0", "0") });
        var r = V6HotfixPasses.ApplyWishIslandPolish(s, Sched(s));
        Assert.Equal(0, r.Applied);
        Assert.Equal(2, r.NewSchedule[2][5]);
    }

    [Fact]
    public void IslandWhoseViolationWasFixedByAnEarlierIslandDoesNotSpendEvaluations()
    {
        // 甲: 希望A(3日目)、2日目の B が上限0超過。乙: 希望A(5日目)、B の下限1未達。甲↔乙の同日交換で両方消える。
        var sched = new List<IReadOnlyList<int>> { new List<int> { 1, 2, 1, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 1, 0 }, new List<int> { 0, 0, 0, 0, 0, 0 } };
        var both = Build(sched, new Dictionary<string, int> { ["0,2"] = 1, ["1,4"] = 1 },
            new Dictionary<string, MagiEngine.Model.Range> { ["0,2"] = new("0", "0"), ["1,2"] = new("1", "") });
        var prm = new V6HotfixPasses.WishIslandParams(MaxPasses: 1, MaxEvaluations: 100, MinIslandBudget: 50);
        var r = V6HotfixPasses.ApplyWishIslandPolish(both, Sched(both), prm);
        var after = UnifiedViolationChecker.Check(both, r.NewSchedule);
        Assert.Equal(1, r.Applied);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("high") + after.Breakdown.GetValueOrDefault("low"));
        Assert.Contains("起動2件", r.Logs[0].Message);
        var evaluated = int.Parse(System.Text.RegularExpressions.Regex.Match(r.Logs[0].Message, @"正式評価(\d+)").Groups[1].Value);
        var alone = Build(sched, new Dictionary<string, int> { ["0,2"] = 1 }, new Dictionary<string, MagiEngine.Model.Range> { ["0,2"] = new("0", "0") });
        var ra = V6HotfixPasses.ApplyWishIslandPolish(alone, Sched(alone), prm);
        var evaluatedAlone = int.Parse(System.Text.RegularExpressions.Regex.Match(ra.Logs[0].Message, @"正式評価(\d+)").Groups[1].Value);
        Assert.True(evaluated <= evaluatedAlone + 1, $"乙の島は評価されない: {evaluated} vs 甲だけ {evaluatedAlone}");
    }

    [Fact]
    public void MovesThatReduceForbiddenRunsAreNotPrunedEvenWhenTheChangedCellStaysInsideOne()
    {
        // [Android 3.501.0] 禁止 [A,B],[B,A],[A,休]。甲: A B A* 休 → 禁止 3 件。乙(2日目=休)との同日交換で 甲: A 休 A 休 → 2 件。
        //   旧の枝刈りは変更セル(甲,2日目)=休 が A休 の中にあるだけで落としていた＝3→2 に減らす手が正式評価へ届かなかった。
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 2, 1, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 0, 0 } },
            new Dictionary<string, int> { ["0,2"] = 1 }, new Dictionary<string, MagiEngine.Model.Range>()) with
        {
            Cons3n = new List<C3Row> { new(new List<string> { "A", "B" }), new(new List<string> { "B", "A" }), new(new List<string> { "A", "休" }) },
        };
        var before = UnifiedViolationChecker.Check(s, Sched(s));
        Assert.Equal(3, before.Breakdown["c3n"]);
        var r = V6HotfixPasses.ApplyWishIslandPolish(s, Sched(s));
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(r.Applied >= 1, r.Logs[0].Message);
        Assert.Equal(2, after.Breakdown["c3n"]);
        Assert.Equal(1, r.NewSchedule[0][2]);
    }

    [Fact]
    public void Rotate3KeepsAQuarterOfTheIslandBudgetWhenOrdinaryMovesFindNothing()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0, 0, 0 }, new List<int> { 0, 1, 0, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 0, 2 } },
            new Dictionary<string, int> { ["0,2"] = 1 }, new Dictionary<string, MagiEngine.Model.Range> { ["0,1"] = new("0", "0") });
        var r = V6HotfixPasses.ApplyWishIslandPolish(s, Sched(s), new V6HotfixPasses.WishIslandParams(MaxPasses: 1, MaxEvaluations: 8, MinIslandBudget: 8));
        var msg = r.Logs[0].Message;
        Assert.Contains("起動1件", msg);
        var evaluated = int.Parse(System.Text.RegularExpressions.Regex.Match(msg, @"正式評価(\d+)").Groups[1].Value);
        Assert.True(evaluated == 8 || r.Applied >= 1, msg);
    }

    [Fact]
    public void ZeroBudgetAndNegativeParamsDoNothingAndDoNotCrash()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 2, 1, 0, 0, 0 }, new List<int> { 0, 1, 0, 0, 0, 0 }, new List<int> { 0, 0, 0, 0, 0, 0 } },
            new Dictionary<string, int> { ["0,2"] = 1 }, new Dictionary<string, MagiEngine.Model.Range> { ["0,2"] = new("0", "0") });
        var zero = V6HotfixPasses.ApplyWishIslandPolish(s, Sched(s), new V6HotfixPasses.WishIslandParams(MaxEvaluations: 0));
        Assert.Equal(0, zero.Applied);
        Assert.Equal(Sched(s).Select(r => string.Join(",", r)), zero.NewSchedule.Select(r => string.Join(",", r)));
        var negative = V6HotfixPasses.ApplyWishIslandPolish(s, Sched(s),
            new V6HotfixPasses.WishIslandParams(MaxPasses: -1, MaxEvaluations: -5, BeamWidth: 0, BeamDepth: -1, MinIslandBudget: 0, BeamBranchFactor: 0));
        Assert.Equal(0, negative.Applied);
    }
}
