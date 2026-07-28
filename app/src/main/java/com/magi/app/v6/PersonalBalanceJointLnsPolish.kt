package com.magi.app.v6

import com.magi.app.model.MagiState
import java.util.Random
import kotlin.math.max

/**
 * 個人回数(range)と適切回数(apt)を同時に直す focused LNS。
 *
 * 単独の RangePolish/AptPolish は、本人の1セル変更で生じる同日coverage不足や他職員の
 * range/apt副作用を修復する前に候補を捨てやすい。本パスは、個人ペナルティが下がる割当変更と
 * 同日coverage玉突き(findCovUChain)を一つの候補として構成し、必要なら複数候補をdebt付きbeamで束ねる。
 *
 * 特に、専門シフトのapt不足と別シフトのrange/apt超過が同一職員に共存するケースを優先する。
 * 例: 大島愛  Aｱ過剰 + 休過剰 + Pｼ不足。Aｱ→Pｼで本人の3族を同時改善し、空いたAｱは
 * 同日玉突きで補充する。
 *
 * 目標値の総和が月日数を超える等、apt違反が構造的に不可避な職員については、希望固定と
 * range/aptだけを用いた厳密count-DP下限を計算する。下限到達済みの違反を無駄に追わず、
 * 同じ下限値の別配置が正式目的を改善する場合だけ移し替える。
 */
internal object PersonalBalanceJointLnsPolish {
    data class Config(
        val beamWidth: Int = 16,
        val maxDepth: Int = 4,
        val maxRestarts: Int = 3,
        val maxFocusStaff: Int = 6,
        val maxGoals: Int = 28,
        val maxVariantsPerGoal: Int = 8,
        val hardDebt: Int = 1,
        val totalDebt: Int = 16,
        val personalDebt: Int = 4,
        val maxMillis: Long = 6_000L,
    )

    private data class Goal(
        val staff: Int,
        val day: Int,
        val target: Int,
        val marginal: Int,
        val weight: Int,
        val reason: String,
    )

    private data class CellOp(val staff: Int, val day: Int, val shift: Int)

    private data class Candidate(
        val schedule: Array<IntArray>,
        val ops: List<CellOp>,
        val label: String,
    )

    private data class Node(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val personal: IntArray,
        val focusTotal: Int,
        val path: List<String>,
        val changedCells: Int,
    )

    private data class Seen(val schedule: Array<IntArray>)

