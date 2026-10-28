using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>backlog#46 T02: 変更→CSV 保存→元に戻す→再検査→保存一致表示。Kotlin <c>CsvSavedMarkTest</c> の移植（sample_state_v6 の代わりに oct2026 の実データ）。</summary>
public class CsvSavedMarkTest
{
    private static readonly MagiState St = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));
    private static int[][] Board() => St.Schedule.ToIntArray2D();
    private static int[][] Flipped(int[][] b)
    {
        var c = b.Copy2D();
        c[0][0] = c[0][0] == 0 ? 1 : 0;
        return c;
    }

    [Fact]
    public void SavedLineIsBoundToTheExportedContent()
    {
        var m = new CsvSavedMark();
        var b = Board();
        m.NoteExport(St, b, ScheduleCsvBridge.Build(St, b));
        m.NoteSaved(St, b);
        Assert.StartsWith("この内容で勤務表CSVを保存済みです（10:00", m.Line("10:00", St, b));
    }

    [Fact]
    public void SavedLineDistinguishesTheSavedContentFromTheCurrentOne()
    {
        var m = new CsvSavedMark();
        var b = Board();
        m.NoteExport(St, b, ScheduleCsvBridge.Build(St, b));
        var changed = Flipped(b);
        m.NoteSaved(St, changed);
        Assert.Contains("今の内容と違います", m.Line("10:00", St, changed));
        m.Clear();
        Assert.Null(m.Line("10:00", St, changed));
    }

    [Fact]
    public void ChangeSaveUndoRecheckShowsSavedMatch()
    {
        var m = new CsvSavedMark();
        var b0 = Board();
        var b1 = Flipped(b0);
        m.NoteExport(St, b1, ScheduleCsvBridge.Build(St, b1));
        m.NoteSaved(St, b1);
        Assert.True(m.SavedMatches(St, b1));
        Assert.False(m.SavedMatches(St, b0));
        Assert.Contains("今の内容と違います", m.Line("10:00", St, b0));
        var redone = Flipped(b0);
        Assert.True(m.SavedMatches(St, redone));
        var settingsOnly = St with { };
        Assert.True(m.SavedMatches(settingsOnly, redone));
        Assert.Null(m.Line(null, St, redone));
        Assert.False(m.SavedMatches(null, redone));
    }
}
