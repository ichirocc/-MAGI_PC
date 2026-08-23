package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Test

/**
 * [3.428.0] 100件レビューの未確認項目を再検証した結果、実在した項目の回帰。
 *
 * ここで固定するのは **#30**（`hf66DataHardening` の埋めシフト規則が `fillShiftIndex` の写しだった）。
 * 他の3件（#14 消し残りの記録・#43 前景化失敗のログ・#7 原子置換の断念）は `MagiViewModel` /
 * `OptimizationWorker` にあり Android 依存でホスト実行できない（#7 の callback 経路だけは
 * `RunFilesTest` が固定している）。
 */
class ReviewFixes3428Test {

    /** 休が index0 でない＝旧実装との差が観測できる最小フィクスチャ。 */
    private fun stRestNotFirst() = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-02",
        shifts = listOf(Shift("X", "X", "", ""), Shift("休", "休", "", "")),
        groups = listOf(Group("G0", "G0")),
        staff = listOf(Staff("s0", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(listOf(99, 0)),   // 1セル目が範囲外＝埋め直しの対象
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    /**
     * #30: 担当外・範囲外セルを埋めるシフトは `fillShiftIndex(allowed, rest)` が決める。
     *
     * 旧実装は `allowed.firstOrNull() ?: 0`＝**index 最小**を選んでいたため、休が先頭でないデータでは
     * 勤務シフトへ倒れていた（3.106.0 が `Ws1Ops.removeShift` で、3.410.0/P-01 が `initialAssignment`
     * で直したのと同じ取り違えが、この HARD 修復パスにだけ残っていた）。3.419.0 で規則を1箇所へ
     * 集約したときの取り残し。
     */
    @Test
    fun hf66FillsUnauthorizedCellsWithRestNotWithTheLowestIndex() {
        val st = stRestNotFirst()
        val p = Problem(st)
        assertEquals("休が index0 でないフィクスチャであること", 1, p.restIdx)
        assertEquals("両シフトとも担当可＝旧実装なら先頭(X)を選ぶ局面", listOf(0, 1), p.allowedShiftsForStaff(0).toList())

        // **`initialAssignment()` を通さない**: あれは 3.410.0/P-01 で範囲外セルを既に休へ写すので、
        //   ここへ渡すと fallback に一度も到達せず、テストが何も検証しなくなる（最初にそう書いて
        //   「戻しても落ちない」ことで気づいた＝教訓#30）。生の範囲外セルを直接渡す。
        val raw = arrayOf(intArrayOf(99, 0))
        val out = V6NativeOptimizer.hf66DataHardening(st, raw, "test")
        assertEquals("埋めシフトは休（旧実装は勤務シフト X へ倒れていた）", p.restIdx, out[0][0])
        assertEquals("担当可の既存セルは触らない", 0, out[0][1])
    }

    /** 規則そのもの：休が担当可なら休、無ければ先頭 allowed、どちらも無ければ休へ倒す（例外を投げない）。 */
    @Test
    fun fillShiftIndexPrefersRestThenFirstAllowedAndNeverThrows() {
        assertEquals("休が担当可なら休", 1, fillShiftIndex(intArrayOf(0, 1, 2), rest = 1))
        assertEquals("休が担当外なら先頭 allowed", 2, fillShiftIndex(intArrayOf(2, 3), rest = 1))
        assertEquals("担当可能が空でも落ちず休へ倒す", 1, fillShiftIndex(intArrayOf(), rest = 1))
    }
}
