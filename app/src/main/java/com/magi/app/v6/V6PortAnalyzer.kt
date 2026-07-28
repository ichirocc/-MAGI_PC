package com.magi.app.v6

import com.magi.app.model.C3Row
import com.magi.app.model.MagiState
import java.time.LocalDate
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.roundToInt

/** Native port of the V6 Web overview / risk / load analysis layer. */
data class V6DayRisk(
    val dayIndex: Int,
    val label: String,
    val shortage: Int,
    val detail: String,
)

data class V6StaffProfile(
    val staffIndex: Int,
    val name: String,
    val groupSymbol: String,
    val workCount: Int,
    val violationCount: Int,
    val workloadText: String,
)

data class V6PortReport(
    val coveragePct: Int?,
    val demand: Int,
    val covU: Int,
    val hardCore: Int,
    val hardGuard: Int,
    val softCore: Int,
    val topRiskDay: Int,
    val topRiskLabel: String,
    val topRiskShortage: Int,
    val dayRisks: List<V6DayRisk>,
    val staffProfiles: List<V6StaffProfile>,
    val aptPenalty: Double,
    val equPenalty: Double,
    val sanityWarnings: List<String>,
    val sanityNotes: List<String>,
) {
    /** Days that still have a coverage shortage (referenced by the V6 RISK gauge). */
    val highRiskDays: Int get() = dayRisks.count { it.shortage > 0 }
}

/** 充足不可(infeasible)＝担当可能な職員数が必要数に届かない / 充足可能(fixable)＝枠は足りるが最適化が未到達。 */
enum class CoverageVerdict { INFEASIBLE, FIXABLE }

/** 人員不足(covU)が残る 1 つの (日, シフト) 枠の診断。読み取り専用・エンジン非変更。 */
data class CoverageShortfall(
    val dayIndex: Int,
    val dayLabel: String,
    val shiftIndex: Int,
    val shiftSymbol: String,
    val need: Int,
    val got: Int,
    val miss: Int,
    /** その日そのシフトに法的に配置し得る最大職員数（担当可 かつ 別シフトへ希望固定されていない）。 */
    val capacity: Int,
    val verdict: CoverageVerdict,
    val reason: String,
)

/** 人員過剰(covO)が残る 1 つの (日, シフト) 枠の診断。読み取り専用・エンジン非変更。 */
data class CoverageSurplus(
    val dayIndex: Int,
    val dayLabel: String,
    val shiftIndex: Int,
    val shiftSymbol: String,
    val need: Int,
    val got: Int,
    val excess: Int,
    /** この枠の在勤者中、他シフトへ動かせる／動かせない内訳と理由。 */
    val reason: String,
)

/** covU(人員不足)の原因診断。どの枠が「数学的に充足不可」か「充足可能だが未到達」かを切り分ける。 */
data class CoverageDiagnosis(
    val totalShortfall: Int,
    val infeasibleSlots: Int,
    val fixableSlots: Int,
    val shortfalls: List<CoverageShortfall>,
    val relaxations: List<String> = emptyList(),  // [IIS/緩和案] 担当追加で解ける見込みの提案（データは変えない）
    /** [人員過剰(covO)の「なぜ減らないか」診断] shortfalls と対の存在。データは変えない。 */
    val totalSurplus: Int = 0,
    val surpluses: List<CoverageSurplus> = emptyList(),
) {
    val hasShortage: Boolean get() = totalShortfall > 0
    /** 不足が全て「充足不可」＝このデータでは HARD=0 にできない（想定内の残存）。 */
    val allInfeasible: Boolean get() = hasShortage && fixableSlots == 0
    val hasSurplus: Boolean get() = totalSurplus > 0

    /** 診断ログ（エクスポートされる「MAGI ログ」に載る形式の文字列）。 */
    fun logLines(): List<String> {
        val out = ArrayList<String>()
        if (hasShortage) {
            out.add("[W] CoverageDiag: 人員不足 合計${totalShortfall} — 充足不可${infeasibleSlots}枠 / 充足可能${fixableSlots}枠")
            for (s in shortfalls.take(8)) {
                val v = if (s.verdict == CoverageVerdict.INFEASIBLE) "充足不可" else "充足可能"
                out.add("[W] CoverageDiag: ${s.dayLabel} ${s.shiftSymbol} 必要${s.need}/現状${s.got}(不足${s.miss}) — ${v}: ${s.reason}")
            }
            if (shortfalls.size > 8) out.add("[W] CoverageDiag: ほか${shortfalls.size - 8}枠")
            for (r in relaxations.take(4)) out.add("[W] CoverageDiag 緩和案: $r")
        }
        if (hasSurplus) {
            out.add("[W] CoverageDiag: 人員過剰 合計${totalSurplus} — ${surpluses.size}枠（なぜ減らないか）")
            for (s in surpluses.take(8)) {
                out.add("[W] CoverageDiag: ${s.dayLabel} ${s.shiftSymbol} 必要${s.need}/現状${s.got}(過剰${s.excess}) — ${s.reason}")
            }
            if (surpluses.size > 8) out.add("[W] CoverageDiag: ほか${surpluses.size - 8}枠（過剰）")
        }
        return out
    }
}

/** 禁止連続(c3n)違反 run の1セルの脱出可否分類。 */
enum class ForbiddenCellEscape {
    /** 安全な代替シフトが存在（適用すれば HARD が厳密に減る＝探索未到達の可能性）。 */
    FREE,
    /** 直接は離脱元シフトが covU 化するが、玉突き連鎖（findCovUChain）で埋め直せることを実証済み。 */
    CHAIN,
    /** 代替は全て新たな禁止連続を作るが、隣接日調整（tryFixForbiddenRunViaAdjacentDay）で崩せることを実証済み。 */
    ADJACENT,
    /** 本人の希望で固定（動かすと pref(9000)>c3n(7000) の悪化＝isBetter が正しく却下する）。 */
    PINNED,
    /** 全ての代替が塞がっている（新たな禁止連続・covU受け皿なし・代替シフトなし）。 */
    BLOCKED,
}

