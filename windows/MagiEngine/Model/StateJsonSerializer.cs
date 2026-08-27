using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using static MagiEngine.Model.JsonHelpers;

namespace MagiEngine.Model;

/// <summary>
/// Parses / serializes the state JSON. Mirrors Kotlin's <c>StateParser.kt</c> field-for-field
/// (including the "reparse original text, mutate only the touched keys, keep everything else
/// verbatim" strategy for <see cref="ExportWithSchedule"/>/<see cref="ExportWithEdits"/>), so
/// JSON exported by the Android app round-trips through this C# port without conversion.
/// </summary>
public static class StateJsonSerializer
{
    /// <summary>Top-level keys the model owns; anything else is preserved verbatim in
    /// <see cref="MagiState.Extras"/> for lossless round-tripping.</summary>
    private static readonly HashSet<string> ModelledKeys = new()
    {
        "shifts", "groups", "staff", "groupShift", "groupShiftApt", "schedule", "wishes", "staffRange",
        "needDay1", "needDay2", "cons1", "cons2", "cons3", "cons3n", "cons3m", "cons3mn",
        "cons41", "cons42", "shiftColors", "startDate", "endDate", "use2Patterns",
        "skillGroups", "cons41s", "cons42s",
    };

    /// <summary>Keys derived from `schedule`/edits that must be dropped before re-emitting the
    /// original JSON with a new schedule/edits — stale caches would otherwise silently disagree
    /// with the new schedule (mirrors the Kotlin `exportWithSchedule`/`exportWithEdits` list).</summary>
    private static readonly string[] DerivedKeysToDrop =
        { "violations", "needViolations", "countViolations", "lastResult", "lastPhase" };

    public static MagiState Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var o = doc.RootElement;

        var shifts = MapObjects(OptArray(o, "shifts"), "shifts", it =>
            new Shift(OptString(it, "name"), OptString(it, "kigou"), AsStr(Opt(it, "need1")), AsStr(Opt(it, "need2"))));
        var groups = MapObjects(OptArray(o, "groups"), "groups", it =>
            new Group(OptString(it, "name"), OptString(it, "kigou")));
        var staff = MapObjects(OptArray(o, "staff"), "staff", it =>
            new Staff(OptString(it, "name"), OptInt(it, "groupIdx", 0), OptInt(it, "skillIdx", 0)));
        var skillGroups = MapObjects(OptArray(o, "skillGroups"), "skillGroups", it =>
            new Group(OptString(it, "name"), OptString(it, "kigou")));
        var groupShift = MapArrays(OptArray(o, "groupShift"), "groupShift", row =>
            (IReadOnlyList<int>)IntRow(row, 0));
        var groupShiftApt = MapArrays(OptArray(o, "groupShiftApt"), "groupShiftApt", row =>
            (IReadOnlyList<string>)StrRow(row));
        var schedule = MapArrays(OptArray(o, "schedule"), "schedule", row =>
            // [監査A9, 移植元] 不正/null セルは 0(先頭シフト) でなく -1(未割当) に倒す（勝手な勤務化を防ぐ）。
            (IReadOnlyList<int>)IntRow(row, -1));

        var wishes = new Dictionary<string, int>();
        var wishesObj = OptObject(o, "wishes");
        if (wishesObj is not null)
        {
            foreach (var prop in wishesObj.Value.EnumerateObject())
                wishes[prop.Name] = OptIntAt(prop.Value, -1);
        }

        var staffRange = new Dictionary<string, Range>();
        var staffRangeObj = OptObject(o, "staffRange");
        if (staffRangeObj is not null)
        {
            foreach (var prop in staffRangeObj.Value.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    staffRange[prop.Name] = new Range(AsStr(Opt(prop.Value, "lo")), AsStr(Opt(prop.Value, "hi")));
            }
        }

        var needDay1 = StrMap(OptObject(o, "needDay1"));
        var needDay2 = StrMap(OptObject(o, "needDay2"));
        var shiftColors = StrMap(OptObject(o, "shiftColors"));

