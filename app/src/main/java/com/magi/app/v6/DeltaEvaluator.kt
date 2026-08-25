package com.magi.app.v6

/**
 * Incremental (delta) evaluator — the native equivalent of the Web's BIT-DELTA framework.
 *
 * Instead of recomputing the whole objective for every candidate (Evaluator.fullEval,
 * O(S * T * constraints)), this maintains running per-piece penalty totals plus the
 * aggregates needed to update them, and computes the score change caused by a single
 * cell move (i, j): old shift -> new shift, touching only the windows/columns that can
 * actually change.
 *
 * Per-worker mutable state; not thread-safe (each SA worker owns one instance).
 *
 * The delta logic was validated against fullEval with a 20,000-move randomized
 * differential test (zero mismatches on both preview and committed score).
 */
class DeltaEvaluator(private val p: Problem) {

    private val S = p.S; private val T = p.T; private val K = p.K
    private val a: Array<IntArray>

    // aggregates
    private val cntSS = Array(S) { IntArray(K) }      // per-staff per-shift count
    private val cntDay = Array(K) { IntArray(T) }     // per-shift per-day count
    // [統一weekly/3.345.0] per-staff × per-shift の曜日別カウント（休も1シフト＝特別扱いしない）。
    private val wdCnt = Array(S) { Array(K) { IntArray(7) } }

    // running penalty pieces
    private var sc1 = 0L; private var sc2 = 0L; private var sc41 = 0L; private var sc42 = 0L
    private var sc41s = 0L; private var sc42s = 0L   // [スキルグループ] c41s/c42s（ssk ベース、soft）
    private var sc3 = 0L; private var hc3n = 0L; private var sc3m = 0L; private var sc3mn = 0L
    private var hpref = 0L; private var hct = 0L
    // [3.318.0] groupViol（担当できないシフトに就いているセル）。MirrorKeys.hard は元から4族なのに
    //   評価器側だけ3族（c3n/pref/covU）で、同じ盤面に対しチェッカーと評価器の hard が食い違っていた。
    private var hGrpV = 0L
    private var sApt = 0L                             // [統一apt] 適切回数(双方向目標)の running total（SOFT, 重み1）
    private var sFair = 0L                            // [統一fair] グループ内公平化の running total（SOFT, 重み1）
    private var sWeekly = 0L                          // [統一weekly] 曜日平準化の running total（SOFT, 重み1）
    private var scovO = 0L                            // [統一a] 過剰被覆(covO)の running total（SOFT）
    private var covUTot = 0L   // [監査#4b] per-cell covU の総和（セル局所Δで維持）

    // stashed deltas from the last preview (applied by commit())
    private var lI = -1; private var lJ = -1; private var lOld = -1; private var lNw = -1
    private var dC1 = 0L; private var dC2 = 0L; private var dC41 = 0L; private var dC42 = 0L
    private var dC41s = 0L; private var dC42s = 0L
    private var dC3 = 0L; private var dC3n = 0L; private var dC3m = 0L; private var dC3mn = 0L
    private var dPref = 0L; private var dGrpV = 0L; private var dCt = 0L; private var dApt = 0L; private var dFair = 0L; private var dWeekly = 0L; private var dCovO = 0L; private var nCovU = 0L

    init {
        a = p.initialAssignment()
        rebuild()
    }

    /** Reset to a fresh assignment and recompute all aggregates / totals.
     *
     *  [3.410.0/D-01] 旧: 行長・値域を検証せず `System.arraycopy` していたため、S×T に満たない盤面で
     *  `IndexOutOfBoundsException`、範囲外シフト値で `rebuild()` の `cntSS/cntDay` 添字が飛んだ。
     *  正規経路（`Problem.initialAssignment` / `allowedShiftsForStaff` 由来）では起きないが、この関数は
     *  internal＝直接呼出で到達しうる。**丸めず落とす**: `rebuild()` は `cntSS[i][k]++` を無検証で行うので
     *  センチネル -1 を入れても結局そこで飛ぶし、`restIdx` へ黙って丸めるのは「静かに意味を変える」
     *  fail-open そのもの。この不変条件（盤面は必ず S×T かつ全セルが [0,K)）は `initialAssignment` が
     *  入口で保証しており、破れているなら呼出側のバグ＝その場で名指しするのが正しい。 */
    fun reset(init: Array<IntArray>) {
        require(init.size >= S) { "reset: rows ${init.size} < S=$S" }
        for (i in 0 until S) {
            val row = init[i]
            require(row.size >= T) { "reset: row $i has ${row.size} cells < T=$T" }
            for (j in 0 until T) {
                val k = row[j]
                require(k in 0 until K) { "reset: cell($i,$j)=$k out of [0,$K)" }
                a[i][j] = k
            }
        }
        rebuild()
    }

