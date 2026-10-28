namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>DayDetailTest</c>（3.727.1）の 1 対 1 移植。日の詳細（日ヘッダのシート）の行: 入っている職員・必要人数との対比・日送りの端。</summary>
public class DayDetailTest
{
    private static readonly string[] Sym = { "休", "Pｼ", "Dﾃ", "Aｱ" };
    // 職員 0〜4 の 1 日目（添字 0）だけを使う。
    private static readonly IReadOnlyList<IReadOnlyList<int>> Sched = new[] { new[] { 1 }, new[] { 1 }, new[] { 2 }, new[] { 0 }, new[] { 3 } };
    private static (int, int)? Limits(int k, int _) => k switch { 1 => (2, 2), 2 => (2, 2), 3 => (0, 0), _ => null };

    [Fact]
    public void RowsShowWhoIsOnEachShiftAndTheNeed()
    {
        var rows = DayDetail.DaySlotRows(Sched, Sym, 0, (k, j) => Limits(k, j));
        Assert.Equal(new[] { 0, 1, 2, 3 }, rows.Select(r => r.Shift));
        Assert.Equal(new[] { 0, 1 }, rows[1].Staff);
        Assert.Equal("休　1人", rows[0].Line);
        Assert.Equal("Pｼ　2/2人　充足", rows[1].Line);
        Assert.Equal("Dﾃ　1/2人　1人不足", rows[2].Line);
        Assert.Equal("Aｱ　1人（適正 0人）　1人多い", rows[3].Line);
        Assert.Equal(new[] { DaySlotState.None, DaySlotState.Ok, DaySlotState.Under, DaySlotState.Over }, rows.Select(r => r.State));
    }

    [Fact]
    public void ShiftsWithNobodyAndNoNeedAreLeftOut()
    {
        var rows = DayDetail.DaySlotRows(new[] { new[] { 1 } }, Sym, 0, (_, _) => null);
        Assert.Equal(new[] { 1 }, rows.Select(r => r.Shift));
    }

    [Fact]
    public void ANeededShiftWithNobodyStillShows()
    {
        var rows = DayDetail.DaySlotRows(new[] { new[] { 0 } }, Sym, 0, (k, _) => k == 2 ? (1, 1) : null);
        Assert.Equal("Dﾃ　0/1人　1人不足", rows.Single(r => r.Shift == 2).Line);
    }

    [Fact]
    public void DayStepStopsAtTheEdges()
    {
        Assert.Null(DayDetail.DayStep(0, -1, 30));
        Assert.Equal(1, DayDetail.DayStep(0, 1, 30));
        Assert.Null(DayDetail.DayStep(29, 1, 30));
    }
}
