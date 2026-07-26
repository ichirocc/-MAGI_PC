package com.magi.app.v6

import com.magi.app.model.MagiState
import kotlinx.coroutines.withContext
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.isActive

/**
 * Final bridge for Web App-level handlers.
 *
 * V6 Web kept many behaviors inside React App methods instead of standalone
 * worker functions: handleSimple, handleCheck, handleOptimize, busy-detail
 * construction, impossible-wish gate, algorithm labels, and post-optimization
 * HF80/HF67/HF66/HF70 chaining.  This object ports those App-level semantics as
 * pure Kotlin so ViewModel/Compose can call the same workflow without WebView.
 */
/** 勤務表最適化のタイムアウト上限（秒）。高精度を保ったまま5分(300s)以内へ圧縮（停滞早期脱出＋RSI++をこの予算に収める）。 */
const val MAX_OPTIMIZE_SEC = 300

object V6FinalPort {
    data class BusyDetail(
        val algorithm: String,
        val base: String = algorithm,
        val problemSize: String,
        val constraintCount: String,
        val subtitle: String = "",
        val phaseDesc: String = "",
        val expectedSec: String = "",
        val estimatedIter: String = "",
        val uiFrozen: Boolean = false,
        val startedAt: Long = System.currentTimeMillis(),
    )

    data class AlgorithmLabel(val icon: String, val name: String, val desc: String, val tech: String)

    sealed class OptimizationPlan {
        data class V5(val seconds: Int) : OptimizationPlan()
        data class ALNS(val seconds: Int, val restarts: Int) : OptimizationPlan()
        data class RSIThenALNS(val rsiSec: Int, val alnsSec: Int, val alnsRestarts: Int) : OptimizationPlan()
        // [3.266.0] 旧RSIPlusから改称。211秒以上はRSI++クローン群でなく、ALNS/RSI/RSI++の異種
        //   非同期適応ポートフォリオ(V6Algorithm.PORTFOLIO)を使う（HypothesisDiversityPolicy.
        //   autoAlgorithmForBudget と同じ閾値・同じ意図。旧実装はここが独立した別ロジックのままで、
        //   V6NativeOptimizer.chooseAlgorithm側だけを直しても実際のAUTOフローには反映されなかった）。
        data class Portfolio(val seconds: Int) : OptimizationPlan()
    }

    data class ImpossibleWishGate(
        val allowed: Boolean,
        val count: Int,
        val message: String,
        val logs: List<MirrorLog>,
    )

    data class ActionResult(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val phase: String,
        val busyDetail: BusyDetail,
        val logs: List<MirrorLog>,
        val post: V6PostOptimizationResult? = null,
    )

    fun buildBusyDetail(state: MagiState, algorithm: String, overrides: Map<String, String> = emptyMap()): BusyDetail {
        val n = state.staffCount
        val t = state.dayCount
        val k = state.shiftCount
        val totalHardCons = state.cons1.size + state.cons2.size + state.cons3.size + state.cons3n.size + state.cons41.size + state.cons42.size
        val totalSoftCons = state.cons3m.size + state.cons3mn.size
        val wishCount = state.wishes.size
        return BusyDetail(
            algorithm = algorithm,
            problemSize = "${n}名 × ${t}日 × ${k}シフト = ${n * t * k} セル",
            constraintCount = "HARD ${totalHardCons}件 / SOFT ${totalSoftCons}件 / 希望 ${wishCount}件",
            subtitle = overrides["subtitle"].orEmpty(),
            phaseDesc = overrides["phaseDesc"].orEmpty(),
            expectedSec = overrides["expectedSec"].orEmpty(),
            estimatedIter = overrides["estimatedIter"].orEmpty(),
            uiFrozen = overrides["uiFrozen"]?.toBooleanStrictOrNull() ?: false,
        )
    }

    fun confirmDespiteImpossibleWishes(state: MagiState, allowImpossible: Boolean = false): ImpossibleWishGate {
        val imp = V6SanityPort.detectImpossibleWishes(state)
        if (imp.isEmpty()) return ImpossibleWishGate(true, 0, "不可能希望なし", emptyList())
        val lines = imp.groupBy { it.staffName }.entries.take(12).map { (name, rows) ->
            "・$name: ${rows.size}件 (${rows.take(3).joinToString { "${it.dayIndex + 1}日=${it.shiftSymbol}" }})"
        }
        val msg = "不可能希望が ${imp.size}件あります。担当範囲外シフトへの希望は永久に充足できません。\n" +
            lines.joinToString("\n") +
            if (imp.size > lines.sumOf { line -> Regex("\\d+件").find(line)?.value?.removeSuffix("件")?.toIntOrNull() ?: 0 }) "\n…詳細はSanityCheckを確認" else ""
        val level = if (allowImpossible) "W" else "E"
        val logs = listOf(MirrorLog(level = level, tag = "ImpossibleWishGate", message = msg.replace("\n", " / ")))
        return ImpossibleWishGate(allowImpossible, imp.size, msg, logs)
    }

    fun optimizationPlan(seconds: Int): OptimizationPlan {
        // [review #4] Honor the user's budget: the algorithm is still chosen by range, but the
        // run time uses the requested `seconds` (previously fixed 10/30/90/150+75... regardless).
        // [5分圧縮] 上限300sでも最上位のフェーズが回るよう閾値を前倒し。
        val s = seconds.coerceAtLeast(1)
        return when {
            s <= 30 -> OptimizationPlan.V5(s)
            // [実機指摘「60秒予算を1つだけのアルゴリズムで使用」] 旧: 31〜90s は ALNS 単発で、詰まった
            //   HARD 族（c3n 等）を狙う RSI フェーズが一度も走らなかった。短予算でも複合
            //   （RSI=違反集中 2/3 → ALNS=研磨 1/3）へ。各段は入力比 keep-best 番兵つき＝退化なし。
            s <= 210 -> { val rsi = (s * 2) / 3; OptimizationPlan.RSIThenALNS(rsi, s - rsi, 2) }
            // [3.266.0] 211秒以上は同型RSI++クローン8本でなく、ALNS/RSI/RSI++の異種非同期適応
            //   ポートフォリオ(V6Algorithm.PORTFOLIO)を使う。旧実装はここが一貫してRSIPlusを返すため、
            //   V6NativeOptimizer.chooseAlgorithm側の同種の変更だけでは実際のAUTOフローに反映されず、
            //   本来の狙い（長時間AUTOでの基盤/役割多様化）が発現しない欠陥があった。
            else -> OptimizationPlan.Portfolio(s)
        }
    }

