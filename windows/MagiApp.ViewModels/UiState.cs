using CommunityToolkit.Mvvm.ComponentModel;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9] <c>MagiUiState.kt</c> の <c>data class UiState</c> の移植。
///
/// Kotlin原本は不変(immutable) data class で、<c>MagiViewModel</c> が
/// <c>_ui.update{it.copy(...)}</c>（StateFlow への新スナップショット差し替え）で更新し、
/// Compose がその差し替えを検知して再構成する設計。WinUI3/XAML の慣用（バインディング対象は
/// <c>INotifyPropertyChanged</c> を実装し、プロパティ単位で変更通知する）に合わせ、この移植では
/// <see cref="ObservableObject"/> ＋ <c>[ObservableProperty]</c>（CommunityToolkit.Mvvm の
/// source generator）による**可変**の観測可能プロパティ群として表現する。<c>MagiViewModel</c>
/// （フェーズ9で移植完了。<c>MagiApp.ViewModels/MagiViewModel*.cs</c> 参照）は Kotlin の
/// <c>copy()</c> 呼び出し1つ1つを、対応するプロパティへの直接代入に置き換えて実装している
/// （1画面分の値だけを直接更新でき、XAMLバインディングは変更のあった
/// プロパティだけを再評価する＝Composeの全体再構成より効率が良い）。
///
/// <c>editRev: Int</c> は意図的に移植していない——Kotlin原本のコメントが明記するとおり、これは
/// Compose の再構成トリガー用ワークアラウンド（<c>structureEdited</c> が既に true のとき
/// <c>copy()</c> が同値になり StateFlow が emit しない問題への対処）で、プロパティ単位の変更通知を
/// 使う WinUI3 の <see cref="ObservableObject"/> では構造的に不要（各プロパティが独立に
/// <c>PropertyChanged</c> を上げるため、同じ値を再設定しても呼び出し元が明示的にプロパティを
/// 触れば通知は必ず飛ぶ設計になっている）。
/// </summary>
public sealed partial class UiState : ObservableObject
{
    // Kotlin: internal val emptyBreakdown = MirrorKeys.all.associateWith { 0 }
    // [テスト可視性/再利用のためinternal化] MagiViewModel.MakeUi（フェーズ9 ピース6）が
    // Kotlin原本の `emptyBreakdown + report.breakdown`（マップ合成で全19キーを保証する）と
    // 同じ土台を必要とするため、複製せずここへ委譲する（複製は必ずドリフトする＝確立済みの規約）。
    internal static IReadOnlyDictionary<string, int> EmptyBreakdown() =>
        MirrorKeys.All.ToDictionary(k => k, _ => 0);

    [ObservableProperty] private bool loaded;
    [ObservableProperty] private bool canUndo;
    [ObservableProperty] private bool canRedo; // [Web反映] 手動修正ループ用の「やり直し」
    [ObservableProperty] private int staff;
    [ObservableProperty] private int days;
    [ObservableProperty] private int shifts;
    [ObservableProperty] private int groups;
    [ObservableProperty] private bool use2;
    [ObservableProperty] private long initHard;
    [ObservableProperty] private long initSoft;
    /// <summary>進捗の基準を取った盤面の族ごとの件数（進捗行の「開始」を単位つきで言う）。空＝基準なし。</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, int> initBreakdown = new Dictionary<string, int>();

    /// <summary>実行中の**表示**。可否の判定には使わない（ViewModel の optimizeInFlight が唯一の根拠）。</summary>
    [ObservableProperty] private bool running;

