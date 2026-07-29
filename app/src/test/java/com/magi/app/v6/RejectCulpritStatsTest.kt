package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.321.0] 研磨パスの不採用理由を分類して数える受け皿の検証。
 *
 * 旧実装は `worstWorsenedFamily` の結果だけを数えており、呼出側の受理条件
 * `isBetter(rep, best) && !exactPinRegression(...)` の**後半で落ちた候補**（＝厳密ピン破り。
 * `isBetter` 自体は true なので悪化族が存在しない）が `rejected` だけ増やして主因に何も残さず、
 * 「不採用N件(主因 …)」の N と内訳が合わなくなっていた。分類は `betterReport` の判定順
 * （hard → weightedScore → total）と厳密に一致していなければならない。
 */
class RejectCulpritStatsTest {

    /** hard / weightedScore / total を直接指定した合成レポート。 */
    private fun rep(hard: Int, weighted: Double, total: Int, breakdown: Map<String, Int> = emptyMap()) =
        ViolationReport(
            violations = emptyMap(), needViolations = emptyMap(), countViolations = emptyMap(),
            breakdown = breakdown, hard = hard, total = total, soft = total - hard,
            weightedScore = weighted,
        )

    @Test
    fun pinBreakIsCountedSeparatelyAndCarriesNoCulprit() {
        // ピン破りは「違反自体は改善している」ので悪化族を持たない。旧実装ではここが
        // rejected だけ増えて内訳が空になっていた。
        val s = RejectCulpritStats()
        s.record(after = rep(0, 10.0, 5), before = rep(0, 20.0, 8), pinBroken = true)
        assertEquals(1, s.rejected)
        assertEquals(1, s.pinBroken)
        assertEquals("スコア比較は行わない", 0, s.weightUp)
        assertTrue("内訳にピン破りが出る", s.summary().contains("ピン破り:1"))
    }

    @Test
    fun classificationFollowsTheAcceptanceOrder() {
        // hard → weightedScore → total の順。上位キーで決まったら下位は見ない。
        val s = RejectCulpritStats()
        s.record(rep(2, 10.0, 1), rep(1, 99.0, 99))   // hard 増（weighted/total は改善）
        s.record(rep(1, 50.0, 1), rep(1, 10.0, 99))   // hard 同値・weighted 増
        s.record(rep(1, 10.0, 9), rep(1, 10.0, 5))    // hard/weighted 同値・total 増
        s.record(rep(1, 10.0, 5), rep(1, 10.0, 5))    // すべて同値
        assertEquals(4, s.rejected)
        assertEquals(1, s.hardUp)
        assertEquals(1, s.weightUp)
        assertEquals(1, s.totalUp)
        assertEquals(1, s.same)
        assertEquals("ピン破りは0", 0, s.pinBroken)
    }

    @Test
    fun breakdownAlwaysSumsToRejected() {
        // 内訳の合計が総数と一致する＝「N件」と内訳が食い違わない、という不変条件。
        val s = RejectCulpritStats()
        s.record(rep(0, 1.0, 1), rep(0, 9.0, 9), pinBroken = true)
        s.record(rep(3, 1.0, 1), rep(1, 1.0, 1))
        s.record(rep(1, 9.0, 1), rep(1, 1.0, 1))
        s.record(rep(1, 1.0, 9), rep(1, 1.0, 1))
        s.record(rep(1, 1.0, 1), rep(1, 1.0, 1))
        assertEquals(s.rejected, s.pinBroken + s.hardUp + s.weightUp + s.totalUp + s.same)
    }

    @Test
    fun culpritFamilyIsReportedForScoreRejections() {
        // 悪化族が分かるときは主因として出す（重み付きで最も増えた族）。
        val s = RejectCulpritStats()
        s.record(
            after = rep(0, 200.0, 5, mapOf("low" to 2, "c1" to 1)),
            before = rep(0, 10.0, 5, mapOf("low" to 0, "c1" to 0)),
        )
        assertEquals(1, s.weightUp)
        assertTrue("重い族 low が主因として出る: ${s.summary()}", s.summary().contains("low:1"))
    }

    @Test
    fun emptyStatsProduceNoText() {
        assertEquals("", RejectCulpritStats().summary())
    }
}
