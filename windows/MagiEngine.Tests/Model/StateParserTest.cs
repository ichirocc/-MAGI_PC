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
}