    [ObservableProperty] private bool hasResult;
    /// <summary>
    /// [3.475.0/論理監査] エンジンが実際に走った事実。<see cref="HasResult"/> は手編集・取込でも true になる
    /// 「盤面がある」旗なので、分析の「計算済み／未計算」はこちらだけを読む（手操作で false に戻す）。
    /// </summary>
    [ObservableProperty] private bool engineRan;
    /// <summary>直前の作成で前の勤務表のほうが良く、前のままにした（作成の札とお知らせの言い方を変える）。</summary>
    [ObservableProperty] private bool keptPrevious;
    /// <summary>自動作成のあとに手で変えたか（EngineRan が手操作で false に戻った盤面）。上部の「手で変更あり」だけが読む。保存しない。</summary>
    [ObservableProperty] private bool ranBeforeEdit;
    /// <summary>いまの盤面が S6（設定を緩めて手順を当てる）の結果か。分析の注記だけが読む。EngineRan を書く箇所で false に戻す。</summary>
    [ObservableProperty] private bool relaxedBoard;
    /// <summary>
    /// 表示中の検査結果の世代。<c>MakeUi</c>（検査完了・最適化完了など報告の反映）のたびに増える。
    /// 「押したあとの再検査が盤面に追いついたか」を Schedule の変更通知でなくこの世代で判定する
    /// （Schedule は SetCell 直後に変わるが CoverageDiag はまだ古い＝そこで候補を再有効化すると連打で covO/covU を同時に作れる）。
    /// </summary>
    [ObservableProperty] private long checkRev;
    [ObservableProperty] private long bestHard;
    [ObservableProperty] private long bestSoft;
    [ObservableProperty] private int totalViolations;
    [ObservableProperty] private double weightedScore;
    [ObservableProperty] private IReadOnlyDictionary<string, int> breakdown = EmptyBreakdown();
    [ObservableProperty] private IReadOnlyDictionary<string, string> violationCells = new Dictionary<string, string>();
    [ObservableProperty] private IReadOnlyDictionary<string, string> needViolations = new Dictionary<string, string>();
    [ObservableProperty] private IReadOnlyDictionary<string, string> countViolations = new Dictionary<string, string>();

    // [Set化] セル("i,j")の全違反クラス（重み降順。violationCells は最重1クラス）。タップ全列挙とE7整合に使う。
    [ObservableProperty]
    private IReadOnlyDictionary<string, IReadOnlyList<string>> violationCellFamilies = new Dictionary<string, IReadOnlyList<string>>();

    // 回数キー("i,k")/被覆キー("k,j")の全違反クラス（重み降順）。violationCellFamilies の兄弟。
    // breakdownLocations（内訳→場所タップ）が重い族に隠れた軽い族の場所を取りこぼさないために使う。
    [ObservableProperty]
    private IReadOnlyDictionary<string, IReadOnlyList<string>> countFamilies = new Dictionary<string, IReadOnlyList<string>>();

    [ObservableProperty]
    private IReadOnlyDictionary<string, IReadOnlyList<string>> needFamilies = new Dictionary<string, IReadOnlyList<string>>();

    // [場所表示] fair/weekly の職員単位の偏り箇所。"weekly"->[[i,dev],..] / "fair"->[[i,k,dev],..]（dev降順）。
    // 内訳パネルの場所表示専用（グリッドには出さない）。表示のみ・スコア不変。
    [ObservableProperty]
    private IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>> distLocations = new Dictionary<string, IReadOnlyList<IReadOnlyList<int>>>();

    /// <summary>期間の制約の不足区間（<see cref="C1Display.Shortages"/>）。画面の表示専用の印と帯を作る元。</summary>
    [ObservableProperty] private IReadOnlyList<C1Shortage> c1Shortages = Array.Empty<C1Shortage>();

    /// <summary>個人の上限0のシフトが入っているセル（<see cref="GridDisplayMarks.ZeroCapCells"/>）。画面の表示専用の印。</summary>
    [ObservableProperty] private IReadOnlySet<string> zeroCapCells = new HashSet<string>();

    /// <summary>必須のうち希望どうしのぶつかり（計算では消せない）の件数。</summary>
    [ObservableProperty] private int hardWishConflict;

