package com.magi.app.v6

import com.magi.app.toHankakuKigou
import com.magi.app.model.C3Row
import com.magi.app.model.MagiState

/**
 * Deeper native port of V6 Web diagnostics: detectImpossibleWishes(),
 * buildLoadDataBitSummary(), buildShiftCountDiagnostic(), and the practical parts
 * of buildSanityCheck().  It intentionally returns structured Kotlin data so both
 * Compose and tests can consume the same result.
 */
data class ImpossibleWish(
    val staffIndex: Int,
    val dayIndex: Int,
    val staffName: String,
    val groupSymbol: String,
    val shiftSymbol: String,
    val reason: String,
)

/** 設定ミスの種別。UI でアイコン/誘導先タブを切り替えるのに使う。 */
enum class IssueKind { WISH, CONSTRAINT, DEMAND, RANGE }

/**
 * [設定ミスの誘導修正] 制約・希望の設定間違いを「どこが・なぜ・どう直すか」で人間に提示するための構造化項目。
 * - where: 場所（例「佐藤 7/3 希望『日勤』」「連続パターン『Dﾃ→A4』」）
 * - problem: 何が問題か（平易な日本語）
 * - fix: 具体的な直し方（どの画面で何をするか）
 */
/**
 * [ワンタップ修正] カード上で画面遷移・スクロールなしに直せる安全な単一操作の種類。
 * NONE = 自動修正不可（編集画面へ誘導）。それ以外はカードのボタン1つで適用→自動再診断。
 */
enum class SettingFixAction { NONE, REMOVE_WISH, DELETE_DUP_SEQ, ZERO_RANGE_LO, CLAMP_RANGE_LO, CAP_DEMAND, CLAMP_GROUP_RANGE_LO }

data class SettingIssue(
    val kind: IssueKind,
    val where: String,
    val problem: String,
    val fix: String,
    // --- ワンタップ修正（少ないスクロール・少ないボタン操作のための直接修正情報） ---
    val action: SettingFixAction = SettingFixAction.NONE,
    val actionLabel: String = "",          // ボタン文言（空=ワンタップ不可で編集画面へ）
    val wishKey: String? = null,           // REMOVE_WISH: "i,j"
    val seqFamily: String? = null,         // DELETE_DUP_SEQ: c3 / c3n / c3m / c3mn
    val seqKey: String? = null,            // DELETE_DUP_SEQ: "Dﾃ→A4"（→区切り・非空のみ）
    val rangeKey: String? = null,          // ZERO/CLAMP_RANGE_LO: "i,k"
    val newLo: String? = null,             // ZERO/CLAMP_RANGE_LO: 新しい下限
    val demandShiftIdx: Int? = null,       // CAP_DEMAND: シフトidx
    val demandCap: Int? = null,            // CAP_DEMAND: 担当可能人数（上限）
    // CLAMP_GROUP_RANGE_LO: 群/スキル群のレンジ行。行は List なので index で指すと、診断からタップまでの間に
    //   並びが変わると別の行を壊す。DELETE_DUP_SEQ と同じく**内容一致**で指す（data class の equals）。
    val groupRangeFamily: String? = null,  // "c41" / "c41s"
    val groupRangeRow: com.magi.app.model.C41Row? = null,
)

data class ShiftCountDiagnostic(
    val staffIndex: Int,
    val staffName: String,
    val shiftSymbol: String,
    val count: Int,
    val lo: Int?,
    val hi: Int?,
    val status: String,
)

data class V6SanityReport(
    val ok: Boolean,
    val warns: List<String>,
    val notes: List<String>,
    val loadDataBitSummary: String,
    val loadDataBitDetails: List<String>,
    val shiftCountDiagnostics: List<ShiftCountDiagnostic>,
    val impossibleWishes: List<ImpossibleWish>,
    val duplicateSeqConstraints: List<String>,
    val guidance: List<SettingIssue>,
)

object V6SanityPort {
    fun build(state: MagiState, schedule: Array<IntArray> = state.schedule.toIntArray2D()): V6SanityReport {
        val p = Problem(state)
        val s = normalizeSchedule(schedule, p)
        val warns = ArrayList<String>()
        val notes = ArrayList<String>()

        val invalidAssignments = invalidAssignmentCells(state, p, s)
        if (invalidAssignments.isNotEmpty()) {
            warns.add("担当不可または範囲外の配置が ${invalidAssignments.size} セルあります")
        }

        val impossible = detectImpossibleWishes(state, p)
        if (impossible.isNotEmpty()) {
            warns.add("実現不能な希望シフトが ${impossible.size} 件あります")
        }

        val dup = findDuplicateSeqConstraints(state)
        if (dup.isNotEmpty()) warns.add("連続パターン制約の重複が ${dup.size} 件あります")

        val badRanges = badStaffRanges(state, p)
        if (badRanges > 0) warns.add("staffRange の範囲外キーまたは lo>hi が ${badRanges} 件あります")

        val impossibleDemand = impossibleDemandDays(state, p)
        if (impossibleDemand.isNotEmpty()) {
            val head = ArrayList<String>()
            val lim = minOf(4, impossibleDemand.size)
            var idx = 0
            while (idx < lim) {
                head.add(impossibleDemand[idx])
                idx++
            }
            val suffix = if (impossibleDemand.size > 4) " …" else ""
            warns.add("担当可能人数を超える需要があります: ${head.joinToString(" / ")}$suffix")
        }

        var aptSet = 0
        for (row in state.groupShiftApt) {
            for (cell in row) {
                if (cell.trim().isNotEmpty()) aptSet++
            }
        }
        notes.add("groupShiftApt 適切回数: ${aptSet} 件")
        notes.add("shifts=${p.K} groups=${p.G} staff=${p.S} days=${p.T}")
        if (state.use2Patterns) notes.add("2世代需要(セル毎OR/AND: #4b)が有効") else notes.add("需要はP1のみ")

        return V6SanityReport(
            ok = warns.isEmpty(),
            warns = warns,
            notes = notes,
            loadDataBitSummary = buildLoadDataBitSummary(state, p, s),
            loadDataBitDetails = buildLoadDataBitDetails(state, p),
            shiftCountDiagnostics = buildShiftCountDiagnostic(state, p, s),
            impossibleWishes = impossible,
            duplicateSeqConstraints = dup,
            guidance = buildGuidance(state, p),
        )
    }

    fun detectImpossibleWishes(state: MagiState, p: Problem = Problem(state)): List<ImpossibleWish> {
        val out = ArrayList<ImpossibleWish>()
        for ((key, k) in state.wishes) {
            val parts = key.split(',')
            val i = parts.getOrNull(0)?.toIntOrNull()
            val j = parts.getOrNull(1)?.toIntOrNull()
            val reason = when {
                i == null || j == null -> "希望キーが i,j 形式ではありません"
                i !in 0 until p.S || j !in 0 until p.T -> "職員または日付が範囲外です"
                k !in 0 until p.K -> "希望シフトが範囲外です"
                !p.canDo(i, k) -> "職員のグループでは担当不可です"
                else -> null
            }
            if (reason != null) {
                val si = i?.takeIf { it in 0 until p.S } ?: -1
                val gi = si.takeIf { it >= 0 }?.let { p.sgrp[it] } ?: -1
                out.add(
                    ImpossibleWish(
                        staffIndex = si,
                        dayIndex = j ?: -1,
                        staffName = state.staff.getOrNull(si)?.name ?: "#$si",
                        groupSymbol = state.groups.getOrNull(gi)?.kigou?.let { toHankakuKigou(it) } ?: "?",
                        shiftSymbol = state.shifts.getOrNull(k)?.kigou?.let { toHankakuKigou(it) } ?: k.toString(),
                        reason = reason,
                    )
                )
            }
        }
        return out.sortedWith(compareBy<ImpossibleWish> { it.staffIndex }.thenBy { it.dayIndex })
    }

    /** シフト単位の「証明可能に解消不能な covU 不足」。担当可能人数 capable(k) を全員そのシフトへ
     *  就けても残る不足（= covUCell(k,j,capable) の総和）。covUCell は got 単調減少なので、これは当該セルの
     *  covU 最小値＝どう割り当てても避けられない不足量。need1/need2 両設定時は covUCell が MIN(OR救済) を返す
     *  ため過大検出しない。誤検知ゼロ・読み取り専用・データ不変。 */
    data class ForcedCovU(val shiftIndex: Int, val shiftSymbol: String, val cells: Int, val amount: Int)

    fun forcedCovU(state: MagiState, p: Problem = Problem(state)): List<ForcedCovU> {
        val out = ArrayList<ForcedCovU>()
        for (k in 0 until p.K) {
            val capable = (0 until p.S).count { i -> p.canDo(i, k) }
            var cells = 0; var amount = 0
            for (j in 0 until p.T) {
                val u = p.covUCell(k, j, capable)
                if (u > 0) { cells++; amount += u }
            }
            if (amount > 0) {
                val sym = state.shifts.getOrNull(k)?.kigou?.let { toHankakuKigou(it) } ?: k.toString()
                out.add(ForcedCovU(k, sym, cells, amount))
            }
        }
        return out
    }

    /** データ起因で証明可能に解消不能な HARD 違反の下限（report.hard と同単位＝covU 不足量の総和）。
     *  ・covU: forcedCovU の総量。有資格者を全員そのシフトに就けても埋まらない席＝どう探索しても消えない HARD。
     *  ・実現不能希望(pref): 監査#11② で HARD 寄与0（対称除外）のため下限に含めない。
     *  ・群外配置(groupViol): 探索は canDo ガードで群外を置かない＋不可能希望は gate 済＝構造下限では常時0。
     *  構造(assignability/need)のみ依存で最適化中に変化しないため一度だけ算出してよい。 */
    fun structuralHardFloor(state: MagiState, p: Problem = Problem(state)): Int =
        forcedCovU(state, p).sumOf { it.amount }

    /**
     * [3.354.0/6b・6c 共通] 職員 i の、シフト k 以外の担当可能シフトの個人上限の合計（上限未設定は期間日数
     * で丸める）。`p.T - この値` が「他シフトの上限を守る限り k に必ず回ってくる日数」の下界になる。
     * 合計が期間日数に達した時点で下界は 0 以下＝発火しないので早期に打ち切る。
     */
    internal fun otherShiftCapSum(p: Problem, i: Int, k: Int): Int {
        var sum = 0
        for (k2 in 0 until p.K) {
            if (k2 == k || !p.canDo(i, k2)) continue
            val hi = p.rangeHi[i][k2]
            sum += if (hi == Int.MAX_VALUE) p.T else minOf(maxOf(hi, 0), p.T)
            if (sum >= p.T) return sum
        }
        return sum
    }

    /**
     * [3.354.0] 個人の担当構成から強制される **(apt + high) の合計下限**。
     *
     * 個人上限(rangeHi)は SOFT なので「必ず k に forcedMin 回入る」とは言えない（実機ログで確認: 6b が
     * 「B4 は最低20回」と言う職員が、休の上限を1日超過して B4=19 に着地していた）。ただし上限を d 日ぶん
     * 破れば count は forcedMin−d まで下がる代わりに high が d 増えるので、**両者の和** は
     * `forcedMin − 目標` を下回れない。よってこの値は apt+high の真の下限になる。
     *
     * 同じ職員の複数シフトで下界が立つ場合は上限超過ぶん(d)が共有されうるため、**職員ごとに最大値だけ**を
     * 取って合計する（保守的＝過大に見積もらない）。読み取り専用・スコア不変。
     */
    fun structuralPersonalFloor(p: Problem): Int {
        var floor = 0
        for (i in 0 until p.S) {
            var best = 0
            for (k in 0 until p.K) {
                val t = p.apt[i][k]
                if (t < 0 || !p.canDo(i, k)) continue
                val d = (p.T - otherShiftCapSum(p, i, k)) - t
                if (d > best) best = d
            }
            floor += best
        }
        return floor
    }

