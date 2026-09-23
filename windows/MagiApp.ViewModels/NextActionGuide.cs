using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[Android 3.612.0 思考誘導S3] 残っている必須違反に関わる希望 1 件（職員・日・理由）。Kotlin <c>InvolvedWish</c>。</summary>
public sealed record InvolvedWish(int Staff, int Day, string Name, string Reason);

/// <summary>
/// [Android 3.612.0 思考誘導] ホームの主ボタンとその行き先が使う、画面に依存しない判定。
/// Kotlin <c>fixImpactLines</c>（BreakdownLabels.kt）と <c>involvedWishes</c>（MagiViewState.kt）の移植。
/// 族名の日本語は UI 層が持つので <c>labelOf</c> で受け取る（<see cref="AnalysisTriage"/> と同じ形）。
/// </summary>
public static class NextActionGuide
{
    /// <summary>1手の提案の得失を、利用者が最初に考える順に2行で言う。1行目＝必須の約束が減るか、2行目＝注意（増える要調整。無ければ null）。</summary>
    public static (string HardLine, string? Caution) FixImpactLines(FixSuggestion s, Func<string, string> labelOf)
    {
        var hardLine = s.DeltaHard < 0 ? $"必須の約束: 減る（{-s.DeltaHard}件）"
            : s.DeltaHard == 0 ? "必須の約束: 変わらない"
            : $"必須の約束: 増える（{s.DeltaHard}件）";
        var worse = s.Diff.Where(d => d.Delta > 0 && !MirrorKeys.Hard.Contains(d.Family)).ToList();
        var caution = worse.Count == 0 ? null : "注意: " + string.Join("・", worse.Select(d => $"{labelOf(d.Family)} +{d.Delta}"));
        return (hardLine, caution);
    }

    /// <summary>
    /// 必須違反に関わる希望を、名前・日付・理由つきで列挙する（職員順→日順）。関わる＝そのセルに希望違反(pref)がある、
    /// 希望前日の禁止(c3w)の翌日の希望（c3w の印は前日側のセルに付く）、または禁止の並び(c3n)が希望で固定したセルに掛かっている。
    /// </summary>
    public static IReadOnlyList<InvolvedWish> InvolvedWishes(UiState ui)
    {
        var list = new List<InvolvedWish>();
        foreach (var (key, fams) in ui.ViolationCellFamilies)
        {
            var parts = key.Split(',');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var j)) continue;
            var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
            if (fams.Contains("vio-pref")) list.Add(new InvolvedWish(i, j, name, "希望の勤務になっていません"));
            if (fams.Contains("vio-c3w")) list.Add(new InvolvedWish(i, j + 1, name, $"前日（{j + 1}日）に置けない勤務が入っています"));
            if (fams.Contains("vio-c3n") && ui.Wishes.ContainsKey(key)) list.Add(new InvolvedWish(i, j, name, "希望が禁止の並びに掛かっています"));
        }
        return list.Distinct().OrderBy(w => w.Staff).ThenBy(w => w.Day).ToList();
    }
}
