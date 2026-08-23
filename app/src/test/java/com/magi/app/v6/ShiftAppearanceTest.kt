package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.393.0] 旧 `V6WebCompatTest` のうち、**本アプリが実際に使う4関数**を検証していた分を引き継いだもの。
 * Web専用だった検証（colLetter/popcnt32/historyReducer/buildWorkbook/Web側診断ビルダ）は対象ごと撤去した。
 */
class ShiftAppearanceTest {

    @Test fun severityFollowsTheWeightHierarchy() {
        // HARD 4族は CRITICAL、重いソフト(low90/high45/c3mn15)は HIGH、整え(fair/weekly)は INFO。
        for (k in listOf("groupViol", "covU", "pref", "c3n")) assertEquals(k, "CRITICAL", ShiftAppearance.severityFromVioKey(k))
        for (k in listOf("low", "high", "c3mn")) assertEquals(k, "HIGH", ShiftAppearance.severityFromVioKey(k))
        for (k in listOf("fair", "weekly")) assertEquals(k, "INFO", ShiftAppearance.severityFromVioKey(k))
        assertEquals("WARN", ShiftAppearance.severityFromVioKey("c1"))
        // 表示側は "vio-" 接頭辞つきのクラス名で引く。
        assertEquals("CRITICAL", ShiftAppearance.severityFromVioKey("vio-covU"))
        // 未知キーは INFO へ倒す（新族を足しても画面が落ちない）。
        assertEquals("INFO", ShiftAppearance.severityFromVioKey("no-such-family"))
    }

    /**
     * [3.417.0] 色は「利用者の明示色 → 一覧上の位置」だけで決まり、記号・名称からは何も推測しない。
     * 記号を引数に取らない形にしたので、この不変条件は**シグネチャで構造的に保証**される
     * （文字列を渡す余地が無い＝将来また字面で分岐する実装へ戻れない）。
     */
    @Test fun colorResolutionUsesOnlyExplicitColorOrPosition() {
        assertEquals("#123456", ShiftAppearance.resolveShiftColor(explicit = "#123456", index = 3))
        // 隣接する index は異なる色（同じ色に潰れないことがパレットの目的）。
        assertNotEquals(ShiftAppearance.resolveShiftColor(index = 0), ShiftAppearance.resolveShiftColor(index = 1))
        // 位置が不明なときはどのシフトでも同じ中立色＝記号による優劣を持たない。
        assertEquals(ShiftAppearance.NEUTRAL_SHIFT_COLOR, ShiftAppearance.resolveShiftColor())
    }

    @Test fun textColorIsTheHigherContrastOfTheTwoInkColors() {
        assertEquals("#14110d", ShiftAppearance.pickTextColor("#ffffff"))   // 明るい地には黒
        assertEquals("#fbf4e8", ShiftAppearance.pickTextColor("#000000"))   // 暗い地には生成り
        assertEquals("#14110d", ShiftAppearance.pickTextColor("こわれた値"))  // 解釈できなければ黒へ倒す
        // パレットは中間色ぞろいなので、どの色にも「黒か生成りのどちらか」が返る（未定義の色を返さない）。
        for (i in 0 until 16) {
            val ink = ShiftAppearance.pickTextColor(ShiftAppearance.resolveShiftColor(index = i))
            assertTrue(ink, ink == "#14110d" || ink == "#fbf4e8")
        }
    }
}
