package com.magi.app.v6

/** Role used by one hypothesis during one adaptive portfolio epoch. */
internal enum class HypothesisEpochRole {
    BASELINE_REFINE,
    ELITE_RELINK,
    DAY_BLOCK_ALNS,
    HARD_FAMILY_RSI,
    HARD_DEBT_RSI_PLUS,
    LARGE_DESTROY_ALNS,
    PERSONAL_RSI,
    MAX_DISTANCE_RSI_PLUS,
}

// [3.278.0/デッドコード除去] safetyFloor フィールドは計算されるだけで本番で一度も読まれなかった
//   （テスト assert のみ）。W0/W4 の安全床という設計意図は assignmentFor の role 分岐(slot==0/4)自体が担う。
internal data class HypothesisEpochAssignment(
    val role: HypothesisEpochRole,
    val algorithm: V6Algorithm,
    val intensity: Int,
)

/**
 * Pure scheduling policy for convergence-aware parallel hypotheses.
 *
 * W0 is the permanent safety floor. W4 starts as a second precision worker and changes to
 * elite path relinking after its first plateau. The other six workers rotate through six
 * genuinely different escape roles whenever they stop improving or collapse onto another
 * worker's basin.
 */
internal object AdaptiveHypothesisEpochPolicy {
    const val BASE_QUANTUM_SEC = 5
    const val IMPROVING_QUANTUM_SEC = 8
    const val RSI_PLUS_BASE_QUANTUM_SEC = 35
    const val RSI_PLUS_IMPROVING_QUANTUM_SEC = 45
    const val DUPLICATE_DISTANCE_CELLS = 2

    private val escapeRoles = arrayOf(
        HypothesisEpochRole.DAY_BLOCK_ALNS,
        HypothesisEpochRole.HARD_FAMILY_RSI,
        HypothesisEpochRole.HARD_DEBT_RSI_PLUS,
        HypothesisEpochRole.LARGE_DESTROY_ALNS,
        HypothesisEpochRole.PERSONAL_RSI,
        HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS,
    )

    private fun baseEscapeOffset(index: Int): Int = when (Math.floorMod(index, 8)) {
        1 -> 0
        2 -> 1
        3 -> 2
        5 -> 3
        6 -> 4
        7 -> 5
        else -> 0
    }

    fun assignmentFor(index: Int, reassignments: Int): HypothesisEpochAssignment {
        val slot = Math.floorMod(index, 8)
        val role = when {
            slot == 0 -> HypothesisEpochRole.BASELINE_REFINE
            slot == 4 && reassignments == 0 -> HypothesisEpochRole.BASELINE_REFINE
            slot == 4 -> HypothesisEpochRole.ELITE_RELINK
            else -> escapeRoles[Math.floorMod(baseEscapeOffset(slot) + reassignments, escapeRoles.size)]
        }
        return HypothesisEpochAssignment(
            role = role,
            algorithm = algorithmFor(role),
            intensity = intensityFor(role, reassignments),
        )
    }

    fun algorithmFor(role: HypothesisEpochRole): V6Algorithm = when (role) {
        HypothesisEpochRole.DAY_BLOCK_ALNS,
        HypothesisEpochRole.LARGE_DESTROY_ALNS -> V6Algorithm.ALNS

        HypothesisEpochRole.HARD_FAMILY_RSI,
        HypothesisEpochRole.PERSONAL_RSI -> V6Algorithm.RSI

        HypothesisEpochRole.BASELINE_REFINE,
        HypothesisEpochRole.ELITE_RELINK,
        HypothesisEpochRole.HARD_DEBT_RSI_PLUS,
        HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS -> V6Algorithm.RSI_PLUS
    }

