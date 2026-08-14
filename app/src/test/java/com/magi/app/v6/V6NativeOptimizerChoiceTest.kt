package com.magi.app.v6

import com.magi.app.model.C3Row
import com.magi.app.model.C41Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Random

class V6NativeOptimizerChoiceTest {
    @Test fun autoBudgetChoosesExpectedAlgorithm() {
        // [3.284.0/外部レビュー=AUTO帯統一] ≤30 V5 / 31-210 RSI / それ以上 PORTFOLIO(適応epoch異種並列)。
        //   旧 31-90=ALNS はアプリ経路(optimizationPlan=RSI→ALNS複合)と食い違う二重分岐だったため、
        //   複合プランの主段= RSI へ寄せて統一（詳細は HypothesisDiversityPolicy.autoAlgorithmForBudget）。
        assertEquals(V6Algorithm.V5, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 10))
        assertEquals(V6Algorithm.RSI, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 60))
        assertEquals(V6Algorithm.RSI, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 150))
        assertEquals(V6Algorithm.PORTFOLIO, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.AUTO, 300))
        assertEquals(V6Algorithm.ALNS, V6NativeOptimizer.chooseAlgorithm(V6Algorithm.ALNS, 10))
    }

    @Test fun roleProfilesDiversifyWithBaselineFirst() {
        // [HF290 役割分担] W0 は必ず 1.0（ベースライン＝退化防止）、以降は探索(>1)/精製(<1)で多様化。
        assertEquals(1.0, V6NativeOptimizer.roleExploreFor(0), 1e-9)
        assertEquals(2.0, V6NativeOptimizer.roleExploreFor(1), 1e-9)   // 探索
        assertEquals(0.5, V6NativeOptimizer.roleExploreFor(2), 1e-9)   // 精製
        assertEquals(1.6, V6NativeOptimizer.roleExploreFor(3), 1e-9)
        assertEquals(0.6, V6NativeOptimizer.roleExploreFor(4), 1e-9)
    }

    // [3.228.0/ドッグフーディングで発見・修正] 仮説数上限撤廃(3.225.0)でi>=5の仮説が実際に生成される
    // ようになったが、旧実装はi>=5で全てroleExploreFor(0)=1.0・roleAcceptFor=SA・roleOpSelectFor=
    // ROULETTEへ縮退し、種(seed)以外ベースラインと同一の「クローン仮説」になっていた。i<5は不変
    // （既存テストroleProfilesDiversifyWithBaselineFirstが担保）で、i>=5が実際に多様化することを固定する。
    @Test fun roleProfilesDiversifyBeyondOldFixedArraySize() {
        val exploreValues = (5..12).map { V6NativeOptimizer.roleExploreFor(it) }
        // 旧実装のクローン値(=roleExploreFor(0)=1.0)へ縮退していないこと
        assertTrue("i>=5はベースライン(1.0)への縮退でないこと", exploreValues.none { kotlin.math.abs(it - 1.0) < 1e-6 })
        // 互いに異なる値へ分散していること（同じ値の繰り返しでない＝真の多様化）
        assertEquals("i=5..12の8個が全て相異なる値であること", 8, exploreValues.toSet().size)
        // 値域[0.35,2.4]に収まっていること
        assertTrue(exploreValues.all { it in 0.35..2.4 })

        // accept/opSelectも旧来の一律デフォルト(SA/ROULETTE)への縮退でなく複数モードへ分散していること
        val acceptModes = (5..10).map { V6NativeOptimizer.roleAcceptFor(it) }.toSet()
        assertTrue("i>=5のacceptModeが複数種に分散していること(旧: 全てSAに縮退)", acceptModes.size > 1)
        val opSelectModes = (5..10).map { V6NativeOptimizer.roleOpSelectFor(it) }.toSet()
        assertTrue("i>=5のopSelectModeが複数種に分散していること(旧: 全てROULETTEに縮退)", opSelectModes.size > 1)
    }

    @Test fun roleProfilesForIndicesBelowFiveAreUnaffectedByDiversification() {
        // [回帰] i<5の既存分岐(SA/GD/LAM・ROULETTE/THOMPSON)は3.228.0で一切変更していないこと。
        assertEquals(AcceptMode.SA, V6NativeOptimizer.roleAcceptFor(0))
        assertEquals(AcceptMode.SA, V6NativeOptimizer.roleAcceptFor(1))
        assertEquals(AcceptMode.GREAT_DELUGE, V6NativeOptimizer.roleAcceptFor(2))
        assertEquals(AcceptMode.LAM_ADAPTIVE, V6NativeOptimizer.roleAcceptFor(3))
        assertEquals(AcceptMode.GREAT_DELUGE, V6NativeOptimizer.roleAcceptFor(4))
        assertEquals(OpSelectMode.ROULETTE, V6NativeOptimizer.roleOpSelectFor(0))
        assertEquals(OpSelectMode.THOMPSON, V6NativeOptimizer.roleOpSelectFor(1))
        assertEquals(OpSelectMode.ROULETTE, V6NativeOptimizer.roleOpSelectFor(2))
    }

    @Test fun greatDelugeLevelDecaysFromInitialToBest() {
        // [論文活用] 時間予定型GD: frac=1(序盤)→initial、frac=0(終盤)→best へ線形降下、外れ値はクランプ。
        assertEquals(100.0, V6NativeOptimizer.greatDelugeLevel(100.0, 10.0, 1.0), 1e-9)
        assertEquals(10.0, V6NativeOptimizer.greatDelugeLevel(100.0, 10.0, 0.0), 1e-9)
        assertEquals(55.0, V6NativeOptimizer.greatDelugeLevel(100.0, 10.0, 0.5), 1e-9)
        assertEquals(100.0, V6NativeOptimizer.greatDelugeLevel(100.0, 10.0, 2.0), 1e-9)
        assertEquals(10.0, V6NativeOptimizer.greatDelugeLevel(100.0, 10.0, -1.0), 1e-9)
    }

    @Test fun roleAcceptAssignsGreatDelugeToSomeWorkers() {
        assertEquals(AcceptMode.SA, V6NativeOptimizer.roleAcceptFor(0))   // W0 ベースライン
        assertEquals(AcceptMode.SA, V6NativeOptimizer.roleAcceptFor(1))
        assertEquals(AcceptMode.GREAT_DELUGE, V6NativeOptimizer.roleAcceptFor(2))
        // [陳腐化修正] W3 は後日 Lam-Delosme 適応冷却(LAM_ADAPTIVE)へ多様化された（roleAcceptFor 実装＋専用コード）。
        //   テストが旧SA期待のまま残り V6 Engine Check が恒常赤だったのを実装に追随。
        assertEquals(AcceptMode.LAM_ADAPTIVE, V6NativeOptimizer.roleAcceptFor(3))
        assertEquals(AcceptMode.GREAT_DELUGE, V6NativeOptimizer.roleAcceptFor(4))
    }

    // [実機ログ起因=公平化のズレ] apt(適切回数)は旧 order に無く RSI 探索中は一度も focus されなかった
    //   （実データ検証: apt L1偏差合計37 vs staffRange低/高はわずか3）。maxViolatedFamily の SOFT
    //   フォールバックが apt を件数最大の族として選べることを固定する。
    private fun report(breakdown: Map<String, Int>, hard: Int = 0): ViolationReport = ViolationReport(
        violations = emptyMap(), needViolations = emptyMap(), countViolations = emptyMap(),
        breakdown = breakdown, total = breakdown.values.sum(), hard = hard,
        soft = breakdown.values.sum() - hard, weightedScore = breakdown.values.sum().toDouble(),
    )

    @Test fun maxViolatedFamilyPicksAptWhenDominantSoft() {
        val r = report(mapOf("apt" to 5, "c2" to 1, "covO" to 2))
        assertEquals("apt", V6NativeOptimizer.maxViolatedFamily(r))
    }

    @Test fun maxViolatedFamilyStillPrioritizesHardOverApt() {
        // [回帰] apt追加が既存のHARD優先ルール(D1/A1)を壊さないこと。
        val r = report(mapOf("c3n" to 1, "apt" to 100), hard = 1)
        assertEquals("c3n", V6NativeOptimizer.maxViolatedFamily(r))
    }

    @Test fun maxViolatedFamilyFallsBackToTotalWhenAllZero() {
        // [回帰/E8] 全族0件なら apt/weekly/fair 追加後も従来どおり "total"（件数0族を focus しない）。
        val r = report(emptyMap())
        assertEquals("total", V6NativeOptimizer.maxViolatedFamily(r))
    }

    // [同根の穴=weekly/fair] apt と同じ理由で未focusだった weekly/fair も件数最大なら選ばれること
    //   （実データ検証: weekly L1偏差合計65はaptの37より大きい・fair合計11）。aptが0のときのみ有効。
    @Test fun maxViolatedFamilyPicksWeeklyWhenDominantSoft() {
        val r = report(mapOf("weekly" to 65, "fair" to 11))
        assertEquals("weekly", V6NativeOptimizer.maxViolatedFamily(r))
    }

    // [ユーザー明示指示(2026-07-20)「weeklyをaptより優先順位を下げる」] weeklyの件数がaptより大きくても、
    //   aptに残りがあれば常にaptを優先する（HARD>SOFTと同型の絶対優先。件数比較には依らない）。
    @Test fun maxViolatedFamilyPrefersAptOverWeeklyEvenWhenWeeklyCountIsHigher() {
        val r = report(mapOf("weekly" to 65, "apt" to 37, "fair" to 11))
        assertEquals("apt", V6NativeOptimizer.maxViolatedFamily(r))
    }

    @Test fun maxViolatedFamilyStillPicksWeeklyWhenAptIsZero() {
        val r = report(mapOf("weekly" to 65, "apt" to 0, "fair" to 11))
        assertEquals("weekly", V6NativeOptimizer.maxViolatedFamily(r))
    }

    @Test fun maxViolatedFamilyAptOverWeeklyRuleRespectsAvoid() {
        // aptがavoid対象(HF63等でdeprioritize済み)なら、従来どおり件数最大のweeklyが選ばれる。
        val r = report(mapOf("weekly" to 65, "apt" to 37, "fair" to 11))
        assertEquals("weekly", V6NativeOptimizer.maxViolatedFamily(r, avoid = setOf("apt")))
    }

    @Test fun maxViolatedFamilyPicksFairWhenDominantSoft() {
        val r = report(mapOf("fair" to 11, "c2" to 1))
        assertEquals("fair", V6NativeOptimizer.maxViolatedFamily(r))
    }

    // [3.204.0/実機ログ起因] covO は日×シフトのセル単独違反のため件数が常に一桁台に留まり、
    // c1/c42/weekly のような数十件規模の族に「件数最大」選択で恒久的に勝てない
    // （CoverageDiag診断で「動かせる」と出たcovOセルが300秒経っても解消されないことを実機ログで確認）。
    // 3ラウンドに1回、count>0のcovOを件数によらず優先する周期的保証枠を固定する。
    @Test fun maxViolatedFamilyGivesCovOPeriodicSlotEvenWhenSmall() {
        val r = report(mapOf("c1" to 87, "covO" to 2))
        assertEquals("covO", V6NativeOptimizer.maxViolatedFamily(r, round = 2))
        assertEquals("covO", V6NativeOptimizer.maxViolatedFamily(r, round = 5))
    }

    @Test fun maxViolatedFamilyCovOPeriodicSlotDoesNotFireOnOtherRounds() {
        // [回帰] round%3==2 以外は従来どおり件数最大(c1)が選ばれること。
        val r = report(mapOf("c1" to 87, "covO" to 2))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r, round = 0))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r, round = 1))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r))   // round省略(-1)も従来どおり
    }

    @Test fun maxViolatedFamilyStillPrioritizesHardOverCovOPeriodicSlot() {
        // [回帰] covO周期枠の追加がD1/A1のHARD優先ルールを壊さないこと。
        val r = report(mapOf("c3n" to 1, "covO" to 5), hard = 1)
        assertEquals("c3n", V6NativeOptimizer.maxViolatedFamily(r, round = 2))
    }

    @Test fun maxViolatedFamilyCovOPeriodicSlotRespectsAvoid() {
        // [回帰] E9冷却等でcovOがavoidに入っていれば周期枠も発火せず通常選択にフォールバックすること。
        val r = report(mapOf("c1" to 87, "covO" to 2))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r, avoid = setOf("covO"), round = 2))
    }

    // [3.207.0/実機ログ起因=3.204.0の周期枠が典型的な5ラウンドRSIで丸ごと空振りしていた] round%3==2 の
    // 唯一の該当ラウンド(0始まりで2番目)は、HF63がc3n/covUをdeprioritizeし終える前(約3ラウンドの停滞を要する)
    // に来てしまい、HARD優先ループがそのラウンドを消費してcovO分岐へ到達しなかった（実機ログ: round=3/5
    // focus=c3n、covO=6は最後まで不変）。HARDが本当に解けない場合はHF63が最終的にdeprioritizeし尽くすため、
    // RSIフェーズの最終ラウンドを追加の保証枠にする。
    @Test fun maxViolatedFamilyGivesCovOFinalRoundSlotEvenOutsidePeriodicModulo() {
        val r = report(mapOf("weekly" to 56, "covO" to 6))
        // round=4は5ラウンド構成(roundsTotal=5)の最終ラウンド(0始まり)。4%3=1で周期枠には該当しないが
        // 最終ラウンドとして発火し、件数最大(weekly=56)を上書きしてcovOが選ばれること。
        assertEquals("covO", V6NativeOptimizer.maxViolatedFamily(r, round = 4, roundsTotal = 5))
    }

    @Test fun maxViolatedFamilyFinalRoundSlotRequiresRoundsTotalToBeProvided() {
        // [回帰] roundsTotal省略(-1、旧経路)では最終ラウンド判定が無効化され従来どおり件数最大に戻ること。
        val r = report(mapOf("weekly" to 56, "covO" to 6))
        assertEquals("weekly", V6NativeOptimizer.maxViolatedFamily(r, round = 4))
    }

    @Test fun maxViolatedFamilyFinalRoundSlotStillRespectsHardPriorityAndAvoid() {
        // [回帰] 最終ラウンドの保証枠もHARD優先ルールとavoidを壊さないこと。
        val r1 = report(mapOf("c3n" to 1, "covO" to 6), hard = 1)
        assertEquals("c3n", V6NativeOptimizer.maxViolatedFamily(r1, round = 4, roundsTotal = 5))
        val r2 = report(mapOf("weekly" to 56, "covO" to 6))
        assertEquals("weekly", V6NativeOptimizer.maxViolatedFamily(r2, avoid = setOf("covO"), round = 4, roundsTotal = 5))
    }

    // [3.208.0/提供された全実機ログ(7本)でaptが同型のstarvationを起こしていたことを確認] apt は常に
    // breakdown 最小級(1〜11、他族の一桁〜二桁下)で、"focus=apt" は一度も出現せず"focus=weekly"のみが
    // 件数最大フォールバックで選ばれ続けていた。covOとは別の周期(round%3==1)と最終ラウンドをaptにも割当てる。
    @Test fun maxViolatedFamilyGivesAptPeriodicSlotEvenWhenSmall() {
        val r = report(mapOf("c1" to 87, "apt" to 1))
        assertEquals("apt", V6NativeOptimizer.maxViolatedFamily(r, round = 1))
        assertEquals("apt", V6NativeOptimizer.maxViolatedFamily(r, round = 4))
    }

    @Test fun maxViolatedFamilyAptPeriodicSlotDoesNotFireOnOtherRounds() {
        // [回帰] round%3==1 以外(かつ最終ラウンドでもない)は従来どおり件数最大(c1)が選ばれること。
        val r = report(mapOf("c1" to 87, "apt" to 1))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r, round = 0))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r, round = 2))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r))   // round省略(-1)も従来どおり
    }

    @Test fun maxViolatedFamilyAptAndCovOPeriodicSlotsDoNotCollide() {
        // [回帰] apt(round%3==1)とcovO(round%3==2)は別ラウンドに割当てられ、互いを上書きしないこと。
        val r = report(mapOf("c1" to 87, "apt" to 1, "covO" to 6))
        assertEquals("apt", V6NativeOptimizer.maxViolatedFamily(r, round = 1))
        assertEquals("covO", V6NativeOptimizer.maxViolatedFamily(r, round = 2))
    }

    @Test fun maxViolatedFamilyFinalRoundPrefersAptOverCovOWhenBothPresent() {
        // [3.239.0で固定順→件数比較へ訂正] apt=1 < covO=6 のため、より件数が少ない(=件数最大選択に
        // 構造的に勝てない)方であるaptが選ばれる。結果は旧実装(固定でapt優先)と同じだが、判定基準が
        // 「常にapt優先」から「件数が少ない方優先」へ変わったことをこのテスト自体でも明示する。
        val r = report(mapOf("weekly" to 56, "apt" to 1, "covO" to 6))
        assertEquals("apt", V6NativeOptimizer.maxViolatedFamily(r, round = 4, roundsTotal = 5))
    }

    @Test fun maxViolatedFamilyFinalRoundPrefersCovOWhenAptIsLarger() {
        // [3.239.0/実機ログ起因の実バグ修正] 旧実装は最終ラウンドで常にaptを先にチェックする固定順
        // だったため、apt=29 > covO=4（実機ログで確認された逆転データ）でもaptが選ばれ、covOには
        // 一度も到達しなかった（8/26のcovO過剰1が「動かせる」診断なのに300秒経っても未解消だった
        // 根本原因の一つ）。新ロジックでは件数が少ない方(covO)を優先する。
        val r = report(mapOf("c1" to 164, "apt" to 29, "covO" to 4))
        assertEquals("covO", V6NativeOptimizer.maxViolatedFamily(r, round = 4, roundsTotal = 5))
    }

    @Test fun maxViolatedFamilyAptSlotStillRespectsHardPriorityAndAvoid() {
        // [回帰] aptの保証枠もHARD優先ルールとavoidを壊さないこと。
        val r1 = report(mapOf("c3n" to 1, "apt" to 1), hard = 1)
        assertEquals("c3n", V6NativeOptimizer.maxViolatedFamily(r1, round = 1))
        val r2 = report(mapOf("c1" to 87, "apt" to 1))
        assertEquals("c1", V6NativeOptimizer.maxViolatedFamily(r2, avoid = setOf("apt"), round = 1))
    }

    // [3.204.0] covO は markNeed(k,j) で needViolations に載り report.violations(セル"i,j"マップ)には
    // 現れないため、他の focus 未対応族が使う destroyRepairViolations では covO 専用のヒントが無かった。
    // applyCovOFree が「動かせる」在勤者を実際に他シフトへ移し covO を解消することを固定する。
    // [3.253.0] 両staffを別々の単独群(G0/G1)にする（m<2で fair 対象外＝covO単体の効果を汚染しない）。
    //   旧実装は同一群(G0)2名だったため、a,b どちらも group内公平化(fair)の唯一のペアとなり、
    //   commitBestMoveの全体評価導入後は「covO解消の代わりにfairが同量発生する」ちょうど中立な
    //   トレードになって不採用になっていた（実データ検証で判明した Free 系共通の欠陥そのものを
    //   このテスト自身が偶然踏んでいた＝新実装が正しく動作している証拠）。
    private fun covOState(schedule: List<List<Int>>, wishes: Map<String, Int> = emptyMap()): MagiState = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-01",
        shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "1", "")),
        groups = listOf(Group("G0", "G0"), Group("G1", "G1")),
        staff = listOf(Staff("a", 0), Staff("b", 1)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1), listOf(1, 1)), groupShiftApt = listOf(listOf("", ""), listOf("", "")),
        schedule = schedule, wishes = wishes, staffRange = emptyMap(),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
        cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
        cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun applyCovOFreeRelievesFreelyMovableSurplus() {
        val st = covOState(listOf(listOf(1), listOf(1)))   // 両者ともA（必要1に対し現状2＝過剰1）
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["covO"])
        val applied = V6NativeOptimizer.applyCovOFree(st, sched, Random(1))
        assertEquals(1, applied)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals(0, after.breakdown["covO"] ?: 0)
        assertEquals(0, after.hard)
    }

    @Test fun applyCovOFreeLeavesWishPinnedSurplusUntouched() {
        // 両者ともAを希望固定（希望どおり配置済み）だと、動かすと希望未充足(pref)に化けるため何もしない。
        val st = covOState(listOf(listOf(1), listOf(1)), wishes = mapOf("0,0" to 1, "1,0" to 1))
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["covO"])
        val applied = V6NativeOptimizer.applyCovOFree(st, sched, Random(1))
        assertEquals(0, applied)
        assertEquals(1, sched[0][0])
        assertEquals(1, sched[1][0])
    }

    // [3.226.0/隣接日調整] 8/26相当の実機ケース: covO対象(休, day1)の唯一の在勤者(a)が、移動先候補
    // すべて禁止連続(c3n)で塞がっている（直接候補X→"Y,X"禁止・直接候補Y→"Y,Y"禁止）。旧実装は
    // どちらも即諦めて covO を残していたが、隣接日調整(day0のYを休へ変更しパターンを崩す)で
    // X側の候補が解放され解消できることを固定する。
    @Test fun applyCovOFreeResolvesViaAdjacentDayFixWhenAllDirectMovesAreForbidden() {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", ""))
        val groups = listOf(Group("G0", "G0"))
        val groupShift = listOf(listOf(1, 1, 1))
        val staff = listOf(Staff("a", 0), Staff("b", 0))
        val schedule = listOf(
            listOf(2, 0, 1),   // a: Y, 休(covO対象), X
            listOf(1, 1, 1),   // b: X, X, X（休には関与しない）
        )
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-03",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = List(1) { List(3) { "" } },
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = mapOf("0,1" to "0"), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = listOf(C3Row(listOf("Y", "X")), C3Row(listOf("Y", "Y"))),
            cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["covO"])
        assertEquals(0, before.breakdown["c3n"] ?: 0)
        val p = cachedProblem(st)
        assertTrue("直接移動は両候補とも禁止連続で塞がる前提", p.makesForbiddenRun(sched, 0, 1, 1))
        assertTrue("直接移動は両候補とも禁止連続で塞がる前提", p.makesForbiddenRun(sched, 0, 1, 2))

        val applied = V6NativeOptimizer.applyCovOFree(st, sched, Random(2))
        assertEquals(1, applied)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals(0, after.breakdown["covO"] ?: 0)
        assertEquals(0, after.breakdown["c3n"] ?: 0)
        assertEquals(0, after.hard)
        assertEquals(0, sched[0][0])   // a の day0 が休へ変わりYYYパターンを崩す
        assertEquals(1, sched[0][1])   // a の day1 がXへ（禁止連続を回避しつつ使われる）
    }

    // [smoke] focus="apt" が destroyRepairStaff 経路(low/high/c2と合流)へ正しくルーティングされ、
    //   例外なく同一次元の盤面を返すこと。改善量そのものはラウンド単位 keep-best が別途保証する。
    @Test fun rsiGenerateHypothesisAptFocusReturnsValidSchedule() {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("P", "P", "1", ""))
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("a", 0), Staff("b", 0))
        val schedule = listOf(listOf(0, 1, 0, 1), listOf(1, 0, 1, 0))
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-04",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)), groupShiftApt = listOf(listOf("", "1")),
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val base = st.schedule.toIntArray2D()
        val rep = UnifiedViolationChecker.check(st, base)
        val out = V6NativeOptimizer.rsiGenerateHypothesis(st, base, rep, "apt", Random(1))
        assertNotNull(out)
        assertEquals(base.size, out.size)
        assertEquals(base[0].size, out[0].size)
        // [smoke] weekly/fair も同じ経路（apt同様、専用オペレータ不要で例外なく完走すること）。
        for (focus in listOf("weekly", "fair")) {
            val out2 = V6NativeOptimizer.rsiGenerateHypothesis(st, base, rep, focus, Random(1))
            assertNotNull(out2)
            assertEquals(base.size, out2.size)
        }
    }

    // [smoke] focus="covO" が新設の applyCovOFree 経路へ正しくルーティングされ、例外なく同一次元の
    //   盤面を返すこと。実際の解消効果は applyCovOFreeRelievesFreelyMovableSurplus が直接検証する。
    @Test fun rsiGenerateHypothesisCovOFocusReturnsValidSchedule() {
        val st = covOState(listOf(listOf(1), listOf(1)))
        val base = st.schedule.toIntArray2D()
        val rep = UnifiedViolationChecker.check(st, base)
        val out = V6NativeOptimizer.rsiGenerateHypothesis(st, base, rep, "covO", Random(1))
        assertNotNull(out)
        assertEquals(base.size, out.size)
        assertEquals(base[0].size, out[0].size)
    }

    // [3.241.0/実機ログ起因=専用オペレータの改善がdestroyRepairDayで相殺される順序バグ修正]
    //   旧実装は「applyCovOFree→destroyRepairDay×6」の順で、destroyRepairDayのdestroy段階
    //   （非希望セルを休へ変える）がneed<=0のシフト(休)へは一切repairが働かないため、直前に
    //   applyCovOFreeが解消したcovOをdestroy段階で再発させ、そのまま最終hypothesisに残っていた
    //   （8/26の休過剰1がcovO focusのラウンドでも解消されなかった実例の再現）。T=1日のため
    //   destroyRepairDay(6回)は必ずこの日を選ぶ＝決定的に検証できる。過剰1人(b)をCへ動かせば
    //   受け皿あり(need1/need2とも未設定=完全無制限)という最小構成で、新しい順序(destroyRepairDay→
    //   applyCovOFree)なら複数seedいずれでも最終的にcovOが解消されることを固定する。
    //   [手計算で確認済みの罠] AのneedはCとは異なり need1=1(use2Patterns=false のため実質上限も1)
    //   であり、既に a が1人埋めているため b を A へ動かすと新たな covO(A) を作ってしまい
    //   applyCovOFree のガードで拒否される＝わざと「Aは受け皿でない・Cのみが受け皿」という
    //   構成にして、destroyRepairDay の repair 段階(need>0のAのみ埋め戻す)がこの b→C の解決を
    //   決して代替できないことも同時に検証する。
    // [3.253.0] 過剰の起点をAに変更（旧: 休。休を過剰起点にすると、修復のb:休→Cが本人の
    //   休↔勤務(weekly)分類を跨ぐため T=1 日ではweeklyの偏差とcovOの解消が必ず同量で相殺される
    //   ちょうど中立なトレードになっていた＝Free系共通欠陥をこのテスト自身が踏んでいた）。
    //   A→A(過剰)→Cはどちらも「勤務」区分でweeklyの分類を跨がないため中立化しない。
    //   また両staffを別々の単独群(G0/G1)にしfair対象外にする（covOState と同じ理由）。
    private fun covOOrderState(): MagiState = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-01",
        shifts = listOf(Shift("休み", "休", "", ""), Shift("早番", "A", "1", ""), Shift("雑務", "C", "", "")),
        groups = listOf(Group("G0", "G0"), Group("G1", "G1")),
        staff = listOf(Staff("a", 0), Staff("b", 1)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1), listOf(1, 1, 1)), groupShiftApt = listOf(listOf("", "", ""), listOf("", "", "")),
        schedule = listOf(listOf(1), listOf(1)),   // a=A(需要どおり1人), b=A(過剰1人)
        wishes = emptyMap(), staffRange = emptyMap(),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
        cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
        cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun rsiGenerateHypothesisCovOFocusOrderKeepsFixAfterDestroyRepairDay() {
        val st = covOOrderState()
        val base = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, base)
        assertEquals("初期 covO=1（休の過剰1）", 1, before.breakdown["covO"] ?: 0)

        for (seed in 1L..5L) {
            val out = V6NativeOptimizer.rsiGenerateHypothesis(st, base, before, "covO", Random(seed))
            val after = UnifiedViolationChecker.check(st, out)
            assertEquals("seed=$seed: destroyRepairDayを先に・applyCovOFreeを最後に実行する順序のため" +
                "hypothesisの最終状態でcovOが解消されていること", 0, after.breakdown["covO"] ?: 0)
        }
    }

    // [3.209.0] c41/c41s は covO と同じく markNeed(needViolations) にしか載らず、GLSキック/
    // destroyRepairViolations が一切ヒントを持てない。applyC41Free が群レンジ[l,u]の超過・不足を
    // 実際に動かして解消することを固定する。
    private fun c41State(schedule: List<List<Int>>, l: String, u: String, wishes: Map<String, Int> = emptyMap()): MagiState {
        val days = schedule.firstOrNull()?.size ?: 1
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-0$days",
            shifts = listOf(Shift("休み", "Y", "", ""), Shift("勤務", "X", "", "")),
            groups = listOf(Group("G0", "G0")),
            staff = listOf(Staff("a", 0), Staff("b", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)), groupShiftApt = listOf(listOf("", "")),
            schedule = schedule, wishes = wishes, staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = listOf(C41Row(groupKigou = "G0", shiftKigou = "X", l = l, u = u)),
            cons42 = emptyList(),
        )
    }

    // [3.253.0] T=2へ拡張し、2日目に「修復後は a/b の X・Y 回数が完全に対称化する」背景を1日分だけ
    //   固定で持たせた（旧: T=1・単日のみだったため、a のみを動かす修復は必ず group内公平化(fair)を
    //   ちょうど同量発生させ、commitBestMoveの全体評価導入後は中立トレードとして不採用になっていた
    //   ＝Free系共通欠陥をこのテスト自身が踏んでいた。日1固定＋日0修復で対称化するよう設計）。
    @Test fun applyC41FreeRelievesFreelyMovableExcess() {
        // 群定員[0,1]に対し day0 は2名がXに在籍＝超過1。day1(背景, a=X,b=Y)は対称化のため固定。
        val st = c41State(listOf(listOf(1, 1), listOf(1, 0)), l = "0", u = "1")
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c41"] ?: 0)
        val applied = V6NativeOptimizer.applyC41Free(st, sched, Random(1), skill = false)
        assertEquals(1, applied)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals(0, after.breakdown["c41"] ?: 0)
        assertEquals(0, after.hard)
    }

    @Test fun applyC41FreeFillsFreelyMovableDeficiency() {
        // 群定員[1,2]に対し day0 は0名がXに在籍＝不足1。day1(背景, a=Y,b=X)は対称化のため固定。
        val st = c41State(listOf(listOf(0, 0), listOf(0, 1)), l = "1", u = "2")
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c41"] ?: 0)
        val applied = V6NativeOptimizer.applyC41Free(st, sched, Random(1), skill = false)
        assertEquals(1, applied)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals(0, after.breakdown["c41"] ?: 0)
        assertEquals(0, after.hard)
    }

    @Test fun applyC41FreeLeavesWishPinnedExcessUntouched() {
        // 両者ともXを希望固定（希望どおり配置済み）だと、動かすと希望未充足に化けるため何もしない。
        val st = c41State(listOf(listOf(1), listOf(1)), l = "0", u = "1", wishes = mapOf("0,0" to 1, "1,0" to 1))
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c41"] ?: 0)
        val applied = V6NativeOptimizer.applyC41Free(st, sched, Random(1), skill = false)
        assertEquals(0, applied)
        assertEquals(1, sched[0][0])
        assertEquals(1, sched[1][0])
    }

    @Test fun applyC41FreeIsNoOpWhenRulesEmpty() {
        val st = c41State(listOf(listOf(1), listOf(1)), l = "0", u = "1").copy(cons41 = emptyList())
        val sched = st.schedule.toIntArray2D()
        assertEquals(0, V6NativeOptimizer.applyC41Free(st, sched, Random(1), skill = false))
    }

    // [監査(他の制約は大丈夫か)/玉突き連鎖の横展開その4] 旧実装は「離脱元/到着先どちらもcovU/covO
    //   非悪化」の直接移動が見つからないと即座に諦めていた。c3mn(3.214.0)/RangePolish(3.215.0)/
    //   C3RunPolishと同型の穴: 離脱元シフト(X)自体が全体needをちょうど単独充足しており、誰か1人が
    //   離脱すると即covU化する構造的にブロックされた局面。findCovUChainで別職員に玉突き充填すれば
    //   解消できることを固定する。
    // [3.253.0] T=2へ拡張。day1に「修復後は A/B の X・Y 回数が対称化する」背景を1日固定で追加
    //   （旧: T=1・単日だったため、Aのみを動かす修復が group内公平化(fair)をちょうど同量発生させ、
    //   commitBestMoveの全体評価導入後は中立トレードとして不採用になっていた＝Free系共通欠陥の再発）。
    @Test fun applyC41FreeResolvesExcessViaChainWhenDirectMoveWouldCreateCovU() {
        // shift: 0=休(need無) 1=X(c41対象、need1=2でA/Bちょうど単独充足) 2=Y(need無、Aの逃げ先)
        //   3=Z(need無、Cのday0在籍地)
        val shifts = listOf(
            Shift("休", "休", "", ""), Shift("X", "X", "2", ""),
            Shift("Y", "Y", "", ""), Shift("Z", "Z", "", ""),
        )
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"))
        val groupShift = listOf(
            listOf(1, 1, 1, 0), // G0(A,B)=休,X,Y
            listOf(1, 1, 1, 1), // G1(C)=休,X,Y,Z
        )
        val staff = listOf(Staff("A", 0), Staff("B", 0), Staff("C", 1))
        // day0: A=X,B=X,C=Z（G0のX在籍2名=超過1、Xのneed1=2をちょうど充足）。
        // day1(背景, 固定): A=X,B=Y,C=X（G0のX在籍=Aのみ=1、need1=2はA,Cが充足）。
        val schedule = listOf(listOf(1, 1), listOf(1, 2), listOf(3, 1))
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-02",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = List(2) { List(4) { "" } },
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = listOf(C41Row(groupKigou = "G0", shiftKigou = "X", l = "0", u = "1")),
            cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c41"] ?: 0)
        assertEquals(0, before.hard)   // Xはneed1=2をA/Bがちょうど充足＝離脱すると即covU化する構造的にブロックされた局面

        val applied = V6NativeOptimizer.applyC41Free(st, sched, Random(1), skill = false)
        assertTrue("玉突き連鎖を含め何らかの手が採用されている", applied > 0)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals("c41超過が解消", 0, after.breakdown["c41"] ?: -1)
        assertEquals("HARDは悪化しない", 0, after.hard)
        assertEquals("Xの被覆(covU)は悪化しない", 0, after.breakdown["covU"] ?: -1)
    }

    @Test fun applyC41FreeResolvesDeficiencyViaChainWhenDirectMoveWouldCreateCovU() {
        // shift: 0=休(need無) 1=X(c41対象、need無) 2=Y(need1=1、Aの現在地) 3=W(need1=1、Bの現在地)
        //   4=Z(need無、Cの現在地)。offShift候補(A,B)がどちらもneed1を単独充足しており直接移動は不可。
        val shifts = listOf(
            Shift("休", "休", "", ""), Shift("X", "X", "", ""),
            Shift("Y", "Y", "1", ""), Shift("W", "W", "1", ""), Shift("Z", "Z", "", ""),
        )
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"))
        val groupShift = listOf(
            listOf(1, 1, 1, 1, 0), // G0(A,B)=休,X,Y,W
            listOf(1, 0, 1, 1, 1), // G1(C)=休,Y,W,Z
        )
        val staff = listOf(Staff("A", 0), Staff("B", 0), Staff("C", 1))
        val schedule = listOf(listOf(2), listOf(3), listOf(4)) // A=Y, B=W（G0のX在籍0名=不足1）, C=Z
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-01",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = List(2) { List(5) { "" } },
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = listOf(C41Row(groupKigou = "G0", shiftKigou = "X", l = "1", u = "2")),
            cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c41"] ?: 0)
        assertEquals(0, before.hard)

        val applied = V6NativeOptimizer.applyC41Free(st, sched, Random(1), skill = false)
        assertTrue("玉突き連鎖を含め何らかの手が採用されている", applied > 0)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals("c41不足が解消", 0, after.breakdown["c41"] ?: -1)
        assertEquals("HARDは悪化しない", 0, after.hard)
        assertEquals("Y/Wの被覆(covU)は悪化しない", 0, after.breakdown["covU"] ?: -1)
    }

    // [smoke] focus="c41"/"c41s" が新設の applyC41Free 経路へ正しくルーティングされ、例外なく
    //   同一次元の盤面を返すこと。実際の解消効果は上記の直接テストが検証する。
    @Test fun rsiGenerateHypothesisC41FocusReturnsValidSchedule() {
        val st = c41State(listOf(listOf(1), listOf(1)), l = "0", u = "1")
        val base = st.schedule.toIntArray2D()
        val rep = UnifiedViolationChecker.check(st, base)
        for (focus in listOf("c41", "c41s")) {
            val out = V6NativeOptimizer.rsiGenerateHypothesis(st, base, rep, focus, Random(1))
            assertNotNull(out)
            assertEquals(base.size, out.size)
            assertEquals(base[0].size, out[0].size)
        }
    }

    // [3.233.0/covO(3.204.0)・c41,c41s(3.209.0)と同型の穴] c42(群ペア禁止: 群g1のs1×群g2のs2が同日に
    // 同時発生禁止)も「動かせるか」を判定する専用オペレータが無くdestroyRepairViolationsの汎用ランダム
    // 再割当頼みだった。applyC42Freeが違反ペアの片側を実際に動かして解消することを固定する。
    private fun c42State(schedule: List<List<Int>>, wishes: Map<String, Int> = emptyMap()): MagiState = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-01",
        shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", "")),
        groups = listOf(Group("G0", "G0"), Group("G1", "G1")),
        staff = listOf(Staff("A", 0), Staff("B", 1)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 0), listOf(1, 0, 1)),
        groupShiftApt = listOf(listOf("", "", ""), listOf("", "", "")),
        schedule = schedule, wishes = wishes, staffRange = emptyMap(),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
        cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
        cons41 = emptyList(),
        cons42 = listOf(com.magi.app.model.C42Row(g1Kigou = "G0", g2Kigou = "G1", s1Kigou = "X", s2Kigou = "Y")),
    )

    @Test fun applyC42FreeResolvesFreelyMovablePair() {
        // A(G0)=X, B(G1)=Y は禁止ペア。Xに需要なし＝Aを休へ直接動かすだけで解消できるはず。
        val st = c42State(listOf(listOf(1), listOf(2)))
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c42"] ?: 0)
        val applied = V6NativeOptimizer.applyC42Free(st, sched, Random(1), skill = false)
        assertEquals(1, applied)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals(0, after.breakdown["c42"] ?: 0)
        assertEquals(0, after.hard)
    }

    @Test fun applyC42FreeLeavesWishPinnedPairUntouched() {
        // 両者とも現在のシフトを希望固定（希望どおり配置済み）だと、動かすと希望未充足に化けるため何もしない。
        val st = c42State(listOf(listOf(1), listOf(2)), wishes = mapOf("0,0" to 1, "1,0" to 2))
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c42"] ?: 0)
        val applied = V6NativeOptimizer.applyC42Free(st, sched, Random(1), skill = false)
        assertEquals(0, applied)
        assertEquals(1, sched[0][0])
        assertEquals(2, sched[1][0])
    }

    @Test fun applyC42FreeIsNoOpWhenRulesEmpty() {
        val st = c42State(listOf(listOf(1), listOf(2))).copy(cons42 = emptyList())
        val sched = st.schedule.toIntArray2D()
        assertEquals(0, V6NativeOptimizer.applyC42Free(st, sched, Random(1), skill = false))
    }

    // [監査(他の制約は大丈夫か)/玉突き連鎖の横展開] 旧実装（今回新設した直接移動のみ）だと、離脱元シフトが
    //   全体needをちょうど単独充足しており誰か1人が離脱すると即covU化する構造的にブロックされた局面を
    //   解けない。findCovUChainで別職員に玉突き充填すれば解消できることを固定する。
    @Test fun applyC42FreeResolvesViaChainWhenDirectMoveWouldCreateCovU() {
        // shift: 0=休(need無) 1=X(c42対象、need1=1でAがちょうど単独充足) 2=Y(need無、Bの現在地)
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "1", ""), Shift("Y", "Y", "", ""))
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"), Group("G2", "G2"))
        val groupShift = listOf(
            listOf(1, 1, 0), // G0(A)=休,X
            listOf(1, 0, 1), // G1(B)=休,Y
            listOf(1, 1, 0), // G2(C)=休,X（Aの離脱後にXを埋め直せる玉突き候補）
        )
        val staff = listOf(Staff("A", 0), Staff("B", 1), Staff("C", 2))
        val schedule = listOf(listOf(1), listOf(2), listOf(0)) // A=X, B=Y（禁止ペア）, C=休
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-01",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = List(3) { List(3) { "" } },
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(),
            cons42 = listOf(com.magi.app.model.C42Row(g1Kigou = "G0", g2Kigou = "G1", s1Kigou = "X", s2Kigou = "Y")),
        )
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals(1, before.breakdown["c42"] ?: 0)
        assertEquals(0, before.hard)   // Xはneed1=1をAがちょうど充足＝離脱すると即covU化する構造的にブロックされた局面

        val applied = V6NativeOptimizer.applyC42Free(st, sched, Random(1), skill = false)
        assertTrue("玉突き連鎖を含め何らかの手が採用されている", applied > 0)
        val after = UnifiedViolationChecker.check(st, sched)
        assertEquals("c42が解消", 0, after.breakdown["c42"] ?: -1)
        assertEquals("HARDは悪化しない", 0, after.hard)
        assertEquals("Xの被覆(covU)は悪化しない", 0, after.breakdown["covU"] ?: -1)
    }

    // [smoke] focus="c42"/"c42s" が新設の applyC42Free 経路へ正しくルーティングされ、例外なく
    //   同一次元の盤面を返すこと。実際の解消効果は上記の直接テストが検証する。
    @Test fun rsiGenerateHypothesisC42FocusReturnsValidSchedule() {
        val st = c42State(listOf(listOf(1), listOf(2)))
        val base = st.schedule.toIntArray2D()
        val rep = UnifiedViolationChecker.check(st, base)
        for (focus in listOf("c42", "c42s")) {
            val out = V6NativeOptimizer.rsiGenerateHypothesis(st, base, rep, focus, Random(1))
            assertNotNull(out)
            assertEquals(base.size, out.size)
            assertEquals(base[0].size, out[0].size)
        }
    }

    // [余剰ワーカー活用] perHypothesisWorkers: 仕様§2.2の5仮説上限を超えて設定したworkersを、
    // 各仮説の内部並列度（RSI/RSI++のSAチェーン数・ALNSの多チェーン）へ均等配分する計算のみを固定する
    // 純粋関数テスト（並列実行そのものはJVMユニットテストでは検証しない＝実機ログ/CIビルドで確認）。
    @Test fun perHypothesisWorkersDistributesSurplusEvenly() {
        assertEquals(1, V6NativeOptimizer.perHypothesisWorkers(workers = 1, hypotheses = 5))
        assertEquals(1, V6NativeOptimizer.perHypothesisWorkers(workers = 5, hypotheses = 5))
        // 実機ログ実例: workers設定8・仮説5 → 8/5=1(切り捨て)。旧実装は常に1だったのでこの値は不変。
        assertEquals(1, V6NativeOptimizer.perHypothesisWorkers(workers = 8, hypotheses = 5))
        // workers=16・仮説5 → 16/5=3。旧実装なら5を超えた11ワーカー分が完全に無駄だった。
        assertEquals(3, V6NativeOptimizer.perHypothesisWorkers(workers = 16, hypotheses = 5))
    }

    @Test fun perHypothesisWorkersNeverReturnsLessThanOne() {
        assertEquals(1, V6NativeOptimizer.perHypothesisWorkers(workers = 0, hypotheses = 5))
        // hypotheses=0 は縮退入力（実際は coerceIn(1,5) 済みで到達しない）。内側ガード max(1,hypotheses)=1 に
        //   より 3/1=3 が返る＝「1未満を返さない」性質は満たす（初版の期待値1は誤り→CI失敗で検出・修正）。
        assertEquals(3, V6NativeOptimizer.perHypothesisWorkers(workers = 3, hypotheses = 0))
    }

    // [敵対的レビュー3.212.0] hypothesisChainPlan: 余り配分（6〜9帯で余剰が実際に使われる=旧perW床のみの
    //   「無駄にならない」虚偽表示の是正）＋コア数クランプ（壁時計締切下の希釈=品質逆行リスクの回避）。
    @Test fun chainPlanDistributesRemainderToLeadingHypotheses() {
        // 動機となった実機ログ当該ケース: workers=8/8コア → [2,2,2,1,1]（旧perW床は全仮説1で余剰3本廃棄だった）
        val plan8 = V6NativeOptimizer.hypothesisChainPlan(workers = 8, hypotheses = 5, cores = 8)
        assertEquals(listOf(2, 2, 2, 1, 1), plan8.toList())
        assertEquals(8, plan8.sum())
        // 割り切れるケース: workers=5 → 全仮説1（旧来どおり）
        assertEquals(listOf(1, 1, 1, 1, 1), V6NativeOptimizer.hypothesisChainPlan(5, 5, 8).toList())
    }

    @Test fun chainPlanClampsToCoreCount() {
        // workers=16/8コア → 配分総量はコア数8まで（15コルーチン/8スレッドの希釈を作らない）
        val plan = V6NativeOptimizer.hypothesisChainPlan(workers = 16, hypotheses = 5, cores = 8)
        assertEquals(8, plan.sum())
        assertEquals(listOf(2, 2, 2, 1, 1), plan.toList())
        // 16コアなら16本フル配分
        assertEquals(16, V6NativeOptimizer.hypothesisChainPlan(16, 5, 16).sum())
    }

    @Test fun chainPlanEveryHypothesisGetsAtLeastOneChain() {
        // workers<hypotheses でも各仮説に最低1本（合計は hypotheses を下回らない）
        val plan = V6NativeOptimizer.hypothesisChainPlan(workers = 2, hypotheses = 5, cores = 8)
        assertEquals(5, plan.size)
        assertTrue(plan.all { it >= 1 })
        // 縮退入力も安全
        assertTrue(V6NativeOptimizer.hypothesisChainPlan(0, 0, 0).all { it >= 1 })
    }

    // [敵対的レビュー] cores<hypotheses（低コア端末で5仮説固定）のときは、仕様上の「最低1仮説1本」の
    //   floorがコア数クランプより優先される（各仮説は最低1本必要＝5仮説なら5本を下回れない）。これは
    //   hypothesisChainPlan の欠陥ではなく、hypotheses数自体を減らさない設計（w=MAX_HYPOTHESESは
    //   仕様上不変）に由来する構造的な下限であることを固定する（余剰チェーンの"追加"はしないことを
    //   合わせて確認=このケースでは各仮説ちょうど1本のみで、コア数超のオーバーサブスクライブを
    //   それ以上増やさない）。
    @Test fun chainPlanFloorsAtHypothesesCountEvenBelowCoreCount() {
        val plan = V6NativeOptimizer.hypothesisChainPlan(workers = 16, hypotheses = 5, cores = 2)
        assertEquals(5, plan.sum())
        assertEquals(listOf(1, 1, 1, 1, 1), plan.toList())
    }

    // [敵対的レビュー修正・#6] V5(高速計算)はhypothesisChainPlanを使わずoptions.workersをそのまま
    //   SAチェーン数へ渡していたため、コア数クランプの恩恵を受けなかった。専用の総並列度クランプを検証。
    @Test fun clampWorkersToCoresLimitsToAvailableCores() {
        assertEquals(8, V6NativeOptimizer.clampWorkersToCores(workers = 16, cores = 8))
        assertEquals(16, V6NativeOptimizer.clampWorkersToCores(workers = 16, cores = 16))
        assertEquals(4, V6NativeOptimizer.clampWorkersToCores(workers = 4, cores = 8))
    }

    @Test fun clampWorkersToCoresNeverReturnsLessThanOne() {
        assertEquals(1, V6NativeOptimizer.clampWorkersToCores(workers = 0, cores = 8))
        assertEquals(1, V6NativeOptimizer.clampWorkersToCores(workers = 4, cores = 0))
    }

    // [仮説数上限撤廃・ユーザー指示「仮説数は最低2最大設定値」] 旧 optimize() は
    // options.workers.coerceIn(1, MAX_HYPOTHESES=5) で固定上限だった。hypothesisCount はこの上限を
    // 撤廃し、workers>=2 ならそのまま workers を仮説数として使う（多様性優先）。
    @Test fun hypothesisCountScalesWithWorkersBeyondOldFixedCapOfFive() {
        assertEquals(8, V6NativeOptimizer.hypothesisCount(8))
        assertEquals(16, V6NativeOptimizer.hypothesisCount(16))
    }

    @Test fun hypothesisCountFloorsAtTwoEvenWhenWorkersIsOne() {
        // workers=1 でも最低2仮説の多様探索を保証する（意図的なオーバーサブスクライブ）。
        assertEquals(2, V6NativeOptimizer.hypothesisCount(1))
        assertEquals(2, V6NativeOptimizer.hypothesisCount(0))
    }

    @Test fun hypothesisCountMatchesWorkersInTheOldCapRange() {
        // 旧上限5以下の帯でも従来どおり workers に一致すること（回帰確認）。
        assertEquals(2, V6NativeOptimizer.hypothesisCount(2))
        assertEquals(5, V6NativeOptimizer.hypothesisCount(5))
    }

    // [3.371.0/並列SA本格再有効化] hypothesisSpawnPlan: workers<=cores（大半の端末）では
    // hypothesisChainPlan の旧来どおりの結果と完全一致すること（無変更を固定）。
    @Test fun spawnPlanMatchesLegacyBehaviorWhenWorkersFitsWithinCores() {
        val (hSpawn8, plan8) = V6NativeOptimizer.hypothesisSpawnPlan(workers = 8, w = 8, cores = 8)
        assertEquals(8, hSpawn8)
        assertEquals(V6NativeOptimizer.hypothesisChainPlan(8, 8, cores = 8).toList(), plan8.toList())
        assertTrue("workers<=coresでは常に1本ずつ(旧来と同一)", plan8.all { it == 1 })

        val (hSpawn4, plan4) = V6NativeOptimizer.hypothesisSpawnPlan(workers = 4, w = 4, cores = 8)
        assertEquals(4, hSpawn4)
        assertEquals(V6NativeOptimizer.hypothesisChainPlan(4, 4, cores = 8).toList(), plan4.toList())
        assertTrue(plan4.all { it == 1 })
    }

    // workers>cores（端末のコア数を超える設定）のときだけ、spawn数をコア数まで絞り、
    // その分を仮説内チェーン数へ配分する（並列SAの本格再有効化）。
    @Test fun spawnPlanRedistributesSurplusAsChainDepthWhenWorkersExceedsCores() {
        val (hSpawn, plan) = V6NativeOptimizer.hypothesisSpawnPlan(workers = 16, w = 16, cores = 4)
        assertEquals(4, hSpawn)                 // 希釈を避けコア数まで縮小（1 hypothesis/core）
        assertEquals(4, plan.size)
        assertEquals(16, plan.sum())            // workers予算の合計は不変（コルーチンをコア数超に増やさない）
        assertTrue("各仮説が複数チェーンを持つはず(本格再有効化)", plan.all { it > 1 })
        assertEquals(listOf(4, 4, 4, 4), plan.toList())
    }

    // workers=2（オーバーサブスクライブ時のhypothesisCount floor）は cores に関わらず
    // 多様性の下限2を割らない＝深さへの転用を行わない（コーナーケースの安全確認）。
    @Test fun spawnPlanNeverDropsBelowTheDiversityFloorOfTwo() {
        val (hSpawn, plan) = V6NativeOptimizer.hypothesisSpawnPlan(workers = 1, w = 2, cores = 1)
        assertEquals(2, hSpawn)
        assertEquals(2, plan.size)
        assertTrue(plan.all { it >= 1 })
    }

    // 縮退入力でも例外にならず安全であること。
    @Test fun spawnPlanIsSafeForDegenerateInputs() {
        val (hSpawn, plan) = V6NativeOptimizer.hypothesisSpawnPlan(workers = 0, w = 0, cores = 0)
        assertTrue(hSpawn >= 2)
        assertTrue(plan.isNotEmpty())
        assertTrue(plan.all { it >= 1 })
    }

    // [3.231.0/ドッグフーディングで発見・修正] rsiHf63EffortIters: 実機ログ(rounds=5)で
    // covU/apt/c1がE9冷却で交互切替を続け全ラウンドtotal不変だった事例。旧1800固定は5000到達に
    // 3回のfocusを要し、round1,3,5(=最終)でようやく成立し振り向け先が残らなかった。
    @Test fun rsiHf63EffortItersReachesThresholdInTwoAttemptsForTypicalRoundBudget() {
        val e5 = V6NativeOptimizer.rsiHf63EffortIters(5)
        assertTrue("2回のfocusで5000へ到達すること(round1,3で成立しround4,5を振り向けに残せる)", e5 * 2 >= 5000)
        assertTrue("1回の不運なfocusだけでは5000へ到達しない(E9の1R冷却との役割分担を保つ)こと", e5 * 1 < 5000)
    }

    @Test fun rsiHf63EffortItersNeverDropsBelowTwoAttempts() {
        // 極小のrounds(2)でも下限2回のfocusは必ず要する。
        val e2 = V6NativeOptimizer.rsiHf63EffortIters(2)
        assertTrue(e2 * 1 < 5000)
        assertTrue(e2 * 2 >= 5000)
    }

    @Test fun rsiHf63EffortItersRelaxesForLargerRoundBudgets() {
        // roundsが大きいほど許容attempts回数が増え、effortItersは小さくなる(じっくり粘れる)。
        val e5 = V6NativeOptimizer.rsiHf63EffortIters(5)
        val e8 = V6NativeOptimizer.rsiHf63EffortIters(8)
        assertTrue(e8 <= e5)
    }

    // [敵対的レビュー修正・#3] liveBest の CAS 管理: publishLiveBest は真に better() な報告のときだけ
    // liveBest を更新し、劣る/同値の報告では既存の liveBest を保持する（旧last-writer-winsの退行を防ぐ）。
    // グローバルなシングルトン状態(liveBest/liveBestReport)を扱うため、他テストの残存値に依存しないよう
    // 「明確に最良(hard=0,total=0)」→「明確に劣る(hard=0,total=極大)」の順で呼び、劣る側が無視されることを
    // 直接確認する（逆順=劣る値を先に публиしても後続の最良値が正しく採用されることも合わせて確認）。
    private fun tinyState(): MagiState {
        val shifts = listOf(Shift("休", "休", "", ""))
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("A", 0))
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-01",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1)), groupShiftApt = listOf(listOf("")),
            schedule = listOf(listOf(0)), wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test fun publishLiveBestIgnoresWorseReportAfterBestPublished() {
        val st = tinyState()
        val sched = st.schedule.toIntArray2D()
        val goodReport = UnifiedViolationChecker.check(st, sched)  // hard=0/total=0（休のみ・制約なし）
        val goodSched = arrayOf(intArrayOf(0))
        V6NativeOptimizer.publishLiveBest(goodReport, goodSched)
        assertEquals(listOf(listOf(0)), V6NativeOptimizer.liveBest)

        // 明確に劣る report をでっち上げて publish を試みる（同一 hard・total だけ悪化させた copy）。
        val worseReport = goodReport.copy(total = goodReport.total + 999_999)
        val worseSched = arrayOf(intArrayOf(999))
        V6NativeOptimizer.publishLiveBest(worseReport, worseSched)
        // 劣る report は無視され、liveBest は直前の良好な盤面のまま。
        assertEquals(listOf(listOf(0)), V6NativeOptimizer.liveBest)

        // 更に良い report(total を減らす)なら正しく更新される。
        val betterReport = goodReport.copy(total = -1)
        val betterSched = arrayOf(intArrayOf(7))
        V6NativeOptimizer.publishLiveBest(betterReport, betterSched)
        assertEquals(listOf(listOf(7)), V6NativeOptimizer.liveBest)
    }

    // [3.240.0/5ラウンド完全停滞の修正] destroyRepairStaffReps は destroyRepairDay(1回で最大S人分の
    // セル変化・repeat(6))と総攪乱セル数(6*S)を揃えるための反復回数。実機データ(S=10,T=31)のような
    // S<T では旧固定repeat(8)より小さくなり摂動が弱まる。S>=T では従来以上(>=6)を維持し退化しない。
    @Test fun destroyRepairStaffRepsShrinksWhenDaysExceedStaff() {
        // S=10,T=31: (6*10+30)/31 = 90/31 = 2（切り捨て）。旧固定値8より大幅に小さい。
        assertEquals(2, V6NativeOptimizer.destroyRepairStaffReps(10, 31))
    }

    @Test fun destroyRepairStaffRepsStaysAtOrAboveSixWhenStaffExceedsDays() {
        // S=31,T=10: (6*31+9)/10 = 195/10 = 19。S=10,T=10: (60+9)/10=6。いずれも旧repeat(6)基準以上。
        assertEquals(19, V6NativeOptimizer.destroyRepairStaffReps(31, 10))
        assertEquals(6, V6NativeOptimizer.destroyRepairStaffReps(10, 10))
    }

    @Test fun destroyRepairStaffRepsNeverReturnsLessThanOne() {
        assertEquals(1, V6NativeOptimizer.destroyRepairStaffReps(0, 100))
        assertEquals(6, V6NativeOptimizer.destroyRepairStaffReps(1, 1))
    }

    // ---- [3.288.0/ログ強化=回数軸] focus 足跡の連続圧縮 ----

    @Test fun compressFocusTrailCollapsesConsecutiveRepeats() {
        assertEquals("covU×2→c3n", V6NativeOptimizer.compressFocusTrail(listOf("covU", "covU", "c3n")))
        assertEquals("c1", V6NativeOptimizer.compressFocusTrail(listOf("c1")))
        assertEquals("", V6NativeOptimizer.compressFocusTrail(emptyList()))
    }

    @Test fun compressFocusTrailKeepsMarkersInPlaceAndDoesNotMergeThem() {
        // マーカー([..])は圧縮対象にせず、発生位置のまま挟む（同じ族が前後に分かれても別区間として残る）。
        assertEquals(
            "covU×2→[HF63降格:covU]→c3n",
            V6NativeOptimizer.compressFocusTrail(listOf("covU", "covU", "[HF63降格:covU]", "c3n")),
        )
        assertEquals(
            "c1→[HF63降格:covU]→c1",
            V6NativeOptimizer.compressFocusTrail(listOf("c1", "[HF63降格:covU]", "c1")),
        )
    }

    // ---- [3.288.0/ログ強化=状態軸] HF63 学習族の実行横断 union ----

    @Test fun recordInfeasibleUnionsAcrossCallsAndClearResets() {
        V6NativeOptimizer.clearInfeasible()
        assertEquals(emptySet<String>(), V6NativeOptimizer.lastInfeasibleFamilies)
        V6NativeOptimizer.recordInfeasible(listOf("covU"))
        V6NativeOptimizer.recordInfeasible(listOf("c3n", "covU"))
        assertEquals(setOf("covU", "c3n"), V6NativeOptimizer.lastInfeasibleFamilies)
        V6NativeOptimizer.recordInfeasible(emptyList())   // 空は no-op
        assertEquals(setOf("covU", "c3n"), V6NativeOptimizer.lastInfeasibleFamilies)
        V6NativeOptimizer.clearInfeasible()
        assertEquals(emptySet<String>(), V6NativeOptimizer.lastInfeasibleFamilies)
    }

    // ---- [3.335.0/外部レビュー P1] 実行ごとの成果物は返り値で受け取る ----
    //
    // 旧実装は `lastAlternatives` 等の可変 static を呼び出し側が返却後に読んでいた。実行が重なると
    // （WorkManager の REPLACE で旧 Worker が協調キャンセルを待つ間など）入口の初期化が相手の値を消し、
    // 「他の案」「残存分析」「ライブ表示」が混ざり得た。採用盤面は元から返り値で流れるので誤った
    // 勤務表にはならないが、診断が別の実行のものになる。

    @Test fun runSlotKeepsEachRunsOwnOutputs() {
        val a = V6NativeOptimizer.RunSlot(1)
        val b = V6NativeOptimizer.RunSlot(2)
        a.addInfeasible(listOf("covU"))
        b.addInfeasible(listOf("c3n"))
        a.addInfeasible(listOf("pref", "covU"))
        assertEquals("別の実行の学習が混ざらない", setOf("covU", "pref"), a.infeasible)
        assertEquals(setOf("c3n"), b.infeasible)
        a.addInfeasible(emptyList())   // 空は no-op
        assertEquals(setOf("covU", "pref"), a.infeasible)
    }

    @Test fun optimizeResultCarriesItsOwnOutputs() = runBlocking {
        // 実行の返り値がその実行の成果物を持つ（static を経由しない）ことを、最小の盤面で固定する。
        val st = canDoState(emptyMap())
        val r = V6NativeOptimizer.optimize(
            st, st.schedule.toIntArray2D(),
            V6OptimizerOptions(V6Algorithm.V5, totalBudgetSec = 1, workers = 1),
        )
        // 返り値の器が存在し、static とは独立に読めること（V5 単発では中身は空でよい）。
        assertEquals(V6NativeOptimizer.lastInfeasibleFamilies, r.infeasibleFamilies)
        assertEquals(st.staffCount, r.schedule.size)
    }

    // ---- [3.319.0] destroy-repair の marginal cost を目的関数と揃える -------------------------
    //
    // `staffCountPenaltyAt` は候補選択の marginal cost で、`Evaluator.fullEvalParts` と同じ族
    // （low 90 / high 45 / apt 1）を見る。ところが low だけ `p.canDo` ガードが無く、担当外シフトに
    // 個人下限が設定されたデータで「実際には存在しない違反」を重み90 で数えていた。

    /** 2職員×2日・シフト {休, X, Y}。G0 は {休, X} のみ担当可＝Y は担当外。 */
    private fun canDoState(staffRange: Map<String, Range>) = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-02",
        shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", "")),
        groups = listOf(Group("G0", "G0")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 0)),
        groupShiftApt = listOf(listOf("", "", "")),
        schedule = listOf(listOf(0, 0), listOf(0, 0)),
        wishes = emptyMap(), staffRange = staffRange, needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test
    fun marginalCostIgnoresLowerBoundOnDisallowedShift() {
        // Y(k=2) は担当外。そこに下限2を置いても marginal cost は 0 でなければならない
        // （Evaluator も checker も canDo ガードで数えない＝目的関数に存在しない違反）。
        val p = Problem(canDoState(mapOf("0,2" to Range("2", ""))))
        assertFalse("前提: s0 は Y を担当できない", p.canDo(0, 2))
        assertEquals("担当外の下限は marginal cost に入らない",
            0L, V6NativeOptimizer.staffCountPenaltyAt(p, 0, 2, 0))
    }

    @Test
    fun marginalCostStillCountsLowerBoundOnAllowedShift() {
        // X(k=1) は担当可。下限2に対し現在0なら 2×90=180。回帰の固定。
        val p = Problem(canDoState(mapOf("0,1" to Range("2", ""))))
        assertTrue("前提: s0 は X を担当できる", p.canDo(0, 1))
        assertEquals("担当可の下限は従来どおり重み90で数える",
            180L, V6NativeOptimizer.staffCountPenaltyAt(p, 0, 1, 0))
    }

    @Test
    fun marginalCostUpperBoundIsUnchanged() {
        // high は担当可否を問わず Evaluator と同じ扱い（両方ガード無しで一致）。X 上限1に対し3回で 2×45=90。
        val p = Problem(canDoState(mapOf("0,1" to Range("", "1"))))
        assertEquals("上限側は不変", 90L, V6NativeOptimizer.staffCountPenaltyAt(p, 0, 1, 3))
    }

    // ---- [3.346.1/方針B] 停滞シグナルの確認窓 ------------------------------------------------
    // `shouldStop` は単調でない（締切・キャンセルは永久に真だが、停滞シグナルは他のワーカーが
    // 改善を1件出せば偽に戻る）。旧実装は epoch ループの while 条件でこれを見ていたため、
    // 一瞬 true の瞬間にポーリングしたワーカーだけが恒久離脱していた（実機で8本中4本）。

    @Test
    fun transientStopSignalIsNotConfirmed() = runBlocking {
        // 1回目の問い合わせだけ真＝一瞬のシグナル。確認窓で偽へ戻るので「本物でない」と判定する。
        var calls = 0
        val transient = { calls++ == 0 }
        assertTrue("前提: 初回は真", transient())
        calls = 0
        assertFalse("一瞬のシグナルは確認されない（走行を続ける）",
            V6NativeOptimizer.confirmStop(transient, deadline = Long.MAX_VALUE, stopIsFinal = { false }))
    }

    @Test
    fun persistentStopSignalIsConfirmed() = runBlocking {
        // 常に真＝本物の停滞。確認窓ぶん待ってから真を返す（従来どおり離脱する）。
        val t0 = System.currentTimeMillis()
        assertTrue("続くシグナルは本物と判定する",
            V6NativeOptimizer.confirmStop({ true }, deadline = Long.MAX_VALUE, stopIsFinal = { false }))
        val waited = System.currentTimeMillis() - t0
        assertTrue("確認窓のあいだ待ってから確定する（実測 ${waited}ms）",
            waited >= V6NativeOptimizer.STOP_CONFIRM_MS / 2)
    }

    @Test
    fun deadlinePassedIsConfirmedImmediately() = runBlocking {
        // 締切超過は単調＝待たずに確定する（確認窓で早期終了の効きを削らない）。
        // deadline は nowMs()（nanoTime 系の単調時計）と同じ物差しなので、
        // 「必ず過ぎている値」として 0 を渡す（壁時計を混ぜると別の物差しになる）。
        val t0 = System.currentTimeMillis()
        assertTrue("締切超過は即確定",
            V6NativeOptimizer.confirmStop({ true }, deadline = 0L, stopIsFinal = { false }))
        assertTrue("待たない", System.currentTimeMillis() - t0 < 1_000L)
    }

    @Test
    fun finalStopIsConfirmedWithoutWaiting() = runBlocking {
        // 探索締切・キャンセルは単調＝確認窓を回さずに即確定する。旧実装（stopIsFinal 無し）は
        // ここでも5秒待ち、探索が締切を超えて後処理予約を食っていた（実測 探索109.99s→114.998s）。
        val t0 = System.currentTimeMillis()
        assertTrue("単調な停止は即確定",
            V6NativeOptimizer.confirmStop({ true }, deadline = Long.MAX_VALUE, stopIsFinal = { true }))
        assertTrue("待たない", System.currentTimeMillis() - t0 < 1_000L)
    }
}
