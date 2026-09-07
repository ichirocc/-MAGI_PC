using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>DeterministicPostChainTest</c>（3.507.3）の移植。決定的モード＝時間でなく回数で止める。同じ入力・seed なら同じ盤面。</summary>
public class DeterministicPostChainTest
{
    private static MagiState State() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-08",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "2", "") }, groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("X", 0), new("Y", 0), new("Z", 0) }, use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } }, groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0, 1, 1, 1, 0 }, new List<int> { 0, 0, 1, 1, 0, 0, 1, 1 }, new List<int> { 1, 0, 0, 1, 1, 0, 0, 1 } },
        wishes: new Dictionary<string, int>(), staffRange: new Dictionary<string, MagiEngine.Model.Range>(),
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row> { new("3", "休", "1") }, cons2: new List<C2Row>(), cons3: new List<C3Row>(), cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
        cons41: new List<C41Row>(), cons42: new List<C42Row>());

    private static int[][] Work(MagiState s) => s.Schedule.Select(r => r.ToArray()).ToArray();

    private static V6HotfixPasses.V6PostOptimizationResult Run(V6HotfixPasses.PostOptimizationParams p)
    {
        var s = State();
        return V6HotfixPasses.RunPostOptimization(s, Work(s), "det", seed: 5L, deadlineMs: EngineClock.NowMs() + 10_000L, parameters: p);
    }

    [Fact]
    public void TwoRunsProduceTheSameBoard()
    {
        var p = new V6HotfixPasses.PostOptimizationParams(Deterministic: true);
        var a = Run(p); var b = Run(p);
        for (var i = 0; i < a.Schedule.Length; i++) Assert.Equal(a.Schedule[i], b.Schedule[i]);
        Assert.Equal(a.Report.WeightedScore, b.Report.WeightedScore);
    }

    [Fact]
    public void JointLnsStopsByEvaluationCount()
    {
        var s = State();
        var r = C1RepairOperators.JointLns(s, Work(s), config: new C1JointLnsPolish.Config(MaxEvaluations: 3, PatienceMs: 0L));
        Assert.Contains("評価回数上限3", r.Logs[0].Message);
    }

    [Fact]
    public void EvaluationCapIsIgnoredWhenZero()
    {
        var s = State();
        var r = C1RepairOperators.JointLns(s, Work(s), config: new C1JointLnsPolish.Config(MaxEvaluations: 0, MaxMillis: 500L, PatienceMs: 0L));
        Assert.DoesNotContain("評価回数上限", r.Logs[0].Message);
    }
}
