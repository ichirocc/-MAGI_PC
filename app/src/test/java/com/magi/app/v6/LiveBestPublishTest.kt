package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.util.concurrent.CyclicBarrier
import java.util.concurrent.atomic.AtomicReference

/**
 * [3.385.0] `publishLiveBest` が publish する「評価」と「盤面」が食い違わないことを固定する。
 *
 * 旧実装は report を CAS してから**その外で**盤面を代入していたため、CAS に勝った直後に
 * プリエンプトされたスレッドが、より良い盤面を書いた後続スレッドを**劣る盤面で上書き**できた
 * （`liveBestReport` は最良なのに `liveBest` は劣る盤面＝docstring が謳う単調性が破れる）。
 *
 * 競合そのものは確率的にしか再現できないので、ここでは**盤面に自分の評価値を埋め込み**、
 * 「最後に残った盤面が、publish された最良の評価に対応していること」を検査する。
 * 盤面コピーが O(職員×日) かかることが窓を作るので、実データ規模（30×31）で回す。
 */
class LiveBestPublishTest {

    private fun rep(hard: Int) = ViolationReport(
        violations = emptyMap(),
        needViolations = emptyMap(),
        countViolations = emptyMap(),
        breakdown = emptyMap(),
        total = hard,
        hard = hard,
        soft = 0,
        weightedScore = hard.toDouble(),
    )

    /** 全セルに `mark` を敷いた盤面（どのセルを読んでも由来が分かる＝部分的な上書きも検出できる）。 */
    private fun board(mark: Int, staff: Int = 30, days: Int = 31) =
        Array(staff) { IntArray(days) { mark } }

    @Test
    fun publishedBoardAlwaysMatchesThePublishedBest() {
        val threads = 8
        val perThread = 400
        // 評価は小さいほど良い（hard が第1キー）。全スレッドを通した最小値が最終的な勝者。
        val best = 1
        val worst = threads * perThread

        repeat(6) { attempt ->
            V6NativeOptimizer.resetLiveBestForTest()
            assertNull("各試行はクリアな状態から始める", V6NativeOptimizer.liveBest)

            val barrier = CyclicBarrier(threads)
            val failure = AtomicReference<Throwable?>(null)
            val ts = (0 until threads).map { t ->
                Thread {
                    try {
                        barrier.await()
                        // 各スレッドが降順に publish する＝常に「勝てる」候補を出し続けて競合を最大化。
                        for (i in 0 until perThread) {
                            val h = worst - (i * threads + t)
                            V6NativeOptimizer.publishLiveBest(rep(h), board(h))
                        }
                    } catch (e: Throwable) {
                        failure.compareAndSet(null, e)
                    }
                }
            }
            ts.forEach { it.start() }
            ts.forEach { it.join() }
            failure.get()?.let { throw AssertionError("publish 中に例外 (attempt=$attempt)", it) }

            val b = V6NativeOptimizer.liveBest
                ?: throw AssertionError("publish したのに liveBest が null (attempt=$attempt)")
            // 盤面に埋め込んだ評価値が最良と一致していなければ、劣るスレッドが後から上書きしている。
            assertEquals("最終盤面が最良の評価に対応していない (attempt=$attempt)", best, b[0][0])
            for (row in b) for (v in row) {
                assertEquals("盤面の一部だけ別の publish 由来になっている (attempt=$attempt)", best, v)
            }
        }
    }

    /** 劣る評価では publish されない（既存の単調性）。 */
    @Test
    fun worseOrEqualPublishIsIgnored() {
        V6NativeOptimizer.resetLiveBestForTest()
        V6NativeOptimizer.publishLiveBest(rep(5), board(5))
        assertEquals(5, V6NativeOptimizer.liveBest!![0][0])

        V6NativeOptimizer.publishLiveBest(rep(9), board(9))   // 劣る
        assertEquals("劣る publish で退行してはいけない", 5, V6NativeOptimizer.liveBest!![0][0])

        V6NativeOptimizer.publishLiveBest(rep(5), board(7))   // 同値
        assertEquals("同値の publish でも差し替えない", 5, V6NativeOptimizer.liveBest!![0][0])

        V6NativeOptimizer.publishLiveBest(rep(2), board(2))   // 真に改善
        assertEquals(2, V6NativeOptimizer.liveBest!![0][0])
    }

    /** publish 後に呼出側が自分の盤面を書き換えても、publish 済みスナップショットは動かない。 */
    @Test
    fun publishedSnapshotIsIndependentOfTheCallersArray() {
        V6NativeOptimizer.resetLiveBestForTest()
        val mine = board(3)
        V6NativeOptimizer.publishLiveBest(rep(3), mine)
        mine[0][0] = 99
        assertEquals("publish はコピーであって参照共有ではない", 3, V6NativeOptimizer.liveBest!![0][0])
    }
}
