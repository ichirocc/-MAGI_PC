package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.C2Row
import com.magi.app.model.C3Row
import com.magi.app.model.C41Row
import com.magi.app.model.C42Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * [3.429.0/R-03] 削除確認ダイアログへ渡す影響件数（shiftRefCount/groupRefCount/skillGroupRefCount）。
 * `Problem.shiftIdxOf`/`groupIdxOf`/`skillGroupIdxOf` と同じ厳密一致(==)で数えることを固定する。
 */
class Ws1OpsRefCountTest {

    private fun state() = MagiState(
        startDate = "2026-07-01", endDate = "2026-07-02",
        shifts = listOf(Shift("日勤", "日", "", ""), Shift("休み", "休", "", ""), Shift("夜勤", "夜", "", "")),
        groups = listOf(Group("A", "A"), Group("B", "B")),
        staff = listOf(Staff("s1", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1, 1), listOf(1, 1, 1)),
        groupShiftApt = emptyList(),
        schedule = listOf(listOf(0, 1)),
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = listOf(C1Row("4", "日", "1")),
        cons2 = listOf(C2Row("日", "3"), C2Row("休", "2")),
        cons3 = listOf(C3Row(listOf("日", "夜"))),
        cons3n = emptyList(),
        cons3m = listOf(C3Row(listOf("休", ""))),
        cons3mn = emptyList(),
        cons41 = listOf(C41Row("A", "日", "1", "2"), C41Row("B", "夜", "0", "1")),
        cons42 = listOf(C42Row("A", "B", "日", "夜")),
        skillGroups = listOf(Group("リーダー", "L"), Group("新人", "N")),
        cons41s = listOf(C41Row("L", "日", "1", "1")),
        cons42s = listOf(C42Row("L", "N", "夜", "休")),
    )

    @Test fun shiftRefCountSumsAllReferencingFamilies() {
        val s = state()
        // 「日」: cons1(1) + cons2(1) + cons3 pattern(1) + cons41(1) + cons42 s1(1) + cons41s(1) = 6
        assertEquals(6, Ws1Ops.shiftRefCount(s, "日"))
        // 「夜」: cons3 pattern(1) + cons41(1) + cons42 s2(1) + cons42s s1(1) = 4
        assertEquals(4, Ws1Ops.shiftRefCount(s, "夜"))
        // 「休」: cons2(1) + cons3m pattern(1) + cons42s s2(1) = 3
        assertEquals(3, Ws1Ops.shiftRefCount(s, "休"))
    }

    @Test fun shiftRefCountIsZeroForUnreferencedOrUnknownSymbol() {
        val s = state().copy(shifts = state().shifts + Shift("A4", "A4", "", ""))
        assertEquals(0, Ws1Ops.shiftRefCount(s, "A4"))
        assertEquals(0, Ws1Ops.shiftRefCount(s, "存在しない記号"))
    }

    @Test fun groupRefCountSumsCons41And42Only() {
        val s = state()
        assertEquals(2, Ws1Ops.groupRefCount(s, "A")) // cons41(1) + cons42 g1(1)
        assertEquals(2, Ws1Ops.groupRefCount(s, "B")) // cons41(1) + cons42 g2(1)
        // スキル群 "L" は勤務グループの参照カウントには入らない（別分類）
        assertEquals(0, Ws1Ops.groupRefCount(s, "L"))
    }

    @Test fun skillGroupRefCountSumsCons41sAnd42sOnly() {
        val s = state()
        assertEquals(2, Ws1Ops.skillGroupRefCount(s, "L")) // cons41s(1) + cons42s g1(1)
        assertEquals(1, Ws1Ops.skillGroupRefCount(s, "N")) // cons42s g2(1)
        // 勤務グループ "A" はスキル群の参照カウントには入らない
        assertEquals(0, Ws1Ops.skillGroupRefCount(s, "A"))
    }

    @Test fun exactMatchDoesNotTrim() {
        // shiftIdxOf は完全一致(==)なので trim しない。前後空白の別記号とは一致させない。
        val s = state()
        assertEquals(0, Ws1Ops.shiftRefCount(s, " 日"))
        assertEquals(0, Ws1Ops.shiftRefCount(s, "日 "))
    }
}
