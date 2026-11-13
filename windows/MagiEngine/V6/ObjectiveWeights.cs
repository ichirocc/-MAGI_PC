namespace MagiEngine.V6;

/// <summary>Kotlin <c>ObjectiveMethod</c> の移植。Weights＝今の重み（既定）、Priority＝段の並びを近似する重み（試験）。</summary>
public enum ObjectiveMethod { Weights, Priority }

/// <summary>
/// Kotlin <c>ObjectiveWeights</c> の移植: 採点に使う重み（評価器・差分評価・チェッカーの WeightedScore・修復と研磨の見積り）。方式で切り替わる。
/// 違反の色・重なりの優先（表示）は方式に関わらず <see cref="MirrorKeys.Weights"/> のまま。
/// Priority の重み（HF77）: 必須（T0）は今のまま、T1＝200、T2＝20、T3＝1、fair＝50（Android 3.727.80、2026-11-13 ユーザー指示「50」。旧 20）。
/// </summary>
public static class ObjectiveWeights
{
    private static volatile ObjectiveMethod _method = ObjectiveMethod.Weights;
    public static ObjectiveMethod Method { get => _method; set => _method = value; }

    // 宣言順＝加算順（WeightedScore の double 合計を Kotlin とビット単位で一致させる）。
    private static readonly (string Key, double Weight)[] PriorityOrdered = MirrorKeys.Weights
        .Select(kv => (kv.Key, kv.Key == "fair" ? 50.0 : TierObjective.Tiers[1].Contains(kv.Key) ? 200.0
            : TierObjective.Tiers[2].Contains(kv.Key) ? 20.0
            : TierObjective.Tiers[3].Contains(kv.Key) ? 1.0 : kv.Weight)).ToArray();
    private static readonly IReadOnlyDictionary<string, double> PriorityByKey = PriorityOrdered.ToDictionary(kv => kv.Key, kv => kv.Weight);

    public static IReadOnlyList<(string Key, double Weight)> Table(ObjectiveMethod m) => m == ObjectiveMethod.Priority ? PriorityOrdered : MirrorKeys.Weights;
    public static IReadOnlyList<(string Key, double Weight)> Table() => Table(Method);

    /// <summary>公平化／適切回数の 6% 許容を使える方式か（Kotlin <c>ObjectiveWeights.allowsAptFairTolerance</c>）。
    /// 優先順位は T3 の apt/fair のために上の段を悪くする手を採らない＝許容と両立しないので使わない。</summary>
    public static bool AllowsAptFairTolerance(ObjectiveMethod m) => m != ObjectiveMethod.Priority;
    public static bool AllowsAptFairTolerance() => AllowsAptFairTolerance(Method);

    /// <summary>族 1 つの採点の重み（<see cref="MirrorKeys.WeightOf"/> の採点版。aptLow/aptHigh は apt）。</summary>
    public static double Of(string family)
    {
        if (Method != ObjectiveMethod.Priority) return MirrorKeys.WeightOf(family);
        var f = family is "aptLow" or "aptHigh" ? "apt" : family;
        return PriorityByKey.TryGetValue(f, out var w) ? w : 0.0;
    }

    /// <summary>SOFT の族ごとの重み（long）。評価器と差分評価がホットパスで読む。</summary>
    public sealed class SoftSet
    {
        public readonly long C1, C2, C3, C3m, C3mn, C41, C42, C41s, C42s, CovO, Low, High, Apt, Fair, Weekly, CovU;

        internal SoftSet(IReadOnlyList<(string Key, double Weight)> t)
        {
            var d = t.ToDictionary(kv => kv.Key, kv => kv.Weight);
            long W(string f) => d[f] == Math.Floor(d[f]) ? (long)d[f] : throw new InvalidOperationException($"{f} の重み {d[f]} は整数でない");
            C1 = W("c1"); C2 = W("c2"); C3 = W("c3"); C3m = W("c3m"); C3mn = W("c3mn");
            C41 = W("c41"); C42 = W("c42"); C41s = W("c41s"); C42s = W("c42s");
            CovO = W("covO"); Low = W("low"); High = W("high"); Apt = W("apt"); Fair = W("fair"); Weekly = W("weekly"); CovU = W("covU");
        }
    }

    private static readonly SoftSet SoftWeights = new(MirrorKeys.Weights);
    private static readonly SoftSet SoftPriority = new(PriorityOrdered);

    public static SoftSet Soft(ObjectiveMethod m) => m == ObjectiveMethod.Priority ? SoftPriority : SoftWeights;
    public static SoftSet Soft() => Soft(Method);
}
