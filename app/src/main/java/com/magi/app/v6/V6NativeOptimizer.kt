package com.magi.app.v6

import com.magi.app.model.MagiState
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext
import kotlinx.coroutines.yield
import kotlin.coroutines.coroutineContext
import kotlin.math.exp
import kotlin.math.max
import kotlin.math.min
import kotlin.math.ln
import kotlin.math.cos
import kotlin.math.sqrt
import kotlin.math.PI
import java.util.Random

/**
 * Native port / fusion of the V6 Web optimizer dispatcher.
 *
 * Web V6 chooses V5 / ALNS / RSI / RSI++ by budget and then runs post-passes
 * (HF66/HF67/HF80 family).  This Kotlin version keeps the same public semantics:
 * AUTO chooses an algorithm by time budget, V5 is parallel SA, ALNS uses destroy/repair
 * multi-restart, RSI focuses on the currently most violated family, and RSI++ chains
 * seed -> hypothesis -> refine -> polish.
 */
enum class V6Algorithm { AUTO, V5, ALNS, RSI, RSI_PLUS, PORTFOLIO }

/** ALNS の受理基準。SA=Boltzmann(従来) / GREAT_DELUGE=時間予定型 Great Deluge（水位以下を受理） /
 *  LAM_ADAPTIVE=Lam-Delosme適応冷却（受理率を目標値に追従させ温度を自己調整。Boltzmann受理を使う）。 */
enum class AcceptMode { SA, GREAT_DELUGE, LAM_ADAPTIVE }

/** ALNS の演算子選択方式。ROULETTE=重み比例(従来) / THOMPSON=Thompson sampling(平滑報酬opWを
 *  事後平均、時間減衰ノイズで探索する確率的選択。停滞しにくく不確実性下で原理的)。 */
enum class OpSelectMode { ROULETTE, THOMPSON }

data class V6OptimizerOptions(
    val algorithm: V6Algorithm = V6Algorithm.AUTO,
    val totalBudgetSec: Int = 300,
    val workers: Int = Runtime.getRuntime().availableProcessors().coerceIn(1, 8),
    val softPolish: Boolean = true,
    val restarts: Int = 2,
    val seed: Long = 0L,
    /** [HF528/532移植] RectSwap2/C1BlockN を RSI 系へ伝播。Web optFlags.rectSwap 既定ON(HF532 恒久ON確定)。 */
    val rectSwap: Boolean = true,
    /** Run the final HF80 epilogue polish inside optimize(). Set false when the caller
     *  (e.g. V6FinalPort.handleOptimize) runs its own post-optimization chain, to avoid
     *  polishing twice. Direct callers keep the default so they still get a polish. */
    val postPolish: Boolean = true,
    /** [HF290 役割分担移植] 探索/精製の温度・摂動倍率。1.0=ベースライン(従来)。>1=探索(高温/大摂動)、<1=精製(低温)。
     *  並列仮説ごとに別の値を割当てて多様化（W0は常に1.0でベースライン保持＝退化防止）。 */
    val explore: Double = 1.0,
    /** ALNS の受理基準。並列仮説の一部に Great Deluge を割当てて受理戦略を多様化（W0は SA でベースライン保持）。 */
    val accept: AcceptMode = AcceptMode.SA,
    /** ALNS の演算子選択方式。並列仮説の一部に Thompson sampling を割当てて選択戦略を多様化（W0は ROULETTE でベースライン保持）。 */
    val opSelect: OpSelectMode = OpSelectMode.ROULETTE,
    /** 局所移動に短期Tabu記憶を適用（直近変更セルの即時復帰を tenure 期間禁止。global最良更新時はアスピレーションで解禁）。
     *  並列仮説の一部にのみ割当て（W0はOFFでベースライン保持）。destroy/repair等の大近傍手は対象外。 */
    val tabu: Boolean = false,
)

data class V6OptimizerResult(
    val schedule: Array<IntArray>,
    val report: ViolationReport,
    val algorithm: V6Algorithm,
    val phaseLogs: List<MirrorLog>,
    val iterations: Long,
    val elapsedMs: Long,
    // [3.335.0/外部レビュー P1] 以下は**この実行の成果物**。旧実装は `lastAlternatives` 等の可変 static を
    //   呼び出し側が返却後に読んでいたため、実行が重なると（WorkManager の REPLACE で旧 Worker が
    //   協調キャンセルを待つ間など）別の実行の値を読み得た。採用盤面は元から返り値で流れるので
    //   **誤った勤務表にはならない**が、「他の案」「残存分析」「ライブ表示」が混ざり得た。
    val alternatives: List<Array<IntArray>> = emptyList(),
    val infeasibleFamilies: Set<String> = emptySet(),
) {
    /** 同上。`AdaptiveElite` は internal なので本体プロパティとして持つ（`copy()` は引き継がない＝
     *  作った側が明示的に載せる）。 */
    internal var fusionElites: List<AdaptiveElite> = emptyList()
}

object V6NativeOptimizer {
    /** [GLS移植] 最良未更新がこの反復数を超えたら GLS penalty を強化（Web版 glsTrigger 既定200）。 */
    private const val GLS_TRIGGER = 200L
    private const val GLS_DECAY_EVERY = 256   // [GLS aging] この kick 数ごとに penalty を減衰し肥大化を防ぐ

    /** [HF290 役割分担移植] 並列仮説の探索/精製プロファイル（温度・摂動の倍率）。
     *  W0=1.0(ベースライン=退化防止)、以降は探索(>1)/精製(<1)を交互に割当てて portfolio を多様化。
     *  [仮説数上限撤廃(3.225.0)後のドッグフーディングで発見・3.228.0で修正] この配列は5要素固定のため
     *  i>=5（3.225.0でworkers設定まで仮説数が増えたことで実際に生成されうる）は全て else 節の
     *  既定値=roleExploreFor(0)と同値に縮退し、役割分担が完全に無効化されていた（仮説5,6,7…は
     *  種(seed)以外ベースラインと区別できないクローン＝「多様性を優先する」という3.225.0自身の
     *  狙いを裏切っていた）。i<5 の既存値は一切変更せず(既存テスト・チューニング結果を保持)、
     *  i>=5 だけ黄金比の低食い違い列(golden-ratio low-discrepancy sequence)で [0.35, 2.4] へ
     *  決定的かつ非周期的に写像する（配列を単に延長・循環させるとi=5%5=0で結局ベースラインに
     *  戻るクローン問題を繰り返すため、周期を持たない生成式を採用）。 */
    private val ROLE_EXPLORE = doubleArrayOf(1.0, 2.0, 0.5, 1.6, 0.6)
    internal fun roleExploreFor(i: Int): Double {
        if (i in ROLE_EXPLORE.indices) return ROLE_EXPLORE[i]
        val frac = (i * 0.6180339887498949) % 1.0
        return 0.35 + frac * (2.4 - 0.35)
    }

    /** [論文活用] 並列仮説で受理戦略を多様化（W0,W1=SA基準 / W2,W4=Great Deluge / W3=Lam適応冷却）。
     *  W0 は常に SA でベースライン保持＝退化防止。
     *  [3.228.0] i<=4 の既存分岐は不変。i>=5 は else節で一律SAに縮退していた（roleExploreFor と同じ
     *  クローン問題）ため、GD/LAM/SAを i%3 で巡回させ実際に多様化する。 */
    internal fun roleAcceptFor(i: Int): AcceptMode = when (i) {
        2, 4 -> AcceptMode.GREAT_DELUGE
        3 -> AcceptMode.LAM_ADAPTIVE
        0, 1 -> AcceptMode.SA
        else -> when (i % 3) {
            0 -> AcceptMode.GREAT_DELUGE
            1 -> AcceptMode.LAM_ADAPTIVE
            else -> AcceptMode.SA
        }
    }

    /** [論文活用] 並列仮説で演算子選択を多様化（W1=Thompson sampling / 他=roulette）。
     *  W0 は常に roulette でベースライン保持＝退化防止。
     *  [3.228.0] i<=4 の既存分岐は不変。i>=5 は一律rouletteに縮退していたため、偶奇でTHOMPSON/ROULETTEを
     *  交互に割当てて多様化する。 */
    internal fun roleOpSelectFor(i: Int): OpSelectMode = when {
        i == 1 -> OpSelectMode.THOMPSON
        i in 0..4 -> OpSelectMode.ROULETTE
        i % 2 == 1 -> OpSelectMode.THOMPSON
        else -> OpSelectMode.ROULETTE
    }

    /** [3.266.0/hypothesis basin diversity] 変更セル数。診断ログとdiversity判定の両方に使う。
     *  実体は AdaptiveEliteArchive.scheduleDistance（唯一の実装）への委譲。両クラスから同じ距離定義を
     *  共有し、アルゴリズムの二重実装（DRY違反）を避ける。 */
    internal fun scheduleDistance(a: Array<IntArray>, b: Array<IntArray>): Int =
        AdaptiveEliteArchive.scheduleDistance(a, b)

    /**
     * [3.266.0/hypothesis basin diversity] 非ベースライン仮説に構造的に異なる吸引域を与える。
     * W0/W4 は現行盤面の完全コピーのまま＝既存の安全フロアは維持される。
     */
    internal fun hypothesisStartFor(
        state: MagiState,
        base: Array<IntArray>,
        index: Int,
        seed: Long,
    ): Array<IntArray> {
        val out = base.copy2D()
        val plan = HypothesisDiversityPolicy.startPlanFor(index)
        if (plan.mode == HypothesisStartMode.BASELINE) return out
        val p = cachedProblem(state)
        val rng = Random(actualSeed(seed) xor 0xD1A5EEDL xor
            (index.toLong() * -0x61c8864680b583ebL))
        repeat(plan.intensity) {
            when (plan.mode) {
                HypothesisStartMode.DAY_REPAIR -> if (p.T > 0) destroyRepairDayAt(state, out, rng.nextInt(p.T), rng)
                HypothesisStartMode.STAFF_REPAIR -> if (p.S > 0) destroyRepairStaffAt(state, out, rng.nextInt(p.S), rng)
                HypothesisStartMode.MIXED_REPAIR -> {
                    if (p.T > 0) destroyRepairDayAt(state, out, rng.nextInt(p.T), rng)
                    if (p.S > 0) destroyRepairStaffAt(state, out, rng.nextInt(p.S), rng)
                }
                HypothesisStartMode.BASELINE -> Unit
            }
        }
        if (scheduleDistance(base, out) == 0) forceDiverseKick(p, out, rng, max(1, plan.intensity))
        return out
    }

    private fun forceDiverseKick(p: Problem, out: Array<IntArray>, rng: Random, target: Int) {
        if (p.S == 0 || p.T == 0) return
        val touched = HashSet<Long>()
        var changed = 0
        var attempts = 0
        val maxAttempts = max(32, p.S * p.T * 4)
        while (changed < target && attempts++ < maxAttempts) {
            val i = rng.nextInt(p.S); val j = rng.nextInt(p.T)
            val key = i.toLong() * max(1, p.T) + j
            if (!touched.add(key) || p.wishLocked(i, j)) continue
            val old = out[i][j]
            val alternatives = p.allowedShiftsForStaff(i).filter { it != old }
            if (alternatives.isEmpty()) continue
            out[i][j] = alternatives[rng.nextInt(alternatives.size)]
            changed++
        }
    }

    /**
     * 時間予定型 Great Deluge の水位（Burke, Bykov, Newall & Petrovic 2004）。
     * frac=1(序盤)で initial、frac=0(終盤)で best へ線形降下。候補スコア ≤ 水位 なら受理。
     */
    internal fun greatDelugeLevel(initial: Double, best: Double, frac: Double): Double =
        best + (initial - best) * frac.coerceIn(0.0, 1.0)

    // ───────── [3.335.0/外部レビュー P1] 実行ごとの成果物入れ ─────────
    //   `lastAlternatives` などの可変 static は「直近の実行の値」しか持てず、実行が重なると
    //   （WorkManager の REPLACE で旧 Worker が協調キャンセルを待つ間・kill 後の再スケジュール）
    //   入口の初期化が相手の値を消し、書き込みも読み出しも混ざり得た。実行ごとに [RunSlot] を作り、
    //   コルーチンのコンテキストで呼び出し木の隅々まで運ぶ（suspend 関数なので引数を増やさずに届く）。
    //   static は「いちばん新しい実行のライブ表示」用として残す（新しい方が勝つのが正しい面）。
    internal class RunSlot(val id: Long) {
        @Volatile var alternatives: List<Array<IntArray>> = emptyList()
        @Volatile var fusionElites: List<AdaptiveElite> = emptyList()
        private val lock = Any()
        @Volatile var infeasible: Set<String> = emptySet()
            private set
        fun addInfeasible(fams: Collection<String>) {
            if (fams.isEmpty()) return
            synchronized(lock) { infeasible = infeasible + fams }
        }
    }
    private class RunSlotElement(val slot: RunSlot) :
        kotlin.coroutines.AbstractCoroutineContextElement(RunSlotElement) {
        companion object Key : kotlin.coroutines.CoroutineContext.Key<RunSlotElement>
    }
    private val runSeq = java.util.concurrent.atomic.AtomicLong(0)
    /** いちばん新しい `optimize()` の実行 id。static への書き込みはこれと一致するときだけ行う。 */
    @Volatile private var newestRunId = 0L
    private suspend fun runSlot(): RunSlot? =
        kotlin.coroutines.coroutineContext[RunSlotElement]?.slot
    /** static（＝新しい実行が勝つライブ表示側）へ書いてよいか。スロット無し＝直接呼び出しは従来どおり許す。 */
    private fun ownsStatics(slot: RunSlot?): Boolean = slot == null || slot.id == newestRunId

    /** 直近の並列探索で得た「他の案」（採用案以外の候補スケジュール、品質順・最大3件）。
     *  [3.335.0] **これは「いちばん新しい実行」の値**。呼び出し側は `V6OptimizerResult.alternatives`
     *  （実行ごとの値）を読むこと。ここは表示・互換のために残している。 */
    @Volatile var lastAlternatives: List<Array<IntArray>> = emptyList()
        private set

    // [3.288.0/ログ強化=状態軸] この optimize() 実行中に HF63 が「構造的に充足困難」と学習した族の集合
    //   （全 runRsi 呼出＝直接RSI/RSI++/適応ポートフォリオの各ワーカーからの union）。エピローグの
    //   「残存分析」行が読む。optimize() 入口でクリアする。並行ワーカーからの union 更新のため synchronized。
    @Volatile var lastInfeasibleFamilies: Set<String> = emptySet()
        private set
    private val infeasLock = Any()
    internal fun recordInfeasible(fams: Collection<String>) {
        if (fams.isEmpty()) return
        synchronized(infeasLock) { lastInfeasibleFamilies = lastInfeasibleFamilies + fams }
    }
    /** [3.335.0] この実行のスロットへ記録し、いちばん新しい実行のときだけ static も更新する。 */
    private suspend fun recordInfeasibleScoped(fams: Collection<String>) {
        val slot = runSlot()
        slot?.addInfeasible(fams)
        if (ownsStatics(slot)) recordInfeasible(fams)
    }
    internal fun clearInfeasible() { synchronized(infeasLock) { lastInfeasibleFamilies = emptySet() } }

    /** [3.288.0/ログ強化=回数軸] focus 足跡の連続圧縮（"c3n,c3n,c1" → "c3n×2→c1"）。マーカー([..])はそのまま挟む。 */
    internal fun compressFocusTrail(trail: List<String>): String {
        val out = StringBuilder(); var i = 0
        while (i < trail.size) {
            val t = trail[i]
            if (t.startsWith("[")) { if (out.isNotEmpty()) out.append("→"); out.append(t); i++; continue }
            var j = i + 1
            while (j < trail.size && trail[j] == t) j++
            if (out.isNotEmpty()) out.append("→")
            out.append(t); if (j - i > 1) out.append("×${j - i}")
            i = j
        }
        return out.toString()
    }

    /** [3.268.0/elite archive fusion] 全epochから圧縮した品質・距離・橋渡しエリート
     *  （最適化後の再結合/Fusion専用、PORTFOLIO実行時のみ非空）。 */
    @Volatile internal var lastFusionElites: List<AdaptiveElite> = emptyList()
        private set

    /** [DefragLiveView移植] 実行中の最良盤面スナップショット（計算中ライブ表示用・読取専用）。
     *  進捗の節目で更新。
     *  [敵対的レビュー修正] 旧実装は単純 last-writer-wins だった（コメント上は「並列時はどのワーカーの
     *  最良でも有効な解」としていたが、実際には各仮説/チェーンが**自分のローカル最良**を無条件に書くため、
     *  劣った仮説が後から書き込むと途中結果(kill復旧用)の品質が非単調に退行し得た）。[liveBestReport]で
     *  CAS管理する真のグローバル最良のときだけ更新する（[publishLiveBest]経由。better()と同一基準）。 */
    val liveBest: List<List<Int>>? get() = liveBestRef.get()?.board

    /**
     * [3.385.0] 評価と盤面を**1つの不変オブジェクト**にして1回の CAS で publish する。
     *
     * 旧実装は「report を CAS → 成功したら liveBest へ代入」の2段だった。CAS は report を単調にするが、
     * **盤面の代入は CAS の外**なので次の順で割り込める:
     *   A: CAS(null → 必須3) 成功 → 盤面コピー(O(職員×日))の途中でプリエンプト
     *   B: CAS(必須3 → 必須1) 成功 → 盤面B を書く
     *   A: 再開して**盤面A（劣る方）で上書き**
     * 結果 `liveBestReport` は必須1 なのに `liveBest` は必須3 の盤面＝**両者が食い違い、
     * 途中最良が退行する**（docstring が「退行を防ぐ」と謳っていた不変条件そのものが破れる）。
     * 8並列ワーカーが同時に publish し、間に O(310) のコピーが挟まるので稀ではない。
     *
     * 実害は誤った勤務表ではない（採用は必ず checker の keep-best が決める）。破れるのは
     * ①kill 復旧用スナップショット(`magi_bg_best.json`)が最良より劣る盤面になり進捗を捨てる
     * ②ライブ表示の数字と盤面が食い違う、の2点。
     */
    private class LiveBestSnapshot(val report: ViolationReport, val board: List<List<Int>>)

    private val liveBestRef = java.util.concurrent.atomic.AtomicReference<LiveBestSnapshot?>(null)

    /** [敵対的レビュー修正] liveBest を真にグローバルな最良のときだけ更新する。呼出元のローカル
     *  best/report が既存の liveBest より劣る/同値なら何もしない＝退行を防ぐ。 */
    internal fun publishLiveBest(report: ViolationReport, schedule: Array<IntArray>) {
        // 盤面コピーは「勝ち目がある」と分かってから1回だけ。負ける呼出（多数）はコピーを払わない。
        var snap: LiveBestSnapshot? = null
        while (true) {
            val cur = liveBestRef.get()
            if (cur != null && !better(report, cur.report)) return
            if (snap == null) snap = LiveBestSnapshot(report, schedule.map { it.toList() })
            if (liveBestRef.compareAndSet(cur, snap)) return
            // [3.387.0] ここへ来る＝**別スレッドが同時に publish していた**。3.385.0 で直した競合が
            //   実機で本当に起きるのかは単体テストでは分からない（合成の競合しか作れない）ので、
            //   ここだけが唯一の実測点。0 が続くなら「理論上の窓」、非ゼロなら「実際に起きている」。
            liveBestContention.incrementAndGet()
        }
    }

    /** [3.387.0] `publishLiveBest` の CAS が競合で再試行した回数（この実行ぶん・診断表示のみ）。 */
    private val liveBestContention = java.util.concurrent.atomic.AtomicInteger(0)

    internal fun liveBestContentionCount(): Int = liveBestContention.get()

    /** テスト専用（`optimize()` を回さずに publish の不変条件だけを検査するため）。 */
    internal fun resetLiveBestForTest() { liveBestRef.set(null); liveBestContention.set(0) }

    /**
     * [3.388.0/外部レビュー] **利用者の1回の「つくる」ぶん**の計測をゼロから始める。
     *
     * 旧実装は [optimize] の入口で `TuningTelemetry.reset()` と競合カウンタを落としていたが、
     * `handleOptimize` は AUTO の 31〜210秒帯で **optimize() を最大3回**呼ぶ（RSI → ALNS →
     * ExtraRefine）。よって後続の呼出が先行ぶんを上書きし、診断に出るのは**最後の pass だけ**だった
     * ＝主計算(8ワーカー)の計測が捨てられ、`設定の効き` も競合数も 0 に見える。
     * 「0 なら理論上の窓に留まっている」と読ませる行がまさに false negative になる
     * （3.102.1 の `lastAlternatives` と同じ罠）。入口で1回だけ落とす形へ移す。
     */
    fun beginTelemetry() {
        TuningTelemetry.reset()
        liveBestContention.set(0)
    }

    /**
     * [3.335.0/外部レビュー P1] この実行だけの成果物入れ（[RunSlot]）を作り、コルーチンのコンテキストで
     * 呼び出し木の隅々まで運ぶ。結果は返り値に載せて返すので、**実行が重なっても呼び出し側は自分の
     * 実行の値だけを読む**（旧実装は返却後に可変 static を読んでいた）。static は「いちばん新しい実行の
     * ライブ表示」として残し、置き換えられた古い実行は書き込まない。
     */
    suspend fun optimize(
        state: MagiState,
        initial: Array<IntArray> = state.schedule.toIntArray2D(),
        options: V6OptimizerOptions = V6OptimizerOptions(),
        shouldStop: () -> Boolean = { false },
        onProgressRaw: (String, ViolationReport?, Long, Long) -> Unit = { _, _, _, _ -> },
        /** [3.346.1] `shouldStop` が真のとき、それが**単調な停止**（探索締切・キャンセル）かを返す。
         *  停滞シグナルは単調でない（改善が届けば偽に戻る）ので、適応ポートフォリオはそれだけを
         *  確認窓で再確認する。既定は「常に単調」＝確認せず即離脱＝従来どおり。 */
        stopIsFinal: () -> Boolean = { true },
    ): V6OptimizerResult {
        val slot = RunSlot(runSeq.incrementAndGet())
        newestRunId = slot.id
        lastAlternatives = emptyList()
        lastFusionElites = emptyList()
        clearInfeasible()   // [3.288.0/状態軸] この実行の HF63 学習をゼロから集約する
        liveBestRef.set(null)
        val r = withContext(RunSlotElement(slot)) {
            optimizeInSlot(state, initial, options, shouldStop, onProgressRaw, stopIsFinal)
        }
        return r.copy(alternatives = slot.alternatives, infeasibleFamilies = slot.infeasible)
            .also { it.fusionElites = slot.fusionElites }
    }

    private suspend fun optimizeInSlot(
        state: MagiState,
        initial: Array<IntArray>,
        options: V6OptimizerOptions,
        shouldStop: () -> Boolean,
        onProgressRaw: (String, ViolationReport?, Long, Long) -> Unit,
        stopIsFinal: () -> Boolean,
    ): V6OptimizerResult {
        val started = nowMs()
        // [敵対的レビュー: 進捗コールバックの直列化] runMultiWorker(仮説横断)・runAlnsChains(チェーン横断)は
        //   複数の Dispatchers.Default コルーチンから同じ onProgress を並行呼出しうる（best更新自体は
        //   各所でCAS/ロック済みだが、呼出元のコールバック本体＝UI/Worker側の非同期共有状態(スナップショット
        //   ファイル書込み等)は無保護だった）。この最上位の入口1箇所でロックすれば、内側の多層fan-out
        //   （仮説×チェーン）を経ても最終的にユーザーコールバックへは必ず直列で届く。
        val progressLock = Any()
        val onProgress: (String, ViolationReport?, Long, Long) -> Unit = { phase, report, iters, elapsed ->
            synchronized(progressLock) { onProgressRaw(phase, report, iters, elapsed) }
        }
        val chosen = chooseAlgorithm(options.algorithm, options.totalBudgetSec)
        val p = cachedProblem(state)
        var schedule = hf66DataHardening(state, normalizeSchedule(initial, p), "pre")
        // [N1b] 入口修復(hf67)は better(hard→weighted→total) 改善時のみ採用。既に良好な入力
        //   （前回結果の再最適化など）を破壊し、探索を劣化seedに係留する事故を防ぐ
        //   （運用ログ実例: 入力214 → 修復後HARD4/250 → 275秒が回復に浪費）。hf66(群内正規化)は無条件維持。
        val entryReport = UnifiedViolationChecker.check(state, schedule)
        val repaired = hf67HardRepair(state, schedule, Random(actualSeed(options.seed) xor 0x67L)).schedule
        val repairedReport = UnifiedViolationChecker.check(state, repaired)
        val hf67Adopted = better(repairedReport, entryReport)
        if (hf67Adopted) schedule = repaired
        val entryBoard = schedule.copy2D()   // [N1c] 内側番兵用に入力の勤務表を保持
        val entryBoardReport = if (hf67Adopted) repairedReport else entryReport
        // [仮説数上限撤廃] 旧仕様は「最大MAX_HYPOTHESES(5)仮説」固定で、超過ワーカーは仮説内並列度へ配分
        //   していた。ユーザー指示により固定上限を撤廃し、仮説数(w)をワーカー設定にそのまま連動させる
        //   （多様性>深さ。V5だけは仮説の概念を使わずworkersをそのままSAチェーン数とする＝対象外）。
        val w = hypothesisCount(options.workers)
        // [3.371.0/並列SA本格再有効化] 表示はエンジンが実際に使う hypothesisSpawnPlan（runMultiWorker と
        //   同一関数）から導出。PORTFOLIO は runMultiWorker を経由せず各ロールが portfolioRoleChainCount()
        //   本のチェーンで走るため、この plan でなくそちらを表示する（3.372.0/レビュー修正: 旧実装は
        //   「仮説内チェーンは対象外」を無条件に印字し、portfolioRoleParallelSa を ON にしても実挙動が
        //   ログに出ず、実機ログでのA/Bというトグル本来の目的を潰していた）。
        val (spawnHyp, plan) = hypothesisSpawnPlan(options.workers, w)
        val planNote = plan.let { pl ->
            val mn = pl.min(); val mx = pl.max()
            if (mn == mx) "仮説内${mn}並列" else "仮説内${mn}〜${mx}並列"
        }
        val workersNote = when (chosen) {
            V6Algorithm.V5 -> "workers=${options.workers}（SAチェーン）"
            V6Algorithm.PORTFOLIO -> {
                val roleChains = portfolioRoleChainCount()
                "workers=${options.workers}（適応ポートフォリオ仮説${w}・ロール内チェーン${roleChains}本" +
                    "${if (roleChains > 1) "＝ロール内並列SA ON" else ""}）"
            }
            else -> "workers=${options.workers}（実効仮説${spawnHyp}${if (spawnHyp < w) "＝設定${w}をコア数まで縮小" else ""}・$planNote）"
        }
        var logs = listOf(
            MirrorLog(tag = "V6Dispatcher", message = "algorithm=$chosen budget=${options.totalBudgetSec}s $workersNote"),
            MirrorLog(tag = "HF67", message = if (hf67Adopted)
                "入口修復を採用 HARD ${entryReport.hard}->${repairedReport.hard} / total ${entryReport.total}->${repairedReport.total}"
            else
                "入口修復を見送り（入力の方が良好: HARD ${entryReport.hard}/total ${entryReport.total} ≦ 修復後 HARD ${repairedReport.hard}/total ${repairedReport.total}）"),
        )
        val full = max(1, options.totalBudgetSec)
        val result = when (chosen) {
            // V5 already runs `workers` parallel SA chains inside SaOptimizer.
            V6Algorithm.V5 -> runV5(state, schedule, options, full, shouldStop, onProgress)
            // ALNS/RSI/RSI++ are run as up to 5 parallel hypotheses with hybrid early-cancel.
            // [3.266.0/hypothesis basin diversity] 各仮説の入口盤面を hypothesisStartFor で多様化
            //   （W0/W4のみ現行盤面のコピー=安全フロア維持）。旧実装は全仮説が同一盤面から出発しており、
            //   探索経路が異なっても頻繁に同じ吸引域へ収束していた（実データで8仮説→相異なる解1件を確認）。
            V6Algorithm.ALNS -> runMultiWorker(w, options, onProgress) { i, o, prog ->
                runAlns(state, hypothesisStartFor(state, schedule, i, o.seed), o, full, shouldStop, prog)
            }
            V6Algorithm.RSI -> runMultiWorker(w, options, onProgress) { i, o, prog ->
                runRsi(state, hypothesisStartFor(state, schedule, i, o.seed), o, full, shouldStop, prog)
            }
            V6Algorithm.RSI_PLUS -> runMultiWorker(w, options, onProgress) { i, o, prog ->
                runRsiPlus(state, hypothesisStartFor(state, schedule, i, o.seed), o, full, shouldStop, prog)
            }
            // [3.267.0/adaptive hypothesis epochs] 1回起動して終了を待つ旧協力ポートフォリオ（各仮説に
            //   異なる方式を割当て keep-best で最良採用）では、入口を多様化しても収束後は同じ吸引域へ
            //   潰れたまま残時間を消費していた。5〜8秒（RSI++は35秒）epochで停滞/basin重複を検知し、
            //   エリートを保存しながら役割を再配属する非同期適応ポートフォリオへ置換。
            V6Algorithm.PORTFOLIO -> runAdaptivePortfolio(state, schedule, w, options, full, shouldStop, stopIsFinal, onProgress)
            V6Algorithm.AUTO -> error("AUTO must be resolved")
        }
        logs = logs + result.phaseLogs
        // [E11/多人数ブロック移動] エピローグで残 covU を「勤務→勤務」連鎖で充填（ALNS単独や covU を focus
        //   しなかった経路でも走る保険）。keep-best 照合＝退化不能。ユーザー実例(8/11・8/17)の詰み局面を解く。
        var resultSched = result.schedule
        run {
            val preRep = UnifiedViolationChecker.check(state, resultSched)
            if (preRep.hard > 0 && (preRep.breakdown["covU"] ?: 0) > 0 && !shouldStop()) {
                val cand = resultSched.copy2D()
                val n = applyCovUChains(state, cand, Random(actualSeed(options.seed) xor 0xC0FFEEL))
                if (n > 0) {
                    val candRep = UnifiedViolationChecker.check(state, cand)
                    if (better(candRep, preRep)) {
                        resultSched = cand
                        logs = logs + MirrorLog(tag = "ChainFill",
                            message = "多人数ブロック移動で covU 充填: HARD ${preRep.hard}→${candRep.hard} / total ${preRep.total}→${candRep.total}（連鎖${n}件）")
                    }
                }
            }
        }
        // [review #3] Final epilogue polish only when the caller isn't running its own post chain.
        val polished = if (options.postPolish && !shouldStop())
            hf80PostPolish(state, resultSched, max(1, min(30, options.totalBudgetSec / 20)), actualSeed(options.seed) xor 0x80L, shouldStop)
        else PolishResult(resultSched, emptyList(), 0)
        val finalReport = UnifiedViolationChecker.check(state, polished.schedule)
        logs = logs + polished.logs + MirrorLog(
            tag = "V6Dispatcher",
            message = "完了 algorithm=$chosen HARD=${finalReport.hard} total=${finalReport.total} elapsed=${nowMs() - started}ms",
        )
        // [N1c] 内側番兵: 最終結果が入力の勤務表より劣るなら入力の勤務表へ復帰（FinalPortの外側Sentinelと二重化）。
        //   全段keep-bestのため通常は発火しない。発火時は「予算が改善に寄与しなかった」ことの可視化を兼ねる。
        if (better(entryBoardReport, finalReport)) {
            logs = logs + MirrorLog(level = "W", tag = "V6Dispatcher",
                message = "内側番兵: 結果(HARD=${finalReport.hard}/total=${finalReport.total})が入力の勤務表(HARD=${entryBoardReport.hard}/total=${entryBoardReport.total})より劣化のため入力の勤務表を採用")
            return V6OptimizerResult(entryBoard, entryBoardReport.copy(logs = logs + entryBoardReport.logs), chosen, logs, result.iterations + polished.iterations, nowMs() - started)
        }
        return V6OptimizerResult(polished.schedule, finalReport.copy(logs = logs + finalReport.logs), chosen, logs, result.iterations + polished.iterations, nowMs() - started)
    }

