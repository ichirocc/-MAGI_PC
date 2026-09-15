using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>RestZeroWindowLnsTest.kt</c> の移植: 夜勤ブロックの位置を並べ替えて、休を明示 0 とした日の休を消す（回数は職員ごとに不変）。</summary>
public class V6HotfixPassesRestZeroLnsTest
{
    // 休0 D1 A2 B3。D の翌日は D か休のみ（D→A, D→B 禁止）、D は 3 連まで。
    private static MagiState St(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyDictionary<string, int>? wishes = null, IReadOnlyDictionary<string, string>? needDay = null)
        => MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-06",
            shifts: new List<Shift> { new("休", "休", "", ""), new("D", "D", "1", ""), new("A", "A", "1", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("X", 0), new("Y", 0), new("Z", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "", "" } },
            schedule: schedule, wishes: wishes,
            needDay1: needDay ?? new Dictionary<string, string> { ["0,3"] = "0" },
            cons3n: new List<C3Row> { new(new List<string> { "D", "A" }), new(new List<string> { "D", "B" }), new(new List<string> { "D", "D", "D", "D" }) });

    // X の夜勤 3 連が 3 日目で終わり 4 日目（休 0 の日）に休が強制されている盤面。
    private static readonly IReadOnlyList<IReadOnlyList<int>> Initial = new List<IReadOnlyList<int>>
    {
        new List<int> { 1, 1, 1, 0, 2, 2 },
        new List<int> { 2, 2, 2, 2, 0, 1 },
        new List<int> { 3, 3, 3, 1, 1, 0 },
    };
    private static int[][] Sched(MagiState s) => s.Schedule.ToIntArray2D();
    private static List<int> Counts(int[][] b, int i) => Enumerable.Range(0, 4).Select(k => b[i].Count(v => v == k)).ToList();

    [Fact]
    public void NightBlockIsRealignedSoTheZeroDayHasNoRest()
    {
        var s = St(Initial);
        var before = UnifiedViolationChecker.Check(s, Sched(s));
        Assert.Equal(0, before.Hard); Assert.Equal(1, before.Breakdown.GetValueOrDefault("covO"));
        var r = V6HotfixPasses.ApplyRestZeroWindowLns(s, Sched(s));
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.Equal(1, r.Applied);
        Assert.Equal(0, after.Hard);
        Assert.Equal(0, Enumerable.Range(0, 3).Count(i => r.NewSchedule[i][3] == 0));
        for (var i = 0; i < 3; i++) Assert.Equal(Counts(Sched(s), i), Counts(r.NewSchedule, i));
        Assert.True(UnifiedViolationChecker.BetterReport(after, before));
    }

    [Fact]
    public void WishLockedRestOnTheZeroDayIsNotATarget()
    {
        var s = St(Initial, wishes: new Dictionary<string, int> { ["0,3"] = 0 });
        var r = V6HotfixPasses.ApplyRestZeroWindowLns(s, Sched(s));
        Assert.Equal(0, r.Applied);
        Assert.Contains("対象日なし", r.Logs.Single().Message);
    }

    [Fact]
    public void NoExplicitRestNeedMeansNoTarget()
    {
        var s = St(Initial, needDay: new Dictionary<string, string>());
        var r = V6HotfixPasses.ApplyRestZeroWindowLns(s, Sched(s));
        Assert.Equal(0, r.Applied);
        Assert.Contains("対象日なし", r.Logs.Single().Message);
    }
}
