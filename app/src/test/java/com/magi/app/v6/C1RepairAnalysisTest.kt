package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.C3Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.273.0] C1 Repair Analysis（A6）＋ 厳密窓修復（A2/A3/A4）の検証。
 * 各テストは「厳密探索が単一same-day swapの合成では到達できない多日多職員連動手を見つける」
 * または「coverage入替でも解消不能を証明する」ことを、手計算で答えを設計した最小盤面で固定する。
 */
class C1RepairAnalysisTest {

    private fun st(
        days: Int,
        staff: Int,
        sched: List<List<Int>>,
        cons1: List<C1Row>,
        staffRange: Map<String, Range> = emptyMap(),
        cons3n: List<C3Row> = emptyList(),
    ): MagiState {
        val end = "2026-01-" + days.toString().padStart(2, '0')
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", ""))
        return MagiState(
            startDate = "2026-01-01", endDate = end,
            shifts = shifts, groups = listOf(Group("G", "G")),
            staff = List(staff) { Staff("s$it", 0) }, use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)), groupShiftApt = listOf(listOf("", "", "")),
            schedule = sched, wishes = emptyMap(), staffRange = staffRange,
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = cons1, cons2 = emptyList(), cons3 = emptyList(),
            cons3n = cons3n, cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    // ---- [3.315.0] 探索の目的関数を実採否と揃える（厳密ピン・c3n） -----------------------------
    //
    // 共通盤面: 3日・2職員・ルール「X 2日窓≥1」（窓 [0,1] と [1,2]）。
    //   i0 = Y,Y,Y → 2窓とも不足        a = X,X,Y → 充足
    // X トークンは day0/day1 に1個ずつ。coverage 保存の並べ替えで到達できる配置は4通りで、
    // joint c1 は baseline=2、最小=1。最小を取る配置は**必ず i0 が X を1個受け取る**
    //   （i0 が X を取らない配置は i0 が2件のまま＝joint>=2）。手計算で全4通りを検算済み。

    private fun pinFixture(staffRange: Map<String, Range> = emptyMap(), cons3n: List<C3Row> = emptyList()) =
        st(3, 2, listOf(listOf(2, 2, 2), listOf(1, 1, 2)), listOf(C1Row("2", "X", "1")), staffRange, cons3n)

    @Test
    fun exactSolveFindsPatchWhenNoPinOrForbiddenRunBlocksIt() {
        // 回帰: 制約が無ければ従来どおり joint 2→1 の手を見つける。
        val s = pinFixture()
        val p = Problem(s); val sched = s.schedule.toIntArray2D()
        assertEquals(2, UnifiedViolationChecker.check(s, sched).breakdown["c1"])
        val v = C1RepairAnalysis.analyze(p, sched).first { it.staff == 0 }
        val r = C1RepairAnalysis.solveWindow(p, sched, v)
        assertEquals("baseline は joint 2", 2, r.baselineJointC1)
        assertEquals("coverage保存で joint 1 まで下げられる", 1, r.minJointC1)
        assertNotNull("改善手が出る", r.patch)
    }

    @Test
    fun exactSolveRejectsPatchThatBreaksAnExactPin() {
        // i0 の X を 0回に固定（lo==hi==0・現状も0＝ピン充足中）。joint を下げる配置は必ず i0 が X を
        // 受け取るので、ピンを守る限り改善手は存在しない。旧実装は joint c1 しか見ずこれを提案していた。
        val s = pinFixture(staffRange = mapOf("0,1" to Range("0", "0")))
        val p = Problem(s); val sched = s.schedule.toIntArray2D()
        val v = C1RepairAnalysis.analyze(p, sched).first { it.staff == 0 }
        val r = C1RepairAnalysis.solveWindow(p, sched, v)
        assertNull("厳密ピンを崩す手は候補にしない", r.patch)
        assertEquals("採用候補が無いので baseline のまま", r.baselineJointC1, r.minJointC1)
    }

    @Test
    fun exactSolveRejectsPatchThatCreatesForbiddenRun() {
        // 禁止「Y→X」。baseline は i0=Y,Y,Y / a=X,X,Y で fire 0。joint を下げる配置は i0 か a のどちらかに
        // Y→X を作る（X を後ろの日へ移すため）ので、c3n を増やさない限り改善手は存在しない。
        val s = pinFixture(cons3n = listOf(C3Row(listOf("Y", "X"))))
        val p = Problem(s); val sched = s.schedule.toIntArray2D()
        assertEquals("baseline に禁止連続は無い", 0, UnifiedViolationChecker.check(s, sched).breakdown["c3n"] ?: 0)
        val v = C1RepairAnalysis.analyze(p, sched).first { it.staff == 0 }
        val r = C1RepairAnalysis.solveWindow(p, sched, v)
        assertNull("禁止連続を増やす手は候補にしない", r.patch)
        assertEquals("採用候補が無いので baseline のまま", r.baselineJointC1, r.minJointC1)
    }

