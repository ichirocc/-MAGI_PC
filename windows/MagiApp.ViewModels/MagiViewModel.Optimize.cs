using System.Text.RegularExpressions;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9, Services/UseCases/DI層] <c>MagiViewModel.kt</c> の <c>runV6FullOptimize()</c>
/// （1103-1404行）と <c>runSoftPolish()</c>（1411-1504行）の移植——最適化の実行制御そのもの
/// （Kotlin原本コメントの「最適化の実行制御(<c>runV6FullOptimize</c>/<c>runSoftPolish</c>/
/// <c>stop</c>)」のうち前2つ。<c>stop()</c>（1506行）は <c>checkJob</c>/<c>fixJob</c> 相当の一部
/// （<c>findFixSuggestions</c>）が未移植のため別ピースで扱う）。
///
/// [Services/UseCases/DI層への変更点] Kotlin原本は <c>V6FinalPort.handleOptimize</c> を直接呼ぶが、
/// この移植ではユーザーが明示的に選択した Services/UseCases/DI 層に従い
/// <see cref="MagiViewModel._optimizationService"/>（<see cref="Services.IOptimizationService"/>）
/// を介して呼ぶ。エンジンの挙動・引数・戻り値は完全に同一——この層は呼出しの間接化のみを担う
/// （<see cref="Services.IOptimizationService"/> のクラスKDoc参照）。
///
/// [job フィールドの共有] Kotlin原本の <c>job</c>（クラスフィールド、102行）は
/// <c>load</c>/<c>runV6FullOptimize</c>/<c>runSoftPolish</c>/<c>stop</c> で共有される単一の
/// <c>Job?</c>。この移植でも同じ <c>private CancellationTokenSource? _job</c>
/// （<c>MagiViewModel.Persistence.cs</c> で宣言済み、partial class のため本ファイルからも直接参照可）を
/// 再利用する——複製すると <c>stop()</c>（未移植・別ピース）が正しいトークンを掴めなくなる。
///
/// [Phase 10 未移植の由来] Kotlin原本の <c>writeRunMarker("fg")</c>/<c>clearBgFiles(...)</c>
/// （<c>work/RunFiles.kt</c> 相当、プロセスkill耐性のためのファイルI/O）は計画どおり Phase 10
/// （背景実行）で Windows 向けに再実装する。このピースでは意図的に省略する
/// （<c>MagiViewModel.cs</c> クラスKDocの「背景実行の切り分け」と同じ方針）。
///
/// [_ui.update{it.copy(...)} の置き換え方針] クラスKDoc（<c>MagiViewModel.cs</c>）参照——
/// <c>Ui.X = ...;</c> の直接代入へ置き換える。
/// </summary>
public sealed partial class MagiViewModel
{
    // Kotlin原本 1021-1023行: runV6FullOptimize が「前回と同じ設定での再実行」を検知するための状態。
    private string? _lastSettingsSig;
    private long _lastResultHard = -1L;
    private string? _lastTopHardFamily;

    /// <summary>Kotlin原本 <c>hardFamilyJp</c>（1087行）の逐語移植。</summary>
    private static string HardFamilyJp(string key) => key switch
    {
        "covU" => "人員不足（必要人数）",
        "c3n" => "禁止の並び（連勤など）",
        "pref" => "希望シフト",
        "groupViol" => "担当外シフト",
        "low" => "個人の回数下限",
        "high" => "個人の回数上限",
        _ => key,
    };

    /// <summary>Kotlin原本 <c>topHardFamilyJp</c>（1097行）の逐語移植。</summary>
    private static string? TopHardFamilyJp(IReadOnlyDictionary<string, int> breakdown)
    {
        var keys = new[] { "covU", "c3n", "pref", "groupViol", "low", "high" };
        var top = keys.MaxBy(k => breakdown.GetValueOrDefault(k, 0));
        if (top is null) return null;
        return breakdown.GetValueOrDefault(top, 0) > 0 ? HardFamilyJp(top) : null;
    }

    /// <summary>[テスト可視性のための追加] 直近の <see cref="RunV6FullOptimize"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastRunOptimizeTask { get; private set; }

