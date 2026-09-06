using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [Phase 10 本体 → 2026-09-01 簡略化] Kotlin原本 <c>runInBackground()</c>/<c>applyBgResult()</c>
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
/// <see cref="_bgCts"/> で管理する <c>Task.Run</c> で満たす。<b>アプリのプロセス自体が終了すれば
/// 計算も終わる</b>（OSがプロセスを再起動して継続させる仕組みは無い）。
///
/// [2026-09-01: kill耐性を撤去（ユーザー明示判断）] 当初はここに <see cref="Work.RunFiles"/>
/// ベースの共有ファイル4種（入力・完了結果・8秒ごとの途中最良スナップショット・所有権マーカー）を
/// 書き、プロセスが強制終了しても次回起動で再開できるようにしていた。ユーザーから「Windows11版は
/// クラッシュからの復旧はそこまで重視しない」と明示判断があり（経緯は <c>windows/README.md</c>
/// フェーズ10節参照）、この機構を全撤去した。背景実行はこのファイルの中だけで完結する
/// 純粋なインメモリ処理になり、ディスクI/Oは一切行わない（自動保存＝<c>MagiViewModel.Persistence.cs</c>
/// の <see cref="AutoSave"/> は完了結果の反映時に呼ぶため、確定した結果自体は従来どおり残る）。
///
/// [Kotlin原本との差②: Stop() の背景停止] <c>MagiViewModel.Optimize.cs</c> の <c>Stop()</c> は
/// 実際にバックグラウンド <c>Task</c> を <see cref="_bgCts"/> を Cancel して止める（機構は
/// Kotlin原本の <c>WorkManager.cancelUniqueWork</c> と異なるが、「背景計算を止める」という意図は
/// 忠実に果たす）。
///
/// [Kotlin原本との差③: 通知/バブル] <c>OptimizationWorker.kt</c> の Android
/// Notification／会話バブル（<c>BubbleSupport</c>）は Windows に直接の対応が無いため移植しない。
/// 完了・失敗・停止のフィードバックは（Android通知に頼らず）このピースが直接 <see cref="UiState.Message"/>
/// と操作ログへ書く。
///
/// [Kotlin原本との差④: pub/sub 越しにしない] Kotlin原本の Worker は WorkManager が生成する
/// <b>別オブジェクト</b>なので、進捗も結果も ViewModel へ届けるには <see cref="Work.OptimizationRepository"/>
/// という静的なバスを経由するしかない。このC#移植では背景実行ロジックが <see cref="MagiViewModel"/> 自身の
/// メソッドとして存在するため、同じ理由が無い——<see cref="RunInBackgroundCoreAsync"/> は前景側
/// （<c>RunV6FullOptimizeCoreAsync</c>）と同じく <see cref="Ui"/> へ直接書き、成功時は
/// <see cref="ApplyBgResult"/> を<b>直接 await</b> する（<see cref="Work.OptimizationRepository.PublishResult"/>/
/// <see cref="Work.OptimizationRepository.ResultPublished"/> のイベントには乗せない）。
/// [この差分の理由＝テストで発見した実バグ] 当初は Kotlin原本のとおり「コンストラクタで
/// <c>ResultPublished</c> を購読し、Worker役は publish するだけ」という構造で実装したが、
/// <c>OptimizationRepository</c> は<b>プロセス全体で共有される static</b> なため、テストで
/// <c>MagiViewModel</c> を複数生成すると<b>過去のテストが作った
/// インスタンスの購読が残ったまま</b>で、無関係なテストの <c>PublishResult</c> 呼出しにまで
/// 反応してしまう実害を確認した。<c>MagiViewModel</c> に
/// <c>IDisposable</c>／購読解除の仕組みは無く、それを新設して既存テストへ波及させるより、
/// そもそも「同一プロセス内なら直接呼べる」という設計判断（クラスKDoc冒頭）を素直に徹底するほうが
/// 単純かつ安全——publish/subscribe は「結果を静的バス経由でも参照できるようにする」という補助的な
/// 役割にとどめ、<see cref="ApplyBgResult"/> の呼出し自体はイベント購読に依存させない。
/// </summary>
public sealed partial class MagiViewModel
{
    // Kotlin原本 328-330行相当: 背景の結果が「開始時の入力の答え」であることを確かめるための指紋。
    // [2026-09-01] ディスクI/Oとは無関係のインメモリ整合性チェックなので kill耐性撤去後も維持する
    // （「Stop()後に旧タスクが遅れて完了し、新しい入力の結果を誤って上書きする」ことを防ぐ）。
    private long _bgStateKey;
    private long _bgRunId;
    /// <summary>[3.475.0/論理監査] keep-best の比較先＝この背景実行に渡した入力盤面。無い（復元経路）ときだけ前回の結果と比較する。</summary>
    private int[][]? _bgInput;

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

