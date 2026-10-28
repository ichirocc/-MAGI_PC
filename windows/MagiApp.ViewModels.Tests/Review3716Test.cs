namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3716Test</c> の 1 対 1 移植。3.716.0 論理監査の是正: 問題一覧の 1 行は名前と日を残して型の側を切る（INV-01）、希望の行の語、
/// 禁止の並びは全日をつなぐ、案なしの 1 行は締切なら時間の行、続けて 0 件は人の操作だけ数える、札は検査済みのときだけ、分析の一文の単位。
/// </summary>
public class Review3716Test
{
    [Fact]
    public void ProblemLineKeepsPrefixAndTruncatesBody()
    {
        // 前置き（名前と日）は残し、型の側を「…」で切る＝長い名前でも行の区別がつく。
        Assert.Equal("福澤俊陽 10/1 希望と違う勤務…", CellSheetLogic.ProblemLine("pref", "福澤俊陽", "10/1", "A"));
        Assert.Equal("とても長い名前の職員さん 12/3…", CellSheetLogic.ProblemLine("pref", "とても長い名前の職員さん", "12/31", "A"));   // 前置きだけで 17 字を超える
        Assert.Equal("とても長い名前の職員さんです 12…", CellSheetLogic.ProblemLine("groupViol", "とても長い名前の職員さんです", "12/31", "A"));
        Assert.Equal("福澤 10/1 希望と違う勤務です", CellSheetLogic.ProblemLine("pref", "福澤", "10/1", "A"));
        Assert.Equal("福澤 10/1 拡張希望と違う勤務", CellSheetLogic.ProblemLine("extWish", "福澤", "10/1", "A"));
        foreach (var f in new[] { "covU", "c3w", "low", "high", "c3n", "pref", "extWish", "groupViol", "c41" })
        {
            var l = CellSheetLogic.ProblemLine(f, "とても長い名前の職員さんですよ", "12/31", "長い記号の勤務", "長い記号の勤務", 12, x => x == "c41" ? "グループのレンジ" : x, new[] { "長い記号", "長い記号", "長い記号" });
            Assert.True(l.Length <= CellSheetLogic.ProblemLineMax, l);
            if (f != "covU") Assert.StartsWith("とても長い名前の職員さんですよ", l);
        }
    }

    [Fact]
    public void ForbiddenRunJoinsAllSymbols()
    {
        Assert.Equal("福澤 夜→夜→夜の並び", CellSheetLogic.ProblemLine("c3n", "福澤", "10/1", "夜", "夜", syms: new[] { "夜", "夜", "夜" }));
        Assert.Equal("夜→夜→夜→夜→夜→夜→夜→夜→夜…", CellSheetLogic.ProblemLine("c3n", "", "10/1", "夜", "夜", syms: Enumerable.Repeat("夜", 12).ToList()));
        Assert.Equal("禁止の並びです", CellSheetLogic.ProblemLine("c3n", "", "10/1", "夜"));
        Assert.Equal("福澤 禁止の並びです", CellSheetLogic.ProblemLine("c3n", "福澤", "10/1", "夜", syms: new[] { "夜" }));
        var item = new TourItem("c3n", 0, new[] { 0, 1, 2 }, "禁止の並び 夜→夜→夜 ・ 10/1〜10/3", Syms: new[] { "夜", "夜", "夜" }, DayMd: "10/1");
        Assert.Equal("福澤 夜→夜→夜の並び", CellSheetLogic.TourProblemLine(item, "福澤"));
        Assert.Equal("福澤 10/1 希望と違う勤務です", CellSheetLogic.TourProblemLine(new TourItem("pref", 0, new[] { 0 }, "希望 A ・ 10/1", Syms: new[] { "A" }, DayMd: "10/1"), "福澤"));
        // C# のみ: 型の無い族は labelOf の族名（Kotlin の breakdownLabels と同じ）。
        Assert.Equal("福澤 10/1 グループのレンジ", CellSheetLogic.TourProblemLine(new TourItem("c41", 0, new[] { 0 }, "", Syms: new[] { "A" }, DayMd: "10/1"), "福澤", f => f == "c41" ? "グループのレンジ" : f));
    }

    [Fact]
    public void NoFixLineSaysTimeWhenTimedOut()
    {
        Assert.Equal("時間内に見つかりませんでした", AnalysisTriage.NoFixLineOf(false, timedOut: true));
        Assert.Equal(AnalysisTriage.NoFixTimeoutLine, AnalysisTriage.NoFixLineOf(true, timedOut: true));
        Assert.Equal(AnalysisTriage.RepeatedEmptyLine, AnalysisTriage.NoFixLineOf(true, timedOut: false));
        Assert.Equal(AnalysisTriage.NoFixLine, AnalysisTriage.NoFixLineOf(false));
        Assert.True(AnalysisTriage.NoFixTimeoutLine.Length <= 22);
    }

    [Fact]
    public void EmptyStreakIgnoresAutoSearches()
    {
        var a = AnalysisTriage.NextEmptySearchStreak(null, "k|全体|False", 1L, 2L, 0);
        // 自動の探索は数えも途切れさせもしない。
        Assert.Same(a, AnalysisTriage.NextEmptySearchStreak(a, "k|全体|False", 1L, 2L, 0, auto: true));
        Assert.Same(a, AnalysisTriage.NextEmptySearchStreak(a, "k|全体|False", 1L, 2L, 3, auto: true));
        Assert.Null(AnalysisTriage.NextEmptySearchStreak(null, "k|全体|False", 1L, 2L, 0, auto: true));
        Assert.Equal(2, AnalysisTriage.NextEmptySearchStreak(a, "k|全体|False", 1L, 2L, 0)!.Count);
        // セルのシート（quick）と全面は別の鍵。
        Assert.Equal(1, AnalysisTriage.NextEmptySearchStreak(a, "k|全体|True", 1L, 2L, 0)!.Count);
    }

    [Fact]
    public void BadgesOnlyAfterCheckAndCountLineUnits()
    {
        var plan = CellSheetLogic.HomePlanOf(false, true, true);
        Assert.DoesNotContain(HomeSection.Badges, CellSheetLogic.HomeCardSectionsOf(plan, new UiState { Loaded = true, HasResult = true, EngineRan = true, CheckRev = 0 }));
        Assert.Contains(HomeSection.Badges, CellSheetLogic.HomeCardSectionsOf(plan, new UiState { Loaded = true, HasResult = true, EngineRan = true, CheckRev = 1 }));
        var made = new UiState { Loaded = true, HasResult = true, EngineRan = true, CheckRev = 1, BestHard = 0L, BestSoft = 5L, Breakdown = new Dictionary<string, int> { ["c1"] = 5 } };
        Assert.Equal("必須0件 · 要調整5件", AnalysisTriage.AnalysisSummaryOf(made, null).Sentence);
    }

    [Fact]
    public void SameResultKeyedByStateFingerprint()
    {
        IReadOnlyList<IReadOnlyList<int>> b = new[] { new[] { 0, 1 } };
        var n = new SameResultNote(3, 7L, b);
        Assert.True(AnalysisTriage.SameResultVisible(n, 7L, b));
        Assert.False(AnalysisTriage.SameResultVisible(n, 8L, b));
        Assert.False(AnalysisTriage.SameResultVisible(n, 7L, new[] { new[] { 1, 1 } }));
    }
}
