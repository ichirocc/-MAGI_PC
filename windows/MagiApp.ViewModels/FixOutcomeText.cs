namespace MagiApp.ViewModels;

/// <summary>直し方を当てた結果の 1 行（Kotlin <c>FixOutcome</c>）。表示は <c>MagiViewModel.FixOutcomeLine</c>＝盤面か設定が変わったら出さない。</summary>
/// <param name="Before">当てる前の報告（改善手の適用だけ。内訳 <see cref="FixOutcomeText.Detail"/> に使う）。</param>
/// <param name="After">当てた後の報告。</param>
/// <param name="Tail">内訳のあとに続ける文（月の境目の注記）。</param>
public sealed record FixOutcome(string Line, MagiEngine.V6.ViolationReport? Before = null, MagiEngine.V6.ViolationReport? After = null, string? Tail = null);

/// <summary>直し方を当てたあと、同じ問題について「どうなったか」を 1 行で返す（Kotlin <c>FixOutcomeText.kt</c>、3.643.0）。</summary>
public static class FixOutcomeText
{
    /// <param name="beforeBreakdown">当てる前の内訳。前後とも単位ごとに言う（件と人を足さない＝INV-07、3.693.0）。無ければ件で言う。</param>
    public static string Applied(string label, int hardBefore, int hardAfter, string softBefore, string softAfter,
        IReadOnlyDictionary<string, int>? afterBreakdown = null, IReadOnlyDictionary<string, int>? beforeBreakdown = null)
    {
        var left = AnalysisTriage.HardText(afterBreakdown, hardAfter);
        return hardAfter == 0 && hardBefore > 0 ? $"{label} を適用しました。必須違反はなくなりました（要調整 {softBefore}→{softAfter}）。"
            : hardAfter < hardBefore ? $"{label} を適用しました。必須違反 {AnalysisTriage.HardText(beforeBreakdown, hardBefore)}→{left}。ほかの必須違反は {left} 残っています。"
            : $"{label} を適用しました。必須違反は変わらず {left}（要調整 {softBefore}→{softAfter}）。";
    }

    /// <summary>
    /// 当てたあとの内訳（Kotlin <c>fixResultDetail</c>、違反セルの半自動修正仕様 §8、3.663.0）: 解消・減少した違反と、新たに生じた問題を分けて言う。
    /// 数は報告の内訳（族ごとの件数、曜日の偏り・公平化は pt）。件数が増えなくても別の場所に生まれた違反は「場所が変わった」と言う
    /// （<see cref="MagiEngine.V6.ViolationIdentity"/>）。必須の残りは <see cref="Applied"/> が言う＝ここでは「新たな必須違反なし」と全体の 0 件を言い分けるだけ。
    /// </summary>
    public static string Detail(MagiEngine.V6.ViolationReport before, MagiEngine.V6.ViolationReport after, Func<string, string> labelOf)
    {
        // Kotlin の sortedByDescending（安定ソート）と同じ順＝LINQ の OrderByDescending（安定）。
        var fams = before.Breakdown.Keys.Concat(after.Breakdown.Keys.Where(k => !before.Breakdown.ContainsKey(k)))
            .OrderByDescending(MagiEngine.V6.MirrorKeys.WeightOf).ToList();
        int D(string f) => after.Breakdown.GetValueOrDefault(f, 0) - before.Breakdown.GetValueOrDefault(f, 0);
        var down = fams.Where(f => D(f) < 0).Select(f => $"{labelOf(f)} {-D(f)}{AnalysisTriage.UnitOf(f)}").ToList();
        var up = fams.Where(f => D(f) > 0).ToList();
        var moved = MagiEngine.V6.ViolationIdentity.Of(before, after).Appeared.Select(k => k.Family).Distinct()
            .Where(f => !up.Contains(f) && after.Breakdown.GetValueOrDefault(f, 0) > 0).OrderByDescending(MagiEngine.V6.MirrorKeys.WeightOf).ToList();
        var appeared = up.Select(f => $"{labelOf(f)} +{D(f)}{AnalysisTriage.UnitOf(f)}").Concat(moved.Select(f => $"{labelOf(f)}（場所が変わった）")).ToList();
        var newHard = MagiEngine.V6.ViolationIdentity.NewHard(before, after).Count;
        return (down.Count == 0 ? "解消・減少した違反: なし。" : $"解消・減少した違反: {string.Join("・", down)}。") +
            (appeared.Count == 0 ? "新たに生じた問題: なし。" : $"新たに生じた問題: {string.Join("・", appeared)}。") +
            (newHard > 0 ? $"新たな必須違反が {newHard}か所あります。" : after.Hard > 0 ? $"新たな必須違反はありません（全体の必須違反は {AnalysisTriage.HardText(after.Breakdown, after.Hard)}）。" : "");
    }

