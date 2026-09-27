using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>日付の表記の単一ソース（利用者決定 2026-09-26）: 既定「10/8(木)」、密な所（手順・一覧）「10/8」、範囲「10/8〜10/9」。開始日が読めなければ「8日」。Kotlin <c>DayText</c>。</summary>
public static class DayText
{
    public static string Full(string startDate, int j) => ScheduleUtil.FormatDay(startDate, j);
    public static string Short(string startDate, int j) { var f = Full(startDate, j); var q = f.IndexOf('('); return q >= 0 ? f[..q] : f; }
    public static string Range(string startDate, int a, int b) => a == b ? Full(startDate, a) : $"{Short(startDate, a)}〜{Short(startDate, b)}";
}

/// <summary>[Android 3.612.0 思考誘導S3] 残っている必須違反に関わる希望 1 件（職員・日・理由）。Kotlin <c>InvolvedWish</c>。</summary>
public sealed record InvolvedWish(int Staff, int Day, string Name, string Reason);

/// <summary>
/// [Android 3.612.0 思考誘導] ホームの主ボタンとその行き先が使う、画面に依存しない判定。
/// Kotlin <c>fixImpactLines</c>（BreakdownLabels.kt）と <c>involvedWishes</c>（MagiViewState.kt）の移植。
/// 族名の日本語は UI 層が持つので <c>labelOf</c> で受け取る（<see cref="AnalysisTriage"/> と同じ形）。
/// </summary>
/// <summary>[S5] 試算の候補 1 行。Locked=false（担当できない勤務の希望）は試算ボタンを出さず <see cref="NextActionGuide.WishTrialNotLocked"/> を出す。</summary>
public sealed record WishTrialRow(int Staff, int Day, string Name, string Reason, bool Locked, bool Pinned = false);

/// <summary>[S5b] 人員不足の枠 1 つ（見出し「12日 日勤 1人不足」）と、その日に別の勤務で希望固定されている人の行（職員順）。</summary>
public sealed record ShortfallWishGroup(int Day, int Shift, string Header, IReadOnlyList<WishTrialRow> Rows);

public sealed record WishTrialCandidates(IReadOnlyList<WishTrialRow> Direct, IReadOnlyList<ShortfallWishGroup> Shortfall)
{
    public bool IsEmpty => Direct.Count == 0 && Shortfall.All(g => g.Rows.Count == 0);
}

/// <summary>[S6] 起点の違反の呼び方（名前・日の範囲・族名）。ダイアログの題とホームの見出しが共有する。Kotlin <c>RelaxTarget</c>。</summary>
public sealed record RelaxTarget(string Name, string Span, string What);

/// <summary>[S6] 試算ダイアログの文（<c>docs/s6_relax_trial.md</c> §5）。Kotlin <c>RelaxTrialText</c>。盤面は持たない＝手順は言葉だけ。</summary>
public sealed record RelaxTrialText(
    string Title, string DialogTitle, string HardLine, string ScaleLine, IReadOnlyList<string> PrerequisiteRows, string Lead,
    IReadOnlyList<string> Rows, string SolveNote, IReadOnlyList<string> MoveLines, IReadOnlyList<string> OtherMoveLines, int OtherMoves, string? KeepNote);

/// <summary>[S6] ホームの次にやることカードの文（見出し・本文・残る件数の注記）。Kotlin <c>RelaxCardText</c>。</summary>
public sealed record RelaxCardText(string Headline, string Body, string? Note);

