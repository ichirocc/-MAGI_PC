package com.magi.app.v6

/** How a parallel hypothesis obtains its initial board. */
internal enum class HypothesisStartMode {
    BASELINE,
    DAY_REPAIR,
    STAFF_REPAIR,
    MIXED_REPAIR,
}

internal data class HypothesisStartPlan(
    val mode: HypothesisStartMode,
    val intensity: Int,
)

/**
 * Deterministic role assignment for the parallel search portfolio.
 *
 * W0 and W4 keep the original board as safety/precision baselines. The other roles
 * start from structurally different destroy/repair basins. Algorithm assignment is
 * intentionally orthogonal to the start-board assignment.
 */
internal object HypothesisDiversityPolicy {
    fun startPlanFor(index: Int): HypothesisStartPlan = when (Math.floorMod(index, 8)) {
        0, 4 -> HypothesisStartPlan(HypothesisStartMode.BASELINE, 0)
        1 -> HypothesisStartPlan(HypothesisStartMode.DAY_REPAIR, 1)
        2 -> HypothesisStartPlan(HypothesisStartMode.STAFF_REPAIR, 1)
        3 -> HypothesisStartPlan(HypothesisStartMode.MIXED_REPAIR, 1)
        5 -> HypothesisStartPlan(HypothesisStartMode.DAY_REPAIR, 2)
        6 -> HypothesisStartPlan(HypothesisStartMode.STAFF_REPAIR, 2)
        else -> HypothesisStartPlan(HypothesisStartMode.MIXED_REPAIR, 2)
    }

    // [3.278.0/デッドコード除去] algorithmFor(index) は本番呼出0だった（実際のアルゴリズム割当は
    //   AdaptiveHypothesisEpochPolicy.algorithmFor が担う）。3.266.0 統合時の残滓のため撤去。

    /** Long AUTO runs use an actual heterogeneous portfolio instead of eight RSI++ clones. */
    // [3.284.0/外部レビュー] AUTO の二重分岐を解消: 旧 31-90秒帯は ALNS で、アプリ経路の
    //   V6FinalPort.optimizationPlan（31-210秒=RSI(2/3)→ALNS(1/3) の複合）と食い違い、直接APIだけ
    //   別アルゴリズムになっていた。単一アルゴリズムしか表現できない本関数では複合プランの主段= RSI
    //   （偶数ラウンドで内部的に ALNS も回る）へ寄せ、帯を 31-210=RSI に統一する。
    fun autoAlgorithmForBudget(budgetSec: Int): V6Algorithm = when {
        budgetSec <= 30 -> V6Algorithm.V5
        budgetSec <= 210 -> V6Algorithm.RSI
        else -> V6Algorithm.PORTFOLIO
    }

    /** Reservoir-sampling tie break: every tied candidate has equal probability. */
    fun takeReservoirTie(tieCount: Int, rng: java.util.Random): Boolean =
        tieCount > 0 && rng.nextInt(tieCount) == 0
}
