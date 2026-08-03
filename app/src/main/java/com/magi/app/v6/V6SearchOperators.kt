package com.magi.app.v6

import java.util.Random
import kotlin.math.exp
import kotlin.math.max

// [リファクタ Phase2] V6NativeOptimizer から抽出した純粋ヘルパ群（オブジェクト状態に非依存・
// 引数 Problem/DeltaEvaluator/GlsPenalty/Random のみ）。ターゲット修正(findXxx)と生スコアの
// 受理判定・差分ユーティリティ。同一パッケージのトップレベル internal 関数なので呼び出し側は不変。

// ── findXxx: ターゲット型 single-cell 修正を [i, j, newK] で返す（無ければ null）。
// 集合構築は 2 パス（個数を数えてから N 番目を選ぶ）で ArrayList/filter を排し GC 圧を下げる。
// ALNS の直接評価アームから eval+cur へ copy2D なしで適用される。

internal fun findCovOFix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    if (p.T == 0 || p.K == 0) return null
    val j = rng.nextInt(p.T)
    var overK = -1; var maxOver = 0
    for (k in 0 until p.K) {
        val lo = p.need1[k][j]; if (lo < 0) continue
        val hi = if (p.use2 && p.need2[k][j] >= 0) p.need2[k][j] else lo
        val over = eval.countOnDay(k, j) - hi
        if (over > maxOver) { maxOver = over; overK = k }
    }
    if (overK < 0) return null
    var wCnt = 0
    for (i in 0 until p.S) if (eval.at(i, j) == overK && !p.wishLocked(i, j)) wCnt++
    if (wCnt == 0) return null
    var pickW = rng.nextInt(wCnt); var i = 0
    for (ii in 0 until p.S) if (eval.at(ii, j) == overK && !p.wishLocked(ii, j)) { if (pickW-- == 0) { i = ii; break } }
    var bestNw = -1; var bestDef = Int.MIN_VALUE
    for (k in 0 until p.K) {
        if (k == overK || !p.canDo(i, k)) continue
        val lo = p.need1[k][j]
        val def = if (lo >= 0) lo - eval.countOnDay(k, j) else 0
        if (def > bestDef) { bestDef = def; bestNw = k }
    }
    return if (bestNw >= 0) intArrayOf(i, j, bestNw) else null
}

internal fun findC2Fix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    if (p.cons2.isEmpty()) return null
    val c = p.cons2[rng.nextInt(p.cons2.size)]
    var dCnt = 0
    for (i in 0 until p.S) { if (!p.canDo(i, c.shiftIdx)) continue; if (eval.countForStaff(i, c.shiftIdx) < c.count) dCnt++ }
    if (dCnt == 0) return null
    var pickI = rng.nextInt(dCnt); var stf = 0
    for (i in 0 until p.S) { if (!p.canDo(i, c.shiftIdx)) continue; if (eval.countForStaff(i, c.shiftIdx) < c.count) { if (pickI-- == 0) { stf = i; break } } }
    var dayCnt = 0
    for (j in 0 until p.T) if (eval.at(stf, j) != c.shiftIdx && !p.wishLocked(stf, j)) dayCnt++
    if (dayCnt == 0) return null
    var pickJ = rng.nextInt(dayCnt); var day = 0
    for (j in 0 until p.T) if (eval.at(stf, j) != c.shiftIdx && !p.wishLocked(stf, j)) { if (pickJ-- == 0) { day = j; break } }
    return intArrayOf(stf, day, c.shiftIdx)
}

internal fun findRangeLowFix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    var cCnt = 0
    for (i in 0 until p.S) for (k in 0 until p.K) { val lo = p.rangeLo[i][k]; if (lo == Int.MIN_VALUE || !p.canDo(i, k)) continue; if (eval.countForStaff(i, k) < lo) cCnt++ }
    if (cCnt == 0) return null
    var pickC = rng.nextInt(cCnt); var rlI = 0; var rlK = 0
    outer@ for (i in 0 until p.S) for (k in 0 until p.K) { val lo = p.rangeLo[i][k]; if (lo == Int.MIN_VALUE || !p.canDo(i, k)) continue; if (eval.countForStaff(i, k) < lo) { if (pickC-- == 0) { rlI = i; rlK = k; break@outer } } }
    var dayCnt = 0
    for (j in 0 until p.T) if (eval.at(rlI, j) != rlK && !p.wishLocked(rlI, j)) dayCnt++
    if (dayCnt == 0) return null
    var pickJ = rng.nextInt(dayCnt); var day = 0
    for (j in 0 until p.T) if (eval.at(rlI, j) != rlK && !p.wishLocked(rlI, j)) { if (pickJ-- == 0) { day = j; break } }
    return intArrayOf(rlI, day, rlK)
}

internal fun findC41Fix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    if (p.cons41.isEmpty() || p.T == 0) return null
    val c = p.cons41[rng.nextInt(p.cons41.size)]
    val j = rng.nextInt(p.T)
    var cnt = 0
    for (i in 0 until p.S) if (p.sgrp[i] == c.groupIdx && eval.at(i, j) == c.shiftIdx) cnt++
    return when {
        cnt > c.u -> {
            var wCnt = 0
            for (i in 0 until p.S) if (p.sgrp[i] == c.groupIdx && eval.at(i, j) == c.shiftIdx && !p.wishLocked(i, j)) wCnt++
            if (wCnt == 0) return null
            var pickW = rng.nextInt(wCnt); var ci = 0
            for (i in 0 until p.S) if (p.sgrp[i] == c.groupIdx && eval.at(i, j) == c.shiftIdx && !p.wishLocked(i, j)) { if (pickW-- == 0) { ci = i; break } }
            val allowed41 = p.allowedShiftsForStaff(ci)
            var oCnt = 0; for (ak in allowed41) if (ak != c.shiftIdx) oCnt++
            if (oCnt == 0) return null
            var pickK = rng.nextInt(oCnt); var nwK = 0
            for (ak in allowed41) if (ak != c.shiftIdx) { if (pickK-- == 0) { nwK = ak; break } }
            intArrayOf(ci, j, nwK)
        }
        cnt < c.l -> {
            var aCnt = 0
            for (i in 0 until p.S) if (p.sgrp[i] == c.groupIdx && eval.at(i, j) != c.shiftIdx && !p.wishLocked(i, j) && p.canDo(i, c.shiftIdx)) aCnt++
            if (aCnt == 0) return null
            var pickA = rng.nextInt(aCnt); var ai = 0
            for (i in 0 until p.S) if (p.sgrp[i] == c.groupIdx && eval.at(i, j) != c.shiftIdx && !p.wishLocked(i, j) && p.canDo(i, c.shiftIdx)) { if (pickA-- == 0) { ai = i; break } }
            intArrayOf(ai, j, c.shiftIdx)
        }
        else -> null
    }
}

