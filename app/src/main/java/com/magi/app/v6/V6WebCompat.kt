package com.magi.app.v6

import com.magi.app.model.MagiState
import java.time.LocalDate
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/** Native substitutes for non-DOM V6 Web helpers. */
object V6WebCompat {
    data class HistoryEntry(
        val label: String,
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val ts: Long = System.currentTimeMillis(),
    )

    data class HistoryState(
        val undo: List<HistoryEntry> = emptyList(),
        val redo: List<HistoryEntry> = emptyList(),
        val limit: Int = 30,
    )

    sealed class HistoryAction {
        data class Push(val entry: HistoryEntry) : HistoryAction()
        object Undo : HistoryAction()
        object Redo : HistoryAction()
        object Clear : HistoryAction()
    }

    data class ImpossibleAssignment(
        val staffIndex: Int,
        val dayIndex: Int,
        val shiftIndex: Int,
        val staffName: String,
        val shiftSymbol: String,
        val reason: String,
    )

    data class StructuredShiftCountDiagnostic(
        val status: String,
        val staffName: String,
        val shiftSymbol: String,
        val count: Int,
        val lo: Int?,
        val hi: Int?,
        val message: String,
    )

    data class DistributionReview(
        val summary: String,
        val staffLines: List<String>,
        val shiftLines: List<String>,
    )

    data class StaffViolLog(
        val staffIndex: Int,
        val staffName: String,
        val hard: Int,
        val soft: Int,
        val messages: List<String>,
    )

    data class WorksheetCell(
        val row: Int,
        val col: Int,
        val value: String,
        val note: String? = null,
    )

    data class Worksheet(val name: String, val cells: List<WorksheetCell>) {
        fun toTsv(): String {
            var maxR = 0
            var maxC = 0
            for (cell in cells) {
                if (cell.row > maxR) maxR = cell.row
                if (cell.col > maxC) maxC = cell.col
            }
            val grid = Array(maxR + 1) { Array(maxC + 1) { "" } }
            for (cell in cells) {
                if (cell.row >= 0 && cell.col >= 0) {
                    grid[cell.row][cell.col] = cell.value
                }
            }
            val sb = StringBuilder()
            for (r in grid.indices) {
                if (r > 0) sb.append('\n')
                for (c in grid[r].indices) {
                    if (c > 0) sb.append('\t')
                    sb.append(grid[r][c].replace("\t", " ").replace("\n", " "))
                }
            }
            return sb.toString()
        }
    }

    data class Workbook(val sheets: List<Worksheet>)
    data class V5Flags(
        val useDcb: Boolean = true,
        val useVns: Boolean = true,
        val useLahc: Boolean = true,
        val useMctsLite: Boolean = true,
        val usePathRelinking: Boolean = true,
        val useGroundedDestroy: Boolean = true,
        val usePostRepair: Boolean = true,
    )

    fun makeInitialHistoryState(limit: Int = 30): HistoryState = HistoryState(limit = limit)

    fun historyReducer(state: HistoryState, action: HistoryAction): HistoryState {
        return when (action) {
            is HistoryAction.Push -> {
                val undo = ArrayList<HistoryEntry>(state.undo)
                undo.add(action.entry)
                while (undo.size > state.limit) undo.removeAt(0)
                HistoryState(undo, emptyList(), state.limit)
            }
            HistoryAction.Undo -> {
                if (state.undo.isEmpty()) {
                    state
                } else {
                    val undo = ArrayList<HistoryEntry>(state.undo)
                    val moved = undo.removeAt(undo.lastIndex)
                    val redo = ArrayList<HistoryEntry>(state.redo)
                    redo.add(moved)
                    while (redo.size > state.limit) redo.removeAt(0)
                    HistoryState(undo, redo, state.limit)
                }
            }
            HistoryAction.Redo -> {
                if (state.redo.isEmpty()) {
                    state
                } else {
                    val redo = ArrayList<HistoryEntry>(state.redo)
                    val moved = redo.removeAt(redo.lastIndex)
                    val undo = ArrayList<HistoryEntry>(state.undo)
                    undo.add(moved)
                    while (undo.size > state.limit) undo.removeAt(0)
                    HistoryState(undo, redo, state.limit)
                }
            }
            HistoryAction.Clear -> makeInitialHistoryState(state.limit)
        }
    }

