using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3698Test</c> の 1 対 1 移植。3.698.0: ホームの主カードを「見出し → 状態 2 行 → 主ボタン 1 つ」に（並びは純関数、対象は INV-01、単位は INV-07）。</summary>
public class Review3698Test
{
    private static UiState Ui(bool running, bool hasResult, bool hard0, bool? engineRan = null) =>
        new() { Loaded = true, Running = running, HasResult = hasResult, BestHard = hard0 ? 0L : 3L, EngineRan = engineRan ?? hasResult, CheckRev = hasResult ? 1 : 0 };

    /// <summary>Review3690Test の表を使い回す: 段ごとに 見出し → 状態 → 主ボタン の順で、塗りの主は 1 つ、でき具合は結果があるときだけ。</summary>
    [Fact]
    public void HomeCardSectionsOrderPerStage()
    {
        foreach (var running in new[] { false, true })
        foreach (var hasResult in new[] { false, true })
        foreach (var hard0 in new[] { false, true })
        {
            var plan = CellSheetLogic.HomePlanOf(running, hasResult, hard0);
            var s = CellSheetLogic.HomeCardSectionsOf(plan, Ui(running, hasResult, hard0)).ToList();
            var @case = $"running={running} hasResult={hasResult} hard0={hard0} kind={plan.Kind}";
            Assert.True(s.SequenceEqual(s.Distinct()), @case);
            Assert.True((running ? 0 : 1) == s.Count(x => x == HomeSection.Primary), @case);
            if (running) { Assert.True(s.SequenceEqual(new[] { HomeSection.Progress }), @case); continue; }
            Assert.True(s.IndexOf(HomeSection.Headline) < s.IndexOf(HomeSection.Status), @case);
            Assert.True(s.IndexOf(HomeSection.Status) < s.IndexOf(HomeSection.Primary), @case);
            Assert.True(s.IndexOf(HomeSection.Primary) < s.IndexOf(HomeSection.Helper), @case);
            Assert.True(s.IndexOf(HomeSection.Helper) < s.IndexOf(HomeSection.Detail), @case);
            Assert.True(hasResult == s.Contains(HomeSection.Satisfaction), @case);
            Assert.True(hasResult == s.Contains(HomeSection.Creation), @case);
            Assert.True(hasResult == s.Contains(HomeSection.Badges), @case);
            if (hasResult) Assert.True(s.IndexOf(HomeSection.Detail) < s.IndexOf(HomeSection.Satisfaction) && s.IndexOf(HomeSection.Satisfaction) < s.IndexOf(HomeSection.Info), @case);
        }
        // 結果はあるが自動作成を経ていない（手で編集）＝作成の行は出ない。
        Assert.DoesNotContain(HomeSection.Creation, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, false), Ui(false, true, false, engineRan: false)));
    }

    [Fact]
    public void OthersLinkHiddenWhenPrimaryGoesToAnalysis()
    {
        var u = Ui(false, true, false);
        Assert.DoesNotContain(HomeSection.Others, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, false), u));                       // LIST
        Assert.DoesNotContain(HomeSection.Others, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, false, hasMove: true), u));       // APPLY_MOVE
        Assert.Contains(HomeSection.Others, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, false, guided: true), u));              // GUIDED_FIX
        Assert.Contains(HomeSection.Others, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, false, wishes: true), u));              // WISHES
        Assert.DoesNotContain(HomeSection.Primary, CellSheetLogic.HomeCardSectionsOf(CellSheetLogic.HomePlanOf(false, true, false, fixSearching: true), u)); // SEARCHING は主ボタン無し
    }

    [Fact]
    public void StatusLinesAreAtMostTwo()
    {
        Assert.Equal(new[] { "10/15(水) の Cｵ が 1人 足りません", "未確認事項 2 件" },
            CellSheetLogic.HomeStatusLines(HomePlanKind.GuidedFix, "10/15(水) の Cｵ が 1人 足りません", "未確認事項 2 件", "この内容で勤務表CSVを保存済みです（10:00）。"));
        Assert.Equal(new[] { "未確認事項 2 件", "09:00 に保存した勤務表CSVは、今の内容と違います（配るなら保存し直してください）。" },
            CellSheetLogic.HomeStatusLines(HomePlanKind.PreDist, "", "未確認事項 2 件", "09:00 に保存した勤務表CSVは、今の内容と違います（配るなら保存し直してください）。"));
        Assert.Empty(CellSheetLogic.HomeStatusLines(HomePlanKind.PreDist, "", null, "この内容で勤務表CSVを保存済みです（10:00）。"));
        Assert.Empty(CellSheetLogic.HomeStatusLines(HomePlanKind.Make, "", null, null));
        foreach (var k in Enum.GetValues<HomePlanKind>()) Assert.True(CellSheetLogic.HomeStatusLines(k, "文", "相談", "違います").Count <= 2);
    }

    [Fact]
    public void HeadlinePerStage()
    {
        Assert.Equal("今月の判断", CellSheetLogic.HomeHeadline(HomePlanKind.List));
        Assert.Equal("今月の判断", CellSheetLogic.HomeHeadline(HomePlanKind.GuidedFix));
        Assert.Equal("今月の判断", CellSheetLogic.HomeHeadline(HomePlanKind.Wishes));
        Assert.Equal(CellSheetLogic.HomeMakeHeadline, CellSheetLogic.HomeHeadline(HomePlanKind.Make));
        Assert.Equal(CellSheetLogic.HomeMonthHeadline, CellSheetLogic.HomeHeadline(HomePlanKind.PreDist));
        Assert.DoesNotContain("②", CellSheetLogic.HomeMakeHeadline);
        Assert.DoesNotContain("③", CellSheetLogic.HomeHard0Headline);
    }

    private static readonly CoverageShortfall Shortfall = new(14, "10/15(水)", 2, "Cｵ", Need: 2, Got: 1, Miss: 1, Capacity: 5, Verdict: CoverageVerdict.Fixable, Reason: "");

    [Fact]
    public void ShortageHeadlineNamesShiftAndCount()
    {
        var h = CellSheetLogic.ShortageHeadlineOf(Shortfall);
        Assert.Equal("10/15のCｵが1人不足", h.Headline);
        Assert.Equal("配置 1 / 必要 2", h.Status);
        Assert.Equal("担当・希望の確認", h.Badge);
    }

    /// <summary>「次に確認する問題」は主ボタンの対象と同じ（INV-01）。見出しと同じ対象なら完了の 1 行だけ。</summary>
    [Fact]
    public void CreationNextTargetFollowsStage()
    {
        static string? T(HomePlanKind k) => CellSheetLogic.CreationNextTarget(k, shortage: "10/15(水) Cｵ", move: "山田 10/3", wish: "佐藤 10/8", relax: "鈴木 10/8〜10/9", tour: "福澤さん 禁止の並び");
        Assert.Equal("10/15(水) Cｵ", T(HomePlanKind.GuidedFix));
        Assert.Equal("山田 10/3", T(HomePlanKind.ApplyMove));
        foreach (var k in new[] { HomePlanKind.Wishes, HomePlanKind.WishFloor, HomePlanKind.WishInfeasible }) Assert.Equal("佐藤 10/8", T(k));
        Assert.Equal("鈴木 10/8〜10/9", T(HomePlanKind.Relax));
        Assert.Equal("福澤さん 禁止の並び", T(HomePlanKind.List));
        Assert.Equal("福澤さん 禁止の並び", T(HomePlanKind.Searching));
        foreach (var k in new[] { HomePlanKind.Make, HomePlanKind.PreDist, HomePlanKind.Running, HomePlanKind.DataInfeasible }) Assert.Null(T(k));
        Assert.Equal("10/15(水) Cｵの直し方を見る", AnalysisTriage.HomeTargetLabel("直し方を見る", T(HomePlanKind.GuidedFix), null));
        Assert.Equal(new[] { CellSheetLogic.CreationDoneTitle }, CellSheetLogic.CreationTopLines("10/15(水) Cｵ", headlineTarget: "10/15(水) Cｵ"));
        Assert.Equal(new[] { CellSheetLogic.CreationDoneTitle, "次に確認する問題：X" }, CellSheetLogic.CreationTopLines("X", headlineTarget: "Y"));
    }

    [Fact]
    public void EmptyStateWords()
    {
        Assert.Equal("読み込んでいます…", CellSheetLogic.EmptyStateSubtitle(true));
        Assert.Equal("保存データ・サンプル・新規から選べます", CellSheetLogic.EmptyStateSubtitle(false));
        Assert.Equal("変更内容を確認する", CellSheetLogic.ReviewChangeButton);
        Assert.Equal("必須を見る", CellSheetLogic.HomeListButton);
    }
}
