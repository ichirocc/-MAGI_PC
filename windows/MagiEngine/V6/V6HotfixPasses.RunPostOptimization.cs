using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [頭打ち調査・「なぜゼロにならないのか」] <see cref="ApplyC3mnPolish"/>/<see cref="ApplyRangePolish"/>/
    /// <see cref="ApplyC3RunPolish"/>/研磨の各族は <see cref="RunPostOptimization"/> のフィックスポイント巡回
    /// (最大4巡)から**ラウンドごとに再呼出**されるが、旧実装は seed 引数を渡さず既定値固定のままだった。
    /// findCovUChain の候補順は rng 由来なので、ある(staff,shift)ペアがラウンドNで頭打ち（候補が構造的に
    /// 全滅／isBetter に拒否）すると、盤面の当該箇所が変化しない限りラウンドN+1以降も<b>全く同じrng列＝
    /// 同じ結果</b>を再生するだけで、永久に頭打ちのまま抜け出せなかった（桒澤美幸のAｱ超過が段階的にしか
    /// 縮まらない実例で発覚）。ラウンドごとに異なる seed を与え、再挑戦のたびに違う候補順を試せるようにする
    /// （isBetter による keep-best 採否は不変＝退化不能。単なる探索の多様化）。
    /// </summary>
    internal static long RoundSeed(long baseSeed, long tag, int round) =>
        baseSeed ^ tag ^ ((long)round * -0x61c8864680b583ebL);

    /// <summary>
    /// Kotlin原本 <c>V6PostOptimizationResult</c>（top-level data class）の忠実な移植。
    /// <see cref="RunPostOptimization"/> の戻り値。
    /// </summary>
    public sealed record V6PostOptimizationResult(
        int[][] Schedule,
        ViolationReport Report,
        HF80Result Hf80,
        HF67Result Hf67,
        HF66Result Hf66,
        HF70Result Hf70,
        IReadOnlyList<MirrorLog> Logs,
        /// <summary>[3.322.0] 窓の要件(c1)が最後まで残った理由の構造化診断（残存なしなら null）。</summary>
        C1PlateauDiagnosis? C1Plateau = null,
        /// <summary>
        /// [3.323.0] 厳密ピン(lo==hi)だけが却下した候補の<b>計測できた試行数</b>。これらは
        /// <see cref="IsBetter"/> が採用を認めた手で、ピンのガードだけが止めている。
        ///
        /// **正確な読み方**: 「手の数」ではなく「試行の回数」。巡回研磨は最大4巡するので、同じ手が
        /// 複数の巡で数えられうる（重複排除していない）。<b>全パス横断ではない</b>——
        /// <see cref="V6HotfixPasses"/> の19パスに加え <see cref="C1JointLnsPolish"/> と
        /// <see cref="PersonalBalanceJointLnsPolish"/> を計測する。計測外は
        /// <see cref="EliteIntegrationPolish"/>(4)・<see cref="C1TemporalFlowPolish"/>(1)・
        /// <see cref="CombinatorialRepair"/>(2)・<see cref="C1RepairAnalysis"/>(1)の計8箇所と、
        /// ピン保護を持たない探索本体(SA/ALNS/LAHC)。「N 件の手が緩和で通る」ではなく
        /// 「<b>少なくとも N 回、回数固定だけが却下の理由だった</b>」が言えることの上限。0 でも
        /// 「緩めても何も変わらない」の証明にはならない（未計測分がある）。
        /// </summary>
        int ObservedPinBlockedAttempts = 0,
        /// <summary>[3.326.0] どのピン(職員,シフト)が何回止めたか。緩和対象の提示に使う。</summary>
        PinBlockAttribution? PinBlocks = null);

    /// <summary>
    /// 後処理チェーンの探索幅と予算（Kotlin 3.500.0 <c>PostOptimizationParams</c> の移植。既定値は従来の手書き値＝挙動不変）。
    /// HF67/HF66 の上限は「残予算の半分・絶対上限」の保険（3.282.0）。<see cref="JointLnsReserveMaxMs"/> は巡回研磨クラスタの前に
    /// 共同 LNS 2 本のための残予算の半分を確保する（3.271.0 の飢餓解消）。最終 LNS の残予算は既定比 8:6 で按分し、
    /// <see cref="RemainingClampMs"/> で乗算オーバーフローを避ける（3.255.0）。
    /// </summary>
    public sealed record PostOptimizationParams(
        int Hf80MaxCycles = 3,
        int Hf67MaxSwaps = 30,
        long Hf67CapMs = 3_000L,
        int Hf66MaxMoves = 30,
        long Hf66CapMs = 6_000L,
        long JointLnsReserveMaxMs = 14_000L,
        int MaxRounds = 4,
        int CyclicSwapPasses = 4,
        int C1WindowPasses = 3,
        int C1FlowPasses = 2,
        int C1FlowRelocations = 4,
        int C1FlowTrials = 4,
        int C3SequencePasses = 3,
        int C3RotatePasses = 2,
        int C3mnPasses = 3,
        int C3nPasses = 3,
        int RangePasses = 3,
        int C3RunPasses = 3,
        int C3PatternPasses = 3,
        int AnchorWindowPasses = 3,
        int AnchorWindowEvaluations = 48,
        int WishIslandPasses = 3,
        int WishIslandEvaluations = 120,
        int BlockSwapPasses = 2,
        int BlockSwapCandidatesPerLength = 8,
        int BlockSwapEvaluations = 48,
        int AptPasses = 3,
        int FairPasses = 3,
        int WeeklyRebalancePasses = 2,
        int AlternatingSweeps = 4,
        long C1LnsMaxMs = 8_000L,
        long PersonalLnsMaxMs = 6_000L,
        long RemainingClampMs = 100_000L,
        int PassLogTopN = 8,
        /// <summary>[Iteration 2] 各パスの拒否候補を巡の末尾で違反起点のトランザクションに束ねる（ViolationComponentRepair）。Android 3.505.1 でハイブリッド併用＝既定 ON。</summary>
        bool ComponentRepairEnabled = true,
        ViolationComponentRepair.Params? ComponentRepair = null);

    /// <summary>巡ごとの乱数列を分けるためのパス別タグ（<see cref="RoundSeed"/>）。値は従来の手書き値と同じ＝乱数列不変。</summary>
    private static class SeedTag
    {
        public const long Hf80 = 0x80L;
        public const long C1Window = 0x1C1L;
        public const long C1Index = 0x1C1D2L;
        public const long C1Flow = 0xC1F10L;
        public const long C1Beam = 0xC1BEAL;
        public const long C3mn = 0xC3AL;
        public const long C3n = 0xC3EL;
        public const long Range = 0x8A9EL;
        public const long C3Run = 0xC3A2L;
        public const long C3Pattern = 0xC3B4L;
        public const long Apt = 0xA97L;
        public const long Fair = 0xFA12L;
    }

    /// <summary>SoftPolishVerify の「採用内訳」の並び（ログ文言の順序を固定する）。</summary>
    private static readonly string[] AdoptionKeys =
    {
        "循環", "c1", "c3", "c3回転", "c3mn玉突き", "c3n", "range玉突き", "c3run玉突き", "c3pattern玉突き",
        "アンカー窓交換", "希望島", "ブロック交換", "apt玉突き", "fair玉突き", "成分修復",
    };

    /// <summary>SoftPolishVerify で「対象」に数える族（3.278.0 で CyclicSwap の対象族、3.475.0 で c3n を追加）。</summary>
    private static readonly string[] SoftTargetFamilies =
    {
        "c1", "c3", "c3m", "c3mn", "c3n", "low", "high", "apt", "fair", "c2", "c41", "c42", "c41s", "c42s", "covO",
    };

    /// <summary>
    /// 後処理チェーンの作業域＝盤面・ログ・パス別所要・ピン帰属の合流点。各パスは必ず <see cref="Adopt(CyclicSwapResult, bool)"/> を通す＝
    /// 「PinBlocks の合流を書き忘れる」（3.350.0・3.409.9 で実際に起きた）を構造的に防ぐ。
    /// </summary>
    private sealed class PostChain
    {
        private readonly Action<string>? _onPhase;
        public int[][] Work { get; private set; }
        public List<MirrorLog> Logs { get; } = new();
        public Dictionary<string, long> PassMs { get; } = new();
        public PinBlockAttribution PinBlocksAll { get; } = new();
        /// <summary>[Iteration 2] 巡の中で各パスが残した拒否候補。巡の末尾で違反起点修復へ渡して空にする。</summary>
        public List<CombinatorialRepair.Candidate> RejectedPool { get; } = new();

        public PostChain(Action<string>? onPhase, int[][] schedule)
        {
            _onPhase = onPhase;
            Work = schedule.Copy2D();
        }

        /// <summary>フェーズ名を UI へ通知し、所要 ms を <paramref name="key"/> に累算しながら <paramref name="block"/> を実行する。</summary>
        public R Timed<R>(string phase, string key, Func<int[][], R> block)
        {
            _onPhase?.Invoke(phase);
            var t = EngineClock.NowMs();
            var r = block(Work);
            var elapsed = EngineClock.NowMs() - t;
            PassMs[key] = PassMs.TryGetValue(key, out var cur) ? cur + elapsed : elapsed;
            return r;
        }

        /// <summary>結果を盤面へ反映し、ピン帰属を合流させ、<paramref name="keepLogs"/> のときだけログを積む。採用数を返す。</summary>
        public int Adopt(CyclicSwapResult r, bool keepLogs = true)
        {
            if (r.PinBlocks != null) PinBlocksAll.Merge(r.PinBlocks);
            if (r.RejectedCandidates != null) RejectedPool.AddRange(r.RejectedCandidates);
            Work = r.NewSchedule.Copy2D();
            if (keepLogs) Logs.AddRange(r.Logs);
            return r.Applied;
        }

        public void Adopt(DayAssignResult r)
        {
            if (r.PinBlocks != null) PinBlocksAll.Merge(r.PinBlocks);
            Work = r.NewSchedule.Copy2D();
            Logs.AddRange(r.Logs);
        }

        public void ReplaceBoard(int[][] newSchedule, IReadOnlyList<MirrorLog> passLogs)
        {
            Work = newSchedule.Copy2D();
            Logs.AddRange(passLogs);
        }
    }

    /// <summary>
    /// [review: budget] 後処理チェーン HF80 → HF67 → HF66 → 厳密日割当 → 巡回研磨クラスタ（最大 MaxRounds 巡）→ 曜日/交互研磨 →
    /// 共同 LNS 2 本 → HF70。全パス keep-best（正式チェッカーの HARD→weighted→total）なので順序・巡回数・予算配分は
    /// 「時間の使い方」だけを変え、退化はしない。<paramref name="shouldStop"/> は全体予算超過とキャンセルを束ねる。
    /// HF70（異常検知＝安価）は診断のため常に実行する。<paramref name="onPhase"/> は各パス開始時に UI 進捗へ。
    /// </summary>
    public static V6PostOptimizationResult RunPostOptimization(
        MagiState state,
        int[][] schedule,
        string algoName,
        long? seed = null,
        Func<bool>? shouldStop = null,
        Action<string>? onPhase = null,
        long deadlineMs = long.MaxValue,
        PostOptimizationParams? parameters = null)
    {
        var p = parameters ?? new PostOptimizationParams();
        var seedVal = seed ?? System.Diagnostics.Stopwatch.GetTimestamp();
        var stop = shouldStop ?? (() => false);
        var chain = new PostChain(onPhase, schedule);
        var t0 = EngineClock.NowMs();

        var r80 = chain.Timed("後処理 HF80 戦略的振動", "HF80StrategicOscillation", work =>
            ApplyHF80StrategicOscillation(state, work, maxCycles: p.Hf80MaxCycles, seed: seedVal ^ SeedTag.Hf80, shouldStop: stop));
        chain.ReplaceBoard(r80.NewSchedule, r80.Logs);

        var t67 = EngineClock.NowMs();
        var r67 = chain.Timed("後処理 HF67 職員間スワップ", "HF67InterStaffSwap", work =>
        {
            var cap = Math.Min(Math.Max(deadlineMs - t67, 0L) / 2, p.Hf67CapMs);
            return ApplyHF67InterStaffSwap(state, work, maxSwaps: p.Hf67MaxSwaps, shouldStop: stop, deadlineMs: t67 + cap);
        });
        chain.ReplaceBoard(r67.NewSchedule, r67.Logs);

        var t66 = EngineClock.NowMs();
        var r66 = chain.Timed("後処理 HF66 職員内再配分", "HF66IntraStaffRedistribution", work =>
        {
            // HF66 は手ごとに全候補をフル check する高コストパス＝残予算の半分（後段の研磨群へ残り半分）で打ち切る。
            var cap = Math.Min(Math.Max(deadlineMs - t66, 0L) / 2, p.Hf66CapMs);
            return ApplyHF66IntraStaffRedistribution(state, work, maxMoves: p.Hf66MaxMoves, shouldStop: stop, deadlineMs: t66 + cap);
        });
        chain.ReplaceBoard(r66.NewSchedule, r66.Logs);
        var t66Done = EngineClock.NowMs();

        // 巡回研磨クラスタは自身の締切を持たないため、共同 LNS 2 本の取り分を先に確保して ClusterStop に畳む（3.271.0）。
        var jointLnsReserve = deadlineMs == long.MaxValue ? 0L
            : Math.Min(Math.Max(deadlineMs - t66Done, 0L) / 2, p.JointLnsReserveMaxMs);
        var clusterDeadline = deadlineMs == long.MaxValue ? long.MaxValue : deadlineMs - jointLnsReserve;
        bool ClusterStop() => stop() || EngineClock.NowMs() >= clusterDeadline;

        chain.Adopt(chain.Timed("後処理 厳密日割当", "DayAssignmentPolish", work =>
            ApplyDayAssignmentPolish(state, work, shouldStop: ClusterStop)));

        var preSoftRep = UnifiedViolationChecker.Check(state, chain.Work);
        var c1Plateau = RunPolishCluster(state, chain, p, seedVal, ClusterStop, preSoftRep);

        // weekly は同日 2 者スワップでは動かない（曜日別の勤務/休が不変）→ 被覆保存の 2 職員×2 日 長方形交換。
        chain.Adopt(chain.Timed("後処理 曜日平準化(長方形交換)", "WeeklyRebalancePolish", work =>
            ApplyWeeklyRebalancePolish(state, work, maxPasses: p.WeeklyRebalancePasses, shouldStop: ClusterStop)));
        // 長方形交換（クロス日）が届かない同日内の割当先を Hungarian で再配置＝相補的なので両方走らせる。
        chain.Adopt(chain.Timed("後処理 交互最適化(日ブロック割当)", "AlternatingSoftPolish", work =>
            ApplyAlternatingSoftPolish(state, work, maxSweeps: p.AlternatingSweeps, shouldStop: ClusterStop)));

        // 最終 LNS 2 本（高コストなので巡回ループでなく最終 1 回）。残予算は既定比 8:6 で按分（3.255.0）。
        var tC1Lns = EngineClock.NowMs();
        var lnsTotal = p.C1LnsMaxMs + p.PersonalLnsMaxMs;
        chain.Adopt(chain.Timed("後処理 期間要件(c1)共同LNS", "C1共同LNS", work =>
        {
            var remaining = Math.Min(Math.Max(deadlineMs - tC1Lns, 0L), p.RemainingClampMs);
            var cap = lnsTotal <= 0L ? 0L : Math.Min(remaining * p.C1LnsMaxMs / lnsTotal, p.C1LnsMaxMs);
            return C1RepairOperators.JointLns(state, work, config: new C1JointLnsPolish.Config(MaxMillis: cap), shouldStop: stop);
        }));
        var tPersonalLns = EngineClock.NowMs();
        chain.Adopt(chain.Timed("後処理 個人回数/適切回数 共同LNS", "個人回数共同LNS", work =>
        {
            var cap = Math.Min(Math.Max(deadlineMs - tPersonalLns, 0L), p.PersonalLnsMaxMs);
            return PersonalBalanceJointLnsPolish.Apply(state, work, config: new PersonalBalanceJointLnsPolish.Config(MaxMillis: cap), shouldStop: stop);
        }));

        var tHf = EngineClock.NowMs();
        if (stop())
        {
            chain.Logs.Add(new MirrorLog(level: "W", tag: "POST",
                message: "予算超過のため後処理は締切で短縮されました(各パスは内部で打ち切り済み・以降は最終検査のみ)"));
        }

        onPhase?.Invoke("後処理 HF70 異常検知");
        var work = chain.Work;
        var report = UnifiedViolationChecker.Check(state, work);
        var r70 = DetectHF70Anomalies(state, work, algoName, report);
        chain.Logs.AddRange(r70.Logs);

        var tEnd = EngineClock.NowMs();
        chain.Logs.Add(new MirrorLog(level: "I", tag: "POST",
            message: $"後処理タイミング 総{tEnd - t0}ms: HF80={t67 - t0}ms HF67={t66 - t67}ms HF66={t66Done - t66}ms" +
                $" 巡回研磨(厳密日割当+c1/c3/range/apt/fair+曜日/交互)={tC1Lns - t66Done}ms" +
                $" C1共同LNS={tPersonalLns - tC1Lns}ms 個人共同LNS={tHf - tPersonalLns}ms" +
                $" 最終検査+HF70={tEnd - tHf}ms"));
        // パスごとの内訳（多い順・上位 N）。「時間を食っているのに採用0」のパスが各パス自身の行と突き合わせられる（3.339.0）。
        if (chain.PassMs.Count > 0)
        {
            var sum = Math.Max(chain.PassMs.Values.Sum(), 1L);
            chain.Logs.Add(new MirrorLog(level: "I", tag: "POST",
                message: $"後処理パス別 計{sum}ms: " + string.Join(" ", chain.PassMs
                    .OrderByDescending(kv => kv.Value)
                    .Take(p.PassLogTopN)
                    .Select(kv => $"{kv.Key}={kv.Value}ms({kv.Value * 100 / sum}%)"))));
        }

        var plateauOut = FinalC1Plateau(state, work, report, c1Plateau);
        var allLogs = new List<MirrorLog>(chain.Logs);
        allLogs.AddRange(report.Logs);
        return new V6PostOptimizationResult(
            work, report with { Logs = allLogs }, r80, r67, r66, r70, chain.Logs,
            plateauOut, chain.PinBlocksAll.Attempts, chain.PinBlocksAll);
    }

    /// <summary>
    /// 巡回研磨クラスタ（循環交換〜fair 玉突き）を「1 巡で 1 手も採用されなくなるまで」最大 MaxRounds 巡繰り返す。
    /// 各パスは内部で自己収束するが、別パスの変更が他パスの改善を再び開く。各パスの個別ログは巡 1 だけ積み、
    /// SoftPolishVerify の集約行は全巡合計を出す。C1 研磨の構造化診断（巡ごとに合算）を返す。
    /// </summary>
    private static C1PlateauDiagnosis? RunPolishCluster(
        MagiState state, PostChain chain, PostOptimizationParams p, long seedVal, Func<bool> clusterStop, ViolationReport preSoftRep)
    {
        var adopted = new Dictionary<string, int>();
        foreach (var k in AdoptionKeys) adopted[k] = 0;
        var c3Anchor = new HashSet<string> { "vio-c3", "vio-c3m", "vio-c3mn" };
        var pC1 = new Problem(state);   // state の純関数＝巡回間で不変（C1DeltaPrefilter のゲート用）
        C1PlateauDiagnosis? c1Plateau = null;
        var round = 0;
        while (round < p.MaxRounds && !clusterStop())
        {
            var first = round == 0;
            var tag = $" [巡{round + 1}]";
            var roundApplied = 0;
            void Take(string key, CyclicSwapResult r)
            {
                var n = chain.Adopt(r, keepLogs: first);
                adopted[key] += n;
                roundApplied += n;
            }

            Take("循環", chain.Timed($"後処理 循環交換(k=2,3){tag}", "CyclicSwapPolish", work =>
                ApplyCyclicSwapPolish(state, work, maxPasses: p.CyclicSwapPasses, shouldStop: clusterStop)));

            // c1 違反セルに厳密アンカーする 2 op は、不足窓が無ければ必ず no-op＝C1DeltaPrefilter で 1 回判定して飛ばす（3.275.0/3.276.0）。
            if (C1DeltaPrefilter.HasActionableC1(C1RepairIndex.Build(pC1, chain.Work)))
            {
                var rC1 = chain.Timed($"後処理 期間要件(c1)研磨{tag}", "C1同日交換", work =>
                    C1RepairOperators.SelfRelocateAndSameDaySwap(state, work, maxPasses: p.C1WindowPasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C1Window, round)));
                Take("c1", rC1);
                // 構造化診断は巡ごとに合算（3.331.0。最後の巡だけだと観測が減る）。末尾で最終盤面に対して再フィルタする。
                if (rC1.Plateau != null) c1Plateau = c1Plateau?.MergedWith(rC1.Plateau) ?? rC1.Plateau;
                Take("c1", chain.Timed($"後処理 期間要件(c1)index駆動修復{tag}", "C1索引修復", work =>
                    C1RepairOperators.IndexChainRepair(state, work, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C1Index, round))));
            }
            // 時系列 DP＋同日ジョイント再割当（3.254.0 の ablation で一本化）。広域ビームより前に置く。
            Take("c1", chain.Timed($"後処理 期間要件(c1)時系列DP+ジョイント再割当研磨{tag}", "C1時系列フロー", work =>
                C1RepairOperators.TemporalFlow(state, work, maxPasses: p.C1FlowPasses, maxRelocations: p.C1FlowRelocations, trials: p.C1FlowTrials,
                    shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C1Flow, round))));
            Take("c1", chain.Timed($"後処理 期間要件(c1)広域ビーム研磨{tag}", "C1広域ビーム", work =>
                C1RepairOperators.WideBeam(state, work, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C1Beam, round))));
            Take("c1", chain.Timed($"後処理 期間要件(c1)厳密窓修復{tag}", "C1厳密窓", work =>
                C1RepairOperators.ExactWindow(state, work, shouldStop: clusterStop)));

            var rC3 = chain.Timed($"後処理 連続規則(c3系)研磨{tag}", "C3SequencePolish", work =>
                ApplyC3SequencePolish(state, work, maxPasses: p.C3SequencePasses, shouldStop: clusterStop));
            Take("c3", rC3);
            // 3 者回転は O(候補^3) で通常時の寄与ゼロ（3.300.0 ablation）＝主手が詰まった巡と最終巡だけの脱出手。
            if (rC3.Applied == 0 || round == p.MaxRounds - 1)
            {
                Take("c3回転", chain.Timed($"後処理 連続規則(c3系)3者回転研磨{tag}", "BlockRotationPolish", work =>
                    ApplyBlockRotationPolish(state, work, c3Anchor, "C3Rotate", maxPasses: p.C3RotatePasses, shouldStop: clusterStop)));
            }
            Take("c3mn玉突き", chain.Timed($"後処理 回避パターン(c3mn)玉突き研磨{tag}", "C3mnPolish", work =>
                ApplyC3mnPolish(state, work, maxPasses: p.C3mnPasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C3mn, round))));
            Take("c3n", chain.Timed($"後処理 禁止連続(c3n)研磨{tag}", "C3nPolish", work =>
                ApplyC3nPolish(state, work, maxPasses: p.C3nPasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C3n, round))));
            Take("range玉突き", chain.Timed($"後処理 個人回数(low/high)玉突き研磨{tag}", "RangePolish", work =>
                ApplyRangePolish(state, work, maxPasses: p.RangePasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.Range, round))));
            Take("c3run玉突き", chain.Timed($"後処理 連続規則(c3/c3m単一シフト連)玉突き研磨{tag}", "C3RunPolish", work =>
                ApplyC3RunPolish(state, work, maxPasses: p.C3RunPasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C3Run, round))));
            Take("c3pattern玉突き", chain.Timed($"後処理 連続規則(c3/c3m複数シフトパターン)玉突き研磨{tag}", "C3PatternPolish", work =>
                ApplyC3PatternPolish(state, work, maxPasses: p.C3PatternPasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.C3Pattern, round))));
            Take("アンカー窓交換", chain.Timed($"後処理 違反アンカー窓交換{tag}", "AnchoredWindowSwap", work =>
                ApplyAdaptiveBlockSwapPolish(state, work, maxPasses: p.AnchorWindowPasses, maxEvaluations: p.AnchorWindowEvaluations,
                    shouldStop: clusterStop, mode: WindowMode.StrictWholeWindow)));
            Take("希望島", chain.Timed($"後処理 希望島研磨{tag}", "WishIslandPolish", work =>
                ApplyWishIslandPolish(state, work, maxPasses: p.WishIslandPasses, maxEvaluations: p.WishIslandEvaluations, shouldStop: clusterStop)));
            Take("ブロック交換", chain.Timed($"後処理 長期ブロック丸ごと交換(11/13/17/19/23/28日){tag}", "AdaptiveBlockSwapPolish", work =>
                ApplyAdaptiveBlockSwapPolish(state, work, maxPasses: p.BlockSwapPasses, candidatesPerLength: p.BlockSwapCandidatesPerLength,
                    maxEvaluations: p.BlockSwapEvaluations, shouldStop: clusterStop)));
            Take("apt玉突き", chain.Timed($"後処理 適切回数(apt)研磨{tag}", "AptPolish", work =>
                ApplyAptPolish(state, work, maxPasses: p.AptPasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.Apt, round))));
            Take("fair玉突き", chain.Timed($"後処理 グループ内公平化(fair)玉突き研磨{tag}", "FairPolish", work =>
                ApplyFairPolish(state, work, maxPasses: p.FairPasses, shouldStop: clusterStop, seed: RoundSeed(seedVal, SeedTag.Fair, round))));
            // [Iteration 2] 巡の中で各パスが単独では不採用にした候補を、違反起点のトランザクションに束ねる。
            var pool = chain.RejectedPool.ToList(); chain.RejectedPool.Clear();
            if (p.ComponentRepairEnabled && pool.Count >= 2)
            {
                Take("成分修復", chain.Timed($"後処理 違反連結成分修復{tag}", "ComponentRepair", work =>
                    ViolationComponentRepair.Repair(state, work, pool, p.ComponentRepair, shouldStop: clusterStop)));
            }

            round++;
            if (roundApplied == 0) break; // この巡で 1 手も採用なし＝joint 局所最適に到達
        }

        chain.Logs.Add(SoftPolishVerifyLog(state, chain.Work, preSoftRep, round, adopted));
        return c1Plateau;
    }

    /// <summary>研磨可否の検証ログ。採用 0 かつ対象 &gt; 0 なら「頭打ち（正常）」、対象 0 なら「対象なし」と明示する。</summary>
    private static MirrorLog SoftPolishVerifyLog(MagiState state, int[][] work, ViolationReport preSoftRep, int rounds, Dictionary<string, int> adopted)
    {
        var softAfter = UnifiedViolationChecker.Check(state, work);
        int Bd(ViolationReport r, string k) => r.Breakdown.GetValueOrDefault(k, 0);
        var adoptedTotal = adopted.Values.Sum();
        var targets = SoftTargetFamilies.Sum(k => Bd(preSoftRep, k));
        var verdict = adoptedTotal > 0 ? $"有効(採用{adoptedTotal}手)"
            : targets == 0 ? "対象なし"
            : "頭打ち(採用0=改善手なし・正常)";
        var hardNote = softAfter.Hard == preSoftRep.Hard ? "不変" : $"変化{preSoftRep.Hard}->{softAfter.Hard}!";
        return new MirrorLog(tag: "SoftPolishVerify", message:
            $"ソフトc1/c3系研磨 可否={verdict} ({rounds}巡・各パス行は巡1のみ表示/本行は全巡合計) | c1 {Bd(preSoftRep, "c1")}->{Bd(softAfter, "c1")}" +
            $" / c3 {Bd(preSoftRep, "c3")}->{Bd(softAfter, "c3")}" +
            $" / c3m {Bd(preSoftRep, "c3m")}->{Bd(softAfter, "c3m")}" +
            $" / c3mn {Bd(preSoftRep, "c3mn")}->{Bd(softAfter, "c3mn")}" +
            $" / low {Bd(preSoftRep, "low")}->{Bd(softAfter, "low")}" +
            $" / high {Bd(preSoftRep, "high")}->{Bd(softAfter, "high")}" +
            $" / apt {Bd(preSoftRep, "apt")}->{Bd(softAfter, "apt")}" +
            $" / fair {Bd(preSoftRep, "fair")}->{Bd(softAfter, "fair")}" +
            $" | HARD {hardNote} / total {preSoftRep.Total}->{softAfter.Total}" +
            " (採用内訳 " + string.Join(" ", AdoptionKeys.Select(k => $"{k}:{adopted[k]}")) + ")");
    }

    /// <summary>
    /// C1 研磨の時点で作った構造化診断（3.322.0）を最終盤面に合わせ直す（共同 LNS 等が直した箇所を「直せなかった」と見せない）。
    /// c1 が残っているなら観測が 1 件も無くても診断を返す＝UI が「原因未確定」と出す（3.325.0）。
    /// </summary>
    private static C1PlateauDiagnosis? FinalC1Plateau(MagiState state, int[][] work, ViolationReport report, C1PlateauDiagnosis? plateau)
    {
        var c1Left = report.Breakdown.GetValueOrDefault("c1", 0);
        C1PlateauDiagnosis? refreshed = null;
        if (plateau != null)
        {
            var pFin = ScheduleUtil.CachedProblem(state);
            bool StillDeficient(int i, int x, int ri)
            {
                var c = ri >= 0 && ri < pFin.Cons1.Count ? pFin.Cons1[ri] : null;
                if (c == null || c.ShiftIdx != x || c.Day1 <= 0) return false;
                for (var j = 0; j <= pFin.T - c.Day1; j++)
                    if (InDeficientC1Window(pFin, work, i, x, c.Day1, c.Day2, j)) return true;
                return false;
            }
            refreshed = plateau.RefreshedAgainst(c1Left, StillDeficient);
        }
        return refreshed != null && (refreshed.HasEntries || refreshed.CauseUnknown)
            ? refreshed
            : c1Left > 0 ? new C1PlateauDiagnosis(c1Left, Array.Empty<C1PlateauEntry>())
            : null;
    }
}