    fun pickTextColor(bgHex: String): String {
        val rgb = parseHex(bgHex) ?: return "#14110d"
        val lum = relLum(rgb.first, rgb.second, rgb.third)
        val dark = relLum(0x14, 0x11, 0x0d)
        val light = relLum(0xfb, 0xf4, 0xe8)
        return if (contrast(lum, dark) >= contrast(lum, light)) "#14110d" else "#fbf4e8"
    }

    fun shiftCatDefault(symbol: String, name: String = ""): String {
        val s = (symbol + " " + name).lowercase(Locale.JAPAN)
        if (symbol.isBlank()) return "unknown"
        if (s.contains("休") || s.contains("off") || s.contains("明")) return "rest"
        if (s.contains("夜") || s.contains("night") || s.contains("深")) return "night"
        if (s.contains("早") || s.contains("early")) return "early"
        if (s.contains("遅") || s.contains("late")) return "late"
        if (s.contains("日") || s.contains("day") || s.contains("勤")) return "day"
        return "work"
    }

    // [判別性パレット] 似たカテゴリのシフトが同色に潰れないよう、休(rest)以外はシフトの並び順(index)で
    //   色相を十分に離した既定色を割り当てる。隣接シフトのコントラストを保つため暖色/寒色を交互配置。
    private val SHIFT_WORK_PALETTE = listOf(
        "#E59B96", // coral
        "#74BEB0", // teal
        "#E0B968", // amber
        "#93A9E0", // periwinkle
        "#A6C77E", // lime
        "#D7A0D0", // orchid
        "#84C4DC", // sky
        "#E0A0B4", // rose
        "#7FC59B", // mint
        "#B79CE0", // lavender
        "#CFC56A", // gold
        "#C2A98A", // taupe
        "#BBC58A", // olive
        "#E0B0A0", // peach
        "#9AC0C8", // dusty cyan
        "#C8A0C0", // mauve
    )

    fun resolveShiftColor(symbol: String, name: String = "", explicit: String? = null, index: Int = -1): String {
        if (!explicit.isNullOrBlank()) return explicit
        // 休(rest)は常に落ち着いたスレート（休みであることを一目で）。
        if (shiftCatDefault(symbol, name) == "rest") return "#A7B4C2"
        // それ以外はシフトの並び順で判別性の高い色を割り当て（同カテゴリの色潰れを防ぐ）。
        if (index >= 0) return SHIFT_WORK_PALETTE[index % SHIFT_WORK_PALETTE.size]
        // index 未指定（後方互換）: 旧カテゴリ別の既定色。
        return when (shiftCatDefault(symbol, name)) {
            "night" -> "#B79CE0"  // 夜: ダスティ・ラベンダー
            "early" -> "#74BEB0"  // 早: やわらかいティール
            "late" -> "#E0B968"   // 遅: 穏やかなアンバー
            "day" -> "#8CBE89"    // 日: やさしいセージ
            "work" -> "#84C4DC"   // 勤: やわらかいスカイ
            else -> "#C2B4A0"     // 他: 温かいトープ
        }
    }

    fun shiftIdxFromKigou(state: MagiState, kigou: String): Int {
        for (i in state.shifts.indices) {
            val sh = state.shifts[i]
            if (sh.kigou == kigou || sh.name == kigou) return i
        }
        return -1
    }

    fun groupIdxFromKigou(state: MagiState, kigou: String): Int {
        for (i in state.groups.indices) {
            val g = state.groups[i]
            if (g.kigou == kigou || g.name == kigou) return i
        }
        return -1
    }

