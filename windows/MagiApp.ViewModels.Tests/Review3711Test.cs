namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3711Test</c> の 1 対 1 移植。自動の案探しの記録、前回と同じ結果、続けて 0 件の案内。</summary>
public class Review3711Test
{
    [Fact]
    public void AutoSearchLinesAreDistinct()
    {
        Assert.Equal("案を探す（自動）: 全体", OpLogText.FixSearchStartLine(OpLogText.FixOriginAll, auto: true));
        Assert.Equal("案（自動）: 2件（3秒）", OpLogText.FixSearchEndLine(2, 3_500L, auto: true));
        Assert.Equal("案（自動）: なし（1秒）", OpLogText.FixSearchEndLine(0, 1_000L, auto: true));
        Assert.Equal("案: なし（1秒）", OpLogText.FixSearchEndLine(0, 1_000L));
    }

    [Fact]
    public void HesitationSkipsAutoSearches()
    {
        var msgs = new[]
        {
            OpLogText.FixSearchStartLine(OpLogText.FixOriginAll, auto: true), OpLogText.FixSearchEndLine(3, 1_000L, auto: true),
            OpLogText.FixSearchStartLine(OpLogText.FixOriginAll, auto: true), OpLogText.FixSearchEndLine(0, 1_000L, auto: true),
            OpLogText.FixSearchStartLine("11/3のAｱ"), OpLogText.FixSearchEndLine(0, 2_000L),
        };
        var h = OpLogText.HesitationOf(msgs, msgs.Select((_, i) => i * 1_000L).ToList());
        Assert.Equal(1, h.Searches); Assert.Equal(0, h.SearchesWithResult); Assert.Equal(1, h.SearchesEmpty);
    }

    [Fact]
    public void SameResultNoticeAndVisibility()
    {
        Assert.Equal("前回と同じ結果です。条件を変えない限り、この必須違反3件は消えません。" + AnalysisTriage.SameResultNextDraft, AnalysisTriage.SameResultNotice(3));
        Assert.Equal("前回と同じ結果です。" + AnalysisTriage.SameResultNextDraft, AnalysisTriage.SameResultNotice(0));
        Assert.False(AnalysisTriage.IsSameRunResult(null, (1L, 2L)));
        Assert.True(AnalysisTriage.IsSameRunResult((1L, 2L), (1L, 2L)));
        Assert.False(AnalysisTriage.IsSameRunResult((1L, 2L), (1L, 3L)));
        Assert.False(AnalysisTriage.IsSameRunResult((1L, 2L), (9L, 2L)));
        IReadOnlyList<IReadOnlyList<int>> b = new[] { new[] { 0, 1 } };
        // [3.716.0] Android も同じ鍵（状態指紋）に揃えた。
        var n = new SameResultNote(3, 4L, b);
        Assert.True(AnalysisTriage.SameResultVisible(n, 4L, new[] { new[] { 0, 1 } }));
        Assert.False(AnalysisTriage.SameResultVisible(n, 5L, b));
        Assert.False(AnalysisTriage.SameResultVisible(n, 4L, new[] { new[] { 1, 1 } }));
        Assert.False(AnalysisTriage.SameResultVisible(null, 4L, b));
        Assert.Equal("同じ結果（盤面指紋 00000000000000ab）", OpLogText.SameResultLine(0xabL));
    }

    [Fact]
    public void EmptyStreakCountsOnlySameOriginAndBoard()
    {
        var a = AnalysisTriage.NextEmptySearchStreak(null, "k|全体", 1L, 2L, 0);
        Assert.Equal(1, a!.Count);
        Assert.Equal(2, AnalysisTriage.NextEmptySearchStreak(a, "k|全体", 1L, 2L, 0)!.Count);
        Assert.Equal(1, AnalysisTriage.NextEmptySearchStreak(a, "j|11/3", 1L, 2L, 0)!.Count);
        Assert.Equal(1, AnalysisTriage.NextEmptySearchStreak(a, "k|全体", 5L, 2L, 0)!.Count);
        Assert.Equal(1, AnalysisTriage.NextEmptySearchStreak(a, "k|全体", 1L, 6L, 0)!.Count);
        Assert.Null(AnalysisTriage.NextEmptySearchStreak(a, "k|全体", 1L, 2L, 1));
        Assert.Equal(RepeatedEmptyAction.Wishes, AnalysisTriage.RepeatedEmptyActionOf(true, true));
        Assert.Equal(RepeatedEmptyAction.Settings, AnalysisTriage.RepeatedEmptyActionOf(false, true));
        Assert.Null(AnalysisTriage.RepeatedEmptyActionOf(false, false));
        Assert.Equal("続けて案がありませんでした", AnalysisTriage.RepeatedEmptyLine);
    }
}
