package com.magi.app.model

import org.json.JSONArray
import org.json.JSONObject

/**
 * Parses / serializes the Web app's `state` JSON. Uses org.json (built into Android)
 * because the schema mixes types freely — e.g. need fields are sometimes an integer
 * and sometimes the empty string "". org.json tolerates that cleanly.
 */
object StateParser {

    fun parse(json: String): MagiState {
        val o = JSONObject(json)

        val shifts = o.optJSONArray("shifts").mapObjects("shifts") {
            Shift(it.optString("name"), it.optString("kigou"), asStr(it.opt("need1")), asStr(it.opt("need2")))
        }
        val groups = o.optJSONArray("groups").mapObjects("groups") {
            Group(it.optString("name"), it.optString("kigou"))
        }
        val staff = o.optJSONArray("staff").mapObjects("staff") {
            Staff(it.optString("name"), it.optInt("groupIdx", 0), it.optInt("skillIdx", 0))
        }
        val skillGroups = o.optJSONArray("skillGroups").mapObjects("skillGroups") {
            Group(it.optString("name"), it.optString("kigou"))
        }
        val groupShift = o.optJSONArray("groupShift").mapArrays("groupShift") { row ->
            (0 until row.length()).map { row.optInt(it, 0) }
        }
        val groupShiftApt = o.optJSONArray("groupShiftApt").mapArrays("groupShiftApt") { row ->
            (0 until row.length()).map { asStr(row.opt(it)) }
        }
        val schedule = o.optJSONArray("schedule").mapArrays("schedule") { row ->
            // [監査A9] 不正/null セルは 0(先頭シフト) でなく -1(未割当) に倒す（勝手な勤務化を防ぐ）。
            (0 until row.length()).map { row.optInt(it, -1) }
        }

        val wishes = HashMap<String, Int>()
        o.optJSONObject("wishes")?.let { w ->
            w.keys().forEach { k -> wishes[k] = w.optInt(k, -1) }
        }
        val staffRange = HashMap<String, Range>()
        o.optJSONObject("staffRange")?.let { r ->
            r.keys().forEach { k ->
                val ro = r.optJSONObject(k)
                if (ro != null) staffRange[k] = Range(asStr(ro.opt("lo")), asStr(ro.opt("hi")))
            }
        }
        val needDay1 = strMap(o.optJSONObject("needDay1"))
        val needDay2 = strMap(o.optJSONObject("needDay2"))
        val shiftColors = strMap(o.optJSONObject("shiftColors"))

        val cons1 = o.optJSONArray("cons1").mapObjects("cons1") {
            C1Row(asStr(it.opt("day1")), it.optString("shiftKigou"), asStr(it.opt("day2")))
        }
        val cons2 = o.optJSONArray("cons2").mapObjects("cons2") {
            C2Row(it.optString("shiftKigou"), asStr(it.opt("count")))
        }
        val cons3 = o.optJSONArray("cons3").mapObjects("cons3") { C3Row(strList(it.optJSONArray("pattern"))) }
        val cons3n = o.optJSONArray("cons3n").mapObjects("cons3n") { C3Row(strList(it.optJSONArray("pattern"))) }
        val cons3m = o.optJSONArray("cons3m").mapObjects("cons3m") { C3Row(strList(it.optJSONArray("pattern"))) }
        val cons3mn = o.optJSONArray("cons3mn").mapObjects("cons3mn") { C3Row(strList(it.optJSONArray("pattern"))) }
        val cons41 = o.optJSONArray("cons41").mapObjects("cons41") {
            C41Row(it.optString("groupKigou"), it.optString("shiftKigou"), asStr(it.opt("l")), asStr(it.opt("u")))
        }
        val cons42 = o.optJSONArray("cons42").mapObjects("cons42") {
            C42Row(it.optString("g1Kigou"), it.optString("g2Kigou"), it.optString("s1Kigou"), it.optString("s2Kigou"))
        }
        val cons41s = o.optJSONArray("cons41s").mapObjects("cons41s") {
            C41Row(it.optString("groupKigou"), it.optString("shiftKigou"), asStr(it.opt("l")), asStr(it.opt("u")))
        }
        val cons42s = o.optJSONArray("cons42s").mapObjects("cons42s") {
            C42Row(it.optString("g1Kigou"), it.optString("g2Kigou"), it.optString("s1Kigou"), it.optString("s2Kigou"))
        }

        // Keep unmodelled top-level keys verbatim for lossless export.
        val modelled = setOf(
            "shifts", "groups", "staff", "groupShift", "groupShiftApt", "schedule", "wishes", "staffRange",
            "needDay1", "needDay2", "cons1", "cons2", "cons3", "cons3n", "cons3m", "cons3mn",
            "cons41", "cons42", "shiftColors", "startDate", "endDate", "use2Patterns",
            "skillGroups", "cons41s", "cons42s"
        )
        val extras = HashMap<String, Any?>()
        o.keys().forEach { key -> if (key !in modelled) extras[key] = o.get(key) }

        return MagiState(
            startDate = o.optString("startDate", "2025-01-01"),
            endDate = o.optString("endDate", ""),
            shifts = shifts, groups = groups, staff = staff,
            use2Patterns = o.optBoolean("use2Patterns", false),
            groupShift = groupShift, groupShiftApt = groupShiftApt, schedule = schedule,
            wishes = wishes, staffRange = staffRange,
            needDay1 = needDay1, needDay2 = needDay2,
            shiftColors = shiftColors,
            cons1 = cons1, cons2 = cons2,
            cons3 = cons3, cons3n = cons3n, cons3m = cons3m, cons3mn = cons3mn,
            cons41 = cons41, cons42 = cons42,
            skillGroups = skillGroups, cons41s = cons41s, cons42s = cons42s,
            extras = extras,
        )
    }

