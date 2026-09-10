using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range —
// see the same alias pattern already established in TestSupport/MinimalState.cs.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ9] 職員／シフト種別、グループの並び替え（<see cref="Ws1Ops.MoveStaff"/> /
/// <see cref="Ws1Ops.MoveShift"/> / <see cref="Ws1Ops.MoveGroup"/>、<c>Ws1OpsMoveTest.kt</c> の逐語移植）。
/// 不変条件は「index で保存しているもの（勤務表・希望・個人の回数・日別必要人数・担当可否・群目標・
/// 職員の所属）が全部追従し、記号で参照するもの（制約行・表示色）は触らない」こと。端の並び替えは
/// 同じ state を返す。
/// </summary>
public class Ws1OpsMoveTest
{
    // 休=0 / A=1 / B=2 の3シフト、s0(G0)・s1(G1)・s2(G0) の3職員、2日。
    private static MagiState State() => new MagiState(
        StartDate: "2026-07-01", EndDate: "2026-07-02",
        Shifts: new List<Shift> { new("休み", "休", "", ""), new("A", "A", "1", ""), new("B", "B", "2", "") },
        Groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        StaffList: new List<Staff> { new("s0", 0, 1), new("s1", 1, -1), new("s2", 0, 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 }, new List<int> { 1, 0, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "3", "" }, new List<string> { "", "", "4" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 0 }, new List<int> { 2, 2 }, new List<int> { 0, 1 } },
        Wishes: new Dictionary<string, int> { ["0,0"] = 1, ["1,1"] = 2 },
        StaffRange: new Dictionary<string, Range> { ["0,1"] = new("1", "2"), ["1,2"] = new("", "5") },
        NeedDay1: new Dictionary<string, string> { ["1,0"] = "9" },
        NeedDay2: new Dictionary<string, string> { ["2,1"] = "8" },
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
        Cons3n: new List<C3Row> { new(new List<string> { "A", "B" }) },
        Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string> { ["A"] = "#112233" },
        Extras: MinimalState.NoExtras
    );

    private static int[][] Grid(MagiState st) => st.Schedule.Select(row => row.ToArray()).ToArray();

    [Fact]
    public void MoveStaffSwapsRowAndEveryStaffIndexedMap()
    {
        var st = State();
        var r = Ws1Ops.MoveStaff(st, Grid(st), 0, +1);
        Assert.Equal(new[] { "s1", "s0", "s2" }, r.State.StaffList.Select(s => s.Name));
        Assert.Equal(new[] { -1, 1, 0 }, r.State.StaffList.Select(s => s.SkillIdx)); // skillIdx は職員と一緒に動く
        Assert.Equal(new[] { new[] { 2, 2 }, new[] { 1, 0 }, new[] { 0, 1 } }, r.State.Schedule.Select(row => row.ToArray()));
        Assert.Equal(new[] { new[] { 2, 2 }, new[] { 1, 0 }, new[] { 0, 1 } }, r.Schedule);
        Assert.Equal(new Dictionary<string, int> { ["1,0"] = 1, ["0,1"] = 2 }, r.State.Wishes);
        Assert.Equal(new Dictionary<string, Range> { ["1,1"] = new("1", "2"), ["0,2"] = new("", "5") }, r.State.StaffRange);
        Assert.Equal(st.NeedDay1, r.State.NeedDay1); // シフト軸のものは不変
        // 上へ戻すと元どおり
        var back = Ws1Ops.MoveStaff(r.State, r.Schedule, 1, -1);
        Assert.Equal(st.StaffList, back.State.StaffList);
        Assert.Equal(st.Wishes, back.State.Wishes);
        Assert.Equal(st.Schedule, back.State.Schedule);
    }

    [Fact]
    public void MoveStaffAtTheEdgeIsANoOp()
    {
        var st = State();
        var g = Grid(st);
        Assert.Same(st, Ws1Ops.MoveStaff(st, g, 0, -1).State);
        Assert.Same(st, Ws1Ops.MoveStaff(st, g, 2, +1).State);
        Assert.Same(st, Ws1Ops.MoveStaff(st, g, 7, +1).State);
    }

