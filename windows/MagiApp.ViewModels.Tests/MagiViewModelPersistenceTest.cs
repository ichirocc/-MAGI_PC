using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース7] <c>MagiViewModel.kt</c> の永続化/入出力サブシステム（自動保存・元に戻す/
/// やり直すの公開入口・読込/復元・構造検証・違反チェックの再計算・実行中ガード・返事・JSON書き出し）
/// の検証。Kotlin原本には専用テストが無い（<c>UiStateTest</c>/<c>MagiViewModelTest</c>/
/// <c>MagiViewModelDiagnosticsTest</c> と同じ経緯、各クラスKDoc参照）。
///
/// <c>Undo</c>/<c>Redo</c>/<c>RunBlockedByInFlight</c>/<c>LoadAsync</c>（<c>fromRestore=false</c>）は
/// <see cref="MagiViewModel.OptimizeInFlight"/> 経由で <see cref="OptimizationRepository"/> の
/// プロセス共有 static 状態を読むため、<see cref="MagiViewModelTest"/> と同じ直列コレクションに属する。
///
/// ファイルI/Oを伴うテストは <see cref="FreshTempDir"/> でテストごとに隔離した一時ディレクトリを
/// <see cref="MagiViewModel.DataDir"/> へ注入する（既定の <c>LocalApplicationData</c> は触らない）。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelPersistenceTest : IDisposable
{
    private readonly List<string> _dirs = new();

    public MagiViewModelPersistenceTest()
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
        var dir = Path.Combine(Path.GetTempPath(), "magi-vm-persist-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    private MagiViewModel NewVm() => new() { DataDir = FreshTempDir() };

    // ===================================================================
    // Validate（構造検証・static）
    // ===================================================================

    [Fact]
    public void ValidateReturnsNullForAWellFormedState()
    {
        Assert.Null(MagiViewModel.Validate(MinimalState.Build()));
    }

    [Fact]
    public void ValidateRejectsEmptyStaff()
    {
        var st = MinimalState.Build(staffList: new List<Staff>());
        Assert.Equal("staff が空です", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsEmptySchedule()
    {
        var st = MinimalState.Build(schedule: new List<IReadOnlyList<int>>());
        Assert.Equal("schedule が空です", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsEmptyShifts()
    {
        var st = MinimalState.Build(shifts: new List<Shift>());
        Assert.Equal("shifts が空です", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsEmptyGroups()
    {
        var st = MinimalState.Build(groups: new List<Group>());
        Assert.Equal("groups が空です", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsScheduleRowCountMismatch()
    {
        // Default staffList has 2 people, but only 1 schedule row is supplied.
        var st = MinimalState.Build(
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 7).ToList() });
        Assert.Equal("schedule の行数が staff 数と一致しません", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsGroupShiftRowCountShortage()
    {
        var st = MinimalState.Build(groupShift: new List<IReadOnlyList<int>>());
        Assert.Equal("groupShift の行数が groups より少ないです", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsGroupShiftRowWithNoCanDoShift()
    {
        var st = MinimalState.Build(groupShift: new List<IReadOnlyList<int>> { new List<int> { 0, 0 } });
        Assert.Equal("groupShift[0] に担当可能シフトがありません", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsGroupShiftAptRowTooShort()
    {
        var st = MinimalState.Build(
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "" } });
        Assert.Equal("groupShiftApt[0] の列数が shifts より少ないです", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsStaffGroupIdxOutOfRange()
    {
        var st = MinimalState.Build(
            staffList: new List<Staff> { new("職員A", 5) },
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 7).ToList() });
        Assert.Equal("staff[0].groupIdx が範囲外です (5)", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsRaggedScheduleRows()
    {
        var st = MinimalState.Build(schedule: new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(0, 7).ToList(),
            Enumerable.Repeat(0, 5).ToList(),
        });
        Assert.Equal("schedule[1] の日数が不揃いです", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateRejectsOutOfRangeScheduleCell()
    {
        var row0 = Enumerable.Repeat(0, 7).ToList();
        row0[3] = 99; // shiftCount is 2 (default) — 99 is out of range
        var st = MinimalState.Build(schedule: new List<IReadOnlyList<int>>
        {
            row0,
            Enumerable.Repeat(0, 7).ToList(),
        });
        Assert.Equal("schedule[0][3] のシフト番号が範囲外です (99)", MagiViewModel.Validate(st));
    }

    [Fact]
    public void ValidateAllowsTheUnassignedSentinelMinusOne()
    {
        var row0 = Enumerable.Repeat(0, 7).ToList();
        row0[3] = -1; // sentinel for "not yet assigned" — must NOT be rejected
        var st = MinimalState.Build(schedule: new List<IReadOnlyList<int>>
        {
            row0,
            Enumerable.Repeat(0, 7).ToList(),
        });
        Assert.Null(MagiViewModel.Validate(st));
    }

    // ===================================================================
    // EnsureValidForRun
    // ===================================================================

    [Fact]
    public void EnsureValidForRunReturnsTrueAndLeavesUiUntouchedForAWellFormedState()
    {
        var vm = NewVm();
        var st = MinimalState.Build();

        Assert.True(vm.EnsureValidForRun(st, MinimalState.BuildSchedule()));
        Assert.Null(vm.Ui.Message);
        Assert.False(vm.Ui.MessageIsError);
    }

    [Fact]
    public void EnsureValidForRunReturnsFalseAndExplainsWhyForAMalformedSchedule()
    {
        var vm = NewVm();
        var st = MinimalState.Build();
        var badSchedule = new[] { new[] { 0, 0, 0 }, new[] { 0, 0, 0, 0, 0, 0, 0 } }; // row0 too short

        var ok = vm.EnsureValidForRun(st, badSchedule);

        Assert.False(ok);
        Assert.True(vm.Ui.MessageIsError);
        Assert.False(vm.Ui.Running);
        Assert.Contains("実行できません:", vm.Ui.Message);
        Assert.Contains("日数が不揃い", vm.Ui.Message);
    }

    // ===================================================================
    // RunBlockedByInFlight
    // ===================================================================

    [Fact]
    public void RunBlockedByInFlightReturnsFalseAndTouchesNothingWhenIdle()
    {
        var vm = NewVm();

        Assert.False(vm.RunBlockedByInFlight("勤務表をつくる"));
        Assert.Null(vm.Ui.Message);
        Assert.Empty(vm.Ui.OpLog);
    }

    [Fact]
    public void RunBlockedByInFlightReturnsTrueAndExplainsWhichJobIsBusy()
    {
        var vm = NewVm();
        vm.BeginBoardJob("勤務表をつくる");

        var blocked = vm.RunBlockedByInFlight("読み込み");

        Assert.True(blocked);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("勤務表をつくる", vm.Ui.Message);
        Assert.Contains("[W]", vm.Ui.OpLog[0]);
        Assert.Contains("読み込み", vm.Ui.OpLog[0]);
    }

    // ===================================================================
    // Notify / ClearMessage
    // ===================================================================

    [Fact]
    public void NotifyDefaultsToInfoLevelAndUpdatesTheMessage()
    {
        var vm = NewVm();

        vm.Notify("hello");

        Assert.Equal("hello", vm.Ui.Message);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("[I]", vm.Ui.OpLog[0]);
        Assert.Contains("hello", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void NotifyWithWarningLevelMarksTheMessageAsAnError()
    {
        var vm = NewVm();

        vm.Notify("bad thing happened", "W");

        Assert.Equal("bad thing happened", vm.Ui.Message);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("[W]", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void ClearMessageWithNoArgumentAlwaysClears()
    {
        var vm = NewVm();
        vm.Notify("bad", "W");

        vm.ClearMessage();

        Assert.Null(vm.Ui.Message);
        Assert.False(vm.Ui.MessageIsError);
    }

    [Fact]
    public void ClearMessageOnlyClearsWhenTheShownMessageStillMatchesCompareAndClear()
    {
        var vm = NewVm();
        vm.Notify("A");

        vm.ClearMessage("B"); // stale — a different message is being shown now (hypothetically)
        Assert.Equal("A", vm.Ui.Message); // untouched

        vm.ClearMessage("A"); // matches what's currently shown
        Assert.Null(vm.Ui.Message);
    }

    // ===================================================================
    // SaveNow（hydrated ガート・通知の遷移）
    // ===================================================================

    [Fact]
    public void StaleAutosaveGenerationNeverOverwritesANewerOne()
    {
        // [レビュー指摘 2026-09-04] 古い世代の書き込みが新しい世代の後に完了しても、ファイルは新しい方のまま。
        var vm = NewVm();
        var path = Path.Combine(vm.DataDir, "magi_autosave.json");

        Assert.True(vm.WriteAutosaveIfLatest(2, "{\"gen\":2}"));
        Assert.Null(vm.WriteAutosaveIfLatest(1, "{\"gen\":1}"));   // 古い世代は捨てる
        Assert.Equal("{\"gen\":2}", File.ReadAllText(path));

        Assert.True(vm.WriteAutosaveIfLatest(2, "{\"gen\":2b}"));  // 同じ世代の再書き込みは許す
        Assert.True(vm.WriteAutosaveIfLatest(3, "{\"gen\":3}"));
        Assert.Equal("{\"gen\":3}", File.ReadAllText(path));
    }

    [Fact]
    public void SaveNowIsANoOpBeforeHydration()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.Ui.StructureEdited = true;

        vm.SaveNow();

        Assert.False(File.Exists(Path.Combine(vm.DataDir, "magi_autosave.json")));
        Assert.Empty(vm.Ui.OpLog);
    }

    [Fact]
    public void SaveNowIsANoOpWhenThereIsNothingToExport()
    {
        var vm = NewVm();
        vm._hydrated = true; // hydrated, but no state/schedule loaded yet -> ExportJson() is null

        vm.SaveNow();

        Assert.False(File.Exists(Path.Combine(vm.DataDir, "magi_autosave.json")));
    }

    [Fact]
    public void SaveNowWritesTheCurrentDraftWhenHydratedAndLoaded()
    {
        var vm = NewVm();
        vm._hydrated = true;
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.Ui.StructureEdited = true; // -> ExportJson uses the full Serialize() branch

        vm.SaveNow();

        var path = Path.Combine(vm.DataDir, "magi_autosave.json");
        Assert.True(File.Exists(path));
        var roundTripped = StateJsonSerializer.Parse(File.ReadAllText(path));
        Assert.Equal(2, roundTripped.StaffCount);
    }

    /// <summary>
    /// [ReportAutoSave の遷移] 直前と結果が同じ(ok==true, 既定)なら黙ったまま。書込が失敗し始めた瞬間
    /// だけ警告を1回、復旧した瞬間だけ情報ログを1回——遷移のみを記録する設計を、rename の親ディレクトリを
    /// 通常ファイルで塞ぐ（<see cref="Directory.CreateDirectory"/> が失敗する）ことで実際の書込失敗を
    /// 起こして固定する。
    /// </summary>
    [Fact]
    public void SaveNowLogsOnlyOnTheFailureAndRecoveryTransitionsNotOnSteadyState()
    {
        var vm = NewVm();
        vm._hydrated = true;
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.Ui.StructureEdited = true;

        // A regular FILE occupying the path where AutosaveFile's parent directory would need to be
        // created -> Directory.CreateDirectory throws inside WriteFileAtomically -> SaveNow's catch
        // sets ok=false.
        var blockerFile = Path.Combine(vm.DataDir, "blocker.txt");
        File.WriteAllText(blockerFile, "not a directory");
        vm.DataDir = blockerFile;

        vm.SaveNow(); // first failure -> transition true->false -> logs a warning
        Assert.Single(vm.Ui.OpLog);
        Assert.Contains("[W]", vm.Ui.OpLog[0]);
        Assert.Contains("自動保存に失敗", vm.Ui.Message);
        Assert.True(vm.Ui.MessageIsError);

        vm.SaveNow(); // still failing -> ok stays false -> no additional log line (steady state)
        Assert.Single(vm.Ui.OpLog);

        // Now point DataDir at a real, writable directory -> the next SaveNow succeeds -> recovery log.
        vm.DataDir = FreshTempDir();
        vm.SaveNow();

        Assert.Equal(2, vm.Ui.OpLog.Count);
        Assert.Contains("[I]", vm.Ui.OpLog[0]);
        Assert.Contains("自動保存が復旧", vm.Ui.OpLog[0]);

        vm.SaveNow(); // steady state again (ok stays true) -> no additional log line
        Assert.Equal(2, vm.Ui.OpLog.Count);
    }

    // ===================================================================
    // Undo / Redo（公開の入口: 実行中ガード + AutoSave/RefreshCheck の起動）
    // ===================================================================

    [Fact]
    public void UndoIsANoOpWhenTheStackIsEmpty()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();

        vm.Undo();

        Assert.Null(vm.Ui.Message);
        Assert.Null(vm.LastRefreshCheckTask);
    }

    [Fact]
    public void UndoIsANoOpWhileAnotherJobIsInFlight()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.PushUndo();
        vm.BeginBoardJob("勤務表をつくる");

        vm.Undo();

        Assert.Equal(1, vm.UndoStackCount); // untouched
        Assert.Null(vm.Ui.Message);
    }

    [Fact]
    public async Task UndoRestoresThePreviousSnapshotAndPushesTheCurrentStateToRedo()
    {
        var vm = NewVm();
        var stateA = MinimalState.Build(startDate: "2025-01-01");
        vm._state = stateA;
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.PushUndo(); // snapshot A pushed to the undo stack

        var stateB = MinimalState.Build(startDate: "2025-02-01");
        vm._state = stateB; // simulate an edit that moved on to state B

        vm.Undo();

        // Undo() sets Ui.Message = "1つ前に戻しました" and logs "元に戻す" — but it then calls
        // RefreshCheck() unconditionally in the SAME synchronous call stack, and RefreshCheck()
        // itself immediately overwrites Ui.Message to "違反チェック中…" before yielding to its
        // background continuation. So the "1つ前に戻しました" message is never independently
        // observable from outside — only its LogOp trace (Ui.OpLog, which accumulates rather than
        // being overwritten) survives. The state/stack mechanics ARE stable and safe to check here.
        Assert.Same(stateA, vm._state); // back to A
        Assert.Equal(0, vm.UndoStackCount);
        Assert.Equal(1, vm.RedoStackCount); // B was pushed to redo
        Assert.False(vm.Ui.CanUndo);
        Assert.True(vm.Ui.CanRedo);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("元に戻す"));

        // Undo() also triggers RefreshCheck() — confirm it actually ran and completed cleanly,
        // and that its completion is what Ui.Message ends up showing.
        Assert.NotNull(vm.LastRefreshCheckTask);
        await vm.LastRefreshCheckTask!;
        Assert.Contains("違反チェック完了", vm.Ui.Message);
    }

    [Fact]
    public void RedoIsANoOpWhileAnotherJobIsInFlight()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.PushUndo();
        vm.Undo(); // populates the redo stack
        vm.BeginBoardJob("勤務表をつくる");

        vm.Redo();

        Assert.Equal(1, vm.RedoStackCount); // untouched
    }

    [Fact]
    public async Task RedoRestoresTheNextSnapshotAndPushesTheCurrentStateBackToUndo()
    {
        var vm = NewVm();
        var stateA = MinimalState.Build(startDate: "2025-01-01");
        vm._state = stateA;
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.PushUndo();

        var stateB = MinimalState.Build(startDate: "2025-02-01");
        vm._state = stateB;
        vm.Undo(); // -> back to A, B now on redo stack
        await vm.LastRefreshCheckTask!;

        vm.Redo();

        // Same ordering concern as UndoRestoresThePreviousSnapshotAndPushesTheCurrentStateToRedo:
        // Redo()'s own "やり直しました" message is immediately overwritten (in the same synchronous
        // call stack) by the RefreshCheck() it triggers — only Ui.OpLog's accumulated trace survives.
        Assert.Same(stateB, vm._state); // forward to B again
        Assert.Equal(1, vm.UndoStackCount);
        Assert.Equal(0, vm.RedoStackCount);
        Assert.True(vm.Ui.CanUndo);
        Assert.False(vm.Ui.CanRedo);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("やり直し"));

        await vm.LastRefreshCheckTask!;
        Assert.Contains("違反チェック完了", vm.Ui.Message);
    }

    // ===================================================================
    // Load / InitBlankState / LoadAsync
    // ===================================================================

    [Fact]
    public async Task InitBlankStateProducesAValidMinimalLoadedState()
    {
        var vm = NewVm();

        vm.InitBlankState();
        Assert.NotNull(vm.LastLoadTask);
        await vm.LastLoadTask!;

        Assert.True(vm.Ui.Loaded);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Equal(1, vm.Ui.Staff);
        Assert.Equal(31, vm.Ui.Days);
        Assert.Equal(1, vm.Ui.Shifts);
        Assert.Equal(1, vm.Ui.Groups);
        Assert.False(vm.Ui.HasResult);
        Assert.Contains("読込完了:", vm.Ui.Message);
    }

    [Fact]
    public async Task LoadAsyncAppendsTheGivenNoteToTheCompletionMessage()
    {
        var vm = NewVm();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());

        vm.LoadAsync(json, note: "（推定: 2026年1月）");
        await vm.LastLoadTask!;

        Assert.EndsWith("（推定: 2026年1月）", vm.Ui.Message);
    }

    [Fact]
    public async Task LoadAsyncMarksTheScheduleAsAResultWhenMarkResultIsTrue()
    {
        var vm = NewVm();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());

        vm.LoadAsync(json, markResult: true);
        await vm.LastLoadTask!;

        Assert.True(vm.Ui.HasResult);
    }

    [Fact]
    public async Task LoadAsyncStripsALeadingBomWithoutLoggingAMojibakeWarning()
    {
        var vm = NewVm();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());

        vm.LoadAsync((char)0xFEFF + json); // leading BOM, represented as a numeric cast (not a literal glyph)
        await vm.LastLoadTask!;

        Assert.True(vm.Ui.Loaded);
        Assert.DoesNotContain(vm.Ui.OpLog, l => l.Contains("文字化け"));
    }

    [Fact]
    public async Task LoadAsyncRejectsStructurallyInvalidDataAndLeavesThePriorStateInPlace()
    {
        var vm = NewVm();
        var goodJson = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());
        vm.LoadAsync(goodJson);
        await vm.LastLoadTask!;
        Assert.True(vm.Ui.Loaded);
        var stateBeforeBadLoad = vm._state;

        var badState = MinimalState.Build(staffList: new List<Staff>());
        var badJson = StateJsonSerializer.Serialize(badState, badState.Schedule.ToIntArray2D());

        vm.LoadAsync(badJson);
        await vm.LastLoadTask!;

        Assert.Same(stateBeforeBadLoad, vm._state); // untouched by the rejected load
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("読み込めませんでした（", vm.Ui.Message);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("読込失敗:"));
    }

    [Fact]
    public async Task LoadAsyncIsBlockedWhileAnotherJobIsInFlightUnlessFromRestore()
    {
        var vm = NewVm();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());
        vm.BeginBoardJob("勤務表をつくる");

        vm.LoadAsync(json); // fromRestore defaults to false -> blocked
        Assert.Null(vm.LastLoadTask);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("読み込み") && l.Contains("取り消しました"));

        vm.LoadAsync(json, fromRestore: true); // bypasses the in-flight gate
        Assert.NotNull(vm.LastLoadTask);
        await vm.LastLoadTask!;
        Assert.True(vm.Ui.Loaded);
    }

    // ===================================================================
    // RestorePreviousData
    // ===================================================================

    [Fact]
    public async Task RestorePreviousDataReportsWhenNoBackupExists()
    {
        var vm = NewVm();

        vm.RestorePreviousData();
        Assert.NotNull(vm.LastRestorePreviousDataTask);
        await vm.LastRestorePreviousDataTask!;

        Assert.Equal("開く前のデータの退避がありません", vm.Ui.Message);
        Assert.False(vm.Ui.MessageIsError);
    }

    /// <summary>
    /// [判断設計監査 #3の由来] 「データを開く」直前の状態は1世代だけ退避され、RestorePreviousData で
    /// 往復できる。往復自体も退避を挟む（＝スワップ）ため、もう一度押すと元へ戻る。
    /// </summary>
    [Fact]
    public async Task RestorePreviousDataRoundTripsTheRetiredStateAsASwap()
    {
        var vm = NewVm();

        var stateA = MinimalState.Build(
            staffList: new List<Staff> { new("職員A", 0) },
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 7).ToList() });
        var jsonA = StateJsonSerializer.Serialize(stateA, stateA.Schedule.ToIntArray2D());

        var stateB = MinimalState.Build(); // default shape: 2 staff
        var jsonB = StateJsonSerializer.Serialize(stateB, stateB.Schedule.ToIntArray2D());

        vm.LoadAsync(jsonA);
        await vm.LastLoadTask!;
        Assert.Equal(1, vm.Ui.Staff);
        Assert.False(vm.Ui.PrevBackupAvailable); // nothing retired yet on the very first load

        vm.LoadAsync(jsonB); // _state (A) is non-null now -> A gets retired before switching to B
        await vm.LastLoadTask!;
        Assert.Equal(2, vm.Ui.Staff);
        Assert.True(vm.Ui.PrevBackupAvailable);

        vm.RestorePreviousData();
        await vm.LastRestorePreviousDataTask!;
        await vm.LastLoadTask!; // RestorePreviousData fires a nested LoadAsync — await that too

        Assert.Equal(1, vm.Ui.Staff); // back to A
        Assert.True(vm.Ui.PrevBackupAvailable); // B was retired in turn -> still available (swap)
    }

    [Fact]
    public void RestorePreviousDataIsBlockedWhileAnotherJobIsInFlight()
    {
        var vm = NewVm();
        vm.BeginBoardJob("勤務表をつくる");

        vm.RestorePreviousData();

        Assert.Null(vm.LastRestorePreviousDataTask);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("実行中は操作できません", vm.Ui.Message);
    }

    // ===================================================================
    // RefreshCheck
    // ===================================================================

    [Fact]
    public void RefreshCheckIsANoOpWithNoLoadedState()
    {
        var vm = NewVm();

        vm.RefreshCheck();

        Assert.Null(vm.LastRefreshCheckTask);
    }

    [Fact]
    public async Task RefreshCheckComputesAndPublishesTheReport()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();

        vm.RefreshCheck();
        Assert.NotNull(vm.LastRefreshCheckTask);
        await vm.LastRefreshCheckTask!;

        Assert.Contains("違反チェック完了: 必須=", vm.Ui.Message);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Equal(2, vm.Ui.Schedule.Count);
    }

    /// <summary>[3.328.0の由来] 最適化ジョブが動いている間の違反チェックは、完了しても実行中表示を
    /// 戻さない——最適化中の設定編集で全ガードが素通しになる事故の再発防止。</summary>
    [Fact]
    public async Task RefreshCheckLeavesRunningTrueWhenAnotherJobIsStillInFlight()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.BeginBoardJob("勤務表をつくる"); // left open — deliberately not ended

        vm.RefreshCheck();
        await vm.LastRefreshCheckTask!;

        Assert.True(vm.Ui.Running);
        Assert.Contains("違反チェック完了:", vm.Ui.Message);
    }

    /// <summary>[review #6の由来] 後から始まったチェックが古いチェックの完了を追い越しても、古い方の
    /// 結果でUIを上書きしない（seq番号による使い捨て判定）。</summary>
    [Fact]
    public async Task RefreshCheckDiscardsAStaleRunWhenASecondCallSupersedesTheFirst()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();

        vm.RefreshCheck();
        var firstTask = vm.LastRefreshCheckTask;
        Assert.NotNull(firstTask);

        vm.RefreshCheck(); // supersedes — cancels the first CTS and bumps the seq counter
        var secondTask = vm.LastRefreshCheckTask;
        Assert.NotNull(secondTask);
        Assert.NotSame(firstTask, secondTask);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstTask!);
        await secondTask!; // the current (second) call completes normally
        Assert.Contains("違反チェック完了", vm.Ui.Message);
    }

    // ===================================================================
    // ExportJson
    // ===================================================================

    [Fact]
    public void ExportJsonReturnsNullWithNothingLoaded()
    {
        var vm = NewVm();
        Assert.Null(vm.ExportJson());
    }

    [Fact]
    public void ExportJsonUsesTheFullSerializeBranchWhenStructureWasEdited()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.Ui.StructureEdited = true;

        var json = vm.ExportJson();

        Assert.NotNull(json);
        var st = StateJsonSerializer.Parse(json!);
        Assert.Equal(2, st.StaffCount);
    }

    [Fact]
    public async Task ExportJsonUsesTheScheduleOnlyOverwriteBranchRightAfterALoad()
    {
        var vm = NewVm();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());
        vm.LoadAsync(json);
        await vm.LastLoadTask!;
        // A freshly-completed load resets both edited flags -> ExportWithSchedule branch.
        Assert.False(vm.Ui.StructureEdited);
        Assert.False(vm.Ui.ConstraintsEdited);

        var exported = vm.ExportJson();

        Assert.NotNull(exported);
        var st = StateJsonSerializer.Parse(exported!);
        Assert.Equal(2, st.StaffCount);
    }

    [Fact]
    public async Task ExportJsonUsesTheConstraintsOnlyEditBranchWhenOnlyConstraintsWereEdited()
    {
        var vm = NewVm();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());
        vm.LoadAsync(json);
        await vm.LastLoadTask!;
        vm.Ui.ConstraintsEdited = true; // StructureEdited stays false

        var exported = vm.ExportJson();

        Assert.NotNull(exported);
        var st = StateJsonSerializer.Parse(exported!);
        Assert.Equal(2, st.StaffCount);
    }

    [Fact]
    public void ExportJsonReturnsNullWhenConstraintsEditedButThereIsNoOriginalJsonToPatch()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build(); // set directly, bypassing Load -> _originalJson stays null
        vm._currentSchedule = MinimalState.BuildSchedule();
        vm.Ui.ConstraintsEdited = true;
        vm.Ui.StructureEdited = false;

        Assert.Null(vm.ExportJson());
    }

    // ===================================================================
    // RestoreOnStartup（<c>MagiViewModel.Restore.cs</c>）
    //
    // [2026-09-01] クラッシュ復旧機構（実行中マーカー・RunFiles ベースの背景スナップショット・
    // 起動時の「中断されました」検知）はユーザー明示判断で全撤去した（詳細は
    // MagiViewModel.Restore.cs のクラスKDoc参照）。ここでは撤去後も残る2つの通常運用UXだけを
    // 検証する: ①自動保存からの起動時復元 ②開く前データの退避有無フラグ(PrevBackupAvailable)。
    // ===================================================================

    [Fact]
    public async Task RestoreOnStartupRestoresTheAutosaveWhenNoStateIsLoadedYet()
    {
        var vm = NewVm();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(), MinimalState.BuildSchedule());
        File.WriteAllText(Path.Combine(vm.DataDir, "magi_autosave.json"), json);

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;
        await vm.LastLoadTask!;

        Assert.True(vm.Ui.Loaded);
        Assert.Equal(2, vm.Ui.Staff);
    }

    [Fact]
    public async Task RestoreOnStartupSetsPrevBackupAvailableWhenARetiredBackupExists()
    {
        var vm = NewVm();
        File.WriteAllText(Path.Combine(vm.DataDir, "magi_prev_before_open.json"), "{}");

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.True(vm.Ui.PrevBackupAvailable);
    }

    [Fact]
    public async Task RestoreOnStartupDoesNothingAndHydratesWhenNothingIsPresent()
    {
        var vm = NewVm();

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.False(vm.Ui.Loaded);
        Assert.False(vm.Ui.PrevBackupAvailable);
        Assert.True(vm._hydrated); // 復元が終わったので自動保存を解禁してよい
    }

    [Fact]
    public async Task RestoreOnStartupDoesNotOverwriteStateAlreadyPresent()
    {
        var vm = NewVm();
        vm._state = MinimalState.Build();
        var json = StateJsonSerializer.Serialize(MinimalState.Build(startDate: "2025-12-08", endDate: "2025-12-14"), MinimalState.BuildSchedule());
        File.WriteAllText(Path.Combine(vm.DataDir, "magi_autosave.json"), json);

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.Null(vm.LastLoadTask); // 既に state があるので自動保存からの復元は起きない
        Assert.True(vm._hydrated);
    }

    [Fact]
    public async Task RestoreOnStartupRemovesStrayTempFilesLeftByAnInterruptedAtomicWrite()
    {
        var vm = NewVm();
        var strayA = Path.Combine(vm.DataDir, "magi_autosave.json.t1.tmp");
        var strayB = Path.Combine(vm.DataDir, "magi_prev_before_open.json.t2.tmp");
        File.WriteAllText(strayA, "半端に書きかけの中身");
        File.WriteAllText(strayB, "同上");

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.False(File.Exists(strayA));
        Assert.False(File.Exists(strayB));
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("迷子の一時ファイルを2件片付けました"));
    }

    [Fact]
    public async Task RestoreOnStartupIsSilentWhenNoStrayTempFileExists()
    {
        var vm = NewVm();

        _ = vm.RestoreOnStartup();
        await vm.LastRestoreOnStartupTask!;

        Assert.DoesNotContain(vm.Ui.OpLog, l => l.Contains("迷子の一時ファイル"));
    }
}