    /**
     * [設定ミスの誘導修正] 制約・希望の設定間違いを、人間が直せる粒度（誰の/何日の/どのシフト/どの制約、
     * そして具体的な直し方）で列挙する。検出済みの構造化データを平易な日本語の指示文に変換するだけで、
     * 重み・データは一切変更しない（読み取り専用＝安全）。表示順は「直すべき度合い」が高い順。
     */
    /**
     * [適切回数の検算・単一ソース] シフトごとの「適切回数(apt)の合計」と「それを受け止められる上限」を返す。
     *
     * 盤面を一切参照しない（need / apt / staffRange だけで決まる）ため、勤務表を作る**前**でも計算できる。
     * これが [buildGuidance] の検査6-C の実体で、設定画面（目標カード）もこの同じ関数を読む。
     * 二重実装にすると「診断は警告を出すのに設定画面は何も言わない」というズレが生まれるため、
     * 判定は必ずここへ集約する。
     *
     * - 通常シフト: capacity = 必要数(上限)の合計 seatsHi。目標の合計がこれを超えると、
     *   全員の目標は同時に満たせない（目標割れ aptLow か過剰配置 covO/aptHigh が必ず出る）。
     * - 休(restIdx): 「1日に何人休んでよいか」という座席上限を持たないため、
     *   capacity = 各職員が他シフトの個人下限を満たしたうえで最大何日休めるかの合計。
     *
     * [3.301.0 で直った挙動] 旧実装は検査6-C を「必要人数が1日でも設定されているシフト」のループ内に
     * 置いていたため、**休に必要人数を設定しない通常運用では休の判定が一度も実行されなかった**
     * （golden_state で実測: 休の必要人数設定日=0）。3.235.0 で入れた休向け restCapacity 比較が
     * そのゲートに阻まれて死んでいた潜在バグ。休は必要人数を参照しないので、ここではゲートの外に出す。
     * 通常シフトのゲート（必要人数が1日も無ければ対象外）は維持する：勤務シフトの必要人数未設定は
     * 「まだ設定していない」だけの可能性が高く、上限0とみなすと誤検知になる（3.235.0 の指摘と同型）。
     * なお休の capacity は休自身の個人上限(rangeHi)を見ていない＝実際より大きく見積もる。
     * 検出漏れ側に倒れるだけで誤検知は増えない。
     */
    data class AptBalance(
        val shiftIdx: Int,
        val kigou: String,
        /** 適切回数の合計（担当可能な職員ぶん・staffRange クランプ後の実効値）。 */
        val aptSum: Int,
        /** それを受け止められる上限（通常＝必要数の合計 / 休＝最大可能日数の合計）。 */
        val capacity: Int,
        val isRest: Boolean,
    ) {
        /** 目標の合計が上限を超えている＝何をしても目標割れか過剰配置が出る。 */
        val overloaded: Boolean get() = aptSum > capacity
        /** 何回ぶん届かないか。 */
        val shortfall: Int get() = (aptSum - capacity).coerceAtLeast(0)
    }

    /**
     * [適切回数の検算] 目標が設定されているシフトについて [AptBalance] を返す（盤面不要）。
     *
     * [p] は既定で state のキャッシュを使うが、[buildGuidance] のように呼び出し元が既に Problem を
     * 持っている場合はそれを渡す（同じ関数が別の Problem を見て診断と設定画面がズレるのを防ぐ）。
     */
    /**
     * 休(rest)の実質的な上限＝各職員が「他の担当シフトの個人下限」を満たしたうえで最大何日休めるか、の合計。
     *
     * 休は「1日に何人休んでよいか」という座席（必要人数）の概念を持たないため、必要人数の合計と比べても
     * 意味がない。他シフトの下限が未設定なら minOther=0＝ほぼ T 日休める計算になり、誤検知を避ける側へ
     * 保守的に丸まる。[3.235.0] で適切回数の検査へ導入したものを [3.316.0] で下限合計の検査とも共有する。
     */
    internal fun restCapacity(p: Problem): Int {
        val k = p.restIdx
        var cap = 0
        for (i in 0 until p.S) {
            if (!p.canDo(i, k)) continue
            var minOther = 0
            for (k2 in 0 until p.K) {
                if (k2 == k || !p.canDo(i, k2)) continue
                val lo2 = p.rangeLo[i][k2]
                if (lo2 != Int.MIN_VALUE && lo2 > 0) minOther += lo2
            }
            cap += maxOf(0, p.T - minOther)
        }
        return cap
    }

    fun aptBalances(state: MagiState, p: Problem = cachedProblem(state)): List<AptBalance> {
        val out = ArrayList<AptBalance>()
        for (k in 0 until p.K) {
            var aptSum = 0
            var anyApt = false
            for (i in 0 until p.S) {
                if (!p.canDo(i, k)) continue
                val a = p.apt[i][k]
                if (a >= 0) { aptSum += a; anyApt = true }
            }
            if (!anyApt) continue   // 目標が1つも設定されていないシフトは検算対象外
            val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
            if (k == p.restIdx) {
                out.add(AptBalance(k, sym, aptSum, restCapacity(p), isRest = true))
            } else {
                var seatsHi = 0
                var hasDemand = false
                for (j in 0 until p.T) {
                    val n1 = p.need1[k][j]
                    if (n1 < 0) continue
                    hasDemand = true
                    val hi = if (p.use2 && p.need2[k][j] >= 0) p.need2[k][j] else n1
                    seatsHi += maxOf(hi, 0)
                }
                if (!hasDemand) continue   // 必要人数が1日も設定されていない＝比較対象がない
                out.add(AptBalance(k, sym, aptSum, seatsHi, isRest = false))
            }
        }
        return out
    }

    /**
     * 下限>上限 なら (下限, 上限) を返す。**入力ダイアログの阻止と、この事後診断が同じ判定を使う**ための単一ソース。
     * 片方だけ緩いと「画面は通すのに、あとから直せと言われる」入力が生まれる（3.403.0）。
     * 両方が数値のときだけ矛盾と見なす＝空欄（未設定）や数値でない値は 2h/2f の別の検査が扱う。
     */
    fun rangeOrderConflict(lo: String?, hi: String?): Pair<Int, Int>? {
        val l = lo?.trim()?.toIntOrNull() ?: return null
        val h = hi?.trim()?.toIntOrNull() ?: return null
        return if (l > h) l to h else null
    }

