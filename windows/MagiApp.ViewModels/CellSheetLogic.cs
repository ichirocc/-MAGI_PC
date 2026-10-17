using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>シフトボタン 1 枠。CanDo=false は枠を残したまま「外」で灰色にする。</summary>
public sealed record ShiftSlot(int Shift, bool CanDo);

public enum CellSeverity { Hard, Soft, None }

/// <summary>直し方探しの状態（計算・チェック待ち／未開始／探索中／完了／失敗）。</summary>
/// <summary>ホームの主カードの段（主ボタンの行き先）。文言と色は HomeView が段ごとに決める（Kotlin <c>HomePlanKind</c>）。</summary>
public enum HomePlanKind { Running, Make, Draft, PreDist, WishInfeasible, DataInfeasible, GuidedFix, ApplyMove, Searching, Relax, WishFloor, Wishes, List }

/// <summary>主ボタン（1 つだけ）と補助ボタンの行き先。</summary>
public enum HomeAction { None, Make, SmartInitial, PreDist, Schedule, ShowWishes, Landing, Fix, Setup, ShowMove, ApplyMove, ShowRelax, ShowList }

public sealed record HomePlan(HomePlanKind Kind, HomeAction Primary, bool PrimaryEnabled, HomeAction? Helper);

/// <summary>ホームの主カードの区画。並びの順は <see cref="CellSheetLogic.HomeCardSectionsOf"/> が決め、HomeView はその順に描くだけ（Kotlin <c>HomeSection</c> と 1 対 1）。</summary>
public enum HomeSection { Creation, Headline, Badges, Status, Progress, Primary, Helper, Others, Detail, Satisfaction, Info, DraftLink }

public enum FixPanelState { WaitCheck, NotStarted, Running, Done, Failed, Cancelled }

/// <summary>[S6] セルシートから設定の緩和へ渡す状態。Kotlin <c>RelaxHandoff</c>。</summary>
public enum RelaxHandoff { None, Searching, Offer, NoWall, Stopped, Unavailable, Failed }

/// <summary>操作の通知。<paramref name="UndoSerial"/>＝その操作が積んだ元に戻すの段。
/// <paramref name="Undoable"/>＝false は「元に戻す」を付けない結果の通知（元に戻す・やり直す自身の結果など）。</summary>
/// <summary>
/// 保存の結果からダイアログを決める（Kotlin <c>SaveFailNotifier</c> と 1 対 1）。成功から失敗に変わった瞬間に First を 1 回、
/// 失敗中に利用者がやり直して失敗したら Again。失敗が続くだけの自動保存では出さない（1.2 秒ごとに走るため）。
/// </summary>
public sealed class SaveFailNotifier
{
    public bool LastOk { get; private set; } = true;

    public SaveFailDialog? OnResult(bool ok, bool retried)
    {
        var prev = LastOk;
        LastOk = ok;
        return ok ? null : prev ? SaveFailDialog.First : retried ? SaveFailDialog.Again : null;
    }
}

public sealed record OpNotice(long Id, string Text, long UndoSerial, bool Undoable = true);

/// <summary>Cause は接頭辞（必須/要調整）を除いた原因だけ（希望を守っている板挟みの 2 行目に使う）。</summary>
public sealed record CellStatus(CellSeverity Severity, string Text, string Cause = "");

