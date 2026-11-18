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

    /// <summary>職員の行 <paramref name="row"/> で、日 <paramref name="js"/> から <paramref name="seq"/> がそのまま並んでいるか（Kotlin <c>C3Run.matchAt</c>）。</summary>
    public static bool MatchAt(int[] row, int js, int[] seq)
    {
        for (int l = 0; l < seq.Length; l++) if (row[js + l] != seq[l]) return false;
        return true;
    }

    /// <summary>
    /// 複数シフトの推奨の並び（c3・c3m）で、日 <paramref name="js"/> の窓を 1 件と数えるか: 先頭が一致して並び全体は一致しない窓。
    /// ただし前の日に始まって並び全体が一致した窓の中（2 つ目以降のセル）は数えない（Kotlin <c>C3Run.wantFires</c>、Android 3.727.102）。
    /// </summary>
    public static bool WantFires(int[] row, int js, int[] seq)
    {
        if (row[js] != seq[0] || MatchAt(row, js, seq)) return false;
        for (int l = 1; l < seq.Length; l++) if (js - l >= 0 && MatchAt(row, js - l, seq)) return false;
        return true;
    }

    /// <summary><see cref="WantFires"/> のビット版。<paramref name="first"/>＝先頭が一致する開始日、<paramref name="full"/>＝並び全体が一致する開始日。</summary>
    public static long WantFireMask(long first, long full, int d)
    {
        long covered = 0L;
        for (int l = 1; l < d; l++) covered |= full << l;
        return first & ~full & ~covered;
    }
}