    /** [3.231.0/ドッグフーディングで発見・修正] RSIラウンドループがHf63Infeasibilityへ渡す
     *  ラウンド当たりeffortIters。旧実装は1800/round固定で、INFEAS_STALL_ITERS=5000到達に
     *  約3ラウンドの同族focusを要した。E9冷却(1ラウンド休止)が2〜3の詰んだ族を交互に切替える
     *  実運用では、rounds が小さい(既定5等)と3回目のfocusが最終ラウンドに達し、deprioritize が
     *  成立しても振り向け先の残りラウンドが無かった。rounds に応じて動的に決め、
     *  「残り最低reserveRounds分を振り向けに残せる」タイミングでdeprioritizeが完了するようにする
     *  （E9の1-in-2交互を想定しattemptsTarget=ceil((rounds-reserveRounds)/2)、下限2で一度の不運な
     *  1ラウンドだけではdeprioritizeしない=E9のより軽い1R冷却との役割分担を保つ）。純関数として抽出し
     *  ユニットテスト可能にする。 */
    internal fun rsiHf63EffortIters(rounds: Int, reserveRounds: Int = 2): Int {
        val attemptsTarget = max(2, (max(0, rounds - reserveRounds) + 1) / 2)
        return (Hf63Infeasibility.INFEAS_STALL_ITERS + attemptsTarget - 1) / attemptsTarget
    }

    /** [仮説数上限撤廃・ユーザー指示] かつて仕様§2.2の仮説数固定上限(5)だった定数。optimize() の
     *  仮説数計算（[hypothesisCount] 参照）はこの値を上限として使わなくなった＝ワーカー設定まで仮説を
     *  増やす（下限2）。現在は ①ExtraRefine(微小予算5〜25sの追加精製)専用の意図的な小さいキャップ
     *  （仮説内多チェーンの固定費が小予算を侵食するのを避ける、V6FinalPort参照）②hypothesisChainPlan の
     *  デフォルト引数、の2用途にのみ残置（名前は歴史的経緯・値の意味は「旧上限」から「小予算時の安全キャップ」
     *  へ転用）。 */
    const val MAX_HYPOTHESES = 5

    /** [仮説数上限撤廃・ユーザー指示「仮説数は最低2最大設定値」] 仮説数(w)の実効値。旧 optimize() は
     *  `options.workers.coerceIn(1, MAX_HYPOTHESES)` で workers>5 分を仮説内並列度へ配分していたが、
     *  ユーザー指示によりこの固定上限を撤廃し**多様性(仮説数)を優先**する。下限2（workers=1でも最低2仮説の
     *  多様探索を保証・diversity目的で意図的にworkersを1オーバーサブスクライブする）・上限は無し
     *  （options.workers自体が上限）。optimize() 本体と V6FinalPort の診断表示(effHypotheses)が両方
     *  本関数から導出＝独立再計算によるUI/ログの乖離を防ぐ（3.212.0 と同じ設計原則）。 */
    internal fun hypothesisCount(workers: Int): Int = max(2, workers)

    /** [余剰ワーカー活用] 仮説数(hypotheses)に対し、設定workersのうち何本を各仮説の
     *  内部並列度（SAチェーン数・ALNS多チェーン）へ均等配分するか。workers<=hypothesesなら1(旧来どおり
     *  単一チェーン)。余りは切り捨て（例: workers=8,hypotheses=5 → 1本/仮説・workers=16,hypotheses=5 → 3本/仮説）。
     *  ※均等床の計算のみ。実際の配分は hypothesisChainPlan（余り配分＋コア数クランプ）を使う。
     *  [仮説数上限撤廃後] 本体の w=hypothesisCount(workers) は workers>=2 で hypotheses==workers となるため
     *  実運用では常に1（内部並列は事実上不使用）。本関数は ExtraRefine 等 hypotheses<workers な呼出のために残置。 */
    internal fun perHypothesisWorkers(workers: Int, hypotheses: Int): Int =
        max(1, workers / max(1, hypotheses))

    /** [敵対的レビュー修正・#6] V5(高速計算)は仮説の概念を使わず options.workers をそのまま
     *  SaParams.workers(=SAチェーン数)へ渡していたため、hypothesisChainPlan のコア数クランプの
     *  恩恵を受けず、コア数を超えるCPU-boundコルーチンを壁時計締切下で希釈しうる（例: 8コア機に
     *  workers=16設定でV5選択→16並列SAチェーンが8コアを奪い合う）。V5専用に総並列度をコア数以内へ
     *  クランプする（hypothesisChainPlan と異なりV5は「最低1仮説」のような競合する下限が無いため、
     *  単純にコア数でクランプするだけで良い）。 */
    internal fun clampWorkersToCores(workers: Int, cores: Int = Runtime.getRuntime().availableProcessors()): Int =
        max(1, workers).coerceAtMost(max(1, cores))

    /** [敵対的レビュー3.212.0/単一ソース] 仮説ごとのチェーン本数プラン。レビューで確定した2欠陥を修正:
     *  ①旧 perW=床のみ配分は workers 6〜9（既定上限8＝動機の実機ログ当該ケース）で余り1〜4本を黙って
     *    廃棄しながらUI/docsが「無駄にならない」と虚偽主張（HF77: コメント≠実装）→ 余りを先頭仮説から
     *    +1ずつ配分し、主張どおり「5を超えた分は実際に使われる」ようにする。
     *  ②コア数クランプ無しで workers=16/8コア端末が15 CPU-boundコルーチンを壁時計締切下で希釈し
     *    「浅い3本のkeep-best < 深い1本」の品質逆行リスク（2.55/2.56のA/B実測原則にも反する）→
     *    配分総量を min(workers, cores) にクランプ（コア数以内なら挙動は配分の名のとおり）。
     *  UI注記・診断ログ・エンジン本体が全て本関数から導出＝表示と実挙動の乖離を構造的に防ぐ。
     *  返り値: 長さ hypotheses の各仮説チェーン本数（各要素>=1・合計=max(hypotheses, min(workers, cores))）。 */
    internal fun hypothesisChainPlan(
        workers: Int,
        hypotheses: Int = MAX_HYPOTHESES,
        cores: Int = Runtime.getRuntime().availableProcessors(),
    ): IntArray {
        val h = max(1, hypotheses)
        val distributable = max(h, kotlin.math.min(max(1, workers), max(1, cores)))
        val basePer = distributable / h
        val remainder = distributable % h
        return IntArray(h) { i -> basePer + if (i < remainder) 1 else 0 }
    }

    /** [3.371.0/並列SA本格再有効化] `runMultiWorker` が実際に spawn する仮説コルーチン数と、各仮説の
     *  内部チェーン本数プランを、診断ログ側とも共有する単一ソース（3.212.0/3.225.0と同じ「表示は実挙動
     *  から導出」原則）。
     *
     *  背景: `w=hypothesisCount(workers)` は workers>=2 のとき常に `w==workers` になる（3.224.0 の
     *  多様性優先化）。これを [hypothesisChainPlan] の hypotheses へそのまま渡すと
     *  `distributable=max(w, min(workers,cores))=w` に構造的に一致し、内部チェーン本数（並列SA/ALNS）が
     *  **コア数に関わらず恒久的に1本**に収束していた（3.211.0/3.212.0で作った「余剰ワーカーを内部並列へ
     *  配分」する仕組みが 3.224.0 以降、実質死んでいた）。
     *
     *  workers<=cores（大半の端末・既定の並列ワーカー設定）ではこの関数は無変更の挙動を返す
     *  （hSpawn==w のため下記 if に入らず、旧来と完全に同一の spawn 数・plan）。
     *  workers>cores（端末のコア数を超える設定）のときだけ、spawn する仮説コルーチン数を実コア数まで
     *  落とし（cores<w の希釈を避ける、V5用 [clampWorkersToCores] と同じ発想）、その分の予算(workers)を
     *  各仮説の内部チェーン数へ回す（[hypothesisChainPlan] の cores 引数へ options.workers を渡し、
     *  既定のコア数クランプを迂回して「予算workers・仮説hSpawn本」を素直に配る）。
     *  workers 予算の合計は不変（コア数を超えてコルーチンを増やさない＝オーバーサブスクライブの新規発生
     *  なし。3.224.0 で固定された `hypothesisChainPlan(5,5,8)==[1,1,1,1,1]` 等の既存契約は無変更）。
     *  返り値: (spawn する仮説コルーチン数, 各仮説のチェーン本数プラン=長さそのhSpawn)。 */
    internal fun hypothesisSpawnPlan(
        workers: Int,
        w: Int,
        cores: Int = Runtime.getRuntime().availableProcessors(),
    ): Pair<Int, IntArray> {
        // [3.372.0/レビュー修正] 旧実装は `max(2, min(w, cores))` で、w<2 のとき hSpawn(=2) が w(=1) を
        //   上回り、plan を w で組んでいたため `plan.size(1) < hSpawn(2)` ＝ runMultiWorker が index する
        //   不変条件 `hSpawn == plan.size` を破っていた（plan[1] で AIOOBE）。本番の3呼出は全て
        //   w=hypothesisCount(workers)=max(2,workers)>=2 のため到達しないが、本関数は internal で
        //   テスト/将来の呼出から届く＝潜在バグ。①hSpawn が w を超えないようにし ②plan を必ず hSpawn で
        //   組む（hypothesisChainPlan は IntArray(max(1,hypotheses)) を返す＝不変条件が構造的に成立）。
        //   多様性の下限2は「w>=2 のときだけ意味を持つ」ので min(w, ...) の内側に置く。
        val hSpawn = max(1, min(w, max(2, cores)))
        // [テスト容易性] else 分岐も明示的に cores を渡す（渡さないと hypothesisChainPlan の既定引数＝
        //   実デバイスのコア数へ暗黙フォールバックし、この関数のテストが実行環境依存になる）。
        //   hSpawn==w のときは hypothesisChainPlan 自体が h==hypotheses に一致し distributable も
        //   常に h と一致するため（本関数のKDoc参照）、cores を明示的に渡しても渡さなくても結果は同一。
        val plan = if (hSpawn < w) hypothesisChainPlan(workers, hSpawn, cores = workers)
            else hypothesisChainPlan(workers, hSpawn, cores = cores)
        return hSpawn to plan
    }

    /**
     * [3.372.0/レビュー修正] PORTFOLIO の各ロールが内部の V5(SA)/ALNS へ渡すチェーン本数。
     * `runAdaptivePortfolio` の実配線と診断ログの表示が同じ値を読むための単一ソース
     * （旧: 診断ログが `仮説内チェーンは対象外` を無条件に印字し、トグルONでも実挙動を隠していた＝
     *   実機ログでのA/Bというトグル本来の目的を潰していた。3.153.0 の NativeBridge 行と同型）。
     */
    internal fun portfolioRoleChainCount(
        cores: Int = Runtime.getRuntime().availableProcessors(),
    ): Int = if (PolishGate.portfolioRoleParallelSa)
        PolishGate.portfolioRoleChains.coerceIn(1, max(1, cores))
    else 1

    /**
     * [3.409.4] PORTFOLIO の**外側ワーカー**が壁時計上でどれだけ並行していたかの観測値
     * （役割別worker秒の合計 ÷ ポートフォリオ本体の経過）。**CPU 使用率でも、仮説内チェーンを
     * 含む使用コア数でもない。**
     *
     * 目的は「8仮説の設定なのに実質1本」で走る片肺化を、入力・端末を跨いだログで一目で見ること。
     * 実機ログで実際に判別できることを確認済み: 3.402.0 の 2,189s/275.007s = **7.96**（健全）に対し、
     * 3.370.0 の 74s/79.593s = **0.93**＝同じログの離脱行が
     * `ワーカー離脱=8/8本が締切前(勝者確定7本@0s…)` で、**3.376.0 で撤廃した「HARD=0 到達時に
     * 残りを即キャンセルする」機構**そのものだった。つまりこの指標は当時なら即座に検出できた。
     * そのバグは既に直っているので、前向きの用途は**回帰検出**である。
     */
    internal fun observedOuterParallelism(totalWorkerMs: Long, wallElapsedMs: Long): Double =
        if (totalWorkerMs <= 0L || wallElapsedMs <= 0L) 0.0
        else totalWorkerMs.toDouble() / wallElapsedMs.toDouble()

    /**
     * [3.409.16] ワーカーの離脱理由が「締切前の早期離脱」か。
     * 「締切」=自分の while ループの deadline 到達／「探索締切」=同じ締切（またはキャンセル）が
     * stopIsFinal() の stop シグナル経由で届いた正常終了＝どちらも早期離脱ではない。
     * 旧判定（!= "締切" のみ）は、全ワーカーが予算を使い切った正常な実行を
     * 「ワーカー離脱=8/8本が締切前(探索締切8本@275s)」と自己矛盾で報告していた（3.409.14 実機ログで発覚）。
     * 早期離脱として数えるのは「停滞シグナル」（confirmStop の確認窓を通った本物の停滞）と「例外」。
     */
    internal fun isEarlyWorkerExit(exitReason: String): Boolean =
        exitReason != "締切" && exitReason != "探索締切"

    private data class AdaptiveWorkerOutcome(
        val elite: Array<IntArray>,
        val report: ViolationReport,
        val logs: List<MirrorLog>,
        val iterations: Long,
        val epochs: Int,
        val reassignments: Int,
        val roleRuns: Map<HypothesisEpochRole, Int>,
        /** [3.307.0/ログ強化] 役割ごとの実消費ミリ秒。量子(5/8/35/45秒)は要求値であって
         *  消費値ではないため、予算配分を論じるにはこちらが要る。 */
        val roleMillis: Map<HypothesisEpochRole, Long>,
        /** [3.306.0] ワーカーが epoch ループを抜けた時点の役割。エリート登録の分類に使う
         *  （再配属回数からの逆算では、残差ベース経路のとき実際の役割と一致しないため）。 */
        val lastRole: HypothesisEpochRole,
        /** [3.346.0/実機ログ] ワーカーが epoch ループを抜けた理由と、その時点の経過秒。
         *  `shouldStop` は**単調でない**（改善が届けば偽に戻る）のに `while` 条件で使うため、
         *  一瞬 true になった瞬間にポーリングしたワーカーだけが恒久的に離脱する。実機ログ
         *  2026-08-03 では 8本中4本が 115〜116s で離脱し、残り159秒を4本で走っていた
         *  （役割別worker秒を手で足さないと気づけなかった）。理由と時刻を明示して可視化する。 */
        val exitReason: String,
        val exitAtSec: Long,
        /** [3.346.1] 一瞬の停滞シグナルを確認窓で見送った回数（＝旧実装なら恒久離脱していた回数）。 */
        val survivedStops: Int = 0,
        /** [3.283.0/ログ強化] ワーカー専属HF63がエポック横断で学習した回避族（勝者以外のfocus学習は
         *  従来この要約でしか外へ出ない＝W1/W2が何を諦めたかがログ解析不能だった穴を埋める）。 */
        val hf63Avoided: List<String> = emptyList(),
    )

    /** [3.346.1] 停滞シグナルの確認窓。この間 shouldStop が続けて真なら本物とみなす。 */
    internal const val STOP_CONFIRM_MS = 5_000L
    private const val STOP_CONFIRM_POLL_MS = 250L

    /**
     * [3.346.1/方針B] `shouldStop` が**本物の停止**かを確認窓のあいだ再確認する。
     *
     * `shouldStop` は単調でない: 締切・キャンセルは一度真なら永久に真だが、停滞シグナルは
     * 「最終改善からの経過 > 閾値」なので**他のワーカーが改善を1件出した瞬間に偽へ戻る**。
     * 旧実装はこれを epoch ループの `while` 条件で見ていたため、たまたまその瞬間にポーリングした
     * ワーカーだけが恒久的に離脱していた（実機 2026-08-03: 8本中4本が115〜116秒で消え、
     * 残り159秒を半分の並列度で走行。閾値37.5秒に対し改善間隔が37〜41秒＝ほぼ毎回きわどい）。
     *
     * ここでは短い窓のあいだ再確認し、途中で偽へ戻れば false（＝一瞬のシグナル・走行を続ける）、
     * 窓のあいだ真のままなら true（＝本物・従来どおり離脱）を返す。窓を [STOP_CONFIRM_MS] に
     * 取るのは、閾値をわずかに超えて発火する near-miss なら次の改善が数秒で届くため
     * （実機の該当ケースは発火115秒→次の改善が約3秒後）。本物の停滞ならこの窓ぶんだけ
     * 離脱が遅れるが、待機は suspend なので CPU は消費せず、8本が並行に待つので壁時計の
     * 追加も窓1回ぶん。締切超過とキャンセルは単調なので即座に true を返す。
     *
     * `deadline` は [nowMs]（`System.nanoTime()` 系の単調時計）と同じ物差しで渡すこと。
     * 壁時計（`System.currentTimeMillis()`）を混ぜると別の原点になり、締切超過を検出できない。
     */
    internal suspend fun confirmStop(
        shouldStop: () -> Boolean,
        deadline: Long,
        stopIsFinal: () -> Boolean = { true },
    ): Boolean {
        if (stopIsFinal()) return true
        val until = nowMs() + STOP_CONFIRM_MS
        while (nowMs() < until) {
            if (nowMs() >= deadline || stopIsFinal()) return true
            if (!coroutineContext.isActive) return true
            try {
                delay(STOP_CONFIRM_POLL_MS)
            } catch (e: kotlinx.coroutines.CancellationException) {
                // キャンセルは単調＝確定。旧実装（shouldStop 真で while を抜ける）と同じく
                // ループを正常終了させ、蓄積済みエリートを成果として返す。
                return true
            }
            if (!shouldStop()) return false
        }
        return true
    }