    /**
     * Re-emit the state with [newSchedule] substituted. We reparse the original text
     * so every field (including ones this app does not model) is preserved exactly,
     * then overwrite only "schedule".
     */
    fun exportWithSchedule(originalJson: String, newSchedule: Array<IntArray>): String {
        val o = JSONObject(originalJson)
        // 最適化後 schedule と食い違う古い派生・キャッシュ系フィールドを除去（互換性事故防止）。
        for (k in listOf("violations", "needViolations", "countViolations", "lastResult", "lastPhase")) {
            o.remove(k)
        }
        val arr = JSONArray()
        for (row in newSchedule) {
            val r = JSONArray()
            for (v in row) r.put(v)
            arr.put(r)
        }
        o.put("schedule", arr)
        return o.toString(2)
    }

    /**
     * Like [exportWithSchedule] but also overwrites the 8 constraint arrays from [state]
     * (used after ws3-5 constraint editing). All other top-level fields — including
     * groupShiftApt and any unmodelled keys — are preserved via the original-JSON round-trip.
     */
    fun exportWithEdits(originalJson: String, state: MagiState, newSchedule: Array<IntArray>): String {
        val o = JSONObject(originalJson)
        for (k in listOf("violations", "needViolations", "countViolations", "lastResult", "lastPhase")) o.remove(k)
        val sched = JSONArray()
        for (row in newSchedule) { val r = JSONArray(); for (v in row) r.put(v); sched.put(r) }
        o.put("schedule", sched)
        o.put("cons1", consArr(state.cons1) { obj("day1" to it.day1, "shiftKigou" to it.shiftKigou, "day2" to it.day2) })
        o.put("cons2", consArr(state.cons2) { obj("shiftKigou" to it.shiftKigou, "count" to it.count) })
        o.put("cons3", consArr(state.cons3) { patternObj(it.pattern) })
        o.put("cons3n", consArr(state.cons3n) { patternObj(it.pattern) })
        o.put("cons3m", consArr(state.cons3m) { patternObj(it.pattern) })
        o.put("cons3mn", consArr(state.cons3mn) { patternObj(it.pattern) })
        o.put("cons41", consArr(state.cons41) { obj("groupKigou" to it.groupKigou, "shiftKigou" to it.shiftKigou, "l" to it.l, "u" to it.u) })
        o.put("cons42", consArr(state.cons42) { obj("g1Kigou" to it.g1Kigou, "g2Kigou" to it.g2Kigou, "s1Kigou" to it.s1Kigou, "s2Kigou" to it.s2Kigou) })
        // [監査A1] スキル制約も書き出す（従来は旧8族のみで、cons41s/42s の追加・削除・変更が
        //   このエクスポート経路(constraintsEditedのみ)から無言で欠落していた）。
        o.put("cons41s", consArr(state.cons41s) { obj("groupKigou" to it.groupKigou, "shiftKigou" to it.shiftKigou, "l" to it.l, "u" to it.u) })
        o.put("cons42s", consArr(state.cons42s) { obj("g1Kigou" to it.g1Kigou, "g2Kigou" to it.g2Kigou, "s1Kigou" to it.s1Kigou, "s2Kigou" to it.s2Kigou) })
        return o.toString(2)
    }