/** 禁止連続(c3n)違反 run 1件ぶんのセル別診断。 */
data class ForbiddenRunCell(
    val dayIndex: Int,
    val dayLabel: String,
    val shiftSymbol: String,
    val escape: ForbiddenCellEscape,
    /** 分類の根拠（代替の内訳件数など）。 */
    val detail: String,
)

/** 禁止連続(c3n)違反 run 1件の診断（CoverageShortfall/CoverageSurplus と対の存在）。 */
data class ForbiddenRunDiag(
    val staffIndex: Int,
    val staffName: String,
    val startDay: Int,
    /** 例: "Cｱ→Aｱ"（禁止パターンの記号列）。 */
    val seqLabel: String,
    val cells: List<ForbiddenRunCell>,
    /** run 全体の判定と次の一手の案内。 */
    val hint: String,
) {
    val escapable: Boolean get() = cells.any {
        it.escape == ForbiddenCellEscape.FREE || it.escape == ForbiddenCellEscape.CHAIN ||
            it.escape == ForbiddenCellEscape.ADJACENT
    }
}

/**
 * [3.280.0] 禁止連続(c3n, HARD)の「なぜ崩せないか」診断。CoverageDiag（covU/covO）と対。
 * 実機で c3n=1 が HARD 専任ワーカー67エポックでも不動だった事例（2026-12 データ・アリフ Cｱ→Aｱ）で、
 * 「構造的に不能」か「探索漏れ」かをログから判別できなかった穴を埋める。読取専用・スコア不変。
 */
data class ForbiddenRunDiagnosis(
    val totalRuns: Int,
    val runs: List<ForbiddenRunDiag>,
) {
    val hasRuns: Boolean get() = totalRuns > 0
    /** 全 run が構造的に塞がっている（＝このデータ・希望のままでは c3n を 0 にできない）。 */
    val allBlocked: Boolean get() = hasRuns && runs.none { it.escapable }

    /** 診断ログ（エクスポートされる「MAGI ログ」に載る形式の文字列）。 */
    fun logLines(): List<String> {
        if (!hasRuns) return emptyList()
        val out = ArrayList<String>()
        out.add("[W] ForbiddenDiag: 禁止連続(c3n) ${totalRuns}件 — なぜ崩せないか")
        for (r in runs.take(6)) {
            val cellsTxt = r.cells.joinToString(" / ") { c ->
                val tag = when (c.escape) {
                    ForbiddenCellEscape.FREE -> "崩せる"
                    ForbiddenCellEscape.CHAIN -> "玉突きで崩せる"
                    ForbiddenCellEscape.ADJACENT -> "隣接日調整で崩せる"
                    ForbiddenCellEscape.PINNED -> "希望固定"
                    ForbiddenCellEscape.BLOCKED -> "塞がり"
                }
                "${c.dayLabel}=${c.shiftSymbol}:${tag}(${c.detail})"
            }
            out.add("[W] ForbiddenDiag: ${r.staffName} ${r.seqLabel} — $cellsTxt")
            out.add("[W] ForbiddenDiag: ${r.staffName} ${r.seqLabel} → ${r.hint}")
        }
        if (runs.size > 6) out.add("[W] ForbiddenDiag: ほか${runs.size - 6}件")
        return out
    }
}

