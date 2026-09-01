using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [Phase 10 本体] Kotlin原本 <c>runInBackground()</c>/<c>applyBgResult()</c>
/// （<c>MagiViewModel.kt</c> 420-563行）と <c>OptimizationWorker.kt</c>（428行、<c>doWork()</c>）の移植——
/// バックグラウンド最適化そのもの。
///
/// [設計判断: 子プロセスではなく同一プロセス内 Task] 当初「子プロセス」案を検討したが、
/// <see cref="Work.OptimizationRepository"/> 自身のクラスKDocが「Kotlin原本はAndroid/WorkManagerに
/// 一切依存しない純粋な object（<b>プロセス内</b> pub/sub）」と明記しているとおり、Progress/Result の
/// 公開APIは値をメモリ上で直接やり取りする設計（volatile フィールド＋event）で、既にその前提で
/// 完全移植済み（フェーズ9）。子プロセス化すると <c>MagiState</c>/<c>ViolationReport</c> 等を
/// プロセス境界の外側へ運ぶシリアライズ層が新たに要る＝既に動いている pub/sub ブリッジを
/// 作り替えることになる。同一プロセスの <c>Task</c> ならこのブリッジをそのまま使える。
///
/// [Kotlin原本との差①: WorkManager→Task] Android の WorkManager（前景サービス化・OSのジョブ
/// スケジューラによる再起動保証）に直接対応する Windows デスクトップの機構は無い。この移植では
/// 「バックグラウンドで計算を進め、UIをブロックしない」という<b>意図</b>を、UIに紐付かない
/// <see cref="_bgCts"/> で管理する <c>Task.Run</c> で満たす。ただし WorkManager と異なり
/// <b>アプリのプロセス自体が終了すれば計算も終わる</b>（OSがプロセスを再起動して継続させる仕組みは無い）。
/// kill 耐性（強制終了からの再開）は Phase 10 で先に移植した <see cref="Work.RunFiles"/> ベースの
/// スナップショット退避／起動時復元（<c>RestoreOnStartup</c>）がそのまま担う——「プロセスが生きている
/// 間はウィンドウを閉じても計算が続く」ことと「プロセスごと死んでも次回起動時に再開できる」ことは
/// 別の性質で、このピースが実装するのは主に後者（前者はアプリのライフサイクル設計＝トレイ常駐等の
/// 別判断で、シェル側のスコープとして明示的に残す。無いものを実装済みに見せない＝HF77）。
///
/// [Kotlin原本との差②: Stop() の背景停止] <c>MagiViewModel.Optimize.cs</c> の <c>Stop()</c> は
/// 「Windows側の背景実行機構が無い」ことを理由に背景停止を警告ログのみに留めていたが、このピースで
/// 実際にバックグラウンド <c>Task</c> を導入したため、<c>Stop()</c> 側も <see cref="_bgCts"/> を
/// Cancel して実際に止める（機構は Kotlin原本の <c>WorkManager.cancelUniqueWork</c> と異なるが、
/// 「背景計算を止める」という意図は忠実に果たす）。
///
/// [Kotlin原本との差③: 通知/バブル] <c>OptimizationWorker.kt</c> の Android
/// Notification／会話バブル（<c>BubbleSupport</c>）は Windows に直接の対応が無いため移植しない。
/// 完了・失敗・停止のフィードバックは（Android通知に頼らず）このピースが直接 <see cref="UiState.Message"/>
/// と操作ログへ書く——Android版は完了時の <c>Ui.Message</c> 反映を <c>applyBgResult</c> に、
/// 失敗/停止の利用者向け通知を Android Notification に分担させていたが、Windows には後者が無いため
/// 失敗/停止でも <see cref="UiState.Message"/> を明示的に更新する（Android版より手厚くなる意図的な差分）。
///
/// [Kotlin原本との差④: pub/sub 越しにしない] Kotlin原本の Worker は WorkManager が生成する
/// <b>別オブジェクト</b>なので、進捗も結果も ViewModel へ届けるには <see cref="Work.OptimizationRepository"/>
/// という静的なバスを経由するしかない（Worker役が publish → <c>init{}</c> で購読した別の購読者が
/// <c>applyBgResult</c> を呼ぶ）。このC#移植では背景実行ロジックが <see cref="MagiViewModel"/> 自身の
/// メソッドとして存在するため、同じ理由が無い——<see cref="RunInBackgroundCoreAsync"/> は前景側
/// （<c>RunV6FullOptimizeCoreAsync</c>）と同じく <see cref="Ui"/> へ直接書き、成功時は
/// <see cref="ApplyBgResult"/> を<b>直接 await</b> する（<see cref="Work.OptimizationRepository.PublishResult"/>/
/// <see cref="Work.OptimizationRepository.ResultPublished"/> のイベントには乗せない）。
/// [この差分の理由＝テストで発見した実バグ] 当初は Kotlin原本のとおり「コンストラクタで
/// <c>ResultPublished</c> を購読し、Worker役は publish するだけ」という構造で実装したが、
/// <c>OptimizationRepository</c> は<b>プロセス全体で共有される static</b> なため、テストで
/// <c>MagiViewModel</c> を複数生成すると（本番は Android の単一 Activity 同様シングルトン運用を
/// 想定している一方、xUnit は1テストごとに新しいインスタンスを作る）<b>過去のテストが作った
/// インスタンスの購読が残ったまま</b>で、無関係なテストの <c>PublishResult</c> 呼出しにまで
/// 反応してしまう実害を確認した（既存の <c>OptimizationRepositoryTest.PublishResultUpdatesTheValueAndRaisesTheEventIncludingNull</c>
/// が本ピース追加直後に失敗＝別インスタンスの購読が結果を先に握り潰していた）。<c>MagiViewModel</c> に
/// <c>IDisposable</c>／購読解除の仕組みは無く、それを新設して既存391件のテストへ波及させるより、
/// そもそも「同一プロセス内なら直接呼べる」という設計判断（クラスKDoc冒頭）を素直に徹底するほうが
/// 単純かつ安全——publish/subscribe は「結果を静的バス経由でも参照できるようにする」という補助的な
/// 役割にとどめ、<see cref="ApplyBgResult"/> の呼出し自体はイベント購読に依存させない。
/// </summary>
public sealed partial class MagiViewModel
{
    // Kotlin原本 328-330行相当: 背景の結果が「開始時の入力の答え」であることを確かめるための指紋。
    private long _bgStateKey;
    private long _bgRunId;

