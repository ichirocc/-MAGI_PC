using System.Threading;
using System.Threading.Tasks;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9 ピース7] <c>MagiViewModel.kt</c> の永続化/入出力サブシステムの移植——
/// <c>autoSave</c>/<c>saveNow</c>（自動保存・即時保存）、<c>undo</c>/<c>redo</c>（公開の入口。
/// データ構造自体はピース5の <see cref="PushUndo"/>/<see cref="SnapNow"/> で移植済み）、
/// <c>load</c>/<c>initBlankState</c>/<c>loadAsync</c>/<c>restorePreviousData</c>（読込・復元）、
/// <c>validate</c>/<c>ensureValidForRun</c>（構造検証）、<c>refreshCheck</c>（違反チェックの再計算）、
/// <c>runBlockedByInFlight</c>/<c>notify</c>/<c>clearMessage</c>（実行中ガード・返事）、
/// <c>exportJson</c>（JSON書き出し）を担う。
///
/// [Job? → CancellationTokenSource? + Task の対応] Kotlin原本は <c>viewModelScope.launch{}</c> が
/// 返す <c>Job?</c> フィールド（<c>job</c>/<c>checkJob</c>/<c>saveJob</c>）に「実行中の非同期処理」を
/// 保持し、新しい実行を始める前に <c>?.cancel()</c> して前の実行を協調的にキャンセルする
/// （fire-and-forget＋自己置換パターン）。この移植では対応する各フィールドを
/// <c>CancellationTokenSource?</c> とし、同じ自己置換パターン（新しい呼出しが前の CTS を
/// <c>Cancel()</c> してから新しい CTS を発行する）で表現する。加えて、Kotlin の <c>Job</c> は
/// テストから完了を待つ手段（ViewModel スコープ外からの <c>join()</c>）を持たないが、この移植は
/// プラットフォーム非依存で初めてテスト可能になった（クラスKDoc各所に記録済みの確立済み理由）ため、
/// 各非同期処理が返す <see cref="Task"/> を <c>internal</c> プロパティ（<see cref="LastLoadTask"/> 等）
/// として公開し、テストが決定的に完了を待てるようにする——Kotlin原本には存在しない追加だが、
/// 挙動そのもの（fire-and-forget＋自己キャンセル）は変えていない。
///
/// [CancellationTokenSource の明示的 Dispose を行わない方針] このピースの各 CTS フィールドは
/// 意図的に <c>Dispose()</c> しない。理由: ①<c>Cancel()</c> 呼出し後すぐに <c>Dispose()</c> すると、
/// 別スレッドで進行中の <c>Cancel()</c>/コールバック実行と競合しうる（<c>Dispose</c>-after-<c>Cancel</c>
/// レース）②<c>CancellationTokenSource</c> はアンマネージドリソースを持たない限り、GC/ファイナライズで
/// 適切に回収される（Kotlin原本の <c>Job</c> オブジェクトが明示的な破棄を要求しないのと同じ扱い）。
/// これは単純化として意図的に受け入れる（Kotlin原本にも対応する破棄処理は存在しない）。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>
    /// [テスト可視性のためinternal化] Kotlin原本は <c>private var hydrated = false</c> で、
    /// <c>init{}</c> の起動時復元が完了した時点で true へ立てる唯一の書き手。このC#移植では
    /// Phase 10 で <see cref="RestoreOnStartup"/>（<c>MagiViewModel.RunMarker.cs</c>）が
    /// その唯一の書き手になった。<see cref="AutoSave"/>/<see cref="SaveNow"/> の no-op ガードを
    /// テストから直接運動できるよう internal のままにしておく。
    /// </summary>
    internal bool _hydrated;

    /// <summary>
    /// [プラットフォーム非依存化] Kotlin原本の <c>getApplication&lt;Application&gt;().filesDir</c> は
    /// Android の per-app 永続化ディレクトリ。WinUI3固有の <c>ApplicationData.Current.LocalFolder</c>
    /// に相当するが、このプロジェクト（MagiApp.ViewModels, net8.0・Windows App SDK 非依存）からは
    /// 参照できない。差し替え可能なプロパティとして公開し、既定値は
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/> 配下の "Magi" ディレクトリとする。
    /// WinUI3シェル側（後続フェーズ）が実際のパッケージ格納庫を注入できるようにする設計。
    /// ディレクトリ自体の作成は <see cref="AtomicFileWrite.WriteFileAtomically"/> 側に遅延する
    /// （プロパティ参照だけでは副作用を起こさない）。
    /// </summary>
    public string DataDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Magi");

    private string AutosaveFile => Path.Combine(DataDir, "magi_autosave.json");
    private string PrevBackupFile => Path.Combine(DataDir, "magi_prev_before_open.json");

    // ===== 自動保存/即時保存 =====

    /// <summary>直前の自動保存が成功したか。失敗を連続で通知しないための状態。</summary>
    private bool _autoSaveOk = true;

    /// <summary>
    /// [3.428.0/#7 相当] 原子置換を諦めた（rename 不能）ことを書込側から受け取る旗。
    /// Kotlin原本は <c>Dispatchers.IO</c> から立つため <c>@Volatile</c>——この移植でも
    /// <see cref="AtomicFileWrite.WriteFileAtomically"/> の <c>onNonAtomic</c> コールバックが
    /// バックグラウンドの <see cref="Task.Run"/> 内から書くため <c>volatile</c> とする。
    /// </summary>
    private volatile bool _nonAtomicSaveSeen;

    /// <summary>一度だけ記録したか（1.2秒ごとに走るので毎回は出さない）。</summary>
    private bool _nonAtomicSaveLogged;

    private CancellationTokenSource? _saveCts;

    /// <summary>
    /// [レビュー指摘 2026-09-04] 保存の世代番号。<c>_saveCts.Cancel()</c> は**既に始まった書き込みを止められない**
    /// ので、古い自動保存（状態A）の書き込みが、後から始まった <see cref="SaveNow"/>（状態B）の後に完了すると
    /// 自動保存ファイルが A へ戻る（原子置換は破損を防ぐが順序の逆転は防げない）。
    /// 書き手は main で世代を採番し（<c>ExportJson</c> と同じ時点＝状態の順序と一致）、
    /// <see cref="WriteAutosaveIfLatest"/> がロック下で「より新しい世代が書かれた後の古い世代」を捨てる。
    /// </summary>
    private int _saveGen;
    private int _lastWrittenGen;
    private readonly object _saveLock = new();

    /// <summary>
    /// 世代 <paramref name="gen"/> の JSON を、より新しい世代がまだ書かれていない場合だけ書く。
    /// 戻り値: 書いた=true／書き込み失敗=false／古い世代なので捨てた=null。ロックで直列化するため
    /// 「確認→書き込み」の間に別の書き手が割り込むことはない。
    /// </summary>
    internal bool? WriteAutosaveIfLatest(int gen, string json)
    {
        lock (_saveLock)
        {
            if (gen < _lastWrittenGen) return null;
            bool ok;
            try
            {
                ok = AtomicFileWrite.WriteFileAtomically(AutosaveFile, json, onNonAtomic: () => _nonAtomicSaveSeen = true);
            }
            catch
            {
                ok = false;
            }
            if (ok) _lastWrittenGen = gen;
            return ok;
        }
    }

    /// <summary>[テスト可視性のための追加] 直近の <see cref="AutoSave"/> 呼出しが背後で走らせる
    /// Task。Kotlin原本の <c>saveJob</c> に相当するが、テストが完了を決定的に待てるよう公開する
    /// （クラスKDoc参照）。</summary>
    internal Task? LastAutoSaveTask { get; private set; }

    /// <summary>
    /// デバウンス付き自動保存（1.2秒後に書込）。onStop/onPause 等から呼ばれる想定。
    /// hydrated（起動時復元完了）前は no-op——復元前に空のドラフトで自動保存を上書きしないため。
    /// </summary>
    private void AutoSave()
    {
        if (!_hydrated) return;
        _saveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _saveCts = cts;
        LastAutoSaveTask = AutoSaveCoreAsync(cts.Token);
    }

    private async Task AutoSaveCoreAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(1200, ct);
            var gen = Interlocked.Increment(ref _saveGen);   // ExportJson の時点の状態順。UI スレッド直列が前提だが、テスト等の別コンテキストでも世代が重ならないよう原子加算にする
            var json = ExportJson();
            if (json is null) return;
            var result = await Task.Run(() => WriteAutosaveIfLatest(gen, json), ct);
            if (result is null) return;   // より新しい世代が先に書かれた＝この世代は捨てる（通知しない）
            var ok = result.Value;
            // [3.428.0/#7 相当] 記録は main（このコルーチン継続）へ戻ってから行う——LogOp は
            // 共有可変状態(_opLog)とUIプロパティを変更するため、バックグラウンドスレッドから
            // 直接呼ばない（3.176.0の由来コメント参照）。
            ReportNonAtomicSave();
            ReportAutoSave(ok);
        }
        catch (OperationCanceledException)
        {
            // 新しい AutoSave() 呼出しに置き換えられた（あるいは SaveNow() に先を越された）。
            // Kotlin原本の `saveJob?.cancel()` と同じ協調キャンセルで、エラーではない。
        }
    }

    /// <summary>
    /// rename が使えず**原子性を諦めて直接書いた**ことを記録する。書き込みは成功しうるので失敗としては
    /// 扱わないが、この経路で書いている最中にプロセスが落ちると壊れた自動保存が残る
    /// （原子置換を入れた動機そのもの）。
    /// </summary>
    internal void ReportNonAtomicSave()
    {
        if (_nonAtomicSaveLogged || !_nonAtomicSaveSeen) return;
        _nonAtomicSaveLogged = true;
        LogOp("W", "自動保存で原子置換（一時ファイルの差し替え）が使えず直接書き込みました" +
            "（書き込み中にアプリが強制終了すると自動保存が壊れる可能性があります）");
    }

    internal void ReportAutoSave(bool ok)
    {
        if (ok == _autoSaveOk) return;
        _autoSaveOk = ok;
        if (ok) LogOp("I", "自動保存が復旧しました");
        else Notify("自動保存に失敗しています（端末の空き容量をご確認ください）。「データを保存」で書き出してください", "W");
    }

    /// <summary>
    /// 即時保存（デバウンスなし・同期書込）。バックグラウンド遷移から呼び、保留中の編集を確実に
    /// 永続化する。autoSave の1200msデバウンス中にプロセスが破棄されても編集が失われないための保険。
    /// [saveNowメインスレッドI/O] 意図的な同期I/O（<see cref="SaveNowSlowMs"/> のKDoc参照）を前提の
    /// まま残すが、想定外に長く塞いだ回だけ観測できるようにする（表示・エンジンは不変）。
    /// </summary>
    public void SaveNow()
    {
        if (!_hydrated) return;
        _saveCts?.Cancel();
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        var gen = Interlocked.Increment(ref _saveGen);
        var json = ExportJson();
        if (json is null) return;
        // 同期呼出しなので世代は常に最新＝null（捨て）にはならないが、走行中の自動保存とはロックで直列化される。
        var ok = WriteAutosaveIfLatest(gen, json) ?? true;
        // saveNow は同期なので旗を立てたその場で記録して構わない。
        ReportNonAtomicSave();
        ReportAutoSave(ok);
        var ms = t0.ElapsedMilliseconds;
        if (ms >= SaveNowSlowMs) LogOp("W", $"即時保存に{ms}ms（想定より遅い。端末のストレージ負荷をご確認ください）");
    }

    // ===== 元に戻す/やり直す（公開の入口） =====

    /// <summary>直前の編集・取込・計算開始前の状態へ戻す（最大30段）。現在状態は redo へ退避。</summary>
    public void Undo()
    {
        // [3.328.0の意図を保ったまま簡略化] Kotlin原本は `job?.isActive == true || optimizeInFlight()`。
        // `job`（前景ジョブ）を設定する全5箇所（loadAsync/下書きづくり/勤務表づくり/仕上げ最適化/
        // CSV取込）は必ず先に `beginBoardJob(...)` を呼んでおり、`job` の「活動中」区間は
        // `OptimizeInFlight()` の第1項（_boardJobLabel is not null）の区間の真部分集合になる——
        // よって `job?.isActive` の項は論理的に冗長。挙動を変える改善ではなく、既に成立している
        // 契約を明示するための簡略化。
        if (OptimizeInFlight()) return;
        var lastNode = _undoStack.Last;
        if (lastNode is null) return;
        _undoStack.RemoveLast();
        var snap = lastNode.Value;
        var cur = SnapNow();
        if (cur is not null) _redoStack.AddLast(cur);
        _state = snap.State;
        _currentSchedule = snap.Schedule.Copy2D();
        // 元に戻す/やり直しは手操作＝「計算済み」ではない。前の結果盤面と改善提案は、この盤面とは別の実体なので外す
        // （提案は指紋照合でも弾かれるが、画面に古い候補を残さない）。
        _resultSchedule = null;
        Ui.EngineRan = false;
        Ui.FixSuggestions = System.Array.Empty<MagiEngine.V6.FixSuggestion>();
        Ui.MessageIsError = false;
        Ui.StructureEdited = true;
        Ui.CanUndo = _undoStack.Count > 0;
        Ui.CanRedo = true;
        Ui.Message = "1つ前に戻しました";
        LogOp("I", "元に戻す");
        RefreshCheck();
        AutoSave();
    }

    /// <summary>元に戻した操作をやり直す（手動修正のループ：修正→戻す→やり直し、を支える）。</summary>
    public void Redo()
    {
        if (OptimizeInFlight()) return; // 上記 Undo() と同じ簡略化の根拠。
        var lastNode = _redoStack.Last;
        if (lastNode is null) return;
        _redoStack.RemoveLast();
        var snap = lastNode.Value;
        var cur = SnapNow();
        if (cur is not null) _undoStack.AddLast(cur);
        _state = snap.State;
        _currentSchedule = snap.Schedule.Copy2D();
        // 元に戻す/やり直しは手操作＝「計算済み」ではない。前の結果盤面と改善提案は、この盤面とは別の実体なので外す
        // （提案は指紋照合でも弾かれるが、画面に古い候補を残さない）。
        _resultSchedule = null;
        Ui.EngineRan = false;
        Ui.FixSuggestions = System.Array.Empty<MagiEngine.V6.FixSuggestion>();
        Ui.MessageIsError = false;
        Ui.StructureEdited = true;
        Ui.CanUndo = true;
        Ui.CanRedo = _redoStack.Count > 0;
        Ui.Message = "やり直しました";
        LogOp("I", "やり直し");
        RefreshCheck();
        AutoSave();
    }

    // ===== 読込/復元 =====

    public void Load(string json, string note = "") => LoadAsync(json, note: note);

    /// <summary>
    /// [⛏6相当] ゼロから作る起点。最小の有効データ(1シフト/1グループ/1スタッフ/31日)を既存の
    /// Load() 経路(StateJsonSerializer.Parse→Validate→Problem→MakeUi)にそのまま流す。
    /// サンプルと同じ構造を最小化したものなので、専用の初期化ロジックを持たず実行時の不整合
    /// リスクを抑える。読込後はユーザーが編集画面（年次マスター）でシフト/グループ/スタッフを
    /// 一括追加して育てる想定。
    /// </summary>
    public void InitBlankState()
    {
        const int days = 31;
        var sched = string.Join(",", Enumerable.Repeat("0", days));
        var seed =
            "{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\"," +
            "\"shifts\":[{\"name\":\"休み\",\"kigou\":\"休\",\"need1\":\"\",\"need2\":\"\"}]," +
            "\"groups\":[{\"name\":\"グループA\",\"kigou\":\"A\"}]," +
            "\"staff\":[{\"name\":\"職員1\",\"groupIdx\":0}]," +
            "\"use2Patterns\":true," +
            "\"groupShift\":[[1]],\"groupShiftApt\":[[\"\"]]," +
            "\"cons1\":[],\"cons2\":[],\"cons3\":[],\"cons3n\":[],\"cons3m\":[],\"cons3mn\":[],\"cons41\":[],\"cons42\":[]," +
            "\"wishes\":{},\"staffRange\":{},\"needDay1\":{},\"needDay2\":{}," +
            $"\"schedule\":[[{sched}]]}}";
        Load(seed);
    }

    /// <summary>
    /// [Result&lt;LoadedProblem&gt;.fold の置き換え] Kotlin原本の loadAsync は「構造検証の失敗」を
    /// <c>Result.failure(IllegalArgumentException(it))</c> として明示的に構築し、
    /// <c>loaded.fold(onSuccess, onFailure)</c> の onFailure 分岐（LogOp を呼ぶ）へ流す。それ以外の
    /// 例外（JSON構文エラー等、StateJsonSerializer.Parse や Problem 構築が投げるもの）は
    /// withContext を素通りして外側の <c>catch (e: Throwable)</c>（LogOp を**呼ばない**）へ落ちる。
    /// この非対称は Kotlin原本の逐語的な読みであり、意図的に保存する（BCLの汎用例外を流用すると、
    /// 無関係な内部例外が誤って onFailure 経路へ紛れ込みうるため、専用の例外型で区別する）。
    /// </summary>
    internal sealed class StateValidationException : Exception
    {
        public StateValidationException(string message) : base(message) { }
    }

    private sealed record LoadedProblem(MagiState State, int[][] Schedule, ViolationReport Report);

    private CancellationTokenSource? _job;

    /// <summary>[テスト可視性のための追加] 直近の <see cref="LoadAsync"/> 呼出しが背後で走らせる
    /// Task（クラスKDoc参照）。</summary>
    internal Task? LastLoadTask { get; private set; }

    /// <param name="note">[3.414.0/I-02相当] 読込完了メッセージの末尾へ足す一言。期間を推定している
    /// 呼出元がその事実を利用者へ届けるための唯一の口。既定は空。</param>
    /// <param name="fromRestore">起動時の復元だけが渡す想定（Phase 10）。背景実行の最中にアプリが
    /// 起動して state を復元するのは正常な経路なので、その場合だけ実行中ガードを迂回する。</param>
    public void LoadAsync(string rawJson, bool markResult = false, bool fromRestore = false, string note = "")
    {
        if (!fromRestore && RunBlockedByInFlight("読み込み")) return;
        var json = MojibakeRepair.Repair(rawJson);
        // [3.282.0相当] 旧: 参照比較のため BOM 除去だけの健全なファイルでも毎回「文字化けを自動修復」と
        //   誤警告していた。実際に二重エンコードを復号したときだけ警告し、元ファイル自体は直らない
        //   （再取込のたび修復が走る）ことも案内する。
        var repaired = MojibakeRepair.WasDecoded(rawJson, json);
        _job?.Cancel();
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.Message = "読込中…";
        // [3.404.0相当] 読み込みも「完了時に state と勤務表を丸ごと差し替える」ジョブ＝その間の編集を止める。
        var boardToken = BeginBoardJob("読み込み");
        var cts = new CancellationTokenSource();
        _job = cts;
        LastLoadTask = LoadAsyncCoreAsync(json, repaired, markResult, note, boardToken, cts.Token);
    }

    private async Task LoadAsyncCoreAsync(
        string json, bool repaired, bool markResult, string note, int boardToken, CancellationToken ct)
    {
        try
        {
            if (repaired)
            {
                LogOp("W", "文字化け（二重エンコード）を自動修復して読み込みました。元のファイル自体は修復されません" +
                    "（「データを保存」で保存し直すと次回からこの警告は出ません）");
            }

            LoadedProblem lp;
            string? endDateFixedFrom = null;   // [レビュー指摘 2026-09-04] EndDate を日数に合わせて補正したときの旧値
            var normalizedOnLoad = false;      // [自己見直し 2026-09-04] 読込時の正規化で state が差し替わったか
            try
            {
                lp = await Task.Run(() =>
                {
                    var parsed = StateJsonSerializer.Parse(json);
                    // EndDate と日数の食い違いは検証を通り抜けていた＝日数を正として EndDate を揃える。
                    var st0 = Ws1Ops.NormalizeEndDate(parsed);
                    if (st0.EndDate != parsed.EndDate) endDateFixedFrom = parsed.EndDate;
                    var err = Validate(st0);
                    if (err is not null) throw new StateValidationException(err);
                    // [レビュー指摘 2026-09-04] 検証を通ったあとで GroupShiftApt を G×K に揃える（空配列・行不足は空欄）。
                    var st = Ws1Ops.NormalizeGroupShiftApt(st0);
                    normalizedOnLoad = !ReferenceEquals(st, parsed);
                    var p = new Problem(st);
                    var init = p.InitialAssignment();
                    var report = UnifiedViolationChecker.Check(st, init);
                    return new LoadedProblem(st, init, report);
                }, ct);
            }
            catch (StateValidationException err)
            {
                // Kotlin原本の onFailure 分岐——構造検証の失敗だけがここへ来る（他の例外は下の
                // 外側 catch へ落ちる。このメソッドのクラスKDoc参照）。
                LogOp("W", $"読込失敗: {err.GetType().Name}: {err.Message}");
                Ui.Running = false;
                Ui.Message = $"読み込めませんでした（{err.GetType().Name}）。ファイルの中身を確認してください";
                Ui.MessageIsError = true;
                return;
            }

            // [判断設計監査 #3相当] 「データを開く」直前の状態を1世代退避。「開く前のデータに戻す」
            //   （RestorePreviousData）で往復できる（戻す操作自体も退避を挟む＝スワップ）。
            var prevJson = _state is not null ? ExportJson() : null;
            var prevSaved = false;
            if (prevJson is not null)
            {
                prevSaved = await Task.Run(() =>
                {
                    try
                    {
                        return AtomicFileWrite.WriteFileAtomically(PrevBackupFile, prevJson);
                    }
                    catch
                    {
                        return false;
                    }
                }, ct);
            }

            if (endDateFixedFrom is not null)
                LogOp("W", $"期間の終了日（endDate）が日数と合っていなかったため補正しました（{endDateFixedFrom} → {lp.State.EndDate}）。「データを保存」で保存し直すと次回からこの警告は出ません");
            // [自己見直し 2026-09-04] 旧: 正規化（EndDate 補正・GroupShiftApt の G×K 化）をしても _originalJson は
            //   **生のファイル**のままで、StructureEdited=false の ExportJson はその生 JSON に schedule だけ差し込んで
            //   返す＝直後の AutoSave も「データを保存」も補正前の endDate を書き戻し、警告文の「保存し直すと
            //   次回から出ません」が嘘だった。正規化したときだけ、正規化後の state を Serialize したものを原本にする。
            _originalJson = normalizedOnLoad ? StateJsonSerializer.Serialize(lp.State, lp.Schedule) : json;
            _state = lp.State.WithSchedule(lp.Schedule);
            _currentSchedule = lp.Schedule.Copy2D();
            // [bg復元相当] markResult=true は「バックグラウンド最適化の結果 JSON」の読込。schedule が
            //   結果そのものなので resultSchedule/hasResult を立て、上位バーの「未計算」表示を防ぐ。
            _resultSchedule = markResult ? lp.Schedule.Copy2D() : null;
            ClearUndo();
            AutoSave();
            await PushReportAsync(lp.State, lp.Schedule, lp.Report, transform: ui =>
            {
                ui.MessageIsError = false;
                ui.Loaded = true;
                ui.Running = false;
                ui.HasResult = markResult;
                ui.EngineRan = markResult;
                ui.ConstraintsEdited = false;
                ui.StructureEdited = false;
                ui.Staff = lp.State.StaffCount;
                ui.Days = lp.State.DayCount;
                ui.Shifts = lp.State.ShiftCount;
                ui.Groups = lp.State.GroupCount;
                ui.Use2 = lp.State.Use2Patterns;
                ui.InitHard = lp.Report.Hard;
                ui.InitSoft = lp.Report.Soft;
                ui.ElapsedMs = 0;
                // [3.289.0相当] 書込が実際に成功したときだけ立てる（既存の退避があれば維持）。
                ui.PrevBackupAvailable = prevSaved || ui.PrevBackupAvailable;
                ui.Message = $"読込完了: {lp.State.StaffCount}名 / {lp.State.DayCount}日 / {lp.State.ShiftCount}シフト{note}";
            }, ct: ct);
            LogOp("I", $"読込 {lp.State.StaffCount}名/{lp.State.DayCount}日/{lp.State.ShiftCount}シフト");
        }
        catch (OperationCanceledException)
        {
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.Message = "読み込みを中止しました"; // 停止は失敗ではない。
            throw;
        }
        catch (Exception e)
        {
            Ui.Running = false;
            Ui.Message = $"読み込めませんでした（{e.GetType().Name}）。ファイルの中身を確認してください";
            Ui.MessageIsError = true;
        }
        finally
        {
            EndBoardJob(boardToken);
        }
    }

    /// <summary>[テスト可視性のための追加] 直近の <see cref="RestorePreviousData"/> 呼出しが背後で
    /// 走らせる Task（クラスKDoc参照）。</summary>
    internal Task? LastRestorePreviousDataTask { get; private set; }

    /// <summary>
    /// [判断設計監査 #3相当] 「データを開く」直前に退避した1世代前の状態へ戻す。LoadAsync 経由のため
    /// 現在のデータが再び退避される＝もう一度押すと元へ戻る（スワップ）。
    /// </summary>
    public void RestorePreviousData()
    {
        if (OptimizeInFlight())
        {
            Ui.MessageIsError = true;
            Ui.Message = $"{BusyWhat()}の実行中は操作できません";
            return;
        }
        LastRestorePreviousDataTask = RestorePreviousDataCoreAsync();
    }

    private async Task RestorePreviousDataCoreAsync()
    {
        var txt = await Task.Run(() =>
        {
            try
            {
                return File.Exists(PrevBackupFile) ? File.ReadAllText(PrevBackupFile) : null;
            }
            catch
            {
                return null;
            }
        });
        if (string.IsNullOrWhiteSpace(txt))
        {
            Ui.MessageIsError = false;
            Ui.Message = "開く前のデータの退避がありません";
            return;
        }
        LogOp("I", "開く前のデータに戻します（もう一度押すと入れ替わります）");
        LoadAsync(txt);
    }

    // ===== 構造検証 =====

    /// <summary>状態が構造的に妥当かを検証し、妥当でなければ利用者向けの理由文字列を返す。
    /// [テスト可視性のためinternal化] Kotlin原本は private fun。</summary>
    internal static string? Validate(MagiState st)
    {
        if (st.StaffCount == 0) return "staff が空です";
        // [レビュー指摘 2026-09-04] 読めない startDate を受理すると Problem.Dow0 が黙って日曜へ落ちる。
        if (Ws1Ops.StartDateError(st) is { } sdErr) return sdErr;
        if (st.DayCount == 0) return "schedule が空です";
        if (st.ShiftCount == 0) return "shifts が空です";
        if (st.GroupCount == 0) return "groups が空です";
        if (st.Schedule.Count != st.StaffCount) return "schedule の行数が staff 数と一致しません";
        if (st.GroupShift.Count < st.GroupCount) return "groupShift の行数が groups より少ないです";
        for (var g = 0; g < st.GroupShift.Count; g++)
        {
            var row = st.GroupShift[g];
            if (row.Count < st.ShiftCount) return $"groupShift[{g}] の列数が shifts より少ないです";
            if (!row.Take(st.ShiftCount).Any(v => v == 1)) return $"groupShift[{g}] に担当可能シフトがありません";
        }
        for (var g = 0; g < st.GroupShiftApt.Count; g++)
        {
            var row = st.GroupShiftApt[g];
            if (g < st.GroupCount && row.Count > 0 && row.Count < st.ShiftCount)
                return $"groupShiftApt[{g}] の列数が shifts より少ないです";
        }
        for (var i = 0; i < st.StaffList.Count; i++)
        {
            var s = st.StaffList[i];
            if (s.GroupIdx < 0 || s.GroupIdx >= st.GroupCount) return $"staff[{i}].groupIdx が範囲外です ({s.GroupIdx})";
        }
        for (var i = 0; i < st.Schedule.Count; i++)
        {
            var row = st.Schedule[i];
            if (row.Count != st.DayCount) return $"schedule[{i}] の日数が不揃いです";
            for (var j = 0; j < row.Count; j++)
            {
                var k = row[j];
                if (k != -1 && (k < 0 || k >= st.ShiftCount)) return $"schedule[{i}][{j}] のシフト番号が範囲外です ({k})";
            }
        }
        return null;
    }

    /// <summary>
    /// [native堅牢化相当] 最適化・生成の実行前に構造を検証する。期間/スタッフ/シフトの不整合や
    /// 未割当グループ・範囲外シフト等があれば、クラッシュさせず理由を表示して中止する。
    /// [テスト可視性のためinternal化] Kotlin原本は private fun。
    /// </summary>
    internal bool EnsureValidForRun(MagiState st, int[][] sched)
    {
        var err = Validate(st.WithSchedule(sched));
        if (err is null) return true;
        Ui.MessageIsError = true;
        Ui.Running = false;
        Ui.Message = $"実行できません: {err}。編集内容を確認してください";
        return false;
    }

    // ===== 違反チェックの再計算 =====

    private CancellationTokenSource? _checkCts;

    /// <summary>[テスト可視性のための追加] 直近の <see cref="RefreshCheck"/> 呼出しが背後で走らせる
    /// Task（クラスKDoc参照）。</summary>
    internal Task? LastRefreshCheckTask { get; private set; }

    public void RefreshCheck()
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule?.Copy2D();
        if (sched is null) return;
        var seq = ++_checkSeq;
        _checkCts?.Cancel();
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.Message = "違反チェック中…";
        var cts = new CancellationTokenSource();
        _checkCts = cts;
        LastRefreshCheckTask = RefreshCheckCoreAsync(st, sched, seq, cts.Token);
    }

    private async Task RefreshCheckCoreAsync(MagiState st, int[][] sched, long seq, CancellationToken ct)
    {
        try
        {
            // [Task.Run による明示的な背景ディスパッチ] Kotlin原本の handleCheck は
            // `suspend fun handleCheck(...) = withContext(Dispatchers.Default) { ... }` で背景
            // ディスパッチが内部に埋め込まれているが、このC#移植の V6FinalPort.HandleCheck は
            // 同期メソッドのまま（移植時にラップしない設計を選んだ）。呼び出し側であるここが
            // Task.Run で明示的にディスパッチすることで、Kotlin原本と同じ「背景スレッドで走る」
            // 性質を保つ。
            var res = await Task.Run(() => V6FinalPort.HandleCheck(st, sched), ct);
            if (seq != _checkSeq) return; // [review #6相当] a newer check started; drop stale result
            var hard = res.Report.Hard;
            var total = res.Report.Total;
            await PushReportAsync(st, res.Schedule, res.Report, transform: ui =>
            {
                ui.MessageIsError = false;
                // [3.328.0相当] 最適化が動いていれば実行中のまま。旧: 無条件に false で、
                //   最適化中の設定編集→検査完了で全ガードが素通りになっていた。
                ui.Running = OptimizeInFlight();
                ui.Message = $"違反チェック完了: 必須={hard} 合計={total}";
            }, ct: ct);
            LogOp("I", $"違反チェック 必須={hard} 合計={total}");
        }
        catch (OperationCanceledException)
        {
            // [3.284.0/外部レビューHigh③ 相当] 停止時の running 固着を解消。新しいチェックによる
            //   キャンセル（seq != _checkSeq＝後続が直後に running=true を立て直す）では触らず、
            //   明示停止によるキャンセルのときだけ実行中表示を戻す。
            if (seq == _checkSeq)
            {
                Ui.MessageIsError = false;
                Ui.Running = OptimizeInFlight();
                Ui.Message = "違反チェックを停止しました";
            }
            throw;
        }
        catch (Exception e)
        {
            // [3.392.0/3.400.0相当] Error まで拾う。旧: Exception だけで running=true が固着すると、
            //   running を根拠にした編集ガードが全て閉じたまま＝アプリが読取専用になった。
            //   毎回のセル編集で走る RefreshCheck は必ず痕跡を残す。
            LogOp("W", $"違反チェック 失敗: {e.GetType().Name}: {e.Message}");
            if (seq == _checkSeq)
            {
                Ui.Running = OptimizeInFlight();
                Ui.Message = $"違反チェックに失敗しました（{e.GetType().Name}）";
                Ui.MessageIsError = true;
            }
        }
    }

    // ===== 実行中ガード・返事 =====

    /// <summary>
    /// [3.383.0相当] 実行中に別の実行を頼まれて黙って無視したことを記録する。押した痕跡を必ず
    /// 操作ログへ残す（「押したのに何も起きない」を「実行が重なった」と区別できるようにする）。
    /// </summary>
    internal bool RunBlockedByInFlight(string what)
    {
        if (!OptimizeInFlight()) return false;
        LogOp("W", $"{what} を取り消しました（{BusyWhat()}が実行中）");
        Ui.MessageIsError = true;
        Ui.Message = $"{BusyWhat()}の実行中です。終わるか「やめる」を押してからにしてください。";
        return true;
    }

    /// <summary>
    /// 画面へ1行の返事を出す。ファイルの読み書きのように ViewModel の外で完結する操作が
    /// 結果を返すための入口。level は操作ログの水準（既定 I。失敗は W）。
    /// </summary>
    public void Notify(string text, string level = "I")
    {
        LogOp(level, text);
        Ui.Message = text;
        Ui.MessageIsError = level == "W";
    }

    /// <summary>
    /// 直近メッセージを消す。shown を渡すと**それがまだ表示中のときだけ**消す（compare-and-clear）。
    /// 表示し終えたあとに素で消すと、その間に届いた新しいメッセージまで消してしまうため。
    /// </summary>
    public void ClearMessage(string? shown = null)
    {
        if (shown is null || Ui.Message == shown)
        {
            Ui.Message = null;
            Ui.MessageIsError = false;
        }
    }

    // ===== JSON書き出し =====

    /// <summary>現在のJSONを書き出す。年次マスター編集 -> 全体シリアライズ、制約編集 -> 制約のみ上書き、
    /// それ以外 -> 盤面のみ上書き。</summary>
    public string? ExportJson()
    {
        var sched = _currentSchedule ?? _resultSchedule;
        if (sched is null) return null;
        var st = _state;
        if (Ui.StructureEdited && st is not null) return StateJsonSerializer.Serialize(st, sched);
        var orig = _originalJson;
        if (orig is null) return null;
        return Ui.ConstraintsEdited && st is not null
            ? StateJsonSerializer.ExportWithEdits(orig, st, sched)
            : StateJsonSerializer.ExportWithSchedule(orig, sched);
    }
}
