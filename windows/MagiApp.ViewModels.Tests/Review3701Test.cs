using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3701Test</c> の 1 対 1 移植。3.701.0: 保存の失敗を知らせる（上部の語・ダイアログ・主カードの状態の行）と、上部の札の狭い幅。</summary>
public class Review3701Test
{
    [Fact]
    public void TopBarSaveWordOnlyWhenFailed()
    {
        Assert.Equal("自動保存できていません", CellSheetLogic.TopBarSaveWord(SaveState.Failed));
        Assert.Null(CellSheetLogic.TopBarSaveWord(SaveState.Saved));
        Assert.Null(CellSheetLogic.TopBarSaveWord(SaveState.Saving));
        Assert.Null(CellSheetLogic.TopBarSaveWord(SaveState.Dirty));
    }

    [Fact]
    public void NarrowTopBarWrapsBelow396dp()
    {
        Assert.True(CellSheetLogic.TopBarStatusWrapped(395));
        Assert.False(CellSheetLogic.TopBarStatusWrapped(396));
    }

    /// <summary>書き込みを失敗させる保存先（fake）。</summary>
    private sealed class FakeStore(bool failing)
    {
        public bool Failing { get; set; } = failing;
        public bool Write() => !Failing;
    }

    [Fact]
    public void DialogShownOnceWhenSaveTurnsFailed()
    {
        var store = new FakeStore(failing: false);
        var n = new SaveFailNotifier();
        Assert.Null(n.OnResult(store.Write(), retried: false));
        store.Failing = true;
        Assert.Equal(SaveFailDialog.First, n.OnResult(store.Write(), retried: false));
        // 1.2 秒ごとの自動保存が失敗し続けても出し直さない。
        Assert.Null(n.OnResult(store.Write(), retried: false));
        Assert.Null(n.OnResult(store.Write(), retried: false));
        // 利用者のやり直しも失敗したら「まだ保存できていません」。
        var again = n.OnResult(store.Write(), retried: true);
        Assert.Equal(SaveFailDialog.Again, again);
        Assert.Equal("まだ保存できていません", CellSheetLogic.SaveFailDialogTitle(again!.Value));
        Assert.Equal("自動保存できませんでした", CellSheetLogic.SaveFailDialogTitle(SaveFailDialog.First));
        store.Failing = false;
        Assert.Null(n.OnResult(store.Write(), retried: true));
        Assert.True(n.LastOk);
        store.Failing = true;
        Assert.Equal(SaveFailDialog.First, n.OnResult(store.Write(), retried: false));
    }

    [Fact]
    public void SaveFailedLineInHomeStatusRegardlessOfStage()
    {
        foreach (var engineRan in new[] { true, false })
        {
            var ui = new UiState { Loaded = true, HasResult = true, BestHard = 0L, EngineRan = engineRan, SaveState = SaveState.Failed };
            Assert.Contains(HomeSection.Status, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, true), ui));
        }
        var pre = CellSheetLogic.HomeStatusLines(HomePlanKind.PreDist, null, "未確認事項 1 件", "勤務表CSVは今の内容と違います", saveFailed: true);
        Assert.Equal(new[] { CellSheetLogic.HomeSaveFailedLine, "未確認事項 1 件" }, pre);
        var list = CellSheetLogic.HomeStatusLines(HomePlanKind.List, "必須違反が 2件 残っています。", "未確認事項 1 件", null, saveFailed: true);
        Assert.Equal(2, list.Count);
        Assert.Equal(CellSheetLogic.HomeSaveFailedLine, list[0]);
        Assert.DoesNotContain(CellSheetLogic.HomeSaveFailedLine, CellSheetLogic.HomeStatusLines(HomePlanKind.Make, "", null, null));
        Assert.Equal(new[] { CellSheetLogic.HomeSaveFailedLine }, CellSheetLogic.HomeStatusLines(HomePlanKind.Make, "", null, null, saveFailed: true));
    }

    [Fact]
    public void CsvFailureReopensPreDistribution()
    {
        Assert.True(CellSheetLogic.CsvExportReopensPreDist(false));
        Assert.False(CellSheetLogic.CsvExportReopensPreDist(true));
    }
}
