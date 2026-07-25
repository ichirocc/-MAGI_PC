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

/** [3.275.0] C1RepairIndex（読取専用索引）の各ルックアップを、手計算で答えを設計した最小盤面で固定する。 */
class C1RepairIndexTest {

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

    @Test
    fun indexEnumeratesWindowsDaysGainAndDonorMargin() {
        // s0: X X Y Y  s1: Y Y X X, ルール「X 2日窓≥1」(day1=2,day2=1)。
        //   不足窓: s0[2,3]=X 0個・s1[0,1]=X 0個 の計2窓。
        val s = st(4, 2, listOf(listOf(1, 1, 2, 2), listOf(2, 2, 1, 1)), listOf(C1Row("2", "X", "1")))
        val p = Problem(s)
        val idx = C1RepairIndex.build(p, s.schedule.toIntArray2D())

        assertTrue(idx.hasActionable)
        assertEquals(2, idx.windows.size)
        assertEquals("不足合計=2", 2, idx.deficitTotal)

        // dayToWindows: s0窓は日2,3を含む / s1窓は日0,1を含む。
        assertEquals(1, idx.windowsCovering(0).size)
        assertTrue(idx.windowsCovering(0).all { it.staff == 1 })
        assertTrue(idx.windowsCovering(2).all { it.staff == 0 })
        assertTrue(idx.windowsCovering(3).all { it.staff == 0 })

        // staffRuleWindows: (staff, ruleIndex=0)
        assertEquals(1, idx.activeWindows(0, 0).size)
        assertEquals(1, idx.activeWindows(1, 0).size)

        // expectedGain: 不足窓のY候補日をXにすると各1窓解消。X既在日は候補でない=0。
        //   [3.279.0/C1-07] API は対象シフトを含む3引数へ（shift=1=X）。
        assertEquals(1, idx.expectedGain(0, 2, 1))
        assertEquals(1, idx.expectedGain(0, 3, 1))
        assertEquals(1, idx.expectedGain(1, 0, 1))
        assertEquals(0, idx.expectedGain(0, 0, 1))   // 日0は既にX=候補でない

        // donorMargin: s0 day0(X) は窓[0,1]のみ(z=2,余裕1)=安全。day1(X)は窓[1,2](z=1,余裕0)=危険。
        assertEquals(1, idx.donorMargin(0, 0))
        assertEquals(0, idx.donorMargin(0, 1))
        // Yセル(c1規則が依存しない)は無限大=常に安全。
        assertEquals(Int.MAX_VALUE, idx.donorMargin(0, 2))
    }

    @Test
    fun indexIsEmptyWhenNoDeficientWindow() {
        // 各日Xが存在し全窓充足=不足窓ゼロ。
        val s = st(3, 1, listOf(listOf(1, 1, 1)), listOf(C1Row("2", "X", "1")))
        val idx = C1RepairIndex.build(Problem(s), s.schedule.toIntArray2D())
        assertFalse(idx.hasActionable)
        assertEquals(0, idx.windows.size)
    }

    @Test
    fun expectedGainSeparatesTargetShifts() {
        // [3.279.0/C1-07] 同一職員・同一日に別シフトの不足窓が併存しても gain が混合されない。
        //   盤面 [休,休]・ルール「X 2日窓≥1」(1手で解消=gain1) と「Y 2日窓≥2」(1手では解消しない=gain0)。
        //   旧 API(staff,day) は max 混合で両問い合わせとも 1 になり判別不能だった。
        val s = st(2, 1, listOf(listOf(0, 0)), listOf(C1Row("2", "X", "1"), C1Row("2", "Y", "2")))
        val idx = C1RepairIndex.build(Problem(s), s.schedule.toIntArray2D())
        assertEquals("Xへの1手は窓を解消=gain1", 1, idx.expectedGain(0, 0, 1))
        assertEquals("Yは必要2で1手では解消しない=gain0", 0, idx.expectedGain(0, 0, 2))
    }
}
