using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>シフトボタン 1 枠。CanDo=false は枠を残したまま「外」で灰色にする。</summary>
public sealed record ShiftSlot(int Shift, bool CanDo);

public enum CellSeverity { Hard, Soft, None }

/// <summary>直し方探しの状態（計算・チェック待ち／未開始／探索中／完了／失敗）。</summary>
public enum FixPanelState { WaitCheck, NotStarted, Running, Done, Failed }

/// <summary>操作の通知。<paramref name="UndoSerial"/>＝その操作が積んだ元に戻すの段。</summary>
public sealed record OpNotice(long Id, string Text, long UndoSerial);

/// <summary>Cause は接頭辞（必須/要調整）を除いた原因だけ（希望を守っている板挟みの 2 行目に使う）。</summary>
public sealed record CellStatus(CellSeverity Severity, string Text, string Cause = "");

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
    public static CellStatus StatusLine(MagiState state, Problem p, int[][] s, int i, int j, IReadOnlyList<string> families, Func<string, string> labelOf)
    {
        if (families.Count == 0) return new CellStatus(CellSeverity.None, "違反なし");
        var top = families[0];
        var hard = families.Any(f => MirrorKeys.Hard.Contains(f));
        var detail = FamilyDetail(state, p, s, i, j, top, labelOf) ?? labelOf(top);
        var more = families.Count > 1 ? $"（ほか{families.Count - 1}件）" : "";
        return hard ? new CellStatus(CellSeverity.Hard, $"⚠ 必須：{detail}{more}", detail + more)
            : new CellStatus(CellSeverity.Soft, $"⚠ 要調整：{detail}{more}", detail + more);
    }

    private static string? FamilyDetail(MagiState state, Problem p, int[][] s, int i, int j, string fam, Func<string, string> labelOf)
    {
        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : "?";
        string Name(int x) => x >= 0 && x < state.StaffList.Count ? state.StaffList[x].Name : $"#{x}";
        string Day(int d) { var f = ScheduleUtil.FormatDay(state.StartDate, d); var q = f.IndexOf('('); return q >= 0 ? f[..q] : f; }
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
                return c is null ? null : $"{labelOf(fam)} {string.Join("→", c.Seq.Select(Sym))} が{Day(j)}から続かない";
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
                return fam == "covU" ? $"{Day(j)}の{Sym(cur)}が人員不足（必要{lo}人に{n}人）" : $"{Day(j)}の{Sym(cur)}が人員過剰（適正{hi}人に{n}人）";
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
            case "high": return cur < 0 ? null : $"{Sym(cur)}が{Count(cur)}回（上限{p.RangeHi[i][cur]}）";
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

    private static (int[] Seq, int J0)? ForbiddenRunAt(Problem p, int[][] s, int i, int j, IReadOnlyList<C3> list)
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

    /// <summary>1 行の回数（例「Aｱ 7(適8)▼」）。この職員の回数の族があるシフトだけ。</summary>
    public static string StaffCountShort(MagiState state, Problem p, int[][] s, int i, IReadOnlyDictionary<string, IReadOnlyList<string>> countClasses)
    {
        var parts = new List<string>();
        for (var k = 0; k < p.K; k++)
        {
            if (!countClasses.TryGetValue($"{i},{k}", out var cls) || cls.Count == 0) continue;
            var fams = cls.Select(c => c.StartsWith("vio-", StringComparison.Ordinal) ? c[4..] : c).ToList();
            var n = s[i].Count(x => x == k);
            (string Ref, bool Under) t =
                fams.Contains("low") ? ($"下{p.RangeLo[i][k]}", true) :
                fams.Contains("high") ? ($"上{p.RangeHi[i][k]}", false) :
                fams.Contains("aptLow") ? ($"適{p.Apt[i][k]}", true) :
                fams.Contains("aptHigh") ? ($"適{p.Apt[i][k]}", false) :
                (p.Cons2.FirstOrDefault(c => c.ShiftIdx == k) is { } c2 ? $"計{c2.Count}" : "", true);
            var sym = k < state.Shifts.Count ? state.Shifts[k].Kigou : "?";
            parts.Add($"{sym} {n}{(t.Ref.Length == 0 ? "" : $"({t.Ref})")}{(t.Under ? "▼" : "▲")}");
        }
        return string.Join(" ", parts);
    }

    /// <summary>日送りボタンの日付「7日(水)」（範囲外は null）。</summary>
    public static string? AdjacentDayLabel(string startDate, int days, int j)
    {
        if (j < 0 || j >= days) return null;
        var f = ScheduleUtil.FormatDay(startDate, j);
        if (!f.Contains('/')) return f;
        var rest = f[(f.IndexOf('/') + 1)..];
        var q = rest.IndexOf('(');
        return q >= 0 ? rest[..q] + "日" + rest[q..] : rest;
    }

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

    /// <summary>通知の「元に戻す」は、その操作が今も元に戻すの先頭にあるときだけ効く（後の別の操作を戻さない）。</summary>
    public static bool NoticeUndoApplies(long? topSerial, long noticeSerial) => topSerial is { } t && t == noticeSerial;

    /// <summary>操作の通知を出している間、通常の文言では置き換えない（失敗・拒否だけは置き換える）。</summary>
    public static bool MessageMayReplaceNotice(bool noticeShowing, bool isError) => !noticeShowing || isError;

    public static string CellChangedMessage(string name, int day, string symbol) => $"{name} {day + 1}日を{symbol}に変更しました";
}
