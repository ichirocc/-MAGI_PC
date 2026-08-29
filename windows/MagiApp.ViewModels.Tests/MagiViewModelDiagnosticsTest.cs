using MagiApp.ViewModels.Tests.TestSupport;
using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース6] <c>MagiViewModel.kt</c> の診断/レポート集約パイプライン
/// （<c>Analysis</c>/<c>AnalyzeParallelAsync</c>/<c>PushReportAsync</c>/<c>MakeUi</c>、および
/// <c>BoardKey</c>/<c>StateKey</c>/<c>SetPolishDiagnostics</c>/<c>CompressDiagLogs</c>）の検証。
/// Kotlin原本には専用テストが無い（<c>UiStateTest</c>/<c>MagiViewModelTest</c> と同じ経緯）。
///
/// このクラスは <see cref="Work.OptimizationRepository"/> の共有 static 状態に触れないため、
/// 直列コレクション（<see cref="TestSupport.OptimizationRepositoryStateCollection"/>）には属さない
/// （他クラスと並列に走ってよい）。
/// </summary>
public class MagiViewModelDiagnosticsTest
{
    // ===== BoardKey（純粋な多項式ハッシュ） =====

    [Fact]
    public void BoardKeyIsDeterministicForTheSameContent()
    {
        var a = new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 } };
        var b = new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 } };

        Assert.Equal(MagiViewModel.BoardKey(a), MagiViewModel.BoardKey(b));
    }

    [Fact]
    public void BoardKeyDiffersWhenAnySingleCellDiffers()
    {
        var a = new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 5 } };
        var b = new[] { new[] { 0, 1, 2 }, new[] { 3, 4, 9 } };

        Assert.NotEqual(MagiViewModel.BoardKey(a), MagiViewModel.BoardKey(b));
    }

    /// <summary>
    /// [Kotlin原本の式を独立に検算] <c>h = 1125899906842597L; for row for v: h = h * 31L + v</c>。
    /// この定数式は分割代入や「改善」で静かにドリフトしうるため、実際の formula を再現し数値で固定する。
    /// </summary>
    [Fact]
    public void BoardKeyMatchesTheDocumentedPolynomialHashFormula()
    {
        var schedule = new[] { new[] { 1, 2 }, new[] { 3 } };
        var expected = 1125899906842597L;
        expected = expected * 31L + 1;
        expected = expected * 31L + 2;
        expected = expected * 31L + 3;

        Assert.Equal(expected, MagiViewModel.BoardKey(schedule));
    }

    // ===== CompressDiagLogs =====

    [Fact]
    public void CompressDiagLogsReturnsShortListsUnchanged()
    {
        Assert.Empty(MagiViewModel.CompressDiagLogs(Array.Empty<string>()));
        Assert.Equal(new[] { "only" }, MagiViewModel.CompressDiagLogs(new[] { "only" }));
    }

    [Fact]
    public void CompressDiagLogsCollapsesOnlyConsecutiveDuplicates()
    {
        // "a" appears twice but is NOT adjacent both times → must not be merged across the gap.
        var lines = new[] { "a", "a", "b", "a" };
        var result = MagiViewModel.CompressDiagLogs(lines);

        Assert.Equal(new[] { "a  ×2", "b", "a" }, result);
    }

    [Fact]
    public void CompressDiagLogsDoesNotTruncateAtExactlyTheCap()
    {
        var lines = Enumerable.Range(0, 5).Select(i => $"line{i}").ToArray();
        var result = MagiViewModel.CompressDiagLogs(lines, cap: 5);

        Assert.Equal(5, result.Count);
        Assert.DoesNotContain(result, l => l.Contains("省略"));
    }

    [Fact]
    public void CompressDiagLogsTruncatesToHeadSeventyPercentPlusTailWithAnEllipsisLine()
    {
        var lines = Enumerable.Range(0, 10).Select(i => $"line{i}").ToArray();
        var result = MagiViewModel.CompressDiagLogs(lines, cap: 5); // head=3, tail=2

        Assert.Equal(6, result.Count); // 5 kept + 1 ellipsis line
        Assert.Equal(new[] { "line0", "line1", "line2" }, result.Take(3));
        Assert.Contains("中略", result[3]);
        Assert.Contains("5 行省略", result[3]); // 10 collapsed - 3 head - 2 tail = 5
        Assert.Equal(new[] { "line8", "line9" }, result.Skip(4));
    }

    // ===== StateKey（StateFingerprint への委譲） =====

    [Fact]
    public void StateKeyIsDeterministicAndSensitiveToStateChanges()
    {
        var a = MinimalState.Build(startDate: "2025-12-01");
        var b = MinimalState.Build(startDate: "2025-12-01");
        var c = MinimalState.Build(startDate: "2025-12-02");

        Assert.Equal(MagiViewModel.StateKey(a), MagiViewModel.StateKey(b));
        Assert.NotEqual(MagiViewModel.StateKey(a), MagiViewModel.StateKey(c));
    }

    // ===== AnalyzeParallelAsync =====

    [Fact]
    public async Task AnalyzeParallelAsyncOmitsCoverageAndForbiddenDiagnosesWhenNeitherIsPresent()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var report = UnifiedViolationChecker.Check(st, sched);

        var analysis = await MagiViewModel.AnalyzeParallelAsync(st, sched, report);

        Assert.Null(analysis.CoverageDiag);
        Assert.Null(analysis.ForbiddenDiag);
        Assert.NotNull(analysis.V6);
        Assert.NotNull(analysis.Sanity);
        Assert.Contains(analysis.V6Logs, l => l.StartsWith("[I] LoadDataBit:"));
        // RawDiagLogs is a superset that also folds in the mapped ViolationReport.Logs and the
        // (uncompressed) violation-debug lines — it must contain at least everything V6Logs has.
        foreach (var line in analysis.V6Logs) Assert.Contains(line, analysis.RawDiagLogs);
    }

    // ===== SetPolishDiagnostics + MakeUi: diagFresh gating =====

    [Fact]
    public async Task MakeUiExposesThePlateauDiagnosisOnlyWhenTheScheduleMatchesTheObservedOne()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var report = UnifiedViolationChecker.Check(st, sched);
        var analysis = await MagiViewModel.AnalyzeParallelAsync(st, sched, report);

        var plateau = new C1PlateauDiagnosis(RemainingC1: 3, Entries: Array.Empty<C1PlateauEntry>());
        var vm = new MagiViewModel { _state = st };

        // Fabricate a report whose breakdown carries a c1 > 0 (the checker itself won't produce
        // one for this trivial fixture — MakeUi's gate reads report.Breakdown directly).
        var reportWithC1 = report with { Breakdown = MergeBreakdown(report.Breakdown, "c1", 4) };

        vm.SetPolishDiagnostics(plateau, observedPinBlockedAttempts: 7, sched);
        vm.MakeUi(st, sched, reportWithC1, analysis);

        Assert.Same(plateau, vm.Ui.C1Plateau);
        Assert.Equal(7, vm.Ui.ObservedPinBlockedAttempts);

        // A different schedule ⇒ the observation no longer matches the board being displayed ⇒ mute.
        var otherSched = new[] { new[] { 1, 0 }, new[] { 0, 0 } };
        vm.MakeUi(st, otherSched, reportWithC1, analysis);

        Assert.Null(vm.Ui.C1Plateau);
        Assert.Equal(0, vm.Ui.ObservedPinBlockedAttempts);
        Assert.Empty(vm.Ui.PinTargets);
    }

    [Fact]
    public async Task MakeUiMutesThePlateauDiagnosisWhenC1IsZeroEvenIfTheScheduleIsFresh()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var report = UnifiedViolationChecker.Check(st, sched); // no c1 in the trivial fixture
        var analysis = await MagiViewModel.AnalyzeParallelAsync(st, sched, report);

        var plateau = new C1PlateauDiagnosis(RemainingC1: 3, Entries: Array.Empty<C1PlateauEntry>());
        var vm = new MagiViewModel { _state = st };
        vm.SetPolishDiagnostics(plateau, observedPinBlockedAttempts: 7, sched);

        vm.MakeUi(st, sched, report, analysis); // same schedule ⇒ "fresh" ⇒ but breakdown["c1"] is 0

        Assert.Null(vm.Ui.C1Plateau);
        // observedPinBlockedAttempts has no such gate in Kotlin — only the schedule/state freshness matters.
        Assert.Equal(7, vm.Ui.ObservedPinBlockedAttempts);
    }

    /// <summary>
    /// [フェーズ9 ピース6/教訓#30の実践] 当初案は存在しない <c>PinBlockAttribution.RecordManualForTest</c>
    /// を呼ぶ壊れた試験だった（ビルドすれば <c>CS1061</c> で必ず落ちる）。<see cref="PinBlockAttribution"/>
    /// は値を直接注入する手段を持たず、<c>Record(Problem, int[][], int[][])</c>（実際に
    /// <see cref="V6SearchOperators.ExactPinOffenders"/> を判定して記録する、本番と同一の経路）を
    /// 通してのみ記録できる。よってこの試験は<b>本物の Problem/before/after を作り</b>、
    /// <c>Record</c> を実際に呼んで検証する（Kotlin原本もこの機構専用のテストを持たないため、
    /// C#移植で新規に固定する）。
    ///
    /// 記録時（<c>pRec</c>）は職員0・職員1の両方をシフト1(A)で厳密ピン(lo==hi)にし、
    /// 「職員0だけが目標から遠ざかる手」を5回・「職員1だけが目標から遠ざかる手」を9回、それぞれ
    /// 独立に <c>Record</c> する（1回の呼出は<b>その時点で規約に違反している全ピンを同時に</b>記録する
    /// ため、別々の回数にするには片方だけが規約違反する before/after ペアを別々に用意する必要がある）。
    ///
    /// 表示時（<c>st</c>）は職員1のシフト1を<b>非ピン化</b>（lo=1,hi=3）する——
    /// <see cref="MagiViewModel"/> の <c>BuildPinTargets</c> は <c>pinBlocks.ByTarget()</c> の結果を
    /// 現在の <see cref="ScheduleUtil.CachedProblem"/> で<b>再検査</b>する独立した防御コードなので、
    /// 「記録時点ではピンだったが表示時点では外れた」対象を正しく除外することを、この非対称な
    /// attempts数（職員1=9 &gt; 職員0=5）で確かめる（単純な「試行回数が多い方が勝つ」ではなく、
    /// 現在の規約と厳密に照合していることの証明になる）。
    /// </summary>
    [Fact]
    public async Task MakeUiIncludesOnlyPinTargetsWhoseRangeIsCurrentlyExactlyPinned()
    {
        var stRec = MinimalState.Build(staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new Range("2", "2"), // pinned: lo == hi == 2
            ["1,1"] = new Range("4", "4"), // pinned (at record time): lo == hi == 4
        });
        var pRec = new Problem(stRec);

        // Both staff sit exactly on their pinned target (distance 0) in the "before" board.
        var before = new[]
        {
            new[] { 1, 1, 0, 0, 0, 0, 0 }, // staff0: count(shift1)=2 == lo0
            new[] { 1, 1, 1, 1, 0, 0, 0 }, // staff1: count(shift1)=4 == lo1
        };

        // Regress ONLY staff0 away from its pin (distance 0 -> 1); staff1 stays on target.
        // (Whether this actually registers as a regression is proven below via ByTarget()'s counts —
        // V6SearchOperators.ExactPinRegression itself is `internal` to MagiEngine and not visible from
        // this assembly, so we verify through the public Record()/ByTarget() surface instead.)
        var afterStaff0Regresses = new[]
        {
            new[] { 1, 1, 1, 0, 0, 0, 0 }, // staff0: count(shift1)=3, distance 1 > 0 -> offender
            new[] { 1, 1, 1, 1, 0, 0, 0 }, // staff1: unchanged, still on target
        };

        // Regress ONLY staff1 away from its pin; staff0 stays on target.
        var afterStaff1Regresses = new[]
        {
            new[] { 1, 1, 0, 0, 0, 0, 0 }, // staff0: unchanged, still on target
            new[] { 1, 1, 1, 1, 1, 0, 0 }, // staff1: count(shift1)=5, distance 1 > 0 -> offender
        };

        var pinBlocks = new PinBlockAttribution();
        for (var i = 0; i < 5; i++) pinBlocks.Record(pRec, before, afterStaff0Regresses);
        for (var i = 0; i < 9; i++) pinBlocks.Record(pRec, before, afterStaff1Regresses);
        Assert.Equal(14, pinBlocks.Attempts);

        var byTarget = pinBlocks.ByTarget();
        Assert.Equal(2, byTarget.Count);
        Assert.Contains(byTarget, t => t.Staff == 0 && t.Shift == 1 && t.Count == 5);
        Assert.Contains(byTarget, t => t.Staff == 1 && t.Shift == 1 && t.Count == 9);

        // Display time: staff1's shift1 range has since been widened (no longer lo==hi).
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new Range("2", "2"), // still pinned
            ["1,1"] = new Range("1", "3"), // no longer pinned: lo != hi
        });
        var sched = MinimalState.BuildSchedule();
        var report = UnifiedViolationChecker.Check(st, sched);
        var analysis = await MagiViewModel.AnalyzeParallelAsync(st, sched, report);

        var vm = new MagiViewModel { _state = st };
        vm.SetPolishDiagnostics(plateau: null, observedPinBlockedAttempts: 14, sched, pinBlocks);
        vm.MakeUi(st, sched, report, analysis);

        var targets = vm.Ui.PinTargets;
        Assert.Single(targets); // staff1's entry is filtered out — it is no longer pinned at display time.
        Assert.Equal(0, targets[0].Staff);
        Assert.Equal(1, targets[0].Shift);
        Assert.Equal(2, targets[0].PinnedCount);
        Assert.Equal(5, targets[0].Attempts);
    }

    // ===== MakeUi: field mapping smoke test =====

    [Fact]
    public async Task MakeUiMapsCoreFieldsFromTheStateAndReport()
    {
        var st = MinimalState.Build(startDate: "2025-12-01");
        var sched = MinimalState.BuildSchedule();
        var report = UnifiedViolationChecker.Check(st, sched);
        var analysis = await MagiViewModel.AnalyzeParallelAsync(st, sched, report);

        var vm = new MagiViewModel { _state = st };
        vm.MakeUi(st, sched, report, analysis);

        Assert.Equal(st.StaffCount, vm.Ui.Staff);
        Assert.Equal(st.DayCount, vm.Ui.Days);
        Assert.Equal(st.ShiftCount, vm.Ui.Shifts);
        Assert.Equal(st.GroupCount, vm.Ui.Groups);
        Assert.Equal(report.Hard, vm.Ui.BestHard);
        Assert.Equal(report.Soft, vm.Ui.BestSoft);
        Assert.Equal(report.Total, vm.Ui.TotalViolations);
        Assert.Equal(report.WeightedScore, vm.Ui.WeightedScore);
        Assert.Equal("2025-12-01", vm.Ui.StartDate);
        Assert.Equal(new[] { "職員A", "職員B" }, vm.Ui.StaffNames);
        Assert.Equal(2, vm.Ui.Schedule.Count);
        // Every one of MirrorKeys.All must be present in Breakdown even when the checker never
        // reported that family (Kotlin: `emptyBreakdown + report.breakdown`).
        foreach (var key in MirrorKeys.All) Assert.True(vm.Ui.Breakdown.ContainsKey(key));
    }

    private static IReadOnlyDictionary<string, int> MergeBreakdown(
        IReadOnlyDictionary<string, int> baseDict, string key, int value)
    {
        var d = baseDict.ToDictionary(kv => kv.Key, kv => kv.Value);
        d[key] = value;
        return d;
    }
}
