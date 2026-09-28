using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>シフトボタン 1 枠。CanDo=false は枠を残したまま「外」で灰色にする。</summary>
public sealed record ShiftSlot(int Shift, bool CanDo);

public enum CellSeverity { Hard, Soft, None }

/// <summary>直し方探しの状態（計算・チェック待ち／未開始／探索中／完了／失敗）。</summary>
public enum FixPanelState { WaitCheck, NotStarted, Running, Done, Failed }

/// <summary>[S6] セルシートから設定の緩和へ渡す状態。Kotlin <c>RelaxHandoff</c>。</summary>
public enum RelaxHandoff { None, Searching, Offer, NoWall }

/// <summary>操作の通知。<paramref name="UndoSerial"/>＝その操作が積んだ元に戻すの段。</summary>
public sealed record OpNotice(long Id, string Text, long UndoSerial);

/// <summary>Cause は接頭辞（必須/要調整）を除いた原因だけ（希望を守っている板挟みの 2 行目に使う）。</summary>
public sealed record CellStatus(CellSeverity Severity, string Text, string Cause = "");

/// <summary>巡回の 1 件＝必須違反 1 件（族・職員・関連セルの日・見出し）。1 セルが 2 件に属せば 2 回止まる（<c>report.hard</c> の数え方と同じ）。
/// セルで辿れる族だけ（c3n＝並びの全日、c3w＝前日＋希望の翌日、pref/groupViol＝1 セル）。人員不足は日ヘッダから＝件数は別に添える。Kotlin <c>TourItem</c>。</summary>
public sealed record TourItem(string Family, int Staff, IReadOnlyList<int> Days, string Heading)
{
    public (int I, int J) Cell => (Staff, Days[0]);
}

/// <summary>シフトボタンの印。Recommended＝緑の点、HardRisk＝置くと必須の族が増える（警告の印）。</summary>
public sealed record ShiftMarks(IReadOnlySet<int> Recommended, IReadOnlySet<int> HardRisk)
{
    public static readonly ShiftMarks Empty = new(new HashSet<int>(), new HashSet<int>());
}

/// <summary>
/// 勤務表のセル編集シートの純ロジック。Kotlin <c>CellSheetLogic.kt</c> の移植（固定配置・1 行の状態・印・巡回・回数の 1 行）。
/// 族名の日本語化は View 層にあるので labelOf で受ける。
/// </summary>
public static class CellSheetLogic
{
    public const int Columns = 4;

    /// <summary>枠に出すシフト＝データの中で誰か 1 人でも担当できるもの（データごとに 1 回。誰も担当できなければ全部）。</summary>
    public static IReadOnlyList<int> SheetShifts(int shiftCount, IEnumerable<IEnumerable<int>> allowedByStaff)
    {
        var any = allowedByStaff.SelectMany(x => x).ToHashSet();
        var shown = Enumerable.Range(0, shiftCount).Where(any.Contains).ToList();
        return shown.Count > 0 ? shown : Enumerable.Range(0, shiftCount).ToList();
    }

    /// <summary>枠は shifts の順で固定。行の末尾の空きは null。leftHand は各行を空きも含めて左右反転する。</summary>
    public static IReadOnlyList<IReadOnlyList<ShiftSlot?>> Slots(IReadOnlyList<int> shifts, IReadOnlySet<int> canDo, bool leftHand = false) =>
        shifts.Select(k => new ShiftSlot(k, canDo.Contains(k))).Chunk(Columns).Select(row =>
        {
            var r = row.Cast<ShiftSlot?>().Concat(Enumerable.Repeat<ShiftSlot?>(null, Columns - row.Length)).ToList();
            if (leftHand) r.Reverse();
            return (IReadOnlyList<ShiftSlot?>)r;
        }).ToList();

