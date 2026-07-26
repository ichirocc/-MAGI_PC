package com.magi.app.v6

import com.magi.app.model.MagiState
import java.util.Random
import kotlin.math.max
import kotlin.math.min

/**
 * Native replacements for the Web-only post-optimization hotfix modules.
 *
 * The Web V6 calls HF80 -> HF67 -> HF66 -> HF70 after each optimizer run from
 * inside App.handleOptimize().  Android does not have window.HFxx modules, so the
 * passes live here as pure Kotlin and can be called from ViewModel/tests.
 */
data class HF80Result(
    val newSchedule: Array<IntArray>,
    val beforeHard: Int,
    val afterHard: Int,
    val beforeScore: Double,
    val afterScore: Double,
    val cycles: Int,
    val applied: Boolean,
    val reason: String,
    val logs: List<MirrorLog>,
)

data class HF67Result(
    val newSchedule: Array<IntArray>,
    val beforeTotal: Int,
    val afterTotal: Int,
    val swapsApplied: Int,
    val shortageSwaps: Int,
    val capacitySwaps: Int,
    val swapsRollback: Int,
    val logs: List<MirrorLog>,
)

data class HF66Result(
    val newSchedule: Array<IntArray>,
    val beforeTotal: Int,
    val afterTotal: Int,
    val movesApplied: Int,
    val shortageMoves: Int,
    val capacityMoves: Int,
    val movesRollback: Int,
    val logs: List<MirrorLog>,
)

data class HF70Result(
    val anomalies: Int,
    val message: String,
    val advice: String,
    val logs: List<MirrorLog>,
)

data class V6PostOptimizationResult(
    val schedule: Array<IntArray>,
    val report: ViolationReport,
    val hf80: HF80Result,
    val hf67: HF67Result,
    val hf66: HF66Result,
    val hf70: HF70Result,
    val logs: List<MirrorLog>,
)

object V6HotfixPasses {
    /**
     * [頭打ち調査・「なぜゼロにならないのか」] C1Polish/C3mnPolish/RangePolish/C3RunPolish は
     * `runPostOptimization`のフィックスポイント巡回(最大maxRounds=4)から**ラウンドごとに再呼出**
     * されるが、旧実装はseed引数を渡さず既定値固定のままだった。findCovUChainの候補順はrng由来
     * なので、ある(staff,shift)ペアがラウンドNで頭打ち(候補が構造的に全滅/isBetterに拒否)すると、
     * 盤面の当該箇所が変化しない限りラウンドN+1以降も**全く同じrng列＝同じ結果**を再生するだけで、
     * 永久に頭打ちのまま抜け出せなかった（桒澤美幸のAｱ超過が段階的にしか縮まらない実例で発覚）。
     * ラウンドごとに異なるseedを与え、再挑戦のたびに違う候補順を試せるようにする（isBetterによる
     * keep-best採否は不変＝退化不能。単なる探索の多様化）。
     */
    internal fun roundSeed(base: Long, tag: Long, round: Int) = base xor tag xor (round.toLong() * -0x61c8864680b583ebL)

    private const val DAY_MATCH_INF = 1_000_000_000_000L

    /**
     * 正方コスト行列の最小費用完全割当（Hungarian法、O(n^3)）。
     * 戻り値[row] = 採用した column。到達不能辺は [DAY_MATCH_INF]。
     *
     * RangePolishの日単位再割当で、現在日のシフト多重集合を一切変えずに、
     * 「誰がどのシフトを担当するか」だけを全員同時に最適化するために使う。
     */
    private fun minCostPerfectAssignment(
        cost: Array<LongArray>,
        inf: Long = DAY_MATCH_INF,
    ): IntArray? {
        val n = cost.size
        if (n == 0 || cost.any { it.size != n }) return null
        val u = LongArray(n + 1)
        val v = LongArray(n + 1)
        val p = IntArray(n + 1)
        val way = IntArray(n + 1)

        for (i in 1..n) {
            p[0] = i
            var j0 = 0
            val minv = LongArray(n + 1) { inf }
            val used = BooleanArray(n + 1)
            do {
                used[j0] = true
                val i0 = p[j0]
                var delta = inf
                var j1 = -1
                for (j in 1..n) {
                    if (used[j]) continue
                    val raw = cost[i0 - 1][j - 1]
                    if (raw < inf / 2) {
                        val cur = raw - u[i0] - v[j]
                        if (cur < minv[j]) {
                            minv[j] = cur
                            way[j] = j0
                        }
                    }
                    // raw が到達不能でも、別の交互木ノードから既に入った minv は比較対象。
                    if (minv[j] < delta) {
                        delta = minv[j]
                        j1 = j
                    }
                }
                if (j1 < 0 || delta >= inf / 2) return null
                for (j in 0..n) {
                    if (used[j]) {
                        u[p[j]] += delta
                        v[j] -= delta
                    } else if (j > 0 && minv[j] < inf / 2) {
                        minv[j] -= delta
                    }
                }
                j0 = j1
            } while (p[j0] != 0)

            do {
                val j1 = way[j0]
                p[j0] = p[j1]
                j0 = j1
            } while (j0 != 0)
        }

        val out = IntArray(n) { -1 }
        for (j in 1..n) if (p[j] > 0) out[p[j] - 1] = j - 1
        if (out.any { it < 0 }) return null
        for (i in 0 until n) if (cost[i][out[i]] >= inf / 2) return null
        return out
    }

    /**
     * [review: budget] 後処理チェーン HF80 -> HF67 -> HF66 -> HF70。
     * @param shouldStop true を返した時点で各パスの反復を打ち切る。全体予算(deadline)超過と
     *        coroutine キャンセルの両方を呼び出し側でこのラムダに束ねる。HF80/67/66 は
     *        deadline で短縮/打ち切り、HF70(異常検知=安価)は診断のため常に実行する。
     * @param onPhase 各パス開始時に呼ばれ、UI 進捗を後処理中も更新できる(ハング誤認の防止)。
     */
    fun runPostOptimization(
        state: MagiState,
        schedule: Array<IntArray>,
        algoName: String,
        seed: Long = System.nanoTime(),
        shouldStop: () -> Boolean = { false },
        onPhase: (String) -> Unit = {},
        deadlineMs: Long = Long.MAX_VALUE,
    ): V6PostOptimizationResult {
        var work = schedule.copy2D()
        val logs = ArrayList<MirrorLog>()
        val t0 = System.currentTimeMillis()

        onPhase("後処理 HF80 戦略的振動")
        val t80 = System.currentTimeMillis()
        val r80 = applyHF80StrategicOscillation(state, work, maxCycles = 3, seed = seed xor 0x80L, shouldStop = shouldStop)
        work = r80.newSchedule.copy2D()
        logs.addAll(r80.logs)

        onPhase("後処理 HF67 職員間スワップ")
        val t67 = System.currentTimeMillis()
        // [3.282.0] HF66 と同型の専用上限（残り予算の半分・絶対上限3s）。実機実測は数十ms＝通常は無影響で、
        //   大規模データでのフォールバック総当たり暴走だけを防ぐ保険。
        val hf67Cap = ((deadlineMs - t67).coerceAtLeast(0L) / 2).coerceAtMost(3_000L)
        val r67 = applyHF67InterStaffSwap(state, work, maxSwaps = 30, shouldStop = shouldStop, deadlineMs = t67 + hf67Cap)
        work = r67.newSchedule.copy2D()
        logs.addAll(r67.logs)

        onPhase("後処理 HF66 職員内再配分")
        val t66 = System.currentTimeMillis()
        // [残予算ガード] HF66 は手ごとに全候補をフル check する高コストパス。残予算の半分まで(残り半分を
        //   後段の研磨群へ確保)＋絶対上限6sで打ち切り、暴走で後続パスを予算超過で打ち切らせない。
        val hf66Cap = ((deadlineMs - t66).coerceAtLeast(0L) / 2).coerceAtMost(6_000L)
        val r66 = applyHF66IntraStaffRedistribution(state, work, maxMoves = 30, shouldStop = shouldStop, deadlineMs = t66 + hf66Cap)
        work = r66.newSchedule.copy2D()
        logs.addAll(r66.logs)
        val t66Done = System.currentTimeMillis()

        // [3.271.0, 実機ログ2本連続で実証された飢餓の解消] 巡回研磨クラスタ（厳密日割当〜曜日平準化）は
        //   自身の締切を持たず shouldStop（全体予算）だけで走るため、探索フェーズが予算を使い切る実運用
        //   では後処理予約枠(8〜25s)を丸ごと消費し、後段の C1共同LNS/個人共同LNS が毎回「探索上限0=
        //   明示的に無効」でスキップされていた（両パスは実データで HARD削減の実績があるのに本番では
        //   一度も走れない＝事実上の死に機能）。HF66 の予算按分と同じ考え方で、クラスタ開始時点の
        //   残予算の半分（上限14s=両LNSの既定合計 8s+6s）を共同LNS用に確保し、クラスタには
        //   clusterStop（自前の締切つき）を渡す。クラスタが早期にフィックスポイント到達すれば共同LNSは
        //   確保分より多く使える（従来挙動と同一）。全パス keep-best のため時間配分の変更のみ＝退化不能。
        val jointLnsReserve = if (deadlineMs == Long.MAX_VALUE) 0L
            else ((deadlineMs - t66Done).coerceAtLeast(0L) / 2).coerceAtMost(14_000L)
        val clusterDeadline = if (deadlineMs == Long.MAX_VALUE) Long.MAX_VALUE else deadlineMs - jointLnsReserve
        val clusterStop: () -> Boolean = { shouldStop() || System.currentTimeMillis() >= clusterDeadline }

        onPhase("後処理 厳密日割当")
        val rAsg = applyDayAssignmentPolish(state, work, shouldStop = clusterStop)
        work = rAsg.newSchedule.copy2D()
        logs.addAll(rAsg.logs)

        // [研磨可否の検証] ソフト研磨クラスタ(循環 / c1 / c1回転 / c3 / c3回転)の前後を測る基準。
        val preSoftRep = UnifiedViolationChecker.check(state, work)

        // [パス間フィックスポイント再ループ] 各パスは内部で自己収束するが、別パスの変更が他パスの改善を
        //   再び開く（例: c3の組替えで新たなc1充足余地が出る）。クラスタ全体を「1巡で1手も採用されなく
        //   なるまで」最大 maxRounds 巡だけ繰り返す。全パスkeep-best＝退化なし。shouldStop と maxRounds で
        //   上限。違反セル指向なので空巡は即終了（コスト0）。
        val c3Anchor = setOf("vio-c3", "vio-c3m", "vio-c3mn")
        val maxRounds = 4
        // [C1RepairIndex / 3.275.0] c1不足窓の索引用 Problem（state の純関数＝巡回間で不変。各オペレータが
        //   内部で構築する Problem(state) と同一）。C1DeltaPrefilter のクラスタ前段ゲートに使う。
        val pC1 = Problem(state)
        var round = 0
        var totalCyc = 0; var totalC1 = 0; var totalC3 = 0; var totalC3r = 0; var totalC3mn = 0; var totalRange = 0; var totalC3run = 0; var totalC3pat = 0; var totalBlockSwap = 0; var totalApt = 0; var totalFair = 0
        while (round < maxRounds && !clusterStop()) {
            var roundApplied = 0

            onPhase("後処理 循環交換(k=2,3) [巡${round + 1}]")
            val rCyc = applyCyclicSwapPolish(state, work, maxPasses = 4, shouldStop = clusterStop)
            work = rCyc.newSchedule.copy2D(); totalCyc += rCyc.applied; roundApplied += rCyc.applied
            if (round == 0) logs.addAll(rCyc.logs)

            // [C1RepairOperators façade / 3.275.0] 散在していた C1 オペレータを図の1層へ集約（1:1委譲＝挙動同一）。
            //   自己内移設+同日swap(applyC1WindowPolish)は c1違反セルに**厳密アンカー**する＝不足窓ゼロなら必ず
            //   no-op。C1DeltaPrefilter で不足窓の有無を1回判定し、無ければ本オペレータのみ安全にスキップする
            //   （Index/Prefilter を hot path で実際に使う唯一の provably-safe な地点）。他3op(temporalFlow/
            //   wideBeam/exact)は c1中立の total改善手を出し得る／独自の内部ゲートを持つため gate せず従来どおり実行。
            val c1Index = C1RepairIndex.build(pC1, work)
            if (C1DeltaPrefilter.hasActionableC1(c1Index)) {
                onPhase("後処理 期間要件(c1)研磨 [巡${round + 1}]")
                val rC1 = C1RepairOperators.selfRelocateAndSameDaySwap(state, work, maxPasses = 3, shouldStop = clusterStop, seed = roundSeed(seed, 0x1C1L, round))
                work = rC1.newSchedule.copy2D(); totalC1 += rC1.applied; roundApplied += rC1.applied
                if (round == 0) logs.addAll(rC1.logs)

                // [C1IndexRepair / 3.276.0] index駆動の候補生成＋prefilter選別＋玉突き連鎖。C1RepairIndex/
                //   C1DeltaPrefilter を実駆動する経路。厳密c1アンカー＝不足窓ゼロで no-op のため本ゲート内に配置。
                //   生成する手は既存手B/beam/exactと重複しうるが keep-best で無害（退化不能）。
                onPhase("後処理 期間要件(c1)index駆動修復 [巡${round + 1}]")
                val rC1idx = C1RepairOperators.indexChainRepair(state, work, shouldStop = clusterStop, seed = roundSeed(seed, 0x1C1D2L, round))
                work = rC1idx.newSchedule.copy2D(); totalC1 += rC1idx.applied; roundApplied += rC1idx.applied
                if (round == 0) logs.addAll(rC1idx.logs)
            }

            // [3.254.0/C1TemporalFlowPolish, C1時系列DP+ジョイント再割当研磨=旧C1TemporalSwapPolish/
            // C1Rotate/BeamC1PolishV2 を置換] ユーザー指摘「applyC1WindowPolish(単一職員局所)・
            // applyC1BeamPolish(広域ビーム)・BeamC1PolishV2(同日swap束)・CombinatorialRepair の
            // 責任を整理し統合してほしい」に対する実測駆動の回答。ホストJVM実行で golden_state.json/
            // sample_state_v6.json に対しablation測定した結果:
            //  - 旧`C1TemporalSwapPolish`(DP+同日2人swap限定の実現)は単体でも他パスと組み合わせても
            //    寄与ゼロ(golden: DP単体0.0%改善、Window+DP+Rotateは Window単体と完全一致)。
            //    原因はDPが選ぶ目標パターンを「厳密に相補的なシフトを持つ同日1人との交換」でしか
            //    実現できず、そのような相手が存在しない日ではDPの改善が丸ごと死ぬため。
            //  - `applyBlockRotationPolish(c1Anchor)`(3者回転)も同様に寄与ゼロ(no-Rotateの結果が
            //    ALL5と完全一致)。
            //  - `BeamC1PolishV2`(同日swap束)も寄与ゼロ(no-BeamV2の結果がALL5と完全一致。3.252.0の
            //    実機ログでの「採用0/頭打ち」が本番ログ限定でなく実データでも構造的と確認)。
            // → 3者とも撤去し、DPの実現ステップを`FlexibleDayFlow`(3.245.0既存の同日全員参加min-cost
            //   flow)による同日ジョイント再割当へ置換した`C1TemporalFlowPolish`に一本化。実測:
            //   golden_state.json で c1 115→79(旧ALL5比 92→79 でさらに改善)・total 313→260
            //   (Window+Flow+BeamWideの順、旧ALL5の274より改善)。sample_state_v6.json で
            //   c1 7→2(71.4%改善、HARDも15→10へ同時改善)。順序が重要(Flow は BeamWide の**前**に
            //   置く。逆順だと golden で 278 止まりに劣化することを実測確認済み)。
            // CombinatorialRepair(3.249.0)はC1Window/C3mn/Range/Apt/Fairの内部augmentationで
            // C1系の別パスではないため対象外(廃止候補にはしない)。
            onPhase("後処理 期間要件(c1)時系列DP+ジョイント再割当研磨 [巡${round + 1}]")
            val rC1flow = C1RepairOperators.temporalFlow(
                state, work, maxPasses = 2, maxRelocations = 4, trials = 4,
                shouldStop = clusterStop, seed = roundSeed(seed, 0xC1F10L, round),
            )
            work = rC1flow.newSchedule.copy2D(); totalC1 += rC1flow.applied; roundApplied += rC1flow.applied
            if (round == 0) logs.addAll(rC1flow.logs)

            // [C1BeamPolish, 外部パッチ受領→ランキング修正+keep-best安全網追加のうえ適用] BeamC1PolishV2
            // (厳密な単発bundle採否)とは別系統の、より広い時空間ビーム探索。実データ(golden_state.json/
            // sample_state_v6.json)の両方・全15シードでtotalが真に改善することを確認済み(applyC1BeamPolish
            // のdocを参照)。BeamC1PolishV2で見つからない残差にも届く可能性があるため直後に配線。
            onPhase("後処理 期間要件(c1)広域ビーム研磨 [巡${round + 1}]")
            val rC1wide = C1RepairOperators.wideBeam(state, work, shouldStop = clusterStop, seed = roundSeed(seed, 0xC1BEAL, round))
            work = rC1wide.newSchedule.copy2D(); totalC1 += rC1wide.applied; roundApplied += rC1wide.applied
            if (round == 0) logs.addAll(rC1wide.logs)

            // [A2/A3 厳密窓修復] 上記の局所/ビーム系が届かない「別日で連動して初めて解ける多職員手」を、
            //   窓スコープの coverage保存 permutation 厳密探索で拾う（純Kotlin・依存ゼロ）。A1=解析駆動
            //   ディスパッチ: 証明された解消不能スパン(exhaustive && min==base)を memo で二度解かない。
            onPhase("後処理 期間要件(c1)厳密窓修復 [巡${round + 1}]")
            val rC1exact = C1RepairOperators.exactWindow(state, work, shouldStop = clusterStop)
            work = rC1exact.newSchedule.copy2D(); totalC1 += rC1exact.applied; roundApplied += rC1exact.applied
            if (round == 0) logs.addAll(rC1exact.logs)

            onPhase("後処理 連続規則(c3系)研磨 [巡${round + 1}]")
            val rC3 = applyC3SequencePolish(state, work, maxPasses = 3, shouldStop = clusterStop)
            work = rC3.newSchedule.copy2D(); totalC3 += rC3.applied; roundApplied += rC3.applied
            if (round == 0) logs.addAll(rC3.logs)

            onPhase("後処理 連続規則(c3系)3者回転研磨 [巡${round + 1}]")
            val rC3r = applyBlockRotationPolish(state, work, c3Anchor, "C3Rotate", maxPasses = 2, shouldStop = clusterStop)
            work = rC3r.newSchedule.copy2D(); totalC3r += rC3r.applied; roundApplied += rC3r.applied
            if (round == 0) logs.addAll(rC3r.logs)

            // [C3mnPolish・玉突き連鎖の横展開] cons3n(HARD)で直接候補が全滅する局面向けに findCovUChain
            //   をc3mn(回避,SOFT)専用に反映（grilling 2026-07-19、金沢勇輝のDﾃ4連続実例）。
            onPhase("後処理 回避パターン(c3mn)玉突き研磨 [巡${round + 1}]")
            val rC3mn = applyC3mnPolish(state, work, maxPasses = 3, shouldStop = clusterStop, seed = roundSeed(seed, 0xC3AL, round))
            work = rC3mn.newSchedule.copy2D(); totalC3mn += rC3mn.applied; roundApplied += rC3mn.applied
            if (round == 0) logs.addAll(rC3mn.logs)

            // [RangePolish・玉突き連鎖の横展開その2] 個人別回数(low/high)を、交換相手が構造的に存在しない
            //   局面(担当可能シフトが極端に少ない職員等)向けに findCovUChain で研磨（grilling不要・
            //   C3mnPolishと同型のためユーザー承認のうえ直接実装、桒澤美幸のAｱ超過実例）。
            onPhase("後処理 個人回数(low/high)玉突き研磨 [巡${round + 1}]")
            val rRange = applyRangePolish(state, work, maxPasses = 3, shouldStop = clusterStop, seed = roundSeed(seed, 0x8A9EL, round))
            work = rRange.newSchedule.copy2D(); totalRange += rRange.applied; roundApplied += rRange.applied
            if (round == 0) logs.addAll(rRange.logs)

            // [C3RunPolish・玉突き連鎖の横展開その3] cons3/cons3m(単一シフト連=run-deficit)を、
            //   相互交換の相手が構造的に存在しない局面向けに findCovUChain で研磨（grilling不要・
            //   C3mnPolish/RangePolishと同型のためユーザー承認のうえ直接実装）。
            onPhase("後処理 連続規則(c3/c3m単一シフト連)玉突き研磨 [巡${round + 1}]")
            val rC3run = applyC3RunPolish(state, work, maxPasses = 3, shouldStop = clusterStop, seed = roundSeed(seed, 0xC3A2L, round))
            work = rC3run.newSchedule.copy2D(); totalC3run += rC3run.applied; roundApplied += rC3run.applied
            if (round == 0) logs.addAll(rC3run.logs)

            // [C3PatternPolish・玉突き連鎖の横展開その4] 複数シフトc3/c3mパターン(非single-shift)を、
            //   交換相手が構造的に存在しない局面向けに findCovUChain で研磨（棚卸し監査で発見、ユーザー承認）。
            onPhase("後処理 連続規則(c3/c3m複数シフトパターン)玉突き研磨 [巡${round + 1}]")
            val rC3pat = applyC3PatternPolish(state, work, maxPasses = 3, shouldStop = clusterStop, seed = roundSeed(seed, 0xC3B4L, round))
            work = rC3pat.newSchedule.copy2D(); totalC3pat += rC3pat.applied; roundApplied += rC3pat.applied
            if (round == 0) logs.addAll(rC3pat.logs)

            // [BlockSwapPolish・15日ブロック丸ごと2人交換] 同一担当グループの2人×15日ブロックを
            //   丸ごと入替える大きな手。1日単位の局所交換が踏めない改善（range/pref/apt/weekly が
            //   絡む多家族同時トレード）に到達し得る（grilling 2026-07-19、金沢/アリフの検討から）。
            onPhase("後処理 15日ブロック丸ごと交換研磨 [巡${round + 1}]")
            val rBlockSwap = applyBlockSwapPolish(state, work, blockLen = 15, maxPasses = 3, shouldStop = clusterStop)
            work = rBlockSwap.newSchedule.copy2D(); totalBlockSwap += rBlockSwap.applied; roundApplied += rBlockSwap.applied
            if (round == 0) logs.addAll(rBlockSwap.logs)

            // [AptPolish・適切回数(apt)専用研磨] 自己振替→同一グループ相互交換→玉突きチェーンの順で
            //   apt(重み1)違反を専用に研磨（grilling 2026-07-19、大島愛の休/Pｼ実例）。
            onPhase("後処理 適切回数(apt)研磨 [巡${round + 1}]")
            val rApt = applyAptPolish(state, work, maxPasses = 3, shouldStop = clusterStop, seed = roundSeed(seed, 0xA97L, round))
            work = rApt.newSchedule.copy2D(); totalApt += rApt.applied; roundApplied += rApt.applied
            if (round == 0) logs.addAll(rApt.logs)

            // [FairPolish・グループ内公平化(fair)専用研磨] 棚卸し(c42/c42s以外の「動かせるか」欠如監査)で
            //   発見。AptPolishと同型の3段構成（自己振替→同一グループ相互交換→玉突きチェーン）。
            onPhase("後処理 グループ内公平化(fair)玉突き研磨 [巡${round + 1}]")
            val rFair = applyFairPolish(state, work, maxPasses = 3, shouldStop = clusterStop, seed = roundSeed(seed, 0xFA12L, round))
            work = rFair.newSchedule.copy2D(); totalFair += rFair.applied; roundApplied += rFair.applied
            if (round == 0) logs.addAll(rFair.logs)

            round++
            if (roundApplied == 0) break   // この巡で1手も採用なし＝joint局所最適に到達
        }

        // [研磨可否の検証ログ] ソフトc3系3種(c3/c3m/c3mn)とc1の増減・採用数・HARD不変・巡回数を集約。
        // 採用0かつ対象>0なら「頭打ち(改善手なし=正常)」、対象0なら「対象なし」と明示。
        run {
            val softAfter = UnifiedViolationChecker.check(state, work)
            fun bd(r: ViolationReport, k: String) = r.breakdown[k] ?: 0
            val adopted = totalCyc + totalC1 + totalC3 + totalC3r + totalC3mn + totalRange + totalC3run + totalC3pat + totalBlockSwap + totalApt + totalFair
            // [3.278.0/監査修正] CyclicSwap の正当な対象族(c2/c41/c42/c41s/c42s/covO)も対象数に含める
            //   （旧: c42等のみ違反の盤面で採用0のとき誤って「対象なし」と表示していた）。
            val targets = bd(preSoftRep, "c1") + bd(preSoftRep, "c3") + bd(preSoftRep, "c3m") + bd(preSoftRep, "c3mn") +
                bd(preSoftRep, "low") + bd(preSoftRep, "high") + bd(preSoftRep, "apt") + bd(preSoftRep, "fair") +
                bd(preSoftRep, "c2") + bd(preSoftRep, "c41") + bd(preSoftRep, "c42") +
                bd(preSoftRep, "c41s") + bd(preSoftRep, "c42s") + bd(preSoftRep, "covO")
            val verdict = when {
                adopted > 0 -> "有効(採用${adopted}手)"
                targets == 0 -> "対象なし"
                else -> "頭打ち(採用0=改善手なし・正常)"
            }
            val hardNote = if (softAfter.hard == preSoftRep.hard) "不変" else "変化${preSoftRep.hard}->${softAfter.hard}!"
            logs.add(MirrorLog(tag = "SoftPolishVerify", message =
                // [3.271.0, 外部レビューの誤読対策] 各パスの個別ログ行は巡1のみ表示（4巡ぶんのスパム防止）
                //   だが、この集約行の増減・採用内訳は全巡合計。旧表記では「C1Polish 採用0なのにc1が
                //   65→57に減った＝責務逆転?」という誤読を実際に生んだため、表示仕様を行内に明記する。
                "ソフトc1/c3系研磨 可否=$verdict (${round}巡・各パス行は巡1のみ表示/本行は全巡合計) | c1 ${bd(preSoftRep, "c1")}->${bd(softAfter, "c1")}" +
                    " / c3 ${bd(preSoftRep, "c3")}->${bd(softAfter, "c3")}" +
                    " / c3m ${bd(preSoftRep, "c3m")}->${bd(softAfter, "c3m")}" +
                    " / c3mn ${bd(preSoftRep, "c3mn")}->${bd(softAfter, "c3mn")}" +
                    " / low ${bd(preSoftRep, "low")}->${bd(softAfter, "low")}" +
                    " / high ${bd(preSoftRep, "high")}->${bd(softAfter, "high")}" +
                    " / apt ${bd(preSoftRep, "apt")}->${bd(softAfter, "apt")}" +
                    " / fair ${bd(preSoftRep, "fair")}->${bd(softAfter, "fair")}" +
                    " | HARD $hardNote / total ${preSoftRep.total}->${softAfter.total}" +
                    " (採用内訳 循環:${totalCyc} c1:${totalC1} c3:${totalC3} c3回転:${totalC3r} c3mn玉突き:${totalC3mn} range玉突き:${totalRange} c3run玉突き:${totalC3run} c3pattern玉突き:${totalC3pat} ブロック交換:${totalBlockSwap} apt玉突き:${totalApt} fair玉突き:${totalFair})"))
        }

        // [weekly 研磨の穴を埋める] 曜日平準化(weekly)は同日2者スワップでは動かせない（勤務↔勤務は曜日別の
        //   勤務/休が不変）ため、被覆保存の2職員×2日 長方形交換で「過剰曜日→過少曜日」へ勤務を移す。実目的関数
        //   isBetter で採否＝退化なし。下の equalize 系(分散指標)より先に L1 指向のこのパスを走らせる。
        onPhase("後処理 曜日平準化(長方形交換)")
        val rWrb = applyWeeklyRebalancePolish(state, work, maxPasses = 2, shouldStop = clusterStop)
        work = rWrb.newSchedule.copy2D()
        logs.addAll(rWrb.logs)

        // [交互最適化(Alternating Optimization)] 長方形交換(クロス日)が届かない同日内の「休の割当先」を、日ブロック
        //   ごとの最小費用割当(Hungarian＝凸最適化)で weekly/range/apt 同時最適に再配置し、不動点まで巡回する。
        //   rectangle(クロス日)と AO(同日内)は相補的＝両方走らせて weekly の取りこぼしを二方向から詰める。keep-best。
        onPhase("後処理 交互最適化(日ブロック割当)")
        val rAlt = applyAlternatingSoftPolish(state, work, maxSweeps = 4, shouldStop = clusterStop)
        work = rAlt.newSchedule.copy2D()
        logs.addAll(rAlt.logs)

        onPhase("後処理 グループ内シフト回数の平準化")
        val rGeq = applyGroupShiftEqualizePolish(state, work, maxPasses = 2, shouldStop = clusterStop)
        work = rGeq.newSchedule.copy2D()
        logs.addAll(rGeq.logs)

        onPhase("後処理 7日周期(曜日)の平準化")
        val rWeq = applyWeeklyEqualizePolish(state, work, maxPasses = 2, shouldStop = clusterStop)
        work = rWeq.newSchedule.copy2D()
        logs.addAll(rWeq.logs)

        // [3.255.0/C1JointLnsPolish・PersonalBalanceJointLnsPolish, 受領・検証のうえ適用] ここまでの
        // 巡回研磨は各パスが候補を作った直後に正式目的関数で採否するため、C1改善や個人回数改善に伴う
        // coverage/range/c3系の副作用を別の手で相殺する前に候補を失うことがある。この2パスはdebt付き
        // beamで複数手を束ね、最終採用のみ正式順序(hard→total→weighted)のkeep-bestで判定する（中間ノードの
        // debtは探索のみに影響し退化不能）。ホストJVM実行でgolden_state.json/sample_state_v6.jsonに対し
        // 既存パイプライン適用後の追加効果を実測: golden_state.jsonでは両方とも0（既存パイプラインが
        // 既に汲み尽くし済み＝安全なno-op）、sample_state_v6.jsonではC1JointLnsPolishがHARD5→4（既存
        // パイプラインが見つけていなかったHARD削減）、PersonalBalanceJointLnsPolishが個人回数34→31
        // （total 196→195）を発見。実行コストが高い(既定8s/6s)ため巡回ループでなく最終1回のみ実行。
        // [予算按分, receiving-code-review→自己検証で訂正] 以前は各パスの既定Config(8s/6s)をそのまま
        //   使いshouldStopのみを渡していたため、外側deadlineMsの残りがそれより短くても内部deadlineは
        //   呼出時点から新規に8s/6s確保され、最大14秒ぶん外側締切を超過し得た。
        //   [訂正の経緯] 初版はHF66(187行)と同型の「残予算の半分を後段へ確保」を踏襲したが、HF66は
        //   後段に巡回ループ全体(多数のパス)を控えるのに対し、この2パスの後段はPersonalBalance
        //   JointLnsPolish単体(既定6s)+HF70(安価・常時実行)のみ＝文脈が異なり折半は不適切と判明。
        //   remaining=14000ms(=両者の既定合計値)ちょうどの境界で検算すると、折半案はC1に7000msしか
        //   与えず自身の既定8000msに届かず、Personalは残り7000msのうち自身の既定6000msしか使わず
        //   1000msが誰にも使われないまま終わる(半分確保がPersonalの実需要=6000msを知らずに一律確保
        //   するため)。既定比8:6の按分なら、この境界で双方とも過不足なく自身の既定を得られる。
        //   remainingは整数乗算オーバーフロー回避のため安全な上限(100秒、実運用の予算を大きく超える
        //   値)へ先にクランプしてから按分する。残0なら各パスのmaxMillis<=0ガードにより即スキップ
        //   (explicitly無効)される。
        onPhase("後処理 期間要件(c1)共同LNS")
        val tC1Lns = System.currentTimeMillis()
        val remainingForC1Lns = (deadlineMs - tC1Lns).coerceAtLeast(0L).coerceAtMost(100_000L)
        val c1LnsCap = (remainingForC1Lns * 8_000L / 14_000L).coerceAtMost(8_000L)
        val rC1Lns = C1RepairOperators.jointLns(
            state, work, config = C1JointLnsPolish.Config(maxMillis = c1LnsCap), shouldStop = shouldStop,
        )
        work = rC1Lns.newSchedule.copy2D()
        logs.addAll(rC1Lns.logs)

        onPhase("後処理 個人回数/適切回数 共同LNS")
        val tPersonalLns = System.currentTimeMillis()
        val personalLnsCap = (deadlineMs - tPersonalLns).coerceAtLeast(0L).coerceAtMost(6_000L)
        val rPersonalLns = PersonalBalanceJointLnsPolish.apply(
            state, work, config = PersonalBalanceJointLnsPolish.Config(maxMillis = personalLnsCap), shouldStop = shouldStop,
        )
        work = rPersonalLns.newSchedule.copy2D()
        logs.addAll(rPersonalLns.logs)

        val tHf = System.currentTimeMillis()
        if (shouldStop()) {
            // [3.278.0/文言修正] この時点で残るのは最終検査(HF70)のみ＝「残りパスの打ち切り」は各パス内部の
            //   shouldStop で既に済んでいる事実に合わせる。
            logs.add(MirrorLog(level = "W", tag = "POST", message = "予算超過のため後処理は締切で短縮されました(各パスは内部で打ち切り済み・以降は最終検査のみ)"))
        }

        onPhase("後処理 HF70 異常検知")
        val report = UnifiedViolationChecker.check(state, work)
        val r70 = detectHF70Anomalies(state, work, algoName, report)
        logs.addAll(r70.logs)

        val tEnd = System.currentTimeMillis()
        // [ログ精度修正] 旧表記は t66〜tHf の間(=HF66本体＋厳密日割当＋巡回研磨4巡＋曜日/交互研磨＋
        //   C1/個人共同LNS＝パイプライン成長で大半を占めるようになった区間)を丸ごと「HF66」と誤表示していた
        //   （HF66自身は t66+hf66Cap で内部上限≤6s に自己制限済みのため、実際にそれ以上かかっていたのは
        //   後続の巡回研磨クラスタ）。C1JointLNS/個人共同LNSが「探索上限0=明示的に無効」になる理由
        //   （＝ここまでの区間で後処理予算を使い切った）が読めるよう区間ごとに分割表示する。表示のみ・
        //   スコアリング不変。
        logs.add(MirrorLog(level = "I", tag = "POST",
            message = "後処理タイミング 総${tEnd - t0}ms: HF80=${t67 - t80}ms HF67=${t66 - t67}ms HF66=${t66Done - t66}ms" +
                " 巡回研磨(厳密日割当+c1/c3/range/apt/fair+曜日/交互)=${tC1Lns - t66Done}ms" +
                " C1共同LNS=${tPersonalLns - tC1Lns}ms 個人共同LNS=${tHf - tPersonalLns}ms" +
                // [3.278.0] 旧: 最終検査(フルcheck+HF70)が無区間で「区間合計 < 総」の不一致を生んでいた。
                " 最終検査+HF70=${tEnd - tHf}ms"))

        val allLogs = ArrayList<MirrorLog>()
        allLogs.addAll(logs)
        allLogs.addAll(report.logs)
        return V6PostOptimizationResult(work, report.copy(logs = allLogs), r80, r67, r66, r70, logs)
    }

