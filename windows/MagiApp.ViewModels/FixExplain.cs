using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>付随の変更 1 つと、その理由（null＝外しても重い族が増えない＝理由を言えない）。OutOfFrameHard＝範囲の外の必須違反を直すための変更。Kotlin <c>ExtraReason</c>。
/// Incidental＝主の変更が生んだ違反の埋め合わせでなく、前からあった別の違反を直すだけの変更（ついでに直る＝必要な変更ではない）。</summary>
public sealed record ExtraReason(IReadOnlyList<FixCell> Ops, string? Reason, bool OutOfFrameHard = false, bool Incidental = false);

/// <summary>案の説明: 入り口の範囲の中の主の変更、付随の変更と理由、ついでに直る範囲の外の必須違反。Kotlin <c>FixExplanation</c>。</summary>
public sealed record FixExplanation(IReadOnlyList<FixCell> Primary, IReadOnlyList<ExtraReason> Extras, IReadOnlyList<string> Incidental)
{
    /// <summary>付随の変更がすべて主の変更の埋め合わせ（<see cref="ExtraReason.Incidental"/> は必要な変更に数えない）。</summary>
    public bool AllExplained => Extras.All(x => x.Reason is not null && !x.Incidental);
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

    /// <summary>違反の鍵が入り口の範囲の中か（個人の回数＝その職員、日の人員＝その日／まとめて見る日々）。</summary>
    public static bool KeyInFrame(FixTarget t, ViolationKey k)
    {
        int? staff = k.Space == ViolationKey.Kind.Need ? null : k.A;
        int? day = k.Space == ViolationKey.Kind.Count ? null : k.B;
        if (t.Staff is not null) return staff == t.Staff;
        if (t.Multi) return day is int d && t.Days.Contains(d);
        return day is int d2 && d2 == t.Day;
    }

    /// <summary>
    /// 付随の変更を 1 つずつ外して正式な検査で数え直す（3.687.0/R03: 場所と因果で判定）。初め・主の変更だけ・全部の 3 つを数え、
    /// 外すと戻る違反の鍵（族＋職員/日/枠）が「主の変更だけ」で初めて生まれた鍵なら必要な埋め合わせ、前からあった別の違反なら
    /// ついでに直る（必要な変更ではない）。範囲の外の必須違反かは族の名前でなく鍵の場所で決める。外したときの検査は 1 案あたり 3 回まで
    /// （付随の変更が 4 つ以上なら 3 つ目以降をまとめて外す）。
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
        var primaryOnly = UnifiedViolationChecker.Check(state, Apply(primary));
        var baseKeys = ViolationIdentity.Keys(b0);
        var introduced = ViolationIdentity.Keys(primaryOnly).Where(k => !baseKeys.Contains(k)).ToHashSet();
        var fullKeys = ViolationIdentity.Keys(full);
        var groups = extras.Count <= 3 ? extras.Select(x => (IReadOnlyList<FixCell>)new[] { x }).ToList()
            : extras.Take(2).Select(x => (IReadOnlyList<FixCell>)new[] { x }).Append(extras.Skip(2).ToList()).ToList();
        var incidentalNotes = new List<string>();
        double W(ViolationKey k) => MirrorKeys.WeightOf(k.Family);
        var reasons = groups.Select(g =>
        {
            var without = UnifiedViolationChecker.Check(state, Apply(sug.Ops.Where(o => !g.Contains(o))));
            var back = ViolationIdentity.Keys(without).Where(k => !fullKeys.Contains(k)).ToList();
            var comp = back.Where(introduced.Contains).ToList();
            if (comp.Count > 0)
            {
                var top = comp.MaxBy(W)!;
                var cnt = comp.Count(k => k.Family == top.Family);
                return new ExtraReason(g, $"入れないと{CellSheetLogic.ViolationKeyPlace(top, names, syms, state.StartDate)}の{label(top.Family)}が" +
                    $"{cnt}{AnalysisTriage.UnitOf(top.Family)}戻るため（主の変更で生まれる分の埋め合わせ）");
            }
            if (back.Count > 0)
            {
                // 前からあった違反を直すだけ＝必要な変更ではない。範囲の外の必須違反なら「範囲の外を直すための変更」。
                foreach (var k in back.Where(k => k.Hard)) incidentalNotes.Add(CellSheetLogic.ViolationKeyLabel(k, names, syms, state.StartDate, label));
                var top = back.MaxBy(W)!;
                return new ExtraReason(g, $"入れると{CellSheetLogic.ViolationKeyLabel(top, names, syms, state.StartDate, label)}もついでに直るため（なくても対象は直ります）",
                    OutOfFrameHard: back.Any(k => k.Hard && !KeyInFrame(target, k)), Incidental: true);
            }
            // 鍵が増えない（同じ場所で量だけ増える回数の族など）＝族の量で見る。主の変更で増えた族なら埋め合わせ。
            var pos = WeightedDelta(full, without).Where(kv => kv.Value > 0).ToList();
            if (pos.Count == 0) return new ExtraReason(g, null);
            var fam = pos.MaxBy(kv => kv.Value).Key;
            var n = without.Breakdown.GetValueOrDefault(fam, 0) - full.Breakdown.GetValueOrDefault(fam, 0);
            var grewByPrimary = primaryOnly.Breakdown.GetValueOrDefault(fam, 0) > b0.Breakdown.GetValueOrDefault(fam, 0);
            var days = g.Select(o => o.Day).Distinct().ToList();
            var where = fam switch
            {
                "low" or "high" or "apt" or "c2" or "weekly" => ExtraStaffWhere(g, names),
                "covU" or "covO" => string.Join("・", days.Select(d => DayText.Short(state.StartDate, d))) + "の" + (days.Count > 1 ? "合計 " : ""),
                _ => "",
            };
            return new ExtraReason(g, $"入れないと{where}{label(fam)}が{n}{AnalysisTriage.UnitOf(fam)}増えるため", Incidental: !grewByPrimary);
        }).ToList();
        var incidental = ViolationIdentity.Of(b0, full).Resolved.Where(k => k.Hard && !KeyInFrame(target, k))
            .Select(k => CellSheetLogic.ViolationKeyLabel(k, names, syms, state.StartDate, label)).Concat(incidentalNotes).Distinct().ToList();
        return new FixExplanation(primary, reasons, incidental);
    }

    /// <summary>[3.687.0/R04] 付随の変更（まとめた組を含む）の職員の言い方。1 人ならその人、複数なら「A さん・B さんの合計」（先頭の人に合計を寄せない）。</summary>
    public static string ExtraStaffWhere(IReadOnlyList<FixCell> g, IReadOnlyList<string> names)
    {
        var who = g.Select(o => o.Staff).Distinct().Select(i => $"{(i >= 0 && i < names.Count ? names[i] : CellSheetLogic.UnknownLabel)}さん").ToList();
        return who.Count == 1 ? $"{who[0]}の" : string.Join("・", who) + "の合計 ";
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
    public const string DraftUndoNote = "「元に戻す」で前の勤務表に戻せます";
}
