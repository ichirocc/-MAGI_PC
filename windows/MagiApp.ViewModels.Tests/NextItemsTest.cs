using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>NextItemsTest</c> の 1 対 1 移植。2026-10-29 画面案: ホームの「次にやること」・問題一覧の 2 段の行・ホームの区画。</summary>
public class NextItemsTest
{
    private static readonly IReadOnlyList<TourItem> Items = new[]
    {
        new TourItem("c3n", 0, new[] { 7, 8 }, "禁止の並び Dﾃ→A4", new[] { "Dﾃ", "A4" }, "10/8"),
        new TourItem("c3w", 1, new[] { 3 }, "希望の前日", new[] { "Dﾃ" }, "10/4"),
        new TourItem("pref", 2, new[] { 5 }, "希望と違う勤務", new[] { "A4" }, "10/6"),
        new TourItem("c3n", 3, new[] { 1, 2 }, "禁止の並び", new[] { "Dﾃ", "A4" }, "10/2"),
    };
    private static readonly IReadOnlyList<string> Names = new[] { "アリフ", "古泉", "福澤", "大島" };
    private static readonly IReadOnlyList<HardListRow> Rows = CellSheetLogic.HardViolationRows(Items, Names, new HashSet<string> { "1,3" });

    [Fact]
    public void TopThreeWithTagsAndWishOnlyLast()
    {
        var fixable = new HashSet<ViolationKey> { new("c3n", ViolationKey.Kind.Cell, 0, 8) };
        var next = CellSheetLogic.HomeNextItems(Rows, Items, fixable);
        Assert.Equal(new[] { 1, 2, 3 }, next.Select(n => n.Rank));
        Assert.Equal(new[] { "アリフ", "福澤", "大島" }, next.Select(n => n.Name));
        Assert.Equal(new[] { CellSheetLogic.NextTagFixable, CellSheetLogic.NextTagCheck, CellSheetLogic.NextTagCheck }, next.Select(n => n.Tag));
        Assert.Equal("Dﾃ→A4の並び", next[0].Reason);
        Assert.Equal((0, 7), next[0].Cell);
    }

    [Fact]
    public void NoTagAboutFixesBeforeSearching()
    {
        var next = CellSheetLogic.HomeNextItems(Rows, Items, null, max: 4);
        Assert.Equal(new string?[] { null, null, null, NextActionGuide.WishOnlyTag }, next.Select(n => n.Tag));
    }

    [Fact]
    public void RowPartsSplitTheNameFromTheReason()
    {
        Assert.Equal(("福澤", "10/4 希望の前日"), CellSheetLogic.ProblemRowParts("福澤 10/4 希望の前日", "福澤"));
        Assert.Equal(((string?)null, "10/10のAｱが1人不足"), CellSheetLogic.ProblemRowParts("10/10のAｱが1人不足", null));
        Assert.Equal(((string?)null, "別の人の行"), CellSheetLogic.ProblemRowParts("別の人の行", "福澤"));
        Assert.Equal("必須 3件", CellSheetLogic.ProblemListCount("3件"));
        Assert.Equal("要調整 (12)", CellSheetLogic.SoftFoldLabel(12));
    }

    [Fact]
    public void NextItemsSectionSitsBetweenStatusAndPrimaryOnlyWithHard()
    {
        var plan = CellSheetLogic.HomePlanOf(false, true, false);
        var s = CellSheetLogic.HomeCardSectionsOf(plan, new UiState { Loaded = true, HasResult = true, BestHard = 3L, CheckRev = 1 }).ToList();
        Assert.True(s.IndexOf(HomeSection.Status) < s.IndexOf(HomeSection.NextItems));
        Assert.True(s.IndexOf(HomeSection.NextItems) < s.IndexOf(HomeSection.Primary));
        Assert.DoesNotContain(HomeSection.NextItems, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, true), new UiState { Loaded = true, HasResult = true, BestHard = 0L, CheckRev = 1 }));
        Assert.DoesNotContain(HomeSection.NextItems, CellSheetLogic.HomeCardSectionsOf(plan, new UiState { Loaded = true, HasResult = true, BestHard = 3L, CheckRev = 0 }));
    }
}
