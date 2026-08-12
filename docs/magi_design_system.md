# MAGI デザインシステム仕様（実装直前レベル）

添付画像（Time Allowances / Shortcuts / Reminders / Calendar）の“テイスト”を抽出し、
Apple直コピーではなく **Jetpack Compose / Material 3 に自然に落とし込む** ための実装仕様。

- 対象: `app/src/main/java/com/magi/app/`（純Kotlin / Compose / Material3）
- ベース方針: 「明るく静かなベースの上に、柔らかいカード・大きな数値・意味ある色分け・
  見やすいカレンダー・迷わない操作導線を置く」
- 互換: Material3 の `MaterialTheme.colorScheme / typography / shapes` を一次ソースとし、
  不足分（spacing・意味色・シフト色・アクセント）だけを `MagiTokens` で補う。

実装状況の凡例: ✅=実装済 / 🟡=部分 / ⬜=未

> 全画面・ポップアップ・メッセージの画像付き定義は [`screen_spec.md`（画面仕様書）](screen_spec.md) を参照。

---

## 0. 導入順（Phase）

| Phase | 内容 | 状況 |
|---|---|---|
| A | テーマ整備（色・角丸・タイポ・スペーシング） | ✅ 色/角丸/タイポ（`MainActivity.MagiTheme`） + ✅ `MagiTokens`（spacing/意味色） |
| B | 共通コンポーネント（カード/タイル/チップ/セグメント/ゲージ/ダイアログ） | 🟡 `QuickActionTile`/`MagiSegmentedControl`/`MagiScoreGauge`/`MagiTagChip`/`MagiSectionHeader`✅ 他⬜ |
| C | 画面反映（ホーム/勤務表/分析/編集/設定） | 🟡 ホーム✅ 勤務表(カレンダー/非色手がかり)✅ 分析(ゲージ)🟡 編集(月次/年次マスター7折りたたみ節)✅ 設定(冗長除去・見出し統一)✅ |
| D | 画面最適化（横/折りたたみ等） | 対象外（ユーザー指示により非対応） |

---

## 1. カラートークン

### 1.1 Material3 colorScheme（実装済 / `MainActivity.kt`）

ライト（既定）:

| ロール | HEX | 用途 |
|---|---|---|
| `background` | `#F5F5F7` | 明るく静かなベース |
| `surface` | `#FFFFFF` | カード面 |
| `surfaceVariant` | `#F0F1F4` | 副次面（SurfaceSubtle） |
| `outline` | `#D9DCE3` | 淡い境界 |
| `onSurface` | `#111318` | 主要テキスト（TextPrimary） |
| `onSurfaceVariant` | `#6B7280` | 補助テキスト（TextSecondary） |
| `primary` | `#3B82F6` | CTA / 実行中（青） |
| `tertiary` | `#22C55E` | 成功 / 配布可（緑） |
| `error` | `#EF4444` | 重大違反（赤） |

ダーク: `background #111318` / `surface #1B1D22` / `primary #60A5FA` / `tertiary #4ADE80` /
`error #F87171`。UD（高コントラスト, `mode=3`）は白地 + `#000` 境界の独立スキーム。

### 1.2 MagiTokens 意味色・アクセント（未実装 / 追加対象）

colorScheme に無い「意味色／シフト色」を一元化する。`@Immutable object MagiAccent`。

| トークン | HEX | 意味 |
|---|---|---|
| `blue` | `#3B82F6` | 実行中 / 早番 |
| `green` | `#22C55E` | 成功 / 日勤 |
| `orange` | `#F59E0B` | 警告 / 夜勤 |
| `purple` | `#A855F7` | 遅番 / 個人属性 |
| `pink` | `#EC4899` | 希望 / 個人属性 |
| `red` | `#EF4444` | 重大違反 / NG制約 |
| `gray` | `#9CA3AF` | 休み / 無効 |

意味付け: 最適化成功=green / 実行中=blue / 警告=orange / 重大違反=red / 希望・個人属性=pink|purple。

### 1.3 シフト色マッピング（既定パレット）

**重要**: 実データの色（`state.shiftColors[kigou]` → `V6WebCompat.resolveShiftColor`）が**最優先**。
データに色が無い場合のみ、以下の既定テイストにフォールバックする（`shiftAccentFallback(kigou,name)`）。

