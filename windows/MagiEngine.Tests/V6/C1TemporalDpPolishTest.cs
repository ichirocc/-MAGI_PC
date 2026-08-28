using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.254.0/3.310.0 移植元] <see cref="C1TemporalDp.Solve"/> / <see cref="C1TemporalDp.CountFires"/> /
/// <see cref="C1TemporalFlowPolish.Apply"/> を固定する。Kotlin原本 <c>C1TemporalDpPolishTest.kt</c> の
/// 5件すべてを移植（旧 <c>C1TemporalDpTest.cs</c> は <c>C1TemporalFlowPolish</c> 移植に伴いこのファイルへ
/// 統合・改名した）。
/// </summary>
public class C1TemporalDpPolishTest
{
    [Fact]
    public void ExactDpCrossesTwoSwapLocalMinimumAndPreservesCount()
    {
        // T=11, D=5,N=2。X={0,1,5,6}はc1=1。
        // 全1回swapはc1を減らせないが、0→2 と 5→7 の2同時移設でc1=0になる。
        var row = new[] { 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0 };
        var rules = new List<C1TemporalDp.Rule> { new(5, 2) };
        int before = C1TemporalDp.CountFires(row, 1, rules);
        Assert.Equal(1, before);

        var outp = C1TemporalDp.Solve(
            row: row, targetShift: 1, rules: rules, locked: new bool[row.Length],
            maxRelocations: 4, seed: 7L);
        Assert.True(outp is not null);
        Assert.Equal(0, outp!.Fires);
        Assert.Equal(2, outp.Relocations);
        Assert.Equal(4, outp.ChangedCells);
        Assert.Equal(row.Count(v => v == 1), outp.TargetDays.Count(v => v)); // X月間回数を保存
    }

    [Fact]
    public void ExactDpNeverChangesLockedTargetOrNonTargetDay()
    {
        // [CI失敗の修正] 日0(X)・日10(非X)を固定すると、窓[1-5]と窓[6-10]が互いに素な区間で
        // それぞれ独立に2件以上要求するため、日0固定分(1)と合わせて必要X数≥5だが月間回数保存で
        // 4のまま＝数学的に不可能（鳩の巣原理）。日1(X, 解=0→2/5→7で不変)・日9(非X, 同解で不変)を
        // 固定に差し替え、既知の実行可能解(X→{1,2,6,7})と両立することを確認済みの構成にする。
        var row = new[] { 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0 };
        var locked = new bool[row.Length];
        locked[1] = true; // X固定
        locked[9] = true; // 非X固定
        var outp = C1TemporalDp.Solve(
            row: row, targetShift: 1, rules: new List<C1TemporalDp.Rule> { new(5, 2) }, locked: locked,
            maxRelocations: 4, seed: 11L);
        Assert.True(outp is not null);
        Assert.True(outp!.TargetDays[1]);
        Assert.False(outp.TargetDays[9]);
    }

    // [3.254.0/C1TemporalFlowPolish, C1TemporalSwapPolish置換] 旧C1TemporalSwapPolishは変更日ごとに
    // 「厳密に相補的なシフトを持つ1人との同日swap」でしかDPの目標パターンを実現できず、そのような
    // 相手が居ない日では改善が丸ごと死んでいた（実データ検証=golden_state.jsonで寄与0%確認）。
    // この盤面は day0 に「相手がYへ渡せる余剰を持たない」よう partner を意図的に非対称化し
    // （partnerのday0はY=targetと同じでswap不成立）、旧実装なら day0 の改善だけが失敗する構成にする。
    // 被覆制約(needDay)を一切設定しないため、target が1人だけ自由に動いても構造的に無害
    // （FlexibleDayFlowは強制的な2人swapでなく、費用最小の任意人数再割当を解く）。
    private static MagiState AsymmetricSwapState()
    {
        var shifts = new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", ""), new("Z", "Z", "", "") };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var staffList = new List<Staff> { new("target", 0), new("partner", 0), new("stable", 0), new("helper", 1) };
        var target = new List<int> { 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0 };
        var partnerRow = new List<int> { 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 1 };
        partnerRow[0] = 0; // day0のpartnerもYのまま＝day0だけ同日swap相手が存在しない
        var stable = Enumerable.Repeat(1, 11).ToList();
        var helper = Enumerable.Repeat(2, 11).ToList();
        return MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-11",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>>   // G1(helper)はXを担当不可＝c1対象外
            {
                new List<int> { 1, 1, 0 },
                new List<int> { 1, 0, 1 },
            },
            groupShiftApt: new List<IReadOnlyList<string>>
            {
                new List<string> { "", "", "" },
                new List<string> { "", "", "" },
            },
            schedule: new List<IReadOnlyList<int>> { target, partnerRow, stable, helper },
            wishes: new Dictionary<string, int>(),
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("4", "4") }, // X回数保存の同時移設だけが解になるよう固定
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row> { new(Day1: "5", ShiftKigou: "X", Day2: "2") },
            cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
    }

    [Fact]
    public void TemporalFlowPolishResolvesWhenNoExactSwapPartnerExistsOnChangedDay()
    {
        var st = AsymmetricSwapState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c1", 0));
        Assert.Equal(0, before.Hard);

        var outp = C1TemporalFlowPolish.Apply(
            st, sched, maxPasses: 1, maxRelocations: 4, trials: 8, seed: 7L);
        var after = UnifiedViolationChecker.Check(st, outp.NewSchedule);
        Assert.True(after.Breakdown.GetValueOrDefault("c1", -1) == 0, "day0に同日swap相手が居なくても解消できる");
        Assert.Equal(0, after.Hard);
        Assert.Equal(1, outp.Applied);
        Assert.True(outp.NewSchedule[0].Count(v => v == 1) == 4, "targetのX月間回数を保存");
    }

    [Fact]
    public void TemporalFlowPolishIsNoOpWhenNoCons1Rules()
    {
        var st = AsymmetricSwapState() with { Cons1 = new List<C1Row>() };
        var sched = st.Schedule.ToIntArray2D();
        var outp = C1TemporalFlowPolish.Apply(st, sched);
        Assert.Equal(0, outp.Applied);
    }

    /// <summary>
    /// [3.310.0] 状態数の安全弁。DP は疎マップなので窓長ガードだけでは状態数を縛れない。
    /// 上限を超えたら解を返さず諦める（null）＝呼出側は提案なしとして扱い keep-best は不変。
    /// ここでは「上限1」を渡して打切り経路そのものを踏ませ、通常上限なら解が出ることと対比する。
    /// </summary>
    [Fact]
    public void ExactDpBailsOutInsteadOfExplodingWhenStateCountExceedsTheCap()
    {
        int t = 12;
        var row = Enumerable.Range(0, t).Select(i => i % 3 == 0 ? 1 : 0).ToArray();
        var locked = new bool[t];
        var rules = new List<C1TemporalDp.Rule> { new(5, 2) };
        var normal = C1TemporalDp.Solve(row: row, targetShift: 1, locked: locked, rules: rules, seed: 1L);
        Assert.True(normal is not null, "通常の上限なら解が出る前提を固定する");
        var capped = C1TemporalDp.Solve(
            row: row, targetShift: 1, locked: locked, rules: rules, seed: 1L, maxDpStates: 1);
        Assert.True(capped is null, "状態数が上限を超えたら例外でなく null で諦める");
    }
}
