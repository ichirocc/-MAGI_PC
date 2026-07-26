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
object LightMirrorOptimizer {
    fun optimize(state: MagiState, initial: Array<IntArray>, seconds: Double, seed: Long = 42L): LightOptimizeResult {
        val t0 = System.nanoTime()
        val rng = Random(seed)
        val p = Problem(state)
        // [レビュー#4 3.213.0] 希望の凍結規則をエンジン本体と統一。
        //   旧: lockedMatrix(p)=canDo 無視で全希望をロック＋事前適用なし＝(a)実現可能だが未充足の希望が
        //   永久に直らない (b)実現不能希望が凍結され他制約の修復を妨げる、の両方を抱えていた。
        //   wishLocked（実現可能な希望のみ凍結・MirrorCore 監査#11①）へ統一し、凍結する希望は
        //   最初に盤面へ適用してから探索する（pref=HARD の凍結セルを最初から充足させる）。
        var bestSchedule = normalizeSchedule(initial, p)
        val locked = Array(p.S) { i -> BooleanArray(p.T) { j -> p.wishLocked(i, j) } }
        for (i in 0 until p.S) for (j in 0 until p.T) if (locked[i][j]) bestSchedule[i][j] = p.wish[i][j]
        var curSchedule = bestSchedule.copy2D()
        var curReport = UnifiedViolationChecker.check(state, curSchedule)
        var bestReport = curReport
        var iters = 0L
        var accepts = 0L
        var lastImprove = 0L
        val safeSeconds = max(0.1, seconds)
        val deadline = t0 + (safeSeconds * 1_000_000_000L).toLong()

        while (System.nanoTime() < deadline) {
            iters++
            val i = rng.nextInt(p.S)
            val j = rng.nextInt(p.T)
            if (locked[i][j]) continue
            val old = curSchedule[i][j]
            val allowed0 = p.allowedShiftsForStaff(i)
            val candidates = ArrayList<Int>()
            for (kk in allowed0) {
                if (kk != old) candidates.add(kk)
            }
            if (candidates.isEmpty()) continue

            val nw = candidates[rng.nextInt(candidates.size)]
            curSchedule[i][j] = nw
            val trialReport = UnifiedViolationChecker.check(state, curSchedule)
            var accept = isBetter(trialReport, curReport)
            if (!accept) {
                val elapsed = (System.nanoTime() - t0) / 1_000_000_000.0
                val temp = max(0.05, 1.0 - elapsed / safeSeconds)
                val delta = trialReport.weightedScore - curReport.weightedScore
                val denom = 80.0 * temp + 1e-9
                if (delta < 150.0 && rng.nextDouble() < exp(-max(0.0, delta) / denom)) {
                    accept = true
                }
            }
            if (accept) {
                curReport = trialReport
                accepts++
                if (isBetter(curReport, bestReport)) {
                    bestReport = curReport
                    bestSchedule = curSchedule.copy2D()
                    lastImprove = iters
                }
            } else {
                curSchedule[i][j] = old
            }
        }

        val finalReport0 = UnifiedViolationChecker.check(state, bestSchedule)
        val elapsedMs = ((System.nanoTime() - t0) / 1_000_000L)
        val scoreText = String.format(Locale.US, "%.1f", finalReport0.weightedScore)
        val log = MirrorLog(
            iter = iters,
            tag = "LightOptimize",
            message = "軽量最適化完了: HARD=${finalReport0.hard} total=${finalReport0.total} score=$scoreText iter=$iters accept=$accepts lastImprove=$lastImprove (${elapsedMs}ms)",
        )
        val logs = ArrayList<MirrorLog>()
        logs.add(log)
        logs.addAll(finalReport0.logs)
        return LightOptimizeResult(bestSchedule, finalReport0.copy(logs = logs), iters, accepts, elapsedMs)
    }

    private fun isBetter(a: ViolationReport, b: ViolationReport): Boolean {
        return betterReport(a, b)  // [3.287.0 keep-best統一] hard→weighted→total（MirrorCore.betterReport）
    }
}
