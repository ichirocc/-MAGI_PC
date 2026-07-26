package com.magi.app.v6

import com.magi.app.model.MagiState
import java.util.Random
import kotlin.math.max

/**
 * C1 共同 Large-Neighbourhood Search。
 *
 * 従来の C1Window/Temporal/Rotate/Bundle/Wide は、各々が候補を作った直後に完全目的関数で
 * 採否するため、C1 改善に伴う coverage/range/c3 系の副作用を別の手で相殺する前に候補を失う。
 * 本パスは C1 不足セルに加え、covU と range-low の不足セルを同じ goal pool に入れ、
 * 同日交換・3者回転・自己日交換・クロス日移送・一時的な直接変更を一つの debt 付き beam で束ねる。
 *
 * 中間ノードは root から hard/total/c1 の小さな debt を許す。最終採用は必ず
 * UnifiedViolationChecker の正式順序 hard -> total -> weightedScore で root より良く、かつ C1 が
 * 狭義減少する状態だけ。root は常に別枠で保持し、engine へ共有配列を渡す方式も使わない。
 *
 * 50% は「構造下限までの改善可能幅」に対する進捗目標であり、終了条件ではない。探索は C1=下限、
 * deadline、shouldStop、または全 restart の停滞まで継続する。
 */
internal object C1JointLnsPolish {
    data class Config(
        val targetReductionPercent: Int = 50,
        val beamWidth: Int = 24,
        val maxDepth: Int = 5,
        val maxRestarts: Int = 4,
        val maxGoals: Int = 36,
        val maxMovesPerGoal: Int = 24,
        val hardDebt: Int = 1,
        val totalDebt: Int = 12,
        val c1Debt: Int = 4,
        val maxMillis: Long = 8_000L,
    )

    private enum class GoalKind { C1, TEMPORAL, COVERAGE, RANGE_LOW }

    private data class Goal(
        val staff: Int,
        val day: Int,
        val targetShift: Int,
        val weight: Int,
        val kind: GoalKind,
    )

    private sealed interface Move {
        data class Direct(val staff: Int, val day: Int, val target: Int) : Move
        data class SameDaySwap(val a: Int, val b: Int, val day: Int) : Move
        data class Rotate3(val receiver: Int, val donor: Int, val bridge: Int, val day: Int) : Move
        data class SelfDaySwap(val staff: Int, val dayA: Int, val dayB: Int) : Move
        data class CrossDayTransfer(
            val receiver: Int,
            val receiveDay: Int,
            val donor: Int,
            val donateDay: Int,
        ) : Move
    }

    private data class Node(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val c1: Int,
        val path: List<Move>,
        val changedCells: Int,
    )

    private data class SeenState(val schedule: Array<IntArray>, val node: Node)

