using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ9] <c>MagiAccent</c>（<c>MagiTokens.kt</c> の <c>object MagiAccent</c> の値のみの逐語移植）の検証。
/// </summary>
public class MagiAccentTest
{
    [Fact]
    public void NamedColorsMatchTheKotlinArgbLiteralsWithAlphaStripped()
    {
        // Kotlin: Color(0xFF3B6FD4) 等 — 上位バイトは不透明を表すアルファ(FF)、残り6桁がRGB。
        Assert.Equal("#3B6FD4", MagiAccent.Blue);
        Assert.Equal("#2E9E62", MagiAccent.Green);
        Assert.Equal("#E08A1E", MagiAccent.Orange);
        Assert.Equal("#8A5CD1", MagiAccent.Purple);
        Assert.Equal("#D24D89", MagiAccent.Pink);
        Assert.Equal("#D23B34", MagiAccent.Red);
        Assert.Equal("#8A979B", MagiAccent.Gray);
    }

    [Fact]
    public void AllPreservesTheKotlinDeclarationOrder()
    {
        Assert.Equal(
            new[] { MagiAccent.Blue, MagiAccent.Green, MagiAccent.Orange, MagiAccent.Purple, MagiAccent.Pink, MagiAccent.Red, MagiAccent.Gray },
            MagiAccent.All);
    }
}