    /// <param name="stillMiss">再検査後にその枠でまだ足りない人数（null＝解消）。</param>
    public static string Guided(string dayLabel, string shiftSymbol, int? stillMiss, int hardAfter, IReadOnlyDictionary<string, int>? breakdown = null) =>
        stillMiss is null
            ? $"{dayLabel} の「{shiftSymbol}」の人員不足を解消しました。" + (hardAfter > 0 ? $"ほかの必須違反は {AnalysisTriage.HardText(breakdown, hardAfter)} 残っています。" : "必須違反はなくなりました。")
            : $"{dayLabel} の「{shiftSymbol}」はまだ {stillMiss}人 足りません。必須違反は {AnalysisTriage.HardText(breakdown, hardAfter)}です。";

    private static readonly System.Text.RegularExpressions.Regex ChainShiftRe = new("^（玉突き連鎖）(\\d+)人・(\\d+)マスを順に入れ替え$");
    private static readonly System.Text.RegularExpressions.Regex ReoptRe = new("^（再最適化）(.+) の(\\d+)名を最適割当$");
    private static readonly System.Text.RegularExpressions.Regex ChainFillRe = new("^（連鎖）(.+) の「(.+)」不足を(\\d+)コマ補充$");

    /// <summary>改善の 1 手の名を画面の言葉へ（Kotlin <c>fixDisplayLabel</c>。エンジンの名は照合の鍵にも使う＝表示のときだけ言い換える）。合わなければそのまま。</summary>
    public static string FixDisplayLabel(string label)
    {
        if (ChainShiftRe.Match(label) is { Success: true } m1) return $"{m1.Groups[1].Value}人の勤務を順に入れ替える（{m1.Groups[2].Value}か所）";
        if (ReoptRe.Match(label) is { Success: true } m2) return $"{m2.Groups[1].Value} の {m2.Groups[2].Value}人の勤務を組み直す";
        if (ChainFillRe.Match(label) is { Success: true } m3) return $"{m3.Groups[1].Value}さんの {m3.Groups[2].Value} を {m3.Groups[3].Value}回増やす";
        return label;
    }

    /// <summary>改善の案の 1 行目＝効き目（必須違反の変化、無ければ要調整の変化）。どちらも動かなければ null。族名は <paramref name="labelOf"/>。</summary>
    public static string? FixBenefitLine(MagiEngine.V6.FixSuggestion s, Func<string, string> labelOf)
    {
        if (CellSheetLogic.HardChangeLine(s.Diff, labelOf) is { } h) return h;
        var hard = new HashSet<string>(MagiEngine.V6.MirrorKeys.Hard);
        var soft = AnalysisTriage.FamilyDeltaList(s.Diff.Where(x => !hard.Contains(x.Family)), labelOf);
        return soft.Length > 0 ? $"要調整の変化：{soft}" : null;
    }

    public const string NoFixSuggestionText = "今の勤務表で、1手で減らせる案は見つかりませんでした。上の職員を押すと、その人に絞って探します";

