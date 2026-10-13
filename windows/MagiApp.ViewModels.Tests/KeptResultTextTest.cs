using static MagiApp.ViewModels.KeptResultText;

namespace MagiApp.ViewModels.Tests;

public class KeptResultTextTest
{
    [Fact]
    public void weightedDecidesWhenTotalDropped()
    {
        var now = new Score(5, 48412.4, 369); var prev = new Score(5, 48230.0, 376);
        Assert.Equal(
            "改善が確認できなかったため、前回の勤務表を維持しました（今回の案は採用していません）。理由：重み付き評価点が高いため（今回 必須5・評価点48,412・合計369／前回 必須5・評価点48,230・合計376。比べる順＝必須→評価点→合計）。",
            Screen(now, prev));
        Assert.Equal(
            "再実行: 今回 必須5・評価点48,412・合計369 は前回 必須5・評価点48,230・合計376 以下に改善せず（重み付き評価点が高いため）→ 前回を維持",
            Log("再実行", now, prev));
    }

    [Fact]
    public void hardThenTotalDecide()
    {
        Assert.Equal("必須違反が多いため", Reason(new Score(6, 1.0, 1), new Score(5, 9.0, 9)));
        Assert.Equal("違反の合計が多いため", Reason(new Score(5, 9.0, 10), new Score(5, 9.0, 9)));
        Assert.Equal("同じ点数のため", Reason(new Score(5, 9.0, 9), new Score(5, 9.0, 9)));
    }

    [Fact]
    public void noteIsAppendedOnlyWhenPresent()
    {
        Assert.Equal("A", KeptResultText.WithNote("A", null));
        Assert.Equal("A B", KeptResultText.WithNote("A", "B"));
    }
}
