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

    @Test fun constraintsCsvRejectsPatternWithAGap() {
        // [3.336.0/外部レビュー P2] `MUST連続,休,,A` は空セルで打ち切られ ["休"] になり、**A が黙って
        //   消えたまま accepted に数えられて**いた（3.333.0 の「評価されない行を受理しない」の取り残し）。
        val st = csvState()
        val gap = ConstraintsCsvIO.parse("MUST連続,休,,A", st)
        assertEquals("穴あきの並びは取り込まない", 0, gap!!.accepted)
        assertEquals(1, gap.rejected)
        assertEquals(0, gap.state.cons3.size)
        // 末尾が空なのは正常（並びは可変長）。
        val ok = ConstraintsCsvIO.parse("MUST連続,休,A", st)
        assertEquals(1, ok!!.accepted)
        assertEquals(0, ok.rejected)
        assertEquals(listOf("休", "A"), ok.state.cons3[0].pattern)
    }

    // ---- [3.336.0/敵対レビュー H3] c1 ブーストの採否が weightedScore を悪化させない ----
    //
    // `V6LateOperators` の gate は `betterReport` が偽でも「c1 が減る横移動」を採る例外(HF537互換)を持つ。
    // 旧条件の `lim = 200*high + 120*low` は**目的関数(high45 < low90)と大小が逆**なので、high−1/low+1 の
    // 入れ替えは lim を下げつつ weighted を悪化させられた。ここでは条件式そのものを反例で固定する
    // （gate は improve() の内側 private なので、同じ判定を外から検算する）。

    @Test fun c1BoostMustNotAcceptAWeightedRegression() {
        fun w(c1: Int, high: Int, low: Int) = c1 * 15.0 + high * 45.0 + low * 90.0
        fun lim(high: Int, low: Int) = 200 * high + 120 * low
        // cur: c1=5 high=2 low=1 ／ nv: c1=4 high=1 low=2
        val curW = w(5, 2, 1); val nvW = w(4, 1, 2)
        // 旧 boost の条件は全部通る
        assertTrue("c1 は減る", 4 < 5)
        assertTrue("lim は下がる", lim(1, 2) <= lim(2, 1))
        assertTrue("生の件数も増えない", (4 + 1 + 2) <= (5 + 2 + 1))
        // なのに weighted は悪化する＝旧実装はこれを採用していた
        assertTrue("反例が weighted を悪化させることの確認", nvW > curW)
        // 3.336.0 で足した条件がこれを弾く
        assertTrue("weighted 非増の条件で弾かれる", !(nvW <= curW))
    }

    // ---- レビュー指摘P1(3.106.0)＋方針転換(3.416.0): 休シフトも通常の編集規則 ----

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

    @Test fun removeShiftMapsDeletedCellsToRest() {
        val st = threeShiftState()
        val sched = arrayOf(intArrayOf(0, 1, 2))
        // A(idx0) を削除: A のセルは休(削除後 idx0)へ、休(1)→0、B(2)→1 に追従（3.106.0 の本体＝
        // ハードコード0で勤務シフトへ化けるバグの回帰）
        val r = Ws1Ops.removeShift(st, sched, 0)
        assertEquals("休", r.state.shifts[0].kigou)
        assertEquals(listOf(0, 0, 1), r.schedule[0].toList())
    }

    /** [3.416.0/方針「休は通常のシフト定義」] 休シフト自体も他シフトと同じ規則で削除できる。
     *  削除セルは**削除後の一覧**の既定シフト（「休」があればそれ、無ければ先頭）へ。
     *  旧実装（3.106.0）はここを no-op で禁止していた＝この2件は方針転換の回帰ガード。 */
    @Test fun removeShiftAllowsDeletingTheRestShiftItself() {
        val st = threeShiftState()   // shifts = [A, 休, B]
        val sched = arrayOf(intArrayOf(0, 1, 2))
        val r = Ws1Ops.removeShift(st, sched, 1)   // 休(idx1) を削除
        assertEquals(2, r.state.shifts.size)
        assertEquals(listOf("A", "B"), r.state.shifts.map { it.kigou })
        // 削除後の一覧に「休」が無い＝既定は先頭(A=0)。休セル(1)→0、B(2)→1 へ追従。範囲外や-1は出ない。
        assertEquals(listOf(0, 0, 1), r.schedule[0].toList())
        assertTrue(r.schedule[0].all { it in 0 until 2 })
    }

    /** 休が末尾indexのとき削除しても範囲外セルを作らない（旧式 `rest>k ? rest-1 : rest` は
     *  k==rest の末尾削除で削除済みindexを指し、正規化で -1 センチネル＝必須違反化していた形）。 */
    @Test fun removeShiftDeletingTrailingRestStaysInBounds() {
        val st = threeShiftState().copy(
            shifts = listOf(Shift("A", "A", "1", ""), Shift("B", "B", "1", ""), Shift("休", "休", "", "")),
            schedule = listOf(listOf(0, 1, 2)),
        )
        val sched = arrayOf(intArrayOf(0, 1, 2))
        val r = Ws1Ops.removeShift(st, sched, 2)   // 末尾の休を削除
        assertEquals(listOf("A", "B"), r.state.shifts.map { it.kigou })
        assertEquals(listOf(0, 1, 0), r.schedule[0].toList())   // 休セルは先頭(A)へ
        assertTrue(r.schedule[0].all { it in 0 until 2 })
    }

    /** [3.416.0] 休シフトの改名も通常経路＝制約参照（記号の文字列）が renameShiftInConstraints で追従し、
     *  「休」記号が消えた場合の既定シフト解決は先頭へ倒れる（検査2g が案内する既定挙動）。 */
    @Test fun editShiftRenamingRestFollowsConstraintsLikeAnyShift() {
        val st = threeShiftState().copy(
            cons1 = listOf(com.magi.app.model.C1Row("5", "休", "2")),
        )
        val r = Ws1Ops.editShift(st, 1, "公休", "公", "", "")
        assertEquals("公", r.shifts[1].kigou)
        assertEquals("公", r.cons1[0].shiftKigou)          // 窓ルールが改名へ追従＝同じシフトを指し続ける
        assertEquals(0, restShiftIndex(r))                  // 「休」記号は消えた＝既定解決は先頭へ
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

    @Test fun filledCellsAreAlwaysAShiftTheStaffMayActuallyWork() {
        // [3.418.0] 空きマスを埋めるとき、**その職員が担当できないシフトを置かない**。
        //   旧実装は担当可否を見ずに一律「休」で埋めていたため、担当可否から休を外した群
        //   （UI の担当可否チップで実際にできる操作）に職員を足す／期間を伸ばすと、
        //   その全日が groupViol(HARD 重み10000) になった。埋めた瞬間に必須違反が並ぶ。
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-02",
            shifts = listOf(Shift("A", "A", "0", ""), Shift("B", "B", "0", ""), Shift("休", "休", "0", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 0)),   // この群は休を担当できない
            groupShiftApt = listOf(listOf("", "", "")),
            schedule = listOf(listOf(0, 1)),
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        assertEquals("前提: 休は index 2 で、この群は担当できない", 2, restShiftIndex(st))
        assertTrue("前提: 群0 は休を担当できない", !Problem(st).canDo(0, 2))

        val added = Ws1Ops.addStaff(st, st.schedule.toIntArray2D(), "s1", 0)
        val pAdd = Problem(added.state)
        assertTrue("追加した職員の全日が担当可能なシフト",
            added.schedule[1].all { pAdd.canDo(1, it) })
        assertEquals("担当外シフトを置いていない（groupViol=0）",
            0, UnifiedViolationChecker.check(added.state, added.schedule).breakdown["groupViol"] ?: 0)

        val grown = Ws1Ops.resizeDays(st, st.schedule.toIntArray2D(), 4)
        val pGrow = Problem(grown.state)
        assertTrue("伸ばした日も担当可能なシフト",
            grown.schedule[0].all { pGrow.canDo(0, it) })
        assertEquals("元の日は不変", 1, grown.schedule[0][1])

        // 3つ目の埋め込み経路: シフト削除で空いたマス（s0 は day0 に A が入っている）。
        val removed = Ws1Ops.removeShift(st, st.schedule.toIntArray2D(), 0)
        val pRem = Problem(removed.state)
        assertTrue("消したシフトのマスも担当可能なシフト",
            removed.schedule[0].all { pRem.canDo(0, it) })
        assertEquals("担当外シフトを置いていない（groupViol=0）",
            0, UnifiedViolationChecker.check(removed.state, removed.schedule).breakdown["groupViol"] ?: 0)

        // [3.419.0] 4つ目の経路＝探索へ渡す初期盤面。範囲外の値と欠損セル（行が短い）を穴埋めするとき、
        //   入力の不備だけを理由に担当外シフトを置いて groupViol を作らない。
        val broken = st.copy(schedule = listOf(listOf(999, 1)))          // day0 が範囲外
        assertTrue("範囲外セルの穴埋めが担当可能なシフト",
            Problem(broken).initialAssignment()[0].all { Problem(broken).canDo(0, it) })
        val short = st.copy(schedule = listOf(listOf(1)))                // day1 が欠損（行が短い）
        assertTrue("欠損セルの穴埋めが担当可能なシフト",
            Problem(short).initialAssignment()[0].all { Problem(short).canDo(0, it) })
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