object V6PortAnalyzer {
    /**
     * 人員不足(covU)の枠ごとの原因診断。エンジンは変更せず、現在の解だけを読み取り、
     * 各不足枠について「担当可能な職員の最大数(capacity)」を数え、必要数に届くかで判定する。
     *  - capacity < need → INFEASIBLE（どう割り当ててもこの枠は埋まらない＝データ上充足不可）
     *  - capacity >= need → FIXABLE（枠は足りる。他シフトに就いている人を移せば理論上は解消し得るが、
     *    並び/回数などの制約に阻まれ最適化が未到達）
     */
    fun diagnoseCoverage(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
        report: ViolationReport = UnifiedViolationChecker.check(state, schedule),
    ): CoverageDiagnosis {
        val p = cachedProblem(state)
        val norm = normalizeSchedule(schedule, p)
        val cov = coverage(p, norm)
        // [なぜ埋まらないか / 三連・五連など任意長対応] 職員 i を日 j にシフト newK へ動かすと
        //   禁止連続(c3n)を作るか。Problem.makesForbiddenRun が任意長ルールを一般判定する。
        fun c3nAt(i: Int, j: Int, newK: Int): Boolean = p.makesForbiddenRun(norm, i, j, newK)
        val list = ArrayList<CoverageShortfall>()
        var infeasible = 0
        var fixable = 0
        var total = 0
        for (j in 0 until p.T) {
            for (k in 0 until p.K) {
                // [監査/実バグ修正] need1 のみを見て miss=need1-got を計算していたため、need1 が未設定で
                //   need2 単独定義のセル（Problem.covUCell の「片方定義=その値」対応セル）が丸ごと
                //   スキップされ、本物の covU(HARD) 違反が診断から完全に消えていた（need2<need1 の
                //   OR救済無視という既知の理論的エッジケースより広く、通常のデータでも起こり得る）。
                //   covUCell（source of truth）を直接使い、need1/need2 双方の OR 意味論に一致させる。
                val got = cov[j][k]
                val miss = p.covUCell(k, j, got)
                if (miss <= 0) continue
                val need = got + miss   // [表示用] 実際に不足を生んだ実効しきい値（covUCellのOR選択と整合）
                total += miss
                var capacity = 0
                for (i in 0 until p.S) {
                    if (!p.canDo(i, k)) continue
                    val w = p.wish[i][j]
                    if (w in 0 until p.K && w != k) continue   // 別シフトへ希望固定 → この枠には回せない
                    capacity++
                }
                val verdict = if (capacity < need) CoverageVerdict.INFEASIBLE else CoverageVerdict.FIXABLE
                if (verdict == CoverageVerdict.INFEASIBLE) infeasible++ else fixable++
                val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                val reason = if (verdict == CoverageVerdict.INFEASIBLE) {
                    "担当可能な職員が${capacity}人で必要数${need}に届きません（データ上、充足不可）"
                } else {
                    // [なぜ埋まらないか] 「移せる候補」(canDo・別シフト希望でない)を、なぜ今動かせないかで
                    //   5分類する。既に在勤=capacity には入るが移動候補ではない / 空き番=休/過剰から直接移せる /
                    //   玉突き=引くと別のcovU / 希望固定=本人の希望で固定 / 禁止連続=移すと c3n。読取専用・スコア不変。
                    //   [敵対的レビュー修正] already を明示計上し free+cascade+pinned+forbid+already==capacity を
                    //   保証（旧: already を素通り=capacity と内訳合計が一致せず表示が混乱を招いた）。
                    var already = 0; var free = 0; var cascade = 0; var pinned = 0; var forbid = 0
                    for (i in 0 until p.S) {
                        if (!p.canDo(i, k)) continue
                        val m = norm[i][j]
                        val w = p.wish[i][j]
                        if (w in 0 until p.K && w != k) continue               // 別シフトへ希望固定=capacity 対象外
                        if (m == k) { already++; continue }                    // 既にこのシフト=移す対象でない
                        // [監査(未レビュー領域再監査) 実バグ修正] p.wishLocked(i,j) は「希望が設定されている」の
                        //   意味だが、上の事前フィルタ(127-128行目)で w!=k の候補は既に除外済み＝ここに残る
                        //   wishLocked==true は必ず wish==k（=まさにこのシフトへの希望）。希望と移動先が一致する
                        //   候補を「固定されていて動かせない」と分類するのは意味が逆転している（むしろ動かすと
                        //   希望未充足(pref)も同時に解消できる最良の候補）。他シフトへの希望固定は既に対象外
                        //   なので、本関数では「希望固定」で除外すべき候補は存在せず、free/cascade判定へ委ねる。
                        if (c3nAt(i, j, k)) { forbid++; continue }
                        // m から1人引くと covU が増える=玉突き（多人数入替=連鎖でしか解けない）。
                        if (m in 0 until p.K && p.covUCell(m, j, cov[j][m] - 1) > p.covUCell(m, j, cov[j][m])) cascade++ else free++
                    }
                    // [3.263.0, 深い停滞調査(600秒改善ゼロ)で判明] 「玉突き」は1ホップ判定
                    // （このセルへの直接移動は別のcovUを生む）に過ぎず、その先が実際に埋まる保証が
                    // 無かった。実データ検証(findCovUChainを200 seed総当たり)で「玉突き候補はいる
                    // のに、その先を埋める人が全員その日の希望で固定されており実際は誰一人動かせ
                    // ない」という真の壁を確認済み（pref(重み9000)>covU(重み8000)のため、希望を
                    // 破ってまでcovUを直す手はisBetterが正しく却下する＝バグではない）。診断が
                    // 「玉突きが必要」と楽観的に言うだけでは、この壁を「もっと粘れば直る」との
                    // 誤解を招くため、findCovUChain（探索本体と同一の関数）で実在を確認してから
                    // 案内を出し分ける。複数seedを試すのは、rng順（候補の並べ替え）に依存する
                    // 網羅性の揺らぎを吸収し安定した判定にするため（実データで200 seed総当たりし
                    // 全て不成立だった局面を確認済み・8 seedは診断呼出コストとのバランス）。
                    val chainVerified = cascade > 0 && (0 until 8).any { seed ->
                        findCovUChain(p, norm, k, j, java.util.Random(seed.toLong())) != null
                    }
                    val hint = when {
                        free > 0 -> "空き番${free}人を${sym}へ移せば充足（最適化が未到達＝勤務表でこのセルの『直し方を探す』で解消可）"
                        cascade > 0 && chainVerified -> "空き番が無く、過剰シフトからの多人数入替（玉突き=ブロック移動）が必要"
                        cascade > 0 -> "玉突き候補${cascade}人はいますが、移動先の受け皿もすべて希望固定/禁止連続で塞がっており、" +
                            "現在の希望のままではどう組んでも解消できません。希望を1件調整するか担当を追加してください"
                        else -> "候補が希望/禁止連続で塞がっており、希望を1件調整するか担当を追加すると解消に近づく"
                    }
                    "担当可能${capacity}人（うち在勤中${already}人）・今動かせる空き番${free}人（玉突き${cascade}・希望固定${pinned}・禁止連続${forbid}）。$hint"
                }
                list.add(
                    CoverageShortfall(j, dayLabel(state.startDate, j), k, sym, need, got, miss, capacity, verdict, reason)
                )
            }
        }
        list.sortWith(
            compareByDescending<CoverageShortfall> { it.verdict == CoverageVerdict.INFEASIBLE }
                .thenByDescending { it.miss }
        )
        // [緩和案/IIS] 構造的に充足不可なシフトについて、担当追加(クロストレーニング)で解ける見込みを提示する。
        //   候補は未活用(需要のあるシフトへの稼働が少ない)職員を優先。これは担当追加の「提案」であって
        //   データは一切変更しない（採否は業務担当者が判断）。HF77準拠。
        val relaxations = ArrayList<String>()
        run {
            // [同根修正] need1 単独判定だと need2 単独定義シフトの需要を見落とす（上の miss 計算と同じ穴）。
            val demandShifts = (0 until p.K).filter { kk ->
                (0 until p.T).any { jj -> p.need1[kk][jj] > 0 || (p.use2 && p.need2[kk][jj] > 0) }
            }.toSet()
            fun demandLoad(i: Int): Int = (0 until p.T).count { jj -> norm[i][jj] in demandShifts }
            val infeasByShift = list.filter { it.verdict == CoverageVerdict.INFEASIBLE }
                .groupBy { it.shiftIndex }
                .mapValues { e -> e.value.maxOf { it.miss } }
            for ((k, peakMiss) in infeasByShift.entries.sortedByDescending { it.value }) {
                val sym = state.shifts.getOrNull(k)?.kigou ?: "$k"
                val cands = (0 until p.S).filter { !p.canDo(it, k) }
                    .sortedBy { demandLoad(it) }
                    .take((peakMiss + 1).coerceAtLeast(2))
                    .map { state.staff.getOrNull(it)?.name ?: "#$it" }
                if (cands.isNotEmpty()) {
                    relaxations.add("「$sym」は担当可能者が不足（ピーク不足${peakMiss}人）。$sym を ${cands.joinToString("・")}（稼働が少なめ）に担当追加すると解消に近づきます")
                }
            }
        }
        // [人員過剰(covO)の「なぜ減らないか」診断] covU診断(空き番/玉突き/希望固定/禁止連続)の対。
        //   在勤者を他シフトへ動かせば消えるはずの過剰が、なぜ最適化で解消されないかを枠ごとに示す。
        //   covO は全19族中もっとも軽い(重み1.0)ため、動かした先で他の族が1点でも悪化すると
        //   isBetter に負けて採用されない＝件数自体は「動かせるか」の構造診断であり、
        //   「動かせるのに動いていない」ことの説明にはならない点に注意（読取専用・スコア不変）。
        val surplusList = ArrayList<CoverageSurplus>()
        var totalSurplus = 0
        for (j in 0 until p.T) {
            for (k in 0 until p.K) {
                val got = cov[j][k]
                val excess = p.covOCell(k, j, got)
                if (excess <= 0) continue
                val need = got - excess
                totalSurplus += excess
                val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                var pinned = 0; var forbid = 0; var cascade = 0; var free = 0
                for (i in 0 until p.S) {
                    if (norm[i][j] != k) continue   // このシフトの在勤者だけが移動候補
                    if (p.wish[i][j] == k) { pinned++; continue }   // 本人希望＝動かすと希望未充足(pref)化
                    val alts = p.allowedShiftsForStaff(i).filter { it != k }
                    if (alts.isEmpty()) { forbid++; continue }      // 担当可能な代替シフトが無い
                    var hasRoom = false; var blockedByC3n = true
                    for (m in alts) {
                        if (c3nAt(i, j, m)) continue
                        blockedByC3n = false
                        // m へ1人足しても covO が増えない＝受け皿あり。
                        if (p.covOCell(m, j, cov[j][m] + 1) <= p.covOCell(m, j, cov[j][m])) { hasRoom = true; break }
                    }
                    when {
                        hasRoom -> free++
                        !blockedByC3n -> cascade++   // 代替はあるが、どこも受け皿がない＝玉突きが必要
                        else -> forbid++              // 代替は全て禁止連続で塞がる
                    }
                }
                val hint = when {
                    free > 0 -> "在勤${free}人を他シフトへ移せば解消可能（最適化が未到達＝勤務表でこのセルの『直し方を探す』で解消可）"
                    cascade > 0 -> "移動先はどこも定員一杯で、過剰シフトからの多人数入替（玉突き）が必要"
                    else -> "在籍者は希望固定/禁止連続で動かせず、希望を1件調整するか担当を減らすと解消に近づく"
                }
                surplusList.add(
                    CoverageSurplus(j, dayLabel(state.startDate, j), k, sym, need, got, excess,
                        "在勤者中 動かせる${free}人・玉突き必要${cascade}人・希望固定${pinned}人・禁止連続${forbid}人。$hint")
                )
            }
        }
        surplusList.sortByDescending { it.excess }
        return CoverageDiagnosis(total, infeasible, fixable, list, relaxations, totalSurplus, surplusList)
    }

