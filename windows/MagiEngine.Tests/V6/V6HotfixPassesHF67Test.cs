using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース23] <see cref="V6HotfixPasses.ApplyHF67InterStaffSwap"/> の検証。
///
/// 移植元: <c>Hf67DeadlineTest.kt</c>の2件（3.282.0/新領域ログ監査＝専用締切deadlineMsの是正）。
///  - <c>expiredDeadlineReturnsInputUnchangedWithoutScanning</c>→
///    <see cref="ExpiredDeadlineReturnsInputUnchangedWithoutScanning"/>
///  - <c>defaultDeadlineKeepsLegacyBehavior</c>→<see cref="DefaultDeadlineKeepsLegacyBehavior"/>
/// </summary>
public class V6HotfixPassesHF67Test
{
    // s0 は A 過多(下限1上限1に対し3)・s1 は A 不足(0<1) ＝ 主スキャンに実際の低/高ペアがある盤面。
    private static MagiState St() => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-04",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0 },
            new List<int> { 0, 0, 0, 2 },
        },
        staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new Range("1", "1"),
            ["1,1"] = new Range("1", "1"),
        });

    [Fact]
    public void ExpiredDeadlineReturnsInputUnchangedWithoutScanning()
    {
        var s = St();
        var sched = s.Schedule.ToIntArray2D();
        // 締切が既に過ぎている → 主スキャンもフォールバック総当たりも走らず即 return（keep-best＝入力維持）。
        var r = V6HotfixPasses.ApplyHF67InterStaffSwap(s, sched, maxSwaps: 30, deadlineMs: 0L);
        Assert.Equal(0, r.SwapsApplied); // 締切超過では1手も適用しない
        Assert.Equal(0, r.SwapsRollback); // フォールバック総当たりも起動しない（rollback=0）
        var norm = ScheduleUtil.NormalizeSchedule(sched, new Problem(s));
        for (var i = 0; i < r.NewSchedule.Length; i++)
        {
            Assert.Equal(norm[i], r.NewSchedule[i]); // 盤面は入力(正規化後)と同一
        }
    }

    [Fact]
    public void DefaultDeadlineKeepsLegacyBehavior()
    {
        var s = St();
        var sched = s.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(s, sched);
        // 既定(deadline=long.MaxValue)は従来どおり動く＝low/high ペアの改善スワップを見つけられる。
        var r = V6HotfixPasses.ApplyHF67InterStaffSwap(s, sched, maxSwaps: 30);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(after.Total <= before.Total, "従来経路は退化しない");
        Assert.True(r.SwapsApplied > 0, "この盤面では改善スワップが実際に見つかる");
    }
}