        // [3.327.0/外部レビュー High3 の由来をそのまま記録] 実行の識別子を先に確定する
        //   （置き換えられた旧実行の結果を新実行のものと取り違えないためのインメモリ指紋。
        //   2026-09-01 のkill耐性撤去後もこの用途自体は残る＝ApplyBgResult のガード参照）。
        var runId = NowMs() * 1000L + Random.Shared.Next(0, 1000);

        _bgStateKey = StateKey(st0);
        _bgRunId = runId;
        _bgInput = sched0.Copy2D();
        OptimizationRepository.Request = new OptimizationRepository.RequestPayload(st0, sched0.Copy2D());
        OptimizationRepository.Seconds = Ui.BudgetSec;
        OptimizationRepository.Workers = Ui.Workers;

        // [Kotlin原本との差①] `OptimizationRepository.SetRunning(true)` は Kotlin原本では Worker.doWork()
        //   の内部（enqueue から実際の実行開始まで遅延しうる）で立つが、この移植では enqueue と
        //   実行開始が「同じプロセス内で即座に Task.Run される」ため区別する理由が無い。ここで
        //   同期的に立てることで、`RunBlockedByInFlight`（`OptimizeInFlight()` 経由で
        //   `OptimizationRepository.Running` を見る）が次の呼出しを確実にブロックできる。
        OptimizationRepository.SetRunning(true);

        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.HasResult = false;
        Ui.Message = "バックグラウンドで最適化を開始しました（完了時に通知）";
        LogOp("I", $"バックグラウンド最適化 開始 (予算{Ui.BudgetSec}s, 並列{Ui.Workers})");

