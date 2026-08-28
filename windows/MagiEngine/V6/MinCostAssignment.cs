namespace MagiEngine.V6;

/// <summary>
/// 正方コスト行列に対する最小費用割当（Hungarian / Kuhn–Munkres、ポテンシャル法 O(n^3)）。
/// ソフト研磨の「日ごと厳密割当」で使用。禁止辺は十分大きなコスト(<see cref="Inf"/>)で表現する。
/// 返り値 assign[row] = 割り当てられた列。
///
/// [Kotlin 3.278.0/監査で実証されたクラッシュの修正] ある行が<b>全列 INF</b>（＝その職員が当日のどの
/// slot も担当不可: 担当可否が全て未チェックの群の職員・-1センチネルセル等）のとき、ポテンシャル
/// <c>v[j]</c> は単調非増加(≤0)のため <c>cur = INF − u − v ≥ INF = minv 初期値</c> で厳密更新が
/// 一度も起きず、<c>j1 = -1</c> のまま <c>p[-1]</c> を読んで AIOOBE でプロセスごと落ちていた
/// （<c>handleOptimize</c> フル経路で再現済み。兄弟実装 <c>minCostPerfectAssignment</c>（手M用）は
/// null fail-safe を持つのにこちらだけ欠けていた非対称）。<c>j1 == -1</c> は「この行に到達可能な列が
/// 無い＝実行可能な完全割当なし」を意味するため<b>null を返す</b>。呼出側はその日をスキップする
/// （keep-best 不変・退化不能）。
/// </summary>
public static class MinCostAssignment
{
    public const long Inf = long.MaxValue / 4;

    public static int[]? Solve(long[][] cost)
    {
        int n = cost.Length;
        if (n == 0) return Array.Empty<int>();
        foreach (var row in cost)
        {
            if (row.Length != n) throw new ArgumentException("cost must be square", nameof(cost));
        }

        var u = new long[n + 1];
        var v = new long[n + 1];
        var p = new int[n + 1];   // p[col] = 割当済み row（1-indexed）。p[0] は探索の起点マーカ。
        var way = new int[n + 1];
        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            var minv = new long[n + 1];
            Array.Fill(minv, Inf);
            var used = new bool[n + 1];
            do
            {
                used[j0] = true;
                int i0 = p[j0];
                long delta = Inf;
                int j1 = -1;
                for (int j = 1; j <= n; j++)
                {
                    if (!used[j])
                    {
                        long cur = cost[i0 - 1][j - 1] - u[i0] - v[j];
                        if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                        if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                    }
                }
                if (j1 == -1) return null;   // 全INF行＝増加路なし（実行可能な完全割当が存在しない）
                for (int j = 0; j <= n; j++)
                {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else minv[j] -= delta;
                }
                j0 = j1;
            } while (p[j0] != 0);
            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        var assign = new int[n];
        for (int j = 1; j <= n; j++)
        {
            if (p[j] >= 1 && p[j] <= n) assign[p[j] - 1] = j - 1;
        }
        return assign;
    }
}
