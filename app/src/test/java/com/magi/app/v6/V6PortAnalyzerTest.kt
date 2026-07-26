package com.magi.app.v6

import com.magi.app.model.*
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class V6PortAnalyzerTest {
    @Test
    fun v6OverviewComputesAptAndRisk() {
        val st = MagiState(
            startDate = "2025-12-01",
            endDate = "2025-12-03",
            shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "1", "1")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            use2Patterns = true,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "2")),
            schedule = listOf(listOf(1, 1, 1), listOf(0, 0, 0)),
            wishes = emptyMap(),
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val report = UnifiedViolationChecker.check(st)
        val v6 = V6PortAnalyzer.analyze(st, st.schedule.toIntArray2D(), report)
        assertEquals(3, v6.demand)
        assertEquals(100, v6.coveragePct)
        assertTrue(v6.aptPenalty > 0.0)
        assertTrue(v6.sanityNotes.any { it.contains("groupShiftApt") })
    }

    // [実バグ修正の回帰] diagnoseCoverage が need1 のみを見て miss=need1-got を計算していたため、
    // need1 未設定・need2 単独定義（Problem.covUCell の「片方定義=その値」対応セル）の covU 違反が
    // 診断から丸ごと消えていた。need1="" / need2="2" で1人しか配置しない盤面を使い、
    // covUCell（source of truth）どおり不足1として検出されることを固定する。
    @Test
    fun diagnoseCoverageCatchesNeed2OnlyShortfall() {
        val st = MagiState(
            startDate = "2025-12-01",
            endDate = "2025-12-01",
            shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "", "2")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            use2Patterns = true,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(listOf(1), listOf(0)),
            wishes = emptyMap(),
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val diag = V6PortAnalyzer.diagnoseCoverage(st)
        assertEquals(1, diag.totalShortfall)
        assertEquals(1, diag.shortfalls.size)
        val sf = diag.shortfalls.single()
        assertEquals(1, sf.shiftIndex)
        assertEquals(1, sf.got)
        assertEquals(1, sf.miss)
    }

    // [3.263.0, 600秒改善ゼロの深い停滞調査で判明] 「玉突き」判定は1ホップ(直接移動が別のcovUを
    // 生むか)のみで、その先が実際に埋まる保証がなかった。実データ(findCovUChainを200 seed総当たり)
    // で「玉突き候補はいるが下流の唯一の候補が希望固定で誰も動けない」真の壁を確認したため、
    // findCovUChainで実在を検証してから案内を出し分けるよう修正。この2件は同一形状(X の covU、
    // Aが唯一の直接候補でYを空けるとcascade、CがYを埋める唯一の depth2 候補)で、Cの希望有無だけを
    // 変え、chainVerified の有無で案内文が変わることを固定する。
    private fun cascadeChainState(cWished: Boolean): MagiState = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-01",
        shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "1", ""), Shift("Y", "Y", "1", "")),
        groups = listOf(Group("GA", "GA"), Group("GC", "GC")),
        staff = listOf(Staff("A", 0), Staff("C", 1)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1), listOf(1, 0, 1)),   // GA:休/X/Y可 / GC:休/Y可(Xは不可)
        groupShiftApt = listOf(listOf("", "", ""), listOf("", "", "")),
        schedule = listOf(listOf(2), listOf(0)),                 // A=Y, C=休
        wishes = if (cWished) mapOf("1,0" to 0) else emptyMap(), // Cが休に希望固定
        staffRange = emptyMap(),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test
    fun diagnoseCoverageConfirmsCascadeWhenChainActuallyResolves() {
        val diag = V6PortAnalyzer.diagnoseCoverage(cascadeChainState(cWished = false))
        val sf = diag.shortfalls.single { it.shiftIndex == 1 }
        assertTrue("玉突き候補が本当に解消できるときは従来どおりの案内",
            sf.reason.contains("玉突き=ブロック移動") && sf.reason.contains("必要"))
        assertTrue("「どう組んでも解消できません」は出さない", !sf.reason.contains("どう組んでも"))
    }

    @Test
    fun diagnoseCoverageWarnsWhenCascadeIsBlockedByDownstreamWish() {
        // Cが唯一のdepth2候補だが休へ希望固定＝findCovUChainの候補から除外され連鎖が完成しない。
        val diag = V6PortAnalyzer.diagnoseCoverage(cascadeChainState(cWished = true))
        val sf = diag.shortfalls.single { it.shiftIndex == 1 }
        assertTrue("連鎖が実在しないことを正直に案内する",
            sf.reason.contains("どう組んでも解消できません"))
    }

    // [人員過剰(covO)の「なぜ減らないか」診断] 在勤2人のうち誰も希望固定・禁止連続に阻まれない盤面では
    // 「動かせる」人数が過剰人数と一致し、解消可能ヒントが出ることを固定する。
    @Test
    fun diagnoseCoverageMarksFreelyRelievableSurplus() {
        val st = MagiState(
            startDate = "2025-12-01",
            endDate = "2025-12-01",
            shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "1", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(listOf(1), listOf(1)),   // 両者ともA（必要1に対し現状2＝過剰1、休へ動かす余地あり）
            wishes = emptyMap(),
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val diag = V6PortAnalyzer.diagnoseCoverage(st)
        assertEquals(1, diag.totalSurplus)
        assertEquals(1, diag.surpluses.size)
        val sp = diag.surpluses.single()
        assertEquals(1, sp.shiftIndex)
        assertEquals(2, sp.got)
        assertEquals(1, sp.excess)
        assertTrue(sp.reason.contains("動かせる2人"))
        assertTrue(sp.reason.contains("解消可能"))
    }

    // 両者とも希望固定（希望どおりに配置済み＝pref違反ゼロ）だと、動かすと希望未充足に化けるため
    // 「動かせる」人数は0になり、希望調整が必要という理由が出ることを固定する
    // （実機ログで「回数制限のない有が増えない」問い合わせの根本原因の再現）。
    @Test
    fun diagnoseCoverageMarksWishPinnedSurplusAsUnmovable() {
        val st = MagiState(
            startDate = "2025-12-01",
            endDate = "2025-12-01",
            shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "1", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(listOf(1), listOf(1)),
            wishes = mapOf("0,0" to 1, "1,0" to 1),   // 両者ともAを希望固定
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val diag = V6PortAnalyzer.diagnoseCoverage(st)
        assertEquals(1, diag.totalSurplus)
        val sp = diag.surpluses.single()
        assertEquals(1, sp.excess)
        assertTrue(sp.reason.contains("希望固定2人"))
        assertTrue(sp.reason.contains("希望"))
    }

    // ==== [3.280.0] 禁止連続(c3n)の「なぜ崩せないか」診断 ====

    private fun forbiddenState(
        schedule: List<List<Int>>,
        cons3n: List<C3Row>,
        wishes: Map<String, Int> = emptyMap(),
        shifts: List<Shift> = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", "")),
        staff: List<Staff> = listOf(Staff("s0", 0)),
        groupShift: List<List<Int>> = listOf(List(3) { 1 }),
    ): MagiState {
        val days = schedule[0].size
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-" + days.toString().padStart(2, '0'),
            shifts = shifts, groups = List(groupShift.size) { Group("G$it", "G$it") },
            staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = groupShift.map { g -> g.map { "" } },
            schedule = schedule, wishes = wishes, staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = cons3n, cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    // 需要も希望も無い盤面の禁止連続は、どのセルも休へ変えるだけで安全に崩せる＝FREE。
    // FREE を含む run は「探索未到達の可能性」として案内される（もし残っていたら本物のシグナル）。
    @Test
    fun diagnoseForbiddenRunsMarksFreelyBreakableRunAsEscapable() {
        val st = forbiddenState(
            schedule = listOf(listOf(1, 1, 0)),               // X X 休 → [X,X] が1件
            cons3n = listOf(C3Row(listOf("X", "X"))),
        )
        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        assertEquals(1, diag.totalRuns)
        val run = diag.runs.single()
        assertTrue("安全な代替がある run は escapable", run.escapable)
        assertTrue(run.cells.any { it.escape == ForbiddenCellEscape.FREE })
        assertTrue("探索未到達の案内", run.hint.contains("探索未到達"))
    }

    // 両セルとも本人希望どおり＝動かすと pref(9000)>c3n(7000) の悪化で isBetter が却下する（設計どおり）。
    // 全セル PINNED → 構造的に崩せないことを正直に案内する（実機 c3n=1 が67エポック不動だった穴の再現）。
    @Test
    fun diagnoseForbiddenRunsReportsWishPinnedRunAsStructurallyBlocked() {
        val st = forbiddenState(
            schedule = listOf(listOf(1, 1, 0)),
            cons3n = listOf(C3Row(listOf("X", "X"))),
            wishes = mapOf("0,0" to 1, "0,1" to 1),           // 両セルとも X を希望固定
        )
        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        val run = diag.runs.single()
        assertTrue("全セル希望固定", run.cells.all { it.escape == ForbiddenCellEscape.PINNED })
        assertTrue(diag.allBlocked)
        assertTrue("希望固定の明示と対処の案内", run.hint.contains("希望固定") && run.hint.contains("残ります"))
    }

    // 離脱すると covU 穴が空くが、玉突き連鎖（findCovUChain=探索本体と同一関数）で埋め直せる局面は
    // CHAIN（実証済みの多段手）として案内される。
    @Test
    fun diagnoseForbiddenRunsVerifiesChainEscapeWhenDepartureCreatesCovU() {
        // P(需要1/日)を s0 が2連続（[P,P]禁止）。s0 が抜けた穴は休中の s1 が埋められる。
        val st = forbiddenState(
            schedule = listOf(listOf(1, 1), listOf(0, 0)),
            cons3n = listOf(C3Row(listOf("P", "P"))),
            shifts = listOf(Shift("休", "休", "", ""), Shift("P", "P", "1", ""), Shift("Q", "Q", "", "")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            groupShift = listOf(List(3) { 1 }),
        )
        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        val run = diag.runs.single()
        assertTrue("玉突き連鎖の実在を実証したうえで escapable",
            run.cells.any { it.escape == ForbiddenCellEscape.CHAIN })
        assertTrue(run.hint.contains("玉突き") || run.hint.contains("隣接日"))
    }

    // 同じ局面で唯一の受け皿 s1 が両日とも休へ希望固定されると連鎖が実在しなくなり、
    // 「covU受け皿なし」として全セル BLOCKED＝構造的な壁を正直に報告する（3.263.0 の教訓の c3n 版）。
    @Test
    fun diagnoseForbiddenRunsReportsNoReceiverWallWhenChainCannotFill() {
        val st = forbiddenState(
            schedule = listOf(listOf(1, 1), listOf(0, 0)),
            cons3n = listOf(C3Row(listOf("P", "P"))),
            wishes = mapOf("1,0" to 0, "1,1" to 0),           // s1 は両日とも休へ希望固定＝連鎖の受け皿なし
            shifts = listOf(Shift("休", "休", "", ""), Shift("P", "P", "1", ""), Shift("Q", "Q", "", "")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            groupShift = listOf(List(3) { 1 }),
        )
        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        val run = diag.runs.single()
        assertTrue("全セルが受け皿なしで塞がる", run.cells.all { it.escape == ForbiddenCellEscape.BLOCKED })
        assertTrue(run.cells.all { it.detail.contains("受け皿なし") })
        assertTrue(diag.allBlocked)
        // [3.284.0] 「証明」の強さを限定: 受け皿なしの塞がりは「探索手の全滅を検証」であり
        //   全空間の数学的証明ではない＝断定を避けた文言になったことを固定。
        assertTrue(run.hint.contains("崩せる見込みがありません"))
    }

    // 代替が全て新たな禁止連続を作る局面でも、隣接日調整（tryFixForbiddenRunViaAdjacentDay=
    // 探索本体と同一関数）で崩せるなら ADJACENT として実証つきで案内される。
    @Test
    fun diagnoseForbiddenRunsVerifiesAdjacentDayEscape() {
        // s0: [Q, P, P, 休]。禁止=[P,P](run@1-2)・[Q,休]・[Q,Q]。
        //   day1 の代替は 休→[Q,休]@0 / Q→[Q,Q]@0 と全て新たな禁止連続を作るが、
        //   day0 を Q→休 に隣接日調整すれば day1=休 が安全に置ける＝ADJACENT。
        //   day2 は休へ変えるだけで安全＝FREE（同一 run 内で両分類が同時に検証される）。
        val st = forbiddenState(
            schedule = listOf(listOf(2, 1, 1, 0)),
            cons3n = listOf(C3Row(listOf("P", "P")), C3Row(listOf("Q", "休")), C3Row(listOf("Q", "Q"))),
            shifts = listOf(Shift("休", "休", "", ""), Shift("P", "P", "", ""), Shift("Q", "Q", "", "")),
        )
        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        val run = diag.runs.single { it.seqLabel == "P→P" }
        val day1 = run.cells.single { it.dayIndex == 1 }
        val day2 = run.cells.single { it.dayIndex == 2 }
        assertEquals(ForbiddenCellEscape.ADJACENT, day1.escape)
        assertEquals(ForbiddenCellEscape.FREE, day2.escape)
        assertTrue(run.escapable)
    }
}
