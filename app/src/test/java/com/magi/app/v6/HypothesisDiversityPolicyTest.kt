package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import java.util.Random
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class HypothesisDiversityPolicyTest {
    private fun state(): MagiState = MagiState(
        startDate = "2026-08-01",
        endDate = "2026-08-05",
        shifts = listOf(
            Shift("休", "休", "", ""),
            Shift("D", "D", "", ""),
            Shift("A", "A", "", ""),
        ),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("S0", 0), Staff("S1", 0), Staff("S2", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1)),
        groupShiftApt = listOf(listOf("", "", "")),
        schedule = listOf(
            listOf(1, 1, 1, 1, 1),
            listOf(2, 2, 2, 2, 2),
            listOf(1, 2, 1, 2, 1),
        ),
        wishes = emptyMap(),
        staffRange = emptyMap(),
        needDay1 = emptyMap(),
        needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
        cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
        cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test
    fun longAutoUsesHeterogeneousPortfolio() {
        // [3.284.0/外部レビュー] AUTO帯の二重分岐解消: 31-210秒は直接APIでも RSI（アプリ経路の
        //   optimizationPlan=RSI→ALNS 複合の主段と同種）。旧 31-90=ALNS の食い違いを固定的に排除。
        assertEquals(V6Algorithm.RSI, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 31))
        assertEquals(V6Algorithm.RSI, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 90))
        assertEquals(V6Algorithm.RSI, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 210))
        assertEquals(V6Algorithm.PORTFOLIO, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 211))
        assertEquals(V6Algorithm.PORTFOLIO, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 300))
        assertEquals(V6Algorithm.RSI_PLUS, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.RSI_PLUS, 300))
    }

    @Test
    fun eightRolesContainFourStartShapes() {
        // [3.278.0] HypothesisDiversityPolicy.algorithmFor はデッドコードとして撤去
        //   （実際のアルゴリズム割当は AdaptiveHypothesisEpochPolicy.algorithmFor が担う）。
        val modes = (0 until 8).map { HypothesisDiversityPolicy.startPlanFor(it).mode }.toSet()
        assertEquals(HypothesisStartMode.values().toSet(), modes)
        assertEquals(HypothesisStartMode.BASELINE, HypothesisDiversityPolicy.startPlanFor(0).mode)
        assertEquals(HypothesisStartMode.BASELINE, HypothesisDiversityPolicy.startPlanFor(4).mode)
    }

    @Test
    fun nonBaselineHypothesesStartFromDifferentBoards() {
        val st = state()
        val base = st.schedule.toIntArray2D()
        val starts = (0 until 8).map { i ->
            V6NativeOptimizer.hypothesisStartFor(st, base, i, 1000L + i)
        }
        assertTrue(base.contentDeepEquals(starts[0]))
        assertTrue(base.contentDeepEquals(starts[4]))
        val distinct = starts.map { s -> s.joinToString("|") { it.joinToString(",") } }.distinct().size
        assertTrue("at least four genuinely different entry boards", distinct >= 4)
        assertTrue(starts.indices.filter { it != 0 && it != 4 }.any { V6NativeOptimizer.scheduleDistance(base, starts[it]) > 0 })
    }

    @Test
    fun startGenerationIsDeterministicForSameRoleAndSeed() {
        val st = state()
        val base = st.schedule.toIntArray2D()
        val a = V6NativeOptimizer.hypothesisStartFor(st, base, 7, 12345L)
        val b = V6NativeOptimizer.hypothesisStartFor(st, base, 7, 12345L)
        assertTrue(a.contentDeepEquals(b))
    }

    @Test
    fun tiedRepairCandidatesDoNotAlwaysChooseFirstIndex() {
        val choices = (0L until 128L).map { seed ->
            HypothesisDiversityPolicy.takeReservoirTie(2, Random(seed * -7046029254386353131L))
        }.toSet()
        assertEquals(setOf(false, true), choices)
    }
}