    /// <summary>許容0の超過のセル → 表示クラス（<see cref="GridDisplayMarks.ZeroAllowCells"/>）。画面の表示専用の印。</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, string> zeroAllowCells = new Dictionary<string, string>();

    /// <summary>改善提案（違反を減らす1手＝変更/交換）。</summary>
    [ObservableProperty] private IReadOnlyList<FixSuggestion> fixSuggestions = Array.Empty<FixSuggestion>();

    /// <summary>見ている問題の案を段に分けたもの（鍵＝依頼の FixFocus.Key。別の依頼では使わない）。</summary>
    [ObservableProperty] private FixTierView? fixTiers;

    /// <summary>下書きで今の勤務表を置き換える前の確認の文（null＝出さない）。</summary>
    [ObservableProperty] private bool draftStage;   // 最後のエンジン実行が下書きづくり（手を入れたかは EngineRan で見る）
    [ObservableProperty] private string? draftConfirm;

    /// <summary>改善手を探索中。</summary>
    [ObservableProperty] private bool fixSearching;

    /// <summary>[Android 3.612.0 思考誘導S0] 盤面全体の1手探索を今の盤面で終えたか（未探索と「探して0件」を分ける）。</summary>
    [ObservableProperty] private bool fixSearched;
    /// <summary>同じ依頼が続けて案 0 件で終わったときの依頼の鍵（<c>FixFocus.Key</c>、null＝続いていない）。</summary>
    [ObservableProperty] private string? fixEmptyRepeatKey;
    /// <summary>前回の作成と同じ設定から同じ盤面が出た（<see cref="AnalysisTriage.SameResultVisible"/> で照合）。</summary>
    [ObservableProperty] private SameResultNote? sameResult;

    /// <summary>[Android 3.612.0 思考誘導S4] 直近の実行で長く改善せず採用盤面にも残った必須族（盤面を変えたら空）。</summary>
    [ObservableProperty] private IReadOnlyList<string> stalledHardFamilies = Array.Empty<string>();
    [ObservableProperty] private V6FinalPort.StopSummary? stopSummary;   // [3.643.0] 直近の実行がどう終わったか（ホームの説明行）。結果が無効になる操作で null

    /// <summary>絞り込み対象スタッフ名（空=全体）。</summary>
    [ObservableProperty] private string fixFocusName = "";
    /// <summary>直し方の探索を終えた依頼の鍵（<see cref="FixFocus.Key"/>、空＝画面全体や未完了）。印・セルのシートが自分の結果か見分ける。</summary>
    [ObservableProperty] private string fixDoneKey = "";
    /// <summary>直し方の探索が失敗した依頼の鍵。</summary>
    [ObservableProperty] private string fixFailedKey = "";
    [ObservableProperty] private string fixCancelledKey = "";   // 利用者が止めた探索の対象（止めたことを言い、もう一度探せるようにする）
    [ObservableProperty] private bool fixTimedOut;   // [仕様 §7] 直近の直し方の探索が締切で打ち切られたか（見つからない理由を「時間不足」と言う）

    [ObservableProperty] private IReadOnlyList<string> logs = Array.Empty<string>();
    [ObservableProperty] private long elapsedMs;
    [ObservableProperty] private int workers = Math.Clamp(Environment.ProcessorCount, 1, 8);
    [ObservableProperty] private int budgetSec = 60;   // 探索の時間。後処理は PostReserveSec を後ろに足す（3.659.0 利用者指示: 既定 300→60）

    /// <summary>C++ネイティブ加速(SAチャンク)のユーザートグル。C#移植では常に非該当（magi_native.cpp相当層は
    /// 対象外）だが、UI/設定の往復互換のためフィールド自体は保持する。</summary>
    [ObservableProperty] private bool nativeAccel = true;

    /// <summary>Kotlinパリティ照合トグル。同上の理由で保持。</summary>
    [ObservableProperty] private bool nativeParity = true;

