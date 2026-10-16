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

    /// <summary>族の表示語（付随の変更の理由に使う）。族名の日本語は UI 層が持つので画面が差し込む。</summary>
    public Func<string, string> FamilyLabelOf { get; set; } = f => f;

    /// <summary>[テスト可視性のための追加] 直近の <see cref="FindFixSuggestions"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastFindFixSuggestionsTask { get; private set; }

    /// <summary>
    /// [改善提案] 違反を減らす「1手（変更/交換）」を探索して UI に提示する。
    /// focusStaff != null のときはそのスタッフが関わる手だけに絞る（違反タップ起点）。重い処理のため非同期。
    /// Kotlin原本 <c>findFixSuggestions(focusStaff, focusShift)</c> の移植。
    /// </summary>
    /// <summary>[3.475.0/論理監査] 改善提案を計算した盤面/設定の指紋（適用時に照合する。0=未計算）。</summary>
    private long _fixBoardKey;
    private long _fixStateKey;
    private string _fixSearchKey = "";

    /// <param name="quick">セルのシート（締切 <see cref="CellSheetLogic.FixSearchQuickMs"/>＝3 秒。手は 1 件でよい。8 秒待たせると答えが無いときも長い＝3.646.0 実機報告）。
    /// ホーム・分析は <see cref="CellSheetLogic.FixSearchMs"/>＝8 秒。</param>
    /// <param name="allDays">同じ勤務の人員不足の日々をまとめて見る（Kotlin 3.673.0）。対象は <see cref="CellSheetLogic.MultiNeedTarget"/>。</param>
    public void FindFixSuggestions(int? focusStaff = null, int? focusShift = null, string focusKey = "", int? exceptStaff = null, int? day = null, bool quick = false, bool allDays = false)
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule;
        if (sched is null) return;
        var focusName = focusStaff is not null && focusStaff.Value >= 0 && focusStaff.Value < st.StaffList.Count
            ? st.StaffList[focusStaff.Value].Name
            : "";
        var snap = sched.Copy2D();
        _fixBoardKey = BoardKey(snap);
        _fixStateKey = StateKey(st);
        // [3.392.0の由来をそのまま記録] `seq` を持つのは refreshCheck と同じ理由＝`Cancel()` は非同期なので、
        //   後続の探索が `fixSearching=true` を立てた**後**に古いジョブの後始末が走ると、新しい探索の旗を
        //   消してしまう。
        var seq = ++_fixSeq;
        _fixCts?.Cancel(); // 連続タップ時の前探索を破棄（古い結果で UI を上書きしない）
        _fixSearchKey = focusKey;
        Ui.FixSearching = true;
        Ui.FixFocusName = focusName;
        Ui.FixDoneKey = "";
        Ui.FixFailedKey = "";
        Ui.FixCancelledKey = "";
        var cts = new CancellationTokenSource();
        _fixCts = cts;
        LastFindFixSuggestionsTask = FindFixSuggestionsCoreAsync(st, snap, focusStaff, focusShift, focusName, focusKey, seq, cts.Token, exceptStaff, day, quick, allDays);
    }

    /// <summary>1手の候補・「探索済み」・下限判定の材料を消す（盤面が変わった／使えなくなったとき。Kotlin の各リセット箇所と同じ）。</summary>
    internal void ClearFixState()
    {
        Ui.FixSuggestions = Array.Empty<FixSuggestion>();
        Ui.FixTiers = null;
        Ui.FixSearched = false;
        Ui.FixDoneKey = "";
        Ui.FixFailedKey = "";
        Ui.StalledHardFamilies = Array.Empty<string>();
    }

    /// <summary>通知の「元に戻す」。その操作がまだ元に戻すの先頭にあるときだけ戻す。</summary>
    public void UndoNotice(OpNotice n)
    {
        if (!n.Undoable) return;   // 結果だけの通知（UndoSerial は前の操作の段）で別の操作を戻さない
        if (CellSheetLogic.NoticeUndoApplies(_undoStack.Last?.Value.Serial, n.UndoSerial)) Undo();
        else { Ui.MessageIsError = true; Ui.Message = "このあとに別の操作があるため、通知からは戻せません（「元に戻す」ボタンで順に戻せます）"; }
    }

    public void ClearOpNotice(long id) { if (Ui.OpNotice?.Id == id) Ui.OpNotice = null; }

    private async Task FindFixSuggestionsCoreAsync(
        MagiState st, int[][] snap, int? focusStaff, int? focusShift, string focusName, string focusKey, long seq, CancellationToken ct,
        int? exceptStaff = null, int? day = null, bool quick = false, bool allDays = false)
    {
        try
        {
            var deadline = quick ? CellSheetLogic.FixSearchQuickMs : CellSheetLogic.FixSearchMs;
            var timedOut = false;
            // 集計・日の印から来た依頼は、見ている問題（回数・その日の人数）を変える案を先に（2026-10-14 利用者レビュー:
            //   11/3 の不足に別の日の案だけが並んだ）。広めに集めて並べ替え、上位 8 件を返す。
            var target = exceptStaff is null ? CellSheetLogic.PanelTarget(Ui, new FixFocus(focusStaff, focusShift, day, AllDays: allDays), StaffCellLimits, NeedCellLimits) : null;
            // [3.697.0] 起点は画面の見出しと同じ語（対象＝FixTargetLabel、職員＝氏名、どちらも無ければ全体）。
            var searchStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LogOp("I", OpLogText.FixSearchStartLine(target is { } tg ? CellSheetLogic.FixTargetLabel(tg, OpSy(tg.Shift), st.StartDate) : focusName.Length == 0 ? OpLogText.FixOriginAll : focusName));
            var board = snap.Select(r => (IReadOnlyList<int>)r.ToList()).ToList();
            FixTierView? tiers = null;
            var labelOf = FamilyLabelOf;
            var list = await Task.Run(
                () =>
                {
                    var found = exceptStaff is { } ex && day is { } dd
                        // 「他の人で補う」（Android CellSheetLogic.fixesByOthers）: 全体で探し、本人を含まずその日を含む手だけ。
                        ? CellSheetLogic.FixesByOthers(FixSuggester.Suggest(st, snap, focusStaff: null, focusShift: focusShift, maxResults: 40, deadlineMs: deadline), dd, ex).Take(8).ToList()
                        : target is not null
                        ? TieredFor(target)
                        : FixSuggester.Suggest(st, snap, focusStaff: focusStaff, focusShift: focusShift, maxResults: 8, deadlineMs: deadline);
                    timedOut = FixSuggester.LastTimedOut;
                    return found;

                    // 入り口の範囲の中の 1 セルの変更を先に評価し、探索の案と合わせて段に分ける。
                    IReadOnlyList<FixSuggestion> TieredFor(FixTarget t)
                    {
                        var entry = FixTiers.EntryMoves(st, snap, t, ScheduleUtil.RestShiftIndex(st));
                        var ranked = CellSheetLogic.RankForTarget(FixSuggester.Suggest(st, snap, focusStaff: focusStaff, focusShift: focusShift, maxResults: 40, deadlineMs: deadline), board, t).Take(8).ToList();
                        tiers = FixTiers.ClassifyTiers(st, snap, t, entry, ranked, NeedCellLimits, labelOf: labelOf) with { Key = focusKey };
                        return tiers.Items.Select(x => x.Sug).ToList();
                    }
                }, ct)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.ForceYielding);   // RefreshCheckCoreAsync と同じ理由
            if (seq != _fixSeq) return; // 後続の探索が始まっている＝古い結果で上書きしない
            // 盤面を差し替えるジョブの最中は書き戻さず探し直しもしない（完了後の盤面で探し直す）。
            if (OptimizeInFlight()) { Ui.FixSearching = false; return; }
            // [Android 3.612.0] 探索中に盤面か設定が変わったら（元に戻す・セル編集・実行の完了など）、古い盤面の
            //   結果と「探索済み」を書き戻さず、今の盤面で探し直す。
            var curSched = _currentSchedule; var curSt = _state;
            if (curSched is null || curSt is null || BoardKey(curSched) != BoardKey(snap) || StateKey(curSt) != StateKey(st))
            {
                Ui.FixSearching = false;
                if (curSched is not null && curSt is not null) FindFixSuggestions(focusStaff, focusShift, focusKey, exceptStaff, day, quick, allDays);
                return;
            }
            Ui.FixSuggestions = list;
            Ui.FixTiers = tiers;
            Ui.FixSearching = false;
            Ui.FixTimedOut = timedOut;
            Ui.FixFocusName = focusName;
            Ui.FixSearched = focusName.Length == 0;
            Ui.FixDoneKey = focusKey;
            LogOp("I", OpLogText.FixSearchEndLine(list.Count, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - searchStartMs));
            // [S6 §8] 全体の 1 手探索が必須を減らす候補なしで終わったら、背景で設定の壁を探す。
            if (focusName.Length == 0 && !list.Any(x => x.DeltaHard < 0) && Ui.BestHard > 0 && RelaxTrialEnabled) StartRelaxTrial();
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
                Ui.MessageIsError = true;
                Ui.FixSearching = false;
                Ui.FixFailedKey = focusKey;
                Ui.Message = $"{(focusName.Length == 0 ? "" : $"{focusName}さんの")}直し方を探せませんでした（{FailureWords.Of(e, FailureKind.Engine)}）。もう一度お試しください";
            }
        }
    }

    /// <summary>走行中の直し方の探索を捨てる（世代を進めるので、完了間際の結果も書き戻さない）。</summary>
    public void CancelFixSearch()
    {
        ++_fixSeq;
        _fixCts?.Cancel();
        _fixCts = null;
        if (Ui.FixSearching) { Ui.FixSearching = false; Ui.FixCancelledKey = _fixSearchKey; }
    }

    /// <summary>[改善提案] 改善手を1タップで適用（ops のセル代入を一括反映）。Undo 可・自動再診断・自動保存。
    /// Kotlin原本 <c>applyFixSuggestion(s: FixSuggestion)</c> の移植。</summary>
    // [3.643.0] 直し方を当てた結果の文脈（S5 の結果行と同じ鮮度の規則＝盤面か設定が変わったら出さない）。
    private TrialCtx? _fixOutcomeCtx;
    private (int DayIndex, int ShiftIndex, string DayLabel, string ShiftSymbol)? _pendingGuided;

    /// <summary>「なおし方を見る」で 1 人を入れた直後に枠を覚える。再検査の結果（MakeUi）でその枠がまだ足りないかを見て 1 行にする。</summary>
    public void NoteGuidedFix(int dayIndex, int shiftIndex, string dayLabel, string shiftSymbol) => _pendingGuided = (dayIndex, shiftIndex, dayLabel, shiftSymbol);

    /// <param name="labelOf">族の表示語。渡すと改善手の適用の内訳（<see cref="FixOutcomeText.Detail"/>）を続けて返す。</param>
    public string? FixOutcomeLine(Func<string, string>? labelOf = null) =>
        Ui.FixOutcome is { } o && CtxMatches(_fixOutcomeCtx)
            ? o.Line + (labelOf is not null && o.Before is not null && o.After is not null ? FixOutcomeText.Detail(o.Before, o.After, labelOf) : "") + (o.Tail ?? "")
            : null;

    private void ResolvePendingGuidedFix(MagiState st, int[][] schedule, ViolationReport report, CoverageDiagnosis? diag)
    {
        if (_pendingGuided is not { } g) return;
        _pendingGuided = null;
        _guidedApplied = true;
        var still = diag?.Shortfalls.FirstOrDefault(sf => sf.DayIndex == g.DayIndex && sf.ShiftIndex == g.ShiftIndex && sf.Miss > 0)?.Miss;
        _fixOutcomeCtx = new TrialCtx(st, BoardKey(schedule));
        Ui.FixOutcome = new FixOutcome(FixOutcomeText.Guided(g.DayLabel, g.ShiftSymbol, still, report.Hard, report.Breakdown));
    }

    /// <summary>改善手を適用する。適用したら true（断ったら false、理由は <see cref="UiState.Message"/>）。
    /// <paramref name="fixTarget"/> の持ち回り: 枠や問題を見て出した案（案の一覧・不足枠の 1 手・相談に積んだ案）は対象を運び、その数が減らない手は断る。
    /// ホームと分析タブの「この手を使う」は全体の研磨の案＝<paramref name="globalPolish"/>（全体の評価と <see cref="FixApplyGate"/> だけで判定）。
    /// 対象が null で <paramref name="globalPolish"/> も無い案は断る（呼び手がどちらかを明示する）。
    /// <paramref name="fromPreview"/> の案は案を出したときの指紋が必須＝0 なら断る（全体の指紋で代わりに通さない）。</summary>
    /// <param name="focusKey">案の一覧のどの依頼から来たか。あれば成功時に <see cref="UiState.AppliedFix"/> を残す（Kotlin 3.669.0）。</param>
    public bool ApplyFixSuggestion(FixSuggestion s, long? originBoard = null, long? originState = null, string? focusKey = null, FixTarget? fixTarget = null,
        bool fromPreview = false, bool globalPolish = false)
    {
        var st = _state;
        if (st is null) return false;
        // [外部レビューH2/applyWishes等と同根] running中は currentSchedule が最適化ジョブの sched0 と
        //   同一参照のため良化採用時に上書き消失しうる。編集は必ず4入口を通るガード対象。
        if (OptimizeInFlight()) { Ui.Message = BusyEditMessage(); Ui.MessageIsError = true; return false; }
        var sched = _currentSchedule;
        if (sched is null) return false;
        if (s.Ops.Count == 0) return false;
        // 提案は計算時の盤面/設定に対する差分。その後のセル編集・元に戻す・別データ読込・職員/シフト削除のあとに
        // 同じ ops を書き込むと staff/day/toShift が別の実体を指す。一致しなければ適用せず再探索を促す（Kotlin 3.475.0）。
        // [Kotlin 3.650.0] 照合は案を出したときの指紋（相談に積んだ案は自分の指紋を運ぶ＝あとの探索で上書きされた全体の指紋で通さない）。
        var ob = originBoard ?? _fixBoardKey;
        var os = originState ?? _fixStateKey;
        if ((CellSheetLogic.FixOriginRefusal(ob, fromPreview) ?? CellSheetLogic.FixTargetRefusal(fixTarget, globalPolish)) is { } refusal)
        {
            // [3.694.0] 断った理由を操作ログにも残す（画面の message は不変）。
            LogOp("W", $"改善手を見送り: {s.Label}（案の出どころが違う: {refusal}）");
            Ui.MessageIsError = true; Ui.Message = refusal; return false;
        }
        var nowBoard = BoardKey(sched);
        var nowState = StateKey(st);
        if (ob != 0L)
        {
            if (ob != nowBoard || os != nowState)
            {
                LogOp("W", $"改善手を見送り: {s.Label}（案を出したときと違う: 盤面指紋 案={OpLogText.Hex16(ob)} 今={OpLogText.Hex16(nowBoard)} ・ 状態指紋 案={OpLogText.Hex16(os)} 今={OpLogText.Hex16(nowState)}）");
                Ui.MessageIsError = true;
                ClearFixState();
                Ui.Message = "勤務表か設定が変わったため、この提案は適用できません。「変更案を探す」をもう一度押してください";
                return false;
            }
        }
        // 見ている問題の数が減らない案は、全体の評価が良くなっても当てない（統合仕様 INV-03。案の一覧の主一覧と同じ判定）。
        if (!CellSheetLogic.ImprovesTarget(fixTarget, sched, s.Ops))
        {
            LogOp("W", $"改善手を見送り: {s.Label}（見ている問題が減らない）");
            Ui.MessageIsError = true;
            ClearFixState();
            Ui.Message = CellSheetLogic.NotImprovingTargetMessage;
            return false;
        }
        // [Android 3.509.4/自動化方針] 適用直前に仮盤面で完全再評価し、辞書式で改善しない・固定を崩す提案は反映しない。
        var gate = FixApplyGate.Apply(st, sched, s.Ops);
        if (!gate.Applied)
        {
            LogOp("W", $"改善手を見送り: {s.Label}（{gate.Reason}）");
            Ui.MessageIsError = true;
            ClearFixState();
            Ui.Message = $"この提案は見送りました（{gate.Reason}）。「変更案を探す」で探し直してください";
            return false;
        }
        PushUndo();
        var applied = gate.Schedule!;
        _currentSchedule = applied;
        _state = st.WithSchedule(applied);
        AutoSave();
        Ui.MessageIsError = false;
        Ui.HasResult = true;
        Ui.RanBeforeEdit = Ui.EngineRan || Ui.RanBeforeEdit;
        Ui.EngineRan = false;
        Ui.RelaxedBoard = false;
        Ui.Schedule = applied.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        ClearFixState(); // 適用後は候補をクリア（盤面が変わるため再探索を促す）
        if (focusKey is not null)
            Ui.AppliedFix = new AppliedFix(focusKey, s.Label, s.Ops, sched.Select(r => (IReadOnlyList<int>)r.ToList()).ToList(), Ui.Schedule, fixTarget,
                gate.Before.Hard, gate.After.Hard, gate.Before.Soft, gate.After.Soft, gate.Before.Breakdown, gate.After.Breakdown);
        // [3.694.0] 適用前の盤面は照合に使った sched（originBoard は 0 のことがある）。状態指紋は盤面を含まず前後で同じなので 1 つ。
        var seenMs = fromPreview && _chainPreviewOpenedAtMs > 0L ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _chainPreviewOpenedAtMs : (long?)null;
        _chainPreviewOpenedAtMs = 0L;
        var line = $"改善手を適用: {s.Label}（必須 {gate.Before.Hard}→{gate.After!.Hard}・合計 {gate.Before.Total}→{gate.After.Total}）" +
            $" ・ 期間={OpLogText.Period(st.StartDate, st.EndDate, st.DayCount)} ・ 盤面指紋={OpLogText.Hex16(nowBoard)}→{OpLogText.Hex16(BoardKey(applied))} ・ 状態指紋={OpLogText.Hex16(nowState)}";
        LogOp("I", seenMs is { } seen ? OpLogText.WithSeenSeconds(line, seen) : line);
        var maxLen = Math.Max(new[] { st.Cons3, st.Cons3n, st.Cons3m, st.Cons3mn }.SelectMany(x => x).Select(r => r.Pattern.Count).DefaultIfEmpty(0).Max(),
            (st.Cons3w?.Count ?? 0) > 0 ? 2 : 0);
        var edge = PreDistributionCheck.MonthEdgeTouched(s.Ops.Select(o => o.Day), applied.FirstOrDefault()?.Length ?? 0, maxLen) ? $"{PreDistributionCheck.MonthEdgeNote}。" : null;
        var softBefore = AnalysisTriage.UnitTotals(gate.Before.Breakdown, MirrorKeys.Soft) ?? "なし";
        var softAfter = AnalysisTriage.UnitTotals(gate.After.Breakdown, MirrorKeys.Soft) ?? "なし";
        Ui.FixOutcome = new FixOutcome(FixOutcomeText.Applied(FixOutcomeText.FixDisplayLabel(s.Label), gate.Before.Hard, gate.After.Hard, softBefore, softAfter, gate.After.Breakdown, gate.Before.Breakdown), gate.Before, gate.After, edge);
        // 通知は 2 行まで＝必須が動いた手は必須だけ、動かない手は合計だけを添える（両方の数はホームの結果の行とログ）。
        PostOpNotice($"{FixOutcomeText.FixDisplayLabel(s.Label)} を適用しました（" + (gate.Before.Hard != gate.After.Hard ? $"必須違反 {gate.Before.Hard}→{gate.After.Hard}"
            : $"要調整 {softBefore}→{softAfter}") + "）");
        _fixOutcomeCtx = _state is { } stNow ? new TrialCtx(stNow, BoardKey(applied)) : null;
        RefreshCheck();
        return true;
    }

    /// <summary>当てる前に一覧（だれの・どの日の・何→何）と新たな必須違反を見せる（Kotlin <c>previewFixSuggestion</c>）。
    /// <paramref name="fixTarget"/> は見ている問題（あれば適用時にその数が減るかを確かめる）。</summary>
    public void PreviewFixSuggestion(FixSuggestion s, long? originBoard = null, long? originState = null, FixTarget? fixTarget = null, bool globalPolish = false)
    {
        var ob = originBoard ?? _fixBoardKey;
        var os = originState ?? _fixStateKey;
        var sched = _currentSchedule;
        if (sched is null || _state is not { } st) return;
        var snap = sched.Copy2D();
        OpenPreview(s, snap, st, new ChainTarget(null, null, s.Label, Suggestion: s, BoardKey: ob, StateKey: os), fixTarget: fixTarget, globalPolish: globalPolish);
    }

    /// <summary>休のシフトの番号（Kotlin <c>ConditionsView.restIdx</c>。休が無い設定は -1＝どのシフトにも一致しない番兵）。</summary>
    public int RestIdx => _state is { } st ? ScheduleUtil.RestShiftIndex(st) ?? -1 : -1;

    /// <summary>案の一覧から: 1 セルの案も当てる前に確認の画面（目的・全変更・対象の数の変化）を出す（Kotlin <c>previewFix</c>、
    /// 2026-10-14 利用者レビュー「見ると適用するを分ける」）。</summary>
    public void PreviewFix(FixSuggestion s, string? purpose, IReadOnlyList<string> effect, string? focusKey = null, FixTarget? fixTarget = null)
    {
        var sched = _currentSchedule;
        if (sched is null || _state is not { } st) return;
        var snap = sched.Copy2D();
        OpenPreview(s, snap, st, new ChainTarget(null, null, s.Label, Suggestion: s, BoardKey: _fixBoardKey, StateKey: _fixStateKey), purpose, effect,
            fromList: true, focusKey: focusKey, fixTarget: fixTarget, globalPolish: fixTarget is null);
    }

    /// <summary>相談に積んだ入れ替えの枠を今の勤務表で探し直す（日付・記号で枠を引き直す＝月やシフトが変わっていれば断る）。
    /// 枠を持たない案は一覧をもう一度出す（当てるときは <see cref="ApplyFixSuggestion"/> の指紋照合と適用の門が守る）。Kotlin <c>resumeConsultChain</c>。</summary>
    public void ResumeConsultChain(ConsultItem c)
    {
        if (c.Chain is not { } t) return;
        var u = Ui;
        var target = ConsultList.ConsultChainTarget(t, u.StartDate, u.ShiftSymbols, u.Days);
        if (target is { } tg) PrepareShortageChainFix(tg.J, tg.K, t.Label);
        else if (t.Suggestion is { } s && _currentSchedule is { } sched && _state is { } st && !ConsultList.ConsultChainStale(t, BoardKey(sched), StateKey(st)))
            PreviewFixSuggestion(s, t.BoardKey, t.StateKey, t.FixTarget, t.GlobalPolish);
        else if (t.Suggestion is not null)
        {
            Ui.MessageIsError = true;
            Ui.Message = ConsultList.Stale;
        }
        else
        {
            Ui.MessageIsError = true;
            Ui.Message = ConsultList.ConsultTargetNote(c, u.StartDate, u.StaffNames, u.ShiftSymbols, u.Days, u.StaffIds) ?? "この枠はいまのデータにありません";
        }
    }
}
