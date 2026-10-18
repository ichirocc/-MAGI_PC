namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3719Test</c> の 1 対 1 移植。3.719.0 視覚弱点者対応 第1便: 文字拡大で 1 行の文を 2 行にする閾値、ホームの主ボタンの字数は
/// 倍率で縮む、要調整のアンバーは白地と #E5E5E5 で 3:1 以上、見分けにくいセル地の輪郭の判定。勤務表の dp 寸法は Android 固有なので移さない。
/// </summary>
public class Review3719Test
{
    [Fact]
    public void MaxLinesGrowsAtLargeFontScale()
    {
        Assert.Equal(1, A11y.MaxLines(1.0));
        Assert.Equal(1, A11y.MaxLines(1.15));
        Assert.Equal(2, A11y.MaxLines(1.3));
        Assert.Equal(2, A11y.MaxLines(2.0));
        Assert.Equal(3, A11y.MaxLines(1.3, baseLines: 2));
    }

    [Fact]
    public void HomeTargetLabelShrinksWithFontScale()
    {
        // 18 字 ÷ 倍率。1.3× は 13 字まで＝「アリ 10/8の希望を見る」（13 字）は入り、「アリフ 10/8の希望を見る」（14 字）は元の文言に戻る。
        Assert.Equal("アリフ 10/8の希望を見る", AnalysisTriage.HomeTargetLabel(AnalysisTriage.HomeWishButton, "アリフ", "10/8"));
        Assert.Equal("アリ 10/8の希望を見る", AnalysisTriage.HomeTargetLabel(AnalysisTriage.HomeWishButton, "アリ", "10/8", 1.3));
        Assert.Equal(AnalysisTriage.HomeWishButton, AnalysisTriage.HomeTargetLabel(AnalysisTriage.HomeWishButton, "アリフ", "10/8", 1.3));
        Assert.Equal("10/8のなおし方を見る", AnalysisTriage.HomeTargetLabel("なおし方を見る", null, "10/8", 1.0));
        Assert.Equal("なおし方を見る", AnalysisTriage.HomeTargetLabel("なおし方を見る", null, "10/8", 2.0));   // 9 字まで
    }

    [Fact]
    public void AmberMeetsUiContrastOnWhiteAndLightGray()
    {
        Assert.Equal("#B86E00", MagiEngine.V6.MagiAccent.Orange);
        Assert.True(A11y.ContrastRatio(MagiEngine.V6.MagiAccent.Orange, "#FFFFFF") >= 3.0);
        Assert.True(A11y.ContrastRatio(MagiEngine.V6.MagiAccent.Orange, "#E5E5E5") >= 3.0);
        Assert.Equal(3.99, A11y.ContrastRatio(MagiEngine.V6.MagiAccent.Orange, "#FFFFFF"), 0.02);
        Assert.Equal(3.16, A11y.ContrastRatio(MagiEngine.V6.MagiAccent.Orange, "#E5E5E5"), 0.02);
        // 旧値は足りなかった（記録のため）。
        Assert.True(A11y.ContrastRatio("#E08A1E", "#FFFFFF") < 3.0);
        Assert.True(A11y.ContrastRatio("#F59E0B", "#FFFFFF") < 3.0);
        Assert.Equal(21.0, A11y.ContrastRatio("#000000", "#FFFFFF"), 0.01);
    }

    [Fact]
    public void OutlineOnlyWhenCellBlendsIntoSurface()
    {
        var surface = A11y.WcagLuminance("#FBFDFC");
        Assert.True(A11y.LowContrastOutline(A11y.WcagLuminance("#FFFFFF"), surface));
        Assert.True(A11y.LowContrastOutline(A11y.WcagLuminance("#E5E5E5"), surface));   // 1.25:1
        Assert.True(A11y.LowContrastOutline(A11y.WcagLuminance("#FFEECA"), surface));
        Assert.False(A11y.LowContrastOutline(A11y.WcagLuminance("#B86E00"), surface));
        Assert.False(A11y.LowContrastOutline(A11y.WcagLuminance("#8A979B"), surface));
        Assert.False(A11y.LowContrastOutline(A11y.WcagLuminance("#C68B0C"), surface));
    }
}