    /// <summary>[Android 3.615.0同期] 設定画面「探索の強さ」。保存しない。</summary>
    [ObservableProperty] private SearchStrength searchStrength = SearchStrength.Normal;
    /// <summary>[Android 3.638.0同期] <c>!V6HotfixPasses.HeavySoftGuard</c>（測定用・既定OFF）。じっくりの容認 6% でだけ効く。保存しない。</summary>
    [ObservableProperty] private bool aptFairToleranceUnguarded;

    // adaptiveEscape / portfolioRoleParallelSa はKotlin原本で単体A/B中立につき機構ごと撤去済み＝移植対象外。

    /// <summary>仕上げ最適化（品質研磨）。既定ON。keep-best で悪化しない。</summary>
    [ObservableProperty] private bool softPolish = true;
    [ObservableProperty] private bool agentAuto = true;   // 作成のあと代行で仕上げる（AX）。保存しない
    [ObservableProperty] private bool agentRunning;
    [ObservableProperty] private AgentRecord? agentRecord;

    [ObservableProperty] private V6Algorithm v6Algorithm = V6Algorithm.Auto;
    [ObservableProperty] private IReadOnlyList<string> staffNames = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<int> staffIds = Array.Empty<int>();   // StaffNames と同じ並びの職員 ID（相談の対象を ID で引く）
    [ObservableProperty] private IReadOnlyList<string> staffGroupSymbols = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> shiftSymbols = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> shiftColorHex = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> shiftTextHex = Array.Empty<string>();

    /// <summary>違反の表示色（空＝テーマのエラー色）。shiftColors["__vio__"] に保存。</summary>
    [ObservableProperty] private string violationColorHex = "";

    // [見直し候補] セル修正時に「基本ルールの見直し候補にする」で積むメモ（セッション内のみ・state非保存）。
    [ObservableProperty] private IReadOnlyList<string> reviewMemos = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<ConsultItem> consults = Array.Empty<ConsultItem>();   // [3.645.0] 相談してから決める判断（セッション内のみ・state 非保存。主カードの行は ConsultList.Line）

    /// <summary>要調整(ソフト違反)の表示色（空＝既定の橙）。shiftColors["__vioSoft__"] に保存。</summary>
    [ObservableProperty] private string violationSoftColorHex = "";

    /// <summary>[違反色/族別] 族(c1/c3n/…)ごとの個別色。shiftColors["__vioFam_&lt;fam&gt;__"] 由来。
    /// 未設定族は重大度色へフォールバック。</summary>
    [ObservableProperty]
    private IReadOnlyDictionary<string, string> violationFamilyColorHex = new Dictionary<string, string>();

    [ObservableProperty] private IReadOnlyList<IReadOnlyList<int>> schedule = Array.Empty<IReadOnlyList<int>>();

