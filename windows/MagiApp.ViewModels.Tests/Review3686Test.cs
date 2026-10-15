using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3686Test</c> の 1 対 1 移植。3.686.0 のコードレビュー対応（純関数の部分）。</summary>
public class Review3686Test
{
    private readonly MagiEngine.Model.MagiState st = FixTiersTest.Board12x16();

    [Fact]
    public void initBaseKeyChangesOnBoardReplacementOnly()
    {
        Assert.Equal(CellSheetLogic.InitBaseKeyOf(5L, 0L), CellSheetLogic.InitBaseKeyOf(5L, 0L));
        Assert.NotEqual(CellSheetLogic.InitBaseKeyOf(5L, 0L), CellSheetLogic.InitBaseKeyOf(5L, 1L));
        Assert.NotEqual(CellSheetLogic.InitBaseKeyOf(5L, 0L), CellSheetLogic.InitBaseKeyOf(6L, 0L));
    }

    [Fact]
    public void preRunAckReappearsOnlyWhenHandPlacedUpperZeroCellsChange()
    {
        var none = Array.Empty<PreRunCheck.HandPlacedCell>();
        Assert.Equal(StateFingerprint.Of(st), CellSheetLogic.PreRunAckFingerprint(st, none));
        var c = new PreRunCheck.HandPlacedCell(1, 1, FixTiersTest.D);
        Assert.NotEqual(CellSheetLogic.PreRunAckFingerprint(st, none), CellSheetLogic.PreRunAckFingerprint(st, new[] { c }));
        Assert.Equal(CellSheetLogic.PreRunAckFingerprint(st, new[] { c }), CellSheetLogic.PreRunAckFingerprint(st, new[] { c with { } }));
        // 上限 0 の D を手で置いた盤面は外れるセルとして数える
        var capped = st with { StaffRange = new Dictionary<string, Range>(st.StaffRange) { ["1,1"] = new Range("", "0") } };
        Assert.NotEmpty(PreRunCheck.RerunClears(capped, FixTiersTest.Sched(capped)));
    }

    [Fact]
    public void alternativeSummaryByAdoptionOutcome()
    {
        Assert.Equal("案1：必須違反 1件（今と同じ）", CellSheetLogic.AlternativeSummary(1, "1件", 1, 1, worse: false, better: false));
        Assert.Equal("案1：必須違反 1件（今と同じ・全体は今より良い（重みつき））", CellSheetLogic.AlternativeSummary(1, "1件", 1, 1, worse: false, better: true));
        Assert.Equal("案1：必須違反 1件（今と同じ・全体は今より悪い（重みつき））", CellSheetLogic.AlternativeSummary(1, "1件", 1, 1, worse: true, better: false));
        Assert.Equal("案1：必須違反 1件", CellSheetLogic.AlternativeSummary(1, "1件", null, 1, worse: false, better: false));
    }

    [Fact]
    public void removedWishCountsAsAlreadyRemoved()
    {
        var issue = new SettingIssue(IssueKind.Wish, "x", "p", "f", Action: SettingFixAction.RemoveWish, WishKey: "0,0", WishShift: 1);
        var gone = st with { Wishes = new Dictionary<string, int>() };
        Assert.True(SettingFixLogic.WishAlreadyRemoved(gone, issue));
        Assert.False(SettingFixLogic.WishChanged(gone, issue));
        var other = st with { Wishes = new Dictionary<string, int> { ["0,0"] = 0 } };
        Assert.True(SettingFixLogic.WishChanged(other, issue));
        Assert.False(SettingFixLogic.WishAlreadyRemoved(other, issue));
    }

    [Fact]
    public void draftConfirmWithoutCountWhenNotEvaluated()
    {
        Assert.Equal("今の勤務表を下書きで置き換えます。元に戻せます", FixExplain.DraftConfirmText(null));
        Assert.Equal("今の勤務表（必須 2件）を下書きで置き換えます。元に戻せます", FixExplain.DraftConfirmText("2件"));
    }

    [Fact]
    public void progressSummaryUsesUnitsForStartAndSoft()
    {
        var ui = new UiState { BestHard = 2, InitHard = 5, Breakdown = new Dictionary<string, int> { ["covU"] = 2 }, InitBreakdown = new Dictionary<string, int> { ["covU"] = 5 } };
        var t = CellSheetLogic.ProgressSummary(ui);
        Assert.True(t.StartsWith("必須違反 残り2人（開始5人）", StringComparison.Ordinal), t);
        ui.InitBreakdown = new Dictionary<string, int>();
        t = CellSheetLogic.ProgressSummary(ui);
        Assert.True(t.StartsWith("必須違反 残り2人  ", StringComparison.Ordinal), t);
        var soft = new UiState { BestHard = 0, BestSoft = 3, Breakdown = new Dictionary<string, int> { ["high"] = 3 }, InitBreakdown = new Dictionary<string, int> { ["high"] = 7 } };
        t = CellSheetLogic.ProgressSummary(soft);
        Assert.True(t.StartsWith("要調整 3回（開始7回）", StringComparison.Ordinal), t);
    }

    [Fact]
    public void topFixResolvedOnlyWhenBoardAndOpsMatch()
    {
        var ops = new[] { new FixCell(1, 1, 0) };
        IReadOnlyList<IReadOnlyList<int>> sch = new[] { new[] { 1, 1 } };
        var sug = new FixSuggestion(FixKind.Change, ops, "x", -1, -1, Array.Empty<(string, int)>());
        var ui = new UiState { Schedule = sch, FixSuggestions = new[] { sug }, TopFixResolved = new TopFixResolved(ops, sch, new[] { "人員不足：A" }) };
        Assert.Equal(new[] { "人員不足：A" }, CellSheetLogic.TopFixResolvedLabels(ui));
        ui.Schedule = new[] { new[] { 0, 1 } };
        Assert.Empty(CellSheetLogic.TopFixResolvedLabels(ui));
    }

    [Fact]
    public void gateOverloadWithPrebuiltProblemGivesSameOutcome()
    {
        var s = FixTiersTest.Sched(st);
        foreach (var op in new[] { new FixCell(1, 1, 0), new FixCell(0, 1, 0), new FixCell(2, 0, 1) })
        {
            var a = FixApplyGate.Apply(st, s, new[] { op });
            var b = FixApplyGate.Apply(st, s, new[] { op }, new Problem(st), UnifiedViolationChecker.Check(st, s));
            Assert.Equal(a.Applied, b.Applied);
            Assert.Equal(a.Before.Total, b.Before.Total);
        }
    }

    [Fact]
    public void entryMovesSkipShiftsTheOptimizerMayNotPlace()
    {
        var capped = st with { StaffRange = new Dictionary<string, Range>(st.StaffRange) { ["1,1"] = new Range("", "0") } };
        var under = new FixTarget(1, FixTiersTest.D, null, 3, true, "x");
        Assert.Empty(FixTiers.EntryMoves(capped, FixTiersTest.Sched(capped), under, FixTiersTest.REST));
    }
}
