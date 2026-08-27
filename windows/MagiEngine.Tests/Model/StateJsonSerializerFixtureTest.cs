using System.Text.Json;
using System.Text.Json.Nodes;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;

namespace MagiEngine.Tests.Model;

/// <summary>
/// フェーズ1の合否ゲート: 実データ4フィクスチャ（Android版と同一のstate JSON）で
/// parse→serialize→parse / parse→ExportWithSchedule→parse / parse→ExportWithEdits→parse
/// が全フィールド（extras含む）で往復一致することを確認する。
/// </summary>
public class StateJsonSerializerFixtureTest
{
    // Test-local mirror of StateJsonSerializer's private DerivedKeysToDrop: ExportWithSchedule
    // and ExportWithEdits both intentionally strip these from the original JSON before
    // re-emitting (stale caches must not survive a schedule/edit re-export), so state1's
    // Extras (parsed straight from the untouched fixture) legitimately still has them while
    // state2's (parsed from the export output) legitimately does not. That's the documented
    // behavior under test just below in each of those two methods — not a bug.
    private static readonly string[] DerivedKeys =
        { "violations", "needViolations", "countViolations", "lastResult", "lastPhase" };

    private static string ReadFixture(string name) => FixtureLoader.ReadRaw(name);

    private static int[][] ToJagged(IReadOnlyList<IReadOnlyList<int>> grid) =>
        grid.Select(row => row.ToArray()).ToArray();

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void ParseSerializeParse_RoundTrips(string fixtureFile)
    {
        var raw = ReadFixture(fixtureFile);
        var state1 = StateJsonSerializer.Parse(raw);

        var reJson = StateJsonSerializer.Serialize(state1, ToJagged(state1.Schedule));
        var state2 = StateJsonSerializer.Parse(reJson);

        AssertStatesEqual(state1, state2);
    }

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void ParseExportWithScheduleParse_RoundTrips(string fixtureFile)
    {
        var raw = ReadFixture(fixtureFile);
        var state1 = StateJsonSerializer.Parse(raw);

        var reJson = StateJsonSerializer.ExportWithSchedule(raw, ToJagged(state1.Schedule));
        var state2 = StateJsonSerializer.Parse(reJson);

        AssertStatesEqual(state1, state2, ignoreExtrasKeys: DerivedKeys);

        // Derived/stale keys must be dropped, not merely left stale, by ExportWithSchedule.
        foreach (var derived in new[] { "violations", "needViolations", "countViolations", "lastResult", "lastPhase" })
        {
            Assert.False(state2.Extras.ContainsKey(derived),
                $"{fixtureFile}: '{derived}' should have been dropped by ExportWithSchedule but is still present");
        }
    }

    [Theory]
    [MemberData(nameof(FixtureLoader.AllFiles), MemberType = typeof(FixtureLoader))]
    public void ParseExportWithEditsParse_RoundTrips(string fixtureFile)
    {
        var raw = ReadFixture(fixtureFile);
        var state1 = StateJsonSerializer.Parse(raw);

        // Identity "edit": re-emit the same constraints the state already carries.
        var reJson = StateJsonSerializer.ExportWithEdits(raw, state1, ToJagged(state1.Schedule));
        var state2 = StateJsonSerializer.Parse(reJson);

        // ExportWithEdits drops the same derived keys as ExportWithSchedule (see DerivedKeys).
        AssertStatesEqual(state1, state2, ignoreExtrasKeys: DerivedKeys);

        // [監査A1] cons41s/cons42s must always be written now, even for a fixture whose
        // original JSON never had the key at all (e.g. golden_state.json has neither key).
        var reDoc = JsonNode.Parse(reJson)!.AsObject();
        Assert.True(reDoc.ContainsKey("cons41s"), $"{fixtureFile}: cons41s missing from ExportWithEdits output");
        Assert.True(reDoc.ContainsKey("cons42s"), $"{fixtureFile}: cons42s missing from ExportWithEdits output");
    }

    // ---- deep comparison -------------------------------------------------

