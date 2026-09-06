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
        var vm = new MagiViewModel
        {
            _state = MinimalState.Build(),
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
        Assert.Null(vm.Ui.Message);
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
}
