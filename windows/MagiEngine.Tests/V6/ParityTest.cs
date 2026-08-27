using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 3 gate (「最重要」per the migration plan): the "parity triangle" —
/// <see cref="UnifiedViolationChecker"/> (full recompute, the source of truth for correctness),
/// <see cref="Evaluator"/> (full recompute, packed lexicographic score used by SA/ALNS scoring),
/// and <see cref="DeltaEvaluator"/> (incremental, used inside the hot search loop) — must agree
/// on every one of the 19 violation families, on every fixture, at every point along a sequence
/// of moves. A total-score match alone is not enough: several families share the same weight
/// (c1 and c3mn both = 30; c2/c41/c42/c41s/c42s/apt/fair/weekly all = 1), so a +1/-1 error split
/// across two same-weight families would cancel out and be invisible in the aggregate — this is
/// exactly why <see cref="DeltaEvaluator.FamilyRaw"/> exists (checked here against
/// <see cref="ViolationReport.Breakdown"/> family-by-family).
///
/// Excluded from every assertion (by design, not oversight): <see cref="ViolationReport.Logs"/>,
/// whose message embeds a wall-clock elapsed-ms figure and is therefore non-deterministic
/// run-to-run.
/// </summary>
public class ParityTest
{
    private static MagiState LoadFixture(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    // ---- shared assertion: report vs Evaluator vs DeltaEvaluator, at the CURRENT board -------

    /// <summary>
    /// Cross-checks all three evaluators against each other for the board <paramref name="report"/>
    /// was computed from. <paramref name="p"/> and <paramref name="ev"/> must already reflect that
    /// same <see cref="Problem"/>; <paramref name="de"/> must already have been <c>Reset</c>/<c>Apply</c>'d
    /// to that exact board.
    /// </summary>
    private static void AssertThreeWayParity(ViolationReport report, Problem p, Evaluator ev, DeltaEvaluator de, int[][] board)
    {
        var parts = ev.FullEvalParts(board);
        long hard1 = parts[0], soft = parts[1];

        // ---- checker <-> Evaluator (aggregate level: Evaluator has no per-family breakdown) ----
        Assert.Equal(report.Hard, (int)hard1);

        double hardWeighted = 0.0;
        foreach (var key in MirrorKeys.Hard)
            hardWeighted += (report.Breakdown.TryGetValue(key, out var v) ? v : 0) * MirrorKeys.WeightOf(key);
        // report.WeightedScore は全19族（HARDも含む）の重み付き和。Evaluator の soft は SOFT族のみの
        // 重み付き和なので、「WeightedScore - HARD族の重み付き寄与」と一致するはず。
        Assert.Equal(report.WeightedScore, hardWeighted + soft, precision: 6);

        long expectedPacked = hard1 * Evaluator.SCORE_HARD_UNIT + soft;
        Assert.Equal(expectedPacked, ev.FullEval(board));

        // ---- checker <-> DeltaEvaluator (per-family: this is the check that actually matters) ---
        Assert.Equal(expectedPacked, de.Score());

        var familyRaw = de.FamilyRaw();
        Assert.Equal(17, familyRaw.Count); // 19 families - {low, high} (covered by RangeRaw below)
        foreach (var (key, value) in familyRaw)
        {
            int expected = report.Breakdown.TryGetValue(key, out var bv) ? bv : 0;
            Assert.True(expected == value, $"family '{key}': checker={expected} delta={value}");
        }

        var (lowRaw, highRaw) = de.RangeRaw();
        int lowExpected = report.Breakdown.TryGetValue("low", out var lv) ? lv : 0;
        int highExpected = report.Breakdown.TryGetValue("high", out var hv) ? hv : 0;
        Assert.Equal(lowExpected, lowRaw);
        Assert.Equal(highExpected, highRaw);
        Assert.Equal(lowRaw * 90L + highRaw * 45L, de.RangeWeighted());
        Assert.Equal(lowExpected * 90L + highExpected * 45L, de.RangeWeighted());

        // every one of the 19 families is accounted for exactly once across the two checks above
        Assert.Equal(19, familyRaw.Count + 2);
    }

    // ---- real fixtures, as loaded (exercises Rebuild(), not PreviewMove/Commit) --------------

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void Fixture_AsLoaded_AllThreeEvaluatorsAgree(string fixtureFile)
    {
        var state = LoadFixture(fixtureFile);
        var p = new Problem(state);
        var board = ScheduleUtil.NormalizeSchedule(state.Schedule.ToIntArray2D(), p);
        var report = UnifiedViolationChecker.Check(state, board);
        var ev = new Evaluator(p);
        var de = new DeltaEvaluator(p);
        de.Reset(board);

        AssertThreeWayParity(report, p, ev, de, board);
    }

    // ---- real fixtures, randomized single-cell moves (exercises PreviewMove/Commit) ----------

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void Fixture_RandomizedMoves_AllThreeEvaluatorsAgreeAtEveryStep(string fixtureFile)
    {
        var state = LoadFixture(fixtureFile); // kept as ONE reference throughout: Check(state, board)
                                                // reuses ScheduleUtil.CachedProblem's single-entry
                                                // memo instead of rebuilding Problem every move.
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var de = new DeltaEvaluator(p);
        de.Reset(ScheduleUtil.NormalizeSchedule(state.Schedule.ToIntArray2D(), p));

        // Fixed seed: deterministic, reproducible failures. nw is drawn uniformly over [0,K) with
        // NO canDo filtering — this deliberately exercises groupViol (assigning a shift the staff
        // can't take), matching the Kotlin original's own 20,000-move differential test.
        var rng = new Random(unchecked((int)0x4D414749) ^ fixtureFile.GetHashCode());

        for (int step = 0; step < 250; step++)
        {
            int i = rng.Next(p.S);
            int j = rng.Next(p.T);
            int nw = rng.Next(p.K);

            de.Apply(i, j, nw);
            var board = de.Snapshot();
            var report = UnifiedViolationChecker.Check(state, board);

            AssertThreeWayParity(report, p, ev, de, board);
        }
    }

    // ---- synthetic fixture exercising all 19 families at once ---------------------------------

    /// <summary>
    /// Deliberately constructed so every one of the 19 families can fire: 2 unit groups (G0 can't
    /// do "B", G1 can do everything — exercises groupViol) crossed with 2 SKILL groups that split
    /// the same 4 staff differently (Sk0/Sk1, independent of G0/G1 — exercises c41s/c42s on a
    /// genuinely different partition than c41/c42, not a coincidentally-identical one). One
    /// constraint of every family, including both branches of the c3-family dual-mode evaluation
    /// (HF507 run-deficit for the single-shift "want" pattern vs. window-match for everything
    /// else), a same-group/same-shift c42 row (exercises <see cref="Evaluator.C42PairCount"/>'s
    /// C(n,2) branch), Use2Patterns=true with distinct Need1/Need2 (exercises the covU/covO
    /// OR/AND logic), a staffRange (low/high), a wish (pref), and per-group apt targets.
    /// </summary>
    private static MagiState BuildAllFamiliesState()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),  // 0 = rest
            new("A", "A", "1", "2"),  // 1 = daily need1=1 / need2=2 (P2/OR upper)
            new("B", "B", "", ""),    // 2
        };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 0 }, // G0: 休,A only (cannot take B -> groupViol exercisable)
            new List<int> { 1, 1, 1 }, // G1: 休,A,B
        };
        var groupShiftApt = new List<IReadOnlyList<string>>
        {
            new List<string> { "", "1", "" },  // G0 targets 1x A
            new List<string> { "", "2", "1" }, // G1 targets 2x A, 1x B
        };
        var staffList = new List<Staff>
        {
            new("職員0", 0, 0), // G0, Sk0
            new("職員1", 0, 1), // G0, Sk1 (cannot do B, same as all of G0)
            new("職員2", 1, 0), // G1, Sk0
            new("職員3", 1, 1), // G1, Sk1
        };
        var skillGroups = new List<Group> { new("Sk0", "Sk0"), new("Sk1", "Sk1") };

        const int t = 14;
        var schedule = Enumerable.Range(0, 4)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, t).ToList())
            .ToList();

        return new MagiState(
            StartDate: "2025-12-01",
            EndDate: "2025-12-14",
            Shifts: shifts,
            Groups: groups,
            StaffList: staffList,
            Use2Patterns: true,
            GroupShift: groupShift,
            GroupShiftApt: groupShiftApt,
            Schedule: schedule,
            Wishes: new Dictionary<string, int> { ["0,2"] = 1 }, // staff0 wants "A" on day2 (realizable: G0 can do A)
            StaffRange: new Dictionary<string, Range> { ["0,1"] = new Range("2", "4") }, // staff0 x shift A in [2,4]
            NeedDay1: new Dictionary<string, string>(),
            NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row> { new("4", "A", "1") }, // every 4-day window needs >=1 "A"
            Cons2: new List<C2Row> { new("A", "3") },      // every staff needs >=3 "A" total
            Cons3: new List<C3Row> { new(new List<string> { "A", "A", "A" }) },   // want: run of 3 A's (HF507 run-deficit path)
            Cons3n: new List<C3Row> { new(new List<string> { "A", "B" }) },       // forbidden: A directly followed by B
            Cons3m: new List<C3Row> { new(new List<string> { "休", "A" }) },      // want: rest then A (multi-shift window-match path)
            Cons3mn: new List<C3Row> { new(new List<string> { "休", "休" }) },    // avoid: two consecutive rest days
            Cons41: new List<C41Row> { new("G0", "A", "0", "1") },               // G0's daily A-count in [0,1]
            Cons42: new List<C42Row>
            {
                new("G0", "G1", "A", "B"),  // cross-group pair
                new("G0", "G0", "A", "A"),  // same group/shift -> C(n,2) branch of C42PairCount
            },
            SkillGroups: skillGroups,
            Cons41s: new List<C41Row> { new("Sk0", "A", "0", "1") },
            Cons42s: new List<C42Row> { new("Sk0", "Sk1", "A", "B") },
            ShiftColors: new Dictionary<string, string>(),
            Extras: MinimalState.NoExtras
        );
    }

    [Fact]
    public void SyntheticFixture_AllNineteenFamiliesFireAtLeastOnceAcrossTheRun_AndParityHolds()
    {
        var state = BuildAllFamiliesState();
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var de = new DeltaEvaluator(p);
        de.Reset(ScheduleUtil.NormalizeSchedule(state.Schedule.ToIntArray2D(), p));

        var everFired = new HashSet<string>();
        void RecordFired(ViolationReport r)
        {
            foreach (var (key, v) in r.Breakdown) if (v > 0) everFired.Add(key);
        }

        // t=0 (Rebuild path): several families are designed to fire on the initial all-rest board
        // (c1, c2, covU, apt) before any move is made.
        var board0 = de.Snapshot();
        var report0 = UnifiedViolationChecker.Check(state, board0);
        AssertThreeWayParity(report0, p, ev, de, board0);
        RecordFired(report0);

        var rng = new Random(0x415A);
        for (int step = 0; step < 400; step++)
        {
            int i = rng.Next(p.S);
            int j = rng.Next(p.T);
            int nw = rng.Next(p.K);

            de.Apply(i, j, nw);
            var board = de.Snapshot();
            var report = UnifiedViolationChecker.Check(state, board);

            AssertThreeWayParity(report, p, ev, de, board);
            RecordFired(report);
        }

        var missing = MirrorKeys.All.Where(k => !everFired.Contains(k)).ToList();
        Assert.True(missing.Count == 0, $"families that never fired across the whole run: {string.Join(", ", missing)}");
    }
}