    private static void AssertStatesEqual(
        MagiState expected, MagiState actual, IReadOnlyCollection<string>? ignoreExtrasKeys = null)
    {
        Assert.Equal(expected.StartDate, actual.StartDate);
        Assert.Equal(expected.EndDate, actual.EndDate);
        Assert.Equal(expected.Use2Patterns, actual.Use2Patterns);
        Assert.Equal(expected.StaffCount, actual.StaffCount);
        Assert.Equal(expected.DayCount, actual.DayCount);
        Assert.Equal(expected.ShiftCount, actual.ShiftCount);
        Assert.Equal(expected.GroupCount, actual.GroupCount);
        Assert.Equal(expected.SkillGroupCount, actual.SkillGroupCount);

        // All-scalar records (Shift/Group/Staff/C1Row/C2Row/C41Row/C42Row) get correct
        // structural equality from the compiler-generated record Equals, so a plain
        // Assert.Equal over the list is reliable here.
        Assert.Equal(expected.Shifts, actual.Shifts);
        Assert.Equal(expected.Groups, actual.Groups);
        Assert.Equal(expected.StaffList, actual.StaffList);
        Assert.Equal(expected.SkillGroups, actual.SkillGroups);
        Assert.Equal(expected.GroupShift, actual.GroupShift);
        Assert.Equal(expected.GroupShiftApt, actual.GroupShiftApt);
        Assert.Equal(expected.Schedule, actual.Schedule);
        Assert.Equal(expected.Wishes, actual.Wishes);
        Assert.Equal(expected.StaffRange, actual.StaffRange);
        Assert.Equal(expected.NeedDay1, actual.NeedDay1);
        Assert.Equal(expected.NeedDay2, actual.NeedDay2);
        Assert.Equal(expected.ShiftColors, actual.ShiftColors);
        Assert.Equal(expected.Cons1, actual.Cons1);
        Assert.Equal(expected.Cons2, actual.Cons2);
        Assert.Equal(expected.Cons41, actual.Cons41);
        Assert.Equal(expected.Cons42, actual.Cons42);
        Assert.Equal(expected.Cons41s, actual.Cons41s);
        Assert.Equal(expected.Cons42s, actual.Cons42s);

        // C3Row carries a nested List<string> Pattern — the record's generated Equals
        // compares that field by reference, so it needs an explicit deep comparison.
        AssertC3RowsEqual(expected.Cons3, actual.Cons3);
        AssertC3RowsEqual(expected.Cons3n, actual.Cons3n);
        AssertC3RowsEqual(expected.Cons3m, actual.Cons3m);
        AssertC3RowsEqual(expected.Cons3mn, actual.Cons3mn);

        // JsonElement has no semantic Equals/GetHashCode; compare via JsonNode.DeepEquals.
        AssertExtrasEqual(expected.Extras, actual.Extras, ignoreExtrasKeys);
    }

    private static void AssertC3RowsEqual(IReadOnlyList<C3Row> expected, IReadOnlyList<C3Row> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Pattern, actual[i].Pattern);
        }
    }

    private static void AssertExtrasEqual(
        IReadOnlyDictionary<string, JsonElement> expected,
        IReadOnlyDictionary<string, JsonElement> actual,
        IReadOnlyCollection<string>? ignoreKeys = null)
    {
        var ignore = ignoreKeys is null ? new HashSet<string>() : new HashSet<string>(ignoreKeys);
        var expectedKeys = expected.Keys.Where(k => !ignore.Contains(k)).OrderBy(k => k, StringComparer.Ordinal);
        var actualKeys = actual.Keys.Where(k => !ignore.Contains(k)).OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(expectedKeys, actualKeys);
        foreach (var key in expected.Keys)
        {
            if (ignore.Contains(key)) continue;
            var a = JsonNode.Parse(expected[key].GetRawText());
            var b = JsonNode.Parse(actual[key].GetRawText());
            Assert.True(JsonNode.DeepEquals(a, b), $"extras['{key}'] differs after round-trip");
        }
    }
}
