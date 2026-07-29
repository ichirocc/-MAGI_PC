package com.magi.app.v6

import com.magi.app.model.*
import org.junit.Assert.assertEquals
import org.junit.Test
import java.util.Random

/**
 * Differential test for the incremental [DeltaEvaluator] against the authoritative
 * [Evaluator.fullEval]. Mirrors the off-line randomized verification: after every move
 * (single and composite, applied/reverted through the public apply() API) the maintained
 * score must exactly equal a from-scratch full evaluation of the same assignment.
 *
 * Uses a hand-built MagiState (no JSON / no Android deps) chosen to exercise every
 * constraint family: c1 window, c2 per-staff total, c3/c3n/c3m/c3mn sequences,
 * c41 group-day range, c42 group pair, covU with use2Patterns (MIN=OR), staff ranges,
 * and wishes.
 */
class DeltaEvaluatorTest {

    private fun shift(name: String, kigou: String, n1: String, n2: String) = Shift(name, kigou, n1, n2)

    private fun buildState(): MagiState {
        // shifts: 0=休(no need), A,B,C have per-day needs (P1/P2 differ -> exercises MIN=OR)
        val shifts = listOf(
            shift("休", "休", "", ""),
            shift("A", "A", "1", "2"),
            shift("B", "B", "1", "1"),
            shift("C", "C", "1", ""),
        )
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"))
        // skillIdx (3rd arg) は groupIdx と独立: SK0={s0,s2,s4}, SK1={s1,s3}
        val staff = listOf(
            Staff("s0", 0, 0), Staff("s1", 0, 1), Staff("s2", 1, 0), Staff("s3", 1, 1), Staff("s4", 0, 0),
        )
        // groupShift: G0 -> {休,A,B}, G1 -> {休,B,C}
        val groupShift = listOf(
            listOf(1, 1, 1, 0),
            listOf(1, 0, 1, 1),
        )
        val days = 8
        val schedule = listOf(
            listOf(0, 1, 2, 0, 1, 0, 2, 1),
            listOf(1, 0, 0, 2, 1, 2, 0, 0),
            listOf(0, 2, 3, 0, 2, 0, 3, 2),
            listOf(3, 0, 2, 3, 0, 2, 0, 3),
            listOf(2, 1, 0, 1, 0, 1, 2, 0),
        )
        val wishes = mapOf("0,0" to 0, "1,4" to 1, "2,2" to 3)
        val staffRange = mapOf(
            "0,1" to Range("2", "4"),   // s0 shift A in [2,4]
            "1,2" to Range("", "3"),    // s1 shift B <= 3
            "3,3" to Range("1", ""),    // s3 shift C >= 1
        )
        val needDay1 = mapOf("1,0" to "2")   // override A need on day0
        val needDay2 = mapOf("2,5" to "2")
        val cons1 = listOf(C1Row("3", "A", "1"))           // every 3-day window: >=1 A
        val cons2 = listOf(C2Row("B", "2"))                // each staff: total B >= 2
        val cons3 = listOf(C3Row(listOf("A", "B")))        // want A then B
        val cons3n = listOf(C3Row(listOf("C", "C")))       // forbid C then C
        val cons3m = listOf(C3Row(listOf("B", "A")))       // want B then A
        val cons3mn = listOf(C3Row(listOf("A", "A")))      // discourage A then A
        val cons41 = listOf(C41Row("G0", "A", "", "2"))    // per day, G0 doing A <= 2
        val cons42 = listOf(C42Row("G0", "G1", "A", "C"))  // per day, G0-A vs G1-C conflict
        // [スキルグループ] skillIdx ベースの c41s/c42s（差分評価の検証対象）
        val skillGroups = listOf(Group("SK0", "SK0"), Group("SK1", "SK1"))
        val cons41s = listOf(C41Row("SK0", "A", "", "2"))    // per day, SK0 doing A <= 2
        val cons42s = listOf(C42Row("SK0", "SK1", "A", "C")) // per day, SK0-A vs SK1-C conflict

        return MagiState(
            startDate = "2025-01-01", endDate = "2025-01-08",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = true,
            // [監査補強] apt(適切回数)の Δ 差分パスを差分テストで実行するため非空目標を設定（従来は全空で未カバー）。
            //   G0(休/A/B可): A目標2・B目標1、G1(休/B/C可): B目標1・C目標2。staffRange でクランプされ apt Δ を発火させる。
            groupShift = groupShift, groupShiftApt = listOf(listOf("", "2", "1", ""), listOf("", "", "1", "2")), schedule = schedule,
            wishes = wishes, staffRange = staffRange, needDay1 = needDay1, needDay2 = needDay2,
            cons1 = cons1, cons2 = cons2, cons3 = cons3, cons3n = cons3n,
            cons3m = cons3m, cons3mn = cons3mn, cons41 = cons41, cons42 = cons42,
            skillGroups = skillGroups, cons41s = cons41s, cons42s = cons42s,
        )
    }

