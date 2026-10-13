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
    public const string Note = "必須違反 0 件は、登録した必須条件を満たしたことだけを示します。相談の完了や法令への適合は含みません。配るかどうかはあなたが決めてください。";

    public static IReadOnlyList<PreDistRow> Rows(UiState ui, string? csvLine, Func<string, string> labelOf)
    {
        var period = !string.IsNullOrWhiteSpace(ui.StartDate) && ui.Days > 0 ? DayText.Range(ui.StartDate, 0, ui.Days - 1) : "期間未設定";
        var evaluated = ui.HasResult && !ui.Running && ui.V6 is not null;
        var hardFams = MirrorKeys.Hard.Where(k => ui.Breakdown.TryGetValue(k, out var v) && v > 0).Select(k => (k, ui.Breakdown[k])).ToList();
        var hardText = ui.BestHard == 0 ? "0件" : $"{ui.BestHard}件（" + string.Join("・", hardFams.Select(f => $"{labelOf(f.k)} {f.Item2}件")) + "）";
        var soft = ui.Breakdown.Where(kv => kv.Value > 0 && !MirrorKeys.Hard.Contains(kv.Key)).OrderByDescending(kv => kv.Value).ToList();
        var softText = soft.Count == 0 ? "なし" : string.Join("・", soft.Take(3).Select(kv => $"{labelOf(kv.Key)} {kv.Value}件")) +
            (soft.Count > 3 ? $" ほか{soft.Count - 3}種類" : "");
        var consults = ui.Consults.Count;
        var held = ui.Consults.Count(c => c.Held);
        return new List<PreDistRow>
        {
            new("対象", $"{period} の採用した勤務表", null),
            new("評価", evaluated ? "最新の勤務表を評価済み" : "まだ評価していません（問題がないか調べてください）", evaluated),
            new("必須違反", hardText, ui.BestHard == 0),
            new("要調整", softText, null),
            new("相談", consults == 0 ? "未完了の相談・保留はありません" : $"未完了 {consults}件" + (held > 0 ? $"（うち保留 {held}件）" : "") + "（ホームの「相談中」）", consults == 0),
            new("保存", csvLine ?? "まだ勤務表 CSV に保存していません", csvLine?.StartsWith("この内容で", StringComparison.Ordinal) == true),
        };
    }
}
