using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

public class TimeSummaryTest
{
    private static V6FinalPort.TimeSummary Summary(bool over = false) => new(
        438_066L,
        new[] { ("探索", 50_017L), ("連鎖", 0L), ("解き直し", 370_016L), ("統合", 1_003L), ("後処理", 8_967L), ("追加精製", 8_062L) },
        new[] { ("C1玉突き連鎖", 4_105L), ("MembraneCellLns", 1_730L) },
        over);

    [Fact]
    public void longestPhaseFirstWithPostDetail() =>
        Assert.Equal(
            "所要 438秒: 解き直し 370秒（84%） ・ 探索 50秒（11%） ・ 後処理 9.0秒（2%）（C1玉突き連鎖 4.1秒・MembraneCellLns 1.7秒） ・ 追加精製 8.1秒（1%） ・ 統合 1.0秒（0%）",
            Summary().OpLine());

    [Fact]
    public void overBudgetIsMarked() =>
        Assert.StartsWith("所要 438秒（予算超過）: ", Summary(over: true).OpLine());

    [Fact]
    public void allPhasesUnderOneSecond() =>
        Assert.Equal("所要 0.8秒: 内訳はどれも1秒未満",
            new V6FinalPort.TimeSummary(800L, new[] { ("探索", 500L), ("後処理", 300L) }, Array.Empty<(string, long)>(), false).OpLine());
}
