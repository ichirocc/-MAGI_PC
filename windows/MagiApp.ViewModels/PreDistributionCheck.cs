using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>配布前確認の 1 行（Kotlin <c>PreDistRow</c>）。Ok: true＝満たす、false＝注意、null＝情報。</summary>
public sealed record PreDistRow(string Label, string Value, bool? Ok);

/// <summary>
/// 配布前確認（判断支援 UX 仕様 §10、Kotlin <c>preDistributionRows</c> の移植）。計算完了・必須違反 0 件・相談の完了・保存は
/// 別の状態＝どれか 1 つを根拠に「配れる」とは言わず、行ごとに並べて最後の判断はマネージャーに残す。
/// </summary>
public static class PreDistributionCheck
{
    public const string Note = "必須違反 0 件は、登録した必須条件を満たしたことだけを示します。相談の完了や法令への適合は含みません。相談事項や職場の確認手続きを確かめたうえで、配布をご判断ください。";

    /// <summary>評価できていない範囲（仕様 §7・§8、3.665.0 利用者決定「未評価と明示するだけ」。Kotlin <c>MONTH_EDGE_NOTE</c>）。前月・翌月の勤務はデータに無い。</summary>
    public const string MonthEdgeNote = "前月末・翌月初をまたぐ並び（前月・翌月の勤務）は評価していません（今月の中だけで判定）";

    /// <summary>変更した日が月の初め・終わりの maxLen−1 日以内か（並びの長さ maxLen の窓が月の外へはみ出しうる）。</summary>
    public static bool MonthEdgeTouched(IEnumerable<int> days, int totalDays, int maxLen) =>
        maxLen >= 2 && days.Any(d => d < maxLen - 1 || d > totalDays - maxLen);

    public static IReadOnlyList<PreDistRow> Rows(UiState ui, string? csvLine, Func<string, string> labelOf)
    {
        var period = !string.IsNullOrWhiteSpace(ui.StartDate) && ui.Days > 0 ? DayText.Range(ui.StartDate, 0, ui.Days - 1) : "期間未設定";
        var evaluated = ui.HasResult && !ui.Running && ui.V6 is not null;
        var hardFams = MirrorKeys.Hard.Where(k => ui.Breakdown.TryGetValue(k, out var v) && v > 0).Select(k => (k, ui.Breakdown[k])).ToList();
        var hardText = ui.BestHard == 0 ? "0件" : $"{AnalysisTriage.HardText(ui.Breakdown, ui.BestHard)}（" +
            string.Join("・", hardFams.Select(f => $"{labelOf(f.k)} {f.Item2}{AnalysisTriage.UnitOf(f.k)}")) + "）";
        // 作成の結果カードと同じ並び（重み×数の大きい順）。
        var softText = CellSheetLogic.SoftSummary(ui.Breakdown, labelOf);
        var open = ui.Consults.Where(ConsultList.Unresolved).ToList();
        var consults = open.Count;
        var held = open.Count(c => c.Held);
        return new List<PreDistRow>
        {
            new("対象", $"対象期間 {period}・現在採用している勤務表", null),
            new("評価", evaluated ? "最新の勤務表を評価済み" : "まだ評価していません（問題がないか調べてください）", evaluated),
            new("必須違反", hardText, ui.BestHard == 0),
            new("要調整", softText, null),
            new("月の境目", MonthEdgeNote, null),
            new("相談", consults == 0 ? "「相談中」に未解決の項目はありません" : $"未完了 {consults}件" + (held > 0 ? $"（うち保留 {held}件）" : "") + "（ホームの「相談中」）", consults == 0),
            new("保存", csvLine ?? "まだ勤務表 CSV に保存していません", csvLine?.StartsWith("この内容で", StringComparison.Ordinal) == true),
        };
    }
}
