# V6 Native Engine Port (`com.magi.app.v6`)

This is the complete native port of the Web **V6 / mirror** scheduling engine
(`magi_python_mirror.py` + the V6 web worker), brought over from the standalone
`MagiNative` reference build.

## Why a separate package

The existing engine on `main` lives in `com.magi.app.engine` and already defines
its own constraint model (`EngineConstraints.kt`: `C1`, `C2`, `C3`, `C41`, `C42`),
its own `DeltaEvaluator`, and its own `C3Run`. The V6 mirror engine defines
classes with the **same names** but different semantics (e.g. `c1` = in-window
minimum vs. the resolved engine's gap band, `c2` = lower bound vs. upper cap).

To avoid duplicate-declaration collisions — and to keep the two engine lineages
from silently disagreeing — the entire V6 engine is isolated in
**`com.magi.app.v6`**. The two packages share nothing except the model types in
`com.magi.app.model`, so each lineage keeps its own authoritative semantics.

This replaces the earlier `p11-bulk-sync` approach, which kept the engine in
`com.magi.app.engine` and renamed the constraint rows to `P11C1..P11C42`. That
partial sync shipped only a thinned subset (no `SaOptimizer`, no
`V6LateOperators`, no `V6PortAnalyzer`, a 133-line stub `V6NativeOptimizer`,
and only a smoke test). This port brings the full engine across verbatim.

## Contents

Main (`app/src/main/java/com/magi/app/v6/`):

- `Problem.kt`, `Evaluator.kt`, `DeltaEvaluator.kt`, `C3Run.kt` — resolved problem
  view + full / delta scoring.
- `MirrorCore.kt` — `UnifiedViolationChecker`, weighted score, shared helpers.
- `SmartInitialScheduler.kt`, `GreedyMirrorScheduler.kt`, `SaOptimizer.kt` —
  schedule construction and the high-speed simulated-annealing optimizer.
  （`GreedyMirrorScheduler` は 3.393.0 以降テスト専用の旧生成器。`LightMirrorOptimizer.kt` は同版で撤去。）
- `V6NativeOptimizer.kt` — full multi-algorithm dispatcher (V5 / ALNS / RSI /
  RSI+) wiring the SA engine, late operators, and hotfix passes.
- `V6HotfixPasses.kt`, `V6LateOperators.kt` — post-optimization passes.
- `V6SanityPort.kt`, `V6PortAnalyzer.kt` — diagnostics.
- `Ws1Ops.kt`, `ScheduleCsvBridge.kt` — settings ops and CSV round-trip.
- `V6FinalPort.kt` — stable façade for UI / tests
  (`handleCheck` / `handleSmartInitial` / `handleOptimize`)。
- `ShiftAppearance.kt` — シフト記号→表示色・違反キー→重大度（3.393.0 に `V6WebCompat.kt` から切り出し、残りは撤去）。

Tests (`app/src/test/java/com/magi/app/v6/`): `DeltaEvaluatorTest`,
`MirrorEngineTest`, `ShiftAppearanceTest`, `V6SanityPortTest`, `V6PortAnalyzerTest`,
`V6LateOperatorsTest`, `V6NativeOptimizerChoiceTest`, `V6FinalBridgePortTest`.

## UI wiring

`MagiApp` (Compose) drives the V6 engine through **`MagiViewModel`**, which owns
all long-running work on `viewModelScope` (not Compose's `rememberCoroutineScope`).
Each action launches a cancellable `job`; **計算を止める** calls `job?.cancel()`.

- **最適化する** → `MagiViewModel.runV6FullOptimize()` →
  `V6FinalPort.handleOptimize(state, schedule, secondsRaw = _ui.value.budgetSec,
  workers, softPolish, requestedAlgorithm, allowImpossible)`.
  - **予算秒数は UI 設定値** (`budgetSec`)。初期値 **300 秒**・上限 **300 秒（＝5分）**
    (`MAX_BUDGET_SEC = MAX_OPTIMIZE_SEC = 300`、`setBudget` は `coerceIn(10, 300)`)。
    停滞時はさらに早期終了する。旧仕様の「30 秒固定」「600 秒」は撤去済み。
  - **方式は選択式**: `AUTO / V5 / ALNS / RSI / RSI_PLUS`。AUTO は予算からプランを自動選択。
  - `handleOptimize` 自身が `withContext(Dispatchers.Default)` で実行。
- **初期解生成** → `V6FinalPort.handleSmartInitial(state)`（希望・C1 優先の下書き、ViewModel 経由）。
  （旧「簡易作成」`handleSimple` は UI 導線が 3.126.0 で撤去され、本体も 3.393.0 で撤去。）
- **違反チェック** → `V6FinalPort.handleCheck(state, schedule)`（評価のみ、`checkJob` で実行）。

実行中の挙動（旧仕様より複雑）:

- **進捗ストリーム**: `handleOptimize` の `onProgress(phase, report, iters, elapsed)` を
  `UiState`（`bestHard/bestSoft/totalViolations/iters/itersPerSec/elapsedMs/message`）へ反映。
  後処理フェーズ（HF80/67/66/70）も `onPhase` で進捗更新される（ハング誤認の防止）。
- **全体予算管理**: 探索＋後処理を `hardDeadline = 開始 + budgetSec` で一括管理し、
  超過時は後処理を打ち切る。完了ログに総時間と内訳（探索/連鎖/後処理）を出力。
- **並列ワーカー vs 実効仮説**: `workers` は設定上 `1..16`（`UiState` 初期は端末 CPU 数で最大 8）。
  ただし最適化エンジンは仕様 §2.2 により**最大 5 仮説**並列（`workers.coerceIn(1,5)`）。
  ログ・UI では「workers 設定 N / 実効仮説 M」を分けて表示する。
  - **[余剰ワーカー活用] 上限を超えた分は仮説内並列度へ配分**（`hypothesisChainPlan(workers,
    w, cores)`＝**余り配分＋コア数クランプ**）: RSI/RSI++ が Phase1/奇数ラウンドで呼ぶ
    `runV5`(`SaOptimizer`) は元々 `workers` 本の SA チェーンを並列実行できる実装だったが、
    旧 `runMultiWorker` が各仮説へ一律 `workers=1` を強制していたため 5 を超える設定は完全に
    無駄だった（実機ログ「workers設定8 実効仮説5」で発覚）。`runAlns` にも同型の多チェーン機構
    (`runAlnsChains`)を新設し、`options.workers>1` のとき異なるシードで並列実行し `better()`
    （hard→weightedScore→total 辞書式・3.287.0 keep-best統一）で最良を採用する（各チェーンは `runAlnsSingle` を直接呼ぶ
    ＝再帰は構造的に不可能）。「高速」(V5)と、おまかせが高速へ解決される短予算(≤30s)は仮説の
    概念が無く `workers` をそのまま SA チェーン数として使うため対象外。
    - 配分は `hypothesisChainPlan`: `min(workers, コア数)` を仮説数で割り、**余りは先頭仮説から
      +1 ずつ**（例: workers=8/8コア → [2,2,2,1,1]）。コア数クランプは壁時計締切下の
      オーバーサブスクリプション（浅い多チェーン < 深い単一チェーンの品質逆行）を防ぐ。
      仮説数上限は共有定数 `V6NativeOptimizer.MAX_HYPOTHESES`（UI 注記・ログ・エンジンが
      同一プランから表示を導出＝表示と実挙動の乖離を構造的に防止）。
    - `runAlnsChains` は仮説内でも §2.2 絶対評価を維持（HARD=0 到達チェーンが兄弟を即キャンセル・
      非先頭チェーンの合格 report も外側へ転送）し、1チェーンの例外は当該チェーンのみの損失に
      隔離（全滅時のみ再送出）。チェーン毎結果・相異なる解数をログ化。
    - ExtraRefine（残予算 5〜25s の追加精製）はチェーン毎固定費が予算を侵食するため
      `workers ≤ MAX_HYPOTHESES` にキャップ（従来の 5×1 構成のまま）。
- **バックグラウンド最適化**: `OptimizationWorker`（WorkManager 前景サービス）＋
  `OptimizationRepository` ＋ 完了通知。**プロセス kill 耐性に対応**＝約 8 秒ごとに
  `V6NativeOptimizer.liveBest` を `snapshotFile` へ保存し、再起動時に「途中結果から再開」を
  提示（`FOREGROUND_SERVICE_DATA_SYNC`、消費後は `clearFiles` で掃除）。
- **他の案**: 並列探索で得た非採用仮説を `captureAlternatives()` で保持し、`AlternativesCard`
  から個別に「採用」(`applyAlternative()`) 可能。
- **希望で上書き**: `WishApplyCard`／`applyWishes(includeOutOfScope)`（Undo・autoSave・操作ログ・
  再チェック込み）で実装済み。
- **操作ログ**: 設定タブ＞詳細設定の `LogsCard`（操作ログ＋診断ログ）からテキスト/JSON 出力。開始・完了は
  記録されるが、長時間最適化中の詳細進捗監査としてはなお簡素。（旧トップの `OperatorLogView` は
  診断ログの誤ラベル重複のため v3.8 で撤去。）

返却される `ViolationReport`（HARD / SOFT / total / weighted score）は Home / Schedule に表示。
既存の resolved-engine スコア・breakdown・3-evaluator 整合チェックは Analysis タブで従来どおり。

---

# 付録A: Web → Native 移植インベントリ（旧 `v6_web_port_inventory.md` を統合）

出典: `magi_v6_web.html` / P11 package。Web V6 シンボルと Android Native 実装の対応表。
スマートシンク方針: 動く Native コードを盲目的に上書きせず、層ごとに移植・検証し、
ブラウザ専用機構は Android ネイティブ等価物で代替する。

## コア対応サマリ

| Web シンボル / 機能 | Native 実装 / 状態 |
|---|---|
| `makeInitialState` | `StateParser` ＋ 同梱サンプル JSON |
| `_csvEsc` / CSV export | Native CSV ヘルパー・共有/書き出し |
| `parseScheduleCsv` | `ScheduleCsvBridge.parse` |
| `resolveConstraints` | `Problem`（resolved view） |
> **[3.393.0]** 下表は Web 版からの移植先を記録した**当時の対応表**。`V6WebCompat` に置いた Web 専用の
> 助っ人（Worksheet/Workbook 一式・Web 側の診断ビルダ・Web 版 V5 のヘルパー・安定スコア等）は、Web 版が
> 存在しないことをユーザーに確認したうえで撤去した。現存するのは表示色/重大度の4関数＝`ShiftAppearance` のみ。

| `buildImpossibleWishSummary` / `detectImpossibleWishes` | `V6WebCompat` / `V6SanityPort` |
| `buildShiftCountDiagnosticStructured` / `buildSanityCheck` | `V6WebCompat` / `V6SanityPort` ＋ `V6PortAnalyzer` |
| `runSimpleSchedule` | `GreedyMirrorScheduler`（`handleSimple`） |
| `runOptimization` / `runRSIPlusParallel` / `runALNSParallel` | `V6NativeOptimizer`（`handleOptimize`） |
| `runViolationCheck` | `UnifiedViolationChecker`（`handleCheck`） |
| `scoreVecStable` / `betterVec` / `firstDiffTier` / `_magiStableDcb` | `V6WebCompat` 安定スコア |
| `MagiRNG`(xorshift) / `MagiPatternDB` / `zobristHash_v5` | `V6WebCompat` 決定的実装 |
| `buildWs1..buildWs7` | `V6WebCompat` |
| `HomeView`/`OverviewDashboard`/`MobileScheduleView`/`WatchConstraintView` | Compose 画面（`MagiApp` ほか） |
| `getV5Flags` / `ColorSettingsView` / `LogsView` | 詳細設定（`AdvancedSettingsSection`）へ集約 |

## ブラウザ専用（直接移植しない）→ Android 等価物
DOM 操作 / Web Worker Blob URL / Tailwind ランタイム / `window.storage` / browser file API
→ Compose state・ViewModel・ContentResolver・Storage Access Framework・coroutine/WorkManager。

---

# 付録B: Final Bridge 移植ノート（旧 `v6_final_bridge_port_notes.md` を統合）

React `App` コンポーネント内に閉じていた V6 ロジックの最終移植分。

## 新規 Native ファイル
- `V6FinalPort.kt`: `buildBusyDetail` / `confirmDespiteImpossibleWishes` / `handleSmartInitial` /
  `handleCheck` / `handleOptimize` / `getAlgorithmLabel` / `optimizationPlan` / `checkResultWorse`
  （`handleSimple` は 3.393.0 で撤去）
- `V6HotfixPasses.kt`: HF80(戦略的振動) / HF67(職員間スワップ) / HF66(職員内再配分) /
  HF70(異常検出)、後処理連鎖 `HF80 → HF67 → HF66 → HF70`

## Native 置換（1:1 ではなく Android 等価物）
| Web 概念 | Native 置換 |
|---|---|
| `window.confirm`（不可能希望ゲート） | `ImpossibleWishGate` 結果でブロック/許可 |
| `setBusyDetail` / BusyOverlay | `V6FinalPort.BusyDetail` |
| `runPostOptimization` クロージャ | `V6HotfixPasses.runPostOptimization` |
| `window.HF80/67/66/70` | `applyHF80…/applyHF67…/applyHF66…/detectHF70…` |
| `archiveAndClearLogs` | ViewModel の run 境界＋返却 `logs` |
| `checkResultWorse` | `V6FinalPort.checkResultWorse` |

業務ロジックで「手動レビュー要」の積み残しは無し。ブラウザランタイム機構のみ非直接移植。
