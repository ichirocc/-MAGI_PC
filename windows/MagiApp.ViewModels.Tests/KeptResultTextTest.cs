using static MagiApp.ViewModels.KeptResultText;

namespace MagiApp.ViewModels.Tests;

public class KeptResultTextTest
{
    [Fact]
    public void weightedDecidesWhenTotalDropped()
    {
        var now = new Score(5, 48412.4, 369); var prev = new Score(5, 48230.0, 376);
        Assert.Equal(
            "今回（必須5・重み48,412・合計369）は前回（必須5・重み48,230・合計376）より改善しませんでした（重みが大きいため。比べる順＝必須→重み→合計）。前回の結果を維持します。",
            Screen(now, prev));
        Assert.Equal(
            "再実行: 今回 必須5・重み48,412・合計369 は前回 必須5・重み48,230・合計376 以下に改善せず（重みが大きいため）→ 前回を維持",
            Log("再実行", now, prev));
    }

    [Fact]
    public void hardThenTotalDecide()
    {
        Assert.Equal("必須が多いため", Reason(new Score(6, 1.0, 1), new Score(5, 9.0, 9)));
        Assert.Equal("合計が多いため", Reason(new Score(5, 9.0, 10), new Score(5, 9.0, 9)));
        Assert.Equal("同じ点数のため", Reason(new Score(5, 9.0, 9), new Score(5, 9.0, 9)));
    }
}
