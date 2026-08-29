using System.Text;
using MagiEngine.Model;

namespace MagiEngine.Tests.Model;

/// <summary>
/// [フェーズ9 ピース7] <see cref="MojibakeRepair"/>（model/MojibakeRepair.kt の逐語移植）の検証。
/// Kotlin原本には専用テストが無いため、C#移植で新規に固定する（<c>UiStateTest</c> 等と同じ経緯）。
///
/// 特殊文字（BOM/置換文字/Latin-1境界）はソースへ直接埋め込まず、数値キャスト
/// （<c>(char)0xFEFF</c> 等、本体の <see cref="MojibakeRepair"/> と同じ規約）でのみ表現する
/// （このピースの作業中に実際に踏んだエンコード事故の再発防止）。
/// </summary>
public class MojibakeRepairTest
{
    private const char Bom = (char)0xFEFF;

    [Fact]
    public void EmptyStringPassesThroughUnchanged()
    {
        Assert.Equal("", MojibakeRepair.Repair(""));
        Assert.False(MojibakeRepair.LooksMojibake(""));
    }

    [Fact]
    public void AsciiOnlyTextPassesThroughUnchanged()
    {
        const string s = "{\"startDate\":\"2026-01-01\",\"days\":31}";
        Assert.Equal(s, MojibakeRepair.Repair(s));
        Assert.False(MojibakeRepair.LooksMojibake(s));
    }

    [Fact]
    public void GenuineJapaneseTextPassesThroughUnchanged()
    {
        // Already-correct multibyte text (code points > U+00FF) must never be touched —
        // this is the primary safety net against corrupting real (non-mojibake) data.
        const string s = "職員A・休み・グループ1";
        Assert.Equal(s, MojibakeRepair.Repair(s));
        Assert.False(MojibakeRepair.LooksMojibake(s));
    }

    [Fact]
    public void LeadingBomIsAlwaysStripped()
    {
        const string body = "plain ascii body";
        var withBom = Bom + body;

        Assert.Equal(body, MojibakeRepair.Repair(withBom));
        // LooksMojibake compares Repair()'s output against the ORIGINAL (BOM-included) string,
        // so a BOM-only difference still reports as "looks mojibake" (it changed something) —
        // WasDecoded is the one that distinguishes "just a BOM" from "a real double-encoding".
        Assert.True(MojibakeRepair.LooksMojibake(withBom));
        Assert.False(MojibakeRepair.WasDecoded(withBom, MojibakeRepair.Repair(withBom)));
    }

    [Fact]
    public void ActualDoubleEncodingIsRepaired()
    {
        // Simulate the real-world corruption: a UTF-8-encoded Japanese string that was
        // mis-decoded once as Latin-1 (each UTF-8 byte becomes one U+0080..U+00FF code point),
        // then re-encoded as UTF-8. Repair() must invert exactly that transformation.
        const string original = "職員"; // 2 genuine Kanji, each 3 UTF-8 bytes
        var utf8Bytes = Encoding.UTF8.GetBytes(original);
        var mojibake = Encoding.Latin1.GetString(utf8Bytes); // the corrupted string as it would arrive

        Assert.NotEqual(original, mojibake); // sanity: the corruption actually changed the text
        Assert.True(MojibakeRepair.LooksMojibake(mojibake));
        Assert.Equal(original, MojibakeRepair.Repair(mojibake));
        Assert.True(MojibakeRepair.WasDecoded(mojibake, MojibakeRepair.Repair(mojibake)));
    }

    [Fact]
    public void GenuineLatin1TextThatIsNotDoubleEncodedIsProtected()
    {
        // A real Latin-1 string (accented Western European text, e.g. from a CP1252-ish source)
        // that is NOT a double-encoded UTF-8 sequence must be left alone — reinterpreting its
        // bytes as UTF-8 would either throw/replace (caught by the U+FFFD guard) or, worse,
        // silently produce different-but-plausible-looking garbage. "héllo wörld" round-trips
        // through Latin1-bytes -> UTF-8-decode as invalid UTF-8 (Latin-1 high bytes 0xE9/0xF6
        // are not valid UTF-8 lead/continuation bytes here), so decoding must yield replacement
        // characters and Repair() must refuse to touch it.
        const string s = "héllo wörld";
        Assert.Equal(s, MojibakeRepair.Repair(s));
        Assert.False(MojibakeRepair.LooksMojibake(s));
    }

    [Fact]
    public void WasDecodedIsFalseWhenRepairIsANoOpBeyondBomStripping()
    {
        const string s = "no special characters here";
        Assert.False(MojibakeRepair.WasDecoded(s, MojibakeRepair.Repair(s)));
    }

    [Fact]
    public void WasDecodedIsTrueOnlyWhenTheBodyActuallyChanged()
    {
        const string original = "職員";
        var mojibake = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(original));
        var repaired = MojibakeRepair.Repair(mojibake);

        Assert.True(MojibakeRepair.WasDecoded(mojibake, repaired));
    }
}
