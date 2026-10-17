using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3699Test</c> の 1 対 1 移植。3.699.0: 画面精査の是正（日の一覧・門と同じ判定・過剰の題・全部 ⚠ の次の一手・下書きの確認の前後・下書き後の主ボタン）。</summary>
public class Review3699Test
{
    // 職員 3 人 × 2 日。勤務 0＝A、1＝休。
    private static readonly IReadOnlyList<IReadOnlyList<int>> Board = new[] { new[] { 0, 0 }, new[] { 0, 1 }, new[] { 1, 0 } };
    private static readonly IReadOnlyList<string> Names = new[] { "金沢", "山田", "佐藤" };
    private static readonly IReadOnlyList<string> Syms = new[] { "A", "休" };
    private static readonly FixTarget OverDay0 = new(null, 0, 0, 1, false, "適正人数");

    [Fact]
    public void DayListShowsOnlySuggestionsTouchingThatDay()
    {
        Assert.True(CellSheetLogic.ListableFix(null, Board, new[] { new FixCell(0, 0, 1) }, 0));
        Assert.False(CellSheetLogic.ListableFix(null, Board, new[] { new FixCell(0, 1, 1) }, 0));
        Assert.True(CellSheetLogic.ListableFix(null, Board, new[] { new FixCell(0, 1, 1) }, null));
        Assert.Equal(3, CellSheetLogic.FixListDay(new FixFocus(null, 0, 3)));
        Assert.Null(CellSheetLogic.FixListDay(new FixFocus(1, null, 3)));
        Assert.Null(CellSheetLogic.FixListDay(new FixFocus(null, 0, 3, AllDays: true)));
    }

    [Fact]
    public void ListUsesTheApplyGatePredicate()
    {
        var removes = new[] { new FixCell(0, 0, 1) };
        var adds = new[] { new FixCell(2, 0, 0) };
        Assert.Equal(CellSheetLogic.ImprovesTarget(OverDay0, Board, removes), CellSheetLogic.ListableFix(OverDay0, Board, removes, null));
        Assert.True(CellSheetLogic.ListableFix(OverDay0, Board, removes, 0));
        Assert.False(CellSheetLogic.ImprovesTarget(OverDay0, Board, adds));
        Assert.False(CellSheetLogic.ListableFix(OverDay0, Board, adds, 0));
    }

    [Fact]
    public void SurplusMoveTitleSaysItReducesTheSurplus()
    {
        Assert.Equal("金沢さんを A から 休 へ移して過剰を減らす", CellSheetLogic.SurplusMoveTitle(OverDay0, Board, new[] { new FixCell(0, 0, 1) }, Names, Syms));
        Assert.Null(CellSheetLogic.SurplusMoveTitle(OverDay0 with { Under = true }, Board, new[] { new FixCell(0, 0, 1) }, Names, Syms));
        Assert.Null(CellSheetLogic.SurplusMoveTitle(OverDay0, Board, new[] { new FixCell(2, 0, 0) }, Names, Syms));
        Assert.Null(CellSheetLogic.SurplusMoveTitle(OverDay0, Board, new[] { new FixCell(0, 0, 1), new FixCell(1, 0, 1) }, Names, Syms));
    }

    [Fact]
    public void AllBlockedPeekOffersOneSearch()
    {
        // 3.702.0 S-3: ちら見の「変更案を探す」は問題のあるセルで常に出す（候補が全部 ⚠ のときだけ、をやめた）。
        Assert.Equal(CellSheetLogic.PeekSearchButton, CellSheetLogic.PeekSearchAction(0, CellSeverity.Hard));
        Assert.Null(CellSheetLogic.PeekSearchAction(1, CellSeverity.Hard));      // 希望の登録中
        Assert.Null(CellSheetLogic.PeekSearchAction(0, CellSeverity.None));
    }

    [Fact]
    public void DraftConfirmShowsBeforeAndAfter()
    {
        Assert.Equal("今の勤務表を下書きで置き換えます（必須 3件・1人→1件）。元に戻せます", FixExplain.DraftConfirmText("3件・1人", "1件"));
        Assert.Equal("今の勤務表を下書きで置き換えます。元に戻せます", FixExplain.DraftConfirmText(null, "1件"));
    }

    [Fact]
    public void DraftStagePrimaryIsMake()
    {
        var p = CellSheetLogic.HomePlanOf(false, true, false, draft: true, hasMove: true, guided: true);
        Assert.Equal(HomePlanKind.Draft, p.Kind);
        Assert.Equal(HomeAction.Make, p.Primary);
        Assert.True(p.PrimaryEnabled);
        Assert.Equal(HomePlanKind.Draft, CellSheetLogic.HomePlanOf(false, true, true, draft: true).Kind);
        Assert.Equal(HomePlanKind.DataInfeasible, CellSheetLogic.HomePlanOf(false, true, false, infeasible: true, draft: true).Kind);
        Assert.Equal(HomePlanKind.Running, CellSheetLogic.HomePlanOf(true, true, false, draft: true).Kind);
        var s = CellSheetLogic.HomeCardSectionsOf(p, new UiState { Loaded = true, HasResult = true, BestHard = 3L, EngineRan = true });
        Assert.Equal(1, s.Count(x => x == HomeSection.Primary));
        Assert.Equal("下書きができました（必須 3件）。「勤務表をつくる」で仕上げます", CellSheetLogic.HomeDraftStatus("3件", 3L));
    }
}
