package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import java.time.LocalDate

/**
 * ws1 (初期設定) model operations: edit the problem definition — shifts, groups, staff,
 * the period (days), group×shift buckets, and the use2 flag. Operations that change a
 * dimension (S/T/K) re-dimension every index-keyed structure consistently so the result
 * stays scorable. Re-dimensioning semantics validated against the Level Zero data model
 * (see docs) and a numeric prototype (state stays consistent; fullEval remains computable).
 *
 * Each op takes the current (state, working schedule) and returns a new pair; [newSchedule]
 * always matches the returned state's dimensions. Remove operations (which require shift/
 * staff re-indexing) are intentionally deferred to a later increment.
 */
data class Ws1Result(val state: MagiState, val schedule: Array<IntArray>)

object Ws1Ops {

    private fun withSchedule(state: MagiState, sched: Array<IntArray>): MagiState =
        state.copy(schedule = sched.map { it.toList() })

    private fun copyGrid(sched: Array<IntArray>): Array<IntArray> = Array(sched.size) { sched[it].copyOf() }

    // ---- no dimension change -------------------------------------------------

    fun editShift(state: MagiState, k: Int, name: String, kigou: String, need1: String, need2: String): MagiState {
        if (k !in state.shifts.indices) return state
        val old = state.shifts[k].kigou
        val s = state.shifts.toMutableList()
        s[k] = Shift(name, kigou, need1, need2)
        // [記号変更の伝播] 制約はシフト記号(文字列)で参照するため、記号を変えたら参照行も一括置換し
        //   旧記号の幽霊行化(評価では無視されるが表示に残る)を防ぐ。index保存(staffRange/希望/apt/勤務表)は
        //   indexで参照するため自動追従＝対象外。
        return renameShiftInConstraints(state.copy(shifts = s), old, kigou)
    }

    fun editGroup(state: MagiState, g: Int, name: String, kigou: String): MagiState {
        if (g !in state.groups.indices) return state
        val old = state.groups[g].kigou
        val gl = state.groups.toMutableList()
        gl[g] = Group(name, kigou)
        // [記号変更の伝播] cons41/cons42 は群記号で参照。cons41s/cons42s(スキル群)は別系統で対象外。
        return renameGroupInConstraints(state.copy(groups = gl), old, kigou)
    }

    // [記号変更の伝播] 制約は記号(kigou)文字列で参照するため、シフト/群/スキル群の記号を変えたら
    //   参照する制約行も一括置換し、旧記号の幽霊行化を防ぐ。old空 or old==new は no-op。
    private fun renameShiftInConstraints(s: MagiState, old: String, new: String): MagiState {
        if (old.isBlank() || old == new) return s
        fun pat(p: List<String>) = p.map { if (it == old) new else it }
        return s.copy(
            cons1 = s.cons1.map { if (it.shiftKigou == old) it.copy(shiftKigou = new) else it },
            cons2 = s.cons2.map { if (it.shiftKigou == old) it.copy(shiftKigou = new) else it },
            cons3 = s.cons3.map { it.copy(pattern = pat(it.pattern)) },
            cons3n = s.cons3n.map { it.copy(pattern = pat(it.pattern)) },
            cons3m = s.cons3m.map { it.copy(pattern = pat(it.pattern)) },
            cons3mn = s.cons3mn.map { it.copy(pattern = pat(it.pattern)) },
            cons41 = s.cons41.map { if (it.shiftKigou == old) it.copy(shiftKigou = new) else it },
            cons41s = s.cons41s.map { if (it.shiftKigou == old) it.copy(shiftKigou = new) else it },
            cons42 = s.cons42.map { it.copy(s1Kigou = if (it.s1Kigou == old) new else it.s1Kigou, s2Kigou = if (it.s2Kigou == old) new else it.s2Kigou) },
            cons42s = s.cons42s.map { it.copy(s1Kigou = if (it.s1Kigou == old) new else it.s1Kigou, s2Kigou = if (it.s2Kigou == old) new else it.s2Kigou) },
        )
    }

    private fun renameGroupInConstraints(s: MagiState, old: String, new: String): MagiState {
        if (old.isBlank() || old == new) return s
        return s.copy(
            cons41 = s.cons41.map { if (it.groupKigou == old) it.copy(groupKigou = new) else it },
            cons42 = s.cons42.map { it.copy(g1Kigou = if (it.g1Kigou == old) new else it.g1Kigou, g2Kigou = if (it.g2Kigou == old) new else it.g2Kigou) },
        )
    }