| シフト | 既定色 |
|---|---|
| 早番 | blue `#3B82F6` |
| 日勤 | green `#22C55E` |
| 遅番 | purple `#A855F7` |
| 夜勤 | orange `#F59E0B` |
| 休み | gray `#9CA3AF` |
| 希望 | pink `#EC4899` |
| NG/違反 | red `#EF4444` |

各ピル/タイルのテキスト色は `V6WebCompat.pickTextColor(bg)`（既存）で黒/白を自動選択。

---

## 2. Shape / Spacing / Elevation

### 2.1 Shapes（実装済 / `MainActivity.kt`）
`extraSmall 12 / small 16 / medium 20 / large 24 / extraLarge 28`（dp, RoundedCorner）。
- カード = `medium`(20) / タイル = `large`(24) / ピル・チップ = `CircleShape`(999相当)。

### 2.2 Spacing（未実装 / `object MagiSpacing`）
4dp グリッド。

| 名称 | dp | 用途 |
|---|---|---|
| `xs` | 4 | アイコン-文字間 |
| `sm` | 8 | チップ内・密な行 |
| `md` | 12 | カード内行間 |
| `lg` | 16 | カード内余白（標準） |
| `xl` | 20 | カード内余白（広） |
| `section` | 20 | セクション（カード）間 |
| `screenH` | 16 | 画面左右パディング |

（現状ホームは `horizontal=16` / `spacedBy(20)` で既にこの値に整合。）

### 2.3 Elevation
影はごく薄く。Card は `CardDefaults.cardElevation(1.dp)` 相当（既定の tonal でも可）。
過度な影は使わず、面（surface/surfaceVariant）と outline で分離する。

---

## 3. Typography（実装済 / `MainActivity.kt`）

| ロール | size/weight | 用途 |
|---|---|---|
| `displaySmall` | 34 Bold | 大数値（スコア等） |
| `headlineSmall` | 24 Bold | 画面タイトル |
| `titleLarge` | 20 SemiBold | セクションタイトル |
| `titleMedium` | 17 SemiBold | カード見出し |
| `titleSmall` | 15 SemiBold | 行タイトル |
| `bodyLarge/Medium/Small` | 16/15/13 | 本文・補助 |
| `labelLarge/Medium` | 15/13 | ボタン・ラベル |

ルール: 画面タイトル大・本文静か・**数値最大**。`sp` はシステム文字サイズに追従。
大数値は `displaySmall`（34）を基準にし、特に強調する箇所のみ `fontSize=44.sp` をローカル指定。

---

## 4. 共通コンポーネント仕様（API 確定）

すべて `com.magi.app.ui.components`（新規パッケージ）に置く想定。既存composableは移行先を併記。

### 4.1 MagiCard ⬜
```kotlin
@Composable fun MagiCard(
    modifier: Modifier = Modifier,
    onClick: (() -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
)
```
- 形状 `shapes.medium`(20) / 面 `surface` / 内余白 `lg`(16) / 子間 `md`(12)。
- onClick 非null時は `Card(onClick=…)`。タップ領域は最小 48dp を侵さない。
- 既存 `Card(...){ Column(padding(16), spacedBy(12)) }` パターンの置換。

### 4.2 MagiSectionHeader ✅
```kotlin
@Composable fun MagiSectionHeader(title: String, subtitle: String? = null, trailing: @Composable (() -> Unit)? = null)
```
- title=`titleLarge`、subtitle=`bodySmall`+`onSurfaceVariant`。trailing は右寄せアクション。

### 4.3 MagiStatCard / BigStat 🟡（`BigStat` 実装済 → 名称統一）
```kotlin
@Composable fun MagiStatCard(label: String, value: String, accent: Color? = null, modifier: Modifier = Modifier)
```
- value=`displaySmall`(34 Bold)、label=`labelMedium`+`onSurfaceVariant`。
- accent 指定時は value をその色に。複数並置は `Row{ Modifier.weight(1f) }`。

### 4.4 MagiQuickActionTile ✅（`QuickActionTile` 実装済 → 移設）
```kotlin
@Composable fun MagiQuickActionTile(
    icon: ImageVector, title: String, container: Color, onClick: () -> Unit,
    enabled: Boolean = true, modifier: Modifier = Modifier,
)
```
- 形状 `shapes.large`(24) / 高さ最小 86dp / パステル容器色 / アイコン上・タイトル下。
- `enabled=false`（実行中）は淡色＋クリック無効。グリッドは2〜3列 `QuickActionGrid`。

