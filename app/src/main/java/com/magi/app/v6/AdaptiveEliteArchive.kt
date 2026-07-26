package com.magi.app.v6

/** Why an elite is retained after an adaptive hypothesis epoch. */
internal enum class AdaptiveEliteTier { QUALITY, DIVERSITY, BRIDGE }

/**
 * One immutable elite snapshot. [bridge] means that the schedule is search material only: it may
 * be one HARD point above the current best and must never be returned without a later official
 * checker improvement.
 */
internal data class AdaptiveElite(
    val schedule: Array<IntArray>,
    val report: ViolationReport,
    val role: HypothesisEpochRole,
    val worker: Int,
    val epoch: Int,
    val bridge: Boolean,
    val tier: AdaptiveEliteTier = if (bridge) AdaptiveEliteTier.BRIDGE else AdaptiveEliteTier.QUALITY,
)

/**
 * Thread-safe bounded elite archive for the asynchronous island portfolio.
 *
 * Exact duplicates are replaced only by an officially better report. Compression deliberately
 * keeps three different populations instead of simply taking the scalar top-N:
 *  - quality: best HARD -> total -> weightedScore schedules,
 *  - diversity: schedules far from the selected set while staying within best HARD + 1,
 *  - bridge: temporary best HARD + 1 schedules used only as relinking/fusion material.
 */
internal class AdaptiveEliteArchive(
    private val rawCapacity: Int = 64,
) {
    private val entries = ArrayList<AdaptiveElite>()

    @Synchronized
    fun clear() = entries.clear()

    @Synchronized
    fun register(
        schedule: Array<IntArray>,
        report: ViolationReport,
        role: HypothesisEpochRole,
        worker: Int,
        epoch: Int,
        bridge: Boolean,
    ) {
        val hash = scheduleHash(schedule)
        for (idx in entries.indices) {
            val old = entries[idx]
            if (scheduleHash(old.schedule) != hash || !sameSchedule(old.schedule, schedule)) continue
            if (better(report, old.report) || (sameObjective(report, old.report) && old.bridge && !bridge)) {
                entries[idx] = AdaptiveElite(schedule.copy2D(), report, role, worker, epoch, bridge)
            }
            return
        }
        entries.add(AdaptiveElite(schedule.copy2D(), report, role, worker, epoch, bridge))
        if (entries.size > rawCapacity) compactRaw()
    }

    @Synchronized
    fun size(): Int = entries.size

    @Synchronized
    fun snapshot(
        referenceSchedule: Array<IntArray>,
        referenceReport: ViolationReport,
        maxQuality: Int = 4,
        maxDiversity: Int = 4,
        maxBridge: Int = 4,
    ): List<AdaptiveElite> {
        if (entries.isEmpty()) return emptyList()
        val selected = ArrayList<AdaptiveElite>(maxQuality + maxDiversity + maxBridge)

        val quality = entries.asSequence()
            .filter { !it.bridge && it.report.hard <= referenceReport.hard }
            .sortedWith(eliteComparator)
            .toList()
        for (e in quality) {
            if (selected.size >= maxQuality) break
            addUnique(selected, e.copy(tier = AdaptiveEliteTier.QUALITY))
        }

        val diversityPool = entries.asSequence()
            .filter { !it.bridge && it.report.hard <= referenceReport.hard + 1 }
            .filterNot { candidate -> selected.any { sameSchedule(it.schedule, candidate.schedule) } }
            .toMutableList()
        repeat(maxDiversity) {
            if (diversityPool.isEmpty()) return@repeat
            var bestIndex = 0
            var bestDistance = Int.MIN_VALUE
            for (idx in diversityPool.indices) {
                val candidate = diversityPool[idx]
                val distance = if (selected.isEmpty()) {
                    scheduleDistance(referenceSchedule, candidate.schedule)
                } else {
                    selected.minOf { scheduleDistance(it.schedule, candidate.schedule) }
                }
                if (distance > bestDistance ||
                    (distance == bestDistance && better(candidate.report, diversityPool[bestIndex].report))
                ) {
                    bestDistance = distance
                    bestIndex = idx
                }
            }
            val chosen = diversityPool.removeAt(bestIndex).copy(tier = AdaptiveEliteTier.DIVERSITY)
            addUnique(selected, chosen)
        }

        val bridgePool = entries.asSequence()
            .filter { it.bridge || it.report.hard == referenceReport.hard + 1 }
            .filter { it.report.hard <= referenceReport.hard + 1 }
            .sortedWith(
                compareByDescending<AdaptiveElite> { scheduleDistance(referenceSchedule, it.schedule) }
                    .then(eliteComparator)
            )
            .toList()
        var bridges = 0
        for (e in bridgePool) {
            if (bridges >= maxBridge) break
            if (addUnique(selected, e.copy(tier = AdaptiveEliteTier.BRIDGE))) bridges++
        }

        return selected.map { it.copy(schedule = it.schedule.copy2D()) }
    }

    @Synchronized
    fun allForTest(): List<AdaptiveElite> = entries.map { it.copy(schedule = it.schedule.copy2D()) }

    private fun compactRaw() {
        if (entries.size <= rawCapacity) return
        val best = entries.minWithOrNull(eliteComparator) ?: return
        val keep = snapshot(best.schedule, best.report, maxQuality = 8, maxDiversity = 8, maxBridge = 8)
        entries.clear()
        entries.addAll(keep.take(rawCapacity))
    }

    private fun addUnique(target: MutableList<AdaptiveElite>, candidate: AdaptiveElite): Boolean {
        if (target.any { sameSchedule(it.schedule, candidate.schedule) }) return false
        target.add(candidate)
        return true
    }

    companion object {
        internal val eliteComparator: Comparator<AdaptiveElite> = Comparator { a, b ->
            compareReports(a.report, b.report)
        }

        // [3.287.0 keep-best統一] hard→weightedScore→total（MirrorCore.betterReport と同順）。
        internal fun compareReports(a: ViolationReport, b: ViolationReport): Int = when {
            a.hard != b.hard -> a.hard.compareTo(b.hard)
            a.weightedScore != b.weightedScore -> a.weightedScore.compareTo(b.weightedScore)
            else -> a.total.compareTo(b.total)
        }

        internal fun better(a: ViolationReport, b: ViolationReport): Boolean = compareReports(a, b) < 0

        internal fun sameObjective(a: ViolationReport, b: ViolationReport): Boolean =
            a.hard == b.hard && a.total == b.total && a.weightedScore == b.weightedScore

        internal fun scheduleDistance(a: Array<IntArray>, b: Array<IntArray>): Int {
            var d = 0
            val rows = minOf(a.size, b.size)
            for (i in 0 until rows) {
                val cols = minOf(a[i].size, b[i].size)
                for (j in 0 until cols) if (a[i][j] != b[i][j]) d++
                d += kotlin.math.abs(a[i].size - b[i].size)
            }
            for (i in rows until a.size) d += a[i].size
            for (i in rows until b.size) d += b[i].size
            return d
        }

        internal fun sameSchedule(a: Array<IntArray>, b: Array<IntArray>): Boolean {
            if (a.size != b.size) return false
            for (i in a.indices) if (!a[i].contentEquals(b[i])) return false
            return true
        }

        internal fun scheduleHash(schedule: Array<IntArray>): Long {
            var h = -0x340d631b7bdddcdbL
            for (row in schedule) {
                h = (h xor row.size.toLong()) * 0x100000001b3L
                for (v in row) h = (h xor (v.toLong() + 0x9e3779b9L)) * 0x100000001b3L
            }
            return h
        }
    }
}
