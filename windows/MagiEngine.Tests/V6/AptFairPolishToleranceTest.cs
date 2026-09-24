using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.535.0/HF77明示数値指示, Kotlin原本 <c>AptFairPolishToleranceTest</c>]
/// <see cref="V6HotfixPasses.ToleratedBetter"/>（fair/apt研磨で対象家族以外のSOFT悪化を研磨開始時点比
/// +6%まで容認する）の単体検証。実盤面(MagiState)を組むと重みの掛け算を狙って作るのが難しいため、
/// <see cref="ViolationReport"/>を直接構成して比較の算術だけを固定する（判定・重みは既存の
/// <see cref="MirrorKeys"/>から読むだけで新規に増やさない）。
/// </summary>
public class AptFairPolishToleranceTest
{
    private static ViolationReport Rep(int fair, int apt, int low) => RepOf(("fair", fair), ("apt", apt), ("low", low));

    private static ViolationReport RepOf(params (string Family, int Count)[] fams)
    {
        var bd = fams.ToDictionary(f => f.Family, f => f.Count);
        var weighted = bd.Sum(kv => kv.Value * MirrorKeys.WeightOf(kv.Key));
        var hard = bd.Where(kv => MirrorKeys.Hard.Contains(kv.Key)).Sum(kv => kv.Value);
        var total = bd.Values.Sum();
        return new ViolationReport(
            Violations: new Dictionary<string, string>(), NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(), Breakdown: bd,
            Total: total, Hard: hard, Soft: total - hard, WeightedScore: weighted);
    }

    [Fact]
    public void DisabledFallsBackToPlainBetterReport()
    {
        var before = Rep(fair: 10, apt: 0, low: 1);
        var bestRep = before;
        var candidate = Rep(fair: 9, apt: 2, low: 1); // apt+2*4=+8 > fair-1*2=-2 の純悪化
        Assert.False(V6HotfixPasses.ToleratedBetter(candidate, bestRep, before, "fair", enabled: false));
        Assert.Equal(UnifiedViolationChecker.BetterReport(candidate, bestRep),
            V6HotfixPasses.ToleratedBetter(candidate, bestRep, before, "fair", enabled: false));
    }

    [Fact]
    public void WithinBudgetTradeIsAcceptedEvenThoughRawScoreWorsens()
    {
        // baseline: 対象家族(fair)以外のSOFT合計 = low(1)*120 = 120 → 予算 = 120*0.06 = 7.2
        var before = Rep(fair: 10, apt: 0, low: 1);
        var bestRep = before; // weightedScore = 20 + 0 + 120 = 140
        // candidate: fairが1改善(-2)する代わりにaptが2悪化(+8) → 生スコアは+6悪化(betterReportなら却下)
        var candidate = Rep(fair: 9, apt: 2, low: 1); // weightedScore = 18 + 8 + 120 = 146
        Assert.False(UnifiedViolationChecker.BetterReport(candidate, bestRep));
        Assert.True(V6HotfixPasses.ToleratedBetter(candidate, bestRep, before, "fair", enabled: true));
    }

    [Fact]
    public void ExceedingBudgetTradeIsStillRejected()
    {
        var before = Rep(fair: 10, apt: 0, low: 1); // baseline non-fair soft = 120, 予算 = 7.2
        var bestRep = before;
        // candidate: fairが1改善(-2)する代わりにlowが1悪化(+120) → +120 は予算7.2を遥かに超える
        var candidate = Rep(fair: 9, apt: 0, low: 2); // weightedScore = 18 + 0 + 240 = 258
        Assert.False(V6HotfixPasses.ToleratedBetter(candidate, bestRep, before, "fair", enabled: true));
    }

