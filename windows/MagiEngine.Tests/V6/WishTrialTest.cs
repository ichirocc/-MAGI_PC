using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>[S5] 希望取り消しの試算（Kotlin <c>WishTrialTest</c>、<c>docs/s5_wish_trial.md</c> §12 T1〜T7・T11）。</summary>
public class WishTrialTest
{
    private const int Rest = 0, A = 1, B = 2;
    private static readonly int[] Row1 = { Rest, Rest, Rest, Rest, Rest };

    private static MagiState Build(
        string endDate, IReadOnlyList<Shift> shifts, IReadOnlyList<Group> groups, IReadOnlyList<Staff> staff, bool use2,
        IReadOnlyList<IReadOnlyList<int>> groupShift, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyDictionary<string, int> wishes,
        IReadOnlyList<C3wRow>? cons3w = null, IReadOnlyList<C3Row>? cons3n = null, IReadOnlyDictionary<string, Range>? staffRange = null) => new(
        StartDate: "2026-01-01", EndDate: endDate,
        Shifts: shifts, Groups: groups, StaffList: staff, Use2Patterns: use2,
        GroupShift: groupShift,
        GroupShiftApt: groupShift.Select(g => (IReadOnlyList<string>)g.Select(_ => "").ToList()).ToList(),
        Schedule: schedule, Wishes: wishes,
        StaffRange: staffRange ?? new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: cons3n ?? new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: new Dictionary<string, System.Text.Json.JsonElement>(),
        Cons3w: cons3w ?? new List<C3wRow>());

    private static MagiState State(int[][] schedule, Dictionary<string, int> wishes,
        IReadOnlyList<C3wRow>? cons3w = null, IReadOnlyList<C3Row>? cons3n = null, int[][]? groupShift = null) => Build(
        "2026-01-05",
        new[] { new Shift("休", "休", "", "", ShiftRole.Rest), new Shift("A", "A", "", ""), new Shift("B", "B", "", "") },
        new[] { new Group("G", "G") }, new[] { new Staff("s0", 0), new Staff("s1", 0) }, false,
        (groupShift ?? new[] { new[] { 1, 1, 1 } }).Select(r => (IReadOnlyList<int>)r).ToList(),
        schedule.Select(r => (IReadOnlyList<int>)r).ToList(), wishes, cons3w, cons3n);

