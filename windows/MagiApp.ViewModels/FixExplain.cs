using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>付随の変更 1 つと、その理由（null＝外しても重い族が増えない＝理由を言えない）。OutOfFrameHard＝範囲の外の必須違反を直すための変更。Kotlin <c>ExtraReason</c>。</summary>
public sealed record ExtraReason(IReadOnlyList<FixCell> Ops, string? Reason, bool OutOfFrameHard = false);

/// <summary>案の説明: 入り口の範囲の中の主の変更、付随の変更と理由、ついでに直る範囲の外の必須違反。Kotlin <c>FixExplanation</c>。</summary>
public sealed record FixExplanation(IReadOnlyList<FixCell> Primary, IReadOnlyList<ExtraReason> Extras, IReadOnlyList<string> Incidental)
{
    public bool AllExplained => Extras.All(x => x.Reason is not null);
    public bool MainlyOutOfFrameHard => Extras.Any(x => x.OutOfFrameHard);
}

/// <summary>Kotlin <c>FixExplain.kt</c>（3.679.0）。族の表示語は UI 層が持つので <c>labelOf</c> で受け取る。</summary>
public static class FixExplain
{
    /// <summary>入り口の範囲の中で、対象の数を正しい向きへ動かす変更か（日の人員＝その日のその勤務、個人の回数＝その人のその勤務）。</summary>
    public static bool IsPrimaryOp(FixTarget t, IReadOnlyList<IReadOnlyList<int>> schedule, FixCell op)
    {
        if (op.Staff < 0 || op.Staff >= schedule.Count || op.Day < 0 || op.Day >= schedule[op.Staff].Count) return false;
        var cur = schedule[op.Staff][op.Day];
        var inFrame = t.Staff is not null ? op.Staff == t.Staff
            : t.Multi ? t.Days.Contains(op.Day)
            : op.Day == t.Day;
        if (!inFrame) return false;
        return t.Under ? cur != t.Shift && op.ToShift == t.Shift : cur == t.Shift && op.ToShift != t.Shift;
    }

    /// <summary>主の変更と付随の変更に分ける。</summary>
    public static (IReadOnlyList<FixCell> Primary, IReadOnlyList<FixCell> Extras) SplitPrimary(FixTarget t, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<FixCell> ops) =>
        (ops.Where(o => IsPrimaryOp(t, schedule, o)).ToList(), ops.Where(o => !IsPrimaryOp(t, schedule, o)).ToList());

    /// <summary>族の重みつきの差（後 − 前）。正＝後のほうが重い。</summary>
    private static Dictionary<string, double> WeightedDelta(ViolationReport before, ViolationReport after) =>
        before.Breakdown.Keys.Concat(after.Breakdown.Keys).Distinct().ToDictionary(k => k,
            k => (after.Breakdown.GetValueOrDefault(k, 0) - before.Breakdown.GetValueOrDefault(k, 0)) * MirrorKeys.WeightOf(k));

