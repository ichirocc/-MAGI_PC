using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9] <c>MagiViewModel.kt</c> の <c>findFixSuggestions(focusStaff, focusShift)</c>
/// （2021-2048行）と <c>applyFixSuggestion(s: FixSuggestion)</c>（2051-2072行）の移植——
/// 改善提案（<see cref="FixSuggester"/>、既にエンジン側で完全移植済み）をViewModelへ配線する層。
///
/// [_fixSeq/_fixCts の由来] Kotlin原本の <c>fixJob</c>/<c>fixSeq</c> と同じ「世代管理つき
/// fire-and-forget」パターン（<c>MagiViewModel.Persistence.cs</c> の <c>RefreshCheck</c>/<c>_checkSeq</c>/
/// <c>_checkCts</c> と同型）。<c>_fixSeq</c> フィールド自体は <c>MagiViewModel.cs</c>（ピース5）に
/// 3.392.0 の由来コメントつきで既に宣言済み（当時は未使用のまま先行導入されていた）——このピースが
/// 初めてその宣言を実際に運動させる。<c>_fixCts</c> はこのピースで新規追加する（Kotlin原本の
/// <c>fixJob: Job?</c> に対応する専用トークンで、他のジョブと共有しない）。
///
/// [_ui.update{it.copy(...)} の置き換え方針] Piece5のクラスKDoc参照——このC#移植では
/// <c>Ui.X = ...;</c> という直接プロパティ代入へ置き換える。
/// </summary>
public sealed partial class MagiViewModel
{
    // ===== 改善提案（findFixSuggestions / applyFixSuggestion） =====

    private CancellationTokenSource? _fixCts;

    /// <summary>[テスト可視性のための追加] 直近の <see cref="FindFixSuggestions"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastFindFixSuggestionsTask { get; private set; }

    /// <summary>
    /// [改善提案] 違反を減らす「1手（変更/交換）」を探索して UI に提示する。
    /// focusStaff != null のときはそのスタッフが関わる手だけに絞る（違反タップ起点）。重い処理のため非同期。
    /// Kotlin原本 <c>findFixSuggestions(focusStaff, focusShift)</c> の移植。
    /// </summary>
    public void FindFixSuggestions(int? focusStaff = null, int? focusShift = null)
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule;
        if (sched is null) return;
        var focusName = focusStaff is not null && focusStaff.Value >= 0 && focusStaff.Value < st.StaffList.Count
            ? st.StaffList[focusStaff.Value].Name
            : "";
        var snap = sched.Copy2D();
        // [3.392.0の由来をそのまま記録] `seq` を持つのは refreshCheck と同じ理由＝`Cancel()` は非同期なので、
        //   後続の探索が `fixSearching=true` を立てた**後**に古いジョブの後始末が走ると、新しい探索の旗を
        //   消してしまう。
        var seq = ++_fixSeq;
        _fixCts?.Cancel(); // 連続タップ時の前探索を破棄（古い結果で UI を上書きしない）
        Ui.FixSearching = true;
        Ui.FixFocusName = focusName;
        var cts = new CancellationTokenSource();
        _fixCts = cts;
        LastFindFixSuggestionsTask = FindFixSuggestionsCoreAsync(st, snap, focusStaff, focusShift, focusName, seq, cts.Token);
    }

    private async Task FindFixSuggestionsCoreAsync(
        MagiState st, int[][] snap, int? focusStaff, int? focusShift, string focusName, long seq, CancellationToken ct)
    {
        try
        {
            var list = await Task.Run(
                () => FixSuggester.Suggest(st, snap, focusStaff: focusStaff, focusShift: focusShift, maxResults: 8), ct);
            if (seq != _fixSeq) return; // 後続の探索が始まっている＝古い結果で上書きしない
            Ui.FixSuggestions = list;
            Ui.FixSearching = false;
            Ui.FixFocusName = focusName;
        }
        catch (OperationCanceledException)
        {
            if (seq == _fixSeq) Ui.FixSearching = false;
            throw;
        }
        catch (Exception e)
        {
            LogOp("W", $"直し方の探索に失敗: {e.GetType().Name}: {e.Message}");
            if (seq == _fixSeq)
            {
                Ui.MessageIsError = false;
                Ui.FixSearching = false;
                Ui.Message = "直し方を探せませんでした";
            }
        }
    }

    /// <summary>[改善提案] 改善手を1タップで適用（ops のセル代入を一括反映）。Undo 可・自動再診断・自動保存。
    /// Kotlin原本 <c>applyFixSuggestion(s: FixSuggestion)</c> の移植。</summary>
    public void ApplyFixSuggestion(FixSuggestion s)
    {
        var st = _state;
        if (st is null) return;
        // [外部レビューH2/applyWishes等と同根] running中は currentSchedule が最適化ジョブの sched0 と
        //   同一参照のため良化採用時に上書き消失しうる。編集は必ず4入口を通るガード対象。
        if (OptimizeInFlight()) { Ui.Message = BusyEditMessage(); Ui.MessageIsError = true; return; }
        var sched = _currentSchedule;
        if (sched is null) return;
        if (s.Ops.Count == 0) return;
        foreach (var op in s.Ops)
        {
            if (op.Staff < 0 || op.Staff >= sched.Length) return;
            if (op.Day < 0 || op.Day >= sched[op.Staff].Length) return;
            if (op.ToShift < 0) return;
        }
        PushUndo();
        foreach (var op in s.Ops) sched[op.Staff][op.Day] = op.ToShift;
        _currentSchedule = sched;
        _state = st.WithSchedule(sched);
        AutoSave();
        Ui.MessageIsError = false;
        Ui.HasResult = true;
        Ui.EngineRan = false;
        Ui.Schedule = sched.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        Ui.FixSuggestions = Array.Empty<FixSuggestion>(); // 適用後は候補をクリア（盤面が変わるため再探索を促す）
        Ui.Message = $"改善手を適用: {s.Label}";
        RefreshCheck();
    }
}
