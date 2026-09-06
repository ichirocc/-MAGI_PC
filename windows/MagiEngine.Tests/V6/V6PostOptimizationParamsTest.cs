using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[3.500.0 移植元] <c>RunPostOptimization</c> の Params 集約と PostChain ランナー化が採否を変えていないこと、
/// 退化した探索幅でも落ちず keep-best で悪化しないことを固定する（Kotlin <c>V6PostOptimizationParamsTest</c>）。</summary>
public class V6PostOptimizationParamsTest
{
    private static MagiState PinnedState() => MinimalState.Build(
        startDate: "2026-02-01", endDate: "2026-02-11",
        shifts: new List<Shift> { new("休み", "休", "1", "1"), new("X", "X", "1", "1"), new("Y", "Y", "1", "1") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2") },
        staffList: new List<Staff> { new("A", 0), new("B", 1), new("C", 2) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" }, new List<string> { "", "", "" }, new List<string> { "", "", "" } },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1 },
            new List<int> { 1, 1, 1, 1, 0, 0, 2, 2, 2, 2, 2 },
            new List<int> { 2, 2, 2, 2, 2, 2, 0, 0, 0, 0, 0 },
        },
        wishes: new Dictionary<string, int>(),
        staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new("4", "4"), ["0,2"] = new("2", "") },
        cons3n: new List<C3Row>());

    private static List<string> StableLogs(V6HotfixPasses.V6PostOptimizationResult r) =>
        r.Logs.Select(l => l.Message).Where(m => !m.Contains("ms") && !m.Contains("共同LNS")).ToList();

    [Fact]
    public void DefaultParamsMatchTheLegacyCallExactly()
    {
        var st = PinnedState();
        var sched = st.Schedule.ToIntArray2D();
        var legacy = V6HotfixPasses.RunPostOptimization(st, sched.Copy2D(), "t", seed: 7L);
        var withParams = V6HotfixPasses.RunPostOptimization(st, sched.Copy2D(), "t", seed: 7L, parameters: new V6HotfixPasses.PostOptimizationParams());
        for (var i = 0; i < legacy.Schedule.Length; i++) Assert.Equal(legacy.Schedule[i], withParams.Schedule[i]);
        Assert.Equal(legacy.Report.Hard, withParams.Report.Hard);
        Assert.Equal(legacy.Report.Total, withParams.Report.Total);
        Assert.Equal(StableLogs(legacy), StableLogs(withParams));
    }

    [Fact]
    public void DegenerateParamsDoNotCrashAndNeverWorsenTheBoard()
    {
        var st = PinnedState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        var p = new V6HotfixPasses.PostOptimizationParams(
            Hf80MaxCycles: 0, Hf67MaxSwaps: 0, Hf66MaxMoves: 0, MaxRounds: 0, CyclicSwapPasses: 0,
            WeeklyRebalancePasses: 0, AlternatingSweeps: 0, C1LnsMaxMs: 0L, PersonalLnsMaxMs: 0L, PassLogTopN: 0);
        var r = V6HotfixPasses.RunPostOptimization(st, sched.Copy2D(), "t", seed: 7L, parameters: p);
        Assert.True(r.Report.Hard <= before.Hard);
        Assert.True(r.Report.WeightedScore <= before.WeightedScore);
        Assert.Contains(r.Logs, l => l.Tag == "SoftPolishVerify");
        Assert.Contains(r.Logs, l => l.Tag == "POST");
    }
}
