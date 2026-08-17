package com.magi.app.work

import kotlinx.coroutines.flow.take
import kotlinx.coroutines.flow.toList
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.385.0] 耐久保証の失敗を書き出しログへ届ける経路の性質を固定する。
 *
 * `OptimizationWorker` は Android 依存でホストから触れないが、この経路の肝である
 * 「**購読より先に emit された記録が失われない**」は Repository 側の設定（replay）で決まる。
 * Worker はプロセス再起動直後に走り得るので、ViewModel が購読する前の失敗こそ残さなければ意味がない。
 */
class OptimizationRepositoryNotesTest {

    @Test
    fun notesEmittedBeforeAnyoneSubscribesAreStillDelivered() = runBlocking {
        OptimizationRepository.publishNote("I", "A")
        OptimizationRepository.publishNote("I", "B")
        // ここで初めて購読する（＝ViewModel が後から起動した状況）。
        val got = OptimizationRepository.notes.take(2).toList()
        assertEquals(listOf("I" to "A", "I" to "B"), got)
    }

    @Test
    fun publishNoteNeverBlocksOrThrowsEvenWithoutSubscribers() {
        // 記録のために本処理を止めない＝購読者ゼロでも溢れても素通りする（tryEmit）。
        repeat(200) { OptimizationRepository.publishNote("W", "note $it") }
        val got = runBlocking { OptimizationRepository.notes.take(1).toList() }
        assertTrue("溢れても直近の記録は残る", got.single().second.startsWith("note "))
        assertEquals("レベルが失われていない", "W", got.single().first)
    }
}
