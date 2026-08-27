namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>C3nBitScan</c> object — a bitmask scanner for the c3n (禁止連続 /
/// forbidden-sequence) constraint family.
///
/// Folds a staff row into "shift -&gt; bitset of assigned days" and counts forbidden-sequence
/// complete-match windows via AND + shift + popcount. Semantics are kept strictly identical to
/// <see cref="Problem.MakesForbiddenRun"/> / <see cref="C1DeltaPrefilter.StaffC3nFires"/> (both of
/// which mirror the checker's <c>MirrorCore.checkC3Family</c> forbidden branch) — this class does
/// NOT replace either of those scalar implementations; it is used only on the candidate-explosion
/// paths where the naive triple loop (rule × window slide × pattern length) would be too slow.
/// The C++ layer bitmask-ified the same window match in the same way (out of scope for this port);
/// this is the pure-managed Kotlin/C# equivalent.
///
/// T&gt;64 doesn't fit in a <c>long</c>, so callers go through <see cref="C3nRowScan"/> to fall back
/// to the scalar semantics for those cases (same policy as the bitmask introduction elsewhere in
/// this codebase). The raw bit API here is restricted to 64-day horizons.
/// </summary>
internal static class C3nBitScan
{
    /// <summary>Whether the bit path is usable (T within 1..64). If false, callers must use the existing scalar implementation.</summary>
    public static bool Usable(Problem p) => p.T is >= 1 and <= 64;

    /// <summary>Folds staff row <paramref name="row"/> into "shift k -> bitset of days assigned k". Out-of-range cells set no bit.</summary>
    public static long[] BuildRowMask(Problem p, int[] row)
    {
        if (!Usable(p)) throw new ArgumentException("C3nBitScan requires 1..64 days; use C3nRowScan for scalar fallback");
        var m = new long[p.K];
        int t = Math.Min(p.T, row.Length);
        for (int j = 0; j < t; j++)
        {
            int k = row[j];
            if (k is >= 0 && k < p.K) m[k] |= 1L << j;
        }
        return m;
    }

    /// <summary>Swaps day <paramref name="j"/>'s assignment in <paramref name="mask"/> to <paramref name="newK"/> (in-place; caller restores via a mirrored call before returning).</summary>
    private static void SetCell(Problem p, long[] mask, int j, int oldK, int newK)
    {
        long bit = 1L << j;
        if (oldK is >= 0 && oldK < p.K) mask[oldK] &= ~bit;
        if (newK is >= 0 && newK < p.K) mask[newK] |= bit;
    }

    /// <summary>Bitset of window-start days where rule <paramref name="c"/> matches completely. Bit s set iff days s..s+d-1 match seq exactly.</summary>
    private static long MatchMask(Problem p, long[] mask, C3 c)
    {
        var seq = c.Seq;
        int d = seq.Length;
        if (d == 0 || d > p.T) return 0L;
        // [audit#7と同じ方針] out-of-range のシフト index でマスクを引かない（構造上 resolveC3 が弾くが防御）。
        foreach (var k in seq) if (k < 0 || k >= p.K) return 0L;
        long full = mask[seq[0]];
        for (int l = 1; l < d; l++)
        {
            full &= mask[seq[l]] >>> l;
            if (full == 0L) return 0L;
        }
        // 開始位置として有効なのは [0, T-d]。それを超える bit は窓が期間からはみ出すので落とす。
        int starts = p.T - d + 1;
        long valid = starts >= 64 ? -1L : (1L << starts) - 1L;
        return full & valid;
    }

    /// <summary>Row-wide c3n fire count (matches <see cref="C1DeltaPrefilter.StaffC3nFires"/>).</summary>
    public static int Fires(Problem p, long[] mask)
    {
        int n = 0;
        foreach (var c in p.Cons3n) n += System.Numerics.BitOperations.PopCount((ulong)MatchMask(p, mask, c));
        return n;
    }

    /// <summary>Row-wide fire count if day <paramref name="j"/> were set to <paramref name="newK"/>. <paramref name="mask"/> is unchanged on return.</summary>
    public static int FiresAfterSet(Problem p, long[] mask, int j, int oldK, int newK)
    {
        if (j < 0 || j >= p.T) return Fires(p, mask);
        if (oldK == newK) return Fires(p, mask);
        SetCell(p, mask, j, oldK, newK);
        int n = Fires(p, mask);
        SetCell(p, mask, j, newK, oldK);
        return n;
    }

    /// <summary>Whether setting day <paramref name="j"/> to <paramref name="newK"/> makes a forbidden sequence that spans day <paramref name="j"/> itself (matches <see cref="Problem.MakesForbiddenRun"/>). <paramref name="mask"/> is unchanged on return.</summary>
    public static bool HitsAfterSet(Problem p, long[] mask, int j, int oldK, int newK)
    {
        if (j < 0 || j >= p.T) return false;
        SetCell(p, mask, j, oldK, newK);
        bool hit = false;
        foreach (var c in p.Cons3n)
        {
            int d = c.Seq.Length;
            if (d == 0 || d > p.T) continue;
            int lo = Math.Max(j - d + 1, 0);
            int hi = Math.Min(j, p.T - d);
            if (lo > hi) continue;
            long cover = RangeMask(lo, hi);
            if ((MatchMask(p, mask, c) & cover) != 0L) { hit = true; break; }
        }
        SetCell(p, mask, j, newK, oldK);
        return hit;
    }

    /// <summary>
    /// Bitset of days actually used by the forbidden sequences currently matching in
    /// <paramref name="mask"/> that span day <paramref name="j"/> (= the candidate days one could
    /// change to break that pattern). Zero if none currently match.
    /// </summary>
    public static long CoveringRunDays(Problem p, long[] mask, int j)
    {
        if (j < 0 || j >= p.T) return 0L;
        long days = 0L;
        foreach (var c in p.Cons3n)
        {
            int d = c.Seq.Length;
            if (d == 0 || d > p.T) continue;
            int lo = Math.Max(j - d + 1, 0);
            int hi = Math.Min(j, p.T - d);
            if (lo > hi) continue;
            long hits = MatchMask(p, mask, c) & RangeMask(lo, hi);
            while (hits != 0L)
            {
                int s = System.Numerics.BitOperations.TrailingZeroCount((ulong)hits);
                days |= RangeMask(s, s + d - 1);
                hits &= hits - 1;
            }
        }
        return days;
    }

    /// <summary>"After-set" variant of <see cref="CoveringRunDays"/>: the covering-day bitset if day <paramref name="j"/> were set to <paramref name="newK"/>. <paramref name="mask"/> is unchanged on return.</summary>
    public static long CoveringRunDaysAfterSet(Problem p, long[] mask, int j, int oldK, int newK)
    {
        if (j < 0 || j >= p.T) return 0L;
        SetCell(p, mask, j, oldK, newK);
        long days = CoveringRunDays(p, mask, j);
        SetCell(p, mask, j, newK, oldK);
        return days;
    }

    /// <summary>Mask with bits [lo, hi] (inclusive) set. lo&gt;hi yields 0.</summary>
    public static long RangeMask(int lo, int hi)
    {
        if (lo > hi) return 0L;
        int l = Math.Max(lo, 0);
        int h = Math.Min(hi, 63);
        if (l > h) return 0L;
        int width = h - l + 1;
        long baseMask = width >= 64 ? -1L : (1L << width) - 1L;
        return baseMask << l;
    }
}

/// <summary>
/// Faithful port of Kotlin's <c>C3nRowScan</c> class — the single entry point for reading a staff
/// row's c3n state.
///
/// Uses <see cref="C3nBitScan"/>'s long+popcount path for T&lt;=64, and falls back to the existing
/// scalar semantics for T&gt;65. Callers never branch on "which path" themselves, which avoids the
/// bug class of a bit-shift distance wrapping around at 64.
/// </summary>
internal sealed class C3nRowScan
{
    private readonly Problem _p;
    private readonly int[] _row;
    private readonly long[]? _bitMask;

    public C3nRowScan(Problem p, int[] row)
    {
        _p = p;
        _row = row;
        _bitMask = C3nBitScan.Usable(p) ? C3nBitScan.BuildRowMask(p, row) : null;
    }

    public int Fires() => _bitMask is { } mask ? C3nBitScan.Fires(_p, mask) : C1DeltaPrefilter.StaffC3nFires(_p, _row);

    /// <summary>Fire count if row day <paramref name="day"/> were replaced with <paramref name="newShift"/>. Row/mask unchanged on return.</summary>
    public int FiresAfterSet(int day, int newShift)
    {
        if (day < 0 || day >= _p.T || day < 0 || day >= _row.Length) return Fires();
        int old = _row[day];
        if (old == newShift) return Fires();
        if (_bitMask is { } mask) return C3nBitScan.FiresAfterSet(_p, mask, day, old, newShift);
        _row[day] = newShift;
        try
        {
            return C1DeltaPrefilter.StaffC3nFires(_p, _row);
        }
        finally
        {
            _row[day] = old;
        }
    }

    /// <summary>All candidate days spanned by forbidden patterns currently matching around <paramref name="anchorDay"/>.</summary>
    public int[] CoveringDays(int anchorDay)
    {
        if (anchorDay < 0 || anchorDay >= _p.T) return Array.Empty<int>();
        if (_bitMask is { } mask) return DaysFromBits(C3nBitScan.CoveringRunDays(_p, mask, anchorDay));
        return ScalarCoveringDays(anchorDay);
    }

    /// <summary>All candidate days spanned by forbidden patterns that would match after setting row day <paramref name="day"/> to <paramref name="newShift"/>.</summary>
    public int[] CoveringDaysAfterSet(int day, int newShift)
    {
        if (day < 0 || day >= _p.T || day < 0 || day >= _row.Length) return Array.Empty<int>();
        int old = _row[day];
        if (old == newShift) return CoveringDays(day);
        if (_bitMask is { } mask)
        {
            return DaysFromBits(C3nBitScan.CoveringRunDaysAfterSet(_p, mask, day, old, newShift));
        }
        _row[day] = newShift;
        try
        {
            return ScalarCoveringDays(day);
        }
        finally
        {
            _row[day] = old;
        }
    }

    private static int[] DaysFromBits(long initialBits)
    {
        long bits = initialBits;
        var outArr = new int[System.Numerics.BitOperations.PopCount((ulong)bits)];
        int n = 0;
        while (bits != 0L)
        {
            outArr[n++] = System.Numerics.BitOperations.TrailingZeroCount((ulong)bits);
            bits &= bits - 1;
        }
        return outArr;
    }

    /// <summary>T&gt;64 用。既存チェッカーと同じ完全一致窓をそのまま走査する。</summary>
    private int[] ScalarCoveringDays(int anchorDay)
    {
        var selected = new bool[_p.T];
        foreach (var c in _p.Cons3n)
        {
            int width = c.Seq.Length;
            if (width == 0 || width > _p.T) continue;
            int lo = Math.Max(anchorDay - width + 1, 0);
            int hi = Math.Min(anchorDay, _p.T - width);
            for (int start = lo; start <= hi; start++)
            {
                bool matches = true;
                for (int offset = 0; offset < width; offset++)
                {
                    int idx = start + offset;
                    int actual = idx >= 0 && idx < _row.Length ? _row[idx] : -1;
                    if (actual != c.Seq[offset]) { matches = false; break; }
                }
                if (matches) for (int day = start; day < start + width; day++) selected[day] = true;
            }
        }
        var outList = new List<int>();
        for (int day = 0; day < selected.Length; day++) if (selected[day]) outList.Add(day);
        return outList.ToArray();
    }
}
