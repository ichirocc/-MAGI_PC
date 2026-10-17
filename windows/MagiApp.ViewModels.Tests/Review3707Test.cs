namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3707Test</c> の 1 対 1 移植。3.707.0: 編集タブ① 月次条件の見出しカード・希望の行の数え方（人）・ぶつかっている希望の要約（W-1）。
/// 寄せたときに選んだ日が残るか（3a-4）は画面の動きなので CI と実機で確かめる。
/// </summary>
public class Review3707Test
{
    private static readonly EditHeaderCounts Ready = new(Month: "2026年11月", Staff: 5, Shifts: 4, Wish: new WishEntryCounts(3, 1, 2),
        NeedStdOk: true, NeedExceptions: 2, Issues: 0);

    [Fact]
    public void MonthlyHeaderHasTitleSubtitleAndThreeOneUnitPoints()
    {
        var p = CellSheetLogic.EditHeaderPlanOf(0, Ready, panelOpen: false, ranBeforeEdit: false, running: false);
        Assert.Equal("月次条件（2026年11月）", p.Title);
        Assert.Equal(CellSheetLogic.EditScopeMonthlyNote, p.Subtitle);
        Assert.Equal(new[] { "希望・休暇 入力あり 3人（拡張希望のみ 1人）・未入力 2人", "必要人数 標準あり・例外 2件", "入力診断 問題なし" }, p.Points);
        Assert.True(p.Points.Count <= 3);
        Assert.Equal(EditHeaderPrimary.EditNeed, p.Primary);
        Assert.Null(p.Secondary);
        Assert.True(CellSheetLogic.EditBarSecondary(p));
    }

    [Fact]
    public void MonthlyPrimaryTable()
    {
        EditHeaderPrimary? Prim(EditHeaderCounts? c = null, bool panel = false, bool edited = false, bool running = false) =>
            CellSheetLogic.EditHeaderPlanOf(0, c ?? Ready, panel, edited, running).Primary;
        Assert.Equal(EditHeaderPrimary.EditNeed, Prim());
        Assert.Equal(EditHeaderPrimary.Rerun, Prim(edited: true));
        Assert.Equal(EditHeaderPrimary.Panel, Prim(panel: true, edited: true));
        Assert.Null(Prim(panel: true, running: true));
        Assert.Equal(EditHeaderPrimary.OpenMaster, Prim(Ready with { Shifts = 0, Staff = 0 }));
        Assert.Equal(EditHeaderPrimary.AddStaff, Prim(Ready with { Staff = 0 }));
        // 塗りは 1 つ（Q10）: Rerun のときだけ下部バーが塗り。
        Assert.False(CellSheetLogic.EditBarSecondary(CellSheetLogic.EditHeaderPlanOf(0, Ready, false, true, false)));
        Assert.True(CellSheetLogic.EditBarSecondary(CellSheetLogic.EditHeaderPlanOf(0, Ready, true, true, false)));
        Assert.False(CellSheetLogic.EditBarSecondary(CellSheetLogic.EditHeaderPlanOf(0, Ready, false, false, true)));
    }

    [Fact]
    public void MonthlyPointsVariants()
    {
        var p = CellSheetLogic.EditHeaderPlanOf(0, Ready with { Month = "", NeedStdOk = false, NeedExceptions = 0, Issues = 3, WishConflicts = true }, false, false, false);
        Assert.Equal("月次条件", p.Title);
        Assert.Equal("必要人数 標準が未設定・例外 0件", p.Points[1]);
        Assert.Equal("入力診断 見直し 3件", p.Points[2]);
        Assert.Equal(EditHeaderSecondary.WishConflicts, p.Secondary);
    }

    [Fact]
    public void WishEntryTextCountsPeopleInNin()
    {
        Assert.Equal("入力あり 2人・未入力 1人", WishEntryCounts.Of(3, new[] { "0,1", "1,2", "1,3" }, Array.Empty<string>()).Text());
        Assert.DoesNotContain("名", new WishEntryCounts(3, 1, 2).Text());
    }

    [Fact]
    public void WishCardTexts()
    {
        Assert.Equal("佐藤さん：希望 3件・拡張希望 1件", CellSheetLogic.WishStaffSummary("佐藤", 3, 1));
        Assert.Equal("ぶつかっている希望を見る（佐藤さん 11/24(火)）", CellSheetLogic.WishConflictLinkText("佐藤", "11/24(火)"));
        Assert.Equal("3日を夜の希望にする", CellSheetLogic.WishApplyButton(3, "夜"));
        Assert.Equal("2日を選んだシフト以外にする", CellSheetLogic.ExtWishApplyButton(2));
        Assert.Equal("4日に適用する", CellSheetLogic.NeedApplyButton(4));
        Assert.Equal("⚠「夜」は佐藤さんの担当外です。希望は登録できますが、配置すると必須違反になります。", CellSheetLogic.WishOutOfScopeNote("夜", "佐藤"));
        Assert.Equal("拡張希望を登録できませんでした。希望のある日だけです。日かシフトを選び直して、もう一度登録してください。", CellSheetLogic.ExtWishFailedText("希望のある日だけです。"));
    }

    [Fact]
    public void WishConflictSummaryShowsOnlyNonZeroPoints()
    {
        static WishTrialRow R(int s, int d, bool only) => new(s, d, $"n{s}", "理由", Locked: true, WishOnly: only);
        var c = new WishTrialCandidates(new[] { R(0, 1, true), R(1, 2, false) },
            new[] { new ShortfallWishGroup(3, 0, "4日 日勤 1人不足", new[] { R(2, 3, false), R(2, 3, false), R(4, 3, false) }) });
        Assert.Equal(new[] { "必須違反に関わる希望 2件", "人員不足の日に別の勤務の希望 2人", "うち計算では消せない 1件" }, CellSheetLogic.WishConflictSummary(c));
        Assert.Equal(new[] { "必須違反に関わる希望 1件" }, CellSheetLogic.WishConflictSummary(new WishTrialCandidates(new[] { R(1, 2, false) }, Array.Empty<ShortfallWishGroup>())));
        Assert.Empty(CellSheetLogic.WishConflictSummary(new WishTrialCandidates(Array.Empty<WishTrialRow>(), Array.Empty<ShortfallWishGroup>())));
    }

    [Fact]
    public void EditReturnBandPutsProblemFirstInTwoLines()
    {
        var r = new EditReturn("なおし方", EditReturn.Guided, Problem: "11/24(火) の A4 が 1人 足りません");
        Assert.Equal(new[] { "11/24(火) の A4 が 1人 足りません", "直したら「なおし方」へ戻れます。" }, CellSheetLogic.EditReturnLines(r));
        Assert.Equal(new[] { EditReturn.Line(r with { Problem = null }) }, CellSheetLogic.EditReturnLines(r with { Problem = null }));
        Assert.Null(CellSheetLogic.EditReturnProblem(Array.Empty<MagiEngine.V6.CoverageShortfall>(), 0, 3));
        Assert.Null(CellSheetLogic.EditReturnProblem(Array.Empty<MagiEngine.V6.CoverageShortfall>(), null, null));
    }
}
