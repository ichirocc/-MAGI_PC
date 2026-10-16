using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3700Test</c> の 1 対 1 移植。3.700.0: D03 争点カード（推奨A／案B を既存の主一覧の上位 2 件から）。</summary>
public class Review3700Test
{
    // 職員 3 人 × 1 日。勤務 0＝A、1＝休。A は必要 2 人で 1 人不足。
    private static readonly IReadOnlyList<IReadOnlyList<int>> Board = new[] { new[] { 0 }, new[] { 1 }, new[] { 1 } };
    private static readonly IReadOnlyList<string> Names = new[] { "古泉", "上條", "佐藤" };
    private static readonly IReadOnlyList<string> Syms = new[] { "A", "休" };
    private static readonly FixTarget Under = new(null, 0, 0, 2, true, "必要人数");
    private static FixSuggestion Sug(int staff, string label) => new(FixKind.Change, new[] { new FixCell(staff, 0, 0) }, label, -1, -1, Array.Empty<(string, int)>());
    private static readonly FixSuggestion S1 = Sug(1, "上條出勤");
    private static readonly FixSuggestion S2 = Sug(2, "佐藤出勤");
    private static readonly FixSuggestion S3 = Sug(1, "上條出勤（別）");
    private static ContestCard? Card(IReadOnlyList<FixSuggestion> main, FixTarget? t = null) => CellSheetLogic.ContestCardOf(t ?? Under, "10/1のA", main, Board, Names, Syms);

    [Fact]
    public void NoSuggestionsNoCard() => Assert.Null(Card(Array.Empty<FixSuggestion>()));

    [Fact]
    public void OneSuggestionShowsAOnly()
    {
        var c = Card(new[] { S1 })!;
        Assert.Same(S1, c.A.Suggestion);
        Assert.Null(c.B);
    }

    [Fact]
    public void AAndBFollowTheExistingListOrder()
    {
        var c = Card(new[] { S2, S1, S3 })!;
        Assert.Same(S2, c.A.Suggestion);
        Assert.Same(S1, c.B!.Suggestion);
        Assert.Equal("推奨A: 佐藤出勤", c.A.Label);
        Assert.Equal("案B: 上條出勤", c.B!.Label);
        Assert.Null(c.B!.Reason);
    }

    [Fact]
    public void ReasonComesFromTheSuggestionsRecountedEffect()
    {
        var c = Card(new[] { S1, S2 })!;
        var e = Under.Effect(Board, S1.Ops);
        Assert.Equal($"理由: {e.AfterNote(Under.Basis)}（{e.GapLine()}）", c.A.Reason);
        Assert.Equal("10/1のAの不足", c.Title);
    }

    [Fact]
    public void BothTargetTheSameProblemAndNonFixesAreSkipped()
    {
        var noop = new FixSuggestion(FixKind.Change, new[] { new FixCell(1, 0, 1) }, "変わらない", 0, 0, Array.Empty<(string, int)>());
        var c = Card(new[] { noop, S1, S2 })!;
        foreach (var o in new[] { c.A, c.B! }) Assert.True(Under.Effect(Board, o.Suggestion.Ops).Fixes);
        Assert.Null(Card(new[] { S1 }, new FixTarget(null, 0, 0, 0, false, "適正人数")));
    }
}
