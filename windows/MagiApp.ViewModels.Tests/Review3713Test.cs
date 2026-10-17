using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3713Test</c> の 1 対 1 移植。3.713.0: 利用者のデザイン例（2026-10-17）に合わせた短い骨格。
/// 見出し 12 字以内・説明 1 行 22 字以内・必須の札は残りの件数だけ。決定済みのボタンの語は変えない。
/// </summary>
public class Review3713Test
{
    private static readonly CoverageShortfall Sf = new(9, "10/10(土)", 0, "Aｱ", Need: 1, Got: 0, Miss: 1, Capacity: 3, Verdict: CoverageVerdict.Fixable, Reason: "");

    [Fact]
    public void HomeHeadingPillAndProblemLine()
    {
        Assert.Equal("今月の判断", CellSheetLogic.HomeHeadline(HomePlanKind.List, "2026-10-01"));
        Assert.Equal("必須 残り4件", CellSheetLogic.HomeHardPill(4L, "4件"));
        Assert.Equal("必須は満たしています", CellSheetLogic.HomeHardPill(0L, "0件"));
        Assert.Equal("10/10のAｱが1人不足", CellSheetLogic.ShortageHeadlineOf(Sf).Headline);
        Assert.Equal("配置 0 / 必要 1", CellSheetLogic.ShortageHeadlineOf(Sf).Status);
        Assert.Equal("担当・希望の確認", CellSheetLogic.ShortageTag);
        Assert.Equal("必須を見る", CellSheetLogic.HomeListButton);
        Assert.Equal("配布前の確認", CellSheetLogic.HomePreDistButton);
        Assert.Equal("表で全体を見る", CellSheetLogic.HomeScheduleHelper);
        Assert.Equal("相談中 3件", ConsultList.Line(3));
    }

    [Fact]
    public void DialogsAndEmptyStates()
    {
        Assert.Equal("変更の確認", ChainFixPreview.PreviewTitle);
        Assert.Equal("2か所変わります", ChainFixPreview.PreviewCountLine(2));
        Assert.Equal("山本  10/15  休 → 遅番", ChainFixPreview.PreviewRowLine("山本", "10/15", "休", "遅番"));
        Assert.Equal("案はありません", AnalysisTriage.NoFixTitle);
        Assert.Equal("設定か希望の確認が必要です", AnalysisTriage.NoFixLineOf(false));
        Assert.Equal(AnalysisTriage.RepeatedEmptyLine, AnalysisTriage.NoFixLineOf(true));
        Assert.Equal("作成が終わりました", CellSheetLogic.CreationDoneTitle);
        Assert.Equal("必須3件 · 要調整12件", AnalysisTriage.AnalysisCountLine("3件", "12件"));
        // 決定済みの語は据え置き。
        Assert.Equal("この変更を適用する", ChainFixPreview.ApplyChangeButton);
        Assert.Equal("今は変更しない", ChainFixPreview.KeepCurrentButton);
    }

    [Fact]
    public void LengthLimits()
    {
        foreach (var h in new[] { CellSheetLogic.HomeMonthHeadline, ChainFixPreview.PreviewTitle, AnalysisTriage.NoFixTitle, CellSheetLogic.CreationDoneTitle, CellSheetLogic.CellSheetTitle })
            Assert.True(h.Length <= 12, h);
        foreach (var l in new[] { CellSheetLogic.HomeHard0Headline, CellSheetLogic.ShortageHeadlineOf(Sf).Headline, CellSheetLogic.ShortageHeadlineOf(Sf).Status,
                     AnalysisTriage.NoFixLine, AnalysisTriage.RepeatedEmptyLine, ChainFixPreview.PreviewCountLine(12), ConsultList.Line(10)!, CellSheetLogic.HomeHardPill(4L, "4件") })
            Assert.True(l.Length <= 22, l);
    }
}
