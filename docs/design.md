# design.md — 設計（インタフェース・処理フロー）

> **このファイルの役割**：「どう作られているか」の全体像。主要インタフェースと処理フローを渡す。静的なモジュール地図・依存関係は [`architecture.md`](./architecture.md) に分離（本書はそれを前提に、**動的な流れと接点**を述べる）。
> **対象**：Android ネイティブ（Kotlin / Jetpack Compose / Material3 / MVVM）。
> **最終更新**：2026-06-30

---

## 1. アーキテクチャ要約
役割分担は **UI＝表示と操作のみ／`MagiViewModel`＝唯一のハブ（状態・操作・最適化起動・I/O）／`v6` エンジン＝探索本体／`model`＝データ／`work`＝中断耐性のある背景実行**。詳細な構成図とエンティティ表は `architecture.md`。

---

## 2. 主要インタフェース（接点）

### 2.1 UI → ViewModel（操作の委譲）
画面は状態を持たず、操作を `MagiViewModel` のメソッド/コールバックに委譲する。代表例（`OperatorNextActionCard` の引数）：
- `onMake`（勤務表をつくる＝最適化）／`onDraft`（下書き＝簡易作成）／`onStop`（停止）
- `onExport`（CSV書き出し・配布）／`onSchedule`（勤務表へ）／`onFix`（手直し）／`onSetup`（編集へ）
- セル編集：`onCellClick(i, j)` → シフト選択 → ViewModel が `schedule` 更新。

### 2.2 ViewModel ⇄ ドメイン/エンジン
- 状態：`MagiViewModel` が `MagiState`（ドメイン）を保持し、表示用に `UiState` を生成する。
- 最適化：`MagiViewModel` が `V6NativeOptimizer` を起動。エンジンは `MagiState` から `Problem` を構築し、`SaOptimizer`／各オペレータ／seeder／Web一致層を使って探索、`Evaluator`/`DeltaEvaluator` で採点（重みは `MirrorKeys.weights`）。
- 背景実行：長時間の最適化は `OptimizationWorker`（WorkManager の**前景サービス**）が `V6NativeOptimizer` を回し、中断耐性とスナップショットを担保。
- 入出力：JSON は `StateParser`、CSV は `ScheduleCsvBridge` を介して `MagiState` と相互変換。

### 2.3 型の境界
- `MagiState`（=JSON 入出力スキーマ）と `UiState`（=表示専用）は明確に分離。詳細は [`data-models.md`](./data-models.md)。
- 制約の判定・重み・エラー方針は [`business-logic.md`](./business-logic.md)。

---

## 3. 処理フロー

```
[起動] MainActivity → MagiApp（5タブ・状態は ViewModel から）
   │
[読込] JSON/CSV → StateParser/ScheduleCsvBridge → MagiState（自動保存）
   │
[編集] UI 操作 → ViewModel が MagiState 更新 → Problem 再構築
   │
[最適化] onMake → OptimizationWorker（前景）→ V6NativeOptimizer
   │        seed → SA/ALNS/operators → Evaluator/DeltaEvaluator
   │        → weightedScore（MirrorKeys.weights）→ 最良解
   │        （中断時はスナップショットから復帰）
   │
[反映] 結果 → ViewModel → UiState → UI（違反枠/希望/集計/フェーズ）
   │
[配布] HARD=0 なら「配布可」→ CSV/印刷で書き出し
```

フェーズ表示（探索→完成→狩猟）は `OperatorNextActionCard` の状態分岐に名前を与えたもの：未最適化→探索／`bestHard==0`→完成／HARD>0→狩猟。

---

## 4. 主要な設計判断
- **単一ハブ MVVM**：状態と副作用を `MagiViewModel` に集約し、UI を純表示に保つ。
- **前景サービスでの最適化**：長時間処理を WorkManager 前景で実行し、画面を閉じても継続・中断復帰。
- **差分評価（Δ）**：`DeltaEvaluator` で手ごとの差分採点を行い高速化。
- **ファサード層**：`V6SanityPort`/`V6FinalPort`/`V6PortAnalyzer` が事前診断・UI 向け入口・分析を担う。
  （旧「Web 一致層」の `V6WebCompat` は 3.393.0 に撤去＝Web 版は存在しない。生き残った表示色/重大度は `ShiftAppearance`。）
- **重みの単一の真実**：`MirrorKeys.weights` を UI も最適化器も参照し、ドリフトを防止。
- **1本指・最小UI**：ドラッグ非依存、主操作は下部固定、二重符号化（色＋形）。詳細は [`screen_spec.md`](./screen_spec.md) / [`magi_design_system.md`](./magi_design_system.md)。

---

## 5. 詳細リンク
- モジュール地図・依存：[`architecture.md`](./architecture.md)
- データ定義：[`data-models.md`](./data-models.md)／業務ルール：[`business-logic.md`](./business-logic.md)
- 画面挙動：[`screen_spec.md`](./screen_spec.md)／エンジン移植：[`v6_engine_native_port.md`](./v6_engine_native_port.md)
