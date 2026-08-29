using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース5] <c>MagiViewModel.kt</c>（3,495行）のうち、このC#移植の最初のピースが担う範囲——
/// 状態管理サブシステム（設定セッター・盤面ジョブの排他制御・操作ログ・元に戻す/やり直すのデータ構造）
/// ——を検証する。Kotlin原本には専用テストが無い（<c>UiStateTest</c> と同じ経緯、クラスKDoc参照）。
///
/// <see cref="Work.OptimizationRepository"/> のプロセス共有 static 状態に触れるため、
/// <see cref="OptimizationRepositoryTest"/> と同じ直列コレクションに属する（<see cref="TestSupport.OptimizationRepositoryStateCollection"/> 参照）。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelTest
{
    public MagiViewModelTest()
    {
        // 各テストの前に必ずリセットする（他クラスの後始末に依存しない＝Arrange側で保証する）。
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    [Fact]
    public void FreshInstanceIsIdleAndHasNoLoadedState()
    {
        var vm = new MagiViewModel();

        Assert.NotNull(vm.Ui);
        Assert.False(vm.OptimizeInFlight());
        Assert.Equal("バックグラウンド計算", vm.BusyWhat());
        Assert.Null(vm.SnapNow());
        Assert.Equal(0, vm.UndoStackCount);
        Assert.Equal(0, vm.RedoStackCount);
    }

    // ===== 設定セッター =====

    [Theory]
    [InlineData(0, 1)]   // 下限未満は1へ
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(16, 16)]
    [InlineData(50, 16)] // 上限超は16へ
    public void SetWorkersClampsToOneToSixteen(int input, int expected)
    {
        var vm = new MagiViewModel();
        vm.SetWorkers(input);

        Assert.Equal(expected, vm.Ui.Workers);
        Assert.Contains($"並列数 → {expected}", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void SetNativeAccelUpdatesUiStateOnlyAndLogsInfo()
    {
        var vm = new MagiViewModel();
        vm.SetNativeAccel(false);

        Assert.False(vm.Ui.NativeAccel);
        Assert.Contains("[I]", vm.Ui.OpLog[0]);
        Assert.Contains("ネイティブ加速 → OFF", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void SetNativeParityOnLogsAsInfo()
    {
        var vm = new MagiViewModel();
        vm.SetNativeParity(true);

        Assert.True(vm.Ui.NativeParity);
        Assert.Contains("[I]", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void SetNativeParityOffLogsAsWarning()
    {
        var vm = new MagiViewModel();
        vm.SetNativeParity(false);

        Assert.False(vm.Ui.NativeParity);
        Assert.Contains("[W]", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void SetBlockSwapC3nFilterUpdatesUiAndPolishGate()
    {
        var vm = new MagiViewModel();
        try
        {
            vm.SetBlockSwapC3nFilter(true);
            Assert.True(vm.Ui.BlockSwapC3nFilter);
            Assert.True(PolishGate.FilterC3nIncrease);
        }
        finally
        {
            PolishGate.FilterC3nIncrease = false; // 既定へ戻す（他テストへ漏らさない）
        }
    }

    [Fact]
    public void SetWideC3nBreakUpdatesUiAndPolishGate()
    {
        var vm = new MagiViewModel();
        try
        {
            vm.SetWideC3nBreak(true);
            Assert.True(vm.Ui.WideC3nBreak);
            Assert.True(PolishGate.WideC3nBreakDays);
        }
        finally
        {
            PolishGate.WideC3nBreakDays = false;
        }
    }

    [Theory]
    [InlineData(1, 10)]                                  // 下限未満は10秒へ
    [InlineData(10, 10)]
    [InlineData(120, 120)]
    [InlineData(99999, MagiViewModel.MaxBudgetSec)]       // 上限超はMaxBudgetSecへ
    public void SetBudgetClampsToTenToMaxBudgetSec(int input, int expected)
    {
        var vm = new MagiViewModel();
        vm.SetBudget(input);

        Assert.Equal(expected, vm.Ui.BudgetSec);
    }

    [Fact]
    public void SetSoftPolishTogglesUi()
    {
        var vm = new MagiViewModel();
        vm.SetSoftPolish(false);
        Assert.False(vm.Ui.SoftPolish);
    }

    [Fact]
    public void SetV6AlgorithmUpdatesUiAndLogsThePascalCaseName()
    {
        var vm = new MagiViewModel();
        vm.SetV6Algorithm(V6Algorithm.Portfolio);

        Assert.Equal(V6Algorithm.Portfolio, vm.Ui.V6Algorithm);
        Assert.Contains("Portfolio", vm.Ui.OpLog[0]);
    }

    // ===== 盤面ジョブの排他制御（BeginBoardJob/EndBoardJob のトークン意味論） =====

    [Fact]
    public void BeginBoardJobMarksBusyWithTheGivenLabel()
    {
        var vm = new MagiViewModel();
        var token = vm.BeginBoardJob("勤務表をつくる");

        Assert.True(vm.OptimizeInFlight());
        Assert.Equal("勤務表をつくる", vm.BusyWhat());

        vm.EndBoardJob(token);
        Assert.False(vm.OptimizeInFlight());
        Assert.Equal("バックグラウンド計算", vm.BusyWhat());
    }

    /// <summary>
    /// [3.404.0の由来をそのまま検証] 後から始まったジョブの旗を、先に終わった側が下ろして
    /// ロックを早く解いてしまう事故が起きないことを固定する。
    /// </summary>
    [Fact]
    public void StaleTokenDoesNotReleaseALaterJob()
    {
        var vm = new MagiViewModel();
        var token1 = vm.BeginBoardJob("A");
        var token2 = vm.BeginBoardJob("B");

        vm.EndBoardJob(token1); // 古いトークン＝何も起きない
        Assert.True(vm.OptimizeInFlight());
        Assert.Equal("B", vm.BusyWhat());

        vm.EndBoardJob(token2); // 現在のトークン＝解放される
        Assert.False(vm.OptimizeInFlight());
    }

    [Fact]
    public void EngineRunTagsSubsequentLogLinesWithARunNumber()
    {
        var vm = new MagiViewModel();

        vm.SetWorkers(3);
        Assert.DoesNotContain("#", vm.Ui.OpLog[0]);

        var token = vm.BeginBoardJob("勤務表をつくる", engineRun: true);
        vm.SetWorkers(4);
        Assert.Contains("#1 ", vm.Ui.OpLog[0]);

        vm.EndBoardJob(token);
        vm.SetWorkers(5);
        Assert.DoesNotContain("#", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void OptimizeInFlightReflectsBackgroundRunningWithoutALocalBoardJob()
    {
        var vm = new MagiViewModel();
        Assert.False(vm.OptimizeInFlight());

        OptimizationRepository.SetRunning(true);
        Assert.True(vm.OptimizeInFlight());

        OptimizationRepository.SetRunning(false);
        Assert.False(vm.OptimizeInFlight());
    }

    // ===== 元に戻す（PushUndo/ClearUndo/SnapNow）=====

    [Fact]
    public void SnapNowReturnsNullWhenNoStateIsLoaded()
    {
        var vm = new MagiViewModel();
        Assert.Null(vm.SnapNow());
    }

    [Fact]
    public void PushUndoIsANoOpWhenNoStateIsLoaded()
    {
        var vm = new MagiViewModel();
        vm.PushUndo();

        Assert.Equal(0, vm.UndoStackCount);
        Assert.False(vm.Ui.CanUndo);
    }

    [Fact]
    public void PushUndoCapturesASnapshotAndEnablesUndo()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(),
            _currentSchedule = MinimalState.BuildSchedule(),
        };

        var snap = vm.SnapNow();
        Assert.NotNull(snap);
        Assert.Same(vm._state, snap!.State);
        // Copy2D() は複製であって同一参照ではない（後から currentSchedule を書き換えても
        // スナップショットが影響を受けない、という undo の前提）。
        Assert.NotSame(vm._currentSchedule, snap.Schedule);
        Assert.Equal(vm._currentSchedule![0], snap.Schedule[0]);

        vm.PushUndo();
        Assert.Equal(1, vm.UndoStackCount);
        Assert.True(vm.Ui.CanUndo);
        Assert.False(vm.Ui.CanRedo);
    }

    [Fact]
    public void PushUndoTrimsTheStackAtThirtyEntries()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(),
            _currentSchedule = MinimalState.BuildSchedule(),
        };

        for (var i = 0; i < 35; i++) vm.PushUndo();

        Assert.Equal(30, vm.UndoStackCount);
        Assert.True(vm.Ui.CanUndo);
    }

    [Fact]
    public void ClearUndoResetsTheStackAndTheFlags()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(),
            _currentSchedule = MinimalState.BuildSchedule(),
        };
        vm.PushUndo();
        Assert.Equal(1, vm.UndoStackCount);

        vm.ClearUndo();

        Assert.Equal(0, vm.UndoStackCount);
        Assert.Equal(0, vm.RedoStackCount);
        Assert.False(vm.Ui.CanUndo);
        Assert.False(vm.Ui.CanRedo);
    }

    // ===== OpDays（純粋な整形ロジック） =====

    [Fact]
    public void OpDaysJoinsUpToTenDaysIndividually()
    {
        var days = new[] { 0, 1, 2 };
        Assert.Equal("1日,2日,3日", MagiViewModel.OpDays(days));
    }

    [Fact]
    public void OpDaysSummarizesMoreThanTenDaysAsACount()
    {
        var days = Enumerable.Range(0, 11).ToArray();
        Assert.Equal("11日分", MagiViewModel.OpDays(days));
    }
}
