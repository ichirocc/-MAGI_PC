package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.C2Row
import com.magi.app.model.C3Row
import com.magi.app.model.C41Row
import com.magi.app.model.C42Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test

/**
 * [3.330.0] 指紋は2つの安全機構（診断の鮮度・背景結果の照合）の土台なので、**入力の族を1つでも
 * 書き忘れると黙って効かなくなる**。族ごとに「変えたら指紋も変わる」ことを固定する。
 *
 * 3.327.0 は `staffRange` と `cons1` しか見ておらず、希望や担当可否を変えても古い診断が残った。
 * その再発をここで止める。
 */
class StateFingerprintTest {
    private fun base() = MagiState(
        startDate = "2026-08-01", endDate = "2026-08-03",
        shifts = listOf(Shift("休", "休", "0", "1"), Shift("A", "A", "1", "2")),
        groups = listOf(Group("G", "G"), Group("H", "H")),
        staff = listOf(Staff("s0", 0, 0), Staff("s1", 1, 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1), listOf(1, 0)),
        groupShiftApt = listOf(listOf("", "2"), listOf("", "")),
        schedule = listOf(listOf(0, 1, 0), listOf(1, 0, 1)),
        wishes = mapOf("0,0" to 1),
        staffRange = mapOf("0,1" to Range("1", "2")),
        needDay1 = mapOf("1,0" to "2"),
        needDay2 = mapOf("1,0" to "3"),
        cons1 = listOf(C1Row("3", "休", "1")),
        cons2 = listOf(C2Row("A", "5")),
        cons3 = listOf(C3Row(listOf("A", "A"))),
        cons3n = listOf(C3Row(listOf("A", "休"))),
        cons3m = listOf(C3Row(listOf("休", "A"))),
        cons3mn = listOf(C3Row(listOf("休", "休"))),
        cons41 = listOf(C41Row("G", "A", "0", "1")),
        cons42 = listOf(C42Row("G", "H", "A", "休")),
        skillGroups = listOf(Group("S", "S")),
        cons41s = listOf(C41Row("S", "A", "0", "1")),
        cons42s = listOf(C42Row("S", "S", "A", "休")),
    )

    /** 各族を1つだけ変えた state を返す。名前は失敗時にどの族かが分かるようにする。 */
    private fun variants(b: MagiState): List<Pair<String, MagiState>> = listOf(
        "startDate" to b.copy(startDate = "2026-09-01"),
        "endDate" to b.copy(endDate = "2026-08-04"),
        "use2Patterns" to b.copy(use2Patterns = true),
        "シフト名" to b.copy(shifts = b.shifts.mapIndexed { i, s -> if (i == 1) s.copy(name = "A2") else s }),
        "シフト記号" to b.copy(shifts = b.shifts.mapIndexed { i, s -> if (i == 1) s.copy(kigou = "B") else s }),
        "必要人数(既定)" to b.copy(shifts = b.shifts.mapIndexed { i, s -> if (i == 1) s.copy(need1 = "2") else s }),
        "上限人数(既定)" to b.copy(shifts = b.shifts.mapIndexed { i, s -> if (i == 1) s.copy(need2 = "9") else s }),
        "群" to b.copy(groups = b.groups + Group("I", "I")),
        "スキル群" to b.copy(skillGroups = b.skillGroups + Group("T", "T")),
        "職員名" to b.copy(staff = b.staff.mapIndexed { i, p -> if (i == 0) p.copy(name = "x") else p }),
        "職員の所属群" to b.copy(staff = b.staff.mapIndexed { i, p -> if (i == 0) p.copy(groupIdx = 1) else p }),
        "職員のスキル群" to b.copy(staff = b.staff.mapIndexed { i, p -> if (i == 0) p.copy(skillIdx = -1) else p }),
        "担当可否" to b.copy(groupShift = listOf(listOf(1, 0), listOf(1, 0))),
        "適切回数" to b.copy(groupShiftApt = listOf(listOf("", "3"), listOf("", ""))),
        "希望" to b.copy(wishes = mapOf("0,0" to 0)),
        "希望(件数)" to b.copy(wishes = b.wishes + ("1,1" to 1)),
        "個人の回数" to b.copy(staffRange = mapOf("0,1" to Range("1", "3"))),
        "日別の最低人数" to b.copy(needDay1 = mapOf("1,0" to "1")),
        "日別の上限人数" to b.copy(needDay2 = mapOf("1,0" to "4")),
        "窓の要件" to b.copy(cons1 = listOf(C1Row("4", "休", "1"))),
        "個人の合計" to b.copy(cons2 = listOf(C2Row("A", "6"))),
        "必須の並び" to b.copy(cons3 = listOf(C3Row(listOf("A", "休")))),
        "禁止の並び" to b.copy(cons3n = listOf(C3Row(listOf("A", "A")))),
        "推奨の並び" to b.copy(cons3m = listOf(C3Row(listOf("A", "A")))),
        "回避の並び" to b.copy(cons3mn = listOf(C3Row(listOf("A", "A")))),
        "群のレンジ" to b.copy(cons41 = listOf(C41Row("G", "A", "1", "1"))),
        "スキル群のレンジ" to b.copy(cons41s = listOf(C41Row("S", "A", "1", "1"))),
        "群ペア禁止" to b.copy(cons42 = listOf(C42Row("G", "H", "休", "A"))),
        "スキル群ペア禁止" to b.copy(cons42s = listOf(C42Row("S", "S", "休", "A"))),
    )

    @Test fun everyInputFamilyChangesTheFingerprint() {
        val b = base()
        val h0 = StateFingerprint.of(b)
        for ((label, v) in variants(b)) {
            assertNotEquals("「$label」を変えたのに指紋が変わらない（この族の変更を診断・結果照合が見逃す）",
                h0, StateFingerprint.of(v))
        }
    }

    @Test fun everyVariantIsDistinctFromEachOther() {
        // 族どうしの衝突も見る（別の族を変えたのに同じ値になると、片方の変更が実質無視される）。
        val b = base()
        val seen = HashMap<Long, String>()
        seen[StateFingerprint.of(b)] = "変更なし"
        for ((label, v) in variants(b)) {
            val h = StateFingerprint.of(v)
            val prev = seen.put(h, label)
            assertEquals("「$label」と「$prev」の指紋が衝突している", null, prev)
        }
    }

    @Test fun scheduleIsDeliberatelyExcluded() {
        // 盤面は別（boardKey）で見るので、ここに混ぜると結果の適用で必ず不一致になり使えなくなる。
        val b = base()
        val moved = b.copy(schedule = listOf(listOf(1, 0, 1), listOf(0, 1, 0)))
        assertEquals("盤面だけの違いは指紋を変えない", StateFingerprint.of(b), StateFingerprint.of(moved))
    }

    @Test fun sameStateGivesSameFingerprint() {
        assertEquals(StateFingerprint.of(base()), StateFingerprint.of(base()))
    }
}
