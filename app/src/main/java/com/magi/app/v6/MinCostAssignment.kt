package com.magi.app.v6

/**
 * 正方コスト行列に対する最小費用割当（Hungarian / Kuhn–Munkres、ポテンシャル法 O(n^3)）。
 * ソフト研磨の「日ごと厳密割当」で使用。禁止辺は十分大きなコスト(INF)で表現する。
 * 返り値 assign[row] = 割り当てられた列。
 *
 * [3.278.0/監査で実証されたクラッシュの修正] ある行が**全列 INF**（＝その職員が当日のどの slot も
 * 担当不可: 担当可否が全て未チェックの群の職員・-1センチネルセル等）のとき、ポテンシャル v[j] は
 * 単調非増加(≤0)のため `cur = INF − u − v ≥ INF = minv 初期値` で厳密更新が一度も起きず、
 * j1 = -1 のまま `p[-1]` を読んで AIOOBE でプロセスごと落ちていた（handleOptimize フル経路で再現済み。
 * 兄弟実装 minCostPerfectAssignment(手M用) は null fail-safe を持つのにこちらだけ欠けていた非対称）。
 * j1 == -1 は「この行に到達可能な列が無い＝実行可能な完全割当なし」を意味するため **null を返す**。
 * 呼出側はその日をスキップする（keep-best 不変・退化不能）。
 */
object MinCostAssignment {
    const val INF: Long = Long.MAX_VALUE / 4

    fun solve(cost: Array<LongArray>): IntArray? {
        val n = cost.size
        if (n == 0) return IntArray(0)
        require(cost.all { it.size == n }) { "cost must be square" }
        val u = LongArray(n + 1)
        val v = LongArray(n + 1)
        val p = IntArray(n + 1)        // p[col] = 割当済み row（1-indexed）。p[0] は探索の起点マーカ。
        val way = IntArray(n + 1)
        for (i in 1..n) {
            p[0] = i
            var j0 = 0
            val minv = LongArray(n + 1) { INF }
            val used = BooleanArray(n + 1)
            do {
                used[j0] = true
                val i0 = p[j0]
                var delta = INF
                var j1 = -1
                for (j in 1..n) {
                    if (!used[j]) {
                        val cur = cost[i0 - 1][j - 1] - u[i0] - v[j]
                        if (cur < minv[j]) { minv[j] = cur; way[j] = j0 }
                        if (minv[j] < delta) { delta = minv[j]; j1 = j }
                    }
                }
                if (j1 == -1) return null   // 全INF行＝増加路なし（実行可能な完全割当が存在しない）
                for (j in 0..n) {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta } else minv[j] -= delta
                }
                j0 = j1
            } while (p[j0] != 0)
            do {
                val j1 = way[j0]
                p[j0] = p[j1]
                j0 = j1
            } while (j0 != 0)
        }
        val assign = IntArray(n)
        for (j in 1..n) if (p[j] in 1..n) assign[p[j] - 1] = j - 1
        return assign
    }
}
