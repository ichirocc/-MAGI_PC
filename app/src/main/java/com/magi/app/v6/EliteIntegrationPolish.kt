package com.magi.app.v6

import com.magi.app.model.MagiState

/**
 * Integrates the elite archive after the adaptive islands stop.
 *
 * The integration has two independent keep-best stages:
 *  1. bidirectional path relinking between quality/diversity/bridge elites,
 *  2. disagreement-region beam fusion using only values present in the elites.
 *
 * Bridge schedules are never returned directly. Every adopted schedule is re-evaluated by the
 * official checker, must improve HARD -> total -> weightedScore, and must not regress exact pins.
 */
internal object EliteIntegrationPolish {
    data class Config(
        val maxElites: Int = 12,
        val maxPairs: Int = 12,
        val pathOrdersPerDirection: Int = 2,
        val maxFusionGroups: Int = 8,
        val maxFusionCells: Int = 14,
        val beamWidth: Int = 12,
        val hardDebt: Int = 1,
        val totalDebt: Int = 24,
    )

    data class Result(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val logs: List<MirrorLog>,
        val elitesUsed: Int,
        val relinkPaths: Int,
        val relinkImprovements: Int,
        val fusionGroups: Int,
        val fusionImprovements: Int,
    )

    private data class Candidate(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val role: HypothesisEpochRole?,
        val bridge: Boolean,
    )

    private data class BeamNode(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val changed: Int,
    )

    fun apply(
        state: MagiState,
        rootSchedule: Array<IntArray>,
        elites: List<AdaptiveElite>,
        shouldStop: () -> Boolean,
        deadlineMs: Long,
        config: Config = Config(),
    ): Result {
        val p = cachedProblem(state)
        val root = rootSchedule.copy2D()
        val rootReport = UnifiedViolationChecker.check(state, root)
        var bestSchedule = root.copy2D()
        var bestReport = rootReport
        var relinkPaths = 0
        var relinkImprovements = 0
        var fusionGroups = 0
        var fusionImprovements = 0

        val candidates = ArrayList<Candidate>()
        candidates.add(Candidate(root.copy2D(), rootReport, null, bridge = false))
        for (e in elites.take(config.maxElites)) {
            if (AdaptiveEliteArchive.sameSchedule(root, e.schedule)) continue
            if (candidates.any { AdaptiveEliteArchive.sameSchedule(it.schedule, e.schedule) }) continue
            candidates.add(Candidate(e.schedule.copy2D(), e.report, e.role, e.bridge))
        }
        if (candidates.size <= 1 || stopped(shouldStop, deadlineMs)) {
            // [3.349.2/敵対検証] 旧実装はここで**ログを1行も返さなかった**ので、実機ログから
            //   「統合が走ったが素材が無かった」のか「そもそも呼ばれていない」のかが読めなかった。
            //   ただし PORTFOLIO 以外は毎回 elites が空＝毎実行1行のノイズになるので、
            //   **エリートはあったのに1件も使えなかったとき**だけ出す。これは
            //   「全ワーカーが同じ解へ潰れた」という意味のある信号（3.332.0 の距離0と同じ）。
            val note = if (elites.isEmpty()) emptyList() else listOf(
                MirrorLog(tag = "EliteIntegration",
                    message = "エリート統合: 素材${elites.size}件はすべて現在の勤務表と同一" +
                        (if (stopped(shouldStop, deadlineMs)) "／または締切" else "") + "＝統合の余地なし"),
            )
            return Result(root, rootReport.copy(logs = note + rootReport.logs), note,
                candidates.size - 1, 0, 0, 0, 0)
        }

        // Non-bridge endpoints are official return candidates in their own right. Re-check them
        // instead of trusting the archived report; bridge schedules remain search material only.
        for (candidate in candidates.drop(1)) {
            if (candidate.bridge || stopped(shouldStop, deadlineMs)) continue
            val checked = UnifiedViolationChecker.check(state, candidate.schedule)
            if (better(checked, bestReport) && !exactPinRegression(p, root, candidate.schedule)) {
                bestSchedule = candidate.schedule.copy2D()
                bestReport = checked
            }
        }

        val pairs = selectPairs(candidates, config.maxPairs)
        for ((aIdx, bIdx) in pairs) {
            if (stopped(shouldStop, deadlineMs)) break
            val a = candidates[aIdx]
            val b = candidates[bIdx]
            val directions = arrayOf(a to b, b to a)
            for ((source, target) in directions) {
                if (stopped(shouldStop, deadlineMs)) break
                val orderCount = config.pathOrdersPerDirection.coerceIn(1, 2)
                for (variant in 0 until orderCount) {
                    if (stopped(shouldStop, deadlineMs)) break
                    relinkPaths++
                    val improved = relinkOnePath(
                        state, p, root, source, target, variant, shouldStop, deadlineMs,
                        bestReport,
                    )
                    if (improved != null && better(improved.second, bestReport)) {
                        bestSchedule = improved.first
                        bestReport = improved.second
                        relinkImprovements++
                    }
                }
            }
        }

        val fusionCandidates = ArrayList<Candidate>()
        fusionCandidates.add(Candidate(bestSchedule.copy2D(), bestReport, null, false))
        for (c in candidates.sortedWith(candidateComparator)) {
            if (fusionCandidates.any { AdaptiveEliteArchive.sameSchedule(it.schedule, c.schedule) }) continue
            fusionCandidates.add(c)
            if (fusionCandidates.size >= minOf(config.maxElites, 9)) break
        }
        val groups = selectFusionGroups(fusionCandidates, config.maxFusionGroups)
        for (group in groups) {
            if (stopped(shouldStop, deadlineMs)) break
            fusionGroups++
            val improved = fuseGroup(
                state = state,
                p = p,
                rootSchedule = root,
                currentBest = bestSchedule,
                currentBestReport = bestReport,
                group = group.map { fusionCandidates[it] },
                shouldStop = shouldStop,
                deadlineMs = deadlineMs,
                config = config,
            )
            if (improved != null && better(improved.second, bestReport)) {
                bestSchedule = improved.first
                bestReport = improved.second
                fusionImprovements++
            }
        }

        val checked = UnifiedViolationChecker.check(state, bestSchedule)
        val valid = better(checked, rootReport) && !exactPinRegression(p, root, bestSchedule)
        val chosen = if (valid) bestSchedule.copy2D() else root.copy2D()
        val chosenReport = if (valid) checked else rootReport
        val log = MirrorLog(
            tag = "EliteIntegration",
            message = "エリート統合: elite=${candidates.size - 1} relink=$relinkPaths(改善$relinkImprovements) " +
                "fusion=$fusionGroups(改善$fusionImprovements) / HARD ${rootReport.hard}->${chosenReport.hard} " +
                "total ${rootReport.total}->${chosenReport.total} 採用=${if (valid) 1 else 0}",
        )
        return Result(
            chosen,
            chosenReport.copy(logs = listOf(log) + chosenReport.logs),
            listOf(log),
            candidates.size - 1,
            relinkPaths,
            relinkImprovements,
            fusionGroups,
            fusionImprovements,
        )
    }

