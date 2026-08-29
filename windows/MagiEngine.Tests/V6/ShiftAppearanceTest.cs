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