    [Fact]
    public void CumulativeBudgetShrinksAsItIsSpent()
    {
        var before = Rep(fair: 10, apt: 0, low: 1); // baseline non-fair soft = 120, 予算 = 7.2
        // bestRep が既に apt+1(=4)ぶん予算を使った状態からスタート（残り予算 = 7.2 - 4 = 3.2）。
        var bestRep = Rep(fair: 9, apt: 1, low: 1); // weightedScore = 18 + 4 + 120 = 142
        // 追加でapt+1(=4)悪化する手は、残り予算3.2を超える（forgiven=3.2、未容認分0.8が残る）。
        // fairは変化なしなので、この0.8を相殺する改善が無く却下される。
        var candidateNoFairGain = Rep(fair: 9, apt: 2, low: 1); // weightedScore = 18 + 8 + 120 = 146
        Assert.False(V6HotfixPasses.ToleratedBetter(candidateNoFairGain, bestRep, before, "fair", enabled: true));
    }

    // ==== [3.592.0] countパラメータ: pinBad診断分岐からの呼び出しを許容カウンタへ数えない ====

    [Fact]
    public void CountFalseSkipsTheTelemetryCounterEvenWhenAccepted()
    {
        var before = Rep(fair: 10, apt: 0, low: 1);
        var bestRep = before;
        var candidate = Rep(fair: 9, apt: 2, low: 1); // WithinBudgetTradeIsAcceptedと同じ＝容認採用される手
        TuningTelemetry.Reset();
        Assert.True(V6HotfixPasses.ToleratedBetter(candidate, bestRep, before, "fair", enabled: true, count: false));
        Assert.Equal(0, TuningTelemetry.AptFairToleranceUsedCount());
        Assert.True(V6HotfixPasses.ToleratedBetter(candidate, bestRep, before, "fair", enabled: true, count: true));
        Assert.Equal(1, TuningTelemetry.AptFairToleranceUsedCount());
    }

    // ==== [無害化, Kotlin原本 2026-09-24] 許容 ON でも重い SOFT の増加・必須どうしの付け替え・対象族の改善なしの容認は採らない ====

    [Fact]
    public void HeavySoftIncreaseIsRejectedEvenWhenTheRawScoreImproves()
    {
        // c1 が 1 増えても fair が大きく減れば素の betterReport は採るが、許容 ON では c1 の増加を 1 件も許さない。
        var before = RepOf(("fair", 60), ("c1", 0), ("weekly", 5));
        var candidate = RepOf(("fair", 30), ("c1", 1), ("weekly", 5));
        Assert.True(UnifiedViolationChecker.BetterReport(candidate, before));
        Assert.False(V6HotfixPasses.ToleratedBetter(candidate, before, before, "fair", enabled: true));
        var lowUp = RepOf(("fair", 30), ("low", 1), ("weekly", 5));
        Assert.False(V6HotfixPasses.ToleratedBetter(lowUp, before, before, "fair", enabled: true));
    }

    [Fact]
    public void HardFamilySwapIsRejectedEvenAtTheSameHardTotal()
    {
        // 必須の合計は同点(1)でも covU→c3n の付け替えは採らない（fair は改善していても）。
        var bestRep = RepOf(("covU", 1), ("c3n", 0), ("fair", 10));
        var candidate = RepOf(("covU", 0), ("c3n", 1), ("fair", 2));
        Assert.False(V6HotfixPasses.ToleratedBetter(candidate, bestRep, bestRep, "fair", enabled: true));
    }

    [Fact]
    public void ToleranceIsNotUsedWhenTheTargetFamilyDoesNotImprove()
    {
        // weekly −3(−6)・apt +2(+8) で加重 +2・件数 −1。容認(2 ≤ 予算 6)で実効 0・件数減なので旧判定なら採るが、
        //   対象の fair が減っていない＝他の族を入れ替えただけの手は採らない。
        var before = RepOf(("fair", 10), ("weekly", 50), ("apt", 0)); // 非 fair SOFT = 100 → 予算 6
        var candidate = RepOf(("fair", 10), ("weekly", 47), ("apt", 2));
        Assert.False(UnifiedViolationChecker.BetterReport(candidate, before));
        Assert.False(V6HotfixPasses.ToleratedBetter(candidate, before, before, "fair", enabled: true));
        var withFairGain = RepOf(("fair", 9), ("weekly", 47), ("apt", 2)); // fair も減るなら容認で採る
        Assert.True(V6HotfixPasses.ToleratedBetter(withFairGain, before, before, "fair", enabled: true));
    }
}