    private fun relinkOnePath(
        state: MagiState,
        p: Problem,
        rootSchedule: Array<IntArray>,
        source: Candidate,
        target: Candidate,
        variant: Int,
        shouldStop: () -> Boolean,
        deadlineMs: Long,
        incumbentReport: ViolationReport,
    ): Pair<Array<IntArray>, ViolationReport>? {
        val current = source.schedule.copy2D()
        val diffs = ArrayList<Pair<Int, Int>>()
        for (i in current.indices) for (j in current[i].indices) {
            if (target.schedule.getOrNull(i)?.getOrNull(j) != null && current[i][j] != target.schedule[i][j]) {
                diffs.add(i to j)
            }
        }
        if (diffs.isEmpty()) return null
        // [賢く再構成] c1(期間要件の窓不足)の違反セルを最優先し、他族の違反セル・非違反セルの順で並べる。
        // 従来は「違反セルか否か」の2階層のみで、covU/pref等の件数が多いデータではc1セルが希釈され
        // 後回しにされ得た（C1JointLnsPolish/EliteIntegrationPolish双方でユーザー指摘の穴）。
        val c1Priority = c1Cells(source.report)
        val violationCells = violationCells(source.report)
        diffs.sortWith(
            compareBy<Pair<Int, Int>>(
                { if (it in c1Priority) 0 else if (it in violationCells) 1 else 2 },
                { it.first },
                { it.second },
            ),
        )
        if (variant == 1) diffs.reverse()

        var bestSchedule: Array<IntArray>? = null
        var bestReport = incumbentReport
        for ((i, j) in diffs) {
            if (stopped(shouldStop, deadlineMs)) break
            val k = target.schedule[i][j]
            if (p.wishLocked(i, j) && p.wish[i][j] != k) continue
            if (!p.canDo(i, k)) continue
            current[i][j] = k
            val report = UnifiedViolationChecker.check(state, current)
            if (better(report, bestReport) && !exactPinRegression(p, rootSchedule, current)) {
                bestSchedule = current.copy2D()
                bestReport = report
            }
        }
        return bestSchedule?.let { it to bestReport }
    }

