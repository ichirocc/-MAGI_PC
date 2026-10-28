using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9, Services/UseCases/DI層] <c>MagiViewModel.kt</c> の <c>generateSmartInitial()</c>
/// （969-1018行）の移植——希望シフト→C1(窓の要件)→日別必要人数→個人下限→残り埋め の順で
/// 初期解を組み立てる「初期解生成(賢い版)」。本最適化(SA/ALNS)へは続けない
/// （続けて最適化したい場合は利用者がこの後に別途「勤務表をつくる」を押す）。
///
/// [Services/UseCases/DI層を通さない理由] <c>RunV6FullOptimize</c>/<c>RunSoftPolish</c>
/// （<c>MagiViewModel.Optimize.cs</c>）と異なり、この関数が呼ぶ <see cref="V6FinalPort.HandleSmartInitial"/>
/// は <see cref="Services.IOptimizationService"/> の境界に切り出されていない
/// （同インターフェースのクラスKDoc「<c>runV6FullOptimize</c>/<c>runSoftPolish</c> が使う2つの
/// エンジン入口だけをこの境界に切り出す」を参照——<c>HandleSmartInitial</c> は <c>HandleCheck</c> と
/// 同様、既存ピースが直接呼ぶ対象として明示的にスコープ外とされている）。したがってこのピースも
/// <see cref="V6FinalPort.HandleSmartInitial"/> を直接呼ぶ。
///
/// [Task.Run が必要な理由] <see cref="V6FinalPort.HandleSmartInitial"/> は（<c>HandleOptimize</c> と異なり）
/// 素の同期メソッド。<c>RefreshCheckCoreAsync</c>（<c>MagiViewModel.Persistence.cs</c>）が同じ理由で
/// <c>HandleCheck</c> を <c>Task.Run</c> で包むのと同じ手法で、呼出し元スレッドから外す。
///
/// [job フィールドの共有] Kotlin原本の <c>job</c>（クラスフィールド）は
/// <c>load</c>/<c>runV6FullOptimize</c>/<c>runSoftPolish</c>/<c>generateSmartInitial</c>/<c>stop</c> で
/// 共有される単一の <c>Job?</c>。この移植でも同じ <c>private CancellationTokenSource? _job</c>
/// （<c>MagiViewModel.Persistence.cs</c> で宣言済み）を再利用する（<c>MagiViewModel.Optimize.cs</c>
/// クラスKDocと同じ方針——複製すると <c>stop()</c>（未移植・別ピース）が正しいトークンを掴めなくなる）。
///
/// [finally が薄い理由] Kotlin原本の <c>generateSmartInitial</c> の <c>finally</c> は
/// <c>endBoardJob(boardToken)</c> のみ（<c>runV6FullOptimize</c> の
/// <c>terminalLogged</c>/<c>LiveSchedule</c>クリア/<c>running</c>強制解除は無い）。この移植も
/// Kotlin原本を忠実に踏襲し、意図的にこの非対称を保つ（HF77＝「直す」対象ではない）。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>[テスト可視性のための追加] 直近の <see cref="GenerateSmartInitial"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastGenerateSmartInitialTask { get; private set; }

    /// <summary>
    /// 初期解生成(賢い版)。Kotlin原本 <c>generateSmartInitial()</c>（969-1018行）の移植。
    /// </summary>
    /// <param name="confirmed">置き換えの確認（<see cref="UiState.DraftConfirm"/>）で「置き換える」を押した。</param>
    public void GenerateSmartInitial(bool confirmed = false)
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null) return;
        // 手直しや結果のある勤務表を黙って置き換えない。確認の文は置き換える前の必須違反を単位つきで言う。
        if (!confirmed && !Ui.Running && (Ui.HasResult || Ui.EngineRan || Ui.RanBeforeEdit || sched.Any(r => r.Any(v => v >= 0))))
        {
            // 下書きは決まった種で作る（同じ入力なら同じ盤面）＝先に 1 回だけ作り、確認に後の必須を出し、置き換えるときはその結果を使う。
            var before = Ui.HasResult ? AnalysisTriage.HardText(Ui.Breakdown, Ui.BestHard) : null;
            LastDraftPreviewTask = PrepareDraftConfirmAsync(st, sched.Copy2D(), before, ++_draftConfirmSeq);
            return;
        }
        // [3.692.0] 確認（3.679.0）を通ったかをログで読めるようにする。[3.697.0] 行は完了の後＝置き換える前と後の必須を並べる。
        var replacing = confirmed && Ui.DraftConfirm is not null;
        var hardBefore = Ui.BestHard;
        // [Android 3.727.3] 置き換える前の勤務表の値（あとの作成がこれに負けたら知らせて戻せるように）。検査済みのときだけ。
        var preDraft = replacing && Ui.CheckRev > 0 ? new PreDraftBoard(sched.Copy2D(), StateKey(st), Ui.BestHard, Ui.WeightedScore, Ui.TotalViolations, AnalysisTriage.HardText(Ui.Breakdown, Ui.BestHard)) : null;
        // [3.271.0, 実機ログ起因の由来をそのまま記録] 実行中ガード。旧: ガード無しのため
        //   「勤務表をつくる」の直後に隣接する本ボタンを連続タップすると、走行中の最適化と
        //   初期解生成が併走し、job 参照の上書き（走行中jobが停止不能のゾンビ化）と
        //   currentSchedule の同時書き換えが起きていた。runV6FullOptimize/runSoftPolish と同じガード。
        //   [Android 3.727.1] 確認のあとに別の実行が始まっていたら断るだけで確認は閉じない（確認を消してから断ると選び直せない）。
        if (RunBlockedByInFlight("下書きづくり")) return;
        if (Ui.DraftConfirm is not null) Ui.DraftConfirm = null;
        if (!EnsureValidForRun(st, sched, "下書きづくり")) return;
        PushUndo();
        var hadResult = Ui.HasResult;
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.HasResult = false;
        Ui.Message = "下書きをつくっています…";
        // [3.404.0の由来をそのまま記録] 完了時に currentSchedule/state を丸ごと差し替えるので、
        //   その間の編集を止める旗を立てる。
        var boardToken = BeginBoardJob("下書きづくり", engineRun: true);
        // [3.694.0] 開始行（#N 付き）に入力の期間・人数・指紋。下書きは読込を経由しないので、ここが盤面の出どころの記録になる。
        LogOp("I", $"初期解生成 開始 ・ {RunInputWords(st, sched)}");
        var cts = new CancellationTokenSource();
        _job = cts;
        LastGenerateSmartInitialTask = GenerateSmartInitialCoreAsync(st, sched.Copy2D(), _resultSchedule, hadResult, boardToken, cts.Token, confirmed, replacing ? hardBefore : null, preDraft);
    }

    /// <summary>[テスト可視性のための追加] 確認の前に下書きを作る Task。</summary>
    internal Task? LastDraftPreviewTask { get; private set; }

    /// <summary>確認のために先に作った下書き（作ったときの設定と盤面の指紋つき。違えば使わず作り直す）。</summary>
    private ((long, long) Keys, V6FinalPort.ActionResult Result)? _pendingDraft;

    private int _draftConfirmSeq;

    private async Task PrepareDraftConfirmAsync(MagiState st, int[][] sched, string? before, int seq)
    {
        var keys = (StateKey(st), BoardKey(sched));
        V6FinalPort.ActionResult? res = null;
        try { res = await Task.Run(() => V6FinalPort.HandleSmartInitial(st.WithSchedule(sched), allowImpossible: true)); }
        catch (Exception) { res = null; }
        // [Android 3.727.1] 作っている間に盤面・設定が変わった／別の実行が始まった／もう一度押された＝この確認は古い。出さない。
        if (seq != _draftConfirmSeq || _state is not { } nowSt || _currentSchedule is not { } nowSc ||
            keys != (StateKey(nowSt), BoardKey(nowSc)) || OptimizeInFlight()) return;
        _pendingDraft = res is null ? null : (keys, res);
        Ui.DraftConfirm = FixExplain.DraftConfirmText(before, res is null ? null : AnalysisTriage.HardText(res.Report.Breakdown, res.Report.Hard));
    }

    /// <summary>[Android 3.727.3] 下書きで置き換える前の勤務表（<see cref="AnalysisTriage.PreDraftBetter"/> で作成の結果と比べる）。設定が変われば使わない。</summary>
    private PreDraftBoard? _preDraft;

    /// <summary>[Android 3.727.3] 置き換え前の勤務表へ戻す（元に戻すで取り消せる。Kotlin <c>restorePreDraft</c>）。</summary>
    public void RestorePreDraft()
    {
        if (_preDraft is not { } p) return;
        if (_state is not { } st) return;
        if (RunBlockedByInFlight("置き換え前に戻す")) return;
        if (p.StateKey != StateKey(st)) { _preDraft = null; Ui.PreDraftNotice = null; return; }
        PushUndo();
        var board = p.Board.Copy2D();
        _currentSchedule = board;
        _state = st.WithSchedule(board);
        _resultSchedule = null;
        _preDraft = null;
        Ui.Schedule = board.Select(r => (IReadOnlyList<int>)r.ToList()).ToList();
        Ui.PreDraftNotice = null;
        PostOpNotice("置き換え前の勤務表に戻しました");
        LogOp("I", $"置き換え前の勤務表に戻す 必須={p.Hard} 重み={(long)p.Weighted} ・ 盤面指紋={OpLogText.Hex16(BoardKey(board))}");
        AutoSave();
        RefreshCheck();
    }

    /// <summary>[Android 3.727.3] 作成の結果を置き換え前の勤務表と比べる。結果が勝てば忘れ、負ければ知らせる（設定が変わっていれば捨てる）。</summary>
    internal void NotePreDraftAfterRun(ViolationReport adopted)
    {
        if (_preDraft is not { } p) return;
        var curKey = _state is { } st ? StateKey(st) : (long?)null;
        if (p.StateKey != curKey || AnalysisTriage.PreDraftBetter(p.Hard, p.Weighted, p.Total, adopted.Hard, adopted.WeightedScore, adopted.Total)) _preDraft = null;
        else if (AnalysisTriage.PreDraftBetter(adopted.Hard, adopted.WeightedScore, adopted.Total, p.Hard, p.Weighted, p.Total))
        {
            Ui.PreDraftNotice = AnalysisTriage.PreDraftNoticeText(p.HardWord, p.Weighted);
            LogOp("I", $"置き換え前の勤務表のほうが良い: 必須 {p.Hard}・重み {(long)p.Weighted}（今の結果 必須 {adopted.Hard}・重み {(long)adopted.WeightedScore}）");
        }
    }

    public void DismissDraftConfirm()
    {
        _pendingDraft = null;
        if (Ui.DraftConfirm is not null) LogOp("I", "下書きで置き換えない（確認で取りやめ）");
        Ui.DraftConfirm = null;
    }

    private async Task GenerateSmartInitialCoreAsync(MagiState st, int[][] sched, int[][]? result0, bool hadResult, int boardToken, CancellationToken ct, bool confirmed = false, long? replacingHard = null, PreDraftBoard? before = null)
    {
        var committed = false;   // 差し替え後・表示前。ここで中止・失敗したら下書き前へ戻す（ImportCsv と同じ）
        try
        {
            var keys = (StateKey(st), BoardKey(sched));
            var res = _pendingDraft is { } pd && pd.Keys == keys ? pd.Result
                : await Task.Run(() => V6FinalPort.HandleSmartInitial(st.WithSchedule(sched), allowImpossible: true), ct);
            _pendingDraft = null;
            _boardReplaceRev++;
            _currentSchedule = res.Schedule.Copy2D();
            AutoSave();
            _resultSchedule = res.Schedule.Copy2D();
            _state = st.WithSchedule(res.Schedule);
            committed = true;
            await PushReportAsync(_state ?? st, res.Schedule, res.Report, runLabel: "下書きづくり", transform: ui =>
            {
                ui.MessageIsError = false;
                ui.Running = false;
                ui.HasResult = true;
                ui.EngineRan = true;
                ui.RanBeforeEdit = false;
                ui.RelaxedBoard = false;
                ui.KeptPrevious = false;
                ui.ElapsedMs = 0;
                ui.Message = $"下書きをつくりました: {AnalysisTriage.ReportCountsText(res.Report.Breakdown)}。画面下の「再作成」で仕上げられます" + (confirmed ? $"。{FixExplain.DraftUndoNote}" : "");
            }, ct: ct);
            committed = false;
            LogOp("I", $"初期解生成 完了 必須={res.Report.Hard} 合計={res.Report.Total} ・ 盤面指紋={OpLogText.Hex16(BoardKey(res.Schedule))}");
            if (replacingHard is { } hb) LogOp("I", OpLogText.DraftReplaceLine(hb, res.Report.Hard));
            // 置き換えた勤務表を覚える。覚えている方が良ければそのまま。
            if (before is not null)
            {
                var prev = _preDraft is { } pd0 && pd0.StateKey == before.StateKey ? pd0 : null;
                if (prev is null || AnalysisTriage.PreDraftBetter(prev.Hard, prev.Weighted, prev.Total, before.Hard, before.Weighted, before.Total)) _preDraft = before;
            }
        }
        catch (OperationCanceledException)
        {
            // [3.404.0の由来をそのまま記録] 停止・ジョブ上書きを「失敗」と呼ばない。
            if (committed) RollbackBoardCommit(st, sched, result0);
            LogOp("I", "初期解生成 停止");
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.HasResult = hadResult;
            Ui.Message = "下書きづくりを停止しました";
            throw;
        }
        catch (Exception e)
        {
            if (committed) RollbackBoardCommit(st, sched, result0);
            // [3.271.0の由来をそのまま記録] 失敗を操作ログにも残す。
            LogOp("W", $"初期解生成 失敗: {e.GetType().Name}: {e.Message}");
            Ui.Running = false;
            Ui.HasResult = hadResult;
            Ui.Message = $"下書きをつくれませんでした（{FailureWords.Of(e, FailureKind.Engine)}）。もう一度お試しください（詳しくは設定＞詳細設定＞ログ）";
            Ui.MessageIsError = true;
        }
        finally
        {
            EndBoardJob(boardToken);
        }
    }
}
