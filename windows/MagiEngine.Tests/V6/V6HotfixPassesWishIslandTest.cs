using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[3.496.0 移植元] 希望島研磨の検証（Kotlin <c>WishIslandPolishTest</c> の3件）。</summary>
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
}
