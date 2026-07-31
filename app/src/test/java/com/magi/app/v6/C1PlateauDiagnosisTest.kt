package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.322.0] c1 頭打ちの構造化診断。原因の判定・最終盤面での再フィルタ・文言の固定。
 */
class C1PlateauDiagnosisTest {

    /** キーは (職員, シフト, 規則index)。テストは規則index 0 を既定に使う。 */
    private fun build(
        stats: Map<Triple<Int, Int, Int>, Map<String, Int>>,
        culprits: Map<Triple<Int, Int, Int>, Map<String, Int>> = emptyMap(),
        stillDeficient: (Int, Int, Int) -> Boolean = { _, _, _ -> true },
        remainingC1: Int = 9,
    ) = C1PlateauDiagnosis.build(
        remainingC1 = remainingC1,
        blockStats = stats,
        culpritStats = culprits,
        staffName = { "S$it" },
        shiftKigou = { "K$it" },
        ruleLabel = { "R$it" },
        stillDeficient = stillDeficient,
    )

    @Test
    fun pinRejectionsDominateSoCauseIsPinConstrained() {
        val d = build(mapOf(Triple(0, 1, 0) to mapOf(
            C1PlateauDiagnosis.REASON_PIN to 12,
            C1PlateauDiagnosis.REASON_SCORE to 3,
        )))
        val e = d.entries.single()
        assertEquals(C1PlateauCause.PIN_CONSTRAINED, e.cause)
        assertEquals(12, e.rejectedByPin)
        assertEquals(3, e.rejectedByScore)
        assertEquals(1, d.pinConstrained)
        // 「構造的に不能」とは言わない＝緩めれば通る可能性を残した文言であること。
        val action = e.recommendedAction()
        assertTrue("回数固定の緩和を案内する", action.contains("固定"))
        assertTrue("不能と断定しない", !action.contains("不能"))
    }

    @Test
    fun scoreRejectionsReportTheWorstFamilyInTheAction() {
        val d = build(
            mapOf(Triple(2, 0, 0) to mapOf(C1PlateauDiagnosis.REASON_SCORE to 7)),
            culprits = mapOf(Triple(2, 0, 0) to mapOf("low" to 5, "high" to 2)),
        )
        val e = d.entries.single()
        assertEquals(C1PlateauCause.SCORE_TRADEOFF, e.cause)
        assertEquals("low" to 5, e.topScoreCulprits.first())
        assertTrue("表示名は呼出側から受ける", e.recommendedAction { "下限割れ" }.contains("下限割れ"))
    }

    @Test
    fun noCandidateCountsBothOfItsReasonStrings() {
        val d = build(mapOf(Triple(1, 1, 0) to mapOf(
            C1PlateauDiagnosis.REASON_NO_CANDIDATE to 4,
            C1PlateauDiagnosis.REASON_NO_REPACK to 5,
        )))
        val e = d.entries.single()
        assertEquals(C1PlateauCause.NO_CANDIDATE, e.cause)
        assertEquals(9, e.noCandidate)
        // 候補が1件も作れていない＝却下の観測が無い。
        assertEquals(C1PlateauEvidence.UNKNOWN, e.evidence)
    }

    @Test
    fun noCandidateIsNeverClaimedWhenSomeCandidateWasActuallyRejected() {
        // 実データで踏んだ取り違え: 「スコア却下8・候補なし10」を件数の多数決で分類すると
        //   「入れ替えられる相手が見つかりません」と案内してしまうが、実際は相手が居て
        //   禁止連続で落ちていた。候補が1件でも作れているなら「候補なし」とは言わない。
        val d = build(
            mapOf(Triple(0, 0, 0) to mapOf(
                C1PlateauDiagnosis.REASON_SCORE to 8,
                C1PlateauDiagnosis.REASON_NO_CANDIDATE to 10,
            )),
            culprits = mapOf(Triple(0, 0, 0) to mapOf("c3n" to 5)),
        )
        val e = d.entries.single()
        assertEquals(C1PlateauCause.SCORE_TRADEOFF, e.cause)
        assertEquals(10, e.noCandidate)  // 件数自体は内訳に残す
        assertTrue("相手が居ないとは言わない", !e.recommendedAction().contains("見つかりません"))
    }

    @Test
    fun pinIsOnlyTheCauseWhenItStrictlyOutnumbersScoreRejections() {
        // 同数ならスコア却下側に倒す（ピンを緩めても通らない可能性が同程度に高いため、
        //   「回数固定が壁」という強い断定を避ける）。
        val d = build(mapOf(Triple(0, 0, 0) to mapOf(
            C1PlateauDiagnosis.REASON_PIN to 4,
            C1PlateauDiagnosis.REASON_SCORE to 4,
        )))
        assertEquals(C1PlateauCause.SCORE_TRADEOFF, d.entries.single().cause)
    }

    @Test
    fun entriesResolvedOnTheFinalBoardAreDropped() {
        val stats = mapOf(
            Triple(0, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_PIN to 2),
            Triple(1, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_PIN to 2),
        )
        // 研磨の時点では2件とも残っていたが、後続パスが (1,1) を直した。
        val d = build(stats).refreshedAgainst(1) { i, _, _ -> i == 0 }
        assertEquals(1, d.entries.size)
        assertEquals(0, d.entries.single().staff)
        assertEquals(1, d.remainingC1)
    }

    @Test
    fun alreadyResolvedTargetsAreNeverListed() {
        // 対象が全部解消されたなら c1 も 0（残っていないので語ることが無い）。
        val d = build(
            mapOf(Triple(0, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_PIN to 2)),
            stillDeficient = { _, _, _ -> false },
            remainingC1 = 0,
        )
        assertTrue("解消済みは出さない", d.entries.isEmpty())
        assertTrue("原因未確定でもない", !d.causeUnknown)
        assertTrue("ログも出さない", d.logLines().isEmpty())
    }

