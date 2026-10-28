namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>AgentTextTest</c> のうち 3.727.1 で足された <c>softHeadPicksFirstMovedUnit</c> の移植（見出し・通知は代行役の本体と一緒に移る）。
/// </summary>
public class AgentTextTest
{
    [Fact]
    public void SoftHeadPicksFirstMovedUnit()
    {
        Assert.Equal("147→140件", AgentText.SoftHeadDelta("147件・3人・80回・283pt", "140件・4人・70回・277pt"));
        Assert.Equal("80→70回", AgentText.SoftHeadDelta("3人・80回", "3人・70回"));
        Assert.Equal("3→3人", AgentText.SoftHeadDelta("3人", "3人"));
        Assert.Equal("5→0件", AgentText.SoftHeadDelta("5件", "なし"));
        Assert.Equal("なし→2件", AgentText.SoftHeadDelta("なし", "2件"));
        Assert.Equal("代行をやめる", CellSheetLogic.StopButtonLabel(false, true, true));
        Assert.Equal("やめる", CellSheetLogic.StopButtonLabel(true, true, true));
        Assert.Equal(CellSheetLogic.CancelFixSearchButton, CellSheetLogic.StopButtonLabel(false, true, false));
    }
}
