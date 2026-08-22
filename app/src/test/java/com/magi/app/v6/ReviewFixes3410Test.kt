package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.410.0] 外部レビュー100件のうち、実コードに当てて **実在** と確認できた項目の回帰。
 *
 * 「確定」とされた30件を検証したところ、2件は既修正・6件は事実でない・8件は記録済みの意図的設計だった。
 * ここで固定するのは残った実在項目のうち v6 層のもの（work/ui 層はホストでコンパイルできない）。
 */
class ReviewFixes3410Test {

    // ---------- E-02: PORTFOLIO のコア数クランプ ----------

    /** 既定（workers==コア数）では **no-op**。ここが崩れると全ユーザーの探索が静かに変わる。 */
    @Test
    fun portfolioWorkerCountIsNoOpWhenWorkersFitsWithinCores() {
        assertEquals(8, V6NativeOptimizer.portfolioWorkerCount(8, cores = 8))
        assertEquals(4, V6NativeOptimizer.portfolioWorkerCount(4, cores = 4))
        assertEquals(2, V6NativeOptimizer.portfolioWorkerCount(2, cores = 8))
    }

    /** 設定タブの並列ワーカーは 16 まで上げられる＝8コア機で2倍の希釈が**設定画面から作れた**。 */
    @Test
    fun portfolioWorkerCountClampsOversubscriptionToCores() {
        assertEquals(8, V6NativeOptimizer.portfolioWorkerCount(16, cores = 8))
        assertEquals(4, V6NativeOptimizer.portfolioWorkerCount(16, cores = 4))
    }

    /** 3.224.0 の多様性の下限2（1コア機でも2仮説）は割らない。 */
    @Test
    fun portfolioWorkerCountKeepsTheDiversityFloorOfTwo() {
        assertEquals(2, V6NativeOptimizer.portfolioWorkerCount(2, cores = 1))
        assertEquals(2, V6NativeOptimizer.portfolioWorkerCount(16, cores = 1))
        assertEquals(1, V6NativeOptimizer.portfolioWorkerCount(1, cores = 1))
    }

    // ---------- E-15: SaParams の不正値は構築時に落とす ----------

    @Test
    fun saParamsRejectsDegenerateLahcLenAndChain() {
        assertThrows(IllegalArgumentException::class.java) { SaParams(lahcLen = 0) }
        assertThrows(IllegalArgumentException::class.java) { SaParams(chain = 0) }
        // 既定は当然通る（require を足したせいで通常経路が落ちないことの確認）。
        assertEquals(200, SaParams().lahcLen)
    }

    // ---------- P-01 / P-06 ----------

