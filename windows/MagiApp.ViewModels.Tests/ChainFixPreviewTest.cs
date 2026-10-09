using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.644.0/UX-03] 複数人の入替を当てる前の一覧（Kotlin <c>ChainFixPreviewTest</c> と 1 対 1。必須の増減は <c>NextActionGuide.FixImpactLines</c> が担う）。</summary>
public class ChainFixPreviewTest
{
    private static readonly int[][] Snap = { new[] { 0, 0, 1 }, new[] { 1, 0, 0 }, new[] { 0, 2, 0 } };

    private static FixSuggestion S() => new(FixKind.Chain,
        new[] { new FixCell(0, 2, 2), new FixCell(2, 2, 1), new FixCell(1, 0, 2) }, "（玉突き）10/3 の「夜」を複数人の入替で埋める", -1, 1,
        new (string, int)[] { ("covU", -1), ("covO", 1) });

    [Fact]
    public void ListsEveryChangeWithBeforeAndAfter()
    {
        var p = ChainFixPreview.Of(S(), Snap, new[] { "甲", "乙", "丙" }, new[] { "休", "日", "夜" }, "2026-10-01");
        Assert.Equal("複数人の入れ替え（3 人・3 マス）", p.Title);
        Assert.Equal(new[] { "甲 10/3 日 → 夜", "丙 10/3 休 → 日", "乙 10/1 日 → 夜" }, p.Changes);
        var (hard, caution) = NextActionGuide.FixImpactLines(p.Suggestion, f => f == "covO" ? "人員過剰" : f);
        Assert.Equal("必須違反: 1件減る", hard);
        Assert.Equal("増える要調整: 人員過剰 +1", caution);
    }

    [Fact]
    public void NamesFallBackWhenTheUiHasNone()
    {
        var p = ChainFixPreview.Of(S(), Snap, Array.Empty<string>(), Array.Empty<string>(), "2026-10-01");
        Assert.Equal("職員1 10/3 1 → 2", p.Changes[0]);
    }
}
