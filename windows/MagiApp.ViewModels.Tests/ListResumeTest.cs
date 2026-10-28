using MagiApp.ViewModels;
using Xunit;

namespace MagiApp.ViewModels.Tests;

/// <summary>問題一覧へ戻ったときの行（Android 3.727.5、Kotlin <c>ListResumeTest</c>）。見ていた行が残ればそれ、直して消えれば繰り上がった次の問題。</summary>
public class ListResumeTest
{
    private static ProblemRow Row(int staff, int day, string fam = "c3n") => new(fam, staff, new[] { day }, $"r{staff}-{day}", true);

    [Fact]
    public void KeepsTheRowStillPresent()
    {
        var rows = new[] { Row(0, 1), Row(0, 2), Row(1, 4) };
        Assert.Equal(1, CellSheetLogic.ListResumeIndex(rows, 1, (0, 2), "c3n"));
    }

    [Fact]
    public void FixedRowMovesToTheNextProblem()
    {
        var after = new[] { Row(0, 1), Row(1, 4) };
        Assert.Equal(1, CellSheetLogic.ListResumeIndex(after, 1, (0, 2), "c3n"));
        Assert.Equal(1, CellSheetLogic.ListResumeIndex(after, 5, (0, 2), "c3n"));
    }

    [Fact]
    public void NothingWhenNothingWasPickedOrTheListIsEmpty()
    {
        Assert.Null(CellSheetLogic.ListResumeIndex(new[] { Row(0, 1) }, null, null));
        Assert.Null(CellSheetLogic.ListResumeIndex(Array.Empty<ProblemRow>(), 0, (0, 1)));
        Assert.Equal(0, CellSheetLogic.ListResumeIndex(new[] { Row(0, 1) }, null, (0, 1)));
    }
}