    fun renameSkillGroupInConstraints(s: MagiState, old: String, new: String): MagiState {
        if (old.isBlank() || old == new) return s
        return s.copy(
            cons41s = s.cons41s.map { if (it.groupKigou == old) it.copy(groupKigou = new) else it },
            cons42s = s.cons42s.map { it.copy(g1Kigou = if (it.g1Kigou == old) new else it.g1Kigou, g2Kigou = if (it.g2Kigou == old) new else it.g2Kigou) },
        )
    }

    fun editStaff(state: MagiState, i: Int, name: String, groupIdx: Int): MagiState {
        if (i !in state.staff.indices) return state
        val gi = groupIdx.coerceIn(0, (state.groups.size - 1).coerceAtLeast(0))
        val sl = state.staff.toMutableList()
        // [P1修正/レビュー指摘] 旧 Staff(name, gi) は skillIdx を既定0へ戻し、名前だけ直しても
        //   スキル区分が無言で消えて cons41s/cons42s の評価が変わっていた。既存のコピーで保持する。
        sl[i] = sl[i].copy(name = name, groupIdx = gi)
        return state.copy(staff = sl)
    }

    fun setGroupShift(state: MagiState, g: Int, k: Int, allowed: Boolean): MagiState {
        if (g !in state.groupShift.indices) return state
        val grid = state.groupShift.map { it.toMutableList() }.toMutableList()
        if (k !in grid[g].indices) return state
        grid[g][k] = if (allowed) 1 else 0
        return state.copy(groupShift = grid)
    }

    /**
     * グループ別シフトの「適切回数 (groupShiftApt)」を1セル設定。Web版の
     * 「グループ別 担当シフトと適切回数」エディタ相当。1人あたりの期間内目標回数（空欄＝目標なし）。
     * groupShiftApt が未初期化/不揃いでも G×K に正規化してから設定する。
     */
    fun setGroupApt(state: MagiState, g: Int, k: Int, value: String): MagiState {
        if (g !in state.groups.indices) return state
        val kCount = state.shifts.size
        if (k !in 0 until kCount) return state
        val grid = MutableList(state.groups.size) { gi ->
            val row = state.groupShiftApt.getOrNull(gi) ?: emptyList()
            MutableList(kCount) { kk -> row.getOrNull(kk) ?: "" }
        }
        grid[g][k] = value.trim()
        return state.copy(groupShiftApt = grid)
    }

    /**
     * [apt強制リセット] グループ別シフトの適切回数(groupShiftApt)を全て空欄(=目標なし)に戻す。
     * G×K に正規化したうえで全セルを "" にする。apt 由来のソフト違反は消えるが、
     * 担当ON/OFF(groupShift)・回数レンジ・勤務表・シフト/グループ定義は一切変更しない。
     */
    fun resetGroupApt(state: MagiState): MagiState {
        val grid = List(state.groups.size) { List(state.shifts.size) { "" } }
        return state.copy(groupShiftApt = grid)
    }

    fun setUse2(state: MagiState, on: Boolean): MagiState = state.copy(use2Patterns = on)

    // ---- append (low-risk dimension change, no re-indexing) ------------------

    /** Add a shift (index K). Existing schedule/wishes indices stay valid; groupShift/apt gain a column. */
    fun addShift(state: MagiState, name: String, kigou: String, need1: String, need2: String): MagiState {
        val shifts = state.shifts + Shift(name, kigou, need1, need2)
        val gs = state.groupShift.map { it + 0 }                 // new shift not allowed by default
        val apt = if (state.groupShiftApt.isEmpty()) state.groupShiftApt
        else state.groupShiftApt.map { it + "" }
        return state.copy(shifts = shifts, groupShift = gs, groupShiftApt = apt)
    }

    /** Add a group (index G). groupShift/apt gain a row; staff group indices stay valid.
     *  [review #5] The new group is allowed the 休 (rest) shift so it passes validation
     *  (every group needs >=1 doable shift); otherwise add -> save -> reload would be rejected. */
    fun addGroup(state: MagiState, name: String, kigou: String): MagiState {
        val k = state.shifts.size
        val rest = restShiftIndex(state)
        val groups = state.groups + Group(name, kigou)
        val gs = state.groupShift + listOf(List(k) { idx -> if (idx == rest) 1 else 0 })
        val apt = if (state.groupShiftApt.isEmpty()) state.groupShiftApt
        else state.groupShiftApt + listOf(List(k) { "" })
        return state.copy(groups = groups, groupShift = gs, groupShiftApt = apt)
    }

