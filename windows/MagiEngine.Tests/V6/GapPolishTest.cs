using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>シフトの間隔（gap）の研磨: 1 マスの長方形交換では禁止の並びになって直せない盤面を、区間の入れ替えで直す（Kotlin <c>GapPolishTest.kt</c>、Android 3.727.127）。</summary>
[Collection("PolishGateGlobal")]
public class GapPolishTest : IDisposable
{
    public void Dispose() => V6HotfixPasses.GapSegmentSwap = true;

    // 0 休・1 D・2 A。D の翌日に A は禁止、D は 4 日以上空ける。
    private static readonly MagiState St = MinimalState.Build(
        startDate: "2026-12-01", endDate: "2026-12-10",
        shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("D", "D", "", ""), new("A", "A", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: Enumerable.Range(0, 2).Select(i => new Staff($"s{i}", 0, 0)).ToList(),
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 0, 1, 0, 2, 2, 2, 2, 2, 2 },   // D 休 D 休 A … ＝間隔 2（足りない 2 日）
            new List<int> { 2, 2, 2, 2, 2, 1, 0, 2, 2, 2 },
        },
        cons3n: new List<C3Row> { new(new List<string> { "D", "A" }) })
        with { ConsGap = new List<GapRow> { new("D", "4") } };

    private static int[][] Board() => St.Schedule.Select(r => r.ToArray()).ToArray();

    [Fact]
    public void rectangleAloneCannotFixWithoutForbiddenRun()
    {
        V6HotfixPasses.GapSegmentSwap = false;
        var r = V6HotfixPasses.ApplyGapPolish(St, Board());
        Assert.Equal(2, r.Report!.Breakdown.GetValueOrDefault("gap"));
        Assert.Equal(0, r.Applied);
    }

    [Fact]
    public void segmentSwapMovesTheBlockWithItsFollowingDay()
    {
        var before = UnifiedViolationChecker.Check(St, Board());
        var r = V6HotfixPasses.ApplyGapPolish(St, Board());
        var after = UnifiedViolationChecker.Check(St, r.NewSchedule);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("gap"));
        Assert.Equal(0, after.Hard);
        Assert.True(UnifiedViolationChecker.BetterReport(after, before));
        for (var j = 0; j < 10; j++)
            for (var k = 0; k < 3; k++)
                Assert.Equal(Board().Count(row => row[j] == k), r.NewSchedule.Count(row => row[j] == k));   // 各日の人数は変わらない
    }
}
