package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [可変長ブロック交換] applyAdaptiveBlockSwapPolish の検証。
 * 旧 applyBlockSwapPolish（3.300.0 で削除）は「同一担当グループ × 15日固定」のため、
 *   (a) 別グループ同士の交換
 *   (b) 15日以外の長さ（11/13/17/19/23/28）
 * に到達できなかった。本テストは (a)+(b) の両方を同時に要求する最小盤面で、
 * 新演算子だけが改善に到達することを固定する。
 */
class AdaptiveBlockSwapPolishTest {

    /**
     * T=11・2職員・別グループ。両者とも 休/X/Y を担当できる。
     * X も Y も毎日1人必要なので、2人は必ず「片方X・片方Y」に分かれる＝被覆は交換で不変。
     * 個人の回数ピンは A=「Xを11回」/ B=「Yを11回」だが、初期盤面は真逆（A=Y×11, B=X×11）。
     * → 11日ブロックを丸ごと交換した時だけ両者の下限割れが同時に解消する。
     */
    private fun crossGroupState(wishes: Map<String, Int> = emptyMap()): MagiState {
        val shifts = listOf(
            Shift("休み", "休", "", ""),
            Shift("X", "X", "1", "1"),
            Shift("Y", "Y", "1", "1"),
        )
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"))
        val staff = listOf(Staff("A", 0), Staff("B", 1))
        return MagiState(
            startDate = "2026-02-01", endDate = "2026-02-11",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1), listOf(1, 1, 1)),   // 両グループとも 休/X/Y 可
            groupShiftApt = List(2) { List(3) { "" } },
            schedule = listOf(
                List(11) { 2 },   // A: Y×11（Xの下限11を丸ごと割っている）
                List(11) { 1 },   // B: X×11（Yの下限11を丸ごと割っている）
            ),
            wishes = wishes,
            staffRange = mapOf(
                "0,1" to Range("11", "11"),   // A の X を 11 に固定
                "1,2" to Range("11", "11"),   // B の Y を 11 に固定
            ),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun adaptiveSwapCrossesGroupsAndBlockLengthsThatTheLegacyPassCannotReach() {
        val st = crossGroupState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertTrue("初期は個人下限割れがある", (before.breakdown["low"] ?: 0) > 0)
        assertEquals("初期 HARD=0（被覆は満たしている）", 0, before.hard)

        // [3.300.0] 旧 applyBlockSwapPolish（同一グループ×15日固定）は削除済み。この盤面は同一グループの
        //   ペアが存在しないため旧パスは手を1つも作れなかった＝ここで確認する改善は新演算子に固有のもの。
        //   別グループ×11日ブロックで両者の下限割れが同時に解消する。
        val res = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, sched.copy2D())
        val after = UnifiedViolationChecker.check(st, res.newSchedule)
        assertTrue("可変長ブロック交換が採用されたこと", res.applied > 0)
        assertEquals("個人下限割れが解消", 0, after.breakdown["low"] ?: 0)
        assertEquals("HARD は不変(=0)", 0, after.hard)
        // 被覆保存: 各日の X/Y はそれぞれ1人のまま。
        for (j in 0 until 11) {
            val col = (0 until 2).map { res.newSchedule[it][j] }
            assertEquals("日${j + 1}のXは1人", 1, col.count { it == 1 })
            assertEquals("日${j + 1}のYは1人", 1, col.count { it == 2 })
        }
    }

    /**
     * [3.291.0 候補生成の緩和] 希望固定日をブロック内に含んでいても、その日だけ据え置いて残りを交換する。
     *
     * 旧（全か無か）の候補生成では、ブロック内に希望固定が1日でもあれば `return null` でブロックごと棄却
     * していた。この盤面は T=11 で有効な長さが 11 のみ＝唯一のブロック(0〜10日)が固定日を必ず含むため、
     * 旧実装なら候補0件＝完全に不活性になる。緩和後は固定日を除く10日が交換され、下限割れが 22→2 まで縮む
     * （固定日ぶんの1回だけ届かない＝据え置きの意味論そのもの）。
     */
    @Test
    fun adaptiveSwapKeepsWishLockedDaysInPlaceAndSwapsTheRest() {
        // A は6日目(index 5)に Y を希望＝その日は動かせない。
        val st = crossGroupState(wishes = mapOf("0,5" to 2))
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期の下限割れ合計(A11+B11)", 22, before.breakdown["low"] ?: 0)

        val res = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, sched.copy2D())
        val after = UnifiedViolationChecker.check(st, res.newSchedule)
        assertTrue("固定日があっても残りの日で交換が成立すること", res.applied > 0)
        assertEquals("希望固定日の A は据え置き(Y のまま)", 2, res.newSchedule[0][5])
        assertEquals("希望固定日の B も据え置き(X のまま)", 1, res.newSchedule[1][5])
        assertEquals("固定日ぶん(A・B 各1回)だけ残して下限割れが縮む", 2, after.breakdown["low"] ?: 0)
        assertEquals("HARD は不変(=0・希望も充足のまま)", 0, after.hard)
        for (j in 0 until 11) {
            val col = (0 until 2).map { res.newSchedule[it][j] }
            assertEquals("日${j + 1}のXは1人", 1, col.count { it == 1 })
            assertEquals("日${j + 1}のYは1人", 1, col.count { it == 2 })
        }
    }

    /**
     * [3.292.0 3者巡回] 3職員がそれぞれ「担当できる2シフト」しか持たず、**どの2者交換も担当不可で成立しない**が
     * 3者巡回 A←C←B←A なら全員が目標シフトへ収まる盤面。
     *
     * A: X/Y 可（今 Y・X を11回欲しい） / B: Y/Z 可（今 Z・Y を11回欲しい） / C: X/Z 可（今 X・Z を11回欲しい）。
     * 2者交換は A↔B が Z を A に、B↔C が X を B に、A↔C が Y を C に渡すため**3通りとも canDo で不成立**。
     */
    private fun threeWayCycleState(): MagiState {
        val shifts = listOf(
            Shift("休み", "休", "", ""),
            Shift("X", "X", "1", "1"),
            Shift("Y", "Y", "1", "1"),
            Shift("Z", "Z", "1", "1"),
        )
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"), Group("G2", "G2"))
        return MagiState(
            startDate = "2026-02-01", endDate = "2026-02-11",
            shifts = shifts, groups = groups,
            staff = listOf(Staff("A", 0), Staff("B", 1), Staff("C", 2)),
            use2Patterns = false,
            groupShift = listOf(
                listOf(0, 1, 1, 0),   // A: X/Y
                listOf(0, 0, 1, 1),   // B: Y/Z
                listOf(0, 1, 0, 1),   // C: X/Z
            ),
            groupShiftApt = List(3) { List(4) { "" } },
            schedule = listOf(
                List(11) { 2 },   // A: Y×11（欲しいのは X）
                List(11) { 3 },   // B: Z×11（欲しいのは Y）
                List(11) { 1 },   // C: X×11（欲しいのは Z）
            ),
            wishes = emptyMap(),
            staffRange = mapOf(
                "0,1" to Range("11", "11"),   // A の X を 11 に固定
                "1,2" to Range("11", "11"),   // B の Y を 11 に固定
                "2,3" to Range("11", "11"),   // C の Z を 11 に固定
            ),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun threeWayCycleSolvesWhatNoTwoWaySwapCan() {
        val st = threeWayCycleState()
        val sched = st.schedule.toIntArray2D()
        assertEquals("初期の下限割れ合計(3人×11)", 33, UnifiedViolationChecker.check(st, sched).breakdown["low"] ?: 0)

        // 2者交換までに制限すると、どの手も担当不可で成立しない。
        val pairOnly = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, sched.copy2D(), maxCycle = 2)
        assertEquals("2者交換だけでは到達不能", 0, pairOnly.applied)

        // 3者巡回を許すと1手で全員が目標へ収まる。
        val res = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, sched.copy2D())
        val after = UnifiedViolationChecker.check(st, res.newSchedule)
        assertTrue("3者巡回が採用されたこと", res.applied > 0)
        assertEquals("下限割れが完全に解消", 0, after.breakdown["low"] ?: 0)
        assertEquals("HARD は不変(=0)", 0, after.hard)
        for (j in 0 until 11) {
            val col = (0 until 3).map { res.newSchedule[it][j] }
            assertEquals("日${j + 1}の被覆保存(X/Y/Z 各1人)", listOf(1, 2, 3), col.sorted())
        }
    }

    /**
     * [3.292.0 多者巡回] 4職員が一本の4者巡回でしか解けない盤面。
     * A: P/Q 可（今 Q・P が目標） / B: Q/R（今 R・Q が目標） / C: R/S（今 S・R が目標） / D: S/P（今 P・S が目標）。
     * 2者交換も3者巡回も canDo で全滅し、A←D←C←B←A の4者巡回だけが閉じる。
     */
    private fun fourWayCycleState(): MagiState {
        val shifts = listOf(
            Shift("休み", "休", "", ""),
            Shift("P", "P", "1", "1"),
            Shift("Q", "Q", "1", "1"),
            Shift("R", "R", "1", "1"),
            Shift("S", "S", "1", "1"),
        )
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"), Group("G2", "G2"), Group("G3", "G3"))
        return MagiState(
            startDate = "2026-02-01", endDate = "2026-02-11",
            shifts = shifts, groups = groups,
            staff = listOf(Staff("A", 0), Staff("B", 1), Staff("C", 2), Staff("D", 3)),
            use2Patterns = false,
            groupShift = listOf(
                listOf(0, 1, 1, 0, 0),   // A: P/Q
                listOf(0, 0, 1, 1, 0),   // B: Q/R
                listOf(0, 0, 0, 1, 1),   // C: R/S
                listOf(0, 1, 0, 0, 1),   // D: S/P
            ),
            groupShiftApt = List(4) { List(5) { "" } },
            schedule = listOf(
                List(11) { 2 },   // A: Q×11（欲しいのは P）
                List(11) { 3 },   // B: R×11（欲しいのは Q）
                List(11) { 4 },   // C: S×11（欲しいのは R）
                List(11) { 1 },   // D: P×11（欲しいのは S）
            ),
            wishes = emptyMap(),
            staffRange = mapOf(
                "0,1" to Range("11", "11"),
                "1,2" to Range("11", "11"),
                "2,3" to Range("11", "11"),
                "3,4" to Range("11", "11"),
            ),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun fourWayCycleSolvesWhatShorterCyclesCannot() {
        val st = fourWayCycleState()
        val sched = st.schedule.toIntArray2D()
        assertEquals("初期の下限割れ合計(4人×11)", 44, UnifiedViolationChecker.check(st, sched).breakdown["low"] ?: 0)

        val upToThree = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, sched.copy2D(), maxCycle = 3)
        assertEquals("3者巡回まででは到達不能", 0, upToThree.applied)

        val res = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, sched.copy2D())
        val after = UnifiedViolationChecker.check(st, res.newSchedule)
        assertTrue("4者巡回が採用されたこと", res.applied > 0)
        assertEquals("下限割れが完全に解消", 0, after.breakdown["low"] ?: 0)
        assertEquals("HARD は不変(=0)", 0, after.hard)
        for (j in 0 until 11) {
            val col = (0 until 4).map { res.newSchedule[it][j] }
            assertEquals("日${j + 1}の被覆保存(P/Q/R/S 各1人)", listOf(1, 2, 3, 4), col.sorted())
        }
    }

    /**
     * [3.294.0 ピン保存交換] ブロック全体を交換すると厳密ピン(lo==hi)が崩れる盤面で、
     * **ピンの回数が変わらない部分集合**だけを交換して改善に到達する。
     *
     * A は休4回で固定（lo=hi=4・充足中）、Y が下限2に対し0回。ブロック(11日)を丸ごと B と交換すると
     * A の休は 4→2 になり `exactPinRegression` で必ず却下される（＝旧実装なら採用0）。
     * 休の増減が打ち消し合う日だけを選べば、休4を保ったまま Y の下限割れを解消できる。
     */
    private fun pinnedRestState(): MagiState {
        val shifts = listOf(
            Shift("休み", "休", "1", "1"),
            Shift("X", "X", "1", "1"),
            Shift("Y", "Y", "1", "1"),
        )
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"), Group("G2", "G2"))
        return MagiState(
            startDate = "2026-02-01", endDate = "2026-02-11",
            shifts = shifts, groups = groups,
            staff = listOf(Staff("A", 0), Staff("B", 1), Staff("C", 2)),
            use2Patterns = false,
            groupShift = List(3) { listOf(1, 1, 1) },
            groupShiftApt = List(3) { List(3) { "" } },
            schedule = listOf(
                listOf(0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1),   // A: 休4 / X7 / Y0
                listOf(1, 1, 1, 1, 0, 0, 2, 2, 2, 2, 2),   // B: X4 / 休2 / Y5
                listOf(2, 2, 2, 2, 2, 2, 0, 0, 0, 0, 0),   // C: Y6 / 休5
            ),
            wishes = emptyMap(),
            staffRange = mapOf(
                "0,0" to Range("4", "4"),   // A の休を4回に固定（現状ちょうど充足）
                "0,2" to Range("2", ""),    // A の Y は2回以上（現状0＝下限割れ）
            ),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun pinPreservingSwapKeepsExactPinAndStillImproves() {
        val st = pinnedRestState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期は A の Y が下限2に対し0＝下限割れ2", 2, before.breakdown["low"] ?: 0)
        assertEquals("初期の A の休は4（ピン充足）", 4, (0 until 11).count { sched[0][it] == 0 })

        val res = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, sched.copy2D())
        val after = UnifiedViolationChecker.check(st, res.newSchedule)
        assertTrue("部分集合の交換が採用されたこと", res.applied > 0)
        assertEquals("厳密ピン（A の休4）が保たれること", 4, (0 until 11).count { res.newSchedule[0][it] == 0 })
        assertTrue("下限割れが減ること", (after.breakdown["low"] ?: 0) < (before.breakdown["low"] ?: 0))
        assertEquals("HARD は不変(=0)", 0, after.hard)
        for (j in 0 until 11) {
            val col = (0 until 3).map { res.newSchedule[it][j] }
            assertEquals("日${j + 1}の被覆保存(休/X/Y 各1人)", listOf(0, 1, 2), col.sorted())
        }
    }

    @Test
    fun adaptiveSwapIsNoOpOnAlreadyOptimalBoard() {
        val st = crossGroupState()
        // 最初から正しい配置（A=X, B=Y）にしておく。
        val ok = arrayOf(IntArray(11) { 1 }, IntArray(11) { 2 })
        val res = V6HotfixPasses.applyAdaptiveBlockSwapPolish(st, ok)
        assertEquals("改善手が無ければ採用0", 0, res.applied)
        assertTrue("ログが出ること", res.logs.isNotEmpty())
    }
}