    fun applyHF80StrategicOscillation(
        state: MagiState,
        schedule: Array<IntArray>,
        maxCycles: Int = 3,
        seed: Long = System.nanoTime(),
        shouldStop: () -> Boolean = { false },
    ): HF80Result {
        val p = Problem(state)
        val rng = Random(seed)
        val before = UnifiedViolationChecker.check(state, schedule)
        var best = normalizeSchedule(schedule, p)
        var bestReport = before
        var applied = false
        var usedCycles = 0
        val cycleMax = max(0, maxCycles)
        var cycle = 0
        while (cycle < cycleMax) {
            if (shouldStop()) break
            val cand = best.copy2D()
            val strength = max(1, (p.S * p.T * (0.03 + cycle * 0.02)).toInt())
            var t = 0
            while (t < strength) {
                if (p.S > 0 && p.T > 0) {
                    val i = rng.nextInt(p.S)
                    val j = rng.nextInt(p.T)
                    if (p.wish[i][j] < 0) {
                        val allowed = p.allowedShiftsForStaff(i)
                        if (allowed.isNotEmpty()) cand[i][j] = allowed[rng.nextInt(allowed.size)]
                    }
                }
                t++
            }
            val polished = localBestImprovement(state, cand, 250 + cycle * 120, rng, shouldStop)
            val rep = UnifiedViolationChecker.check(state, polished)
            usedCycles = cycle + 1
            if (isBetter(rep, bestReport)) {
                best = polished
                bestReport = rep
                applied = true
            }
            cycle++
        }
        val reason = if (applied) "strategic oscillation accepted" else "no improving oscillation"
        val logs = listOf(MirrorLog(tag = "HF80", message = "SO applied=$applied HARD ${before.hard}->${bestReport.hard} score ${before.weightedScore.toLong()}->${bestReport.weightedScore.toLong()} cycles=$usedCycles"))
        return HF80Result(best, before.hard, bestReport.hard, before.weightedScore, bestReport.weightedScore, usedCycles, applied, reason, logs)
    }

