using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [backlog#38] スキルグループ未指定の職員は <c>SkillIdx = -1</c>（未所属）。既定の出どころ（型の既定値・<c>skillIdx</c> の無い JSON・
/// 職員追加・名簿取込）がすべて -1 になることと、<see cref="Ws1Ops.AddSkillGroup"/> が 0 件から最初の 1 群を作るときだけ全員を
/// 未所属にする（群が 1 件以上なら触らない）ことを固定する（<c>Ws1OpsSkillGroupTest.kt</c> の逐語移植）。
/// </summary>
public class Ws1OpsSkillGroupTest
{
    /// <summary><c>MagiViewModel.InitBlankState</c> と同じ種（職員は <c>skillIdx</c> を持たない）。</summary>
    private const string BlankSeed =
        "{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-03\"," +
        "\"shifts\":[{\"name\":\"休み\",\"kigou\":\"休\",\"need1\":\"\",\"need2\":\"\"}]," +
        "\"groups\":[{\"name\":\"グループA\",\"kigou\":\"A\"}]," +
        "\"staff\":[{\"name\":\"職員1\",\"groupIdx\":0}]," +
        "\"use2Patterns\":true," +
        "\"groupShift\":[[1]],\"groupShiftApt\":[[\"\"]]," +
        "\"cons1\":[],\"cons2\":[],\"cons3\":[],\"cons3n\":[],\"cons3m\":[],\"cons3mn\":[],\"cons41\":[],\"cons42\":[]," +
        "\"wishes\":{},\"staffRange\":{},\"needDay1\":{},\"needDay2\":{}," +
        "\"schedule\":[[0,0,0]]}";

    private static MagiState State(IReadOnlyList<Staff> staff, IReadOnlyList<Group> skillGroups) => new MagiState(
        StartDate: "2026-07-01", EndDate: "2026-07-02",
        Shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: staff,
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: staff.Select(_ => (IReadOnlyList<int>)new List<int> { 1, 0 }).ToList(),
        Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(),
        Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(), Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: skillGroups, Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: MinimalState.NoExtras
    );

    private static int[][] Grid(MagiState st) => st.Schedule.Select(row => row.ToArray()).ToArray();

    [Fact]
    public void StaffWithoutSkillIdxKeyParsesAsUnassigned()
    {
        Assert.Equal(-1, new Staff("x", 0).SkillIdx); // 型の既定値
        Assert.Equal(new[] { -1 }, StateJsonSerializer.Parse(BlankSeed).StaffList.Select(s => s.SkillIdx)); // キーの無い JSON
        var explicitZero = BlankSeed.Replace("\"groupIdx\":0}", "\"groupIdx\":0,\"skillIdx\":0}");
        Assert.Equal(new[] { 0 }, StateJsonSerializer.Parse(explicitZero).StaffList.Select(s => s.SkillIdx)); // 明示の 0 はそのまま
    }

    [Fact]
    public void AddStaffLeavesTheNewStaffUnassigned()
    {
        var st = State(new List<Staff> { new("s0", 0, 0) }, new List<Group> { new("L", "L") });
        var r = Ws1Ops.AddStaff(st, Grid(st), "新人", 0);
        Assert.Equal(new[] { 0, -1 }, r.State.StaffList.Select(s => s.SkillIdx));
    }

    [Fact]
    public void RosterCsvImportLeavesNewStaffUnassigned()
    {
        var template = string.Join("\n", new[]
        {
            "令和8年,,,7,月",
            "ユニット名：,,柳,,1,2,3",
            "№,,氏 名,,水,木,金",
            "1,リーダー,古泉 健一,予定,A4,,休",
            "2,,山本 昌幸,予定,A4,休,",
            ",,,,,,",
            ",記号,時刻,休憩時間,水,木,金",
            ",A4,6:00～15:00,1h,1,0,1",
            ",休,定休,,1,2,1",
        });
        var flat = string.Join("\n", new[]
        {
            "ユニット,No,役職,氏名,1,2,3",
            "柳,1,,古泉 健一,A,,休",
            "柳,2,,山本 昌幸,B,A,",
        });
        foreach (var (label, st) in new[] { ("テンプレ", RosterCsvImport.Parse(template)!), ("一覧", FlatRosterCsvImport.Parse(flat)!) })
        {
            Assert.True(st.StaffList.Select(s => s.SkillIdx).SequenceEqual(new[] { -1, -1 }), label);
        }
    }

    [Fact]
    public void BlankStartThenFirstSkillGroupHasNoMembers()
    {
        var st = StateJsonSerializer.Parse(BlankSeed);
        var added = Ws1Ops.AddStaff(st, Grid(st), "職員2", 0).State;
        var after = Ws1Ops.AddSkillGroup(added, "リーダー", "L");
        Assert.Equal(new List<Group> { new("リーダー", "L") }, after.SkillGroups);
        Assert.All(new Problem(after).Ssk, k => Assert.Equal(-1, k)); // 誰も所属しない
    }

    [Fact]
    public void AddingAnotherSkillGroupKeepsExistingAssignments()
    {
        var st = State(new List<Staff> { new("s0", 0, 0), new("s1", 0, 1), new("s2", 0, -1) }, new List<Group> { new("L", "L"), new("N", "N") });
        var after = Ws1Ops.AddSkillGroup(st, "中堅", "M");
        Assert.Equal(new[] { "L", "N", "M" }, after.SkillGroups.Select(g => g.Kigou));
        Assert.Equal(new[] { 0, 1, -1 }, after.StaffList.Select(s => s.SkillIdx)); // 群が 1 件以上なら割当は触らない
    }

    [Fact]
    public void FirstSkillGroupOnAnOldSaveWithExplicitZerosUnassignsEveryone()
    {
        // 既定が 0 だった頃の保存＝群が 0 件なのに全員が明示の 0。
        var st = State(new List<Staff> { new("s0", 0, 0), new("s1", 0, 0) }, new List<Group>());
        var after = Ws1Ops.AddSkillGroup(st, "リーダー", "L");
        Assert.Equal(new[] { -1, -1 }, after.StaffList.Select(s => s.SkillIdx));
        Assert.Equal( // この時点では採点は変わらない
            UnifiedViolationChecker.Check(st, Grid(st)).WeightedScore, UnifiedViolationChecker.Check(after, Grid(after)).WeightedScore);
    }
}