/** c41 のスキルグループ版（ssk + cons41s）。形は findC41Fix と同一。 */
internal fun findC41sFix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    if (p.cons41s.isEmpty() || p.T == 0) return null
    val c = p.cons41s[rng.nextInt(p.cons41s.size)]
    val j = rng.nextInt(p.T)
    var cnt = 0
    for (i in 0 until p.S) if (p.ssk[i] == c.groupIdx && eval.at(i, j) == c.shiftIdx) cnt++
    return when {
        cnt > c.u -> {
            var wCnt = 0
            for (i in 0 until p.S) if (p.ssk[i] == c.groupIdx && eval.at(i, j) == c.shiftIdx && !p.wishLocked(i, j)) wCnt++
            if (wCnt == 0) return null
            var pickW = rng.nextInt(wCnt); var ci = 0
            for (i in 0 until p.S) if (p.ssk[i] == c.groupIdx && eval.at(i, j) == c.shiftIdx && !p.wishLocked(i, j)) { if (pickW-- == 0) { ci = i; break } }
            val allowed41 = p.allowedShiftsForStaff(ci)
            var oCnt = 0; for (ak in allowed41) if (ak != c.shiftIdx) oCnt++
            if (oCnt == 0) return null
            var pickK = rng.nextInt(oCnt); var nwK = 0
            for (ak in allowed41) if (ak != c.shiftIdx) { if (pickK-- == 0) { nwK = ak; break } }
            intArrayOf(ci, j, nwK)
        }
        cnt < c.l -> {
            var aCnt = 0
            for (i in 0 until p.S) if (p.ssk[i] == c.groupIdx && eval.at(i, j) != c.shiftIdx && !p.wishLocked(i, j) && p.canDo(i, c.shiftIdx)) aCnt++
            if (aCnt == 0) return null
            var pickA = rng.nextInt(aCnt); var ai = 0
            for (i in 0 until p.S) if (p.ssk[i] == c.groupIdx && eval.at(i, j) != c.shiftIdx && !p.wishLocked(i, j) && p.canDo(i, c.shiftIdx)) { if (pickA-- == 0) { ai = i; break } }
            intArrayOf(ai, j, c.shiftIdx)
        }
        else -> null
    }
}

internal fun findRangeHighFix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    var cCnt = 0
    for (i in 0 until p.S) for (k in 0 until p.K) { val hi = p.rangeHi[i][k]; if (hi == Int.MAX_VALUE) continue; if (eval.countForStaff(i, k) > hi) cCnt++ }
    if (cCnt == 0) return null
    var pickC = rng.nextInt(cCnt); var rhI = 0; var rhK = 0
    outer@ for (i in 0 until p.S) for (k in 0 until p.K) { val hi = p.rangeHi[i][k]; if (hi == Int.MAX_VALUE) continue; if (eval.countForStaff(i, k) > hi) { if (pickC-- == 0) { rhI = i; rhK = k; break@outer } } }
    var dayCnt = 0
    for (j in 0 until p.T) if (eval.at(rhI, j) == rhK && !p.wishLocked(rhI, j)) dayCnt++
    if (dayCnt == 0) return null
    var pickJ = rng.nextInt(dayCnt); var day = 0
    for (j in 0 until p.T) if (eval.at(rhI, j) == rhK && !p.wishLocked(rhI, j)) { if (pickJ-- == 0) { day = j; break } }
    val allowed = p.allowedShiftsForStaff(rhI)
    var oCnt = 0; for (ak in allowed) if (ak != rhK) oCnt++
    if (oCnt == 0) return null
    var pickK = rng.nextInt(oCnt); var nwK = 0
    for (ak in allowed) if (ak != rhK) { if (pickK-- == 0) { nwK = ak; break } }
    return intArrayOf(rhI, day, nwK)
}

internal fun findC3WantFix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    val list = when {
        p.cons3.isNotEmpty() && p.cons3m.isNotEmpty() -> if (rng.nextBoolean()) p.cons3 else p.cons3m
        p.cons3.isNotEmpty() -> p.cons3
        p.cons3m.isNotEmpty() -> p.cons3m
        else -> return null
    }
    val c = list[rng.nextInt(list.size)]
    val seq = c.seq; val D = seq.size
    if (D < 2 || D > p.T) return null
    val iStart = rng.nextInt(p.S)
    for (di in 0 until p.S) {
        val i = (iStart + di) % p.S
        var j = 0
        while (j <= p.T - D) {
            if (eval.at(i, j) == seq[0]) {
                var miss = 0; var missL = -1
                for (l in 1 until D) {
                    if (eval.at(i, j + l) != seq[l]) { miss++; if (miss > 1) break else missL = l }
                }
                if (miss == 1 && missL >= 0) {
                    val ml = j + missL
                    if (!p.wishLocked(i, ml) && p.canDo(i, seq[missL])) return intArrayOf(i, ml, seq[missL])
                }
            }
            j++
        }
    }
    return null
}

