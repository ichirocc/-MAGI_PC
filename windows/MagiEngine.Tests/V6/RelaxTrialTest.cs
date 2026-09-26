using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>[S6] 設定の緩和の試算（Kotlin <c>RelaxTrialTest</c>、<c>docs/s6_relax_trial.md</c> §12 R1〜R13）。</summary>
public class RelaxTrialTest
{
    private const int Rest = 0, A = 1, B = 2;

    // 2 日・A は毎日 1 人必要・「A→A」は禁止。s0 が A を 2 日続ける（必須違反 1）。s1 は A の上限 0。
    private static MagiState State(int[]? s1Row = null, Dictionary<string, Range>? staffRange = null,
        int[][]? groupShift = null, Staff[]? staff = null, Group[]? groups = null, Dictionary<string, int>? wishes = null) => new(
        StartDate: "2026-01-01", EndDate: "2026-01-02",
        Shifts: new[] { new Shift("休", "休", "", "", ShiftRole.Rest), new Shift("A", "A", "1", "1"), new Shift("B", "B", "", "") },
        Groups: groups ?? new[] { new Group("G", "G") }, StaffList: staff ?? new[] { new Staff("s0", 0), new Staff("s1", 0) }, Use2Patterns: false,
        GroupShift: (groupShift ?? new[] { new[] { 1, 1, 1 } }).Select(r => (IReadOnlyList<int>)r).ToList(),
        GroupShiftApt: new List<IReadOnlyList<string>>(),
        Schedule: new List<IReadOnlyList<int>> { new[] { A, A }, s1Row ?? new[] { Rest, Rest } },
        Wishes: wishes ?? new Dictionary<string, int>(),
        StaffRange: staffRange ?? new Dictionary<string, Range> { ["1,1"] = new("0", "0") },
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new[] { new C3Row(new[] { "A", "A" }) }, Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: new Dictionary<string, System.Text.Json.JsonElement>(),
        Cons3w: new List<C3wRow>());

