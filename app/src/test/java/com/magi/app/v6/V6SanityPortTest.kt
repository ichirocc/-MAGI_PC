package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class V6SanityPortTest {
    @Test fun detectsImpossibleWishAndInvalidAssignment() {
        val st = MagiState(
            startDate = "2026-06-01",
            endDate = "2026-06-02",
            shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", "1", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 0)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(listOf(1, 0)),
            wishes = mapOf("0,0" to 1),
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val rep = V6SanityPort.build(st)
        assertEquals(1, rep.impossibleWishes.size)
        assertTrue(rep.warns.any { it.contains("実現不能") })
        assertTrue(rep.warns.any { it.contains("担当不可") })
    }

    /** ベース: 2職員×6日、A は 1日1スロット。cons1 A(窓3日で2回以上) を切替えて壁/ダイヤルを検証。 */
    private fun windowState(need1A: String, cons1: List<com.magi.app.model.C1Row>) = MagiState(
        startDate = "2026-06-01", endDate = "2026-06-06",
        shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", need1A, "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = List(2) { listOf(1, 1, 0, 1, 1, 0) },
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = cons1, cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun classifiesStructuralWindowWallButNotDial() {
        val cons = listOf(com.magi.app.model.C1Row("3", "A", "2"))   // A を3日窓で2回以上
        // 壁: A 供給=6(1/日×6) < 需要下界=2人×2回×floor(6/3=2)=8 → 構造的不能。
        val wall = V6SanityPort.buildGuidance(windowState("1", cons))
        assertTrue("A の窓が壁として案内される",
            wall.any { it.where.contains("A") && it.problem.contains("構造的に残ります") })
        // ダイヤル: A 供給=12(2/日×6) ≥ 需要8 → 案内しない。
        val dial = V6SanityPort.buildGuidance(windowState("2", cons))
        assertTrue("供給充足なら窓の壁案内は出さない",
            dial.none { it.where.contains("窓ルール") && it.problem.contains("構造的に残ります") })
    }

    /** [3.228.0/個人内壁検知] 1職員×31日、cons1(day1日窓でXが≥day2回)、Xの個人上限をhiで指定。 */
    private fun personalWallState(day1: String, day2: String, hi: String) = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-31",
        shifts = listOf(Shift("休", "休", "0", ""), Shift("X", "X", "0", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(List(31) { 0 }),
        wishes = emptyMap(),
        staffRange = mapOf("0,1" to com.magi.app.model.Range("0", hi)),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = listOf(com.magi.app.model.C1Row(day1, "X", day2)),
        cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    // [3.228.0/ドッグフーディングで発見・修正] 壁/ダイヤル分類器(2b-2)は全体供給(集計)しか見ないため、
    // 「集計では担当者が大勢いても、この1人だけは自分の個人上限のせいで自分の窓ルールを満たせない」
    // 局面（例: 桒澤美幸のAｱ上限2×「14日窓Aｱ≥1」）を検知できていなかった。実データ相当(T=31,14日窓,
    // 下界=1×floor(31/14)=2)で、個人上限1(<下界2)は構造的不能として案内されることを固定する。
    @Test fun personalC1WallDetectsWhenRangeHiBelowConservativeMinimum() {
        val impossible = V6SanityPort.buildGuidance(personalWallState("14", "1", "1"))
        assertTrue("個人上限1<下界2は個人内で構造的不能として案内されること",
            impossible.any { it.where.contains("s0") && it.where.contains("個人上限と窓ルールの衝突") })
    }

    // [重要=当初の仮説を訂正] 美幸の実際の設定(個人上限2, 下界も2)は「上限==下界」で理論上ぎりぎり
    // 満たせる（false wallと誤検知してはいけない）。この保守的下界チェックでは壁と判定されないことを
    // 固定し、彼女の実際の停滞原因が「データの構造的矛盾」でなく「探索が最適配置を見つけていないこと」
    // であるという訂正済みの理解を裏付ける。
    @Test fun personalC1WallDoesNotFalselyFlagBorderlineSatisfiableCase() {
        val borderline = V6SanityPort.buildGuidance(personalWallState("14", "1", "2"))
        assertTrue("個人上限2==下界2は壁として誤検知しないこと",
            borderline.none { it.where.contains("個人上限と窓ルールの衝突") })
    }

    @Test fun personalC1WallIgnoresStaffWithoutPersonalCap() {
        // 個人上限が未設定(無制限)なら誰でも壁にはならない。
        val uncapped = V6SanityPort.buildGuidance(personalWallState("14", "1", ""))
        assertTrue(uncapped.none { it.where.contains("個人上限と窓ルールの衝突") })
    }

    // [3.262.0, 「初期解生成でC1違反をゼロにする」調査で判明] 旧2b-3は同一シフトの複数窓ルールを
    // 各ルール独立(非重複窓の粗い下界)で判定していたが、`SmartInitialScheduler.minDaysForFullCompliance`
    // で総当たり検証したところ、同一シフトに複数ルールがある場合の**真の同時充足に必要な最小日数**は
    // 各ルール個別の下界の最大値を上回りうることを確認（例: T=26,「9日窓5回以上」＋「14日窓7回以上」＝
    // 個別下界は5,10で旧判定なら上限10で「足りている」と誤判定するが、実際の同時充足には14日必要）。
    private fun personalWallStateTwoRules(
        day1a: String, day2a: String, day1b: String, day2b: String, hi: String, t: Int,
    ) = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-${t.toString().padStart(2, '0')}",
        shifts = listOf(Shift("休", "休", "0", ""), Shift("X", "X", "0", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(List(t) { 0 }),
        wishes = emptyMap(),
        staffRange = mapOf("0,1" to Range("0", hi)),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = listOf(
            com.magi.app.model.C1Row(day1a, "X", day2a),
            com.magi.app.model.C1Row(day1b, "X", day2b),
        ),
        cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun personalC1WallDetectsTrueJointMinimumExceedingEachRulesOwnBound() {
        // T=26,「9日窓5回以上」(旧下界=5×floor(26/9)=5)＋「14日窓7回以上」(旧下界=7×floor(26/14)=7)。
        // 旧判定(各ルール独立)なら上限10は両方の下界(5,7)以上のため「足りている」と誤判定するが、
        // 実際に0違反へ同時到達するには14日必要（ホストJVM実行で確認済み）で、上限10は真に不足。
        val flagged = V6SanityPort.buildGuidance(personalWallStateTwoRules("9", "5", "14", "7", "10", 26))
        assertTrue("同一シフト複数ルールの真の同時最小(14)>上限(10)は壁として案内されること",
            flagged.any { it.where.contains("s0") && it.where.contains("個人上限と窓ルールの衝突") })
    }

    @Test fun personalC1WallDoesNotFlagWhenCapMeetsTrueJointMinimum() {
        // 同じ規則で上限=14(真の同時最小と一致)なら壁として誤検知しないこと。
        val ok = V6SanityPort.buildGuidance(personalWallStateTwoRules("9", "5", "14", "7", "14", 26))
        assertTrue("上限が真の同時最小と一致すれば壁として誤検知しないこと",
            ok.none { it.where.contains("個人上限と窓ルールの衝突") })
    }

    // [3.227.0/c1内訳] 「違反詳細 c1(N件)」はDETAIL_CAP=8で打ち切られ職員別の内訳が読めないため、
    // 職員×窓ルール別の全件集計を別行で出すようにした。s0のみ「休(5日窓≥2)」ルールに1件違反する
    // 最小盤面で、正確な件数がその1行に出ることを固定する。
    @Test fun violationDebugReportsC1CountsPerStaffAndRule() {
        val st = MagiState(
            startDate = "2026-06-01", endDate = "2026-06-07",
            shifts = listOf(Shift("休", "休", "0", ""), Shift("A", "A", "0", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            // s0: 最初の5日が全て A（休0回、5日窓で休>=2に違反）／s1: 休とAの交互（常に窓内2回以上で違反なし）
            schedule = listOf(
                listOf(1, 1, 1, 1, 1, 0, 0),
                listOf(0, 1, 0, 1, 0, 1, 0),
            ),
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(com.magi.app.model.C1Row("5", "休", "2")),
            cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val report = UnifiedViolationChecker.check(st, sched)
        assertTrue("前提: c1違反が発生していること", (report.breakdown["c1"] ?: 0) > 0)
        val lines = V6SanityPort.buildViolationDebug(st, sched, report)
        val summary = lines.firstOrNull { it.contains("c1内訳") }
        assertTrue("c1内訳サマリ行が出力されること", summary != null)
        // [3.282.0] 計数を「違反ラン先頭のみ」→ checker の inc と同じ「違反窓ごと」へ是正。
        //   この盤面は窓j=0(休0回)とj=1(休1回)の2窓が違反＝breakdown c1=2 と厳密に一致する
        //   （旧実装は連続窓を1ランとして「1件」と表示し breakdown と食い違う第3の計数だった）。
        assertEquals("前提: 違反窓は2窓", 2, report.breakdown["c1"])
        assertTrue("s0が休(5日窓≥2)で2件（=breakdownと同じ窓計上）と表示されること", summary!!.contains("s0 休(5日窓≥2)2件"))
        assertTrue("s1は違反なしのため内訳に出ないこと", !summary.contains("s1 "))
    }

    /** [3.282.0/新領域ログ監査] 違反詳細ヘッダは「最重クラスで解決済みのセル位置数」で breakdown の
     *  fire 数と意味が異なり（c3n=1 fireでもパターン全セルをmark等）、実機ログで「c1(11件)」vs
     *  「UnifiedCheck c1=12」の食い違いとして混乱を生んでいた。fires(breakdown)と位置数が異なるときは
     *  「件数F・場所N箇所」と両方を明示することを固定する。 */
    @Test fun violationDebugShowsFiresAndLocationsWhenTheyDiffer() {
        val st = MagiState(
            startDate = "2026-06-01", endDate = "2026-06-03",
            shifts = listOf(Shift("休", "休", "0", ""), Shift("A", "A", "0", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(listOf(1, 1, 0)),   // A A 休 → 禁止連続 [A,A] に1 fire・mark は2セル
            wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = listOf(com.magi.app.model.C3Row(listOf("A", "A"))),
            cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val report = UnifiedViolationChecker.check(st, sched)
        assertEquals("前提: c3n は1 fire", 1, report.breakdown["c3n"])
        val lines = V6SanityPort.buildViolationDebug(st, sched, report)
        val detail = lines.firstOrNull { it.contains("違反詳細 c3n") }
        assertTrue("c3n の違反詳細行が出力されること", detail != null)
        assertTrue("fire数と位置数が異なるときは両方を明示すること: $detail",
            detail!!.contains("c3n(件数1・場所2箇所)"))
    }

    /** [3.234.0→3.236.0/休の適切回数合計チェック誤検知修正→実質的上限へ差替え] 休は「1日に何人休んで
     *  よいか」という座席上限を持たないため need1(=seatsHi)との比較は無意味だが、「本当に過大」な設定は
     *  引き続き検出したい。休の実質的上限＝Σ_i(T − 他シフトの個人下限)と比較する新ロジックを検証する。
     *  T=days・staff数=2・他シフトへの個人下限はotherLoで指定（未指定なら無し）。 */
    private fun aptVsNeedState(days: Int, need1: String, aptTarget: String, otherLo: String = "") = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-${days.toString().padStart(2, '0')}",
        shifts = listOf(Shift("休", "休", need1, ""), Shift("X", "X", need1, "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf(aptTarget, aptTarget)),
        schedule = List(2) { List(days) { 0 } },
        wishes = emptyMap(),
        staffRange = if (otherLo.isBlank()) emptyMap() else mapOf("0,1" to com.magi.app.model.Range(otherLo, ""), "1,1" to com.magi.app.model.Range(otherLo, "")),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    /**
     * [3.316.0/休の下限合計チェック誤検知修正] 検査A（下限の合計 > それを受け止められる上限）は休も
     * 必要人数の合計と比べており、休に need1=0 が明示設定された実データでは「下限合計80 vs 必要数0」で
     * **必ず誤警告**が出ていた。休には「1日に何人休んでよいか」の座席が無いので、3.235.0 で適切回数へ
     * 導入したのと同じ実質上限（Σ_i(T − 他シフトの個人下限)）と比べる。
     * 2名・休(k=0)/X(k=1) の2シフト。restLo/otherLo でそれぞれの個人下限を与える。
     */
    private fun restLoState(days: Int, restLo: String, otherLo: String = "") = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-${days.toString().padStart(2, '0')}",
        shifts = listOf(Shift("休", "休", "0", ""), Shift("X", "X", "0", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = List(2) { List(days) { 0 } },
        wishes = emptyMap(),
        staffRange = buildMap {
            put("0,0", com.magi.app.model.Range(restLo, ""))
            put("1,0", com.magi.app.model.Range(restLo, ""))
            if (otherLo.isNotBlank()) {
                put("0,1", com.magi.app.model.Range(otherLo, ""))
                put("1,1", com.magi.app.model.Range(otherLo, ""))
            }
        },
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    private fun hasLowerBoundIssue(rep: List<SettingIssue>, sym: String) =
        rep.any { it.where.contains(sym) && it.where.contains("回数下限の合計") }

    @Test fun restLowerBoundCheckUsesRestCapacityInsteadOfNeed() {
        // T=10・休下限3(合計6)・X下限3。休の実質上限=2人×(10−3)=14 ≥ 6 → 誤検知しない。
        // 同じ設定でも非休シフト X は必要数0に対し下限合計6＝従来どおり真の矛盾として検出する。
        val rep = V6SanityPort.buildGuidance(restLoState(days = 10, restLo = "3", otherLo = "3"))
        assertFalse("控えめな休の下限は誤検知しない(need=0との比較をやめた効果)", hasLowerBoundIssue(rep, "休"))
        assertTrue("同一設定の非休シフト(X)は従来どおり検出する", hasLowerBoundIssue(rep, "X"))
    }

    @Test fun restLowerBoundCheckStillFlagsGenuinelyImpossibleLowerBound() {
        // T=2・休下限5(合計10)。休の実質上限=2人×2日=4 < 10 → 物理的に不可能なので検出する。
        val rep = V6SanityPort.buildGuidance(restLoState(days = 2, restLo = "5"))
        assertTrue("期間日数を超える休の下限は検出すること", hasLowerBoundIssue(rep, "休"))
    }

    @Test fun restLowerBoundCheckAccountsForOtherShiftLowerBounds() {
        // T=10・休下限8(合計16)。X下限5 があると休の実質上限=2人×(10−5)=10 < 16 → 検出。
        // X下限が無ければ実質上限=2人×10=20 ≥ 16 → 検出しない（同じ休の下限でも他シフト次第で変わる）。
        assertTrue("他シフト下限を差し引いた実質上限を下回れば検出",
            hasLowerBoundIssue(V6SanityPort.buildGuidance(restLoState(10, restLo = "8", otherLo = "5")), "休"))
        assertFalse("他シフト下限が無ければ収まるので検出しない",
            hasLowerBoundIssue(V6SanityPort.buildGuidance(restLoState(10, restLo = "8")), "休"))
    }

    @Test fun aptSumCheckUsesRestCapacityInsteadOfNeedForRestShift() {
        // T=10・apt目標3(合計6)。休の実質上限=2人×10日=20 ≥ 6 → 誤検知しない。
        val rep = V6SanityPort.buildGuidance(aptVsNeedState(days = 10, need1 = "0", aptTarget = "3"))
        assertTrue("控えめな休の目標は誤検知しない(need=0との比較をやめた効果)",
            rep.none { it.where.contains("休") && it.where.contains("適切回数の合計") })
        assertTrue("同一設定の非休シフト(X)は従来どおり検出する",
            rep.any { it.where.contains("X") && it.where.contains("適切回数の合計") })
    }

    @Test fun aptSumCheckStillFlagsRestShiftWhenGenuinelyExcessive() {
        // T=2・apt目標5(合計10)。休の実質上限=2人×2日=4 < 10 → 本当に過大なので検出する。
        val rep = V6SanityPort.buildGuidance(aptVsNeedState(days = 2, need1 = "0", aptTarget = "5"))
        assertTrue("物理的に不可能な休の目標(T=2日に対し目標5)は検出すること",
            rep.any { it.where.contains("休") && it.where.contains("適切回数の合計") })
    }

    /**
     * [3.301.0 目標の検算を設定画面へ] `aptBalances` は設定ミス診断（検査6-C）と**同じ単一ソース**。
     * 目標カードはこの値を直接読んで「目標の合計 N回 ／ 必要人数 M回 → K回は必ず届きません」を出す。
     * ここがズレると「診断は警告するのに設定画面は何も言わない」状態に戻るため、両者の一致を固定する。
     *
     * T=10・X の必要人数は毎日1人＝合計10回。2名に目標6を設定すると合計12回で、2回ぶん届かない。
     */
    @Test fun aptBalancesMatchesTheSettingIssueAndReportsShortfall() {
        val st = aptVsNeedState(days = 10, need1 = "1", aptTarget = "6")
        val x = V6SanityPort.aptBalances(st).single { it.kigou == "X" }
        assertEquals("目標の合計は担当2名ぶん", 12, x.aptSum)
        assertEquals("受け止められる上限は必要人数の合計", 10, x.capacity)
        assertTrue("超過していること", x.overloaded)
        assertEquals("何回ぶん届かないか", 2, x.shortfall)
        assertTrue("非休シフトは isRest=false", !x.isRest)

        // 同じ設定に対し、設定ミス診断も同じ数字で警告すること（単一ソースの確認）。
        val issue = V6SanityPort.buildGuidance(st).single { it.where.contains("X") && it.where.contains("適切回数の合計") }
        assertTrue("診断の本文が検算と同じ合計値を使うこと", issue.problem.contains("12") && issue.problem.contains("10"))
    }

    /** 目標が上限に収まっていれば overloaded=false＝設定画面には何も出さない（誤警告を出さない）。 */
    @Test fun aptBalancesReportsNoOverloadWhenTargetsFitTheDemand() {
        val st = aptVsNeedState(days = 10, need1 = "1", aptTarget = "5")
        val x = V6SanityPort.aptBalances(st).single { it.kigou == "X" }
        assertEquals(10, x.aptSum)
        assertEquals(10, x.capacity)
        assertTrue("ちょうど収まるなら超過ではない", !x.overloaded)
        assertTrue("診断も出さないこと",
            V6SanityPort.buildGuidance(st).none { it.where.contains("X") && it.where.contains("適切回数の合計") })
    }

    /** 目標が1つも設定されていないシフトは検算対象外＝空欄運用のユーザーに何も見せない。 */
    @Test fun aptBalancesSkipsShiftsWithoutAnyTarget() {
        val st = aptVsNeedState(days = 10, need1 = "1", aptTarget = "")
        assertTrue("目標なしなら行そのものを出さない", V6SanityPort.aptBalances(st).isEmpty())
    }

    @Test fun aptSumCheckAccountsForOtherShiftLowerBoundsReducingRestCapacity() {
        // T=10・apt目標3(合計6)だが、他シフトXの個人下限が8(各自)設定済み＝休の実質上限=2人×(10-8)=4 < 6。
        val rep = V6SanityPort.buildGuidance(aptVsNeedState(days = 10, need1 = "0", aptTarget = "3", otherLo = "8"))
        assertTrue("他シフトの個人下限を差し引いた実質上限を下回るなら検出すること",
            rep.any { it.where.contains("休") && it.where.contains("適切回数の合計") })
    }

    // [3.242.0/6c=staffRange上限版・grilling確定=美幸・上條・大島の実例を踏まえ実装] 6bと同じ
    // 「担当レパートリーから強制される最低回数」ロジックを staffRange 上限(hi)にも適用する検査。
    // target(担当=休,X,Y): 休lo=hi=4固定・Yのhi=3・T=10日 → 休+Yの上限合計7では10日を埋めきれず、
    // 残り3日は必ずXに回る(強制下限3)。Xの個人上限は2なので、targetがXを担当し続ける限り上限超過は
    // 構造的に不可避。sub(G0=target側と同じグループ)はXを担当可能＝代用要員候補として提示されるはず。
    private fun rangeHiWallState(subCanDoX: Boolean): MagiState = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-10",
        shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", "")),
        groups = listOf(Group("G0", "G0"), Group("G1", "G1")),
        staff = listOf(Staff("target", 0), Staff("sub", if (subCanDoX) 0 else 1)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1), listOf(1, 0, 1)),   // G0=休,X,Y全部可 / G1=休,Yのみ可(X不可)
        groupShiftApt = listOf(listOf("", "", ""), listOf("", "", "")),
        schedule = listOf(List(10) { 0 }, List(10) { 0 }),
        wishes = emptyMap(),
        staffRange = mapOf(
            "0,0" to Range("4", "4"),   // target: 休 lo=hi=4固定
            "0,1" to Range("", "2"),    // target: X 上限2(対象)
            "0,2" to Range("", "3"),    // target: Y 上限3
        ),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun personalHighWallDetectsWhenForcedMinExceedsRangeHiAndListsSubstitute() {
        val rep = V6SanityPort.buildGuidance(rangeHiWallState(subCanDoX = true))
        val issue = rep.firstOrNull { it.where.contains("target") && it.where.contains("X") && it.where.contains("上限") }
        assertNotNull("担当構成上Xの強制下限(3)が上限(2)を超えるため検出されること", issue)
        assertTrue("代用要員候補(sub)が案内されること", issue!!.problem.contains("sub"))
    }

    @Test fun personalHighWallReportsNoSubstituteWhenNoneCanDo() {
        val rep = V6SanityPort.buildGuidance(rangeHiWallState(subCanDoX = false))
        val issue = rep.firstOrNull { it.where.contains("target") && it.where.contains("X") && it.where.contains("上限") }
        assertNotNull("subがXを担当できなくても壁自体は検出されること", issue)
        assertTrue("代用要員がいない旨が案内されること", issue!!.problem.contains("代用できる他の担当者がいません"))
    }

    @Test fun personalHighWallDoesNotFireWhenOtherShiftHasNoUpperBound() {
        // Yの上限を未設定(無制限)にすると、休+Y(無制限)だけで10日を埋めきれるため強制下限が0以下になり
        // 発火しない(6bと同じ保守的判定)。
        val st = rangeHiWallState(subCanDoX = true).let {
            it.copy(staffRange = it.staffRange - "0,2")
        }
        val rep = V6SanityPort.buildGuidance(st)
        assertTrue("他シフトに上限未設定が1つでもあれば誤検知しないこと",
            rep.none { it.where.contains("target") && it.where.contains("X") && it.where.contains("上限") })
    }

    /**
     * [3.309.0] 存在しないシフト記号を含む連続パターン行。旧実装は `Problem.resolveC3` が
     * 無言で捨てており、シフトを改名・削除すると禁止連続(HARD)が黙って無効化されるのに
     * 画面にもログにも何も出なかった（同じ関数の L>期間 のケースは記録して案内していた非対称）。
     */
    private fun unknownShiftState(pattern: List<String>) = MagiState(
        startDate = "2026-06-01", endDate = "2026-06-06",
        shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", "1", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = List(2) { listOf(1, 1, 0, 1, 1, 0) },
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
        cons3n = listOf(com.magi.app.model.C3Row(pattern)),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test fun warnsWhenForbiddenRunReferencesAMissingShiftSymbol() {
        // 「Cｳ」は今のシフト一覧に無い＝この行は評価されない。無言で消さず必ず案内する。
        val issues = V6SanityPort.buildGuidance(unknownShiftState(listOf("A", "Cｳ")))
        assertTrue("未定義記号の行が案内される: $issues",
            issues.any { it.problem.contains("今のシフト一覧にない") && it.where.contains("Cｳ") })
    }

    @Test fun doesNotWarnWhenAllShiftSymbolsResolve() {
        val issues = V6SanityPort.buildGuidance(unknownShiftState(listOf("A", "休")))
        assertTrue("既知記号だけなら未定義記号の案内は出さない",
            issues.none { it.problem.contains("今のシフト一覧にない") })
    }

    // ---- [3.320.0] 記号が解決できない制約行 / 「休」不在 の可視化 ----------------------------
    //
    // 3.309.0 は連続パターン(cons3系)の無言除外だけを直したが、窓の要件・個人の合計・群/スキル群の
    // レンジ・群/スキル群ペア禁止の6族にも同じ穴が残っていた。シフトや群を改名・削除すると、それを
    // 参照する行が警告なく評価対象から消える（窓の要件は重み15）。

    private fun unresolvedState(
        cons1: List<com.magi.app.model.C1Row> = emptyList(),
        cons41: List<com.magi.app.model.C41Row> = emptyList(),
        cons42: List<com.magi.app.model.C42Row> = emptyList(),
        restKigou: String = "休",
    ) = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-03",
        shifts = listOf(Shift(restKigou, restKigou, "0", ""), Shift("X", "X", "0", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(List(3) { 0 }),
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = cons1, cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = cons41, cons42 = cons42,
    )

    @Test fun unknownShiftInWindowRuleIsReported() {
        // 存在しない記号 NIGHT を参照する窓ルール。旧実装は Problem のパースで無言に捨てていた。
        val st = unresolvedState(cons1 = listOf(com.magi.app.model.C1Row("3", "NIGHT", "1")))
        assertTrue("前提: この行は評価対象から外れる", Problem(st).cons1.isEmpty())
        val rep = V6SanityPort.buildGuidance(st)
        assertTrue("窓の要件の未解決行が案内されること",
            rep.any { it.where.contains("窓の要件") && it.where.contains("〈NIGHT〉") })
    }

    @Test fun unknownGroupInPairBanIsReported() {
        // 存在しない群 GX を参照するペア禁止。
        val st = unresolvedState(cons42 = listOf(com.magi.app.model.C42Row("G", "GX", "X", "X")))
        assertTrue("前提: この行は評価対象から外れる", Problem(st).cons42.isEmpty())
        val rep = V6SanityPort.buildGuidance(st)
        assertTrue("群ペア禁止の未解決行が案内されること",
            rep.any { it.where.contains("群ペア禁止") && it.where.contains("〈GX〉") })
    }

    @Test fun nonNumericRangeRowIsReported() {
        // 群レンジの下限・上限がどちらも空＝評価できない行。記号は解決できているので〈〉は付かない。
        val st = unresolvedState(cons41 = listOf(com.magi.app.model.C41Row("G", "X", "", "")))
        assertTrue("前提: この行は評価対象から外れる", Problem(st).cons41.isEmpty())
        val rep = V6SanityPort.buildGuidance(st)
        assertTrue("群のレンジの未解決行が案内されること", rep.any { it.where.contains("群のレンジ") })
    }

    @Test fun resolvableRowsAreNotReported() {
        // 回帰: すべて解決できる行なら未解決の案内は出さない。
        val st = unresolvedState(
            cons1 = listOf(com.magi.app.model.C1Row("3", "X", "1")),
            cons41 = listOf(com.magi.app.model.C41Row("G", "X", "0", "1")),
        )
        val rep = V6SanityPort.buildGuidance(st)
        assertFalse("解決できる行は案内しない",
            rep.any { it.problem.contains("この行は評価されていません") })
    }

    @Test fun missingRestShiftIsReported() {
        // 記号「休」のシフトが無いと restShiftIndex が先頭シフト(index 0)を休として扱う。
        val st = unresolvedState(restKigou = "OFF")
        assertEquals("前提: 先頭シフトが休として扱われる", 0, Problem(st).restIdx)
        val rep = V6SanityPort.buildGuidance(st)
        assertTrue("「休」不在が案内されること", rep.any { it.where.contains("「休」のシフトがありません") })
        assertFalse("「休」があれば案内しない",
            V6SanityPort.buildGuidance(unresolvedState()).any { it.where.contains("「休」のシフトがありません") })
    }
}
