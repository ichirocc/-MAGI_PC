using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>入り口の範囲の中の 1 セルの変更（Tier 1）。適用できなければ Reason に門の拒否理由を持つ。Kotlin <c>EntryMove</c>。</summary>
public sealed record EntryMove(FixSuggestion Sug, bool Applicable, string? Reason);

/// <summary>案の段: T1＝範囲の中の 1 セル、T2＝付随の変更がすべて理由つき、MoveOnly＝期間全体の過不足が減らない、T3＝そのほか（別の問題）。</summary>
public enum FixTier { T1, T2, MoveOnly, T3 }

public sealed record TieredFix(FixSuggestion Sug, FixTier Tier, FixExplanation? Explain = null);

/// <summary>段に分けた案と、適用できない Tier 1 の理由の行。Key は依頼の鍵（別の依頼の結果を出さない）。</summary>
public sealed record FixTierView(string Key, IReadOnlyList<TieredFix> Items, IReadOnlyList<string> Blocked)
{
    public IReadOnlyList<TieredFix> Of(FixTier t) => Items.Where(x => x.Tier == t).ToList();
    /// <summary>主の一覧（T1・T2）に出す案があるか。</summary>
    public bool HasMain => Items.Any(x => x.Tier == FixTier.T1 || x.Tier == FixTier.T2);
}

/// <summary>Kotlin <c>FixTiers.kt</c>（3.679.0）。</summary>
public static class FixTiers
{
    /// <summary>入り口の範囲の中の候補の評価回数の上限（セルのシートの 3 秒に収める）。</summary>
    public const int EntryMoveEvals = 60;

    /// <summary>
    /// 入り口の範囲の中で 1 セルだけを変える候補を作り、適用の門（<see cref="FixApplyGate"/>）で 1 つずつ評価する。
    /// 日の人員＝その日の 1 人のセル、個人の回数＝その人の 1 セル。過剰は外す側（休を先に試す）、不足は入れる側。
    /// 置き換え先は最適化器が置いてよいもの（<see cref="Problem.MayPlaceAt"/>＝担当可・上限 0 でない・拡張希望で禁止されていない）だけ＝評価の回数を無駄にしない。
    /// セルごとに最も良い置き換え先を 1 つ残す（通るものがあれば通るものを優先）。まとめて見る対象は作らない。
    /// </summary>
    public static IReadOnlyList<EntryMove> EntryMoves(MagiState state, int[][] s, FixTarget target, int? rest, int maxEvals = EntryMoveEvals)
    {
        if (target.Multi) return Array.Empty<EntryMove>();
        var k = target.Shift;
        var nK = state.Shifts.Count;
        var cells = (target.Staff is { } ts ? (ts >= 0 && ts < s.Length ? Enumerable.Range(0, s[ts].Length).Select(j => (ts, j)) : Enumerable.Empty<(int, int)>())
                : target.Day is { } td ? Enumerable.Range(0, s.Length).Select(i => (i, td))
                : Enumerable.Empty<(int, int)>())
            .Where(c => c.Item2 >= 0 && c.Item2 < s[c.Item1].Length && (target.Under ? s[c.Item1][c.Item2] != k : s[c.Item1][c.Item2] == k)).ToList();
        var board = s.Select(r => (IReadOnlyList<int>)r.ToList()).ToList();
        var p = new Problem(state);
        var before = UnifiedViolationChecker.Check(state, s);
        var evals = 0;
        var outList = new List<EntryMove>();
        foreach (var (i, j) in cells)
        {
            var tos = target.Under ? new List<int> { k }
                : (rest is { } r0 && r0 >= 0 && r0 < nK && r0 != k ? new List<int> { r0 } : new List<int>())
                    .Concat(Enumerable.Range(0, nK).Where(x => x != k && x != rest)).ToList();
            tos = tos.Where(x => p.MayPlaceAt(i, j, x)).ToList();
            (FixApplyGate.Outcome R, FixCell Op)? best = null;
            foreach (var to in tos)
            {
                if (evals >= maxEvals) break;
                evals++;
                var op = new FixCell(i, j, to);
                var r = FixApplyGate.Apply(state, s, new[] { op }, p, before);
                var cur = best;
                best = cur is null ? (r, op)
                    : r.Applied && !cur.Value.R.Applied ? (r, op)
                    : r.Applied && cur.Value.R.Applied && UnifiedViolationChecker.BetterReport(r.After!, cur.Value.R.After!) ? (r, op)
                    : cur;
                // 休で通れば十分（過剰を外す 1 セルは休がいちばん他を崩しにくい）。
                if (r.Applied && to == rest) break;
            }
            if (best is null) break;
            var (res, bop) = best.Value;
            if (!target.Effect(board, new[] { bop }).Fixes) continue;
            var after = res.After;
            var name = i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : CellSheetLogic.UnknownLabel;
            var curSym = s[i][j] >= 0 && s[i][j] < nK ? state.Shifts[s[i][j]].Kigou : "—";
            var toSym = bop.ToShift >= 0 && bop.ToShift < nK ? state.Shifts[bop.ToShift].Kigou : CellSheetLogic.UnknownLabel;
            var label = $"{name} {DayText.Short(state.StartDate, j)} {curSym}→{toSym}";
            var diff = after is null ? new List<(string Family, int Delta)>()
                : res.Before.Breakdown.Keys.Concat(after.Breakdown.Keys).Distinct()
                    .Select(f => (Family: f, Delta: after.Breakdown.GetValueOrDefault(f, 0) - res.Before.Breakdown.GetValueOrDefault(f, 0)))
                    .Where(x => x.Delta != 0).OrderBy(x => x.Delta).ToList();
            var sug = new FixSuggestion(FixKind.Change, new[] { bop }, label, (after?.Hard ?? res.Before.Hard) - res.Before.Hard, (after?.Total ?? res.Before.Total) - res.Before.Total, diff);
            outList.Add(res.Applied ? new EntryMove(sug, true, null) : new EntryMove(sug, false, res.Reason));
            if (evals >= maxEvals) break;
        }
        return outList;
    }

