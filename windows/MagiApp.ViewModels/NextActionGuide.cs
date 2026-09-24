using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[Android 3.612.0 思考誘導S3] 残っている必須違反に関わる希望 1 件（職員・日・理由）。Kotlin <c>InvolvedWish</c>。</summary>
public sealed record InvolvedWish(int Staff, int Day, string Name, string Reason);

/// <summary>
/// [Android 3.612.0 思考誘導] ホームの主ボタンとその行き先が使う、画面に依存しない判定。
/// Kotlin <c>fixImpactLines</c>（BreakdownLabels.kt）と <c>involvedWishes</c>（MagiViewState.kt）の移植。
/// 族名の日本語は UI 層が持つので <c>labelOf</c> で受け取る（<see cref="AnalysisTriage"/> と同じ形）。
/// </summary>
/// <summary>[S5] 試算の候補 1 行。Locked=false（担当できない勤務の希望）は試算ボタンを出さず <see cref="NextActionGuide.WishTrialNotLocked"/> を出す。</summary>
public sealed record WishTrialRow(int Staff, int Day, string Name, string Reason, bool Locked);

/// <summary>[S5b] 人手不足の枠 1 つ（見出し「12日 日勤 1人不足」）と、その日に別の勤務で希望固定されている人の行（職員順）。</summary>
public sealed record ShortfallWishGroup(int Day, int Shift, string Header, IReadOnlyList<WishTrialRow> Rows);

public sealed record WishTrialCandidates(IReadOnlyList<WishTrialRow> Direct, IReadOnlyList<ShortfallWishGroup> Shortfall)
{
    public bool IsEmpty => Direct.Count == 0 && Shortfall.All(g => g.Rows.Count == 0);
}

public static class NextActionGuide
{
    public const string WishTrialNotLocked = "担当できない勤務の希望なので、取り消しても勤務表は変わりません。";
    /// <summary>[S5b] 1 枠に並べる行の上限（超えたら「ほか N人」）。</summary>
    public const int WishTrialGroupLimit = 8;

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

    /// <summary>
    /// [S5] 試算の候補（Kotlin <c>wishTrialCandidates</c>、§2.2・§2.3）。S5a＝必須違反に関わる希望を (職員, 日) で重複除去し、
    /// 代表の理由を pref＞c3w＞c3n で選ぶ（他は「ほか: …」）。c3w は翌日の希望 X と、印の付く前日自身が WishLocked の希望 Y の両方。
    /// S5b＝人手不足の枠の WishPinned（日→シフト、職員順）。S5a と重なる (職員, 日) は S5a を代表にし「ほか: 人手不足の日」を足す。
    /// </summary>
    public static WishTrialCandidates WishTrialCandidatesOf(UiState ui)
    {
        // 優先度（小さいほど代表）と「ほか」に出す短い名前。
        string[] shortNames = { "希望の勤務になっていない", "前日の禁止", "禁止の並び" };
        var hits = new List<(int Staff, int Day, int Prio, string Reason)>();
        foreach (var (key, fams) in ui.ViolationCellFamilies)
        {
            var parts = key.Split(',');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var j)) continue;
            if (fams.Contains("vio-pref")) hits.Add((i, j, 0, "希望の勤務になっていません"));
            if (fams.Contains("vio-c3w"))
            {
                hits.Add((i, j + 1, 1, $"前日（{j + 1}日）に置けない勤務が入っています"));
                if (ui.LockedWishKeys.Contains(key)) hits.Add((i, j, 1, $"翌日（{j + 2}日）の希望の勤務の前日に置けない勤務の希望です"));
            }
            if (fams.Contains("vio-c3n") && ui.Wishes.ContainsKey(key)) hits.Add((i, j, 2, "希望が禁止の並びに掛かっています"));
        }
        var pinned = (ui.CoverageDiag?.Shortfalls ?? Array.Empty<CoverageShortfall>()).Where(s => s.WishPinned.Count > 0)
            .OrderBy(s => s.DayIndex).ThenBy(s => s.ShiftIndex).ToList();
        var pinnedKeys = pinned.SelectMany(s => s.WishPinned.Select(i => (i, s.DayIndex))).ToHashSet();
        string Name(int i) => i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
        var direct = hits.GroupBy(h => (h.Staff, h.Day)).Select(g =>
        {
            var rep = g.OrderBy(h => h.Prio).First();
            var others = g.Select(h => h.Prio).Distinct().Where(p => p != rep.Prio).OrderBy(p => p).Select(p => shortNames[p]).ToList();
            if (pinnedKeys.Contains(g.Key)) others.Add("人手不足の日");
            var reason = others.Count == 0 ? rep.Reason : $"{rep.Reason}（ほか: {string.Join("・", others)}）";
            return new WishTrialRow(g.Key.Staff, g.Key.Day, Name(g.Key.Staff), reason, ui.LockedWishKeys.Contains($"{g.Key.Staff},{g.Key.Day}"));
        }).OrderBy(r => r.Staff).ThenBy(r => r.Day).ToList();
        var directKeys = direct.Select(r => (r.Staff, r.Day)).ToHashSet();
        var shortfall = pinned.Select(s =>
        {
            var rows = s.WishPinned.OrderBy(i => i).Where(i => !directKeys.Contains((i, s.DayIndex))).Select(i =>
            {
                var sym = ui.Wishes.TryGetValue($"{i},{s.DayIndex}", out var k) && k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : "別の勤務";
                return new WishTrialRow(i, s.DayIndex, Name(i), $"{sym}の希望", ui.LockedWishKeys.Contains($"{i},{s.DayIndex}"));
            }).ToList();
            return new ShortfallWishGroup(s.DayIndex, s.ShiftIndex, $"{s.DayLabel} {s.ShiftSymbol} {s.Miss}人不足", rows);
        }).Where(g => g.Rows.Count > 0).ToList();
        return new WishTrialCandidates(direct, shortfall);
    }

    /// <summary>[S5] 試算結果 1 行の文（§5 の表）。止めた試算は null（数字を出さない）。</summary>
    public static string? WishTrialText(WishTrial.Outcome o) => o switch
    {
        WishTrial.Result r when r.Rk >= r.H0 && r.Att <= 0 => "この試算では、減る見込みは見つかりませんでした（もう一度つくると減ることはあります）。",
        WishTrial.Result r when r.Rk >= r.H0 && r.APrime > 0 && r.B > 0 => $"取り消すと必須違反が確実に{r.APrime}件 減り、もう一度つくるとさらに{r.B}件 減る見込みです。",
        WishTrial.Result r when r.Rk >= r.H0 && r.APrime > 0 => $"取り消すと必須違反が確実に{r.APrime}件 減ります。",
        WishTrial.Result r when r.Rk >= r.H0 => $"取り消してもう一度つくると、必須違反が{r.B}件 減る見込みです。",
        WishTrial.Result r when r.Att > 0 => $"もう一度つくるだけの場合より、さらに{r.Att}件 減る見込みです。",
        WishTrial.Result => "取り消さなくても、もう一度つくるだけで同じだけ減る見込みです。",
        WishTrial.Unavailable u => $"試算できませんでした（{u.Reason}）。",
        _ => null,
    };

    /// <summary>[S5] Rk &lt; H0 の盤面でダイアログの先頭に出す文（§5）。対照だけで減らないなら null。</summary>
    public static string? WishTrialKeepOnlyText(WishTrial.Control control) =>
        control.Rk < control.H0 ? $"希望を残したまま、もう一度つくるだけで必須違反が{control.H0 - control.Rk}件 減る見込みです。" : null;
}
