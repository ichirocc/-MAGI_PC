using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, C3n（禁止連続）研磨] <see cref="V6HotfixPasses.ApplyC3nPolish"/> の検証。
///
/// [Kotlin原本] <c>C3nPolishTest.kt</c>の3件を移植:
///  - <c>resolvesThreeRunByChangingItsHeadTwoDaysBefore</c>→
///    <see cref="ResolvesThreeRunByChangingItsHeadTwoDaysBefore"/>。
///  - <c>resolvesTwoRunByChangingTheViolatingDayItself</c>→
///    <see cref="ResolvesTwoRunByChangingTheViolatingDayItself"/>。
///  - <c>isNoOpWhenNoForbiddenRuleExists</c>→<see cref="IsNoOpWhenNoForbiddenRuleExists"/>。
///
/// [C3nPolish, 3.303.0] 禁止連続を「違反パターンがまたぐ全日」で崩せることの検証。
/// 実データの cons3n には <c>Dﾃ→休→A4</c> のような3連があり、違反が末尾セルに立つと<b>パターンの
/// 先頭は2日前</b>にある。既存機構（当日1セルだけ / 隣接日 j±1 だけ）はそこに構造的に届かなかった。
/// </summary>
public class V6HotfixPassesC3nTest
{
    // shift index: 0=休 1=D 2=A 3=Z(逃げ先・どのルールにも出てこない)
    private static readonly List<string> Kigou = new() { "休", "D", "A", "Z" };
    private const int Rest = 0;
    private const int DShift = 1;
    private const int AShift = 2;

    private static MagiState State(
        IReadOnlyList<IReadOnlyList<int>> schedule,
        IReadOnlyList<C3Row> cons3n,
        IReadOnlyDictionary<string, int>? wishes = null)
    {
        var shifts = Kigou.Select(k => new Shift(k, k, "", "")).ToList();
        var groups = new List<Group> { new("G", "G") };
        var staff = Enumerable.Range(0, schedule.Count).Select(i => new Staff($"s{i}", 0)).ToList();
        var groupShift = new List<IReadOnlyList<int>> { Enumerable.Repeat(1, Kigou.Count).ToList() };
        return MinimalState.Build(
            startDate: "2026-12-01", endDate: "",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift,
            schedule: schedule,
            wishes: wishes ?? new Dictionary<string, int>(),
            cons3n: cons3n);
    }

    /// <summary>
    /// 3連 <c>D→休→A</c> が窓[0..2]で成立。当日(=末尾 day2 の A)と直前(day1 の 休)は<b>希望で固定</b>し、
    /// 崩せるのはパターン先頭 day0 の D だけ、という構成。
    /// - 当日1セルしか触らない既存機構 → day2 が固定なので手が無い
    /// - 隣接日 j±1 しか見ない旧実装 → day1 が固定・day3 はパターン外なので手が無い
    /// - 本パス（パターン全域＝day0 も候補） → day0 の D を Z へ変えて解消できる
    /// </summary>
    [Fact]
    public void ResolvesThreeRunByChangingItsHeadTwoDaysBefore()
    {
        const int days = 5;
        var row = Enumerable.Repeat(Rest, days).ToList();
        row[0] = DShift; row[1] = Rest; row[2] = AShift;
        var st = State(
            schedule: new List<IReadOnlyList<int>> { row },
            cons3n: new List<C3Row> { new(new List<string> { "D", "休", "A" }) },
            // day1(休) と day2(A) を希望固定 → 崩せるのは day0 だけ
            wishes: new Dictionary<string, int> { ["0,1"] = Rest, ["0,2"] = AShift });
        var sched = st.Schedule.ToIntArray2D();
        var beforeRep = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, beforeRep.Breakdown.GetValueOrDefault("c3n", 0)); // 前提: 3連がちょうど1件成立

        var result = V6HotfixPasses.ApplyC3nPolish(st, sched, maxPasses: 2, seed: 7L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n", -1)); // 禁止連続が解消
        Assert.Equal(0, after.Hard); // HARD も減る（c3n は HARD）
        Assert.NotEqual(DShift, result.NewSchedule[0][0]); // 崩したのはパターン先頭 day0
        Assert.Equal(Rest, result.NewSchedule[0][1]); // 希望固定の day1 は不変
        Assert.Equal(AShift, result.NewSchedule[0][2]); // 希望固定の day2 は不変
        Assert.Contains("候補日延べ", result.Logs.First().Message); // 候補日がパターン全域に広がっている
    }

    /// <summary>
    /// 当日を変えれば解ける2連。当日自身が候補に入っていることの確認
    /// （「前後日と当日も」という要件の当日側）。
    /// </summary>
    [Fact]
    public void ResolvesTwoRunByChangingTheViolatingDayItself()
    {
        const int days = 4;
        var row = Enumerable.Repeat(Rest, days).ToList();
        row[0] = DShift; row[1] = AShift;
        var st = State(
            schedule: new List<IReadOnlyList<int>> { row },
            cons3n: new List<C3Row> { new(new List<string> { "D", "A" }) },
            // day0(D) を希望固定 → 崩せるのは当日 day1 のみ
            wishes: new Dictionary<string, int> { ["0,0"] = DShift });
        var sched = st.Schedule.ToIntArray2D();
        Assert.Equal(1, UnifiedViolationChecker.Check(st, sched).Breakdown.GetValueOrDefault("c3n", 0)); // 前提: 2連が成立

        var result = V6HotfixPasses.ApplyC3nPolish(st, sched, maxPasses: 2, seed: 7L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n", -1)); // 禁止連続が解消
        Assert.Equal(DShift, result.NewSchedule[0][0]); // 希望固定の day0 は不変
        Assert.NotEqual(AShift, result.NewSchedule[0][1]); // 当日 day1 が変わった
    }

    [Fact]
    public void IsNoOpWhenNoForbiddenRuleExists()
    {
        var st = State(
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(Rest, 4).ToList() },
            cons3n: new List<C3Row>());
        var sched = st.Schedule.ToIntArray2D();
        var result = V6HotfixPasses.ApplyC3nPolish(st, sched, maxPasses: 2, seed: 7L);
        Assert.Equal(0, result.Applied);
        Assert.Equal(sched[0], result.NewSchedule[0]);
    }
}
