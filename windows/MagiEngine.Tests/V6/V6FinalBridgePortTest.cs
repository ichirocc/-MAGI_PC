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
/// [フェーズ7ピース6, 追加移植] <c>algorithmLabelsMatchWebThresholds</c> を追加した——当時未移植だった
/// <c>V6FinalPort.GetAlgorithmLabel</c>/<c>GetOptimizationPlan</c> が
/// <c>V6FinalPort.AlgorithmPlan.cs</c>としてピース6で移植されたため。
///
/// 移植しなかった残り8件の理由（いずれも <c>RunPostOptimization</c> 以外の既に移植済みの部品を対象とする）:
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

    /// <summary>Faithful port of Kotlin's <c>algorithmLabelsMatchWebThresholds</c>.</summary>
    [Fact]
    public void AlgorithmLabelsMatchWebThresholds()
    {
        // [3.128.0] 31〜210s は複合（RSI違反集中→ALNS研磨）に統一（実機指摘: 60s が ALNS 単発だった）。
        // [3.266.0] 211s〜は異種並列ポートフォリオ（PORTFOLIO、300超は拡張）。
        Assert.Equal("v5", V6FinalPort.GetAlgorithmLabel(10).Tech);
        Assert.Equal("v5", V6FinalPort.GetAlgorithmLabel(30).Tech);
        Assert.Equal("RSI→ALNS", V6FinalPort.GetAlgorithmLabel(60).Tech);
        Assert.Equal("RSI→ALNS", V6FinalPort.GetAlgorithmLabel(90).Tech);
        Assert.Equal("RSI→ALNS", V6FinalPort.GetAlgorithmLabel(180).Tech);
        Assert.Equal("PORTFOLIO", V6FinalPort.GetAlgorithmLabel(300).Tech);
        Assert.Equal("PORTFOLIO拡張", V6FinalPort.GetAlgorithmLabel(600).Tech);
    }

    /// <summary>
    /// [フェーズ7ピース6, 新規テスト] <c>optimizationPlan</c> には対応する Kotlin テストが存在しない
    /// （<c>grep -rn "optimizationPlan(" app/src/test/</c> で確認済み、0件）。この C# 移植で最も
    /// リスクの高い2つの判断——(a) <c>sealed class</c>+<c>data class</c>変種を<c>abstract record</c>+
    /// 入れ子<c>sealed record</c>で表現したこと、(b) <c>optimizationPlan</c>→<see cref="V6FinalPort.
    /// GetOptimizationPlan"/> への機械的改名（型名<c>OptimizationPlan</c>との衝突回避）——を実際に
    /// 検証する既存のオラクルが無いため、Kotlin原本の分岐（<c>V6FinalPort.kt:99-116</c>を直接読んで
    /// 転記）をそのまま境界値で固定する新規テストとして追加した。
    /// </summary>
    [Fact]
    public void OptimizationPlanMatchesKotlinThresholdsAndRsiSplit()
    {
        // 下限クランプ: 0 以下は 1 秒として扱う（V5(1)）。
        Assert.Equal(new V6FinalPort.OptimizationPlan.V5(1), V6FinalPort.GetOptimizationPlan(0));
        Assert.Equal(new V6FinalPort.OptimizationPlan.V5(1), V6FinalPort.GetOptimizationPlan(-5));

        // <= 30 秒: V5 単発。
        Assert.Equal(new V6FinalPort.OptimizationPlan.V5(1), V6FinalPort.GetOptimizationPlan(1));
        Assert.Equal(new V6FinalPort.OptimizationPlan.V5(30), V6FinalPort.GetOptimizationPlan(30));

        // 31〜210 秒: RSI(2/3)→ALNS(1/3)、整数除算の切り捨てを含め Kotlin と一致すること。
        Assert.Equal(new V6FinalPort.OptimizationPlan.RSIThenALNS(20, 11, 2), V6FinalPort.GetOptimizationPlan(31));
        Assert.Equal(new V6FinalPort.OptimizationPlan.RSIThenALNS(40, 20, 2), V6FinalPort.GetOptimizationPlan(60));
        Assert.Equal(new V6FinalPort.OptimizationPlan.RSIThenALNS(140, 70, 2), V6FinalPort.GetOptimizationPlan(210));

        // 211 秒以上: Portfolio（拡張予算でも変種は変わらず秒数だけが伸びる）。
        Assert.Equal(new V6FinalPort.OptimizationPlan.Portfolio(211), V6FinalPort.GetOptimizationPlan(211));
        Assert.Equal(new V6FinalPort.OptimizationPlan.Portfolio(300), V6FinalPort.GetOptimizationPlan(300));
        Assert.Equal(new V6FinalPort.OptimizationPlan.Portfolio(600), V6FinalPort.GetOptimizationPlan(600));
    }

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
