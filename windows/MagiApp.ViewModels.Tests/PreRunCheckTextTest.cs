using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[つくる前の確認] 実データ（2026-10、氏名は伏せ字）でシートの行。Kotlin <c>PreRunCheckTextTest</c> の 1 対 1 移植。</summary>
public class PreRunCheckTextTest
{
    private static readonly MagiState St = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));

    [Fact]
    public void RealData_SheetRows()
    {
        var ui = new UiState { StaffNames = St.StaffList.Select(s => s.Name).ToList(), ShiftSymbols = St.Shifts.Select(s => s.Kigou).ToList(), StartDate = St.StartDate };
        var t = PreRunCheckText.Of(PreRunCheck.Build(St, St.Schedule.ToIntArray2D()), ui);
        Assert.Equal("何度つくっても残る（9件）", t.FloorHeader);
        Assert.Equal(new[]
        {
            "職員01 10/25・10/26・10/27 本人の希望「休→休→休」が禁止の並びに当たっています",
            "職員03 10/1「Dﾃ」→ 10/2「休」 本人の希望どうしが希望の前日の禁止に当たっています",
            "職員05 10/23・10/24・10/25 本人の希望「休→休→休」が禁止の並びに当たっています",
            "職員08 10/10・10/11・10/12 本人の希望「休→休→休」が禁止の並びに当たっています",
            "10/9(金) 必要人数と本人の希望の衝突（8件は同時に成立しません）（入れないシフトの指定が関係）",
            "10/10(土) 必要人数と本人の希望の衝突（9件は同時に成立しません）（入れないシフトの指定が関係）",
            "10/11(日) 必要人数と本人の希望の衝突（9件は同時に成立しません）（入れないシフトの指定が関係）",
            "10/29(木) 必要人数と本人の希望の衝突（7件は同時に成立しません）（入れないシフトの指定が関係）",
            "職員04 本人の希望と条件の組合せ（4件は同時に成立しません）",
        }, t.FloorRows.Select(r => r.Text));
        Assert.True(t.HasWishRows);
        Assert.Equal(PreRunCheckText.ZeroCapNoteText, t.ZeroCapNote);
        Assert.Equal("再作成すると外れる（4件）", t.RerunHeader);
        Assert.Equal(new[] { "職員08 10/9 Cｱ", "職員04 10/10 Aｱ", "職員04 10/11 Cｵ", "職員08 10/29 Cｱ" }, t.RerunRows.Select(r => r.Text));
        Assert.Equal(new (int?, int?)[] { (7, 8), (3, 9), (3, 10), (7, 28) }, t.RerunRows.Select(r => (r.Staff, r.Day)));
        Assert.Equal("職員08「有」など：上限0のシフトに希望が載っています。上限0は意図した制限です。残るのは要調整です。希望を変えるか、入れない指定を設定で見直してください。", t.OverCapNote);
        Assert.Equal(new[] { "職員08「有」 本人の希望1件（個人の上限0回）", "職員11「Cｵ」 本人の希望12件（個人の上限0回）" }, t.OverCapRows!.Select(r => r.Text));
        Assert.Equal("個人の上限0：22組（8人）。入れないシフトの指定です。", t.WallLine);
    }

    [Fact]
    public void ZeroCapRows_TaggedAndPointToRelax()
    {
        var ui = new UiState { StaffNames = St.StaffList.Select(s => s.Name).ToList(), ShiftSymbols = St.Shifts.Select(s => s.Kigou).ToList(), StartDate = St.StartDate };
        var b = PreRunCheck.Build(St, St.Schedule.ToIntArray2D());
        var t = PreRunCheckText.Of(b with { ForcedShortfalls = new[] { new ForcedCovU(0, "X", 2, 3) }, ZeroCapShortShifts = new HashSet<int> { 0 },
            ZeroCapProofDays = new HashSet<int> { b.DayProofs[0].Day } }, ui);
        Assert.EndsWith(PreRunCheckText.ZeroCapTag, t.FloorRows.First(r => r.Text.StartsWith("「X」")).Text);
        Assert.EndsWith(PreRunCheckText.ZeroCapTag, t.FloorRows.First(r => r.Text.StartsWith("10/9(金)")).Text);
        Assert.DoesNotContain(t.FloorRows, r => r.Text.StartsWith("10/10(土)") && r.Text.EndsWith(PreRunCheckText.ZeroCapTag));
        Assert.Equal(PreRunCheckText.ZeroCapNoteText, t.ZeroCapNote);
    }

    /// <summary>[3.644.0/UX-02] セルを持たない行は原因の入力箇所へ着地する（Kotlin <c>rowsWithoutACellLandOnTheirInput</c>）。</summary>
    [Fact]
    public void RowsWithoutACellLandOnTheirInput()
    {
        var ui = new UiState { StaffNames = St.StaffList.Select(s => s.Name).ToList(), ShiftSymbols = St.Shifts.Select(s => s.Kigou).ToList(), StartDate = St.StartDate };
        var b = PreRunCheck.Build(St, St.Schedule.ToIntArray2D());
        var t = PreRunCheckText.Of(b with { ForcedShortfalls = new[] { new ForcedCovU(0, "X", 2, 3), new ForcedCovU(1, "Y", 1, 1) }, ZeroCapShortShifts = new HashSet<int> { 0 } }, ui);
        Assert.Equal(new EditLanding(2, "yr_count", Label: GuidedFixRules.LandingZeroCap), t.FloorRows.First(r => r.Text.StartsWith("「X」")).Landing);
        Assert.Equal(new EditLanding(2, "yr_ws1"), t.FloorRows.First(r => r.Text.StartsWith("「Y」")).Landing);
        var yuu = St.Shifts.ToList().FindIndex(s => s.Kigou == "有");
        Assert.Equal(new EditLanding(2, "yr_count", CountStaff: 7, CountShift: yuu, Label: GuidedFixRules.LandingZeroCap), t.OverCapRows![0].Landing);
        Assert.Equal(new EditLanding(2, "yr_count", Label: GuidedFixRules.LandingZeroCap), t.WallLanding);
        Assert.All(t.FloorRows.Where(r => r.Staff is not null), r => Assert.Null(r.Landing));
    }
}
