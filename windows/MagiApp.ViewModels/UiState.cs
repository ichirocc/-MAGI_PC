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
/// （このC#移植では未着手）は Kotlin の <c>copy()</c> 呼び出し1つ1つを、対応するプロパティへの
/// 直接代入に置き換えて実装する（1画面分の値だけを直接更新でき、XAMLバインディングは変更のあった
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

    /// <summary>実行中の**表示**。可否の判定には使わない（ViewModel の optimizeInFlight が唯一の根拠）。</summary>
    [ObservableProperty] private bool running;

    [ObservableProperty] private bool hasResult;
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

    /// <summary>改善提案（違反を減らす1手＝変更/交換）。</summary>
    [ObservableProperty] private IReadOnlyList<FixSuggestion> fixSuggestions = Array.Empty<FixSuggestion>();

    /// <summary>改善手を探索中。</summary>
    [ObservableProperty] private bool fixSearching;

    /// <summary>絞り込み対象スタッフ名（空=全体）。</summary>
    [ObservableProperty] private string fixFocusName = "";

    [ObservableProperty] private IReadOnlyList<string> logs = Array.Empty<string>();
    [ObservableProperty] private long elapsedMs;
    [ObservableProperty] private int workers = Math.Clamp(Environment.ProcessorCount, 1, 8);
    [ObservableProperty] private int budgetSec = 300;

    /// <summary>C++ネイティブ加速(SAチャンク)のユーザートグル。C#移植では常に非該当（magi_native.cpp相当層は
    /// 対象外）だが、UI/設定の往復互換のためフィールド自体は保持する。</summary>
    [ObservableProperty] private bool nativeAccel = true;

    /// <summary>Kotlinパリティ照合トグル。同上の理由で保持。</summary>
    [ObservableProperty] private bool nativeParity = true;

    /// <summary>ブロック巡回交換で c3n が増える候補を候補生成段階で捨てるか。採用結果は不変・評価枠の節約のみ。</summary>
    [ObservableProperty] private bool blockSwapC3nFilter;

    /// <summary>禁止連続を崩す日を j±1 から違反パターン全域へ広げるか。既定OFF（実データで利得が一貫しない）。</summary>
    [ObservableProperty] private bool wideC3nBreak;

    // adaptiveEscape / portfolioRoleParallelSa はKotlin原本で単体A/B中立につき機構ごと撤去済み＝移植対象外。

    /// <summary>仕上げ最適化（品質研磨）。既定ON。keep-best で悪化しない。</summary>
    [ObservableProperty] private bool softPolish = true;

    [ObservableProperty] private V6Algorithm v6Algorithm = V6Algorithm.Auto;
    [ObservableProperty] private IReadOnlyList<string> staffNames = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> staffGroupSymbols = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> shiftSymbols = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> shiftColorHex = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> shiftTextHex = Array.Empty<string>();

    /// <summary>違反の表示色（空＝テーマのエラー色）。shiftColors["__vio__"] に保存。</summary>
    [ObservableProperty] private string violationColorHex = "";

    // [見直し候補] セル修正時に「基本ルールの見直し候補にする」で積むメモ（セッション内のみ・state非保存）。
    [ObservableProperty] private IReadOnlyList<string> reviewMemos = Array.Empty<string>();

    /// <summary>要調整(ソフト違反)の表示色（空＝既定の橙）。shiftColors["__vioSoft__"] に保存。</summary>
    [ObservableProperty] private string violationSoftColorHex = "";

    /// <summary>[違反色/族別] 族(c1/c3n/…)ごとの個別色。shiftColors["__vioFam_&lt;fam&gt;__"] 由来。
    /// 未設定族は重大度色へフォールバック。</summary>
    [ObservableProperty]
    private IReadOnlyDictionary<string, string> violationFamilyColorHex = new Dictionary<string, string>();

    [ObservableProperty] private IReadOnlyList<IReadOnlyList<int>> schedule = Array.Empty<IReadOnlyList<int>>();

    /// <summary>ws3 希望 "i,j"->shiftIdx（表示融合用）。</summary>
    [ObservableProperty] private IReadOnlyDictionary<string, int> wishes = new Dictionary<string, int>();

    /// <summary>[DefragLiveView] 計算中の最良盤面（実行中のみ）。</summary>
    [ObservableProperty] private IReadOnlyList<IReadOnlyList<int>> liveSchedule = Array.Empty<IReadOnlyList<int>>();

    [ObservableProperty] private V6PortReport? v6;
    [ObservableProperty] private bool constraintsEdited;
    [ObservableProperty] private bool structureEdited;

    // editRev（Compose再構成トリガー用ワークアラウンド）はここでは移植しない。理由はクラスKDoc参照。

    [ObservableProperty] private string? message;

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
    [ObservableProperty] private int impossibleWishCount;
    [ObservableProperty] private IReadOnlyList<string> opLog = Array.Empty<string>();

    /// <summary>他の案（採用案以外の候補サマリ）。</summary>
    [ObservableProperty] private IReadOnlyList<string> alternatives = Array.Empty<string>();

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

    /// <summary>前回の計算がプロセスkill等で中断された。</summary>
    [ObservableProperty] private bool interruptedRun;

    [ObservableProperty] private string? interruptedInfo;
}

/// <summary>
/// [フェーズ9] <c>MagiUiState.kt</c> の <c>data class PinTargetView</c> の逐語移植。
/// 回数固定(lo==hi)の緩和対象1件。<see cref="Attempts"/> は「目的関数が採用を認めた手を、
/// このピンのガードだけが止めた**計測できた回数**」。手の数ではなく試行の回数で、研磨の巡
/// （最大4）を重複排除せず数えている。**0 件でも緩和が無意味とは限らない**——緩和は下限割れ
/// (low, 重み90)の罰も外すため、「ピン以外の理由で」却下されていた候補が通るようになる経路が
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
