namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>TourCursorTest</c>（3.726.0）の 1 対 1 移植。巡回の位置が一覧の作り直し（違反の解消・発生）に追従する。
/// Kotlin の関数 <c>tourCursor</c> は C# では型 <see cref="TourCursor"/> と名前がぶつかるので <c>TourCursorOf</c>。
/// ソースを読む検査（Compose の引数の形）は WinUI の <c>ScheduleView</c> の呼び出しを見る形に置き換えた。
/// </summary>
public class TourCursorTest
{
    private static TourItem Item(int staff, int day, string fam = "pref") => new(fam, staff, new[] { day }, $"{fam} {staff}/{day}");
    private readonly TourItem a = Item(0, 1), b = Item(1, 2), c = Item(2, 3), d = Item(3, 4);

    private static TourCursor Tc(IReadOnlyList<TourItem> baseList, int at, IReadOnlyList<TourItem> items) => CellSheetLogic.TourCursorOf(baseList, at, items);

    [Fact]
    public void UnchangedListKeepsTheIndexAndWraps()
    {
        var l = new[] { a, b, c };
        Assert.Equal(new TourCursor(1, 2, 0), Tc(l, 1, l));
        Assert.Equal(new TourCursor(2, 0, 1), Tc(l, 2, l));
        Assert.Equal(new TourCursor(0, null, null), Tc(new[] { a }, 0, new[] { a }));   // 1 件だけなら前・次は無い
    }

    [Fact]
    public void FixingTheCurrentItemPointsNextAtItsSuccessorNotOnePastIt()
    {
        var now = new[] { a, c, d };
        var t = Tc(new[] { a, b, c, d }, 1, now);
        Assert.Equal(new TourCursor(null, 1, 0), t);
        Assert.Equal(c, now[t.Next!.Value]);
        Assert.Equal(a, now[t.Prev!.Value]);
    }

    [Fact]
    public void AnEarlierItemVanishingFollowsTheCurrentItem()
    {
        Assert.Equal(new TourCursor(1, 0, 0), Tc(new[] { a, b, c }, 2, new[] { b, c }));
        Assert.Equal(new TourCursor(1, 2, 0), Tc(new[] { a, b, c }, 1, new[] { a, b, d }));
    }

    [Fact]
    public void VanishedItemWithNewArrivalsAndEdges()
    {
        Assert.Equal(new TourCursor(null, 0, 1), Tc(new[] { a, b, c }, 0, new[] { b, c }));
        Assert.Equal(new TourCursor(null, 0, 1), Tc(new[] { a, b, c }, 2, new[] { a, b }));
        Assert.Equal(new TourCursor(null, 1, 0), Tc(new[] { a, b, c }, 1, new[] { a, d, c }));
        Assert.Equal(new TourCursor(null, 0, 0), Tc(new[] { a, b }, 1, new[] { a }));
        Assert.Equal(new TourCursor(null, null, null), Tc(new[] { a }, 0, Array.Empty<TourItem>()));   // 全部直った
    }

    [Fact]
    public void ACellInTwoViolationsStillAdvancesByPosition()
    {
        var run = new TourItem("c3n", 0, new[] { 0, 1, 2 }, "禁止の並び");
        var wish = new TourItem("c3w", 0, new[] { 0, 1 }, "希望の前日禁止");
        var l = new[] { run, wish, b };
        var t = Tc(l, 0, l);
        Assert.Equal(new TourCursor(0, 1, 2), t);
        Assert.Equal(run.Cell, l[t.Next!.Value].Cell);
        Assert.Equal("必須違反 2 / 3 ・ 希望の前日禁止", CellSheetLogic.TourHeading(l, Tc(l, t.Next!.Value, l).At!.Value));
    }

    [Fact]
    public void TourButtonsMoveByItemNotByCell()
    {
        string? dir = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && dir is null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MagiApp.WinUI", "Views", "ScheduleView.xaml.cs"))) dir = d.FullName;
        var src = File.ReadAllText(Path.Combine(dir!, "MagiApp.WinUI", "Views", "ScheduleView.xaml.cs"));
        Assert.Contains("CellSheetLogic.TourCursorOf(_tourBase, _tourAt, tourItems)", src);
    }

    [Fact]
    public void MatchesByFamilyStaffAndDaysNotByHeading()
    {
        Assert.Equal(new TourCursor(0, 1, 1), Tc(new[] { a, b }, 0, new[] { a with { Heading = "見出しだけ変わった" }, b }));
        Assert.Equal(new TourCursor(null, 1, 0), Tc(new[] { a, b }, 0, new[] { Item(0, 1, "c3w"), b }));   // 同じセルでも族が違えば別の違反
    }
}
