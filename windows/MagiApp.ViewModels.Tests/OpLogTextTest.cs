namespace MagiApp.ViewModels.Tests;

/// <summary>[3.694.0] 操作ログの整形（<see cref="OpLogText"/>）。定数は Kotlin <c>OpLogTextTest</c> と同じ値＝片側だけの更新を落とす。</summary>
public class OpLogTextTest
{
    [Fact]
    public void Hex16IsSixteenLowercaseDigitsWithTwosComplementForNegatives()
    {
        Assert.Equal("0000000000000000", OpLogText.Hex16(0L));
        Assert.Equal("0000000000000001", OpLogText.Hex16(1L));
        Assert.Equal("ffffffffffffffff", OpLogText.Hex16(-1L));
        Assert.Equal("8000000000000000", OpLogText.Hex16(long.MinValue));
    }

    [Fact]
    public void PeriodUsesIsoDatesAndTheDayCount()
    {
        Assert.Equal("2026-10-01〜2026-10-31（31日）", OpLogText.Period("2026-10-01", "2026-10-31", 31));
        Assert.Equal("状態指紋=0000000000000001 ・ 盤面指紋=ffffffffffffffff", OpLogText.Keys(1L, -1L));
    }

    [Fact]
    public void DropNoteCountsOnlyAboveTheCapAndNeverMentionsARunNumber()
    {
        Assert.Equal(1000, OpLogText.OpLogCap);
        Assert.Equal(0, OpLogText.Dropped(1000, OpLogText.OpLogCap));
        Assert.Equal(1, OpLogText.Dropped(1001, OpLogText.OpLogCap));
        Assert.Equal("上限1000件を超えて捨てた古い操作は 0 件です。", OpLogText.DropNote(0));
        Assert.Equal("上限1000件を超えて捨てた古い操作は 234 件です。", OpLogText.DropNote(234));
        var note = OpLogText.Note("2026/10/16 10:07:12", 0);
        Assert.Equal("※この記録は 2026/10/16 10:07:12 からの分です。上限1000件を超えて捨てた古い操作は 0 件です。", note);
        Assert.DoesNotContain("実行#", note);
    }
}