/**
 * 8 種のターゲット修正(covO/c2/low/c41/high/c41s/c3want/apt)を一様シャッフル順に試し、最初に見つかった修正を返す（無ければ null）。
 * 1 種が null でも次へフォールスルーするため、違反が少ない近最適解でも毎反復に有効手を当てやすい。
 */
internal fun findTargetedFix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    val order = IntArray(8) { it }
    for (i in 7 downTo 1) { val j = rng.nextInt(i + 1); val t = order[i]; order[i] = order[j]; order[j] = t }
    for (idx in order) {
        val fix = when (idx) {
            0 -> findCovOFix(p, eval, rng)
            1 -> findC2Fix(p, eval, rng)
            2 -> findRangeLowFix(p, eval, rng)
            3 -> findC41Fix(p, eval, rng)
            4 -> findRangeHighFix(p, eval, rng)
            5 -> findC41sFix(p, eval, rng)
            6 -> findC3WantFix(p, eval, rng)
            else -> findAptFix(p, eval, rng)
        }
        if (fix != null) return fix
    }
    return null
}

/** [apt研磨] 適切回数(apt)の偏差を1セルで縮める手を探す。あるスタッフで apt 超過のシフト kOver を
 *  1日、apt 不足の担当可シフト kUnder へ振り替える（超過−1・不足−1 の双方向改善）。
 *  既存の find*Fix には apt 専用がなく、apt 超過(例: 単一専門職の休過多)が研磨で直らなかったため追加。
 *  担当不可・希望ロックの日は除外。covO 等の副作用は呼び出し側スコアの受理で評価。 */
internal fun findAptFix(p: Problem, eval: DeltaEvaluator, rng: Random): IntArray? {
    if (p.S == 0 || p.T == 0) return null
    val order = IntArray(p.S) { it }
    for (i in p.S - 1 downTo 1) { val j = rng.nextInt(i + 1); val t = order[i]; order[i] = order[j]; order[j] = t }
    for (i in order) {
        val allowed = p.allowedShiftsForStaff(i)
        if (allowed.isEmpty()) continue
        val cnt = IntArray(p.K)
        for (j in 0 until p.T) cnt[eval.at(i, j)]++
        var kOver = -1; var kUnder = -1
        for (k in 0 until p.K) {
            val tg = p.apt[i][k]
            if (tg < 0) continue
            if (kOver < 0 && cnt[k] > tg) kOver = k
            if (kUnder < 0 && cnt[k] < tg && allowed.contains(k)) kUnder = k
        }
        if (kOver < 0 || kUnder < 0 || kOver == kUnder) continue
        val dayStart = rng.nextInt(p.T)
        for (d in 0 until p.T) {
            val j = (dayStart + d) % p.T
            if (!p.wishLocked(i, j) && eval.at(i, j) == kOver) return intArrayOf(i, j, kUnder)
        }
    }
    return null
}

/** [GLS] 1 セルの割当変更による penalty 拡張分の差分（変更セルだけで O(1)）。 */
internal fun glsMoveAug(gls: GlsPenalty, i: Int, j: Int, oldK: Int, nwK: Int): Double =
    if (oldK == nwK) 0.0 else gls.lambda * (gls.penaltyOf(i, j, nwK) - gls.penaltyOf(i, j, oldK)).toDouble()

/** DeltaEvaluator 生スコア(hard*1_000_000+soft)の比較。小さいほど良い。 */
internal fun betterScore(a: Long, b: Long): Boolean = a < b

/** DeltaEvaluator 生スコアの SA 受理。hard が +2 超増える手は却下。 */
internal fun acceptWorseScore(a: Long, b: Long, temp: Double, rng: Random): Boolean {
    // [3.213.0見落とし修正] SCORE_HARD_UNIT を1e6→1e9へ拡大した際にこの閾値だけ旧スケール(2*1e6)の
    //   まま残っていた。新スケールでは2*1e6は1 HARD単位(1e9)の0.2%に過ぎず、hardが1増えるだけで
    //   即座に(却下する意図の"+2超"よりずっと手前で)却下される退行だった。2*SCORE_HARD_UNITへ同期。
    if (a > b + 2 * SCORE_HARD_UNIT) return false
    val delta = (a - b).toDouble()
    return delta <= 0.0 || rng.nextDouble() < exp(-max(0.0, delta) / (200.0 * temp + 1e-9))
}

/**
 * [3.303.0] 職員 i を day j で [fillShift] にしたとき成立する禁止パターンについて、「崩しに行ける日」を
 * j に近い順で返す（j 自身は目的のシフトなので除く）。
 *
 * 旧実装は j-1 / j+1 の2日**固定**だった。実データの cons3n には `Dﾃ→休→A4` のような3連があり、
 * 当日が末尾(A4)のときパターンの先頭は j-2 にあるため、**旧実装ではそのパターンを一度も崩せなかった**
 * （c1 研磨の不採用主因が c3n だと 3.302.0 のログ強化で実測できたことが、この穴を追う入口になった）。
 * ここでは実際に成立している窓がまたぐ日をビット走査で求め、その全部を候補にする。
 * T>64 は `C3nBitScan` が使えないので従来どおり j±1 のみ（安全側＝候補が減るだけ）。
 */
