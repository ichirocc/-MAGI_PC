package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class C1TemporalDpPolishTest {
    @Test
    fun exactDpCrossesTwoSwapLocalMinimumAndPreservesCount() {
        // T=11, D=5,N=2。X={0,1,5,6}はc1=1。
        // 全1回swapはc1を減らせないが、0→2 と 5→7 の2同時移設でc1=0になる。
        val row = intArrayOf(1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0)
        val rules = listOf(C1TemporalDp.Rule(days = 5, minimum = 2))
        val before = C1TemporalDp.countFires(row, 1, rules)
        assertEquals(1, before)

        val out = C1TemporalDp.solve(
            row = row,
            targetShift = 1,
            rules = rules,
            locked = BooleanArray(row.size),
            maxRelocations = 4,
            seed = 7L,
        )
        assertNotNull(out)
        requireNotNull(out)
        assertEquals(0, out.fires)
        assertEquals(2, out.relocations)
        assertEquals(4, out.changedCells)
        assertEquals("X月間回数を保存", row.count { it == 1 }, out.targetDays.count { it })
    }

    @Test
    fun exactDpNeverChangesLockedTargetOrNonTargetDay() {
        // [CI失敗の修正] 日0(X)・日10(非X)を固定すると、窓[1-5]と窓[6-10]が互いに素な区間で
        // それぞれ独立に2件以上要求するため、日0固定分(1)と合わせて必要X数≥5だが月間回数保存で
        // 4のまま＝数学的に不可能（鳩の巣原理）。日1(X, 解=0→2/5→7で不変)・日9(非X, 同解で不変)を
        // 固定に差し替え、既知の実行可能解(X→{1,2,6,7})と両立することを確認済みの構成にする。
        val row = intArrayOf(1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0)
        val locked = BooleanArray(row.size)
        locked[1] = true   // X固定
        locked[9] = true   // 非X固定
        val out = C1TemporalDp.solve(
            row, 1, listOf(C1TemporalDp.Rule(5, 2)), locked,
            maxRelocations = 4, seed = 11L,
        )
        assertNotNull(out)
        requireNotNull(out)
        assertTrue(out.targetDays[1])
        assertFalse(out.targetDays[9])
    }

    // [3.254.0/C1TemporalFlowPolish, C1TemporalSwapPolish置換] 旧C1TemporalSwapPolishは変更日ごとに
    // 「厳密に相補的なシフトを持つ1人との同日swap」でしかDPの目標パターンを実現できず、そのような
    // 相手が居ない日では改善が丸ごと死んでいた（実データ検証=golden_state.jsonで寄与0%確認）。
    // この盤面は day0 に「相手がYへ渡せる余剰を持たない」よう partner を意図的に非対称化し
    // （partnerのday0はY=targetと同じでswap不成立）、旧実装なら day0 の改善だけが失敗する構成にする。
    // 被覆制約(needDay)を一切設定しないため、target が1人だけ自由に動いても構造的に無害
    // （FlexibleDayFlowは強制的な2人swapでなく、費用最小の任意人数再割当を解く）。
    private fun asymmetricSwapState(): MagiState {
        val shifts = listOf(Shift("Y", "Y", "", ""), Shift("X", "X", "", ""), Shift("Z", "Z", "", ""))
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"))
        val staff = listOf(Staff("target", 0), Staff("partner", 0), Staff("stable", 0), Staff("helper", 1))
        val target = listOf(1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0)
        val partnerRow = intArrayOf(0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 1)
        partnerRow[0] = 0   // day0のpartnerもYのまま＝day0だけ同日swap相手が存在しない
        val stable = List(11) { 1 }
        val helper = List(11) { 2 }
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-11",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 0), listOf(1, 0, 1)),   // G1(helper)はXを担当不可＝c1対象外
            groupShiftApt = List(2) { List(3) { "" } },
            schedule = listOf(target, partnerRow.toList(), stable, helper),
            wishes = emptyMap(),
            staffRange = mapOf("0,1" to Range("4", "4")),   // X回数保存の同時移設だけが解になるよう固定
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "5", shiftKigou = "X", day2 = "2")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun temporalFlowPolishResolvesWhenNoExactSwapPartnerExistsOnChangedDay() {
        val st = asymmetricSwapState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c1"] ?: 0)
        assertEquals(0, before.hard)

        val out = C1TemporalFlowPolish.apply(
            st, sched, maxPasses = 1, maxRelocations = 4, trials = 8, seed = 7L,
        )
        val after = UnifiedViolationChecker.check(st, out.newSchedule)
        assertEquals("day0に同日swap相手が居なくても解消できる", 0, after.breakdown["c1"] ?: -1)
        assertEquals(0, after.hard)
        assertEquals(1, out.applied)
        assertEquals("targetのX月間回数を保存", 4, out.newSchedule[0].count { it == 1 })
    }

    @Test
    fun temporalFlowPolishIsNoOpWhenNoCons1Rules() {
        val st = asymmetricSwapState().copy(cons1 = emptyList())
        val sched = st.schedule.toIntArray2D()
        val out = C1TemporalFlowPolish.apply(st, sched)
        assertEquals(0, out.applied)
    }

    /**
     * [3.310.0] 状態数の安全弁。DP は疎マップなので窓長ガードだけでは状態数を縛れない。
     * 上限を超えたら解を返さず諦める（`null`）＝呼出側は提案なしとして扱い keep-best は不変。
     * ここでは「上限1」を渡して打切り経路そのものを踏ませ、通常上限なら解が出ることと対比する。
     */
    @Test
    fun exactDpBailsOutInsteadOfExplodingWhenStateCountExceedsTheCap() {
        val t = 12
        val row = IntArray(t) { if (it % 3 == 0) 1 else 0 }
        val locked = BooleanArray(t)
        val rules = listOf(C1TemporalDp.Rule(days = 5, minimum = 2))
        val normal = C1TemporalDp.solve(
            row = row, targetShift = 1, locked = locked, rules = rules, seed = 1L,
        )
        assertNotNull("通常の上限なら解が出る前提を固定する", normal)
        val capped = C1TemporalDp.solve(
            row = row, targetShift = 1, locked = locked, rules = rules, seed = 1L,
            maxDpStates = 1,
        )
        assertNull("状態数が上限を超えたら例外でなく null で諦める", capped)
    }
}