    [Fact]
    public void MoveShiftSwapsColumnsValuesAndShiftIndexedKeysButNotSymbols()
    {
        var st = State();
        var r = Ws1Ops.MoveShift(st, Grid(st), 1, +1); // A(1) <-> B(2)
        Assert.Equal(new[] { "休", "B", "A" }, r.State.Shifts.Select(s => s.Kigou));
        Assert.Equal(new[] { new[] { 1, 0, 1 }, new[] { 1, 1, 0 } }, r.State.GroupShift.Select(row => row.ToArray()));
        Assert.Equal(new[] { new[] { "", "", "3" }, new[] { "", "4", "" } }, r.State.GroupShiftApt.Select(row => row.ToArray()));
        Assert.Equal(new[] { new[] { 2, 0 }, new[] { 1, 1 }, new[] { 0, 2 } }, r.State.Schedule.Select(row => row.ToArray()));
        Assert.Equal(new Dictionary<string, int> { ["0,0"] = 2, ["1,1"] = 1 }, r.State.Wishes);
        Assert.Equal(new Dictionary<string, Range> { ["0,2"] = new("1", "2"), ["1,1"] = new("", "5") }, r.State.StaffRange);
        Assert.Equal(new Dictionary<string, string> { ["2,0"] = "9" }, r.State.NeedDay1);
        Assert.Equal(new Dictionary<string, string> { ["1,1"] = "8" }, r.State.NeedDay2);
        Assert.Equal(st.Cons3n, r.State.Cons3n); // 記号参照は不変
        Assert.Equal(st.ShiftColors, r.State.ShiftColors);
        Assert.Equal(0, ScheduleUtil.RestShiftIndex(r.State)); // 休の解決は記号なので位置に依らない
        var back = Ws1Ops.MoveShift(r.State, r.Schedule, 2, -1);
        // MagiState は record だが List/Dictionary の各要素は参照等価のため、往復確認はフィールド単位で行う
        // （MagiState.cs の doc comment「fixture round-trip tests compare field-by-field instead」のとおり）。
        Assert.Equal(st.Shifts, back.State.Shifts);
        Assert.Equal(st.GroupShift.Select(row => row.ToArray()), back.State.GroupShift.Select(row => row.ToArray()));
        Assert.Equal(st.GroupShiftApt.Select(row => row.ToArray()), back.State.GroupShiftApt.Select(row => row.ToArray()));
        Assert.Equal(st.Schedule, back.State.Schedule);
        Assert.Equal(st.Wishes, back.State.Wishes);
        Assert.Equal(st.StaffRange, back.State.StaffRange);
        Assert.Equal(st.NeedDay1, back.State.NeedDay1);
        Assert.Equal(st.NeedDay2, back.State.NeedDay2);
    }

    [Fact]
    public void MoveShiftAtTheEdgeIsANoOp()
    {
        var st = State();
        var g = Grid(st);
        Assert.Same(st, Ws1Ops.MoveShift(st, g, 0, -1).State);
        Assert.Same(st, Ws1Ops.MoveShift(st, g, 2, +1).State);
    }

    [Fact]
    public void MoveGroupSwapsRowsAndStaffGroupIdxButNotScheduleOrWishes()
    {
        var st = State();
        var ns = Ws1Ops.MoveGroup(st, 0, +1); // G0(0) <-> G1(1)
        Assert.Equal(new[] { "G1", "G0" }, ns.Groups.Select(g => g.Name));
        Assert.Equal(new[] { new[] { 1, 0, 1 }, new[] { 1, 1, 0 } }, ns.GroupShift.Select(row => row.ToArray()));
        Assert.Equal(new[] { new[] { "", "", "4" }, new[] { "", "3", "" } }, ns.GroupShiftApt.Select(row => row.ToArray()));
        Assert.Equal(new[] { 1, 0, 1 }, ns.StaffList.Select(s => s.GroupIdx)); // 所属していた群番号が追従
        Assert.Equal(new[] { 1, -1, 0 }, ns.StaffList.Select(s => s.SkillIdx)); // skillIdx は無関係
        Assert.Equal(st.Schedule, ns.Schedule); // 勤務表・希望は職員行基準のため無変化
        Assert.Equal(st.Wishes, ns.Wishes);
        Assert.Equal(st.StaffRange, ns.StaffRange);
        Assert.Equal(st.Cons3n, ns.Cons3n); // 記号参照は不変
        var back = Ws1Ops.MoveGroup(ns, 1, -1);
        // フィールド単位で確認（理由は MoveShift の往復確認と同じ）。
        Assert.Equal(st.Groups, back.Groups);
        Assert.Equal(st.GroupShift.Select(row => row.ToArray()), back.GroupShift.Select(row => row.ToArray()));
        Assert.Equal(st.GroupShiftApt.Select(row => row.ToArray()), back.GroupShiftApt.Select(row => row.ToArray()));
        Assert.Equal(st.StaffList, back.StaffList);
    }

    [Fact]
    public void MoveGroupAtTheEdgeIsANoOp()
    {
        var st = State();
        Assert.Same(st, Ws1Ops.MoveGroup(st, 0, -1));
        Assert.Same(st, Ws1Ops.MoveGroup(st, 1, +1));
    }
}
