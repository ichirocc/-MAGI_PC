using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 2 gate: <c>Problem.cs</c> (the Kotlin <c>Problem.kt</c> port) row-for-row against
/// hand-derived expectations, plus the 4 real fixtures for dimension/bucket/InitialAssignment
/// sanity and a byte-exact regression on <c>blocked_covu_state.json</c>'s diagnostic output
/// (independently re-derived via a one-off <c>MagiEngine.GoldenGen</c> inspection run, not
/// assumed).
/// </summary>
public class ProblemTest
{
    private static MagiState LoadFixture(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    // Shared 3-shift vocabulary (休=rest/A/B) reused by the MakesForbiddenRun and
    // InitialAssignment regions below.
    private static readonly IReadOnlyList<Shift> ThreeShifts = new List<Shift>
    {
        new("休", "休", "", ""), // 0 = rest
        new("A", "A", "", ""),   // 1
        new("B", "B", "", ""),   // 2
    };

    // ---- Dow0: startDate -> weekday offset (%7, Sunday=0) ----------------

    [Theory]
    [InlineData("1970-01-01", 4)] // Thursday (Unix epoch)
    [InlineData("2000-01-01", 6)] // Saturday
    [InlineData("2000-01-02", 0)] // Sunday
    [InlineData("2024-01-01", 1)] // Monday
    public void Dow0_MatchesKnownCalendarAnchors(string startDate, int expectedDow0)
    {
        var p = new Problem(MinimalState.Build(startDate: startDate));
        Assert.Equal(expectedDow0, p.Dow0);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("")]
    [InlineData("2025-13-99")] // date-shaped but not a real calendar date (month=13, day=99)
    public void Dow0_FallsBackToZeroOnUnparseableStartDate(string startDate)
    {
        var p = new Problem(MinimalState.Build(startDate: startDate));
        Assert.Equal(0, p.Dow0);
    }

    // ---- Fixture-driven sanity (all 4 real fixtures) ----------------------

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void Fixture_DimensionsMatchState(string fixtureFile)
    {
        var state = LoadFixture(fixtureFile);
        var p = new Problem(state);

        Assert.Equal(state.StaffCount, p.S);
        Assert.Equal(state.DayCount, p.T);
        Assert.Equal(state.ShiftCount, p.K);
        Assert.Equal(state.GroupCount, p.G);
        Assert.Equal(state.SkillGroupCount, p.SkillG);
    }

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void Fixture_BucketMatchesRawGroupShift(string fixtureFile)
    {
        var state = LoadFixture(fixtureFile);
        var p = new Problem(state);

        for (int g = 0; g < p.G; g++)
        {
            var expectedAllowed = new HashSet<int>();
            if (g < state.GroupShift.Count)
            {
                var row = state.GroupShift[g];
                for (int k = 0; k < p.K && k < row.Count; k++)
                    if (row[k] == 1) expectedAllowed.Add(k);
            }
            Assert.Equal(expectedAllowed, new HashSet<int>(p.Bucket[g]));
        }
    }

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void Fixture_InitialAssignmentIsAlwaysInShiftRange(string fixtureFile)
    {
        var state = LoadFixture(fixtureFile);
        var p = new Problem(state);
        var ia = p.InitialAssignment();

        Assert.Equal(p.S, ia.Length);
        foreach (var row in ia)
        {
            Assert.Equal(p.T, row.Length);
            Assert.All(row, v => Assert.InRange(v, 0, p.K - 1));
        }
    }

    // ---- blocked_covu_state.json: exact C3UnknownShift regression --------
    //
    // Ground truth independently re-derived via a one-off MagiEngine.GoldenGen inspection run
    // over the real fixture (not assumed): the fixture references an undefined "Cｳ" shift
    // symbol in 2 cons3n rows and 1 cons3mn row.

    [Fact]
    public void BlockedCovU_C3UnknownShiftMatchesExactKnownList()
    {
        var p = new Problem(LoadFixture("blocked_covu_state.json"));

        Assert.Equal(
            new (string Family, string Text)[]
            {
                ("c3n", "Dﾃ〈Cｳ〉"),
                ("c3n", "〈Cｳ〉A4"),
                ("c3mn", "〈Cｳ〉Aｱ"),
            },
            p.C3UnknownShift);

        // The malformed rows are excluded from the resolved lists, not merely reported.
        Assert.Equal(13, p.Cons3n.Count);
        Assert.Equal(8, p.Cons3mn.Count);
    }

    // ---- CovUCell / CovOCell truth table ----------------------------------

    private static Problem BuildCovProblem(string need1, string need2, bool use2)
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", need1, need2) };
        return new Problem(MinimalState.Build(shifts: shifts, use2Patterns: use2));
    }

