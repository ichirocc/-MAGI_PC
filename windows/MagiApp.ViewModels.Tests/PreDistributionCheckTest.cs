using MagiApp.ViewModels;
using Xunit;

namespace MagiApp.ViewModels.Tests;

/// <summary>配布前確認（仕様 §10）: Kotlin <c>PreDistributionCheckTest</c> と 1 対 1。</summary>
public class PreDistributionCheckTest
{
    private static string L(string k) => k switch { "c1" => "期間の制約", "weekly" => "曜日の偏り", "covU" => "人員不足", "c3n" => "禁止の並び", _ => k };

    private static UiState Base()
    {
        var ui = new UiState { StartDate = "2026-10-01", Days = 31, HasResult = true, BestHard = 0 };
        ui.Breakdown = new Dictionary<string, int> { ["c1"] = 4, ["weekly"] = 20, ["covU"] = 0 };
        return ui;
    }

    [Fact]
    public void ZeroHardWithOpenConsultShowsBothStates()
    {
        var ui = Base();
        ui.Consults = new List<ConsultItem> { new("s", "n", StaffName: "a") };
        var rows = PreDistributionCheck.Rows(ui, null, L).ToDictionary(r => r.Label);
        Assert.True(rows["必須違反"].Ok);
        Assert.Equal("0件", rows["必須違反"].Value);
        Assert.False(rows["相談"].Ok);   // 必須 0 でも相談の未完了は別に示す
        Assert.False(rows["保存"].Ok);
        Assert.Equal("まだ勤務表 CSV に保存していません", rows["保存"].Value);
        Assert.Equal("期間の制約 4件・曜日の偏り 20pt", rows["要調整"].Value);   // 重み×数の順（作成の結果カードと同じ）
        Assert.Equal("対象期間 10/1〜10/31・現在採用している勤務表", rows["対象"].Value);
    }

    [Fact]
    public void HardRowListsFamiliesAndSaveRowReadsTheCsvLine()
    {
        var ui = Base();
        ui.BestHard = 3;
        ui.Breakdown = new Dictionary<string, int> { ["covU"] = 2, ["c3n"] = 1 };
        var rows = PreDistributionCheck.Rows(ui, "この内容で勤務表 CSV を保存済みです（10:15）。", L).ToDictionary(r => r.Label);
        Assert.Equal("1件・2人（禁止の並び 1件・人員不足 2人）", rows["必須違反"].Value);
        Assert.True(rows["保存"].Ok);
    }

    /// <summary>[3.678.0] 相談の未完了は回答待ちと保留だけ（了承・不可の回答があれば数えない）。</summary>
    [Fact]
    public void ConsultRowCountsOnlyPendingAndHeld()
    {
        var ui = Base();
        ui.Consults = new List<ConsultItem> { new("a", "n"), new("b", "n", Answer: ConsultAnswer.Agreed),
            new("c", "n", Held: true, Answer: ConsultAnswer.Agreed), new("d", "n", Answer: ConsultAnswer.Declined) };
        Assert.Equal("未完了 2件（うち保留 1件）（ホームの「相談中」）", PreDistributionCheck.Rows(ui, null, L).ToDictionary(r => r.Label)["相談"].Value);
        var agreed = Base();
        agreed.Consults = new List<ConsultItem> { new("b", "n", Answer: ConsultAnswer.Agreed) };
        Assert.True(PreDistributionCheck.Rows(agreed, null, L).ToDictionary(r => r.Label)["相談"].Ok);
    }

    /// <summary>[3.665.0] 月の境目は評価していないと明示する（前月・翌月の勤務はデータに無い）。注記は窓が月の外へはみ出しうる日を変えたときだけ。</summary>
    [Fact]
    public void MonthEdgeIsShownAsNotEvaluated()
    {
        Assert.Equal(PreDistributionCheck.MonthEdgeNote, PreDistributionCheck.Rows(Base(), null, L).ToDictionary(r => r.Label)["月の境目"].Value);
        Assert.True(PreDistributionCheck.MonthEdgeTouched(new[] { 1 }, 31, 3));
        Assert.True(PreDistributionCheck.MonthEdgeTouched(new[] { 29 }, 31, 3));
        Assert.False(PreDistributionCheck.MonthEdgeTouched(new[] { 2, 28 }, 31, 3));
        Assert.False(PreDistributionCheck.MonthEdgeTouched(new[] { 0 }, 31, 1));
    }
}
