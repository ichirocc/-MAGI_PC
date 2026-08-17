package com.magi.app.v6

import com.magi.app.model.*
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
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

    /**
     * [3.344.0] 「充足可能N枠」というサマリと「いまの希望のままではどう組んでも解消できません」という
     * 説明が**同じ枠に同時に出ていた**。`verdict` は「担当できる人数 >= 必要数」の静的判定なので
     * FIXABLE のまま残すのが正しい（希望を1件変えれば直りうる）が、それだけを数えたサマリは
     * 説明と矛盾する。実データ（real/user）でも `充足可能=3 不能=0` と出しながら3枠とも
     * 「どう組んでも解消できません」だった。判定を文字列でなく値（`blockedNow`）として持たせる。
     */
    @Test
    fun blockedNowSeparatesStaticCapacityFromWhatCanActuallyBeFilledNow() {
        val blocked = V6PortAnalyzer.diagnoseCoverage(cascadeChainState(cWished = true))
        val sfB = blocked.shortfalls.single { it.shiftIndex == 1 }
        assertEquals("枠は足りているので verdict は FIXABLE のまま", CoverageVerdict.FIXABLE, sfB.verdict)
        assertTrue("だが『いまの希望では埋められない』ことを値として持つ", sfB.blockedNow)
        assertEquals("サマリと説明が一致する", 1, blocked.blockedNowSlots)
        assertTrue("この盤面は探索を続けても covU が減らない", blocked.allBlockedNow)

        val fixable = V6PortAnalyzer.diagnoseCoverage(cascadeChainState(cWished = false))
        val sfF = fixable.shortfalls.single { it.shiftIndex == 1 }
        assertTrue("玉突きが実在するなら『今は不能』とは言わない", !sfF.blockedNow)
        assertEquals(0, fixable.blockedNowSlots)
        assertTrue("再実行で解消し得る盤面を『減りません』と断定しない", !fixable.allBlockedNow)
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

    /**
     * [3.297.0 壁の緩和導線の前提] ForbiddenDiag の `seqLabel` が、cons3n 行から
     * `Problem.resolveC3` と同じ意味論（**最初の空白まで**を本体とする）で作ったキーと一致すること。
     *
     * MagiViewModel.relaxForbiddenRule はこのキー一致だけを頼りに「壁になっている並び」を削除するため、
     * ここがずれると「削除ボタンを押しても何も消えない」か「別のルールが消える」。UI 層はホストで
     * コンパイルできないので、依存する不変条件を v6 層のテストとして固定する。
     * 空白を**除去**する `SettingFixAction.DELETE_DUP_SEQ` のキーとは意味が違う点も同時に押さえる。
     */
    @Test
    fun forbiddenRunSeqLabelMatchesRuleKeyDerivedFromCons3nRows() {
        val rows = listOf(
            C3Row(listOf("X", "X")),
            C3Row(listOf("X", "Y", "")),          // 末尾空白（実データで普通に出る形）
            C3Row(listOf("Y", "", "X")),          // 途中空白＝resolveC3 は ["Y"] として扱う
        )
        val st = forbiddenState(
            schedule = listOf(listOf(1, 1, 2, 0)),   // X X Y 休 → [X,X] と [X,Y] が1件ずつ
            cons3n = rows,
        )
        fun ruleKey(row: C3Row): String {
            val end = row.pattern.indexOfFirst { it.isBlank() }
            val body = if (end >= 0) row.pattern.subList(0, end) else row.pattern
            return body.joinToString("\u2192")
        }
        val keys = rows.map { ruleKey(it) }
        assertEquals(listOf("X\u2192X", "X\u2192Y", "Y"), keys)

        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        assertTrue("違反 run が検出されること", diag.totalRuns > 0)
        for (run in diag.runs) {
            assertTrue("seqLabel=${run.seqLabel} が cons3n 行のキーに含まれること", run.seqLabel in keys)
        }
        // 削除導線は同じ並びの重複行をまとめて消す＝キー一致で数えられること。
        assertEquals(1, keys.count { it == "X\u2192X" })
    }

    /**
     * [3.311.0] 1セルが**複数の**禁止連続 fire に関与する局面では、希望どおりのセルでも動かす価値がある。
     * 禁止「X→X」・行 X,X,X の中央セルは 2件の fire に関与し、休へ動かすと c3n 2→0 / pref 0→1 ＝
     * betterReport の第1キー hard が 2→1 と厳密に改善する（isBetter は採用する）。
     * 旧実装は `wishLocked && wish == cur` で HARD 差分を見ずに即 PINNED を返し、run 全体を
     * 「構造壁」と誤診していた。偽の壁は 3.281.0 の短い停滞タイムアウトを早期に発火させうる。
     */
    @Test
    fun wishPinnedCellIsNotAWallWhenMovingItRemovesTwoForbiddenFires() {
        val st = forbiddenState(
            schedule = listOf(listOf(1, 1, 1)),              // X X X → [X,X] が2件
            cons3n = listOf(C3Row(listOf("X", "X"))),
            wishes = mapOf("0,1" to 1),                      // 中央セルだけ X を希望固定
        )
        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        val center = diag.runs.flatMap { it.cells }.filter { it.dayIndex == 1 }
        assertTrue("中央セルが検出される", center.isNotEmpty())
        assertTrue(
            "希望固定でも正味の必須違反が減るなら壁ではない: ${center.map { it.escape }}",
            center.none { it.escape == ForbiddenCellEscape.PINNED },
        )
        assertFalse("この盤面を構造壁と誤診しない", diag.allBlocked)
    }

    /**
     * [3.343.0] **隣接日調整でも「正味の HARD が減るか」まで見る**。
     *
     * 3.311.0 で PINNED 判定へ `prefCost` を入れたとき、隣接日調整（ADJACENT）の分岐には入れ忘れて
     * いた。隣接日調整はこの職員の複数日を動かすので、本セルだけでなく行全体の希望違反が増えうる。
     *
     * この盤面（担当可能=休/X/Y の1名・T=3・行 X,Y,X・day1 は Y を希望固定）:
     *  - day0 はどの代替も新たな禁止連続を作り、隣接日 day1 は希望固定で動かせない＝BLOCKED。
     *  - day1 を「休」にすると `休→X` が新たに発火するが、day2 を「休」へ変えれば並びは崩せる。
     *    ところが day1 の希望を破るので **c3n 1→0 に対し pref 0→1＝正味の HARD は減らない**
     *    （weighted では 9000−7000＝+2000 の悪化で、`betterReport` は決して採用しない）。
     * 旧実装はこれを ADJACENT＝「崩せる」と誤って主張し、①利用者へ「探索が見つけていないだけ」と
     * 誤った期待を与え ②3.281.0 の停滞打ち切り（全 run 塞がりなら短い閾値）を発火させなくしていた。
     */
    @Test
    fun adjacentDayFixIsNotAnEscapeWhenItOnlyTradesForbiddenRunForABrokenWish() {
        val st = forbiddenState(
            schedule = listOf(listOf(1, 2, 1)),              // X Y X → [X,Y] が1件
            cons3n = listOf(
                C3Row(listOf("X", "Y")),                     // 本命の違反
                C3Row(listOf("Y", "Y")),                     // day0 を Y にしても崩れない
                C3Row(listOf("休", "X")),                    // day1 を休にすると新たに発火する
                C3Row(listOf("休", "Y")),                    // day0 を休にしても崩れない
            ),
            wishes = mapOf("0,1" to 2),                      // day1 は Y を希望固定
        )
        val diag = V6PortAnalyzer.diagnoseForbiddenRuns(st)
        assertEquals("違反 run は1件", 1, diag.totalRuns)
        val cells = diag.runs.flatMap { it.cells }
        assertTrue(
            "希望を破る代金のほうが高い手を『崩せる』と主張してはいけない: ${cells.map { it.dayIndex to it.escape }}",
            cells.none { it.escape == ForbiddenCellEscape.ADJACENT },
        )
        val pinned = cells.filter { it.dayIndex == 1 }
        assertTrue(
            "希望が効いていることを『希望固定』として説明する: ${pinned.map { it.escape }}",
            pinned.all { it.escape == ForbiddenCellEscape.PINNED },
        )
        assertTrue("全セル塞がり＝停滞打ち切りが正しく発火できる", diag.allBlocked)
    }

    /**
     * [3.377.0/実機ログ起因] 実機ログの同じ実行の中で、`CoverageDiag` が
     * 「充足可能2枠（うち2枠は いまの希望のままでは不能）＝この希望・担当のままでは人員不足は減りません」
     * と出し、設定ミス診断(検査9)が同じ2日を「証明つき」で名指ししているのに、`残存分析` だけが
     * `covU 2件` を **「まだ狙える」** に入れていた。原因は covU の構造判定が `hardFloor`
     * （有資格者数ベースの静的下限）しか見ておらず、そのログは `構造的HARD下限=0` だったこと。
     *
     * この盤面（`cascadeChainState(cWished = true)`）はまさに **担当者は足りる(FIXABLE)が
     * 希望固定で玉突きが完成しない** ＝ hardFloor では捉えられない形。
     */
    @Test
    fun residualAnalysisTreatsWishBlockedCovUAsAWallEvenWhenSupplyFloorIsZero() {
        val blocked = V6PortAnalyzer.diagnoseCoverage(cascadeChainState(cWished = true))
        val miss = V6FinalPort.covUBlockedAmount(blocked)
        assertEquals("いまの希望では埋められない不足人数を拾う", 1, miss)
        // hardFloor=0（担当者は足りるので供給下限は立たない）でも壁として数える。
        assertEquals("旧実装が『まだ狙える』に入れていた分が壁になる",
            1, V6FinalPort.covUStructuralWall(covUNow = 1, hardFloor = 0, blockedMiss = miss))

        // 玉突きが実在する盤面は従来どおり「まだ狙える」のまま＝壁と誤断定しない。
        val fixable = V6PortAnalyzer.diagnoseCoverage(cascadeChainState(cWished = false))
        val missF = V6FinalPort.covUBlockedAmount(fixable)
        assertEquals("解ける枠は壁に数えない", 0, missF)
        assertEquals(0, V6FinalPort.covUStructuralWall(covUNow = 1, hardFloor = 0, blockedMiss = missF))

        // 供給下限（従来の判定）は引き続き有効で、壁は covU 件数を超えない。
        assertEquals("構造的下限だけでも壁になる（従来の挙動）",
            2, V6FinalPort.covUStructuralWall(covUNow = 2, hardFloor = 2, blockedMiss = 0))
        assertEquals("壁は残存件数を超えない", 3, V6FinalPort.covUStructuralWall(3, 0, 99))
        assertEquals("covU が無ければ壁も無い", 0, V6FinalPort.covUStructuralWall(0, 5, 5))
    }
    // [3.391.0 実バグ回帰] 実現不能な希望（担当できないシフトへの希望）を「別シフトへ固定」として
    // capacity から外していたため、verdict が FIXABLE→INFEASIBLE へ倒れ「データ上、充足不可」という
    // 誤った断定を出していた。s1 は A を担当できるが、担当できない B への希望を持つ＝
    // wishLocked=false なのでこの枠へ回せる。旧実装なら capacity=1 < need=2 で INFEASIBLE。
    @Test
    fun infeasibleWishDoesNotShrinkCoverageCapacity() {
        val st = MagiState(
            startDate = "2025-12-01",
            endDate = "2025-12-01",
            shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "2", "2"), Shift("遅番", "B", "", "")),
            groups = listOf(Group("G", "G"), Group("H", "H")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            use2Patterns = true,
            // 群G は 休/A のみ担当可（B は担当不可）。
            groupShift = listOf(listOf(1, 1, 0), listOf(1, 1, 1)),
            groupShiftApt = listOf(listOf("", "", ""), listOf("", "", "")),
            schedule = listOf(listOf(1), listOf(0)),
            wishes = mapOf("1,0" to 2),   // s1 は担当できない B を希望＝実現不能
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val p = Problem(st)
        assertFalse("前提: s1 の B 希望は実現不能", p.wishLocked(1, 0))
        val diag = V6PortAnalyzer.diagnoseCoverage(st)
        val sf = diag.shortfalls.single()
        assertEquals("A の不足1件", 1, sf.miss)
        assertEquals("実現不能な希望は capacity を減らさない", CoverageVerdict.FIXABLE, sf.verdict)
        assertFalse("「充足不可」と断定しない", sf.reason.contains("充足不可"))
    }

    // [3.391.0 実バグ回帰] covO 側も同型。**希望が過剰シフトそのものを指し、かつ実現不能**でないと
    // 旧コードの `wish == k` を踏まないので、s0 を「A を担当できないのに A に在勤し、A を希望している」
    // 形にする（＝groupViol も立っている）。旧実装はこれを「希望固定＝動かせない」と案内していたが、
    // 実現不能な希望は凍結しない＝動かせるし、動かせば groupViol も同時に消える。
    @Test
    fun infeasibleWishIsNotReportedAsPinnedInSurplus() {
        val st = MagiState(
            startDate = "2025-12-01",
            endDate = "2025-12-01",
            shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "0", "0")),
            groups = listOf(Group("G", "G"), Group("H", "H")),
            staff = listOf(Staff("s0", 0), Staff("s1", 1)),
            use2Patterns = true,
            // 群G(s0) は 休 のみ担当可＝A は担当不可。群H(s1) は両方可。
            groupShift = listOf(listOf(1, 0), listOf(1, 1)),
            groupShiftApt = listOf(listOf("", ""), listOf("", "")),
            schedule = listOf(listOf(1), listOf(0)),   // s0 が担当外の A に在勤＝need 0 に対し過剰1
            wishes = mapOf("0,0" to 1),                 // s0 は担当できない A を希望＝実現不能
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val p = Problem(st)
        assertFalse("前提: s0 の A 希望は実現不能", p.wishLocked(0, 0))
        assertEquals("前提: 担当外セルなので groupViol が立っている",
            1, UnifiedViolationChecker.check(st, st.schedule.toIntArray2D()).breakdown["groupViol"])

        val sp = V6PortAnalyzer.diagnoseCoverage(st).surpluses.single()
        assertEquals("A の過剰1件", 1, sp.excess)
        // reason は 0 件でも「希望固定0人」というラベルを必ず含むので、件数で見る。
        assertTrue("実現不能な希望を「希望固定」に数えない: " + sp.reason, sp.reason.contains("希望固定0人"))
        assertTrue("動かせる候補として数える: " + sp.reason, sp.reason.contains("動かせる1人"))
    }

}
