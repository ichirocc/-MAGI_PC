using System.Globalization;

namespace MagiApp.ViewModels;

/// <summary>勤務表タブの下部バーの種別（Kotlin <c>BottomBarMode</c>）。WinUI の殻は別行のバーを常設するため、現在は表示側からは使わない。</summary>
public enum BottomBarMode { None, Command, MergedWeek, MergedViolation, Hidden }

/// <summary>勤務表タブの縦寸法まわりの純ロジック（Kotlin <c>ScheduleLayoutMetrics.kt</c> の関数部分。dp 定数は Android 固有なので移さない）。</summary>
public static class ScheduleLayout
{
    /// <summary>勤務表タブ以外＝従来のコマンドバー。勤務表タブ＝統合バー。シートを開いていて最適化中でなければ隠す。実行中は「やめる」を残す。</summary>
    public static BottomBarMode BottomBarModeOf(bool loaded, bool scheduleTab, bool sheetOpen, bool busy, bool violationNav)
    {
        if (!loaded) return BottomBarMode.None;
        if (!scheduleTab) return BottomBarMode.Command;
        if (sheetOpen && !busy) return BottomBarMode.Hidden;
        if (violationNav && !sheetOpen) return BottomBarMode.MergedViolation;
        return BottomBarMode.MergedWeek;
    }

    /// <summary>別タブ→勤務表タブへ入ったときだけグリッド上端へスクロールする。シート・注目セルがあれば各自の位置決めに任せる。</summary>
    public static bool ShouldScrollToGridTop(int prevTab, int tab, bool loaded, bool sheetOpen, bool focusPending)
        => tab == 1 && prevTab != 1 && loaded && !sheetOpen && !focusPending;

    /// <summary>[3.648.0] 横スクロールで実際に見えている日の範囲（0 始まり、Kotlin <c>visibleDayRange</c>）。左端は最も近い列、列数はビューポートに
    /// 収まる数（端数は四捨五入）、最後の日で止める。測れていなければ null。WinUI の殻は自前の列計測で現在週を出しており、この関数はロジックの写し。</summary>
    public static (int First, int Last)? VisibleDayRange(int scrollPx, int viewportPx, int cellWpx, int days)
    {
        if (cellWpx <= 0 || viewportPx <= 0 || days <= 0) return null;
        var first = Math.Clamp((int)Math.Round(scrollPx / (double)cellWpx, MidpointRounding.AwayFromZero), 0, days - 1);
        var cols = Math.Max(1, (viewportPx + cellWpx / 2) / cellWpx);
        return (first, Math.Min(days - 1, first + cols - 1));
    }

    /// <summary>グリッド左上に出す日の範囲（2 行）。同じ月なら「10/1」「〜7」、月をまたぐなら「10/29」「〜11/4」。解析できなければ null。</summary>
    public static (string Start, string End)? WeekRangeCaption(string startDate, int firstDay, int lastDay)
    {
        if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d0)) return null;
        var a = d0.AddDays(firstDay);
        var b = d0.AddDays(lastDay);
        return ($"{a.Month}/{a.Day}", a.Month == b.Month ? $"〜{b.Day}" : $"〜{b.Month}/{b.Day}");
    }
}