    /// <summary>[Kotlin原本との差①] 背景 Task 専用のキャンセルトークン。<c>_job</c>（前景専用）とは
    /// 別に持つ——前景と背景は同時に走らないが（<c>RunBlockedByInFlight</c> が排他する）、
    /// <c>Stop()</c> が「どちらが走っていても確実に止める」ために両方を毎回 Cancel する。</summary>
    private CancellationTokenSource? _bgCts;

    /// <summary>[テスト可視性のための追加] 直近の <see cref="RunInBackground"/> が背後で走らせる Task
    /// （成功時の <see cref="ApplyBgResult"/> 呼出しも含めて待てる＝クラスKDoc「Kotlin原本との差④」参照）。</summary>
    internal Task? LastRunInBackgroundTask { get; private set; }

    /// <summary>バックグラウンドで最適化を開始。完了時に画面反映。Kotlin原本 <c>runInBackground()</c> の移植。</summary>
    public void RunInBackground()
    {
        var st0 = _state;
        var sched0 = _currentSchedule;
        if (st0 is null || sched0 is null) return;
        if (RunBlockedByInFlight("バックグラウンド計算の開始")) return;
        if (!EnsureValidForRun(st0, sched0)) return;
        PushUndo();
        OptimizationRepository.Clear();

        // [3.327.0/外部レビュー High3 の由来をそのまま記録] 実行の識別子を先に確定する。
        //   ファイル名は固定なので、これが無いと置き換えられた旧実行が新実行の入力を消したり、
        //   別データの結果を書き残したりできてしまう。下位に乱数を混ぜ同一ミリ秒の衝突を避ける。
        var runId = NowMs() * 1000L + Random.Shared.Next(0, 1000);
        // [3.410.0/U-02 の由来をそのまま記録] 順序が重要——**所有権を立ててから**旧途中状態を掃除する。
        //   先に掃除すると、まだ走っている旧実行が掃除の隙にファイルを書き戻せてしまう窓ができる。
        var markerOk = BgFiles.BeginRun(runId);
        if (markerOk) ClearBgFiles("背景計算の開始（旧途中状態の掃除）", keepRunId: true);

        // [外部レビュー P1-01 の由来をそのまま記録] 非原子な書込はプロセス強制終了で壊れたJSONを残す。
        var inputOk = markerOk && TryWriteAtomically(BgFiles.Input, StateJsonSerializer.Serialize(st0, sched0));
        if (!markerOk || !inputOk)
        {
            ClearBgFiles("背景計算の開始に失敗");
            Notify("バックグラウンド計算を開始できませんでした（端末の空き容量をご確認ください）", "W");
            return;
        }

        _bgStateKey = StateKey(st0);
        _bgRunId = runId;
        OptimizationRepository.Request = new OptimizationRepository.RequestPayload(st0, sched0.Copy2D());
        OptimizationRepository.Seconds = Ui.BudgetSec;
        OptimizationRepository.Workers = Ui.Workers;

        // [Kotlin原本との差①] `OptimizationRepository.SetRunning(true)` は Kotlin原本では Worker.doWork()
        //   の内部（enqueue から実際の実行開始まで遅延しうる）で立つが、この移植では enqueue と
        //   実行開始が「同じプロセス内で即座に Task.Run される」ため区別する理由が無い。ここで
        //   同期的に立てることで、`RunBlockedByInFlight`（`OptimizeInFlight()` 経由で
        //   `OptimizationRepository.Running` を見る）が次の呼出しを確実にブロックできる
        //   （Kotlin原本はこの窓を WorkManager 自身の enqueueUniqueWork 一意性で塞いでいた——
        //   Windows側にその一意性機構が無い分、ここで同期的に立てて塞ぐ）。
        OptimizationRepository.SetRunning(true);

        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.HasResult = false;
        Ui.InterruptedRun = false;
        Ui.InterruptedInfo = null;
        Ui.Message = "バックグラウンドで最適化を開始しました（完了時に通知）";
        WriteRunMarker("bg");
        LogOp("I", $"バックグラウンド最適化 開始 (予算{Ui.BudgetSec}s, 並列{Ui.Workers})");

        var cts = new CancellationTokenSource();
        _bgCts = cts;
        LastRunInBackgroundTask = RunInBackgroundCoreAsync(st0, sched0.Copy2D(), runId, Ui.BudgetSec, Ui.Workers, cts.Token);
    }

