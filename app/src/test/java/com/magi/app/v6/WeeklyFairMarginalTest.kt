package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import java.util.Random
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.267.0/weekly+fair統合] destroyRepairDayAt/destroyRepairStaffAt/destroyRepairViolations の
 * marginal cost(staffCountPenaltyAt)は low/high/apt にしか対応しておらず、3.170.0で「weekly=曜日
 * バケット・fairは群平均が必要で、対応するにはweeklyDevOfBucket/fairDevAt相当の統合が要る、より
 * 大きな改修」として未対応のまま残っていた（focus選択には追加済みだがcost計算は素通り）。
 * `weeklyMarginalAt`/`fairMarginalAt` を新設しこの穴を埋めた。本テストは、両関数が返す値が
 * 実際のUnifiedViolationChecker(weightedScore)の差分と厳密に一致することを、cons1/cons3/pref/
 * groupViol/covU/covOを全て起こりえない構成（low/high/apt/fair/weeklyのみ寄与）で検証する。
 */
class WeeklyFairMarginalTest {

    private fun randomState(rng: Random, s: Int, t: Int, k: Int, groupCount: Int): MagiState {
        val groups = (0 until groupCount).map { Group("G$it", "G$it") }
        val staff = (0 until s).map { Staff("S$it", rng.nextInt(groupCount)) }
        val groupShift = (0 until groupCount).map { List(k) { 1 } }
        val groupShiftApt = (0 until groupCount).map {
            (0 until k).map { if (rng.nextInt(3) == 0) rng.nextInt(t + 1).toString() else "" }
        }
        val schedule = List(s) { List(t) { rng.nextInt(k) } }
        val staffRange = HashMap<String, Range>()
        for (i in 0 until s) for (shiftIdx in 1 until k) {
            if (rng.nextInt(2) != 0) continue
            val lo = if (rng.nextInt(2) == 0) rng.nextInt(t / 2 + 1).toString() else ""
            val hi = if (rng.nextInt(2) == 0) (rng.nextInt(t / 2 + 1) + t / 4).toString() else ""
            if (lo.isNotEmpty() || hi.isNotEmpty()) staffRange["$i,$shiftIdx"] = Range(lo = lo, hi = hi)
        }
        return MagiState(
            startDate = "2026-0${1 + rng.nextInt(9)}-0${1 + rng.nextInt(9)}",
            endDate = "2026-12-28",
            shifts = listOf(Shift("休", "休", "", "")) + (1 until k).map { Shift("S$it", "S$it", "", "") },
            groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = groupShiftApt, schedule = schedule,
            wishes = emptyMap(), staffRange = staffRange, needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    private fun countPenalty(p: Problem, i: Int, shiftIdx: Int, n: Int): Long {
        var pen = 0L
        val lo = p.rangeLo[i][shiftIdx]; val hi = p.rangeHi[i][shiftIdx]
        if (lo != Int.MIN_VALUE && lo != 0 && n < lo) pen += (lo - n).toLong() * 90L
        if (hi != Int.MAX_VALUE && n > hi) pen += (n - hi).toLong() * 45L
        val tgt = p.apt[i][shiftIdx]
        if (tgt >= 0) pen += kotlin.math.abs(n - tgt).toLong()
        return pen
    }

    @Test
    fun marginalDeltaMatchesFullCheckerAcrossRandomStates() {
        val rng = Random(0x9F3267L)
        var checked = 0
        repeat(80) { case ->
            val s = 2 + rng.nextInt(8)
            val t = 5 + rng.nextInt(20)
            val k = 2 + rng.nextInt(3)
            val groupCount = 1 + rng.nextInt(2)
            val state = randomState(rng, s, t, k, groupCount)
            val board = state.schedule.toIntArray2D()
            val p = cachedProblem(state)
            val rest = 0

            repeat(5) {
                val i = rng.nextInt(s)
                val j = rng.nextInt(t)
                val old = board[i][j]
                val newK = rng.nextInt(k)
                if (newK == old) return@repeat
                checked++

                val before = UnifiedViolationChecker.check(state, board)
                val after = UnifiedViolationChecker.check(state, board.copy2D().also { it[i][j] = newK })
                val realDelta = after.weightedScore - before.weightedScore

                val cntI = IntArray(k)
                for (jj in 0 until t) { val kk = board[i][jj]; if (kk in 0 until k) cntI[kk]++ }
                val dOld = countPenalty(p, i, old, cntI[old] - 1).toDouble() - countPenalty(p, i, old, cntI[old]).toDouble()
                val dNew = countPenalty(p, i, newK, cntI[newK] + 1).toDouble() - countPenalty(p, i, newK, cntI[newK]).toDouble()

                // [3.345.0] weekly は職員×シフト×曜日。old→newK のシフト移動として marginal を取る。
                val wd = Array(k) { IntArray(7) }
                for (jj in 0 until t) { val kk = board[i][jj]; if (kk in 0 until k) wd[kk][(p.dow0 + jj) % 7]++ }
                val bucket = (p.dow0 + j) % 7
                val dWeekly = V6NativeOptimizer.weeklyMarginalAt(wd, bucket, old, newK).toDouble()

                val counts = Array(s) { ss -> IntArray(k).also { a -> for (jj in 0 until t) { val kk = board[ss][jj]; if (kk in 0 until k) a[kk]++ } } }
                val grpTotal = Array(p.G) { IntArray(k) }
                for (ss in 0 until s) for (kk in 0 until k) grpTotal[p.sgrp[ss]][kk] += counts[ss][kk]
                val dFair = V6NativeOptimizer.fairMarginalAt(p, i, old, -1, counts, grpTotal).toDouble() +
                    V6NativeOptimizer.fairMarginalAt(p, i, newK, 1, counts, grpTotal).toDouble()

                val myDelta = dOld + dNew + dWeekly + dFair
                assertEquals(
                    "case=$case i=$i j=$j old=$old new=$newK breakdown_before=${before.breakdown} breakdown_after=${after.breakdown}",
                    realDelta, myDelta, 1e-6,
                )
            }
        }
        assertTrue("十分な件数を検証したこと", checked > 100)
    }

    @Test
    fun weeklyMarginalAtIsZeroWhenTheShiftDoesNotChange() {
        // [3.345.0] 同じシフトへの「移動」は盤面が動かない＝寄与ゼロ。
        val wd = Array(3) { intArrayOf(1, 1, 1, 1, 1, 1, 1) }
        assertEquals(0L, V6NativeOptimizer.weeklyMarginalAt(wd, 3, 1, 1))
    }

    @Test
    fun weeklyMarginalAtMovesBothBucketsOfTheOldAndNewShift() {
        // [3.345.0] シフト0が月曜(bucket0)に偏り、シフト1は均等。月曜の1日を 0→1 へ移すと
        //   偏っている側は均され、均等な側は崩れる。両バケットの寄与を足したものが返る。
        val wd = arrayOf(
            intArrayOf(3, 0, 0, 0, 0, 0, 0),   // 計3・目標 round(3/7)=0 → dev=3
            intArrayOf(1, 1, 1, 1, 1, 1, 1),   // 計7・目標 1 → dev=0
        )
        // 0側: 計2・目標0 → dev=2（-1）／1側: 計8・目標 round(8/7)=1 → 月曜2で dev=1（+1）
        assertEquals(0L, V6NativeOptimizer.weeklyMarginalAt(wd, 0, 0, 1))
        // 逆向き（1→0）: 0側は月曜4で計4・目標1 → dev=3+... を実測でなく式で確かめるのは別テストの役目。
        // ここでは「呼んでも wd を書き換えない」ことだけ固定する。
        assertEquals(3, wd[0][0]); assertEquals(1, wd[1][0])
    }

    @Test
    fun fairMarginalAtIsZeroWhenGroupHasFewerThanTwoMembers() {
        // 3 staff, each in their own singleton group (m=1 per group) -> fair must never fire.
        val state = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-06",
            shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", "", ""), Shift("B", "B", "", "")),
            groups = listOf(Group("G0", "G0"), Group("G1", "G1"), Group("G2", "G2")),
            staff = listOf(Staff("S0", 0), Staff("S1", 1), Staff("S2", 2)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1), listOf(1, 1, 1), listOf(1, 1, 1)),
            groupShiftApt = listOf(listOf("", "", ""), listOf("", "", ""), listOf("", "", "")),
            schedule = listOf(listOf(1, 1, 1, 1, 1, 1), listOf(2, 2, 2, 2, 2, 2), listOf(0, 0, 0, 0, 0, 0)),
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val p = cachedProblem(state)
        val counts = Array(3) { IntArray(3) }
        val grpTotal = Array(p.G) { IntArray(3) }
        assertEquals(0L, V6NativeOptimizer.fairMarginalAt(p, 0, 1, 1, counts, grpTotal))
    }
}
