using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, C3Pattern（複数シフトパターンの玉突き）研磨] <see cref="V6HotfixPasses.ApplyC3PatternPolish"/> の検証。
///
/// [Kotlin原本] <c>C3PatternPolishTest.kt</c>の3件を移植:
///  - <c>c3PatternPolishResolvesViaChainWhenDirectSelfChangeWouldCreateCovU</c>→
///    <see cref="ResolvesViaChainWhenDirectSelfChangeWouldCreateCovU"/>。
///  - <c>c3PatternPolishIsNoOpWhenNoMultiShiftRules</c>→<see cref="IsNoOpWhenNoMultiShiftRules"/>。
///  - <c>c3PatternPolishSkipsSingleShiftSequencesAsOutOfScope</c>→
///    <see cref="SkipsSingleShiftSequencesAsOutOfScope"/>。
///
/// [C3PatternPolish] ユーザー指示「c42/c42s以外にも『動かせるか』専用オペレータの欠如が無いか
/// 棚卸しする」で発見。cons3/cons3mの複数シフトMUST/Wantパターン(非single-shift、C3Run.IsSingleShiftSeq
/// が偽)は3.216.0で「既存機構(2者ブロック交換/3者回転)のまま対象外」と明記されスコープ外にされたまま
/// だった。C3mnPolishTest(3.214.0)と同型の最小盤面（need1で唯一の担当者に絞り、直接の自己修正だけでは
/// 被覆が欠ける局面）で検証する。
/// </summary>
public class V6HotfixPassesC3PatternTest
{
    private static MagiState ChainState()
    {
        // shift: 0=休(need無) 1=X(need1=1) 2=Y(need無) 3=Z(need無)
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("X", "X", "1", ""),
            new("Y", "Y", "", ""),
            new("Z", "Z", "", ""),
        };
        var groups = new List<Group> { new("GA", "GA"), new("GB", "GB") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0 }, // GA(A)=休,X,Y
            new List<int> { 1, 1, 1, 1 }, // GB(B)=休,X,Y,Z（Yも担当可＝玉突きでXを埋めても新規発火しないように）
        };
        var staff = new List<Staff> { new("A", 0), new("B", 1) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1 }, // A = X,X（「X→Y」必須パターン: day0=Xの後day1がYでない=未完成で発火）
            new List<int> { 3, 2 }, // B = Z,Y（day1が既にY＝玉突きでday0をXへ埋めても「X→Y」が完成し新規発火しない）
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-02",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift,
            groupShiftApt: Enumerable.Range(0, 2)
                .Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("", 4).ToList())
                .ToList(),
            schedule: schedule,
            cons3: new List<C3Row> { new(new List<string> { "X", "Y" }) });
    }

    [Fact]
    public void ResolvesViaChainWhenDirectSelfChangeWouldCreateCovU()
    {
        var st = ChainState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c3", 0) > 0, "初期はc3違反(未完成パターン)があること");
        Assert.Equal(0, before.Hard); // 初期はHARD=0(covU無し、AがXを単独充足)

        var result = V6HotfixPasses.ApplyC3PatternPolish(st, sched, seed: 1L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3", -1)); // 玉突き適用後はc3=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない(0のまま)
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU", -1)); // Xの被覆(covU)は悪化しない
        Assert.True(result.Applied > 0, "実際に手が採用されている");
    }

    [Fact]
    public void IsNoOpWhenNoMultiShiftRules()
    {
        var st = ChainState() with { Cons3 = new List<C3Row>() };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyC3PatternPolish(st, sched, seed: 1L);
        Assert.Equal(0, result.Applied);
    }

    [Fact]
    public void SkipsSingleShiftSequencesAsOutOfScope()
    {
        // 単一シフト連(run-deficitモデル)はC3RunPolish(3.215.0)の担当。本パスは何もしない(対象外)。
        var st = ChainState() with { Cons3 = new List<C3Row> { new(new List<string> { "X", "X", "X" }) } };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyC3PatternPolish(st, sched, seed: 1L);
        Assert.Equal(0, result.Applied);
    }
}