/// <summary>巡回の 1 件＝必須違反 1 件（族・職員・関連セルの日・見出し）。1 セルが 2 件に属せば 2 回止まる（<c>report.hard</c> の数え方と同じ）。
/// セルで辿れる族だけ（c3n＝並びの全日、c3w＝前日＋希望の翌日、pref/groupViol/extWish＝1 セル）。人員不足は日ヘッダから＝件数は別に添える。Kotlin <c>TourItem</c>。</summary>
/// <summary>勤務表タブの「必須 N ▼」一覧の 1 行。TourAt は HardViolationItems の何件目か（行を押すと巡回と同じ移動でそのセルを開く）。</summary>
public sealed record HardListRow(int TourAt, string Name, string Heading, bool WishOnly);

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

    /// <summary>職員 i の担当できるシフト（Kotlin <c>sheetCanDo</c>）。データ未読込（一覧が空）のときだけ全部。担当できるシフトが 0 件の職員は空のまま（「外」を押せる扱いにしない）。</summary>
    public static IReadOnlySet<int> SheetCanDo(IReadOnlyList<IReadOnlyCollection<int>> allowedByStaff, int i, int shiftCount) =>
        allowedByStaff.Count == 0 ? Enumerable.Range(0, shiftCount).ToHashSet()
        : i >= 0 && i < allowedByStaff.Count ? allowedByStaff[i].ToHashSet() : new HashSet<int>();

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
        string Group(int x) => x >= 0 && x < state.StaffList.Count && state.StaffList[x].GroupIdx >= 0 && state.StaffList[x].GroupIdx < state.Groups.Count
            && !string.IsNullOrWhiteSpace(state.Groups[state.StaffList[x].GroupIdx].Name) ? $"グループ「{state.Groups[state.StaffList[x].GroupIdx].Name}」" : "この職員";
        var cur = i < s.Length && j < s[i].Length ? s[i][j] : -1;
        int Count(int k) => s[i].Count(x => x == k);
        // 氏名とこの日はシートの見出しにある＝ここでは繰り返さない（見出しと違う日・範囲だけ書く）。
        switch (fam)
        {
            case "c3w":
                return j + 1 < p.T && p.Wish[i][j + 1] >= 0 ? $"{labelOf(fam)} {Sym(cur)}→{Sym(p.Wish[i][j + 1])}（{DayText.Range(state.StartDate, j, j + 1)}）" : null;
            case "extWish":
            {
                var banned = Enumerable.Range(0, p.K).Where(k => p.ExtBanned(i, j, k)).ToList();
                return banned.Count == 0 ? null : $"拡張希望（{string.Join("・", banned.Select(Sym))}以外）に{Sym(cur)}が入っています";
            }
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
                return c is null ? null : $"{labelOf(fam)} {string.Join("→", c.Seq.Select(Sym))} がこの日から続かない";
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
                return fam == "covU" ? $"{Sym(cur)}が人員不足（必要{lo}人に{n}人）" : $"{Sym(cur)}が人員過剰（適正{hi}人に{n}人）";
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
            case "groupViol": return $"{Sym(cur)}は{Group(i)}の担当外";
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
            else if (fam == "c3")
            {
                var d = p.Cons3.Count == 0 ? 2 : Math.Clamp(p.Cons3.Max(c => c.Seq.Length), 2, 3);
                for (var x = j - d + 1; x <= j + d - 1; x++) if (x != j) outSet.Add(x);
            }
        }
        if (j > 0 && cellFamilies.TryGetValue($"{i},{j - 1}", out var prev) && prev.Contains("vio-c3w")) outSet.Add(j - 1);
        return outSet.Where(d => d >= 0 && d < p.T).OrderBy(d => d).ToList();
    }

    public static string PartnerCellLabel(string startDate, int day, bool single) =>
        single ? $"同じ違反のもう一方のセル（{DayText.Full(startDate, day)}）を見る" : $"同じ違反のほかのセル（{DayText.Full(startDate, day)}）を見る";

    public const string SingleCellNote = "1 マスでは直りません。前後の日の組み合わせが必要です。";

    /// <summary>このセルが「足りない側」の族（Kotlin <c>shortSideFamilies</c>＝当日の自分の勤務の人員不足・自分の回数の下限割れ／目標未満）。セルの問題として赤で出さない。</summary>
    public static IReadOnlySet<string> ShortSideFamilies(IReadOnlyList<string> needClasses, IReadOnlyList<string> countClasses)
    {
        var s = new HashSet<string>();
        if (needClasses.Contains("vio-covU")) s.Add("covU");
        if (countClasses.Contains("vio-low")) s.Add("low");
        if (countClasses.Contains("vio-aptLow")) s.Add("apt");
        return s;
    }

    /// <summary>足りない側のセルの注意の 1 行。<paramref name="covGapAfter"/>＝変えたあとの当日の不足人数（人員不足の族が無ければ null）。</summary>
    public static string ShortSideCaution(string name, string sym, string dayShort, int? covGapAfter, int countAfter, string basis, int? target) =>
        $"{name}さんの{sym}は必要な勤務です（" + (
            covGapAfter is { } g ? $"変えると {dayShort} の {sym} の不足が {g}人に増えます"
            : target is { } t ? $"変えると {sym} が {countAfter}回になり、{basis} {t}回をさらに下回ります"
            : $"変えると {sym} の回数がさらに足りなくなります") + "）";

    /// <summary>足りない側のセルの主ボタン（当日のその勤務に入れる人を探す）。</summary>
    public static string ShortSideSearchButton(string dayShort, string sym) => $"{dayShort} の {sym} に入れる人を探す";

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

    /// <summary>希望タブの 1 行。拡張希望（この日はこのシフト以外）がある日は、その内容と反映状況も出す（Kotlin <c>wishTabLine</c>、3.645.2。
    /// 旧: 通常希望が無い日に拡張希望があっても「未登録」と言った）。</summary>
    public static string WishTabLine(int? wish, int current, IReadOnlySet<int>? extBanned, Func<int, string> sym, bool pinned)
    {
        var ext = extBanned is { Count: > 0 } b
            ? $"{string.Join("・", b.OrderBy(k => k).Select(sym))} 以外（{(b.Contains(current) ? "未反映" : "反映済")}）"
            : null;
        var core = wish is null && ext is not null
            ? $"希望 {ext}"
            : $"希望 {(wish is null ? "—" : sym(wish.Value))}（{WishTabState(wish, current)}）" + (ext is null ? "" : $"・{ext}");
        return core + (pinned ? "・手動固定" : "");
    }

    /// <summary>本人の希望どおりのセルに違反がある＝「他の人で補う」を先に出す。</summary>
    public static bool IsWishDilemma(int? wish, int current, CellSeverity severity) =>
        wish is { } w && w >= 0 && w == current && severity != CellSeverity.None;

    public static string WishKeptLine(string wishSymbol) => $"本人の希望（{wishSymbol}）を守っています";

    /// <summary>希望タブの注記の手動固定版（手動固定は希望より強い＝再作成でも希望へ戻さない。<c>docs/business-logic.md</c> の手動固定）。</summary>
    public const string WishTabPinnedNote = "希望を変えても勤務表のセルはそのままです（未反映になります）。このセルは手動固定のため、再作成しても希望には合わせません（固定を外すと合わせます）。";

    /// <summary>職員 <paramref name="i"/> の <paramref name="j"/> 日（0 始まり）が拡張希望の指定日なら、その日に禁止のシフト記号（シフト一覧の順）。指定日でなければ null。
    /// 日だけで決める＝希望を保存しない日（<c>ExtWishRules.WishBlockedBy</c>）と同じ。</summary>
    public static IReadOnlyList<string>? ExtWishDayKigou(IReadOnlyList<MagiViewModel.ExtWishView> extWishes, int i, int j, IReadOnlyList<string> shiftKigou)
    {
        var hits = extWishes.Where(e => e.I == i && e.Days.Contains(j + 1)).ToList();
        if (hits.Count == 0) return null;
        var ks = hits.SelectMany(e => e.Kigou).Distinct().ToList();
        return shiftKigou.Where(ks.Contains).Concat(ks.Where(k => !shiftKigou.Contains(k))).ToList();
    }

    public static string ExtWishDayNote(IReadOnlyList<string> kigou) =>
        $"この日は拡張希望{(kigou.Count == 0 ? "" : $"（{string.Join("・", kigou)} 以外）")}の指定日なので、希望は入れられません";

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
                    case "extWish":
                    {
                        var wish = fam == "pref" && p.Wish[i][j] >= 0 ? $"（希望は{Sym(p.Wish[i][j])}）" : "";
                        outMap.TryAdd($"{fam},{i},{j}", new TourItem(fam, i, new[] { j }, $"{labelOf(fam)} {Sym(s[i][j])}{wish} ・ {DayText.Full(state.StartDate, j)}"));
                        break;
                    }
                }
            }
        }
        return outMap.Values.OrderBy(t => t.Days[0]).ThenBy(t => t.Staff).ToList();
    }

    /// <summary>必須違反の一覧（巡回の項目を 1 件 1 行）。希望どうしのぶつかり（関連セルがすべて wishSelfKeys）は「希望のまま」を付けて後ろへ。</summary>
    public static IReadOnlyList<HardListRow> HardViolationRows(IReadOnlyList<TourItem> items, IReadOnlyList<string> staffNames, IReadOnlySet<string> wishSelfKeys) =>
        items.Select((it, at) => new HardListRow(at, it.Staff >= 0 && it.Staff < staffNames.Count ? staffNames[it.Staff] : $"職員{it.Staff + 1}",
            it.Heading, it.Days.All(d => wishSelfKeys.Contains($"{it.Staff},{d}")))).OrderBy(r => r.WishOnly).ToList();

    /// <summary>巡回の見出し「必須違反 2 / 5 ・ 禁止の並び Dﾃ→A4 ・ 10/8〜10/9」。</summary>
    public static string? TourHeading(IReadOnlyList<TourItem> items, int at) =>
        at >= 0 && at < items.Count ? $"必須違反 {at + 1} / {items.Count} ・ {items[at].Heading}" : null;

    /// <summary>巡回に入らない人員不足の件数の 1 行（0 なら null）。</summary>
    public static string? TourCovULine(int covU) => covU > 0 ? $"ほかに人員不足 {covU}人（日の印から開けます）" : null;

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

    /// <summary>直し方の探索の締切。セルのシート（手は 1 件でよい）は短く＝押してから 3 秒以内に答えが返る（Nielsen の 1 秒／10 秒の目安、3.646.0 実機報告）。
    /// ホーム・分析の全体探索は従来どおり 8 秒。</summary>
    public const long FixSearchMs = 8000L;
    public const long FixSearchQuickMs = 3000L;

    /// <summary>探索中の 1 行（最長の秒数を添えて待ち時間の見当をつけてもらう）。</summary>
    public static string FixSearchingText(bool quick) =>
        quick ? $"この場所の勤務変更案を探しています…（最長 {FixSearchQuickMs / 1000} 秒）" : $"勤務変更案を探しています…（最長 {FixSearchMs / 1000} 秒）";

    /// <summary>見ている問題がある探索中の 2 行（何を減らす案を探しているか）。</summary>
    public static IReadOnlyList<string> TargetSearchingLines(FixTarget t, string label) => new[]
    {
        $"{label}の{t.GapWord}を減らす変更案を探しています",
        "ほかの日や職員の変更が必要な場合は、関連する変更もまとめて確認します。",
    };

    /// <summary>案の変更箇所（「2人・3セル」）。</summary>
    public static string ChangeSizeLine(IReadOnlyList<FixCell> ops) => $"変更箇所：{ops.Select(o => o.Staff).Distinct().Count()}人・{ops.Count}セル";

    /// <summary>変更案の探索を止めるボタン（勤務表の作成の「やめる」と区別する）。</summary>
    public const string CancelFixSearchButton = "変更案の検索を中止";
    public const string FixSearchCancelled = "探すのを止めました。";
    public const string FixSearchRetryButton = "もう一度探す";

    /// <summary>その場の直し方探しの状態。スピナーは <see cref="FixPanelState.Running"/> だけ。</summary>
    public static FixPanelState PanelState(bool running, bool fixSearching, string doneKey, string failedKey, string key, string cancelledKey = "") =>
        fixSearching ? FixPanelState.Running
        : running ? FixPanelState.WaitCheck
        : doneKey == key ? FixPanelState.Done
        : failedKey == key ? FixPanelState.Failed
        : cancelledKey == key ? FixPanelState.Cancelled
        : FixPanelState.NotStarted;

    /// <summary>ちら見に出す上位 <paramref name="n"/> シフト: 今の割当・希望を先に、残りは担当できるものを枠の順で（Kotlin <c>peekShifts</c>）。</summary>
    public static IReadOnlyList<int> PeekShifts(IReadOnlyList<int> shown, IReadOnlySet<int> canDo, int current, int? wish, int n = 4)
    {
        var head = new List<int>();
        if (current >= 0) head.Add(current);
        if (wish is >= 0) head.Add(wish.Value);
        return head.Concat(shown.Where(canDo.Contains)).Where(shown.Contains).Distinct().Take(n).ToList();
    }

    /// <summary>ちら見の操作行に並べるシフトの数: 「詳しく ▸」(64dp) と同じ行に 52dp＋間 6dp で入るだけ（1〜4。Kotlin <c>peekPickCount</c>）。</summary>
    public static int PeekPickCount(int contentWidthDp) => Math.Clamp((contentWidthDp - 64 + 6) / 58, 1, 4);

    /// <summary>ちら見の見出し: 巡回の見出しから日付の範囲を落とす（Kotlin <c>peekHeading</c>）。</summary>
    public static string PeekHeading(string tourHeading)
    {
        var at = tourHeading.LastIndexOf(" ・ ", StringComparison.Ordinal);
        return at < 0 ? tourHeading : tourHeading[..at];
    }

    public const string PeekHardRiskNote = "⚠＝変えると必須違反";

    /// <summary>[S6] 既にある組の手順がこのセルを動かすなら、その行き先（新しく試算しない。Kotlin <c>peekRecommendation</c>）。</summary>
    public static int? PeekRecommendation(RelaxTrial.Result? r, int i, int j) =>
        r?.Moves.FirstOrDefault(m => m.Staff == i && m.Day == j)?.To;

    /// <summary>[S6] セルシートから設定の緩和へ渡す状態（Kotlin <c>relaxHandoff</c>）。Offer＝ホームで見つかった組の起点の窓か手順のセル（同じ結果を同じ確定で開く。
    /// セルごとに試算はしない）、Searching＝背景で探している、NoWall＝探し終えて組が無い、Stopped＝途中で止めた／Failed＝失敗した（どちらもやり直しを出す）、
    /// Unavailable＝試算できない盤面（未割当など。理由を出す）。</summary>
    public static RelaxHandoff RelaxHandoffOf(RelaxTrial.Result? r, bool searching, bool noWall, int i, int j, bool stopped = false,
        string? unavailable = null, bool failed = false) =>
        r is not null && ((r.Staff == i && j >= r.WindowFirst && j <= r.WindowLast) || r.Moves.Any(m => m.Staff == i && m.Day == j)) ? RelaxHandoff.Offer
        : r is not null ? RelaxHandoff.None
        : searching ? RelaxHandoff.Searching
        : unavailable is not null ? RelaxHandoff.Unavailable
        : noWall ? RelaxHandoff.NoWall
        : failed ? RelaxHandoff.Failed
        : stopped ? RelaxHandoff.Stopped
        : RelaxHandoff.None;

    public static string RelaxUnavailableText(string reason) => $"設定を緩める試算はできません（{reason}）";

    /// <summary>[S6] 背景の試算の結果を反映してよいか: 最新の試算で、始めたときのデータが今も同じとき（古い失敗で今の状態を上書きしない）。</summary>
    public static bool RelaxOutcomeApplies<C>(long seq, long latestSeq, C ctx, C? ctxNow) => seq == latestSeq && Equals(ctx, ctxNow);

    public static string RelaxHandoffLine(RelaxTrial.Result r, UiState ui, Func<string, string> labelOf) =>
        $"設定を緩めると、この{NextActionGuide.RelaxTargetOf(r, ui, labelOf).What}を解消できる見込みです（上限 {r.Relaxes.Count}件）";

    /// <summary>通知の「元に戻す」は、その操作が今も元に戻すの先頭にあるときだけ効く（後の別の操作を戻さない）。</summary>
    public static bool NoticeUndoApplies(long? topSerial, long noticeSerial) => topSerial is { } t && t == noticeSerial;

    /// <summary>操作の通知を出している間、通常の文言では置き換えない（失敗・拒否だけは置き換える）。</summary>
    public static bool MessageMayReplaceNotice(bool noticeShowing, bool isError) => !noticeShowing || isError;

    // 手動固定の通知「…を手動固定しました（自動では変更しません）」の後ろに付く＝同じことを繰り返さない。
    public const string PinHardHint = "このセルには必須違反があります。";
    public const string PinNotCanDoHint = "担当外のシフトなので、必須違反が残ります。";
    private static readonly string[] PinHardCellFamilies = { "c3n", "c3w", "pref", "groupViol", "extWish" };

    /// <summary>手動固定を付けたセルが担当外／必須違反を抱えているときの一文（付けない＝空）。固定の挙動は変えない＝知らせるだけ（Kotlin <c>pinRegisterHint</c>）。</summary>
    public static string PinRegisterHint(bool canDo, IEnumerable<string> families) =>
        !canDo ? PinNotCanDoHint
        : families.Any(f => PinHardCellFamilies.Contains(VioBuckets.FamilyOfVioClass(f))) ? PinHardHint
        : "";

    public static string CellChangedMessage(string name, string startDate, int day, string symbol) => $"{name}さんの {DayText.Full(startDate, day)} を {symbol} に変更しました";

    public const string AddressCheckingText = "案ごとに、このセルの問題が解消するか確認しています…";

    /// <summary>
    /// 開いた時点の職員 ID（<paramref name="staffId"/>・<paramref name="exceptId"/>、0 以下や null＝ID なし）を今の並びの番号に引き直す（統合仕様 5.1、Kotlin <c>resolveFixFocus</c>）。
    /// 並べ替えのあとは新しい番号、削除されていれば null（別の人を探さない）。ID が無ければ番号のまま。
    /// </summary>
    public static FixFocus? ResolveFixFocus(FixFocus f, int? staffId, int? exceptId, IReadOnlyList<int> staffIds)
    {
        int? At(int? index, int? id) => index is null || id is not { } v || v <= 0 ? index : IndexOf(staffIds, v);
        var s = At(f.Staff, staffId); var x = At(f.ExceptStaff, exceptId);
        if (s == -1 || x == -1) return null;
        return s == f.Staff && x == f.ExceptStaff ? f : f with { Staff = s, ExceptStaff = x };
    }

    private static int IndexOf(IReadOnlyList<int> list, int v)
    {
        for (var i = 0; i < list.Count; i++) if (list[i] == v) return i;
        return -1;
    }

    public const string FixStaffGoneNote = "この職員は一覧から削除されたため、変更案を探せません。画面を閉じて開き直してください。";

    public const string SaveRetryButton = "もう一度保存する";
    public const string SaveFailedNotice = "自動保存できませんでした。PCに書き込めません（空き容量が足りない可能性があります）。「" + SaveRetryButton + "」を押してください";
    public const string HomeSaveFailedLine = "自動保存できていません。PCの空き容量を確かめてください";
    public const string TopBarSaveFailed = "保存できていません";

    /// <summary>上部の巡回の入口（Kotlin <c>topBarTourLabel</c>）。省略されても数が残るよう、数を前に置く。</summary>
    public static string TopBarTourLabel(int n) => $"必須違反{n}か所を見る";
    /// <summary>上部の札の幅の境界（Kotlin <c>TOP_BAR_NARROW_DP</c>）。この移植は窓の幅を変えられるので文言だけ揃える。</summary>
    public const int TopBarNarrowDp = 396;

    /// <summary>上部の札の左に出す保存の語。失敗のときだけ（押すと保存の失敗のダイアログ）。</summary>
    public static string? TopBarSaveWord(SaveState s) => s == SaveState.Failed ? TopBarSaveFailed : null;

    public static bool TopBarStatusWrapped(int widthDp) => widthDp < TopBarNarrowDp;

    public static string SaveFailDialogTitle(SaveFailDialog d) => d == SaveFailDialog.Again ? "まだ保存できていません" : "自動保存できませんでした";
    public const string SaveFailDialogBody = "PCに書き込めません（空き容量が足りない可能性があります）。";

    /// <summary>勤務表 CSV の書き出しに失敗したら、配布前の確認を開き直す。</summary>
    public static bool CsvExportReopensPreDist(bool ok) => !ok;

    public const string ReviewCheckingText = "変更後の影響を確認しています…";

    public const string PickProblemFirstNote = "問題を選ぶと、その問題がこのセルで解消する変更案を探します。";

    /// <summary>1 つのセルに重なった問題の見出し（Kotlin <c>problemChoiceHeading</c>、仕様 §3-2・§5、3.665.0）。2 件以上のときだけ。</summary>
    public static string? ProblemChoiceHeading(int n) => n >= 2 ? $"この勤務に関係する問題が {n} 件あります。どの問題の直し方を確認しますか？" : null;

    /// <summary>選んだ問題（族）を減らす案か。family が null＝選んでいない（すべての案）。</summary>
    public static bool FixAddresses(FixSuggestion s, string? family) => family is null || s.Diff.Any(d => d.Family == family && d.Delta < 0);

    /// <summary>選んだ問題を減らす案が無いとき（ほかの問題の案はある）。</summary>
    public static string NoFixForProblemLine(string label) => $"選んだ問題（{label}）を減らす案は見つかりませんでした。ほかの問題を選ぶと、その直し方を確認できます。";

    /// <summary>回数の問題の目標（職員×シフト、Kotlin <c>countTargetOf</c>）。null＝回数の過不足ではない、または基準が無い。</summary>
    public static (int Target, bool Under)? CountTargetOf(string? vio, (int? Lo, int? Hi, int? Apt) limits) => vio switch
    {
        "vio-low" when limits.Lo is { } l => (l, true),
        "vio-aptLow" when limits.Apt is { } a => (a, true),
        "vio-high" when limits.Hi is { } h => (h, false),
        "vio-aptHigh" when limits.Apt is { } a => (a, false),
        _ => null,
    };

    private static string BasisOfCount(string? vio) => vio switch { "vio-low" => "下限", "vio-high" => "上限", _ => "目標" };

    /// <summary>職員×シフトの回数の問題（集計の職員別セル・職員の勤務状況から）。</summary>
    public static FixTarget? CountFixTarget(IReadOnlyDictionary<string, string> countViolations, int staff, int shift, (int? Lo, int? Hi, int? Apt) limits)
    {
        var vio = countViolations.GetValueOrDefault($"{staff},{shift}");
        return CountTargetOf(vio, limits) is { } c ? new FixTarget(staff, shift, null, c.Target, c.Under, BasisOfCount(vio)) : null;
    }

    /// <summary>シフト×日の人員の問題（日ヘッダ・集計の日別セルから）。<paramref name="need"/> は (必要, 適正)。</summary>
    public static FixTarget? NeedFixTarget(IReadOnlyDictionary<string, IReadOnlyList<string>> needFamilies, int shift, int day, (int Lo, int Hi)? need)
    {
        var classes = needFamilies.GetValueOrDefault($"{shift},{day}") ?? Array.Empty<string>();
        if (need is not { } n) return null;
        if (classes.Contains("vio-covU")) return new FixTarget(null, shift, day, n.Lo, true, "必要人数");
        if (classes.Contains("vio-covO")) return new FixTarget(null, shift, day, n.Hi, false, "適正人数");
        return null;
    }

    /// <summary>対象の名（回数＝記号、人員＝「11/3のAア」）。</summary>
    public static string FixTargetLabel(FixTarget t, string sym, string startDate) =>
        t.Multi ? $"{sym}（{t.Days.Count}日）" : t.Day is { } d ? $"{DayText.Short(startDate, d)}の{sym}" : sym;

    /// <summary>同じ勤務の人員不足の日々をまとめて見る対象（Kotlin <c>multiNeedTarget</c>、2 日以上のときだけ）。<paramref name="need"/> は (勤務, 日) → (必要, 適正)。</summary>
    public static FixTarget? MultiNeedTarget(IReadOnlyDictionary<string, IReadOnlyList<string>> needFamilies, int shift, Func<int, int, (int Lo, int Hi)?>? need)
    {
        if (need is null) return null;
        var days = new List<int>();
        foreach (var (key, fams) in needFamilies)
        {
            var comma = key.IndexOf(',');
            if (comma < 0 || !fams.Contains("vio-covU") || !int.TryParse(key.AsSpan(0, comma), out var k) || k != shift || !int.TryParse(key.AsSpan(comma + 1), out var j)) continue;
            if (need(shift, j) is not null) days.Add(j);
        }
        days.Sort();
        if (days.Count < 2) return null;
        return new FixTarget(null, shift, null, 0, true, "必要人数", days, days.Select(j => need(shift, j)!.Value.Lo).ToList());
    }

    /// <summary>まとめて調整するボタンの名（2 日なら「両日」）。</summary>
    public static string MultiFixButton(int n) => (n == 2 ? "両日" : $"{n}日") + "の不足をまとめて直す案を探す";

    /// <summary>対象の見出し 2 行: 「下限まで、あと8回です」「現在 7回 ／ 下限 15回」。人員は「必要人数より1人少ない状態です」「配置 0人 ／ 必要 1人」。</summary>
    public static IReadOnlyList<string> FixTargetHead(FixTarget t, int count, IReadOnlyList<IReadOnlyList<int>>? schedule = null, string startDate = "")
    {
        if (t.Multi) return new[] { $"{t.Days.Count}日で合計{t.GapOf(schedule ?? Array.Empty<IReadOnlyList<int>>())}人不足しています（日ごとの不足の合計）",
            "対象の日：" + string.Join("・", t.Days.Select(d => DayText.Short(startDate, d))) };
        var gap = Math.Max(0, t.Under ? t.Target - count : count - t.Target);
        if (t.Staff is not null)
            return new[] { t.Under ? $"{t.Basis}まで、あと{gap}回です" : $"{t.Basis}を{gap}回超えています", $"現在 {count}回 ／ {t.Basis} {t.Target}回" };
        var shortBasis = t.Basis.EndsWith("人数", StringComparison.Ordinal) ? t.Basis[..^2] : t.Basis;
        return new[] { t.Under ? $"{t.Basis}より{gap}人少ない状態です" : $"{t.Basis}より{gap}人多い状態です", $"配置 {count}人 ／ {shortBasis} {t.Target}人" };
    }

    /// <summary>適用前の確認の「今回の目的」。</summary>
    public static string FixTargetPurpose(FixTarget t, string? staffName, string label) =>
        t.Multi ? $"{label}の{t.GapWord}をまとめて解消する"
        : t.Staff is not null ? $"{(staffName is null ? "" : $"{staffName}さんの")}{label}{t.GapWord}を減らす" : $"{label}{t.GapWord}を解消する";

    /// <summary>対象の問題を変える案を先に（集計・日の印から探した結果の並べ替え。同順位は元の効果順＝安定ソート）。</summary>
    public static IReadOnlyList<FixSuggestion> RankForTarget(IReadOnlyList<FixSuggestion> list, IReadOnlyList<IReadOnlyList<int>> schedule, FixTarget? t) =>
        t is null ? list : list.OrderBy(s => t.Effect(schedule, s.Ops).Fixes ? 0 : 1).ToList();

    /// <summary>「ほかの問題への影響」の行（対象の族は上に出すので除く）。必須違反の増減を先頭に。族名は <paramref name="labelOf"/>（WinUI の BreakdownLabels）。
    /// カードの先頭に <see cref="HardChangeLine"/> があれば必須は繰り返さない（<paramref name="hardShown"/>）。</summary>
    public static IReadOnlyList<string> OtherEffectLines(FixSuggestion s, IReadOnlySet<string> skipFamilies, Func<string, string> labelOf, bool hardShown = false)
    {
        var hard = "必須違反：" + (AnalysisTriage.UnitDeltaText(s.Diff, MirrorKeys.Hard)
            ?? (s.DeltaHard == 0 ? "変わらない" : s.DeltaHard < 0 ? $"{-s.DeltaHard}件減る" : $"{s.DeltaHard}件増える"));
        return (hardShown ? Array.Empty<string>() : new[] { hard }).Concat(s.Diff.Where(d => d.Delta != 0 && !skipFamilies.Contains(d.Family)
                && !(hardShown && MirrorKeys.Hard.Contains(d.Family)))
            .Select(d => $"{labelOf(d.Family)}：{Math.Abs(d.Delta)}{AnalysisTriage.UnitOf(d.Family)}{(d.Delta < 0 ? "減る" : "増える")}")).ToList();
    }

    /// <summary>対象の族（「ほかの問題への影響」から除く）。</summary>
    public static IReadOnlySet<string> FixTargetFamilies(FixTarget t) =>
        t.Staff is not null ? new HashSet<string> { "low", "high", "apt" } : new HashSet<string> { "covU", "covO" };

    /// <summary>対象の問題を変えない案の見出し（開くまで畳む）。</summary>
    public static string OtherFixHeading(int n) => $"別の問題を改善する案（{n}件）";
    public static string OtherFixNote(string label, FixTarget t) =>
        $"{label}の{(t.Staff is not null ? "回数" : "人数")}は{(t.Under ? "増えません" : "減りません")}";

    /// <summary>対象の問題を変える案の見出し。</summary>
    public static string ThisFixHeading(string label, FixTarget t) => $"{label}を{(t.Under ? "増やす" : "減らす")}案";

    /// <summary>対象を減らす案が無いとき（人員の問題）。</summary>
    public static string NoNeedFixLine(string label, FixTarget t, bool timedOut = false) =>
        // 時間切れは「案が無い」と分けて言う（統合仕様 INV-08＝探索不足と案なしを混同しない）。
        timedOut ? $"{label}の{t.GapWord}を減らす案は、制限時間内には見つかりませんでした。もう一度探すと続きを探せます。"
        : $"{label}の{t.GapWord}を減らす案は、今回の検索では見つかりませんでした。" +
          (t.Under ? "担当できる職員と、その日の希望・固定勤務をご確認ください。" : "その日に入っている職員の希望・固定勤務をご確認ください。");

    /// <summary>回数の過不足の要点（集計セルの詳細）。「▼4」だけでは今の回数か不足数か分からない＝現在・基準・差を言葉で。</summary>
    public static string? CountGapLine(string vio, int count, (int? Lo, int? Hi, int? Apt) limits) => vio switch
    {
        "vio-low" when limits.Lo is { } l => $"現在 {count}回／下限 {l}回 ・ 不足 {Math.Max(0, l - count)}回",
        "vio-high" when limits.Hi is { } h => $"現在 {count}回／上限 {h}回 ・ 超過 {Math.Max(0, count - h)}回",
        "vio-aptLow" when limits.Apt is { } a => $"現在 {count}回／目標 {a}回 ・ 不足 {Math.Max(0, a - count)}回",
        "vio-aptHigh" when limits.Apt is { } a => $"現在 {count}回／目標 {a}回 ・ 超過 {Math.Max(0, count - a)}回",
        _ => null,
    };

    /// <summary>選んだ問題（族）が 1 案でどう変わるか。案の先頭に出す。</summary>
    public static string? FamilyEffectLine(FixSuggestion s, string family, string label)
    {
        foreach (var (fam, d) in s.Diff)
            if (fam == family) return d < 0 ? $"{label}：{-d}件減る" : d > 0 ? $"{label}：{d}件増える" : null;
        return null;
    }

    /// <summary>適用ボタンの下に添える注記（ボタンの文言は短く＝折り返さない）。</summary>
    public const string ApplyUndoNote = "適用したあとでも元に戻せます。";

    /// <summary>案のカードのボタン（見るだけ。適用は確認の画面で）。</summary>
    public const string ReviewChangeButton = "変更内容を確認する";

    /// <summary>確認の画面の適用ボタン（件数を言う）。</summary>
    public static string ApplyCountButton(int n) => n >= 2 ? $"この{n}か所の変更を適用する" : ChainFixPreview.ApplyChangeButton;

    /// <summary>確認の画面を閉じるボタン（案の一覧から来たときは一覧へ戻る）。</summary>
    public const string BackToFixesButton = "別案を見る";

    /// <summary>必須 0 の結果の札。「配布可」とは言い切らず、配布前の確認へ案内する。</summary>
    public static string ReadyBadgeLabel(long soft, IReadOnlyDictionary<string, int>? breakdown = null) =>
        soft > 0 ? $"要調整 {(breakdown is null ? null : AnalysisTriage.UnitTotals(breakdown, MirrorKeys.Soft)) ?? $"{soft}件"}・配布前の確認" : "必須違反 0・配布前の確認";

    /// <summary>集計期間（見出しの 2 行目）。</summary>
    public static string? PeriodLine(string startDate, int days) =>
        days <= 0 || string.IsNullOrWhiteSpace(startDate) ? null : "対象期間：" + DayText.Range(startDate, 0, days - 1);

    /// <summary>盤面に案のセル代入を当てた後の盤面。</summary>
    public static IReadOnlyList<IReadOnlyList<int>> BoardAfter(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops)
    {
        var after = schedule.Select(r => r.ToArray()).ToList();
        foreach (var op in ops)
            if (op.Staff >= 0 && op.Staff < after.Count && op.Day >= 0 && op.Day < after[op.Staff].Length) after[op.Staff][op.Day] = op.ToShift;
        return after.Select(r => (IReadOnlyList<int>)r).ToList();
    }

    private static int? CellAt(IReadOnlyList<IReadOnlyList<int>> b, int i, int j) => i >= 0 && i < b.Count && j >= 0 && j < b[i].Count ? b[i][j] : null;
    private static int Headcount(IReadOnlyList<IReadOnlyList<int>> b, int k, int j) => b.Count(r => j >= 0 && j < r.Count && r[j] == k);

    /// <summary>
    /// 案で人数が変わる枠（日×勤務）の行（Kotlin <c>headcountImpact</c>、2026-10-14 利用者レビュー「どの日の不足を直す案か」と「別の日への影響」を分ける）。
    /// Same＝見ている日のほかの勤務、Other＝別の日。<paramref name="skip"/>＝見ている枠（勤務, 日）は上に出すので除く。
    /// 必要・適正の判定は設定の値で言い、値が無い枠は人数だけ（「安全に変更できます」とは言わない）。
    /// </summary>
    public static (IReadOnlyList<string> Same, IReadOnlyList<string> Other) HeadcountImpact(
        IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops, IReadOnlyList<string> shiftSymbols, string startDate,
        Func<int, int, (int Lo, int Hi)?>? needLimits, (int Shift, int Day)? skip = null)
    {
        var after = BoardAfter(schedule, ops);
        var same = new List<string>(); var other = new List<string>();
        foreach (var j in ops.Select(o => o.Day).Distinct().Order())
        {
            var shifts = ops.Where(o => o.Day == j).SelectMany(o => new[] { CellAt(schedule, o.Staff, j), o.ToShift }).OfType<int>().Distinct().Order();
            foreach (var k in shifts)
            {
                if (skip is { } sk && sk.Shift == k && sk.Day == j) continue;
                var b = Headcount(schedule, k, j); var a = Headcount(after, k, j);
                if (a == b) continue;
                var note = needLimits?.Invoke(k, j) is not { } lim ? ""
                    : a < lim.Lo ? $"（必要 {lim.Lo}人を下回ります）"
                    : a > lim.Hi ? $"（適正 {lim.Hi}人を超えます）"
                    : $"（必要 {lim.Lo}人を満たしたまま）";
                var line = $"{DayText.Short(startDate, j)}の{(k >= 0 && k < shiftSymbols.Count ? shiftSymbols[k] : UnknownLabel)}：{b}人 → {a}人{note}";
                if (skip is { } s2 && j == s2.Day) same.Add(line); else other.Add(line);
            }
        }
        return (same, other);
    }

    /// <summary>案で回数が変わる（または変わらない）勤務を職員ごとに言う（Kotlin <c>staffCountNotes</c>）。2 か所以上を変える理由の事実（例「休の回数は変わりません（12回のまま）」）。</summary>
    public static IReadOnlyList<string> StaffCountNotes(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits = null, int? targetStaff = null)
    {
        if (ops.Count < 2) return Array.Empty<string>();
        var after = BoardAfter(schedule, ops);
        return ops.Select(o => o.Staff).Distinct().SelectMany(i =>
        {
            IReadOnlyList<int> row = i >= 0 && i < schedule.Count ? schedule[i] : Array.Empty<int>();
            IReadOnlyList<int> rowA = i >= 0 && i < after.Count ? after[i] : Array.Empty<int>();
            var name = i >= 0 && i < staffNames.Count ? staffNames[i] : UnknownLabel;
            return ops.Where(o => o.Staff == i).SelectMany(o => new[] { o.Day >= 0 && o.Day < row.Count ? row[o.Day] : (int?)null, o.ToShift })
                .OfType<int>().Distinct().Order().Select(k =>
                {
                    var b = row.Count(v => v == k); var a = rowA.Count(v => v == k);
                    var sym = k >= 0 && k < shiftSymbols.Count ? shiftSymbols[k] : UnknownLabel;
                    if (a == b) return $"{name}さんの「{sym}」の回数は変わりません（{b}回のまま）";
                    var cross = i != targetStaff && limits?.Invoke(i, k) is { } l ? CountCrossNote(b, a, l.Lo, l.Hi) : "";
                    return $"{name}さんの「{sym}」：{b}回 → {a}回" + cross;
                });
        }).ToList();
    }

    /// <summary>回数が基準をまたぐときの根拠（「（下限 8回を下回ります）」「（上限 4回を超えます）」）。またがなければ空。</summary>
    public static string CountCrossNote(int before, int after, int? lo, int? hi) =>
        lo is { } l && after < before && after < l ? $"（下限 {l}回を下回ります）"
        : hi is { } h && after > before && after > h ? $"（上限 {h}回を超えます）"
        : "";

    /// <summary>回数の対象の案で、見ている職員以外に新しく生まれる下限割れ・上限超えの族（対象の族でも、別の職員なら言う）。族名は <paramref name="labelOf"/>。</summary>
    public static IReadOnlyList<string> NewCountBreakLines(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops, int? targetStaff,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, Func<string, string> labelOf)
    {
        if (limits is null) return Array.Empty<string>();
        var after = BoardAfter(schedule, ops);
        return ops.Select(o => o.Staff).Distinct().Where(i => i != targetStaff).SelectMany(i =>
        {
            IReadOnlyList<int> row = i >= 0 && i < schedule.Count ? schedule[i] : Array.Empty<int>();
            IReadOnlyList<int> rowA = i >= 0 && i < after.Count ? after[i] : Array.Empty<int>();
            return ops.Where(o => o.Staff == i).SelectMany(o => new[] { o.Day >= 0 && o.Day < row.Count ? row[o.Day] : (int?)null, o.ToShift })
                .OfType<int>().Distinct().Order().Select(k =>
                {
                    var (lo, hi, _) = limits(i, k);
                    var b = row.Count(v => v == k); var a = rowA.Count(v => v == k);
                    var fam = lo is { } l && a < l && b >= l ? "low" : hi is { } h && a > h && b <= h ? "high" : null;
                    return fam is null ? null
                        : $"新たに生じる：{labelOf(fam)}（{(i >= 0 && i < staffNames.Count ? staffNames[i] : UnknownLabel)}さんの{(k >= 0 && k < shiftSymbols.Count ? shiftSymbols[k] : UnknownLabel)}）";
                }).OfType<string>();
        }).ToList();
    }

    /// <summary>回数の対象の族（下限→low、上限→high、目標→apt）。日の対象は null。</summary>
    public static string? CountTargetFamily(FixTarget t) => t.Staff is null ? null : t.Basis switch { "下限" => "low", "上限" => "high", _ => "apt" };

    /// <summary>回数の対象で、その族の勤務表全体の量が減らない案（見ている職員の不足が別の職員へ移るだけ）。</summary>
    public static bool CountMovesOnly(FixSuggestion s, FixTarget? t)
    {
        if (t is null || CountTargetFamily(t) is not { } fam) return false;
        foreach (var (f, d) in s.Diff) if (f == fam) return d >= 0;
        return true;
    }

    public const string MovesToOtherStaffNote = "＝別の職員へ移るだけです";

    /// <summary>同じ勤務のほかの日で、案のあとも残る人員の過不足（Kotlin <c>remainingSameShift</c>「11/25のDテ不足は残ります」）。<paramref name="board"/> は見る盤面（案の前なら案を当てた後）。</summary>
    public static IReadOnlyList<string> RemainingSameShift(IReadOnlyDictionary<string, IReadOnlyList<string>> needFamilies, IReadOnlyList<IReadOnlyList<int>> board, FixTarget t,
        Func<int, int, (int Lo, int Hi)?>? needLimits, IReadOnlyList<string> shiftSymbols, string startDate)
    {
        if (t.Day is not { } day) return Array.Empty<string>();
        var cls = t.Under ? "vio-covU" : "vio-covO";
        var sym = t.Shift >= 0 && t.Shift < shiftSymbols.Count ? shiftSymbols[t.Shift] : $"{t.Shift}";
        var days = new List<int>();
        foreach (var (key, fams) in needFamilies)
        {
            var comma = key.IndexOf(',');
            if (comma < 0 || !fams.Contains(cls) || !int.TryParse(key.AsSpan(0, comma), out var k) || k != t.Shift || !int.TryParse(key.AsSpan(comma + 1), out var j)) continue;
            if (j != day) days.Add(j);
        }
        var outList = new List<string>();
        foreach (var j in days.Order())
        {
            if (needLimits?.Invoke(t.Shift, j) is not { } lim) continue;
            var n = Headcount(board, t.Shift, j);
            var gap = t.Under ? lim.Lo - n : n - lim.Hi;
            if (gap > 0) outList.Add($"{DayText.Short(startDate, j)}の{sym}は、{(t.Under ? $"{gap}人不足" : $"{gap}人多い状態")}のままです");
        }
        return outList;
    }

    /// <summary>同じ勤務の期間全体の過不足（Kotlin <c>shiftPeriodGap</c>。日ごとに基準までの差を 0 で打ち切って足す。設定の値が無い日は数えない）。値が無ければ null。</summary>
    public static int? ShiftPeriodGap(IReadOnlyList<IReadOnlyList<int>> board, int shift, bool under, Func<int, int, (int Lo, int Hi)?>? needLimits)
    {
        if (needLimits is null) return null;
        var g = 0;
        var days = board.Count > 0 ? board[0].Count : 0;
        for (var j = 0; j < days; j++)
        {
            if (needLimits(shift, j) is not { } lim) continue;
            var n = Headcount(board, shift, j);
            g += under ? Math.Max(0, lim.Lo - n) : Math.Max(0, n - lim.Hi);
        }
        return g;
    }

    /// <summary>案のカードの「期間全体」の行（Kotlin <c>periodGapLine</c>、日の人員の対象）。見ている日が直っても期間全体で減らなければ、移るだけと言う（2026-10-15 利用者「思考誘導が混乱する」）。</summary>
    public static string PeriodGapLine(string sym, bool under, int before, int after) =>
        $"{sym}の{(under ? "不足" : "過剰")}（期間全体）：{before}人 → {after}人" + (after > before ? "＝期間全体では増えます" : after == before ? "＝別の日へ移るだけです" : "");

    /// <summary>案のカードの先頭: 必須違反の変化（族ごと、Kotlin <c>hardChangeLine</c>）。変わらなければ null。</summary>
    public static string? HardChangeLine(IEnumerable<(string Family, int Delta)> diff, Func<string, string> labelOf)
    {
        var hard = new HashSet<string>(MirrorKeys.Hard);
        var txt = AnalysisTriage.FamilyDeltaList(diff.Where(x => hard.Contains(x.Family)), labelOf);
        return txt.Length > 0 ? $"必須違反の変化：{txt}" : null;
    }

    /// <summary>パネルの上に 1 回だけ出す、同じ勤務のほかの日の過不足（Kotlin <c>otherDaysGapLine</c>、カードごとに繰り返さない）。無ければ null。</summary>
    public static string? OtherDaysGapLine(IReadOnlyDictionary<string, IReadOnlyList<string>> needFamilies, IReadOnlyList<IReadOnlyList<int>> board, FixTarget t,
        Func<int, int, (int Lo, int Hi)?>? needLimits, IReadOnlyList<string> shiftSymbols, string startDate)
    {
        if (t.Day is not { } day || needLimits is null) return null;
        var cls = t.Under ? "vio-covU" : "vio-covO";
        var days = new List<int>();
        foreach (var (key, fams) in needFamilies)
        {
            var comma = key.IndexOf(',');
            if (comma < 0 || !fams.Contains(cls) || !int.TryParse(key.AsSpan(0, comma), out var k) || k != t.Shift || !int.TryParse(key.AsSpan(comma + 1), out var j)) continue;
            if (j != day) days.Add(j);
        }
        var parts = new List<string>();
        foreach (var j in days.Order())
        {
            if (needLimits(t.Shift, j) is not { } lim) continue;
            var n = Headcount(board, t.Shift, j);
            var gap = t.Under ? lim.Lo - n : n - lim.Hi;
            if (gap > 0) parts.Add($"{DayText.Short(startDate, j)}（{gap}人）");
        }
        if (parts.Count == 0) return null;
        var sym = t.Shift >= 0 && t.Shift < shiftSymbols.Count ? shiftSymbols[t.Shift] : $"{t.Shift}";
        return $"ほかに{sym}が{(t.Under ? "足りない" : "多い")}日：{string.Join("・", parts)}";
    }

    /// <summary>自動保存の状態の 1 行（適用後の案内）。</summary>
    public static string SaveStateLine(SaveState s) => "保存：" + s switch
    {
        SaveState.Saved => "完了",
        SaveState.Saving => "保存中",
        SaveState.Failed => $"失敗（「{SaveRetryButton}」で保存し直してください）",
        _ => "未保存（まもなく保存します）",
    };

    /// <summary>勤務の人員のシート（集計の「計（期間）」）で、過不足のある日が 2 日以上のときの問い。</summary>
    public static string ShiftDayPickHeading() => "どの日を調整しますか？";

    /// <summary>選ぶ行（日）: 「11/24(火)　配置 0人／必要 1人　1人不足」。</summary>
    public static string ShiftDayPickRow(UiState ui, int k, int j, bool under, (int Lo, int Hi)? need)
    {
        var row = DayPickRow(ui, j, new CoverageMark(k, under), need);
        var at = row.IndexOf('　');
        return DayText.Full(ui.StartDate, j) + "　" + (at >= 0 ? row[(at + 1)..] : row);
    }

    /// <summary>休みの人数の過剰（休の勤務の人員のシート）: 先に設定の意味を確かめてもらう。<paramref name="allZero"/>＝どの日も適正 0 人。</summary>
    public static IReadOnlyList<string> RestOverIntro(bool allZero) => new[]
    {
        allZero ? "各日の適正人数が0人と表示されています。意図した設定か、確認できます。" : "休みの人数が、設定上の適正人数を超えている日があります。",
        "休みの人数を減らす前に、個人の休日数・希望・連続勤務への影響もご確認ください。",
    };

    /// <summary>休みの人数の行: 「11/13(金)　休み 2人／適正 0人」（値が無ければ「未設定」）。</summary>
    public static string RestOverRow(UiState ui, int k, int j, (int Lo, int Hi)? need) =>
        $"{DayText.Full(ui.StartDate, j)}　休み {Headcount(ui.Schedule, k, j)}人／適正 " + (need is { } n ? $"{n.Hi}人" : "未設定");

    /// <summary>日の人員のシートで、過不足のある勤務が 2 つ以上のときの問い。</summary>
    public static string DayPickHeading(IReadOnlyList<CoverageMark> marks) =>
        marks.All(m => m.Under) ? "どの勤務の不足を確認しますか？"
        : !marks.Any(m => m.Under) ? "どの勤務の過剰を確認しますか？"
        : "どの勤務の過不足を確認しますか？";

    /// <summary>選ぶ行: 「Aア　配置 0人／必要 1人　1人不足」。</summary>
    public static string DayPickRow(UiState ui, int j, CoverageMark m, (int Lo, int Hi)? need)
    {
        var sym = m.Shift >= 0 && m.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[m.Shift] : $"{m.Shift}";
        var now = ui.Schedule.Count(r => j >= 0 && j < r.Count && r[j] == m.Shift);
        return m.Under
            ? $"{sym}　配置 {now}人／必要 {(need is { } a ? a.Lo.ToString() : "?")}人" + (need is { } n1 ? $"　{Math.Max(0, n1.Lo - now)}人不足" : "")
            : $"{sym}　配置 {now}人／適正 {(need is { } b ? b.Hi.ToString() : "?")}人" + (need is { } n2 ? $"　{Math.Max(0, now - n2.Hi)}人多い" : "");
    }

    /// <summary>
    /// 見ている問題（集計の職員別・日別セル、日ヘッダの印から。Kotlin <c>panelTarget</c>）。セルのシート（日あり・職員あり）や「他の人で補う」は null。
    /// C# には ConditionsView が無い＝VM の <c>StaffCellLimits</c>/<c>NeedCellLimits</c> を受ける（null＝条件が読めない）。
    /// </summary>
    public static FixTarget? PanelTarget(UiState ui, FixFocus f, Func<int, int, (int? Lo, int? Hi, int? Apt)>? staffLimits, Func<int, int, (int Lo, int Hi)?>? needLimits)
    {
        if (staffLimits is null || needLimits is null || f.Shift is not { } k) return null;
        if (f.AllDays && f.Staff is null) return MultiNeedTarget(ui.NeedFamilies, k, needLimits);
        if (f.Staff is { } i && f.Day is null) return CountFixTarget(ui.CountViolations, i, k, staffLimits(i, k));
        // 日があれば、職員に絞った探索（集計の日別の詳細）でもその日のその勤務の問題のまま（日付を落とさない）。
        if (f.Day is { } d && f.ExceptStaff is null) return NeedFixTarget(ui.NeedFamilies, k, d, needLimits(k, d));
        return null;
    }

    /// <summary>
    /// セルを 1 つ変える前の確認（Kotlin <c>cellChangeReview</c>、2026-10-14 利用者レビュー「問題を確認 → 変更方法を選ぶ → 影響を確認して適用」）。
    /// 前後の報告を正式な検査（<see cref="UnifiedViolationChecker"/>）で比べ、違反の同一性（<see cref="ViolationIdentity"/>）で
    /// このセルの問題が解消するか、ほかで解消する問題、新しく生じる・悪くなる問題を言う。値はすべて評価した結果（仮の値を書かない）。族名は <paramref name="labelOf"/>。
    /// Verdict は変更の行の直後の一文（必須違反が増える＝VerdictBad、またはこのセルの問題が解消する）。言えることが無ければ null。
    /// </summary>
    public static CellChangeReview CellChangeReview(MagiState state, int[][] s, int i, int j, int k, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits, Func<int, int, (int Lo, int Hi)?>? needLimits, bool pinned, Func<string, string> labelOf,
        IReadOnlyList<FixTarget>? targets = null)
    {
        string Sym(int? x) => x is { } v && v >= 0 && v < shiftSymbols.Count ? shiftSymbols[v] : "—";
        string NameOf(int x) => x >= 0 && x < staffNames.Count ? staffNames[x] : $"#{x}";
        var name = i >= 0 && i < staffNames.Count ? staffNames[i] : UnknownLabel;
        var cur = s[i][j];
        var trial = s.Select(r => r.ToArray()).ToArray();
        trial[i][j] = k;
        var before = UnifiedViolationChecker.Check(state, s);
        var after = UnifiedViolationChecker.Check(state, trial);
        var diff = ViolationIdentity.Of(before, after);
        string Label(ViolationKey key) => ViolationKeyLabel(key, staffNames, shiftSymbols, state.StartDate, labelOf);
        // 前後とも残る違反のうち、量が増えるもの（「必須・人員不足（10/8のA4）：不足 1人 → 2人」）。
        var beforeKeys = ViolationIdentity.Keys(before);
        var worsened = ViolationIdentity.Keys(after).Where(beforeKeys.Contains).Select(key =>
            ViolationKeyGap(key, s, limits, needLimits) is { } g0 && ViolationKeyGap(key, trial, limits, needLimits) is { } g1 && g1.Amount > g0.Amount
                ? $"{Label(key)}：{g0.Word} {g0.Amount}{g0.Unit} → {g1.Amount}{g1.Unit}" : null).OfType<string>().ToList();
        var here = ViolationIdentity.Keys(before).Where(key => key.Space == ViolationKey.Kind.Cell && key.A == i && key.B == j).ToList();
        var ops = new[] { new FixCell(i, j, k) };
        var sl = s.Select(r => (IReadOnlyList<int>)r).ToList();
        // 日の人数・回数の問題から開いたときは、その数の前後も言う（このセルに違反が無くても見ている問題は変わる）。
        var thisCell = (targets ?? Array.Empty<FixTarget>()).Select(t => $"{FixTargetReviewName(t, staffNames, shiftSymbols, state.StartDate, labelOf)}：{t.Effect(sl, ops).CountLine()}")
            .Concat(here.Select(key => $"{labelOf(key.Family)}：" + (diff.Resolved.Contains(key) ? "解消します" : "残ります"))).ToList();
        var nb = string.Join(" → ", new[]
        {
            j - 1 >= 0 ? $"{DayText.Short(state.StartDate, j - 1)}：{Sym(s[i][j - 1])}" : null,
            $"{DayText.Short(state.StartDate, j)}：{Sym(k)}",
            j + 1 < s[i].Length ? $"{DayText.Short(state.StartDate, j + 1)}：{Sym(s[i][j + 1])}" : null,
        }.OfType<string>());
        var board = s.Select(r => (IReadOnlyList<int>)r).ToList();
        var (_, other) = HeadcountImpact(board, new[] { new FixCell(i, j, k) }, shiftSymbols, state.StartDate, needLimits);
        var counts = new[] { cur, k }.Where(x => x >= 0).Distinct().Select(x =>
        {
            var b = s[i].Count(v => v == x); var a = trial[i].Count(v => v == x);
            var (lo, hi, apt) = limits?.Invoke(i, x) ?? (null, null, null);
            var basis = lo is { } l1 && hi is { } h1 && l1 == h1 ? $"／下限＝上限 {l1}回"
                : lo is { } l2 && hi is { } h2 ? $"／範囲 {l2}〜{h2}回"
                : lo is { } l3 ? $"／下限 {l3}回"
                : hi is { } h3 ? $"／上限 {h3}回"
                : apt is { } t ? $"／目標 {t}回"
                : "";
            return $"本人の{Sym(x)}回数：{b}回 → {a}回{basis}";
        }).ToList();
        var wishPin = new List<string>();
        if (state.Wishes.TryGetValue($"{i},{j}", out var w))
            wishPin.Add(w == k ? $"希望（{Sym(w)}）どおりになります" : w == cur ? $"希望（{Sym(w)}）と異なる勤務になります" : $"希望（{Sym(w)}）とは、変更の前後とも異なります");
        if (pinned) wishPin.Add("このセルは手動固定されています（自動作成・調整ではこの勤務を変えません）");
        var hereResolved = here.Where(diff.Resolved.Contains).Select(key => labelOf(key.Family)).Distinct().ToList();
        var (verdict, bad) = after.Hard > before.Hard
            ? ($"この変更で必須違反が増えます（{AnalysisTriage.HardText(before.Breakdown, before.Hard)} → {AnalysisTriage.HardText(after.Breakdown, after.Hard)}）", true)
            : hereResolved.Count > 0 ? ($"このセルの{string.Join("・", hereResolved)}は解消します", false)
            : ((string?)null, false);
        return new CellChangeReview(
            Change: $"{name}さん　{DayText.Full(state.StartDate, j)} {Sym(cur)} → {Sym(k)}",
            Neighbors: nb,
            ThisCell: thisCell.Count > 0 ? thisCell : new List<string> { NoCellViolation },
            OthersResolved: diff.Resolved.Where(key => !here.Contains(key)).Select(Label).ToList(),
            Appeared: diff.Appeared.Select(Label).Concat(worsened).ToList(),
            HardLine: $"必須違反（勤務表全体）：{AnalysisTriage.HardText(before.Breakdown, before.Hard)} → {AnalysisTriage.HardText(after.Breakdown, after.Hard)}",
            Headcount: other,
            Counts: counts,
            WishPin: wishPin,
            Verdict: verdict, VerdictBad: bad);
    }

    public const string KeepUnchangedButton = "今は変更しない";

    public const string RemoveWishButton = "希望を外す（元に戻せます）";

    public const string ReviewAppearedHeading = "新しく生じる・悪くなる問題";

    /// <summary>違反 1 件の量（Kotlin <c>violationKeyGap</c>＝人員は基準までの人数、回数は基準までの回数）。言葉・単位・量。量を言えない族は null。</summary>
    public static (string Word, string Unit, int Amount)? ViolationKeyGap(ViolationKey key, int[][] board,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits, Func<int, int, (int Lo, int Hi)?>? needLimits)
    {
        switch (key.Space)
        {
            case ViolationKey.Kind.Need:
                if (needLimits?.Invoke(key.A, key.B) is not { } nl) return null;
                var n = board.Count(r => key.B >= 0 && key.B < r.Length && r[key.B] == key.A);
                return key.Family switch { "covU" => ("不足", "人", Math.Max(0, nl.Lo - n)), "covO" => ("過剰", "人", Math.Max(0, n - nl.Hi)), _ => null };
            case ViolationKey.Kind.Count:
                if (limits?.Invoke(key.A, key.B) is not { } sl || key.A < 0 || key.A >= board.Length) return null;
                var c = board[key.A].Count(v => v == key.B);
                return key.Family switch
                {
                    "low" => sl.Lo is { } lo ? ("不足", "回", Math.Max(0, lo - c)) : null,
                    "high" => sl.Hi is { } hi ? ("超過", "回", Math.Max(0, c - hi)) : null,
                    "apt" => sl.Apt is { } apt ? ("目標との差", "回", Math.Abs(c - apt)) : null,
                    _ => null,
                };
            default:
                return null;
        }
    }

    public const string NoCellViolation = "このセル自体の違反はありません";

    /// <summary>確認の画面で見ている問題の名（「人員過剰（12/16のDﾃ）」「上限（福澤さんのB4）」）。</summary>
    public static string FixTargetReviewName(FixTarget t, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, string startDate, Func<string, string> labelOf)
    {
        var sym = t.Shift >= 0 && t.Shift < shiftSymbols.Count ? shiftSymbols[t.Shift] : $"{t.Shift}";
        return t.Staff is { } st ? $"{t.Basis}（{(st >= 0 && st < staffNames.Count ? staffNames[st] : $"#{st}")}さんの{sym}）"
            : $"{labelOf(t.Under ? "covU" : "covO")}（{FixTargetLabel(t, sym, startDate)}）";
    }

    /// <summary>違反 1 件の名と場所（Kotlin <c>violationKeyLabel</c>「必須・禁止の並び（福澤さん 11/29）」「人員不足（11/3のAア）」「下限割れ（福澤さんのB4）」）。</summary>
    public static string ViolationKeyLabel(ViolationKey key, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, string startDate, Func<string, string> labelOf)
        => (key.Hard ? "必須・" : "") + labelOf(key.Family) + $"（{ViolationKeyPlace(key, staffNames, shiftSymbols, startDate)}）";

    /// <summary>違反 1 件の場所（「福澤さん 11/29」「11/3のAア」「福澤さんのB4」）。</summary>
    public static string ViolationKeyPlace(ViolationKey key, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, string startDate)
    {
        string Sym(int x) => x >= 0 && x < shiftSymbols.Count ? shiftSymbols[x] : $"{x}";
        string NameOf(int x) => x >= 0 && x < staffNames.Count ? staffNames[x] : $"#{x}";
        return key.Space switch
        {
            ViolationKey.Kind.Cell => $"{NameOf(key.A)}さん {DayText.Short(startDate, key.B)}",
            ViolationKey.Kind.Need => $"{DayText.Short(startDate, key.B)}の{Sym(key.A)}",
            _ => $"{NameOf(key.A)}さんの{Sym(key.B)}",
        };
    }

    /// <summary>案で解消する必須違反を「族：場所」で（正式な検査の前後の違反の同一性で決める）。</summary>
    public static IReadOnlyList<string> ResolvedHardLabels(MagiState state, int[][] s, IReadOnlyList<FixCell> ops, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols,
        Func<string, string> labelOf) =>
        ViolationIdentity.Of(UnifiedViolationChecker.Check(state, s), UnifiedViolationChecker.Check(state, Trial(s, ops)))
            .Resolved.Where(k => k.Hard).Select(k => $"{labelOf(k.Family)}：{ViolationKeyPlace(k, staffNames, shiftSymbols, state.StartDate)}").ToList();

    /// <summary>手 ops で解消する必須違反の鍵（before＝今の盤面の正式な報告。案ごとに検査し直さない）。</summary>
    public static IReadOnlySet<ViolationKey> ResolvedHardKeys(MagiState state, int[][] s, ViolationReport before, IReadOnlyList<FixCell> ops) =>
        ViolationIdentity.Of(before, UnifiedViolationChecker.Check(state, Trial(s, ops))).Resolved.Where(k => k.Hard).ToHashSet();

    /// <summary>案ごとの解消の鍵を合わせる（同じ場所を直す案が重なっても 1 か所）。</summary>
    public static IReadOnlySet<ViolationKey> FixResolvedUnionOf(IReadOnlyList<IReadOnlyList<FixCell>> opsList, Func<IReadOnlyList<FixCell>, IReadOnlySet<ViolationKey>> resolve) =>
        opsList.SelectMany(resolve).ToHashSet();

    /// <summary>分析タブの「変更案で直せる必須違反 Nか所」の N。未探索・盤面か案が変わった後は null（出さない）。</summary>
    public static int? AnalysisFixResolved(UiState ui)
    {
        if (!ui.FixSearched) return null;
        var opsList = ui.FixSuggestions.Where(f => f.DeltaHard < 0).Select(f => f.Ops).ToList();
        if (opsList.Count == 0) return 0;
        return ui.FixResolvedUnion is { } u && u.Matches(opsList, ui.Schedule) ? u.Keys.Count : null;
    }

    /// <summary>ホームの「直す手」の見出し。解消する必須違反の名と場所が分かれば 2 件まで名指しする（3 件目からは件数）。</summary>
    public static string HomeFixHeadline(string hard, IReadOnlyList<string> resolved)
    {
        if (resolved.Count == 0) return $"必須違反 {hard} を減らす手があります";
        var names = string.Join("、", resolved.Take(2)) + (resolved.Count > 2 ? " ほか" : "");
        return $"必須違反 {hard} のうち {resolved.Count}件（{names}）を減らす手があります";
    }

    private static int[][] Trial(int[][] s, IReadOnlyList<FixCell> ops)
    {
        var trial = s.Select(r => r.ToArray()).ToArray();
        foreach (var op in ops)
            if (op.Staff >= 0 && op.Staff < trial.Length && op.Day >= 0 && op.Day < trial[op.Staff].Length) trial[op.Staff][op.Day] = op.ToShift;
        return trial;
    }

    /// <summary>案で別の場所に新しく生まれる必須違反の鍵（正式な検査の前後。<see cref="NewHardLabels"/> の元）。VM はこの鍵を確認の画面へ渡し、画面が族名を付ける。</summary>
    public static IReadOnlyList<ViolationKey> NewHardKeys(MagiState state, int[][] s, IReadOnlyList<FixCell> ops) =>
        ViolationIdentity.NewHard(UnifiedViolationChecker.Check(state, s), UnifiedViolationChecker.Check(state, Trial(s, ops))).ToList();

    /// <summary>案で別の場所に新しく生まれる必須違反（Kotlin <c>newHardLabels</c>、確認の画面の「新たな必須違反」）。</summary>
    public static IReadOnlyList<string> NewHardLabels(MagiState state, int[][] s, IReadOnlyList<FixCell> ops, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, Func<string, string> labelOf) =>
        NewHardKeys(state, s, ops).Select(k => ViolationKeyLabel(k, staffNames, shiftSymbols, state.StartDate, labelOf)).ToList();

    /// <summary>確認の画面の「希望への影響」と「必要な相談」（行の希望の注記から。希望と異なる勤務になる人がいれば相談の対象）。</summary>
    public static (string Wish, string Consult) PreviewWishLines(IReadOnlyList<ChangeRow> rows)
    {
        var off = rows.Where(r => r.Wish.StartsWith("希望（", StringComparison.Ordinal)).ToList();
        var on = rows.Count(r => r.Wish == "希望どおり");
        var wish = off.Count > 0 ? "希望と異なる勤務になる：" + string.Join("、", off.Select(r => $"{r.Staff}さん {r.Date}"))
            : on > 0 ? $"希望どおりになる勤務が {on}件あります。希望と異なる勤務にはなりません"
            : "変更する勤務に希望はありません";
        var consult = off.Count == 0 ? "必要な相談：なし" : "必要な相談：" + string.Join("、", off.Select(r => r.Staff).Distinct().Select(x => $"{x}さん")) + "（希望と異なる勤務になる日）";
        return (wish, consult);
    }

    /// <summary>
    /// 案がこのセルの問題を解消するか（Kotlin <c>resolvesAtCell</c>、2026-10-14 利用者レビュー: 族の全体の件数が減るだけでは、選んだセルが直るとは限らない）。
    /// この場所の鍵＝このセル・その日のその勤務の人数・本人のその勤務の回数（<paramref name="family"/> を選べばその族だけ）が、案の後の正式な報告から消えるか。
    /// </summary>
    public static bool ResolvesAtCell(MagiState state, int[][] s, ViolationReport before, IReadOnlyList<FixCell> ops, int i, int j, string? family)
    {
        if (i < 0 || i >= s.Length || j < 0 || j >= s[i].Length) return false;
        var cur = s[i][j];
        bool Here(ViolationKey k) => (family is null || k.Family == family) && k.Space switch
        {
            ViolationKey.Kind.Cell => k.A == i && k.B == j,
            ViolationKey.Kind.Need => k.A == cur && k.B == j,
            _ => k.A == i && k.B == cur,
        };
        var b = ViolationIdentity.Keys(before).Where(Here).ToList();
        if (b.Count == 0) return false;
        var after = ViolationIdentity.Keys(UnifiedViolationChecker.Check(state, Trial(s, ops)));
        return b.Any(k => !after.Contains(k));
    }

    /// <summary>セルのシートの問題の一文（族ごと）。前日・翌日との並びは並びの族の説明。</summary>
    public static string CellProblemSentence(string fam, Func<string, string> labelOf) => fam switch
    {
        "c3n" => "前後の勤務との組み合わせが禁止条件に該当しています",
        "c3mn" => "前後の勤務との組み合わせが、避けたい並びに該当しています",
        "c3w" => "希望の前日に禁止されている勤務です",
        "pref" => "本人の希望と違う勤務です",
        "extWish" => "拡張希望で除いた勤務です",
        "groupViol" => "この職員のグループが担当しない勤務です",
        "covU" => "この日のこの勤務の人数が必要人数に足りません",
        "covO" => "この日のこの勤務の人数が適正人数を超えています",
        _ => $"{labelOf(fam)}に該当しています",
    };

    /// <summary>並びの違反の日々を「11/28(土) Dテ → 11/29(日) B4」で。<paramref name="days"/> はこのセルと相手の日。</summary>
    public static string? SequenceLine(string startDate, IReadOnlyList<int> row, IReadOnlyList<int> days, IReadOnlyList<string> shiftSymbols) =>
        days.Count < 2 ? null : string.Join(" → ", days.Order().Select(d =>
            $"{DayText.Full(startDate, d)} {(d >= 0 && d < row.Count && row[d] >= 0 && row[d] < shiftSymbols.Count ? shiftSymbols[row[d]] : "—")}"));

    /// <summary>今月の勤務回数（セルのシート）: 今の勤務と、回数に過不足のある勤務。「Cオ：現在4回／目標6回」。</summary>
    public static IReadOnlyList<string> CellCountLines(UiState ui, int i, int current, Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits)
    {
        IReadOnlyList<int> row = i >= 0 && i < ui.Schedule.Count ? ui.Schedule[i] : Array.Empty<int>();
        var fromViolations = ui.CountViolations.Keys.Select(key =>
        {
            var comma = key.IndexOf(',');
            return comma > 0 && int.TryParse(key.AsSpan(0, comma), out var a) && a == i && int.TryParse(key.AsSpan(comma + 1), out var b) ? b : (int?)null;
        }).OfType<int>();
        var ks = new[] { current }.Concat(fromViolations).Where(x => x >= 0).Distinct();
        return ks.Select(k =>
        {
            var (lo, hi, apt) = limits?.Invoke(i, k) ?? (null, null, null);
            var n = row.Count(v => v == k);
            var basis = ui.CountViolations.GetValueOrDefault($"{i},{k}") switch
            {
                "vio-low" => lo is { } l ? $"／下限{l}回" : null,
                "vio-high" => hi is { } h ? $"／上限{h}回" : null,
                _ => apt is { } t ? $"／目標{t}回" : lo is { } l2 ? $"／下限{l2}回" : hi is { } h2 ? $"／上限{h2}回" : null,
            } ?? "";
            return $"{(k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : UnknownLabel)}：現在{n}回{basis}";
        }).ToList();
    }

    /// <summary>手動固定の説明（通常の修正操作と分ける）。</summary>
    public const string PinTitle = "この勤務を自動変更しない";
    public const string PinNote = "固定すると、自動作成・調整ではこの勤務を変更しません。現在の違反が解消されるわけではありません。";

    /// <summary>担当外の勤務の理由（自分で勤務を選ぶ画面）。</summary>
    public const string NotCanDoNote = "「外」はこの職員のグループが担当しない勤務です（担当可否の設定）。選ぶと担当外シフトの必須違反になります。";

    /// <summary>上部の札の 2 行目以降: 自動作成の状態と、今の勤務表の評価（2026-10-14 利用者レビュー「未計算」を分ける）。</summary>
    public static (string First, string Second) BoardStatusLines(bool engineRan, bool hasResult, bool checkedBoard, long hard, long soft,
        IReadOnlyDictionary<string, int>? breakdown = null, bool ranBeforeEdit = false)
    {
        var b = breakdown ?? new Dictionary<string, int>();
        return (engineRan ? "自動作成済み" : ranBeforeEdit && hasResult ? "手で変更あり" : hasResult ? "手で編集" : "自動作成前",
            !checkedBoard ? "評価：未評価" : hard > 0 ? $"評価：必須違反 {AnalysisTriage.HardText(b, hard)}"
            : soft > 0 ? $"評価：要調整 {AnalysisTriage.UnitTotals(b, MirrorKeys.Soft) ?? $"{soft}件"}" : "評価：違反なし");
    }

    // ===== 自動と担当者の分担（2026-10-14 利用者レビュー「どこまで自動で進み、どこで担当者が判断するか」） =====

    /// <summary>組む時間の表記（3.706.0 S2: 作成前の要点と設定タブで共有）。60 の倍数は「N分」、それ以外は「N秒」。</summary>
    public static string BudgetText(int sec) => sec % 60 == 0 ? $"{sec / 60}分" : $"{sec}秒";

    /// <summary>設定タブの組む時間の状態行（3.706.0 S2）。</summary>
    public static string BudgetStatusLine(int sec, int reserveSec) => $"組む時間 {BudgetText(sec)}（＋仕上げと玉突き {reserveSec}秒）";

    /// <summary>「組む時間を選ぶ」の候補（3.706.0 S1）。10秒・1〜5分に、範囲内へ丸めた今の値を必ず含めて昇順。既定・範囲は変えない（HF77）。</summary>
    public static IReadOnlyList<int> BudgetPresets(int currentSec) =>
        new[] { 10, 60, 120, 180, 240, V6FinalPort.MaxOptimizeSec, Math.Clamp(currentSec, 10, V6FinalPort.MaxOptimizeSec) }.Distinct().OrderBy(x => x).ToList();

    /// <summary>勤務表を作成する画面の要点（作成前）を 1 行に。時間は設定値（探索）と固定の仕上げ枠。</summary>
    public static string PreRunPlanLine(string startDate, int days, int staff, int budgetSec, int reserveSec)
    {
        var period = days <= 0 || string.IsNullOrWhiteSpace(startDate) ? "" : DayText.Range(startDate, 0, days - 1) + "・";
        return $"{period}{staff}人・組む時間 {BudgetText(budgetSec)}（＋仕上げ {reserveSec}秒）。開始後は一手ごとの確認はしません";
    }

    /// <summary>登録条件の 1 行（3.704.0 R-4: 節ごとの件数を並べる）。<paramref name="overCaps"/>＝希望が個人の上限を超える組（作成しても残る）。</summary>
    public static string PreRunConditionLine(int floor, int rerun, int overCaps, int pins)
    {
        var parts = new List<string>();
        if (floor > 0) parts.Add($"何度つくっても残る {floor}件");
        if (rerun > 0) parts.Add($"再作成で外れる {rerun}件");
        if (overCaps > 0) parts.Add($"希望が上限を超える {overCaps}件");
        return "登録条件：" + (parts.Count == 0 ? "つくる前に直す項目はありません" : string.Join("・", parts)) +
            (pins > 0 ? $"／手動固定 {pins}件は保持" : "");
    }

    /// <summary>単純な確認の点（3.704.0 G-4）: 空行を除いて <see cref="ConfirmMaxPoints"/> 個まで。超えたら確認ではなく読む画面にする。</summary>
    public static IReadOnlyList<string> ConfirmDialogPoints(IEnumerable<string> points)
    {
        var p = points.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (p.Count > ConfirmMaxPoints) throw new ArgumentException($"確認の点は {ConfirmMaxPoints} つまで（{p.Count}）");
        return p;
    }

    public const int ConfirmMaxPoints = 3;

    /// <summary>作成前のシートの主ボタン＝開いたボタンと同じ語（3.704.0、利用者決定 Q3）。</summary>
    public static string PreRunProceedLabel(bool hasResult) => hasResult ? "再作成" : "勤務表をつくる";

    /// <summary>同じ探索の設定で再作成するときの一言。<paramref name="top"/>＝前回いちばん多い必須違反（「族名（N単位）」）。</summary>
    public static string PreRunRepeatHintText(string? top) =>
        "前回と同じ組む時間・方式で再作成します。" + (top is null ? "" : $"前回いちばん多い必須違反は{top}です。") +
        "同じ設定では同じ違反が残りやすいため、編集タブでこれを 1 つ緩めると減る見込みがあります。";

    /// <summary>
    /// 「勤務表を作成する」を一度見たら出さない基準＝設定と希望と、再作成で外れる手置きのセル（<paramref name="clears"/>）。
    /// 盤面そのものは含めない（再作成のたびに変わる）。上限 0 のセルを手で置き直したときだけシートをもう一度出す。
    /// </summary>
    public static long PreRunAckFingerprint(MagiState st, IReadOnlyList<PreRunCheck.HandPlacedCell> clears)
    {
        var h = StateFingerprint.Of(st);
        unchecked { foreach (var c in clears) h = ((h * 31L + c.Staff) * 31L + c.Day) * 31L + c.Shift; }
        return h;
    }

    public const string PreRunTitle = "勤務表を作成する";
    public const string PreRunCheckConditions = "条件を確認する";

    /// <summary>要調整の内訳（族ごとに件数か pt。重み×件数の多い順に <paramref name="max"/> 種まで）。族名は <paramref name="labelOf"/>。</summary>
    public static string SoftSummary(IReadOnlyDictionary<string, int> breakdown, Func<string, string> labelOf, int max = 4)
    {
        var soft = MirrorKeys.Soft.Where(k => breakdown.TryGetValue(k, out var n) && n > 0).Select(k => (K: k, N: breakdown[k]))
            .OrderByDescending(x => MirrorKeys.WeightOf(x.K) * x.N).ToList();
        if (soft.Count == 0) return "なし";
        return string.Join("・", soft.Take(max).Select(x => $"{labelOf(x.K)} {x.N}{AnalysisTriage.UnitOf(x.K)}")) + (soft.Count > max ? $" ほか{soft.Count - max}種" : "");
    }

    public const string CreationDoneTitle = "作成が終わりました";

    /// <summary>見ている問題の数が厳密に減るか（統合仕様 INV-03＝適用は対象の改善と正式な門の両方）。対象を持たない案は判定しない。</summary>
    public static bool ImprovesTarget(FixTarget? t, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops) =>
        t is null || t.Effect(schedule, ops).Fixes;

    /// <summary>勤務変更案の一覧に出してよいか（INV-01/INV-03）: 適用の門と同じ <see cref="ImprovesTarget"/> を通り、日から開いた一覧（day）ならその日のセルに触れる案だけ。</summary>
    public static bool ListableFix(FixTarget? t, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops, int? day) =>
        (day is null || ops.Any(o => o.Day == day)) && ImprovesTarget(t, schedule, ops);

    /// <summary>一覧を日で絞る依頼の日（日・列から開いた依頼。職員を見る依頼とまとめて見る依頼は絞らない）。</summary>
    public static int? FixListDay(FixFocus f) => f.Staff is null && !f.AllDays ? f.Day : null;

    /// <summary>過剰の対象で、その勤務の 1 人を別の勤務へ移す案の題。それ以外は null（CountEffect.Title を使う）。</summary>
    public static string? SurplusMoveTitle(FixTarget? t, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols)
    {
        if (t is null || t.Under || t.Staff is not null) return null;
        if (ops.Count != 1) return null;
        var op = ops[0];
        if (op.Staff < 0 || op.Staff >= schedule.Count || op.Day < 0 || op.Day >= schedule[op.Staff].Count) return null;
        var from = schedule[op.Staff][op.Day];
        if (from != t.Shift || op.ToShift == from) return null;
        if (op.Staff >= staffNames.Count) return null;
        string Sym(int k) => k >= 0 && k < shiftSymbols.Count ? shiftSymbols[k] : UnknownLabel;
        return $"{staffNames[op.Staff]}さんを {Sym(from)} から {Sym(op.ToShift)} へ移して過剰を減らす";
    }

    public const string PeekSearchButton = "変更案を探す";

    /// <summary>ちら見の主ボタン（S-3）: 問題のあるセルの割当では常に「変更案を探す」。緩める候補は外枠に下げる（主は 1 つ＝INV-09）。null＝出さない。</summary>
    public static string? PeekSearchAction(int mode, CellSeverity severity) =>
        mode == 0 && severity != CellSeverity.None ? PeekSearchButton : null;

    /// <summary>勤務表タブの状態の 1 行の上限（D4 の幅で 1 行に収まる字数）。超えたら末尾を「…」にする。</summary>
    public const int ScheduleStateMax = 23;

    /// <summary>
    /// 状態の 1 行（G2-1）。未作成・組んでいます・必須あり（巡回の先頭を添える）・必須は人員不足だけ・必須 0 の 5 通り。
    /// <paramref name="next"/> は巡回の先頭の「M/D 名」（無ければ人員不足だけ）。必須の数は単位ごと（INV-07）の <paramref name="hardText"/> をそのまま使う。
    /// </summary>
    public static ScheduleStateLine ScheduleStateLineOf(bool hasResult, bool running, long bestHard, string hardText, string softText, string? next)
    {
        var (text, hard, tour) =
            running ? ("組んでいます", false, false)
            : !hasResult ? ("まだ作っていません", false, false)
            : bestHard > 0L && next != null ? ($"必須違反 {hardText}（次：{next}さん）", true, true)
            : bestHard > 0L ? ($"必須違反 {hardText}（日の印から開けます）", true, false)
            : ($"必須違反はありません · 要調整 {softText}", false, false);
        return new ScheduleStateLine(CapStateLine(text), hard, tour);
    }

    public static string CapStateLine(string text, int max = ScheduleStateMax) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>必須違反の一覧の見出し（単位ごとの件数を添える）と、先頭の主ボタンの語（G2-3）。</summary>
    public static string HardListTitle(string hardText) => $"必須違反の一覧（{hardText}）";
    public const string HardListStartButton = "上から順に見る";

    /// <summary>セルの確認シートの題（S-1）。</summary>
    public const string CellSheetTitle = "このマスの確認";

    /// <summary>対象の行「{名}さん · {M/D(曜)} · {記号}」（S-2）。今の勤務はここで言う（空のセルは記号を省く）。</summary>
    public static string CellSheetTarget(string name, string dayLabel, string? symbol) =>
        $"{name}さん · {dayLabel}" + (string.IsNullOrWhiteSpace(symbol) ? "" : $" · {symbol}");

    /// <summary>ちら見の 2 行目の札。必須は巡回中なら「必須 2/5」。問題が無ければ null（札を出さない）。</summary>
    public static string? CellSheetBadge(CellSeverity severity, int? tourAt = null, int tourTotal = 0) => severity switch
    {
        CellSeverity.Hard => tourAt is { } at && tourTotal > 0 ? $"必須 {at + 1}/{tourTotal}" : "必須",
        CellSeverity.Soft => "要調整",
        _ => null,
    };

    /// <summary>足りない側の人探しのボタンを塗りにするか（S-4）。セルに人員不足以外の問題もあれば外枠（主は「変更案を探す」＝INV-09）。</summary>
    public static bool ShortSideFilled(IEnumerable<string> families) => families.All(f => f == "covU");

    public const string EditScopeMonthlyNote = "翌月だけの条件：希望・必要人数・例外（毎月ここから）";
    public const string EditScopeStaffNote = "入退職・所属・資格スキル（随時変更）。職員の一覧はここだけ";
    public const string EditScopeMasterNote = "毎月は変えない基本の設定：シフト・ルール・人数（制度変更時のみ）";
    public const string EditNeedButton = "必要人数を編集する";
    public const string WishConflictsButton = "ぶつかっている希望を見る";

    /// <summary>
    /// [3.707.0 E-2] 編集タブ（scope 0=月次条件／1=職員管理／2=年間マスター）の見出し（Kotlin <c>editHeaderPlan</c>）。点は 3 つまで、1 点に単位は 1 つ（INV-07）。
    /// 主ボタン（Q10）: 実行中は無し、入力パネルを開いている間は Panel、結果のあとに条件を変えたなら Rerun、
    /// 職員かシフトが無ければ OpenMaster（シフトはあって職員だけ無ければ AddStaff）、それ以外は区分ごとの主（月次＝EditNeed）。
    /// </summary>
    public static EditHeaderPlan EditHeaderPlanOf(int scope, EditHeaderCounts counts, bool panelOpen, bool ranBeforeEdit, bool running)
    {
        var title = scope switch
        {
            0 => string.IsNullOrWhiteSpace(counts.Month) ? "月次条件" : $"月次条件（{counts.Month}）",
            1 => "職員管理",
            _ => "年間マスター",
        };
        var subtitle = scope switch { 0 => EditScopeMonthlyNote, 1 => EditScopeStaffNote, _ => EditScopeMasterNote };
        IReadOnlyList<string> points = scope switch
        {
            0 => new[]
            {
                $"希望・休暇 {counts.Wish.Text()}",
                "必要人数 " + (counts.NeedStdOk ? "標準あり" : "標準が未設定") + $"・例外 {counts.NeedExceptions}件",
                "入力診断 " + (counts.Issues == 0 ? "問題なし" : $"見直し {counts.Issues}件"),
            },
            1 => new[] { $"職員 {counts.Staff}人" },
            _ => new[] { $"シフト {counts.Shifts}種類" },
        };
        EditHeaderPrimary? primary =
            running ? null
            : panelOpen ? EditHeaderPrimary.Panel
            : ranBeforeEdit ? EditHeaderPrimary.Rerun
            : counts.Shifts == 0 ? EditHeaderPrimary.OpenMaster
            : counts.Staff == 0 ? EditHeaderPrimary.AddStaff
            : scope == 0 ? EditHeaderPrimary.EditNeed
            : scope == 1 ? EditHeaderPrimary.AddStaff
            : EditHeaderPrimary.OpenMaster;
        EditHeaderSecondary? secondary = scope == 0 && counts.WishConflicts ? EditHeaderSecondary.WishConflicts : null;
        return new EditHeaderPlan(title, subtitle, points, primary, secondary);
    }

    /// <summary>[3.707.0 3a-3] 下部バーの作成ボタンを外枠にするか（編集タブでは見出しの主が Rerun のときだけ塗り）。</summary>
    public static bool EditBarSecondary(EditHeaderPlan plan) => plan.Primary is { } p && p != EditHeaderPrimary.Rerun;

    /// <summary>[3.707.0 3b-1] 希望カードの職員の行。</summary>
    public static string WishStaffSummary(string name, int wishes, int extWishes) => $"{name}さん：希望 {wishes}件・拡張希望 {extWishes}件";

    /// <summary>[3.707.0 3b-2] その職員のぶつかっている希望への文字ボタン（件数は出さない）。</summary>
    public static string WishConflictLinkText(string dayLabel) => $"{dayLabel}のぶつかる希望を見る";

    /// <summary>[3.707.0 3b-4] 希望の入力パネルのボタン。</summary>
    public static string WishApplyButton(int days, string symbol) => $"{days}日を{symbol}の希望にする";
    public static string ExtWishApplyButton(int days) => $"{days}日を選んだシフト以外にする";
    public const string ClearSelectionButton = "選択を外す";
    public static string NeedApplyButton(int days) => $"{days}日に適用する";

    /// <summary>[3.707.0 3b-5] 担当外のシフトを選んだときの注記。</summary>
    public static string WishOutOfScopeNote(string symbol, string name) =>
        $"⚠「{symbol}」は{name}さんの担当外です。希望は登録できますが、配置すると必須違反になります。";

    /// <summary>[3.707.0 3b-5] 拡張希望の登録に失敗したときの文（できなかったこと・原因・次の行動）。</summary>
    public static string ExtWishFailedText(string reason) =>
        $"拡張希望を登録できませんでした。{reason.TrimEnd('。')}。日かシフトを選び直して、もう一度登録してください。";

    /// <summary>[3.712.0] ぶつかっている希望の行の 1 行目: 「福澤俊陽 · 10/1」（理由は次の行。題の下の件数の要約は 3.712.0 で撤去）。</summary>
    public static string WishRowHead(string name, string day) => $"{name} · {day}";

    /// <summary>[3.707.0 3a-6] 固定の帯の行（2 行まで）。理由があれば先に言い、2 行目は戻り先だけ（Kotlin <c>editReturnLines</c>）。</summary>
    public static IReadOnlyList<string> EditReturnLines(EditReturn r) =>
        r.Problem is { } p ? new[] { p, $"直したら「{r.Label}」へ戻れます。" } : new[] { EditReturn.Line(r) };

    /// <summary>[3.707.0 3a-6] 着地先が必要人数の日なら、その日の人員不足の 1 文（3.698.0 の ShortageHeadline）。無ければ null。</summary>
    public static string? EditReturnProblem(IEnumerable<CoverageShortfall> shortfalls, int? shift, int? day)
    {
        if (shift is null || day is null) return null;
        return shortfalls.FirstOrDefault(s => s.ShiftIndex == shift && s.DayIndex == day) is { } sf ? ShortageHeadlineOf(sf).Headline : null;
    }

    public const string NotImprovingTargetMessage = "見ている問題が減らないため、この提案は適用しませんでした。「変更案を探す」で探し直してください";

    public const string FixOriginUnknownMessage = "案を作ったときの勤務表が分からないため適用しませんでした。もう一度探してください";

    public const string FixTargetUnknownMessage = "見ている問題が分からないため適用しませんでした。もう一度探してください";

    /// <summary>対象も全体の研磨の明示も無い案は断る。null＝断らない（Kotlin <c>fixTargetRefusal</c>）。</summary>
    public static string? FixTargetRefusal(FixTarget? fixTarget, bool globalPolish) => fixTarget is null && !globalPolish ? FixTargetUnknownMessage : null;

    /// <summary>確認の画面から当てる案は、案を作ったときの盤面の指紋が要る（0＝不明なら断る。今の全体の指紋で代わりに通さない）。null＝断らない（Kotlin <c>fixOriginRefusal</c>）。</summary>
    public static string? FixOriginRefusal(long originBoard, bool fromPreview) => fromPreview && originBoard == 0L ? FixOriginUnknownMessage : null;

    /// <summary>ホームの「次にやること」の段を決める（Kotlin <c>homePlan</c>、純関数）。主ボタンは常に 1 つ＝組立中と探索中だけ押せない。
    /// 保存の失敗と相談中は段を変えない＝完成の見出しと書き出しを止めない（D10）。</summary>
    public static HomePlan HomePlanOf(bool running, bool hasResult, bool hard0,
        bool infeasible = false, bool hasPinned = false, bool guided = false,
        bool hasMove = false, bool focusBlank = true, bool fixSearching = false, bool fixSearched = false,
        bool relax = false, bool stalled = false, bool wishes = false,
        bool saveFailed = false, int consults = 0, bool draft = false)
    {
        _ = saveFailed; _ = consults;
        if (running) return new(HomePlanKind.Running, HomeAction.None, false, null);
        if (!hasResult) return new(HomePlanKind.Make, HomeAction.Make, true, HomeAction.SmartInitial);
        // 下書きのまま（手を入れていない）＝次は仕上げの作成。直す一覧へは進めない。
        if (draft && !infeasible) return new(HomePlanKind.Draft, HomeAction.Make, true, HomeAction.Schedule);
        if (hard0) return new(HomePlanKind.PreDist, HomeAction.PreDist, true, HomeAction.Schedule);
        if (infeasible && hasPinned) return new(HomePlanKind.WishInfeasible, HomeAction.ShowWishes, true, HomeAction.Landing);
        if (infeasible) return new(HomePlanKind.DataInfeasible, HomeAction.Landing, true, HomeAction.PreDist);
        if (guided) return new(HomePlanKind.GuidedFix, HomeAction.Fix, true, null);
        if (hasMove && focusBlank) return new(HomePlanKind.ApplyMove, HomeAction.ShowMove, true, HomeAction.ApplyMove);
        if (fixSearching) return new(HomePlanKind.Searching, HomeAction.None, false, null);
        if (fixSearched && !hasMove && relax) return new(HomePlanKind.Relax, HomeAction.ShowRelax, true, wishes ? HomeAction.ShowWishes : null);
        if (fixSearched && !hasMove && stalled && wishes) return new(HomePlanKind.WishFloor, HomeAction.ShowWishes, true, HomeAction.PreDist);
        if (wishes) return new(HomePlanKind.Wishes, HomeAction.ShowWishes, true, null);
        return new(HomePlanKind.List, HomeAction.ShowList, true, null);
    }

    /// <summary>主ボタンが分析タブへ行く段（「ほかの問題を見る」と行き先が同じ）。</summary>
    private static readonly HashSet<HomeAction> HomeActionsToAnalysis = new() { HomeAction.ShowMove, HomeAction.ShowList };

    /// <summary>
    /// ホームの主カードの並び（Kotlin <c>homeCardSections</c>、純関数）: 作成の 2 行 → 見出し → 札 → 状態（2 行まで）→ 主ボタン 1 つ → 補助 →「ほかの問題を見る」→
    /// 詳細 → でき具合 → ⓘ → 下書きのリンク。実行中は進捗の行だけ。でき具合は結果があるときだけ（作っていないのに出さない）。
    /// </summary>
    public static IReadOnlyList<HomeSection> HomeCardSectionsOf(HomePlan plan, UiState ui)
    {
        var list = new List<HomeSection>();
        if (ui.Running) { list.Add(HomeSection.Progress); return list; }
        var creation = ui.HasResult && ui.EngineRan;
        if (creation) list.Add(HomeSection.Creation);
        list.Add(HomeSection.Headline);
        if (ui.HasResult) list.Add(HomeSection.Badges);
        list.Add(HomeSection.Status);
        if (plan.PrimaryEnabled) list.Add(HomeSection.Primary);
        if (plan.Helper is not null || ui.HasResult) list.Add(HomeSection.Helper);
        if (creation && !HomeActionsToAnalysis.Contains(plan.Primary)) list.Add(HomeSection.Others);
        list.Add(HomeSection.Detail);
        if (ui.HasResult) { list.Add(HomeSection.Satisfaction); list.Add(HomeSection.Info); list.Add(HomeSection.DraftLink); }
        return list;
    }

    /// <summary>下書きの段の状態の 1 文（次は仕上げの作成）。</summary>
    public static string HomeDraftStatus(string hard, long bestHard) =>
        "下書きができました" + (bestHard > 0L ? $"（必須 {hard}）" : "") + "。「勤務表をつくる」で仕上げます";

    public const string HomeMakeHeadline = "ボタンひとつで、勤務表をつくります";
    public const string HomeHard0Headline = "必須は満たしています";
    public const string HomeMonthHeadline = "今月の判断";
    public const string HomeScheduleHelper = "表で全体を見る";
    public const string HomeListButton = "必須を見る";
    public const string HomePreDistButton = "配布前の確認";
    public const string PreDistTitle = "配布前の確認";
    public const string HomeSoftNote = "要調整は分析で確認";
    public const string HomeOthersButton = "ほかの問題を見る";
    public const string InterruptedTitle = "前回の勤務表づくりは途中で止まりました";
    public const string EmptyStateTitle = "勤務表を開く";

    /// <summary>未読込の補足。読込中は 1 文だけ。</summary>
    public static string EmptyStateSubtitle(bool loading) => loading ? "読み込んでいます…" : "保存データ・サンプル・新規から選べます";

    /// <summary>対象の月の見出し「2026年10月の判断」（開始日が読めなければ「いまの判断」）。</summary>
    public static string HomeMonthHeadlineOf(string startDate)
    {
        var m = System.Text.RegularExpressions.Regex.Match(startDate, @"^(\d{4})-(\d{2})-\d{2}");
        if (!m.Success) return "いまの判断";
        return $"{int.Parse(m.Groups[1].Value)}年{int.Parse(m.Groups[2].Value)}月の判断";
    }

    /// <summary>段ごとの見出し: 未作成は 1 文、結果があれば「今月の判断」（必須の有無は札が言う。3.713.0 デザイン例）。</summary>
    public static string HomeHeadline(HomePlanKind kind, string startDate) => kind == HomePlanKind.Make ? HomeMakeHeadline : HomeMonthHeadline;

    public static ShortageHeadline ShortageHeadlineOf(string dayLabel, string shiftSymbol, int need, int got, int miss) =>
        new($"{(dayLabel.IndexOf('(') is var p and >= 0 ? dayLabel[..p] : dayLabel)}の{shiftSymbol}が{miss}人不足", $"配置 {got} / 必要 {need}", ShortageTag);

    public const string ShortageTag = "担当・希望の確認";

    public static ShortageHeadline ShortageHeadlineOf(CoverageShortfall s) => ShortageHeadlineOf(s.DayLabel, s.ShiftSymbol, s.Need, s.Got, s.Miss);

    /// <summary>必須の札は 1 つ＝残りの件数だけ（単位ごと＝INV-07）。必須 0 は「満たしています」。</summary>
    public static string HomeHardPill(long bestHard, string hardLabel) => bestHard > 0L ? $"必須 残り{hardLabel}" : HomeHard0Headline;

    public static IReadOnlyList<string> HomeBadges(string hardLabel, CoverageShortfall? shortage) => new[] { HomeHardPill(1L, hardLabel) };

    /// <summary>状態の行（2 行まで）。保存の失敗は段に関係なく先頭（SF-1）。必須あり＝問題の 1 文と相談中の行（D10）。必須 0＝相談中の行と、CSV が今の内容と違う行（保存済みの行は主ボタンの下）。</summary>
    public static IReadOnlyList<string> HomeStatusLines(HomePlanKind kind, string? sentence, string? consultLine, string? savedLine, bool saveFailed = false) =>
        new[] { saveFailed ? HomeSaveFailedLine : null }.Concat(kind == HomePlanKind.PreDist
            ? new[] { consultLine, savedLine is not null && savedLine.Contains("今の内容と違います", StringComparison.Ordinal) ? savedLine : null }
            : new[] { string.IsNullOrWhiteSpace(sentence) ? null : sentence, consultLine }).OfType<string>().Take(2).ToList();

    /// <summary>
    /// 段の対象（「次に確認する問題」と主ボタンの括弧の中の両方に使う＝INV-01）。null＝対象なし。
    /// 今すぐ直せる不足＝不足の枠、1手あり＝手の先頭のセル、希望の段＝関わる希望の先頭、緩める＝その組、残りの一覧と探索中＝巡回の先頭。
    /// </summary>
    public static string? CreationNextTarget(HomePlanKind kind, string? shortage, string? move, string? wish, string? relax, string? tour) => kind switch
    {
        HomePlanKind.GuidedFix => shortage,
        HomePlanKind.ApplyMove => move,
        HomePlanKind.WishInfeasible or HomePlanKind.WishFloor or HomePlanKind.Wishes => wish,
        HomePlanKind.Relax => relax,
        HomePlanKind.List or HomePlanKind.Searching => tour,
        _ => null,
    };

    public const string ContestAButton = "推奨Aで進む";
    public const string ContestBButton = "案Bにする";

    /// <summary>
    /// 争点カードを組む（Kotlin <c>contestCard</c>、D03）。<paramref name="main"/> は画面の主一覧と同じ順の案（新しく探さない・並べ替えない）。
    /// 対象が必須違反（日の人員の不足）でない、または案が無いときは null。A と B は同じ <paramref name="target"/> を直す案だけ＝INV-01。
    /// </summary>
    public static ContestCard? ContestCardOf(FixTarget target, string label, IReadOnlyList<FixSuggestion> main, IReadOnlyList<IReadOnlyList<int>> schedule,
        IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols)
    {
        if (target.Day is null || target.Multi || !target.Under) return null;
        var picks = main.Where(s => target.Effect(schedule, s.Ops).Fixes).Take(2).ToList();
        if (picks.Count == 0) return null;
        string Name(FixSuggestion s) => SurplusMoveTitle(target, schedule, s.Ops, staffNames, shiftSymbols) ?? FixOutcomeText.FixDisplayLabel(s.Label);
        var e = target.Effect(schedule, picks[0].Ops);
        var a = new ContestOption($"推奨A: {Name(picks[0])}", $"理由: {e.AfterNote(target.Basis)}（{e.GapLine()}）", picks[0]);
        var b = picks.Count > 1 ? new ContestOption($"案B: {Name(picks[1])}", null, picks[1]) : null;
        return new ContestCard($"{label}の{target.GapWord}", a, b);
    }

    /// <summary>確認の画面の「新たな必須違反」（null＝評価していない＝なしと言わない、統合仕様 INV-10）。</summary>
    public static string NewHardLine(IReadOnlyList<string>? names) =>
        "新たな必須違反：" + (names is null ? "未評価（適用の直前に検査します）" : names.Count == 0 ? "なし" : string.Join("、", names));

    /// <summary>適用後の「今回の問題」の状態（解消／減少／残存）。</summary>
    public static string AppliedStatusWord(CountEffect e) => e.GapAfter == 0 ? "解消" : e.GapAfter < e.GapBefore ? "減少" : "残存";

    /// <summary>相談事項に登録しても、連絡や了承の記録にはならない（記録だけ）。</summary>
    public const string ConsultNotContactNote = "相談事項に登録しても、本人への連絡や了承済みにはなりません。";

    /// <summary>「変更を反映しました」の行（Kotlin <c>appliedLines</c>）: 見ている問題（前 → 今）、期間全体の過不足（日の対象）、勤務表全体の必須、新たに生じた問題、
    /// ほかに人数が変わった枠、保存（保存中・失敗のときだけ）、残る問題、探し直したこと。族名は <paramref name="labelOf"/>（省略時は族キーのまま）。</summary>
    public static IReadOnlyList<string> AppliedLines(UiState ui, AppliedFix a, FixTarget? target, string label, Func<int, int, (int Lo, int Hi)?>? needLimits,
        Func<string, string>? labelOf = null)
    {
        var outList = new List<string>();
        if (target is not null)
        {
            var e = target.Effect(a.Before, a.Ops);
            outList.Add($"今回の問題：{AppliedStatusWord(e)}（{label}：{e.CountLine()}、{(e.GapAfter == 0 ? e.AfterNote(target.Basis) : $"{target.GapWord} {e.GapAfter}{target.Unit}")}）");
            if (target.Day is not null && !target.Multi)
            {
                var g0 = ShiftPeriodGap(a.Before, target.Shift, target.Under, needLimits);
                var g1 = ShiftPeriodGap(a.After, target.Shift, target.Under, needLimits);
                if (g0 is { } b0 && g1 is { } b1)
                    outList.Add(PeriodGapLine(target.Shift >= 0 && target.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[target.Shift] : $"{target.Shift}", target.Under, b0, b1));
            }
        }
        // 内訳があれば単位ごとに（件・人・回・pt を 1 つの数に足さない＝統合仕様 INV-07）。
        static string T(IReadOnlyDictionary<string, int>? b, IReadOnlyList<string> fams, int n) => b is null ? $"{n}件" : AnalysisTriage.UnitTotals(b, fams) ?? "なし";
        if (a.HardBefore >= 0) outList.Add($"必須 {T(a.BreakdownBefore, MirrorKeys.Hard, a.HardBefore)}→{T(a.BreakdownAfter, MirrorKeys.Hard, a.HardAfter)}");
        if (AppearedFamiliesLine(a.BreakdownBefore, a.BreakdownAfter, labelOf ?? (f => f)) is { } ap) outList.Add(ap);
        var (same, other) = HeadcountImpact(a.Before, a.Ops, ui.ShiftSymbols, ui.StartDate, needLimits, target?.Day is { } td ? (target.Shift, td) : null);
        outList.AddRange(same);
        outList.AddRange(other);
        if (ui.SaveState is SaveState.Failed or SaveState.Saving) outList.Add(SaveStateLine(ui.SaveState));
        // [3.712.0] 見ている問題の残りは 1 行目が言う＝「次」はほかの日の同じ勤務だけ（同じ文を 2 回出さない）。
        var next = target?.Day is not null ? RemainingSameShift(ui.NeedFamilies, ui.Schedule, target, needLimits, ui.ShiftSymbols, ui.StartDate) : Array.Empty<string>();
        if (next.Count > 0) outList.Add("次：" + string.Join("。", next));
        outList.Add("勤務表が変わったため、変更案を探し直しました（下の案は今の勤務表のものです）。");
        return outList;
    }

    /// <summary>当てたあとも残る、見ている問題と同じ勤務の過不足（日の対象＝ほかの日、回数＝基準までの差）。</summary>
    public static IReadOnlyList<string> AppliedRemaining(UiState ui, AppliedFix a, FixTarget? target, string label, Func<int, int, (int Lo, int Hi)?>? needLimits)
    {
        if (target is null) return Array.Empty<string>();
        var eff = target.Effect(a.Before, a.Ops);
        if (target.Day is not null)
        {
            var l = new List<string>();
            if (eff.GapAfter > 0) l.Add($"{label}は、まだ{target.GapWord} {eff.GapAfter}{target.Unit}です");
            l.AddRange(RemainingSameShift(ui.NeedFamilies, ui.Schedule, target, needLimits, ui.ShiftSymbols, ui.StartDate));
            return l;
        }
        return eff.GapAfter > 0 ? new[] { RemainingGapLine(label, target, eff.GapAfter) } : Array.Empty<string>();
    }

    /// <summary>当てたあとに増えた族（「新たに生じた問題：禁止の並び +1件」）。内訳が無いか増えた族が無ければ null。</summary>
    public static string? AppearedFamiliesLine(IReadOnlyDictionary<string, int>? before, IReadOnlyDictionary<string, int>? after, Func<string, string> labelOf)
    {
        if (before is null || after is null) return null;
        var up = before.Keys.Concat(after.Keys).Distinct().Where(k => after.GetValueOrDefault(k, 0) > before.GetValueOrDefault(k, 0))
            .OrderByDescending(MirrorKeys.WeightOf).ToList();
        if (up.Count == 0) return null;
        return "新たに生じた問題：" + string.Join("・", up.Select(k => $"{labelOf(k)} +{after.GetValueOrDefault(k, 0) - before.GetValueOrDefault(k, 0)}{AnalysisTriage.UnitOf(k)}"));
    }

    /// <summary>反映の帯の次の一歩のボタン。同じ問題が残れば続けてその勤務の案を、無ければ残りの必須違反の巡回を、どちらも無ければ null。
    /// か所の数は巡回できる項目の数（上部の札と同じ値。報告の必須合計は件と人が混ざる＝INV-07、3.693.0）。</summary>
    public static string? AppliedNextButton(bool sameRemains, string? sym, int hardLeft, int tourCount) =>
        sameRemains && sym is not null ? $"続けて {sym} の案を見る"
        : hardLeft > 0 && tourCount > 0 ? FixOutcomeText.GuidedTourButton(tourCount)
        : null;

    /// <summary>当てたあとに残る差（回数＝「A4は、下限まであと2回です」、まとめて見る日々＝「Dテ（2日）は、合計 3人不足です」）。</summary>
    public static string RemainingGapLine(string label, FixTarget t, int gap) =>
        t.Multi ? $"{label}は、合計 {gap}{t.Unit}{t.GapWord}です" : $"{label}は、{t.Basis}まであと{gap}{t.Unit}です";

    /// <summary>[operator_ux §5] ある不足枠(日×シフト)に1タップで入れられる候補職員。Kotlin <c>shortageCandidates</c> の移植。</summary>
    public static IReadOnlyList<FixCandidate> ShortageCandidates(MagiState st, Problem p, int[][] sched, int dayIndex, int shiftIndex, int? rest)
    {
        if (shiftIndex < 0 || shiftIndex >= p.K || dayIndex < 0 || dayIndex >= p.T) return System.Array.Empty<FixCandidate>();
        var outList = new List<FixCandidate>();
        for (var i = 0; i < p.S; i++)
        {
            if (i >= sched.Length || dayIndex >= sched[i].Length) continue;
            // 担当できない・上限 0・拡張希望で禁止のシフトは出さない（最適化器と同じ MayPlaceAt 基準）。
            if (!p.MayPlaceAt(i, dayIndex, shiftIndex)) continue;
            if (p.Pinned(i, dayIndex)) continue;   // 手動固定は自動でも変えない
            // 上限に届いている人は入れると上限超過になる。
            if (p.RangeHi[i][shiftIndex] != int.MaxValue && sched[i].Count(x => x == shiftIndex) >= p.RangeHi[i][shiftIndex]) continue;
            if (sched[i][dayIndex] == shiftIndex) continue;   // すでにそのシフト
            // [監査A5] 実現可能な希望のみ固定扱い（不可能希望のセルはエンジン同様に可動）。
            if (p.WishLocked(i, dayIndex) && p.LockTo(i, dayIndex) != shiftIndex) continue;
            // [3.401.0] CoverageDiagnosis が「空き番」と数えるのと同じ2条件で、実際に動かせる人だけを出す。
            //   ① 移すと禁止連続(c3n)になる人は出さない。
            if (p.MakesForbiddenRun(sched, i, dayIndex, shiftIndex)) continue;
            //   ② 抜けると元のシフトに穴が空く人は「動かせる人」ではない（玉突きが要る＝この画面の手には余る）。
            var from = sched[i][dayIndex];
            if (from >= 0 && from < p.K)
            {
                var cnt = 0;
                for (var it = 0; it < p.S; it++)
                    if (it < sched.Length && dayIndex < sched[it].Length && sched[it][dayIndex] == from) cnt++;
                if (p.CovUCell(from, dayIndex, cnt - 1) > p.CovUCell(from, dayIndex, cnt)) continue;
            }
            var g = i < st.StaffList.Count ? st.StaffList[i].GroupIdx : -1;
            var name = i < st.StaffList.Count ? st.StaffList[i].Name : UnknownLabel;
            var groupSym = g >= 0 && g < st.Groups.Count ? st.Groups[g].Kigou : "";
            outList.Add(new FixCandidate(i, name, groupSym, sched[i][dayIndex] == rest));
        }
        // 休みの人（動かしやすい）を先頭に。
        return outList.OrderBy(c => c.FromRest ? 0 : 1).ToList();
    }

    /// <summary>「{日} の希望を見る」（日が無ければ「希望を見る」）。希望の確認への行き先の名を 1 つにする。</summary>
    public static string WishDayButton(string? dayShort) => string.IsNullOrWhiteSpace(dayShort) ? "希望を見る" : $"{dayShort} の希望を見る";

    public const string CountSectionTitle = "対象期間の勤務回数";
    public const string MoreOpsLabel = "ほかの操作 ▸";

    /// <summary>セルのシートの「対象期間の勤務回数」は、その職員に回数の族（下限・上限・適切回数など）があるときだけ出す。</summary>
    public static bool ShowCountSection(IEnumerable<string> countKeys, int i, IReadOnlyList<string> lines) =>
        lines.Count > 0 && countKeys.Any(k => int.TryParse(k.Split(',')[0], out var x) && x == i);

    public const string CompactNoFix = "この場所を直す案は見つかりませんでした";

    /// <summary>セルのシートで案が無いときの 1 行。<paramref name="single"/>＝問題が 1 つならその名（前後の日へ案内する）、無ければ先頭の理由を添える。</summary>
    public static string CompactNoFixLine(IReadOnlyList<string> reasons, string? single, string? prevDay, string? nextDay)
    {
        if (single is not null)
        {
            var days = new[] { prevDay, nextDay }.OfType<string>().ToList();
            return $"このセルの{single}を解消する案は見つかりませんでした" + (days.Count == 0 ? "" : $"。前後の日（{string.Join("・", days)}）を開いて確かめられます");
        }
        return reasons.Count > 0 ? $"{CompactNoFix}。{reasons[0]}" : CompactNoFix;
    }

    public const string ViewFixesButton = "変更案を見る";

    public const string StaffOtherFixesLabel = "この職員のほかの変更案 ▸";

    /// <summary>職員の勤務状況で最初から選んでおく行（選べる行＝勤務のある行が 1 つだけのとき）。</summary>
    public static int? StaffSheetPreselect(IEnumerable<int> shiftIdxs)
    {
        var d = shiftIdxs.Where(x => x >= 0).Distinct().ToList();
        return d.Count == 1 ? d[0] : null;
    }

    public const string PickMarksLegend = "●＝必須違反が減る　⚠＝必須違反が増える";
    public const string TallyLegendText = "▼不足・▲超過（職員・日の欄の数字＝今の回数・人数、計（期間）の ▼2＝不足の日が 2日）";

    /// <summary>次に確認する問題の名（職員の問題は「{名}さん {見出し}」）。</summary>
    public static string CreationNextProblem(string? staffName, string heading) =>
        string.IsNullOrWhiteSpace(staffName) || heading.StartsWith(staffName, StringComparison.Ordinal) ? heading : $"{staffName}さん {heading}";

    /// <summary>主カード上端の 2 行: 作成の完了と、次に確認する問題（見出しと同じ対象なら完了の 1 行だけ）。</summary>
    public static IReadOnlyList<string> CreationTopLines(string? next, bool kept = false, string? headlineTarget = null) =>
        new[] { kept ? KeptResultText.KeptTitle : CreationDoneTitle, next is null || next == headlineTarget ? null : $"次に確認する問題：{next}" }.OfType<string>().ToList();

    public const string SoftPolishLink = "要調整を減らせるか試す";
    public const string SoftPolishNote = "減らない場合もあります。";

    public const string TriageHardHeading = "必須違反（配る前に直す必要があります）";
    public const string TriageSettingsHeading = "設定を変えないと消えない項目";

    /// <summary>分析の畳んだ一覧の見出し（内部の語を使わない）。</summary>
    public static string ConditionStatusTitle(int ok, int busy) => $"条件ごとの状況（守れている {ok}／残っている {busy}）";

    public static string BulkClearWishesLabel(int n) => $"担当外の希望 {n}件を外す";

    /// <summary>Kotlin <c>ALTERNATIVES_ALL_WORSE</c>（C# は同名のメソッドと並べられないため Text を付ける）。</summary>
    public const string AlternativesAllWorseText = "今の勤務表より良くありません";

    /// <summary>
    /// 他の案の 1 行（必須違反と、計算した時点の勤務表との比べ）。比べは採用の基準（BetterReport）の結果で語る＝
    /// <paramref name="better"/>＝案が今より良い、<paramref name="worse"/>＝今のほうが良い、どちらでもなければ今と同じ。
    /// </summary>
    public static string AlternativeSummary(int n, string hardTxt, int? curHard, int altHard, bool worse, bool better)
    {
        var cmp = curHard is null ? ""
            : altHard < curHard ? "（今より少ない）"
            : altHard > curHard ? "（今より多い）"
            : better ? "（今と同じ・全体は今より良い（重みつき））"
            : worse ? "（今と同じ・全体は今より悪い（重みつき））"
            : "（今と同じ）";
        return $"案{n}：必須違反 {hardTxt}{cmp}";
    }

    /// <summary>すべての案が今より悪い（印の数が案の数と合うときだけ）。</summary>
    public static bool AlternativesAllWorse(IReadOnlyList<bool> worse, int n) => n > 0 && worse.Count == n && worse.All(w => w);

    public const string ShortageLabelInfeasible = "担当できる人が足りない";
    public const string ShortageLabelBlocked = "今の希望では埋まらない";
    public const string ShortageLabelSearchMiss = "試した入れ替えでは埋まらなかった";
    public const string ShortageLabelFixable = "動かせば埋まる";
    public const string ShortageEnoughText = "担当できる人の数は足りています。「なおし方を見る」で 1 人ずつ入れられます";

    /// <summary>[3.712.0] 診断カードの行は 1 行の事実と札だけ。理由・内訳はカードの末尾の「詳しく」に 1 回だけ（Kotlin と同じ文言）。</summary>
    public const string DiagDetailsLabel = "詳しく";
    public const string DiagDetailsClose = "詳しくを閉じる";
    public const string ShortageTagInfeasible = "担当が足りない";
    public const string ShortageMovableLine = "休みや余裕のある勤務から移せます";
    public const string ForbiddenRetryNote = "玉突きか隣の日の調整で崩せます。もう一度つくると直る場合があります";

    /// <summary>人員不足の行の札（Kotlin <c>shortageRowTag</c>）。</summary>
    public static string ShortageRowTag(bool infeasible, bool blockedNow) =>
        infeasible ? ShortageTagInfeasible : blockedNow ? ShortageLabelBlocked : ShortageLabelFixable;

    /// <summary>人員不足の行: 「10/10(土) Cｵ 不足1」。</summary>
    public static string ShortageRowLine(string dayLabel, string shiftSymbol, int miss) => $"{dayLabel} {shiftSymbol} 不足{miss}";

    /// <summary>人員不足の行の下の 1 行（動かせば埋まる枠だけ）。</summary>
    public static string? ShortageRowNote(bool infeasible, bool blockedNow) => !infeasible && !blockedNow ? ShortageMovableLine : null;

    /// <summary>人員不足カードの見出しの下の 1 行: 「足りない日3 · 動かせる枠あり」。</summary>
    public static string ShortageCardLine(int days, bool movable) => $"足りない日{days} · " + (movable ? "動かせる枠あり" : "動かせる枠なし");

    /// <summary>人員過剰の行の理由（「。内訳: …」から先は「詳しく」へ）。</summary>
    public static string SurplusShortReason(string reason)
    {
        var i = reason.IndexOf("。内訳: ", StringComparison.Ordinal);
        return i < 0 ? reason : reason[..i];
    }

    /// <summary>禁止の並びの行の札。</summary>
    public static string ForbiddenRunTag(bool escapable) => escapable ? "崩せる" : "崩せない";

    /// <summary>禁止の並びの行の下の 1 行: 「10/9 Dﾃ · 10/10 Aｱ」。</summary>
    public static string ForbiddenRunCellsLine(IEnumerable<(string Day, string Symbol)> cells) => string.Join(" · ", cells.Select(c => $"{c.Day} {c.Symbol}"));

    public const string SettingsTimeTitle = "勤務表をつくる時間";

    /// <summary>名前・記号が引けないときの共通の表示（番号を見せない）。</summary>
    public const string UnknownLabel = "—";

    /// <summary>進捗の基準（InitHard/InitSoft）を取り直すかの指紋＝データの指紋＋盤面差し替えの回数。</summary>
    public static long InitBaseKeyOf(long stateKey, long boardReplaceRev) => unchecked(stateKey * 1_000_003L + boardReplaceRev);

    /// <summary>ホームの見出しが読む解消の行（鮮度が合わなければ空＝件数だけの見出し）。</summary>
    public static IReadOnlyList<string> TopFixResolvedLabels(UiState ui)
    {
        var top = ui.FixSuggestions.FirstOrDefault(f => f.DeltaHard < 0);
        if (top is null) return Array.Empty<string>();
        return ui.TopFixResolved is { } t && t.Matches(top.Ops, ui.Schedule) ? t.Labels : Array.Empty<string>();
    }

    /// <summary>実行中の進捗サマリ文字列: 残りの違反（開始の値つき）・ 残り時間。読取専用。反復数は作り手の指標なので出さない。</summary>
    public static string ProgressSummary(UiState ui)
    {
        var parts = new List<string>(4);
        if (ui.BestHard > 0L)
        {
            // 開始は基準の盤面の内訳から同じ単位で言う（内訳が無い・減っていなければ出さない＝件と人を混ぜない）。
            var start = ui.InitBreakdown.Count > 0 && ui.InitHard > ui.BestHard ? $"（開始{AnalysisTriage.HardText(ui.InitBreakdown, ui.InitHard)}）" : "";
            parts.Add($"必須違反 残り{AnalysisTriage.HardText(ui.Breakdown, ui.BestHard)}{start}");
        }
        else if (ui.InitBreakdown.Count > 0 || ui.BestSoft > 0L)
        {
            // 要調整も族の単位ごとの合計で言う（生の件数は単位が混ざる）。
            var now = AnalysisTriage.UnitTotals(ui.Breakdown, MirrorKeys.Soft) ?? "なし";
            var start = ui.InitBreakdown.Count > 0 ? AnalysisTriage.UnitTotals(ui.InitBreakdown, MirrorKeys.Soft) : null;
            parts.Add($"要調整 {now}" + (start is not null && start != now ? $"（開始{start}）" : ""));
        }
        else parts.Add("要調整 –");
        var secLeft = Math.Max(0L, (ui.BudgetSec + V6FinalPort.PostReserveSec + V6FinalPort.ChainReserveSec) * 1000L - ui.ElapsedMs) / 1000L;
        parts.Add($"残り {secLeft / 60}:{secLeft % 60:00}");
        return string.Join("  ・  ", parts);
    }
}

