# data-models.md — データモデル（項目名と型の正解）

> **このファイルの役割**：エンティティ定義・項目名・型の**唯一の正解**。AI が存在しないフィールドを創作するのを防ぐ。ここに無い項目は「存在しない」とみなす。
> **コード基準**：`app/src/main/java/com/magi/app/model/MagiState.kt`。Web 版の `state` オブジェクトと名前・意味が一致し、JSON が往復する。
> **最終更新**：2026-08-18（3.394.0 — 3.393.0 で UiState から撤去した**結果スナップショット8種**
> （`resultSchedule` / `hasResultSnapshot` / result 専用マップ6種）を §4 から削除し、件数を 82 → 74 へ。
> 3.396.0 で `iters` / `itersPerSec`（反復数＝作り手の指標。操作画面から外し診断ログへ一本化）を削除し **72** へ。
> 3.390.0 で **§4 の UiState 一覧を全フィールドへ刷新**。旧記述は30フィールドが未記載で、
> `*Families` 3種・result 専用マップ・調整トグル4種・診断5種などが丸ごと落ちていた。
> 3.389.0 で §3 の2件を訂正（`schedule[i][j] < 0` ＝公休は誤り／`skillIdx = -1` は正規の値）。
> §1・§2 のフィールド表は実装と一致を再確認済み。全体像は [`sudo_model.md`](./sudo_model.md) 参照）

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
| `Staff` | `name: String`, `groupIdx: Int`, `skillIdx: Int = 0` | groupIdx→ユニット群（担当可否/covU）、skillIdx→スキル群（C41s/C42s 専用）。**`skillIdx = -1` は「未所属」の正規の値**（UI の「(なし)」・3.70.0）。`ssk[i] == groupIdx(>=0)` が常に偽になるので cons41s/cons42s から安全に外れる。群削除時の再割当も `-1` へ寄せる（3.328.0） |
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

**`schedule[i][j]` は必ず有効なシフト index**（0..K-1）。希望が反映済みか＝ `wishes["i,j"] == schedule[i][j]`。

> **[3.389.0 訂正]** 旧記述「`schedule[i][j] < 0` ＝ 公休（未割当）」は**誤り**だった。
> 「休」は `kigou == "休"` で解決される**通常のシフト index**（`Problem.restIdx` = `shifts.indexOfFirst { it.kigou == "休" } ?: 0`）
> であって負値ではない。負値は `MirrorCore.normalizeSchedule` が**範囲外セルへ付けるセンチネル `-1`**で、
> 意味は「公休」ではなく**「不正な値」**（行が短い場合は `0` で埋める）。`Problem.initialAssignment` も
> `k < 0` を `0` へクランプする。「休は特殊な OFF ではなく通常のシフト種の一つ」は 3.345.0 で全面的に
> 徹底された前提で、weekly もシフト別に均すので休を特別扱いしない。

---

## 4. UiState（画面表示用の派生状態）

`data class UiState`（`ui/MagiUiState.kt`）。MagiState と `ViolationReport` から ViewModel が生成する**表示専用**の
状態。**全72フィールド**（下記は全数。`MagiUiState.kt` と機械照合済み）。

**読込/履歴**（3）：`loaded`, `canUndo`, `canRedo`

**規模**（5）：`staff`, `days`, `shifts`, `groups`, `use2`

**最適化の状態**（9）：`running`, `hasResult`, `initHard`/`initSoft`(Long), `bestHard`/`bestSoft`(Long),
`totalViolations`, `weightedScore`(Double), `elapsedMs`

**計算の設定**（10）：`workers`(既定=コア数を1..8でクランプ), `budgetSec`(=300), `v6Algorithm`(=AUTO),
`softPolish`(=true), `nativeAccel`(=true), `nativeParity`(=true) と、**既定 OFF の調整トグル**
`blockSwapC3nFilter` / `wideC3nBreak` / `adaptiveEscape` / `portfolioRoleParallelSa`
（意味と見直しの条件は [`algorithm_portfolio.md`](./algorithm_portfolio.md)）

**違反の内訳と場所**（8）— **3つのキー空間**を混ぜないこと（§3 参照）：

