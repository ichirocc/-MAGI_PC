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
/// [2026-09-01] Kotlin原本の <c>writeRunMarker("fg")</c>/<c>clearBgFiles(...)</c>/<c>clearRunMarker()</c>
/// （プロセスkill耐性のためのファイルI/O）は Phase 10 で一時移植したが、ユーザー明示判断
/// 「クラッシュからの復旧はそこまで重視しない」により全撤去した（経緯は
/// <c>windows/README.md</c> フェーズ10節・<c>MagiViewModel.Background.cs</c> クラスKDoc参照）。
///
/// [_ui.update{it.copy(...)} の置き換え方針] クラスKDoc（<c>MagiViewModel.cs</c>）参照——
/// <c>Ui.X = ...;</c> の直接代入へ置き換える。
/// </summary>
public sealed partial class MagiViewModel
{
    // Kotlin原本 1021-1023行: runV6FullOptimize が「前回と同じ設定での再実行」を検知するための状態。
    //   [テスト可視性のためinternal化] 読込・背景結果での更新／リセットを直接確かめる。
    internal string? _lastSettingsSig;
    internal long _lastResultHard = -1L;
    /// <summary>進捗の基準（InitHard/InitSoft）を取った入力の指紋。読込で null に戻す。</summary>
    internal long? _initBaseKey;
    /// <summary>盤面を丸ごと差し替えた回数（CSV 取込・下書き・他の案）。進捗の基準の指紋に混ぜる＝手の編集では上げない。</summary>
    internal long _boardReplaceRev;
    internal string? _lastTopHardFamily;

    /// <summary>Kotlin原本 <c>hardFamilyJp</c>（1087行）の逐語移植。</summary>
    private static string HardFamilyJp(string key) => key switch
    {
        "covU" => "人員不足（必要人数）",
        "c3n" => "禁止の並び（連勤など）",
        "c3w" => "希望の前日の禁止",
        "pref" => "希望と違う勤務",
        "extWish" => "拡張希望",
        "groupViol" => "担当外シフト",
        _ => key,
    };

    /// <summary>いちばん多い必須違反の族を「『族名』（N件）」で返す。数えるのは必須の族だけ（下限割れ・上限超過は要調整＝「必須違反」と呼ばない）。Kotlin原本 <c>topHardFamilyJp</c>。</summary>
    private static string? TopHardFamilyJp(IReadOnlyDictionary<string, int> breakdown)
    {
        var keys = new[] { "covU", "c3n", "c3w", "pref", "extWish", "groupViol" };
        var top = keys.MaxBy(k => breakdown.GetValueOrDefault(k, 0));
        if (top is null) return null;
        var n = breakdown.GetValueOrDefault(top, 0);
        return n > 0 ? $"{HardFamilyJp(top)}（{n}{AnalysisTriage.UnitOf(top)}）" : null;
    }

    /// <summary>[テスト可視性のための追加] 直近の <see cref="RunV6FullOptimize"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastRunOptimizeTask { get; private set; }

    /// <summary>勤務表を最初からつくる（Kotlin <c>runV6FullOptimize()</c>）。何度つくっても残る／再作成すると外れる項目があれば <see cref="UiState.PreRunCheck"/> を出して止まる（つくる前の確認）。</summary>
    public void RunV6FullOptimize()
    {
        var st = _state; var sched = _currentSchedule;
        if (st is not null && sched is not null && !Ui.Running && CellSheetLogic.PreRunAckFingerprint(st, PreRunCheck.RerunClears(st, sched)) != _preRunAckKey)
        {
            // [Android 3.672.0] 作成の前に「勤務表を作成する」シート（自動で行うこと・保持するもの・時間と、残る／外れる項目）を出して止まる。
            //   同じデータ（指紋）で一度「勤務表を作成」を押したら、次からは出さない（直す項目が無いときも出す）。
            var sum = PreRunCheck.Build(st, sched);
            Ui.PreRunRepeatHint = RepeatHint();
            Ui.PreRunCheck = sum;
            LogOp("I", $"勤務表を作成する 表示 (消えない{sum.FloorCount}件, 外れる{sum.RerunClears.Count}件)");
            LogUi(new OpLogText.UiOp.PreRunOpen());
            return;
        }
        StartFullOptimize(pushUndo: true, null);
    }

