using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>RSI++ のフェーズ按分（M-04）。41 秒以上は合計＝予算で、予算の締切を足す経路に入らない＝従来どおり。</summary>
public class RsiPlusPhasePlanTest
{
    [Fact]
    public void PlanSumsToBudgetFrom41Seconds()
    {
        for (var b = 41; b <= 600; b++) Assert.True(b == V6NativeOptimizer.RsiPlusPhaseSecs(b).Sum(), $"budget={b}");
    }

    [Fact]
    public void FloorsExceedBudgetBelow35Seconds()
    {
        for (var b = 1; b <= 34; b++) Assert.True(V6NativeOptimizer.RsiPlusPhaseSecs(b).Sum() > b, $"budget={b}");
        for (var b = 35; b <= 40; b++) Assert.True(V6NativeOptimizer.RsiPlusPhaseSecs(b).Sum() >= b, $"budget={b}");
    }

    [Fact]
    public void DefaultBudgetsKeepTheirSplit()
    {
        Assert.Equal(new[] { 60, 105, 90, 45 }, V6NativeOptimizer.RsiPlusPhaseSecs(300));
        Assert.Equal(new[] { 10, 10, 10, 5 }, V6NativeOptimizer.RsiPlusPhaseSecs(1));
    }
}
