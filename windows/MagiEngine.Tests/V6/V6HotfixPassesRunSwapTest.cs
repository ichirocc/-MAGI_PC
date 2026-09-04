using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.494.0 移植元] <see cref="V6HotfixPasses.ApplyRunSwapPolish"/> の検証（Kotlin <c>RunSwapPolishTest</c> の2件）。
/// シフトの意味を使わないことを2つの形で固定: 挟まれセル（禁止の並び N→E）と、禁止の並びが一切無い休連の交換で low 解消。
/// </summary>
public class V6HotfixPassesRunSwapTest
{
    private static MagiState Build(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyDictionary<string, int> wishes,
        IReadOnlyDictionary<string, MagiEngine.Model.Range> staffRange, IReadOnlyList<C3Row> cons3n) => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-06",
        shifts: new List<Shift> { new("休", "休", "", ""), new("N", "N", "", ""), new("E", "E", "", "") },
        groups: new List<Group> { new("A", "A"), new("B", "B") },
        staffList: new List<Staff> { new("甲", 0), new("乙", 1) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" }, new List<string> { "", "", "" } },
        schedule: schedule, wishes: wishes, staffRange: staffRange, cons3n: cons3n);

    [Fact]
    public void ExchangingTheAdjacentRunFreesTheSandwichedDay()
    {
        var s = Build(
            new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0, 2, 2, 2 }, new List<int> { 2, 2, 0, 2, 1, 1 } },
            new Dictionary<string, int> { ["0,3"] = 2 },
            new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new("0", "0") },
            new List<C3Row> { new(new List<string> { "N", "E", "", "", "" }) });
        var sched = s.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(s, sched);
        Assert.Equal(1, before.Breakdown["high"]);
        var r = V6HotfixPasses.ApplyRunSwapPolish(s, sched);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(r.Applied >= 1, r.Logs[0].Message);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("high"));
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n"));
        Assert.Equal(2, r.NewSchedule[0][3]);
        Assert.Equal(2, r.NewSchedule[0].Count(v => v == 1));
        Assert.Equal(2, r.NewSchedule[1].Count(v => v == 1));
        Assert.True(UnifiedViolationChecker.BetterReport(after, before));
    }

    [Fact]
    public void ExchangingARestRunFixesACountShortfallWithoutAnyForbiddenRule()
    {
        var s = Build(
            new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2, 2, 2 }, new List<int> { 0, 0, 1, 1, 2, 2 } },
            new Dictionary<string, int>(),
            new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new("2", "") },
            new List<C3Row>());
        var sched = s.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(s, sched);
        Assert.Equal(2, before.Breakdown["low"]);
        var r = V6HotfixPasses.ApplyRunSwapPolish(s, sched);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(r.Applied >= 1, r.Logs[0].Message);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("low"));
        Assert.Equal(2, r.NewSchedule[0].Count(v => v == 0));
        Assert.True(UnifiedViolationChecker.BetterReport(after, before));
    }
}
