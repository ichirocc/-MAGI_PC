using System.Diagnostics;

namespace MagiEngine.V6;

/// <summary>
/// SA tunables. Defaults mirror the Web baseline SA-ver1 (t0=10, tf=0.1, alpha=0.975, chain=20).
///
/// <see cref="SoftPolish"/> (default OFF) enables a faithful port of the Web PhaseB late-acceptance
/// SOFT-polish (LAHC, history length = <see cref="LahcLen"/>). A worker switches into PhaseB only
/// after its HARD best has not improved for <see cref="HardStallMs"/> (i.e. the HARD floor is
/// reached), and PhaseB is HARD-guarded — it never accepts a move that raises the achieved HARD
/// level, so it can only reduce SOFT. Left off by default because on short, HARD-time-bound budgets
/// uninterrupted PhaseA SA is at least as good (per the Kotlin original's own README: high
/// run-to-run variance otherwise).
///
/// [C#移植上の判断] この移植では <c>magi_native.cpp</c>（JNI経由のC++高速化ミラー）は対象外
/// （移植計画書の明示的なスコープ外決定）。Kotlin原本の <c>runWorkerNative</c>/<c>runLahcNative</c>/
/// <c>strongPerturbFlat</c>（「まずネイティブを試し、番兵不発ならKotlinへ退化する」経路）はこの理由で
/// 一切移植していない。<see cref="SaOptimizer"/> は Kotlin 原本の純Kotlinフォールバック経路
/// （<c>runWorker</c>）のみを唯一の実装として C# へ移植したもの — フォールバック元が存在しないので
/// 「ネイティブを試す」分岐自体が無い、その意味で忠実な移植である。
///
/// [C#移植上の判断・record検証] Kotlin の <c>init { require(...) }</c> ブロック（<c>LahcLen</c>/
/// <c>Chain</c> の下限検証）は、この record の主コンストラクタでは表現できない（C#の positional
/// record は「同一シグネチャの明示コンストラクタで検証を追加する」ことができない — 主コンストラクタと
/// 同じ引数型リストを持つ別コンストラクタの宣言はコンパイルエラーになる、実験で確認済み）。この2つの
/// 検証は代わりに <see cref="SaOptimizer.Run"/> の先頭で行う（消費される直前という意味では実質的に
/// 同じタイミング — このクラス全体を通じて <see cref="SaParams"/> の唯一の消費者は <c>Run</c> のため）。
/// </summary>
public sealed record SaParams(
    double T0 = 10.0,
    double Tf = 0.1,
    double Alpha = 0.975,
    int Chain = 20,
    int? Workers = null,
    long BudgetMs = 8_000,
    bool SoftPolish = false,
    long HardStallMs = 2_500,
    /// <summary>LAHC(PhaseB) の履歴長。**1 以上**（<c>bIt % lahcLen</c> でゼロ除算になるため。
    /// [3.410.0/E-15, Kotlin原本] 検証は <see cref="SaOptimizer.Run"/> 側で行う）。</summary>
    int LahcLen = 200,
    /// <summary>外部からの協調停止（停滞早期脱出・ユーザー停止）。既定 null は「常に false」を意味する
    /// （<see cref="EffectiveShouldStop"/> 参照）。</summary>
    Func<bool>? ShouldStop = null,
    /// <summary>MagiConductor（UCB1で停滞脱出戦略を自律選択）を有効化。既定ON。停滞前は既定の reset-to-best 再加熱。</summary>
    bool Conductor = true,
    /// <summary>Conductor の停滞しきい値（最良未更新の反復数）。これを超えると再加熱境界で脱出戦略を選ぶ。</summary>
    int ConductorStag = 3000,
    /// <summary>[多様化] 乱数シード。0=従来通り時刻ベース。多仮説では各仮説に異なる seed を渡して探索を多様化・再現可能にする（各ワーカーは内部で seed xor (w*定数) に分散）。</summary>
    long Seed = 0L)
{
    /// <summary>
    /// [computed default, Kotlin: <c>Runtime.getRuntime().availableProcessors().coerceIn(1, 8)</c>]
    /// C#の記録型では非定数（<see cref="Environment.ProcessorCount"/> はコンパイル時定数ではない）を
    /// 主コンストラクタの既定引数値にできないため、<see cref="Workers"/> は nullable のまま受け取り、
    /// このプロパティが「未指定なら実コア数を[1,8]へクランプ」を計算する（同名プロパティで正引数を
    /// 上書きする形は CS8866 型不一致で不可 — <c>int?</c> パラメータには <c>int?</c> 型のプロパティしか
    /// 対応できないと確認済みのため、意図的に別名にしてある）。
    /// </summary>
    public int EffectiveWorkers => Workers ?? Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>[computed default, Kotlin: <c>{ false }</c>] 未指定なら常に false を返す関数。</summary>
    public Func<bool> EffectiveShouldStop => ShouldStop ?? (() => false);
}

