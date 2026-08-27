using System.Text.Json;
using MagiEngine.Model;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
// This alias makes bare `Range` in this file resolve unambiguously to our Range record
// (a using-alias directive always wins over a using-namespace directive during lookup).
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.TestSupport;

/// <summary>
/// Builds small, deterministic <see cref="MagiState"/> instances for unit tests, with a
/// sensible default for every field so a test only needs to override what it actually cares
/// about. Shared across phase 2 (<c>Problem</c>) and later phases' unit tests.
///
/// Default shape (when nothing is overridden): 2 shifts (index 0 = "休"/rest, index 1 = "A"/
/// work), 1 group ("G0") that can take both shifts, 2 staff ("職員A"/"職員B") both in G0, a
/// 7-day all-rest schedule, no constraints of any family, no wishes/ranges/need overrides.
/// </summary>
internal static class MinimalState
{
    public static readonly IReadOnlyDictionary<string, JsonElement> NoExtras =
        new Dictionary<string, JsonElement>();

    public static MagiState Build(
        string startDate = "2025-12-01",
        string endDate = "2025-12-07",
        IReadOnlyList<Shift>? shifts = null,
        IReadOnlyList<Group>? groups = null,
        IReadOnlyList<Staff>? staffList = null,
        bool use2Patterns = false,
        IReadOnlyList<IReadOnlyList<int>>? groupShift = null,
        IReadOnlyList<IReadOnlyList<string>>? groupShiftApt = null,
        IReadOnlyList<IReadOnlyList<int>>? schedule = null,
        IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyDictionary<string, Range>? staffRange = null,
        IReadOnlyDictionary<string, string>? needDay1 = null,
        IReadOnlyDictionary<string, string>? needDay2 = null,
        IReadOnlyList<C1Row>? cons1 = null,
        IReadOnlyList<C2Row>? cons2 = null,
        IReadOnlyList<C3Row>? cons3 = null,
        IReadOnlyList<C3Row>? cons3n = null,
        IReadOnlyList<C3Row>? cons3m = null,
        IReadOnlyList<C3Row>? cons3mn = null,
        IReadOnlyList<C41Row>? cons41 = null,
        IReadOnlyList<C42Row>? cons42 = null,
        IReadOnlyList<Group>? skillGroups = null,
        IReadOnlyList<C41Row>? cons41s = null,
        IReadOnlyList<C42Row>? cons42s = null,
        IReadOnlyDictionary<string, string>? shiftColors = null)
    {
        var shifts2 = shifts ?? new List<Shift>
        {
            new("休", "休", "", ""),
            new("A", "A", "", ""),
        };
        const int t = 7;
        var schedule2 = schedule ?? Enumerable.Range(0, 2)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, t).ToList())
            .ToList();
        var groups2 = groups ?? new List<Group> { new("G0", "G0") };
        // Default: every group can take every shift (so canDo/bucket tests aren't accidentally
        // gated unless a test explicitly narrows groupShift itself).
        var groupShift2 = groupShift
            ?? groups2.Select(_ => (IReadOnlyList<int>)shifts2.Select(_ => 1).ToList()).ToList();
        var staffList2 = staffList ?? new List<Staff> { new("職員A", 0), new("職員B", 0) };

        return new MagiState(
            StartDate: startDate,
            EndDate: endDate,
            Shifts: shifts2,
            Groups: groups2,
            StaffList: staffList2,
            Use2Patterns: use2Patterns,
            GroupShift: groupShift2,
            GroupShiftApt: groupShiftApt
                ?? groups2.Select(_ => (IReadOnlyList<string>)shifts2.Select(_ => "").ToList()).ToList(),
            Schedule: schedule2,
            Wishes: wishes ?? new Dictionary<string, int>(),
            StaffRange: staffRange ?? new Dictionary<string, Range>(),
            NeedDay1: needDay1 ?? new Dictionary<string, string>(),
            NeedDay2: needDay2 ?? new Dictionary<string, string>(),
            Cons1: cons1 ?? new List<C1Row>(),
            Cons2: cons2 ?? new List<C2Row>(),
            Cons3: cons3 ?? new List<C3Row>(),
            Cons3n: cons3n ?? new List<C3Row>(),
            Cons3m: cons3m ?? new List<C3Row>(),
            Cons3mn: cons3mn ?? new List<C3Row>(),
            Cons41: cons41 ?? new List<C41Row>(),
            Cons42: cons42 ?? new List<C42Row>(),
            SkillGroups: skillGroups ?? new List<Group>(),
            Cons41s: cons41s ?? new List<C41Row>(),
            Cons42s: cons42s ?? new List<C42Row>(),
            ShiftColors: shiftColors ?? new Dictionary<string, string>(),
            Extras: NoExtras
        );
    }
}
