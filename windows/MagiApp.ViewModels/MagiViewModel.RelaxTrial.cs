using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[S6] 確定の照合に使う試算時の文脈（§6 の 1）。Kotlin <c>RelaxToken</c>。</summary>
public sealed record RelaxToken(long StateKey, long BoardKey, RelaxTrial.Result Result);

/// <summary>[S6] 「設定を緩めたら」試算と確定（Kotlin <c>MagiViewModel.kt</c> の S6 節、<c>docs/s6_relax_trial.md</c> §6・§8）。</summary>
public sealed partial class MagiViewModel
{
    /// <summary>鮮度はデータ（盤面・希望・制約・データに保存される設定）の指紋で見る。実行設定は含まない（S5 と同じ規則）。</summary>
    private sealed record RelaxCtx(long StateKey, long BoardKey);

    private CancellationTokenSource? _relaxCts;
    private long _relaxSeq;
    private RelaxCtx? _relaxCtx;
    private RelaxTrial.Outcome? _relaxResult;
    private (RelaxCtx Ctx, string Line)? _relaxDone;
    private RelaxCtx? _relaxStoppedCtx;
    private RelaxCtx? _relaxFailedCtx;

    /// <summary>[テスト可視性のための追加] 直近の <see cref="StartRelaxTrial"/> が背後で走らせる Task。</summary>
    internal Task? LastRelaxTrialTask { get; private set; }

    private RelaxCtx? RelaxCtxNow() =>
        _state is { } st && _currentSchedule is { } b ? new RelaxCtx(StateKey(st), BoardKey(b)) : null;

    /// <summary>[S6] 個人の上限0は例外でも緩めない（Android 3.620.0、2026-10-05 ユーザー決定）。false の間は自動でも再試行でも試算を始めない。</summary>
    internal const bool RelaxTrialEnabled = false;

    /// <summary>背景で起点 3 件まで試算する（§8）。同じ ctx で済んでいる・走っているなら何もしない。</summary>
    internal void StartRelaxTrial()
    {
        var st = _state;
        var b = _currentSchedule;
        if (st is null || b is null) return;
        if (OptimizeInFlight()) return;
        var ctx = new RelaxCtx(StateKey(st), BoardKey(b));
        if (_relaxCtx == ctx && (_relaxResult is not null || LastRelaxTrialTask is { IsCompleted: false })) return;
        _relaxCts?.Cancel();
        var seq = ++_relaxSeq;
        _relaxCtx = ctx;
        _relaxResult = null;
        _relaxStoppedCtx = null;
        _relaxFailedCtx = null;
        var cts = new CancellationTokenSource();
        _relaxCts = cts;
        Ui.RelaxSearching = true;
        LastRelaxTrialTask = RelaxTrialCoreAsync(st, b.Copy2D(), seq, cts.Token, ctx);
    }

    private async Task RelaxTrialCoreAsync(MagiState st, int[][] board, long seq, CancellationToken ct, RelaxCtx ctx)
    {
        try
        {
            var o = await Task.Run(() => RelaxTrial.FirstWall(st, board, shouldStop: () => ct.IsCancellationRequested));
            if (seq != _relaxSeq) return;
            if (o is not RelaxTrial.StoppedOutcome) _relaxResult = o;
            if (o is RelaxTrial.Result r) LogOp("I", $"S6 試算: 起点 {r.Staff + 1}/{r.Day + 1} 組{r.Relaxes.Count} 前提{r.Prerequisite.Count} {r.H0}/{r.Rk}/{r.RkH}/{r.Rr}");
        }
        catch (Exception e)
        {
            LogOp("W", $"設定の緩和の試算 失敗: {e.GetType().Name}: {e.Message}");
            if (CellSheetLogic.RelaxOutcomeApplies(seq, _relaxSeq, ctx, RelaxCtxNow())) _relaxFailedCtx = ctx;
        }
        finally
        {
            if (seq == _relaxSeq)
            {
                Ui.RelaxSearching = false;
                Ui.RelaxRev++;
            }
        }
    }

    /// <summary>試算を止める（結果は出さない）。利用者の「やめる」と盤面ジョブの入口から呼ぶ。</summary>
    public void CancelRelaxTrial()
    {
        if (LastRelaxTrialTask is { IsCompleted: false }) _relaxStoppedCtx = _relaxCtx;
        _relaxCts?.Cancel();
        ++_relaxSeq;
        if (Ui.RelaxSearching) Ui.RelaxSearching = false;
    }

    /// <summary>いまのデータで見つかった組（無い・古いなら null）。読むたびに照合する。</summary>
    public RelaxToken? RelaxTrialFor() =>
        _relaxCtx is { } c && _relaxResult is RelaxTrial.Result r && RelaxCtxNow() == c ? new RelaxToken(c.StateKey, c.BoardKey, r) : null;