    /**
     * [3.267.0/adaptive hypothesis epochs, 3.268.0/elite archive fusion]
     * Adaptive asynchronous island portfolio. Each worker owns its epoch clock, so lightweight
     * ALNS/RSI roles can rotate every 5-8 seconds while RSI++ roles receive the 35 seconds required
     * to execute Seed(10)+RSI(10)+ALNS(10)+Polish(5) instead of being accidentally reduced to Seed.
     * A plateau saves the local elite and changes role/start basin/seed; only the shared deadline,
     * user cancellation, or HARD=0 ends the portfolio. W0 is never reassigned. Every schedule ever
     * produced by an epoch (start or role-search result) is registered into an [AdaptiveEliteArchive]
     * so the post-portfolio elite integration (EliteIntegrationPolish, see V6FinalPort) can relink
     * and fuse across the whole run, not just the final per-worker elite.
     */
    private suspend fun runAdaptivePortfolio(
        state: MagiState,
        entry: Array<IntArray>,
        w: Int,
        options: V6OptimizerOptions,
        budgetSec: Int,
        shouldStop: () -> Boolean,
        stopIsFinal: () -> Boolean,
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
    ): V6OptimizerResult = kotlinx.coroutines.supervisorScope {
        val started = nowMs()
        val deadline = started + budgetSec.coerceAtLeast(1) * 1000L
        val baseSeed = actualSeed(options.seed)
        val workers = max(1, w)
        val lock = Any()
        val firstError = java.util.concurrent.atomic.AtomicReference<Throwable?>(null)
        val hardZeroWinner = java.util.concurrent.atomic.AtomicInteger(-1)
        // [3.360.0/ログ強化] 全体最良を更新した回数。1回ごとの onProgress 行は既にあるが（スロットル対象外＝
        //   3.283.0）、**何回あったか**は要約に無く、実行の締めくくりで「序盤に1回きりで止まった」のか
        //   「終盤まで刻み続けた」のかがログから読めなかった（Watchdog 行が出すのは最終改善の時刻だけ）。
        //   改善が確定した分岐で数えるだけ＝ホットパスに追加コストなし。
        val globalImproves = java.util.concurrent.atomic.AtomicInteger(0)
        val archive = AdaptiveEliteArchive()

        val sharedTrajectories = Array(workers) { i -> hypothesisStartFor(state, entry, i, baseSeed) }
        val initialReports = Array(workers) { i -> UnifiedViolationChecker.check(state, sharedTrajectories[i]) }
        var globalBest = entry.copy2D()
        var globalReport = UnifiedViolationChecker.check(state, globalBest)
        var globalLogs: List<MirrorLog> = emptyList()
        archive.register(
            entry, globalReport, HypothesisEpochRole.BASELINE_REFINE, worker = 0, epoch = 0, bridge = false,
        )
        for (i in 0 until workers) {
            // [3.308.0] 初期配置であることを名前で示す（値は assignmentFor(i, 0) と同じ）。
            val assignment = AdaptiveHypothesisEpochPolicy.initialAssignmentFor(i)
            archive.register(
                sharedTrajectories[i], initialReports[i], assignment.role, i, 0,
                bridge = initialReports[i].hard == globalReport.hard + 1,
            )
            if (better(initialReports[i], globalReport)) {
                globalBest = sharedTrajectories[i].copy2D(); globalReport = initialReports[i]
            }
        }

        // [3.376.0/ユーザー指示「ワーカー、並列が本当に動くようにする」] `hardZeroWinner` は
        //   「先に HARD=0 へ到達した者が勝ち＝残りを即キャンセル」する省電力機構だった（docstring 参照）。
        //   だが **HARD=0 に到達した時点で残る仕事は全部 SOFT** で、勝者1本だけがそれを担うと
        //   利用者が指定した並列度の 1/8 しか使われない。実機は初期解 HARD=64 から**1秒で HARD=0**へ
        //   到達しており（実機ログ 13:53:53→13:53:54）、そこから 195 秒を実質1並列で走っていた。
        //   3.375.0 は「入口が既に HARD=0」の場合だけを塞いだが、HARD>0 入口でも同じ潰れが
        //   数秒遅れて起きる（同じ機構）。**キル自体を撤廃**し、全ワーカーを締切まで走らせる。
        //   `hardZeroWinner` は「誰が最初に到達したか」の記録としてのみ残す（ログの情報価値を維持）。
        //   安全性: 採否は全段 keep-best（better()）なので探索を増やしても品質は退化しない。
        //   代償は電池/発熱だが、総時間は予算と停滞ウォッチドッグが従来どおり govern する。
        //   [測れなかったこと] 手元の fixture では PORTFOLIO が HARD=0 へ到達しない
        //   （golden の greedy 初期解 hard=15 → 30秒で hard=1 止まり）ため、この機構の品質への効果は
        //   A/B できていない。確かなのは構造的な帰結（キルされなくなる）だけ。
        val jobs = Array<kotlinx.coroutines.Deferred<AdaptiveWorkerOutcome>>(workers) { i ->
            async(Dispatchers.Default) {
                var trajectory = synchronized(lock) { sharedTrajectories[i].copy2D() }
                var elite = trajectory.copy2D()
                var eliteReport = initialReports[i]
                var eliteLogs: List<MirrorLog> = emptyList()
                var reassignments = 0
                var stagnantEpochs = 0
                var improvedPrevious = false
                // [3.306.0/既定OFF] 残差ベースの4段脱出制御。null=従来経路（回数で機械的に巡回し、
                //   再配属のたびに stagnantEpochs を 0 へ戻す）。非null=盤面の残差と peer 距離で
                //   次の役割を選び、停滞の深さを役割変更でも保持する。既定 OFF の根拠は PolishGate 参照。
                val escapeController =
                    if (PolishGate.adaptiveEscapeControl) StagnationEscapeController(i) else null
                var controlledAssignment =
                    if (escapeController != null) AdaptiveHypothesisEpochPolicy.initialAssignmentFor(i) else null
                var epoch = 0
                var iterations = 0L
                var exitReason = ""   // [3.346.0] epoch ループの離脱理由（下で確定）
                var survivedStops = 0 // [3.346.1] 一瞬の停滞シグナルを見送った回数
                val roleRuns = LinkedHashMap<HypothesisEpochRole, Int>()
                // [3.307.0/ログ強化] 役割ごとの**実消費ミリ秒**。roleRuns(エポック数)だけでは
                //   「どの役割が予算のどれだけを実際に食ったか」が読めない（量子は役割ごとに
                //   5/8/35/45 秒と桁が違い、かつロールは締切・HARD=0・内部早期終了で量子より
                //   早く戻ることがある）。エポック境界の摂動・検査・距離計算も含めた実測。
                val roleMillis = LinkedHashMap<HypothesisEpochRole, Long>()
                // [3.281.0/停滞レビューB] ワーカー専属のHF63をエポック横断で共有。旧: runRsi 呼出ローカルのため
                //   短いエポック(rounds=2)では「focus 2ラウンドで threshold 到達→即破棄→次エポックで白紙から
                //   再学習」を全エポックで反復し、解けない族(実機: c3n)へ毎回突撃していた。ワーカー内は逐次
                //   実行＝並行アクセスなし。ワーカー間では共有しない（役割多様性を汚染しないため）。
                val workerHf63 = Hf63Infeasibility()

                // [3.278.0/監査修正×2]
                //   ①勝者継続: 旧条件 `hardZeroWinner.get() < 0` は HARD=0 到達で**勝者自身も**次 epoch 境界で
                //     停止させ、実行可能データでは残り数百秒のソフト研磨予算を放棄していた（runMultiWorker の
                //     文書化契約「勝者は自予算でソフト研磨を続ける」と衝突）。勝者だけは deadline まで継続する。
                //   ②epoch 例外隔離: 旧実装は try がロール実行(runAlns等)だけを包み、adaptiveEpochStart／check／
                //     archive.register／共有採用が無保護＝そこで例外が出ると Deferred が例外完了し await 再送出で
                //     **全ワーカーの keep-best 成果ごと optimize() が失敗**していた（runMultiWorker は仮説ごとの
                //     隔離＋firstError＋全滅時のみフォールバックを採用済みの非対称）。epoch 本体を try で包み、
                //     例外はこのワーカーの epoch ループだけを終了させて現エリートを成果として返す。
                // [3.346.1/方針B] 停滞シグナルは単調でない（改善が届けば偽に戻る）ので、
                //   `while` 条件には単調な締切・勝者確定だけを置き、シグナルは confirmStop で
                //   確認窓ぶん再確認してから離脱する。一瞬のシグナルで片肺運転にならない。
                while (nowMs() < deadline) {
                    if (shouldStop()) {
                        if (confirmStop(shouldStop, deadline, stopIsFinal)) {
                            exitReason = if (stopIsFinal()) "探索締切" else "停滞シグナル"
                            break
                        }
                        survivedStops++
                        continue
                    }
                    try {
                    val assignment = controlledAssignment
                        ?: AdaptiveHypothesisEpochPolicy.assignmentFor(i, reassignments)
                    val epochT0 = nowMs()
                    val roleSeed = AdaptiveHypothesisEpochPolicy.epochSeed(baseSeed, i, epoch, reassignments)
                    // [3.282.0/新領域ログ監査] エポック改善の基準線＝エポック開始時点の自己エリート。
                    //   旧: `better(result, startReport)` で startReport は**破壊摂動済みの入口盤面**（escape系
                    //   ロールは globalBest を意図的に壊した盤面）＝keep-best のロール実行は入口には
                    //   ほぼ必ず勝つため improvedThisEpoch が恒真化し、plateau 再配属・強度昇圧
                    //   （intensityFor=reassignments/2）・役割回転が全 escape ロールで実質死んでいた
                    //   （実機ログ: W2 が凍結した globalBest を35エポック同一役で摂動し続け再配属0、
                    //   ~8s/epoch=improving量子が常時選択、グローバル改善は150秒ゼロ）。エリート比なら
                    //   「ワーカーの過去最良を実際に前進させたか」という文書化された契約どおりの判定になる。
                    val preEpochEliteReport = eliteReport
                    val snapshot = synchronized(lock) {
                        Triple(
                            globalBest.copy2D(),
                            globalReport,
                            Array(workers) { x -> sharedTrajectories[x].copy2D() },
                        )
                    }
                    val start = adaptiveEpochStart(
                        state = state,
                        globalBest = snapshot.first,
                        localTrajectory = trajectory,
                        peers = snapshot.third,
                        assignment = assignment,
                        seed = roleSeed,
                        shouldStop = shouldStop,
                    )
                    val startReport = UnifiedViolationChecker.check(state, start)
                    archive.register(
                        start, startReport, assignment.role, i, epoch,
                        bridge = startReport.hard == snapshot.second.hard + 1,
                    )
                    trajectory = start
                    if (better(startReport, eliteReport)) {
                        elite = start.copy2D(); eliteReport = startReport
                        // [3.278.0/監査修正] 旧: eliteLogs 未更新＝この入口盤面が最終勝者になると、採用盤面を
                        //   生成していない古いロール実行のフェーズログが globalLogs としてユーザーに表示されていた。
                        eliteLogs = listOf(MirrorLog(tag = "AdaptivePortfolio",
                            message = "W$i epoch${epoch + 1} 入口盤面(${AdaptiveHypothesisEpochPolicy.roleLabel(assignment)})をエリート採用 HARD=${startReport.hard} total=${startReport.total}"))
                    }
                    var startImprovedGlobal = false
                    synchronized(lock) {
                        sharedTrajectories[i] = start.copy2D()
                        if (better(startReport, globalReport)) {
                            globalBest = start.copy2D(); globalReport = startReport
                            globalLogs = eliteLogs   // [3.278.0] 同上: グローバル側の stale ログも同期
                            startImprovedGlobal = true
                        }
                    }
                    if (startImprovedGlobal) {
                        globalImproves.incrementAndGet()
                        onProgress(
                            "適応portfolio W$i ${AdaptiveHypothesisEpochPolicy.roleLabel(assignment)} 入口改善",
                            startReport, iterations, nowMs() - started,
                        )
                    }

                    val remainingSec = ((deadline - nowMs() + 999L) / 1000L).toInt().coerceAtLeast(0)
                    val quantum = AdaptiveHypothesisEpochPolicy.quantumSeconds(
                        assignment, improvedPrevious, remainingSec,
                    )
                    if (quantum <= 0) break
                    val roleDeadline = minOf(deadline, nowMs() + quantum * 1000L)
                    val roleIndex = i + reassignments * 8
                    // [並列SA本格再有効化, 3.371.0/既定OFF] 通常はロール1本=内部チェーン1本（希釈回避、
                    //   workers==コア数のときの安全な既定）。トグルONのときだけコア数以内で複数チェーンへ
                    //   広げる（詳細は PolishGate.portfolioRoleParallelSa の docstring）。
                    //   [3.372.0] 実配線と診断ログ表示が同じ値を読むよう portfolioRoleChainCount へ集約。
                    val roleWorkers = portfolioRoleChainCount()
                    val roleOptions = options.copy(
                        workers = roleWorkers,
                        seed = roleSeed,
                        explore = when (assignment.role) {
                            HypothesisEpochRole.HARD_DEBT_RSI_PLUS,
                            HypothesisEpochRole.LARGE_DESTROY_ALNS,
                            HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS -> max(2.0, roleExploreFor(roleIndex))
                            else -> roleExploreFor(roleIndex)
                        },
                        accept = roleAcceptFor(roleIndex),
                        opSelect = roleOpSelectFor(roleIndex),
                        tabu = assignment.role != HypothesisEpochRole.BASELINE_REFINE,
                    )
                    val stopRole = {
                        shouldStop() || nowMs() >= roleDeadline
                    }
                    val result = try {
                        val progress: (String, ViolationReport?, Long, Long) -> Unit = { phase, rep, it, elapsed ->
                            if (rep?.hard == 0) hardZeroWinner.compareAndSet(-1, i)   // 記録のみ（キルしない）
                            if (i == 0 || rep?.hard == 0) {
                                onProgress(
                                    "適応portfolio W$i epoch${epoch + 1} ${AdaptiveHypothesisEpochPolicy.roleLabel(assignment)} / $phase",
                                    rep, it, elapsed,
                                )
                            }
                        }
                        when (assignment.algorithm) {
                            V6Algorithm.ALNS -> runAlns(state, start.copy2D(), roleOptions, quantum, stopRole, progress)
                            V6Algorithm.RSI -> runRsi(state, start.copy2D(), roleOptions, quantum, stopRole, progress, workerHf63)
                            else -> runRsiPlus(state, start.copy2D(), roleOptions, quantum, stopRole, progress, workerHf63)
                        }
                    } catch (ce: kotlinx.coroutines.CancellationException) {
                        throw ce
                    } catch (e: Exception) {
                        firstError.compareAndSet(null, e)
                        null
                    }

                    if (result != null) {
                        if (result.report.hard == 0) hardZeroWinner.compareAndSet(-1, i)   // 記録のみ
                        iterations += result.iterations
                        archive.register(
                            result.schedule, result.report, assignment.role, i, epoch,
                            bridge = result.report.hard == snapshot.second.hard + 1,
                        )
                        trajectory = result.schedule.copy2D()
                        if (better(result.report, eliteReport)) {
                            elite = result.schedule.copy2D()
                            eliteReport = result.report
                            eliteLogs = result.phaseLogs
                        }
                        var improvedGlobal = false
                        synchronized(lock) {
                            sharedTrajectories[i] = result.schedule.copy2D()
                            if (better(result.report, globalReport)) {
                                globalBest = result.schedule.copy2D()
                                globalReport = result.report
                                globalLogs = result.phaseLogs
                                improvedGlobal = true
                            }
                        }
                        if (improvedGlobal) {
                            globalImproves.incrementAndGet()
                            onProgress(
                                "適応portfolio グローバル最良更新 W$i epoch${epoch + 1}",
                                result.report, iterations, nowMs() - started,
                            )
                        }
                    }

                    // [3.282.0] エポック改善＝自己エリートの前進（入口盤面の採用・ロール結果の採用いずれも
                    //   eliteReport 更新経由でここに反映される）。escape ロールが摂動入口に勝っただけでは
                    //   改善と数えない＝plateau 再配属・強度昇圧・役割回転が文書どおり機能する。
                    val improvedThisEpoch = better(eliteReport, preEpochEliteReport)
                    stagnantEpochs = AdaptiveHypothesisEpochPolicy.nextStagnantEpochs(
                        stagnantEpochs, improvedThisEpoch,
                    )
                    val nearest = synchronized(lock) {
                        var d = Int.MAX_VALUE
                        for (x in 0 until workers) if (x != i) {
                            d = minOf(d, scheduleDistance(trajectory, sharedTrajectories[x]))
                        }
                        d
                    }
                    if (escapeController != null) {
                        // [3.306.0/既定OFF経路] stagnantEpochs をリセットしない＝停滞の深さが役割変更を
                        //   跨いで残り、target → 再結合 → 多様化 → 深い破壊 の4段へ進める。
                        TuningTelemetry.escapeControlUsed.incrementAndGet()
                        val next = escapeController.nextAssignment(
                            current = controlledAssignment!!,
                            report = eliteReport,
                            plateauDepth = stagnantEpochs,
                            nearestOtherDistance = nearest,
                            hasOtherHypothesis = workers > 1,
                        )
                        val roleChanged = next.role != controlledAssignment!!.role
                        if (roleChanged) reassignments++
                        controlledAssignment = next
                        // [3.308.0] 役割が変わった直後は改善直後の長い量子を引き継がない
                        //   （既定経路が元から守っていた契約。制御器経路だけ抜けていた）。
                        improvedPrevious = AdaptiveHypothesisEpochPolicy
                            .carriesImprovingQuantum(improvedThisEpoch, roleChanged)
                    } else if (AdaptiveHypothesisEpochPolicy.shouldReassign(
                            index = i,
                            improvedThisEpoch = improvedThisEpoch,
                            stagnantEpochs = stagnantEpochs,
                            nearestOtherDistance = nearest,
                        )
                    ) {
                        reassignments++
                        stagnantEpochs = 0
                        // [3.308.1/敵対検証] 既定経路はこの分岐で常に基準量子へ戻していた（旧
                        //   `improvedPrevious = false`）。roleChanged=true はその挙動を保つための
                        //   引数であって「役割が必ず変わる」という主張ではない。実際 W4 は
                        //   再配属2回目以降 ELITE_RELINK のまま変わらない
                        //   （assignmentFor(4, r>=1) は常に ELITE_RELINK）。W1/2/3/5/6/7 は
                        //   escapeRoles の index が1つ進むので必ず変わる。
                        improvedPrevious = AdaptiveHypothesisEpochPolicy
                            .carriesImprovingQuantum(improvedThisEpoch, roleChanged = true)
                    } else {
                        improvedPrevious = AdaptiveHypothesisEpochPolicy
                            .carriesImprovingQuantum(improvedThisEpoch, roleChanged = false)
                    }
                    // [3.282.0] 集計はロールが実際に走ることが確定してから（旧: quantum<=0 break の
                    //   前に merge しており、締切間際に「実行していないロール」が1件多く summary に載っていた）。
                    // [3.308.1/敵対検証] 回数と秒をここで同時に数える。旧実装は回数だけをエポック
                    //   冒頭で加算していたため、例外で break したエポックが回数には入るのに秒には
                    //   入らず、さらに epoch++ もされないので sum(roleRuns) > epochs になっていた
                    //   （ログの角括弧の合計が epoch 数と合わない）。両方をここに置けば
                    //   sum(roleRuns) == epochs == roleMillis の母集団が常に成り立つ。
                    //   なお `quantum <= 0` と例外の break はここへ到達しないため、その回の
                    //   摂動＋フル検査の時間は秒合計に入らない。実測でも 8ワーカー×300秒=2400 に対し
                    //   計2396s と数秒少なく出る（各ワーカーの最後の1回ぶん）。
                    roleRuns.merge(assignment.role, 1, Int::plus)
                    roleMillis.merge(assignment.role, nowMs() - epochT0, Long::plus)
                    epoch++
                    } catch (ce: kotlinx.coroutines.CancellationException) {
                        throw ce
                    } catch (e: Exception) {
                        // [3.278.0] epoch単位の隔離: このワーカーだけ停止し、蓄積済みエリートを成果として返す。
                        firstError.compareAndSet(null, e)
                        exitReason = "例外"
                        break
                    }
                }
                // [3.346.0] while 条件のどれで抜けたかを確定
                //   （例外と確認済み停滞シグナルは上で確定済み。ここは単調な2条件のみ）。
                if (exitReason.isEmpty()) {
                    exitReason = "締切"   // [3.376.0] 勝者キル撤廃により、単調な離脱条件は締切のみ
                }
                val exitAtSec = (nowMs() - started) / 1000

                AdaptiveWorkerOutcome(
                    elite = elite,
                    report = eliteReport,
                    logs = eliteLogs,
                    lastRole = (controlledAssignment
                        ?: AdaptiveHypothesisEpochPolicy.assignmentFor(i, reassignments)).role,
                    exitReason = exitReason,
                    exitAtSec = exitAtSec,
                    survivedStops = survivedStops,
                    iterations = iterations,
                    epochs = epoch,
                    reassignments = reassignments,
                    roleRuns = roleRuns,
                    roleMillis = roleMillis,
                    hf63Avoided = workerHf63.infeasibleFamilies(),
                )
            }
        }

        val outcomes = jobs.map { d -> d.await() }
        ensureActive()
        for ((index, o) in outcomes.withIndex()) {
            archive.register(
                o.elite, o.report,
                o.lastRole,
                index, o.epochs, bridge = o.report.hard == globalReport.hard + 1,
            )
            if (better(o.report, globalReport)) {
                globalBest = o.elite.copy2D(); globalReport = o.report; globalLogs = o.logs
            }
        }
        val compressedElites = archive.snapshot(globalBest, globalReport)
        val alts = compressedElites.asSequence()
            .filter { !it.bridge }
            .filter { !AdaptiveEliteArchive.sameSchedule(it.schedule, globalBest) }
            .map { it.schedule.copy2D() }
            .take(3)
            .toList()
        // [3.335.0] まずこの実行のスロットへ。static は新しい実行が勝つライブ表示用なので所有時のみ。
        runSlot()?.let { it.fusionElites = compressedElites; it.alternatives = alts }
        if (ownsStatics(runSlot())) { lastFusionElites = compressedElites; lastAlternatives = alts }

        // [3.332.0/実機ログで判明] 旧表記は「圧縮elite=N 相異なるelite=M 距離=a..b」を1行に並べていたが、
        //   **M と 距離 の母集団が違った**（M=アーカイブの圧縮エリート／距離=8ワーカーの最終解）。
        //   読むと「10件すべて相異なるのに最小距離0」という矛盾に見える。しかも M は恒真値だった
        //   （`register` が sameSchedule で重複を弾き `snapshot` も filterNot で除くので、
        //   圧縮エリートは常に相異なる＝実機ログでも2実行とも 10/10）。
        //   意味があるのは**ワーカー解が潰れているか**（同一解に収束＝並列の無駄）なので、そちらを出す。
        val distinctWorkers = outcomes.map { o ->
            o.elite.joinToString("|") { it.joinToString(",") }
        }.distinct().size
        val pairDistances = ArrayList<Int>()
        for (i in outcomes.indices) for (j in i + 1 until outcomes.size) {
            pairDistances.add(scheduleDistance(outcomes[i].elite, outcomes[j].elite))
        }
        val distanceNote = if (pairDistances.isEmpty()) "対象外" else
            "${pairDistances.minOrNull()}..${pairDistances.maxOrNull()}セル" +
                (if (distinctWorkers < outcomes.size) "・同一解あり" else "")
        val roleNote = outcomes.indices.joinToString(" | ") { i ->
            val o = outcomes[i]
            val used = o.roleRuns.entries.joinToString(",") { e ->
                val sec = (o.roleMillis[e.key] ?: 0L) / 1000.0
                "${e.key.name}x${e.value}/${"%.0f".format(sec)}s"
            }
            val avoided = if (o.hf63Avoided.isEmpty()) "" else "/HF63回避=${o.hf63Avoided.joinToString("+")}"
            val survived = if (o.survivedStops == 0) "" else "/停滞見送り${o.survivedStops}回"
            "W${i}:epoch${o.epochs}/再配属${o.reassignments}[$used]$avoided/離脱=${o.exitReason}@${o.exitAtSec}s$survived"
        }
        // [3.307.0/ログ強化] 全ワーカー横断の役割別 worker秒。予算配分を論じるときに見るのはここ
        //   （量子は「1エポックの要求長」であって消費ではない＝両者を取り違えると偏りの診断を誤る）。
        val roleTotals = LinkedHashMap<HypothesisEpochRole, Long>()
        for (o in outcomes) for ((r, ms) in o.roleMillis) roleTotals.merge(r, ms, Long::plus)
        val totalWorkerMs = roleTotals.values.sum().coerceAtLeast(1L)
        // [3.409.4] 外側ワーカーの実効並列度。片肺化（設定8なのに実質1本）を数字1つで検出する。
        val outerParallelism = observedOuterParallelism(totalWorkerMs, nowMs() - started)
        val budgetNote = roleTotals.entries.sortedByDescending { it.value }.joinToString(" ") { e ->
            "${e.key.name}=${e.value / 1000}s(${e.value * 100 / totalWorkerMs}%)"
        }
        // [3.346.0/実機ログ] 締切前に離脱したワーカーを1行で明示する。`shouldStop` は単調でないため
        //   一瞬の停滞シグナルでポーリングが当たったワーカーだけが恒久的に抜け、残りは走り続ける
        //   （実機 2026-08-03: 8本中4本が 115〜116s で離脱＝残り159秒を半分の並列度で走っていた）。
        //   旧ログは役割別worker秒を手で足さないと気づけなかった。
        //   [3.346.1] 一瞬のシグナルは confirmStop が見送るので、ここに出る停滞シグナルは
        //   確認窓を通った本物。見送り回数も併記して「何回きわどい発火があったか」を残す。
        //   [3.409.16] 早期離脱の判定は isEarlyWorkerExit（KDoc参照＝「探索締切」は正常終了）。
        val earlyExits = outcomes.filter { isEarlyWorkerExit(it.exitReason) }
        val survivedTotal = outcomes.sumOf { it.survivedStops }
        val survivedNote = if (survivedTotal == 0) "" else " 停滞見送り計${survivedTotal}回"
        val exitNote = (if (earlyExits.isEmpty()) "ワーカー離脱=全て締切まで実行" else
            "ワーカー離脱=${earlyExits.size}/${outcomes.size}本が締切前(" +
                earlyExits.groupBy { it.exitReason }.entries.joinToString(",") { e ->
                    "${e.key}${e.value.size}本@${e.value.joinToString("/") { "${it.exitAtSec}s" }}"
                } + ")") + survivedNote
        val summary = MirrorLog(
            tag = "AdaptivePortfolio",
            // [3.360.0] 合計iter と 最良更新回数 を併記。MultiWorker/AlnsChains/V5 は元から合計iterを出すのに
            //   PORTFOLIO（予算211秒以上の既定経路＝実機の主経路）だけが出しておらず、規模の比較ができなかった。
            message = "合計iter=${outcomes.sumOf { it.iterations }} 全体最良更新=${globalImproves.get()}回 / " +
                "非同期適応仮説 archive=${archive.size()} 圧縮elite=${compressedElites.size} " +
                "ワーカー解=${outcomes.size}本(相異なる${distinctWorkers}本) 距離=$distanceNote / $exitNote / " +
                "役割別worker秒(計${totalWorkerMs / 1000}s・実効外側並列=${"%.2f".format(outerParallelism)}): $budgetNote / $roleNote" +
                (firstError.get()?.let { " / 一部例外=${it.message}" } ?: "") +
                " / 採用 HARD=${globalReport.hard} total=${globalReport.total}",
        )
        val logs = globalLogs + summary
        V6OptimizerResult(
            globalBest,
            globalReport.copy(logs = logs + globalReport.logs),
            V6Algorithm.PORTFOLIO,
            logs,
            outcomes.sumOf { it.iterations },
            nowMs() - started,
        )
    }

    private fun adaptiveEpochStart(
        state: MagiState,
        globalBest: Array<IntArray>,
        localTrajectory: Array<IntArray>,
        peers: Array<Array<IntArray>>,
        assignment: HypothesisEpochAssignment,
        seed: Long,
        shouldStop: () -> Boolean,
    ): Array<IntArray> {
        val p = cachedProblem(state)
        val rng = Random(seed)
        val n = max(1, assignment.intensity)
        return when (assignment.role) {
            HypothesisEpochRole.BASELINE_REFINE -> localTrajectory.copy2D()
            HypothesisEpochRole.ELITE_RELINK -> {
                val alternatives = peers.asSequence()
                    .filter { scheduleDistance(globalBest, it) > 0 }
                    .sortedByDescending { scheduleDistance(globalBest, it) }
                    .take(3).map { it.copy2D() }.toList()
                val relinked = elitePathRelink(state, globalBest, alternatives, shouldStop).first
                if (scheduleDistance(globalBest, relinked) > 0) relinked
                else hypothesisStartFor(state, globalBest, 7, seed)
            }
            HypothesisEpochRole.DAY_BLOCK_ALNS -> globalBest.copy2D().also { out ->
                if (p.T > 0) {
                    val first = rng.nextInt(p.T)
                    repeat(n * 2) { x -> destroyRepairDayAt(state, out, (first + x) % p.T, rng) }
                }
            }
            HypothesisEpochRole.HARD_FAMILY_RSI -> {
                var out = globalBest.copy2D()
                repeat(n) {
                    val rep = UnifiedViolationChecker.check(state, out)
                    val focus = when {
                        (rep.breakdown["covU"] ?: 0) > 0 -> "covU"
                        (rep.breakdown["c3n"] ?: 0) > 0 -> "c3n"
                        else -> maxViolatedFamily(rep)
                    }
                    out = rsiGenerateHypothesis(state, out, rep, focus, rng)
                }
                out
            }
            HypothesisEpochRole.HARD_DEBT_RSI_PLUS -> globalBest.copy2D().also { out ->
                forceDiverseKick(p, out, rng, 2 + n)
            }
            HypothesisEpochRole.LARGE_DESTROY_ALNS -> globalBest.copy2D().also { out ->
                repeat(n * 2) {
                    if (p.T > 0) destroyRepairDayAt(state, out, rng.nextInt(p.T), rng)
                    if (p.S > 0) destroyRepairStaffAt(state, out, rng.nextInt(p.S), rng)
                }
            }
            HypothesisEpochRole.PERSONAL_RSI -> {
                var out = globalBest.copy2D()
                repeat(n) {
                    val rep = UnifiedViolationChecker.check(state, out)
                    val focus = when {
                        (rep.breakdown["apt"] ?: 0) > 0 -> "apt"
                        (rep.breakdown["high"] ?: 0) > 0 -> "high"
                        (rep.breakdown["low"] ?: 0) > 0 -> "low"
                        (rep.breakdown["fair"] ?: 0) > 0 -> "fair"
                        else -> "total"
                    }
                    out = rsiGenerateHypothesis(state, out, rep, focus, rng)
                }
                out
            }
            HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS -> globalBest.copy2D().also { out ->
                forceMaxDistanceKick(p, out, peers, rng, 3 + n)
            }
        }
    }

    private fun forceMaxDistanceKick(
        p: Problem,
        out: Array<IntArray>,
        peers: Array<Array<IntArray>>,
        rng: Random,
        target: Int,
    ) {
        if (p.S == 0 || p.T == 0) return
        var changed = 0
        var attempts = 0
        val touched = HashSet<Long>()
        while (changed < target && attempts++ < max(64, p.S * p.T * 6)) {
            val i = rng.nextInt(p.S); val j = rng.nextInt(p.T)
            val key = i.toLong() * max(1, p.T) + j
            if (!touched.add(key) || p.wishLocked(i, j)) continue
            val old = out[i][j]
            val allowed = p.allowedShiftsForStaff(i).filter { it != old }
            if (allowed.isEmpty()) continue
            var bestK = -1; var bestFreq = Int.MAX_VALUE; var tied = 0
            for (k in allowed) {
                val freq = peers.count { peer -> peer.getOrNull(i)?.getOrNull(j) == k }
                if (freq < bestFreq) { bestFreq = freq; bestK = k; tied = 1 }
                else if (freq == bestFreq) {
                    tied++
                    if (HypothesisDiversityPolicy.takeReservoirTie(tied, rng)) bestK = k
                }
            }
            if (bestK >= 0) { out[i][j] = bestK; changed++ }
        }
    }