public sealed record SaProgress(long BestScore, long TotalIters, long ElapsedMs);

public sealed record SaResult(
    int[][] Schedule,
    long Score,
    long TotalIters,
    long ElapsedMs,
    /// <summary>
    /// [3.356.0, Kotlin原本コメント] 各SAチェーンが「全体の最良」を更新した回数。設定タブの
    /// **並列ワーカー**は V5(高速)経路ではそのままチェーン数になるが、旧ログはチェーン数も内訳も
    /// 出さず、増やした意味があったかを判断できなかった。1本しか勝っていなければ残りは無駄と読める。
    /// </summary>
    int[] ChainWins);

/// <summary>
/// Parallel SA with incremental (delta) evaluation, a multi-operator neighbourhood, and an
/// optional HARD-guarded PhaseB SOFT-polish. Each worker task owns a <see cref="DeltaEvaluator"/>,
/// runs independently with its own RNG, and the global best is kept under a lock. The final best is
/// reconciled once with the full <see cref="Evaluator"/> as a safety net.
///
/// [フェーズ5a: C#の非同期/キャンセルの型の確立] Kotlin の <c>coroutineScope { async(Dispatchers.Default)
/// { ... }; awaitAll() }</c> は <c>Task.Run(...)</c> ワーカー群 + <c>Task.WhenAll(...)</c> へ対応する
/// （<c>Dispatchers.Default</c>=CPUバウンドのスレッドプール dispatcher は <c>Task.Run</c> の既定挙動と
/// 同義）。Kotlin の <c>coroutineContext.ensureActive()</c>（構造化並行性からの協調キャンセル検出、
/// 検出すると <c>CancellationException</c> を投げる）と <c>params.shouldStop()</c>（明示的な外部停止
/// コールバック、非throw）という2つの独立した仕組みは、この移植では **1つの非throwチェックへ統合**した
/// （<c>TimeUp()</c> が <see cref="CancellationToken.IsCancellationRequested"/> も
/// <see cref="SaParams.EffectiveShouldStop"/> も同じ非throwな真偽判定として扱う）。Kotlin原本でも
/// <c>ensureActive()</c> の実際の呼出粒度は冷却ラダーの1段（既定 chain=20 反復）や flush 境界
/// （既定8000反復）単位でしかなく、実質的に <c>timeUp()</c> と同程度の粗さでしか働いていなかったため、
/// この統合は挙動の忠実性を損なわない。かつ、この統合により**このメソッド内では例外によるキャンセルが
/// 一切発生しない**（ワーカーは必ず自分のループを正常終了し、その時点の最良解を flush してから
/// return する）＝計画書がこのフェーズの検証基準として明示する「③実行中キャンセルでクリーンに最良解を
/// 返すこと（TPL変換の最も壊れやすい箇所）」を、例外処理でなく単純な非throwポーリングで構造的に満たす。
/// </summary>
public sealed class SaOptimizer
{
    private readonly Problem _problem;
    private readonly Evaluator _evaluator;
    private const long M = Evaluator.SCORE_HARD_UNIT;

    public SaOptimizer(Problem problem, Evaluator evaluator)
    {
        _problem = problem;
        _evaluator = evaluator;
    }

