using System.Text.RegularExpressions;

namespace MagiApp.ViewModels.Tests;

/// <summary>[phase9 #21] 36 色パレットが原本（3.460.0）と同じ形であることを固定する。</summary>
public class ShiftColorPaletteTest
{
    [Fact]
    public void PaletteHasThirtySixUniqueLowercaseHexColorsWithTheOriginalAnchorFirst()
    {
        Assert.Equal(36, ShiftColorPalette.All.Count);
        Assert.Equal(36, ShiftColorPalette.All.Distinct().Count());
        Assert.All(ShiftColorPalette.All, hex => Assert.Matches(new Regex("^#[0-9a-f]{6}$"), hex));
        Assert.Equal("#e08a1e", ShiftColorPalette.All[0]);
        Assert.Equal(6, ShiftColorPalette.PerRow);
    }

    [Fact]
    public void PickFgUsesDarkTextOnLightAndLightTextOnDarkAndToleratesBadHex()
    {
        Assert.Equal("#14110d", ShiftColorPalette.PickFg("#e5e5e5"));
        Assert.Equal("#fbf4e8", ShiftColorPalette.PickFg("#023047"));
        Assert.Equal("#fbf4e8", ShiftColorPalette.PickFg("garbage")); // 不正値は 0x888888（明度 136 ≤ 140）扱い
    }
}