    /**
     * [3.280.0] 禁止連続(c3n)の「なぜ崩せないか」診断。CoverageDiag と同じ設計思想:
     * エンジンは変更せず現在の解だけを読み取り、違反 run の各セルについて「単一セル変更で崩せるか」を
     * HARD 意味論（c3n 正味減・pref・covU 離脱穴）で厳密に分類する。
     *  - FREE: 安全な代替あり＝適用すれば HARD が厳密に減る（isBetter は必ず採用）＝探索未到達の可能性。
     *  - CHAIN: 離脱元が covU 化するが findCovUChain（探索本体と同一関数・8 seed）で埋め直せることを実証。
     *  - ADJACENT: 代替は全て新たな禁止連続を作るが、隣接日調整（tryFixForbiddenRunViaAdjacentDay=
     *    探索本体と同一関数）で崩せることを実証。
     *  - PINNED: 本人希望どおりのセルで、かつどの代替も**正味の HARD を減らせない**＝isBetter が正しく却下。
     *    （3.311.0 で厳密化。旧実装は希望が一致した時点で無条件に固定扱いしており、1セルが複数の
     *    禁止連続 fire に関与する局面で偽の壁を作っていた。）
     *  - BLOCKED: 全代替が「新たな禁止連続」か「covU 受け皿なし」＝この希望・担当のままでは崩せない。
     * 3.263.0 の教訓（「玉突きが必要」と楽観的に言うだけでは壁を誤解させる）に従い、CHAIN/ADJACENT は
     * 実際に探索本体の関数で成立を確認してからそう名乗る。読取専用・スコアリング不変。
     */
    fun diagnoseForbiddenRuns(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
    ): ForbiddenRunDiagnosis {
        val p = cachedProblem(state)
        val norm = normalizeSchedule(schedule, p)
        val cov = coverage(p, norm)
        fun sym(k: Int) = state.shifts.getOrNull(k)?.kigou ?: k.toString()
        val runs = ArrayList<ForbiddenRunDiag>()
        val seen = HashSet<String>()   // 重複ルール（DuplicateSeq）由来の同一 run を1件に集約
        for (c in p.cons3n) {
            val seq = c.seq
            val d = seq.size
            if (d == 0 || d > p.T) continue
            val seqLabel = seq.joinToString("→") { sym(it) }
            for (i in 0 until p.S) {
                var j0 = 0
                while (j0 <= p.T - d) {
                    var z = 0
                    for (l in 0 until d) if (norm[i][j0 + l] == seq[l]) z++
                    if (z != d) { j0++; continue }   // checker の forbidden 窓完全一致と同一意味論
                    val key = "$i,$j0,$seqLabel"
                    if (!seen.add(key)) { j0++; continue }
                    val cells = ArrayList<ForbiddenRunCell>()
                    for (l in 0 until d) {
                        val j = j0 + l
                        val cur = norm[i][j]
                        cells.add(diagnoseForbiddenCell(state, p, norm, cov, i, j, cur))
                    }
                    val pinnedDays = cells.filter { it.escape == ForbiddenCellEscape.PINNED }.joinToString("・") { it.dayLabel }
                    val hint = when {
                        cells.any { it.escape == ForbiddenCellEscape.FREE } ->
                            "安全に崩せる手が存在します（適用すれば必須違反が減る＝探索未到達の可能性。勤務表の該当セルから『直し方を探す』で解消を試せます）"
                        cells.any { it.escape == ForbiddenCellEscape.CHAIN || it.escape == ForbiddenCellEscape.ADJACENT } ->
                            "単独の1手では崩せず、玉突き連鎖/隣接日調整の多段手でのみ崩せます（探索は候補を持っています＝再実行で解消し得ます）"
                        // [3.284.0/外部レビュー] 「証明」の強さを分ける: 全セル希望固定は「本人希望どおりの並びが
                        //   禁止パターンを構成」＝辞書式意味論(pref9000>c3n7000)の下で証明相当。それ以外の塞がり
                        //   (受け皿なし等)は「現在の探索手(単独変更・玉突き連鎖・隣接日調整)を検証して全て不成立」
                        //   という強い証拠であり、全勤務表空間の数学的な非充足証明ではない＝断定を避けた表現にする。
                        cells.all { it.escape == ForbiddenCellEscape.PINNED } ->
                            "本人希望どおりの並びが禁止パターンを構成しています（希望固定: $pinnedDays）。" +
                                "希望を変えない限りどう組んでもこの禁止連続は残ります。どちらか1件の希望を調整してください"
                        else ->
                            "全セルが塞がっています" +
                                (if (pinnedDays.isNotEmpty()) "（希望固定: $pinnedDays）" else "") +
                                "。単独変更・玉突き連鎖・隣接日調整のすべてを検証して不成立＝現在の希望・担当のままでは" +
                                "崩せる見込みがありません。周辺の希望を1件調整するか、担当を追加してください"
                    }
                    runs.add(ForbiddenRunDiag(i, state.staff.getOrNull(i)?.name ?: "#$i", j0, seqLabel, cells, hint))
                    j0++
                }
            }
        }
        // 構造的に塞がっている run（＝業務担当者の対処が必要）を先頭へ。
        runs.sortWith(compareBy { it.escapable })
        return ForbiddenRunDiagnosis(runs.size, runs)
    }

