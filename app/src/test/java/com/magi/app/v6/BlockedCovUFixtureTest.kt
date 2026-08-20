package com.magi.app.v6

import com.magi.app.model.StateParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.409.15/backlog#6 解消] 「covU が構造床を超えて、いまの希望・盤面では埋められない
 * （blocked-now）」形状の実データ fixture を初めて repo へ置き、その形状自体を固定する。
 *
 * 3.361.0/3.377.0 が繰り返し記録したギャップ＝golden は入力盤面 hard=0、sample_v6 の covU は
 * 解ける形で、**3.377.0 が直した残存分析の「もう直せない covU」判定（`allBlockedNow` 委譲）を
 * 実データ形状で exercise する fixture が repo に1つも無かった**。当時この分岐の検証は
 * 実機ログと合成盤面に頼っていた。
 *
 * fixture は 2026-08 実運用 state の匿名化版（職員名のみ 職員A..J へ置換・診断ログ配列は除去）。
 * 匿名化が評価に影響しないことは、匿名化前後で Evaluator/checker/床/診断の全数値が bit 一致する
 * ことをホスト実行で確認済み（eval hard=4 soft=1681 / covU=4 / floor=0 / blockedNowSlots=4）。
 */
class BlockedCovUFixtureTest {
    private fun load() = StateParser.parse(
        javaClass.getResourceAsStream("/blocked_covu_state.json")!!.bufferedReader().readText())!!

    @Test
    fun fixtureHasTheBlockedNowCovUShape() {
        val st = load()
        val sched = Array(st.schedule.size) { i -> st.schedule[i].toIntArray() }
        val rep = UnifiedViolationChecker.check(st, sched)
        // covU=4 が構造床(0)を**超えて**残っている＝供給床では説明できない不足。
        assertEquals(0, V6SanityPort.structuralHardFloor(st))
        assertEquals(4, rep.breakdown["covU"] ?: 0)
        // 3.344.0/3.377.0 の判定: 空き番なし・玉突き連鎖(findCovUChain)も不成立を実証済み＝
        // 「いまの希望・担当のままでは埋められない」。全4枠が blocked-now。
        val diag = V6PortAnalyzer.diagnoseCoverage(st, sched)
        assertEquals(4, diag.blockedNowSlots)
        assertTrue("blocked-now の枠が残っているのに allBlockedNow が偽", diag.allBlockedNow)
        // 恒久的な「データ上充足不可」とは別物（担当者数は足りている）＝3.263.0 の意図的な区別。
        assertEquals(0, diag.infeasibleSlots)
    }

    @Test
    fun fixtureIsAnonymized() {
        // 誰かが匿名化前の版へ差し替えたら落ちる（実名を public repo へ増やさないための防具）。
        val st = load()
        assertEquals(10, st.staff.size)
        for (s in st.staff) {
            assertTrue("職員名が匿名化規約（職員A..J）に従っていません: ${s.name}", s.name.startsWith("職員"))
        }
        assertNotNull(st)
    }
}
