package com.magi.app.v6

import com.magi.app.model.MagiState

/**
 * C1時系列DP研磨（`C1TemporalSwapPolish`）の実現ステップを、同日2人swapから
 * `FlexibleDayFlow`（3.245.0, RangePolish手Fで使用）による同日全員参加の最小費用再割当へ拡張する。
 *
 * `C1TemporalDp` が求める「対象シフトか否かの月内最適二値列」自体は正しいが、旧`C1TemporalSwapPolish`
 * はその変更日を「厳密に相補的なシフトを持つ1人との同日swap」でしか実現できず、そのような相手が
 * 存在しない日ではDPの改善が丸ごと死んでいた（実測: golden_state.jsonでDP単体寄与0%を確認）。
 * 本パスは同じDP出力を使い、各変更日を「対象職員をtarget/非targetへ強制し、他の全職員は
 * `FlexibleDayFlow`が費用最小で再配置する」同日ジョイント再割当で実現する。covU/covO(被覆)は
 * `shiftMarginalCost`、staffRange/apt(回数)は`staffShiftCost`に組み込み済みのため、C1改善に伴う
 * 被覆・回数への副作用はこの日次ソルバー自身が最小化する。禁止連続(c3n)は候補セルの事前フィルタで回避。
 *
 * 日ごとの独立最適化のため月全体での厳密最適解ではないが、既存の同日swap限定より確実に広い（同日swapは
 * この解の特殊ケース＝実現可能な集合の真部分集合）。最終採否は必ず`UnifiedViolationChecker`と
 * hard→weightedScore→totalのkeep-bestで行う（退化不能）。
 */
internal object C1TemporalFlowPolish {
    private data class Plan(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val staff: Int,
        val shift: Int,
        val relocations: Int,
        val daysTouched: Int,
    )

