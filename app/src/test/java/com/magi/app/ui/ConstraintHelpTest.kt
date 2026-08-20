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

    // [3.409.18] ペア禁止の「向き」の説明（違うシフトの組は鏡の2行が必要・同じシフトは1行でよい）。
    //   実機でまさにこの点を聞き返された＝画面で答えられるべき内容なので、落とすと失敗する形で固定。
    @Test
    fun pairBanHelpExplainsDirectionality() {
        for (k in listOf("cons42", "cons42s")) {
            val v = constraintHelp.getValue(k)
            assertTrue("$k が向き（逆向きにもう1行）を説明していません", "逆向き" in v)
            assertTrue("$k が同一シフト1行の規則を説明していません", "1行で両方向" in v)
        }
    }

    // [3.409.20/ユーザー指示「C41,C42,C41s,C42sは別々の制約です。別々の説明も明確に分かるように」]
    //   4族はエンジンでも別々（cons41/42=勤務グループ所属 sgrp・cons41s/42s=スキル割当 ssk）。
    //   説明が近い写しに戻ると区別が読めなくなるため、①どの分類で数えるか ②兄弟制約を名指しして
    //   「別の制約」と言うこと、を落とすと失敗する形で固定する。
    @Test
    fun groupAndSkillFamiliesAreExplicitlyDistinguished() {
        for (k in listOf("cons41", "cons42")) {
            val v = constraintHelp.getValue(k)
            assertTrue("$k が勤務グループ所属で数えることを言っていません", "勤務グループの所属だけを見" in v)
            assertTrue("$k が兄弟のスキル群制約と別物だと言っていません", "別の制約" in v && "スキル群" in v)
        }
        for (k in listOf("cons41s", "cons42s")) {
            val v = constraintHelp.getValue(k)
            assertTrue("$k がスキル割当で数えることを言っていません", "スキル割当だけを見" in v)
            assertTrue("$k が兄弟の群制約と別物だと言っていません", "別の制約" in v)
            assertTrue("$k が担当とは独立の分類だと言っていません", "独立" in v)
        }
    }
}
