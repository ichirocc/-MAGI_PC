using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>DeterministicPostChainTest</c>（3.507.3）の移植。決定的モード＝時間でなく回数で止める。同じ入力・seed なら同じ盤面。</summary>
public class DeterministicPostChainTest
{
    private static MagiState State(string needA = "2") => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-08",
        shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", needA, "") }, groups: new List<Group> { new("G", "G") },
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

    // [postChainRunningKeepBest] チェーン内で「良い手→悪い手」の順に畳み込まれたとき、flag OFF は退行放置、
    // flag ON は悪い手の直前（＝良い手の盤面）へ巻き戻すことを、PostChain を直接駆動して確認する。
    private static V6HotfixPasses.CyclicSwapResult Result(int[][] schedule, ViolationReport report, string tag) =>
        new(schedule, report.Total, report.Total, 1, new List<MirrorLog> { new(tag: tag, message: $"{tag} 適用") });

    private static int[][] With(int[][] b, params (int I, int J, int V)[] cells)
    {
        var c = b.Select(r => r.ToArray()).ToArray();
        foreach (var (i, j, v) in cells) c[i][j] = v;
        return c;
    }

    private static bool Same(int[][] a, int[][] b) => a.Length == b.Length && a.Zip(b).All(t => t.First.SequenceEqual(t.Second));

    [Fact]
    public void RunningKeepBestOffKeepsChainRegression()
    {
        var s = State();
        var work0 = Work(s);
        var report0 = UnifiedViolationChecker.Check(s, work0);
        var improved = With(work0, (1, 1, 1));
        var improvedReport = UnifiedViolationChecker.Check(s, improved);
        var regressed = With(improved, (1, 0, 1));
        var regressedReport = UnifiedViolationChecker.Check(s, regressed);
        Assert.True(UnifiedViolationChecker.BetterReport(improvedReport, report0));
        Assert.True(UnifiedViolationChecker.BetterReport(improvedReport, regressedReport));

        var chainOff = new V6HotfixPasses.PostChain(_ => { }, work0, s, runningKeepBest: false, initialReport: report0);
        chainOff.Adopt(Result(improved, improvedReport, "Good"));
        chainOff.Adopt(Result(regressed, regressedReport, "Bad"));
        Assert.True(Same(chainOff.Work, regressed));
        Assert.DoesNotContain(chainOff.Logs, l => l.Message.Contains("チェーン内巻き戻しで不採用"));
    }

    [Fact]
    public void RunningKeepBestOnRevertsChainRegression()
    {
        var s = State();
        var work0 = Work(s);
        var report0 = UnifiedViolationChecker.Check(s, work0);
        var improved = With(work0, (1, 1, 1));
        var regressed = With(improved, (1, 0, 1));

        var chainOn = new V6HotfixPasses.PostChain(_ => { }, work0, s, runningKeepBest: true, initialReport: report0);
        chainOn.Adopt(Result(improved, UnifiedViolationChecker.Check(s, improved), "Good"));
        chainOn.Adopt(Result(regressed, UnifiedViolationChecker.Check(s, regressed), "Bad"));
        Assert.True(Same(chainOn.Work, improved));
        Assert.Contains(chainOn.Logs, l => l.Tag == "Bad" && l.Message.Contains("チェーン内巻き戻しで不採用"));
        Assert.Contains(chainOn.Logs, l => l.Tag == "Good" && !l.Message.Contains("チェーン内巻き戻しで不採用"));
    }

    // 外部レビュー N9: 盤面を変えなかったパス（最良と同点・同盤面）には巻き戻し印を付けない。
    [Fact]
    public void RunningKeepBestDoesNotMarkUnchangedPass()
    {
        var s = State();
        var work0 = Work(s);
        var report0 = UnifiedViolationChecker.Check(s, work0);
        var improved = With(work0, (1, 1, 1));
        var improvedReport = UnifiedViolationChecker.Check(s, improved);
        var chain = new V6HotfixPasses.PostChain(_ => { }, work0, s, runningKeepBest: true, initialReport: report0);
        chain.Adopt(Result(improved, improvedReport, "Good"));
        chain.Adopt(Result(With(improved), improvedReport, "Noop"));
        Assert.True(Same(chain.Work, improved));
        Assert.DoesNotContain(chain.Logs, l => l.Message.Contains("チェーン内巻き戻しで不採用"));
    }

    // 構造的 covU 床 > 0（必要人数 5 > 職員 3）の盤面では巻き戻さない＝必須件数が増えた試行はすべてこの形だった。
    [Fact]
    public void RunningKeepBestIsInactiveWhenStructuralHardFloorIsPositive()
    {
        var s = State(needA: "5");
        Assert.True(V6SanityPort.StructuralHardFloor(s) > 0);
        var work0 = Work(s);
        var report0 = UnifiedViolationChecker.Check(s, work0);
        var improved = With(work0, (1, 1, 1));
        var regressed = With(improved, (1, 0, 0), (0, 0, 0));
        var chain = new V6HotfixPasses.PostChain(_ => { }, work0, s, runningKeepBest: true, initialReport: report0);
        chain.Adopt(Result(improved, UnifiedViolationChecker.Check(s, improved), "Good"));
        chain.Adopt(Result(regressed, UnifiedViolationChecker.Check(s, regressed), "Bad"));
        Assert.True(Same(chain.Work, regressed));
    }

    // acceptTies: 同点の横移動は受け入れ、厳密な悪化は巻き戻す（既定は同点でも最良盤面へ戻す）。
    [Fact]
    public void RunningKeepBestAcceptTiesKeepsLateralMove()
    {
        var s = State();
        var work0 = Work(s);
        var report0 = UnifiedViolationChecker.Check(s, work0);
        var improved = With(work0, (1, 1, 1));
        var improvedReport = UnifiedViolationChecker.Check(s, improved);
        Assert.True(UnifiedViolationChecker.BetterReport(improvedReport, report0));
        // 同群・個人設定なしの 2 人の行を入れ替えた盤面＝報告は同点
        var lateral = new[] { improved[1].ToArray(), improved[0].ToArray(), improved[2].ToArray() };
        var lateralReport = UnifiedViolationChecker.Check(s, lateral);
        Assert.True(!UnifiedViolationChecker.BetterReport(lateralReport, improvedReport) && !UnifiedViolationChecker.BetterReport(improvedReport, lateralReport));
        Assert.False(Same(lateral, improved));
        int[][] RunChain(bool acceptTies, int[][] last, ViolationReport lastReport)
        {
            var c = new V6HotfixPasses.PostChain(_ => { }, work0, s, runningKeepBest: true, initialReport: report0, acceptTies: acceptTies);
            c.Adopt(Result(improved, improvedReport, "Good"));
            c.Adopt(Result(last, lastReport, "Last"));
            return c.Work;
        }
        Assert.True(Same(RunChain(false, lateral, lateralReport), improved));
        Assert.True(Same(RunChain(true, lateral, lateralReport), lateral));
        Assert.True(Same(RunChain(true, work0, report0), improved));
    }
}