    private fun fuseGroup(
        state: MagiState,
        p: Problem,
        rootSchedule: Array<IntArray>,
        currentBest: Array<IntArray>,
        currentBestReport: ViolationReport,
        group: List<Candidate>,
        shouldStop: () -> Boolean,
        deadlineMs: Long,
        config: Config,
    ): Pair<Array<IntArray>, ViolationReport>? {
        if (group.size < 2) return null
        // [賢く再構成] relinkOnePathと同じ3階層(c1違反セル最優先)。maxFusionCellsの枠がc1改善に
        // 使われずcovU/pref等の件数優位な族に埋め尽くされる問題への対応。
        val c1Priority = c1Cells(currentBestReport)
        val priority = violationCells(currentBestReport)
        val cells = ArrayList<Pair<Int, Int>>()
        for (i in currentBest.indices) for (j in currentBest[i].indices) {
            val values = HashSet<Int>()
            values.add(currentBest[i][j])
            for (c in group) c.schedule.getOrNull(i)?.getOrNull(j)?.let(values::add)
            if (values.size > 1) cells.add(i to j)
        }
        if (cells.isEmpty()) return null
        cells.sortWith(
            compareBy<Pair<Int, Int>>({ if (it in c1Priority) 0 else if (it in priority) 1 else 2 })
                .thenByDescending { cell ->
                    group.mapNotNull { it.schedule.getOrNull(cell.first)?.getOrNull(cell.second) }.distinct().size
                }
                .thenBy { it.first }
                .thenBy { it.second }
        )
        val selected = cells.take(config.maxFusionCells)
        var beam = listOf(BeamNode(currentBest.copy2D(), currentBestReport, 0))
        var bestSchedule: Array<IntArray>? = null
        var bestReport = currentBestReport

        for ((i, j) in selected) {
            if (stopped(shouldStop, deadlineMs)) break
            val values = LinkedHashSet<Int>()
            for (node in beam) values.add(node.schedule[i][j])
            for (c in group) c.schedule.getOrNull(i)?.getOrNull(j)?.let(values::add)
            val next = ArrayList<BeamNode>()
            val seen = HashMap<Long, MutableList<Array<IntArray>>>()
            for (node in beam) {
                for (k in values) {
                    if (p.wishLocked(i, j) && p.wish[i][j] != k) continue
                    if (!p.canDo(i, k)) continue
                    val changed = if (node.schedule[i][j] == k) node.changed else node.changed + 1
                    val schedule = node.schedule.copy2D()
                    schedule[i][j] = k
                    val hash = AdaptiveEliteArchive.scheduleHash(schedule)
                    val bucket = seen.getOrPut(hash) { ArrayList() }
                    if (bucket.any { AdaptiveEliteArchive.sameSchedule(it, schedule) }) continue
                    bucket.add(schedule)
                    val report = UnifiedViolationChecker.check(state, schedule)
                    if (!withinDebt(report, currentBestReport, config)) continue
                    val child = BeamNode(schedule, report, changed)
                    next.add(child)
                    if (better(report, bestReport) && !exactPinRegression(p, rootSchedule, schedule)) {
                        bestSchedule = schedule.copy2D()
                        bestReport = report
                    }
                }
            }
            // [3.278.0/監査修正] 1セルの候補全滅（wishLocked で希望値が候補に無い・全候補が非canDo 等）で
            //   break すると**残り全セルの融合を放棄**していた。正しくはこのセルだけ skip して現ビームのまま継続。
            if (next.isEmpty()) continue
            beam = next.sortedWith(beamComparator).take(config.beamWidth)
        }
        return bestSchedule?.let { it to bestReport }
    }

    /**
     * ビーム中間ノードの許容幅。[baseline] は**呼出時点の現在最良**（`fuseGroup` の
     * `currentBestReport`）であって入口盤面ではない。[3.349.2] 引数名が `root` だったため
     * 「入口比の debt」と読めたが、実際は現在最良比＝窓はより狭い。名前を実態へ合わせた。
     * 中間ノードの緩さは探索にしか効かず、採用は必ず [better] ＋ `exactPinRegression` が決める。
     */
    private fun withinDebt(report: ViolationReport, baseline: ViolationReport, config: Config): Boolean {
        if (report.hard < baseline.hard) return true
        if (report.hard > baseline.hard + config.hardDebt) return false
        return report.total <= baseline.total + config.totalDebt
    }

