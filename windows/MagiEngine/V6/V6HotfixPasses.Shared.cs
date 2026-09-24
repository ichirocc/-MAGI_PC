using System.Linq;
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

    /// <summary>[3.535.0/HF77明示数値指示, Kotlin原本 <c>AptFairPolish.SOFT_TOLERANCE_FRACTION</c>]
    /// 研磨開始時点の対象家族(apt/fair)以外のSOFT合計の6%を上限に、その悪化を容認する累積予算。
    /// 既定OFF（<see cref="PolishGate.AptFairSoftTolerance"/>）。</summary>
    internal const double SoftToleranceFraction = 0.06;

    /// <summary>[無害化, Kotlin原本 <c>AptFairPolish.TOLERANCE_BLOCKED_FAMILIES</c>（2026-09-24 ユーザー指定の集合）]
    /// 許容 ON でも 1 件でも増えたら採らない重い SOFT（apt/fair/weekly/c2/c3/c3m は軽い側として許容しうる）。</summary>
    internal static readonly IReadOnlySet<string> ToleranceBlockedFamilies =
        new HashSet<string> { "c1", "low", "high", "covO", "c3mn", "c41", "c42", "c41s", "c42s" };

    /// <summary>internal＝<c>AptFairPolishToleranceTest</c> から直接検証するため（Kotlin原本と同じ可視性）。</summary>
    internal static double NonFamilySoftTotal(ViolationReport rep, string excludeFamily) =>
        MirrorKeys.Soft.Where(f => f != excludeFamily).Sum(f => rep.Breakdown.GetValueOrDefault(f, 0) * MirrorKeys.WeightOf(f));

    /// <summary>
    /// [3.535.0, Kotlin原本 <c>AptFairPolish.toleratedBetter</c>] OFF時はbetterReport(keep-best)のまま。
    /// ON時はHARD不増加は変えず、対象家族以外のSOFT悪化分を研磨開始時点(before)比+6%の累積予算まで
    /// 差し引いて weightedScore を比較する。
    /// </summary>
    internal static bool ToleratedBetter(ViolationReport rep, ViolationReport bestRep, ViolationReport before, string family, bool enabled, bool count = true)
    {
        if (!enabled) return IsBetter(rep, bestRep);
        // [無害化, Kotlin原本 2026-09-24] 許容 ON の敗因（重い SOFT の増加・必須どうしの付け替え）を切る。
        //   ①どの必須族も best より増やさない（合計が同点でも covU→c3n の付け替えを拒む＝1手の提案ゲートと同型）。
        if (UnifiedViolationChecker.NewHardFamilyViolation(bestRep, rep) is not null) return false;
        if (rep.Hard != bestRep.Hard) return rep.Hard < bestRep.Hard;
        //   ②重い SOFT が 1 件でも増える手は許容の有無にかかわらず採らない。
        if (ToleranceBlockedFamilies.Any(f => rep.Breakdown.GetValueOrDefault(f, 0) > bestRep.Breakdown.GetValueOrDefault(f, 0))) return false;
        var baseline = NonFamilySoftTotal(before, family);
        var budget = baseline * SoftToleranceFraction;
        var usedByBest = Math.Max(NonFamilySoftTotal(bestRep, family) - baseline, 0.0);
        var remaining = Math.Max(budget - usedByBest, 0.0);
        var increase = Math.Max(NonFamilySoftTotal(rep, family) - NonFamilySoftTotal(bestRep, family), 0.0);
        var forgiven = Math.Min(increase, remaining);
        var rawDelta = rep.WeightedScore - bestRep.WeightedScore;
        var effectiveDelta = rawDelta - forgiven;
        //   ③容認を使うなら、研磨対象の族（apt/fair）の件数が best より減っていること。
        var targetImproved = rep.Breakdown.GetValueOrDefault(family, 0) < bestRep.Breakdown.GetValueOrDefault(family, 0);
        var accepted = (effectiveDelta < 0.0 || (effectiveDelta == 0.0 && rep.Total < bestRep.Total)) && (forgiven <= 0.0 || targetImproved);
        // [3.535.0/実機ログで発覚, Kotlin原本] 素のbetterReport（＝rawDeltaだけで同じ判定）なら却下されるはずの
        //   手を、容認で採用に転じさせた回数だけを数える。
        // [3.592.0, Kotlin原本] countはpinBad診断分岐からの呼び出し(実際には不採用)を数えないためのガード。
        if (count && accepted && forgiven > 0.0)
        {
            var rawAccepted = rawDelta < 0.0 || (rawDelta == 0.0 && rep.Total < bestRep.Total);
            if (!rawAccepted) TuningTelemetry.IncrementAptFairToleranceUsed();
        }
        return accepted;
    }

    /// <summary>
    /// [3.580.0/backlog#26, Kotlin原本 <c>targetFamiliesRemain</c>] 既定OFFの専用修復腕を再活性化フラグ
    /// 経由で呼ぶかどうかの判定材料。腕自身の自己申告カウンタ（TuningTelemetryの適用数など）でなく、
    /// 正式チェッカーの breakdown 生値で「対象違反が今まだ残っているか」を見る。各 xxxReactivate フラグが
    /// OFF なら一切呼ばれない＝挙動不変。
    /// </summary>
    internal static bool TargetFamiliesRemain(MagiState state, int[][] work, bool quantitativeRangeEval, params string[] families)
    {
        var rep = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        return families.Any(f => rep.Breakdown.GetValueOrDefault(f, 0) > 0);
    }

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
