package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.306.0/既定OFF経路] 残差ベースの4段停滞脱出。既定経路（回数で機械的に巡回）とは独立に、
 * 「いま残っている違反の種類で役割を選ぶ」「停滞の深さを役割変更で消さない」という2点を固定する。
 *
 * この経路は `PolishGate.adaptiveEscapeControl` が真のときだけ本番で走る。既定 OFF の根拠
 * （実データ3件×4回の A/B で有意差を検出できず）は `PolishGate` の docstring に記録してある。
 */
class StagnationEscapeControllerTest {

    private fun report(breakdown: Map<String, Int>, hard: Int = 0): ViolationReport =
        ViolationReport(
            violations = emptyMap(),
            needViolations = emptyMap(),
            countViolations = emptyMap(),
            breakdown = breakdown,
            total = breakdown.values.sum(),
            hard = hard,
            soft = breakdown.values.sum(),
            weightedScore = breakdown.entries.sumOf { it.value * MirrorKeys.weightOf(it.key) },
        )

    private fun assign(role: HypothesisEpochRole, depth: Int = 0) =
        AdaptiveHypothesisEpochPolicy.assignmentFor(role, depth)

    @Test
    fun w0NeverLeavesTheSafetyFloor() {
        val c = StagnationEscapeController(0)
        for (depth in 0..9) {
            val next = c.nextAssignment(
                current = assign(HypothesisEpochRole.LARGE_DESTROY_ALNS, depth),
                report = report(mapOf("c1" to 9), hard = 3),
                plateauDepth = depth,
                nearestOtherDistance = 0,
                hasOtherHypothesis = true,
            )
            assertEquals(HypothesisEpochRole.BASELINE_REFINE, next.role)
            assertEquals("W0 は強度も上げない", 0, next.intensity)
        }
    }

    /** 残差の種類が役割を決める＝既定経路（回数だけで巡回）との本質的な違い。 */
    @Test
    fun roleFollowsTheDominantResidualFamily() {
        val c = StagnationEscapeController(1)
        fun pick(rep: ViolationReport): HypothesisEpochRole = c.nextAssignment(
            current = assign(HypothesisEpochRole.BASELINE_REFINE, 1),
            report = rep,
            plateauDepth = 1,
            nearestOtherDistance = 99,
            hasOtherHypothesis = false,
        ).role

        assertEquals(HypothesisEpochRole.HARD_FAMILY_RSI, pick(report(mapOf("covU" to 2), hard = 2)))
        assertEquals(HypothesisEpochRole.DAY_BLOCK_ALNS, pick(report(mapOf("c1" to 7))))
        assertEquals(HypothesisEpochRole.PERSONAL_RSI, pick(report(mapOf("low" to 4))))
    }

    /**
     * 同じ役割にとどまる限り、停滞の深さが増えれば強度は上がる。
     * （役割を跨ぐと `intensityFor` の base が役割ごとに 0〜3 と違うので単調性は成立しない。
     * 深さの保持が効くのは「同じ役割を続けたときの昇圧」と「4段の巡回」の2つ。）
     */
    @Test
    fun intensityEscalatesWithDepthWithinTheSameRole() {
        val role = HypothesisEpochRole.DAY_BLOCK_ALNS
        val series = (0..8).map { AdaptiveHypothesisEpochPolicy.intensityFor(role, it) }
        assertTrue("深さで単調非減少", series.zipWithNext().all { it.first <= it.second })
        assertTrue("深いほど強い", series.last() > series.first())
    }

    /**
     * 停滞の深さは役割を替えても保持され、4段（制約別主手 → 再結合 → 多様化 → 深い破壊）を巡る。
     * 既定経路は `shouldReassign` で再配属するたび呼出側が停滞カウンタを 0 に戻すため、ここが最大の差。
     */
    @Test
    fun deepeningPlateauVisitsSeveralDistinctRoles() {
        val c = StagnationEscapeController(2)
        val rep = report(mapOf("c1" to 5))
        var current = assign(HypothesisEpochRole.BASELINE_REFINE, 0)
        val visited = LinkedHashSet<HypothesisEpochRole>()
        for (depth in 1..8) {
            current = c.nextAssignment(
                current = current,
                report = rep,
                plateauDepth = depth,
                nearestOtherDistance = 99,
                hasOtherHypothesis = false,
            )
            visited.add(current.role)
        }
        assertTrue("深い停滞では複数の役割を巡る（実測 $visited）", visited.size >= 3)
    }

    /** 改善したエポックは同じ役割を続け、それまでの昇圧は落とす。 */
    @Test
    fun improvementKeepsRoleAndDropsEscalation() {
        val c = StagnationEscapeController(3)
        val current = assign(HypothesisEpochRole.DAY_BLOCK_ALNS, 6)
        val next = c.nextAssignment(
            current = current,
            report = report(mapOf("c1" to 3)),
            plateauDepth = 0,
            nearestOtherDistance = 99,
            hasOtherHypothesis = false,
        )
        assertEquals(current.role, next.role)
        assertTrue("昇圧は落ちる", next.intensity < current.intensity)
    }

    /** 他仮説と同じ盆地へ潰れたら、スカラー改善があっても必ず別の役割へ移る。 */
    @Test
    fun collapsedBasinAlwaysLeavesTheCurrentRole() {
        val c = StagnationEscapeController(5)
        val current = assign(HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS, 0)
        val next = c.nextAssignment(
            current = current,
            report = report(mapOf("c1" to 2)),
            plateauDepth = 0,
            nearestOtherDistance = AdaptiveHypothesisEpochPolicy.DUPLICATE_DISTANCE_CELLS,
            hasOtherHypothesis = true,
        )
        assertNotEquals(current.role, next.role)
    }

    /**
     * [3.308.2/敵対トレース] 旧テストは名前が「役割変更でリセットされない」と言いながら、実際には
     * `nextPlateauDepth` の足し算しか見ておらず、役割変更に一度も触れていなかった（しかもその関数は
     * 本番から呼ばれないデッドコードだった）。役割が変わっても深さが効き続けることは、**制御器の
     * 出力の強度**として観測できる。そこを直接固定する。
     */
    @Test
    fun accumulatedDepthStillRaisesIntensityAfterTheRoleChanges() {
        val controller = StagnationEscapeController(workerIndex = 1)
        val current = AdaptiveHypothesisEpochPolicy.initialAssignmentFor(1)
        val deep = controller.nextAssignment(
            current = current,
            report = report(mapOf("c1" to 3)),
            plateauDepth = 6,
            nearestOtherDistance = 40,
            hasOtherHypothesis = true,
        )
        // 役割は実際に変わっている（深さが役割継続の副産物ではないことの確認）。
        assertNotEquals(current.role, deep.role)
        // 深さ6は intensityFor の growth=3 を意味する。同じ役割を深さ0で組んだものより必ず強い。
        val shallow = AdaptiveHypothesisEpochPolicy.assignmentFor(deep.role, escapeDepth = 0)
        assertEquals(shallow.intensity + 3, deep.intensity)
    }

    /** 既定 OFF＝本番の経路は従来のまま、を明示的に固定する。 */
    @Test
    fun gateIsOffByDefault() {
        assertEquals(false, PolishGate.adaptiveEscapeControl)
    }
}
