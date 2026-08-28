using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース28] <see cref="V6HotfixPasses.ApplyC1IndexChainRepair"/> の検証。
///
/// 移植元Kotlinテスト: <c>C1RepairOperatorsTest.kt</c> の3件（<c>indexChainRepairReducesC1ViaDirectMove</c>/
/// <c>indexChainRepairFillsHoleViaChain</c>/<c>indexChainRepairIsNoOpWhenNoC1</c>）。同ファイルの残り2件
/// （<c>indexChainRepairDelegatesIdentically</c>/<c>gateIsSafeOnC1CleanBoard</c>）は
/// <c>C1RepairOperators</c>（façade、ピース29予定）に依存するため、その移植時に別ファイルへ追加する。
/// </summary>
public class V6HotfixPassesC1IndexChainTest
{
    private static MagiState St(
        int days, int staff, IReadOnlyList<IReadOnlyList<int>> sched, IReadOnlyList<C1Row> cons1) =>
        MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-" + days.ToString("D2"),
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, staff).Select(i => new Staff($"s{i}", 0)).ToList(),
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: sched, cons1: cons1);

    [Fact]
    public void IndexChainRepairReducesC1ViaDirectMove()
    {
        // s0=[Y,Y], ルール「X 2日窓≥1」, 被覆要件なし → 直接移動 Y→X で c1 1→0（他族悪化なし）。
        var s = St(2, 1, new List<IReadOnlyList<int>> { new List<int> { 2, 2 } },
            new List<C1Row> { new("2", "X", "1") });
        var sc = s.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(s, sc);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c1"));
        var res = V6HotfixPasses.ApplyC1IndexChainRepair(s, sc);
        var after = UnifiedViolationChecker.Check(s, res.NewSchedule);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c1"));
        Assert.True(after.Hard <= before.Hard, "HARD非悪化");
        Assert.True(after.Total <= before.Total, "total非悪化");
        var echo = s.Schedule.ToIntArray2D();
        for (var i = 0; i < sc.Length; i++) Assert.Equal(echo[i], sc[i]); // 入力配列は不変
    }

    [Fact]
    public void IndexChainRepairFillsHoleViaChain()
    {
        // 被覆要件 X=1・Y=1/日。s0=[Y,Y]・s1=[X,X]。ルール「X 2日窓≥1」で s0 が不足。
        //   直接移動 s0:Y→X は Y@day0 に covU 穴（Y需要1）を作り却下 → findCovUChain が s1:X→Y で埋め直し採用。
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "1", ""), new("Y", "Y", "1", "") };
        var s = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-02",
            shifts: shifts, groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("s0", 0), new("s1", 0) },
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 2, 2 }, new List<int> { 1, 1 } },
            cons1: new List<C1Row> { new("2", "X", "1") });
        var sc = s.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(s, sc);
        Assert.True(before.Breakdown.GetValueOrDefault("c1") >= 1, "s0がc1不足");
        var res = V6HotfixPasses.ApplyC1IndexChainRepair(s, sc);
        var after = UnifiedViolationChecker.Check(s, res.NewSchedule);
        Assert.True(after.Breakdown.GetValueOrDefault("c1") < before.Breakdown.GetValueOrDefault("c1"), "c1改善");
        Assert.True(after.Hard <= before.Hard, "HARD非悪化(covU穴を連鎖で埋めた)");
        Assert.True(res.Applied >= 1, "採用あり");
    }

    [Fact]
    public void IndexChainRepairIsNoOpWhenNoC1()
    {
        var s = St(3, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            new List<C1Row> { new("2", "X", "1") });
        var res = V6HotfixPasses.ApplyC1IndexChainRepair(s, s.Schedule.ToIntArray2D());
        Assert.Equal(0, res.Applied);
    }
}
