using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;
// MagiEngine.Model.Range vs System.Range — same alias as MinimalState.cs, for the same reason.
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース8] <c>MagiViewModel.kt</c> のうち「盤面を直接編集する」「構造を直接編集する」系の
/// 全エディタ・クエリ（<c>MagiViewModel.Editing.cs</c> のクラスKDoc参照）の検証。Kotlin原本には
/// 専用テストが無い（<c>UiStateTest</c>/<c>MagiViewModelTest</c>/<c>MagiViewModelDiagnosticsTest</c>/
/// <c>MagiViewModelPersistenceTest</c> と同じ経緯、各クラスKDoc参照）。
///
/// このピースの全編集ガード（<c>OptimizeInFlight</c>）は <see cref="MagiViewModel.OptimizeInFlight"/>
/// 経由で <see cref="OptimizationRepository.Running"/> も読むため、<see cref="MagiViewModelTest"/>/
/// <see cref="MagiViewModelPersistenceTest"/> と同じ直列コレクションに属する。ファイルI/Oを伴わない
/// （<c>AutoSave</c> は <c>_hydrated==false</c> の既定状態では no-op）ため、<c>DataDir</c> の一時
/// ディレクトリ注入は不要——プレーンな <c>new MagiViewModel()</c> で構わない。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelEditingTest
{
    public MagiViewModelEditingTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    // ===================================================================
    // AllowedShiftsFor / GetSetupCounts / WishOutOfScopeCount
    // ===================================================================

    [Fact]
    public void RestShiftIndexResolvesTheRestShiftAndDefaultsToZeroWhenUnloaded()
    {
        Assert.Equal(0, new MagiViewModel().RestShiftIndex());
        var vm = new MagiViewModel { _state = ThreeShiftTwoGroupState() };
        Assert.Equal(ScheduleUtil.RestShiftIndex(ThreeShiftTwoGroupState()), vm.RestShiftIndex());
    }

    [Fact]
    public void AllowedShiftsForReturnsTheGroupsBucket()
    {
        var st = ThreeShiftTwoGroupState();
        var vm = new MagiViewModel { _state = st };

        Assert.Equal(new[] { 0, 1 }, vm.AllowedShiftsFor(0)); // 職員A: G0 canDo 休,A only
        Assert.Equal(new[] { 0, 1, 2 }, vm.AllowedShiftsFor(1)); // 職員B: G1 canDo all
    }

    [Fact]
    public void AllowedShiftsForReturnsEmptyWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Empty(vm.AllowedShiftsFor(0));
    }

    [Fact]
    public void MonthlyChecklistIsAllEmptyWithoutLoadedState()
    {
        var view = new MagiViewModel().MonthlyChecklist();
        Assert.Equal(new MagiViewModel.MonthlyChecklistView(0, 0, false, 0, 0), view);
    }

    [Fact]
    public void MonthlyChecklistCountsWishStaffDistinctlyAndNeedStdFromAnyNonBlankNeed1()
    {
        // 職員0 に希望 2 件・職員1 に 1 件 → 希望あり職員は 2 名（件数 3 ではない）。例外は needDay1/needDay2 の和集合キー数。
        var st = MinimalState.Build(
            shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "1", "") },
            wishes: new Dictionary<string, int> { ["0,0"] = 1, ["0,3"] = 1, ["1,2"] = 0 },
            needDay1: new Dictionary<string, string> { ["1,0"] = "2" },
            needDay2: new Dictionary<string, string> { ["1,0"] = "3", ["1,4"] = "1" });
        var vm = new MagiViewModel { _state = st };
        Assert.Equal(new MagiViewModel.MonthlyChecklistView(2, 2, true, 2, 0), vm.MonthlyChecklist());

        var noStd = new MagiViewModel { _state = MinimalState.Build() };
        Assert.False(noStd.MonthlyChecklist().NeedStdOk);
        Assert.Equal(0, noStd.MonthlyChecklist().WishStaff);
    }

    [Fact]
    public void StaffingRealityIsEmptyWithoutLoadedStateOrWithoutAnyDemand()
    {
        Assert.Empty(new MagiViewModel().StaffingReality());
        Assert.Empty(new MagiViewModel { _state = MinimalState.Build() }.StaffingReality());
    }

    [Fact]
    public void StaffingRealityCountsCanDoStaffAndSumsDailyDemandIncludingDayOverrides()
    {
        // G0 は 休/A のみ担当可（B は担当不可）。A: need1=1 ×7日、B: need1=2 ×7日 ＋ 3日目だけ例外で 3。
        var st = MinimalState.Build(
            shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "1", ""), new("B", "B", "2", "") },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 } },
            needDay1: new Dictionary<string, string> { ["2,2"] = "3" });
        var rows = new MagiViewModel { _state = st }.StaffingReality();

        Assert.Equal(2, rows.Count);
        Assert.Equal(new MagiViewModel.StaffingRealityRow("A", 2, 7, 1), rows[0]);
        Assert.Equal(new MagiViewModel.StaffingRealityRow("B", 0, 15, 3), rows[1]);
    }

    [Fact]
    public void GetSetupCountsReturnsZeroesWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        var counts = vm.GetSetupCounts();
        Assert.Equal(new MagiViewModel.SetupCounts(0, 0, 0, 0, 0, 0, 0, 0, false), counts);
    }

    [Fact]
    public void GetSetupCountsTalliesAllFieldsForTheDefaultFixture()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        var counts = vm.GetSetupCounts();
        Assert.Equal(new MagiViewModel.SetupCounts(7, 2, 2, 1, 0, 0, 0, 0, false), counts);
    }

    [Fact]
    public void GetSetupCountsSumsConstraintFamiliesAndRangesAndWishes()
    {
        var st = MinimalState.Build(
            wishes: new Dictionary<string, int> { ["0,0"] = 1 },
            needDay1: new Dictionary<string, string> { ["1,0"] = "2" },
            needDay2: new Dictionary<string, string> { ["1,0"] = "3" },
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("1", "2") },
            cons1: new List<C1Row> { new("5", "休", "2") },
            cons2: new List<C2Row> { new("A", "3") },
            cons41: new List<C41Row> { new("G0", "A", "1", "2") },
            cons42: new List<C42Row> { new("G0", "G0", "休", "A") },
            use2Patterns: true);
        var vm = new MagiViewModel { _state = st };

        var counts = vm.GetSetupCounts();

        Assert.Equal(1, counts.Wishes);
        Assert.Equal(2, counts.NeedDay); // needDay1 + needDay2, each keyed separately
        Assert.Equal(4, counts.Constraints); // cons1+cons2+cons41+cons42 (cons3 family all empty here)
        Assert.Equal(1, counts.Ranges);
        Assert.True(counts.Use2);
    }

    [Fact]
    public void WishOutOfScopeCountCountsOnlyWishesForShiftsTheStaffCannotDo()
    {
        var st = ThreeShiftTwoGroupState(wishes: new Dictionary<string, int>
        {
            ["0,0"] = 1, // 職員A(G0, canDo 休,A) wishes A — in scope
            ["1,0"] = 2, // 職員B(G1, canDo all) wishes B — in scope
            ["0,1"] = 2, // 職員A wishes B — OUT of scope (G0 cannot do B)
        });
        var vm = new MagiViewModel { _state = st };

        Assert.Equal(1, vm.WishOutOfScopeCount());
    }

    [Fact]
    public void WishOutOfScopeCountReturnsZeroWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Equal(0, vm.WishOutOfScopeCount());
    }

    // ===================================================================
    // ApplyWishes
    // ===================================================================

    [Fact]
    public void ApplyWishesAppliesInScopeWishesOnlyByDefault()
    {
        var st = ThreeShiftTwoGroupState(wishes: new Dictionary<string, int>
        {
            ["0,0"] = 1, // 職員A: 休->A, in scope
            ["1,0"] = 2, // 職員B: 休->B, in scope
            ["0,1"] = 2, // 職員A day1: out of scope (skipped entirely, not even counted as oos)
        });
        var vm = new MagiViewModel { _state = st, _currentSchedule = ThreeShiftSchedule() };

        vm.ApplyWishes(includeOutOfScope: false);

        Assert.Equal(1, vm._currentSchedule![0][0]);
        Assert.Equal(2, vm._currentSchedule![1][0]);
        Assert.Equal(0, vm._currentSchedule![0][1]); // untouched — out-of-scope wish was skipped
        Assert.False(vm.Ui.MessageIsError);
        Assert.True(vm.Ui.HasResult);
        // Ui.Message は末尾の RefreshCheck() が同期的に「違反チェック中…」へ即座に上書きするため、
        // ここでは（LogOp が RefreshCheck より前に書く）操作ログで「希望を反映」の件数を確認する。
        Assert.Contains("[I]", vm.Ui.OpLog[0]);
        Assert.Contains("希望を勤務表へ反映 2件", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void ApplyWishesIncludesOutOfScopeWishesWhenRequested()
    {
        var st = ThreeShiftTwoGroupState(wishes: new Dictionary<string, int>
        {
            ["0,0"] = 1,
            ["1,0"] = 2,
            ["0,1"] = 2, // out of scope, now included
        });
        var vm = new MagiViewModel { _state = st, _currentSchedule = ThreeShiftSchedule() };

        vm.ApplyWishes(includeOutOfScope: true);

        Assert.Equal(2, vm._currentSchedule![0][1]);
        Assert.Contains("[W]", vm.Ui.OpLog[0]); // oos>0 → warn level
        Assert.Contains("希望を勤務表へ反映 3件（担当外 1件含む）", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void ApplyWishesIsBlockedWhileAJobIsInFlight()
    {
        var st = ThreeShiftTwoGroupState(wishes: new Dictionary<string, int> { ["0,0"] = 1 });
        var vm = new MagiViewModel { _state = st, _currentSchedule = ThreeShiftSchedule() };
        vm.BeginBoardJob("勤務表をつくる");

        vm.ApplyWishes(false);

        Assert.Equal(0, vm._currentSchedule![0][0]); // unchanged
        Assert.True(vm.Ui.MessageIsError);
    }

    [Fact]
    public void ApplyWishesIsANoOpWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        vm.ApplyWishes(false); // must not throw
        Assert.Null(vm.Ui.Message);
    }

    // ===================================================================
    // CaptureAlternatives / ApplyAlternative
    // ===================================================================

    [Fact]
    public async Task CaptureAlternativesBuildsPerAltSummaries()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };
        var altA = MinimalState.BuildSchedule();
        var altB = MinimalState.BuildSchedule();
        altB[0][0] = 1;

        await vm.CaptureAlternatives(new[] { altA, altB });

        // altB は1セルだけ休->A へ動かした盤面。hard=0 だが fair/weekly のソフト偏差が発火する
        // （SetCellChangesTheCellAndTriggersARecheck と同じ理由・同じ実測値）。
        Assert.Equal(new[] { "案1: 必須=0 合計=0", "案2: 必須=0 合計=4" }, vm.Ui.Alternatives);
    }

    [Fact]
    public async Task CaptureAlternativesIsANoOpWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        await vm.CaptureAlternatives(new[] { MinimalState.BuildSchedule() });
        Assert.Empty(vm.Ui.Alternatives);
    }

    [Fact]
    public async Task ApplyAlternativeAppliesTheChosenScheduleAndRefreshesReport()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        var alt = MinimalState.BuildSchedule();
        alt[0][0] = 1;
        await vm.CaptureAlternatives(new[] { alt });

        vm.ApplyAlternative(0);
        Assert.NotNull(vm.LastApplyAlternativeTask);
        await vm.LastApplyAlternativeTask!;

        Assert.Equal(1, vm._currentSchedule![0][0]);
        Assert.Equal("他の案 1 を適用", vm.Ui.Message);
        Assert.False(vm.Ui.MessageIsError);
        Assert.True(vm.Ui.HasResult);
    }

    [Fact]
    public void ApplyAlternativeIgnoresOutOfRangeIndex()
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };

        vm.ApplyAlternative(0); // no CaptureAlternatives call — index 0 is out of range for an empty list

        Assert.Same(sched, vm._currentSchedule);
        Assert.Null(vm.LastApplyAlternativeTask);
    }

    [Fact]
    public async Task ApplyAlternativeIsBlockedWhileAJobIsInFlight()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        var alt = MinimalState.BuildSchedule();
        alt[0][0] = 1;
        await vm.CaptureAlternatives(new[] { alt });
        vm.BeginBoardJob("勤務表をつくる");

        vm.ApplyAlternative(0);

        Assert.Equal(0, vm._currentSchedule![0][0]); // unchanged
        Assert.True(vm.Ui.MessageIsError);
        Assert.Null(vm.LastApplyAlternativeTask);
    }

    // ===================================================================
    // SetCell / SetCells
    // ===================================================================

    [Fact]
    public async Task SetCellChangesTheCellAndTriggersARecheck()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetCell(0, 0, 1);
        Assert.NotNull(vm.LastRefreshCheckTask);
        await vm.LastRefreshCheckTask!;

        Assert.Equal(1, vm._currentSchedule![0][0]);
        Assert.Equal(1, vm.Ui.Schedule[0][0]);
        // hard=0 だが、7日全休の均一盤面から1セルだけ動かすと fair/weekly のソフト偏差が実際に発火する
        // （エンジンの正しい挙動——0を仮定しない、ScratchDebugBreakdown で実測済み: fair=2 weekly=2）。
        Assert.Equal("違反チェック完了: 必須=0 合計=4", vm.Ui.Message);
        Assert.Equal(1, vm.UndoStackCount);
    }

    [Fact]
    public void SetCellIsANoOpWhenTheValueIsUnchanged()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetCell(0, 0, 0); // already rest

        Assert.Equal(0, vm.UndoStackCount);
        Assert.Null(vm.LastRefreshCheckTask);
        Assert.Null(vm.Ui.Message);
    }

    [Fact]
    public void SetCellIsBlockedWhileAJobIsInFlight()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("勤務表をつくる");

        vm.SetCell(0, 0, 1);

        Assert.Equal(0, vm._currentSchedule![0][0]);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Null(vm.LastRefreshCheckTask);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 100)]
    public void SetCellIgnoresOutOfRangeIndices(int i, int j)
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetCell(i, j, 1);

        Assert.Equal(0, vm.UndoStackCount);
        Assert.Null(vm.LastRefreshCheckTask);
    }

    [Fact]
    public async Task SetCellsChangesMultipleCellsInASingleUndoStep()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetCells(new[] { (0, 0), (0, 1), (1, 0) }, 1);
        await vm.LastRefreshCheckTask!;

        Assert.Equal(1, vm._currentSchedule![0][0]);
        Assert.Equal(1, vm._currentSchedule![0][1]);
        Assert.Equal(1, vm._currentSchedule![1][0]);
        Assert.Equal(1, vm.UndoStackCount); // single PushUndo for the whole batch
    }

    [Fact]
    public void SetCellsSkipsCellsAlreadyAtTheTargetValue()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm._currentSchedule![0][0] = 1; // pre-set directly, bypassing SetCell

        vm.SetCells(new[] { (0, 0), (0, 1) }, 1);

        // Ui.Message は末尾の RefreshCheck() が同期的に「違反チェック中…」へ即座に上書きするため、
        // ここでは（LogOp が RefreshCheck より前に書く）操作ログで実際に変更したマス数を確認する。
        Assert.Contains("一括編集: 1マス → A", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void SetCellsIsANoOpWhenNoCellsChange()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetCells(new[] { (0, 0) }, 0); // already rest

        Assert.Equal(0, vm.UndoStackCount);
        Assert.Null(vm.Ui.Message);
    }

    [Fact]
    public void SetCellsIgnoresOutOfRangeCellsWithinTheBatch()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetCells(new[] { (0, 0), (-1, 0), (0, 999) }, 1);

        // 範囲外の2マスは無視され、有効な1マスだけが変更される。
        Assert.Contains("一括編集: 1マス → A", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void SetCellsIsBlockedWhileAJobIsInFlight()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("勤務表をつくる");

        vm.SetCells(new[] { (0, 0) }, 1);

        Assert.Equal(0, vm._currentSchedule![0][0]);
        Assert.True(vm.Ui.MessageIsError);
    }

    // ===================================================================
    // ShortageFixCandidates
    // ===================================================================

    [Fact]
    public void ShortageFixCandidatesExcludesStaffWhoCannotDoTheShift()
    {
        // 職員A(G0, canDo 休,A only) is excluded from shift B; 職員B(G1, canDo all) is not.
        var st = ThreeShiftTwoGroupState();
        var vm = new MagiViewModel { _state = st, _currentSchedule = ThreeShiftSchedule() };

        var candidates = vm.ShortageFixCandidates(dayIndex: 0, shiftIndex: 2 /* B */);

        Assert.Single(candidates);
        Assert.Equal(1, candidates[0].StaffIndex);
        Assert.True(candidates[0].FromRest);
    }

    [Fact]
    public void ShortageFixCandidatesExcludesOnlyWishLockForADifferentShiftNotAMatchingOne()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") };
        var staff = new List<Staff> { new("職員1", 0), new("職員2", 0), new("職員3", 0) };
        var schedule = new List<IReadOnlyList<int>> { new[] { 0 }, new[] { 0 }, new[] { 0 } };
        var st = MinimalState.Build(
            startDate: "2025-12-01", endDate: "2025-12-01",
            shifts: shifts, staffList: staff, schedule: schedule,
            wishes: new Dictionary<string, int>
            {
                ["0,0"] = 1, // 職員1 wishes A — locked to a DIFFERENT shift than the target (B) -> excluded
                ["1,0"] = 2, // 職員2 wishes B — locked to the SAME shift as the target -> not excluded
            });
        var vm = new MagiViewModel { _state = st, _currentSchedule = st.Schedule.ToIntArray2D() };

        var candidates = vm.ShortageFixCandidates(dayIndex: 0, shiftIndex: 2 /* B */);

        Assert.Equal(new[] { 1, 2 }, candidates.Select(c => c.StaffIndex).OrderBy(x => x));
        Assert.DoesNotContain(candidates, c => c.StaffIndex == 0);
    }

    [Fact]
    public void ShortageFixCandidatesExcludesStaffWhoWouldCreateAForbiddenRun()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") };
        var staff = new List<Staff> { new("職員1", 0), new("職員2", 0) };
        // day0/day1: staff0 stays 休/休 (assigning A on day1 completes the forbidden run 休->A);
        // staff1 is A/休 (assigning A on day1 does NOT match the forbidden run's first element).
        var schedule = new List<IReadOnlyList<int>> { new[] { 0, 0 }, new[] { 1, 0 } };
        var st = MinimalState.Build(
            startDate: "2025-12-01", endDate: "2025-12-02",
            shifts: shifts, staffList: staff, schedule: schedule,
            cons3n: new List<C3Row> { new(new List<string> { "休", "A" }) });
        var vm = new MagiViewModel { _state = st, _currentSchedule = st.Schedule.ToIntArray2D() };

        var candidates = vm.ShortageFixCandidates(dayIndex: 1, shiftIndex: 1 /* A */);

        Assert.Single(candidates);
        Assert.Equal(1, candidates[0].StaffIndex);
    }

    [Fact]
    public void ShortageFixCandidatesOrdersRestFirstAndExcludesCoverageHoleCreators()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("A", "A", "2", ""), // need1=2 -> moving someone off A when exactly 2 are on it opens a hole
            new("B", "B", "", ""),
            new("C", "C", "", ""),
        };
        var staff = new List<Staff> { new("職員1", 0), new("職員2", 0), new("職員3", 0), new("職員4", 0) };
        var schedule = new List<IReadOnlyList<int>> { new[] { 1 }, new[] { 1 }, new[] { 0 }, new[] { 3 } };
        var st = MinimalState.Build(startDate: "2025-12-01", endDate: "2025-12-01", shifts: shifts, staffList: staff, schedule: schedule);
        var vm = new MagiViewModel { _state = st, _currentSchedule = st.Schedule.ToIntArray2D() };

        var candidates = vm.ShortageFixCandidates(dayIndex: 0, shiftIndex: 2 /* B */);

        // 職員1/職員2 (on A, need1=2) would open a coverage hole -> excluded.
        // 職員3 (on rest) is included and sorted first; 職員4 (on C, no demand) is included after.
        Assert.Equal(2, candidates.Count);
        Assert.Equal(2, candidates[0].StaffIndex);
        Assert.True(candidates[0].FromRest);
        Assert.Equal(3, candidates[1].StaffIndex);
        Assert.False(candidates[1].FromRest);
    }

    [Fact]
    public void ShortageFixCandidatesReturnsEmptyWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Empty(vm.ShortageFixCandidates(0, 0));
    }

    [Fact]
    public void ShortageFixCandidatesReturnsEmptyForOutOfRangeShiftOrDay()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        Assert.Empty(vm.ShortageFixCandidates(0, 99));
        Assert.Empty(vm.ShortageFixCandidates(99, 0));
    }

    // ===================================================================
    // 日別必要人数 (needDay)
    // ===================================================================

    [Fact]
    public void NeedDayOverridesListsAndOrdersEntriesByDayThenShift()
    {
        var st = MinimalState.Build(needDay1: new Dictionary<string, string>
        {
            ["1,2"] = "3",
            ["0,1"] = "2",
            ["1,1"] = "4",
        });
        var vm = new MagiViewModel { _state = st };

        var rows = vm.NeedDayOverrides();

        // key は "{k},{j}"（シフト先・日後）で、ソートは J 昇順→K 昇順。
        // "0,1"→(J=1,K=0)・"1,1"→(J=1,K=1)・"1,2"→(J=2,K=1)。
        Assert.Equal(new[] { (1, 0), (1, 1), (2, 1) }, rows.Select(r => (r.J, r.K)));
    }

    [Fact]
    public void SetNeedDayWritesBothPatternsAndTrimsInput()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.SetNeedDay(1, 2, "  3  ", " 5 ");

        Assert.Equal("3", vm._state!.NeedDay1["1,2"]);
        Assert.Equal("5", vm._state!.NeedDay2["1,2"]);
    }

    [Fact]
    public void SetNeedDayRemovesEntryWhenBothBlank()
    {
        var st = MinimalState.Build(
            needDay1: new Dictionary<string, string> { ["1,2"] = "3" },
            needDay2: new Dictionary<string, string> { ["1,2"] = "5" });
        var vm = new MagiViewModel { _state = st };

        vm.SetNeedDay(1, 2, "  ", "");

        Assert.False(vm._state!.NeedDay1.ContainsKey("1,2"));
        Assert.False(vm._state!.NeedDay2.ContainsKey("1,2"));
    }

    [Fact]
    public void RemoveNeedDayDeletesTheKeyFromBothPatterns()
    {
        var st = MinimalState.Build(
            needDay1: new Dictionary<string, string> { ["1,2"] = "3" },
            needDay2: new Dictionary<string, string> { ["1,2"] = "5" });
        var vm = new MagiViewModel { _state = st };

        vm.RemoveNeedDay(1, 2);

        Assert.False(vm._state!.NeedDay1.ContainsKey("1,2"));
        Assert.False(vm._state!.NeedDay2.ContainsKey("1,2"));
    }

    // ===================================================================
    // 個人別の回数 (staffRange)
    // ===================================================================

    [Fact]
    public void SetStaffRangeWritesTrimmedRange()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.SetStaffRange(0, 1, " 2 ", " 5 ");

        Assert.Equal(new Range("2", "5"), vm._state!.StaffRange["0,1"]);
    }

    [Fact]
    public void SetStaffRangeRemovesEntryWhenBothBlank()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "5") });
        var vm = new MagiViewModel { _state = st };

        vm.SetStaffRange(0, 1, "", "  ");

        Assert.False(vm._state!.StaffRange.ContainsKey("0,1"));
    }

    [Fact]
    public void RemoveStaffRangeDeletesTheKey()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "5") });
        var vm = new MagiViewModel { _state = st };

        vm.RemoveStaffRange(0, 1);

        Assert.False(vm._state!.StaffRange.ContainsKey("0,1"));
    }

    [Fact]
    public void RelaxStaffRangePinWidensByTheGivenDeltasAndClampsAtZero()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "2") });
        var vm = new MagiViewModel { _state = st };

        vm.RelaxStaffRangePin(0, 1, loDelta: -5, hiDelta: 3);

        // lo: max(2-5, 0) = 0 ; hi: max(2+3, 0) = 5
        Assert.Equal(new Range("0", "5"), vm._state!.StaffRange["0,1"]);
    }

    [Fact]
    public void RelaxStaffRangePinKeepsHiAtLeastLoWhenOnlyLoWidens()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "2") });
        var vm = new MagiViewModel { _state = st };

        vm.RelaxStaffRangePin(0, 1, loDelta: -1, hiDelta: 0);

        // lo: max(2-1,0)=1 ; hi: max(2+0, newLo=1) = 2 (unchanged, since 2>=1)
        Assert.Equal(new Range("1", "2"), vm._state!.StaffRange["0,1"]);
    }

    [Fact]
    public void RelaxStaffRangePinIsANoOpWhenTheKeyIsMissing()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.RelaxStaffRangePin(0, 1, -1, 1); // no "0,1" entry — must not throw or create one
        Assert.False(vm._state!.StaffRange.ContainsKey("0,1"));
    }

    [Fact]
    public void RelaxStaffRangePinIsANoOpWhenDeltasProduceNoChange()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "2") });
        var vm = new MagiViewModel { _state = st };

        vm.RelaxStaffRangePin(0, 1, 0, 0);

        Assert.Equal(0, vm.UndoStackCount); // SetStaffRange (which pushes undo via ApplyStructure) was never reached
    }

    [Fact]
    public void RelaxStaffRangePinIsBlockedWhileAJobIsInFlightWithItsOwnMessage()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "2") });
        var vm = new MagiViewModel { _state = st };
        vm.BeginBoardJob("仕上げ最適化");

        vm.RelaxStaffRangePin(0, 1, -1, 1);

        Assert.Equal(new Range("2", "2"), vm._state!.StaffRange["0,1"]); // unchanged
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("仕上げ最適化", vm.Ui.Message);
    }

    // ===================================================================
    // グループ単位の回数
    // ===================================================================

    [Fact]
    public void GroupLabelsFormatsNameAndKigouWhenTheyDiffer()
    {
        var st = MinimalState.Build(groups: new List<Group> { new("病棟A", "A班"), new("G1", "G1") });
        var vm = new MagiViewModel { _state = st };

        Assert.Equal(new[] { "病棟A·A班", "G1" }, vm.GroupLabels());
    }

    [Fact]
    public void GroupMemberCountCountsStaffInTheGroup()
    {
        var st = ThreeShiftTwoGroupState();
        var vm = new MagiViewModel { _state = st };

        Assert.Equal(1, vm.GroupMemberCount(0));
        Assert.Equal(1, vm.GroupMemberCount(1));
        Assert.Equal(0, vm.GroupMemberCount(99));
    }

    [Fact]
    public void AllowedShiftsForGroupIntersectsAllMembersCanDo()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 } }; // G0 canDo 休,A only
        var staff = new List<Staff> { new("職員1", 0), new("職員2", 0) };
        var st = MinimalState.Build(shifts: shifts, groupShift: groupShift, staffList: staff);
        var vm = new MagiViewModel { _state = st };

        Assert.Equal(new[] { 0, 1 }, vm.AllowedShiftsForGroup(0).OrderBy(x => x));
    }

    [Fact]
    public void AllowedShiftsForGroupReturnsEmptyForAGroupWithNoMembers()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        Assert.Empty(vm.AllowedShiftsForGroup(99));
    }

    [Fact]
    public void SetGroupRangeWritesAllMembersAndSkipsExisting()
    {
        var staff = new List<Staff> { new("職員1", 0), new("職員2", 0), new("職員3", 0) };
        var st = MinimalState.Build(
            staffList: staff,
            staffRange: new Dictionary<string, Range> { ["1,1"] = new("9", "9") }); // 職員2 already has a personal value
        var vm = new MagiViewModel { _state = st };

        vm.SetGroupRange(0, 1, "2", "4");

        Assert.Equal(new Range("2", "4"), vm._state!.StaffRange["0,1"]);
        Assert.Equal(new Range("9", "9"), vm._state!.StaffRange["1,1"]); // untouched — pre-existing value wins
        Assert.Equal(new Range("2", "4"), vm._state!.StaffRange["2,1"]);
    }

    [Fact]
    public void SetGroupRangeAlsoSetsGroupAptWhenLoEqualsHi()
    {
        var st = MinimalState.Build(staffList: new List<Staff> { new("職員1", 0) });
        var vm = new MagiViewModel { _state = st };

        vm.SetGroupRange(0, 1, "3", "3");

        Assert.Equal("3", vm._state!.GroupShiftApt[0][1]);
    }

    [Fact]
    public void SetGroupRangeClearsAptWhenLoDiffersFromHi()
    {
        var st = MinimalState.Build(
            staffList: new List<Staff> { new("職員1", 0) },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "9" } });
        var vm = new MagiViewModel { _state = st };

        vm.SetGroupRange(0, 1, "2", "4");

        Assert.Equal("", vm._state!.GroupShiftApt[0][1]);
    }

    [Fact]
    public void SetGroupRangeIsANoOpWhenBothBoundsAreBlank()
    {
        var st = MinimalState.Build(staffList: new List<Staff> { new("職員1", 0) });
        var vm = new MagiViewModel { _state = st };

        vm.SetGroupRange(0, 1, "", "  ");

        Assert.Empty(vm._state!.StaffRange);
    }

    [Fact]
    public void SetGroupRangeIsANoOpForAGroupWithNoMembers()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetGroupRange(99, 1, "2", "4");
        Assert.Empty(vm._state!.StaffRange);
    }

    [Fact]
    public void ClearGroupRangeRemovesOnlyExactlyMatchingEntries()
    {
        var staff = new List<Staff> { new("職員1", 0), new("職員2", 0) };
        var st = MinimalState.Build(
            staffList: staff,
            staffRange: new Dictionary<string, Range>
            {
                ["0,1"] = new("2", "4"), // matches the displayed range -> cleared
                ["1,1"] = new("9", "9"), // an individually-overridden value -> kept
            });
        var vm = new MagiViewModel { _state = st };

        vm.ClearGroupRange(0, 1, "2", "4");

        Assert.False(vm._state!.StaffRange.ContainsKey("0,1"));
        Assert.Equal(new Range("9", "9"), vm._state!.StaffRange["1,1"]);
        Assert.Equal("", vm._state!.GroupShiftApt[0][1]);
    }

    [Fact]
    public void ClearGroupRangeIsANoOpWhenNothingMatches()
    {
        var st = MinimalState.Build(
            staffList: new List<Staff> { new("職員1", 0) },
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("9", "9") });
        var vm = new MagiViewModel { _state = st };

        vm.ClearGroupRange(0, 1, "2", "4"); // displayed (2,4) doesn't match the stored (9,9)

        Assert.Equal(new Range("9", "9"), vm._state!.StaffRange["0,1"]); // untouched
    }

    [Fact]
    public void GroupRangeSummaryIncludesMajorityShareAndSingletonGroupsOnly()
    {
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var staff = new List<Staff>
        {
            new("職員1", 0), new("職員2", 0), new("職員3", 0), // G0: 3 members
            new("職員4", 1), // G1: 1 member (singleton)
        };
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") };
        var staffRange = new Dictionary<string, Range>
        {
            // shift 1 (A): 2 of 3 G0 members share (2,2) -> majority, included
            ["0,1"] = new("2", "2"),
            ["1,1"] = new("2", "2"),
            ["2,1"] = new("3", "3"),
            // shift 2 (B): all 3 G0 members have distinct values -> no majority (best share=1), excluded
            ["0,2"] = new("1", "1"),
            ["1,2"] = new("2", "2"),
            ["2,2"] = new("3", "3"),
            // G1's sole member on shift 1 -> singleton group, included despite share=1
            ["3,1"] = new("5", "5"),
        };
        var st = MinimalState.Build(groups: groups, staffList: staff, shifts: shifts, staffRange: staffRange);
        var vm = new MagiViewModel { _state = st };

        var rows = vm.GroupRangeSummary();

        Assert.Equal(2, rows.Count);
        var g0Row = Assert.Single(rows, r => r.G == 0);
        Assert.Equal((1, "G0", "A", "2", "2", 3, 2), (g0Row.K, g0Row.GroupName, g0Row.Kigou, g0Row.Lo, g0Row.Hi, g0Row.Members, g0Row.Shared));
        var g1Row = Assert.Single(rows, r => r.G == 1);
        Assert.Equal((1, "G1", "A", "5", "5", 1, 1), (g1Row.K, g1Row.GroupName, g1Row.Kigou, g1Row.Lo, g1Row.Hi, g1Row.Members, g1Row.Shared));
    }

    [Fact]
    public void GroupRangeSummaryReturnsEmptyWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Empty(vm.GroupRangeSummary());
    }

    // ===================================================================
    // 集計セルのしきい値 (StaffCellLimits / NeedCellLimits)
    // ===================================================================

    [Fact]
    public void StaffCellLimitsMapsSentinelsToNull()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // no staffRange, no apt configured
        var (lo, hi, apt) = vm.StaffCellLimits(0, 1);
        Assert.Null(lo);
        Assert.Null(hi);
        Assert.Null(apt);
    }

    [Fact]
    public void StaffCellLimitsReturnsConfiguredValues()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "5") });
        var vm = new MagiViewModel { _state = st };
        var (lo, hi, apt) = vm.StaffCellLimits(0, 1);
        Assert.Equal(2, lo);
        Assert.Equal(5, hi);
    }

    [Fact]
    public void StaffCellLimitsKeepsAnExplicitZeroLowerBound()
    {
        // [2026-09-04 実機報告「個人の下限をゼロに出来ない」] 旧: 0 を未設定扱い＝適用しても「なし」へ戻って見えた。
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("0", "0"), ["1,1"] = new("0", "2") });
        var vm = new MagiViewModel { _state = st };
        Assert.Equal((0, 0), (vm.StaffCellLimits(0, 1).Item1, vm.StaffCellLimits(0, 1).Item2));
        Assert.Equal((0, 2), (vm.StaffCellLimits(1, 1).Item1, vm.StaffCellLimits(1, 1).Item2));
    }

    [Fact]
    public void StaffCellLimitsReturnsNullForOutOfRangeIndices()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        Assert.Equal((null, null, null), vm.StaffCellLimits(99, 0));
    }

    [Fact]
    public void NeedCellLimitsReturnsNullWhenNeitherPatternIsDefined()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        Assert.Null(vm.NeedCellLimits(1, 0));
    }

    [Fact]
    public void NeedCellLimitsUsesTheSingleValueWhenOnlyNeed1IsDefined()
    {
        var st = MinimalState.Build(needDay1: new Dictionary<string, string> { ["1,0"] = "4" });
        var vm = new MagiViewModel { _state = st };
        Assert.Equal((4, 4), vm.NeedCellLimits(1, 0));
    }

    [Fact]
    public void NeedCellLimitsReturnsNullWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Null(vm.NeedCellLimits(0, 0));
    }

    // ===================================================================
    // 回数センター (StaffCountRules)
    // ===================================================================

    [Fact]
    public void StaffCountRulesIncludesOnlyCellsWithARangeOrAnAptTarget()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") };
        var staff = new List<Staff> { new("職員1", 0) };
        var st = MinimalState.Build(
            shifts: shifts, staffList: staff,
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "5") },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "3", "" } });
        var vm = new MagiViewModel { _state = st };

        var rows = vm.StaffCountRules();

        Assert.Equal(2, rows.Count); // (i=0,k=0): apt only ; (i=0,k=1): range only
        var restRow = Assert.Single(rows, r => r.K == 0);
        Assert.True(restRow.AptEff >= 0);
        Assert.False(restRow.HasRange);
        var aRow = Assert.Single(rows, r => r.K == 1);
        Assert.True(aRow.HasRange);
        Assert.Equal("2", aRow.Lo);
        Assert.Equal("5", aRow.Hi);
    }

    [Fact]
    public void StaffCountRulesReturnsEmptyWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Empty(vm.StaffCountRules());
    }

    // ===================================================================
    // 希望シフト (wishes)
    // ===================================================================

    [Fact]
    public void WishOverridesListsAndOrdersEntriesByStaffThenDay()
    {
        var st = MinimalState.Build(wishes: new Dictionary<string, int> { ["1,0"] = 1, ["0,2"] = 1, ["0,0"] = 1 });
        var vm = new MagiViewModel { _state = st };

        var rows = vm.WishOverrides();

        Assert.Equal(new[] { (0, 0), (0, 2), (1, 0) }, rows.Select(r => (r.I, r.J)));
    }

    [Fact]
    public void SetWishWritesTheEntry()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetWish(0, 1, 1);
        Assert.Equal(1, vm._state!.Wishes["0,1"]);
    }

    [Fact]
    public void RemoveWishDeletesTheEntry()
    {
        var st = MinimalState.Build(wishes: new Dictionary<string, int> { ["0,1"] = 1 });
        var vm = new MagiViewModel { _state = st };
        vm.RemoveWish(0, 1);
        Assert.False(vm._state!.Wishes.ContainsKey("0,1"));
    }

    [Fact]
    public void SetNeedDaysForDaysWritesBothSidesAndBlankSideRestoresTheDefault()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetNeedDaysForDays(1, new[] { 0, 2 }, "2", "3");
        Assert.Equal(new Dictionary<string, string> { ["1,0"] = "2", ["1,2"] = "2" }, vm._state!.NeedDay1);
        Assert.Equal(new Dictionary<string, string> { ["1,0"] = "3", ["1,2"] = "3" }, vm._state!.NeedDay2);
        vm.SetNeedDaysForDays(1, new[] { 0 }, "", "4");
        Assert.False(vm._state!.NeedDay1.ContainsKey("1,0"));
        Assert.Equal("4", vm._state!.NeedDay2["1,0"]);
        vm.SetNeedDaysForDays(9, new[] { 0 }, "1", "");
        Assert.Equal(1, vm._state!.NeedDay1.Count);
    }

    [Fact]
    public void ClearNeedDaysForDaysRemovesOnlyTheGivenDaysAndIsANoOpWhenNothingChanges()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetNeedDaysForDays(1, new[] { 0, 2, 4 }, "2", "");
        var before = vm._state;
        vm.ClearNeedDaysForDays(1, new[] { 5 });
        Assert.Same(before, vm._state);
        vm.ClearNeedDaysForDays(1, new[] { 0, 4 });
        Assert.Equal(new Dictionary<string, string> { ["1,2"] = "2" }, vm._state!.NeedDay1);
    }

    [Fact]
    public void SetWishesForDaysAppliesToASingleStaffMemberAcrossTheGivenDays()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetWishesForDays(0, new[] { 1, 3 }, 1);
        Assert.Equal(new Dictionary<string, int> { ["0,1"] = 1, ["0,3"] = 1 }, vm._state!.Wishes);
    }

    [Fact]
    public void SetWishesForDaysAppliesToAllStaffWhenStaffIndexIsNull()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // 2 default staff
        vm.SetWishesForDays(null, new[] { 0 }, 1);
        Assert.Equal(new Dictionary<string, int> { ["0,0"] = 1, ["1,0"] = 1 }, vm._state!.Wishes);
    }

    [Fact]
    public void SetWishesForDaysIsANoOpWithNoDaysOrAnInvalidShift()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetWishesForDays(0, Array.Empty<int>(), 1);
        vm.SetWishesForDays(0, new[] { 0 }, 99);
        Assert.Empty(vm._state!.Wishes);
    }

    [Fact]
    public void ClearWishesForDaysRemovesOnlyTheGivenDays()
    {
        var st = MinimalState.Build(wishes: new Dictionary<string, int> { ["0,0"] = 1, ["0,1"] = 1 });
        var vm = new MagiViewModel { _state = st };

        vm.ClearWishesForDays(0, new[] { 0 });

        Assert.False(vm._state!.Wishes.ContainsKey("0,0"));
        Assert.True(vm._state!.Wishes.ContainsKey("0,1"));
    }

    [Fact]
    public void ClearAllWishesRemovesEverything()
    {
        var st = MinimalState.Build(wishes: new Dictionary<string, int> { ["0,0"] = 1, ["1,1"] = 1 });
        var vm = new MagiViewModel { _state = st };
        vm.ClearAllWishes();
        Assert.Empty(vm._state!.Wishes);
    }

    [Fact]
    public void ClearAllWishesIsANoOpWhenAlreadyEmpty()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.ClearAllWishes();
        Assert.Equal(0, vm.UndoStackCount); // ApplyStructure was never reached
    }

    // ===================================================================
    // シフトの表示色
    // ===================================================================

    [Fact]
    public void ShiftColorListResolvesPaletteColorsByPositionWhenUnset()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // 休=idx0, A=idx1, no overrides
        var colors = vm.ShiftColorList();
        Assert.Equal("#E59B96", colors[0].Hex);
        Assert.False(colors[0].Custom);
        Assert.Equal("#74BEB0", colors[1].Hex);
        Assert.False(colors[1].Custom);
    }

    [Fact]
    public void ShiftColorListPrefersAnExplicitOverride()
    {
        var st = MinimalState.Build(shiftColors: new Dictionary<string, string> { ["休"] = "#123456" });
        var vm = new MagiViewModel { _state = st };
        var colors = vm.ShiftColorList();
        Assert.Equal("#123456", colors[0].Hex);
        Assert.True(colors[0].Custom);
    }

    [Fact]
    public void SetShiftColorWritesTheOverride()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetShiftColor("休", " #abcdef ");
        Assert.Equal("#abcdef", vm._state!.ShiftColors["休"]);
    }

    [Fact]
    public void ResetShiftColorRemovesTheOverride()
    {
        var st = MinimalState.Build(shiftColors: new Dictionary<string, string> { ["休"] = "#123456" });
        var vm = new MagiViewModel { _state = st };
        vm.ResetShiftColor("休");
        Assert.False(vm._state!.ShiftColors.ContainsKey("休"));
    }

    [Theory]
    [InlineData("__vio__")]
    [InlineData("__vioSoft__")]
    public void ViolationColorSettersUseTheReservedKeys(string reservedKey)
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        if (reservedKey == "__vio__") vm.SetViolationColor("#ff0000"); else vm.SetViolationSoftColor("#ff0000");
        Assert.Equal("#ff0000", vm._state!.ShiftColors[reservedKey]);

        if (reservedKey == "__vio__") vm.ResetViolationColor(); else vm.ResetViolationSoftColor();
        Assert.False(vm._state!.ShiftColors.ContainsKey(reservedKey));
    }

    [Fact]
    public void ViolationFamilyColorSettersUseAPerFamilyReservedKey()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.SetViolationFamilyColor("c3n", "#ff0000");
        Assert.Equal("#ff0000", vm._state!.ShiftColors["__vioFam_c3n__"]);
        vm.ResetViolationFamilyColor("c3n");
        Assert.False(vm._state!.ShiftColors.ContainsKey("__vioFam_c3n__"));
    }

    // ===================================================================
    // 見直し候補メモ（state 非保存・UI のみ）
    // ===================================================================

    [Fact]
    public void AddReviewMemoAppendsTrimmedTextWithoutRequiringLoadedState()
    {
        var vm = new MagiViewModel(); // no _state at all
        vm.AddReviewMemo("  基本ルールの見直し候補  ");
        Assert.Equal(new[] { "基本ルールの見直し候補" }, vm.Ui.ReviewMemos);
        Assert.Equal("見直し候補に追加しました", vm.Ui.Message);
    }

    [Fact]
    public void AddReviewMemoIgnoresBlankText()
    {
        var vm = new MagiViewModel();
        vm.AddReviewMemo("   ");
        Assert.Empty(vm.Ui.ReviewMemos);
    }

    [Fact]
    public void RemoveReviewMemoDeletesByIndexAndIgnoresOutOfRange()
    {
        var vm = new MagiViewModel();
        vm.AddReviewMemo("A");
        vm.AddReviewMemo("B");

        vm.RemoveReviewMemo(0);
        Assert.Equal(new[] { "B" }, vm.Ui.ReviewMemos);

        vm.RemoveReviewMemo(99); // out of range — no-op, no throw
        Assert.Equal(new[] { "B" }, vm.Ui.ReviewMemos);
    }

    // ===================================================================
    // 制約CRUD (cons1..cons42s)
    // ===================================================================

    [Fact]
    public void ConstraintFamiliesFormatsCons1Cons2Cons41AndCons42Rows()
    {
        var st = MinimalState.Build(
            cons1: new List<C1Row> { new("5", "休", "2") },
            cons2: new List<C2Row> { new("A", "3") },
            cons41: new List<C41Row> { new("G0", "A", "1", "3") },
            cons42: new List<C42Row> { new("GX", "GY", "SX", "SY") });
        var vm = new MagiViewModel { _state = st };

        var families = vm.ConstraintFamilies().ToDictionary(f => f.Key);

        Assert.Equal(new[] { "休   5日で2回以上" }, families["cons1"].Rows);
        Assert.Equal(new[] { "A   合計3回以上" }, families["cons2"].Rows);
        Assert.Equal(new[] { "G0・A   1〜3" }, families["cons41"].Rows);
        Assert.Equal(new[] { "GXのSX ✕ GYのSY" }, families["cons42"].Rows);
    }

    [Fact]
    public void ConstraintFamiliesJoinsCons3PatternsAndFallsBackToAnEmptyMarker()
    {
        var st = MinimalState.Build(
            cons3: new List<C3Row> { new(new List<string> { "休", "A", "B" }) },
            cons3n: new List<C3Row> { new(new List<string> { "", "" }) }); // no non-blank tokens
        var vm = new MagiViewModel { _state = st };

        var families = vm.ConstraintFamilies().ToDictionary(f => f.Key);

        Assert.Equal(new[] { "休 -> A -> B" }, families["cons3"].Rows);
        Assert.Equal(new[] { "(空)" }, families["cons3n"].Rows);
    }

    [Fact]
    public void ConstraintFamiliesReturnsEmptyWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Empty(vm.ConstraintFamilies());
    }

    [Fact]
    public void SkillConstraintFamiliesFormatsCons41sAndCons42sRows()
    {
        var st = MinimalState.Build(
            cons41s: new List<C41Row> { new("SkillA", "A", "1", "2") },
            cons42s: new List<C42Row> { new("SkillA", "SkillB", "X", "Y") });
        var vm = new MagiViewModel { _state = st };

        var families = vm.SkillConstraintFamilies().ToDictionary(f => f.Key);

        Assert.Equal(new[] { "SkillA・A   1〜2" }, families["cons41s"].Rows);
        Assert.Equal(new[] { "SkillAのX ✕ SkillBのY" }, families["cons42s"].Rows);
    }

    [Fact]
    public void AddCons3TruncatesAtTheFirstBlankAndCapsAtFiveTokens()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.AddCons3("cons3", new[] { "休", "A", "", "B", "C" }); // stops at the blank -> ["休","A"]

        Assert.Equal(new[] { "休", "A" }, vm._state!.Cons3[0].Pattern);
    }

    [Fact]
    public void AddCons3CapsAtFiveTokensWhenThereIsNoBlank()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.AddCons3("cons3n", new[] { "A", "B", "C", "D", "E", "F", "G" });

        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, vm._state!.Cons3n[0].Pattern);
    }

    [Fact]
    public void AddCons3IsANoOpWhenThePatternIsAllBlank()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.AddCons3("cons3", new[] { "", "" });
        Assert.Empty(vm._state!.Cons3);
    }

    [Fact]
    public void AddCons3IsANoOpForAnUnknownFamily()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.AddCons3("cons3-bogus", new[] { "A" });
        Assert.Equal(0, vm.UndoStackCount);
    }

    [Fact]
    public void RemoveConstraintDeletesByIndexAndShiftsSubsequentRows()
    {
        var st = MinimalState.Build(cons1: new List<C1Row> { new("5", "休", "1"), new("6", "A", "2") });
        var vm = new MagiViewModel { _state = st };

        vm.RemoveConstraint("cons1", 0);

        var remaining = Assert.Single(vm._state!.Cons1);
        Assert.Equal(new C1Row("6", "A", "2"), remaining);
    }

    [Fact]
    public void RemoveConstraintIgnoresAnOutOfRangeIndex()
    {
        var st = MinimalState.Build(cons1: new List<C1Row> { new("5", "休", "1") });
        var vm = new MagiViewModel { _state = st };

        vm.RemoveConstraint("cons1", 5);

        Assert.Single(vm._state!.Cons1);
        Assert.Equal(0, vm.UndoStackCount);
    }

    [Fact]
    public void RemoveConstraintIgnoresAnUnknownFamily()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.RemoveConstraint("cons-bogus", 0);
        Assert.Equal(0, vm.UndoStackCount);
    }

    [Fact]
    public void ConstraintRowValuesReturnsFieldsInTheDocumentedOrderForEachFamily()
    {
        var st = MinimalState.Build(
            cons1: new List<C1Row> { new("5", "休", "2") },
            cons2: new List<C2Row> { new("A", "3") },
            cons41: new List<C41Row> { new("G0", "A", "1", "3") },
            cons42: new List<C42Row> { new("GA", "GB", "SA", "SB") },
            cons3: new List<C3Row> { new(new List<string> { "休", "A" }) });
        var vm = new MagiViewModel { _state = st };

        Assert.Equal(new[] { "5", "休", "2" }, vm.ConstraintRowValues("cons1", 0));
        Assert.Equal(new[] { "A", "3" }, vm.ConstraintRowValues("cons2", 0));
        Assert.Equal(new[] { "G0", "A", "1", "3" }, vm.ConstraintRowValues("cons41", 0));
        Assert.Equal(new[] { "GA", "SA", "GB", "SB" }, vm.ConstraintRowValues("cons42", 0));
        Assert.Equal(new[] { "休", "A" }, vm.ConstraintRowValues("cons3", 0));
        Assert.Null(vm.ConstraintRowValues("cons1", 99));
        Assert.Null(vm.ConstraintRowValues("cons-bogus", 0));
    }

    [Fact]
    public void UpdateConstraintCons42RoundTripsFieldOrderCorrectly()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.AddCons42("GA", "GB", "SX", "SY");

        var values = vm.ConstraintRowValues("cons42", 0)!;
        Assert.Equal(new[] { "GA", "SX", "GB", "SY" }, values);

        vm.UpdateConstraint("cons42", 0, values);

        Assert.Equal(new C42Row("GA", "GB", "SX", "SY"), vm._state!.Cons42[0]);
        Assert.Equal(values, vm.ConstraintRowValues("cons42", 0));
    }

    [Fact]
    public void UpdateConstraintCons1ReplacesTheRowInPlace()
    {
        var st = MinimalState.Build(cons1: new List<C1Row> { new("5", "休", "1"), new("6", "A", "2") });
        var vm = new MagiViewModel { _state = st };

        vm.UpdateConstraint("cons1", 1, new[] { "9", "A", "4" });

        Assert.Equal(new C1Row("5", "休", "1"), vm._state!.Cons1[0]); // untouched
        Assert.Equal(new C1Row("9", "A", "4"), vm._state!.Cons1[1]);
    }

    [Fact]
    public void UpdateConstraintCons3ReNormalizesThePattern()
    {
        var st = MinimalState.Build(cons3: new List<C3Row> { new(new List<string> { "休", "A" }) });
        var vm = new MagiViewModel { _state = st };

        vm.UpdateConstraint("cons3", 0, new[] { "B", "C", "", "D" }); // truncates at the blank

        Assert.Equal(new[] { "B", "C" }, vm._state!.Cons3[0].Pattern);
    }

    [Fact]
    public void UpdateConstraintIsANoOpForAnOutOfRangeIndexOrUnknownFamily()
    {
        var st = MinimalState.Build(cons1: new List<C1Row> { new("5", "休", "1") });
        var vm = new MagiViewModel { _state = st };

        vm.UpdateConstraint("cons1", 5, new[] { "1", "2", "3" });
        vm.UpdateConstraint("cons-bogus", 0, new[] { "1" });

        Assert.Equal(new C1Row("5", "休", "1"), vm._state!.Cons1[0]);
        Assert.Equal(0, vm.UndoStackCount);
    }

    [Fact]
    public void AddAndRemoveCons41sAndCons42sRoundTrip()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.AddCons41s("SkillA", "A", "1", "2");
        vm.AddCons42s("SkillA", "SkillB", "X", "Y");
        Assert.Single(vm._state!.Cons41s);
        Assert.Single(vm._state!.Cons42s);

        vm.RemoveConstraint("cons41s", 0);
        vm.RemoveConstraint("cons42s", 0);
        Assert.Empty(vm._state!.Cons41s);
        Assert.Empty(vm._state!.Cons42s);
    }

    [Fact]
    public void MutateConstraintsIsBlockedWhileAJobIsInFlightAndLogsAWarning()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        vm.BeginBoardJob("勤務表をつくる");

        vm.AddCons1("5", "休", "2");

        Assert.Empty(vm._state!.Cons1);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("W", vm.Ui.OpLog[0]); // the block-warning is the most recent log line
    }

    // ===================================================================
    // 実行中ガード・構造編集の土台
    // ===================================================================

    [Fact]
    public void EditBlockedNowIsFalseWhenIdle()
    {
        var vm = new MagiViewModel();
        Assert.False(vm.EditBlockedNow());
        Assert.Null(vm.Ui.Message);
    }

    [Fact]
    public void EditBlockedNowIsTrueAndSetsAnErrorMessageWhenAJobIsInFlight()
    {
        var vm = new MagiViewModel();
        vm.BeginBoardJob("勤務表をつくる");

        Assert.True(vm.EditBlockedNow());
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("勤務表をつくる", vm.Ui.Message);
    }

    [Fact]
    public void Ws1ReturnsNullWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Null(vm.Ws1());
    }

    [Fact]
    public void Ws1DerivesDaysFromTheLoadedScheduleWhenPresent()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 0 } } };
        Assert.Equal(3, vm.Ws1()!.Days);
    }

    [Fact]
    public void Ws1FallsBackToStateDayCountWithoutALoadedSchedule()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // 7-day default state, no _currentSchedule
        Assert.Equal(7, vm.Ws1()!.Days);
    }

    // ===================================================================
    // 目標の検算 (AptBalances)
    // ===================================================================

    [Fact]
    public void AptBalancesReturnsEmptyWithoutLoadedState()
    {
        var vm = new MagiViewModel();
        Assert.Empty(vm.AptBalances());
    }

    [Fact]
    public void AptBalancesDelegatesToV6SanityPortForALoadedState()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "2", "") };
        var st = MinimalState.Build(
            shifts: shifts,
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "3" } });
        var vm = new MagiViewModel { _state = st };

        var result = vm.AptBalances();

        var expected = V6SanityPort.AptBalances(st);
        Assert.NotEmpty(result); // sanity: this fixture actually produces a row (shift A has demand)
        Assert.Equal(
            expected.Select(b => (b.ShiftIdx, b.Kigou, b.AptSum, b.Capacity, b.IsRest)),
            result.Select(b => (b.ShiftIdx, b.Kigou, b.AptSum, b.Capacity, b.IsRest)));
    }

    // ===================================================================
    // 壁になっている禁止の並びを緩める (RelaxForbiddenRule)
    // ===================================================================

    [Fact]
    public async Task RelaxForbiddenRuleRemovesAllRowsMatchingTheTruncatedKeyOnly()
    {
        var st = MinimalState.Build(cons3n: new List<C3Row>
        {
            new(new List<string> { "休", "A", "", "", "" }),  // truncated key "休→A"
            new(new List<string> { "休", "A", "B", "", "" }), // truncated key "休→A→B" — different, must survive
            new(new List<string> { "休", "A" }),               // no blank at all -> key is also "休→A" (same effective rule)
        });
        // _currentSchedule も要る — 無いと ApplyStructureWithMessage の早期returnパス（LastApplyStructure
        // WithMessageTask=null のまま同期完了）に落ちてしまう（このピースのクラスKDoc・schedule非要求と
        // 誤解しやすい箇所。ApplyWishes等と違いこちらは盤面編集ではなく構造編集なので schedule 任意）。
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.RelaxForbiddenRule("休→A");
        Assert.NotNull(vm.LastApplyStructureWithMessageTask);
        await vm.LastApplyStructureWithMessageTask!;

        var remaining = Assert.Single(vm._state!.Cons3n);
        // RelaxForbiddenRule は行を削除するだけで Pattern の中身自体は変更しない（末尾空白はそのまま）。
        Assert.Equal(new[] { "休", "A", "B", "", "" }, remaining.Pattern);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("2件", vm.Ui.Message);
        Assert.Contains("必須=", vm.Ui.Message); // ApplyStructureWithMessage's async completion suffix ran
    }

    [Fact]
    public void RelaxForbiddenRuleReportsNotFoundWhenNoRowMatches()
    {
        var st = MinimalState.Build(cons3n: new List<C3Row> { new(new List<string> { "休", "A" }) });
        var vm = new MagiViewModel { _state = st };

        vm.RelaxForbiddenRule("A→B→C");

        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("見つかりませんでした", vm.Ui.Message);
        Assert.Single(vm._state!.Cons3n); // unchanged
    }

    [Fact]
    public void RelaxForbiddenRuleIsBlockedWhileAJobIsInFlight()
    {
        var st = MinimalState.Build(cons3n: new List<C3Row> { new(new List<string> { "休", "A" }) });
        var vm = new MagiViewModel { _state = st };
        vm.BeginBoardJob("勤務表をつくる");

        vm.RelaxForbiddenRule("休→A");

        Assert.Single(vm._state!.Cons3n); // unchanged
        Assert.True(vm.Ui.MessageIsError);
    }

    // ===================================================================
    // Fixtures
    // ===================================================================

    /// <summary>
    /// 休(0)/A(1)/B(2)の3シフト・G0(canDo 休,A のみ)/G1(canDo 全部)の2グループ・
    /// 職員A(G0)/職員B(G1)の2職員。<see cref="ShortageFixCandidates"/>系のcanDo/wishLocked検証に使う。
    /// </summary>
    private static MagiState ThreeShiftTwoGroupState(
        IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyList<C3Row>? cons3n = null)
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 0 }, // G0: 休,A only
            new List<int> { 1, 1, 1 }, // G1: all
        };
        var staff = new List<Staff> { new("職員A", 0), new("職員B", 1) };
        return MinimalState.Build(
            shifts: shifts, groups: groups, groupShift: groupShift, staffList: staff,
            schedule: ThreeShiftSchedule().ToIntArray2DRows(),
            wishes: wishes, cons3n: cons3n);
    }

    private static int[][] ThreeShiftSchedule() => new[]
    {
        new[] { 0, 0, 0, 0, 0, 0, 0 },
        new[] { 0, 0, 0, 0, 0, 0, 0 },
    };
}

file static class TestFixtureExtensions
{
    /// <summary>int[][] -> IReadOnlyList&lt;IReadOnlyList&lt;int&gt;&gt; for MinimalState.Build's schedule param.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> ToIntArray2DRows(this int[][] a) =>
        a.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
}
