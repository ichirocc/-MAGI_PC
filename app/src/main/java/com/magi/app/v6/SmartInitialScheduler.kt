package com.magi.app.v6

import com.magi.app.model.MagiState
import java.util.Random
import kotlin.math.max

/**
 * 初期解生成（賢い版）。既存の`GreedyMirrorScheduler`(希望→個人下限→必要人数→残り埋め)は
 * C1(窓の要件)を一切考慮せず後段の研磨任せだった。本スケジューラは「①希望シフト→②C1窓制約→
 * ③日別必要人数→④個人下限→⑤残り埋め」の順で初期解を組み立て、C1を構築段階から考慮する。
 *
 * ②のC1充足は`C1TemporalDp`（既存の月内最適配置DP）を流用したいが、あちらは「対象シフトの
 * 月間回数を厳密保存しつつ限られた移設数で再配置する」ポリッシュ専用（既存回数0=ゼロからの
 * 構築には使えない）。そのため本ファイルに`solveConstructionDp`を新設: 回数保存・移設数上限を
 * 課さず、希望で確定済みの日(forced)だけ固定し、違反窓数(最優先)→対象日数(次点、他制約への
 * 自由度を残すため最小化)の順で最適な対象/非対象の月内配置をビットマスクDPで直接求める。
 *
 * 生成した解はそのまま`currentSchedule`を置き換える下書き。以降の本最適化(SA/ALNS)や後段研磨は
 * 従来どおり別ボタン「勤務表をつくる」が担う（本関数は初期解生成のみ、続けて最適化は行わない）。
 */
object SmartInitialScheduler {
    private data class C1Rule(val days: Int, val minimum: Int)