    /**
     * Run up to [w] independent hypotheses concurrently (distinct seeds) and keep the best —
     * the native W0..Wn multi-worker pool with the spec's hybrid termination (§2.2/§4.2):
     *  - 絶対評価: the first hypothesis to reach the pass line (HARD=0) is recorded as the winner.
     *    [3.376.0] It no longer cancels the others: once HARD=0 is reached the remaining work is all
     *    SOFT, and keeping a single worker wastes the parallelism the user asked for.
     *  - 相対評価: if none passes by the deadline, the lowest-penalty hypothesis is adopted.
     * Worker 0's progress is forwarded, prefixed with the number of hypotheses still running.
     */
    private suspend fun runMultiWorker(
        w: Int,
        options: V6OptimizerOptions,
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
        run: suspend (Int, V6OptimizerOptions, (String, ViolationReport?, Long, Long) -> Unit) -> V6OptimizerResult,
    ): V6OptimizerResult = kotlinx.coroutines.supervisorScope {
        // [3.371.0/並列SA本格再有効化] spawn数×チェーン内訳は hypothesisSpawnPlan（単一ソース）から。
        val (hSpawn, plan) = hypothesisSpawnPlan(options.workers, w)
        if (hSpawn <= 1) return@supervisorScope run(0, options.copy(workers = plan[0]), onProgress)
        val base = actualSeed(options.seed)
        val completed = java.util.concurrent.atomic.AtomicInteger(0)
        val winner = java.util.concurrent.atomic.AtomicInteger(-1)
        // [レビュー#2 3.213.0] 全ワーカー横断の最良(hard→weighted→total)を追跡し、どのワーカーの改善も
        //   外側へ転送する。旧: i==0 のみ転送＝W1..W4 だけが改善を続ける局面で外側ウォッチドッグ
        //   (V6FinalPort の停滞時計)が改善を観測できず、HARD平坦時の短い猶予(stallHardMs)で全ワーカーを
        //   早期停止し得た。非改善レポートまで転送すると phase 文字列が W0 と交互に振れて外側の
        //   フェーズ遷移リセット（意図的な猶予付与）を偽発火させるため、改善時のみ転送する。
        val sharedBest = java.util.concurrent.atomic.AtomicReference<ViolationReport?>(null)
        fun improvesShared(r: ViolationReport): Boolean {
            while (true) {
                val cur = sharedBest.get()
                if (cur != null && !better(r, cur)) return false
                if (sharedBest.compareAndSet(cur, r)) return true
            }
        }
        // [敵対的レビュー修正・#4例外隔離] supervisorScope＋仮説ごとのtry/catchで、1仮説の通常例外が
        //   他仮説を道連れにしない（runAlnsChainsと同型のパターンへ統一）。firstErrorは全滅時のみ使用。
        val firstError = java.util.concurrent.atomic.AtomicReference<Throwable?>(null)
        // [敵対的レビュー修正・#5早期winner] 全ジョブをLAZYで生成してから一斉start。生成前のjobsはnullでは
        //   なくLAZYな未開始Deferredなので、早いwinnerのcancel()がまだstart前のジョブにも正しく効く
        //   （旧実装はjobs配列がnullのままcancel()を呼んでも無効化されず、後から作られる新規ジョブが
        //   キャンセルを免れて走ってしまっていた）。
        val jobs = arrayOfNulls<kotlinx.coroutines.Deferred<V6OptimizerResult?>>(hSpawn)
        for (i in 0 until hSpawn) {
            jobs[i] = async(Dispatchers.Default, start = kotlinx.coroutines.CoroutineStart.LAZY) {
                // 開始時点で既に勝者が確定していれば(まれな競合)何もせず抜ける。
                if (winner.get() >= 0 && winner.get() != i) return@async null
                try {
                    // [HF290 役割分担＋論文活用] 各仮説に探索/精製プロファイル＋受理基準(SA/GD)を割当て多様化（W0=ベースライン）。
                    run(i, options.copy(workers = plan[i], seed = base + (i + 1) * 0x9E3779B1L, explore = roleExploreFor(i), accept = roleAcceptFor(i), opSelect = roleOpSelectFor(i))) { phase, report, iters, elapsed ->
                        val improved = report != null && improvesShared(report)
                        if (i == 0 || improved) onProgress("仮説${(hSpawn - completed.get()).coerceAtLeast(1)}本探索中 / $phase", report, iters, elapsed)
                        // 絶対評価: 合格ライン(HARD=0)に最初に到達した仮説が、残りを即キャンセル
                        // [3.376.0] 「HARD=0 到達で残りを即キャンセル」を撤廃（runAdaptivePortfolio と同じ理由:
                        //   到達後に残る仕事は全部 SOFT で、1本に絞ると指定した並列度が無駄になる）。
                        //   winner は「誰が最初に合格したか」の記録として残す（下のログ表記に使う）。
                        if (report != null && report.hard == 0) winner.compareAndSet(-1, i)
                    }
                } catch (ce: kotlinx.coroutines.CancellationException) {
                    throw ce
                } catch (e: Exception) {
                    firstError.compareAndSet(null, e)
                    null
                } finally {
                    completed.incrementAndGet()
                }
            }
        }
        jobs.forEach { it?.start() }
        val results = jobs.mapNotNull { d ->
            try { d?.await() } catch (_: kotlinx.coroutines.CancellationException) { null }
        }
        // 兄弟キャンセル(自己)とユーザー停止(外部)を区別: 外部停止ならここで伝播させる。
        ensureActive()
        val best = if (results.isEmpty()) run(0, options.copy(workers = plan[0]), onProgress)
        else results.reduce { a, b -> if (better(b.report, a.report)) b else a }
        // 「他の案」: 採用案以外の仮説結果を品質順に保持（重複schedule除外、最大3件）
        val alts = results.asSequence()
            .filter { it !== best }
            .sortedWith(compareBy(reportComparator) { it.report })
            .map { it.schedule }
            .distinctBy { sch -> sch.joinToString("|") { it.joinToString(",") } }
            .take(3)
            .toList()
        runSlot()?.alternatives = alts                      // [3.335.0] この実行の「他の案」
        if (ownsStatics(runSlot())) lastAlternatives = alts
        val totalIters = results.sumOf { it.iterations }
        val mode = if (winner.get() >= 0) "合格あり(全本継続)" else "時間内最良採用"
        val chainNote = if (plan.max() > 1) "・仮説内${plan.min()}〜${plan.max()}並列(SA/ALNS多チェーン、設定${options.workers}がコア数を超えるため仮説数を絞り並列SAへ配分)" else ""
        val failNote = if (results.size < hSpawn) "・失敗${hSpawn - results.size}本(例外/キャンセル${firstError.get()?.let { "・${it.message}" } ?: ""})" else ""
        // [3.266.0/hypothesis basin diversity] 各仮説の入口が実際にどう多様化されたかをログに残す。
        val entryRoles = (0 until hSpawn).joinToString(" ") { i ->
            val sp = HypothesisDiversityPolicy.startPlanFor(i)
            "W$i=${sp.mode.name.removeSuffix("_REPAIR")}${if (sp.intensity > 0) "x${sp.intensity}" else ""}"
        }
        val hypNote = if (hSpawn < w) "${hSpawn}本(設定仮説数${w}をコア数まで縮小)" else "${hSpawn}本"
        val extra = MirrorLog(tag = "MultiWorker", message = "仮説 $hypNote ($mode・役割分担:探索/精製＋受理SA/GreatDeluge多様化$chainNote$failNote) → 採用 HARD=${best.report.hard} total=${best.report.total} 合計iter=${totalIters} / 入口役割 $entryRoles")
        // [過程検証] 各仮説の個別結果・多様性（相異なる解の数）・保持した他の案数をログ化し、探索過程を後から検証できるようにする。
        //   各仮説の合計が揃っていれば収束、ばらけていれば多様な探索ができている、と判別できる。
        val perHyp = results.sortedWith(compareBy(reportComparator) { it.report })
            .joinToString("  ") { r -> "[必須${r.report.hard}/合計${r.report.total}${if (r === best) "★採用" else ""}]" }
        val distinctSols = results.map { r -> r.schedule.joinToString("|") { row -> row.joinToString(",") } }.distinct().size
        val pairDistances = ArrayList<Int>()
        for (a in results.indices) for (b in a + 1 until results.size) {
            pairDistances.add(AdaptiveEliteArchive.scheduleDistance(results[a].schedule, results[b].schedule))
        }
        val distanceNote = if (pairDistances.isEmpty()) "解間距離=対象外" else
            "解間距離=${pairDistances.minOrNull()}..${pairDistances.maxOrNull()}セル"
        val verifyLog = MirrorLog(tag = "仮説検証", message = "各仮説 ${results.size} 本の結果: $perHyp / 相異なる解=${distinctSols}件 / $distanceNote / 他の案として保持=${lastAlternatives.size}件")
        best.copy(phaseLogs = best.phaseLogs + extra + verifyLog, iterations = totalIters)
    }

    /** Roulette-wheel operator selection for the adaptive LNS. */
    private fun rouletteSelect(weights: DoubleArray, rng: Random): Int {
        var sum = 0.0
        for (wgt in weights) sum += wgt
        if (sum <= 0.0) return rng.nextInt(weights.size)
        var r = rng.nextDouble() * sum
        for (i in weights.indices) {
            r -= weights[i]
            if (r <= 0.0) return i
        }
        return weights.size - 1
    }

    /** [Thompson sampling] 演算子選択。平滑報酬 opW を事後平均、探索ノイズを反復で減衰させた
     *  ガウス事後から各演算子の標本を引き、最大の演算子を選ぶ。重み比例(roulette)より停滞しにくく、
     *  不確実性下での選択が原理的。ノイズσは序盤大きく(探索)→終盤小さく(活用)アニールする。 */
    private fun thompsonSelect(opW: DoubleArray, iter: Long, rng: Random): Int {
        val sigma = 0.5 / sqrt(1.0 + iter / 500.0)
        var bestOp = 0
        var bestSample = Double.NEGATIVE_INFINITY
        for (k in opW.indices) {
            val u1 = rng.nextDouble().coerceIn(1e-9, 1.0)
            val u2 = rng.nextDouble()
            val g = sqrt(-2.0 * ln(u1)) * cos(2.0 * PI * u2)   // Box-Muller 標準正規
            val s = opW[k] + g * sigma
            if (s > bestSample) { bestSample = s; bestOp = k }
        }
        return bestOp
    }

    fun chooseAlgorithm(requested: V6Algorithm, budgetSec: Int): V6Algorithm {
        if (requested != V6Algorithm.AUTO) return requested
        // [3.266.0] 211秒以上は同型RSI++クローン群でなく、ALNS/RSI/RSI++の異種PORTFOLIOを使う
        //   （HypothesisDiversityPolicy.autoAlgorithmForBudget、旧 portfolioAlgoFor は
        //   runAdaptivePortfolio への置換で不要化したため削除）。
        return HypothesisDiversityPolicy.autoAlgorithmForBudget(budgetSec)
    }

    private suspend fun runV5(
        state: MagiState,
        initial: Array<IntArray>,
        options: V6OptimizerOptions,
        budgetSec: Int,
        shouldStop: () -> Boolean = { false },
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
    ): V6OptimizerResult {
        val t0 = nowMs()
        val p = Problem(state.withSchedule(initial))
        val ev = Evaluator(p)
        var lastReport: ViolationReport? = null
        // [HF290 役割分担] explore 倍率で初期温度を調整（探索=高温/精製=低温）。explore=1.0 は従来と同一。
        val saT0 = (10.0 * options.explore).coerceIn(2.0, 40.0)
        val res = SaOptimizer(p, ev).run(
            SaParams(t0 = saT0, workers = clampWorkersToCores(options.workers), budgetMs = budgetSec * 1000L, softPolish = options.softPolish, shouldStop = shouldStop, seed = options.seed),
        ) { pr ->
            if (pr.elapsedMs % 1000L < 220L) onProgress("V5 SA", lastReport, pr.totalIters, pr.elapsedMs)
        }
        val repaired = hf67HardRepair(state, res.schedule, Random(actualSeed(options.seed) xor 0x5L))
        var outSched = repaired.schedule
        var report = UnifiedViolationChecker.check(state, outSched)
        // [退化防止番兵 / 実機ログ起因] runAlns(578行)と同じ入力比keep-best。従来 runV5 だけ番兵が無く、SA+修復が
        //   入力より悪化した結果をそのまま返していた。RSI++ は Phase1 Seed に runV5 を使い、以降の各段は前段比
        //   keep-best のため、Phase1 の劣化(実測: 入力HARD=1/195 → Seed HARD=2/229)が全チェーンへ伝播し、
        //   最後にディスパッチャ番兵が入力へ復帰＝予算全体が無駄になっていた(実機で 275s×2回)。入力を品質床に
        //   することで以降の全フェーズが「入力以上」から積み上がる。SA が入力より良い解を見つけた場合は素通し
        //   ＝多様化は維持。スコアリング不変(選択のみ・better()=hard→weighted→total)。
        val baseSched = normalizeSchedule(initial, p)
        val baseReport = UnifiedViolationChecker.check(state, baseSched)
        val keptInput = better(baseReport, report)
        if (keptInput) { outSched = baseSched; report = baseReport }
        lastReport = report
        val logs = listOf(MirrorLog(tag = "RunMAGI_V5",
            message = "高速SA完了 HARD=${report.hard} total=${report.total} iter=${res.totalIters}" +
                (if (res.chainWins.isNotEmpty()) {
                    val wins = res.chainWins.count { it > 0 }
                    " SAチェーン${res.chainWins.size}本(最良を更新した本数=$wins" +
                        (if (wins <= 1 && res.chainWins.size > 1) "＝並列を増やした効果は出ていません" else "") + ")"
                } else "") +
                if (keptInput) "（SA結果が入力より悪化のため入力を維持=番兵）" else "")) + repaired.logs
        return V6OptimizerResult(outSched, report.copy(logs = logs + report.logs), V6Algorithm.V5, logs, res.totalIters, nowMs() - t0)
    }

    /** [余剰ワーカー活用/多チェーンALNS] runAlns を [chains] 本、異なるシードで並列実行し keep-best
     *  で最良を採用する（SaOptimizer の多チェーンSAと同型の考え方をALNSへ拡張）。各チェーンは runAlnsSingle
     *  （単一チェーン本体）を直接呼ぶ＝再帰は構造的に不可能（[敵対的レビュー3.212.0] 旧実装は runAlns 経由の
     *  ガード再帰で、無限再帰防止が options.copy(workers=1) 1引数とコメントのみに依存していた）。
     *  restarts・GLS・destroy-repair 等の内部ロジックは一切変更しない。最終選択は全チェーン共通の
     *  better()（hard→weighted→total辞書式）でゲートするため退化不能。
     *  [敵対的レビュー3.212.0で追加した3つの堅牢化]
     *  ①部分結果許容: 1チェーンの非Cancellation例外で兄弟チェーンの有効な結果を道連れにしない（チェーン毎に
     *    捕捉・全滅時のみ最初の例外を再送出=旧単一チェーンと同じ失敗面）。
     *  ②HARD=0早期キャンセル: 非先頭チェーンの合格も検知して兄弟を即キャンセルし（§2.2絶対評価）、合格
     *    reportは c!=0 でも外側 onProgress へ転送＝runMultiWorker の仮説間キャンセルにも見えるようにする。
     *  ③観測性: 仮説検証と同型のチェーン毎結果・相異なる解数・chain0内訳をログ化（全チェーン同一解収束＝
     *    並列の無駄、をログレビューで検出可能に）。 */
    private suspend fun runAlnsChains(
        state: MagiState,
        initial: Array<IntArray>,
        options: V6OptimizerOptions,
        budgetSec: Int,
        shouldStop: () -> Boolean,
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
    ): V6OptimizerResult = coroutineScope {
        val chains = max(1, options.workers)
        val base = actualSeed(options.seed)
        val passed = java.util.concurrent.atomic.AtomicInteger(-1)
        val firstError = java.util.concurrent.atomic.AtomicReference<Throwable?>(null)
        // [レビュー#2 3.213.0] runMultiWorker と同型: チェーン横断の改善も外側へ転送（停滞時計の集約）。
        val sharedBest = java.util.concurrent.atomic.AtomicReference<ViolationReport?>(null)
        fun improvesShared(r: ViolationReport): Boolean {
            while (true) {
                val cur = sharedBest.get()
                if (cur != null && !better(r, cur)) return false
                if (sharedBest.compareAndSet(cur, r)) return true
            }
        }
        // [敵対的レビュー修正・#5早期winner] runMultiWorkerと同型: 全チェーンをLAZYで生成してから一斉start。
        val jobs = arrayOfNulls<kotlinx.coroutines.Deferred<V6OptimizerResult?>>(chains)
        for (c in 0 until chains) {
            jobs[c] = async(Dispatchers.Default, start = kotlinx.coroutines.CoroutineStart.LAZY) {
                if (passed.get() >= 0 && passed.get() != c) return@async null
                try {
                    runAlnsSingle(state, initial.copy2D(), options.copy(workers = 1, seed = base + (c + 1) * 0x2545F4914F6CDD1DL), budgetSec, shouldStop) { phase, report, iters, elapsed ->
                        val won = report != null && report.hard == 0 && passed.compareAndSet(-1, c)
                        if (won) { for (j in 0 until chains) if (j != c) jobs[j]?.cancel() }
                        // 先頭チェーンは常時、非先頭は合格時＋チェーン横断改善時に転送
                        //（合格の可視化がrunMultiWorkerの絶対評価に、改善の可視化が外側停滞時計に必要）。
                        val improved = report != null && improvesShared(report)
                        if (c == 0 || won || improved) onProgress(phase, report, iters, elapsed)
                    }
                } catch (ce: kotlinx.coroutines.CancellationException) {
                    throw ce
                } catch (e: Exception) {
                    firstError.compareAndSet(null, e)
                    null
                }
            }
        }
        jobs.forEach { it?.start() }
        val results = jobs.mapNotNull { d ->
            try { d?.await() } catch (_: kotlinx.coroutines.CancellationException) { null }
        }
        ensureActive()
        if (results.isEmpty()) {
            // 全チェーン失敗（キャンセル起因は上のensureActiveで伝播済＝ここは例外全滅のみ）。
            // 旧単一チェーンと同じ失敗面へ縮退: 最初の例外を再送出（黙って空成功にしない）。
            throw firstError.get() ?: IllegalStateException("runAlnsChains: no chain produced a result")
        }
        val best = results.reduce { a, b -> if (better(b.report, a.report)) b else a }
        val totalIters = results.sumOf { it.iterations }
        val chain0Iters = results.firstOrNull()?.iterations ?: 0L
        val perChain = results.sortedWith(compareBy(reportComparator) { it.report })
            .joinToString("  ") { r -> "[必須${r.report.hard}/合計${r.report.total}${if (r === best) "★採用" else ""}]" }
        val distinctSols = results.map { r -> r.schedule.joinToString("|") { row -> row.joinToString(",") } }.distinct().size
        val failNote = if (results.size < chains) "・失敗${chains - results.size}本(例外/キャンセル)" else ""
        val extra = MirrorLog(tag = "AlnsChains", message =
            "ALNS多チェーン(${chains}並列$failNote) → 採用 HARD=${best.report.hard} total=${best.report.total}" +
                " 合計iter=${totalIters}(先頭chain=${chain0Iters}) / 各チェーン: $perChain / 相異なる解=${distinctSols}件")
        best.copy(phaseLogs = best.phaseLogs + extra, iterations = totalIters)
    }

    /** [敵対的レビュー3.212.0/構造分割] workers の意味過重（設定値/仮説内チェーン数/チェーン内=1）を
     *  ディスパッチャ3行に閉じ込める。本体 runAlnsSingle は workers を一切読まない＝再帰・誤fan-outが
     *  構造的に不可能。既存呼出元（optimize/runRsi/runRsiPlus）のシグネチャは不変。 */
    private suspend fun runAlns(
        state: MagiState,
        initial: Array<IntArray>,
        options: V6OptimizerOptions,
        budgetSec: Int,
        shouldStop: () -> Boolean = { false },
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
    ): V6OptimizerResult {
        if (options.workers > 1) return runAlnsChains(state, initial, options, budgetSec, shouldStop, onProgress)
        return runAlnsSingle(state, initial, options, budgetSec, shouldStop, onProgress)
    }