    private fun <T> consArr(list: List<T>, f: (T) -> JSONObject): JSONArray {
        val a = JSONArray(); for (e in list) a.put(f(e)); return a
    }

    private fun obj(vararg pairs: Pair<String, String>): JSONObject {
        val o = JSONObject(); for ((k, v) in pairs) o.put(k, v); return o
    }

    private fun patternObj(pattern: List<String>): JSONObject {
        val o = JSONObject(); val p = JSONArray(); for (s in pattern) p.put(s); o.put("pattern", p); return o
    }

    /**
     * Full serialization of a [MagiState] (used after ws1 initial-setup edits that change
     * dimensions, where the original-JSON round-trip is no longer valid). Writes every modelled
     * field plus any [MagiState.extras] verbatim, using the exact key names [parse] reads, so
     * serialize -> parse round-trips. [schedule] overrides state.schedule (the working table).
     */
    fun serialize(state: MagiState, schedule: Array<IntArray>): String {
        val o = JSONObject()
        o.put("startDate", state.startDate)
        o.put("endDate", state.endDate)
        o.put("use2Patterns", state.use2Patterns)
        o.put("shifts", consArr(state.shifts) {
            obj("name" to it.name, "kigou" to it.kigou, "need1" to it.need1, "need2" to it.need2)
        })
        o.put("groups", consArr(state.groups) { obj("name" to it.name, "kigou" to it.kigou) })
        val staffArr = JSONArray()
        for (s in state.staff) { val so = JSONObject(); so.put("name", s.name); so.put("groupIdx", s.groupIdx); so.put("skillIdx", s.skillIdx); staffArr.put(so) }
        o.put("staff", staffArr)
        o.put("skillGroups", consArr(state.skillGroups) { obj("name" to it.name, "kigou" to it.kigou) })
        o.put("groupShift", intGrid(state.groupShift))
        o.put("groupShiftApt", strGrid(state.groupShiftApt))
        val sched = JSONArray()
        for (row in schedule) { val r = JSONArray(); for (v in row) r.put(v); sched.put(r) }
        o.put("schedule", sched)
        val wishes = JSONObject(); for ((k, v) in state.wishes) wishes.put(k, v); o.put("wishes", wishes)
        val sr = JSONObject()
        for ((k, v) in state.staffRange) { val ro = JSONObject(); ro.put("lo", v.lo); ro.put("hi", v.hi); sr.put(k, ro) }
        o.put("staffRange", sr)
        o.put("needDay1", strKeyMap(state.needDay1))
        o.put("needDay2", strKeyMap(state.needDay2))
        o.put("shiftColors", strKeyMap(state.shiftColors))
        o.put("cons1", consArr(state.cons1) { obj("day1" to it.day1, "shiftKigou" to it.shiftKigou, "day2" to it.day2) })
        o.put("cons2", consArr(state.cons2) { obj("shiftKigou" to it.shiftKigou, "count" to it.count) })
        o.put("cons3", consArr(state.cons3) { patternObj(it.pattern) })
        o.put("cons3n", consArr(state.cons3n) { patternObj(it.pattern) })
        o.put("cons3m", consArr(state.cons3m) { patternObj(it.pattern) })
        o.put("cons3mn", consArr(state.cons3mn) { patternObj(it.pattern) })
        o.put("cons41", consArr(state.cons41) { obj("groupKigou" to it.groupKigou, "shiftKigou" to it.shiftKigou, "l" to it.l, "u" to it.u) })
        o.put("cons42", consArr(state.cons42) { obj("g1Kigou" to it.g1Kigou, "g2Kigou" to it.g2Kigou, "s1Kigou" to it.s1Kigou, "s2Kigou" to it.s2Kigou) })
        o.put("cons41s", consArr(state.cons41s) { obj("groupKigou" to it.groupKigou, "shiftKigou" to it.shiftKigou, "l" to it.l, "u" to it.u) })
        o.put("cons42s", consArr(state.cons42s) { obj("g1Kigou" to it.g1Kigou, "g2Kigou" to it.g2Kigou, "s1Kigou" to it.s1Kigou, "s2Kigou" to it.s2Kigou) })
        for ((k, v) in state.extras) if (!o.has(k)) o.put(k, v)
        return o.toString(2)
    }

