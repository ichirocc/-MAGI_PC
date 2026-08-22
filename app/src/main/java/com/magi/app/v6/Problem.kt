package com.magi.app.v6

import com.magi.app.model.MagiState

/** Resolved constraint rows in index form (kigou -> indices). */
class C1(@JvmField val day1: Int, @JvmField val shiftIdx: Int, @JvmField val day2: Int)
class C2(@JvmField val shiftIdx: Int, @JvmField val count: Int)
class C3(@JvmField val seq: IntArray)
class C41(@JvmField val groupIdx: Int, @JvmField val shiftIdx: Int, @JvmField val l: Int, @JvmField val u: Int)
class C42(@JvmField val g1: Int, @JvmField val s1: Int, @JvmField val g2: Int, @JvmField val s2: Int)

/**
 * Immutable, index-resolved view of a [MagiState] ready for fast evaluation.
 * Faithfully mirrors the Web worker's prelude + resolveConstraints().
 */
class Problem(val state: MagiState) {
    val S = state.staffCount
    val T = state.dayCount
    val K = state.shiftCount
    val G = state.groupCount
    val use2 = state.use2Patterns

    /** [3.410.0/P-06] `groupIdx` が群の範囲外だった職員 index（表示は `V6SanityPort` の検査2k）。
     *  旧: `sgrp = IntArray(S) { state.staff[it].groupIdx }` は無検証で、外部 JSON 由来の範囲外値が
     *  そのまま `bucket[sgrp[i]]` / `grpCnt[sgrp[i]*K+k]` の添字になり **Kotlin 側で AIOOBE** になった。
     *  3.327.0 は同じクラスの `skillIdx` に検査2i を入れたが `groupIdx` は取り残しだった。
     *  C++ 側は 3.171.0 が `nativeCreateProblem` で拒否するが、拒否＝ハンドル0＝**Kotlin へ退化**なので
     *  救いにならない（結局 Kotlin で落ちる）。先頭群へ寄せる（`Ws1Ops.removeGroup` が所属者を先頭群へ
     *  移すのと同じ規約）＋**必ず知らせる**（黙って寄せると別の群のルールが静かに掛かる）。 */
    private val _outOfRangeGroupStaff = mutableListOf<Int>()
    val outOfRangeGroupStaff: List<Int> get() = _outOfRangeGroupStaff

    val sgrp = IntArray(S) { i ->
        val g = state.staff[i].groupIdx
        if (g in 0 until G) g else { _outOfRangeGroupStaff.add(i); 0 }
    }

    /** 休シフトの index（記号"休"解決、無ければ0）。曜日平準化(weekly)で「勤務日か休か」を判定。 */
    val restIdx: Int = restShiftIndex(state)

    /** startDate の曜日オフセット（%7）。weekday(j)=(dow0+j)%7。曜日平準化(weekly)の曜日バケットに使う。
     *  絶対曜日ラベルは重要でなく、day j と day j+7 が同一バケットに落ちることのみが必要。
     *  V6HotfixPasses.dayOfWeekVariance / V6PortAnalyzer.startDow と同式。 */
    val dow0: Int = runCatching { java.time.LocalDate.parse(state.startDate).dayOfWeek.value % 7 }.getOrDefault(0)

    /** Allowed shift indices per group (groupShift[g][k]==1). */
    val bucket: Array<IntArray> = Array(G) { g ->
        val row = state.groupShift.getOrNull(g) ?: emptyList()
        (0 until K).filter { k -> row.getOrNull(k) == 1 }.toIntArray()
    }

    /** groupMembers[g] = 群gに属する staff index。グループ内公平化(fair)で群メンバー間の回数偏差を均すのに使う。 */
    val groupMembers: Array<IntArray> = Array(G) { g -> (0 until S).filter { sgrp[it] == g }.toIntArray() }

