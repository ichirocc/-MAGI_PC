using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, 個人回数(low/high)研磨] <see cref="V6HotfixPasses.ApplyRangePolish"/> の検証。
///
/// [Kotlin原本] <c>RangePolishTest.kt</c>の7件を移植:
///  - <c>rangePolishResolvesHighViaChainWhenDirectSelfChangeWouldCreateCovU</c>→
///    <see cref="ResolvesHighViaChainWhenDirectSelfChangeWouldCreateCovU"/>。
///  - <c>rangePolishIsNoOpWhenNoStaffRange</c>→<see cref="IsNoOpWhenNoStaffRange"/>。
///  - <c>rangePolishLogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked</c>→
///    <see cref="LogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked"/>。
///  - <c>rangePolishExactDayMatchingFindsFourPersonCycleWithoutLowReceiver</c>→
///    <see cref="ExactDayMatchingFindsFourPersonCycleWithoutLowReceiver"/>。
///  - <c>rangePolishExactDayMatchingRespectsWishLockedBridge</c>→
///    <see cref="ExactDayMatchingRespectsWishLockedBridge"/>。
///  - <c>worstWorsenedFamilyPicksHeaviestWeightedIncreaseNotLargestCount</c>→
///    <see cref="WorstWorsenedFamilyPicksHeaviestWeightedIncreaseNotLargestCount"/>。
///  - <c>worstWorsenedFamilyReturnsNullWhenNothingGotWorse</c>→
///    <see cref="WorstWorsenedFamilyReturnsNullWhenNothingGotWorse"/>。
///
/// [RangePolish・玉突き連鎖の横展開その2] grilling不要(C3mnPolishと同型、ユーザー承認2026-07-19)で
/// 実装した個人回数(low/high)研磨の検証。桒澤美幸の実例（Aｱ超過・B1担当が全職員中唯一で交換相手が
/// 構造的に存在しない）を最小盤面で再現する: A(職員)がshift X(need1=1)を超過(high)。Aが自身のX保有日を
/// 別シフト(Y)へ動かすだけではXの被覆が欠けるため、直接の自己修正は成立しない。B(在勤中のZ、需要なし=
/// いつでも動かせる)がXへ玉突きで補充することで初めて解消する局面。
/// </summary>
public class V6HotfixPassesRangeTest
{
    // shift: 0=休(need無) 1=X(need1=1) 2=Y(need無、Aの逃げ先) 3=Z(need無、Bの現在地)
    private static MagiState HighState()
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
            new List<int> { 1, 1 }, // A = X, X （高hi=1に対し2回＝超過1）
            new List<int> { 3, 3 }, // B = Z, Z （需要なし=いつでも動かせる）
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-02",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift,
            schedule: schedule,
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("0", "1") }); // A の shift X: lo=0, hi=1
    }

    [Fact]
    public void ResolvesHighViaChainWhenDirectSelfChangeWouldCreateCovU()
    {
        var st = HighState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("high", 0) > 0, "初期はhigh違反があること");
        Assert.Equal(0, before.Hard); // 初期はHARD=0(covU無し、AがXを単独充足)

        var result = V6HotfixPasses.ApplyRangePolish(st, sched, seed: 1L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("high", -1)); // 玉突き適用後はhigh=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない(0のまま)
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU", -1)); // Xの被覆(covU)は悪化しない
        Assert.True(result.Applied > 0, "実際に手が採用されている");
    }

    [Fact]
    public void IsNoOpWhenNoStaffRange()
    {
        var st = HighState() with { StaffRange = new Dictionary<string, Range>() };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyRangePolish(st, sched, seed: 1L);
        Assert.Equal(0, result.Applied);
    }

    // [頭打ちの理由を可視化] Bが両日とも希望固定(Z)だと玉突きの唯一の候補が使えず「候補なし」で
    // 頭打ちする。ログの残存表示にその理由が出ることを固定する。
    [Fact]
    public void LogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked()
    {
        var st = HighState() with { Wishes = new Dictionary<string, int> { ["1,0"] = 3, ["1,1"] = 3 } };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyRangePolish(st, sched, seed: 1L);
        Assert.Equal(0, result.Applied); // 唯一の候補が希望固定のため採用0回
        var msg = result.Logs.First().Message;
        Assert.True(msg.Contains("候補なし"), $"残存表示に候補なしの理由が出ること: {msg}");
        Assert.True(msg.Contains("A "), $"対象職員名(A)が出ること: {msg}");
    }

    /// <summary>
    /// [3.244.0 手M] 直接2人交換が不可能な4人循環。
    ///
    /// 初期: high=A, substitute=C, bridge1=D, bridge2=B
    /// 解:   high=B, substitute=A, bridge1=C, bridge2=D
    ///
    /// highはCを担当不可なので direct pair swap は不可能。日単位完全割当なら
    /// A→B→D→C→A の4-cycleを一度に解き、日別シフト人数を完全保存できる。
    /// substituteにはAのlow違反を設定しないため、「low対象だけを見る」旧ロジックではなく
    /// 担当可能＋上限余力の代用者探索が動くことも同時に固定する。
    /// </summary>
    private static MagiState ExactDayCycleState(bool bridgeWishLocked = false)
    {
        var shifts = new List<Shift>
        {
            new("A", "A", "1", ""),
            new("B", "B", "1", ""),
            new("C", "C", "1", ""),
            new("D", "D", "1", ""),
        };
        var groups = new List<Group>
        {
            new("H", "H"), new("S", "S"), new("R1", "R1"), new("R2", "R2"),
        };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 0, 0 }, // high: A/B
            new List<int> { 1, 0, 1, 0 }, // substitute: A/C
            new List<int> { 0, 0, 1, 1 }, // bridge1: C/D
            new List<int> { 0, 1, 0, 1 }, // bridge2: B/D
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts,
            groups: groups,
            staffList: new List<Staff>
            {
                new("high", 0), new("substitute", 1), new("bridge1", 2), new("bridge2", 3),
            },
            use2Patterns: false,
            groupShift: groupShift,
            schedule: new List<IReadOnlyList<int>>
            {
                new List<int> { 0 }, // high=A（hi=0に対し超過1）
                new List<int> { 2 }, // substitute=C
                new List<int> { 3 }, // bridge1=D
                new List<int> { 1 }, // bridge2=B
            },
            wishes: bridgeWishLocked
                ? new Dictionary<string, int> { ["3,0"] = 1 }
                : new Dictionary<string, int>(),
            staffRange: new Dictionary<string, Range> { ["0,0"] = new("0", "0") });
    }

    [Fact]
    public void ExactDayMatchingFindsFourPersonCycleWithoutLowReceiver()
    {
        var st = ExactDayCycleState();
        var sched = st.Schedule.ToIntArray2D();
        var beforeDay = sched.Select(row => row[0]).OrderBy(x => x).ToList();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("high", 0));
        Assert.NotEqual("vio-low", before.CountViolations.GetValueOrDefault("1,0")); // 代用者にはAのlow違反が無い

        var result = V6HotfixPasses.ApplyRangePolish(st, sched, maxPasses: 1, seed: 1L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("high", -1)); // high解消
        Assert.Equal(0, after.Hard); // HARD不変
        Assert.Equal(beforeDay, result.NewSchedule.Select(row => row[0]).OrderBy(x => x).ToList()); // 日別シフト多重集合を完全保存
        Assert.Equal(new List<int> { 1, 0, 2, 3 }, result.NewSchedule.Select(row => row[0]).ToList()); // 4-cycleの一意解
        Assert.Contains("日割当:1", result.Logs.First().Message); // 手Mが採用されたこと
    }

    [Fact]
    public void ExactDayMatchingRespectsWishLockedBridge()
    {
        var st = ExactDayCycleState(bridgeWishLocked: true);
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyRangePolish(st, sched, maxPasses: 1, seed: 1L);

        Assert.Equal(0, result.Applied); // 循環に必須のbridge2が希望固定なら不採用
        Assert.Equal(
            sched.Select(row => row.ToList()).ToList(),
            result.NewSchedule.Select(row => row.ToList()).ToList());
    }

    /// <summary>
    /// [不採用の主因, 3.302.0] 「不採用」のときログへ併記する主因族の算出。件数でなく<b>重み付き</b>で
    /// 最も増えた族を返すこと（c1=15 が1件増えるより low=90 が1件増えるほうが主因）を固定する。
    /// </summary>
    [Fact]
    public void WorstWorsenedFamilyPicksHeaviestWeightedIncreaseNotLargestCount()
    {
        var before = Report(new Dictionary<string, int> { ["c1"] = 10, ["low"] = 0, ["c3"] = 0 });
        // c3 は +5件(重み3=15)、low は +1件(重み90)、c1 は減少。重み最大の low が主因。
        var after = Report(new Dictionary<string, int> { ["c1"] = 9, ["low"] = 1, ["c3"] = 5 });
        Assert.Equal("low", V6SearchOperators.WorstWorsenedFamily(after, before));
    }

    [Fact]
    public void WorstWorsenedFamilyReturnsNullWhenNothingGotWorse()
    {
        var before = Report(new Dictionary<string, int> { ["c1"] = 10, ["low"] = 2 });
        var after = Report(new Dictionary<string, int> { ["c1"] = 8, ["low"] = 2 });
        Assert.Null(V6SearchOperators.WorstWorsenedFamily(after, before));
    }

    private static ViolationReport Report(IReadOnlyDictionary<string, int> breakdown) =>
        new(
            Violations: new Dictionary<string, string>(),
            NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(),
            Breakdown: breakdown,
            Total: breakdown.Values.Sum(),
            Hard: 0,
            Soft: breakdown.Values.Sum(),
            WeightedScore: breakdown.Sum(kv => kv.Value * MirrorKeys.WeightOf(kv.Key)));
}
