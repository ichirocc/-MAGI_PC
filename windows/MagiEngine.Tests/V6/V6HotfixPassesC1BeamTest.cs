using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース20] <see cref="V6HotfixPasses.ApplyC1BeamPolish"/> の検証。
///
/// <c>C1BeamPolishTest.kt</c>の5件を移植:
///  - <c>c1BeamPolishResolvesDeficiencyAndImprovesTotal</c>→
///    <see cref="ResolvesDeficiencyAndImprovesTotal"/>。
///  - <c>c1BeamPolishIsNoOpWhenNoCons1Rules</c>→<see cref="IsNoOpWhenNoCons1Rules"/>。
///  - <c>moreStepsNeverProduceAWorseResult</c>→<see cref="MoreStepsNeverProduceAWorseResult"/>
///    （最良保持=<c>bestEver</c>の直接検証、実データ golden_state.json 使用）。
///  - <c>earlyStopKeepsTheImprovementAndNeverDegrades</c>→
///    <see cref="EarlyStopKeepsTheImprovementAndNeverDegrades"/>。
///  - <c>c1BeamPolishNeverReturnsScheduleWorseThanInputAcrossManySeeds</c>→
///    <see cref="NeverReturnsScheduleWorseThanInputAcrossManySeeds"/>（keep-best安全網の広域確認）。
/// </summary>
public class V6HotfixPassesC1BeamTest
{
    private static MagiState LoadFixture(string name) =>
        StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    // T=7日, cons1="5日窓X>=2"。target(職員0)がX不足(1<2)。partner1/partner2との同日swap+
    // 玉突きチェーンの組合せで解消可能な最小盤面（BeamC1PolishV2Test/C1WindowTestと同一盤面を再利用）。
    private static MagiState DeficientState() => new MagiState(
        StartDate: "2026-08-01", EndDate: "2026-08-07",
        Shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
        Groups: new List<Group> { new("G0", "G0") },
        StaffList: new List<Staff> { new("target", 0), new("partner1", 0), new("partner2", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 0, 1, 0, 0, 0, 1 },
            new List<int> { 0, 1, 0, 0, 1, 1, 0 },
            new List<int> { 0, 1, 0, 1, 0, 0, 1 },
        },
        Wishes: new Dictionary<string, int>(),
        StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(),
        NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row> { new("5", "X", "2") },
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
        Extras: new Dictionary<string, System.Text.Json.JsonElement>()
    );

    [Fact]
    public void ResolvesDeficiencyAndImprovesTotal()
    {
        var st = DeficientState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.TryGetValue("c1", out var c1v) ? c1v : 0); // 初期はcons1窓不足が1件
        Assert.Equal(0, before.Hard); // 初期HARD=0
        // [Kotlin 3.345.0] weekly がシフト別になり初期 total の内訳が変わった（旧 10）。盤面自体は不変。
        Assert.Equal(19, before.Total); // 初期total

        var result = V6HotfixPasses.ApplyC1BeamPolish(st, sched);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.TryGetValue("c1", out var c1a) ? c1a : -1); // 広域ビーム研磨後はc1=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない
        Assert.True(after.Total < before.Total); // totalは真に改善する(退化しない)
        Assert.True(result.Applied > 0); // 実際に手が採用されている
    }

    [Fact]
    public void IsNoOpWhenNoCons1Rules()
    {
        var st = DeficientState() with { Cons1 = new List<C1Row>() };
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyC1BeamPolish(st, sched);
        Assert.Equal(0, result.Applied);
    }

    /// <summary>
    /// [Kotlin 3.340.0] <b>ステップを増やしても結果は決して悪くならない</b>（最良保持の直接検証）。
    ///
    /// 旧実装は最終ビームの最小しか返さず、ビームは各ステップで全メンバーを子に置き換えるため
    /// 探索が進むほど途中で見つけた良い盤面を捨てていた。実データ(golden_state, beamWidth=8, seed=7)
    /// では maxSteps=4 で weighted 2859 まで下がるのに、<b>6以降は根(2985)に戻り採用0</b>になる
    /// ＝長く回すほど成果を失う。最良保持を入れると全 maxSteps で 2834（旧の最良より良い）で一定。
    /// </summary>
    [Fact]
    public void MoreStepsNeverProduceAWorseResult()
    {
        var st = LoadFixture("golden_state.json");
        var sched = st.Schedule.ToIntArray2D();
        var root = UnifiedViolationChecker.Check(st, sched);
        ViolationReport? prev = null;
        foreach (var ms in new[] { 2, 4, 6, 8, 12 })
        {
            var r = V6HotfixPasses.ApplyC1BeamPolish(st, sched, beamWidth: 8, maxSteps: ms, seed: 7L);
            var a = UnifiedViolationChecker.Check(st, r.NewSchedule);
            if (prev != null)
            {
                Assert.False(
                    UnifiedViolationChecker.BetterReport(prev, a),
                    $"maxSteps={ms} がより少ないステップ数の結果より悪い（最良を保持していない）: " +
                        $"{prev.Hard}/{prev.WeightedScore}/{prev.Total} -> {a.Hard}/{a.WeightedScore}/{a.Total}");
            }
            prev = a;
        }
        var last = prev!;
        Assert.True(
            UnifiedViolationChecker.BetterReport(last, root),
            "この盤面には実際に改善があるので根へ戻ってはいけない");
    }

    /// <summary>[Kotlin 3.340.0] 停滞打ち切り(patience)を最小にしても keep-best は壊れず、1手で解ける改善は取り逃さない。</summary>
    [Fact]
    public void EarlyStopKeepsTheImprovementAndNeverDegrades()
    {
        var st = DeficientState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        foreach (var patience in new[] { 1, 2, 20, 60 })
        {
            var r = V6HotfixPasses.ApplyC1BeamPolish(st, sched, patience: patience);
            var a = UnifiedViolationChecker.Check(st, r.NewSchedule);
            Assert.Equal(0, a.Breakdown.TryGetValue("c1", out var c1v) ? c1v : -1); // patience=X でもc1は解消する
            Assert.Equal(before.Hard, a.Hard); // patience=X でHARDは不変
            Assert.False(UnifiedViolationChecker.BetterReport(before, a)); // patience=X で入力より悪化してはいけない
        }
    }

    [Fact]
    public void NeverReturnsScheduleWorseThanInputAcrossManySeeds()
    {
        // [keep-best安全網の直接検証] 受領コードには無かった安全網が実際に機能し、任意のseedで
        // 「入力より悪化した結果を返す」ことが起きないことを広く確認する。
        var st = DeficientState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        for (long seed = 0L; seed < 20L; seed++)
        {
            var result = V6HotfixPasses.ApplyC1BeamPolish(st, sched, seed: seed * 97L + 3L);
            var after = UnifiedViolationChecker.Check(st, result.NewSchedule);
            Assert.True(
                after.Total <= before.Total,
                $"seed={seed}: total悪化(before={before.Total}, after={after.Total})は許されない");
            Assert.Equal(before.Hard, after.Hard); // seed=X: HARDは不変
        }
    }
}
