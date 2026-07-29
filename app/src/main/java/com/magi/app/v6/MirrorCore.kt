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
data class MirrorLog(
    val ts: Long = System.currentTimeMillis(),
    val iter: Long = 0,
    val level: String = "I",
    val tag: String,
    val message: String,
)

data class ViolationReport(
    val violations: Map<String, String>,
    val needViolations: Map<String, String>,
    val countViolations: Map<String, String>,
    // [Set化] セル("i,j")に重なった全違反クラスを重み降順で保持（violations は最重1クラス＝後方互換のまま）。
    //   タップ時の全列挙と E7 フィルタの整合（最重族がOFFでも表示中の族があれば枠を出す）に使う。表示のみ。
    val cellFamilies: Map<String, List<String>> = emptyMap(),
    val breakdown: Map<String, Int>,
    val total: Int,
    val hard: Int,
    val soft: Int,
    val weightedScore: Double,
    // [場所表示] fair/weekly はセル単位でなく職員/群×シフト単位の偏りのため violations(mark) に出せない。
    //   内訳パネルの場所表示専用に、職員単位の偏り箇所を構造化して持つ（グリッドには出さない＝飽和回避）。
    //   "weekly" -> [[staffIdx, dev], ...] / "fair" -> [[staffIdx, shiftIdx, dev], ...]（dev降順）。表示のみ・スコア不変。
    val distLocations: Map<String, List<List<Int>>> = emptyMap(),
    val logs: List<MirrorLog> = emptyList(),
)

/**
 * [3.287.0 keep-best統一＝改善の質] 全 Kotlin keep-best 比較器の単一ソース。
 * 順序は hard → weightedScore → total（旧: hard → total → weightedScore）。
 * 根拠: SA/ALNS/C++ の評価器 soft は元々「重み付き和」＝探索本体は weighted を最適化しているのに、
 * Kotlin 側の keep-best だけが total(重み無視の生カウント)優先で、low90/high45 の厳密ピンを
 * 軽い族(c3=3等)の件数と交換する「total改善・weighted悪化」の採用を許していた（実機 2026-12 で
 * 吉江/桒澤の休 lo==hi=10 が割れた実例）。第2キーを weightedScore に揃えることで、keep-best が
 * チェッカーの重み階層（=業務優先度）と評価器の目的関数の両方に一致する。total は決定性のための
 * 第3タイブレークに降格。C++ 側は元から weighted のため変更不要（パリティ不変）。
 */
fun betterReport(a: ViolationReport, b: ViolationReport): Boolean = when {
    a.hard != b.hard -> a.hard < b.hard
    a.weightedScore != b.weightedScore -> a.weightedScore < b.weightedScore
    else -> a.total < b.total
}

data class ScheduleRunResult(
    val schedule: Array<IntArray>,
    val report: ViolationReport,
    /** CSV取込で氏名が一致したスタッフ行数。最適化系の結果では未使用(-1)。 */
    val matched: Int = -1,
)

data class LightOptimizeResult(
    val schedule: Array<IntArray>,
    val report: ViolationReport,
    val iterations: Long,
    val accepts: Long,
    val elapsedMs: Long,
)