    fun buildImpossibleWishSummary(state: MagiState): String {
        val list = V6SanityPort.detectImpossibleWishes(state)
        if (list.isEmpty()) return "実現不能な希望はありません"
        val sb = StringBuilder("実現不能な希望 ${list.size}件")
        val n = min(30, list.size)
        for (idx in 0 until n) {
            val it = list[idx]
            sb.append('\n').append("- ${it.staffName} ${it.dayIndex + 1}日 ${it.shiftSymbol}: ${it.reason}")
        }
        if (list.size > 30) sb.append("\n…ほか${list.size - 30}件")
        return sb.toString()
    }

    fun buildShiftCountDiagnosticStructured(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
    ): List<StructuredShiftCountDiagnostic> {
        val src = V6SanityPort.build(state, schedule).shiftCountDiagnostics
        val out = ArrayList<StructuredShiftCountDiagnostic>(src.size)
        for (d in src) {
            val msg = when (d.status) {
                "LOW" -> "${d.staffName} の ${d.shiftSymbol} が下限不足: ${d.count}/${d.lo}"
                "HIGH" -> "${d.staffName} の ${d.shiftSymbol} が上限超過: ${d.count}/${d.hi}"
                else -> "${d.staffName} の ${d.shiftSymbol} は範囲内: ${d.count}"
            }
            out.add(StructuredShiftCountDiagnostic(d.status, d.staffName, d.shiftSymbol, d.count, d.lo, d.hi, msg))
        }
        return out
    }

    fun buildImpossibleAssignmentSummary(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
    ): List<ImpossibleAssignment> {
        val p = Problem(state)
        val s = normalizeSchedule(schedule, p)
        val out = ArrayList<ImpossibleAssignment>()
        for (i in 0 until p.S) {
            for (j in 0 until p.T) {
                val k = s[i][j]
                val reason = if (k !in 0 until p.K) {
                    "シフト番号が範囲外"
                } else if (!p.canDo(i, k)) {
                    "職員のグループでは担当不可"
                } else {
                    null
                }
                if (reason != null) {
                    out.add(ImpossibleAssignment(i, j, k, state.staff.getOrNull(i)?.name ?: "#${i}", state.shifts.getOrNull(k)?.kigou ?: k.toString(), reason))
                }
            }
        }
        return out
    }

    fun runSimpleSchedule(state: MagiState): ScheduleRunResult = GreedyMirrorScheduler.generate(state)

    fun runViolationCheck(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
    ): ViolationReport = UnifiedViolationChecker.check(state, schedule)

    fun buildDistributionReview(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
    ): DistributionReview {
        val p = Problem(state)
        val s = normalizeSchedule(schedule, p)
        val counts = countMatrix(p, s)
        val staffLines = ArrayList<String>(p.S)
        for (i in 0 until p.S) {
            val parts = ArrayList<String>()
            for (k in 0 until p.K) {
                if (counts[i][k] > 0) parts.add("${state.shifts.getOrNull(k)?.kigou ?: k.toString()}=${counts[i][k]}")
            }
            val name = state.staff.getOrNull(i)?.name ?: i.toString()
            val row = parts.joinToString(" ")
            staffLines.add("${name}: ${if (row.isBlank()) "割当なし" else row}")
        }
        val totals = IntArray(p.K)
        for (i in 0 until p.S) for (k in 0 until p.K) totals[k] += counts[i][k]
        val shiftLines = ArrayList<String>(p.K)
        for (k in 0 until p.K) {
            val avg = String.format(Locale.US, "%.1f", totals[k].toDouble() / max(1, p.S))
            shiftLines.add("${state.shifts.getOrNull(k)?.kigou ?: k.toString()}: total=${totals[k]} avg=$avg")
        }
        var minLoad = Int.MAX_VALUE
        var maxLoad = Int.MIN_VALUE
        for (i in 0 until p.S) {
            var sum = 0
            for (k in 0 until p.K) sum += counts[i][k]
            if (sum < minLoad) minLoad = sum
            if (sum > maxLoad) maxLoad = sum
        }
        if (p.S == 0) {
            minLoad = 0
            maxLoad = 0
        }
        return DistributionReview("負荷範囲 ${minLoad}..${maxLoad} / シフト数=${p.K}", staffLines, shiftLines)
    }

