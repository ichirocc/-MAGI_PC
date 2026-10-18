using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.721.2] 終わり方の一文は採用した最終結果の数だけを言う（Kotlin <c>StopExplanationBoundaryTest</c> と 1 対 1）。</summary>
public class StopExplanationBoundaryTest
{
    private static V6FinalPort.StopSummary S(V6FinalPort.StopKind kind, int finalHard, bool early = true) =>
        new(early, kind, UsedSec: 90, BudgetSec: 120, StalledSec: 30, RemainingHard: finalHard);

    [Fact]
    public void PostProcessingReductionIsReflectedInTheLine()
    {
        var e = StopExplanation.Of(S(V6FinalPort.StopKind.NormalStall, 2), new Dictionary<string, int> { ["c3n"] = 2 })!;
        Assert.Contains("必須違反 2件", e.Line);
        Assert.DoesNotContain("5件", e.Line);
    }

    [Fact]
    public void FinalBreakdownUnitsWinOverTheCount()
    {
        var e = StopExplanation.Of(S(V6FinalPort.StopKind.Deadline, 3, early: false), new Dictionary<string, int> { ["covU"] = 1, ["c3n"] = 2 })!;
        Assert.Contains("最終結果の必須違反は 2件・1人です", e.Line);
    }

    [Fact]
    public void FloorThenPostProcessingStaysConsistentWithFinalCounts()
    {
        var e = StopExplanation.Of(S(V6FinalPort.StopKind.PlateauFloor, 1), new Dictionary<string, int> { ["covU"] = 1 })!;
        Assert.Contains("残る必須違反 1人", e.Line);
        Assert.Equal(StopNext.ReviewStaffing, e.Next);
        var w = StopExplanation.Of(S(V6FinalPort.StopKind.WishFloor, 1), new Dictionary<string, int> { ["pref"] = 1 })!;
        Assert.Contains("残る必須違反 1件", w.Line);
    }

    [Fact]
    public void FinalZeroHardShowsNoExplanationForAnyKind()
    {
        foreach (var k in Enum.GetValues<V6FinalPort.StopKind>())
        {
            Assert.Null(StopExplanation.Of(S(k, 0, early: k != V6FinalPort.StopKind.Deadline)));
            Assert.Null(StopExplanation.Of(S(k, 0, early: k != V6FinalPort.StopKind.Deadline), new Dictionary<string, int>()));
        }
    }
}