    [Theory]
    [InlineData(2, 1, 0)] // got=2 < need1(5) and < need2(3): min(3,1)=1 short, 0 over
    [InlineData(6, 0, 1)] // got=6 > both: 0 short, min(1,3)=1 over
    public void CovUOCell_BothDefinedUse2True(int got, int expectedCovU, int expectedCovO)
    {
        var p = BuildCovProblem(need1: "5", need2: "3", use2: true);
        Assert.Equal(expectedCovU, p.CovUCell(1, 0, got));
        Assert.Equal(expectedCovO, p.CovOCell(1, 0, got));
    }

    [Fact]
    public void CovUCell_Use2False_IgnoresNeed2EvenThoughItIsDefined()
    {
        var p = BuildCovProblem(need1: "5", need2: "3", use2: false);
        Assert.Equal(3, p.CovUCell(1, 0, got: 2)); // only need1(5) counted: 5-2=3
    }

    [Fact]
    public void CovUOCell_Need2AloneUse2True_IsStillEvaluated()
    {
        // "P2 alone" branch: need1 is unset ("") but need2 is defined and Use2Patterns=true.
        var p = BuildCovProblem(need1: "", need2: "5", use2: true);
        Assert.Equal(3, p.CovUCell(1, 0, got: 2)); // 5-2=3
        Assert.Equal(0, p.CovOCell(1, 0, got: 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void CovUOCell_BothUndefined_AlwaysZeroRegardlessOfGot(int got)
    {
        var p = BuildCovProblem(need1: "", need2: "", use2: true);
        Assert.Equal(0, p.CovUCell(1, 0, got));
        Assert.Equal(0, p.CovOCell(1, 0, got));
    }

    // ---- MakesForbiddenRun -------------------------------------------------

    private static readonly IReadOnlyList<C3Row> ABForbidden =
        new List<C3Row> { new(new List<string> { "A", "B" }) };
    private static readonly IReadOnlyList<C3Row> AABForbidden =
        new List<C3Row> { new(new List<string> { "A", "A", "B" }) };

    private static (Problem P, int[][] Schedule) BuildC3n(int[] row, IReadOnlyList<C3Row> cons3n)
    {
        var schedule = new int[][] { row };
        var state = MinimalState.Build(
            shifts: ThreeShifts,
            staffList: new List<Staff> { new("職員", 0) },
            schedule: schedule.Select(r => (IReadOnlyList<int>)r.ToList()).ToList(),
            cons3n: cons3n);
        return (new Problem(state), schedule);
    }

    [Fact]
    public void MakesForbiddenRun_CurrentPositionEndsTheWindow()
    {
        var (p, schedule) = BuildC3n(new[] { 0, 1, 0, 0, 0 }, ABForbidden);
        Assert.True(p.MakesForbiddenRun(schedule, 0, 2, newK: 2)); // day1=A already, day2 -> B
    }

    [Fact]
    public void MakesForbiddenRun_CurrentPositionStartsTheWindow()
    {
        var (p, schedule) = BuildC3n(new[] { 0, 0, 0, 2, 0 }, ABForbidden);
        Assert.True(p.MakesForbiddenRun(schedule, 0, 2, newK: 1)); // day2 -> A, day3=B already
    }

    [Fact]
    public void MakesForbiddenRun_StartOfScheduleBoundary()
    {
        var (p, schedule) = BuildC3n(new[] { 0, 2, 0, 0, 0 }, ABForbidden);
        Assert.True(p.MakesForbiddenRun(schedule, 0, 0, newK: 1)); // day0 -> A, day1=B already
    }

    [Fact]
    public void MakesForbiddenRun_EndOfScheduleBoundary()
    {
        var (p, schedule) = BuildC3n(new[] { 0, 0, 0, 1, 0 }, ABForbidden);
        Assert.True(p.MakesForbiddenRun(schedule, 0, 4, newK: 2)); // day3=A already, day4 -> B
    }

    [Fact]
    public void MakesForbiddenRun_LengthThreeRule()
    {
        var (p, schedule) = BuildC3n(new[] { 1, 1, 0, 0, 0 }, AABForbidden);
        Assert.True(p.MakesForbiddenRun(schedule, 0, 2, newK: 2)); // day0=A,day1=A already, day2 -> B
    }

    [Fact]
    public void MakesForbiddenRun_NoMatchReturnsFalse()
    {
        var (p, schedule) = BuildC3n(new[] { 0, 0, 0, 0, 0 }, ABForbidden);
        Assert.False(p.MakesForbiddenRun(schedule, 0, 2, newK: 1));
    }

    // ---- InitialAssignment: wish/bucket/fill-fallback interplay -----------
    //
    // G0 can do {休,A}; G1 can do {B} only (no 休); G2 can do nothing at all.

    private static readonly IReadOnlyList<Group> InitAssignGroups =
        new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2") };
    private static readonly IReadOnlyList<IReadOnlyList<int>> InitAssignGroupShift =
        new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 0 }, // G0: 休,A
            new List<int> { 0, 0, 1 }, // G1: B only
            new List<int> { 0, 0, 0 }, // G2: nothing
        };

    private static Problem BuildSingleStaff(int groupIdx, int scheduleValue, int? wish = null)
    {
        var wishes = wish is int w ? new Dictionary<string, int> { ["0,0"] = w } : new Dictionary<string, int>();
        var state = MinimalState.Build(
            shifts: ThreeShifts,
            groups: InitAssignGroups,
            groupShift: InitAssignGroupShift,
            staffList: new List<Staff> { new("職員", groupIdx) },
            schedule: new List<IReadOnlyList<int>> { new List<int> { scheduleValue } },
            wishes: wishes);
        return new Problem(state);
    }

    [Fact]
    public void InitialAssignment_InBucketRawValueWithNoWish_IsPreservedAsIs()
    {
        var p = BuildSingleStaff(groupIdx: 0, scheduleValue: 1); // A, in G0's bucket
        Assert.Equal(1, p.InitialAssignment()[0][0]);
    }

    [Fact]
    public void InitialAssignment_OutOfBucketRawValueWithNoWish_IsPreservedAsIsNotClamped()
    {
        // Counter-intuitive but deliberate (Web HF143): a pre-existing schedule value outside
        // the staff's canDo bucket is a groupViol the checker (phase 3) will flag -- but
        // Problem itself does not "fix" it by clamping into the bucket. G0's bucket is
        // {休,A}; B (index 2) is not in it.
        var p = BuildSingleStaff(groupIdx: 0, scheduleValue: 2);
        Assert.Equal(2, p.InitialAssignment()[0][0]);
    }

    [Fact]
    public void InitialAssignment_WishInBucket_OverridesRawValue()
    {
        var p = BuildSingleStaff(groupIdx: 0, scheduleValue: 0, wish: 1); // raw=休, wish=A (in bucket)
        Assert.Equal(1, p.InitialAssignment()[0][0]);
    }

    [Fact]
    public void InitialAssignment_WishNotInBucket_IsIgnored()
    {
        var p = BuildSingleStaff(groupIdx: 0, scheduleValue: 1, wish: 2); // raw=A, wish=B (NOT in bucket)
        Assert.Equal(1, p.InitialAssignment()[0][0]); // wish ignored, raw value kept
    }

    [Fact]
    public void InitialAssignment_MissingCellFallsBackToBucketFirstWhenRestNotInBucket()
    {
        // G1's bucket is {B} only -- rest (休) isn't in it, so the fill falls back to the
        // bucket's first entry rather than to rest itself.
        var p = BuildSingleStaff(groupIdx: 1, scheduleValue: -1);
        Assert.Equal(2, p.InitialAssignment()[0][0]); // B (bucket[0])
    }

    [Fact]
    public void InitialAssignment_MissingCellFallsBackToRestWhenBucketIsEmpty()
    {
        // G2's bucket is empty -- FillShiftIndex falls back to rest anyway ("may still be
        // unfillable": this staff genuinely cannot take any shift, a groupViol either way).
        var p = BuildSingleStaff(groupIdx: 2, scheduleValue: -1);
        Assert.Equal(0, p.InitialAssignment()[0][0]); // 休 (rest), even though G2 can't do it
    }

    [Fact]
    public void InitialAssignment_MissingCellFillsWithRestWhenBucketContainsIt()
    {
        var p = BuildSingleStaff(groupIdx: 0, scheduleValue: -1); // G0's bucket contains 休
        Assert.Equal(0, p.InitialAssignment()[0][0]);
    }

    // ---- UnresolvedRows: per-family malformed-row diagnostics --------------

    [Fact]
    public void UnresolvedRows_Cons1UnknownShift_IsRecordedAndExcluded()
    {
        var p = new Problem(MinimalState.Build(cons1: new List<C1Row> { new("5", "NOPE", "2") }));
        Assert.Empty(p.Cons1);
        Assert.Contains(("窓の要件", "〈NOPE〉 を5日で2回以上"), p.UnresolvedRows);
    }

    [Fact]
    public void UnresolvedRows_Cons2UnknownShift_IsRecordedAndExcluded()
    {
        var p = new Problem(MinimalState.Build(cons2: new List<C2Row> { new("NOPE", "3") }));
        Assert.Empty(p.Cons2);
        Assert.Contains(("個人の合計", "〈NOPE〉 を3回以上"), p.UnresolvedRows);
    }

    [Fact]
    public void UnresolvedRows_Cons41UnknownGroup_IsRecordedAndExcluded()
    {
        var p = new Problem(MinimalState.Build(cons41: new List<C41Row> { new("NOPE", "A", "1", "5") }));
        Assert.Empty(p.Cons41);
        Assert.Contains(("群のレンジ", "〈NOPE〉 の A（1〜5）"), p.UnresolvedRows);
    }

    [Fact]
    public void UnresolvedRows_Cons41BothBoundsBlank_IsRecordedAndExcluded()
    {
        // Group and shift both resolve fine; the row is still unresolved because it carries
        // no lower AND no upper bound at all (hasLo=false, hasHi=false).
        var p = new Problem(MinimalState.Build(cons41: new List<C41Row> { new("G0", "A", "", "") }));
        Assert.Empty(p.Cons41);
        Assert.Contains(("群のレンジ", "G0 の A（〜）"), p.UnresolvedRows);
    }

    [Fact]
    public void UnresolvedRows_Cons42UnknownGroup_IsRecordedAndExcluded()
    {
        var p = new Problem(MinimalState.Build(cons42: new List<C42Row> { new("NOPE", "G0", "A", "A") }));
        Assert.Empty(p.Cons42);
        Assert.Contains(("群ペア禁止", "〈NOPE〉/A × G0/A"), p.UnresolvedRows);
    }

    [Fact]
    public void UnresolvedRows_Cons41sUnknownSkillGroup_IsRecordedAndExcluded()
    {
        var p = new Problem(MinimalState.Build(
            skillGroups: new List<Group> { new("S0", "S0") },
            cons41s: new List<C41Row> { new("NOPE", "A", "1", "2") }));
        Assert.Empty(p.Cons41s);
        Assert.Contains(("スキル群のレンジ", "〈NOPE〉 の A（1〜2）"), p.UnresolvedRows);
    }

    [Fact]
    public void UnresolvedRows_Cons42sUnknownSkillGroup_IsRecordedAndExcluded()
    {
        var p = new Problem(MinimalState.Build(
            skillGroups: new List<Group> { new("S0", "S0") },
            cons42s: new List<C42Row> { new("NOPE", "S0", "A", "A") }));
        Assert.Empty(p.Cons42s);
        Assert.Contains(("スキル群ペア禁止", "〈NOPE〉/A × S0/A"), p.UnresolvedRows);
    }

    // ---- NeedAt fallthrough behavior ---------------------------------------

    private static Problem BuildNeedProblem()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),   // 0: both Need1/Need2 blank
            new("A", "A", "7", "9"),  // 1: Need1 default "7", Need2 default "9"
        };
        var needDay1 = new Dictionary<string, string>
        {
            ["1,0"] = "",     // present but blank -> falls through to default
            ["1,1"] = "3",    // valid override, differs from default -> proves override wins
            ["1,2"] = "abc",  // present, non-blank, unparseable -> falls through to default
            ["1,3"] = "0",    // valid override that happens to be zero -> must not be confused with "unset"
        };
        var needDay2 = new Dictionary<string, string> { ["1,0"] = "4" };
        var schedule = new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(0, 5).ToList(),
            Enumerable.Repeat(0, 5).ToList(),
        };
        return new Problem(MinimalState.Build(shifts: shifts, needDay1: needDay1, needDay2: needDay2, schedule: schedule));
    }

    [Fact]
    public void NeedAt_BlankOverrideFallsThroughToShiftDefault()
    {
        Assert.Equal(7, BuildNeedProblem().Need1[1][0]);
    }

    [Fact]
    public void NeedAt_ValidOverrideWins()
    {
        Assert.Equal(3, BuildNeedProblem().Need1[1][1]);
    }

    [Fact]
    public void NeedAt_UnparseableOverrideFallsThroughToShiftDefaultNotNegativeOne()
    {
        Assert.Equal(7, BuildNeedProblem().Need1[1][2]);
    }

    [Fact]
    public void NeedAt_ZeroOverrideIsRespectedNotConfusedWithUnset()
    {
        Assert.Equal(0, BuildNeedProblem().Need1[1][3]);
    }

    [Fact]
    public void NeedAt_AbsentOverrideAndBlankShiftDefault_YieldsNegativeOne()
    {
        Assert.Equal(-1, BuildNeedProblem().Need1[0][4]); // shift 0 (休): no override key exists at all
    }

    [Fact]
    public void NeedAt_Need2UsesItsOwnMapAndFieldIndependentlyFromNeed1()
    {
        var p = BuildNeedProblem();
        Assert.Equal(4, p.Need2[1][0]); // needDay2 override
        Assert.Equal(9, p.Need2[1][1]); // no override -> shift default Need2="9" (not Need1's "7")
    }

    // ---- Apt: group-target clamping to the staff's own [RangeLo,RangeHi] --

    private static readonly IReadOnlyList<Shift> TwoShiftsRestA =
        new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") };

    private static Problem BuildAptProblem()
    {
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1 }, // G0: 休,A
            new List<int> { 1, 0 }, // G1: 休 only (no A)
        };
        var groupShiftApt = new List<IReadOnlyList<string>>
        {
            new List<string> { "-5", "10" }, // G0: 休 target -5 (negative -> skipped), A target 10
            new List<string> { "", "20" },   // G1: 休 unset, A target 20 (but G1 can't do A)
        };
        var staffList = new List<Staff>
        {
            new("s0", 0), // G0, RangeLo/Hi for A = [5,15] -> target 10 within range
            new("s1", 0), // G0, RangeHi for A = 6        -> clamp target down to 6
            new("s2", 0), // G0, RangeLo for A = 12       -> clamp target up to 12
            new("s3", 1), // G1                            -> can't do A at all
        };
        var staffRange = new Dictionary<string, Range>
        {
            ["0,1"] = new Range("5", "15"),
            ["1,1"] = new Range("", "6"),
            ["2,1"] = new Range("12", ""),
        };
        var schedule = Enumerable.Range(0, 4).Select(_ => (IReadOnlyList<int>)new List<int> { 0 }).ToList();
        var state = MinimalState.Build(
            shifts: TwoShiftsRestA, groups: groups, groupShift: groupShift, groupShiftApt: groupShiftApt,
            staffList: staffList, staffRange: staffRange, schedule: schedule);
        return new Problem(state);
    }

    [Fact]
    public void Apt_TargetWithinRange_PassesThroughUnclamped()
    {
        Assert.Equal(10, BuildAptProblem().Apt[0][1]); // s0, shift A
    }

    [Fact]
    public void Apt_TargetAboveRangeHi_ClampsDown()
    {
        Assert.Equal(6, BuildAptProblem().Apt[1][1]); // s1, shift A, RangeHi=6
    }

    [Fact]
    public void Apt_TargetBelowRangeLo_ClampsUp()
    {
        Assert.Equal(12, BuildAptProblem().Apt[2][1]); // s2, shift A, RangeLo=12
    }

    [Fact]
    public void Apt_ShiftNotInBucket_StaysUnset()
    {
        // s3 is in G1, which cannot do A at all, despite G1's own target=20 for A.
        Assert.Equal(-1, BuildAptProblem().Apt[3][1]);
    }

    [Fact]
    public void Apt_NegativeGroupTarget_IsSkipped()
    {
        Assert.Equal(-1, BuildAptProblem().Apt[0][0]); // s0, shift 休, G0's target is "-5"
    }

    // ---- OutOfRangeGroupStaff ----------------------------------------------

    [Fact]
    public void OutOfRangeGroupStaff_ClampsToZeroAndRecordsOnlyOutOfRangeIndices()
    {
        var staffList = new List<Staff> { new("Normal", 0), new("TooHigh", 99), new("Negative", -1) };
        var state = MinimalState.Build(
            staffList: staffList,
            schedule: Enumerable.Range(0, 3).Select(_ => (IReadOnlyList<int>)new List<int> { 0 }).ToList());
        var p = new Problem(state);

        Assert.Equal(new[] { 0, 0, 0 }, p.Sgrp); // all clamped to the only defined group
        Assert.Equal(new[] { 1, 2 }, p.OutOfRangeGroupStaff); // in encounter order
    }
}
