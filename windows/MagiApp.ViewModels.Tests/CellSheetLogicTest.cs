using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>CellSheetLogicTest</c> の 1 対 1 移植。セル編集シートの固定配置・1 行の状態・印・巡回を実データで固定する。</summary>
public class CellSheetLogicTest
{
    private static readonly MagiState St = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));
    private static readonly int[][] S = St.Schedule.ToIntArray2D();
    private static readonly ViolationReport Rep = UnifiedViolationChecker.Check(St, S);
    private static readonly Problem P = ScheduleUtil.CachedProblem(St);
    private static readonly Dictionary<string, string> Labels = new() { ["c3n"] = "禁止の並び", ["c42s"] = "スキルグループペア" };
    private static string Label(string f) => Labels.GetValueOrDefault(f, f);

    private static int Staff(string name) => St.StaffList.ToList().FindIndex(x => x.Name == name);
    private static IReadOnlyList<string> Of(IReadOnlyDictionary<string, IReadOnlyList<string>>? m, string k) =>
        m is not null && m.TryGetValue(k, out var v) ? v : Array.Empty<string>();

    private static CellStatus Status(int i, int j)
    {
        var cur = S[i][j];
        var fams = CellSheetLogic.StatusFamilies(Of(Rep.CellFamilies, $"{i},{j}"), Of(Rep.NeedFamilies, $"{cur},{j}"), Of(Rep.CountFamilies, $"{i},{cur}"));
        return CellSheetLogic.StatusLine(St, P, S, i, j, fams, Label);
    }

    private static List<List<int?>> Ids(IReadOnlyList<IReadOnlyList<ShiftSlot?>> slots) => slots.Select(r => r.Select(x => x?.Shift).ToList()).ToList();

    [Fact]
    public void SlotsKeepTheirPlaceWhateverTheStaffCanDo()
    {
        var shifts = Enumerable.Range(0, 10).ToList();
        var all = CellSheetLogic.Slots(shifts, shifts.ToHashSet());
        var few = CellSheetLogic.Slots(shifts, new HashSet<int> { 0, 3 });
        Assert.Equal(3, all.Count);
        Assert.Equal(Ids(all), Ids(few));
        Assert.Equal(new[] { true, false, false, true }, few[0].Select(x => x!.CanDo));
        Assert.Equal(new int?[] { 8, 9, null, null }, few[2].Select(x => x?.Shift));
    }

    [Fact]
    public void ShiftsNobodyCanDoAreHiddenAndLeftHandMirrors()
    {
        var shown = CellSheetLogic.SheetShifts(6, new[] { new[] { 0, 2 }, new[] { 2, 5 }, Array.Empty<int>() });
        Assert.Equal(new[] { 0, 2, 5 }, shown);
        Assert.Equal(new[] { 0, 1, 2 }, CellSheetLogic.SheetShifts(3, new[] { Array.Empty<int>() }));
        Assert.Equal(new int?[] { 0, 2, 5, null }, CellSheetLogic.Slots(shown, new HashSet<int> { 0 })[0].Select(x => x?.Shift));
        Assert.Equal(new int?[] { null, 5, 2, 0 }, CellSheetLogic.Slots(shown, new HashSet<int> { 0 }, leftHand: true)[0].Select(x => x?.Shift));
        var canDo = Enumerable.Range(0, St.StaffCount).Select(i => P.CanDoShiftsForStaff(i).ToHashSet()).ToList();
        var real = CellSheetLogic.SheetShifts(St.ShiftCount, canDo);
        var layout = Ids(CellSheetLogic.Slots(real, new HashSet<int>()));
        foreach (var c in canDo) Assert.Equal(layout, Ids(CellSheetLogic.Slots(real, c)));
    }

    [Fact]
    public void C3wLineNamesTheNextDayWish()
    {
        var line = Status(Staff("職員03"), 0);
        Assert.Equal(CellSeverity.Hard, line.Severity);
        Assert.Equal("⚠ 必須：翌日(休)への前日禁止（Dﾃ）", line.Text);
    }

    [Fact]
    public void C42sLineNamesThePairedStaff()
    {
        var line = Status(Staff("職員06"), 5);
        Assert.Equal("⚠ 要調整：職員11(Cｵ)とのスキルグループペア禁止", line.Text);
    }

    [Fact]
    public void C3nLineShowsThePatternAndDays()
    {
        var line = Status(Staff("職員10"), 7);
        Assert.Equal(CellSeverity.Hard, line.Severity);
        Assert.Equal("⚠ 必須：禁止の並び Dﾃ→A4（10/8〜10/9）（ほか1件）", line.Text);
    }

    [Fact]
    public void CleanCellSaysNoViolationAndGetsNoDots()
    {
        var clean = Enumerable.Range(0, St.StaffCount).SelectMany(i => Enumerable.Range(0, St.DayCount).Select(j => (i, j))).First(c =>
        {
            var cur = S[c.i][c.j];
            return Of(Rep.CellFamilies, $"{c.i},{c.j}").Count == 0 && Of(Rep.NeedFamilies, $"{cur},{c.j}").Count == 0 && Of(Rep.CountFamilies, $"{c.i},{cur}").Count == 0;
        });
        Assert.Equal(new CellStatus(CellSeverity.None, "違反なし"), Status(clean.i, clean.j));
        Assert.Empty(CellSheetLogic.EvaluateShiftMarks(St, S, clean.i, clean.j, CellSeverity.None, Enumerable.Range(0, St.ShiftCount)).Recommended);
    }

    [Fact]
    public void MarksRecommendOnlyHardReducersAndWarnOnNewHard()
    {
        var i = Staff("職員03");
        var cands = Enumerable.Range(0, St.ShiftCount).ToList();
        var m = CellSheetLogic.EvaluateShiftMarks(St, S, i, 0, CellSeverity.Hard, cands);
        var bas = UnifiedViolationChecker.Check(St, S);
        foreach (var k in cands)
        {
            if (k == S[i][0]) continue;
            var t = S.Select(r => (int[])r.Clone()).ToArray();
            t[i][0] = k;
            var r = UnifiedViolationChecker.Check(St, t);
            var newHard = MirrorKeys.Hard.Any(f => r.Breakdown.GetValueOrDefault(f) > bas.Breakdown.GetValueOrDefault(f));
            Assert.Equal(newHard, m.HardRisk.Contains(k));
            Assert.Equal(!newHard && r.Hard < bas.Hard, m.Recommended.Contains(k));
        }
        var stopped = CellSheetLogic.EvaluateShiftMarks(St, S, i, 0, CellSeverity.Hard, cands, () => false);
        Assert.Empty(stopped.Recommended);
        Assert.Empty(stopped.HardRisk);
    }

    [Fact]
    public void A4OnStaff10Oct8IsMarkedAsHardRisk()
    {
        var i = Staff("職員10");
        var a4 = St.Shifts.ToList().FindIndex(x => x.Kigou == "A4");
        var m = CellSheetLogic.EvaluateShiftMarks(St, S, i, 7, CellSeverity.Hard, Enumerable.Range(0, St.ShiftCount));
        Assert.Contains(a4, m.HardRisk);
    }

    [Fact]
    public void WishDilemmaOnStaff08Oct3NamesTheCause()
    {
        var i = Staff("職員08");
        var line = Status(i, 2);
        var wish = St.Wishes[$"{i},2"];
        Assert.Equal(S[i][2], wish);
        Assert.True(CellSheetLogic.IsWishDilemma(wish, S[i][2], line.Severity));
        Assert.Equal("本人の希望（休）を守っています", CellSheetLogic.WishKeptLine(St.Shifts[wish].Kigou));
        Assert.Equal("職員04(休)とのスキルグループペア禁止", line.Cause);
        Assert.False(CellSheetLogic.IsWishDilemma(wish, S[i][2], CellSeverity.None));
        Assert.False(CellSheetLogic.IsWishDilemma(null, S[i][2], line.Severity));
        Assert.False(CellSheetLogic.IsWishDilemma(wish + 1, S[i][2], line.Severity));
    }

    [Fact]
    public void TourVisitsHardCellsFirstByDayThenStaff()
    {
        var ui = new UiState
        {
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>>
            {
                ["3,5"] = new[] { "vio-c3n" }, ["1,5"] = new[] { "vio-pref" }, ["0,2"] = new[] { "vio-c42s" }, ["2,1"] = new[] { "vio-c3w" },
            },
        };
        Assert.Equal(new[] { (2, 1), (1, 5), (3, 5) }, CellSheetLogic.ViolationTour(ui));
        Assert.Equal(new[] { (2, 1), (1, 5), (3, 5), (0, 2) }, CellSheetLogic.ViolationTour(ui, includeSoft: true));
        var soft = new UiState { ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>> { ["0,2"] = new[] { "vio-c42s" } } };
        Assert.Equal(new[] { (0, 2) }, CellSheetLogic.ViolationTour(soft));
        var tour = CellSheetLogic.ViolationTour(ui);
        Assert.Equal((1, 5), CellSheetLogic.NextTourCell(tour, (2, 1)));
        Assert.Equal((2, 1), CellSheetLogic.NextTourCell(tour, (3, 5)));
        Assert.Equal((2, 1), CellSheetLogic.NextTourCell(tour, (9, 9)));
        Assert.Null(CellSheetLogic.NextTourCell(Array.Empty<(int, int)>(), (0, 0)));
    }

    [Fact]
    public void CountLineAndDayLabels()
    {
        Assert.Equal("休 11(適10)▲ Cｵ 9(適5)▲", CellSheetLogic.StaffCountShort(St, P, S, 0, Rep.CountFamilies!));
        Assert.Equal("", CellSheetLogic.StaffCountShort(St, P, S, 0, new Dictionary<string, IReadOnlyList<string>>()));
        Assert.Equal("7日(水)", CellSheetLogic.AdjacentDayLabel("2026-10-01", 31, 6));
        Assert.Null(CellSheetLogic.AdjacentDayLabel("2026-10-01", 31, 31));
        Assert.Null(CellSheetLogic.AdjacentDayLabel("2026-10-01", 31, -1));
        Assert.Equal("職員01 3日をA4に変更しました", CellSheetLogic.CellChangedMessage("職員01", 2, "A4"));
        Assert.Equal("未登録", CellSheetLogic.WishTabState(null, 1));
        Assert.Equal("反映済", CellSheetLogic.WishTabState(1, 1));
        Assert.Equal("未反映", CellSheetLogic.WishTabState(2, 1));
    }

    [Fact]
    public void FixesByOthersKeepThePersonAndTheDay()
    {
        FixSuggestion Mk(params FixCell[] ops) => new(FixKind.Change, ops, "", 0, 0, Array.Empty<(string, int)>());
        var a = Mk(new FixCell(1, 2, 0));
        var b = Mk(new FixCell(7, 2, 0), new FixCell(1, 2, 3));
        var c = Mk(new FixCell(1, 3, 0));
        Assert.Equal(new[] { a }, CellSheetLogic.FixesByOthers(new[] { a, b, c }, 2, 7));
        Assert.NotEqual(new FixFocus(null, null, 2, 7).Key, new FixFocus(null, null, 2).Key);
    }

    [Fact]
    public void FixPanelStatesSpinOnlyWhileRunning()
    {
        Assert.Equal(FixPanelState.WaitCheck, CellSheetLogic.PanelState(running: true, fixSearching: false, doneKey: "k", failedKey: "", key: "k"));
        Assert.Equal(FixPanelState.NotStarted, CellSheetLogic.PanelState(false, false, "", "", "k"));
        Assert.Equal(FixPanelState.Running, CellSheetLogic.PanelState(false, true, "", "", "k"));
        Assert.Equal(FixPanelState.Done, CellSheetLogic.PanelState(false, false, "k", "", "k"));
        Assert.Equal(FixPanelState.Failed, CellSheetLogic.PanelState(false, false, "", "k", "k"));
        Assert.Equal(FixPanelState.NotStarted, CellSheetLogic.PanelState(false, false, "other", "", "k"));
    }

    [Fact]
    public void NoticeUndoOnlyForItsOwnOperationAndProgressDoesNotReplaceIt()
    {
        Assert.True(CellSheetLogic.NoticeUndoApplies(7L, 7L));
        Assert.False(CellSheetLogic.NoticeUndoApplies(8L, 7L));
        Assert.False(CellSheetLogic.NoticeUndoApplies(null, 7L));
        Assert.False(CellSheetLogic.MessageMayReplaceNotice(noticeShowing: true, isError: false));
        Assert.True(CellSheetLogic.MessageMayReplaceNotice(noticeShowing: true, isError: true));
        Assert.True(CellSheetLogic.MessageMayReplaceNotice(noticeShowing: false, isError: false));
    }

    [Fact]
    public void DetailListsEveryOverlappingFamilyAndC1Runs()
    {
        var (key, cls) = Rep.CellFamilies!.First(kv => kv.Value.Select(VioBuckets.FamilyOfVioClass).Distinct().Count() >= 2);
        var parts = key.Split(',');
        int i = int.Parse(parts[0]), j = int.Parse(parts[1]);
        var fams = CellSheetLogic.StatusFamilies(cls, Array.Empty<string>(), Array.Empty<string>());
        var lines = CellSheetLogic.CellDetailLines(St, P, S, i, j, fams, null, Label);
        Assert.Equal(fams.Count, lines.Count);
        Assert.All(lines, l => Assert.True(l.StartsWith("必須・") || l.StartsWith("要調整・"), l));
        var c1 = CellSheetLogic.CellDetailLines(St, P, S, i, j, new[] { "c1" }, 3, f => f == "c1" ? "期間の制約" : f);
        Assert.Contains("期間の制約（連続 3 区間）", Assert.Single(c1));
    }
}