| フィールド | 型 | 意味 |
|---|---|---|
| `breakdown` | `Map<String,Int>` | 19族の件数（キー = `MirrorKeys.all`） |
| `violationCells` | `"i,j"→"vio-xxx"` | セル系。**最重1クラスのみ** |
| `countViolations` | `"i,k"→"vio-xxx"` | 回数系。**最重1クラスのみ** |
| `needViolations` | `"k,j"→"vio-xxx"` | 被覆系。**最重1クラスのみ** |
| `violationCellFamilies` | `"i,j"→List` | セルの**全**違反クラス（重み降順）。3.111.0 |
| `countFamilies` | `"i,k"→List` | 回数キーの全クラス。3.353.0 |
| `needFamilies` | `"k,j"→List` | 被覆キーの全クラス。3.370.0 |
| `distLocations` | `"weekly"→[[i,dev]]` / `"fair"→[[i,k,dev]]` | セル位置を持たない2族の偏り箇所（内訳パネル専用） |

> `*Families` の3つは「重い違反（low 90 / covU 8000 等）が同じキーの軽い違反を隠す」問題への対処。
> 先頭要素は対応する単一クラスマップと常に一致する。内訳→場所タップと E7 フィルタはこちらを読む。

**表示素材**（12）：`schedule`, `liveSchedule`(計算中の最良盤面), `wishes`(`"i,j"→shiftIdx`),
`staffNames`, `staffGroupSymbols`, `shiftSymbols`, `shiftColorHex`, `shiftTextHex`,
`violationColorHex`（必須違反・空＝テーマ error / `shiftColors["__vio__"]` 由来）,
`violationSoftColorHex`（要調整・`__vioSoft__` 由来）, `violationFamilyColorHex`（族別の個別色・`__vioFam_<fam>__` 由来）,
`reviewMemos`（見直し候補メモ・セッション内のみ）

**編集/再構成**（4）：`constraintsEdited`, `structureEdited`, `editRev`, `prevBackupAvailable`

> `editRev` は構造編集ごとに単調増加する。`structureEdited` は Boolean なので既に true だと `copy` が同値になり
> StateFlow が emit せず、`currentSchedule == null` のときは `refreshCheck` も早期 return するため、
> 編集画面が再構成されず「+/- で数字が変わらない」実機バグを生んでいた（3.185.0/3.189.0）。
> `editRev` があると必ず distinct な UiState になる。

**誘導/診断**（14）：`satisfaction`(%), `copilotHint`, `polishExhausted`, `impossibleWishCount`,
`settingIssues`, `fixSuggestions`, `fixSearching`, `fixFocusName`, `alternatives`,
`coverageDiag`(covU/covO の原因診断), `forbiddenDiag`(禁止連続の壁・3.280.0),
`c1Plateau`(窓の要件が直せなかった理由・3.322.0), `observedPinBlockedAttempts` と `pinTargets`
（回数固定が却下した候補の**計測できた下限**と対象・3.326.0）

**中断**（2）：`interruptedRun`, `interruptedInfo`

**その他**（5）：`v6`(`V6PortReport?`), `message`, `opLog`(操作ログ), `logs`(診断ログ), `startDate`

> **各グループに件数を書いてあるのは機械照合できるようにするため。** 合計 3+5+9+10+8+12+4+14+2+5 = **72** で
> `MagiUiState.kt` の `val` 宣言数と一致する。グループ本文の名前を数えて宣言側と突き合わせれば、
> **フィールドが増減したのにここを直し忘れた**ことが件数のずれとして出る（実際、本文を書いた直後の照合で
> 4グループとも数字が間違っていた）。件数を落とすと照合は無意味になるので、更新のたびに数字も直すこと。

---

## 5. 創作禁止の原則
- 上の表に**無いフィールド・カラムは存在しない**。推測で項目名を作らない。
- 名前は Web 版 `state` と一致する。改名・別名を勝手に導入しない。
- 改修で項目が増減したら**このファイルを即更新**する（data-models と business-logic は最も stale 化しやすい）。

関連：制約の判定・重みは [`business-logic.md`](./business-logic.md)、モジュール地図は [`architecture.md`](./architecture.md)。
