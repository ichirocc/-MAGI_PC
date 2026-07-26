package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * HF63 の忠実移植検証。Web `HF63.runSelfTest()` の判定値（A=6250 / B=50000 / C=2500 / D=50000）を
 * そのまま再現して固定する。
 */
class Hf63InfeasibilityTest {

    @Test
    fun scenarioA_hardCovU_deprioritizedAfterStall() {
        val hf = Hf63Infeasibility()
        hf.update(8, 24, 0)        // CovU(HARD) 初回: best=24
        hf.update(8, 24, 5000)     // 5000 iter 改善なし → infeasible 判定
        assertTrue(hf.isInfeasibleLikely(8))
        assertEquals(6250, hf.maxLam(8))   // 50000 / 8
    }

    @Test
    fun scenarioB_selfCorrectionRestoresFullLambda() {
        val hf = Hf63Infeasibility()
        hf.update(8, 24, 0)
        hf.update(8, 24, 5000)             // deprioritize
        hf.update(8, 10, 12000)            // 改善検出 → フラグ解除
        assertFalse(hf.isInfeasibleLikely(8))
        assertEquals(50000, hf.maxLam(8))  // 平常復帰
    }

    @Test
    fun scenarioC_softLimMax_deprioritized() {
        val hf = Hf63Infeasibility()
        hf.update(12, 18, 0)               // LimMax(SOFT) best=18
        hf.update(12, 18, 5000)            // stall → infeasible
        assertTrue(hf.isInfeasibleLikely(12))
        assertEquals(2500, hf.maxLam(12))  // 10000 / 4
    }

    @Test
    fun scenarioD_activeConstraintNeverFlagged() {
        val hf = Hf63Infeasibility()
        for (i in 0 until 10000) {
            hf.update(3, 100 - i / 100, i)  // C3n が改善し続ける
        }
        assertFalse(hf.isInfeasibleLikely(3))
        assertEquals(50000, hf.maxLam(3))   // HARD 平常維持
    }

    @Test
    fun sentinelSkipsUntrackedAndBreakdownMapsFamilies() {
        val hf = Hf63Infeasibility()
        // covU=8 を停滞させ、c3n=3 は SENTINEL でスキップ
        val arr = IntArray(Hf63Infeasibility.N_CONSTRAINTS) { Hf63Infeasibility.SENTINEL }
        arr[8] = 5
        hf.updateBatch(arr, 0)
        hf.updateBatch(arr, 5000)
        assertTrue(hf.isInfeasibleLikely(8))
        assertFalse(hf.isInfeasibleLikely(3))   // SENTINEL のため未追跡

        // breakdown 経由でも covU が追える
        val hf2 = Hf63Infeasibility()
        hf2.updateFromBreakdown(mapOf("covU" to 7), 0)
        hf2.updateFromBreakdown(mapOf("covU" to 7), 5000)
        assertTrue(hf2.isInfeasibleLikely(8))
        assertEquals(listOf("CovU"), hf2.infeasibleFamilies())
    }

    @Test
    fun infeasibleBreakdownKeysForFocusAvoidance() {
        val hf = Hf63Infeasibility()
        // covU と pref を停滞させる
        hf.updateFromBreakdown(mapOf("covU" to 3, "pref" to 2, "c1" to 5), 0)
        hf.updateFromBreakdown(mapOf("covU" to 3, "pref" to 2, "c1" to 1), 5000) // c1 は改善
        val avoid = hf.infeasibleBreakdownKeys()
        assertTrue("covU" in avoid)
        assertTrue("pref" in avoid)
        assertFalse("c1" in avoid)   // 改善中の族は回避しない
    }

    @Test
    fun weightFactorReflectsDeprioritization() {
        val hf = Hf63Infeasibility()
        assertEquals(1.0, hf.weightFactor(8), 1e-9)   // 平常
        hf.update(8, 3, 0); hf.update(8, 3, 5000)
        assertEquals(0.125, hf.weightFactor(8), 1e-9) // HARD infeasible → 1/8
    }
    // ---- [レビュー#5 3.213.0] focus 投入量ベースの更新（updateFromBreakdownFocused）----

