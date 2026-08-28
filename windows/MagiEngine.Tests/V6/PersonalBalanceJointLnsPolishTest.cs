using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.255.0, 受領・検証のうえ適用] <see cref="PersonalBalanceJointLnsPolish"/>単体の検証。実データ
/// (golden_state.json/sample_state_v6.json、ホストJVM実行)で、既存パイプライン適用後にも追加で改善を
/// 見つけること(sample_state_v6.jsonでpersonal 34->31・total 196->195)を確認済み。
/// </summary>
public class PersonalBalanceJointLnsPolishTest
{
    [Fact]
    public void ResolvesSimpleLowDeficiencyWithoutFairSideEffect()
    {
        // 2職員を別々の単独群(G0/G1)にする＝fair(群内公平化)の巻き添えを避ける
        // （2人共有群だとこの規模ではfairが同時に動いてtotalが改善しない中立トレードになる）。
        var shifts = new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var staffList = new List<Staff> { new("a", 0), new("b", 1) };
        var a = new List<int> { 1, 1, 0, 0, 0 };
        var b = new List<int> { 0, 0, 0, 0, 0 };
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-05",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>>
            {
                new List<string> { "", "" },
                new List<string> { "", "" },
            },
            schedule: new List<IReadOnlyList<int>> { a, b },
            wishes: new Dictionary<string, int>(),
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("4", "") },
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(2, before.Breakdown.GetValueOrDefault("low", 0));
        Assert.Equal(0, before.Hard);

        var outp = PersonalBalanceJointLnsPolish.Apply(
            st, sched,
            new PersonalBalanceJointLnsPolish.Config(MaxMillis: 2000L, MaxRestarts: 2, MaxDepth: 3));
        var after = UnifiedViolationChecker.Check(st, outp.NewSchedule);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("low", -1));
        Assert.Equal(0, after.Hard);
        Assert.True(outp.Applied > 0, "何らかの手が採用されている");
        Assert.True(after.Total < before.Total, "totalが真に改善する");
    }

    [Fact]
    public void IsNoOpWhenNoRangeOrAptConfigured()
    {
        var shifts = new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G", "G") };
        var staffList = new List<Staff> { new("a", 0) };
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-03",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1, 0 } },
            wishes: new Dictionary<string, int>(), staffRange: new Dictionary<string, Range>(),
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var sched = st.Schedule.ToIntArray2D();
        var outp = PersonalBalanceJointLnsPolish.Apply(st, sched);
        Assert.Equal(0, outp.Applied);
    }
}