    private static int[][] Board(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();
    private static RelaxTrial.Outcome? Discover(MagiState st, int i = 0, int j = 0, Func<bool>? stop = null) =>
        RelaxTrial.Discover(st, Board(st), i, j, shouldStop: stop);

    private static MagiState TwoGroups(Dictionary<string, Range> sr) => State(staffRange: sr,
        groups: new[] { new Group("G", "G"), new Group("H", "H") }, groupShift: new[] { new[] { 1, 1, 1 }, new[] { 1, 0, 1 } },
        staff: new[] { new Staff("s0", 0), new Staff("s1", 1) });

    [Fact]
    public void R1_FindsTheUpperZeroThatBlocksTheFix()
    {
        var st = State();
        Assert.Equal(1, UnifiedViolationChecker.Check(st, Board(st)).Hard);
        var r = (RelaxTrial.Result)Discover(st)!;
        Assert.Equal(new[] { new RelaxTrial.Relax(1, A, 1) }, r.Relaxes);
        Assert.Empty(r.Prerequisite);
        Assert.Equal(1, r.H0); Assert.Equal(1, r.Rk); Assert.Equal(0, r.Rr);
        Assert.Equal(1, r.Att); Assert.Equal(1, r.AttWalls);
        Assert.Equal(2, r.Moves.Count);
        Assert.All(r.Moves, m => Assert.Equal(r.Moves[0].Day, m.Day));
    }

    [Fact]
    public void R2_NoUpperZeroWallGivesNoWall()
    {
        var st = TwoGroups(new() { ["1,2"] = new("0", "0") });
        Assert.Equal(new[] { (1, B) }, RelaxTrial.UpperZeroWalls(st).Select(w => (w.Staff, w.Shift)));
        Assert.Equal(RelaxTrial.NoWall, Discover(st));
        Assert.Equal(RelaxTrial.NoWall, Discover(TwoGroups(new())));
    }

    [Fact]
    public void R3_HandPlacedCappedCellIsAPrerequisiteNotAWall()
    {
        var st = State(new[] { B, Rest }, new() { ["1,1"] = new("0", "0"), ["1,2"] = new("", "0") });
        Assert.Equal(new[] { new RelaxTrial.Relax(1, B, 1) }, RelaxTrial.HandPlaced(st, Board(st)));
        var r = (RelaxTrial.Result)Discover(st)!;
        Assert.Equal(new[] { new RelaxTrial.Relax(1, B, 1) }, r.Prerequisite);
        Assert.Equal(new[] { new RelaxTrial.Relax(1, A, 1) }, r.Relaxes);
        Assert.Equal(0, r.Rr);
    }

    [Fact]
    public void R4_Deterministic() => Assert.Equal(Discover(State()), Discover(State()));

    [Fact]
    public void R5_UnassignedCellIsUnavailableNotZero() => Assert.IsType<RelaxTrial.Unavailable>(Discover(State(new[] { -1, Rest })));

    [Fact]
    public void R6_StoppedGivesNoNumbers() => Assert.Equal(RelaxTrial.Stopped, Discover(State(), stop: () => true));

    [Fact]
    public void R7_InputsAreNotMutated()
    {
        var st = State();
        var board = Board(st);
        var before = board.Copy2D();
        var sr = st.StaffRange.ToDictionary(kv => kv.Key, kv => kv.Value);
        RelaxTrial.Discover(st, board, 0, 0);
        Assert.Equal(before, board);
        Assert.Equal(sr, st.StaffRange);
    }

    [Fact]
    public void R8_NonHardCellIsNotAnAnchor() => Assert.Null(Discover(State(), 1, 0));

    [Fact]
    public void R9_ApplyRaisesOnlyTheUpperAndPinsAreNotWalls()
    {
        var st = State(staffRange: new() { ["1,1"] = new("0", "0"), ["1,0"] = new("", "0"), ["0,2"] = new("1", "1") });
        Assert.Equal(new[] { (1, A) }, RelaxTrial.UpperZeroWalls(st).Select(w => (w.Staff, w.Shift)));
        var st2 = RelaxTrial.Apply(st, new[] { new RelaxTrial.Relax(1, A, 1) });
        Assert.Equal(new Range("0", "1"), st2.StaffRange["1,1"]);
        Assert.Equal(st.StaffRange["0,2"], st2.StaffRange["0,2"]);
        Assert.True(ScheduleUtil.CachedProblem(st2).MayPlace(1, A));
    }

    [Fact]
    public void R10_WishSelfConflictCellIsNotAnAnchor()
    {
        var st = State(wishes: new() { ["0,0"] = A, ["0,1"] = A });
        Assert.Empty(RelaxTrial.Anchors(st, Board(st)));
        Assert.Equal(RelaxTrial.NoWall, RelaxTrial.FirstWall(st, Board(st)));
        Assert.Equal(new[] { (0, 0) }, RelaxTrial.Anchors(State(), Board(State())).Select(a => (a.Staff, a.Day)));
    }

    [Fact]
    public void R11_ApplyMovesRefusesAMismatchedBoard()
    {
        var bd = new[] { new[] { A, A }, new[] { Rest, Rest } };
        var m = new[] { new RelaxTrial.Move(0, 1, A, Rest), new RelaxTrial.Move(1, 1, Rest, A) };
        Assert.Equal(new[] { new[] { A, Rest }, new[] { Rest, A } }, RelaxTrial.ApplyMoves(bd, m, (_, _) => false));
        Assert.Equal(A, bd[0][1]);
        Assert.Null(RelaxTrial.ApplyMoves(new[] { new[] { A, B }, new[] { Rest, Rest } }, m, (_, _) => false));
        Assert.Null(RelaxTrial.ApplyMoves(bd, m, (i, j) => i == 1 && j == 1));
    }

    [Fact]
    public void R12_TooManyRelaxesIsNoWall() => Assert.Equal(RelaxTrial.NoWall, RelaxTrial.Discover(State(), Board(State()), 0, 0, maxRelaxes: 0));

    /// <summary>実データ（2026-10、氏名は伏せ字）: 職員10 の禁止の並びを 職員11 Cｵ＋職員10 Pｼ の組で解く（§13）。</summary>
    [Fact]
    public void R13_RealData_SetFoundAndConfirmReproducesTheTrial()
    {
        var st = StateJsonSerializer.Parse(FixtureLoader.ReadRaw("oct2026_grid_state.json"));
        var board = Board(st);
        var pS = st.Shifts.ToList().FindIndex(s => s.Kigou == "Pｼ");
        var cO = st.Shifts.ToList().FindIndex(s => s.Kigou == "Cｵ");
        var r = (RelaxTrial.Result)RelaxTrial.FirstWall(st, board);
        Assert.Equal(new[] { new RelaxTrial.Relax(9, pS, 1), new RelaxTrial.Relax(10, cO, 1) }, r.Relaxes);
        Assert.Equal(5, r.H0); Assert.Equal(4, r.Rr); Assert.Equal(1, r.Att);
        Assert.Equal(RelaxTrial.HandPlaced(st, board), r.Prerequisite);
        var ns = RelaxTrial.Apply(st, r.Prerequisite.Concat(r.Relaxes).ToList());
        var nb = RelaxTrial.ApplyMoves(board, r.Moves, (_, _) => false)!;
        Assert.Equal(r.Rr, UnifiedViolationChecker.Check(ns, nb).Hard);
        var issue = Assert.Single(V6SanityPort.Build(st, board).Guidance, g => g.Problem.Contains("上限 0 と食い違っています"));
        Assert.Equal("手で置いた勤務 4件 が上限 0 と食い違っています。もう一度つくると外されます", issue.Problem);
    }
}
