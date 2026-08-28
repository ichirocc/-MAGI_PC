namespace MagiEngine.V6;

/// <summary>
/// [C1 Repair Index / 3.275.0 移植元] <see cref="C1RepairAnalysis"/> の出力を、修復オペレータが O(1)
/// で引ける読取専用の索引へまとめる（図の C1RepairIndex 層）。<b>純関数（Problem×盤面）＝スコアリング
/// 不変・副作用なし</b>。各オペレータが個別に再計算していた「窓走査／候補日の gain／ドナー余裕」を
/// 1箇所へ集約する。
///
///  - DayToWindows      : day → その日を含む不足窓（重複窓の同時解消判断に）
///  - StaffRuleWindows  : (staff,ruleIndex) → その職員・規則で不足している窓
///  - ExpectedGain      : (staff,day,targetShift) → その日を targetShift に変えたとき解消する窓不足数の
///                        最大（希望固定/禁止連続で実際に置けない候補は 0 扱い。3.279.0でシフト分離）
///  - DonorMargin       : (staff,day) → 現在そこにある c1シフトを抜いても壊れない余裕（&gt;0=安全ドナー）
///
/// 本索引は候補生成・枝刈り・診断のための情報のみを提供し、採否には一切関与しない
/// （最終採否は常に呼出側の <see cref="UnifiedViolationChecker"/> + keep-best）。
/// </summary>
public sealed class C1RepairIndexResult
{
    public IReadOnlyList<C1WindowViolation> Windows { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<C1WindowViolation>> DayToWindows { get; }
    public IReadOnlyDictionary<long, IReadOnlyList<C1WindowViolation>> StaffRuleWindows { get; }
    private readonly IReadOnlyDictionary<long, int> _gain;
    private readonly IReadOnlyDictionary<long, int> _donor;

    internal C1RepairIndexResult(
        IReadOnlyList<C1WindowViolation> windows,
        IReadOnlyDictionary<int, IReadOnlyList<C1WindowViolation>> dayToWindows,
        IReadOnlyDictionary<long, IReadOnlyList<C1WindowViolation>> staffRuleWindows,
        IReadOnlyDictionary<long, int> gain,
        IReadOnlyDictionary<long, int> donor)
    {
        Windows = windows;
        DayToWindows = dayToWindows;
        StaffRuleWindows = staffRuleWindows;
        _gain = gain;
        _donor = donor;
    }

    /// <summary>不足窓が1つでもあるか（＝c1修復オペレータが実際に動く余地があるか）。</summary>
    public bool HasActionable => Windows.Count > 0;

    /// <summary>全不足窓の不足量合計（診断用）。</summary>
    public int DeficitTotal => Windows.Sum(w => w.Deficit);

    public IReadOnlyList<C1WindowViolation> WindowsCovering(int day) =>
        DayToWindows.TryGetValue(day, out var w) ? w : Array.Empty<C1WindowViolation>();

    public IReadOnlyList<C1WindowViolation> ActiveWindows(int staff, int ruleIndex) =>
        StaffRuleWindows.TryGetValue(C1RepairIndex.Key(staff, ruleIndex), out var w)
            ? w : Array.Empty<C1WindowViolation>();

    /// <summary>
    /// その日を targetShift へ変えたとき解消する窓不足数の最大（実際に置けない候補は 0）。
    /// [3.279.0/外部レビューC1-07 移植元] 旧 API は (staff,day) のみで、同日に別シフトの不足窓が併存
    /// すると gain が混合されどのシフトへの gain か判別不能だった。targetShift をキーに含めて分離。
    /// </summary>
    public int ExpectedGain(int staff, int day, int targetShift) =>
        _gain.TryGetValue(C1RepairIndex.Key3(staff, day, targetShift), out var g) ? g : 0;

    /// <summary>そのセルの c1シフトを抜いても壊れない余裕。c1規則が依存しないセルは無限大（常に安全）。</summary>
    public int DonorMargin(int staff, int day) =>
        _donor.TryGetValue(C1RepairIndex.Key(staff, day), out var d) ? d : int.MaxValue;
}

public static class C1RepairIndex
{
    // S,T,K は実運用で高々数十。10万進法で衝突しない一意キー。
    internal static long Key(int a, int b) => (long)a * 100_000L + b;
    internal static long Key3(int a, int b, int c) => ((long)a * 100_000L + b) * 100_000L + c;

    public static C1RepairIndexResult Build(Problem p, int[][] schedule)
    {
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        var windows = C1RepairAnalysis.Analyze(p, s);

        var dayToWindows = new Dictionary<int, List<C1WindowViolation>>();
        var staffRule = new Dictionary<long, List<C1WindowViolation>>();
        foreach (var w in windows)
        {
            for (int d = w.Start; d < w.Start + w.WindowDays; d++)
            {
                if (!dayToWindows.TryGetValue(d, out var list)) dayToWindows[d] = list = new List<C1WindowViolation>();
                list.Add(w);
            }
            long srKey = Key(w.Staff, w.RuleIndex);
            if (!staffRule.TryGetValue(srKey, out var srList)) staffRule[srKey] = srList = new List<C1WindowViolation>();
            srList.Add(w);
        }

        // expectedGain: 不足窓ごとの候補日の局所 gain（opportunities と同一定義）。実際に置けない
        //   （希望固定/禁止連続）候補は除外（=そのセルの gain には寄与しない）。
        var gain = new Dictionary<long, int>();
        foreach (var w in windows)
        {
            foreach (var opp in C1RepairAnalysis.Opportunities(p, s, w))
            {
                if (opp.WishConflict || opp.PatternRisk) continue;
                // [3.279.0/C1-07] キーに対象シフトを含める（同日別シフトの gain 混合を解消）。
                long k = Key3(w.Staff, opp.Day, w.Shift);
                if (opp.Gain > gain.GetValueOrDefault(k)) gain[k] = opp.Gain;
            }
        }

        // donorMargin: 各セル(i,d)が現在保持する c1シフト x について、d を含む全窓の (z - 必要数) の最小。
        //   >0 なら x@d を抜いてもどの窓も割れない＝安全ドナー。x が c1規則を持たないセルは donor に載せない
        //   （引くと呼出側は無限大＝常に安全と解釈）。
        var donor = new Dictionary<long, int>();
        for (int i = 0; i < p.S; i++)
        {
            for (int d = 0; d < p.T; d++)
            {
                int x = s[i][d];
                int margin = int.MaxValue;
                foreach (var c in p.Cons1)
                {
                    if (c.ShiftIdx != x || c.Day1 < 1 || c.Day1 > p.T || c.Day2 < 1) continue;
                    if (!p.CanDo(i, x)) continue;
                    int lo = Math.Max(d - c.Day1 + 1, 0);
                    int hi = Math.Min(d, p.T - c.Day1);
                    for (int ws = lo; ws <= hi; ws++)
                    {
                        int z = 0;
                        for (int l = 0; l < c.Day1; l++) if (s[i][ws + l] == x) z++;
                        int slack = z - c.Day2;
                        if (slack < margin) margin = slack;
                    }
                }
                if (margin != int.MaxValue) donor[Key(i, d)] = margin;
            }
        }

        return new C1RepairIndexResult(windows, dayToWindows.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<C1WindowViolation>)kv.Value),
            staffRule.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<C1WindowViolation>)kv.Value), gain, donor);
    }
}