object MirrorKeys {
    val hard = listOf("groupViol", "c3n", "covU", "pref")
    val soft = listOf("c1", "c2", "c3", "c3m", "c3mn", "c41", "c42", "c41s", "c42s", "covO", "low", "high", "apt", "fair", "weekly")
    val all = listOf("c1", "c2", "c3", "c3n", "c3m", "c3mn", "c41", "c42", "c41s", "c42s", "covU", "covO", "pref", "low", "high", "groupViol", "apt", "fair", "weekly")
    // [N2/⛏11] weightedScore の重み（単一の真実）。UI の重み表もこのマップを描画して
    //   最適化器とのドリフトを防ぐ。挿入順 = weightedScore の加算順（Double 結果を不変に保つ）。
    val weights: Map<String, Double> = linkedMapOf(
        "groupViol" to 10000.0, "pref" to 9000.0, "covU" to 8000.0, "c3n" to 7000.0,
        "low" to 90.0, "high" to 45.0,
        // [HF77明示数値指示(2026-07-20)] 回避の並び(c3mn)=15・窓の要件(c1)=5 に変更（旧12/4）。
        "c3mn" to 15.0, "c1" to 15.0, "c3" to 3.0, "c3m" to 2.0,
        "c2" to 1.0, "c41" to 1.0, "c42" to 1.0, "c41s" to 1.0, "c42s" to 1.0,
        "apt" to 1.0, "fair" to 1.0, "weekly" to 1.0,
        // [目的関数統一] covO は最適化器(Evaluator/Delta/C++)が amount×1.0 で加算しており、
        //   チェッカー weightedScore も 1.0 に統一（旧: 0.5 で最適化器と factor-2 乖離＝族寄与不一致）。
        //   ユーザー指示(2026-07-13)により「最適化器を正」として 1.0 で確定（HF77 明示数値指示）。
        "covO" to 1.0,
    )

    // [表示優先度/HF77明示指示 2026-07-20] aptLow/aptHigh は apt の表示専用サブクラス（重み表(WeightTableCard)には
    //   出さない＝weights map 自体には追加しない）。markCount/cellFamilies の重み優先比較では実体である apt の
    //   重み(1.0)をそのまま使う（旧: weights にキーが無く 0.0 扱い＝常に最下位に劣後していた。ユーザー指示により
    //   「aptLow/aptHighは重み1.0扱いにする」＝c2/c41/c42/c41s/c42s/fair/weekly と同格の重み1.0で競わせる）。
    fun weightOf(family: String): Double = when (family) {
        "aptLow", "aptHigh" -> weights["apt"] ?: 0.0
        else -> weights[family] ?: 0.0
    }
}

object UnifiedViolationChecker {
    private val vioClass = mapOf(
        "c1" to "vio-c1", "c2" to "vio-c2", "c3" to "vio-c3", "c3n" to "vio-c3n",
        "c3m" to "vio-c3m", "c3mn" to "vio-c3mn", "c41" to "vio-c41", "c42" to "vio-c42",
        "c41s" to "vio-c41s", "c42s" to "vio-c42s",
        "covU" to "vio-covU", "covO" to "vio-covO", "pref" to "vio-pref",
        "low" to "vio-low", "high" to "vio-high", "groupViol" to "vio-groupViol",
        // 適切回数(双方向目標): 不足=赤 / 超過=橙（range と同色だが家族は別。TallyCard/内訳で個別解決可能にする）。
        "aptLow" to "vio-aptLow", "aptHigh" to "vio-aptHigh",
    )