    public async Task<SaResult> Run(
        SaParams? saParams = null,
        Action<SaProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var p = saParams ?? new SaParams();
        // [3.410.0/E-15, Kotlin原本 SaParams.init] 直接呼出からの不正値は構築時でなく消費直前に落とす
        // （SaParams のクラス doc comment 参照 — record の主コンストラクタでは表現できなかったため）。
        if (p.LahcLen < 1) throw new ArgumentException($"lahcLen must be >= 1 (got {p.LahcLen})");
        if (p.Chain < 1) throw new ArgumentException($"chain must be >= 1 (got {p.Chain})");

        var init = _problem.InitialAssignment();
        var sw = Stopwatch.StartNew();

        long globalBest = _evaluator.FullEval(init);
        int[][] globalBestSol = CopyOf(init);
        long totalIters = 0L;
        int workerCount = Math.Max(p.EffectiveWorkers, 1);
        var chainWins = new int[workerCount];
        var lockObj = new object();

        void Report() => onProgress?.Invoke(new SaProgress(globalBest, totalIters, sw.ElapsedMilliseconds));
        Report();

        var tasks = new Task[workerCount];
        for (int w = 0; w < workerCount; w++)
        {
            int wCapture = w; // capture-by-value for the loop variable, matching Kotlin's `(0 until params.workers).map { w -> ... }`
            tasks[wCapture] = Task.Run(() =>
            {
                // [多様化, Kotlin原本] params.seed!=0 なら各仮説の固有シードを使用（呼び出し側が仮説ごとに
                //   別シードを渡す）。0 のときのみ従来の時刻ベース。ワーカー内は seed xor (w*定数) で更に分散。
                long sbase = p.Seed != 0L ? p.Seed : DateTime.UtcNow.Ticks;
                long seed = sbase ^ unchecked(wCapture * -0x61c8864680b583ebL);
                void Flush(long localBest, int[][] localSol, long iters)
                {
                    lock (lockObj)
                    {
                        totalIters += iters;
                        if (localBest < globalBest)
                        {
                            globalBest = localBest; globalBestSol = localSol; chainWins[wCapture]++;
                        }
                        Report();
                    }
                }
                RunWorker(init, p, new JavaRandom(seed), sw, Flush, cancellationToken);
            });
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        long finalScore = _evaluator.FullEval(globalBestSol);
        lock (lockObj) { globalBest = finalScore; Report(); }
        return new SaResult(globalBestSol, finalScore, totalIters, sw.ElapsedMilliseconds, chainWins);
    }

    /// <summary>
    /// The pure-managed SA worker loop (1:1 port of Kotlin's <c>runWorker</c> — the only worker
    /// path in this build, per this class's doc comment). Runs synchronously; the caller schedules
    /// it via <see cref="Task.Run(Action)"/>.
    /// </summary>
    private void RunWorker(
        int[][] init,
        SaParams saParams,
        JavaRandom rng,
        Stopwatch sw,
        Action<long, int[][], long> flush,
        CancellationToken cancellationToken)
    {
        int S = _problem.S, T = _problem.T;
        var de = new DeltaEvaluator(_problem);
        de.Reset(init);
        long curVal = de.Score();
        long best = curVal;
        int[][] bestSol = de.Snapshot();
        long bestHard = best / M;
        long lastHardImprove = sw.ElapsedMilliseconds;

        int cap = T + S + 16;
        var bi = new int[cap]; var bj = new int[cap]; var bOld = new int[cap];
        int bn = 0;
        void ApplyCell(int i, int j, int nw)
        {
            if (bn >= cap) return;
            bi[bn] = i; bj[bn] = j; bOld[bn] = de.At(i, j); bn++;
            de.Apply(i, j, nw);
        }
        void Revert() { int k = bn - 1; while (k >= 0) { de.Apply(bi[k], bj[k], bOld[k]); k--; } bn = 0; }
        int RandShiftFor(int i)
        {
            var b = _problem.Bucket[_problem.Sgrp[i]];
            return b.Length == 0 ? de.At(i, 0) : b[rng.NextInt(b.Length)];
        }
        // [3.334.0, Kotlin原本コメント] 近傍は**実現可能な希望が入ったセルを触らない**。後処理研磨の
        //   全パスと C++ の修復オペレータは元から wishLocked を見ているのに、探索の近傍だけが見て
        //   いなかった（非対称）。採点は元から正しい（pref は hard＝希望を破ると差分が 1e9 単位で増え
        //   Metropolis はほぼ必ず却下）ので誤った勤務表は出ないが、手の35〜36%がその却下される手に
        //   費やされていた（実測）。入口の hf67HardRepair が実現可能な希望を先に盤面へ入れるので、
        //   触らなければ正しいまま残る。
        bool Locked(int i, int j) => _problem.WishLocked(i, j);
        void OpSingle()
        {
            int i = rng.NextInt(S);
            int j = rng.NextInt(T);
            int tries = 0;
            while (Locked(i, j) && tries < 4) { j = rng.NextInt(T); tries++; }
            if (Locked(i, j)) return;
            var b = _problem.Bucket[_problem.Sgrp[i]];
            if (b.Length == 0) return;
            ApplyCell(i, j, b[rng.NextInt(b.Length)]);
        }
        void OpSwapDays()
        {
            int i = rng.NextInt(S);
            if (T < 2) return;
            int j1 = rng.NextInt(T);
            int j2 = rng.NextInt(T);
            if (j1 == j2) j2 = (j2 + 1) % T;
            if (Locked(i, j1) || Locked(i, j2)) return;
            int o1 = de.At(i, j1), o2 = de.At(i, j2);
            if (o1 == o2) return;
            ApplyCell(i, j1, o2); ApplyCell(i, j2, o1);
        }
        void OpBlockFill()
        {
            var cs = _problem.Cons1;
            if (cs.Count == 0) { OpSingle(); return; }
            var c = cs[rng.NextInt(cs.Count)];
            var pool = _problem.StaffForShift[c.ShiftIdx];
            if (pool.Length == 0) { OpSingle(); return; }
            int i = pool[rng.NextInt(pool.Length)];
            int maxStart = T - c.Day1;
            if (maxStart < 0) { OpSingle(); return; }
            int js = rng.NextInt(maxStart + 1);
            // [3.341.0, Kotlin原本コメント] 固定セルを飛ばして「部分的に埋まった窓」を作らない。窓を
            //   埋めるのがこの手の意図で、途中が抜けた窓はその意図を果たさないまま多数のセルを壊すだけ。
            int q = 0;
            while (q < c.Day1) { if (Locked(i, js + q)) return; q++; }
            int l = 0;
            while (l < c.Day1) { ApplyCell(i, js + l, c.ShiftIdx); l++; }
        }
        void OpLns()
        {
            // [3.341.0, Kotlin原本コメント] 破壊する集合を先に決め、固定セルが混ざっていたら手ごと
            //   見送る（部分適用しない）。
            switch (rng.NextInt(3))
            {
                case 0:
                {
                    int i = rng.NextInt(S);
                    int cnt = 2 + rng.NextInt(Math.Min(7, T));
                    var js = new int[cnt];
                    for (int idx = 0; idx < cnt; idx++) js[idx] = rng.NextInt(T);
                    if (js.Any(j => Locked(i, j))) return;
                    foreach (var j in js) ApplyCell(i, j, RandShiftFor(i));
                    break;
                }
                case 1:
                {
                    int j = rng.NextInt(T);
                    for (int i = 0; i < S; i++) if (Locked(i, j)) return;
                    for (int i = 0; i < S; i++) ApplyCell(i, j, RandShiftFor(i));
                    break;
                }
                default:
                {
                    int cnt = 3 + rng.NextInt(8);
                    var cells = new (int I, int J)[cnt];
                    for (int idx = 0; idx < cnt; idx++) cells[idx] = (rng.NextInt(S), rng.NextInt(T));
                    if (cells.Any(c => Locked(c.I, c.J))) return;
                    foreach (var c in cells) ApplyCell(c.I, c.J, RandShiftFor(c.I));
                    break;
                }
            }
        }
        bool hasC1 = _problem.Cons1.Count > 0;
        void PickOperator()
        {
            int r = rng.NextInt(100);
            if (r < 60) OpSingle();
            else if (r < 80) OpSwapDays();
            else if (r < 92) { if (hasC1) OpBlockFill(); else OpSingle(); }
            else OpLns();
        }

        long itersSinceFlush = 0L;
        const int flushEvery = 8000;
        bool phaseB = false;
        long[] hist = Array.Empty<long>();
        long bIt = 0L;

        // MagiConductor: 停滞時に UCB1 で脱出戦略を自律選択。停滞前は Noop＝既定の reset-to-best 再加熱。
        var conductor = saParams.Conductor ? new MagiConductor(saParams.ConductorStag) : null;
        ConductorAction? pendingAction = null; // 前境界で適用した脱出戦略（報酬を次境界で評価）
        long bestAtAction = best;
        // strongPerturb 用: best から数手だけ単発移動して離す（コミット＝undo破棄）。
        void StrongPerturb()
        {
            int moves = 4 + rng.NextInt(8);
            bn = 0;
            for (int m = 0; m < moves; m++) OpSingle();
            bn = 0;
        }

        bool TimeUp() => saParams.EffectiveShouldStop() || cancellationToken.IsCancellationRequested
            || sw.ElapsedMilliseconds >= saParams.BudgetMs;

        while (!TimeUp())
        {
            if (!phaseB)
            {
                // ----- PhaseA: SA, reset-to-best reheat at cooling completion -----
                double t = saParams.T0;
                bool enteredPhaseBThisLadder = false;
                while (t >= saParams.Tf && !TimeUp())
                {
                    int ls = 0;
                    while (ls < saParams.Chain)
                    {
                        bn = 0;
                        PickOperator();
                        long cand = de.Score();
                        long dE = cand - curVal;
                        bool improvedBest = false;
                        if (dE <= 0 || Math.Exp(-(double)dE / t) > rng.NextDouble())
                        {
                            curVal = cand;
                            if (cand < best)
                            {
                                if (cand / M < bestHard) { bestHard = cand / M; lastHardImprove = sw.ElapsedMilliseconds; }
                                best = cand; de.SnapshotInto(bestSol); improvedBest = true;
                            }
                            bn = 0;
                        }
                        else Revert();
                        conductor?.UpdateStagnation(improvedBest);

                        itersSinceFlush++;
                        if (itersSinceFlush >= flushEvery)
                        {
                            flush(best, CopyOf(bestSol), itersSinceFlush); itersSinceFlush = 0;
                            if (TimeUp()) { flush(best, CopyOf(bestSol), 0); return; }
                        }
                        if (saParams.SoftPolish && sw.ElapsedMilliseconds - lastHardImprove > saParams.HardStallMs)
                        {
                            phaseB = true; enteredPhaseBThisLadder = true; break;
                        }
                        ls++;
                    }
                    if (enteredPhaseBThisLadder) break; // Kotlin's `break@cooling`
                    t *= saParams.Alpha;
                }
                // reheat / escape (MagiConductor) / or enter PhaseB from the best
                if (phaseB || conductor is null)
                {
                    de.Reset(bestSol); curVal = best;
                }
                else
                {
                    // 前回の脱出戦略の効果を報酬として学習（best が改善したか）。
                    if (pendingAction is { } action) conductor.UpdateReward(action, best < bestAtAction ? 1.0 : 0.0);
                    switch (conductor.SelectAction())
                    {
                        case ConductorAction.StrongPerturb:
                            de.Reset(bestSol); StrongPerturb(); curVal = de.Score(); pendingAction = ConductorAction.StrongPerturb;
                            break;
                        case ConductorAction.ScaleTemp:
                            // best へ戻さず現在解を保持（温度倍率なし＝次ラダーで t0 再加熱）
                            curVal = de.Score(); pendingAction = ConductorAction.ScaleTemp;
                            break;
                        case ConductorAction.Reheat:
                            de.Reset(bestSol); curVal = best; pendingAction = ConductorAction.Reheat;
                            break;
                        default: // Noop
                            de.Reset(bestSol); curVal = best; pendingAction = null; // 既定の reset-to-best
                            break;
                    }
                    bestAtAction = best;
                }
                if (phaseB) { hist = new long[saParams.LahcLen]; Array.Fill(hist, curVal); bIt = 0; }
            }
            else
            {
                // ----- PhaseB: HARD-guarded LAHC SOFT polish -----
                bn = 0;
                PickOperator();
                long cand = de.Score();
                long candHard = cand / M;
                int histIdx = (int)(bIt % saParams.LahcLen);
                long v = hist[histIdx];
                if (candHard <= bestHard && (cand <= v || cand <= curVal))
                {
                    curVal = cand;
                    if (candHard < bestHard) bestHard = candHard;
                    if (cand < best) { best = cand; de.SnapshotInto(bestSol); }
                    bn = 0;
                }
                else Revert();
                histIdx = (int)(bIt % saParams.LahcLen);
                if (curVal < hist[histIdx]) hist[histIdx] = curVal;
                bIt++;

                itersSinceFlush++;
                if (itersSinceFlush >= flushEvery)
                {
                    flush(best, CopyOf(bestSol), itersSinceFlush); itersSinceFlush = 0;
                    if (TimeUp()) { flush(best, CopyOf(bestSol), 0); return; }
                }
            }
        }
        flush(best, CopyOf(bestSol), itersSinceFlush);
    }

    private static int[][] CopyOf(int[][] a)
    {
        var result = new int[a.Length][];
        for (int i = 0; i < a.Length; i++) result[i] = (int[])a[i].Clone();
        return result;
    }
}