    /// <summary>
    /// 勤務表を最初からつくる（本最適化）。Kotlin原本 <c>runV6FullOptimize()</c> の移植。
    /// </summary>
    public void RunV6FullOptimize()
    {
        var st0 = _state;
        var sched0 = _currentSchedule;
        if (st0 is null || sched0 is null) return;
        if (RunBlockedByInFlight("勤務表の作成")) return;
        if (!EnsureValidForRun(st0, sched0)) return;
        PushUndo();
        var sig = $"{Ui.BudgetSec}|{Ui.Workers}|{Ui.V6Algorithm}|{Ui.SoftPolish}";
        var hint = sig == _lastSettingsSig && _lastResultHard > 0
            ? $"前回と同じ設定での再実行です。いちばん多い必須違反は『{_lastTopHardFamily ?? "不明"}』。編集タブでこれを1つ緩めると改善の可能性が高いです。"
            : null;
        _lastSettingsSig = sig;
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.HasResult = false;
        Ui.CopilotHint = hint;
        Ui.Alternatives = Array.Empty<string>();
        Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
        Ui.InterruptedRun = false;
        Ui.InterruptedInfo = null;
        Ui.Message = "勤務表をつくり始めました";
        LogOp("I", $"最適化 開始 (予算{Ui.BudgetSec}s, 並列{Ui.Workers}, 方式{Ui.V6Algorithm})");
        var startMs = NowMs();
        var hf63 = new Hf63Infeasibility();
        var boardToken = BeginBoardJob("勤務表づくり", engineRun: true);
        var cts = new CancellationTokenSource();
        _job = cts;
        LastRunOptimizeTask = RunV6FullOptimizeCoreAsync(st0, sched0.Copy2D(), startMs, hf63, boardToken, cts.Token);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Kotlin <c>String.substringAfter(delimiter)</c>: デリミタが無ければ全体を返す。</summary>
    private static string SubstringAfter(string s, string delimiter)
    {
        var idx = s.IndexOf(delimiter, StringComparison.Ordinal);
        return idx < 0 ? s : s[(idx + delimiter.Length)..];
    }

    private async Task RunV6FullOptimizeCoreAsync(
        MagiState st0, int[][] sched0, long startMs, Hf63Infeasibility hf63, int boardToken, CancellationToken ct)
    {
        // [3.372.0/実機ログ起因の由来をそのまま記録] 終端ログ（完了/停止/失敗）を必ず1行残す保証。
        var terminalLogged = false;
        // ---- 最適化中ログ強化用のスロットル状態（Kotlin原本のコルーチンローカル var 群） ----
        var liveHard = long.MaxValue;
        var livePhase = "";
        var lastUiPushMs = long.MinValue / 4;
        ViolationReport? lastLiveReport = null;
        var runWall0 = NowMs();
        var lastPhaseLogMs = -10_000L;
        var phaseNameLastLogMs = new Dictionary<string, long>();
        var lastHardLogMs = -10_000L;
        try
        {
            // [再実行 keep-best] 実行開始時の入力解(sched0)の違反を評価し、完了時の採用判定の基準にする。
            var baseReport = await Task.Run(() => UnifiedViolationChecker.Check(st0, sched0), ct);
            Ui.InitHard = baseReport.Hard;
            Ui.InitSoft = baseReport.Soft;

            void OnProgress(string phase, ViolationReport? rep, long _, long __)
            {
                if (rep is not null) hf63.UpdateFromBreakdown(rep.Breakdown, (int)((NowMs() - startMs) / 10L));
                var wallElapsed = NowMs() - runWall0;
                var baseName = SubstringAfter(phase, "/ ").Trim();
                if (baseName.Length == 0) baseName = phase;
                if (rep is not null) lastLiveReport = rep;
                var uiDue = (rep is not null && (long)rep.Hard < liveHard) ||
                    wallElapsed - lastUiPushMs >= OptimizationRepository.ProgressPushMs;
                if (uiDue)
                {
                    lastUiPushMs = wallElapsed;
                    var shown = lastLiveReport;
                    if (shown is not null) Ui.BestHard = shown.Hard;
                    if (shown is not null) Ui.BestSoft = shown.Soft;
                    if (shown is not null) Ui.TotalViolations = shown.Total;
                    if (shown is not null)
                    {
                        var bd = UiState.EmptyBreakdown().ToDictionary(kv => kv.Key, kv => kv.Value);
                        foreach (var kv in shown.Breakdown) bd[kv.Key] = kv.Value;
                        Ui.Breakdown = bd;
                    }
                    Ui.ElapsedMs = NowMs() - startMs;
                    Ui.LiveSchedule = V6NativeOptimizer.LiveBest ?? Ui.LiveSchedule;
                }
                // ---- 最適化中ログ強化（スロットル付き）----
                var important = baseName.Contains("最良更新") || baseName.Contains("改善");
                if (baseName != livePhase && (important || wallElapsed - lastPhaseLogMs >= 2_500))
                {
                    var nameKey = Regex.Replace(baseName, "[0-9]+", "#");
                    var hasLastForName = phaseNameLastLogMs.TryGetValue(nameKey, out var lastForName);
                    if (important || !hasLastForName || wallElapsed - lastForName >= 60_000)
                    {
                        var score = important && rep is not null
                            ? $"・必須{rep.Hard} 合計{rep.Total} 重み{(long)rep.WeightedScore}" : "";
                        LogOp("I", $"探索フェーズ: {baseName}（経過{wallElapsed / 1000}秒{score}）");
                        phaseNameLastLogMs[nameKey] = wallElapsed;
                        lastPhaseLogMs = wallElapsed;
                    }
                    livePhase = baseName;
                }
                if (rep is not null && (long)rep.Hard < liveHard)
                {
                    if (rep.Hard == 0 || wallElapsed - lastHardLogMs >= 1_500)
                    {
                        LogOp("I", $"必須違反 残り{rep.Hard}件 に改善（経過{wallElapsed / 1000}秒・合計{rep.Total}）");
                        lastHardLogMs = wallElapsed;
                    }
                    liveHard = rep.Hard;
                }
            }

            var res = await _optimizationService.OptimizeAsync(
                st0, sched0.Copy2D(), Ui.BudgetSec, Ui.Workers, Ui.SoftPolish, Ui.V6Algorithm,
                allowImpossible: true, onProgress: OnProgress, cancellationToken: ct);

            // [再実行 keep-best] 完了結果が入力より悪化なら、入力解を維持して通知する。
            var newHard = (long)res.Report.Hard; var newTotal = res.Report.Total;
            var baseHard = (long)baseReport.Hard; var baseTotal = baseReport.Total;
            // Kotlin原本 betterReport(baseReport, res.report): 真なら入力(baseReport)が結果より
            // 厳密に良い＝結果は「改善しなかった」ので入力を維持する。
            var inputBeatsResult = UnifiedViolationChecker.ReportComparer.Compare(baseReport, res.Report) < 0;
            if (inputBeatsResult)
            {
                var kept = sched0.Copy2D();
                SetPolishDiagnostics(null, 0, kept);
                _currentSchedule = kept;
                AutoSave();
                _resultSchedule = kept;
                _state = st0.WithSchedule(kept);
                await PushReportAsync(_state ?? st0, kept, baseReport, transform: ui =>
                {
                    ui.MessageIsError = false;
                    ui.Running = false;
                    ui.HasResult = true;
                    ui.Message = $"今回(必須{newHard}/合計{newTotal})は前回(必須{baseHard}/合計{baseTotal})より改善しませんでした。前回の結果を維持します。";
                }, ct: ct);
                LogOp("I", $"再実行: 今回 必須{newHard}/合計{newTotal} は前回 必須{baseHard}/合計{baseTotal} 以下に改善せず → 前回を維持");
                _lastResultHard = baseHard;
            }
            else
            {
                SetPolishDiagnostics(res.Post?.C1Plateau, res.Post?.ObservedPinBlockedAttempts ?? 0, res.Schedule, res.Post?.PinBlocks);
                _currentSchedule = res.Schedule.Copy2D();
                AutoSave();
                _resultSchedule = res.Schedule.Copy2D();
                _state = st0.WithSchedule(res.Schedule);
                await PushReportAsync(_state ?? st0, res.Schedule, res.Report, runLabel: "最適化", transform: ui =>
                {
                    ui.MessageIsError = false;
                    ui.Running = false;
                    ui.HasResult = true;
                    ui.Message = $"勤務表ができました: 必須={res.Report.Hard} 合計={res.Report.Total} ({NowMs() - startMs}ms)";
                }, ct: ct);
                _lastResultHard = newHard;
            }
            foreach (var line in (_lastC1Plateau?.LogLines() ?? Array.Empty<string>()).Take(4))
                LogOp("W", line.StartsWith("[W] ", StringComparison.Ordinal) ? line["[W] ".Length..] : line);
            _lastTopHardFamily = res.Report.Hard > 0 ? TopHardFamilyJp(res.Report.Breakdown) : null;
            LogOp(res.Report.Hard == 0 ? "I" : "W", $"最適化 完了 必須={res.Report.Hard} 合計={res.Report.Total} ({res.Phase})");
            // [3.409.17/実機ログ起因の由来をそのまま記録] 予算超過の実行は内訳が診断ログ（次の実行で消える）
            //   にしか残らず特定不能だった。超過時は TIME/エポック超過/後処理パス別 を操作ログへ写す。
            if (res.Logs.Any(l => l.Tag == "TIME" && l.Level == "W"))
            {
                var timeLog = res.Logs.FirstOrDefault(l => l.Tag == "TIME");
                if (timeLog is not null) LogOp("W", $"予算超過: {timeLog.Message}");
                var epochLog = res.Logs.FirstOrDefault(l => l.Tag == "エポック超過");
                if (epochLog is not null) LogOp("W", epochLog.Message);
                var postLog = res.Logs.LastOrDefault(l => l.Tag == "POST" && l.Message.StartsWith("後処理パス別", StringComparison.Ordinal));
                if (postLog is not null) LogOp("W", $"予算超過の内訳(後処理): {postLog.Message}");
            }
            terminalLogged = true;
            // HF63 検出: 50秒改善のない制約族＝データ上満たせない可能性が高い（業務担当者へ提示）。
            var staleKeys = hf63.InfeasibleBreakdownKeys().Where(k => res.Report.Breakdown.GetValueOrDefault(k, 0) > 0).ToList();
            if (staleKeys.Count > 0)
            {
                var names = staleKeys
                    .Select(k => Hf63Infeasibility.KeyToIndex.TryGetValue(k, out var idx) ? Hf63Infeasibility.CNames[idx] : null)
                    .Where(n => n is not null);
                LogOp("W", $"構造的に充足が難しい制約を検出: {string.Join(", ", names)}（データの見直しを推奨）");
            }
            await CaptureAlternatives(res.Alternatives);
        }
        catch (OperationCanceledException)
        {
            // [停止 keep-best] 中断時は実行中の(未採用の)途中盤面ではなく、直前に確定していた
            //   入力解(sched0)をそのまま保持し、表示の違反数も実際の盤面に合わせる。
            var kept = sched0.Copy2D();
            var keptReport = await Task.Run(() => UnifiedViolationChecker.Check(st0, kept), CancellationToken.None);
            _currentSchedule = kept;
            _resultSchedule = kept;
            _state = st0.WithSchedule(kept);
            try
            {
                await PushReportAsync(_state ?? st0, kept, keptReport, nonCancellable: true, transform: ui =>
                {
                    ui.MessageIsError = false;
                    ui.Running = false;
                    ui.HasResult = true;
                    ui.Message = $"停止しました。直前の勤務表（必須={keptReport.Hard} 合計={keptReport.Total}）を保持しています。";
                });
            }
            catch (Exception t)
            {
                Ui.Running = false;
                Ui.HasResult = true;
                Ui.MessageIsError = false;
                Ui.Message = $"停止しました。直前の勤務表（必須={keptReport.Hard} 合計={keptReport.Total}）を保持しています。";
                LogOp("W", $"停止時の診断に失敗: {t.GetType().Name}: {t.Message}");
            }
            LogOp("I", $"停止: 直前の勤務表 必須={keptReport.Hard}/合計={keptReport.Total} を保持");
            terminalLogged = true;
            throw;
        }
        catch (Exception e)
        {
            // [3.271.0/3.382.0相当の由来をそのまま記録] 失敗を操作ログにも残す。C#に Kotlin の
            //   Throwable/Error 相当の区別は無いため、この移植では確立済みの規約
            //   （RefreshCheckCoreAsync 等）に倣い単一の Exception 捕捉とする。
            LogOp("W", $"最適化 失敗: {e.GetType().Name}: {e.Message}");
            terminalLogged = true;
            Ui.Running = false;
            Ui.Message = $"勤務表をつくれませんでした（{e.GetType().Name}）。もう一度お試しください（詳しくは設定＞詳細設定＞ログ）";
            Ui.MessageIsError = true;
        }
        finally
        {
            if (Ui.LiveSchedule.Count > 0) Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
            // clearRunMarker() は Phase 10（背景実行）未移植——このピースでは対応するファイルI/O自体が無い。
            if (!terminalLogged)
                LogOp("W", "最適化 終了: 完了・停止・失敗のいずれも記録されませんでした（想定外の経路。停止処理自体の失敗が疑われます）");
            EndBoardJob(boardToken);
            if (Ui.Running) Ui.Running = false;
        }
    }

    /// <summary>[テスト可視性のための追加] 直近の <see cref="RunSoftPolish"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastRunSoftPolishTask { get; private set; }

    /// <summary>
    /// [ソフト研磨のみ] 現在の勤務表をHARDガード付きで局所研磨し、SOFT違反だけを削る。Kotlin原本
    /// <c>runSoftPolish()</c>（1411-1504行）の移植。「もう一度つくる」と違い破壊/多様化を行わないため
    /// 必須が一時的に増えることはなく、keep-best により入力より悪い結果は採用しない（HARD=0 を壊さない）。
    /// </summary>
    public void RunSoftPolish()
    {
        var st0 = _state;
        var sched0 = _currentSchedule;
        if (st0 is null || sched0 is null) return;
        if (RunBlockedByInFlight("仕上げ最適化の開始")) return;
        if (!EnsureValidForRun(st0, sched0)) return;
        PushUndo();
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.HasResult = false;
        Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
        Ui.Message = "自動で整えています…";
        LogOp("I", $"ソフト研磨 開始 (予算{Ui.BudgetSec}s)");
        var startMs = NowMs();
        var boardToken = BeginBoardJob("仕上げ最適化", engineRun: true);
        var cts = new CancellationTokenSource();
        _job = cts;
        LastRunSoftPolishTask = RunSoftPolishCoreAsync(st0, sched0.Copy2D(), startMs, boardToken, cts.Token);
    }

    private async Task RunSoftPolishCoreAsync(MagiState st0, int[][] sched0, long startMs, int boardToken, CancellationToken ct)
    {
        // [3.372.0相当の由来をそのまま記録] 終端ログ（完了/停止/失敗）を必ず1行残す保証。
        var terminalLogged = false;
        try
        {
            var baseReport = await Task.Run(() => UnifiedViolationChecker.Check(st0, sched0), ct);
            var polished = await _optimizationService.SoftPolishAsync(st0, sched0.Copy2D(), Ui.BudgetSec, ct);
            var polishedReport = await Task.Run(() => UnifiedViolationChecker.Check(st0, polished), ct);
            // softPolishOnly は退化防止済みだが、VM側でも入力以上のみ採用（保険）。keep-best は
            // hard→weightedScore→total を単一実装する ReportComparer（runV6FullOptimize と同一）。
            var worse = UnifiedViolationChecker.ReportComparer.Compare(baseReport, polishedReport) < 0;
            var finalSched = worse ? sched0.Copy2D() : polished.Copy2D();
            var finalReport = worse ? baseReport : polishedReport;
            _currentSchedule = finalSched;
            AutoSave();
            _resultSchedule = finalSched;
            _state = st0.WithSchedule(finalSched);
            var gain = baseReport.Total - finalReport.Total;
            await PushReportAsync(_state ?? st0, finalSched, finalReport, runLabel: "仕上げ最適化", transform: ui =>
            {
                ui.MessageIsError = false;
                ui.Running = false;
                ui.HasResult = true;
                ui.Message = gain > 0
                    ? $"整えました: 合計 {baseReport.Total} → {finalReport.Total}（-{gain}）必須={finalReport.Hard} ({NowMs() - startMs}ms)"
                    : $"これ以上は整いませんでした（合計={finalReport.Total} 必須={finalReport.Hard}）。残りは構造的要因の可能性。";
            }, ct: ct);
            LogOp("I", $"ソフト研磨 完了 必須={finalReport.Hard} 合計={finalReport.Total}（{(gain > 0 ? $"-{gain}" : "増減なし")}）");
            terminalLogged = true;
        }
        catch (OperationCanceledException)
        {
            // [停止 keep-best] 中断時は直前の確定盤面を保持し表示も整合させる（runV6FullOptimize と同型）。
            var kept = sched0.Copy2D();
            var keptReport = await Task.Run(() => UnifiedViolationChecker.Check(st0, kept), CancellationToken.None);
            _currentSchedule = kept;
            _resultSchedule = kept;
            _state = st0.WithSchedule(kept);
            try
            {
                await PushReportAsync(_state ?? st0, kept, keptReport, nonCancellable: true, transform: ui =>
                {
                    ui.MessageIsError = false;
                    ui.Running = false;
                    ui.HasResult = true;
                    ui.Message = $"停止しました。直前の勤務表（必須={keptReport.Hard} 合計={keptReport.Total}）を保持しています。";
                });
            }
            catch (Exception t)
            {
                Ui.Running = false;
                Ui.HasResult = true;
                Ui.MessageIsError = false;
                Ui.Message = $"停止しました。直前の勤務表（必須={keptReport.Hard} 合計={keptReport.Total}）を保持しています。";
                LogOp("W", $"停止時の診断に失敗: {t.GetType().Name}: {t.Message}");
            }
            LogOp("I", $"ソフト研磨 停止: 直前の勤務表 必須={keptReport.Hard}/合計={keptReport.Total} を保持");
            terminalLogged = true;
            throw;
        }
        catch (Exception e)
        {
            // [3.271.0/3.382.0相当の由来をそのまま記録] C#に Kotlin の Throwable/Error 相当の区別は
            //   無いため、確立済みの規約に倣い単一の Exception 捕捉とする（RunV6FullOptimizeCoreAsync 参照）。
            LogOp("W", $"ソフト研磨 失敗: {e.GetType().Name}: {e.Message}");
            terminalLogged = true;
            Ui.MessageIsError = true;
            Ui.Running = false;
            Ui.Message = $"整えられませんでした（{e.GetType().Name}）。もう一度お試しください（詳しくは設定＞詳細設定＞ログ）";
        }
        finally
        {
            if (Ui.LiveSchedule.Count > 0) Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
            // clearRunMarker() は Phase 10（背景実行）未移植——このピースでは対応するファイルI/O自体が無い。
            if (!terminalLogged)
                LogOp("W", "ソフト研磨 終了: 完了・停止・失敗のいずれも記録されませんでした（想定外の経路。停止処理自体の失敗が疑われます）");
            EndBoardJob(boardToken);
            if (Ui.Running) Ui.Running = false;
        }
    }

    /// <summary>
    /// 実行中の処理を止める。Kotlin原本 <c>stop()</c>（1506-1544行）の移植——前景ジョブ
    /// （<c>job</c>/<c>checkJob</c>/<c>fixJob</c> 相当）の停止部分のみ。Kotlin原本の
    /// <c>WorkManager.cancelUniqueWork</c>/<c>clearBgFiles</c>/<c>clearRunMarker</c> は Phase 10
    /// （背景実行）で Windows 向けのバックグラウンド実行機構自体を実装するまで対応するものが無いため、
    /// このピースでは意図的に省略する（<c>OptimizationRepository.Running</c> が true＝背景実行中の場合は
    /// それを停止する実装が無いことを警告ログへ残すに留める。前景の <c>_job</c>/<c>_checkCts</c>/
    /// <c>_fixCts</c> はすべて即座に確実にキャンセルできる）。
    /// </summary>
    public void Stop()
    {
        _job?.Cancel();
        _checkCts?.Cancel();
        _fixCts?.Cancel();

        if (OptimizationRepository.Running)
        {
            // [Phase 10未移植] Kotlin原本はここで WorkManager.cancelUniqueWork(...) を呼び、背景実行
            //   そのものを止める。対応する Windows 側の背景実行機構がまだ無いため、それができないことを
            //   利用者へ明示する（黙って「停止しました」と嘘をつかない）。
            LogOp("W", "バックグラウンド計算の停止は未対応です（Phase 10で実装予定）。前景の処理のみ停止しました。");
        }
        else if (Ui.Running || Ui.FixSearching)
        {
            // [3.284.0相当] 前景の違反チェック(_checkCts)/改善探索(_fixCts)は自身の catch(OperationCanceledException)
            //   で running/fixSearching を戻す機会があるが、ここでの即時リセットは冪等
            //   （後からジョブ側の確定メッセージが上書きする）。
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.FixSearching = false;
            Ui.Message = "停止しました";
            // [Kotlin原本の挙動をそのまま保存] "改善探索" 分岐は fixSearching を直前で false に
            // リセットした**後**の値を読むため、Kotlin原本(1526-1541行)でも実質常に到達しない
            // （`_ui.update{fixSearching=false}` の直後に `_ui.value.fixSearching` を読んでいる）。
            // HF77＝逐語移植の対象としてそのまま保存する（勝手に「直さない」）。
            var what = new List<string>();
            if (_boardJobLabel is not null) what.Add(_boardJobLabel);
            if (Ui.FixSearching) what.Add("改善探索");
            if (what.Count == 0) what.Add("違反チェック");
            LogOp("I", $"停止を押しました（対象: {string.Join("・", what)}）");
        }
    }
}