    fun apply(
        state: MagiState,
        schedule: Array<IntArray>,
        config: Config = Config(),
        shouldStop: () -> Boolean = { false },
        seed: Long = 0xA97B4L,
    ): V6HotfixPasses.CyclicSwapResult {
        val p = Problem(state)
        val rootSchedule = normalizeSchedule(schedule, p)
        val rootReport = UnifiedViolationChecker.check(state, rootSchedule)
        if (p.S <= 0 || p.T <= 0 || p.K <= 0) return noOp(rootSchedule, rootReport, "対象なし")
        if (config.beamWidth <= 0 || config.maxDepth <= 0 || config.maxRestarts <= 0 ||
            config.maxFocusStaff <= 0 || config.maxGoals <= 0 || config.maxVariantsPerGoal <= 0 ||
            config.maxMillis <= 0L
        ) return noOp(rootSchedule, rootReport, "探索上限0=明示的に無効")

        val rootPersonal = personalPenaltyByStaff(p, rootSchedule)
        val lower = IntArray(p.S) { staffLowerBound(p, it) }
        val focus = chooseFocusStaff(p, rootSchedule, rootPersonal, lower, config.maxFocusStaff)
        if (focus.isEmpty()) return noOp(rootSchedule, rootReport, "range/apt対象なし")

        val rootFocus = focus.sumOf { rootPersonal[it] }
        val budgetMillis = config.maxMillis.coerceAtMost(60_000L)
        val deadline = System.nanoTime() + budgetMillis * 1_000_000L
        fun stopped(): Boolean = shouldStop() || System.nanoTime() >= deadline

        val root = Node(rootSchedule.copy2D(), rootReport, rootPersonal, rootFocus, emptyList(), 0)
        var best = root
        var generated = 0
        var expanded = 0
        var debtRejected = 0
        var duplicateRejected = 0
        var restartsDone = 0

        for (restart in 0 until config.maxRestarts) {
            if (stopped() || focus.all { best.personal[it] <= lower[it] }) break
            restartsDone++
            val rng = Random(seed xor (restart.toLong() * -0x61c8864680b583ebL))
            var beam = if (best === root) listOf(root) else listOf(root, best)
            val seen = HashMap<Long, MutableList<Seen>>()
            for (n in beam) remember(seen, n.schedule)

            for (depth in 0 until config.maxDepth) {
                if (stopped()) break
                val children = ArrayList<Node>()
                for (parent in beam) {
                    if (stopped()) break
                    expanded++
                    val goals = collectGoals(p, parent.schedule, focus, lower, config.maxGoals, rng)
                    for (goal in goals) {
                        if (stopped()) break
                        val variants = buildCandidates(
                            p, parent.schedule, goal, config.maxVariantsPerGoal, rng,
                        )
                        for (candidate in variants) {
                            if (stopped()) break
                            generated++
                            val report = UnifiedViolationChecker.check(state, candidate.schedule)
                            val personal = personalPenaltyByStaff(p, candidate.schedule)
                            val focusTotal = focus.sumOf { personal[it] }
                            if (report.hard > rootReport.hard + config.hardDebt.coerceAtLeast(0) ||
                                report.total > rootReport.total + config.totalDebt.coerceAtLeast(0) ||
                                focusTotal > rootFocus + config.personalDebt.coerceAtLeast(0)
                            ) {
                                debtRejected++
                                continue
                            }
                            if (!remember(seen, candidate.schedule)) {
                                duplicateRejected++
                                continue
                            }
                            val child = Node(
                                candidate.schedule,
                                report,
                                personal,
                                focusTotal,
                                parent.path + candidate.label,
                                changedCellCount(rootSchedule, candidate.schedule),
                            )
                            children.add(child)
                            if (isFinalCandidate(p, child, root, focus)) {
                                if (best === root || betterFinal(child, best, focus, lower)) best = child
                            }
                        }
                    }
                }
                if (children.isEmpty()) break
                beam = selectBeam(children, rootReport, focus, lower, config.beamWidth, rng)
            }
        }

        val checked = UnifiedViolationChecker.check(state, best.schedule)
        val checkedPersonal = personalPenaltyByStaff(p, best.schedule)
        // [receiving-code-review] focusTotal は「悪化させない(<=)」まで緩和。以前は狭義減少(<)を
        // 要求しており、docstring が明記する「下限到達済みの違反は、同じ下限値の別配置が正式目的
        // (better())を改善する場合だけ移し替える」ケース（personal合計は不変・total等は改善）を
        // 機械的に拒否していた。best自体は better() で選ばれた真に改善する解であり、focus側は
        // 「その改善の副作用でfocus対象が悪化していないか」だけを見れば十分（primary固有の狭義
        // 改善要求は focus.all の悪化なしチェックと重複するため撤去）。
        val valid = best !== root && better(checked, rootReport) &&
            focus.sumOf { checkedPersonal[it] } <= rootFocus &&
            focus.all { checkedPersonal[it] <= rootPersonal[it] } &&
            !exactPinRegression(p, rootSchedule, best.schedule)

        val chosen = if (valid) best.schedule.copy2D() else rootSchedule.copy2D()
        val chosenReport = if (valid) checked else rootReport
        val chosenPersonal = if (valid) checkedPersonal else rootPersonal
        val focusText = focus.joinToString(", ") { i ->
            val name = state.staff.getOrNull(i)?.name ?: "#$i"
            val suffix = if (chosenPersonal[i] <= lower[i]) "=下限" else ""
            "$name ${rootPersonal[i]}->${chosenPersonal[i]}(下限${lower[i]}$suffix)"
        }
        val reason = when {
            valid && focus.all { chosenPersonal[it] <= lower[it] } -> "個人構造下限到達"
            shouldStop() -> "外部停止"
            System.nanoTime() >= deadline -> "期限"
            else -> "探索停滞"
        }
        val log = MirrorLog(
            tag = "PersonalJointLNS",
            message = "個人回数/apt共同LNS: personal $rootFocus->${focus.sumOf { chosenPersonal[it] }}" +
                " / low ${rootReport.breakdown["low"] ?: 0}->${chosenReport.breakdown["low"] ?: 0}" +
                " high ${rootReport.breakdown["high"] ?: 0}->${chosenReport.breakdown["high"] ?: 0}" +
                " apt ${rootReport.breakdown["apt"] ?: 0}->${chosenReport.breakdown["apt"] ?: 0}" +
                " / total ${rootReport.total}->${chosenReport.total} HARD ${rootReport.hard}->${chosenReport.hard}" +
                " 採用${if (valid) 1 else 0}束 手数${if (valid) best.path.size else 0}" +
                " restart$restartsDone 展開$expanded 候補$generated debt除外$debtRejected 重複除外$duplicateRejected" +
                " 停止=$reason 対象: $focusText" +
                (if (valid) " 経路: ${best.path.joinToString("+")}" else " [頭打ち=正式目的を改善する個人違反減少束なし]"),
        )
        return V6HotfixPasses.CyclicSwapResult(
            chosen, rootReport.total, chosenReport.total, if (valid) 1 else 0, listOf(log),
        )
    }