### 4.5 MagiSegmentedControl ✅（Time Allowances テイスト）
```kotlin
@Composable fun MagiSegmentedControl(options: List<String>, selected: Int, onSelect: (Int) -> Unit, modifier: Modifier = Modifier)
```
- Material3 `SingleChoiceSegmentedButtonRow` を採用（自前描画より堅牢）。
- 選択中=`secondaryContainer`/`primary` の塗り、各セグメント最小高 40dp。
- 用途: 勤務表の 月/週/スタッフ別 切替、設定の実行モード、編集の制約レベル（標準/強い/弱い）。

### 4.6 MagiScoreGauge ✅（Time Allowances の中央大数値）
```kotlin
@Composable fun MagiScoreGauge(score: Int, max: Int = 100, label: String, sub: String? = null)
```
- 中央に大数値（`fontSize=44.sp` Bold）、下に細い `LinearProgressIndicator(progress=score/max)`。
- 色: score高=green / 中=blue / 低=orange（しきい値は呼び出し側）。
- 用途: 分析タブの総合スコア、ホームの満足度（`CopilotCard` の満足度行を置換可）。

### 4.7 MagiLabeledSlider ⬜
```kotlin
@Composable fun MagiLabeledSlider(label: String, value: Int, range: IntRange, onChange: (Int) -> Unit, suffix: String = "")
```
- 行: 左ラベル(`titleSmall`) / 右現在値(`titleMedium` 強調) / 下 `Slider`。
- 用途: 設定（実行時間・仮説数1..5・研磨強度・並列数）、編集（必要人数・制約強度）。

### 4.8 MagiTagChip ✅（Reminders のカテゴリ）
```kotlin
@Composable fun MagiTagChip(text: String, color: Color, leadingIcon: ImageVector? = null)
```
- `CircleShape` / 高さ24–32dp / `color.copy(alpha=.16f)` 背景 + `color` 文字。
- 用途: シフト種別・制約タグ・希望タグ・違反タグ・グループタグ。

### 4.9 MagiListRow ⬜（Reminders の軽い一覧）
```kotlin
@Composable fun MagiListRow(leadingIcon: ImageVector? = null, title: String, subtitle: String? = null,
                            trailing: @Composable (() -> Unit)? = null, onClick: (() -> Unit)? = null)
```
- 左アイコン色付き / 中央 title+subtitle 階層 / 右 件数or状態（`MagiTagChip` 等）。行高 最小 56dp。
- 用途: スタッフ/制約/希望/アラート/違反内訳の一覧。

### 4.10 MagiEditSheet ⬜（Reminders の編集ダイアログ / ボトムシート）
```kotlin
@Composable fun MagiEditSheet(title: String, onDismiss: () -> Unit, onConfirm: () -> Unit,
                              confirmEnabled: Boolean = true, content: @Composable ColumnScope.() -> Unit)
```
- `ModalBottomSheet`。上に title(`titleLarge`)、本文は入力欄を目立たせる、下に明確な確定/取消ボタン（最小48dp）。
- 用途: スタッフ編集・希望編集・必要人数編集・色設定。既存 `ShiftPickerSheet` と作法を統一。
- **実装メモ（v3.35）**: 専用 `MagiEditSheet` 名では作らず、**§4.14 の `DialogHeader`＋共有3ボタン＋`W1Shell`** で同等を実現（タイトル＋右上✕／確定=右・取消=左・48dp）。ボトムシート（`ShiftPickerSheet`/`WishBulkSheet`）も `DialogHeader` を共有し作法統一済み。

### 4.11 MagiColorPickerRow ⬜
```kotlin
@Composable fun MagiColorPickerRow(selected: Color, palette: List<Color> = MagiAccent.all, onSelect: (Color) -> Unit)
```
- 丸いカラーチップ（36dp）横並び、選択中はリング。既存 AlertDialog 色ピッカーの置換。

