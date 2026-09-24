using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[S5] 試算 1 行の表示状態（<see cref="MagiViewModel.WishTrialFor"/>）。Kotlin <c>WishTrialView</c>。</summary>
public abstract record WishTrialView
{
    public sealed record NoneView : WishTrialView;
    public sealed record BusyView : WishTrialView;
    /// <summary>盤面か希望が試算時から変わった（結果は消さずに隠す）。</summary>
    public sealed record StaleView : WishTrialView;
    public sealed record Ready(WishTrial.Outcome Outcome, WishTrialToken Token) : WishTrialView;

    public static readonly WishTrialView None = new NoneView();
    public static readonly WishTrialView Busy = new BusyView();
    public static readonly WishTrialView Stale = new StaleView();
}

/// <summary>[S5] 確定の照合に使う試算時の文脈（§6 の 3）。<see cref="Result"/>＝null は試算できなかった行（確定できない）。</summary>
public sealed record WishTrialToken(MagiState State, long BoardKey, int Staff, int Day, int Shift, WishTrial.Result? Result);

/// <summary>
/// [S5] 「この希望を取り消したら」試算と確定（Kotlin <c>MagiViewModel.kt</c> の S5 節、<c>docs/s5_wish_trial.md</c> §6・§8・§14 D）。
/// 結果と対照は ctx（state の参照＋盤面キー）つきで VM が持ち、画面は読むたびに問い合わせる。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>試算の文脈。state は参照で見る（どの編集でも state が差し替わる＝指紋より厳しい、§7 I4）。</summary>
    private sealed class TrialCtx(MagiState st, long boardKey)
    {
        public MagiState St { get; } = st;
        public long BoardKey { get; } = boardKey;
    }

    private sealed record StalledSnap(MagiState St, long BoardKey, IReadOnlyList<string> Families);

    /// <summary>[S5] 確定操作の文脈（§6 の 10）。希望はすでに state から消えている。</summary>
    private sealed record S5Ctx(string Name, int Day, string Symbol, int H0, int Hx, int Rk, int Rr, int PCancel)
    {
        public string Label => $"{Name} {Day + 1}日 {Symbol}";
    }

    private CancellationTokenSource? _wishTrialCts;
    /// <summary>Cancel は非同期なので世代で古い完了を捨てる（_fixSeq と同じ理由）。</summary>
    private long _wishTrialSeq;
    /// <summary>結果と対照は 1 世代だけ。別の ctx で新しい試算を始めたときにだけ入れ替える（読む・確定するでは消さない）。</summary>
    private TrialCtx? _trialCtx;
    private WishTrial.Outcome? _trialControl;
    private readonly Dictionary<string, WishTrial.Outcome> _trialResults = new();   // "i,j,k"
    private TrialCtx? _cancelOutcomeCtx;
    /// <summary>確定の直前の StalledHardFamilies。確定を元に戻して同じ (state, 盤面) に戻ったら復元する（§14 D）。</summary>
    private StalledSnap? _stalledBeforeConfirm;

    /// <summary>[テスト可視性のための追加] 直近の <see cref="StartWishTrial"/> が背後で走らせる Task。</summary>
    internal Task? LastWishTrialTask { get; private set; }

    private bool CtxMatches(TrialCtx? c)
    {
        var st = _state;
        var b = _currentSchedule;
        return c is not null && st is not null && b is not null && ReferenceEquals(c.St, st) && c.BoardKey == BoardKey(b);
    }

    /// <summary>1 行の試算を始める。本実行・背景実行の最中は始めない（I11）。盤面は複製して渡す（I1）。</summary>
    public void StartWishTrial(int i, int j)
    {
        var st = _state;
        var b = _currentSchedule;
        if (st is null || b is null) return;
        if (OptimizeInFlight()) return;
        if (!st.Wishes.TryGetValue($"{i},{j}", out var k)) return;
        var bKey = BoardKey(b);
        var ctx = _trialCtx is { } c0 && ReferenceEquals(c0.St, st) && c0.BoardKey == bKey ? c0 : null;
        if (ctx is null)
        {
            ctx = new TrialCtx(st, bKey);
            _trialCtx = ctx;
            _trialControl = null;
            _trialResults.Clear();
        }
        _wishTrialCts?.Cancel();
        var seq = ++_wishTrialSeq;
        var cts = new CancellationTokenSource();
        _wishTrialCts = cts;
        Ui.WishTrialBusy = $"{i},{j}";
        LastWishTrialTask = WishTrialCoreAsync(st, b.Copy2D(), i, j, k, ctx, _trialControl, seq, cts.Token);
    }

    private async Task WishTrialCoreAsync(
        MagiState st, int[][] board, int i, int j, int k, TrialCtx ctx, WishTrial.Outcome? ctl0, long seq, CancellationToken ct)
    {
        try
        {
            var (ctl, outcome) = await Task.Run(() =>
            {
                bool Stop() => ct.IsCancellationRequested;
                var c = ctl0 ?? WishTrial.ControlOf(st, board, Stop);
                var o = c is WishTrial.ControlOutcome co ? WishTrial.Trial(st, board, i, j, co.Control, Stop) : c;
                return (c, o);
            });
            if (seq != _wishTrialSeq || !ReferenceEquals(_trialCtx, ctx)) return;
            if (ctl is not WishTrial.StoppedOutcome) _trialControl = ctl;
            if (outcome is not null and not WishTrial.StoppedOutcome) _trialResults[$"{i},{j},{k}"] = outcome;
        }
        catch (Exception e)
        {
            LogOp("W", $"希望の試算 失敗: {e.GetType().Name}: {e.Message}");
            if (seq == _wishTrialSeq && ReferenceEquals(_trialCtx, ctx)) _trialResults[$"{i},{j},{k}"] = new WishTrial.Unavailable(e.GetType().Name);
        }
        finally
        {
            if (seq == _wishTrialSeq)
            {
                Ui.WishTrialBusy = null;
                Ui.WishTrialRev++;
            }
        }
    }

    /// <summary>試算ジョブの取消だけ（CPU を返す）。結果は消さない＝正しさは読むときの照合が守る（§8）。</summary>
    public void CancelWishTrial()
    {
        _wishTrialCts?.Cancel();
        ++_wishTrialSeq;
        if (Ui.WishTrialBusy is not null) Ui.WishTrialBusy = null;
    }

    /// <summary>行の表示。読むたびに ctx を照合し、ずれていれば Stale（結果は消さない）。</summary>
    public WishTrialView WishTrialFor(int i, int j, int k)
    {
        if (Ui.WishTrialBusy == $"{i},{j}") return WishTrialView.Busy;
        if (!_trialResults.TryGetValue($"{i},{j},{k}", out var o)) return WishTrialView.None;
        var c = _trialCtx;
        if (c is null || !CtxMatches(c) || _state is null || !_state.Wishes.TryGetValue($"{i},{j}", out var cur) || cur != k)
            return WishTrialView.Stale;
        return new WishTrialView.Ready(o, new WishTrialToken(c.St, c.BoardKey, i, j, k, o as WishTrial.Result));
    }

    /// <summary>いまの (state, 盤面) の対照。Rk &lt; H0 ならダイアログの先頭行を出す（§5）。</summary>
    public WishTrial.Control? WishTrialControlFor() =>
        CtxMatches(_trialCtx) && _trialControl is WishTrial.ControlOutcome co ? co.Control : null;

    /// <summary>直近の確定の結果 1 行。確定の後の (state, 盤面) から変わったら出さない（§9）。</summary>
    public string? WishCancelOutcomeLine() =>
        Ui.WishCancelOutcome is { } o && CtxMatches(_cancelOutcomeCtx) ? o.Line : null;

    /// <summary>確定「希望を取り消して、もう一度つくる」（§6 の 1〜10。ガードはすべて最初の書き換えより前＝I5）。</summary>
    public void CancelWishAndRebuild(WishTrialToken token)
    {
        var st = _state;
        var b = _currentSchedule;
        if (st is null || b is null) return;
        if (RunBlockedByInFlight("希望の取り消し")) return;
        var key = $"{token.Staff},{token.Day}";
        var r = token.Result;
        if (r is null || !ReferenceEquals(st, token.State) || BoardKey(b) != token.BoardKey
            || !st.Wishes.TryGetValue(key, out var cur) || cur != token.Shift)
        {
            Ui.MessageIsError = true;
            Ui.Message = "勤務表か希望が変わりました。もう一度試算してください。";
            return;
        }
        CancelWishTrial();
        var ns = st with { Wishes = st.Wishes.Where(kv => kv.Key != key).ToDictionary(kv => kv.Key, kv => kv.Value) };
        if (!EnsureValidForRun(ns, b)) return;
        _stalledBeforeConfirm = new StalledSnap(st, token.BoardKey, Ui.StalledHardFamilies);
        PushUndo();
        _state = ns;
        ++_checkSeq;
        _checkCts?.Cancel();
        Ui.Wishes = ns.Wishes;
        Ui.StructureEdited = true;
        Ui.RunSummary = null;
        SaveNow();
        var name = token.Staff >= 0 && token.Staff < st.StaffList.Count ? st.StaffList[token.Staff].Name : $"職員{token.Staff + 1}";
        var sym = token.Shift >= 0 && token.Shift < st.Shifts.Count ? st.Shifts[token.Shift].Kigou : "?";
        LogOp("I", $"希望取消＋もう一度つくる: {name} {token.Day + 1}日（{sym}） {r.H0}/{r.Hx}/{r.Rk}/{r.Rr}/{r.PCancel}");
        StartFullOptimize(pushUndo: false, new S5Ctx(name, token.Day, sym, r.H0, r.Hx, r.Rk, r.Rr, r.PCancel));
    }

    /// <summary>元に戻す・やり直しで確定前の (state, 盤面) に戻ったときの StalledHardFamilies（§14 D）。</summary>
    private IReadOnlyList<string> StalledAfterRestore(MagiState st, int[][] sched) =>
        _stalledBeforeConfirm is { } s && ReferenceEquals(s.St, st) && s.BoardKey == BoardKey(sched) ? s.Families : Array.Empty<string>();
}