    private fun noOp(
        schedule: Array<IntArray>, report: ViolationReport, reason: String,
    ): V6HotfixPasses.CyclicSwapResult = V6HotfixPasses.CyclicSwapResult(
        schedule.copy2D(), report.total, report.total, 0,
        listOf(MirrorLog(tag = "PersonalJointLNS", message = reason)),
    )

    private fun chooseFocusStaff(
        p: Problem,
        schedule: Array<IntArray>,
        current: IntArray,
        lower: IntArray,
        limit: Int,
    ): IntArray {
        val improving = (0 until p.S)
            .filter { current[it] > lower[it] }
            .sortedWith(
                compareByDescending<Int> { current[it] - lower[it] }
                    .thenByDescending { current[it] }
                    .thenBy { it },
            )
        val unavoidableExclusive = (0 until p.S)
            .filter { current[it] > 0 && current[it] <= lower[it] && hasExclusiveAptViolation(p, schedule, it) }
            .sortedWith(compareByDescending<Int> { current[it] }.thenBy { it })
        return (improving + unavoidableExclusive).distinct().take(limit).toIntArray()
    }

    private fun hasExclusiveAptViolation(p: Problem, schedule: Array<IntArray>, staff: Int): Boolean {
        val counts = IntArray(p.K)
        for (j in 0 until p.T) {
            val k = schedule[staff][j]
            if (k in 0 until p.K) counts[k]++
        }
        for (k in 0 until p.K) {
            val target = p.apt[staff][k]
            if (target >= 0 && counts[k] != target && p.staffForShift[k].size == 1) return true
        }
        return false
    }

    internal fun personalPenaltyByStaff(p: Problem, schedule: Array<IntArray>): IntArray {
        val counts = Array(p.S) { IntArray(p.K) }
        for (i in 0 until p.S) for (j in 0 until p.T) {
            val k = schedule[i][j]
            if (k in 0 until p.K) counts[i][k]++
        }
        return IntArray(p.S) { i -> countPenalty(p, i, counts[i]) }
    }

    private fun countPenalty(p: Problem, staff: Int, counts: IntArray): Int {
        var total = 0
        for (k in 0 until p.K) {
            val c = counts[k]
            val lo = p.rangeLo[staff][k]
            val hi = p.rangeHi[staff][k]
            if (lo != Int.MIN_VALUE && c < lo) total += lo - c
            if (hi != Int.MAX_VALUE && c > hi) total += c - hi
            val target = p.apt[staff][k]
            if (target >= 0) total += kotlin.math.abs(c - target)
        }
        return total
    }

    /** 希望固定・担当可否・range/aptだけを使う、職員単位の厳密count下限。 */
    internal fun staffLowerBound(p: Problem, staff: Int): Int {
        val allowed = (0 until p.K).filter { p.canDo(staff, it) }
        if (allowed.isEmpty()) return 0
        val forced = IntArray(p.K)
        var fixed = 0
        for (j in 0 until p.T) {
            // [3.309.0] 旧実装は生の `w >= 0` で数えており、**担当できないシフトへの希望**まで
            //   「固定」として下限に織り込んでいた（この関数の docstring 自身が「担当可否も使う」と
            //   書いているのに、この分岐だけ canDo を見ていなかった）。規約は Problem.wishLocked
            //   ＝実現可能な希望だけが凍結される（3.264.0 / 3.270.0 / 3.278.0 と同じ retrofit）。
            if (!p.wishLocked(staff, j)) continue
            val w = p.wish[staff][j]
            if (w in 0 until p.K) {
                forced[w]++
                fixed++
            }
        }
        val free = (p.T - fixed).coerceAtLeast(0)
        val inf = 1_000_000
        var dp = IntArray(free + 1) { inf }
        dp[0] = 0
        for (k in allowed) {
            val next = IntArray(free + 1) { inf }
            for (used in 0..free) {
                val base = dp[used]
                if (base >= inf) continue
                for (extra in 0..free - used) {
                    val counts = IntArray(p.K)
                    counts[k] = forced[k] + extra
                    val cost = singleShiftPenalty(p, staff, k, counts[k])
                    val v = base + cost
                    if (v < next[used + extra]) next[used + extra] = v
                }
            }
            dp = next
        }
        // Penalties on non-allowed but wished shifts are not optimizable; include their fixed cost.
        var fixedOther = 0
        for (k in 0 until p.K) if (k !in allowed && forced[k] > 0) {
            fixedOther += singleShiftPenalty(p, staff, k, forced[k])
        }
        return if (dp[free] >= inf) 0 else dp[free] + fixedOther
    }

