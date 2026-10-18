using System.Globalization;

namespace MagiApp.ViewModels;

/// <summary>
/// [Android 3.719.0 視覚弱点者対応 第1便] 文字拡大・コントラストの純ロジック（Kotlin <c>A11y.kt</c> の 1 対 1）。
/// View は端末の文字倍率と色の相対輝度を渡すだけ＝テストが同じ数で検算できる。表示のみ・採点不変。
/// </summary>
public static class A11y
{
    /// <summary>端末の文字拡大がこの倍率以上なら、1 行で切っていた文を 1 行増やす（Android M3 の「大」＝1.3×）。</summary>
    public const double WrapFontScale = 1.3;

    /// <summary>1 行＋「…」の文の行数。拡大 1.3× 以上は <paramref name="baseLines"/>+1 行。</summary>
    public static int MaxLines(double fontScale, int baseLines = 1) => fontScale >= WrapFontScale ? baseLines + 1 : baseLines;

    /// <summary>WCAG 2.x の相対輝度（<c>#rrggbb</c>）。</summary>
    public static double WcagLuminance(string hex)
    {
        var s = hex.TrimStart('#');
        static double Ch(string h)
        {
            var c = int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(s[..2]) + 0.7152 * Ch(s[2..4]) + 0.0722 * Ch(s[4..6]);
    }

    /// <summary>2 つの相対輝度のコントラスト比（(明+0.05)/(暗+0.05)、1 以上）。</summary>
    public static double ContrastRatio(double lumA, double lumB) => (Math.Max(lumA, lumB) + 0.05) / (Math.Min(lumA, lumB) + 0.05);

    public static double ContrastRatio(string hexA, string hexB) => ContrastRatio(WcagLuminance(hexA), WcagLuminance(hexB));

    /// <summary>セル地と surface のコントラストがこれ未満なら、違反の無いセルにも 1px の輪郭を引く（似た明度の隣のセルと切り分ける）。</summary>
    public const double OutlineContrastMin = 1.5;

    public static bool LowContrastOutline(double lumBg, double lumSurface) => ContrastRatio(lumBg, lumSurface) < OutlineContrastMin;
}
