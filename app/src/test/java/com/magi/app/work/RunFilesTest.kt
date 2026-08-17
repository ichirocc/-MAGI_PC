package com.magi.app.work

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

/**
 * [3.386.0] バックグラウンド最適化の共有ファイルまわりで、**戻したら落ちる**ものを用意する。
 *
 * `OptimizationWorker` はこのアプリで最も監査が濃い（3.327.0 High3・3.329.0 H-03・3.333.0・
 * 3.336.0 S3/P0残・3.385.0 の計11件）のに、`ctx.filesDir` を直接触るため JVM 単体テストから1行も
 * 届かず、**再発防止がコメントだけ**だった。所有権・後片付け・原子置換をここへ移して固定する。
 *
 * **ここで守れないもの（正直に）**: `doWork()` の並び（耐久保存→公開・失敗パスが所有権を閉じること・
 * 進捗公開の前の所有権確認）と、所有確認〜置き換えの TOCTOU。どちらも Worker のライフサイクル側に
 * あり Robolectric か instrumented test が要る。
 */
class RunFilesTest {

    @get:Rule val tmp = TemporaryFolder()

    private fun files() = RunFiles(tmp.root)

    // --- 所有権（3.327.0 High3 / 3.329.0 H-03） -------------------------------------------------

    @Test
    fun legacyRunWithoutAnIdIsAlwaysTreatedAsTheOwner() {
        // runId を持たない旧経路は非破壊で従来どおり動かす（マーカーの有無に関わらず）。
        assertTrue(files().owns(0L))
        files().beginRun(777L)
        assertTrue("マーカーが他人でも、旧経路は所有者扱いのまま", files().owns(0L))
    }

    @Test
    fun onlyTheRunNamedByTheMarkerOwnsTheFiles() {
        files().beginRun(42L)
        assertTrue(files().owns(42L))
        // REPLACE で新しい実行がマーカーを書いた ＝ 旧実行は以後いっさい書かない/消さない。
        files().beginRun(43L)
        assertFalse("置き換えられた旧実行が所有者のままになっている", files().owns(42L))
        assertTrue(files().owns(43L))
    }

    @Test
    fun aMissingOrCorruptMarkerOwnsNothing() {
        // マーカー無し。
        assertEquals(0L, files().activeRunId())
        assertFalse("マーカーが無いのに所有者と判定している", files().owns(42L))
        // 中身が壊れている（書き込み途中で落ちた等）。
        files().runId.writeText("ゴミ")
        assertEquals("壊れたマーカーは 0（誰も所有しない）へ倒す", 0L, files().activeRunId())
        assertFalse(files().owns(42L))
    }

    @Test
    fun beginRunRoundTripsThroughTheMarkerFile() {
        files().beginRun(1_234_567_890_123L)
        assertEquals(1_234_567_890_123L, files().activeRunId())
    }

    // --- 後片付けの網羅（3.336.0 P0残 / 敵対的レビュー #9） -------------------------------------

    @Test
    fun clearRemovesEverySharedFileWithoutException() {
        val f = files()
        f.input.writeText("in"); f.result.writeText("res"); f.snapshot.writeText("best"); f.beginRun(9L)
        assertEquals(4, tmp.root.listFiles()!!.size)

        f.clear()

        // 「1つ足して消し忘れる」が次回起動で古い状態を掴む事故になるので、
        // 個別の存在確認でなく **ディレクトリが空** で網羅を固定する。
        assertEquals(
            "clear が消し残したファイル: ${tmp.root.listFiles()!!.joinToString { it.name }}",
            0, tmp.root.listFiles()!!.size,
        )
        f.clear()   // 何も無い状態で呼んでも落ちない（停止と完了で二重に呼ばれる経路がある）。
    }

    // --- 原子置換（3.336.0 S3 / 3.385.0） ------------------------------------------------------

    @Test
    fun writeAtomicallyLeavesTheTargetCompleteAndNoTemporaryResidue() {
        val f = files()
        val target = File(tmp.root, "out.json")
        assertTrue(f.writeAtomically(target, "{\"a\":1}"))
        assertEquals("{\"a\":1}", target.readText())
        assertNull(
            "一時ファイルが残っている（次回の書き込みや掃除が拾ってしまう）",
            tmp.root.listFiles()!!.firstOrNull { it.name.endsWith(".tmp") },
        )
    }

    @Test
    fun writeAtomicallyReplacesExistingContentEntirely() {
        val f = files()
        val target = File(tmp.root, "out.json")
        target.writeText("これは前回の長い内容です。切り詰めの取り残しがあれば末尾に残る。")
        assertTrue(f.writeAtomically(target, "短い"))
        assertEquals("前の内容が末尾に残っている", "短い", target.readText())
    }

    @Test
    fun aFailedCommitGuardLeavesTheTargetUntouched() {
        // 3.385.0: 所有権を失っていたら、既にある結果へ **一切触れない**（一時ファイルだけ捨てる）。
        val f = files()
        val target = File(tmp.root, "out.json")
        target.writeText("先行実行の結果")

        assertFalse(f.writeAtomically(target, "置き換えられた実行の結果") { false })

        assertEquals("ガードが偽なのに target を書き換えている", "先行実行の結果", target.readText())
        assertNull(
            "ガードで諦めたのに一時ファイルが残っている",
            tmp.root.listFiles()!!.firstOrNull { it.name.endsWith(".tmp") },
        )
    }

    @Test
    fun commitGuardIsEvaluatedOnceAfterTheTemporaryFileIsWritten() {
        // ガードを直列化より前に戻すと TOCTOU の窓が ms 級へ広がる。呼ばれる回数と順序を固定しておく。
        val f = files()
        val target = File(tmp.root, "out.json")
        var calls = 0
        var tmpExistedWhenGuardRan = false
        f.writeAtomically(target, "x") {
            calls++
            tmpExistedWhenGuardRan = File(tmp.root, "out.json.tmp").exists()
            true
        }
        assertEquals("ガードはちょうど1回", 1, calls)
        assertTrue("ガードは一時ファイルを書いた後に評価されなければならない", tmpExistedWhenGuardRan)
    }
}
