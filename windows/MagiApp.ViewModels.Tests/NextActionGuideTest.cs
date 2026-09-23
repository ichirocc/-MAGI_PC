using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>FixImpactLinesTest</c> と <c>InvolvedWishesTest</c>（3.612.0 思考誘導 S1/S3）の移植。</summary>
public class NextActionGuideTest
{
    private static readonly Dictionary<string, string> Labels = new() { ["high"] = "上限超過", ["covO"] = "人員過剰", ["c1"] = "期間の制約", ["covU"] = "人員不足" };
    private static string LabelOf(string k) => Labels.TryGetValue(k, out var v) ? v : k;

    private static FixSuggestion S(int dh, params (string, int)[] diff) =>
        new(FixKind.Change, Array.Empty<FixCell>(), "x", dh, 0, diff);

    [Fact]
    public void HardLineComesFromDeltaHard()
    {
        Assert.Equal("必須の約束: 減る（2件）", NextActionGuide.FixImpactLines(S(-2), LabelOf).HardLine);
        Assert.Equal("必須の約束: 変わらない", NextActionGuide.FixImpactLines(S(0), LabelOf).HardLine);
        Assert.Equal("必須の約束: 増える（1件）", NextActionGuide.FixImpactLines(S(1), LabelOf).HardLine);
    }

    [Fact]
    public void CautionListsOnlyWorsenedSoftFamilies()
    {
        var (_, caution) = NextActionGuide.FixImpactLines(S(-1, ("covU", -1), ("high", 1), ("c1", -1), ("covO", 2)), LabelOf);
        Assert.Equal("注意: 上限超過 +1・人員過剰 +2", caution);
        Assert.Null(NextActionGuide.FixImpactLines(S(-1, ("covU", -1)), LabelOf).Caution);
    }

    [Fact]
    public void ListsWishesInvolvedInHardViolations()
    {
        var ui = new UiState
        {
            StaffNames = new[] { "山田", "佐藤" },
            Wishes = new Dictionary<string, int> { ["1,4"] = 0 },
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>>
            {
                ["0,2"] = new[] { "vio-pref" },
                ["1,3"] = new[] { "vio-c3w" },
                ["1,4"] = new[] { "vio-c3n" },
                ["0,5"] = new[] { "vio-c3n" },      // 希望でないセルの禁止の並び＝対象外
                ["0,6"] = new[] { "vio-c1" },
            },
        };
        var got = NextActionGuide.InvolvedWishes(ui).Select(w => (w.Name, w.Day, w.Reason)).ToList();
        Assert.Equal(new[]
        {
            ("山田", 2, "希望の勤務になっていません"),
            ("佐藤", 4, "前日（4日）に置けない勤務が入っています"),
            ("佐藤", 4, "希望が禁止の並びに掛かっています"),
        }, got);
    }

    [Fact]
    public void OneCellWithPrefAndC3wListsBothWishes()
    {
        // 同じセルに希望違反と希望前日の禁止が重なる＝そのセルの希望と翌日の希望の両方が関わる。
        var ui = new UiState
        {
            StaffNames = new[] { "山田" },
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>> { ["0,2"] = new[] { "vio-pref", "vio-c3w" } },
        };
        Assert.Equal(new[] { (2, "希望の勤務になっていません"), (3, "前日（3日）に置けない勤務が入っています") },
            NextActionGuide.InvolvedWishes(ui).Select(w => (w.Day, w.Reason)).ToList());
    }
}