    fun snapshot(): Array<IntArray> = Array(S) { a[it].copyOf() }

    /** Copy the current assignment into a caller-owned buffer (no allocation; hot-loop best capture). */
    fun snapshotInto(dst: Array<IntArray>) {
        for (i in 0 until S) System.arraycopy(a[i], 0, dst[i], 0, T)
    }

    /** Current shift assigned at (i,j). */
    fun at(i: Int, j: Int): Int = a[i][j]

    /** Running per-staff count of shift k (O(1) lookup; backs the findXxx targeted fixers). */
    fun countForStaff(i: Int, k: Int): Int = cntSS[i][k]

    /** Running per-day count of shift k on day j (O(1) lookup; backs findCovOFix). */
    fun countOnDay(k: Int, j: Int): Int = cntDay[k][j]

    /**
     * [3.371.0/soft全族の完全差分] running per-family の生カウント（`MirrorKeys.all` の各キーと
     * 一対一・checker の `report.breakdown[key]` と同一単位）を検証専用に公開する。
     *
     * `score()`(soft集約) は各フィールドへ重みを乗じて合算するため、**総和が一致しても族ごとの誤差が
     * 相殺されて隠れる余地がある**（例: c1(重み30)とc3mn(重み30)が同じ重みを持つため、片方+1・もう片方-1
     * の誤りは総和では検出できない。c2/c41/c42/c41s/c42s/apt/fair/weekly/covO も全て重み1で同じ穴を持つ）。
     * このマップは checker の `breakdown` と**1キーずつ**突き合わせる per-family パリティ検証のために
     * 存在する（`DeltaEvaluatorTest` 参照）。low/high だけは [rangeWeighted] を参照（下記）。
     */
    internal fun familyRaw(): Map<String, Long> = linkedMapOf(
        "c1" to sc1, "c2" to sc2, "c41" to sc41, "c42" to sc42, "c41s" to sc41s, "c42s" to sc42s,
        "c3" to sc3, "c3n" to hc3n, "c3m" to sc3m, "c3mn" to sc3mn,
        "pref" to hpref, "groupViol" to hGrpV, "apt" to sApt, "fair" to sFair, "weekly" to sWeekly,
        "covO" to scovO, "covU" to covUTot,
    )

    /**
     * [3.371.0/soft全族の完全差分] `hct` は low(重み90)/high(重み45) を**その場で重み適用済み**の1つの
     * running total へ合算している（`rangeViol` 参照）。checker の `breakdown["low"]`/`breakdown["high"]`
     * は生カウント(UNweighted)なので、単体では直接比較できない。検証は
     * `breakdown["low"]*90 + breakdown["high"]*45 == rangeWeighted()` の形で行う。
     */
    internal fun rangeWeighted(): Long = hct