    /// <summary>ws3 希望 "i,j"->shiftIdx（表示融合用）。</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, int> wishes = new Dictionary<string, int>();
    /// <summary>印と一文の写し（<see cref="DisplaySnapshot"/>）。検査結果の反映（MakeUi）で作り直す。保存しない。</summary>
    [ObservableProperty] private DisplaySnapshot? display;
    /// <summary>ホームの先頭の直す手が解消する必須違反の名と場所（VM が作る。<see cref="TopFixResolved.Matches"/> で鮮度を照合）。保存しない。</summary>
    [ObservableProperty] private TopFixResolved? topFixResolved;
    /// <summary>必須違反を減らすすべての案の解消の鍵の和集合（分析タブの 3 点目。VM が背景で作る）。保存しない。</summary>
    [ObservableProperty] private FixResolvedUnion? fixResolvedUnion;
    /// <summary>[S5] 試算できる希望のキー（実現可能で手動固定でない）。</summary>
    [ObservableProperty] private IReadOnlySet<string> lockedWishKeys = new HashSet<string>();
    /// <summary>[#41] 手動固定のセル "i,j"。</summary>
    [ObservableProperty] private IReadOnlySet<string> manualPins = new HashSet<string>();
    /// <summary>拡張希望: セル "i,j" → その日に禁止のシフト index。</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, IReadOnlySet<int>> extBanned = new Dictionary<string, IReadOnlySet<int>>();
    /// <summary>[S5] 希望どうしの衝突（兄弟の希望を試算の候補に足す）。</summary>
    [ObservableProperty] private IReadOnlyList<WishSelfConflict> wishSelfConflicts = Array.Empty<WishSelfConflict>();
    /// <summary>[S5] 試算が終わるたびに進む（画面は <c>WishTrialFor</c> で読み直す）。</summary>
    [ObservableProperty] private int wishTrialRev;
    /// <summary>[S5] 試算中の行 "i,j"（null＝なし）。</summary>
    [ObservableProperty] private string? wishTrialBusy;
    /// <summary>[S6] 承認フロー R1〜R5（null＝閉じている）。</summary>
    [ObservableProperty] private RelaxApproval.Flow? relaxFlow;
    /// <summary>[S6] 試算が終わるたびに進む（画面は <c>RelaxTrialFor</c> で読み直す）。</summary>
    [ObservableProperty] private int relaxRev;
    /// <summary>[S6] 背景で設定の壁を探している。</summary>
    [ObservableProperty] private bool relaxSearching;
    /// <summary>[S5] 直近の「希望を取り消して再作成」の結果（表示は <c>WishCancelOutcomeLine</c>）。</summary>
    [ObservableProperty] private WishCancelOutcome? wishCancelOutcome;
    [ObservableProperty] private FixOutcome? fixOutcome;        // [3.643.0] 直近に当てた直し方の結果 1 行（表示は FixOutcomeLine＝盤面が変わったら出さない）
    [ObservableProperty] private string? csvSavedAt;            // [3.643.0] この盤面を勤務表 CSV に保存した時刻 HH:mm（表示は CsvSavedLine）
    [ObservableProperty] private MonthMovePlan? monthMovePrompt;   // [3.643.0] 対象の月を移す前の確認。null＝閉じている
    [ObservableProperty] private AppliedFix? appliedFix;           // [3.669.0] 案の一覧から当てた手の結果（同じ依頼の画面で残る問題へ案内）
    [ObservableProperty] private SaveState saveState = SaveState.Saved;   // [Android 3.529.0] 自動保存の状態（適用後の案内「保存：…」が読む）
    [ObservableProperty] private SaveFailDialog? saveFailDialog;   // 保存の失敗を知らせるダイアログ（null＝閉じている）
    [ObservableProperty] private ChainFixPreview? chainPreview;    // [3.644.0] 複数人の入替を当てる前の一覧（だれの・どの日の・何→何）。null＝閉じている

    /// <summary>[つくる前の確認] 非 null の間シートを出す。</summary>
    [ObservableProperty] private MagiEngine.V6.PreRunCheck.Summary? preRunCheck;
    [ObservableProperty] private string? preRunRepeatHint;

    /// <summary>[Android 3.727.1] 勤務表CSV（重ね合わせ）を反映する前の確認の行（<see cref="CsvImportPreview.CsvImportPreviewLines"/>）。非 null＝確認待ち。</summary>
    [ObservableProperty] private IReadOnlyList<string>? csvImportPrompt;
    /// <summary>引用符が閉じていない勤務表CSVの「読めた部分だけ取り込むか」確認。非 null の間ダイアログを出す（保存しない）。</summary>
    [ObservableProperty] private string? csvPartialPrompt;

    /// <summary>[DefragLiveView] 計算中の最良盤面（実行中のみ）。</summary>
    [ObservableProperty] private IReadOnlyList<IReadOnlyList<int>> liveSchedule = Array.Empty<IReadOnlyList<int>>();