    /** [3.230.0/停滞ウォッチドッグの分離] 「フェーズ公平猶予」と「真の頭打ち検知」を分離した判定を
     *  純関数として抽出（壁時計に依存する周囲のコードから切り離してユニットテスト可能にする）。
     *  旧実装は `max(lastBestImproveMs, lastPhaseChangeMs)` を単一のstallMs(=予算9/10、300s予算で270s)
     *  と比較しており、20〜90秒間隔で頻発するフェーズ遷移のたびにタイマがリセットされ続け、270秒という
     *  長い閾値には実質的に一度も到達し得なかった（改善が本当に無くても検知できない）。
     *  本関数は two-condition AND: ①現フェーズ自身が phaseGraceMs 以上経過（起動直後の誤検知防止のみ）
     *  ②最終改善から effStall 以上経過（フェーズ遷移でリセットしない＝真の頭打ち）。 */
    internal fun watchdogStagnationFired(
        now: Long, startMs: Long, minRunMs: Long,
        lastPhaseChangeMs: Long, phaseGraceMs: Long,
        lastBestImproveMs: Long, effStall: Long,
    ): Boolean =
        now - startMs > minRunMs &&
            now - lastPhaseChangeMs > phaseGraceMs &&
            now - lastBestImproveMs > effStall

    /** [3.281.0/停滞レビューA] ウォッチドッグの実効停滞閾値の選択を純関数として抽出（ユニットテスト用）。
     *  従来: 「bestHard<=hardFloor(構造的covU床) かつ 非covU HARD=0」のときだけ短い stallHardMs＝
     *  c3n が1件でも残ると常に stallMs(=予算9/10)で、300s予算では発火に270s必要＝**構造的に発火不能**
     *  だった（実機ログ: 125s以降150s無改善のまま探索275sを完走・追加精製0）。covU には structuralHardFloor
     *  という「解けないHARD」の静的判定があるのに c3n には無い非対称が根本原因。
     *  新規: 残る非covU HARD が **c3n のみ**で、かつ 3.280.0 ForbiddenDiag が全 run の塞がりを**証明**した
     *  （c3nWallProven）場合も plateau とみなし stallHardMs へ移行する。証明つきのため誤発火なし・
     *  早期終了は時間/電池の節約のみで品質は keep-best が担保（退化不能）。 */
    internal fun effectiveStallMs(
        bestHard: Int, hardFloor: Int, nonCovUHard: Int, nonCovUAllC3n: Boolean,
        c3nWallProven: Boolean, stallHardMs: Long, stallMs: Long,
    ): Long {
        val basePlateau = bestHard <= hardFloor && nonCovUHard == 0
        val c3nWallPlateau = nonCovUHard > 0 && nonCovUAllC3n &&
            bestHard <= hardFloor + nonCovUHard && c3nWallProven
        return if (basePlateau || c3nWallPlateau) stallHardMs else stallMs
    }

    fun getAlgorithmLabel(seconds: Int): AlgorithmLabel = when {
        seconds <= 10 -> AlgorithmLabel("⚡", "高速", "短時間でサッと作成", "v5")
        seconds <= 30 -> AlgorithmLabel("★", "標準", "速さと品質のバランス", "v5")
        // [実機指摘] 31〜210s は複合（違反集中→研磨）に統一。表示ラベルもプランと同期。
        seconds <= 210 -> AlgorithmLabel("🧬", "学習+研磨", "RSI違反集中→ALNS研磨", "RSI→ALNS")
        // [3.266.0] 表示ラベルもプラン(Portfolio)と同期。同型RSI++クローン8本でなく、ALNS/RSI/RSI++が
        //   異なる基盤・役割から非同期に探索し、停滞/重複を検知して再配属する。
        seconds <= 300 -> AlgorithmLabel("🌈", "究極(5分)", "ALNS/RSI/RSI++ 異種並列探索(適応epoch)", "PORTFOLIO")
        else -> AlgorithmLabel("🌈", "究極", "最大限の品質 (${seconds / 60}分)", "PORTFOLIO拡張")
    }

    suspend fun handleSimple(state: MagiState, allowImpossible: Boolean = false): ActionResult = withContext(Dispatchers.Default) {
        require(state.dayCount > 0) { "対象期間が無効です。終了日を開始日より後の日付にしてください" }
        val gate = confirmDespiteImpossibleWishes(state, allowImpossible)
        if (!gate.allowed) error(gate.message)
        val busy = buildBusyDetail(state, "シフト作成中", mapOf(
            "subtitle" to "初期勤務表を作成中",
            "phaseDesc" to "希望シフトと必要人数を考慮して、担当できるシフトを割り当てています",
            "expectedSec" to "< 1 秒",
            "estimatedIter" to "~800 回",
        ))
        val res = GreedyMirrorScheduler.generate(state)
        val logs = gate.logs + res.report.logs + MirrorLog(tag = "MAGI_GenerateInitial", message = "簡易作成 完了 HARD=${res.report.hard} total=${res.report.total}")
        ActionResult(res.schedule, res.report.copy(logs = logs), "simple", busy, logs)
    }

