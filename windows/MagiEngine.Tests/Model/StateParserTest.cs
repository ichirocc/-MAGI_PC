using MagiEngine.Model;

namespace MagiEngine.Tests.Model;

/// <summary>
/// <c>StateParserTest.kt</c> の移植。[外部レビュー P2-02] 要素がオブジェクト/配列でなければ明示的に失敗すること、
/// [外部レビュー N1] 休み（<see cref="ShiftRole.Rest"/>）が保存→再読込で往復することを固定する。
/// </summary>
public class StateParserTest
{
    // 最小の妥当な JSON（staff 2件・shifts 1件）。壊れていない入力は従来どおり通ることの対照。
    private const string ValidJson = """
        {
          "shifts": [{"name":"日勤","kigou":"日","need1":"1","need2":""}],
          "groups": [{"name":"A","kigou":"A"}],
          "staff": [{"name":"田中","groupIdx":0,"skillIdx":0},{"name":"鈴木","groupIdx":0,"skillIdx":0}]
        }
        """;

    [Fact]
    public void WellFormedArraysParseNormally()
    {
        var st = StateJsonSerializer.Parse(ValidJson);
        Assert.Equal(2, st.StaffList.Count);
        Assert.Single(st.Shifts);
        Assert.Single(st.Groups);
    }