/// <summary>[operator_ux §5] 不足枠の候補職員 1 人。</summary>
public sealed record FixCandidate(int StaffIndex, string Name, string GroupSymbol, bool FromRest);

/// <summary>
/// 画面で見ている 1 つの問題（Kotlin <c>FixTarget</c>、2026-10-14 利用者レビュー「今回の問題がどう変わるかを最上段に」）。
/// 回数＝Staff×Shift（Day は null）、人員＝Shift×Day（Staff は null）。Target は下限・目標・上限・必要人数・適正人数、Basis はその名、Under＝足りない側。
/// </summary>
public sealed record FixTarget(int? Staff, int Shift, int? Day, int Target, bool Under, string Basis,
    IReadOnlyList<int>? Days = null, IReadOnlyList<int>? Needs = null)
{
    /// <summary>複数の日をまとめて見る（同じ勤務の不足の日々と、それぞれの必要人数）。空＝1 つの枠。</summary>
    public IReadOnlyList<int> Days { get; init; } = Days ?? Array.Empty<int>();
    public IReadOnlyList<int> Needs { get; init; } = Needs ?? Array.Empty<int>();

    public string Unit => Staff is not null ? "回" : "人";
    public bool Multi => Days.Count > 0;

    public int CountOf(IReadOnlyList<IReadOnlyList<int>> schedule) =>
        Staff is { } i ? (i >= 0 && i < schedule.Count ? schedule[i].Count(v => v == Shift) : 0)
        : Multi ? Days.Sum(j => schedule.Count(r => j >= 0 && j < r.Count && r[j] == Shift))
        : schedule.Count(r => Day is { } d && d >= 0 && d < r.Count && r[d] == Shift);

    /// <summary>日ごとの差の合計（まとめて見るとき）。日をまたいで足し引きしない＝ある日の過剰で別の日の不足を相殺しない。</summary>
    public int GapOf(IReadOnlyList<IReadOnlyList<int>> schedule) => Enumerable.Range(0, Days.Count).Sum(n =>
    {
        var c = schedule.Count(r => Days[n] >= 0 && Days[n] < r.Count && r[Days[n]] == Shift);
        return Math.Max(0, Under ? Needs[n] - c : c - Needs[n]);
    });

    /// <summary>盤面に案のセル代入を当てて数え直す＝評価器と同じ数え方。</summary>
    public CountEffect Effect(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops)
    {
        var after = schedule.Select(r => r.ToArray()).ToList();
        foreach (var op in ops)
            if (op.Staff >= 0 && op.Staff < after.Count && op.Day >= 0 && op.Day < after[op.Staff].Length) after[op.Staff][op.Day] = op.ToShift;
        var afterBoard = after.Select(r => (IReadOnlyList<int>)r).ToList();
        return new CountEffect(CountOf(schedule), CountOf(afterBoard), Target, Under, Unit, Multi ? (GapOf(schedule), GapOf(afterBoard)) : null);
    }

    public string GapWord => Under ? "不足" : "超過";

    // Kotlin の data class は List を中身で比べる＝C# の record も Days/Needs を要素で比べる（既定はリストの参照比較）。
    public bool Equals(FixTarget? o) => o is not null && Staff == o.Staff && Shift == o.Shift && Day == o.Day && Target == o.Target && Under == o.Under
        && Basis == o.Basis && Days.SequenceEqual(o.Days) && Needs.SequenceEqual(o.Needs);
    public override int GetHashCode() => HashCode.Combine(Staff, Shift, Day, Target, Under, Basis, Days.Count, Needs.Count);
}