    private suspend fun runAlnsSingle(
        state: MagiState,
        initial: Array<IntArray>,
        options: V6OptimizerOptions,
        budgetSec: Int,
        shouldStop: () -> Boolean = { false },
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
    ): V6OptimizerResult {
        val started = nowMs()
        val rng = Random(actualSeed(options.seed) xor 0xA17A5L)
        val p = cachedProblem(state)
        val restarts = max(1, options.restarts)
        val per = max(1, budgetSec / restarts)
        var globalBest = normalizeSchedule(initial, p)
        var globalReport = UnifiedViolationChecker.check(state, globalBest)
        // [退化防止] hot-loop は生スコア(DeltaEvaluator)で最良を追うが、生スコアと weightedScore は
        // 目的が異なる（range は生スコアで hard、weightedScore で soft）。最終結果が入力(best)より
        // hard→weighted→total の辞書順で悪化しないよう、開始時の盤面を baseline として保持し最後に番兵比較する。
        val baseBest = globalBest.copy2D()
        val baseReport = globalReport
        var itersTotal = 0L
        val logs = ArrayList<MirrorLog>()
        // [GLS移植] Guided Local Search: 受理(accept-worse)を penalty で誘導し局所最適から脱出。
        // グローバル最良は生スコアで別管理するので、GLSで真の最良を失うことはない。
        val gls = GlsPenalty(p.S, p.T, p.K)
        var lastImproveIter = 0L
        // [差分化移植] DeltaEvaluator を hot-loop のスコア源にして copy2D / 全件 check() を回避する。
        //   - 直接評価アーム(op3-6): eval+cur に直接適用、不採択は反転（copy2D なし／ゼロアロケーション）。
        //   - copy系アーム(op0-2): 変更セルだけを eval へ反映（op0=O(S)列, op1=O(T)行 の targeted 差分）。
        // グローバル最良は生スコア(score)でゲートし、更新時のみ check() で ViolationReport を確定する
        // （SaOptimizer と同じ目的関数。最終的なアルゴリズム間比較は better(ViolationReport) で行う）。
        val eval = DeltaEvaluator(p)
        eval.reset(globalBest)
        var globalScore = eval.score()
        // [高速化/零アロケ] op0-2(copy系)は毎反復 cur.copy2D() を新規確保していた（数百万回/実行のGC圧）。
        //   使い回しのスクラッチ盤面へ arraycopy し、採用時は cur とスワップ（旧 cur が次のスクラッチになる）。
        //   hf67 経由(fixed!==cand)の採用時は fixed が新規配列なのでスクラッチはそのまま次反復で再利用。
        var scratchBuf = Array(p.S) { IntArray(p.T) }
        val diffBuf = IntArray(p.S * p.T)   // scratch: flat indices i*T+j of changed cells (zero-alloc)

        // [ネイティブ加速 Stage8b] runAlns の内側ループ(下の while)を C++ ALNS チャンクへ JNI 委譲する。
        //   Kotlin 保持: restart 境界の perturb+hf67・進捗/liveBest・キャンセル・2層番兵。problem は read-only で
        //   restart 跨ぎ共有。ネイティブ不可 or 番兵発火時は下の従来 Kotlin ループへフォールバック（退化不能）。
        val nativeProblem = if (NativeGate.usable) runCatching { NativeEval.createHandle(p) }.getOrDefault(0L) else 0L
        val fullEvaluator = Evaluator(p)
        val bestFlat = IntArray(p.S * p.T)
        suspend fun runRestartNative(cur: Array<IntArray>, deadline: Long, perSec: Int, r: Int): Boolean {
            val alns = runCatching {
                NativeBridge.nativeAlnsCreate(nativeProblem, NativeEval.flatten(cur), rng.nextLong(),
                    options.accept.ordinal, options.opSelect.ordinal, options.explore)
            }.getOrDefault(0L)
            if (alns == 0L) { NativeGate.disable("ALNS状態生成NG"); return false }
            try {
                // [全体計算の最小化] checker.check と liveBest 全面コピーは表示専用のため 250ms 周期に間引く。
                //   2層目番兵の fullEval（正しさ）は従来どおり改善チャンクごとに実施＝退化不能は不変。
                var reportStale = false
                var lastUiMs = 0L
                val mySlot = runSlot()   // [3.335.0] 非 suspend なローカル関数からは取れないので先に捕まえる
                fun syncReport() {
                    if (reportStale) {
                        globalReport = UnifiedViolationChecker.check(state, globalBest)
                        if (ownsStatics(mySlot)) publishLiveBest(globalReport, globalBest)
                        reportStale = false
                    }
                }
                while (nowMs() < deadline && !shouldStop()) {
                    coroutineContext.ensureActive()
                    val frac = ((deadline - nowMs()).toDouble() / max(1.0, perSec * 1000.0)).coerceIn(0.0, 1.0)
                    val ret = NativeBridge.nativeAlnsChunk(alns, 200, frac)
                    if (ret.size < 6 || ret[0] != 0L) { syncReport(); NativeGate.disable("ALNSチャンク整合性NG(status=${ret.getOrNull(0)})"); return false }
                    itersTotal += ret[4]
                    if (ret[3] == 1L && ret[2] < globalScore) {
                        // [2層目の番兵] best 更新を Kotlin Evaluator でフル照合（Long== 許容誤差なし）。
                        //   [照合トグル] OFF=純ネイティブ（照合せず信頼）。C++自己整合(status)は上で常時検査済。
                        NativeBridge.nativeAlnsRead(alns, 0, bestFlat)
                        val bestSol = NativeEval.unflatten(bestFlat, p.S, p.T)
                        if (NativeGate.parityCheckEnabled) {
                            TuningTelemetry.parityChecks.incrementAndGet()
                            val kScore = fullEvaluator.fullEval(bestSol)
                            if (kScore != ret[2]) { syncReport(); NativeGate.disable("ALNS Kotlin照合NG(native=${ret[2]} kotlin=$kScore)"); return false }
                        }
                        globalBest = bestSol; globalScore = ret[2]
                        lastImproveIter = itersTotal
                        reportStale = true
                    }
                    val nowUi = nowMs()
                    if (reportStale && nowUi - lastUiMs >= 250) { syncReport(); lastUiMs = nowUi }
                    onProgress("ALNS restart ${r + 1}/$restarts", globalReport, itersTotal, nowMs() - started)
                    yield()
                }
                syncReport()   // restart 終端ログ(HARD/total)と liveBest を最終同期
                return true
            } finally {
                NativeBridge.nativeAlnsDestroy(alns)
            }
        }

        try {
        for (r in 0 until restarts) {
            if (shouldStop()) break
            coroutineContext.ensureActive()
            // [restart 摂動] 一律 strength=0.18。非線形スケジュール(2.51)は nsp_bench --real の final 品質で
            //   +101% 悪化と実測されたため revert(序盤の大摂動が強い repair 下で良解を壊し最終品質を損なう)。
            var cur = if (r == 0) globalBest.copy2D() else perturb(state, globalBest, rng, strength = (0.18 * options.explore).coerceIn(0.05, 0.6))
            cur = hf67HardRepair(state, cur, rng).schedule
            val deadline = nowMs() + per * 1000L
            // [Stage8b] ネイティブ ALNS チャンクへ委譲。不可 or 番兵発火なら下の従来 Kotlin ループへ。
            val usedNative = nativeProblem != 0L && NativeGate.enabled && runRestartNative(cur, deadline, per, r)
            if (!usedNative) {
            var curReport = UnifiedViolationChecker.check(state, cur)
            eval.reset(cur)
            var curScore = eval.score()
            // [監査(未レビュー領域再監査) HF77修正] 旧コメント「再構築は restart 毎のみ」は実装と不一致だった。
            //   `gls`(374行目)は本 runAlns 呼出につき1回だけ生成され、restart(for r)ループ間で共有・持ち越される
            //   （decay(654行目)のみが希薄化する）。再構築が起きるのは runAlns が新規に呼ばれた時（RSI各ラウンド/
            //   各並列ワーカー等）のみ。globalBestは生スコアで別管理のため、この共有自体は受理動学にのみ作用し
            //   正しさは不変（keep-best）。
            var curAug = gls.augment(cur)   // [GLS] 現行盤面の penalty 拡張分を増分維持
            // [論文活用] Great Deluge の初期水位＝このリスタート開始時のスコア（時間予定型で best へ降下）。
            val gdInitial = curScore.toDouble()
            var iter = 0L
            // [Adaptive LNS] learned operator weights (roulette-wheel selection + reaction-factor
            // update), per Ropke & Pisinger and recent adaptive-LNS personnel-scheduling work
            // (Ouberkouk, Boufflet & Moukrim, J. Heuristics 2023). Replaces uniform operator choice.
            val opW = DoubleArray(7) { 1.0 }
            val opScore = DoubleArray(7)
            val opCnt = IntArray(7)
            var sinceUpdate = 0
            // [Lam適応冷却] W3 (accept==LAM_ADAPTIVE) のみ使用。観測受理率 lamAcc を Lam-Delosme の
            //   目標受理率(序盤0.44で平坦→中盤で線形降下→終盤≈0)に追従させ、温度 lamTemp を乗算的に自己調整。
            //   温度パラメータの手調整が不要になる。リスタートごとに高温から再開。他ワーカ/W0には無影響。
            var lamTemp = max(1.0, options.explore)
            var lamAcc = 0.44
            fun lamUpdate(accepted: Boolean) {
                lamAcc = 0.97 * lamAcc + 0.03 * (if (accepted) 1.0 else 0.0)
                val f = ((deadline - nowMs()).toDouble() / max(1.0, per * 1000.0)).coerceIn(0.0, 1.0)
                val target = when { f > 0.85 -> 0.44; f > 0.15 -> 0.44 * (f - 0.15) / 0.70; else -> 0.0 }
                lamTemp = (lamTemp * if (lamAcc > target) 0.998 else 1.002).coerceIn(0.03, 4.0)
            }
            while (nowMs() < deadline && !shouldStop()) {
                coroutineContext.ensureActive()
                var op = if (options.opSelect == OpSelectMode.THOMPSON) thompsonSelect(opW, iter, rng)
                         else rouletteSelect(opW, rng)
                // [賢いsoft集中] HARD が最良水準(curHard<=bestHard)に到達したら残り探索を soft 修復へ寄せる。
                //   HARD=0 なら積極的に(0.30)、HARD>0 の床(構造的に解けない covU/pref/c3n 等)では控えめに(0.15)
                //   op5(targeted repair=covO/c2/上下限/c41/c41s/c3Want/apt 修復)を優先。HARD>床 の間はHARD優先で不変。
                //   従来 curHard==0 限定だと、構造的HARD床から下がれない局面で soft研磨が一度も起動しなかった
                //   (apt超過/fair が放置)。最良HARD水準なら床>0 でも soft を磨くよう修正。
                val softFocusProb = if (globalScore / SCORE_HARD_UNIT == 0L) 0.30 else 0.15
                if (curScore / SCORE_HARD_UNIT <= globalScore / SCORE_HARD_UNIT && rng.nextDouble() < softFocusProb) op = 5
                // [HF290 役割分担] explore 倍率で受理温度を調整（探索=受理寛容/精製=厳格）。explore=1.0 は従来と同一。
                //   ただし LAM_ADAPTIVE は受理率追従の適応温度 lamTemp を使う（自己調整）。
                val temp = if (options.accept == AcceptMode.LAM_ADAPTIVE) lamTemp
                           else max(0.03, (deadline - nowMs()).toDouble() / max(1.0, per * 1000.0) * options.explore)
                val curHard = curScore / SCORE_HARD_UNIT
                val gdLevel = if (options.accept == AcceptMode.GREAT_DELUGE) {
                    val frac = ((deadline - nowMs()).toDouble() / max(1.0, per * 1000.0)).coerceIn(0.0, 1.0)
                    greatDelugeLevel(gdInitial, globalScore.toDouble(), frac)
                } else 0.0
                var reward = 0.2   // default: rejected / no-op

                if (op in 3..6) {
                    // ── 直接評価パス(op3-6): copy2D なし。eval+cur に直接適用し、不採択は反転 ──
                    // 不変条件: eval.at(i,j) == cur[i][j] が常時成立。変更セル(≤2)を保持して反転に使う。
                    var moved = false
                    var ns = curScore
                    var moveAug = 0.0
                    var c0i = -1; var c0j = -1; var c0old = -1
                    var c1i = -1; var c1j = -1; var c1old = -1
                    when {
                        op == 3 && p.S > 0 && p.T >= 2 -> {   // 同一職員の2日入替
                            val i = rng.nextInt(p.S)
                            var ja = rng.nextInt(p.T); var jb = rng.nextInt(p.T)
                            if (ja == jb) jb = (jb + 1) % p.T
                            if (!p.wishLocked(i, ja) && !p.wishLocked(i, jb)) {
                                val ka = eval.at(i, ja); val kb = eval.at(i, jb)
                                if (ka != kb) {
                                    eval.apply(i, ja, kb); eval.apply(i, jb, ka)
                                    c0i = i; c0j = ja; c0old = ka; c1i = i; c1j = jb; c1old = kb
                                    moveAug = glsMoveAug(gls, i, ja, ka, kb) + glsMoveAug(gls, i, jb, kb, ka)
                                    ns = eval.score(); moved = true
                                }
                            }
                        }
                        op == 4 && p.S > 0 && p.T > 0 -> {   // randomAllowedCell
                            val i = rng.nextInt(p.S); val j = rng.nextInt(p.T)
                            if (!p.wishLocked(i, j)) {
                                val allowed = p.allowedShiftsForStaff(i)
                                if (allowed.isNotEmpty()) {
                                    val oldK = eval.at(i, j); val nw = allowed[rng.nextInt(allowed.size)]
                                    if (nw != oldK) {
                                        eval.apply(i, j, nw)
                                        c0i = i; c0j = j; c0old = oldK
                                        moveAug = glsMoveAug(gls, i, j, oldK, nw)
                                        ns = eval.score(); moved = true
                                    }
                                }
                            }
                        }
                        op == 5 -> {   // targeted single-cell repair (direct-eval)
                            val fix = findTargetedFix(p, eval, rng)
                            if (fix != null) {
                                val oldK = eval.at(fix[0], fix[1])
                                if (fix[2] != oldK) {
                                    eval.apply(fix[0], fix[1], fix[2])
                                    c0i = fix[0]; c0j = fix[1]; c0old = oldK
                                    moveAug = glsMoveAug(gls, fix[0], fix[1], oldK, fix[2])
                                    ns = eval.score(); moved = true
                                }
                            }
                        }
                        op == 6 && p.S >= 2 && p.T > 0 -> {   // swapTwoStaffSameDay (coverage-neutral)
                            val j = rng.nextInt(p.T)
                            val i1 = rng.nextInt(p.S); var i2 = rng.nextInt(p.S)
                            if (i2 == i1) i2 = (i2 + 1) % p.S
                            if (!p.wishLocked(i1, j) && !p.wishLocked(i2, j)) {
                                val k1 = eval.at(i1, j); val k2 = eval.at(i2, j)
                                if (k1 != k2 && p.canDo(i1, k2) && p.canDo(i2, k1)) {
                                    eval.apply(i1, j, k2); eval.apply(i2, j, k1)
                                    c0i = i1; c0j = j; c0old = k1; c1i = i2; c1j = j; c1old = k2
                                    moveAug = glsMoveAug(gls, i1, j, k1, k2) + glsMoveAug(gls, i2, j, k2, k1)
                                    ns = eval.score(); moved = true
                                }
                            }
                        }
                    }
                    if (moved) {
                        val improvedCur = ns < curScore
                        val accepted = improvedCur || glsAccept(ns, curScore, moveAug, curAug, options.accept, temp, gdLevel, rng)
                        if (options.accept == AcceptMode.LAM_ADAPTIVE) lamUpdate(accepted)
                        if (accepted) {
                            cur[c0i][c0j] = eval.at(c0i, c0j)
                            if (c1i >= 0) cur[c1i][c1j] = eval.at(c1i, c1j)
                            curScore = ns; curAug += moveAug
                            if (ns < globalScore) {
                                globalBest = cur.copy2D(); globalScore = ns
                                globalReport = UnifiedViolationChecker.check(state, cur)
                                lastImproveIter = itersTotal
                                reward = 4.0
                            } else reward = if (improvedCur) 2.0 else 1.0
                        } else {
                            if (c1i >= 0) eval.apply(c1i, c1j, c1old)   // revert eval; cur was never mutated
                            eval.apply(c0i, c0j, c0old)
                        }
                        opScore[op] += reward; opCnt[op]++
                    }
                } else {
                    // ── copy系パス(op0-2): 変更セルだけ eval へ反映（targeted O(S)/O(T) 差分） ──
                    val cand = scratchBuf
                    for (i2 in 0 until p.S) System.arraycopy(cur[i2], 0, cand[i2], 0, p.T)
                    val drDay = if (op == 0 && p.T > 0) rng.nextInt(p.T) else -1
                    val drStaff = if (op == 1 && p.S > 0) rng.nextInt(p.S) else -1
                    when (op) {
                        0 -> if (drDay >= 0) destroyRepairDayAt(state, cand, drDay, rng)
                        1 -> if (drStaff >= 0) destroyRepairStaffAt(state, cand, drStaff, rng)
                        else -> destroyRepairViolations(state, cand, curReport, rng)
                    }
                    // hf67 は hard 違反がある時のみ必要。
                    val fixed = if (iter % 7L == 0L && curHard > 0L) hf67HardRepair(state, cand, rng).schedule else cand
                    val nDiffs = when {
                        op == 0 && drDay >= 0 && fixed === cand -> {
                            var n = 0
                            for (i in 0 until p.S) if (cur[i][drDay] != fixed[i][drDay]) diffBuf[n++] = i * p.T + drDay
                            n
                        }
                        op == 1 && drStaff >= 0 && fixed === cand -> {
                            var n = 0
                            val row = fixed[drStaff]; val curRow = cur[drStaff]
                            for (j in 0 until p.T) if (curRow[j] != row[j]) diffBuf[n++] = drStaff * p.T + j
                            n
                        }
                        else -> diffInto(p.T, cur, fixed, diffBuf)
                    }
                    var moveAug = 0.0
                    for (idx in 0 until nDiffs) {
                        val flat = diffBuf[idx]; val i = flat / p.T; val j = flat % p.T
                        moveAug += glsMoveAug(gls, i, j, cur[i][j], fixed[i][j])
                        eval.apply(i, j, fixed[i][j])
                    }
                    val ns = eval.score()
                    val improvedCur = ns < curScore
                    val accepted = improvedCur || glsAccept(ns, curScore, moveAug, curAug, options.accept, temp, gdLevel, rng)
                    if (options.accept == AcceptMode.LAM_ADAPTIVE) lamUpdate(accepted)
                    if (accepted) {
                        // [零アロケ] スクラッチ採用時は cur とスワップ（旧 cur を次のスクラッチへ）。
                        if (fixed === scratchBuf) { val t = cur; cur = fixed; scratchBuf = t } else cur = fixed
                        curScore = ns; curAug += moveAug
                        if (ns < globalScore) {
                            globalBest = fixed.copy2D(); globalScore = ns
                            globalReport = UnifiedViolationChecker.check(state, fixed)
                            lastImproveIter = itersTotal
                            reward = 4.0
                        } else reward = if (improvedCur) 2.0 else 1.0
                    } else {
                        for (idx in 0 until nDiffs) {
                            val flat = diffBuf[idx]; eval.apply(flat / p.T, flat % p.T, cur[flat / p.T][flat % p.T])
                        }
                    }
                    opScore[op] += reward; opCnt[op]++
                }

                // [GLS] 停滞時(直近の最良更新から GLS_TRIGGER 反復超)に、違反セルの最大util割当を強化。
                if (itersTotal - lastImproveIter > GLS_TRIGGER && iter % 50L == 0L) {
                    val cells = ArrayList<Pair<Int, Int>>(curReport.violations.size)
                    for (vkey in curReport.violations.keys) {
                        val parts = vkey.split(',')
                        val ci = parts.getOrNull(0)?.toIntOrNull()
                        val cj = parts.getOrNull(1)?.toIntOrNull()
                        if (ci != null && cj != null) cells.add(ci to cj)
                    }
                    if (gls.penalizeWorst(cur, cells)) {
                        curAug += gls.lambda   // penalized a current cell -> augment(cur) += lambda
                        // [GLS aging] 一定 kick ごとに penalty を減衰し肥大化を防ぐ。penalty集合が変わるので
                        //   curAug を augment(cur) で再同期（globalBest は生スコア管理＝解の質は退化しない）。
                        if (gls.kickCount() % GLS_DECAY_EVERY == 0) { gls.decay(); curAug = gls.augment(cur) }
                    }
                }
                // destroyRepairViolations 用に curReport を周期更新（hint の鮮度確保）。
                if (iter % 200L == 0L) curReport = UnifiedViolationChecker.check(state, cur)
                if (++sinceUpdate >= 64) {
                    for (k in opW.indices) {
                        if (opCnt[k] > 0) opW[k] = (0.8 * opW[k] + 0.2 * (opScore[k] / opCnt[k])).coerceAtLeast(0.05)
                        opScore[k] = 0.0; opCnt[k] = 0
                    }
                    sinceUpdate = 0
                }
                iter++
                itersTotal++
                if (iter % 120L == 0L) {
                    // [3.335.0] 置き換えられた古い実行が新しい実行のライブ盤面を上書きしないようにする。
                    if (ownsStatics(runSlot())) publishLiveBest(globalReport, globalBest)
                    onProgress("ALNS restart ${r + 1}/$restarts", globalReport, itersTotal, nowMs() - started)
                    yield()
                }
            }
            }   // if (!usedNative)
            logs.add(MirrorLog(iter = itersTotal, tag = "RunMAGI_ALNS", message = "restart=${r + 1}/$restarts best HARD=${globalReport.hard} total=${globalReport.total} GLS=${if (usedNative) "native" else gls.kickCount().toString()}"))
        }
        } finally { if (nativeProblem != 0L) NativeBridge.nativeDestroyProblem(nativeProblem) }
        // [退化防止] 生スコア最良が weightedScore 辞書順では入力より悪い可能性があるため番兵で保証。
        if (better(baseReport, globalReport)) { globalBest = baseBest; globalReport = baseReport }
        return V6OptimizerResult(globalBest, globalReport.copy(logs = logs + globalReport.logs), V6Algorithm.ALNS, logs, itersTotal, nowMs() - started)
    }

    private suspend fun runRsi(
        state: MagiState,
        initial: Array<IntArray>,
        options: V6OptimizerOptions,
        budgetSec: Int,
        shouldStop: () -> Boolean = { false },
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
        sharedHf63: Hf63Infeasibility? = null,
    ): V6OptimizerResult {
        val started = nowMs()
        val rng = Random(actualSeed(options.seed) xor 0x451L)
        var best = normalizeSchedule(initial, cachedProblem(state))
        var bestReport = UnifiedViolationChecker.check(state, best)
        var iters = 0L
        val rounds = max(2, min(8, budgetSec / 30 + 2))
        val per = max(1, budgetSec / rounds)
        val logs = ArrayList<MirrorLog>()
        // [HF63] ラウンド境界で改善ストリームを追跡し、構造的に充足困難な族を focus 対象から外す。
        // best-of-rounds のため、回避は「無駄なラウンドを達成可能な族へ振り向ける」だけで悪化は起きない。
        // [3.281.0/停滞レビューB] 適応ポートフォリオの短いエポック(rounds=2)では「focus を2ラウンド投入して
        //   threshold に達した瞬間に runRsi ごと破棄→次エポックで白紙から再学習」を数十回反復していた
        //   （実機ログ: W1x32+W2x35=67エポックが毎回 c3n を2ラウンドずつ攻めて学習を捨てる）。呼出元
        //   （ワーカー）が sharedHf63 を渡すとエポックを跨いで停滞学習が持続する。gBestCurV は全期間min の
        //   ため、エポック間の摂動で族の件数が一時的に増えても self-correction は誤発火しない（真の改善=
        //   全期間minの更新のみでリセット）＝クロスエポック共有は意味論的に健全。省略時は従来どおり新規。
        val hf63 = sharedHf63 ?: Hf63Infeasibility()
        // [3.288.0/ログ強化=回数軸] 戦略変更（focus遷移・E9冷却・HF63降格）を1行に集約するための足跡。
        //   スパム対応: HF63/ピボット行は「内容が変わったときだけ」出す（旧: avoid が立つと毎ラウンド同文を出力）。
        val focusTrail = ArrayList<String>()
        var e9Cooldowns = 0
        var lastLoggedAvoid: Set<String>? = null
        var lastLoggedPivot: String? = null
        // [HARD=0非到達への配慮 / 静的covU床] 構造的 covU 下限（有資格者を全員就けても埋まらない席=forcedCovU）は
        //   最適化中に不変。covU がこの床に達したら「これ以上 covU は下げられない」と静的に確定するので、HF63 の
        //   動的検知(約3ラウンド無改善を要する)を待たず round 0 から即 focus 除外し、RSI の残ラウンドを解ける族
        //   (他HARD/SOFT)へ回す。床=0（構造的不足なし＝HARD=0 到達可能な一般ケース）なら常に no-op＝挙動不変。
        //   focus 選択のみの変更でスコアリング不変（keep-best=better() が結果を担保）＝退化なし・3.74.0 と同方針。
        val covUFloor = try { V6SanityPort.structuralHardFloor(state, cachedProblem(state)) } catch (_: Exception) { 0 }
        var stagnantRounds = 0   // [N4] better() 無改善の連続ラウンド数
        // [E9/状況適応] 直前ラウンドが「完全空振り」(候補不採用＋focus族の件数も不変)だった focus を
        //   次の1ラウンドだけ回避する軽い冷却。同一 focus の同一仮説を3連発する空転(実機: c3n×3R=~63s 無変化、
        //   HF63 の恒久判定は約3R を要す)を、c3n→c1→c3n… の交互へ多様化する。1ラウンド限定なので
        //   乱数運の悪い1回で族を見捨てない(恒久除外は従来どおり HF63 のみ)。focus 選択のみ＝スコアリング不変。
        var cooldownFocus: String? = null
        // [レビュー#5 3.213.0] HF63 の停滞加算を「直前ラウンドで実際に focus した族」に限定する。
        //   旧: updateFromBreakdown が全族を無差別に停滞加算し、covU 張り付き中に一度も試していない
        //   c3n 等の HARD 族まで約3ラウンドで誤 deprioritize し得た（SOFT は 3.184.0 の avoid フィルタで
        //   緩和済みだが HARD は残っていた）。
        // [3.231.0/ドッグフーディングで発見・修正] 旧 effortIters=1800/round(固定) は
        //   INFEAS_STALL_ITERS=5000 到達に約3ラウンドの同族focusを要する。E9冷却(1ラウンド休止)が
        //   2〜3の詰んだ族を交互に切替えるため、実際にその族が3回目の focus を受けるのは
        //   （rounds=5の場合）round1,3,5＝最終ラウンドで、deprioritize が成立しても振り向け先の
        //   ラウンドが残っていなかった（実機ログでround1〜5が3族の堂々巡りのまま全く改善しない事例を
        //   確認）。effortIters を rounds に応じて動的に決め、詰んだ族の deprioritize が
        //   「残り最低2ラウンドを振り向けに残せる」タイミングで完了するようにする
        //   （reserveRounds=2・E9の1-in-2交互を想定しattemptsTarget=ceil((rounds-2)/2)を2で下駄履かせ、
        //   一度の不運な1ラウンドだけでは deprioritize しない=E9のより軽い1R冷却との役割分担を保つ）。
        //   rounds が大きいほど attemptsTarget も緩み、旧来同様じっくり粘れる。focus 選択のみの変更で
        //   スコアリング不変（keep-best=better()が結果を担保）。
        val effortIters = rsiHf63EffortIters(rounds)
        var lastFocus: String? = null
        for (round in 0 until rounds) {
            if (shouldStop()) break
            coroutineContext.ensureActive()
            // [監査修正] HF63 は Web の per-iter 前提(5000 iter 無改善で infeasible)。ラウンド粒度の呼出に
            //   effortIters/round を渡し、閾値5000到達を有限ラウンド分の focus 投入無改善に引き伸ばす
            //   （class は Web 忠実移植のまま・呼出側で粒度を補正）。iters 自体は本来用途に不変。
            hf63.updateFromBreakdownFocused(bestReport.breakdown, lastFocus, effortIters)
            // [12h見直し] 動的(HF63)と静的(covU床)の avoid を分離して保持する。N4 早期脱出(下記)の発火条件は
            //   HF63 の動的検知のみでゲートしないと、構造的covU>0 のデータでは静的除外が round 0 から avoid を
            //   非空にし、「旧N4の厳密な部分集合」保証(650-654行)を破って2停滞ラウンドで RSI が即終了してしまう。
            val dynamicAvoid = hf63.infeasibleBreakdownKeys()
            // [実機ログ起因/SOFT誤deprioritize] HF63 は breakdown 値が減らなければ族を stall 計上するため、
            //   「covU に focus が張り付いて一度も focus されず不減の SOFT 族」(実機: c1=87/low/high 等)まで
            //   infeasible 判定してしまう。これを focus の avoid に入れると本来直せる SOFT が永久に focus されない
            //   （pivot しても weekly/fair だけ残り destroyRepairStaff が cost 未対応で効かない）。focus の
            //   deprioritize は真に構造的な HARD（covU 床/c3n/pref/groupViol）のみに限定し、SOFT は常に focusable に
            //   保つ（SOFT の同一 focus 空転は cooldownFocus の1R休止＋keep-best＋有限ラウンドで自己収束）。
            //   N4 早期終了の武装判定（下記）は従来どおり dynamicAvoid（全族）で行い、pivot 可否は avoid(HARD) で判定する。
            val avoid = dynamicAvoid.filterTo(mutableSetOf()) { it in MirrorKeys.hard }
            // [静的covU床] covU が構造的下限（covUFloor）に達している間は解けないので focus から即除外する。
            //   合法配置では covU >= covUFloor（下限）。担当外配置(groupViol)が混在すると covU が床を下回り得るが、
            //   その間 covU を focus しても無意味（groupViol が hard-first で先に選ばれる）なので `<=` で除外が正しい。
            if (covUFloor > 0 && (bestReport.breakdown["covU"] ?: 0) <= covUFloor) avoid.add("covU")
            // [E9] 冷却は focus 選択にのみ合流（HF63 ログ・N4 発火条件には混ぜない＝恒久判定と区別）。
            val focusAvoid = if (cooldownFocus != null) avoid + cooldownFocus!! else avoid
            val focus = maxViolatedFamily(bestReport, focusAvoid, round, rounds)
            if (avoid.isNotEmpty() && avoid != lastLoggedAvoid) {
                // [3.288.0/スパム対応] 集合が変化したラウンドのみログ（旧: 毎ラウンド同文）。
                logs.add(MirrorLog(iter = iters, tag = "HF63", message = "deprioritize ${avoid.joinToString(",")} → focus=$focus (round ${round + 1})"))
                lastLoggedAvoid = avoid.toSet()
                focusTrail.add("[HF63降格:${avoid.joinToString("+")}]")
            }
            if (cooldownFocus != null) {
                logs.add(MirrorLog(iter = iters, tag = "RSIFocus", message = "直前ラウンド空振りのため ${cooldownFocus} を1ラウンド休止 → focus=$focus (round ${round + 1})"))
                e9Cooldowns++
            }
            focusTrail.add(focus)
            val focusedBefore = bestReport.breakdown[focus] ?: 0
            lastFocus = focus   // [レビュー#5] 次ラウンド頭の HF63 更新へ「このラウンドの投入先」を渡す
            val hypothesis = rsiGenerateHypothesis(state, best, bestReport, focus, rng, shouldStop)
            val phase = if (round % 2 == 0) runAlns(state, hypothesis, options.copy(restarts = 1), per, shouldStop, onProgress) else runV5(state, hypothesis, options, per, shouldStop, onProgress)
            iters += phase.iterations
            var candSched = phase.schedule
            var candReport = phase.report
            // [HF361/528/541移植] EarlyChain: Web 内部V5の停滞(reheat)フック(L11705-)に対応する RSI ラウンド境界で発火
            //   Chain3/4 は常時、Rect/BlkN は optFlags.rectSwap(既定ON)に従う — Web 呼出順 e3/e4/e5/e6 と同一。
            run {
                val lr = V6LateOperators.improve(state, candSched, candReport, rng, started + budgetSec * 1000L, rectEnabled = options.rectSwap)
                if (lr.chain3 + lr.chain4 + lr.rect + lr.blkN > 0) {
                    candSched = lr.schedule
                    candReport = lr.report
                    logs.add(MirrorLog(iter = iters, tag = "EarlyChain", message = "早期循環フック改善 (Chain3=${lr.chain3} Chain4=${lr.chain4} Rect=${lr.rect} BlkN=${lr.blkN}) round=${round + 1} HARD=${candReport.hard} total=${candReport.total}"))
                    logs.addAll(lr.logs)
                }
            }
            if (better(candReport, bestReport)) {
                best = candSched.copy2D()
                bestReport = candReport
                stagnantRounds = 0
                cooldownFocus = null   // [E9] 進展あり＝冷却解除
            } else {
                stagnantRounds++
                // [E9] 完全空振り(不採用＋focus族の件数が減っていない)なら次ラウンドだけこの focus を休止。
                //   候補が focus 族を減らしていた(=方向は有望だが総合で負けた)場合は冷却しない。
                cooldownFocus = if (focus != "total" && (candReport.breakdown[focus] ?: 0) >= focusedBefore) focus else null
            }
            // [3.288.0/スパム対応] ラウンド行は「改善したラウンド」と「最終ラウンド」だけに絞る。
            //   適応ポートフォリオは 1エポック=2ラウンド×数十エポック×ワーカー数のため、旧・全ラウンド出力では
            //   この1種類だけで診断ログの大半を占め、重要イベント（HF63降格・早期終了・壁判定）を押し出していた。
            //   焦点の履歴は末尾の「戦略変更」1行（focus遷移を連続圧縮）が全ラウンド分を保持する＝情報は失わない。
            if (stagnantRounds == 0 || round == rounds - 1) {
                logs.add(MirrorLog(iter = iters, tag = "RunMAGI_RSI", message = "round=${round + 1}/$rounds focus=$focus best HARD=${bestReport.hard} total=${bestReport.total}" + if (stagnantRounds > 0) "（無改善${stagnantRounds}R）" else "（改善）"))
            }
            if (ownsStatics(runSlot())) publishLiveBest(bestReport, best)   // [DefragLiveView] 計算中ライブ盤面
            onProgress("RSI $focus", bestReport, iters, nowMs() - started)
            // [N4改] focus枯渇の早期終了。旧版は「2R無改善」だけで打ち切っていたが、これは達成可能族が
            //   残る場合でもランダム探索(destroy-repair)を早期に切り、乱数運の悪い2Rで本来伸びる盤面を捨てうる
            //   （proxyでA/B不能な領域＝安全側に倒す）。発火条件を hf63 が infeasible 族を検出済み(avoid非空)＝
            //   「達成可能な focus を撃ち尽くした」ときに限定する。これは旧条件の厳密な部分集合のため、
            //   旧N4より早く止まることはない＝品質は退化しない。avoid が空(まだ狙える族がある)の間は全予算で探索。
            //   ※後段(hf80)は固定予算のため厳密な「予算移譲」ではなく、無改善ラウンドの空転停止(電池/熱/時間節約)。
            // [12h見直し] 発火は動的検知(dynamicAvoid=HF63)のみでゲートする。静的covU床(合流後のavoid)を使うと
            //   構造的covU>0 のデータで round 0 から常時武装し、旧N4保証(上記)を破る。
            // [ユーザー指示/HARD残でもSOFT focus] 停滞した HARD(covU等)を deprioritize してもなお狙える族
            //   (low/high/c2・c41/c41s/c42/c42s・covO・weekly・fair・c1・c3系・apt)が残るなら、早期終了せず
            //   残ラウンドを SOFT 最適化に振り向ける（focus は L741 で focusAvoid により既に SOFT へピボット済）。
            //   keep-best(better() は hard 非悪化を要求)が HARD 悪化を防ぐ＝HARD残のまま SOFT を最適化しても安全。
            //   本当に狙える族が尽きた(pivot=="total" or 件数0)ときだけ従来どおり空転停止する。stuck な SOFT も
            //   HF63 が順次 dynamicAvoid へ入れて focusable から外すため、いずれ pivot 枯渇→終了で自己収束する。
            if (stagnantRounds >= 2 && dynamicAvoid.isNotEmpty()) {
                val pivot = maxViolatedFamily(bestReport, avoid, round, rounds)   // avoid=dynamicAvoid＋静的covU床
                if (pivot == "total" || (bestReport.breakdown[pivot] ?: 0) == 0) {
                    logs.add(MirrorLog(iter = iters, tag = "RunMAGI_RSI", message = "早期終了: 狙える族が枯渇(deprioritize=${avoid.size}族)＋${stagnantRounds}R無改善（残${rounds - round - 1}Rの空転を停止）"))
                    break
                }
                if (pivot != lastLoggedPivot) {
                    // [3.288.0/スパム対応] pivot が変わったときだけログ（旧: 停滞が続く限り毎ラウンド同文）。
                    logs.add(MirrorLog(iter = iters, tag = "RunMAGI_RSI", message = "HARD残(${dynamicAvoid.joinToString(",")})を回避しSOFTへピボット継続 → 次focus候補=$pivot（HARD非悪化はkeep-bestが担保）"))
                    lastLoggedPivot = pivot
                }
            }
        }
        // [3.288.0/ログ強化=回数軸] 戦略変更の1行サマリ（focus遷移を連続圧縮）。2手以上あるときだけ出す＝スパムなし。
        if (focusTrail.count { !it.startsWith("[") } >= 2) {
            logs.add(MirrorLog(iter = iters, tag = "戦略変更", message =
                "RSI focus遷移: ${compressFocusTrail(focusTrail)}" +
                    (if (e9Cooldowns > 0) " / E9冷却${e9Cooldowns}回" else "") +
                    (hf63.infeasibleFamilies().takeIf { it.isNotEmpty() }?.let { " / HF63降格={${it.joinToString(",")}}" } ?: "")))
        }
        // [3.288.0/ログ強化=状態軸] このRSI実行でHF63が「構造的に充足困難」と学習した族を実行横断で集約
        //   （エピローグの残存分析行が読む。ワーカー並行呼出があるため synchronized 集約）。
        recordInfeasibleScoped(hf63.infeasibleFamilies())
        return V6OptimizerResult(best, bestReport.copy(logs = logs + bestReport.logs), V6Algorithm.RSI, logs, iters, nowMs() - started)
    }