    /**
     * [初期解生成(賢い版)] 希望シフト→C1(窓の要件)→日別必要人数→個人下限→残り埋め の順で
     * 初期解を組み立てる`SmartInitialScheduler`のポート。本最適化(SA/ALNS)へは続けず、
     * 生成した下書きをそのまま返す（続けての本最適化は既存の「勤務表をつくる」が担当）。
     */
    suspend fun handleSmartInitial(state: MagiState, allowImpossible: Boolean = false): ActionResult = withContext(Dispatchers.Default) {
        require(state.dayCount > 0) { "対象期間が無効です。終了日を開始日より後の日付にしてください" }
        val gate = confirmDespiteImpossibleWishes(state, allowImpossible)
        if (!gate.allowed) error(gate.message)
        val busy = buildBusyDetail(state, "初期解を作成中", mapOf(
            "subtitle" to "初期解を作成中",
            "phaseDesc" to "希望シフトとC1(窓の要件)を優先し、次に必要人数・個人下限を考慮しています",
            "expectedSec" to "< 1 秒",
            "estimatedIter" to "~800 回",
        ))
        val res = SmartInitialScheduler.generate(state)
        val logs = gate.logs + res.report.logs + MirrorLog(tag = "MAGI_GenerateInitial", message = "初期解生成 完了 HARD=${res.report.hard} total=${res.report.total}")
        ActionResult(res.schedule, res.report.copy(logs = logs), "smart_initial", busy, logs)
    }

    suspend fun handleCheck(state: MagiState, schedule: Array<IntArray> = state.schedule.toIntArray2D()): ActionResult = withContext(Dispatchers.Default) {
        val busy = buildBusyDetail(state, "違反チェック中", mapOf(
            "subtitle" to "違反チェック",
            "phaseDesc" to "勤務表のすべての違反を確認しています（最適化結果は変更しません）",
            "expectedSec" to "< 0.1 秒",
            "estimatedIter" to "評価のみ （反復なし）",
        ))
        val report = UnifiedViolationChecker.check(state, schedule)
        val hc = report.hard
        val sc = report.soft
        val logs = report.logs + MirrorLog(tag = "UnifiedCheck", message = if (report.total == 0) "違反なし ✓" else "HARD ${hc}件・品質 ${sc}件")
        ActionResult(schedule.copy2D(), report.copy(logs = logs), "check", busy, logs)
    }