    fun apply(
        state: MagiState,
        schedule: Array<IntArray>,
        config: Config = Config(),
        shouldStop: () -> Boolean = { false },
        seed: Long = 0xC1A11L,
    ): V6HotfixPasses.CyclicSwapResult {
        val p = Problem(state)
        val rootSchedule = normalizeSchedule(schedule, p)
        val rootReport = UnifiedViolationChecker.check(state, rootSchedule)
        val rootC1 = rootReport.breakdown["c1"] ?: 0
        if (p.cons1.isEmpty() || rootC1 <= 0 || p.T <= 0 || p.S <= 0) {
            return V6HotfixPasses.CyclicSwapResult(
                rootSchedule, rootReport.total, rootReport.total, 0,
                listOf(MirrorLog(tag = "C1JointLNS", message = "期間要件(c1)対象なし=スキップ")),
            )
        }

        if (config.beamWidth <= 0 || config.maxDepth <= 0 || config.maxRestarts <= 0 ||
            config.maxGoals <= 0 || config.maxMovesPerGoal <= 0 || config.maxMillis <= 0L
        ) {
            return V6HotfixPasses.CyclicSwapResult(
                rootSchedule, rootReport.total, rootReport.total, 0,
                listOf(MirrorLog(tag = "C1JointLNS", message = "探索上限0=明示的に無効")),
            )
        }
        val width = config.beamWidth
        val depthLimit = config.maxDepth
        val restartLimit = config.maxRestarts
        val goalLimit = config.maxGoals
        val moveLimit = config.maxMovesPerGoal
        val budgetMillis = config.maxMillis.coerceAtMost(60_000L)
        val deadline = System.nanoTime() + budgetMillis * 1_000_000L
        fun stopped(): Boolean = shouldStop() || System.nanoTime() >= deadline

        val lowerBound = structuralC1LowerBound(p)
        val improvable = (rootC1 - lowerBound).coerceAtLeast(0)
        val pct = config.targetReductionPercent.coerceIn(1, 100)
        val targetC1 = rootC1 - ((improvable * pct + 99) / 100)

        val root = Node(rootSchedule.copy2D(), rootReport, rootC1, emptyList(), 0)
        var best = root
        var expanded = 0
        var generated = 0
        var debtRejected = 0
        var duplicateRejected = 0
        var restartsDone = 0

        for (restart in 0 until restartLimit) {
            if (stopped() || best.c1 <= lowerBound) break
            restartsDone++
            val rng = Random(seed xor (restart.toLong() * -0x61c8864680b583ebL))
            var beam = if (best === root) listOf(root) else listOf(root, best)
            val seen = HashMap<Long, MutableList<SeenState>>()
            for (n in beam) remember(seen, n)

            for (depth in 0 until depthLimit) {
                if (stopped()) break
                val children = ArrayList<Node>()
                for (parent in beam) {
                    if (stopped()) break
                    expanded++
                    val goals = collectGoals(p, parent.schedule, goalLimit, rng, includeTemporal = parent.path.isEmpty())
                    for (goal in goals) {
                        if (stopped()) break
                        val moves = generateMoves(p, parent.schedule, goal, moveLimit, rng)
                        for (move in moves) {
                            if (stopped()) break
                            val next = parent.schedule.copy2D()
                            if (!applyMove(next, move)) continue
                            generated++
                            val report = UnifiedViolationChecker.check(state, next)
                            val c1 = report.breakdown["c1"] ?: 0
                            if (report.hard > rootReport.hard + config.hardDebt.coerceAtLeast(0) ||
                                report.total > rootReport.total + config.totalDebt.coerceAtLeast(0) ||
                                c1 > rootC1 + config.c1Debt.coerceAtLeast(0)
                            ) {
                                debtRejected++
                                continue
                            }
                            val child = Node(
                                next,
                                report,
                                c1,
                                parent.path + move,
                                changedCellCount(rootSchedule, next),
                            )
                            if (!remember(seen, child)) {
                                duplicateRejected++
                                continue
                            }
                            children.add(child)

                            val finalCandidate = isFinalCandidate(p, child, root)
                            if (finalCandidate && better(child.report, best.report)) best = child
                        }
                    }
                }
                if (children.isEmpty()) break
                beam = selectBeam(children, rootReport, lowerBound, width, rng)
            }
        }

        // Defensive re-check. A shared-array bug or future operator mistake can never escape this gate.
        val finalReport = UnifiedViolationChecker.check(state, best.schedule)
        val finalC1 = finalReport.breakdown["c1"] ?: 0
        val valid = best !== root && finalC1 < rootC1 && better(finalReport, rootReport) &&
            !exactPinRegression(p, rootSchedule, best.schedule)
        val chosen = if (valid) best.schedule.copy2D() else rootSchedule.copy2D()
        val chosenReport = if (valid) finalReport else rootReport
        val chosenC1 = if (valid) finalC1 else rootC1
        val progress = if (improvable <= 0) 100 else
            (((rootC1 - chosenC1).coerceAtLeast(0) * 100) / improvable).coerceIn(0, 100)
        // [receiving-code-review] 返却盤面(chosenC1)基準。以前は探索中に一度でもtargetC1へ届いた
        // 中間候補があれば恒久trueになる targetSeen フラグを表示しており、その後 better() がより
        // 良い(だがc1はtargetC1超の)候補へ best を差し替えても「到達」と表示され続けていた。
        val targetReached = chosenC1 <= targetC1

        val stopReason = when {
            chosenC1 <= lowerBound -> "構造下限到達"
            shouldStop() -> "外部停止"
            System.nanoTime() >= deadline -> "期限"
            else -> "探索停滞"
        }
        val log = MirrorLog(
            tag = "C1JointLNS",
            message = "期間要件(c1)共同LNS: c1 $rootC1->$chosenC1 (構造下限≥$lowerBound, 改善可能幅進捗$progress%, 50%目標=${if (targetReached) "到達" else "未達"})" +
                " / total ${rootReport.total}->${chosenReport.total} HARD ${rootReport.hard}->${chosenReport.hard}" +
                " 採用${if (valid) 1 else 0}束 手数${if (valid) best.path.size else 0}" +
                " restart$restartsDone 展開$expanded 候補$generated debt除外$debtRejected 重複除外$duplicateRejected 停止=$stopReason" +
                (if (!valid) " [頭打ち=正式目的を改善するC1減少束なし]" else ""),
        )
        return V6HotfixPasses.CyclicSwapResult(
            chosen, rootReport.total, chosenReport.total, if (valid) 1 else 0, listOf(log),
        )
    }

