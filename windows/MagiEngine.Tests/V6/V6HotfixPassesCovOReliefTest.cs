using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>CovOReliefPolishTest.kt</c> の移植: 過剰セルの在勤者を需要 0 のシフト（B）へ退避し、希望固定と禁止連続は避ける。
/// X と Y は別グループ＝fair を外す（2 人群では退避 1 セルが fair を 2 件動かし、covO 1 件(10)と同点で keep-best に採られない。3.647.0 fair 5）。</summary>
public class V6HotfixPassesCovOReliefTest
{
    private static MagiState St(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyList<C3Row>? cons3n = null, IReadOnlyDictionary<string, Range>? staffRange = null, IReadOnlyList<C1Row>? cons1 = null)
    {
        var t = schedule[0].Count;
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: $"2026-08-{t:00}",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G", "G"), new("H", "H") },
            staffList: new List<Staff> { new("X", 0), new("Y", 1) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" }, new List<string> { "", "", "" } },
            schedule: schedule, wishes: wishes, cons3n: cons3n, cons1: cons1,
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

    /// <summary>回数固定（lo==hi）は他の研磨パスと同じく崩さない。X の休は 1 回に固定＝A→休 は c1 を 2 窓直して重み上は得でも採らず、受け皿 B へ退避する。</summary>
    [Fact]
    public void ExactPinIsNotDriftedEvenWhenTheWeightsPreferIt()
    {
        var s = St(new List<IReadOnlyList<int>> { new List<int> { 0, 1, 1, 1, 1 }, new List<int> { 1, 1, 0, 1, 1 } },
            staffRange: new Dictionary<string, Range> { ["0,0"] = new("1", "1") },
            cons1: new List<C1Row> { new("3", "休", "1") });
        var before = Sched(s);
        var r = V6HotfixPasses.ApplyCovOReliefPolish(s, Sched(s));
        Assert.True(r.Applied > 0, "退避は起きる");
        Assert.Equal(1, r.NewSchedule[0].Count(x => x == 0));   // X の休は固定の 1 回のまま
        Assert.False(V6SearchOperators.ExactPinRegression(new Problem(s), before, r.NewSchedule));
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
