using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3703Test</c> の 1 対 1 移植。3.703.0: 人員不足の手伝い（H）と変更の確認（D）の文言。</summary>
public class Review3703Test
{
    private static readonly IReadOnlyList<IReadOnlyList<int>> Snap = new IReadOnlyList<int>[] { new[] { 0, 0, 1 }, new[] { 1, 0, 0 }, new[] { 0, 2, 0 } };
    private static int[][] Arr => Snap.Select(r => r.ToArray()).ToArray();
    private static readonly string[] Names = { "甲", "乙", "丙" };
    private static readonly string[] Syms = { "休", "日", "夜" };
    private static readonly FixSuggestion Sug = new(FixKind.Chain, new[] { new FixCell(0, 2, 2), new FixCell(2, 2, 1), new FixCell(1, 0, 2) }, "（玉突き）", -1, 1,
        new (string, int)[] { ("covU", -1), ("covO", 1) });
    private static readonly FixTarget Need = new(null, 2, 2, 1, true, "必要人数");

    [Fact]
    public void GuidedFixTitlePerStage()
    {
        Assert.Equal("10/3の「夜」に入る人", FixOutcomeText.GuidedFixTitle(GuidedStage.Candidates, "10/3", "夜"));
        Assert.Equal("10/3の「夜」を入れ替えで埋める", FixOutcomeText.GuidedFixTitle(GuidedStage.Chain, "10/3", "夜"));
        Assert.Equal("10/3の「夜」は埋められません", FixOutcomeText.GuidedFixTitle(GuidedStage.Blocked, "10/3", "夜"));
        Assert.Equal("残りの人員不足は埋められません", FixOutcomeText.GuidedFixTitle(GuidedStage.Blocked, null, null));
        Assert.Equal(FixOutcomeText.ShortageDoneTitle, FixOutcomeText.GuidedFixTitle(GuidedStage.Done, "10/3", "夜"));
    }

    [Fact]
    public void StatusPointsAndCandidateLabel()
    {
        Assert.Equal(new[] { "いま1人／必要2人", "動かせる人が3人います" }, FixOutcomeText.GuidedStatusPoints(2, 1, 3));
        Assert.Equal(new[] { "いま1人／必要2人" }, FixOutcomeText.GuidedStatusPoints(2, 1, 0));
        Assert.Equal("山田さんを「A4」に入れる（休から）", FixOutcomeText.GuidedCandidateLabel("山田", "A4", "休"));
        Assert.Equal("山田さんを「A4」に入れる", FixOutcomeText.GuidedCandidateLabel("山田", "A4", null));
        Assert.Equal("山田さんを「A4」に入れる", FixOutcomeText.GuidedCandidateLabel("山田", "A4", ""));
        Assert.Equal("残っている必須違反を順に見る（4か所）", FixOutcomeText.GuidedTourButton(4));
        Assert.Equal("ほかの必須違反（1件）が残っています", FixOutcomeText.OtherHardAfterShortage(new Dictionary<string, int> { ["covU"] = 2, ["c3n"] = 1 }));
    }

    [Fact]
    public void SummaryLineWithAndWithoutTarget()
    {
        var e = Need.Effect(Snap, Sug.Ops);
        Assert.Equal("今回の不足 1人 → 0人（解消）・3か所を変更・新たな必須違反なし", ChainFixPreview.PreviewSummaryLine(e, 3, -1, 0));
        Assert.Equal("必須違反は減ります・1か所を変更・新たな必須違反は未評価（適用の直前に検査）", ChainFixPreview.PreviewSummaryLine(null, 1, -1, null));
        Assert.Equal("2か所を変更・新たな必須違反 1件", ChainFixPreview.PreviewSummaryLine(null, 2, 0, 1));
        // 区切りごとに単位は 1 つ（INV-07）、未評価を「なし」と言わない（INV-10）。
        var units = new[] { "人", "か所", "件", "回", "pt" };
        foreach (var line in new[] { ChainFixPreview.PreviewSummaryLine(e, 3, -1, 0), ChainFixPreview.PreviewSummaryLine(null, 2, 1, 1) })
            foreach (var seg in line.Split('・'))
                Assert.True(units.Count(u => System.Text.RegularExpressions.Regex.IsMatch(seg, $@"\d+\s*{u}")) <= 1, seg);
        Assert.DoesNotContain("なし", ChainFixPreview.PreviewSummaryLine(null, 1, 0, null));
    }

    [Fact]
    public void PurposeAndEffectFilledOnChainAndCandidatePaths()
    {
        var basePreview = ChainFixPreview.Of(Sug, Arr, Names, Syms, "2026-10-01") with { FixTarget = Need, NewHard = Array.Empty<ViolationKey>() };
        var p = ChainFixPreview.FillPreview(basePreview, Snap, Syms, "2026-10-01", Names);
        Assert.Equal("10/3の夜の不足を減らす", p.Purpose);
        Assert.Equal(new[] { "10/3の夜：0人 → 1人", "今回の不足 1人 → 0人", "必要 1人を満たします" }, p.Effect);
        Assert.Equal("今回の不足 1人 → 0人（解消）・3か所を変更・新たな必須違反なし", p.Summary);
    }

    [Fact]
    public void ListAndContestPathsKeepCallerPurposeAndEffect()
    {
        var e = Need.Effect(Snap, Sug.Ops);
        var eff = ChainFixPreview.PreviewEffectLines("10/3の夜", e, Need.Basis);
        var basePreview = ChainFixPreview.Of(Sug, Arr, Names, Syms, "2026-10-01") with { Purpose = "推奨A", Effect = eff, FromList = true, FixTarget = Need };
        var p = ChainFixPreview.FillPreview(basePreview, Snap, Syms, "2026-10-01", Names);
        Assert.Equal("推奨A", p.Purpose);
        Assert.Equal(eff, p.Effect);
        Assert.EndsWith("新たな必須違反は未評価（適用の直前に検査）", p.Summary!);
    }

    [Fact]
    public void NoTargetLeavesPurposeEmpty()
    {
        var p = ChainFixPreview.FillPreview(ChainFixPreview.Of(Sug, Arr, Names, Syms, "2026-10-01"), Snap, Syms, "2026-10-01", Names);
        Assert.Null(p.Purpose);
        Assert.Empty(p.Effect);
        Assert.StartsWith("必須違反は減ります", p.Summary!);
        Assert.Equal("この3か所の変更を適用する", CellSheetLogic.ApplyCountButton(3));
    }
}
