package com.magi.app.ui

import com.magi.app.model.MagiState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.409.14] 制約の「詳しい説明」（constraintHelp）が、制約10族と**過不足なく**一致することを固定する。
 * 単一ソースは MagiState の cons* フィールド（族を足すなら必ずここに生える）＝Java リフレクションで
 * 引くので、族を1つ足して説明を書き忘れると（または綴りを誤ると）このテストが落ちる。
 * BreakdownLabelsTest（3.409.7）と同じ型の防具。
 */
class ConstraintHelpTest {
    private fun consFamilies(): Set<String> =
        MagiState::class.java.declaredFields
            .map { it.name }
            .filter { it.startsWith("cons") }
            .toSet()

    @Test
    fun everyConstraintFamilyHasHelp() {
        assertEquals(consFamilies(), constraintHelp.keys)
    }

    @Test
    fun helpIsSubstantiveJapanese() {
        for ((k, v) in constraintHelp) {
            assertTrue("$k の説明が短すぎます", v.length >= 40)
            assertTrue("$k の説明が ASCII だけ＝内部キーの貼り付け漏れの疑い", v.any { it.code > 0x7F })
            // 必須/任意の区別を必ず言う（利用者が優先度を誤解しないため）。
            assertTrue("$k が必須かどうかを言っていません", "必須条件" in v || "できるだけ守る" in v)
        }
        // 禁止の並び（cons3n）だけが必須＝MirrorKeys.hard のうち利用者がこの画面で登録できる唯一の族。
        assertTrue("必須条件" in constraintHelp.getValue("cons3n"))
        assertEquals(1, constraintHelp.values.count { "必須条件＝" in it })
    }
}
