# MAGI Android — モジュール構成と依存・呼び出し関係（知識グラフ）

本書は **MAGI ネイティブ（Android / Kotlin / Jetpack Compose / Material3 / MVVM）** の主要モジュール・サービスと、その依存・呼び出し関係を **entities / relations / observations** 形式でまとめたもの。各 entity の役割は短い observation として併記する。関係は実コード（import / 参照）で裏取りしている。画面の挙動は `screen_spec.md`、デザイン基盤は `magi_design_system.md` を参照。

- リポジトリ: `ichirocc/magi7ichiro` ／ パッケージ: `com.magi.app`
- ソース: `app/src/main/java/com/magi/app/`（パッケージ: `model/` `v6/` `ui/` `work/` ＋ `MainActivity`）

---

## レイヤ概観（上から下へ）

```
MainActivity (Activity)
        │ hosts
        ▼
MagiApp (UI シェル・5タブ)
        │ observes / calls
        ▼
MagiViewModel  ── produces ─▶ UiState ──▶ UI 画面群（勤務表/分析/編集…）
        │ holds                                   │ use
        │ ▶ MagiState（ドメイン＝JSONスキーマ）      ▼
        │ calls                              Affordance / MagiComponents（共有部品）
        ▼
V6NativeOptimizer（最適化エンジン核）
   ├ uses ─▶ SaOptimizer / V6 operators / V6 seeders / V6 web-parity
   ├ scores-with ─▶ Evaluator / DeltaEvaluator ── use ─▶ MirrorCore（18違反種・重み）
   └ depends-on ─▶ Problem ◀── built-from ── MagiState

OptimizationWorker（WorkManager 前景サービス）── runs ─▶ V6NativeOptimizer
StateParser（JSON I/O） / ScheduleCsvBridge（CSV I/O）── map ─▶ MagiState
```

役割の分担：**UI** は表示と操作のみ、**ViewModel** が唯一のハブ（状態・操作・最適化起動・I/O）、**v6 エンジン**が探索本体、**model** がデータ、**work** が中断耐性のある背景実行。

---

## Entities（type ｜ 役割＝observation）

### アプリ基盤
| Entity | type | 役割 |
|---|---|---|
| `MainActivity` | Activity | アプリの入口。テーマ/Shapes を設定し `MagiApp` をホストする |
| `MagiApp` | UI-Shell | 画面骨格。5タブ（ホーム/勤務表/編集/分析/設定）＋ TopBar（状態チップ）＋ BottomCommandBar |
| `MagiViewModel` | ViewModel-Hub | **中央ハブ**。状態保持・全操作・最適化起動・I/O・`UiState` 生成（最大級・約124KB） |
| `UiState` | UI-State | UI 表示用の派生状態（違反/breakdown/schedule/色/満足度 等） |

### ドメイン・データ（model）
| Entity | type | 役割 |
|---|---|---|
| `MagiState` | Domain-Model | ドメイン状態＝**JSON 入出力スキーマ**（shifts/groups/staff/cons1..42/wishes/staffRange/shiftColors） |
| `StateParser` | IO-JSON | JSON ↔ `MagiState` の解析・直列化 |

### 最適化エンジン（v6）
| Entity | type | 役割 |
|---|---|---|
| `Problem` | Engine-Model | `MagiState` から構築する最適化問題（次元・制約・重み） |
| `V6NativeOptimizer` | Engine-Core | **主最適化器**。SA + ALNS + GLS + Tabu + Path Relinking を統括、最良解を研磨（約70KB） |
| `SaOptimizer` | Engine-SA | 焼きなまし（Metropolis 基準）本体 |
| `Evaluator` / `DeltaEvaluator` | Engine-Scoring | 違反スコアの計算 / 差分評価（高速化） |
| `MirrorCore`（`MirrorKeys`） | Constraint-Defs | **18 違反種と重み**＝`weightedScore` の唯一の真実 |
| `V6SearchOperators` / `V6LateOperators` / `V6SwapSuggester` | Engine-Operators | 近傍・交換・修復などの探索手 |
| `SmartInitialScheduler` / `GreedyMirrorScheduler` | Engine-Seed | 初期解の生成（後者はテスト専用の旧生成器） |
| `V6SanityPort` / `V6FinalPort` / `V6PortAnalyzer` | Engine-Facade | 事前診断・UI 向けファサード・分析層 |
| `ShiftAppearance` | UI-Support | シフト記号→表示色 と 違反キー→重大度 の唯一の解決元（3.393.0 に `V6WebCompat` から切り出し） |
| `ScheduleCsvBridge` | IO-CSV | 勤務表 / 希望 CSV ↔ `MagiState`（文字コード自動判定） |

