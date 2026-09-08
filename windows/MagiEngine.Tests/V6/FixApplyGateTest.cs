using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>[Android 3.509.4 移植] 提案の適用直前に仮盤面で再評価し、改善しない・固定を崩す提案は入力を変えずに拒否する。</summary>
public class FixApplyGateTest
{
    private const int REST = 0, A = 1;

    private static MagiState State(IReadOnlyDictionary<string, int>? wishes = null, IReadOnlyDictionary<string, Range>? ranges = null) => new(
        StartDate: "2026-01-01", EndDate: "2026-01-02",
        Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "1", "1") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        Use2Patterns: true,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { REST, A }, new List<int> { REST, A } },
        Wishes: wishes ?? new Dictionary<string, int>(), StaffRange: ranges ?? new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    private static int[][] Sched(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();

    [Fact]
    public void ImprovingOpsAreAppliedToACopy()
    {
        var st = State(); var s = Sched(st);
        var r = FixApplyGate.Apply(st, s, new[] { new FixCell(0, 0, A) });
        Assert.True(r.Applied);
        Assert.True(r.After!.Hard < r.Before.Hard);
        Assert.Equal(A, r.Schedule![0][0]); Assert.Equal(REST, s[0][0]);
    }

    [Fact]
    public void NonImprovingAndPinBreakingOpsAreRejected()
    {
        var st = State(); var s2 = Sched(st); s2[0][0] = A;
        var same = FixApplyGate.Apply(st, s2, new[] { new FixCell(0, 0, A) });
        Assert.False(same.Applied); Assert.Equal(new[] { A, A }, s2[0]);
        var locked = State(wishes: new Dictionary<string, int> { ["0,0"] = REST });
        Assert.False(FixApplyGate.Apply(locked, Sched(locked), new[] { new FixCell(0, 0, A) }).Applied);
        var pinned = State(ranges: new Dictionary<string, Range> { ["0,1"] = new("1", "1") });
        Assert.False(FixApplyGate.Apply(pinned, Sched(pinned), new[] { new FixCell(0, 0, A) }).Applied);
    }
}
