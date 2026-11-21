using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>残り違反に合わせて役割を選ぶ制御（Kotlin <c>OpportunityControllerTest.kt</c>、Android 3.727.122）。</summary>
public class OpportunityControllerTest
{
    private static readonly HypothesisEpochRole[] Roles =
        { HypothesisEpochRole.DayBlockAlns, HypothesisEpochRole.HardFamilyRsi, HypothesisEpochRole.HardDebtRsiPlus };

    private static ViolationReport Report(int hard, double weighted, Dictionary<string, int> breakdown) =>
        new(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), breakdown,
            breakdown.Values.Sum(), hard, breakdown.Values.Sum() - hard, weighted);

    [Fact]
    public void phaseIsTheHeaviestRemainingFamily()
    {
        var c = new OpportunityController(Roles);
        Assert.Equal("covU", c.PhaseOf(Report(2, 20_000, new() { ["covU"] = 1, ["c3n"] = 1, ["c1"] = 50 })));
        Assert.Equal("c1", c.PhaseOf(Report(0, 5_000, new() { ["c1"] = 50, ["c2"] = 10 })));
    }

    [Fact]
    public void untriedRolesFirstThenBestRatePerSecond()
    {
        var c = new OpportunityController(Roles);
        var before = Report(1, 20_000, new() { ["covU"] = 1 });
        Assert.NotEqual(HypothesisEpochRole.DayBlockAlns, c.Choose("covU", HypothesisEpochRole.DayBlockAlns));
        for (var n = 0; n < 5; n++)
        {
            c.Record("covU", HypothesisEpochRole.HardFamilyRsi, 10.0, before, Report(0, 10_000, new()));
            c.Record("covU", HypothesisEpochRole.HardDebtRsiPlus, 30.0, before, before);
            c.Record("covU", HypothesisEpochRole.DayBlockAlns, 5.0, before, before);
        }
        Assert.Equal(HypothesisEpochRole.HardFamilyRsi, c.Choose("covU", HypothesisEpochRole.DayBlockAlns));
        Assert.Equal(HypothesisEpochRole.HardFamilyRsi, c.Choose("c1", HypothesisEpochRole.DayBlockAlns));
    }

    [Fact]
    public void hardDropCountsEvenWhenWeightRises()
    {
        Assert.Equal(OpportunityController.HardUnit, OpportunityController.GainOf(Report(2, 20_000, new()), Report(1, 25_000, new())), 9);
    }
}
