package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.442.0] ドッグフーディング検証で実在を確認した項目の回帰。
 *
 * ここで固定できるのは **H3**（職員一覧CSVで追加した職員の空き日が、その群の担当可否を無視して
 * 休で埋まり行まるごと groupViol になっていた）だけ。他の3件は層が違う:
 *  - C1（停止パスで `releasedByMe` を立てず `OptimizationRepository.running` が固着）は
 *    `OptimizationWorker` のライフサイクル＝Robolectric か instrumented test が要る（3.386.0 の既知のギャップ）。
 *  - H2（`applyWishes`/`applyAlternative` の実行中ガード）は `MagiViewModel`＝Android 依存。
 *  - M2 はコメントのみ。
 */
class ReviewFixes3442Test {

    /** 群 G0 は **X しか担当できない**（休は担当外）。休は index1＝旧実装との差が観測できる。 */
    private fun stRestNotAllowed() = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-03",
        shifts = listOf(Shift("X", "X", "", ""), Shift("休", "休", "", "")),
        groups = listOf(Group("G0", "G0")),
        staff = listOf(Staff("s0", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 0)),          // X=可 / 休=不可
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(listOf(0, 0, 0)),
        wishes = emptyMap(), staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(), cons3n = emptyList(),
        cons3m = emptyList(), cons3mn = emptyList(), cons41 = emptyList(), cons42 = emptyList(),
    )

    /**
     * H3: CSV で追加した職員の空き日は「その群が担当できるシフト」で埋める。
     *
     * 旧実装は `IntArray(t) { restShiftIndex(state) }`＝**休の記号解決だけ**で担当可否を見ておらず、
     * 休を担当可否から外した群（UI の担当可否チップで実際にできる操作）へ CSV で職員を足すと
     * 追加行の全日が groupViol(HARD 10000) になっていた。31日なら1回の取込で必須違反31件。
     * 3.418.0 が `Ws1Ops.addStaff`/`resizeDays`/`removeShift` で直した穴の、CSV 側の取り残し。
     */
    @Test
    fun csvUpsertFillsNewStaffRowWithAnAllowedShift() {
        val st = stRestNotAllowed()
        val sched = arrayOf(intArrayOf(0, 0, 0))
        val r = StaffCsvIO.parseUpsert("氏名,グループ\n新人,G0\n", st, sched)!!
        assertEquals("1名が追加される", 1, r.added)

        val row = r.schedule[1]
        assertEquals("期間ぶんの行ができる", 3, row.size)
        val rest = restShiftIndex(st)
        assertEquals("休は index1（旧実装との差が観測できる構成）", 1, rest)
        for (j in row.indices) {
            assertEquals("空き日は担当できる X で埋まる（旧実装は休＝担当外だった）", 0, row[j])
        }

        // 実際に必須違反が出ないことまで見る（この修正の目的そのもの）。
        val rep = UnifiedViolationChecker.check(r.state, r.schedule)
        assertEquals("追加行が担当外シフトで埋まっていない", 0, rep.breakdown["groupViol"] ?: 0)
    }

    /** 休を担当できる群では従来どおり休で埋まる（3.418.0 の意味論を後退させない）。 */
    @Test
    fun csvUpsertStillPrefersRestWhenTheGroupCanTakeIt() {
        val st = stRestNotAllowed().copy(groupShift = listOf(listOf(1, 1)))
        val sched = arrayOf(intArrayOf(0, 0, 0))
        val r = StaffCsvIO.parseUpsert("氏名,グループ\n新人,G0\n", st, sched)!!
        val row = r.schedule[1]
        assertTrue("全日が休", row.all { it == restShiftIndex(st) })
    }
}
