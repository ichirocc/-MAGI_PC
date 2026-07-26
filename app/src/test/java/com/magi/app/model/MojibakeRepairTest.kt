package com.magi.app.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MojibakeRepairTest {

    /** UTF-8 を Latin-1 として読んでしまった「二重エンコード」状態を再現する。 */
    private fun doubleEncode(s: String): String =
        String(s.toByteArray(Charsets.UTF_8), Charsets.ISO_8859_1)

    @Test
    fun repairsDoubleEncodedJapaneseName() {
        val original = "古泉 健一"
        assertEquals(original, MojibakeRepair.repair(doubleEncode(original)))
    }

    @Test
    fun repairsDoubleEncodedWithinJson() {
        val json = """{"staff":[{"name":"山本 昌幸","groupIdx":0}],"shifts":[{"kigou":"夜勤"}]}"""
        assertEquals(json, MojibakeRepair.repair(doubleEncode(json)))
    }

    @Test
    fun leavesCleanJapaneseUntouched() {
        val s = "古泉 健一 ・ 夜勤 ・ 休"
        assertEquals(s, MojibakeRepair.repair(s))
        assertFalse(MojibakeRepair.looksMojibake(s))
    }

    @Test
    fun leavesAsciiUntouched() {
        val s = """{"name":"A4","count":12}"""
        assertEquals(s, MojibakeRepair.repair(s))
    }

    @Test
    fun detectsMojibake() {
        assertTrue(MojibakeRepair.looksMojibake(doubleEncode("佐藤 美和")))
    }

    @Test
    fun idempotentOnRepairedText() {
        val original = "鈴木 隆"
        val once = MojibakeRepair.repair(doubleEncode(original))
        assertEquals(once, MojibakeRepair.repair(once)) // 既に正常 → 二重適用しても不変
    }

    // ==== [3.282.0/新領域ログ監査] BOM除去だけの健全なファイルを「二重エンコード修復」と誤警告しない ====

    @Test
    fun bomOnlyStripIsNotReportedAsDecoded() {
        // Windows系エディタ由来の BOM付き・健全な UTF-8 JSON。repair は BOM を除去した新 String を返すが、
        // これは「二重エンコードの復号」ではない＝wasDecoded は false（旧: 参照比較 `!==` のため毎回
        // 「文字化けを自動修復」という誤った警告が出ていた）。
        val clean = """{"staff":[{"name":"古泉 健一"}]}"""
        val withBom = "\uFEFF" + clean
        val repaired = MojibakeRepair.repair(withBom)
        assertEquals("BOMは除去される", clean, repaired)
        assertFalse("BOM除去だけでは『修復』と報告しない", MojibakeRepair.wasDecoded(withBom, repaired))
    }

    @Test
    fun realDecodeIsReportedAsDecoded() {
        val garbled = doubleEncode("佐藤 美和")
        val repaired = MojibakeRepair.repair(garbled)
        assertTrue("本当に復号したときは true", MojibakeRepair.wasDecoded(garbled, repaired))
    }

    @Test
    fun bomPlusDoubleEncodeIsReportedAsDecoded() {
        val garbled = "\uFEFF" + doubleEncode("山本 昌幸")
        val repaired = MojibakeRepair.repair(garbled)
        assertEquals("山本 昌幸", repaired)
        assertTrue("BOM付きでも復号があれば true", MojibakeRepair.wasDecoded(garbled, repaired))
    }
}