    fun buildStaffViolLogs(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
        report: ViolationReport = UnifiedViolationChecker.check(state, schedule),
    ): List<StaffViolLog> {
        val byStaff = LinkedHashMap<Int, MutableList<String>>()
        for ((cell, cls) in report.violations) {
            val i = cell.substringBefore(',').toIntOrNull() ?: continue
            byStaff.getOrPut(i) { ArrayList() }.add("${cell}:${cls}")
        }
        val out = ArrayList<StaffViolLog>()
        for (i in 0 until state.staffCount) {
            val msgs = byStaff[i] ?: continue
            var hard = 0
            for (m in msgs) {
                if (m.contains("vio-covU") || m.contains("vio-c3n") || m.contains("vio-pref") || m.contains("vio-groupViol")) hard++
            }
            out.add(StaffViolLog(i, state.staff.getOrNull(i)?.name ?: "#${i}", hard, msgs.size - hard, msgs))
        }
        return out
    }

    fun invalidAssignmentCount(state: MagiState, schedule: Array<IntArray> = state.schedule.toIntArray2D()): Int {
        return buildImpossibleAssignmentSummary(state, schedule).size
    }

    fun popcnt32(x: Int): Int = Integer.bitCount(x)

    fun validStartMask(sequenceLength: Int, term: Int): Int {
        if (sequenceLength <= 0 || term <= 0 || sequenceLength > 31) return 0
        val maxStart = max(0, min(31, term - sequenceLength + 1))
        return if (maxStart >= 31) -1 else (1 shl maxStart) - 1
    }

    class MagiRNG(seed: Long) {
        private var x = if (seed == 0L) 0x13579BDF2468ACEFL else seed
        fun nextU32(): Int {
            x = x xor (x shl 13)
            x = x xor (x ushr 7)
            x = x xor (x shl 17)
            return x.toInt()
        }
        fun nextDouble(): Double = ((nextU32().toLong() and 0xffffffffL).toDouble() / 4294967296.0)
        fun nextInt(bound: Int): Int = if (bound <= 0) 0 else ((nextU32().toLong() and 0xffffffffL) % bound).toInt()
    }

    class MagiPatternDB(patterns: List<List<Int>>) {
        private val set = HashSet<String>()
        init {
            for (p in patterns) set.add(p.joinToString(","))
        }
        fun contains(seq: List<Int>): Boolean = set.contains(seq.joinToString(","))
        fun size(): Int = set.size
    }

    fun makeXorShift(seed: Long): MagiRNG = MagiRNG(seed)

    fun hammingDistanceV5(a: Array<IntArray>, b: Array<IntArray>): Int {
        val s = min(a.size, b.size)
        var d = abs(a.size - b.size)
        for (i in 0 until s) {
            val t = min(a[i].size, b[i].size)
            d += abs(a[i].size - b[i].size)
            for (j in 0 until t) if (a[i][j] != b[i][j]) d++
        }
        return d
    }

    fun zobristHashV5(schedule: Array<IntArray>, seed: Long = 0x51EDL): Long {
        var h = seed xor -7046029254386353131L
        for (i in schedule.indices) {
            for (j in schedule[i].indices) {
                h = mix64(h xor ((schedule[i][j] + 129L) * 1099511628211L + i * 1009L + j * 9176L))
            }
        }
        return h
    }

