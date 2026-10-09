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
        // [3.653.0] 採点にも入る: 必須の族 extWish が 1 件、重みは希望と同じ（HF77 明示指示）。評価器も同じ件数。
        var plain = UnifiedViolationChecker.Check(Base(), board);
        Assert.Equal(1, rep.Breakdown["extWish"]);
        Assert.Equal(plain.Hard + 1, rep.Hard);
        Assert.Equal(plain.WeightedScore + MirrorKeys.WeightOf("pref"), rep.WeightedScore);
        var expected = plain.Breakdown.ToDictionary(kv => kv.Key, kv => kv.Value);
        expected["extWish"] = 1;
        Assert.Equal((IReadOnlyDictionary<string, int>)expected, rep.Breakdown);
        Assert.Equal((long)rep.Hard, new Evaluator(new Problem(st)).FullEvalParts(board)[0]);
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

/// <summary>外部レビュー DATA-01〜03・NEW-04・OLD-01〜03（Kotlin <c>ReviewExtWishFollowTest</c> の移植）。</summary>
public class ReviewExtWishFollowTest
{
    // 0=休 1=A 2=D。職員 A,B,C。B に「D 禁止」（1〜2 日）。
    private static MagiState State(IReadOnlyList<ExtWish>? ext = null) => MinimalState.Build(
        startDate: "2026-07-01", endDate: "2026-07-03",
        shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("A", "A", "1", ""), new("D", "D", "1", "") },
        staffList: new List<Staff> { new("A", 0), new("B", 0), new("C", 0) },
        schedule: new List<IReadOnlyList<int>> { new[] { 1, 0, 0 }, new[] { 2, 2, 0 }, new[] { 0, 1, 1 } })
        with { ExtWishes = ext ?? new[] { new ExtWish(1, new[] { "2026-07-01", "2026-07-02" }, new[] { "D" }) } };
    private static int[][] Grid(MagiState st) => st.Schedule.ToIntArray2D();

    [Fact]
    public void MoveStaffKeepsTheExtWishOnTheSamePerson()
    {
        var st = State();
        var r = Ws1Ops.MoveStaff(st, Grid(st), 1, -1);
        Assert.Equal("B", r.State.StaffList[0].Name);
        Assert.Equal(new[] { 0 }, r.State.ExtWishes!.Select(e => e.Staff));
    }

    [Fact]
    public void RemoveStaffDropsItsExtWishAndShiftsTheRest()
    {
        var st = State(new[] { new ExtWish(1, new[] { "2026-07-01" }, new[] { "D" }), new ExtWish(2, new[] { "2026-07-02" }, new[] { "A" }) });
        var r = Ws1Ops.RemoveStaff(st, Grid(st), 1);
        Assert.Equal(new[] { 1 }, r.State.ExtWishes!.Select(e => e.Staff));
        Assert.Equal(new[] { "A" }, r.State.ExtWishes![0].Shifts);
    }

    [Fact]
    public void RenamingAShiftFollowsIntoExtWishesAndRemovingDropsIt()
    {
        var st = State();
        var renamed = Ws1Ops.EditShift(st, 2, "D", "NEW", "1", "", false);
        Assert.Equal(new[] { "NEW" }, renamed.ExtWishes![0].Shifts);
        Assert.Empty(Ws1Ops.RemoveShift(st, Grid(st), 2).State.ExtWishes!);
    }

    [Fact]
    public void ShrinkingThePeriodDropsDaysOutside()
    {
        var st = State();
        Assert.Equal(new[] { "2026-07-01" }, Ws1Ops.ResizeDays(st, Grid(st), 1).State.ExtWishes![0].Days);
    }

    [Fact]
    public void UnionWithExistingBansMustLeaveAPlaceableShift()
    {
        var st = State(new[] { new ExtWish(1, new[] { "2026-07-01" }, new[] { "休", "A" }) });
        Assert.Null(ExtWishRules.Sanitize(st, new ExtWish(1, new[] { "2026-07-01" }, new[] { "D" })).Saved);
        Assert.NotNull(ExtWishRules.Sanitize(st, new ExtWish(1, new[] { "2026-07-02" }, new[] { "D" })).Saved);
    }

    [Fact]
    public void FingerprintSeparatesNeedDay1FromNeedDay2AndSeesExtWishes()
    {
        var a = State(Array.Empty<ExtWish>()) with { NeedDay1 = new Dictionary<string, string> { ["1,0"] = "1" } };
        var b = State(Array.Empty<ExtWish>()) with { NeedDay2 = new Dictionary<string, string> { ["1,0"] = "1" } };
        Assert.NotEqual(StateFingerprint.Of(a), StateFingerprint.Of(b));
        Assert.NotEqual(StateFingerprint.Of(State(Array.Empty<ExtWish>())), StateFingerprint.Of(State()));
    }

    [Fact]
    public void CsvImportsRefuseToGuessBetweenSameNameStaff()
    {
        var st = State(Array.Empty<ExtWish>()) with
        {
            StaffList = new List<Staff> { new("同名", 0), new("同名", 0), new("C", 0) },
            Wishes = new Dictionary<string, int> { ["2,0"] = 1 },
        };
        var w = WishesCsvIO.Parse("氏名,日,希望シフト\n同名,1,D\n", st)!;
        Assert.Equal(1, w.Rejected); Assert.Equal(0, w.Accepted);
        Assert.StartsWith("同姓同名", w.Samples[0]);
        var groups2 = st with
        {
            Groups = new List<Group> { new("G0", "G0"), new("H", "H") },
            GroupShift = new List<IReadOnlyList<int>> { new[] { 1, 1, 1 }, new[] { 1, 1, 1 } },
            GroupShiftApt = new List<IReadOnlyList<string>> { new[] { "", "", "" }, new[] { "", "", "" } },
        };
        Assert.Null(StaffCsvIO.Parse("氏名,グループ,スキル\n同名,H,\n", groups2));
        var up = StaffCsvIO.ParseUpsert("氏名,グループ,スキル\n同名,H,\n", groups2, Grid(groups2));
        Assert.True(up is null || (up.Updated == 0 && up.Added == 0 && up.AmbiguousNames!.SequenceEqual(new[] { "同名" })));
    }
}