    /// <summary>セル・人員・回数の族を重い順に（族キー、vio- なし）。</summary>
    public static IReadOnlyList<string> StatusFamilies(IEnumerable<string> cellClasses, IEnumerable<string> needClasses, IEnumerable<string> countClasses) =>
        cellClasses.Concat(needClasses).Concat(countClasses).Select(VioBuckets.FamilyOfVioClass).Distinct()
            .OrderByDescending(MirrorKeys.WeightOf).ToList();

    /// <summary>1 行の状態。最も重い族について、原因と関わる人・日・数をチェッカーと同じ盤面から言う。</summary>
    /// <summary>[#41] 手動固定のセルに違反が残るときの状態の 1 行の言い方（「直せない」と言わない＝周囲のセルを変えて解消できる場合はある）。</summary>
    public const string PinBlockedNote = "このセルは手動固定のため、自動では変更しません";

    public const string ZeroCapText = "個人の上限0（入れない指定）のシフトです。残るのは要調整です";
    public const string ZeroCapWishText = "本人の希望が個人の上限0（入れない指定）のシフトに載っています。残るのは要調整です";

    public static CellStatus StatusLine(MagiState state, Problem p, int[][] s, int i, int j, IReadOnlyList<string> families, Func<string, string> labelOf)
    {
        if (families.Count == 0) return new CellStatus(CellSeverity.None, "違反なし");
        var top = families[0];
        var hard = families.Any(f => MirrorKeys.Hard.Contains(f));
        var detail = FamilyDetail(state, p, s, i, j, top, labelOf) ?? labelOf(top);
        // [#41] 手動固定のセルは違反を数えて見せたまま、最適化器がこのセルを動かさないことを言う。
        var more = (families.Count > 1 ? $"（ほか{families.Count - 1}件）" : "") +
            (i >= 0 && i < p.S && j >= 0 && j < p.T && p.Pinned(i, j) ? $"。{PinBlockedNote}" : "");
        return hard ? new CellStatus(CellSeverity.Hard, $"⚠ 必須：{detail}{more}", detail + more)
            : new CellStatus(CellSeverity.Soft, $"⚠ 要調整：{detail}{more}", detail + more);
    }

