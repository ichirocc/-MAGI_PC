package com.magi.app.v6

/**
 * [C1 Repair Analysis + Exact Window Repair / 3.273.0] C1（窓の要件）を「評価」と「修復」で完全分離する。
 *
 * 設計原則（ユーザー合意 A1-A6 / v8第2段）:
 *  - **評価器（checker/Evaluator/C++）は修復を考えない**。本モジュールは checker を一切変えず、
 *    その出力（c1違反）を構造化し（A6=`C1WindowViolation`/`RepairOpportunity`）、修復候補を生成する。
 *  - **A2/A3=厳密窓修復**は OR-Tools 等の重量ネイティブ依存を持ち込まず、**純Kotlinの分枝限定探索**で
 *    窓スコープの部分問題（数職員×窓幅日）を解く（実機で検証不能なネイティブ依存を避ける＝
 *    「checkerが正・番兵で照合」の契約を守る）。探索は **coverage保存**（各日のシフト多重集合を
 *    関与職員の間で並べ替えるだけ）＝covU/covO構造的に不変。最終採用は必ず呼出側の checker+keep-best。
 *  - `exhaustive=true`（node予算内で全探索完了）のときのみ `minJointC1` は**証明された下限**（A3）。
 *    予算超過時は best-effort（探索多様化として安全＝keep-best が退化を防ぐ）。
 */
object C1RepairAnalysis {

    /** A6: 構造化されたc1窓違反（1窓=1件。checker の inc("c1") 意味論と一致）。 */
    data class C1WindowViolation(
        val ruleIndex: Int,
        val staff: Int,
        val start: Int,
        val windowDays: Int,
        val shift: Int,
        val required: Int,
        val actual: Int,
    ) {
        val deficit: Int get() = (required - actual).coerceAtLeast(0)
    }

    /** A6: 窓内の各候補日の局所情報（探索はまだしない＝Analysisの仕事）。 */
    data class RepairOpportunity(
        val day: Int,
        val gain: Int,          // この日を shift にすると解消される窓不足の数（重複窓ボーナス込み）
        val coverageRisk: Int,  // この日 shift を増やすと covO / 別シフト covU が悪化する量
        val patternRisk: Boolean, // この日 shift にすると c3n（禁止連続）を作る
        val wishConflict: Boolean, // 希望で他シフトに固定されている
    )

    data class Config(
        val maxInvolvedStaff: Int = 6,
        val maxWindowDays: Int = 16,
        val nodeBudget: Int = 300_000,
        val perDayBranchCap: Int = 24,
    )

    /** A2/A3: 窓スコープ厳密探索の結果。 */
    data class ExactResult(
        val minJointC1: Int,         // 関与職員の joint c1 の（探索した中で）最小値
        val baselineJointC1: Int,    // 変更前の joint c1
        val patch: List<IntArray>?,  // [[staff,day,newShift],...]（minを達成する差分）。改善なし=null
        val exhaustive: Boolean,     // node予算内で全探索を完了したか（true のとき minJointC1 は証明）
        // [3.279.0/外部レビューC1-03] **対象窓（v.ruleIndex/v.start）だけ**の残 fire（0 or 1）の全葉最小。
        //   旧: 焦点職員の全ルール×全窓の残数だったため、対象窓が解消可能でも別窓（別シフトのルール含む）が
        //   残るだけで「この窓は壁」と誤認していた（provenWalls の false positive）。
        val focusResidual: Int,
    )

    /** A4: coverage入替（同シフト多重集合の並べ替え）でも解消できないと**証明された**窓（cross-staff の構造的壁）。 */
    data class CoverageNeutralWall(val staff: Int, val shift: Int, val start: Int, val windowDays: Int)