    /** Staff indices that may take a given shift (used by block-fill moves). */
    val staffForShift: Array<IntArray> = Array(K) { k ->
        // [3.410.0/P-06] 旧: `state.staff[i].groupIdx` の**直読み**で、`sgrp` のクランプを迂回していた
        //   （範囲外 groupIdx でここだけ AIOOBE）。群の解決は必ず `sgrp` を通す＝単一ソース。
        (0 until S).filter { i -> bucket[sgrp[i]].contains(k) }.toIntArray()
    }

    /** wish[i][j] = desired shift index, or -1. */
    val wish: Array<IntArray> = Array(S) { IntArray(T) { -1 } }

    /** need1[k][j] / need2[k][j] = required count, or -1 (= no requirement). */
    val need1: Array<IntArray>
    val need2: Array<IntArray>

    /** rangeLo/Hi[i][k] = LimMin/LimMax, or Int.MIN/MAX_VALUE when unset. */
    val rangeLo: Array<IntArray>
    val rangeHi: Array<IntArray>

    /** apt[i][k] = 適切回数（群単位の双方向目標 groupShiftApt[群][シフト]）, or -1 when unset.
     *  担当可能(canDo=bucket)なシフトのみ展開し、解消不能な幻のapt偏差を作らない（c1 と同じ方針）。 */
    val apt: Array<IntArray>

    val cons1: List<C1>
    val cons2: List<C2>
    val cons3: List<C3>
    val cons3n: List<C3>
    val cons3m: List<C3>
    val cons3mn: List<C3>
    // [監査#9] 期間より長い連続パターンはパース段階で除外し、(族, パターン表示) をここに記録する。
    //   供給源を一元化することで、評価器/Δ/チェッカー/修復手のどれもが L>T 行を見ない状態を保証する
    //   （従来: チェッカーのみ d>T skip、評価器の run-mode は罰し続ける乖離があった）。
    private val _c3OverT = mutableListOf<Pair<String, String>>()
    val c3OverT: List<Pair<String, String>> get() = _c3OverT

    // [3.309.0] 存在しないシフト記号を含む連続パターン行。旧実装は resolveC3 で
    //   `if (si < 0) return@mapNotNull null` と**無言で捨てて**おり、シフトを改名・削除すると
    //   その行を参照する連続パターン（禁止=HARD を含む）が評価対象から消えるのに、画面にも
    //   ログにも何も出なかった。すぐ下の L>期間 のケースは _c3OverT に記録して Sanity が案内する
    //   のに、この分岐だけ同じ扱いになっていなかった非対称。fam と、行の記号列（未定義記号を
    //   〈〉で囲んだもの）を記録する。読み取り専用＝評価・重みは一切変えない。
    private val _c3UnknownShift = mutableListOf<Pair<String, String>>()
    val c3UnknownShift: List<Pair<String, String>> get() = _c3UnknownShift

    // [3.412.0/P-04] 期間より長い窓の要件(cons1)。`MirrorCore.checkC1Family` は `c.day1 > p.T` を
    //   `continue` で**無言で飛ばす**ため、31日の月に「休を35日で4回以上」と入れると評価もされず
    //   画面にも何も出ない。連続パターンは同じ状況を `_c3OverT` に記録して検査2d が案内するのに、
    //   窓の要件だけ取り残されていた（3.320.0 が6族へ広げた `_unresolvedRows` は「行が解決できない」
    //   ケースで、こちらは「行は解決できるが窓が期間を超える」＝別の穴）。読み取り専用＝評価不変。
    private val _c1OverT = mutableListOf<String>()
    val c1OverT: List<String> get() = _c1OverT

    // [3.320.0] 3.309.0 は連続パターン(cons3系)だけを直したが、**同じ無言除外が残り6族にあった**:
    //   cons1(窓ルール)・cons2(個人合計)・cons41/cons42(群)・cons41s/cons42s(スキル群)は、記号が
    //   解決できない行や数値が不正な行を `mapNotNull { ... else null }` でやはり黙って捨てる。
    //   シフトや群を改名・削除すると、それを参照する窓ルール(重み15)や群ペア禁止が警告なく評価から
    //   消える。(族ラベル, 行の表示) を記録し、Sanity が cons3 系と同じ形で案内する。読み取り専用。
    private val _unresolvedRows = mutableListOf<Pair<String, String>>()
    val unresolvedRows: List<Pair<String, String>> get() = _unresolvedRows

