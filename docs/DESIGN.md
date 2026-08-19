# MAGI DESIGN.md — デザイン憲法（人にも AI にも読める）

> melta-ui（@tsubotax, https://github.com/tsubotax/melta-ui）の「AI-Ready Design System」思想を
> Jetpack Compose / Material 3 に翻案した MAGI の**デザイン憲法**。melta-ui は Tailwind 前提のため
> *クラス*ではなく*方法*（憲法＋トークン＋禁止事項＋検証ハーネス）を移植する。
>
> **一次ソース**：色/タイポ/角丸＝`MainActivity.MagiTheme`、意味色/シフト色/余白＝`MagiTokens.kt`。
> 本書はそれらの**意図と判断基準**を自然言語で固定する層（＝melta の DESIGN.md 相当）。
> 検証ハーネス＝`tools/design_lint.py`（禁止事項を機械チェック）。

## 1. ブランド identity ＝「Ward（病棟）」
看護師/スタッフが**毎日・片手一本指**で使う professional な道具。目指す質感は
**「静かで正確、信頼できる」＝ clinical calm**。装飾より可読と一目性を優先する。

- **紙**：冷たいクリニカルペーパー（寒色寄りの白 `#F4F7F7`）。旧・暖色クリームから刷新。
- **主色**：ディープティール `#0E6E63`（医療的な落ち着き・確信）。旧・ダスティブルーから刷新。
- **幾何**：引き締めた角丸（10–24dp）。柔らかいプランナー調（12–28dp）から精密な道具へ。
- **本文インク**：純黒を使わず暖色寄りの濃インク `#171D1C`（目が疲れない）。

## 2. 原則（melta-ui 由来）
1. **トークンが唯一の真実源**：色・角丸・余白・タイポは Theme/Tokens 経由で使う。画面に生値を撒かない。
2. **純黒を本文/背景に使わない**：`onSurface` 等のトークン（暖色寄り濃インク）を使う。純黒 `#000000` は
   高コントラスト（UD, `mode=3`）モードの意図的な最大可読時のみ。
3. **重い影を使わない**：階層は「境界（outline）＋ surface トーン（surfaceContainer*）」で作る。
   影は最小限（`Card` の既定 tonalElevation 程度）に留める。
4. **意味で色を選ぶ**：赤=必須(error)／橙=要調整(warn/amber)／緑=成功(tertiary)／灰=情報。
   重大度は色に依存させず、記号（▼不足/▲過剰/⚡窓/破線）でも冗長化する（色覚多様性）。
5. **コントラストを満たす**：本文/on* は 4.5:1 以上、UI（outline/container/大文字）は 3:1 以上。
6. **一目性 > 密度、ただし数値は最大**：見出しは静か、数値（件数・回数）は大きく。
7. **スコアリング不変**：デザイン変更は表示のみ。重み/データ/目的関数に触れない（HF77）。

## 3. トークン参照
### 3.1 色（`MainActivity.MagiTheme` = Material3 colorScheme）
| ロール | ライト | ダーク | 用途 |
|---|---|---|---|
| `primary` | `#0E6E63` | `#86D6C9` | 主操作 CTA・実行中の強調 |
| `secondary` | `#4A6360` | `#B1CCC7` | 補助・選択容器 |
| `tertiary` | `#3E6837` | `#A3D397` | 成功／配布可 |
| `error` | `#B3261E` | `#FFB4AB` | 重大違反（必須） |
| `background` | `#F4F7F7` | `#0E1514` | ベース紙 |
| `surface` | `#FBFDFC` | `#0E1514` | カード面 |
| `surfaceVariant` | `#DAE5E2` | `#3F4947` | 副次面・非活性チップ |
| `onSurface` | `#171D1C` | `#DDE4E1` | 本文（純黒不使用） |
| `onSurfaceVariant` | `#3F4947` | `#BEC9C6` | 補助テキスト |
| `outline` | `#6F7977` | `#899391` | 境界（影の代わりの階層） |

UD（高コントラスト, `mode=3`）＝白地＋黒境界＋濃色ロール。ブランドは deep teal に統一。

### 3.2 意味色・シフト色（`MagiTokens.kt`）
- `magiWarnColors()`＝要調整（amber container/onContainer、テーマ明暗で切替）。
- `MagiAccent`＝シフト/違反アクセント（blue/green/orange/purple/pink/red/gray）。**7 色の色相位置は固定**（機能色＝
  認識性を保つ）。3.89.0 で "Ward" 地に馴染むよう一段深い「診療チャート」調へ調和（ネオン Tailwind-500 系から更新）。
  **保存済みのユーザー指定シフト色は不変**（既定パレット＋直接使用アクセントのみ更新）。
- `ensureReadable(bg, preferred)`＝ユーザー指定色が背景に対し 4.5:1 未満なら黒/白へフォールバック（描画時のみ・保存値不変）。

### 3.3 タイポ（`MagiTheme` Typography）
見出し静か・本文底上げ（最小 14sp）・数値最大。`sp` はシステム文字サイズに追従。
`display/headline`＝数値ヒーロー、`title`＝カード見出し、`body`＝説明、`label`＝チップ/凡例。

