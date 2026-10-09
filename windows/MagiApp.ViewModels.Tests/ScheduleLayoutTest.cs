namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>ScheduleLayoutMetricsTest</c> の純ロジック部分の 1 対 1 の写し（dp の行数計算は Android 固有）。</summary>
public class ScheduleLayoutTest
{
    [Fact]
    public void BarModeByTabSheetRunningAndViolationNav()
    {
        Assert.Equal(BottomBarMode.None, ScheduleLayout.BottomBarModeOf(false, true, false, false, false));
        Assert.Equal(BottomBarMode.Command, ScheduleLayout.BottomBarModeOf(true, false, false, false, false));
        Assert.Equal(BottomBarMode.Command, ScheduleLayout.BottomBarModeOf(true, false, true, true, true));
        Assert.Equal(BottomBarMode.MergedWeek, ScheduleLayout.BottomBarModeOf(true, true, false, false, false));
        Assert.Equal(BottomBarMode.MergedViolation, ScheduleLayout.BottomBarModeOf(true, true, false, false, true));
        Assert.Equal(BottomBarMode.Hidden, ScheduleLayout.BottomBarModeOf(true, true, true, false, false));
        Assert.Equal(BottomBarMode.Hidden, ScheduleLayout.BottomBarModeOf(true, true, true, false, true));
        Assert.Equal(BottomBarMode.MergedWeek, ScheduleLayout.BottomBarModeOf(true, true, true, true, true));
        Assert.Equal(BottomBarMode.MergedViolation, ScheduleLayout.BottomBarModeOf(true, true, false, true, true));
    }

    [Fact]
    public void ScrollToGridTopOnlyOnEnteringTheScheduleTab()
    {
        Assert.True(ScheduleLayout.ShouldScrollToGridTop(0, 1, true, false, false));
        Assert.True(ScheduleLayout.ShouldScrollToGridTop(-1, 1, true, false, false));
        Assert.False(ScheduleLayout.ShouldScrollToGridTop(1, 1, true, false, false));
        Assert.False(ScheduleLayout.ShouldScrollToGridTop(0, 2, true, false, false));
        Assert.False(ScheduleLayout.ShouldScrollToGridTop(0, 1, false, false, false));
        Assert.False(ScheduleLayout.ShouldScrollToGridTop(0, 1, true, true, false));
        Assert.False(ScheduleLayout.ShouldScrollToGridTop(0, 1, true, false, true));
    }

    [Fact]
    public void WeekRangeCaptionForTheGridCorner()
    {
        Assert.Equal(("10/1", "〜7"), ScheduleLayout.WeekRangeCaption("2026-10-01", 0, 6));
        Assert.Equal(("10/29", "〜11/4"), ScheduleLayout.WeekRangeCaption("2026-10-01", 28, 34));
        Assert.Equal(("12/28", "〜1/3"), ScheduleLayout.WeekRangeCaption("2026-12-01", 27, 33));
        Assert.Null(ScheduleLayout.WeekRangeCaption("bad", 0, 6));
    }

    /// <summary>[3.648.0] 左上の範囲は暦の週でなく、いま見えている列（Kotlin <c>visible day range follows…</c>／<c>corner caption names…</c>）。</summary>
    [Fact]
    public void VisibleDayRangeFollowsTheScrollAndTheViewport()
    {
        Assert.Equal((0, 6), ScheduleLayout.VisibleDayRange(0, 700, 100, 31));
        Assert.Equal((4, 10), ScheduleLayout.VisibleDayRange(400, 700, 100, 31));
        Assert.Equal((24, 30), ScheduleLayout.VisibleDayRange(2400, 700, 100, 31));
        Assert.Equal((0, 6), ScheduleLayout.VisibleDayRange(0, 698, 100, 31));
        Assert.Equal((3, 9), ScheduleLayout.VisibleDayRange(260, 700, 100, 31));
        Assert.Equal((0, 3), ScheduleLayout.VisibleDayRange(0, 700, 100, 4));
        Assert.Null(ScheduleLayout.VisibleDayRange(0, 700, 0, 31));
        Assert.Null(ScheduleLayout.VisibleDayRange(0, 0, 100, 31));
        var start = ScheduleLayout.VisibleDayRange(0, 700, 100, 31)!.Value;
        Assert.Equal(("10/1", "〜7"), ScheduleLayout.WeekRangeCaption("2026-10-01", start.First, start.Last));
        var end = ScheduleLayout.VisibleDayRange(2400, 700, 100, 31)!.Value;
        Assert.Equal(("10/25", "〜31"), ScheduleLayout.WeekRangeCaption("2026-10-01", end.First, end.Last));
    }
}
