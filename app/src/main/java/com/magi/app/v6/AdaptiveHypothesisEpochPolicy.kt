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
     * 前の役割が稼いだ改善で 35→45 秒（RSI+）へ昇格するのは根拠が無い。既定経路は
     * 再配属分岐で `improvedPrevious=false` として元からこの契約を守っていたが、
     * 3.306.0 で足した制御器経路だけが `improvedThisEpoch` をそのまま引き継いでいた。
     * 契約に名前を付けて両経路から呼ぶ（インライン式のままだと再び片側だけずれる）。
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
     * [3.306.0/既定OFF経路] 役割を選ばずに index だけで初期配置を決める。
     * [StagnationEscapeController] を使うとき、最初の1エポックはフィードバックが存在しないため
     * 従来と同じ6役割の分散配置から始める（`assignmentFor(index, 0)` と同値）。
     */
    fun initialAssignmentFor(index: Int): HypothesisEpochAssignment = assignmentFor(index, 0)

    /**
     * [3.306.0/既定OFF経路] コントローラが選んだ役割から具体的な割当を組む。
     * [escapeDepth] は**自己エリートが改善しなかった連続エポック数**であって、役割変更の回数ではない。
     * 役割を替えても「それまでの手が全部失敗した」という証拠を消さないのがこの経路の要点
     * （既定経路の `shouldReassign` は再配属のたびに停滞カウンタを 0 に戻す）。
     */
    fun assignmentFor(role: HypothesisEpochRole, escapeDepth: Int): HypothesisEpochAssignment =
        HypothesisEpochAssignment(
            role = role,
            algorithm = algorithmFor(role),
            intensity = intensityFor(role, escapeDepth),
        )

    /** [3.306.0/既定OFF経路] 連続の非改善エポック数。役割の再配属では減らさない。 */
    fun nextPlateauDepth(previous: Int, improvedThisEpoch: Boolean): Int =
        if (improvedThisEpoch) 0 else if (previous == Int.MAX_VALUE) previous else previous + 1

    fun roleLabel(assignment: HypothesisEpochAssignment): String =
        "${assignment.role.name}/x${assignment.intensity}"
}


/* ------------------------------------------------------------------------------------------------
 * [3.306.0] 残差ベースの4段停滞脱出（既定 OFF）。
 *
 * 既定経路（`assignmentFor(index, reassignments)` ＋ `shouldReassign`）は盤面を一切見ず、再配属の
 * 回数だけで6役割を機械的に巡回し、再配属のたびに停滞カウンタを 0 へ戻す。下の制御器は
 * 「いま残っている違反の種類」と「他仮説との距離」から次の役割を選び、停滞の深さを役割変更でも
 * 保持する。設計としてはこちらが筋が通っているが、実データ3件×4回の A/B では有意な差を検出できず
 * （範囲が完全に重なり中央値の大小も一貫しない）、既定は OFF のまま温存する
 * （`PolishGate.adaptiveEscapeControl`）。詳細は CLAUDE.md の 3.306.0 節。
 * ---------------------------------------------------------------------------------------------- */

/**
 * Compact bit flags for the information that matters at an epoch boundary.  A mask keeps the
 * controller's decision table explicit and makes all signal combinations easy to audit.
 */
internal object StagnationEscapePressure {
    const val HARD = 1 shl 0
    const val TEMPORAL = 1 shl 1
    const val PERSONAL = 1 shl 2
    const val COLLAPSED = 1 shl 3
    const val DISTINCT_PEER = 1 shl 4

    fun maskFor(
        report: ViolationReport,
        nearestOtherDistance: Int,
        hasOtherHypothesis: Boolean,
    ): Int {
        var mask = 0
        if (report.hard > 0) mask = mask or HARD
        if (has(report, "c1") || has(report, "c3") || has(report, "c3m") || has(report, "c3mn") ||
            has(report, "c3n") || has(report, "c41") || has(report, "c42") || has(report, "c41s") ||
            has(report, "c42s") || has(report, "covO")
        ) {
            mask = mask or TEMPORAL
        }
        if (has(report, "low") || has(report, "high") || has(report, "c2") || has(report, "apt") ||
            has(report, "fair") || has(report, "weekly")
        ) {
            mask = mask or PERSONAL
        }
        if (hasOtherHypothesis) {
            if (nearestOtherDistance <= AdaptiveHypothesisEpochPolicy.DUPLICATE_DISTANCE_CELLS) {
                mask = mask or COLLAPSED
            } else {
                mask = mask or DISTINCT_PEER
            }
        }
        return mask
    }

    fun contains(mask: Int, flag: Int): Boolean = (mask and flag) != 0

    private fun has(report: ViolationReport, family: String): Boolean =
        (report.breakdown[family] ?: 0) > 0
}