    /// <summary>「勤務表を作成」。同じ指紋のあいだは同じ理由で止めない。</summary>
    public void ProceedPreRun()
    {
        var st = _state; var sched = _currentSchedule;
        if (st is null || sched is null) return;
        _preRunAckKey = CellSheetLogic.PreRunAckFingerprint(st, PreRunCheck.RerunClears(st, sched));
        DismissPreRun();
        LogOp("I", "勤務表を作成する 勤務表を作成");
        StartFullOptimize(pushUndo: true, null, hintShown: true);
    }

    /// <summary><paramref name="how"/> は閉じ方（閉じる／条件を確認する）。開いていなければ何も書かない。</summary>
    public void DismissPreRun(OpLogText.UiOp? how = null)
    {
        if (Ui.PreRunCheck is not null) LogUi(how ?? new OpLogText.UiOp.PreRunClose());
        Ui.PreRunCheck = null;
        Ui.PreRunRepeatHint = null;
    }

    /// <summary>編集タブの「元の確認へ戻る」: 勤務表を作成するシートを今のデータで作り直して出す（実行はしない）。残る項目が無くても出す（3.704.0）。Kotlin <c>reopenPreRun</c>。</summary>
    public void ReopenPreRun()
    {
        var st = _state; var sched = _currentSchedule;
        if (st is null || sched is null) return;
        var sum = PreRunCheck.Build(st, sched);
        Ui.PreRunRepeatHint = RepeatHint();
        Ui.PreRunCheck = sum;
        LogUi(new OpLogText.UiOp.PreRunOpen());
    }

    private long? _preRunAckKey;

    private string RunSig() => $"{Ui.BudgetSec}|{Ui.Workers}|{Ui.V6Algorithm}|{Ui.SoftPolish}";

    private string? RepeatHint() => RunSig() == _lastSettingsSig && _lastResultHard > 0 ? CellSheetLogic.PreRunRepeatHintText(_lastTopHardFamily) : null;

