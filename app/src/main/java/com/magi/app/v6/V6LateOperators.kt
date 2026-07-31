package com.magi.app.v6

import com.magi.app.model.MagiState
import java.time.LocalDate
import java.util.Random

/**
 * [HF528/537/540/541 PORTED FROM WEB 2026-06-11] 後期演算子(EarlyChainフック用)。
 *
 * Web `runRectSwap2`(magi_v6_web.html L10637-) / `runC1BlockN`(L10753-) の忠実移植。
 *  - RectSwap2 [HF528]: 2人×多日(2..5日)の矩形交換。同日内の入替なので被覆(covU)保存。
 *      [HF540] ドナー狙い撃ち(D): i1 が個人別回数(下限)違反者のとき 70% で不足シフト kd の
 *      最多保持者 i2 を貪欲選択し、窓を「i2 が kd を持ち i1 が持たない転送日」を含む位置へ寄せる。
 *  - C1BlockN [HF541 = VBA HF219 逆輸入]: c1 違反窓内の連続 blen=min(不足,5) 日を、各日別ドナー
 *      (既選択優先→kd 最多保持)との同日交換で一括充足し窓を割る(最大6者=i1+5ドナー)。
 *
 * 採否ゲート [HF537 同等]:
 *  base  = (hard減) or (hard同 かつ soft減)
 *  boost = base不成立でも Δc1<0 ∧ Δhard<=0 ∧ Δ(200*high+120*low)<=0 ∧ ΔSOFT<=0 なら採用
 *  ※ native 採点では LimMin/LimMax(low/high)=hard2 のため lim 節は hard 条件に実質内包されるが、
 *    Web ゲート(HF151系 200/120)との同値性を明示するため breakdown の low/high で同式を保持する。
 *  ※ native の SOFT は無重み合計(soft)。Web の weighted ΔSOFT に対する保守的同等条件として ΔSOFT<=0 を用いる。
 *  不採用は全 revert。Web の _lockActive/_separable(ロック機能)は native 未実装のため対象外(全員 active)。
 *
 * 統合点: V6NativeOptimizer の RSI 系(runRsi 各ラウンド後 / RSI++ Refine 後)。Web の
 * 「内部V5の reheat 停滞時 EarlyChain(L11705-)」に対応する native の停滞境界。
 * フラグ: optFlags.rectSwap(既定ON [HF532])を Rect/BlkN で共用(Web L11710-11711 と同じ)。
 */
object V6LateOperators {

    data class LateImproveResult(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val chain3: Int,
        val chain4: Int,
        val rect: Int,
        val blkN: Int,
        val logs: List<MirrorLog>,
    )

    /** state.extras 経由の optFlags.<name> 読取(JSONObject/Map の両形に対応)。未設定は def。 */
    fun optFlagBool(state: MagiState, name: String, def: Boolean): Boolean {
        val of = state.extras["optFlags"] ?: return def
        return when (of) {
            is org.json.JSONObject -> if (of.has(name)) of.optBoolean(name, def) else def
            is Map<*, *> -> (of[name] as? Boolean) ?: def
            else -> def
        }
    }