    private fun selectPairs(candidates: List<Candidate>, maxPairs: Int): List<Pair<Int, Int>> {
        if (maxPairs <= 0) return emptyList()
        val pairs = LinkedHashSet<Pair<Int, Int>>()
        // [3.314.0] root ペアで予算を食い尽くさない。旧実装は (0,i) を全部入れてから `>= maxPairs` で
        //   return しており、エリートが maxPairs 件以上あると**非root ペア（elite 間・高距離・別役割）へ
        //   一度も到達しなかった**＝「EliteIntegration 改善なし」の説明になりうる。root に 2/3 を上限として
        //   割り当て、残り 1/3 は下のランキング済み非root ペアへ必ず残す。
        val rootQuota = maxOf(1, maxPairs * 2 / 3)
        for (i in 1 until candidates.size) {
            pairs.add(0 to i)
            if (pairs.size >= rootQuota) break
        }
        val all = ArrayList<Triple<Int, Int, Int>>()
        for (i in 1 until candidates.size) for (j in i + 1 until candidates.size) {
            val roleBonus = if (candidates[i].role != candidates[j].role) 10_000 else 0
            val distance = AdaptiveEliteArchive.scheduleDistance(candidates[i].schedule, candidates[j].schedule)
            all.add(Triple(i, j, roleBonus + distance))
        }
        for ((i, j) in all.sortedByDescending { it.third }) {
            pairs.add(i to j)
            if (pairs.size >= maxPairs) break
        }
        return pairs.toList()
    }

    private fun selectFusionGroups(candidates: List<Candidate>, maxGroups: Int): List<IntArray> {
        if (candidates.size < 2 || maxGroups <= 0) return emptyList()
        val groups = ArrayList<IntArray>()
        // [3.314.0] selectPairs と同じ理由で 2者組が枠を食い尽くさないようにする（旧: 3者融合へ到達しない）。
        val pairQuota = maxOf(1, maxGroups * 2 / 3)
        for (i in 1 until candidates.size) {
            groups.add(intArrayOf(0, i))
            if (groups.size >= pairQuota) break
        }
        val far = (1 until candidates.size).sortedByDescending {
            AdaptiveEliteArchive.scheduleDistance(candidates[0].schedule, candidates[it].schedule)
        }
        for (a in far.indices) for (b in a + 1 until far.size) {
            groups.add(intArrayOf(0, far[a], far[b]))
            if (groups.size >= maxGroups) return groups
        }
        return groups
    }

    private fun violationCells(report: ViolationReport): Set<Pair<Int, Int>> {
        val out = HashSet<Pair<Int, Int>>()
        for (key in report.violations.keys) {
            val i = key.substringBefore(',').toIntOrNull() ?: continue
            val j = key.substringAfter(',').substringBefore(',').toIntOrNull() ?: continue
            out.add(i to j)
        }
        return out
    }

    /** [賢く再構成] c1(期間要件の窓不足)が重なっているセルだけを抽出。`cellFamilies`(1セルに重なった
     *  全違反クラスを保持、`violations`は最重1クラスのみ)を使うため、c1がより重い違反(例: c3n)に
     *  上書きされて`violations`の最重クラスから消えていても取りこぼさない（3.205.0のC1Polish
     *  anchor選定と同種の穴をここでも回避）。 */
    internal fun c1Cells(report: ViolationReport): Set<Pair<Int, Int>> {
        val out = HashSet<Pair<Int, Int>>()
        for ((key, fams) in report.cellFamilies) {
            if ("vio-c1" !in fams) continue
            val i = key.substringBefore(',').toIntOrNull() ?: continue
            val j = key.substringAfter(',').substringBefore(',').toIntOrNull() ?: continue
            out.add(i to j)
        }
        return out
    }

    private fun stopped(shouldStop: () -> Boolean, deadlineMs: Long): Boolean =
        shouldStop() || System.currentTimeMillis() >= deadlineMs

    private fun better(a: ViolationReport, b: ViolationReport): Boolean =
        AdaptiveEliteArchive.better(a, b)

    private val candidateComparator = Comparator<Candidate> { a, b ->
        AdaptiveEliteArchive.compareReports(a.report, b.report)
    }

    private val beamComparator = Comparator<BeamNode> { a, b ->
        val q = AdaptiveEliteArchive.compareReports(a.report, b.report)
        if (q != 0) q else a.changed.compareTo(b.changed)
    }
}
