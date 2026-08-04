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

    /**
     * [3.352.0] アーカイブの並べ替えは `MirrorCore.reportComparator`（keep-best と同じ辞書式）へ
     * 委譲している。3キーを写した実装へ戻すと、順序を変えたときにここだけ取り残される
     * （3.287.0 の統一後に4箇所で実際に起きた）。委譲が生きていることを直接固定する。
     */
    @Test
    fun archiveOrderingIsTheKeepBestOrderAndFallsThroughToTotal() {
        // hard が第1キー: hard が小さければ weighted/total がどれだけ悪くても勝つ。
        assertTrue(AdaptiveEliteArchive.better(report(0, 999, 9999.0), report(1, 1, 1.0)))
        // weightedScore が第2キー: hard 同値なら total が悪くても weighted が小さいほうが勝つ。
        assertTrue(AdaptiveEliteArchive.better(report(1, 999, 10.0), report(1, 1, 11.0)))
        // total は最後のタイブレーク。
        assertTrue(AdaptiveEliteArchive.better(report(1, 5, 10.0), report(1, 6, 10.0)))
        assertEquals(0, AdaptiveEliteArchive.compareReports(report(1, 5, 10.0), report(1, 5, 10.0)))

        // 全ペアで keep-best 本体と一致すること（片方だけ順序を変えたら必ず落ちる）。
        val samples = listOf(
            report(0, 5, 10.0), report(0, 5, 11.0), report(0, 6, 10.0),
            report(1, 1, 1.0), report(1, 5, 10.0), report(2, 0, 0.0),
        )
        for (a in samples) for (b in samples) {
            assertEquals(
                "compareReports が betterReport と食い違う: $a vs $b",
                betterReport(a, b),
                AdaptiveEliteArchive.compareReports(a, b) < 0,
            )
        }
    }

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