internal fun breakableDaysFor(p: Problem, sched: Array<IntArray>, i: Int, j: Int, fillShift: Int): IntArray {
    // [既定 OFF・3.303.0] 一般化として正しいが実データ3件で利得が一貫しなかった（PolishGate の
    //   docstring に計測値）。既定は従来どおり j±1 のみで、ゲートを ON にしたときだけ広げる。
    if (!PolishGate.wideC3nBreakDays) return intArrayOf(j - 1, j + 1)
    if (!C3nBitScan.usable(p) || i !in sched.indices) return intArrayOf(j - 1, j + 1)
    val row = sched[i]
    val mask = C3nBitScan.buildRowMask(p, row)
    val old = if (j in 0 until p.T && j < row.size) row[j] else -1
    val days = C3nBitScan.coveringRunDaysAfterSet(p, mask, j, old, fillShift)
    if (days == 0L) return IntArray(0)
    // j に近い日から試す（当日から遠い日ほど他の制約への波及が読みにくいため、影響の小さい順）。
    val out = ArrayList<Int>(java.lang.Long.bitCount(days))
    var rest = days and (1L shl j).inv()
    while (rest != 0L) {
        out.add(java.lang.Long.numberOfTrailingZeros(rest))
        rest = rest and (rest - 1)
    }
    out.sortBy { kotlin.math.abs(it - j) }
    return out.toIntArray()
}

/**
 * [3.163.0でfindCovUChain内に導入・3.226.0で共通ヘルパーへ汎用化] 職員 i を day j で fillShift へ
 * 動かすと禁止連続(c3n)に触れる場合、パターンがまたぐ日の i 自身の割当を別シフトへ変えて
 * 崩せないか試す（3.303.0 で j±1 固定 → 実際に成立している窓の全日へ拡張）。
 * 変更で空くシフト(oldJ2)が covU 悪化を招くなら、findCovUChain を
 * `allowCrossDayFix=false` で1段だけ再帰し玉突き連鎖として埋め直す（無限展開防止）。
 * 見つかれば [(i, j2, alt), ...サブ連鎖]（すべて day j2 のみの手）を返す（盤面は判定中に一時変更するが
 * 必ず復元＝呼び出し側が実際に適用するかを決める）。見つからなければ null。
 * findCovUChain（covU側の禁止連続回避）・applyCovOFree（covO側、3.226.0で追加採用）から共通利用。
 * [3.232.0/ドッグフーディングで発見] maxDepth既定は findCovUChain と同じ理由で (p.K-1).coerceAtLeast(1)
 * （下記 findCovUChain の docstring 参照）。
 */
internal fun tryFixForbiddenRunViaAdjacentDay(
    p: Problem, sched: Array<IntArray>, i: Int, j: Int, fillShift: Int, rng: Random,
    allowCrossDayFix: Boolean = true, maxDepth: Int = (p.K - 1).coerceAtLeast(1),
): List<IntArray>? {
    if (!allowCrossDayFix) return null
    for (j2 in breakableDaysFor(p, sched, i, j, fillShift)) {
        if (j2 !in 0 until p.T || p.wishLocked(i, j2)) continue
        val oldJ2 = sched[i][j2]
        if (oldJ2 !in 0 until p.K) continue
        // 候補シフト: 担当可能シフトを順に試す。
        // [3.345.0] 休は通常のシフト種の一つ＝先頭に置く優先をやめた（旧: 休を第一候補にしていた）。
        //   実データ3件の後処理研磨で最終盤面がバイト一致＝この優先は実質不活性だった。
        val altOrder = ArrayList<Int>()
        for (s in p.allowedShiftsForStaff(i)) if (s != oldJ2) altOrder.add(s)
        for (alt in altOrder) {
            val cntBefore = (0 until p.S).count { sched[it][j2] == oldJ2 }
            sched[i][j2] = alt   // [一時変更] 下の判定後に必ず復元する
            val jOk = !p.makesForbiddenRun(sched, i, j, fillShift)
            val j2Ok = !p.makesForbiddenRun(sched, i, j2, alt)
            if (!jOk || !j2Ok) { sched[i][j2] = oldJ2; continue }
            if (p.covUCell(oldJ2, j2, cntBefore - 1) > p.covUCell(oldJ2, j2, cntBefore)) {
                // i の離脱で oldJ2 が covU 悪化 → 同アルゴリズムを1段だけ再帰して埋め直す。
                val subChain = findCovUChain(p, sched, oldJ2, j2, rng, maxDepth = maxDepth, exclude = i, allowCrossDayFix = false)
                sched[i][j2] = oldJ2
                if (subChain != null) return listOf(intArrayOf(i, j2, alt)) + subChain
            } else {
                sched[i][j2] = oldJ2
                return listOf(intArrayOf(i, j2, alt))
            }
        }
    }
    return null
}

/**
 * [E11/多人数ブロック移動] covU セル (k0, j) を同日・多人数の「玉突き連鎖」で充填する交代連鎖を
 * BFS（最短優先）で探す。対象の failure mode（実機 2026-08 データ・ユーザー指摘で確認）:
 * 直接充填の候補が (a)希望ロック (b)単一被覆シフト在勤＝引き抜くと玉突き covU (c)禁止連続 に当たり、
 * 既存の修復オペレータ（destroyRepairDay=「休→勤務」しか試さない）では踏めない「勤務→勤務」連鎖でのみ
 * 埋まる局面。ユーザー実例: 8/11 モニカ B4→Cｵ（深さ1・過剰B4から補充）／8/17 上條 Cｵ→Cｱ, 山本 →Cｵ（深さ2）。
 *
 * 探索: 「k0 を職員 i が埋める → i が空けたシフト m を次の職員が埋める → … → 空けても covU が増えない
 * シフト（需要0 or 余裕あり）で終端」。リンク条件: canDo・非wishLocked・禁止連続(c3n, 任意長=三連/五連等)の
 * プルーニング・同一職員の再訪なし・同一シフト展開の再訪なし・深さ≤maxDepth。
 * 同日内交換なので被覆総量は保存。
 *
 * [3.232.0/ドッグフーディングで発見・maxDepth既定を(p.K-1)へ引き上げ] `visited`はシフト単位
 * (BooleanArray(p.K))で管理されるため、本BFSは元々シフト数K種を超えて展開できない＝maxDepthを
 * K以上にしても計算量は増えない（自然にO(K×S)で頭打ち）。旧既定5(「最大5人の玉突き」という
 * 検証しやすさ重視の設計意図)は、実データでこの上限より深い箇所にのみ解が存在する場合に
 * 「候補なし」として誤って諦めてしまう（桒澤美幸のAｱ超過・8/6のCｱ不足など、RangePolish/RSI covU
 * focusで繰り返し「候補なし/玉突き必要」のまま残る事例で確認）。計算コストがほぼ無視できることを
 * 踏まえ、既定を(p.K-1).coerceAtLeast(1)（=シフト全種を使い切るまで探索）へ引き上げた
 * （ユーザー承認: 「人間が検証しやすい5人まで」の設計意図より網羅性を優先）。
 *
 * [禁止連続の回避=隣接日調整] 候補 i が k0 を埋めると禁止連続(c3n)に触れる場合、即除外せず、
 * 隣接日(j-1/j+1)の i 自身の割当も変えてパターンを崩せないか試す（`tryFixC3nViaAdjacentDay`）。
 * その調整で i の隣接日の元シフトが空き covU が悪化するなら、そこも同じ findCovUChain を
 * `allowCrossDayFix=false` で再帰し玉突き連鎖として埋め直す（cross-day 再帰は1段のみ＝無限展開防止）。
 * 見つかった追加手は Node.extra に積み、最終手順に合流する。
 *
 * 返り値 = 適用手 [(i, j, newK), ...]（本関数は盤面を変更しない。適用と採否=keep-best は呼び出し側＝
 * スコアリング不変・退化不能）。見つからなければ null。
 */
