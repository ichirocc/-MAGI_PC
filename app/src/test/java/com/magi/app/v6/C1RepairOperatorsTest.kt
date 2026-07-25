package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.275.0] C1RepairOperators（façade）が既存実装へ **1:1 委譲**（挙動完全同一）であることと、
 * hasActionableC1 ゲートが c1不足ゼロ盤面で **provably-safe**（applyC1WindowPolish が no-op）であることを固定。
 */
class C1RepairOperatorsTest {

    private fun st(days: Int, staff: Int, sched: List<List<Int>>, cons1: List<C1Row>): MagiState {
        val end = "2026-01-" + days.toString().padStart(2, '0')
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", ""))
        return MagiState(
            startDate = "2026-01-01", endDate = end,
            shifts = shifts, groups = listOf(Group("G", "G")),
            staff = List(staff) { Staff("s$it", 0) }, use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)), groupShiftApt = listOf(listOf("", "", "")),
            schedule = sched, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = cons1, cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    private fun same(a: Array<IntArray>, b: Array<IntArray>): Boolean =
        a.size == b.size && a.indices.all { a[it].contentEquals(b[it]) }

    // c1不足を含む盤面（各オペレータが実際に手を試す）。
    private fun deficientState() =
        st(4, 2, listOf(listOf(1, 1, 2, 2), listOf(2, 2, 1, 1)), listOf(C1Row("2", "X", "1")))

    @Test
    fun windowPolishDelegatesIdentically() {
        val s = deficientState(); val sc = s.schedule.toIntArray2D()
        val direct = V6HotfixPasses.applyC1WindowPolish(s, sc, maxPasses = 3, seed = 0x1C1L)
        val viaFacade = C1RepairOperators.selfRelocateAndSameDaySwap(s, s.schedule.toIntArray2D(), maxPasses = 3, seed = 0x1C1L)
        assertTrue(same(direct.newSchedule, viaFacade.newSchedule))
        assertEquals(direct.applied, viaFacade.applied)
        assertEquals(direct.afterTotal, viaFacade.afterTotal)
    }

    @Test
    fun temporalFlowDelegatesIdentically() {
        val s = deficientState()
        val direct = C1TemporalFlowPolish.apply(s, s.schedule.toIntArray2D(), seed = 0xC1F10FL)
        val viaFacade = C1RepairOperators.temporalFlow(s, s.schedule.toIntArray2D(), seed = 0xC1F10FL)
        assertTrue(same(direct.newSchedule, viaFacade.newSchedule))
        assertEquals(direct.afterTotal, viaFacade.afterTotal)
    }

    @Test
    fun wideBeamDelegatesIdentically() {
        val s = deficientState()
        val direct = V6HotfixPasses.applyC1BeamPolish(s, s.schedule.toIntArray2D(), seed = 0x1CBEAL)
        val viaFacade = C1RepairOperators.wideBeam(s, s.schedule.toIntArray2D(), seed = 0x1CBEAL)
        assertTrue(same(direct.newSchedule, viaFacade.newSchedule))
        assertEquals(direct.afterTotal, viaFacade.afterTotal)
    }

    @Test
    fun exactWindowDelegatesIdentically() {
        val s = deficientState()
        val direct = V6HotfixPasses.applyC1ExactWindowRepair(s, s.schedule.toIntArray2D())
        val viaFacade = C1RepairOperators.exactWindow(s, s.schedule.toIntArray2D())
        assertTrue(same(direct.newSchedule, viaFacade.newSchedule))
        assertEquals(direct.afterTotal, viaFacade.afterTotal)
    }

    @Test
    fun jointLnsDelegatesIdentically() {
        val s = deficientState()
        val direct = C1JointLnsPolish.apply(s, s.schedule.toIntArray2D())
        val viaFacade = C1RepairOperators.jointLns(s, s.schedule.toIntArray2D())
        assertTrue(same(direct.newSchedule, viaFacade.newSchedule))
        assertEquals(direct.afterTotal, viaFacade.afterTotal)
    }

