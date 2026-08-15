package com.magi.app.v6

import com.magi.app.model.MagiState

/**
 * [汎用玉突き結合フレームワーク, 3.249.0] 単独では isBetter に不採用(拒否)された複数の候補を
 * 束ねてまとめて適用し、全体としてなら採用できないか再挑戦する汎用ヘルパ。
 *
 * ユーザー実データの研磨ログ「不採用×78」「不採用×19」は、chain探索自体は候補を構築できた
 * (findCovUChain/手M/手F等が成立)がisBetterの最終総合判定(hard→weightedScore→total)に負けた
 * 個別候補の件数。多くは「その1手だけでは他族とのトレードオフで損」だが、複数の個別に損な手を
 * 同時に適用すると全体では改善する組合せが存在しうる（例: Aさんの休振替とBさんのDﾃ振替は単独
 * ではどちらもweekly悪化で負けるが、組合わせるとweekly族の増減が打ち消し合い総合改善する）。
 *
 * grilling確定(2026-07-20): 対象=c1/range/c3mn/apt/fairの5族横断の汎用フレームワーク。起動方式=
 * 各パス内でリアルタイムに束ねる（独立メタパスでない）。束ね単位=上限K=3〜4件の可変長組合せ。
 * 候補プール上限=なし(shouldStop()のみで打ち切り、時間予算のみで制御)。完了条件=5族各々に
 * 「単独では不採用だが結合で採用」の最小盤面テストを固定。
 *
 * 安全性: 組合せの採否は必ずUnifiedViolationChecker+isBetter(hard→weightedScore→total辞書式、
 * 呼び出し側からinjectされる)でゲート。近似は一切せず本物の目的関数で評価するため退化不能
 * （悪化する組合せは採用されない。最悪ケースは「見つからず終わる」だけ＝既存の単独手の結果より
 * 悪化することはない）。
 */
internal object CombinatorialRepair {
    /**
     * 単独では isBetter に拒否された1候補。ops=[staff,day,newShift]の差分列（適用順・巻き戻し済み）。
     * hint は捕捉時点の表示名（例「桒澤美幸(Aｱ)」）。ops先頭からの逆算は対象(staff,違反シフト)と
     * 移動先シフトが食い違いうるため、捕捉時に呼び出し側が意味の通る名前を確定させる。
     */
    data class Candidate(val ops: List<IntArray>, val mechanism: String, val hint: String = "")

    /** ログ強化用の集計。呼び出し側が最終ログ文字列にそのまま連結できる。 */
    class Stats {
        var combosTried = 0
        var combosAccepted = 0
        var truncated = false
        // [停滞検知, ユーザー指示「早期脱出しないのか?」への対応] 連続maxStagnantTries回不採用のまま
        //   進むと成立見込み薄と判断し早期break（truncated=時間切れ、こちらは無駄打ち回避で区別）。
        var stagnantExit = false
        /** [3.375.0/ユーザー指示「停滞脱出のログにイテ回数と時間を出す」] 結合探索に費やしたミリ秒。
         *  旧: 「200通り試行→無駄打ち回避で早期終了」と回数だけで、その空振りが一瞬なのか秒単位なのかが
         *  読めず、打ち切り閾値(maxStagnantTries)が妥当かを実機ログから判断できなかった。 */
        var elapsedMs = 0L
        val mechanismCounts = LinkedHashMap<String, Int>()
        val acceptedLabels = ArrayList<String>()

        internal fun onFeed(c: Candidate) {
            mechanismCounts.merge(c.mechanism, 1, Int::plus)
        }

        /** 「結合候補: 手B×2 tryRelocate×78 / 結合探索: 42通り試行→打ち切り / 結合成立×3(...)」形式。 */
        fun summary(): String {
            if (mechanismCounts.isEmpty()) return ""
            val parts = ArrayList<String>()
            parts.add("結合候補: " + mechanismCounts.entries.joinToString(" ") { "${it.key}×${it.value}" })
            val exitReason = when {
                truncated -> "→時間切れ打ち切り"
                stagnantExit -> "→無駄打ち回避で早期終了"
                else -> ""
            }
            parts.add("結合探索: ${combosTried}通り試行${if (elapsedMs > 0) "/${elapsedMs}ms" else ""}$exitReason")
            if (combosAccepted > 0) {
                val labelPart = if (acceptedLabels.isNotEmpty()) "(${acceptedLabels.joinToString(", ")})" else ""
                parts.add("結合成立×$combosAccepted$labelPart")
            }
            return parts.joinToString(" / ")
        }
    }

