using System.Collections.Generic;

namespace MagiApp.ViewModels;

/// <summary>
/// 色ピッカーの 36 色（6×6）。Kotlin原本 <c>ShiftColorEditor.kt</c> の <c>COLOR_PALETTE</c>（3.460.0、ユーザー指示「6×6の36色にしてください」）
/// と同じ順・同じ値。格納順は保存値（<c>shiftColors[kigou]</c> は hex 文字列そのもの）に影響しないが、
/// row0col0=#e08a1e のアンカーを含む既定 25 色の位置・値は原本と同じに保つ（テストで固定）。
/// </summary>
public static class ShiftColorPalette
{
    public const int PerRow = 6;

    public static readonly IReadOnlyList<string> All = new[]
    {
        "#e08a1e", "#e5e5e5", "#52b788", "#f77f00", "#ffb3c6",
        "#83a6ed", "#ff8c42", "#3a86ff", "#e76f51", "#457b9d",
        "#9d4edd", "#a7c957", "#ff006e", "#ffcc00", "#b5838d",
        "#8338ec", "#f7ee7f", "#48cae4", "#f4978e", "#606c38",
        "#f4a261", "#a2d2ff", "#e09f3e", "#2a9d8f", "#a82246",
        "#d62828", "#2b9348", "#023047", "#6a4c93", "#6f4518",
        "#adb5bd", "#06d6a0", "#7209b7", "#ef476f", "#588157",
        "#ffd166",
    };

    /// <summary>明度から選択チェック印の文字色を選ぶ（原本 <c>pickFg</c>）。不正な hex は中間灰として扱う。</summary>
    public static string PickFg(string bgHex)
    {
        var h = (bgHex ?? "").Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v)) v = 0x888888;
        var r = (v >> 16) & 0xFF; var g = (v >> 8) & 0xFF; var b = v & 0xFF;
        var lum = 0.299 * r + 0.587 * g + 0.114 * b;
        return lum > 140 ? "#14110d" : "#fbf4e8";
    }
}
