package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.410.0] 外部レビュー100件のうち、実コードに当てて **実在** と確認できた項目の回帰。
 *
 * 「確定」とされた30件を検証したところ、2件は既修正・6件は事実でない・8件は記録済みの意図的設計だった。
 * ここで固定するのは残った実在項目のうち v6 層のもの（work/ui 層はホストでコンパイルできない）。
 */
class ReviewFixes3410Test {

    // ---------- E-02: PORTFOLIO のコア数クランプ ----------

    /** 既定（workers==コア数）では **no-op**。ここが崩れると全ユーザーの探索が静かに変わる。 */
    @Test
    fun portfolioWorkerCountIsNoOpWhenWorkersFitsWithinCores() {
        assertEquals(8, V6NativeOptimizer.portfolioWorkerCount(8, cores = 8))
        assertEquals(4, V6NativeOptimizer.portfolioWorkerCount(4, cores = 4))
        assertEquals(2, V6NativeOptimizer.portfolioWorkerCount(2, cores = 8))
    }

    /** 設定タブの並列ワーカーは 16 まで上げられる＝8コア機で2倍の希釈が**設定画面から作れた**。 */
    @Test
    fun portfolioWorkerCountClampsOversubscriptionToCores() {
        assertEquals(8, V6NativeOptimizer.portfolioWorkerCount(16, cores = 8))
        assertEquals(4, V6NativeOptimizer.portfolioWorkerCount(16, cores = 4))
    }

    /** 3.224.0 の多様性の下限2（1コア機でも2仮説）は割らない。 */
    @Test
    fun portfolioWorkerCountKeepsTheDiversityFloorOfTwo() {
        assertEquals(2, V6NativeOptimizer.portfolioWorkerCount(2, cores = 1))
        assertEquals(2, V6NativeOptimizer.portfolioWorkerCount(16, cores = 1))
        assertEquals(1, V6NativeOptimizer.portfolioWorkerCount(1, cores = 1))
    }

    // ---------- E-15: SaParams の不正値は構築時に落とす ----------

    @Test
    fun saParamsRejectsDegenerateLahcLenAndChain() {
        assertThrows(IllegalArgumentException::class.java) { SaParams(lahcLen = 0) }
        assertThrows(IllegalArgumentException::class.java) { SaParams(chain = 0) }
        // 既定は当然通る（require を足したせいで通常経路が落ちないことの確認）。
        assertEquals(200, SaParams().lahcLen)
    }

    // ---------- P-01 / P-06 ----------

    /** 休が index 0 でないデータ。schedule に範囲外セルを1つ置く。 */
    private fun stWithRestNotFirst(groupIdxOfS0: Int) = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-02",
        shifts = listOf(Shift("X", "X", "", ""), Shift("休", "休", "", "")),
        groups = listOf(Group("G0", "G0")),
        staff = listOf(Staff("s0", groupIdxOfS0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(listOf(99, 0)),   // 1セル目が範囲外
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    /**
     * P-01: 範囲外セルはハードコードの 0 でなく **休** へ。旧実装は休が先頭でないデータで
     * 勤務シフトへ化けていた（3.106.0 が `Ws1Ops.removeShift` で直したのと同じ取り違え）。
     */
    @Test
    fun initialAssignmentSendsOutOfRangeCellsToRestNotToIndexZero() {
        val p = Problem(stWithRestNotFirst(0))
        assertEquals("休が index0 でないフィクスチャであること", 1, p.restIdx)
        assertEquals(p.restIdx, p.initialAssignment()[0][0])
    }

    /**
     * P-06: `groupIdx` が範囲外でも **落ちず**（旧: `bucket[sgrp[i]]` で AIOOBE）、
     * 先頭群へ寄せたうえで**必ず記録される**（黙って寄せると別の群のルールが静かに掛かる）。
     */
    @Test
    fun outOfRangeGroupIdxIsClampedAndRecordedInsteadOfCrashing() {
        val p = Problem(stWithRestNotFirst(groupIdxOfS0 = 7))
        assertEquals(0, p.sgrp[0])
        assertEquals(listOf(0), p.outOfRangeGroupStaff)
        // 落ちないことの確認（旧実装はここで AIOOBE）。
        assertTrue(p.initialAssignment()[0].size == 2)
    }

    /** 正常データでは記録が空＝誤検知しない。 */
    @Test
    fun validGroupIdxIsNotRecorded() {
        assertTrue(Problem(stWithRestNotFirst(0)).outOfRangeGroupStaff.isEmpty())
    }

    /** 2k) 範囲外グループが設定ミス診断に出る（2i のスキル群と対）。 */
    @Test
    fun outOfRangeGroupIdxIsReportedBySanityGuidance() {
        val st = stWithRestNotFirst(groupIdxOfS0 = 7)
        val issues = V6SanityPort.buildGuidance(st, Problem(st))
        assertTrue("グループの割当の診断が出ること: ${issues.map { it.where }}",
            issues.any { it.where == "グループの割当" })
        val clean = stWithRestNotFirst(0)
        assertTrue(V6SanityPort.buildGuidance(clean, Problem(clean)).none { it.where == "グループの割当" })
    }

    // ---------- D-01 / D-02: DeltaEvaluator の不正入力は丸めず落とす ----------

    @Test
    fun deltaEvaluatorRejectsMalformedBoards() {
        val p = Problem(stWithRestNotFirst(0))
        val de = DeltaEvaluator(p)
        assertThrows(IllegalArgumentException::class.java) { de.reset(arrayOf(intArrayOf(0))) }          // 行が短い
        assertThrows(IllegalArgumentException::class.java) { de.reset(arrayOf(intArrayOf(0, 99))) }      // 値が範囲外
        assertThrows(IllegalArgumentException::class.java) { de.apply(0, 0, 99) }                         // nw が範囲外
        de.reset(arrayOf(intArrayOf(0, 1)))   // 正常な盤面は通る
    }
}
