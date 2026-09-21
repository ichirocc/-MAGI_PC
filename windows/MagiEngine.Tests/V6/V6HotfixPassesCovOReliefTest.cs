using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>CovOReliefPolishTest.kt</c> の移植: 過剰セルの在勤者を需要 0 のシフト（B）へ退避し、希望固定と禁止連続は避ける。</summary>
public class V6HotfixPassesCovOReliefTest
{
    private static MagiState St(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyList<C3Row>? cons3n = null, IReadOnlyDictionary<string, Range>? staffRange = null)
    {
        var t = schedule[0].Count;
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: $"2026-08-{t:00}",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("X", 0), new("Y", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: schedule, wishes: wishes, cons3n: cons3n,
            staffRange: staffRange ?? new Dictionary<string, Range> { ["0,0"] = new("0", "0"), ["1,0"] = new("0", "0") });
    }
    private static int[][] Sched(MagiState s) => s.Schedule.ToIntArray2D();

    [Fact]
    public void SurplusWorkerIsMovedToDemandlessShift()
    {
        var s = St(new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } });
        var r = V6HotfixPasses.ApplyCovOReliefPolish(s, Sched(s));
        Assert.Equal(1, r.BeforeCovO); Assert.Equal(0, r.AfterCovO); Assert.Equal(1, r.Applied);
        Assert.Equal(1, r.NewSchedule.Count(row => row[0] == 2));
        Assert.Equal(1, r.NewSchedule.Count(row => row[0] == 1));
        Assert.Contains("採用1回", r.Logs.Single().Message);
    }

    [Fact]
    public void WishLockedSurplusIsLeftAlone()
    {
        var s = St(new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } },
            wishes: new Dictionary<string, int> { ["0,0"] = 1, ["1,0"] = 1 });
        var r = V6HotfixPasses.ApplyCovOReliefPolish(s, Sched(s));
        Assert.Equal(0, r.Applied); Assert.Equal(1, r.AfterCovO);
        Assert.Contains("希望固定", r.Logs.Single().Message);
    }

    [Fact]
    public void ForbiddenRunBlocksTheMoveAndTheOtherWorkerMoves()
    {
        var s = St(new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 0 } },
            cons3n: new List<C3Row> { new(new List<string> { "B", "A" }) },
            staffRange: new Dictionary<string, Range> { ["0,0"] = new("0", "0"), ["1,0"] = new("1", "1") });
        var r = V6HotfixPasses.ApplyCovOReliefPolish(s, Sched(s));
        Assert.Equal(1, r.Applied);
        Assert.Equal(1, r.NewSchedule[0][0]); Assert.Equal(1, r.NewSchedule[0][1]);
        Assert.Equal(2, r.NewSchedule[1][0]);
    }
}
