package com.magi.app.ui

import com.magi.app.v6.MirrorKeys
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.409.7] 族を追加したとき、画面に**生の英字キー**が出るのを防ぐ。
 *
 * `breakdownLabels` は10箇所から `breakdownLabels[key] ?: key` の形で引かれる。引けなければ
 * "c41s" のような内部キーがそのまま利用者の画面に出る＝`docs/operator_ux.md`「英字符号を画面に出さない」
 * に反するが、**例外も警告も出ない**ので実機で誰かが気づくまで分からない。
 *
 * ここで固定するのは「表と `MirrorKeys.all` が過不足なく一致する」という1つの不変条件と、
 * 「ラベルが空でなく、ASCII だけでもない（＝日本語になっている）」こと。
 * 3.72.0 で weekly が19族目として実際に追加された経緯があり、同じことは今後も起こる。
 */
class BreakdownLabelsTest {
    @Test
    fun everyFamilyHasALabel() {
        val missing = MirrorKeys.all.filterNot { breakdownLabels.containsKey(it) }
        assertTrue("日本語ラベルが無い族（画面に生キーが出る）: $missing", missing.isEmpty())
    }

    @Test
    fun noLabelForAFamilyThatDoesNotExist() {
        val extra = breakdownLabels.keys.filterNot { it in MirrorKeys.all }
        assertTrue("MirrorKeys.all に無い族のラベル（族名の綴り違い等）: $extra", extra.isEmpty())
    }

    @Test
    fun labelCountMatchesFamilyCount() {
        // 上2件は集合として見るので、重複キー等で件数がずれたときにここで落ちる。
        assertEquals(MirrorKeys.all.size, breakdownLabels.size)
    }

    @Test
    fun everyLabelIsHumanReadableJapanese() {
        val bad = breakdownLabels.filter { (_, v) -> v.isBlank() || v.all { it.code < 128 } }
        assertTrue("空 or ASCII のみのラベル（内部キーの貼り付け漏れが疑わしい）: ${bad.keys}", bad.isEmpty())
    }
}
