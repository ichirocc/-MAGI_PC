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

    /// <summary>
    /// [視覚弱点者対応 第2便・C# 側のみ] 勤務表セルの読み上げ文（Kotlin <c>MagiScheduleViews.kt</c> の FlatCell に渡す <c>cd</c> と同じ語順・語彙）。
    /// <paramref name="vk"/>: 0=無し・1=必須・2/3=要調整。<paramref name="wish"/>: 0=無し・1=希望（反映）・2=希望未反映。
    /// <paramref name="extBanSyms"/>: 拡張希望の指定日なら禁止シフトの記号（null=指定なし）、<paramref name="extViolated"/> は割当が禁止のシフト。
    /// </summary>
    public static string CellDescription(string staffName, string dayFull, string sym, int vk, bool inBand, int wish, string wishSym,
        bool pinned, IReadOnlyList<string>? extBanSyms, bool extViolated)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(staffName).Append(' ').Append(dayFull).Append(' ').Append(string.IsNullOrWhiteSpace(sym) ? "なし" : sym);
        if (vk == 1) sb.Append("・必須"); else if (vk >= 2) sb.Append("・要調整");
        if (inBand) sb.Append("・期間の制約の窓");
        if (wish == 2) sb.Append("・希望未反映（希望=").Append(string.IsNullOrWhiteSpace(wishSym) ? "?" : wishSym).Append('）');
        else if (wish != 0) sb.Append("・希望");
        if (pinned) sb.Append("・手動固定");
        if (extBanSyms is not null)
        {
            if (extViolated) sb.Append("・拡張希望の違反");
            else sb.Append("・拡張希望（").Append(string.Join("・", extBanSyms)).Append("以外）");
        }
        return sb.Append("、Enterで変更").ToString();
    }

    /// <summary>矢印キーでの注目セルの移動（表の端で止まる）。<paramref name="di"/>/<paramref name="dj"/> は -1/0/1。注目が無ければ (0,0) から始める。</summary>
    public static (int I, int J) MoveCell((int I, int J)? cur, int di, int dj, int staffCount, int dayCount)
    {
        if (staffCount <= 0 || dayCount <= 0) return (0, 0);
        if (cur is not { } c) return (0, 0);
        return (Math.Clamp(c.I + di, 0, staffCount - 1), Math.Clamp(c.J + dj, 0, dayCount - 1));
    }

    public enum CellBorder { None, Violation, Focus, Outline }

    /// <summary>
    /// セルの枠の選び方（注目＞必須違反＞輪郭）。Windows のハイコントラストでは無違反セルにも必ず輪郭を引く
    /// （シフト色の地が HC の配色と無関係なため、隣のセルとの境目が消える）。
    /// </summary>
    public static CellBorder CellBorderOf(bool focused, bool hardViolation, bool lowContrast, bool highContrast) =>
        focused ? CellBorder.Focus
        : hardViolation ? CellBorder.Violation
        : lowContrast || highContrast ? CellBorder.Outline
        : CellBorder.None;

    /// <summary>開閉の状態の読み上げ（▸／▾ の印は読み上げに渡さない。Kotlin <c>disclosureState</c>）。</summary>
    public static string DisclosureState(bool open) => open ? "開いています" : "閉じています";

    /// <summary>
    /// [Android 3.725.0] 開閉や行き先の印つきの見た目の文字「{label} ▸／▾」（Kotlin <c>DisclosureText</c> の見た目）。
    /// 読み上げは <paramref name="label"/> と <see cref="DisclosureState"/> だけにする（記号は「右向きの三角」と読まれる）。
    /// <paramref name="open"/> が null なら行き先の印 ▸ で状態は読まない。
    /// </summary>
    public static string DisclosureText(string label, bool? open) => label + (open == true ? " ▾" : " ▸");
}