public static class NextActionGuide
{
    public const string WishTrialNotLocked = "担当できない勤務の希望なので、取り消しても勤務表は変わりません。";
    /// <summary>[#41] 手動固定のセルは試算の候補にしない（LockedWishKeys が外す）＝担当外と混ぜず、固定が理由だと言う。</summary>
    public const string WishTrialPinned = "このセルは手動固定のため、自動では変更しません。固定を外すと試算できます。";
    /// <summary>希望タブの注記（希望が必須違反の並びに掛かっているとき）。希望を変えても盤面のセルはそのまま＝黙って崩さない。</summary>
    public const string WishTabKeepNote = "希望を変えても勤務表のセルはそのままです（未反映になります）。もう一度つくると希望に合わせます。";
    public static string? WishTabInvolvedLine(string wishSymbol, IReadOnlyList<string> families) =>
        families.Contains("c3n") ? $"{wishSymbol}はこの禁止の並びに関係しています。"
        : families.Contains("c3w") ? $"{wishSymbol}はこの希望の前日の禁止に関係しています。"
        : null;
    public const string WishKeepFooter = "希望は、あなたが選ぶまで取り消しません。";
    public const string WishToRelaxLabel = "希望を残したまま、設定を緩めて試す";
    /// <summary>[S5b] 1 枠に並べる行の上限（超えたら「ほか N人」）。</summary>
    public const int WishTrialGroupLimit = 8;
    public const string RelaxWishLine = "希望: 変更しません";
    public const string RelaxPrereqHead = "前提として上限を上げる設定";
    public const string RelaxPrereqWhy = "いま手で置いてある勤務に合わせます（もう一度つくったときに手置きの勤務が外れないため）。";
    public const string RelaxSetHead = "解消に使う設定";

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
    /// 希望の前日の禁止(c3w)の翌日の希望（c3w の印は前日側のセルに付く）、または禁止の並び(c3n)が本人の希望のセルに掛かっている。
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
            if (fams.Contains("vio-c3w")) list.Add(new InvolvedWish(i, j + 1, name, $"前日（{DayText.Short(ui.StartDate, j)}）に置けない勤務が入っています"));
            if (fams.Contains("vio-c3n") && ui.Wishes.ContainsKey(key)) list.Add(new InvolvedWish(i, j, name, "希望が禁止の並びに掛かっています"));
        }
        return list.Distinct().OrderBy(w => w.Staff).ThenBy(w => w.Day).ToList();
    }

    /// <summary>
    /// [S5] 試算の候補（Kotlin <c>wishTrialCandidates</c>、§2.2・§2.3）。S5a＝必須違反に関わる希望を (職員, 日) で重複除去し、
    /// 代表の理由を pref＞c3w＞c3n で選ぶ（他は「ほか: …」）。c3w は翌日の希望 X と、印の付く前日自身が WishLocked の希望 Y の両方。
    /// 満たされない希望が希望どうしの衝突（<c>UiState.WishSelfConflicts</c>）の組に入っていれば、組のほかの希望も S5a の行にする（§2.2）。
    /// S5b＝人員不足の枠の WishPinned（日→シフト、職員順）。S5a と重なる (職員, 日) は S5a を代表にし「ほか: 人員不足の日」を足す。
    /// </summary>
    public static WishTrialCandidates WishTrialCandidatesOf(UiState ui)
    {
        // 優先度（小さいほど代表）と「ほか」に出す短い名前。
        string[] shortNames = { "希望の勤務になっていない", "前日の禁止", "禁止の並び", "希望どうし" };
        var hits = new List<(int Staff, int Day, int Prio, string Reason)>();
        foreach (var (key, fams) in ui.ViolationCellFamilies)
        {
            var parts = key.Split(',');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var j)) continue;
            if (fams.Contains("vio-pref")) hits.Add((i, j, 0, "希望の勤務になっていません"));
            if (fams.Contains("vio-c3w"))
            {
                hits.Add((i, j + 1, 1, $"前日（{DayText.Short(ui.StartDate, j)}）に置けない勤務が入っています"));
                if (ui.LockedWishKeys.Contains(key)) hits.Add((i, j, 1, $"翌日（{DayText.Short(ui.StartDate, j + 1)}）の希望の勤務の前日に置けない勤務の希望です"));
            }
            if (fams.Contains("vio-c3n") && ui.Wishes.ContainsKey(key)) hits.Add((i, j, 2, "希望が禁止の並びに掛かっています"));
        }
        var hitKeys = hits.Select(h => (h.Staff, h.Day)).ToHashSet();
        var prefKeys = ui.ViolationCellFamilies.Where(kv => kv.Value.Contains("vio-pref")).Select(kv => kv.Key).ToHashSet();
        var siblings = ui.WishSelfConflicts.Where(g => g.WishKeys.Any(prefKeys.Contains)).SelectMany(g =>
        {
            var pat = string.Join("→", g.Shifts.Select(k => k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : "?"));
            var reason = g.Family == "c3w" ? $"希望どうしが前日の禁止「{pat}」に当たっています" : $"希望どうしが禁止の並び「{pat}」を作っています";
            return g.Days.Where(d => !hitKeys.Contains((g.Staff, d))).Select(d => (Staff: g.Staff, Day: d, Prio: 3, Reason: reason));
        }).DistinctBy(h => (h.Staff, h.Day)).ToList();
        hits.AddRange(siblings);
        var pinned = (ui.CoverageDiag?.Shortfalls ?? Array.Empty<CoverageShortfall>()).Where(s => s.WishPinned.Count > 0)
            .OrderBy(s => s.DayIndex).ThenBy(s => s.ShiftIndex).ToList();
        var pinnedKeys = pinned.SelectMany(s => s.WishPinned.Select(i => (i, s.DayIndex))).ToHashSet();
        string Name(int i) => i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
        var direct = hits.GroupBy(h => (h.Staff, h.Day)).Select(g =>
        {
            var rep = g.OrderBy(h => h.Prio).First();
            var others = g.Select(h => h.Prio).Distinct().Where(p => p != rep.Prio).OrderBy(p => p).Select(p => shortNames[p]).ToList();
            if (pinnedKeys.Contains(g.Key)) others.Add("人員不足の日");
            var reason = others.Count == 0 ? rep.Reason : $"{rep.Reason}（ほか: {string.Join("・", others)}）";
            return new WishTrialRow(g.Key.Staff, g.Key.Day, Name(g.Key.Staff), reason, ui.LockedWishKeys.Contains($"{g.Key.Staff},{g.Key.Day}"), ui.ManualPins.Contains($"{g.Key.Staff},{g.Key.Day}"));
        }).OrderBy(r => r.Staff).ThenBy(r => r.Day).ToList();
        var directKeys = direct.Select(r => (r.Staff, r.Day)).ToHashSet();
        var shortfall = pinned.Select(s =>
        {
            var rows = s.WishPinned.OrderBy(i => i).Where(i => !directKeys.Contains((i, s.DayIndex))).Select(i =>
            {
                var sym = ui.Wishes.TryGetValue($"{i},{s.DayIndex}", out var k) && k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : "別の勤務";
                return new WishTrialRow(i, s.DayIndex, Name(i), $"{sym}の希望", ui.LockedWishKeys.Contains($"{i},{s.DayIndex}"), ui.ManualPins.Contains($"{i},{s.DayIndex}"));
            }).ToList();
            return new ShortfallWishGroup(s.DayIndex, s.ShiftIndex, $"{s.DayLabel} {s.ShiftSymbol} {s.Miss}人不足", rows);
        }).Where(g => g.Rows.Count > 0).ToList();
        return new WishTrialCandidates(direct, shortfall);
    }

    /// <summary>[S5] 試算結果 1 行の文（§5 の表）。止めた試算は null（数字を出さない）。</summary>
    public static string? WishTrialText(WishTrial.Outcome o) => o switch
    {
        WishTrial.Result r when r.Rk >= r.H0 && r.Att <= 0 => $"この希望を取り消しても、必須は減らない見込みです（必須 {r.H0}件 → {r.PCancel}件）。これは全探索で解けない証明ではありません。",
        WishTrial.Result r when r.Rk >= r.H0 && r.APrime > 0 && r.B > 0 => $"取り消すと必須違反が確実に{r.APrime}件 減り、もう一度つくるとさらに{r.B}件 減る見込みです。",
        WishTrial.Result r when r.Rk >= r.H0 && r.APrime > 0 => $"取り消すと必須違反が確実に{r.APrime}件 減ります。",
        WishTrial.Result r when r.Rk >= r.H0 => $"取り消してもう一度つくると、必須違反が{r.B}件 減る見込みです。",
        WishTrial.Result r when r.Att > 0 => $"もう一度つくるだけの場合より、さらに{r.Att}件 減る見込みです。",
        WishTrial.Result => "取り消さなくても、もう一度つくるだけで同じだけ減る見込みです。",
        WishTrial.Unavailable u => $"試算できませんでした（{u.Reason}）。",
        _ => null,
    };

    /// <summary>[S5] 取り消しても必須が減らない見込みの行（S6 の組があれば「希望を残したまま、設定を緩めて試す」を添える）。</summary>
    public static bool WishTrialNoGain(WishTrial.Outcome o) => o is WishTrial.Result r && r.Rk >= r.H0 && r.Att <= 0;

    /// <summary>[S5] Rk &lt; H0 の盤面でダイアログの先頭に出す文（§5）。対照だけで減らないなら null。</summary>
    public static string? WishTrialKeepOnlyText(WishTrial.Control control) =>
        control.Rk < control.H0 ? $"希望を残したまま、もう一度つくるだけで必須違反が{control.H0 - control.Rk}件 減る見込みです。" : null;

    private static IReadOnlyList<string> FamsAt(UiState ui, int i, int j) =>
        ui.ViolationCellFamilies.TryGetValue($"{i},{j}", out var f) ? f : Array.Empty<string>();

    /// <summary>[S6] 起点の違反の呼び方（Kotlin <c>relaxTarget</c>）。族名は下流の語彙（<c>BreakdownLabels</c>）を labelOf で受ける。</summary>
    public static RelaxTarget RelaxTargetOf(RelaxTrial.Result r, UiState ui, Func<string, string> labelOf)
    {
        var name = r.Staff < ui.StaffNames.Count ? ui.StaffNames[r.Staff] : $"職員{r.Staff + 1}";
        var fams = FamsAt(ui, r.Staff, r.Day);
        var fam = new[] { "c3n", "c3w", "pref", "groupViol" }.FirstOrDefault(f => fams.Contains("vio-" + f)) ?? "groupViol";
        var hardDays = Enumerable.Range(r.WindowFirst, r.WindowLast - r.WindowFirst + 1)
            .Where(j => FamsAt(ui, r.Staff, j).Any(v => MirrorKeys.Hard.Contains(VioBuckets.FamilyOfVioClass(v)))).ToList();
        var span = hardDays.Count > 1 ? DayText.Range(ui.StartDate, hardDays[0], hardDays[^1]) : DayText.Full(ui.StartDate, r.Day);
        return new RelaxTarget(name, span, labelOf(fam));
    }

    /// <summary>
    /// [S6] 結果を文にする（Kotlin <c>relaxTrialText</c>）。上限の行は 0→1。当てた後の回数が 2 回以上になる行は
    /// 要調整（上限超過）に数えることを添える。組は探索が見つけた十分条件＝「この組で」と言い、最小とは言わない。
    /// 手順は起点の窓の日をそのまま、窓の外は畳んで <see cref="RelaxTrialText.OtherMoveLines"/> に全件（確定の前に全部読める）。
    /// </summary>
    public static RelaxTrialText RelaxTrialTextOf(RelaxTrial.Result r, UiState ui, Func<string, string> labelOf)
    {
        string Name(int i) => i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
        string Sym(int k) => k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : "?";
        var board = ui.Schedule.Select(row => row.ToArray()).ToArray();
        var after = RelaxTrial.ApplyMoves(board, r.Moves, (i, j) => ui.ManualPins.Contains($"{i},{j}")) ?? board;
        var target = RelaxTargetOf(r, ui, labelOf);
        string Row(RelaxTrial.Relax x)
        {
            var n = x.Staff < after.Length ? after[x.Staff].Count(v => v == x.Shift) : 0;
            var note = n > x.NewHi ? $"（この月は {n}回になります。要調整に数えます）" : "";
            return $"{Name(x.Staff)} {Sym(x.Shift)} 上限 0→{x.NewHi}{note}";
        }
        var preRows = r.Prerequisite.Select(x =>
        {
            var days = x.Staff < board.Length ? Enumerable.Range(0, board[x.Staff].Length).Where(j => board[x.Staff][j] == x.Shift) : Enumerable.Empty<int>();
            return $"{Row(x)}（{string.Join("・", days.Select(d => DayText.Short(ui.StartDate, d)))} に置いてあります）";
        }).ToList();
        IReadOnlyList<string> DayLines(IEnumerable<RelaxTrial.Move> ms) => ms.GroupBy(m => m.Day).OrderBy(g => g.Key)
            .Select(g => $"{DayText.Short(ui.StartDate, g.Key)}　" + string.Join("、", g.Select(m => $"{Name(m.Staff)} {Sym(m.From)}→{Sym(m.To)}"))).ToList();
        var inWin = r.Moves.Where(m => m.Day >= r.WindowFirst && m.Day <= r.WindowLast).ToList();
        var outWin = r.Moves.Where(m => m.Day < r.WindowFirst || m.Day > r.WindowLast).ToList();
        var lead = r.Prerequisite.Count == 0 ? $"この組で緩めると、必須違反が {r.Att}件 減る見込みです。"
            : $"手で置いた勤務に合わせて上限を上げ、この組も緩めると、必須違反が {r.Att}件 減る見込みです。";
        var keep = r.Rk > r.H0 ? $"設定をそのままにもう一度つくると、手で置いた勤務が外されて必須違反が {r.Rk}件 に増えます（元の勤務表が残ります）。" : null;
        var people = r.Prerequisite.Concat(r.Relaxes).Select(x => x.Staff).Concat(r.Moves.Select(m => m.Staff)).Distinct().Count();
        return new RelaxTrialText(
            Title: $"{target.Name} {target.Span}　{target.What}",
            DialogTitle: $"設定を緩める候補 — {target.Name} {target.Span} {target.What}",
            HardLine: $"必須違反: {r.H0}件 → {r.Rr}件",
            ScaleLine: $"変更規模: 設定 {r.Prerequisite.Count + r.Relaxes.Count}項目・{people}人・{r.Moves.Count}セル",
            PrerequisiteRows: preRows,
            Lead: lead,
            Rows: r.Relaxes.Select(Row).ToList(),
            SolveNote: $"この{target.What}を解消できます。" + (r.Rr > 0 ? $"他の必須違反 {r.Rr}件 は残ります。" : "必須違反はなくなる見込みです。"),
            MoveLines: DayLines(inWin),
            OtherMoveLines: DayLines(outWin),
            OtherMoves: outWin.Count,
            KeepNote: keep);
    }

    public const string RelaxSearchingText = "希望を変えずに、設定側で直す方法を調べています…";

    /// <summary>[S6] ホームの次にやることカードの文（Kotlin <c>relaxCardText</c>）。起点の違反と件数を名指しする。</summary>
    public static RelaxCardText RelaxCardTextOf(RelaxTrial.Result r, UiState ui, Func<string, string> labelOf)
    {
        var t = RelaxTargetOf(r, ui, labelOf);
        return new RelaxCardText(
            Headline: $"{t.Name} {t.Span}の{t.What}（必須 {r.H0}件中 {r.H0 - r.Rr}件）は、設定が壁になっています",
            Body: "希望を残したまま、設定と勤務表を手順で変えられます",
            Note: r.Rr > 0 ? $"残りの必須違反 {r.Rr}件はそのまま残ります" : null);
    }

    /// <summary>[S6 §9] 確定の後、次にやることカードに出す 1 行。</summary>
    public static string RelaxDoneLine(int h0, int after) =>
        $"設定を緩めて手順を当てました: 必須違反 {h0} → {after}。元に戻すで設定と勤務表をまとめて戻せます。";
}
