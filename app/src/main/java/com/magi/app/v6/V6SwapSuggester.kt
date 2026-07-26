package com.magi.app.v6

import com.magi.app.model.MagiState

/** 改善手の種類（UIのチップ表示用）。 */
enum class FixKind { CHANGE, CHANGE_MULTI, SWAP, SWAP_XDAY, SWAP_MULTI, CHAIN, WINDOW }

/** 1セルへの代入（move = これらを盤面にセットする）。 */
data class FixCell(val staff: Int, val day: Int, val toShift: Int)

/**
 * [改善提案] 1手で違反がどれだけ減るかを評価した候補。
 * move は ops（セル代入の集合）で表現し、適用は ops を順にセットするだけ（全種類を統一）。
 *  - CHANGE       : 1マスを別シフトへ
 *  - CHANGE_MULTI : 同一スタッフの2マスを同時変更（下限の競合など、1マスでは直せない不足に有効）
 *  - SWAP         : 同日2人を入れ替え（被覆不変）
 *  - SWAP_XDAY    : 別日どうしを入れ替え（被覆が両日で変化）
 *  - SWAP_MULTI   : 同日3人を巡回交換（2人交換が担当可否で塞がる時の打開）
 *  - CHAIN        : 不足シフトを貪欲に最大3コマ補充（エジェクションチェーン／玉突き）
 *  - WINDOW       : 1日×最大4名を総当たりで最適割当（ミニ・マスヒューリスティクス）
 */
data class FixSuggestion(
    val kind: FixKind,
    val ops: List<FixCell>,
    val label: String,
    val deltaHard: Int,
    val deltaTotal: Int,
    val diff: List<Pair<String, Int>>,
)

/**
 * 違反を減らす「1手」を列挙する。最適化エンジンと同じ評価（canDo 可否・希望ロック保護・
 * UnifiedViolationChecker による被覆込み (hard,total,weighted) 辞書式改善）。CHANGE / CHANGE_MULTI /
 * SWAP / SWAP_XDAY / SWAP_MULTI / CHAIN / WINDOW を統合し、効果順・同型重複排除で返す。読取専用。
 * 高コストな手（複数マス・別日・3人）は違反箇所にターゲットし、締切（deadlineMs）で打ち切る賢い探索。
 */
