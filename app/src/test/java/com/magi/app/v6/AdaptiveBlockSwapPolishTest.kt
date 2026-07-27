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
 * 旧 applyBlockSwapPolish は「同一担当グループ × 15日固定」のため、
 *   (a) 別グループ同士の交換
 *   (b) 15日以外の長さ（11/13/17/19/23/28）
 * に到達できなかった。本テストは (a)+(b) の両方を同時に要求する最小盤面で、
 * 新演算子だけが改善に到達することを固定する（旧パスが 0 採用であることも同じ盤面で確認）。
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

        // 旧パス: 同一グループのペアが存在しない＝そもそも手が無い。
        val legacy = V6HotfixPasses.applyBlockSwapPolish(st, sched.copy2D(), blockLen = 15, maxPasses = 3)
        assertEquals("旧15日固定・同一グループ限定では到達不能", 0, legacy.applied)

        // 新パス: 別グループ×11日ブロックで両者の下限割れが同時に解消する。
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