    fun apply(
        state: MagiState,
        schedule: Array<IntArray>,
        maxPasses: Int = 2,
        maxRelocations: Int = 4,
        trials: Int = 4,
        shouldStop: () -> Boolean = { false },
        seed: Long = 0xC1F10FL,
    ): V6HotfixPasses.CyclicSwapResult {
        val p = Problem(state)
        var work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        var rowNoGain = 0                       // 行の c1 が減らず候補にならなかった数
        val rejects = RejectCulpritStats()      // 目的関数/ピンで落ちた候補の内訳
        // [3.359.0] 「目的関数は採用を認めたのにピンだけが止めた」候補の**対象(職員,シフト)**を集める。
        //   実データでの規模は小さい（real で6件・他は0）が、UI の「回数の固定について」一覧の
        //   計測範囲を、このパスについても実装どおりにするための配線。
        val pinBlocks = PinBlockAttribution()
        var dpCandidates = 0
        var flowFailures = 0
        val fixedLabels = ArrayList<String>()

        if (p.cons1.isEmpty()) {
            return V6HotfixPasses.CyclicSwapResult(
                work, before.total, before.total, 0,
                listOf(MirrorLog(tag = "C1TemporalFlow", message = "cons1なし=スキップ")),
            )
        }

        val rulesByShift = LinkedHashMap<Int, MutableList<C1TemporalDp.Rule>>()
        for (c in p.cons1) {
            if (c.shiftIdx !in 0 until p.K || c.day1 <= 0 || c.day2 <= 0) continue
            rulesByShift.getOrPut(c.shiftIdx) { ArrayList() }.add(C1TemporalDp.Rule(c.day1, c.day2))
        }

        fun better(a: ViolationReport, b: ViolationReport): Boolean {
            return betterReport(a, b)  // [3.287.0 keep-best統一] hard→weighted→total（MirrorCore.betterReport）
        }

        fun rowC1Fires(s: Array<IntArray>, i: Int): Int {
            var out = 0
            for (c in p.cons1) {
                val x = c.shiftIdx; val d = c.day1
                if (x !in 0 until p.K || d <= 0 || d > p.T || !p.canDo(i, x)) continue
                var count = 0
                for (j in 0 until d) if (s[i][j] == x) count++
                if (count < c.day2) out++
                var start = 1
                while (start <= p.T - d) {
                    if (s[i][start - 1] == x) count--
                    if (s[i][start + d - 1] == x) count++
                    if (count < c.day2) out++
                    start++
                }
            }
            return out
        }

        // 日 j の全職員を同時再割当する。forcedStaff は disallow に含まれるシフトへは行けない
        // （target化なら disallow=対象外全部、非target化なら disallow={対象シフト}）。
        // 他の職員は staffRange/apt(回数)+covU/covO(被覆)の合計費用最小へ FlexibleDayFlow が解く。
        // board は判定基準の盤面（累積中のtrialWork）を明示的に受け取る（暗黙のwork捕捉を避ける）。
        fun solveDay(board: Array<IntArray>, j: Int, forcedStaff: Int, disallow: Set<Int>, trialSeed: Long): IntArray? {
            val oldDay = IntArray(p.S) { board[it][j] }
            val counts = Array(p.S) { IntArray(p.K) }
            for (i in 0 until p.S) for (jj in 0 until p.T) {
                val kk = board[i][jj]; if (kk in 0 until p.K) counts[i][kk]++
            }
            fun rangeAndAptCost(i: Int, oldK: Int, newK: Int): Long {
                var out = 0L
                for (kk in 0 until p.K) {
                    var c = counts[i][kk]
                    if (newK != oldK) {
                        if (kk == oldK) c--
                        if (kk == newK) c++
                    }
                    val lo = p.rangeLo[i][kk]; val hi = p.rangeHi[i][kk]
                    if (lo != Int.MIN_VALUE && c < lo) out += (lo - c).toLong() * 90L
                    if (hi != Int.MAX_VALUE && c > hi) out += (c - hi).toLong() * 45L
                    val a = p.apt[i][kk]
                    if (a >= 0) out += kotlin.math.abs(c - a).toLong()
                }
                if (newK != oldK) out += 2L
                return out
            }
            fun dayPenalty(k: Int, q: Int): Long =
                p.covUCell(k, j, q).toLong() * 8000L + p.covOCell(k, j, q).toLong()

            val staffCost = Array(p.S) { LongArray(p.K) { FlexibleDayFlow.INF } }
            for (i in 0 until p.S) {
                val oldK = oldDay[i]
                for (newK in 0 until p.K) {
                    if (i == forcedStaff && newK in disallow) continue
                    val changed = newK != oldK
                    if (changed) {
                        // [3.415.0] 記号「希」を割当先から外すガードを撤去（詳細は V6HotfixPasses の同種箇所）。
                        if (p.wishLocked(i, j) || !p.canDo(i, newK)) continue
                        board[i][j] = newK
                        val bad = p.makesForbiddenRun(board, i, j, newK)
                        board[i][j] = oldK
                        if (bad) continue
                    }
                    val primary = rangeAndAptCost(i, oldK, newK)
                    val tie = ((i.toLong() * 131 + newK.toLong() * 31 + trialSeed) and 1023L)
                    staffCost[i][newK] = primary * 1024L + tie
                }
            }
            if ((0 until p.K).none { it !in disallow && staffCost[forcedStaff][it] < FlexibleDayFlow.INF / 2 }) {
                return null   // forcedStaffに実現可能な行先が無い
            }
            val marginal = Array(p.K) { k ->
                LongArray(p.S) { q0 -> val q = q0 + 1; (dayPenalty(k, q) - dayPenalty(k, q - 1)) * 1024L }
            }
            val solved = FlexibleDayFlow.solve(staffCost, marginal) ?: return null
            if (solved.assignment[forcedStaff] in disallow) return null
            return solved.assignment
        }

        fun buildPlan(i: Int, x: Int, candidate: C1TemporalDp.Candidate, trialSeed: Long): Plan? {
            val changedDays = (0 until p.T).filter { j -> candidate.targetDays[j] != (work[i][j] == x) }
            if (changedDays.isEmpty()) return null
            var trialWork = work.copy2D()
            for (j in changedDays) {
                if (shouldStop()) return null
                val wantsX = candidate.targetDays[j]
                val disallow = if (wantsX) (0 until p.K).filter { it != x }.toSet() else setOf(x)
                val assignment = solveDay(trialWork, j, i, disallow, trialSeed xor j.toLong())
                if (assignment == null) { flowFailures++; return null }
                val next = trialWork.copy2D()
                for (s in 0 until p.S) next[s][j] = assignment[s]
                trialWork = next
            }
            val newRowFires = rowC1Fires(trialWork, i)
            if (newRowFires >= rowC1Fires(work, i)) { rowNoGain++; return null }
            val rep = UnifiedViolationChecker.check(state, trialWork)
            // [3.356.0/実機ログ起因] 旧ログは「DP候補12 flow失敗0 採用0回」までで、12件が
            //   ①行のc1が減らない ②目的関数に負けた ③厳密ピンを崩す のどれで落ちたかが読めなかった。
            //   判定の順序は変えず（better → ピン）、落ちた理由だけを数える。
            val ok = better(rep, bestRep)
            // [厳密ピン保護] 他職員のジョイント再割当(FlexibleDayFlow)がstaffRange厳密ピンを崩す
            // 副作用は、total/weightedScoreが改善してもここで拒否する（keep-best不変・追加ガードのみ）。
            val pinBad = ok && pinBlocks.blocksImproving(p, work, trialWork)
            if (!ok || pinBad) { rejects.record(rep, bestRep, pinBad); return null }
            return Plan(trialWork, rep, i, x, candidate.relocations, changedDays.size)
        }

        var pass = 0
        while (pass < maxPasses && !shouldStop()) {
            var improved = false
            for (i in 0 until p.S) {
                if (shouldStop()) break
                if (rowC1Fires(work, i) == 0) continue
                var bestForStaff: Plan? = null
                for ((x, rules) in rulesByShift) {
                    if (shouldStop()) break
                    if (!p.canDo(i, x)) continue
                    val focusBefore = C1TemporalDp.countFires(work[i], x, rules)
                    if (focusBefore == 0) continue
                    val locked = BooleanArray(p.T) { j -> p.wishLocked(i, j) }
                    for (trial in 0 until trials.coerceAtLeast(1)) {
                        if (shouldStop()) break
                        val trialSeed = seed xor (i.toLong() shl 32) xor (x.toLong() shl 16) xor
                            (pass.toLong() shl 8) xor trial.toLong()
                        val cand = C1TemporalDp.solve(
                            work[i], x, rules, locked, maxRelocations = maxRelocations, seed = trialSeed,
                        ) ?: continue
                        dpCandidates++
                        val plan = buildPlan(i, x, cand, trialSeed) ?: continue
                        val old = bestForStaff
                        if (old == null || better(plan.report, old.report) ||
                            (plan.report.hard == old.report.hard && plan.report.total == old.report.total &&
                                kotlin.math.abs(plan.report.weightedScore - old.report.weightedScore) <= 1e-9 &&
                                plan.daysTouched < old.daysTouched)
                        ) bestForStaff = plan
                    }
                }
                val chosen = bestForStaff ?: continue
                work = chosen.schedule.copy2D()
                bestRep = chosen.report
                applied++
                improved = true
                val name = state.staff.getOrNull(chosen.staff)?.name ?: "#${chosen.staff}"
                val symbol = state.shifts.getOrNull(chosen.shift)?.kigou ?: chosen.shift.toString()
                fixedLabels.add("$name $symbol(${chosen.relocations}移設/${chosen.daysTouched}日ジョイント再割当)")
            }
            pass++
            if (!improved) break
        }

        val logs = listOf(
            MirrorLog(
                tag = "C1TemporalFlow",
                message = "期間要件(c1)時系列DP+ジョイント再割当研磨: c1 ${before.breakdown["c1"] ?: 0}->" +
                    "${bestRep.breakdown["c1"] ?: 0} / total ${before.total}->${bestRep.total} " +
                    "HARD ${before.hard}->${bestRep.hard} 採用${applied}回 DP候補$dpCandidates " +
                    "flow失敗$flowFailures" +
                    (if (rowNoGain > 0) " 行c1が減らず${rowNoGain}件" else "") +
                    rejects.summary() +
                    (if (fixedLabels.isNotEmpty()) " 対象: ${fixedLabels.joinToString(", ")}" else "") +
                    (if (applied == 0 && (before.breakdown["c1"] ?: 0) > 0) " [頭打ち=ジョイント再割当解なし]" else ""),
            ),
        )
        return V6HotfixPasses.CyclicSwapResult(
            work, before.total, bestRep.total, applied, logs,
            observedPinBlockedAttempts = pinBlocks.attempts, pinBlocks = pinBlocks,
        )
    }
}