### 4.12 MagiCalendarMonthView / DayShiftCell / ShiftEventPill ✅（最重要・勤務表）
```kotlin
@Composable fun MagiCalendarMonthView(year: Int, month: Int, days: List<DayCell>, onDayClick: (Int) -> Unit)
data class DayCell(val day: Int, val pills: List<ShiftPill>, val hasViolation: Boolean, val shortageNote: String?)
@Composable fun ShiftEventPill(symbol: String, color: Color)
```
- 日曜始まり・曜日ヘッダ（`月火水木金土日`）。日付は大きく左上、シフトは小さな色付きピル、右上に違反マーカー（赤）、下部に人数不足/過多の補助。
- セル最小 64dp 角丸 `small`(16)。違反日は赤みの補助表示（背景 `error.copy(alpha=.08f)`）。
- `CalendarModeSwitcher` = `MagiSegmentedControl(["月","週","スタッフ別"])`。

### 4.13 MagiScreenScaffold ⬜
```kotlin
@Composable fun MagiScreenScaffold(title: String, actions: @Composable RowScope.() -> Unit = {}, content: @Composable ColumnScope.() -> Unit)
```
- `background` 地 / 上部に大タイトル / 縦スクロール `Column(padding(horizontal=screenH), spacedBy(section))`。
- 既存の各タブ Column ラッパを段階的に置換。

### 4.14 ポップアップ共有部品 ✅（`Affordance.kt`・全ダイアログ/シート/ピッカーの正典）
```kotlin
@Composable fun DialogHeader(title: String, onClose: () -> Unit)          // タイトル＋右上✕
@Composable fun DialogConfirmButton(text: String, onClick: () -> Unit, enabled: Boolean = true) // 確定=塗り
@Composable fun DialogDismissButton(onClick: () -> Unit, text: String = "キャンセル")            // 取消=枠線
@Composable fun DialogDangerButton(text: String, onClick: () -> Unit, enabled: Boolean = true)  // 破壊的=⚠＋エラー色
```
- **`DialogHeader`**: `Row[ Text(title, titleLarge, weight 1f) + IconButton(Close) ]`。フォーム系ダイアログ・ボトムシート・ピッカーのタイトルスロットに置き、ドラッグ不要の明示的な閉じる導線を上部に与える（no-drag方針）。単純な確認ダイアログ（削除の確認 等）には付けず素のタイトル。
- **3ボタン**: いずれも `Modifier.heightIn(min = 48.dp)`。`AlertDialog` の `confirmButton`=右・`dismissButton`=左に割り当てることで、Material標準により**確定=右／取消=左**が全画面で固定。`DialogDangerButton` は `Icons.Filled.Warning`(18dp)＋エラー色で破壊的操作（削除/全リセット/すべて削除）に使用。
- **規約**: ダイアログ/シートのボタンに生の `Button`/`TextButton` を直書きしない（左右逆・サイズ不揃い・危険色の付け忘れを防ぐ）。多択（CSV取込の5択等）は `DialogConfirmButton` を縦積みにし、選択肢に✓は付けない（選択であり確定ではない）。
- **フォーム外殻 `W1Shell`**(`Ws1Editor.kt`): `AlertDialog`＋縦スクロール本文の共通殻。`DialogHeader`＋3ボタンを内包し、シフト/グループ/スタッフ/一括追加の各フォームで共有。
- 適用: 全 `AlertDialog`（約10箇所）＋`W1Shell`（フォーム4種）＋ボトムシート2種（`ShiftPickerSheet`/`WishBulkSheet`）＋ピッカー（色/職員）＝**全ポップアップ**。

---


> 下図は **トークン正確モック**（`MainActivity.kt`/`MagiApp.kt` の実トークンで描画）であり、
> 実機スクリーンショットではない。`tools/mock_render_dogfood.py` で再生成できる
> （出力先 `docs/screens/`）。運用フロー **初期解 → 手動修正 → 最適化** をたどって検証した版。

| 画面 | 図 |
|---|---|
| ① ホーム | ![ホーム](screens/01_home.png) |
| ② 勤務表 7日 | ![勤務表7日](screens/02_schedule7.png) |
| ③ シフト選択シート | ![シフト選択](screens/03_picker.png) |
| ④ カレンダー月表示 | ![カレンダー](screens/04_calendar.png) |
| ⑤ 確認ダイアログ | ![ダイアログ](screens/05_dialog.png) |

