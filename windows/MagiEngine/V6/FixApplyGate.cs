using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [Android 3.509.4 移植] 改善提案（<see cref="FixSuggestion.Ops"/>）を本盤面へ反映する直前の安全ゲート。
/// 仮盤面へ適用して正式チェッカーで再評価し、辞書式（hard→weightedScore→total）で改善し厳密ピンを崩さないときだけ
/// 適用後の盤面を返す。入力の盤面は変えない。
/// </summary>
public static class FixApplyGate
{
    public sealed record Outcome(int[][]? Schedule, ViolationReport Before, ViolationReport? After, string? Reason)
    {
        public bool Applied => Schedule is not null;
    }

    public static Outcome Apply(MagiState state, int[][] schedule, IReadOnlyList<FixCell> ops)
    {
        var p = new Problem(state);
        var before = UnifiedViolationChecker.Check(state, schedule);
        if (ops.Count == 0) return new Outcome(null, before, null, "変更がありません");
        var work = schedule.Copy2D();
        foreach (var op in ops)
        {
            if (op.Staff < 0 || op.Staff >= work.Length || op.Day < 0 || op.Day >= work[op.Staff].Length || op.ToShift < 0 || op.ToShift >= p.K)
                return new Outcome(null, before, null, "提案の範囲が今の勤務表と合いません");
            if (p.WishLocked(op.Staff, op.Day) && p.Wish[op.Staff][op.Day] != op.ToShift)
                return new Outcome(null, before, null, "希望で固定されたセルを変える提案です");
            work[op.Staff][op.Day] = op.ToShift;
        }
        var after = UnifiedViolationChecker.Check(state, work);
        if (!UnifiedViolationChecker.BetterReport(after, before)) return new Outcome(null, before, after, "今の勤務表では改善になりません");
        if (V6SearchOperators.ExactPinRegression(p, schedule, work)) return new Outcome(null, before, after, "回数固定（下限＝上限）を崩す提案です");
        return new Outcome(work, before, after, null);
    }
}
