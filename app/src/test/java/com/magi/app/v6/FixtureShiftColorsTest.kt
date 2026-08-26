package com.magi.app.v6

import com.magi.app.model.StateParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.462.0/外部レビュー L-01] 3.455.0 が4つのテスト用サンプルデータへ `shiftColors`（記号→表示色の
 * 対応表）を追加した際、専用テストが無かった。将来の fixture 再生成・JSON整形で色が消えても、
 * エンジンのパリティ試験（`shiftColors` を読まない）だけでは検出できない。
 *
 * 固定する不変条件: ①各色が `#rrggbb` 形式 ②対応するキーが実在のシフト記号（誤記の記号を
 * 登録していない）③`ShiftAppearance.resolveShiftColor` が明示色をそのまま返す（表示側の契約）
 * ④`shiftColors` の有無で `StateFingerprint` が変わらない（3.455.0 が謳う「採点・探索は不変」の実測）。
 */
class FixtureShiftColorsTest {
    private val hexPattern = Regex("^#[0-9A-Fa-f]{6}$")

    private fun load(name: String) = StateParser.parse(
        javaClass.getResourceAsStream("/$name.json")!!.bufferedReader().readText())!!

    private val fixtures = listOf("golden_state", "sample_state_v6", "blocked_covu_state", "sept2026_state")

    @Test
    fun everyFixtureHasWellFormedShiftColors() {
        for (name in fixtures) {
            val st = load(name)
            assertTrue("$name: shiftColors が空です", st.shiftColors.isNotEmpty())
            val kigous = st.shifts.map { it.kigou }.toSet()
            for ((kigou, hex) in st.shiftColors) {
                assertTrue("$name: 色 $hex ($kigou) が #rrggbb 形式ではありません", hexPattern.matches(hex))
                assertTrue("$name: shiftColors のキー「$kigou」が実在のシフト記号にありません", kigou in kigous)
            }
        }
    }

    @Test
    fun resolveShiftColorReturnsTheExplicitFixtureColor() {
        for (name in fixtures) {
            val st = load(name)
            for ((kigou, hex) in st.shiftColors) {
                assertEquals("$name/$kigou", hex, ShiftAppearance.resolveShiftColor(explicit = hex, index = 0))
            }
        }
    }

    @Test
    fun shiftColorsDoNotAffectTheStateFingerprint() {
        for (name in fixtures) {
            val st = load(name)
            assertEquals(
                "$name: shiftColors の有無で StateFingerprint が変わってはいけません（表示専用フィールド）",
                StateFingerprint.of(st), StateFingerprint.of(st.copy(shiftColors = emptyMap())),
            )
        }
    }
}