    /** 入力 schedule は変更しない(コピーに適用)。採用が無ければ入力 report をそのまま返す。 */
    fun improve(
        state: MagiState,
        schedule: Array<IntArray>,
        report: ViolationReport,
        rng: Random,
        deadlineMs: Long,
        rectEnabled: Boolean = true,
        chainTry3: Int = 20,
        chainTry4: Int = 12,
        rectTry: Int = 12,
        blkTry: Int = 8,
    ): LateImproveResult {
        val p = cachedProblem(state)
        val sched = schedule.copy2D()
        val logs = ArrayList<MirrorLog>()
        var cur = report
        fun timeUp() = System.currentTimeMillis() >= deadlineMs
        fun lim(r: ViolationReport): Int =
            200 * (r.breakdown["high"] ?: 0) + 120 * (r.breakdown["low"] ?: 0)
        fun c1(r: ViolationReport): Int = r.breakdown["c1"] ?: 0
        // 採否ゲート [HF537]: 採用なら cur 更新 + ログ。不採用なら false(呼び元で revert)。
        fun gate(tag: String, detail: String): Boolean {
            val nv = UnifiedViolationChecker.check(state, sched)
            // [3.287.0 keep-best統一→3.335.0 委譲] 判定は `betterReport`（hard→weightedScore→total）へ。
            //   3.287.0 は第2キーだけ weightedScore へ寄せて**第3キー total へ落ちる分岐を書き忘れて**おり、
            //   weighted 同値・total 改善の候補（例: c1×1 と c42×15 は weighted 15 で同値・total は14違う）を
            //   捨てていた。boost 側の soft<= ガードは「生カウントも悪化させない」追加条件として従来どおり残す。
            val base = betterReport(nv, cur)
            // [3.336.0/敵対レビュー H3] boost に weightedScore 非増を要求する。`lim` は 200×high+120×low で
            //   **目的関数の重み(high45 < low90)と大小が逆**なので、high−1/low+1 の入れ替えは lim を下げつつ
            //   weighted を +45 悪化させられた。反例: c1 5→4・high 2→1・low 1→2 は
            //   lim 520→440 ✓ / soft件数 8→7 ✓ / c1 減 ✓ を全部満たしつつ weighted 255→285。
            //   これで boost は「hard・weighted・件数のどれも悪化させずに c1 だけ減らす」横移動に限定される。
            val boost = !base &&
                c1(nv) < c1(cur) &&
                nv.hard <= cur.hard &&
                nv.weightedScore <= cur.weightedScore &&
                lim(nv) <= lim(cur) &&
                nv.soft <= cur.soft
            if (base || boost) {
                cur = nv
                logs.add(MirrorLog(tag = tag, message = "$detail${if (boost) "(c1ブースト)" else ""} hard=${nv.hard} soft=${nv.soft}"))
                return true
            }
            return false
        }

        var rect = 0
        var blkN = 0

        var chain3 = 0
        var chain4 = 0

        // ── 共有ヘルパ(ChainSwap3/4) ──
        val sN = p.S
        val tN = p.T
        val kN = p.K
        val alw = Array(sN) { p.allowedShiftsForStaff(it) }
        fun ssnOf(sc: Array<IntArray>): Array<IntArray> {
            val a = Array(sN) { IntArray(kN) }
            for (i in 0 until sN) for (j in 0 until tN) {
                val kk = sc[i][j]; if (kk in 0 until kN) a[i][kk]++
            }
            return a
        }
        fun baseViolators(ssn: Array<IntArray>): ArrayList<Int> {
            val v = ArrayList<Int>()
            for (i in 0 until sN) {
                var bad = false
                for (kk in 0 until kN) if (ssn[i][kk] < p.rangeLo[i][kk] || ssn[i][kk] > p.rangeHi[i][kk]) { bad = true; break }
                if (bad) v.add(i)
            }
            return v
        }
        // [HF354→三連/五連など任意長対応] c3n前後チェック: 循環後 newK が前後日と c3n を作るなら true。
        //   Problem.makesForbiddenRun が任意長ルール(旧: 長さ2の禁止連続のみ)を一般判定する。
        fun c3nHit(i: Int, j: Int, newK: Int): Boolean = p.makesForbiddenRun(sched, i, j, newK)
        // [HF411 Level Zero準拠] 平準化対象シフト: need定義済み かつ 担当可能2名以上(番号非依存=全シフト同等)
        fun isBalanceable(bk: Int): Boolean {
            if (bk !in 0 until kN) return false
            // [3.309.0] 旧実装は生 state の need1 / needDay1 しか見ず、**P2 だけで需要が定義された
            //   シフトを「需要なし」と判定**して Chain 系の候補から丸ごと外していた（3.173.0 で
            //   CoverageDiagnosis に対して直したのと同型の取り残し）。判定は source of truth の
            //   Problem.covUCell に委ねる＝誰も配置しない状態(got=0)で不足が出るならその日は需要がある。
            var hasNeed = false
            for (j in 0 until tN) {
                if (p.covUCell(bk, j, 0) > 0) { hasNeed = true; break }
            }
            if (!hasNeed) return false
            var elig = 0
            for (i in 0 until sN) if (alw[i].contains(bk)) { if (++elig >= 2) return true }
            return false
        }
        // 採否(Chain系)。[3.309.0] 旧実装は weightedScore 純改善のみで **HARD を一切見ていなかった**
        //   （すぐ上の gate() は 3.287.0 で hard 優先へ統一済みなのに、ここだけ 3.287.0/3.289.0 の
        //   全サイト掃討から漏れていた）。実害は到達不能に近い＝同日3〜4者交換は最大4セルしか変えず、
        //   soft から得られる weighted 改善は現実的に数百（low=90/high=45/c1=15）に対し、HARD の最小重みは
        //   c3n=7000 なので HARD を増やす受理は成立しない。それでも契約は揃える（将来 HF77 で HARD 重みが
        //   下がったときに静かに壊れる罠を残さない）。[3.335.0] 判定は `betterReport` へ委譲＝第3キー total
        //   まで見る（3.309.0 は hard→weightedScore を手書きで複製しており total へ落ちなかった）。
        fun gateW(): Boolean {
            val nv = UnifiedViolationChecker.check(state, sched)
            val ok = betterReport(nv, cur)   // [3.335.0] gate と同じく betterReport へ委譲（第3キー total まで見る）
            if (ok) { cur = nv; return true }
            return false
        }

        // ───────── ChainSwap3 [HF354-358] 3者同日循環 ─────────
        run {
            val ssn = ssnOf(sched)
            val violators = baseViolators(ssn)
            // [HF356] c3単発(必須2連続の孤立が3件以上)のスタッフを追加
            val c3Mand = p.cons3.filter { it.seq.size == 2 && it.seq[0] == it.seq[1] }.map { it.seq[0] }
            if (c3Mand.isNotEmpty()) {
                val vset = violators.toHashSet()
                for (i in 0 until sN) {
                    if (vset.contains(i)) continue
                    var iso = 0
                    for (mk in c3Mand) for (j in 0 until tN) {
                        if (sched[i][j] != mk) continue
                        val prevSame = j > 0 && sched[i][j - 1] == mk
                        val nextSame = j < tN - 1 && sched[i][j + 1] == mk
                        if (!prevSame && !nextSame) iso++
                    }
                    if (iso >= 3) violators.add(i)
                }
            }
            // [HF357] 曜日偏在(労働シフトの曜日σ>1.0)のスタッフを追加
            run {
                val dow0 = runCatching { LocalDate.parse(state.startDate).dayOfWeek.value % 7 }.getOrDefault(0)
                val vset = violators.toHashSet()
                for (i in 0 until sN) {
                    if (vset.contains(i)) continue
                    val wd = IntArray(7)
                    for (j in 0 until tN) {
                        val kk = sched[i][j]
                        if (kk in 0 until kN && isBalanceable(kk)) wd[(dow0 + j) % 7]++
                    }
                    val avg = wd.sum() / 7.0
                    var vs = 0.0
                    for (x in wd) vs += (x - avg) * (x - avg)
                    if (Math.sqrt(vs / 7.0) > 1.0) violators.add(i)
                }
            }
            // [HF358] シフト集中(担当可能者間σ>0.8 で平均から1σ超)のスタッフを追加
            run {
                val vset = violators.toHashSet()
                for (kk in 0 until kN) {
                    if (!isBalanceable(kk)) continue
                    val elig = ArrayList<Pair<Int, Int>>()
                    for (i in 0 until sN) if (alw[i].contains(kk)) elig.add(i to ssn[i][kk])
                    if (elig.size < 2) continue
                    val avg = elig.sumOf { it.second } / elig.size.toDouble()
                    var vs = 0.0
                    for ((_, c) in elig) vs += (c - avg) * (c - avg)
                    val std = Math.sqrt(vs / elig.size)
                    if (std <= 0.8) continue
                    for ((i, c) in elig) {
                        if (vset.contains(i)) continue
                        if (Math.abs(c - avg) > std) { violators.add(i); vset.add(i) }
                    }
                }
            }
            var tr = 0
            while (tr < chainTry3) {
                tr++
                if (timeUp()) break
                // [HF355] 3段階選択: 違反2+経由1(前半50%) / 違反1(〜80%) / 全ランダム(残り)
                val i1: Int; val i2: Int; val i3: Int
                if (violators.size >= 2 && tr <= (chainTry3 * 0.5).toInt()) {
                    val a = rng.nextInt(violators.size)
                    var b = rng.nextInt(violators.size)
                    if (b == a) b = (b + 1) % violators.size
                    i1 = violators[a]; i3 = violators[b]; i2 = rng.nextInt(sN)
                } else if (violators.isNotEmpty() && tr <= (chainTry3 * 0.8).toInt()) {
                    i1 = violators[rng.nextInt(violators.size)]; i2 = rng.nextInt(sN); i3 = rng.nextInt(sN)
                } else {
                    i1 = rng.nextInt(sN); i2 = rng.nextInt(sN); i3 = rng.nextInt(sN)
                }
                val j = rng.nextInt(tN)
                if (i1 == i2 || i2 == i3 || i1 == i3) continue
                val k1 = sched[i1][j]; val k2 = sched[i2][j]; val k3 = sched[i3][j]
                if (k1 < 0 || k2 < 0 || k3 < 0) continue
                if (k1 == k2 || k2 == k3 || k1 == k3) continue
                if (!alw[i1].contains(k2) || !alw[i2].contains(k3) || !alw[i3].contains(k1)) continue
                if (p.wishLocked(i1, j) || p.wishLocked(i2, j) || p.wishLocked(i3, j)) continue
                if (c3nHit(i1, j, k2) || c3nHit(i2, j, k3) || c3nHit(i3, j, k1)) continue
                sched[i1][j] = k2; sched[i2][j] = k3; sched[i3][j] = k1
                if (gateW()) chain3++ else { sched[i1][j] = k1; sched[i2][j] = k2; sched[i3][j] = k3 }
            }
        }

        // ───────── ChainSwap4 [HF360] 4者同日循環(3-wayの補完) ─────────
        run {
            val violators = baseViolators(ssnOf(sched))
            var tr = 0
            while (tr < chainTry4) {
                tr++
                if (timeUp()) break
                val i1: Int; val i2: Int; val i3: Int; val i4: Int
                if (violators.size >= 2 && tr <= (chainTry4 * 0.7).toInt()) {
                    val a = rng.nextInt(violators.size)
                    var b = rng.nextInt(violators.size)
                    if (b == a) b = (b + 1) % violators.size
                    i1 = violators[a]; i3 = violators[b]; i2 = rng.nextInt(sN); i4 = rng.nextInt(sN)
                } else {
                    i1 = rng.nextInt(sN); i2 = rng.nextInt(sN); i3 = rng.nextInt(sN); i4 = rng.nextInt(sN)
                }
                val j = rng.nextInt(tN)
                if (setOf(i1, i2, i3, i4).size < 4) continue
                val k1 = sched[i1][j]; val k2 = sched[i2][j]; val k3 = sched[i3][j]; val k4 = sched[i4][j]
                if (k1 < 0 || k2 < 0 || k3 < 0 || k4 < 0) continue
                if (setOf(k1, k2, k3, k4).size < 4) continue
                if (!alw[i1].contains(k2) || !alw[i2].contains(k3) || !alw[i3].contains(k4) || !alw[i4].contains(k1)) continue
                if (p.wishLocked(i1, j) || p.wishLocked(i2, j) ||
                    p.wishLocked(i3, j) || p.wishLocked(i4, j)) continue
                if (c3nHit(i1, j, k2) || c3nHit(i2, j, k3) || c3nHit(i3, j, k4) || c3nHit(i4, j, k1)) continue
                sched[i1][j] = k2; sched[i2][j] = k3; sched[i3][j] = k4; sched[i4][j] = k1
                if (gateW()) chain4++ else { sched[i1][j] = k1; sched[i2][j] = k2; sched[i3][j] = k3; sched[i4][j] = k4 }
            }
        }

        // ───────── RectSwap2 [HF528+540] ─────────
        if (rectEnabled) run {
            val s = p.S
            val t = p.T
            val k = p.K
            // 個人別回数の現況(Web ssnR 相当)と違反者(Web 同様、ループ前に一度だけ算出)
            val ssn = Array(s) { IntArray(k) }
            for (i in 0 until s) for (j in 0 until t) {
                val kk = sched[i][j]
                if (kk in 0 until k) ssn[i][kk]++
            }
            val violators = ArrayList<Int>()
            for (i in 0 until s) {
                var bad = false
                for (kk in 0 until k) {
                    if (ssn[i][kk] < p.rangeLo[i][kk] || ssn[i][kk] > p.rangeHi[i][kk]) { bad = true; break }
                }
                if (bad) violators.add(i)
            }
            var tr = 0
            while (tr < rectTry) {
                tr++
                if (timeUp()) break
                val i1 = if (violators.isNotEmpty() && tr <= (rectTry * 0.7).toInt())
                    violators[rng.nextInt(violators.size)] else rng.nextInt(s)
                // [HF540] ドナー狙い撃ち
                var i2 = -1
                var dJd = -1
                if (violators.contains(i1) && rng.nextDouble() < 0.7) {
                    var dKd = -1
                    for (kk in 0 until k) if (ssn[i1][kk] < p.rangeLo[i1][kk]) { dKd = kk; break }
                    if (dKd >= 0) {
                        var bestC = -1
                        val st0 = rng.nextInt(s)
                        for (o in 0 until s) {
                            val c2 = (st0 + o) % s
                            if (c2 == i1) continue
                            if (ssn[c2][dKd] > bestC) { bestC = ssn[c2][dKd]; i2 = c2 }
                        }
                        if (i2 >= 0 && bestC > 0) {
                            val dds = ArrayList<Int>()
                            for (j in 0 until t) if (sched[i2][j] == dKd && sched[i1][j] != dKd) dds.add(j)
                            dJd = if (dds.isNotEmpty()) dds[rng.nextInt(dds.size)] else -1
                        }
                        if (dJd < 0) i2 = -1
                    }
                }
                val dMode = i2 >= 0
                if (!dMode) i2 = rng.nextInt(s)
                if (i1 == i2) continue
                val len = 2 + rng.nextInt(4) // 2..5日
                val j1 = if (dMode) {
                    val off = rng.nextInt(len)
                    (dJd - off).coerceIn(0, maxOf(0, t - len))
                } else rng.nextInt(maxOf(1, t - len + 1))
                val j2 = minOf(t - 1, j1 + len - 1)
                val b1 = p.allowedShiftsForStaff(i1)
                val b2 = p.allowedShiftsForStaff(i2)
                var ok = true
                var anyDiff = false
                val ks1 = IntArray(j2 - j1 + 1)
                val ks2 = IntArray(j2 - j1 + 1)
                var x = 0
                var j = j1
                while (j <= j2) {
                    if (p.wishLocked(i1, j) || p.wishLocked(i2, j)) { ok = false; break } // 希望(pref=HARD)破壊回避
                    val k1 = sched[i1][j]
                    val k2 = sched[i2][j]
                    if (k1 < 0 || k2 < 0) { ok = false; break }
                    if (k1 != k2) anyDiff = true
                    if (!b1.contains(k2) || !b2.contains(k1)) { ok = false; break } // 群互換(双方向)
                    ks1[x] = k1; ks2[x] = k2
                    x++; j++
                }
                if (!ok || !anyDiff) continue
                // 適用(同日内交換=被覆保存)
                x = 0; j = j1
                while (j <= j2) { sched[i1][j] = ks2[x]; sched[i2][j] = ks1[x]; x++; j++ }
                if (gate("RectSwap2", "矩形交換採用${if (dMode) "(D)" else ""} i=$i1<->$i2 j=[$j1..$j2]")) {
                    rect++
                } else { // revert
                    x = 0; j = j1
                    while (j <= j2) { sched[i1][j] = ks1[x]; sched[i2][j] = ks2[x]; x++; j++ }
                }
            }
        }

        // ───────── C1BlockN [HF541 = VBA HF219] ─────────
        if (rectEnabled) run {
            val rules = p.cons1
            if (rules.isNotEmpty()) {
                val s = p.S
                val t = p.T
                var tr = 0
                while (tr < blkTry) {
                    tr++
                    if (timeUp()) break
                    val c = rules[rng.nextInt(rules.size)]
                    val kd = c.shiftIdx
                    val days = c.day1
                    val need = c.day2
                    if (days < 2 || need <= 0 || days > t) continue
                    val i1 = rng.nextInt(s)
                    if (!p.allowedShiftsForStaff(i1).contains(kd)) continue
                    // 違反窓を1つ探す(ランダム起点巡回)
                    val wN = t - days + 1
                    val w0 = rng.nextInt(maxOf(1, wN))
                    var w = -1
                    var have = 0
                    for (o in 0 until wN) {
                        val ws = (w0 + o) % wN
                        var cnt = 0
                        for (j in ws until ws + days) if (sched[i1][j] == kd) cnt++
                        if (cnt < need) { w = ws; have = cnt; break }
                    }
                    if (w < 0) continue
                    val blen = minOf(need - have, 5)
                    if (blen < 1) continue
                    // 窓内の連続 blen 日(i1 が非kd かつ 希望なし)
                    var j1 = -1
                    for (s0 in w..(w + days - blen)) {
                        var okc = true
                        for (d in 0 until blen) {
                            val j = s0 + d
                            if (p.wishLocked(i1, j) || sched[i1][j] == kd) { okc = false; break }
                        }
                        if (okc) { j1 = s0; break }
                    }
                    if (j1 < 0) continue
                    // 各日のドナー貪欲選択(既選択優先 → 新規は kd 総保持数最多)
                    val oldKs = IntArray(blen)
                    val donors = IntArray(blen)
                    val used = ArrayList<Int>()
                    var fail = false
                    for (d in 0 until blen) {
                        val j = j1 + d
                        val k1 = sched[i1][j]
                        if (k1 < 0) { fail = true; break }
                        var pick = -1
                        for (u in used) {
                            if (sched[u][j] == kd && !p.wishLocked(u, j) &&
                                p.allowedShiftsForStaff(u).contains(k1)
                            ) { pick = u; break }
                        }
                        if (pick < 0) {
                            if (used.size >= 5) { fail = true; break }
                            var bestC = -1
                            val st0 = rng.nextInt(s)
                            for (o in 0 until s) {
                                val c2 = (st0 + o) % s
                                if (c2 == i1 || used.contains(c2)) continue
                                if (sched[c2][j] != kd) continue
                                if (p.wishLocked(c2, j)) continue
                                if (!p.allowedShiftsForStaff(c2).contains(k1)) continue
                                var cnt2 = 0
                                for (jj in 0 until t) if (sched[c2][jj] == kd) cnt2++
                                if (cnt2 > bestC) { bestC = cnt2; pick = c2 }
                            }
                            if (pick >= 0) used.add(pick)
                        }
                        if (pick < 0) { fail = true; break }
                        oldKs[d] = k1; donors[d] = pick
                    }
                    if (fail) continue
                    // 一括適用(i1: oldK->kd / donor: kd->oldK = 同日交換で被覆保存)
                    for (d in 0 until blen) {
                        val j = j1 + d
                        sched[i1][j] = kd
                        sched[donors[d]][j] = oldKs[d]
                    }
                    if (gate("C1BlockN", "N者間採用 i=$i1 kd=$kd j=[$j1..${j1 + blen - 1}] 者=${used.size + 1}")) {
                        blkN++
                    } else { // revert
                        for (d in 0 until blen) {
                            val j = j1 + d
                            sched[i1][j] = oldKs[d]
                            sched[donors[d]][j] = kd
                        }
                    }
                }
            }
        }

        return LateImproveResult(sched, cur, chain3, chain4, rect, blkN, logs)
    }
}