    /**
     * [3.372.0/レビュー修正] low/high を**別々の生 amount** で返す（検証専用・O(S×K) のフル再計算）。
     * `rangeWeighted()` だけだと `90a+45b` が単射でない（low=1,high=0 と low=0,high=2 がどちらも90）ため、
     * 「low を1件見落として high を2件過剰に数える」型の取り違えを検出できない＝soft全族の完全差分という
     * 主張が low/high についてだけ成立していなかった。
     *
     * ホットパスの [rangeViol] は重み適用済みの1本(`hct`)で持つ設計（性能上の意図的な選択）なので、
     * ここは同じ述語を書き下したフル再計算にしてある。両者のドリフトは、テストが
     * `rangeRaw().first*90 + rangeRaw().second*45 == rangeWeighted()` を毎手つき合わせることで検出する
     * （左辺=フル再計算・右辺=差分維持なので、この等式は同時に増分整合性の検査にもなる）。
     */
    internal fun rangeRaw(): Pair<Long, Long> {
        var lowAmt = 0L; var highAmt = 0L
        for (i in 0 until S) for (k in 0 until K) {
            val n = cntSS[i][k]
            val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]
            if (lo != Int.MIN_VALUE && lo != 0 && n < lo && p.canDo(i, k)) lowAmt += (lo - n).toLong()
            if (hi != Int.MAX_VALUE && n > hi) highAmt += (n - hi).toLong()
        }
        return lowAmt to highAmt
    }

    /** Fused previewMove + commit for a single cell. Returns the new total score. */
    fun apply(i: Int, j: Int, nw: Int): Long {
        previewMove(i, j, nw)
        commit(i, j, nw)
        return score()
    }

    fun score(): Long = scoreFrom(covUTot)

    private fun scoreFrom(cu: Long): Long {
        val h1 = hc3n + cu + hpref + hGrpV
        // [統一a/b] range(hct, 重み付き) と covO(scovO) を SOFT に含める（旧: hct は h2=表示HARD）。
        // [統一c] c3/c3m/c3mn に checker 重み(3/2/30)を適用（sc3等は #fire/run-deficit の生カウント）。
        // [統一c1] c1 にも checker 重み(30)を適用（sc1 は #fire 生カウント、canDoガード済）。
        // [統一apt/fair/weekly] sApt(適切回数) sFair(群内公平化) sWeekly(曜日平準化) を SOFT に含める（共に重み1）。
        // [HF77明示数値指示] c1: 4→5(2026-07-20)→15(2026-07-21)→30(3.409.24)。c3mn: 12→15(2026-07-20)→30(3.409.24)。
        //   [外部レビューM2] 上3行のコメントは長らく旧値(15)のまま残っていた＝実装(下の * 30)は常に正しい。
        val soft = sc1 * 30 + sc2 + sc41 + sc42 + sc41s + sc42s + sc3 * 3 + sc3m * 2 + sc3mn * 30 + hct + sApt + sFair + sWeekly + scovO
        return h1 * SCORE_HARD_UNIT + soft
    }

    /** Preview the score after moving (i,j) -> nw, stashing deltas for commit(). No mutation of totals. */
    private fun previewMove(i: Int, j: Int, nw: Int): Long {
        // [3.410.0/D-02] 旧: `nw` を無検証で `cntDay[nw][j]` 等の添字に使っており、範囲外で即座に
        //   ArrayIndexOutOfBounds になった。正規の探索オペレータは `allowedShiftsForStaff` から選ぶので
        //   到達しないが、`revert()` は過去の `a[i][j]` を戻すため、盤面の不変条件（reset の require）と
        //   対で保っておく必要がある。ここも丸めずに落とす（理由は reset の KDoc と同じ）。
        require(nw in 0 until K) { "previewMove: nw=$nw out of [0,$K)" }
        val old = a[i][j]
        lI = i; lJ = j; lOld = old; lNw = nw
        if (nw == old) {
            dC1 = 0; dC2 = 0; dC41 = 0; dC42 = 0; dC41s = 0; dC42s = 0; dC3 = 0; dC3n = 0; dC3m = 0; dC3mn = 0
            dPref = 0; dGrpV = 0; dCt = 0; dApt = 0; dFair = 0; dWeekly = 0; dCovO = 0; nCovU = covUTot
            return score()
        }

        // windowed families (c1, c3 family): before/after via temporary swap
        val bC1 = c1Local(i, j); val bC3 = c3Local(i, j, p.cons3, false)
        val bC3n = c3Local(i, j, p.cons3n, true); val bC3m = c3Local(i, j, p.cons3m, false)
        val bC3mn = c3Local(i, j, p.cons3mn, true)
        // [外部レビュー検証/例外安全性] a[i][j] を一時的に nw へ書き換えて after 側を測るこの窓は、
        //   c1Local/c3Local が現行の不変条件下では例外を投げない（境界は min/max で常にクランプ済み）
        //   ため実害は未確認だが、将来の変更で throw する経路が増えても「復元されないまま盤面が
        //   壊れる」という最悪の失敗モード（クラッシュではなく以降の全差分計算が静かに狂う）を
        //   構造的に防ぐため try/finally で復元を保証する。プリミティブ var のみ＝ボクシング無し。
        a[i][j] = nw
        var aC1 = 0L; var aC3 = 0L; var aC3n = 0L; var aC3m = 0L; var aC3mn = 0L
        try {
            aC1 = c1Local(i, j); aC3 = c3Local(i, j, p.cons3, false)
            aC3n = c3Local(i, j, p.cons3n, true); aC3m = c3Local(i, j, p.cons3m, false)
            aC3mn = c3Local(i, j, p.cons3mn, true)
        } finally {
            a[i][j] = old
        }
        dC1 = (aC1 - bC1); dC3 = (aC3 - bC3); dC3n = (aC3n - bC3n); dC3m = (aC3m - bC3m); dC3mn = (aC3mn - bC3mn)

        // c2 (per-staff total) for shifts old / nw
        var d2 = 0L
        // [監査#5] 担当不可は対象外（全量/集計/差分の3箇所で同一条件に統一し再乖離を防ぐ）。
        //   in-bucket不変量下では old/nw は常に担当可のため実質no-op（差分恒等性は保たれる）。
        for (c in p.cons2) {
            if (!p.canDo(i, c.shiftIdx)) continue
            when (c.shiftIdx) {
                old -> d2 += viol01(cntSS[i][old] - 1 < c.count) - viol01(cntSS[i][old] < c.count)
                nw -> d2 += viol01(cntSS[i][nw] + 1 < c.count) - viol01(cntSS[i][nw] < c.count)
            }
        }
        dC2 = d2

        // ct (LimMin/LimMax) for shifts old / nw
        dCt = (rangeViol(i, old, cntSS[i][old] - 1) - rangeViol(i, old, cntSS[i][old])) +
              (rangeViol(i, nw, cntSS[i][nw] + 1) - rangeViol(i, nw, cntSS[i][nw]))

        // [統一apt] 適切回数(双方向目標) for shifts old / nw — staff i の old/nw 列のみ変化（range と同形）。
        dApt = (aptViol(i, old, cntSS[i][old] - 1) - aptViol(i, old, cntSS[i][old])) +
               (aptViol(i, nw, cntSS[i][nw] + 1) - aptViol(i, nw, cntSS[i][nw]))

        // [統一fair] グループ内公平化 — staff i の群 gI の old/nw 列のみ偏差が動く（本人 ±1 で平均も動くので群内再計算）。
        val gI = p.sgrp[i]
        dFair = 0L
        if (p.canDo(i, old)) dFair += fairDevAt(gI, old, i, -1) - fairDevAt(gI, old, -1, 0)
        if (p.canDo(i, nw)) dFair += fairDevAt(gI, nw, i, +1) - fairDevAt(gI, nw, -1, 0)

        // [統一weekly/3.345.0] 曜日平準化 — シフト別なので old と nw の2バケットだけが動く（old==nw は不変）。
        //   範囲外セントネル(-1等)はそのバケットを持たないので、範囲ガードで片側だけ動かす。
        // [3.445.0続/L-04経由で発見] ここも c1Local/c3Local の a[i][j] 窓と同型の一時書換え→測定→復元
        //   パターン（b[wdIdx]-- ... b[wdIdx]++）で、復元前に例外が飛ぶと wdCnt が恒久的にずれる。
        //   weeklyDevOfBucket は現行不変条件下で例外を投げない純関数（配列を添字なしで走査するのみ・
        //   0除算は 7.0 固定なので発生しない）ため実害は未確認だが、previewMove 冒頭の a[i][j] と
        //   同じ理由で try/finally を掛け、将来の変更でも構造的に安全にする。
        dWeekly = 0L
        val wdIdx = (p.dow0 + j) % 7
        if (old != nw) {
            var accW = 0L
            if (old in 0 until p.K) {
                val b = wdCnt[i][old]
                val before = weeklyDevOfBucket(b).toLong()
                b[wdIdx]--
                try {
                    accW += weeklyDevOfBucket(b).toLong() - before
                } finally {
                    b[wdIdx]++
                }
            }
            if (nw in 0 until p.K) {
                val b = wdCnt[i][nw]
                val before = weeklyDevOfBucket(b).toLong()
                b[wdIdx]++
                try {
                    accW += weeklyDevOfBucket(b).toLong() - before
                } finally {
                    b[wdIdx]--
                }
            }
            dWeekly = accW
        }

        // pref (this cell only)
        val w = p.wish[i][j]
        // [監査#11②] 実現可能な希望のみ pref 計上（不可能希望は全量/集計/差分で対称除外）。
        val wOk = w >= 0 && p.canDo(i, w)
        dPref = (if (wOk && nw != w) 1L else 0L) - (if (wOk && old != w) 1L else 0L)

        // [3.318.0] groupViol（担当できないシフト）はセル単位なので差分もこの1セルだけ。
        //   範囲外セントネル(-1)は canDo の対象外＝0 として扱う（Evaluator/checker と同じ範囲ガード）。
        dGrpV = (if (nw in 0 until K && !p.canDo(i, nw)) 1L else 0L) -
            (if (old in 0 until K && !p.canDo(i, old)) 1L else 0L)

        // c41 (group/day range) on day j — only constraints touching this staff's group & shifts
        val gi = p.sgrp[i]
        var d41 = 0L
        for (c in p.cons41) {
            if (c.groupIdx != gi || (c.shiftIdx != old && c.shiftIdx != nw)) continue
            var z = 0
            for (ii in 0 until S) if (p.sgrp[ii] == c.groupIdx && a[ii][j] == c.shiftIdx) z++
            val za = z + (if (c.shiftIdx == nw) 1 else 0) - (if (c.shiftIdx == old) 1 else 0)
            d41 += viol01(za < c.l || c.u < za) - viol01(z < c.l || c.u < z)
        }
        dC41 = d41

        // c42 (group pair) on day j — skip constraints the move cannot change (mirrors c41's guard).
        var d42 = 0L
        for (c in p.cons42) {
            val touch1 = c.g1 == gi && (c.s1 == old || c.s1 == nw)
            val touch2 = c.g2 == gi && (c.s2 == old || c.s2 == nw)
            if (!touch1 && !touch2) continue   // n1a==n1 && n2a==n2 -> delta 0
            var n1 = 0; var n2 = 0
            for (ii in 0 until S) {
                if (p.sgrp[ii] == c.g1 && a[ii][j] == c.s1) n1++
                if (p.sgrp[ii] == c.g2 && a[ii][j] == c.s2) n2++
            }
            val n1a = n1 + (if (c.g1 == gi && c.s1 == nw) 1 else 0) - (if (c.g1 == gi && c.s1 == old) 1 else 0)
            val n2a = n2 + (if (c.g2 == gi && c.s2 == nw) 1 else 0) - (if (c.g2 == gi && c.s2 == old) 1 else 0)
            val same = c.g1 == c.g2 && c.s1 == c.s2
            d42 += c42PairCount(same, n1a, n2a) - c42PairCount(same, n1, n2)
        }
        dC42 = d42

        // c41s (skill group/day range) on day j — same shape as c41 but indexed by ssk
        val gis = p.ssk[i]
        var d41s = 0L
        for (c in p.cons41s) {
            if (c.groupIdx != gis || (c.shiftIdx != old && c.shiftIdx != nw)) continue
            var z = 0
            for (ii in 0 until S) if (p.ssk[ii] == c.groupIdx && a[ii][j] == c.shiftIdx) z++
            val za = z + (if (c.shiftIdx == nw) 1 else 0) - (if (c.shiftIdx == old) 1 else 0)
            d41s += viol01(za < c.l || c.u < za) - viol01(z < c.l || c.u < z)
        }
        dC41s = d41s

        // c42s (skill group pair) on day j — same shape as c42 but indexed by ssk
        var d42s = 0L
        for (c in p.cons42s) {
            val touch1 = c.g1 == gis && (c.s1 == old || c.s1 == nw)
            val touch2 = c.g2 == gis && (c.s2 == old || c.s2 == nw)
            if (!touch1 && !touch2) continue
            var n1 = 0; var n2 = 0
            for (ii in 0 until S) {
                if (p.ssk[ii] == c.g1 && a[ii][j] == c.s1) n1++
                if (p.ssk[ii] == c.g2 && a[ii][j] == c.s2) n2++
            }
            val n1a = n1 + (if (c.g1 == gis && c.s1 == nw) 1 else 0) - (if (c.g1 == gis && c.s1 == old) 1 else 0)
            val n2a = n2 + (if (c.g2 == gis && c.s2 == nw) 1 else 0) - (if (c.g2 == gis && c.s2 == old) 1 else 0)
            val same = c.g1 == c.g2 && c.s1 == c.s2
            d42s += c42PairCount(same, n1a, n2a) - c42PairCount(same, n1, n2)
        }
        dC42s = d42s

        // [監査#4b] covU/covO 差分はセル局所（影響は (old,j)/(nw,j) の2セルのみ）。共有ヘルパで全量と同式。
        val co = cntDay[old][j]
        val cn = cntDay[nw][j]
        val dCovU = (p.covUCell(old, j, co - 1) - p.covUCell(old, j, co)).toLong() +
                    (p.covUCell(nw, j, cn + 1) - p.covUCell(nw, j, cn)).toLong()
        nCovU = covUTot + dCovU
        dCovO = (p.covOCell(old, j, co - 1) - p.covOCell(old, j, co)).toLong() +
                (p.covOCell(nw, j, cn + 1) - p.covOCell(nw, j, cn)).toLong()

        // [統一b] dCt(range) は SOFT へ移動（hard から除外）。
        val dHard = dC3n + (nCovU - covUTot) + dPref + dGrpV
        // [統一c] c3/c3m/c3mn の delta にも checker 重み(3/2/15)を適用（full soft と同一係数）。
        // [統一c1] c1 の delta にも ×15。[HF77明示数値指示(2026-07-20)] c1=4→5・c3mn=12→15。
        // [HF77明示数値指示(2026-07-21)] c1=5→15 に変更。
        val dSoft = dC1 * 30 + dC2 + dC41 + dC42 + dC41s + dC42s + dC3 * 3 + dC3m * 2 + dC3mn * 30 + dCt + dApt + dFair + dWeekly + dCovO
        return score() + dHard * SCORE_HARD_UNIT + dSoft
    }

    /** Apply the stashed move from the last previewMove(i,j,nw). Internal — external callers use apply(). */
    private fun commit(i: Int, j: Int, nw: Int) {
        require(lI >= 0 && i == lI && j == lJ && nw == lNw) { "commit must match last preview" }
        require(a[i][j] == lOld) { "state changed after preview" }
        val old = lOld
        try {
            if (nw == old) return
            a[i][j] = nw
            cntSS[i][old]--; cntSS[i][nw]++
            cntDay[old][j]--; cntDay[nw][j]++
            // [統一weekly/3.345.0] シフト別バケットを更新（previewMove の dWeekly と同ステップ）。
            //   範囲ガードは preview と対称（範囲外セントネルでも wdCnt が乖離しない）。
            if (old != nw) {
                val wIdx = (p.dow0 + j) % 7
                if (old in 0 until K) wdCnt[i][old][wIdx]--
                if (nw in 0 until K) wdCnt[i][nw][wIdx]++
            }
            sc1 += dC1; sc2 += dC2; sc41 += dC41; sc42 += dC42; sc41s += dC41s; sc42s += dC42s
            sc3 += dC3; hc3n += dC3n; sc3m += dC3m; sc3mn += dC3mn
            hpref += dPref; hGrpV += dGrpV; hct += dCt; sApt += dApt; sFair += dFair; sWeekly += dWeekly; scovO += dCovO
            covUTot = nCovU
        } finally {
            // invalidate the stash so a stray double-commit cannot corrupt aggregates
            lI = -1; lJ = -1; lOld = -1; lNw = -1
        }
    }

    // ---- aggregate / total rebuild --------------------------------------------

    private fun rebuild() {
        for (i in 0 until S) java.util.Arrays.fill(cntSS[i], 0)
        for (k in 0 until K) java.util.Arrays.fill(cntDay[k], 0)
        for (i in 0 until S) for (k in 0 until K) java.util.Arrays.fill(wdCnt[i][k], 0)
        for (i in 0 until S) for (j in 0 until T) {
            val k = a[i][j]; cntSS[i][k]++; cntDay[k][j]++
            if (k in 0 until K) wdCnt[i][k][(p.dow0 + j) % 7]++
        }

        sc1 = c1All(); sc2 = c2All(); sc41 = c41All(); sc42 = c42All(); sc41s = c41sAll(); sc42s = c42sAll()
        sc3 = c3All(p.cons3, false); hc3n = c3All(p.cons3n, true)
        sc3m = c3All(p.cons3m, false); sc3mn = c3All(p.cons3mn, true)
        hpref = prefAll(); hGrpV = groupViolAll(); hct = ctAll(); sApt = aptAll(); sFair = fairAll(); sWeekly = weeklyAll(); scovO = covOAll()
        covUTot = covUAll()
    }

    // ---- helpers ---------------------------------------------------------------

    private fun viol01(b: Boolean): Long = if (b) 1L else 0L

    private fun rangeViol(i: Int, k: Int, n: Int): Long {
        // [統一b] UnifiedViolationChecker と同分類(SOFT)・同重み: low(lo!=0, canDo必須)=amount×90 / high=amount×45。
        val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]
        var v = 0L
        if (lo != Int.MIN_VALUE && lo != 0 && n < lo && p.canDo(i, k)) v += (lo - n).toLong() * 90L
        if (hi != Int.MAX_VALUE && n > hi) v += (n - hi).toLong() * 45L
        return v
    }

    /** [統一a] 1セル(shift k, 日 j)の過剰被覆量。上限 hi=(use2&&need2>=0?need2:need1)。checker covO と同一。 */
    private fun covOAll(): Long {
        var s = 0L
        for (k in 0 until K) for (j in 0 until T) s += p.covOCell(k, j, cntDay[k][j]).toLong()
        return s
    }

    private fun c1Local(i: Int, j: Int): Long {
        var tot = 0L
        for (c in p.cons1) {
            if (!p.canDo(i, c.shiftIdx)) continue   // [統一] 担当不可は対象外(チェッカーと一致)
            val js0 = maxOf(0, j - c.day1 + 1); val js1 = minOf(T - c.day1, j)
            var js = js0
            while (js <= js1) {
                var z = 0; var l = 0
                while (l < c.day1) { if (a[i][js + l] == c.shiftIdx) z++; l++ }
                if (z < c.day2) tot += 1   // [統一] #fire 計上。重みは soft 集約で×4
                js++
            }
        }
        return tot
    }

    private fun c3Local(i: Int, j: Int, list: List<C3>, fbd: Boolean): Long {
        var sub = 0L
        for (c in list) {
            val seq = c.seq; val D = seq.size
            if (D == 0) continue
            // [HF507] single-shift run: deficit is per-staff whole-row, not windowed.
            // A move at (i,j) only affects staff i's row, so recompute row i's run deficit
            // (before/after via the caller's swap captures the delta correctly).
            if (!fbd && C3Run.isSingleShiftSeq(seq)) {
                sub += C3Run.rowDeficit(a, i, seq[0], D)
                continue
            }
            val js0 = maxOf(0, j - D + 1); val js1 = minOf(T - D, j)
            var js = js0
            while (js <= js1) {
                if (a[i][js] == seq[0]) {
                    var z = 0; var l = 1
                    while (l < D) { if (a[i][js + l] == seq[l]) z++; l++ }
                    val fire = if (fbd) (z == D - 1) else (z < D - 1)
                    if (fire) sub += 1   // [統一] #fire 計上。重みは soft 集約で適用
                }
                js++
            }
        }
        return sub
    }

    private fun c1All(): Long {
        var tot = 0L
        for (c in p.cons1) for (i in 0 until S) {
            if (!p.canDo(i, c.shiftIdx)) continue   // [統一] 担当不可は対象外(チェッカーと一致)
            var js = 0
            while (js <= T - c.day1) {
                var z = 0; var l = 0
                while (l < c.day1) { if (a[i][js + l] == c.shiftIdx) z++; l++ }
                if (z < c.day2) tot += 1   // [統一] #fire 計上。重みは soft 集約で×4
                js++
            }
        }
        return tot
    }

    private fun c2All(): Long {
        var tot = 0L
        // [監査#5] 担当不可の職員は対象外（チェッカーと同一条件）
        for (c in p.cons2) for (i in 0 until S) if (p.canDo(i, c.shiftIdx) && cntSS[i][c.shiftIdx] < c.count) tot += 1
        return tot
    }

    private fun c41All(): Long {
        var tot = 0L
        for (c in p.cons41) for (j in 0 until T) {
            var z = 0
            for (i in 0 until S) if (p.sgrp[i] == c.groupIdx && a[i][j] == c.shiftIdx) z++
            if (z < c.l || c.u < z) tot += 1
        }
        return tot
    }

    private fun c42All(): Long {
        var tot = 0L
        for (c in p.cons42) for (j in 0 until T) {
            var n1 = 0; var n2 = 0
            for (i in 0 until S) {
                if (p.sgrp[i] == c.g1 && a[i][j] == c.s1) n1++
                if (p.sgrp[i] == c.g2 && a[i][j] == c.s2) n2++
            }
            tot += c42PairCount(c.g1 == c.g2 && c.s1 == c.s2, n1, n2)
        }
        return tot
    }

    private fun c41sAll(): Long {
        var tot = 0L
        for (c in p.cons41s) for (j in 0 until T) {
            var z = 0
            for (i in 0 until S) if (p.ssk[i] == c.groupIdx && a[i][j] == c.shiftIdx) z++
            if (z < c.l || c.u < z) tot += 1
        }
        return tot
    }

    private fun c42sAll(): Long {
        var tot = 0L
        for (c in p.cons42s) for (j in 0 until T) {
            var n1 = 0; var n2 = 0
            for (i in 0 until S) {
                if (p.ssk[i] == c.g1 && a[i][j] == c.s1) n1++
                if (p.ssk[i] == c.g2 && a[i][j] == c.s2) n2++
            }
            tot += c42PairCount(c.g1 == c.g2 && c.s1 == c.s2, n1, n2)
        }
        return tot
    }

    private fun c3All(list: List<C3>, fbd: Boolean): Long {
        var sub = 0L
        for (c in list) {
            val seq = c.seq; val D = seq.size
            if (D == 0) continue
            // [HF507] non-forbidden single-shift run -> run deficit (per staff whole-row)
            if (!fbd && C3Run.isSingleShiftSeq(seq)) {
                for (i in 0 until S) sub += C3Run.rowDeficit(a, i, seq[0], D)
                continue
            }
            for (i in 0 until S) {
                var j = 0
                while (j <= T - D) {
                    if (a[i][j] == seq[0]) {
                        var z = 0; var l = 1
                        while (l < D) { if (a[i][j + l] == seq[l]) z++; l++ }
                        val fire = if (fbd) (z == D - 1) else (z < D - 1)
                        if (fire) sub += 1   // [統一] #fire 計上。重みは soft 集約で適用
                    }
                    j++
                }
            }
        }
        return sub
    }

    private fun prefAll(): Long {
        var h = 0L
        for (i in 0 until S) for (j in 0 until T) {
            val w = p.wish[i][j]; if (w >= 0 && p.canDo(i, w) && a[i][j] != w) h++
        }
        return h
    }

    /** [3.318.0] 担当できないシフトに就いているセル数（checker の "groupViol" と同一条件）。 */
    private fun groupViolAll(): Long {
        var h = 0L
        for (i in 0 until S) for (j in 0 until T) {
            val k = a[i][j]; if (k in 0 until K && !p.canDo(i, k)) h++
        }
        return h
    }

    private fun ctAll(): Long {
        var h = 0L
        for (i in 0 until S) for (k in 0 until K) h += rangeViol(i, k, cntSS[i][k])
        return h
    }

    /** [統一apt] 1セル(staff i, shift k)の適切回数偏差。重み1の L1 |n-t|。UnifiedViolationChecker の "apt" と一致。 */
    private fun aptViol(i: Int, k: Int, n: Int): Long {
        val t = p.apt[i][k]
        return if (t >= 0) kotlin.math.abs(n - t).toLong() else 0L
    }
    private fun aptAll(): Long {
        var h = 0L
        for (i in 0 until S) for (k in 0 until K) h += aptViol(i, k, cntSS[i][k])
        return h
    }

    /** [統一fair] 群g・シフトk の公平化偏差。staff [special] のカウントに [delta] を加味（preview用）。
     *  round(平均) からのメンバー L1 偏差和。UnifiedViolationChecker の "fair" と一致。 */
    private fun fairDevAt(g: Int, k: Int, special: Int, delta: Int): Long {
        val mem = p.groupMembers[g]
        val m = mem.size
        if (m < 2) return 0L
        var sum = 0
        for (x in mem) sum += cntSS[x][k] + (if (x == special) delta else 0)
        val tgt = Math.round(sum.toDouble() / m).toInt()
        var d = 0L
        for (x in mem) { val c = cntSS[x][k] + (if (x == special) delta else 0); d += kotlin.math.abs(c - tgt).toLong() }
        return d
    }
    private fun fairAll(): Long {
        var h = 0L
        for (g in 0 until p.G) for (k in p.bucket[g]) h += fairDevAt(g, k, -1, 0)
        return h
    }

    /** [統一weekly/3.345.0] 全職員×全シフトの曜日平準化偏差の総和。wdCnt を単一ソースとし checker/Evaluator と同一。 */
    private fun weeklyAll(): Long {
        var h = 0L
        for (i in 0 until S) for (k in 0 until K) h += weeklyDevOfBucket(wdCnt[i][k]).toLong()
        return h
    }

    private fun covUAll(): Long {
        var t = 0L
        for (j in 0 until T) for (k in 0 until K) t += p.covUCell(k, j, cntDay[k][j]).toLong()
        return t
    }
}
