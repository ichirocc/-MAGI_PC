using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース2] Direct tests for the schedule-independent structural-diagnostic slice
/// ported in <c>V6SanityPort.Core.cs</c>. Ported from the subset of the source project's
/// <c>V6SanityPortTest.kt</c> (~60 tests total) that exercises these functions BY NAME with no
/// dependency on not-yet-ported types (<c>SettingIssue</c>/<c>buildGuidance</c>/
/// <c>buildViolationDebug</c>/<c>V6SanityReport</c>, all later phase-7 pieces). Two of the ported
/// <c>AptBalances</c> tests drop their trailing Kotlin-side <c>buildGuidance</c> cross-check
/// assertion (deferred to piece 14) — noted inline. <c>NeedDefined</c>/<c>EffectiveDemand</c>/
/// <c>EffectiveCap</c> have zero direct Kotlin-side test coverage either (only exercised
/// transitively through <c>AptBalances</c> here, and later through <c>buildViolationDebug</c>/
/// <c>buildGuidance</c>) — no speculative direct coverage is invented for them.
///
/// <see cref="V6SanityPort.SafeDayLabel"/> likewise has zero direct Kotlin-side test coverage (it
/// is <c>private</c> in the source, only exercised transitively via <c>buildViolationDebug</c>/
/// <c>buildGuidance</c>, both later pieces) — its coverage here is newly C#-authored, built from
/// the two confirmed divergences from <see cref="ScheduleUtil.FormatDay"/> documented on its own
/// doc comment. Every literal test case below was independently re-verified against a real
/// Kotlin runtime immediately before this file was written (not merely assumed from an earlier
/// investigation), including the trailing-content, short-year, and out-of-range-field cases.
/// </summary>
public class V6SanityPortTest
{
    private static MagiState LoadFixture(string name) =>
        StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    // ---- StructuralHardFloor (real fixture) -----------------------------------------------

    [Fact]
    public void StructuralHardFloor_IsZeroForTheBlockedCovUFixture()
    {
        // Mirrors (only) the StructuralHardFloor half of the Kotlin source's
        // BlockedCovUFixtureTest.fixtureHasTheBlockedNowCovUShape — the diagnoseCoverage half of
        // that test belongs to V6PortAnalyzer.Coverage.cs (piece 3) and is not ported here. This
        // fixture's covU=4 is a "blocked-now" shortfall (unmet by the CURRENT wishes/board), not
        // a structural one (too few qualified staff overall) — so the structural floor is
        // correctly 0 even though covU itself is not. Exercises StructuralHardFloor's own
        // default `Problem(state)` parameter against real, non-synthetic data.
        var state = LoadFixture("blocked_covu_state.json");
        Assert.Equal(0, V6SanityPort.StructuralHardFloor(state));
    }

    // ---- StructuralPersonalFloor / OtherShiftCapSum ----------------------------------------

