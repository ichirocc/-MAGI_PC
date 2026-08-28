using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース21/22] <see cref="V6HotfixPasses.ApplyDayAssignmentPolish"/> と
/// <see cref="V6HotfixPasses.ApplyAlternatingSoftPolish"/> の検証。
///
/// 移植元:
///  - <c>PolishRobustnessTest.kt</c>の<c>dayAssignmentPolishSkipsInfeasibleDaysInsteadOfCrashing</c>→
///    <see cref="SkipsInfeasibleDaysInsteadOfCrashing"/>（3.278.0 クラッシュ回帰の直接検証。
///    <c>minCostAssignmentReturnsNullForAllInfRowInsteadOfCrashing</c>は
///    <c>MinCostAssignmentTest.cs</c>で既に別途カバー済みのため対象外）。
///  - <c>WeeklyRebalancePolishTest.kt</c>の
///    <c>alternatingOptimizationReducesWeeklyViaPerDayReassignment</c>→
///    <see cref="AlternatingOptimizationReducesWeeklyViaPerDayReassignment"/>、
///    <c>alternatingOptimizationIsNoOpWhenAlreadyOptimal</c>→
///    <see cref="AlternatingOptimizationIsNoOpWhenAlreadyOptimal"/>
///    （同ファイルの<c>weeklyRebalance*</c>2件は<c>V6HotfixPassesCyclicSwapTest.cs</c>で既に移植済み）。
///
/// 未移植（範囲外）: <c>V6FinalBridgePortTest.kt</c>の<c>dayAssignmentPolishNeverWorsens</c>は共有
/// フィクスチャ基盤（<c>sampleState()</c>/<c>notWorseThan()</c>）が未移植のため、
/// <c>V6HotfixPassesC1WindowTest.cs</c>と同じ理由で据え置き。
/// </summary>
public class V6HotfixPassesDayAssignTest
{
    /// <summary>G1 = 担当可否が1つもチェックされていない群（正規のエディタ操作で作れるデータ）。</summary>
    private static MagiState EmptyBucketState() => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-03",
        shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "1", "") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 1) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0, 0 } },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 0, 1 },
            new List<int> { 0, 1, 0 },
        });

    [Fact]
    public void SkipsInfeasibleDaysInsteadOfCrashing()
    {
        // 空bucket職員(s1)の行は全列 INF → 旧実装は Hungarian 内で AIOOBE。新実装は null→その日 skip の no-op。
        var s = EmptyBucketState();
        var p = new Problem(s);
        var res = V6HotfixPasses.ApplyDayAssignmentPolish(s, s.Schedule.ToIntArray2D());
        var norm = ScheduleUtil.NormalizeSchedule(s.Schedule.ToIntArray2D(), p);
        for (var i = 0; i < res.NewSchedule.Length; i++)
        {
            Assert.Equal(norm[i], res.NewSchedule[i]); // 盤面は不変（全日が実行可能な割当なし=skip）
        }
    }

    // AO(日ブロック再配置)用: WeeklyState と同じ勤務パターンだが A/B を別々の単独グループに置く。
    // 交互最適化は「その日の休を誰に割り当てるか」で各職員の勤務総数を変えるため、同一グループだと
    // weekly の改善が fair(群内シフト回数)の悪化と 1:1 で相殺され採用されない。単独グループ(メンバー<2)は
    // fair の対象外のため、weekly のみが目的関数に効く純粋な検証になる。
    private static MagiState WeeklyStateSeparateGroups() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-14",
        shifts: new List<Shift> { new("休", "休", "", ""), new("W", "W", "1", "") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        staffList: new List<Staff> { new("A", 0), new("B", 1) }, // A∈G0, B∈G1（各単独＝fair対象外）
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0 }, // A: {0,1,2,3,7,8,9}
            new List<int> { 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1 }, // B: 残り
        });

    [Fact]
    public void AlternatingOptimizationReducesWeeklyViaPerDayReassignment()
    {
        // 交互最適化(日ブロックの最小費用割当・weekly込み)が、被覆保存の同日再配置で weekly を下げること。
        // 2職員・14日・各日 {W, 休} の1枠ずつ＝各日どちらが働くかを日ブロックで最適に決め直せる。
        // A/B は別グループ(単独)＝fair 対象外なので weekly のみが効く純検証。
        var st = WeeklyStateSeparateGroups();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, before.Hard); // 初期 HARD=0
        Assert.True(before.Breakdown.GetValueOrDefault("weekly", 0) > 0, "初期 weekly>0");

        var res = V6HotfixPasses.ApplyAlternatingSoftPolish(st, sched);
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);

        Assert.True(res.AppliedDays > 0, "交互最適化で1日以上採用");
        Assert.True(after.Breakdown.GetValueOrDefault("weekly", 0) < before.Breakdown.GetValueOrDefault("weekly", 0), "weekly が減少");
        Assert.True(after.Total <= before.Total, "total 非悪化(keep-best)");
        Assert.Equal(0, after.Hard); // HARD 不変(=0)
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU", 0)); // 被覆保存: covU=0
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covO", 0)); // 被覆保存: covO=0
    }

    [Fact]
    public void AlternatingOptimizationIsNoOpWhenAlreadyOptimal()
    {
        // weekly=0(A が各曜日ちょうど1回勤務)・A/B は別グループ(単独=fair対象外)。どの日を入替えても
        // weekly が増える(改善余地なし)ため交互最適化は1日も採用しない(no-op)。
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-07",
            shifts: new List<Shift> { new("休", "休", "", ""), new("W", "W", "1", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: new List<Staff> { new("A", 0), new("B", 1) },
            schedule: new List<IReadOnlyList<int>>
            {
                new List<int> { 1, 1, 1, 1, 1, 1, 1 },
                new List<int> { 0, 0, 0, 0, 0, 0, 0 },
            });
        var sched = st.Schedule.ToIntArray2D();
        var res = V6HotfixPasses.ApplyAlternatingSoftPolish(st, sched);
        Assert.Equal(0, res.AppliedDays); // 均等配置では採用0(no-op)
    }
}