    @Test
    fun analyzeEnumeratesDeficientWindowsMatchingChecker() {
        // i0: X X Y Y  a: Y Y X X, ルール「X 2日窓≥1」。checker の c1 と件数一致を確認。
        val s = st(4, 2, listOf(listOf(1, 1, 2, 2), listOf(2, 2, 1, 1)), listOf(C1Row("2", "X", "1")))
        val p = Problem(s)
        val rep = UnifiedViolationChecker.check(s, s.schedule.toIntArray2D())
        val vios = C1RepairAnalysis.analyze(p, s.schedule.toIntArray2D())
        assertEquals("analyze の窓件数は checker の c1 と一致", rep.breakdown["c1"], vios.size)
    }

    @Test
    fun exactSolveFindsCoordinatedCrossDayMultiStaffMove() {
        // i0: X X Y Y  a: Y Y X X, coverage=各日{X,Y}固定. ルール「X 2日窓≥1」.
        // 唯一の0達成は day1,day2 双方の i0<->a swap（多日連動）＝単一same-day swapの1手では到達不能.
        val s = st(4, 2, listOf(listOf(1, 1, 2, 2), listOf(2, 2, 1, 1)), listOf(C1Row("2", "X", "1")))
        val p = Problem(s); val sched = s.schedule.toIntArray2D()
        val base = UnifiedViolationChecker.check(s, sched)
        assertEquals(2, base.breakdown["c1"])
        val v = C1RepairAnalysis.analyze(p, sched).first { it.staff == 0 }
        val r = C1RepairAnalysis.solveWindow(p, sched, v)
        assertTrue("探索を完了(exhaustive)", r.exhaustive)
        assertEquals("joint c1 を 0 まで下げられると証明", 0, r.minJointC1)
        val patch = requireNotNull(r.patch)
        assertTrue("多日連動(2日以上を触る)", patch.map { it[1] }.distinct().size >= 2)
        // 適用して checker で確認: c1=0・coverage保存
        val w = sched.map { it.clone() }.toTypedArray()
        for (op in patch) w[op[0]][op[1]] = op[2]
        val after = UnifiedViolationChecker.check(s, w)
        assertEquals(0, after.breakdown["c1"])
        for (d in 0 until p.T) for (k in 0 until p.K) {
            assertEquals("coverage保存 d=$d k=$k",
                (0 until p.S).count { sched[it][d] == k }, (0 until p.S).count { w[it][d] == k })
        }
    }

    @Test
    fun exactSolveProvesCoverageNeutralWallWhenTokensAreTrulyScarce() {
        // [3.274.0 監査で再設計] 窓内に X トークンが**1個しかない**構成で i0 が「X 3日窓≥2」を要求。
        //   どう並べ替えても i0 は窓内 X を最大1回しか持てない＝真の壁（minFocusResidual=1>0 を証明）。
        //   旧テストは「各日 X 1個(計3個)」で i0 が2個取れる＝壁でない構成を、rows未復元バグ由来の
        //   false wall で「壁」と誤検出していたのを固定していた（本セッションの監査で判明・是正）。
        // day: 0 1 2  i0: Y Y Y (X=0)  a: X 休 休 (X=1のみ)  → 窓内 X トークンは day0 の1個だけ.
        val s = st(3, 2, listOf(listOf(2, 2, 2), listOf(1, 0, 0)), listOf(C1Row("3", "X", "2")))
        val p = Problem(s); val sched = s.schedule.toIntArray2D()
        val v = C1RepairAnalysis.analyze(p, sched).first { it.staff == 0 }
        val r = C1RepairAnalysis.solveWindow(p, sched, v)
        assertTrue("探索完了", r.exhaustive)
        assertTrue("焦点は coverage入替でも X を2回持てない(残>0を証明)", r.focusResidual > 0)
        assertNull("焦点のjoint改善patchは存在しない", r.patch)
        val walls = C1RepairAnalysis.provenWalls(p, sched)
        assertTrue("provenWalls が i0 の真の壁を検出", walls.any { it.staff == 0 && it.shift == 1 })
    }

