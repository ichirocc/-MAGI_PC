package com.magi.app.v6

/**
 * [c3n(禁止連続)のビット走査] 職員行を「シフト→割当日のビット集合」へ畳んで、禁止連続の完全一致窓を
 * AND＋シフト＋popcount で数える。意味論は `Problem.makesForbiddenRun` / `C1DeltaPrefilter.staffC3nFires`
 * （どちらもチェッカー `MirrorCore.checkC3Family` の forbidden 分岐と同一）と厳密に一致させる。
 *
 * 動機（3.303.0）: c3n の回避経路を「隣接日1日」から「パターンがまたぐ全日」へ広げ、さらに当日自身も
 * 変更候補に含めると、1つの違反あたりの候補数が (パターン長 × 担当可能シフト数) 倍に増える。素朴な
 * 三重ループ（ルール × 窓スライド × パターン長）のままでは探索が回らないため、先に判定を畳む。
 * 実データ（cons3n 13本・T=31）で、1候補あたり ルール13 × 窓31 × 長さ2.2 ≒ 890 回の比較が、
 * 行マスク構築 O(T)=31 ＋ 判定 O(Σd)=29 に落ちる。
 *
 * C++ 側は 3.174.0 で `SaChunk::contribC3RowFam` の窓マッチを同型に popcount 化済み。これはその
 * Kotlin 版だが、**既存のスカラー実装は一切置き換えない**（`makesForbiddenRun` は全経路が依存する
 * オラクルとして温存し、こちらは候補が爆発する新経路だけが使う）。両者の一致は
 * `C3nBitScanTest` のランダムパリティで固定する。
 *
 * T>64 は long に収まらないためスカラーへフォールバックする（3.172.0 の bitmask 導入と同じ方針）。
 */
internal object C3nBitScan {

    /** ビット経路が使えるか（T が 64 日以内）。偽なら呼び出し側は既存のスカラー実装を使う。 */
    fun usable(p: Problem): Boolean = p.T in 1..64

    /** 職員行 [row] を「シフト k → k が割り当てられた日のビット集合」へ。範囲外セルはどこにも立てない。 */
    fun buildRowMask(p: Problem, row: IntArray): LongArray {
        val m = LongArray(p.K)
        val t = minOf(p.T, row.size)
        for (j in 0 until t) {
            val k = row[j]
            if (k in 0 until p.K) m[k] = m[k] or (1L shl j)
        }
        return m
    }

    /** [mask] の日 [j] の割当を [newK] へ差し替える（[restoreCell] で戻す前提の in-place 操作）。 */
    private fun setCell(p: Problem, mask: LongArray, j: Int, oldK: Int, newK: Int) {
        val bit = 1L shl j
        if (oldK in 0 until p.K) mask[oldK] = mask[oldK] and bit.inv()
        if (newK in 0 until p.K) mask[newK] = mask[newK] or bit
    }

    /**
     * ルール [c] が完全一致する「窓の開始日」のビット集合。
     * bit s が立つ ⇔ 日 s..s+d-1 が seq と完全一致。
     */
    private fun matchMask(p: Problem, mask: LongArray, c: C3): Long {
        val seq = c.seq
        val d = seq.size
        if (d == 0 || d > p.T) return 0L
        // [audit#7 と同じ方針] 範囲外のシフト index でマスクを引かない（構造上 resolveC3 が弾くが防御）。
        for (k in seq) if (k !in 0 until p.K) return 0L
        var full = mask[seq[0]]
        for (l in 1 until d) {
            full = full and (mask[seq[l]] ushr l)
            if (full == 0L) return 0L
        }
        // 開始位置として有効なのは [0, T-d]。それを超える bit は窓が期間からはみ出すので落とす。
        val starts = p.T - d + 1
        val valid = if (starts >= 64) -1L else (1L shl starts) - 1L
        return full and valid
    }

