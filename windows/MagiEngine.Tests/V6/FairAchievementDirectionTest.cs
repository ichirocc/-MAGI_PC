using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.590.0/backlog#27①, Kotlin原本 <c>FairAchievementDirectionTest</c>]
/// <see cref="V6HotfixPasses.PostOptimizationParams.FairAchievementDirection"/>（既定OFF）を検証する。
/// OFF時は FairTarget の生回数round(平均)がたまたま一致するセルを候補生成から取りこぼす
/// （3.588.0実測）。ON時は FairDevOfBucket の黒箱観測(±1)へ分類を揃え、取りこぼしを解消する。
/// </summary>
public class FairAchievementDirectionTest
{
    // 3.588.0で実測したsept2026 g=0,k=0,x=2の構図を再現: 範囲[7,9]/[7,9]/[3,10]・回数7/8/9。
    // 生回数平均は(7+8+9)/3=8=中央の職員(idx1)の回数と一致し「match」判定で候補生成が握り潰される。
    private static MagiState ThreeMemberState()
    {
        var shifts = new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("X", "X", "", "") };
        const int t = 9;
        var counts = new[] { 7, 8, 9 };
        var schedule = Enumerable.Range(0, 3)
            .Select(i => (IReadOnlyList<int>)Enumerable.Range(0, t).Select(j => j < counts[i] ? 1 : 0).ToList())
            .ToList();
        var sr = new Dictionary<string, MagiEngine.Model.Range> { ["0,1"] = new("7", "9"), ["1,1"] = new("7", "9"), ["2,1"] = new("3", "10") };
        return MinimalState.Build(
            startDate: "2026-10-01", endDate: "2026-10-09",
            shifts: shifts, groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, 3).Select(i => new Staff($"S{i}", 0)).ToList(),
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: schedule, staffRange: sr);
    }

    // idx1(中央の職員、回数8)自身の回数だけを見る＝OFF時は生回数平均(8)と一致するため候補生成が
    // 一切触れないが、idx0/idx2(生回数平均でも高低が一致する側)はOFFでも動く（実測: applied=3）。
    // 「OFFが何もしない」でなく「OFFはidx1だけを取りこぼす」ことを検証する。
    private static int XCountOfMiddleStaff(MagiState st, int[][] schedule)
    {
        var p = new Problem(st);
        return ScheduleUtil.CountMatrix(p, schedule)[1][1];
    }

    [Fact]
    public void OffLeavesTheMatchedCellUntouched()
    {
        var st = ThreeMemberState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();

        // maxPasses=1で固定する: 複数パス回すとidx0/idx2の交換でcountsが動きidx1の平均一致が
        // 偶然崩れて次パスで拾われてしまい、分類漏れそのもの(1パス内の欠陥)を隠してしまう。
        var result = V6HotfixPasses.ApplyFairPolish(st, sched, maxPasses: 1, fairAchievementDirection: false);
        Assert.Equal(8, XCountOfMiddleStaff(st, result.NewSchedule));
    }

    [Fact]
    public void OnGeneratesCandidatesForTheMatchedCell()
    {
        var st = ThreeMemberState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);

        var result = V6HotfixPasses.ApplyFairPolish(st, sched, maxPasses: 1, fairAchievementDirection: true);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.NotEqual(8, XCountOfMiddleStaff(st, result.NewSchedule));
        Assert.True(after.Breakdown.GetValueOrDefault("fair", 0) <= before.Breakdown.GetValueOrDefault("fair", 0));
        Assert.Equal(0, after.Hard);
    }
}