    /// <summary>いまのデータで探し終えて組が無かった（NoWall）。走っている・未着手・古い・試算不可なら false。</summary>
    public bool RelaxNoWall() =>
        _relaxCtx is { } c && c == RelaxCtxNow() && _relaxResult is RelaxTrial.NoWallOutcome;

    /// <summary>いまのデータでは試算できない理由（未割当のセルなど）。無い・古いなら null。</summary>
    public string? RelaxUnavailable() =>
        _relaxCtx is { } c && c == RelaxCtxNow() && _relaxResult is RelaxTrial.Unavailable u ? u.Reason : null;

    /// <summary>いまのデータで試算が失敗した（結果なし）。「もう一度試す」を出す。</summary>
    public bool RelaxFailed() => _relaxFailedCtx is { } c && c == RelaxCtxNow() && _relaxResult is null;

    /// <summary>いまのデータで試算を途中で止めた（結果なし）。「もう一度試す」を出す。</summary>
    public bool RelaxStopped() => _relaxStoppedCtx is { } c && c == RelaxCtxNow() && _relaxResult is null;

    /// <summary>止めた試算を利用者の操作でやり直す。</summary>
    public void RetryRelaxTrial() { if (RelaxTrialEnabled) StartRelaxTrial(); }

    /// <summary>直近の確定の結果 1 行。確定の後のデータから変わったら出さない（§9）。</summary>
    public string? RelaxDoneLine() => _relaxDone is { } d && d.Ctx == RelaxCtxNow() ? d.Line : null;

    /// <summary>承認フローを開く（§15）。いまのデータの試算だけを R1 に出し、それ以外は理由つきの R4。</summary>
    public void OpenRelaxFlow()
    {
        var c = _relaxCtx;
        var now = RelaxCtxNow();
        var fresh = c is not null && c == now;
        var outcome = _relaxResult ?? (_relaxStoppedCtx is not null && _relaxStoppedCtx == now ? RelaxTrial.Stopped : null);
        var searching = fresh && _relaxResult is null && LastRelaxTrialTask is { IsCompleted: false };
        var failed = RelaxFailed();
        var k = c ?? now ?? new RelaxCtx(0L, 0L);
        Ui.RelaxFlow = RelaxApproval.Start(outcome, fresh, RelaxApproval.TrialId(k.StateKey, k.BoardKey), RelaxApproval.Fingerprint(k.StateKey, k.BoardKey), searching, failed);
    }

    public void RelaxFlowNext()
    {
        if (Ui.RelaxFlow is { } f) Ui.RelaxFlow = RelaxApproval.Next(f, RelaxTrialFor() is not null);
    }

    public void RelaxFlowDecline()
    {
        if (Ui.RelaxFlow is { } f) Ui.RelaxFlow = RelaxApproval.DeclineFlow(f);
    }

    public void CloseRelaxFlow() => Ui.RelaxFlow = null;

