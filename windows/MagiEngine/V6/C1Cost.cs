namespace MagiEngine.V6;

/// <summary>
/// 期間の制約（c1）の件数（Kotlin <c>C1Cost</c>、Android 3.727.111）。規則×職員ごとに、窓を 1 日ずつ滑らせて不足量（day2 − 回数）を見て、
/// 不足している窓が続くまとまりの「不足量の和」を 2 乗して足す（不足 1 の窓が 1 つ＝1 件、3 つ続く＝9 件）。
/// </summary>
public static class C1Cost
{
    /// <summary>不足量を窓の順に <see cref="Add"/> し、最後に <see cref="End"/> で件数を受け取る。</summary>
    public sealed class Acc
    {
        private long _run;
        private long _tot;
        public void Add(int deficit) { if (deficit > 0) _run += deficit; else { _tot += _run * _run; _run = 0; } }
        public long End() { _tot += _run * _run; _run = 0; return _tot; }
    }

    public static long RowCost(int[] row, int x, int day1, int day2)
    {
        int t = row.Length;
        if (day1 <= 0 || day1 > t) return 0L;
        var acc = new Acc();
        int z = 0;
        for (int l = 0; l < day1; l++) if (row[l] == x) z++;
        for (int j = 0; j <= t - day1; j++)
        {
            if (j > 0) { if (row[j - 1] == x) z--; if (row[j + day1 - 1] == x) z++; }
            acc.Add(day2 - z);
        }
        return acc.End();
    }
}