    fun check(state: MagiState, schedule: Array<IntArray> = state.schedule.toIntArray2D()): ViolationReport {
        val t0 = System.nanoTime()
        val p = cachedProblem(state)
        val s = normalizeSchedule(schedule, p)
        val breakdown = linkedMapOf<String, Int>()
        for (key0 in MirrorKeys.all) breakdown[key0] = 0
        val violations = linkedMapOf<String, String>()
        val needViolations = linkedMapOf<String, String>()
        val countViolations = linkedMapOf<String, String>()

        fun inc(key: String, amount: Int = 1) { breakdown[key] = (breakdown[key] ?: 0) + amount }
        // [判読性/レビュー指摘] 同一セルに複数族が重なる場合、従来は「後にマークした族」が無条件上書きで、
        //   評価順の最後(c3系)が pref/groupViol(必須)のマークを潰し、実線枠が角マーク(軽ソフト)へ降格し得た
        //   （重大度の逆転）。MirrorKeys.weights を表示優先度として使い、常に最重の族のマークを保持する。
        //   1セル1クラスの型は維持（複数違反の全保持=Set化は別段の改修）。inc/breakdown は従来どおり全件計上
        //   ＝スコアリング不変・表示のみ。
        // [Set化] 重なった全クラスは cellFams("i,j"→クラス列)にも蓄積（重複なし・後で重み降順に整列）。
        //   violations は従来どおり最重1クラス＝既存読者は不変。
        val cellFams = linkedMapOf<String, MutableList<String>>()
        fun mark(i: Int, j: Int, family: String) {
            val key = "$i,$j"
            val cls = vioClass[family] ?: family
            val fams = cellFams.getOrPut(key) { ArrayList(2) }
            if (cls !in fams) fams.add(cls)
            val prev = violations[key]
            if (prev != null) {
                val prevW = MirrorKeys.weightOf(prev.removePrefix("vio-"))
                val newW = MirrorKeys.weightOf(family)
                if (prevW >= newW) return
            }
            violations[key] = cls
        }
        // [判読性] mark() と同じ重み優先。旧: 後勝ちで軽い族(旧 covO=0.5 等)が重い族(c41 等)のマークを上書きし得た。
        fun markNeed(k: Int, j: Int, family: String) {
            val key = "$k,$j"
            val prev = needViolations[key]
            if (prev != null) {
                val prevW = MirrorKeys.weightOf(prev.removePrefix("vio-"))
                if (prevW >= MirrorKeys.weightOf(family)) return
            }
            needViolations[key] = vioClass[family] ?: family
        }
        // [防御的統一/敵対的監査で確認] mark()/markNeed() と同じ重み優先へ統一。旧: 無条件上書き
        //   (last-write-wins)は、現在の呼出順(c2→low→high→apt)と apt呼出側の手動 containsKey ガードが
        //   偶然噛み合っているだけで安全が成立していた（低い重みの族が後から呼ばれると高い重みの族の
        //   マークを消し得る潜在的な地雷）。今回は実害の確認された不具合ではないが、mark/markNeed と
        //   同じ規律に揃えて将来の族追加に対して頑健にする。
        //   [3.243.0, HF77明示指示] aptLow/aptHigh は `MirrorKeys.weightOf` により apt 本体と同じ重み1.0で
        //   解決する（旧: weights にキーが無く 0.0 扱い＝c2/low/high 等の全実族に対し常に劣後していた）。
        //   同重み同士は先勝ち(mark順)＝c2(先に呼ばれる)が apt(後に呼ばれる)より引き続き優先される。
        //   表示のみ・スコアリング(weightedScore/breakdown/inc)は不変。
        fun markCount(i: Int, k: Int, family: String) {
            val key = "$i,$k"
            val prev = countViolations[key]
            if (prev != null) {
                val prevW = MirrorKeys.weightOf(prev.removePrefix("vio-"))
                if (prevW >= MirrorKeys.weightOf(family)) return
            }
            countViolations[key] = vioClass[family] ?: family
        }
        fun cellIs(i: Int, j: Int, k: Int): Boolean = i in 0 until p.S && j in 0 until p.T && s[i][j] == k

        for (c in p.cons1) {
            for (i in 0 until p.S) {
                if (!p.canDo(i, c.shiftIdx)) continue
                var j = 0
                // [視認性] scoring(inc)は各違反窓ごとに従来どおり計上（不変）。表示(mark)だけは
                //   窓幅ぶんの塗り広げを止め、違反窓ランの先頭1セルにアンカーする。スライド窓が重複して
                //   持続不足で行全体を破線で埋めていた（1論理違反≒窓幅×重複数セル）のを 1不足領域=1マーカーへ。
                var prevViol = false
                while (j <= p.T - c.day1) {
                    var z = 0
                    for (l in 0 until c.day1) if (cellIs(i, j + l, c.shiftIdx)) z++
                    val viol = z < c.day2
                    if (viol) {
                        inc("c1")
                        if (!prevViol) mark(i, j, "c1")
                    }
                    prevViol = viol
                    j++
                }
            }
        }

        val counts = countMatrix(p, s)
        for (c in p.cons2) {
            for (i in 0 until p.S) {
                if (!p.canDo(i, c.shiftIdx)) continue
                if (counts[i][c.shiftIdx] < c.count) {
                    inc("c2")
                    markCount(i, c.shiftIdx, "c2")
                }
            }
        }

        for (c in p.cons41) {
            for (j in 0 until p.T) {
                var z = 0
                for (i in 0 until p.S) if (p.sgrp[i] == c.groupIdx && cellIs(i, j, c.shiftIdx)) z++
                if (z < c.l || z > c.u) {
                    inc("c41")
                    markNeed(c.shiftIdx, j, "c41")
                }
            }
        }

        for (c in p.cons42) {
            for (j in 0 until p.T) {
                val left = ArrayList<Int>()
                val right = ArrayList<Int>()
                for (i in 0 until p.S) {
                    if (p.sgrp[i] == c.g1 && cellIs(i, j, c.s1)) left.add(i)
                    if (p.sgrp[i] == c.g2 && cellIs(i, j, c.s2)) right.add(i)
                }
                // [3.318.0] 自己ペア／同一集合の順序重複を数えない（`c42PairCount` と同じ意味論）。
                //   left と right が同じ集合になるのは g1==g2 かつ s1==s2 のときだけ。
                val sameSet = c.g1 == c.g2 && c.s1 == c.s2
                for (i in left) for (i2 in right) {
                    if (i == i2) continue
                    if (sameSet && i2 < i) continue
                    inc("c42")
                    mark(i, j, "c42")
                    mark(i2, j, "c42")
                }
            }
        }

        // [スキルグループ新設] スキル群の C41/C42 相当（ssk を参照・既存ユニットの sgrp とは独立）。
        for (c in p.cons41s) {
            for (j in 0 until p.T) {
                var z = 0
                for (i in 0 until p.S) if (p.ssk[i] == c.groupIdx && cellIs(i, j, c.shiftIdx)) z++
                if (z < c.l || z > c.u) { inc("c41s"); markNeed(c.shiftIdx, j, "c41s") }
            }
        }
        for (c in p.cons42s) {
            for (j in 0 until p.T) {
                val left = ArrayList<Int>(); val right = ArrayList<Int>()
                for (i in 0 until p.S) {
                    if (p.ssk[i] == c.g1 && cellIs(i, j, c.s1)) left.add(i)
                    if (p.ssk[i] == c.g2 && cellIs(i, j, c.s2)) right.add(i)
                }
                val sameSet = c.g1 == c.g2 && c.s1 == c.s2   // [3.318.0] c42 と同じ（自己ペア／順序重複を除く）
                for (i in left) for (i2 in right) {
                    if (i == i2) continue
                    if (sameSet && i2 < i) continue
                    inc("c42s"); mark(i, j, "c42s"); mark(i2, j, "c42s")
                }
            }
        }

        checkC3Family(p, s, p.cons3, "c3", forbidden = false, { key, amt -> inc(key, amt) }, ::mark)
        checkC3Family(p, s, p.cons3n, "c3n", forbidden = true, { key, amt -> inc(key, amt) }, ::mark)
        checkC3Family(p, s, p.cons3m, "c3m", forbidden = false, { key, amt -> inc(key, amt) }, ::mark)
        checkC3Family(p, s, p.cons3mn, "c3mn", forbidden = true, { key, amt -> inc(key, amt) }, ::mark)

        for (i in 0 until p.S) for (j in 0 until p.T) {
            val w = p.wish[i][j]
            // [監査#11②] 実現可能な希望の未充足のみ HARD(pref) 計上・着色。担当不可の不可能希望は
            //   充足しようがなく「配布可(HARD=0)」を恒久不能にしていたため計数から対称除外する。
            //   可視性は impossibleWishCount と Sanity の不可能希望案内が担う。
            if (w in 0 until p.K && p.canDo(i, w) && s[i][j] != w) {
                inc("pref")
                mark(i, j, "pref")
            }
        }

        for (i in 0 until p.S) {
            for (k in 0 until p.K) {
                val lo = p.rangeLo[i][k]
                val hi = p.rangeHi[i][k]
                val n = counts[i][k]
                if (lo != Int.MIN_VALUE && lo != 0 && p.canDo(i, k) && n < lo) {
                    inc("low", lo - n)
                    markCount(i, k, "low")
                }
                if (hi != Int.MAX_VALUE && n > hi) {
                    inc("high", n - hi)
                    markCount(i, k, "high")
                }
                // [統一apt] 適切回数(群単位の双方向目標)。SOFT・重み1・L1偏差|n-t|。担当可シフトのみ(apt 構築時に canDo ガード済)。
                // セル着色は range(low/high, 重み90/45)を優先し、markCount の重み優先ガードにより低優先の
                // apt 色(不足=赤/超過=橙)は既存マークを上書きしない（手動 containsKey ガードは markCount 側の
                // 重み優先に統合済みのため撤去）。
                val t = p.apt[i][k]
                if (t >= 0 && n != t) {
                    inc("apt", kotlin.math.abs(n - t))
                    markCount(i, k, if (n > t) "aptHigh" else "aptLow")
                }
            }
        }

        // [統一fair] グループ内公平化: 群×担当ONシフトごと、メンバー回数の round(平均) からの L1 偏差和。
        // SOFT・重み1。最適化器(Evaluator/Delta)と同一指標。内訳チップ(UI)には出さず weightedScore/total に算入。
        // [場所表示] 偏っているメンバー(x,k,dev)を収集（内訳パネル用・グリッドには出さない）。
        val fairLocs = ArrayList<List<Int>>()
        for (g in 0 until p.G) {
            val mem = p.groupMembers[g]
            val m = mem.size
            if (m < 2) continue
            for (k in p.bucket[g]) {
                var sum = 0
                for (x in mem) sum += counts[x][k]
                val tgt = Math.round(sum.toDouble() / m).toInt()
                var d = 0
                for (x in mem) {
                    val dx = kotlin.math.abs(counts[x][k] - tgt)
                    d += dx
                    if (dx > 0) fairLocs.add(listOf(x, k, dx))
                }
                if (d > 0) inc("fair", d)
            }
        }

        // [統一weekly] 7日周期(曜日)シフト平準化: 職員ごと、勤務日(非休)の曜日別カウントの round(平均) からの
        // L1 偏差和。SOFT・重み1。最適化器(Evaluator/Delta)と同一指標。内訳チップ(UI)には出さず weightedScore/total に算入。
        // [場所表示] 偏っている職員(i,dev)を収集（内訳パネル用・グリッドには出さない）。
        val weeklyLocs = ArrayList<List<Int>>()
        for (i in 0 until p.S) {
            val wd = IntArray(7)
            for (j in 0 until p.T) { val k = s[i][j]; if (k != p.restIdx && k in 0 until p.K) wd[(p.dow0 + j) % 7]++ }
            val d = weeklyDevOfBucket(wd)
            if (d > 0) { inc("weekly", d); weeklyLocs.add(listOf(i, d)) }
        }
        val distLocations = mapOf(
            "weekly" to weeklyLocs.sortedByDescending { it[1] },
            "fair" to fairLocs.sortedByDescending { it[2] },
        )

        val cov = coverage(p, s)
        // [監査#4b] 被覆は per-cell OR/AND（VBA本家=Web HF574 と三面統一）。件数=Σセル寄与、
        //   着色=そのセルのU/Oが正のときのみ（「P2で救済されるP1不足は光らない」を自然に内包）。
        //   U>0とO>0は同一セルで両立しないため旧else-if遮蔽は不要。共有ヘルパで最適化器と同式。
        for (j in 0 until p.T) {
            for (k in 0 until p.K) {
                val got = cov[j][k]
                val u = p.covUCell(k, j, got)
                if (u > 0) { inc("covU", u); markNeed(k, j, "covU") }
                val o = p.covOCell(k, j, got)
                if (o > 0) { inc("covO", o); markNeed(k, j, "covO") }
            }
        }

        for (i in 0 until p.S) for (j in 0 until p.T) {
            val k = s[i][j]
            if (k in 0 until p.K && !p.canDo(i, k)) {
                inc("groupViol")
                mark(i, j, "groupViol")
            }
        }

        var total = 0
        for (v in breakdown.values) total += v
        var hard = 0
        for (key0 in MirrorKeys.hard) hard += breakdown[key0] ?: 0
        val soft = total - hard
        val elapsedMs = ((System.nanoTime() - t0) / 1_000_000L)
        val hardParts = ArrayList<String>()
        for (key0 in MirrorKeys.hard) hardParts.add("${key0}=${breakdown[key0] ?: 0}")
        val hardStr = hardParts.joinToString(" ")
        val softParts = ArrayList<String>()
        for (key0 in MirrorKeys.soft) {
            val n = breakdown[key0] ?: 0
            if (n > 0) softParts.add("${key0}=${n}")
        }
        val softStr = softParts.joinToString(" ")
        val msg = if (total == 0) {
            "違反なし"
        } else {
            "合計=$total | HARD=$hard [$hardStr]" + if (soft > 0) " | SOFT=$soft [$softStr]" else ""
        }
        val level = if (total == 0) "I" else "W"
        // [Set化] クラス列を重み降順に整列（安定ソート＝同重みはマーク順維持 → 先頭は violations[key] と常に一致）。
        val cellFamilies = LinkedHashMap<String, List<String>>(cellFams.size)
        for ((ck, cv) in cellFams) cellFamilies[ck] =
            if (cv.size <= 1) cv else cv.sortedByDescending { MirrorKeys.weightOf(it.removePrefix("vio-")) }
        return ViolationReport(
            violations = violations,
            needViolations = needViolations,
            countViolations = countViolations,
            cellFamilies = cellFamilies,
            breakdown = breakdown,
            total = total,
            hard = hard,
            soft = soft,
            weightedScore = weightedScore(breakdown),
            distLocations = distLocations,
            logs = listOf(MirrorLog(iter = 0, level = level, tag = "UnifiedCheck", message = "$msg (${elapsedMs}ms)")),
        )
    }

