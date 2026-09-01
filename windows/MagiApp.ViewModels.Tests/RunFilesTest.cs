using MagiApp.ViewModels.Services;
using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [Phase 10] <see cref="RunFiles"/>（<c>work/RunFiles.kt</c> の移植）と、それを使う
/// <c>MagiViewModel.RunMarker.cs</c>（実行中マーカーの読み書き・共有ファイルの後片付け・
/// 起動時の中断検知）の検証。
///
/// Kotlin原本 <c>RunFilesTest.kt</c> の存在理由がそのまま当てはまる——このファイル層で守るべき性質
/// （所有権の判定・後片付けの網羅・原子置換・「マーカーが残っていたら中断」）は、プラットフォーム固有の
/// 何かではなく**ディレクトリ1つ**で表現できるので、テストできる場所に置いて初めて再発防止になる。
/// とくに <see cref="RunFiles.Clear"/> の「4ファイルすべて」は、1つ足し忘れると次回起動が古い状態を
/// 掴むという静かな失敗になるため、「clear 後にディレクトリが空」で網羅を固定する。
///
/// ファイルI/Oを伴うため、テストごとに <see cref="FreshTempDir"/> で隔離した一時ディレクトリを使い、
/// 既定の <c>LocalApplicationData</c>（実ユーザーのホーム）には一切触れない
/// （<see cref="MagiViewModelPersistenceTest"/> と同じ規約）。ViewModel を運動させるテストは
/// <see cref="Work.OptimizationRepository"/> のプロセス共有 static 状態を読むため、同じ直列コレクションに属する。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class RunFilesTest : IDisposable
{
    private readonly List<string> _dirs = new();

    public RunFilesTest()
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
        var dir = Path.Combine(Path.GetTempPath(), "magi-runfiles-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    /// <summary>4ファイルすべてに中身を置く（<see cref="RunFiles.Clear"/> の網羅検証用）。</summary>
    private static void FillAll(RunFiles f)
    {
        File.WriteAllText(f.Input, "{}");
        File.WriteAllText(f.Result, "{}");
        File.WriteAllText(f.Snapshot, "{}");
        File.WriteAllText(f.RunId, "7");
    }

    // ===================================================================
    // RunFiles — 所有権マーカー（beginRun / activeRunId / owns）
    // ===================================================================

    [Fact]
    public void BeginRunWritesTheIdAndActiveRunIdReadsItBack()
    {
        var f = new RunFiles(FreshTempDir());

        Assert.True(f.BeginRun(4242L));

        Assert.True(File.Exists(f.RunId));
        Assert.Equal(4242L, f.ActiveRunId());
    }

    [Fact]
    public void BeginRunCreatesTheDirectoryWhenItDoesNotExistYet()
    {
        // [プラットフォーム置換の検証] Android の filesDir と違い、Windows の DataDir は初回起動時に
        // まだ存在しない。存在しなければ作る（作れなければ false を返す）。
        var dir = Path.Combine(FreshTempDir(), "not-yet");
        var f = new RunFiles(dir);

        Assert.True(f.BeginRun(1L));

        Assert.True(Directory.Exists(dir));
        Assert.Equal(1L, f.ActiveRunId());
    }

    [Fact]
    public void ActiveRunIdIsZeroWhenTheMarkerIsAbsentOrCorrupt()
    {
        var f = new RunFiles(FreshTempDir());
        Assert.Equal(0L, f.ActiveRunId());   // 記録が無い

        File.WriteAllText(f.RunId, "これは数値ではない");
        Assert.Equal(0L, f.ActiveRunId());   // 壊れている
    }

    [Fact]
    public void OwnsTreatsIdZeroAsOwnerAndRejectsAReplacedRun()
    {
        var f = new RunFiles(FreshTempDir());
        f.BeginRun(100L);

        Assert.True(f.Owns(0L));     // runId を持たない旧経路は従来どおり所有者（非破壊）
        Assert.True(f.Owns(100L));   // 自分が所有者
        Assert.False(f.Owns(99L));   // 置き換えられた旧実行は書きも消しもしない
    }

    [Fact]
    public void OwnsIsFalseForANamedRunOnceTheMarkerIsGone()
    {
        var f = new RunFiles(FreshTempDir());
        f.BeginRun(100L);

        f.Clear();   // 停止＝所有権マーカーごと消える

        Assert.False(f.Owns(100L));
        Assert.True(f.Owns(0L));
    }

    // ===================================================================
    // RunFiles — 後片付け（clear）
    // ===================================================================

    [Fact]
    public void ClearRemovesAllFourFilesAndLeavesTheDirectoryEmpty()
    {
        var dir = FreshTempDir();
        var f = new RunFiles(dir);
        FillAll(f);

        var stuck = f.Clear();

        Assert.Empty(stuck);
        // 「4ファイルすべて」の網羅を固定する（1つ足し忘れると次回起動が古い状態を掴む）。
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public void ClearWithKeepRunIdKeepsOnlyTheOwnershipMarker()
    {
        var f = new RunFiles(FreshTempDir());
        FillAll(f);

        var stuck = f.Clear(keepRunId: true);

        Assert.Empty(stuck);
        Assert.False(File.Exists(f.Input));
        Assert.False(File.Exists(f.Result));
        Assert.False(File.Exists(f.Snapshot));
        // [3.410.0/U-02] 自分で立てたばかりの所有権を自分で捨ててはいけない。
        Assert.True(File.Exists(f.RunId));
    }

    [Fact]
    public void ClearIsSafeAndSilentWhenTheFilesAreAlreadyAbsent()
    {
        var dir = FreshTempDir();
        var f = new RunFiles(dir);

        var stuck = f.Clear();          // 1回目：そもそも何も無い
        var stuckAgain = f.Clear();     // 2回目：冪等

        Assert.Empty(stuck);
        Assert.Empty(stuckAgain);
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    // [覆えていない分岐を正直に] [3.410.0/B-06] の「消し残った名前を返す」経路
    // （<see cref="RunFiles.Clear"/> が空でないリストを返し、ViewModel 側が警告ログを出す）は
    // ここでは検証できていない。削除を確実に失敗させる可搬な方法が無いため——このサンドボックスの
    // テストは root で走るのでパーミッションでは止まらず、対象パスをディレクトリにすり替えても
    // <c>File.Exists</c> が false を返して「消すものが無い」と扱われる（RunFiles.cs のKDoc
    // 「Kotlin原本との微差」参照）。Kotlin原本の <c>RunFilesTest</c> も同じ理由でこの分岐は
    // 持っていない。契約そのもの（返り値を読んで記録する）は
    // <see cref="MagiViewModel.DismissInterrupted"/> 等の呼出側コードで固定されている。

    // ===================================================================
    // RunFiles — 原子置換（writeAtomically は AtomicFileWrite への委譲）
    // ===================================================================

    [Fact]
    public void WriteAtomicallyRoundTripsAndLeavesNoTemporaryFile()
    {
        var dir = FreshTempDir();
        var f = new RunFiles(dir);

        Assert.True(f.WriteAtomically(f.Snapshot, "{\"a\":1}"));

        Assert.Equal("{\"a\":1}", File.ReadAllText(f.Snapshot));
        Assert.Equal(new[] { f.Snapshot }, Directory.GetFiles(dir));   // .tmp の残骸なし
    }

    [Fact]
    public void WriteAtomicallyWithAFalseCommitGuardDoesNotTouchTheTarget()
    {
        // [3.385.0] 所有権の再確認をコミット直前に置くための穴。false のときは対象へ一切触れない。
        var dir = FreshTempDir();
        var f = new RunFiles(dir);
        File.WriteAllText(f.Result, "既存の中身");

        Assert.False(f.WriteAtomically(f.Result, "新しい中身", commitGuard: () => false));

        Assert.Equal("既存の中身", File.ReadAllText(f.Result));
        Assert.Equal(new[] { f.Result }, Directory.GetFiles(dir));
    }

    // ===================================================================
    // MagiViewModel — 実行中マーカーの書き込みと消去
    // ===================================================================

    private sealed class FakeOptimizationService : IOptimizationService
    {
        public bool HangUntilCancelled { get; set; }

        public async Task<V6FinalPort.ActionResult> OptimizeAsync(
            MagiState state, int[][] schedule, int secondsRaw, int? workers, bool softPolish,
            V6Algorithm requestedAlgorithm, bool allowImpossible,
            Action<string, ViolationReport?, long, long>? onProgress, CancellationToken cancellationToken)
        {
            if (HangUntilCancelled) await Task.Delay(Timeout.Infinite, cancellationToken);
            var empty = new Dictionary<string, string>();
            return new V6FinalPort.ActionResult(
                Schedule: MinimalState.BuildSchedule(),
                Report: new ViolationReport(empty, empty, empty, new Dictionary<string, int>(),
                    Total: 0, Hard: 0, Soft: 0, WeightedScore: 0),
                Phase: "test:Fake",
                BusyDetail: new V6FinalPort.BusyDetail("Fake", "2名 x 7日", "HARD 0件"),
                Logs: Array.Empty<MirrorLog>());
        }

        public Task<int[][]> SoftPolishAsync(
            MagiState state, int[][] schedule, int seconds, CancellationToken cancellationToken) =>
            Task.FromResult(schedule);
    }

    private MagiViewModel NewVmWithState(IOptimizationService svc) =>
        new(svc)
        {
            DataDir = FreshTempDir(),
            _state = MinimalState.Build(),
            _currentSchedule = MinimalState.BuildSchedule(),
        };

    private static string MarkerPath(MagiViewModel vm) => Path.Combine(vm.DataDir, "magi_run_marker.json");

    [Fact]
    public async Task RunV6FullOptimizeWritesTheRunMarkerAtStartAndClearsItWhenTheRunEnds()
    {
        var vm = NewVmWithState(new FakeOptimizationService { HangUntilCancelled = true });

        vm.RunV6FullOptimize();
        // マーカーは開始と同時（同期的に）書かれる——kill されてから書いても意味が無い。
        Assert.True(File.Exists(MarkerPath(vm)));

        vm.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.LastRunOptimizeTask!);

        Assert.False(File.Exists(MarkerPath(vm)));
    }

    [Fact]
    public async Task RunV6FullOptimizeClearsTheRunMarkerAfterANormalCompletion()
    {
        var vm = NewVmWithState(new FakeOptimizationService());

        vm.RunV6FullOptimize();
        await vm.LastRunOptimizeTask!;

        // 正常終了・停止・失敗のいずれでもマーカーは消える（中断のときだけ残る）。
        Assert.False(File.Exists(MarkerPath(vm)));
    }

    [Fact]
    public async Task RunSoftPolishAlsoWritesAndClearsTheRunMarker()
    {
        var vm = NewVmWithState(new FakeOptimizationService());

        vm.RunSoftPolish();
        Assert.True(File.Exists(MarkerPath(vm)));
        await vm.LastRunSoftPolishTask!;

        Assert.False(File.Exists(MarkerPath(vm)));
    }

    [Fact]
    public void RunV6FullOptimizeClearsStaleBackgroundFilesAtStart()
    {
        // [C1] 前景実行では背景の途中状態は無関係＝掃除する（残すと次回起動が拾ってしまう）。
        var vm = NewVmWithState(new FakeOptimizationService { HangUntilCancelled = true });
        var files = new RunFiles(vm.DataDir);
        FillAll(files);

        vm.RunV6FullOptimize();

        Assert.False(File.Exists(files.Input));
        Assert.False(File.Exists(files.Result));
        Assert.False(File.Exists(files.Snapshot));
        vm.Stop();
    }

    [Fact]
    public void DismissInterruptedDeletesTheBackgroundFiles()
    {
        var vm = new MagiViewModel { DataDir = FreshTempDir(), _state = MinimalState.Build() };
        var files = new RunFiles(vm.DataDir);
        FillAll(files);
        vm.Ui.InterruptedRun = true;
        vm.Ui.InterruptedInfo = "何か中断情報";

        vm.DismissInterrupted();

        Assert.False(vm.Ui.InterruptedRun);
        Assert.Null(vm.Ui.InterruptedInfo);
        // 破棄したのに途中状態が残ると、次回起動が同じ「中断されました」を再び掴む。
        Assert.Empty(Directory.GetFileSystemEntries(vm.DataDir));
    }

    // ===================================================================
    // MagiViewModel — 起動時の中断検知（RestoreOnStartup）
    // ===================================================================

    [Fact]
    public async Task RestoreOnStartupFlagsAnInterruptedRunWhenAStaleMarkerIsFound()
    {
        var vm = new MagiViewModel { DataDir = FreshTempDir() };
        File.WriteAllText(MarkerPath(vm), "{\"startedAt\":1,\"mode\":\"fg\",\"budgetSec\":300}");

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.True(vm.Ui.InterruptedRun);
        Assert.Contains("中断されました", vm.Ui.InterruptedInfo);
        // マーカーは検知の時点で消す（次の起動でもう一度「中断」と言わない）。
        Assert.False(File.Exists(MarkerPath(vm)));
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("中断を検知"));
        Assert.True(vm._hydrated);
    }

    [Fact]
    public async Task RestoreOnStartupSaysTheRunCanBeResumedWhenASnapshotSurvived()
    {
        var vm = new MagiViewModel
        {
            DataDir = FreshTempDir(),
            // state を入れておくと復元の読込（LoadAsync）は走らない＝中断案内だけを見る。
            _state = MinimalState.Build(),
        };
        var files = new RunFiles(vm.DataDir);
        File.WriteAllText(MarkerPath(vm), "{\"mode\":\"bg\"}");
        File.WriteAllText(files.Snapshot, "{}");

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.True(vm.Ui.InterruptedRun);
        Assert.Contains("再開できます", vm.Ui.InterruptedInfo);
    }

    [Fact]
    public async Task RestoreOnStartupNamesTheBackgroundModeFromTheMarker()
    {
        var vm = new MagiViewModel { DataDir = FreshTempDir(), _state = MinimalState.Build() };
        File.WriteAllText(MarkerPath(vm), "{\"mode\":\"bg\"}");

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.Contains("バックグラウンド", vm.Ui.InterruptedInfo);
    }

    [Fact]
    public async Task RestoreOnStartupFallsBackToTheDefaultTextWhenTheMarkerIsCorrupt()
    {
        var vm = new MagiViewModel { DataDir = FreshTempDir(), _state = MinimalState.Build() };
        File.WriteAllText(MarkerPath(vm), "これはJSONではない");

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.True(vm.Ui.InterruptedRun);
        Assert.Equal("前回の計算は完了前に中断されました。入力は自動保存済みです。", vm.Ui.InterruptedInfo);
    }

    [Fact]
    public async Task RestoreOnStartupDoesNotFlagAnythingWhenNoMarkerIsLeftBehind()
    {
        var dir = FreshTempDir();
        var vm = new MagiViewModel { DataDir = dir };

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.False(vm.Ui.InterruptedRun);
        Assert.Null(vm.Ui.InterruptedInfo);
        Assert.True(vm._hydrated);   // 復元が終わったので自動保存を解禁してよい
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public async Task RestoreOnStartupRestoresTheAutosaveWhenNoStateIsLoadedYet()
    {
        var vm = new MagiViewModel { DataDir = FreshTempDir() };
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());
        File.WriteAllText(Path.Combine(vm.DataDir, "magi_autosave.json"), json);

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;
        await vm.LastLoadTask!;

        Assert.True(vm.Ui.Loaded);
        Assert.Equal(2, vm.Ui.Staff);
    }

    [Fact]
    public async Task RestoreOnStartupAdoptsAUsableBackgroundResultAndCleansUpAfterIt()
    {
        var vm = new MagiViewModel { DataDir = FreshTempDir() };
        var files = new RunFiles(vm.DataDir);
        File.WriteAllText(MarkerPath(vm), "{\"mode\":\"bg\"}");
        File.WriteAllText(files.Input, "{}");
        File.WriteAllText(files.Snapshot, "{}");
        File.WriteAllText(files.Result,
            StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule()));

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;
        await vm.LastLoadTask!;

        Assert.True(vm.Ui.HasResult);
        // 完了しているので「中断」ではない。マーカーも共有ファイルも残さない。
        Assert.False(vm.Ui.InterruptedRun);
        Assert.False(File.Exists(MarkerPath(vm)));
        Assert.False(File.Exists(files.Input));
        Assert.False(File.Exists(files.Snapshot));
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("結果を反映しました"));
    }

    [Fact]
    public async Task RestoreOnStartupKeepsTheResumePathWhenTheBackgroundResultIsCorrupt()
    {
        // [3.406.0/B-02] **読めることを確かめてから**共有ファイルを消す。壊れた結果のせいで
        // 入力・途中最良・マーカーまで失うと、利用者は何も取り戻せない。
        var vm = new MagiViewModel { DataDir = FreshTempDir(), _state = MinimalState.Build() };
        var files = new RunFiles(vm.DataDir);
        File.WriteAllText(MarkerPath(vm), "{\"mode\":\"bg\"}");
        File.WriteAllText(files.Input, "{}");
        File.WriteAllText(files.Result, "壊れている");

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.False(File.Exists(files.Result));   // 読めない結果だけ捨てる
        Assert.True(File.Exists(files.Input));     // 再開手段は残す
        Assert.True(vm.Ui.InterruptedRun);         // 中断の経路へ落ちる
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("壊れていて読めませんでした"));
    }
}
