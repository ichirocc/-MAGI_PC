package com.magi.app.v6

import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/**
 * シフト記号 → 表示色 と 違反キー → 重大度 の**唯一の解決元**。
 *
 * [3.393.0] 旧 `V6WebCompat`（Web 版の非DOMヘルパーを移植したもの）から、本アプリが実際に使う
 * 4関数だけを切り出した。**Web 版は存在しない**（ユーザー確認）ので、Web専用だった以下は削除した:
 * Worksheet/Workbook 一式（buildWs1〜7・buildWorkbook・buildScheduleSheetCells・colLetter）／
 * Web側の undo-redo リデューサ（HistoryEntry/HistoryState/HistoryAction/historyReducer。
 * 本アプリは MagiViewModel の undoStack/redoStack を使う）／Web側の診断ビルダ
 * （buildImpossibleWishSummary・buildShiftCountDiagnosticStructured・buildImpossibleAssignmentSummary・
 * buildDistributionReview・buildStaffViolLogs。本アプリは V6SanityPort/V6PortAnalyzer が担う）／
 * Web版 V5 のヘルパー（popcnt32・validStartMask・makeXorShift・hammingDistanceV5・zobristHashV5・
 * V5Flags）／呼出ゼロの雑多ユーティリティ（runSimpleSchedule・runViolationCheck・progressStageLabel・
 * sectionForTab・termNum・dayDate・zeros2D/3D・clamp・formatLogsAsText・shiftIdxFromKigou・
 * groupIdxFromKigou）。
 */
object ShiftAppearance {

    /** 背景色 [bgHex] の上に載せる文字色を、コントラストが大きい方（黒 or 生成り）で選ぶ。 */
    fun pickTextColor(bgHex: String): String {
        val rgb = parseHex(bgHex) ?: return "#14110d"
        val lum = relLum(rgb.first, rgb.second, rgb.third)
        val dark = relLum(0x14, 0x11, 0x0d)
        val light = relLum(0xfb, 0xf4, 0xe8)
        return if (contrast(lum, dark) >= contrast(lum, light)) "#14110d" else "#fbf4e8"
    }

    // [判別性パレット] シフトが同色に潰れないよう、並び順(index)で色相を十分に離した既定色を
    //   割り当てる。隣接シフトのコントラストを保つため暖色/寒色を交互配置。
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

    /**
     * 表示色は「利用者が設定した色」→「一覧上の位置」の順で決める。
     *
     * [3.415.0] 旧実装は記号・名称に「休/off/明」「夜/night/深」「早」「遅」「日/勤」が含まれるかで
     * カテゴリを推測し、rest だけは index パレットより優先してスレート固定にしていた。これは外部データに
     * 無い意味を記号の字面から作り出す推測で、①「公」「OFF」のように別の記号を使う職場では黙って効かない
     * ②「休日」のように複数のカテゴリ語を含む名称では先に書いた条件が勝つだけ、という当てにならない規則
     * だった。利用者が色を決めたいシフトは `shiftColors` の明示色（第1優先）で指定できる。
     */
    fun resolveShiftColor(explicit: String? = null, index: Int = -1): String {
        if (!explicit.isNullOrBlank()) return explicit
        if (index >= 0) return SHIFT_WORK_PALETTE[index % SHIFT_WORK_PALETTE.size]
        return NEUTRAL_SHIFT_COLOR
    }

    /** 位置が不明なときの色。どのシフトでも同じ＝記号による優劣を持たない。 */
    const val NEUTRAL_SHIFT_COLOR = "#84C4DC"

    fun severityFromVioKey(key: String): String = when (key.removePrefix("vio-")) {
        "groupViol", "covU", "pref", "c3n" -> "CRITICAL"                                   // HARD
        "low", "high", "c3mn" -> "HIGH"                                                    // 重い soft(90/45/30)
        "c1", "c3", "c3m", "c2", "c41", "c42", "c41s", "c42s", "apt", "covO" -> "WARN"     // c1=30 は最多件数で飽和回避(3.367.0)・他は1〜3/過剰配置。下流(V6RemainingScreens)はHIGH/WARNを同一表示に畳む
        "fair", "weekly" -> "INFO"                                                         // 整え(常時非ゼロ)
        else -> "INFO"
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
}
