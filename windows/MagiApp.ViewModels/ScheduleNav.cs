namespace MagiApp.ViewModels;

/// <summary>勤務表タブの週送り（Kotlin原本 <c>MagiViewState.kt</c> の <c>currentWeekIndex</c>）。UI 非依存の純関数。</summary>
public static class ScheduleNav
{
    /// <summary>週送りの現在週。左端の日を含む週、右端まで来ていれば最終週（最終週が 7 日未満の月は左端の日だけでは届かない）。</summary>
    public static int CurrentWeekIndex(IReadOnlyList<IReadOnlyList<int>> weeks, int leftDay, bool atEnd)
    {
        if (weeks.Count == 0) return 0;
        if (atEnd) return weeks.Count - 1;
        for (var w = 0; w < weeks.Count; w++)
            if (weeks[w].Count > 0 && leftDay <= weeks[w][^1]) return w;
        return weeks.Count - 1;
    }
}
