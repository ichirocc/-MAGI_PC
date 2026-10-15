using static MagiApp.ViewModels.KeptResultText;

namespace MagiApp.ViewModels.Tests;

public class KeptResultTextTest
{
    [Fact]
    public void weightedDecidesWhenTotalDropped()
    {
        var now = new Score(5, 48412.4, 369); var prev = new Score(5, 48230.0, 376);
        Assert.Equal(
            "再作成した案は前の勤務表より良くなかったため採用していません（再作成した案：必須違反 2件・要調整 なし／前の勤務表：必須違反 なし・要調整 3件）。勤務表は変わっていません。",
            Screen(new Dictionary<string, int> { ["c3n"] = 2 }, new Dictionary<string, int> { ["c1"] = 3 }));
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

    /// <summary>計算完了の通知は「計算が終わった」と「必須違反 0 件＝配布前確認へ」を分ける（仕様 §9）。</summary>
    [Fact]
    public void completedTextSeparatesDoneFromZeroHard()
    {
        Assert.Equal("計算が完了しました。設定した必須条件を満たしています。配布前の確認に進んでください（要調整 12件）",
            CompletedText.Of(0, new Dictionary<string, int> { ["c1"] = 12 }, ""));
        Assert.Equal("計算が完了しました（閉じている間に計算）。必須違反 2件・1人・要調整 34件・6回",
            CompletedText.Of(3, new Dictionary<string, int> { ["covU"] = 1, ["c3n"] = 2, ["low"] = 6, ["c1"] = 34 }, "（閉じている間に計算）"));
    }
}
