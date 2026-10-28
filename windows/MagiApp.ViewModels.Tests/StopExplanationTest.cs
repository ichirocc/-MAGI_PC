using System.Text.RegularExpressions;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.643.0] 終わり方の一文は種別ごとに次の一手が決まり、内部名（英字）を含まない（Kotlin <c>StopExplanationTest</c> と 1 対 1）。</summary>
public class StopExplanationTest
{
    private static V6FinalPort.StopSummary S(V6FinalPort.StopKind kind, int hard, bool early = true) =>
        new(early, kind, UsedSec: 84, BudgetSec: 120, StalledSec: 45, RemainingHard: hard);

    [Fact]
    public void NothingToSayWhenTheDeadlineEndedAPerfectBoard() =>
        Assert.Null(StopExplanation.Of(S(V6FinalPort.StopKind.Deadline, 0, early: false)));

    /// <summary>[3.678.0] 必須 0 なら早く終えた理由も言わない（結果カードの見出しで足りる）。</summary>
    [Fact]
    public void EarlyStopWithNoHardLeftSaysNothing() => Assert.Null(StopExplanation.Of(S(V6FinalPort.StopKind.PlateauFloor, 0)));

    /// <summary>[3.678.0] 探索時間がすでに上限なら「延ばす」を勧めない。</summary>
    [Fact]
    public void DeadlineAtMaxBudgetDoesNotSuggestMoreTime()
    {
        var atMax = new V6FinalPort.StopSummary(false, V6FinalPort.StopKind.Deadline, UsedSec: 84, BudgetSec: V6FinalPort.MaxOptimizeSec, StalledSec: 45, RemainingHard: 2);
        Assert.Equal(StopNext.FindFix, StopExplanation.Of(atMax)!.Next);
    }

    [Fact]
    public void RemainingHardUsesUnits()
    {
        var e = StopExplanation.Of(S(V6FinalPort.StopKind.Deadline, 3, early: false), new Dictionary<string, int> { ["c3n"] = 1, ["covU"] = 2 })!;
        Assert.Contains("残る必須違反は 1件・2人です", e.Line);
        Assert.False(e.Line.Contains("主探索") || e.Line.Contains("制限時間"));
    }

    [Fact]
    public void EachKindMapsToItsNextStep()
    {
        Assert.Equal(StopNext.MoreTime, StopExplanation.Of(S(V6FinalPort.StopKind.Deadline, 2, early: false))!.Next);
        Assert.Equal(StopNext.ReviewStaffing, StopExplanation.Of(S(V6FinalPort.StopKind.PlateauFloor, 2))!.Next);
        Assert.Equal(StopNext.ReviewWishes, StopExplanation.Of(S(V6FinalPort.StopKind.WishFloor, 1))!.Next);
        Assert.Equal(StopNext.ReviewWishes, StopExplanation.Of(S(V6FinalPort.StopKind.C3nWallCertified, 1))!.Next);
        Assert.Equal(StopNext.FindFix, StopExplanation.Of(S(V6FinalPort.StopKind.C3nWallEmpirical, 1))!.Next);
        Assert.Equal(StopNext.FindFix, StopExplanation.Of(S(V6FinalPort.StopKind.NormalStall, 3))!.Next);
    }

    [Fact]
    public void LinesCarryTheNumbersAndNoInternalNames()
    {
        foreach (var k in Enum.GetValues<V6FinalPort.StopKind>())
        {
            var e = StopExplanation.Of(S(k, 1))!;
            Assert.False(Regex.IsMatch(e.Line, "[A-Za-z]"), $"内部名（英字）を画面に出さない: {e.Line}");
            Assert.Contains("1件", e.Line);
        }
        Assert.Contains("45 秒", StopExplanation.Of(S(V6FinalPort.StopKind.NormalStall, 3))!.Line);
        Assert.Null(StopExplanation.NextLabel(StopNext.None));
    }
}