    private static string? FamilyDetail(MagiState state, Problem p, int[][] s, int i, int j, string fam, Func<string, string> labelOf)
    {
        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : "?";
        string Name(int x) => x >= 0 && x < state.StaffList.Count ? state.StaffList[x].Name : $"#{x}";
        string Day(int d) => DayText.Short(state.StartDate, d);
        string DayFull(int d) => DayText.Full(state.StartDate, d);
        var cur = i < s.Length && j < s[i].Length ? s[i][j] : -1;
        int Count(int k) => s[i].Count(x => x == k);
        switch (fam)
        {
            case "c3w":
                return j + 1 < p.T && p.Wish[i][j + 1] >= 0 ? $"翌日({Sym(p.Wish[i][j + 1])})への前日禁止（{Sym(cur)}）" : null;
            case "c3n":
            case "c3mn":
            {
                var run = ForbiddenRunAt(p, s, i, j, fam == "c3n" ? p.Cons3n : p.Cons3mn);
                return run is null ? null : $"{labelOf(fam)} {string.Join("→", run.Value.Seq.Select(Sym))}（{Day(run.Value.J0)}〜{Day(run.Value.J0 + run.Value.Seq.Length - 1)}）";
            }
            case "c3":
            case "c3m":
            {
                var c = (fam == "c3" ? p.Cons3 : p.Cons3m).FirstOrDefault(x => x.Seq.Length > 0 && x.Seq[0] == cur);
                return c is null ? null : $"{labelOf(fam)} {string.Join("→", c.Seq.Select(Sym))} が{DayFull(j)}から続かない";
            }
            case "c42s":
            case "c42":
            {
                var skill = fam == "c42s";
                var grp = skill ? p.Ssk : p.Sgrp;
                var partners = new List<int>();
                foreach (var c in skill ? p.Cons42s : p.Cons42)
                    for (var x = 0; x < p.S; x++)
                    {
                        if (x == i) continue;
                        var xk = s[x][j];
                        var hit = (grp[i] == c.G1 && cur == c.S1 && grp[x] == c.G2 && xk == c.S2) ||
                                  (grp[i] == c.G2 && cur == c.S2 && grp[x] == c.G1 && xk == c.S1);
                        if (hit && !partners.Contains(x)) partners.Add(x);
                    }
                return partners.Count == 0 ? null
                    : string.Join("・", partners.Select(x => $"{Name(x)}({Sym(s[x][j])})")) + "との" + (skill ? "スキルグループペア禁止" : "グループペア禁止");
            }
            case "covU":
            case "covO":
            {
                if (cur < 0) return null;
                var lo = p.Need1[cur][j];
                var hi = p.Use2 && p.Need2[cur][j] >= 0 ? p.Need2[cur][j] : lo;
                var n = Enumerable.Range(0, p.S).Count(x => s[x][j] == cur);
                return fam == "covU" ? $"{DayFull(j)}の{Sym(cur)}が人員不足（必要{lo}人に{n}人）" : $"{DayFull(j)}の{Sym(cur)}が人員過剰（適正{hi}人に{n}人）";
            }
            case "c41s":
            case "c41":
            {
                if (cur < 0) return null;
                var skill = fam == "c41s";
                var grp = skill ? p.Ssk : p.Sgrp;
                var c = (skill ? p.Cons41s : p.Cons41).FirstOrDefault(x => x.ShiftIdx == cur && x.GroupIdx == grp[i]);
                if (c is null) return null;
                var n = Enumerable.Range(0, p.S).Count(x => grp[x] == c.GroupIdx && s[x][j] == cur);
                return $"{labelOf(fam)}：{Sym(cur)}が{n}人（{c.L}〜{c.U}人）";
            }
            case "c1":
                return C1Display.CellText(C1Display.Shortages(p, s), s, i, j, Sym, Day);
            case "pref":
                return p.Wish[i][j] >= 0 ? $"希望は{Sym(p.Wish[i][j])}（今は{Sym(cur)}）" : null;
            case "groupViol": return $"{Sym(cur)}は{Name(i)}の担当外";
            case "low": return cur < 0 ? null : $"{Sym(cur)}が{Count(cur)}回（下限{p.RangeLo[i][cur]}）";
            case "high":
                if (cur < 0) return null;
                if (p.RangeHi[i][cur] == 0 && cur != p.RestIdx) return p.Wish[i][j] == cur ? ZeroCapWishText : ZeroCapText;
                return $"{Sym(cur)}が{Count(cur)}回（上限{p.RangeHi[i][cur]}）";
            case "apt": return cur < 0 ? null : $"{Sym(cur)}が{Count(cur)}回（適切{p.Apt[i][cur]}回）";
            case "c2":
            {
                if (cur < 0) return null;
                var c = p.Cons2.FirstOrDefault(x => x.ShiftIdx == cur);
                return c is null ? null : $"{Sym(cur)}が{Count(cur)}回（個人の合計{c.Count}回）";
            }
            default: return null;
        }
    }

    /// <summary>セル (i,j) を含む、完全に一致した禁止の並び（最初の 1 つ）と開始日。</summary>
    public static (int[] Seq, int J0)? ForbiddenRunAt(Problem p, int[][] s, int i, int j, IReadOnlyList<C3> list)
    {
        foreach (var c in list)
        {
            var d = c.Seq.Length;
            for (var j0 = Math.Max(0, j - d + 1); j0 <= j; j0++)
            {
                if (j0 + d > p.T) continue;
                var ok = true;
                for (var l = 0; l < d && ok; l++) ok = s[i][j0 + l] == c.Seq[l];
                if (ok) return (c.Seq, j0);
            }
        }
        return null;
    }

