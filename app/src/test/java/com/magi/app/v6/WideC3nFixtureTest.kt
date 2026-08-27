package com.magi.app.v6

import com.magi.app.model.StateParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.449.0/wideC3nBreakDays 再測定] ユーザー提供の実機ログ（2026-09データ、8回中5回が
 * `禁止連続の崩し範囲=ON` を実稼働・`AdaptiveBlockSwap` の不採用内訳が c3n 主因）を受け、
 * その基となる state.json（ユーザーが直接アップロード）を捕捉済みfixtureとして初めて repo へ置く。
 *
 * 「4件目以降の実データが手に入ったら再測定する」（docs/algorithm_portfolio.md 見直しの条件）を
 * 満たす初の real fixture。ホストJVM上で 240s×6ペアの A/B を実施し、ON 2勝・OFF 3勝・引分1
 * （weighted平均差 0.5%）＝符号がはっきりしない＝**既定OFF据え置き**という結論に使った。
 *
 * fixture は職員名のみ 職員A..J へ、group の name/kigou も実姓（大島/岡田/桒澤/吉江/古泉）が
 * 混入していたため G0..G9 へ匿名化（cons42 の g1Kigou/g2Kigou も同時に付け替え）。匿名化前後で
 * checker の hard/total/weighted/全19族 breakdown が bit 一致することをホスト実行で確認済み
 * （hard=0 total=435 weighted=3140.0＝covO重み1.0 時点の値）。
 * [2026-08-27] covO 1.0→5.0（HF77明示指示）。covO 23件ぶん weighted +92（=23*(5-1)）＝3140.0→3232.0。
 *   hard/total は covO が SOFT のみのため不変。
 */
class WideC3nFixtureTest {
    private fun load() = StateParser.parse(
        javaClass.getResourceAsStream("/sept2026_state.json")!!.bufferedReader().readText())!!

    @Test
    fun fixtureShapeAndBaselineEval() {
        val st = load()
        assertEquals(10, st.staff.size)
        assertEquals(10, st.shifts.size)
        assertEquals(10, st.groups.size)
        assertEquals("2026-09-01", st.startDate)
        assertEquals("2026-09-30", st.endDate)
        val sched = Array(st.schedule.size) { i -> st.schedule[i].toIntArray() }
        val rep = UnifiedViolationChecker.check(st, sched)
        assertEquals(0, rep.hard)
        assertEquals(435, rep.total)
        assertEquals(3232.0, rep.weightedScore, 1e-9)
    }

    @Test
    fun fixtureIsAnonymized() {
        val st = load()
        for (s in st.staff) {
            assertTrue("職員名が匿名化規約（職員A..J）に従っていません: ${s.name}", s.name.startsWith("職員"))
        }
        for (g in st.groups) {
            assertTrue("グループ名/記号が匿名化規約(グループN/GN)に従っていません: ${g.name}/${g.kigou}",
                g.name.startsWith("グループ") && g.kigou.matches(Regex("G\\d+")))
        }
    }
}