    /** 行全体の c3n fire 数（`C1DeltaPrefilter.staffC3nFires` と同値）。 */
    fun fires(p: Problem, mask: LongArray): Int {
        var n = 0
        for (c in p.cons3n) n += java.lang.Long.bitCount(matchMask(p, mask, c))
        return n
    }

    /** 日 [j] を [newK] にしたときの行全体の fire 数。[mask] は呼び出し前後で不変。 */
    fun firesAfterSet(p: Problem, mask: LongArray, j: Int, oldK: Int, newK: Int): Int {
        if (oldK == newK) return fires(p, mask)
        setCell(p, mask, j, oldK, newK)
        val n = fires(p, mask)
        setCell(p, mask, j, newK, oldK)
        return n
    }

    /**
     * 日 [j] を [newK] にすると **日 j をまたぐ窓で** 禁止連続が成立するか
     * （`Problem.makesForbiddenRun` と同値）。[mask] は呼び出し前後で不変。
     */
    fun hitsAfterSet(p: Problem, mask: LongArray, j: Int, oldK: Int, newK: Int): Boolean {
        if (j !in 0 until p.T) return false
        setCell(p, mask, j, oldK, newK)
        var hit = false
        for (c in p.cons3n) {
            val d = c.seq.size
            if (d == 0 || d > p.T) continue
            val lo = (j - d + 1).coerceAtLeast(0)
            val hi = minOf(j, p.T - d)
            if (lo > hi) continue
            val cover = rangeMask(lo, hi)
            if ((matchMask(p, mask, c) and cover) != 0L) { hit = true; break }
        }
        setCell(p, mask, j, newK, oldK)
        return hit
    }

    /**
     * いま [mask] で成立している禁止連続のうち、日 [j] をまたぐものが実際に使っている日の集合
     * （＝そのパターンを崩すために変更しうる候補日）。成立していなければ 0。
     *
     * [3.303.0] 旧 `tryFixForbiddenRunViaAdjacentDay` は j±1 の2日しか崩しに行かず、実データにある
     * 3連（Dﾃ→休→A4 等）でパターンの先頭 j-2 に届かなかった。ここでパターンがまたぐ全日を返し、
     * 呼び出し側がその全部を候補にできるようにする。
     */
    fun coveringRunDays(p: Problem, mask: LongArray, j: Int): Long {
        if (j !in 0 until p.T) return 0L
        var days = 0L
        for (c in p.cons3n) {
            val d = c.seq.size
            if (d == 0 || d > p.T) continue
            val lo = (j - d + 1).coerceAtLeast(0)
            val hi = minOf(j, p.T - d)
            if (lo > hi) continue
            var hits = matchMask(p, mask, c) and rangeMask(lo, hi)
            while (hits != 0L) {
                val s = java.lang.Long.numberOfTrailingZeros(hits)
                days = days or rangeMask(s, s + d - 1)
                hits = hits and (hits - 1)
            }
        }
        return days
    }

    /**
     * 日 [j] を [newK] にしたときに成立する禁止パターンがまたぐ日の集合（[coveringRunDays] の
     * 「変更後」版）。[mask] は呼び出し前後で不変。
     */
    fun coveringRunDaysAfterSet(p: Problem, mask: LongArray, j: Int, oldK: Int, newK: Int): Long {
        if (j !in 0 until p.T) return 0L
        setCell(p, mask, j, oldK, newK)
        val days = coveringRunDays(p, mask, j)
        setCell(p, mask, j, newK, oldK)
        return days
    }

    /** [lo]..[hi]（両端含む）の bit を立てたマスク。lo>hi は 0。 */
    fun rangeMask(lo: Int, hi: Int): Long {
        if (lo > hi) return 0L
        val l = lo.coerceAtLeast(0)
        val h = hi.coerceAtMost(63)
        if (l > h) return 0L
        val width = h - l + 1
        val base = if (width >= 64) -1L else (1L shl width) - 1L
        return base shl l
    }
}
