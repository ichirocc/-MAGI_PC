using System.Numerics;

namespace MagiEngine.V6;

/// <summary>
/// C1（D日間に対象シフトをN回以上）の月内配置を、対象シフトの月間回数を変えずに
/// 時系列DPで再設計する。
///
/// 既存の1回swap（2-opt）では、2箇所以上を同時に動かさない限り途中解が改善しない
/// 局所最適を越えられない。本DPは対象シフトか否かの二値列を月全体で最適化し、
/// 最大<see cref="Solve"/>のmaxRelocations回の「非対象→対象」と同数の「対象→非対象」を一括生成する。
///
/// - 全C1規則のうち同じ対象シフトの規則を同時評価する。
/// - 希望固定日は現在の対象/非対象状態を固定する。
/// - 対象シフトの月間回数を厳密保存するため、staffRange/apt/c2の対象回数は不変。
/// - このクラスは二値配置だけを解く。退避した非対象シフトtokenの再割当と全制約採否は
///   V6HotfixPasses側が担当する。
///
/// 月31日・最大窓15日・最大4移設の実データ規模では、状態数は通常数万以下。
/// </summary>
internal static class C1TemporalDp
{
    public readonly record struct Rule(int Days, int Minimum);

    public sealed record Candidate(
        /// <summary>TargetDays[j] == true なら日jを対象シフトにする。</summary>
        bool[] TargetDays,
        /// <summary>この対象シフトに関する全C1規則の違反窓総数。</summary>
        int Fires,
        /// <summary>変更セル数。回数保存なので常に relocations * 2。</summary>
        int ChangedCells,
        /// <summary>非対象→対象へ移した日数。</summary>
        int Relocations);

    private readonly record struct Record(long Cost, long Bits, int Fires, int Changed);

    private const long FIRE_COST = 1_000_000L;
    private const long CHANGE_COST = 1_000L;

    /// <summary>
    /// [3.310.0] 1日ぶんの DP が保持してよい状態数の上限。超えたら解を返さず諦める（<c>return null</c>）。
    ///
    /// DP は密配列でなく <see cref="Dictionary{TKey,TValue}"/> の疎表現なので「窓長20なら必ず 2^19 面を
    /// 確保する」わけではない。ただし到達可能な状態数は「窓内の対象日の 2^n × 追加(&lt;=maxRelocations) の
    /// 組合せ × count × reloc」で決まり、<b>窓長ガードだけでは縛れない</b>（出現回数の多いシフト × 長い窓で
    /// 数百万に達しうる）。ここで状態数そのものに上限を置けば、メモリと時間の両方が同時に有界になる。
    /// <c>null</c> はこの関数の正規の出口（t&gt;63 / 有効ルール無し / 既に違反0 など）で、呼出側の
    /// <c>C1TemporalFlowPolish</c> は提案が無いものとして扱うだけ＝keep-best は不変・退化しない。
    /// 3.305.0 で <c>C1JointLnsPolish</c> の密DPへ入れた <c>MAX_EXACT_LOWER_BOUND_CELLS</c> と同じ考え方。
    /// </summary>
    private const int MAX_DP_STATES = 200_000;

    private const int COUNT_BITS = 6;
    private const int RELOC_BITS = 6;
    private const int LOW_BITS = COUNT_BITS + RELOC_BITS;