    private fun checkC3Family(
        p: Problem,
        schedule: Array<IntArray>,
        list: List<C3>,
        key: String,
        forbidden: Boolean,
        inc: (String, Int) -> Unit,
        mark: (Int, Int, String) -> Unit,
    ) {
        for (c in list) {
            val seq = c.seq
            val d = seq.size
            if (d == 0 || d > p.T) continue
            // [統一: 最適化器 Evaluator の HF507 と一致] 非forbidden の単一シフト連は run-deficit で評価する。
            // 長さ r(<d) の run ごとに (d-r) を加算し、その run のセルを強調。素の窓マッチとは違反の「方向」が
            // 異なる（窓=未完成窓数 / run=不足ぶん）ため、最適化器と表示・提案が食い違わないよう統一する。
            if (!forbidden && C3Run.isSingleShiftSeq(seq)) {
                val first = seq[0]
                for (i in 0 until p.S) {
                    val row = schedule[i]
                    val t = row.size
                    var runStart = -1
                    var r = 0
                    var j = 0
                    while (j <= t) {
                        val on = j < t && row[j] == first
                        if (on) {
                            if (r == 0) runStart = j
                            r++
                        } else if (r > 0) {
                            val deficit = d - r
                            if (deficit > 0) {
                                inc(key, deficit)
                                // [視認性] 不足run全塗り→run先頭1セルへアンカー（scoring不変: incは不足ぶん従来どおり）。
                                mark(i, runStart, key)
                            }
                            r = 0; runStart = -1
                        }
                        j++
                    }
                }
                continue
            }
            for (i in 0 until p.S) {
                var j = 0
                while (j <= p.T - d) {
                    if (schedule[i][j] == seq[0]) {
                        var z = 0
                        for (l in 1 until d) if (schedule[i][j + l] == seq[l]) z++
                        val fire = if (forbidden) z == d - 1 else z < d - 1
                        if (fire) {
                            inc(key, 1)
                            // [視認性] SOFT want窓は先頭1セルへアンカー。forbidden(c3n=HARD/c3mn)は禁止パターン
                            //   全体を表示（短く、致命は「どの並びが禁止か」を示す方が有益）。scoring(inc)は不変。
                            if (forbidden) { for (l in 0 until d) mark(i, j + l, key) } else mark(i, j, key)
                        }
                    }
                    j++
                }
            }
        }
    }


