namespace MagiApp.ViewModels.Tests;

/// <summary>代行役（AX）のカードの文: 何もしなかった輪は出さない・必須が動けば必須を、動かなければ要調整を言う。Kotlin <c>AgentTextTest</c> の移植。</summary>
public class AgentTextTest
{
    private static AgentRecord Rec(int n, int hb, int ha) => new(Enumerable.Range(0, n).Select(i => $"手{i}").ToList(), hb, ha, "10件", "8件", "当てられる手が尽きた");

    [Fact]
    public void Headline()
    {
        Assert.Null(AgentText.Headline(Rec(0, 3, 3)));
        Assert.Equal("代行で 2 手を当てました（必須違反 3→1）", AgentText.Headline(Rec(2, 3, 1)));
        Assert.Equal("代行で 1 手を当てました（要調整 10→8件）", AgentText.Headline(Rec(1, 1, 1)));
        Assert.Equal("代行で 1 手を当てました", AgentText.Notice(Rec(1, 1, 1)));
        Assert.Null(AgentText.Notice(Rec(0, 1, 1)));
    }

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

    [Fact]
    public void Pending()
    {
        Assert.Empty(AgentText.PendingItems(0, false, 0));
        Assert.Null(AgentText.PendingLine(Array.Empty<string>()));
        Assert.Equal("判断待ち：希望の見直し 2件・設定を緩める候補・設定ミスの直し 3件", AgentText.PendingLine(AgentText.PendingItems(2, true, 3)));
        Assert.Equal("判断待ち：設定ミスの直し 1件", AgentText.PendingLine(AgentText.PendingItems(0, false, 1)));
    }

    [Fact]
    public void EdgeCounts()
    {
        Assert.Empty(AgentText.PendingItems(-1, false, -3));   // 負の数は判断待ちに数えない
        Assert.Equal("代行で 12 手を当てました（必須違反 9999→0）", AgentText.Headline(Rec(12, 9999, 0)));
    }

    [Fact]
    public void HomeDiagnosisCountAllCards()
    {
        var u = new UiState { CopilotHint = "x", ObservedPinBlockedAttempts = 1 };
        Assert.Equal(2, CellSheetLogic.HomeDiagnosisCount(u));
    }
}
