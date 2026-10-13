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
        Assert.Equal("曜日の偏り 20件・期間の制約 4件", rows["要調整"].Value);
        Assert.Equal("10/1〜10/31 の採用した勤務表", rows["対象"].Value);
    }

    [Fact]
    public void HardRowListsFamiliesAndSaveRowReadsTheCsvLine()
    {
        var ui = Base();
        ui.BestHard = 3;
        ui.Breakdown = new Dictionary<string, int> { ["covU"] = 2, ["c3n"] = 1 };
        var rows = PreDistributionCheck.Rows(ui, "この内容で勤務表 CSV を保存済みです（10:15）。", L).ToDictionary(r => r.Label);
        Assert.Equal("3件（禁止の並び 1件・人員不足 2件）", rows["必須違反"].Value);
        Assert.True(rows["保存"].Ok);
    }
}