    private fun weightedScore(b: Map<String, Int>): Double {
        // [N2/⛏11] 重みは MirrorKeys.weights を単一の真実として参照。挿入順を保持しているため
        //   加算順は従来と同一＝Double 結果は不変。UI の重み表も同マップを描画する。
        var out = 0.0
        for ((key, weight) in MirrorKeys.weights) out += (b[key] ?: 0).toDouble() * weight
        return out
    }

}

fun List<List<Int>>.toIntArray2D(): Array<IntArray> {
    return Array(size) { i -> this[i].toIntArray() }
}
fun Array<IntArray>.copy2D(): Array<IntArray> {
    return Array(size) { i -> this[i].copyOf() }
}

fun Problem.canDo(staffI: Int, shiftK: Int): Boolean {
    if (staffI !in 0 until S || shiftK !in 0 until K) return false
    val g = sgrp[staffI]
    return bucket.getOrNull(g)?.contains(shiftK) == true
}

/** [監査#11①] セル(i,j)の希望を「不可侵（凍結）」として扱うか。
 *  実現可能な希望のみ凍結する。担当不可（bucket外）の不可能希望は凍結しない＝セルを
 *  被覆等の最適化へ復帰させ、入口fallback値のまま座礁するのを防ぐ。
 *  正当性: 不可能希望の pref 寄与は「w を割当て不能」ゆえ割当値に依存しない定数1。
 *  可動化しても pref は増減せず、他目的の最適化余地だけが広がる。
 *  pref の計数自体（不可能希望も違反として表示・カウント）は不変更（#11②は別裁定）。 */
