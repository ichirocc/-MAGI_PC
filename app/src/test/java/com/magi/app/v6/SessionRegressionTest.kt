package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * 本セッションの修正群への回帰テスト。
 *  - checkResultWorse の辞書順3節（3.92.0 の hard>= ガード含む）
 *  - 検査6b: 担当レパートリー強制下限 > apt目標（3.98.0）
 *  - CSVヘッダ無し先頭行の取込（3.103.0）
 */
class SessionRegressionTest {

    // ---- checkResultWorse: [3.287.0 keep-best統一] hard→weightedScore→total の辞書順で「悪化した時だけ」発火する ----

    private fun rep(hard: Int, total: Int, weighted: Double) = ViolationReport(
        violations = emptyMap(), needViolations = emptyMap(), countViolations = emptyMap(),
        breakdown = emptyMap(), total = total, hard = hard, soft = total - hard, weightedScore = weighted,
    )

    @Test fun checkResultWorse_lexicographic() {
        val base = rep(hard = 2, total = 10, weighted = 100.0)
        // 厳密に良い（各層）→ 発火しない
        assertNull(V6FinalPort.checkResultWorse(base, rep(1, 99, 9999.0)))   // hard改善は weighted/total 悪化でも良化
        assertNull(V6FinalPort.checkResultWorse(base, rep(2, 999, 99.0)))    // weighted改善は total 悪化でも良化（3.287.0 新順序）
        assertNull(V6FinalPort.checkResultWorse(base, rep(2, 10, 99.0)))     // weighted のみ改善
        assertNull(V6FinalPort.checkResultWorse(base, rep(2, 9, 100.0)))     // weighted 同値・total 改善（第3キー）
        assertNull(V6FinalPort.checkResultWorse(base, rep(2, 10, 100.0)))    // 完全同値
        // [3.92.0 ガード] hard改善なら weighted/total 悪化でも良化（旧実装はここで誤発火していた）
        assertNull(V6FinalPort.checkResultWorse(base, rep(1, 10, 200.0)))
        // 厳密に悪い（各層）→ 発火する
        assertNotNull(V6FinalPort.checkResultWorse(base, rep(3, 1, 1.0)))    // hard悪化
        assertNotNull(V6FinalPort.checkResultWorse(base, rep(2, 9, 101.0)))  // 同hard・weighted悪化（total改善でも悪化＝3.287.0 新順序）
        assertNotNull(V6FinalPort.checkResultWorse(base, rep(2, 11, 100.0))) // 同hard/weighted・total悪化（第3キー）
        // before=null は常に発火しない
        assertNull(V6FinalPort.checkResultWorse(null, rep(9, 99, 999.0)))
    }

    // ---- 検査6b: 担当={休,B4,有}・休10-10・有1-1・31日 → B4 は最低20回＝目標1は達成不能 ----

