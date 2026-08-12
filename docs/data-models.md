# data-models.md — データモデル（項目名と型の正解）

> **このファイルの役割**：エンティティ定義・項目名・型の**唯一の正解**。AI が存在しないフィールドを創作するのを防ぐ。ここに無い項目は「存在しない」とみなす。
> **コード基準**：`app/src/main/java/com/magi/app/model/MagiState.kt`（main commit `6769806` 時点）。Web 版の `state` オブジェクトと名前・意味が一致し、JSON が無変換で往復する。
> **最終更新**：2026-06-30

---

## 1. MagiState（ドメイン状態 ＝ JSON 入出力スキーマ）

`data class MagiState`。トップレベルのフィールド：

| フィールド | 型 | 意味 |
|---|---|---|
| `startDate` / `endDate` | `String` | 期間の開始・終了日 |
| `shifts` | `List<Shift>` | シフト種別の一覧 |
| `groups` | `List<Group>` | ユニットグループ（担当可否・covU の単位） |
| `staff` | `List<Staff>` | 職員 |
| `use2Patterns` | `Boolean` | P2 被覆生成が有効か（P1 と **MIN=OR**） |
| `groupShift` | `List<List<Int>>` | 群×シフトの 0/1 マスク（その群が就けるシフト） |
| `groupShiftApt` | `List<List<String>>` | 群×シフトの「適切回数」目標（空＝未設定） |
| `schedule` | `List<List<Int>>` | 初期割当。`schedule[i][j]` = 職員 i・日 j のシフト index |
| `wishes` | `Map<String, Int>` | 希望シフト。キー `"i,j"` → シフト index |
| `staffRange` | `Map<String, Range>` | 個人×シフトの回数範囲。キー `"i,k"` → `{lo,hi}`（LimMin/LimMax） |
| `needDay1` / `needDay2` | `Map<String, String>` | 日別の必要数オーバーライド。キー `"k,j"` |
| `cons1` | `List<C1Row>` | 制約 C1（窓） |
| `cons2` | `List<C2Row>` | 制約 C2（個人合計） |
| `cons3` / `cons3n` / `cons3m` / `cons3mn` | `List<C3Row>` | C3 族（MUST / 禁止 / Want / Hate の連勤パターン） |
| `cons41` | `List<C41Row>` | 群レンジ |
| `cons42` | `List<C42Row>` | 群ペア（同日併存不可） |
| `skillGroups` | `List<Group>` = `[]` | スキルグループ（ユニットとは別の第2分類。担当可否には使わない） |
| `cons41s` / `cons42s` | `List<C41Row>` / `List<C42Row>` = `[]` | スキル群版の C41 / C42 |
| `shiftColors` | `Map<String, String>` = `{}` | 表示色の上書き。キー＝シフト記号 → `"#rrggbb"`（**表示のみ・エンジン無影響**）。特殊キー `"__vio__"` ＝違反色 |
| `extras` | `Map<String, Any?>` = `{}` | 未モデル化の項目を逐語保持（往復の無損失化） |

### 計算プロパティ（保持しない／導出）
`staffCount = staff.size` ／ `dayCount = schedule[0].size`（空なら0）／ `shiftCount = shifts.size` ／ `groupCount = groups.size` ／ `skillGroupCount = skillGroups.size`。

---

## 2. サブ型（フィールドと型）

| 型 | フィールド | 備考 |
|---|---|---|
| `Shift` | `name: String`, `kigou: String`, `need1: String`, `need2: String` | need1/need2 = P1/P2 の既定必要数（`""`/null＝要件なし） |
| `Group` | `name: String`, `kigou: String` | kigou＝制約で使う記号 |
| `Staff` | `name: String`, `groupIdx: Int`, `skillIdx: Int = 0` | groupIdx→ユニット群（担当可否/covU）、skillIdx→スキル群（C41s/C42s 専用） |
| `Range` | `lo: String`, `hi: String` | 個人×シフトの下限/上限（LimMin/LimMax） |
| `C1Row` | `day1: String`, `shiftKigou: String`, `day2: String` | 「day1 日窓で shiftKigou を day2 回」 |
| `C2Row` | `shiftKigou: String`, `count: String` | 個人の shiftKigou 合計の目標 |
| `C3Row` | `pattern: List<String>` | 連勤の列パターン（記号の並び） |
| `C41Row` | `groupKigou: String`, `shiftKigou: String`, `l: String`, `u: String` | 群 X のシフト Y を1日に [l,u] 回 |
| `C42Row` | `g1Kigou: String`, `g2Kigou: String`, `s1Kigou: String`, `s2Kigou: String` | 群 g1 の s1 と 群 g2 の s2 が同日併存不可 |

> 数値項目（need1/need2/count/lo/hi/l/u 等）は**文字列**で保持する（空欄＝未設定を表現するため）。利用時に整数へ解釈する。

---

## 3. キー規約（Map のキー文字列）

| 用途 | キー形式 | 例 |
|---|---|---|
| セル系（職員×日） | `"i,j"` | `wishes`, `violationCells` |
| 回数系（職員×シフト） | `"i,k"` | `staffRange`, `countViolations` |
| 被覆系（シフト×日） | `"k,j"` | `needDay1/2`, `needViolations` |

`schedule[i][j] < 0` ＝ 公休（未割当）。希望が反映済みか＝ `wishes["i,j"] == schedule[i][j]`。

---

## 4. UiState（画面表示用の派生状態）

`data class UiState`（`ui/MagiUiState.kt`）。MagiState から ViewModel が生成する**表示専用**の状態。主なフィールド：

- **読込/履歴**：`loaded`, `canUndo`, `canRedo`
- **規模**：`staff`, `days`, `shifts`, `groups`, `use2`
- **最適化状態**：`running`, `hasResult`, `bestHard`(Long), `bestSoft`(Long), `initHard/initSoft`, `weightedScore`(Double), `totalViolations`, `iters`, `itersPerSec`, `elapsedMs`, `workers`, `budgetSec`(=300), `softPolish`(=true), `v6Algorithm`
- **違反の内訳と場所**：`breakdown: Map<String,Int>`（19種の件数）、`violationCells["i,j"]→"vio-xxx"`（セル系）、`countViolations["i,k"]`（回数系）、`needViolations["k,j"]`（被覆系）
- **表示素材**：`schedule`, `resultSchedule`(ws6確定), `liveSchedule`(計算中の最良盤面), `wishes`, `staffNames`, `staffGroupSymbols`, `shiftSymbols`, `shiftColorHex`, `shiftTextHex`, `violationColorHex`（空＝テーマerror、`shiftColors["__vio__"]`）
- **誘導/診断**：`satisfaction`(%), `copilotHint`, `impossibleWishCount`, `coverageDiag`(covU原因診断), `settingIssues`, `fixSuggestions`, `alternatives`, `polishExhausted`
- **中断**：`interruptedRun`, `interruptedInfo`
- **その他**：`v6: V6PortReport?`, `message`, `opLog`, `logs`, `startDate`

---

## 5. 創作禁止の原則
- 上の表に**無いフィールド・カラムは存在しない**。推測で項目名を作らない。
- 名前は Web 版 `state` と一致する。改名・別名を勝手に導入しない。
- 改修で項目が増減したら**このファイルを即更新**する（data-models と business-logic は最も stale 化しやすい）。

関連：制約の判定・重みは [`business-logic.md`](./business-logic.md)、モジュール地図は [`architecture.md`](./architecture.md)。