fun Problem.wishLocked(i: Int, j: Int): Boolean {
    val w = wish[i][j]
    return w >= 0 && canDo(i, w)
}

fun Problem.allowedShiftsForStaff(staffI: Int): IntArray {
    // canDo と整合: 群bucketをそのまま返す（空＝担当可能シフトなし）。全呼び出し側は空配列を
    // ガード済み。旧実装は空bucketで全Kにフォールバックし canDo(=false) と矛盾していた（潜在バグ）。
    // 実データ（各群に担当シフト定義あり=非空）では挙動不変。
    return bucket.getOrNull(sgrp.getOrNull(staffI) ?: -1) ?: IntArray(0)
}

fun normalizeSchedule(schedule: Array<IntArray>, p: Problem): Array<IntArray> = Array(p.S) { i ->
    IntArray(p.T) { j ->
        val k = schedule.getOrNull(i)?.getOrNull(j) ?: 0
        if (k in 0 until p.K) k else -1
    }
}

/** [統一weekly] 曜日バケット(size 7)の平準化偏差 = round(平均) からの L1 偏差和。
 *  Evaluator / DeltaEvaluator / UnifiedViolationChecker の "weekly" 共通ソース（3面のドリフト防止）。 */
fun weeklyDevOfBucket(wd: IntArray): Int {
    var sum = 0
    for (w in wd) sum += w
    val tgt = Math.round(sum.toDouble() / 7.0).toInt()
    var d = 0
    for (w in wd) d += kotlin.math.abs(w - tgt)
    return d
}

