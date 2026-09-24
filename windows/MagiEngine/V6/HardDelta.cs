namespace MagiEngine.V6;

/// <summary>
/// 候補盤面の HARD 正味差分（groupViol/pref/c3w/c3n/covU）を変わったセル・行・日だけから厳密に数える
/// （Kotlin 原本 <c>HardDelta.kt</c> の忠実な移植）。<c>Check(cand).Hard == Check(baseB).Hard + Delta(...)</c> が
/// <see cref="UnifiedViolationChecker.Check"/> と同じ意味論で成り立つ。呼出側は「このΔなら必ず却下する」候補だけ
/// checker を省く＝採用集合・盤面は不変の速度専用（<see cref="PolishGate.HardDeltaPrefilter"/>）。
/// </summary>
internal static class HardDelta
{
    private static int V(Problem p, int x) => x >= 0 && x < p.K ? x : -1;

    /// <summary>1 セル (i,j)=k の、セル単位 HARD 族（groupViol/pref/c3w）の件数。</summary>
    private static int CellHard(Problem p, int i, int j, int k)
    {
        int h = 0;
        if (k >= 0 && !p.CanDo(i, k)) h++;
        int w = p.Wish[i][j];
        if (w >= 0 && w < p.K && p.CanDo(i, w) && k != w) h++;
        if (p.C3wBanned(i, j, k)) h++;
        return h;
    }

    /// <summary>行 row の cons3n fire のうち、窓が列 j を含むものの数（j を含まない窓は j の書換えで変わらない）。</summary>
    private static int C3nFiresCovering(Problem p, int[] row, int j)
    {
        int fires = 0;
        foreach (var c in p.Cons3n)
        {
            var seq = c.Seq;
            int d = seq.Length;
            if (d == 0 || d > p.T) continue;
            int hi = Math.Min(j, p.T - d);
            for (int s = Math.Max(j - d + 1, 0); s <= hi; s++)
            {
                int z = 0;
                for (int l = 0; l < d; l++) if (row[s + l] == seq[l]) z++;
                if (z == d) fires++;
            }
        }
        return fires;
    }

    /// <summary>任意の多セル変更 baseB→cand の HARD 正味差分。covU は変わった日の (日,シフト) 人数の到着・離脱の両方を数える。</summary>
    public static int Delta(Problem p, int[][] baseB, int[][] cand)
    {
        int d = 0;
        bool[]? rows = null;
        bool[]? days = null;
        for (int i = 0; i < p.S; i++)
        {
            var br = baseB[i]; var cr = cand[i];
            for (int j = 0; j < p.T; j++)
            {
                int o = V(p, br[j]), n = V(p, cr[j]);
                if (o == n) continue;
                d += CellHard(p, i, j, n) - CellHard(p, i, j, o);
                (rows ??= new bool[p.S])[i] = true;
                (days ??= new bool[p.T])[j] = true;
            }
        }
        if (rows == null) return d;
        if (p.Cons3n.Count > 0)
        {
            for (int i = 0; i < p.S; i++)
            {
                if (!rows[i]) continue;
                var nr = new int[p.T]; var or = new int[p.T];
                for (int j = 0; j < p.T; j++) { nr[j] = V(p, cand[i][j]); or[j] = V(p, baseB[i][j]); }
                d += C1DeltaPrefilter.StaffC3nFires(p, nr) - C1DeltaPrefilter.StaffC3nFires(p, or);
            }
        }
        var cb = new int[p.K]; var cc = new int[p.K];
        for (int j = 0; j < p.T; j++)
        {
            if (!days![j]) continue;
            Array.Clear(cb); Array.Clear(cc);
            for (int i = 0; i < p.S; i++)
            {
                int b = V(p, baseB[i][j]); if (b >= 0) cb[b]++;
                int c = V(p, cand[i][j]); if (c >= 0) cc[c]++;
            }
            for (int k = 0; k < p.K; k++) if (cb[k] != cc[k]) d += p.CovUCell(k, j, cc[k]) - p.CovUCell(k, j, cb[k]);
        }
        return d;
    }

    /// <summary>
    /// 同日 j 内の置換（職員 staff[t] の旧値 old[t] → 現在の work[staff[t]][j]）の HARD 正味差分。work は適用後。
    /// 健全性: 置換は日 j の値の多重集合を保つので (j,k) 人数が不変＝covU の差は 0。groupViol/pref/c3w はセル単位
    /// （c3wBan は静的表）、c3n は変わった行の j を含む窓だけが変わる＝この和は <see cref="Delta"/> と一致する。
    /// </summary>
    public static int SameDayPermutationDelta(Problem p, int[][] work, int j, int[] staff, int[] old)
    {
        int d = 0;
        for (int t = 0; t < staff.Length; t++)
        {
            int i = staff[t]; var row = work[i];
            int n = V(p, row[j]), o = V(p, old[t]);
            if (n == o) continue;
            d += CellHard(p, i, j, n) - CellHard(p, i, j, o);
            if (p.Cons3n.Count > 0)
            {
                int after = C3nFiresCovering(p, row, j);
                int keep = row[j]; row[j] = old[t];
                d += after - C3nFiresCovering(p, row, j);
                row[j] = keep;
            }
        }
        return d;
    }
}
