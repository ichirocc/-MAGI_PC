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
        DistLocations = Rep.DistLocations, C1Shortages = C1Display.Shortages(ScheduleUtil.CachedProblem(St), St.Schedule.ToIntArray2D()),
    };
    private static string Label(string f) => f;

    private static readonly IReadOnlySet<string> Marks = GridDisplayMarks.C1DisplayMarks(Ui);
    private static List<int> Marked(int i) => Enumerable.Range(0, St.DayCount).Where(d => GridDisplayMarks.DisplayCellClasses(Ui, $"{i},{d}", Marks).Contains("vio-c1")).ToList();
    private static int Rest => St.Shifts.ToList().FindIndex(s => s.Kigou == "休");

    [Fact]
    public void C1MarksAreChangeableDaysOnly()
    {
        var p = ScheduleUtil.CachedProblem(St);
        var s0 = Ui.C1Shortages.Single(x => x.Staff == 0);
        Assert.Equal((2, 11), (s0.From, s0.To));
        Assert.True(s0.Band);
        Assert.DoesNotContain(2, Marked(0));
        var expect0 = Enumerable.Range(3, 9).Where(d => St.Schedule[0][d] != Rest && C1Display.Changeable(p, 0, d, Rest)).ToList();
        Assert.Equal(expect0, Marked(0));
        Assert.Equal(new[] { 3, 4, 6, 8, 9, 10 }, Marked(0));
        Assert.True(!Marked(6).Contains(6) && St.Schedule[6][6] == Rest);
        Assert.True(!Marked(9).Contains(4) && St.Schedule[9][4] == Rest);
        foreach (var sh in Ui.C1Shortages) foreach (var d in sh.Marks) Assert.NotEqual(Rest, St.Schedule[sh.Staff][d]);
        var band = GridDisplayMarks.C1Band(Ui, VioBuckets.AllKeys, St.StaffCount, St.DayCount);
        Assert.Equal(Enumerable.Range(2, 10), Enumerable.Range(0, St.DayCount).Where(d => band[0][d]));
        Assert.All(GridDisplayMarks.C1Band(Ui, VioBuckets.AllKeys.Where(k => k != "window").ToHashSet(), St.StaffCount, St.DayCount), r => Assert.DoesNotContain(true, r));
    }

    [Fact]
    public void EveryViolatedC1WindowWithAChangeableDayHasAMark()
    {
        Assert.Equal(Rep.Breakdown.GetValueOrDefault("c1"), Ui.C1Shortages.Sum(x => x.Windows));
        var p = ScheduleUtil.CachedProblem(St);
        foreach (var sh in Ui.C1Shortages)
            for (var w = sh.From; w <= sh.To - sh.Day1 + 1; w++)
            {
                var win = Enumerable.Range(w, sh.Day1).ToList();
                if (win.Any(d => St.Schedule[sh.Staff][d] != sh.Shift && C1Display.Changeable(p, sh.Staff, d, sh.Shift)))
                    Assert.Contains(win, d => sh.Marks.Contains(d));
            }
    }

    [Fact]
    public void C1SheetTextNamesThePeriodAndCountsHeldDays()
    {
        var p = ScheduleUtil.CachedProblem(St); var s = St.Schedule.ToIntArray2D();
        var text = Assert.Single(CellSheetLogic.CellDetailLines(St, p, s, 0, 2, new[] { "c1" }, Label));
        Assert.Equal("要調整・期間の約束: 7日のなかに「休」が2日必要です。いま足りない期間（10/3〜10/12）があり、印の日を休にすると届く見込みです。（この日の休はすでに数に入っています）", text);
        Assert.DoesNotContain("（この日の", Assert.Single(CellSheetLogic.CellDetailLines(St, p, s, 0, 3, new[] { "c1" }, Label)));
        Assert.Contains("vio-c1", CellSheetLogic.SheetCellClasses(GridDisplayMarks.DisplayCellClasses(Ui, "0,2", Marks), true));
    }

    [Fact]
    public void C1WorksForANonRestShift()
    {
        var st2 = St with { Cons1 = new[] { new C1Row("7", "A4", "2") } };
        var p = ScheduleUtil.CachedProblem(st2); var s = st2.Schedule.ToIntArray2D();
        var a4 = st2.Shifts.ToList().FindIndex(x => x.Kigou == "A4");
        var sh = C1Display.Shortages(p, s);
        Assert.True(sh.Count > 0 && sh.All(x => x.Shift == a4));
        foreach (var x in sh) foreach (var d in x.Marks) Assert.True(s[x.Staff][d] != a4 && C1Display.Changeable(p, x.Staff, d, a4));
        var y = sh.First(x => x.Marks.Count > 0);
        var line = Assert.Single(CellSheetLogic.CellDetailLines(st2, p, s, y.Staff, y.Marks[0], new[] { "c1" }, Label));
        Assert.True(line.Contains("「A4」が2日必要") && line.Contains("印の日をA4にすると"), line);
    }

    [Fact]
    public void WindowWithNoChangeableDayFallsBack()
    {
        var wishes = St.Wishes.ToDictionary(kv => kv.Key, kv => kv.Value);
        for (var d = 3; d <= 9; d++) wishes[$"0,{d}"] = St.Schedule[0][d];
        var st2 = St with { Wishes = wishes };
        var p = ScheduleUtil.CachedProblem(st2); var s = st2.Schedule.ToIntArray2D();
        var all = C1Display.Shortages(p, s);
        var s0 = all.Single(x => x.Staff == 0);
        Assert.True(s0.Stuck);
        Assert.DoesNotContain(s0.Marks, d => d >= 3 && d <= 9);
        Assert.Contains(C1Display.StuckText, Assert.Single(CellSheetLogic.CellDetailLines(st2, p, s, 0, 4, new[] { "c1" }, Label)));
        var u2 = new UiState { Schedule = St.Schedule, ShiftSymbols = Ui.ShiftSymbols, C1Shortages = all };
        Assert.Contains(0, GridDisplayMarks.C1Stuck(u2));
        Assert.Contains(GridDisplayMarks.StaffCountLines(u2, 0, Label), l => l.Contains(C1Display.StuckText));
        Assert.Contains(Ui.C1Shortages, x => x.Staff == 10 && x.Stuck);
    }

    [Fact]
    public void CheckerMarksStayAtTheRunHeadOnly()
    {
        var c1Cells = Rep.CellFamilies.Where(kv => kv.Value.Contains("vio-c1")).Select(kv => kv.Key).ToHashSet();
        Assert.Equal(Rep.C1Runs.Select(r => $"{r[0]},{r[1]}").ToHashSet(), c1Cells);
        foreach (var key in c1Cells.Where(k => !Marks.Contains(k))) Assert.DoesNotContain("vio-c1", GridDisplayMarks.DisplayCellClasses(Ui, key, Marks));
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
        var hidden = Rep.CellFamilies.Keys.Select(k => GridDisplayMarks.DisplayCellClasses(Ui, k, Marks))
            .Where(c => c.Contains("vio-c42s") && c[0] != "vio-c42s").ToList();
        Assert.NotEmpty(hidden);
        foreach (var c in hidden) Assert.NotNull(GridDisplayMarks.SecondVisibleClass(c, VioBuckets.AllKeys));
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
