using System.Text.Json.Nodes;
using MagiEngine.Model;
using MagiEngine.V6;
using Group = MagiEngine.Model.Group;

namespace MagiEngine.Tests.Model;

/// <summary>不変の職員 ID（2026-10-13 ユーザー決定、Kotlin <c>StaffIdTest</c> と 1 対 1）: 読込で欠け・重複・0 以下に振り、保存で書き、構造編集で保つ。</summary>
public class StaffIdTest
{
    private static string Json(string staff) => """
        {
          "startDate": "2026-07-01", "endDate": "2026-07-02",
          "shifts": [{"name":"日勤","kigou":"日","need1":"1","need2":""},{"name":"休み","kigou":"休","need1":"0","need2":""}],
          "groups": [{"name":"A","kigou":"A"}],
          "staff": [
        """ + staff + """
        ],
          "schedule": [[0,1],[1,0],[0,0],[1,1]]
        }
        """;

    private static readonly string Legacy = Json("""{"name":"田中","groupIdx":0},{"name":"鈴木","groupIdx":0},{"name":"田中","groupIdx":0},{"name":"佐藤","groupIdx":0}""");

    private static int[][] Sched(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();

    private static int[] Ids(MagiState st) => st.StaffList.Select(s => s.Id).ToArray();

    private static int[] IdsOf(string json) =>
        JsonNode.Parse(json)!["staff"]!.AsArray().Select(n => (int)n!["id"]!).ToArray();

    [Fact]
    public void LegacyDataGetsIdsInReadingOrder() =>
        Assert.Equal(new[] { 1, 2, 3, 4 }, Ids(StateJsonSerializer.Parse(Legacy)));

    [Fact]
    public void MissingDuplicateAndNonPositiveIdsGetMaxPlusOne()
    {
        var st = StateJsonSerializer.Parse(Json("""{"id":7,"name":"a","groupIdx":0},{"id":7,"name":"b","groupIdx":0},{"id":0,"name":"c","groupIdx":0},{"name":"d","groupIdx":0},{"id":-3,"name":"e","groupIdx":0},{"id":2,"name":"f","groupIdx":0}"""));
        Assert.Equal(new[] { 7, 8, 9, 10, 11, 2 }, Ids(st));
    }

    /// <summary>最大値が int の上限でも負へ回り込まず、使われていない最小の正の ID を振る。</summary>
    [Fact]
    public void IdsNeverWrapNegativeAtIntMax()
    {
        var st = StateJsonSerializer.Parse(Json("""{"id":2147483647,"name":"a","groupIdx":0},{"name":"b","groupIdx":0},{"id":1,"name":"c","groupIdx":0},{"name":"d","groupIdx":0}"""));
        Assert.Equal(new[] { int.MaxValue, 2, 1, 3 }, Ids(st));
        Assert.Equal(4, StaffIds.NextStaffId(st.StaffList));
    }

    [Fact]
    public void SerializeRoundTripsIds()
    {
        var st = StateJsonSerializer.Parse(Json("""{"id":5,"name":"a","groupIdx":0},{"id":2,"name":"b","groupIdx":0},{"name":"c","groupIdx":0},{"id":9,"name":"d","groupIdx":0}"""));
        Assert.Equal(new[] { 5, 2, 10, 9 }, Ids(st));
        var json = StateJsonSerializer.Serialize(st, Sched(st));
        Assert.Equal(st.StaffList, StateJsonSerializer.Parse(json).StaffList);
        Assert.Equal(new[] { 5, 2, 10, 9 }, IdsOf(json));
    }

    [Fact]
    public void ScheduleOnlyAndConstraintSavesWriteTheAssignedIds()
    {
        var st = StateJsonSerializer.Parse(Legacy);
        var a = StateJsonSerializer.ExportCurrent(Legacy, st, Sched(st), structureEdited: false, constraintsEdited: false)!;
        var b = StateJsonSerializer.ExportCurrent(Legacy, st, Sched(st), structureEdited: false, constraintsEdited: true)!;
        foreach (var output in new[] { a, b })
        {
            Assert.Equal(new[] { 1, 2, 3, 4 }, IdsOf(output));
            Assert.Equal(st.StaffList, StateJsonSerializer.Parse(output).StaffList);
        }
    }

    [Fact]
    public void StructureEditsKeepIdsAndAddingTakesMaxPlusOne()
    {
        var st = StateJsonSerializer.Parse(Json("""{"id":3,"name":"a","groupIdx":0},{"id":8,"name":"b","groupIdx":0},{"id":5,"name":"c","groupIdx":0},{"id":1,"name":"d","groupIdx":0}"""));
        var added = Ws1Ops.AddStaff(st, Sched(st), "e", 0);
        Assert.Equal(new[] { 3, 8, 5, 1, 9 }, Ids(added.State));
        var moved = Ws1Ops.MoveStaff(added.State, added.Schedule, 0, +1);
        Assert.Equal(new[] { 8, 3, 5, 1, 9 }, Ids(moved.State));
        var renamed = Ws1Ops.EditStaff(moved.State, 1, "a2", 0);
        Assert.Equal(3, renamed.StaffList[1].Id);
        var removed = Ws1Ops.RemoveStaff(renamed, moved.Schedule, 0);
        Assert.Equal(new[] { 3, 5, 1, 9 }, Ids(removed.State));
        var again = Ws1Ops.AddStaff(removed.State, removed.Schedule, "f", 0);
        Assert.Equal(10, again.State.StaffList[^1].Id);   // 次は今の最大＋1
    }

    [Fact]
    public void RemovingAGroupKeepsIds()
    {
        var st0 = StateJsonSerializer.Parse(Legacy);
        var st = st0 with
        {
            Groups = st0.Groups.Append(new Group("B", "B")).ToList(),
            GroupShift = st0.GroupShift.Append(new[] { 1, 1 }).ToList(),
        };
        var moved = st with { StaffList = st.StaffList.Select((s, i) => i == 1 ? s with { GroupIdx = 1 } : s).ToList() };
        Assert.Equal(new[] { 1, 2, 3, 4 }, Ids(Ws1Ops.RemoveGroup(moved, 0)));
    }
}
