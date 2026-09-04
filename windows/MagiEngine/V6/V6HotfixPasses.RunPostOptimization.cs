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
    /// [フェーズ6, ピース30] Kotlin原本 <c>runPostOptimization</c>（<c>V6HotfixPasses.kt</c> 328-796行）の
    /// 忠実な移植。後処理研磨の統括:
    ///
    ///  HF80(戦略的振動) → HF67(職員間スワップ) → HF66(職員内再配分) → 厳密日割当 →
    ///  [フィックスポイント巡回・最大4巡: 循環交換(k=2,3) → C1同日交換+index駆動修復(不足窓ゼロならスキップ) →
    ///   C1時系列フロー → C1広域ビーム → C1厳密窓 → C3系研磨 → (停滞巡/最終巡のみ)C3系3者回転 →
    ///   C3mn玉突き → C3n研磨 → 個人回数(low/high)玉突き → C3/C3m単一シフト連玉突き →
    ///   C3/C3m複数シフトパターン玉突き → 長期ブロック丸ごと交換 → 適切回数(apt)研磨 →
    ///   グループ内公平化(fair)玉突き] → 曜日平準化(長方形交換) → 交互最適化(日ブロック割当) →
    ///   C1共同LNS → 個人回数/適切回数共同LNS → HF70(異常検知)。
    ///
    /// 全パス keep-best（<see cref="UnifiedViolationChecker"/> + <see cref="IsBetter"/>）＝退化不能。
    /// 重み・パラメータ・目的関数は一切変更しない（読取専用の後処理オーケストレーションのみ）。
    ///
    /// [C#化の注記]
    ///  - Kotlin の既定引数 <c>seed: Long = System.nanoTime()</c>／<c>shouldStop: () -&gt; Boolean = { false }</c>
    ///    は非定数式のため、他ピースと同じ null許容化＋本体内 null合体パターンへ翻訳した。
    ///  - <c>onPhase: (String) -&gt; Unit = {}</c> は、既存の <c>onProgress</c> 系コールバックと同じ確立済み
    ///    パターン（<c>Action&lt;string&gt;? onPhase = null</c> ＋ 呼出側 <c>onPhase?.Invoke(...)</c>）へ。
    ///  - <c>System.currentTimeMillis()</c> の頻出（後処理タイミング計測が20箇所超）を、このメソッド内に
    ///    限定した local function <c>NowMs()</c> へ集約した（挙動は <c>DateTimeOffset.UtcNow.
    ///    ToUnixTimeMilliseconds()</c> と同一・可読性のためだけの局所的な集約で新しい抽象は導入しない）。
    ///  - <c>LinkedHashMap&lt;String, Long&gt;().merge(key, elapsed) { a, b -&gt; a + b }</c>（パス別消費msの
    ///    累算）は、この <c>passMs</c> が最終的に値でソートされるだけ（挿入順に意味が無い）ため、
    ///    順序無保証の <c>Dictionary&lt;string, long&gt;</c> ＋ local function <c>MergePassMs</c> でそのまま
    ///    置き換えられる（<see cref="C1PlateauDiagnosis.MergedWith"/> の挿入順保持ケースとは事情が異なる）。
    /// </summary>
    public static V6PostOptimizationResult RunPostOptimization(
        MagiState state,
        int[][] schedule,
        string algoName,
        long? seed = null,
        Func<bool>? shouldStop = null,
        Action<string>? onPhase = null,
        long deadlineMs = long.MaxValue)
    {
        var seedVal = seed ?? System.Diagnostics.Stopwatch.GetTimestamp();
        var stop = shouldStop ?? (() => false);
        long NowMs() => EngineClock.NowMs();

        var work = schedule.Copy2D();
        var logs = new List<MirrorLog>();
        var t0 = NowMs();
        // [3.339.0/敵対レビューA4] パスごとの消費ms。3.269.0の区間分割（HF80/HF67/HF66/巡回研磨/共同LNS×2）
        //   は「巡回研磨」が18パスの合計で、どのパスが時間を食っているかが見えなかった（読取専用）。
        var passMs = new Dictionary<string, long>();
        void MergePassMs(string key, long elapsed) =>
            passMs[key] = passMs.TryGetValue(key, out var cur) ? cur + elapsed : elapsed;

        onPhase?.Invoke("後処理 HF80 戦略的振動");
        var t80 = NowMs();
        var __t0 = NowMs();
        var r80 = ApplyHF80StrategicOscillation(state, work, maxCycles: 3, seed: seedVal ^ 0x80L, shouldStop: stop);
        MergePassMs("HF80StrategicOscillation", NowMs() - __t0);
        work = r80.NewSchedule.Copy2D();
        logs.AddRange(r80.Logs);

        onPhase?.Invoke("後処理 HF67 職員間スワップ");
        var t67 = NowMs();
        // [3.282.0] HF66と同型の専用上限（残り予算の半分・絶対上限3s）。実機実測は数十ms＝通常は無影響で、
        //   大規模データでのフォールバック総当たり暴走だけを防ぐ保険。
        var hf67Cap = Math.Min(Math.Max(deadlineMs - t67, 0L) / 2, 3_000L);
        var __t1 = NowMs();
        var r67 = ApplyHF67InterStaffSwap(state, work, maxSwaps: 30, shouldStop: stop, deadlineMs: t67 + hf67Cap);
        MergePassMs("HF67InterStaffSwap", NowMs() - __t1);
        work = r67.NewSchedule.Copy2D();
        logs.AddRange(r67.Logs);

        onPhase?.Invoke("後処理 HF66 職員内再配分");
        var t66 = NowMs();
        // [残予算ガード] HF66は手ごとに全候補をフル check する高コストパス。残予算の半分まで(残り半分を
        //   後段の研磨群へ確保)＋絶対上限6sで打ち切り、暴走で後続パスを予算超過で打ち切らせない。
        var hf66Cap = Math.Min(Math.Max(deadlineMs - t66, 0L) / 2, 6_000L);
        var __t2 = NowMs();
        var r66 = ApplyHF66IntraStaffRedistribution(state, work, maxMoves: 30, shouldStop: stop, deadlineMs: t66 + hf66Cap);
        MergePassMs("HF66IntraStaffRedistribution", NowMs() - __t2);
        work = r66.NewSchedule.Copy2D();
        logs.AddRange(r66.Logs);
        var t66Done = NowMs();

        // [3.271.0, 実機ログ2本連続で実証された飢餓の解消] 巡回研磨クラスタ（厳密日割当〜曜日平準化）は
        //   自身の締切を持たずshouldStop（全体予算）だけで走るため、探索フェーズが予算を使い切る実運用では
        //   後処理予約枠(8〜25s)を丸ごと消費し、後段のC1共同LNS/個人共同LNSが毎回「探索上限0=明示的に無効」
        //   でスキップされていた（両パスは実データでHARD削減の実績があるのに本番では一度も走れない＝事実上の
        //   死に機能）。HF66の予算按分と同じ考え方で、クラスタ開始時点の残予算の半分（上限14s=両LNSの既定
        //   合計8s+6s）を共同LNS用に確保し、クラスタにはclusterStop（自前の締切つき）を渡す。クラスタが早期
        //   にフィックスポイント到達すれば共同LNSは確保分より多く使える（従来挙動と同一）。全パスkeep-best
        //   のため時間配分の変更のみ＝退化不能。
        var jointLnsReserve = deadlineMs == long.MaxValue ? 0L
            : Math.Min(Math.Max(deadlineMs - t66Done, 0L) / 2, 14_000L);
        var clusterDeadline = deadlineMs == long.MaxValue ? long.MaxValue : deadlineMs - jointLnsReserve;
        bool ClusterStop() => stop() || NowMs() >= clusterDeadline;

        // [3.326.0] 全研磨パス横断で「回数固定だけが却下した候補試行」を対象別に合算する
        //   （isBetterは採用を認めていた手＝緩めれば通ったはずの手）。最初の使用より前で宣言する。
        var pinBlocksAll = new PinBlockAttribution();

        onPhase?.Invoke("後処理 厳密日割当");
        var __t3 = NowMs();
        var rAsg = ApplyDayAssignmentPolish(state, work, shouldStop: ClusterStop);
        MergePassMs("DayAssignmentPolish", NowMs() - __t3);
        if (rAsg.PinBlocks != null) pinBlocksAll.Merge(rAsg.PinBlocks);
        work = rAsg.NewSchedule.Copy2D();
        logs.AddRange(rAsg.Logs);

        // [研磨可否の検証] ソフト研磨クラスタ(循環/c1/c1回転/c3/c3回転)の前後を測る基準。
        var preSoftRep = UnifiedViolationChecker.Check(state, work);

        // [パス間フィックスポイント再ループ] 各パスは内部で自己収束するが、別パスの変更が他パスの改善を
        //   再び開く（例: c3の組替えで新たなc1充足余地が出る）。クラスタ全体を「1巡で1手も採用されなく
        //   なるまで」最大maxRounds巡だけ繰り返す。全パスkeep-best＝退化なし。shouldStopとmaxRoundsで上限。
        //   違反セル指向なので空巡は即終了（コスト0）。
        var c3Anchor = new HashSet<string> { "vio-c3", "vio-c3m", "vio-c3mn" };
        const int maxRounds = 4;
        // [C1RepairIndex/3.275.0] c1不足窓の索引用Problem（stateの純関数＝巡回間で不変。各オペレータが
        //   内部で構築するProblem(state)と同一）。C1DeltaPrefilterのクラスタ前段ゲートに使う。
        var pC1 = new Problem(state);
        var round = 0;
        C1PlateauDiagnosis? c1Plateau = null;
        var totalCyc = 0; var totalC1 = 0; var totalC3 = 0; var totalC3r = 0; var totalC3mn = 0; var totalC3n = 0;
        var totalRange = 0; var totalC3run = 0; var totalC3pat = 0; var totalAnchorSwap = 0; var totalBlockSwap = 0; var totalApt = 0; var totalFair = 0;
        while (round < maxRounds && !ClusterStop())
        {
            var roundApplied = 0;

            onPhase?.Invoke($"後処理 循環交換(k=2,3) [巡{round + 1}]");
            var __t4 = NowMs();
            var rCyc = ApplyCyclicSwapPolish(state, work, maxPasses: 4, shouldStop: ClusterStop);
            MergePassMs("CyclicSwapPolish", NowMs() - __t4);
            if (rCyc.PinBlocks != null) pinBlocksAll.Merge(rCyc.PinBlocks);
            work = rCyc.NewSchedule.Copy2D(); totalCyc += rCyc.Applied; roundApplied += rCyc.Applied;
            if (round == 0) logs.AddRange(rCyc.Logs);

            // [C1RepairOperatorsファサード/3.275.0] 散在していたC1オペレータを図の1層へ集約（1:1委譲＝挙動同一）。
            //   自己内移設+同日swap(ApplyC1WindowPolish)はc1違反セルに厳密アンカーする＝不足窓ゼロなら必ずno-op。
            //   C1DeltaPrefilterで不足窓の有無を1回判定し、無ければ本オペレータのみ安全にスキップする
            //   （Index/Prefilterをhot pathで実際に使う唯一のprovably-safeな地点）。他3op(temporalFlow/
            //   wideBeam/exact)はc1中立のtotal改善手を出し得る／独自の内部ゲートを持つためgateせず従来どおり実行。
            var c1Index = C1RepairIndex.Build(pC1, work);
            if (C1DeltaPrefilter.HasActionableC1(c1Index))
            {
                onPhase?.Invoke($"後処理 期間要件(c1)研磨 [巡{round + 1}]");
                var __t5 = NowMs();
                var rC1 = C1RepairOperators.SelfRelocateAndSameDaySwap(
                    state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0x1C1L, round));
                MergePassMs("C1同日交換", NowMs() - __t5);
                work = rC1.NewSchedule.Copy2D(); totalC1 += rC1.Applied; roundApplied += rC1.Applied;
                if (round == 0) logs.AddRange(rC1.Logs);
                // [構造化診断, 3.322.0/3.331.0] 巡ごとに上書きせず合算する。旧は最後の巡だけが残り、
                //   2巡目は1巡目が直したあとの盤面を見るので観測が少なく説明できる箇所が減っていた。
                if (rC1.Plateau != null) c1Plateau = c1Plateau?.MergedWith(rC1.Plateau) ?? rC1.Plateau;
                if (rC1.PinBlocks != null) pinBlocksAll.Merge(rC1.PinBlocks);

                // [C1IndexRepair/3.276.0] index駆動の候補生成＋prefilter選別＋玉突き連鎖。C1RepairIndex/
                //   C1DeltaPrefilterを実駆動する経路。厳密c1アンカー＝不足窓ゼロでno-opのため本ゲート内に配置。
                //   生成する手は既存手B/beam/exactと重複しうるがkeep-bestで無害（退化不能）。
                onPhase?.Invoke($"後処理 期間要件(c1)index駆動修復 [巡{round + 1}]");
                var __t6 = NowMs();
                var rC1idx = C1RepairOperators.IndexChainRepair(
                    state, work, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0x1C1D2L, round));
                MergePassMs("C1索引修復", NowMs() - __t6);
                if (rC1idx.PinBlocks != null) pinBlocksAll.Merge(rC1idx.PinBlocks);
                work = rC1idx.NewSchedule.Copy2D(); totalC1 += rC1idx.Applied; roundApplied += rC1idx.Applied;
                if (round == 0) logs.AddRange(rC1idx.Logs);
            }

            // [C1TemporalFlowPolish, C1時系列DP+ジョイント再割当研磨] DPが選ぶ目標パターンを、同日全員参加
            //   min-cost flow(FlexibleDayFlow)によるジョイント再割当へ実現する。順序が重要（BeamWideの前）。
            onPhase?.Invoke($"後処理 期間要件(c1)時系列DP+ジョイント再割当研磨 [巡{round + 1}]");
            var __t7 = NowMs();
            var rC1flow = C1RepairOperators.TemporalFlow(
                state, work, maxPasses: 2, maxRelocations: 4, trials: 4,
                shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xC1F10L, round));
            MergePassMs("C1時系列フロー", NowMs() - __t7);
            work = rC1flow.NewSchedule.Copy2D(); totalC1 += rC1flow.Applied; roundApplied += rC1flow.Applied;
            if (rC1flow.PinBlocks != null) pinBlocksAll.Merge(rC1flow.PinBlocks);
            if (round == 0) logs.AddRange(rC1flow.Logs);

            // [C1BeamPolish] BeamC1PolishV2(厳密な単発bundle採否)とは別系統の、より広い時空間ビーム探索。
            onPhase?.Invoke($"後処理 期間要件(c1)広域ビーム研磨 [巡{round + 1}]");
            var __t8 = NowMs();
            var rC1wide = C1RepairOperators.WideBeam(
                state, work, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xC1BEAL, round));
            MergePassMs("C1広域ビーム", NowMs() - __t8);
            work = rC1wide.NewSchedule.Copy2D(); totalC1 += rC1wide.Applied; roundApplied += rC1wide.Applied;
            // [3.409.9] 広域ビームはPinBlockAttributionを作って返すのに、ここだけ合流を書き忘れていた
            //   （他20サイトは全てmerge済み＝この1つだけが終端の「回数の固定について」から抜けていた）。
            if (rC1wide.PinBlocks != null) pinBlocksAll.Merge(rC1wide.PinBlocks);
            if (round == 0) logs.AddRange(rC1wide.Logs);

            // [A2/A3厳密窓修復] 上記の局所/ビーム系が届かない「別日で連動して初めて解ける多職員手」を、
            //   窓スコープのcoverage保存permutation厳密探索で拾う（純Kotlin・依存ゼロ）。A1=解析駆動
            //   ディスパッチ: 証明された解消不能スパン(exhaustive && min==base)をmemoで二度解かない。
            onPhase?.Invoke($"後処理 期間要件(c1)厳密窓修復 [巡{round + 1}]");
            var __t9 = NowMs();
            var rC1exact = C1RepairOperators.ExactWindow(state, work, shouldStop: ClusterStop);
            MergePassMs("C1厳密窓", NowMs() - __t9);
            work = rC1exact.NewSchedule.Copy2D(); totalC1 += rC1exact.Applied; roundApplied += rC1exact.Applied;
            if (rC1exact.PinBlocks != null) pinBlocksAll.Merge(rC1exact.PinBlocks);
            if (round == 0) logs.AddRange(rC1exact.Logs);

            onPhase?.Invoke($"後処理 連続規則(c3系)研磨 [巡{round + 1}]");
            var __t10 = NowMs();
            var rC3 = ApplyC3SequencePolish(state, work, maxPasses: 3, shouldStop: ClusterStop);
            MergePassMs("C3SequencePolish", NowMs() - __t10);
            if (rC3.PinBlocks != null) pinBlocksAll.Merge(rC3.PinBlocks);
            work = rC3.NewSchedule.Copy2D(); totalC3 += rC3.Applied; roundApplied += rC3.Applied;
            if (round == 0) logs.AddRange(rC3.Logs);

            // [3.300.0 高コストの脱出手へ格下げ] 3者回転はO(候補^3)の全組合せをフル評価する重い手。
            //   ablation（3データセットで完全に外して実行）の結果、採用0かつ結果がバイト一致＝通常時の
            //   寄与はゼロと実測した（C1用の同じ回転を3.254.0で撤去したのと同じ根拠）。撤去はせず、
            //   主手ApplyC3SequencePolishが1手も採れなかった巡（＝停滞）と最終巡だけに限定する。
            //   別のデータ形状で主手が詰まる局面には従来どおり効く。c3違反が無ければApplyBlockRotationPolish
            //   自身がアンカー0で即returnする＝追加コストなし。
            if (rC3.Applied == 0 || round == maxRounds - 1)
            {
                onPhase?.Invoke($"後処理 連続規則(c3系)3者回転研磨 [巡{round + 1}]");
                var __t11 = NowMs();
                var rC3r = ApplyBlockRotationPolish(state, work, c3Anchor, "C3Rotate", maxPasses: 2, shouldStop: ClusterStop);
                MergePassMs("BlockRotationPolish", NowMs() - __t11);
                if (rC3r.PinBlocks != null) pinBlocksAll.Merge(rC3r.PinBlocks);
                work = rC3r.NewSchedule.Copy2D(); totalC3r += rC3r.Applied; roundApplied += rC3r.Applied;
                if (round == 0) logs.AddRange(rC3r.Logs);
            }

            // [C3mnPolish・玉突き連鎖の横展開] cons3n(HARD)で直接候補が全滅する局面向けにfindCovUChainを
            //   c3mn(回避,SOFT)専用に反映（金沢勇輝のDﾃ4連続実例）。
            onPhase?.Invoke($"後処理 回避パターン(c3mn)玉突き研磨 [巡{round + 1}]");
            var __t12 = NowMs();
            var rC3mn = ApplyC3mnPolish(state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xC3AL, round));
            MergePassMs("C3mnPolish", NowMs() - __t12);
            work = rC3mn.NewSchedule.Copy2D(); totalC3mn += rC3mn.Applied; roundApplied += rC3mn.Applied;
            if (rC3mn.PinBlocks != null) pinBlocksAll.Merge(rC3mn.PinBlocks);
            if (round == 0) logs.AddRange(rC3mn.Logs);

            // [C3nPolish, 3.303.0] 禁止連続(c3n, HARD)を、違反パターンがまたぐ全日（前日・当日・翌日）を
            //   候補にして崩す。当日1セルしか触らない既存機構では3連の先頭に構造的に届かなかった。
            onPhase?.Invoke($"後処理 禁止連続(c3n)研磨 [巡{round + 1}]");
            var __t13 = NowMs();
            var rC3n = ApplyC3nPolish(state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xC3EL, round));
            MergePassMs("C3nPolish", NowMs() - __t13);
            work = rC3n.NewSchedule.Copy2D(); totalC3n += rC3n.Applied; roundApplied += rC3n.Applied;
            if (rC3n.PinBlocks != null) pinBlocksAll.Merge(rC3n.PinBlocks);
            if (round == 0) logs.AddRange(rC3n.Logs);

            // [RangePolish・玉突き連鎖の横展開その2] 個人別回数(low/high)を、交換相手が構造的に存在しない
            //   局面(担当可能シフトが極端に少ない職員等)向けにfindCovUChainで研磨（桒澤美幸のAｱ超過実例）。
            onPhase?.Invoke($"後処理 個人回数(low/high)玉突き研磨 [巡{round + 1}]");
            var __t14 = NowMs();
            var rRange = ApplyRangePolish(state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0x8A9EL, round));
            MergePassMs("RangePolish", NowMs() - __t14);
            work = rRange.NewSchedule.Copy2D(); totalRange += rRange.Applied; roundApplied += rRange.Applied;
            if (rRange.PinBlocks != null) pinBlocksAll.Merge(rRange.PinBlocks);
            if (round == 0) logs.AddRange(rRange.Logs);

            // [C3RunPolish・玉突き連鎖の横展開その3] cons3/cons3m(単一シフト連=run-deficit)を、相互交換の
            //   相手が構造的に存在しない局面向けにfindCovUChainで研磨。
            onPhase?.Invoke($"後処理 連続規則(c3/c3m単一シフト連)玉突き研磨 [巡{round + 1}]");
            var __t15 = NowMs();
            var rC3run = ApplyC3RunPolish(state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xC3A2L, round));
            MergePassMs("C3RunPolish", NowMs() - __t15);
            work = rC3run.NewSchedule.Copy2D(); totalC3run += rC3run.Applied; roundApplied += rC3run.Applied;
            if (rC3run.PinBlocks != null) pinBlocksAll.Merge(rC3run.PinBlocks);
            if (round == 0) logs.AddRange(rC3run.Logs);

            // [C3PatternPolish・玉突き連鎖の横展開その4] 複数シフトc3/c3mパターン(非single-shift)を、
            //   交換相手が構造的に存在しない局面向けにfindCovUChainで研磨。
            onPhase?.Invoke($"後処理 連続規則(c3/c3m複数シフトパターン)玉突き研磨 [巡{round + 1}]");
            var __t16 = NowMs();
            var rC3pat = ApplyC3PatternPolish(state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xC3B4L, round));
            MergePassMs("C3PatternPolish", NowMs() - __t16);
            work = rC3pat.NewSchedule.Copy2D(); totalC3pat += rC3pat.Applied; roundApplied += rC3pat.Applied;
            if (rC3pat.PinBlocks != null) pinBlocksAll.Merge(rC3pat.PinBlocks);
            if (round == 0) logs.AddRange(rC3pat.Logs);

            // [違反アンカー型・可変長ウィンドウ交換, 3.495.0 移植元（3.494.0 の RunSwap を置換）] AdaptiveBlockSwap の
            //   STRICT_WHOLE_WINDOW モード: 違反セル／回数超過・不足／連続規則／週偏りをアンカーに、接する可変長の窓を
            //   同じ日付範囲で一括交換（部分交換しない）。回数不足は相手の対象シフト日から逆引き。pass ごとに最良1手。
            onPhase?.Invoke($"後処理 違反アンカー窓交換 [巡{round + 1}]");
            var __t16b = NowMs();
            var rAnchor = ApplyAdaptiveBlockSwapPolish(state, work, maxPasses: 3, maxEvaluations: 48, shouldStop: ClusterStop, mode: WindowMode.StrictWholeWindow);
            MergePassMs("AnchoredWindowSwap", NowMs() - __t16b);
            work = rAnchor.NewSchedule.Copy2D(); totalAnchorSwap += rAnchor.Applied; roundApplied += rAnchor.Applied;
            if (rAnchor.PinBlocks != null) pinBlocksAll.Merge(rAnchor.PinBlocks);
            if (round == 0) logs.AddRange(rAnchor.Logs);

            // [AdaptiveBlockSwap・長期ブロック丸ごと2人交換] 15日固定の旧手を、11/13/17/19/23/28日の
            //   非等間隔ポートフォリオへ拡張。同群に限らず、ブロック内の全セルを相互に担当可能な他者も
            //   候補にし、希望固定・厳密ピン・正式スコアの全ガードを通過した最良の1手だけを採用する。
            onPhase?.Invoke($"後処理 長期ブロック丸ごと交換(11/13/17/19/23/28日) [巡{round + 1}]");
            var __t17 = NowMs();
            var rBlockSwap = ApplyAdaptiveBlockSwapPolish(
                state, work, maxPasses: 2, candidatesPerLength: 8, maxEvaluations: 48, shouldStop: ClusterStop);
            MergePassMs("AdaptiveBlockSwapPolish", NowMs() - __t17);
            if (rBlockSwap.PinBlocks != null) pinBlocksAll.Merge(rBlockSwap.PinBlocks);
            work = rBlockSwap.NewSchedule.Copy2D(); totalBlockSwap += rBlockSwap.Applied; roundApplied += rBlockSwap.Applied;
            if (round == 0) logs.AddRange(rBlockSwap.Logs);

            // [AptPolish・適切回数(apt)専用研磨] 自己振替→同一グループ相互交換→玉突きチェーンの順で
            //   apt(重み1)違反を専用に研磨（大島愛の休/Pｼ実例）。
            onPhase?.Invoke($"後処理 適切回数(apt)研磨 [巡{round + 1}]");
            var __t18 = NowMs();
            var rApt = ApplyAptPolish(state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xA97L, round));
            MergePassMs("AptPolish", NowMs() - __t18);
            work = rApt.NewSchedule.Copy2D(); totalApt += rApt.Applied; roundApplied += rApt.Applied;
            if (rApt.PinBlocks != null) pinBlocksAll.Merge(rApt.PinBlocks);
            if (round == 0) logs.AddRange(rApt.Logs);

            // [FairPolish・グループ内公平化(fair)専用研磨] AptPolishと同型の3段構成
            //   （自己振替→同一グループ相互交換→玉突きチェーン）。
            onPhase?.Invoke($"後処理 グループ内公平化(fair)玉突き研磨 [巡{round + 1}]");
            var __t19 = NowMs();
            var rFair = ApplyFairPolish(state, work, maxPasses: 3, shouldStop: ClusterStop, seed: RoundSeed(seedVal, 0xFA12L, round));
            MergePassMs("FairPolish", NowMs() - __t19);
            work = rFair.NewSchedule.Copy2D(); totalFair += rFair.Applied; roundApplied += rFair.Applied;
            if (rFair.PinBlocks != null) pinBlocksAll.Merge(rFair.PinBlocks);
            if (round == 0) logs.AddRange(rFair.Logs);

            round++;
            if (roundApplied == 0) break; // この巡で1手も採用なし＝joint局所最適に到達
        }

        // [研磨可否の検証ログ] ソフトc3系3種(c3/c3m/c3mn)とc1の増減・採用数・HARD不変・巡回数を集約。
        //   採用0かつ対象>0なら「頭打ち(改善手なし=正常)」、対象0なら「対象なし」と明示。
        {
            var softAfter = UnifiedViolationChecker.Check(state, work);
            int Bd(ViolationReport r, string k) => r.Breakdown.GetValueOrDefault(k, 0);
            var adopted = totalCyc + totalC1 + totalC3 + totalC3r + totalC3mn + totalC3n + totalRange +
                totalC3run + totalC3pat + totalAnchorSwap + totalBlockSwap + totalApt + totalFair;
            // [3.278.0/監査修正] CyclicSwapの正当な対象族(c2/c41/c42/c41s/c42s/covO)も対象数に含める
            //   （旧: c42等のみ違反の盤面で採用0のとき誤って「対象なし」と表示していた）。
            var targets = Bd(preSoftRep, "c1") + Bd(preSoftRep, "c3") + Bd(preSoftRep, "c3m") + Bd(preSoftRep, "c3mn") +
                Bd(preSoftRep, "low") + Bd(preSoftRep, "high") + Bd(preSoftRep, "apt") + Bd(preSoftRep, "fair") +
                Bd(preSoftRep, "c2") + Bd(preSoftRep, "c41") + Bd(preSoftRep, "c42") +
                Bd(preSoftRep, "c41s") + Bd(preSoftRep, "c42s") + Bd(preSoftRep, "covO");
            var verdict = adopted > 0 ? $"有効(採用{adopted}手)"
                : targets == 0 ? "対象なし"
                : "頭打ち(採用0=改善手なし・正常)";
            var hardNote = softAfter.Hard == preSoftRep.Hard ? "不変" : $"変化{preSoftRep.Hard}->{softAfter.Hard}!";
            logs.Add(new MirrorLog(tag: "SoftPolishVerify", message:
                // [3.271.0, 外部レビューの誤読対策] 各パスの個別ログ行は巡1のみ表示（4巡ぶんのスパム防止）
                //   だが、この集約行の増減・採用内訳は全巡合計。旧表記では「C1Polish採用0なのにc1が
                //   65→57に減った＝責務逆転?」という誤読を実際に生んだため、表示仕様を行内に明記する。
                $"ソフトc1/c3系研磨 可否={verdict} ({round}巡・各パス行は巡1のみ表示/本行は全巡合計) | c1 {Bd(preSoftRep, "c1")}->{Bd(softAfter, "c1")}" +
                $" / c3 {Bd(preSoftRep, "c3")}->{Bd(softAfter, "c3")}" +
                $" / c3m {Bd(preSoftRep, "c3m")}->{Bd(softAfter, "c3m")}" +
                $" / c3mn {Bd(preSoftRep, "c3mn")}->{Bd(softAfter, "c3mn")}" +
                $" / low {Bd(preSoftRep, "low")}->{Bd(softAfter, "low")}" +
                $" / high {Bd(preSoftRep, "high")}->{Bd(softAfter, "high")}" +
                $" / apt {Bd(preSoftRep, "apt")}->{Bd(softAfter, "apt")}" +
                $" / fair {Bd(preSoftRep, "fair")}->{Bd(softAfter, "fair")}" +
                $" | HARD {hardNote} / total {preSoftRep.Total}->{softAfter.Total}" +
                $" (採用内訳 循環:{totalCyc} c1:{totalC1} c3:{totalC3} c3回転:{totalC3r} c3mn玉突き:{totalC3mn} c3n:{totalC3n}" +
                $" range玉突き:{totalRange} c3run玉突き:{totalC3run} c3pattern玉突き:{totalC3pat} アンカー窓交換:{totalAnchorSwap} ブロック交換:{totalBlockSwap}" +
                $" apt玉突き:{totalApt} fair玉突き:{totalFair})"));
        }

        // [weekly研磨の穴を埋める] 曜日平準化(weekly)は同日2者スワップでは動かせない（勤務↔勤務は曜日別の
        //   勤務/休が不変）ため、被覆保存の2職員×2日長方形交換で「過剰曜日→過少曜日」へ勤務を移す。実目的
        //   関数isBetterで採否＝退化なし。下のequalize系(分散指標)より先にL1指向のこのパスを走らせる。
        onPhase?.Invoke("後処理 曜日平準化(長方形交換)");
        var __t20 = NowMs();
        var rWrb = ApplyWeeklyRebalancePolish(state, work, maxPasses: 2, shouldStop: ClusterStop);
        MergePassMs("WeeklyRebalancePolish", NowMs() - __t20);
        if (rWrb.PinBlocks != null) pinBlocksAll.Merge(rWrb.PinBlocks);
        work = rWrb.NewSchedule.Copy2D();
        logs.AddRange(rWrb.Logs);

        // [交互最適化(Alternating Optimization)] 長方形交換(クロス日)が届かない同日内の「休の割当先」を、
        //   日ブロックごとの最小費用割当(Hungarian＝凸最適化)でweekly/range/apt同時最適に再配置し、不動点
        //   まで巡回する。rectangle(クロス日)とAO(同日内)は相補的＝両方走らせてweeklyの取りこぼしを二方向
        //   から詰める。keep-best。
        onPhase?.Invoke("後処理 交互最適化(日ブロック割当)");
        var __t21 = NowMs();
        var rAlt = ApplyAlternatingSoftPolish(state, work, maxSweeps: 4, shouldStop: ClusterStop);
        MergePassMs("AlternatingSoftPolish", NowMs() - __t21);
        if (rAlt.PinBlocks != null) pinBlocksAll.Merge(rAlt.PinBlocks);
        work = rAlt.NewSchedule.Copy2D();
        logs.AddRange(rAlt.Logs);

        // [3.317.0] ここにあった分散指標ベースの平準化2パスは撤去した（実測で寄与ゼロ）。fair/weeklyの
        //   L1研磨はApplyFairPolish/ApplyWeeklyRebalancePolish/ApplyAlternatingSoftPolishが担う。

        // [3.255.0/C1JointLnsPolish・PersonalBalanceJointLnsPolish] ここまでの巡回研磨は各パスが候補を
        //   作った直後に正式目的関数で採否するため、C1改善や個人回数改善に伴うcoverage/range/c3系の副作用を
        //   別の手で相殺する前に候補を失うことがある。この2パスはdebt付きbeamで複数手を束ね、最終採用のみ
        //   正式順序(hard→weighted→total)のkeep-bestで判定する（中間ノードのdebtは探索のみに影響し退化不能）。
        //   実行コストが高い(既定8s/6s)ため巡回ループでなく最終1回のみ実行。
        // [予算按分] remaining=14000ms(=両者の既定合計値)ちょうどの境界で検算すると、折半案はC1に7000msしか
        //   与えず自身の既定8000msに届かず、Personalは残り7000msのうち自身の既定6000msしか使わず1000msが
        //   誰にも使われないまま終わる。既定比8:6の按分なら、この境界で双方とも過不足なく自身の既定を得られる。
        //   remainingは整数乗算オーバーフロー回避のため安全な上限(100秒)へ先にクランプしてから按分する。
        //   残0なら各パスのmaxMillis<=0ガードにより即スキップ(explicitly無効)される。
        onPhase?.Invoke("後処理 期間要件(c1)共同LNS");
        var tC1Lns = NowMs();
        var remainingForC1Lns = Math.Min(Math.Max(deadlineMs - tC1Lns, 0L), 100_000L);
        var c1LnsCap = Math.Min(remainingForC1Lns * 8_000L / 14_000L, 8_000L);
        var __t22 = NowMs();
        var rC1Lns = C1RepairOperators.JointLns(
            state, work, config: new C1JointLnsPolish.Config(MaxMillis: c1LnsCap), shouldStop: stop);
        MergePassMs("C1共同LNS", NowMs() - __t22);
        work = rC1Lns.NewSchedule.Copy2D();
        // [3.350.0/敵対検証] 最終LNS2パスのピン却下がpinBlocksAllへ合流していなかった
        //   （旧: この2パスはPinBlockAttributionを作らずpinBlocksが常にnullだった）。
        if (rC1Lns.PinBlocks != null) pinBlocksAll.Merge(rC1Lns.PinBlocks);
        logs.AddRange(rC1Lns.Logs);

        onPhase?.Invoke("後処理 個人回数/適切回数 共同LNS");
        var tPersonalLns = NowMs();
        var personalLnsCap = Math.Min(Math.Max(deadlineMs - tPersonalLns, 0L), 6_000L);
        var __t23 = NowMs();
        var rPersonalLns = PersonalBalanceJointLnsPolish.Apply(
            state, work, config: new PersonalBalanceJointLnsPolish.Config(MaxMillis: personalLnsCap), shouldStop: stop);
        MergePassMs("個人回数共同LNS", NowMs() - __t23);
        work = rPersonalLns.NewSchedule.Copy2D();
        if (rPersonalLns.PinBlocks != null) pinBlocksAll.Merge(rPersonalLns.PinBlocks);
        logs.AddRange(rPersonalLns.Logs);

        var tHf = NowMs();
        if (stop())
        {
            // [3.278.0/文言修正] この時点で残るのは最終検査(HF70)のみ＝「残りパスの打ち切り」は各パス内部の
            //   shouldStopで既に済んでいる事実に合わせる。
            logs.Add(new MirrorLog(level: "W", tag: "POST",
                message: "予算超過のため後処理は締切で短縮されました(各パスは内部で打ち切り済み・以降は最終検査のみ)"));
        }

        onPhase?.Invoke("後処理 HF70 異常検知");
        var report = UnifiedViolationChecker.Check(state, work);
        var r70 = DetectHF70Anomalies(state, work, algoName, report);
        logs.AddRange(r70.Logs);

        var tEnd = NowMs();
        // [ログ精度修正] 旧表記はt66〜tHfの間(=HF66本体＋厳密日割当＋巡回研磨4巡＋曜日/交互研磨＋C1/個人
        //   共同LNS＝パイプライン成長で大半を占めるようになった区間)を丸ごと「HF66」と誤表示していた
        //   （HF66自身はt66+hf66Capで内部上限≤6sに自己制限済みのため、実際にそれ以上かかっていたのは後続の
        //   巡回研磨クラスタ）。C1JointLNS/個人共同LNSが「探索上限0=明示的に無効」になる理由（＝ここまでの
        //   区間で後処理予算を使い切った）が読めるよう区間ごとに分割表示する。表示のみ・スコアリング不変。
        logs.Add(new MirrorLog(level: "I", tag: "POST",
            message: $"後処理タイミング 総{tEnd - t0}ms: HF80={t67 - t80}ms HF67={t66 - t67}ms HF66={t66Done - t66}ms" +
                $" 巡回研磨(厳密日割当+c1/c3/range/apt/fair+曜日/交互)={tC1Lns - t66Done}ms" +
                $" C1共同LNS={tPersonalLns - tC1Lns}ms 個人共同LNS={tHf - tPersonalLns}ms" +
                // [3.278.0] 旧: 最終検査(フルcheck+HF70)が無区間で「区間合計 < 総」の不一致を生んでいた。
                $" 最終検査+HF70={tEnd - tHf}ms"));

        // [3.339.0] パスごとの内訳（多い順・上位8）。「時間を食っているのに採用0」のパスは各パス自身の行
        //   （採用N回）と突き合わせれば分かる。合計は上の区間合計とほぼ一致する（計測外＝ループ制御のみ）。
        if (passMs.Count > 0)
        {
            var sum = Math.Max(passMs.Values.Sum(), 1L);
            logs.Add(new MirrorLog(level: "I", tag: "POST",
                message: $"後処理パス別 計{sum}ms: " + string.Join(" ", passMs
                    .OrderByDescending(kv => kv.Value)
                    .Take(8)
                    .Select(kv => $"{kv.Key}={kv.Value}ms({kv.Value * 100 / sum}%)"))));
        }

        // [構造化診断, 3.322.0] C1研磨の時点で作った診断を最終盤面に合わせ直す
        //   （そのあとの共同LNS等が直した箇所を「直せなかった」と見せない）。
        C1PlateauDiagnosis? plateau = null;
        if (c1Plateau != null)
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
            plateau = c1Plateau.RefreshedAgainst(report.Breakdown.GetValueOrDefault("c1", 0), StillDeficient);
        }
        // [3.325.0] c1が残っているなら、観測が1件も無くても診断を返す（UIが「原因未確定」と出す）。
        //   旧: hasEntriesでnullにしていたため、観測ゼロのときカードごと消えて「残っているのに何も説明
        //   されない」状態になっていた。
        var c1Left = report.Breakdown.GetValueOrDefault("c1", 0);
        var plateauOut = plateau != null && (plateau.HasEntries || plateau.CauseUnknown)
            ? plateau
            : c1Left > 0 ? new C1PlateauDiagnosis(c1Left, Array.Empty<C1PlateauEntry>())
            : null;

        var allLogs = new List<MirrorLog>();
        allLogs.AddRange(logs);
        allLogs.AddRange(report.Logs);
        return new V6PostOptimizationResult(
            work, report with { Logs = allLogs }, r80, r67, r66, r70, logs,
            plateauOut, pinBlocksAll.Attempts, pinBlocksAll);
    }
}
