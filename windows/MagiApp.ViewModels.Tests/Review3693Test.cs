namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3693Test</c> の 1 対 1 移植。3.693.0「8秒理解UI 第1便」: 必須違反の数は件と人を足さない（INV-07）＝実行中の札・反映の帯の次の一歩・適用の通知。</summary>
public class Review3693Test
{
    [Fact]
    public void RunningBadgeSaysHardPerUnit()
    {
        Assert.Equal("組んでいます 必須1件・2人", AnalysisTriage.RunningBadgeLabel(new Dictionary<string, int> { ["covU"] = 2, ["c3n"] = 1 }, 3));
        Assert.Equal("組んでいます 必須5件", AnalysisTriage.RunningBadgeLabel(new Dictionary<string, int>(), 5));
        Assert.Equal("組んでいます", AnalysisTriage.RunningBadgeLabel(new Dictionary<string, int>(), 0));
    }

    [Fact]
    public void AppliedNextButtonCountsTourItemsNotHardTotal()
    {
        Assert.Equal("必須違反2か所を順に見る", CellSheetLogic.AppliedNextButton(false, "A", 3, 2));
        Assert.Null(CellSheetLogic.AppliedNextButton(false, "A", 3, 0));
    }

    [Fact]
    public void FixOutcomeSaysBeforeAndAfterPerUnit()
    {
        Assert.Equal("入替 を適用しました。必須違反 1件・2人→1人。ほかの必須違反は 1人 残っています。",
            FixOutcomeText.Applied("入替", 3, 1, "40件", "38件", new Dictionary<string, int> { ["covU"] = 1 }, new Dictionary<string, int> { ["c3n"] = 1, ["covU"] = 2 }));
    }
}
