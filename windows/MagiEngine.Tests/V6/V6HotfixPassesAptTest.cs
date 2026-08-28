using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, Apt（適切回数）研磨] <see cref="V6HotfixPasses.ApplyAptPolish"/> の検証。
///
/// [Kotlin原本] <c>AptPolishTest.kt</c>の5件を移植:
///  - <c>aptPolishResolvesViaSelfSwapWhenSameStaffHasOppositeImbalance</c>→
///    <see cref="ResolvesViaSelfSwapWhenSameStaffHasOppositeImbalance"/>。
///  - <c>aptPolishResolvesViaMutualSwapWithSameGroupMember</c>→
///    <see cref="ResolvesViaMutualSwapWithSameGroupMember"/>。
///  - <c>aptPolishResolvesSingleDirectionViaChainWhenNoSelfOrMutualPartner</c>→
///    <see cref="ResolvesSingleDirectionViaChainWhenNoSelfOrMutualPartner"/>。
///  - <c>aptPolishExhaustsSelfSwapWithinSinglePassForMultiUnitImbalance</c>→
///    <see cref="ExhaustsSelfSwapWithinSinglePassForMultiUnitImbalance"/>。
///  - <c>aptPolishIsNoOpWhenNoAptTargetsSet</c>→<see cref="IsNoOpWhenNoAptTargetsSet"/>。
///
/// [AptPolish] ユーザー指示「専用の研磨パスAptPolish的なものを賢く深く網羅的に作る」の検証
/// （grillingで確定: ①自己振替最優先 ②同一グループ内の相互交換 ③RangePolish型の玉突きチェーン）。
/// </summary>
public class V6HotfixPassesAptTest
{
    // 手①: 単一職員が同一シフト内でaptHigh(X)とaptLow(Y)を同時に持つ最小盤面。
    // 休/X/Yとも need 無し(被覆制約ゼロ)＝自己振替が構造的に無償で成立する。
    private static MagiState SelfSwapState()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("X", "X", "", ""),
            new("Y", "Y", "", ""),
        };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } };
        var groupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "1", "3" } }; // X目標1・Y目標3
        var staff = new List<Staff> { new("A", 0) };
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2 } }; // A: X,X,Y,Y（Xは目標1に対し2=超過、Yは目標3に対し2=不足）
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-04",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift, groupShiftApt: groupShiftApt,
            schedule: schedule);
    }

    [Fact]
    public void ResolvesViaSelfSwapWhenSameStaffHasOppositeImbalance()
    {
        var st = SelfSwapState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("apt", 0) > 0, "初期はapt違反があること");
        Assert.Equal(0, before.Hard); // 初期HARD=0

        var result = V6HotfixPasses.ApplyAptPolish(st, sched);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("apt", -1)); // 自己振替後はapt=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない
        Assert.True(result.Applied > 0, "実際に手が採用されている");
    }

    // 手②: 同一グループの2職員が同一シフトで逆方向のapt不均衡を持つ最小盤面（自身の中には
    // 逆方向シフトが無いため自己振替は成立せず、相互交換のみが解となる）。
    private static MagiState MutualSwapState()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1 } };
        var groupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "1" } }; // X目標1（休は目標なし＝自己振替の相手になり得ない）
        var staff = new List<Staff> { new("A", 0), new("B", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1 }, // A = X,X（目標1に対し2=超過1）
            new List<int> { 0, 0 }, // B = 休,休（目標1に対し0=不足1）
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-02",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift, groupShiftApt: groupShiftApt,
            schedule: schedule);
    }

    [Fact]
    public void ResolvesViaMutualSwapWithSameGroupMember()
    {
        var st = MutualSwapState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("apt", 0) > 0, "初期はapt違反(AがaptHigh・BがaptLow)があること");

        var result = V6HotfixPasses.ApplyAptPolish(st, sched);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("apt", -1)); // 相互交換後はapt=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない
        Assert.True(result.Applied > 0, "実際に手が採用されている");
        // 被覆総量保存＝両者の担当日数の合計(X日数)は不変であることも確認。
        var xCountBefore = st.Schedule.Sum(row => row.Count(v => v == 1));
        var xCountAfter = result.NewSchedule.Sum(row => row.Count(v => v == 1));
        Assert.Equal(xCountBefore, xCountAfter); // Xの総日数(=被覆総量)は保存される
    }

    // 手③: 自己振替/相互交換の相手が構造的に存在しない単一方向のaptHighを、玉突きチェーンで解消する。
    // Aが唯一のX担当可能者でXを独占(need1=1)。Bは需要のない別シフトZに在勤中(いつでも動かせる)。
    private static MagiState ChainState()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("X", "X", "1", ""),
            new("Y", "Y", "", ""), // Aの逃げ先
            new("Z", "Z", "", ""), // Bの現在地
        };
        var groups = new List<Group> { new("GA", "GA"), new("GB", "GB") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0 }, // GA(A) = 休,X,Y
            new List<int> { 1, 1, 0, 1 }, // GB(B) = 休,X,Z
        };
        var groupShiftApt = new List<IReadOnlyList<string>>
        {
            new List<string> { "", "1", "", "" }, // GA: X目標1(Yは目標なし=自己振替の相手なし)
            new List<string> { "", "", "", "" },  // GB: apt対象なし
        };
        var staff = new List<Staff> { new("A", 0), new("B", 1) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1 }, // A = X,X（目標1に対し2=超過1）
            new List<int> { 3, 3 }, // B = Z,Z（需要なし=いつでも動かせる）
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-02",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift, groupShiftApt: groupShiftApt,
            schedule: schedule);
    }

    [Fact]
    public void ResolvesSingleDirectionViaChainWhenNoSelfOrMutualPartner()
    {
        var st = ChainState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("apt", 0) > 0, "初期はapt違反があること");
        Assert.Equal(0, before.Hard); // 初期HARD=0(AがXを単独充足)

        var result = V6HotfixPasses.ApplyAptPolish(st, sched, seed: 1L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("apt", -1)); // 玉突き適用後はapt=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU", -1)); // Xの被覆(covU)は悪化しない
        Assert.True(result.Applied > 0, "実際に手が採用されている");
    }

    // [3.260.0, ユーザー指摘「大島が違反研磨で来てない」の根本原因] 手①(自己振替)は旧実装だと
    // 1パスにつき(i,k)ペア1回成功したら次のhighTargetsへ移っており、excess/deficitが複数単位ある
    // 職員は1パスで1単位しか解消できなかった（実機ログで大島愛の休(目標11・実績17=超過6)/Pｼ(目標19・
    // 実績9=不足10)が、AptPolishの1回の呼出で採用1回=2単位しか縮まらず残存し続けていた実例を再現）。
    private static MagiState MultiUnitSelfSwapState()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("X", "X", "", ""),
            new("Y", "Y", "", ""),
        };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } };
        var groupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "1", "3" } }; // X目標1・Y目標3
        var staff = new List<Staff> { new("A", 0) };
        // A: X,X,X,X,休,休,休（Xは目標1に対し4=超過3、Yは目標3に対し0=不足3）
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1, 0, 0, 0 } };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-07",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift, groupShiftApt: groupShiftApt,
            schedule: schedule);
    }

    [Fact]
    public void ExhaustsSelfSwapWithinSinglePassForMultiUnitImbalance()
    {
        var st = MultiUnitSelfSwapState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(6, before.Breakdown.GetValueOrDefault("apt", -1)); // 初期apt偏差=6(X超過3+Y不足3)

        // maxPasses=1に固定し、1パス内での自己振替の反復可否そのものを検証する
        // （旧実装は1パス・1ターゲットにつき1回しか自己振替せず、超過3件のうち1件しか解消できなかった）。
        var result = V6HotfixPasses.ApplyAptPolish(st, sched, maxPasses: 1);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        // [3.345.0] weekly が「職員×シフト×曜日」になり同じ目的関数の中で自己振替と競合するため、
        //   1パスで apt=0 まで到達するとは限らなくなった（この盤面は T=7＝曜日が各1回で weekly が最も強く効く）。
        //   このテストが本来固定したいのは「1パス内で自己振替が反復される」ことなので、そこを直接見る。
        Assert.True(result.Applied >= 3, "1パス内で複数回の手が採用されている(旧実装は1回で打ち切っていた)");
        Assert.True(after.Breakdown.GetValueOrDefault("apt", 99) < before.Breakdown.GetValueOrDefault("apt", 0)); // apt は厳密に減る
        Assert.Equal(0, after.Hard); // HARDは悪化しない
        // [3.345.0] apt=0 まで到達するとは限らない。この盤面は T=7＝各曜日が1回しか無く、apt(重み1)と
        //   weekly(重み1)が同じ目的関数の中で正面から競合するため、目的関数の最適が apt=0 とは一致しない
        //   （実測 apt=5 で落ち着く）。ここで固定できる真の不変条件は keep-best＝パスを重ねても悪化しないこと。
        var settled = UnifiedViolationChecker.Check(
            st, V6HotfixPasses.ApplyAptPolish(st, result.NewSchedule, maxPasses: 4).NewSchedule);
        Assert.True(settled.Total <= after.Total, "keep-best: パスを重ねても目的関数(total)は悪化しない");
    }

    [Fact]
    public void IsNoOpWhenNoAptTargetsSet()
    {
        var st = SelfSwapState() with { GroupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "", "" } } };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyAptPolish(st, sched);
        Assert.Equal(0, result.Applied);
    }
}