    private static int[][] Board(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();
    private static WishTrial.Result Run(MagiState st, int i, int j) => (WishTrial.Result)WishTrial.Trial(st, Board(st), i, j)!;

    [Fact]
    public void T1_CertainPartIsOneForPrefAndC3wXAndZeroForC3n()
    {
        var pref = State(new[] { new[] { Rest, Rest, B, Rest, Rest }, Row1 }, new() { ["0,2"] = A });
        Assert.Equal(1, Run(pref, 0, 2).A);
        var c3w = State(new[] { new[] { Rest, B, A, Rest, Rest }, Row1 }, new() { ["0,2"] = A }, cons3w: new[] { new C3wRow("A", "B") });
        Assert.Equal(1, Run(c3w, 0, 2).A);
        var c3n = State(new[] { new[] { Rest, A, A, Rest, Rest }, Row1 }, new() { ["0,2"] = A }, cons3n: new[] { new C3Row(new[] { "A", "A" }) });
        var r = Run(c3n, 0, 2);
        Assert.Equal(1, r.H0);
        Assert.Equal(0, r.A);   // 満たされた希望は同じ盤面では何も消さない
    }

    [Fact]
    public void T2_UnlockedOrMissingWishIsNotATarget()
    {
        var unlocked = State(new[] { new[] { Rest, Rest, B, Rest, Rest }, Row1 }, new() { ["0,2"] = A }, groupShift: new[] { new[] { 1, 0, 1 } });
        Assert.Null(WishTrial.Trial(unlocked, Board(unlocked), 0, 2));
        Assert.DoesNotContain("0,2", WishTrial.LockedWishKeys(unlocked));
        var minus = State(new[] { new[] { Rest, Rest, B, Rest, Rest }, Row1 }, new() { ["0,2"] = -1 });
        Assert.Null(WishTrial.Trial(minus, Board(minus), 0, 2));
        Assert.Null(WishTrial.Trial(minus, Board(minus), 1, 2));
        var locked = State(new[] { new[] { Rest, Rest, B, Rest, Rest }, Row1 }, new() { ["0,2"] = A });
        Assert.Equal(new HashSet<string> { "0,2" }, WishTrial.LockedWishKeys(locked));
    }

    [Fact]
    public void T3_RepairIndependentOfTheWishIsSubtractedByTheControl()
    {
        // s1 の禁止の並び（希望と無関係）は希望を残したままでも修復で消える＝取り消しに帰属させない。
        var st = State(new[] { new[] { Rest, A, A, Rest, Rest }, new[] { A, A, Rest, Rest, Rest } }, new() { ["0,2"] = A },
            cons3n: new[] { new C3Row(new[] { "A", "A" }) });
        var r = Run(st, 0, 2);
        Assert.True(r.Rr < r.H0, "前提: 修復で減る");
        Assert.True(r.Rk < r.H0, "前提: 対照も減る");
        Assert.Equal(0, r.Att);
        Assert.Equal(0, r.B);
        Assert.Equal(0, r.APrime);
    }

    [Fact]
    public void T3b_C3wFixableWhileKeepingTheWishGivesAttBelowA()
    {
        // 前日の B は希望を残したまま動かせる＝対照が a をすでに含む。生の a を「確実に」と言わない。
        var st = State(new[] { new[] { Rest, B, A, Rest, Rest }, Row1 }, new() { ["0,2"] = A }, cons3w: new[] { new C3wRow("A", "B") });
        var r = Run(st, 0, 2);
        Assert.Equal(1, r.A);
        Assert.True(r.Rk < r.H0, "対照が減らす");
        Assert.True(r.Att < r.A);
        Assert.True(r.APrime < r.A);
    }

    [Fact]
    public void T4_Deterministic()
    {
        var st = State(new[] { new[] { Rest, A, A, Rest, Rest }, new[] { A, A, Rest, Rest, Rest } }, new() { ["0,2"] = A },
            cons3n: new[] { new C3Row(new[] { "A", "A" }) });
        Assert.Equal(Run(st, 0, 2), Run(st, 0, 2));
        var ctl = ((WishTrial.ControlOutcome)WishTrial.ControlOf(st, Board(st))).Control;
        Assert.Equal(Run(st, 0, 2), WishTrial.Trial(st, Board(st), 0, 2, control: ctl));
    }

    [Fact]
    public void T5_UnassignedCellIsUnavailableNotZero()
    {
        var st = State(new[] { new[] { Rest, -1, B, Rest, Rest }, Row1 }, new() { ["0,2"] = A });
        Assert.IsType<WishTrial.Unavailable>(WishTrial.Trial(st, Board(st), 0, 2));
        Assert.IsType<WishTrial.Unavailable>(WishTrial.ControlOf(st, Board(st)));
    }

    [Fact]
    public void T6_StoppedGivesNoNumbers()
    {
        var st = State(new[] { new[] { Rest, Rest, B, Rest, Rest }, Row1 }, new() { ["0,2"] = A });
        Assert.Equal(WishTrial.Stopped, WishTrial.Trial(st, Board(st), 0, 2, shouldStop: () => true));
    }

    [Fact]
    public void T7_InputsAreNotMutated()
    {
        var st = State(new[] { new[] { Rest, A, A, Rest, Rest }, new[] { A, A, Rest, Rest, Rest } }, new() { ["0,2"] = A },
            cons3n: new[] { new C3Row(new[] { "A", "A" }) });
        var wishesBefore = st.Wishes.ToDictionary(kv => kv.Key, kv => kv.Value);
        var schedBefore = st.Schedule.Select(r => r.ToArray()).ToArray();
        var board = Board(st);
        var before = board.Copy2D();
        WishTrial.Trial(st, board, 0, 2);
        Assert.Equal(wishesBefore, st.Wishes);
        Assert.Equal(schedBefore, st.Schedule.Select(r => r.ToArray()).ToArray());
        Assert.Equal(before, board);
    }

    [Fact]
    public void T11_WishPinnedListsOnlyPlaceableStaffLockedToAnotherShift()
    {
        // A の必要 3。s0=B 希望（入る）、s1=A 希望（不足シフトの希望＝入らない）、s2=A 上限 0 で B 希望（入らない）、
        // s3=希望なし（入らない）、s4=A を担当できず B 希望（入らない）。
        var shifts = new[] { new Shift("休", "休", "", "", ShiftRole.Rest), new Shift("A", "A", "3", "3"), new Shift("B", "B", "", "") };
        var groups = new[] { new Group("G", "G"), new Group("H", "H") };
        var gs = new[] { new[] { 1, 1, 1 }, new[] { 1, 0, 1 } }.Select(r => (IReadOnlyList<int>)r).ToList();
        var sched = new[] { new[] { B }, new[] { A }, new[] { B }, new[] { Rest }, new[] { B } }.Select(r => (IReadOnlyList<int>)r).ToList();
        var staff = new[] { new Staff("s0", 0), new Staff("s1", 0), new Staff("s2", 0), new Staff("s3", 0), new Staff("s4", 1) };
        var st = Build("2026-01-01", shifts, groups, staff, true, gs, sched,
            new Dictionary<string, int> { ["0,0"] = B, ["1,0"] = A, ["2,0"] = B, ["4,0"] = B },
            staffRange: new Dictionary<string, Range> { ["2,1"] = new Range("", "0") });
        var sf = V6PortAnalyzer.DiagnoseCoverage(st).Shortfalls.Single(s => s.ShiftIndex == A);
        Assert.Equal(new[] { 0 }, sf.WishPinned);
        Assert.Equal(CoverageVerdict.Infeasible, sf.Verdict);
        Assert.Equal("いまの希望のままでは担当できる人が2人で必要数3に届きません（希望で別の勤務に固定: 1人）", sf.Reason);

        var noWish = st with
        {
            Wishes = new Dictionary<string, int>(),
            StaffList = staff.Take(2).Append(new Staff("s2", 1)).Concat(staff.Skip(3).Select(s => new Staff(s.Name, 1))).ToList(),
        };
        var sf2 = V6PortAnalyzer.DiagnoseCoverage(noWish).Shortfalls.Single(s => s.ShiftIndex == A);
        Assert.Empty(sf2.WishPinned);
        Assert.Equal(CoverageVerdict.Infeasible, sf2.Verdict);
        Assert.StartsWith("担当可能な職員が", sf2.Reason);
    }
}