internal fun findCovUChain(
    p: Problem, sched: Array<IntArray>, k0: Int, j: Int, rng: Random, maxDepth: Int = (p.K - 1).coerceAtLeast(1), exclude: Int = -1, allowCrossDayFix: Boolean = true,
    // [C1研磨・手B強化] 候補(staff,shift,day)がその職員自身のc1不足解消にも資するかの述語。
    //   非nullのとき candidates() の返り値を「述語=true」優先に並べ替えるだけ（探索ロジック自体は不変）。
    //   既定null=既存呼出元は完全に挙動不変。
    c1Pref: ((staff: Int, shift: Int, day: Int) -> Boolean)? = null,
    // [頭打ち調査で判明・RangePolish/C3mnPolish/C3RunPolish向け] candidates() は構造的妥当性
    //   (canDo/希望ロック/禁止連続)のみで選び、rng順の最初の1件が完成すればそれで確定＝コスト無視。
    //   桒澤美幸のAｱ超過(3.215.0)研磨が頭打ちする実例を追跡した結果、「候補自身のstaffRange上限を
    //   新たに超えさせる」手を先頭で引くと、excess(3)+excess(1)で合計は不変＝isBetterが改善なしとして
    //   却下し、その日は二度と試行されない（1日1回きりの呼出のため）ことを確認。rangeAvoid が真を返す
    //   候補は「除外」でなく「後回し」にするだけ（他に候補が無ければ従来どおり使う＝解が消えない）。
    //   既定null=既存呼出元は完全に挙動不変。
    rangeAvoid: ((staff: Int, fillShift: Int) -> Boolean)? = null,
): List<IntArray>? {
    if (j !in 0 until p.T || k0 !in 0 until p.K || p.S == 0) return null
    val cnt = IntArray(p.K)
    for (i in 0 until p.S) { val kk = sched[i][j]; if (kk in 0 until p.K) cnt[kk]++ }
    // 充填で covU が実際に減るセルのみ対象（need 未設定などは対象外）。
    if (p.covUCell(k0, j, cnt[k0] + 1) >= p.covUCell(k0, j, cnt[k0])) return null

    // [三連/五連など任意長対応] 禁止連続(c3n)を作る移動を除外（最終ゲートは呼び出し側 checker が
    //   担保＝ここは成功率向上の枝刈り。Problem.makesForbiddenRun が任意長ルールを一般判定）。
    fun c3nHits(i: Int, newK: Int): Boolean = p.makesForbiddenRun(sched, i, j, newK)

    // [禁止連続の回避=隣接日調整] i を k0(day j) へ動かすと禁止連続に触れるとき、隣接日(j-1/j+1)の
    //   i の割当を別シフトへ変えてパターンを崩せないか試す（共通ヘルパー tryFixForbiddenRunViaAdjacentDay
    //   に汎用化・3.226.0でapplyCovOFreeからも再利用）。
    fun tryFixC3nViaAdjacentDay(i: Int, fillShift: Int): List<IntArray>? =
        tryFixForbiddenRunViaAdjacentDay(p, sched, i, j, fillShift, rng, allowCrossDayFix, maxDepth)

    // BFS ノード = 「fillShift へ staff が入る」手。子 = staff が空けた現シフトを埋める手。
    // extra = 禁止連続を回避するための追加手（隣接日調整＋サブ連鎖。無ければ null）。
    class Node(val fillShift: Int, val staff: Int, val prev: Node?, val extra: List<IntArray>? = null)

    // 職員の走査順を乱択（同型解の多様化。決定性が欲しい呼び出しは seed 固定の rng を渡す）。
    val order = IntArray(p.S) { it }
    for (x in p.S - 1 downTo 1) { val y = rng.nextInt(x + 1); val t = order[x]; order[x] = order[y]; order[y] = t }

    fun candidates(fillShift: Int, prev: Node?): List<Node> {
        val out = ArrayList<Node>()
        for (i in order) {
            if (i == exclude) continue   // [C1×E11] 呼出元が別途動かした職員を連鎖の候補から除外（無効な回帰手を防ぐ）
            val m = sched[i][j]
            if (m !in 0 until p.K || m == fillShift) continue
            if (!p.canDo(i, fillShift) || p.wishLocked(i, j)) continue
            var q = prev; var used = false
            while (q != null) { if (q.staff == i) { used = true; break }; q = q.prev }
            if (used) continue
            if (c3nHits(i, fillShift)) {
                val fix = tryFixC3nViaAdjacentDay(i, fillShift) ?: continue
                out.add(Node(fillShift, i, prev, extra = fix))
                continue
            }
            out.add(Node(fillShift, i, prev))
        }
        // [頭打ち調査] rangeAvoid が真の候補（自身の新規range違反を招く）を後回しへ。他に候補が無ければ
        //   最終的にはこの中から使われる＝解が消えることはない（「除外」でなく「並べ替え」のみ）。
        var result: List<Node> = out
        if (rangeAvoid != null && result.size > 1) {
            val (keep, avoid) = result.partition { !rangeAvoid(it.staff, fillShift) }
            result = keep + avoid
        }
        // [C1研磨・手B強化] c1Pref を満たす候補を先頭へ（tryComplete は frontier を先頭から見て
        //   最初に成立した連鎖を採用するため、並べ替えだけで「その職員のc1不足も一緒に解消する連鎖」が
        //   優先的に見つかる。安全条件(canDo/wishLock/c3n)は上のフィルタ済のままなので探索の正しさは不変）。
        if (c1Pref != null && result.size > 1) {
            val (pref, rest) = result.partition { c1Pref(it.staff, fillShift, j) }
            result = pref + rest
        }
        return result
    }
    // 終端: このノードの職員が空けるシフト m は、1人減っても covU が増えない（需要0 or 余裕あり）。
    fun tryComplete(node: Node): List<IntArray>? {
        val m = sched[node.staff][j]
        // [敵対的レビュー修正] cnt[] は探索開始時点の静的値。祖先ノードのチェーン適用でシフト m の
        //   実際のheadcountは変わりうるため、祖先を辿って m への「到着」(+1: 祖先の fillShift==m)と
        //   「離脱」(-1: 祖先の元シフト==m、つまりその祖先はチェーンの一員として既に m を離れることが
        //   確定している)を両方加味した真のheadcountで安全性を判定する。
        //   [第2版・重要] 到着分だけを補正する初版修正は不完全だった: 祖先 a が m から離脱しつつ別の
        //   祖先 g が m へ到着するケース（3段連鎖等）では、離脱を差し引かないと m のheadcountを過大評価し、
        //   実際には covU を悪化させる連鎖を安全と誤判定しかねない（false accept）。呼出元の checker+isBetter
        //   が最終防波堤とはいえ、判定ロジック自体は到着・離脱の両方を対称に扱うのが正しい。
        var adj = 0
        var anc: Node? = node.prev
        while (anc != null) {
            val a = anc                                       // [Kotlin] var の smart-cast 回避のため局所val化
            if (a.fillShift == m) adj++                        // 祖先 a が m へ到着済み
            if (sched[a.staff][j] == m) adj--                  // 祖先 a の元シフトが m＝m から離脱済み
            anc = a.prev
        }
        val trueCnt = cnt[m] + adj
        if (p.covUCell(m, j, trueCnt - 1) > p.covUCell(m, j, trueCnt)) return null
        val moves = ArrayList<IntArray>()
        var n: Node? = node
        while (n != null) {
            moves.add(intArrayOf(n.staff, j, n.fillShift))
            n.extra?.let { moves.addAll(it) }   // [禁止連続の回避] 隣接日調整＋サブ連鎖の追加手を合流
            n = n.prev
        }
        return moves
    }

    val visited = BooleanArray(p.K).also { it[k0] = true }
    var frontier = candidates(k0, null)
    var depth = 0
    while (depth < maxDepth && frontier.isNotEmpty()) {
        for (node in frontier) tryComplete(node)?.let { return it }
        val next = ArrayList<Node>()
        for (node in frontier) {
            val m = sched[node.staff][j]
            if (m in 0 until p.K && !visited[m]) { visited[m] = true; next.addAll(candidates(m, node)) }
        }
        frontier = next
        depth++
    }
    return null
}

