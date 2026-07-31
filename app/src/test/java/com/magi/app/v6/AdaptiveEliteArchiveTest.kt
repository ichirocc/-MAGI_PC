package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AdaptiveEliteArchiveTest {
    private fun report(hard: Int, total: Int, weighted: Double = total.toDouble()) = ViolationReport(
        violations = emptyMap(),
        needViolations = emptyMap(),
        countViolations = emptyMap(),
        breakdown = emptyMap(),
        total = total,
        hard = hard,
        soft = (total - hard).coerceAtLeast(0),
        weightedScore = weighted,
    )

    private fun board(vararg values: Int): Array<IntArray> = arrayOf(values)

    @Test
    fun exactDuplicateIsReplacedOnlyByBetterOfficialObjective() {
        val archive = AdaptiveEliteArchive()
        val a = board(0, 1, 0, 1)
        archive.register(a, report(1, 9), HypothesisEpochRole.DAY_BLOCK_ALNS, 1, 1, bridge = false)
        archive.register(a, report(1, 10), HypothesisEpochRole.HARD_FAMILY_RSI, 2, 2, bridge = false)
        assertEquals(9, archive.allForTest().single().report.total)

        archive.register(a, report(1, 8), HypothesisEpochRole.HARD_FAMILY_RSI, 2, 3, bridge = false)
        assertEquals(1, archive.size())
        assertEquals(8, archive.allForTest().single().report.total)
        assertEquals(3, archive.allForTest().single().epoch)
    }

    @Test
    fun compressionKeepsQualityDistanceAndBridgePopulations() {
        val archive = AdaptiveEliteArchive()
        val reference = board(0, 0, 0, 0, 0, 0)
        archive.register(board(0, 0, 0, 0, 0, 1), report(0, 8), HypothesisEpochRole.BASELINE_REFINE, 0, 0, false)
        archive.register(board(0, 0, 0, 0, 1, 0), report(0, 9), HypothesisEpochRole.PERSONAL_RSI, 6, 1, false)
        archive.register(board(1, 1, 1, 0, 0, 0), report(0, 11), HypothesisEpochRole.DAY_BLOCK_ALNS, 1, 1, false)
        archive.register(board(1, 1, 1, 1, 1, 1), report(0, 12), HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS, 7, 1, false)
        archive.register(board(2, 2, 2, 2, 2, 2), report(1, 7), HypothesisEpochRole.HARD_DEBT_RSI_PLUS, 3, 1, true)

        val selected = archive.snapshot(reference, report(0, 10), maxQuality = 2, maxDiversity = 2, maxBridge = 1)
        assertEquals(5, selected.size)
        assertEquals(2, selected.count { it.tier == AdaptiveEliteTier.QUALITY })
        assertEquals(2, selected.count { it.tier == AdaptiveEliteTier.DIVERSITY })
        assertEquals(1, selected.count { it.tier == AdaptiveEliteTier.BRIDGE })
        assertTrue(selected.any { AdaptiveEliteArchive.sameSchedule(it.schedule, board(1, 1, 1, 1, 1, 1)) })
        assertTrue(selected.any { it.bridge && it.report.hard == 1 })
    }

    @Test
    fun snapshotsAreDefensiveCopies() {
        val archive = AdaptiveEliteArchive()
        val a = board(0, 1, 2)
        archive.register(a, report(0, 1), HypothesisEpochRole.BASELINE_REFINE, 0, 0, false)
        a[0][0] = 9
        val snapshot = archive.snapshot(board(0, 0, 0), report(0, 2))
        assertFalse(snapshot.single().schedule[0][0] == 9)
        snapshot.single().schedule[0][1] = 9
        assertEquals(1, archive.allForTest().single().schedule[0][1])
    }

    @Test
    fun snapshotNeverReturnsTheSameBoardTwice() {
        // [3.332.0] ログから「相異なるelite」を落とした根拠。`register` が sameSchedule で重複を弾き
        //   `snapshot` も filterNot で除くので、圧縮エリートは**常に**相異なる＝数えても恒真値だった
        //   （実機ログでも2実行とも 10/10）。その不変条件をここで固定する。
        val archive = AdaptiveEliteArchive()
        val reference = board(0, 0, 0, 0, 0, 0)
        // 同じ盤面を役割・エポック・品質を変えて何度も登録する。
        val dup = board(0, 0, 0, 0, 0, 1)
        archive.register(dup, report(0, 9), HypothesisEpochRole.BASELINE_REFINE, 0, 0, false)
        archive.register(dup, report(0, 8), HypothesisEpochRole.PERSONAL_RSI, 1, 1, false)
        archive.register(dup, report(1, 7), HypothesisEpochRole.DAY_BLOCK_ALNS, 2, 2, true)
        archive.register(board(1, 1, 0, 0, 0, 0), report(0, 10), HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS, 3, 1, false)
        archive.register(board(1, 1, 1, 1, 0, 0), report(0, 11), HypothesisEpochRole.LARGE_DESTROY_ALNS, 4, 1, false)
        archive.register(board(2, 2, 2, 2, 2, 2), report(1, 6), HypothesisEpochRole.HARD_DEBT_RSI_PLUS, 5, 1, true)
        val snap = archive.snapshot(reference, report(0, 12))
        val keys = snap.map { e -> e.schedule.joinToString("|") { it.joinToString(",") } }
        assertEquals("圧縮エリートに同じ盤面は現れない", keys.size, keys.distinct().size)
        assertTrue("何か選ばれている（空で通る安易なテストにしない）", snap.isNotEmpty())
    }
}