    [ObservableProperty] private V6PortReport? v6;
    [ObservableProperty] private bool constraintsEdited;
    [ObservableProperty] private bool structureEdited;

    // editRev（Compose再構成トリガー用ワークアラウンド）はここでは移植しない。理由はクラスKDoc参照。

    [ObservableProperty] private string? message;
    /// <summary>操作の通知（「元に戻す」付き）。検査の進み具合の <see cref="Message"/> とは別のイベント＝再検査で上書きされない。</summary>
    [ObservableProperty] private OpNotice? opNotice;
    /// <summary>セル編集シートの利き手（false=右手・既定）。左手はボタンの行を左右反転し、日送りと閉じるを左へ。</summary>
    [ObservableProperty] private bool leftHand;

    /// <summary>直近メッセージが「失敗・拒否」か。表示側の色（エラー系）と表示時間（長め）を分ける根拠として使う。
    /// notify(text, "W") が唯一の true の書き手という契約は ViewModel 側の実装責務（このプロパティ自体は
    /// 単なる可変フラグ）。</summary>
    [ObservableProperty] private bool messageIsError;

    /// <summary>[判断設計監査 #3] 「データを開く」直前の1世代退避が存在するか（設定タブの復元導線の表示条件）。</summary>
    [ObservableProperty] private bool prevBackupAvailable;

    // 操作コパイロット: 満足度(0-100) / 研磨の限界 / ガチャ操作の助言
    [ObservableProperty] private int satisfaction;
    [ObservableProperty] private bool polishExhausted;
    [ObservableProperty] private string? copilotHint;

    /// <summary>[Android 3.509.4/3.510.3 同期] 直近の最適化・自動修正の前後比較（完了カード用）。Kotlin原本の
    /// <c>runSummary</c> は整形済み文字列だが、族名の日本語化（<c>BreakdownLabels</c>）が View 層にあるため、
    /// 生の <see cref="MagiEngine.V6.ChangeSummary"/> を保持し整形は <c>HomeView.xaml.cs</c> 側で行う。
    /// 盤面が変わる操作（PushUndo）・元に戻す・読込で消す＝別の盤面の比較を完了カードに残さない。</summary>
    [ObservableProperty] private ChangeSummary? runSummary;
    /// <summary>[Android 3.727.1・iOS のヘルスケア「ハイライト」] この作成で必須違反・要調整がいくつからいくつになったか（変化なしは null）。RunSummary と同じ寿命。</summary>
    [ObservableProperty] private string? runDelta;
    /// <summary>[Android 3.727.3] 下書きに置き換える前の勤務表のほうが良い（作成の結果がそれに負けたとき）。盤面が動けば消す。</summary>
    [ObservableProperty] private string? preDraftNotice;

    [ObservableProperty] private int impossibleWishCount;
    [ObservableProperty] private IReadOnlyList<string> opLog = Array.Empty<string>();

    /// <summary>他の案（採用案以外の候補サマリ）。</summary>
    [ObservableProperty] private IReadOnlyList<string> alternatives = Array.Empty<string>();
    /// <summary>他の案ごとに、計算した時点の勤務表より悪いか。</summary>
    [ObservableProperty] private IReadOnlyList<bool> alternativesWorse = Array.Empty<bool>();

    /// <summary>いま盤面に適用している他の案の添字（-1=どれでもない）。</summary>
    [ObservableProperty] private int alternativeApplied = -1;

    /// <summary>人員不足(covU)/人員過剰(covO)の原因診断（充足不可/充足可能の切り分け・過剰がなぜ動かせないか）。</summary>
    [ObservableProperty] private CoverageDiagnosis? coverageDiag;

    /// <summary>禁止連続(c3n)の「なぜ崩せないか」診断（c3n=0 なら null）。</summary>
    [ObservableProperty] private ForbiddenRunDiagnosis? forbiddenDiag;