    fun generate(state: MagiState, seed: Long = 0x517A2L): ScheduleRunResult {
        val t0 = System.nanoTime()
        val p = Problem(state)
        if (p.T <= 0 || p.S <= 0 || p.K <= 0) throw IllegalArgumentException("期間/職員/シフトが不足しています")
        val restK = restShiftIndex(state)
        // [3.261.0, ユーザー実機報告「初期解生成後にC1違反になる/何度も出来ない」で判明した実バグ修正]
        // 旧実装は既存スケジュールの充足率で「既存表ベース(そのまま保持)/空表ベース(ゼロから構築)」を
        // 切り替えていたが、本関数の呼出元(ボタン)は「初期解を(作り直す)」という常に**ゼロから組み立て
        // 直す**操作である以上、これは有害だった: ①1回目の生成が完了すると盤面は必ず100%充足済みに
        // なり、②次に呼ぶと「既存表ベース」判定でschedule=既存のまま=希望/C1/必要人数/個人下限/残り埋め
        // の全ステップが「空きセルが無い」ため丸ごとno-op（実データで検証: run1=C1充足セル0件・
        // c1=78、run2はrun1と盤面が完全一致=無変化、対照の白紙強制ではC1充足セル57件・c1=31と大幅改善）。
        // 実運用データは最初からschedule欄が埋まっていることも多く、**初回の生成ですらこの穴を踏み得た**。
        // 希望(state.wish)は盤面と独立に保持されるため、常に白紙から組み立てても希望登録は失われない。
        // 呼出元(generateSmartInitial)は実行前に必ずpushUndo()する＝元の盤面へはいつでも復元可能。
        val schedule = Array(p.S) { IntArray(p.T) { -1 } }

        // ① 希望シフト（担当可能な希望のみ直接適用。担当外は据え置き＝後段の診断で案内される）。
        var wishIn = 0
        var wishOut = 0
        for (i in 0 until p.S) for (j in 0 until p.T) {
            if (schedule[i][j] >= 0) continue
            val w = p.wish[i][j]
            if (w !in 0 until p.K) continue
            if (p.canDo(i, w)) { schedule[i][j] = w; wishIn++ } else wishOut++
        }

        // ② C1(窓の要件)。対象シフトごとに規則をまとめ、シフトindex順（決定的）に処理する。
        //   同一職員が複数シフトのC1規則を持つ場合、先に処理したシフトの決定が後続シフトの forced を
        //   自然に制約する（空きセルのみ埋めるため、既存の希望/決定済みセルを上書きしない）。
        val rulesByShift = LinkedHashMap<Int, MutableList<C1Rule>>()
        for (c in p.cons1) {
            if (c.shiftIdx !in 0 until p.K || c.day1 <= 0 || c.day2 <= 0) continue
            rulesByShift.getOrPut(c.shiftIdx) { ArrayList() }.add(C1Rule(c.day1, c.day2))
        }
        var c1Filled = 0
        val rng = Random(seed)
        for (x in rulesByShift.keys.sorted()) {
            val rules = rulesByShift.getValue(x)
            for (i in 0 until p.S) {
                if (!p.canDo(i, x)) continue
                val forced = IntArray(p.T) { j ->
                    when (schedule[i][j]) { -1 -> -1; x -> 1; else -> 0 }
                }
                val cap = p.rangeHi[i][x]
                val targetDays = solveConstructionDp(p.T, rules, forced, rng.nextLong(), cap) ?: continue
                for (j in 0 until p.T) {
                    if (targetDays[j] && schedule[i][j] < 0) { schedule[i][j] = x; c1Filled++ }
                }
            }
        }

        // ③ 日別必要人数(need1/need2=covUCellのOR)。不足の大きいシフトから、超過/回数の少ない職員を
        //   優先して埋める（GreedyMirrorSchedulerの充足フィルと同一ロジック）。
        //   [need2単独定義セル見落とし修正] need1のみを見ると、need1未設定・need2のみで需要が定義された
        //   シフトがdemandOrderへ一切追加されず初期解が積極的に埋めない（3.173.0のCoverageDiagnosis修正と
        //   同根、covUCellをsource of truthとして統一）。
        var counts = countMatrix(p, schedule)
        var cov = coverage(p, schedule)
        for (j in 0 until p.T) {
            val demandOrder = ArrayList<Pair<Int, Int>>()
            for (k in 0 until p.K) {
                val deficit = p.covUCell(k, j, cov[j][k])
                if (deficit > 0) demandOrder.add(deficit to k)
            }
            demandOrder.sortWith { a, b ->
                val d = b.first.compareTo(a.first)
                if (d != 0) d else a.second.compareTo(b.second)
            }
            for (pair in demandOrder) {
                val k = pair.second
                while (p.covUCell(k, j, cov[j][k]) > 0) {
                    var bestI = -1
                    var bestPenalty = Int.MAX_VALUE
                    for (i in 0 until p.S) {
                        if (schedule[i][j] >= 0 || !p.canDo(i, k)) continue
                        val hi = p.rangeHi[i][k]
                        val over = hi != Int.MAX_VALUE && counts[i][k] >= hi
                        val penalty = (if (over) 1000 else 0) + counts[i][k] * 2
                        if (penalty < bestPenalty) { bestPenalty = penalty; bestI = i }
                    }
                    if (bestI < 0) break
                    schedule[bestI][j] = k
                    counts[bestI][k]++
                    cov[j][k]++
                }
            }
        }

        // ④ 個人下限(rangeLo)。
        counts = countMatrix(p, schedule)
        for (i in 0 until p.S) {
            val allowed = p.allowedShiftsForStaff(i)
            val free = ArrayList<Int>()
            for (jj in 0 until p.T) if (schedule[i][jj] < 0) free.add(jj)
            var pos = 0
            for (k in allowed) {
                val lo = p.rangeLo[i][k].takeIf { it != Int.MIN_VALUE } ?: 0
                var need = max(0, lo - counts[i][k])
                while (need > 0 && pos < free.size) {
                    val j = free[pos++]
                    schedule[i][j] = k
                    counts[i][k]++
                    need--
                }
            }
        }

        // ⑤ 残りの空きセルをペナルティ最小で埋める。
        counts = countMatrix(p, schedule)
        for (i in 0 until p.S) {
            val allowed = p.allowedShiftsForStaff(i)
            for (j in 0 until p.T) {
                if (schedule[i][j] >= 0) continue
                var bestK = allowed.firstOrNull() ?: restK
                var bestPenalty = Int.MAX_VALUE
                for (k in allowed) {
                    val hi = p.rangeHi[i][k]
                    val over = hi != Int.MAX_VALUE && counts[i][k] >= hi
                    var covNow = 0
                    for (ii in 0 until p.S) if (schedule[ii][j] == k) covNow++
                    // [need2単独定義セル見落とし修正] 上のstep③と同根。need1のみでなくcovUCell(OR)で判定。
                    val demandBonus = if (p.covUCell(k, j, covNow) > 0) -100 else 0
                    // [3.345.0] 休は通常のシフト種の一つ＝残り埋めで優先しない（旧: 休だけ -10 のボーナス）。
                    //   実データ3件で hard/covO/covU/low/high/c1 が全て同一＝この優先は実質不活性だった。
                    val penalty = (if (over) 1000 else 0) + counts[i][k] + demandBonus
                    if (penalty < bestPenalty) { bestPenalty = penalty; bestK = k }
                }
                schedule[i][j] = bestK
                counts[i][bestK]++
            }
        }

        val report = UnifiedViolationChecker.check(state, schedule)
        val elapsedMs = ((System.nanoTime() - t0) / 1_000_000L)
        val log = MirrorLog(
            tag = "SmartInitial",
            message = "初期解生成: HARD=${report.hard} total=${report.total} " +
                "希望seed=${wishIn}件/担当外=${wishOut}件 C1充足セル=${c1Filled}件 (${elapsedMs}ms)",
        )
        return ScheduleRunResult(schedule, report.copy(logs = listOf(log) + report.logs))
    }