    val cons41: List<C41>
    val cons42: List<C42>
    // [スキルグループ新設] スキル群の C41/C42 相当（ssk = staff のスキル群index。既存 sgrp とは独立）。
    val skillG = state.skillGroupCount
    val ssk = IntArray(S) { state.staff[it].skillIdx }
    val cons41s: List<C41>
    val cons42s: List<C42>

    /** 記号が解決できたかを `〈〉` で示す表示（cons3 系の記録と同じ書式）。 */
    private fun mark(kigou: String, resolved: Boolean): String = if (resolved) kigou else "〈$kigou〉"

    init {
        for ((key, v) in state.wishes) {
            val p = key.split(',')
            val i = p.getOrNull(0)?.toIntOrNull() ?: continue
            val j = p.getOrNull(1)?.toIntOrNull() ?: continue
            if (i in 0 until S && j in 0 until T) wish[i][j] = v
        }

        need1 = Array(K) { IntArray(T) { -1 } }
        need2 = Array(K) { IntArray(T) { -1 } }
        for (k in 0 until K) for (j in 0 until T) {
            need1[k][j] = needAt(k, j, false)
            need2[k][j] = needAt(k, j, true)
        }

        rangeLo = Array(S) { IntArray(K) { Int.MIN_VALUE } }
        rangeHi = Array(S) { IntArray(K) { Int.MAX_VALUE } }
        for ((key, r) in state.staffRange) {
            val p = key.split(',')
            val i = p.getOrNull(0)?.toIntOrNull() ?: continue
            val k = p.getOrNull(1)?.toIntOrNull() ?: continue
            if (i in 0 until S && k in 0 until K) {
                r.lo.trim().toIntOrNull()?.let { rangeLo[i][k] = it }
                r.hi.trim().toIntOrNull()?.let { rangeHi[i][k] = it }
            }
        }

        // 適切回数（双方向目標）: state.groupShiftApt[群][シフト] を個人別 apt[i][k] へ展開（群単位＝同群全員に同一目標）。
        // 担当ONシフトのみ（bucket=canDo）有効化し、担当不可シフトの幻のapt偏差を除外する。
        apt = Array(S) { IntArray(K) { -1 } }
        for (i in 0 until S) {
            val g = sgrp[i]
            val row = state.groupShiftApt.getOrNull(g) ?: continue
            val canK = bucket.getOrNull(g)
            for (k in 0 until K) {
                var t = row.getOrNull(k)?.trim()?.toIntOrNull() ?: continue
                if (t < 0 || canK?.contains(k) != true) continue
                // [整合] 個人別回数(staffRange=LimMin/LimMax)の[lo,hi]外の群目標は到達不能。範囲端にクランプし、
                // staffRangeで固定/制限された職員に解消不能な幻のapt違反が出るのを防ぐ（例: Dﾃを2-2固定の職員に群目標10）。
                val rlo = rangeLo[i][k]; val rhi = rangeHi[i][k]
                if (rlo != Int.MIN_VALUE && t < rlo) t = rlo
                if (rhi != Int.MAX_VALUE && t > rhi) t = rhi
                apt[i][k] = t
            }
        }

        cons1 = state.cons1.mapNotNull {
            val d1 = it.day1.toIntOrNull() ?: 0
            val si = shiftIdxOf(it.shiftKigou)
            val d2 = it.day2.toIntOrNull() ?: 0
            if (d1 > 0 && si >= 0 && d2 > 0) {
                // [3.412.0/P-04] 行としては解決できるが窓が期間を超える＝チェッカーが無言で飛ばす。
                //   行は残す（評価の挙動は完全に不変）が、記録して Sanity が理由を案内する。
                if (d1 > T) _c1OverT.add("${it.shiftKigou} を${d1}日で${d2}回以上")
                C1(d1, si, d2)
            } else { _unresolvedRows.add("窓の要件" to "${mark(it.shiftKigou, si >= 0)} を${it.day1}日で${it.day2}回以上"); null }
        }
        cons2 = state.cons2.mapNotNull {
            val si = shiftIdxOf(it.shiftKigou)
            val c = it.count.toIntOrNull() ?: 0
            if (si >= 0 && c > 0) C2(si, c)
            else { _unresolvedRows.add("個人の合計" to "${mark(it.shiftKigou, si >= 0)} を${it.count}回以上"); null }
        }
        cons3 = resolveC3(state.cons3, "c3")
        cons3n = resolveC3(state.cons3n, "c3n")
        cons3m = resolveC3(state.cons3m, "c3m")
        cons3mn = resolveC3(state.cons3mn, "c3mn")
        cons41 = state.cons41.mapNotNull {
            val gi = groupIdxOf(it.groupKigou)
            val si = shiftIdxOf(it.shiftKigou)
            val hasLo = it.l.isNotBlank()
            val hasHi = it.u.isNotBlank()
            val lo = if (hasLo) it.l.toIntOrNull() ?: 0 else 0
            val hi = if (hasHi) it.u.toIntOrNull() ?: Int.MAX_VALUE else Int.MAX_VALUE
            if (gi >= 0 && si >= 0 && (hasLo || hasHi)) C41(gi, si, lo, hi)
            else {
                _unresolvedRows.add("群のレンジ" to
                    "${mark(it.groupKigou, gi >= 0)} の ${mark(it.shiftKigou, si >= 0)}（${it.l}〜${it.u}）")
                null
            }
        }
        cons42 = state.cons42.mapNotNull {
            val g1 = groupIdxOf(it.g1Kigou); val g2 = groupIdxOf(it.g2Kigou)
            val s1 = shiftIdxOf(it.s1Kigou); val s2 = shiftIdxOf(it.s2Kigou)
            if (g1 >= 0 && g2 >= 0 && s1 >= 0 && s2 >= 0) C42(g1, s1, g2, s2)
            else {
                _unresolvedRows.add("群ペア禁止" to
                    "${mark(it.g1Kigou, g1 >= 0)}/${mark(it.s1Kigou, s1 >= 0)} × " +
                        "${mark(it.g2Kigou, g2 >= 0)}/${mark(it.s2Kigou, s2 >= 0)}")
                null
            }
        }
        cons41s = state.cons41s.mapNotNull {
            val gi = skillGroupIdxOf(it.groupKigou)
            val si = shiftIdxOf(it.shiftKigou)
            val hasLo = it.l.isNotBlank(); val hasHi = it.u.isNotBlank()
            val lo = if (hasLo) it.l.toIntOrNull() ?: 0 else 0
            val hi = if (hasHi) it.u.toIntOrNull() ?: Int.MAX_VALUE else Int.MAX_VALUE
            if (gi >= 0 && si >= 0 && (hasLo || hasHi)) C41(gi, si, lo, hi)
            else {
                _unresolvedRows.add("スキル群のレンジ" to
                    "${mark(it.groupKigou, gi >= 0)} の ${mark(it.shiftKigou, si >= 0)}（${it.l}〜${it.u}）")
                null
            }
        }
        cons42s = state.cons42s.mapNotNull {
            val g1 = skillGroupIdxOf(it.g1Kigou); val g2 = skillGroupIdxOf(it.g2Kigou)
            val s1 = shiftIdxOf(it.s1Kigou); val s2 = shiftIdxOf(it.s2Kigou)
            if (g1 >= 0 && g2 >= 0 && s1 >= 0 && s2 >= 0) C42(g1, s1, g2, s2)
            else {
                _unresolvedRows.add("スキル群ペア禁止" to
                    "${mark(it.g1Kigou, g1 >= 0)}/${mark(it.s1Kigou, s1 >= 0)} × " +
                        "${mark(it.g2Kigou, g2 >= 0)}/${mark(it.s2Kigou, s2 >= 0)}")
                null
            }
        }
    }

