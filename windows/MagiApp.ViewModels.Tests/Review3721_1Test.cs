namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3721_1Test</c> の 1 対 1 移植。3.721.1: 問題一覧の選んだ行は一覧の作り直しで行番号がずれても同じ族に留まる。
/// </summary>
public class Review3721_1Test
{
    private static readonly List<ProblemRow> Before = new()
    {
        new("c3n", 0, new[] { 2 }, "a", true),
        new("covU", 0, new[] { 2 }, "b", true),
    };

    [Fact]
    public void RebuiltListShiftingIndexStaysOnSameFamily()
    {
        var after = new List<ProblemRow>
        {
            new("covU", 0, new[] { 2 }, "b", true),
            new("c3n", 0, new[] { 2 }, "a", true),
        };
        Assert.Equal(1, CellSheetLogic.ProblemPickedIndex(Before, 1, (0, 2), "covU"));
        Assert.Equal(0, CellSheetLogic.ProblemPickedIndex(after, 1, (0, 2), "covU"));
    }

    [Fact]
    public void FamilyGoneFallsBackToFirstRowOfCell()
    {
        var after = new List<ProblemRow>
        {
            new("c1", 1, new[] { 4 }, "c", false),
            new("c3n", 0, new[] { 2 }, "a", true),
        };
        Assert.Equal(1, CellSheetLogic.ProblemPickedIndex(after, 1, (0, 2), "covU"));
        Assert.Equal(1, CellSheetLogic.ProblemPickedIndex(after, 0, (0, 2), "covU"));
    }

    [Fact]
    public void NullFamilyKeepsOldBehavior()
    {
        Assert.Equal(1, CellSheetLogic.ProblemPickedIndex(Before, 1, (0, 2), null));
        Assert.Equal(0, CellSheetLogic.ProblemPickedIndex(Before, null, (0, 2), null));
    }
}