/// <summary>対象の数の変化（Kotlin <c>CountEffect</c>）。GapBefore/GapAfter は基準までの差（0 未満は 0）。</summary>
/// <param name="Gaps">まとめて見るときの日ごとの差の合計（前, 後）。null＝1 つの枠。</param>
public sealed record CountEffect(int Before, int After, int Target, bool Under, string Unit = "回", (int Before, int After)? Gaps = null)
{
    private int Gap(int c) => Math.Max(0, Under ? Target - c : c - Target);
    public int GapBefore => Gaps?.Before ?? Gap(Before);
    public int GapAfter => Gaps?.After ?? Gap(After);
    public bool Fixes => GapAfter < GapBefore;

    /// <summary>「A4を2回増やす」。対象が変わらない案は「A4は変わりません」。</summary>
    public string Title(string label) =>
        After > Before ? $"{label}を{After - Before}{Unit}増やす"
        : After < Before ? $"{label}を{Before - After}{Unit}減らす"
        : $"{label}は変わりません";

    public string CountLine() => $"{Before}{Unit} → {After}{Unit}";
    public string GapLine() => $"{(Under ? "不足" : "超過")} {GapBefore}{Unit} → {GapAfter}{Unit}";

    public string AfterNote(string basis) =>
        Gaps is not null && GapAfter == 0 ? $"対象の日はすべて{basis}を{(Under ? "満たします" : "超えなくなります")}"
        : Gaps is not null ? $"適用後も、合計 {GapAfter}{Unit}{(Under ? "不足" : "超過")}"
        : GapAfter == 0 && Under && Unit == "人" ? $"{(basis.EndsWith("人数", StringComparison.Ordinal) ? basis[..^2] : basis)} {Target}人を満たします"
        : GapAfter == 0 && Unit == "人" ? $"{(basis.EndsWith("人数", StringComparison.Ordinal) ? basis[..^2] : basis)} {Target}人以内になります"
        : GapAfter == 0 && Under ? $"適用後は{basis}に届きます"
        : GapAfter == 0 ? $"適用後は{basis}以内になります"
        : Under ? $"適用後も、{basis}まで{GapAfter}{Unit}不足"
        : $"適用後も、{basis}を{GapAfter}{Unit}超えます";
}

