package com.magi.app.v6

/**
 * [HF63] Infeasibility-Aware Adaptive Deprioritization — faithful port of the Web V6
 * standalone module (`magi_v6_web.html`, const `HF63`).
 *
 * 目的（業務担当者の核心要望）: 「データ問題があっても最適化できるアルゴリズム」。
 * 各制約族の改善を追跡し、INFEAS_STALL_ITERS 反復のあいだ改善が無い族を
 * 「構造的に充足不能（infeasible-likely）」と推定して学習する。改善を検出したら解除（self-correction）。
 *
 * **この学習を何に使うか**（3.409.0 で実態へ訂正・3.409.10 で範囲を確定）:
 *  - `infeasibleBreakdownKeys()` が `runRsi` の `dynamicAvoid` になり、充足困難と学習した族を
 *    RSI の focus 候補から外す（3.184.0 で HARD のみに限定・3.281.0 でワーカー専属インスタンスを
 *    エポック横断で共有・3.213.0 で focus 投入量ベースの停滞加算へ）。
 *  - `infeasibleFamilies()` が `recordInfeasibleScoped` 経由で残存分析（3.288.0）へ供給する。
 *  - **目的関数の重みには一切触れない**。重みの変更は HF77 該当で、明示の数値指示と
 *    Evaluator/Delta/C++/checker の4面同時変更が要る。
 *
 * [3.409.10] **λ上限(penalty cap)の一式を撤去した**。移植元の VBA は制約ごとの Lagrange 乗数
 * `gLam(0..13)` を持ち、その上限を infeasible 族について縮小する設計だったが、**この Kotlin エンジンに
 * `gLam` は存在しない**（固定重み `MirrorKeys.weights` ＋ GLS penalty で動く）。よって `maxLam` は
 * 存在しない変数の上限を返し、`weightFactor` は「探索スコアへ掛ける係数」を名乗るが、3.213.0 以降の
 * スコアは `hard*1e9 + soft` の**辞書式パック Long** で、その一部の族だけに 0.125 を掛ける意味が無い。
 * ＝「まだ配線していない」ではなく**書かれたままでは配線できない**（3.393.0 で撤去した `c3RunMode` と
 * 同じ、配線すると静かに壊れる種類のスイッチ）。学習の半分だけを残す。
 */
class Hf63Infeasibility {
    companion object {
        const val INFEAS_STALL_ITERS = 5000        // 不可能性判定の iter 閾値
        const val N_CONSTRAINTS = 14
        val CNAMES = listOf(
            "C1", "C2", "C3", "C3n", "C3m", "C3mn", "C41", "C42",
            "CovU", "CovO", "Pref", "LimMin", "LimMax", "Apt",
        )
        /** UnifiedViolationChecker の breakdown キー → HF63 index（無いものは追跡しない）。 */
        val KEY_TO_INDEX: Map<String, Int> = mapOf(
            "c1" to 0, "c2" to 1, "c3" to 2, "c3n" to 3, "c3m" to 4, "c3mn" to 5,
            "c41" to 6, "c42" to 7, "covU" to 8, "covO" to 9, "pref" to 10,
            "low" to 11, "high" to 12,
        )
    }

    private val gBestCurV = IntArray(N_CONSTRAINTS) { Int.MAX_VALUE }
    private val gLastImproveIter = IntArray(N_CONSTRAINTS)
    private val gInfeasibleLikely = BooleanArray(N_CONSTRAINTS)
    // [レビュー#5 3.213.0] focus 投入量ベースの停滞累積（updateFromBreakdownFocused 用）。
    //   gIter 時計と独立に「実際に focus した無改善ラウンドの概算反復数」だけを族ごとに積む。
    private val gFocusedStall = IntArray(N_CONSTRAINTS)

    fun reset() {
        for (c in 0 until N_CONSTRAINTS) {
            gBestCurV[c] = Int.MAX_VALUE
            gLastImproveIter[c] = 0
            gInfeasibleLikely[c] = false
            gFocusedStall[c] = 0
        }
    }

