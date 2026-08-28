using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.278.0/監査で実証された2クラッシュの回帰テスト, フェーズ6ピース30]
/// <c>PolishRobustnessTest.kt</c>のうち <see cref="V6HotfixPasses.RunPostOptimization"/> および
/// その依存 <see cref="C1RepairIndex"/>/<see cref="C1DeltaPrefilter"/> を直接運動させる2件のみを移植。
///
/// 移植しなかった残り5件の理由:
///  - <c>minCostAssignmentReturnsNullForAllInfRowInsteadOfCrashing</c> →
///    <c>MinCostAssignmentTest.SolveReturnsNullWhenARowIsEntirelyInfeasible</c>等で既に移植済み。
///  - <c>dayAssignmentPolishSkipsInfeasibleDaysInsteadOfCrashing</c> →
///    <c>V6HotfixPassesDayAssignTest.SkipsInfeasibleDaysInsteadOfCrashing</c>で既に移植済み。
///  - <c>emptyStaffIsRejectedWithAnActionableMessage</c> → <c>V6FinalPort.handleOptimize</c> に依存し、
///    それは未移植（フェーズ7「V6FinalPort統括」のスコープ）。
///  - <c>tuningCountersDoNotLoseIncrementsUnderParallelWorkers</c>/
///    <c>tuningSummaryDistinguishesOffFromOnWithNoObservedEffect</c> → <c>TuningTelemetry</c> は
///    <c>RunPostOptimization</c>本体から一切参照されない（Kotlin原本のgrepで確認済み）ため対象外。
///
///  1. MinCostAssignment: 全INF行（担当可否ゼロの群の職員・-1センチネル等）で j1=-1 のまま p[-1] を読み
///     AIOOBE でプロセスごと落ちていた（handleOptimize フル経路=12秒予算で再現済み。ポテンシャル v[j] は
///     単調非増加のため全INF行では厳密更新が構造的に一度も起きない＝決定的クラッシュ）。C# 側はこの修正
///     （<c>MinCostAssignment.Solve</c> の nullable 化）を既に引き継いだ状態で移植済み。
///  2. C1RepairAnalysis.Opportunities: -1 セルを無検証で covUCell(-1,…)＝need1[-1][j] へ渡し AIOOBE
///     （hasActionableC1 ゲート／applyC1IndexChainRepair の両経路で再現済み・3.270.0 と同型の取り残し）。
///     C# 側は<see cref="C1RepairAnalysis"/>で <c>old &gt;= 0 &amp;&amp; old &lt; p.K</c> ガードとして
///     既に修正済み（本テストはその回帰を固定する）。
/// </summary>
public class PolishRobustnessTest
{
    /// <summary>G1 = 担当可否が1つもチェックされていない群（正規のエディタ操作で作れるデータ）。</summary>
    private static MagiState EmptyBucketState()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "1", "") };
        return new MagiState(
            StartDate: "2026-01-01", EndDate: "2026-01-03",
            Shifts: shifts, Groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            StaffList: new List<Staff> { new("s0", 0), new("s1", 1) }, Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0, 0 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" }, new List<string> { "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 0, 1 }, new List<int> { 0, 1, 0 } },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    /// <summary>範囲外セル(99)入り＝normalizeSchedule が -1 センチネルへ写像する盤面。c1不足窓が -1 日を含む。</summary>
    private static MagiState Neg1CellState()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "1", "") };
        return new MagiState(
            StartDate: "2026-01-01", EndDate: "2026-01-03",
            Shifts: shifts, Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0) }, Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 2, 99, 2 } },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row> { new("3", "X", "1") }, Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    [Fact]
    public void RunPostOptimizationSurvivesEmptyBucketStaff()
    {
        // 旧: applyDayAssignmentPolish 経由で runPostOptimization 全体（=handleOptimize 本番経路）が
        //   決定的にクラッシュしていた（12秒予算のフル経路で再現済み）。
        var s = EmptyBucketState();
        var res = V6HotfixPasses.RunPostOptimization(s, s.Schedule.ToIntArray2D(), algoName: "test", seed: 1L);
        var rep = UnifiedViolationChecker.Check(s, res.Schedule);
        Assert.True(rep.Total >= 0, "完走しレポートが得られる");
    }

    [Fact]
    public void C1IndexGateAndRepairSurviveNeg1Cells()
    {
        // 旧: opportunities が -1 セルで covUCell(-1,…)＝need1[-1][j] を読み、hasActionableC1 ゲートと
        //   applyC1IndexChainRepair の両方が AIOOBE。修正後は除去項0として扱い完走する。
        var s = Neg1CellState();
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(-1, ScheduleUtil.NormalizeSchedule(sc, p)[0][1]); // 前提: 99 は -1 に正規化される
        var index = C1RepairIndex.Build(p, sc);
        Assert.True(C1DeltaPrefilter.HasActionableC1(index), "不足窓は正しく検出される（クラッシュせず）");
        var res = V6HotfixPasses.ApplyC1IndexChainRepair(s, sc);
        Assert.True(res.AfterTotal >= 0, "index駆動修復も完走する");
    }
}