    private fun isFinalCandidate(p: Problem, node: Node, root: Node): Boolean =
        node.c1 < root.c1 && better(node.report, root.report) &&
            !exactPinRegression(p, root.schedule, node.schedule)

    private fun better(a: ViolationReport, b: ViolationReport): Boolean {
        return betterReport(a, b)  // [3.287.0 keep-best統一] hard→weighted→total（MirrorCore.betterReport）
    }

    /**
     * Optimistic C1 lower bound. Each staff/rule is minimized independently under wishes,
     * capability and that shift's monthly range. Summing independent minima is still a valid
     * lower bound for the combined C1 objective (it may be loose, never overstates feasibility).
     */
    internal fun structuralC1LowerBound(p: Problem): Int {
        var total = 0
        for (c in p.cons1) {
            if (c.day1 <= 0 || c.day1 > p.T || c.shiftIdx !in 0 until p.K) continue
            for (i in 0 until p.S) {
                if (!p.canDo(i, c.shiftIdx)) continue
                total += singleRuleLowerBound(p, i, c)
            }
        }
        return total
    }

    private fun singleRuleLowerBound(p: Problem, staff: Int, c: C1): Int {
        val d = c.day1
        // Exact suffix-mask DP is practical for the rules used by MAGI. For an unusually long
        // rule return zero: zero is a safe (weaker) lower bound, unlike a heuristic overestimate.
        if (d > 20) return 0
        val suffixBits = (d - 1).coerceAtLeast(0)
        val maskLimit = if (suffixBits == 0) 1 else (1 shl suffixBits)
        val maskKeep = maskLimit - 1
        val lo0 = p.rangeLo[staff][c.shiftIdx]
        val hi0 = p.rangeHi[staff][c.shiftIdx]
        val lo = if (lo0 == Int.MIN_VALUE) 0 else lo0.coerceIn(0, p.T)
        val hi = if (hi0 == Int.MAX_VALUE) p.T else hi0.coerceIn(0, p.T)
        if (lo > hi) return 0
        val inf = 1_000_000
        var dp = Array(p.T + 1) { IntArray(maskLimit) { inf } }
        dp[0][0] = 0
        for (day in 0 until p.T) {
            val next = Array(p.T + 1) { IntArray(maskLimit) { inf } }
            val wished = p.wish[staff][day]
            val locked = p.wishLocked(staff, day)
            val minBit = if (locked) (if (wished == c.shiftIdx) 1 else 0) else 0
            val maxBit = if (locked) minBit else 1
            for (cnt in 0..day) for (mask in 0 until maskLimit) {
                val base = dp[cnt][mask]
                if (base >= inf) continue
                for (bit in minBit..maxBit) {
                    val nc = cnt + bit
                    if (nc > hi) continue
                    val windowPenalty = if (day + 1 >= d) {
                        val ones = Integer.bitCount(mask) + bit
                        if (ones < c.day2) 1 else 0
                    } else 0
                    val nm = if (suffixBits == 0) 0 else ((mask shl 1) or bit) and maskKeep
                    val v = base + windowPenalty
                    if (v < next[nc][nm]) next[nc][nm] = v
                }
            }
            dp = next
        }
        var best = inf
        for (cnt in lo..hi) for (mask in 0 until maskLimit) best = minOf(best, dp[cnt][mask])
        return if (best >= inf) 0 else best
    }

