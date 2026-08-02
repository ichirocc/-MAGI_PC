package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [C1BeamPolish, 外部パッチ受領→2箇所修正のうえ適用] `V6HotfixPasses.applyC1BeamPolish` の検証。
 * 盤面はホストJVM(kotlin-compiler-embeddable 2.0.21をGradle配布物から利用)でこの関数自体を実際に
 * コンパイル・実行して確認済み（デフォルトseedでc1=1->0・total=10->9・applied=2、5シード共通の
 * 決定的結果）。実データ検証（golden_state.json/sample_state_v6.json, 全15シード）でも
 * totalが真に改善することを確認済み（受領コードの内部ランキングをc1専用近似から真の目的関数
 * hard->total->weightedScoreへ修正し、keep-best安全網を追加した後の話。修正前のコードは
 * golden_state.jsonでc1を91->63まで下げる一方でtotalを291->349・weightedScoreを1939->3722へ
 * 悪化させており、そのまま採用していれば重大な退化だった）。
 */
class C1BeamPolishTest {
    // T=7日, cons1="5日窓X>=2"。target(職員0)がX不足(1<2)。partner1/partner2との同日swap+
    // 玉突きチェーンの組合せで解消可能な最小盤面（BeamC1PolishV2Testと同一盤面を再利用）。
    private fun deficientState(): MagiState {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""))
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("target", 0), Staff("partner1", 0), Staff("partner2", 0))
        val schedule = listOf(
            listOf(1, 0, 1, 0, 0, 0, 1),
            listOf(0, 1, 0, 0, 1, 1, 0),
            listOf(0, 1, 0, 1, 0, 0, 1),
        )
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-07",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "5", shiftKigou = "X", day2 = "2")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun c1BeamPolishResolvesDeficiencyAndImprovesTotal() {
        val st = deficientState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期はcons1窓不足が1件", 1, before.breakdown["c1"] ?: 0)
        assertEquals("初期HARD=0", 0, before.hard)
        // [3.345.0] weekly がシフト別になり初期 total の内訳が変わった（旧 10）。盤面自体は不変。
        assertEquals("初期total", 19, before.total)

        val result = V6HotfixPasses.applyC1BeamPolish(st, sched)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        assertEquals("広域ビーム研磨後はc1=0", 0, after.breakdown["c1"] ?: -1)
        assertEquals("HARDは悪化しない", 0, after.hard)
        assertTrue("totalは真に改善する(退化しない)", after.total < before.total)
        assertTrue("実際に手が採用されている", result.applied > 0)
    }

    @Test
    fun c1BeamPolishIsNoOpWhenNoCons1Rules() {
        val st = deficientState().copy(cons1 = emptyList())
        val sched = st.schedule.toIntArray2D()
        val result = V6HotfixPasses.applyC1BeamPolish(st, sched)
        assertEquals(0, result.applied)
    }

    /**
     * [3.340.0] **ステップを増やしても結果は決して悪くならない**（最良保持の直接検証）。
     *
     * 旧実装は最終ビームの最小しか返さず、ビームは各ステップで全メンバーを子に置き換えるため
     * 探索が進むほど途中で見つけた良い盤面を捨てていた。実データ(golden_state, beamWidth=8, seed=7)
     * では maxSteps=4 で weighted 2859 まで下がるのに、**6以降は根(2985)に戻り採用0**になる
     * ＝長く回すほど成果を失う。最良保持を入れると全 maxSteps で 2834（旧の最良より良い）で一定。
     */
    @Test
    fun moreStepsNeverProduceAWorseResult() {
        val json = javaClass.getResourceAsStream("/golden_state.json")
            ?.bufferedReader()?.use { it.readText() }
            ?: error("golden_state.json not found on the test classpath")
        val st = com.magi.app.model.StateParser.parse(json)
        val sched = st.schedule.toIntArray2D()
        val root = UnifiedViolationChecker.check(st, sched)
        var prev: ViolationReport? = null
        for (ms in listOf(2, 4, 6, 8, 12)) {
            val r = V6HotfixPasses.applyC1BeamPolish(st, sched, beamWidth = 8, maxSteps = ms, seed = 7L)
            val a = UnifiedViolationChecker.check(st, r.newSchedule)
            val p = prev
            if (p != null) {
                assertTrue(
                    "maxSteps=$ms がより少ないステップ数の結果より悪い（最良を保持していない）: " +
                        "${p.hard}/${p.weightedScore}/${p.total} -> ${a.hard}/${a.weightedScore}/${a.total}",
                    !betterReport(p, a),
                )
            }
            prev = a
        }
        val last = prev!!
        assertTrue("この盤面には実際に改善があるので根へ戻ってはいけない", betterReport(last, root))
    }

    /** [3.340.0] 停滞打ち切り(patience)を最小にしても keep-best は壊れず、1手で解ける改善は取り逃さない。 */
    @Test
    fun earlyStopKeepsTheImprovementAndNeverDegrades() {
        val st = deficientState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        for (p in listOf(1, 2, 20, 60)) {
            val r = V6HotfixPasses.applyC1BeamPolish(st, sched, patience = p)
            val a = UnifiedViolationChecker.check(st, r.newSchedule)
            assertEquals("patience=$p でもc1は解消する", 0, a.breakdown["c1"] ?: -1)
            assertEquals("patience=$p でHARDは不変", before.hard, a.hard)
            assertTrue("patience=$p で入力より悪化してはいけない", !betterReport(before, a))
        }
    }

    @Test
    fun c1BeamPolishNeverReturnsScheduleWorseThanInputAcrossManySeeds() {
        // [keep-best安全網の直接検証] 受領コードには無かった安全網が実際に機能し、任意のseedで
        // 「入力より悪化した結果を返す」ことが起きないことを広く確認する。
        val st = deficientState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        for (seed in 0L until 20L) {
            val result = V6HotfixPasses.applyC1BeamPolish(st, sched, seed = seed * 97L + 3L)
            val after = UnifiedViolationChecker.check(st, result.newSchedule)
            assertTrue(
                "seed=$seed: total悪化(before=${before.total}, after=${after.total})は許されない",
                after.total <= before.total,
            )
            assertEquals("seed=$seed: HARDは不変", before.hard, after.hard)
        }
    }
}