    /// <summary>同じ違反のもう一方のセルの日（板挟みで他の人の手が無いときの行き先）。禁止の並びは一致した並びの他の日、
    /// 希望の前日の禁止は印のある前日⇄希望の翌日。チェッカーの印だけから決め、希望は触らない。Kotlin <c>violationPartnerDays</c>。</summary>
    public static IReadOnlyList<int> ViolationPartnerDays(Problem p, int[][] s, int i, int j, IReadOnlyList<string> families,
        IReadOnlyDictionary<string, IReadOnlyList<string>> cellFamilies)
    {
        var outSet = new HashSet<int>();
        foreach (var fam in families)
        {
            if (fam is "c3n" or "c3mn")
            {
                if (ForbiddenRunAt(p, s, i, j, fam == "c3n" ? p.Cons3n : p.Cons3mn) is { } run)
                    for (var d = run.J0; d < run.J0 + run.Seq.Length; d++) if (d != j) outSet.Add(d);
            }
            else if (fam == "c3w" && j + 1 < p.T) outSet.Add(j + 1);
        }
        if (j > 0 && cellFamilies.TryGetValue($"{i},{j - 1}", out var prev) && prev.Contains("vio-c3w")) outSet.Add(j - 1);
        return outSet.Where(d => d >= 0 && d < p.T).OrderBy(d => d).ToList();
    }

    public static string PartnerCellLabel(string startDate, int day, bool single) =>
        single ? $"同じ違反のもう一方のセル（{DayText.Full(startDate, day)}）を見る" : $"同じ違反のほかのセル（{DayText.Full(startDate, day)}）を見る";

    public const string SingleCellNote = "1 マスでは直りません。前後の日の組み合わせが必要です。";

    /// <summary>おすすめが無く、置ける候補（今の値を除く）がすべて必須を増やす印なら、この 1 マスだけでは直らない。印の計算前（空）は false。</summary>
    public static bool SingleCellHopeless(ShiftMarks marks, IEnumerable<int> candidates, int current)
    {
        var others = candidates.Where(k => k != current).ToList();
        return marks.Recommended.Count == 0 && others.Count > 0 && others.All(marks.HardRisk.Contains);
    }

    /// <summary>全部 ⚠ の理由 1 行（無ければ null）: 本人の希望のセル→希望と違う勤務、前日（翌日）から続く禁止の並び→その日の勤務を名指し。
    /// 候補ごとの増える必須の形が混ざるときは言わない（推測を書かない）。Kotlin <c>allRiskReason</c>。</summary>
    public static string? AllRiskReason(MagiState state, Problem p, int[][] s, int i, int j, ShiftMarks marks, IReadOnlyCollection<int> candidates)
    {
        var cur = s[i][j];
        if (!SingleCellHopeless(marks, candidates, cur)) return null;
        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : "?";
        if (p.Wish[i][j] == cur) return $"{Sym(cur)} は本人の希望なので、ほかへ変えると希望と違う勤務になります";
        var trial = s.Select(r => (int[])r.Clone()).ToArray();
        var fromPrev = true; var fromNext = true;
        foreach (var k in marks.HardRisk)
        {
            trial[i][j] = k;
            if (ForbiddenRunAt(p, trial, i, j, p.Cons3n) is not { } run) return null;
            if (!(run.J0 < j && trial[i][j - 1] == s[i][j - 1])) fromPrev = false;
            if (!(run.J0 == j && run.Seq.Length > 1)) fromNext = false;
        }
        var keep = string.Join("・", candidates.Where(k => !marks.HardRisk.Contains(k)).Select(Sym));
        var tail = keep.Length == 0 ? "どれに変えても禁止の並びになります" : $"{keep} 以外はどれも禁止の並びになります";
        return fromPrev && j > 0 ? $"前日が {Sym(s[i][j - 1])} なので、{tail}"
            : fromNext && j + 1 < p.T ? $"翌日が {Sym(s[i][j + 1])} なので、{tail}"
            : null;
    }