    @Test
    fun deltaMatchesFullEval_initial() {
        val p = Problem(buildState())
        val ev = Evaluator(p)
        val de = DeltaEvaluator(p)
        assertEquals(ev.fullEval(p.initialAssignment()), de.score())
    }

    /**
     * [3.318.0] groupViol（担当できないシフトに就いているセル）を評価器の HARD に含めた。
     * 既存の差分テストは移動先を `p.bucket[p.sgrp[i]]`（＝常に担当可）から選ぶため、この族の Δ を
     * 一度も通っていなかった。全シフトから選ぶ変種で Δ==フルを固定する。
     */
    @Test
    fun deltaMatchesFullEval_movesIntoDisallowedShifts() {
        val p = Problem(buildState())
        val ev = Evaluator(p)
        val de = DeltaEvaluator(p)
        val rng = Random(24680)
        var sawViolation = false
        repeat(20_000) {
            val i = rng.nextInt(p.S); val j = rng.nextInt(p.T)
            val old = de.at(i, j)
            val nw = rng.nextInt(p.K)                 // 担当可否を問わず選ぶ
            de.apply(i, j, nw)
            if (!p.canDo(i, nw)) sawViolation = true
            assertEquals("preview/commit mismatch", ev.fullEval(de.snapshot()), de.score())
            if (rng.nextBoolean()) {
                de.apply(i, j, old)
                assertEquals("revert mismatch", ev.fullEval(de.snapshot()), de.score())
            }
        }
        org.junit.Assert.assertTrue("担当外への移動を実際に踏んでいること", sawViolation)
    }

    /** [3.318.0] groupViol が評価器の hard（soft ではなく）に計上されること。 */
    @Test
    fun groupViolCountsAsHardInEvaluator() {
        val p = Problem(buildState())
        val ev = Evaluator(p)
        val base = p.initialAssignment()
        val basePartsBefore = ev.fullEvalParts(base)
        // s0 は G0（休/A/B 可）なので C(=index3) は担当外。1セルだけ C にすると hard が 1 増えるはず。
        val i = 0
        val j = (0 until p.T).first { base[0][it] != 3 }
        org.junit.Assert.assertTrue("前提: s0 は C を担当できない", !p.canDo(i, 3))
        val mutated = Array(p.S) { base[it].clone() }
        mutated[i][j] = 3
        val after = ev.fullEvalParts(mutated)
        assertEquals("groupViol は hard に +1", basePartsBefore[0] + 1, after[0] - (prefDelta(p, base, mutated, i, j)))
    }

    /** 上のテストで pref の変化分を切り分けるための補助（希望セルを踏んだ場合の hard 差を打ち消す）。 */
    private fun prefDelta(p: Problem, before: Array<IntArray>, after: Array<IntArray>, i: Int, j: Int): Long {
        val w = p.wish[i][j]
        if (w < 0 || !p.canDo(i, w)) return 0L
        val b = if (before[i][j] != w) 1L else 0L
        val a2 = if (after[i][j] != w) 1L else 0L
        return a2 - b
    }

    @Test
    fun deltaMatchesFullEval_singleMoves() {
        val p = Problem(buildState())
        val ev = Evaluator(p)
        val de = DeltaEvaluator(p)
        val rng = Random(12345)
        repeat(20_000) {
            val i = rng.nextInt(p.S); val j = rng.nextInt(p.T)
            val b = p.bucket[p.sgrp[i]]
            if (b.isEmpty()) return@repeat
            val old = de.at(i, j)
            val nw = b[rng.nextInt(b.size)]
            de.apply(i, j, nw)
            assertEquals("preview/commit mismatch", ev.fullEval(de.snapshot()), de.score())
            if (rng.nextBoolean()) {
                de.apply(i, j, old) // revert
                assertEquals("revert mismatch", ev.fullEval(de.snapshot()), de.score())
            }
        }
    }

    @Test
    fun deltaMatchesFullEval_compositeMovesWithRevert() {
        val p = Problem(buildState())
        val ev = Evaluator(p)
        val de = DeltaEvaluator(p)
        val rng = Random(999)
        repeat(4_000) {
            val before = de.score()
            val log = ArrayList<Triple<Int, Int, Int>>()
            val blk = 1 + rng.nextInt(6)
            repeat(blk) {
                val i = rng.nextInt(p.S); val j = rng.nextInt(p.T)
                val b = p.bucket[p.sgrp[i]]
                if (b.isEmpty()) return@repeat
                log.add(Triple(i, j, de.at(i, j)))
                de.apply(i, j, b[rng.nextInt(b.size)])
            }
            assertEquals("composite apply mismatch", ev.fullEval(de.snapshot()), de.score())
            if (rng.nextBoolean()) {
                for (k in log.indices.reversed()) {
                    val (i, j, old) = log[k]
                    de.apply(i, j, old)
                }
                assertEquals("composite revert score", before, de.score())
                assertEquals("composite revert full", ev.fullEval(de.snapshot()), de.score())
            }
        }
    }
}
