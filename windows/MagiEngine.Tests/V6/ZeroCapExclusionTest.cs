using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Kotlin <c>ZeroCapExclusionTest</c>（3.507.0）の移植。個人上限 0 のシフトは最適化器が置かない（MayPlace）。評価・表示は不変。
/// 盤面: 休/A、A は毎日 1 名必要。X は A 上限 0、Y/Z は制限なし。入力は X が全日 A（上限超過 4 だが被覆は満たす）。
/// </summary>
public class ZeroCapExclusionTest
{
    private static MagiState State(IReadOnlyDictionary<string, int>? wishes = null, IReadOnlyDictionary<string, Range>? extraRange = null)
    {
        var range = new Dictionary<string, Range> { ["0,1"] = new("0", "0") };
        if (extraRange is not null) foreach (var kv in extraRange) range[kv.Key] = kv.Value;
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-04",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "") },
            groups: new List<Group> { new("G0", "G0") },
            staffList: new List<Staff> { new("X", 0), new("Y", 0), new("Z", 0) }, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 }, new List<int> { 0, 0, 0, 0 }, new List<int> { 0, 0, 0, 0 } },
            wishes: wishes ?? new Dictionary<string, int>(),
            staffRange: range,
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
    }

    private static int[][] Work(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();
    private static int CountA(int[][] sched, int i) => sched[i].Count(k => k == 1);
    private static void NoOpProgress(string phase, ViolationReport? rep, long iters, long elapsed) { }

    [Fact]
    public void MayPlaceExcludesCappedShiftButCanDoAndUiListsAreUnchanged()
    {
        var p = ScheduleUtil.CachedProblem(State(extraRange: new Dictionary<string, Range> { ["1,0"] = new("0", "0") }));   // Y の休も上限 0（休は除外しない）
        Assert.True(p.CanDo(0, 1)); Assert.False(p.MayPlace(0, 1));
        Assert.True(p.MayPlace(0, 0)); Assert.True(p.MayPlace(1, 1));
        Assert.True(p.MayPlace(1, 0));
        Assert.Equal(new[] { 0 }, p.AllowedShiftsForStaff(0));
        Assert.Equal(new[] { 0, 1 }, p.CanDoShiftsForStaff(0));
        Assert.Equal(new[] { 1, 2 }, p.StaffForShift[1]);
        Assert.Equal(new[] { 0, 1, 2 }, p.StaffForShift[0]);
    }

    [Fact]
    public void CheckerStillCountsCappedCellsAsHighNotHard()
    {
        var s = State();
        var r = UnifiedViolationChecker.Check(s, Work(s));
        Assert.Equal(0, r.Hard);
        Assert.Equal(4, r.Breakdown["high"]);
        Assert.Equal(0, r.Breakdown.TryGetValue("groupViol", out var gv) ? gv : 0);
    }

    [Fact]
    public void EntryHardeningClearsCappedCellsUnlessWishLockedToThatShift()
    {
        var s = State(wishes: new Dictionary<string, int> { ["0,2"] = 1 });   // X の 3 日目は A を希望（希望が優先＝残す）
        var input = Work(s);
        var outSched = V6NativeOptimizer.Hf66DataHardening(s, input, "test");
        Assert.Equal(new[] { 0, 0, 1, 0 }, outSched[0]);
        var (cleared, n) = V6NativeOptimizer.ClearCappedCells(s, input);
        Assert.Equal(3, n);
        Assert.Equal(new[] { 0, 0, 1, 0 }, cleared[0]);
        Assert.Equal(new[] { 1, 1, 1, 1 }, input[0]);
    }

    [Fact]
    public async Task OptimizerMovesCoverageOffTheCappedStaff()
    {
        var s = State();
        var r = await V6NativeOptimizer.Optimize(s, Work(s), new V6OptimizerOptions(Algorithm: V6Algorithm.V5, TotalBudgetSec: 2, Workers: 1,
            SoftPolish: false, Restarts: 0, Seed: 7L, PostPolish: false), onProgressRaw: NoOpProgress);
        Assert.Equal(0, CountA(r.Schedule, 0));
        Assert.Equal(0, UnifiedViolationChecker.Check(s, r.Schedule).Hard);
    }

    [Fact]
    public async Task HandleOptimizeReturnsZeroCappedCellsAndDoesNotRevertToInput()
    {
        var s = State();
        var res = await V6FinalPort.HandleOptimize(s, secondsRaw: 2, workers: 1, requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, onProgress: NoOpProgress);
        Assert.Equal(0, CountA(res.Schedule, 0));
        Assert.Equal(0, res.Report.Hard);
        Assert.Contains(res.Logs, l => l.Tag == "CapZero" && l.Message.Contains("4件"));
        Assert.Equal(new V6FinalPort.CapZeroNotice(4, 0, 4), res.CapZero);
        Assert.DoesNotContain(res.Logs, l => l.Tag == "Sentinel");
    }

    /// <summary>上限0のセルだけが被覆を担っていた盤面: 入口で外すと必須が増え、ログと結果に前後の件数が出る。</summary>
    [Fact]
    public async Task CapZeroNoticeReportsHardRiseWhenCappedCellsCarriedCoverage()
    {
        var s = State(extraRange: new Dictionary<string, Range> { ["1,1"] = new("0", "0"), ["2,1"] = new("0", "0") });
        var res = await V6FinalPort.HandleOptimize(s, secondsRaw: 1, workers: 1, requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, onProgress: NoOpProgress);
        Assert.Equal(new V6FinalPort.CapZeroNotice(4, 0, 4), res.CapZero);
        Assert.Contains(res.Logs, l => l.Tag == "CapZero" && l.Message ==
            "入口: 個人上限0のセル4件を外しました（必須 0→4）。最適化は上限0の勤務を置かないため、この設定では入力の必須0件には戻れません");
        Assert.Equal("今の勤務表には個人の上限0のシフトが4件入っています。最適化は上限0の勤務を置かないため、この設定では必須0件まで戻れません（上限を見直すか、そのまま使ってください）",
            res.CapZero?.KeptNote());
        Assert.Null(new V6FinalPort.CapZeroNotice(4, 0, 0).KeptNote());
    }

    /// <summary>[Android 3.512.1/回帰] 本番の呼出元（EngineOptimizationService）は onProgress/cancellationToken を
    /// **位置引数**で渡す（Kotlin のトレーリングラムダに当たる）。quantitativeRangeEval を末尾以外へ足すと
    /// 実引数がずれる＝同じ呼出形をここで固定する。</summary>
    [Fact]
    public async Task HandleOptimizeAcceptsOnProgressAsTrailingLambdaLikeProductionCallers()
    {
        var s = State();
        var progressCalls = 0;
        var res = await V6FinalPort.HandleOptimize(s, 1, null, 1, false, V6Algorithm.V5, true,
            (phase, _, _, _) => { if (phase.Length > 0) Interlocked.Increment(ref progressCalls); },
            CancellationToken.None);
        Assert.True(progressCalls > 0);
        Assert.Equal(0, res.Report.Hard);
    }

    /// <summary>lastOptimizerSeed は静的なので、他の HandleOptimize と並列に走らない collection に置く。</summary>
    [Collection(nameof(HandleOptimizeSeedCollection))]
    public class Seed
    {
        [Fact]
        public async Task HandleOptimizeSeedReachesOptimizerOptionsAndDefaultKeepsZero()
        {
            var s = State();
            await V6FinalPort.HandleOptimize(s, secondsRaw: 1, workers: 1, requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, onProgress: NoOpProgress, seed: 42L);
            Assert.Equal(42L, V6FinalPort.lastOptimizerSeed);
            await V6FinalPort.HandleOptimize(s, secondsRaw: 1, workers: 1, requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, onProgress: NoOpProgress);
            Assert.Equal(0L, V6FinalPort.lastOptimizerSeed);
        }
    }

    [Fact]
    public async Task WishForCappedShiftStaysPinnedAndIsTheOnlyPlacement()
    {
        var s = State(wishes: new Dictionary<string, int> { ["0,1"] = 1 });
        var r = await V6NativeOptimizer.Optimize(s, Work(s), new V6OptimizerOptions(Algorithm: V6Algorithm.V5, TotalBudgetSec: 2, Workers: 1,
            SoftPolish: false, Restarts: 0, Seed: 7L, PostPolish: false), onProgressRaw: NoOpProgress);
        Assert.Equal(1, r.Schedule[0][1]);
        Assert.Equal(1, CountA(r.Schedule, 0));
    }

    /// <summary>上限0のセルを含む段は捨てずに、そのセルを外した盤面を候補に足す（入力・上限0なしの段は足さない）。</summary>
    [Fact]
    public void ClearedCapZeroStagesAddsTheStageWithCappedCellsRemoved()
    {
        var s = State();
        var p = ScheduleUtil.CachedProblem(s);
        var raw = Work(s);
        var cleared = V6NativeOptimizer.ClearCappedCells(s, raw).Schedule;
        ViolationReport Rep(int[][] b) => UnifiedViolationChecker.Check(s, b);
        var stages = new List<V6FinalPort.StageCandidate>
        {
            new("入力", cleared, Rep(cleared)),
            new("探索", raw, Rep(raw)),
            new("統合", cleared, Rep(cleared)),
        };
        var added = V6FinalPort.ClearedCapZeroStages(s, p, stages, false);
        Assert.Equal(new[] { "探索（上限0を外す）" }, added.Select(a => a.Label).ToArray());
        Assert.Equal(0, CountA(added[0].Sched, 0));
        Assert.Empty(p.CapZeroCells(added[0].Sched));
    }
}

[CollectionDefinition(nameof(HandleOptimizeSeedCollection), DisableParallelization = true)]
public class HandleOptimizeSeedCollection { }
