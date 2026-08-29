using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ9] <c>ShiftAppearanceTest.kt</c> の逐語移植。3関数（重大度分類・色解決・文字色選択）を検証する。
/// </summary>
public class ShiftAppearanceTest
{
    [Fact]
    public void SeverityFollowsTheWeightHierarchy()
    {
        // HARD 4族は CRITICAL、重いソフト(low90/high45/c3mn15)は HIGH、整え(fair/weekly)は INFO。
        foreach (var k in new[] { "groupViol", "covU", "pref", "c3n" })
            Assert.Equal("CRITICAL", ShiftAppearance.SeverityFromVioKey(k));
        foreach (var k in new[] { "low", "high", "c3mn" })
            Assert.Equal("HIGH", ShiftAppearance.SeverityFromVioKey(k));
        foreach (var k in new[] { "fair", "weekly" })
            Assert.Equal("INFO", ShiftAppearance.SeverityFromVioKey(k));
        Assert.Equal("WARN", ShiftAppearance.SeverityFromVioKey("c1"));
        // 表示側は "vio-" 接頭辞つきのクラス名で引く。
        Assert.Equal("CRITICAL", ShiftAppearance.SeverityFromVioKey("vio-covU"));
        // 未知キーは INFO へ倒す（新族を足しても画面が落ちない）。
        Assert.Equal("INFO", ShiftAppearance.SeverityFromVioKey("no-such-family"));
    }

    /// <summary>
    /// [3.417.0] 色は「利用者の明示色 → 一覧上の位置」だけで決まり、記号・名称からは何も推測しない。
    /// 記号を引数に取らない形にしたので、この不変条件は**シグネチャで構造的に保証**される
    /// （文字列を渡す余地が無い＝将来また字面で分岐する実装へ戻れない）。
    /// </summary>
    [Fact]
    public void ColorResolutionUsesOnlyExplicitColorOrPosition()
    {
        Assert.Equal("#123456", ShiftAppearance.ResolveShiftColor(explicitHex: "#123456", index: 3));
        // 隣接する index は異なる色（同じ色に潰れないことがパレットの目的）。
        Assert.NotEqual(ShiftAppearance.ResolveShiftColor(index: 0), ShiftAppearance.ResolveShiftColor(index: 1));
        // 位置が不明なときはどのシフトでも同じ中立色＝記号による優劣を持たない。
        Assert.Equal(ShiftAppearance.NeutralShiftColor, ShiftAppearance.ResolveShiftColor());
    }

    /// <summary>
    /// [フェーズ9] <c>ensureReadable</c>（<c>MagiTokens.kt</c>）の逐語移植の検証。
    /// 数値は WCAG 相対輝度式で独立に検算済み（bg=白 vs 黒: contrast=21.0 / bg=白 vs #F0F0F0:
    /// contrast≈1.14 / bg=黒 vs #101010: contrast≈1.10、いずれも4.5未満）。
    /// </summary>
    [Fact]
    public void PreferredColorIsKeptWhenItAlreadyMeetsTheRatio()
    {
        // 白地に黒＝コントラスト比21.0、既定しきい値4.5を大きく超える＝そのまま採用。
        Assert.Equal("#000000", ShiftAppearance.EnsureReadable("#FFFFFF", "#000000"));
    }

    [Fact]
    public void FallsBackToPureBlackWhenPreferredIsTooCloseToAWhiteBackground()
    {
        // 白地に薄灰(#F0F0F0)＝コントラスト比≈1.14と既定しきい値4.5未満＝黒/白のうち高い方(黒)へ。
        Assert.Equal("#000000", ShiftAppearance.EnsureReadable("#FFFFFF", "#F0F0F0"));
    }

    [Fact]
    public void FallsBackToPureWhiteWhenPreferredIsTooCloseToABlackBackground()
    {
        // 黒地に濃灰(#101010)＝コントラスト比≈1.10と既定しきい値4.5未満＝黒/白のうち高い方(白)へ。
        Assert.Equal("#FFFFFF", ShiftAppearance.EnsureReadable("#000000", "#101010"));
    }

    [Fact]
    public void ALowMinRatioLetsTheOriginallyRejectedPreferredColorThrough()
    {
        // 同じ薄灰(#F0F0F0)でも minRatio=1.0 なら 1.14 >= 1.0 で通る＝指定色をそのまま尊重する。
        Assert.Equal("#F0F0F0", ShiftAppearance.EnsureReadable("#FFFFFF", "#F0F0F0", minRatio: 1.0));
    }

    [Fact]
    public void UnparseableBackgroundReturnsThePreferredColorUnchanged()
    {
        // 背景が解釈できなければコントラストを判定できない＝指定色をそのまま返す
        // （Kotlin原本はColor型を受けるためこの経路は無いが、文字列を受けるC#移植側の保険）。
        Assert.Equal("#123456", ShiftAppearance.EnsureReadable("こわれた値", "#123456"));
    }

    [Fact]
    public void UnparseablePreferredColorFallsBackJustLikeInsufficientContrast()
    {
        // 指定色が解釈できない＝コントラスト要件を満たせないのと同様に扱い、黒/白の高い方へ。
        Assert.Equal("#000000", ShiftAppearance.EnsureReadable("#FFFFFF", "こわれた値"));
    }

    [Fact]
    public void TextColorIsTheHigherContrastOfTheTwoInkColors()
    {
        Assert.Equal("#14110d", ShiftAppearance.PickTextColor("#ffffff"));  // 明るい地には黒
        Assert.Equal("#fbf4e8", ShiftAppearance.PickTextColor("#000000"));  // 暗い地には生成り
        Assert.Equal("#14110d", ShiftAppearance.PickTextColor("こわれた値")); // 解釈できなければ黒へ倒す
        // パレットは中間色ぞろいなので、どの色にも「黒か生成りのどちらか」が返る（未定義の色を返さない）。
        for (int i = 0; i < 16; i++)
        {
            var ink = ShiftAppearance.PickTextColor(ShiftAppearance.ResolveShiftColor(index: i));
            Assert.True(ink is "#14110d" or "#fbf4e8", ink);
        }
    }
}
