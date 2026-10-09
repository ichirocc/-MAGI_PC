using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [統合段の評価モード, 3.642.0] Kotlin原本 <c>EliteIntegrationQuantitativeTest</c> の 1:1 移植。
/// 統合（<see cref="EliteIntegrationPolish.Apply"/>）が返す報告は、呼出元の量的評価モードで採点される。
/// 検査するのは最終報告と根の盤面だけで、内部の中間評価まで網羅はしない。
/// </summary>
public class EliteIntegrationQuantitativeTest
{
    private static MagiState BuildState(IReadOnlyList<IReadOnlyList<int>> schedule) => MinimalState.Build(
        startDate: "2025-01-01",
        endDate: $"2025-01-0{schedule[0].Count}",
        shifts: new List<Shift>
        {
            new("休", "休", "", "", ShiftRole.Rest),
            new("A", "A", "", ""),
            new("B", "B", "", ""),
        },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0), new("s2", 0), new("s3", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: schedule,
        cons2: new List<C2Row> { new("A", "3") },
        cons41: new List<C41Row> { new("G0", "B", "2", "2") });

    [Fact]
    public void ReturnedReportFollowsTheRequestedEvaluationMode()
    {
        const int days = 5;
        var state = BuildState(Enumerable.Range(0, 4)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, days).ToList())
            .ToList());
        var root = Enumerable.Range(0, 4).Select(_ => new int[days]).ToArray();
        var res = EliteIntegrationPolish.Apply(
            state, root, new List<AdaptiveElite>(), () => false, EngineClock.NowMs() + 60_000L,
            quantitativeRangeEval: true);
        Assert.Equal(12, res.Report.Breakdown.GetValueOrDefault("c2", 0));
        var quant = UnifiedViolationChecker.Check(state, res.Schedule, quantitativeRangeEval: true);
        Assert.Equal(quant.WeightedScore, res.Report.WeightedScore);
        // この盤面は評価モードで差が出る（二値は 4）
        Assert.True(UnifiedViolationChecker.Check(state, res.Schedule).Breakdown.GetValueOrDefault("c2", 0)
            != res.Report.Breakdown.GetValueOrDefault("c2", 0));
    }
}