fun countMatrix(p: Problem, schedule: Array<IntArray>): Array<IntArray> {
    val out = Array(p.S) { IntArray(p.K) }
    for (i in 0 until p.S) for (j in 0 until p.T) {
        val k = schedule[i][j]
        if (k in 0 until p.K) out[i][k]++
    }
    return out
}

/**
 * 同一 state 参照に対する Problem の単一エントリ・メモ化（性能）。
 * Problem の構築は全制約解決(cons3族/c41/c42/bucket/need…)で高コスト。最適化中は state 参照が
 * 一定なので、ここでキャッシュすると毎反復の再構築（1反復あたり破壊/修復＋hf67＋check で約3回）を排除できる。
 * Problem は実質イミュータブルで、これらの用途は schedule 非依存のため、5ワーカー間の共有読取も安全。
 * 参照比較(===)のみ。別 state が来れば作り直すので陳腐化しない。
 *
 * スレッド安全性: key と value を 1 つの不変 Entry にまとめ、@Volatile 参照を 1 回だけ読む。
 * 旧実装は key/value を別々の Volatile に持ち非アトミックに書いていたため、別スレッドが
 * 「新しい key だが古い value」を読み、要求 state と次元(S/T/K)の異なる Problem を返し得た
 * （fg の refreshCheck と bg 最適化が同一プロセスで重なると到達 → 誤スコア/AIOOBE）。
 */
