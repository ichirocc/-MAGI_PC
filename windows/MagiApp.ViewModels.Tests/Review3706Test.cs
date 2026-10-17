using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3706Test</c> の 1 対 1 移植。3.706.0: 設定タブの組む時間の候補と表記。</summary>
public class Review3706Test
{
    [Fact]
    public void PresetsAlwaysIncludeCurrentWithinRangeAscending()
    {
        foreach (var cur in new[] { 10, 45, 60, 90, 300, 5, 999 })
        {
            var p = CellSheetLogic.BudgetPresets(cur);
            Assert.Contains(Math.Clamp(cur, 10, V6FinalPort.MaxOptimizeSec), p);
            Assert.All(p, x => Assert.InRange(x, 10, V6FinalPort.MaxOptimizeSec));
            Assert.Equal(p.OrderBy(x => x).Distinct().ToList(), p);
        }
        Assert.Equal(new[] { 10, 60, 120, 180, 240, 300 }, CellSheetLogic.BudgetPresets(60));
        Assert.Equal(new[] { 10, 60, 90, 120, 180, 240, 300 }, CellSheetLogic.BudgetPresets(90));
    }

    [Fact]
    public void BudgetTextMatchesPreRunPlanLine()
    {
        foreach (var sec in new[] { 10, 60, 90, 300 })
            Assert.Contains($"組む時間 {CellSheetLogic.BudgetText(sec)}（", CellSheetLogic.PreRunPlanLine("2026-10-01", 31, 12, sec, 20));
        Assert.Equal("1分", CellSheetLogic.BudgetText(60));
        Assert.Equal("90秒", CellSheetLogic.BudgetText(90));
        Assert.Equal("組む時間 1分（＋仕上げと玉突き 20秒）", CellSheetLogic.BudgetStatusLine(60, 20));
    }
}