### 3.4 角丸（`MagiTheme` Shapes）
`extraSmall 10 / small 12 / medium 14 / large 18 / extraLarge 24`（dp）。**任意 dp の角丸を新規に使わない**。

### 3.5 余白（`MagiTokens.MagiSpacing`）
4dp グリッド：`xs4 / sm8 / md12 / lg16 / xl20`、`section20`（カード間）、`screenH16`（画面左右）。

## 4. 禁止事項（machine-checkable ＝ `tools/design_lint.py`）
- **P1 純黒本文/背景**：`ui/*.kt` で `Color(0xFF000000)` / `Color.Black` を直書き禁止（UD の `MainActivity` は例外）。
- **P2 生 hex の散布**：`ui/*.kt`（`MagiTokens.kt` 除く）に新規の `Color(0x……)` 直書きを増やさない。
  **3.409.5 でラチェット化**＝`P2_BASELINE` を超えたら lint が exit 1（旧: 報告するだけで増えても
  通っていた＝「増分を監視」が実態でなかった）。減ったときも定数の更新を促して落とす（下げ忘れると
  次の増加を見逃すため）。**3.409.6 で baseline を 0 に**＝残っていた1件（`ensureReadable` の第2引数の
  `Color(0xFFFFFFFF)`）を、同じ呼び出しをしている兄弟サイトと同じ `Color.White` に揃えて解消した。
  以後この検査は「既存分を許す」ではなく**1件でも増えたら落ちる**。
- **P3 重い影**：`.shadow(` / 大きな `shadowElevation` の使用禁止（境界＋surfaceトーンで階層）。
- **P4 任意角丸**：カード/チップ級（**≥8dp**）の新規任意角丸禁止（`MaterialTheme.shapes.*` を使う）。
  密なデータセル（**≤6dp**：グリッド/カレンダー/集計）と pill（999）は意図的例外。3.90.0 で tier と同値の
  カード級角丸（12→small・14→medium・10→extraSmall）を全て `MaterialTheme.shapes.*` へ移行（**視覚不変**）。
  **3.409.6 で残る 12 件も tier へ寄せて baseline 0**。3.90.0 が「非同値だから backlog」と残した分で、
  寄せ先は 3.4 の tier 表がそのまま決める＝**8dp → `extraSmall`(10dp, chip/入力)・16dp → `large`(18dp,
  タイル/シート)**。対象は日セル・色見本・集計セル・シフト選択タイル・ドロップダウン枠で、いずれも
  tier 表が名指ししている用途。例外は「最低>上限」のエラー枠だけで、3.403.0 が同じ枠に使った
  `medium` に合わせた（**同じ UI 要素は同じトークン**）。**視覚は +2dp 変わる**（不変ではない）。
  以後この検査も**1件でも増えたら落ちる**。
- **P5 テンプレート食い込み**：`"$count件"` のように変数の直後へ日本語を書かない。Kotlin は日本語を識別子
  文字として扱うため `count件` という未定義シンボルになり**必ずコンパイルエラー**（3.290.0/3.397.0 で実際に
  ビルドを落とした）。`${count}件` と波括弧で囲む。exit 1。
- **P6 message の severity**：`copy(message = …)` を書くなら `messageIsError` も必ず同じ `copy` の中で書く。
  `copy` は既定値でなく**現在値を引き継ぐ**ため、直前がエラーなら成功が失敗色で出る（逆も同じ）。
  コンパイルは通るので lint が要る（3.400.0 で作り込み・3.406.0 で全数修正・**3.409.0 で複数行の
  `copy(...)` を見落としていた検査自身を修正**）。exit 1。
- **P7 二重エンコードの文字化け**：UTF-8 を Latin-1 として読んだ内容を再び UTF-8 で保存した状態を検出。
  UTF-8 として妥当なので既存のどの検査も素通りし、**同梱 asset が文字化けしたまま出荷されていた**
  （3.407.0 で発見・修復）。exit 1。

**lint の対象外（人が守る規律）**
- **S1 スコアリング混入**：デザイン変更コミットで重み/データ/目的関数（Evaluator/Checker/MirrorKeys 重み）に
  触れない。**機械検査は無い**（旧記述はこれを「P5」と呼んでいたが、`design_lint.py` の P5 は上記の
  テンプレート食い込みで**別物**。同じ番号で違う意味を指していたので改番した）。

## 5. 検証ハーネス（壊れたら気づく）
`python3 tools/design_lint.py` が P1–P7 を静的に検査し、違反箇所（`file:line`）と baseline 差分を報告する。
`.github/workflows/design-lint.yml` が push/PR ごとに走らせる（python3 のみ・数秒）。
**落ちる検査は途中で止めず全部まとめて報告する**（3.409.6。旧: 最初に当たった1つで `return 1` していたため、
P2 を直して再実行してはじめて P4 が見える、という往復が起きていた）。melta-ui の「壊れたら気づくハーネス」に相当。

---
更新日: 2026-07-08 ・ 対象: Android ネイティブ（`com.magi.app`）・ 一次ソース: `MainActivity.MagiTheme` / `MagiTokens.kt`