    /// <param name="row">現在の1職員分シフト列。</param>
    /// <param name="targetShift">C1対象シフトindex。</param>
    /// <param name="rules">targetShiftに対するC1規則群。</param>
    /// <param name="locked">trueの日は対象/非対象の現在状態を変えない。</param>
    /// <param name="maxRelocations">一度に移設する最大対象シフト数。</param>
    /// <param name="seed">同じC1違反数・変更数の別配置を得る決定的tie-break seed。</param>
    /// <param name="maxExactWindow">ビットマスクDPで厳密に扱う最大窓長。</param>
    /// <param name="maxDpStates">1日ぶんのDPが保持してよい状態数の上限（<see cref="MAX_DP_STATES"/>）。</param>
    public static Candidate? Solve(
        int[] row,
        int targetShift,
        IReadOnlyList<Rule> rules,
        bool[] locked,
        int maxRelocations = 4,
        long seed = 0L,
        int maxExactWindow = 20,
        int maxDpStates = MAX_DP_STATES)
    {
        int t = row.Length;
        // [3.213.0のSCORE_HARD_UNIT検証と同型] RELOC_BITS(6bit)を超えるmaxRelocationsはKey()のビット
        //   詰め込みでcountフィールドへ溢れ、異なる(relocations,count)状態がDP重複排除で誤って同一視され
        //   状態が黙って潰れうる（現在の全4呼出元は4/6で範囲内=到達不能だが、silent corruption を
        //   明示的な no-op（null返却=このパスがない場合の安全側フォールバック）に変える）。
        if (t == 0 || t > 63 || locked.Length != t || maxRelocations <= 0 ||
            maxRelocations > (1 << RELOC_BITS) - 1) return null;
        var validRules = rules.Where(r => r.Days >= 1 && r.Days <= t && r.Minimum > 0).ToList();
        if (validRules.Count == 0) return null;
        int maxWindow = validRules.Max(r => r.Days);
        if (maxWindow > maxExactWindow || maxWindow >= 63) return null;

        int originalCount = row.Count(v => v == targetShift);
        if (originalCount == 0 || originalCount == t) return null;
        int currentFires = CountFires(row, targetShift, validRules);
        if (currentFires == 0) return null;

        int keepBits = Math.Max(maxWindow - 1, 0);
        long keepMask = keepBits == 0 ? 0L : (1L << keepBits) - 1L;

        long Key(long mask, int count, int relocations) =>
            (mask << LOW_BITS) | ((long)count << RELOC_BITS) | (long)relocations;

        long Tie(int day, bool changed)
        {
            if (!changed) return 0L;
            long z = seed ^ ((long)day * -0x61c8864680b583ebL);
            z ^= z >>> 33;
            z *= -0x00ae502812aa7333L;
            z ^= z >>> 29;
            return z & 511L;
        }

        var dp = new Dictionary<long, Record> { [Key(0L, 0, 0)] = new Record(0L, 0L, 0, 0) };

        for (int day = 0; day < t; day++)
        {
            var next = new Dictionary<long, Record>(Math.Max(16, dp.Count * 2));
            bool oldBit = row[day] == targetShift;
            foreach (var (packed, rec) in dp)
            {
                int reloc = (int)(packed & ((1L << RELOC_BITS) - 1L));
                int count = (int)((packed >>> RELOC_BITS) & ((1L << COUNT_BITS) - 1L));
                long mask = packed >>> LOW_BITS;
                int[] choices = locked[day] ? (oldBit ? new[] { 1 } : new[] { 0 }) : new[] { 0, 1 };
                foreach (int bit in choices)
                {
                    int newCount = count + bit;
                    if (newCount > originalCount) continue;
                    bool added = bit == 1 && !oldBit;
                    int newReloc = reloc + (added ? 1 : 0);
                    if (newReloc > maxRelocations) continue;

                    long full = (mask << 1) | (long)bit;
                    int fireInc = 0;
                    foreach (var rule in validRules)
                    {
                        if (day + 1 < rule.Days) continue;
                        long rm = (1L << rule.Days) - 1L;
                        if (BitOperations.PopCount((ulong)(full & rm)) < rule.Minimum) fireInc++;
                    }
                    bool changed = (bit == 1) != oldBit;
                    int changedCount = rec.Changed + (changed ? 1 : 0);
                    int fires = rec.Fires + fireInc;
                    long cost = rec.Cost + (long)fireInc * FIRE_COST +
                        (changed ? CHANGE_COST : 0L) + Tie(day, changed);
                    long newMask = full & keepMask;
                    long nk = Key(newMask, newCount, newReloc);
                    long bits = bit == 1 ? rec.Bits | (1L << day) : rec.Bits;
                    if (!next.TryGetValue(nk, out var old) || cost < old.Cost ||
                        (cost == old.Cost && (ulong)bits < (ulong)old.Bits))
                    {
                        next[nk] = new Record(cost, bits, fires, changedCount);
                    }
                }
            }
            if (next.Count == 0) return null;
            // [3.310.0] 状態爆発の安全弁。密DPでない＝窓長だけでは状態数を縛れないため、
            //   実際の到達状態数で打ち切る。諦めても後段は keep-best のまま（退化しない）。
            if (next.Count > maxDpStates) return null;
            dp = next;
        }

        Record? best = null;
        int bestRelocations = 0;
        foreach (var (packed, rec) in dp)
        {
            int reloc = (int)(packed & ((1L << RELOC_BITS) - 1L));
            int count = (int)((packed >>> RELOC_BITS) & ((1L << COUNT_BITS) - 1L));
            if (count != originalCount || reloc <= 0 || rec.Fires >= currentFires) continue;
            if (best is null || rec.Cost < best.Value.Cost ||
                (rec.Cost == best.Value.Cost && (ulong)rec.Bits < (ulong)best.Value.Bits))
            {
                best = rec;
                bestRelocations = reloc;
            }
        }
        if (best is null) return null;
        var chosen = best.Value;
        var targetDays = new bool[t];
        for (int day = 0; day < t; day++) targetDays[day] = ((chosen.Bits >>> day) & 1L) != 0L;
        return new Candidate(targetDays, chosen.Fires, chosen.Changed, bestRelocations);
    }

    public static int CountFires(int[] row, int targetShift, IReadOnlyList<Rule> rules)
    {
        int fires = 0;
        foreach (var rule in rules)
        {
            int d = rule.Days;
            if (d < 1 || d > row.Length || rule.Minimum <= 0) continue;
            int count = 0;
            for (int j = 0; j < d; j++) if (row[j] == targetShift) count++;
            if (count < rule.Minimum) fires++;
            int start = 1;
            while (start <= row.Length - d)
            {
                if (row[start - 1] == targetShift) count--;
                if (row[start + d - 1] == targetShift) count++;
                if (count < rule.Minimum) fires++;
                start++;
            }
        }
        return fires;
    }
}