    /// <summary>窓の要件(c1)がなぜ直せなかったかの構造化診断（直近の最適化の観測。残存なし/未実行なら null）。</summary>
    [ObservableProperty] private C1PlateauDiagnosis? c1Plateau;

    /// <summary>
    /// 直近の最適化で「回数固定(lo==hi)だけが却下の理由だった」**計測済みの候補試行数**。
    /// 全手数でも改善予測でもない。計測しているのは後処理研磨のうち V6HotfixPasses の19パス＋
    /// 最終LNS 2本（C1JointLns/PersonalBalanceJointLns）＝21パス。EliteIntegration(4)/
    /// C1TemporalFlow(1)/CombinatorialRepair(2)/C1RepairAnalysis(1) の計8箇所と探索本体(SA/ALNS/LAHC)は
    /// 計測外。最大4巡を重複排除せず加算する。0 は「緩めても変わらない」の証明にならない。
    /// </summary>
    [ObservableProperty] private int observedPinBlockedAttempts;

    /// <summary>緩和の対象候補（どのピンが何回止めたか・多い順）。空＝観測なし。</summary>
    [ObservableProperty] private IReadOnlyList<PinTargetView> pinTargets = Array.Empty<PinTargetView>();

    /// <summary>制約/希望の設定ミスと直し方の誘導。</summary>
    [ObservableProperty] private IReadOnlyList<SettingIssue> settingIssues = Array.Empty<SettingIssue>();

    /// <summary>期間開始日（カレンダー表示の曜日整列に使用）。</summary>
    [ObservableProperty] private string startDate = "";
}

/// <summary>
/// [フェーズ9] <c>MagiUiState.kt</c> の <c>data class PinTargetView</c> の逐語移植。
/// 回数固定(lo==hi)の緩和対象1件。<see cref="Attempts"/> は「目的関数が採用を認めた手を、
/// このピンのガードだけが止めた**計測できた回数**」。手の数ではなく試行の回数で、研磨の巡
/// （最大4）を重複排除せず数えている。**0 件でも緩和が無意味とは限らない**——緩和は下限割れ
/// (low)の罰も外すため、「ピン以外の理由で」却下されていた候補が通るようになる経路が
/// 別にある（Kotlin原本で実測確認済み）。
/// </summary>
public sealed record PinTargetView(
    int Staff,
    int Shift,
    string StaffName,
    string ShiftKigou,
    /// <summary>固定されている回数（lo==hi の値）。</summary>
    int PinnedCount,
    int Attempts);

/// <summary>[S5] 「希望を取り消して再作成」の結果（<c>docs/s5_wish_trial.md</c> §9）。<see cref="Line"/> は次にやることカードに出す 1 行。</summary>
public sealed record WishCancelOutcome(string Name, int Day, string Symbol, int H0, int PCancel, int G, string Line);

/// <summary>[Android 3.615.0同期] Thorough は測定中の4フラグを ON、Normal は既定値へ戻す（出力は従来どおり）。</summary>
public enum SearchStrength { Normal, Thorough }

public static class SearchStrengthExtensions
{
    /// <summary>[3.694.0] 4 つのトグルの値（Kotlin <c>SearchStrength.flags</c>）。<see cref="Apply"/> と設定変更の行の表示が同じ源を読む。</summary>
    public static (bool CombineExhaustPairs, bool WideC3nBreakDays, bool CountChainPolish, bool AptFairSoftTolerance) Flags(this SearchStrength strength)
    {
        var on = strength == SearchStrength.Thorough;
        return (on, on, on, on);
    }

    public static void Apply(this SearchStrength strength)
    {
        var f = strength.Flags();
        MagiEngine.V6.PolishGate.CombineExhaustPairs = f.CombineExhaustPairs;
        MagiEngine.V6.PolishGate.WideC3nBreakDays = f.WideC3nBreakDays;
        MagiEngine.V6.PolishGate.CountChainPolish = f.CountChainPolish;
        MagiEngine.V6.PolishGate.AptFairSoftTolerance = f.AptFairSoftTolerance;
    }

