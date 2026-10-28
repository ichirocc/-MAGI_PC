using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>代行役（AX、Kotlin <c>MagiViewModel.kt</c> の maybeRunAgent・undoAgent・agentDecisionApproved）。</summary>
public sealed partial class MagiViewModel
{
    private CancellationTokenSource? _agentCts;
    private readonly HashSet<long> _agentSeen = new();   // 代行が一度見た盤面×設定（元に戻した盤面・同じ結果で輪を回し直さない）
    private bool _agentResume;                            // 判断待ちを承認した＝次の検査のあと、作成を待たずに代行が続きを回す

    /// <summary>[テスト可視性のための追加] 直近の <see cref="MaybeRunAgent"/> が背後で走らせる Task。</summary>
    internal Task? LastAgentTask { get; private set; }

    public void SetAgentAuto(bool b)
    {
        Ui.AgentAuto = b;
        LogOp("I", $"設定変更: 作成のあと代行で仕上げる → {(b ? "ON" : "OFF")}");
    }

    /// <summary>希望・設定の判断を承認した入口から呼ぶ（再作成まで走る入口は作成の完了で回るので要らない）。</summary>
    internal void AgentDecisionApproved() => _agentResume = true;

    /// <summary>代行のカードの「元に戻す」。代行の段がまだ先頭にあるときだけ戻す（後の別の操作は戻さない）。</summary>
    public void UndoAgent()
    {
        if (Ui.AgentRecord is not { } rec) return;
        if (rec.UndoSerial != 0L && CellSheetLogic.NoticeUndoApplies(_undoStack.Last?.Value.Serial, rec.UndoSerial))
        {
            Undo();
            Ui.AgentRecord = null;
        }
        else
        {
            Ui.MessageIsError = true;
            Ui.Message = "このあとに別の操作があるため、代行の分だけは戻せません（「元に戻す」ボタンで順に戻せます）";
        }
    }

    /// <summary>
    /// 代行役（AX）: 作成の直後、必須違反が残り未見の盤面なら、<see cref="AgentLoop"/> で安全ゲート合格の勤務の変更を順に当てる。
    /// 希望・設定は変えない（残りはホームの判断のカードへ）。当てた分は「元に戻す」1 回で戻る。途中で盤面か設定が変われば当てない。
    /// 画面が Running・EngineRan・BestHard・CheckRev の変化で呼ぶ（Kotlin の LaunchedEffect と同じ）。
    /// </summary>
    public void MaybeRunAgent()
    {
        var u = Ui;
        if (u.Running || u.AgentRunning || OptimizeInFlight()) return;
        var resume = _agentResume; _agentResume = false;
        if (!u.AgentAuto || !(u.EngineRan || resume) || !u.HasResult || u.BestHard <= 0L) return;
        if (_state is not { } st || _currentSchedule is not { } sched) return;
        var key0 = BoardKey(sched);
        var stKey = StateKey(st);
        if (!_agentSeen.Add(key0 * 31 + stKey)) return;
        CancelFixSearch();
        Ui.AgentRunning = true;
        Ui.AgentRecord = null;
        LogOp("I", $"代行: 開始（必須 {u.BestHard}）");
        _agentCts?.Cancel();
        var cts = new CancellationTokenSource();
        _agentCts = cts;
        LastAgentTask = RunAgentCoreAsync(st, sched.Copy2D(), key0, stKey, cts.Token);
    }

    private async Task RunAgentCoreAsync(MagiState st, int[][] snap, long key0, long stKey, CancellationToken ct)
    {
        try
        {
            var r = await Task.Run(() => AgentLoop.Run(st, snap, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + AgentText.BudgetMs,
                active: () => !ct.IsCancellationRequested,
                widen: (s0, b, leftMs) => V6NativeOptimizer.SoftPolishOnly(s0, b, (int)Math.Clamp(leftMs / 1000L, 1L, AgentText.WidenMaxSec),
                    shouldStop: () => ct.IsCancellationRequested)), CancellationToken.None);
            if (ct.IsCancellationRequested) return;
            if (_currentSchedule is not { } curSched || _state is not { } curSt || BoardKey(curSched) != key0 || StateKey(curSt) != stKey || OptimizeInFlight())
            {
                LogOp("W", "代行: 途中で勤務表か設定が変わったため当てませんでした");
                return;
            }
            string SoftText(IReadOnlyDictionary<string, int> b) => AnalysisTriage.UnitTotals(b, MirrorKeys.Soft) ?? "なし";
            var rec = new AgentRecord(r.Steps.Select(s => FixOutcomeText.FixDisplayLabel(s.Label)).ToList(), r.Before.Hard, r.After.Hard,
                SoftText(r.Before.Breakdown), SoftText(r.After.Breakdown), AgentText.StopWord(r.StopReason));
            foreach (var s in r.Steps)
                LogOp("I", $"代行: {FixOutcomeText.FixDisplayLabel(s.Label)}（必須 {s.HardBefore}→{s.HardAfter}・合計 {s.TotalBefore}→{s.TotalAfter}）");
            LogOp("I", $"代行: 終了 {r.Steps.Count}手（{rec.Stop}）必須 {r.Before.Hard}→{r.After.Hard}");
            if (r.Steps.Count == 0) { Ui.AgentRecord = rec; return; }
            PushUndo();
            var recU = rec with { UndoSerial = _undoStack.Last?.Value.Serial ?? 0L };
            _currentSchedule = r.Schedule;
            _state = st.WithSchedule(r.Schedule);
            _agentSeen.Add(BoardKey(r.Schedule) * 31 + stKey);
            AutoSave();
            Ui.Schedule = r.Schedule.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
            Ui.AgentRecord = recU;
            ClearFixState();
            if (AgentText.Headline(recU) is { } h) PostOpNotice(h);
            RefreshCheck();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            LogOp("W", $"代行に失敗: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            Ui.AgentRunning = false;
        }
    }
}
