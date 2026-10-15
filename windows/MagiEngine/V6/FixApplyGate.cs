using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [Android 3.509.4 移植] 改善提案（<see cref="FixSuggestion.Ops"/>）を本盤面へ反映する直前の安全ゲート。
/// 仮盤面へ適用して正式チェッカーで再評価し、辞書式（hard→weightedScore→total）で改善し厳密ピンを崩さず必須違反を新たに生まない
/// （<see cref="ViolationIdentity.IntroducesHardViolation"/>）ときだけ適用後の盤面を返す。入力の盤面は変えない。
/// </summary>
public static class FixApplyGate
{
    public sealed record Outcome(int[][]? Schedule, ViolationReport Before, ViolationReport? After, string? Reason)
    {
        public bool Applied => Schedule is not null;
    }

    public static Outcome Apply(MagiState state, int[][] schedule, IReadOnlyList<FixCell> ops) =>
        Apply(state, schedule, ops, new Problem(state), UnifiedViolationChecker.Check(state, schedule));

    /// <summary>同じ盤面の候補を続けて評価する呼び手向け＝<paramref name="p"/> と <paramref name="before"/> は state/schedule から作ったものを渡す（結果は上と同じ）。</summary>
    public static Outcome Apply(MagiState state, int[][] schedule, IReadOnlyList<FixCell> ops, Problem p, ViolationReport before)
    {
        if (ops.Count == 0) return new Outcome(null, before, null, "変更がありません");
        var work = schedule.Copy2D();
        foreach (var op in ops)
        {
            if (op.Staff < 0 || op.Staff >= work.Length || op.Day < 0 || op.Day >= work[op.Staff].Length || op.ToShift < 0 || op.ToShift >= p.K)
                return new Outcome(null, before, null, "提案の範囲が今の勤務表と合いません");
            if (p.Pinned(op.Staff, op.Day) && p.Pin[op.Staff][op.Day] != op.ToShift)
                return new Outcome(null, before, null, "手動固定のセルを変える提案です");
            if (p.WishLocked(op.Staff, op.Day) && p.LockTo(op.Staff, op.Day) != op.ToShift)
                return new Outcome(null, before, null, "希望で固定されたセルを変える提案です");
            if (op.ToShift != schedule[op.Staff][op.Day] && p.ExtBanned(op.Staff, op.Day, op.ToShift))
                return new Outcome(null, before, null, "拡張希望で禁止された勤務を置く提案です");
            work[op.Staff][op.Day] = op.ToShift;
        }
        var after = UnifiedViolationChecker.Check(state, work);
        if (!UnifiedViolationChecker.BetterReport(after, before)) return new Outcome(null, before, after, "今の勤務表では改善になりません");
        if (V6SearchOperators.ExactPinRegression(p, schedule, work)) return new Outcome(null, before, after, "回数固定（下限＝上限）を崩す提案です");
        var newHard = UnifiedViolationChecker.NewHardFamilyViolation(before, after);
        if (newHard != null) return new Outcome(null, before, after, $"別の必須条件（{FamLabel(newHard)}）が新たに崩れる提案です");
        // 件数が減っても、別の職員・日・枠に必須違反が新たに生まれる手は通さない（違反の同一性、3.663.0）。
        var born = ViolationIdentity.NewHard(before, after);
        if (born.Count > 0)
            return new Outcome(null, before, after, $"別の職員・日・枠に必須違反（{string.Join("・", born.Select(k => FamLabel(k.Family)).Distinct())}）が新たに {born.Count} 件生まれる提案です");
        return new Outcome(work, before, after, null);
    }

    /// <summary>拒否理由の族名（画面に英字の記号を出さない）。</summary>
    private static string FamLabel(string fam) => fam switch
    {
        "covU" => "人員不足", "c3n" => "禁止の並び", "c3w" => "希望の前日の禁止", "pref" => "希望と違う勤務",
        "extWish" => "拡張希望の違反", "groupViol" => "担当外シフト", _ => fam,
    };
}
