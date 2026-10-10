using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>EjectionChainPipelineTest</c> の移植。予察で当たりが無ければ深い探索をしない（不変条件）。</summary>
public class EjectionChainPipelineTest
{
    /// <summary>2 人×3 日。s0 は夜勤だけ・s1 は日勤だけで、どちらも c1（3 日に日勤・夜勤を各 1 以上）を欠く。同日の入れ替えで直る。</summary>
    private static MagiState SwapState(IReadOnlyList<IReadOnlyList<int>>? schedule = null) => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-03",
        shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("日勤", "D", "1", ""), new("夜勤", "N", "1", "") },
        groups: new List<Group> { new("G0", "G0") }, staffList: new List<Staff> { new("s0", 0), new("s1", 0) }, use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }, groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: schedule ?? new List<IReadOnlyList<int>> { new List<int> { 2, 2, 2 }, new List<int> { 1, 1, 1 } },
        wishes: new Dictionary<string, int>(), staffRange: new Dictionary<string, MagiEngine.Model.Range>(),
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row> { new("3", "D", "1"), new("3", "N", "1") }, cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(), cons41: new List<C41Row>(), cons42: new List<C42Row>());

    /// <summary>1 人×3 日。日勤の必要 1 を毎日 s0 だけが満たすので、c1（夜勤 1 以上）を直すと必ず人員不足になる＝直す手順が無い。</summary>
    private static MagiState WallState() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-03",
        shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("日勤", "D", "1", ""), new("夜勤", "N", "", "") },
        groups: new List<Group> { new("G0", "G0") }, staffList: new List<Staff> { new("s0", 0) }, use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }, groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        wishes: new Dictionary<string, int>(), staffRange: new Dictionary<string, MagiEngine.Model.Range>(),
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row> { new("3", "N", "1") }, cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(), cons41: new List<C41Row>(), cons42: new List<C42Row>());

    private static int[][] Work(MagiState s) => s.Schedule.Select(r => r.ToArray()).ToArray();
    private static bool Same(int[][] a, int[][] b) => a.Length == b.Length && a.Zip(b).All(t => t.First.SequenceEqual(t.Second));

    private static V6HotfixPasses.CyclicSwapResult Run(MagiState st, EjectionChainPipeline.Focus focus, List<EjectionChainPipeline.Telemetry> tel) =>
        EjectionChainPipeline.Apply(st, Work(st), new EjectionChainPipeline.Config(focus, Deterministic: true), telemetry: tel);

    [Fact]
    public void ProbeHitIsCommittedAndDeepSearchRunsOnlyWithHits()
    {
        var st = SwapState();
        var rep0 = UnifiedViolationChecker.Check(st, Work(st));
        var tel = new List<EjectionChainPipeline.Telemetry>();
        var r = Run(st, EjectionChainPipeline.Focus.C1, tel);
        var t = Assert.Single(tel);
        Assert.True(t.ShallowHits + t.MidHits > 0, t.Line());
        Assert.True(t.DeepRan, t.Line());
        Assert.True(t.Committed > 0, t.Line());
        Assert.Equal("committed", t.EndReason);
        Assert.True(UnifiedViolationChecker.BetterReport(r.Report!, rep0));
        Assert.True(r.Report!.Hard <= rep0.Hard);
        Assert.Equal(UnifiedViolationChecker.Check(st, r.NewSchedule).WeightedScore, r.Report!.WeightedScore);
    }

    [Fact]
    public void ProbeMissSkipsTheDeepSearch()
    {
        var st = WallState();
        var s0 = Work(st);
        Assert.True((UnifiedViolationChecker.Check(st, s0).Breakdown.TryGetValue("c1", out var c1) ? c1 : 0) > 0);
        var tel = new List<EjectionChainPipeline.Telemetry>();
        var r = Run(st, EjectionChainPipeline.Focus.C1, tel);
        var t = Assert.Single(tel);
        Assert.True(t.Seeds > 0, t.Line());
        Assert.Equal(0, t.ShallowHits + t.MidHits);
        Assert.False(t.DeepRan, t.Line());
        Assert.Equal("probe_miss", t.EndReason);
        Assert.True(Same(r.NewSchedule, s0));
    }

    [Fact]
    public void NoResidualDoesNothing()
    {
        var st = SwapState(new List<IReadOnlyList<int>> { new List<int> { 1, 2, 1 }, new List<int> { 2, 1, 2 } });
        var s0 = Work(st);
        Assert.Equal(0, UnifiedViolationChecker.Check(st, s0).Breakdown.TryGetValue("c1", out var c1) ? c1 : 0);
        var tel = new List<EjectionChainPipeline.Telemetry>();
        var r = Run(st, EjectionChainPipeline.Focus.C1, tel);
        Assert.Equal("no_residual", Assert.Single(tel).EndReason);
        Assert.False(tel[0].DeepRan);
        Assert.True(Same(r.NewSchedule, s0));
    }

    [Fact]
    public void DeterministicRunsAreReproducible()
    {
        var st = SwapState();
        var a = Run(st, EjectionChainPipeline.Focus.BOTH, new List<EjectionChainPipeline.Telemetry>());
        var b = Run(st, EjectionChainPipeline.Focus.BOTH, new List<EjectionChainPipeline.Telemetry>());
        Assert.True(Same(a.NewSchedule, b.NewSchedule));
        var noMs = new System.Text.RegularExpressions.Regex(@"\d+ms");
        Assert.Equal(a.Logs.Select(l => noMs.Replace(l.Message, "ms")), b.Logs.Select(l => noMs.Replace(l.Message, "ms")));
    }

    // BOTH は C1→SOFT。SOFT 側の残差は c1 を除いた数（c1 は C1 側が受け持つ）。
    [Fact]
    public void BothRunsC1ThenSoftWithoutC1()
    {
        var st = SwapState();
        var tel = new List<EjectionChainPipeline.Telemetry>();
        Run(st, EjectionChainPipeline.Focus.BOTH, tel);
        Assert.Equal(new[] { "C1", "SOFT" }, tel.Select(t => t.FocusName));
        var soft = tel[1];
        var b = soft.Before!;
        Assert.Equal(b.Total - b.Hard - (b.Breakdown.TryGetValue("c1", out var c1) ? c1 : 0), soft.Residual);
        foreach (var t in tel) Assert.True(!t.DeepRan || t.ShallowHits + t.MidHits > 0, t.Line());
    }

    // 有効なとき、後処理に段「玉突きパイプライン」が入る（Kotlin は同じ位置の従来の玉突きを走らせないことも見る＝C# は従来の常時フルを持たない）。
    [Fact]
    public void PostChainRunsThePipelineInsteadOfTheOldChain()
    {
        var st = SwapState();
        var oldFocus = PolishGate.EjectionPipelineFocus;
        try
        {
            PolishGate.EjectionPipelineFocus = EjectionChainPipeline.Focus.C1;
            var r = V6HotfixPasses.RunPostOptimization(st, Work(st), "pipe", seed: 3L, deadlineMs: EngineClock.NowMs() + 10_000L,
                parameters: new V6HotfixPasses.PostOptimizationParams(Deterministic: true));
            Assert.Contains(r.StageRecords!, s => s.Key == "玉突きパイプライン");
            Assert.DoesNotContain(r.StageRecords!, s => s.Key == "C1玉突き連鎖");
        }
        finally
        {
            PolishGate.EjectionPipelineFocus = oldFocus;
        }
    }
}
