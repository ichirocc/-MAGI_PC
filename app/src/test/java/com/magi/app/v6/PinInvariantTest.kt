package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.C2Row
import com.magi.app.model.C3Row
import com.magi.app.model.C41Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import com.magi.app.model.StateParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Random

/**
 * [3.338.0/敵対レビュー A2] **ピンの不変条件を「読んで確認した」から「試験が主張する」へ上げる**。
 *
 * この最適化器は2種類のピンを守ると宣言している。
 *  1. **実現可能な希望**（`Problem.wishLocked` = 希望があり、かつ担当できる）
 *     … 入口の `initialAssignment`/`hf67HardRepair` が盤面へ載せ、以後どの手も触らない
 *  2. **厳密な回数固定**（`staffRange` の lo==hi）… `exactPinRegression` が目標から遠ざかる手を却下する
 *
 * どちらも各パスがローカルに `movable` / `exactPinRegression` を呼ぶ形で守られており、
 * **1箇所書き忘れても誰も気づけない**（3.334.0 の SA 近傍・3.336.0 の `strongPerturbFlat` が実例）。
 * ここで後処理チェーン全体を通した不変条件として固定する。
 *
 * 実データでの確認（3.338.0 時点）: golden/real/user とも固定 84/81/81 件で**動かされた 0 件**。
 *
 * **この試験が捕まえる範囲（実測して確かめた）**:
 *  - 14箇所の `movable` を**全部**潰す → 落ちる ✓
 *  - `exactPinRegression` を無効化する → 落ちる ✓
 *  - `movable` を**1箇所だけ**潰す → **落ちない**。希望を破ると pref(9000/HARD) が増え、
 *    そのパスの `isBetter` が必ず却下するため。つまり不変条件を最終的に強制しているのは**採否**で、
 *    `movable` は「必ず却下される手を作らない」ための事前フィルタ。3.334.0 の SA 近傍の欠落が
 *    「誤った勤務表」でなく「反復の空振り」で済んだのは同じ理由。
 *  → よってこの試験は**不変条件**を守るが、**個々のガードの有無**は守らない。ガードの網羅は
 *    grep で数える（教訓#31）。
 */
class PinInvariantTest {

    private fun lockedCells(p: Problem): List<Pair<Int, Int>> =
        (0 until p.S).flatMap { i -> (0 until p.T).map { j -> i to j } }
            .filter { (i, j) -> p.wishLocked(i, j) }

    /** 厳密ピン(lo==hi)のうち、入口で目標どおりだったものを列挙する。 */
    private fun exactPins(p: Problem, board: Array<IntArray>): List<Triple<Int, Int, Int>> {
        val out = ArrayList<Triple<Int, Int, Int>>()
        for (i in 0 until p.S) {
            val cnt = IntArray(p.K)
            for (j in 0 until p.T) board[i][j].let { if (it in 0 until p.K) cnt[it]++ }
            for (k in 0 until p.K) {
                val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]
                if (lo != Int.MIN_VALUE && hi != Int.MAX_VALUE && lo == hi && cnt[k] == lo) {
                    out.add(Triple(i, k, lo))
                }
            }
        }
        return out
    }

    private fun assertPinsHeld(label: String, st: MagiState) {
        val p = Problem(st)
        val start = p.initialAssignment()   // 実現可能な希望を載せた入口盤面
        val pins = exactPins(p, start)
        val r = V6HotfixPasses.runPostOptimization(st, start.copy2D(), "p", seed = 12345L)

        for ((i, j) in lockedCells(p)) {
            assertEquals("$label: 実現可能な希望（職員$i 日$j）が後処理で動いた",
                p.wish[i][j], r.schedule[i][j])
        }
        // 厳密ピンは「入口で満たしていたものを外さない」＝exactPinRegression の契約。
        for ((i, k, target) in pins) {
            var cnt = 0
            for (j in 0 until p.T) if (r.schedule[i][j] == k) cnt++
            assertEquals("$label: 厳密な回数固定（職員$i シフト$k = $target 回）が後処理で崩れた",
                target, cnt)
        }
    }

    @Test fun postOptimizationHoldsPinsOnRealProductionState() {
        val json = javaClass.getResourceAsStream("/golden_state.json")
            ?.bufferedReader()?.use { it.readText() }
            ?: error("golden_state.json not found on the test classpath")
        val st = StateParser.parse(json)
        val p = Problem(st)
        assertTrue("固定セルが無いと何も検証できない", lockedCells(p).isNotEmpty())
        assertPinsHeld("golden_state", st)
    }

    /** 研磨パスが実際に走るよう、違反を多めに含む小さな状態を作る。 */
    private fun busyState(rng: Random, s: Int, t: Int, k: Int): MagiState {
        val groups = listOf(Group("G", "G"))
        val staff = (0 until s).map { Staff("S$it", 0) }
        val shifts = listOf(Shift("休", "休", "0", "")) + (1 until k).map {
            Shift("S$it", "S$it", "1", (1 + rng.nextInt(2)).toString())
        }
        val staffRange = HashMap<String, Range>()
        for (i in 0 until s) {
            // 一部の職員に厳密ピン（lo==hi）を置く＝exactPinRegression の対象を必ず作る。
            if (rng.nextInt(2) == 0) {
                val n = 2 + rng.nextInt(3)
                staffRange["$i,0"] = Range(n.toString(), n.toString())
            }
            for (x in 1 until k) if (rng.nextInt(3) == 0) {
                staffRange["$i,$x"] = Range("1", (2 + rng.nextInt(3)).toString())
            }
        }
        val wishes = HashMap<String, Int>()
        repeat(s * t / 5) { wishes["${rng.nextInt(s)},${rng.nextInt(t)}"] = rng.nextInt(k) }
        fun sym(x: Int) = if (x == 0) "休" else "S$x"
        return MagiState(
            startDate = "2026-05-01", endDate = "2026-12-28",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = true,
            groupShift = listOf(List(k) { 1 }),
            groupShiftApt = listOf((0 until k).map { if (rng.nextInt(2) == 0) (1 + rng.nextInt(4)).toString() else "" }),
            schedule = List(s) { List(t) { rng.nextInt(k) } },
            wishes = wishes, staffRange = staffRange,
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row("4", "休", "1")),
            cons2 = listOf(C2Row(sym(1), "2")),
            cons3 = listOf(C3Row(listOf(sym(1), sym(1)))),
            cons3n = listOf(C3Row(listOf(sym(1), sym(if (k > 2) 2 else 0)))),
            cons3m = listOf(C3Row(listOf("休", sym(1)))),
            cons3mn = listOf(C3Row(listOf("休", "休"))),
            cons41 = listOf(C41Row("G", sym(1), "1", "2")),
            cons42 = emptyList(),
        )
    }

    @Test fun postOptimizationHoldsPinsAcrossRandomStates() {
        val rng = Random(0x91A7L)
        var totalLocked = 0
        var totalExact = 0
        repeat(12) { iter ->
            val st = busyState(rng, s = 4 + rng.nextInt(3), t = 10 + rng.nextInt(8), k = 3 + rng.nextInt(2))
            val p = Problem(st)
            totalLocked += lockedCells(p).size
            totalExact += exactPins(p, p.initialAssignment()).size
            assertPinsHeld("random#$iter", st)
        }
        // 対象が無いまま緑になっていないことを数字で見せる。
        assertTrue("希望固定セルが一度も現れていない（試験が何も守っていない）", totalLocked > 20)
        assertTrue("厳密な回数固定が一度も現れていない（試験が何も守っていない）", totalExact > 3)
    }
}