    /** 制約 c の改善状況を追跡し、不可能性を判定する（VBA UpdateInfeasibilityState 等価）。 */
    fun update(c: Int, curV: Int, gIter: Int) {
        if (c !in 0 until N_CONSTRAINTS) return
        if (curV < gBestCurV[c]) {
            gBestCurV[c] = curV
            gLastImproveIter[c] = gIter
            if (gInfeasibleLikely[c]) gInfeasibleLikely[c] = false   // self-correction
        } else if (curV == 0) {
            // 一度でも充足(0)に到達した族は「不能」ではない。再違反に備え停滞カウンタを進めておく
            // （これを怠ると、0到達後に摂動で再違反した瞬間 gIter-旧改善 が即 STALL を超え、
            //  解けた族を誤って infeasible 判定し RSI focus から外してしまう）。
            gLastImproveIter[c] = gIter
        } else if (gIter - gLastImproveIter[c] >= INFEAS_STALL_ITERS) {
            // curV>0 かつ STALL 反復改善なし。focus 回避/診断のため deprioritize する。
            gInfeasibleLikely[c] = true                               // 構造的下限推定 → deprioritize
        }
    }

    /** UnifiedViolationChecker の breakdown から更新（族→indexへマップ）。
     *  注: 全族の停滞を無差別に加算する旧セマンティクス。focus の概念が無い呼出元
     *  （ViewModel の実行後警告など）用に温存。RSI の focus 選択には
     *  [updateFromBreakdownFocused] を使う（レビュー#5）。 */
    fun updateFromBreakdown(breakdown: Map<String, Int>, gIter: Int) {
        for ((key, idx) in KEY_TO_INDEX) {
            update(idx, breakdown[key] ?: 0, gIter)
        }
    }

    /** [レビュー#5 3.213.0] focus 投入量ベースの更新。実際に探索資源を投入した族（focusedKey=
     *  直前ラウンドの focus）だけ停滞を加算し、他族は改善/0到達の追跡（self-correction）のみ行う。
     *  旧 updateFromBreakdown は「covU に focus が張り付いている間、一度も focus されなかった族」まで
     *  約3ラウンドで infeasible 判定していた（3.184.0 は SOFT 側のみ avoid フィルタで緩和＝HARD 族の
     *  誤 deprioritize は残っていた）。本更新は HARD/SOFT を問わず「試していない族を不能と推定しない」。
     *  effortIters=そのラウンドに focus へ投入した概算反復数（RSI は 1800/round 換算＝既存の粒度補正と同じ）。 */
    fun updateFromBreakdownFocused(breakdown: Map<String, Int>, focusedKey: String?, effortIters: Int) {
        for ((key, idx) in KEY_TO_INDEX) {
            val curV = breakdown[key] ?: 0
            if (curV < gBestCurV[idx]) {
                gBestCurV[idx] = curV
                gFocusedStall[idx] = 0
                if (gInfeasibleLikely[idx]) gInfeasibleLikely[idx] = false   // self-correction
            } else if (curV == 0) {
                gFocusedStall[idx] = 0   // 充足済みの族は「不能」ではない（update() の 0 到達分岐と同義）
            } else if (key == focusedKey) {
                gFocusedStall[idx] += effortIters
                if (gFocusedStall[idx] >= INFEAS_STALL_ITERS) gInfeasibleLikely[idx] = true
            }
            // focus外かつ非改善: 何もしない＝停滞時計を進めない（探索資源を投入していないため）。
        }
    }

    fun isInfeasibleLikely(c: Int): Boolean = c in 0 until N_CONSTRAINTS && gInfeasibleLikely[c]

    /** 構造的に充足不能と推定された制約族名（診断/ログ用）。 */
    fun infeasibleFamilies(): List<String> =
        (0 until N_CONSTRAINTS).filter { gInfeasibleLikely[it] }.map { CNAMES[it] }

    /** infeasible-likely な族の breakdown キー集合（探索の focus 回避に使用）。 */
    fun infeasibleBreakdownKeys(): Set<String> =
        KEY_TO_INDEX.filterValues { gInfeasibleLikely[it] }.keys
}