/// <summary>人員不足の 1 文（ホームの「いま決めること」と人員不足の手伝いの題）と、札・状態の行（Kotlin <c>ShortageHeadline</c>）。</summary>
public sealed record ShortageHeadline(string Headline, string Status, string Badge);

/// <summary>セルを 1 つ変える前の確認の中身（Kotlin <c>CellChangeReview</c>）。</summary>
public sealed record CellChangeReview(
    string Change, string Neighbors, IReadOnlyList<string> ThisCell, IReadOnlyList<string> OthersResolved,
    IReadOnlyList<string> Appeared, string HardLine, IReadOnlyList<string> Headcount, IReadOnlyList<string> Counts, IReadOnlyList<string> WishPin,
    string? Verdict = null, bool VerdictBad = false);

/// <summary>争点カード（D03）の 1 案（Kotlin <c>ContestOption</c>）: 案の名、理由（推奨A だけ）、選んだときに確認へ渡す案。</summary>
public sealed record ContestOption(string Label, string? Reason, FixSuggestion Suggestion);

/// <summary>争点カード（D03、Kotlin <c>ContestCard</c>）: 見ている必須違反の題と、既存の主一覧の上位 2 件（推奨A／案B）。</summary>
public sealed record ContestCard(string Title, ContestOption A, ContestOption? B);

