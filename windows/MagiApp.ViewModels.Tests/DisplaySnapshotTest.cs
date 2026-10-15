using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>DisplaySnapshotTest</c> の 1 対 1 移植。印と一文の写しが、セルのシート・上部の札・巡回と同じ答えを持つことを実データで固定する。</summary>
public class DisplaySnapshotTest
{
    private static readonly MagiState St = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));
    private static readonly int[][] S = St.Schedule.ToIntArray2D();
    private static readonly ViolationReport Rep = UnifiedViolationChecker.Check(St, S);
    private static string Label(string f) => f;

    private static UiState NewUi()
    {
        var b = UiState.EmptyBreakdown().ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var kv in Rep.Breakdown) b[kv.Key] = kv.Value;
        return new UiState
        {
            Schedule = S.Select(r => (IReadOnlyList<int>)r.ToList()).ToList(), BestHard = Rep.Hard, BestSoft = Rep.Soft,
            Breakdown = b, ViolationCells = Rep.Violations, ViolationCellFamilies = Rep.CellFamilies,
            NeedFamilies = Rep.NeedFamilies, CountFamilies = Rep.CountFamilies,
            C1Shortages = C1Display.Shortages(ScheduleUtil.CachedProblem(St), S), CheckRev = 1,
        };
    }

    [Fact]
    public void cellSeverityMatchesItsStatusLine()
    {
        var ui = NewUi();
        var snap = DisplaySnapshots.Build(St, ui, Label);
        Assert.NotEmpty(snap.CellMarks);
        var p = ScheduleUtil.CachedProblem(St);
        var c1 = GridDisplayMarks.C1DisplayMarks(ui);
        foreach (var ((i, j), m) in snap.CellMarks)
        {
            Assert.Equal(DisplaySnapshots.CellMarkFamilies(ui, i, j, c1), m.Families);
            var line = CellSheetLogic.StatusLine(St, p, S, i, j, m.Families, Label);
            Assert.Equal(line.Severity, m.Severity);
            Assert.Equal(line.Text, m.OneLine);
            Assert.Equal(m.Families.Any(f => MirrorKeys.Hard.Contains(f)), m.Severity == CellSeverity.Hard);
            Assert.StartsWith(m.Severity == CellSeverity.Hard ? "⚠ 必須：" : "⚠ 要調整：", m.OneLine);
        }
        Assert.Equal(CellSeverity.None, snap.CellMarkAt(-1, -1).Severity);
    }

    [Fact]
    public void hardTextEqualsUnitTotalsAndDisplayHardTextReadsIt()
    {
        var ui = NewUi();
        var snap = DisplaySnapshots.Build(St, ui, Label);
        Assert.Equal(AnalysisTriage.UnitTotals(ui.Breakdown, MirrorKeys.Hard) ?? $"{ui.BestHard}件", snap.HardText);
        ui.Display = snap;
        Assert.True(snap.FreshFor(ui));
        Assert.Equal(snap.HardText, DisplaySnapshots.DisplayHardText(ui));
        ui.CheckRev = 2; ui.BestHard = 0; ui.Breakdown = UiState.EmptyBreakdown();
        Assert.False(snap.FreshFor(ui));
        Assert.Equal(AnalysisTriage.HardText(UiState.EmptyBreakdown(), 0), DisplaySnapshots.DisplayHardText(ui));
    }

    [Fact]
    public void tourCountEqualsTourItems()
    {
        var ui = NewUi();
        var snap = DisplaySnapshots.Build(St, ui, Label);
        var items = CellSheetLogic.HardViolationItems(St, ScheduleUtil.CachedProblem(St), S, Rep.CellFamilies, Label);
        static string Key(TourItem t) => $"{t.Family}|{t.Staff}|{string.Join(',', t.Days)}|{t.Heading}";
        Assert.Equal(items.Select(Key), snap.TourItems.Select(Key));
        Assert.Equal(items.Count, snap.TourItems.Count);
        Assert.Equal(items.Count == 0 ? CellSheetLogic.TourCovULine(snap.CovUPeople) : CellSheetLogic.TourHeading(items, 0), snap.NextAction);
    }
}