    /** Add a staff (index S). The working schedule gains a row of 休.
     *  [3.329.0/外部レビュー H-01] 旧コメントは「休/idx0」と両者を同一視していたが、**休は記号で解決する**
     *  （`restShiftIndex`）。休が先頭でないデータでは、新しい職員の空き日が丸ごと勤務シフトになっていた。 */
    fun addStaff(state: MagiState, sched: Array<IntArray>, name: String, groupIdx: Int): Ws1Result {
        val t = if (sched.isNotEmpty()) sched[0].size else state.dayCount
        val gi = groupIdx.coerceIn(0, (state.groups.size - 1).coerceAtLeast(0))
        val staff = state.staff + Staff(name, gi)
        val rest = restShiftIndex(state)
        val newSched = copyGrid(sched) + IntArray(t) { rest }
        val ns = state.copy(staff = staff)
        return Ws1Result(withSchedule(ns, newSched), newSched)
    }

    // ---- period resize -------------------------------------------------------

    /** Resize the period to [newT] days: schedule columns padded with 休 or truncated;
     *  out-of-range needDay/wishes dropped; endDate recomputed from startDate.
     *  [3.329.0/外部レビュー H-01] 追加された日も休の**記号解決**で埋める（旧: index 0 直書き）。 */
    fun resizeDays(state: MagiState, sched: Array<IntArray>, newT: Int): Ws1Result {
        val t = newT.coerceIn(1, 31)
        val rest = restShiftIndex(state)
        val newSched = Array(sched.size) { i ->
            IntArray(t) { j -> if (j < sched[i].size) sched[i][j] else rest }
        }
        fun dayOf(key: String) = key.split(",").getOrNull(1)?.toIntOrNull() ?: -1
        val need1 = state.needDay1.filterKeys { dayOf(it) in 0 until t }
        val need2 = state.needDay2.filterKeys { dayOf(it) in 0 until t }
        val wishes = state.wishes.filterKeys { dayOf(it) in 0 until t }
        val end = runCatching { LocalDate.parse(state.startDate).plusDays((t - 1).toLong()).toString() }
            .getOrDefault(state.endDate)
        val ns = state.copy(needDay1 = need1, needDay2 = need2, wishes = wishes, endDate = end)
        return Ws1Result(withSchedule(ns, newSched), newSched)
    }

    // ---- remove (re-indexing; verified against a numeric prototype) ----------

    /** Remap a "a,b"-keyed map after removing index [removed] from axis 0 (a) or axis 1 (b):
     *  drop keys whose axis index == removed, decrement those greater. */
    private fun <V> reindexKeys(m: Map<String, V>, axis: Int, removed: Int): Map<String, V> {
        val out = LinkedHashMap<String, V>()
        for ((key, v) in m) {
            val parts = key.split(",")
            val a = parts.getOrNull(0)?.toIntOrNull() ?: continue
            val b = parts.getOrNull(1)?.toIntOrNull() ?: continue
            var idx = if (axis == 0) a else b
            if (idx == removed) continue
            if (idx > removed) idx -= 1
            out[if (axis == 0) "$idx,$b" else "$a,$idx"] = v
        }
        return out
    }

    fun canRemoveGroup(state: MagiState, g: Int): Boolean =
        state.groups.size > 1 && state.staff.none { it.groupIdx == g }

    /** Remove shift [k]: drop the shift; schedule cells ==k -> 休（削除後のindexへ追従）, >k decremented;
     *  wish values ==k dropped, >k decremented; groupShift/apt lose column k; needDay (axis k) and
     *  staffRange (axis k) re-indexed. Constraints referencing the removed kigou simply stop
     *  resolving (kept verbatim). No-op if only one shift remains, or if [k] is the 休 shift itself
     *  （削除すると次のシフトが index0 となり全休日が無言で勤務へ変わるため禁止＝P1修正/レビュー指摘）. */
    fun removeShift(state: MagiState, sched: Array<IntArray>, k: Int): Ws1Result {
        if (k !in state.shifts.indices || state.shifts.size <= 1) return Ws1Result(state, sched)
        // [P1修正] 休シフト自体の削除は禁止（no-op）。呼出側(ViewModel)がメッセージを出す。
        val rest = restShiftIndex(state)
        if (k == rest) return Ws1Result(state, sched)
        // [P1修正] 削除シフトのセルは「休」へ。旧実装はハードコードの 0 で、休が index0 でないデータや
        //   休より前のシフトを消した場合に勤務シフトへ化けていた。削除後の休indexへ正しく追従させる。
        val newRest = if (rest > k) rest - 1 else rest
        val shifts = state.shifts.filterIndexed { i, _ -> i != k }
        val gs = state.groupShift.map { row -> row.filterIndexed { i, _ -> i != k } }
        val apt = if (state.groupShiftApt.isEmpty()) state.groupShiftApt
        else state.groupShiftApt.map { row -> row.filterIndexed { i, _ -> i != k } }
        val newSched = Array(sched.size) { r ->
            IntArray(sched[r].size) { j -> val v = sched[r][j]; if (v == k) newRest else if (v > k) v - 1 else v }
        }
        val wishes = LinkedHashMap<String, Int>()
        for ((key, v) in state.wishes) { if (v == k) continue; wishes[key] = if (v > k) v - 1 else v }
        val ns = state.copy(
            shifts = shifts, groupShift = gs, groupShiftApt = apt, wishes = wishes,
            needDay1 = reindexKeys(state.needDay1, 0, k),
            needDay2 = reindexKeys(state.needDay2, 0, k),
            staffRange = reindexKeys(state.staffRange, 1, k),
        )
        return Ws1Result(withSchedule(ns, newSched), newSched)
    }