    fun buildGuidance(state: MagiState, p: Problem = Problem(state)): List<SettingIssue> {
        val out = ArrayList<SettingIssue>()

        // 1) 希望シフトの設定ミス（担当外・範囲外など）
        for (w in detectImpossibleWishes(state, p)) {
            val where = "${w.staffName} ${safeDayLabel(state.startDate, w.dayIndex)} 希望「${w.shiftSymbol}」"
            val fix = when {
                w.reason.contains("担当不可") ->
                    "この希望を取り消すか、設定で${w.staffName}さんの担当に「${w.shiftSymbol}」を追加してください"
                w.reason.contains("範囲外") ->
                    "希望のシフト記号・日付が勤務表の範囲内かを確認してください"
                else -> "希望の入力（i,j形式）を確認してください"
            }
            val canOneTap = w.reason.contains("担当不可")
            out.add(SettingIssue(IssueKind.WISH, where, "実現できない希望です（${w.reason}）", fix,
                action = if (canOneTap) SettingFixAction.REMOVE_WISH else SettingFixAction.NONE,
                actionLabel = if (canOneTap) "この希望を取消" else "",
                wishKey = if (canOneTap) "${w.staffIndex},${w.dayIndex}" else null))
        }

        // 2) 連続パターン制約の重複（例: c3n:Dﾃ→A4）
        for (d in findDuplicateSeqConstraints(state)) {
            val famRaw = d.substringBefore(':')
            val fam = c3FamilyJp(famRaw)
            val seq = d.substringAfter(':')
            out.add(SettingIssue(IssueKind.CONSTRAINT, "連続パターン「$seq」($fam)",
                "同じパターンが2重に登録されています", "連続パターン設定で「$seq」の重複行を1つ削除してください",
                action = SettingFixAction.DELETE_DUP_SEQ, actionLabel = "重複を1つ削除",
                seqFamily = famRaw, seqKey = seq))
        }

        // 2b) [監査#8 / Web HF557 A4 の native 移植] 連勤・回数窓制約(cons1)の不能設定
        //   d1>期間: 窓が期間を超え、判定が一度も走らず無言で無効。 d2>d1: 物理的に不可能で全員・全窓が発火し続ける。
        for (c in p.cons1) {
            val sym = state.shifts.getOrNull(c.shiftIdx)?.kigou ?: c.shiftIdx.toString()
            if (c.day1 > p.T) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "連勤/休制約「$sym ${c.day1}日で${c.day2}回以上」",
                    "窓${c.day1}日が期間${p.T}日を超えるため、この制約は一度も判定されません（無言で無効です）",
                    "制約設定（連勤・回数）で日数を期間${p.T}日以下に直すか、この行を削除してください"))
            } else if (c.day2 > c.day1) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "連勤/休制約「$sym ${c.day1}日で${c.day2}回以上」",
                    "${c.day1}日の窓に${c.day2}回は物理的に不可能で、全員・全期間が違反になり続けます",
                    "制約設定（連勤・回数）で回数を${c.day1}回以下に直すか、この行を削除してください"))
            }
        }

        // 2b-2) [壁/covO-tension 分類] c1 窓制約の充足可否。需要 = 各 canDo 職員 × day2 × floor(T/day1)（disjoint窓の下界）。
        //   [3.364.0 訂正・実データ計測起因] 非休シフトの供給に per-day 上限(need2/need1)の総和を使うのは誤り。
        //   need2/need1 は covO の SOFT 目標(1日あたりの過剰配置しきい値=重み1)であって物理上限ではなく、最適化は covO を
        //   払って上限を超えて配置できる。かつ day2<=day1 ガードより「物理供給(担当nCanDo人×T日) >= 需要」が**常に成立**する
        //   ので、非休の c1 窓は原理的に構造的不能にはならない（旧実装は golden の Dﾃ を「構造的に残る」と誤断定していたが、
        //   実データの手作り表は Dﾃ を上限超えの35回配置しており供給31は実上限でないことを実測で確認）。
        //   → 休のみ「S*T−Σ最小work需要」が実在の物理上限＝供給<需要なら真の壁。非休は上限が窓ルールに届かない場合のみ、
        //     c1 充足に過剰配置(covO)が要る旨を「解消不能ではないトレードオフ」として正直に案内する。read-only・スコア不変。
        run {
            var workMinDemand = 0
            for (k in 0 until p.K) for (j in 0 until p.T) workMinDemand += p.need1[k][j].coerceAtLeast(0)
            for (c in p.cons1) {
                val si = c.shiftIdx
                // 退化ケース(窓>期間 / 回数>窓)は 2b が別途案内。ここは通常窓のみ。
                if (c.day1 <= 0 || c.day2 <= 0 || c.day1 > p.T || c.day2 > c.day1) continue
                val disjoint = p.T / c.day1
                if (disjoint <= 0) continue
                val nCanDo = (0 until p.S).count { p.canDo(it, si) }
                if (nCanDo == 0) continue   // 担当者ゼロは別の案内対象
                val demand = nCanDo * c.day2 * disjoint
                val sym = state.shifts.getOrNull(si)?.kigou ?: si.toString()
                if (si == p.restIdx) {
                    // 休は「作業に回さないセル数(S*T−最小work需要)」が実在の物理上限＝供給<需要なら真の構造的不能。
                    val supply = p.S * p.T - workMinDemand
                    if (supply < demand) {
                        out.add(SettingIssue(IssueKind.CONSTRAINT, "窓ルール「$sym を${c.day1}日で${c.day2}回以上」",
                            "「$sym」の供給${supply}に対し必要${demand}(=担当${nCanDo}人×${c.day2}回×${disjoint}窓)で${demand - supply} 不足。" +
                                "どう組んでもこの窓違反(c1)は構造的に残ります（最適化では消せません）。",
                            "作業シフトの最低人数を下げて「$sym」に回せる余地を増やすか、窓ルールの回数を下げる／日数を延ばす(制約設定)。"))
                    }
                } else {
                    // 非休は物理供給(担当nCanDo人×T日)>=需要が常に成立＝壁ではない。per-day 上限(need2/need1)の総和が
                    //   窓ルールに届かない場合のみ、c1 充足に過剰配置(covO)が要る旨をトレードオフとして案内。
                    var capSum = 0
                    for (j in 0 until p.T) {
                        val h = if (p.use2 && p.need2[si][j] >= 0) p.need2[si][j] else p.need1[si][j]
                        capSum += h.coerceAtLeast(0)
                    }
                    if (capSum < demand) {
                        val short = demand - capSum
                        out.add(SettingIssue(IssueKind.CONSTRAINT, "窓ルール「$sym を${c.day1}日で${c.day2}回以上」",
                            "「$sym」の1日あたり上限の合計(${capSum})が窓ルールの必要回数(${demand})に${short}回ぶん届かず、" +
                                "c1 を満たすには一部の日で上限を超える配置(過剰配置)が要ります。構造的に不能ではなく、最適化は過剰配置を少し払って解消できます。",
                            "「$sym」の1日あたり上限を上げるか、${short}回ぶんの過剰配置を許容してください。"))
                    }
                }
            }
        }

        // 2b-3) [壁/ダイヤル分類・個人版/ドッグフーディングで発見、3.262.0で厳密化] 2b-2は全体供給(集計)
        //   のみ判定するため、「集計では担当者が大勢いて足りているのに、特定の1人だけは自分の個人上限
        //   (staffRange上限)のせいで自分自身の窓ルールを満たせない」局面を見逃していた（例: Aｱ担当可能者
        //   は全体で10人いても、ある1人だけAｱ個人上限が低く「14日窓でAｱ≥1」を自分では満たせない）。
        //   [3.262.0] 旧実装は2b-2と同じ非重複窓の粗い下界(day2×floor(T/day1))を使っていたが、これは
        //   スライド窓の真の必要量を過小評価する（実データ検証: 「15日窓4回以上」の粗い下界=8だが、
        //   実際に0違反へ到達するには9〜11日必要な職員が複数おり、粗い下界では「上限8/9で足りている」
        //   と誤って見逃していた＝false negative）。`SmartInitialScheduler.minDaysForFullCompliance`
        //   （構築本体の`solveConstructionDp`を無制限capで呼び、0違反を達成する最小日数を求める）へ
        //   置換し、同一シフトの複数規則(例: 休の5日窓＋15日窓)も**同時充足**の真の必要量として厳密判定。
        run {
            val rulesByShift = LinkedHashMap<Int, MutableList<C1>>()
            for (c in p.cons1) {
                if (c.shiftIdx !in 0 until p.K || c.day1 <= 0 || c.day2 <= 0 || c.day1 > p.T || c.day2 > c.day1) continue
                rulesByShift.getOrPut(c.shiftIdx) { ArrayList() }.add(c)
            }
            for ((shiftIdx, rules) in rulesByShift) {
                // [3.272.0] ConstraintMus.cachedMinDays（同じ純関数のプロセス全域キャッシュ）経由に統一。
                //   buildGuidance はセル編集ごとに走るため、重いDP（15日窓で数百ms）を毎回払わない。
                val minNeeded = ConstraintMus.cachedMinDays(
                    p.T, rules.map { it.day1 to it.day2 },
                ) ?: continue
                val sym = state.shifts.getOrNull(shiftIdx)?.kigou ?: shiftIdx.toString()
                val ruleDesc = rules.joinToString(" かつ ") { "${it.day1}日で${it.day2}回以上" }
                for (i in 0 until p.S) {
                    if (!p.canDo(i, shiftIdx)) continue
                    val hi = p.rangeHi[i][shiftIdx]
                    if (hi == Int.MAX_VALUE || hi >= minNeeded) continue
                    val name = state.staff.getOrNull(i)?.name ?: "#$i"
                    out.add(SettingIssue(IssueKind.RANGE, "${name}さんの「$sym」個人上限と窓ルールの衝突",
                        "窓ルール「$sym を$ruleDesc」を同時に満たすには最低${minNeeded}回が必要ですが、" +
                            "${name}さんの「$sym」個人上限は${hi}回です。この人だけではどう配置しても窓ルールを満たせません",
                        "${name}さんの「$sym」個人上限を${minNeeded}回以上に上げるか、窓ルールの回数を下げてください"))
                }
            }
        }

        // 2c) [監査#5] 担当可能者ゼロの回数制約(cons2) — canDoガード後は事実上無効になるため案内する。
        for (c in p.cons2) {
            val eligible = (0 until p.S).count { p.canDo(it, c.shiftIdx) }
            if (eligible == 0) {
                val sym = state.shifts.getOrNull(c.shiftIdx)?.kigou ?: c.shiftIdx.toString()
                out.add(SettingIssue(IssueKind.CONSTRAINT, "回数制約「$sym を${c.count}回以上」",
                    "このシフトを担当できる職員がいないため、この制約は事実上無効です",
                    "担当設定（グループ×シフト）で担当者を追加するか、この行を削除してください"))
            }
        }

        // 2d) [監査#9] 期間より長い連続パターン — パース段階で除外済み（Problem.c3OverT）。理由を案内する。
        for ((fam, seqStr) in p.c3OverT) {
            val famJp = c3FamilyJp(fam)
            val negative = fam == "c3n" || fam == "c3mn"
            out.add(SettingIssue(IssueKind.CONSTRAINT, "連続パターン「$seqStr」($famJp)",
                if (negative) "パターン長が期間${p.T}日を超えるため期間内に発生し得ず、この制約は無効です"
                else "パターン長が期間${p.T}日を超えるため物理的に充足できず、この制約は無効です",
                "連続パターン設定でパターンを${p.T}日以下に短縮するか、この行を削除してください"))
        }

        // 2e) [3.309.0] 存在しないシフト記号を含む連続パターン — パース段階で無言除外されていた。
        //   シフトの改名・削除でこうなる。禁止(c3n)なら HARD 制約が黙って無効化されるため必ず案内する。
        for ((fam, seqStr) in p.c3UnknownShift) {
            val famJp = c3FamilyJp(fam)
            out.add(SettingIssue(IssueKind.CONSTRAINT, "連続パターン「$seqStr」($famJp)",
                "〈〉で囲んだ記号が今のシフト一覧にないため、この行は評価されていません" +
                    "（シフトを改名・削除するとこうなります）",
                "連続パターン設定でこの行の記号を今あるシフトに直すか、行を削除してください"))
        }

        // 2f) [3.320.0] 3.309.0 は連続パターンだけを直したが、**同じ無言除外が残り6族にもあった**
        //   （窓の要件・個人の合計・群/スキル群のレンジ・群/スキル群ペア禁止）。記号が今の一覧に無い、
        //   または日数・回数が空/非数値の行は `Problem` のパース段階で捨てられ、画面にもログにも
        //   出ないまま評価対象から消えていた。窓の要件は重み15、群ペア禁止は実データでも発火する族。
        for ((famJp, rowStr) in p.unresolvedRows) {
            out.add(SettingIssue(IssueKind.CONSTRAINT, "$famJp「$rowStr」",
                "この行は評価されていません。〈〉で囲んだ記号が今の一覧にないか、日数・回数が空か数値でない" +
                    "ためです（シフトや群を改名・削除するとこうなります）",
                "制約設定でこの行を今ある記号・正しい数値に直すか、行を削除してください"))
        }

        // 2g) [3.320.0] 「休」記号のシフトが無い＝先頭シフトが黙って休として扱われる。
        //   `restShiftIndex` は記号"休"が見つからなければ `?: 0` を返す。これは 3.103.0 で -1 に
        //   すると全シフトが勤務扱いになる別のバグを避けた**意図的な**フォールバックだが、
        //   曜日平準化(weekly)の「勤務日か休か」・診断の休関連の判定が先頭シフトを休とみなすため、
        //   入力が黙って別の意味になる。データ側で直せるので明示的に案内する。
        if (state.shifts.none { it.kigou == "休" }) {
            val head = state.shifts.firstOrNull()?.kigou ?: "(シフト未登録)"
            out.add(SettingIssue(IssueKind.CONSTRAINT, "「休」のシフトがありません",
                "記号が「休」のシフトが無いため、先頭の「$head」を休として扱っています" +
                    "（曜日の偏りや休み関連の診断がこの前提で動きます）",
                "シフト設定で休みのシフトの記号を「休」にしてください"))
        }

        // 2j) [3.349.0/ユーザー確認「最大期間一ヶ月です」] 業務前提は **職員30名以内・期間1か月(31日)以内**。
        //   これまでこの前提は文書（CLAUDE.md）にしかなく、**コードはどこでも強制も確認もしていなかった**
        //   （`dayCount` は盤面の列数から導出するだけ）。前提を超えた入力でも実行はできるが、
        //   64日を境に `C3nBitScan`（c3n のビット走査）と C++ `SaChunk` の bitmask 経路が
        //   スカラー退避へ落ち、探索が目に見えて遅くなる。**止めない**（実行できるものを止めない）。
        //   知らせるだけ＝read-only・スコアリング不変。
        if (state.dayCount > 31) {
            val slow = if (state.dayCount > 64) "。64日を超えるとビット演算の高速経路が使えず探索が遅くなります" else ""
            out.add(SettingIssue(IssueKind.DEMAND, "対象期間が1か月を超えています",
                "対象期間が${state.dayCount}日あります。想定は1か月（31日）以内です$slow",
                "基本情報で開始日・終了日を1か月以内にするか、月ごとに分けて作成してください"))
        }
        if (state.staffCount > 30) {
            out.add(SettingIssue(IssueKind.DEMAND, "職員数が想定を超えています",
                "職員が${state.staffCount}名います。想定は30名以内です",
                "職員を分けて作成するか、この規模で使う場合は計算時間が延びることを見込んでください"))
        }

        // 2h) [3.327.0/外部レビュー High4] **捨てられずに fail-open で解釈される**数値。
        //   2f が拾うのは `Problem` がパースに失敗して行ごと捨てたものだけ。一方
        //   `staffRange` の lo/hi・`cons41(s)` の l/u・シフトの必要人数は、非数値でも行は生き残り
        //   **空欄と同じ扱い**になる（staffRange は `toIntOrNull()?.let{}` を素通り＝未設定センチネル
        //   `Int.MIN_VALUE`/`MAX_VALUE` のまま、必要人数は `?: -1`＝要件なし）。
        //   **空欄＝未設定は正しい仕様**なので対象にせず、
        //   「空でないのに数値でない」ものだけを出す（弱い問題を解いて成功扱いになるのを防ぐ）。
        fun badNum(v: String): Boolean = v.isNotBlank() && v.trim().toIntOrNull() == null
        for ((key, r) in state.staffRange) {
            if (!badNum(r.lo) && !badNum(r.hi)) continue
            val idx = key.split(",")
            val nm = idx.getOrNull(0)?.toIntOrNull()?.let { state.staff.getOrNull(it)?.name } ?: key
            val sy = idx.getOrNull(1)?.toIntOrNull()?.let { state.shifts.getOrNull(it)?.kigou } ?: ""
            out.add(SettingIssue(IssueKind.CONSTRAINT, "個人の回数「$nm $sy」",
                "下限「${r.lo}」上限「${r.hi}」に数値でない値があります。その側は**制限なし**として" +
                    "扱われるため、意図より弱い条件で計算されます",
                "個人の回数で数値を入れ直すか、制限しないなら空欄にしてください"))
        }
        for (sh in state.shifts) {
            if (!badNum(sh.need1) && !badNum(sh.need2)) continue
            out.add(SettingIssue(IssueKind.CONSTRAINT, "必要人数「${sh.kigou}」",
                "最低人数「${sh.need1}」上限人数「${sh.need2}」に数値でない値があります。その側は" +
                    "**未設定（要件なし）**として扱われます",
                "必要人数で数値を入れ直すか、設定しないなら空欄にしてください"))
        }
        fun checkRange(famJp: String, fam: String, rows: List<com.magi.app.model.C41Row>) {
            for (c in rows) {
                if (badNum(c.l) || badNum(c.u)) {
                    out.add(SettingIssue(IssueKind.CONSTRAINT, "$famJp「${c.groupKigou} ${c.shiftKigou}」",
                        "下限「${c.l}」上限「${c.u}」に数値でない値があります。その側は**制限なし**として" +
                            "扱われるため、意図より弱い条件で計算されます",
                        "制約設定で数値を入れ直すか、制限しないなら空欄にしてください"))
                    continue
                }
                // [3.399.0] 下限>上限。engine は `z < l || z > u` で判定するため、l>u だと**どの人数でも
                //   必ずどちらかが真**＝その群×シフトは**期間の全日が違反**になり、しかも何をしても消えない。
                //   個人の回数(staffRange)は同じ矛盾を既に検出してワンタップ修正まで出しているのに、
                //   群/スキル群のレンジだけ取り残されていた（3.327.0 の 2h は「数値でない」しか見ていない）。
                rangeOrderConflict(c.l, c.u)?.let { (lo, hi) ->
                    out.add(SettingIssue(IssueKind.CONSTRAINT, "$famJp「${c.groupKigou} ${c.shiftKigou}」",
                        "下限$lo > 上限$hi で矛盾しています。この組み合わせは期間の全日が違反になり、" +
                            "勤務表をどう組んでも消えません",
                        "制約設定で下限≤上限に直してください",
                        action = SettingFixAction.CLAMP_GROUP_RANGE_LO,
                        actionLabel = "下限を${hi}に下げる",
                        newLo = hi.toString(), groupRangeFamily = fam, groupRangeRow = c))
                }
            }
        }
        checkRange("群のレンジ", "c41", state.cons41)
        checkRange("スキル群のレンジ", "c41s", state.cons41s)
        // [3.328.0/外部レビュー] 日別の必要人数と適切回数も同じ穴。とくに needDay は
        //   `needAt` が非数値のとき**シフト既定値へ黙って読み替える**ので、0 になるより性質が悪い
        //   （その日だけ意図と違う人数で計算され、画面には何も出ない）。
        for ((map, jp) in listOf(state.needDay1 to "最低人数", state.needDay2 to "上限人数")) {
            val bad = map.entries.filter { badNum(it.value) }.sortedBy { it.key }
            if (bad.isEmpty()) continue
            val where = bad.take(3).joinToString("・") { e ->
                val kj = e.key.split(",")
                val sy = kj.getOrNull(0)?.toIntOrNull()?.let { state.shifts.getOrNull(it)?.kigou } ?: e.key
                val d = kj.getOrNull(1)?.toIntOrNull()?.let { safeDayLabel(state.startDate, it) } ?: ""
                "$sy $d「${e.value}」"
            }
            out.add(SettingIssue(IssueKind.CONSTRAINT, "日別の$jp",
                "${bad.size}件（$where${if (bad.size > 3) " ほか" else ""}）が数値ではありません。" +
                    "その日は**シフトの既定値**で計算されます（例外を設定したつもりでも効きません）",
                "日別の必要人数で数値を入れ直すか、例外にしないなら削除してください"))
        }
        run {
            val bad = ArrayList<String>()
            for ((g, row) in state.groupShiftApt.withIndex()) for ((k, v) in row.withIndex()) {
                if (!badNum(v)) continue
                val gk = state.groups.getOrNull(g)?.kigou ?: "#$g"
                val sk = state.shifts.getOrNull(k)?.kigou ?: "#$k"
                bad.add("$gk $sk「$v」")
            }
            if (bad.isNotEmpty()) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "適切回数（1人あたりの目標）",
                    "${bad.size}件（${bad.take(3).joinToString("・")}${if (bad.size > 3) " ほか" else ""}）が" +
                        "数値ではありません。**目標なし**として扱われます",
                    "回数設定で数値を入れ直すか、目標にしないなら空欄にしてください"))
            }
        }

        // 2i) [3.327.0/外部レビュー High5] スキル群の割当が範囲外。
        //   `Staff.skillIdx` の既定は 0 で、`Problem` は素通しする（native は 3.311.0 で巨大確保だけ
        //   防いでいるが、意味論は検証していない）。範囲外だと `ssk[i]==groupIdx` が常に偽＝その職員が
        //   スキル群の制約から**静かに外れる**。さらに旧いデータは未指定が 0 なので、あとからスキル群を
        //   作ると全員が先頭の群に所属したことになる。自動で書き換えると意味が変わるので**知らせるだけ**にする。
        if (state.skillGroups.isNotEmpty()) {
            val bad = state.staff.withIndex().filter { (_, st2) ->
                st2.skillIdx != -1 && st2.skillIdx !in state.skillGroups.indices
            }
            if (bad.isNotEmpty()) {
                val names = bad.take(4).joinToString("・") { it.value.name.ifBlank { "#${it.index}" } }
                out.add(SettingIssue(IssueKind.CONSTRAINT, "スキル群の割当",
                    "${bad.size}名（$names${if (bad.size > 4) " ほか" else ""}）のスキル群が今の一覧の範囲外です。" +
                        "この職員はスキル群の制約から外れて計算されます",
                    "職員管理でスキル群を選び直すか、所属させないなら「(なし)」にしてください"))
            }
        }

        // 3) 需要 > 担当可能人数（その枠は誰をどう並べても必ず不足）
        for (j in 0 until p.T) for (k in 0 until p.K) {
            val need = p.need1[k][j]
            if (need <= 0) continue
            var capable = 0
            for (i in 0 until p.S) if (p.canDo(i, k)) capable++
            if (need > capable) {
                val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                out.add(SettingIssue(IssueKind.DEMAND, "${safeDayLabel(state.startDate, j)} $sym",
                    "必要${need}人ですが担当できるのは${capable}人だけです",
                    "担当できる職員を増やすか、必要人数を${capable}人以下に下げてください",
                    action = SettingFixAction.CAP_DEMAND, actionLabel = "必要数を${capable}人に下げる",
                    demandShiftIdx = k, demandCap = capable))
            }
        }

        // 4) 回数レンジ(staffRange)の設定ミス
        for ((key, r) in state.staffRange) {
            val parts = key.split(',')
            val i = parts.getOrNull(0)?.toIntOrNull()
            val k = parts.getOrNull(1)?.toIntOrNull()
            val lo = r.lo.trim().toIntOrNull()
            val name = i?.let { state.staff.getOrNull(it)?.name } ?: "#$i"
            val sym = k?.let { state.shifts.getOrNull(it)?.kigou } ?: "$k"
            if (i == null || k == null || i !in 0 until p.S || k !in 0 until p.K) {
                out.add(SettingIssue(IssueKind.RANGE, "回数設定 $key", "対象職員/シフトが範囲外です", "設定で正しい職員・シフトに付け直してください"))
                continue
            }
            rangeOrderConflict(r.lo, r.hi)?.let { (cLo, cHi) ->
                out.add(SettingIssue(IssueKind.RANGE, "$name の「$sym」回数", "下限$cLo > 上限$cHi で矛盾しています", "設定で下限≤上限に直してください",
                    action = SettingFixAction.CLAMP_RANGE_LO, actionLabel = "下限を${cHi}に下げる", rangeKey = key, newLo = cHi.toString()))
            }
            if (lo != null && lo > 0 && !p.canDo(i, k)) {
                out.add(SettingIssue(IssueKind.RANGE, "$name の「$sym」回数", "担当できないシフトに下限${lo}が設定されています", "下限を0にするか、${name}さんの担当に「$sym」を追加してください",
                    action = SettingFixAction.ZERO_RANGE_LO, actionLabel = "下限を0にする", rangeKey = key, newLo = "0"))
            }
            if (lo != null && lo > p.T) {
                out.add(SettingIssue(IssueKind.RANGE, "$name の「$sym」回数", "下限${lo}が期間日数(${p.T}日)を超えています", "下限を${p.T}以下に直してください",
                    action = SettingFixAction.CLAMP_RANGE_LO, actionLabel = "下限を${p.T}に下げる", rangeKey = key, newLo = p.T.toString()))
            }
        }

        // 5) 1人の各シフト下限の合計が期間日数を超える（割り当て不能）
        for (i in 0 until p.S) {
            var sumLo = 0
            for (k in 0 until p.K) {
                val lo = p.rangeLo[i][k]
                if (lo != Int.MIN_VALUE && lo > 0) sumLo += lo
            }
            if (sumLo > p.T) {
                val name = state.staff.getOrNull(i)?.name ?: "#$i"
                out.add(SettingIssue(IssueKind.RANGE, "$name の回数下限の合計",
                    "各シフトの下限の合計が${sumLo}で、期間日数(${p.T}日)を超えています",
                    "どれかのシフトの下限を下げてください（合計を${p.T}以下に）"))
            }
        }

        // 6) [事前診断] シフト単位の構造的な過拘束（席数 vs 下限/上限の合計）。実行前に「何をしても無理」を提示し、
        //    無駄な最適化(数分)を避ける。誤検知を避けるため、明確に矛盾する2ケースのみ（読み取り専用・データ不変）。
        for (k in 0 until p.K) {
            var seatsLo = 0; var seatsHi = 0; var hasDemand = false
            for (j in 0 until p.T) {
                val n1 = p.need1[k][j]
                if (n1 < 0) continue   // need 未設定の日は対象外
                hasDemand = true
                val hi = if (p.use2 && p.need2[k][j] >= 0) p.need2[k][j] else n1
                seatsLo += maxOf(n1, 0); seatsHi += maxOf(hi, 0)
            }
            // [3.316.0] 休は need に依存しない実質上限（restCapacity）で判定するので、必要人数が1日も
            //   設定されていなくても検査する（3.301.1 で適切回数の検査に同じ変更を入れたのと同じ理由）。
            if (!hasDemand && k != p.restIdx) continue
            val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
            var capable = 0; var loSum = 0; var capSum = 0; var allCapped = true
            for (i in 0 until p.S) {
                if (!p.canDo(i, k)) continue
                capable++
                val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]
                if (lo != Int.MIN_VALUE && lo > 0) loSum += lo
                if (hi != Int.MAX_VALUE) capSum += hi else allCapped = false
            }
            // A) 下限の合計 > それを受け止められる上限 → 全員の下限を満たすと過剰配置/下限割れが不可避。
            // [3.316.0] 休だけ比較対象を restCapacity にする。旧実装は休も必要人数の合計(seatsHi)と
            //   比べており、休に need1=0 が明示設定された実データ（real/user）では seatsHi=0 に対し
            //   下限合計80 で**必ず誤警告**が出ていた（休に「1日に何人休んでよいか」の座席は無い）。
            //   3.235.0 で適切回数(6-C)には同じ理由で restCapacity 比較を入れたが、この検査は取り残していた。
            val loCapacity = if (k == p.restIdx) restCapacity(p) else seatsHi
            if (loSum > loCapacity) {
                out.add(SettingIssue(IssueKind.DEMAND, "「$sym」の回数下限の合計",
                    if (k == p.restIdx)
                        "担当者の下限の合計が${loSum}回ですが、他シフトの個人下限を差し引いた「$sym」の" +
                            "最大可能日数の合計は${loCapacity}回しかありません。全員の下限は同時に満たせず、下限割れが必ず出ます"
                    else
                        "担当者の下限の合計が${loSum}回ですが、必要数の合計は${loCapacity}回しかありません。全員の下限は同時に満たせず、過剰配置か下限割れが必ず出ます",
                    if (k == p.restIdx) "「$sym」の個人下限を下げるか、他シフトの個人下限を見直してください"
                    else "「$sym」の個人下限を下げるか、必要人数を増やしてください"))
            }
            // B) 全担当者に上限があり、上限の合計 < 必要数 → 席を埋めきれず人員不足(covU)が不可避。
            if (capable > 0 && allCapped && capSum < seatsLo) {
                out.add(SettingIssue(IssueKind.DEMAND, "「$sym」の必要人数",
                    "必要数の合計は${seatsLo}回ですが、担当者の上限の合計は${capSum}回しかありません。席を埋めきれず人員不足になります",
                    "「$sym」の個人上限を上げる/担当者を増やすか、必要人数を下げてください"))
            }
        }
        // C) 適切回数(apt=職員のレパートリー目標)の合計 > それを受け止められる上限 → 全員の目標を満たすと
        //    目標割れ(aptLow)か過剰配置(covO/aptHigh)が必ず出る。レパートリーと被覆が両立しない設定ズレ。
        //    休(restIdx)は「1日に何人休んでよいか」という座席上限を持たないため、必要数の合計ではなく
        //    「各職員が他シフトの個人下限を満たしたうえで最大何日休めるか」の合計と比較する。
        //    [3.301.0] 判定は aptBalances() に集約した。設定画面の「目標」カードが同じ関数を読むため、
        //    「診断は警告を出すのに設定画面は何も言わない」というズレが構造的に起きない。
        for (b in aptBalances(state, p)) {
            if (!b.overloaded) continue
            out.add(SettingIssue(IssueKind.DEMAND, "「${b.kigou}」の適切回数の合計",
                if (b.isRest)
                    "適切回数(レパートリー目標)の合計が${b.aptSum}回ですが、他シフトの個人下限を差し引いた「${b.kigou}」の" +
                        "最大可能日数の合計は${b.capacity}回しかありません。全員の目標は同時に満たせず、目標割れか過剰配置が必ず出ます"
                else
                    "適切回数(レパートリー目標)の合計が${b.aptSum}回ですが、必要数の合計は${b.capacity}回しかありません。" +
                        "全員の目標は同時に満たせず、目標割れか過剰配置が必ず出ます",
                if (b.isRest) "「${b.kigou}」の適切回数を下げるか、他シフトの個人下限を見直してください"
                else "「${b.kigou}」の適切回数を下げるか、必要人数を増やしてください"))
        }

        // 6b) [事前診断/幻のapt目標] 担当レパートリーから強制される最低回数 > apt目標 → 目標は構造的に達成不能。
        //    全日はいずれかの担当可シフトで埋まるため、シフト k の回数には
        //      count(k) >= T − Σ_{k'≠k, 担当可} min(上限(k'), T)
        //    の下界が成立（他シフトの上限合計では期間を埋めきれない分が k に必ず回る）。この強制下限が
        //    apt目標を超えるなら、超過(aptHigh)は何をどう最適化しても残る＝データ側の目標を直すのが正道。
        //    例: 担当={休,B4,有}・休10-10固定・有1-1固定・31日 → B4は最低20回。目標1は幻のaptHigh違反。
        //    上限未設定の他シフトが1つでもあれば下界は0以下＝発火しない（誤検知ゼロの保守的判定・読み取り専用）。
        for (i in 0 until p.S) {
            val name = state.staff.getOrNull(i)?.name ?: "#$i"
            for (k in 0 until p.K) {
                val t = p.apt[i][k]
                if (t < 0 || !p.canDo(i, k)) continue
                val otherHiSum = otherShiftCapSum(p, i, k)
                val forcedMin = p.T - otherHiSum
                if (forcedMin > t) {
                    val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                    out.add(SettingIssue(IssueKind.RANGE, "$name の「$sym」適切回数",
                        "担当できるシフトの構成上、他の担当シフトの個人上限（合計${otherHiSum}回）を守る限り「$sym」は最低${forcedMin}回になります（${p.T}日を埋めきれないぶんが必ず回ってくる）。" +
                            "適切回数${t}回との差${forcedMin - t}回は、個人上限を破って別のシフトへ逃がさない限り消えません（上限超過は上限違反として同じだけ残ります）",
                        "「$sym」の適切回数を${forcedMin}回以上にするか空欄にする、または他シフトの担当・上限を見直してください"))
                }
            }
        }

        // 6d) [3.373.0/実機ログ起因・希望で固定された回数 > apt目標] 希望どおりに置かれるセルは動かせない
        //    （希望を破る代金 pref=9000 は apt=1 の利得では絶対に釣り合わない＝`wishLocked` は探索・研磨の
        //    全パスが尊重する）。よってシフト k について「担当可能な希望が W 件」あれば count(k) >= W が
        //    どの解でも成立し、W が apt目標 t を超えるなら超過(aptHigh)は W−t 回ぶん必ず残る。
        //    [発見の経緯] 実機ログ(2026-08-15)で 大島愛 が 休17回・目標10 のまま動かず、apt が停滞していた。
        //    同型構造を合成して測ったところ、休の希望を 0件→17件 と増やすと AptPolish の到達点が
        //    休10(apt 0・採用5手) → 休17(apt 14・採用0手) へ単調に悪化し、**17件で実機の観測と完全に一致**した
        //    ＝最適化の不具合ではなく希望ロックが正しく効いている。しかし当時どの診断もその理由を述べず
        //    （6b は担当レパートリー由来の下限のみ・検査9 は個人上限との矛盾のみを見る）、利用者には
        //    「直せない apt 違反」が理由不明のまま残っていた。読み取り専用・データは変更しない。
        for (i in 0 until p.S) {
            val name = state.staff.getOrNull(i)?.name ?: "#$i"
            for (k in 0 until p.K) {
                val t = p.apt[i][k]
                if (t < 0 || !p.canDo(i, k)) continue
                var wished = 0
                for (j in 0 until p.T) if (p.wishLocked(i, j) && p.wish[i][j] == k) wished++
                if (wished > t) {
                    val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                    out.add(SettingIssue(IssueKind.RANGE, "$name の「$sym」適切回数と希望",
                        "「$sym」の希望が${wished}件あり、適切回数の目標${t}回を超えています。希望どおりに配置する限り" +
                            "「$sym」は必ず${wished}回以上になるため、差${wished - t}回ぶんの超過は最適化では消せません",
                        "「$sym」の適切回数を${wished}回以上にするか、${name}さんの「$sym」の希望を${wished - t}件減らしてください"))
                }
            }
        }

        // 6c) [事前診断/幻のhigh超過+代用要員提示・grilling確定=美幸・上條・大島の実例を踏まえ実装]
        //    6bと同じ「担当レパートリーから強制される最低回数」ロジックを staffRange 上限(hi、個人上限)
        //    にも適用。担当できるシフトの構成上、あるシフトの回数が個人上限を必ず上回ってしまう（他の
        //    担当シフトの上限合計だけでは全日を埋めきれない＝残りが必ずこのシフトに回る）場合、その
        //    職員をこのシフトの担当から外し、代わりに担当できる他の職員（代用要員候補）に置き換える
        //    ことを提案する。データは変更しない（HF77準拠、実際の担当変更は業務担当者が判断）。
        //    他シフトに上限未設定が1つでもあれば下界0以下＝発火しない（6bと同じ保守的判定・誤検知ゼロ）。
        for (i in 0 until p.S) {
            val name = state.staff.getOrNull(i)?.name ?: "#$i"
            for (k in 0 until p.K) {
                val hi = p.rangeHi[i][k]
                if (hi == Int.MAX_VALUE || !p.canDo(i, k)) continue
                val otherHiSum = otherShiftCapSum(p, i, k)
                val forcedMin = p.T - otherHiSum
                if (forcedMin > hi) {
                    val sym = state.shifts.getOrNull(k)?.kigou ?: k.toString()
                    val substitutes = (0 until p.S).filter { it != i && p.canDo(it, k) }
                        .map { state.staff.getOrNull(it)?.name ?: "#$it" }
                    val subText = if (substitutes.isEmpty()) "代用できる他の担当者がいません"
                        else "代用要員候補: ${substitutes.joinToString("・")}"
                    out.add(SettingIssue(IssueKind.RANGE, "${name}さんの「$sym」上限と担当構成の衝突",
                        "担当できるシフトの構成上、「$sym」は最低${forcedMin}回になります（他の担当シフトの上限合計${otherHiSum}回では${p.T}日を埋めきれません）が、${name}さんの「$sym」上限は${hi}回です。この人が担当を続ける限り上限超過は必ず出ます。$subText",
                        "${name}さんを「$sym」の担当から外し代用要員に置き換えるか、上限を${forcedMin}回以上に上げてください"))
                }
            }
        }

        // 7) [事前診断/配布不可] ある日に「そのシフトを担当できる人数」より必要人数が多い＝どう割り当てても
        //    人員不足(covU=HARD)が確定＝配布不可。最適化の hardFloor と同じ forcedCovU で検出（誤検知ゼロ）。
        for (fc in forcedCovU(state, p)) {
            out.add(SettingIssue(IssueKind.DEMAND, "「${fc.shiftSymbol}」の担当者不足（配布不可の原因）",
                "${fc.cells}日で、担当できる人数より必要人数が多く、人員不足(covU)が必ず出ます（不足の合計${fc.amount}）。この不足は最適化では解消できません",
                "「${fc.shiftSymbol}」を担当できる職員を増やすか、その日の必要人数を下げてください"))
        }

        // 8) [事前診断/重複定義・レビュー指摘P1] 氏名(空白無視)・シフト/グループ/スキル群の記号の重複を警告。
        //    重複があると制約評価・CSV取込とも「最初の1件」に解決される(firstWinsMap で統一済)が、2件が
        //    同一視されること自体は利用者に見えないため、定義の一意化を促す(read-only・非ブロック＝既存データは開ける)。
        run {
            fun dups(items: List<String>): List<String> =
                items.filter { it.isNotBlank() }.groupingBy { it }.eachCount().filter { it.value > 1 }.keys.toList()
            for (d in dups(state.staff.map { nameMatchKey(it.name) })) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "職員名の重複「$d」",
                    "同名（空白を除き一致）の職員が複数います。制約とCSV取込は最初の1人に解決され、2人目以降は区別できません",
                    "氏名を一意にしてください（例: 姓名の間や末尾に識別子を付ける）"))
            }
            for (d in dups(state.shifts.map { it.kigou.trim() })) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "シフト記号の重複「$d」",
                    "同じ記号のシフトが複数あります。制約とCSV取込は最初の1件に解決され、2件目以降は参照されません",
                    "シフト記号を一意にしてください"))
            }
            for (d in dups(state.groups.map { it.kigou.trim() })) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "グループ記号の重複「$d」",
                    "同じ記号のグループが複数あります。制約とCSV取込は最初の1件に解決されます",
                    "グループ記号を一意にしてください"))
            }
            for (d in dups(state.skillGroups.map { it.kigou.trim() })) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "スキルグループ記号の重複「$d」",
                    "同じ記号のスキルグループが複数あります。制約とCSV取込は最初の1件に解決されます",
                    "スキルグループ記号を一意にしてください"))
            }
        }

        // 9) [Constraint IR + MUS / 3.272.0] 矛盾の「最小説明」。希望（wishLocked）が絡む証明可能な
        //    矛盾の組合せを ConstraintMus（厳密DP・鳩の巣・二部マッチングの健全な証明）で検出し、
        //    「この◯件は同時に成立しません→どれか1件を緩めると解消」まで提示する。既存の手彫り検査
        //    （2b-3/6b/6c/検査3）はいずれも希望を扱わないため、**コアに希望を含む矛盾のみ**を出す＝
        //    重複ゼロ。read-only・スコアリング不変。発火＝真に矛盾（健全・誤検知ゼロの設計）。
        run {
            fun sym(k: Int) = state.shifts.getOrNull(k)?.kigou ?: k.toString()
            fun staffName(i: Int) = state.staff.getOrNull(i)?.name ?: "#$i"
            fun itemLabel(it: ConstraintMus.Item): String = when (it) {
                is ConstraintMus.WishPin ->
                    "希望「${staffName(it.staff)} ${safeDayLabel(state.startDate, it.day)}=${sym(it.shift)}」"
                is ConstraintMus.RangeCap -> "個人上限「${sym(it.shift)}を最大${it.hi}回」"
                is ConstraintMus.RangeFloor -> "個人下限「${sym(it.shift)}を最低${it.lo}回」"
                is ConstraintMus.WindowRule -> "窓ルール「${sym(it.shift)}を${it.windowDays}日で${it.minCount}回以上」"
                is ConstraintMus.DayNeed -> "必要人数「${sym(it.shift)}に${it.need}人」"
            }
            fun relaxHint(it: ConstraintMus.Item): String = when (it) {
                is ConstraintMus.WishPin ->
                    "${staffName(it.staff)}さんの${safeDayLabel(state.startDate, it.day)}の希望を調整する"
                is ConstraintMus.RangeCap -> "「${sym(it.shift)}」の個人上限を上げる"
                is ConstraintMus.RangeFloor -> "「${sym(it.shift)}」の個人下限を下げる"
                is ConstraintMus.WindowRule -> "窓ルール「${sym(it.shift)} ${it.windowDays}日で${it.minCount}回以上」の回数を下げる"
                is ConstraintMus.DayNeed -> "${sym(it.shift)}の必要人数を下げる"
            }
            fun hasWish(core: List<ConstraintMus.Item>) = core.any { it is ConstraintMus.WishPin }
            for (sc in ConstraintMus.analyzeStaffConflicts(p).filter { hasWish(it.core) }.sortedBy { it.core.size }.take(3)) {
                val name = staffName(sc.staff)
                val labels = sc.core.joinToString(" ・ ") { itemLabel(it) }
                val hints = sc.core.take(2).joinToString(" / ") { relaxHint(it) }
                out.add(SettingIssue(IssueKind.WISH, "${name}さんの希望と条件の組合せ",
                    "次の${sc.core.size}件は同時に成立しません（証明つき）: $labels",
                    "いずれか1件を緩めてください（例: $hints）"))
            }
            for (dc in ConstraintMus.analyzeDayConflicts(p).filter { hasWish(it.core) }.sortedBy { it.core.size }.take(3)) {
                val labels = dc.core.joinToString(" ・ ") { itemLabel(it) }
                val wishHint = dc.core.firstOrNull { it is ConstraintMus.WishPin }?.let { relaxHint(it) }
                out.add(SettingIssue(IssueKind.WISH, "${safeDayLabel(state.startDate, dc.day)} の必要人数と固定希望の衝突",
                    "固定された希望の組合せでは、この日の必要人数を満たせません。次の${dc.core.size}件は同時に成立しません（証明つき）: $labels",
                    "この日の希望を1件調整するか、必要人数を下げてください" + (wishHint?.let { "（例: $it）" } ?: "")))
            }
        }

        // [監査#7] SOFT 桁溢れ（辞書式崩壊）: soft 合計がスコア上限 1,000,000 に接近/超過すると、
        //   HARD 1件(=1,000,000) と soft が桁で干渉し「必須違反ゼロ最優先」が崩れる。初期解の soft で概算警告。
        run {
            val soft = Evaluator(p).fullEvalParts(normalizeSchedule(state.schedule.toIntArray2D(), p))[1]
            if (soft >= 900_000L) {
                out.add(SettingIssue(IssueKind.CONSTRAINT, "SOFT違反の合計が過大（${soft}）",
                    "調整項(SOFT)の合計がスコア上限 1,000,000 に接近しており、必須(HARD)違反ゼロを最優先する評価が崩れる恐れがあります",
                    "解消不能な制約（回数>日数の連勤条件など）や、多数の同時禁止(C42)・広すぎる範囲制約を見直して調整項を減らしてください"))
            }
        }

        // [誘導] 直すべき度合いが高い順に整列。SettingIssuesCard は先頭 take(6) のみ表示するため、
        //   最重要のデータ起因（配布不可→実現不能希望→過拘束→範囲矛盾）を確実に上位へ。sortedBy は安定＝同順は挿入順。
        return out.sortedBy { iss ->
            when {
                iss.where.contains("配布不可") -> 0   // covU 確定＝配布不可（最優先）
                iss.kind == IssueKind.WISH -> 1        // 実現不能希望
                iss.kind == IssueKind.DEMAND -> 2      // 過拘束（下限/上限/適切回数 vs 必要数）
                iss.kind == IssueKind.RANGE -> 3       // 範囲の矛盾
                else -> 4                              // 制約・SOFT桁溢れ ほか
            }
        }
    }

    private fun c3FamilyJp(fam: String): String = when (fam) {
        "c3" -> "必須の並び"
        "c3n" -> "禁止の並び"
        "c3m" -> "推奨の並び"
        "c3mn" -> "回避の並び"
        else -> fam
    }

    /**
     * [デバッグ用] 確定スケジュールの制約違反を「家族ごとに・場所と実値つき」で列挙する。
     *  - 被覆(covU/covO/c41/c41s): 必要数/現状数 を実値表示（needが未設定=demand無しかどうかも即判明）
     *  - 回数(low/high/c2): 回数/下限/上限
     *  - セル(c1/c3/c3n/c3m/c3mn/c42/c42s/pref/groupViol): 誰の・何日・どのシフトか
     * 読み取り専用（重み・データ不変＝安全）。家族ごと最大件数で打ち切り、ログ肥大を防ぐ。
     */
    fun buildViolationDebug(state: MagiState, schedule: Array<IntArray>, report: ViolationReport): List<String> {
        val p = Problem(state)
        val s = normalizeSchedule(schedule, p)
        val out = ArrayList<String>()
        // [スパム対策] 各違反家族の詳細列挙の上限。1パターン把握には十分な件数に絞り、長大化を防ぐ
        //   （以前は 12〜15。c1/c3m など大量家族の1行が極端に伸びていた）。総数は「(N件)」で常に保持。
        val DETAIL_CAP = 8
        fun sym(k: Int) = state.shifts.getOrNull(k)?.kigou ?: k.toString()
        fun nm(i: Int) = state.staff.getOrNull(i)?.name ?: "#$i"
        fun day(j: Int) = safeDayLabel(state.startDate, j)
        // [構造HARD下限] データ起因で解消不能な必須違反(covU)の下限。最適化の hardFloor と同値。
        //   >0 なら「配布不可はデータ起因＝最適化は残りをSOFT研磨する」と判断できる（読み取り専用）。
        run {
            val forced = forcedCovU(state, p)
            val floor = forced.sumOf { it.amount }
            if (floor > 0) out.add("[W] 構造HARD下限: 担当者不足で covU=$floor が解消不能（配布不可はデータ起因）: " +
                forced.joinToString(" / ") { "${it.shiftSymbol} ${it.cells}日 不足${it.amount}" })
            else out.add("[I] 構造HARD下限: 0（担当者数の観点では各シフトが需要を満たせる。希望/禁止連続による構造的な人員不足は別途 CoverageDiag/設定ミス を参照）")
        }
        // [3.282.0/新領域ログ監査] 違反詳細ヘッダの件数は「最重クラスで解決済みのセル位置数」で、
        //   breakdown の fire 数とは意味が異なる（c1=窓ごと計上だがmarkはrun先頭のみ・c3n=1 fireでも
        //   パターン全セルをmark・重い族に同一セルを奪われた軽い族は位置ごと消える等）。実機ログで
        //   「違反詳細 c1(11件)」vs「UnifiedCheck c1=12」の食い違いとして混乱を生んでいたため、
        //   fires(breakdown)を併記し両者が異なるときは「件数F・場所N箇所」と明示する。表示のみ・スコア不変。
        fun emit(byFam: Map<String, MutableList<String>>, cap: Int, fires: Map<String, Int>? = null) {
            for ((fam, items) in byFam) {
                val shown = items.take(cap).joinToString(" ; ")
                val more = if (items.size > cap) " …他${items.size - cap}件" else ""
                val f = fires?.get(fam)
                val head = if (f != null && f != items.size) "件数${f}・場所${items.size}箇所" else "${items.size}件"
                out.add("[D] 違反詳細 $fam($head): $shown$more")
            }
        }

        // 0) 需給サマリ: シフトごとに「日次需要」と「個人下限/上限・適切回数(クランプ後)の供給圧力・現状配置」を
        //    対比し、過剰(covO=日数オーバー)/不足(covU)の構造的要因を一目で示す。読み取り専用（重み・データ不変）。
        //    例: Dﾃ 需要31 < 適切回数計35 → 各人をその回数へ近づける圧力が需要を超え、過剰配置(1日2人)が出る。
        //    注: 下限/上限/適切回数の「計」は設定済み職員のみの合計。未設定者がいると実効上限は無制限なので、
        //    上限計<需要でも不足とは限らない（不足の構造判定は全員に上限がある場合に限定する）。
        run {
            val cnt = countMatrix(p, s)
            for (k in 0 until p.K) {
                var demand = 0
                for (j in 0 until p.T) { val n = p.need1[k][j]; if (n > 0) demand += n }
                var doable = 0; var loSum = 0; var hiSum = 0; var aptSum = 0
                var loCnt = 0; var hiCnt = 0; var aptCnt = 0; var cur = 0
                for (i in 0 until p.S) {
                    cur += cnt[i][k]
                    if (!p.canDo(i, k)) continue
                    doable++
                    val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]; val t = p.apt[i][k]
                    if (lo != Int.MIN_VALUE) { loSum += lo; loCnt++ }
                    if (hi != Int.MAX_VALUE) { hiSum += hi; hiCnt++ }
                    if (t >= 0) { aptSum += t; aptCnt++ }
                }
                val hasRange = loCnt > 0 || hiCnt > 0
                val hasApt = aptCnt > 0
                if (demand == 0 && !hasRange && !hasApt) continue   // 需給の概念が薄いシフトは省略
                val notes = ArrayList<String>()
                // [3.274.0 監査で修正] 実際の過不足は**日次 covOCell/covUCell の合計**（source of truth）で示す。
                //   旧実装は月間の `現状 − 需要` を covO/covU とラベルしていたが、毎日需要のあるシフト(Dﾃ 等)
                //   でしか両者は一致せず、稀にしか需要のないシフト(B4 等: need1=0/need2=1)では「月間の過剰配置数」を
                //   covO件数と誤表示していた（実機ログ「B4 過剰6(covO)」だが実covO=1）。日次 cov の合計へ統一。
                var covUreal = 0; var covOreal = 0
                for (j in 0 until p.T) {
                    var g = 0; for (i in 0 until p.S) if (s[i][j] == k) g++
                    covUreal += p.covUCell(k, j, g)
                    covOreal += p.covOCell(k, j, g)
                }
                if (covUreal > 0) notes.add("現状${cur}(需要${demand})→不足${covUreal}(covU)")
                if (covOreal > 0) notes.add("現状${cur}(需要${demand})→過剰${covOreal}(covO)")
                // 構造要因(過剰): 各人が下限/適切回数まで埋める圧力(=確実に埋まる量)の合計が、
                //   **covO を払わずに置ける上限**を超過。
                // [3.372.0/実機ログ起因] 比較先を need1 合計(demand)から seatsHi へ是正した。旧実装は
                //   demand と比べていたため、2世代需要(use2)が有効で need2>need1 のデータでは
                //   「置いても罰の無い枠」を無視して圧力を過大報告していた（実機 2026-08-15 の B4:
                //   需要2 に対し「供給圧力7(適切回数)>需要2」と出るが、need2 の枠内なら7人置いても
                //   covO は増えない＝構造的な矛盾は無い）。設定ミス検査6-C は同じ状況で正しく沈黙して
                //   おり、**同じ問い「目標は席に収まるか」に2つの診断が違う容量定義で違う答えを出して
                //   いた**。6-C と同じ seatsHi に揃える（緩い側が正しかった）。
                var seatsHi = 0
                for (j in 0 until p.T) {
                    val n1 = p.need1[k][j]
                    if (n1 < 0) continue
                    val hi = if (p.use2 && p.need2[k][j] >= 0) p.need2[k][j] else n1
                    seatsHi += maxOf(hi, 0)
                }
                val pull = maxOf(loSum, aptSum)
                val pullSrc = if (aptSum >= loSum) "適切回数" else "下限"
                if (seatsHi > 0 && pull > seatsHi) notes.add("供給圧力${pull}(${pullSrc})>置ける上限${seatsHi}")
                // 構造要因(不足): 全担当者に上限があり、その合計が需要未満のときのみ（未設定者は無制限なので除外）。
                if (demand > 0 && doable > 0 && hiCnt == doable && hiSum < demand) notes.add("全${doable}名の上限計${hiSum}<需要${demand}→構造的に不足")
                fun cs(sum: Int, c: Int) = if (c == doable) "$sum" else "$sum(${c}/${doable}名)"
                val tag = if (notes.any { it.contains("過剰") || it.contains("不足") }) "需給注意" else "需給"
                val rangeStr = if (hasRange) " 下限計${cs(loSum, loCnt)} 上限計${cs(hiSum, hiCnt)}" else ""
                val aptStr = if (hasApt) " 適切回数計${cs(aptSum, aptCnt)}" else ""
                out.add("[D] $tag ${sym(k)}: 需要$demand 担当${doable}名$rangeStr$aptStr 現状$cur" +
                    (if (notes.isNotEmpty()) " → ${notes.joinToString(" / ")}" else ""))
            }
        }

        // 0b) 上下チェック(全シフト網羅): 下限/上限(staffRange)が設定された全シフトについて、個人別の
        //     下限割れ(low)/上限超過(high)を担当者ぶん洗い出す。違反詳細(low/high)は違反のみ列挙だが、
        //     こちらは設定済みシフトを網羅し違反0でも「上下OK」を出す。判定は UnifiedViolationChecker と一致
        //     （low: lo!=0 かつ canDo かつ 回数<lo / high: 回数>hi）。読み取り専用。
        run {
            val cnt = countMatrix(p, s)
            for (k in 0 until p.K) {
                val lows = ArrayList<String>(); val highs = ArrayList<String>()
                var hasBound = false
                for (i in 0 until p.S) {
                    if (!p.canDo(i, k)) continue
                    val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]; val n = cnt[i][k]
                    if (lo != Int.MIN_VALUE && lo != 0) { hasBound = true; if (n < lo) lows.add("${nm(i)} $n<$lo") }
                    if (hi != Int.MAX_VALUE) {
                        hasBound = true
                        // [代用要員提示/grilling確定=美幸・上條・大島の実例] 上限超過している職員に、
                        //   このシフトを担当できる他の職員(代用要員候補)の人数を併記する。担当を外し
                        //   代用要員へ置き換えることで解消できる可能性を示す（読取専用・データ変更なし）。
                        if (n > hi) {
                            val subCount = (0 until p.S).count { it != i && p.canDo(it, k) }
                            highs.add("${nm(i)} $n>$hi(代用可${subCount}名)")
                        }
                    }
                }
                if (!hasBound) continue
                fun part(label: String, xs: List<String>) =
                    if (xs.isEmpty()) "${label}0名" else "${label}${xs.size}名(${xs.take(8).joinToString(" ")}${if (xs.size > 8) " …他${xs.size - 8}件" else ""})"
                val tag = if (lows.isEmpty() && highs.isEmpty()) "上下OK" else "上下注意"
                out.add("[D] $tag ${sym(k)}: ${part("下限割れ", lows)} / ${part("上限超過", highs)}")
            }
        }

        // 1) 被覆: 必要数/現状数の実値（needViolations は k,j キー）。covU/covO のみ扱う。
        if (report.needViolations.isNotEmpty()) {
            val cov = coverage(p, s)
            val byFam = LinkedHashMap<String, MutableList<String>>()
            for ((key, cls) in report.needViolations) {
                // [診断強化②③] c41/c41s は被覆ではなく「群(スキル)×シフトの人数制約」。被覆テンプレ(必要{need1})では
                //   群が判別できず、休など need1=-1 のシフトで「必要-1」と誤表示される。専用集約(1b)へ回す。
                if (cls == "vio-c41" || cls == "vio-c41s") continue
                val parts = key.split(','); val k = parts.getOrNull(0)?.toIntOrNull() ?: continue; val j = parts.getOrNull(1)?.toIntOrNull() ?: continue
                if (k !in 0 until p.K || j !in 0 until p.T) continue
                val n1 = p.need1[k][j]; val n2 = if (p.use2) p.need2[k][j] else n1
                val needStr = if (p.use2 && n2 >= 0 && n2 != n1) "$n1~$n2" else "$n1"
                byFam.getOrPut(cls.removePrefix("vio-")) { ArrayList() }.add("${day(j)} ${sym(k)} 必要$needStr/現状${cov[j][k]}")
            }
            // [3.380.0/実機ログ起因] **この呼出だけ `fires` を渡していなかった**＝3.282.0 が
            //   「件数(breakdown)と場所(セル数)は別物」と明示するために入れた仕組みの取り残し。
            //   covO は1枠が最大4人ぶん超過しうるので両者が大きく食い違い、実機ログでは
            //   `UnifiedCheck covO=23` / `CoverageDiag 合計23 — 14枠` に対して
            //   **`違反詳細 covO(14件)`** と、**場所数を件数のように**出していた（他の族は
            //   `件数23・場所14箇所` と正しく書き分けている）。同じ report の中で数字が食い違う。
            emit(byFam, DETAIL_CAP, report.breakdown)
        }

        // 1b) [診断強化②③＋スパム削減] c41/c41s = 日次・群(スキル)×シフトの人数が[下限,上限]に収まるか。
        //     被覆テンプレでは群が消え、複数群が同じ(シフト,日)で1件に潰れて件数も合わない(例 score c41=124 vs 詳細31)。
        //     cons 行ごとに「群/スキル × シフト・下限上限・違反日数・現状人数範囲」で集約し、どの群が何日どれだけ
        //     外れたかを最小行で示す（124件→cons行数の数行に圧縮）。
        run {
            fun emitCons(rows: List<C41>, fam: String, memberOf: (Int) -> Int, groupSym: (Int) -> String) {
                for (c in rows) {
                    var vdays = 0; var minZ = Int.MAX_VALUE; var maxZ = 0
                    for (j in 0 until p.T) {
                        var z = 0
                        for (i in 0 until p.S) if (memberOf(i) == c.groupIdx && s[i][j] == c.shiftIdx) z++
                        if (z < c.l || z > c.u) { vdays++; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z }
                    }
                    if (vdays > 0) {
                        val range = if (minZ == maxZ) "$minZ" else "$minZ〜$maxZ"
                        out.add("[D] 違反詳細 $fam: ${groupSym(c.groupIdx)}×${sym(c.shiftIdx)} ${vdays}日違反 (下限${c.l}/上限${c.u}, 現状$range)")
                    }
                }
            }
            emitCons(p.cons41, "c41", { i -> p.sgrp[i] }, { g -> state.groups.getOrNull(g)?.kigou ?: "群$g" })
            emitCons(p.cons41s, "c41s", { i -> p.ssk[i] }, { g -> state.skillGroups.getOrNull(g)?.kigou ?: "スキル$g" })
        }

        // 2) 回数: 回数/下限/上限（countViolations は i,k キー）
        //   [3.353.0] 旧実装は `countViolations`（1セル=最重1クラス）だけを見ていたため、軽い族が重い族と
        //   同じ (職員,シフト) に重なると診断から**丸ごと消えて**いた（実機ログ: 内訳 c2=1 なのに詳細行が
        //   無い／apt=29 に対し表示は7箇所ぶん＝残り3単位は low の裏に隠れていた）。3.111.0 が cellFamilies で
        //   セル空間に対して解いたのと同じ形で、回数空間の全クラス（countFamilies）を列挙する。
        //   ヘッダも 3.282.0 と同じく breakdown と突き合わせ、件数と場所数が違うときは両方出す。
        if (report.countViolations.isNotEmpty()) {
            val cnt = countMatrix(p, s)
            val byFam = LinkedHashMap<String, MutableList<String>>()
            val pairs = if (report.countFamilies.isNotEmpty()) {
                report.countFamilies.entries.flatMap { (k, list) -> list.map { k to it } }
            } else {
                report.countViolations.entries.map { it.key to it.value }
            }
            for ((key, cls) in pairs) {
                val parts = key.split(','); val i = parts.getOrNull(0)?.toIntOrNull() ?: continue; val k = parts.getOrNull(1)?.toIntOrNull() ?: continue
                if (i !in 0 until p.S || k !in 0 until p.K) continue
                val lo = p.rangeLo[i][k].takeIf { it != Int.MIN_VALUE }
                val hi = p.rangeHi[i][k].takeIf { it != Int.MAX_VALUE }
                // [実機ログ起因] aptLow/aptHigh は「目標(クランプ後)との偏差」が発火理由なのに従来は staffRange の
                //   下限/上限しか出ず（例: 回数4 下限4 上限5 → なぜ違反?）、読者が原因を特定できなかった。目標を併記。
                val apt = if (cls == "vio-aptLow" || cls == "vio-aptHigh") p.apt[i][k].takeIf { it >= 0 } else null
                byFam.getOrPut(cls.removePrefix("vio-")) { ArrayList() }
                    .add("${nm(i)} ${sym(k)} 回数${cnt[i][k]}" + (apt?.let { " 目標$it" } ?: "") + (lo?.let { " 下限$it" } ?: "") + (hi?.let { " 上限$it" } ?: ""))
            }
            // 族名が breakdown のキーと一致するもの(low/high/c2)だけ突き合わせる。aptLow/aptHigh は
            //   breakdown に個別キーが無く実体は apt（重み1.0・3.243.0）＝両方へ同じ 29 を出すと二重に見える。
            //   apt は下の専用行で「合計と場所数」を1度だけ示す。
            emit(byFam, DETAIL_CAP, report.breakdown)
            val aptFires = report.breakdown["apt"] ?: 0
            if (aptFires > 0) {
                val lo = byFam["aptLow"]?.size ?: 0
                val hi = byFam["aptHigh"]?.size ?: 0
                out.add(
                    "[D] 違反詳細 apt(件数$aptFires・場所${lo + hi}箇所): 目標割れ${lo}箇所 + 目標超過${hi}箇所" +
                        "（件数=各行の|回数−目標|の合計＝1箇所で複数件になる）",
                )
            }
        }

        // 3) セル違反: 誰の・何日・どのシフト（violations は i,j キー）
        if (report.violations.isNotEmpty()) {
            val byFam = LinkedHashMap<String, MutableList<String>>()
            for ((key, cls) in report.violations) {
                val parts = key.split(','); val i = parts.getOrNull(0)?.toIntOrNull() ?: continue; val j = parts.getOrNull(1)?.toIntOrNull() ?: continue
                if (i !in 0 until p.S || j !in 0 until p.T) continue
                byFam.getOrPut(cls.removePrefix("vio-")) { ArrayList() }.add("${nm(i)} ${day(j)}=${sym(s[i][j])}")
            }
            emit(byFam, DETAIL_CAP, report.breakdown)
        }

        // 3.4) [3.355.0/ログ強化] DETAIL_CAP で切れる大きなセル族は「…他58件」で終わり、**誰に集中して
        //   いるか**が読めなかった（実機ログ: c3 77件・場所66箇所のうち8箇所しか見えない）。checker が出した
        //   場所（cellFamilies）をそのまま職員別に数え直すだけ＝規則の再実装をしないのでドリフトしない。
        run {
            val perFam = LinkedHashMap<String, HashMap<Int, Int>>()
            for ((key, list) in report.cellFamilies) {
                val i = key.substringBefore(',').toIntOrNull() ?: continue
                if (i !in 0 until p.S) continue
                for (cls in list) perFam.getOrPut(cls.removePrefix("vio-")) { HashMap() }.merge(i, 1) { a, b -> a + b }
            }
            for ((fam, byStaff) in perFam) {
                if (fam == "c1") continue                          // c1 は下の「職員×窓ルール別」がより詳しい
                if (byStaff.values.sum() <= DETAIL_CAP) continue   // 全件が上に出ているなら冗長
                val txt = byStaff.entries.sortedByDescending { it.value }
                    .joinToString(" / ") { "${nm(it.key)} ${it.value}箇所" }
                out.add("[D] $fam 集約（職員別・場所数の全件）: $txt")
            }
        }

        // 3.5) [c1族の職員×窓ルール別件数] 「違反詳細 c1(N件)」はDETAIL_CAP=8で打ち切られ、特定職員が
        //   どの窓ルールで何件かは埋もれる（例: N件中8件しか見えず職員別内訳が分からない）。全件を
        //   職員×ルール別に再集計し、打ち切りなしの1行サマリとして追加する。読取専用（重み・データ不変）。
        //   [3.282.0/新領域ログ監査] 旧実装は mark と同じ `!prevViol`（違反ラン先頭のみ）で数えており、
        //   ラン長>1 のとき breakdown c1（=違反窓ごと計上）と食い違う第3の計数意味論になっていた
        //   （3.227.0 の意図は「breakdown と突合できる全件件数」）。checker の inc と同一の
        //   「違反窓ごと」計上へ是正＝本行の合計は常に UnifiedCheck の c1 と一致する。
        if ((report.breakdown["c1"] ?: 0) > 0) {
            val perStaffRule = LinkedHashMap<Int, LinkedHashMap<String, Int>>()
            for (c in p.cons1) {
                val ruleLabel = "${sym(c.shiftIdx)}(${c.day1}日窓≥${c.day2})"
                for (i in 0 until p.S) {
                    if (!p.canDo(i, c.shiftIdx)) continue
                    var j = 0
                    while (j <= p.T - c.day1) {
                        var z = 0
                        for (l in 0 until c.day1) if (s[i][j + l] == c.shiftIdx) z++
                        if (z < c.day2) perStaffRule.getOrPut(i) { LinkedHashMap() }.merge(ruleLabel, 1, Int::plus)
                        j++
                    }
                }
            }
            if (perStaffRule.isNotEmpty()) {
                val lines = perStaffRule.entries.joinToString(" / ") { (i, rules) ->
                    "${nm(i)} " + rules.entries.joinToString(", ") { (label, cnt) -> "$label${cnt}件" }
                }
                out.add("[D] c1内訳（職員×窓ルール別件数・全件）: $lines")
            }
        }

        // 3.6) [3.355.0/ログ強化] weekly は実機で最大の族（合計307中156）なのに内訳が一切無く、
        //   「まだ狙える weekly 156件」としか読めなかった。**回数が7の倍数でないぶんは配置をどう変えても
        //   消せない**（目標=round(回数/7) なので余りが必ず偏差として残る）。その構造床と、曜日の寄せ方で
        //   減らせる残りを分けて示す。床は `weeklyFloorOfCount` の総和＝checker と同じ目標値から導出。
        if ((report.breakdown["weekly"] ?: 0) > 0) {
            val cntW = countMatrix(p, s)
            var floor = 0
            val worst = ArrayList<Triple<Int, Int, Int>>()   // (staff, shift, いま減らせる余地)
            for (i in 0 until p.S) for (k in 0 until p.K) {
                val c = cntW[i][k]
                if (c <= 0) continue
                floor += weeklyFloorOfCount(c)
            }
            for (loc in report.distLocations["weekly"].orEmpty()) {
                val i = loc.getOrNull(0) ?: continue; val k = loc.getOrNull(1) ?: continue
                val dev = loc.getOrNull(2) ?: continue
                val room = dev - weeklyFloorOfCount(cntW.getOrNull(i)?.getOrNull(k) ?: 0)
                if (room > 0) worst.add(Triple(i, k, room))
            }
            val total = report.breakdown["weekly"] ?: 0
            val head = "[D] weekly内訳: 合計${total}件 = 構造床${minOf(floor, total)}件(回数が7の倍数でない＝配置では消せない)" +
                " + 曜日の寄せ方で減らせる${(total - floor).coerceAtLeast(0)}件"
            val topTxt = worst.sortedByDescending { it.third }.take(DETAIL_CAP)
                .joinToString(" ; ") { (i, k, room) -> "${nm(i)} ${sym(k)} 余地${room}" }
            out.add(if (topTxt.isEmpty()) head else "$head / 余地の大きい順: $topTxt")
        }

        if (out.isEmpty()) out.add("[D] 違反詳細: 制約違反はありません")
        return out
    }

    private fun buildLoadDataBitSummary(state: MagiState, p: Problem, schedule: Array<IntArray>): String {
        var assigned = 0
        for (row in schedule) {
            for (v in row) {
                if (v in 0 until p.K) assigned++
            }
        }
        val possible = p.S * p.T
        var allowBits = 0
        for (g in 0 until p.G) {
            allowBits += p.bucket.getOrNull(g)?.size ?: 0
        }
        val wishCount = state.wishes.size
        val rangeCount = state.staffRange.size
        return "LoadDataBit: staffN=${p.S} termT=${p.T} shiftK=${p.K} assigned=$assigned/$possible allowBits=$allowBits wishes=$wishCount ranges=$rangeCount"
    }

    private fun buildLoadDataBitDetails(state: MagiState, p: Problem): List<String> {
        val out = ArrayList<String>()
        for (g in 0 until p.G) {
            val allowedParts = ArrayList<String>()
            for (k in p.bucket[g]) {
                allowedParts.add(state.shifts.getOrNull(k)?.kigou ?: k.toString())
            }
            val allowed = allowedParts.joinToString(" ")
            var members = 0
            for (staff in state.staff) {
                if (staff.groupIdx == g) members++
            }
            out.add("Group ${state.groups.getOrNull(g)?.kigou ?: g}: members=$members allowed=[$allowed]")
        }
        return out
    }

    private fun buildShiftCountDiagnostic(state: MagiState, p: Problem, schedule: Array<IntArray>): List<ShiftCountDiagnostic> {
        val counts = countMatrix(p, schedule)
        val out = ArrayList<ShiftCountDiagnostic>()
        for (i in 0 until p.S) for (k in 0 until p.K) {
            val lo = p.rangeLo[i][k].takeIf { it != Int.MIN_VALUE }
            val hi = p.rangeHi[i][k].takeIf { it != Int.MAX_VALUE }
            if (lo == null && hi == null) continue
            val n = counts[i][k]
            val status = when {
                lo != null && n < lo -> "LOW"
                hi != null && n > hi -> "HIGH"
                else -> "OK"
            }
            out.add(ShiftCountDiagnostic(i, state.staff.getOrNull(i)?.name ?: "#$i", state.shifts.getOrNull(k)?.kigou ?: k.toString(), n, lo, hi, status))
        }
        return out.sortedWith(compareBy<ShiftCountDiagnostic> { it.status != "LOW" && it.status != "HIGH" }.thenBy { it.staffIndex })
    }

    private fun invalidAssignmentCells(state: MagiState, p: Problem, schedule: Array<IntArray>): List<String> {
        val out = ArrayList<String>()
        for (i in 0 until p.S) for (j in 0 until p.T) {
            val k = schedule[i][j]
            if (k !in 0 until p.K) out.add("$i,$j=範囲外($k)")
            else if (!p.canDo(i, k)) out.add("$i,$j=${state.shifts.getOrNull(k)?.kigou ?: k}")
        }
        return out
    }

    private fun badStaffRanges(state: MagiState, p: Problem): Int {
        var bad = 0
        for ((key, r) in state.staffRange) {
            val parts = key.split(',')
            val i = parts.getOrNull(0)?.toIntOrNull()
            val k = parts.getOrNull(1)?.toIntOrNull()
            val lo = r.lo.trim().toIntOrNull()
            val hi = r.hi.trim().toIntOrNull()
            if (i == null || k == null || i !in 0 until p.S || k !in 0 until p.K) bad++
            if (lo != null && hi != null && lo > hi) bad++
        }
        return bad
    }

    private fun impossibleDemandDays(state: MagiState, p: Problem): List<String> {
        val out = ArrayList<String>()
        for (j in 0 until p.T) for (k in 0 until p.K) {
            val need = p.need1[k][j]
            if (need <= 0) continue
            var capable = 0
            for (i in 0 until p.S) {
                if (p.canDo(i, k)) capable++
            }
            if (need > capable) out.add("${safeDayLabel(state.startDate, j)} ${state.shifts.getOrNull(k)?.kigou ?: k}: need=$need capable=$capable")
        }
        return out
    }

    private fun findDuplicateSeqConstraints(state: MagiState): List<String> {
        val out = ArrayList<String>()
        collectDuplicateSeq("c3", state.cons3, out)
        collectDuplicateSeq("c3n", state.cons3n, out)
        collectDuplicateSeq("c3m", state.cons3m, out)
        collectDuplicateSeq("c3mn", state.cons3mn, out)
        return out
    }

    private fun collectDuplicateSeq(name: String, rows: List<C3Row>, out: MutableList<String>) {
        val seen = HashSet<String>()
        for (r in rows) {
            val parts = ArrayList<String>()
            for (item in r.pattern) {
                if (item.isBlank()) break
                parts.add(item)
            }
            val key = parts.joinToString("→")
            if (key.isBlank()) continue
            if (!seen.add(key)) out.add("$name:$key")
        }
    }
}

private fun safeDayLabel(startDate: String, offset: Int): String = try {
    val d = java.time.LocalDate.parse(startDate).plusDays(offset.toLong())
    val wd = "月火水木金土日"[d.dayOfWeek.value - 1]
    "${d.monthValue}/${d.dayOfMonth}($wd)"
} catch (_: Exception) {
    "${offset + 1}日"
}
