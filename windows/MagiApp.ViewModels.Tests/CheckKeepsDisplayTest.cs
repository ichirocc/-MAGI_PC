using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>CheckKeepsDisplayTest</c>（3.726.0）の移植。走行中の検査の結果が、その後の手動固定・表示色（検査を回さない 2 経路）を
/// 画面から巻き戻さない（<c>KeepDisplayOf</c>）。Kotlin は UiState の拡張関数、C# は VM が Ui を書き換える＝VM の上で同じ場面を作る。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class CheckKeepsDisplayTest
{
    public CheckKeepsDisplayTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    private static readonly MagiState Checked = MinimalState.Build();

    [Fact]
    public void PinAndColorChangedDuringTheCheckSurviveItsResult()
    {
        var vm = new MagiViewModel { _state = Checked, _currentSchedule = MinimalState.BuildSchedule() };
        vm.ApplyWishDisplay(Checked);
        // 検査の後に固定を付け、色を変えた。
        var now = Checked with { ManualPins = new[] { new ManualPin(0, 0, 1) }, ShiftColors = new Dictionary<string, string> { ["A"] = "#112233" } };
        vm.KeepDisplayOf(Checked, now);
        Assert.Equal(new HashSet<string> { "0,0" }, vm.Ui.ManualPins);
        Assert.Equal(ShiftAppearance.ResolveShiftColor("#112233", 1), vm.Ui.ShiftColorHex[1]);
    }

    [Fact]
    public void OtherChangesAndNoChangeLeaveTheCheckResultAlone()
    {
        var vm = new MagiViewModel { _state = Checked, _currentSchedule = MinimalState.BuildSchedule() };
        vm.ApplyWishDisplay(Checked);
        vm.KeepDisplayOf(Checked, null);
        vm.KeepDisplayOf(Checked, Checked);
        Assert.Empty(vm.Ui.ManualPins);
        // 希望の変更は採点に効く＝検査の世代で古い結果ごと捨てる側。ここでは触らない。
        var wished = Checked with { Wishes = new Dictionary<string, int> { ["0,0"] = 1 }, ManualPins = new[] { new ManualPin(0, 0, 1) } };
        Assert.False(DisplayOnlyUndo.DiffersOnlyInPinsOrColors(Checked, wished));
        vm.KeepDisplayOf(Checked, wished);
        Assert.Empty(vm.Ui.ManualPins);
    }

    [Fact]
    public void PushReportAppliesItWithTheCurrentState()
    {
        string? dir = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && dir is null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MagiApp.ViewModels", "MagiViewModel.Diagnostics.cs"))) dir = d.FullName;
        var src = File.ReadAllText(Path.Combine(dir!, "MagiApp.ViewModels", "MagiViewModel.Diagnostics.cs"));
        var body = src[src.IndexOf("internal async Task PushReportAsync(", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n    }\n", StringComparison.Ordinal)];
        Assert.Contains("KeepDisplayOf(st, _state);", body);
    }
}
