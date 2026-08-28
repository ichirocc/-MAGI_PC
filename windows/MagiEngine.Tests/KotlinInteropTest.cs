namespace MagiEngine.Tests;

public class KotlinInteropTest
{
    [Theory]
    [InlineData("123", 123)]
    [InlineData("-123", -123)]
    [InlineData("+123", 123)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void ToIntOrNull_ParsesValidAsciiIntegers(string input, int expected)
    {
        Assert.Equal(expected, KotlinInterop.ToIntOrNull(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 123")]        // leading whitespace: Kotlin's toIntOrNull trims nothing
    [InlineData("123 ")]        // trailing whitespace
    [InlineData(" 123 ")]
    [InlineData("12.3")]        // decimal point is not a digit
    [InlineData("1,230")]       // grouping separator
    [InlineData("abc")]
    [InlineData("-")]           // sign only, no digits
    [InlineData("+")]
    [InlineData("--5")]         // double sign
    [InlineData("5-")]          // sign not at start
    [InlineData("2147483648")]  // int.MaxValue + 1: overflow
    [InlineData("-2147483649")] // int.MinValue - 1: overflow
    [InlineData("99999999999999999999")] // wildly out of range
    public void ToIntOrNull_RejectsInvalidInput(string input)
    {
        Assert.Null(KotlinInterop.ToIntOrNull(input));
    }

    [Fact]
    public void ToIntOrNull_RejectsNull()
    {
        Assert.Null(KotlinInterop.ToIntOrNull(null));
    }

    // Kotlin's toIntOrNull resolves each character via the JVM's Character.digit(c, 10), which
    // (unlike a naive ASCII-only parser) recognizes any Unicode decimal-digit character — notably
    // full-width digits (U+FF10-FF19), which a Japanese-language input field can produce. This is
    // a real, previously-observed behavior for this app (see KotlinInterop's own doc comment).
    [Theory]
    [InlineData("０", 0)]   // full-width "０"
    [InlineData("２", 2)]   // full-width "２"
    [InlineData("１２３", 123)] // full-width "１２３"
    [InlineData("1２2３3", 12233)] // mixed ASCII/full-width interleaved digits: "1２2３3" -> digits 1,2,2,3,3
    public void ToIntOrNull_AcceptsFullWidthUnicodeDigits(string input, int expected)
    {
        Assert.Equal(expected, KotlinInterop.ToIntOrNull(input));
    }

    [Fact]
    public void ToIntOrNull_AcceptsAsciiSignWithFullWidthDigits()
    {
        // The sign character itself must be ASCII '-'/'+' (checked by literal char comparison in
        // both the Kotlin source and this port); only the digits after it may be full-width.
        Assert.Equal(-2, KotlinInterop.ToIntOrNull("-２"));
    }

    // [フェーズ7ピース1] ToDoubleOrNull. Every case below was cross-checked against a real Kotlin
    // runtime (kotlin-compiler-embeddable) before being trusted — the phase-7 master plan's claim
    // that this method needed ToIntOrNull-style full-width-Unicode-digit support was checked this
    // way and found to be FALSE ("３".toDoubleOrNull() -> null in real Kotlin, unlike ToIntOrNull).

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("-1.5", -1.5)]
    [InlineData("+1.5", 1.5)]
    [InlineData("0", 0.0)]
    [InlineData("1", 1.0)]
    [InlineData("1.", 1.0)]     // trailing decimal point, no digits after: valid partial form
    [InlineData(".5", 0.5)]     // leading decimal point, no digits before: valid partial form
    [InlineData("1.5e10", 1.5e10)]
    [InlineData("1.5E-10", 1.5e-10)]
    public void ToDoubleOrNull_ParsesValidNumerals(string input, double expected)
    {
        Assert.Equal(expected, KotlinInterop.ToDoubleOrNull(input));
    }

    [Fact]
    public void ToDoubleOrNull_OverflowingLiteralsProduceInfinityOrZero()
    {
        // Standard IEEE754 conversion behavior for decimal literals whose magnitude exceeds
        // double's range — confirmed real Kotlin agrees exactly, including with the 'd' suffix
        // (see ToDoubleOrNull_AcceptsJavaTypeSuffixes: "1e400d" is a genuine overflowing numeral
        // WITH a valid suffix, and must be distinguished from a suffixed keyword like "Infinityd",
        // which must stay null — see ToDoubleOrNull_RejectsSuffixedKeywords).
        Assert.Equal(double.PositiveInfinity, KotlinInterop.ToDoubleOrNull("1e400"));
        Assert.Equal(0.0, KotlinInterop.ToDoubleOrNull("1e-400"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("1,5")]  // grouping separator — Kotlin's toDoubleOrNull does not accept it either
    [InlineData(".")]    // bare decimal point, no digits at all: invalid
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("d")]
    [InlineData("D")]
    [InlineData("e10")]
    [InlineData("1e")]
    [InlineData(".e5")]
    [InlineData("+.")]
    [InlineData("-.")]
    public void ToDoubleOrNull_RejectsInvalidInput(string? input)
    {
        Assert.Null(KotlinInterop.ToDoubleOrNull(input));
    }

    [Fact]
    public void ToDoubleOrNull_TrimsSurroundingWhitespace()
    {
        // Unlike the sibling ToIntOrNull (which trims nothing — see
        // ToIntOrNull_RejectsInvalidInput's " 123"/"123 " cases), Kotlin's toDoubleOrNull DOES
        // accept leading/trailing whitespace (confirmed empirically: "  1.5  ".toDoubleOrNull()
        // -> 1.5 in real Kotlin) — this genuine asymmetry between the two sibling functions is
        // preserved faithfully, not "fixed" into consistency, per HF77.
        Assert.Equal(1.5, KotlinInterop.ToDoubleOrNull("  1.5  "));
    }

    // Dimension 1 (see KotlinInterop.ToDoubleOrNull's KDoc): Java float/double literal type
    // suffixes (d/D/f/F) appended to a genuine numeral. .NET's double.TryParse rejects any
    // trailing letter outright, so this exercises the strip-and-retry fallback path.
    [Theory]
    [InlineData("1.5d", 1.5)]
    [InlineData("1.5D", 1.5)]
    [InlineData("1.5f", 1.5)]
    [InlineData("1.5F", 1.5)]
    [InlineData("5d", 5.0)]
    [InlineData("5D", 5.0)]
    public void ToDoubleOrNull_AcceptsJavaTypeSuffixes(string input, double expected)
    {
        Assert.Equal(expected, KotlinInterop.ToDoubleOrNull(input));
    }

    [Fact]
    public void ToDoubleOrNull_SuffixedOverflowStillProducesInfinity()
    {
        // "1e400d": a genuine overflowing numeral WITH a suffix — must succeed as Infinity, not be
        // confused with the suffixed-keyword case ("Infinityd", which must reject — see
        // ToDoubleOrNull_RejectsSuffixedKeywords). Confirmed against real Kotlin.
        Assert.Equal(double.PositiveInfinity, KotlinInterop.ToDoubleOrNull("1e400d"));
        Assert.Equal(0.0, KotlinInterop.ToDoubleOrNull("1e-400f"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("+NaN")]
    [InlineData("-NaN")]
    public void ToDoubleOrNull_AcceptsExactCaseNaNKeyword(string input)
    {
        Assert.True(double.IsNaN(KotlinInterop.ToDoubleOrNull(input)!.Value));
    }

    [Fact]
    public void ToDoubleOrNull_AcceptsExactCaseInfinityKeywords()
    {
        Assert.Equal(double.PositiveInfinity, KotlinInterop.ToDoubleOrNull("Infinity"));
        Assert.Equal(double.PositiveInfinity, KotlinInterop.ToDoubleOrNull("+Infinity"));
        Assert.Equal(double.NegativeInfinity, KotlinInterop.ToDoubleOrNull("-Infinity"));
    }

    // Dimension 2 (see KotlinInterop.ToDoubleOrNull's KDoc): the case-sensitivity divergence this
    // session's investigation was centered on. .NET's double.TryParse recognizes the NaN/Infinity
    // symbols CASE-INSENSITIVELY (confirmed: "nan"/"NAN"/"infinity"/"INFINITY"/"-infinity" all
    // parse successfully via a naive double.TryParse call) — but real Kotlin's toDoubleOrNull is
    // case-sensitive, exact-spelling-only, and rejects every one of these. A naive delegation to
    // double.TryParse would silently accept input real Kotlin rejects; this must not regress.
    [Theory]
    [InlineData("nan")]
    [InlineData("NAN")]
    [InlineData("nAn")]
    [InlineData("Nan")]
    [InlineData("infinity")]
    [InlineData("INFINITY")]
    [InlineData("InFiNiTy")]
    [InlineData("-infinity")]
    [InlineData("+infinity")]
    public void ToDoubleOrNull_RejectsWrongCaseNaNInfinitySpellings(string input)
    {
        Assert.Null(KotlinInterop.ToDoubleOrNull(input));
    }

    // Dimension 3 (see KotlinInterop.ToDoubleOrNull's KDoc): the d/D/f/F suffix is valid ONLY on a
    // genuine FloatingPointLiteral, never on the NaN/Infinity keyword productions themselves — even
    // though "NaN"/"Infinity" alone succeed (see ToDoubleOrNull_AcceptsExactCase*), appending a
    // suffix to them must still reject. Confirmed against real Kotlin for all four sign variants.
    [Theory]
    [InlineData("NaNd")]
    [InlineData("NaNf")]
    [InlineData("Infinityd")]
    [InlineData("+Infinityd")]
    [InlineData("-Infinityd")]
    [InlineData("nand")]        // wrong-case keyword + suffix: rejected on both counts
    [InlineData("infinityd")]
    public void ToDoubleOrNull_RejectsSuffixedKeywords(string input)
    {
        Assert.Null(KotlinInterop.ToDoubleOrNull(input));
    }

    // [Accepted, documented gap] Java/Kotlin's hex-float literal grammar (e.g. "0x1.8p3" -> 12.0)
    // is deliberately NOT replicated — both real call sites in V6PortAnalyzer.kt parse simple
    // decimal apt-count overrides that will never contain hex floats, and .NET has no built-in
    // hex-float parsing to delegate to. This asserts the accepted divergence explicitly rather than
    // leaving it undocumented.
    [Theory]
    [InlineData("0x1.0p0")]
    [InlineData("0X1.8P3")]
    public void ToDoubleOrNull_RejectsHexFloats_DocumentedGapFromRealKotlin(string input)
    {
        Assert.Null(KotlinInterop.ToDoubleOrNull(input));
    }
}