        var cts = new CancellationTokenSource();
        _bgCts = cts;
        LastRunInBackgroundTask = RunInBackgroundCoreAsync(st0, sched0.Copy2D(), runId, Ui.BudgetSec, Ui.Workers, cts.Token);
    }

    /// <summary>
    /// バックグラウンド実行の本体。Kotlin原本 <c>OptimizationWorker.doWork()</c>（56-343行）の移植——
    /// Android固有の前景サービス化(<c>setForeground</c>)・通知(<c>NotificationCompat</c>)・
    /// 会話バブル(<c>BubbleSupport</c>)は移植しない（クラスKDoc「Kotlin原本との差③」参照）。
    /// [2026-09-01] 途中経過の8秒ごとファイル退避・完了結果/入力のファイル保存は撤去済み
    /// （クラスKDoc冒頭参照）——このメソッドは <see cref="RunV6FullOptimizeCoreAsync"/>（前景実行）と
    /// 同型の、純粋なインメモリ処理になった。
    /// </summary>
    private async Task RunInBackgroundCoreAsync(
        MagiState st0, int[][] sched0, long runId, int budgetSec, int workers, CancellationToken ct)
    {
        var terminalLogged = false;
        void BgNote(string msg, string level = "I") => LogOp(level, $"バックグラウンド計算: {msg}");
        void Terminal(string msg, string level = "I")
        {
            if (terminalLogged) return;
            terminalLogged = true;
            BgNote(msg, level);
        }

        var wallStart = NowMs();
        var lastPublishMs = long.MinValue / 4;
        try
        {
            // [2026-09-02, 外部レビュー#43] 前景実行(MagiViewModel.Optimize.cs)と同じ理由でPostToUi経由
            //   にする——このOnProgressもエンジンの並列ワーカーがTask.Run内部から直接呼ぶため
            //   UIスレッドとは限らない（クラスKDoc「MagiViewModel.cs」のPostToUi参照）。
            void OnProgress(string phase, ViolationReport? report, long _, long __)
            {
                var wallElapsed = NowMs() - wallStart;
                if (report is null) return;
                if (wallElapsed - lastPublishMs >= OptimizationRepository.ProgressPushMs)
                {
                    lastPublishMs = wallElapsed;
                    PostToUi(() =>
                    {
                        Ui.BestHard = report.Hard;
                        Ui.BestSoft = report.Soft;
                        Ui.TotalViolations = report.Total;
                        Ui.ElapsedMs = wallElapsed;
                    });
                }
            }

            var res = await _optimizationService.OptimizeAsync(
                st0, sched0.Copy2D(), budgetSec, workers,
                // [Kotlin原本との差なし・逐語] Worker.doWork() は softPolish/requestedAlgorithm を
                //   指定せず handleOptimize の既定値（softPolish=false, AUTO）に任せている
                //   （V6FinalPort.kt:257-258）——前景の Ui.SoftPolish/Ui.V6Algorithm は使わない。
                softPolish: false, requestedAlgorithm: V6Algorithm.Auto,
                allowImpossible: true, onProgress: OnProgress, cancellationToken: ct);

            Terminal($"完了（必須{res.Report.Hard} 合計{res.Report.Total}）");
            // [クラスKDoc「Kotlin原本との差④」] Kotlin原本は publishResult(...) 経由で
            //   別途購読された applyBgResult() を呼ぶが、この移植では同じオブジェクトの
            //   メソッドとして直接 await する（結果の採否判定＝keep-best は ApplyBgResult の
            //   責務のまま分離を保つ——呼び方だけを直接呼出しへ変える）。
            await ApplyBgResult(new OptimizationRepository.BgResult(res.Schedule, res.Report, res.Phase, runId));
        }
        catch (OperationCanceledException)
        {
            Terminal("停止");
            // [Kotlin原本との差③] Android通知が無いため、ここで直接 Ui を更新する（クラスKDoc参照）。
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.Message = "バックグラウンド計算を停止しました";
            throw;
        }
        catch (Exception e)
        {
            Terminal($"失敗: {e.GetType().Name}: {e.Message}", "W");
            // [Kotlin原本との差③] Android通知が無いため、ここで直接 Ui を更新する（クラスKDoc参照）。
            Ui.MessageIsError = true;
            Ui.Running = false;
            Ui.Message = $"バックグラウンド計算に失敗しました（{e.GetType().Name}）";
        }
        finally
        {
            OptimizationRepository.SetRunning(false);
            if (!terminalLogged)
                Terminal("終了: 完了・停止・失敗のいずれも記録されませんでした（想定外の経路。Error(OOM等)や停止処理自体の失敗が疑われます）", "W");
        }
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
        //   通してしまう。runId==0 は識別子を持たない経路（この移植では実質未使用）。
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
            _bgInput = null;
            LogOp("W", "バックグラウンド計算の結果を破棄しました（計算中に設定またはデータが変わったため）");
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.Message = "計算中に設定が変わったため、結果は反映しませんでした。もう一度つくってください。";
            return;
        }
        _bgStateKey = 0L;
        _bgRunId = 0L;

        // [再実行 keep-best] 背景完了結果がこの実行の入力より悪化なら入力を維持（前景と同じ方針）。
        //   旧: 前回の結果(_resultSchedule)と比較していたため、前回結果のあとに手編集・元に戻した盤面で背景実行すると、
        //   入力より悪化していないその編集が「前回の結果を維持」の名目で巻き戻された（Kotlin 3.475.0 と同じ修正）。
        var prev = _bgInput ?? _resultSchedule;
        _bgInput = null;
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
                    ui.EngineRan = true;
                    ui.Message = $"今回(必須{newHard}/合計{newTotal})は前回(必須{prevReport.Hard}/合計{prevReport.Total})より改善せず。前回の結果を維持しました。";
                });
                LogOp("I", $"バックグラウンド: 今回 必須{newHard}/合計{newTotal} は前回 以下に改善せず → 前回を維持");
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
            ui.EngineRan = true;
            ui.Message = $"バックグラウンド最適化 完了: 必須={r.Report.Hard} 合計={r.Report.Total}";
        });
        LogOp("I", $"バックグラウンド最適化 完了 必須={r.Report.Hard} 合計={r.Report.Total}");
        _lastResultHard = r.Report.Hard;
        // 消費したらクリア（再生成時の二重適用を防ぐ）。
        OptimizationRepository.Request = null;
        OptimizationRepository.PublishResult(null);
    }
}