    private fun singleShiftPenalty(p: Problem, staff: Int, shift: Int, count: Int): Int {
        var v = 0
        val lo = p.rangeLo[staff][shift]
        val hi = p.rangeHi[staff][shift]
        if (lo != Int.MIN_VALUE && count < lo) v += lo - count
        if (hi != Int.MAX_VALUE && count > hi) v += count - hi
        val target = p.apt[staff][shift]
        if (target >= 0) v += kotlin.math.abs(count - target)
        return v
    }

    private fun collectGoals(
        p: Problem,
        schedule: Array<IntArray>,
        focus: IntArray,
        lower: IntArray,
        limit: Int,
        rng: Random,
    ): List<Goal> {
        // [監査で発見・3.270.0] normalizeSchedule はセンチネル -1 を作りうる（削除済シフトの残存index等）
        //   ため、生の schedule[i][j] を無検証で配列添字に使うとAIOOBEになりうる。ガード追加
        //   （同ファイル内 hasExclusiveAptViolation/personalPenaltyByStaff は既に同種のガード付き）。
        val counts = Array(p.S) { IntArray(p.K) }
        for (i in 0 until p.S) for (j in 0 until p.T) { val k = schedule[i][j]; if (k in 0 until p.K) counts[i][k]++ }
        val groups = ArrayList<MutableList<Goal>>()
        for (i in focus) {
            val before = countPenalty(p, i, counts[i])
            val list = ArrayList<Goal>()
            for (j in 0 until p.T) {
                if (p.wishLocked(i, j)) continue
                val old = schedule[i][j]
                if (old !in 0 until p.K) continue
                for (target in 0 until p.K) {
                    if (target == old || !p.canDo(i, target)) continue
                    counts[i][old]--
                    counts[i][target]++
                    val after = countPenalty(p, i, counts[i])
                    counts[i][target]--
                    counts[i][old]++
                    val marginal = after - before
                    val targetDef = targetDeficit(p, i, target, counts[i][target])
                    val sourceExcess = sourceExcess(p, i, old, counts[i][old])
                    val exclusive = if (p.staffForShift[target].size == 1) 40 else 0
                    val atLowerBound = before <= lower[i]
                    // 改善手を主対象。下限到達済みは、違反の置き場所を変えて他制約を改善できる
                    // marginal=0 の手だけ残す。一時+1はbeam debtで協調解を作るため少量許可。
                    if (marginal > 1) continue
                    if (marginal == 1 && targetDef == 0 && sourceExcess == 0) continue
                    if (marginal == 0 && !atLowerBound && targetDef == 0 && sourceExcess == 0) continue
                    val reason = when {
                        marginal < 0 -> "個人${-marginal}改善"
                        marginal == 0 -> "下限内移替"
                        else -> "一時debt"
                    }
                    val weight = (-marginal * 100) + targetDef * 30 + sourceExcess * 30 + exclusive
                    list.add(Goal(i, j, target, marginal, weight, reason))
                }
            }
            if (list.isNotEmpty()) groups.add(list.shuffled(rng).sortedByDescending { it.weight }.toMutableList())
        }
        val out = ArrayList<Goal>()
        while (out.size < limit && groups.any { it.isNotEmpty() }) {
            for (g in groups) {
                if (g.isNotEmpty()) out.add(g.removeAt(0))
                if (out.size >= limit) break
            }
        }
        return out
    }

    private fun targetDeficit(p: Problem, staff: Int, shift: Int, count: Int): Int {
        var v = 0
        val lo = p.rangeLo[staff][shift]
        if (lo != Int.MIN_VALUE && count < lo) v += lo - count
        val target = p.apt[staff][shift]
        if (target >= 0 && count < target) v += target - count
        return v
    }