/// <summary>勤務表タブの状態の 1 行（Kotlin <c>ScheduleStateLine</c>）。TourFirst＝押すと巡回の先頭を開く（必須違反があり、セルで辿れるときだけ）。</summary>
public sealed record ScheduleStateLine(string Text, bool Hard, bool TourFirst);

/// <summary>[3.707.0 E-2] 編集タブの見出しカードの主ボタン（塗りは 1 つ＝INV-09）。Rerun は下部バーの「再作成」、Panel は開いている入力パネルの適用ボタン。</summary>
public enum EditHeaderPrimary { EditNeed, OpenMaster, AddStaff, Rerun, Panel }
public enum EditHeaderSecondary { WishConflicts }

/// <summary>見出しカードに要る数（画面の状態から集める）。Month は「2026年11月」、無ければ空。</summary>
public sealed record EditHeaderCounts(
    string Month = "", int Staff = 0, int Shifts = 0, WishEntryCounts? Wish = null, bool NeedStdOk = false, int NeedExceptions = 0,
    int Issues = 0, bool WishConflicts = false)
{
    public WishEntryCounts Wish { get; init; } = Wish ?? new WishEntryCounts(0, 0, 0);
}

public sealed record EditHeaderPlan(string Title, string Subtitle, IReadOnlyList<string> Points, EditHeaderPrimary? Primary, EditHeaderSecondary? Secondary);
