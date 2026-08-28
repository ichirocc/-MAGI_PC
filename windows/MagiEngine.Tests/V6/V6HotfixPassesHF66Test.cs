using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース24] <see cref="V6HotfixPasses.ApplyHF66IntraStaffRedistribution"/> の検証。
///
/// 移植元Kotlinテスト無し（<c>grep -rln "applyHF66IntraStaffRedistribution\|HF66Result\|
/// IntraStaffRedistribution" app/src/test</c> は0件・3通りの検索パターンで確認済み）。呼出は
/// <c>runPostOptimization</c> 経由の統合検証のみで、その基盤（<c>V6FinalBridgePortTest.kt</c>の
/// <c>sampleState()</c>/<c>notWorseThan()</c>）は他ピースと同じ理由で未移植のまま据え置き。
/// ここでは兄弟 HF67(<c>Hf67DeadlineTest.kt</c>)と同型の最小盤面で、他の全ピースが共有する普遍的な
/// 不変条件（keep-best・自明ケースでのno-op・専用締切）を独自に固定する。
/// </summary>
public class V6HotfixPassesHF66Test
{
    // 単一職員s0が休/A/Bすべて担当可能。A過多(下限0上限1に対し3)・B不足(下限1上限1に対し0)＝
    // 同一職員内でA→Bへ付け替える手が実際にある盤面。need/cons41は未設定＝covU/covOはこの検証では発火しない。
    private static MagiState St() => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-04",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0 } },
        staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new Range("0", "1"),
            ["0,2"] = new Range("1", "1"),
        });

    [Fact]
    public void ResolvesLowHighPairViaIntraStaffMove()
    {
        var s = St();
        var sched = s.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(s, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("high", 0) > 0, "初期はA過多(high)がある");
        Assert.True(before.Breakdown.GetValueOrDefault("low", 0) > 0, "初期はB不足(low)がある");
        Assert.Equal(0, before.Hard);

        var r = V6HotfixPasses.ApplyHF66IntraStaffRedistribution(s, sched);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);

        Assert.True(r.MovesApplied > 0, "少なくとも1手は採用される");
        Assert.True(after.Total < before.Total, "totalは真に改善する(退化しない)");
        Assert.Equal(0, after.Hard); // HARDは不変(=0)のまま
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU", 0)); // 被覆要件未設定＝covUは発火しない
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covO", 0)); // 同上covO
    }

    [Fact]
    public void IsNoOpWhenAlreadyBalanced()
    {
        // A=1(下限0上限1に一致)・B=1(下限1上限1に一致)＝すでに理想通り。動かす理由がない。
        var s = MinimalState.Build(
            startDate: "2026-06-01", endDate: "2026-06-02",
            shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("s0", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 2 } },
            staffRange: new Dictionary<string, Range>
            {
                ["0,1"] = new Range("0", "1"),
                ["0,2"] = new Range("1", "1"),
            });
        var sched = s.Schedule.ToIntArray2D();
        var r = V6HotfixPasses.ApplyHF66IntraStaffRedistribution(s, sched);
        Assert.Equal(0, r.MovesApplied); // 均衡済み配置では採用0(no-op)
    }

    [Fact]
    public void ExpiredDeadlineReturnsInputUnchangedWithoutScanning()
    {
        var s = St();
        var sched = s.Schedule.ToIntArray2D();
        // 締切が既に過ぎている → 主スキャンもフォールバック総当たりも走らず即 return（keep-best＝入力維持）。
        var r = V6HotfixPasses.ApplyHF66IntraStaffRedistribution(s, sched, maxMoves: 30, deadlineMs: 0L);
        Assert.Equal(0, r.MovesApplied); // 締切超過では1手も適用しない
        Assert.Equal(0, r.MovesRollback); // フォールバック総当たりも起動しない（rollback=0）
        var norm = ScheduleUtil.NormalizeSchedule(sched, new Problem(s));
        for (var i = 0; i < r.NewSchedule.Length; i++)
        {
            Assert.Equal(norm[i], r.NewSchedule[i]); // 盤面は入力(正規化後)と同一
        }
    }
}