    /**
     * [3.262.0, V6SanityPort検査2b-3(個人内壁検知)向け] 個人上限(rangeHi)を無視した場合に、
     * 指定の窓ルール群(同一シフトの複数規則も可)を**同時に**完全充足するには最低何日必要かを、
     * 構築本体(`solveConstructionDp`)自身で計算する。旧2b-3は非重複窓の粗い下界
     * （`day2*floor(T/day1)`）を使っており、スライド窓の真の必要量を過小評価していた
     * （実データ検証: 15日窓4回以上の粗い下界=8だが、実際にDPが0違反へ到達するには9〜11日
     * 必要な職員が複数存在し、粗い下界では「上限8/9で足りている」と誤って見逃していた）。
     * 無制限cap・全日自由で`solveConstructionDp`を呼び、0違反を達成する解のうち対象日数最小の
     * ものを返す（DPの優先順位=違反数最優先→対象日数次点、と一致するため正確）。
     * 0違反が原理的に不可能（規則の日数が期間を超える等）ならnullを返す。
     */
    fun minDaysForFullCompliance(t: Int, rules: List<Pair<Int, Int>>, seed: Long = 0x517A2L): Int? {
        val c1Rules = rules.map { C1Rule(it.first, it.second) }
        val forced = IntArray(t) { -1 }
        val targetDays = solveConstructionDp(t, c1Rules, forced, seed, t) ?: return null
        for (rule in c1Rules) {
            for (j0 in 0..(t - rule.days)) {
                var cnt = 0
                for (j in j0 until j0 + rule.days) if (targetDays[j]) cnt++
                if (cnt < rule.minimum) return null
            }
        }
        return targetDays.count { it }
    }

