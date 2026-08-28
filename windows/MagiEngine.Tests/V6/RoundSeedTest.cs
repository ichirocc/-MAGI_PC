using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース30] <c>RoundSeedTest.kt</c>の3件の忠実な移植。
///
/// [頭打ち調査・「なぜゼロにならないのか」] <see cref="V6HotfixPasses.RunPostOptimization"/>の
/// フィックスポイント巡回はC1Polish/C3mnPolish/RangePolish/C3RunPolishを毎ラウンド再呼出するが、
/// 旧実装はseed引数を渡さず既定値固定のままだった＝ある(staff,shift)ペアがラウンドNで頭打ちすると、
/// 盤面の当該箇所が変わらない限りラウンドN+1以降も同じrng列を再生するだけで永久に頭打ちのままだった。
/// <see cref="V6HotfixPasses.RoundSeed"/> はラウンドごとに異なるseedを与えて再挑戦のたびに違う候補順を
/// 試せるようにする（isBetterのkeep-best採否は不変・単なる探索の多様化）。
/// </summary>
public class RoundSeedTest
{
    [Fact]
    public void RoundSeedProducesDistinctValuesAcrossRounds()
    {
        var values = Enumerable.Range(0, 4)
            .Select(round => V6HotfixPasses.RoundSeed(baseSeed: 1L, tag: 0x8A9EL, round: round))
            .ToList();
        Assert.Equal(values.Distinct().Count(), values.Count); // 4ラウンド分がすべて異なること
    }

    [Fact]
    public void RoundSeedIsDeterministic()
    {
        var a = V6HotfixPasses.RoundSeed(baseSeed: 42L, tag: 0xC3AL, round: 2);
        var b = V6HotfixPasses.RoundSeed(baseSeed: 42L, tag: 0xC3AL, round: 2);
        Assert.Equal(a, b); // 同じ引数なら同じ値(再現性)
    }

    [Fact]
    public void RoundSeedDiffersAcrossDistinctTagsForSameRound()
    {
        // C1/C3mn/Range/C3Runの各パスは同一round・異なるtag定数でも互いに衝突しないこと。
        var tags = new long[] { 0x1C1L, 0xC3AL, 0x8A9EL, 0xC3A2L };
        var values = tags.Select(tag => V6HotfixPasses.RoundSeed(baseSeed: 7L, tag: tag, round: 1)).ToList();
        Assert.True(values.Distinct().Count() == values.Count, "異なるtagは異なるseedを生むこと");
    }
}
