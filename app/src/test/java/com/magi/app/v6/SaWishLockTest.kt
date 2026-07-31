package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.334.0] SA/LAHC の近傍が**実現可能な希望の入ったセルを触らない**ことを固定する。
 *
 * 後処理研磨の全パスと C++ の修復オペレータは元から `wishLocked` を見ているのに、探索の近傍だけが
 * 見ていなかった。採点は元から正しい（pref は hard）ので誤った勤務表は出ないが、実測で
 * **手の 35〜36% がその却下される手**に費やされていた。
 *
 * `Problem.initialAssignment` は実現可能な希望を先に盤面へ入れるので、近傍が触らなければ
 * 出力でも希望どおりのまま残る＝この不変条件がそのまま「無駄打ちしていない」の証拠になる。
 */
class SaWishLockTest {
    /** 2職員×12日・シフト3種。希望は全部「担当できる」＝実現可能。 */
    private fun state(): MagiState = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-12",
        shifts = listOf(Shift("休", "休", "0", ""), Shift("A", "A", "1", ""), Shift("B", "B", "1", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1)),
        groupShiftApt = listOf(listOf("", "", "")),
        schedule = listOf(List(12) { 0 }, List(12) { 0 }),
        // 希望を散らす（連続していると opBlockFill の効果が見えない）。
        wishes = mapOf("0,1" to 1, "0,4" to 2, "0,9" to 1, "1,2" to 2, "1,6" to 1, "1,11" to 2),
        // 窓の要件を入れて opBlockFill を実際に走らせる。
        cons1 = listOf(C1Row("5", "休", "1")),
        cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        staffRange = mapOf("0,0" to Range("2", "6"), "1,0" to Range("2", "6")),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
    )

    @Test fun strongPerturbNeverMovesAFeasibleWish() {
        // [3.336.0/敵対レビュー H1] `strongPerturbFlat`（ネイティブ経路の停滞脱出）は 3.334.0 の
        //   ガードから漏れていた。
        //
        //   **間接テストでは検証できない**: ホスト JVM は `.so` を読めないので `NativeGate.usable` が
        //   false になり、唯一の呼出元 `runWorkerNative` が走らない。実際、最初に書いた
        //   「SaOptimizer.run を停滞させて呼ばせる」テストは**バグを戻しても通った**。直接呼ぶ。
        val st = state()
        val p = Problem(st)
        val ev = Evaluator(p, c3RunMode = true)
        val sa = SaOptimizer(p, ev)
        val init = p.initialAssignment()
        val flat = IntArray(p.S * p.T) { init[it / p.T][it % p.T] }
        for (seed in 1L..40L) {   // 手数は毎回4〜11なので、固定セルへ当たるまで回数を稼ぐ
            val cur = flat.copyOf()
            sa.strongPerturbFlat(cur, java.util.Random(seed))
            for (i in 0 until p.S) for (j in 0 until p.T) if (p.wishLocked(i, j)) {
                assertEquals("seed=$seed 強い揺さぶりが職員$i 日$j の希望を動かした",
                    p.wish[i][j], cur[i * p.T + j])
            }
        }
    }

    @Test fun searchNeverMovesACellThatHoldsAFeasibleWish() {
        val st = state()
        val p = Problem(st)
        val ev = Evaluator(p, c3RunMode = true)

        // 前提: 入口の盤面では実現可能な希望がすべて反映されている。
        val init = p.initialAssignment()
        var locked = 0
        for (i in 0 until p.S) for (j in 0 until p.T) if (p.wishLocked(i, j)) {
            locked++
            assertEquals("入口で希望が入っていない（この前提が崩れるとテストの意味が変わる）",
                p.wish[i][j], init[i][j])
        }
        assertTrue("希望固定セルが無いと何も検証できない", locked >= 6)

        // 複数シードで回す（近傍はランダムなので1回では触っていないだけかもしれない）。
        for (seed in 1L..6L) {
            val r = runBlocking {
                SaOptimizer(p, ev).run(SaParams(budgetMs = 400L, workers = 1, seed = seed))
            }
            for (i in 0 until p.S) for (j in 0 until p.T) if (p.wishLocked(i, j)) {
                assertEquals("seed=$seed で職員$i 日$j の希望が動いた（近傍が固定セルを触っている）",
                    p.wish[i][j], r.schedule[i][j])
            }
            val rep = UnifiedViolationChecker.check(st, r.schedule)
            assertEquals("seed=$seed で希望違反(pref)が残った", 0, rep.breakdown["pref"] ?: 0)
        }
    }
}