    private suspend fun runRsiPlus(
        state: MagiState,
        initial: Array<IntArray>,
        options: V6OptimizerOptions,
        budgetSec: Int,
        shouldStop: () -> Boolean = { false },
        onProgress: (String, ViolationReport?, Long, Long) -> Unit,
        sharedHf63: Hf63Infeasibility? = null,   // [3.281.0/B] Phase2 RSI へ透過（エポック跨ぎのHF63学習持続）
    ): V6OptimizerResult {
        val started = nowMs()
        val seedSec = max(10, (budgetSec * 0.20).toInt())
        val rsiSec = max(10, (budgetSec * 0.35).toInt())
        val alnsSec = max(10, (budgetSec * 0.30).toInt())
        val polishSec = max(5, budgetSec - seedSec - rsiSec - alnsSec)
        val logs = ArrayList<MirrorLog>()
        val seed = runV5(state, initial, options, seedSec, shouldStop, onProgress)
        logs.add(MirrorLog(tag = "RSIPlus", message = "Phase1 Seed: HARD=${seed.report.hard} total=${seed.report.total}"))
        val rsi = if (shouldStop()) seed else runRsi(state, seed.schedule, options, rsiSec, shouldStop, onProgress, sharedHf63)
        val base = if (better(rsi.report, seed.report)) rsi else seed
        logs.add(MirrorLog(tag = "RSIPlus", message = "Phase2 Hypothesis: HARD=${base.report.hard} total=${base.report.total}"))
        val refine = if (shouldStop()) base else runAlns(state, base.schedule, options.copy(restarts = max(1, options.restarts)), alnsSec, shouldStop, onProgress)
        val best = if (better(refine.report, base.report)) refine else base
        var bestSched = best.schedule
        // [HF361/528/541移植] EarlyChain: Refine 確定後の停滞境界で Chain3/4(常時)+Rect/BlkN(rectSwap)を発火
        run {
            val lr = V6LateOperators.improve(state, bestSched, best.report, Random(actualSeed(options.seed) xor 0x528L), started + budgetSec * 1000L, rectEnabled = options.rectSwap)
            val fired = lr.chain3 + lr.chain4 + lr.rect + lr.blkN > 0
            // [監査#1] Chain3/4の受理(gateW)はweighted単層でHARD増を相殺受理し得るため、採用は
            //   runRsiと同じ better(hard→weighted→total) でゲートする（素通しでHARD悪化を最終出力しない）。
            // ※ run{} 末尾のため if を式位置にしない（else-if 連鎖は式扱いとなり全分岐必須。ネストifの文形式で書く）。
            if (fired) {
                if (better(lr.report, best.report)) {
                    bestSched = lr.schedule
                    logs.add(MirrorLog(tag = "EarlyChain", message = "早期循環フック改善 (Chain3=${lr.chain3} Chain4=${lr.chain4} Rect=${lr.rect} BlkN=${lr.blkN}) HARD=${lr.report.hard} total=${lr.report.total}"))
                    logs.addAll(lr.logs)
                } else {
                    logs.add(MirrorLog(tag = "EarlyChain", message = "採用見送り（hard/total非改善ガード） HARD=${lr.report.hard} total=${lr.report.total}"))
                }
            }
        }
        val polish = hf80PostPolish(state, bestSched, polishSec, actualSeed(options.seed) xor 0x555L, shouldStop)
        val report = UnifiedViolationChecker.check(state, polish.schedule)
        logs.add(MirrorLog(tag = "RSIPlus", message = "Phase3/4 Refine+Polish: HARD=${report.hard} total=${report.total}"))
        return V6OptimizerResult(
            polish.schedule,
            report.copy(logs = logs + seed.phaseLogs + rsi.phaseLogs + refine.phaseLogs + polish.logs + report.logs),
            V6Algorithm.RSI_PLUS,
            logs + seed.phaseLogs + rsi.phaseLogs + refine.phaseLogs + polish.logs,
            seed.iterations + rsi.iterations + refine.iterations + polish.iterations,
            nowMs() - started,
        )
    }

    private fun hf66DataHardening(state: MagiState, schedule: Array<IntArray>, tag: String): Array<IntArray> {
        val p = cachedProblem(state)
        val out = normalizeSchedule(schedule, p)
        for (i in 0 until p.S) {
            val allowed = p.allowedShiftsForStaff(i)
            val fallback = allowed.firstOrNull() ?: 0
            for (j in 0 until p.T) {
                val k = out[i][j]
                if (k !in 0 until p.K || !p.canDo(i, k)) out[i][j] = fallback
            }
        }
        return out
    }

    private data class RepairResult(val schedule: Array<IntArray>, val logs: List<MirrorLog>)

    private fun hf67HardRepair(state: MagiState, schedule: Array<IntArray>, rng: Random): RepairResult {
        val p = cachedProblem(state)
        val out = hf66DataHardening(state, schedule, "hf67")
        val logs = ArrayList<MirrorLog>()
        var changed = 0

        // Apply feasible wishes first; infeasible wishes are logged by Sanity, not forced.
        for (i in 0 until p.S) for (j in 0 until p.T) {
            val w = p.wish[i][j]
            if (w in 0 until p.K && p.canDo(i, w) && out[i][j] != w) {
                out[i][j] = w
                changed++
            }
        }

        repeat(3) {
            val cov = coverage(p, out)
            val counts = countMatrix(p, out)
            for (j in 0 until p.T) for (k in 0 until p.K) {
                // [N1a] 充填量は per-cell 実需要（#4b: OR/AND）。旧 need1 のみ基準では P2 で救済済みの
                //   セル（休日体制など P1>P2）まで埋めに行き、既良盤面を壊していた。
                var miss = p.covUCell(k, j, cov[j][k])
                while (miss > 0) {
                    val i = bestStaffForCoverage(p, out, counts, j, k)
                    if (i < 0) break
                    val old = out[i][j]
                    if (old == k) break
                    out[i][j] = k
                    cov[j][k]++
                    if (old in 0 until p.K) cov[j][old]--
                    changed++
                    miss--
                }
            }
        }

        // Range lower bounds: fill shortage where possible without touching locked wishes.
        val counts = countMatrix(p, out)
        for (i in 0 until p.S) for (k in 0 until p.K) {
            val lo = p.rangeLo[i][k]
            if (lo == Int.MIN_VALUE || !p.canDo(i, k)) continue
            var need = lo - counts[i][k]
            var guard = 0
            while (need > 0 && guard++ < p.T) {
                var bestJ = -1
                var bestScore = Int.MAX_VALUE
                for (jj in 0 until p.T) {
                    if (p.wishLocked(i, jj) || out[i][jj] == k) continue
                    val score = coverageShortageCost(p, out, jj, out[i][jj]) + rng.nextInt(3)
                    if (score < bestScore) {
                        bestScore = score
                        bestJ = jj
                    }
                }
                if (bestJ < 0) break
                val j = bestJ
                val old = out[i][j]
                out[i][j] = k
                if (old in 0 until p.K) counts[i][old]--
                counts[i][k]++
                changed++
                need--
            }
        }
        if (changed > 0) logs.add(MirrorLog(tag = "HF67", message = "HardRepair changed=$changed"))
        return RepairResult(out, logs)
    }

    private data class PolishResult(val schedule: Array<IntArray>, val logs: List<MirrorLog>, val iterations: Long)

    /**
     * [ソフト研磨専用] 現在の盤面をHARDガード付きで局所研磨し、SOFTのみ削減する公開エントリ。
     * 破壊/多様化フェーズは行わず、hf80PostPolish の keep-best＋退化防止により入力以上の盤面のみ返す
     * （HARD=0 は壊さない）。最適化(もう一度つくる)と違い、必須が一時的に増えることはない。
     */
    suspend fun softPolishOnly(
        state: MagiState,
        schedule: Array<IntArray>,
        seconds: Int,
        seed: Long = 0x50F11L,
        shouldStop: () -> Boolean = { false },
    ): Array<IntArray> = hf80PostPolish(state, schedule, max(1, seconds), seed, shouldStop).schedule

    /**
     * 最終研磨フェーズ。[差分化移植] DeltaEvaluator を生スコア源にして直接評価で回す
     * （copy2D / 全件 check() を毎反復行わない）。op0-2 は単一/二セル直接評価、op3-8 は
     * findTargetedFix（シャッフル付きフォールバック）、op9-10 は copy 系の destroy/repair を
     * 変更セルだけ eval へ反映する。受理は hard 非悪化(best基準)＋ SA。
     * 不変条件: eval.at(i,j) == cur[i][j]。生スコアと weightedScore は目的が異なるため、
     * 入力(best)を baseline として保持し最後に番兵比較して退化を防ぐ。
     */
    private suspend fun hf80PostPolish(state: MagiState, initial: Array<IntArray>, seconds: Int, seed: Long, shouldStop: () -> Boolean = { false }): PolishResult {
        val started = nowMs()
        val rng = Random(seed)
        val p = cachedProblem(state)
        var best = initial.copy2D()
        var bestReport = UnifiedViolationChecker.check(state, best)
        val baseSched = best          // 入力スナップショット（best は改善時に別配列へ差し替わる）
        val baseReport = bestReport
        var iters = 0L
        val deadline = started + seconds * 1000L
        // [E10/停滞早期終了] 実機ログで PostPolish が 45s枠を最後まで走り切って改善0（40.977s/40.988s の2例）
        //   ＝重研磨済み盤面ではプラトー後の期待値が低い。best が枠の1/5(下限3s)無改善なら早期に返す。
        //   keep-best＋末尾の入力比番兵(better(baseReport,bestReport)→入力復帰)のため品質は不変＝時間/電池だけ節約
        //   （2.65.0 HF66 / 2.67.0 停滞ウォッチドッグと同方針）。native/Kotlin 両経路で共通。
        val stallMs = max(3000L, seconds * 1000L / 5)
        var stalled = false
        // [Stage10/第3期] ネイティブ経路: C++ PolishChunk（同一オペ構成11-way・同一受理・keep-best）＋2層番兵。
        //   枠を消費し切れば早期 return。番兵発火時は「照合済み best」だけ引き継ぎ、下の Kotlin ループが
        //   残り時間を続行する（NativeGate は閉鎖済み＝以後の実行は全て Kotlin）。
        val nat = runPolishChunksNative(p, best, deadline, stallMs, seed, shouldStop)
        iters += nat.iters
        nat.best?.let { best = it; bestReport = UnifiedViolationChecker.check(state, it) }
        if (nat.stalled) stalled = true
        if (nat.completed) {
            if (better(baseReport, bestReport)) { best = baseSched; bestReport = baseReport }
            val logs = listOf(MirrorLog(iter = iters, tag = "HF80", message = "PostPolish ${nowMs() - started}ms HARD=${bestReport.hard} total=${bestReport.total}（ネイティブ）" + if (stalled) "（停滞早期終了 枠${seconds}s）" else ""))
            return PolishResult(best, logs, iters)
        }
        var cur = best.copy2D()
        val eval = DeltaEvaluator(p)
        eval.reset(cur)
        var curScore = eval.score()
        var bestScore = curScore
        val diffBuf = IntArray(p.S * p.T)
        // [C③修正] ここへ来るのは native 経路が番兵発火で未完了(nat.completed==false)に戻った異常系のみ。
        //   native 区間で一度も改善しなかった(nat.best==null)なら、その無改善経過を停滞時計へ引き継ぐため
        //   起点を started にする（旧: 常に nowMs() で再スタートし、native の無改善時間が停滞判定から抜け落ち、
        //   さらに約 stallMs ぶん余計に回っていた）。改善済みは最終改善時刻不明のため保守的に nowMs()。
        var lastImproveMs = if (nat.best == null) started else nowMs()
        var lastBestMark = bestScore
        var stallDurationMs = -1L   // [停滞時間のログ出力] 発火の瞬間の無改善経過(ms)
        while (!shouldStop()) {
            val nowLoop = nowMs()
            if (nowLoop >= deadline) break
            if (bestScore < lastBestMark) { lastBestMark = bestScore; lastImproveMs = nowLoop }
            else if (nowLoop - lastImproveMs >= stallMs) { stalled = true; stallDurationMs = nowLoop - lastImproveMs; break }
            coroutineContext.ensureActive()
            val curHard = curScore / SCORE_HARD_UNIT
            val bestHard = bestScore / SCORE_HARD_UNIT
            when (rng.nextInt(11)) {
                0 -> {   // random allowed single cell (direct-eval)
                    if (p.S > 0 && p.T > 0) {
                        val i = rng.nextInt(p.S); val j = rng.nextInt(p.T)
                        if (!p.wishLocked(i, j)) {
                            val allowed = p.allowedShiftsForStaff(i)
                            if (allowed.isNotEmpty()) {
                                val oldK = eval.at(i, j); val nw = allowed[rng.nextInt(allowed.size)]
                                if (nw != oldK) {
                                    eval.apply(i, j, nw)
                                    val ns = eval.score()
                                    if (ns / SCORE_HARD_UNIT <= bestHard && (betterScore(ns, curScore) || acceptWorseScore(ns, curScore, 0.15, rng))) {
                                        cur[i][j] = nw; curScore = ns
                                        if (betterScore(ns, bestScore)) { best = cur.copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.check(state, cur) }
                                    } else eval.apply(i, j, oldK)
                                }
                            }
                        }
                    }
                }
                1 -> {   // swap two days within one staff row (direct-eval)
                    if (p.S > 0 && p.T >= 2) {
                        val i = rng.nextInt(p.S)
                        var ja = rng.nextInt(p.T); var jb = rng.nextInt(p.T)
                        if (ja == jb) jb = (jb + 1) % p.T
                        if (!p.wishLocked(i, ja) && !p.wishLocked(i, jb)) {
                            val ka = eval.at(i, ja); val kb = eval.at(i, jb)
                            if (ka != kb) {
                                eval.apply(i, ja, kb); eval.apply(i, jb, ka)
                                val ns = eval.score()
                                if (ns / SCORE_HARD_UNIT <= bestHard && (betterScore(ns, curScore) || acceptWorseScore(ns, curScore, 0.15, rng))) {
                                    cur[i][ja] = kb; cur[i][jb] = ka; curScore = ns
                                    if (betterScore(ns, bestScore)) { best = cur.copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.check(state, cur) }
                                } else { eval.apply(i, ja, ka); eval.apply(i, jb, kb) }
                            }
                        }
                    }
                }
                2 -> {   // swap two staff on same day (direct-eval, coverage-neutral)
                    if (p.S >= 2 && p.T > 0) {
                        val j = rng.nextInt(p.T)
                        val i1 = rng.nextInt(p.S); var i2 = rng.nextInt(p.S)
                        if (i2 == i1) i2 = (i2 + 1) % p.S
                        if (!p.wishLocked(i1, j) && !p.wishLocked(i2, j)) {
                            val k1 = eval.at(i1, j); val k2 = eval.at(i2, j)
                            if (k1 != k2 && p.canDo(i1, k2) && p.canDo(i2, k1)) {
                                eval.apply(i1, j, k2); eval.apply(i2, j, k1)
                                val ns = eval.score()
                                if (ns / SCORE_HARD_UNIT <= bestHard && (betterScore(ns, curScore) || acceptWorseScore(ns, curScore, 0.15, rng))) {
                                    cur[i1][j] = k2; cur[i2][j] = k1; curScore = ns
                                    if (betterScore(ns, bestScore)) { best = cur.copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.check(state, cur) }
                                } else { eval.apply(i1, j, k1); eval.apply(i2, j, k2) }
                            }
                        }
                    }
                }
                in 3..8 -> {   // targeted single-cell fix with shuffled fallback (direct-eval)
                    val fix = findTargetedFix(p, eval, rng)
                    if (fix != null) {
                        val oldK = eval.at(fix[0], fix[1])
                        if (fix[2] != oldK) {
                            eval.apply(fix[0], fix[1], fix[2])
                            val ns = eval.score()
                            if (ns / SCORE_HARD_UNIT <= bestHard && (betterScore(ns, curScore) || acceptWorseScore(ns, curScore, 0.15, rng))) {
                                cur[fix[0]][fix[1]] = fix[2]; curScore = ns
                                if (betterScore(ns, bestScore)) { best = cur.copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.check(state, cur) }
                            } else eval.apply(fix[0], fix[1], oldK)
                        }
                    }
                }
                else -> {   // copy-based multi-cell destroy/repair (ops 9,10)
                    val cand = cur.copy2D()
                    val drDay2 = if (rng.nextBoolean()) { destroyRepairViolations(state, cand, bestReport, rng); -1 }
                                 else { val j = if (p.T > 0) rng.nextInt(p.T) else -1; if (j >= 0) destroyRepairDayAt(state, cand, j, rng); j }
                    // hard-feasible のときは hf67 を省略（DeltaEvaluator が hard 退化を弾く）。
                    val fixed = if (curHard > 0L) hf67HardRepair(state, cand, rng).schedule else cand
                    val nDiffs = if (drDay2 >= 0 && fixed === cand) {
                        var n = 0
                        for (i in 0 until p.S) if (cur[i][drDay2] != fixed[i][drDay2]) diffBuf[n++] = i * p.T + drDay2
                        n
                    } else diffInto(p.T, cur, fixed, diffBuf)
                    for (idx in 0 until nDiffs) {
                        val flat = diffBuf[idx]; eval.apply(flat / p.T, flat % p.T, fixed[flat / p.T][flat % p.T])
                    }
                    val ns = eval.score()
                    if (ns / SCORE_HARD_UNIT <= bestHard && (betterScore(ns, curScore) || acceptWorseScore(ns, curScore, 0.15, rng))) {
                        cur = fixed; curScore = ns
                        if (betterScore(ns, bestScore)) { best = fixed.copy2D(); bestScore = ns; bestReport = UnifiedViolationChecker.check(state, fixed) }
                    } else {
                        for (idx in 0 until nDiffs) {
                            val flat = diffBuf[idx]; eval.apply(flat / p.T, flat % p.T, cur[flat / p.T][flat % p.T])
                        }
                    }
                }
            }
            iters++
            if (iters % 150L == 0L) yield()
        }
        // [退化防止] 生スコア最良が weightedScore 辞書順で入力より悪い場合は入力へ戻す。
        if (better(baseReport, bestReport)) { best = baseSched; bestReport = baseReport }
        val logs = listOf(MirrorLog(iter = iters, tag = "HF80", message = "PostPolish ${nowMs() - started}ms HARD=${bestReport.hard} total=${bestReport.total}" +
            if (stalled) "（停滞早期終了 枠${seconds}s・停滞${stallDurationMs}ms無改善）" else ""))
        return PolishResult(best, logs, iters)
    }

    /** [Stage10] ネイティブ Polish 実行の結果。completed=枠を消費し切った(=Kotlin ループ不要) /
     *  best=Kotlin フル評価で照合済みの改善盤面(改善なし・未使用は null) / stalled=E10 停滞早期終了で戻った。 */
    private class NativePolishRun(val completed: Boolean, val best: Array<IntArray>?, val iters: Long, val stalled: Boolean)

    /**
     * [Stage10/第3期] hf80PostPolish の C++ チャンク駆動。SaOptimizer.runWorkerNative / runRestartNative と同型:
     * チャンク間でキャンセル・締切・E10 停滞を確認し、best 更新チャンクの盤面を Kotlin Evaluator.fullEval で
     * Long== 照合（2層目番兵。1層目=チャンク末尾の C++ 自己整合=status）。どちらか発火で NativeGate を閉じ
     * completed=false を返す＝呼び出し側の Kotlin ループが「照合済み best」から残り時間を続行（退化不能）。
     */
    private suspend fun runPolishChunksNative(
        p: Problem,
        initial: Array<IntArray>,
        deadline: Long,
        stallMs: Long,
        seed: Long,
        shouldStop: () -> Boolean,
    ): NativePolishRun {
        if (!NativeGate.usable) return NativePolishRun(false, null, 0L, false)
        val ph = runCatching { NativeEval.createHandle(p) }.getOrDefault(0L)
        if (ph == 0L) return NativePolishRun(false, null, 0L, false)
        try {
            val h = NativeBridge.nativePolishCreate(ph, NativeEval.flatten(initial), seed)
            if (h == 0L) return NativePolishRun(false, null, 0L, false)
            try {
                val fullEvaluator = Evaluator(p)
                val buf = IntArray(p.S * p.T)
                var verifiedBest = fullEvaluator.fullEval(initial)
                var best: Array<IntArray>? = null
                var iters = 0L
                var lastImproveMs = nowMs()
                while (!shouldStop()) {
                    val nowLoop = nowMs()
                    if (nowLoop >= deadline) return NativePolishRun(true, best, iters, false)
                    if (nowLoop - lastImproveMs >= stallMs) return NativePolishRun(true, best, iters, true)
                    coroutineContext.ensureActive()
                    // [全体計算の最小化] 400反復/チャンク＝チャンク末尾の自己整合フル評価の頻度を半減
                    //   （hint は best 改善駆動で更新されるためチャンク粒度に依存しない。締切/停滞/キャンセルの
                    //   確認粒度は ms 級のまま）。
                    val ret = NativeBridge.nativePolishChunk(h, 400)
                    if (ret.size < 5 || ret[0] != 0L) {
                        NativeGate.disable("Polishチャンク整合性NG(status=${ret.getOrNull(0)})")
                        return NativePolishRun(false, best, iters, false)
                    }
                    iters += ret[4]
                    if (ret[3] == 1L && ret[2] < verifiedBest) {
                        // [2層目番兵] best 更新チャンクの盤面を Kotlin フル評価で照合（Long== 許容誤差なし）。
                        //   [照合トグル] OFF=純ネイティブ（照合せず信頼）。C++自己整合(status)は上で常時検査済。
                        NativeBridge.nativePolishRead(h, 0, buf)
                        val sol = NativeEval.unflatten(buf, p.S, p.T)
                        if (NativeGate.parityCheckEnabled) {
                            TuningTelemetry.parityChecks.incrementAndGet()
                            val k = fullEvaluator.fullEval(sol)
                            if (k != ret[2]) {
                                NativeGate.disable("Polish Kotlin照合NG(native=${ret[2]} kotlin=$k)")
                                return NativePolishRun(false, best, iters, false)
                            }
                        }
                        best = sol
                        verifiedBest = ret[2]
                        lastImproveMs = nowLoop
                    }
                    yield()
                }
                return NativePolishRun(true, best, iters, false)   // shouldStop=キャンセル/締切は呼び出し側の扱いと同じ
            } finally {
                NativeBridge.nativePolishDestroy(h)
            }
        } finally {
            NativeBridge.nativeDestroyProblem(ph)
        }
    }

    private fun bestStaffForCoverage(p: Problem, schedule: Array<IntArray>, counts: Array<IntArray>, j: Int, k: Int): Int {
        var bestI = -1
        var bestScore = Int.MAX_VALUE
        for (i in 0 until p.S) {
            if (!p.canDo(i, k)) continue
            if (p.wishLocked(i, j) && p.wish[i][j] != k) continue
            val old = schedule[i][j]
            if (old == k) continue   // [監査#3] 既就業者はスキップ（旧: return で当該(日,シフト)の充填全体が中断していた）
            val hi = p.rangeHi[i][k]
            val over = if (hi != Int.MAX_VALUE && counts[i][k] >= hi) 500 else 0
            val oldNeedCost = coverageShortageCost(p, schedule, j, old)
            // [監査#12] 符号修正: 旧 `- oldNeedCost` は「外すと不足が生じる職員」ほど優先ドナー化していた
            //   （最小スコア採用のため減算=優遇）。引き抜きコストとして加算し、休・過剰被覆側を優先する。
            val score = over + counts[i][k] * 3 + oldNeedCost
            if (score < bestScore) { bestScore = score; bestI = i }
        }
        return bestI
    }

    private fun coverageShortageCost(p: Problem, schedule: Array<IntArray>, j: Int, k: Int): Int {
        if (k !in 0 until p.K) return 0
        var cov = 0
        for (i in 0 until p.S) if (schedule[i][j] == k) cov++
        // [N1a] 引き抜きで per-cell 実需要(U)が増える＝不足を生む職員はコスト50（旧: need1のみ基準）。
        //   ちょうど充足のセル(U=0→1)も保護される点は旧 `cov <= need` と同等。
        return if (p.covUCell(k, j, cov - 1) > p.covUCell(k, j, cov)) 50 else 0
    }

    private fun destroyRepairDay(state: MagiState, schedule: Array<IntArray>, rng: Random) {
        val p = cachedProblem(state)
        if (p.T == 0) return
        destroyRepairDayAt(state, schedule, rng.nextInt(p.T), rng)
    }

