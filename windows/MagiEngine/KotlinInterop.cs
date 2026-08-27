using System.Globalization;

namespace MagiEngine;

/// <summary>
/// Mirrors Kotlin's <c>String.toIntOrNull()</c> exactly. <c>Problem.kt</c> (and its downstream
/// callers) parse the string-typed numeric fields of <see cref="Model.MagiState"/> (e.g. day1,
/// count, lo, hi, l, u, need1/need2 overrides) using this function pervasively, so its exact
/// parsing rules matter for correctness, not just for "close enough" behavior.
///
/// This is deliberately NOT implemented via <see cref="int.TryParse(string, out int)"/>, because
/// that has two behavioral differences from Kotlin's toIntOrNull that this codebase's data can
/// actually hit:
///
///  1. int.TryParse's default NumberStyles.Integer allows leading/trailing whitespace; Kotlin's
///     toIntOrNull allows none at all (callers that want trimming call <c>.trim()</c> first — this
///     is mirrored at each Problem.cs call site by calling <see cref="string.Trim()"/> before
///     invoking this method, exactly where — and only where — the Kotlin source does).
///  2. int.TryParse only recognizes ASCII '0'-'9'. Kotlin's implementation resolves each character
///     via the JVM's <c>Character.digit(c, 10)</c>, which recognizes ANY Unicode decimal-digit
///     character (category Nd) — full-width digits (U+FF10-FF19), Arabic-Indic digits, etc. This
///     is a real, previously observed behavior difference for this Japanese-language app (a user
///     typing the full-width "２" into a numeric field parses as 2 in the Android app; see the
///     Kotlin project's CLAUDE.md history, 3.327.0 lesson). <see cref="CharUnicodeInfo.GetDecimalDigitValue"/>
///     is the .NET equivalent of <c>Character.digit(c, 10)</c> for this purpose.
///
/// The overflow handling also intentionally reproduces Kotlin's own algorithm (accumulate as a
/// negative number so both int.MinValue and int.MaxValue are representable symmetrically, bounds-
/// checked before each multiply/subtract) rather than delegating to int.TryParse's overflow
/// detection, so the two are guaranteed to agree bit-for-bit on every input, not just typical ones.
/// </summary>
public static class KotlinInterop
{
    public static int? ToIntOrNull(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;

        int start;
        bool isNegative;
        int limit;
        char char0 = s[0];
        if (char0 < '0')
        {
            switch (char0)
            {
                case '-': isNegative = true; limit = int.MinValue; start = 1; break;
                case '+': isNegative = false; limit = -int.MaxValue; start = 1; break;
                default: return null;
            }
        }
        else
        {
            isNegative = false;
            limit = -int.MaxValue;
            start = 0;
        }

        if (start >= s.Length) return null; // sign with no digits following

        const int radix = 10;
        int result = 0;
        int limitBeforeMul = limit / radix;
        for (int idx = start; idx < s.Length; idx++)
        {
            int digit = CharUnicodeInfo.GetDecimalDigitValue(s[idx]);
            if (digit < 0) return null;
            if (result < limitBeforeMul) return null;
            result *= radix;
            if (result < limit + digit) return null;
            result -= digit;
        }
        return isNegative ? result : -result;
    }
}
