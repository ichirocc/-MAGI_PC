namespace MagiEngine;

/// <summary>
/// Bit-exact port of <c>java.util.Random</c>'s 48-bit linear congruential generator (the JDK's
/// documented algorithm: multiplier 0x5DEECE66D, increment 0xB, 48-bit state mask), needed
/// because <c>SmartInitialScheduler.generate</c> (phase 4) seeds its per-(shift,staff) DP
/// tie-break via <c>java.util.Random(seed).nextLong()</c>.
///
/// No test in this codebase actually asserts a bit-exact tie-break choice (every assertion in
/// <c>SmartInitialSchedulerTest.kt</c> is on an aggregate/derived property — hard/soft counts,
/// set membership, relative comparisons between two runs — never a specific cell's chosen shift
/// when multiple choices tie on cost), so a different seeded PRNG algorithm would still pass
/// every ported test. This exact port is implemented anyway because the real algorithm is small,
/// well-documented, and removes any ambiguity about whether substituting a different PRNG could
/// ever change which of several equal-cost DP solutions gets picked.
/// </summary>
public sealed class JavaRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1L;

    private long _seed;

    public JavaRandom(long seed)
    {
        _seed = (seed ^ Multiplier) & Mask;
    }

    private int Next(int bits)
    {
        _seed = (_seed * Multiplier + Addend) & Mask;
        // _seed is always in [0, 2^48) after the mask above, so a plain arithmetic right shift
        // is equivalent to Java's unsigned `>>>` here (bit 47 and above are never set).
        return (int)(_seed >> (48 - bits));
    }

    /// <summary>
    /// Mirrors <c>java.util.Random.nextLong()</c> exactly, including its well-known quirk: the
    /// JDK source is <c>return ((long)(next(32)) &lt;&lt; 32) + next(32);</c> — the *second*
    /// <c>next(32)</c> call's result is added as a sign-extended <c>int</c>-to-<c>long</c>
    /// widening conversion, not masked to unsigned 32 bits. C#'s implicit <c>int</c>-to-<c>long</c>
    /// conversion is likewise sign-extending, so the literal expression below reproduces this
    /// exactly.
    /// </summary>
    public long NextLong() => ((long)Next(32) << 32) + Next(32);
}
