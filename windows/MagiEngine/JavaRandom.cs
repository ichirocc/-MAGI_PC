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

    /// <summary>
    /// [フェーズ5a追加] Bit-exact port of <c>java.util.Random.nextInt(int bound)</c> — used
    /// pervasively by <c>SaOptimizer</c>'s SA neighbourhood operators. The rejection-loop
    /// termination condition (<c>bits - val + (bound-1) &lt; 0</c>) relies on 32-bit signed
    /// <c>int</c> overflow wraparound, exactly as it does in the JDK; this project does not enable
    /// <c>CheckForOverflowUnderflow</c>, so C#'s default unchecked <c>int</c> arithmetic wraps the
    /// same way and this is a faithful translation, not an approximation.
    /// </summary>
    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentException("bound must be positive");
        if ((bound & -bound) == bound) // bound is a power of 2
            return (int)((bound * (long)Next(31)) >> 31);
        int bits, val;
        do
        {
            bits = Next(31);
            val = bits % bound;
        } while (bits - val + (bound - 1) < 0);
        return val;
    }

    /// <summary>[フェーズ5a追加] Bit-exact port of <c>java.util.Random.nextDouble()</c>.</summary>
    public double NextDouble() => (((long)Next(26) << 27) + Next(27)) / (double)(1L << 53);

    /// <summary>[フェーズ5b追加] Bit-exact port of <c>java.util.Random.nextBoolean()</c>
    /// (<c>V6SearchOperators.findC3WantFix</c> uses it to pick between cons3/cons3m).</summary>
    public bool NextBoolean() => Next(1) != 0;
}

/// <summary>
/// [フェーズ6/C1JointLnsPolish 追加] Kotlin's <c>Iterable&lt;T&gt;.shuffled(random: java.util.Random)</c>
/// — confirmed (via disassembling the vendored <c>kotlin-stdlib-2.0.21.jar</c>'s
/// <c>CollectionsKt__CollectionsJVMKt.shuffled</c>) to be the JVM-platform-specific overload that
/// calls <c>toMutableList()</c> then delegates directly to
/// <c>java.util.Collections.shuffle(List, Random)</c> — i.e. the JDK's documented in-place
/// Fisher-Yates: <c>for (i = size; i &gt; 1; i--) swap(list, i-1, rnd.nextInt(i))</c> (the
/// <c>RandomAccess</c>-list branch, which applies here since a freshly-copied <see cref="List{T}"/>
/// is random-access). <see cref="JavaRandom.NextInt"/> is already a bit-exact port of
/// <c>java.util.Random.nextInt(int)</c>, so replicating that same loop here reproduces the exact
/// same permutation for the same seed and input order.
/// </summary>
public static class JavaRandomExtensions
{
    public static List<T> Shuffled<T>(this IEnumerable<T> source, JavaRandom rng)
    {
        var list = new List<T>(source);
        for (int i = list.Count; i > 1; i--)
        {
            int j = rng.NextInt(i);
            (list[i - 1], list[j]) = (list[j], list[i - 1]);
        }
        return list;
    }
}