    private fun collectGoals(
        p: Problem,
        schedule: Array<IntArray>,
        limit: Int,
        rng: Random,
        includeTemporal: Boolean,
    ): List<Goal> {
        val map = LinkedHashMap<String, Goal>()
        fun add(i: Int, j: Int, x: Int, w: Int, kind: GoalKind) {
            if (i !in 0 until p.S || j !in 0 until p.T || x !in 0 until p.K) return
            if (schedule[i][j] == x || !allowed(p, i, j, x)) return
            val key = "$kind,$i,$j,$x"
            val old = map[key]
            if (old == null || w > old.weight) map[key] = Goal(i, j, x, w, kind)
        }

        // C1 deficits. Weight counts how many deficient windows need this cell.
        val c1Weight = HashMap<Triple<Int, Int, Int>, Int>()
        for (c in p.cons1) {
            val d = c.day1; val n = c.day2; val x = c.shiftIdx
            if (d <= 0 || d > p.T || x !in 0 until p.K) continue
            for (i in 0 until p.S) {
                if (!p.canDo(i, x)) continue
                for (start in 0..p.T - d) {
                    var count = 0
                    for (j in start until start + d) if (schedule[i][j] == x) count++
                    if (count >= n) continue
                    for (j in start until start + d) {
                        if (schedule[i][j] == x || !allowed(p, i, j, x)) continue
                        val key = Triple(i, j, x)
                        c1Weight[key] = (c1Weight[key] ?: 0) + (n - count).coerceAtLeast(1)
                    }
                }
            }
        }
        for ((k, w) in c1Weight) add(k.first, k.second, k.third, 100 + w, GoalKind.C1)

        // Reuse the existing exact temporal DP as a proposal oracle, but only at root-like
        // nodes. The DP does not commit a schedule; its desired incoming target days become
        // goals inside this joint beam, where coverage/range/c3 side effects can be repaired.
        if (includeTemporal) {
            val rankedPairs = c1Weight.entries
                .groupBy { Pair(it.key.first, it.key.third) }
                .mapValues { (_, es) -> es.sumOf { it.value } }
                .entries.sortedByDescending { it.value }.take(4)
            for ((pair, weight) in rankedPairs) {
                val i = pair.first; val x = pair.second
                val rules = p.cons1.filter { it.shiftIdx == x }
                    .map { C1TemporalDp.Rule(it.day1, it.day2) }
                // [3.278.0/監査修正] 生 wish>=0 は実現不能な希望（担当外シフトへの希望）まで固定扱いし、
                //   DP 提案オラクルを過剰ロックしていた（同ファイル他2サイトは 3.264.0 で wishLocked へ統一済みの
                //   retrofit 漏れ第3サイト）。wishLocked = 実現可能な希望のみ凍結（規約どおり）。
                val locked = BooleanArray(p.T) { day -> p.wishLocked(i, day) }
                val proposal = C1TemporalDp.solve(
                    row = schedule[i], targetShift = x, rules = rules, locked = locked,
                    maxRelocations = 6, seed = rng.nextLong(), maxExactWindow = 20,
                ) ?: continue
                for (day in 0 until p.T) {
                    if (proposal.targetDays[day] && schedule[i][day] != x) {
                        add(i, day, x, 150 + weight, GoalKind.TEMPORAL)
                    }
                }
            }
        }

        // Coverage shortages are HARD side effects that often block a C1 move. Include them in
        // the same beam so a C1 move and its coverage repair can be completed as one bundle.
        for (j in 0 until p.T) {
            val got = IntArray(p.K)
            for (i in 0 until p.S) { val k = schedule[i][j]; if (k in 0 until p.K) got[k]++ }
            for (x in 0 until p.K) {
                val shortage = p.covUCell(x, j, got[x])
                if (shortage <= 0) continue
                for (i in 0 until p.S) add(i, j, x, 200 + shortage, GoalKind.COVERAGE)
            }
        }

        // Monthly lower-range shortages. They are SOFT but frequently counterbalance C1/range-high.
        // [監査で発見・3.270.0] normalizeSchedule はセンチネル -1 を作りうる（削除済シフトの残存index等）
        //   ため、生の schedule[i][j] を無検証で配列添字に使うとAIOOBEになりうる。ガード追加。
        val counts = Array(p.S) { IntArray(p.K) }
        for (i in 0 until p.S) for (j in 0 until p.T) { val k = schedule[i][j]; if (k in 0 until p.K) counts[i][k]++ }
        for (i in 0 until p.S) for (x in 0 until p.K) {
            val lo = p.rangeLo[i][x]
            if (lo == Int.MIN_VALUE || counts[i][x] >= lo) continue
            val deficit = lo - counts[i][x]
            for (j in 0 until p.T) add(i, j, x, 50 + deficit, GoalKind.RANGE_LOW)
        }

        // Stratified round-robin prevents one staff/rule from occupying every goal slot.
        val groups = map.values.groupBy { Triple(it.kind, it.staff, it.targetShift) }
            .values.map { it.shuffled(rng).sortedByDescending(Goal::weight).toMutableList() }
            .shuffled(rng).toMutableList()
        val out = ArrayList<Goal>()
        while (out.size < limit && groups.any { it.isNotEmpty() }) {
            for (g in groups) {
                if (g.isNotEmpty()) out.add(g.removeAt(0))
                if (out.size >= limit) break
            }
        }
        return out
    }

