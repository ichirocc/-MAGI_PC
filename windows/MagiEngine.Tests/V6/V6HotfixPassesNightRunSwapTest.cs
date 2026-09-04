using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.493.0 移植元] <see cref="V6HotfixPasses.ApplyNightRunSwapPolish"/> の検証（Kotlin <c>NightRunSwapPolishTest</c> の2件）。
/// 挟まれセル（前日=夜勤 N、翌日=希望固定の早番 E、禁止の並び N→E）で当日に置けるのが 休 だけ＝休の上限0 を破る局面。
/// </summary>
public class V6HotfixPassesNightRunSwapTest
{
    // 0=休 1=N(夜勤) 2=E(早番)。被覆は使わない（need 空）。
    private static MagiState State() => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-06",
        shifts: new List<Shift> { new("休", "休", "", ""), new("N", "N", "", ""), new("E", "E", "", "") },
        groups: new List<Group> { new("A", "A"), new("B", "B") },
        staffList: new List<Staff> { new("甲", 0), new("乙", 1) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" }, new List<string> { "", "", "" } },
        // 甲: N N 休 E* E E（3日目が挟まれセル） / 乙: E E 休 E N N
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0, 2, 2, 2 }, new List<int> { 2, 2, 0, 2, 1, 1 } },
        wishes: new Dictionary<string, int> { ["0,3"] = 2 },
        staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new MagiEngine.Model.Range("0", "0") },
        cons3n: new List<C3Row> { new(new List<string> { "N", "E", "", "", "" }) });

    [Fact]
    public void ExchangingTheNightRunFreesTheSandwichedDay()
    {
        var s = State();
        var sched = s.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(s, sched);
        Assert.Equal(1, before.Breakdown["high"]);
        var r = V6HotfixPasses.ApplyNightRunSwapPolish(s, sched);
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
    public void SkipsCleanlyWithoutForbiddenSequences()
    {
        var s = State() with { Cons3n = new List<C3Row>() };
        var sched = s.Schedule.Select(r => r.ToArray()).ToArray();
        var r = V6HotfixPasses.ApplyNightRunSwapPolish(s, sched);
        Assert.Equal(0, r.Applied);
        Assert.Contains("スキップ", r.Logs[0].Message);
    }
}