    /** getNeed(k,j,p2): per-day override, falling back to shift default; -1 == none. */
    private fun needAt(k: Int, j: Int, p2: Boolean): Int {
        val map = if (p2) state.needDay2 else state.needDay1
        val v = map["$k,$j"]
        if (!v.isNullOrBlank()) v.trim().toIntOrNull()?.let { return it }
        val def = if (p2) state.shifts[k].need2 else state.shifts[k].need1
        return if (def.isBlank()) -1 else (def.trim().toIntOrNull() ?: -1)
    }

    private fun shiftIdxOf(kigou: String): Int = state.shifts.indexOfFirst { it.kigou == kigou }
    private fun groupIdxOf(kigou: String): Int = state.groups.indexOfFirst { it.kigou == kigou }
    private fun skillGroupIdxOf(kigou: String): Int = state.skillGroups.indexOfFirst { it.kigou == kigou }

    /**
     * resolveC3: truncate the pattern at the first blank symbol; drop the whole row if
     * an interior symbol is unresolvable (mirrors the Web doc7#4 fix — never compact).
     */
    private fun resolveC3(rows: List<com.magi.app.model.C3Row>, fam: String): List<C3> = rows.mapNotNull { row ->
        val raw = row.pattern
        val end = raw.indexOfFirst { it.isBlank() }
        val body = if (end >= 0) raw.subList(0, end) else raw
        if (body.isEmpty()) return@mapNotNull null
        val seq = IntArray(body.size)
        for (idx in body.indices) {
            val si = shiftIdxOf(body[idx])
            if (si < 0) {
                // [3.309.0] 無言で捨てず記録する（Sanity が「この行は効いていない」と案内する）。
                _c3UnknownShift.add(
                    fam to body.joinToString("") { t -> if (shiftIdxOf(t) < 0) "〈$t〉" else t },
                )
                return@mapNotNull null
            }
            seq[idx] = si
        }
        if (seq.size > T) {   // [監査#9] L>期間はどの族でも判定不能/無意味 → 除外して記録（Sanityが案内）
            _c3OverT.add(fam to body.joinToString(""))
            return@mapNotNull null
        }
        C3(seq)
    }