    /// <summary>自動で整えた結果の通知（採用したか＝<paramref name="adopted"/>）。要調整は単位ごと、必須違反は残りの有無だけ言う。</summary>
    public static string PolishResultText(bool adopted, string softBefore, string softAfter, int hardAfter, string hardAfterText) =>
        adopted ? $"自動で整えました。要調整 {softBefore} → {softAfter}" + (hardAfter == 0 ? "（必須違反はなしのまま）" : $"（必須違反 {hardAfterText}）")
        : "自動で整えましたが、要調整は減りませんでした（勤務表は変えていません）";

    /// <summary>人員不足の候補 1 人の影響（「休 8回 → 7回（下限 8回を割ります）」）。今の勤務が無ければ null。</summary>
    public static string? GuidedCandidateImpact(string? fromSym, int before, int? lo) =>
        fromSym is null ? null : $"{fromSym} {before}回 → {before - 1}回" + (lo is { } l && before - 1 < l ? $"（下限 {l}回を割ります）" : "");

    /// <summary>人員不足を直し終えたときの、ほかの必須違反（人員不足を除く）。無ければ null。</summary>
    public static string? OtherHardAfterShortage(IReadOnlyDictionary<string, int> breakdown) =>
        AnalysisTriage.UnitTotals(breakdown, MagiEngine.V6.MirrorKeys.Hard.Where(f => f != "covU")) is { } t ? $"ほかの必須違反（{t}）が残っています" : null;

    /// <summary>人員不足の手伝いの終わりの主ボタン（巡回の先頭を開く、Kotlin <c>guidedTourButton</c>）。</summary>
    public static string GuidedTourButton(int n) => $"必須違反{n}か所を順に見る";

    /// <summary>人員不足の手伝いの題（H-1、Kotlin <c>guidedFixTitle</c>）。枠が無い埋められない段は「残りの人員不足は埋められません」。</summary>
    public static string GuidedFixTitle(GuidedStage stage, string? dayLabel, string? shiftSymbol)
    {
        var slot = dayLabel is not null && shiftSymbol is not null ? $"{dayLabel}の「{shiftSymbol}」" : null;
        if (stage == GuidedStage.Done) return ShortageDoneTitle;
        if (slot is null) return "残りの人員不足は埋められません";
        return stage switch
        {
            GuidedStage.Candidates => $"{slot}に入る人",
            GuidedStage.Chain => $"{slot}を入れ替えで埋める",
            _ => $"{slot}は埋められません",
        };
    }

    public static string GuidedFixTitle(GuidedStage stage, MagiEngine.V6.CoverageShortfall? target) => GuidedFixTitle(stage, target?.DayLabel, target?.ShiftSymbol);

    /// <summary>状態の 2 点（H-2、Kotlin <c>guidedStatusPoints</c>）: 「いまN人／必要M人」と、候補がいれば「動かせる人がN人います」。</summary>
    public static IReadOnlyList<string> GuidedStatusPoints(int need, int got, int movable) =>
        new[] { CellSheetLogic.ShortageHeadlineOf("", "", need, got, need - got).Status, movable > 0 ? $"動かせる人が{movable}人います" : null }.OfType<string>().ToList();

    /// <summary>候補 1 人の行（H-3、Kotlin <c>guidedCandidateLabel</c>）「山田さんをA4に入れる」。今の勤務は下の回数の行が示す。</summary>
    public static string GuidedCandidateLabel(string name, string shiftSymbol) => $"{name}さんを{shiftSymbol}に入れる";

    public const string ShortageDoneTitle = "人員不足は直し終わりました";
}

/// <summary>人員不足の手伝いの段（候補の人がいる／入れ替えで埋める／埋められない／終わり。Kotlin <c>GuidedStage</c>）。</summary>
public enum GuidedStage { Candidates, Chain, Blocked, Done }