    public static string Label(this SearchStrength strength) => strength == SearchStrength.Thorough ? "じっくり" : "ふつう";

    /// <summary>設定変更の行の括弧の中（語は「設定の効き」と同じ）。</summary>
    public static string FlagWords(this SearchStrength strength)
    {
        var f = strength.Flags();
        static string W(bool b) => b ? "ON" : "OFF";
        return $"結合探索を粘り強く={W(f.CombineExhaustPairs)}・禁止連続の崩し範囲={W(f.WideC3nBreakDays)}・回数連鎖研磨={W(f.CountChainPolish)}・公平化/適切回数の他ソフト許容(6%)={W(f.AptFairSoftTolerance)}";
    }
}

/// <summary>[Android 3.529.0] 自動保存の状態（Kotlin <c>SaveState</c>）。</summary>
public enum SaveState { Dirty, Saving, Saved, Failed }

/// <summary>保存の失敗を知らせるダイアログ（First＝成功から失敗に変わった、Again＝失敗中のやり直しも失敗した）。</summary>
public enum SaveFailDialog { First, Again }

/// <summary>案の一覧から確認して当てた手（Kotlin <c>AppliedFix</c>）。FocusKey の依頼の画面へ「変更を反映しました」を返す（After と今の盤面が違えば出さない）。</summary>
public sealed record AppliedFix(string FocusKey, string Label, IReadOnlyList<MagiEngine.V6.FixCell> Ops,
    IReadOnlyList<IReadOnlyList<int>> Before, IReadOnlyList<IReadOnlyList<int>> After, FixTarget? Target = null,
    int HardBefore = -1, int HardAfter = -1, int SoftBefore = -1, int SoftAfter = -1,
    IReadOnlyDictionary<string, int>? BreakdownBefore = null, IReadOnlyDictionary<string, int>? BreakdownAfter = null)
{
    /// <summary>今の盤面がこの手の直後か（Kotlin の <c>after == ui.schedule</c>＝要素ごとの比較）。</summary>
    public bool IsCurrent(IReadOnlyList<IReadOnlyList<int>> schedule) =>
        After.Count == schedule.Count && After.Zip(schedule).All(p => p.First.SequenceEqual(p.Second));
}

/// <summary>先頭の直す手（Ops）を Schedule に当てたとき解消する必須違反の行。盤面と手が今と同じときだけ使う。</summary>
public sealed record TopFixResolved(IReadOnlyList<FixCell> Ops, IReadOnlyList<IReadOnlyList<int>> Schedule, IReadOnlyList<string> Labels)
{
    public bool Matches(IReadOnlyList<FixCell>? ops, IReadOnlyList<IReadOnlyList<int>> schedule) =>
        ops is not null && ops.SequenceEqual(Ops) && schedule.Count == Schedule.Count && schedule.Zip(Schedule).All(p => p.First.SequenceEqual(p.Second));
}

/// <summary>必須違反を減らす案の手（OpsList）を Schedule に当てたとき解消する必須違反の鍵の和集合。</summary>
public sealed record FixResolvedUnion(IReadOnlyList<IReadOnlyList<FixCell>> OpsList, IReadOnlyList<IReadOnlyList<int>> Schedule, IReadOnlySet<ViolationKey> Keys)
{
    public bool Matches(IReadOnlyList<IReadOnlyList<FixCell>> opsList, IReadOnlyList<IReadOnlyList<int>> schedule) =>
        opsList.Count == OpsList.Count && opsList.Zip(OpsList).All(p => p.First.SequenceEqual(p.Second))
        && schedule.Count == Schedule.Count && schedule.Zip(Schedule).All(p => p.First.SequenceEqual(p.Second));
}
