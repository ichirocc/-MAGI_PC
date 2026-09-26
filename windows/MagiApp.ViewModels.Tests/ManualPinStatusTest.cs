using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[#41] Kotlin <c>ManualPinTest.cellStatusLineSaysAPinnedViolationCannotBeFixed</c> の移植: 手動固定のセルの状態の 1 行。</summary>
public class ManualPinStatusTest
{
    [Fact]
    public void CellStatusLineSaysAPinnedViolationCannotBeFixed()
    {
        var st0 = StateJsonSerializer.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));
        var s = st0.Schedule.ToIntArray2D();
        var pinned = st0 with { ManualPins = new List<ManualPin> { new(0, 0, s[0][0]) } };
        var line = CellSheetLogic.StatusLine(pinned, new Problem(pinned), s, 0, 0, new[] { "pref" }, f => f);
        Assert.Contains(CellSheetLogic.PinBlockedNote, line.Text);
        Assert.DoesNotContain(CellSheetLogic.PinBlockedNote, CellSheetLogic.StatusLine(st0, new Problem(st0), s, 0, 0, new[] { "pref" }, f => f).Text);
    }
}
