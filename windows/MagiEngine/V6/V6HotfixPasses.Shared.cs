using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [V6HotfixPasses / フェーズ6, 共有低レベルヘルパ] Kotlin原本 <c>V6HotfixPasses.kt</c> 末尾
/// （ファイル全体の最後、4595〜4713行）に置かれた <c>isBetter</c>／<c>StaffObjective</c>／
/// <c>staffObjective</c>／<c>c3FamCount</c> の移植。いずれも <c>object V6HotfixPasses</c> の
/// メンバで、Kotlin原本では <c>applyC3SequencePolish</c>（1132行）と <c>applyBlockRotationPolish</c>
/// （1223行、body未移植）の両方から参照される共有ヘルパ。
///
/// [命名衝突の解消] Kotlin は型 <c>StaffObjective</c>（data class）と関数 <c>staffObjective</c>
/// （factory）を大文字/小文字だけで区別するが、C#のPascalCase規約ではどちらも <c>StaffObjective</c>
/// に潰れ、型とメンバが同一識別子を名乗ることになり不正（CS0102）。factory 側を
/// <see cref="ComputeStaffObjective"/> へ改名して衝突を解消した（Kotlin原本には対応する識別子は無い、
/// 本移植のみの命名）。
/// </summary>
public static partial class V6HotfixPasses
{
    // [3.287.0 keep-best統一] hard→weightedScore→total（単一ソース betterReport へ委譲。MirrorCore.kt 参照）。
    private static bool IsBetter(ViolationReport a, ViolationReport b) => UnifiedViolationChecker.BetterReport(a, b);

    /// <summary>個人上限(<c>Problem.RangeHi</c>)の未設定センチネル(<see cref="int.MaxValue"/>)を
    /// 「実質無制限」を表す大きな有限値へ丸める。日別の highs/lows 走査や Hungarian のコスト行列で
    /// <see cref="int.MaxValue"/> をそのまま算術に使うとオーバーフローするため。</summary>
    private static int EffectiveHi(Problem p, int i, int k)
    {
        var hi = p.RangeHi[i][k];
        return hi == int.MaxValue ? int.MaxValue / 4 : hi;
    }

    /// <summary>
    /// C3 系ブロック研磨の低コストな局所目的。公式の <see cref="UnifiedViolationChecker.BetterReport"/> と同じ
    /// HARD → weightedScore → total 順で比較する。
    ///
    /// apt/fair/weekly はここでは数えないため、この前フィルタは改善手を取りこぼし得るが、
    /// 最終採否を誤ることはない。重みは数値を複製せず <see cref="MirrorKeys"/> を単一ソースにする。
    /// </summary>
    internal sealed record StaffObjective(long Hard, double Weighted, long Total)
    {
        public static StaffObjective operator +(StaffObjective a, StaffObjective b) =>
            new(a.Hard + b.Hard, a.Weighted + b.Weighted, a.Total + b.Total);

        internal bool IsBetterThan(StaffObjective other) =>
            Hard != other.Hard ? Hard < other.Hard :
            Weighted != other.Weighted ? Weighted < other.Weighted :
            Total < other.Total;
    }

    /// <summary>
    /// ブロック交換・3者回転の**差分前フィルタ**。同 sgrp かつ同 ssk の参加者だけで使い、
    /// 「その職員たちの部分目的が改善しないなら、フル checker を呼ばずに捨てる」ための近似。
    ///
    /// **既知の近似2つ**（3.84.0 から「報告のみ」で残っていた項目）:
    ///  - c3/c3m を **窓の#fire** で数える。チェッカーは単一シフト連を <c>C3Run.rowDeficit</c>
    ///    （run-deficit）で評価するので、単一シフト連のルールではモデルが違う。
    ///  - apt/fair/weekly を集計しない（群平均・曜日バケットが要るため）。それらだけが改善する手はこぼす。
    ///
    /// [3.349.1/実測] どちらも **このデータでは一度も良い候補を落としていない**。捨てた候補すべてに
    /// フル checker を当てて「本来なら採用されたか」を数えたところ、**golden 235件・user 899件・
    /// real 896件の skip に対し採用相当は 0件**。捨てるのは checker も却下する候補ばかりで、
    /// 近似は inert。よってモデルを揃える改修はしない（測れる利得が無い＝3.290.0/3.310.1 と同じ判断）。
    /// 落としても keep-best は無関係なので**正しさには元から影響しない**（機会損失だけが論点だった）。
    /// </summary>
    private static StaffObjective ComputeStaffObjective(Problem p, int[][] sched, int i)
    {
        var total = 0L;
        var weighted = 0.0;
        var cnt = new int[p.K]; // 期間内シフト回数(c2/low/high 用)
        for (var j = 0; j < p.T; j++) { var k = sched[i][j]; if (k >= 0 && k < p.K) cnt[k]++; }
        foreach (var c in p.Cons1) // c1: d日窓で shiftIdx が day2 回未満
        {
            if (!p.CanDo(i, c.ShiftIdx)) continue;
            var j = 0;
            while (j <= p.T - c.Day1)
            {
                var z = 0;
                for (var l = 0; l < c.Day1; l++) if (sched[i][j + l] == c.ShiftIdx) z++;
                if (z < c.Day2) { total++; weighted += MirrorKeys.WeightOf("c1"); }
                j++;
            }
        }
        foreach (var c in p.Cons2) // c2
        {
            if (p.CanDo(i, c.ShiftIdx) && cnt[c.ShiftIdx] < c.Count) { total++; weighted += MirrorKeys.WeightOf("c2"); }
        }
        for (var k = 0; k < p.K; k++) // low/high: 回数レンジ(不足/超過「量」を加算)
        {
            var lo = p.RangeLo[i][k];
            var hi = p.RangeHi[i][k];
            var n = cnt[k];
            if (lo != int.MinValue && lo != 0 && p.CanDo(i, k) && n < lo)
            {
                var d = (long)(lo - n);
                total += d; weighted += d * MirrorKeys.WeightOf("low");
            }
            if (hi != int.MaxValue && n > hi)
            {
                var d = (long)(n - hi);
                total += d; weighted += d * MirrorKeys.WeightOf("high");
            }
        }
        var c3nC = C3FamCount(p, sched, i, p.Cons3n, forbidden: true); // c3n は HARD
        var c3C = C3FamCount(p, sched, i, p.Cons3, forbidden: false);
        var c3mC = C3FamCount(p, sched, i, p.Cons3m, forbidden: false);
        var c3mnC = C3FamCount(p, sched, i, p.Cons3mn, forbidden: true);
        total += c3nC + c3C + c3mC + c3mnC;
        weighted += c3nC * MirrorKeys.WeightOf("c3n") +
            c3C * MirrorKeys.WeightOf("c3") +
            c3mC * MirrorKeys.WeightOf("c3m") +
            c3mnC * MirrorKeys.WeightOf("c3mn");
        return new StaffObjective(c3nC, weighted, total);
    }

    private static long C3FamCount(Problem p, int[][] sched, int i, IReadOnlyList<C3> list, bool forbidden)
    {
        var c = 0L;
        foreach (var con in list)
        {
            var seq = con.Seq;
            var d = seq.Length;
            if (d == 0 || d > p.T) continue;
            var j = 0;
            while (j <= p.T - d)
            {
                if (sched[i][j] == seq[0])
                {
                    var z = 0;
                    for (var l = 1; l < d; l++) if (sched[i][j + l] == seq[l]) z++;
                    var fire = forbidden ? z == d - 1 : z < d - 1;
                    if (fire) c++;
                }
                j++;
            }
        }
        return c;
    }
}