    /** Remove staff [i]: drop the staff and its schedule row; wishes/staffRange (axis i)
     *  re-indexed. No-op if only one staff remains. */
    fun removeStaff(state: MagiState, sched: Array<IntArray>, i: Int): Ws1Result {
        if (i !in state.staff.indices || state.staff.size <= 1) return Ws1Result(state, sched)
        val staff = state.staff.filterIndexed { idx, _ -> idx != i }
        val newSched = ArrayList<IntArray>(sched.size - 1)
        for (r in sched.indices) if (r != i) newSched.add(sched[r].copyOf())
        val arr = newSched.toTypedArray()
        val ns = state.copy(
            staff = staff,
            wishes = reindexKeys(state.wishes, 0, i),
            staffRange = reindexKeys(state.staffRange, 0, i),
        )
        return Ws1Result(withSchedule(ns, arr), arr)
    }

    /** Remove group [g]: allowed whenever 2+ groups exist. groupShift/apt lose the row; staff in
     *  the removed group are reassigned to the first remaining group (new index 0); staff group
     *  indices > g are decremented (skillIdx preserved). Constraints referencing the removed
     *  group kigou simply stop resolving. */
    fun removeGroup(state: MagiState, g: Int): MagiState {
        if (g !in state.groups.indices || state.groups.size <= 1) return state
        val groups = state.groups.filterIndexed { idx, _ -> idx != g }
        val gs = state.groupShift.filterIndexed { idx, _ -> idx != g }
        val apt = if (state.groupShiftApt.isEmpty()) state.groupShiftApt
        else state.groupShiftApt.filterIndexed { idx, _ -> idx != g }
        val staff = state.staff.map { s ->
            val ni = when {
                s.groupIdx == g -> 0          // 所属者は先頭グループへ移動
                s.groupIdx > g -> s.groupIdx - 1
                else -> s.groupIdx
            }
            if (ni == s.groupIdx) s else Staff(s.name, ni, s.skillIdx)
        }
        return state.copy(groups = groups, groupShift = gs, groupShiftApt = apt, staff = staff)
    }

    /**
     * [3.330.0/外部レビュー] スキル群 [g] を削除する。担当グループの [removeGroup] と対になる操作で、
     * これまで ViewModel 側に手書きされていた（Android 依存＝テストできなかった）ので同じ置き場へ移す。
     *
     * **所属者は `-1`（未所属）へ**。3.328.0 まで `0`（先頭の群）へ寄せており、
     *  - 無関係な先頭の群の制約が黙って掛かる
     *  - 最後の1群を消すと全員 0 になり、あとで群を1つ足すと全員がそこに所属した扱いになる
     * という2つの取り違えを起こしていた（3.70.0 が「(なし)=-1」を正規の値として用意済み）。
     * 後ろの群は1つ詰める。残った `cons41s`/`cons42s` の参照外れは `Problem` 解決時に無視される。
     */
    fun removeSkillGroup(state: MagiState, g: Int): MagiState {
        if (g !in state.skillGroups.indices) return state
        val skillGroups = state.skillGroups.filterIndexed { idx, _ -> idx != g }
        val staff = state.staff.map { s ->
            val ni = when {
                s.skillIdx == g -> -1          // 所属していた群が無くなった＝未所属
                s.skillIdx > g -> s.skillIdx - 1
                else -> s.skillIdx
            }
            if (ni == s.skillIdx) s else s.copy(skillIdx = ni)
        }
        return state.copy(skillGroups = skillGroups, staff = staff)
    }
}
