using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[S5] 試算の候補（§2.2・§2.3）と結果の文（§5）。Kotlin <c>WishTrialCandidatesTest</c>（§12 T9・T10）の移植。</summary>
public class WishTrialCandidatesTest
{
    private static CoverageShortfall Shortfall(int day, int shift, params int[] pinned) => new(
        day, $"{day + 1}日", shift, "日", Need: 2, Got: 1, Miss: 1, Capacity: 1,
        Verdict: CoverageVerdict.Infeasible, Reason: "", WishPinned: pinned);

    [Fact]
    public void T9_DedupeRepresentativeReasonC3wYUnlockedAndShortfallRows()
    {
        var ui = new UiState
        {
            StaffNames = new[] { "山田", "佐藤", "鈴木" },
            ShiftSymbols = new[] { "休", "日", "夜" },
            Wishes = new Dictionary<string, int> { ["0,2"] = 1, ["1,3"] = 2, ["1,4"] = 1, ["2,5"] = 1, ["0,6"] = 0, ["2,6"] = 2 },
            LockedWishKeys = new HashSet<string> { "0,2", "1,3", "1,4", "0,6", "2,6" },
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>>
            {
                ["0,2"] = new[] { "vio-pref", "vio-c3n" },   // 同じ (職員, 日) ＝pref を代表、c3n は「ほか」
                ["1,3"] = new[] { "vio-c3w" },               // 翌日 (1,4) の X と、前日自身の希望 Y (1,3)
                ["2,5"] = new[] { "vio-c3n" },               // 担当できない勤務の希望＝Locked=false
            },
            CoverageDiag = new CoverageDiagnosis(2, 2, 0, new[] { Shortfall(6, 1, 2, 0), Shortfall(2, 1, 0) }, Array.Empty<string>(),
                0, Array.Empty<CoverageSurplus>()),
        };
        var c = NextActionGuide.WishTrialCandidatesOf(ui);
        Assert.Equal(new[]
        {
            new WishTrialRow(0, 2, "山田", "希望の勤務になっていません（ほか: 禁止の並び・人手不足の日）", true),
            new WishTrialRow(1, 3, "佐藤", "翌日（5日）の希望の勤務の前日に置けない勤務の希望です", true),
            new WishTrialRow(1, 4, "佐藤", "前日（4日）に置けない勤務が入っています", true),
            new WishTrialRow(2, 5, "鈴木", "希望が禁止の並びに掛かっています", false),
        }, c.Direct);
        // (0,2) は S5a が代表＝S5b の 3日 の枠は空になって消える。枠は日順、行は職員順。
        var g = Assert.Single(c.Shortfall);
        Assert.Equal("7日 日 1人不足", g.Header);
        Assert.Equal(new[] { new WishTrialRow(0, 6, "山田", "休の希望", true), new WishTrialRow(2, 6, "鈴木", "夜の希望", true) }, g.Rows);
    }

    [Fact]
    public void T9_C3wWithoutLockedYListsOnlyX()
    {
        var ui = new UiState
        {
            StaffNames = new[] { "山田" },
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>> { ["0,1"] = new[] { "vio-c3w" } },
            LockedWishKeys = new HashSet<string> { "0,2" },
        };
        Assert.Equal(new[] { 2 }, NextActionGuide.WishTrialCandidatesOf(ui).Direct.Select(r => r.Day));
    }

    // 実データ（古泉 10/25-27 の休の希望と「休→休→休」禁止、福澤 10/1 Dﾃ・10/2 休と前日の禁止）の形。
    private static UiState SelfUi(Dictionary<string, IReadOnlyList<string>> fams) => new()
    {
        StaffNames = new[] { "古泉", "福澤" },
        ShiftSymbols = new[] { "休", "日", "Dﾃ" },
        Wishes = new Dictionary<string, int> { ["0,24"] = 0, ["0,25"] = 0, ["0,26"] = 0, ["1,0"] = 2, ["1,1"] = 0 },
        LockedWishKeys = new HashSet<string> { "0,24", "0,25", "0,26", "1,0", "1,1" },
        WishSelfConflicts = new[]
        {
            new WishSelfConflict(0, "c3n", new[] { 24, 25, 26 }, new[] { 0, 0, 0 }),
            new WishSelfConflict(1, "c3w", new[] { 0, 1 }, new[] { 2, 0 }),
        },
        ViolationCellFamilies = fams,
    };

    [Fact]
    public void T9_PrefCellInWishSelfConflictListsItsSiblingWishes()
    {
        var ui = SelfUi(new() { ["0,24"] = new[] { "vio-pref" }, ["1,1"] = new[] { "vio-pref" } });
        Assert.Equal(new[]
        {
            new WishTrialRow(0, 24, "古泉", "希望の勤務になっていません", true),
            new WishTrialRow(0, 25, "古泉", "希望どうしが禁止の並び「休→休→休」を作っています", true),
            new WishTrialRow(0, 26, "古泉", "希望どうしが禁止の並び「休→休→休」を作っています", true),
            new WishTrialRow(1, 0, "福澤", "希望どうしが前日の禁止「Dﾃ→休」に当たっています", true),
            new WishTrialRow(1, 1, "福澤", "希望の勤務になっていません", true),
        }, NextActionGuide.WishTrialCandidatesOf(ui).Direct);
    }