/**
 * Deterministic four-stage escape controller for one portfolio worker.
 *
 * A plateau is handled as a sequence, not as a random restart:
 *
 *  1. target the constraint family that still dominates;
 *  2. recombine a genuinely different elite when one exists;
 *  3. leave a collapsed basin with a maximum-distance or large-destroy move;
 *  4. use a deeper escape (HARD debt RSI+ or a second diversified move).
 *
 * An actual local-elite improvement resets the depth and therefore the intensity.  A role change
 * never does.  W0 remains the permanent baseline.  This controller allocates an existing search
 * role only; the official checker and [betterReport] remain the sole acceptance authority.
 */
internal class StagnationEscapeController(
    private val workerIndex: Int,
) {
    fun nextAssignment(
        current: HypothesisEpochAssignment,
        report: ViolationReport,
        plateauDepth: Int,
        nearestOtherDistance: Int,
        hasOtherHypothesis: Boolean,
    ): HypothesisEpochAssignment {
        val slot = Math.floorMod(workerIndex, 8)
        if (slot == 0) return AdaptiveHypothesisEpochPolicy.assignmentFor(
            HypothesisEpochRole.BASELINE_REFINE,
            escapeDepth = 0,
        )

        val pressure = StagnationEscapePressure.maskFor(
            report,
            nearestOtherDistance,
            hasOtherHypothesis,
        )
        val collapsed = StagnationEscapePressure.contains(pressure, StagnationEscapePressure.COLLAPSED)
        val depth = plateauDepth.coerceAtLeast(0)
        if (depth == 0 && !collapsed) {
            // A real local-elite improvement earns another precise epoch, but drops any old
            // escalation strength accumulated before that improvement.
            return AdaptiveHypothesisEpochPolicy.assignmentFor(current.role, escapeDepth = 0)
        }

        val escapeDepth = depth.coerceAtLeast(1)
        val role = chooseRole(current.role, pressure, escapeDepth, slot)
        return AdaptiveHypothesisEpochPolicy.assignmentFor(role, escapeDepth)
    }

    private fun chooseRole(
        current: HypothesisEpochRole,
        pressure: Int,
        depth: Int,
        slot: Int,
    ): HypothesisEpochRole {
        val hard = StagnationEscapePressure.contains(pressure, StagnationEscapePressure.HARD)
        val temporal = StagnationEscapePressure.contains(pressure, StagnationEscapePressure.TEMPORAL)
        val personal = StagnationEscapePressure.contains(pressure, StagnationEscapePressure.PERSONAL)
        val collapsed = StagnationEscapePressure.contains(pressure, StagnationEscapePressure.COLLAPSED)
        val hasDistinctPeer = StagnationEscapePressure.contains(pressure, StagnationEscapePressure.DISTINCT_PEER)

        val target = when {
            hard -> HypothesisEpochRole.HARD_FAMILY_RSI
            temporal -> HypothesisEpochRole.DAY_BLOCK_ALNS
            personal -> HypothesisEpochRole.PERSONAL_RSI
            else -> HypothesisEpochRole.LARGE_DESTROY_ALNS
        }
        val structural = if (hasDistinctPeer) {
            HypothesisEpochRole.ELITE_RELINK
        } else {
            HypothesisEpochRole.LARGE_DESTROY_ALNS
        }
        val diversify = HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS
        val deep = if (hard) {
            HypothesisEpochRole.HARD_DEBT_RSI_PLUS
        } else {
            HypothesisEpochRole.LARGE_DESTROY_ALNS
        }

        // A collapse is stronger evidence than a scalar improvement: two workers that converge
        // to the same board have stopped contributing independent search information.  Alternate
        // the two basin-leaving moves rather than retrying the same one.
        if (collapsed) return firstDifferent(
            current,
            diversify,
            HypothesisEpochRole.LARGE_DESTROY_ALNS,
            target,
            deep,
            structural,
        )

        // W4 begins as a second precision baseline.  Its first genuine plateau is reserved for
        // structural recombination, but only when a distinct peer actually exists.
        if (slot == 4 && depth == 1 && hasDistinctPeer) {
            return firstDifferent(current, HypothesisEpochRole.ELITE_RELINK, target, diversify, deep)
        }

        return when ((depth - 1) and 3) {
            0 -> firstDifferent(current, target, structural, diversify, deep)
            1 -> firstDifferent(current, structural, diversify, target, deep)
            2 -> firstDifferent(current, diversify, deep, target, structural)
            else -> firstDifferent(current, deep, target, structural, diversify)
        }
    }

    private fun firstDifferent(
        current: HypothesisEpochRole,
        vararg candidates: HypothesisEpochRole,
    ): HypothesisEpochRole = candidates.firstOrNull { it != current } ?: current
}
