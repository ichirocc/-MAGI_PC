using MagiEngine.Model;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>MagiConstraintsViewTest</c> のうち 3.723.1（backlog #42(h)⑥）の 2 件の移植。golden_state の代わりに oct2026 の実データを使う。</summary>
public class MagiConstraintsViewTest
{
    private static MagiState Load() => StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));

    [Fact]
    public void Cons2InputErrorRejectsMoreThanThePeriod()
    {
        Assert.NotNull(MagiConstraintsView.Cons2InputError("0"));
        Assert.Null(MagiConstraintsView.Cons2InputError("3"));
        Assert.Null(MagiConstraintsView.Cons2InputError(""));
        Assert.NotNull(MagiConstraintsView.Cons2InputError("31", 30));
        Assert.Null(MagiConstraintsView.Cons2InputError("30", 30));
        Assert.Null(MagiConstraintsView.Cons2InputError("40", 0));
    }

    /// <summary>[backlog #42(h)⑥] 取込は断らず、期間を超える行だけエディタと同じ文言で警告する。</summary>
    [Fact]
    public void PeriodOverflowWarningsUseEditorWording()
    {
        var b = Load();
        var t = b.DayCount;
        var k = b.Shifts[0].Kigou;
        var st = b with
        {
            Cons1 = new List<C1Row> { new($"{t + 5}", k, "1"), new($"{t}", k, "1") },
            Cons2 = new List<C2Row> { new(k, $"{t + 1}"), new(k, $"{t}") },
        };
        var w = MagiConstraintsView.PeriodOverflowWarnings(st);
        Assert.Equal(2, w.Count);
        Assert.Equal($"期間の制約「{t + 5}日間に{k} 1回以上」：" + MagiConstraintsView.Cons1InputError($"{t + 5}", "", t), w[0]);
        Assert.StartsWith($"個人の合計「{k} {t + 1}回」：", w[1]);
        Assert.EndsWith(MagiConstraintsView.Cons2InputError($"{t + 1}", t)!, w[1]);
        Assert.Contains("2件", MagiConstraintsView.PeriodOverflowNote(st));
        Assert.Equal("", MagiConstraintsView.PeriodOverflowNote(b with { Cons1 = new List<C1Row>(), Cons2 = new List<C2Row>() }));
    }
}
