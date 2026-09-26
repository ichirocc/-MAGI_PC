using MagiApp.ViewModels.Services;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [S6] 「設定を緩めたら」試算と確定の VM（<c>MagiViewModel.RelaxTrial.cs</c>、<c>docs/s6_relax_trial.md</c> §6・§8・§12）。
/// 実データ（2026-10、氏名は伏せ字）: 組 {職員11 Cｵ, 職員10 Pｼ}、5→4。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelRelaxTrialTest : IDisposable
{
    private readonly List<string> _dirs = new();

    public MagiViewModelRelaxTrialTest()
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

    private sealed class NoOptimize : IOptimizationService
    {
        public Task<V6FinalPort.ActionResult> OptimizeAsync(MagiState state, int[][] schedule, int secondsRaw, int? workers, bool softPolish,
            V6Algorithm requestedAlgorithm, bool allowImpossible, Action<string, ViolationReport?, long, long>? onProgress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<int[][]> SoftPolishAsync(MagiState state, int[][] schedule, int seconds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static readonly MagiState Oct = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));

    private MagiViewModel NewVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "magi-vm-relax-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        var vm = new MagiViewModel(new NoOptimize())
        {
            DataDir = dir, _state = Oct, _currentSchedule = Oct.Schedule.Select(r => r.ToArray()).ToArray(),
        };
        vm._hydrated = true;
        vm.Ui.BestHard = 5;
        return vm;
    }

    private static async Task<RelaxToken> Found(MagiViewModel vm)
    {
        vm.StartRelaxTrial();
        Assert.True(vm.Ui.RelaxSearching);
        await vm.LastRelaxTrialTask!;
        Assert.False(vm.Ui.RelaxSearching);
        return Assert.IsType<RelaxToken>(vm.RelaxTrialFor());
    }

    [Fact]
    public async Task ConfirmReproducesTheTrialAndOneUndoRestoresSettingsAndBoard()
    {
        var vm = NewVm();
        var st0 = vm._state!;
        var b0 = vm._currentSchedule!.Copy2D();
        var token = await Found(vm);
        Assert.Equal(2, token.Result.Relaxes.Count);
        var undo0 = vm.UndoStackCount;

        vm.RelaxAndApply(token);

        Assert.Equal(undo0 + 1, vm.UndoStackCount);
        Assert.Equal(4, UnifiedViolationChecker.Check(vm._state!, vm._currentSchedule!.Copy2D()).Hard);
        Assert.Equal("1", vm._state!.StaffRange["10,6"].Hi);
        Assert.Equal("設定を緩めて手順を当てました: 必須違反 5 → 4。元に戻すで設定と勤務表をまとめて戻せます。", vm.RelaxDoneLine());

        vm.Undo();
        Assert.Same(st0, vm._state);
        Assert.Equal(b0, vm._currentSchedule);
        Assert.Null(vm.RelaxDoneLine());
    }

    [Fact]
    public async Task StaleDataRefusesButRunSettingsDoNot()
    {
        var vm = NewVm();
        var token = await Found(vm);
        vm.Ui.BudgetSec = vm.Ui.BudgetSec + 10;   // 実行設定は鮮度に含めない
        Assert.NotNull(vm.RelaxTrialFor());

        var b = vm._currentSchedule!.Copy2D();
        b[0][0] = b[0][0] == 0 ? 1 : 0;
        vm._currentSchedule = b;
        var undo0 = vm.UndoStackCount;
        Assert.Null(vm.RelaxTrialFor());
        vm.RelaxAndApply(token);
        Assert.Equal(undo0, vm.UndoStackCount);
        Assert.Same(Oct, vm._state);
        Assert.Contains("もう一度試算", vm.Ui.Message);
    }

    [Fact]
    public async Task MovesThatDoNotReproduceTheTrialAreRefused()
    {
        var vm = NewVm();
        var token = await Found(vm);
        var bad = token with { Result = token.Result with { Moves = token.Result.Moves.Take(1).ToList() } };
        var b0 = vm._currentSchedule!.Copy2D();
        var undo0 = vm.UndoStackCount;

        vm.RelaxAndApply(bad);

        Assert.Equal(undo0, vm.UndoStackCount);
        Assert.Same(Oct, vm._state);
        Assert.Equal(b0, vm._currentSchedule);
        Assert.Contains("同じ結果になりませんでした", vm.Ui.Message);
    }

    [Fact]
    public void CancelRelaxTrialStopsWithoutAResult()
    {
        var vm = NewVm();
        vm.StartRelaxTrial();
        vm.CancelRelaxTrial();
        Assert.False(vm.Ui.RelaxSearching);
        Assert.Null(vm.RelaxTrialFor());
    }

    [Fact]
    public void TextNamesTheSetAndTheMoves()
    {
        var board = Oct.Schedule.Select(r => r.ToArray()).ToArray();
        var ui = new UiState
        {
            StaffNames = Oct.StaffList.Select(s => s.Name).ToList(),
            ShiftSymbols = Oct.Shifts.Select(s => s.Kigou).ToList(),
            Schedule = Oct.Schedule,
            ViolationCellFamilies = UnifiedViolationChecker.Check(Oct, board).CellFamilies,
        };
        var r = (RelaxTrial.Result)RelaxTrial.FirstWall(Oct, board);
        var t = NextActionGuide.RelaxTrialTextOf(r, ui);
        Assert.Equal(new[] { "職員10 Pｼ 上限 0→1", "職員11 Cｵ 上限 0→1" }, t.Rows.Select(x => x.Split('（')[0]));
        Assert.Equal("手で置いた勤務に合わせて上限を上げ、この組も緩めると、必須違反が 1件 減る見込みです。", t.Lead);
        Assert.StartsWith("職員10 ", t.Title);
        Assert.EndsWith("禁止の並び", t.Title);
        Assert.Equal(r.Moves.Count, t.MoveLines.Sum(l => l.Count(c => c == '→')) + t.OtherMoves);
    }
}