    /// <summary>本実行。<paramref name="pushUndo"/>＝false は Undo を積まない（S5 の確定がすでに積んでいる）。</summary>
    private void StartFullOptimize(bool pushUndo, S5Ctx? s5, bool hintShown = false)
    {
        var st0 = _state;
        var sched0 = _currentSchedule;
        if (st0 is null || sched0 is null) return;
        if (RunBlockedByInFlight("勤務表の作成")) return;
        if (!EnsureValidForRun(st0, sched0, "勤務表の作成")) return;
        if (pushUndo) PushUndo();
        var sig = RunSig();
        var hint = s5 is null && !hintShown ? RepeatHint() : null;
        _lastSettingsSig = sig;
        // 停止・失敗で入力の盤面へ戻すときは、実行前の旗へ戻す（一度も計算していない盤面を「計算済み」にしない）。
        var hadResult = Ui.HasResult;
        var engineRanBefore = Ui.EngineRan;
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.KeptPrevious = false;
        Ui.HasResult = false;
        Ui.CopilotHint = hint;
        Ui.WishCancelOutcome = null;
        Ui.RunSummary = null;
        Ui.StopSummary = null;
        Ui.Alternatives = Array.Empty<string>();
        Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
        ClearFixState(); // [Android 3.612.0] 前の盤面の1手の候補を残さない
        Ui.Message = "勤務表をつくり始めました";
        var startMs = NowMs();
        var hf63 = new Hf63Infeasibility();
        var boardToken = BeginBoardJob("勤務表づくり", engineRun: true);
        // [3.694.0] 開始行は BeginBoardJob の後（#N が付く）。期間・人数・指紋は実行前の入力、設定は開始点で 1 回読む（停止・失敗の実行にも残る）。
        LogOp("I", $"最適化 開始 (予算{Ui.BudgetSec}s, 並列{Ui.Workers}, 方式{Ui.V6Algorithm}) ・ {RunInputWords(st0, sched0)}");
        LogOp("I", "設定の開始時: " + SettingsAtStart());
        var cts = new CancellationTokenSource();
        _job = cts;
        LastRunOptimizeTask = RunV6FullOptimizeCoreAsync(st0, sched0.Copy2D(), startMs, hf63, boardToken, s5, hadResult, engineRanBefore, cts.Token);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>[3.694.0] 開始行の共通部「期間=… ・ 職員=N名 ・ 状態指紋=… ・ 盤面指紋=…」（前景・背景・下書きが同じ並び）。</summary>
    private static string RunInputWords(MagiState st, int[][] sched) =>
        $"期間={OpLogText.Period(st.StartDate, st.EndDate, st.DayCount)} ・ 職員={st.StaffCount}名 ・ {OpLogText.Keys(StateKey(st), BoardKey(sched))}";

    /// <summary>[3.694.0] 「設定の開始時」の本文。「（設定）」は利用者の選択＝ネイティブ層の無いこの移植では「設定の効き」が常に OFF を示す。
    /// LnsAdaptive は UI トグルが無く <c>PostOptimizationParams</c> の既定 true（「設定の効き」と同じ）。</summary>
    private string SettingsAtStart() => TuningTelemetry.StartLine(
        nativeOn: Ui.NativeAccel, parityOn: Ui.NativeParity, filterC3nIncrease: PolishGate.FilterC3nIncrease,
        wideC3nBreakDays: PolishGate.WideC3nBreakDays, softPolishOn: Ui.SoftPolish, combineExhaustPairs: PolishGate.CombineExhaustPairs,
        lnsAdaptive: true, aptFairSoftTolerance: PolishGate.AptFairSoftTolerance, countChainPolish: PolishGate.CountChainPolish);

    /// <summary>Kotlin <c>String.substringAfter(delimiter)</c>: デリミタが無ければ全体を返す。</summary>
    private static string SubstringAfter(string s, string delimiter)
    {
        var idx = s.IndexOf(delimiter, StringComparison.Ordinal);
        return idx < 0 ? s : s[(idx + delimiter.Length)..];
    }

    private async Task RunV6FullOptimizeCoreAsync(
        MagiState st0, int[][] sched0, long startMs, Hf63Infeasibility hf63, int boardToken, S5Ctx? s5,
        bool hadResult, bool engineRanBefore, CancellationToken ct)
    {
        // [3.372.0/実機ログ起因の由来をそのまま記録] 終端ログ（完了/停止/失敗）を必ず1行残す保証。
        var terminalLogged = false;
        var s5Suffix = s5 is not null ? "（希望は外したままです。元に戻すで希望も戻ります）" : "";
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
            // 同じデータ（設定・希望の指紋が同じ）の再作成では基準を据え置く＝でき具合の％が再作成のたびに 0 へ戻らない。
            var baseKey = CellSheetLogic.InitBaseKeyOf(StateKey(st0), _boardReplaceRev);
            if (baseKey != _initBaseKey)
            {
                _initBaseKey = baseKey;
                Ui.InitHard = baseReport.Hard;
                Ui.InitSoft = baseReport.Soft;
                Ui.InitBreakdown = baseReport.Breakdown;
            }

            // [2026-09-02, 外部レビュー#43] エンジンの並列ワーカーがTask.Run内部から直接呼ぶため、
            //   UIスレッドとは限らない。Ui.*への書き込み・LogOp呼出しをまとめてPostToUiへ渡す
            //   （クラスKDoc「MagiViewModel.cs」のPostToUi参照。ローカル変数(liveHard等)の読み書き自体は
            //   このコールバックの外からは触られないため単一スレッドの逐次実行のまま=競合なし、
            //   UIへの反映だけを投げ返す）。
            void OnProgress(string phase, ViolationReport? rep, long _, long __) => PostToUi(() =>
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
            });

            var res = await _optimizationService.OptimizeWithFlagsAsync(
                st0, sched0.Copy2D(), Ui.BudgetSec, Ui.Workers, Ui.SoftPolish, Ui.V6Algorithm,
                allowImpossible: true,
                extraRefineRequirePostHardDrop: false,
                wishFloorMode: PolishGate.WishConflictFloorMode,
                onProgress: OnProgress, cancellationToken: ct);

            // [再実行 keep-best] 完了結果が入力より悪化なら、入力解を維持して通知する。
            var newHard = (long)res.Report.Hard; var newTotal = res.Report.Total;
            var baseHard = (long)baseReport.Hard; var baseTotal = baseReport.Total;
            // Kotlin原本 betterReport(baseReport, res.report): 真なら入力(baseReport)が結果より
            // 厳密に良い＝結果は「改善しなかった」ので入力を維持する。
            // 比べる基準はエンジンの番兵と同じ「個人上限0のセルを外した入力」（ユーザー決定 2026-10-06）。生の入力と比べると、
            //   上限0のセルが人員を満たしていた盤面では最適化の結果が毎回「悪化」に見えて前回へ戻っていた（実機 #9）。
            var (clearedInput, clearedCount) = V6NativeOptimizer.ClearCappedCells(st0, sched0);
            var cmpReport = clearedCount > 0 ? UnifiedViolationChecker.Check(st0, clearedInput) : baseReport;
            var inputBeatsResult = UnifiedViolationChecker.ReportComparer.Compare(cmpReport, res.Report) < 0;
            if (inputBeatsResult)
            {
                var kept = sched0.Copy2D();
                SetPolishDiagnostics(null, 0, kept);
                _rejectedRunDiagLogs = res.Logs.Select(l => $"[{l.Level}] {l.Tag}: {l.Message}").ToList();
                _rejectedRunDiagSerial = _activeRunSerial;
                _rejectedRunDiagAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _currentSchedule = kept;
                AutoSave();
                _resultSchedule = kept;
                _state = st0.WithSchedule(kept);
                // [S5 §9] 維持の分岐は「前回の結果を維持します」だと希望が消えたことが伝わらない＝置き換える。
                var nowScore = new KeptResultText.Score(newHard, res.Report.WeightedScore, newTotal);
                var baseScore = new KeptResultText.Score(baseHard, baseReport.WeightedScore, baseTotal);
                var capNote = res.CapZero?.KeptNote();
                var keptMsg = s5 is null
                    ? KeptResultText.WithNote(KeptResultText.Screen(res.Report.Breakdown, baseReport.Breakdown), capNote)
                    : s5.H0 - baseReport.Hard > 0
                        ? $"希望（{s5.Label}）を調整しました（希望は外れています）。必須違反は {s5.H0}件 → {AnalysisTriage.HardText(baseReport.Breakdown, baseReport.Hard)}（外した分だけ）。再作成しても、それ以上は減りませんでした。元に戻すで希望と勤務表をまとめて戻せます。"
                        : $"希望（{s5.Label}）を調整しましたが（希望は外れています）、再作成しても必須違反は減りませんでした（必須違反 {AnalysisTriage.HardText(baseReport.Breakdown, baseReport.Hard)}）。元に戻すで希望と勤務表をまとめて戻せます。";
                await PushReportAsync(_state ?? st0, kept, baseReport, transform: ui =>
                {
                    ui.MessageIsError = false;
                    ui.Running = false;
                    ui.HasResult = true;
                    ui.EngineRan = true;
                    ui.RanBeforeEdit = false;
                    ui.RelaxedBoard = false;
                    ui.Message = keptMsg;
                    ui.KeptPrevious = s5 is null;
                    ui.WishCancelOutcome = s5 is null ? null : new WishCancelOutcome(s5.Name, s5.Day, s5.Symbol, s5.H0, s5.PCancel, baseReport.Hard, keptMsg);
                }, ct: ct);
                LogOp("I", KeptResultText.WithNote(KeptResultText.Log("再実行", nowScore, baseScore), capNote));
                _lastResultHard = baseHard;
            }
            else
            {
                SetPolishDiagnostics(res.Post?.C1Plateau, res.Post?.ObservedPinBlockedAttempts ?? 0, res.Schedule, res.Post?.PinBlocks);
                _rejectedRunDiagLogs = Array.Empty<string>();
                _currentSchedule = res.Schedule.Copy2D();
                _resultAdoptedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                AutoSave();
                _resultSchedule = res.Schedule.Copy2D();
                _state = st0.WithSchedule(res.Schedule);
                var adoptedMsg = s5 is null
                    ? CompletedText.Of(res.Report.Hard, res.Report.Breakdown, "") +
                      (CompletedText.AdoptedDespiteMore(baseReport.Total, res.Report.Total, baseReport.WeightedScore, res.Report.WeightedScore) is { } why ? $"。{why}" : "")
                    : $"希望（{s5.Label}）を調整して（希望は外しました）、再作成しました: 必須違反 {s5.H0}件 → {AnalysisTriage.HardText(res.Report.Breakdown, res.Report.Hard)}（試算の見込み {s5.PCancel}件）" +
                      (res.Report.Hard > s5.PCancel ? "。見込みまでは減りませんでした。再作成するか、元に戻す（希望と勤務表をまとめて戻す）を選べます。" : "");
                await PushReportAsync(_state ?? st0, res.Schedule, res.Report, runLabel: "最適化", transform: ui =>
                {
                    ui.MessageIsError = false;
                    ui.Running = false;
                    ui.HasResult = true;
                    ui.EngineRan = true;
                    ui.RanBeforeEdit = false;
                    ui.RelaxedBoard = false;
                    ui.KeptPrevious = false;
                    ui.Message = adoptedMsg;
                    ui.WishCancelOutcome = s5 is null ? null : new WishCancelOutcome(s5.Name, s5.Day, s5.Symbol, s5.H0, s5.PCancel, res.Report.Hard, adoptedMsg);
                    ui.RunSummary = ChangeSummary.Of(st0, sched0, res.Schedule, res.Report, baseReport);
                    ui.StopSummary = res.Stop;
                }, ct: ct);
                _lastResultHard = newHard;
            }
            foreach (var line in (_lastC1Plateau?.LogLines() ?? Array.Empty<string>()).Take(4))
                LogOp("W", line.StartsWith("[W] ", StringComparison.Ordinal) ? line["[W] ".Length..] : line);
            // [Android 3.475.0 同期] 採用した盤面の族を覚える（旧 C#: 入力維持の分岐でも捨てた盤面 res.Report から取っていた）。
            var adoptedReport = inputBeatsResult ? baseReport : res.Report;
            _lastTopHardFamily = adoptedReport.Hard > 0 ? TopHardFamilyJp(adoptedReport.Breakdown) : null;
            // [3.694.0] 盤面指紋は採用後の盤面（維持なら入力・採用なら結果＝adoptedReport と同じ盤面）。開始行の指紋と並べて入力の同一性を読む。
            LogOp(res.Report.Hard == 0 ? "I" : "W", $"最適化 完了 必須={res.Report.Hard} 合計={res.Report.Total} ({res.Phase}) ・ 盤面指紋={(_currentSchedule is { } cs ? OpLogText.Hex16(BoardKey(cs)) : OpLogText.Hex16(0L))}");
            NoteRunResult(adoptedReport.Hard, kept: inputBeatsResult);
            if (s5 is not null)
            {
                _cancelOutcomeCtx = _state is { } cst && _currentSchedule is { } csch ? new TrialCtx(cst, BoardKey(csch)) : null;
                LogOp("I", $"S5 結果: {s5.H0}/{s5.Hx}/{s5.Rk}/{s5.Rr}/{s5.PCancel} → {adoptedReport.Hard}");
            }
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
            // [Android 3.612.0 思考誘導S4] 画面の下限判定は採用した盤面で見る（入力維持の分岐では捨てた盤面の族を出さない）。
            Ui.StalledHardFamilies = hf63.InfeasibleBreakdownKeys()
                .Where(k => MirrorKeys.Hard.Contains(k) && adoptedReport.Breakdown.GetValueOrDefault(k, 0) > 0).ToList();
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
                    ui.HasResult = s5 is not null || hadResult;
                    ui.EngineRan = engineRanBefore;
                    ui.RelaxedBoard = false;
                    ui.Message = $"停止しました。直前の勤務表（{AnalysisTriage.ReportCountsText(keptReport.Breakdown)}）を保持しています。{s5Suffix}";
                });
            }
            catch (Exception t)
            {
                Ui.Running = false;
                Ui.HasResult = s5 is not null || hadResult;
                Ui.EngineRan = engineRanBefore;
                Ui.RelaxedBoard = false;
                Ui.MessageIsError = false;
                Ui.Wishes = st0.Wishes;
                Ui.Message = $"停止しました。直前の勤務表（{AnalysisTriage.ReportCountsText(keptReport.Breakdown)}）を保持しています。{s5Suffix}";
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
            var late = !ReferenceEquals(_state, st0) ? _state : null;   // 非 null＝この実行が state を差し替えた後の失敗
            LogOp("W", $"最適化 失敗{(late is not null ? "（結果の採用後）" : "")}: {e.GetType().Name}: {e.Message}");
            terminalLogged = true;
            var failMsg = $"勤務表をつくれませんでした（{FailureWords.Of(e, FailureKind.Engine)}）。もう一度お試しください（詳しくは設定＞詳細設定＞ログ）";
            if (late is { } curSt && _currentSchedule is { } curSched)
            {
                // [S5 §10] 維持・採用の書き込みの後で投げた＝VM と自動保存は今回の結果。捨てずに今の (state, 盤面) で描き直す。
                var lateMsg = $"勤務表の作成は終わりましたが、最後の処理でエラーが起きました（{e.GetType().Name}）。表示は今の勤務表です。必要なら再作成してください（詳しくは設定＞詳細設定＞ログ）{s5Suffix}";
                var curB = curSched.Copy2D();
                try
                {
                    var rep = await Task.Run(() => UnifiedViolationChecker.Check(curSt, curB), CancellationToken.None);
                    await PushReportAsync(curSt, curB, rep, nonCancellable: true, transform: ui =>
                    {
                        ui.Running = false;
                        ui.HasResult = true;
                        ui.MessageIsError = true;
                        ui.Message = lateMsg;
                    });
                }
                catch (Exception)
                {
                    // 診断がまた落ちても盤面だけは今のものを出す（Kotlin と同じ）。
                    Ui.Running = false;
                    Ui.HasResult = true;
                    Ui.Wishes = curSt.Wishes;
                    Ui.Schedule = curB.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
                    Ui.Message = lateMsg;
                    Ui.MessageIsError = true;
                }
            }
            else
            {
                // [S5 §10] 希望は消えたまま＝画面もその state で数え直す（RefreshCheck は失敗文を上書きするので使わない）。
                //   通常の実行も同じ＝進捗が書いた途中の件数を、入力の盤面の件数へ戻す。
                try
                {
                    var rep = await Task.Run(() => UnifiedViolationChecker.Check(st0, sched0), CancellationToken.None);
                    await PushReportAsync(st0, sched0, rep, nonCancellable: true, transform: ui =>
                    {
                        ui.Running = false;
                        ui.HasResult = s5 is not null || hadResult;
                        ui.MessageIsError = true;
                        ui.Message = failMsg + s5Suffix;
                    });
                }
                catch (Exception)
                {
                    Ui.Running = false;
                    Ui.Wishes = st0.Wishes;
                    Ui.Message = failMsg + s5Suffix;
                    Ui.MessageIsError = true;
                }
            }
        }
        finally
        {
            if (Ui.LiveSchedule.Count > 0) Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
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
    /// <c>runSoftPolish()</c>（1411-1504行）の移植。「再作成」と違い破壊/多様化を行わないため
    /// 必須が一時的に増えることはなく、keep-best により入力より悪い結果は採用しない（HARD=0 を壊さない）。
    /// </summary>
    public void RunSoftPolish()
    {
        var st0 = _state;
        var sched0 = _currentSchedule;
        if (st0 is null || sched0 is null) return;
        if (RunBlockedByInFlight("仕上げ最適化の開始")) return;
        if (!EnsureValidForRun(st0, sched0, "仕上げ最適化")) return;
        PushUndo();
        var engineRanBefore = Ui.EngineRan;   // 停止で入力へ戻すとき用（StartFullOptimize と同じ）
        var hadResult = Ui.HasResult;
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.KeptPrevious = false;
        Ui.HasResult = false;
        Ui.RunSummary = null;
        Ui.StopSummary = null;
        Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
        ClearFixState();
        Ui.Message = "自動で整えています…";
        LogOp("I", $"ソフト研磨 開始 (予算{Ui.BudgetSec}s)");
        var startMs = NowMs();
        var boardToken = BeginBoardJob("仕上げ最適化", engineRun: true);
        var cts = new CancellationTokenSource();
        _job = cts;
        LastRunSoftPolishTask = RunSoftPolishCoreAsync(st0, sched0.Copy2D(), startMs, boardToken, engineRanBefore, hadResult, cts.Token);
    }

    private async Task RunSoftPolishCoreAsync(MagiState st0, int[][] sched0, long startMs, int boardToken, bool engineRanBefore, bool hadResult, CancellationToken ct)
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
                ui.EngineRan = true;
                ui.RanBeforeEdit = false;
                ui.RelaxedBoard = false;
                ui.KeptPrevious = false;
                ui.Message = FixOutcomeText.PolishResultText(gain > 0, AnalysisTriage.UnitTotals(baseReport.Breakdown, MirrorKeys.Soft) ?? "なし",
                    AnalysisTriage.UnitTotals(finalReport.Breakdown, MirrorKeys.Soft) ?? "なし", finalReport.Hard, AnalysisTriage.HardText(finalReport.Breakdown, finalReport.Hard));
                ui.RunSummary = ChangeSummary.Of(st0, sched0, finalSched, finalReport, baseReport);
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
                    ui.EngineRan = engineRanBefore;
                    ui.RelaxedBoard = false;
                    ui.Message = $"停止しました。直前の勤務表（必須={keptReport.Hard} 合計={keptReport.Total}）を保持しています。";
                });
            }
            catch (Exception t)
            {
                Ui.Running = false;
                Ui.HasResult = true;
                Ui.EngineRan = engineRanBefore;
                Ui.RelaxedBoard = false;
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
            Ui.HasResult = hadResult;
            Ui.Message = $"整えられませんでした（{FailureWords.Of(e, FailureKind.Engine)}）。もう一度お試しください（詳しくは設定＞詳細設定＞ログ）";
        }
        finally
        {
            if (Ui.LiveSchedule.Count > 0) Ui.LiveSchedule = Array.Empty<IReadOnlyList<int>>();
            if (!terminalLogged)
                LogOp("W", "ソフト研磨 終了: 完了・停止・失敗のいずれも記録されませんでした（想定外の経路。停止処理自体の失敗が疑われます）");
            EndBoardJob(boardToken);
            if (Ui.Running) Ui.Running = false;
        }
    }

    /// <summary>
    /// 実行中の処理を止める。Kotlin原本 <c>stop()</c>（1506-1544行）の移植——前景ジョブ
    /// （<c>job</c>/<c>checkJob</c>/<c>fixJob</c> 相当）の停止部分のみ。
    ///
    /// [Phase 10 本体で更新] 当初このメソッドは「Windows向けの背景実行機構が無い」ことを理由に
    /// <c>OptimizationRepository.Running</c> のときは警告ログのみに留めていたが、
    /// <c>MagiViewModel.Background.cs</c> で実際にバックグラウンド <c>Task</c>（<see cref="_bgCts"/>）
    /// を実装したため、ここも実際に Cancel する。Kotlin原本の <c>WorkManager.cancelUniqueWork</c> とは
    /// 機構が異なるが「背景計算を止める」という意図は忠実に果たす——止まった後の後片付け
    /// （<c>OptimizationRepository.SetRunning(false)</c>）は
    /// <c>RunInBackgroundCoreAsync</c> 自身の <c>catch (OperationCanceledException)</c>/<c>finally</c>
    /// が担う（Kotlin原本で Worker 自身が同じ経路を担うのと同じ構造）。
    /// </summary>
    public void Stop()
    {
        CancelWishTrial();
        CancelRelaxTrial();
        _job?.Cancel();
        _checkCts?.Cancel();
        _fixCts?.Cancel();
        _agentCts?.Cancel();
        _bgCts?.Cancel();

        if (Ui.Running || Ui.FixSearching)
        {
            // [3.284.0相当] 前景の違反チェック(_checkCts)/改善探索(_fixCts)は自身の catch(OperationCanceledException)
            //   で running/fixSearching を戻す機会があるが、ここでの即時リセットは冪等
            //   （後からジョブ側の確定メッセージが上書きする）。
            var wasFixSearching = Ui.FixSearching;   // [Android 3.509.1 同期] リセット前に取る
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
            // [Phase 10 本体で追加] 背景実行には _boardJobLabel が無い（クラスKDoc
            //   「MagiViewModel.Background.cs」の WireBackgroundSubscriptions 節参照）ため、
            //   これが無いと背景最適化の停止が誤って既定の「違反チェック」に分類されていた。
            if (OptimizationRepository.Running) what.Add("バックグラウンド最適化");
            if (wasFixSearching) what.Add("改善探索");
            if (what.Count == 0) what.Add("違反チェック");
            LogOp("I", $"停止を押しました（対象: {string.Join("・", what)}）");
        }
    }
}