    @Test
    fun focusedUpdateFlagsOnlyTheFocusedFamilyAfterStall() {
        val hf = Hf63Infeasibility()
        val bd = mapOf("covU" to 3, "c3n" to 1, "c1" to 5)
        hf.updateFromBreakdownFocused(bd, null, 1800)      // round0 頭（前ラウンド無し）＝ベースライン記録のみ
        hf.updateFromBreakdownFocused(bd, "covU", 1800)    // covU へ focus 投入（無改善 +1800）
        hf.updateFromBreakdownFocused(bd, "covU", 1800)    // +3600
        assertFalse(hf.isInfeasibleLikely(8))              // 5000 未満はまだ flag しない
        hf.updateFromBreakdownFocused(bd, "covU", 1800)    // +5400 >= 5000 → flag
        assertTrue(hf.isInfeasibleLikely(8))
        // 一度も focus していない族は、値が不減でも「不能」と推定しない（旧実装との差分の核心）
        assertFalse(hf.isInfeasibleLikely(3))   // c3n
        assertFalse(hf.isInfeasibleLikely(0))   // c1
    }

    @Test
    fun focusedUpdateImprovementResetsStallAndFlag() {
        val hf = Hf63Infeasibility()
        hf.updateFromBreakdownFocused(mapOf("covU" to 3), null, 1800)
        repeat(3) { hf.updateFromBreakdownFocused(mapOf("covU" to 3), "covU", 1800) }
        assertTrue(hf.isInfeasibleLikely(8))
        hf.updateFromBreakdownFocused(mapOf("covU" to 2), "covU", 1800)   // 改善 → self-correction＋stall リセット
        assertFalse(hf.isInfeasibleLikely(8))
        repeat(2) { hf.updateFromBreakdownFocused(mapOf("covU" to 2), "covU", 1800) }
        assertFalse(hf.isInfeasibleLikely(8))   // リセット後は再び約3ラウンドの focus 無改善を要する
        hf.updateFromBreakdownFocused(mapOf("covU" to 2), "covU", 1800)
        assertTrue(hf.isInfeasibleLikely(8))
    }

    // ==== [3.281.0/停滞レビューB] エポック横断共有の意味論 ====

    @Test
    fun sharedInstanceAccumulatesFocusedStallAcrossShortEpochs() {
        // 適応ポートフォリオの短いエポック(rounds=2→effortIters=2500/round)の runRsi 実挙動を忠実に再現:
        //   各ラウンド頭に updateFromBreakdownFocused(breakdown, lastFocus, 2500) — round1頭は lastFocus=null
        //   （前ラウンド無し）のため停滞加算なし、round2頭で初めて +2500。よって1エポックの加算は2500のみ
        //   ＝ threshold(5000) には**1エポックでは絶対に届かず**、エポックごと新規インスタンス（旧実装）では
        //   何十エポック繰り返しても永久に deprioritize されない（実機ログ: 67エポックが毎回 c3n へ突撃）。
        fun simulateEpoch(hf: Hf63Infeasibility) {
            hf.updateFromBreakdownFocused(mapOf("c3n" to 1), null, 2500)     // round1頭（lastFocus=null）
            hf.updateFromBreakdownFocused(mapOf("c3n" to 1), "c3n", 2500)    // round2頭（round1のfocus=c3n）
        }
        val perEpoch = Hf63Infeasibility()
        simulateEpoch(perEpoch)
        assertFalse("旧実装: 1エポックの学習(2500)では threshold(5000) に届かない＝破棄で永久に学習しない",
            perEpoch.isInfeasibleLikely(3))

        val shared = Hf63Infeasibility()
        simulateEpoch(shared)   // エポック1（旧実装がここで捨てていた学習=2500）
        simulateEpoch(shared)   // エポック2: 累積5000 → 到達
        assertTrue("共有インスタンスなら2エポック目で deprioritize が成立し以後の focus 選択に効く",
            shared.isInfeasibleLikely(3))
    }

    @Test
    fun perturbationBounceDoesNotResetSharedLearning() {
        // エポック間の摂動で族の件数が一時的に増えても（1→3→1）、gBestCurV は全期間min のため
        //   self-correction は誤発火せず、停滞学習は保持される（クロスエポック共有の健全性の根拠）。
        val hf = Hf63Infeasibility()
        hf.updateFromBreakdownFocused(mapOf("c3n" to 1), "c3n", 2500)
        hf.updateFromBreakdownFocused(mapOf("c3n" to 3), "c3n", 2500)   // 摂動で悪化＝改善ではない
        hf.updateFromBreakdownFocused(mapOf("c3n" to 1), "c3n", 2500)   // 元に戻る＝全期間minと同値＝改善ではない
        assertTrue("摂動の往復では学習がリセットされない", hf.isInfeasibleLikely(3))
        // 真の改善（全期間minの更新=1→0）では正しく self-correction する。
        hf.updateFromBreakdownFocused(mapOf("c3n" to 0), "c3n", 2500)
        assertFalse("真の改善では解除される", hf.isInfeasibleLikely(3))
    }
}
