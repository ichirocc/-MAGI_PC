using MagiApp.ViewModels.Services;
using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [Phase 10 本体] <see cref="MagiViewModel.RunInBackground"/>/<see cref="MagiViewModel.ApplyBgResult"/>
/// （<c>MagiViewModel.Background.cs</c>、Kotlin原本 <c>runInBackground()</c>/<c>applyBgResult()</c>/
/// <c>OptimizationWorker.doWork()</c> の移植）の検証。
///
/// <see cref="MagiViewModelOptimizeTest"/> と同じ規約に従う: <see cref="FakeOptimizationService"/>
/// を注入し実エンジンの探索を待たない。<c>[Collection("OptimizationRepositoryState")]</c>
/// （<see cref="Work.OptimizationRepository"/> はプロセス全体で共有される static——
/// <c>TestSupport/SerialCollections.cs</c> 参照）。
/// [2026-09-01] <c>RunInBackground</c> は当初開始時に同期的なファイルI/Oを行っていたため
/// <see cref="MagiViewModel.DataDir"/> の隔離が必須だったが、kill耐性の全撤去によりディスクI/Oは
/// 無くなった。<c>FreshTempDir</c> は自動保存（<c>AutoSave</c>）が触れる先を隔離する目的で維持する。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelBackgroundTest : IDisposable
{
    private readonly List<string> _dirs = new();

    public MagiViewModelBackgroundTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    public void Dispose()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private string FreshTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "magi-vm-bg-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    private sealed class FakeOptimizationService : IOptimizationService
    {
        public int OptimizeCallCount { get; private set; }
        public Func<MagiState, int[][], V6FinalPort.ActionResult>? Result { get; set; }
        public Exception? ThrowInstead { get; set; }
        public bool HangUntilCancelled { get; set; }
        public bool RequestedSoftPolish { get; private set; }
        public V6Algorithm RequestedAlgorithm { get; private set; }

        public async Task<V6FinalPort.ActionResult> OptimizeAsync(
            MagiState state, int[][] schedule, int secondsRaw, int? workers, bool softPolish,
            V6Algorithm requestedAlgorithm, bool allowImpossible,
            Action<string, ViolationReport?, long, long>? onProgress, CancellationToken cancellationToken)
        {
            OptimizeCallCount++;
            RequestedSoftPolish = softPolish;
            RequestedAlgorithm = requestedAlgorithm;
            onProgress?.Invoke("テスト探索中", null, 0, 0);
            if (HangUntilCancelled) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (ThrowInstead is not null) throw ThrowInstead;
            return Result!(state, schedule);
        }

        public Task<int[][]> SoftPolishAsync(MagiState state, int[][] schedule, int seconds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("背景実行では使わない経路");
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyS = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, int> EmptyI = new Dictionary<string, int>();

    private static ViolationReport Report(int hard, int total) =>
        new(EmptyS, EmptyS, EmptyS, EmptyI, Total: total, Hard: hard, Soft: total, WeightedScore: total);

    private static V6FinalPort.ActionResult ActionResult(int hard, int total) => new(
        Schedule: MinimalState.BuildSchedule(),
        Report: Report(hard, total),
        Phase: "test:Fake",
        BusyDetail: new V6FinalPort.BusyDetail("Fake", "2名 x 7日", $"HARD {hard}件"),
        Logs: Array.Empty<MirrorLog>());

    [Fact]
    public void NoStateLoaded_DoesNotCallServiceAndStaysIdle()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir() };

        vm.RunInBackground();

        Assert.Equal(0, fake.OptimizeCallCount);
        Assert.False(vm.Ui.Running);
        Assert.Null(vm.LastRunInBackgroundTask);
    }

    [Fact]
    public void BlockedWhileAnotherBoardJobInFlight_DoesNotCallServiceAndNotifies()
    {
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.BeginBoardJob("読み込み");

        vm.RunInBackground();

        Assert.Equal(0, fake.OptimizeCallCount);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("実行中です", vm.Ui.Message);
    }

    [Fact]
    public void SecondCall_WhileFirstBackgroundRunInFlight_IsBlocked()
    {
        // [クラスKDoc「Kotlin原本との差①」検証] OptimizationRepository.SetRunning(true) を
        // RunInBackground() が同期的に立てるため、Task がまだ完了していなくても
        // 2回目の呼出しは RunBlockedByInFlight で弾かれるはず。
        var fake = new FakeOptimizationService { HangUntilCancelled = true };
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunInBackground();
        Assert.NotNull(vm.LastRunInBackgroundTask);
        var firstTask = vm.LastRunInBackgroundTask;

        vm.RunInBackground();

        Assert.Same(firstTask, vm.LastRunInBackgroundTask); // 2回目は新しい Task を起動していない
        Assert.Equal(1, fake.OptimizeCallCount);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("実行中です", vm.Ui.Message);

        vm.Stop();
    }

    [Fact]
    public async Task ImprovedResult_IsAdopted()
    {
        var fake = new FakeOptimizationService { Result = (_, _) => ActionResult(hard: 0, total: 0) };
        var st = MinimalState.Build();
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunInBackground();
        Assert.NotNull(vm.LastRunInBackgroundTask);
        await vm.LastRunInBackgroundTask!;

        Assert.Equal(1, fake.OptimizeCallCount);
        // [Kotlin原本との差なし・逐語検証] Worker.doWork() は softPolish/requestedAlgorithm を
        //   指定しない＝V6FinalPort.handleOptimize の既定値(false/AUTO)のまま——前景の設定を継承しない。
        Assert.False(fake.RequestedSoftPolish);
        Assert.Equal(V6Algorithm.Auto, fake.RequestedAlgorithm);
        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("バックグラウンド最適化 完了", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("バックグラウンド最適化 完了"));
        Assert.False(OptimizationRepository.Running);
    }

    [Fact]
    public async Task WorseResult_KeepsPreviousResultAndDiscardsNewOne()
    {
        // [_resultSchedule は private] 直接注入できないため、まず1回目のバックグラウンド実行で
        // 良い結果(hard=0/total=0)を採用させて _resultSchedule を確立し、2回目の実行が
        // それより悪い結果(total=100)を返す状況を再現する——実運用の「再実行」と同じ経路。
        var fake = new FakeOptimizationService { Result = (_, _) => ActionResult(hard: 0, total: 0) };
        var st = MinimalState.Build();
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm.RunInBackground();
        await vm.LastRunInBackgroundTask!;
        Assert.True(vm.Ui.HasResult);

        fake.Result = (_, _) => ActionResult(hard: 0, total: 100);
        vm.RunInBackground();
        await vm.LastRunInBackgroundTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.Contains("前回の結果を維持しました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("前回を維持"));
    }

    [Fact]
    public async Task Cancelled_ReportsStoppedAndReleasesRunningFlag()
    {
        var fake = new FakeOptimizationService { HangUntilCancelled = true };
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunInBackground();
        Assert.True(vm.Ui.Running);
        Assert.True(OptimizationRepository.Running);

        vm.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.LastRunInBackgroundTask!);

        Assert.False(vm.Ui.Running);
        Assert.False(OptimizationRepository.Running);
        // [発見した実装上の注記] Task.Delay(Timeout.Infinite, ct) を Cancel() すると、その継続
        // （RunInBackgroundCoreAsync の catch(OperationCanceledException)〜finally）は .NET の
        // CancellationTokenSource.Cancel() の仕様どおり Stop() を呼んだスレッド上で**同期的に**
        // 走り切る（実エンジンは深い探索ループの中で ct を定期確認するため、この同期的な巻き戻りは
        // 起きない＝このテストの HangUntilCancelled という単純化に起因する）。そのため Stop() 自身の
        // 「停止を押しました（対象: ...）」ログに辿り着く前に Ui.Message が背景タスク側のより具体的な
        // メッセージへ確定する。「対象」ラベリング自体の検証は
        // Stop_WhileBackgroundRunningFlagSet_LogsBackgroundAsTarget で行う。
        Assert.Contains("バックグラウンド計算を停止しました", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("バックグラウンド計算: 停止"));
    }

    [Fact]
    public void Stop_WhileBackgroundRunningFlagSet_LogsBackgroundAsTarget()
    {
        // [Stop() の「対象」ラベリング修正の検証] 背景実行には _boardJobLabel が無い
        // （MagiViewModel.Background.cs クラスKDoc参照）ため、OptimizationRepository.Running を
        // 見て「バックグラウンド最適化」と正しく名指しできることを、実タスクを介さず直接検証する
        // （MagiViewModelOptimizeTest.Stop_WhileRunningFlagSet_ResetsRunningAndLogsWhichJob と同じ手法）。
        var fake = new FakeOptimizationService();
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        OptimizationRepository.SetRunning(true);
        vm.Ui.Running = true;

        vm.Stop();

        Assert.False(vm.Ui.Running);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("停止を押しました") && l.Contains("バックグラウンド最適化"));
    }

    [Fact]
    public async Task Failed_ReportsErrorAndReleasesRunningFlag()
    {
        var fake = new FakeOptimizationService { ThrowInstead = new InvalidOperationException("boom") };
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.RunInBackground();
        await vm.LastRunInBackgroundTask!;

        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("バックグラウンド計算に失敗しました", vm.Ui.Message);
        Assert.False(OptimizationRepository.Running);
    }

    // ===== ApplyBgResult の指紋ガード（[テスト可視性のためinternal化] を利用した単体検証） =====

    [Fact]
    public async Task ApplyBgResult_MismatchedRunId_IsDiscardedAsReplacedRun()
    {
        var fake = new FakeOptimizationService { HangUntilCancelled = true };
        var st = MinimalState.Build();
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm.RunInBackground(); // _bgRunId を確定させる（値は非公開のため直接比較しない）

        var bogus = new OptimizationRepository.BgResult(MinimalState.BuildSchedule(), Report(0, 0), "test", RunId: -999L);
        await vm.ApplyBgResult(bogus);

        Assert.Contains(vm.Ui.OpLog, l => l.Contains("置き換えられた古い実行の結果"));
        // 破棄されたので HasResult は立たない（このテストでは他に結果を反映していない）。
        Assert.False(vm.Ui.HasResult);

        vm.Stop();
    }

    [Fact]
    public async Task ApplyBgResult_StateChangedSinceStart_IsDiscardedWithMessage()
    {
        var fake = new FakeOptimizationService { HangUntilCancelled = true };
        var st = MinimalState.Build();
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm.RunInBackground();
        // 実行中に入力が変わった状況を模擬(職員名を変える等ではなく、単純に別の State に差し替える)。
        vm._state = MinimalState.Build(startDate: "2025-12-08", endDate: "2025-12-14");

        var stale = new OptimizationRepository.BgResult(MinimalState.BuildSchedule(), Report(0, 0), "test", RunId: 0L);
        await vm.ApplyBgResult(stale);

        Assert.Contains("結果は反映しませんでした", vm.Ui.Message);
        Assert.False(vm.Ui.Running);
        Assert.False(vm.Ui.HasResult);

        vm.Stop();
    }
}
