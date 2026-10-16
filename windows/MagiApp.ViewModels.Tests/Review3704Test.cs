using MagiApp.ViewModels;
using MagiEngine.V6;
using Xunit;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.704.0] 配布前の確認の上段と「詳しく」・作成前の条件の行・確認の点（3 つまで）。Kotlin <c>Review3704Test</c> と 1 対 1。</summary>
public class Review3704Test
{
    private static string L(string k) => k;

    private static UiState Base()
    {
        var ui = new UiState { StartDate = "2026-10-01", Days = 31, HasResult = true, BestHard = 0, V6 = null };
        ui.Breakdown = new Dictionary<string, int> { ["c1"] = 4, ["weekly"] = 20 };
        return ui;
    }

    [Fact]
    public void PreDistTopIsHardConsultSaveAndEvaluationOnlyWhenNotEvaluated()
    {
        var notEvaluated = PreDistributionCheck.Rows(Base(), null, L);   // V6 == null は未評価
        Assert.Equal(new[] { "評価", "必須違反", "相談", "保存" }, PreDistributionCheck.Top(notEvaluated).Select(r => r.Label));
        Assert.Equal(new[] { "要調整" }, PreDistributionCheck.Details(notEvaluated).Select(r => r.Label));
    }

    [Fact]
    public void PreDistEvaluatedMovesEvaluationToDetails()
    {
        var ui = Base();
        ui.V6 = new V6PortReport(null, 0, 0, 0, 0, 0, 0, "", 0, new List<V6DayRisk>(), new List<V6StaffProfile>(), 0.0, 0.0, new List<string>(), new List<string>());
        var rows = PreDistributionCheck.Rows(ui, null, L);
        Assert.Equal(new[] { "必須違反", "相談", "保存" }, PreDistributionCheck.Top(rows).Select(r => r.Label));
        Assert.Equal(new[] { "評価", "要調整" }, PreDistributionCheck.Details(rows).Select(r => r.Label));
    }

    [Fact]
    public void PreRunConditionLineCountsEachSection()
    {
        Assert.Equal("登録条件：何度つくっても残る 3件・再作成で外れる 2件・希望が上限を超える 2件／手動固定 5件は保持", CellSheetLogic.PreRunConditionLine(3, 2, 2, 5));
        Assert.Equal("登録条件：再作成で外れる 1件", CellSheetLogic.PreRunConditionLine(0, 1, 0, 0));
        Assert.Equal("登録条件：つくる前に直す項目はありません", CellSheetLogic.PreRunConditionLine(0, 0, 0, 0));
    }

    [Fact]
    public void PreRunPrimaryUsesTheOpeningButtonWord()
    {
        Assert.Equal("勤務表をつくる", CellSheetLogic.PreRunProceedLabel(false));
        Assert.Equal("再作成", CellSheetLogic.PreRunProceedLabel(true));
    }

    [Fact]
    public void ConfirmPointsAreAtMostThree()
    {
        Assert.Equal(new[] { "a", "b" }, CellSheetLogic.ConfirmDialogPoints(new[] { "a", " ", "b" }));
        Assert.Equal(3, CellSheetLogic.ConfirmDialogPoints(new[] { "a", "b", "c" }).Count);
        Assert.Throws<ArgumentException>(() => CellSheetLogic.ConfirmDialogPoints(new[] { "a", "b", "c", "d" }));
    }
}
