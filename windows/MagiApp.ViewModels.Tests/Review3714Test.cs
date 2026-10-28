using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3714Test</c> の 1 対 1 移植。3.714.0: 3.713.0 で見送ったデザイン例の残り（問題一覧の 1 行・人員不足の文字ボタン・
/// 希望の前日の行・案なしの主ボタン・セルのシートのルールの 1 行・設定／条件の見出し・未作成の見出し）。決定済みのボタンの語は変えない。
/// </summary>
public class Review3714Test
{
    private static readonly CoverageShortfall Sf = new(9, "10/10(土)", 0, "Aｱ", Need: 1, Got: 0, Miss: 1, Capacity: 3, Verdict: CoverageVerdict.Fixable, Reason: "",
        WishPinned: new[] { 4 });

    private static string Label(string f) => f == "c41" ? "グループのレンジ" : f;

    [Fact]
    public void ProblemLinePerFamily()
    {
        Assert.Equal("10/10のAｱが1人不足", CellSheetLogic.ProblemLine("covU", "", "10/10", "Aｱ"));
        Assert.Equal(CellSheetLogic.ShortageHeadlineOf(Sf).Headline, CellSheetLogic.ProblemLine("covU", "", "10/10", "Aｱ"));
        Assert.Equal("福澤 10/1 希望の前日", CellSheetLogic.ProblemLine("c3w", "福澤", "10/1", "Dﾃ", "休"));
        Assert.Equal("夜勤があと1回不足", CellSheetLogic.ProblemLine("low", "", "10/1", "夜勤"));
        Assert.Equal("福澤 夜勤があと2回不足", CellSheetLogic.ProblemLine("low", "福澤", "10/1", "夜勤", n: 2));
        Assert.Equal("福澤 夜勤が1回多い", CellSheetLogic.ProblemLine("high", "福澤", "10/1", "夜勤"));
        Assert.Equal("夜勤→早番の並び", CellSheetLogic.ProblemLine("c3n", "", "10/1", "夜勤", "早番"));
        Assert.Equal("福澤 夜勤→早番の並び", CellSheetLogic.ProblemLine("c3n", "福澤", "10/1", "夜勤", "早番"));
        Assert.Equal("福澤 10/1 希望と違う勤務です", CellSheetLogic.ProblemLine("pref", "福澤", "10/1", "A"));
        Assert.Equal("福澤 10/1 担当外の勤務", CellSheetLogic.ProblemLine("groupViol", "福澤", "10/1", "A"));
    }

    [Fact]
    public void ProblemLineRowsAndLimits()
    {
        var items = new[] { new TourItem("c3w", 0, new[] { 0, 1 }, "希望の前日の禁止 Dﾃ→休 ・ 10/1〜10/2", Syms: new[] { "Dﾃ", "休" }, DayMd: "10/1") };
        var rows = CellSheetLogic.HardViolationRows(items, new[] { "福澤" }, new HashSet<string>());
        Assert.Equal("福澤 10/1 希望の前日", Assert.Single(rows).Line);
        Assert.Equal("残っている問題 4件", CellSheetLogic.HardListTitle("4件"));
        foreach (var f in new[] { "covU", "c3w", "low", "high", "c3n", "pref", "extWish", "groupViol", "c41" })
        {
            var l = CellSheetLogic.ProblemLine(f, "とても長い名前の職員さん", "12/31", "長い記号の勤務", "長い記号の勤務", 12, Label);
            Assert.True(l.Length <= CellSheetLogic.ProblemLineMax, l);
        }
    }

    [Fact]
    public void ShortageDetailAndWishDayBefore()
    {
        Assert.Equal("希望を見る", GuidedFixRules.ShortageWishButton);
        Assert.Equal("担当を編集", GuidedFixRules.ShortageSkillButton);
        Assert.Equal(new EditLanding(0, null, 4, Day: 9), GuidedFixRules.ShortageWishLanding(Sf));
        Assert.Equal(new EditLanding(2, "yr_ws1"), GuidedFixRules.ShortageSkillLanding());
        Assert.Equal("翌日の休の前に置けない勤務", NextActionGuide.C3wShortLine(true, "休"));
        Assert.Equal("前日のDﾃと並べられない希望", NextActionGuide.C3wShortLine(false, "Dﾃ"));
        Assert.Equal("希望のまま", NextActionGuide.WishKeepButton);
        Assert.Equal("相談", NextActionGuide.WishConsultShort);
        Assert.Equal("保留", NextActionGuide.WishHoldShort);
    }

    [Fact]
    public void NoFixCellSheetTabsAndHome()
    {
        Assert.Equal("問題一覧", AnalysisTriage.NoFixListButton);
        Assert.Equal("閉じる", AnalysisTriage.NoFixCloseButton);
        Assert.Equal("希望の前日ルールに当たります", CellSheetLogic.CellRuleLine("c3w", Label));
        Assert.Equal("詳しく ▸", CellSheetLogic.CellDetailButton);
        Assert.Equal("設定", CellSheetLogic.SettingsHeading);
        Assert.Equal("組む時間 1分", CellSheetLogic.BudgetShortLine(60));
        Assert.Equal("組む時間 10秒", CellSheetLogic.BudgetShortLine(10));
        Assert.Equal("組む時間を変える", CellSheetLogic.BudgetChangeButton);
        Assert.Equal("条件", CellSheetLogic.EditTabHeading);
        Assert.Equal("希望・人数・基本", CellSheetLogic.EditTabLine);
        Assert.Equal("まだ作っていません", CellSheetLogic.HomeHeadline(HomePlanKind.Make));
        Assert.Equal("データを開く", CellSheetLogic.EmptyStateTitle);
        Assert.Equal("勤務を見る", ConsultList.Open);
    }

    [Fact]
    public void LengthLimits()
    {
        foreach (var h in new[] { CellSheetLogic.SettingsHeading, CellSheetLogic.EditTabHeading, CellSheetLogic.HomeMakeHeadline, CellSheetLogic.EmptyStateTitle, CellSheetLogic.HardListTitle("12件") })
            Assert.True(h.Length <= 12, h);
        var fams = new[] { "c3n", "c3mn", "c3w", "pref", "extWish", "groupViol", "covU", "covO", "low", "high", "c1", "c41s", "c42s" };
        var labels = new Dictionary<string, string> { ["low"] = "下限割れ", ["high"] = "上限超過", ["c1"] = "期間の制約", ["c41s"] = "スキルグループのレンジ", ["c42s"] = "スキルグループペア" };
        var lines = fams.Select(f => CellSheetLogic.CellRuleLine(f, k => labels.GetValueOrDefault(k, k)))
            .Concat(new[] { CellSheetLogic.EditTabLine, CellSheetLogic.BudgetShortLine(300), NextActionGuide.C3wShortLine(true, "Dﾃ"), NextActionGuide.C3wShortLine(false, "Pｼ"), CellSheetLogic.EmptyStateSubtitle(false) });
        foreach (var l in lines) Assert.True(l.Length <= 22, l);
    }
}
