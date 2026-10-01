using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>CsvPinFollowTest</c> の写し＋ViewModel 層（CSV 取込で手動固定が取り込んだ値へ追従・同じ undo 段・完了文の件数、固定登録時のヒント）。</summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelCsvPinFollowTest
{
    public MagiViewModelCsvPinFollowTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    private static MagiState Pinned(params ManualPin[] pins) => MinimalState.Build() with { ManualPins = pins };

    [Fact]
    public void HelperFollowsDropsAndKeeps()
    {
        var st = Pinned(new ManualPin(0, 1, 1), new ManualPin(1, 2, 0));
        var old = new[] { new[] { 0, 1, 0, 0, 0, 0, 0 }, new[] { 0, 0, 0, 0, 0, 0, 0 } };
        var nw = new[] { new[] { 0, 0, 0, 0, 0, 0, 0 }, new[] { 0, 0, -1, 0, 0, 0, 0 } };
        var (ns, n) = st.WithPinsFollowingBoard(old, nw);
        Assert.Equal(2, n);
        Assert.Equal(0, ns.PinAt(0, 1)!.Shift);
        Assert.Null(ns.PinAt(1, 2));
        var (ns2, n2) = st.WithPinsFollowingBoard(old, new[] { new[] { 0, 1, 0, 0, 0, 0, 0 }, new[] { 0, 0, 9, 0, 0, 0, 0 } });
        Assert.Equal(1, n2);
        Assert.Null(ns2.PinAt(1, 2));
        Assert.Equal(1, ns2.PinAt(0, 1)!.Shift);
        var bare = MinimalState.Build();
        var (b, nb) = bare.WithPinsFollowingBoard(old, nw);
        Assert.Same(bare, b); Assert.Equal(0, nb);
        var (s0, n0) = st.WithPinsFollowingBoard(old, old);
        Assert.Same(st, s0); Assert.Equal(0, n0);
    }

    [Fact]
    public async Task ImportCsvMakesPinFollowInOneUndoStepAndReportsTheCount()
    {
        var st = Pinned(new ManualPin(0, 1, 0), new ManualPin(1, 3, 0));
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        var edited = MinimalState.BuildSchedule();
        edited[0][1] = 1;
        vm.ImportCsv(ScheduleCsvBridge.Build(st, edited));
        await vm.LastImportCsvTask!;

        Assert.Equal(1, vm._state!.PinAt(0, 1)!.Shift);
        Assert.Equal(0, vm._state!.PinAt(1, 3)!.Shift);
        Assert.Contains("手動固定 1 件を取り込んだ値に合わせました", vm.Ui.Message);

        vm.Undo();
        Assert.Equal(0, vm._state!.PinAt(0, 1)!.Shift);
        Assert.Equal(0, vm._currentSchedule![0][1]);
    }

    [Fact]
    public async Task ImportCsvWithoutPinChangeSaysNothingAboutPins()
    {
        var st = Pinned(new ManualPin(0, 1, 0));
        var sched = MinimalState.BuildSchedule();
        var vm = new MagiViewModel { _state = st, _currentSchedule = sched };
        vm.ImportCsv(ScheduleCsvBridge.Build(st, sched));
        await vm.LastImportCsvTask!;
        Assert.DoesNotContain("手動固定", vm.Ui.Message);
        Assert.Equal(0, vm._state!.PinAt(0, 1)!.Shift);
    }

    [Fact]
    public void PinRegisterHintOnlyInforms()
    {
        Assert.Equal("", CellSheetLogic.PinRegisterHint(true, Array.Empty<string>()));
        Assert.Equal("", CellSheetLogic.PinRegisterHint(true, new[] { "vio-c3mn", "vio-covO" }));
        Assert.Equal(CellSheetLogic.PinHardHint, CellSheetLogic.PinRegisterHint(true, new[] { "vio-c3mn", "vio-c3n" }));
        Assert.Equal(CellSheetLogic.PinHardHint, CellSheetLogic.PinRegisterHint(true, new[] { "vio-pref" }));
        Assert.Equal(CellSheetLogic.PinNotCanDoHint, CellSheetLogic.PinRegisterHint(false, new[] { "vio-groupViol" }));
        Assert.DoesNotContain("直せません", CellSheetLogic.PinHardHint + CellSheetLogic.PinNotCanDoHint);
    }

    [Fact]
    public void TogglePinAppendsTheHintOnlyOnRegistration()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>> { ["0,1"] = new[] { "vio-c3n" } };
        vm.TogglePin(0, 1);
        Assert.Contains(CellSheetLogic.PinHardHint, vm.Ui.OpNotice!.Text);
        Assert.NotNull(vm._state!.PinAt(0, 1));
        vm.TogglePin(0, 1);
        Assert.DoesNotContain("必須違反", vm.Ui.OpNotice!.Text);
        vm.TogglePin(0, 2);
        Assert.DoesNotContain("必須違反", vm.Ui.OpNotice!.Text);
        Assert.NotNull(vm._state!.PinAt(0, 2));
    }
}