    private fun intGrid(grid: List<List<Int>>): JSONArray {
        val a = JSONArray(); for (row in grid) { val r = JSONArray(); for (v in row) r.put(v); a.put(r) }; return a
    }

    private fun strGrid(grid: List<List<String>>): JSONArray {
        val a = JSONArray(); for (row in grid) { val r = JSONArray(); for (v in row) r.put(v); a.put(r) }; return a
    }

    private fun strKeyMap(m: Map<String, String>): JSONObject {
        val o = JSONObject(); for ((k, v) in m) o.put(k, v); return o
    }

    // ---- helpers -------------------------------------------------------------

    private fun asStr(v: Any?): String = when (v) {
        null, JSONObject.NULL -> ""
        is String -> v
        is Int -> v.toString()
        is Long -> v.toString()
        is Double -> if (v == v.toLong().toDouble()) v.toLong().toString() else v.toString()
        else -> v.toString()
    }

    private fun strList(a: JSONArray?): List<String> {
        if (a == null) return emptyList()
        return (0 until a.length()).map { asStr(a.opt(it)) }
    }

    private fun strMap(o: JSONObject?): Map<String, String> {
        if (o == null) return emptyMap()
        val m = HashMap<String, String>()
        o.keys().forEach { k -> m[k] = asStr(o.opt(k)) }
        return m
    }

    // [外部レビュー P2-02] 旧: 要素がオブジェクト/配列でなければ黙って読み飛ばしていた（null・数値・文字列の
    //   混入で staff/schedule 等が本来より短いリストへ静かに変わる）。インポート・復元データは職員名や
    //   勤務割当を含むため、読み飛ばしより「配列N番目が不正」という明示的な失敗のほうが安全。
    //   throw は3呼出元（MagiViewModel×2・OptimizationWorker×1）すべてが runCatching/try-catch で
    //   包んでいる（確認済み）ので、ここで投げても未捕捉クラッシュにはならない。
    private inline fun <T> JSONArray?.mapObjects(name: String, f: (JSONObject) -> T): List<T> {
        if (this == null) return emptyList()
        val out = ArrayList<T>(length())
        for (i in 0 until length()) {
            val o = optJSONObject(i)
                ?: throw IllegalArgumentException("$name[$i] がオブジェクトではありません（データが壊れています）")
            out.add(f(o))
        }
        return out
    }

    private inline fun <T> JSONArray?.mapArrays(name: String, f: (JSONArray) -> T): List<T> {
        if (this == null) return emptyList()
        val out = ArrayList<T>(length())
        for (i in 0 until length()) {
            val a = optJSONArray(i)
                ?: throw IllegalArgumentException("$name[$i] が配列ではありません（データが壊れています）")
            out.add(f(a))
        }
        return out
    }
}