### 5.1 ホーム ✅
`StatusHero`✅ → `CopilotCard`(満足度ゲージ)✅ → `CoverageDiagnosisCard`✅ →
`SummaryCard`(`BigStat`×3)✅ → `ActionCard`✅ → `AlternativesCard`✅ → `QuickActionGrid`✅。

![ホーム](screens/01_home.png)

状態バッジ（配布可/要確認/実行中）・満足度・**人員不足の原因**（充足不可/充足可能を `MagiTagChip` で明示）
までをひと目で提示し、下部コマンドバーの「最適化する」へ親指誘導。

### 5.2 勤務表 ✅
`ScheduleGrid` の表示切替を **`MagiSegmentedControl`(7日/カレンダー/1ヶ月)** に統一✅。
月表示は **`MagiCalendarMonthView` + `DayShiftCell` + `ShiftEventPill`**（曜日列・シフト色ピル・不足赤枠）✅。
セル編集は `ShiftPickerSheet`(親指ゾーンの大タイル)✅。シフト色は §1.3。
違反は赤リング+赤ドットに加え、**非色手がかり(HARD実線/SOFT破線)+凡例 `ViolationLegend`** ✅。

![勤務表7日](screens/02_schedule7.png)
![シフト選択](screens/03_picker.png)
![カレンダー](screens/04_calendar.png)

### 5.3 編集 🟡
3サブタブ **今月の調整／シフト希望／基本マスター**（`MagiSegmentedControl`→`editScope`）。基本マスターは**5節に集約**（①シフト・グループ・スタッフ ②スキルグループ ③回数[目標/個人/グループ] ④人数と組み合わせ[C41/C42/C41s/C42s] ⑤並び・くり返し[cons系]）。各節先頭に `SectionNote`。フォーム/ダイアログは `DialogHeader`＋共有3ボタンで統一（§4.14）。`WishApplyCard`✅。一覧の `MagiListRow` 化は段階移行。

![ダイアログ](screens/05_dialog.png)

### 5.4 分析 🟡
上部に **一般/プロ** 切替(`proMode`)。`OverviewDashboard`(ようす) / `CheckSummaryView`(チェック概要) / `BreakdownCard`(**違反内訳=全19種/100%**・fair/weekly含む) / `BottleneckCard` / `FixSuggestionCard`(1手提案)。`重大のみ`フィルタ✅。プロ時のみ `V6DashboardCard`＋`WeightTableCard`。プロ表示は冗長な説明文を非表示（構造的不足ヒント・状態・実データは常時）。

### 5.5 設定 ✅
外観(`AppearanceCard`：自動/明/暗/UD ＋片手 ＋かんたん/プロ・`MagiSegmentedControl`化✅) /
**最適化設定**(`SettingsCard`：並列・時間予算・計算方式・仕上げ最適化・版表示) /
データ(`DataActionsCard`：JSON/CSV入出力・コンポーネント別出力) /
詳細設定(折りたたみ・既定=閉：1ヶ月俯瞰・ログ・違反色トークン)。
**設定の重複を排除（v3.8）**：計算方式/ログの二重表示を解消（旧 FlagsView・トップの OperatorLogView を撤去）、カード見出しを `titleMedium` に統一。

---

## 6. 必須の操作原則（テイスト導入時の品質ゲート）

1. タップ領域は広く（最小 48dp、リスト行56dp、カレンダー日セル64dp）。
2. 情報を詰め込みすぎない（カード内は1カード1テーマ、セクション間 `section`20dp）。
3. 色に意味を持たせる（§1.2 の意味付けを逸脱しない）。
4. 進捗は必ず視覚化（実行中=blue、`MagiScoreGauge`/`LinearProgressIndicator`）。
5. リストは静かに、重要数値は強く（`displaySmall`）。
6. Androidらしい操作を壊さない（ボトムシート・触覚・スワイプ・OSファイルピッカーを維持）。

---

## 7. 実装メモ

- 一次ソースは Material3 テーマ。`MagiTokens`（`MagiAccent` / `MagiSpacing` / `shiftAccentFallback`）だけを追加し、
  `MagiColors/MagiShapes/MagiTypography` 相当は `MaterialTheme.*` を直接参照する（二重管理を避ける）。
