using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[つくる前の確認] 分類と指紋（<see cref="PreRunCheck"/>）。Kotlin <c>PreRunCheckTest.kt</c> の逐語移植。</summary>
public class PreRunCheckTest
{
    private static readonly MagiState St = StateJsonSerializer.Parse(FixtureLoader.ReadRaw("oct2026_grid_state.json"));
    private static int[][] Board() => St.Schedule.Select(r => r.ToArray()).ToArray();

    [Fact]
    public void RealData_Counts()
    {
        var s = PreRunCheck.Build(St, Board());
        Assert.Equal(new[] { "c3n", "c3w", "c3n", "c3n" }, s.WishConflicts.Select(g => g.Family));
        Assert.Empty(s.ImpossibleWishes);
        Assert.Empty(s.ForcedShortfalls);
        Assert.Equal(new[] { 8, 9, 10, 28 }, s.DayProofs.Select(d => d.Day));
        Assert.Equal(new[] { 3 }, s.StaffProofs.Select(c => c.Staff));
        Assert.Equal(new[] { (7, 1, 0), (10, 12, 0) }, s.WishOverCaps.Select(w => (w.Staff, w.Wished, w.Hi)));
        Assert.Equal(9, s.FloorCount);
        Assert.Equal(4, s.RerunClears.Count);
        Assert.Equal(new PreRunCheck.WallHint(22, 8, 7, 7), s.Wall);
        Assert.True(s.NeedsSheet);
    }

    [Fact]
    public void RerunClearsMatchRelaxTrialHandPlaced()
    {
        var pairs = PreRunCheck.Build(St, Board()).RerunClears.Select(c => (c.Staff, c.Shift)).ToHashSet();
        Assert.Equal(RelaxTrial.HandPlaced(St, Board()).Select(r => (r.Staff, r.Shift)).ToHashSet(), pairs);
    }

    [Fact]
    public void EmptyWhenNothingToSay()
    {
        var s = new PreRunCheck.Summary(Array.Empty<WishSelfConflict>(), Array.Empty<ImpossibleWish>(), Array.Empty<ForcedCovU>(),
            Array.Empty<ConstraintMus.DayConflict>(), Array.Empty<ConstraintMus.StaffConflict>(), Array.Empty<PreRunCheck.WishOverCap>(),
            Array.Empty<PreRunCheck.HandPlacedCell>(), null);
        Assert.False(s.NeedsSheet);
        Assert.Null(PreRunCheck.WallHintOf(new List<(int, int)>()));
        var overCapOnly = s with { WishOverCaps = new[] { new PreRunCheck.WishOverCap(0, 1, 2, 0) } };
        Assert.False(overCapOnly.NeedsSheet);
    }

    [Fact]
    public void WallHint_TieGoesToLowerIndex() =>
        Assert.Equal(new PreRunCheck.WallHint(4, 2, 1, 2), PreRunCheck.WallHintOf(new List<(int, int)> { (3, 1), (1, 1), (3, 2), (1, 2) }));

    [Fact]
    public void Fingerprint_ChangesWithBoardAndWishes()
    {
        var f0 = PreRunCheck.Fingerprint(St, Board());
        Assert.Equal(f0, PreRunCheck.Fingerprint(St, Board()));
        var b2 = Board(); b2[0][0] = (b2[0][0] + 1) % St.Shifts.Count;
        Assert.NotEqual(f0, PreRunCheck.Fingerprint(St, b2));
        var w = St.Wishes.ToDictionary(kv => kv.Key, kv => kv.Value); w.Remove(w.Keys.First());
        Assert.NotEqual(f0, PreRunCheck.Fingerprint(St with { Wishes = w }, Board()));
    }
}
