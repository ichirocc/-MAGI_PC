package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class V6FinalBridgePortTest {
    /**
     * [3.289.0/外部レビューMedium] 「入力より悪化していない」の共通判定。
     * 旧: 各テストが hard→total→weighted の辞書式を手書きで複製しており、3.287.0 の keep-best 統一
     * （hard→weightedScore→total）後もそのまま残っていた＝テストだけが旧目的関数を固定していた。
     * その結果 (a) weighted が悪化して total が減る結果を「悪化なし」と誤って許し、(b) 正しく採用すべき
     * 「weighted 改善・total 増」を「悪化」と誤判定する、という二重のドリフトを抱えていた。
     * 本番と同じ単一ソース `betterReport` に委譲する（after が before より厳密に悪くない＝not worse）。
     */
    private fun notWorseThan(after: ViolationReport, before: ViolationReport): Boolean =
        !betterReport(before, after)

    @Test fun algorithmLabelsMatchWebThresholds() {
        // [3.128.0] 31〜210s は複合（RSI違反集中→ALNS研磨）に統一（実機指摘: 60s が ALNS 単発だった）。
        // [3.266.0] 211s〜は異種並列ポートフォリオ（PORTFOLIO、300超は拡張）。
        assertEquals("v5", V6FinalPort.getAlgorithmLabel(10).tech)
        assertEquals("v5", V6FinalPort.getAlgorithmLabel(30).tech)
        assertEquals("RSI→ALNS", V6FinalPort.getAlgorithmLabel(60).tech)
        assertEquals("RSI→ALNS", V6FinalPort.getAlgorithmLabel(90).tech)
        assertEquals("RSI→ALNS", V6FinalPort.getAlgorithmLabel(180).tech)
        assertEquals("PORTFOLIO", V6FinalPort.getAlgorithmLabel(300).tech)
        assertEquals("PORTFOLIO拡張", V6FinalPort.getAlgorithmLabel(600).tech)
    }

    @Test fun busyDetailAndGateWork() {
        val st = sampleState()
        val b = V6FinalPort.buildBusyDetail(st, "違反チェック中")
        assertTrue(b.problemSize.contains("2名"))
        assertTrue(b.constraintCount.contains("希望 0件"))
        assertTrue(V6FinalPort.confirmDespiteImpossibleWishes(st).allowed)
    }

    @Test fun elitePathRelinkNeverWorsensBest() {
        val st = sampleState()
        val best = listOf(listOf(0, 1), listOf(1, 0)).toIntArray2D()
        val alt = listOf(listOf(1, 0), listOf(0, 1)).toIntArray2D()
        val bestRep = UnifiedViolationChecker.check(st, best)
        val (_, rep) = V6NativeOptimizer.elitePathRelink(st, best, listOf(alt)) { false }
        // 退化しない: 結果は best 以上（hard→weighted→total の辞書順で悪化しない）。
        val notWorse = notWorseThan(rep, bestRep)
        assertTrue(notWorse)
        // 精鋭解が無ければ best 不変。
        val (_, r2) = V6NativeOptimizer.elitePathRelink(st, best, emptyList()) { false }
        assertEquals(bestRep.total, r2.total)
    }

    @Test fun minCostAssignmentFindsOptimum() {
        // 既知の最適割当: 反対角(行0→列2, 行1→列1, 行2→列0)が最小費用。
        val cost = arrayOf(
            longArrayOf(9, 9, 1),
            longArrayOf(9, 1, 9),
            longArrayOf(1, 9, 9),
        )
        // [3.278.0] solve は全INF行の fail-safe で nullable 化（実行可能な行列では常に非null）。
        val a = MinCostAssignment.solve(cost)!!
        assertEquals(2, a[0]); assertEquals(1, a[1]); assertEquals(0, a[2])
        // 各行・各列が一意（順列）。
        assertEquals(3, a.toSet().size)
    }

    @Test fun dayAssignmentPolishNeverWorsens() {
        val st = sampleState()
        val before = UnifiedViolationChecker.check(st, st.schedule.toIntArray2D())
        val r = V6HotfixPasses.applyDayAssignmentPolish(st, st.schedule.toIntArray2D())
        val after = UnifiedViolationChecker.check(st, r.newSchedule)
        // 退化しない: hard→weighted→total の辞書順で悪化しない。
        val notWorse = notWorseThan(after, before)
        assertTrue(notWorse)
        assertEquals(0, invalidAssignmentCount(st, r.newSchedule))   // 割当は常に妥当（人数=列固定）
    }

    @Test fun cyclicSwapPolishNeverWorsens() {
        val st = sampleState()
        val before = UnifiedViolationChecker.check(st, st.schedule.toIntArray2D())
        val r = V6HotfixPasses.applyCyclicSwapPolish(st, st.schedule.toIntArray2D())
        val after = UnifiedViolationChecker.check(st, r.newSchedule)
        val notWorse = notWorseThan(after, before)
        assertTrue(notWorse)
        assertEquals(0, invalidAssignmentCount(st, r.newSchedule))   // 被覆保存＝割当は常に妥当
    }

    @Test fun c1WindowPolishNeverWorsens() {
        // cons1（2日窓に「日」を1回以上）付きの状態でも退化しない＋割当は妥当。
        val st = sampleState().copy(cons1 = listOf(com.magi.app.model.C1Row("2", "日", "1")))
        val before = UnifiedViolationChecker.check(st, st.schedule.toIntArray2D())
        val r = V6HotfixPasses.applyC1WindowPolish(st, st.schedule.toIntArray2D())
        val after = UnifiedViolationChecker.check(st, r.newSchedule)
        val notWorse = notWorseThan(after, before)
        assertTrue(notWorse)
        assertEquals(0, invalidAssignmentCount(st, r.newSchedule))
    }

    @Test fun c3SequencePolishNeverWorsens() {
        val st = sampleState()
        val before = UnifiedViolationChecker.check(st, st.schedule.toIntArray2D())
        val r = V6HotfixPasses.applyC3SequencePolish(st, st.schedule.toIntArray2D())
        val after = UnifiedViolationChecker.check(st, r.newSchedule)
        val notWorse = notWorseThan(after, before)
        assertTrue(notWorse)
        assertEquals(0, invalidAssignmentCount(st, r.newSchedule))
    }

    @Test fun equalizePolishesNeverWorsenMainObjective() {
        // [3.317.0] 対象を分散指標ベースの旧2パス（撤去済み）から、その役割を引き継いだ L1 ベースの
        //   後継へ差し替え。fair/weekly の平準化は主目的(hard→weighted→total)を悪化させない。
        val st = sampleState()
        for (op in listOf<(MagiState, Array<IntArray>) -> Array<IntArray>>(
            { s, sc -> V6HotfixPasses.applyFairPolish(s, sc).newSchedule },
            { s, sc -> V6HotfixPasses.applyWeeklyRebalancePolish(s, sc).newSchedule },
        )) {
            val before = UnifiedViolationChecker.check(st, st.schedule.toIntArray2D())
            val after = UnifiedViolationChecker.check(st, op(st, st.schedule.toIntArray2D()))
            val notWorse = notWorseThan(after, before)
            assertTrue(notWorse)
        }
    }

    @Test fun skillGroupConstraintsCount() {
        val base = sampleState()
        val skilled = base.copy(
            skillGroups = listOf(Group("看護", "N")),
            staff = base.staff.map { it.copy(skillIdx = 0) },
        )
        // C41s: スキル群Nは「日」を毎日2回必要 → 各日1回しか居ないので2日とも違反。
        val c41 = skilled.copy(cons41s = listOf(com.magi.app.model.C41Row("N", "日", "2", "2")))
        assertEquals(2, UnifiedViolationChecker.check(c41, c41.schedule.toIntArray2D()).breakdown["c41s"] ?: 0)
        // C42s: 同日に N の「日」と N の「休」が併存不可 → 各日1ペアで2違反。
        val c42 = skilled.copy(cons42s = listOf(com.magi.app.model.C42Row("N", "N", "日", "休")))
        assertEquals(2, UnifiedViolationChecker.check(c42, c42.schedule.toIntArray2D()).breakdown["c42s"] ?: 0)
        // 制約が無ければ従来どおり（スキル族は breakdown に出ない＝ゴールデン不変）。
        assertEquals(0, UnifiedViolationChecker.check(skilled, skilled.schedule.toIntArray2D()).breakdown["c41s"] ?: 0)
    }

    @Test fun postHotfixChainReturnsReport() {
        val st = sampleState()
        val post = V6HotfixPasses.runPostOptimization(st, st.schedule.toIntArray2D(), "test")
        assertEquals(0, invalidAssignmentCount(st, post.schedule))
        assertTrue(post.logs.isNotEmpty())
        assertEquals(post.report.total, UnifiedViolationChecker.check(st, post.schedule).total)
    }

    private fun sampleState(): MagiState = MagiState(
        startDate = "2026-06-01",
        endDate = "2026-06-02",
        shifts = listOf(Shift("日勤", "日", "1", "1"), Shift("休み", "休", "", "")),
        groups = listOf(Group("A", "A")),
        staff = listOf(Staff("s1", 0), Staff("s2", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(listOf(0, 1), listOf(1, 0)),
        wishes = emptyMap(),
        staffRange = mapOf("0,0" to Range("0", "2")),
        needDay1 = emptyMap(),
        needDay2 = emptyMap(),
        cons1 = emptyList(),
        cons2 = emptyList(),
        cons3 = emptyList(),
        cons3n = emptyList(),
        cons3m = emptyList(),
        cons3mn = emptyList(),
        cons41 = emptyList(),
        cons42 = emptyList(),
    )
}

/**
 * 盤面のうち「担当できないシフト／範囲外のシフト番号」が入っているセル数。
 * [3.393.0] 旧 `V6WebCompat.invalidAssignmentCount`。本番の呼出は無く、この検証オラクルが唯一の用途
 * だったので Web 互換オブジェクトの撤去に合わせてテスト側へ移した（挙動は同一）。
 */
internal fun invalidAssignmentCount(state: MagiState, schedule: Array<IntArray> = state.schedule.toIntArray2D()): Int {
    val p = Problem(state)
    val s = normalizeSchedule(schedule, p)
    var n = 0
    for (i in 0 until p.S) for (j in 0 until p.T) {
        val k = s[i][j]
        if (k !in 0 until p.K || !p.canDo(i, k)) n++
    }
    return n
}
