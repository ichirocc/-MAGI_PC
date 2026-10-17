namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3702Test</c> の 1 対 1 移植。3.702.0: 勤務表タブの状態の 1 行（G2-1）・必須違反の一覧の形（G2-3）・セルの確認シート（S-1〜S-6）。</summary>
public class Review3702Test
{
    [Fact]
    public void StateLineFiveStates()
    {
        Assert.Equal("まだ作っていません", CellSheetLogic.ScheduleStateLineOf(false, false, 0, "", "なし", null).Text);
        Assert.Equal("組んでいます", CellSheetLogic.ScheduleStateLineOf(true, true, 3, "3件", "なし", "10/3 山田").Text);
        var tour = CellSheetLogic.ScheduleStateLineOf(true, false, 2, "2件", "5回", "10/3 山田");
        Assert.Equal("必須違反 2件（次：10/3 山田さん）", tour.Text);
        Assert.True(tour.Hard && tour.TourFirst);
        var covU = CellSheetLogic.ScheduleStateLineOf(true, false, 1, "1人", "5回", null);
        Assert.Equal("必須違反 1人（日の印から開けます）", covU.Text);
        Assert.True(covU.Hard); Assert.False(covU.TourFirst);
        var ok = CellSheetLogic.ScheduleStateLineOf(true, false, 0, "0件", "4件・3回", null);
        Assert.Equal("必須違反はありません · 要調整 4件・3回", ok.Text);
        Assert.False(ok.Hard || ok.TourFirst);
    }

    [Fact]
    public void StateLineIsCapped()
    {
        var @long = CellSheetLogic.ScheduleStateLineOf(true, false, 9, "12件・3人", "なし", "10/13 長谷川太郎");
        Assert.Equal(CellSheetLogic.ScheduleStateMax, @long.Text.Length);
        Assert.EndsWith("…", @long.Text);
        Assert.Equal("abc", CellSheetLogic.CapStateLine("abc", 3));
        Assert.Equal("ab…", CellSheetLogic.CapStateLine("abcd", 3));
    }

    [Fact]
    public void HardListShape()
    {
        Assert.Equal("残っている問題 3件・2人", CellSheetLogic.HardListTitle("3件・2人"));
        Assert.Equal("直し方を見る", CellSheetLogic.HardListStartButton);
    }

    [Fact]
    public void TargetLineAndBadge()
    {
        Assert.Equal("このマスの確認", CellSheetLogic.CellSheetTitle);
        Assert.Equal("山田さん · 10/3(金) · A4", CellSheetLogic.CellSheetTarget("山田", "10/3(金)", "A4"));
        Assert.Equal("山田さん · 10/3(金)", CellSheetLogic.CellSheetTarget("山田", "10/3(金)", null));
        Assert.Equal("必須", CellSheetLogic.CellSheetBadge(CellSeverity.Hard));
        Assert.Equal("必須 2/5", CellSheetLogic.CellSheetBadge(CellSeverity.Hard, 1, 5));
        Assert.Equal("要調整", CellSheetLogic.CellSheetBadge(CellSeverity.Soft, 1, 5));
        Assert.Null(CellSheetLogic.CellSheetBadge(CellSeverity.None));
    }

    [Fact]
    public void PeekHasExactlyOnePrimary()
    {
        // 問題のあるセルの割当では主（全幅の塗り）は「変更案を探す」1 つ。緩める候補・希望を外すは外枠（画面側）。
        foreach (var sev in new[] { CellSeverity.Hard, CellSeverity.Soft }) Assert.Equal(CellSheetLogic.PeekSearchButton, CellSheetLogic.PeekSearchAction(0, sev));
        Assert.Equal("変更案を探す", CellSheetLogic.PeekSearchButton);
        Assert.Null(CellSheetLogic.PeekSearchAction(0, CellSeverity.None));
        Assert.Null(CellSheetLogic.PeekSearchAction(1, CellSeverity.Soft));
    }

    [Fact]
    public void ShortSideIsPrimaryOnlyWhenAlone()
    {
        Assert.True(CellSheetLogic.ShortSideFilled(new[] { "covU" }));
        Assert.False(CellSheetLogic.ShortSideFilled(new[] { "covU", "c3n" }));
    }
}
