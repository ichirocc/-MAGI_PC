namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>IosPatternsTest</c>（3.727.1）の 1 対 1 移植。iOS のリマインダー（配布前の確認の並び）・天気（日ごとの印）・時計（作成の輪）・ハイライトの純粋な部分。</summary>
public class IosPatternsTest
{
    [Fact]
    public void ProgressRingUsesTheSameFrameAsTheRemainingTime()
    {
        // 予算 60 秒＋後処理 10＋玉突き 10＝80 秒の枠。
        Assert.Equal(0f, CellSheetLogic.RunProgressFraction(60, 0L), 6);
        Assert.Equal(0.5f, CellSheetLogic.RunProgressFraction(60, 40_000L), 6);
        Assert.Equal(1f, CellSheetLogic.RunProgressFraction(60, 999_000L), 6);
    }

    [Fact]
    public void DayMarksShowShortageAndHardCellsPerDay()
    {
        var ui = new UiState
        {
            Days = 3, StartDate = "2026-10-01",
            NeedViolations = new Dictionary<string, string> { ["2,1"] = "vio-covU", ["3,2"] = "vio-covO" },
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>> { ["0,2"] = new[] { "vio-c3w" }, ["4,2"] = new[] { "vio-c1" } },
        };
        var m = CellSheetLogic.DayStripMarks(ui);
        Assert.Equal(new[] { false, true, false }, m.Select(x => x.Short));
        Assert.Equal(new[] { 0, 0, 1 }, m.Select(x => x.HardCells));   // c1（要調整）は数えない・人員過剰は不足ではない
        Assert.Equal("1", m[0].Dom);
        Assert.Equal("木", m[0].Dow);
        Assert.Equal($"問題なし 1日 ・ 見ておく日 2日（押すとその日へ）\n{CellSheetLogic.DayStripLegend}", CellSheetLogic.DayStripSummary(m));
        Assert.Equal("2日 金曜 人員不足", CellSheetLogic.DayMarkSpoken(m[1]));
        Assert.Equal("3日すべて 問題なし", CellSheetLogic.DayStripSummary(CellSheetLogic.DayStripMarks(new UiState { Days = 3, StartDate = "2026-10-01" })));
    }

    [Fact]
    public void PreDistributionPutsOpenItemsFirst()
    {
        var rows = new[] { new PreDistRow("評価", "済", true), new PreDistRow("必須", "2件", false), new PreDistRow("相談", "未", null), new PreDistRow("保存", "済", true) };
        Assert.Equal(new[] { "必須", "相談", "評価", "保存" }, CellSheetLogic.PreDistributionOrder(rows).Select(r => r.Label));
    }

    [Fact]
    public void HighlightSaysBeforeAndAfterByUnit()
    {
        Assert.Equal("必須違反 1件・3人 → 1件 ・ 要調整 30件 → 27件",
            AnalysisTriage.RunDeltaText(new Dictionary<string, int> { ["c3w"] = 1, ["covU"] = 3, ["c1"] = 30 }, new Dictionary<string, int> { ["c3w"] = 1, ["c1"] = 27 }));
        Assert.Null(AnalysisTriage.RunDeltaText(new Dictionary<string, int> { ["c1"] = 3 }, new Dictionary<string, int> { ["c1"] = 3 }));
    }
}
