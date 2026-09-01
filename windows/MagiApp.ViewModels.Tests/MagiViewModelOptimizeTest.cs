using MagiApp.ViewModels.Services;
using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9, Services/UseCases/DI層] <see cref="MagiViewModel.RunV6FullOptimize"/>
/// （<c>MagiViewModel.Optimize.cs</c>、Kotlin原本 <c>runV6FullOptimize()</c> の移植）の検証。
///
/// <see cref="FakeOptimizationService"/> を <see cref="MagiViewModel(IOptimizationService)"/> 経由で
/// 注入し、実エンジン（数百ms〜数百秒の探索）を待たずに、呼出しの間接化・keep-best判定
/// （入力が結果より良ければ入力を維持）・停止時の直前盤面保持・失敗時のメッセージだけを検証する。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelOptimizeTest
{
    public MagiViewModelOptimizeTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    private sealed class FakeOptimizationService : IOptimizationService
    {
        public int OptimizeCallCount { get; private set; }
        public Func<MagiState, int[][], V6FinalPort.ActionResult>? Result { get; set; }
        public Exception? ThrowInstead { get; set; }

        /// <summary>true にすると、呼出元がキャンセルするまで完了しない（Stop()検証用）。</summary>
        public bool HangUntilCancelled { get; set; }

        public async Task<V6FinalPort.ActionResult> OptimizeAsync(
            MagiState state, int[][] schedule, int secondsRaw, int? workers, bool softPolish,
            V6Algorithm requestedAlgorithm, bool allowImpossible,
            Action<string, ViolationReport?, long, long>? onProgress, CancellationToken cancellationToken)
        {
            OptimizeCallCount++;
            onProgress?.Invoke("テスト探索中", null, 0, 0);
            if (HangUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            if (ThrowInstead is not null) throw ThrowInstead;
            return Result!(state, schedule);
        }

        public int SoftPolishCallCount { get; private set; }
        public Func<MagiState, int[][], int[][]>? PolishedSchedule { get; set; }
        public Exception? ThrowInsteadOnSoftPolish { get; set; }

        public Task<int[][]> SoftPolishAsync(MagiState state, int[][] schedule, int seconds, CancellationToken cancellationToken)
        {
            SoftPolishCallCount++;
            if (ThrowInsteadOnSoftPolish is not null) throw ThrowInsteadOnSoftPolish;
            return Task.FromResult(PolishedSchedule?.Invoke(state, schedule) ?? schedule);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyS = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, int> EmptyI = new Dictionary<string, int>();

    private static ViolationReport Report(int hard, int total) =>
        new(EmptyS, EmptyS, EmptyS, EmptyI, Total: total, Hard: hard, Soft: total, WeightedScore: total);

    [Fact]
    public void NoStateLoaded_DoesNotCallServiceAndStaysIdle()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake);

        vm.RunV6FullOptimize();

        Assert.Equal(0, fake.OptimizeCallCount);
        Assert.False(vm.Ui.Running);
        Assert.Null(vm.LastRunOptimizeTask);
    }

    [Fact]
    public void BlockedWhileAnotherBoardJobInFlight_DoesNotCallServiceAndNotifies()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("読み込み"); // 別ジョブが進行中を模擬（EndBoardJobを呼ばず占有したまま）

        vm.RunV6FullOptimize();

        Assert.Equal(0, fake.OptimizeCallCount);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("実行中です", vm.Ui.Message);
    }

    [Fact]
    public async Task ImprovedResult_IsAdoptedAndMarkedAsResult()
    {
        var fake = new FakeOptimizationService
        {
            Result = (state, _) => new V6FinalPort.ActionResult(
                Schedule: MinimalState.BuildSchedule(),
                Report: Report(hard: 0, total: 0),
                Phase: "test:Fake",
                BusyDetail: new V6FinalPort.BusyDetail("Fake", "2名 x 7日", "HARD 0件"),
                Logs: Array.Empty<MirrorLog>()),
        };
        var st = MinimalState.Build();
        var vm = new MagiViewModel(fake) { _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunV6FullOptimize();
        Assert.NotNull(vm.LastRunOptimizeTask);
        await vm.LastRunOptimizeTask!;

        Assert.Equal(1, fake.OptimizeCallCount);
        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("勤務表ができました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("最適化 完了"));
    }

    [Fact]
    public async Task WorseResult_KeepsInputScheduleAndSaysSoInMessage()
    {
        var fake = new FakeOptimizationService
        {
            // 入力(全休・違反0)より明確に悪い結果（Hard=0/Total=100）を返す＝keep-bestが入力を維持するはず。
            Result = (state, _) => new V6FinalPort.ActionResult(
                Schedule: MinimalState.BuildSchedule(),
                Report: Report(hard: 0, total: 100),
                Phase: "test:Fake",
                BusyDetail: new V6FinalPort.BusyDetail("Fake", "2名 x 7日", "HARD 0件"),
                Logs: Array.Empty<MirrorLog>()),
        };
        var st = MinimalState.Build();
        var inputSchedule = MinimalState.BuildSchedule();
        var vm = new MagiViewModel(fake) { _state = st, _currentSchedule = inputSchedule };

        vm.RunV6FullOptimize();
        await vm.LastRunOptimizeTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("改善しませんでした", vm.Ui.Message);
        Assert.Contains("前回の結果を維持します", vm.Ui.Message);
        // 採用された盤面は入力(全休)のまま——結果側の盤面ではない。
        Assert.All(vm.Ui.Schedule, row => Assert.All(row, cell => Assert.Equal(0, cell)));
    }

    [Fact]
    public async Task CancelledRun_KeepsInputScheduleAndReportsStopped()
    {
        var fake = new FakeOptimizationService { ThrowInstead = new OperationCanceledException() };
        var st = MinimalState.Build();
        var vm = new MagiViewModel(fake) { _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunV6FullOptimize();
        Assert.NotNull(vm.LastRunOptimizeTask);
        // Kotlin原本の runV6FullOptimize は停止処理(keep-best保持・ログ)のあと `throw e` で
        // CancellationException を再送出する（呼出元は viewModelScope の fire-and-forget で誰も
        // 観測しない）。この移植の Task も同じ理由で Canceled 状態のまま終わる
        // （確立済みの規約: MagiViewModelPersistenceTest の同型アサーション参照）。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.LastRunOptimizeTask!);

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("停止しました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("停止: 直前の勤務表"));
    }

    [Fact]
    public async Task FailedRun_ReportsErrorAndDoesNotCrash()
    {
        var fake = new FakeOptimizationService { ThrowInstead = new InvalidOperationException("boom") };
        var st = MinimalState.Build();
        var vm = new MagiViewModel(fake) { _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunV6FullOptimize();
        await vm.LastRunOptimizeTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("つくれませんでした", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("最適化 失敗"));
    }

    [Fact]
    public void RunBlockedByInFlightMessage_NamesTheRunningJob()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("読み込み");

        vm.RunV6FullOptimize();

        Assert.Contains("読み込み", vm.Ui.Message);
    }

    // ===== RunSoftPolish（MagiViewModel.Optimize.cs, Kotlin原本 runSoftPolish 1411-1504行）=====

    [Fact]
    public async Task SoftPolish_NoGain_ReportsNoFurtherImprovement()
    {
        // MinimalState.Build() は制約皆無＝どの盤面も違反0。gain=0 の「これ以上は整いませんでした」分岐を検証。
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunSoftPolish();
        Assert.NotNull(vm.LastRunSoftPolishTask);
        await vm.LastRunSoftPolishTask!;

        Assert.Equal(1, fake.SoftPolishCallCount);
        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("これ以上は整いませんでした", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("ソフト研磨 完了"));
    }

    [Fact]
    public async Task SoftPolish_Cancelled_KeepsInputAndReportsStopped()
    {
        var fake = new FakeOptimizationService { ThrowInsteadOnSoftPolish = new OperationCanceledException() };
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunSoftPolish();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.LastRunSoftPolishTask!);

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.Contains("停止しました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("ソフト研磨 停止: 直前の勤務表"));
    }

    [Fact]
    public async Task SoftPolish_Failure_ReportsErrorAndDoesNotCrash()
    {
        var fake = new FakeOptimizationService { ThrowInsteadOnSoftPolish = new InvalidOperationException("boom") };
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunSoftPolish();
        await vm.LastRunSoftPolishTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("整えられませんでした", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("ソフト研磨 失敗"));
    }

    [Fact]
    public void SoftPolish_BlockedWhileAnotherJobInFlight_DoesNotCallService()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("勤務表づくり");

        vm.RunSoftPolish();

        Assert.Equal(0, fake.SoftPolishCallCount);
        Assert.True(vm.Ui.MessageIsError);
    }

    // ===== Stop（MagiViewModel.Optimize.cs, Kotlin原本 stop() 1506-1544行）=====

    [Fact]
    public void Stop_WithNothingRunning_IsANoOp()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.Stop(); // 例外を投げないこと。

        Assert.False(vm.Ui.Running);
    }

    [Fact]
    public async Task Stop_CancelsInFlightOptimizeRun()
    {
        var fake = new FakeOptimizationService { HangUntilCancelled = true };
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunV6FullOptimize();
        Assert.NotNull(vm.LastRunOptimizeTask);
        Assert.True(vm.Ui.Running);

        vm.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.LastRunOptimizeTask!);

        Assert.False(vm.Ui.Running);
        Assert.Contains("停止しました", vm.Ui.Message);
    }

    [Fact]
    public void Stop_WhileRunningFlagSet_ResetsRunningAndLogsWhichJob()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("勤務表づくり");
        vm.Ui.Running = true;

        vm.Stop();

        Assert.False(vm.Ui.Running);
        Assert.Equal("停止しました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("停止を押しました") && l.Contains("勤務表づくり"));
    }
}
