using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3706Test</c> の 1 対 1 移植。3.706.0: 設定タブの組む時間の候補と表記。</summary>
public class Review3706Test
{
    /// <summary>3.727.88: タイルは 3・5・7分、ユーザー指定は 2〜10分を 1 分ずつ。既定は 5分。</summary>
    [Fact]
    public void presetsAndCustomRange()
    {
        Assert.Equal(new[] { 180, 300, 420 }, CellSheetLogic.BudgetPresetSecs);
        Assert.All(CellSheetLogic.BudgetPresetSecs, x => Assert.InRange(x, MagiViewModel.MinBudgetSec, MagiViewModel.MaxBudgetSec));
        Assert.Equal((120, 600), (MagiViewModel.MinBudgetSec, MagiViewModel.MaxBudgetSec));
        Assert.Equal(240, CellSheetLogic.BudgetCustomStep(180, 1));
        Assert.Equal(120, CellSheetLogic.BudgetCustomStep(180, -5));
        Assert.Equal(600, CellSheetLogic.BudgetCustomStep(540, 3));
        Assert.Equal(120, CellSheetLogic.BudgetCustomStep(60, 0));
        Assert.Equal(180, CellSheetLogic.BudgetCustomStep(150, 0));
        Assert.Equal(300, new UiState().BudgetSec);
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