object FixSuggester {
    fun suggest(
        state: MagiState,
        schedule: Array<IntArray>,
        focusStaff: Int? = null,
        focusShift: Int? = null,
        maxResults: Int = 8,
        deadlineMs: Long = 8000L,
    ): List<FixSuggestion> {
        val p = Problem(state)
        if (p.S < 1 || p.T < 1) return emptyList()
        val s = normalizeSchedule(schedule, p)
        val base = UnifiedViolationChecker.check(state, s)

        fun nm(i: Int) = state.staff.getOrNull(i)?.name ?: "#$i"
        fun sym(k: Int) = if (k >= 0) (state.shifts.getOrNull(k)?.kigou ?: "$k") else "—"
        fun dlab(j: Int): String = try {
            val d = java.time.LocalDate.parse(state.startDate).plusDays(j.toLong())
            "${d.monthValue}/${d.dayOfMonth}"
        } catch (e: Exception) { "${j + 1}日" }
        fun diffOf(rep: ViolationReport): List<Pair<String, Int>> {
            val out = ArrayList<Pair<String, Int>>()
            for (k in (base.breakdown.keys + rep.breakdown.keys)) {
                val d = (rep.breakdown[k] ?: 0) - (base.breakdown[k] ?: 0)
                if (d != 0) out.add(k to d)
            }
            out.sortBy { it.second }
            return out
        }

        val found = ArrayList<Quad>()
        val start = System.currentTimeMillis()
        var evals = 0
        fun timeUp() = System.currentTimeMillis() - start > deadlineMs

        // ops をその場で適用→評価→復元（割当を作らず高速）。改善なら候補に追加。
        fun tryOps(kind: FixKind, ops: List<FixCell>, label: String) {
            val saved = IntArray(ops.size) { s[ops[it].staff][ops[it].day] }
            for (op in ops) s[op.staff][op.day] = op.toShift
            val rep = UnifiedViolationChecker.check(state, s)
            evals++
            for (idx in ops.indices) s[ops[idx].staff][ops[idx].day] = saved[idx]
            val better = rep.hard < base.hard ||
                (rep.hard == base.hard && rep.weightedScore < base.weightedScore) ||
                (rep.hard == base.hard && rep.weightedScore == base.weightedScore && rep.total < base.total)
            if (better) {
                found.add(Quad(FixSuggestion(kind, ops, label, rep.hard - base.hard, rep.total - base.total, diffOf(rep)),
                    rep.hard - base.hard, rep.total - base.total, rep.weightedScore - base.weightedScore))
            }
        }

        val focus = focusStaff
        fun inFocus(i: Int) = focus == null || i == focus
        fun pairFocus(i: Int, i2: Int) = focus == null || i == focus || i2 == focus

        // 違反に関与する staff / day / shift をターゲット集合として抽出。
        val countHot = HashSet<Int>()                  // 回数違反(low/high)のあるstaff
        val shortShift = HashMap<Int, MutableSet<Int>>()  // staff -> 下限割れのシフト集合
        for ((key, cls) in base.countViolations) {
            val pp = key.split(","); val i = pp.getOrNull(0)?.toIntOrNull() ?: continue; val k = pp.getOrNull(1)?.toIntOrNull() ?: continue
            countHot.add(i)
            if (cls == "vio-low") shortShift.getOrPut(i) { HashSet() }.add(k)
        }
        val hotCells = ArrayList<Int>()                // pack i*1000+j（セル違反）
        val hotDays = HashSet<Int>()
        for (key in base.violations.keys) {
            val pp = key.split(","); val i = pp.getOrNull(0)?.toIntOrNull() ?: continue; val j = pp.getOrNull(1)?.toIntOrNull() ?: continue
            hotCells.add(i * 1000 + j); hotDays.add(j)
        }
        for (key in base.needViolations.keys) key.split(",").getOrNull(1)?.toIntOrNull()?.let { hotDays.add(it) }

        // ---- Phase 1: 単一マス変更（広く）----
        run {
            for (i in 0 until p.S) {
                if (!inFocus(i)) continue
                val allowed = p.allowedShiftsForStaff(i)
                for (j in 0 until p.T) {
                    if (p.wish[i][j] >= 0) continue
                    val a = s[i][j]
                    for (k in allowed) {
                        if (k == a || timeUp()) continue
                        tryOps(FixKind.CHANGE, listOf(FixCell(i, j, k)), "${nm(i)} ${dlab(j)} 「${sym(a)}」→「${sym(k)}」")
                    }
                }
            }
        }
        // ---- Phase 2: 同日2人交換 ----
        run {
            for (i in 0 until p.S) for (i2 in i + 1 until p.S) {
                if (!pairFocus(i, i2)) continue
                for (j in 0 until p.T) {
                    if (timeUp()) break
                    if (p.wish[i][j] >= 0 || p.wish[i2][j] >= 0) continue
                    val a = s[i][j]; val b = s[i2][j]
                    if (a == b || !p.canDo(i, b) || !p.canDo(i2, a)) continue
                    tryOps(FixKind.SWAP, listOf(FixCell(i, j, b), FixCell(i2, j, a)),
                        "${nm(i)} 「${sym(a)}」 ↔ ${nm(i2)} 「${sym(b)}」（${dlab(j)}）")
                }
            }
        }
        // ---- Phase 3: 複数マス変更（同一スタッフ・下限割れ当事者にターゲット）----
        run {
            val targetStaff = if (focus != null) listOf(focus) else countHot.toList()
            for (i in targetStaff) {
                if (timeUp()) break
                val allowed = p.allowedShiftsForStaff(i)
                // 目標シフト = そのstaffの下限割れシフト ∪ 休(0)。なければ単一マス候補を流用するため全許可。
                val targets: List<Int> = (shortShift[i]?.toList() ?: emptyList()).let { if (it.isEmpty()) allowed.toList() else it + 0 }.distinct()
                val cells = (0 until p.T).filter { p.wish[i][it] < 0 }
                for (a in cells.indices) {
                    if (timeUp()) break
                    for (b in a + 1 until cells.size) {
                        if (timeUp()) break
                        val j1 = cells[a]; val j2 = cells[b]; val s1 = s[i][j1]; val s2 = s[i][j2]
                        for (k1 in targets) {
                            if (k1 == s1) continue
                            for (k2 in targets) {
                                if (k2 == s2 || timeUp()) continue
                                tryOps(FixKind.CHANGE_MULTI, listOf(FixCell(i, j1, k1), FixCell(i, j2, k2)),
                                    "${nm(i)} ${dlab(j1)}「${sym(s1)}」→「${sym(k1)}」＋${dlab(j2)}「${sym(s2)}」→「${sym(k2)}」")
                            }
                        }
                    }
                }
            }
        }
        // ---- Phase 6: エジェクションチェーン（不足シフトを貪欲に最大3コマ充足。文書§2 玉突き）----
        run {
            val chainStaff = if (focus != null) listOf(focus) else countHot.toList()
            for (i in chainStaff) {
                if (timeUp()) break
                val shorts = shortShift[i] ?: continue
                for (x in shorts) {
                    if (timeUp()) break
                    val picked = ArrayList<FixCell>()
                    val applied = ArrayList<Pair<Int, Int>>()   // (day, savedShift) 復元用
                    var rounds = 0
                    while (rounds < 3 && !timeUp()) {
                        // 現在の積み上げ盤面のスコアを基準に、x へ変えて更に改善する可動コマを1つ選ぶ（単調改善を保証）
                        val curRep = UnifiedViolationChecker.check(state, s); evals++
                        var bestJ = -1; var bestSaved = -1
                        var bestHard = curRep.hard; var bestTotal = curRep.total; var bestW = curRep.weightedScore
                        var improved = false
                        for (j in 0 until p.T) {
                            if (p.wish[i][j] >= 0) continue
                            val a = s[i][j]
                            if (a == x) continue
                            s[i][j] = x
                            val rep = UnifiedViolationChecker.check(state, s); evals++
                            s[i][j] = a
                            val take = rep.hard < bestHard ||
                                (rep.hard == bestHard && rep.weightedScore < bestW) ||
                                (rep.hard == bestHard && rep.weightedScore == bestW && rep.total < bestTotal)
                            if (take) { improved = true; bestHard = rep.hard; bestTotal = rep.total; bestW = rep.weightedScore; bestJ = j; bestSaved = a }
                        }
                        if (!improved || bestJ < 0) break
                        s[i][bestJ] = x; picked.add(FixCell(i, bestJ, x)); applied.add(bestJ to bestSaved); rounds++
                    }
                    for ((j, sv) in applied) s[i][j] = sv   // 復元
                    // 2コマ以上のときだけ採用（1コマは単一変更で既出）。base 改善を再確認。
                    if (picked.size >= 2) {
                        val saved = IntArray(picked.size) { s[picked[it].staff][picked[it].day] }
                        for (op in picked) s[op.staff][op.day] = op.toShift
                        val rep = UnifiedViolationChecker.check(state, s); evals++
                        for (idx in picked.indices) s[picked[idx].staff][picked[idx].day] = saved[idx]
                        val better = rep.hard < base.hard ||
                            (rep.hard == base.hard && rep.weightedScore < base.weightedScore) ||
                            (rep.hard == base.hard && rep.weightedScore == base.weightedScore && rep.total < base.total)
                        if (better) found.add(Quad(FixSuggestion(FixKind.CHAIN, picked.toList(),
                            "（連鎖）${nm(i)} の「${sym(x)}」不足を${picked.size}コマ補充", rep.hard - base.hard, rep.total - base.total, diffOf(rep)),
                            rep.hard - base.hard, rep.total - base.total, rep.weightedScore - base.weightedScore))
                    }
                }
            }
        }
        // ---- Phase 7: ミニ再最適化（1日×最大4名を総当たりで最適割当。文書§4 マスヒューリスティクスのミニ版）----
        run {
            val wDays = if (focus != null) (0 until p.T).toList() else hotDays.toList()
            var windows = 0
            for (j in wDays) {
                if (timeUp() || windows >= 5) break
                val movable = (0 until p.S).filter { p.wish[it][j] < 0 }
                if (movable.size < 2) continue
                // 違反関与(countHot)を優先、focus があれば先頭に。最大4名。
                val ranked = movable.sortedByDescending { it in countHot }
                val chosen0 = if (focus != null) (ranked.filter { it == focus } + ranked.filter { it != focus }) else ranked
                var n = minOf(4, chosen0.size)
                val cells0 = chosen0.take(4)
                val opts0 = cells0.map { p.allowedShiftsForStaff(it).toList() }
                fun combos(m: Int): Long { var c = 1L; for (t in 0 until m) c *= opts0[t].size; return c }
                while (n > 2 && combos(n) > 20000L) n--
                if (n < 2 || combos(n) > 20000L) continue
                val cells = cells0.take(n)
                val cellOpts = opts0.take(n)
                val cur = IntArray(n) { s[cells[it]][j] }
                windows++
                val sizes = IntArray(n) { cellOpts[it].size }
                val idx = IntArray(n)
                var haveBest = false; var bHard = 0; var bTotal = 0; var bW = 0.0
                var bestCombo: IntArray? = null
                while (true) {
                    for (c in 0 until n) s[cells[c]][j] = cellOpts[c][idx[c]]
                    val rep = UnifiedViolationChecker.check(state, s); evals++
                    val better = rep.hard < base.hard ||
                        (rep.hard == base.hard && rep.weightedScore < base.weightedScore) ||
                        (rep.hard == base.hard && rep.weightedScore == base.weightedScore && rep.total < base.total)
                    if (better) {
                        val take = !haveBest || rep.hard < bHard ||
                            (rep.hard == bHard && rep.weightedScore < bW) ||
                            (rep.hard == bHard && rep.weightedScore == bW && rep.total < bTotal)
                        if (take) { haveBest = true; bHard = rep.hard; bTotal = rep.total; bW = rep.weightedScore; bestCombo = IntArray(n) { cellOpts[it][idx[it]] } }
                    }
                    var c = 0
                    while (c < n) { idx[c]++; if (idx[c] < sizes[c]) break; idx[c] = 0; c++ }
                    if (c == n || timeUp()) break
                }
                for (c in 0 until n) s[cells[c]][j] = cur[c]   // 復元
                val bc = bestCombo
                if (haveBest && bc != null) {
                    val ops = ArrayList<FixCell>()
                    for (c in 0 until n) if (bc[c] != cur[c]) ops.add(FixCell(cells[c], j, bc[c]))
                    if (ops.size >= 2) {
                        for (op in ops) s[op.staff][op.day] = op.toShift
                        val rep = UnifiedViolationChecker.check(state, s); evals++
                        for (op in ops) s[op.staff][op.day] = cur[cells.indexOf(op.staff)]
                        found.add(Quad(FixSuggestion(FixKind.WINDOW, ops.toList(),
                            "（再最適化）${dlab(j)} の${ops.size}名を最適割当", rep.hard - base.hard, rep.total - base.total, diffOf(rep)),
                            rep.hard - base.hard, rep.total - base.total, rep.weightedScore - base.weightedScore))
                    }
                }
            }
        }
        // ---- Phase 4: 同日3人巡回交換（被覆不変・違反日にターゲット）----
        run {
            val days3 = if (focus != null) (0 until p.T).toList() else hotDays.toList()
            for (j in days3) {
                if (timeUp()) break
                for (a in 0 until p.S) {
                    if (timeUp()) break
                    if (p.wish[a][j] >= 0) continue
                    for (b in 0 until p.S) {
                        if (b == a || p.wish[b][j] >= 0) continue
                        for (c in 0 until p.S) {
                            if (c == a || c == b || p.wish[c][j] >= 0 || timeUp()) continue
                            if (focus != null && a != focus && b != focus && c != focus) continue
                            // 重複列挙を避けるため a を最小に固定
                            if (a > b || a > c) continue
                            val sa = s[a][j]; val sb = s[b][j]; val sc = s[c][j]
                            if (sa == sb && sb == sc) continue
                            // 巡回: a<-sb, b<-sc, c<-sa
                            if (!p.canDo(a, sb) || !p.canDo(b, sc) || !p.canDo(c, sa)) continue
                            tryOps(FixKind.SWAP_MULTI, listOf(FixCell(a, j, sb), FixCell(b, j, sc), FixCell(c, j, sa)),
                                "（3人）${nm(a)}・${nm(b)}・${nm(c)} を ${dlab(j)} で入替")
                        }
                    }
                }
            }
        }
        // ---- Phase 5: 別日交換（違反関与セルを起点）----
        run {
            val anchors = ArrayList<Int>()
            anchors.addAll(hotCells)
            val anchorStaff = if (focus != null) listOf(focus) else countHot.toList()
            for (i in anchorStaff) for (j in 0 until p.T) if (p.wish[i][j] < 0) anchors.add(i * 1000 + j)
            val seenAnchor = HashSet<Int>()
            for (packed in anchors) {
                if (!seenAnchor.add(packed) || timeUp()) continue
                val i1 = packed / 1000; val j1 = packed % 1000
                if (i1 !in 0 until p.S || j1 !in 0 until p.T || p.wish[i1][j1] >= 0) continue
                val a = s[i1][j1]
                for (i2 in 0 until p.S) {
                    if (timeUp()) break
                    if (focus != null && i1 != focus && i2 != focus) continue
                    for (j2 in 0 until p.T) {
                        if (j2 == j1 && i2 == i1) continue
                        if (p.wish[i2][j2] >= 0 || timeUp()) continue
                        val b = s[i2][j2]
                        if (a == b || !p.canDo(i1, b) || !p.canDo(i2, a)) continue
                        val label = if (i1 == i2)
                            "${nm(i1)} ${dlab(j1)}「${sym(a)}」 ↔ ${dlab(j2)}「${sym(b)}」（別日）"
                        else
                            "${nm(i1)} ${dlab(j1)}「${sym(a)}」 ↔ ${nm(i2)} ${dlab(j2)}「${sym(b)}」（別日）"
                        tryOps(FixKind.SWAP_XDAY, listOf(FixCell(i1, j1, b), FixCell(i2, j2, a)), label)
                    }
                }
            }
        }

        // 効果順（必須減 > 合計減 > 重み減）。同型の手は1つに絞り多様性確保。
        found.sortWith(compareBy({ it.dHard }, { it.dWeighted }, { it.dTotal }))  // [3.287.0 keep-best統一] hard→weighted→total
        // [セル限定] focusShift 指定時、押したセル(focus職員×focusシフト)に効く手だけに絞る。
        //   そのセルからシフトを移す(原状=focusShift)か、そのシフトへ移す(toShift=focusShift)手を採用。
        val fShift = focusShift
        fun touchesFocusCell(sug: FixSuggestion): Boolean {
            if (fShift == null) return true
            return sug.ops.any { c ->
                if (focusStaff != null && c.staff != focusStaff) return@any false
                if (c.toShift == fShift) return@any true
                val row = s.getOrNull(c.staff) ?: return@any false
                c.day in row.indices && row[c.day] == fShift
            }
        }
        // [重複排除の頑健化] 旧署名(kind名+ops列挙順)は3種の見落としで実質同一の盤面変化を複数回
        // 表示していた: ①SWAP_XDAY は起点(i1,j1)/(i2,j2)どちらから見るかで ops が逆順生成され別署名化
        // ②同日の SWAP と kind違いなだけの SWAP_XDAY（Phase5 は j2==j1 も除外していない＝「別日」ラベルが
        // 実は同日）③SWAP_MULTI の退化3巡回(1脚が無変化=実質2人交換)や CHAIN(同一shiftへの2コマ)が
        // CHANGE_MULTI 等と同じ盤面変化になり得る、のいずれも kind をまたいで重複表示されていた。
        // ここに至る時点で s は Phase1-7 の全 tryOps が適用→復元を徹底しているため元の(normalize後)
        // 盤面に一致する＝「toShift==sの現在値」は実質no-opの脚と判定できる。no-op脚を除外し、
        // 残りを (staff,day) で正規化した順序で署名化することで、kind やops列挙順に依らず
        // 「最終的にどのセルがどの値になるか」という盤面変化の実体だけで重複を判定する。
        val seen = HashSet<String>()
        val result = ArrayList<FixSuggestion>()
        for (q in found) {
            val sug = q.sug
            if (!touchesFocusCell(sug)) continue
            val realOps = sug.ops.filter { it.toShift != s[it.staff][it.day] }
            if (realOps.isEmpty()) continue   // 全脚が無変化＝実質no-op（表示する意味がない）
            val sig = realOps.sortedWith(compareBy({ it.staff }, { it.day })).joinToString("|") { "${it.staff}.${it.toShift}" }
            if (seen.add(sig)) result.add(sug)
            if (result.size >= maxResults) break
        }
        return result
    }

    private class Quad(val sug: FixSuggestion, val dHard: Int, val dTotal: Int, val dWeighted: Double)
}
