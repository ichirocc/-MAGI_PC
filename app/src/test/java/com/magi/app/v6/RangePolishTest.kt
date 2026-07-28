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
 * [RangePolish・玉突き連鎖の横展開その2] grilling不要(C3mnPolishと同型、ユーザー承認2026-07-19)で
 * 実装した個人回数(low/high)研磨の検証。桒澤美幸の実例（Aｱ超過・B1担当が全職員中唯一で交換相手が
 * 構造的に存在しない）を最小盤面で再現する: A(職員)がshift X(need1=1)を超過(high)。Aが自身のX保有日を
 * 別シフト(Y)へ動かすだけではXの被覆が欠けるため、直接の自己修正は成立しない。B(在勤中のZ、需要なし=
 * いつでも動かせる)がXへ玉突きで補充することで初めて解消する局面。
 */
class RangePolishTest {

    private fun highState(): MagiState {
        // shift: 0=休(need無) 1=X(need1=1) 2=Y(need無、Aの逃げ先) 3=Z(need無、Bの現在地)
        val shifts = listOf(
            Shift("休", "休", "", ""),
            Shift("X", "X", "1", ""),
            Shift("Y", "Y", "", ""),
            Shift("Z", "Z", "", ""),
        )
        val groups = listOf(Group("GA", "GA"), Group("GB", "GB"))
        val groupShift = listOf(
            listOf(1, 1, 1, 0), // GA(A)=休,X,Y
            listOf(1, 1, 0, 1), // GB(B)=休,X,Z
        )
        val staff = listOf(Staff("A", 0), Staff("B", 1))
        val schedule = listOf(
            listOf(1, 1), // A = X, X （高hi=1に対し2回＝超過1）
            listOf(3, 3), // B = Z, Z （需要なし=いつでも動かせる）
        )
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-02",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift,
            groupShiftApt = List(2) { List(4) { "" } },
            schedule = schedule,
            wishes = emptyMap(),
            staffRange = mapOf("0,1" to Range("0", "1")), // A の shift X: lo=0, hi=1
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun rangePolishResolvesHighViaChainWhenDirectSelfChangeWouldCreateCovU() {
        val st = highState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertTrue("初期はhigh違反があること", (before.breakdown["high"] ?: 0) > 0)
        assertEquals("初期はHARD=0(covU無し、AがXを単独充足)", 0, before.hard)

        val result = V6HotfixPasses.applyRangePolish(st, sched, seed = 1L)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        assertEquals("玉突き適用後はhigh=0", 0, after.breakdown["high"] ?: -1)
        assertEquals("HARDは悪化しない(0のまま)", 0, after.hard)
        assertEquals("Xの被覆(covU)は悪化しない", 0, after.breakdown["covU"] ?: -1)
        assertTrue("実際に手が採用されている", result.applied > 0)
    }

    @Test
    fun rangePolishIsNoOpWhenNoStaffRange() {
        val st = highState().copy(staffRange = emptyMap())
        val sched = st.schedule.toIntArray2D()
        val result = V6HotfixPasses.applyRangePolish(st, sched, seed = 1L)
        assertEquals(0, result.applied)
    }

    // [頭打ちの理由を可視化] Bが両日とも希望固定(Z)だと玉突きの唯一の候補が使えず「候補なし」で
    // 頭打ちする。ログの残存表示にその理由が出ることを固定する。
    @Test
    fun rangePolishLogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked() {
        val st = highState().copy(wishes = mapOf("1,0" to 3, "1,1" to 3))
        val sched = st.schedule.toIntArray2D()
        val result = V6HotfixPasses.applyRangePolish(st, sched, seed = 1L)
        assertEquals("唯一の候補が希望固定のため採用0回", 0, result.applied)
        val msg = result.logs.first().message
        assertTrue("残存表示に候補なしの理由が出ること: $msg", msg.contains("候補なし"))
        assertTrue("対象職員名(A)が出ること: $msg", msg.contains("A "))
    }

    /**
     * [3.244.0 手M] 直接2人交換が不可能な4人循環。
     *
     * 初期: high=A, substitute=C, bridge1=D, bridge2=B
     * 解:   high=B, substitute=A, bridge1=C, bridge2=D
     *
     * highはCを担当不可なので direct pair swap は不可能。日単位完全割当なら
     * A→B→D→C→A の4-cycleを一度に解き、日別シフト人数を完全保存できる。
     * substituteにはAのlow違反を設定しないため、「low対象だけを見る」旧ロジックではなく
     * 担当可能＋上限余力の代用者探索が動くことも同時に固定する。
     */
    private fun exactDayCycleState(bridgeWishLocked: Boolean = false): MagiState {
        val shifts = listOf(
            Shift("A", "A", "1", ""),
            Shift("B", "B", "1", ""),
            Shift("C", "C", "1", ""),
            Shift("D", "D", "1", ""),
        )
        val groups = listOf(
            Group("H", "H"), Group("S", "S"), Group("R1", "R1"), Group("R2", "R2"),
        )
        val groupShift = listOf(
            listOf(1, 1, 0, 0), // high: A/B
            listOf(1, 0, 1, 0), // substitute: A/C
            listOf(0, 0, 1, 1), // bridge1: C/D
            listOf(0, 1, 0, 1), // bridge2: B/D
        )
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-01",
            shifts = shifts,
            groups = groups,
            staff = listOf(
                Staff("high", 0), Staff("substitute", 1), Staff("bridge1", 2), Staff("bridge2", 3),
            ),
            use2Patterns = false,
            groupShift = groupShift,
            groupShiftApt = List(4) { List(4) { "" } },
            schedule = listOf(
                listOf(0), // high=A（hi=0に対し超過1）
                listOf(2), // substitute=C
                listOf(3), // bridge1=D
                listOf(1), // bridge2=B
            ),
            wishes = if (bridgeWishLocked) mapOf("3,0" to 1) else emptyMap(),
            staffRange = mapOf("0,0" to Range("0", "0")),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun rangePolishExactDayMatchingFindsFourPersonCycleWithoutLowReceiver() {
        val st = exactDayCycleState()
        val sched = st.schedule.toIntArray2D()
        val beforeDay = sched.map { it[0] }.sorted()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["high"] ?: 0)
        assertTrue("代用者にはAのlow違反が無い", before.countViolations["1,0"] != "vio-low")

        val result = V6HotfixPasses.applyRangePolish(st, sched, maxPasses = 1, seed = 1L)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        assertEquals("high解消", 0, after.breakdown["high"] ?: -1)
        assertEquals("HARD不変", 0, after.hard)
        assertEquals("日別シフト多重集合を完全保存", beforeDay, result.newSchedule.map { it[0] }.sorted())
        assertEquals("4-cycleの一意解", listOf(1, 0, 2, 3), result.newSchedule.map { it[0] })
        assertTrue("手Mが採用されたこと", result.logs.first().message.contains("日割当:1"))
    }

    @Test
    fun rangePolishExactDayMatchingRespectsWishLockedBridge() {
        val st = exactDayCycleState(bridgeWishLocked = true)
        val sched = st.schedule.toIntArray2D()
        val result = V6HotfixPasses.applyRangePolish(st, sched, maxPasses = 1, seed = 1L)

        assertEquals("循環に必須のbridge2が希望固定なら不採用", 0, result.applied)
        assertEquals(sched.map { it.toList() }, result.newSchedule.map { it.toList() })
    }

    /**
     * [不採用の主因, 3.302.0] 「不採用」のときログへ併記する主因族の算出。件数でなく**重み付き**で
     * 最も増えた族を返すこと（c1=15 が1件増えるより low=90 が1件増えるほうが主因）を固定する。
     */
    @Test
    fun worstWorsenedFamilyPicksHeaviestWeightedIncreaseNotLargestCount() {
        val before = report(mapOf("c1" to 10, "low" to 0, "c3" to 0))
        // c3 は +5件(重み3=15)、low は +1件(重み90)、c1 は減少。重み最大の low が主因。
        val after = report(mapOf("c1" to 9, "low" to 1, "c3" to 5))
        assertEquals("low", worstWorsenedFamily(after, before))
    }

    @Test
    fun worstWorsenedFamilyReturnsNullWhenNothingGotWorse() {
        val before = report(mapOf("c1" to 10, "low" to 2))
        val after = report(mapOf("c1" to 8, "low" to 2))
        assertEquals(null, worstWorsenedFamily(after, before))
    }

    private fun report(breakdown: Map<String, Int>): ViolationReport =
        ViolationReport(
            violations = emptyMap(),
            needViolations = emptyMap(),
            countViolations = emptyMap(),
            breakdown = breakdown,
            total = breakdown.values.sum(),
            hard = 0,
            soft = breakdown.values.sum(),
            weightedScore = breakdown.entries.sumOf { it.value * MirrorKeys.weightOf(it.key) },
        )
}