    /// <summary>R3「承認して反映する」。理由が空・古い・押し済みなら何もしない。承認を記録してから反映し、結果で R5 か R4。</summary>
    public void ApproveRelax(string reason)
    {
        if (Ui.RelaxFlow is not { } f) return;
        var token = RelaxTrialFor();
        if (!RelaxApproval.CanApprove(reason, token?.Result, token is not null))
        {
            if (f.Step == RelaxApproval.Step.R3 && token is null) Ui.RelaxFlow = RelaxApproval.Finish(f, RelaxApproval.Verdict.Stale);
            return;
        }
        var r = token!.Result;
        var changes = r.Prerequisite.Concat(r.Relaxes)
            .Select(x => $"{(x.Staff < Ui.StaffNames.Count ? Ui.StaffNames[x.Staff] : $"職員{x.Staff + 1}")} {(x.Shift < Ui.ShiftSymbols.Count ? Ui.ShiftSymbols[x.Shift] : "?")} 上限0→{x.NewHi}")
            .Append($"手順{r.Moves.Count}セル").ToList();
        if (RelaxApproval.Press(f, RelaxApproval.DecisionId(f.TrialId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) is not { } pressed) return;
        pressed = pressed with { Applied = changes };
        Ui.RelaxFlow = pressed;
        LogOp("I", RelaxApproval.ApprovalLog(pressed.DecisionId!, pressed.TrialId, reason, changes));
        AgentDecisionApproved();
        var v = RelaxAndApply(token);
        Ui.RelaxFlow = RelaxApproval.Finish(pressed, v);
    }

    private sealed record AltsBackup(IReadOnlyList<int[][]> Scheds, long BoardKey, long StateKey);

    /// <summary>反映の前の (state, 盤面) と、反映が書き換えた画面の欄・段・結果盤面・他の案・確定の 1 行へ戻す（事後確認の不一致・保存失敗）。</summary>
    private void RestoreBeforeRelax(MagiState st, int[][] b, RelaxRollback<UndoSnap, int[][], AltsBackup> rb, (RelaxCtx Ctx, string Line)? done)
    {
        rb.RestoreStacks(_undoStack, _redoStack);
        _state = st;
        _currentSchedule = b.Copy2D();
        _resultSchedule = rb.ResultSchedule;
        _alternativeScheds = rb.Alts.Scheds; _altBoardKey = rb.Alts.BoardKey; _altStateKey = rb.Alts.StateKey;
        _relaxDone = done;
        rb.Restore(Ui);
        RefreshCheck();
    }

    /// <summary>確定「上限を緩め、手順を当てる」（§6。ガードはすべて最初の書き換えより前）。Undo 1 段で設定と盤面がまとめて戻る。</summary>
    public RelaxApproval.Verdict RelaxAndApply(RelaxToken token)
    {
        var st = _state;
        var b = _currentSchedule;
        if (st is null || b is null)
        {
            LogOp("W", RelaxApproval.ApplyLog("失敗", -1) + " 理由=データがない");
            return RelaxApproval.Verdict.Stale;
        }
        if (RunBlockedByInFlight("設定の緩和"))
        {
            LogOp("W", RelaxApproval.ApplyLog("失敗", -1) + " 理由=実行中");
            return RelaxApproval.Verdict.Blocked;
        }
        if (StateKey(st) != token.StateKey || BoardKey(b) != token.BoardKey)
        {
            LogOp("W", RelaxApproval.ApplyLog("失敗", -1) + " 理由=データが変わった");
            return RelaxApproval.Verdict.Stale;
        }
        var r = token.Result;
        var ns0 = RelaxTrial.Apply(st, r.Prerequisite.Concat(r.Relaxes).ToList());
        var pinP = ScheduleUtil.CachedProblem(st);
        var nb = RelaxTrial.ApplyMoves(b, r.Moves, (i, j) => i >= 0 && i < pinP.S && j >= 0 && j < pinP.T && pinP.Pinned(i, j));
        var got = nb is null ? (int?)null : UnifiedViolationChecker.Check(ns0, nb.Copy2D()).Hard;
        if (nb is null || got != r.Rr)
        {
            LogOp("W", RelaxApproval.ApplyLog("失敗", got ?? -1) + $" 理由=手順を当てた必須 {got} ≠ 試算 {r.Rr}");
            return RelaxApproval.Verdict.NotReproduced;
        }
        if (!EnsureValidForRun(ns0, nb, "設定の緩和"))
        {
            LogOp("W", RelaxApproval.ApplyLog("失敗", -1) + " 理由=データに問題");
            return RelaxApproval.Verdict.Blocked;
        }
        CancelRelaxTrial();
        var before = b.Copy2D();
        var rb = new RelaxRollback<UndoSnap, int[][], AltsBackup>(Ui, _undoStack.ToList(), _redoStack.ToList(), _resultSchedule,
            new AltsBackup(_alternativeScheds, _altBoardKey, _altStateKey));
        var doneBefore = _relaxDone;
        if (!PushUndo())
        {
            LogOp("W", RelaxApproval.ApplyLog("失敗", -1) + " 理由=元に戻す段を積めない");
            return RelaxApproval.Verdict.Blocked;
        }
        var ns = ns0.WithSchedule(nb);
        _state = ns;
        _currentSchedule = nb;
        _resultSchedule = null;
        var post = UnifiedViolationChecker.Check(ns, nb.Copy2D()).Hard;
        if (!RelaxApproval.PostCheckOk(r.Rr, post))
        {
            RestoreBeforeRelax(st, before, rb, doneBefore);
            LogOp("W", RelaxApproval.ApplyLog("復旧", post) + $" 理由=反映後の必須 {post} ≠ 試算 {r.Rr}");
            return new RelaxApproval.Verdict.Applied(post);
        }
        _relaxDone = (new RelaxCtx(StateKey(ns), BoardKey(nb)), NextActionGuide.RelaxDoneLine(r.H0, post));
        Ui.MessageIsError = false;
        Ui.HasResult = true;
        Ui.RanBeforeEdit = Ui.EngineRan || Ui.RanBeforeEdit;
        Ui.EngineRan = false;
        Ui.RelaxedBoard = true;
        Ui.StructureEdited = true;
        Ui.Schedule = nb.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        Ui.RunSummary = null;
        Ui.StopSummary = null;
        RefreshCheck();
        SaveNow();
        if (_hydrated && Ui.SaveState == SaveState.Failed)
        {
            RestoreBeforeRelax(st, before, rb, doneBefore);
            LogOp("W", RelaxApproval.ApplyLog("復旧", post) + " 理由=保存に失敗");
            AutoSave();
            return RelaxApproval.Verdict.SaveFailed;
        }
        PostOpNotice("設定を緩めて手順を適用しました（元に戻せます）");
        LogOp("I", RelaxApproval.ApplyLog("成功", post) + " 組 " + string.Join(", ", r.Prerequisite.Concat(r.Relaxes).Select(x => $"{x.Staff + 1}/{x.Shift}")) + $" 必須 {r.H0}→{post}");
        return new RelaxApproval.Verdict.Applied(post);
    }
}