    /**
     * [3.308.0] 次のエポックが「改善直後」の長い量子（8/45 秒）を受け取れるか。
     *
     * **役割が変わった直後は受け取れない**。新しい役割はまだ何も証明していないのに、
     * 前の役割が稼いだ改善で 35→45 秒（RSI+）へ昇格するのは根拠が無い。
     * 契約に名前を付けて呼出側から使う（インライン式のままだと呼出サイトごとにずれる。
     * 3.306.0 の制御器経路が実際にずれた実績＝経路自体は 3.409.21 で削除済み）。
     */
    fun carriesImprovingQuantum(improvedThisEpoch: Boolean, roleChanged: Boolean): Boolean =
        improvedThisEpoch && !roleChanged

    fun intensityFor(role: HypothesisEpochRole, growthBasis: Int): Int {
        // 2回失敗してから強度を上げる。最初の停滞は「別の考え方を試す」段階であって、
        // いきなり最大近傍へ残予算を注ぐ段階ではない。負値は呼出側の想定外なので 0 へ丸める。
        val growth = (growthBasis.coerceAtLeast(0) / 2).coerceAtMost(3)
        val base = when (role) {
            HypothesisEpochRole.BASELINE_REFINE -> 0
            HypothesisEpochRole.ELITE_RELINK -> 1
            HypothesisEpochRole.DAY_BLOCK_ALNS,
            HypothesisEpochRole.HARD_FAMILY_RSI,
            HypothesisEpochRole.PERSONAL_RSI -> 1
            HypothesisEpochRole.HARD_DEBT_RSI_PLUS -> 2
            HypothesisEpochRole.LARGE_DESTROY_ALNS,
            HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS -> 3
        }
        return base + growth
    }

    /**
     * A plateau is not a stop condition. It requests another role. W0 never leaves the safety
     * floor. W4 changes to path relinking after one plateau. Duplicate basins are reassigned even
     * when their scalar score happened to improve, because diversity is a separate invariant.
     */
    fun shouldReassign(
        index: Int,
        improvedThisEpoch: Boolean,
        stagnantEpochs: Int,
        nearestOtherDistance: Int,
    ): Boolean {
        val slot = Math.floorMod(index, 8)
        if (slot == 0) return false
        if (nearestOtherDistance <= DUPLICATE_DISTANCE_CELLS) return true
        return !improvedThisEpoch && stagnantEpochs >= 1
    }

    fun nextStagnantEpochs(previous: Int, improvedThisEpoch: Boolean): Int =
        if (improvedThisEpoch) 0 else previous + 1

    fun quantumSeconds(
        assignment: HypothesisEpochAssignment,
        improvedPreviousEpoch: Boolean,
        remainingSeconds: Int,
    ): Int {
        if (remainingSeconds <= 0) return 0
        val requested = if (assignment.algorithm == V6Algorithm.RSI_PLUS) {
            if (improvedPreviousEpoch) RSI_PLUS_IMPROVING_QUANTUM_SEC else RSI_PLUS_BASE_QUANTUM_SEC
        } else {
            if (improvedPreviousEpoch) IMPROVING_QUANTUM_SEC else BASE_QUANTUM_SEC
        }
        return requested.coerceAtMost(remainingSeconds).coerceAtLeast(1)
    }

    fun epochSeed(base: Long, index: Int, epoch: Int, reassignments: Int): Long =
        base xor ((index + 1L) * -7046029254386353131L) xor
            ((epoch + 1L) * 0x2545F4914F6CDD1DL) xor
            ((reassignments + 1L) * 0x369DEA0F31A53F85L)

    /**
     * [3.308.0] 役割巡回に入る前の**初期配置**であることを名前で示す（値は `assignmentFor(index, 0)` と同値）。
     * [3.409.21] かつては既定OFFの停滞脱出制御器（StagnationEscapeController・3.306.0）の入口も兼ねたが、
     * 単体 A/B（15ペア ON5/OFF10＝2度目の中立）を根拠に制御器ごと削除した。
     */
    fun initialAssignmentFor(index: Int): HypothesisEpochAssignment = assignmentFor(index, 0)

    fun roleLabel(assignment: HypothesisEpochAssignment): String =
        "${assignment.role.name}/x${assignment.intensity}"
}