/**
 * 非改善手の受理判定（生スコア＝hard*1_000_000+soft）。GLS 拡張分(moveAug=候補−現行)を加味する。
 * hard が +2 超増える手は常に却下。Great Deluge は水位以下かつ hard 非増加で受理。
 */
internal fun glsAccept(
    ns: Long, curScore: Long, moveAug: Double, curAug: Double,
    mode: AcceptMode, temp: Double, gdLevel: Double, rng: Random,
): Boolean {
    // [3.213.0見落とし修正] acceptWorseScore と同じ理由で 2*SCORE_HARD_UNIT へ同期（詳細は上のコメント参照）。
    if (ns > curScore + 2 * SCORE_HARD_UNIT) return false
    return when (mode) {
        AcceptMode.GREAT_DELUGE ->
            (ns.toDouble() + curAug + moveAug) <= gdLevel && (ns / SCORE_HARD_UNIT) <= (curScore / SCORE_HARD_UNIT)
        AcceptMode.SA, AcceptMode.LAM_ADAPTIVE -> {
            // LAM_ADAPTIVE は受理式は SA と同じ Boltzmann。違いは呼び出し側が temp を受理率追従で適応させる点。
            val delta = (ns - curScore).toDouble() + moveAug
            delta <= 0.0 || rng.nextDouble() < exp(-max(0.0, delta) / (200.0 * temp + 1e-9))
        }
    }
}

/** from と to で異なるセルの flat index(i*T+j) を buf に詰め、件数を返す（ゼロアロケーション）。 */
internal fun diffInto(T: Int, from: Array<IntArray>, to: Array<IntArray>, buf: IntArray): Int {
    var n = 0
    for (i in from.indices) {
        val fr = from[i]; val tr = to[i]
        for (j in 0 until T) if (fr[j] != tr[j]) buf[n++] = i * T + j
    }
    return n
}