    fun colLetter(index0: Int): String {
        var n = index0 + 1
        val sb = StringBuilder()
        while (n > 0) {
            n--
            sb.append(('A'.code + (n % 26)).toChar())
            n /= 26
        }
        return sb.reverse().toString()
    }

    fun buildScheduleSheetCells(state: MagiState, schedule: Array<IntArray> = state.schedule.toIntArray2D()): List<WorksheetCell> {
        val cells = ArrayList<WorksheetCell>()
        cells.add(WorksheetCell(0, 0, "Staff"))
        for (j in 0 until state.dayCount) cells.add(WorksheetCell(0, j + 1, dayDate(state.startDate, j)))
        for (i in 0 until state.staffCount) {
            cells.add(WorksheetCell(i + 1, 0, state.staff.getOrNull(i)?.name ?: "#${i}"))
            for (j in 0 until state.dayCount) {
                val k = schedule.getOrNull(i)?.getOrNull(j) ?: -1
                cells.add(WorksheetCell(i + 1, j + 1, state.shifts.getOrNull(k)?.kigou ?: ""))
            }
        }
        return cells
    }

    fun buildWs1(state: MagiState, schedule: Array<IntArray> = state.schedule.toIntArray2D()): Worksheet {
        return Worksheet("WS1_Schedule", buildScheduleSheetCells(state, schedule))
    }

    fun buildWs2(state: MagiState, report: ViolationReport = UnifiedViolationChecker.check(state)): Worksheet {
        val cells = ArrayList<WorksheetCell>()
        cells.add(WorksheetCell(0, 0, "Violation"))
        cells.add(WorksheetCell(0, 1, "Count"))
        for (idx in MirrorKeys.all.indices) {
            val key = MirrorKeys.all[idx]
            cells.add(WorksheetCell(idx + 1, 0, key))
            cells.add(WorksheetCell(idx + 1, 1, (report.breakdown[key] ?: 0).toString()))
        }
        return Worksheet("WS2_Violations", cells)
    }

    fun buildWs3(state: MagiState): Worksheet {
        val cells = ArrayList<WorksheetCell>()
        cells.add(WorksheetCell(0, 0, "Name"))
        cells.add(WorksheetCell(0, 1, "Group"))
        for (i in 0 until state.staffCount) {
            val st = state.staff[i]
            cells.add(WorksheetCell(i + 1, 0, st.name))
            cells.add(WorksheetCell(i + 1, 1, state.groups.getOrNull(st.groupIdx)?.kigou ?: "?"))
        }
        return Worksheet("WS3_Staff", cells)
    }

    fun buildWs4(state: MagiState): Worksheet {
        val cells = ArrayList<WorksheetCell>()
        cells.add(WorksheetCell(0, 0, "Kigou"))
        cells.add(WorksheetCell(0, 1, "Name"))
        cells.add(WorksheetCell(0, 2, "Need1"))
        cells.add(WorksheetCell(0, 3, "Need2"))
        for (i in 0 until state.shiftCount) {
            val sh = state.shifts[i]
            cells.add(WorksheetCell(i + 1, 0, sh.kigou))
            cells.add(WorksheetCell(i + 1, 1, sh.name))
            cells.add(WorksheetCell(i + 1, 2, sh.need1))
            cells.add(WorksheetCell(i + 1, 3, sh.need2))
        }
        return Worksheet("WS4_Shifts", cells)
    }

    fun buildWs5(state: MagiState): Worksheet {
        val cells = ArrayList<WorksheetCell>()
        cells.add(WorksheetCell(0, 0, "Group"))
        cells.add(WorksheetCell(0, 1, "Name"))
        for (i in 0 until state.groupCount) {
            val g = state.groups[i]
            cells.add(WorksheetCell(i + 1, 0, g.kigou))
            cells.add(WorksheetCell(i + 1, 1, g.name))
        }
        return Worksheet("WS5_Groups", cells)
    }