    private static bool TryWriteAtomically(string path, string text)
    {
        try
        {
            return new RunFiles(Path.GetDirectoryName(path) ?? ".").WriteAtomically(path, text);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// バックグラウンド実行の本体。Kotlin原本 <c>OptimizationWorker.doWork()</c>（56-343行）の移植——
    /// Android固有の前景サービス化(<c>setForeground</c>)・通知(<c>NotificationCompat</c>)・
    /// 会話バブル(<c>BubbleSupport</c>)は移植しない（クラスKDoc「Kotlin原本との差③」参照）。
    ///
    /// [Kotlin原本との差] Worker.doWork() 冒頭の「<c>OptimizationRepository.request ?: loadInputFromFile(ctx)</c>」
    /// （プロセス再起動でメモリの request が失われた場合にファイルから復元する分岐）は、この移植では
    /// 不要——<see cref="RunInBackground"/> がこのメソッドを起動した時点で <paramref name="st0"/>/
    /// <paramref name="sched0"/> は既に確定した引数として渡っており、「enqueue後・実行開始前にプロセスが
    /// 再起動する」という中間状態が同一プロセス設計には存在しない（その種のプロセス死からの再開は
    /// 次回起動時の <c>RestoreOnStartup</c> が別途担う）。同じ理由で Worker 冒頭の「入力退避（二重書き）」
    /// も省略する（<see cref="RunInBackground"/> が既に書いている）。
    /// </summary>
    private async Task RunInBackgroundCoreAsync(
        MagiState st0, int[][] sched0, long runId, int budgetSec, int workers, CancellationToken ct)
    {
        var t0 = NowMs();
        var steps = new List<string>();
        var terminalLogged = false;
        var droppedProgress = 0;
        void Step(string name) => steps.Add($"{name}@{(NowMs() - t0) / 1000}秒");
        // [クラスKDoc「Kotlin原本との差④」] Kotlin原本の Worker.note() は別オブジェクトから
        //   ViewModel へ届けるため OptimizationRepository.publishNote 経由だったが、ここは同じ
        //   MagiViewModel のメソッドなので LogOp を直接呼ぶ。
        void BgNote(string msg, string level = "I") => LogOp(level, $"バックグラウンド計算: {msg}");
        void Terminal(string msg, string level = "I")
        {
            if (terminalLogged) return;
            terminalLogged = true;
            var dropped = droppedProgress > 0 ? $"・進捗{droppedProgress}回は所有権喪失で破棄" : "";
            BgNote(msg + dropped + (steps.Count == 0 ? "" : $" ／ 手順: {string.Join("→", steps)}"), level);
        }
        void ReportClear(string where)
        {
            IReadOnlyList<string> stuck;
            try { stuck = BgFiles.Clear(); }
            catch (Exception e) { BgNote($"{where} の片付けに失敗しました: {e.GetType().Name}", "W"); return; }
            if (stuck.Count > 0)
            {
                BgNote($"{where} の片付けで削除できないファイルが残りました: {string.Join("・", stuck)}" +
                    "（次回起動が古い状態を「中断」として掴む可能性があります）", "W");
            }
        }
        void ReportDelete(string f, string what)
        {
            bool ok;
            try { ok = !File.Exists(f) || TryDeleteFile(f); }
            catch { ok = false; }
            if (!ok) BgNote($"{what} を削除できませんでした（次回起動が古い状態を「中断」として掴む可能性があります）", "W");
        }

        bool OwnsFiles() => BgFiles.Owns(runId);

        if (!OwnsFiles())
        {
            BgNote("開始前に所有権を失っていたため何もしませんでした（置き換えまたは停止）");
            return;
        }

        var releasedByMe = false;
        var lastSnapMs = 0L;
        var lastPublishMs = long.MinValue / 4;
        var lostOwnership = false;
        var wallStart = NowMs();
        try
        {
            void OnProgress(string phase, ViolationReport? report, long _, long __)
            {
                var wallElapsed = NowMs() - wallStart;
                if (lostOwnership) { droppedProgress++; return; }
                if (report is null) return;
                if (wallElapsed - lastPublishMs >= OptimizationRepository.ProgressPushMs)
                {
                    lastPublishMs = wallElapsed;
                    if (!OwnsFiles()) { lostOwnership = true; droppedProgress++; return; }
                    Ui.BestHard = report.Hard;
                    Ui.BestSoft = report.Soft;
                    Ui.TotalViolations = report.Total;
                    Ui.ElapsedMs = wallElapsed;
                }
                // [#4/C1の由来をそのまま記録] 途中最良解を8秒ごとにスナップショット
                //   → kill されても「途中結果から再開」できる。
                if (wallElapsed - lastSnapMs > 8_000L)
                {
                    lastSnapMs = wallElapsed;
                    var live = V6NativeOptimizer.LiveBest;
                    if (live is not null && OwnsFiles())
                    {
                        try
                        {
                            BgFiles.WriteAtomically(
                                BgFiles.Snapshot, StateJsonSerializer.Serialize(st0, live.ToIntArray2D()),
                                commitGuard: OwnsFiles);
                        }
                        catch (Exception e)
                        {
                            BgNote("途中経過の退避に失敗（kill されると途中の改善が失われます）: " +
                                $"{e.GetType().Name}", "W");
                        }
                    }
                }
            }

            var res = await _optimizationService.OptimizeAsync(
                st0, sched0.Copy2D(), budgetSec, workers,
                // [Kotlin原本との差なし・逐語] Worker.doWork() は softPolish/requestedAlgorithm を
                //   指定せず handleOptimize の既定値（softPolish=false, AUTO）に任せている
                //   （V6FinalPort.kt:257-258）——前景の Ui.SoftPolish/Ui.V6Algorithm は使わない。
                softPolish: false, requestedAlgorithm: V6Algorithm.Auto,
                allowImpossible: true, onProgress: OnProgress, cancellationToken: ct);

            // [3.327.0/外部レビュー High3 の由来をそのまま記録] 置き換えられた実行の結果は
            //   公開も保存もしない。
            if (OwnsFiles())
            {
                var saved = false;
                try
                {
                    saved = BgFiles.WriteAtomically(
                        BgFiles.Result, StateJsonSerializer.Serialize(st0, res.Schedule), commitGuard: OwnsFiles);
                }
                catch (Exception e)
                {
                    BgNote($"完了結果の保存に失敗（プロセスが終了すると結果が失われます）: {e.GetType().Name}", "W");
                }

                if (!saved && !OwnsFiles())
                {
                    Terminal("結果の保存直前に所有権を失いました（置き換え）。公開も片付けもしていません", "W");
                }
                else
                {
                    if (saved) Step("耐久保存");
                    if (saved)
                    {
                        ReportDelete(BgFiles.Input, "入力ファイル");
                        ReportDelete(BgFiles.Snapshot, "途中最良のスナップショット");
                        Step("片付け");
                    }
                    else
                    {
                        BgNote("結果を保存できなかったため、入力と途中経過は残します（次回起動で再開できます）", "W");
                    }
                    ReportDelete(BgFiles.RunId, "所有権マーカー");
                    releasedByMe = true;
                    Terminal($"完了（必須{res.Report.Hard} 合計{res.Report.Total}）" +
                        (saved ? "" : "・結果を保存できず（プロセス終了で失われます）"));
                    // [クラスKDoc「Kotlin原本との差④」] Kotlin原本は publishResult(...) 経由で
                    //   別途購読された applyBgResult() を呼ぶが、この移植では同じオブジェクトの
                    //   メソッドとして直接 await する（結果の採否判定＝keep-best は ApplyBgResult の
                    //   責務のまま分離を保つ——呼び方だけを直接呼出しへ変える）。
                    Step("結果反映開始");
                    await ApplyBgResult(new OptimizationRepository.BgResult(res.Schedule, res.Report, res.Phase, runId));
                }
            }
            else
            {
                Terminal("完了したが所有権を失っていたため保存も公開もしませんでした（置き換え）");
            }
        }
        catch (OperationCanceledException)
        {
            var owned = OwnsFiles();
            if (owned) { ReportClear("停止"); releasedByMe = true; }
            Terminal(owned ? "停止（片付け済み）" : "停止（所有権が無いため片付けなし）");
            // [Kotlin原本との差③] Android通知が無いため、ここで直接 Ui を更新する（クラスKDoc参照）。
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.Message = "バックグラウンド計算を停止しました";
            throw;
        }
        catch (Exception e)
        {
            var owned = OwnsFiles();
            if (owned) { ReportClear("失敗"); releasedByMe = true; }
            Terminal($"失敗: {e.GetType().Name}: {e.Message}" + (owned ? "（片付け済み）" : "（所有権なし）"), "W");
            // [Kotlin原本との差③] Android通知が無いため、ここで直接 Ui を更新する（クラスKDoc参照）。
            Ui.MessageIsError = true;
            Ui.Running = false;
            Ui.Message = $"バックグラウンド計算に失敗しました（{e.GetType().Name}）";
        }
        finally
        {
            if (releasedByMe || OwnsFiles()) OptimizationRepository.SetRunning(false);
            Terminal("終了: 完了・停止・失敗のいずれも記録されませんでした（想定外の経路。Error(OOM等)や停止処理自体の失敗が疑われます）", "W");
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try { File.Delete(path); return true; }
        catch { return false; }
    }

    /// <summary>
    /// 背景計算の完了結果を反映（keep-best 判定含む）。Kotlin原本 <c>applyBgResult()</c>
    /// （497-563行）の移植。<see cref="RunInBackgroundCoreAsync"/> が成功時に直接 await する
    /// （クラスKDoc「Kotlin原本との差④」参照）。[テスト可視性のためinternal化] Kotlin原本は
    /// private fun——このC#移植では <see cref="RunInBackgroundCoreAsync"/> をフルに駆動せずとも
    /// keep-best 判定単体を検証できるようにする。
    /// </summary>
    internal async Task ApplyBgResult(OptimizationRepository.BgResult r)
    {
        var st0 = _state;
        if (st0 is null) return;
        // [3.410.0/U-01 の由来をそのまま記録] 実行の識別子で先に弾く。入力の指紋は「入力が同じなら
        //   別の実行でも一致する」ため、置き換えられた古い実行が完了間際に publish した結果を
        //   通してしまう。runId==0 は識別子を持たない経路（この移植では実質未使用だが、Kotlin原本の
        //   「プロセス再起動後のファイル復元」に相当する経路を将来足す余地として残す）。
        if (_bgRunId != 0L && r.RunId != 0L && r.RunId != _bgRunId)
        {
            LogOp("W", "バックグラウンド計算の結果を破棄しました（置き換えられた古い実行の結果）");
            return;
        }
        // [3.328.0/外部レビューの由来をそのまま記録] 背景の結果は「開始時の入力」に対して計算されたもの。
        //   実行中に別データを開く等で入力が変わっていたら、その結果は今の入力の答えではないので捨てる。
        if (_bgStateKey != 0L && _bgStateKey != StateKey(st0))
        {
            _bgStateKey = 0L;
            _bgRunId = 0L;
            LogOp("W", "バックグラウンド計算の結果を破棄しました（計算中に設定またはデータが変わったため）");
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.Message = "計算中に設定が変わったため、結果は反映しませんでした。もう一度つくってください。";
            return;
        }
        _bgStateKey = 0L;
        _bgRunId = 0L;

        // [再実行 keep-best] 背景完了結果が前回採用解より悪化なら前回を維持（前景と同じ方針）。
        var prev = _resultSchedule;
        if (prev is not null)
        {
            var prevReport = await Task.Run(() => UnifiedViolationChecker.Check(st0, prev));
            var newHard = (long)r.Report.Hard;
            var newTotal = r.Report.Total;
            var worse = UnifiedViolationChecker.ReportComparer.Compare(prevReport, r.Report) < 0;
            if (worse)
            {
                var kept = prev.Copy2D();
                _currentSchedule = kept;
                _resultSchedule = kept;
                _state = st0.WithSchedule(kept);
                AutoSave();
                await PushReportAsync(_state ?? st0, kept, prevReport, transform: ui =>
                {
                    ui.MessageIsError = false;
                    ui.Running = false;
                    ui.HasResult = true;
                    ui.Message = $"今回(必須{newHard}/合計{newTotal})は前回(必須{prevReport.Hard}/合計{prevReport.Total})より改善せず。前回の結果を維持しました。";
                });
                LogOp("I", $"バックグラウンド: 今回 必須{newHard}/合計{newTotal} は前回 以下に改善せず → 前回を維持");
                ClearRunMarker();
                ClearBgFiles("背景結果: 前回を維持");
                OptimizationRepository.Request = null;
                OptimizationRepository.PublishResult(null);
                return;
            }
        }
        var sched = r.Schedule.Copy2D();
        _currentSchedule = sched;
        _resultSchedule = sched;
        _state = st0.WithSchedule(sched);
        AutoSave();
        await PushReportAsync(_state ?? st0, sched, r.Report, transform: ui =>
        {
            ui.MessageIsError = false;
            ui.Running = false;
            ui.HasResult = true;
            ui.Message = $"バックグラウンド最適化 完了: 必須={r.Report.Hard} 合計={r.Report.Total}";
        });
        LogOp("I", $"バックグラウンド最適化 完了 必須={r.Report.Hard} 合計={r.Report.Total}");
        _lastResultHard = r.Report.Hard;
        ClearRunMarker();
        ClearBgFiles("背景最適化 完了");
        // 消費したらクリア（再生成時の二重適用を防ぐ）。
        OptimizationRepository.Request = null;
        OptimizationRepository.PublishResult(null);
    }
}