/**
 * [不採用の主因] 候補盤面 [after] が基準 [before] より「重み付きで最も増えた」族を返す（同点は
 * MirrorKeys.all の順で先勝ち・悪化が無ければ null）。研磨パスが候補を捨てたとき、ログに
 * 「何を壊したから捨てたのか」を族名で残すための読み取り専用ヘルパー。
 *
 * 動機: 頭打ち理由の分類（RangePolish 3.222.0 / C1Polish 3.236.0）は「候補なし（構造的に手が無い）」と
 * 「不採用（手はあるが目的関数が拒否）」を区別するが、後者の**理由**までは出していなかった。実機ログの
 * c1 残存が「不採用×65 / 候補なし×4」＝ほぼ全部が拒否だったため、次に何を緩めるべきかがログから読めない。
 * AdaptiveBlockSwap（3.293.0）は同じ趣旨の「悪化の主因」を既に出しており、その計算をここへ共通化する。
 */
internal fun worstWorsenedFamily(after: ViolationReport, before: ViolationReport): String? {
    var worstFam: String? = null
    var worstDelta = 0.0
    for (fam in MirrorKeys.all) {
        val d = ((after.breakdown[fam] ?: 0) - (before.breakdown[fam] ?: 0)) * MirrorKeys.weightOf(fam)
        if (d > worstDelta) { worstDelta = d; worstFam = fam }
    }
    return worstFam
}

/**
 * [不採用の理由・パス共通の集計, 3.303.0 → 3.321.0 で分類化] 研磨パスが候補を捨てたときの理由を数える。
 *
 * 3.302.0 で C1Polish / RangePolish に入れた職員別の主因表示を、構造の異なる他パスへも同じ形で
 * 広げるための最小の受け皿（パス単位の集計＝AdaptiveBlockSwap と同じ粒度）。各パスの不採用地点で
 * [record] を呼び、ログ末尾に [summary] を足すだけで済む。読み取り専用・採否には一切影響しない。
 *
 * [3.321.0] **理由を分類する**。旧実装は `worstWorsenedFamily` の結果だけを数えていたため、
 * 呼出側の受理条件 `isBetter(rep, best) && !exactPinRegression(...)` の**後半で落ちた候補**
 * （＝厳密ピン破り。`isBetter` 自体は true なので悪化族が存在しない）が `rejected` だけ増やして
 * 主因に何も残さず、「不採用N件(主因 …)」の N と内訳が合わなくなっていた。
 * 分類は `betterReport` の判定順（hard → weightedScore → total）と厳密に一致させ、
 * AdaptiveBlockSwap(3.293.0) が既に持っていた5分類をここへ集約する。
 */
internal class RejectCulpritStats {
    private val counts = LinkedHashMap<String, Int>()

    /** 却下の総数。以下の内訳の合計と必ず一致する。 */
    var rejected = 0
        private set

    /** 厳密ピン(lo==hi)を目標から遠ざけるため却下。違反自体は改善しているので主因族を持たない。 */
    var pinBroken = 0
        private set

    /** 必須違反(HARD)が増えるため却下。主因族つき。 */
    var hardUp = 0
        private set

    /** HARD 同値で重み付きスコアが悪化するため却下。主因族つき。 */
    var weightUp = 0
        private set

    /** HARD・重みとも同値で件数が増えるため却下。 */
    var totalUp = 0
        private set

    /** どのキーも同値＝改善しないため却下。 */
    var same = 0
        private set

    /**
     * @param pinBroken 呼出側の `exactPinRegression` の結果。
     *
     * [3.347.0/敵対検証] **「ピン破り」を名乗れるのは、目的関数が採用を認めた手をピンだけが止めたとき**
     * （そのときだけ「違反自体は改善しているので主因族を持たない」が成り立つ）。呼出12箇所は
     * `isBetter(...) && !exactPinRegression(...)` の形で、隣の `pinBlocks.record` は
     * `pinBad && isBetter(...)` と正しく絞っているのに、こちらは生の `pinBad` をそのまま受けていた。
     * 結果、**採点でも落ちる手までピン破りに数え、本当の主因族を隠していた**。実データ計測:
     * golden の c3mn 96件・fair 28件、user の c3mn 98件・fair 266件が**全件**「非改善なのにピン破り」で、
     * 98〜100% が誤ラベル。3.326.0 が `PinBlockAttribution` 側で厳密化した意味論の取り残し（教訓#31）。
     * ここで `betterReport` を通し、採点で落ちる手は従来どおり必須増／重み悪化／件数悪化へ分類する。
     */
    fun record(after: ViolationReport, before: ViolationReport, pinBroken: Boolean = false) {
        rejected++
        if (pinBroken && betterReport(after, before)) { this.pinBroken++; return }
        when {
            after.hard > before.hard -> {
                hardUp++
                worstWorsenedFamily(after, before)?.let { counts[it] = (counts[it] ?: 0) + 1 }
            }
            after.weightedScore > before.weightedScore -> {
                weightUp++
                worstWorsenedFamily(after, before)?.let { counts[it] = (counts[it] ?: 0) + 1 }
            }
            after.total > before.total -> totalUp++
            else -> same++
        }
    }

    fun summary(): String {
        if (rejected == 0) return ""
        val parts = ArrayList<String>()
        if (pinBroken > 0) parts.add("ピン破り:$pinBroken")
        if (hardUp > 0) parts.add("必須増:$hardUp")
        if (weightUp > 0) parts.add("重み悪化:$weightUp")
        if (totalUp > 0) parts.add("件数悪化:$totalUp")
        if (same > 0) parts.add("同値:$same")
        val culprits = counts.entries.sortedByDescending { it.value }.take(3)
            .joinToString(" ") { "${it.key}:${it.value}" }
        return " 不採用${rejected}件(${parts.joinToString(" ")})" +
            (if (culprits.isEmpty()) "" else "(主因 $culprits)")
    }
}

