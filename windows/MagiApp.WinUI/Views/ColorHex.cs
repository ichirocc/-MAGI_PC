using Windows.UI;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [2026-09-02, 配線] "#rrggbb"/"#rgb" 文字列 ⇔ <see cref="Color"/> の変換。<see cref="ScheduleView"/>
/// （勤務表グリッド/シフト集計の色描画）と <see cref="SettingsView"/>（色設定の変更UI）の両方が使うため
/// ここへ一本化する（複製すると2箇所が別々にドリフトする——<see cref="UiState"/>クラスKDoc等で
/// 明示されているこのプロジェクトの確立済みの規約）。
/// </summary>
internal static class ColorHex
{
    /// <summary>必須違反の既定色（<c>Styles/MagiTheme.xaml</c> の <c>MagiErrorColor</c> と同値のRGB）。</summary>
    public const string DefaultHardVioHex = "#8C0009";

    /// <summary>要調整(ソフト違反)の既定色。[色覚アクセシビリティ, Android同期] 深紅(必須)との明度差を
    /// 最大化したアンバー（ユーザー指示：P型/D型色覚での誤認対策）。[Android 3.719.0] 白地で 3:1 を満たす
    /// <c>MagiAccent.Orange</c>（#B86E00）と同値へ（旧 #F59E0B は 2.15:1）。Kotlin <c>SOFT_VIO_DEFAULT_HEX</c>。</summary>
    public const string DefaultSoftVioHex = MagiEngine.V6.MagiAccent.Orange;

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var s = hex.Trim();
        if (s.StartsWith("#", StringComparison.Ordinal)) s = s[1..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6) return fallback;
        try
        {
            var r = Convert.ToByte(s[..2], 16);
            var g = Convert.ToByte(s[2..4], 16);
            var b = Convert.ToByte(s[4..6], 16);
            return Color.FromArgb(0xFF, r, g, b);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
