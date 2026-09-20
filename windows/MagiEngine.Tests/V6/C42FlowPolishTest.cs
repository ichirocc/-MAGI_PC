using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [Kotlin原本 <c>C42FlowPolishTest</c>] C42FlowPolish の検証。群ペア禁止(c42)違反を、片側固定の
/// ヤコビ近似(g1側/g2側の対称2試行)で min-cost-flow 再配分して解消する。テスト設計の教訓
/// （C41FlowPolishTest/C2PolishTest と同型）: 群の人数が奇数だと、可動メンバーを寄せる手が群内の
/// fair（公平化）を新規に悪化させ相殺されて却下される。両群とも人数=2で回避した。
/// </summary>
public class C42FlowPolishTest
{
    private static MagiState State(IReadOnlyDictionary<string, int>? wishes = null) => MinimalState.Build(
        startDate: "2026-02-01", endDate: "2026-02-01",
        shifts: new List<Shift> { new("休み", "休", "", ""), new("P", "P", "", ""), new("Q", "Q", "", "") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        staffList: new List<Staff> { new("A", 0), new("B", 0), new("C", 1), new("D", 1) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" }, new List<string> { "", "", "" } },
        // A=P、B=Q（G0側）／ C=Q、D=休（G1側）。G0のPとG1のQが同日併存＝禁止ペア1件(n1=1,n2=1)。
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 2 }, new List<int> { 2 }, new List<int> { 0 } },
        wishes: wishes ?? new Dictionary<string, int>(),
        cons42: new List<C42Row> { new(G1Kigou: "G0", G2Kigou: "G1", S1Kigou: "P", S2Kigou: "Q") });

    [Fact]
    public void ResolvesTheGroupPairViolationByReassigningWithinTheGroups()
    {
        var st = State();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c42", 0));
        var res = C42FlowPolish.ApplyC42FlowPolish(st, sched.Select(r => (int[])r.Clone()).ToArray());
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c42", 0));
        Assert.True(res.Applied > 0);
        Assert.True(after.Hard <= before.Hard);
        Assert.True(after.WeightedScore <= before.WeightedScore);
    }

    [Fact]
    public void NoMovableMemberLeavesTheBoardUntouched()
    {
        // 全員希望固定＝両側とも可動メンバーが0人。何も変えない。
        var st = State(new Dictionary<string, int> { ["0,0"] = 1, ["1,0"] = 2, ["2,0"] = 2, ["3,0"] = 0 });
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var res = C42FlowPolish.ApplyC42FlowPolish(st, sched.Select(r => (int[])r.Clone()).ToArray());
        Assert.Equal(0, res.Applied);
        for (var i = 0; i < 4; i++) Assert.Equal(st.Schedule[i], res.NewSchedule[i].ToList());
    }

    [Fact]
    public void EnablingInTheFullChainResolvesC42WithoutWorseningTheBoard()
    {
        var st = State();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);
        var on = V6HotfixPasses.RunPostOptimization(st, sched.Select(r => (int[])r.Clone()).ToArray(), "t", seed: 7L,
            parameters: new V6HotfixPasses.PostOptimizationParams(Deterministic: true, C42FlowPolishEnabled: true));
        Assert.Equal(0, on.Report.Breakdown.GetValueOrDefault("c42", -1));
        Assert.True(on.Report.Hard <= before.Hard);
        Assert.True(on.Report.WeightedScore <= before.WeightedScore);
    }
}