### 背景実行（work）
| Entity | type | 役割 |
|---|---|---|
| `OptimizationWorker` | Background-Service | WorkManager の**前景サービス**で最適化を実行。中断耐性・スナップショット |

### UI 画面・部品（ui）
| Entity | type | 役割 |
|---|---|---|
| `MagiScheduleViews` | UI-Schedule | 勤務表グリッド / セル / シフト選択シート / 集中モード |
| `MagiDashboardCards` ＋ `V6RemainingScreens` | UI-Analysis | 分析カード（違反の内訳18 / 俯瞰 / チェック概要 / ボトルネック / 改善提案） |
| `Ws1Editor` / `StaffRangeEditor` / `ConstraintEditor` / `WishEditor` / `NeedDayEditor` / `SkillGroupEditor` / `CountSettingsScreen` / `ShiftColorEditor` | UI-Editors | 「基本マスター」の各エディタ |
| `MagiSetupCards` | UI-Setup | 初期設定・外観・データ操作などのカード群 |
| `Affordance` ＋ `MagiComponents` | UI-Components | 共有部品（`DialogHeader`・3ダイアログボタン・`MagiSegmentedControl` 等） |

---

## Relations（呼ぶ・依存する など）

UI 層
- `MainActivity` **hosts** `MagiApp`
- `MagiApp` **observes** `MagiViewModel`
- `MagiApp` **renders** `MagiScheduleViews`, `MagiDashboardCards`, `V6RemainingScreens`, 各 Editor, `MagiSetupCards`
- UI 画面群 **call** `MagiViewModel`（操作の委譲）
- UI 画面群 **use** `Affordance`, `MagiComponents`

ViewModel ハブ
- `MagiViewModel` **produces** `UiState`
- `MagiViewModel` **holds** `MagiState`
- `MagiViewModel` **calls** `V6NativeOptimizer`
- `MagiViewModel` **enqueues**（WorkManager 経由）`OptimizationWorker`
- `MagiViewModel` **uses** `StateParser`（JSON）, `ScheduleCsvBridge`（CSV）

エンジン（v6）
- `OptimizationWorker` **runs** `V6NativeOptimizer` ／ **uses** `StateParser`
- `V6NativeOptimizer` **depends-on** `Problem`
- `V6NativeOptimizer` **uses** `SaOptimizer`, `V6SearchOperators`/`V6LateOperators`/`V6SwapSuggester`, `SmartInitialScheduler`/`GreedyMirrorScheduler`, `V6SanityPort`/`V6FinalPort`/`V6PortAnalyzer`
- `V6NativeOptimizer` **scores-with** `Evaluator` / `DeltaEvaluator`
- `DeltaEvaluator` **builds-on** `Evaluator`
- `Evaluator` **depends-on** `Problem`
- `Problem` **built-from** `MagiState`
- `Evaluator`系・`MagiDashboardCards` **use** `MirrorCore`（重み）

データ・I/O
- `StateParser` **parses/serializes** `MagiState`（JSON）
- `ScheduleCsvBridge` **maps** `MagiState`（CSV）

---

## 主要フロー（呼び出し連鎖）

1. **起動**: `MainActivity` → `MagiApp`（タブ・状態を `MagiViewModel` から取得）。
2. **最適化（前景）**: ユーザ操作 → `MagiViewModel.optimize()` → `OptimizationWorker`（前景サービス）→ `V6NativeOptimizer`（seed → SA/ALNS/operators、`Evaluator`/`DeltaEvaluator` で採点、`MirrorCore` の重みで `weightedScore`）→ 結果を `MagiViewModel` → `UiState` → UI 反映（中断時はスナップショットから復帰）。
3. **編集 → 再最適化**: UI 編集 → `MagiViewModel` が `MagiState` 更新（自動保存 JSON）→ `Problem` 再構築 → 再最適化。
4. **保存/読込/取込**: JSON は `StateParser`、CSV は `ScheduleCsvBridge` を介して `MagiState` と相互変換。

---

## 補足
- 本書は「主要モジュール」を対象とした要約であり、全ファイルの網羅ではない（`v6/` には Hotfix/解析系の補助ファイルも存在する）。
- `Hf63Infeasibility` は呼び手が自己テストのみで実質死蔵（Web 側と同様）。
- 関係は import / 参照に基づくが、実行時の動的呼び出しの一部は含まれない場合がある。
- 関連ドキュメント: 画面挙動＝`screen_spec.md`、デザイン基盤＝`magi_design_system.md`、エンジン移植＝`v6_engine_native_port.md`。