        var cons1 = MapObjects(OptArray(o, "cons1"), "cons1", it =>
            new C1Row(AsStr(Opt(it, "day1")), OptString(it, "shiftKigou"), AsStr(Opt(it, "day2"))));
        var cons2 = MapObjects(OptArray(o, "cons2"), "cons2", it =>
            new C2Row(OptString(it, "shiftKigou"), AsStr(Opt(it, "count"))));
        var cons3 = MapObjects(OptArray(o, "cons3"), "cons3", it => new C3Row(StrList(OptArray(it, "pattern"))));
        var cons3n = MapObjects(OptArray(o, "cons3n"), "cons3n", it => new C3Row(StrList(OptArray(it, "pattern"))));
        var cons3m = MapObjects(OptArray(o, "cons3m"), "cons3m", it => new C3Row(StrList(OptArray(it, "pattern"))));
        var cons3mn = MapObjects(OptArray(o, "cons3mn"), "cons3mn", it => new C3Row(StrList(OptArray(it, "pattern"))));
        var cons41 = MapObjects(OptArray(o, "cons41"), "cons41", it =>
            new C41Row(OptString(it, "groupKigou"), OptString(it, "shiftKigou"), AsStr(Opt(it, "l")), AsStr(Opt(it, "u"))));
        var cons42 = MapObjects(OptArray(o, "cons42"), "cons42", it =>
            new C42Row(OptString(it, "g1Kigou"), OptString(it, "g2Kigou"), OptString(it, "s1Kigou"), OptString(it, "s2Kigou")));
        var cons41s = MapObjects(OptArray(o, "cons41s"), "cons41s", it =>
            new C41Row(OptString(it, "groupKigou"), OptString(it, "shiftKigou"), AsStr(Opt(it, "l")), AsStr(Opt(it, "u"))));
        var cons42s = MapObjects(OptArray(o, "cons42s"), "cons42s", it =>
            new C42Row(OptString(it, "g1Kigou"), OptString(it, "g2Kigou"), OptString(it, "s1Kigou"), OptString(it, "s2Kigou")));

        // Keep unmodelled top-level keys verbatim for lossless export. Clone() detaches each
        // element from `doc`'s backing buffer so it stays valid after `doc` is disposed.
        var extras = new Dictionary<string, JsonElement>();
        foreach (var prop in o.EnumerateObject())
        {
            if (!ModelledKeys.Contains(prop.Name)) extras[prop.Name] = prop.Value.Clone();
        }