    /// <summary>適用できない Tier 1 の 1 行（ボタンは出さない＝利用者決定 U2）。</summary>
    public static string BlockedEntryLine(EntryMove m) => $"{m.Sug.Label} は適用できません：{m.Reason ?? "理由を確かめられません"}";

    private sealed class OpsComparer : IEqualityComparer<IReadOnlyList<FixCell>>
    {
        public bool Equals(IReadOnlyList<FixCell>? a, IReadOnlyList<FixCell>? b) => a is not null && b is not null && a.SequenceEqual(b);
        public int GetHashCode(IReadOnlyList<FixCell> o) => o.Aggregate(17, (h, c) => h * 31 + c.GetHashCode());
    }

    /// <summary>
    /// 案を段に分ける（並びは Tier 1 → T2 → 移すだけ → T3、各段の中は元の順）。範囲の中の 1 セルの案（入り口の候補と、
    /// 探索が出した付随の変更の無い 1 セルの案）は T1。対象の数が減らない案は T3。日の人員の対象で期間全体の過不足が減らない案は移すだけ。
    /// 付随の変更がすべて理由つきで、範囲の外の必須違反を直すための変更を含まない案は T2、そうでなければ T3（利用者決定 U3）。
    /// 説明（チェッカー）は explainCap 件まで。
    /// </summary>
    public static FixTierView ClassifyTiers(
        MagiState state, int[][] s, FixTarget target, IReadOnlyList<EntryMove> entry, IReadOnlyList<FixSuggestion> list,
        Func<int, int, (int Lo, int Hi)?>? needLimits, int explainCap = 8, Func<string, string>? labelOf = null)
    {
        var board = s.Select(r => (IReadOnlyList<int>)r.ToList()).ToList();
        var b0 = UnifiedViolationChecker.Check(state, s);
        var seen = new HashSet<IReadOnlyList<FixCell>>(new OpsComparer());
        var items = new List<TieredFix>();
        foreach (var m in entry) if (m.Applicable && seen.Add(m.Sug.Ops)) items.Add(new TieredFix(m.Sug, FixTier.T1));
        var g0 = target.Day is not null && !target.Multi ? CellSheetLogic.ShiftPeriodGap(board, target.Shift, target.Under, needLimits) : null;
        var explained = 0;
        foreach (var sug in list)
        {
            if (!seen.Add(sug.Ops)) continue;
            if (!target.Effect(board, sug.Ops).Fixes) { items.Add(new TieredFix(sug, FixTier.T3)); continue; }
            var (primary, extras) = FixExplain.SplitPrimary(target, board, sug.Ops);
            if (primary.Count == 0) { items.Add(new TieredFix(sug, FixTier.T3)); continue; }
            if (extras.Count == 0 && primary.Count == 1) { items.Add(new TieredFix(sug, FixTier.T1)); continue; }
            FixExplanation? ex = null;
            if (extras.Count > 0 && explained < explainCap) { explained++; ex = FixExplain.ExplainExtras(state, s, target, sug, b0, labelOf); }
            var moveOnly = g0 is { } g && (CellSheetLogic.ShiftPeriodGap(CellSheetLogic.BoardAfter(board, sug.Ops), target.Shift, target.Under, needLimits) ?? g) >= g;
            var tier = moveOnly ? FixTier.MoveOnly
                : extras.Count == 0 ? FixTier.T2
                : ex is not null && ex.AllExplained && !ex.MainlyOutOfFrameHard ? FixTier.T2
                : FixTier.T3;
            items.Add(new TieredFix(sug, tier, ex));
        }
        return new FixTierView("", items.OrderBy(x => (int)x.Tier).ToList(), entry.Where(m => !m.Applicable).Take(3).Select(BlockedEntryLine).ToList());
    }

    /// <summary>移すだけの案の見出し（利用者決定 U1: 主の一覧から畳む）。</summary>
    public static string MoveOnlyHeading(string sym, bool under, int n) =>
        $"{sym}の{(under ? "不足" : "過剰")}は減りません（別の日へ移すだけの案 {n}件）";

    /// <summary>Tier 1 の見出し（日の人員＝この日だけ、個人の回数＝この人の 1 か所だけ）。</summary>
    public static string Tier1Heading(FixTarget t) => t.Staff is not null ? "この人の勤務を1つだけ変える案" : "この日の勤務を1つだけ変える案";

    public const string Tier2Heading = "崩れた分も合わせて整える案";

    public const string MoveOnlyNote = "見ている日は直りますが、同じ勤務の別の日の過不足が増えます";

    /// <summary>移すだけの案の確認の画面の目的（「解消する」と言わない）。</summary>
    public static string MoveOnlyPurpose(string label, FixTarget t) => $"{label}の{t.GapWord}を別の日へ移す";
}
