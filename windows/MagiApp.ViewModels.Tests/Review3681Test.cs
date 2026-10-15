using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3681Test</c> の 1 対 1 移植。3.681.0: 不足枠の候補（B4）、確認の画面の対象の前後（B8）、残る差の単位（B11）、止めた探索（B7）。</summary>
public class Review3681Test
{
    // 0=休 1=A（1 日目は 2 人要る） 2=D。職員 X,Y,Z（同じ群）。
    private static MagiState State(List<ExtWish>? ext = null, Dictionary<string, Range>? range = null, List<int>? x = null) => new(
        StartDate: "2026-07-01", EndDate: "2026-07-03",
        Shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("A", "A", "1", ""), new("D", "D", "", "") },
        Groups: new List<Group> { new("G0", "G0") }, StaffList: new List<Staff> { new("X", 0), new("Y", 0), new("Z", 0) }, Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }, GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        Schedule: new List<IReadOnlyList<int>> { x ?? new List<int> { 0, 0, 0 }, new List<int> { 0, 0, 0 }, new List<int> { 1, 1, 1 } },
        Wishes: new Dictionary<string, int>(), StaffRange: range ?? new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string> { ["1,0"] = "2" }, NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(), SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>(), ExtWishes: ext);

    private static int[][] Grid(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();
    private static List<int> Cands(MagiState st) => CellSheetLogic.ShortageCandidates(st, new Problem(st), Grid(st), 0, 1, 0).Select(c => c.StaffIndex).ToList();
    private static string Label(string f) => f == "covU" ? "人員不足" : f == "covO" ? "人員過剰" : f;

    [Fact]
    public void ExtBannedStaffIsNotOfferedForTheShortage()
    {
        Assert.Equal(new List<int> { 0, 1 }, Cands(State()));
        // 拡張希望で A を禁止した日の Y は出さない
        Assert.Equal(new List<int> { 0 }, Cands(State(new List<ExtWish> { new(1, new List<string> { "2026-07-01" }, new List<string> { "A" }) })));
    }

    [Fact]
    public void StaffAtTheirUpperLimitIsNotOffered()
    {
        var st = State(range: new Dictionary<string, Range> { ["0,1"] = new Range("", "1") }, x: new List<int> { 0, 1, 0 });
        Assert.Equal(new List<int> { 1 }, Cands(st));
    }

    [Fact]
    public void ReviewSaysTheTargetsBeforeAndAfter()
    {
        var st = State();
        var t = new FixTarget(null, 1, 0, 2, true, "必要人数");
        var names = st.StaffList.Select(s => s.Name).ToList(); var syms = st.Shifts.Select(s => s.Kigou).ToList();
        var r = CellSheetLogic.CellChangeReview(st, Grid(st), 0, 0, 1, names, syms, null, null, false, Label, new[] { t });
        Assert.Equal($"人員不足（{DayText.Short(st.StartDate, 0)}のA）：1人 → 2人", r.ThisCell[0]);
        var none = CellSheetLogic.CellChangeReview(st, Grid(st), 0, 2, 2, names, syms, null, null, false, Label);
        Assert.Equal(new List<string> { CellSheetLogic.NoCellViolation }, none.ThisCell);
    }

    [Fact]
    public void RemainingGapUsesTheTargetsUnit()
    {
        var multi = new FixTarget(null, 1, null, 0, true, "必要人数", new List<int> { 0, 1 }, new List<int> { 2, 2 });
        Assert.Equal("A（2日）は、合計 3人不足です", CellSheetLogic.RemainingGapLine("A（2日）", multi, 3));
        Assert.Equal("Aは、下限まであと2回です", CellSheetLogic.RemainingGapLine("A", new FixTarget(0, 1, null, 3, true, "下限"), 2));
    }

    [Fact]
    public void CancelledSearchIsItsOwnPanelState()
    {
        Assert.Equal(FixPanelState.Cancelled, CellSheetLogic.PanelState(false, false, "", "", "k", cancelledKey: "k"));
        Assert.Equal(FixPanelState.Running, CellSheetLogic.PanelState(false, true, "", "", "k", cancelledKey: "k"));   // 探し直しが始まれば探索中
        Assert.Equal("もう一度探す", CellSheetLogic.FixSearchRetryButton);
    }
}
