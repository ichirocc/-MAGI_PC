using System.Globalization;
using System.Text;
using System.Threading;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7 ピース18/19] <c>V6FinalPort.kt</c> の <c>handleOptimize</c>（252-988行）の移植。
/// これで <c>handleOptimize</c> 自身を除く全メンバ（<see cref="V6FinalPort.AlgorithmPlan"/>／
/// <see cref="V6FinalPort.Watchdog"/>／<see cref="V6FinalPort.Tail"/>／本体ファイルの
/// <c>ActionResult</c>/<c>ImpossibleWishGate</c>/<c>BuildBusyDetail</c>/<c>ConfirmDespiteImpossibleWishes</c>/
/// <c>HandleSmartInitial</c>/<c>HandleCheck</c>）が揃っていた土台の上に、最後の・最大の入口を移植する。
///
/// <para><b>設計判断（HF77＝逐語移植・数値やパラメータを勝手に補正しない・が本項目)</b></para>
/// <list type="bullet">
/// <item><description><b>パラメータの並び替え</b>：C#は必須引数を省略可能引数より前に置く必要があるため、
/// Kotlinで既定値を持たない <c>secondsRaw</c> を2番目へ移動した（Kotlin原本は
/// <c>state, schedule=null, workers=null, secondsRaw, softPolish=false, ...</c> の順）。
/// それ以外の意味・既定値は完全に同一。</description></item>
/// <item><description><b><c>effWorkers</c>/<c>sched</c>/<c>progress</c></b>：Kotlin側は関数冒頭で
/// <c>schedule = schedule ?: state.schedule.toIntArray2D()</c>（既定値解決）のように仮引数を
/// re-bind するが、C#では仮引数の再代入でなく別名の局所変数（<c>sched</c>=解決済み盤面・
/// <c>effWorkers</c>=解決済みワーカー数・<c>progress</c>=解決済みコールバック）を導入し、
/// 以降 Kotlin本体中の素の <c>schedule</c>/<c>workers</c>/<c>onProgress</c> 参照は全てこれらへ対応する。</description></item>
/// <item><description><b>watchdog状態＝ローカル変数＋<c>Volatile</c>/<c>Interlocked</c></b>：Kotlinは
/// 呼出のたびに新規生成する <c>AtomicLong</c>/<c>AtomicInteger</c>/<c>AtomicBoolean</c> のローカル変数
/// （並列ワーカーから読み書きされるため atomic が要る）を多数持つ。C#では同じスコープの
/// 素の <c>long</c>/<c>int</c>/<c>bool</c> ローカル変数とし、ネストしたローカル関数
/// （<see cref="ProgressWatch"/> 相当のクロージャ等）から <c>Volatile.Read</c>/<c>Volatile.Write</c>
/// （単純代入・参照）または <c>Interlocked.Add</c>/<c>Interlocked.Increment</c>（複合更新の2箇所のみ＝
/// <c>observedIters</c> の加算と <c>bestVersion</c> の増分）で読み書きする。Kotlin側の非atomicな
/// プレーン <c>var</c>（<c>bTotal</c>/<c>bWeighted</c>/<c>lastPhase</c>/<c>itersByPhase</c>）は
/// 全て単一の <c>lock (progressLock)</c> ブロック内でしか触られないため <c>Volatile</c> は不要
/// （lock自体がメモリバリアを提供する）。</description></item>
/// <item><description><b><c>nativeLog</c> は意図的に省略</b>：Kotlin原本（776-810行付近）は
/// <c>NativeBridge.available</c>/<c>NativeGate.userEnabled</c>/<c>.parityCheckEnabled</c>/<c>.enabled</c>/
/// <c>.usable</c>/<c>.disable(...)</c>/<c>NativeEval.parityCheck(...)</c>/
/// <c>TuningTelemetry.parityChecks.incrementAndGet()</c> を参照する診断ブロックを持つが、これらの型は
/// このC#移植（<c>magi_native.cpp</c> のJNI/C++層は計画により対象外）には一切存在しない。
/// よってこのブロックは丸ごと省略し、<c>logs</c> の先頭は Kotlinの4要素
/// <c>[timingLog, budgetPlanLog, nativeLog, tuningLog]</c> でなく3要素
/// <c>[timingLog, budgetPlanLog, tuningLog]</c> となる。<see cref="TuningTelemetry.Summary"/> は
/// <c>nativeOn</c>/<c>parityOn</c> を常に <c>false</c> で呼ぶ。</description></item>
/// <item><description><b>「をNativeエンジンで実行中」等の表示文言はHF77により逐語のまま保持</b>：
/// 上記のとおりネイティブ層は存在しないが、これは実装の省略であって表示文言の意味変更ではない
/// （利用者向け文言をこの移植のためだけに書き換えるのはHF77＝逐語移植の規律に反する）。</description></item>
/// </list>
/// </summary>
public static partial class V6FinalPort
{
    /// <summary>
    /// 最適化本体。予算(<paramref name="secondsRaw"/>秒、上限は <see cref="MaxOptimizeSec"/>)に応じて
    /// V5/ALNS/RSIThenALNS/Portfolio のいずれかを選択し（<see cref="GetOptimizationPlan"/>）、
    /// <see cref="V6NativeOptimizer.Optimize"/> → <see cref="EliteIntegrationPolish.Apply"/> →
    /// <see cref="V6HotfixPasses.RunPostOptimization"/> の順で駆動する。
    /// </summary>
    public static async Task<ActionResult> HandleOptimize(
        MagiState state,
        int secondsRaw,
        int[][]? schedule = null,
        int? workers = null,
        bool softPolish = false,
        V6Algorithm requestedAlgorithm = V6Algorithm.Auto,
        bool allowImpossible = false,
        Action<string, ViolationReport?, long, long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        static long NowMs() => EngineClock.NowMs();
        static int TryOrZero(Func<int> f)
        {
            try { return f(); }
            catch (Exception) { return 0; }
        }
        static string SecF(long ms) => (ms / 1000.0).ToString(CultureInfo.InvariantCulture);

        var sched = schedule ?? state.Schedule.ToIntArray2D();
        var effWorkers = workers ?? Math.Clamp(Environment.ProcessorCount, 1, 8);
        var progress = onProgress ?? ((_, _, _, _) => { });

        V6NativeOptimizer.BeginTelemetry();

        if (state.DayCount <= 0)
            throw new ArgumentException("対象期間が無効です。基本情報で終了日を開始日より後にしてください");
        // [3.360.3, Kotlin原本コメント] 期間には T>0 のガードがあるのに職員数には無く、非対称だった。
        //   S=0 は編集画面からは作れない（Ws1Ops.removeStaff が最後の1名を消さない）が、
        //   JSON/CSV 取込で外部から入りうる。その場合 SaOptimizer の rng.nextInt(S) が例外を投げ、
        //   原因の読めない文言を出していた（このガードで原因と直し方が読めるメッセージへ）。
        if (state.StaffList.Count == 0)
            throw new ArgumentException("職員が1人も登録されていません。職員管理で追加してください");

        var seconds = Math.Clamp(secondsRaw, 1, MaxOptimizeSec);

        var gate = ConfirmDespiteImpossibleWishes(state, allowImpossible);
        if (!gate.Allowed)
            throw new InvalidOperationException(gate.Message);

        var startMs = NowMs();

        var baseProblem = ScheduleUtil.CachedProblem(state);
        var normInput = ScheduleUtil.NormalizeSchedule(sched, baseProblem);
        var inputReport = UnifiedViolationChecker.Check(state, normInput);

        var label = GetAlgorithmLabel(seconds);
        var plan = GetOptimizationPlan(seconds);
        // [Kotlin原本] overrides のキーは BuildBusyDetail が読む固定キー（"subtitle"/"phaseDesc"/
        //   "expectedSec"/"estimatedIter"）。"をNativeエンジンで実行中" は HF77 により逐語のまま保持
        //   （このC#移植にネイティブ層は無いが、これは実装の省略であって表示文言の変更ではない）。
        var busy = BuildBusyDetail(state, label.Name, new Dictionary<string, string>
        {
            ["subtitle"] = label.Desc,
            ["phaseDesc"] = $"{label.Tech} をNativeエンジンで実行中",
            ["expectedSec"] = $"約 {seconds} 秒",
            ["estimatedIter"] = "問題サイズに応じて自動調整",
        });

        // [review #1, Kotlin原本] 利用者が明示的にアルゴリズムを選んだ（AUTOでない）場合はその全予算を尊重する。
        // AUTOは時間予算ベースの計画に従う。[review #3] postPolish=false＝optimize() 内部では研磨しない
        // （下記の単一の後処理チェーンだけが研磨を担う。ここでの二重研磨は意図的に無い）。
        var opts = requestedAlgorithm != V6Algorithm.Auto
            ? new V6OptimizerOptions(
                Algorithm: requestedAlgorithm, TotalBudgetSec: Math.Max(seconds, 1), Workers: effWorkers,
                SoftPolish: softPolish, Restarts: 2, PostPolish: false)
            : plan switch
            {
                OptimizationPlan.V5 v5 => new V6OptimizerOptions(
                    Algorithm: V6Algorithm.V5, TotalBudgetSec: v5.Seconds, Workers: effWorkers,
                    SoftPolish: softPolish, Restarts: 1, PostPolish: false),
                OptimizationPlan.ALNS alns => new V6OptimizerOptions(
                    Algorithm: V6Algorithm.Alns, TotalBudgetSec: alns.Seconds, Workers: effWorkers,
                    SoftPolish: softPolish, Restarts: alns.Restarts, PostPolish: false),
                OptimizationPlan.RSIThenALNS rsiThenAlns => new V6OptimizerOptions(
                    Algorithm: V6Algorithm.Rsi, TotalBudgetSec: rsiThenAlns.RsiSec, Workers: effWorkers,
                    SoftPolish: softPolish, Restarts: rsiThenAlns.AlnsRestarts, PostPolish: false),
                OptimizationPlan.Portfolio portfolio => new V6OptimizerOptions(
                    Algorithm: V6Algorithm.Portfolio, TotalBudgetSec: portfolio.Seconds, Workers: effWorkers,
                    SoftPolish: softPolish, Restarts: 2, PostPolish: false),
                _ => throw new InvalidOperationException($"未知の OptimizationPlan: {plan}"),
            };
        // [HF532移植] optFlags.rectSwap 既定ON。
        var optsR = opts with { RectSwap = V6LateOperators.OptFlagBool(state, "rectSwap", true) };

        // ----- 予算一本化: optimize() + runPostOptimization() を一つの予算で管理する -----
        var budgetMs = (long)seconds * 1000L;
        var hardDeadlineMs = startMs + budgetMs;
        // [3.372.0/レビュー修正, Kotlin原本] 診断ログ表示専用。実際の dispatch は V6NativeOptimizer.Optimize()
        //   内の同名関数呼出が担うため、ここも同じ V6NativeOptimizer.HypothesisCount から導出し独立再計算による
        //   表示/実挙動の乖離を防ぐ。
        var plannedHypotheses = V6NativeOptimizer.HypothesisCount(effWorkers);
        var effHypotheses = opts.Algorithm is V6Algorithm.Alns or V6Algorithm.Rsi or V6Algorithm.RsiPlus
            ? V6NativeOptimizer.HypothesisSpawnPlan(effWorkers, plannedHypotheses).HSpawn
            : plannedHypotheses;

        // ----- 停滞早期脱出ウォッチドッグ -----
        // [3.314.0] 下限 8 秒は「UI 経路は 10 秒下限」を前提にした値。予算そのものでクランプして
        //   searchDeadline <= hardDeadline を構造的に保証する（10 秒以上では minRunMs が支配するため結果は不変）。
        var minRunMs = Math.Min(Math.Clamp(budgetMs / 6, 8_000L, 45_000L), budgetMs);
        // [3.422.0/3.424.0] 後処理予約枠。探索は searchDeadlineMs で止め、後処理は hardDeadlineMs まで走らせる。
        var postReserveMs = Math.Min(Math.Clamp(budgetMs / 12, 8_000L, 25_000L), budgetMs / 2);
        var searchDeadlineMs = Math.Max(hardDeadlineMs - postReserveMs, startMs + minRunMs);
        var searchWindowMs = searchDeadlineMs - startMs;
        // [5分強化/3.422.0 Part B・3.424.0で基準是正] 固定 9/10 を PolishGate.NormalStallFraction
        //   （既定 0.9＝旧値と同一）へ外出し。NormalStallMs（純関数）へ委譲。
        var stallMs = NormalStallMs(budgetMs, searchWindowMs);
        // [5分圧縮/3.424.0] budgetMs 基準を復元（この値は budget/8 <= budget/2 <= searchWindow で常に探索区間内）。
        var stallHardMs = Math.Max(budgetMs / 8, 15_000L);   // 5分予算→37.5s
        // [賢い早期脱出] 証明可能に解消不能な「データ起因HARD」の下限（構造的covU）。構造(assignability/need)
        //   のみ依存で最適化中に不変＝一度だけ算出する。
        var hardFloor = TryOrZero(() => V6SanityPort.StructuralHardFloor(state));

        // [レビュー#9/3.230.0] 「最良改善」と「フェーズ遷移」の時計を分離。フェーズ遷移は短い個別猶予
        //   (phaseGraceMs)としてのみ機能させ、「本当に改善が無い時間」は lastBestImproveMs 単独で計測する。
        long lastBestImproveMs = startMs;
        long lastPhaseChangeMs = startMs;
        bool stagnationFired = false;
        // [停滞時間のログ出力] 発火の瞬間に「何ms無改善だったか」を記録する。
        long stagnationDurationMs = -1L;
        // [3.375.0] 進捗ストリームで観測した反復数を「最終改善の瞬間」と「停滞発火の瞬間」に記録する
        //   （進捗報告ぶんの目安であり真の総量ではない。詳細は Kotlin 原本コメント参照）。
        var itersByPhase = new Dictionary<string, long>();
        long observedIters = 0L;
        long lastBestImproveIters = 0L;
        long stagnationIters = -1L;
        int bestHard = int.MaxValue;   // 並列ワーカーから読むため Volatile
        // [hardFloor 精度] best の「非covU HARD」(groupViol/pref/c3n=解けるHARD)件数。
        int bestNonCovUHard = int.MaxValue;
        // [3.281.0/停滞レビューA] c3n構造壁の動的床。
        bool bestNonCovUAllC3n = false;
        int bestVersion = 0;
        int c3nWallCheckedVersion = -1;
        bool c3nWallResult = false;
        int bTotal = int.MaxValue;
        double bWeighted = double.MaxValue;
        var lastPhase = "";
        var progressLock = new object();   // [競合解消] 並列ワーカーから呼ばれる best 追跡の read-modify-write を直列化

        void ProgressWatch(string phase, ViolationReport? report, long iters, long elapsed)
        {
            lock (progressLock)
            {
                // [3.375.0] フェーズごとの増分を足して総反復数にする（同期ブロック内＝競合なし）。
                var prevIt = itersByPhase.GetValueOrDefault(phase, 0L);
                Interlocked.Add(ref observedIters, iters >= prevIt ? iters - prevIt : iters);
                itersByPhase[phase] = iters;
                // 「仮説N本探索中 / 」接頭辞を除去（"/ " の直後の内側フェーズ名だけを見る）。
                var afterSlash = phase.Contains("/ ", StringComparison.Ordinal)
                    ? phase[(phase.IndexOf("/ ", StringComparison.Ordinal) + 2)..]
                    : phase;
                var basePhase = afterSlash.Trim();
                if (basePhase.Length == 0) basePhase = phase;
                if (basePhase != lastPhase) { lastPhase = basePhase; Volatile.Write(ref lastPhaseChangeMs, NowMs()); }
                if (report != null)
                {
                    var h = report.Hard; var t = report.Total; var wgt = report.WeightedScore;
                    var bh = Volatile.Read(ref bestHard);
                    // [3.287.0 keep-best統一/3.289.0] hard→weighted→total（betterReport と同順）。
                    //   許容誤差(1e-6)付きは意図的（停滞ウォッチドッグの改善検知専用・採否は betterReport が担う）。
                    var improved = h < bh || (h == bh && wgt < bWeighted - 1e-6) || (h == bh && wgt <= bWeighted + 1e-6 && t < bTotal);
                    if (improved)
                    {
                        Volatile.Write(ref bestHard, h); bTotal = t; bWeighted = wgt;
                        Volatile.Write(ref lastBestImproveMs, NowMs());
                        Volatile.Write(ref lastBestImproveIters, Volatile.Read(ref observedIters));   // [3.375.0]
                        // [3.346.0/実機ログ] 停滞ラッチを解除する。shouldStop は単調でない。
                        Volatile.Write(ref stagnationFired, false);
                        Volatile.Write(ref stagnationDurationMs, -1L);
                        Volatile.Write(ref stagnationIters, -1L);
                        // 非covU HARD(=解けるHARD)件数を best と同時に捕捉。
                        var gv = report.Breakdown.GetValueOrDefault("groupViol", 0);
                        var pf = report.Breakdown.GetValueOrDefault("pref", 0);
                        var c3n = report.Breakdown.GetValueOrDefault("c3n", 0);
                        Volatile.Write(ref bestNonCovUHard, gv + pf + c3n);
                        // [3.281.0/A] 非covU HARD が c3n のみか＋best世代を進める。
                        Volatile.Write(ref bestNonCovUAllC3n, gv == 0 && pf == 0 && c3n > 0);
                        Interlocked.Increment(ref bestVersion);
                    }
                }
            }
            progress(phase, report, iters, elapsed);   // ユーザーコールバックはロック外で呼ぶ
        }

        // [3.230.0] 現フェーズ自身にも与える短い個別猶予。
        var phaseGraceMs = Math.Clamp(budgetMs / 40, 2_000L, 15_000L);

        // [3.281.0/A] c3n構造壁の遅延証明。best 世代ごとに一度だけ ForbiddenDiag を実行しキャッシュする。
        bool C3nWallProven()
        {
            var v = Volatile.Read(ref bestVersion);
            if (Volatile.Read(ref c3nWallCheckedVersion) != v)
            {
                var board = V6NativeOptimizer.LiveBest;
                bool proven;
                if (board == null) proven = false;
                else
                {
                    try
                    {
                        var arr = new int[board.Count][];
                        for (var r = 0; r < board.Count; r++)
                        {
                            arr[r] = new int[board[r].Count];
                            for (var c = 0; c < board[r].Count; c++) arr[r][c] = board[r][c];
                        }
                        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(state, arr);
                        proven = diag.HasRuns && diag.AllBlocked;
                    }
                    catch (Exception) { proven = false; }
                }
                Volatile.Write(ref c3nWallResult, proven);
                Volatile.Write(ref c3nWallCheckedVersion, v);
            }
            return Volatile.Read(ref c3nWallResult);
        }

        bool ShouldStop()
        {
            var now = NowMs();
            // [賢い早期脱出] bestHard が構造的covU下限以下＝解けるHARDは出し切った状態。
            //   ただし非covU HARDが残る間は long stall で粘る。
            // [3.281.0/A] 追加: 残る非covU HARD が c3n のみで ForbiddenDiag が全 run の塞がりを証明した場合も
            //   plateau として stallHardMs へ移行。
            var nonCovU = Volatile.Read(ref bestNonCovUHard);
            var wall = nonCovU > 0 && Volatile.Read(ref bestNonCovUAllC3n) &&
                Volatile.Read(ref bestHard) <= hardFloor + nonCovU &&
                now - Volatile.Read(ref lastBestImproveMs) > stallHardMs && C3nWallProven();
            var effStall = EffectiveStallMs(
                Volatile.Read(ref bestHard), hardFloor, nonCovU, Volatile.Read(ref bestNonCovUAllC3n), wall, stallHardMs, stallMs);
            if (now >= searchDeadlineMs || cancellationToken.IsCancellationRequested) return true;
            if (WatchdogStagnationFired(now, startMs, minRunMs, Volatile.Read(ref lastPhaseChangeMs), phaseGraceMs,
                    Volatile.Read(ref lastBestImproveMs), effStall))
            {
                Volatile.Write(ref stagnationDurationMs, now - Volatile.Read(ref lastBestImproveMs));
                Volatile.Write(ref stagnationIters, Volatile.Read(ref observedIters));   // [3.375.0]
                Volatile.Write(ref stagnationFired, true);
                return true;
            }
            return false;
        }

        // [3.346.1/方針B, Kotlin原本] shouldStop が真のとき、それが単調な停止かを返す。停滞シグナルは
        //   他のワーカーが改善を1件出せば偽に戻る（単調でない）。適応ポートフォリオはそれだけを確認窓で
        //   再確認する（一瞬のシグナルで片肺運転にしないため）。
        bool StopIsFinal() => NowMs() >= searchDeadlineMs || cancellationToken.IsCancellationRequested;
        // 後処理(runPostOptimization)用の別締切。stall では止めず予約枠 hardDeadlineMs まで使える。
        bool PostShouldStop() => NowMs() >= hardDeadlineMs || cancellationToken.IsCancellationRequested;

        var tFirst0 = NowMs();
        var first = await V6NativeOptimizer.Optimize(state, sched, optsR, ShouldStop, ProgressWatch, StopIsFinal, cancellationToken)
            .ConfigureAwait(false);
        var tFirst1 = NowMs();
        // [review #5, Kotlin原本] RSIThenALNS は RSI(first)→ALNS(chained) を同一予算内で直列実行する。
        //   各段は postPolish=false（optsR で統一）なので段内 polish は走らない。最終 polish は段ではなく
        //   下流の RunPostOptimization() に一度だけ集約しているため、ここでの二重 polish は意図的に無い。
        var chained = requestedAlgorithm == V6Algorithm.Auto && plan is OptimizationPlan.RSIThenALNS rsiThenAlnsPlan && !ShouldStop()
            ? await V6NativeOptimizer.Optimize(
                    state, first.Schedule,
                    optsR with { Algorithm = V6Algorithm.Alns, TotalBudgetSec = rsiThenAlnsPlan.AlnsSec },
                    ShouldStop, ProgressWatch, StopIsFinal, cancellationToken)
                .ConfigureAwait(false)
            : first;
        var tChain1 = NowMs();
        // [3.377.0/実機ログ起因] 停滞ウォッチドッグの遠隔測定は探索フェーズの話。探索終了時点でスナップショット
        //   し、ウォッチドッグの数字は全てこの時刻基準で揃える（後処理・追加精製の影響を受けない）。
        var lastImpAtSearchEnd = Volatile.Read(ref lastBestImproveMs);
        var lastPhaseAtSearchEnd = Volatile.Read(ref lastPhaseChangeMs);
        var itersAtSearchEnd = Volatile.Read(ref observedIters);
        var lastImpItersAtSearchEnd = Volatile.Read(ref lastBestImproveIters);

        // [3.268.0/エリート統合] 旧「エリート再結合(Path Relinking)」を置換。8役の最終1解だけでなく、
        //   非同期適応ポートフォリオが全epochから保存した品質/距離/橋渡しエリート(FusionElites)を統合する。
        //   PORTFOLIO以外のアルゴリズムでは FusionElites が空のため、旧来の Alternatives(最大3件)へ
        //   フォールバックする（挙動は変わらず、対象が無ければ即no-op）。
        var integrationBudgetMs = Math.Clamp(budgetMs / 20, 6_000L, 16_000L);
        // [監査修正を継承] integrationも hardDeadlineMs-postReserveMs/2 で止め、後処理へ予約枠の半分を必ず残す
        //   （両者 keep-best＝退化なし）。
        var integrationDeadline = Math.Max(
            Math.Min(hardDeadlineMs - postReserveMs / 2, NowMs() + integrationBudgetMs), NowMs());
        bool IntegrationStop() => NowMs() >= integrationDeadline || cancellationToken.IsCancellationRequested;
        // [3.335.0/外部レビュー P1] 可変 static でなく**この実行の返り値**から読む（実行が重なっても
        //   別の実行の値を拾わない）。読む対象は従来どおり最後の段（RSIThenALNS なら ALNS 段）。
        var archivedElites = chained.FusionElites;
        var fusionElites = archivedElites.Count > 0
            ? archivedElites
            : chained.Alternatives
                .Select((sc, index) => AdaptiveElite.Create(
                    sc.Copy2D(), UnifiedViolationChecker.Check(state, sc),
                    HypothesisEpochRole.EliteRelink, index, 0, false))
                .ToList();
        var integrated = EliteIntegrationPolish.Apply(
            state: state,
            rootSchedule: chained.Schedule,
            elites: fusionElites,
            shouldStop: IntegrationStop,
            deadlineMs: integrationDeadline);
        var tIntegration1 = NowMs();

        var post = V6HotfixPasses.RunPostOptimization(
            state, integrated.Schedule, label.Tech,
            shouldStop: PostShouldStop,
            onPhase: phase => ProgressWatch(phase, null, NowMs() - startMs, budgetMs),
            deadlineMs: hardDeadlineMs);   // [残予算ガード] HF66 が後段パスを押し出さないよう全体締切を渡す
        var tPost1 = NowMs();

        // [高精度化/予算残の活用] 後処理予約枠(budget/12, 8〜25s)は後処理が早期にフィックスポイント到達すると
        //   大半が未使用のまま返っていた。残り5s以上かつ違反が残る場合、最終盤面を起点に keep-best の
        //   追加精製(ALNS)へ回す。runAlns は入力比番兵つき＝結果は post 以上を保証。
        //   停滞検知(stagnationFired)による早期終了時はスキップ＝「無改善なら早く返す」方針を壊さない。
        var refSched = post.Schedule;
        var refReport = post.Report;
        IReadOnlyList<MirrorLog> extraLog = Array.Empty<MirrorLog>();
        {
            // [監査(3e)] 上限は後処理予約枠(postReserveMs, 8〜25s)＝「未使用の予約を再利用する」設計意図に固定。
            var extraMs = Math.Min(hardDeadlineMs - tPost1, postReserveMs);
            // [3.378.0] 予算が残っているのに走らせなかったときは理由を残す。判定は1回だけ評価して分岐と
            //   説明で共有する（isActive相当を2度読むと食い違い得るため）。
            var stopRequested = cancellationToken.IsCancellationRequested;
            var stagnated = Volatile.Read(ref stagnationFired);
            var canExtra = !stopRequested && !stagnated && post.Report.Total > 0;
            if (extraMs >= 5_000 && !canExtra)
            {
                var why = stopRequested ? "停止要求"
                    : stagnated ? "停滞検知で早期終了済み（無改善なら早く返す方針）"
                    : "違反が残っていない";
                // [3.379.0/レビュー] extraMs は予約枠(postReserveMs)でクランプ済みなので「予算残」と呼ぶと
                //   未使用の予算を過小に報告する（300s 予算で 150s に停滞終了すると実際は約145s 余るのに
                //   「予算残25s」と出る）。実測の残りを主に出す。
                var leftMs = Math.Max(hardDeadlineMs - tPost1, 0L);
                extraLog = new List<MirrorLog>
                {
                    new(level: "I", tag: "ExtraRefine",
                        message: $"予算残{leftMs / 1000}s（追加精製に使える上限は予約枠の{extraMs / 1000}s）だが実行せず: {why}"),
                };
            }
            if (extraMs >= 5_000 && canExtra)
            {
                var extraDeadline = tPost1 + extraMs;
                bool ExtraStop() => NowMs() >= extraDeadline || cancellationToken.IsCancellationRequested;
                // [3.335.0] 「他の案」は chained.Alternatives（この実行の返り値）で保持済みなので、
                //   追加精製が static を上書きしても失われない＝旧来の退避/復元は不要になった。
                // [敵対的レビュー3.212.0、仮説数上限撤廃後も維持] 微小予算(5〜25s)の追加精製は本走行と異なり
                //   仮説数を workers まで増やすと悪化しうる（チェーン毎の固定費が小予算を侵食する）→
                //   ここだけ意図的に MAX_HYPOTHESES(5) までにキャップ＝旧来の5×1構成を維持。
                var extra = await V6NativeOptimizer.Optimize(
                        state, post.Schedule,
                        optsR with
                        {
                            Algorithm = V6Algorithm.Alns,
                            TotalBudgetSec = Math.Max((int)(extraMs / 1000L), 5),
                            Workers = Math.Min(optsR.EffectiveWorkers, V6NativeOptimizer.MAX_HYPOTHESES),
                        },
                        ExtraStop, ProgressWatch, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                // [3.287.0 keep-best統一] hard→weighted→total（betterReport と同順）。
                var imp = UnifiedViolationChecker.BetterReport(extra.Report, post.Report);
                if (!imp)
                {
                    // [3.378.0/実機ログ起因] 旧: 改善したときだけログしていたため、実行時間を使った
                    //   追加精製が1行も残らない実行があった。
                    extraLog = new List<MirrorLog>
                    {
                        new(level: "I", tag: "ExtraRefine",
                            message: $"予算残{extraMs / 1000}sで追加精製: 改善なし" +
                                $"（HARD {post.Report.Hard} / total {post.Report.Total} のまま後処理の結果を採用）"),
                    };
                }
                if (imp)
                {
                    refSched = extra.Schedule; refReport = extra.Report;
                    extraLog = new List<MirrorLog>
                    {
                        new(level: "I", tag: "ExtraRefine",
                            message: $"予算残{extraMs / 1000}sで追加精製: HARD {post.Report.Hard}→{extra.Report.Hard} / total {post.Report.Total}→{extra.Report.Total}"),
                        // [監査(3c)/N3と同型] ログ末尾の UnifiedCheck/違反詳細は「精製前の盤面」の診断のまま
                        //   残るため、採用した勤務表の集計を明示して件数の取り違えを防ぐ。
                        new(level: "I", tag: "UnifiedCheck",
                            message: $"採用した勤務表の集計: HARD={extra.Report.Hard} 合計={extra.Report.Total}（直近のUnifiedCheck行・違反詳細は追加精製前の盤面の診断）"),
                    };
                }
            }
        }
        var tExtra1 = NowMs();

        var overBudget = tExtra1 - startMs > budgetMs;
        var timingLog = new MirrorLog(
            level: overBudget ? "W" : "I",
            tag: "TIME",
            message: $"総{SecF(tExtra1 - startMs)}s (予算{seconds}s{(overBudget ? " 超過" : "")}): " +
                $"探索{SecF(tFirst1 - tFirst0)}s + 連鎖{SecF(tChain1 - tFirst1)}s + 統合{SecF(tIntegration1 - tChain1)}s + " +
                $"後処理{SecF(tPost1 - tIntegration1)}s + 追加精製{SecF(tExtra1 - tPost1)}s " +
                $"/ workers設定{effWorkers} " +
                (opts.Algorithm == V6Algorithm.V5
                    ? $"SAチェーン{effWorkers}本"
                    : $"実効仮説{effHypotheses}{(effHypotheses < plannedHypotheses ? $"（設定{plannedHypotheses}をコア数まで縮小）" : "")}"));
        var integrationLog = integrated.Logs;

        // [3.283.0/ログ強化] ウォッチドッグの内部状態を非発火時も1行で可視化。
        // [3.288.0/ログ強化=時間軸] 「いつ探索全体を切り上げるか」を決める予算配分を1行で開示。
        var budgetPlanLog = new MirrorLog(
            level: "I", tag: "TimeBudget",
            message: $"予算配分: 総{seconds}s = 探索{(searchDeadlineMs - startMs) / 1000}s + 後処理予約{postReserveMs / 1000}s" +
                $" / 早期終了の条件: 最短実行{minRunMs / 1000}s経過かつ現フェーズ{phaseGraceMs / 1000}s経過かつ無改善が" +
                $"{stallMs / 1000}s(通常)〜{stallHardMs / 1000}s(頭打ち=HARD下限到達 or c3n構造壁)続いたとき" +
                $" / 構造的HARD下限={hardFloor}");

        List<MirrorLog> BuildWatchdogLog()
        {
            var lastImp = lastImpAtSearchEnd;
            var endStallS = Math.Max(tChain1 - lastImp, 0L) / 1000;
            var nonCovU = Volatile.Read(ref bestNonCovUHard);
            var kind = Volatile.Read(ref bestHard) <= hardFloor && nonCovU == 0
                ? $"plateau=短{stallHardMs / 1000}s"
                : Volatile.Read(ref c3nWallResult) && Volatile.Read(ref bestNonCovUAllC3n)
                    ? $"c3n壁=短{stallHardMs / 1000}s"
                    : $"通常=長{stallMs / 1000}s";
            // [3.375.2/実測で判明] 発火しなかったとき、どの条件が塞いだかを出す。
            // [3.408.0] フェーズ猶予は遅延に降格した（閾値の StallOverrideFactor 倍で上書き発火）ので、
            //   理由として挙げるのは「まだ上書き倍率にも達していない」ときだけ。
            // [3.383.0/ユーザー指示] 実測値(a/bの形)を併記する。
            var blockNote = "";
            if (!Volatile.Read(ref stagnationFired))
            {
                var reasons = new List<string>();
                if (tChain1 - startMs <= minRunMs)
                    reasons.Add($"最短実行未達(実測{(tChain1 - startMs) / 1000}s/{minRunMs / 1000}s)");
                var effStallForLog = kind.StartsWith("通常", StringComparison.Ordinal) ? stallMs : stallHardMs;
                if (tChain1 - lastPhaseAtSearchEnd <= phaseGraceMs &&
                    tChain1 - lastImp <= effStallForLog * StallOverrideFactor)
                    reasons.Add($"現フェーズ猶予未達(実測{(tChain1 - lastPhaseAtSearchEnd) / 1000}s/{phaseGraceMs / 1000}s" +
                        "＝並列ワーカーがフェーズ名を共有し頻繁に更新されるため満たしにくい。停滞が" +
                        $"{effStallForLog * StallOverrideFactor / 1000}s に達すれば猶予に関わらず発火する)");
                if (tChain1 - lastImp <= effStallForLog)
                    reasons.Add($"停滞が閾値未満(実測{(tChain1 - lastImp) / 1000}s/{effStallForLog / 1000}s)");
                if (reasons.Count > 0) blockNote = $"・未発火の理由={string.Join("＋", reasons)}";
            }
            // 探索の後（後処理・追加精製）で改善したなら別項目として出す。探索フェーズの停滞と混ぜない。
            var afterNote = Volatile.Read(ref lastBestImproveMs) > tChain1
                ? $"・探索後も改善あり(経過{(Volatile.Read(ref lastBestImproveMs) - startMs) / 1000}s＝後処理/追加精製)"
                : "";
            var wallNote = Volatile.Read(ref c3nWallCheckedVersion) >= 0
                ? $"・c3n壁診断={(Volatile.Read(ref c3nWallResult) ? "構造的な壁と判定" : "壁ではない（崩す手が実在）")}"
                : "";
            return new List<MirrorLog>
            {
                new(level: "I", tag: "Watchdog",
                    message: $"停滞監視: 最終改善=経過{Math.Max((lastImp - startMs) / 1000, 0)}s・" +
                        $"探索終了時の停滞{endStallS}s・実効閾値({kind})・発火={(Volatile.Read(ref stagnationFired) ? "あり" : "なし")}" +
                        $"・反復(進捗報告ぶん・目安)=最終改善時{FmtIter(lastImpItersAtSearchEnd)}→" +
                        $"探索終了時{FmtIter(itersAtSearchEnd)}（無改善のまま約{FmtIter(itersAtSearchEnd - lastImpItersAtSearchEnd)}転・" +
                        $"総量はAdaptivePortfolioの合計iter参照）{blockNote}{afterNote}{wallNote}"),
            };
        }
        var watchdogLog = BuildWatchdogLog();

        IReadOnlyList<MirrorLog> stagnationLog = Volatile.Read(ref stagnationFired)
            ? new List<MirrorLog>
            {
                new(level: "I", tag: "EarlyStop",
                    message: $"停滞検知: 改善が無いため早期終了（予算{seconds}s中 {(tPost1 - startMs) / 1000}sで停止・" +
                        $"停滞{Volatile.Read(ref stagnationDurationMs) / 1000}s無改善" +
                        // [3.375.0] 何回転ぶん空回りしたか。ここは検索終了時のスナップショットでなく
                        //   ログ構築時点の**最新値**を読む（Kotlin原本 stagnationIters.get() -
                        //   lastBestImproveIters.get() と同じ、後処理/追加精製の改善まで反映する）。
                        (Volatile.Read(ref stagnationIters) >= 0
                            ? $"・発火までに無改善のまま約{FmtIter(Volatile.Read(ref stagnationIters) - Volatile.Read(ref lastBestImproveIters))}転(進捗報告ぶん・目安)"
                            : "") +
                        "・解は最良を維持）" +
                        // [3.281.0/A] c3n構造壁（証明つき）が短い閾値への移行理由だった場合はそれを明示。
                        (Volatile.Read(ref c3nWallResult) && Volatile.Read(ref bestNonCovUAllC3n)
                            ? "（残る必須=禁止連続はForbiddenDiagが構造的な壁と判定済み。希望固定=証明相当/それ以外=探索手の全滅を検証）"
                            : "")),
            }
            : Array.Empty<MirrorLog>();

        // [最終番兵/多重防御] 全段 keep-best のため通常は発火しないが、万一パイプラインが入力より
        // 悪い結果を返した場合は入力を採用し退化を防ぐ（CheckResultWorse をここで配線）。
        var regression = CheckResultWorse(inputReport, refReport);
        var finalSched = regression != null ? normInput : refSched;
        var finalReport = regression != null ? inputReport : refReport;
        IReadOnlyList<MirrorLog> sentinelLog = regression != null
            ? new List<MirrorLog>
            {
                new(level: "W", tag: "Sentinel",
                    message: $"後処理結果が入力より悪化を検知したため入力を採用しました（多重防御）: {regression}"),
                // [N3] ログ末尾には棄却盤面(post)の UnifiedCheck/診断行が履歴として残るため、採用した
                //   勤務表の集計を明示して読者の取り違えを防ぐ。
                new(level: "I", tag: "UnifiedCheck",
                    message: $"採用した勤務表の集計: HARD={inputReport?.Hard} 合計={inputReport?.Total}（直近のUnifiedCheck行・違反詳細は棄却盤面の診断）"),
            }
            : Array.Empty<MirrorLog>();

        // [3.356.0/ユーザー指示] 詳細設定の調整トグルがその実行で実際に何をしたかを1行で開示する。
        //   このC#移植には native/parity 層が無いため nativeOn/parityOn は常に false で呼ぶ
        //   （クラス doc comment 参照）。
        var tuningLog = new MirrorLog(level: "I", tag: "設定の効き",
            message: TuningTelemetry.Summary(nativeOn: false, parityOn: false, softPolishOn: softPolish));

        // [3.288.0/ログ強化=状態軸] 「本当に改善可能な制約が残るか」を最終盤面で1行に集約。
        //   残った族を ①構造的な壁（もう直せない: 構造的covU下限・証明済みc3n壁・HF63が学習した充足困難族）
        //   ②まだ狙える（追えば減る見込み）に仕分ける。
        List<MirrorLog> BuildResidualLog()
        {
            var bd = finalReport.Breakdown;
            var infeasLearned = chained.InfeasibleFamilies;
            var c3nWall = Volatile.Read(ref c3nWallResult) && Volatile.Read(ref bestNonCovUAllC3n);
            var walls = new List<string>();
            var open = new List<string>();
            // [3.375.0/実機ログ起因] 構造床は族ループより先に計算する。open 側から差し引くため。
            var weeklyNow = bd.GetValueOrDefault("weekly", 0);
            var weeklyWall = weeklyNow <= 0 ? 0 : Math.Min(weeklyNow, TryOrZero(() =>
            {
                var pw = ScheduleUtil.CachedProblem(state);
                var cw = ScheduleUtil.CountMatrix(pw, finalSched);
                var f = 0;
                for (var i = 0; i < pw.S; i++)
                    for (var k = 0; k < pw.K; k++)
                        f += ScheduleUtil.WeeklyFloorOfCount(cw[i][k]);
                return f;
            }));
            var personalFloor = TryOrZero(() => V6SanityPort.StructuralPersonalFloor(ScheduleUtil.CachedProblem(state)));
            var aptHighNow = bd.GetValueOrDefault("apt", 0) + bd.GetValueOrDefault("high", 0);
            // apt+high の床は2族の和に対して立つ（片方だけには割り振れない）ので、床が立つときは
            //   open 側もまとめて "apt+high" の1項目として残りを出す。
            var personalWall = personalFloor > 0 && aptHighNow > 0 ? Math.Min(personalFloor, aptHighNow) : 0;
            var covUNow = bd.GetValueOrDefault("covU", 0);
            var covUFloor = hardFloor > 0 && covUNow >= 1 && covUNow <= hardFloor ? covUNow : 0;
            var covUBlocked = covUNow <= 0 ? 0 : TryOrZero(() =>
                CovUBlockedAmount(V6PortAnalyzer.DiagnoseCoverage(state, finalSched, finalReport)));
            var covUWall = CovUStructuralWall(covUNow, hardFloor, covUBlocked);
            foreach (var key in MirrorKeys.All)
            {
                var n0 = bd.GetValueOrDefault(key, 0);
                if (n0 <= 0) continue;
                if (personalWall > 0 && (key == "apt" || key == "high")) continue;   // 下でまとめて出す
                string? structural = key == "c3n" && c3nWall ? "証明済みの壁"
                    : infeasLearned.Contains(key) ? "探索が充足困難と学習"
                    : null;
                if (structural != null) { walls.Add($"{key} {n0}件({structural})"); continue; }
                var n = key switch
                {
                    "weekly" => n0 - weeklyWall,
                    "covU" => n0 - covUWall,
                    _ => n0,
                };
                if (n > 0) open.Add($"{key} {n}件");
            }
            if (covUWall > 0)
            {
                // 床が全部を覆うときだけ従来どおり「構造的下限」（供給不足）と名乗る。それ以外は
                //   「担当者は居るが いまの希望では動かせない」＝データ側で希望を1件調整すれば動きうる、を明示。
                var why = covUFloor >= covUNow ? "構造的下限" : "いまの希望・担当のままでは埋められないと実証済み";
                walls.Add($"covU {covUWall}件({why})");
            }
            // [3.354.0/実機ログ起因] apt と high は「個人の担当構成」から下限が立つ。
            // [3.355.0] weekly も同型: 回数が7の倍数でないぶんは配置では消せない。
            if (weeklyWall > 0) walls.Add($"weekly のうち{weeklyWall}件(回数が7の倍数でない＝配置では消せない)");
            if (personalWall > 0)
            {
                walls.Add($"apt+high のうち{personalWall}件(個人の担当構成＝データ側)");
                var rest = aptHighNow - personalWall;
                if (rest > 0) open.Add($"apt+high {rest}件");
            }
            var wallTxt = walls.Count == 0 ? "なし" : string.Join(" / ", walls);
            var openTxt = open.Count == 0 ? "なし＝これ以上は追っても減りません" : string.Join(" / ", open);
            return new List<MirrorLog>
            {
                new(level: "I", tag: "残存分析", message: $"もう直せない: {wallTxt} ／ まだ狙える: {openTxt}"),
            };
        }
        var residualLog = BuildResidualLog();

        // [3.387.0/3.388.0] 並行アクセスの実レースは実機でしか確かめられない、と記録してきた項目の
        //   唯一の実測点。publishLiveBest の CAS が再試行した回数＝別スレッドと同時に publish が
        //   起きた回数。0 のときは出さない。
        var contentionCount = V6NativeOptimizer.LiveBestContentionCount();
        IReadOnlyList<MirrorLog> contentionLog = contentionCount <= 0
            ? Array.Empty<MirrorLog>()
            : new List<MirrorLog>
            {
                new(level: "W", tag: "LiveBestContention",
                    // [3.388.0/外部レビュー] 主張を観測の範囲へ戻す。この数は CAS の再試行回数＝
                    //   「複数ワーカーが同時に途中最良を publish した回数」であって、3.385.0 が直した
                    //   特定の交錯そのものを数えてはいない。非ゼロは必要条件であって十分条件ではない。
                    message: $"途中最良の同時publish: {contentionCount}回（複数ワーカーが同時に最良を更新した回数。" +
                        "3.385.0 で直した競合が成立しうる条件が実機で揃っている＝0 なら揃っていない）"),
            };

        // [3.378.0/実機ログ起因・デバッグ用] 段をまたいだスコアの収支を1行で追えるようにする。
        //   各段の**採用値**を同じ物差しで並べる。keep-best は hard→weightedScore→total の順なので
        //   重みも出す＝「total は同じなのに採用された」が矛盾でなく weighted の改善だと読めるようにする。
        List<MirrorLog> BuildLedgerLog()
        {
            var stages = new (string Name, ViolationReport R)[]
            {
                // [ローカル関数のキャプチャ由来のNRT警告対策] inputReport は Check() の非null戻り値で
                //   再代入されない（532/534/543行と同一の値）が、ローカル関数からのキャプチャでは
                //   フロー絞り込みが宣言時の注釈へ保守的に戻るため CS8619 が出る。null許容演算子で解消。
                ("入力", inputReport!),
                ("探索", chained.Report),
                ("統合", integrated.Report),
                ("後処理", post.Report),
                ("追加精製", refReport),
                ("採用", finalReport),
            };
            var sb = new StringBuilder("スコア収支（各段の採用値・必須/合計/重み）: ");
            ViolationReport? prev = null;
            var idle = new List<string>();
            for (var idx = 0; idx < stages.Length; idx++)
            {
                var st = stages[idx];
                if (idx > 0) sb.Append(" → ");
                var w = st.R.WeightedScore;
                sb.Append($"{st.Name} {st.R.Hard}/{st.R.Total}/w{(long)w}");
                var p = prev;
                if (p != null)
                {
                    var dt = st.R.Total - p.Total;
                    var dw = w - p.WeightedScore;
                    if (dt == 0 && Math.Abs(dw) < 0.5)
                    {
                        sb.Append("(±0)"); idle.Add(st.Name);
                    }
                    else
                    {
                        sb.Append(dt > 0 ? $"(合計+{dt}" : $"(合計{dt}");
                        if (Math.Abs(dw) >= 0.5) sb.Append(dw > 0 ? $"・重み+{(long)dw}" : $"・重み{(long)dw}");
                        sb.Append(")");
                    }
                }
                prev = st.R;
            }
            // 「時間を使ったのに1点も動かなかった段」を名指しする（どこを削れるかの判断材料）。
            // [3.379.0/レビュー] 探索も対象に入れる。
            var ms = new Dictionary<string, long>
            {
                ["探索"] = tChain1 - tFirst0,
                ["統合"] = tIntegration1 - tChain1,
                ["後処理"] = tPost1 - tIntegration1,
                ["追加精製"] = tExtra1 - tPost1,
            };
            var wasted = idle
                .Where(n => ms.TryGetValue(n, out var v) && v >= 1_000)
                .Select(n => $"{n}{ms[n] / 1000}s")
                .ToList();
            if (wasted.Count > 0) sb.Append($" ／ 変化なしに費やした段: {string.Join("・", wasted)}");
            return new List<MirrorLog> { new(level: "I", tag: "スコア収支", message: sb.ToString()) };
        }
        var ledgerLog = BuildLedgerLog();

        // post.report.logs = [HF80/67/66/70 logs + POST timing + UnifiedViolationChecker logs]。
        // post.logs は post.report.logs の部分集合なので両方足すと重複する → post.report.logs のみ使う。
        var logs = new List<MirrorLog> { timingLog, budgetPlanLog, tuningLog };
        logs.AddRange(sentinelLog);
        logs.AddRange(integrationLog);
        logs.AddRange(extraLog);
        logs.AddRange(watchdogLog);
        logs.AddRange(contentionLog);
        logs.AddRange(ledgerLog);
        logs.AddRange(residualLog);
        logs.AddRange(stagnationLog);
        logs.AddRange(gate.Logs);
        logs.AddRange(first.PhaseLogs);
        if (!ReferenceEquals(chained, first)) logs.AddRange(chained.PhaseLogs);
        logs.AddRange(post.Report.Logs);

        // [3.327.0/外部レビュー High1] post の診断（C1頭打ち・回数固定の却下記録）は post.schedule を
        //   観測した結果。finalSched はこのあと ExtraRefine で差し替わる（refSched）か、最終番兵で入力へ
        //   戻る（normInput）ことがある。盤面が一致するときだけ診断を通す。
        var postForResult = finalSched.ContentDeepEquals(post.Schedule) ? post : null;
        return new ActionResult(finalSched, finalReport with { Logs = logs }, $"optimize:{label.Tech}", busy, logs, postForResult,
            Alternatives: chained.Alternatives);
    }
}
