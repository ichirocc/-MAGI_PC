using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>CsvImportPreviewTest</c>（3.727.1）の 1 対 1 移植。勤務表 CSV を反映する前の確認: 3 点以内で、人数・変わるセル・必須の前後・注意を出す。</summary>
public class CsvImportPreviewTest
{
    private static readonly int[][] Base = { new[] { 0, 1 }, new[] { 1, 1 }, new[] { 0, 0 } };
    private static ViolationReport Rep(int hard) => new(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
        new Dictionary<string, int>(), hard, hard, 0, 0.0);

    [Fact]
    public void CleanImportHasTwoPoints()
    {
        var res = new ScheduleRunResult(new[] { new[] { 1, 1 }, new[] { 1, 1 }, new[] { 0, 2 } }, Rep(0), Matched: 3);
        Assert.Equal(new[] { "3/3名の勤務を取り込みます（変わるセル 2）", "必須違反：2 → 0" }, CsvImportPreview.CsvImportPreviewLines(res, 3, Base, 2));
    }

    [Fact]
    public void WarningsFoldIntoOneLineAndStayWithinTheDialogLimit()
    {
        var res = new ScheduleRunResult(Base, Rep(1), Matched: 1, UnknownCells: 2, UnknownSymbols: new[] { "X" }, HeaderDateMismatches: 1,
            AmbiguousNames: new[] { "佐藤" }, DuplicateRowNames: new[] { "鈴木" });
        var lines = CsvImportPreview.CsvImportPreviewLines(res, 3, Base, 1);
        Assert.True(lines.Count <= CellSheetLogic.ConfirmMaxPoints);
        Assert.Equal("1/3名の勤務を取り込みます（変わるセル 0）", lines[0]);
        Assert.StartsWith("⚠ 2名は氏名が合わずスキップ／読めない記号 2セル（X）", lines[2]);
        Assert.Contains("佐藤さん", lines[2]);
        Assert.Contains("鈴木", lines[2]);
    }
}