    /**
     * A4: 不足窓のうち、厳密探索で「exhaustive かつ焦点職員の残c1>0」＝どう入れ替えても解消不能と
     * 証明されたものだけを返す（node予算超過=未証明は含めない＝誤検知ゼロ）。2b-2/MUS が扱わない
     * 「実際のトークン希少性を全職員横断で厳密に勘定した」構造的不能の証明。
     */
    fun provenWalls(p: Problem, schedule: Array<IntArray>, cfg: Config = Config()): List<CoverageNeutralWall> {
        val out = ArrayList<CoverageNeutralWall>()
        val seen = HashSet<Long>()
        for (v in analyze(p, schedule)) {
            // [3.279.0/外部レビューC1-04] 旧: staff×shift のみのキーで、同一職員・同一シフトの**独立した複数の
            //   不足窓**があっても最初の1窓しか証明探索せず、後続の真の壁を見逃していた。窓単位
            //   (staff×ruleIndex×start) でデデュープする（provenWalls はテスト/診断専用＝コスト増は許容）。
            val f = (v.staff.toLong() * 100_000L + v.ruleIndex) * 100_000L + v.start
            if (!seen.add(f)) continue
            val r = solveWindow(p, schedule, v, cfg)
            if (r.exhaustive && r.focusResidual > 0) out.add(CoverageNeutralWall(v.staff, v.shift, v.start, v.windowDays))
        }
        return out
    }

    // ---- A6: 解析（純 read-only） ---------------------------------------------------------------

    /** checker の c1 窓走査を忠実に再現し、不足窓を構造化列挙（全窓・アンカー集約なし）。 */
    fun analyze(p: Problem, schedule: Array<IntArray>): List<C1WindowViolation> {
        val out = ArrayList<C1WindowViolation>()
        val s = normalizeSchedule(schedule, p)
        for ((ri, c) in p.cons1.withIndex()) {
            val x = c.shiftIdx
            if (x !in 0 until p.K || c.day1 !in 1..p.T || c.day2 < 1) continue
            for (i in 0 until p.S) {
                if (!p.canDo(i, x)) continue
                var j = 0
                while (j <= p.T - c.day1) {
                    var z = 0
                    for (l in 0 until c.day1) if (s[i][j + l] == x) z++
                    if (z < c.day2) out.add(C1WindowViolation(ri, i, j, c.day1, x, c.day2, z))
                    j++
                }
            }
        }
        return out
    }

    /** 窓内の各候補日（現在 shift でない・movable な日）の局所情報を作る（探索なし）。 */
    fun opportunities(p: Problem, schedule: Array<IntArray>, v: C1WindowViolation): List<RepairOpportunity> {
        val s = normalizeSchedule(schedule, p)
        val out = ArrayList<RepairOpportunity>()
        for (d in v.start until v.start + v.windowDays) {
            if (s[v.staff][d] == v.shift) continue
            val wishConflict = p.wishLocked(v.staff, d)
            val patternRisk = p.makesForbiddenRun(s, v.staff, d, v.shift)
            // gain = この日を shift にすると**実際に解消する**（この職員の）不足窓数。
            //   [3.278.0/監査修正] ①窓開始の下界 lo は**ルールごとの窓幅 c.day1** から取る（旧: v.windowDays
            //   固定＝同一シフトのより長い別ルール窓（休の5日窓＋15日窓型）が d を含んでいても切り捨てられ過小）。
            //   ②+1 で充足に変わるのは z == c.day2 - 1 の窓のみ（旧: z < c.day2＝1手では解消しない深い不足窓も
            //   計上する過大）。checker は不足窓ごとに 1 fire 計上するため「解消数」＝ちょうど閾値未満1の窓数。
            var gain = 0
            for (c in p.cons1) {
                if (c.shiftIdx != v.shift || c.day1 !in 1..p.T) continue
                val lo = (d - c.day1 + 1).coerceAtLeast(0)
                for (ws in lo..d.coerceAtMost(p.T - c.day1)) {
                    if (d !in ws until ws + c.day1) continue
                    var z = 0
                    for (l in 0 until c.day1) if (s[v.staff][ws + l] == v.shift) z++
                    if (z == c.day2 - 1) gain++
                }
            }
            val old = s[v.staff][d]
            val cntNew = (0 until p.S).count { s[it][d] == v.shift }
            // [3.278.0/監査で実証されたクラッシュの修正] old は -1（normalizeSchedule のセンチネル＝削除済み
            //   シフト等の残存 index）になり得る。無検証で covUCell(-1,…)＝need1[-1][j] を読むと AIOOBE
            //   （hasActionableC1 ゲート／applyC1IndexChainRepair の両経路で再現済み・3.270.0 と同型の取り残し）。
            //   範囲外セルからの離脱は何も不足させないため除去項は 0。
            val removalRisk = if (old in 0 until p.K) {
                val cntOld = (0 until p.S).count { s[it][d] == old }
                p.covUCell(old, d, cntOld - 1) - p.covUCell(old, d, cntOld)
            } else 0
            val covRisk = (p.covOCell(v.shift, d, cntNew + 1) - p.covOCell(v.shift, d, cntNew)) + removalRisk
            out.add(RepairOpportunity(d, gain, covRisk, patternRisk, wishConflict))
        }
        return out
    }

