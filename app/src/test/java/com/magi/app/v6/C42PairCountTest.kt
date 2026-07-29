package com.magi.app.v6

import com.magi.app.model.C42Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * [3.318.0/HF77 ユーザー明示指示] c42・c42s の同日ペア計数から「自分自身とのペア」と
 * 「同一集合での順序重複」を除く。
 *
 * left（群 g1 で s1 に就いている職員）と right（群 g2 で s2 に就いている職員）が同じ集合になるのは
 * `g1==g2 && s1==s2` のときだけで、そのとき素朴な積 n² は自己ペア n 件と順序重複を余分に数えていた。
 * 実データでは `群9/休 × 群9/休` の行が**1人だけの群**に付いており、その人が休むたび自己ペアを
 * 1件ずつ数えていた（real は c42=16 のうち9件、user は 13 のうち8件）。
 *
 * チェッカー（`UnifiedViolationChecker`）と評価器（`Evaluator`）は同じ値を返さなければならないので、
 * 各ケースで両方を確認する。
 */
class C42PairCountTest {

    /** 2職員・1日・シフト {休, X, Y}。群 G0 に s0/s1、G1 に s2。cons42 は呼び出し側が与える。 */
    private fun st(sched: List<List<Int>>, cons42: List<C42Row>) = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-01",
        shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", "")),
        groups = listOf(Group("G0", "G0"), Group("G1", "G1")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0), Staff("s2", 1)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1), listOf(1, 1, 1)),
        groupShiftApt = listOf(listOf("", "", ""), listOf("", "", "")),
        schedule = sched,
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = cons42,
    )

    private fun c42Of(s: MagiState): Int =
        UnifiedViolationChecker.check(s, s.schedule.toIntArray2D()).breakdown["c42"] ?: 0

    /**
     * 評価器における c42 の寄与だけを取り出す＝同じ盤面で cons42 を空にしたときの soft との差。
     * soft には fair/weekly/covO 等も混ざるため、素の soft では c42 を検証できない。
     */
    private fun evalC42Of(s: MagiState): Long {
        val sched = s.schedule.toIntArray2D()
        val withRule = Evaluator(Problem(s)).fullEvalParts(sched)[1]
        val without = Evaluator(Problem(s.copy(cons42 = emptyList()))).fullEvalParts(sched)[1]
        return withRule - without
    }

    @Test
    fun sameGroupSameShiftWithOnlyOneStaffIsNotAViolation() {
        // 群 G0 の X × 群 G0 の X。その日 X に就いているのは s0 ひとりだけ＝「異なる2人のペア」は存在しない。
        // 旧実装は n²=1 で自己ペアを1件数えていた。
        val s = st(listOf(listOf(1), listOf(0), listOf(0)), listOf(C42Row("G0", "G0", "X", "X")))
        assertEquals("1人だけなら違反なし", 0, c42Of(s))
        assertEquals("評価器もチェッカーと同値", 0L, evalC42Of(s))
    }

    @Test
    fun sameGroupSameShiftWithTwoStaffCountsOnePair() {
        // 同じ行で s0/s1 の2人が X。異なる2人のペアは1組だけ＝C(2,2)=1。旧実装は 2²=4 件。
        val s = st(listOf(listOf(1), listOf(1), listOf(0)), listOf(C42Row("G0", "G0", "X", "X")))
        assertEquals("2人ならペアは1組", 1, c42Of(s))
        assertEquals("評価器もチェッカーと同値", 1L, evalC42Of(s))
    }

    @Test
    fun sameGroupSameShiftWithThreeStaffCountsThreePairs() {
        // 3人が同じ X に就くと C(3,2)=3 組。旧実装は 3²=9 件。
        val s3 = st(listOf(listOf(1), listOf(1), listOf(1)), listOf(C42Row("G0", "G0", "X", "X")))
            .let { it.copy(staff = listOf(Staff("s0", 0), Staff("s1", 0), Staff("s2", 0))) }
        assertEquals("3人ならペアは3組", 3, c42Of(s3))
        assertEquals("評価器もチェッカーと同値", 3L, evalC42Of(s3))
    }

    @Test
    fun distinctGroupsAreUnchanged() {
        // 群が違えば left と right は互いに素なので従来どおり |left|×|right|。回帰の固定。
        // G0 の X = s0/s1 の2人、G1 の Y = s2 の1人 → 2×1 = 2 件。
        val s = st(listOf(listOf(1), listOf(1), listOf(2)), listOf(C42Row("G0", "G1", "X", "Y")))
        assertEquals("異なる群の組合せは従来どおり", 2, c42Of(s))
        assertEquals("評価器もチェッカーと同値", 2L, evalC42Of(s))
    }

    @Test
    fun sameGroupDifferentShiftsAreUnchanged() {
        // 同じ群でもシフトが違えば同じ職員が両方に就くことはなく、集合は互いに素。
        // G0 の X = s0、G0 の Y = s1 → 1×1 = 1 件。
        val s = st(listOf(listOf(1), listOf(2), listOf(0)), listOf(C42Row("G0", "G0", "X", "Y")))
        assertEquals("同じ群でもシフトが違えば従来どおり", 1, c42Of(s))
        assertEquals("評価器もチェッカーと同値", 1L, evalC42Of(s))
    }
}