- 新規コンポーネントは `com.magi.app.ui.components` に集約し、画面は段階移行（churn最小）。
- `Surface(onClick=…)` 等 `@ExperimentalMaterial3Api` を使う箇所は明示 opt-in。
- lint は release で非ゲート化済（個人テスト用）。配布版にするときは `checkReleaseBuilds` を戻す。

---

## ユニバーサルデザイン／HCI の根拠（論文・規格にもとづく設計原則）

スマホ特化・指1本・中学生ITレベルのオペレーターという要件を、確立した文献・規格に対応づけて担保する。
各原則は「根拠 → 実装での担保」の形で記す（ドッグフーディングのチェック観点でもある）。

1. **タッチ標的の大きさ（Fitts の法則 / WCAG 2.5.5 / Material）**
   - 根拠: Fitts (1954) "The information capacity of the human motor system…"; WCAG 2.1 SC 2.5.5 Target Size (44×44 CSS px); Material 3 は最小 48dp。
   - 担保: 主要な操作は `heightIn(min = 48.dp)`／56dp。Material3 は `TextButton`/`IconButton` に 48dp の最小インタラクティブ領域を既定で強制。勤務表セルは 52〜58dp。

2. **選択肢を減らす（Hick–Hyman の法則）**
   - 根拠: Hick (1952), Hyman (1953) — 選択肢数に応じて決定時間が対数的に増える。
   - 担保: 思考誘導ホームは **大ボタン1つ＋控えめな補助1つ**。1画面1目的。詳細は折りたたみ（詳細設定）に隔離。

3. **親指ゾーン／片手操作（モバイル到達性）**
   - 根拠: Hoober & Berkman (2011) "Designing Mobile Interfaces"; Hoober のタッチ実地調査（親指中心・画面下部が到達容易）。
   - 担保: 主操作は**下端の固定コマンドバー**。`片手で使う` トグルで内容を下方へ寄せる。横スクロール必須を作らない（勤務表は7日/カレンダー/月に分割）。

4. **色だけに頼らない（WCAG 1.4.1 / 色覚多様性）**
   - 根拠: WCAG 2.1 SC 1.4.1 Use of Color; Color Universal Design (CUDO)。
   - 担保: 違反は色＋**形**（必須=実線枠＋塗りドット／要調整=破線枠＋中空リングドット）＋凡例。テーマに「見やすさ(UD)」を用意。

5. **コントラスト（WCAG 1.4.3）**
   - 根拠: WCAG 2.1 SC 1.4.3 Contrast (Minimum) 4.5:1（本文）。
   - 担保: 各コンテナに対応する `on*` 前景色（Material3 ロール）を使用。状態色は緑/黄/赤＋十分な前景コントラスト。

6. **スクリーンリーダー対応（WCAG 4.1.2 / 1.1.1）**
   - 根拠: WCAG 2.1 SC 4.1.2 Name, Role, Value; 1.1.1 Non-text Content。
   - 担保: 勤務表セルに `contentDescription`（例「7/28(水) シフト Dﾃ・必須違反、タップで変更」）。アイコンボタンに説明、装飾は `contentDescription = null`。

7. **テキストの可読・リフロー（WCAG 1.4.4 / 1.4.10）**
   - 根拠: WCAG 2.1 SC 1.4.4 Resize Text, 1.4.10 Reflow。
   - 担保: 重要な状態文は省略せず折り返す（Compose は内容に合わせて高さ可変）。固定高での切り詰めを避ける。

8. **一貫性と標準（Nielsen ヒューリスティクス #4, #6）**
   - 根拠: Nielsen (1994) "Enhancing the explanatory power of usability heuristics" — 一貫性と標準／記憶より認識。
   - 担保: 全画面で同一のカード様式・ボトムナビ・トップバー・余白・用語（守れていない約束／できあがり度／コンピューターが組んでいます）・状態色を統一。

9. **認知負荷の最小化（Sweller / 漸進的開示）**
   - 根拠: Sweller (1988) Cognitive Load Theory; Progressive Disclosure（Nielsen）。
   - 担保: 専門記号（c3n/covU/ALNS 等）は画面に出さず、上級/開発項目は詳細設定へ。思考誘導で「次の一手」だけを提示。

10. **グループ化（ゲシュタルト：近接・類同）**
    - 根拠: Wertheimer (1923) ゲシュタルト原理。
    - 担保: 関連項目はカードで近接配置、同種は同形・同色（違反内訳の重大度別チップ等）。