/**
 * [厳密ピン(lo==hi)保護] 職員×シフトの staffRange が下限=上限で完全固定("厳密ピン"＝月に必ずちょうど
 * N回)されている箇所について、候補盤面が基準盤面より目標回数からより遠ざかる職員が1人でもいるか判定する。
 *
 * 動機（桒澤美幸の実例、実機ログ+実データ検証）: 休(rest)が10-10固定の彼女が、C1JointLnsPolish/
 * C1TemporalFlowPolish/PersonalBalanceJointLnsPolish 等の複数職員横断ジョイント研磨により、他職員の
 * c1/covU改善の副作用として10→13へ動かされる（total/weightedScoreは全体として改善するため既存の
 * isBetter/better(hard→total→weighted辞書式)keep-bestだけでは防げない）。通常のlo<hi範囲は既存の
 * 重み(90/45)付きソフト評価のままで良いが、"厳密ピン"は「担当外(canDo)ガード」や「希望固定」と同種の
 * 個人単位の確定事項として扱い、これらのジョイント研磨パスの最終採否にAND条件として追加する。
 *
 * 目的関数・重みは不変（MirrorCore/Evaluator/DeltaEvaluator/magi_native.cppは無変更）。あくまで
 * 該当パスの候補受理を追加で絞るだけ＝退化不能（現状維持したままでも常に選べる）。
 * 既に基準盤面がピンから外れている(データ側の既存不整合)場合は、そこから遠ざける変更のみを禁じ、
 * 現状維持やピンへ近づける変更は妨げない。
 */
/**
 * [3.326.0] 厳密ピンを目標から遠ざけた (職員, シフト) を全部返す。
 *
 * `exactPinRegression` は「1件でもあるか」の高速判定（見つけ次第 return）なので、**どのピンが止めたか**は
 * 分からない。緩和の対象を利用者へ提示するにはそれが必要なので、判定が真だったときだけこちらを呼ぶ
 * （ホットパスは `exactPinRegression` のまま＝早期 return が残る）。
 *
 * @return `[職員, シフト]` の並び。判定と同じ意味論（目標からの距離が増えたものだけ）。
 */
internal fun exactPinOffenders(p: Problem, before: Array<IntArray>, after: Array<IntArray>): List<IntArray> {
    val out = ArrayList<IntArray>(2)
    for (i in 0 until p.S) {
        for (k in 0 until p.K) {
            val lo = p.rangeLo[i][k]
            val hi = p.rangeHi[i][k]
            if (lo == Int.MIN_VALUE || hi == Int.MAX_VALUE || lo != hi) continue
            var beforeCnt = 0
            var afterCnt = 0
            for (j in 0 until p.T) {
                if (before[i][j] == k) beforeCnt++
                if (after[i][j] == k) afterCnt++
            }
            if (kotlin.math.abs(afterCnt - lo) > kotlin.math.abs(beforeCnt - lo)) out.add(intArrayOf(i, k))
        }
    }
    return out
}

/**
 * [3.326.0] 「回数固定(lo==hi)だけが却下の理由だった候補試行」を**対象(職員,シフト)別に**数える。
 *
 * ここに入るのは `isBetter` が採用を認めた手だけ＝ピンのガードを外せば通ったはずの手。
 * よって「どのピンを緩めれば何回ぶん通り得たか」の実測値になる（推測ではない）。
 *
 * UI（緩和対象の一覧）が読むので public。判定本体の `exactPinRegression`/`exactPinOffenders` は internal のまま。
 *
 * **数の性質**（画面へ出すときは必ず添える）: 手の数ではなく**試行の回数**。研磨は最大4巡するので
 * 同じ手が複数の巡で数えられうる（重複排除していない）。
 */
class PinBlockAttribution {
    private val counts = HashMap<Long, Int>()

    /** 計測できた試行の総数。 */
    var attempts = 0
        private set

    /**
     * 判定と記録を同時に行う。**`isBetter` が真であることを確認した直後にだけ呼ぶ**
     * （`isBetter(...) && !pinBlocks.blocksImproving(...)` の形なら短絡により保証される）。
     * 記録対象を「目的関数が採用を認めた手」に限定するのが目的で、そうでない候補を混ぜると
     * 「緩めれば通ったはず」の主張が崩れる。
     */
    fun blocksImproving(p: Problem, before: Array<IntArray>, after: Array<IntArray>): Boolean {
        val bad = exactPinRegression(p, before, after)
        if (bad) record(p, before, after)
        return bad
    }

    /** ピン単独却下を1件記録する。`exactPinRegression` が真だったときだけ呼ぶ。 */
    fun record(p: Problem, before: Array<IntArray>, after: Array<IntArray>) {
        attempts++
        for (o in exactPinOffenders(p, before, after)) {
            val key = (o[0].toLong() shl 32) or o[1].toLong()
            counts.merge(key, 1, Int::plus)
        }
    }

    fun merge(other: PinBlockAttribution) {
        attempts += other.attempts
        for ((k, v) in other.counts) counts.merge(k, v, Int::plus)
    }

    /** (職員, シフト) → 却下試行数。件数の多い順。 */
    fun byTarget(): List<Triple<Int, Int, Int>> =
        counts.entries.sortedByDescending { it.value }
            .map { Triple((it.key shr 32).toInt(), (it.key and 0xFFFFFFFFL).toInt(), it.value) }

    val isEmpty: Boolean get() = attempts == 0
}

internal fun exactPinRegression(p: Problem, before: Array<IntArray>, after: Array<IntArray>): Boolean {
    for (i in 0 until p.S) {
        for (k in 0 until p.K) {
            val lo = p.rangeLo[i][k]
            val hi = p.rangeHi[i][k]
            if (lo == Int.MIN_VALUE || hi == Int.MAX_VALUE || lo != hi) continue
            var beforeCnt = 0
            var afterCnt = 0
            for (j in 0 until p.T) {
                if (before[i][j] == k) beforeCnt++
                if (after[i][j] == k) afterCnt++
            }
            if (kotlin.math.abs(afterCnt - lo) > kotlin.math.abs(beforeCnt - lo)) return true
        }
    }
    return false
}