    /**
     * Initial assignment from state.schedule, overwriting a cell with its wish only when
     * the wished shift is actually in the staff's allowed bucket (Web HF143 capability guard).
     */
    // [監査#4b] 被覆セル評価（per-cell OR/AND）— Evaluator/Δ/Checker の単一ソース（VBA本家=Web HF574と三面統一）。
    //   U: 両定義=min(u1,u2)・片方定義=その値（P2単独定義セルも評価）。
    //   O: 両定義=両超過時のみmin(o1,o2)・片方定義=その超過。U>0とO>0は同一セルで両立しない。
    //   実データ(P1=P2)では u1=u2/o1=o2 のため旧・総量min式とビット単位で同値。
    fun covUCell(k: Int, j: Int, got: Int): Int {
        val lo1 = need1[k][j]
        val lo2 = if (use2) need2[k][j] else -1
        val u1 = if (lo1 >= 0) (lo1 - got).coerceAtLeast(0) else -1
        val u2 = if (lo2 >= 0) (lo2 - got).coerceAtLeast(0) else -1
        return when {
            u1 >= 0 && u2 >= 0 -> minOf(u1, u2)
            u1 >= 0 -> u1
            u2 >= 0 -> u2
            else -> 0
        }
    }
    fun covOCell(k: Int, j: Int, got: Int): Int {
        val lo1 = need1[k][j]
        val lo2 = if (use2) need2[k][j] else -1
        val o1 = if (lo1 >= 0) (got - lo1).coerceAtLeast(0) else -1
        val o2 = if (lo2 >= 0) (got - lo2).coerceAtLeast(0) else -1
        return when {
            o1 >= 0 && o2 >= 0 -> minOf(o1, o2)
            o1 >= 0 -> o1
            o2 >= 0 -> o2
            else -> 0
        }
    }

