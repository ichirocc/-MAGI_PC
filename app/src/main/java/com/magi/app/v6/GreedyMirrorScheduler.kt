package com.magi.app.v6

import com.magi.app.model.MagiState
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.Random
import kotlin.math.exp
import kotlin.math.max

/**
 * Kotlin port of magi_python_mirror.py.
 *
 * The high-speed native SA remains the main optimizer, but this module brings the
 * mirror app's operational layer into the Android app: unified violation breakdown,
 * greedy/simple schedule creation, light local search, and CSV round-trip helpers.
 */
object GreedyMirrorScheduler {
    fun generate(state: MagiState): ScheduleRunResult {
        val t0 = System.nanoTime()
        val p = Problem(state)
        if (p.T <= 0 || p.S <= 0 || p.K <= 0) throw IllegalArgumentException("期間/職員/シフトが不足しています")
        val restK = restShiftIndex(state)
        val existing = state.schedule.toIntArray2D()
        var filled = 0
        for (row in existing) for (v in row) if (v >= 0) filled++
        val schedule: Array<IntArray>
        val baseMode: String
        var wishIn = 0
        var wishOut = 0
        if (filled >= max(1, p.S * p.T / 2)) {
            schedule = normalizeSchedule(existing, p)
            baseMode = "既存表ベース"
        } else {
            schedule = Array(p.S) { IntArray(p.T) { -1 } }
            baseMode = "空表ベース"
            for (i in 0 until p.S) for (j in 0 until p.T) {
                val w = p.wish[i][j]
                if (w !in 0 until p.K) continue
                // [3.391.0] 旧: 担当できないシフトへの希望まで**盤面へ置いていた**。pref は実現可能な
                //   希望しか数えない（MirrorCore）ので置いても pref は1点も得しない一方、担当外セル＝
                //   groupViol(HARD 10000) が確実に立つ＝純損。SmartInitialScheduler は同じ処理で
                //   canDo を見ており（3.257.0）、この旧世代の生成器だけが取り残されていた。
                if (p.canDo(i, w)) { schedule[i][j] = w; wishIn++ } else wishOut++
            }
        }

        var counts = countMatrix(p, schedule)
        for (i in 0 until p.S) {
            val allowed = p.allowedShiftsForStaff(i)
            val free = ArrayList<Int>()
            for (jj in 0 until p.T) if (schedule[i][jj] < 0) free.add(jj)
            var pos = 0
            for (k in allowed) {
                val lo = p.rangeLo[i][k].takeIf { it != Int.MIN_VALUE } ?: 0
                var need = max(0, lo - counts[i][k])
                while (need > 0 && pos < free.size) {
                    val j = free[pos++]
                    schedule[i][j] = k
                    counts[i][k]++
                    need--
                }
            }
        }

        // [need2単独定義セル見落とし修正] need1のみでなくcovUCell(need1/need2のOR、source of truth)を使う
        //   （3.173.0のCoverageDiagnosis修正と同根＝SmartInitialSchedulerと同一パターンで同時修正）。
        counts = countMatrix(p, schedule)
        var cov = coverage(p, schedule)
        for (j in 0 until p.T) {
            val demandOrder = ArrayList<Pair<Int, Int>>()
            for (k in 0 until p.K) {
                val deficit = p.covUCell(k, j, cov[j][k])
                if (deficit > 0) demandOrder.add(deficit to k)
            }
            demandOrder.sortWith { a, b ->
                val d = b.first.compareTo(a.first)
                if (d != 0) d else a.second.compareTo(b.second)
            }
            for (pair in demandOrder) {
                val k = pair.second
                while (p.covUCell(k, j, cov[j][k]) > 0) {
                    var bestI = -1
                    var bestPenalty = Int.MAX_VALUE
                    for (i in 0 until p.S) {
                        if (schedule[i][j] >= 0 || !p.canDo(i, k)) continue
                        val hi = p.rangeHi[i][k]
                        val over = hi != Int.MAX_VALUE && counts[i][k] >= hi
                        val penalty = (if (over) 1000 else 0) + counts[i][k] * 2
                        if (penalty < bestPenalty) {
                            bestPenalty = penalty
                            bestI = i
                        }
                    }
                    if (bestI < 0) break
                    schedule[bestI][j] = k
                    counts[bestI][k]++
                    cov[j][k]++
                }
            }
        }

        counts = countMatrix(p, schedule)
        for (i in 0 until p.S) {
            val allowed = p.allowedShiftsForStaff(i)
            for (j in 0 until p.T) {
                if (schedule[i][j] >= 0) continue
                var bestK = allowed.firstOrNull() ?: restK
                var bestPenalty = Int.MAX_VALUE
                for (k in allowed) {
                    val hi = p.rangeHi[i][k]
                    val over = hi != Int.MAX_VALUE && counts[i][k] >= hi
                    var covNow = 0
                    for (ii in 0 until p.S) if (schedule[ii][j] == k) covNow++
                    // [need2単独定義セル見落とし修正] SmartInitialSchedulerと同根・同時修正。
                    val demandBonus = if (p.covUCell(k, j, covNow) > 0) -100 else 0
                    // [3.345.0] 休は通常のシフト種の一つ＝残り埋めで優先しない（旧: 休だけ -10 のボーナス）。
                    //   実データ3件で hard/covO/covU/low/high/c1 が全て同一＝この優先は実質不活性だった。
                    val penalty = (if (over) 1000 else 0) + counts[i][k] + demandBonus
                    if (penalty < bestPenalty) {
                        bestPenalty = penalty
                        bestK = k
                    }
                }
                schedule[i][j] = bestK
                counts[i][bestK]++
            }
        }

        val report = UnifiedViolationChecker.check(state, schedule)
        val elapsedMs = ((System.nanoTime() - t0) / 1_000_000L)
        val log = MirrorLog(
            tag = "GenerateInitial",
            message = "簡易作成完了($baseMode): HARD=${report.hard} total=${report.total} 希望seed=${wishIn}件/担当外=${wishOut}件 (${elapsedMs}ms)",
        )
        return ScheduleRunResult(schedule, report.copy(logs = listOf(log) + report.logs))
    }
}