    // ---- 3.276.0: index駆動C1修復オペレータ ----

    @Test
    fun indexChainRepairReducesC1ViaDirectMove() {
        // s0=[Y,Y], ルール「X 2日窓≥1」, 被覆要件なし → 直接移動 Y→X で c1 1→0（他族悪化なし）。
        val s = st(2, 1, listOf(listOf(2, 2)), listOf(C1Row("2", "X", "1")))
        val sc = s.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(s, sc)
        assertEquals(1, before.breakdown["c1"])
        val res = V6HotfixPasses.applyC1IndexChainRepair(s, sc)
        val after = UnifiedViolationChecker.check(s, res.newSchedule)
        assertEquals("c1解消", 0, after.breakdown["c1"])
        assertTrue("HARD非悪化", after.hard <= before.hard)
        assertTrue("total非悪化", after.total <= before.total)
        assertTrue("入力配列は不変", same(sc, s.schedule.toIntArray2D()))
    }

    @Test
    fun indexChainRepairFillsHoleViaChain() {
        // 被覆要件 X=1・Y=1/日。s0=[Y,Y]・s1=[X,X]。ルール「X 2日窓≥1」で s0 が不足。
        //   直接移動 s0:Y→X は Y@day0 に covU 穴（Y需要1）を作り却下 → findCovUChain が s1:X→Y で埋め直し採用。
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "1", ""), Shift("Y", "Y", "1", ""))
        val s = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-02",
            shifts = shifts, groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)), use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)), groupShiftApt = listOf(listOf("", "", "")),
            schedule = listOf(listOf(2, 2), listOf(1, 1)), wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row("2", "X", "1")), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val sc = s.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(s, sc)
        assertTrue("s0がc1不足", (before.breakdown["c1"] ?: 0) >= 1)
        val res = V6HotfixPasses.applyC1IndexChainRepair(s, sc)
        val after = UnifiedViolationChecker.check(s, res.newSchedule)
        assertTrue("c1改善", (after.breakdown["c1"] ?: 0) < (before.breakdown["c1"] ?: 0))
        assertTrue("HARD非悪化(covU穴を連鎖で埋めた)", after.hard <= before.hard)
        assertTrue("採用あり", res.applied >= 1)
    }

    @Test
    fun indexChainRepairIsNoOpWhenNoC1() {
        val s = st(3, 1, listOf(listOf(1, 1, 1)), listOf(C1Row("2", "X", "1")))
        val res = V6HotfixPasses.applyC1IndexChainRepair(s, s.schedule.toIntArray2D())
        assertEquals(0, res.applied)
    }

    @Test
    fun indexChainRepairDelegatesIdentically() {
        val s = deficientState()
        val direct = V6HotfixPasses.applyC1IndexChainRepair(s, s.schedule.toIntArray2D(), seed = 0x1C1D2L)
        val viaFacade = C1RepairOperators.indexChainRepair(s, s.schedule.toIntArray2D(), seed = 0x1C1D2L)
        assertTrue(same(direct.newSchedule, viaFacade.newSchedule))
        assertEquals(direct.applied, viaFacade.applied)
        assertEquals(direct.afterTotal, viaFacade.afterTotal)
    }

    @Test
    fun gateIsSafeOnC1CleanBoard() {
        // c1不足ゼロ盤面: hasActionableC1=false かつ applyC1WindowPolish は正真正銘の no-op。
        val s = st(3, 1, listOf(listOf(1, 1, 1)), listOf(C1Row("2", "X", "1")))
        val p = Problem(s); val sc = s.schedule.toIntArray2D()
        assertFalse(C1RepairOperators.hasActionableC1(p, sc))
        val res = V6HotfixPasses.applyC1WindowPolish(s, sc, maxPasses = 3)
        assertEquals("gateがskipする窓研磨は採用0", 0, res.applied)
        assertTrue("盤面は不変(normalize済み入力と一致)", same(res.newSchedule, normalizeSchedule(sc, p)))
    }
}
