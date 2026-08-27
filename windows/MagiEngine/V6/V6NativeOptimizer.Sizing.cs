namespace MagiEngine.V6;

/// <summary>
/// Faithful port of the pure sizing/role/diagnostic helper functions declared inside Kotlin's
/// <c>V6NativeOptimizer</c> object — everything a phase-5c driver function (<c>RunMultiWorker</c>,
/// <c>RunV5</c>, <c>RunAlnsChains</c>/<c>RunAlnsSingle</c>, <c>RunRsi</c>/<c>RunRsiPlus</c>) needs to
/// size its parallelism, pick a role profile for hypothesis <c>i</c>, or format a diagnostic log line.
/// No mutable state; every member here is a pure function (or a thin delegate to one).
///
/// [5c範囲の縮小] <c>PortfolioWorkerCount</c>（phase 5d の <c>runAdaptivePortfolio</c> の唯一の
/// 呼出元）と <c>HypothesisStartFor</c>/<c>ForceDiverseKick</c>（<c>optimizeInSlot</c> の呼出site
/// lambda からのみ呼ばれる、phase 5d/5e scope）は、この partial class の他ファイルへ後ほど追加する。
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>
    /// [仮説数上限撤廃・ユーザー指示] かつて仕様§2.2の仮説数固定上限(5)だった定数。
    /// <see cref="HypothesisCount"/> はこの値を上限として使わなくなった＝ワーカー設定まで仮説を
    /// 増やす（下限2）。現在は ①ExtraRefine(微小予算5〜25sの追加精製)専用の意図的な小さいキャップ
    /// （phase 5e scope）②<see cref="HypothesisChainPlan"/> のデフォルト引数、の2用途にのみ残置
    /// （名前は歴史的経緯・値の意味は「旧上限」から「小予算時の安全キャップ」へ転用）。
    /// </summary>
    internal const int MAX_HYPOTHESES = 5;

    /// <summary>
    /// [HF290 役割分担移植] 並列仮説の探索/精製プロファイル（温度・摂動の倍率）。
    /// W0=1.0(ベースライン=退化防止)、以降は探索(&gt;1)/精製(&lt;1)を交互に割当てて portfolio を多様化。
    /// i&gt;=5 は黄金比の低食い違い列(golden-ratio low-discrepancy sequence)で [0.35, 2.4] へ決定的
    /// かつ非周期的に写像する（配列を単に延長・循環させるとクローン問題を繰り返すため）。
    /// </summary>
    private static readonly double[] RoleExplore = { 1.0, 2.0, 0.5, 1.6, 0.6 };

    internal static double RoleExploreFor(int i)
    {
        if (i >= 0 && i < RoleExplore.Length) return RoleExplore[i];
        var frac = (i * 0.6180339887498949) % 1.0;
        return 0.35 + frac * (2.4 - 0.35);
    }

    /// <summary>
    /// [論文活用] 並列仮説で受理戦略を多様化（W0,W1=SA基準 / W2,W4=Great Deluge / W3=Lam適応冷却）。
    /// W0 は常に SA でベースライン保持＝退化防止。i&gt;=5 は GD/LAM/SA を i%3 で巡回させ多様化する。
    /// </summary>
    internal static AcceptMode RoleAcceptFor(int i) => i switch
    {
        2 or 4 => AcceptMode.GreatDeluge,
        3 => AcceptMode.LamAdaptive,
        0 or 1 => AcceptMode.Sa,
        _ => (i % 3) switch
        {
            0 => AcceptMode.GreatDeluge,
            1 => AcceptMode.LamAdaptive,
            _ => AcceptMode.Sa,
        },
    };

    /// <summary>
    /// [論文活用] 並列仮説で演算子選択を多様化（W1=Thompson sampling / 他=roulette）。
    /// W0 は常に roulette でベースライン保持＝退化防止。i&gt;=5 は偶奇でTHOMPSON/ROULETTEを交互に割当てる。
    /// </summary>
    internal static OpSelectMode RoleOpSelectFor(int i)
    {
        if (i == 1) return OpSelectMode.Thompson;
        if (i is >= 0 and <= 4) return OpSelectMode.Roulette;
        return i % 2 == 1 ? OpSelectMode.Thompson : OpSelectMode.Roulette;
    }

    /// <summary>
    /// [3.266.0/hypothesis basin diversity] 変更セル数。診断ログとdiversity判定の両方に使う。実体は
    /// <see cref="AdaptiveEliteArchive.ScheduleDistance"/>（唯一の実装）への委譲。
    /// </summary>
    internal static int ScheduleDistance(int[][] a, int[][] b) => AdaptiveEliteArchive.ScheduleDistance(a, b);

    /// <summary>
    /// 時間予定型 Great Deluge の水位（Burke, Bykov, Newall &amp; Petrovic 2004）。
    /// frac=1(序盤)で initial、frac=0(終盤)で best へ線形降下。候補スコア ≤ 水位 なら受理。
    /// </summary>
    internal static double GreatDelugeLevel(double initial, double best, double frac) =>
        best + (initial - best) * Math.Clamp(frac, 0.0, 1.0);

    /// <summary>
    /// [3.213.0/レビュー#5] HF63 の focus 投入量ベース停滞判定で使う 1 ラウンドあたりの概算反復数。
    /// rounds が小さい(既定5等)と3回目のfocusが最終ラウンドに達し、deprioritize が成立しても振り向け
    /// 先の残りラウンドが無かった。rounds に応じて動的に決め、「残り最低reserveRounds分を振り向けに
    /// 残せる」タイミングでdeprioritizeが完了するようにする（E9の1-in-2交互を想定し
    /// attemptsTarget=ceil((rounds-reserveRounds)/2)、下限2で一度の不運な1ラウンドだけでは
    /// deprioritizeしない）。
    /// </summary>
    internal static int RsiHf63EffortIters(int rounds, int reserveRounds = 2)
    {
        var attemptsTarget = Math.Max(2, (Math.Max(0, rounds - reserveRounds) + 1) / 2);
        return (Hf63Infeasibility.INFEAS_STALL_ITERS + attemptsTarget - 1) / attemptsTarget;
    }

    /// <summary>
    /// [仮説数上限撤廃・ユーザー指示「仮説数は最低2最大設定値」] 仮説数(w)の実効値。旧
    /// <c>optimize()</c> は <c>options.workers.coerceIn(1, MAX_HYPOTHESES)</c> で workers&gt;5 分を
    /// 仮説内並列度へ配分していたが、固定上限を撤廃し**多様性(仮説数)を優先**する。下限2（workers=1でも
    /// 最低2仮説の多様探索を保証・diversity目的で意図的にworkersを1オーバーサブスクライブする）・
    /// 上限は無し（<c>options.EffectiveWorkers</c> 自体が上限）。
    /// </summary>
    internal static int HypothesisCount(int workers) => Math.Max(2, workers);

    /// <summary>
    /// [余剰ワーカー活用] 仮説数(hypotheses)に対し、設定workersのうち何本を各仮説の内部並列度
    /// （SAチェーン数・ALNS多チェーン）へ均等配分するか。workers&lt;=hypothesesなら1(旧来どおり単一
    /// チェーン)。余りは切り捨て。均等床の計算のみ＝実際の配分は <see cref="HypothesisChainPlan"/>
    /// （余り配分＋コア数クランプ）を使う。[仮説数上限撤廃後] 本体は
    /// <c>hypotheses==HypothesisCount(workers)==workers</c>（workers&gt;=2）のため実運用では常に1
    /// （内部並列は事実上不使用）。本関数は ExtraRefine 等 hypotheses&lt;workers な呼出のために残置。
    /// </summary>
    internal static int PerHypothesisWorkers(int workers, int hypotheses) =>
        Math.Max(1, workers / Math.Max(1, hypotheses));

    /// <summary>
    /// [敵対的レビュー修正・#6] V5(高速計算)は仮説の概念を使わず options.workers をそのまま
    /// SaParams.workers(=SAチェーン数)へ渡すため、<see cref="HypothesisChainPlan"/> のコア数クランプ
    /// の恩恵を受けず、コア数を超えるCPU-boundコルーチンを壁時計締切下で希釈しうる（例: 8コア機に
    /// workers=16設定でV5選択→16並列SAチェーンが8コアを奪い合う）。V5専用に総並列度をコア数以内へ
    /// クランプする（<see cref="HypothesisChainPlan"/> と異なりV5は「最低1仮説」のような競合する下限が
    /// 無いため、単純にコア数でクランプするだけで良い）。
    /// </summary>
    internal static int ClampWorkersToCores(int workers, int? cores = null)
    {
        var c = cores ?? Environment.ProcessorCount;
        return Math.Min(Math.Max(1, workers), Math.Max(1, c));
    }

    /// <summary>
    /// [敵対的レビュー3.212.0/単一ソース] 仮説ごとのチェーン本数プラン。余りを先頭仮説から+1ずつ配分し
    /// （「5を超えた分は実際に使われる」ようにする）、配分総量を min(workers, cores) にクランプする
    /// （コア数を超える CPU-bound コルーチンで壁時計締切下の希釈が起きないようにする）。
    /// UI注記・診断ログ・エンジン本体が全て本関数から導出＝表示と実挙動の乖離を構造的に防ぐ。
    /// 返り値: 長さ hypotheses の各仮説チェーン本数（各要素&gt;=1・合計=max(hypotheses, min(workers, cores))）。
    /// </summary>
    internal static int[] HypothesisChainPlan(int workers, int hypotheses = MAX_HYPOTHESES, int? cores = null)
    {
        var c = cores ?? Environment.ProcessorCount;
        var h = Math.Max(1, hypotheses);
        var distributable = Math.Max(h, Math.Min(Math.Max(1, workers), Math.Max(1, c)));
        var basePer = distributable / h;
        var remainder = distributable % h;
        return Enumerable.Range(0, h).Select(i => basePer + (i < remainder ? 1 : 0)).ToArray();
    }

    /// <summary>
    /// [3.371.0/並列SA本格再有効化] <c>RunMultiWorker</c> が実際に spawn する仮説コルーチン数と、
    /// 各仮説の内部チェーン本数プランを、診断ログ側とも共有する単一ソース。
    ///
    /// 背景: <c>w=HypothesisCount(workers)</c> は workers&gt;=2 のとき常に <c>w==workers</c> になる。
    /// これを <see cref="HypothesisChainPlan"/> の hypotheses へそのまま渡すと
    /// <c>distributable=max(w, min(workers,cores))=w</c> に構造的に一致し、内部チェーン本数
    /// （並列SA/ALNS）が**コア数に関わらず恒久的に1本**に収束していた。
    ///
    /// workers&lt;=cores（大半の端末・既定の並列ワーカー設定）ではこの関数は無変更の挙動を返す
    /// （hSpawn==w のため下記 if に入らず、旧来と完全に同一の spawn 数・plan）。workers&gt;cores
    /// （端末のコア数を超える設定）のときだけ、spawn する仮説コルーチン数を実コア数まで落とし
    /// （cores&lt;w の希釈を避ける、V5用 <see cref="ClampWorkersToCores"/> と同じ発想）、その分の
    /// 予算(workers)を各仮説の内部チェーン数へ回す（<see cref="HypothesisChainPlan"/> の cores 引数へ
    /// workers を渡し、既定のコア数クランプを迂回して「予算workers・仮説hSpawn本」を素直に配る）。
    /// workers 予算の合計は不変（コア数を超えてコルーチンを増やさない）。
    ///
    /// [3.372.0/レビュー修正] hSpawn は必ず w を超えない（<c>hSpawn == plan.Length</c> が構造的に
    /// 保たれる＝呼出側の index アクセスが AIOOBE を起こさない）。
    /// 返り値: (spawn する仮説コルーチン数, 各仮説のチェーン本数プラン=長さそのhSpawn)。
    /// </summary>
    internal static (int HSpawn, int[] Plan) HypothesisSpawnPlan(int workers, int w, int? cores = null)
    {
        var c = cores ?? Environment.ProcessorCount;
        var hSpawn = Math.Max(1, Math.Min(w, Math.Max(2, c)));
        var plan = hSpawn < w
            ? HypothesisChainPlan(workers, hSpawn, cores: workers)
            : HypothesisChainPlan(workers, hSpawn, cores: c);
        return (hSpawn, plan);
    }

    /// <summary>
    /// [3.409.4] PORTFOLIO の**外側ワーカー**が壁時計上でどれだけ並行していたかの観測値（役割別
    /// worker秒の合計 ÷ ポートフォリオ本体の経過）。CPU 使用率でも、仮説内チェーンを含む使用コア数
    /// でもない。目的は「8仮説の設定なのに実質1本」で走る片肺化を、入力・端末を跨いだログで一目で
    /// 見ること。実機ログで実際に判別できることを確認済み: 健全な値=約8（3.402.0の7.96）に対し、
    /// 3.370.0 の 0.93 は「HARD=0 到達時に残りを即キャンセルする」という 3.376.0 で撤廃済みの機構
    /// が原因だった。そのバグは既に直っているので、前向きの用途は回帰検出である。
    /// </summary>
    internal static double ObservedOuterParallelism(long totalWorkerMs, long wallElapsedMs) =>
        totalWorkerMs <= 0L || wallElapsedMs <= 0L ? 0.0 : (double)totalWorkerMs / wallElapsedMs;

    /// <summary>
    /// [3.409.16] ワーカーの離脱理由が「締切前の早期離脱」か。「締切」=自分の while ループの
    /// deadline 到達／「探索締切」=同じ締切（またはキャンセル）が stopIsFinal() の stop シグナル経由
    /// で届いた正常終了＝どちらも早期離脱ではない。旧判定（!= "締切" のみ）は、全ワーカーが予算を
    /// 使い切った正常な実行を「ワーカー離脱=8/8本が締切前(探索締切8本@275s)」と自己矛盾で報告して
    /// いた（実機ログで発覚）。早期離脱として数えるのは「停滞シグナル」（confirmStop の確認窓を通った
    /// 本物の停滞）と「例外」。
    /// </summary>
    internal static bool IsEarlyWorkerExit(string exitReason) =>
        exitReason != "締切" && exitReason != "探索締切";

    /// <summary>
    /// [3.409.17/実機ログ] エポック超過（ロールが roleDeadline を5秒超えて走った記録）の集約行。
    /// 空なら null（＝通常の実行ではログを増やさない）。実機で予算300sの実行が474〜959sまで超過した
    /// のに、どの役割が塞いだかを後から特定できなかった穴を埋める。
    /// </summary>
    internal static MirrorLog? EpochOverrunLog(IReadOnlyList<string> notes)
    {
        if (notes.Count == 0) return null;
        return new MirrorLog(
            tag: "エポック超過",
            level: "W",
            message: "ロールが停止確認(stopRole)を大きく超過: " + string.Join(",", notes.Take(8)) +
                (notes.Count > 8 ? $" ほか{notes.Count - 8}件" : "") +
                "（量子q秒のロールが実N秒走った＝内部で締切を見ない経路がある。役割名から特定する）");
    }

    /// <summary>
    /// [3.409.21] destroy-repair 系の反復回数（<c>DestroyRepairStaffAt</c> の呼出回数）。covU focus
    /// 基準（6*S）に対する均等按分。
    /// </summary>
    internal static int DestroyRepairStaffReps(int s, int t) => Math.Max(1, (6 * s + t - 1) / Math.Max(1, t));

    /// <summary>Thin delegate matching Kotlin's private <c>fun better(a, b) = betterReport(a, b)</c>.</summary>
    private static bool Better(ViolationReport a, ViolationReport b) => UnifiedViolationChecker.BetterReport(a, b);

    /// <summary>
    /// AUTO は budgetSec で <see cref="HypothesisDiversityPolicy.AutoAlgorithmForBudget"/> へ委譲。
    /// それ以外は明示要求をそのまま返す。
    /// </summary>
    public static V6Algorithm ChooseAlgorithm(V6Algorithm requested, int budgetSec) =>
        requested != V6Algorithm.Auto ? requested : HypothesisDiversityPolicy.AutoAlgorithmForBudget(budgetSec);

    /// <summary>Roulette-wheel operator selection for the adaptive LNS.</summary>
    private static int RouletteSelect(double[] weights, JavaRandom rng)
    {
        var sum = weights.Sum();
        if (sum <= 0.0) return rng.NextInt(weights.Length);
        var r = rng.NextDouble() * sum;
        for (var i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (r <= 0.0) return i;
        }
        return weights.Length - 1;
    }

    /// <summary>
    /// [Thompson sampling] 演算子選択。平滑報酬 opW を事後平均、探索ノイズを反復で減衰させたガウス
    /// 事後から各演算子の標本を引き、最大の演算子を選ぶ。重み比例(roulette)より停滞しにくく、
    /// 不確実性下での選択が原理的。ノイズσは序盤大きく(探索)→終盤小さく(活用)アニールする。
    /// </summary>
    private static int ThompsonSelect(double[] opW, long iter, JavaRandom rng)
    {
        var sigma = 0.5 / Math.Sqrt(1.0 + iter / 500.0);
        var bestOp = 0;
        var bestSample = double.NegativeInfinity;
        for (var k = 0; k < opW.Length; k++)
        {
            var u1 = Math.Clamp(rng.NextDouble(), 1e-9, 1.0);
            var u2 = rng.NextDouble();
            var g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2); // Box-Muller 標準正規
            var s = opW[k] + g * sigma;
            if (s > bestSample) { bestSample = s; bestOp = k; }
        }
        return bestOp;
    }
}
