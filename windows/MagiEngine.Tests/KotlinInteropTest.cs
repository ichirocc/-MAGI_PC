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
}
