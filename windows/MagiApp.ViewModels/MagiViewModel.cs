using MagiApp.ViewModels.Services;
using MagiApp.ViewModels.Work;
using MagiEngine;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9] <c>MagiViewModel.kt</c>（3,495行・約130メンバ関数）の移植先。規模が大きいため、
/// 確立済みのコミット単位（「フェーズ9 ピースN」）に合わせて責務のまとまりごとに段階的に移植する。
///
/// このファイル（ピース5）が担う範囲——ファイルI/O・コルーチン(async)・WorkManager相当の
/// バックグラウンド実行のいずれにも依存しない、純粋な状態管理サブシステム:
///  - 中核の可変状態（<see cref="Ui"/> と、Compose の <c>_ui.update{it.copy(...)}</c> に対応する
///    「盤面/入力そのもの」を保持するプライベートフィールド群）
///  - 「盤面を丸ごと差し替える前景ジョブ」の排他制御（<c>boardJobLabel</c>/<c>optimizeInFlight</c>）
///  - 操作ログ（監査用リングバッファ）
///  - 元に戻す/やり直すの**データ構造**（<c>pushUndo</c>/<c>clearUndo</c>/<c>snapNow</c> — ただし
///    公開の <c>undo()</c>/<c>redo()</c> 自体は <c>refreshCheck()</c>/<c>autoSave()</c>（後続ピースで
///    移植する非同期処理）を呼ぶため、このピースにはまだ含めない）
///  - 単純な同期設定セッター（並列数・予算秒数・計算方式 等）
///
/// [背景実行の切り分け] Kotlin原本の <c>OptimizationRepository</c>（<c>work/OptimizationRepository.kt</c>）
/// はAndroid/WorkManagerに一切依存しない純粋な状態ブリッジであることが判明したため、
/// <see cref="Work.OptimizationRepository"/> として今回**先行して完全移植**した（詳細は同クラスの
/// KDoc参照）。一方、それを使って実際にOSのバックグラウンド機構を駆動する
/// <c>OptimizationWorker.kt</c> 相当のうち、**ファイルI/Oによる kill 耐性**は Phase 10 で
/// <see cref="Work.RunFiles"/> ＋ <c>MagiViewModel.RunMarker.cs</c> として移植した
/// （<c>work/RunFiles.kt</c> はディレクトリ1つしか要らない＝Android非依存だったため）。
/// **WorkManager 相当のタスク投入**（<c>runInBackground</c>）は Windows デスクトップに対応する
/// OS機構が無いため未実装のまま——設計判断が要る（無いものを作らない＝HF77）。このピースでは
/// <c>optimizeInFlight()</c> が <see cref="Work.OptimizationRepository.Running"/> を参照できるように
/// なったことで、以降のピースが編集ガードを正しく移植できる土台が整った。
///
/// [_ui.update{it.copy(...)} の置き換え方針] <see cref="UiState"/> は不変 data class ではなく
/// <c>ObservableObject</c> 派生の可変クラスとして移植済み（<c>UiState.cs</c> のクラスKDoc参照）。
/// Kotlin の <c>_ui.update { it.copy(x = ..., y = ...) }</c> は、このC#移植では
/// <c>Ui.X = ...; Ui.Y = ...;</c> という <see cref="Ui"/> インスタンスへの直接複数プロパティ代入に
/// 置き換える（新しいインスタンスへの差し替えではなく、既存インスタンスの変更＝各プロパティが
/// 個別に <c>PropertyChanged</c> を上げる）。
///
/// [partial class 分割方針] <c>V6SanityPort</c>/<c>V6HotfixPasses</c>/<c>V6NativeOptimizer</c> 等、
/// このC#移植で複数ピースにまたがる巨大クラスはすべて <c>partial class</c> ＋
/// <c>ClassName.Topic.cs</c>（例: <c>V6SanityPort.Guidance.cs</c>）という確立済みの規約で分割している
/// （Kotlin原本が単一ファイルなのは積み上げの結果であり意図的設計ではないと判断——計画書「フェーズ6」
/// 参照）。<c>MagiViewModel</c> も同じ規約に従う。このファイル（<c>MagiViewModel.cs</c>）はピース5
/// （状態管理サブシステム）、<c>MagiViewModel.Diagnostics.cs</c> はピース6（診断/レポート集約パイプライン）。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>
    /// [Services/UseCases/DI層] 最適化エンジン呼出しの境界（<see cref="MagiViewModel.Optimize.cs"/>
    /// が使用）。既定は <see cref="EngineOptimizationService"/>（実エンジンをそのまま呼ぶ）——
    /// <see cref="MagiViewModel()"/> の既存呼出元（本体コード・63件のテスト）は一切変更を要らない。
    /// WinUI3シェル側のDIコンテナ、またはテストがフェイク実装を注入したい場合は
    /// <see cref="MagiViewModel(IOptimizationService)"/> を使う。
    /// </summary>
    private readonly IOptimizationService _optimizationService;

    /// <summary>既定コンストラクタ。実エンジン（<see cref="EngineOptimizationService"/>）を使う。</summary>
    public MagiViewModel() : this(new EngineOptimizationService()) { }

    /// <summary>[Services/UseCases/DI層] 最適化エンジンの呼出し方を差し替え可能にするコンストラクタ。</summary>
    public MagiViewModel(IOptimizationService optimizationService)
    {
        _optimizationService = optimizationService;
    }

    /// <summary>
    /// 勤務表最適化のタイムアウト上限（秒）。唯一の真実源はエンジン層の
    /// <see cref="V6FinalPort.MaxOptimizeSec"/>。UI 側はそれを参照し、UI 設定の上限とエンジンの
    /// 頭打ちが乖離しないようにする（Kotlin原本の <c>const val MAX_BUDGET_SEC =
    /// com.magi.app.v6.MAX_OPTIMIZE_SEC</c> と同じ意図）。
    /// </summary>
    public const int MaxBudgetSec = V6FinalPort.MaxOptimizeSec;

    /// <summary>
    /// [saveNow メインスレッドI/O の由来をそのまま記録] onStop/onPause は「プロセスがこの直後に
    /// 破棄されうる」区間なので、saveNow() の同期書込は意図的（非同期へ逃がすと、ディスパッチされた
    /// 処理が走る前にプロセスが死にうる＝saveNow が存在する動機そのものを壊す）。データは業務上限
    /// （最大30名×31日）で小さく通常は数msで終わる想定＝この閾値超過だけを異常として記録する。
    /// </summary>
    private const long SaveNowSlowMs = 100L;

    /// <summary>
    /// 画面がバインドする唯一の可変状態。Kotlin原本の <c>val ui: StateFlow&lt;UiState&gt;</c> に
    /// 対応するが、このC#移植では単一の可変インスタンスを保持し続ける（クラスKDoc参照）。
    /// </summary>
    public UiState Ui { get; } = new();

    private string? _originalJson;

    /// <summary>
    /// [テスト可視性のためinternal化] Kotlin原本は <c>private var state</c>
    /// （<c>MagiViewModel.kt</c> はAndroid依存のためホストJVMで単体テストできず、専用テストが
    /// 元々存在しない）。このC#移植はプラットフォーム非依存で初めてテスト可能になったため、
    /// 「盤面が読み込まれている状態」を要するロジック（<see cref="SnapNow"/> 等）をこの後続ピースの
    /// <c>loadAsync</c> 移植を待たずに検証できるよう、確立済みの規約（Kotlin原本各所の
    /// 「private→internal、テスト可視性のためのみ」promotion）に倣い internal とする。
    /// </summary>
    internal MagiState? _state;
    internal int[][]? _currentSchedule;
    private int[][]? _resultSchedule;

    // job/checkJob/fixJob（Kotlin の kotlinx.coroutines.Job）に対応する取消トークンは、実際に
    // 非同期処理を起動する後続ピース（loadAsync/refreshCheck/findFixSuggestions 等）で導入する。
    private long _checkSeq;
    /// <summary>[3.392.0の由来] 直し方の探索も seq で世代管理する（取消が非同期なため）。</summary>
    private long _fixSeq;

    /// <summary>
    /// [3.404.0の由来をそのまま記録] いま「完了時に勤務表と設定を丸ごと差し替える前景ジョブ」が
    /// 走っているか＝その名前（走っていなければ null）。旧名 <c>optimizeActive</c> は「最適化」としか
    /// 読めず、同じ性質を持つ読み込み・CSV取込・初期解生成の3つが旗を立て忘れていた——この名前
    /// そのものが取り残しの原因だった、という経緯をそのまま引き継ぐ。
    /// </summary>
    private volatile string? _boardJobLabel;

    /// <summary>
    /// 旗の持ち主を識別する通し番号。<c>finally</c> で**自分が立てた旗のときだけ**下ろす
    /// （<c>checkSeq</c>/<c>fixSeq</c> と同じ手＝後から始まったジョブの旗を、先に終わった側が
    /// 下ろしてロックを早く解いてしまう事故を防ぐ）。
    /// </summary>
    private int _boardJobToken;

    /// <summary>
    /// [3.408.0の由来] エンジン実行の通し番号。操作ログ（履歴）と診断ログ（直近1回）を突き合わせる
    /// ための唯一の鍵。<c>activeRunSerial</c> は「いま実行中の番号」（0＝実行外）。
    /// </summary>
    private int _runSerial;

    private volatile int _activeRunSerial;

    /// <summary>
    /// [テスト可視性のためinternal化] 旗の発行・解放そのものに「後発のジョブの旗を先発の finally が
    /// 誤って下ろさない」という取消トークンの正しさが宿る（KDoc参照）。<c>loadAsync</c> 等の
    /// 実際の呼び出し元が移植されるより前に、このトークン機構自体を直接検証できるようにする。
    /// </summary>
    internal int BeginBoardJob(string label, bool engineRun = false)
    {
        _boardJobLabel = label;
        if (engineRun)
        {
            _runSerial++;
            _activeRunSerial = _runSerial;
        }
        return ++_boardJobToken;
    }

    internal void EndBoardJob(int token)
    {
        if (token == _boardJobToken)
        {
            _boardJobLabel = null;
            _activeRunSerial = 0;
        }
    }

    /// <summary>画面のメッセージで「何の実行中か」を言うための名前。背景実行には名前が無いので既定を返す。</summary>
    internal string BusyWhat() => _boardJobLabel ?? "バックグラウンド計算";

    /// <summary>
    /// [3.328.0 → 3.336.0/外部レビュー P1 の由来をそのまま記録] 編集・実行の可否は**ここだけ**を見る。
    /// <c>UiState.Running</c> は画面へ出すための写しに過ぎず、背景実行の購読開始タイミング次第で
    /// stale になりうる。対象は最適化に限らない（<see cref="_boardJobLabel"/> 参照）＝関数名は
    /// Kotlin原本のまま据え置くが、意味は「盤面を丸ごと差し替えるジョブが走っている」。
    /// </summary>
    internal bool OptimizeInFlight() => _boardJobLabel is not null || OptimizationRepository.Running;

    // ===== 元に戻す（undo/redo）: データ構造のみ。公開の Undo()/Redo() は後続ピースで移植する =====
    /// <summary>[テスト可視性のためinternal化] <see cref="SnapNow"/> の戻り値型として internal 昇格が必要。</summary>
    internal sealed record UndoSnap(MagiState State, int[][] Schedule);

    private readonly LinkedList<UndoSnap> _undoStack = new();
    private readonly LinkedList<UndoSnap> _redoStack = new();

    /// <summary>[テスト可視性のためinternal化] 「盤面未読込＝null」と「読込済み＝スナップショット」の両方を直接検証する。</summary>
    internal UndoSnap? SnapNow()
    {
        var st = _state;
        var sc = _currentSchedule;
        if (st is null || sc is null) return null;
        return new UndoSnap(st, sc.Copy2D());
    }

    internal void PushUndo()
    {
        var snap = SnapNow();
        if (snap is null) return;
        _undoStack.AddLast(snap);
        while (_undoStack.Count > 30) _undoStack.RemoveFirst();
        _redoStack.Clear(); // 新しい操作は redo 履歴を無効化（標準的な undo/redo 挙動）
        Ui.CanUndo = true;
        Ui.CanRedo = false;
    }

    internal void ClearUndo()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        Ui.CanUndo = false;
        Ui.CanRedo = false;
    }

    /// <summary>[テスト可視性のためinternal化] 30件トリムの境界挙動をテストから直接数えられるようにする。</summary>
    internal int UndoStackCount => _undoStack.Count;
    internal int RedoStackCount => _redoStack.Count;

    // ===== 操作ログ（監査）: 追記式・新しい順・時刻/レベル付き =====
    /// <summary>
    /// [3.408.0の由来] <c>Run</c> = そのとき走っていたエンジン実行の通し番号（0＝実行外）。
    /// 操作ログは複数回の実行にまたがる履歴、診断ログは直近1回ぶんしか無い——番号を持たせて
    /// 「どの行がどの実行のものか」を機械的に分けられるようにする（詳細は Kotlin 原本 3.408.0 参照）。
    /// </summary>
    private sealed record OpLogEntry(long TimeMs, string Level, string Message, int Run = 0);

    private readonly LinkedList<OpLogEntry> _opLog = new();

    /// <summary>
    /// 操作ログに1件追記し、UIへ反映（新しい順、リングの上限1000件）。
    /// [3.378.0/HF77=コメント≠実装の由来] Kotlin原本の旧KDocは「最大300件」と書いていたが実装は
    /// 1000だった、という教訓をそのまま実装値へ反映する。
    /// </summary>
    private void LogOp(string level, string message)
    {
        _opLog.AddFirst(new OpLogEntry(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), level, message, _activeRunSerial));
        while (_opLog.Count > 1000) _opLog.RemoveLast();
        Ui.OpLog = _opLog.Select(FormatOpLine).ToList();
    }

    /// <summary>[3.408.0の由来] 実行中の行だけ「#N」を付ける（実行外＝0 は従来どおり無印）。</summary>
    private static string FormatOpLine(OpLogEntry e)
    {
        var run = e.Run > 0 ? $"#{e.Run} " : "";
        var time = DateTimeOffset.FromUnixTimeMilliseconds(e.TimeMs).ToLocalTime().ToString("HH:mm:ss");
        return $"{time} [{e.Level}] {run}{e.Message}";
    }

    // 操作再現用デコード（現stateを参照。staff/shift一覧は操作中に不変）。
    private string OpNm(int i)
    {
        var name = _state is not null && i >= 0 && i < _state.StaffList.Count ? _state.StaffList[i].Name : null;
        return name ?? $"#{i}";
    }

    private string OpSy(int k)
    {
        var kigou = _state is not null && k >= 0 && k < _state.Shifts.Count ? _state.Shifts[k].Kigou : null;
        return kigou is not null ? KigouFormat.ToHankakuKigou(kigou) : $"#{k}";
    }

    /// <summary>[テスト可視性のためinternal化] state非依存の純粋な整形ロジック（10件境界の分岐）を直接検証する。</summary>
    internal static string OpDays(IReadOnlyList<int> days) =>
        days.Count <= 10 ? string.Join(",", days.Select(d => $"{d + 1}日")) : $"{days.Count}日分";

    // ===== 単純な同期設定セッター（ファイルI/O・非同期処理を伴わないもの） =====

    public void SetWorkers(int n)
    {
        var v = Math.Clamp(n, 1, 16);
        Ui.Workers = v;
        LogOp("I", $"設定変更: 並列数 → {v}");
    }

    /// <summary>
    /// [ネイティブ加速 Stage4 の由来] Kotlin原本は <c>NativeGate.userEnabled</c>
    /// （C++ SAチャンクの使用可否を制御するJNI層の旗）も同時に更新するが、
    /// このC#移植にネイティブ加速層（<c>magi_native.cpp</c> 相当）は存在しない——
    /// このアプリの計算は最初からマネージドC#のみで行われる。トグル自体
    /// （<see cref="UiState.NativeAccel"/>）は設定データの往復互換のため残し、表示専用の値として更新する。
    /// </summary>
    public void SetNativeAccel(bool on)
    {
        Ui.NativeAccel = on;
        LogOp("I", $"設定変更: ネイティブ加速 → {(on ? "ON" : "OFF")}");
    }

    /// <summary><see cref="SetNativeAccel"/> と同じ理由で <c>NativeGate.parityCheckEnabled</c> 相当は無い（表示専用）。</summary>
    public void SetNativeParity(bool on)
    {
        Ui.NativeParity = on;
        LogOp(on ? "I" : "W", $"設定変更: Kotlinパリティ照合 → {(on ? "ON" : "OFF（純ネイティブ・誤結果の可能性）")}");
    }

    /// <summary>
    /// [3.298.0の由来をそのまま記録] ブロック巡回交換の c3n 事前フィルタ ON/OFF（既定OFF）。
    /// c3n は HARD なので増える候補は <c>isBetter</c> が必ず却下する＝採用結果は ON/OFF で変わらない
    /// （Kotlin原本 3.296.0 の A/B 実測で確認済み）。ON は詰んだ候補へフル評価を呼ばないぶんの
    /// 節約だけ。
    /// </summary>
    public void SetBlockSwapC3nFilter(bool on)
    {
        PolishGate.FilterC3nIncrease = on;
        Ui.BlockSwapC3nFilter = on;
        LogOp("I", $"設定変更: 禁止連続の事前フィルタ → {(on ? "ON" : "OFF")}");
    }

    /// <summary>
    /// [3.304.0の由来をそのまま記録] 禁止連続を崩しに行く日を j±1 から「違反パターンがまたぐ全日」へ
    /// 広げる。実データで利得が一貫しなかったため既定 OFF（詳細は <see cref="PolishGate.WideC3nBreakDays"/> 参照）。
    /// </summary>
    public void SetWideC3nBreak(bool on)
    {
        PolishGate.WideC3nBreakDays = on;
        Ui.WideC3nBreak = on;
        LogOp("I", $"設定変更: 禁止連続の崩し範囲 → {(on ? "パターン全域" : "前後1日")}");
    }

    // [3.409.21の由来] setAdaptiveEscape / setPortfolioRoleParallelSa は Kotlin原本で削除済み
    //   （単体A/B中立＝機構ごと撤去）＝この移植でも対応不要。

    public void SetBudget(int sec)
    {
        var v = Math.Clamp(sec, 10, MaxBudgetSec);
        Ui.BudgetSec = v;
        LogOp("I", $"設定変更: 予算 → {v}秒");
    }

    public void SetSoftPolish(bool b)
    {
        Ui.SoftPolish = b;
        LogOp("I", $"設定変更: ソフト研磨 → {(b ? "ON" : "OFF")}");
    }

    /// <summary>
    /// [表示の記録] Kotlin原本の <c>$a</c>（enumの<c>toString()</c>）は宣言名そのまま
    /// （例: <c>PORTFOLIO</c>）を出すが、この移植の <see cref="V6Algorithm"/> は確立済みの
    /// C#移植規約でPascalCase名（例: <c>Portfolio</c>）を持つ。この操作ログ行は運用者向けの
    /// 表示文字列でエンジンの正しさには関与しないため、命名規約の違いをそのまま反映する
    /// （フィクスチャ検証の対象ではない）。
    /// </summary>
    public void SetV6Algorithm(V6Algorithm a)
    {
        Ui.V6Algorithm = a;
        LogOp("I", $"設定変更: 方式 → {a}");
    }
}