    [Fact]
    public void T9_SelfConflictWithoutPrefCellAddsNothing()
    {
        // 並びが成立している（c3n の印が窓の全セル）＝行は今までどおり。pref の無い組は兄弟を足さない。
        var held = SelfUi(new() { ["0,24"] = new[] { "vio-c3n" }, ["0,25"] = new[] { "vio-c3n" }, ["0,26"] = new[] { "vio-c3n" } });
        var direct = NextActionGuide.WishTrialCandidatesOf(held).Direct;
        Assert.Equal(new[] { 24, 25, 26 }, direct.Select(r => r.Day));
        Assert.Equal(new[] { "希望が禁止の並びに掛かっています" }, direct.Select(r => r.Reason).Distinct());
        Assert.Empty(NextActionGuide.WishTrialCandidatesOf(SelfUi(new())).Direct);
    }

    [Fact]
    public void T9_SiblingRowsDedupeAcrossOverlappingWindowsAndWithShortfall()
    {
        // 休の希望 4 連日＝窓 [1,2,3] と [2,3,4]。2日が崩れると兄弟は 1・3・4日 を 1 行ずつ。4日 は人手不足の枠にも出る＝S5a が代表。
        var ui = new UiState
        {
            StaffNames = new[] { "大島" },
            ShiftSymbols = new[] { "休", "日" },
            Wishes = Enumerable.Range(1, 4).ToDictionary(d => $"0,{d}", _ => 0),
            LockedWishKeys = Enumerable.Range(1, 4).Select(d => $"0,{d}").ToHashSet(),
            WishSelfConflicts = new[]
            {
                new WishSelfConflict(0, "c3n", new[] { 1, 2, 3 }, new[] { 0, 0, 0 }),
                new WishSelfConflict(0, "c3n", new[] { 2, 3, 4 }, new[] { 0, 0, 0 }),
            },
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>> { ["0,2"] = new[] { "vio-pref" } },
            CoverageDiag = new CoverageDiagnosis(1, 1, 0, new[] { Shortfall(4, 1, 0) }, Array.Empty<string>(), 0, Array.Empty<CoverageSurplus>()),
        };
        var c = NextActionGuide.WishTrialCandidatesOf(ui);
        Assert.Equal(new[] { 1, 2, 3, 4 }, c.Direct.Select(r => r.Day));
        Assert.Equal("希望どうしが禁止の並び「休→休→休」を作っています（ほか: 人手不足の日）", c.Direct.Last().Reason);
        Assert.Empty(c.Shortfall);
    }

    private static WishTrial.Result R(int h0, int hx, int rk, int rr)
    {
        var a = h0 - hx; var pk = Math.Min(h0, rk); var pc = Math.Min(hx, rr); var att = pk - pc;
        return new WishTrial.Result(h0, hx, rk, rr, a, att, Math.Min(a, Math.Max(0, att)), Math.Max(0, att - a), pk, pc);
    }

    [Fact]
    public void T10_SevenWordings()
    {
        Assert.Equal("取り消すと必須違反が確実に1件 減り、もう一度つくるとさらに2件 減る見込みです。", NextActionGuide.WishTrialText(R(5, 4, 5, 2)));
        Assert.Equal("取り消すと必須違反が確実に1件 減ります。", NextActionGuide.WishTrialText(R(5, 4, 5, 4)));
        Assert.Equal("取り消してもう一度つくると、必須違反が2件 減る見込みです。", NextActionGuide.WishTrialText(R(5, 5, 5, 3)));
        Assert.Equal("この試算では、減る見込みは見つかりませんでした（もう一度つくると減ることはあります）。", NextActionGuide.WishTrialText(R(5, 5, 5, 5)));
        Assert.Equal("もう一度つくるだけの場合より、さらに1件 減る見込みです。", NextActionGuide.WishTrialText(R(5, 5, 3, 2)));
        Assert.Equal("取り消さなくても、もう一度つくるだけで同じだけ減る見込みです。", NextActionGuide.WishTrialText(R(5, 4, 3, 3)));
        Assert.Equal("試算できませんでした（未割当のセルがあります）。", NextActionGuide.WishTrialText(new WishTrial.Unavailable("未割当のセルがあります")));
        Assert.Null(NextActionGuide.WishTrialText(WishTrial.Stopped));
        Assert.Equal("希望を残したまま、もう一度つくるだけで必須違反が2件 減る見込みです。", NextActionGuide.WishTrialKeepOnlyText(new WishTrial.Control(5, 3)));
        Assert.Null(NextActionGuide.WishTrialKeepOnlyText(new WishTrial.Control(5, 5)));
    }
}
