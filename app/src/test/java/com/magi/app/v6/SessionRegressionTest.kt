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
        assertEquals(2, headerless!!.second)
        assertEquals(1, headerless.first.cons1.size)
        assertEquals(1, headerless.first.cons2.size)
        // ヘッダ有り: 従来どおりヘッダは落ちる
        val withHeader = ConstraintsCsvIO.parse("種別,a,b,c,d,e\n連勤,2,休,1", st)
        assertNotNull(withHeader)
        assertEquals(1, withHeader!!.second)
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
        assertEquals(2, headerless!!.second)
        val withHeader = WishesCsvIO.parse("氏名,日,希望シフト\n花子,1,A", st)
        assertNotNull(withHeader)
        assertEquals(1, withHeader!!.second)
    }
}
