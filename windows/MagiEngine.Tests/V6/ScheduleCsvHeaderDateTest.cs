using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Xunit;

namespace MagiEngine.Tests.V6;

/// <summary>[Android 3.592.0 同期] ヘッダが実日付形式（M/D(曜)）のとき、今の期間と列位置で突き合わせてズレた列数を数える。</summary>
public class ScheduleCsvHeaderDateTest
{
    private static MagiState BuildState() => MinimalState.Build(
        staffList: new List<Staff> { new("山田", 0) },
        schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 7).ToList() });

    private static int[][] Base() => new[] { new[] { 0, 0, 0, 0, 0, 0, 0 } };

    [Fact]
    public void OwnExportMatchesThePeriod()
    {
        var st = BuildState();
        var r = ScheduleCsvBridge.Parse(ScheduleCsvBridge.Build(st, Base()), st, Base());
        Assert.Equal(0, r.HeaderDateMismatches);
    }

    [Fact]
    public void ShiftedHeaderCountsEveryMismatchedColumn()
    {
        var st = BuildState();
        var own = ScheduleCsvBridge.Build(st, Base());
        var shifted = ScheduleCsvBridge.Parse(own.Replace(ScheduleUtil.FormatDay(st.StartDate, 0), "1/1(木)"), st, Base());
        Assert.Equal(1, shifted.HeaderDateMismatches);
    }

    [Fact]
    public void DayNumberHeaderIsNotCompared()
    {
        var st = BuildState();
        var r = ScheduleCsvBridge.Parse("スタッフ \\ 日付,1,2,3,4,5,6,7\n山田,休,休,休,休,休,休,休\n", st, Base());
        Assert.Equal(0, r.HeaderDateMismatches);
    }
}
