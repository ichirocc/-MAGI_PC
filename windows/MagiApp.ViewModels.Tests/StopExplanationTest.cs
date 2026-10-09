using System.Text.RegularExpressions;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.643.0] 終わり方の一文は種別ごとに次の一手が決まり、内部名（英字）を含まない（Kotlin <c>StopExplanationTest</c> と 1 対 1）。</summary>
public class StopExplanationTest
{
    private static V6FinalPort.StopSummary S(V6FinalPort.StopKind kind, int hard, bool early = true) =>
        new(early, kind, UsedSec: 84, BudgetSec: 300, StalledSec: 45, RemainingHard: hard);

    [Fact]
    public void NothingToSayWhenTheDeadlineEndedAPerfectBoard() =>
        Assert.Null(StopExplanation.Of(S(V6FinalPort.StopKind.Deadline, 0, early: false)));

    [Fact]
    public void EarlyStopWithNoHardLeftSaysSoWithoutANextStep()
    {
        var e = StopExplanation.Of(S(V6FinalPort.StopKind.PlateauFloor, 0))!;
        Assert.Equal(StopNext.None, e.Next);
        Assert.Contains("必須違反はありません", e.Line);
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
            Assert.Contains("1 件", e.Line);
        }
        Assert.Contains("45 秒", StopExplanation.Of(S(V6FinalPort.StopKind.NormalStall, 3))!.Line);
        Assert.Null(StopExplanation.NextLabel(StopNext.None));
    }
}
