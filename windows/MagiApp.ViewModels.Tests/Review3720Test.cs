namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3720Test</c> の 1 対 1 移植。3.720.0: 編集タブ② 職員管理・年間マスターの見出し・体制の行・回数のシートのボタン・問題一覧の選んだ行。
/// </summary>
public class Review3720Test
{
    private static readonly EditHeaderCounts Ready = new(Month: "2026年11月", Staff: 15, Shifts: 4, Rules: 12, Ranges: 3, ShortShifts: 1);

    [Fact]
    public void StaffHeaderIsTitleStatusAndAddStaff()
    {
        var p = CellSheetLogic.EditHeaderPlanOf(1, Ready, panelOpen: false, ranBeforeEdit: false, running: false);
        Assert.Equal("職員管理", p.Title);
        Assert.Equal(new[] { "職員 15人" }, p.Points);
        Assert.Equal(EditHeaderPrimary.AddStaff, p.Primary);
        Assert.True(CellSheetLogic.EditBarSecondary(p));
        Assert.Null(p.Notice);
        Assert.Equal(EditHeaderPrimary.Rerun, CellSheetLogic.EditHeaderPlanOf(1, Ready, false, true, false).Primary);
    }

    [Fact]
    public void MasterHeaderHasThreeOneUnitPointsAndOpenMaster()
    {
        var p = CellSheetLogic.EditHeaderPlanOf(2, Ready, panelOpen: false, ranBeforeEdit: false, running: false);
        Assert.Equal("年間マスター", p.Title);
        Assert.Equal("制度が変わったときだけ", p.Subtitle);
        Assert.Equal(new[] { "ルール 12件", "ひとりの回数の上限・下限 3件", "担当が足りないシフト 1種類" }, p.Points);
        Assert.Equal(EditHeaderPrimary.OpenMaster, p.Primary);
        Assert.Null(p.Notice);
        Assert.True(p.Title.Length <= 12);
        foreach (var pt in p.Points) Assert.True(pt.Length <= 22);
    }

    [Fact]
    public void MasterHeaderEmptyNotice()
    {
        Assert.Equal(CellSheetLogic.EditMasterEmptyNote, CellSheetLogic.EditHeaderPlanOf(2, Ready with { Shifts = 0 }, false, false, false).Notice);
        Assert.Equal(CellSheetLogic.EditMasterEmptyNote, CellSheetLogic.EditHeaderPlanOf(2, Ready with { Staff = 0 }, false, false, false).Notice);
        Assert.Null(CellSheetLogic.EditHeaderPlanOf(1, Ready with { Staff = 0 }, false, false, false).Notice);
    }

    [Fact]
    public void StaffingRowUsesPeopleOnly()
    {
        Assert.Equal("担当5人・1日最大3人・余裕2人", CellSheetLogic.StaffingRowText(5, 3));
        Assert.Equal("担当3人・1日最大3人・欠勤の余裕なし", CellSheetLogic.StaffingRowText(3, 3));
        Assert.Equal("担当2人・1日最大3人・1人足りない", CellSheetLogic.StaffingRowText(2, 3));
        Assert.True(!CellSheetLogic.StaffingRowText(5, 3).Contains('回') && !CellSheetLogic.StaffingRowText(5, 3).Contains("人日"));
    }

    [Fact]
    public void CountSheetButtonsSayKai()
    {
        Assert.Equal("上限を6回に上げて適用", CellSheetLogic.RaiseCapButton(6));
        Assert.Equal("下限を2回に下げて適用", CellSheetLogic.LowerFloorButton(2));
    }

    [Fact]
    public void ProblemListPicksTappedRowNotFirstOfCell()
    {
        var rows = new List<ProblemRow>
        {
            new("c3n", 0, new[] { 2 }, "a", true),
            new("covU", 0, new[] { 2 }, "b", true),
            new("c1", 1, new[] { 4 }, "c", false),
        };
        Assert.Equal(1, CellSheetLogic.ProblemPickedIndex(rows, 1, (0, 2)));
        Assert.Equal(0, CellSheetLogic.ProblemPickedIndex(rows, null, (0, 2)));
        Assert.Equal(2, CellSheetLogic.ProblemPickedIndex(rows, 1, (1, 4)));
        Assert.Null(CellSheetLogic.ProblemPickedIndex(rows, 1, null));
        Assert.Null(CellSheetLogic.ProblemPickedIndex(rows, null, (9, 9)));
    }
}
