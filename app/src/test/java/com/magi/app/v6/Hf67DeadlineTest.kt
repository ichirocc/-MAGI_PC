package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.282.0/新領域ログ監査] applyHF67InterStaffSwap の専用締切（deadlineMs）。
 * 兄弟の HF66 は 2.65.0 以来の専用上限＋内側 outOfTime 確認を持つのに、HF67 だけ手ごとの
 * shouldStop のみで、候補ごとフル check の内側スキャンとフォールバック総当たり
 * （実機ログ rollback=264 の正体）が締切後も走り切る非対称だった。その是正を固定する。
 */
class Hf67DeadlineTest {

    private fun st(): MagiState = MagiState(
        startDate = "2026-06-01", endDate = "2026-06-04",
        shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", "", ""), Shift("B", "B", "", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1)),
        groupShiftApt = listOf(listOf("", "", "")),
        // s0 は A 過多(下限1上限1に対し3)・s1 は A 不足(0<1) ＝ 主スキャンに実際の低/高ペアがある盤面
        schedule = listOf(listOf(1, 1, 1, 0), listOf(0, 0, 0, 2)),
        wishes = emptyMap(),
        staffRange = mapOf("0,1" to Range("1", "1"), "1,1" to Range("1", "1")),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test
    fun expiredDeadlineReturnsInputUnchangedWithoutScanning() {
        val s = st()
        val sched = s.schedule.map { it.toIntArray() }.toTypedArray()
        // 締切が既に過ぎている → 主スキャンもフォールバック総当たりも走らず即 return（keep-best＝入力維持）。
        val r = V6HotfixPasses.applyHF67InterStaffSwap(s, sched, maxSwaps = 30, deadlineMs = 0L)
        assertEquals("締切超過では1手も適用しない", 0, r.swapsApplied)
        assertEquals("フォールバック総当たりも起動しない（rollback=0）", 0, r.swapsRollback)
        assertTrue("盤面は入力(正規化後)と同一",
            r.newSchedule.contentDeepEquals(normalizeSchedule(sched, Problem(s))))
    }

    @Test
    fun defaultDeadlineKeepsLegacyBehavior() {
        val s = st()
        val sched = s.schedule.map { it.toIntArray() }.toTypedArray()
        val before = UnifiedViolationChecker.check(s, sched)
        // 既定(deadline=Long.MAX_VALUE)は従来どおり動く＝low/high ペアの改善スワップを見つけられる。
        val r = V6HotfixPasses.applyHF67InterStaffSwap(s, sched, maxSwaps = 30)
        val after = UnifiedViolationChecker.check(s, r.newSchedule)
        assertTrue("従来経路は退化しない", after.total <= before.total)
        assertTrue("この盤面では改善スワップが実際に見つかる", r.swapsApplied > 0)
    }
}
