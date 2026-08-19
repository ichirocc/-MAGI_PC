package com.magi.app.v6

import com.magi.app.model.MagiState

/**
 * [C1 Repair Operators (façade) / 3.275.0] 散在していた C1 修復オペレータを図の1層として集約する薄い門。
 * 各メソッドは既存実装へ **1:1 委譲**（順序・引数・採否を一切変えず＝挙動完全同一）。狙いは
 * 「どこに C1 オペレータがあるか」を1箇所へまとめ、C1RepairIndex/C1DeltaPrefilter を共有の前段として
 * 噛ませること。最終採否は各オペレータ内の UnifiedViolationChecker + isBetter + keep-best のまま
 * ＝スコアリング不変・退化不能。
 *
 * [3.254.0 の教訓との整合] 外部提示の「オペレータを再 dispatch する統合(UnifiedC1Polish)」は退行のため
 *   不採用にした。本 façade は**再 dispatch しない**（委譲のみ）ため、その教訓に反しない。
 *
 * 図の対応:
 *   selfRelocateAndSameDaySwap = 自己内移設 + 同日 coverage保存 swap/permutation（手A/R1/R2/R3）
 *   temporalFlow               = Temporal DP + FlexibleDayFlow
 *   wideBeam                   = 広域時空間ビーム
 *   exactWindow                = 厳密窓修復（coverage保存 permutation 分枝限定）
 *   jointLns                   = Joint LNS（c1 + covU/range-low を同一 goal pool で）
 */
internal object C1RepairOperators {

    // ---- 共有の読取専用前段（Index / Prefilter） ----

    /** 盤面に c1修復の余地（不足窓）があるか。無ければ全 c1オペレータは no-op（安全にスキップ可）。 */
    fun hasActionableC1(p: Problem, schedule: Array<IntArray>): Boolean =
        C1DeltaPrefilter.hasActionableC1(C1RepairIndex.build(p, schedule))

    /** A4: coverage入替でも解消不能と証明された窓（診断）。 */
    fun provenWalls(p: Problem, schedule: Array<IntArray>): List<C1RepairAnalysis.CoverageNeutralWall> =
        C1RepairAnalysis.provenWalls(p, schedule)

    // ---- オペレータ（既存実装への 1:1 委譲） ----

    /** 自己内移設 + 同日 coverage保存 swap/permutation（手A/R1/R2/R3）。 */
    fun selfRelocateAndSameDaySwap(
        state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3,
        shouldStop: () -> Boolean = { false }, seed: Long = 0x1C1L,
    ): V6HotfixPasses.CyclicSwapResult =
        V6HotfixPasses.applyC1WindowPolish(state, schedule, maxPasses, shouldStop, seed)

    /** Temporal DP + FlexibleDayFlow。 */
    fun temporalFlow(
        state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 2, maxRelocations: Int = 4,
        trials: Int = 4, shouldStop: () -> Boolean = { false }, seed: Long = 0xC1F10FL,
    ): V6HotfixPasses.CyclicSwapResult =
        C1TemporalFlowPolish.apply(state, schedule, maxPasses, maxRelocations, trials, shouldStop, seed)

    /** 広域時空間ビーム。 */
    fun wideBeam(
        state: MagiState, schedule: Array<IntArray>, beamWidth: Int = 16, maxSteps: Int = 60,
        shouldStop: () -> Boolean = { false }, seed: Long = 0x1CBEAL,
    ): V6HotfixPasses.CyclicSwapResult =
        V6HotfixPasses.applyC1BeamPolish(state, schedule, beamWidth, maxSteps, shouldStop, seed)

    /** 厳密窓修復（coverage保存 permutation の分枝限定探索）。 */
    fun exactWindow(
        state: MagiState, schedule: Array<IntArray>, cfg: C1RepairAnalysis.Config = C1RepairAnalysis.Config(),
        shouldStop: () -> Boolean = { false },
    ): V6HotfixPasses.CyclicSwapResult =
        V6HotfixPasses.applyC1ExactWindowRepair(state, schedule, cfg, shouldStop)

    /** [3.276.0] index駆動の候補生成＋prefilter選別＋玉突き連鎖のC1修復（Index/Prefilterを実駆動する経路）。 */
    fun indexChainRepair(
        state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 2,
        shouldStop: () -> Boolean = { false }, seed: Long = 0x1C1D2L,
    ): V6HotfixPasses.CyclicSwapResult =
        V6HotfixPasses.applyC1IndexChainRepair(state, schedule, maxPasses, shouldStop, seed)

    /** Joint LNS（c1 + covU/range-low を同一 goal pool で）。 */
    fun jointLns(
        state: MagiState, schedule: Array<IntArray>, config: C1JointLnsPolish.Config = C1JointLnsPolish.Config(),
        shouldStop: () -> Boolean = { false }, seed: Long = 0xC1A11L,
    ): V6HotfixPasses.CyclicSwapResult =
        C1JointLnsPolish.apply(state, schedule, config, shouldStop, seed)
}