    private fun generateMoves(
        p: Problem,
        schedule: Array<IntArray>,
        goal: Goal,
        limit: Int,
        rng: Random,
    ): List<Move> {
        val i = goal.staff; val j = goal.day; val x = goal.targetShift
        val a = schedule[i][j]
        if (a == x || !allowed(p, i, j, x)) return emptyList()
        // [賢く再構成] 全Move種の共通効果=「iのday jにxを置く」がこの時点で既に禁止連続(c3n)を
        // 作るなら、このgoal自体を即座に諦める(手を1つも生成しない)。従来はdebt+最終ゲート
        // (isFinalCandidate/defensive re-check)だけに頼っており、正しさは常に保たれていたが、
        // c3n を作るとhard debtを使い切る候補ばかり生成してしまい、maxMovesPerGoalの枠が
        // 無駄な候補で埋まっていた。事前に弾くのは効率のみの改善＝最終正しさは無関係(不変)。
        if (p.makesForbiddenRun(schedule, i, j, x)) return emptyList()
        val scored = ArrayList<Pair<Int, Move>>()

        // Elastic move. It may temporarily create coverage debt; later goals can repair it.
        scored.add(20 to Move.Direct(i, j, x))

        val staffOrder = (0 until p.S).shuffled(rng)
        for (donor in staffOrder) {
            if (donor == i || schedule[donor][j] != x || !allowed(p, donor, j, a)) continue
            // [賢く再構成] donorがaを受け取る側の禁止連続も同様に事前に弾く。
            if (p.makesForbiddenRun(schedule, donor, j, a)) continue
            scored.add(100 to Move.SameDaySwap(i, donor, j))
        }

        for (donor in staffOrder) {
            if (donor == i || schedule[donor][j] != x) continue
            for (bridge in staffOrder) {
                if (bridge == i || bridge == donor) continue
                val y = schedule[bridge][j]
                if (y == x || y == a) continue
                if (!allowed(p, donor, j, y) || !allowed(p, bridge, j, a)) continue
                if (p.makesForbiddenRun(schedule, donor, j, y) || p.makesForbiddenRun(schedule, bridge, j, a)) continue
                scored.add(80 to Move.Rotate3(i, donor, bridge, j))
            }
        }

        val dayOrder = (0 until p.T).shuffled(rng)
        for (otherDay in dayOrder) {
            if (otherDay == j || schedule[i][otherDay] != x || !allowed(p, i, otherDay, a)) continue
            // [賢く再構成] iがotherDayでaに戻る側も事前チェック(同一職員の別日、元盤面基準の
            // 保守的近似＝jとotherDayが同一窓に入る稀なケースを見逃しても最終checkerが必ず拾う)。
            if (p.makesForbiddenRun(schedule, i, otherDay, a)) continue
            scored.add(70 to Move.SelfDaySwap(i, j, otherDay))
        }

        // Cross-day token transfer: receiver gets x on j, donor gives x on another day and gets a.
        // Global monthly shift totals stay fixed while per-day coverage can move, which the old
        // same-day-only bundle could not express.
        for (donor in staffOrder) for (otherDay in dayOrder) {
            if (donor == i && otherDay == j) continue
            if (schedule[donor][otherDay] != x) continue
            if (!allowed(p, donor, otherDay, a)) continue
            if (p.makesForbiddenRun(schedule, donor, otherDay, a)) continue
            scored.add(60 to Move.CrossDayTransfer(i, j, donor, otherDay))
        }

        return scored.shuffled(rng)
            .sortedByDescending { it.first }
            .map { it.second }
            .distinctBy { it.toString() }
            .take(limit)
    }