    /// <summary>
    /// 付随の変更を 1 つずつ外して正式な検査で数え直し、外すと最も重く増える族を理由にする（チェッカーの呼び出しは 1 案あたり 3 回まで。
    /// 付随の変更が 4 つ以上なら 3 つ目以降をまとめて外す）。理由の族が範囲の外の必須違反なら、その変更は範囲の外を直すためのもの。
    /// </summary>
    public static FixExplanation ExplainExtras(MagiState state, int[][] s, FixTarget target, FixSuggestion sug,
        ViolationReport? baseReport = null, Func<string, string>? labelOf = null)
    {
        var label = labelOf ?? (f => f);
        var b0 = baseReport ?? UnifiedViolationChecker.Check(state, s);
        var board = s.Select(r => (IReadOnlyList<int>)r.ToList()).ToList();
        var (primary, extras) = SplitPrimary(target, board, sug.Ops);
        var names = state.StaffList.Select(x => x.Name).ToList();
        var syms = state.Shifts.Select(x => x.Kigou).ToList();
        int[][] Apply(IEnumerable<FixCell> ops)
        {
            var w = s.Select(r => r.ToArray()).ToArray();
            foreach (var op in ops) if (op.Staff >= 0 && op.Staff < w.Length && op.Day >= 0 && op.Day < w[op.Staff].Length) w[op.Staff][op.Day] = op.ToShift;
            return w;
        }
        var full = UnifiedViolationChecker.Check(state, Apply(sug.Ops));
        var groups = extras.Count <= 3 ? extras.Select(x => (IReadOnlyList<FixCell>)new[] { x }).ToList()
            : extras.Take(2).Select(x => (IReadOnlyList<FixCell>)new[] { x }).Append(extras.Skip(2).ToList()).ToList();
        var targetFams = CellSheetLogic.FixTargetFamilies(target);
        var reasons = groups.Select(g =>
        {
            var without = UnifiedViolationChecker.Check(state, Apply(sug.Ops.Where(o => !g.Contains(o))));
            var pos = WeightedDelta(full, without).Where(kv => kv.Value > 0).ToList();
            if (pos.Count == 0) return new ExtraReason(g, null);
            var top = pos.MaxBy(kv => kv.Value).Key;
            var n = without.Breakdown.GetValueOrDefault(top, 0) - full.Breakdown.GetValueOrDefault(top, 0);
            var op = g[0];
            var where = top switch
            {
                "low" or "high" or "apt" or "c2" or "weekly" => $"{(op.Staff >= 0 && op.Staff < names.Count ? names[op.Staff] : CellSheetLogic.UnknownLabel)}さんの",
                "covU" or "covO" => $"{DayText.Short(state.StartDate, op.Day)}の",
                _ => "",
            };
            return new ExtraReason(g, $"入れないと{where}{label(top)}が{n}{AnalysisTriage.UnitOf(top)}増えるため", MirrorKeys.Hard.Contains(top) && !targetFams.Contains(top));
        }).ToList();
        var incidental = ViolationIdentity.Of(b0, full).Resolved.Where(k => k.Hard && !targetFams.Contains(k.Family))
            .Select(k => CellSheetLogic.ViolationKeyLabel(k, names, syms, state.StartDate, label)).ToList();
        return new FixExplanation(primary, reasons, incidental);
    }

    /// <summary>「合わせて変える勤務：金沢さん 12/31 休 → Dﾃ」と「　理由：…」の 2 行（理由が無ければ「理由を確かめられません」）。</summary>
    public static IReadOnlyList<string> ExtraReasonLines(FixExplanation e, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, string startDate) =>
        e.Extras.SelectMany(x =>
        {
            var what = string.Join("・", x.Ops.Select(op =>
            {
                var cur = op.Staff >= 0 && op.Staff < schedule.Count && op.Day >= 0 && op.Day < schedule[op.Staff].Count ? schedule[op.Staff][op.Day] : -1;
                return $"{(op.Staff >= 0 && op.Staff < staffNames.Count ? staffNames[op.Staff] : CellSheetLogic.UnknownLabel)}さん {DayText.Short(startDate, op.Day)} " +
                    $"{(cur >= 0 && cur < shiftSymbols.Count ? shiftSymbols[cur] : "—")} → {(op.ToShift >= 0 && op.ToShift < shiftSymbols.Count ? shiftSymbols[op.ToShift] : CellSheetLogic.UnknownLabel)}";
            }));
            return new[] { $"合わせて変える勤務：{what}", $"　理由：{x.Reason ?? "理由を確かめられません"}" };
        }).Concat(e.Incidental.Select(i => $"ついでに直る：{i}")).ToList();

    /// <summary>必須違反が残っているのに案が必須を変えないときの添え書き（重い要調整が減るだけなら件数が同じことも言う）。</summary>
    public static IReadOnlyList<string> HardUnchangedLines(FixSuggestion s, bool hardRemains)
    {
        if (!hardRemains || s.DeltaHard != 0) return Array.Empty<string>();
        var weighted = s.Diff.Sum(d => d.Delta * MirrorKeys.WeightOf(d.Family));
        var outList = new List<string> { "必須違反は変わりません（要調整だけが変わります）" };
        if (s.DeltaTotal == 0 && weighted < 0) outList.Add("件数は同じで、重い要調整が減ります");
        return outList;
    }

    /// <summary>下書きの確認の一文。hard＝置き換える前の必須違反（まだ数えていなければ null＝件数を言わない）。</summary>
    public static string DraftConfirmText(string? hard) =>
        hard is null ? "今の勤務表を下書きで置き換えます。元に戻せます" : $"今の勤務表（必須 {hard}）を下書きで置き換えます。元に戻せます";
    public const string DraftUndoNote = "元に戻すで前の勤務表に戻せます";
}