private object ProblemCache {
    private class Entry(val key: MagiState, val value: Problem)
    @Volatile private var entry: Entry? = null
    fun get(state: MagiState): Problem {
        val e = entry
        if (e != null && e.key === state) return e.value
        val np = Problem(state)
        entry = Entry(state, np)   // 単一参照の公開はアトミック。race時の重複生成は等価で無害。
        return np
    }
}
fun cachedProblem(state: MagiState): Problem = ProblemCache.get(state)

fun coverage(p: Problem, schedule: Array<IntArray>): Array<IntArray> {
    val out = Array(p.T) { IntArray(p.K) }
    for (i in 0 until p.S) for (j in 0 until p.T) {
        val k = schedule[i][j]
        if (k in 0 until p.K) out[j][k]++
    }
    return out
}

// [レビュー#4 3.213.0] lockedMatrix(canDo 無視の全希望ロック)は撤去。唯一の呼出元 LightMirrorOptimizer が
//   wishLocked（実現可能希望のみ凍結）へ統一されたため呼出0のデッドコード＝削除。

fun restShiftIndex(state: MagiState): Int = state.shifts.indexOfFirst { it.kigou == "休" }.takeIf { it >= 0 } ?: 0

fun formatDay(startDate: String, offset: Int): String {
    return try {
        val fmt = SimpleDateFormat("yyyy-MM-dd", Locale.US)
        val d = fmt.parse(startDate) ?: return "${offset + 1}日"
        val cal = java.util.Calendar.getInstance(Locale.JAPAN)
        cal.time = d
        cal.add(java.util.Calendar.DATE, offset)
        val wd = "日月火水木金土"[cal.get(java.util.Calendar.DAY_OF_WEEK) - 1]
        "${cal.get(java.util.Calendar.MONTH) + 1}/${cal.get(java.util.Calendar.DAY_OF_MONTH)}($wd)"
    } catch (_: Exception) {
        "${offset + 1}日"
    }
}

fun MagiState.withSchedule(schedule: Array<IntArray>): MagiState {
    val rows = ArrayList<List<Int>>(schedule.size)
    for (row in schedule) rows.add(row.toList())
    return copy(schedule = rows)
}
