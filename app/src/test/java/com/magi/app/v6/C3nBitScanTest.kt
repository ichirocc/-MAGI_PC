package com.magi.app.v6

import com.magi.app.model.C3Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Test
import kotlin.random.Random

/**
 * [c3n ビット走査のパリティ] `C3nBitScan` は既存のスカラー実装（`Problem.makesForbiddenRun` /
 * `C1DeltaPrefilter.staffC3nFires`）を置き換えず、候補が爆発する新経路だけで使う高速版。
 * 両者が食い違うと「禁止連続を作る手を通してしまう」ため、ランダム盤面で全単一セル変更を突き合わせる
 * （3.172.0/3.174.0 で C++ 側のビット化に課したのと同じ規律を Kotlin 側にも課す）。
 */
class C3nBitScanTest {

    // shift index: 0=休 1=D 2=A 3=B 4=C
    private val kigou = listOf("休", "D", "A", "B", "C")
    private val rest = 0
    private val dShift = 1
    private val aShift = 2

    /** 実データの形（Dﾃ→X の2連が主・Dﾃ→休→X の3連が混じる）を模した合成問題。 */
    private fun state(days: Int, schedule: List<List<Int>>): MagiState = MagiState(
        startDate = "2026-12-01",
        endDate = "",
        shifts = kigou.map { Shift(it, it, "", "") },
        groups = listOf(Group("G", "G")),
        staff = List(schedule.size) { Staff("s$it", 0) },
        use2Patterns = false,
        groupShift = listOf(List(kigou.size) { 1 }),
        groupShiftApt = listOf(List(kigou.size) { "" }),
        schedule = schedule,
        wishes = emptyMap(),
        staffRange = emptyMap(),
        needDay1 = emptyMap(),
        needDay2 = emptyMap(),
        cons1 = emptyList(),
        cons2 = emptyList(),
        cons3 = emptyList(),
        // 2連・3連を混在させる（3連が入るとパターン先頭が j-2 に来る局面が生まれる）。
        cons3n = listOf(
            C3Row(listOf("D", "A")),
            C3Row(listOf("D", "B")),
            C3Row(listOf("C", "A")),
            C3Row(listOf("D", "休", "A")),
            C3Row(listOf("D", "休", "B")),
        ),
        cons3m = emptyList(),
        cons3mn = emptyList(),
        cons41 = emptyList(),
        cons42 = emptyList(),
    ).also { require(it.dayCount == days) }

    private fun randomState(days: Int, seed: Int): MagiState {
        val rng = Random(seed)
        return state(days, List(2) { List(days) { rng.nextInt(kigou.size) } })
    }

    @Test
    fun bitScanMatchesScalarOracleForEverySingleCellChange() {
        var checkedFires = 0
        var checkedCells = 0
        for (seed in 1..12) {
            val st = randomState(days = 21, seed = seed)
            val p = Problem(st)
            val sched = st.schedule.toIntArray2D()
            for (i in 0 until p.S) {
                val row = sched[i]
                val mask = C3nBitScan.buildRowMask(p, row)
                assertEquals(
                    "fires が一致 (seed=$seed staff=$i)",
                    C1DeltaPrefilter.staffC3nFires(p, row), C3nBitScan.fires(p, mask),
                )
                checkedFires++
                for (j in 0 until p.T) {
                    val old = row[j]
                    for (k in 0 until p.K) {
                        // makesForbiddenRun は「日 j をまたぐ窓での成立」を判定する。
                        assertEquals(
                            "hitsAfterSet が一致 (seed=$seed staff=$i day=$j k=$k)",
                            p.makesForbiddenRun(sched, i, j, k),
                            C3nBitScan.hitsAfterSet(p, mask, j, old, k),
                        )
                        // firesAfterSet は行全体の fire 数。スカラーは行を実際に書き換えて数える。
                        row[j] = k
                        val expectFires = C1DeltaPrefilter.staffC3nFires(p, row)
                        row[j] = old
                        assertEquals(
                            "firesAfterSet が一致 (seed=$seed staff=$i day=$j k=$k)",
                            expectFires, C3nBitScan.firesAfterSet(p, mask, j, old, k),
                        )
                        checkedCells++
                    }
                    assertEquals("mask は判定で不変", C3nBitScan.buildRowMask(p, row).toList(), mask.toList())
                }
            }
        }
        // 実際に十分な件数を突き合わせたことを固定（フィクスチャ退化でテストが空回りするのを防ぐ）。
        assertEquals(24, checkedFires)
        assertEquals(24 * 21 * 5, checkedCells)
    }

    /**
     * [3連への到達] 旧実装が届かなかった「パターン先頭が j-2」の局面で、崩す候補日として j-2 まで
     * 返ること。これが範囲拡張(3.303.0)の前提。
     */
    @Test
    fun coveringRunDaysReturnsWholePatternIncludingItsHead() {
        val days = 10
        // D(0日) 休(1日) A(2日) → cons3n "D→休→A" が窓[0..2]で成立。以降は休で埋め他ルールを不発に。
        val row = MutableList(days) { rest }
        row[0] = dShift; row[1] = rest; row[2] = aShift
        val st = state(days, listOf(row))
        val p = Problem(st)
        val mask = C3nBitScan.buildRowMask(p, st.schedule.toIntArray2D()[0])

        val fired = C3nBitScan.fires(p, mask)
        assertEquals("3連がちょうど1件成立している前提", 1, fired)

        val covering = C3nBitScan.coveringRunDays(p, mask, 2)
        assertEquals("パターン全域(0,1,2)が候補日として返る", C3nBitScan.rangeMask(0, 2), covering)
        // 旧実装の視野(j±1 = 1,3)ではパターン先頭 day0 に届かないことを対比で固定。
        assertEquals("先頭 day0 が含まれる", 1L, covering and 1L)
    }
}
