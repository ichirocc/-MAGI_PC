using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, C3mn（回避パターン）研磨] <see cref="V6HotfixPasses.ApplyC3mnPolish"/> の検証。
///
/// [Kotlin原本] <c>C3mnPolishTest.kt</c>の2件を移植:
///  - <c>c3mnPolishResolvesViaChainWhenDirectSelfChangeWouldCreateCovU</c>→
///    <see cref="ResolvesViaChainWhenDirectSelfChangeWouldCreateCovU"/>。
///  - <c>c3mnPolishIsNoOpWhenNoCons3mn</c>→<see cref="IsNoOpWhenNoCons3mn"/>。
///
/// [C3mnPolish・玉突き連鎖の横展開] grilling(2026-07-19)で確定した仕様の検証。
/// 金沢勇輝の実例（Dﾃ4連続、cons3n禁止で直接候補が全滅）を最小盤面で再現する:
/// A(職員)が cons3mn "X,X" 回避パターンに触れている。A自身がその日を別シフトへ動かすだけでは
/// Xの被覆(need1=1)が欠けるため、直接の自己修正は成立しない。B(在勤中のZ、需要なし=いつでも
/// 動かせる)がXへ玉突きで補充することで初めて解消する局面。
/// </summary>
public class V6HotfixPassesC3mnTest
{
    /// <summary>
    /// shift: 0=休(need無) 1=X(need1=1) 2=Y(need無) 3=Z(need無)。
    /// GA(A)=休,X,Y / GB(B)=休,X,Z。A=[X,X]（cons3mn "X,X" にヒット）、B=[Z,Z]（需要なし=いつでも
    /// 動かせる）。<c>MinimalState.Build</c>の既定（<c>groupShiftApt</c>=空文字埋め・希望/回数/
    /// 必要人数例外なし・cons1/cons2/cons3/cons3n/cons3m/cons41/cons42=空）をそのまま使う。
    /// </summary>
    private static MagiState ChainState()
    {
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
            new List<int> { 1, 1, 1, 0 }, // GA(A) = 休,X,Y
            new List<int> { 1, 1, 0, 1 }, // GB(B) = 休,X,Z
        };
        var staff = new List<Staff> { new("A", 0), new("B", 1) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1 }, // A = X, X （cons3mn "X,X" にヒット）
            new List<int> { 3, 3 }, // B = Z, Z （需要なし=いつでも動かせる）
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-02",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift,
            schedule: schedule,
            cons3mn: new List<C3Row> { new(new List<string> { "X", "X" }) });
    }

    [Fact]
    public void ResolvesViaChainWhenDirectSelfChangeWouldCreateCovU()
    {
        var st = ChainState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c3mn", 0) > 0, "初期はc3mn違反があること");
        Assert.Equal(0, before.Hard); // 初期はHARD=0(covU無し、AがXを単独充足)

        var result = V6HotfixPasses.ApplyC3mnPolish(st, sched, seed: 1L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3mn", -1)); // 玉突き適用後はc3mn=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない(0のまま)
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU", -1)); // Xの被覆(covU)は悪化しない
        Assert.True(result.Applied > 0, "実際に手が採用されている");
    }

    [Fact]
    public void IsNoOpWhenNoCons3mn()
    {
        var st = ChainState() with { Cons3mn = new List<C3Row>() };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyC3mnPolish(st, sched, seed: 1L);
        Assert.Equal(0, result.Applied);
    }
}
