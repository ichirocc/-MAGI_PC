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
    /// Mirrors Kotlin's <c>String.toDoubleOrNull()</c>. Used by <c>V6PortAnalyzer.kt</c>'s
    /// <c>aptPenalty</c>/<c>equalizationPenalty</c> (both call sites already <c>.trim()</c> first,
    /// exactly where the Kotlin source does — this method does not trim on its own, matching
    /// <see cref="ToIntOrNull"/>'s convention).
    ///
    /// [フェーズ7ピース1] The phase-7 master plan (from an automated "Understand" workflow) claimed
    /// this needed the same full-width-Unicode-digit-aware, hand-rolled character accumulator as
    /// <see cref="ToIntOrNull"/>. That claim was checked empirically against a real Kotlin runtime
    /// (<c>kotlin-compiler-embeddable</c>) before being trusted, and is **false**:
    /// <c>"３".toDoubleOrNull()</c> (full-width 3) returns <c>null</c> in real Kotlin — unlike
    /// <c>toIntOrNull</c>, <c>toDoubleOrNull</c> does not support full-width digits. It instead
    /// delegates essentially to Java's <c>Double.parseDouble</c> grammar, which — after extensive
    /// empirical cross-checking against .NET's own <see cref="double.TryParse(string, NumberStyles, IFormatProvider, out double)"/>
    /// with <see cref="NumberStyles.Float"/> + <see cref="CultureInfo.InvariantCulture"/> — turned
    /// out to match it almost exactly, with three confirmed divergences handled explicitly below:
    ///
    ///  1. Kotlin accepts a single trailing Java float/double literal type-suffix character
    ///     (<c>d</c>/<c>D</c>/<c>f</c>/<c>F</c>) appended to a genuine numeral (e.g. <c>"1.5d"</c>,
    ///     <c>"1e400d"</c> — the latter legitimately overflows to <c>Infinity</c>, and must still
    ///     succeed). .NET's TryParse rejects any trailing letter outright, so this is handled via
    ///     an explicit strip-and-retry fallback.
    ///  2. Kotlin accepts the <c>NaN</c>/<c>Infinity</c> keyword productions (with optional sign)
    ///     **case-sensitively, exact spelling only** — <c>"nan"</c>/<c>"NAN"</c>/<c>"infinity"</c>/
    ///     <c>"INFINITY"</c>/<c>"-infinity"</c> etc. all return <c>null</c>. .NET's TryParse
    ///     recognizes these symbols **case-insensitively**, so a naive direct delegation would
    ///     wrongly accept those spellings; <see cref="LooksLikeNaNOrInfinityKeyword"/> rejects any
    ///     non-exact-case spelling before/instead of trusting TryParse's built-in recognition.
    ///  3. The type suffix is valid ONLY on a genuine <c>FloatingPointLiteral</c>, never on the
    ///     keyword productions themselves — <c>"NaNd"</c>/<c>"Infinityd"</c>/<c>"+Infinityd"</c>/
    ///     <c>"-Infinityd"</c> all return <c>null</c> in real Kotlin (confirmed empirically; the
    ///     JLS/Kotlin grammar treats these as separate productions). The suffix-stripping fallback
    ///     below re-applies the same keyword rejection to the stripped remainder — so e.g.
    ///     <c>"NaNd"</c> strips to <c>"NaN"</c>, which is caught by the keyword check and rejected,
    ///     rather than being handed to a exact-case fast path that would wrongly accept it.
    ///
    /// A fourth divergence (Java/Kotlin's hex-float literal grammar, e.g. <c>"0x1.8p3" -&gt; 12.0</c>)
    /// is a deliberate, accepted gap: it is NOT replicated here. Both real call sites in this
    /// codebase parse simple decimal apt-count overrides that will never contain hex floats, and
    /// .NET has no built-in support for the C99 hex-float grammar to delegate to.
    /// </summary>
    public static double? ToDoubleOrNull(string? s)
    {
        if (s is null) return null;
        var t = s.Trim();
        if (t.Length == 0) return null;

        var direct = ParseCore(t);
        if (direct.HasValue) return direct;

        char last = t[t.Length - 1];
        if (last is not ('d' or 'D' or 'f' or 'F')) return null;
        var stripped = t.Substring(0, t.Length - 1);
        if (stripped.Length == 0) return null;

        // The d/D/f/F suffix applies only to a genuine numeral literal, never to the NaN/Infinity
        // keyword productions (see divergence #3 above) — reject before parsing the remainder.
        if (LooksLikeNaNOrInfinityKeyword(stripped)) return null;
        return double.TryParse(stripped, NumberStyles.Float, CultureInfo.InvariantCulture, out var v2)
            ? v2
            : null;
    }

    /// <summary>Parses one un-suffixed literal per Kotlin's toDoubleOrNull grammar (see divergences #2/#3 on <see cref="ToDoubleOrNull"/>).</summary>
    private static double? ParseCore(string t)
    {
        // Exact-case NaN/Infinity keyword productions — the only spellings toDoubleOrNull accepts.
        switch (t)
        {
            case "NaN": case "+NaN": case "-NaN": return double.NaN;
            case "Infinity": case "+Infinity": return double.PositiveInfinity;
            case "-Infinity": return double.NegativeInfinity;
        }
        // Any other spelling of the keyword (wrong case) must be rejected before calling
        // TryParse, which would otherwise recognize it case-insensitively and let it through.
        if (LooksLikeNaNOrInfinityKeyword(t)) return null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>
    /// True if <paramref name="t"/> (optionally signed) case-insensitively spells "NaN" or
    /// "Infinity" — i.e. it is SOME spelling of the keyword productions, exact-case or not. Used
    /// to reject non-exact-case spellings that .NET's TryParse would otherwise silently accept.
    /// </summary>
    private static bool LooksLikeNaNOrInfinityKeyword(string t)
    {
        var core = t.Length > 0 && (t[0] == '+' || t[0] == '-') ? t.Substring(1) : t;
        return core.Equals("NaN", StringComparison.OrdinalIgnoreCase)
            || core.Equals("Infinity", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// [フェーズ7ピース11] Mirrors Kotlin's <c>String.take(n)</c>: the first <paramref name="n"/>
    /// characters, or the whole string if it is shorter than <paramref name="n"/> — unlike
    /// <see cref="string.Substring(int, int)"/>, this never throws when <paramref name="n"/>
    /// exceeds the string's length (every call site in this port passes a fixed positive literal,
    /// so Kotlin's own <c>require(n &gt;= 0)</c> guard is not reproduced).
    /// </summary>
    public static string Take(this string s, int n) => s.Substring(0, Math.Min(n, s.Length));
}