    fun buildWs6(state: MagiState): Worksheet {
        val p = Problem(state)
        val cells = ArrayList<WorksheetCell>()
        cells.add(WorksheetCell(0, 0, "Group/Shift"))
        for (k in 0 until p.K) cells.add(WorksheetCell(0, k + 1, state.shifts[k].kigou))
        for (g in 0 until p.G) {
            cells.add(WorksheetCell(g + 1, 0, state.groups[g].kigou))
            for (k in 0 until p.K) {
                val value = if (state.groupShift.getOrNull(g)?.getOrNull(k) == 1) "1" else ""
                cells.add(WorksheetCell(g + 1, k + 1, value))
            }
        }
        return Worksheet("WS6_GroupShift", cells)
    }

    fun buildWs7(state: MagiState): Worksheet {
        val sanity = V6SanityPort.build(state)
        val cells = ArrayList<WorksheetCell>()
        cells.add(WorksheetCell(0, 0, "Type"))
        cells.add(WorksheetCell(0, 1, "Message"))
        var r = 1
        for (w in sanity.warns) {
            cells.add(WorksheetCell(r, 0, "WARN")); cells.add(WorksheetCell(r, 1, w)); r++
        }
        for (n in sanity.notes) {
            cells.add(WorksheetCell(r, 0, "NOTE")); cells.add(WorksheetCell(r, 1, n)); r++
        }
        for (d in sanity.loadDataBitDetails) {
            cells.add(WorksheetCell(r, 0, "DETAIL")); cells.add(WorksheetCell(r, 1, d)); r++
        }
        return Worksheet("WS7_Sanity", cells)
    }

    fun buildWorkbook(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
        report: ViolationReport = UnifiedViolationChecker.check(state, schedule),
    ): Workbook {
        val sheets = ArrayList<Worksheet>()
        sheets.add(buildWs1(state, schedule))
        sheets.add(buildWs2(state, report))
        sheets.add(buildWs3(state))
        sheets.add(buildWs4(state))
        sheets.add(buildWs5(state))
        sheets.add(buildWs6(state))
        sheets.add(buildWs7(state))
        return Workbook(sheets)
    }

    fun formatLogsAsText(logs: List<MirrorLog>): String {
        val sb = StringBuilder()
        for (idx in logs.indices) {
            if (idx > 0) sb.append('\n')
            val log = logs[idx]
            sb.append(java.time.Instant.ofEpochMilli(log.ts)).append(" [").append(log.level).append("] ").append(log.tag).append(": ").append(log.message)
        }
        return sb.toString()
    }

    fun getV5Flags(): V5Flags = V5Flags()

    fun progressStageLabel(phase: String): String = when (phase.uppercase(Locale.JAPAN)) {
        "SEED", "V5" -> "初期解生成 / V5"
        "ALNS" -> "破壊・修復探索"
        "RSI" -> "違反要因集中探索"
        "RSI_PLUS", "RSI++" -> "RSI++ 複合探索"
        "HF66" -> "データ堅牢化"
        "HF67" -> "HARD修復"
        "HF80" -> "後処理磨き"
        else -> phase
    }

    // [監査修正] 凡例の重大度を重み階層(MirrorKeys)に整合させる。旧実装は low(90)/high(45)=最重 softを INFO(灰),
    //   covO を最軽 WARN(橙) と**逆転**表示していた(色凡例=ColorSettingsView は live)。表示のみ・スコア不変。
    //   階層: low(90)>high(45)>c1(15)=c3mn(15)>c3(3)>c3m(2)>c2/c41/c42/c41s/c42s/apt/covO(1)>fair/weekly(整え1)。
    //   [目的関数統一] covO は 0.5→1.0 に統一（2026-07-13）。重み1.0=c2/c41 と同格の実違反(過剰配置)なので
    //   WARN へ移動（旧: INFO。集計/グリッドで既に橙=過剰表示のため整合）。fair/weekly は「整え・常時非ゼロ」で INFO 維持。
    fun severityFromVioKey(key: String): String = when (key.removePrefix("vio-")) {
        "groupViol", "covU", "pref", "c3n" -> "CRITICAL"                                   // HARD
        "low", "high", "c3mn" -> "HIGH"                                                    // 重い soft(90/45/15)
        "c1", "c3", "c3m", "c2", "c41", "c42", "c41s", "c42s", "apt", "covO" -> "WARN"     // c1=15は最多件数で飽和回避(grid同様に非heavy)・他は1〜3/過剰配置。下流(V6RemainingScreens)はHIGH/WARNを同一表示に畳む
        "fair", "weekly" -> "INFO"                                                         // 整え(常時非ゼロ)
        else -> "INFO"
    }

