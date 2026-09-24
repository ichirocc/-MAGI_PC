using MagiApp.ViewModels.Services;
using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [S5] 「この希望を取り消したら」試算と確定の VM（<c>MagiViewModel.WishTrial.cs</c>、<c>docs/s5_wish_trial.md</c> §12 V1〜V12）。
/// 盤面は 2 職員×7 日・全セル休に、満たされていない希望 2 件（pref）。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelWishTrialTest : IDisposable
{
    private readonly List<string> _dirs = new();

    public MagiViewModelWishTrialTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    private string FreshTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "magi-vm-wishtrial-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    private sealed class FakeOptimizationService : IOptimizationService
    {
        public int OptimizeCallCount { get; private set; }
        public MagiState? SeenState { get; private set; }
        public Func<MagiState, int[][], V6FinalPort.ActionResult>? Result { get; set; }
        public Exception? ThrowInstead { get; set; }
        public bool HangUntilCancelled { get; set; }

        public async Task<V6FinalPort.ActionResult> OptimizeAsync(
            MagiState state, int[][] schedule, int secondsRaw, int? workers, bool softPolish,
            V6Algorithm requestedAlgorithm, bool allowImpossible,
            Action<string, ViolationReport?, long, long>? onProgress, CancellationToken cancellationToken)
        {
            OptimizeCallCount++;
            SeenState = state;
            if (HangUntilCancelled) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (ThrowInstead is not null) throw ThrowInstead;
            return Result!(state, schedule);
        }

        public Task<int[][]> SoftPolishAsync(MagiState state, int[][] schedule, int seconds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyS = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, int> EmptyI = new Dictionary<string, int>();

    private static V6FinalPort.ActionResult SameBoard(MagiState st, int[][] sch) => new(
        Schedule: sch.Copy2D(), Report: UnifiedViolationChecker.Check(st, sch.Copy2D()), Phase: "test:Fake",
        BusyDetail: new V6FinalPort.BusyDetail("Fake", "2名 x 7日", "fake"), Logs: Array.Empty<MirrorLog>());

    private static V6FinalPort.ActionResult Worse(MagiState st, int[][] sch) => new(
        Schedule: sch.Copy2D(), Report: new ViolationReport(EmptyS, EmptyS, EmptyS, EmptyI, Total: 999, Hard: 99, Soft: 900, WeightedScore: 999),
        Phase: "test:Fake", BusyDetail: new V6FinalPort.BusyDetail("Fake", "2名 x 7日", "fake"), Logs: Array.Empty<MirrorLog>());

    /// <summary>B より厳密に良い結果: 残る希望 (1,3) を満たす（ns 基準で必須 0）。</summary>
    private static readonly int[][] BetterBoard = { new[] { 0, 0, 0, 0, 0, 0, 0 }, new[] { 0, 0, 0, 1, 0, 0, 0 } };

    private static V6FinalPort.ActionResult Better(MagiState st, int[][] _) => SameBoard(st, BetterBoard);

    /// <summary>採用の書き込みの後（他の案の取り込み）で投げる: 行が null の案は複製で落ちる。</summary>
    private static V6FinalPort.ActionResult BetterThenAltThrows(MagiState st, int[][] sch) =>
        Better(st, sch) with { Alternatives = new[] { new int[][] { null!, null! } } };

    private (MagiViewModel Vm, FakeOptimizationService Fake) NewVm(Func<MagiState, int[][], V6FinalPort.ActionResult>? result = null)
    {
        var st = MinimalState.Build(wishes: new Dictionary<string, int> { ["0,2"] = 1, ["1,3"] = 1 });
        var fake = new FakeOptimizationService { Result = result ?? SameBoard };
        var vm = new MagiViewModel(fake) { DataDir = FreshTempDir(), _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm._hydrated = true;
        vm.Ui.Wishes = st.Wishes;
        return (vm, fake);
    }

    private static async Task<WishTrialView.Ready> TrialReady(MagiViewModel vm, int i, int j, int k)
    {
        vm.StartWishTrial(i, j);
        await vm.LastWishTrialTask!;
        return Assert.IsType<WishTrialView.Ready>(vm.WishTrialFor(i, j, k));
    }

    private static int CheckHard(MagiState st, int[][] sch) => UnifiedViolationChecker.Check(st, sch.Copy2D()).Hard;

    [Fact]
    public async Task V1_ConfirmThenUndoOnce_RestoresWishAndBoard()
    {
        var (vm, _) = NewVm(Better);
        var st0 = vm._state!;
        var b0 = vm._currentSchedule!.Copy2D();
        var ready = await TrialReady(vm, 0, 2, 1);
        var undo0 = vm.UndoStackCount;

        vm.CancelWishAndRebuild(ready.Token);
        await vm.LastRunOptimizeTask!;
        Assert.Equal(undo0 + 1, vm.UndoStackCount);
        Assert.Equal(BetterBoard, vm._currentSchedule);

        vm.Undo();
        Assert.Same(st0, vm._state);
        Assert.True(vm._state!.Wishes.ContainsKey("0,2"));
        Assert.Equal(b0, vm._currentSchedule);
    }

    [Fact]
    public async Task V2_StaleTokenAfterBoardOrStateChange_ChangesNothing()
    {
        var (vm, fake) = NewVm();
        var ready = await TrialReady(vm, 0, 2, 1);
        vm._currentSchedule = new[] { new[] { 1, 0, 0, 0, 0, 0, 0 }, new[] { 0, 0, 0, 0, 0, 0, 0 } };
        var st = vm._state;
        var undo0 = vm.UndoStackCount;

        vm.CancelWishAndRebuild(ready.Token);

        Assert.Equal(undo0, vm.UndoStackCount);
        Assert.Same(st, vm._state);
        Assert.Equal(0, fake.OptimizeCallCount);
        Assert.Contains("もう一度試算", vm.Ui.Message);

        vm._currentSchedule = MinimalState.BuildSchedule();
        vm._state = vm._state! with { };   // 構造は同じでも別の参照＝照合しない
        vm.CancelWishAndRebuild(ready.Token);
        Assert.Equal(0, fake.OptimizeCallCount);
        Assert.Equal(undo0, vm.UndoStackCount);
    }

    [Fact]
    public async Task V3_Stop_KeepsWishCancelledAndBoard()
    {
        var (vm, fake) = NewVm();
        fake.HangUntilCancelled = true;
        var b0 = vm._currentSchedule!.Copy2D();
        var ready = await TrialReady(vm, 0, 2, 1);

        vm.CancelWishAndRebuild(ready.Token);
        Assert.False(vm.Ui.Wishes.ContainsKey("0,2"));   // I14: 確定の瞬間から画面の希望は ns
        vm.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.LastRunOptimizeTask!);

        Assert.False(vm._state!.Wishes.ContainsKey("0,2"));
        Assert.Equal(b0, vm._currentSchedule);
        Assert.Contains("希望の取り消しはそのままです", vm.Ui.Message);
        Assert.False(vm.Ui.Wishes.ContainsKey("0,2"));
        Assert.Equal(ready.Token.Result!.Hx, vm.Ui.BestHard);
    }

    [Fact]
    public async Task V4_StructureEdited_ExportCarriesCancellation()
    {
        var (vm, _) = NewVm();
        var ready = await TrialReady(vm, 0, 2, 1);

        vm.CancelWishAndRebuild(ready.Token);
        Assert.True(vm.Ui.StructureEdited);
        await vm.LastRunOptimizeTask!;

        var exported = StateJsonSerializer.Parse(vm.ExportJson()!);
        Assert.False(exported.Wishes.ContainsKey("0,2"));
        Assert.True(exported.Wishes.ContainsKey("1,3"));
    }

    [Fact]
    public async Task V5_TrialDoesNotWriteState()
    {
        var (vm, _) = NewVm();
        var st0 = vm._state;
        var b0 = vm._currentSchedule!.Copy2D();
        var undo0 = vm.UndoStackCount;

        await TrialReady(vm, 0, 2, 1);

        Assert.Same(st0, vm._state);
        Assert.Equal(b0, vm._currentSchedule);
        Assert.Equal(undo0, vm.UndoStackCount);
        Assert.False(vm.Ui.Running);
        Assert.Null(vm.Ui.WishTrialBusy);
    }

    [Fact]
    public async Task V6_HardAfterConfirm_IsAtMostHx()
    {
        var (vm, _) = NewVm(Worse);
        var ready = await TrialReady(vm, 0, 2, 1);

        vm.CancelWishAndRebuild(ready.Token);
        await vm.LastRunOptimizeTask!;

        Assert.True(CheckHard(vm._state!, vm._currentSchedule!) <= ready.Token.Result!.Hx);
        Assert.True(vm.Ui.BestHard <= ready.Token.Result!.Hx);
    }

    [Fact]
    public async Task V7_Guards_RejectBeforeAnyWrite()
    {
        // 実行中
        var (vm, fake) = NewVm();
        var ready = await TrialReady(vm, 0, 2, 1);
        var st = vm._state;
        var token = vm.BeginBoardJob("読み込み");
        vm.CancelWishAndRebuild(ready.Token);
        Assert.Equal(0, vm.UndoStackCount);
        Assert.Same(st, vm._state);
        Assert.Equal(0, fake.OptimizeCallCount);
        vm.EndBoardJob(token);

        // 照合の不一致（希望の値が違う）
        vm.CancelWishAndRebuild(ready.Token with { Shift = 0 });
        Assert.Equal(0, vm.UndoStackCount);
        Assert.Same(st, vm._state);
        Assert.Equal(0, fake.OptimizeCallCount);

        // EnsureValidForRun が偽（盤面の値が範囲外）
        var bad = new[] { new[] { 0, 0, 0, 0, 0, 0, 9 }, new[] { 0, 0, 0, 0, 0, 0, 0 } };
        vm._currentSchedule = bad;
        vm.CancelWishAndRebuild(ready.Token with { BoardKey = MagiViewModel.BoardKey(bad) });
        Assert.Equal(0, vm.UndoStackCount);
        Assert.Same(st, vm._state);
        Assert.Equal(0, fake.OptimizeCallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V8_AfterCompletion_WishKeyGoneFromStateAndUi(bool kept)
    {
        var (vm, fake) = NewVm(kept ? Worse : SameBoard);
        var ready = await TrialReady(vm, 0, 2, 1);

        vm.CancelWishAndRebuild(ready.Token);
        await vm.LastRunOptimizeTask!;

        Assert.False(fake.SeenState!.Wishes.ContainsKey("0,2"));
        Assert.False(vm._state!.Wishes.ContainsKey("0,2"));
        Assert.False(vm.Ui.Wishes.ContainsKey("0,2"));
        Assert.Contains("希望（職員A 3日 A）を取り消", vm.Ui.Message);
        Assert.NotNull(vm.WishCancelOutcomeLine());
        Assert.Equal(kept, vm.Ui.RunSummary is null);
    }

    [Fact]
    public async Task V9_Failure_ShowsHxAndSuffixWithWishGone()
    {
        var (vm, fake) = NewVm();
        fake.ThrowInstead = new InvalidOperationException("boom");
        var ready = await TrialReady(vm, 0, 2, 1);

        vm.CancelWishAndRebuild(ready.Token);
        await vm.LastRunOptimizeTask!;

        Assert.False(vm.Ui.Wishes.ContainsKey("0,2"));
        Assert.Equal(ready.Token.Result!.Hx, vm.Ui.BestHard);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("勤務表をつくれませんでした", vm.Ui.Message);
        Assert.Contains("希望の取り消しはそのままです", vm.Ui.Message);
    }

    [Fact]
    public async Task V10_EditMakesStale_ConfirmUndoRestoresOtherRows()
    {
        var (vm, _) = NewVm();
        var other = await TrialReady(vm, 1, 3, 1);
        var ready = await TrialReady(vm, 0, 2, 1);

        vm.CancelWishAndRebuild(ready.Token);
        await vm.LastRunOptimizeTask!;
        Assert.Same(WishTrialView.Stale, vm.WishTrialFor(1, 3, 1));

        vm.Undo();
        var back = Assert.IsType<WishTrialView.Ready>(vm.WishTrialFor(1, 3, 1));
        Assert.Equal(other.Outcome, back.Outcome);

        vm.SetCell(0, 0, 1);
        Assert.Same(WishTrialView.Stale, vm.WishTrialFor(1, 3, 1));
    }

    [Fact]
    public async Task V11_CancelledTrial_DoesNotStoreResult()
    {
        var (vm, _) = NewVm();
        vm.StartWishTrial(0, 2);
        var task = vm.LastWishTrialTask!;
        vm.CancelWishTrial();
        await task;

        Assert.Same(WishTrialView.None, vm.WishTrialFor(0, 2, 1));
        Assert.Null(vm.Ui.WishTrialBusy);
    }

    [Fact]
    public async Task V12_Autosave_NeverHasWishWithResult()
    {
        var (vm, _) = NewVm(Better);
        var b0 = vm._currentSchedule!.Copy2D();
        var ready = await TrialReady(vm, 0, 2, 1);
        var path = Path.Combine(vm.DataDir, "magi_autosave.json");

        vm.CancelWishAndRebuild(ready.Token);
        var mid = StateJsonSerializer.Parse(File.ReadAllText(path));
        Assert.False(mid.Wishes.ContainsKey("0,2"));
        Assert.Equal(b0.Select(r => (IReadOnlyList<int>)r.ToList()), mid.Schedule, new RowComparer());

        await vm.LastRunOptimizeTask!;
        await vm.LastAutoSaveTask!;
        var done = StateJsonSerializer.Parse(File.ReadAllText(path));
        Assert.False(done.Wishes.ContainsKey("0,2"));
        Assert.Equal(BetterBoard.Select(r => (IReadOnlyList<int>)r.ToList()), done.Schedule, new RowComparer());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LateFailure_AfterAdoption_ShowsAdoptedBoardWithLateMessage(bool s5)
    {
        var (vm, _) = NewVm(BetterThenAltThrows);
        if (s5)
        {
            var ready = await TrialReady(vm, 0, 2, 1);
            vm.CancelWishAndRebuild(ready.Token);
        }
        else
        {
            vm.RunV6FullOptimize();
        }
        await vm.LastRunOptimizeTask!;

        Assert.Equal(BetterBoard, vm._currentSchedule);
        Assert.Equal(BetterBoard.Select(r => (IReadOnlyList<int>)r.ToList()), vm.Ui.Schedule, new RowComparer());
        Assert.Equal(CheckHard(vm._state!, BetterBoard), vm.Ui.BestHard);
        Assert.False(vm.Ui.Running);
        Assert.True(vm.Ui.HasResult);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("勤務表の作成は終わりましたが", vm.Ui.Message);
        Assert.DoesNotContain("つくれませんでした", vm.Ui.Message);
        Assert.Equal(s5, vm.Ui.Message!.Contains("希望の取り消しはそのままです"));
        Assert.Equal(!s5, vm.Ui.Wishes.ContainsKey("0,2"));
    }

    [Fact]
    public async Task StopOrBoardJob_WhileTrialRunning_ClearsBusy()
    {
        var (vm, _) = NewVm();
        vm.StartWishTrial(0, 2);
        var first = vm.LastWishTrialTask!;
        vm.Stop();
        Assert.Null(vm.Ui.WishTrialBusy);

        vm.StartWishTrial(0, 2);
        var second = vm.LastWishTrialTask!;
        var token = vm.BeginBoardJob("読み込み");
        Assert.Null(vm.Ui.WishTrialBusy);
        vm.EndBoardJob(token);

        await Task.WhenAll(first, second);
        Assert.Null(vm.Ui.WishTrialBusy);
    }

    [Fact]
    public async Task StalledFamilies_RestoredOnUndoToPreConfirmBoard()
    {
        var (vm, _) = NewVm();
        var ready = await TrialReady(vm, 0, 2, 1);
        vm.Ui.StalledHardFamilies = new[] { "pref" };

        vm.CancelWishAndRebuild(ready.Token);
        await vm.LastRunOptimizeTask!;
        vm.Undo();

        Assert.Equal(new[] { "pref" }, vm.Ui.StalledHardFamilies);
    }

    [Fact]
    public void StartWishTrial_RefusedWhileOptimizeInFlight()
    {
        var (vm, _) = NewVm();
        vm.BeginBoardJob("読み込み");

        vm.StartWishTrial(0, 2);

        Assert.Null(vm.LastWishTrialTask);
        Assert.Null(vm.Ui.WishTrialBusy);
    }

    private sealed class RowComparer : IEqualityComparer<IReadOnlyList<int>>
    {
        public bool Equals(IReadOnlyList<int>? x, IReadOnlyList<int>? y) => x!.SequenceEqual(y!);
        public int GetHashCode(IReadOnlyList<int> obj) => obj.Count;
    }
}