    private fun applyMove(schedule: Array<IntArray>, move: Move): Boolean = when (move) {
        is Move.Direct -> {
            if (schedule[move.staff][move.day] == move.target) false
            else { schedule[move.staff][move.day] = move.target; true }
        }
        is Move.SameDaySwap -> {
            val x = schedule[move.a][move.day]
            val y = schedule[move.b][move.day]
            if (x == y) false else {
                schedule[move.a][move.day] = y
                schedule[move.b][move.day] = x
                true
            }
        }
        is Move.Rotate3 -> {
            val a = schedule[move.receiver][move.day]
            val x = schedule[move.donor][move.day]
            val y = schedule[move.bridge][move.day]
            if (a == x || x == y || y == a) false else {
                schedule[move.receiver][move.day] = x
                schedule[move.donor][move.day] = y
                schedule[move.bridge][move.day] = a
                true
            }
        }
        is Move.SelfDaySwap -> {
            val a = schedule[move.staff][move.dayA]
            val b = schedule[move.staff][move.dayB]
            if (a == b) false else {
                schedule[move.staff][move.dayA] = b
                schedule[move.staff][move.dayB] = a
                true
            }
        }
        is Move.CrossDayTransfer -> {
            val a = schedule[move.receiver][move.receiveDay]
            val x = schedule[move.donor][move.donateDay]
            if (a == x) false else {
                schedule[move.receiver][move.receiveDay] = x
                schedule[move.donor][move.donateDay] = a
                true
            }
        }
    }

    private fun allowed(p: Problem, staff: Int, day: Int, shift: Int): Boolean {
        val wish = p.wish[staff][day]
        return if (p.wishLocked(staff, day)) wish == shift else p.canDo(staff, shift)
    }

    private fun selectBeam(
        children: List<Node>,
        root: ViolationReport,
        lowerBound: Int,
        width: Int,
        rng: Random,
    ): List<Node> {
        val official = children.sortedWith(
            compareBy<Node> { it.report.hard }
                .thenBy { it.report.weightedScore }
                .thenBy { it.report.total }
                .thenBy { it.c1 }
                .thenBy { it.changedCells },
        ).take(max(1, width / 2))

        val c1Front = children.shuffled(rng).sortedWith(
            compareBy<Node> { (it.report.hard - root.hard).coerceAtLeast(0) }
                .thenBy { (it.c1 - lowerBound).coerceAtLeast(0) }
                .thenBy { (it.report.total - root.total).coerceAtLeast(0) }
                .thenBy { it.report.hard }
                .thenBy { it.report.weightedScore }
                .thenBy { it.report.total }
                .thenBy { it.changedCells },
        ).take(max(1, width - official.size))

        val out = ArrayList<Node>()
        for (n in official + c1Front) if (out.none { sameSchedule(it.schedule, n.schedule) }) out.add(n)
        return out.take(width)
    }

    private fun remember(seen: MutableMap<Long, MutableList<SeenState>>, node: Node): Boolean {
        val h = scheduleHash(node.schedule)
        val bucket = seen.getOrPut(h) { ArrayList() }
        if (bucket.any { sameSchedule(it.schedule, node.schedule) }) return false
        bucket.add(SeenState(node.schedule.copy2D(), node))
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