    fun termNum(state: MagiState): Int = state.dayCount

    fun dayDate(startDate: String, offset: Int): String = try {
        LocalDate.parse(startDate).plusDays(offset.toLong()).format(DateTimeFormatter.ofPattern("M/d(E)", Locale.JAPAN))
    } catch (_: Exception) {
        "${offset + 1}日"
    }

    fun zeros2D(rows: Int, cols: Int): Array<IntArray> = Array(rows) { IntArray(cols) }
    fun zeros3D(a: Int, b: Int, c: Int): Array<Array<IntArray>> = Array(a) { Array(b) { IntArray(c) } }

    fun sectionForTab(tab: String): String = when (tab.lowercase(Locale.JAPAN)) {
        "home", "overview", "dashboard" -> "overview"
        "schedule", "month", "grid" -> "schedule"
        "staff", "people" -> "staff"
        "shift", "shifts" -> "shifts"
        "group", "groups" -> "groups"
        "constraint", "constraints" -> "constraints"
        "logs", "log" -> "logs"
        "flags", "settings" -> "settings"
        else -> tab
    }

    fun clamp(v: Int, lo: Int, hi: Int): Int = min(hi, max(lo, v))
    fun clamp(v: Double, lo: Double, hi: Double): Double = min(hi, max(lo, v))

    suspend fun runALNSParallel(state: MagiState, schedule: Array<IntArray>, budgetSec: Int): V6OptimizerResult {
        return V6NativeOptimizer.optimize(state, schedule, V6OptimizerOptions(algorithm = V6Algorithm.ALNS, totalBudgetSec = budgetSec))
    }

    suspend fun runRSIPlusParallel(state: MagiState, schedule: Array<IntArray>, budgetSec: Int): V6OptimizerResult {
        return V6NativeOptimizer.optimize(state, schedule, V6OptimizerOptions(algorithm = V6Algorithm.RSI_PLUS, totalBudgetSec = budgetSec))
    }

    private fun parseHex(hex: String): Triple<Int, Int, Int>? {
        val s = hex.trim().removePrefix("#")
        val full = when (s.length) {
            3 -> "${s[0]}${s[0]}${s[1]}${s[1]}${s[2]}${s[2]}"
            6 -> s
            else -> return null
        }
        return try {
            Triple(full.substring(0, 2).toInt(16), full.substring(2, 4).toInt(16), full.substring(4, 6).toInt(16))
        } catch (_: Exception) {
            null
        }
    }

    private fun relLum(r: Int, g: Int, b: Int): Double {
        fun ch(v: Int): Double {
            val x = v / 255.0
            return if (x <= 0.03928) x / 12.92 else ((x + 0.055) / 1.055).pow(2.4)
        }
        return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)
    }

    private fun contrast(a: Double, b: Double): Double = (max(a, b) + 0.05) / (min(a, b) + 0.05)

    private fun mix64(x0: Long): Long {
        var x = x0
        x = (x xor (x ushr 30)) * -4658895280553007687L
        x = (x xor (x ushr 27)) * -7723592293110705685L
        return x xor (x ushr 31)
    }
}
