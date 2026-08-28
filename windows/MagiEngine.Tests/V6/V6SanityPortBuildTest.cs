using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース16] Direct test for <see cref="V6SanityPort.Build"/>, ported from the source
/// project's <c>V6SanityPortTest.kt</c> (~60 tests total). This is the sole test in that suite
/// that exercises <c>build()</c>'s own aggregation (as opposed to the individual diagnostics it
/// assembles, each of which already has dedicated coverage in the piece it landed with:
/// <c>V6SanityPortTest.cs</c>=piece 2, <c>V6SanityPortViolationDebugTest.cs</c>=piece 12,
/// <c>ConstraintMusTest.cs</c>=piece 13, and the ~25 <c>buildGuidance</c>-focused tests in
/// <c>V6SanityPortTest.kt</c> that this migration's piece-14/15 test coverage already represents
/// a subset of). This single test exercises <c>V6SanityPort.InvalidAssignmentCells</c>
/// (private, no direct Kotlin-side coverage of its own — only transitively via this test, matching
/// the pattern documented on <c>V6SanityPortTest.cs</c>'s class doc comment for other
/// zero-direct-coverage private helpers) and confirms it feeds <c>build()</c>'s <c>Warns</c> list.
/// </summary>
public class V6SanityPortBuildTest
{
    [Fact]
    public void DetectsImpossibleWishAndInvalidAssignment()
    {
        // s0 の day0 は希望「A」だが groupShift で A を担当不可＝実現不能希望。day1 は担当外の「A」を
        // 割り当て済み（groupShift[0][1]=0）＝invalidAssignmentCells の対象。
        var st = new MagiState(
            StartDate: "2026-06-01", EndDate: "2026-06-02",
            Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "1", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 0 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 0 } },
            Wishes: new Dictionary<string, int> { ["0,0"] = 1 },
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());

        var rep = V6SanityPort.Build(st);
        Assert.Single(rep.ImpossibleWishes);
        Assert.Contains(rep.Warns, w => w.Contains("実現不能"));
        Assert.Contains(rep.Warns, w => w.Contains("担当不可"));
    }
}