    private static MagiState PersonalFloorState(IReadOnlyDictionary<string, Range> staffRange) => new(
        StartDate: "2025-01-01", EndDate: "2025-01-31",
        Shifts: new List<Shift> { new("休", "休", "", ""), new("B4", "B4", "", ""), new("有", "有", "", "") },
        Groups: new List<Group> { new("G0", "G0") },
        StaffList: new List<Staff> { new("s0", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "1", "" } },
        Schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 31).ToList() },
        Wishes: new Dictionary<string, int>(),
        StaffRange: staffRange,
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    [Fact]
    public void StructuralPersonalFloor_MatchesTheForcedRepertoireMinimum()
    {
        var st = PersonalFloorState(new Dictionary<string, Range>
        {
            ["0,0"] = new Range("10", "10"),
            ["0,2"] = new Range("1", "1"),
        });
        var p = new Problem(st);
        Assert.Equal(11, V6SanityPort.OtherShiftCapSum(p, 0, 1)); // 休10 + 有1
        Assert.Equal(19, V6SanityPort.StructuralPersonalFloor(p)); // (31-11) - 目標1
    }

    [Fact]
    public void StructuralPersonalFloor_IsZeroWhenAnotherShiftIsUncapped()
    {
        // 有 は上限未設定 (StaffRange has no "0,2" entry at all)
        var st = PersonalFloorState(new Dictionary<string, Range> { ["0,0"] = new Range("10", "10") });
        Assert.Equal(0, V6SanityPort.StructuralPersonalFloor(new Problem(st)));
    }

    // ---- AptBalances ------------------------------------------------------------------------

    private static MagiState AptVsNeedState(int days, string need1, string aptTarget) => new(
        StartDate: "2026-08-01", EndDate: $"2026-08-{days:D2}",
        Shifts: new List<Shift> { new("休", "休", need1, ""), new("X", "X", need1, "") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { aptTarget, aptTarget } },
        Schedule: new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(0, days).ToList(),
            Enumerable.Repeat(0, days).ToList(),
        },
        Wishes: new Dictionary<string, int>(),
        StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    [Fact]
    public void AptBalances_MatchesTheSettingIssueAndReportsShortfall()
    {
        var st = AptVsNeedState(days: 10, need1: "1", aptTarget: "6");
        var x = V6SanityPort.AptBalances(st).Single(b => b.Kigou == "X");
        Assert.Equal(12, x.AptSum); // 目標の合計は担当2名ぶん
        Assert.Equal(10, x.Capacity); // 受け止められる上限は必要人数の合計
        Assert.True(x.Overloaded); // 超過していること
        Assert.Equal(2, x.Shortfall); // 何回ぶん届かないか
        Assert.False(x.IsRest); // 非休シフトは isRest=false
        // [DEFER TO PIECE 14 — buildGuidance not yet ported] the Kotlin original also asserts
        // that V6SanityPort.buildGuidance(st) reports a matching SettingIssue whose text embeds
        // these same 12/10 figures.
    }

    [Fact]
    public void AptBalances_ReportsNoOverloadWhenTargetsFitTheDemand()
    {
        var st = AptVsNeedState(days: 10, need1: "1", aptTarget: "5");
        var x = V6SanityPort.AptBalances(st).Single(b => b.Kigou == "X");
        Assert.Equal(10, x.AptSum);
        Assert.Equal(10, x.Capacity);
        Assert.False(x.Overloaded); // ちょうど収まるなら超過ではない
        // [DEFER TO PIECE 14] the Kotlin original also asserts buildGuidance emits no matching issue.
    }

    [Fact]
    public void AptBalances_SkipsShiftsWithoutAnyTarget()
    {
        var st = AptVsNeedState(days: 10, need1: "1", aptTarget: "");
        Assert.Empty(V6SanityPort.AptBalances(st)); // 目標なしなら行そのものを出さない
    }

    // ---- RangeOrderConflict ------------------------------------------------------------------

    [Theory]
    [InlineData("3", "1", 3, 1)]
    [InlineData(" 1 ", " 0 ", 1, 0)]
    public void RangeOrderConflict_FlagsARealConflict(string lo, string hi, int expectedLo, int expectedHi)
    {
        var conflict = V6SanityPort.RangeOrderConflict(lo, hi);
        Assert.NotNull(conflict);
        Assert.Equal((expectedLo, expectedHi), conflict!.Value);
    }

    [Theory]
    [InlineData("1", "3")]
    [InlineData("2", "2")]
    [InlineData("", "1")]
    [InlineData("3", "")]
    [InlineData(null, null)]
    [InlineData("あ", "1")]
    public void RangeOrderConflict_DoesNotFlagNonConflicts(string? lo, string? hi)
    {
        Assert.Null(V6SanityPort.RangeOrderConflict(lo, hi));
    }

    // ---- SafeDayLabel (newly C#-authored — see V6SanityPort.Core.cs's class doc comment) ------
    // Every literal below was re-verified against a real Kotlin runtime just before this file
    // was written (see the class-level doc comment).

    [Theory]
    [InlineData("2026-06-01", 0, "6/1(月)")]
    [InlineData("2026-06-01", 1, "6/2(火)")]
    [InlineData("2026-06-06", 0, "6/6(土)")]
    [InlineData("2026-06-07", 0, "6/7(日)")]
    [InlineData("2026-06-01", 30, "7/1(水)")] // crosses the June/July boundary
    public void SafeDayLabel_ComputesDateAndMondayFirstWeekday(string startDate, int offset, string expected)
    {
        Assert.Equal(expected, V6SanityPort.SafeDayLabel(startDate, offset));
    }

    [Fact]
    public void SafeDayLabel_NegativeOffsetFallsBackWithoutEverParsing()
    {
        // offset=-1 -> "0日", NOT clamped to a minimum of "1日" — confirmed against real Kotlin:
        // `require(offset >= 0)` throws before java.time.LocalDate.parse is ever reached, so this
        // exact arithmetic (offset + 1) applies even given a well-formed startDate.
        Assert.Equal("0日", V6SanityPort.SafeDayLabel("2026-06-01", -1));
    }

    [Fact]
    public void SafeDayLabel_FallbackReflectsTheRequestedOffset()
    {
        Assert.Equal("6日", V6SanityPort.SafeDayLabel("garbage", 5));
    }

    // Divergence dimension (see V6SanityPort.Core.cs's class doc comment, point 3): unlike
    // FormatDay, this parse is STRICT — confirmed against real Kotlin's java.time.LocalDate.parse.
    [Theory]
    [InlineData("2026-6-01")]           // unpadded month: FormatDay accepts, SafeDayLabel does not
    [InlineData("2026-06-1")]           // unpadded day
    [InlineData("2026-06-01 ")]         // trailing whitespace
    [InlineData(" 2026-06-01")]         // leading whitespace
    [InlineData("2026-06-01T00:00:00")] // trailing content: FormatDay partial-matches, this does not
    [InlineData("26-06-01")]            // short year
    public void SafeDayLabel_RejectsInputsThatFormatDayWouldLenientlyAccept(string startDate)
    {
        Assert.Equal("1日", V6SanityPort.SafeDayLabel(startDate, 0));
    }

    // Same divergence dimension continued: out-of-range fields do NOT roll over via
    // calendar-carry arithmetic here (unlike FormatDay) — they simply fail to parse.
    [Theory]
    [InlineData("2026-13-05")] // month overflow: FormatDay carries into January of next year
    [InlineData("2026-02-30")] // day overflow: FormatDay carries into March
    [InlineData("2026-00-01")] // month underflow
    [InlineData("2026-06-00")] // day underflow
    public void SafeDayLabel_DoesNotRollOverOutOfRangeFields(string startDate)
    {
        Assert.Equal("1日", V6SanityPort.SafeDayLabel(startDate, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("2026/06/01")] // '-' separators are literal, not lenient
    public void SafeDayLabel_FallsBackToOffsetPlusOneWhenUnparseable(string startDate)
    {
        Assert.Equal("1日", V6SanityPort.SafeDayLabel(startDate, 0));
    }
}