    /// <summary>状態の下の「関連セル: 10/9(金) A4（希望・反映済）」（同じ違反のもう一方のセル。無ければ null）。</summary>
    public static string? RelatedCellsLine(MagiState state, int[][] s, int i, IReadOnlyList<int> partners)
    {
        if (partners.Count == 0) return null;
        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : "?";
        return "関連セル: " + string.Join("、", partners.Select(d =>
        {
            var w = state.Wishes.TryGetValue($"{i},{d}", out var v) ? v : (int?)null;
            return DayText.Full(state.StartDate, d) + " " + Sym(s[i][d]) + (w is null ? "" : w == s[i][d] ? "（希望・反映済）" : "（希望・未反映）");
        }));
    }

    /// <summary>セル (i,j) を各候補にしたときの印（Kotlin <c>evaluateShiftMarks</c>）。stillWanted が false で打ち切る。</summary>
    public static ShiftMarks EvaluateShiftMarks(MagiState state, int[][] s, int i, int j, CellSeverity severity, IEnumerable<int> candidates,
        Func<bool>? stillWanted = null)
    {
        var cur = s[i][j];
        var bas = UnifiedViolationChecker.Check(state, s);
        var rec = new HashSet<int>();
        var risk = new HashSet<int>();
        var trial = s.Select(r => (int[])r.Clone()).ToArray();
        foreach (var k in candidates)
        {
            if (stillWanted is not null && !stillWanted()) return ShiftMarks.Empty;
            if (k == cur) continue;
            trial[i][j] = k;
            var r = UnifiedViolationChecker.Check(state, trial);
            var noNewHard = !MirrorKeys.Hard.Any(f => r.Breakdown.GetValueOrDefault(f) > bas.Breakdown.GetValueOrDefault(f));
            if (!noNewHard) { risk.Add(k); continue; }
            var better = severity switch
            {
                CellSeverity.Hard => r.Hard < bas.Hard,
                CellSeverity.Soft => r.WeightedScore < bas.WeightedScore,
                _ => false,
            };
            if (better) rec.Add(k);
        }
        return new ShiftMarks(rec, risk);
    }

    /// <summary>回数の行（例「Aｱ 7(適切8)▼ Dﾃ 4(下限5)▼」）。この職員の回数の族があるシフトだけ、目安（下限/上限/適切/合計）と ▼▲。
    /// 個人の上限0（休を除く）は「Cｱ 2回（上限0＝入れない指定）▲」（2 つ目からは「（上限0）」）。
    /// 行頭の「回数 」込みで 360dp の 2 行（<see cref="CountLineEm"/>）に収まらなければ目安を 1 字（下/上/適/計）に縮める。</summary>
    public static string StaffCountShort(MagiState state, Problem p, int[][] s, int i, IReadOnlyDictionary<string, IReadOnlyList<string>> countClasses)
    {
        string Build(bool lng)
        {
            var parts = new List<string>();
            var zeroSeen = false;
            for (var k = 0; k < p.K; k++)
            {
                if (!countClasses.TryGetValue($"{i},{k}", out var cls) || cls.Count == 0) continue;
                var fams = cls.Select(c => c.StartsWith("vio-", StringComparison.Ordinal) ? c[4..] : c).ToList();
                var n = s[i].Count(x => x == k);
                var sym = k < state.Shifts.Count ? state.Shifts[k].Kigou : "?";
                if (fams.Contains("high") && p.RangeHi[i][k] == 0 && k != p.RestIdx)
                {
                    parts.Add($"{sym} {n}回（{(zeroSeen ? "上限0" : ZeroCapNote)}）▲"); zeroSeen = true; continue;
                }
                string R(string l, string sh, int v) => $"{(lng ? l : sh)}{v}";
                (string Ref, bool Under) t =
                    fams.Contains("low") ? (R("下限", "下", p.RangeLo[i][k]), true) :
                    fams.Contains("high") ? (R("上限", "上", p.RangeHi[i][k]), false) :
                    fams.Contains("aptLow") ? (R("適切", "適", p.Apt[i][k]), true) :
                    fams.Contains("aptHigh") ? (R("適切", "適", p.Apt[i][k]), false) :
                    (p.Cons2.FirstOrDefault(c => c.ShiftIdx == k) is { } c2 ? R("合計", "計", c2.Count) : "", true);
                parts.Add($"{sym} {n}{(t.Ref.Length == 0 ? "" : $"({t.Ref})")}{(t.Under ? "▼" : "▲")}");
            }
            return string.Join(" ", parts);
        }
        var full = Build(true);
        return FitsTwoLines("回数 " + full, CountLineEm) ? full : Build(false);
    }