    suspend fun handleOptimize(
        state: MagiState,
        schedule: Array<IntArray> = state.schedule.toIntArray2D(),
        secondsRaw: Int,
        workers: Int = Runtime.getRuntime().availableProcessors().coerceIn(1, 8),
        softPolish: Boolean = false,
        requestedAlgorithm: V6Algorithm = V6Algorithm.AUTO,
        allowImpossible: Boolean = false,
        onProgress: (String, ViolationReport?, Long, Long) -> Unit = { _, _, _, _ -> },
    ): ActionResult = withContext(Dispatchers.Default) {
        require(state.dayCount > 0) { "対象期間が無効です。基本情報で終了日を開始日より後にしてください" }
        // タイムアウト上限10分(600s)を厳守（業務指示）。呼び出し側に依らずここで頭打ちにする。
        val seconds = secondsRaw.coerceIn(1, MAX_OPTIMIZE_SEC)
        val gate = confirmDespiteImpossibleWishes(state, allowImpossible)
        if (!gate.allowed) error(gate.message)
        // [最終番兵用] 入力の評価を保持。万一パイプラインが入力より悪い結果を出した場合に復帰する（多重防御）。
        val baseProblem = cachedProblem(state)
        val normInput = normalizeSchedule(schedule, baseProblem)
        val inputReport = UnifiedViolationChecker.check(state, normInput)
        val label = getAlgorithmLabel(seconds)
        val plan = optimizationPlan(seconds)
        val busy = buildBusyDetail(state, label.name, mapOf(
            "subtitle" to label.desc,
            "phaseDesc" to "${label.tech} をNativeエンジンで実行中",
            "expectedSec" to "約 ${seconds} 秒",
            "estimatedIter" to "問題サイズに応じて自動調整",
        ))
        // [review #1] When the user explicitly picks an algorithm (not AUTO), honor it with the
        // full requested budget. AUTO keeps the time-budget-based plan. [review #3] postPolish=false
        // so optimize() does NOT polish internally — the single post chain below owns polishing.
        val opts = if (requestedAlgorithm != V6Algorithm.AUTO) {
            V6OptimizerOptions(requestedAlgorithm, seconds.coerceAtLeast(1), workers, softPolish, restarts = 2, postPolish = false)
        } else when (plan) {
            is OptimizationPlan.V5 -> V6OptimizerOptions(V6Algorithm.V5, plan.seconds, workers, softPolish, restarts = 1, postPolish = false)
            is OptimizationPlan.ALNS -> V6OptimizerOptions(V6Algorithm.ALNS, plan.seconds, workers, softPolish, restarts = plan.restarts, postPolish = false)
            is OptimizationPlan.RSIThenALNS -> V6OptimizerOptions(V6Algorithm.RSI, plan.rsiSec, workers, softPolish, restarts = plan.alnsRestarts, postPolish = false)
            is OptimizationPlan.Portfolio -> V6OptimizerOptions(V6Algorithm.PORTFOLIO, plan.seconds, workers, softPolish, restarts = 2, postPolish = false)
        }
        val optsR = opts.copy(rectSwap = V6LateOperators.optFlagBool(state, "rectSwap", true))   // [HF532移植] optFlags.rectSwap 既定ON
        // [review: 600s budget] optimize() + runPostOptimization() を一つの予算で管理する。
        // 後処理は元々 deadline も progress も持たず、optimize が予算を使い切った後も走り続け、
        // 合計が予算を大きく超過していた(実機44分/予算600s)。ここで全体に hardDeadline を張り、
        // coroutine キャンセル(計算を止める)と束ねて後処理へ伝播する。
        val startMs = System.currentTimeMillis()
        val budgetMs = seconds.toLong() * 1000L
        val hardDeadlineMs = startMs + budgetMs
        // [仮説数上限撤廃・ユーザー指示] 診断ログ表示専用。実際の dispatch は V6NativeOptimizer.optimize()
        //   内の同名関数呼出が担うため、ここも同じ V6NativeOptimizer.hypothesisCount から導出し独立再計算による
        //   表示/実挙動の乖離を防ぐ（3.212.0 と同じ設計原則）。
        val effHypotheses = V6NativeOptimizer.hypothesisCount(workers)

        // ----- 停滞早期脱出ウォッチドッグ -----
        // 進捗ストリームから「最良解(hard→total→重み付きの辞書順)」の更新時刻を追跡し、一定時間
        // (stallMs)改善が無ければ予算上限を待たずに終了する。改善が続く限り絶対に止めない＝品質は不変。
        // フェーズ遷移でもタイマをリセットするので、各フェーズには必ず stallMs 分の猶予がある。
        // 狙い: 「データ上 HARD=0 にできない / 既に研磨が頭打ち」の局面で、最大10分の予算を無駄に
        // 回し続ける（=ユーザーには『ハングして常に10分かかる』と映る）問題を解消する。
        // [早期脱出方針] 実機ログで停滞検知が予算上限とほぼ同時(301s)に発火＝時間がほぼ節約できていなかった。
        //   停滞許容を短縮して「無改善なら早く返す」方針へ。globalBest は生スコア管理のため早期終了でも品質は不変
        //   （最後の改善時刻でタイマをリセット＝改善が続く限り止めない・フェーズ遷移でもリセットで猶予確保）。
        val minRunMs = (budgetMs / 6).coerceIn(8_000L, 45_000L)   // 最初の猶予（早すぎる停止を防ぐ）
        // [5分強化] HARD>0（=未配布・配れない）は最優先で解消すべき失敗状態。予算の大半を使って多様化
        //   （多仮説＋HF80 戦略的振動）で HARD クリアを試みる。旧 budgetMs/6(=300s予算で50s) は早すぎ、
        //   実機ログで HARD=1 のまま 50s で早期終了し残り 250s を捨てていた。→ budgetMs*9/10(=270s)。
        //   改善が続く限り lastBestImproveMs がリセットされるので、生産的な探索は自然に締切まで走る。
        val stallMs = (budgetMs * 9 / 10).coerceAtLeast(20_000L)
        // [5分圧縮] HARD=0到達後（=配布可・残りは研磨のみ）は頭打ちをより早く検知して終了（plateauなので品質は不変）。
        val stallHardMs = (budgetMs / 8).coerceAtLeast(15_000L)   // 5分予算→37.5s
        // [賢い早期脱出] 証明可能に解消不能な「データ起因HARD」の下限（report.hard と同単位）。
        //   ＝有資格者を全員そのシフトに就けても埋まらない席（構造的covU）。どう探索しても消えない HARD なので、
        //   HARD がこの下限まで到達したら「HARD=0 到達」と同じく頭打ち(plateau)とみなし短い stallHardMs へ移行して
        //   残り予算を SOFT 制約の研磨に充てる（データ起因HARDを配慮しつつソフト研磨へ移行）。
        //   下限超のHARD（解ける可能性がある）は従来どおり stallMs(=予算9/10)でしっかり粘るため退行しない。
        //   [修正] 旧版は detectImpossibleWishes().size を下限にしていたが、監査#11②で実現不能希望は pref から
        //     対称除外＝HARD寄与0のため下限にならず、逆に「解けるHARD」を早々に諦める誤りだった。構造的covUへ是正。
        //   構造(assignability/need)のみ依存で最適化中に不変＝一度だけ算出する。
        val hardFloor = try { V6SanityPort.structuralHardFloor(state) } catch (_: Exception) { 0 }
        // [レビュー#9 3.213.0→3.230.0で本格分離] 「最良改善」と「フェーズ遷移」の時計を分離。
        //   [3.230.0/ドッグフーディングで発見・修正] 3.213.0時点では両者を max() で合成していたため、
        //   stallMs=270s(予算9/10)という長い閾値が、20〜90秒間隔で頻発するフェーズ遷移
        //   （RSI各ラウンド・ALNS各restart等）のたびにリセットされ続け、実質的に一度も発火し得ない
        //   状態だった（実機ログでPhase1完了直後から270秒以上一切改善が無いまま予算を使い切る事例を
        //   確認）。フェーズ遷移は「今始まったばかりのフェーズを即座に打ち切らない」ための短い個別
        //   猶予(phaseGraceMs、下記)としてのみ機能させ、「本当に改善が無い時間」は lastBestImproveMs
        //   単独で計測する（フェーズが何回切り替わっても改善が無ければ着実に積み上がる）。
        val lastBestImproveMs = java.util.concurrent.atomic.AtomicLong(startMs)
        val lastPhaseChangeMs = java.util.concurrent.atomic.AtomicLong(startMs)
        val stagnationFired = java.util.concurrent.atomic.AtomicBoolean(false)
        // [停滞時間のログ出力] 発火の瞬間に「何ms無改善だったか」を記録する（ログ側で再計算すると
        //   後処理(runPostOptimization)の所要時間が混入し、実際に判定へ使った値とズレるため）。
        val stagnationDurationMs = java.util.concurrent.atomic.AtomicLong(-1)
        val bestHard = java.util.concurrent.atomic.AtomicInteger(Int.MAX_VALUE)   // 並列ワーカーから読むため atomic
        // [hardFloor 精度] best の「非covU HARD」(groupViol/pref/c3n=解けるHARD)件数。hardFloor は構造的covU
        //   のみの下限なので、`bestHard<=hardFloor` だけだと、担当不可の過配置(groupViol)が covU を構造下限より
        //   見かけ上へこませたケースで、解ける groupViol が残っているのに短い stallHardMs へ早期移行してしまう。
        //   非covU HARD が 0（＝残るHARDが構造的covUのみ）を追加条件にし、上記コメント(214行)の設計意図と一致させる。
        val bestNonCovUHard = java.util.concurrent.atomic.AtomicInteger(Int.MAX_VALUE)
        // [3.281.0/停滞レビューA] c3n構造壁の動的床（covU の structuralHardFloor と対）。
        //   残る非covU HARD が c3n のみ、かつ ForbiddenDiag(3.280.0) が全 run の塞がりを証明したら、
        //   その c3n は「解けないHARD」＝plateau として stallHardMs へ移行できる。診断(~20ms)は
        //   「停滞が stallHardMs を超えた後・best 世代ごとに一度だけ」遅延実行しキャッシュする。
        val bestNonCovUAllC3n = java.util.concurrent.atomic.AtomicBoolean(false)
        val bestVersion = java.util.concurrent.atomic.AtomicInteger(0)
        val c3nWallCheckedVersion = java.util.concurrent.atomic.AtomicInteger(-1)
        val c3nWallResult = java.util.concurrent.atomic.AtomicBoolean(false)
        var bTotal = Int.MAX_VALUE; var bWeighted = Double.MAX_VALUE; var lastPhase = ""
        val progressLock = Any()   // [競合解消] 並列ワーカーから呼ばれる best 追跡の read-modify-write を直列化
        val progressWatch: (String, ViolationReport?, Long, Long) -> Unit = { phase, report, iters, elapsed ->
            synchronized(progressLock) {
                val base = phase.substringAfter("/ ").trim().ifEmpty { phase }   // 「仮説N本探索中 / 」接頭辞を除去
                if (base != lastPhase) { lastPhase = base; lastPhaseChangeMs.set(System.currentTimeMillis()) }
                if (report != null) {
                    val h = report.hard; val t = report.total; val wgt = report.weightedScore
                    val bh = bestHard.get()
                    val improved = h < bh || (h == bh && t < bTotal) || (h == bh && t == bTotal && wgt < bWeighted - 1e-6)
                    if (improved) {
                        bestHard.set(h); bTotal = t; bWeighted = wgt; lastBestImproveMs.set(System.currentTimeMillis())
                        // 非covU HARD(=解けるHARD)件数を best と同時に捕捉（stallHardMs 早期移行の判定に使う）。
                        val gv = report.breakdown["groupViol"] ?: 0
                        val pf = report.breakdown["pref"] ?: 0
                        val c3n = report.breakdown["c3n"] ?: 0
                        bestNonCovUHard.set(gv + pf + c3n)
                        // [3.281.0/A] 非covU HARD が c3n のみか（c3n構造壁チェックの適用条件）＋best世代を進める
                        //   （世代が変わると c3n壁キャッシュは無効化＝新しい best 盤面で再証明する）。
                        bestNonCovUAllC3n.set(gv == 0 && pf == 0 && c3n > 0)
                        bestVersion.incrementAndGet()
                    }
                }
            }
            onProgress(phase, report, iters, elapsed)   // ユーザーコールバックはロック外で呼ぶ
        }
        // [後処理予約] 探索が予算を使い切ると後処理(平準化/fair等のkeep-best研磨)が時間切れ(実機8ms)になる。
        //   末尾に postReserveMs を予約し、探索は searchDeadlineMs で止め、後処理は hardDeadlineMs まで走らせる。
        //   stall早期終了時は探索が早く返るので後処理は自然に余裕を得る＝無改善の末尾だけを後処理へ回す。
        val postReserveMs = (budgetMs / 12).coerceIn(8_000L, 25_000L)
        val searchDeadlineMs = (hardDeadlineMs - postReserveMs).coerceAtLeast(startMs + minRunMs)
        // [3.230.0] 現フェーズ自身にも与える短い個別猶予（フェーズ開始直後の誤検知防止のみが目的。
        //   真の頭打ち検知は effStall/lastBestImproveMs が単独で担う）。stallMs(=予算9/10)のような
        //   長さは不要で、「フェーズがまだ何も試していない」瞬間を除外できれば十分。
        val phaseGraceMs = (budgetMs / 40).coerceIn(2_000L, 15_000L)
        // [3.281.0/A] c3n構造壁の遅延証明。best 世代ごとに一度だけ ForbiddenDiag を実行しキャッシュする。
        //   呼出条件（c3nのみ残存＋停滞がstallHardMs超）は呼び出し側でゲート済み＝停滞局面でしか走らない。
        //   liveBest は publishLiveBest(CAS, better()単調) のグローバル最良スナップショット。best報告との
        //   僅かな世代ズレはあり得るが、本判定は「停滞閾値の選択」にのみ作用（採否/keep-bestとは無関係）で
        //   誤っても時間配分が変わるだけ＝品質は不変。並行呼出は同一結果を二重計算するだけで無害。
        val c3nWallProven = {
            val v = bestVersion.get()
            if (c3nWallCheckedVersion.get() != v) {
                val board = V6NativeOptimizer.liveBest
                val proven = if (board == null) false else try {
                    val arr = Array(board.size) { r -> IntArray(board[r].size) { c -> board[r][c] } }
                    val diag = V6PortAnalyzer.diagnoseForbiddenRuns(state, arr)
                    diag.hasRuns && diag.allBlocked
                } catch (_: Exception) { false }
                c3nWallResult.set(proven)
                c3nWallCheckedVersion.set(v)
            }
            c3nWallResult.get()
        }
        val shouldStop = {
            val now = System.currentTimeMillis()
            // [賢い早期脱出] bestHard が「解消不能な下限(hardFloor=構造的covU)」以下＝解けるHARDは出し切った状態。
            //   この時点で残るのは構造的に埋まらない covU 席のみなので、HARD=0 と同様に短い猶予で頭打ち終了。
            //   ただし非covU HARD(groupViol/pref/c3n=解ける可能性あり)が残る間は long stall で粘る（214行の設計意図）。
            //   担当不可過配置が covU を構造下限より見かけ上へこませ、bestHard<=hardFloor でも groupViol が残るケースを防ぐ。
            //   hardFloor=0 かつ 非covU HARD=0（＝bestHard==0）なら従来の「bestHard==0」と完全一致＝挙動不変。
            // [3.281.0/A] 追加: 残る非covU HARD が c3n のみで ForbiddenDiag が全 run の塞がりを証明した場合も
            //   plateau（解けないHARD）として stallHardMs へ移行（実機ログの「c3n=1のまま150s無改善でも
            //   270s閾値のため発火不能」を解消）。診断は停滞が stallHardMs を超えてから遅延実行（~20ms/世代1回）。
            val nonCovU = bestNonCovUHard.get()
            val wall = nonCovU > 0 && bestNonCovUAllC3n.get() &&
                bestHard.get() <= hardFloor + nonCovU &&
                now - lastBestImproveMs.get() > stallHardMs && c3nWallProven()
            val effStall = effectiveStallMs(
                bestHard.get(), hardFloor, nonCovU, bestNonCovUAllC3n.get(), wall, stallHardMs, stallMs,
            )
            when {
                now >= searchDeadlineMs || !isActive -> true
                watchdogStagnationFired(now, startMs, minRunMs, lastPhaseChangeMs.get(), phaseGraceMs, lastBestImproveMs.get(), effStall) -> {
                    stagnationDurationMs.set(now - lastBestImproveMs.get())
                    stagnationFired.set(true); true
                }
                else -> false
            }
        }
        // 後処理(runPostOptimization)用の別締切。stall では止めず予約枠 hardDeadlineMs まで使える。
        val postShouldStop = { System.currentTimeMillis() >= hardDeadlineMs || !isActive }

        val tFirst0 = System.currentTimeMillis()
        val first = V6NativeOptimizer.optimize(state, schedule, optsR, shouldStop, progressWatch)
        val tFirst1 = System.currentTimeMillis()
        // [review #5] RSIThenALNS は RSI(first)→ALNS(chained) を同一予算内で直列実行する。各段は
        //   postPolish=false（optsR で統一）なので段内 polish は走らない。最終 polish は段ではなく
        //   下流の runPostOptimization() に一度だけ集約しているため、ここでの二重 polish は意図的に無い。
        val chained = if (requestedAlgorithm == V6Algorithm.AUTO && plan is OptimizationPlan.RSIThenALNS && !shouldStop()) {
            V6NativeOptimizer.optimize(state, first.schedule, optsR.copy(algorithm = V6Algorithm.ALNS, totalBudgetSec = plan.alnsSec), shouldStop, progressWatch)
        } else first
        val tChain1 = System.currentTimeMillis()

        // [3.268.0/エリート統合] 旧「エリート再結合(Path Relinking)」を置換。8役の最終1解だけでなく、
        //   非同期適応ポートフォリオが全epochから保存した品質/距離/橋渡しエリート(lastFusionElites)を
        //   統合する: 双方向Path Relinking＋不一致セル限定Fusionを同じ期限で実行。PORTFOLIO以外の
        //   アルゴリズムではlastFusionElitesが空のため、旧来のlastAlternatives(最大3件)にフォールバック
        //   する（挙動は変わらず、対象が無ければ即no-op）。
        val integrationBudgetMs = (budgetMs / 20).coerceIn(6_000L, 16_000L)
        // [監査修正を継承] 旧relinkと同じ理由でintegrationもhardDeadlineMs-postReserveMs/2で止め、
        //   後処理(fair/weekly/c41s 研磨)へ予約枠の半分を必ず残す（両者 keep-best＝退化なし）。
        val integrationDeadline = minOf(hardDeadlineMs - postReserveMs / 2, System.currentTimeMillis() + integrationBudgetMs)
            .coerceAtLeast(System.currentTimeMillis())
        val integrationStop = { System.currentTimeMillis() >= integrationDeadline || !isActive }
        val archivedElites = V6NativeOptimizer.lastFusionElites
        val fusionElites = if (archivedElites.isNotEmpty()) archivedElites else {
            V6NativeOptimizer.lastAlternatives.mapIndexed { index, sched ->
                AdaptiveElite(
                    schedule = sched.copy2D(),
                    report = UnifiedViolationChecker.check(state, sched),
                    role = HypothesisEpochRole.ELITE_RELINK,
                    worker = index,
                    epoch = 0,
                    bridge = false,
                )
            }
        }
        val integrated = EliteIntegrationPolish.apply(
            state = state,
            rootSchedule = chained.schedule,
            elites = fusionElites,
            shouldStop = integrationStop,
            deadlineMs = integrationDeadline,
        )
        val tIntegration1 = System.currentTimeMillis()

        val post = V6HotfixPasses.runPostOptimization(
            state, integrated.schedule, label.tech,
            shouldStop = postShouldStop,
            onPhase = { phase -> progressWatch(phase, null, System.currentTimeMillis() - startMs, budgetMs) },
            deadlineMs = hardDeadlineMs,   // [残予算ガード] HF66 が後段パスを押し出さないよう全体締切を渡す
        )
        val tPost1 = System.currentTimeMillis()
        // [高精度化/予算残の活用] 後処理予約枠(budget/12, 8〜25s)は後処理が早期にフィックスポイント到達すると
        //   大半が未使用のまま返っていた(実機: 予約25s中 実使用0.45s＝約24.5s廃棄)。残り5s以上かつ違反が残る場合、
        //   最終盤面を起点に keep-best の追加精製(ALNS)へ回す。runAlns は入力比番兵つき＝結果は post 以上を保証。
        //   停滞検知(stagnationFired)による早期終了時はスキップ＝「無改善なら早く返す」方針を壊さない。
        var refSched = post.schedule
        var refReport = post.report
        var extraLog = emptyList<MirrorLog>()
        run {
            // [監査(3e)] 上限は後処理予約枠(postReserveMs, 8〜25s)＝「未使用の予約を再利用する」設計意図に固定。
            //   全予算走行なら残り≒予約枠で従来どおり。N4 早期脱出等 stagnationFired 以外の早期復帰では
            //   残りが数分になり得るが、その節約(電池/熱)を ExtraRefine が食い潰さないよう予約枠でキャップする。
            val extraMs = minOf(hardDeadlineMs - tPost1, postReserveMs)
            if (extraMs >= 5_000 && isActive && !stagnationFired.get() && post.report.total > 0) {
                val extraDeadline = tPost1 + extraMs
                val extraStop = { System.currentTimeMillis() >= extraDeadline || !isActive }
                // [他の案の保全] optimize() は入口で lastAlternatives を空にするため、本走行ポートフォリオが
                //   保持した「他の案」を退避し、追加精製の後に復元する（ViewModel の captureAlternatives は
                //   handleOptimize 復帰後に読むため、退避しないと他の案が消える）。
                val savedAlts = V6NativeOptimizer.lastAlternatives
                val savedFusionElites = V6NativeOptimizer.lastFusionElites
                // [敵対的レビュー3.212.0、仮説数上限撤廃後も維持] 微小予算(5〜25s)の追加精製は本走行と異なり
                //   仮説数を workers まで増やすと悪化しうる（チェーン毎の固定費=入口hf67+フルcheck×2+
                //   nativeハンドル生成 が小予算を侵食し、3.102.0が回収した予約枠が高worker設定で再び浪費
                //   される）→ ここだけ意図的に MAX_HYPOTHESES(5) までにキャップ＝旧来の5×1構成を維持。
                val extra = V6NativeOptimizer.optimize(
                    state, post.schedule,
                    optsR.copy(algorithm = V6Algorithm.ALNS, totalBudgetSec = (extraMs / 1000L).toInt().coerceAtLeast(5),
                        workers = optsR.workers.coerceAtMost(V6NativeOptimizer.MAX_HYPOTHESES)),
                    extraStop, progressWatch,
                )
                V6NativeOptimizer.restoreAlternatives(savedAlts)
                V6NativeOptimizer.restoreFusionElites(savedFusionElites)
                val imp = extra.report.hard < post.report.hard ||
                    (extra.report.hard == post.report.hard && extra.report.total < post.report.total) ||
                    (extra.report.hard == post.report.hard && extra.report.total == post.report.total && extra.report.weightedScore < post.report.weightedScore - 1e-6)
                if (imp) {
                    refSched = extra.schedule; refReport = extra.report
                    extraLog = listOf(
                        MirrorLog(level = "I", tag = "ExtraRefine",
                            message = "予算残${extraMs / 1000}sで追加精製: HARD ${post.report.hard}→${extra.report.hard} / total ${post.report.total}→${extra.report.total}"),
                        // [監査(3c)/N3と同型] ログ末尾の UnifiedCheck/違反詳細は「精製前の盤面」の診断のまま残るため、
                        //   採用した勤務表の集計を明示して件数の取り違えを防ぐ。
                        MirrorLog(level = "I", tag = "UnifiedCheck",
                            message = "採用した勤務表の集計: HARD=${extra.report.hard} 合計=${extra.report.total}（直近のUnifiedCheck行・違反詳細は追加精製前の盤面の診断）"),
                    )
                }
            }
        }
        val tExtra1 = System.currentTimeMillis()
        val overBudget = tExtra1 - startMs > budgetMs
        val timingLog = MirrorLog(
            level = if (overBudget) "W" else "I",
            tag = "TIME",
            message = "総${(tExtra1 - startMs) / 1000.0}s (予算${seconds}s${if (overBudget) " 超過" else ""}): " +
                "探索${(tFirst1 - tFirst0) / 1000.0}s + 連鎖${(tChain1 - tFirst1) / 1000.0}s + 統合${(tIntegration1 - tChain1) / 1000.0}s + 後処理${(tPost1 - tIntegration1) / 1000.0}s + 追加精製${(tExtra1 - tPost1) / 1000.0}s " +
                "/ workers設定${workers} 実効仮説${effHypotheses}",
        )
        val integrationLog = integrated.logs
        // [3.283.0/ログ強化] ウォッチドッグの内部状態を非発火時も1行で可視化。旧: 発火時の EarlyStop 行のみで、
        //   「なぜ発火しなかったか」（実効閾値の選択・探索終了時点の停滞量・c3n壁診断の結果）がログから読めず、
        //   実機ログ解析がコード推論頼みだった（2026-12 ログの150s無改善×発火なしの切り分けに実際に必要だった情報）。
        //   読取専用・表示のみ。
        val watchdogLog = run {
            val lastImp = lastBestImproveMs.get()
            val endStallS = (tChain1 - lastImp).coerceAtLeast(0L) / 1000
            val nonCovU = bestNonCovUHard.get()
            val kind = when {
                bestHard.get() <= hardFloor && nonCovU == 0 -> "plateau=短${stallHardMs / 1000}s"
                c3nWallResult.get() && bestNonCovUAllC3n.get() -> "c3n壁=短${stallHardMs / 1000}s"
                else -> "通常=長${stallMs / 1000}s"
            }
            val wallNote = if (c3nWallCheckedVersion.get() >= 0)
                "・c3n壁診断=${if (c3nWallResult.get()) "壁と証明" else "壁ではない（崩す手が実在）"}" else ""
            listOf(MirrorLog(
                level = "I", tag = "Watchdog",
                message = "停滞監視: 最終改善=経過${((lastImp - startMs) / 1000).coerceAtLeast(0)}s・" +
                    "探索終了時の停滞${endStallS}s・実効閾値($kind)・発火=${if (stagnationFired.get()) "あり" else "なし"}$wallNote",
            ))
        }
        val stagnationLog = if (stagnationFired.get()) listOf(MirrorLog(
            level = "I", tag = "EarlyStop",
            message = "停滞検知: 改善が無いため早期終了（予算${seconds}s中 ${(tPost1 - startMs) / 1000}sで停止・" +
                "停滞${stagnationDurationMs.get() / 1000}s無改善・解は最良を維持）" +
                // [3.281.0/A] c3n構造壁（証明つき）が短い閾値への移行理由だった場合はそれを明示。
                (if (c3nWallResult.get() && bestNonCovUAllC3n.get()) "（残る必須=禁止連続はForbiddenDiagが構造的な壁と証明済み）" else ""),
        )) else emptyList()
        // [最終番兵/多重防御] 全段 keep-best のため通常は発火しないが、万一パイプラインが入力より
        // 悪い結果を返した場合は入力を採用し退化を防ぐ（checkResultWorse をここで配線）。
        val regression = checkResultWorse(inputReport, refReport)
        val finalSched = if (regression != null) normInput else refSched
        val finalReport = if (regression != null) inputReport else refReport
        val sentinelLog = if (regression != null) listOf(
            MirrorLog(
                level = "W", tag = "Sentinel",
                message = "後処理結果が入力より悪化を検知したため入力を採用しました（多重防御）: $regression",
            ),
            // [N3] ログ末尾には棄却盤面(post)の UnifiedCheck/診断行が履歴として残るため、
            //   採用した勤務表の集計を明示して読者の取り違え（例: covU詳細と件数の不一致に見える）を防ぐ。
            MirrorLog(
                level = "I", tag = "UnifiedCheck",
                message = "採用した勤務表の集計: HARD=${inputReport?.hard} 合計=${inputReport?.total}（直近のUnifiedCheck行・違反詳細は棄却盤面の診断）",
            ),
        ) else emptyList()
        // [ネイティブ加速 Stage2/3] C++フル評価器と Kotlin Evaluator を採用盤面で照合し、結果を診断ログへ。
        //   照合は read-only（採用結果に影響なし）。SA チャンク(Stage3)の使用可否・番兵発火もここで可視化。
        val nativeLog = run {
            // [照合トグル] OFF=純ネイティブ（起動時パリティも含め Kotlin 照合を一切行わない）。native未ロード時は従来どおり。
            val parityOff = NativeBridge.available && NativeGate.userEnabled && !NativeGate.parityCheckEnabled
            val parity = if (parityOff) null else runCatching { NativeEval.parityCheck(baseProblem, finalSched) }.getOrNull()
            // フル評価パリティ不一致もゲートを閉じる（以後の実行で SA チャンクを使わない＝退化）。
            if (parity?.match == false) NativeGate.disable("フル評価パリティ不一致")
            val gate = if (NativeGate.enabled) "" else "／番兵発火→Kotlinへ退化: ${NativeGate.reason}"
            // [表示バグ修正] 有効/無効は usable（番兵×ユーザートグル×ロード）で判定する。旧: enabled（番兵のみ）
            //   参照のため、設定トグル OFF の実行でも「有効」と表示され A/B ログの判読を妨げていた。
            val searchState = when {
                NativeGate.usable && parityOff -> "有効(照合OFF・純ネイティブ)"
                NativeGate.usable -> "有効(SA＋LAHC＋ALNS＋研磨チャンク)"
                !NativeGate.userEnabled -> "無効(設定トグルOFF)"
                else -> "無効"
            }
            MirrorLog(
                level = if (parityOff || parity?.match == false || !NativeGate.enabled) "W" else "I",
                tag = "NativeBridge",
                message = when {
                    parityOff -> "ネイティブ加速: Kotlin照合OFF＝純ネイティブ（検証/ベンチ用・誤結果の可能性）・ネイティブ探索=$searchState$gate"
                    parity == null -> "ネイティブ加速: 未ロード（Kotlin実行・機能差なし）"
                    parity.match -> "ネイティブ加速: C++評価器パリティ一致 (hard=${parity.kotlinHard} soft=${parity.kotlinSoft} / C++ ${parity.nativeUs}µs vs Kotlin ${parity.kotlinUs}µs=単発・JNI往復込みの参考値)・ネイティブ探索=$searchState$gate"
                    else -> "ネイティブ加速: パリティ不一致のためネイティブ経路は使いません (C++ hard=${parity.nativeHard}/soft=${parity.nativeSoft} ≠ Kotlin hard=${parity.kotlinHard}/soft=${parity.kotlinSoft})$gate"
                },
            )
        }
        // post.report.logs = [HF80/67/66/70 logs + POST timing + UnifiedViolationChecker logs]。
        // post.logs は post.report.logs の部分集合なので両方足すと重複する → post.report.logs のみ使う。
        val logs = listOf(timingLog, nativeLog) + sentinelLog + integrationLog + extraLog + watchdogLog + stagnationLog + gate.logs + first.phaseLogs + (if (chained !== first) chained.phaseLogs else emptyList()) + post.report.logs
        ActionResult(finalSched, finalReport.copy(logs = logs), "optimize:${label.tech}", busy, logs, post)
    }

    fun checkResultWorse(before: ViolationReport?, after: ViolationReport): String? {
        if (before == null) return null
        return when {
            after.hard > before.hard -> "HARDが悪化しました: ${before.hard} -> ${after.hard}"
            after.total > before.total && after.hard >= before.hard -> "違反総数が悪化しました: ${before.total} -> ${after.total}"
            // [監査修正] hard 同値ガードを追加。旧: after.hard<before.hard(HARD改善)でも total==・weighted悪化で発火し、
            //   HARDが改善した結果を「悪化」と誤判定し悪い入力へ復帰し得た（clause2 と同じ hard>= ガードに揃える）。
            after.hard >= before.hard && after.weightedScore > before.weightedScore && after.total >= before.total -> "重み付きスコアが悪化しました: ${before.weightedScore.toLong()} -> ${after.weightedScore.toLong()}"
            else -> null
        }
    }

    fun buildBusyLogLine(detail: BusyDetail): String = buildString {
        append(detail.algorithm)
        if (detail.subtitle.isNotBlank()) append(" — ").append(detail.subtitle)
        append(" / ").append(detail.problemSize)
        append(" / ").append(detail.constraintCount)
    }
}