    @Test
    fun provenWallsDoesNotFalselyFlagWhenFocusIsCoverageNeutrallySatisfiable() {
        // [3.274.0 監査回帰] 各日 X が1個ずつ(計3個)なら i0 は day0,day2 の X を取って窓を充足できる
        //   ＝壁ではない。min-joint配置では i0 が1個止まりでも、min-focus では0にできる。旧バグは
        //   これを false wall と誤検出していた。健全化後は wall を出さないことを固定する。
        val s = st(3, 3, listOf(listOf(2, 0, 2), listOf(1, 2, 1), listOf(0, 1, 0)), listOf(C1Row("3", "X", "2")))
        val p = Problem(s); val sched = s.schedule.toIntArray2D()
        val walls = C1RepairAnalysis.provenWalls(p, sched)
        assertTrue("解消可能な窓を壁と誤検出しない", walls.none { it.staff == 0 })
    }

    @Test
    fun provenWallsExaminesEveryWindowNotJustTheFirstPerStaffShift() {
        // [3.279.0/外部レビューC1-04] 同一職員・同一シフトに独立した複数の不足窓。
        //   i0: [Y,Y,Y,Y,Y]・a: [X,休,休,休,休]・ルール「X 2日窓≥1」。
        //   窓[0,1]: day0 の X トークンを i0 が取れる＝解消可能（壁でない）。
        //   窓[1,2]: 列1・2に X トークンが存在しない＝どう並べ替えても解消不能（真の壁）。
        //   旧: seen が staff×shift のみで最初の窓[0,1]しか探索せず、後続の真の壁を見逃していた。
        val s = st(5, 2, listOf(listOf(2, 2, 2, 2, 2), listOf(1, 0, 0, 0, 0)), listOf(C1Row("2", "X", "1")))
        val p = Problem(s); val sched = s.schedule.toIntArray2D()
        val walls = C1RepairAnalysis.provenWalls(p, sched)
        assertTrue("2窓目以降の真の壁を検出（旧実装は見逃し）", walls.any { it.staff == 0 && it.start == 1 })
        assertTrue("解消可能な窓[0,1]は壁と誤検出しない", walls.none { it.staff == 0 && it.start == 0 })
    }

    @Test
    fun passAppliesExactRepairAndIsKeepBestSafe() {
        val s = st(4, 2, listOf(listOf(1, 1, 2, 2), listOf(2, 2, 1, 1)), listOf(C1Row("2", "X", "1")))
        val sched = s.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(s, sched)
        val res = V6HotfixPasses.applyC1ExactWindowRepair(s, sched)
        val after = UnifiedViolationChecker.check(s, res.newSchedule)
        assertTrue("c1 が改善", (after.breakdown["c1"] ?: 0) < (before.breakdown["c1"] ?: 0))
        assertTrue("HARD 非悪化", after.hard <= before.hard)
        assertTrue("total 非悪化", after.total <= before.total)
        // 入力配列は不変（呼出側が別名共有しても安全）
        assertTrue(sched.indices.all { sched[it].contentEquals(s.schedule[it].toIntArray()) })
    }

    @Test
    fun passIsNoOpWhenNoCons1() {
        val s = st(3, 2, listOf(listOf(1, 0, 1), listOf(0, 1, 0)), emptyList())
        val res = V6HotfixPasses.applyC1ExactWindowRepair(s, s.schedule.toIntArray2D())
        assertEquals(0, res.applied)
    }

    /**
     * [3.314.0] 「証明済み壁」は探索空間を尽くしたときだけ名乗ってよい。旧実装は余力職員を
     * **同群限定**で集め、しかも `maxInvolvedStaff` の cap で候補を切り捨てたあとも exhaustive=true を
     * 返していた＝真部分集合しか見ていないのに壁を証明していた。cap=1（焦点職員のみ）で呼べば
     * 候補は必ず切り捨てられるので、exhaustive を名乗ってはならない。
     */
    @Test
    fun truncatedCandidateSetMustNotClaimAnExhaustiveProof() {
        // 2職員×4日、ルール「X 2日窓>=1」。i1 は X を持つので候補になり得るが cap=1 で切り捨てられる。
        val state = st(4, 2, listOf(listOf(2, 2, 2, 2), listOf(1, 1, 1, 1)), listOf(C1Row("2", "X", "1")))
        val p = Problem(state)
        val v = C1RepairAnalysis.analyze(p, state.schedule.toIntArray2D()).firstOrNull()
        assertNotNull("不足窓が検出される前提", v)
        val capped = C1RepairAnalysis.solveWindow(
            p, state.schedule.toIntArray2D(), v!!,
            C1RepairAnalysis.Config(maxInvolvedStaff = 1),
        )
        assertFalse("候補を cap で切り捨てたら証明を名乗らない", capped.exhaustive)
    }
}