    public const string ZeroCapNote = "上限0＝入れない指定";

    /// <summary>セルシートの回数の行の 1 行の字数（360dp−左右 16dp＝328dp を 12sp で割った数）。</summary>
    public const double CountLineEm = 27.0;

    /// <summary>この職員の個人の上限0（休を除く・担当できるもの）のシフト。シフトボタンの「上限0」の添え字。</summary>
    public static IReadOnlySet<int> ZeroCapShifts(Problem p, int i) =>
        i < 0 || i >= p.S ? new HashSet<int>() : Enumerable.Range(0, p.K).Where(k => k != p.RestIdx && p.RangeHi[i][k] == 0 && p.CanDo(i, k)).ToHashSet();

    /// <summary>希望タブ: 希望が個人の上限0のシフトのときの 1 行（希望は優先して入る。残るのは要調整）。</summary>
    public static string WishZeroCapLine(string wishSymbol) => $"{wishSymbol}は個人の上限0（入れない指定）のシフトです。希望どおり入れると要調整に数えます";

    /// <summary>回数の行が 2 行に収まるか（全角 1・半角 0.5 の字幅で、<paramref name="emPerLine"/> 字ぶん × 2 行）。</summary>
    public static bool FitsTwoLines(string text, double emPerLine)
    {
        double line = 0; var lines = 1;
        foreach (var ch in text)
        {
            var w = ch < 0x80 || (ch >= '\uFF61' && ch <= '\uFF9F') ? 0.5 : 1.0;
            if (line + w > emPerLine) { lines++; line = w; } else line += w;
        }
        return lines <= 2;
    }

    /// <summary>日送りボタンの日付「10/7(水)」（範囲外は null）。</summary>
    public static string? AdjacentDayLabel(string startDate, int days, int j) => j < 0 || j >= days ? null : DayText.Full(startDate, j);

    public static string WishTabState(int? wish, int current) => wish is null ? "未登録" : wish == current ? "反映済" : "未反映";

    /// <summary>本人の希望どおりのセルに違反がある＝「他の人で補う」を先に出す。</summary>
    public static bool IsWishDilemma(int? wish, int current, CellSeverity severity) =>
        wish is { } w && w >= 0 && w == current && severity != CellSeverity.None;

    public static string WishKeptLine(string wishSymbol) => $"本人の希望（{wishSymbol}）を守っています";

    /// <summary>違反を順に見る巡回の順（日→職員）。必須のセルを先に、要調整は必須が 0 件か includeSoft のときだけ。</summary>
    public static IReadOnlyList<(int I, int J)> ViolationTour(UiState ui, bool includeSoft = false)
    {
        var cells = new List<(int I, int J, bool Hard)>();
        foreach (var (key, fams) in ui.ViolationCellFamilies)
        {
            var c = key.IndexOf(',');
            if (c < 0 || !int.TryParse(key.AsSpan(0, c), out var i) || !int.TryParse(key.AsSpan(c + 1), out var j)) continue;
            cells.Add((i, j, fams.Any(f => MirrorKeys.Hard.Contains(VioBuckets.FamilyOfVioClass(f)))));
        }
        var hard = cells.Where(x => x.Hard).ToList();
        var pick = hard.Count > 0 && !includeSoft ? hard : cells;
        return pick.OrderBy(x => !x.Hard).ThenBy(x => x.J).ThenBy(x => x.I).Select(x => (x.I, x.J)).ToList();
    }