    /**
     * [三連/五連など任意長への配慮] 職員 i の j 列を newK に変えたとき、cons3n（禁止連続, HARD）を
     * 新たに作るかを判定する。旧: 呼び出し側3箇所（V6SearchOperators.findCovUChain の枝刈り／
     * V6LateOperators の ChainSwap3/4 前後チェック／CoverageDiagnosis の内訳分類）がいずれも
     * 「長さ2の禁止連続のみ」を仮定した専用コードを個別に持っていた（コメントで明記された既知の狭さ）。
     * cons3n ルールは MirrorCore.checkC3Family の forbidden 分岐と同じ意味論で**任意長**（三連・五連等）
     * を表現できるため、ここで単一ソースとして一般化する: 各ルール(長さd)について j をカバーする
     * 開始位置 s の窓を全て調べ、位置jだけ newK に差し替えて残りは現在の schedule のまま完全一致
     * (z==d)するかを見る。他セルは変えない=1手の影響範囲チェックとして正しい。
     * これは探索の**枝刈り**（成功率向上のヒント）用途で、最終的な正しさは常に UnifiedViolationChecker
     * （source of truth）が担保する＝本関数の判定が仮に見逃しても結果は不変（安全側）。
     */
    fun makesForbiddenRun(schedule: Array<IntArray>, i: Int, j: Int, newK: Int): Boolean {
        for (c in cons3n) {
            val seq = c.seq
            val d = seq.size
            if (d == 0 || d > T) continue
            val sLo = (j - d + 1).coerceAtLeast(0)
            val sHi = j.coerceAtMost(T - d)
            var s = sLo
            while (s <= sHi) {
                var z = 0
                for (l in 0 until d) {
                    val v = if (s + l == j) newK else schedule[i][s + l]
                    if (v == seq[l]) z++
                }
                if (z == d) return true
                s++
            }
        }
        return false
    }

    fun initialAssignment(): Array<IntArray> = Array(S) { i ->
        val b = bucket[sgrp[i]]
        IntArray(T) { j ->
            // [3.419.0] 旧: 欠損セル（行が短い・行が無い）を `?: 0` でハードコードの index 0 にしていた。
            //   0 は合法値なので下の範囲チェックを素通りし、**勤務シフトへ黙って化ける**。3.410.0 が
            //   範囲外セルについて直したのと同じ取り違えが、その1行上に残っていた。-1 へ倒して
            //   下の穴埋めに合流させる。
            var k = state.schedule.getOrNull(i)?.getOrNull(j) ?: -1
            val w = wish[i][j]
            if (w >= 0 && b.contains(w)) k = w
            // [3.410.0/P-01] 旧: 範囲外セルをハードコードの 0 へ寄せていた。0 が休とは限らない
            //   （休が先頭でないデータでは**勤務シフトへ化ける**）。
            // [3.419.0] さらに、寄せ先の `restIdx` を**この職員が担当できるか見ていなかった**＝
            //   休を担当できない群では、範囲外という入力の不備だけを理由に groupViol(HARD) を作っていた。
            //   共通規則 `fillShiftIndex` へ委譲する（Ws1Ops の3経路と同じ判断）。
            //   なお `MirrorCore.normalizeSchedule` は同じセルを -1（センチネル）にするが、ここは
            //   **探索へ渡す初期盤面**なので合法値が要る（-1 を入れると `DeltaEvaluator.rebuild` の
            //   `cntSS[i][k]++` が飛ぶ）＝非対称は意図的。
            if (k < 0 || k >= K) k = fillShiftIndex(b, restIdx)
            k
        }
    }
}