    /**
     * rejected プールから2〜maxK件の組合せを列挙し、まとめて適用してisBetterなら採用する。
     * first-improvementで見つかり次第そのcomboを盤面へコミットし、使った候補をプールから除去
     * して残りでさらに探す（1回の呼出で複数回の結合採用がありうる）。shouldStop()・全組合せ枯渇・
     * 停滞検知（下記）のいずれかで終了。候補プールに上限は設けず、時間予算(shouldStop)のみで
     * 打ち切る（grilling確定）。
     *
     * [停滞検知] 連続maxStagnantTries回（既定200）不採用のまま進むと、それ以上試しても成立見込みが
     * 薄いと判断し早期break（E9/E10/N4等、既存の探索停滞検知と同種の無駄打ち回避）。採否は依然
     * isBetterが決めるため退化不能＝安全に早期終了できる。カウンタは結合成立のたびにリセット
     * （進展がある間は打ち切らない）。
     *
     * ops が重複するセル(staff,day)を含む組合せは互いに排他な代替案で意味を持たないため、列挙は
     * するがフルchecker呼出はスキップする(combosTriedには計上する＝実際に検討した件数として正直)。
     */
    fun combineAndApply(
        state: MagiState,
        work: Array<IntArray>,
        bestRepIn: ViolationReport,
        rejected: List<Candidate>,
        isBetter: (ViolationReport, ViolationReport) -> Boolean,
        maxK: Int = 4,
        shouldStop: () -> Boolean = { false },
        maxStagnantTries: Int = 200,
        stats: Stats = Stats(),
        label: (Candidate) -> String = { it.hint },
        p: Problem? = null,
    ): ViolationReport {
        rejected.forEach(stats::onFeed)
        val t0 = System.currentTimeMillis()   // [3.375.0] 結合探索に費やした時間（summary で出す）
        var bestRep = bestRepIn
        val pool = rejected.toMutableList()
        var misses = 0
        while (pool.size >= 2) {
            if (shouldStop()) { stats.truncated = true; break }
            var acceptedIdx: List<Int>? = null
            var acceptedRep: ViolationReport? = null
            // [3.331.0] 組合せの試行は必ず `work` を元へ戻すので、この外側ループの間ずっと同じ盤面。
            //   旧は組合せごとに copy2D() していた（同じ内容を最大200回作り直していた）。
            val workBeforeCombo = if (p != null) work.copy2D() else emptyArray()
            val upperK = minOf(maxK, pool.size)
            searchK@ for (k in 2..upperK) {
                val combo = IntArray(k) { it }
                while (true) {
                    if (shouldStop()) { stats.truncated = true; break@searchK }
                    stats.combosTried++
                    val ops = combo.flatMap { pool[it].ops }
                    if (!hasCellOverlap(ops)) {
                        val saved = IntArray(ops.size) { work[ops[it][0]][ops[it][1]] }
                        for (op in ops) work[op[0]][op[1]] = op[2]
                        // [厳密ピン保護] 束ねた候補群も複数職員の回数を同時に変えうるため、staffRange
                        //   厳密ピン(lo==hi)を新たに崩す組合せは不採用にする（keep-best/重みは不変）。
                        //
                        // [3.331.0] **安いピン検査を先に**置く。旧はフル checker を必ず呼んでから
                        //   `isBetter(...) && !exactPinRegression(...)` を評価しており、ピンを崩す組合せにも
                        //   毎回フル評価を払っていた。`&&` は両方を要求するので**採否は完全に同一**で、
                        //   ピン破りの組合せぶんだけ checker 呼び出しが減る（実データではプールの大半が
                        //   ピン破り＝AptPolish 69/71・FairPolish 20/20）。
                        val pinBad = p != null && exactPinRegression(p, workBeforeCombo, work)
                        val rep = if (pinBad) null else UnifiedViolationChecker.check(state, work)
                        val ok = rep != null && isBetter(rep, bestRep)
                        for ((idx, op) in ops.withIndex()) work[op[0]][op[1]] = saved[idx]
                        if (ok) {
                            acceptedIdx = combo.toList()
                            acceptedRep = rep
                            break@searchK
                        }
                    }
                    misses++
                    if (misses >= maxStagnantTries) { stats.stagnantExit = true; break@searchK }
                    if (!nextCombination(combo, pool.size)) break
                }
            }
            if (acceptedIdx == null) break
            misses = 0
            val ops = acceptedIdx.flatMap { pool[it].ops }
            for (op in ops) work[op[0]][op[1]] = op[2]
            bestRep = acceptedRep!!
            stats.combosAccepted++
            val lbl = acceptedIdx.joinToString("+") { label(pool[it]) }.trim('+', ' ')
            if (lbl.isNotBlank()) stats.acceptedLabels.add(lbl)
            for (idx in acceptedIdx.sortedDescending()) pool.removeAt(idx)
        }
        stats.elapsedMs += System.currentTimeMillis() - t0
        return bestRep
    }

    private fun hasCellOverlap(ops: List<IntArray>): Boolean {
        val seen = HashSet<Long>()
        for (op in ops) {
            val key = op[0].toLong() * 100_000L + op[1]
            if (!seen.add(key)) return true
        }
        return false
    }

    private fun nextCombination(combo: IntArray, n: Int): Boolean {
        val k = combo.size
        var i = k - 1
        while (i >= 0 && combo[i] == n - k + i) i--
        if (i < 0) return false
        combo[i]++
        for (j in i + 1 until k) combo[j] = combo[j - 1] + 1
        return true
    }
}