    private fun sourceExcess(p: Problem, staff: Int, shift: Int, count: Int): Int {
        var v = 0
        val hi = p.rangeHi[staff][shift]
        if (hi != Int.MAX_VALUE && count > hi) v += count - hi
        val target = p.apt[staff][shift]
        if (target >= 0 && count > target) v += count - target
        return v
    }

    private fun buildCandidates(
        p: Problem,
        base: Array<IntArray>,
        goal: Goal,
        limit: Int,
        rng: Random,
    ): List<Candidate> {
        val i = goal.staff
        val j = goal.day
        val target = goal.target
        val old = base[i][j]
        if (old == target || p.wishLocked(i, j) || !p.canDo(i, target)) return emptyList()
        val out = ArrayList<Candidate>()

        // 同日1対1交換。coverageを完全保存するため最優先。
        val donors = (0 until p.S).shuffled(rng)
        for (d in donors) {
            if (d == i || base[d][j] != target || p.wishLocked(d, j) || !p.canDo(d, old)) continue
            val w = base.copy2D()
            w[i][j] = target
            w[d][j] = old
            if (p.makesForbiddenRun(base, i, j, target) || p.makesForbiddenRun(base, d, j, old)) continue
            out.add(Candidate(w, listOf(CellOp(i, j, target), CellOp(d, j, old)), "${goal.reason}:同日交換"))
            if (out.size >= max(1, limit / 3)) break
        }

        // 本人を直接 target へ移し、空いたoldがcovUを悪化させる場合だけ既存BFS玉突きで同日補充。
        run {
            val w = base.copy2D()
            if (!p.makesForbiddenRun(base, i, j, target)) {
                val beforeOld = (0 until p.S).count { base[it][j] == old }
                w[i][j] = target
                val needsRepair = p.covUCell(old, j, beforeOld - 1) > p.covUCell(old, j, beforeOld)
                val ops = ArrayList<CellOp>()
                ops.add(CellOp(i, j, target))
                var ok = true
                if (needsRepair) {
                    // [監査で発見・3.270.0] w は盤面全体のコピー。goal のセル(i,j)自体はcollectGoalsで
                    //   -1(センチネル)を除外済みだが、他のセル(s,day)は無関係な位置で-1が残っている
                    //   可能性がある。無検証添字はAIOOBEになりうるためガード。
                    val counts = Array(p.S) { s -> IntArray(p.K).also { a -> for (day in 0 until p.T) { val k = w[s][day]; if (k in 0 until p.K) a[k]++ } } }
                    val chain = findCovUChain(
                        p, w, old, j, rng, exclude = i,
                        rangeAvoid = { s, fill ->
                            val hi = p.rangeHi[s][fill]
                            (hi != Int.MAX_VALUE && counts[s][fill] >= hi) ||
                                (p.apt[s][fill] >= 0 && counts[s][fill] > p.apt[s][fill])
                        },
                    )
                    if (chain == null) ok = false else {
                        for (mv in chain) {
                            w[mv[0]][mv[1]] = mv[2]
                            ops.add(CellOp(mv[0], mv[1], mv[2]))
                        }
                    }
                }
                if (ok) out.add(Candidate(w, ops, "${goal.reason}:直接+coverage連鎖"))
            }
        }

        // 本人の別日targetと自己交換。月間回数は不変だが、下限内移替やc1/c3/weeklyの副作用改善に使う。
        for (d2 in (0 until p.T).shuffled(rng)) {
            if (d2 == j || base[i][d2] != target || p.wishLocked(i, d2) || !p.canDo(i, old)) continue
            val w = base.copy2D()
            w[i][j] = target
            w[i][d2] = old
            if (p.makesForbiddenRun(base, i, j, target) || p.makesForbiddenRun(base, i, d2, old)) continue
            out.add(Candidate(w, listOf(CellOp(i, j, target), CellOp(i, d2, old)), "${goal.reason}:自己日交換"))
            if (out.size >= limit) break
        }

        // クロス日token移送。本人の回数を改善しながら全体の月間shift総量を保存する。
        if (out.size < limit) {
            outer@ for (d in donors) for (d2 in (0 until p.T).shuffled(rng)) {
                if (d == i && d2 == j) continue
                if (base[d][d2] != target || p.wishLocked(d, d2) || !p.canDo(d, old)) continue
                val w = base.copy2D()
                w[i][j] = target
                w[d][d2] = old
                if (p.makesForbiddenRun(base, i, j, target) || p.makesForbiddenRun(base, d, d2, old)) continue
                out.add(Candidate(w, listOf(CellOp(i, j, target), CellOp(d, d2, old)), "${goal.reason}:クロス日移送"))
                if (out.size >= limit) break@outer
            }
        }
        return out.distinctBy { candidateKey(it.ops) }.take(limit)
    }

