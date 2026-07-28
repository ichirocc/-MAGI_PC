package com.magi.app.v6

import com.magi.app.model.C3Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [C3nPolish, 3.303.0] 禁止連続を「違反パターンがまたぐ全日」で崩せることの検証。
 *
 * 実データの cons3n には `Dﾃ→休→A4` のような3連があり、違反が末尾セルに立つと**パターンの先頭は
 * 2日前**にある。既存機構（当日1セルだけ / 隣接日 j±1 だけ）はそこに構造的に届かなかった。
 */
class C3nPolishTest {

    // shift index: 0=休 1=D 2=A 3=Z(逃げ先・どのルールにも出てこない)
    private val kigou = listOf("休", "D", "A", "Z")
    private val rest = 0
    private val dShift = 1
    private val aShift = 2

    private fun state(
        schedule: List<List<Int>>,
        cons3n: List<C3Row>,
        wishes: Map<String, Int> = emptyMap(),
    ): MagiState = MagiState(
        startDate = "2026-12-01",
        endDate = "",
        shifts = kigou.map { Shift(it, it, "", "") },
        groups = listOf(Group("G", "G")),
        staff = List(schedule.size) { Staff("s$it", 0) },
        use2Patterns = false,
        groupShift = listOf(List(kigou.size) { 1 }),
        groupShiftApt = listOf(List(kigou.size) { "" }),
        schedule = schedule,
        wishes = wishes,
        staffRange = emptyMap(),
        needDay1 = emptyMap(),
        needDay2 = emptyMap(),
        cons1 = emptyList(),
        cons2 = emptyList(),
        cons3 = emptyList(),
        cons3n = cons3n,
        cons3m = emptyList(),
        cons3mn = emptyList(),
        cons41 = emptyList(),
        cons42 = emptyList(),
    )

    /**
     * 3連 `D→休→A` が窓[0..2]で成立。当日(=末尾 day2 の A)と直前(day1 の 休)は**希望で固定**し、
     * 崩せるのはパターン先頭 day0 の D だけ、という構成。
     * - 当日1セルしか触らない既存機構 → day2 が固定なので手が無い
     * - 隣接日 j±1 しか見ない旧実装 → day1 が固定・day3 はパターン外なので手が無い
     * - 本パス（パターン全域＝day0 も候補） → day0 の D を Z へ変えて解消できる
     */
    @Test
    fun resolvesThreeRunByChangingItsHeadTwoDaysBefore() {
        val days = 5
        val row = MutableList(days) { rest }
        row[0] = dShift; row[1] = rest; row[2] = aShift
        val st = state(
            schedule = listOf(row),
            cons3n = listOf(C3Row(listOf("D", "休", "A"))),
            // day1(休) と day2(A) を希望固定 → 崩せるのは day0 だけ
            wishes = mapOf("0,1" to rest, "0,2" to aShift),
        )
        val sched = st.schedule.toIntArray2D()
        val beforeRep = UnifiedViolationChecker.check(st, sched)
        assertEquals("前提: 3連がちょうど1件成立", 1, beforeRep.breakdown["c3n"] ?: 0)

        val result = V6HotfixPasses.applyC3nPolish(st, sched, maxPasses = 2, seed = 7L)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        assertEquals("禁止連続が解消", 0, after.breakdown["c3n"] ?: -1)
        assertEquals("HARD も減る（c3n は HARD）", 0, after.hard)
        assertEquals("崩したのはパターン先頭 day0", true, result.newSchedule[0][0] != dShift)
        assertEquals("希望固定の day1 は不変", rest, result.newSchedule[0][1])
        assertEquals("希望固定の day2 は不変", aShift, result.newSchedule[0][2])
        assertTrue("候補日がパターン全域に広がっている", result.logs.first().message.contains("候補日延べ"))
    }

    /**
     * 当日を変えれば解ける2連。当日自身が候補に入っていることの確認
     * （「前後日と当日も」という要件の当日側）。
     */
    @Test
    fun resolvesTwoRunByChangingTheViolatingDayItself() {
        val days = 4
        val row = MutableList(days) { rest }
        row[0] = dShift; row[1] = aShift
        val st = state(
            schedule = listOf(row),
            cons3n = listOf(C3Row(listOf("D", "A"))),
            // day0(D) を希望固定 → 崩せるのは当日 day1 のみ
            wishes = mapOf("0,0" to dShift),
        )
        val sched = st.schedule.toIntArray2D()
        assertEquals("前提: 2連が成立", 1, UnifiedViolationChecker.check(st, sched).breakdown["c3n"] ?: 0)

        val result = V6HotfixPasses.applyC3nPolish(st, sched, maxPasses = 2, seed = 7L)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        assertEquals("禁止連続が解消", 0, after.breakdown["c3n"] ?: -1)
        assertEquals("希望固定の day0 は不変", dShift, result.newSchedule[0][0])
        assertTrue("当日 day1 が変わった", result.newSchedule[0][1] != aShift)
    }

    @Test
    fun isNoOpWhenNoForbiddenRuleExists() {
        val st = state(schedule = listOf(List(4) { rest }), cons3n = emptyList())
        val sched = st.schedule.toIntArray2D()
        val result = V6HotfixPasses.applyC3nPolish(st, sched, maxPasses = 2, seed = 7L)
        assertEquals(0, result.applied)
        assertEquals(sched.map { it.toList() }, result.newSchedule.map { it.toList() })
    }
}
