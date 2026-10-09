using MagiEngine.Model;
using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9] <see cref="MagiViewModel.FindFixSuggestions"/>/<see cref="MagiViewModel.ApplyFixSuggestion"/>
/// （<c>MagiViewModel.FixSuggestions.cs</c>、Kotlin原本 <c>findFixSuggestions</c>/<c>applyFixSuggestion</c>
/// の移植）の検証。<see cref="FixSuggester"/> 自体は既にエンジン側で完全移植・テスト済みのため、ここでは
/// 配線（世代管理・実行中ガード・盤面適用・候補クリア）だけを検証する。
///
/// このピースの実行中ガード（<c>OptimizeInFlight</c>）は <see cref="OptimizationRepository.Running"/> も
/// 読むため、<see cref="MagiViewModelOptimizeTest"/>/<see cref="MagiViewModelEditingTest"/> と同じ直列
/// コレクションに属する。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelFixSuggestionsTest
{
    public MagiViewModelFixSuggestionsTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    // ===== FindFixSuggestions =====

    [Fact]
    public async Task FindFixSuggestions_ViolationFreeState_CompletesWithNoSearchingLeftOn()
    {
        // MinimalState.Build() は制約皆無＝どの盤面も違反0。見つかる改善手も0件のはず。
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.FindFixSuggestions();
        Assert.NotNull(vm.LastFindFixSuggestionsTask);
        await vm.LastFindFixSuggestionsTask!;

        Assert.False(vm.Ui.FixSearching);
        Assert.Empty(vm.Ui.FixSuggestions);
    }

    [Fact]
    public void FindFixSuggestions_NoStateLoaded_IsNoOp()
    {
        var vm = new MagiViewModel();

        vm.FindFixSuggestions();

        Assert.Null(vm.LastFindFixSuggestionsTask);
        Assert.False(vm.Ui.FixSearching);
    }

    [Fact]
    public async Task FindFixSuggestions_WithFocusStaff_SetsFixFocusName()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.FindFixSuggestions(focusStaff: 0);
        await vm.LastFindFixSuggestionsTask!;

        Assert.Equal(st.StaffList[0].Name, vm.Ui.FixFocusName);
        Assert.False(vm.Ui.FixSearching);
    }

    // ===== ApplyFixSuggestion =====

    private static FixSuggestion MakeSuggestion(params FixCell[] ops) =>
        new(FixKind.Change, ops, "テスト改善手", DeltaHard: 0, DeltaTotal: 0, Diff: Array.Empty<(string, int)>());

    [Fact]
    public async Task ApplyFixSuggestion_AppliesOpsAndClearsSuggestions()
    {
        // [Android 3.509.4] 適用直前ゲートは改善する提案だけ通す＝A に必要人数 1 を置き、全員 休 の盤面で (0,0)→A が人員不足を減らす形にする。
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "") }),
            _currentSchedule = MinimalState.BuildSchedule(),
        };
        vm.Ui.FixSuggestions = new List<FixSuggestion> { MakeSuggestion(new FixCell(0, 0, 1)) };
        var s = MakeSuggestion(new FixCell(0, 0, 1));

        vm.ApplyFixSuggestion(s);

        // Kotlin原本と同じく末尾で RefreshCheck()（fire-and-forget）を呼ぶため、その完了を待ってから
        // 完了メッセージを検証する（さもなくば「違反チェック中…」に上書きされた直後を捉えてしまう）。
        Assert.NotNull(vm.LastRefreshCheckTask);
        await vm.LastRefreshCheckTask!;

        Assert.Equal(1, vm._currentSchedule![0][0]);
        Assert.Empty(vm.Ui.FixSuggestions);
        Assert.True(vm.Ui.HasResult);
        Assert.False(vm.Ui.MessageIsError);
        Assert.Contains("違反チェック完了", vm.Ui.Message);
    }

    // ===== 複数人の入替の一覧（3.644.0/UX-03） =====

    [Fact]
    public async Task PrepareShortageChainFix_ShowsThePreviewWithoutTouchingTheBoard_ThenApplyAppliesIt()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "") }),
            _currentSchedule = MinimalState.BuildSchedule(),
        };
        vm.Ui.StaffNames = vm._state!.StaffList.Select(s => s.Name).ToList();
        vm.Ui.ShiftSymbols = new[] { "休", "A" };
        vm.Ui.StartDate = vm._state.StartDate;

        vm.PrepareShortageChainFix(dayIndex: 0, shiftIndex: 1, "（玉突き）12/1 の「A」を複数人の入替で埋める");
        Assert.NotNull(vm.LastPrepareChainFixTask);
        await vm.LastPrepareChainFixTask!;

        var p = vm.Ui.ChainPreview;
        Assert.NotNull(p);
        Assert.All(vm._currentSchedule!, row => Assert.All(row, v => Assert.Equal(0, v)));   // 一覧を出しただけ＝盤面は不変
        Assert.NotEmpty(p!.Changes);
        Assert.All(p.Changes, c => Assert.EndsWith("→ A", c));

        vm.ApplyChainPreview();
        Assert.Null(vm.Ui.ChainPreview);
        Assert.NotNull(vm.LastRefreshCheckTask);
        await vm.LastRefreshCheckTask!;
        Assert.Contains(vm._currentSchedule!, row => row[0] == 1);
    }

    [Fact]
    public void DismissChainPreview_ClosesWithoutApplying()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ChainPreview = ChainFixPreview.Of(MakeSuggestion(new FixCell(0, 0, 1)), MinimalState.BuildSchedule(), Array.Empty<string>(), Array.Empty<string>(), "2025-12-01");

        vm.DismissChainPreview();

        Assert.Null(vm.Ui.ChainPreview);
        Assert.Equal(0, vm._currentSchedule![0][0]);
    }

    [Fact]
    public void ApplyFixSuggestion_BlockedWhileAJobIsInFlight_DoesNotApplyAndShowsError()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(),
            _currentSchedule = MinimalState.BuildSchedule(),
        };
        vm.BeginBoardJob("勤務表をつくる");
        var s = MakeSuggestion(new FixCell(0, 0, 1));

        vm.ApplyFixSuggestion(s);

        Assert.Equal(0, vm._currentSchedule![0][0]); // unchanged
        Assert.True(vm.Ui.MessageIsError);
    }

    [Fact]
    public void ApplyFixSuggestion_OutOfBoundsOp_ReturnsWithoutApplyingAnyOp()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(),
            _currentSchedule = MinimalState.BuildSchedule(),
        };
        // 2職員×7日の盤面に対し、1つ目は妥当・2つ目が範囲外(day=99) → 全体が no-op のはず。
        var s = MakeSuggestion(new FixCell(0, 0, 1), new FixCell(1, 99, 1));

        vm.ApplyFixSuggestion(s);

        Assert.Equal(0, vm._currentSchedule![0][0]); // 1つ目も適用されていない
        Assert.True(vm.Ui.MessageIsError);            // [Android 3.509.4] 見送りを告げる
        Assert.Contains("見送りました", vm.Ui.Message);
    }

    [Fact]
    public void ApplyFixSuggestion_ToShiftEqualToShiftCount_ReturnsWithoutUndoAutoSaveOrRecheck()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.ApplyFixSuggestion(MakeSuggestion(new FixCell(0, 0, 2))); // == Shifts.Count
        Assert.Equal(0, vm._currentSchedule![0][0]);
        Assert.Equal(0, vm.UndoStackCount);
        Assert.Null(vm.LastRefreshCheckTask);
        Assert.Null(vm.LastAutoSaveTask);
    }

    [Fact]
    public void ApplyFixSuggestion_ToShiftBeyondShiftCount_ReturnsWithoutApplyingAnyOp()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        // 既定フィクスチャはシフト 2 種（休/A）。toShift=99 は探索中にシフトが削除された等の古い提案。
        vm.ApplyFixSuggestion(MakeSuggestion(new FixCell(0, 0, 1), new FixCell(1, 0, 99)));
        Assert.Equal(0, vm._currentSchedule![0][0]);
    }

    [Fact]
    public async Task ApplyFixSuggestion_RejectsWhenTheBoardChangedSinceTheSearch()
    {
        // 探索した盤面と違う盤面へ古い提案を書き込まない（指紋照合＝Kotlin 3.475.0）。
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.FindFixSuggestions();
        await vm.LastFindFixSuggestionsTask!;
        vm._currentSchedule![1][3] = 1; // 探索後の手編集

        vm.ApplyFixSuggestion(MakeSuggestion(new FixCell(0, 0, 1)));

        Assert.Equal(0, vm._currentSchedule![0][0]);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("もう一度", vm.Ui.Message);
    }

    [Fact]
    public void PushUndoDropsPendingSuggestionsAndAlternatives()
    {
        // [Android 3.475.0/3.529.0] 盤面を変える操作（PushUndo を通る）のあとに、別の盤面で計算した提案・他の案を残さない。
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.FixSuggestions = new[] { MakeSuggestion(new FixCell(0, 0, 1)) };
        vm.Ui.Alternatives = new[] { "案1" };
        vm.PushUndo();
        Assert.Empty(vm.Ui.FixSuggestions);
        Assert.Empty(vm.Ui.Alternatives);
    }

    [Fact]
    public void UndoAndRedoDropEngineRanAndPendingSuggestions()
    {
        // 元に戻す/やり直しは手操作＝「計算済み」ではない。古い提案も画面に残さない。
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.PushUndo();
        vm._currentSchedule![0][0] = 1;
        vm.Ui.EngineRan = true;
        vm.Ui.FixSuggestions = new[] { MakeSuggestion(new FixCell(0, 0, 1)) };

        vm.Undo();
        Assert.False(vm.Ui.EngineRan);
        Assert.Empty(vm.Ui.FixSuggestions);
        Assert.Equal(0, vm._currentSchedule![0][0]);

        vm.Ui.EngineRan = true;
        vm.Ui.FixSuggestions = new[] { MakeSuggestion(new FixCell(0, 0, 1)) };
        vm.Redo();
        Assert.False(vm.Ui.EngineRan);
        Assert.Empty(vm.Ui.FixSuggestions);
        Assert.Equal(1, vm._currentSchedule![0][0]);
    }

    [Fact]
    public void ApplyFixSuggestion_NoStateLoaded_IsNoOp()
    {
        var vm = new MagiViewModel();
        var s = MakeSuggestion(new FixCell(0, 0, 1));

        vm.ApplyFixSuggestion(s); // must not throw

        Assert.Null(vm.Ui.Message);
    }

    [Fact]
    public void ApplyFixSuggestion_EmptyOps_IsNoOp()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(),
            _currentSchedule = MinimalState.BuildSchedule(),
        };
        var s = MakeSuggestion(); // Ops は空

        vm.ApplyFixSuggestion(s);

        Assert.Null(vm.Ui.Message);
    }

    /// <summary>[Android vm-1] 走行中の直し方の探索は、盤面を差し替えるジョブの開始で捨てる
    /// （旧: 実行の途中で完了して古い盤面の提案と「探索済み」を書き戻し、実行後も残っていた）。</summary>
    [Fact]
    public void BoardJobDiscardsAFixSearchThatIsStillRunning()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        var ui = new QueuedSyncContext();
        Task? search = null;
        ui.Run(() =>
        {
            vm.FindFixSuggestions();
            search = vm.LastFindFixSuggestionsTask;
            ui.WaitForPosted();   // 探索は計算を終え、UI の列で書き戻しを待っている
            vm.BeginBoardJob("勤務表づくり");
            Assert.False(vm.Ui.FixSearching);
            ui.RunUntil(search!);
        });
        Assert.False(vm.Ui.FixSearched);
        Assert.Empty(vm.Ui.FixSuggestions);
    }

    /// <summary>盤面を差し替えるジョブの最中に終わった探索は、書き戻しも探し直しもしない（完了後の盤面で探し直す）。</summary>
    [Fact]
    public void FixSearchFinishingDuringABoardJobWritesNothing()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        var ui = new QueuedSyncContext();
        try
        {
            ui.Run(() =>
            {
                vm.FindFixSuggestions();
                var search = vm.LastFindFixSuggestionsTask!;
                ui.WaitForPosted();
                OptimizationRepository.SetRunning(true);   // 取消を経ずに実行中になった経路（防御）
                ui.RunUntil(search);
                Assert.Same(search, vm.LastFindFixSuggestionsTask);   // 探し直していない
            });
            Assert.False(vm.Ui.FixSearching);
            Assert.False(vm.Ui.FixSearched);
        }
        finally
        {
            OptimizationRepository.SetRunning(false);
        }
    }

    // ===== 3.646.0: 2 セル以上の手は当てる前に一覧／相談に積んだ入れ替えの見直し／元に戻すで案内の結果の行を結ばない =====

    private static MagiViewModel VmWithNeed()
    {
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "") }),
            _currentSchedule = MinimalState.BuildSchedule(),
        };
        vm.Ui.StaffNames = vm._state!.StaffList.Select(s => s.Name).ToList();
        vm.Ui.ShiftSymbols = new[] { "休", "A" };
        vm.Ui.StartDate = vm._state.StartDate;
        vm.Ui.Days = vm._state.DayCount;
        return vm;
    }

    [Fact]
    public void PreviewOrApplyFix_TwoOrMoreOpsShowThePreviewCarryingTheSuggestion()
    {
        var vm = VmWithNeed();
        var s = MakeSuggestion(new FixCell(0, 0, 1), new FixCell(1, 1, 1));

        vm.PreviewOrApplyFix(s);

        var p = vm.Ui.ChainPreview;
        Assert.NotNull(p);
        Assert.Same(s, p!.Target!.Suggestion);
        Assert.Null(p.Target.Date);
        Assert.Equal(s.Label, p.Target.Label);
        Assert.All(vm._currentSchedule!, row => Assert.All(row, v => Assert.Equal(0, v)));   // 一覧を出しただけ＝盤面は不変
    }

    [Fact]
    public async Task PreviewOrApplyFix_SingleOpAppliesDirectly()
    {
        var vm = VmWithNeed();

        vm.PreviewOrApplyFix(MakeSuggestion(new FixCell(0, 0, 1)));
        await vm.LastRefreshCheckTask!;

        Assert.Null(vm.Ui.ChainPreview);
        Assert.Equal(1, vm._currentSchedule![0][0]);
        Assert.Contains(vm.Ui.OpLog, l => l.Contains("改善手を適用: テスト改善手（必須 7→6"));   // 操作ログにも残す（3.646.0）
    }

    [Fact]
    public void ResumeConsultChain_ResolvableTargetSearchesTheChainAgain()
    {
        var vm = VmWithNeed();
        var c = new ConsultItem("lbl", "n", Chain: new ChainTarget("2025-12-02", "A", "lbl"));

        vm.ResumeConsultChain(c);

        Assert.NotNull(vm.LastPrepareChainFixTask);
        Assert.False(vm.Ui.MessageIsError);
    }

    [Fact]
    public void ResumeConsultChain_SuggestionWithoutTargetReopensThePreview()
    {
        var vm = VmWithNeed();
        var s = MakeSuggestion(new FixCell(0, 0, 1), new FixCell(1, 1, 1));
        var bk = MagiViewModel.BoardKey(vm._currentSchedule!);
        var sk = MagiViewModel.StateKey(vm._state!);

        vm.ResumeConsultChain(new ConsultItem("lbl", "n", Chain: new ChainTarget(null, null, "lbl", Suggestion: s, BoardKey: bk, StateKey: sk)));

        Assert.Same(s, vm.Ui.ChainPreview!.Suggestion);
        Assert.Equal(bk, vm.Ui.ChainPreview.Target!.BoardKey);   // [3.650.0] 当てるときも積んだ案の指紋で照合する
    }

    /// <summary>[3.650.0/外部レビュー] 積んだあとで勤務表が変わった案（または指紋の無い案）は一覧を出さず、探し直しを促す。</summary>
    [Fact]
    public void ResumeConsultChain_StaleSuggestionIsNotReopened()
    {
        var vm = VmWithNeed();
        var s = MakeSuggestion(new FixCell(0, 0, 1), new FixCell(1, 1, 1));
        var sk = MagiViewModel.StateKey(vm._state!);

        vm.ResumeConsultChain(new ConsultItem("lbl", "n", Chain: new ChainTarget(null, null, "lbl", Suggestion: s, BoardKey: 12345L, StateKey: sk)));
        Assert.Null(vm.Ui.ChainPreview);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Equal(ConsultList.Stale, vm.Ui.Message);

        vm.ResumeConsultChain(new ConsultItem("lbl", "n", Chain: new ChainTarget(null, null, "lbl", Suggestion: s)));
        Assert.Null(vm.Ui.ChainPreview);
    }

    [Fact]
    public void ResumeConsultChain_UnresolvableTargetSaysWhy()
    {
        var vm = VmWithNeed();

        vm.ResumeConsultChain(new ConsultItem("lbl", "n", Chain: new ChainTarget("2026-01-05", "A", "lbl")));

        Assert.True(vm.Ui.MessageIsError);
        Assert.Equal("いまの期間・シフトにない枠です（1/5 の「A」）", vm.Ui.Message);
        Assert.Null(vm.Ui.ChainPreview);
        Assert.Null(vm.LastPrepareChainFixTask);
    }

    [Fact]
    public async Task UndoDropsThePendingGuidedOutcome()
    {
        var vm = VmWithNeed();
        vm.NoteGuidedFix(0, 1, "12/1", "A");
        vm.RefreshCheck();
        await vm.LastRefreshCheckTask!;
        Assert.NotNull(vm.Ui.FixOutcome);   // 対照: 再検査が枠の結果を 1 行にする

        vm.Ui.FixOutcome = null;
        vm.PushUndo();
        vm.NoteGuidedFix(0, 1, "12/1", "A");
        vm.Undo();
        await vm.LastRefreshCheckTask!;

        Assert.Null(vm.Ui.FixOutcome);   // 戻した盤面には結ばない
    }
}