    /// <summary>巡回を違反単位に（Kotlin <c>hardViolationItems</c>）。順は日→職員。族名は labelOf で受ける。</summary>
    public static IReadOnlyList<TourItem> HardViolationItems(MagiState state, Problem p, int[][] s,
        IReadOnlyDictionary<string, IReadOnlyList<string>> cellFamilies, Func<string, string> labelOf)
    {
        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : "?";
        string Span(IReadOnlyList<int> days) => DayText.Range(state.StartDate, days[0], days[^1]);
        var outMap = new Dictionary<string, TourItem>();
        foreach (var (key, fams) in cellFamilies)
        {
            var c = key.IndexOf(',');
            if (c < 0 || !int.TryParse(key.AsSpan(0, c), out var i) || !int.TryParse(key.AsSpan(c + 1), out var j)) continue;
            if (i < 0 || i >= p.S || j < 0 || j >= p.T) continue;
            foreach (var cls in fams)
            {
                var fam = VioBuckets.FamilyOfVioClass(cls);
                switch (fam)
                {
                    case "c3n":
                        if (ForbiddenRunAt(p, s, i, j, p.Cons3n) is { } run)
                        {
                            var days = Enumerable.Range(run.J0, run.Seq.Length).ToList();
                            outMap.TryAdd($"c3n,{i},{run.J0},{string.Join(",", run.Seq)}",
                                new TourItem(fam, i, days, $"{labelOf("c3n")} {string.Join("→", run.Seq.Select(Sym))} ・ {Span(days)}"));
                        }
                        break;
                    case "c3w":
                    {
                        var days = new[] { j, Math.Min(j + 1, p.T - 1) }.Distinct().ToList();
                        outMap.TryAdd($"c3w,{i},{j}", new TourItem(fam, i, days, $"{labelOf("c3w")} {Sym(s[i][j])}→{Sym(s[i][days[^1]])} ・ {Span(days)}"));
                        break;
                    }
                    case "pref":
                    case "groupViol":
                        outMap.TryAdd($"{fam},{i},{j}", new TourItem(fam, i, new[] { j }, $"{labelOf(fam)} {Sym(s[i][j])} ・ {DayText.Full(state.StartDate, j)}"));
                        break;
                }
            }
        }
        return outMap.Values.OrderBy(t => t.Days[0]).ThenBy(t => t.Staff).ToList();
    }

    /// <summary>巡回の見出し「必須違反 2 / 5 ・ 禁止の並び Dﾃ→A4 ・ 10/8〜10/9」。</summary>
    public static string? TourHeading(IReadOnlyList<TourItem> items, int at) =>
        at >= 0 && at < items.Count ? $"必須違反 {at + 1} / {items.Count} ・ {items[at].Heading}" : null;

    /// <summary>巡回に入らない人員不足の件数の 1 行（0 なら null）。</summary>
    public static string? TourCovULine(int covU) => covU > 0 ? $"ほかに人員不足 {covU}件（日ヘッダから）" : null;

    /// <summary>巡回の次のセル（今が巡回に無ければ先頭、末尾なら先頭へ）。空なら null。</summary>
    public static (int I, int J)? NextTourCell(IReadOnlyList<(int I, int J)> tour, (int I, int J)? current)
    {
        if (tour.Count == 0) return null;
        var at = current is null ? -1 : tour.ToList().IndexOf(current.Value);
        return tour[at < 0 ? 0 : (at + 1) % tour.Count];
    }

    /// <summary>「他の人で補う」: 本人を動かさず、その日のセルを含む手だけ。</summary>
    public static IReadOnlyList<FixSuggestion> FixesByOthers(IEnumerable<FixSuggestion> list, int day, int except) =>
        list.Where(s => s.Ops.All(o => o.Staff != except) && s.Ops.Any(o => o.Day == day)).ToList();

    /// <summary>セルを 1 つ変えたときの Snackbar 相当の文言（「元に戻す」付き）。</summary>
    /// <summary>セル詳細（「詳しく」）の行: 重なった族をすべて重い順に「必須・原因」「要調整・原因」。</summary>
    public static IReadOnlyList<string> CellDetailLines(MagiState state, Problem p, int[][] s, int i, int j, IReadOnlyList<string> families, Func<string, string> labelOf) =>
        families.Select(fam =>
        {
            var d = FamilyDetail(state, p, s, i, j, fam, labelOf) ?? labelOf(fam);
            return (MirrorKeys.Hard.Contains(fam) ? "必須・" : "要調整・") + d;
        }).ToList();

