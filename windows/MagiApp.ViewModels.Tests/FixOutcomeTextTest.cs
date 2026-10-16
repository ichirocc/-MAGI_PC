namespace MagiApp.ViewModels.Tests;

/// <summary>[Android 3.643.0 同期] 直したあとの 1 行は、同じ問題について「解消したか」と「ほかに何件残るか」を言う（Kotlin <c>FixOutcomeTextTest</c> と 1 対 1）。</summary>
public class FixOutcomeTextTest
{
    [Fact]
    public void AppliedMoveSaysWhatChangedAndWhatRemains()
    {
        Assert.Equal("山本 10/12「夜」→「休」 を適用しました。必須違反 3件→2件。ほかの必須違反は 2件 残っています。", FixOutcomeText.Applied("山本 10/12「夜」→「休」", 3, 2, "40件", "38件"));
        Assert.Equal("入替 を適用しました。必須違反はなくなりました（要調整 40件→38件）。", FixOutcomeText.Applied("入替", 1, 0, "40件", "38件"));
        Assert.Equal("入替 を適用しました。必須違反は変わらず 2件（要調整 40件→39件）。", FixOutcomeText.Applied("入替", 2, 2, "40件", "39件"));
        // [3.678.0] 内訳があれば単位ごとに（人員不足は人）。
        Assert.Equal("入替 を適用しました。必須違反は変わらず 1件・2人（要調整 40件→39件）。",
            FixOutcomeText.Applied("入替", 3, 3, "40件", "39件", new Dictionary<string, int> { ["c3n"] = 1, ["covU"] = 2 }));
    }

    /// <summary>[3.663.0] 当てたあとの内訳: 解消・減少と新たに生じた問題を分け、場所だけ移った違反も言う（仕様 §8）。</summary>
    [Fact]
    public void ResultDetailSeparatesResolvedAppearedAndMoved()
    {
        static MagiEngine.V6.ViolationReport Rep(Dictionary<string, string[]> cells, Dictionary<string, int> b)
        {
            var hard = b.Where(kv => MagiEngine.V6.MirrorKeys.Hard.Contains(kv.Key)).Sum(kv => kv.Value);
            return new MagiEngine.V6.ViolationReport(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
                b, b.Values.Sum(), hard, b.Values.Sum() - hard, 0.0, CellFamilies: cells.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value));
        }
        var labels = new Dictionary<string, string> { ["c3n"] = "禁止の並び", ["c1"] = "期間の制約", ["weekly"] = "曜日の偏り", ["c3mn"] = "回避の並び", ["covU"] = "人員不足" };
        string L(string k) => labels.TryGetValue(k, out var v) ? v : k;
        var before = Rep(new() { ["0,3"] = new[] { "vio-c3n" }, ["1,2"] = new[] { "vio-c1" }, ["2,2"] = new[] { "vio-covU" } }, new() { ["c3n"] = 1, ["c1"] = 1, ["covU"] = 1, ["weekly"] = 4 });
        var after = Rep(new() { ["1,5"] = new[] { "vio-c1" }, ["2,2"] = new[] { "vio-covU" } }, new() { ["c1"] = 1, ["covU"] = 1, ["weekly"] = 2, ["c3mn"] = 1 });
        Assert.Equal("解消・減少した違反: 禁止の並び 1件・曜日の偏り 2pt。新たに生じた問題: 回避の並び +1件・期間の制約（場所が変わった）。新たな必須違反はありません（全体の必須違反は 1人）。",
            FixOutcomeText.Detail(before, after, L));
        Assert.Equal("解消・減少した違反: 禁止の並び 1件。新たに生じた問題: なし。",
            FixOutcomeText.Detail(Rep(new() { ["0,3"] = new[] { "vio-c3n" } }, new() { ["c3n"] = 1 }), Rep(new(), new()), L));
    }

    [Fact]
    public void GuidedFixSaysResolvedOrStillShort()
    {
        Assert.Equal("10/12(土) の「夜」の人員不足を解消しました。ほかの必須違反は 3件 残っています。", FixOutcomeText.Guided("10/12(土)", "夜", null, 3));
        Assert.Equal("10/12(土) の「夜」の人員不足を解消しました。必須違反はなくなりました。", FixOutcomeText.Guided("10/12(土)", "夜", null, 0));
        Assert.Equal("10/12(土) の「夜」はまだ 1人 足りません。必須違反は 4件です。", FixOutcomeText.Guided("10/12(土)", "夜", 1, 4));
    }
}
