using MagiEngine.Model;

namespace MagiApp.ViewModels;

/// <summary>
/// 表示だけの変更（手動固定・表示色）の判定（Kotlin <c>MagiViewState.kt</c> の <c>DisplayOnlyUndo</c> のうち 3.726.0 で足された判定だけ）。
/// </summary>
public static class DisplayOnlyUndo
{
    /// <summary>設定の差が手動固定・表示色だけか（盤面 Schedule は見ない＝検査の盤面は別に渡る）。<c>KeepDisplayOf</c> の判定。
    /// C# の record は一覧を参照で比べる＝写しが違えば「ほかの差あり」に倒れる（その場合は従来どおり検査の結果のまま）。</summary>
    public static bool DiffersOnlyInPinsOrColors(MagiState a, MagiState b) =>
        a with { ShiftColors = b.ShiftColors, ManualPins = b.ManualPins, Schedule = b.Schedule } == b;
}
