package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.278.0/監査で実証された2クラッシュの回帰テスト]
 *
 *  1. MinCostAssignment: 全INF行（担当可否ゼロの群の職員・-1センチネル等）で j1=-1 のまま p[-1] を読み
 *     AIOOBE でプロセスごと落ちていた（handleOptimize フル経路=12秒予算で再現済み。ポテンシャル v[j] は
 *     単調非増加のため全INF行では厳密更新が構造的に一度も起きない＝決定的クラッシュ）。
 *  2. C1RepairAnalysis.opportunities: -1 セルを無検証で covUCell(-1,…)＝need1[-1][j] へ渡し AIOOBE
 *     （hasActionableC1 ゲート／applyC1IndexChainRepair の両経路で再現済み・3.270.0 と同型の取り残し）。
 */
class PolishRobustnessTest {

    /** G1 = 担当可否が1つもチェックされていない群（正規のエディタ操作で作れるデータ）。 */
    private fun emptyBucketState(): MagiState {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "1", ""))
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-03",
            shifts = shifts, groups = listOf(Group("G0", "G0"), Group("G1", "G1")),
            staff = listOf(Staff("s0", 0), Staff("s1", 1)), use2Patterns = false,
            groupShift = listOf(listOf(1, 1), listOf(0, 0)),
            groupShiftApt = listOf(listOf("", ""), listOf("", "")),
            schedule = listOf(listOf(1, 0, 1), listOf(0, 1, 0)), wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    /** 範囲外セル(99)入り＝normalizeSchedule が -1 センチネルへ写像する盤面。c1不足窓が -1 日を含む。 */
    private fun neg1CellState(): MagiState {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "1", ""))
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-03",
            shifts = shifts, groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0)), use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)), groupShiftApt = listOf(listOf("", "", "")),
            schedule = listOf(listOf(2, 99, 2)), wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row("3", "X", "1")), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun minCostAssignmentReturnsNullForAllInfRowInsteadOfCrashing() {
        // row0 が全列 INF ＝実行可能な完全割当なし → null（旧: p[-1] AIOOBE）。
        val inf = MinCostAssignment.INF
        val infeasible = arrayOf(longArrayOf(inf, inf), longArrayOf(1L, 2L))
        assertNull(MinCostAssignment.solve(infeasible))
        // 実行可能な行列は従来どおり有効な置換を返す。
        val feasible = arrayOf(longArrayOf(1L, 10L), longArrayOf(10L, 1L))
        val assign = MinCostAssignment.solve(feasible)
        assertNotNull(assign)
        assertEquals(setOf(0, 1), assign!!.toSet())
        assertEquals(0, assign[0]); assertEquals(1, assign[1])
    }

    @Test
    fun dayAssignmentPolishSkipsInfeasibleDaysInsteadOfCrashing() {
        // 空bucket職員(s1)の行は全列 INF → 旧実装は Hungarian 内で AIOOBE。新実装は null→その日 skip の no-op。
        val s = emptyBucketState()
        val p = Problem(s)
        val res = V6HotfixPasses.applyDayAssignmentPolish(s, s.schedule.toIntArray2D())
        val norm = normalizeSchedule(s.schedule.toIntArray2D(), p)
        assertTrue("盤面は不変（全日が実行可能な割当なし=skip）",
            res.newSchedule.indices.all { res.newSchedule[it].contentEquals(norm[it]) })
    }

    @Test
    fun runPostOptimizationSurvivesEmptyBucketStaff() {
        // 旧: applyDayAssignmentPolish 経由で runPostOptimization 全体（=handleOptimize 本番経路）が
        //   決定的にクラッシュしていた（12秒予算のフル経路で再現済み）。
        val s = emptyBucketState()
        val res = V6HotfixPasses.runPostOptimization(s, s.schedule.toIntArray2D(), algoName = "test", seed = 1L)
        val rep = UnifiedViolationChecker.check(s, res.schedule)
        assertTrue("完走しレポートが得られる", rep.total >= 0)
    }

    @Test
    fun c1IndexGateAndRepairSurviveNeg1Cells() {
        // 旧: opportunities が -1 セルで covUCell(-1,…)＝need1[-1][j] を読み、hasActionableC1 ゲートと
        //   applyC1IndexChainRepair の両方が AIOOBE。修正後は除去項0として扱い完走する。
        val s = neg1CellState()
        val p = Problem(s)
        val sc = s.schedule.toIntArray2D()
        assertEquals("前提: 99 は -1 に正規化される", -1, normalizeSchedule(sc, p)[0][1])
        val index = C1RepairIndex.build(p, sc)
        assertTrue("不足窓は正しく検出される（クラッシュせず）", C1DeltaPrefilter.hasActionableC1(index))
        val res = V6HotfixPasses.applyC1IndexChainRepair(s, sc)
        assertTrue("index駆動修復も完走する", res.afterTotal >= 0)
    }
    /**
     * [3.356.0] 設定トグルの「効き」ログ。ON なのに観測ゼロなら、そのトグルはこの実行では何もして
     * いない＝消してよい判断材料になる、という点をそのまま固定する。
     */
    /**
     * [3.360.1/敵対検証] 設定の効きカウンタは**並列ワーカーから加算される**（parityChecks は
     * SA/LAHC/ALNS/研磨の4経路×全ワーカーから毎チャンク）。旧実装の `@Volatile var Int` に `++` は
     * read-modify-write なので取りこぼし、ログが実数と称して下限を出していた。
     *
     * 8スレッド×2万回の加算がちょうど16万になることを固定する。`@Volatile var Int` へ戻すと
     * この1件だけが落ちる（実際に戻して確認済み）。他のテストは通る＝この不変条件を守るものが
     * 他に無かったことの裏づけでもある。
     */
    /**
     * [3.360.3] 職員ゼロの入力で、原因の読めない例外でなく**何を直せばよいか言う**メッセージで止まる。
     *
     * 期間には `dayCount > 0` のガードがあるのに職員数には無く非対称だった。S=0 は編集画面からは
     * 作れない（`Ws1Ops.removeStaff` が最後の1名を消さない）が、JSON/CSV 取込で外部から入りうる。
     * **ガードを外して実測**: `handleOptimize` は `bound must be positive`（= `rng.nextInt(0)`）を投げていた。
     * `handleSimple`/`handleSmartInitial` は生成器側が「期間/職員/シフトが不足しています」を返しており
     * 致命的ではなかったが、3経路で同じ文言に揃える。
     */
    @Test
    fun emptyStaffIsRejectedWithAnActionableMessage() = kotlinx.coroutines.runBlocking {
        // 職員ゼロだが schedule に行が残っている＝取込で起こりうる不整合な入力。
        // （staff も schedule も空なら dayCount==0 で既存の期間ガードが先に止めるので、
        //  この形でないと職員ガードには到達しない。）
        val s = emptyBucketState().let { it.copy(staff = emptyList(), schedule = listOf(listOf(0, 1, 0))) }
        for (call in listOf<suspend () -> Unit>(
            { V6FinalPort.handleOptimize(s, emptyArray(), 1) },
            { V6FinalPort.handleSimple(s) },
            { V6FinalPort.handleSmartInitial(s) },
        )) {
            val e = runCatching { call() }.exceptionOrNull()
            assertNotNull("職員ゼロは受け付けない", e)
            assertTrue(
                "原因と直し方が読めるメッセージであること: ${e?.message}",
                e?.message?.contains("職員が1人も登録されていません") == true,
            )
        }
    }

    @Test
    fun tuningCountersDoNotLoseIncrementsUnderParallelWorkers() {
        TuningTelemetry.reset()
        val threads = 8
        val perThread = 20_000
        val gate = java.util.concurrent.CountDownLatch(1)
        val ts = (0 until threads).map {
            Thread {
                gate.await()
                repeat(perThread) { TuningTelemetry.parityChecks.incrementAndGet() }
            }.apply { start() }
        }
        gate.countDown()
        ts.forEach { it.join() }
        assertEquals(threads * perThread, TuningTelemetry.parityChecks.get())
        TuningTelemetry.reset()
        assertEquals(0, TuningTelemetry.parityChecks.get())
    }

    @Test
    fun tuningSummaryDistinguishesOffFromOnWithNoObservedEffect() {
        val wide = PolishGate.wideC3nBreakDays
        val filter = PolishGate.filterC3nIncrease
        val escape = PolishGate.adaptiveEscapeControl
        try {
            TuningTelemetry.reset()
            PolishGate.wideC3nBreakDays = false
            PolishGate.filterC3nIncrease = true
            PolishGate.adaptiveEscapeControl = true
            val off = TuningTelemetry.summary(nativeOn = false, parityOn = false, softPolishOn = false)
            assertTrue(off, off.contains("禁止連続の崩し範囲=OFF"))
            assertTrue(off, off.contains("禁止連続の事前フィルタ=ON(この実行では観測なし)"))
            assertTrue(off, off.contains("立て直し方=ON(この実行では観測なし)"))

            TuningTelemetry.c3nFilterSkipped.set(12)
            TuningTelemetry.escapeControlUsed.set(3)
            val on = TuningTelemetry.summary(nativeOn = true, parityOn = true, softPolishOn = true)
            assertTrue(on, on.contains("ネイティブ加速=ON"))
            assertTrue(on, on.contains("禁止連続の事前フィルタ=ON(12件"))
            assertTrue(on, on.contains("立て直し方=ON(3回の役割決定)"))
            // reset で実行ごとの計測に戻ること（前の実行の数字を持ち越さない）。
            TuningTelemetry.reset()
            assertTrue(TuningTelemetry.summary(true, true, true).contains("立て直し方=ON(この実行では観測なし)"))
        } finally {
            PolishGate.wideC3nBreakDays = wide
            PolishGate.filterC3nIncrease = filter
            PolishGate.adaptiveEscapeControl = escape
            TuningTelemetry.reset()
        }
    }

}