    /**
     * 単一シフトxのC1規則群を満たす「対象日か否か」の月内配置を、ゼロからビットマスクDPで
     * 直接求める（`C1TemporalDp`と異なり回数保存・移設数上限は課さない＝構築専用）。
     *
     * @param forced[day] 1=希望等で既にx確定・0=希望等で既に他シフト確定(選べない)・-1=自由。
     * @param maxCount 対象日数の上限（`staffRange`の個人上限=rangeHi。未設定はInt.MAX_VALUE）。
     *   high違反(重み45)はc1(重み15)より重いため、C1充足のためだけに個人上限を超えて割り当てない。
     *   forced済み(希望由来)の対象日もこの上限に含める＝希望だけで既に上限超過なら
     *   (=既存の別問題)これ以上は増やさずnullで安全側に諦める。
     * @return 目的= まず違反窓数を最小化、次に対象日数を最小化（他制約(③④⑤)への自由度を残す）、
     *   最後にseed由来の決定的tie-breakで一意に選ぶ。
     */
    private fun solveConstructionDp(
        t: Int,
        rules: List<C1Rule>,
        forced: IntArray,
        seed: Long,
        maxCount: Int = Int.MAX_VALUE,
    ): BooleanArray? {
        if (t <= 0 || t > 62 || forced.size != t) return null
        val validRules = rules.filter { it.days in 1..t && it.minimum > 0 }
        if (validRules.isEmpty()) return null
        val maxWindow = validRules.maxOf { it.days }
        if (maxWindow >= 62) return null
        val keepBits = (maxWindow - 1).coerceAtLeast(0)
        val keepMask = if (keepBits == 0) 0L else (1L shl keepBits) - 1L
        // t以上のcapは実質無制限（対象日はt日を超えられない）＝既存の無制限挙動と完全に同値。
        val capBound = if (maxCount >= t) t else maxCount.coerceAtLeast(0)

        data class Rec(val cost: Long, val bits: Long)
        fun tie(day: Int): Long {
            var z = seed xor (day.toLong() * -0x61c8864680b583ebL)
            z = z xor (z ushr 33); z *= -0x00ae502812aa7333L; z = z xor (z ushr 29)
            return z and 511L
        }

        // 状態キー=(直近maxWindow-1日分のビット列, 累積対象日数)。累積数がcapBoundを超える遷移は
        // 生成しない＝個人上限を構造的に超過できない。
        var dp = HashMap<Pair<Long, Int>, Rec>()
        dp[0L to 0] = Rec(0L, 0L)
        for (day in 0 until t) {
            val next = HashMap<Pair<Long, Int>, Rec>(maxOf(16, dp.size * 2))
            val choices = when (forced[day]) {
                1 -> intArrayOf(1)
                0 -> intArrayOf(0)
                else -> intArrayOf(0, 1)
            }
            for ((key, rec) in dp) {
                val (mask, cnt) = key
                for (bit in choices) {
                    val newCnt = cnt + bit
                    if (newCnt > capBound) continue
                    val full = (mask shl 1) or bit.toLong()
                    var fireInc = 0
                    for (rule in validRules) {
                        if (day + 1 < rule.days) continue
                        val rm = (1L shl rule.days) - 1L
                        if (java.lang.Long.bitCount(full and rm) < rule.minimum) fireInc++
                    }
                    val cost = rec.cost + fireInc.toLong() * 1_000_000L + bit.toLong() * 1_000L + tie(day)
                    val newMask = full and keepMask
                    val bits = if (bit == 1) rec.bits or (1L shl day) else rec.bits
                    val nk = newMask to newCnt
                    val old = next[nk]
                    if (old == null || cost < old.cost ||
                        (cost == old.cost && java.lang.Long.compareUnsigned(bits, old.bits) < 0)
                    ) {
                        next[nk] = Rec(cost, bits)
                    }
                }
            }
            if (next.isEmpty()) return null
            dp = next
        }
        var best: Rec? = null
        for (rec in dp.values) {
            val b = best
            if (b == null || rec.cost < b.cost ||
                (rec.cost == b.cost && java.lang.Long.compareUnsigned(rec.bits, b.bits) < 0)
            ) {
                best = rec
            }
        }
        val chosen = best ?: return null
        return BooleanArray(t) { day -> ((chosen.bits ushr day) and 1L) != 0L }
    }
}