    /** [soft-aware repair] 割当 i→shift k の per-staff soft(low/high/apt, checker と同一式)を count n で評価。 */
    internal fun staffCountPenaltyAt(p: Problem, i: Int, k: Int, n: Int): Long {
        var pen = 0L
        val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]
        // [3.319.0] low は**担当できるシフトだけ**。`Evaluator.fullEvalParts` も `MirrorCore` の checker も
        //   元から `p.canDo(i, k)` ガードを持つのに、destroy-repair の marginal cost であるこの関数だけ
        //   欠けていた。担当外シフトに個人下限が設定されたデータ（UI で下限を入れたあと群の担当を外す等で
        //   起こりうる）では、実際には存在しない違反を重み90 で数え、候補選択を無駄な方向へ引っ張る。
        //   最終採否は checker が守るので誤った勤務表は出ないが、有効な候補を取りこぼす。
        //   実データ3件（golden/real/user）では該当セル0＝潜在バグ。high は n>hi の形で担当外なら n=0 に
        //   なり発火せず、かつ Evaluator 側もガードを持たない＝既に一致しているので触らない。
        //   apt は `Problem` 構築時に bucket=canDo でガード済み。
        if (lo != Int.MIN_VALUE && lo != 0 && n < lo && p.canDo(i, k)) pen += (lo - n).toLong() * 90L
        if (hi != Int.MAX_VALUE && n > hi) pen += (n - hi).toLong() * 45L
        val t = p.apt[i][k]
        if (t >= 0) pen += kotlin.math.abs(n - t).toLong()
        return pen
    }

    /** [3.267.0/weekly+fair統合、旧3.170.0「focus露出のみ・cost未対応」の解消] weekly(7日周期のシフト
     *  平準化)の marginal cost。wd は staff のシフト別曜日カウント([K][7]、呼出元が維持)。
     *  [3.345.0] 休を通常のシフト種として扱うため、勤務/休の二値でなく **oldK→newK のシフト移動** を受ける。
     *  動くのは oldK と newK の2バケットだけ（oldK==newK は 0）。weeklyDevOfBucket(checkerと同一式)の
     *  変化のみを計算し、wd 自体は変更しない(コミットは呼出元)。範囲外の値は該当側を寄与ゼロとして扱う。 */
    internal fun weeklyMarginalAt(wd: Array<IntArray>, bucket: Int, oldK: Int, newK: Int): Long {
        if (oldK == newK) return 0L
        var acc = 0L
        if (oldK in wd.indices) {
            val b = wd[oldK]
            val before = weeklyDevOfBucket(b)
            b[bucket]--
            acc += (weeklyDevOfBucket(b) - before).toLong()
            b[bucket]++
        }
        if (newK in wd.indices) {
            val b = wd[newK]
            val before = weeklyDevOfBucket(b)
            b[bucket]++
            acc += (weeklyDevOfBucket(b) - before).toLong()
            b[bucket]--
        }
        return acc
    }

    /** fair(グループ内公平化)の marginal cost。staff i の shift k 保有回数が delta 変化した際の、群
     *  g=p.sgrp[i] のシフト k における L1偏差(checkerと同一式)の変化。m<2(公平化対象外)・k が群の
     *  担当外なら 0（対象外セルは無害にゼロ扱い）。counts/grpTotal は呼出元が維持する S×K・G×K 集計。 */
    internal fun fairMarginalAt(
        p: Problem, i: Int, k: Int, delta: Int, counts: Array<IntArray>, grpTotal: Array<IntArray>,
    ): Long {
        if (delta == 0 || k !in 0 until p.K) return 0L
        val g = p.sgrp[i]
        val mem = p.groupMembers[g]
        val m = mem.size
        if (m < 2 || k !in p.bucket[g]) return 0L
        fun dev(sum: Int): Int {
            val tgt = Math.round(sum.toDouble() / m).toInt()
            var d = 0
            for (x in mem) d += kotlin.math.abs(counts[x][k] - tgt)
            return d
        }
        val before = dev(grpTotal[g][k])
        counts[i][k] += delta
        val after = dev(grpTotal[g][k] + delta)
        counts[i][k] -= delta
        return (after - before).toLong()
    }

    internal fun destroyRepairDayAt(state: MagiState, schedule: Array<IntArray>, j: Int, rng: Random) {
        val p = cachedProblem(state)
        if (p.T == 0) return
        // [soft-aware destroy-repair / 実測検証 tools/nsp_bench.py] 従来はランダム順で穴を埋めるだけ(soft無視)で、
        //   等価ベンチでは soft-aware 修復が AUC -24%〜-34% と唯一の大幅改善だった。ここで同じレバーを適用:
        //   非希望セルを休へ destroy → 各需要を「割当の marginal soft が最小の休スタッフ」で repair。
        //   休→k のみ移すため被覆穴を新たに作らない。希望固定は保持。受理(SA/isBetter)が最終採否=安全。
        val rest = restShiftIndex(state)   // [監査#2] 休はindex0固定でなく記号から解決（Level Zero: 全シフト同等・番号非依存）
        val cnt = Array(p.S) { IntArray(p.K) }
        for (i in 0 until p.S) for (jj in 0 until p.T) { val k = schedule[i][jj]; if (k in 0 until p.K) cnt[i][k]++ }
        // destroy: 非希望セルを休へ。休を担当できない職員は対象外（群外割当を作らない）。cnt も同期。
        for (i in 0 until p.S) {
            if (p.wishLocked(i, j) || !p.canDo(i, rest)) continue
            val old = schedule[i][j]
            if (old != rest && old in 0 until p.K) { schedule[i][j] = rest; cnt[i][old]--; cnt[i][rest]++ }
        }
        val covJ = IntArray(p.K)
        for (i in 0 until p.S) { val k = schedule[i][j]; if (k in 0 until p.K) covJ[k]++ }
        // [c41-aware / 実測 tools/nsp_bench.py: 群レンジ(cons41)があると小幅改善・無ければゼロ overhead で無害]
        //   群の「日次人数レンジ(cons41)」も marginal に加味し、群レンジ(上下限)も同時に研磨する。
        val hasC41 = p.cons41.isNotEmpty()
        val grpCnt = if (hasC41) Array(p.G) { IntArray(p.K) } else emptyArray()
        if (hasC41) for (i in 0 until p.S) { val k = schedule[i][j]; if (k in 0 until p.K) grpCnt[p.sgrp[i]][k]++ }
        fun c41DayMarg(g: Int, k: Int): Long {
            if (!hasC41) return 0L
            var d = 0L
            for (c in p.cons41) {
                if (c.groupIdx != g || c.shiftIdx != k) continue
                val z = grpCnt[g][k]; val z1 = z + 1
                val before = (if (z < c.l) c.l - z else 0) + (if (z > c.u) z - c.u else 0)
                val after = (if (z1 < c.l) c.l - z1 else 0) + (if (z1 > c.u) z1 - c.u else 0)
                d += (after - before).toLong()
            }
            return d
        }
        // [3.267.0/weekly+fair統合] 群合計(fair, 月間total)と職員別曜日バケット(weekly)を一度だけ構築
        // （destroy後のschedule基準＝c41のgrpCntと同じ順序）。day j は固定のため bucket は全候補共通。
        val grpTotal = Array(p.G) { IntArray(p.K) }
        for (i in 0 until p.S) for (k in 0 until p.K) grpTotal[p.sgrp[i]][k] += cnt[i][k]
        val wd = Array(p.S) { s ->
            Array(p.K) { IntArray(7) }.also { a ->
                for (jj in 0 until p.T) { val k2 = schedule[s][jj]; if (k2 in 0 until p.K) a[k2][(p.dow0 + jj) % 7]++ }
            }
        }
        val bucket = (p.dow0 + j) % 7
        // repair: 各勤務シフトの需要を soft(個人 low/high/apt/weekly/fair ＋ 群レンジ c41)最小の休スタッフで満たす。
        for (k in 0 until p.K) {
            if (k == rest) continue   // [監査#2] 休以外の全シフトを対象（旧: k in 1..K-1 の「0=休」前提）
            // [3.379.0/need1直参照の第4世代] 旧: `p.need1[k][j] <= 0 → continue` で
            //   **need2 単独定義の需要を丸ごと素通り**していた（need1 未設定は -1）。3.173.0
            //   (CoverageDiagnosis)・3.309.0(isBalanceable)・3.369.0(初期解生成2つ+findCovOFix)で
            //   同じ穴を潰したのに、**RSI/ALNS 修復の中核であるこの2関数が取り残されていた**＝
            //   そのデータでは covU(HARD, 重み8000) を修復オペレータが原理的に埋められない。
            //   source of truth の `covUCell`（片方定義=その値）へ委譲する。
            var miss = p.covUCell(k, j, covJ[k])
            if (miss <= 0) continue
            while (miss > 0) {
                var bestI = -1; var bestDelta = Long.MAX_VALUE; var tied = 0
                for (i in 0 until p.S) {
                    if (schedule[i][j] != rest || p.wishLocked(i, j) || !p.canDo(i, k)) continue
                    val delta = staffCountPenaltyAt(p, i, k, cnt[i][k] + 1) - staffCountPenaltyAt(p, i, k, cnt[i][k]) +
                        c41DayMarg(p.sgrp[i], k) +
                        weeklyMarginalAt(wd[i], bucket, rest, k) +
                        fairMarginalAt(p, i, rest, -1, cnt, grpTotal) +
                        fairMarginalAt(p, i, k, 1, cnt, grpTotal)
                    if (delta < bestDelta) {
                        bestDelta = delta; bestI = i; tied = 1
                    } else if (delta == bestDelta) {
                        tied++
                        if (HypothesisDiversityPolicy.takeReservoirTie(tied, rng)) bestI = i
                    }
                }
                if (bestI < 0) break
                schedule[bestI][j] = k; cnt[bestI][k]++; cnt[bestI][rest]--; covJ[k]++; miss--
                if (hasC41) grpCnt[p.sgrp[bestI]][k]++
                grpTotal[p.sgrp[bestI]][k]++; grpTotal[p.sgrp[bestI]][rest]--
                wd[bestI][rest][bucket]--; wd[bestI][k][bucket]++
            }
        }
    }

    private fun destroyRepairStaff(state: MagiState, schedule: Array<IntArray>, rng: Random) {
        val p = cachedProblem(state)
        if (p.S == 0) return
        destroyRepairStaffAt(state, schedule, rng.nextInt(p.S), rng)
    }

    internal fun destroyRepairStaffAt(state: MagiState, schedule: Array<IntArray>, i: Int, rng: Random) {
        val p = cachedProblem(state)
        val allowed = p.allowedShiftsForStaff(i)
        if (allowed.isEmpty()) return
        val rest = restShiftIndex(state)   // [監査#2] 休の記号解決
        if (!p.canDo(i, rest)) return      // 休を担当できない職員は破壊修復の対象外（群外割当を作らない）
        // [soft-aware staff-DR / 実測 tools/nsp_bench.py --real: staff+viol で実データ final -49.5%]
        //   非希望セルを休へ destroy → 各日の被覆穴を「staff i の marginal soft 最小のシフト」で repair。
        //   被覆穴のみ埋める(過剰=covO を作らない)。希望固定は保持。スコアリング不変=Δ×フル無関係。
        // [3.267.0/weekly+fair統合] fair(群内公平化)は群メンバー全員の月間totalが要るため、counts は
        // 全職員S×Kで構築する（cntI は counts[i] の別名＝同一配列参照、以降どちらの名前で更新しても
        // 他方に反映される）。grpTotal(G×K, 群合計)とwd(staff iの曜日別非休日数, 7要素)も一度だけ構築。
        val counts = Array(p.S) { s ->
            IntArray(p.K).also { a -> for (jj in 0 until p.T) { val k = schedule[s][jj]; if (k in 0 until p.K) a[k]++ } }
        }
        val cntI = counts[i]
        val grpTotal = Array(p.G) { IntArray(p.K) }
        for (s in 0 until p.S) for (k in 0 until p.K) grpTotal[p.sgrp[s]][k] += counts[s][k]
        val wd = Array(p.K) { IntArray(7) }
        for (jj in 0 until p.T) { val k2 = schedule[i][jj]; if (k2 in 0 until p.K) wd[k2][(p.dow0 + jj) % 7]++ }
        for (j in 0 until p.T) {
            if (p.wishLocked(i, j)) continue
            val old = schedule[i][j]
            if (old != rest && old in 0 until p.K) {
                schedule[i][j] = rest
                cntI[old]--; cntI[rest]++
                grpTotal[p.sgrp[i]][old]--; grpTotal[p.sgrp[i]][rest]++
                wd[old][(p.dow0 + j) % 7]--; wd[rest][(p.dow0 + j) % 7]++
            }
        }
        // [高速化] 旧: 日×シフトごとに被覆を全職員走査(O(T×K×S))。盤面のうち本関数中に変わるのは staff i の行
        //   だけなので、被覆を一度だけ数え(O(S×T))、割当のたびに差分更新する(O(T×K))。挙動は再カウントと同一。
        val cov = Array(p.T) { IntArray(p.K) }
        for (x in 0 until p.S) for (j in 0 until p.T) { val k2 = schedule[x][j]; if (k2 in 0 until p.K) cov[j][k2]++ }
        for (j in 0 until p.T) {
            if (p.wishLocked(i, j) || schedule[i][j] != rest) continue
            val bucket = (p.dow0 + j) % 7
            var bestK = -1; var bestDelta = Long.MAX_VALUE; var tied = 0
            for (k in 0 until p.K) {
                if (k == rest || !p.canDo(i, k)) continue
                // [3.379.0/同上] need2 単独定義の穴を塞ぐ。`covUCell<=0` は「需要なし」と
                //   「既に足りている」の両方を同時に表すので、旧2条件をこれ1つで置き換えられる。
                if (p.covUCell(k, j, cov[j][k]) <= 0) continue
                val delta = staffCountPenaltyAt(p, i, k, cntI[k] + 1) - staffCountPenaltyAt(p, i, k, cntI[k]) +
                    weeklyMarginalAt(wd, bucket, rest, k) +
                    fairMarginalAt(p, i, rest, -1, counts, grpTotal) +
                    fairMarginalAt(p, i, k, 1, counts, grpTotal)
                if (delta < bestDelta) {
                    bestDelta = delta; bestK = k; tied = 1
                } else if (delta == bestDelta) {
                    tied++
                    if (HypothesisDiversityPolicy.takeReservoirTie(tied, rng)) bestK = k
                }
            }
            if (bestK >= 0) {
                schedule[i][j] = bestK
                cntI[bestK]++; cntI[rest]--
                grpTotal[p.sgrp[i]][bestK]++; grpTotal[p.sgrp[i]][rest]--
                wd[rest][bucket]--; wd[bestK][bucket]++
                cov[j][bestK]++; cov[j][rest]--
            }
        }
    }

    private fun destroyRepairViolations(state: MagiState, schedule: Array<IntArray>, report: ViolationReport, rng: Random) {
        val p = cachedProblem(state)
        val keys = report.violations.keys.toList()
        if (keys.isEmpty()) { randomAllowedCell(state, schedule, rng); return }
        repeat(min(8, keys.size)) {
            val key = keys[rng.nextInt(keys.size)]
            val i = key.substringBefore(',').toIntOrNull() ?: return@repeat
            val j = key.substringAfter(',').toIntOrNull() ?: return@repeat
            if (i !in 0 until p.S || j !in 0 until p.T || p.wishLocked(i, j)) return@repeat
            val allowed = p.allowedShiftsForStaff(i)
            if (allowed.isEmpty()) return@repeat
            // [soft-aware violations / 実測で実データ final -22.6%] 違反セルを、staff i の現状回数で
            //   marginal soft(old→k)最小のシフトへ再割当(従来はランダム)。スコアリング不変=Δ×フル無関係。
            val cntI = IntArray(p.K)
            for (jj in 0 until p.T) { val k = schedule[i][jj]; if (k in 0 until p.K) cntI[k]++ }
            // [3.267.0/weekly+fair統合] この手専用にwd(staff iの曜日別非休日数)とgrpTotal(群合計, 全職員
            // スキャン)を構築。件数は最大8回(repeat)に限られ盤面規模も小さいため、毎回の再走査を許容する。
            val wd = Array(p.K) { IntArray(7) }
            for (jj in 0 until p.T) { val k2 = schedule[i][jj]; if (k2 in 0 until p.K) wd[k2][(p.dow0 + jj) % 7]++ }
            val counts = Array(p.S) { s ->
                IntArray(p.K).also { a -> for (jj in 0 until p.T) { val k = schedule[s][jj]; if (k in 0 until p.K) a[k]++ } }
            }
            val grpTotal = Array(p.G) { IntArray(p.K) }
            for (s in 0 until p.S) for (k in 0 until p.K) grpTotal[p.sgrp[s]][k] += counts[s][k]
            val bucket = (p.dow0 + j) % 7
            val old = schedule[i][j]
            var bestK = old; var bestDelta = Long.MAX_VALUE; var tied = 0
            for (k in allowed) {
                if (k == old) continue
                val dOld = if (old in 0 until p.K) staffCountPenaltyAt(p, i, old, cntI[old] - 1) - staffCountPenaltyAt(p, i, old, cntI[old]) else 0L
                val dK = staffCountPenaltyAt(p, i, k, cntI[k] + 1) - staffCountPenaltyAt(p, i, k, cntI[k])
                val dWeekly = weeklyMarginalAt(wd, bucket, old, k)
                val dFair = (if (old in 0 until p.K) fairMarginalAt(p, i, old, -1, counts, grpTotal) else 0L) +
                    fairMarginalAt(p, i, k, 1, counts, grpTotal)
                val delta = dOld + dK + dWeekly + dFair
                if (delta < bestDelta) {
                    bestDelta = delta; bestK = k; tied = 1
                } else if (delta == bestDelta) {
                    tied++
                    if (HypothesisDiversityPolicy.takeReservoirTie(tied, rng)) bestK = k
                }
            }
            if (bestK != old) schedule[i][j] = bestK
        }
    }

    private fun randomAllowedCell(state: MagiState, schedule: Array<IntArray>, rng: Random) {
        val p = cachedProblem(state)
        if (p.S == 0 || p.T == 0) return
        val i = rng.nextInt(p.S)
        val j = rng.nextInt(p.T)
        if (p.wishLocked(i, j)) return
        val allowed = p.allowedShiftsForStaff(i)
        if (allowed.isNotEmpty()) schedule[i][j] = allowed[rng.nextInt(allowed.size)]
    }

    private fun perturb(state: MagiState, base: Array<IntArray>, rng: Random, strength: Double): Array<IntArray> {
        val p = cachedProblem(state)
        val out = base.copy2D()
        val n = max(1, (p.S * p.T * strength).toInt())
        repeat(n) { randomAllowedCell(state, out, rng) }
        return out
    }

    /** [3.240.0] destroyRepairStaff(1回で最大T(日数)セル変化)を、destroyRepairDay(1回で最大S(職員数)
     *  セル変化・covU focusでrepeat(6))と同程度の総攪乱セル数(6*S)に揃えるための反復回数。S>=Tなら
     *  従来のrepeat(8)相当以上(reps>=6の切り上げ計算)を維持し既存の攪乱強度を落とさない。 */
    internal fun destroyRepairStaffReps(s: Int, t: Int): Int = max(1, (6 * s + t - 1) / max(1, t))

    internal fun rsiGenerateHypothesis(
        state: MagiState, base: Array<IntArray>, report: ViolationReport, focus: String, rng: Random,
        // [3.313.0] free repair 群へ締切を通す。既定 `{ false }` ＝既存の直接呼出・テストは挙動不変。
        shouldStop: () -> Boolean = { false },
    ): Array<IntArray> {
        val out = base.copy2D()
        val p = cachedProblem(state)
        when (focus) {
            // [E11] covU は「勤務→勤務」の多人数連鎖で充填（既存 destroyRepairDay は休→勤務のみ＝
            //   候補が過剰シフト/連鎖からしか引けない局面を踏めない）。仮説はラウンド better() でゲート＝退化なし。
            // [3.241.0/実機ログ起因=専用オペレータの改善がdestroyRepairDayで相殺される順序バグ修正]
            //   旧実装は「専用free関数→destroyRepairDay×6」の順で、destroyRepairDayのdestroy段階
            //   （非希望セルを休へ変える）がneed<=0のシフト（covOの主対象＝休等）へは一切repair
            //   （need>0のシフトのみ埋め戻す設計）が働かないため、直後の専用オペレータの改善を
            //   ランダムに(31日中6日=無視できない確率で)打ち消してしまっていた（8/26の休過剰1が
            //   covO focusのラウンドでも解消されなかった実例）。covU/c41/c41sの不足側はrepair段階
            //   （need>0のシフトを埋める設計）で自動的に再修復されるため実害は薄いが、covO/c42/c42s
            //   の過剰・違反ペア解消はrepairの対象外で影響が直接的。順序を「destroyRepairDay×6→
            //   専用free関数」へ統一し、hypothesisの最終状態に専用オペレータの改善が必ず残るようにする。
            "covU" -> { repeat(6) { destroyRepairDay(state, out, rng) }; applyCovUChains(state, out, rng, shouldStop) }
            // [3.209.0/covOと同型の穴=c41/c41sがfocusされてもdestroyRepairDayのc41DayMargは副次効果でしか
            //   効かない] markNeed系(needViolations)にしか載らずGLSキック/destroyRepairViolationsのヒントを
            //   一切持てない点がcovOと同じ。applyC41Freeで群レンジの超過/不足を直接動かす専用オペレータへ。
            "c41" -> { repeat(6) { destroyRepairDay(state, out, rng) }; applyC41Free(state, out, rng, skill = false, shouldStop = shouldStop) }
            "c41s" -> { repeat(6) { destroyRepairDay(state, out, rng) }; applyC41Free(state, out, rng, skill = true, shouldStop = shouldStop) }
            // [3.233.0/c41,c41sと同型の穴] c42/c42sも「動かせるか」を判定する専用オペレータが無く
            // destroyRepairViolationsの汎用ランダム再割当頼みだった。applyC42Freeで違反ペアの
            // 片側を直接動かす専用オペレータへ。
            "c42" -> { repeat(6) { destroyRepairDay(state, out, rng) }; applyC42Free(state, out, rng, skill = false, shouldStop = shouldStop) }
            "c42s" -> { repeat(6) { destroyRepairDay(state, out, rng) }; applyC42Free(state, out, rng, skill = true, shouldStop = shouldStop) }
            // [実機ログ起因=apt未focus] apt(適切回数)は maxViolatedFamily の order に無く探索中は一度も focus
            //   されなかった（post-processing の applyDayAssignmentPolish 頼み）。destroyRepairStaff の marginal
            //   cost(staffCountPenaltyAt)は既にaptを織込み済み(重み1)のため、low/high/c2と同じ経路へ合流するだけで
            //   apt専用の新規オペレータ不要。ラウンド better() keep-best でゲート＝退化なし。
            // [同根の穴=weekly/fair] 同じ理由で weekly/fair も order に無く一度も focus されていなかった
            //   （実データ検証: weekly L1偏差合計65(aptの37より大きい)・fair合計11）。staffCountPenaltyAt は
            //   weekly/fair 未対応(曜日バケット・群平均を持たない)のため厳密な cost-aware 研磨ではないが、
            //   destroyRepairStaff は職員1人の月全体を破壊再構築する汎用オペレータであり、weekly/fair が
            //   支配的なときに専用ラウンドを割り当てるだけでも「total」の無指向な空振りより改善機会が増える。
            //   ラウンド better() keep-best でゲート＝退化なし（厳密な cost 統合は将来の拡張候補）。
            // [3.240.0/実機ログ起因=5ラウンド完全停滞の修正] destroyRepairStaff は「1人を丸ごと非希望日
            //   全休化→被覆穴のみ埋め直す」という1回で最大T(日数)セルを変える大きな摂動。covU focus の
            //   destroyRepairDay(1回で最大S(職員数)セル、repeat(6))と揃えるはずが、固定repeat(8)のまま
            //   だったため、S<T のデータ（実機=10職員/31日）では1ラウンドの総攪乱セル数が数倍に膨らみ、
            //   60秒/ラウンドのSA/ALNSでは破壊前の解に匹敵する状態まで回復しきれず、5ラウンド全て
            //   total不変のまま予算を使い切っていた（runV5の入力比番兵はhypothesis自体との比較のため
            //   この過大摂動を防げない）。destroyRepairDay基準(6回×S人ぶんのセル変化)に総攪乱セル数を
            //   揃えるよう reps を動的計算する（S>=T のデータでは reps>=6 相当まで許容＝挙動退化なし）。
            "low", "high", "c2", "apt", "weekly", "fair" -> {
                repeat(destroyRepairStaffReps(p.S, p.T)) { destroyRepairStaff(state, out, rng) }
            }
            // [3.204.0/実機ログ起因=covOがfocusされても直せなかった] covO は markNeed(k,j) で needViolations に
            //   載り、report.violations(セル"i,j"マップ)には現れないため、他の focus 未対応族の else 分岐が
            //   使う destroyRepairViolations(report.violations.keys 基準)では covO 専用のヒントが1つも無く、
            //   focus が回っても実質ランダムな空振りになっていた。covO診断(V6PortAnalyzer.diagnoseCoverage)と
            //   同じ「動かせるか」判定(希望固定/禁止連続を避け・移動先でcovOを悪化させない)をその場で「実行」する
            //   専用オペレータ applyCovOFree を新設し、covU chain(applyCovUChains)と対称に配線する。
            //   [3.241.0] destroyRepairDayを先に(順序バグ修正、上記covUコメント参照)＝covOは特にneed<=0
            //   シフト(休等)の過剰が主対象でrepair段階の恩恵が皆無のため、この順序修正の効果が最も直接的。
            "covO" -> { repeat(6) { destroyRepairDay(state, out, rng) }; applyCovOFree(state, out, rng, shouldStop) }
            // [実機ログ起因] groupViol/pref は hf67 の作用対象(hf66DataHardening=群外修正・希望反映)だが、
            //   c3n(禁止連続=HARD)は hf67 が一切作用しない(被覆/希望/下限のみ)＝c3n focus のラウンドが no-op 仮説で
            //   空転していた(実機3実行×計10ラウンドで c3n=1 不変→HF63 が c3n を誤 infeasible 判定)。c3n のセルは
            //   violations マップに載る(両端2セル)ので、違反セルを直接再割当する destroyRepairViolations(else)へ回す。
            //   仮説はラウンド単位 better() keep-best でゲート済＝退化なし。
            "groupViol", "pref" -> {
                val fixed = hf67HardRepair(state, out, rng).schedule
                for (i in 0 until p.S) for (j in 0 until p.T) out[i][j] = fixed[i][j]
            }
            else -> repeat(12) { destroyRepairViolations(state, out, report, rng) }
        }
        return out
    }

    /**
     * [E11/多人数ブロック移動] 現盤面の全 covU セルを、同日・多人数の玉突き連鎖（findCovUChain）で
     * 充填する。sched を in-place 変更し、適用手数を返す。連鎖は同日内交換＝被覆総量保存で、canDo/非wishLocked/
     * c3n枝刈り済み。最終採否は呼び出し側の keep-best（ラウンド better() or エピローグの checker 照合）が担保。
     * ユーザー実例（2026-08）: 8/11 モニカ B4→Cｵ（深さ1）／8/17 上條 Cｵ→Cｱ・山本 →Cｵ（深さ2）。
     */
            // [3.313.0] 締切/キャンセル確認。これらは違反セル × 候補職員の二重ループの内側で
        //   フル checker（commitBestMove）と findCovUChain(BFS) を呼ぶ高コストパスで、旧実装は
        //   停止確認を一切持たなかった（3.161.0 で V6HotfixPasses の研磨パスへ入れた「内側ループ
        //   でも締切を見る」の対象漏れ）。既定 `{ false }` なので既存の直接呼出＝挙動不変。
