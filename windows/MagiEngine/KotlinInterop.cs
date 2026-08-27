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

    /// <summary>
    /// Mirrors Java's <c>Math.round(double a): long</c> — "round half up" (an exact .5 midpoint
    /// always rounds toward positive infinity, regardless of sign), per the JDK's documented
    /// contract: the result equals <c>(long) Math.floor(a + 0.5d)</c> for every finite value,
    /// with NaN -&gt; 0 and out-of-<see cref="long"/>-range values clamped to
    /// <see cref="long.MinValue"/>/<see cref="long.MaxValue"/>.
    ///
    /// This differs from C#'s <see cref="Math.Round(double)"/> default (banker's
    /// rounding/round-half-to-even at exact midpoints), which is why a bespoke helper is needed
    /// rather than a direct call. Phase 3's ported v6 code uses this pattern for two families:
    /// <c>weeklyFloorOfCount</c>/<c>weeklyDevOfBucket</c> (divisor 7 — for which the rounding
    /// *mode* is actually provably irrelevant, since <c>c / 7.0</c> for an integer <c>c</c> can
    /// never land exactly on a .5 midpoint: that would require <c>c = 7k + 3.5</c> for some
    /// integer <c>k</c>, which has no integer solution) and <c>fairDevAt</c>'s group-average
    /// target (divisor = the group's member count <c>m</c>, which is NOT fixed and CAN produce
    /// reachable .5 midpoints for some group sizes — e.g. sum=3, m=2 -&gt; 1.5). The helper is
    /// implemented once, faithfully, and used everywhere this pattern appears so correctness
    /// never depends on which divisor a particular call site happens to use.
    ///
    /// The real OpenJDK implementation is a bit-twiddling optimization of the same contract; the
    /// straightforward <c>floor(a + 0.5d)</c> formula below is bit-for-bit equivalent to it for
    /// every value this codebase's data can actually produce (small counts of scheduled shifts
    /// over at most ~31 days, divided by small integers) — the two implementations can only
    /// diverge near <see cref="double"/>'s precision limit close to <see cref="long.MaxValue"/>,
    /// far outside this domain.
    /// </summary>
    public static long MathRound(double a)
    {
        if (double.IsNaN(a)) return 0L;
        if (a <= long.MinValue) return long.MinValue;
        if (a >= long.MaxValue) return long.MaxValue;
        return (long)Math.Floor(a + 0.5d);
    }

    /// <summary>
    /// Mirrors Kotlin/Java's <c>Math.floorMod(x, y)</c>: the result always has the same sign as
    /// <paramref name="y"/> (floor division), unlike C#'s <c>%</c> operator (truncated division,
    /// same as Kotlin's own <c>%</c>/Java's <c>%</c> — result takes the sign of the dividend).
    /// The two agree whenever <paramref name="x"/> is non-negative, but this codebase's phase-5
    /// hypothesis-index arithmetic (<see cref="V6.HypothesisDiversityPolicy.StartPlanFor"/>) uses
    /// the Kotlin source's actual call, so the floor variant is ported faithfully rather than
    /// assumed equivalent to <c>%</c>.
    /// </summary>
    public static int FloorMod(int x, int y)
    {
        int r = x % y;
        return (r != 0 && (r < 0) != (y < 0)) ? r + y : r;
    }
}
