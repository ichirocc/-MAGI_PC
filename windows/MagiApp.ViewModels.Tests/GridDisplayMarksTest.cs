using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>GridDisplayMarksTest</c> の 1 対 1 移植。勤務表の表示専用の印を実データで固定する。</summary>
public class GridDisplayMarksTest
{
    private static readonly MagiState St = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));
    private static readonly ViolationReport Rep = UnifiedViolationChecker.Check(St, St.Schedule.ToIntArray2D());
    private static readonly UiState Ui = new()
    {
        Schedule = St.Schedule, ShiftSymbols = St.Shifts.Select(s => s.Kigou).ToList(),
        ViolationCells = Rep.Violations, ViolationCellFamilies = Rep.CellFamilies,
        NeedViolations = Rep.NeedViolations, NeedFamilies = Rep.NeedFamilies,
        CountViolations = Rep.CountViolations, CountFamilies = Rep.CountFamilies,
        DistLocations = Rep.DistLocations, C1Runs = Rep.C1Runs,
    };
    private static string Label(string f) => f;

    [Fact]
    public void EveryViolatedC1WindowContainsADisplayMark()
    {
        Assert.Equal(Rep.Breakdown.GetValueOrDefault("c1"), Rep.C1Runs.Sum(r => r[2]));
        Assert.NotEmpty(Rep.C1Runs);
        var anchors = GridDisplayMarks.C1DisplayAnchors(Ui);
        foreach (var r in Rep.C1Runs)
            for (var s = r[1]; s < r[1] + r[2]; s++)
                Assert.Contains(Enumerable.Range(s, r[3]), a => GridDisplayMarks.DisplayCellClasses(Ui, $"{r[0]},{a}", anchors).Contains("vio-c1"));
    }

    [Fact]
    public void CheckerMarksStayAtTheRunHeadOnly()
    {
        var c1Cells = Rep.CellFamilies.Where(kv => kv.Value.Contains("vio-c1")).Select(kv => kv.Key).ToHashSet();
        Assert.Equal(Rep.C1Runs.Select(r => $"{r[0]},{r[1]}").ToHashSet(), c1Cells);
    }

    [Fact]
    public void EveryStaffWithACountKeyHasABadge()
    {
        Assert.Equal(27, Rep.CountFamilies.Count);
        var staff = Rep.CountFamilies.Keys.Select(k => int.Parse(k[..k.IndexOf(',')])).ToHashSet();
        Assert.Equal(staff, GridDisplayMarks.CountBadges(Ui, VioBuckets.AllKeys).Keys.ToHashSet());
        foreach (var i in staff) Assert.NotEmpty(GridDisplayMarks.StaffCountLines(Ui, i, Label));
        Assert.Empty(GridDisplayMarks.CountBadges(Ui, VioBuckets.AllKeys.Where(k => k != "count").ToHashSet()));
    }

    [Fact]
    public void CoverageHeaderNamesTheShift()
    {
        var rest = St.Shifts.ToList().FindIndex(s => s.Kigou == "休");
        var a4 = St.Shifts.ToList().FindIndex(s => s.Kigou == "A4");
        var marks = GridDisplayMarks.CoverageHeaderMarks(Ui, VioBuckets.AllKeys, St.Schedule[0].Count);
        Assert.Contains(new CoverageMark(rest, false), marks[27]);
        Assert.Contains(new CoverageMark(rest, false), marks[28]);
        Assert.Contains(new CoverageMark(a4, false), marks[8]);
        Assert.StartsWith("休▲", GridDisplayMarks.CoverageHeaderLabel(Ui, marks[27]));
        Assert.All(GridDisplayMarks.CoverageHeaderMarks(Ui, VioBuckets.AllKeys.Where(k => k != "need").ToHashSet(), 31), Assert.Empty);
    }

    [Fact]
    public void HiddenC42sCellsGetASecondaryDot()
    {
        var hidden = Rep.CellFamilies.Where(kv => kv.Value.Contains("vio-c42s") && kv.Value[0] != "vio-c42s").ToList();
        Assert.Equal(5, hidden.Count);
        foreach (var kv in hidden) Assert.NotNull(GridDisplayMarks.SecondVisibleClass(kv.Value, VioBuckets.AllKeys));
    }

    [Fact]
    public void FairAndWeeklyAppearInTheStaffSheet()
    {
        foreach (var fam in new[] { "fair", "weekly" })
            foreach (var e in Rep.DistLocations.GetValueOrDefault(fam) ?? Array.Empty<IReadOnlyList<int>>())
                Assert.Contains(GridDisplayMarks.StaffCountLines(Ui, e[0], Label), l => l.Contains(fam));
    }

    [Fact]
    public void SequenceBucketIsNamedNarabi() =>
        Assert.Equal("並び", VioBuckets.Buckets.Single(b => b.Key == "seq").Label);

    [Fact]
    public void NoFixReasonsNameOnlyVerifiedFacts()
    {
        var u = new UiState
        {
            Shifts = 3, ShiftSymbols = new[] { "休", "A", "B" },
            Schedule = new IReadOnlyList<int>[] { new[] { 1, 1, 0 }, new[] { 2, 0, 1 } },
            Wishes = new Dictionary<string, int> { ["0,0"] = 1, ["0,1"] = 1 },
        };
        (int? Lo, int? Hi, int? Apt) Limits(int i, int k) => (i, k) switch { (0, 1) => (null, 0, null), (0, 0) => (1, 1, null), _ => (null, null, null) };
        (int Lo, int Hi)? Need(int k, int j) => k == 1 ? (1, 1) : null;
        var why = FixSearchText.NoFixReasons(u, new FixFocus(0, 1), Limits, Need);
        Assert.True(why.WishRelated);
        Assert.Contains(why.Lines, l => l.Contains("どれも本人の希望で固定"));
        Assert.Contains(why.Lines, l => l.Contains("上限 0"));
        Assert.Contains(why.Lines, l => l.Contains("必要人数ぎりぎり"));
        Assert.Contains(why.Lines, l => l.Contains("下限＝上限で固定") && l.Contains("休 1回"));
        Assert.Equal(FixSearchText.NoFixScope, why.Lines[^1]);
        Assert.Equal("yr_count", why.SettingsSection);
        Assert.Equal(new[] { FixSearchText.NoFixScope }, FixSearchText.NoFixReasons(u, new FixFocus(1, 2)).Lines);
        Assert.Equal("yr_headcount", FixSearchText.NoFixReasons(u, new FixFocus(null, 1, 0)).SettingsSection);
        Assert.Contains("このセル", FixSearchText.NoFixReasons(u, new FixFocus(0, null, 0)).Lines[0]);
    }

    [Fact]
    public void FixFocusKeyTellsRequestsApart()
    {
        Assert.NotEqual(new FixFocus(0, 1).Key, new FixFocus(0, null, 1).Key);
        Assert.Equal(new FixFocus(0, 1).Key, new FixFocus(0, 1, null).Key);
    }

    [Fact]
    public void ShiftTotalsCarryCoverageDays()
    {
        var marks = GridDisplayMarks.CoverageHeaderMarks(Ui, VioBuckets.AllKeys, St.Schedule[0].Count);
        var totals = FixSearchText.ShiftCoverageTotals(marks);
        var rest = St.Shifts.ToList().FindIndex(s => s.Kigou == "休");
        var a4 = St.Shifts.ToList().FindIndex(s => s.Kigou == "A4");
        Assert.Contains(27, totals[rest].OverDays);
        Assert.Contains(28, totals[rest].OverDays);
        Assert.Equal(new[] { 8 }, totals[a4].OverDays);
        Assert.StartsWith("▲", totals[a4].Glyph);
        var needKeys = Rep.NeedFamilies.Where(kv => kv.Value.Any(c => c is "vio-covU" or "vio-covO")).Select(kv => int.Parse(kv.Key[..kv.Key.IndexOf(',')])).ToHashSet();
        Assert.Equal(needKeys, totals.Keys.ToHashSet());
    }
}