> 検証: 上記 1–10 を「全画面ドッグフーディング」のチェックリストとして用いる（`tools/mock_render_current.py` でトークン忠実モックを生成して点検）。

---

## カラーテイスト：Daily Planner 調（2026-06-14）

デイリープランナー系アプリに共通する「穏やか・温かい・余白多め」のテイストを、固有ブランド資産は複製せず**自前のトークンとして再構成**して取り入れた（介護現場・非技術オペレーターの安心感に合致）。

- **ライト**：地＝温かいクリーム紙 `#F7F2EA`、カード＝白 `#FFFFFF`、文字＝温かいチャコール `#3A352F`（純黒を避ける）。
  主操作＝落ち着いたダスティブルー `#4E6FC2`、補助＝やわらかラベンダー `#7E79C0`、成功＝穏やかセージ `#3DA776`、
  注意＝温かい赤 `#DE5A52`、罫＝温かい淡色 `#E8E0D4`。
- **ダーク**：地/面を温かいチャコール（`#18161A`/`#221F24`）、文字 `#EDE8E0`。アクセントは従来踏襲。
- **高コントラスト(UD)**：アクセシビリティ優先のため**変更しない**（純白地・高コントラスト維持）。
- 形（カード20dp・タイル24dp・ピル）と書体（見出し大・本文静か）は既存が既にプランナー調のため踏襲。
- **意味色は不変**（緑=OK / 赤=注意 / 青=主操作）。違反の非色手がかり（実線/破線＋ドット）も維持。
- 実装は `MainActivity.MagiTheme` の `lightColorScheme`/`darkColorScheme` のみ。シフトセル色（`MagiAccent`/`V6WebCompat.resolveShiftColor`）は判別性維持のため据え置き。

---

## Material 3 トーナル配色への準拠（2026-06-14）

「Daily Planner 調」を**手選びのサブセット**から、**Material 3 のトーナル・カラーシステム**（Material Theme Builder 相当）へ作り直し、全ロールを種色から導出してアクセシビリティを実測担保した。

- **種色**：主＝ダスティブルー、副＝ラベンダー、三次＝セージ、誤＝M3標準レッド。**ニュートラルを暖色（クリーム）へ傾けて**プランナーの温かさを表現。
- **全ロールを定義**：`primary/secondary/tertiary/error` ＋各 `container/on*`、`surface` と **`surfaceContainerLowest…Highest` / `surfaceBright/Dim`**、`surfaceVariant`、`outline/outlineVariant`、`inverseSurface/inverseOnSurface/inversePrimary`、`surfaceTint`、`scrim`。カードや段差は**トーナル・エレベーション**（白の段差でなく同系トーンの層）で表現。
- **コントラスト実測（WCAG/M3）**：本文ペア（on*/コンテナ・surface）はすべて **4.5:1 以上**、UI ペア（primary/surface・outline/surface）は **3:1 以上** を満たすことを検証済み。
  - ライト例：onSurface/surface=15.5、onPrimary/primary=5.6、primary/surface=5.3、outline/surface=4.2、各 onContainer/container=12〜13。
  - ダーク例：すべて 5.5〜14.4。
- **高コントラスト(UD)テーマ**は最大アクセシビリティ用途として独立維持（変更なし）。
- 実装は `MainActivity.MagiTheme` の `lightColorScheme`/`darkColorScheme`。検証スクリプトの考え方は Material Design Color Tool / M3 ガイドラインのコントラスト基準に準拠。

### 状態色のロール対応（M3 完遂・2026-06-14）
思考誘導カード等の状態色を、ハードコードからテーマロール／集約トークンへ統一：
- **主操作/実行中** → `primaryContainer`、**成功（配れます）** → `tertiaryContainer`、**埋められない** → `errorContainer`（いずれもテーマロール）。
- **警告（もう少し）** は M3 に warning ロールが無いため `magiWarnColors()`（`MagiTokens`）に集約。surface の明るさで明/暗を判定し、明=淡アンバー#FBEAD0/#6B4E00（実測6.55:1）、暗=#5B4300/#FBEAD0（7.91:1）。`RiskChip` の不足1件もこのトークンを使用。
- これで状態色はすべて「テーマロール or 文書化された独自セマンティック」になり、散在ハードコードを解消。