    /**
     * [A1+A2+A3, 3.273.0] C1厳密窓修復パス。C1RepairAnalysis の窓スコープ厳密探索（coverage保存
     * permutation の分枝限定）で、局所/ビーム系が届かない多日多職員連動手を拾う。
     *  - A1 解析駆動ディスパッチ: 「exhaustive で min==baseline」と証明されたスパンは、その (焦点職員,
     *    シフト, スパン内容ハッシュ) を memo し、内容が変わらない限り二度と厳密探索しない（死に候補の刈込）。
     *  - 採否は必ず本物の checker + isBetter + exactPinRegression（keep-best＝退化不能）。厳密探索が返す
     *    patch はあくまで候補（node予算超過時は best-effort＝多様化として安全）。
     */
    fun applyC1ExactWindowRepair(
        state: MagiState,
        schedule: Array<IntArray>,
        cfg: C1RepairAnalysis.Config = C1RepairAnalysis.Config(),
        shouldStop: () -> Boolean = { false },
    ): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        var solved = 0
        var provenWalls = 0
        if (p.cons1.isEmpty() || (before.breakdown["c1"] ?: 0) == 0) {
            return CyclicSwapResult(work, before.total, before.total, 0,
                listOf(MirrorLog(tag = "C1ExactRepair", message = "c1対象なし=スキップ")))
        }
        // [A1] 証明済み「解消不能スパン」のmemo（キー=焦点職員,シフト,スパン内容ハッシュ）。
        val deadSpans = HashSet<String>()
        fun spanKey(staff: Int, shift: Int, days: List<Int>): String {
            val sb = StringBuilder().append(staff).append('|').append(shift).append('|')
            for (d in days) for (i in 0 until p.S) sb.append(work[i][d]).append(',')
            return sb.toString()
        }
        // 焦点=(職員,シフト)ごとに1回だけ厳密探索（同一職員の多数窓は1スパンに束ねられる）。
        val seenFocus = HashSet<Long>()
        for (v in C1RepairAnalysis.analyze(p, work)) {
            if (shouldStop()) break
            val focus = v.staff.toLong() * 1000 + v.shift
            if (!seenFocus.add(focus)) continue
            val span = minOf(cfg.maxWindowDays, p.T)
            val startD = v.start.coerceAtMost(p.T - span).coerceAtLeast(0)
            val days = (startD until startD + span).toList()
            val key = spanKey(v.staff, v.shift, days)
            if (key in deadSpans) continue
            val res = C1RepairAnalysis.solveWindow(p, work, v, cfg)
            solved++
            if (res.patch == null) {
                // 改善候補なし。exhaustive なら「coverage保存では解消不能」と証明済み＝memo。
                if (res.exhaustive) { deadSpans.add(key); provenWalls++ }
                continue
            }
            val workBefore = work.copy2D()
            for (op in res.patch) work[op[0]][op[1]] = op[2]
            val rep = UnifiedViolationChecker.check(state, work)
            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBefore, work)) {
                bestRep = rep; applied++
            } else {
                for (mi in work.indices) work[mi] = workBefore[mi]
            }
        }
        val c1b = before.breakdown["c1"] ?: 0
        val c1a = bestRep.breakdown["c1"] ?: 0
        val logs = listOf(MirrorLog(tag = "C1ExactRepair",
            message = "期間要件(c1)厳密窓修復: c1 $c1b->$c1a / total ${before.total}->${bestRep.total} " +
                "HARD ${before.hard}->${bestRep.hard} 採用${applied}回 探索${solved}回 証明済み壁${provenWalls}件" +
                (if (applied == 0 && c1b > 0) " [頭打ち=改善手なし]" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    data class CyclicSwapResult(
        val newSchedule: Array<IntArray>,
        val beforeTotal: Int,
        val afterTotal: Int,
        val applied: Int,
        val logs: List<MirrorLog>,
    )

    /**
     * [C1 Index-driven Chain Repair / 3.276.0] C1RepairIndex/C1DeltaPrefilter を実際に駆動する新規C1修復
     * オペレータ（図の Index→Prefilter→Operators→checker 経路を end-to-end に通す）。
     *
     *  1. `C1RepairIndex.build` で不足窓を索引化（不足の重い窓から処理）。
     *  2. 窓内の候補日を `C1DeltaPrefilter.c1Delta`（exact net c1 delta）昇順で並べ、`screenCell` が NEUTRAL の候補だけ試す
     *     （無変化/groupViol/pref破り/c3n は checker が確実に却下＝事前に落とす）。
     *  3. 候補日を不足シフトへ直接移動。旧シフトを抜いて covU 穴が空くなら `findCovUChain`（exclude=本人）の
     *     玉突き連鎖で埋め直す（手B と同型）。
     *  4. 採否は必ず本物の `UnifiedViolationChecker` + `isBetter`（hard→total→weighted）+ `exactPinRegression`
     *     （3.256.0の厳密ピン保護）＝keep-best・退化不能。
     *
     * [位置づけ・正直な限界] 生成する手は既存の手B/beam/exact と重複する（keep-best で無害）。本オペレータの
     *   主眼は「index駆動の候補生成＋prefilter選別」という図の経路を load-bearing にすること。実C1削減の
     *   純増は限定的（残差は3.263.0で確認した構造的壁が支配的）。既存オペレータには一切触れない＝退化不能。
     */
    fun applyC1IndexChainRepair(
        state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 2,
        shouldStop: () -> Boolean = { false }, seed: Long = 0x1C1D2L,
    ): CyclicSwapResult {
        val p = Problem(state)
        var work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        if (p.cons1.isEmpty() || (before.breakdown["c1"] ?: 0) == 0) {
            return CyclicSwapResult(work, before.total, before.total, 0,
                listOf(MirrorLog(tag = "C1IndexRepair", message = "c1対象なし=スキップ")))
        }
        val rng = Random(seed)
        var bestRep = before
        var applied = 0
        var chainUsed = 0
        var screened = 0
        // [3.279.0/外部レビューC1-08] 旧: pass 開始時の index を採用後も走査し続け、解消済み窓の再処理と
        //   「採用で新たに生じた窓は次 pass まで不可視」の両方が起きていた（maxPasses=2 の有限 pass で
        //   改善を取りこぼす）。1手採用するたびに窓ループを抜け、最新盤面から Index を再構築する。
        //   終了保証は isBetter の厳密改善（hard→total→weighted 辞書式で単調減少）＋採用上限の安全弁。
        val maxAdoptions = maxPasses * 32
        var capHit = false
        while (!shouldStop()) {
            // [3.279.1/レビューnit] 採用上限は安全弁＝到達を黙って打ち切らずログへ明示する（silent cap 禁止）。
            if (applied >= maxAdoptions) { capHit = true; break }
            val index = C1RepairIndex.build(p, work)
            if (!index.hasActionable) break
            var adopted = false
            windowLoop@ for (w in index.windows.sortedByDescending { it.deficit }) {
                if (shouldStop()) break
                val staff = w.staff; val shift = w.shift
                val cands = (w.start until w.start + w.windowDays)
                    .filter { d ->
                        val neutral = C1DeltaPrefilter.screenCell(p, work, staff, d, shift) == C1DeltaPrefilter.Verdict.NEUTRAL
                        if (!neutral && work[staff][d] != shift) screened++
                        neutral
                    }
                    // [3.277.0] 順位付けを exact net c1 delta へ（旧: index.expectedGain=gainのみの近似）。
                    //   c1Delta は旧シフト除去で別窓を割る loss も勘定＝自己破壊候補を後回しにする賢い順序。
                    //   負=改善なので昇順（最も改善する候補を先に試す）。順位のみ＝keep-best採否は不変。
                    .sortedBy { d -> C1DeltaPrefilter.c1Delta(p, work, staff, d, shift) }
                for (d in cands) {
                    if (shouldStop()) break
                    val old = work[staff][d]
                    val trial = work.copy2D()
                    trial[staff][d] = shift
                    // (a) 直接移動のみで改善（旧シフトに余裕がある場合）。
                    val repDirect = UnifiedViolationChecker.check(state, trial)
                    if (isBetter(repDirect, bestRep) && !exactPinRegression(p, work, trial)) {
                        work = trial; bestRep = repDirect; applied++; adopted = true; break@windowLoop
                    }
                    // (b) 旧シフトを抜いて covU 穴が空くなら玉突き連鎖で埋め直す（exclude=本人で自己選択防止）。
                    val cntOldAfter = (0 until p.S).count { trial[it][d] == old }
                    if (old in 0 until p.K && p.covUCell(old, d, cntOldAfter) > 0) {
                        val chain = findCovUChain(p, trial, old, d, rng, exclude = staff)
                        if (chain != null) {
                            for (mv in chain) trial[mv[0]][mv[1]] = mv[2]
                            val repChain = UnifiedViolationChecker.check(state, trial)
                            if (isBetter(repChain, bestRep) && !exactPinRegression(p, work, trial)) {
                                work = trial; bestRep = repChain; applied++; chainUsed++; adopted = true; break@windowLoop
                            }
                        }
                    }
                }
            }
            if (!adopted) break
        }
        val c1After = bestRep.breakdown["c1"] ?: 0
        return CyclicSwapResult(
            work, before.total, bestRep.total, applied,
            // [3.279.1] screened は Index 再構築のたび同一候補を再判定し得る＝「延べ」件数（重複計上あり）。
            listOf(MirrorLog(tag = "C1IndexRepair",
                message = "index駆動C1修復: c1 ${before.breakdown["c1"] ?: 0}->$c1After 採用$applied(連鎖$chainUsed) " +
                    "prefilter除外(延べ)$screened" + (if (capHit) " 採用上限${maxAdoptions}到達=打ち切り" else ""))),
        )
    }

    /**
     * [ソフト研磨・T2] 被覆を保つ循環交換（k=2,3）研磨。各日の (日,シフト) 人数を保ったまま、職員の
     * シフトを **2職員スワップ / 3職員ローテーション** で組み替える。被覆は不変＝HARD充足を維持し、
     * 連続規則(c3/c3m) や希望・回数の相互作用を**実目的関数(UnifiedViolationChecker)で評価**して
     * 改善時のみ採用（keep-best＝退化なし）。日内Hungarian(range/apt最適)が触れない c3 を狙う。
     * 注: 提案サイクルは必ず実チェックで検証してから採用するため、サイクル生成が不完全でも悪化しない。
     */
    fun applyCyclicSwapPolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 4, shouldStop: () -> Boolean = { false }): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        // [監査で発見・3.270.0] p.wish[i][j]<0 は「希望が一切ない」判定で、実現不能な希望
        //   (canDo(i,wish)==false)まで動かせないと誤判定していた（3.183.0 LightMirrorOptimizer と
        //   同型のバグ）。実現不能な希望はpref計上上も定数=動かして良い＝canDoガード込みの
        //   wishLocked が正しい判定。安全側（isBetter/checkerが最終ゲート）で候補が広がるのみ。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            for (j in 0 until p.T) {
                if (shouldStop()) break
                // --- k=2: 2職員スワップ（同日・被覆不変）---
                for (a in 0 until p.S) {
                    // [監査(未レビュー領域再監査)] HF66(2.65.0)/BlockRotationPolish(3.84.0)と同型の予算超過対策。
                    //   旧: 日(j)ループ先頭のみで確認していたため、1日分のO(S^2)スキャンが締切後も走り切っていた。
                    if (shouldStop()) break
                    if (!movable(a, j)) continue
                    for (b in a + 1 until p.S) {
                        if (!movable(b, j)) continue
                        val sa = work[a][j]; val sb = work[b][j]
                        if (sa == sb || !p.canDo(a, sb) || !p.canDo(b, sa)) continue
                        // [厳密ピン保護] 異なるシフト同士の同日交換はa/bの自身のシフト回数を変えるため、
                        //   staffRange厳密ピン(lo==hi)を新たに崩す候補は不採用にする（keep-best/重み不変）。
                        val workBeforeSwap2 = work.copy2D()
                        work[a][j] = sb; work[b][j] = sa
                        val rep = UnifiedViolationChecker.check(state, work)
                        if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeSwap2, work)) { bestRep = rep; applied++; improved = true }
                        else { work[a][j] = sa; work[b][j] = sb }
                    }
                }
                // --- k=3: 3職員ローテーション（同日・被覆不変）---
                for (a in 0 until p.S) {
                    if (shouldStop()) break
                    if (!movable(a, j)) continue
                    for (b in a + 1 until p.S) {
                        if (!movable(b, j)) continue
                        for (c in b + 1 until p.S) {
                            if (!movable(c, j)) continue
                            if (shouldStop()) break
                            val sa = work[a][j]; val sb = work[b][j]; val sc = work[c][j]
                            if (sa == sb && sb == sc) continue
                            // a←sb, b←sc, c←sa（feasibleなら適用→評価→不採用なら巻き戻し）
                            if (p.canDo(a, sb) && p.canDo(b, sc) && p.canDo(c, sa)) {
                                val workBeforeRotate3 = work.copy2D()
                                work[a][j] = sb; work[b][j] = sc; work[c][j] = sa
                                val rep = UnifiedViolationChecker.check(state, work)
                                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRotate3, work)) { bestRep = rep; applied++; improved = true; continue }
                                work[a][j] = sa; work[b][j] = sb; work[c][j] = sc
                            }
                        }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val logs = listOf(MirrorLog(tag = "CyclicSwap",
            message = "循環交換(k=2,3)研磨: total ${before.total}->${bestRep.total} 採用${applied}回"))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [ソフト研磨・連続規則] c3(必須の並び)・c3m(推奨)・c3mn(回避)・c3n(禁止=HARD) はいずれも職員の
     * 連続日の並びで決まる。同日スワップ(循環交換)では1日しか変えられず多日パターンに届かないため、
     * 2職員 i,i' が 連続 W 日(W=2,3)を丸ごと交換する（各日の被覆＝人数が不変＝HARD維持）。両者の W日
     * パターンが入れ替わり、2〜3日にわたる並びを直せる。実目的関数で評価し改善時のみ採用（keep-best＝
     * 退化なし）。isBetter は HARD を最優先するため、c3n(禁止=HARD) の解消も同時に拾う。
     */
    fun applyC3SequencePolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        var skipped = 0     // [#5] 前フィルタでフル評価を省いた手数
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        val windows = intArrayOf(2, 3)   // 連続2日・3日（c3は最大5連日だが2-3日窓でほぼ捕捉）
        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            // [違反セル指向] c3系で違反している職員のみを起点に絞る。c3は職員ごと→2者交換で改善する手は
            //   必ず違反職員を含む＝取りこぼし無し(ロスレス)。空なら即終了でコスト0。
            // [実バグ修正/applyC1WindowPolishと同根] rep0.violations（1セル=最重1クラスのみ）だと、
            //   c3系のマーク位置に c3n(HARD) 等の更に重い違反も同居する場合、そのセルの分類が上書きされ
            //   "vio-c3/c3m/c3mn"が消える。該当職員の全マーク位置が同様にシャドーイングされていると
            //   anchorStaffから丸ごと漏れ、一度も研磨が試されない。cellFamilies（1セルの全クラス保持）
            //   に切替え、上書きされても検出できるようにする。起点が広がるだけの後方互換な修正。
            val rep0 = if (pass == 0) before else UnifiedViolationChecker.check(state, work)
            val anchorStaff = HashSet<Int>()
            for ((key, fams) in rep0.cellFamilies) {
                if (fams.any { it == "vio-c3" || it == "vio-c3m" || it == "vio-c3mn" }) anchorStaff.add(key.substringBefore(",").toIntOrNull() ?: continue)
            }
            if (anchorStaff.isEmpty()) break
            for (w in windows) {
                if (p.T < w) continue
                for (j in 0..p.T - w) {
                    if (shouldStop()) break
                    for (i in 0 until p.S) {
                        // [監査(未レビュー領域再監査)] O(S^2)内側スキャンにも締切確認を追加（HF66/BlockRotationPolishと同型）。
                        if (shouldStop()) break
                        if ((0 until w).any { !movable(i, j + it) }) continue
                        for (i2 in i + 1 until p.S) {
                            if (i !in anchorStaff && i2 !in anchorStaff) continue   // 違反職員を含む対のみ
                            if ((0 until w).any { !movable(i2, j + it) }) continue
                            var feasible = true; var same = true
                            for (t in 0 until w) {
                                if (!p.canDo(i, work[i2][j + t]) || !p.canDo(i2, work[i][j + t])) { feasible = false; break }
                                if (work[i][j + t] != work[i2][j + t]) same = false
                            }
                            if (!feasible || same) continue
                            // [#5 差分前フィルタ] 同 sgrp かつ同 ssk の2者ブロック交換のみ前判定。
                            val canPre = p.sgrp[i] == p.sgrp[i2] && p.ssk[i] == p.ssk[i2]
                            val preP = if (canPre) staffPacked(p, work, i) + staffPacked(p, work, i2) else 0L
                            // [厳密ピン保護] ブロック交換はwindow内の日ごとにi/i2の自身のシフト回数を変えうる
                            //   （2者間で異なるシフトが混在する日がある限り）。staffRange厳密ピン(lo==hi)を
                            //   崩す候補は不採用にする（keep-best/重みは不変・追加ガードのみ）。
                            val workBeforeBlock = work.copy2D()
                            for (t in 0 until w) { val tmp = work[i][j + t]; work[i][j + t] = work[i2][j + t]; work[i2][j + t] = tmp }
                            if (canPre) {
                                val postP = staffPacked(p, work, i) + staffPacked(p, work, i2)
                                if (postP >= preP) { for (t in 0 until w) { val tmp = work[i][j + t]; work[i][j + t] = work[i2][j + t]; work[i2][j + t] = tmp }; skipped++; continue }
                            }
                            val rep = UnifiedViolationChecker.check(state, work)
                            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeBlock, work)) { bestRep = rep; applied++; improved = true }
                            else for (t in 0 until w) { val tmp = work[i][j + t]; work[i][j + t] = work[i2][j + t]; work[i2][j + t] = tmp }   // 巻き戻し
                        }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val logs = listOf(MirrorLog(tag = "C3Polish",
            message = "連続規則c3系研磨(2者ブロック): c3 ${before.breakdown["c3"] ?: 0}->${bestRep.breakdown["c3"] ?: 0}" +
                " / c3m ${before.breakdown["c3m"] ?: 0}->${bestRep.breakdown["c3m"] ?: 0}" +
                " / c3mn ${before.breakdown["c3mn"] ?: 0}->${bestRep.breakdown["c3mn"] ?: 0}" +
                " / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回 (差分前フィルタで省略${skipped}手)"))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [ソフト研磨・c3系強化] c3/c3m/c3mn(連続規則)で違反しているセルを起点に、3職員×連日(2-3日)の
     * ブロック「回転」を試す。2者ブロック入替や同日k=3巡回では到達できない3者×窓の組替えを、各日の
     * (日,シフト)人数を保ったまま（=被覆/HARD不変）行い、実目的(UnifiedViolationChecker)で改善時のみ
     * 採用（keep-best＝退化なし）。重み・パラメータは不変。違反セル指向なので低コスト。
     * 2回の2者交換に分解すると中間で悪化するため山登りでは越えられない局面を、回転1手で跨ぐのが狙い。
     */
    /**
     * [ソフト研磨・3者回転] 指定クラス(anchorClasses)で違反しているセルを起点に、3職員×連日(2-3日)の
     * ブロック「回転」を試す。2者ブロック入替/同日k=3巡回では到達できない3者×窓の組替えを、各日の
     * (日,シフト)人数を保ったまま（=被覆/HARD不変）行い、実目的(UnifiedViolationChecker)で改善時のみ
     * 採用（keep-best＝退化なし）。c1・c3系どちらの違反起点にも使える汎用版。重み・パラメータ不変。
     * 2回の2者交換に分解すると中間で悪化するため山登りでは越えられない局面を、回転1手で跨ぐのが狙い。
     */
    fun applyBlockRotationPolish(state: MagiState, schedule: Array<IntArray>, anchorClasses: Set<String>, tag: String, maxPasses: Int = 2, shouldStop: () -> Boolean = { false }): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        var skipped = 0     // [#5] 前フィルタでフル評価を省いた手数(有効性ログ用)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は「希望が一切ない」判定で、実現不能な希望
        //   (canDo(i,wish)==false)まで動かせないと誤判定していた（3.183.0 LightMirrorOptimizer と
        //   同型のバグ）。実現不能な希望はpref計上上も定数=動かして良い＝canDoガード込みの
        //   wishLocked が正しい判定。安全側（isBetter/checkerが最終ゲート）で候補が広がるのみ。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        val windows = intArrayOf(2, 3)
        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            // 指定クラスで違反している職員(=回転の起点)を収集。無ければ即終了（コスト0）。
            // [実バグ修正/applyC1WindowPolishと同根] rep0.violations（1セル=最重1クラスのみ）だと、
            //   anchorClassesのマーク位置に更に重い他族が同居する場合そのセルの分類が上書きされ検出漏れ
            //   になる。cellFamilies（1セルの全クラス保持）に切替え、上書きされても検出できるようにする。
            //   起点が広がるだけの後方互換な修正（C1Rotate/C3Rotate 両呼出に共通して適用される）。
            val rep0 = if (pass == 0) before else UnifiedViolationChecker.check(state, work)
            val anchorStaff = HashSet<Int>()
            for ((key, fams) in rep0.cellFamilies) {
                if (fams.any { it in anchorClasses }) anchorStaff.add(key.substringBefore(",").toIntOrNull() ?: continue)
            }
            if (anchorStaff.isEmpty()) break
            var improved = false
            for (w in windows) {
                if (p.T < w) continue
                for (j in 0..p.T - w) {
                    if (shouldStop()) break
                    // この窓で全日movableな職員のみ回転対象（同一3名を各日で回す＝日内人数不変）。
                    val cand = (0 until p.S).filter { i -> (0 until w).all { movable(i, j + it) } }
                    if (cand.size < 3) continue
                    for (ai in cand) {
                        if (shouldStop()) break   // [予算ガード] 締切後は O(cand^3) の全候補フル評価を走り切らせない(HF66=2.65.0と同方針)。
                        if (ai !in anchorStaff) continue
                        for (bi in cand) {
                            if (shouldStop()) break   // [予算ガード] 内側スキャンでも締切確認しバーストを O(cand) 以内に抑える。
                            if (bi == ai) continue
                            for (ci in cand) {
                                if (ci == ai || ci == bi) continue
                                // 回転 ai<-bi, bi<-ci, ci<-ai が各日で担当可能か。
                                var feasible = true
                                for (t in 0 until w) {
                                    if (!p.canDo(ai, work[bi][j + t]) || !p.canDo(bi, work[ci][j + t]) || !p.canDo(ci, work[ai][j + t])) { feasible = false; break }
                                }
                                if (!feasible) continue
                                val sa = IntArray(w) { work[ai][j + it] }
                                val sb = IntArray(w) { work[bi][j + it] }
                                val sc = IntArray(w) { work[ci][j + it] }
                                // [#5 差分前フィルタ] 同 sgrp かつ同 ssk の手のみ前判定(群/スキル群/被覆/pref不変
                                //   →関与3名の packed が改善しなければ全体目的も改善しえない)。採用はフル評価が担う=安全。
                                val canPre = p.sgrp[ai] == p.sgrp[bi] && p.sgrp[bi] == p.sgrp[ci] &&
                                    p.ssk[ai] == p.ssk[bi] && p.ssk[bi] == p.ssk[ci]
                                val preP = if (canPre) staffPacked(p, work, ai) + staffPacked(p, work, bi) + staffPacked(p, work, ci) else 0L
                                // [厳密ピン保護] 3者回転もwindow内で各職員の自身のシフト回数を変えうるため、
                                //   staffRange厳密ピン(lo==hi)を崩す候補は不採用にする（keep-best/重みは不変）。
                                val workBeforeRotate = work.copy2D()
                                for (t in 0 until w) { work[ai][j + t] = sb[t]; work[bi][j + t] = sc[t]; work[ci][j + t] = sa[t] }
                                if (canPre) {
                                    val postP = staffPacked(p, work, ai) + staffPacked(p, work, bi) + staffPacked(p, work, ci)
                                    if (postP >= preP) { for (t in 0 until w) { work[ai][j + t] = sa[t]; work[bi][j + t] = sb[t]; work[ci][j + t] = sc[t] }; skipped++; continue }
                                }
                                val rep = UnifiedViolationChecker.check(state, work)
                                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRotate, work)) { bestRep = rep; applied++; improved = true }
                                else for (t in 0 until w) { work[ai][j + t] = sa[t]; work[bi][j + t] = sb[t]; work[ci][j + t] = sc[t] }   // 巻き戻し
                            }
                        }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val logs = listOf(MirrorLog(tag = tag,
            message = "$tag 3者回転研磨: c1 ${before.breakdown["c1"] ?: 0}->${bestRep.breakdown["c1"] ?: 0}" +
                " / c3 ${before.breakdown["c3"] ?: 0}->${bestRep.breakdown["c3"] ?: 0}" +
                " / c3m ${before.breakdown["c3m"] ?: 0}->${bestRep.breakdown["c3m"] ?: 0}" +
                " / c3mn ${before.breakdown["c3mn"] ?: 0}->${bestRep.breakdown["c3mn"] ?: 0}" +
                " / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回 (差分前フィルタで省略${skipped}手)"))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    // 主目的(hard→weighted→total)を悪化させないか。平準化は「悪化させない範囲」で二次最適化する。
    private fun mainNotWorse(rep: ViolationReport, best: ViolationReport): Boolean =
        rep.hard < best.hard ||
            (rep.hard == best.hard && rep.weightedScore < best.weightedScore - 1e-9) ||
            (rep.hard == best.hard && rep.weightedScore <= best.weightedScore + 1e-9 && rep.total <= best.total)

    // グループ内シフト回数のばらつき（群ごと・担当ONシフトごとの分散の総和）。小さいほど平準。
    private fun groupShiftVariance(p: Problem, state: MagiState, counts: Array<IntArray>): Double {
        var v = 0.0
        for (g in 0 until p.G) {
            val gs = state.groupShift.getOrNull(g) ?: continue
            val mem = (0 until p.S).filter { p.sgrp[it] == g }
            if (mem.size < 2) continue
            for (k in 0 until p.K) {
                if (gs.getOrNull(k) != 1) continue
                var sum = 0; for (i in mem) sum += counts[i][k]
                val mean = sum.toDouble() / mem.size
                for (i in mem) { val d = counts[i][k] - mean; v += d * d }
            }
        }
        return v
    }

    // 7日周期(曜日)の偏り: 各職員の勤務(休以外)を曜日7バケットに割り、分散を総和。小さいほど曜日が均等。
    private fun dayOfWeekVariance(p: Problem, state: MagiState, work: Array<IntArray>, restIdx: Int): Double {
        // [一括修正] dow0 は Problem.dow0（目的関数 weekly と同一ソース）を使う（旧: ここで再パース＝重複計算）。
        val dow0 = p.dow0
        var v = 0.0
        for (i in 0 until p.S) {
            val wd = IntArray(7)
            for (j in 0 until p.T) { val k = work[i][j]; if (k != restIdx && k in 0 until p.K) wd[(dow0 + j) % 7]++ }
            val avg = wd.sum() / 7.0
            for (x in wd) v += (x - avg) * (x - avg)
        }
        return v
    }

    /**
     * [平準化・グループ内シフト回数] 同一グループ内で各シフトの担当回数を均す。同日に同一グループの2職員の
     * シフトを入替え（被覆＝人数不変・HARD維持）、主目的(hard/total/weighted)を悪化させない範囲で
     * グループ内分散を厳密に下げる移動だけ採用（lexicographic）。主目的は不変なので退化しない。
     */
    fun applyGroupShiftEqualizePolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 2, shouldStop: () -> Boolean = { false }): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var bestMetric = groupShiftVariance(p, state, countMatrix(p, work))
        var applied = 0
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        var pass = 0
        while (pass < maxPasses && bestMetric > 0.0) {
            if (shouldStop()) break
            var improved = false
            for (j in 0 until p.T) {
                if (shouldStop()) break
                for (a in 0 until p.S) {
                    // [監査(未レビュー領域再監査)] O(S^2)内側スキャンにも締切確認を追加。
                    if (shouldStop()) break
                    if (!movable(a, j)) continue
                    for (b in a + 1 until p.S) {
                        if (!movable(b, j) || p.sgrp[a] != p.sgrp[b]) continue
                        val sa = work[a][j]; val sb = work[b][j]
                        if (sa == sb || !p.canDo(a, sb) || !p.canDo(b, sa)) continue
                        work[a][j] = sb; work[b][j] = sa
                        val rep = UnifiedViolationChecker.check(state, work)
                        val m = groupShiftVariance(p, state, countMatrix(p, work))
                        if (mainNotWorse(rep, bestRep) && m < bestMetric - 1e-9) { bestRep = rep; bestMetric = m; applied++; improved = true }
                        else { work[a][j] = sa; work[b][j] = sb }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val logs = listOf(MirrorLog(tag = "GroupEqualize",
            message = "グループ内シフト回数の平準化: ばらつき ${"%.1f".format(before.let { groupShiftVariance(p, state, countMatrix(p, normalizeSchedule(schedule, p))) })}->${"%.1f".format(bestMetric)} 採用${applied}回"))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [平準化・7日周期] 各職員の勤務が特定の曜日に偏らないよう均す。同日の2職員のシフトを入替え
     * （被覆不変・HARD維持）、主目的を悪化させない範囲で曜日分散を厳密に下げる移動だけ採用。退化なし。
     */
    fun applyWeeklyEqualizePolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 2, shouldStop: () -> Boolean = { false }): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        // [一括修正] 休 index は Problem.restIdx（目的関数 weekly と同一解決・未発見時は 0 フォールバック）を使う。
        //   旧: ローカル indexOfFirst は未発見で -1 ＝全シフトを勤務扱いし、目的関数と別の指標を最小化していた（latent）。
        val restIdx = p.restIdx
        var bestRep = before
        var bestMetric = dayOfWeekVariance(p, state, work, restIdx)
        val beforeMetric = bestMetric
        var applied = 0
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        var pass = 0
        while (pass < maxPasses && bestMetric > 0.0) {
            if (shouldStop()) break
            var improved = false
            for (j in 0 until p.T) {
                if (shouldStop()) break
                for (a in 0 until p.S) {
                    // [監査(未レビュー領域再監査)] O(S^2)内側スキャンにも締切確認を追加。
                    if (shouldStop()) break
                    if (!movable(a, j)) continue
                    for (b in a + 1 until p.S) {
                        if (!movable(b, j)) continue
                        val sa = work[a][j]; val sb = work[b][j]
                        if (sa == sb || !p.canDo(a, sb) || !p.canDo(b, sa)) continue
                        work[a][j] = sb; work[b][j] = sa
                        val rep = UnifiedViolationChecker.check(state, work)
                        val m = dayOfWeekVariance(p, state, work, restIdx)
                        if (mainNotWorse(rep, bestRep) && m < bestMetric - 1e-9) { bestRep = rep; bestMetric = m; applied++; improved = true }
                        else { work[a][j] = sa; work[b][j] = sb }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val logs = listOf(MirrorLog(tag = "WeeklyEqualize",
            message = "7日周期(曜日)の平準化: 偏り ${"%.1f".format(beforeMetric)}->${"%.1f".format(bestMetric)} 採用${applied}回"))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [ソフト研磨・weekly（曜日平準化）＝長方形交換] weekly は「職員が特定の曜日にばかり勤務する」偏りで、
     * L1偏差（`weeklyDevOfBucket`＝各曜日の勤務数の round(平均) からの偏差和）で評価される。**同日2者スワップ
     * （CyclicSwap / equalize 系）は勤務↔勤務では曜日別の勤務/休が変わらず weekly をほぼ動かせない**（勤務種類が
     * 入れ替わるだけで、どちらの職員も「その曜日に勤務している」事実は不変）。これが「weekly の研磨ができていない」
     * 実害の根本（実機ログで weekly=56＝SOFT 残差の最大級）。
     *
     * そこで **被覆保存の 2職員×2日 長方形交換** を導入する: 職員 i が「過剰曜日の日 j1 で勤務(シフト x)・過少曜日の
     * 日 j2 で休」、相手 i' が「j1 で休・j2 で勤務(シフト y)」のとき、両者の j1/j2 を丸ごと入替える
     * （i: j1→休/j2→y、i': j1→x/j2→休）。各日の各シフト人数は保存される（j1 の x は i→i'、j2 の y は i'→i へ移るだけ）
     * ため covU/covO・群レンジ・pref は不変で、i の勤務が過剰曜日→過少曜日へ移動して weekly が下がる。fair（群内シフト
     * 回数）や low/high/apt/c2 など per-staff 族も副次的に動く。**採否は実目的関数 isBetter のみ**（hard→total→weighted、
     * total は weekly/fair を含む）＝退化なし（keep-best）。weekly>0 の職員のみ起点＋first-improvement で空探索は即終了。
     * 変更セルは wish 固定なら不動（4セルとも movable ガード）。covO/c42/c2 など per-day 族は同日 CyclicSwap（isBetter）が
     * 既に最適に研磨済みのため本パスの対象外（2.49.0 の「専用パスは冗長」の結論を踏襲）。
     */
    fun applyWeeklyRebalancePolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 2, shouldStop: () -> Boolean = { false }): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        val rest = p.restIdx
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        fun weekdayOf(j: Int) = (p.dow0 + j) % 7
        fun wdBucket(i: Int): IntArray {
            val wd = IntArray(7)
            for (j in 0 until p.T) { val k = work[i][j]; if (k != rest && k in 0 until p.K) wd[weekdayOf(j)]++ }
            return wd
        }
        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            for (i in 0 until p.S) {
                if (shouldStop()) break
                val wd = wdBucket(i)
                if (weeklyDevOfBucket(wd) == 0) continue
                var sum = 0; for (w in wd) sum += w
                val tgt = Math.round(sum.toDouble() / 7.0).toInt()
                // 最も過剰な曜日（勤務が多すぎ）と最も過少な曜日（少なすぎ）を1つずつ狙う。
                var wOver = -1; var wUnder = -1; var maxOver = 0; var maxUnder = 0
                for (w in 0 until 7) {
                    if (wd[w] - tgt > maxOver) { maxOver = wd[w] - tgt; wOver = w }
                    if (tgt - wd[w] > maxUnder) { maxUnder = tgt - wd[w]; wUnder = w }
                }
                if (wOver < 0 || wUnder < 0) continue
                // i が過剰曜日に勤務している日(movable, 非休) / 過少曜日に休んでいる日(movable)。
                val overDays = (0 until p.T).filter { weekdayOf(it) == wOver && movable(i, it) && work[i][it] != rest && work[i][it] in 0 until p.K }
                val underDays = (0 until p.T).filter { weekdayOf(it) == wUnder && movable(i, it) && work[i][it] == rest }
                var done = false
                for (j1 in overDays) {
                    if (done || shouldStop()) break
                    val x = work[i][j1]
                    for (j2 in underDays) {
                        // [レビュー#6 3.213.0] 内側ループにも締切確認（各候補がフル check を伴うため、
                        //   キャンセル後のバーストを1候補以内に抑える。HF66=2.65.0/BlockRotation=3.84.0 と同方針）。
                        if (done || shouldStop()) break
                        for (ip in 0 until p.S) {
                            if (done || shouldStop()) break
                            if (ip == i) continue
                            // 相手 i' は j1 で休・j2 で勤務(非休)、両日 movable。被覆保存には i'←x(j1), i←y(j2) が担当可であること。
                            if (!movable(ip, j1) || !movable(ip, j2)) continue
                            if (work[ip][j1] != rest) continue
                            val y = work[ip][j2]
                            if (y == rest || y !in 0 until p.K) continue
                            if (!p.canDo(ip, x) || !p.canDo(i, y)) continue
                            // 長方形交換を適用（被覆保存）→ フル評価 → 改善時のみ採用、不採用なら完全巻き戻し。
                            // [監査で発見・3.270.0] isBetter は hard→total→weightedScore の辞書式のため、
                            //   raw total が改善してもweightedScoreが悪化する組合せ(重い厳密ピン破りを軽い
                            //   weekly改善が数の上で上回る)がありうる。同型の全パスに既に適用済みの
                            //   exactPinRegression ガードをここにも追加（3.256.0の retrofit 漏れ）。
                            val workBeforeRect = work.copy2D()
                            work[i][j1] = rest; work[i][j2] = y; work[ip][j1] = x; work[ip][j2] = rest
                            val rep = UnifiedViolationChecker.check(state, work)
                            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRect, work)) { bestRep = rep; applied++; improved = true; done = true; break }
                            work[i][j1] = x; work[i][j2] = rest; work[ip][j1] = rest; work[ip][j2] = y
                        }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val logs = listOf(MirrorLog(tag = "WeeklyRebalance",
            message = "曜日平準化(長方形交換): total ${before.total}->${bestRep.total} 採用${applied}回"))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /** day j を含む有効窓のどれかで、職員 i のシフト X が N 回未満（=c1不足）か。 */
    private fun inDeficientC1Window(p: Problem, work: Array<IntArray>, i: Int, x: Int, d: Int, n: Int, j: Int): Boolean {
        if (d <= 0) return false
        var w = maxOf(0, j - d + 1)
        val wEnd = minOf(j, p.T - d)
        while (w <= wEnd) {
            var z = 0
            for (l in 0 until d) if (work[i][w + l] == x) z++
            if (z < n) return true
            w++
        }
        return false
    }

    /** 職員 i の c1 fire 総数（全 cons1・canDo ガード。checker/MirrorCore と同一のスライド窓意味論）。 */
    private fun c1RowFires(p: Problem, work: Array<IntArray>, i: Int): Int {
        var fires = 0
        for (c in p.cons1) {
            val x = c.shiftIdx; val d = c.day1; val n = c.day2
            if (x !in 0 until p.K || d <= 0 || !p.canDo(i, x)) continue
            var w = 0
            while (w <= p.T - d) {
                var z = 0
                for (l in 0 until d) if (work[i][w + l] == x) z++
                if (z < n) fires++
                w++
            }
        }
        return fires
    }

    /**
     * [ソフト研磨・C1] 期間要件 cons1（D日窓にシフトXをN回以上・職員ごと）の研磨。
     * c1不足の (職員 i, 窓) を見つけ、その窓内で i が X でない日 j に対し、その日に X をしている提供者 i' と
     * **同日スワップ**（i←X, i'←iの旧シフト＝被覆不変・HARD維持）して i の X を増やす。実目的関数で評価し
     * 改善時のみ採用（keep-best＝退化なし）。汎用循環交換と違い**c1不足の窓に的を絞る**ので c1 を効率的に削る。
     * [E11/多人数ブロック移動を反映] 同日スワップの直接相手 i' が見つからない/不採用のときは諦めず、
     * i を X へ直接動かし、空いた旧シフト a の穴を `findCovUChain`（covU の玉突き連鎖）と同じ機構で
     * 埋め直す（a に need1 が無い/余裕があるなら連鎖不要でそのまま採用判定）。i の移動＋連鎖手をまとめて
     * 1候補として実目的関数で評価し、改善時のみ採用（不採用時は連鎖手も含め正しく全巻き戻し）。
     *
     * [C1研磨アルゴリズムの再設計/回数保存移設の追加] 手A(同日スワップ)/手B(直接移動+連鎖)はどちらも
     * 「i の X 回数を+1する」count-changing 手しか生成できない。golden_state の残差解剖(Python実測)では
     * c1=115 fires のうち relocation-only=48（休 fires の80%が個人別回数の下限=上限で固定された職員由来）
     * は、X追加が low/high(90/45)>c1(4×窓数)で必ず isBetter に棄却され、**i自身のXを余剰位置→不足窓へ
     * 移す回数保存の移設**だけが唯一の改善手と判明（行内2日swapの貪欲シムで c1 115→62, -46%）。
     * 現行手A/Bにこの移設プリミティブが無い欠落を埋めるため、手A(同日交換)の直後・手B(直接移動)の前に
     * 保存性の強い順で2手を追加する:
     *   手R1=鏡像長方形（i=[X@j1,b@j]↔i'=[b@j1,X@j]の4セル交換）: 両職員の回数と日別人数が両方保存
     *        （groupViol/pref/low/high/apt/c2/covU/covO/c41系まで構造的不変）＝isBetterはc1/c3系/weekly
     *        だけの勝負になり採用されやすい最も安全な移設。
     *   手R2=自己2日swap（i の X@j1 ↔ b@j）: i の回数は保存（low/high/apt/c2/pref/groupViol不変）だが
     *        日別人数が変わるため、離脱側2箇所を p.covUCell（source of truth）で事前除外してから適用。
     * どちらも c3n(HARD) は p.makesForbiddenRun で事前枝刈り（見逃しても isBetter が最終拒否＝安全側）。
     * 採否は既存と同じ isBetter(hard→total→weighted) の keep-best のみ＝退化不能・HF77非該当（重み不変）。
     * add-fixable（追加が唯一の解の局面）は既存手A/Bの担当のまま＝手クラスが互いに素で冗長を作らない。
     */
    fun applyC1WindowPolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }, seed: Long = 0x1C1L): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        var aRect = 0; var aSelf = 0
        if (p.cons1.isEmpty()) {
            return CyclicSwapResult(work, before.total, bestRep.total, 0,
                listOf(MirrorLog(tag = "C1Polish", message = "cons1なし=スキップ")))
        }
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        // [C1研磨・手B強化] staff i2 が shift x2 について day を含むいずれかの窓で不足しているか（全cons1横断）。
        //   手B(findCovUChain の玉突き連鎖)の候補選定に c1Pref として渡し、「連鎖に組み込む相手が、たまたま
        //   その相手自身のc1不足も一緒に解消する」候補を優先させる（並べ替えのみ・安全条件は不変・探索の
        //   正しさは常に isBetter が最終担保）。
        fun c1Deficient(i2: Int, x2: Int, day: Int): Boolean {
            if (day !in 0 until p.T) return false
            for (c2 in p.cons1) {
                if (c2.shiftIdx != x2 || c2.day1 <= 0) continue
                if (inDeficientC1Window(p, work, i2, x2, c2.day1, c2.day2, day)) return true
            }
            return false
        }
        // [頭打ちの理由を可視化/RangePolish=3.222.0と同型] 手A/R1/R2いずれも成立しなかった最終フォール
        //   バック(手B=直接移動+玉突き)の結果を(staff,shift)ごとに集計。「候補なし」=findCovUChainが
        //   埋め戻し相手を1人も見つけられなかった／「不採用」=候補は見つかったが実目的関数(isBetter)が
        //   総合的に拒否した、の2分類（RangePolishと同じ粒度）。休の窓ルールが解消しない理由を
        //   ユーザーがログから直接読めるようにする。
        val blockStats = HashMap<Pair<Int, Int>, MutableMap<String, Int>>()
        fun recordBlock(i: Int, x: Int, reason: String) {
            blockStats.getOrPut(i to x) { HashMap() }.merge(reason, 1, Int::plus)
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] 手B/手R3が単独では isBetter に不採用だった候補
        //   （chain/repackとも構築自体は成功したもの）を蓄積し、末尾で複数を束ねて再挑戦する。
        val combinable = ArrayList<CombinatorialRepair.Candidate>()
        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            // [違反セル指向] c1で違反している職員のみを起点に絞る。c1は職員ごと→改善手は必ず違反職員を
            //   含む＝ロスレス。空なら即終了でコスト0。
            // [実機ログ起因/実バグ修正] 旧実装は rep0.violations（1セル=最重1クラスのみ）を見ていたため、
            //   c1違反セルが同じセルでc3n(HARD,重み7000)等の更に重い違反も起こしている場合、そのセルの
            //   c1マークが violations 上では上書きされて消え、該当職員のc1違反自体が研磨の起点候補から
            //   漏れうる潜在バグだった（他のc1違反セルで既に起点に入っていれば実害なしだが、全run-startが
            //   重い違反と同居する職員では研磨が一度も試みられない）。cellFamilies（3.111.0で追加された
            //   1セル=重み降順の全クラスリスト、weight-priorityで discard しない）に切替えれば漏れなく検出
            //   できる。起点集合が広がるだけ(既存の起点は cellFamilies にも必ず含まれる=violationsの
            //   最重クラスはcellFamiliesの先頭要素と同一)なので後方互換・退化なし。
            val rep0 = if (pass == 0) before else UnifiedViolationChecker.check(state, work)
            val anchorStaff = HashSet<Int>()
            for ((key, fams) in rep0.cellFamilies) {
                if ("vio-c1" in fams) anchorStaff.add(key.substringBefore(",").toIntOrNull() ?: continue)
            }
            if (anchorStaff.isEmpty()) break
            for (c in p.cons1) {
                val x = c.shiftIdx; val d = c.day1; val n = c.day2
                if (x !in 0 until p.K || d <= 0) continue
                for (i in 0 until p.S) {
                    if (shouldStop()) break
                    if (i !in anchorStaff) continue
                    if (!p.canDo(i, x)) continue
                    // [移設ドナー] i 自身の X 保有日のうち「抜いても this ルールの窓が新規に不足化しない」余剰位置。
                    //   盤面が変わるたび(i,x)単位で無効化し次の j で再構築する（遅延キャッシュ）。
                    var donorsCache: List<Int>? = null
                    fun donors(): List<Int> = donorsCache ?: (0 until p.T).filter { j1 ->
                        work[i][j1] == x && movable(i, j1) && run {
                            var wStart = maxOf(0, j1 - d + 1); val wEnd = minOf(j1, p.T - d)
                            var surplus = true
                            while (wStart <= wEnd) {
                                var z = 0
                                for (l in 0 until d) if (work[i][wStart + l] == x) z++
                                if (z <= n) { surplus = false; break }   // 閾値ちょうど以下=抜くと新規fire→保守的に除外
                                wStart++
                            }
                            surplus
                        }
                    }.also { donorsCache = it }
                    for (j in 0 until p.T) {
                        // [監査(未レビュー領域再監査)] このjループはi2走査に加えfindCovUChainのBFSも伴い重い
                        //   （HF66/BlockRotationPolishと同型の予算超過対策として日ごとにも確認）。
                        if (shouldStop()) break
                        if (work[i][j] == x || !movable(i, j)) continue
                        if (!inDeficientC1Window(p, work, i, x, d, n, j)) continue
                        val a = work[i][j]                                  // i の旧シフト
                        // [厳密ピン保護] 手A/手B は i(・i2)の自身のシフト回数を実際に変える(x+1/a-1)唯一の
                        //   手（手R1/R2/R3は同一職員内の日入替のみで回数は代数的に保存される＝対象外）。
                        //   staffRangeが下限=上限で完全固定("厳密ピン")の職員をこの手で崩さないよう、
                        //   swap前の盤面を基準にexactPinRegressionで追加ガードする（keep-best/重みは不変）。
                        val workBeforeDay = work.copy2D()
                        var done = false
                        for (i2 in 0 until p.S) {
                            if (i2 == i || work[i2][j] != x || !movable(i2, j) || !p.canDo(i2, a)) continue
                            work[i][j] = x; work[i2][j] = a                 // 同日スワップ（被覆不変）
                            val rep = UnifiedViolationChecker.check(state, work)
                            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeDay, work)) {
                                bestRep = rep; applied++; improved = true; done = true; break
                            }
                            work[i][j] = a; work[i2][j] = x                 // 巻き戻し
                        }
                        if (done) { donorsCache = null; continue }

                        // [手R1] 鏡像長方形: i=[X@j1,a@j] ↔ i2=[a@j1,X@j]。回数・日別人数とも完全保存
                        //   （i2 は既に保有しているシフトしか持たない＝canDo自動成立だが規律として明示検査する）。
                        val fires0 = c1RowFires(p, work, i)
                        for (j1 in donors()) {
                            if (done || shouldStop()) break
                            if (j1 == j) continue
                            work[i][j1] = a; work[i][j] = x
                            val gain = fires0 - c1RowFires(p, work, i)
                            work[i][j1] = x; work[i][j] = a                 // 判定用の一時変更は必ず復元
                            if (gain <= 0) continue
                            for (i2 in 0 until p.S) {
                                if (done || shouldStop()) break
                                if (i2 == i) continue
                                if (work[i2][j1] != a || work[i2][j] != x) continue      // 完全鏡像の相手のみ
                                if (!movable(i2, j1) || !movable(i2, j)) continue
                                if (!p.canDo(i, x) || !p.canDo(i2, a)) continue           // 構造上恒真・規律として明示
                                work[i][j1] = a; work[i][j] = x; work[i2][j1] = x; work[i2][j] = a
                                val bad3n = p.makesForbiddenRun(work, i, j1, a) || p.makesForbiddenRun(work, i, j, x) ||
                                    p.makesForbiddenRun(work, i2, j1, x) || p.makesForbiddenRun(work, i2, j, a)
                                if (!bad3n) {
                                    val rep = UnifiedViolationChecker.check(state, work)
                                    if (isBetter(rep, bestRep)) {
                                        bestRep = rep; applied++; aRect++; improved = true; done = true
                                        donorsCache = null
                                        break
                                    }
                                }
                                if (!done) { work[i][j1] = x; work[i][j] = a; work[i2][j1] = a; work[i2][j] = x }
                            }
                        }
                        if (done) continue

                        // [手R2] 自己2日swap: i の X@j1 ↔ a@j（回数保存＝low/high/apt/c2不変。日別人数が
                        //   変わるため離脱側2箇所を covUCell(source of truth)で事前除外してから適用）。
                        //   a が normalizeSchedule 由来の -1(範囲外/未割当) なら「本物のシフト」ではないため
                        //   R2(自己内の付け替え)の対象外とする（work[i][j1] へ -1 を書き込む不正な手を防ぐ。
                        //   手A/手Bは a=-1 でも canDo(-1)=false / findCovUChain の範囲ガードで元々安全なので
                        //   この場合も手Bへは進める＝ここは continue でなく R2 ブロックだけを囲む）。
                        if (a in 0 until p.K) {
                            for (j1 in donors()) {
                                if (done || shouldStop()) break
                                if (j1 == j) continue
                                work[i][j1] = a; work[i][j] = x
                                val gain = fires0 - c1RowFires(p, work, i)
                                work[i][j1] = x; work[i][j] = a
                                if (gain <= 0) continue
                                var cx = 0; var ca = 0
                                for (s in 0 until p.S) { if (work[s][j1] == x) cx++; if (work[s][j] == a) ca++ }
                                if (p.covUCell(x, j1, cx - 1) > p.covUCell(x, j1, cx)) continue   // X の j1 離脱で covU 悪化
                                if (p.covUCell(a, j, ca - 1) > p.covUCell(a, j, ca)) continue      // a の j 離脱で covU 悪化
                                work[i][j1] = a; work[i][j] = x
                                val bad3n = p.makesForbiddenRun(work, i, j1, a) || p.makesForbiddenRun(work, i, j, x)
                                if (!bad3n) {
                                    val rep = UnifiedViolationChecker.check(state, work)
                                    if (isBetter(rep, bestRep)) {
                                        bestRep = rep; applied++; aSelf++; improved = true; done = true; donorsCache = null
                                    }
                                }
                                if (!done) { work[i][j1] = x; work[i][j] = a }
                            }
                        }
                        if (done) continue

                        // [E11反映] 直接の交換相手が見つからない/不採用 → i を X へ動かし、空いた a の穴を
                        //   玉突き連鎖で埋め直す（findCovUChain は盤面を変えないため元値を保存して巻き戻せるようにする）。
                        work[i][j] = x
                        // exclude=i: i は既に x へ動かした本人なので、a を埋め戻す候補から除外
                        //   （除外しないと「i が a に戻る」= i の移動そのものを打ち消す退行手をBFSが選びうる）。
                        // c1Pref=c1Deficient: 連鎖の相手選びを「その相手自身のc1不足も一緒に解消するか」で
                        //   優先付け（並べ替えのみ・見つからなければ従来どおり）。
                        val chain = findCovUChain(p, work, a, j, rng, exclude = i,
                            c1Pref = { s2, sh, dy -> c1Deficient(s2, sh, dy) })
                        val oldVals = chain?.let { ch -> IntArray(ch.size) { work[ch[it][0]][ch[it][1]] } }
                        chain?.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
                        val rep = UnifiedViolationChecker.check(state, work)
                        if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeDay, work)) {
                            bestRep = rep; applied++; improved = true
                            donorsCache = null
                        } else {
                            if (chain != null && oldVals != null) {
                                for (idx in chain.indices) work[chain[idx][0]][chain[idx][1]] = oldVals[idx]
                                val hint = "${state.staff.getOrNull(i)?.name ?: "#$i"}(${state.shifts.getOrNull(x)?.kigou ?: x})"
                                combinable.add(CombinatorialRepair.Candidate(listOf(intArrayOf(i, j, x)) + chain, "手B", hint))
                            }
                            work[i][j] = a
                            recordBlock(i, x, if (chain == null) "候補なし" else "不採用")
                        }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        // [手R3・局所探索の強化=ユーザー指示「賢く深く網羅的に」] 手A/R1/R2/手Bを尽くしてもなお不足
        //   しているルールに対し、アンカーセルに限定しない全ペア網羅(2-opt完全探索)を1回だけ試す。
        //   grilling確定: 真に壁がある職員（例: 休の個人上限が窓ルール最低必要回数を下回る）でも、
        //   休の「配置の仕方」次第で窓違反件数は変動しうる。既存の手A/R1/R2/手Bはいずれも「現在違反
        //   しているセルj」をアンカーに限定した局所改善のみで、その職員の休配置パターン全体を作り直す
        //   大きな手を一度も試していなかった。DP等の厳密最適化は3.200.0で「正しさのリスクが実装前から
        //   顕在化」として不採用済みのため、既存アーキテクチャに忠実な局所探索強化（手R2の一般化＝
        //   アンカー限定とdonors(改善見込みの事前判定)の両方の制約を外した全ペア評価）を採用。
        //   xの保有movable日×非保有movable日の全ペアを評価し、職員全体のfires(全cons1横断合計)が
        //   最も改善するペアを採用する(best-improvement)。安全性は手R2と同一の被覆ガード(covUCell)＋
        //   makesForbiddenRun事前枝刈り＋isBetter最終ゲート。真に壁がある場合はgain<=0のまま全ペアが
        //   尽き、安全に諦める（退化不能）。対象は残存c1違反のある全職員（壁の有無を問わない＝
        //   壁でない職員も既存の狭い近傍だけでは見つからない改善を拾える）。
        var aRepack = 0
        for (i in 0 until p.S) {
            if (shouldStop()) break
            for (c in p.cons1) {
                if (shouldStop()) break
                val x = c.shiftIdx; val d = c.day1; val n = c.day2
                if (x !in 0 until p.K || d <= 0 || !p.canDo(i, x)) continue
                val stillDeficient0 = (0..p.T - d).any { j -> inDeficientC1Window(p, work, i, x, d, n, j) }
                if (!stillDeficient0) continue
                val hx = (0 until p.T).filter { work[i][it] == x && movable(i, it) }
                val ho = (0 until p.T).filter { work[i][it] != x && movable(i, it) }
                if (hx.isEmpty() || ho.isEmpty()) { recordBlock(i, x, "再配置候補なし"); continue }
                val fires0 = c1RowFires(p, work, i)
                var bestGain = 0; var bestJx = -1; var bestJo = -1
                for (jx in hx) {
                    if (shouldStop()) break
                    for (jo in ho) {
                        val a = work[i][jo]
                        var cx = 0; var ca = 0
                        for (s in 0 until p.S) { if (work[s][jx] == x) cx++; if (work[s][jo] == a) ca++ }
                        if (p.covUCell(x, jx, cx - 1) > p.covUCell(x, jx, cx)) continue
                        if (p.covUCell(a, jo, ca - 1) > p.covUCell(a, jo, ca)) continue
                        work[i][jx] = a; work[i][jo] = x
                        val bad3n = p.makesForbiddenRun(work, i, jx, a) || p.makesForbiddenRun(work, i, jo, x)
                        if (!bad3n) {
                            val fires1 = c1RowFires(p, work, i)
                            val gain = fires0 - fires1
                            if (gain > bestGain) { bestGain = gain; bestJx = jx; bestJo = jo }
                        }
                        work[i][jx] = x; work[i][jo] = a
                    }
                }
                if (bestGain > 0) {
                    val a = work[i][bestJo]
                    work[i][bestJx] = a; work[i][bestJo] = x
                    val rep = UnifiedViolationChecker.check(state, work)
                    if (isBetter(rep, bestRep)) {
                        bestRep = rep; applied++; aRepack++
                    } else {
                        work[i][bestJx] = x; work[i][bestJo] = a
                        val hint = "${state.staff.getOrNull(i)?.name ?: "#$i"}(${state.shifts.getOrNull(x)?.kigou ?: x})"
                        combinable.add(CombinatorialRepair.Candidate(
                            listOf(intArrayOf(i, bestJx, a), intArrayOf(i, bestJo, x)), "手R3", hint))
                        recordBlock(i, x, "不採用")
                    }
                } else {
                    recordBlock(i, x, "再配置候補なし")
                }
            }
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] 単独では不採用だった候補群を2〜4件束ねて再挑戦
        //   （grilling確定・c1/range/c3mn/apt/fair横断の共通ヘルパ）。stuckNames より前に実行し、
        //   結合で解消した箇所が「残存」に残らないようにする。
        val c1CombStats = CombinatorialRepair.Stats()
        bestRep = CombinatorialRepair.combineAndApply(
            state, work, bestRep, combinable.asReversed(), ::isBetter, shouldStop = shouldStop, stats = c1CombStats, p = p,
        )
        applied += c1CombStats.combosAccepted
        // [頭打ちの理由を可視化/RangePolish=3.222.0と同型] 手B(直接移動+玉突き)が最終的に失敗した
        //   (staff,ルールのシフト)のうち、最終盤面でなお当該窓が不足しているものだけを「残存」として表示
        //   （途中で別の手/別のjで解消済みなら除外）。「候補なし」=玉突き相手が1人も見つからない構造的
        //   ブロック／「不採用」=候補は見つかったが総合的に isBetter が拒否（他族とのトレードオフで負け）。
        val stuckNames = blockStats.entries.mapNotNull { (key, reasons) ->
            val (i, x) = key
            val stillDeficient = p.cons1.any { c ->
                c.shiftIdx == x && c.day1 > 0 && (0..p.T - c.day1).any { j -> inDeficientC1Window(p, work, i, x, c.day1, c.day2, j) }
            }
            if (!stillDeficient) return@mapNotNull null
            val lbl = "${state.staff.getOrNull(i)?.name ?: "#$i"} ${state.shifts.getOrNull(x)?.kigou ?: x.toString()}"
            val top = reasons.maxByOrNull { it.value }
            if (top != null) "$lbl(${top.key}×${top.value})" else lbl
        }.distinct()
        val c1CombSummary = c1CombStats.summary()
        val logs = listOf(MirrorLog(tag = "C1Polish",
            message = "期間要件(c1)研磨: c1 ${before.breakdown["c1"] ?: 0}->${bestRep.breakdown["c1"] ?: 0} / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回(鏡像:$aRect 自己:$aSelf 再配置:$aRepack)" +
                (if (applied == 0 && (before.breakdown["c1"] ?: 0) > 0) " [頭打ち=改善手なし]" else "") +
                (if (stuckNames.isNotEmpty()) " 残存: ${stuckNames.joinToString(", ")}" else "") +
                (if (c1CombSummary.isNotEmpty()) " / $c1CombSummary" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [C3mnPolish・玉突き連鎖の横展開] cons3mn(回避パターン, SOFT重み12)専用の研磨パス。
     * grilling(2026-07-19)で確定: 対象はc3mnのみ(c3nはHARDで既存のRSI focus優先/keep-bestが担当済み・
     * 同一パスに混ぜると役割が重複し測定しづらくなる)。既存の`findCovUChain`(玉突き連鎖BFS、深さ5まで)を
     * そのまま再利用し、C1Polish(3.158.0)の「手B/E11」ブロックと同型の構成にする。
     *
     * 動機（金沢勇輝の実例, 実機ログ2026-07-19）: cons3n(HARD)がDﾃ直後の主要シフトを軒並み禁止するため、
     * Dﾃを複数回持つ職員はDﾃを連続させるのが安全側になりやすく、cons3mnの「N連続回避」パターンに
     * ヒットしたまま残ることがある。休を追加すればhigh違反(weight90)の方が高くつく局面では崩せないため、
     * 「その職員自身のDﾃ/休の回数を変えずに、そのセルだけ他シフトへ動かす」手が必要——これはまさに
     * findCovUChainが対応する「直接候補が全員(希望固定/禁止連続/被覆)でブロックされる」局面と同型。
     *
     * アンカー: [レビュー3.111.0系]と同じ理由でcellFamilies(1セル=重み降順の全クラス)から"vio-c3mn"を含む
     * セルを起点にする（violations単一クラスマップだと、より重い違反が同居するセルで見落としうるため）。
     * 各アンカーセル(i,j)について、i の担当可能シフトへ付け替える(c3n新規発生はmakesForbiddenRunで事前枝刈り)。
     * 付け替えで元シフトの被覆が悪化するなら`findCovUChain`で玉突き連鎖を試す(C1Polish手Bと同一パターン)。
     * 採否は既存のisBetter(hard→total→weighted)keep-best＝退化不能。完了条件はユニットテストのみ(grilling決定)。
     */
    /**
     * [頭打ち調査・findCovUChainのrangeAvoid用] 候補(staff)がfillShiftを1つ得ると自身のstaffRange上限
     * (rangeHi)を新たに超えるか。桒澤美幸のAｱ超過が研磨後も残る実例を追跡した結果、findCovUChainの
     * 候補選定がコスト無視（構造的に妥当な最初の1件で確定）なため、「別の職員の新規high違反」で
     * 相殺され isBetter に却下される手を引き続けて頭打ちになるケースを確認。C3mnPolish/RangePolish/
     * C3RunPolishの3箇所で findCovUChain 呼出に渡し、そのような候補を後回し（除外はしない）にする。
     */
    private fun exceedsOwnRangeHi(p: Problem, work: Array<IntArray>, staff: Int, fillShift: Int): Boolean {
        val hi = p.rangeHi[staff][fillShift]
        if (hi == Int.MAX_VALUE) return false
        var c = 0
        for (jj in 0 until p.T) if (work[staff][jj] == fillShift) c++
        return c + 1 > hi
    }

    /** [ログから職員が分かるように] cellFamiliesに famKey を含むセルの職員名を重複なく列挙（登場順）。 */
    private fun stuckStaffNames(state: MagiState, cellFamilies: Map<String, List<String>>, famKey: String): List<String> {
        val out = LinkedHashSet<String>()
        for ((key, fams) in cellFamilies) {
            if (famKey !in fams) continue
            val i = key.split(",").getOrNull(0)?.toIntOrNull() ?: continue
            out.add(state.staff.getOrNull(i)?.name ?: "#$i")
        }
        return out.toList()
    }

    fun applyC3mnPolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }, seed: Long = 0xC3AL): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        if (p.cons3mn.isEmpty()) {
            return CyclicSwapResult(work, before.total, bestRep.total, 0,
                listOf(MirrorLog(tag = "C3mnPolish", message = "cons3mnなし=スキップ")))
        }
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        // [汎用玉突き結合フレームワーク, 3.249.0] 単独では不採用だった候補を蓄積し末尾で束ねる。
        val combinable = ArrayList<CombinatorialRepair.Candidate>()
        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            val rep0 = if (pass == 0) before else UnifiedViolationChecker.check(state, work)
            val anchors = ArrayList<Pair<Int, Int>>()
            for ((key, fams) in rep0.cellFamilies) {
                if ("vio-c3mn" !in fams) continue
                val parts = key.split(",")
                val i = parts.getOrNull(0)?.toIntOrNull() ?: continue
                val j = parts.getOrNull(1)?.toIntOrNull() ?: continue
                anchors.add(i to j)
            }
            if (anchors.isEmpty()) break
            for ((i, j) in anchors) {
                if (shouldStop()) break
                if (!movable(i, j)) continue
                val curK = work[i][j]
                if (curK !in 0 until p.K) continue
                var done = false
                for (alt in p.allowedShiftsForStaff(i)) {
                    if (done || shouldStop()) break
                    if (alt == curK) continue
                    if (p.makesForbiddenRun(work, i, j, alt)) continue
                    var cnt = 0
                    for (s in 0 until p.S) if (work[s][j] == curK) cnt++
                    val needsChain = p.covUCell(curK, j, cnt - 1) > p.covUCell(curK, j, cnt)
                    // [監査で発見・3.270.0] isBetter は hard→total→weightedScore の辞書式のため、raw
                    //   total 改善だけでweightedScoreが悪化する組合せ(厳密ピン破り)がありうる。同型の
                    //   全パスに既に適用済みの exactPinRegression ガードをここにも追加（3.256.0 retrofit漏れ）。
                    val workBeforeMove = work.copy2D()
                    work[i][j] = alt
                    if (!needsChain) {
                        val rep = UnifiedViolationChecker.check(state, work)
                        if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeMove, work)) { bestRep = rep; applied++; improved = true; done = true }
                        else {
                            val hint = "${state.staff.getOrNull(i)?.name ?: "#$i"}(${state.shifts.getOrNull(curK)?.kigou ?: curK})"
                            combinable.add(CombinatorialRepair.Candidate(listOf(intArrayOf(i, j, alt)), "C3mnAlt", hint))
                            work[i][j] = curK
                        }
                        continue
                    }
                    // [玉突き連鎖] i の離脱で curK の被覆が悪化する → 玉突きで埋め直す（盤面不変・巻き戻し可能）。
                    val chain = findCovUChain(p, work, curK, j, rng, exclude = i,
                        rangeAvoid = { st, fk -> exceedsOwnRangeHi(p, work, st, fk) })
                    if (chain == null) { work[i][j] = curK; continue }
                    val oldVals = IntArray(chain.size) { work[chain[it][0]][chain[it][1]] }
                    chain.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
                    val rep = UnifiedViolationChecker.check(state, work)
                    if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeMove, work)) { bestRep = rep; applied++; improved = true; done = true }
                    else {
                        for (idx in chain.indices) work[chain[idx][0]][chain[idx][1]] = oldVals[idx]
                        work[i][j] = curK
                        val hint = "${state.staff.getOrNull(i)?.name ?: "#$i"}(${state.shifts.getOrNull(curK)?.kigou ?: curK})"
                        combinable.add(CombinatorialRepair.Candidate(listOf(intArrayOf(i, j, alt)) + chain, "C3mnAlt", hint))
                    }
                }
            }
            pass++
            if (!improved) break
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames より前に実行し、結合で解消した箇所が
        //   「残存」に残らないようにする。
        val c3mnCombStats = CombinatorialRepair.Stats()
        bestRep = CombinatorialRepair.combineAndApply(
            state, work, bestRep, combinable.asReversed(), ::isBetter, shouldStop = shouldStop, stats = c3mnCombStats, p = p,
        )
        applied += c3mnCombStats.combosAccepted
        val stuckNames = stuckStaffNames(state, bestRep.cellFamilies, "vio-c3mn")
        val c3mnCombSummary = c3mnCombStats.summary()
        val logs = listOf(MirrorLog(tag = "C3mnPolish",
            message = "回避パターン(c3mn)研磨: c3mn ${before.breakdown["c3mn"] ?: 0}->${bestRep.breakdown["c3mn"] ?: 0} / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回" +
                (if (applied == 0 && (before.breakdown["c3mn"] ?: 0) > 0) " [頭打ち=改善手なし]" else "") +
                (if (stuckNames.isNotEmpty()) " 残存: ${stuckNames.joinToString(", ")}" else "") +
                (if (c3mnCombSummary.isNotEmpty()) " / $c3mnCombSummary" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [RangePolish・玉突き連鎖の横展開その2] 個人別回数(staffRange low/high, SOFT重み90/45)専用の研磨パス。
     * 動機（桒澤美幸の実例, 実機ログ2026-07-19）: 担当可能シフトが「休/Aｱ/B1」のみで、休=10/10固定・
     * Aｱ上限2の職員が、実際にはAｱ=6(超過4)・休=12(超過2)のまま残っていた。彼女はB1担当が全職員中唯一
     * のため、既存のCyclicSwap/HF67(同日に相手シフトを持つ相手との交換が前提)では交換相手が構造的に
     * 存在せず、「相手を必要としない一方的な付け替え(休/Aｱ→B1)＋被覆が要る側だけ玉突きで埋め直す」手が
     * 必要——C3mnPolish(3.214.0)と同型の穴。
     *
     * [3.244.0 日単位最小費用完全割当]
     * 既存の「1セル付替え＋最初に見つかった玉突き連鎖」は、同日の直接交換が不可能なとき、
     * ランダム順で最初に完成した1本しか評価しない。そのため、桒澤美幸Aｱを代用可能な一般職員へ
     * 渡したくても、相手の現在シフトを美幸が担当できない局面では「候補なし」を繰り返しやすい。
     *
     * 新しい手Mは、対象日の現在シフト多重集合をtokenとして固定し、全職員への再割当をHungarian法で
     * 厳密に解く。2人交換に限定せず、3人・4人・任意長の循環を1回で発見する。日別の各シフト人数は
     * 完全保存されるためcovU/covOは構造的に不変。canDo・希望固定・禁止連続を辺の実行可能条件、
     * staffRange low/high・apt・変更人数を費用とし、最後はUnifiedViolationChecker＋isBetterで採否する。
     *
     * 代用候補はlow違反者だけに限定しない。target shiftを担当可能で上限余力のある全員を対象にし、
     * ①同shiftのlow、②担当可能シフト数が多い一般代用者、③上限余力、④現在回数が少ない順で試す。
     * 実データでは9シフト担当可能な8名が第1層、4シフト限定の専門職員は第2層となり、名前のハードコード
     * なしで「古泉・山本・福澤・佐藤・上條・金沢・モニカ・アリフ」を先に評価できる。
     *
     * アンカー: `report.countViolations`（"i,k"→"vio-low"/"vio-high"、3.210.0で重み優先解決済）から
     * 違反している(staff,shift)ペアを列挙。HIGH(超過)は当該シフトの保有日を他の担当可能シフトへ、
     * LOW(不足)は保有していない日のうち担当可能な1日をそのシフトへ、それぞれ付け替える。付け替えで
     * 空く/埋まる側の被覆(covUCell)が悪化する場合は`findCovUChain`で玉突き修復する（C1Polish手B/
     * C3mnPolishと同一パターン）。採否はisBetter(hard→total→weighted)keep-best＝退化不能。
     */
    fun applyRangePolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }, seed: Long = 0x8A9EL): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        // [ログから職員が分かるように] 対象(staff,shift)の表示名。
        fun label(i: Int, k: Int) = "${state.staff.getOrNull(i)?.name ?: "#$i"} ${state.shifts.getOrNull(k)?.kigou ?: k.toString()}"
        val fixedNames = ArrayList<String>()
        var dayMatchingApplied = 0
        var flexibleDayApplied = 0

        // [頭打ちの理由を可視化] 対象(staff,shift)ごとに、何が原因で付け替えが不成立だったかを集計。
        //   希望固定=movableで即除外・禁止連続=makesForbiddenRunで即除外・候補なし=findCovUChainがnull・
        //   range後回し=findCovUChainは成立したが使った候補がrangeAvoid該当(=自身の新規high違反を招く)
        //   だった・不採用=chainは成立したがisBetterに拒否された、の5分類。最も多い理由を「残存:」へ表示。
        val blockStats = HashMap<Pair<Int, Int>, MutableMap<String, Int>>()
        fun recordBlock(target: Pair<Int, Int>, reason: String) {
            blockStats.getOrPut(target) { HashMap() }.merge(reason, 1, Int::plus)
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] tryRelocate が単独では不採用だった候補を蓄積し
        //   末尾で束ねる（手M/手Fは既にそれ自体が多職員同時最適化のため対象外＝スコープ限定）。
        val combinable = ArrayList<CombinatorialRepair.Candidate>()

        // [玉突き連鎖つき1セル付け替え] day j の staff i を fromK から toK へ動かす。fromK 側の被覆が
        //   悪化するなら findCovUChain で埋め直す。採用ならtrue（bestRep/appliedは呼び出し側で更新済み）。
        fun tryRelocate(target: Pair<Int, Int>, i: Int, j: Int, fromK: Int, toK: Int): Boolean {
            if (!movable(i, j)) { recordBlock(target, "希望固定"); return false }
            if (p.makesForbiddenRun(work, i, j, toK)) { recordBlock(target, "禁止連続"); return false }
            var cnt = 0
            for (s in 0 until p.S) if (work[s][j] == fromK) cnt++
            val needsChain = p.covUCell(fromK, j, cnt - 1) > p.covUCell(fromK, j, cnt)
            // [厳密ピン保護] i(・玉突き相手)の回数変更がstaffRange厳密ピン(lo==hi)を新たに崩す候補は
            //   不採用にする（keep-best/重みは不変・追加ガードのみ）。
            val workBeforeRelocate = work.copy2D()
            work[i][j] = toK
            if (!needsChain) {
                val rep = UnifiedViolationChecker.check(state, work)
                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRelocate, work)) { bestRep = rep; applied++; return true }
                work[i][j] = fromK
                combinable.add(CombinatorialRepair.Candidate(
                    listOf(intArrayOf(i, j, toK)), "tryRelocate", label(target.first, target.second)))
                recordBlock(target, "不採用")
                return false
            }
            val chain = findCovUChain(p, work, fromK, j, rng, exclude = i,
                rangeAvoid = { st, fk -> exceedsOwnRangeHi(p, work, st, fk) })
            if (chain == null) { work[i][j] = fromK; recordBlock(target, "候補なし"); return false }
            val usedAvoided = chain.any { mv -> exceedsOwnRangeHi(p, work, mv[0], mv[2]) }
            val oldVals = IntArray(chain.size) { work[chain[it][0]][chain[it][1]] }
            chain.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
            val rep = UnifiedViolationChecker.check(state, work)
            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRelocate, work)) { bestRep = rep; applied++; return true }
            for (idx in chain.indices) work[chain[idx][0]][chain[idx][1]] = oldVals[idx]
            work[i][j] = fromK
            combinable.add(CombinatorialRepair.Candidate(
                listOf(intArrayOf(i, j, toK)) + chain, "tryRelocate", label(target.first, target.second)))
            recordBlock(target, if (usedAvoided) "range後回し" else "不採用")
            return false
        }

        // [複数ターゲット同時解決=ユーザー指示「賢く深く網羅的に」・grilling確定] 同一シフトkについて
        //   high(超過)のhiとlow(不足)のloが両方存在する場合、findCovUChainの玉突き探索を経由せず、
        //   直接のペアスワップ(hiのk保有日を1日、loへ振替え・loの元シフトをhiが引き受ける)を最優先で
        //   試す。被覆(covU/covO)は完全保存(同日2者の役割入替のみ)のため、玉突き連鎖が構造的に見つから
        //   ない(=「候補なし」)局面でも確実に解決できる（桒澤美幸のAｱ超過×他職員のAｱ不足のような、
        //   同一シフトの過不足ペアに直接効く。RangePolishの`findCovUChain`頭打ちを回避する第2の経路）。
        fun tryPairSwap(hi: Int, k: Int, lo: Int): Boolean {
            for (j in 0 until p.T) {
                if (shouldStop()) return false
                if (work[hi][j] != k || !movable(hi, j) || !movable(lo, j)) continue
                val loK = work[lo][j]
                if (loK == k || loK !in 0 until p.K) continue
                if (!p.canDo(hi, loK) || !p.canDo(lo, k)) continue
                if (p.makesForbiddenRun(work, hi, j, loK) || p.makesForbiddenRun(work, lo, j, k)) continue
                val workBeforeSwap = work.copy2D()
                work[hi][j] = loK; work[lo][j] = k
                val rep = UnifiedViolationChecker.check(state, work)
                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeSwap, work)) { bestRep = rep; applied++; return true }
                work[hi][j] = k; work[lo][j] = loK
            }
            return false
        }

        /**
         * 手M: 対象日の全職員を最小費用完全割当で同時に組み替える。
         *
         * - `hi` は当該日の k を必ず手放す。
         * - `receiver` は当該日の k を必ず受け取る。
         * - その日のシフトtokenは並べ替えるだけなので日別人数は完全保存。
         * - receiverごとに完全割当を解き、full checkerで最良の1案だけ採用。
         */
        fun tryExactDayMatching(target: Pair<Int, Int>, hi: Int, k: Int): Boolean {
            if (p.S <= 1 || p.T <= 0) return false

            val counts = Array(p.S) { IntArray(p.K) }
            for (i in 0 until p.S) for (j in 0 until p.T) {
                val kk = work[i][j]
                if (kk in 0 until p.K) counts[i][kk]++
            }
            val flex = IntArray(p.S) { p.allowedShiftsForStaff(it).size }

            fun rangePenalty(i: Int, kk: Int, count: Int): Long {
                var out = 0L
                val lo = p.rangeLo[i][kk]
                val hiLim = p.rangeHi[i][kk]
                if (lo != Int.MIN_VALUE && count < lo) out += (lo - count).toLong() * 90L
                if (hiLim != Int.MAX_VALUE && count > hiLim) out += (count - hiLim).toLong() * 45L
                return out
            }

            fun rowCost(i: Int, oldK: Int, newK: Int): Long {
                var out = 0L
                for (kk in 0 until p.K) {
                    var c = counts[i][kk]
                    if (newK != oldK) {
                        if (kk == oldK) c--
                        if (kk == newK) c++
                    }
                    out += rangePenalty(i, kk, c)
                    val apt = p.apt[i][kk]
                    if (apt >= 0) out += (if (c >= apt) c - apt else apt - c).toLong()
                }
                // 同品質なら短い循環を優先し、不要な大規模入替えを避ける。
                if (newK != oldK) out += 2L
                // target shiftの偏在を軽く抑える。明示rangeが無い一般代用者同士のtie-break。
                if (newK == k) out += counts[i][k].toLong()
                return out
            }

            fun receiverRoom(i: Int): Int {
                val hiLim = p.rangeHi[i][k]
                return if (hiLim == Int.MAX_VALUE) 10_000 else hiLim - counts[i][k]
            }

            data class DayPlan(
                val day: Int,
                val shifts: IntArray,
                val report: ViolationReport,
                val changed: Int,
                val heuristic: Long,
            )

            var bestPlan: DayPlan? = null
            var trials = 0
            // 実データ10名×31日では全候補を網羅。大規模データでも後処理予算を食い潰さない上限。
            val maxTrials = 128

            for (j in 0 until p.T) {
                if (shouldStop() || trials >= maxTrials) break
                if (work[hi][j] != k || !movable(hi, j)) continue
                val tokens = IntArray(p.S) { work[it][j] }
                // [3.278.0/監査修正] -1(正規化センチネル)トークンを含む日は当該列が全行INF＝Hungarianが必ず
                //   null になるのに、旧実装は receiver 1件ごとに trials(上限128)を浪費していた。日ごと事前スキップ。
                if (tokens.any { it !in 0 until p.K }) continue

                val rawReceivers = (0 until p.S).filter { r ->
                    r != hi &&
                        work[r][j] != k &&
                        movable(r, j) &&
                        p.canDo(r, k) &&
                        receiverRoom(r) > 0
                }
                if (rawReceivers.isEmpty()) continue
                val maxFlex = rawReceivers.maxOf { flex[it] }
                val receivers = rawReceivers.sortedWith(
                    compareByDescending<Int> { if (bestRep.countViolations["$it,$k"] == "vio-low") 1 else 0 }
                        .thenByDescending { if (flex[it] >= maxFlex - 1) 1 else 0 }
                        .thenByDescending { receiverRoom(it) }
                        .thenBy { counts[it][k] }
                        .thenBy { it },
                )

                for (receiver in receivers) {
                    if (shouldStop() || trials++ >= maxTrials) break
                    val cost = Array(p.S) { LongArray(p.S) { DAY_MATCH_INF } }
                    for (i in 0 until p.S) {
                        val oldK = work[i][j]
                        for (tokenIdx in 0 until p.S) {
                            val newK = tokens[tokenIdx]
                            if (newK !in 0 until p.K) continue
                            if (i == hi && newK == k) continue
                            if (i == receiver && newK != k) continue
                            if (newK != oldK) {
                                if (!movable(i, j) || !p.canDo(i, newK)) continue
                                // [3.278.0/監査修正] 兄弟実装(tryFlexibleDayFlow/C1TemporalFlowPolish)と同じ
                                //   「『希』は希望セルとしてのみ存在させ、最適化が自由生成しない」ガード。手Mだけ
                                //   欠けており、既存の「希」トークンを希望していない別職員へ再割当できていた。
                                if (state.shifts.getOrNull(newK)?.kigou == "希") continue
                                work[i][j] = newK
                                val badRun = p.makesForbiddenRun(work, i, j, newK)
                                work[i][j] = oldK
                                if (badRun) continue
                            }
                            cost[i][tokenIdx] = rowCost(i, oldK, newK)
                        }
                    }

                    val assignment = minCostPerfectAssignment(cost) ?: continue
                    val newDay = IntArray(p.S) { i -> tokens[assignment[i]] }
                    if (newDay[hi] == k || newDay[receiver] != k) continue
                    var changed = 0
                    var heuristic = 0L
                    // [厳密ピン保護] 完全割当は当日のトークンを全職員で並べ替えるため、複数職員の回数を
                    //   同時に変えうる。staffRange厳密ピン(lo==hi)を新たに崩す日案は不採用にする。
                    val workBeforeDayMatch = work.copy2D()
                    for (i in 0 until p.S) {
                        if (newDay[i] != tokens[i]) changed++
                        heuristic += cost[i][assignment[i]]
                        work[i][j] = newDay[i]
                    }
                    val rep = UnifiedViolationChecker.check(state, work)
                    val pinBad = exactPinRegression(p, workBeforeDayMatch, work)
                    for (i in 0 until p.S) work[i][j] = tokens[i]

                    if (!isBetter(rep, bestRep) || pinBad) continue
                    val oldBest = bestPlan
                    val betterPlan = oldBest == null ||
                        isBetter(rep, oldBest.report) ||
                        (rep.hard == oldBest.report.hard &&
                            rep.total == oldBest.report.total &&
                            kotlin.math.abs(rep.weightedScore - oldBest.report.weightedScore) <= 1e-6 &&
                            (heuristic < oldBest.heuristic ||
                                (heuristic == oldBest.heuristic && changed < oldBest.changed)))
                    if (betterPlan) bestPlan = DayPlan(j, newDay, rep, changed, heuristic)
                }
            }

            val plan = bestPlan
            if (plan == null) {
                recordBlock(target, "日割当候補なし")
                return false
            }
            for (i in 0 until p.S) work[i][plan.day] = plan.shifts[i]
            bestRep = plan.report
            applied++
            dayMatchingApplied++
            return true
        }

        /**
         * 手F: 日別シフト多重集合も変えられる最小費用フロー。
         *
         * 手Mは「その日に既に存在するシフトtokenの並替え」なので、美幸Aｱ→B1のように
         * その日にB1 tokenが存在しないケースを表現できない。手Fは各職員から担当可能シフトへ辺を張り、
         * シフト側の1人目・2人目…にcovU/covOの限界費用を与える。これにより
         *   美幸 Aｱ→B1 ＋ 別職員 休/C系→Aｱ
         * のような、日別人数を変える置換を1回の厳密最適化で作る。
         *
         * - 希望/管理者固定セルは現在シフト以外へ移動不可。
         * - 変更先はcanDo必須。希望休「希」は新規生成しない。
         * - c3nはmakesForbiddenRunで辺を除外。ただし直接は禁止連続でも、隣接日(j±1)を本人が
         *   調整すれば崩せる場合は`tryFixForbiddenRunViaAdjacentDay`(3.163.0)で救済し、辺を生かす
         *   （3.246.0・「隣接日連動型」拡張。受取職員自身の隣接日にも同じ救済が及ぶ＝対称）。
         * - staffRange low/high、apt、変更セル数を職員辺費用へ入れる。
         * - covU/covOは人数qに対する凸罰則の限界費用としてシフト→sink辺へ入れる。
         * - 最終採否は必ずUnifiedViolationChecker＋isBetter。近似費用だけでは採用しない
         *   （隣接日の追加手・玉突きも含めた盤面全体で1回評価）。
         */
        fun tryFlexibleDayFlow(
            target: Pair<Int, Int>,
            victim: Int,
            forbiddenK: Int,
            candidateDays: IntArray,
        ): Boolean {
            if (p.S <= 0 || p.K <= 0) return false
            val counts = Array(p.S) { IntArray(p.K) }
            for (i in 0 until p.S) for (j in 0 until p.T) {
                val kk = work[i][j]
                if (kk in 0 until p.K) counts[i][kk]++
            }

            fun rangeAndAptCost(i: Int, oldK: Int, newK: Int): Long {
                var out = 0L
                for (kk in 0 until p.K) {
                    var c = counts[i][kk]
                    if (newK != oldK) {
                        if (kk == oldK) c--
                        if (kk == newK) c++
                    }
                    val lo = p.rangeLo[i][kk]
                    val hi = p.rangeHi[i][kk]
                    if (lo != Int.MIN_VALUE && c < lo) out += (lo - c).toLong() * 90L
                    if (hi != Int.MAX_VALUE && c > hi) out += (c - hi).toLong() * 45L
                    val a = p.apt[i][kk]
                    if (a >= 0) out += kotlin.math.abs(c - a).toLong()
                }
                if (newK != oldK) out += 2L
                return out
            }

            fun dayPenalty(k: Int, j: Int, q: Int): Long =
                p.covUCell(k, j, q).toLong() * 8000L + p.covOCell(k, j, q).toLong()

            data class FlowPlan(
                val day: Int,
                val assignment: IntArray,
                val report: ViolationReport,
                val changed: Int,
                val flowCost: Long,
                val extras: List<IntArray>,
            )

            var bestPlan: FlowPlan? = null
            val days = candidateDays.filter { it in 0 until p.T }.distinct()
            for (j in days) {
                if (shouldStop()) break
                if (work[victim][j] != forbiddenK || !movable(victim, j)) continue
                val oldDay = IntArray(p.S) { work[it][j] }
                // [3.246.0 隣接日連動] (i,newK)ペア単位で「直接は禁止連続だが隣接日調整で救済できるか」を
                // メモ化。j±1は本ループの間ずっと不変(day-jのtrialは他日を触らない)なので日jの間は再利用可。
                val adjacentFix = HashMap<Pair<Int, Int>, List<IntArray>>()

                // primary costを1024倍し、下位10bitだけを決定的tie-breakに使う。
                // 8試行してc42/c1等の非分離制約に対する代替案もfull checkerへ渡す。
                for (trial in 0 until 8) {
                    if (shouldStop()) break
                    val staffCost = Array(p.S) { LongArray(p.K) { FlexibleDayFlow.INF } }
                    for (i in 0 until p.S) {
                        val oldK = oldDay[i]
                        for (newK in 0 until p.K) {
                            if (i == victim && newK == forbiddenK) continue
                            val changed = newK != oldK
                            if (changed) {
                                if (!movable(i, j) || !p.canDo(i, newK)) continue
                                // 「希」は希望セルとしてのみ存在させ、最適化が自由生成しない。
                                if (state.shifts.getOrNull(newK)?.kigou == "希") continue
                                work[i][j] = newK
                                val badRun = p.makesForbiddenRun(work, i, j, newK)
                                work[i][j] = oldK
                                if (badRun) {
                                    val key = i to newK
                                    val fix = adjacentFix.getOrPut(key) {
                                        tryFixForbiddenRunViaAdjacentDay(p, work, i, j, newK, rng) ?: emptyList()
                                    }
                                    if (fix.isEmpty()) continue
                                }
                            } else if (i == victim && !p.canDo(i, newK)) {
                                // groupViol対象をそのまま残す辺は禁止。他職員の固定済み不正セルは
                                // この1手の実行可能性を壊さないため現状維持だけ許す。
                                continue
                            }
                            val primary = rangeAndAptCost(i, oldK, newK)
                            val tie = ((i * 131 + newK * 31 + trial * 17) and 1023).toLong()
                            staffCost[i][newK] = primary * 1024L + tie
                        }
                    }

                    val marginal = Array(p.K) { k ->
                        LongArray(p.S) { q0 ->
                            val q = q0 + 1
                            (dayPenalty(k, j, q) - dayPenalty(k, j, q - 1)) * 1024L
                        }
                    }
                    val solved = FlexibleDayFlow.solve(staffCost, marginal) ?: continue
                    if (solved.assignment[victim] == forbiddenK) continue

                    // 選ばれた(i,newK)のうち禁止連続の隣接日救済が要ったものを1件の候補として合流。
                    val extras = ArrayList<IntArray>()
                    for (i in 0 until p.S) {
                        val newK = solved.assignment[i]
                        if (newK == oldDay[i]) continue
                        adjacentFix[i to newK]?.let { extras.addAll(it) }
                    }

                    var changedCount = 0
                    // [厳密ピン保護] 柔軟日フローも当日の人数構成と隣接日調整(extras)を同時に変えるため、
                    //   複数職員の回数を同時に変えうる。staffRange厳密ピン(lo==hi)を新たに崩す案は不採用。
                    val workBeforeFlow = work.copy2D()
                    for (i in 0 until p.S) {
                        if (solved.assignment[i] != oldDay[i]) changedCount++
                        work[i][j] = solved.assignment[i]
                    }
                    val extraOld = IntArray(extras.size) { work[extras[it][0]][extras[it][1]] }
                    extras.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
                    val rep = UnifiedViolationChecker.check(state, work)
                    val pinBad = exactPinRegression(p, workBeforeFlow, work)
                    for (idx in extras.indices) work[extras[idx][0]][extras[idx][1]] = extraOld[idx]
                    for (i in 0 until p.S) work[i][j] = oldDay[i]
                    if (!isBetter(rep, bestRep) || pinBad) continue

                    val oldBest = bestPlan
                    val betterPlan = oldBest == null ||
                        isBetter(rep, oldBest.report) ||
                        (rep.hard == oldBest.report.hard &&
                            rep.total == oldBest.report.total &&
                            kotlin.math.abs(rep.weightedScore - oldBest.report.weightedScore) <= 1e-6 &&
                            (changedCount < oldBest.changed ||
                                (changedCount == oldBest.changed && solved.cost < oldBest.flowCost)))
                    if (betterPlan) bestPlan = FlowPlan(j, solved.assignment, rep, changedCount, solved.cost, extras)
                }
            }

            val plan = bestPlan
            if (plan == null) {
                recordBlock(target, "柔軟日割当候補なし")
                return false
            }
            for (i in 0 until p.S) work[i][plan.day] = plan.assignment[i]
            plan.extras.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
            bestRep = plan.report
            applied++
            flexibleDayApplied++
            return true
        }

        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false

            // [手F/groupViol] staffRangeのhigh表示に依存せず、担当不可セルを直接対象にする。
            // 添付データの美幸AｱはstaffRange[3,4]が無くてもgroupShift上で担当不可なのでここで5日全て拾う。
            val groupTargets = ArrayList<Triple<Int, Int, Int>>()
            for (i in 0 until p.S) for (j in 0 until p.T) {
                val k = work[i][j]
                if (k in 0 until p.K && !p.canDo(i, k)) groupTargets.add(Triple(i, j, k))
            }
            for ((i, j, k) in groupTargets) {
                if (shouldStop()) break
                if (work[i][j] != k || p.canDo(i, k)) continue
                val target = i to k
                if (!movable(i, j)) {
                    recordBlock(target, "担当不可セルが希望/管理者固定")
                    continue
                }
                if (tryFlexibleDayFlow(target, i, k, intArrayOf(j))) {
                    improved = true
                    fixedNames.add("${label(i, k)} ${j + 1}日")
                }
            }

            // [3.278.0/監査修正] pass 0 でも直前の groupTargets ループ(手F)が盤面を変更済み(improved)なら
            //   before は陳腐＝解消済みターゲットへの空振り・新規違反の見落としを防ぐため再検査する。
            val rep0 = if (pass == 0 && !improved) before else UnifiedViolationChecker.check(state, work)
            val highTargets = ArrayList<Pair<Int, Int>>()
            val lowTargets = ArrayList<Pair<Int, Int>>()
            for ((key, cls) in rep0.countViolations) {
                val parts = key.split(",")
                val i = parts.getOrNull(0)?.toIntOrNull() ?: continue
                val k = parts.getOrNull(1)?.toIntOrNull() ?: continue
                when (cls) {
                    "vio-high" -> highTargets.add(i to k)
                    "vio-low" -> lowTargets.add(i to k)
                }
            }
            if (highTargets.isEmpty() && lowTargets.isEmpty()) break

            // HIGH(超過): shift k の保有日を他の担当可能シフトへ動かす。
            for ((i, k) in highTargets) {
                if (shouldStop()) break
                val target = i to k
                var done = false
                // [手M→手F] まず日別人数を保存する完全割当。無ければ日別人数も最適化するフローへ拡張。
                // 同じ(i,k)が上限を複数回超えていても、この1パス内で上限まで反復して落とす。
                val hiLim = p.rangeHi[i][k]
                var guard = 0
                while (hiLim != Int.MAX_VALUE && work[i].count { it == k } > hiLim && guard++ < p.T) {
                    val fixedOne = tryExactDayMatching(target, i, k) ||
                        tryFlexibleDayFlow(
                            target, i, k,
                            (0 until p.T).filter { j -> work[i][j] == k && movable(i, j) }.toIntArray(),
                        )
                    if (!fixedOne) break
                    improved = true
                    done = true
                    fixedNames.add(label(i, k))
                }
                if (done) continue
                // [複数ターゲット同時解決] まず同一シフトkのlow(不足)職員との直接ペアスワップを試す
                //   （findCovUChain経由の玉突きより優先＝被覆完全保存で確実に解決できる）。
                for ((lo, lk) in lowTargets) {
                    if (done || shouldStop()) break
                    if (lk != k || lo == i) continue
                    if (tryPairSwap(i, k, lo)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                }
                if (done) continue
                for (j in 0 until p.T) {
                    if (done || shouldStop()) break
                    if (work[i][j] != k) continue
                    for (alt in p.allowedShiftsForStaff(i)) {
                        if (done || shouldStop()) break
                        if (alt == k) continue
                        if (tryRelocate(target, i, j, k, alt)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                    }
                }
            }
            // LOW(不足): shift k を保有していない日のうち1日をshift kへ動かす。
            for ((i, k) in lowTargets) {
                if (shouldStop()) break
                if (!p.canDo(i, k)) continue
                val target = i to k
                var done = false
                // [複数ターゲット同時解決] まず同一シフトkのhigh(超過)職員との直接ペアスワップを試す
                //   （HIGHループで既に解決済みのペアはtryPairSwap内でその日を再訪しても無害＝重複コスト
                //   のみ）。
                for ((hi, hk) in highTargets) {
                    if (done || shouldStop()) break
                    if (hk != k || hi == i) continue
                    if (tryPairSwap(hi, k, i)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                }
                if (done) continue
                for (j in 0 until p.T) {
                    if (done || shouldStop()) break
                    val oldK = work[i][j]
                    if (oldK == k || oldK !in 0 until p.K) continue
                    if (tryRelocate(target, i, j, oldK, k)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                }
            }
            pass++
            if (!improved) break
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames より前に実行し、結合で解消した箇所が
        //   「残存」に残らないようにする。
        val rangeCombStats = CombinatorialRepair.Stats()
        bestRep = CombinatorialRepair.combineAndApply(
            state, work, bestRep, combinable.asReversed(), ::isBetter, shouldStop = shouldStop, stats = rangeCombStats, p = p,
        )
        applied += rangeCombStats.combosAccepted
        // [ログから職員が分かるように・頭打ちの理由を可視化] 研磨後もなお残っている(staff,shift)を、
        //   最も多かった頭打ち理由(希望固定/禁止連続/候補なし/range後回し/不採用)付きで列挙。
        val stuckNames = bestRep.countViolations.entries
            .filter { it.value == "vio-high" || it.value == "vio-low" }
            .mapNotNull { (key, _) ->
                val parts = key.split(",")
                val i = parts.getOrNull(0)?.toIntOrNull() ?: return@mapNotNull null
                val k = parts.getOrNull(1)?.toIntOrNull() ?: return@mapNotNull null
                val reasons = blockStats[i to k]
                val top = reasons?.maxByOrNull { it.value }
                if (top != null) "${label(i, k)}(${top.key}×${top.value})" else label(i, k)
            }
        val rangeCombSummary = rangeCombStats.summary()
        val logs = listOf(MirrorLog(tag = "RangePolish",
            message = "個人回数(low/high)玉突き研磨: low ${before.breakdown["low"] ?: 0}->${bestRep.breakdown["low"] ?: 0} / high ${before.breakdown["high"] ?: 0}->${bestRep.breakdown["high"] ?: 0} / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回" +
                "（日割当:$dayMatchingApplied / 柔軟日割当:$flexibleDayApplied）" +
                (if (applied == 0 && ((before.breakdown["low"] ?: 0) + (before.breakdown["high"] ?: 0)) > 0) " [頭打ち=改善手なし]" else "") +
                (if (fixedNames.isNotEmpty()) " 対象: ${fixedNames.joinToString(", ")}" else "") +
                (if (stuckNames.isNotEmpty()) " 残存: ${stuckNames.joinToString(", ")}" else "") +
                (if (rangeCombSummary.isNotEmpty()) " / $rangeCombSummary" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [AptPolish・適切回数(apt, 重み1)専用の研磨パス] ユーザー指示「専用の研磨パスAptPolish的なものを
     * 賢く深く網羅的に作る」（grillingで確定: ①自己振替最優先 ②同一グループ内の相互交換(同日1対1・
     * 被覆総量保存で安全) ③RangePolish型の玉突きチェーン、の順で試す）。
     *
     * 動機（大島愛の実例）: 群目標(groupShiftApt)に対しaptHigh(超過)とaptLow(不足)が同一職員内に同時に
     * 存在するケース（休=超過・Pｼ=不足）は、本人内で1日分を振替えるだけで両方が同時に改善する「タダの
     * 交換」のはずだが、apt(重み1)はRSI探索中のfocus選択で軽視されやすく(3.169.0)、専用研磨が無いまま
     * 残っていた。
     *
     * アンカー: `report.countViolations`（"i,k"→"vio-aptHigh"/"vio-aptLow"、markCountの重み優先解決済）
     * から違反している(staff,shift)ペアを列挙。
     * 手①自己振替: 同一職員が別のシフトでaptLow(逆方向)を持つ場合、その2シフト間で1日を直接付け替える
     *   （他人に一切影響しない最安全な手）。付け替え元/先双方の被覆(covUCell)を悪化させない日のみ候補
     *   にする（悪化するならチェーンを使わず単に見送り＝真に無償の手のみを対象にする）。
     * 手②相互交換: 同一グループ(canDo完全一致)内に、同じシフトで逆方向のapt不均衡を持つ相手がいれば、
     *   同日の2人の割当をまるごと入替える（同日swap＝被覆総量保存＝構造的に安全、BlockSwapPolishと
     *   同型の安全性。相手のcanDoは同一グループのため保証済み）。
     * 手③玉突きチェーン: 上記いずれでも解消しない残りは、RangePolishと同型のfindCovUChain（候補が
     *   自身の新規apt違反を招くなら後回しにするavoid述語つき）で任意の担当可能シフトへ移す。
     * 採否はisBetter(hard→total→weighted)keep-best＝退化不能。全手とも希望固定(movable)・禁止連続
     * (makesForbiddenRun)を事前ガード。
     */
    fun applyAptPolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }, seed: Long = 0xA97L): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        fun label(i: Int, k: Int) = "${state.staff.getOrNull(i)?.name ?: "#$i"} ${state.shifts.getOrNull(k)?.kigou ?: k.toString()}"
        val fixedNames = ArrayList<String>()
        // [汎用玉突き結合フレームワーク, 3.249.0] tryChainRelocate(手③)が単独では不採用だった候補を
        //   蓄積し末尾で束ねる。
        val combinable = ArrayList<CombinatorialRepair.Candidate>()

        // [玉突きチェーンのavoid述語] 候補がfillShiftを1つ得ると自身のapt目標からちょうど新規に
        //   乖離するか（既に乖離済みなら「まだ動いていない」ので中立扱い＝対象外）。
        fun worsensOwnApt(staff: Int, fillShift: Int): Boolean {
            val t = p.apt[staff][fillShift]
            if (t < 0) return false
            var c = 0
            for (jj in 0 until p.T) if (work[staff][jj] == fillShift) c++
            return c == t
        }

        // [厳密ピン保護] 本パスの全手は i(・相手)の回数を直接変える(apt/fair研磨の本質)ため、staffRange
        //   厳密ピン(lo==hi)を新たに崩す候補だけは不採用にする（keep-best/重みは不変・追加ガードのみ）。
        fun applyAndCheck(i: Int, j: Int, fromK: Int, toK: Int): Boolean {
            val workBefore = work.copy2D()
            work[i][j] = toK
            val rep = UnifiedViolationChecker.check(state, work)
            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBefore, work)) { bestRep = rep; applied++; return true }
            work[i][j] = fromK
            return false
        }

        // 手①: 自身の中でfromK(過多)→toK(過少)への1日付け替え。被覆非悪化の日のみ候補にする。
        fun trySelfSwap(i: Int, fromK: Int, toK: Int): Boolean {
            for (j in 0 until p.T) {
                if (shouldStop()) return false
                if (work[i][j] != fromK || !movable(i, j)) continue
                if (p.makesForbiddenRun(work, i, j, toK)) continue
                var cntFrom = 0; var cntTo = 0
                for (s in 0 until p.S) { if (work[s][j] == fromK) cntFrom++; if (work[s][j] == toK) cntTo++ }
                if (p.covUCell(fromK, j, cntFrom - 1) > p.covUCell(fromK, j, cntFrom)) continue
                if (p.covUCell(toK, j, cntTo + 1) > p.covUCell(toK, j, cntTo)) continue
                if (applyAndCheck(i, j, fromK, toK)) return true
            }
            return false
        }

        // 手②: 同一グループ内で同日の2人の割当をまるごと入替（被覆総量保存＝安全）。
        fun tryMutualSwap(i: Int, i2: Int, sharedK: Int): Boolean {
            for (j in 0 until p.T) {
                if (shouldStop()) return false
                val a = work[i][j]; val b = work[i2][j]
                if (a != sharedK || b == sharedK) continue
                if (!movable(i, j) || !movable(i2, j)) continue
                if (p.makesForbiddenRun(work, i, j, b) || p.makesForbiddenRun(work, i2, j, a)) continue
                val workBefore = work.copy2D()
                work[i][j] = b; work[i2][j] = a
                val rep = UnifiedViolationChecker.check(state, work)
                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBefore, work)) { bestRep = rep; applied++; return true }
                work[i][j] = a; work[i2][j] = b
            }
            return false
        }

        // 手③: RangePolish型の玉突きチェーン。
        fun tryChainRelocate(i: Int, j: Int, fromK: Int, toK: Int): Boolean {
            if (!movable(i, j) || p.makesForbiddenRun(work, i, j, toK)) return false
            var cnt = 0
            for (s in 0 until p.S) if (work[s][j] == fromK) cnt++
            val needsChain = p.covUCell(fromK, j, cnt - 1) > p.covUCell(fromK, j, cnt)
            val workBeforeRelocate = work.copy2D()
            work[i][j] = toK
            if (!needsChain) {
                val rep = UnifiedViolationChecker.check(state, work)
                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRelocate, work)) { bestRep = rep; applied++; return true }
                work[i][j] = fromK
                combinable.add(CombinatorialRepair.Candidate(listOf(intArrayOf(i, j, toK)), "AptChain", label(i, fromK)))
                return false
            }
            val chain = findCovUChain(p, work, fromK, j, rng, exclude = i,
                rangeAvoid = { st, fk -> worsensOwnApt(st, fk) })
            if (chain == null) { work[i][j] = fromK; return false }
            val oldVals = IntArray(chain.size) { work[chain[it][0]][chain[it][1]] }
            chain.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
            val rep = UnifiedViolationChecker.check(state, work)
            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRelocate, work)) { bestRep = rep; applied++; return true }
            for (idx in chain.indices) work[chain[idx][0]][chain[idx][1]] = oldVals[idx]
            work[i][j] = fromK
            combinable.add(CombinatorialRepair.Candidate(listOf(intArrayOf(i, j, toK)) + chain, "AptChain", label(i, fromK)))
            return false
        }

        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            val rep0 = if (pass == 0) before else UnifiedViolationChecker.check(state, work)
            val highTargets = ArrayList<Pair<Int, Int>>()
            val lowTargets = ArrayList<Pair<Int, Int>>()
            for ((key, cls) in rep0.countViolations) {
                val parts = key.split(",")
                val i = parts.getOrNull(0)?.toIntOrNull() ?: continue
                val k = parts.getOrNull(1)?.toIntOrNull() ?: continue
                when (cls) {
                    "vio-aptHigh" -> highTargets.add(i to k)
                    "vio-aptLow" -> lowTargets.add(i to k)
                }
            }
            if (highTargets.isEmpty() && lowTargets.isEmpty()) break

            for ((i, k) in highTargets) {
                if (shouldStop()) break
                var done = false
                // 手①: 自身の別シフトでaptLowのものへ振替（同一(fromK,toK)ペアで解消するまで反復＝
                //   RangePolishの「上限まで反復して落とす」と同型に統一。他者に一切影響しない自己完結の
                //   手のためisBetterが認める限り繰り返して安全。旧実装は1回成功したら次のhighTargetsへ
                //   移っており、excess/deficitが複数単位ある職員は1パスにつき1単位しか解消できず、
                //   予算超過で後続パスが打ち切られると大きな乖離が残存し続けていた）。
                for (k2 in 0 until p.K) {
                    if (shouldStop()) break
                    if (k2 == k || !p.canDo(i, k2)) continue
                    if (lowTargets.none { it.first == i && it.second == k2 }) continue
                    while (trySelfSwap(i, k, k2)) { improved = true; done = true }
                }
                if (done) fixedNames.add(label(i, k))
                // 手②: 同一グループで逆方向(aptLow)の相手と相互交換。
                if (!done) {
                    for (i2 in 0 until p.S) {
                        if (done || shouldStop()) break
                        if (i2 == i || p.sgrp[i2] != p.sgrp[i]) continue
                        if (lowTargets.none { it.first == i2 && it.second == k }) continue
                        if (tryMutualSwap(i, i2, k)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                    }
                }
                // 手③: 玉突きチェーンで任意の担当可能シフトへ。
                if (!done) {
                    for (j in 0 until p.T) {
                        if (done || shouldStop()) break
                        if (work[i][j] != k) continue
                        for (alt in p.allowedShiftsForStaff(i)) {
                            if (done || shouldStop()) break
                            if (alt == k) continue
                            if (tryChainRelocate(i, j, k, alt)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                        }
                    }
                }
            }
            // 単独aptLow(自己振替/相互交換で解消しなかった残り)を玉突きチェーンで埋める。
            for ((i, k) in lowTargets) {
                if (shouldStop()) break
                if (!p.canDo(i, k)) continue
                var done = false
                for (j in 0 until p.T) {
                    if (done || shouldStop()) break
                    val oldK = work[i][j]
                    if (oldK == k || oldK !in 0 until p.K) continue
                    if (tryChainRelocate(i, j, oldK, k)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                }
            }
            pass++
            if (!improved) break
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames より前に実行し、結合で解消した箇所が
        //   「残存」に残らないようにする。
        val aptCombStats = CombinatorialRepair.Stats()
        bestRep = CombinatorialRepair.combineAndApply(
            state, work, bestRep, combinable.asReversed(), ::isBetter, shouldStop = shouldStop, stats = aptCombStats, p = p,
        )
        applied += aptCombStats.combosAccepted
        val stuckNames = bestRep.countViolations.entries
            .filter { it.value == "vio-aptHigh" || it.value == "vio-aptLow" }
            .mapNotNull { (key, _) ->
                val parts = key.split(",")
                val i = parts.getOrNull(0)?.toIntOrNull() ?: return@mapNotNull null
                val k = parts.getOrNull(1)?.toIntOrNull() ?: return@mapNotNull null
                label(i, k)
            }
        val aptCombSummary = aptCombStats.summary()
        val logs = listOf(MirrorLog(tag = "AptPolish",
            message = "適切回数(apt)研磨: apt ${before.breakdown["apt"] ?: 0}->${bestRep.breakdown["apt"] ?: 0} / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回" +
                (if (applied == 0 && (before.breakdown["apt"] ?: 0) > 0) " [頭打ち=改善手なし]" else "") +
                (if (fixedNames.isNotEmpty()) " 対象: ${fixedNames.joinToString(", ")}" else "") +
                (if (stuckNames.isNotEmpty()) " 残存: ${stuckNames.joinToString(", ")}" else "") +
                (if (aptCombSummary.isNotEmpty()) " / $aptCombSummary" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [FairPolish・グループ内公平化(fair, 重み1)専用の研磨パス] ユーザー指示「c42/c42s以外にも
     * 『動かせるか』専用オペレータの欠如が無いか棚卸しする」で発見（棚卸し結果はユーザー承認済み）。
     * fair は群×担当ONシフトごとにメンバー回数の round(平均)からのL1偏差和で、apt(3.223.0)と
     * ほぼ同型の違反構造。しかし既存 applyGroupShiftEqualizePolish は同日2者スワップ＋分散指標での
     * 山登りのみでチェーン救済が無く、交換相手が構造的に不在（希望固定/禁止連続/候補不足）だと
     * 頭打ちする、covO/c41/c41s/c42/c42s/apt と同型の穴だった。AptPolish(3.223.0)と同一の3段構成
     * （①自己振替 ②同一グループ内相互交換 ③玉突きチェーン）をfair向けに移植する。
     *
     * fair の目標(tgt)は「その時点のグループ合計の round(平均)」で apt の固定目標と異なり、1日の
     * 付け替えごとに動く。手①②③はいずれも候補選定のスナップショット近似（各手を試す時点で
     * counts/tgt を再計算）でよく、最終的な採否は常に isBetter(実目的関数)が担うため、tgt の近似が
     * ズレても安全性は損なわれない（見逃しても isBetter が拒否するだけ・過大選定しても isBetter が
     * 拒否するだけ）。採否はisBetter(hard→total→weighted)keep-best＝退化不能。全手とも希望固定
     * (movable)・禁止連続(makesForbiddenRun)を事前ガード。
     */
    fun applyFairPolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }, seed: Long = 0xFA12L): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        fun label(i: Int, k: Int) = "${state.staff.getOrNull(i)?.name ?: "#$i"} ${state.shifts.getOrNull(k)?.kigou ?: k.toString()}"
        val fixedNames = ArrayList<String>()
        // [汎用玉突き結合フレームワーク, 3.249.0] tryChainRelocate(手③)が単独では不採用だった候補を
        //   蓄積し末尾で束ねる。
        val combinable = ArrayList<CombinatorialRepair.Candidate>()

        fun fairTarget(g: Int, k: Int, counts: Array<IntArray>): Int {
            val mem = p.groupMembers.getOrNull(g) ?: return 0
            if (mem.isEmpty()) return 0
            var sum = 0
            for (x in mem) sum += counts[x][k]
            return Math.round(sum.toDouble() / mem.size).toInt()
        }

        // [玉突きチェーンのavoid述語] 候補がfillShiftを1つ得ると、候補自身の群目標(スナップショット近似)
        //   からちょうど新規に乖離するか（既に乖離済みなら中立扱い＝対象外）。
        fun worsensOwnFair(staff: Int, fillShift: Int): Boolean {
            val g = p.sgrp.getOrNull(staff) ?: return false
            if (g !in p.bucket.indices || fillShift !in p.bucket[g]) return false
            val counts = countMatrix(p, work)
            val tgt = fairTarget(g, fillShift, counts)
            return counts[staff][fillShift] == tgt
        }

        // [厳密ピン保護] 本パスの全手は i(・相手)の回数を直接変える(apt/fair研磨の本質)ため、staffRange
        //   厳密ピン(lo==hi)を新たに崩す候補だけは不採用にする（keep-best/重みは不変・追加ガードのみ）。
        fun applyAndCheck(i: Int, j: Int, fromK: Int, toK: Int): Boolean {
            val workBefore = work.copy2D()
            work[i][j] = toK
            val rep = UnifiedViolationChecker.check(state, work)
            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBefore, work)) { bestRep = rep; applied++; return true }
            work[i][j] = fromK
            return false
        }

        // 手①: 自身の中でfromK(過多)→toK(過少)への1日付け替え。被覆非悪化の日のみ候補にする。
        fun trySelfSwap(i: Int, fromK: Int, toK: Int): Boolean {
            for (j in 0 until p.T) {
                if (shouldStop()) return false
                if (work[i][j] != fromK || !movable(i, j)) continue
                if (p.makesForbiddenRun(work, i, j, toK)) continue
                var cntFrom = 0; var cntTo = 0
                for (s in 0 until p.S) { if (work[s][j] == fromK) cntFrom++; if (work[s][j] == toK) cntTo++ }
                if (p.covUCell(fromK, j, cntFrom - 1) > p.covUCell(fromK, j, cntFrom)) continue
                if (p.covUCell(toK, j, cntTo + 1) > p.covUCell(toK, j, cntTo)) continue
                if (applyAndCheck(i, j, fromK, toK)) return true
            }
            return false
        }

        // 手②: 同一グループ内で同日の2人の割当をまるごと入替（被覆総量保存＝安全）。
        fun tryMutualSwap(i: Int, i2: Int, sharedK: Int): Boolean {
            for (j in 0 until p.T) {
                if (shouldStop()) return false
                val a = work[i][j]; val b = work[i2][j]
                if (a != sharedK || b == sharedK) continue
                if (!movable(i, j) || !movable(i2, j)) continue
                if (!p.canDo(i, b) || !p.canDo(i2, a)) continue
                if (p.makesForbiddenRun(work, i, j, b) || p.makesForbiddenRun(work, i2, j, a)) continue
                val workBefore = work.copy2D()
                work[i][j] = b; work[i2][j] = a
                val rep = UnifiedViolationChecker.check(state, work)
                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBefore, work)) { bestRep = rep; applied++; return true }
                work[i][j] = a; work[i2][j] = b
            }
            return false
        }

        // 手③: RangePolish/AptPolish型の玉突きチェーン。
        fun tryChainRelocate(i: Int, j: Int, fromK: Int, toK: Int): Boolean {
            if (!movable(i, j) || p.makesForbiddenRun(work, i, j, toK)) return false
            var cnt = 0
            for (s in 0 until p.S) if (work[s][j] == fromK) cnt++
            val needsChain = p.covUCell(fromK, j, cnt - 1) > p.covUCell(fromK, j, cnt)
            val workBeforeRelocate = work.copy2D()
            work[i][j] = toK
            if (!needsChain) {
                val rep = UnifiedViolationChecker.check(state, work)
                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRelocate, work)) { bestRep = rep; applied++; return true }
                work[i][j] = fromK
                combinable.add(CombinatorialRepair.Candidate(listOf(intArrayOf(i, j, toK)), "FairChain", label(i, fromK)))
                return false
            }
            val chain = findCovUChain(p, work, fromK, j, rng, exclude = i,
                rangeAvoid = { st, fk -> worsensOwnFair(st, fk) })
            if (chain == null) { work[i][j] = fromK; return false }
            val oldVals = IntArray(chain.size) { work[chain[it][0]][chain[it][1]] }
            chain.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
            val rep = UnifiedViolationChecker.check(state, work)
            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeRelocate, work)) { bestRep = rep; applied++; return true }
            for (idx in chain.indices) work[chain[idx][0]][chain[idx][1]] = oldVals[idx]
            work[i][j] = fromK
            combinable.add(CombinatorialRepair.Candidate(listOf(intArrayOf(i, j, toK)) + chain, "FairChain", label(i, fromK)))
            return false
        }

        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            val rep0 = if (pass == 0) before else UnifiedViolationChecker.check(state, work)
            val locs = rep0.distLocations["fair"].orEmpty()
            if (locs.isEmpty()) break
            val counts = countMatrix(p, work)
            val highTargets = ArrayList<Pair<Int, Int>>()   // (staff, shift) 過多
            val lowTargets = ArrayList<Pair<Int, Int>>()    // (staff, shift) 過少
            for (loc in locs) {
                val x = loc.getOrNull(0) ?: continue
                val k = loc.getOrNull(1) ?: continue
                if (x !in 0 until p.S || k !in 0 until p.K) continue
                val g = p.sgrp.getOrNull(x) ?: continue
                if (g !in p.bucket.indices) continue
                val tgt = fairTarget(g, k, counts)
                when {
                    counts[x][k] > tgt -> highTargets.add(x to k)
                    counts[x][k] < tgt -> lowTargets.add(x to k)
                }
            }
            if (highTargets.isEmpty() && lowTargets.isEmpty()) break

            for ((i, k) in highTargets) {
                if (shouldStop()) break
                var done = false
                // 手①: 自身の別シフトでfairLow(逆方向)のものへ振替（AptPolishと同型に統一。同一
                //   (fromK,toK)ペアで解消するまで反復。isBetterが認める限り繰り返して安全）。
                for (k2 in 0 until p.K) {
                    if (shouldStop()) break
                    if (k2 == k || !p.canDo(i, k2)) continue
                    if (lowTargets.none { it.first == i && it.second == k2 }) continue
                    while (trySelfSwap(i, k, k2)) { improved = true; done = true }
                }
                if (done) fixedNames.add(label(i, k))
                // 手②: 同一グループで逆方向(fairLow)の相手と相互交換。
                if (!done) {
                    for (i2 in 0 until p.S) {
                        if (done || shouldStop()) break
                        if (i2 == i || p.sgrp.getOrNull(i2) != p.sgrp.getOrNull(i)) continue
                        if (lowTargets.none { it.first == i2 && it.second == k }) continue
                        if (tryMutualSwap(i, i2, k)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                    }
                }
                // 手③: 玉突きチェーンで任意の担当可能シフトへ。
                if (!done) {
                    for (j in 0 until p.T) {
                        if (done || shouldStop()) break
                        if (work[i][j] != k) continue
                        for (alt in p.allowedShiftsForStaff(i)) {
                            if (done || shouldStop()) break
                            if (alt == k) continue
                            if (tryChainRelocate(i, j, k, alt)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                        }
                    }
                }
            }
            // 単独fairLow(自己振替/相互交換で解消しなかった残り)を玉突きチェーンで埋める。
            for ((i, k) in lowTargets) {
                if (shouldStop()) break
                if (!p.canDo(i, k)) continue
                var done = false
                for (j in 0 until p.T) {
                    if (done || shouldStop()) break
                    val oldK = work[i][j]
                    if (oldK == k || oldK !in 0 until p.K) continue
                    if (tryChainRelocate(i, j, oldK, k)) { improved = true; done = true; fixedNames.add(label(i, k)) }
                }
            }
            pass++
            if (!improved) break
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames(distLocations由来)より前に実行する。
        //   結合でwork/bestRepが変わってもdistLocationsはbestRep自身から再取得するため自動整合。
        val fairCombStats = CombinatorialRepair.Stats()
        bestRep = CombinatorialRepair.combineAndApply(
            state, work, bestRep, combinable.asReversed(), ::isBetter, shouldStop = shouldStop, stats = fairCombStats, p = p,
        )
        applied += fairCombStats.combosAccepted
        // [AptPolishと同型] work は毎手の成功時のみコミットしbestRepと同期を保つ（失敗時は必ず巻き戻し）
        //   ため、bestRep.distLocations がそのまま最終盤面の残存箇所＝再チェック不要。
        val stuckNames = bestRep.distLocations["fair"].orEmpty().mapNotNull { loc ->
            val i = loc.getOrNull(0) ?: return@mapNotNull null
            val k = loc.getOrNull(1) ?: return@mapNotNull null
            label(i, k)
        }
        val fairCombSummary = fairCombStats.summary()
        val logs = listOf(MirrorLog(tag = "FairPolish",
            message = "グループ内公平化(fair)研磨: fair ${before.breakdown["fair"] ?: 0}->${bestRep.breakdown["fair"] ?: 0} / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回" +
                (if (applied == 0 && (before.breakdown["fair"] ?: 0) > 0) " [頭打ち=改善手なし]" else "") +
                (if (fixedNames.isNotEmpty()) " 対象: ${fixedNames.joinToString(", ")}" else "") +
                (if (stuckNames.isNotEmpty()) " 残存: ${stuckNames.joinToString(", ")}" else "") +
                (if (fairCombSummary.isNotEmpty()) " / $fairCombSummary" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [C3RunPolish・玉突き連鎖の横展開その3] cons3/cons3m のうち単一シフト連(run-deficit モデル,
     * HF507/C3Run.rowDeficit)専用の研磨パス。C3mnPolish(3.214.0)/RangePolish(3.215.0)と同じ監査
     * （ユーザー指摘「他の制約は大丈夫ですか?」）で発見: 既存のC3Polish(2者ブロック交換)/C3Rotate
     * (3者回転)は「相手が現在の自分のシフトを担当可能」という相互条件を要求し、単一シフト連の
     * run不足（既存runを隣接日へ伸ばせば直る局面）に対しては交換相手が構造的に存在しないと解消できない。
     *
     * スコープ限定（安全側）: 対象は`C3Run.isSingleShiftSeq`が真の規則のみ（cons3/cons3mの大半を占める
     * 典型ケース）。複数シフトのMUST/Wantパターン(非single-shift)は既存のC3Polish/C3Rotateのまま
     * 対象外＝挙動不変（cellFamiliesの"vio-c3"/"vio-c3m"キーは両方のサブケースで共有されるため、
     * アンカー自体は両方拾うが、対応するルールが見つからない/runが既に規定長以上のセルは単に
     * スキップされ何もしない）。
     *
     * アンカー: `report.cellFamilies`から"vio-c3"/"vio-c3m"を含むセル。run-deficitモデルはrun先頭
     * セルをマークするため、そこから実際の run 境界(runStart..runEnd)を再走査し、隣接日(runStart-1
     * または runEnd+1)を該当シフトへ拡張する。拡張元シフトの被覆が悪化する場合は`findCovUChain`
     * （C1Polish/C3mnPolish/RangePolishと同一パターン）で玉突き修復。採否はisBetter keep-best＝退化不能。
     */
    fun applyC3RunPolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }, seed: Long = 0xC3A2L): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        data class RunRule(val k: Int, val len: Int)
        val rules = ArrayList<RunRule>()
        for (c in p.cons3) if (C3Run.isSingleShiftSeq(c.seq)) rules.add(RunRule(c.seq[0], c.seq.size))
        for (c in p.cons3m) if (C3Run.isSingleShiftSeq(c.seq)) rules.add(RunRule(c.seq[0], c.seq.size))
        if (rules.isEmpty()) {
            return CyclicSwapResult(work, before.total, bestRep.total, 0,
                listOf(MirrorLog(tag = "C3RunPolish", message = "対象規則(単一シフト連)なし=スキップ")))
        }
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)

        fun tryExtend(i: Int, extDay: Int, fromK: Int, toK: Int): Boolean {
            if (!movable(i, extDay) || p.makesForbiddenRun(work, i, extDay, toK)) return false
            var cnt = 0
            for (s in 0 until p.S) if (work[s][extDay] == fromK) cnt++
            val needsChain = p.covUCell(fromK, extDay, cnt - 1) > p.covUCell(fromK, extDay, cnt)
            // [厳密ピン保護] i の fromK→toK 直接付替え(+チェーン)は自身の回数を変える唯一の手のため、
            //   staffRange厳密ピン(lo==hi)を崩す候補は不採用にする（keep-best/重みは不変）。
            val workBeforeExtend = work.copy2D()
            work[i][extDay] = toK
            if (!needsChain) {
                val rep = UnifiedViolationChecker.check(state, work)
                if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeExtend, work)) { bestRep = rep; applied++; return true }
                work[i][extDay] = fromK
                return false
            }
            val chain = findCovUChain(p, work, fromK, extDay, rng, exclude = i,
                rangeAvoid = { st, fk -> exceedsOwnRangeHi(p, work, st, fk) })
            if (chain == null) { work[i][extDay] = fromK; return false }
            val oldVals = IntArray(chain.size) { work[chain[it][0]][chain[it][1]] }
            chain.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
            val rep = UnifiedViolationChecker.check(state, work)
            if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforeExtend, work)) { bestRep = rep; applied++; return true }
            for (idx in chain.indices) work[chain[idx][0]][chain[idx][1]] = oldVals[idx]
            work[i][extDay] = fromK
            return false
        }

        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            val rep0 = if (pass == 0) before else UnifiedViolationChecker.check(state, work)
            val anchors = ArrayList<Pair<Int, Int>>()
            for ((key, fams) in rep0.cellFamilies) {
                if ("vio-c3" !in fams && "vio-c3m" !in fams) continue
                val parts = key.split(",")
                val i = parts.getOrNull(0)?.toIntOrNull() ?: continue
                val j = parts.getOrNull(1)?.toIntOrNull() ?: continue
                anchors.add(i to j)
            }
            if (anchors.isEmpty()) break
            for ((i, j) in anchors) {
                if (shouldStop()) break
                val k = work[i][j]
                if (k !in 0 until p.K) continue
                val rule = rules.firstOrNull { it.k == k } ?: continue
                var s0 = j
                while (s0 - 1 >= 0 && work[i][s0 - 1] == k) s0--
                var e0 = j
                while (e0 + 1 < p.T && work[i][e0 + 1] == k) e0++
                if (e0 - s0 + 1 >= rule.len) continue   // 既に規定長以上=スキップ(古いアンカー)
                var done = false
                for (extDay in listOfNotNull((s0 - 1).takeIf { it >= 0 }, (e0 + 1).takeIf { it < p.T })) {
                    if (done || shouldStop()) break
                    val oldK = work[i][extDay]
                    if (oldK == k || oldK !in 0 until p.K) continue
                    if (tryExtend(i, extDay, oldK, k)) { improved = true; done = true }
                }
            }
            pass++
            if (!improved) break
        }
        val stuckNames = (stuckStaffNames(state, bestRep.cellFamilies, "vio-c3") +
            stuckStaffNames(state, bestRep.cellFamilies, "vio-c3m")).distinct()
        val logs = listOf(MirrorLog(tag = "C3RunPolish",
            message = "連続規則(c3/c3m単一シフト連)玉突き研磨: c3 ${before.breakdown["c3"] ?: 0}->${bestRep.breakdown["c3"] ?: 0} / c3m ${before.breakdown["c3m"] ?: 0}->${bestRep.breakdown["c3m"] ?: 0} / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回" +
                (if (applied == 0 && ((before.breakdown["c3"] ?: 0) + (before.breakdown["c3m"] ?: 0)) > 0) " [頭打ち=改善手なし]" else "") +
                (if (stuckNames.isNotEmpty()) " 残存: ${stuckNames.joinToString(", ")}" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [C3PatternPolish・玉突き連鎖の横展開その4] cons3/cons3m のうち複数シフトMUST/Wantパターン
     * （非single-shift、`C3Run.isSingleShiftSeq`が偽の規則）専用の研磨パス。ユーザー指示
     * 「c42/c42s以外にも『動かせるか』専用オペレータの欠如が無いか棚卸しする」で発見（棚卸し結果は
     * ユーザー承認済み）。3.216.0(C3RunPolish)は単一シフト連(run-deficitモデル)のみを対象とし、
     * 複数シフトパターンは「既存機構(2者ブロック交換/3者回転)のまま対象外（安全側・挙動不変）」と
     * 明記して見送っていた。既存の2-3者交換/回転は「相手が対になるパターンを持つ」という相互条件を
     * 要求し、交換相手が構造的に存在しない（誰も対になる並びを持たない）局面では解消できない、
     * c41/c42/covO/apt/fair と同型の穴。
     *
     * `MirrorCore.checkC3Family` の非forbidden複数シフト分岐は「schedule[i][j]==seq[0] かつ
     * 残り(d-1)日が全部一致しない(z<d-1)」を1件の違反として窓先頭セル(i,j)へ計上する。このモデル
     * では「日jのseq[0]を別シフトへ変え、パターンの起点自体を崩す」だけで当該違反インスタンスが
     * 消える（残り日が完成するよう複数日を同時に組み替える方向＝パターン完成は、複数日の依存関係が
     * 絡み正しさの保証が難しいため意図的にスコープ外＝既存の2-3者交換/回転パスに委ねる。見送っても
     * 既存機構が担当を続けるだけ＝安全側）。C3mnPolish(3.214.0)と同一の「1セル付け替え＋
     * findCovUChain玉突き」パターンをそのまま適用する。採否はisBetter(hard→total→weighted)
     * keep-best＝退化不能。
     */
    fun applyC3PatternPolish(state: MagiState, schedule: Array<IntArray>, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }, seed: Long = 0xC3B4L): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        val rules = ArrayList<C3>()
        for (c in p.cons3) if (c.seq.size > 1 && !C3Run.isSingleShiftSeq(c.seq)) rules.add(c)
        for (c in p.cons3m) if (c.seq.size > 1 && !C3Run.isSingleShiftSeq(c.seq)) rules.add(c)
        if (rules.isEmpty()) {
            return CyclicSwapResult(work, before.total, bestRep.total, 0,
                listOf(MirrorLog(tag = "C3PatternPolish", message = "複数シフトc3/c3mパターンなし=スキップ")))
        }
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)

        // アンカー: 各規則(seq,d)で「schedule[i][j]==seq[0]かつ残りd-1日が全一致しない(z<d-1)」窓の
        //   先頭(i,j,seq[0])。checkC3Familyの非forbidden複数シフト分岐と同一の意味論。
        fun collectAnchors(): List<Triple<Int, Int, Int>> {
            val out = ArrayList<Triple<Int, Int, Int>>()
            for (c in rules) {
                val seq = c.seq; val d = seq.size
                if (d > p.T) continue
                for (i in 0 until p.S) {
                    var j = 0
                    while (j <= p.T - d) {
                        if (work[i][j] == seq[0]) {
                            var z = 0
                            for (l in 1 until d) if (work[i][j + l] == seq[l]) z++
                            if (z < d - 1) out.add(Triple(i, j, seq[0]))
                        }
                        j++
                    }
                }
            }
            return out
        }
        val initialCount = collectAnchors().size

        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            val anchors = collectAnchors()
            if (anchors.isEmpty()) break
            for ((i, j, curK) in anchors) {
                if (shouldStop()) break
                if (!movable(i, j) || work[i][j] != curK) continue
                var done = false
                for (alt in p.allowedShiftsForStaff(i)) {
                    if (done || shouldStop()) break
                    if (alt == curK) continue
                    if (p.makesForbiddenRun(work, i, j, alt)) continue
                    var cnt = 0
                    for (s in 0 until p.S) if (work[s][j] == curK) cnt++
                    val needsChain = p.covUCell(curK, j, cnt - 1) > p.covUCell(curK, j, cnt)
                    // [厳密ピン保護] i(・玉突き相手)の回数変更がstaffRange厳密ピン(lo==hi)を新たに崩す
                    //   候補は不採用にする（keep-best/重みは不変・追加ガードのみ）。
                    val workBeforePattern = work.copy2D()
                    work[i][j] = alt
                    if (!needsChain) {
                        val rep = UnifiedViolationChecker.check(state, work)
                        if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforePattern, work)) { bestRep = rep; applied++; improved = true; done = true }
                        else work[i][j] = curK
                        continue
                    }
                    // [玉突き連鎖] i の離脱で curK の被覆が悪化する → 玉突きで埋め直す（盤面不変・巻き戻し可能）。
                    val chain = findCovUChain(p, work, curK, j, rng, exclude = i,
                        rangeAvoid = { st, fk -> exceedsOwnRangeHi(p, work, st, fk) })
                    if (chain == null) { work[i][j] = curK; continue }
                    val oldVals = IntArray(chain.size) { work[chain[it][0]][chain[it][1]] }
                    chain.forEach { mv -> work[mv[0]][mv[1]] = mv[2] }
                    val rep = UnifiedViolationChecker.check(state, work)
                    if (isBetter(rep, bestRep) && !exactPinRegression(p, workBeforePattern, work)) { bestRep = rep; applied++; improved = true; done = true }
                    else {
                        for (idx in chain.indices) work[chain[idx][0]][chain[idx][1]] = oldVals[idx]
                        work[i][j] = curK
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val remaining = collectAnchors()
        val stuckNames = remaining.map { (i, _, _) -> state.staff.getOrNull(i)?.name ?: "#$i" }.distinct()
        val logs = listOf(MirrorLog(tag = "C3PatternPolish",
            message = "連続規則(c3/c3m複数シフトパターン)玉突き研磨: 窓不成立 ${initialCount}->${remaining.size}" +
                " / c3 ${before.breakdown["c3"] ?: 0}->${bestRep.breakdown["c3"] ?: 0}" +
                " / c3m ${before.breakdown["c3m"] ?: 0}->${bestRep.breakdown["c3m"] ?: 0}" +
                " / total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回" +
                (if (applied == 0 && initialCount > 0) " [頭打ち=改善手なし]" else "") +
                (if (stuckNames.isNotEmpty()) " 残存: ${stuckNames.joinToString(", ")}" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [BlockSwapPolish・15日ブロック丸ごと2人交換] ユーザー指示「15日間まるごと2人交換を実装する」
     * （grillingで確定: 対象=同一担当グループのみ／位置=全オフセットのスライド窓／実行=後処理Polish
     * パス／探索範囲=アンカーなし・同グループ内全ペア×全オフセットを無条件に試す）。
     *
     * 動機: 既存の交換系(CyclicSwap=同日1〜3人・鏡像長方形=2日)は局所的なため、「1日ずつ動かすと
     * 途中経過が悪化して isBetter に拒否される」が「まとめて動かせば全体は改善する」ような大きな
     * 交換を発見できない（金沢⇔アリフのような同一range設定のペアでは無意味だが、range/wish/apt等が
     * 異なる同グループのペアでは、ブロックまるごとの入替がlow/high/pref/apt/weeklyを同時に動かし、
     * 1日単位の局所探索が踏めない改善に到達し得る）。
     *
     * 安全性: 同一担当グループ(canDo完全一致)のペアに限定するため、交換後も groupViol/covU/covO/
     * c41(s)/c42(s)/禁止連続の**内部**は構造的に不変（同じシフト列がそのまま相手に移るだけ）。
     * ブロック境界(直前日・直後日との接続)でのみ新規の禁止連続が起こり得るが、それは isBetter の
     * hard判定が担保する。ブロック内に希望固定(wish-lock)がある場合は事前にスキップ（他パスと同じ
     * `movable`規約。無条件に希望を破壊する交換を試みない安全側フィルタ、コスト削減も兼ねる）。
     * 採否はisBetter(hard→total→weighted)keep-best＝退化不能。
     */
    fun applyBlockSwapPolish(state: MagiState, schedule: Array<IntArray>, blockLen: Int = 15, maxPasses: Int = 3, shouldStop: () -> Boolean = { false }): CyclicSwapResult {
        val p = Problem(state)
        val work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        fun name(i: Int) = state.staff.getOrNull(i)?.name ?: "#$i"

        // 同一担当グループ(sgrp)ごとに職員をまとめ、グループ内の全ペアを列挙。
        val byGroup = LinkedHashMap<Int, MutableList<Int>>()
        for (i in 0 until p.S) byGroup.getOrPut(p.sgrp[i]) { ArrayList() }.add(i)
        val pairs = ArrayList<Pair<Int, Int>>()
        for (members in byGroup.values) {
            for (a in members.indices) for (b in a + 1 until members.size) pairs.add(members[a] to members[b])
        }
        if (pairs.isEmpty() || blockLen <= 0 || blockLen > p.T) {
            return CyclicSwapResult(work, before.total, before.total, 0,
                listOf(MirrorLog(tag = "BlockSwapPolish", message = "対象ペア(同一グループ2名以上)なし=スキップ")))
        }

        val fixedNames = ArrayList<String>()
        var pass = 0
        while (pass < maxPasses) {
            if (shouldStop()) break
            var improved = false
            for ((i, i2) in pairs) {
                if (shouldStop()) break
                for (start in 0..(p.T - blockLen)) {
                    if (shouldStop()) break
                    val end = start + blockLen - 1
                    var locked = false
                    var same = true
                    for (j in start..end) {
                        if (!movable(i, j) || !movable(i2, j)) { locked = true; break }
                        if (work[i][j] != work[i2][j]) same = false
                    }
                    if (locked || same) continue
                    for (j in start..end) { val t = work[i][j]; work[i][j] = work[i2][j]; work[i2][j] = t }
                    val rep = UnifiedViolationChecker.check(state, work)
                    if (isBetter(rep, bestRep)) {
                        bestRep = rep; applied++; improved = true
                        fixedNames.add("${name(i)}⇔${name(i2)} ${start + 1}〜${end + 1}日")
                    } else {
                        for (j in start..end) { val t = work[i][j]; work[i][j] = work[i2][j]; work[i2][j] = t }
                    }
                }
            }
            pass++
            if (!improved) break
        }
        val logs = listOf(MirrorLog(tag = "BlockSwapPolish",
            message = "${blockLen}日ブロック丸ごと交換研磨: total ${before.total}->${bestRep.total} HARD ${before.hard}->${bestRep.hard} 採用${applied}回" +
                (if (applied == 0) " [頭打ち=改善手なし]" else "") +
                (if (fixedNames.isNotEmpty()) " 対象: ${fixedNames.joinToString(", ")}" else "")))
        return CyclicSwapResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [C1研磨・複数職員時空間ビーム版, 外部パッチ受領→2箇所修正のうえ適用] applyC1WindowPolish/
     * BeamC1PolishV2 と並存する第3のc1研磨。単一路の同日greedyでなく、各ステップで残っている
     * 不足(staff,day)ターゲットに最小単位の手（同日swap優先、だめならc1Pref付きchain）を足し、
     * HARD悪化のみを絶対条件に生成した候補群を(hard,total,weightedScore)の真の目的関数順で
     * 上位beamWidth本まで残して反復する（デフォルトmaxSteps=60）。
     *
     * **受領コードからの修正2点**（そのまま採用せずレビュー・実データ検証で発見）:
     * ①ビーム剪定の内部ランキングが受領コードでは(hard,c1件数,weightedScore)という**c1専用の
     * 近似指標**だった。golden_state.json実測でこれが致命的と判明: c1を91→63まで下げる候補を
     * 選ぶが、それと引き換えにlow/high/apt/weekly等の他族が軒並み悪化しtotal 291→349・
     * weightedScore 1939→3722（ほぼ倍）という**真の目的関数では大幅な退化**を招いていた
     * （このコードベース全体の規約=hard→total→weightedScoreでなく、c1だけを見て他族への
     * 転嫁を検出できない近似だったため）。ランキングを(hard,total,weightedScore)の真の目的
     * 関数へ修正した結果、golden_state.json/sample_state_v6.jsonの両方・全15シードで
     * 一貫してtotalが真に改善する（golden: 291→274-287, sample_v6: 236→227-229、HARDは
     * 両方とも不変）ことを確認。
     * ②受領コードは検索結果を無条件に返しており、既存の全パスに共通する「root(入力)と比較し
     * 勝てなければroot自身を返す」keep-best安全網が無かった（ビームはrootが必ずしも生き残ら
     * ないためroot自身が最終候補に一度も入らない可能性がある）。`isBetter`によるroot比較＋
     * フォールバックを追加し退化不能にした。
     *
     * 検証はホストJVM(Gradle同梱のkotlin-compiler-embeddable 2.0.21)でandroid非依存の
     * v6/modelパッケージを実コンパイルし、golden_state.json/sample_state_v6.jsonの実データで
     * 実測（このセッション内で実施）。
     */
    fun applyC1BeamPolish(
        state: MagiState, schedule: Array<IntArray>, beamWidth: Int = 16, maxSteps: Int = 60,
        shouldStop: () -> Boolean = { false }, seed: Long = 0x1CBEAL,
    ): CyclicSwapResult {
        val p = Problem(state)
        val work0 = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work0)
        if (p.cons1.isEmpty()) {
            return CyclicSwapResult(work0, before.total, before.total, 0,
                listOf(MirrorLog(tag = "C1BeamPolish", message = "cons1なし=スキップ")))
        }
        val rng = Random(seed)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        fun movable(i: Int, j: Int) = !p.wishLocked(i, j)
        fun c1Deficient(work: Array<IntArray>, i2: Int, x: Int, day: Int): Boolean {
            if (day !in 0 until p.T) return false
            for (c in p.cons1) {
                if (c.shiftIdx != x || c.day1 <= 0) continue
                if (inDeficientC1Window(p, work, i2, x, c.day1, c.day2, day)) return true
            }
            return false
        }

        data class Beam(val work: Array<IntArray>, val rep: com.magi.app.v6.ViolationReport, val applied: Int)

        fun rebuildTargets(work: Array<IntArray>): List<Triple<Int, Int, Int>> {
            val out = ArrayList<Triple<Int, Int, Int>>()
            for ((ci, c) in p.cons1.withIndex()) {
                val x = c.shiftIdx; val d = c.day1; val n = c.day2
                if (x !in 0 until p.K || d <= 0) continue
                for (i in 0 until p.S) {
                    if (!p.canDo(i, x)) continue
                    for (j in 0 until p.T) {
                        if (work[i][j] == x || !movable(i, j)) continue
                        if (inDeficientC1Window(p, work, i, x, d, n, j)) out.add(Triple(ci, i, j))
                    }
                }
            }
            return out
        }
        fun tryOneMove(base: Array<IntArray>, i: Int, j: Int, x: Int): Array<IntArray>? {
            val w = Array(base.size) { base[it].copyOf() }
            val a0 = w[i][j]
            for (i2 in 0 until p.S) {
                if (i2 == i || w[i2][j] != x || !movable(i2, j) || !p.canDo(i2, a0)) continue
                w[i][j] = x; w[i2][j] = a0
                return w
            }
            w[i][j] = x
            val chain = findCovUChain(p, w, a0, j, rng, exclude = i,
                c1Pref = { s2, sh, dy -> c1Deficient(w, s2, sh, dy) })
            if (chain == null) return w
            chain.forEach { mv -> w[mv[0]][mv[1]] = mv[2] }
            return w
        }

        var beam = listOf(Beam(work0, before, 0))
        var step = 0
        while (step < maxSteps) {
            if (shouldStop()) break
            var anyExpanded = false
            val nextCandidates = ArrayList<Beam>()
            for (b in beam) {
                val targets = rebuildTargets(b.work)
                if (targets.isEmpty()) { nextCandidates.add(b); continue }
                val tryList = if (targets.size <= beamWidth * 2) targets else targets.shuffled(rng).take(beamWidth * 2)
                for ((ci, i, j) in tryList) {
                    if (shouldStop()) break
                    val x = p.cons1[ci].shiftIdx
                    val w2 = tryOneMove(b.work, i, j, x) ?: continue
                    val rep2 = UnifiedViolationChecker.check(state, w2)
                    if (rep2.hard > before.hard) continue
                    nextCandidates.add(Beam(w2, rep2, b.applied + 1))
                    anyExpanded = true
                }
            }
            if (!anyExpanded) break
            beam = nextCandidates
                .distinctBy { cand -> cand.work.joinToString("|") { row -> row.joinToString(",") } }
                .sortedWith(compareBy({ it.rep.hard }, { it.rep.weightedScore }, { it.rep.total }))
                .take(beamWidth)
            step++
        }
        // [keep-best安全網] ビーム探索は root 自身を無条件に温存しない（targets 非空の初回展開で
        //   root は子に置き換わり消える）ため、全展開が真の目的関数的には根より悪化する可能性が
        //   ある。既存の全パスが isBetter で keep-best するのに合わせ、root と厳密に比較し、
        //   勝てない場合は必ず未変更の root へフォールバックする（退化不能）。
        val candidate = beam.minWithOrNull(compareBy({ it.rep.hard }, { it.rep.weightedScore }, { it.rep.total })) ?: Beam(work0, before, 0)
        // [厳密ピン保護] ビーム探索の手A/玉突きも i の自身のシフト回数を変えうるため、根(work0)と比較し
        //   staffRange厳密ピン(lo==hi)を崩す最終候補は不採用にする（keep-best/重みは不変・追加ガードのみ）。
        val best = if (isBetter(candidate.rep, before) && !exactPinRegression(p, work0, candidate.work)) candidate else Beam(work0, before, 0)
        val logs = listOf(MirrorLog(tag = "C1BeamPolish",
            message = "期間要件(c1)研磨[ビーム K=$beamWidth steps=$step]: c1 ${before.breakdown["c1"] ?: 0}->${best.rep.breakdown["c1"] ?: 0} / total ${before.total}->${best.rep.total} HARD ${before.hard}->${best.rep.hard} 手数${best.applied}" +
                (if (best.applied == 0 && candidate !== best && candidate.applied > 0) " [探索結果が根に勝てず破棄]" else "")))
        return CyclicSwapResult(best.work, before.total, best.rep.total, best.applied, logs)
    }

    data class DayAssignResult(
        val newSchedule: Array<IntArray>,
        val beforeTotal: Int,
        val afterTotal: Int,
        val appliedDays: Int,
        val logs: List<MirrorLog>,
    )

    /**
     * [ソフト研磨・厳密] 日ごと最小費用割当による研磨。各日の (日,シフト) 人数（=HARD充足）を固定したまま、
     * 希望未固定(wish<0)の職員を、その日の同一シフト集合へ「個人別回数(range)・適切回数(apt)の逸脱が最小」に
     * **厳密再割当**（Hungarian）。乱択でなく日内最適の候補を作り、全体が改善した日だけ採用（keep-best＝退化なし）。
     * 連続規則・希望・平準化など列横断の相互作用は採用判定(UnifiedViolationChecker)で担保する。
     */
    fun applyDayAssignmentPolish(state: MagiState, schedule: Array<IntArray>, shouldStop: () -> Boolean = { false }): DayAssignResult {
        val p = Problem(state)
        var work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        // 適切回数(apt)目標: state.groupShiftApt[群][シフト] の整数（空=なし）。
        fun aptTarget(i: Int, k: Int): Int? {
            val g = state.staff.getOrNull(i)?.groupIdx ?: return null
            return state.groupShiftApt.getOrNull(g)?.getOrNull(k)?.trim()?.toIntOrNull()
        }
        fun cnt(): Array<IntArray> = countMatrix(p, work)
        var counts = cnt()
        for (j in 0 until p.T) {
            if (shouldStop()) break
            val free = (0 until p.S).filter { i -> p.wish[i][j] < 0 }
            if (free.size < 2) continue
            val slots = free.map { work[it][j] }                       // 当日の同一シフト多重集合（人数固定）
            val n = free.size
            val costM = Array(n) { r ->
                val i = free[r]
                LongArray(n) { c ->
                    val k = slots[c]
                    if (k !in 0 until p.K || !p.canDo(i, k)) MinCostAssignment.INF
                    else {
                        val x0 = counts[i][k] - (if (work[i][j] == k) 1 else 0)   // この日を除いた現状カウント
                        val x1 = x0 + 1                                            // k を割当てた後
                        val lo = p.rangeLo[i][k]
                        val hi = effectiveHi(p, i, k)
                        // [ソフト研磨・候補生成の重み整合] 従来の rangePen は low/high を 3/3 の擬似重みで評価していたが、
                        //   真の目的関数(Evaluator / staffCountPenaltyAt / UnifiedViolationChecker)は low=90・high=45・apt=1。
                        //   proxy が重い low/high を apt(重み1)と同格(3対1)に扱うと Hungarian が「軽い apt を直すため重い
                        //   low/high を犠牲にする」候補を生みやすく、良候補を生み損ねる(CLAUDE.md 既知・測定待ち)。
                        //   proxy を目的関数と同一の 90/45/1 に整合させ、生成候補を真の目的へ寄せる。採否は従来どおり
                        //   keep-best(isBetter@UnifiedViolationChecker)が担うため退化なし＝スコアリング不変。
                        fun rangePen(x: Int) = (if (lo != Int.MIN_VALUE) 90L * maxOf(0, lo - x) else 0L) + 45L * maxOf(0, x - hi)
                        var cost = rangePen(x1) - rangePen(x0)                     // range の限界費用
                        val t = aptTarget(i, k)
                        if (t != null) cost += (kotlin.math.abs(x1 - t) - kotlin.math.abs(x0 - t)).toLong()  // apt の限界費用
                        cost
                    }
                }
            }
            // [3.278.0] 全INF行(担当可否ゼロの職員等)は実行可能な完全割当が無い＝nullでその日をスキップ。
            val assign = MinCostAssignment.solve(costM) ?: continue
            val cand = work.copy2D()
            var changed = false
            for (r in free.indices) {
                val i = free[r]; val k = slots[assign[r]]
                if (cand[i][j] != k) { cand[i][j] = k; changed = true }
            }
            if (!changed) continue
            val rep = UnifiedViolationChecker.check(state, cand)
            // [厳密ピン保護] 日ブロック内Hungarian再割当は複数職員の回数を同時に変えうるため、
            //   staffRange厳密ピン(lo==hi)を新たに崩す日案は不採用にする（keep-best/重みは不変）。
            if (isBetter(rep, bestRep) && !exactPinRegression(p, work, cand)) { work = cand; bestRep = rep; counts = cnt(); applied++ }
        }
        val logs = listOf(MirrorLog(tag = "DayAssign",
            message = "日ごと厳密割当: total ${before.total}->${bestRep.total} 採用${applied}日"))
        return DayAssignResult(work, before.total, bestRep.total, applied, logs)
    }

    /**
     * [ソフト研磨・交互最適化(Alternating Optimization / 交代最適化)] 全変数を同時に解かず「1ブロックずつ順に最適化
     * して巡回する」座標降下法（block coordinate descent）をソフト制約研磨に導入する新アルゴリズム。ブロック＝各日(列):
     * その日の (シフト人数=被覆) を固定したまま、希望未固定(wish<0)の職員を「個人別回数(range 90/45)・適切回数(apt 1)・
     * **曜日平準化(weekly 1)**」の限界費用が最小になるよう **最小費用割当(Hungarian＝割当LP＝凸最適化)** で最適再配置し、
     * 日 j を 0..T-1 と巡回して 1スイープで1日も変化しなくなるまで（＝座標降下の不動点）反復する。
     *
     * 既存 `applyDayAssignmentPolish`（range/apt のみ・単発）を、①weekly を費用に含め ②反復収束（交互）まで一般化した
     * もの。weekly を費用に入れる意味＝その日の「休スロット」を誰に割り当てるかで各職員の曜日別勤務数が変わる（被覆は不変）。
     * 「その曜日に働き過ぎの職員へ休を、少なすぎる職員へ勤務を」割り当てる候補を Hungarian が同日内で**同時最適**に生成し、
     * 曜日偏りを直す。同日内の最適再配置＝rectangle（3.197.0, クロス日の2職員×2日）とは別種の被覆保存手＝相補的。
     * 採否は実目的関数 isBetter（hard→total→weighted, keep-best）＝退化なし。fair 等の他 soft は isBetter が担保する
     * （費用に無い族も採用判定で悪化しないことを保証）。純 Kotlin 後処理＝ネイティブ hot-path 非干渉（parity 影響なし）。
     */
    fun applyAlternatingSoftPolish(state: MagiState, schedule: Array<IntArray>, maxSweeps: Int = 4, shouldStop: () -> Boolean = { false }): DayAssignResult {
        val p = Problem(state)
        var work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var bestRep = before
        var applied = 0
        val rest = p.restIdx
        fun aptTarget(i: Int, k: Int): Int? {
            val g = state.staff.getOrNull(i)?.groupIdx ?: return null
            return state.groupShiftApt.getOrNull(g)?.getOrNull(k)?.trim()?.toIntOrNull()
        }
        // weekly の wd バケット（職員×曜日の勤務数）と目標 round(勤務日/7)。被覆保存の再配置ごとに更新。
        fun wdOf(i: Int): IntArray {
            val wd = IntArray(7)
            for (j in 0 until p.T) { val k = work[i][j]; if (k != rest && k in 0 until p.K) wd[(p.dow0 + j) % 7]++ }
            return wd
        }
        fun tgtOf(wd: IntArray): Int { var s = 0; for (w in wd) s += w; return Math.round(s.toDouble() / 7.0).toInt() }
        var wd = Array(p.S) { wdOf(it) }
        var wdTgt = IntArray(p.S) { tgtOf(wd[it]) }
        fun cnt(): Array<IntArray> = countMatrix(p, work)
        var counts = cnt()
        var sweep = 0
        var lastSweep = 0
        while (sweep < maxSweeps) {
            if (shouldStop()) break
            var changedInSweep = false
            for (j in 0 until p.T) {
                if (shouldStop()) break
                val free = (0 until p.S).filter { i -> p.wish[i][j] < 0 }
                if (free.size < 2) continue
                val slots = free.map { work[it][j] }
                val n = free.size
                val wdj = (p.dow0 + j) % 7
                val costM = Array(n) { r ->
                    val i = free[r]
                    LongArray(n) { c ->
                        val k = slots[c]
                        if (k !in 0 until p.K || !p.canDo(i, k)) MinCostAssignment.INF
                        else {
                            val x0 = counts[i][k] - (if (work[i][j] == k) 1 else 0)   // この日を除いた現状カウント
                            val x1 = x0 + 1
                            val lo = p.rangeLo[i][k]
                            val hi = effectiveHi(p, i, k)
                            // range/apt は applyDayAssignmentPolish と同一の目的関数整合 proxy（90/45/1）。
                            fun rangePen(x: Int) = (if (lo != Int.MIN_VALUE) 90L * maxOf(0, lo - x) else 0L) + 45L * maxOf(0, x - hi)
                            var cost = rangePen(x1) - rangePen(x0)
                            val t = aptTarget(i, k)
                            if (t != null) cost += (kotlin.math.abs(x1 - t) - kotlin.math.abs(x0 - t)).toLong()
                            // weekly 限界費用: 当日を k(=勤務 or 休)にしたときの職員 i の曜日 wdj バケットの L1 偏差変化（重み1）。
                            val curWork = if (work[i][j] != rest && work[i][j] in 0 until p.K) 1 else 0
                            val newWork = if (k != rest) 1 else 0
                            if (curWork != newWork) {
                                val base = wd[i][wdj] - curWork              // 当日を除いた曜日カウント
                                val tgt = wdTgt[i]
                                cost += (kotlin.math.abs((base + newWork) - tgt) - kotlin.math.abs((base + curWork) - tgt)).toLong()
                            }
                            cost
                        }
                    }
                }
                // [3.278.0] 全INF行(担当可否ゼロの職員等)は実行可能な完全割当が無い＝nullでその日をスキップ。
                val assign = MinCostAssignment.solve(costM) ?: continue
                val cand = work.copy2D()
                var changed = false
                for (r in free.indices) {
                    val i = free[r]; val k = slots[assign[r]]
                    if (cand[i][j] != k) { cand[i][j] = k; changed = true }
                }
                if (!changed) continue
                val rep = UnifiedViolationChecker.check(state, cand)
                // [厳密ピン保護] 日ブロック内Hungarian再割当は複数職員の回数を同時に変えうるため、
                //   staffRange厳密ピン(lo==hi)を新たに崩す日案は不採用にする（keep-best/重みは不変）。
                if (isBetter(rep, bestRep) && !exactPinRegression(p, work, cand)) {
                    work = cand; bestRep = rep; counts = cnt()
                    wd = Array(p.S) { wdOf(it) }; wdTgt = IntArray(p.S) { tgtOf(wd[it]) }
                    applied++; changedInSweep = true
                }
            }
            sweep++; lastSweep = sweep
            if (!changedInSweep) break   // 座標降下の不動点＝この巡回で1日も改善しない
        }
        val logs = listOf(MirrorLog(tag = "AltOptPolish",
            message = "交互最適化(日ブロック・weekly込み割当): total ${before.total}->${bestRep.total} 採用${applied}日 (${lastSweep}スイープ)"))
        return DayAssignResult(work, before.total, bestRep.total, applied, logs)
    }

    fun applyHF67InterStaffSwap(state: MagiState, schedule: Array<IntArray>, maxSwaps: Int = 30, shouldStop: () -> Boolean = { false }, deadlineMs: Long = Long.MAX_VALUE): HF67Result {
        val p = Problem(state)
        var work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var current = before
        var swaps = 0
        var shortage = 0
        var capacity = 0
        var rollback = 0
        // [3.282.0/新領域ログ監査] 兄弟の HF66 は専用上限(deadlineMs)＋内側スキャンの outOfTime 確認
        //   （2.65.0/3.161.0 の確立方針）を持つのに、HF67 だけ手ごとの shouldStop のみ＝候補ごとフル check の
        //   内側スキャン（k×lows×highs）とフォールバック（全ペア×全日の総当たり=実機で rollback=264 の正体）が
        //   締切後も走り切る非対称だった。同型の締切確認を追加（keep-best のため途中中断でも退化なし）。
        fun outOfTime() = shouldStop() || System.currentTimeMillis() >= deadlineMs

        while (swaps < maxSwaps) {
            if (outOfTime()) break
            val counts = countMatrix(p, work)
            var best: SwapCandidate? = null
            var bestReport: ViolationReport? = null
            scan@ for (k in 0 until p.K) {
                if (outOfTime()) break@scan
                val lows = ArrayList<Int>()
                val highs = ArrayList<Int>()
                for (i in 0 until p.S) {
                    if (p.canDo(i, k) && p.rangeLo[i][k] != Int.MIN_VALUE && counts[i][k] < p.rangeLo[i][k]) lows.add(i)
                    if (counts[i][k] > effectiveHi(p, i, k)) highs.add(i)
                }
                for (to in lows) {
                    if (outOfTime()) break@scan
                    for (from in highs) {
                        if (to == from) continue
                        val cand = trySwapShiftBetweenStaff(p, work, from, to, k) ?: continue
                        val rep = UnifiedViolationChecker.check(state, cand.first)
                        val ref = bestReport ?: current
                        if (isBetter(rep, ref)) {
                            best = cand.second
                            bestReport = rep
                        }
                    }
                }
            }
            if (best == null || bestReport == null) break
            val b = best
            val next = work.copy2D()
            val tmp = next[b.fromStaff][b.fromDay]
            next[b.fromStaff][b.fromDay] = next[b.toStaff][b.toDay]
            next[b.toStaff][b.toDay] = tmp
            work = next
            current = bestReport
            swaps++
            shortage++
            if (current.soft < before.soft) capacity++
        }
        if (swaps == 0 && !outOfTime()) {
            val improved = localPairwiseStaffSwap(state, work, maxSwaps, { outOfTime() })
            work = improved.first
            swaps = improved.second
            rollback = improved.third
            current = UnifiedViolationChecker.check(state, work)
            capacity = swaps
        }
        val logs = listOf(MirrorLog(tag = "HF67", message = "inter-staff swap applied=$swaps rollback=$rollback total ${before.total}->${current.total}"))
        return HF67Result(work, before.total, current.total, swaps, shortage, capacity, rollback, logs)
    }

    fun applyHF66IntraStaffRedistribution(state: MagiState, schedule: Array<IntArray>, maxMoves: Int = 30, shouldStop: () -> Boolean = { false }, deadlineMs: Long = Long.MAX_VALUE): HF66Result {
        val p = Problem(state)
        var work = normalizeSchedule(schedule, p)
        val before = UnifiedViolationChecker.check(state, work)
        var current = before
        var moves = 0
        var shortageMoves = 0
        var capacityMoves = 0
        var rollback = 0
        // [残予算ガード] shouldStop(全体締切)に加え HF66 専用の時間上限(deadlineMs)も尊重する。1手の全候補
        //   スキャンは候補ごとにフル check するため高コスト。手ごとだけでなく内側ループでも締切を確認し、
        //   締切後に1手分のスキャンを走り切って後段の研磨パスを押し出す(=予算超過で打ち切らせる)のを防ぐ。
        //   keep-best のため途中中断しても解は退化しない(採用は isBetter な bestMove のみ)。
        fun outOfTime() = shouldStop() || System.currentTimeMillis() >= deadlineMs

        while (moves < maxMoves) {
            if (outOfTime()) break
            val counts = countMatrix(p, work)
            var bestMove: MoveCandidate? = null
            var bestReport: ViolationReport? = null
            scan@ for (i in 0 until p.S) {
                if (outOfTime()) break@scan
                val lows = ArrayList<Int>()
                val highs = ArrayList<Int>()
                for (k in 0 until p.K) {
                    if (p.canDo(i, k) && p.rangeLo[i][k] != Int.MIN_VALUE && counts[i][k] < p.rangeLo[i][k]) lows.add(k)
                    if (counts[i][k] > effectiveHi(p, i, k)) highs.add(k)
                }
                for (want in lows) for (give in highs) {
                    if (outOfTime()) break@scan
                    for (j in 0 until p.T) {
                        if (work[i][j] != give || p.wish[i][j] >= 0) continue
                        val cand = work.copy2D()
                        cand[i][j] = want
                        val rep = UnifiedViolationChecker.check(state, cand)
                        if (isBetter(rep, bestReport ?: current)) {
                            bestMove = MoveCandidate(i, j, give, want)
                            bestReport = rep
                        }
                    }
                }
            }
            val mv = bestMove ?: break
            work[mv.staff][mv.day] = mv.toShift
            current = bestReport ?: UnifiedViolationChecker.check(state, work)
            moves++
            shortageMoves++
            if (current.soft < before.soft) capacityMoves++
        }
        if (moves == 0 && !outOfTime()) {
            val rng = Random(0x66L)
            var t = 0
            while (t < maxMoves) {
                if (outOfTime()) break
                if (p.S > 0 && p.T > 0) {
                    val cand = work.copy2D()
                    val i = rng.nextInt(p.S)
                    val j = rng.nextInt(p.T)
                    if (p.wish[i][j] < 0) {
                        val allowed = p.allowedShiftsForStaff(i)
                        if (allowed.isNotEmpty()) {
                            val old = cand[i][j]
                            cand[i][j] = allowed[rng.nextInt(allowed.size)]
                            if (cand[i][j] != old) {
                                val rep = UnifiedViolationChecker.check(state, cand)
                                if (isBetter(rep, current)) {
                                    work = cand
                                    current = rep
                                    moves++
                                    capacityMoves++
                                } else {
                                    rollback++
                                }
                            }
                        }
                    }
                }
                t++
            }
        }
        val logs = listOf(MirrorLog(tag = "HF66", message = "intra-staff redistribution applied=$moves rollback=$rollback total ${before.total}->${current.total}"))
        return HF66Result(work, before.total, current.total, moves, shortageMoves, capacityMoves, rollback, logs)
    }

    fun detectHF70Anomalies(
        state: MagiState,
        schedule: Array<IntArray>,
        algoName: String,
        report: ViolationReport = UnifiedViolationChecker.check(state, schedule),
    ): HF70Result {
        val invalid = invalidAssignmentCount(state, schedule)
        val impossible = V6SanityPort.detectImpossibleWishes(state).size
        val hardCore = report.hard - (report.breakdown["pref"] ?: 0)
        val issues = ArrayList<String>()
        if (invalid > 0) issues.add("担当不可/範囲外配置 $invalid 件")
        if (impossible > 0) issues.add("不可能希望 $impossible 件")
        if (hardCore > 0) issues.add("希望以外HARD $hardCore 件")
        val msg = if (issues.isEmpty()) "HF70: $algoName 異常なし" else "HF70: ${issues.joinToString(" / ")}"
        val advice = if (issues.isEmpty()) "" else "設定(担当範囲), 希望, 必要人数, 連勤禁止条件を確認してください"
        val level = if (issues.isEmpty()) "I" else "W"
        val logs = listOf(MirrorLog(level = level, tag = "HF70", message = msg + if (advice.isNotBlank()) " — $advice" else ""))
        return HF70Result(issues.size, msg, advice, logs)
    }

    private data class SwapCandidate(val fromStaff: Int, val fromDay: Int, val toStaff: Int, val toDay: Int)
    private data class MoveCandidate(val staff: Int, val day: Int, val fromShift: Int, val toShift: Int)

    private fun trySwapShiftBetweenStaff(p: Problem, schedule: Array<IntArray>, from: Int, to: Int, shift: Int): Pair<Array<IntArray>, SwapCandidate>? {
        val fromDays = ArrayList<Int>()
        val toDays = ArrayList<Int>()
        for (j in 0 until p.T) {
            if (schedule[from][j] == shift && p.wish[from][j] < 0) fromDays.add(j)
            if (schedule[to][j] != shift && p.wish[to][j] < 0 && p.canDo(to, shift) && p.canDo(from, schedule[to][j])) toDays.add(j)
        }
        for (jf in fromDays) for (jt in toDays) {
            val cand = schedule.copy2D()
            val tmp = cand[from][jf]
            cand[from][jf] = cand[to][jt]
            cand[to][jt] = tmp
            return Pair(cand, SwapCandidate(from, jf, to, jt))
        }
        return null
    }

    private fun localPairwiseStaffSwap(state: MagiState, schedule: Array<IntArray>, maxSwaps: Int, shouldStop: () -> Boolean = { false }): Triple<Array<IntArray>, Int, Int> {
        val p = Problem(state)
        var work = schedule.copy2D()
        var current = UnifiedViolationChecker.check(state, work)
        var applied = 0
        var rollback = 0
        loop@ for (i in 0 until p.S) for (i2 in i + 1 until p.S) for (j in 0 until p.T) {
            if (applied >= maxSwaps || shouldStop()) break@loop
            if (p.wish[i][j] >= 0 || p.wish[i2][j] >= 0) continue
            val a = work[i][j]
            val b = work[i2][j]
            if (a == b || !p.canDo(i, b) || !p.canDo(i2, a)) continue
            val cand = work.copy2D()
            cand[i][j] = b
            cand[i2][j] = a
            val rep = UnifiedViolationChecker.check(state, cand)
            if (isBetter(rep, current)) {
                work = cand
                current = rep
                applied++
            } else {
                rollback++
            }
        }
        return Triple(work, applied, rollback)
    }

    private fun localBestImprovement(state: MagiState, schedule: Array<IntArray>, tries: Int, rng: Random, shouldStop: () -> Boolean = { false }): Array<IntArray> {
        val p = Problem(state)
        var best = schedule.copy2D()
        var bestReport = UnifiedViolationChecker.check(state, best)
        var t = 0
        val maxTry = max(0, tries)
        while (t < maxTry) {
            if (shouldStop()) break
            if (p.S > 0 && p.T > 0) {
                val cand = best.copy2D()
                val i = rng.nextInt(p.S)
                val j = rng.nextInt(p.T)
                if (p.wish[i][j] < 0) {
                    val allowed = p.allowedShiftsForStaff(i)
                    if (allowed.isNotEmpty()) {
                        cand[i][j] = allowed[rng.nextInt(allowed.size)]
                        val rep = UnifiedViolationChecker.check(state, cand)
                        if (isBetter(rep, bestReport)) {
                            best = cand
                            bestReport = rep
                        }
                    }
                }
            }
            t++
        }
        return best
    }

    private fun effectiveHi(p: Problem, i: Int, k: Int): Int {
        val hi = p.rangeHi[i][k]
        return if (hi == Int.MAX_VALUE) Int.MAX_VALUE / 4 else hi
    }

    private fun invalidAssignmentCount(state: MagiState, schedule: Array<IntArray>): Int {
        val p = Problem(state)
        val s = normalizeSchedule(schedule, p)
        var n = 0
        for (i in 0 until p.S) for (j in 0 until p.T) {
            val k = s[i][j]
            if (k !in 0 until p.K || !p.canDo(i, k)) n++
        }
        return n
    }

    // [3.287.0 keep-best統一] hard→weightedScore→total（単一ソース betterReport へ委譲。MirrorCore.kt 参照）。
    private fun isBetter(a: ViolationReport, b: ViolationReport): Boolean = betterReport(a, b)

    // ============================================================================
    // [#5 差分前フィルタ] 職員 i の「研磨手で動きうる families」の重み付き違反量を checker と同一ロジックで
    // 厳密計算する。c1(4) + c3(3) + c3m(2) + c3mn(12) + c3n(HARD=7000)。研磨手は被覆保存の置換なので、
    // 関与職員が全員同一グループなら、群(c41/c42/groupViol)・被覆(covU/covO)・pref(希望固定セルは不動)は不変。
    // よって「関与職員のこの値が改善しない同群の手」は全体目的(hard/total/weighted)も改善しえない。
    // ⇒ そういう手では高コストの UnifiedViolationChecker.check をスキップできる(前フィルタ)。
    // 安全性: 採用判定は従来どおりフル評価 isBetter が担う。前フィルタに誤りがあっても「改善手を取り
    //         こぼす(=研磨が弱まるだけ)」方向にしか働かず、誤採用や研磨前からの悪化は起こらない。
    // ============================================================================
    // [#5 差分前フィルタ] 職員 i の packed メトリクス = (hard:c3n件数) を最上位、(total:変化しうる全family
    // の件数) を中位、(weighted) を下位に詰めた単一 Long（isBetter の hard→total→weighted と同順）。
    // 研磨手で動きうる per-staff family のうち主要分を集計: c1(4)/c2(1)/c3(3)/c3n(HARD7000)/c3m(2)/c3mn(12)/
    // low(90)/high(45)。※apt/fair/weekly(各1) は本 packed に含めない＝それら**のみ**を改善する手はこの前フィルタで
    // こぼす(研磨が弱まるだけ=keep-best 安全、誤採用や悪化は起きない)。関与職員が sgrp も ssk も同一なら、群(c41/c42)・スキル群(c41s/c42s)・被覆(covU/covO)・
    // pref(希望固定セルは不動) は全て不変。よって関与職員のこの packed が改善しなければ全体目的も改善しえず、
    // その手はフル評価をスキップしてよい(前フィルタ)。採用判定は従来どおりフル評価 isBetter が担う＝安全。
    // 1職員あたり各レベルは小さく、関与2-3名の和でも桁跨ぎしない(hardレベル:1e15, totalレベル:1e9)。
    private fun staffPacked(p: Problem, sched: Array<IntArray>, i: Int): Long {
        var hard = 0L; var total = 0L; var wgt = 0L
        val cnt = IntArray(p.K)                                   // 期間内シフト回数(c2/low/high 用)
        for (j in 0 until p.T) { val k = sched[i][j]; if (k in 0 until p.K) cnt[k]++ }
        for (c in p.cons1) {                                      // c1: d日窓で shiftIdx が day2 回未満
            if (!p.canDo(i, c.shiftIdx)) continue
            var j = 0
            while (j <= p.T - c.day1) {
                var z = 0
                for (l in 0 until c.day1) if (sched[i][j + l] == c.shiftIdx) z++
                if (z < c.day2) { total++; wgt += 4 }
                j++
            }
        }
        for (c in p.cons2) if (p.canDo(i, c.shiftIdx) && cnt[c.shiftIdx] < c.count) { total++; wgt += 1 } // c2
        for (k in 0 until p.K) {                                  // low/high: 回数レンジ(不足/超過「量」を加算)
            val lo = p.rangeLo[i][k]; val hi = p.rangeHi[i][k]; val n = cnt[k]
            if (lo != Int.MIN_VALUE && lo != 0 && p.canDo(i, k) && n < lo) { val d = (lo - n).toLong(); total += d; wgt += d * 90 }
            if (hi != Int.MAX_VALUE && n > hi) { val d = (n - hi).toLong(); total += d; wgt += d * 45 }
        }
        val c3nC = c3FamCount(p, sched, i, p.cons3n, true)        // c3n は HARD
        val c3C = c3FamCount(p, sched, i, p.cons3, false)
        val c3mC = c3FamCount(p, sched, i, p.cons3m, false)
        val c3mnC = c3FamCount(p, sched, i, p.cons3mn, true)
        hard += c3nC
        total += c3nC + c3C + c3mC + c3mnC
        wgt += c3nC * 7000 + c3C * 3 + c3mC * 2 + c3mnC * 12
        return hard * 1_000_000_000_000_000L + total * 1_000_000_000L + wgt
    }

    private fun c3FamCount(p: Problem, sched: Array<IntArray>, i: Int, list: List<C3>, forbidden: Boolean): Long {
        var c = 0L
        for (con in list) {
            val seq = con.seq; val d = seq.size
            if (d == 0 || d > p.T) continue
            var j = 0
            while (j <= p.T - d) {
                if (sched[i][j] == seq[0]) {
                    var z = 0
                    for (l in 1 until d) if (sched[i][j + l] == seq[l]) z++
                    val fire = if (forbidden) z == d - 1 else z < d - 1
                    if (fire) c++
                }
                j++
            }
        }
        return c
    }
}