private fun applyCovUChains(
        state: MagiState, sched: Array<IntArray>, rng: Random,
        shouldStop: () -> Boolean = { false },
    ): Int {
        val p = cachedProblem(state)
        if (p.S == 0 || p.T == 0) return 0
        var applied = 0
        val cnt = IntArray(p.K)
        for (j in 0 until p.T) {
            if (shouldStop()) return applied
            for (k in 0 until p.K) cnt[k] = 0
            for (i in 0 until p.S) { val kk = sched[i][j]; if (kk in 0 until p.K) cnt[kk]++ }
            for (k in 0 until p.K) {
                if (p.covUCell(k, j, cnt[k]) <= 0) continue
                val chain = findCovUChain(p, sched, k, j, rng) ?: continue
                for (mv in chain) sched[mv[0]][mv[1]] = mv[2]
                applied++
                // 同日に複数 covU があり得るので当日カウントを再計算。
                for (kk in 0 until p.K) cnt[kk] = 0
                for (i in 0 until p.S) { val kk = sched[i][j]; if (kk in 0 until p.K) cnt[kk]++ }
            }
        }
        return applied
    }

    /**
     * [3.204.0/covO専用repair] 人員過剰(covO)セルの在勤者のうち、他シフトへ移しても新たな違反を生まない
     * （希望固定でない・移すと禁止連続(c3n)を作らない・移動先で covO が悪化しない＝受け皿あり）候補を1人
     * 見つけて実際に移す。V6PortAnalyzer.diagnoseCoverage の covO 診断（動かせる/玉突き必要/希望固定/
     * 禁止連続の4分類）と同じ判定を「実行」する版（診断＝読取専用、こちらは探索オペレータ）。
     * 被覆総量は保存しない（過剰シフトから1人引くだけ＝covOのみ改善方向）。動かせる候補が尽きたセルは
     * そのまま残す（希望固定/または隣接日調整(下記)を含め本当に動かせない、または玉突きが要る＝本
     * オペレータの対象外）。sched を in-place 変更し、適用手数を返す。最終採否は呼び出し側の
     * keep-best が担保＝退化なし。
     *
     * [3.226.0/禁止連続の回避=隣接日調整] 移動先が全て禁止連続(c3n)で塞がる場合、即諦めず
     * `tryFixForbiddenRunViaAdjacentDay`（findCovUChainのcovU側と共通のヘルパー）で隣接日(j-1/j+1)の
     * 本人の割当を変えてパターンを崩せないか試す。空くシフトのcovU悪化はfindCovUChainで玉突き埋め直し
     * 済み（ヘルパー内部で完結）。ここでは追加で「隣接日の変更後、移動先mでcovOが悪化しないか」を
     * 確認し、悪化するなら隣接日側の変更ごと巻き戻して次の候補へ（実際に適用したcov[j2]/schedは
     * 必ず復元してから次を試す）。
     */
    /**
     * [3.253.0, 実データ検証で判明した「Free」系リペア共通の欠陥を修正] `applyCovOFree`/`applyC41Free`/
     * `applyC42Free` は従来「移動先/移動元のcovU/covOだけを見て構造的に安全な最初の候補」を採用しており、
     * 動かす本人自身の他制約(staffRange低/高・apt・c1・c2・weekly・fair等)への影響を一切見ずに移動していた。
     * 実データ検証(golden_state.json/sample_state_v6.json、ホストJVM実行で独立検証)で、covOは単体実行の
     * 大半の試行でtotalを悪化(313→325〜351)、c42はgolden 15/15・sample_v6 11/15が悪化——「動かせる」は
     * 「動かして得」を意味しないことを確認した（ユーザー指摘「大嶋と美幸の違反研磨は適切か」を機に、
     * AptPolish/RangePolishは既に全候補で実チェッカー+isBetter/betterのkeep-best gateを持つ健全な実装と
     * 確認済み＝この欠陥はcovO/c41/c41s/c42/c42s専用のFree系のみ）。
     *
     * 候補（セル代入の束＝直接移動、または移動＋玉突き連鎖の複合手）を1つずつ実際に一時適用し、
     * UnifiedViolationChecker で全体評価、baseline(この手を試す直前の盤面)に対して真に改善する
     * (better()=hard→weighted→total辞書式で厳密改善)候補の中から最良の1件だけを選んでコミットする。
     * 改善する候補が1つも無ければ何もしない(null)＝そのセルは諦める（安全側・退化不能）。
     * 実装コストは度外視（ユーザー指示）＝候補ごとにフルcheckを行うため計算量は増えるが、
     * これらはRSI 1ラウンドにつき1回しか呼ばれない仮説生成器のため許容範囲。
     */
    private fun commitBestMove(
        state: MagiState, sched: Array<IntArray>,
        baseline: ViolationReport, candidates: List<List<IntArray>>,
    ): ViolationReport? {
        var bestOps: List<IntArray>? = null
        var bestRep: ViolationReport? = null
        for (ops in candidates) {
            val saved = IntArray(ops.size) { sched[ops[it][0]][ops[it][1]] }
            for (mv in ops) sched[mv[0]][mv[1]] = mv[2]
            val rep = UnifiedViolationChecker.check(state, sched)
            for (idx in ops.indices) sched[ops[idx][0]][ops[idx][1]] = saved[idx]
            if (better(rep, baseline) && (bestRep == null || better(rep, bestRep!!))) {
                bestOps = ops; bestRep = rep
            }
        }
        val ops = bestOps ?: return null
        for (mv in ops) sched[mv[0]][mv[1]] = mv[2]
        return bestRep
    }

            // [3.313.0] 締切/キャンセル確認。これらは違反セル × 候補職員の二重ループの内側で
        //   フル checker（commitBestMove）と findCovUChain(BFS) を呼ぶ高コストパスで、旧実装は
        //   停止確認を一切持たなかった（3.161.0 で V6HotfixPasses の研磨パスへ入れた「内側ループ
        //   でも締切を見る」の対象漏れ）。既定 `{ false }` なので既存の直接呼出＝挙動不変。
internal fun applyCovOFree(
        state: MagiState, sched: Array<IntArray>, rng: Random,
        shouldStop: () -> Boolean = { false },
    ): Int {
        val p = cachedProblem(state)
        if (p.S == 0 || p.T == 0) return 0
        var applied = 0
        for (j in 0 until p.T) {
            if (shouldStop()) return applied
            for (k in 0 until p.K) {
                while (true) {
                    if (shouldStop()) return applied
                    val cov = IntArray(p.K)
                    for (i in 0 until p.S) { val kk = sched[i][j]; if (kk in 0 until p.K) cov[kk]++ }
                    if (p.covOCell(k, j, cov[k]) <= 0) break
                    val baseline = UnifiedViolationChecker.check(state, sched)
                    val staffOnK = (0 until p.S).filter { sched[it][j] == k }
                    val candidates = ArrayList<List<IntArray>>()
                    for (i in staffOnK) {
                        // [3.391.0] 生の `wish==k` は**実現不能な希望**（担当できないシフトへの希望）まで
                        //   固定扱いにしていた。pref は実現可能な希望しか数えない（MirrorCore）ので、
                        //   その場合ここを動かしても pref は増えず、逆に担当外セル＝groupViol(10000) が消える
                        //   ＝**必須違反が厳密に減る手を丸ごと捨てていた**。規約の wishLocked へ統一（3.351.0 と同型）。
                        if (p.wishLocked(i, j) && p.wish[i][j] == k) continue   // 実現可能な本人希望＝動かすとpref未充足化
                        for (m in p.allowedShiftsForStaff(i).filter { it != k }) {
                            if (p.makesForbiddenRun(sched, i, j, m)) {
                                val fix = tryFixForbiddenRunViaAdjacentDay(p, sched, i, j, m, rng) ?: continue
                                candidates.add(fix + listOf(intArrayOf(i, j, m)))
                            } else {
                                candidates.add(listOf(intArrayOf(i, j, m)))
                            }
                        }
                    }
                    if (candidates.isEmpty()) break
                    if (commitBestMove(state, sched, baseline, candidates) == null) break
                    applied++
                }
            }
        }
        return applied
    }

    /**
     * [3.209.0/c41・c41s専用repair] c41/c41s（群×日×シフトの人数レンジ[l,u]違反）は covO/covU と同じく
     * markNeed(needViolations)にしか載らず report.violations（職員×日マップ）には現れないため、
     * GLSキック・destroyRepairViolations が一切ヒントを持てず、RSI focus されても
     * applyCovUChains+destroyRepairDay（covU=シフト単位の不足専用）では群レンジの上限超過・下限割れの
     * どちらも直接には狙えない（destroyRepairDayAt の c41DayMarg は covU 充填の副次効果でしか働かない）。
     * covO診断/applyCovOFreeと同じ「動かせるか」判定をこの群レンジにも適用し、超過なら群内在籍者を
     * 他シフトへ・不足なら群内の他シフト在籍者を引き入れて実際に解消する。skill=false は cons41(sgrp)、
     * skill=true は cons41s(ssk) を対象にする（DRY化）。希望固定でない・禁止連続(c3n)を作らない・
     * 移動元/移動先で covU/covO を悪化させない候補のみ動かす。sched を in-place 変更し適用手数を返す。
     * 最終採否は呼び出し側の keep-best が担保＝退化不能。
     */
            // [3.313.0] 締切/キャンセル確認。これらは違反セル × 候補職員の二重ループの内側で
        //   フル checker（commitBestMove）と findCovUChain(BFS) を呼ぶ高コストパスで、旧実装は
        //   停止確認を一切持たなかった（3.161.0 で V6HotfixPasses の研磨パスへ入れた「内側ループ
        //   でも締切を見る」の対象漏れ）。既定 `{ false }` なので既存の直接呼出＝挙動不変。
internal fun applyC41Free(
        state: MagiState, sched: Array<IntArray>, rng: Random, skill: Boolean,
        shouldStop: () -> Boolean = { false },
    ): Int {
        val p = cachedProblem(state)
        if (p.S == 0 || p.T == 0) return 0
        val rules = if (skill) p.cons41s else p.cons41
        if (rules.isEmpty()) return 0
        val grp = if (skill) p.ssk else p.sgrp
        var applied = 0
        fun groupCount(c: C41, j: Int): Int {
            var z = 0
            for (i in 0 until p.S) if (grp[i] == c.groupIdx && sched[i][j] == c.shiftIdx) z++
            return z
        }
        // [3.253.0, commitBestMoveへ全面移行] 旧実装は「離脱元/到着先のcovU/covOが非悪化」を満たす
        //   最初の候補（見つからなければ玉突き連鎖の最初の候補）で即採用しており、動かす本人自身の
        //   staffRange/apt/c1/c2/weekly/fair等への影響を一切見ていなかった（実データ検証でcovO/c42の
        //   同型実装が大半の試行でtotalを悪化させることを確認、詳細はcommitBestMoveのdoc参照）。
        //   ここでは構造的に安全（希望非固定・禁止連続なし）な候補を直接移動・玉突き連鎖の両方で
        //   網羅的に集め、commitBestMoveが実チェッカーで全体評価して真に改善する最良の1件だけを選ぶ。
        for (c in rules) {
            if (shouldStop()) return applied
            for (j in 0 until p.T) {
                if (shouldStop()) return applied
                // 超過(z>u): 群在籍者を他シフトへ移す。
                while (groupCount(c, j) > c.u) {
                    val baseline = UnifiedViolationChecker.check(state, sched)
                    val onShift = (0 until p.S).filter { grp[it] == c.groupIdx && sched[it][j] == c.shiftIdx }
                    val candidates = ArrayList<List<IntArray>>()
                    for (i in onShift) {
                        // [3.391.0] 実現不能な希望は固定しない（wishLocked へ統一）。上の applyCovOFree と同型。
                        if (p.wishLocked(i, j) && p.wish[i][j] == c.shiftIdx) continue   // 実現可能な本人希望＝対象外
                        for (m in p.allowedShiftsForStaff(i).filter { it != c.shiftIdx }) {
                            if (p.makesForbiddenRun(sched, i, j, m)) continue
                            candidates.add(listOf(intArrayOf(i, j, m)))
                            // 玉突き連鎖版（離脱先を先に適用してから探索＝本人がまだ在籍中に見える誤判定を防ぐ既定の作法）。
                            val oldK = sched[i][j]
                            sched[i][j] = m
                            val chain = findCovUChain(p, sched, c.shiftIdx, j, rng, exclude = i)
                            sched[i][j] = oldK
                            if (chain != null) candidates.add(listOf(intArrayOf(i, j, m)) + chain)
                        }
                    }
                    if (candidates.isEmpty()) break
                    if (commitBestMove(state, sched, baseline, candidates) == null) break
                    applied++
                }
                // 不足(z<l): 群内の他シフト在籍者を引き入れる。
                while (groupCount(c, j) < c.l) {
                    val baseline = UnifiedViolationChecker.check(state, sched)
                    val offShift = (0 until p.S).filter { grp[it] == c.groupIdx && sched[it][j] != c.shiftIdx && p.canDo(it, c.shiftIdx) }
                    val candidates = ArrayList<List<IntArray>>()
                    for (i in offShift) {
                        val old = sched[i][j]
                        // [3.391.0] 実現不能な希望は固定しない（wishLocked へ統一）。
                        if (old !in 0 until p.K || (p.wishLocked(i, j) && p.wish[i][j] == old)) continue   // 現シフトが実現可能な本人希望＝対象外
                        if (p.makesForbiddenRun(sched, i, j, c.shiftIdx)) continue
                        candidates.add(listOf(intArrayOf(i, j, c.shiftIdx)))
                        sched[i][j] = c.shiftIdx
                        val chain = findCovUChain(p, sched, old, j, rng, exclude = i)
                        sched[i][j] = old
                        if (chain != null) candidates.add(listOf(intArrayOf(i, j, c.shiftIdx)) + chain)
                    }
                    if (candidates.isEmpty()) break
                    if (commitBestMove(state, sched, baseline, candidates) == null) break
                    applied++
                }
            }
        }
        return applied
    }

    /**
     * [3.233.0/ドッグフーディングで発見・covO(3.204.0)/c41,c41s(3.209.0)と同型の専用repair欠如]
     * c42(群ペア禁止: 群g1のs1×群g2のs2が同日に同時発生禁止)は`mark(i,j,"c42")`で
     * report.violations(セルマップ)には載るため destroyRepairViolations の汎用ランダム再割当は
     * 一応届くが、covU/covO/c41のような「動かせるか(希望固定/禁止連続/被覆悪化を避ける)」を判定して
     * 実際に動かす専用オペレータが無かった。違反ペア(left∈g1×s1, right∈g2×s2)のどちらか一方を
     * 実際に他シフトへ動かして崩す。移動先でcovOが悪化しない候補を探し、離脱元でcovUが悪化するなら
     * findCovUChainで玉突きフォールバック（c41Free(3.209.0)で判明済みの罠=「離脱を先にschedへ適用して
     * からfindCovUChainを呼ぶ」順序を踏襲。逆順だと本人がまだ在籍中に見え常にnullが返る）。
     * skill=false は cons42(sgrp)、skill=true は cons42s(ssk) を対象にする（DRY化）。
     * sched を in-place 変更し適用手数を返す。最終採否は呼び出し側のkeep-best（ラウンドbetter()）が
     * 担保＝退化不能。
     */
            // [3.313.0] 締切/キャンセル確認。これらは違反セル × 候補職員の二重ループの内側で
        //   フル checker（commitBestMove）と findCovUChain(BFS) を呼ぶ高コストパスで、旧実装は
        //   停止確認を一切持たなかった（3.161.0 で V6HotfixPasses の研磨パスへ入れた「内側ループ
        //   でも締切を見る」の対象漏れ）。既定 `{ false }` なので既存の直接呼出＝挙動不変。
internal fun applyC42Free(
        state: MagiState, sched: Array<IntArray>, rng: Random, skill: Boolean,
        shouldStop: () -> Boolean = { false },
    ): Int {
        val p = cachedProblem(state)
        if (p.S == 0 || p.T == 0) return 0
        val rules = if (skill) p.cons42s else p.cons42
        if (rules.isEmpty()) return 0
        val grp = if (skill) p.ssk else p.sgrp
        var applied = 0
        // [3.253.0, commitBestMoveへ全面移行] 詳細はcommitBestMove/applyC41Freeのdoc参照。
        //   違反ペアの片側(left=g1×s1 / right=g2×s2)それぞれについて、構造的に安全な直接移動・
        //   玉突き連鎖の両方の候補を集め、commitBestMoveが実チェッカーで全体評価する。
        fun gatherSide(candidates: List<Int>, j: Int, fromShift: Int, out: ArrayList<List<IntArray>>) {
            for (i in candidates) {
                // [3.391.0] 実現不能な希望は固定しない（wishLocked へ統一）。
                if (p.wishLocked(i, j) && p.wish[i][j] == fromShift) continue   // 実現可能な本人希望＝対象外
                for (m in p.allowedShiftsForStaff(i).filter { it != fromShift }) {
                    if (p.makesForbiddenRun(sched, i, j, m)) continue
                    out.add(listOf(intArrayOf(i, j, m)))
                    val oldK = sched[i][j]
                    sched[i][j] = m
                    val chain = findCovUChain(p, sched, fromShift, j, rng, exclude = i)
                    sched[i][j] = oldK
                    if (chain != null) out.add(listOf(intArrayOf(i, j, m)) + chain)
                }
            }
        }
        for (c in rules) {
            if (shouldStop()) return applied
            for (j in 0 until p.T) {
                if (shouldStop()) return applied
                while (true) {
                    val left = (0 until p.S).filter { grp[it] == c.g1 && sched[it][j] == c.s1 }
                    val right = (0 until p.S).filter { grp[it] == c.g2 && sched[it][j] == c.s2 }
                    if (left.isEmpty() || right.isEmpty()) break   // ペアが存在しない＝この日は解消済み
                    val baseline = UnifiedViolationChecker.check(state, sched)
                    val candidates = ArrayList<List<IntArray>>()
                    gatherSide(left, j, c.s1, candidates)
                    gatherSide(right, j, c.s2, candidates)
                    if (candidates.isEmpty()) break
                    if (commitBestMove(state, sched, baseline, candidates) == null) break
                    applied++
                }
            }
        }
        return applied
    }

    internal fun maxViolatedFamily(report: ViolationReport, avoid: Set<String> = emptySet(), round: Int = -1, roundsTotal: Int = -1): String {
        // [実機ログ起因=公平化のズレ] apt(適切回数)を追加。旧orderに無かったため RSI 探索中は一度も
        //   focus されず、post-processing(applyDayAssignmentPolish)頼みで広く未研磨のまま残っていた
        //   （実データ検証: apt L1偏差合計37、staffRange低/高はわずか3で規模が逆転）。rsiGenerateHypothesis
        //   側は既存の destroyRepairStaff(low/high/c2 と同経路、marginal costに apt 込み)へ合流するだけ＝
        //   新規オペレータ不要。
        // [同根の穴=weekly/fair] 同じ理由で weekly/fair も未focusだったため追加（実データ検証: weekly=65
        //   （aptの37より大きい）・fair=11）。destroyRepairStaff は weekly/fair の cost には未対応だが、
        //   専用ラウンドを割り当てるだけで無指向な"total"空振りより改善機会が増える（詳細はrsiGenerateHypothesis）。
        val order = listOf("groupViol", "covU", "pref", "c3n", "low", "high", "c41", "c41s", "c2", "covO", "c42", "c42s", "apt", "weekly", "fair", "c1", "c3", "c3m", "c3mn")
        // [D1/A1] 解ける HARD 族(groupViol/covU/pref/c3n)は件数に関わらず SOFT より先に focus する。
        //   旧実装は純・件数最大だったため、単一の c3n=1 が c1=118 等の高頻度 SOFT に埋もれ RSI が一度も HARD を
        //   狙わない失敗があった。目的関数 better() は辞書式(hard<<total<<weighted)で HARD 支配ゆえ focus も HARD
        //   優先が整合。avoid(HF63=構造的に充足困難)に入る HARD は「解けない」ため除外し無駄打ちを避ける(残予算は
        //   下段の SOFT 研磨へ)。この分岐は hard=0 のとき no-op＝全 soft の一般ケースは従来と不変。
        for (key in order) {
            if (key !in MirrorKeys.hard || key in avoid) continue
            if ((report.breakdown[key] ?: 0) > 0) return key
        }
        // [3.204.0/実機ログ起因=covOが「件数最大」選択に構造的に勝てない] covO は日×シフトのセル単独違反
        //   （新設したCoverageDiag診断＝V6PortAnalyzer.diagnoseCoverage の covO 版で判明）のため件数が常に
        //   一桁台に留まり、c1/c42/c3mn/weekly のような数十件規模の族に下段の「件数最大」選択で恒久的に
        //   絶対勝てない（実機ログで「動かせる」と診断されたcovOセルが300秒経っても解消されないことを確認）。
        //   HARDの「件数に関わらず先に狙う」と同じ発想で、covO専用に周期的な保証枠(3ラウンドに1回)を設け、
        //   count>0かつavoid対象でなければ下段の最大値選択より優先する。他のSOFT族の選択順は完全に不変
        //   （round<0=呼出元が未対応の旧経路 or この分岐に該当しないラウンドは従来どおり件数最大へフォールバック）。
        // [3.207.0/実機ログで判明した3.204.0の実効性不足] 典型的な5ラウンドRSIでは round%3==2 の唯一の
        //   該当ラウンド(0始まりで2番目)が、HF63がc3n/covUをdeprioritizeし終える前(HF63は約3ラウンドの
        //   停滞を要する)に来てしまい、HARD優先ループがそのラウンドを丸ごと消費して covO 分岐へ到達しない
        //   （実機ログ: round=3/5 focus=c3n、covOは合計6のまま最後まで不変）。HARDが本当に解けない場合は
        //   HF63が最終的にdeprioritizeし尽くすため、**RSIフェーズの最終ラウンドでは高確率でavoidが揃っている**
        //   （実機ログでもround=5/5時点でc3n,covU,c1(E9冷却)が全てavoid/cooldown済）。最終ラウンドも
        //   保証枠に加え、周期枠が典型的な短いRSIフェーズで丸ごと空振りする問題を解消する
        //   （roundsTotal<0=呼出元が未対応なら従来どおり無効化＝後方互換）。
        val finalRound = roundsTotal > 0 && round == roundsTotal - 1
        // [3.208.0/実機ログで判明したaptの同型の恒久的starvation] 提供された全ログ(7本)を確認したところ、
        //   apt は常に breakdown 最小級（1または11、他族(c1=87/c42=18/weekly=56等)の一桁〜二桁下）で、
        //   "focus=apt" は一度も出現しなかった（"focus=weekly" のみが件数最大フォールバックで選ばれ続ける）。
        //   apt は 3.169.0 で正に「focus されず未研磨」を解消する狙いで order に追加されたが、追加した
        //   だけでは件数最大選択に構造的に勝てないという covO と全く同じ欠陥を抱えていた（3.169.0時点の
        //   検証データではapt=37とcovOより大きく問題が露呈しなかったが、実運用データでは apt が最小級に
        //   落ち着くことが多いと判明）。covOとは別の周期(round%3==1、covOの%3==2と衝突しない)を割当てる。
        // [3.239.0/実機ログで判明した最終ラウンド枠の固定順バグ] 旧実装は最終ラウンドで常に
        //   「aptを先にチェック（covOより小さく恒常的に不利なため優先）」という固定順だった。これは
        //   「aptは常にcovOより小さい」という3.208.0時点の観測（7本のログ全てでapt<covO）に基づく前提
        //   だったが、この前提自体がデータ依存で普遍的ではない（実機ログで apt=29 > covO=4 という逆転を
        //   確認。5ラウンドRSI中、covUがHARDとして数ラウンド粘り+周期枠(round%3==2)もHARD優先ループに
        //   食われ、最終ラウンドはfinalRound分岐に到達するがaptが先にreturnするためcovOには一度も
        //   到達しなかった＝8/26のcovO過剰1が「動かせる」診断なのに300秒経っても未解消だった根本原因の
        //   一つ）。最終ラウンドで両方が候補になる場合のみ、実際の件数を比較し「より少ない方
        //   （より構造的に不利＝件数最大選択に絶対勝てない方）」を優先する。通常ラウンド(round%3==1/2の
        //   単独枠)は従来どおり衝突しないため無変更。
        val aptEligible = round >= 0 && "apt" !in avoid && (report.breakdown["apt"] ?: 0) > 0 && (round % 3 == 1 || finalRound)
        val covOEligible = round >= 0 && "covO" !in avoid && (report.breakdown["covO"] ?: 0) > 0 && (round % 3 == 2 || finalRound)
        if (aptEligible && covOEligible) {
            return if ((report.breakdown["covO"] ?: 0) <= (report.breakdown["apt"] ?: 0)) "covO" else "apt"
        }
        if (aptEligible) return "apt"
        if (covOEligible) return "covO"
        // 解ける HARD が無い(全て 0 か avoid)＝以降は SOFT。従来どおり非avoidの族から件数最大を返す。
        // [E8/実機ログ起因] 件数0の族は focus しない（旧: bestCount=-1 初期化のため、非avoidの正件数族が
        //   order に1つも無いと先頭 groupViol=0 が「0 > -1」で当選→hf67ルートがクリーン盤面への no-op 仮説
        //   ＝1ラウンド(実測~21s)空振りしていた。12シフト実機ログ round=4/5 focus=groupViol(件数0)で確認）。
        //   該当なしは "total" を返し、rsiGenerateHypothesis の else 分岐＝全違反セル hint の汎用修復
        //   (destroyRepairViolations, focus 非依存)ラウンドとして時間を有効化する。focus 選択のみ＝スコアリング不変。
        var best = "total"
        var bestCount = 0
        for (key in order) {
            if (key in avoid) continue
            val n = report.breakdown[key] ?: 0
            if (n > bestCount) {
                bestCount = n
                best = key
            }
        }
        // [ユーザー明示指示(2026-07-20)「weeklyをaptより優先順位を下げる」] weekly は件数が大きくなりやすく
        //   (実機ログで41〜65)、apt(同1〜29)より小さくても件数最大選択で恒常的に勝ってしまっていた。
        //   件数比較でweeklyが選ばれた場合でも、aptに残り(avoid対象でなければ)があれば apt を優先する
        //   （apt自身の周期枠=aptEligibleが不発だったラウンドのみ到達＝この分岐で初めて効く）。
        //   apt/weekly以外の族どうしの順位（c1/c3/fair等）は無変更。
        if (best == "weekly" && "apt" !in avoid && (report.breakdown["apt"] ?: 0) > 0) {
            best = "apt"
        }
        return best
    }

    // [3.287.0 keep-best統一] hard→weightedScore→total（単一ソース betterReport へ委譲。MirrorCore.kt 参照）。
    private fun better(a: ViolationReport, b: ViolationReport): Boolean = betterReport(a, b)

    /**
     * [品質向上] エリート解の Path Relinking（Glover, Laguna & Martí 2000 / Scatter Search）。
     * 並列ポートフォリオが保持する精鋭解（[lastAlternatives]）と現行最良を「再結合」し、両者の中間に
     * しばしば存在する、どの単独軌道でも届かない良解を拾う。best を起点に各 alt へ強制マーチ（差分セルを
     * alt 値へ順次適用）し、経路上の最良中間解を保持。常に best 起点から評価するので**退化しない**。
     * 早期停止で空いた予算を、頭打ちした同種探索ではなく「別種の探索」に充てて品質を底上げする。
     */
    fun elitePathRelink(
        state: MagiState,
        best: Array<IntArray>,
        alternatives: List<Array<IntArray>>,
        shouldStop: () -> Boolean,
    ): Pair<Array<IntArray>, ViolationReport> {
        var bestSched = best.copy2D()
        var bestRep = UnifiedViolationChecker.check(state, bestSched)
        if (alternatives.isEmpty()) return bestSched to bestRep
        for (alt in alternatives) {
            if (shouldStop()) break
            val cur = bestSched.copy2D()              // 常に現行最良から再結合（中間最良は別管理＝退化なし）
            var curRep = UnifiedViolationChecker.check(state, cur)
            val diffs = ArrayList<Pair<Int, Int>>()
            for (i in cur.indices) for (j in cur[i].indices) {
                if (i < alt.size && j < alt[i].size && cur[i][j] != alt[i][j]) diffs.add(i to j)
            }
            if (diffs.isEmpty()) continue
            // 違反セルを先に動かす（インパクト大の組み替えを前倒し）。
            val vcells = HashSet<Pair<Int, Int>>()
            for (vkey in curRep.violations.keys) {
                val parts = vkey.split(',')
                val ci = parts.getOrNull(0)?.toIntOrNull(); val cj = parts.getOrNull(1)?.toIntOrNull()
                if (ci != null && cj != null) vcells.add(ci to cj)
            }
            diffs.sortBy { if (it in vcells) 0 else 1 }
            for ((i, j) in diffs) {
                if (shouldStop()) break
                cur[i][j] = alt[i][j]                 // alt へ向けた強制マーチ
                curRep = UnifiedViolationChecker.check(state, cur)
                if (better(curRep, bestRep)) { bestSched = cur.copy2D(); bestRep = curRep }
            }
        }
        return bestSched to bestRep
    }

    private fun nowMs(): Long = System.nanoTime() / 1_000_000L
    private fun actualSeed(seed: Long): Long = if (seed == 0L) System.nanoTime() else seed
}
