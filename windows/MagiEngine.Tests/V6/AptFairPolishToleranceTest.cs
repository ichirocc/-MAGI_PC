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
    private static ViolationReport Rep(int fair, int apt, int low)
    {
        var bd = new Dictionary<string, int> { ["fair"] = fair, ["apt"] = apt, ["low"] = low };
        var weighted = fair * MirrorKeys.WeightOf("fair") + apt * MirrorKeys.WeightOf("apt") + low * MirrorKeys.WeightOf("low");
        return new ViolationReport(
            Violations: new Dictionary<string, string>(), NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(), Breakdown: bd,
            Total: fair + apt + low, Hard: 0, Soft: fair + apt + low, WeightedScore: weighted);
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
}
