namespace MagiEngine.V6;

/// <summary>
/// [HF507] c3 run-mode helpers — faithful port of <c>C3Run.kt</c>. For a non-forbidden
/// single-shift c3 sequence (e.g. wanting a run of L consecutive days of shift k), the penalty
/// is the run deficit "sum over runs of max(0, L - r)" rather than the per-window count. Used by
/// the checker (<see cref="ViolationChecker"/>), <see cref="Evaluator"/>, and
/// <see cref="DeltaEvaluator"/> so all three agree on this family's dual-mode evaluation.
/// </summary>
public static class C3Run
{
    /// <summary>True iff <paramref name="seq"/> is a non-empty run of the same shift index.</summary>
    public static bool IsSingleShiftSeq(int[] seq)
    {
        if (seq.Length == 0) return false;
        for (int l = 1; l < seq.Length; l++) if (seq[l] != seq[0]) return false;
        return true;
    }

    /// <summary>
    /// Run deficit for staff <paramref name="i"/>'s row over shift <paramref name="k"/> wanting
    /// runs of length <paramref name="lLen"/>: scan consecutive assigned days; each run of length
    /// r (1 &lt;= r &lt; lLen) adds (lLen - r).
    /// </summary>
    public static long RowDeficit(int[][] a, int i, int k, int lLen)
    {
        var row = a[i];
        int t = row.Length;
        long sub = 0;
        int r = 0;
        int j = 0;
        while (j <= t)
        {
            bool on = j < t && row[j] == k;
            if (on)
            {
                r++;
            }
            else if (r > 0)
            {
                int d = lLen - r;
                if (d > 0) sub += d;
                r = 0;
            }
            j++;
        }
        return sub;
    }
}
