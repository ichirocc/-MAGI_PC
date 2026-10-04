using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [Kotlin C1MoveARepairTest 1:1] <see cref="PolishGate.C1MoveARepair"/> 手A（同日交換）の相手 i2 が受け取るシフトで禁止連続ができる局面。
/// i=[A,A,A] は X(3日で1回以上) が不足。i2=[X,D,X] と day0 を交換すると i2=[A,D,X] で禁止 A→D が成立し、
/// 素の交換は HARD 増で棄却される。i2 の day1 の D を付け替えれば c1 だけが 1 減る。
/// </summary>
public class C1MoveARepairTest : IDisposable
{
    public void Dispose() => PolishGate.C1MoveARepair = false;

    private static MagiState State() => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-03",
        shifts: new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", ""), new("D", "D", "", ""), new("A", "A", "", "") },
        groups: new List<Group> { new("G0", "G0") }, staffList: new List<Staff> { new("i", 0), new("i2", 0) }, use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 3, 3, 3 }, new List<int> { 1, 2, 1 } },
        cons1: new List<C1Row> { new("3", "X", "1") },
        cons3n: new List<C3Row> { new(new[] { "A", "D" }) });

    [Fact]
    public void RepairedSwapReducesC1WithoutHardIncrease()
    {
        var st = State();
        var sched = st.Schedule.ToIntArray2D();
        var p = new Problem(st);
        var w = sched.Copy2D(); w[0][0] = 1; w[1][0] = 3;
        Assert.True(p.MakesForbiddenRun(w, 1, 0, 3));
        var before = UnifiedViolationChecker.Check(st, sched);
        PolishGate.C1MoveARepair = true;
        var res = V6HotfixPasses.ApplyC1WindowPolish(st, sched, maxPasses: 1);
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.Equal(0, after.Hard);
        Assert.True(after.Breakdown.GetValueOrDefault("c1", 0) < before.Breakdown.GetValueOrDefault("c1", 0));
        Assert.Contains(res.Logs, l => l.Message.Contains("手A禁止連続修復:試行1/採用1"));
    }

    [Fact]
    public void OffLeavesLogUnchanged()
    {
        var st = State();
        var res = V6HotfixPasses.ApplyC1WindowPolish(st, st.Schedule.ToIntArray2D(), maxPasses: 1);
        Assert.DoesNotContain(res.Logs, l => l.Message.Contains("手A禁止連続修復"));
    }
}
