package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.C3Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.275.0] C1DeltaPrefilter の **accept非変更** 性質を固定する。
 * HARD_REJECT は「checker+isBetter が確実に却下する候補」＝早期スキップしても採用結果は不変、を意味する。
 */
class C1DeltaPrefilterTest {

    private val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", ""))

    /** 単一群（全シフト担当可）。 */
    private fun single(
        days: Int, staff: Int, sched: List<List<Int>>,
        cons1: List<C1Row> = emptyList(), cons3n: List<C3Row> = emptyList(),
        wishes: Map<String, Int> = emptyMap(),
    ) = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-" + days.toString().padStart(2, '0'),
        shifts = shifts, groups = listOf(Group("G", "G")),
        staff = List(staff) { Staff("s$it", 0) }, use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1)), groupShiftApt = listOf(listOf("", "", "")),
        schedule = sched, wishes = wishes, staffRange = emptyMap(),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = cons1, cons2 = emptyList(), cons3 = emptyList(),
        cons3n = cons3n, cons3m = emptyList(), cons3mn = emptyList(),
        cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test
    fun hasActionableReflectsDeficientWindows() {
        val deficient = single(3, 1, listOf(listOf(0, 0, 0)), cons1 = listOf(C1Row("2", "X", "1")))
        assertTrue(C1DeltaPrefilter.hasActionableC1(C1RepairIndex.build(Problem(deficient), deficient.schedule.toIntArray2D())))
        val clean = single(3, 1, listOf(listOf(1, 1, 1)), cons1 = listOf(C1Row("2", "X", "1")))
        assertFalse(C1DeltaPrefilter.hasActionableC1(C1RepairIndex.build(Problem(clean), clean.schedule.toIntArray2D())))
    }

    @Test
    fun screenCellRejectsNoOp() {
        val s = single(2, 1, listOf(listOf(1, 0)))
        val p = Problem(s); val sc = s.schedule.toIntArray2D()
        assertEquals(C1DeltaPrefilter.Verdict.HARD_REJECT, C1DeltaPrefilter.screenCell(p, sc, 0, 0, 1)) // 既にX
    }

    @Test
    fun screenCellRejectsNonCanDo() {
        // 2群: g0={休,X}, g1={休,Y}。s0(g0)はYを担当不可。
        val s = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-02",
            shifts = shifts, groups = listOf(Group("G0", "G0"), Group("G1", "G1")),
            staff = listOf(Staff("s0", 0), Staff("s1", 1)), use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 0), listOf(1, 0, 1)), groupShiftApt = listOf(listOf("", "", ""), listOf("", "", "")),
            schedule = listOf(listOf(1, 0), listOf(2, 0)), wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val p = Problem(s); val sc = s.schedule.toIntArray2D()
        assertEquals(C1DeltaPrefilter.Verdict.HARD_REJECT, C1DeltaPrefilter.screenCell(p, sc, 0, 1, 2)) // s0→Y=担当外
    }

    @Test
    fun screenCellRejectsBreakingWishButAllowsSatisfyingIt() {
        // (0,0) の希望=X。盤面は休(=希望未充足)。
        val s = single(2, 1, listOf(listOf(0, 0)), wishes = mapOf("0,0" to 1))
        val p = Problem(s); val sc = s.schedule.toIntArray2D()
        // 希望Xを破って Y にする候補 → pref(HARD)悪化 → 却下。
        assertEquals(C1DeltaPrefilter.Verdict.HARD_REJECT, C1DeltaPrefilter.screenCell(p, sc, 0, 0, 2))
        // 希望X自体へ寄せる候補 → 却下しない（改善し得る＝checkerに委ねる）。
        assertEquals(C1DeltaPrefilter.Verdict.NEUTRAL, C1DeltaPrefilter.screenCell(p, sc, 0, 0, 1))
    }

    @Test
    fun screenCellRejectsForbiddenRun() {
        // cons3n=[X,X]。(0,0)=X の隣 (0,1) を X にすると禁止連続。
        val s = single(2, 1, listOf(listOf(1, 0)), cons3n = listOf(C3Row(listOf("X", "X"))))
        val p = Problem(s); val sc = s.schedule.toIntArray2D()
        assertEquals(C1DeltaPrefilter.Verdict.HARD_REJECT, C1DeltaPrefilter.screenCell(p, sc, 0, 1, 1))
    }

    @Test
    fun screenCellNeutralForSafeCandidate() {
        // 休→Y は無変化でない・担当可・希望なし・禁止連続なし → NEUTRAL（checkerに委ねる）。
        val s = single(2, 1, listOf(listOf(0, 0)))
        val p = Problem(s); val sc = s.schedule.toIntArray2D()
        assertEquals(C1DeltaPrefilter.Verdict.NEUTRAL, C1DeltaPrefilter.screenCell(p, sc, 0, 1, 2))
    }
}