    private fun aptState(restCapped: Boolean) = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-31",
        shifts = listOf(Shift("休", "休", "", ""), Shift("B4", "B4", "", ""), Shift("有", "有", "", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("美幸", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1)),
        groupShiftApt = listOf(listOf("", "1", "")),   // B4 の apt目標=1
        schedule = listOf(List(31) { 0 }),
        wishes = emptyMap(),
        staffRange = buildMap {
            if (restCapped) put("0,0", Range("10", "10"))   // 休 10-10 固定
            put("0,2", Range("1", "1"))                     // 有 1-1 固定
        },
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun forcedAptFloorDetected() {
        // 強制下限 = 31 − (休上限10 + 有上限1) = 20 > 目標1 → 発火
        val fired = V6SanityPort.buildGuidance(aptState(restCapped = true))
        assertTrue("強制下限>apt目標 が案内される",
            fired.any { it.where.contains("適切回数") && it.problem.contains("最低20回") })
        // 休に上限が無ければ下界は 0 以下 → 発火しない（保守的判定）
        val silent = V6SanityPort.buildGuidance(aptState(restCapped = false))
        assertTrue("上限未設定の他シフトがあれば発火しない",
            silent.none { it.where.contains("適切回数") && it.problem.contains("最低") })
    }

    // ---- CSVヘッダ無し先頭行: 実データ（既知キーワード/職員名）なら黙殺しない ----

    private fun csvState() = MagiState(
        startDate = "2026-06-01", endDate = "2026-06-06",
        shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", "1", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("花子", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(List(6) { 0 }),
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun headerlessConstraintsCsvKeepsFirstRow() {
        val st = csvState()
        // ヘッダ無し: 先頭行も実データ（連勤）→ 2件とも取り込まれる
        val headerless = ConstraintsCsvIO.parse("連勤,2,休,1\n回数下限,A,3", st)
        assertNotNull(headerless)
        assertEquals(2, headerless!!.accepted)
        assertEquals("[3.329.0] 読めない行は無い", 0, headerless.rejected)
        assertEquals(1, headerless.state.cons1.size)
        assertEquals(1, headerless.state.cons2.size)
        // ヘッダ有り: 従来どおりヘッダは落ちる
        val withHeader = ConstraintsCsvIO.parse("種別,a,b,c,d,e\n連勤,2,休,1", st)
        assertNotNull(withHeader)
        assertEquals(1, withHeader!!.accepted)
    }

    @Test fun constraintsCsvRejectsStructurallyUnusableRows() {
        // [3.333.0/外部レビュー Critical] 種別が既知なだけの行を無条件に受理していた。
        //   `連勤,,,` は C1Row("","","") として件数に数えられるが `Problem` は捨てる＝
        //   **評価されない行で既存の有効な制約を全置換**できた（実質「制約なし」で最適化される）。
        val st = csvState()
        val empty = ConstraintsCsvIO.parse("連勤,2,休,1\n連勤,,,", st)
        assertNotNull(empty)
        assertEquals("読める行は従来どおり数える", 2, empty!!.accepted)
        assertEquals("評価されない行を数える", 1, empty.rejected)

        // 群・スキル群も同じ（記号が今のデータに無い＝その行は一切効かない）。
        val unknownGroup = ConstraintsCsvIO.parse("群回数,ZZ,A,0,1", st)
        assertEquals(1, unknownGroup!!.rejected)

        // 連続パターンの未解決記号は別リスト(c3UnknownShift)に入るので、そちらも見ていることの確認。
        val unknownShift = ConstraintsCsvIO.parse("禁止連続,休,ZZ", st)
        assertEquals(1, unknownShift!!.rejected)

        // 正常な行しかなければ従来どおり 0。
        val clean = ConstraintsCsvIO.parse("群回数,G,A,0,1\n禁止連続,休,A", st)
        assertEquals(2, clean!!.accepted)
        assertEquals(0, clean.rejected)
    }

    // ---- レビュー指摘P1: 休シフト削除でセルが勤務に化けない／休自体は削除禁止 ----

    private fun threeShiftState() = MagiState(
        startDate = "2026-06-01", endDate = "2026-06-03",
        // 休が index0 でない配置（旧実装のハードコード0が露呈するケース）
        shifts = listOf(Shift("A", "A", "1", ""), Shift("休", "休", "", ""), Shift("B", "B", "1", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0, 2)),   // skillIdx=2
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1)),
        groupShiftApt = listOf(listOf("", "", "")),
        schedule = listOf(listOf(0, 1, 2)),
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun removeShiftMapsDeletedCellsToRestAndBlocksRestDeletion() {
        val st = threeShiftState()
        val sched = arrayOf(intArrayOf(0, 1, 2))
        // A(idx0) を削除: A のセルは休(削除後 idx0)へ、休(1)→0、B(2)→1 に追従
        val r = Ws1Ops.removeShift(st, sched, 0)
        assertEquals("休", r.state.shifts[0].kigou)
        assertEquals(listOf(0, 0, 1), r.schedule[0].toList())
        // 休(idx1) 自体の削除は no-op（全休日が勤務へ化けるため禁止）
        val blocked = Ws1Ops.removeShift(st, sched, 1)
        assertEquals(3, blocked.state.shifts.size)
    }

    // ---- 判読性/レビュー指摘: 同一セルの複数違反で「重い族」のマークが軽い族に上書きされない ----

    @Test fun cellMarkKeepsHeaviestFamily() {
        // (0,0)=A で 希望=休(pref, HARD 9000) が発火し、かつ cons3 [A,B] の窓不成立(c3, SOFT 3) も (0,0) を
        // マークする。旧実装は評価順の最後(c3系)が後勝ちで vio-c3 に降格していた。修正後は vio-pref を保持。
        val st = MagiState(
            startDate = "2026-06-01", endDate = "2026-06-03",
            shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", "", ""), Shift("B", "B", "", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)),
            groupShiftApt = listOf(listOf("", "", "")),
            schedule = listOf(listOf(1, 0, 0)),                 // (0,0)=A, 残り休
            wishes = mapOf("0,0" to 0),                          // 希望=休 → pref違反
            staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(),
            cons3 = listOf(com.magi.app.model.C3Row(listOf("A", "B"))),   // A→B 必須連続(未完成=c3発火)
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val rep = UnifiedViolationChecker.check(st, st.schedule.toIntArray2D())
        assertTrue("pref と c3 の両方が計上される", (rep.breakdown["pref"] ?: 0) >= 1 && (rep.breakdown["c3"] ?: 0) >= 1)
        assertEquals("重い族(pref)のマークが保持される", "vio-pref", rep.violations["0,0"])
        // [Set化] cellFamilies は重なった全クラスを重み降順で保持し、先頭は violations と一致する
        val fams = rep.cellFamilies["0,0"] ?: emptyList()
        assertEquals("先頭=最重クラス", "vio-pref", fams.firstOrNull())
        assertTrue("軽い族(c3)も保持される", "vio-c3" in fams)
        for ((key, cls) in rep.violations) {
            assertEquals("全セルで families 先頭 == violations", cls, rep.cellFamilies[key]?.firstOrNull())
        }
    }

    @Test fun editStaffPreservesSkillIdx() {
        val st = threeShiftState()
        val ns = Ws1Ops.editStaff(st, 0, "改名した", 0)
        assertEquals("改名した", ns.staff[0].name)
        assertEquals(2, ns.staff[0].skillIdx)   // 旧実装は 0 に化けていた
    }

    @Test fun headerlessWishesCsvKeepsFirstRow() {
        val st = csvState()
        val headerless = WishesCsvIO.parse("花子,1,A\n花子,2,休", st)
        assertNotNull(headerless)
        assertEquals(2, headerless!!.accepted)
        assertEquals("[3.329.0] 読めない行は無い", 0, headerless.rejected)
        val withHeader = WishesCsvIO.parse("氏名,日,希望シフト\n花子,1,A", st)
        assertNotNull(withHeader)
        assertEquals(1, withHeader!!.accepted)
    }

    // --- [3.329.0/外部レビュー] 入力の意味論 ---

    @Test fun addStaffAndResizeFillWithResolvedRestShift() {
        // H-01: 休が index 0 でないデータ（先頭が勤務シフト）。新しい職員の行・伸ばした日は
        //   index 0 ではなく**休**で埋まること。
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-02",
            shifts = listOf(Shift("A", "A", "0", ""), Shift("B", "B", "0", ""), Shift("休", "休", "0", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)),
            groupShiftApt = listOf(listOf("", "", "")),
            schedule = listOf(listOf(0, 1)),
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val rest = restShiftIndex(st)
        assertEquals("前提: 休は index 2", 2, rest)
        val added = Ws1Ops.addStaff(st, st.schedule.toIntArray2D(), "s1", 0)
        assertTrue("新しい職員の全日が休", added.schedule[1].all { it == rest })
        val grown = Ws1Ops.resizeDays(st, st.schedule.toIntArray2D(), 4)
        assertEquals("伸ばした日は休", rest, grown.schedule[0][2])
        assertEquals("元の日は不変", 1, grown.schedule[0][1])
    }

    @Test fun componentImportReportsUnreadableRowsInsteadOfDroppingThem() {
        // H-02: 希望CSVは既存を全置換する。読めない行を黙って捨てると、その分の希望が消える。
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-03",
            shifts = listOf(Shift("休", "休", "0", ""), Shift("A", "A", "0", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("花子", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(List(3) { 0 }),
            wishes = mapOf("0,0" to 1, "0,1" to 0), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        // 1行は有効、2行は誤記（未知の氏名・未知の記号）。
        val r = WishesCsvIO.parse("花子,1,A\n太郎,1,A\n花子,2,Z", st)
        assertEquals("有効行", 1, r!!.accepted)
        assertEquals("読めない行を数える", 2, r.rejected)
        assertTrue("どこが悪いか示す", r.sample.isNotEmpty())
        // 全部読める場合は従来どおり置換できる。
        val ok = WishesCsvIO.parse("花子,1,A\n花子,2,休", st)
        assertEquals(0, ok!!.rejected)
        assertEquals(2, ok.accepted)
    }

    @Test fun constraintsImportRejectsUnknownKindInsteadOfWipingEverything() {
        // H-02: 種別の綴り違いで制約一式が消えるのを防ぐ。
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-03",
            shifts = listOf(Shift("休", "休", "0", ""), Shift("A", "A", "0", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("花子", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(List(3) { 0 }),
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val r = ConstraintsCsvIO.parse("連勤,2,休,1\n連勤日数,2,休,1", st)
        assertEquals(1, r!!.accepted)
        assertEquals("未知の種別を数える", 1, r.rejected)
        // 氏名・記号が解決できない個人レンジも同じ扱い。
        val r2 = ConstraintsCsvIO.parse("個人レンジ,太郎,A,1,2", st)
        assertEquals(0, r2!!.accepted)
        assertEquals(1, r2.rejected)
    }

    @Test fun removingSkillGroupLeavesMembersUnassignedNotInTheFirstGroup() {
        // [3.330.0/外部レビュー M-01] 削除した群の所属者を 0 へ寄せると、①無関係な先頭の群の制約が
        //   黙って掛かる ②最後の1群を消すと全員 0 になり、あとで群を足すと全員がそこに所属した扱い。
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-02",
            shifts = listOf(Shift("休", "休", "0", ""), Shift("A", "A", "0", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0, 0), Staff("s1", 0, 1), Staff("s2", 0, 2), Staff("s3", 0, -1)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = List(4) { listOf(0, 0) },
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
            skillGroups = listOf(Group("S0", "S0"), Group("S1", "S1"), Group("S2", "S2")),
        )
        val after = Ws1Ops.removeSkillGroup(st, 1)
        assertEquals("群が1つ減る", 2, after.skillGroups.size)
        assertEquals("前の群は不変", 0, after.staff[0].skillIdx)
        assertEquals("削除された群の所属者は未所属(-1)", -1, after.staff[1].skillIdx)
        assertEquals("後ろの群は1つ詰まる", 1, after.staff[2].skillIdx)
        assertEquals("元から未所属は不変", -1, after.staff[3].skillIdx)

        // 最後の1群を消しても、あとで群を足したときに全員が所属した扱いにならないこと。
        var s2 = st
        for (g in st.skillGroups.indices.reversed()) s2 = Ws1Ops.removeSkillGroup(s2, g)
        assertTrue("全員が未所属", s2.staff.all { it.skillIdx == -1 })
        // 群の追加は `skillGroups` に1件足すだけ（MagiViewModel.addSkillGroup と同じ操作）。
        val readded = s2.copy(skillGroups = s2.skillGroups + Group("S9", "S9"))
        assertTrue("群を足しても誰も所属しない", readded.staff.all { it.skillIdx == -1 })

        assertEquals("範囲外は何もしない", st, Ws1Ops.removeSkillGroup(st, 9))
    }
}
