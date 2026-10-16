using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.V6;

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

        vm.GenerateSmartInitial(confirmed: true);
        Assert.NotNull(vm.LastGenerateSmartInitialTask);
        await vm.LastGenerateSmartInitialTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("下書きをつくりました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("初期解生成 完了"));
        // [3.694.0] 開始行は 1 本・#N 付きで入力の期間・人数・指紋、完了行には生成した下書きの盤面指紋。
        var start = Assert.Single(vm.Ui.OpLog, l => l.Contains("初期解生成 開始"));
        Assert.Matches("#1 初期解生成 開始 ・ 期間=2025-12-01〜2025-12-07（7日） ・ 職員=2名 ・ 状態指紋=[0-9a-f]{16} ・ 盤面指紋=[0-9a-f]{16}$", start);
        Assert.Matches("#1 初期解生成 完了 必須=[0-9]+ 合計=[0-9]+ ・ 盤面指紋=[0-9a-f]{16}$", Assert.Single(vm.Ui.OpLog, l => l.Contains("初期解生成 完了")));
    }

    /// <summary>[Android 3.679.0] 盤面のある勤務表は確認してから置き換える（確認の文は今の必須違反を言う）。</summary>
    [Fact]
    public void AsksBeforeReplacingAnExistingBoard()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.GenerateSmartInitial();

        Assert.Null(vm.LastGenerateSmartInitialTask);
        Assert.Equal("今の勤務表を下書きで置き換えます。元に戻せます", vm.Ui.DraftConfirm);   // 結果が無い（数えていない）ときは件数を言わない（3.686.0）
        vm.DismissDraftConfirm();
        Assert.Null(vm.Ui.DraftConfirm);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("下書きで置き換えない（確認で取りやめ）"));   // 3.692.0
    }

    /// <summary>
    /// [3.271.0相当] 実行中ガード。他の実行（<see cref="MagiViewModel.RunV6FullOptimize"/> 等）と同じ
    /// <c>RunBlockedByInFlight</c> で断る（失敗の色・何が走っているかを名指し・押した痕跡を W で残す）。
    /// </summary>
    [Fact]
    public void BlockedWhileAnotherBoardJobInFlight_DoesNotRunAndLeavesScheduleUntouched()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("読み込み"); // 別ジョブが進行中を模擬（EndBoardJobを呼ばず占有したまま）

        vm.GenerateSmartInitial(confirmed: true);

        Assert.Null(vm.LastGenerateSmartInitialTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Equal("読み込みの実行中です。終わるか「やめる」を押してから、もう一度お試しください。", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("[W]") && l.Contains("下書きづくり を取り消しました（読み込みが実行中）"));
        // 盤面は元の全休のまま——生成もPushUndoも一切走っていない。
        Assert.All(vm._currentSchedule!, row => Assert.All(row, cell => Assert.Equal(0, cell)));
    }

    /// <summary>[Android vm-3] 下書きで差し替えた後の診断が中止・失敗したら下書き前へ戻す（CSV 取込の 3.592.0 と同じ）。
    /// PushUndo が積んだ段も外す＝元に戻すの履歴は下書き前のまま。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RollsBackWhenTheDiagnosisStopsOrFailsAfterCommitting(bool cancel)
    {
        var st = MinimalState.Build();
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        vm.Ui.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(UiState.Message) || vm.Ui.Message is not { } m || !m.StartsWith("下書きをつくりました", StringComparison.Ordinal)) return;
            if (cancel) throw new OperationCanceledException();
            throw new InvalidOperationException("late");
        };

        vm.GenerateSmartInitial(confirmed: true);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.LastGenerateSmartInitialTask!);
        else await vm.LastGenerateSmartInitialTask!;

        Assert.Same(st, vm._state);
        Assert.Equal(sched, vm._currentSchedule);
        Assert.Equal(0, vm.UndoStackCount);
        Assert.False(vm.Ui.CanUndo);
        Assert.Contains(cancel ? "停止" : "つくれませんでした", vm.Ui.Message);
    }
}
