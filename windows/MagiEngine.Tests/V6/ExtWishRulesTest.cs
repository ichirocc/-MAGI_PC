using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>仕様「希望シフトの拡張」第 9 節の例（Kotlin <c>ExtWishRulesTest</c> の 1 対 1 移植）。日は 1 始まりの表記、日番号は 0 始まり。</summary>
public class ExtWishRulesTest
{
    // 0=休 1=日勤 2=夜 3=準夜。職員 A=0（3 日＝日番号 2 に日勤の希望）、B=1。
    private static MagiState Base(IReadOnlyList<ExtWish>? ext = null) => MinimalState.Build(
        startDate: "2026-10-01", endDate: "2026-10-05",
        shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("日勤", "日", "", ""), new("夜勤", "夜", "", ""), new("準夜", "準", "", "") },
        schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(1, 5).ToList(), Enumerable.Repeat(0, 5).ToList() },
        wishes: new Dictionary<string, int> { ["0,2"] = 1 }) with { ExtWishes = ext };

    private static readonly ExtWish First = new(0, new[] { "2026-10-01", "2026-10-02", "2026-10-03" }, new[] { "夜", "準" });

    [Fact]
    public void WishDayIsDroppedFromTheSavedDays()
    {
        var r = ExtWishRules.Sanitize(Base(), First);
        Assert.Equal(new[] { "2026-10-01", "2026-10-02" }, r.Saved!.Days);
        Assert.Contains(ExtWishRules.MsgWishDay, r.Notices);
    }

    [Fact]
    public void ExampleTableCountsOnlyDay2()
    {
        var st = Base(new[] { ExtWishRules.Sanitize(Base(), First).Saved! });
        var board = new[] { new[] { 1, 2, 2, 2, 1 }, new[] { 0, 0, 0, 0, 0 } };
        var rep = UnifiedViolationChecker.Check(st, board);
        Assert.Equal(new[] { "0,1" }, rep.ExtWishCells);
        var plain = UnifiedViolationChecker.Check(Base(), board);
        Assert.Equal(plain.WeightedScore, rep.WeightedScore);
        Assert.Equal(plain.Breakdown, rep.Breakdown);
    }

    [Fact]
    public void BasicWishOnAnExtDayIsBlocked()
    {
        var st = Base(new[] { ExtWishRules.Sanitize(Base(), First).Saved! });
        Assert.Equal(ExtWishRules.MsgExtDay, ExtWishRules.WishBlockedBy(st, 0, 0));
        Assert.Null(ExtWishRules.WishBlockedBy(st, 1, 0));
        Assert.Null(ExtWishRules.WishBlockedBy(st, 0, 3));
    }

    [Fact]
    public void SecondEntryUnionsOnSharedDayButCountsOnce()
    {
        var a = ExtWishRules.Sanitize(Base(), First).Saved!;
        var b = new ExtWish(0, new[] { "2026-10-02", "2026-10-05" }, new[] { "準" });
        var st = Base(new[] { a, b });
        var p = new Problem(st);
        foreach (var k in new[] { 2, 3 })
        {
            var board = new[] { new[] { 1, k, 1, 1, 1 }, new[] { 0, 0, 0, 0, 0 } };
            Assert.Single(UnifiedViolationChecker.Check(st, board).ExtWishCells);
        }
        Assert.True(!p.ExtBan.Banned(0, 1, 1) && !p.ExtBan.Banned(0, 1, 0));
        Assert.True(p.ExtBan.Banned(0, 4, 3) && !p.ExtBan.Banned(0, 4, 2));
        Assert.Equal(1, ExtWishRules.Delta(p.ExtBan, 0, 1, 1, 2));
        Assert.Equal(-1, ExtWishRules.Delta(p.ExtBan, 0, 1, 2, 0));
        Assert.Equal(0, ExtWishRules.Delta(p.ExtBan, 0, 1, 2, 3));
        Assert.True(!p.MayPlaceAt(0, 1, 2) && p.MayPlaceAt(0, 1, 1));
    }

    [Fact]
    public void SavingDropsBadPartsAndRejectsDuplicatesAndFullBans()
    {
        var st = Base();
        var r = ExtWishRules.Sanitize(st, new ExtWish(1, new[] { "2026-09-30", "2026-10-04" }, new[] { "夜", "X" }));
        Assert.Equal(new[] { "2026-10-04" }, r.Saved!.Days);
        Assert.Equal(new[] { "夜" }, r.Saved!.Shifts);
        Assert.Null(ExtWishRules.Sanitize(st, new ExtWish(1, new[] { "2026-11-01" }, new[] { "夜" })).Saved);
        Assert.Null(ExtWishRules.Sanitize(st, new ExtWish(1, new[] { "2026-10-04" }, new[] { "休", "日", "夜", "準" })).Saved);
        var st2 = Base(new[] { r.Saved! });
        Assert.Null(ExtWishRules.Sanitize(st2, new ExtWish(1, new[] { "2026-10-04" }, new[] { "夜" })).Saved);
        Assert.NotNull(ExtWishRules.Sanitize(st2, new ExtWish(0, new[] { "2026-10-04" }, new[] { "夜" })).Saved);
    }

    [Fact]
    public void OverlappingLoadedDataKeepsBothAndExcludesTheDay()
    {
        var st = Base(new[] { First });
        var p = new Problem(st);
        Assert.Equal(new[] { (0, 2) }, p.ExtBan.Overlaps);
        Assert.False(p.ExtBan.Banned(0, 2, 2));
        Assert.Single(st.Wishes);
        Assert.Equal(3, st.ExtWishes![0].Days.Count);
    }

    [Fact]
    public void BannedByCellListsOnlyDesignatedDays()
    {
        var st = Base(new[] { First });   // 3 日（希望の日）は読み込みで重なっていても表示しない
        var m = ExtWishRules.BannedByCell(st);
        Assert.Equal(new[] { "0,0", "0,1" }, m.Keys.OrderBy(x => x));
        Assert.True(m["0,0"].SetEquals(new[] { 2, 3 }));
        Assert.Empty(ExtWishRules.BannedByCell(Base()));
    }

    [Fact]
    public void JsonRoundTripAndAbsentKeyIsEmpty()
    {
        var st = Base(new[] { ExtWishRules.Sanitize(Base(), First).Saved! });
        var back = StateJsonSerializer.Parse(StateJsonSerializer.Serialize(st, st.Schedule.ToIntArray2D()));
        Assert.Single(back.ExtWishes!);
        Assert.Equal(st.ExtWishes![0].Days, back.ExtWishes![0].Days);
        Assert.Equal(st.ExtWishes![0].Shifts, back.ExtWishes![0].Shifts);
        Assert.Empty(StateJsonSerializer.Parse(StateJsonSerializer.Serialize(Base(), Base().Schedule.ToIntArray2D())).ExtWishes ?? Array.Empty<ExtWish>());
    }
}