        return new MagiState(
            StartDate: OptString(o, "startDate", "2025-01-01"),
            EndDate: OptString(o, "endDate", ""),
            Shifts: shifts, Groups: groups, StaffList: staff,
            Use2Patterns: OptBoolean(o, "use2Patterns", false),
            GroupShift: groupShift, GroupShiftApt: groupShiftApt, Schedule: schedule,
            Wishes: wishes, StaffRange: staffRange,
            NeedDay1: needDay1, NeedDay2: needDay2,
            Cons1: cons1, Cons2: cons2,
            Cons3: cons3, Cons3n: cons3n, Cons3m: cons3m, Cons3mn: cons3mn,
            Cons41: cons41, Cons42: cons42,
            SkillGroups: skillGroups, Cons41s: cons41s, Cons42s: cons42s,
            ShiftColors: shiftColors,
            Extras: extras
        );
    }

    /// <summary>
    /// Re-emit the state with <paramref name="newSchedule"/> substituted. Reparses the
    /// original text (as a mutable node tree) so every field — including ones this app
    /// does not model — is preserved exactly, then overwrites only "schedule".
    /// </summary>
    public static string ExportWithSchedule(string originalJson, int[][] newSchedule)
    {
        var o = JsonNode.Parse(originalJson)!.AsObject();
        foreach (var k in DerivedKeysToDrop) o.Remove(k);
        o["schedule"] = IntGridNode(newSchedule);
        return o.ToJsonString(PrettyOptions);
    }

    /// <summary>
    /// Like <see cref="ExportWithSchedule"/> but also overwrites the 10 constraint arrays
    /// from <paramref name="state"/> (used after ws3-5 constraint editing). All other
    /// top-level fields — including groupShiftApt and any unmodelled keys — are preserved
    /// via the original-JSON round-trip.
    /// </summary>
    public static string ExportWithEdits(string originalJson, MagiState state, int[][] newSchedule)
    {
        var o = JsonNode.Parse(originalJson)!.AsObject();
        foreach (var k in DerivedKeysToDrop) o.Remove(k);
        o["schedule"] = IntGridNode(newSchedule);
        o["cons1"] = ConsArr(state.Cons1, it => Obj(("day1", it.Day1), ("shiftKigou", it.ShiftKigou), ("day2", it.Day2)));
        o["cons2"] = ConsArr(state.Cons2, it => Obj(("shiftKigou", it.ShiftKigou), ("count", it.Count)));
        o["cons3"] = ConsArr(state.Cons3, PatternObj);
        o["cons3n"] = ConsArr(state.Cons3n, PatternObj);
        o["cons3m"] = ConsArr(state.Cons3m, PatternObj);
        o["cons3mn"] = ConsArr(state.Cons3mn, PatternObj);
        o["cons41"] = ConsArr(state.Cons41, it => Obj(("groupKigou", it.GroupKigou), ("shiftKigou", it.ShiftKigou), ("l", it.L), ("u", it.U)));
        o["cons42"] = ConsArr(state.Cons42, it => Obj(("g1Kigou", it.G1Kigou), ("g2Kigou", it.G2Kigou), ("s1Kigou", it.S1Kigou), ("s2Kigou", it.S2Kigou)));
        // [監査A1, 移植元] スキル制約も書き出す（従来は旧8族のみで、cons41s/42s の追加・削除・変更が
        //   このエクスポート経路(constraintsEditedのみ)から無言で欠落していた）。
        o["cons41s"] = ConsArr(state.Cons41s, it => Obj(("groupKigou", it.GroupKigou), ("shiftKigou", it.ShiftKigou), ("l", it.L), ("u", it.U)));
        o["cons42s"] = ConsArr(state.Cons42s, it => Obj(("g1Kigou", it.G1Kigou), ("g2Kigou", it.G2Kigou), ("s1Kigou", it.S1Kigou), ("s2Kigou", it.S2Kigou)));
        return o.ToJsonString(PrettyOptions);
    }

    /// <summary>
    /// Full serialization of a <see cref="MagiState"/> (used after ws1 initial-setup edits that
    /// change dimensions, where the original-JSON round-trip is no longer valid). Writes every
    /// modelled field plus any <see cref="MagiState.Extras"/> verbatim, using the exact key
    /// names <see cref="Parse"/> reads, so serialize -&gt; parse round-trips. <paramref name="schedule"/>
    /// overrides state.Schedule (the working table).
    /// </summary>
    public static string Serialize(MagiState state, int[][] schedule)
    {
        var o = new JsonObject
        {
            ["startDate"] = state.StartDate,
            ["endDate"] = state.EndDate,
            ["use2Patterns"] = state.Use2Patterns,
            ["shifts"] = ConsArr(state.Shifts, it => Obj(("name", it.Name), ("kigou", it.Kigou), ("need1", it.Need1), ("need2", it.Need2))),
            ["groups"] = ConsArr(state.Groups, it => Obj(("name", it.Name), ("kigou", it.Kigou))),
        };

        var staffArr = new JsonArray();
        foreach (var s in state.StaffList)
            staffArr.Add(new JsonObject { ["name"] = s.Name, ["groupIdx"] = s.GroupIdx, ["skillIdx"] = s.SkillIdx });
        o["staff"] = staffArr;

        o["skillGroups"] = ConsArr(state.SkillGroups, it => Obj(("name", it.Name), ("kigou", it.Kigou)));
        o["groupShift"] = IntGridNode(state.GroupShift);
        o["groupShiftApt"] = StrGridNode(state.GroupShiftApt);
        o["schedule"] = IntGridNode(schedule);

        var wishesNode = new JsonObject();
        foreach (var (k, v) in state.Wishes) wishesNode[k] = v;
        o["wishes"] = wishesNode;

        var srNode = new JsonObject();
        foreach (var (k, v) in state.StaffRange) srNode[k] = new JsonObject { ["lo"] = v.Lo, ["hi"] = v.Hi };
        o["staffRange"] = srNode;

        o["needDay1"] = StrKeyMapNode(state.NeedDay1);
        o["needDay2"] = StrKeyMapNode(state.NeedDay2);
        o["shiftColors"] = StrKeyMapNode(state.ShiftColors);

        o["cons1"] = ConsArr(state.Cons1, it => Obj(("day1", it.Day1), ("shiftKigou", it.ShiftKigou), ("day2", it.Day2)));
        o["cons2"] = ConsArr(state.Cons2, it => Obj(("shiftKigou", it.ShiftKigou), ("count", it.Count)));
        o["cons3"] = ConsArr(state.Cons3, PatternObj);
        o["cons3n"] = ConsArr(state.Cons3n, PatternObj);
        o["cons3m"] = ConsArr(state.Cons3m, PatternObj);
        o["cons3mn"] = ConsArr(state.Cons3mn, PatternObj);
        o["cons41"] = ConsArr(state.Cons41, it => Obj(("groupKigou", it.GroupKigou), ("shiftKigou", it.ShiftKigou), ("l", it.L), ("u", it.U)));
        o["cons42"] = ConsArr(state.Cons42, it => Obj(("g1Kigou", it.G1Kigou), ("g2Kigou", it.G2Kigou), ("s1Kigou", it.S1Kigou), ("s2Kigou", it.S2Kigou)));
        o["cons41s"] = ConsArr(state.Cons41s, it => Obj(("groupKigou", it.GroupKigou), ("shiftKigou", it.ShiftKigou), ("l", it.L), ("u", it.U)));
        o["cons42s"] = ConsArr(state.Cons42s, it => Obj(("g1Kigou", it.G1Kigou), ("g2Kigou", it.G2Kigou), ("s1Kigou", it.S1Kigou), ("s2Kigou", it.S2Kigou)));

        foreach (var (k, v) in state.Extras)
        {
            if (!o.ContainsKey(k)) o[k] = JsonNode.Parse(v.GetRawText());
        }

        return o.ToJsonString(PrettyOptions);
    }

    // ---- node-building helpers (mirror Kotlin's private consArr/obj/patternObj/intGrid/...) ----

    // TypeInfoResolver must be set explicitly: JsonNode.ToJsonString(options) writes boxed
    // primitives (int values added via JsonArray.Add(int)) through a reflection-based path
    // that .NET 8 refuses to run against a JsonSerializerOptions with no resolver configured
    // ("must specify a TypeInfoResolver setting before being marked as read-only").
    private static readonly JsonSerializerOptions PrettyOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static JsonArray ConsArr<T>(IReadOnlyList<T> list, Func<T, JsonObject> f)
    {
        var a = new JsonArray();
        foreach (var e in list) a.Add(f(e));
        return a;
    }

    private static JsonObject Obj(params (string Key, string Value)[] pairs)
    {
        var o = new JsonObject();
        foreach (var (k, v) in pairs) o[k] = v;
        return o;
    }

    private static JsonObject PatternObj(C3Row row)
    {
        var p = new JsonArray();
        foreach (var s in row.Pattern) p.Add(s);
        return new JsonObject { ["pattern"] = p };
    }

    private static JsonArray IntGridNode(IReadOnlyList<IReadOnlyList<int>> grid)
    {
        var a = new JsonArray();
        foreach (var row in grid)
        {
            var r = new JsonArray();
            foreach (var v in row) r.Add(v);
            a.Add(r);
        }
        return a;
    }

    private static JsonArray IntGridNode(int[][] grid)
    {
        var a = new JsonArray();
        foreach (var row in grid)
        {
            var r = new JsonArray();
            foreach (var v in row) r.Add(v);
            a.Add(r);
        }
        return a;
    }

    private static JsonArray StrGridNode(IReadOnlyList<IReadOnlyList<string>> grid)
    {
        var a = new JsonArray();
        foreach (var row in grid)
        {
            var r = new JsonArray();
            foreach (var v in row) r.Add(v);
            a.Add(r);
        }
        return a;
    }

    private static JsonObject StrKeyMapNode(IReadOnlyDictionary<string, string> m)
    {
        var o = new JsonObject();
        foreach (var (k, v) in m) o[k] = v;
        return o;
    }

    // ---- row parsing helpers ----

    private static List<int> IntRow(JsonElement row, int @default)
    {
        var result = new List<int>(row.GetArrayLength());
        foreach (var item in row.EnumerateArray()) result.Add(OptIntAt(item, @default));
        return result;
    }

    private static List<string> StrRow(JsonElement row)
    {
        var result = new List<string>(row.GetArrayLength());
        foreach (var item in row.EnumerateArray()) result.Add(AsStr(item));
        return result;
    }
}
