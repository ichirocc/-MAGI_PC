using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, C3Run（単一シフト連の玉突き）研磨] <see cref="V6HotfixPasses.ApplyC3RunPolish"/> の検証。
///
/// [Kotlin原本] <c>C3RunPolishTest.kt</c>の2件を移植:
///  - <c>c3RunPolishResolvesViaChainWhenDirectSelfExtensionWouldCreateCovU</c>→
///    <see cref="ResolvesViaChainWhenDirectSelfExtensionWouldCreateCovU"/>。
///  - <c>c3RunPolishIsNoOpWhenNoSingleShiftRules</c>→<see cref="IsNoOpWhenNoSingleShiftRules"/>。
///
/// [C3RunPolish・玉突き連鎖の横展開その3] grilling不要(C3mnPolish/RangePolishと同型、ユーザー承認2026-07-19)で
/// 実装したcons3/cons3m(単一シフト連=run-deficitモデル)専用研磨の検証。
/// A(職員)がshift Xの2連続(cons3 "X,X")を1件しか持たず(run長1&lt;L=2)deficit。Yは全日need1=1で
/// day0=B・day1=Aがそれぞれ単独充足しているため、Aが隣接日(day1)を自身でXへ拡張するだけでは
/// day1のYの被覆が欠け、直接の自己修正は成立しない。B(day1はZに在勤=需要なし)が玉突きで
/// day1のYを補充することで初めて解消する局面。
/// </summary>
public class V6HotfixPassesC3RunTest
{
    private static MagiState ChainState()
    {
        // shift: 0=休(need無) 1=X(need無、連続させたい対象) 2=Y(need1=1、全日担保が必要) 3=Z(need無)
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("X", "X", "", ""),
            new("Y", "Y", "1", ""),
            new("Z", "Z", "", ""),
        };
        var groups = new List<Group> { new("GA", "GA"), new("GB", "GB") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0 }, // GA(A)=休,X,Y
            new List<int> { 1, 0, 1, 1 }, // GB(B)=休,Y,Z
        };
        var staff = new List<Staff> { new("A", 0), new("B", 1) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 2 }, // A = X, Y （Xの連続が1日のみ＝L=2に対しdeficit=1。day1のYはAが単独充足）
            new List<int> { 2, 3 }, // B = Y, Z （day0のYはBが単独充足＝両日ともYの被覆はちょうど満たされている）
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-02",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift,
            groupShiftApt: Enumerable.Range(0, 2)
                .Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("", 4).ToList())
                .ToList(),
            schedule: schedule,
            cons3: new List<C3Row> { new(new List<string> { "X", "X" }) });
    }

    [Fact]
    public void ResolvesViaChainWhenDirectSelfExtensionWouldCreateCovU()
    {
        var st = ChainState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c3", 0) > 0, "初期はc3(run-deficit)違反があること");
        Assert.Equal(0, before.Hard); // 初期はHARD=0(covU無し、AがYを単独充足)

        var result = V6HotfixPasses.ApplyC3RunPolish(st, sched, seed: 1L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3", -1)); // 玉突き適用後はc3=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない(0のまま)
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU", -1)); // Yの被覆(covU)は悪化しない
        Assert.True(result.Applied > 0, "実際に手が採用されている");
    }

    [Fact]
    public void IsNoOpWhenNoSingleShiftRules()
    {
        var st = ChainState() with { Cons3 = new List<C3Row>() };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyC3RunPolish(st, sched, seed: 1L);
        Assert.Equal(0, result.Applied);
    }
}
