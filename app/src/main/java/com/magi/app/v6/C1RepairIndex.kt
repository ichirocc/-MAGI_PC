package com.magi.app.v6

/**
 * [C1 Repair Index / 3.275.0] `C1RepairAnalysis` の出力を、修復オペレータが O(1) で引ける読取専用の索引へ
 * まとめる（図の C1RepairIndex 層）。**純関数（Problem×盤面）＝スコアリング不変・副作用なし**。
 * 各オペレータが個別に再計算していた「窓走査／候補日の gain／ドナー余裕」を1箇所へ集約する。
 *
 *  - dayToWindows      : day → その日を含む不足窓（重複窓の同時解消判断に）
 *  - staffRuleWindows  : (staff,ruleIndex) → その職員・規則で不足している窓
 *  - expectedGain      : (staff,day,targetShift) → その日を targetShift に変えたとき解消する窓不足数の最大
 *                        （希望固定/禁止連続で実際に置けない候補は 0 扱い。3.279.0でシフト分離）
 *  - donorMargin       : (staff,day) → 現在そこにある c1シフトを抜いても壊れない余裕（>0=安全ドナー）
 *
 * 本索引は候補生成・枝刈り・診断のための情報のみを提供し、採否には一切関与しない
 * （最終採否は常に呼出側の UnifiedViolationChecker + keep-best）。
 */
object C1RepairIndex {

    data class Index(
        val windows: List<C1RepairAnalysis.C1WindowViolation>,
        val dayToWindows: Map<Int, List<C1RepairAnalysis.C1WindowViolation>>,
        val staffRuleWindows: Map<Long, List<C1RepairAnalysis.C1WindowViolation>>,
        private val gain: Map<Long, Int>,
        private val donor: Map<Long, Int>,
    ) {
        /** 不足窓が1つでもあるか（＝c1修復オペレータが実際に動く余地があるか）。 */
        val hasActionable: Boolean get() = windows.isNotEmpty()

        /** 全不足窓の不足量合計（診断用）。 */
        val deficitTotal: Int get() = windows.sumOf { it.deficit }

        fun windowsCovering(day: Int): List<C1RepairAnalysis.C1WindowViolation> = dayToWindows[day] ?: emptyList()

        fun activeWindows(staff: Int, ruleIndex: Int): List<C1RepairAnalysis.C1WindowViolation> =
            staffRuleWindows[key(staff, ruleIndex)] ?: emptyList()

        /**
         * その日を targetShift へ変えたとき解消する窓不足数の最大（実際に置けない候補は 0）。
         * [3.279.0/外部レビューC1-07] 旧 API は (staff,day) のみで、同日に別シフトの不足窓が併存すると
         * gain が混合されどのシフトへの gain か判別不能だった。targetShift をキーに含めて分離。
         */
        fun expectedGain(staff: Int, day: Int, targetShift: Int): Int = gain[key3(staff, day, targetShift)] ?: 0

        /** そのセルの c1シフトを抜いても壊れない余裕。c1規則が依存しないセルは無限大（常に安全）。 */
        fun donorMargin(staff: Int, day: Int): Int = donor[key(staff, day)] ?: Int.MAX_VALUE
    }

    // S,T,K は実運用で高々数十。10万進法で衝突しない一意キー。
    private fun key(a: Int, b: Int): Long = a.toLong() * 100_000L + b
    private fun key3(a: Int, b: Int, c: Int): Long = (a.toLong() * 100_000L + b) * 100_000L + c

    fun build(p: Problem, schedule: Array<IntArray>): Index {
        val s = normalizeSchedule(schedule, p)
        val windows = C1RepairAnalysis.analyze(p, s)

        val dayToWindows = HashMap<Int, MutableList<C1RepairAnalysis.C1WindowViolation>>()
        val staffRule = HashMap<Long, MutableList<C1RepairAnalysis.C1WindowViolation>>()
        for (w in windows) {
            for (d in w.start until w.start + w.windowDays) dayToWindows.getOrPut(d) { ArrayList() }.add(w)
            staffRule.getOrPut(key(w.staff, w.ruleIndex)) { ArrayList() }.add(w)
        }

        // expectedGain: 不足窓ごとの候補日の局所 gain（opportunities と同一定義）。実際に置けない
        //   （希望固定/禁止連続）候補は除外（=そのセルの gain には寄与しない）。
        val gain = HashMap<Long, Int>()
        for (w in windows) {
            for (opp in C1RepairAnalysis.opportunities(p, s, w)) {
                if (opp.wishConflict || opp.patternRisk) continue
                // [3.279.0/C1-07] キーに対象シフトを含める（同日別シフトの gain 混合を解消）。
                val k = key3(w.staff, opp.day, w.shift)
                if (opp.gain > (gain[k] ?: 0)) gain[k] = opp.gain
            }
        }

        // donorMargin: 各セル(i,d)が現在保持する c1シフト x について、d を含む全窓の (z - 必要数) の最小。
        //   >0 なら x@d を抜いてもどの窓も割れない＝安全ドナー。x が c1規則を持たないセルは donor に載せない
        //   （引くと呼出側は無限大＝常に安全と解釈）。
        val donor = HashMap<Long, Int>()
        for (i in 0 until p.S) {
            for (d in 0 until p.T) {
                val x = s[i][d]
                var margin = Int.MAX_VALUE
                for (c in p.cons1) {
                    if (c.shiftIdx != x || c.day1 !in 1..p.T || c.day2 < 1) continue
                    if (!p.canDo(i, x)) continue
                    val lo = (d - c.day1 + 1).coerceAtLeast(0)
                    val hi = d.coerceAtMost(p.T - c.day1)
                    var ws = lo
                    while (ws <= hi) {
                        var z = 0
                        for (l in 0 until c.day1) if (s[i][ws + l] == x) z++
                        val slack = z - c.day2
                        if (slack < margin) margin = slack
                        ws++
                    }
                }
                if (margin != Int.MAX_VALUE) donor[key(i, d)] = margin
            }
        }

        return Index(windows, dayToWindows, staffRule, gain, donor)
    }
}