    // ---- A2/A3: 窓スコープ厳密探索（coverage保存 permutation の分枝限定） -----------------------

    /** 1つの不足窓を起点に、窓を含む日スパンをまたぐ coverage保存 permutation で joint c1 を最小化する。 */
    fun solveWindow(p: Problem, schedule: Array<IntArray>, v: C1WindowViolation, cfg: Config = Config()): ExactResult {
        val s = normalizeSchedule(schedule, p)
        // [多日連動] days は単一窓幅でなく、窓を含む maxWindowDays 幅の連続スパン。狭いと「別日で連動して
        //   初めて解ける」多職員手（同日swapの合成では到達不能）を表現できないため（実測でこの拡張が必須）。
        val span = minOf(cfg.maxWindowDays, p.T)
        val startD = v.start.coerceAtMost(p.T - span).coerceAtLeast(0)
        val days = (startD until startD + span).toList()

        // 関与職員 M = i0 ∪ スパン内で shift x を持つ職員（cap 内）。coverage保存はこの M の日別多重集合を
        //   M 内で並べ替えることで担保（M外・スパン外は固定）。
        val mSet = LinkedHashSet<Int>()
        mSet.add(v.staff)
        for (d in days) for (i in 0 until p.S) if (s[i][d] == v.shift && i != v.staff) mSet.add(i)
        // 余力: 同群・x担当可の職員も少数加える（3者以上の連動を可能に）
        for (i in 0 until p.S) {
            if (mSet.size >= cfg.maxInvolvedStaff) break
            if (i !in mSet && p.canDo(i, v.shift) && p.sgrp[i] == p.sgrp[v.staff]) mSet.add(i)
        }
        if (mSet.size > cfg.maxInvolvedStaff) return ExactResult(0, 0, null, false, 0)
        val m = mSet.toIntArray()

        // 関与する cons1 規則（M のいずれかが担当可で、窓と交差しうるもの）
        val rules = p.cons1.filterIndexed { _, c ->
            c.shiftIdx in 0 until p.K && c.day1 in 1..p.T && c.day2 >= 1 && m.any { p.canDo(it, c.shiftIdx) }
        }

        // 各職員の行（窓外固定・窓内可変）。joint c1 は M 全員の全 cons1 fire 合計。
        val rows = Array(m.size) { s[m[it]].clone() }
        fun jointC1(): Int {
            var total = 0
            for (mi in m.indices) {
                val i = m[mi]
                for (c in rules) {
                    if (!p.canDo(i, c.shiftIdx)) continue
                    var jj = 0
                    while (jj <= p.T - c.day1) {
                        var z = 0
                        for (l in 0 until c.day1) if (rows[mi][jj + l] == c.shiftIdx) z++
                        if (z < c.day2) total++
                        jj++
                    }
                }
            }
            return total
        }
        val baseline = jointC1()
        // [3.279.0/外部レビューC1-03] 焦点職員 i0 の**対象窓（v.start〜v.start+v.windowDays）だけ**の残 fire。
        //   旧: 全ルール×全窓の残数＝対象窓が解消可能でも別窓が残るだけで「この窓は壁」と誤認していた。
        val fi = m.indexOf(v.staff)
        fun focusResidualOf(arr: Array<IntArray>): Int {
            if (fi < 0) return 0
            var z = 0
            for (l in 0 until v.windowDays) {
                val d = v.start + l
                if (d in 0 until p.T && arr[fi][d] == v.shift) z++
            }
            return if (z < v.required) 1 else 0
        }
        if (baseline == 0) return ExactResult(0, 0, null, true, 0)

        // 各日 d の M 多重集合（並べ替え対象）と、その日の固定要素（希望ロック職員は自分の希望へ固定）。
        var nodes = 0
        var budgetHit = false
        var best = baseline
        var bestRows: Array<IntArray>? = null
        // [3.274.0 監査で発見・修正] A4 provenWalls 用の焦点残の最小値。探索した全ての葉（=完全配置）で
        //   焦点残を測り最小を追跡する。旧実装は探索後に「復元されない rows（＝最後の葉）」から焦点残を
        //   計算しており、①rows が探索の副作用で汚染 ②min-joint と min-focus は別物、の2重の誤りで
        //   列挙順依存の false wall を生んでいた（provenWallsの「証明つき/誤検知ゼロ」契約違反）。
        //   葉ごとに測って最小を取ることで、exhaustive時に「coverage入替でも焦点は解消不能」を厳密に証明する。
        var minFocusResidual = focusResidualOf(rows)   // rows は現時点で元の盤面（=baseline配置）

        // DFS: days を1つずつ、その日の多重集合を M へ割り当てる全単射を枚挙。
        fun assignDay(dayIdx: Int, onComplete: () -> Unit) {
            if (dayIdx == days.size) { onComplete(); return }
            val d = days[dayIdx]
            // その日の M の現在シフト多重集合
            val multiset = ArrayList<Int>(m.size)
            for (mi in m.indices) multiset.add(s[m[mi]][d])
            // 各 M 職員が取り得るシフト（希望ロックは自分の希望のみ）
            // 割当は「多重集合の要素を各職員へ配る全単射」＝バックトラッキング。
            val used = BooleanArray(multiset.size)
            // 分岐順序: i0 は shift を優先、他は現状維持を優先（有望領域を先に）。
            fun orderedSlots(mi: Int): IntArray {
                val idx = (0 until multiset.size).sortedWith(compareByDescending { si ->
                    val sh = multiset[si]
                    var pri = 0
                    if (m[mi] == v.staff && sh == v.shift) pri += 100
                    if (sh == s[m[mi]][d]) pri += 10       // 現状維持
                    pri
                }).toIntArray()
                return idx
            }
            var branchCount = 0
            fun place(mi: Int) {
                if (budgetHit) return
                if (mi == m.size) {
                    assignDay(dayIdx + 1, onComplete); return
                }
                val i = m[mi]
                val wl = if (p.wishLocked(i, d)) p.wish[i][d] else -1
                // [3.279.0/外部レビューC1-10] 同一シフトが多重集合に複数あるとスロット単位の列挙が同値部分木を
                //   重複探索し、node予算を浪費して不必要に exhaustive=false になっていた。同じシフト値は
                //   職員 mi につき1回だけ試す（残り多重集合が同じ＝部分木は同値、の標準的な重複排除）。
                val tried = HashSet<Int>()
                for (si in orderedSlots(mi)) {
                    if (used[si]) continue
                    val sh = multiset[si]
                    if (sh in tried) continue
                    if (!p.canDo(i, sh)) continue
                    if (wl >= 0 && sh != wl) continue
                    if (mi == 0 && branchCount >= cfg.perDayBranchCap) { budgetHit = true; break }
                    tried.add(sh)
                    used[si] = true
                    rows[mi][d] = sh
                    if (mi == 0) branchCount++
                    if (++nodes > cfg.nodeBudget) { budgetHit = true; used[si] = false; return }
                    place(mi + 1)
                    used[si] = false
                    if (budgetHit) return
                }
            }
            place(0)
        }

        assignDay(0) {
            val jc = jointC1()
            if (jc < best) { best = jc; bestRows = Array(m.size) { rows[it].clone() } }
            // 葉(=完全配置)ごとに焦点残を測り最小を追跡（rows はこの時点で全 days 割当済み）。
            val fr = focusResidualOf(rows)
            if (fr < minFocusResidual) minFocusResidual = fr
        }

        val patch = bestRows?.let { br ->
            val diff = ArrayList<IntArray>()
            for (mi in m.indices) for (d in days) if (br[mi][d] != s[m[mi]][d]) diff.add(intArrayOf(m[mi], d, br[mi][d]))
            if (diff.isEmpty()) null else diff
        }
        // exhaustive のとき minFocusResidual は「coverage入替でどう並べても焦点はこれ以上減らせない」証明値。
        return ExactResult(best, baseline, patch, !budgetHit, minFocusResidual)
    }
}