    /** run 内の1セル (i,j,cur) の脱出可否を判定する（diagnoseForbiddenRuns 専用の下請け）。 */
    private fun diagnoseForbiddenCell(
        state: MagiState, p: Problem, norm: Array<IntArray>, cov: Array<IntArray>,
        i: Int, j: Int, cur: Int,
    ): ForbiddenRunCell {
        val label = dayLabel(state.startDate, j)
        val curSym = state.shifts.getOrNull(cur)?.kigou ?: cur.toString()
        // [3.311.0] 希望どおりのセルでも**即 PINNED にはしない**。
        //   旧実装は `wishLocked && wish == cur` で HARD 差分を一切見ずに早期 return しており、
        //   その根拠（「pref(9000) の増加が c3n(7000) の減少を上回る」）は**そのセルが c3n fire
        //   1件にしか関与しない場合しか成り立たない**。例: 禁止「A→A」・行 A,A,A の中央セルは
        //   2件の fire に関与し、B へ動かすと c3n 2→0 / pref 0→1 ＝ betterReport の第1キー hard が
        //   2→1 と厳密に改善する（weighted も 14000→9000）。つまり isBetter は採用する＝固定ではない。
        //   偽の PINNED は run 全体を「構造壁」と誤診し、3.281.0 の短い停滞タイムアウトを早期に
        //   発火させうる。そこで pref の増加分を c3n の正味減と同じ土俵で勘定する。
        val prefCost = if (p.wishLocked(i, j) && p.wish[i][j] == cur) 1 else 0
        // 行 fires の正味減判定（C1DeltaPrefilter.screenCell と同じ row-local 差分・staffC3nFires を共用）。
        val row = IntArray(p.T) { norm[i][it] }
        val firesBefore = C1DeltaPrefilter.staffC3nFires(p, row)
        fun c3nAfter(m: Int): Int {
            row[j] = m
            val after = C1DeltaPrefilter.staffC3nFires(p, row)
            row[j] = cur
            return after
        }
        // 離脱で (cur, j) に covU 穴が空くか。空くなら findCovUChain（探索本体と同一関数）で埋まるか実証する。
        val cnt = if (cur in 0 until p.K) cov[j][cur] else 0
        val departureHole = cur in 0 until p.K && p.covUCell(cur, j, cnt - 1) > p.covUCell(cur, j, cnt)
        fun chainFills(board: Array<IntArray>): Boolean = (0 until 8).any { seed ->
            findCovUChain(p, board, cur, j, java.util.Random(seed.toLong()), exclude = i) != null
        }
        var c3nBlocked = 0
        var noReceiver = 0
        var prefBlocked = 0   // c3n は減るが、希望を破る代金（pref +1）を払えない代替の数
        var chainOk: Int? = null      // CHAIN が成立した代替シフト
        var adjOk: Int? = null        // ADJACENT が成立した代替シフト
        var alts = 0
        for (m in p.allowedShiftsForStaff(i)) {
            if (m == cur) continue
            alts++
            val after = c3nAfter(m)
            // 正味 HARD が減るか（希望を破る手は pref が 1 増える。hard は族横断の件数和なので同じ単位）。
            val netOk = after + prefCost < firesBefore
            // 「新たな禁止連続を作る（＝そもそも c3n が減らない）」かどうかは pref 代とは別問題。
            //   両者を混ぜると、c3n は減るのに pref 代を払えないだけの代替まで隣接日調整へ流れてしまう。
            val createsNewRun = after >= firesBefore
            if (netOk) {
                if (!departureHole) {
                    return ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.FREE,
                        "代替「${state.shifts.getOrNull(m)?.kigou ?: m}」へ変更可")
                }
                // 離脱穴あり → 実際に動かした盤面で連鎖が埋まるかを実証。
                if (chainOk == null) {
                    val tmp = Array(p.S) { norm[it].clone() }
                    tmp[i][j] = m
                    if (chainFills(tmp)) chainOk = m else noReceiver++
                } else noReceiver++   // 既に CHAIN 成立済み＝以降の重い連鎖検証は省略（分類は不変）
            } else if (!createsNewRun) {
                // c3n 自体は減るが、希望を破る代金を払うと正味では減らない＝希望が本当に効いている。
                prefBlocked++
            } else {
                // この代替は新たな禁止連続を作る → 隣接日調整（探索本体と同一関数）で崩せるか実証。
                if (adjOk == null && chainOk == null) {
                    val tmp = Array(p.S) { norm[it].clone() }
                    val extra = tryFixForbiddenRunViaAdjacentDay(p, tmp, i, j, m, java.util.Random(7L))
                    if (extra != null) {
                        // 隣接日の手＋本セルの変更を適用し、本セルの離脱穴が残るなら連鎖で埋まるかまで確認。
                        for (mv in extra) tmp[mv[0]][mv[1]] = mv[2]
                        tmp[i][j] = m
                        if (!departureHole || chainFills(tmp)) adjOk = m else c3nBlocked++
                    } else c3nBlocked++
                } else c3nBlocked++
            }
        }
        return when {
            chainOk != null -> ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.CHAIN,
                "「${state.shifts.getOrNull(chainOk)?.kigou ?: chainOk}」へ変更＋玉突き連鎖で成立")
            adjOk != null -> ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.ADJACENT,
                "「${state.shifts.getOrNull(adjOk)?.kigou ?: adjOk}」へ変更＋隣接日調整で成立")
            alts == 0 -> ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.BLOCKED, "代替シフトなし")
            // [3.311.0] 希望どおりのセルで、かつどの代替も正味 HARD を減らせなかったときだけ
            //   「希望固定」と名乗る（旧: 希望が一致した時点で無条件に固定扱い）。
            prefBlocked > 0 -> ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.PINNED,
                "本人希望=$curSym（動かしても正味の必須違反が減らない）")
            else -> ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.BLOCKED,
                "代替${alts}件全滅: 新たな禁止連続${c3nBlocked}・covU受け皿なし${noReceiver}")
        }
    }

    fun analyze(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
        report: ViolationReport = UnifiedViolationChecker.check(state, schedule),
    ): V6PortReport {
        val p = cachedProblem(state)
        val normalized = normalizeSchedule(schedule, p)
        val cov = coverage(p, normalized)
        val counts = countMatrix(p, normalized)
        val dayRisks = buildDayRisks(state, p, cov)
        val demand = totalDemand(p)
        val covU = report.breakdown["covU"] ?: 0
        val coveragePct = if (demand > 0) {
            (((demand - covU).coerceAtLeast(0).toDouble() / demand.toDouble()) * 100.0).roundToInt()
        } else null

        var topRiskDay = -1
        var topRiskLabel = "-"
        var topRiskShortage = 0
        for (risk in dayRisks) {
            if (risk.shortage > topRiskShortage) {
                topRiskDay = risk.dayIndex
                topRiskLabel = risk.label
                topRiskShortage = risk.shortage
            }
        }

        val hardGuard = report.breakdown["groupViol"] ?: 0
        val hardCore = (report.breakdown["c3n"] ?: 0) + (report.breakdown["covU"] ?: 0) + (report.breakdown["pref"] ?: 0)
        val softCore = (report.total - hardGuard - hardCore).coerceAtLeast(0)
        val staffViol = staffViolationCounts(p, report)

        return V6PortReport(
            coveragePct = coveragePct,
            demand = demand,
            covU = covU,
            hardCore = hardCore,
            hardGuard = hardGuard,
            softCore = softCore,
            topRiskDay = topRiskDay,
            topRiskLabel = topRiskLabel,
            topRiskShortage = topRiskShortage,
            dayRisks = dayRisks,
            staffProfiles = buildStaffProfiles(state, p, normalized, counts, staffViol),
            aptPenalty = aptPenalty(state, p, counts),
            equPenalty = equalizationPenalty(state, p, normalized, counts, report.breakdown),
            sanityWarnings = sanityWarnings(state, p, normalized),
            sanityNotes = sanityNotes(state),
        )
    }

    private fun totalDemand(p: Problem): Int {
        var out = 0
        for (j in 0 until p.T) {
            for (k in 0 until p.K) {
                val need = p.need1[k][j]
                if (need > 0) out += need
            }
        }
        return out
    }

    private fun buildDayRisks(state: MagiState, p: Problem, cov: Array<IntArray>): List<V6DayRisk> {
        val out = ArrayList<V6DayRisk>(p.T)
        for (j in 0 until p.T) {
            var shortfall = 0
            val parts = ArrayList<String>()
            for (k in 0 until p.K) {
                val need = p.need1[k][j]
                if (need < 0) continue
                val miss = (need - cov[j][k]).coerceAtLeast(0)
                shortfall += miss
                if (miss > 0) {
                    val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                    parts.add("${sym}×${miss}")
                }
            }
            out.add(V6DayRisk(j, dayLabel(state.startDate, j), shortfall, parts.joinToString(" ")))
        }
        return out
    }

    private fun staffViolationCounts(p: Problem, report: ViolationReport): IntArray {
        val out = IntArray(p.S)
        fun addCellKey(key: String) {
            val i = key.substringBefore(',').toIntOrNull() ?: return
            if (i in 0 until p.S) out[i]++
        }
        for (key in report.violations.keys) addCellKey(key)
        for (key in report.countViolations.keys) addCellKey(key)
        return out
    }

    private fun buildStaffProfiles(
        state: MagiState,
        p: Problem,
        schedule: Array<IntArray>,
        counts: Array<IntArray>,
        staffViol: IntArray,
    ): List<V6StaffProfile> {
        // [監査(未レビュー領域再監査) 実バグ修正] 休記号改名時に rest=-1 となり「schedule!=-1」が常に真＝
        //   全職員を全日勤務と誤カウントしていた（3.103.0でweeklyに適用済みの p.restIdx フォールバックへ統一）。
        val rest = p.restIdx
        val profiles = ArrayList<V6StaffProfile>(p.S)
        for (i in 0 until p.S) {
            var work = 0
            for (j in 0 until p.T) if (schedule[i][j] != rest) work++

            val pairs = ArrayList<Pair<Int, Int>>()
            for (k in 0 until p.K) {
                val n = counts[i][k]
                if (n > 0) pairs.add(Pair(k, n))
            }
            pairs.sortByDescending { pair -> pair.second }
            val parts = ArrayList<String>()
            val limit = minOf(3, pairs.size)
            for (idx in 0 until limit) {
                val k = pairs[idx].first
                val n = pairs[idx].second
                val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                parts.add("${sym}:${n}")
            }
            val staff = state.staff.getOrNull(i)
            val g = staff?.groupIdx ?: -1
            profiles.add(
                V6StaffProfile(
                    staffIndex = i,
                    name = staff?.name ?: "#${i}",
                    groupSymbol = state.groups.getOrNull(g)?.kigou ?: "",
                    workCount = work,
                    violationCount = staffViol.getOrElse(i) { 0 },
                    workloadText = parts.joinToString(" / "),
                )
            )
        }
        profiles.sortWith(compareByDescending<V6StaffProfile> { it.violationCount }.thenByDescending { it.workCount })
        return profiles
    }

    /** V6 CountApt port: sum((count - apt)^2 / apt^2), group aptitude expanded to staff. */
    private fun aptPenalty(state: MagiState, p: Problem, counts: Array<IntArray>): Double {
        var out = 0.0
        for (i in 0 until p.S) {
            val g = p.sgrp.getOrNull(i) ?: continue
            val row = state.groupShiftApt.getOrNull(g) ?: continue
            for (k in 0 until p.K) {
                val apt = row.getOrNull(k)?.trim()?.toDoubleOrNull() ?: continue
                if (apt <= 0.0) continue
                val d = counts[i][k].toDouble() - apt
                out += (d * d) / (apt * apt)
            }
        }
        return out
    }

    /** Compact V6 equalization overview: member variance + day-of-week variance with HARD-aware psi. */
    private fun equalizationPenalty(
        state: MagiState,
        p: Problem,
        schedule: Array<IntArray>,
        counts: Array<IntArray>,
        breakdown: Map<String, Int>,
    ): Double {
        if (p.S == 0 || p.T == 0 || p.K == 0 || p.G == 0) return 0.0
        val members = Array(p.G) { ArrayList<Int>() }
        for (i in 0 until p.S) {
            val g = p.sgrp[i]
            if (g in 0 until p.G) members[g].add(i)
        }

        var raw = 0.0
        for (g in 0 until p.G) {
            val gs = state.groupShift.getOrNull(g) ?: continue
            val mem = members[g]
            if (mem.isEmpty()) continue
            for (k in 0 until p.K) {
                if (gs.getOrNull(k) != 1) continue
                if (mem.size == 1) {
                    val i = mem[0]
                    val explicit = explicitTarget(p, i, k)
                    val apt = state.groupShiftApt.getOrNull(g)?.getOrNull(k)?.trim()?.toDoubleOrNull()
                    val target = explicit ?: apt ?: continue
                    val dev = counts[i][k].toDouble() - target
                    raw += dev * dev + abs(dev) * 2.0
                } else {
                    var sum = 0
                    for (i in mem) sum += counts[i][k]
                    val mean = sum.toDouble() / mem.size.toDouble()
                    var varSum = 0.0
                    var maxDev = 0.0
                    for (i in mem) {
                        val d = counts[i][k].toDouble() - mean
                        varSum += d * d
                        maxDev = max(maxDev, abs(d))
                    }
                    raw += varSum + maxDev * 2.0
                }
            }
        }

        val startDow = startDow(state.startDate)
        val dowCnt = Array(p.S) { Array(7) { IntArray(p.K) } }
        for (i in 0 until p.S) {
            for (j in 0 until p.T) {
                val k = schedule[i][j]
                if (k in 0 until p.K) dowCnt[i][(startDow + j) % 7][k]++
            }
        }
        for (g in 0 until p.G) {
            val gs = state.groupShift.getOrNull(g) ?: continue
            val mem = members[g]
            if (mem.size <= 1) continue
            for (dow in 0 until 7) {
                for (k in 0 until p.K) {
                    if (gs.getOrNull(k) != 1) continue
                    var sum = 0
                    for (i in mem) sum += dowCnt[i][dow][k]
                    val mean = sum.toDouble() / mem.size.toDouble()
                    var varSum = 0.0
                    var maxDev = 0.0
                    for (i in mem) {
                        val d = dowCnt[i][dow][k].toDouble() - mean
                        varSum += d * d
                        maxDev = max(maxDev, abs(d))
                    }
                    raw += varSum + maxDev * 2.0
                }
            }
        }
        val hard = (breakdown["groupViol"] ?: 0) + (breakdown["c3n"] ?: 0) + (breakdown["covU"] ?: 0) + (breakdown["pref"] ?: 0)
        val psi = max(0.2, 1.0 / (1.0 + 10.0 * hard.toDouble()))
        return raw * psi
    }

    private fun explicitTarget(p: Problem, i: Int, k: Int): Double? {
        val loSet = p.rangeLo[i][k] != Int.MIN_VALUE
        val hiSet = p.rangeHi[i][k] != Int.MAX_VALUE
        return when {
            loSet && hiSet -> (p.rangeLo[i][k] + p.rangeHi[i][k]).toDouble() / 2.0
            loSet -> p.rangeLo[i][k].toDouble()
            hiSet -> p.rangeHi[i][k].toDouble()
            else -> null
        }
    }

    private fun sanityWarnings(state: MagiState, p: Problem, schedule: Array<IntArray>): List<String> {
        val warns = ArrayList<String>()
        var badAssign = 0
        for (i in 0 until p.S) {
            for (j in 0 until p.T) {
                val k = schedule[i][j]
                if (k in 0 until p.K && !p.canDo(i, k)) badAssign++
            }
        }
        if (badAssign > 0) warns.add("担当不可の配置が ${badAssign} セルあります")

        var badWish = 0
        for ((key, k) in state.wishes) {
            val parts = key.split(',')
            val i = parts.getOrNull(0)?.toIntOrNull()
            val j = parts.getOrNull(1)?.toIntOrNull()
            if (i == null || j == null || i !in 0 until p.S || j !in 0 until p.T || k !in 0 until p.K) {
                badWish++
            } else if (!p.canDo(i, k)) {
                badWish++
            }
        }
        if (badWish > 0) warns.add("範囲外または担当外の希望シフトが ${badWish} 件あります")

        var badRange = 0
        for ((key, r) in state.staffRange) {
            val parts = key.split(',')
            val i = parts.getOrNull(0)?.toIntOrNull()
            val k = parts.getOrNull(1)?.toIntOrNull()
            val lo = r.lo.trim().toIntOrNull()
            val hi = r.hi.trim().toIntOrNull()
            if (i == null || k == null || i !in 0 until p.S || k !in 0 until p.K) badRange++
            if (lo != null && hi != null && lo > hi) badRange++
        }
        if (badRange > 0) warns.add("staffRange の範囲外キーまたは lo>hi が ${badRange} 件あります")

        val dup = duplicatePatternCount(state)
        if (dup > 0) warns.add("連続パターン制約の重複定義が ${dup} 件あります")
        if (state.cons41.isEmpty()) warns.add("cons41 が未設定です（グループ別人数範囲を使う場合は確認）")
        return warns
    }

    private fun sanityNotes(state: MagiState): List<String> {
        val notes = ArrayList<String>()
        var aptSet = 0
        for (row in state.groupShiftApt) for (cell in row) if (cell.trim().isNotEmpty()) aptSet++
        notes.add("groupShiftApt 適切回数: ${aptSet} 件設定")
        if (state.needDay1.isEmpty() && state.needDay2.isEmpty()) notes.add("needDay1/needDay2 は全空です（shift既定 need を使用）")
        notes.add("V6 Native overview: リスクカレンダー / 負荷プロフィール / SanityCheck 有効")
        return notes
    }

    private fun duplicatePatternCount(state: MagiState): Int {
        return countDuplicatePatternRows(state.cons3) +
            countDuplicatePatternRows(state.cons3n) +
            countDuplicatePatternRows(state.cons3m) +
            countDuplicatePatternRows(state.cons3mn)
    }

    private fun countDuplicatePatternRows(rows: List<C3Row>): Int {
        val seen = HashSet<String>()
        var dup = 0
        for (row in rows) {
            val symbols = ArrayList<String>()
            for (s in row.pattern) {
                if (s.isBlank()) break
                symbols.add(s)
            }
            val key = symbols.joinToString("→")
            if (key.isBlank()) continue
            if (!seen.add(key)) dup++
        }
        return dup
    }
}

fun dayLabel(startDate: String, offset: Int): String {
    return try {
        val d = LocalDate.parse(startDate).plusDays(offset.toLong())
        val wd = "月火水木金土日"[d.dayOfWeek.value - 1]
        "${d.monthValue}/${d.dayOfMonth}(${wd})"
    } catch (_: Exception) {
        "${offset + 1}日"
    }
}

private fun startDow(startDate: String): Int {
    return try {
        LocalDate.parse(startDate).dayOfWeek.value % 7
    } catch (_: Exception) {
        0
    }
}
