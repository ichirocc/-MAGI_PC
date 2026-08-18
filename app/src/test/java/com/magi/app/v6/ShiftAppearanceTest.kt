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

    @Test fun shiftCategoryIsInferredFromSymbolAndName() {
        assertEquals("rest", ShiftAppearance.shiftCatDefault("休"))
        assertEquals("night", ShiftAppearance.shiftCatDefault("Dﾃ", "夜勤"))
        assertEquals("unknown", ShiftAppearance.shiftCatDefault(""))
    }

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

    @Test fun colorResolutionPrefersExplicitThenRestThenPaletteByIndex() {
        assertEquals("#123456", ShiftAppearance.resolveShiftColor("Dﾃ", index = 3, explicit = "#123456"))
        assertEquals("#A7B4C2", ShiftAppearance.resolveShiftColor("休", index = 3))   // 休は index より優先
        // 隣接する index は異なる色（同カテゴリの色潰れを防ぐのが目的）。
        assertNotEquals(ShiftAppearance.resolveShiftColor("A", index = 0), ShiftAppearance.resolveShiftColor("B", index = 1))
    }

    @Test fun textColorIsTheHigherContrastOfTheTwoInkColors() {
        assertEquals("#14110d", ShiftAppearance.pickTextColor("#ffffff"))   // 明るい地には黒
        assertEquals("#fbf4e8", ShiftAppearance.pickTextColor("#000000"))   // 暗い地には生成り
        assertEquals("#14110d", ShiftAppearance.pickTextColor("こわれた値"))  // 解釈できなければ黒へ倒す
        // パレットは中間色ぞろいなので、どの色にも「黒か生成りのどちらか」が返る（未定義の色を返さない）。
        for (i in 0 until 16) {
            val ink = ShiftAppearance.pickTextColor(ShiftAppearance.resolveShiftColor("A", index = i))
            assertTrue(ink, ink == "#14110d" || ink == "#fbf4e8")
        }
    }
}
