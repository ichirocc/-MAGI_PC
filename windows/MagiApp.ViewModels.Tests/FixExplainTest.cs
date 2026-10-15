using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>付随の変更の理由（1 つずつ外して正式な検査で数え直す）と、必須違反が変わらない案の添え書き（Kotlin <c>FixExplainTest</c>）。</summary>
public class FixExplainTest
{
    private readonly MagiEngine.Model.MagiState st = FixTiersTest.Board12x16();
    private readonly int[][] s;

    public FixExplainTest() { s = FixTiersTest.Sched(st); }

    [Fact]
    public void SplitsPrimaryInsideTheEntryFrame()
    {
        var ops = new[] { new FixCell(0, 1, FixTiersTest.REST), new FixCell(0, 3, FixTiersTest.D) };
        var (p, x) = FixExplain.SplitPrimary(FixTiersTest.Over, st.Schedule, ops);
        Assert.Equal(new[] { ops[0] }, p); Assert.Equal(new[] { ops[1] }, x);
    }

    [Fact]
    public void ExtraReasonComesFromTheFamilyThatGrowsWithoutIt()
    {
        var swap = new FixSuggestion(FixKind.SwapXDay, new[] { new FixCell(0, 1, FixTiersTest.REST), new FixCell(0, 3, FixTiersTest.D) }, "swap", -1, -1, Array.Empty<(string, int)>());
        var e = FixExplain.ExplainExtras(st, s, FixTiersTest.Over, swap);
        Assert.True(e.AllExplained);
        Assert.True(!e.MainlyOutOfFrameHard);
        // D の 2 連続（金沢 12/15→12/16）がついでに直る。
        Assert.True(e.Incidental.Any(i => i.Contains("金沢さん")), string.Join(",", e.Incidental));
        var lines = FixExplain.ExtraReasonLines(e, st.Schedule, st.StaffList.Select(x => x.Name).ToList(), st.Shifts.Select(x => x.Kigou).ToList(), st.StartDate);
        Assert.True(lines[0].StartsWith("合わせて変える勤務：金沢さん ") && lines[1].StartsWith("　理由：入れないと"), string.Join(",", lines));
    }

    [Fact]
    public void HardUnchangedLinesOnlyWhenRequiredRemain()
    {
        var soft = new FixSuggestion(FixKind.Change, Array.Empty<FixCell>(), "x", 0, 0, new[] { ("covO", -2), ("c3", 1) });   // Kotlin の写しは covO -1（-10+15=+5 で重みが減らず 2 行目が出ない）＝重みの和が負になる -2 にする
        Assert.Equal(new[] { "必須違反は変わりません（要調整だけが変わります）", "件数は同じで、重い要調整が減ります" }, FixExplain.HardUnchangedLines(soft, true));
        Assert.Empty(FixExplain.HardUnchangedLines(soft, false));
        Assert.Empty(FixExplain.HardUnchangedLines(soft with { DeltaHard = -1 }, true));
    }
}