    [Fact]
    public void NullElementInStaffArrayThrowsInsteadOfSilentlyShrinking()
    {
        const string corrupted = """
            {
              "shifts": [{"name":"日勤","kigou":"日","need1":"1","need2":""}],
              "groups": [{"name":"A","kigou":"A"}],
              "staff": [{"name":"田中","groupIdx":0,"skillIdx":0}, null]
            }
            """;
        var e = Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(corrupted));
        Assert.Contains("staff", e.Message);
    }

    [Fact]
    public void NumberElementInScheduleRowArrayThrows()
    {
        const string corrupted = """
            {
              "shifts": [{"name":"日勤","kigou":"日","need1":"1","need2":""}],
              "groups": [{"name":"A","kigou":"A"}],
              "staff": [{"name":"田中","groupIdx":0,"skillIdx":0}],
              "schedule": [[0], 5]
            }
            """;
        var e = Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(corrupted));
        Assert.Contains("schedule", e.Message);
    }

    // ---- [外部レビュー N1] 休み（ShiftRole.Rest）の往復 ----

    private static int RestIdx(MagiState st) => st.Shifts.ToList().FindIndex(s => s.Role == ShiftRole.Rest);

    private static MagiState TwoShiftState(ShiftRole rest0, ShiftRole rest1) => StateJsonSerializer.Parse(ValidJson) with
    {
        Shifts = new List<Shift> { new("休み", "休", "", "", rest0), new("日勤", "日", "1", "", rest1) },
        GroupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        Schedule = new List<IReadOnlyList<int>> { new List<int> { 0 }, new List<int> { 1 } },
    };

    private static MagiState RoundTrip(MagiState st) =>
        StateJsonSerializer.Parse(StateJsonSerializer.Serialize(st, st.Schedule.Select(r => r.ToArray()).ToArray()));

    [Fact]
    public void RestRoleTurnedOffSurvivesSaveAndReload()
    {
        // 旧: 保存は全シフト role="" で、読込の後方互換が「どれにも Rest が無い＝旧JSON」と見て記号"休"へ付け直していた。
        var back = RoundTrip(TwoShiftState(ShiftRole.None, ShiftRole.None));
        Assert.True(RestIdx(back) == -1, "休みOFFで保存したのに再読込で休みが付いた");
    }

    [Fact]
    public void RestRoleOnAnotherShiftSurvivesSaveAndReload()
    {
        Assert.Equal(1, RestIdx(RoundTrip(TwoShiftState(ShiftRole.None, ShiftRole.Rest))));
        Assert.Equal(0, RestIdx(RoundTrip(TwoShiftState(ShiftRole.Rest, ShiftRole.None))));
    }

    [Fact]
    public void LegacyJsonWithoutRoleGetsRestBySymbol()
    {
        const string legacy = """{"shifts":[{"name":"日勤","kigou":"日"},{"name":"休み","kigou":"休"}],"groups":[],"staff":[]}""";
        Assert.Equal(1, RestIdx(StateJsonSerializer.Parse(legacy)));
    }

    [Fact]
    public void BlankRoleFromEarlierSavesStillGetsRestBySymbol()
    {
        // 非休を "" で書いていた保存（Android の CSV 取込のまま保存した原本は全シフト ""）＝旧JSONと同じく記号で付与する。
        const string blank = """{"shifts":[{"name":"日勤","kigou":"日","role":""},{"name":"休み","kigou":"休","role":""}],"groups":[],"staff":[]}""";
        Assert.Equal(1, RestIdx(StateJsonSerializer.Parse(blank)));
    }

    private static string With(string extra) => ValidJson.TrimEnd().TrimEnd('}') + ",\n" + extra + "\n}";

    /// <summary>型の違う並び・個人の上下限は空として取り込まず読み込みを止める（Kotlin と 1 対 1）。</summary>
    [Fact]
    public void MalformedPatternOrRangeOrConstraintFieldThrows()
    {
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With("\"cons3n\": [{\"pattern\": \"A\"}]")));
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With("\"cons3n\": {\"pattern\": [\"A\"]}")));
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With("\"staffRange\": {\"0,0\": \"3\"}")));
        Assert.Empty(StateJsonSerializer.Parse(With("\"cons3n\": [{}]")).Cons3n.Single().Pattern);   // 未指定は空のまま
    }

    /// <summary>表・配列そのものの型違い（個人の上下限・希望・日別必要人数・手動固定・拡張希望）も空として取り込まない。</summary>
    [Fact]
    public void MalformedTopLevelTablesThrowWithTheirName()
    {
        foreach (var (key, bad) in new[] { ("staffRange", "\"3\""), ("wishes", "[1]"), ("needDay1", "\"2\""), ("needDay2", "[]"),
                     ("manualPins", "{}"), ("extWishes", "\"x\""), ("schedule", "{}"), ("skillGroups", "\"x\""), ("groupShift", "{}") })
        {
            var e = Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With($"\"{key}\": {bad}")));
            Assert.Contains(key, e.Message);
        }
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With("\"needDay1\": {\"0,0\": [1]}")));
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With("\"extWishes\": [{\"staff\": 0, \"days\": \"1\"}]")));
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With("\"wishes\": {\"0,0\": \"A\"}")));
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(With("\"manualPins\": [{\"staff\": 0, \"day\": 0.5, \"shift\": 0}]")));
    }

    /// <summary>小数・範囲外の勤務番号は丸めずに止める（1.9 を 1 にすると別の勤務として入る）。整数値の小数と null は従来どおり。</summary>
    [Fact]
    public void FractionalOrOutOfRangeShiftNumberThrows()
    {
        static string WithCells(string cells) => ValidJson.TrimEnd().TrimEnd('}') + ",\n\"schedule\": [[" + cells + "],[0]]\n}";
        var e = Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(WithCells("1.9")));
        Assert.Contains("schedule[0][0]", e.Message);
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(WithCells("1")));    // shifts は 1 件＝0 だけが有効
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(WithCells("-2")));
        Assert.Throws<ArgumentException>(() => StateJsonSerializer.Parse(WithCells("\"A\"")));
        Assert.Equal(new[] { 0, -1, -1 }, StateJsonSerializer.Parse(WithCells("0.0, null, -1")).Schedule[0].ToArray());
    }

    /// <summary>全体保存（Serialize）も古い評価情報を書き戻さない（Kotlin と 1 対 1）。</summary>
    [Fact]
    public void FullSaveDropsStaleDerivedKeys()
    {
        var st = StateJsonSerializer.Parse(With("\"violations\": {\"0,0\": \"vio-x\"}, \"lastResult\": {\"hard\": 3}, \"note\": \"keep\""));
        var outJson = System.Text.Json.Nodes.JsonNode.Parse(StateJsonSerializer.Serialize(st, new[] { new[] { 0 }, new[] { 0 } }))!.AsObject();
        Assert.False(outJson.ContainsKey("violations"));
        Assert.False(outJson.ContainsKey("lastResult"));
        Assert.Equal("keep", (string?)outJson["note"]);   // 評価情報でない未知のキーは残す
    }
}