    private fun candidateKey(ops: List<CellOp>): String = ops
        .sortedWith(compareBy<CellOp> { it.staff }.thenBy { it.day }.thenBy { it.shift })
        .joinToString(";") { "${it.staff},${it.day},${it.shift}" }

    private fun isFinalCandidate(
        p: Problem,
        node: Node,
        root: Node,
        focus: IntArray,
    ): Boolean {
        if (!better(node.report, root.report)) return false
        // focusTotal は「悪化させない(<=)」まで緩和。狭義減少(<)のみを許すと、docstring が明記する
        // 「下限到達済みの職員は、personal合計が同じ別配置でも better() が改善するなら移し替える」
        // ケースを機械的に拒否してしまう（primary固有の狭義改善要求は次行の悪化なしチェックと
        // 重複するため撤去済み）。
        if (node.focusTotal > root.focusTotal) return false
        if (focus.any { node.personal[it] > root.personal[it] }) return false
        // [厳密ピン保護] focus外の職員(coverage連鎖の donor/receiver等)がstaffRange厳密ピンから
        // 遠ざかる副作用も拒否する（focusのみを見る上記チェックでは対象外のため追加）。
        if (exactPinRegression(p, root.schedule, node.schedule)) return false
        return true
    }

    private fun betterFinal(a: Node, b: Node, focus: IntArray, lower: IntArray): Boolean {
        // 採用候補間でも正式順序を最優先する。個人下限gapは同一report時のtie-breakだけ。
        if (better(a.report, b.report)) return true
        if (better(b.report, a.report)) return false
        val ag = focus.sumOf { (a.personal[it] - lower[it]).coerceAtLeast(0) }
        val bg = focus.sumOf { (b.personal[it] - lower[it]).coerceAtLeast(0) }
        if (ag != bg) return ag < bg
        return a.changedCells < b.changedCells
    }

    private fun better(a: ViolationReport, b: ViolationReport): Boolean {
        return betterReport(a, b)  // [3.287.0 keep-best統一] hard→weighted→total（MirrorCore.betterReport）
    }

    private fun selectBeam(
        children: List<Node>,
        root: ViolationReport,
        focus: IntArray,
        lower: IntArray,
        width: Int,
        rng: Random,
    ): List<Node> {
        val official = children.sortedWith(
            compareBy<Node> { it.report.hard }
                .thenBy { it.report.weightedScore }
                .thenBy { it.report.total }
                .thenBy { it.focusTotal }
                .thenBy { it.changedCells },
        ).take(max(1, width / 2))
        val personal = children.shuffled(rng).sortedWith(
            compareBy<Node> { (it.report.hard - root.hard).coerceAtLeast(0) }
                .thenBy { n -> focus.sumOf { (n.personal[it] - lower[it]).coerceAtLeast(0) } }
                .thenBy { (it.report.total - root.total).coerceAtLeast(0) }
                .thenBy { it.report.hard }
                .thenBy { it.report.weightedScore }
                .thenBy { it.report.total }
                .thenBy { it.changedCells },
        ).take(max(1, width - official.size))
        val out = ArrayList<Node>()
        for (n in official + personal) if (out.none { sameSchedule(it.schedule, n.schedule) }) out.add(n)
        return out.take(width)
    }

    private fun remember(seen: MutableMap<Long, MutableList<Seen>>, schedule: Array<IntArray>): Boolean {
        val h = scheduleHash(schedule)
        val bucket = seen.getOrPut(h) { ArrayList() }
        if (bucket.any { sameSchedule(it.schedule, schedule) }) return false
        bucket.add(Seen(schedule.copy2D()))
        return true
    }

    private fun scheduleHash(schedule: Array<IntArray>): Long {
        var h = -0x340d631b7bdddcdbL
        for (row in schedule) for (v in row) {
            h = h xor v.toLong()
            h *= 0x100000001b3L
        }
        return h
    }

    private fun sameSchedule(a: Array<IntArray>, b: Array<IntArray>): Boolean {
        if (a.size != b.size) return false
        for (i in a.indices) if (!a[i].contentEquals(b[i])) return false
        return true
    }

    private fun changedCellCount(root: Array<IntArray>, other: Array<IntArray>): Int {
        var n = 0
        for (i in root.indices) for (j in root[i].indices) if (root[i][j] != other[i][j]) n++
        return n
    }
}
