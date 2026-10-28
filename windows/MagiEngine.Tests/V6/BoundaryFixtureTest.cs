using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// T-16（Android <c>docs/overhaul_detailed_spec.md</c> §4.1 B-2）: 職員 1 名・30 名 × 期間 1 日・28〜31 日の境界で、チェッカー・フル評価・
/// 決定的モードの後処理が落ちずに返り、後処理の結果が入力より悪くならない（番兵を通る）。30 名・31 日ではビット化経路が有効。
/// </summary>
public class BoundaryFixtureTest
{
    private const int Rest = 0, A = 1;

    /// <summary>休・A・B の 3 シフト、A/B に必要人数、禁止の並び「B→A」、窓の制約（7 日に休 1 回）、職員 0 の A に回数範囲、希望 1 件。</summary>
    private static MagiState State(int staff, string start, string end, int days)
    {
        var needA = Math.Max(1, staff / 4); var needB = staff / 5;
        var rng = new JavaRandom(staff * 100L + days);
        // 希望（0,0）=休 は入力で満たす
        var sched = Enumerable.Range(0, staff).Select(i => (IReadOnlyList<int>)Enumerable.Range(0, days).Select(j => i == 0 && j == 0 ? Rest : rng.NextInt(3)).ToList()).ToList();
        var nb = needB > 0 ? $"{needB}" : "";
        return MinimalState.Build(
            startDate: start, endDate: end,
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", $"{needA}", $"{needA}"), new("B", "B", nb, nb) },
            groups: new List<Group> { new("G", "G") }, staffList: Enumerable.Range(0, staff).Select(i => new Staff($"s{i}", 0)).ToList(), use2Patterns: true,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }, groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: sched, wishes: new Dictionary<string, int> { ["0,0"] = Rest }, staffRange: new Dictionary<string, Range> { [$"0,{A}"] = new("0", $"{days / 2}") },
            cons1: days >= 7 ? new List<C1Row> { new("7", "休", "1") } : new List<C1Row>(),
            cons3n: new List<C3Row> { new(new List<string> { "B", "A" }) });
    }

    private static void RunBoundary(int staff, string start, string end, int days)
    {
        var st = State(staff, start, end, days);
        Assert.Equal(staff, st.StaffCount); Assert.Equal(days, st.DayCount);
        var p = new Problem(st);
        var input = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, input.Copy2D());
        new Evaluator(p).FullEval(input.Copy2D());
        var outRes = V6HotfixPasses.RunPostOptimization(st, input.Copy2D(), "t16", seed: 1L,
            deadlineMs: EngineClock.NowMs() + 600_000L,
            parameters: new V6HotfixPasses.PostOptimizationParams(Deterministic: true, MaxRounds: 1,
                C1LnsMaxEvaluations: 2_000, PersonalLnsMaxEvaluations: 2_000));
        var tag = $"{staff}名×{days}日";
        Assert.True(staff == outRes.Schedule.Length, $"{tag} 行数");
        Assert.True(outRes.Schedule.All(r => r.Length == days), $"{tag} 列数");
        var after = UnifiedViolationChecker.Check(st, outRes.Schedule.Copy2D());
        Assert.True(after.Hard <= before.Hard, $"{tag} 必須が増えた {before.Hard}->{after.Hard}");
        Assert.False(UnifiedViolationChecker.BetterReport(before, after), $"{tag} 入力より悪い");
        Assert.True(Rest == outRes.Schedule[0][0], $"{tag} 希望固定のセル");
        new Evaluator(p).FullEval(outRes.Schedule.Copy2D());
    }

    [Fact] public void OneStaffOneDay() => RunBoundary(1, "2026-01-01", "2026-01-01", 1);
    [Fact] public void OneStaffThirtyOneDays() => RunBoundary(1, "2026-01-01", "2026-01-31", 31);
    [Fact] public void ThirtyStaffOneDay() => RunBoundary(30, "2026-01-01", "2026-01-01", 1);
    [Fact] public void ThirtyStaffTwentyEightDays() => RunBoundary(30, "2026-02-01", "2026-02-28", 28);

    [Fact]
    public void ThirtyStaffThirtyOneDaysUsesTheBitPath()
    {
        var st = State(30, "2026-01-01", "2026-01-31", 31);
        Assert.True(C3nBitScan.Usable(new Problem(st)), "30 名・31 日でビット化経路が有効");
        RunBoundary(30, "2026-01-01", "2026-01-31", 31);
    }
}
