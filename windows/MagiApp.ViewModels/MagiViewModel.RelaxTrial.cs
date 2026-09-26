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

    /// <summary>[テスト可視性のための追加] 直近の <see cref="StartRelaxTrial"/> が背後で走らせる Task。</summary>
    internal Task? LastRelaxTrialTask { get; private set; }

    private RelaxCtx? RelaxCtxNow() =>
        _state is { } st && _currentSchedule is { } b ? new RelaxCtx(StateKey(st), BoardKey(b)) : null;

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
        var cts = new CancellationTokenSource();
        _relaxCts = cts;
        Ui.RelaxSearching = true;
        LastRelaxTrialTask = RelaxTrialCoreAsync(st, b.Copy2D(), seq, cts.Token);
    }

    private async Task RelaxTrialCoreAsync(MagiState st, int[][] board, long seq, CancellationToken ct)
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
        _relaxCts?.Cancel();
        ++_relaxSeq;
        if (Ui.RelaxSearching) Ui.RelaxSearching = false;
    }

    /// <summary>いまのデータで見つかった組（無い・古いなら null）。読むたびに照合する。</summary>
    public RelaxToken? RelaxTrialFor() =>
        _relaxCtx is { } c && _relaxResult is RelaxTrial.Result r && RelaxCtxNow() == c ? new RelaxToken(c.StateKey, c.BoardKey, r) : null;

    /// <summary>直近の確定の結果 1 行。確定の後のデータから変わったら出さない（§9）。</summary>
    public string? RelaxDoneLine() => _relaxDone is { } d && d.Ctx == RelaxCtxNow() ? d.Line : null;

    /// <summary>確定「この組で緩めて、手順を当てる」（§6。ガードはすべて最初の書き換えより前）。Undo 1 段で設定と盤面がまとめて戻る。</summary>
    public void RelaxAndApply(RelaxToken token)
    {
        var st = _state;
        var b = _currentSchedule;
        if (st is null || b is null) return;
        if (RunBlockedByInFlight("設定の緩和")) return;
        if (StateKey(st) != token.StateKey || BoardKey(b) != token.BoardKey)
        {
            Ui.MessageIsError = true;
            Ui.Message = "勤務表か設定が変わりました。もう一度試算してください。";
            return;
        }
        var r = token.Result;
        var ns0 = RelaxTrial.Apply(st, r.Prerequisite.Concat(r.Relaxes).ToList());
        var nb = RelaxTrial.ApplyMoves(b, r.Moves);
        var got = nb is null ? (int?)null : UnifiedViolationChecker.Check(ns0, nb.Copy2D()).Hard;
        if (nb is null || got != r.Rr)
        {
            LogOp("W", $"S6 確定を見送り: 手順を当てた必須 {got} ≠ 試算 {r.Rr}");
            Ui.MessageIsError = true;
            Ui.Message = "試算の手順を当てても同じ結果になりませんでした。勤務表と設定はそのままです。";
            return;
        }
        if (!EnsureValidForRun(ns0, nb)) return;
        CancelRelaxTrial();
        PushUndo();
        var ns = ns0.WithSchedule(nb);
        _state = ns;
        _currentSchedule = nb;
        _resultSchedule = null;
        _relaxDone = (new RelaxCtx(StateKey(ns), BoardKey(nb)), NextActionGuide.RelaxDoneLine(r.H0, got.Value));
        Ui.MessageIsError = false;
        Ui.HasResult = true;
        Ui.EngineRan = false;
        Ui.StructureEdited = true;
        Ui.Schedule = nb.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        Ui.RunSummary = null;
        Ui.Message = "設定を緩めて手順を当てました（元に戻せます）";
        LogOp("I", "S6 確定: 組 " + string.Join(", ", r.Prerequisite.Concat(r.Relaxes).Select(x => $"{x.Staff + 1}/{x.Shift}")) + $" 必須 {r.H0}→{got}");
        RefreshCheck();
        SaveNow();
    }
}
