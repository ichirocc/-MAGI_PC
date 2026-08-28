using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.245.0 移植元] <see cref="FlexibleDayFlow.Solve"/> を固定する。
///
/// このファイルは Kotlin 原本 <c>FlexibleDayFlowTest.kt</c> のうち <c>FlexibleDayFlow.solve</c> を
/// 直接叩く1件（<c>flowAllowsChangingTheDailyShiftMultiset</c>）のみを移植したもの。残り4件
/// （rangePolishEliminatesFiveIllegalAaCellsInOnePass・infeasibleWishForTheIllegalShiftDoesNotBlockTheFix・
/// rangePolishResolvesDteViaAdjacentDayLinkedFlexibleFlow）は <c>V6HotfixPasses.ApplyRangePolish</c>
/// （手F統合）に依存するため、<c>V6HotfixPasses</c> が移植されるまで意図的に見送る。
/// </summary>
public class FlexibleDayFlowTest
{
    [Fact]
    public void FlowAllowsChangingTheDailyShiftMultiset()
    {
        long x = FlexibleDayFlow.INF;
        var staffCost = new[]
        {
            new[] { x, x, 0L }, // victimはB1のみ
            new[] { 0L, 0L, x }, // substituteは休/Aｱ
        };
        // AｱとB1の1人目を強く優遇。旧token方式では存在しないB1を生成できない。
        var marginal = new[]
        {
            new[] { 0L, 0L },
            new[] { -8000L, 1L },
            new[] { -8000L, 1L },
        };
        var r = FlexibleDayFlow.Solve(staffCost, marginal);
        Assert.True(r is not null);
        Assert.Equal(new[] { 2, 1 }, r!.Assignment);
    }
}
