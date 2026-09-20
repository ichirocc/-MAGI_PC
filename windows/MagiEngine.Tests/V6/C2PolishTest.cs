using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.511.3/測定中, Kotlin原本 <c>C2PolishTest</c>] C2Polish の検証。c2 は二値フラグ（不足量 2 以上でも
/// 1 件の違反）のため、1 セルずつの判定では中間手が同点で却下される＝deficit 分をまとめて一括適用しないと
/// 解消できないことを固定する。T は実運用に近い1か月（31日）を使う: 短すぎる期間だと weekly（シフトごとの
/// 曜日別 L1 偏差）が相対的に過敏になり、c2 を直す手が weekly の悪化で相殺されて betterReport に却下される。
/// </summary>
public class C2PolishTest
{
    private static MagiState MonthState(IReadOnlyDictionary<string, int>? wishes = null)
    {
        const int t = 31;
        // P は7日おき(同じ曜日)に5回、残りはQ。cons2でP>=10を要求＝不足5。
        var schedule = new List<IReadOnlyList<int>> { Enumerable.Range(0, t).Select(j => j % 7 == 0 ? 1 : 2).ToList() };
        return MinimalState.Build(
            startDate: "2026-02-01", endDate: "2026-03-03",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("P", "P", "", ""), new("Q", "Q", "", "") },
            groups: new List<Group> { new("GA", "GA") },
            staffList: new List<Staff> { new("A", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: schedule, wishes: wishes ?? new Dictionary<string, int>(),
            cons2: new List<C2Row> { new("P", "10") });
    }

    [Fact]
    public void DeficitOfFiveIsFilledInOneBatchWhenEnoughSafeDaysExist()
    {
        var st = MonthState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c2", 0));
        var res = C2Polish.ApplyC2Polish(st, sched.Select(r => (int[])r.Clone()).ToArray());
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c2", 0));
        Assert.True(res.Applied > 0);
        Assert.True(after.Hard <= before.Hard);
        Assert.True(after.WeightedScore <= before.WeightedScore);
    }

    [Fact]
    public void InsufficientSafeDaysLeavesTheStaffUntouched()
    {
        // day5以降(かつ7日おきのP日を除く)を希望固定にして、安全な変換先をday1..4の4日だけに絞る＝
        // 不足5に届かず全く触らない（一部だけ変換する中途半端な適用はしない設計の確認）。
        var wishes = Enumerable.Range(5, 26).Where(j => j % 7 != 0).ToDictionary(j => $"0,{j}", _ => 2);
        var st = MonthState(wishes);
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var res = C2Polish.ApplyC2Polish(st, sched.Select(r => (int[])r.Clone()).ToArray());
        Assert.Equal(st.Schedule[0], res.NewSchedule[0].ToList());
        Assert.Equal(0, res.Applied);
    }

    [Fact]
    public void EnablingInTheFullChainResolvesC2WithoutWorseningTheBoard()
    {
        var st = MonthState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);
        var on = V6HotfixPasses.RunPostOptimization(st, sched.Select(r => (int[])r.Clone()).ToArray(), "t", seed: 7L,
            parameters: new V6HotfixPasses.PostOptimizationParams(Deterministic: true, C2PolishEnabled: true));
        Assert.Equal(0, on.Report.Breakdown.GetValueOrDefault("c2", -1));
        Assert.True(on.Report.Hard <= before.Hard);
        Assert.True(on.Report.WeightedScore <= before.WeightedScore);
    }
}