    @Test
    fun remainingWithoutAnyObservationIsReportedAsCauseUnknown() {
        // [3.325.0] c1 は残っているのに却下の観測が1件も無い＝原因未確定。
        //   ここで「直せない理由」を語ると観測していないことを語ることになる。
        val d = build(
            mapOf(Triple(0, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_PIN to 2)),
            stillDeficient = { _, _, _ -> false },
            remainingC1 = 7,
        )
        assertTrue("観測ゼロ＋残存＝原因未確定", d.causeUnknown)
        assertTrue("内訳は持たない", d.entries.isEmpty())
        val line = d.logLines().single()
        assertTrue("残存件数を名乗る", line.contains("7件"))
        assertTrue("原因未確定と明示する", line.contains("原因未確定"))
    }

    @Test
    fun entriesAreOrderedByHowMuchWasActuallyTried() {
        val d = build(mapOf(
            Triple(0, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_PIN to 2),
            Triple(1, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_PIN to 40),
        ))
        assertEquals(1, d.entries.first().staff)
        assertTrue("ログ先頭は残存件数を名乗る", d.logLines().first().contains("窓の要件"))
    }

    // --- [3.331.0/実機ログ] 巡ごとの観測を合算する ---

    @Test fun mergedWithSumsObservationsAcrossRoundsInsteadOfOverwriting() {
        // 実機ログの再現: 1巡目は7箇所を観測、2巡目は（1巡目が直したあとの盤面なので）3箇所だけ。
        // 旧実装は 2巡目で上書きし、5日窓の説明が丸ごと消え、件数も実際より小さく出ていた。
        val round1 = C1PlateauDiagnosis.build(
            remainingC1 = 11,
            blockStats = mapOf(
                Triple(0, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_SCORE to 8),   // 5日窓
                Triple(0, 1, 1) to mapOf(C1PlateauDiagnosis.REASON_SCORE to 24),  // 14日窓
            ),
            culpritStats = mapOf(
                Triple(0, 1, 0) to mapOf("low" to 8),
                Triple(0, 1, 1) to mapOf("low" to 23, "high" to 1),
            ),
            staffName = { "古泉 健一" }, shiftKigou = { "休" },
            ruleLabel = { if (it == 0) "5日で1回以上" else "14日で4回以上" },
            stillDeficient = { _, _, _ -> true },
        )
        val round2 = C1PlateauDiagnosis.build(
            remainingC1 = 8,
            blockStats = mapOf(Triple(0, 1, 1) to mapOf(C1PlateauDiagnosis.REASON_SCORE to 6)),
            culpritStats = mapOf(Triple(0, 1, 1) to mapOf("low" to 6)),
            staffName = { "古泉 健一" }, shiftKigou = { "休" },
            ruleLabel = { if (it == 0) "5日で1回以上" else "14日で4回以上" },
            stillDeficient = { _, _, _ -> true },
        )
        val merged = round1.mergedWith(round2)
        assertEquals("2巡目に出てこない5日窓も残る", 2, merged.entries.size)
        assertEquals("残数は新しい方", 8, merged.remainingC1)
        val wide = merged.entries.first { it.ruleIndex == 1 }
        assertEquals("件数は全巡の合計", 30, wide.rejectedByScore)
        assertEquals("主因の族も合算", 29, wide.topScoreCulprits.first { it.first == "low" }.second)
        assertEquals("多い順", "low", wide.topScoreCulprits.first().first)
        val narrow = merged.entries.first { it.ruleIndex == 0 }
        assertEquals("1巡目だけの観測はそのまま", 8, narrow.rejectedByScore)
    }

    @Test fun mergedWithRecomputesCauseFromTheCombinedCounts() {
        // 1巡目はピン破りが多く、2巡目はスコア却下が多い。合算後の分類は合計で決め直す。
        fun one(pin: Int, score: Int, remaining: Int) = C1PlateauDiagnosis.build(
            remainingC1 = remaining,
            blockStats = mapOf(Triple(0, 1, 0) to mapOf(
                C1PlateauDiagnosis.REASON_PIN to pin, C1PlateauDiagnosis.REASON_SCORE to score)),
            culpritStats = emptyMap(),
            staffName = { "s" }, shiftKigou = { "X" }, ruleLabel = { "r" },
            stillDeficient = { _, _, _ -> true },
        )
        val a = one(pin = 5, score = 1, remaining = 3)
        val b = one(pin = 0, score = 9, remaining = 2)
        assertEquals(C1PlateauCause.PIN_CONSTRAINED, a.entries.single().cause)
        val merged = a.mergedWith(b)
        assertEquals("合計 pin=5 score=10 → スコア却下が多い",
            C1PlateauCause.SCORE_TRADEOFF, merged.entries.single().cause)
    }

    @Test fun mergedWithHandlesEmptySides() {
        val empty = C1PlateauDiagnosis(5, emptyList())
        val one = C1PlateauDiagnosis.build(
            remainingC1 = 3,
            blockStats = mapOf(Triple(0, 1, 0) to mapOf(C1PlateauDiagnosis.REASON_SCORE to 2)),
            culpritStats = emptyMap(),
            staffName = { "s" }, shiftKigou = { "X" }, ruleLabel = { "r" },
            stillDeficient = { _, _, _ -> true },
        )
        assertEquals("空 + 有 = 有", 1, empty.mergedWith(one).entries.size)
        // 有 + 空 は観測を捨てず、残数だけ新しい方を採る（2巡目が何も観測しなくても説明を失わない）。
        val kept = one.mergedWith(empty)
        assertEquals(1, kept.entries.size)
        assertEquals(5, kept.remainingC1)
    }
}
