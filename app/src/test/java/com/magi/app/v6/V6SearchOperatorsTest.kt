package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test
import java.util.Random

/**
 * [/code-review, need2単独定義セル見落とし修正] `findCovOFix` の検証。
 * 旧実装は `p.need1[k][j]` のみで過剰スキャンを行い、need1未設定・need2のみで上限が定義された
 * シフトの過剰配置を一切検出できなかった（3.173.0のCoverageDiagnosis修正・3.309.0の
 * V6LateOperators.isBalanceable修正と同根、covOCell/covUCellをsource of truthとして統一）。
 */
class V6SearchOperatorsTest {
    private fun state(): MagiState = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-01",
        // shift 0="休"(needなし), shift 1="X"(need1未設定・need2=1のみで上限定義)
        shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", "1")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("s0", 0), Staff("s1", 0)),
        use2Patterns = true,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        // 両名ともXへ配置済み＝need2=1に対し2人在勤で過剰配置(covO=1)。
        schedule = listOf(listOf(1), listOf(1)),
        wishes = emptyMap(),
        staffRange = emptyMap(),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
        cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
        cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test
    fun findCovOFixDetectsNeed2OnlyOverCoverage() {
        val p = Problem(state())
        val eval = DeltaEvaluator(p)
        assertEquals("前提: X(day0)が2人で過剰配置(need2=1)", 1, p.covOCell(1, 0, eval.countOnDay(1, 0)))

        val fix = findCovOFix(p, eval, Random(1))
        assertNotNull("need2のみで定義された過剰配置も検出し修正候補を返すこと", fix)
        val (i, j, newK) = fix!!
        assertEquals(0, j)
        assertEquals(1, eval.at(i, j)) // 元は在勤中のX(=1)を退避させる手であること
        assertEquals(0, newK) // 唯一の移動先候補=休(index0)
    }

    /**
     * 過剰配置が無ければ null（誤検知しないことの対照）。
     */
    @Test
    fun findCovOFixReturnsNullWhenNoOverCoverage() {
        val st = state().copy(schedule = listOf(listOf(1), listOf(0))) // 1人だけXへ＝need2=1を満たすのみ
        val p = Problem(st)
        val eval = DeltaEvaluator(p)
        assertEquals(0, p.covOCell(1, 0, eval.countOnDay(1, 0)))
        assertNull(findCovOFix(p, eval, Random(1)))
    }
}
