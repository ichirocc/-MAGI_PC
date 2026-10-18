namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3718Test</c> の 1 対 1 移植。3.718.0 問題パネル: パネルの状態（閉じる／セル／一覧）、高さの上限＝画面の 1/3、
/// 問題一覧の行（必須→要調整、行→セル）、「一覧へ」の語。
/// </summary>
public class Review3718Test
{
    [Fact]
    public void PanelModeTransitions()
    {
        // 一覧を開く → 行を押しても一覧のまま → セルを押すと編集（閉じない）→「一覧へ」で一覧 → 閉じる。
        var m = CellSheetLogic.NextPanelMode(CellSheetLogic.PanelMode.Closed, CellSheetLogic.PanelAction.OpenList); Assert.Equal(CellSheetLogic.PanelMode.List, m);
        m = CellSheetLogic.NextPanelMode(m, CellSheetLogic.PanelAction.RowTap); Assert.Equal(CellSheetLogic.PanelMode.List, m);
        m = CellSheetLogic.NextPanelMode(m, CellSheetLogic.PanelAction.CellTap); Assert.Equal(CellSheetLogic.PanelMode.Cell, m);
        m = CellSheetLogic.NextPanelMode(m, CellSheetLogic.PanelAction.ToList); Assert.Equal(CellSheetLogic.PanelMode.List, m);
        m = CellSheetLogic.NextPanelMode(m, CellSheetLogic.PanelAction.Close); Assert.Equal(CellSheetLogic.PanelMode.Closed, m);
        Assert.Equal(CellSheetLogic.PanelMode.Closed, CellSheetLogic.NextPanelMode(CellSheetLogic.PanelMode.Closed, CellSheetLogic.PanelAction.RowTap));
        Assert.Equal(CellSheetLogic.PanelMode.Cell, CellSheetLogic.NextPanelMode(CellSheetLogic.PanelMode.Closed, CellSheetLogic.PanelAction.CellTap));
        // 画面の状態から: セルを開いていれば編集が優先。
        Assert.Equal(CellSheetLogic.PanelMode.Cell, CellSheetLogic.PanelModeOf(listOpen: true, cellOpen: true));
        Assert.Equal(CellSheetLogic.PanelMode.List, CellSheetLogic.PanelModeOf(listOpen: true, cellOpen: false));
        Assert.Equal(CellSheetLogic.PanelMode.Closed, CellSheetLogic.PanelModeOf(listOpen: false, cellOpen: false));
    }

    [Fact]
    public void PanelHeightIsOneThirdOfScreen()
    {
        Assert.Equal(305, CellSheetLogic.PanelMaxHeight(915, 480));
        Assert.Equal(246, Math.Floor(CellSheetLogic.PanelMaxHeight(740, 480)));
        Assert.Equal(480, CellSheetLogic.PanelMaxHeight(0, 480));   // C# のみ: 測れていないときは従来の値
        Assert.Equal("一覧へ", CellSheetLogic.PanelListButton);
        Assert.Equal("要調整 3件", CellSheetLogic.SoftRowsHeading(3));
        Assert.Null(CellSheetLogic.SoftRowsHeading(0));
    }

    [Fact]
    public void ProblemRowsPutHardFirstThenSoftByDay()
    {
        var items = new[]
        {
            new TourItem("pref", 1, new[] { 3 }, "希望 A ・ 10/4", Syms: new[] { "B" }, DayMd: "10/4"),
            new TourItem("c3n", 0, new[] { 5, 6 }, "禁止の並び 夜→夜 ・ 10/6〜10/7", Syms: new[] { "夜", "夜" }, DayMd: "10/6"),
        };
        var names = new[] { "福澤", "金沢" };
        var hardRows = CellSheetLogic.HardViolationRows(items, names, new HashSet<string>());
        var fams = new Dictionary<string, IReadOnlyList<string>>
        {
            ["1,3"] = new[] { "vio-pref" }, ["0,1"] = new[] { "vio-c3", "vio-c3" }, ["0,0"] = new[] { "vio-c1" }, ["9,9"] = new[] { "vio-c1" }, ["0,5"] = new[] { "vio-c3n" },
        };
        var sched = new IReadOnlyList<int>[] { new[] { 0, 1, 1, 1, 1, 2, 2 }, new[] { 0, 0, 0, 1, 0, 0, 0 } };
        var rows = CellSheetLogic.ProblemRows(hardRows, items, fams, names, new[] { "A", "B", "夜" }, sched, "2026-10-01", f => f == "c3" ? "守るとよい並び" : f == "c1" ? "期間の制約" : f);
        // 必須は巡回の順（TourAt）、要調整は日→職員→族。必須の族（c3n）は要調整に重ねない。範囲外のキーは捨てる。
        Assert.Equal(new[] { true, true, false, false }, rows.Select(r => r.Hard));
        Assert.Equal(new int?[] { 0, 1 }, rows.Where(r => r.Hard).Select(r => r.TourAt));
        Assert.Equal((1, 3), rows[0].Cell);
        Assert.Equal((0, 5), rows[1].Cell);
        Assert.Equal(new[] { "c1", "c3" }, rows.Where(r => !r.Hard).Select(r => r.Family));
        Assert.Equal((0, 0), rows[2].Cell);
        Assert.Equal("福澤 10/2 守るとよい並び", rows[3].Line);   // 要調整の行は ProblemLine の型（名前 日 族の名）
        Assert.All(rows, r => Assert.True(r.Line.Length <= CellSheetLogic.ProblemLineMax, r.Line));
        Assert.Null(rows[2].TourAt);
    }
}
