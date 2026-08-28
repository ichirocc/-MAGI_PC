using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6ピース30] <c>V6FinalBridgePortTest.kt</c>のうち
/// <see cref="V6HotfixPasses.RunPostOptimization"/> を直接運動させる1件のみを移植。
///
/// 移植しなかった残り9件の理由（いずれも <c>RunPostOptimization</c> 以外の既に移植済みの部品を対象とする）:
///  - <c>algorithmLabelsMatchWebThresholds</c> → <c>V6FinalPort.GetAlgorithmLabel</c> は未移植
///    （フェーズ7「V6FinalPort統括」のスコープ。<c>V6FinalPort.cs</c>のKDocが明記）。
///  - <c>busyDetailAndGateWork</c> → <c>BuildBusyDetail</c>/<c>ConfirmDespiteImpossibleWishes</c> は
///    フェーズ4で既に移植済みだが本体テストは未移植のまま（フェーズ4のスコープ外れ、別途対応）。
///  - <c>elitePathRelinkNeverWorsensBest</c> → <c>V6NativeOptimizerPortfolioTest.cs</c>等で既にカバー。
///  - <c>minCostAssignmentFindsOptimum</c> → <c>MinCostAssignmentTest.cs</c>で既に移植済み。
///  - <c>dayAssignmentPolishNeverWorsens</c>/<c>cyclicSwapPolishNeverWorsens</c>/
///    <c>c1WindowPolishNeverWorsens</c>/<c>c3SequencePolishNeverWorsens</c>/
///    <c>equalizePolishesNeverWorsenMainObjective</c> → 各々のポリッシュパス専用テストファイル
///    （<c>V6HotfixPassesDayAssignTest.cs</c>/<c>V6HotfixPassesCyclicSwapTest.cs</c>/
///    <c>V6HotfixPassesC1WindowTest.cs</c>/<c>V6HotfixPassesFairTest.cs</c>）で既にカバー。
///  - <c>skillGroupConstraintsCount</c> → c41s/c42s の <see cref="UnifiedViolationChecker"/> 挙動は
///    <c>ProblemTest.cs</c>/<c>ParityTest.cs</c>等で既にカバー。
/// </summary>
public class V6FinalBridgePortTest
{
    private static MagiState SampleState() => new(
        StartDate: "2026-06-01",
        EndDate: "2026-06-02",
        Shifts: new List<Shift> { new("日勤", "日", "1", "1"), new("休み", "休", "", "") },
        Groups: new List<Group> { new("A", "A") },
        StaffList: new List<Staff> { new("s1", 0), new("s2", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1 }, new List<int> { 1, 0 } },
        Wishes: new Dictionary<string, int>(),
        StaffRange: new Dictionary<string, Range> { ["0,0"] = new("0", "2") },
        NeedDay1: new Dictionary<string, string>(),
        NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(),
        Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(),
        Cons3n: new List<C3Row>(),
        Cons3m: new List<C3Row>(),
        Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(),
        Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(),
        Cons41s: new List<C41Row>(),
        Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    [Fact]
    public void PostHotfixChainReturnsReport()
    {
        var st = SampleState();
        var post = V6HotfixPasses.RunPostOptimization(st, st.Schedule.ToIntArray2D(), "test");
        Assert.Equal(0, ScheduleAssertions.InvalidAssignmentCount(st, post.Schedule));
        Assert.NotEmpty(post.Logs);
        Assert.Equal(post.Report.Total, UnifiedViolationChecker.Check(st, post.Schedule).Total);
    }
}