    /** 休が index 0 でないデータ。schedule に範囲外セルを1つ置く。 */
    private fun stWithRestNotFirst(groupIdxOfS0: Int) = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-02",
        shifts = listOf(Shift("X", "X", "", ""), Shift("休", "休", "", "")),
        groups = listOf(Group("G0", "G0")),
        staff = listOf(Staff("s0", groupIdxOfS0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(listOf(99, 0)),   // 1セル目が範囲外
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    /**
     * P-01: 範囲外セルはハードコードの 0 でなく **休** へ。旧実装は休が先頭でないデータで
     * 勤務シフトへ化けていた（3.106.0 が `Ws1Ops.removeShift` で直したのと同じ取り違え）。
     */
    @Test
    fun initialAssignmentSendsOutOfRangeCellsToRestNotToIndexZero() {
        val p = Problem(stWithRestNotFirst(0))
        assertEquals("休が index0 でないフィクスチャであること", 1, p.restIdx)
        assertEquals(p.restIdx, p.initialAssignment()[0][0])
    }

    /**
     * P-06: `groupIdx` が範囲外でも **落ちず**（旧: `bucket[sgrp[i]]` で AIOOBE）、
     * 先頭群へ寄せたうえで**必ず記録される**（黙って寄せると別の群のルールが静かに掛かる）。
     */
    @Test
    fun outOfRangeGroupIdxIsClampedAndRecordedInsteadOfCrashing() {
        val p = Problem(stWithRestNotFirst(groupIdxOfS0 = 7))
        assertEquals(0, p.sgrp[0])
        assertEquals(listOf(0), p.outOfRangeGroupStaff)
        // 落ちないことの確認（旧実装はここで AIOOBE）。
        assertTrue(p.initialAssignment()[0].size == 2)
    }

    /** 正常データでは記録が空＝誤検知しない。 */
    @Test
    fun validGroupIdxIsNotRecorded() {
        assertTrue(Problem(stWithRestNotFirst(0)).outOfRangeGroupStaff.isEmpty())
    }

    /** 2k) 範囲外グループが設定ミス診断に出る（2i のスキル群と対）。 */
    @Test
    fun outOfRangeGroupIdxIsReportedBySanityGuidance() {
        val st = stWithRestNotFirst(groupIdxOfS0 = 7)
        val issues = V6SanityPort.buildGuidance(st, Problem(st))
        assertTrue("グループの割当の診断が出ること: ${issues.map { it.where }}",
            issues.any { it.where == "グループの割当" })
        val clean = stWithRestNotFirst(0)
        assertTrue(V6SanityPort.buildGuidance(clean, Problem(clean)).none { it.where == "グループの割当" })
    }

    // ---------- W-03: 担当できるシフトが0の群 ----------

    /** 全部 OFF にすると休しか置けなくなる（`allowedShiftsForStaff` が空→`?: restIdx`）。 */
    @Test
    fun groupWithNoAllowedShiftIsReported() {
        val base = stWithRestNotFirst(0)
        val blank = base.copy(groupShift = listOf(listOf(0, 0)))
        assertTrue("担当できるシフトの診断が出ること",
            V6SanityPort.buildGuidance(blank, Problem(blank)).any { it.where == "担当できるシフト" })
        // 所属者がいない群は無害＝出さない。
        val noMember = blank.copy(staff = emptyList(), schedule = emptyList())
        assertTrue(V6SanityPort.buildGuidance(noMember, Problem(noMember)).none { it.where == "担当できるシフト" })
        // 正常データでは出さない。
        assertTrue(V6SanityPort.buildGuidance(base, Problem(base)).none { it.where == "担当できるシフト" })
    }

    // ---------- D-01 / D-02: DeltaEvaluator の不正入力は丸めず落とす ----------

    // ---------- W-01 / W-02: 記号の重複を入力時に断る ----------

    /**
     * 既存の記号へ改名すると `renameShiftInConstraints` が制約行を一括置換して**別の行と合流**し、
     * 改名し直しても戻らない（戻すと相手側まで巻き添え）。検査8 は事後警告なので手遅れ。
     */
    @Test
    fun symbolCollisionIsDetectedForAddAndRename() {
        val syms = listOf("休", "X", "Y")
        // 追加: 既存と同じ記号は衝突
        assertTrue(Ws1Ops.symbolCollides(syms, "X"))
        assertTrue("空白違いも同じ記号として扱う（P-11 の揺れを作らせない）", Ws1Ops.symbolCollides(syms, " X "))
        assertTrue(Ws1Ops.symbolCollides(syms, "Z").not())
        // 改名: 自分自身は除く（同じ記号のままの確定を拒否しない）
        assertTrue("自分自身は衝突にしない", Ws1Ops.symbolCollides(syms, "X", exceptIndex = 1).not())
        assertTrue("他の行との衝突は検出する", Ws1Ops.symbolCollides(syms, "Y", exceptIndex = 1))
        // 空記号は衝突判定の対象外（別途 isBlank で弾いている）
        assertTrue(Ws1Ops.symbolCollides(syms, "").not())
    }

    @Test
    fun deltaEvaluatorRejectsMalformedBoards() {
        val p = Problem(stWithRestNotFirst(0))
        val de = DeltaEvaluator(p)
        assertThrows(IllegalArgumentException::class.java) { de.reset(arrayOf(intArrayOf(0))) }          // 行が短い
        assertThrows(IllegalArgumentException::class.java) { de.reset(arrayOf(intArrayOf(0, 99))) }      // 値が範囲外
        assertThrows(IllegalArgumentException::class.java) { de.apply(0, 0, 99) }                         // nw が範囲外
        de.reset(arrayOf(intArrayOf(0, 1)))   // 正常な盤面は通る
    }

    // ===== I-07: 職員一覧CSV の未知グループ/スキル記号を記録する =====
    // 旧: `gi ?: 0`（新規＝先頭グループ）・`gi ?: cur.groupIdx`（既存＝現状維持）で、空欄と誤記が
    //   見分けられないまま黙って落ちていた。所属グループは担当できるシフトを決める＝説明のつかない盤面になる。
    @Test fun unknownGroupAndSkillSymbolsInStaffCsvAreRecorded() {
        val st = com.magi.app.model.MagiState(
            startDate = "2026-06-01", endDate = "2026-06-02",
            shifts = listOf(com.magi.app.model.Shift("休", "休", "", ""), com.magi.app.model.Shift("A", "A", "", "")),
            groups = listOf(com.magi.app.model.Group("G1", "G1")),
            skillGroups = listOf(com.magi.app.model.Group("S1", "S1")),
            staff = listOf(com.magi.app.model.Staff("既存", 0, 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(listOf(0, 0)),
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val sched = arrayOf(intArrayOf(0, 0))
        val csv = "氏名,グループ,スキル\n新人,ZZ,QQ\n既存,ZZ,S1\n別人,G1,\n"
        val r = com.magi.app.v6.StaffCsvIO.parseUpsert(csv, st, sched)
        assertNotNull("取込自体は成功する", r)
        assertEquals("未知グループ ZZ は2件（新規1＋既存1）", 2, r!!.unknownGroups["ZZ"])
        assertEquals("未知スキル QQ は1件", 1, r.unknownSkills["QQ"])
        assertNull("既知の G1 は未知に数えない", r.unknownGroups["G1"])
        assertNull("空欄は未知に数えない（指定なしと誤記を区別する）", r.unknownSkills[""])
        // 挙動そのものは不変: 新規は先頭グループ、既存は元のまま。
        assertEquals("新規は先頭グループ", 0, r.state.staff.first { it.name == "新人" }.groupIdx)
        assertEquals("既存の所属は維持", 0, r.state.staff.first { it.name == "既存" }.groupIdx)
    }

    // ===== I-08: 引用符が閉じないCSVを検出する =====
    // 旧: `inQuote` が true のまま入力が終わっても検出せず、開いた引用符以降の全文が1セルへ
    //   吸い込まれ残りの行が丸ごと消えるのに、呼出側からは「短いCSV／氏名不一致」と区別が付かなかった。
    @Test fun unclosedQuoteInScheduleCsvIsFlaggedInsteadOfSilentlyTruncated() {
        val st = com.magi.app.model.MagiState(
            startDate = "2026-06-01", endDate = "2026-06-02",
            shifts = listOf(com.magi.app.model.Shift("休", "休", "", ""), com.magi.app.model.Shift("A", "A", "", "")),
            groups = listOf(com.magi.app.model.Group("G1", "G1")),
            staff = listOf(com.magi.app.model.Staff("職員A", 0), com.magi.app.model.Staff("職員B", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(listOf(0, 0), listOf(0, 0)),
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val base = arrayOf(intArrayOf(0, 0), intArrayOf(0, 0))
        val good = "スタッフ \\ 日付,1,2\n職員A,A,休\n職員B,休,A\n"
        val ok = com.magi.app.v6.ScheduleCsvBridge.parse(good, st, base)
        assertEquals("正常なCSVは2名とも一致", 2, ok.matched)
        assertFalse("正常なCSVで旗は立たない", ok.unclosedQuote)
        assertEquals("職員Aの1日目はA", 1, ok.schedule[0][0])

        // 職員A の行で引用符を開いたまま閉じない → 以降（職員B の行を含む）が1セルへ吸い込まれる。
        val bad = "スタッフ \\ 日付,1,2\n職員A,\"A,休\n職員B,休,A\n"
        val ng = com.magi.app.v6.ScheduleCsvBridge.parse(bad, st, base)
        assertTrue("引用符が閉じないことを検出する", ng.unclosedQuote)
        assertTrue("実際に行が失われている（一致が減る）", ng.matched < 2)
    }
}
