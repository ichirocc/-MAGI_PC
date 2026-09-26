using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [#41] 手動固定（Kotlin <c>ManualPinTest</c> の 1 対 1 移植。状態の 1 行は ViewModels 側の <c>ManualPinStatusTest</c>）。
/// 最適化器・後処理・1 手の提案・S5/S6 は固定セルを書き換えない。手の編集は可で値が追従する。採点は固定の有無で変わらない。
/// </summary>
public class ManualPinTest
{
    private static MagiState Sample() => StateJsonSerializer.Parse(FixtureLoader.ReadRaw("sample_state_v6.json"));

    private sealed record Fixture(MagiState St, int[][] Board, (int I, int J) Conflict);

    /// <summary>実データ（sample）に手動固定を置く: 各職員の 4 日おき＋希望と違う値で固定した未反映の希望セル 1 つ（手動＞希望）。</summary>
    private static Fixture Build()
    {
        var st0 = Sample();
        var p0 = new Problem(st0);
        var board = p0.InitialAssignment();
        var (ci, cj) = Enumerable.Range(0, p0.S).SelectMany(i => Enumerable.Range(0, p0.T).Select(j => (i, j)))
            .First(c => p0.WishFixed(c.i, c.j) && p0.AllowedShiftsForStaff(c.i).Any(k => k != p0.Wish[c.i][c.j]));
        board[ci][cj] = p0.AllowedShiftsForStaff(ci).First(k => k != p0.Wish[ci][cj]);
        var pins = new Dictionary<(int, int), ManualPin>();
        var order = new List<(int, int)>();
        void Put(int i, int j) { if (!pins.ContainsKey((i, j))) order.Add((i, j)); pins[(i, j)] = new ManualPin(i, j, board[i][j]); }
        for (var i = 0; i < p0.S; i++) for (var j = i % 4; j < p0.T; j += 4) Put(i, j);
        Put(ci, cj);
        var st = st0 with { Schedule = board.Select(r => (IReadOnlyList<int>)r.ToList()).ToList(), ManualPins = order.Select(k => pins[k]).ToList() };
        return new Fixture(st, board, (ci, cj));
    }

    private static void AssertPinsHeld(MagiState st, int[][] s)
    {
        foreach (var m in st.PinsOf()) Assert.Equal(m.Shift, s[m.Staff][m.Day]);
    }

    [Fact]
    public async Task OptimizeKeepsPinnedValuesAcrossFlagsAndAlgorithms()
    {
        var f = Build();
        var original = PolishGate.WishPinStrict;
        try
        {
            foreach (var strict in new[] { true, false })
                foreach (var algo in new[] { V6Algorithm.V5, V6Algorithm.Alns })
                {
                    PolishGate.WishPinStrict = strict;
                    var res = await V6FinalPort.HandleOptimize(f.St, secondsRaw: 2, schedule: f.Board.Copy2D(), workers: 1,
                        requestedAlgorithm: algo, allowImpossible: true, seed: 11L);
                    AssertPinsHeld(f.St, res.Schedule);
                    Assert.NotEqual(new Problem(f.St).Wish[f.Conflict.I][f.Conflict.J], res.Schedule[f.Conflict.I][f.Conflict.J]);
                }
        }
        finally { PolishGate.WishPinStrict = original; }
    }

    [Fact]
    public void DeterministicPostProcessingAndEntryRepairKeepPins()
    {
        var f = Build();
        var moved = f.Board.Copy2D();
        moved[f.Conflict.I][f.Conflict.J] = new Problem(f.St).Wish[f.Conflict.I][f.Conflict.J];
        var entry = V6NativeOptimizer.Hf67HardRepair(f.St, moved, new JavaRandom(7)).Schedule;
        AssertPinsHeld(f.St, entry);
        var post = V6HotfixPasses.RunPostOptimization(f.St, entry, "t", seed: 1L, deadlineMs: EngineClock.NowMs() + 3_600_000L,
            parameters: new V6HotfixPasses.PostOptimizationParams(Deterministic: true, C1LnsMaxEvaluations: 5_000, PersonalLnsMaxEvaluations: 5_000));
        AssertPinsHeld(f.St, post.Schedule);
    }

    [Fact]
    public void FixSuggesterNeverProposesAPinnedCellAndTheGateRejectsOne()
    {
        var f = Build();
        var p = new Problem(f.St);
        foreach (var s in FixSuggester.Suggest(f.St, f.Board, maxResults: 20, deadlineMs: 4000L))
            foreach (var op in s.Ops) Assert.False(p.Pinned(op.Staff, op.Day), $"{s.Label} が固定セル ({op.Staff},{op.Day}) を動かす");
        var m = f.St.PinsOf()[0];
        var other = p.AllowedShiftsForStaff(m.Staff).First(k => k != m.Shift);
        var outc = FixApplyGate.Apply(f.St, f.Board, new List<FixCell> { new(m.Staff, m.Day, other) });
        Assert.Null(outc.Schedule);
        Assert.Contains("手動固定", outc.Reason);
    }

    [Fact]
    public void RuleAPriorityManualPinBeatsReturnToWish()
    {
        var f = Build();
        var p = new Problem(f.St);
        var (ci, cj) = f.Conflict;
        var cur = f.Board[ci][cj];
        Assert.True(p.WishLocked(ci, cj));
        Assert.Equal(cur, p.LockTo(ci, cj));
        Assert.False(p.WishMoveAllowed(ci, cj, cur, p.Wish[ci][cj], strict: true));
        Assert.False(p.WishMoveAllowed(ci, cj, cur, p.Wish[ci][cj], strict: false));
        Assert.True(p.WishMoveAllowed(ci, cj, cur, cur, strict: false));
        var cand = f.Board.Copy2D();
        cand[ci][cj] = p.Wish[ci][cj];
        Assert.False(p.KeepsWishPins(f.Board, cand, strict: false));
        Assert.True(p.KeepsWishPins(f.Board, f.Board, strict: false));
    }

    [Fact]
    public void PersonSwapKickSkipsPinnedDaysEvenWithTheWishGateOff()
    {
        var f = Build();
        var p = new Problem(f.St);
        for (var seed = 1L; seed <= 4L; seed++)
        {
            var b = f.Board.Copy2D();
            V6NativeOptimizer.PersonSwapKick(p, b, new JavaRandom(seed), pairs: 3, wishPinStrict: false);
            AssertPinsHeld(f.St, b);
        }
    }

    [Fact]
    public void ScoringIsIdenticalWithAndWithoutPins()
    {
        var f = Build();
        var bare = f.St with { ManualPins = null };
        var a = UnifiedViolationChecker.Check(f.St, f.Board.Copy2D());
        var b = UnifiedViolationChecker.Check(bare, f.Board.Copy2D());
        Assert.Equal(b.Hard, a.Hard); Assert.Equal(b.Soft, a.Soft); Assert.Equal(b.Total, a.Total);
        Assert.Equal(b.WeightedScore, a.WeightedScore);
        Assert.Equal(b.Breakdown, a.Breakdown);
        Assert.True(a.Breakdown.GetValueOrDefault("pref") > 0);
        Assert.Equal(new Evaluator(new Problem(bare)).FullEval(f.Board), new Evaluator(new Problem(f.St)).FullEval(f.Board));
    }

    [Fact]
    public void ManualEditKeepsThePinAndUpdatesTheValue()
    {
        var st = Sample() with { ManualPins = new List<ManualPin> { new(0, 1, 0), new(2, 3, 1) } };
        var ns = st.WithPinsFollowing(new[] { (0, 1) }, 4);
        Assert.Equal(new ManualPin(0, 1, 4), ns.PinAt(0, 1));
        Assert.Equal(new ManualPin(2, 3, 1), ns.PinAt(2, 3));
        Assert.Same(st, st.WithPinsFollowing(new[] { (5, 5) }, 2));
    }

    [Fact]
    public void JsonRoundTripKeepsPinsAndAMissingKeyMeansNone()
    {
        var st = Sample() with { ManualPins = new List<ManualPin> { new(0, 1, 0), new(9, 30, 3) } };
        var back = StateJsonSerializer.Parse(StateJsonSerializer.Serialize(st, st.Schedule.Select(r => r.ToArray()).ToArray()));
        Assert.Equal(st.PinsOf(), back.PinsOf());
        Assert.Empty(Sample().PinsOf());
        Assert.DoesNotContain("manualPins", FixtureLoader.ReadRaw("sample_state_v6.json"));
    }

    [Fact]
    public void ToggleIsReversibleAndCarriesTheValueForUndo()
    {
        var st = Sample();
        var on = st.TogglePin(3, 4, 2);
        Assert.Equal(new ManualPin(3, 4, 2), on.PinAt(3, 4));
        Assert.Equal(5, on.WithPinsFollowing(new[] { (3, 4) }, 5).PinAt(3, 4)!.Shift);
        Assert.Equal(st.PinsOf(), on.TogglePin(3, 4, 2).PinsOf());
        Assert.Equal(new ManualPin(3, 4, 2), on.PinAt(3, 4));
    }

    [Fact]
    public void WishTrialExcludesPinnedCells()
    {
        var f = Build();
        var (ci, cj) = f.Conflict;
        Assert.DoesNotContain($"{ci},{cj}", WishTrial.LockedWishKeys(f.St));
        Assert.Contains($"{ci},{cj}", WishTrial.LockedWishKeys(f.St with { ManualPins = null }));
        Assert.Null(WishTrial.Trial(f.St, f.Board, ci, cj));
    }

    [Fact]
    public void SmartInitialPlacesPinsFirst()
    {
        var f = Build();
        AssertPinsHeld(f.St, V6FinalPort.HandleSmartInitial(f.St, allowImpossible: true).Schedule);
    }

    [Fact]
    public void StructureEditsRemapPins()
    {
        var st = Sample() with { ManualPins = new List<ManualPin> { new(0, 1, 0), new(2, 30, 3), new(4, 5, 1) } };
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var moved = Ws1Ops.MoveStaff(st, sched, 0, 1).State;
        Assert.NotNull(moved.PinAt(1, 1)); Assert.Null(moved.PinAt(0, 1));
        Assert.Equal(new[] { new ManualPin(0, 1, 0), new ManualPin(3, 5, 1) }, Ws1Ops.RemoveStaff(st, sched, 2).State.PinsOf());
        Assert.Equal(new[] { new ManualPin(0, 1, 0), new ManualPin(2, 30, 2) }, Ws1Ops.RemoveShift(st, sched, 1).State.PinsOf());
        Assert.Equal(2, Ws1Ops.ResizeDays(st, sched, 30).State.PinsOf().Count);
    }
}
