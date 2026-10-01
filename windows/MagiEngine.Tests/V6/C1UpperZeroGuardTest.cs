using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>[3.507.0] C1 研磨の直接移動も上限 0（MayPlace=false）の (職員,シフト) へ置かない（Kotlin側 C1UpperZeroGuardTest.kt と同型）。</summary>
public class C1UpperZeroGuardTest
{
    private static readonly int[] Rest = { 0, 0, 0 };

    private static MagiState State() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-03",
        shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("日勤", "D", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("s0", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        schedule: new List<IReadOnlyList<int>> { Rest.ToList() },
        wishes: new Dictionary<string, int>(),
        staffRange: new Dictionary<string, Range> { ["0,1"] = new("", "0") },
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row> { new("3", "D", "1") }, cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(), cons41: new List<C41Row>(), cons42: new List<C42Row>());

    private static int[][] Board(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();

    private static void AssertUntouched(V6HotfixPasses.CyclicSwapResult r)
    {
        Assert.Equal(Rest, r.NewSchedule[0]);
        Assert.Equal(0, r.Applied);
    }

    [Fact]
    public void FixtureHasC1DeficitAndUpperZero()
    {
        var st = State();
        Assert.False(new Problem(st).MayPlace(0, 1));
        Assert.Equal(1, UnifiedViolationChecker.Check(st, Board(st)).Breakdown.GetValueOrDefault("c1", 0));
    }

    [Fact]
    public void IndexChainRepairDoesNotPlaceOnUpperZero()
    {
        var st = State();
        AssertUntouched(C1RepairOperators.IndexChainRepair(st, Board(st)));
    }

    [Fact]
    public void WindowPolishDoesNotPlaceOnUpperZero()
    {
        var st = State();
        AssertUntouched(C1RepairOperators.SelfRelocateAndSameDaySwap(st, Board(st)));
    }

    [Fact]
    public void BeamPolishDoesNotPlaceOnUpperZero()
    {
        var st = State();
        Assert.Equal(Rest, C1RepairOperators.WideBeam(st, Board(st)).NewSchedule[0]);
    }
}
