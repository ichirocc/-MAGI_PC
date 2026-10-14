namespace MagiApp.ViewModels;

/// <summary>直し方を当てた結果の 1 行（Kotlin <c>FixOutcome</c>）。表示は <c>MagiViewModel.FixOutcomeLine</c>＝盤面か設定が変わったら出さない。</summary>
/// <param name="Before">当てる前の報告（改善手の適用だけ。内訳 <see cref="FixOutcomeText.Detail"/> に使う）。</param>
/// <param name="After">当てた後の報告。</param>
/// <param name="Tail">内訳のあとに続ける文（月の境目の注記）。</param>
public sealed record FixOutcome(string Line, MagiEngine.V6.ViolationReport? Before = null, MagiEngine.V6.ViolationReport? After = null, string? Tail = null);

/// <summary>直し方を当てたあと、同じ問題について「どうなったか」を 1 行で返す（Kotlin <c>FixOutcomeText.kt</c>、3.643.0）。</summary>
public static class FixOutcomeText
{
    public static string Applied(string label, int hardBefore, int hardAfter, string softBefore, string softAfter) =>
        hardAfter == 0 && hardBefore > 0 ? $"{label} を当てました。必須違反はなくなりました（要調整 {softBefore}→{softAfter}）。"
        : hardAfter < hardBefore ? $"{label} を当てました。必須違反 {hardBefore}→{hardAfter}。ほかの必須違反は {hardAfter} 件残っています。"
        : $"{label} を当てました。必須違反は変わらず {hardAfter} 件（要調整 {softBefore}→{softAfter}）。";

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
            (newHard > 0 ? $"新たな必須違反が {newHard} か所あります。" : after.Hard > 0 ? $"新たな必須違反はありません（全体の必須違反は {after.Hard} 件）。" : "");
    }

    /// <param name="stillMiss">再検査後にその枠でまだ足りない人数（null＝解消）。</param>
    public static string Guided(string dayLabel, string shiftSymbol, int? stillMiss, int hardAfter) =>
        stillMiss is null
            ? $"{dayLabel} の「{shiftSymbol}」の人員不足を解消しました。" + (hardAfter > 0 ? $"ほかの必須違反は {hardAfter} 件残っています。" : "必須違反はなくなりました。")
            : $"{dayLabel} の「{shiftSymbol}」はまだ {stillMiss}人 足りません。必須違反は {hardAfter} 件です。";
}
