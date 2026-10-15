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
            Shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "", "") },
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
        Assert.Equal(1, s.RangeOutStaff);   // s2 だけが上限超過
        Assert.Equal("変更：2人・4か所／希望どおり 2/2件", s.Line());
        Assert.Equal(-1, s.FamilyDeltas["pref"]); Assert.Equal(1, s.FamilyDeltas["high"]);   // s0 の希望が通り、s2 が上限超過
        Assert.Equal("減った：pref 1件", s.FamilyLine().Split('／')[0]);
        Assert.StartsWith("増えた：fair 6件・上限超過 1件", s.FamilyLine(label: k => k == "high" ? "上限超過" : k).Split('／')[1]);   // 加重順: fair +6(=30) が先頭、上限超過 +1(=25) が続く（3.647.0 fair 5。旧: 上限超過が先頭）
    }

    [Fact]
    public void FamilyLineOrdersByWeightedImpactAndReportsEmptySides()
    {
        var line = ChangeSummary.FamilyLine(new Dictionary<string, int> { ["weekly"] = 3, ["c1"] = -1, ["c3mn"] = -2, ["fair"] = 4 });
        Assert.Equal("減った：c3mn 2件・c1 1件／増えた：fair 4件・weekly 3件", line);
        Assert.Equal("減った：なし／増えた：なし", ChangeSummary.FamilyLine(new Dictionary<string, int>()));
    }

    [Fact]
    public void LineSaysAllWithinRangeOrFallsBackWhenStaffNotCounted()
    {
        var none = new Dictionary<string, int>();
        Assert.Equal("変更：0人・0か所／希望どおり 0/0件", new ChangeSummary(0, 0, 0, 0, true, none).Line());
        Assert.Equal("変更：0人・0か所／希望どおり 0/0件", new ChangeSummary(0, 0, 0, 0, false, none).Line());
    }
}
