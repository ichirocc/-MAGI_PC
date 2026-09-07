using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>[Android 3.508.1 移植] HF80 戦略的振動: seed で決定的、keep-best、希望固定と置けないシフトを動かさない。</summary>
public class Hf80StrategicOscillationTest
{
    private const int REST = 0, A = 1, B = 2;

    private static MagiState State() => new(
        StartDate: "2026-01-01", EndDate: "2026-01-06",
        Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "1", "1"), new("B", "B", "1", "1") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: new List<Staff> { new("s0", 0), new("s1", 0), new("s2", 0) },
        Use2Patterns: true,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        Schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { REST, REST, REST, REST, REST, REST },
            new List<int> { REST, REST, REST, A, REST, REST },
            new List<int> { REST, REST, REST, REST, REST, REST },
        },
        Wishes: new Dictionary<string, int> { ["0,0"] = REST, ["1,3"] = A },
        StaffRange: new Dictionary<string, Range> { ["2,2"] = new("0", "0") },
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    private static int[][] Sched(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();
    private static List<List<int>> Rows(int[][] s) => s.Select(r => r.ToList()).ToList();

    [Fact]
    public void SameSeedGivesSameBoardAndNeverWorsensTheInput()
    {
        var st = State();
        var r1 = V6HotfixPasses.ApplyHF80StrategicOscillation(st, Sched(st), maxCycles: 3, seed: 7L);
        var r2 = V6HotfixPasses.ApplyHF80StrategicOscillation(st, Sched(st), maxCycles: 3, seed: 7L);
        Assert.Equal(Rows(r1.NewSchedule), Rows(r2.NewSchedule));
        Assert.Equal(3, r1.Cycles);
        var before = UnifiedViolationChecker.Check(st, Sched(st));
        var after = UnifiedViolationChecker.Check(st, r1.NewSchedule);
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.True(r1.Applied && after.Hard < before.Hard);
    }

    [Fact]
    public void PinnedWishesAndCappedShiftsAreNeverTouched()
    {
        var st = State();
        for (long seed = 1; seed <= 5; seed++)
        {
            var r = V6HotfixPasses.ApplyHF80StrategicOscillation(st, Sched(st), maxCycles: 3, seed: seed);
            Assert.Equal(REST, r.NewSchedule[0][0]);
            Assert.Equal(A, r.NewSchedule[1][3]);
            Assert.DoesNotContain(B, r.NewSchedule[2]);
        }
    }

    [Fact]
    public void ZeroCyclesIsANoOp()
    {
        var st = State();
        var r = V6HotfixPasses.ApplyHF80StrategicOscillation(st, Sched(st), maxCycles: 0, seed: 1L);
        Assert.False(r.Applied); Assert.Equal(0, r.Cycles);
        Assert.Equal(Rows(Sched(st)), Rows(r.NewSchedule));
    }
}
