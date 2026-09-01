using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9, Services/UseCases/DI層] <see cref="MagiViewModel.GenerateSmartInitial"/>
/// （<c>MagiViewModel.SmartInitial.cs</c>、Kotlin原本 <c>generateSmartInitial()</c> の移植）の検証。
///
/// <see cref="MagiViewModel.GenerateSmartInitial"/> は <see cref="MagiEngine.V6.V6FinalPort.HandleSmartInitial"/>
/// を <see cref="MagiApp.ViewModels.Services.IOptimizationService"/> 経由でなく直接呼ぶ
/// （<c>MagiViewModel.SmartInitial.cs</c> クラスKDoc参照）ため、<see cref="MagiViewModelOptimizeTest"/>
/// と異なりフェイク注入はできない——実エンジンをそのまま走らせる。初期解生成は探索を伴わない
/// 組立て処理（希望シフト→C1→必要人数→個人下限→残り埋め）のため実行は近似的に即時。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelSmartInitialTest
{
    public MagiViewModelSmartInitialTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    [Fact]
    public void NoStateLoaded_DoesNotStartAndStaysIdle()
    {
        var vm = new MagiViewModel();

        vm.GenerateSmartInitial();

        Assert.False(vm.Ui.Running);
        Assert.Null(vm.LastGenerateSmartInitialTask);
    }

    [Fact]
    public async Task GeneratesADraftAndMarksItAsResult()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.GenerateSmartInitial();
        Assert.NotNull(vm.LastGenerateSmartInitialTask);
        await vm.LastGenerateSmartInitialTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("下書きをつくりました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("初期解生成 完了"));
    }

    /// <summary>
    /// [3.271.0相当] 実行中ガード。他の <c>RunBlockedByInFlight</c> 系（<see cref="MagiViewModel.RunV6FullOptimize"/>
    /// 等）と異なり、Kotlin原本はここだけ <c>messageIsError = false</c>（穏やかな案内文言）で
    /// <c>RunBlockedByInFlight</c> を経由しない——逐語移植のためその非対称もそのまま検証する。
    /// </summary>
    [Fact]
    public void BlockedWhileAnotherBoardJobInFlight_DoesNotRunAndLeavesScheduleUntouched()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("読み込み"); // 別ジョブが進行中を模擬（EndBoardJobを呼ばず占有したまま）

        vm.GenerateSmartInitial();

        Assert.Null(vm.LastGenerateSmartInitialTask);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("計算の実行中は下書きをつくれません", vm.Ui.Message);
        // 盤面は元の全休のまま——生成もPushUndoも一切走っていない。
        Assert.All(vm._currentSchedule!, row => Assert.All(row, cell => Assert.Equal(0, cell)));
    }
}