    /// <summary>セルシートの族のクラス。印の無い日でも不足区間の中なら期間の制約を読めるようにする（すでに数に入っている日など）。</summary>
    public static IReadOnlyList<string> SheetCellClasses(IReadOnlyList<string> display, bool inC1Shortage) =>
        !inC1Shortage || display.Contains("vio-c1") ? display : display.Append("vio-c1").ToList();

    /// <summary>その場の直し方探しの状態。スピナーは <see cref="FixPanelState.Running"/> だけ。</summary>
    public static FixPanelState PanelState(bool running, bool fixSearching, string doneKey, string failedKey, string key) =>
        fixSearching ? FixPanelState.Running
        : running ? FixPanelState.WaitCheck
        : doneKey == key ? FixPanelState.Done
        : failedKey == key ? FixPanelState.Failed
        : FixPanelState.NotStarted;

    /// <summary>[S6] セルシートから設定の緩和へ渡す状態（Kotlin <c>relaxHandoff</c>）。Offer＝ホームで見つかった組の起点の窓か手順のセル（同じ結果を同じ確定で開く。
    /// セルごとに試算はしない）、Searching＝背景で探している、NoWall＝探し終えて組が無い。</summary>
    /// <summary>ちら見に出す上位 <paramref name="n"/> シフト: 今の割当・希望を先に、残りは担当できるものを枠の順で（Kotlin <c>peekShifts</c>）。</summary>
    public static IReadOnlyList<int> PeekShifts(IReadOnlyList<int> shown, IReadOnlySet<int> canDo, int current, int? wish, int n = 4)
    {
        var head = new List<int>();
        if (current >= 0) head.Add(current);
        if (wish is >= 0) head.Add(wish.Value);
        return head.Concat(shown.Where(canDo.Contains)).Where(shown.Contains).Distinct().Take(n).ToList();
    }

    /// <summary>[S6] 既にある組の手順がこのセルを動かすなら、その行き先（新しく試算しない。Kotlin <c>peekRecommendation</c>）。</summary>
    public static int? PeekRecommendation(RelaxTrial.Result? r, int i, int j) =>
        r?.Moves.FirstOrDefault(m => m.Staff == i && m.Day == j)?.To;

    public static RelaxHandoff RelaxHandoffOf(RelaxTrial.Result? r, bool searching, bool noWall, int i, int j) =>
        r is not null && ((r.Staff == i && j >= r.WindowFirst && j <= r.WindowLast) || r.Moves.Any(m => m.Staff == i && m.Day == j)) ? RelaxHandoff.Offer
        : r is not null ? RelaxHandoff.None
        : searching ? RelaxHandoff.Searching
        : noWall ? RelaxHandoff.NoWall
        : RelaxHandoff.None;

    public static string RelaxHandoffLine(RelaxTrial.Result r, UiState ui, Func<string, string> labelOf) =>
        $"設定を緩めると、この{NextActionGuide.RelaxTargetOf(r, ui, labelOf).What}を解消できる見込みです（上限 {r.Relaxes.Count}件）";

    /// <summary>通知の「元に戻す」は、その操作が今も元に戻すの先頭にあるときだけ効く（後の別の操作を戻さない）。</summary>
    public static bool NoticeUndoApplies(long? topSerial, long noticeSerial) => topSerial is { } t && t == noticeSerial;

    /// <summary>操作の通知を出している間、通常の文言では置き換えない（失敗・拒否だけは置き換える）。</summary>
    public static bool MessageMayReplaceNotice(bool noticeShowing, bool isError) => !noticeShowing || isError;

    public static string CellChangedMessage(string name, string startDate, int day, string symbol) => $"{name} {DayText.Short(startDate, day)} を{symbol}に変更しました";
}
