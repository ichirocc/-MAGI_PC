using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

public class ChangeSummaryTest
{
    [Fact]
    public void CountsChangedStaffCellsWishesAndRanges()
    {
        var st = new MagiState(
            StartDate: "2026-01-01", EndDate: "2026-01-03",
            Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0), new("s1", 0), new("s2", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: Enumerable.Range(0, 3).Select(_ => (IReadOnlyList<int>)new List<int> { 0, 0, 0 }).ToList(),
            Wishes: new Dictionary<string, int> { ["0,0"] = 1, ["1,2"] = 0 }, StaffRange: new Dictionary<string, Range> { ["2,1"] = new("", "1") },
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var before = new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 0 }, new[] { 0, 0, 0 } };
        var after = new[] { new[] { 1, 1, 0 }, new[] { 0, 0, 0 }, new[] { 1, 1, 0 } };
        var s = ChangeSummary.Of(st, before, after, UnifiedViolationChecker.Check(st, after));
        Assert.Equal(2, s.ChangedStaff); Assert.Equal(4, s.ChangedCells);
        Assert.Equal(2, s.WishTotal); Assert.Equal(2, s.WishKept);
        Assert.False(s.RangeAllOk);
        Assert.Equal("変更 2人・4セル／希望 2/2／個人回数 範囲外あり", s.Line());
        Assert.Equal(-1, s.FamilyDeltas["pref"]); Assert.Equal(1, s.FamilyDeltas["high"]);   // s0 の希望が通り、s2 が上限超過
        Assert.Equal("改善 pref -1（重み 9000）", s.FamilyLine().Split('／')[0]);
        Assert.StartsWith("悪化 上限超過 +1・", s.FamilyLine(k => k == "high" ? "上限超過" : k).Split('／')[1]);   // 重み 45 が先頭、fair/weekly が続く
    }

    [Fact]
    public void FamilyLineOrdersByWeightedImpactAndReportsEmptySides()
    {
        var line = ChangeSummary.FamilyLine(new Dictionary<string, int> { ["weekly"] = 3, ["c1"] = -1, ["c3mn"] = -2, ["fair"] = 4 });
        Assert.Equal("改善 c3mn -2・c1 -1（重み 90）／悪化 fair +4・weekly +3（重み 7）", line);
        Assert.Equal("改善 なし／悪化 なし", ChangeSummary.FamilyLine(new Dictionary<string, int>()));
    }
}
