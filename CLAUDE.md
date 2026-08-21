# CLAUDE.md — MAGI ShiftOptimizer (Android) 引き継ぎ

> このファイルは Claude Code 向けのプロジェクトメモリです。チャット側で進めた作業の引き継ぎを兼ねます。

> **まず読む（ドキュメント入口）**：設計・仕様・業務ルールは [`README.md`](./README.md) の「ドキュメント目次」から各 `docs/*.md` に分かれています。実装・調査の前にそこで当たりをつけてください。とくに **業務ルール＝[`docs/business-logic.md`](./docs/business-logic.md)**、**データ項目＝[`docs/data-models.md`](./docs/data-models.md)**（存在しない項目を創作しない）。
> **更新ルール（stale 化させない）**：コードを改修したら、影響する文書（特に `business-logic.md` / `data-models.md`）と `README.md` の目次・最終更新を**同じコミットで**更新する。事実が変わりやすい順に独立させているのは、ここを最新に保つだけでハルシネーションの大半を抑えるため。
> 応答は簡潔・結論先出し・日本語。コード識別子は英語のまま。

## 教訓メモ（更新して使う）
作業の教訓は [`docs/lessons.md`](./docs/lessons.md) に置く。**新しいメモを作らず、このファイルを更新する**
（該当行を書き換える／表に1行足す／同趣旨の行は統合）。最上部の1行要約も実態に合わせて直す。
内容＝修正した点↔機能した点の対／作る前にやめた判断／測り方／この環境の検証手段と穴／レビューの扱い。

## 基本方針
- 不明な点は積極的に質問する
- 質問する時は常にAskUserQuestionを使って回答させる
- **選択肢にはそれぞれ、推奨度と理由を提示する**
  - 推奨度は⭐の5段階評価

## スキル自動起動（2026-07-18 ユーザー決定）
- **タスク着手前にスキル一覧を確認し、該当スキルを Skill ツールで自動起動する**（superpowers流）:
  新機能/設計→brainstorming・計画深掘り→dig・実装/バグ修正→test-driven-development・
  バグ調査→systematic-debugging・完了宣言前→verification-before-completion・
  複数ステップ計画→writing-plans→executing-plans/subagent-driven-development・文章推敲→writing-clearly-and-concisely
- **genshijin 常時起動（通常レベル）**: 全応答を圧縮体（敬語なし・体言止め・助詞省略可）で書く。
  技術用語/コード識別子は正確維持。破壊的操作警告・セキュリティ説明のみ Auto-Clarity で通常日本語。
  解除は「原始人やめて」「通常モード」の明示指示のみ。
- 実装: `~/.claude/settings.json` の SessionStart フック（`~/.claude/session-bootstrap.md` を注入）＋本節の二重化。
  リモートコンテナは使い捨てのためフックは環境ごと消えうる＝本節が永続側の正。
- **プラグイン正規導入済み（2026-07-25, ユーザー指示）**: genshijin@genshijin v1.4.0（サブスキル6種:
  commit/compress/crew/help/review/stats 付き）・superpowers@superpowers-marketplace v6.2.0（14スキル）・
  dig@kuu-marketplace v3.0.1 を `claude plugin install`（userスコープ＝`~/.claude/plugins/`）で導入。
  環境が再構築されて消えた場合の再導入コマンド:
  ```
  claude plugin marketplace add InterfaceX-co-jp/genshijin && claude plugin install genshijin
  claude plugin marketplace add obra/superpowers-marketplace && claude plugin install superpowers@superpowers-marketplace
  claude plugin marketplace add fumiya-kume/claude-code && claude plugin install dig@kuu-marketplace
  ```
  ※fumiya-kume/claude-code の実マーケット名は `kuu-marketplace`。genshijin はソースリポジトリ直接追加
  （この環境の git proxy では公式ディレクトリ(anthropics系)が解決できないため）。
  ※`~/.claude/skills/` に前セッションの手動コピー版（genshijin/dig/superpowers系16件）が残存＝プラグインと
  重複するが無害（一覧ノイズのみ）。掃除する場合はセッション開始直後にバックアップ退避してから削除。

## プロジェクト概要
看護師/スタッフのシフト表を最適化する Android ネイティブアプリ（Kotlin + Jetpack Compose）。
VBA/Web 版から移植した「MAGI V6」最適化エンジン（SA + ALNS + Tabu + GLS + LNS + VNS + LAHC +
PathRelinking + ChainSwap + 適応的オペレータ重み + RSI++ 等）を内蔵。

- **規模の上限（業務前提・2026-07-28 ユーザー明示）**: **職員は最大30名 / 期間は最大1か月（=31日）**。
  この範囲を超えるデータは想定しない。既存のビット化経路はすべてこの範囲に収まる:
  `C3nBitScan.usable`(T<=64) と C++ `SaChunk` の `useBits`(S<=64 && T<=64) は**実運用では常に真**＝
  スカラーへのフォールバックは防御であって通常経路ではない。`adaptiveBlockLengths` の最大28日も
  「2月まるごと」を1ブロックで扱うための上限で、1か月という前提と整合する。
  新しくビット/固定長配列を使う実装を足すときは 30×31 を基準に見積もってよい。
- パッケージ/applicationId: `com.magi.app`（namespace も同じ）
- minSdk=36 (Android 16+), compileSdk/targetSdk=36, java.time ネイティブ可, NDK/desugaring 不使用
  （※Android 17 会話バブル対応済。compileSdk は 36 のまま。**API 37 の platform SDK は 3.409.12 で stable 公開を確認済み**＝3.173.0/3.373.0 の「未公開」は解消。移行手順は下記セクション参照）
- リポジトリ: `ichirocc/magi7ichiro`（public）
- UI 制約: **片手一本指**（ドラッグ不可）、**最小デザイン**（冗長な安全表示はエンジン側に持たせ、操作画面は効率優先）
- 全作業・UI 文言は日本語
- **デザイン憲法＝[`docs/DESIGN.md`](./docs/DESIGN.md)**（melta-ui 流の AI-Ready 設計）。トークン一次ソース＝`MainActivity.MagiTheme`
  （色/タイポ/角丸）＋`MagiTokens.kt`（意味色/シフト色/余白）。禁止事項 P1-P4 は `tools/design_lint.py` で機械検査。
  色/角丸/影を変えるときは DESIGN.md の原則（純黒不使用・重い影不使用・任意値禁止・スコア不変）に従う。

## ビルド/検証（重要）
**このサンドボックスは Android も素の Kotlin もコンパイル不可。** Kotlin の検証は GitHub Actions
"Release Build" ワークフロー（`gradle assembleRelease`）でのみ行う。lint は走らない/警告errorなし。
Claude Code 環境に Android SDK があれば直接 `./gradlew assembleRelease` でビルド可。無ければ CI を使う。

- **アルゴリズム検証は python3** で行う（後述の検証ハーネス）。コンパイル不可でもロジックはPythonで等価確認できる。
- CI ログ本体は results-receiver.actions.githubusercontent.com 上にあり取得不可。**コンパイルエラーは
  目視＋静的チェック（波括弧balance・フィールド名照合）で発見**する。`view`/`grep` を駆使。
- CI 監視 API: `api.github.com/repos/ichirocc/magi7ichiro/actions/runs`（name=='Release Build',
  head_branch でフィルタ）。status は `/actions/runs/{id}`、artifacts は `/actions/runs/{id}/artifacts`、
  失敗stepは `/actions/runs/{id}/jobs`。ビルド ~4-5分 → debug-key APK ~10.9MB。
- 変更ごとに versionCode++ と versionName 更新（`app/build.gradle.kts`）。タグ `vX.Y.Z-...` を push。

## アーキテクチャ（主要ファイル）
エンジンは `app/src/main/java/com/magi/app/v6/`:
- `MirrorCore.kt` — **`UnifiedViolationChecker`（UIの違反表示・提案の基準＝source of truth）**。
  `check(state, schedule) -> ViolationReport{violations, needViolations, countViolations, breakdown, hard, total, weightedScore}`。
  `Problem`（`cachedProblem(state)`）, `canDo(i,k)`, `allowedShiftsForStaff(i)`, `countMatrix`, `coverage`,
  `normalizeSchedule`。`MirrorKeys`（hard/soft/all のキー分割）と weightedScore の重み定義もここ。
- `Evaluator.kt` / `DeltaEvaluator.kt` — **最適化器の目的関数**（SA の受理判定）。`Evaluator(p)`（3.393.0 で `c3RunMode` は撤去＝単一シフト連は常に run-deficit）。
  Delta は差分評価。`SaOptimizer` が Delta×Full の整合チェック（安全網）を行うため**両者は常に一致させる**。
- `C3Run.kt` — `isSingleShiftSeq(seq)`, `rowDeficit(a,i,k,L)`（単一シフト連の不足評価）。
- `V6FinalPort.kt` — `handleOptimize`（最適化オーケストレーション）, `handleCheck`（UnifiedViolationChecker）。
  最終番兵 `checkResultWorse`（入力より悪化したら入力へ復帰）。
- `V6NativeOptimizer.kt`/`V6HotfixPasses.kt`/`V6LateOperators.kt`/`V6SearchOperators.kt` — 探索本体・各オペレータ。
- `V6SwapSuggester.kt` — **`FixSuggester.suggest(...)`**（ユーザー向け修復提案。7種の手を探索）。
- `Problem.kt` — `C1(day1,shiftIdx,day2)` 等の制約データ型。

UI は `app/src/main/java/com/magi/app/ui/`:
- `MagiApp.kt` — タブ: 0=ようす(ダッシュボード), 1=勤務表(編集+集計), 2=設定, 3=詳細, else=外観/データ。
- `MagiViewModel.kt` — 状態管理。`findFixSuggestions`/`applyFixSuggestion`、`refreshCheck`(currentSchedule検査)。
  ジョブ: `job`/`checkJob`/`fixJob`（連続タップ競合回避）。
- `MagiUiState.kt` — `schedule`, `staffNames`, `staffGroupSymbols`, `shiftSymbols`, `countViolations("i,k")`,
  `needViolations("k,j")`, `resultSchedule`, `breakdown` 等。
- `MagiScheduleViews.kt` — `ScheduleGrid`, `StaffCalendarCard`, **`TallyCard`（シフト集計：職員別/日別＋違反ハイライト）**。
- `MagiDashboardCards.kt` — `BreakdownCard`, `FixSuggestionCard` 等。`MagiTokens.kt` — `MagiAccent`(色)。

## 制約ファミリーと意味（confirmed）
- **c1**（窓制約, SOFT, 重み4）: `C1(day1=窓, shiftIdx=単一シフト, day2=最低数)`。窓day1内にshiftIdxがday2回以上。
  **担当不可スタッフは対象外（canDoガード）**。構造上単一シフトのみ（複数種類変種なし）。
- **c2**（職員別合計, SOFT, 重み1）。
- **c3族**（ws4の列パターン。ws3=希望シフトとは別物）:
  - c3 = MUST/want（SOFT, 重み3）, c3m = Want（SOFT, 重み2）— **非forbidden**。
  - c3n = FORBIDDEN（HARD, 重み7000）, c3mn = Hate（SOFT, 重み12）— **forbidden**。
  - 評価モデル: **非forbiddenの単一シフト連 → run-deficit**（C3Run.rowDeficit。完成runを罰しない）。
    それ以外（複数シフト連 / forbidden）→ **窓マッチ #fire**。
- **c41/c42/c41s/c42s**（群/日 範囲・スキル群変種, SOFT, 重み1）。
- **covU**（人員不足, HARD, 重み8000）/ **covO**（人員過剰, SOFT, 重み1.0）。被覆は同日のみ（夜勤繰越なし）。
  ※ covO 重みは 0.5→1.0 に統一（2026-07-13, HF77 明示指示）。旧: 最適化器(Evaluator/Delta/C++)=amount×1.0 に対し
  チェッカー weightedScore のみ×0.5 で factor-2 乖離（族寄与≠weightedScore寄与）。「最適化器を正」として 1.0 で一致。
  need1=P1, need2=P2。lo=need1, hi=(use2 && need2>=0 ? need2 : need1)。MIN/OR条件は2世代前からの意図的設計。
- **low/high**（staffRange=各職員の各シフト回数の下限/上限, SOFT, 重み90/45。amount計上）。
- **apt**（適切回数=`groupShiftApt[群][シフト]` の**群単位双方向目標**, SOFT, 重み1, L1偏差`|回数-目標|`）。
  担当可シフトのみ有効（`Problem.apt` 構築時に bucket=canDo ガード）。不足=赤(vio-aptLow)/超過=橙(vio-aptHigh)。
  **群目標は個人の staffRange[lo,hi] でクランプ**（範囲外の群目標は到達不能＝解消不能な幻のapt違反を防ぐ。
  例: Dﾃを2-2固定の職員に群目標10 → 2にクランプ）。低/高(staffRange) とは別系統（LimMin/LimMax は別画面 ws5）。
  旧: 目的関数にもチェッカーにも未統合だった。
- **fair**（グループ内公平化, SOFT, 重み1, L1偏差）。群×担当ONシフト(`bucket[g]`)ごとに、メンバー回数の
  `round(平均)` からの L1 偏差和。同群の職員間で各シフト回数を均す。`Problem.groupMembers` 使用、m<2の群は対象外。
  目的関数(Evaluator/Delta)/チェッカー3者に統合。UI内訳チップには出さない（常時非ゼロになりやすいため weightedScore/total
  のみ算入）。旧: 後処理polish(`applyGroupShiftEqualizePolish`)＝目的関数外の「整え」だけだった。
- **weekly**（7日周期(曜日)シフト平準化, SOFT, 重み1, L1偏差）。職員ごとに勤務日(非休)の**曜日別カウント**の
  `round(勤務日数/7)` からの L1 偏差和。weekday(j)=`(dow0+j)%7`（`Problem.dow0`=startDate曜日オフセット %7 /
  `Problem.restIdx`=休index）。「毎週おなじ曜日に偏る」を均す。共通ソース=`weeklyDevOfBucket(wd[7])`。
  Evaluator/Delta/チェッカー3者に統合（fairと同型）。UI内訳では fair と同様に「曜日の偏り」チップに件数表示（場所マップ
  は無し＝タップ先は空、fairと同じ）。旧: 後処理polish(`applyWeeklyEqualizePolish`, 分散指標)だけ＝目的関数外の「整え」
  だった（polishは keep-best/mainNotWorse ガードのため併存＝無害・冗長）。
- **pref**（希望シフト未充足, HARD, 重み9000）/ **groupViol**（群外シフト, HARD, 重み10000）。

weightedScore 階層: groupViol(10000) > pref(9000) > covU(8000) > c3n(7000) > low(90) > high(45) >
c3mn(30)=c1(30) > c3(3) > c3m(2) > c2/c41/c42/c41s/c42s/apt/fair/weekly/covO(1)。（covO は 2026-07-13 に 0.5→1.0 統一。
c1 は 4→5→15→**30**、c3mn は 12→15→**30**＝いずれも HF77 明示指示。この行が stale だと監査が誤誘導されるので、
重みを変えたら `MirrorKeys.weights`・`Evaluator.fullEvalParts`・`DeltaEvaluator` の集約式・`magi_native.cpp` の
5箇所・言語跨ぎ期待値3ファイル・`docs/business-logic.md` と**同じコミットで**揃える）

> **決定記録（D3, 業務判断）**: apt/weekly/fair の重みは**現状維持（各1）で確定**（業務担当者レビュー済）。
> ws8/ws9 等と同様、以後この3族の重み変更は**再提案しない**（明示的な数値指示があった場合のみ変更）。
> **（2026-08-02 再確認）** 3.345.0 で weekly の定義をシフト別へ変えて mass が3〜4倍になったため、
> 重みを下げる案（重み付き寄与を旧定義と同程度に戻す）を実測つきで提示したが、業務判断は**重み1のまま**。
> 代償（user_state 相当のデータで c1 が中央 62→84＝1〜2割増、HARD は全データセットで不変）は受容済み。
> weekly を実際に減らすという定義変更の意図を優先する。この件も**再提案しない**。

## 目的関数の統一（完了。最重要の達成事項）
**最適化器（Evaluator/Delta）とUI/提案（UnifiedViolationChecker）が乖離していた目的関数を統一した。**
原則: **チェッカーを source of truth とし、より正確なモデルへ両者を寄せる**。重みは線形集約点で適用し
Δ×フル整合を維持（`soft = sc*W + ..., sc += dC ⇒ soft_new = soft_old + dC*W`）。

| 乖離 | 解消 | コミット |
|---|---|---|
| (a) covO 最適化器で無罰則 | 最適化器 soft に追加 | 2.28.0 |
| (a') covO 最適化器×1.0 vs チェッカー weightedScore×0.5 の factor-2 乖離 | チェッカーも×1.0 に統一（最適化器を正） | 2026-07-13 |
| (b) range(low/high) 最適化器が表示HARD | 最適化器 soft化＋重み90/45 | 2.28.0 |
| (c) c3/c3m 単一シフト連: 窓 vs run-deficit（方向相違） | チェッカーも run-deficit化 | 2.31.0 |
| (c) c3/c3m 複数シフト連: +D vs +1, フラット vs 重み | #fire＋重み3/2/12 | 2.32.0 |
| c3n/c3mn | forbidden は両側窓マッチ#fire・c3mn×12 で既に一致 | (2.32.0で副次的) |
| (a) c1: canDoガード無/+d1/フラット | canDoガード＋#fire＋重み4 | 2.35.0 |
| apt(適切回数): 目的関数/チェッカー双方に未統合（事実上死に機能） | 3者にL1偏差×1で統合・双方向目標・違反表示 | 2.36.0 |
| fair(グループ内公平化): 後処理polishのみ＝目的関数外の「整え」 | 3者にL1偏差×1で統合（群×シフトの round(平均) 偏差） | 2.37.0 |
| c41s/c42s: 違反は研磨・検出済みだが内訳UIに列挙漏れ | breakdownLabels/BreakdownGroupに追加し表示 | 2.37.0 |
| weekly(7日周期(曜日)平準化): 後処理polish(分散)のみ＝目的関数外の「整え」 | 3者にL1偏差×1で統合（職員×曜日の round(勤務日/7) 偏差、`weeklyDevOfBucket`共通ソース） | 3.72.0 |

検証はすべて Python で「最適化器の族寄与 == チェッカー weightedScore 寄与」「soft<<1e6（hardゲート安全）」
「Δ==フル」を確認済み。

## ユーザー向け機能（実装済み）
- **FixSuggester**（`V6SwapSuggester.kt`）: 7種の手（単一変更/同日交換/複数変更/連鎖/再最適化窓/3人交換/別日交換）を
  deadline内で探索、(deltaHard, deltaTotal, deltaWeighted) でランク、kind+ops署名で重複排除。
  UI: `FixSuggestionCard`（kindチップ＋差分＋適用ボタン）、`BreakdownCard` から「直し方を探す」。
- **TallyCard**（`MagiScheduleViews.kt`、タブ1の勤務表表下）: 職員別（職員×シフト回数）と日別（シフト×日 人数）。
  **違反ハイライト**: 職員別=countViolations(vio-low赤/vio-high橙)、日別=needViolations(vio-covU赤/vio-covO橙)。
  ~~注: 読取モードで `gridUi.schedule=resultSchedule` に差し替わるため、編集後に読取へ切替えた場合のみ
  集計値と違反マップがズレ得る~~ **→ 3.96.0 で解消（backlog#1 完了）**: UiState に result専用マップ
  (`resultViolationCells/resultNeedViolations/resultCountViolations`, null=未計算→現行へフォールバック) を追加。
  `makeUi` が `schedule.contentDeepEquals(resultSchedule)` の検査時に report から一元充填（resultSchedule 更新サイトは
  全て makeUi(schedule==result, 対応report) を通ることを確認済）。`commitEditingToResult` は現行マップを引き継ぎ
  （refreshCheck 進行中でも完了時 makeUi が自己修復）。読取モードの gridUi は schedule と3マップを同時に差し替え。
  表示のみ・スコアリング不変。

## 業務ルール（厳守）
- **HF77**: パラメータ/重み/データ変更は**業務担当者の明示数値指示＋1件ずつ**のみ。コメントの主張と実装を必ず
  grep で照合。ただし「賢く統一/改善する」等の明示指示は、目的関数統一における重み変更の承認とみなす。
- ws8/ws9 新規シート, Cells()→CodeName 移行, staffタイプ自動判別, LimMax自動設定 = **すべて実装不要**（再提案しない）。
- VBA配布物(.bas)は SJIS(CP932)+CRLF のみ。Unicode は ASCII 代替、コメント矢印は `->`（→不可）。
- 被覆は同日のみ。MIN/OR は意図的設計。covU 構造的不足-2（供給153 vs 需要155）は確定事項。

## 検証ハーネス（Python）
`/tmp/cellfix.py`（サンドボックス内）が state を読み `sched, names, sym, S, T, K, canDo, locked` と
`violations(sc)`（covU/covO/low/high）を提供。`exec(open('/tmp/cellfix.py').read().split('base=violations')[0])`
で再利用。state JSON（`/mnt/user-data/uploads/magi_state_*.json`）: 10職員/31日/12シフト/2026-07。
シフト index: 0:休 1:Pｼ 2:Dﾃ 3:A4 4:Aｱ 5:Pﾅ 6:Cｵ 7:Cｱ 8:B4 9:有 10:Cｳ 11:B1。
cons1=[5日窓休≥1, 14日窓休≥4, 14日窓Dﾃ≥2]。桒澤美幸・大島愛はDﾃ不可。

## 回数設定UIの改善（進行中）
回数設定が複数画面に分散し紛らわしい（#1必要人数/#3グループ別c41(=1日の人数)/#5適切回数apt/#6個人別low-high/#7合計c2/#8連続窓c1）ため、
「⬇️1日の人数 / ➡️1人が月に何回」の軸でレイアウト統合する方針（ユーザー承認済み）。段階実装:
- 第1段(2.42.0, 完了): `StaffRangeCard`(個人別の回数)に**現状回数「今◯」＋過不足色**(🔴vio-low/aptLow ・ 🟠vio-high/aptHigh)を追加。
  説明文を「1か月に何回」に明確化。既存UiStateのみ使用(低リスク)。
- 第2段(2.43.0, 完了): 個人別カードに**適切回数(apt)の実効目標を併記**。ViewModel に `staffCountRules()`(=`CountRuleView`)を新設し、
  staffRange または apt が効く職員×シフトを統合一覧。チップ表記「Dﾃ 2–5 目標3 ・今3」、クランプ時「目標10→2」。
  apt のみ(個人別上下限なし)のセルも行に出し(× は出さない=群目標は群単位)、タップで個人別上下限を追加可能。`Problem(st).apt` を実効値の source。
- 第3段(予定・大): 軸ハブ画面で need/c41(1日)と apt/low-high/c2/c1(月) を分離。**`ConstraintsCard` が c41(1日)＋c1/c2(月) を束ねているため、
  カード分割を伴う大規模リファクタ**。c41 の「◯回」表記を「◯人(1日)」へ。要・別途着手。

## 回数設定画面（スマホ最適化・シフト中心, 2.60.0 Stage1-3）
回数設定(apt=理想 / staffRange=個人の最少最大 / cons41=群の最少最大)を1画面に統合。**シフト軸・個人軸の2タブ**
(`CountSettingsCard` = CountSettingsScreen.kt)。設計方針=「どのシフトを見るか」で業務するため**シフト中心**を主軸。
高密度リスト＋アコーディオン＋**インライン編集(画面遷移ゼロ)**＋固定シフトフィルタ(横スクロール)＋検索(シフト/群/職員名)。
- データ層(ViewModel): `shiftRuleBlocks()`(シフト→群行 最少|理想|最大・個人行 最少|最大) / `staffRuleBlocks()`(職員→シフト行 最少|最大)、
  `setCons41(群,シフト,l,u)`(更新-or-追加, 両空で削除)を新設。編集は ws1SetGroupApt/setStaffRange/setCons41 を直接呼ぶ
  (NumberStepper の +/- が即モデル反映=インライン)。削除はアコーディオン展開時のみ表示。組み合わせ禁止(cons42)は別画面のまま。
- 配置: 年次マスター編集スコープ先頭に CountSettingsCard。既存 Ws1Card/StaffRangeCard/ConstraintsCard は当面併存(安全)。
- 未: 新規行の追加(未設定の群/職員へ限界を新設)・個人タブのシフトフィルタ・"…"メニュー化は次段。
- (2.63.0, 検証で判明した単位混在を修正): **本画面は「回数(月)」軸に純化**。検証で群行の `最少｜理想｜最大` が
  cons41(=群の**1日の人数**, MirrorCore で `for j` 日次カウント `z<l||z>u`)と apt(=1人の**月回数**目標)で**単位混在**と判明。
  ユーザー選択により **cons41 を本画面から除外**(制約画面で編集)し、群=apt月目標のみ「目標 N回」、個人=staffRange「月 最少〜最大回」に。
  併せて ③`shiftRuleBlocks/staffRuleBlocks` を**空ブロック除外**(設定ゼロのシフト/職員の見出しノイズを排除)、④群に「目標は個人の月上下限でクランプ」注記。
  `GroupRule` から min/max/groupKigou を削除。**latent bug: `shiftRuleBlocks` の c41 キー区切りが空白でなく NUL(0x00) だった**のを関数書き換えで除去(内部一貫のため動作はしていた)。

## ドッグフーディング改善（オブジェクト単位, 2.61→）
コンポーネント単位の評価を反映。「見える」は達成済、残りは「直せる」への接続が主レバー。
- (2.61.0, 進捗の見える化): `progressSummary(ui)`(MagiScheduleViews) = 改善N%(initSoft→bestSoft) ・ 残りM:SS ・ X回
  (hard残あり時は ⚠N)。state既存値(initSoft/bestSoft/iters/elapsedMs/budgetSec)のみ・読取専用・スコアリング不変。
  上位バー実行中バッジに改善量併記＋ホーム組立中カード/LiveScheduleCard/操作ログ先頭で共有。
- (2.62.0, 直せる導線): **集計セル(TallyCard)の違反セルをタップ→原因を数字で提示**(職員別=現在/下限/上限/目標、
  日別=現在/必要/適正)＋「直し方を探す」で分析タブの FixSuggester へ遷移(onFix: staffはfocus、日別はnull=全体探索)。
  しきい値は ViewModel `staffCellLimits(i,k)`(rangeLo/rangeHi/apt)・`needCellLimits(k,j)`(need1/need2,use2)で Problem から取得。
  違反内訳カード(BreakdownCard)は既にチップ→場所→onFocusStaff→findFixSuggestions が機能済(2.62で追認)。
  注: TallyCard は gridUi(読取=resultSchedule)の count を使うため、現在値と違反マップのズレ既知事項は継続(backlog#1)。
- 未(ユーザー選択外): 軽い視覚調整(集計の左列固定/説明文ⓘ折りたたみ/やめる・前次ボタン縮小/ロゴ実行中アイコン化)。
- (2.64.0, 全21画面検証→単位・用語の明確化): 並列検証で全画面を engine 意味論と突合(ロジック誤り0・NUL/死にコード0)。実害は表記に集中し、表記のみ修正(ロジック不変):
  A) **cons41/cons41s を「回数」→「1日の人数」**(ConstraintsCard/SkillConstraintsCard の題・行「[l〜u]人/1日」・Addダイアログ題)。engine は `for j` 日次 `z<l||z>u`=1日人数。
  B) need を「必要数1/2」→**「最低人数」「上限人数(2パターン時)」**(Ws1Card 表示/ダイアログ・NeedDayCard 表示/ステッパー)。engine: need1=covU下限, need2=covO上限(use2時)。
  C) CoverageDiagnosisCard「不足N件」→**「不足N人」**(totalShortfall は人数)。
  非対応(検証で誤りでないと確認): SettingIssues の RANGE「…の回数/下限を…」DEMAND「…人に下げる」は既に単位適切=V6SanityPort 不変。
  UX摩擦(希望の日範囲選択・群×シフト表の横スク・満足度尺度・スキル解除・ワーカー用語隠し・集計左列固定)は構造変更を伴い backlog。
- (2.66.0, 群×シフト表の見やすさ): Ws1Card「担当できるシフト(群×シフト)」「適切回数」が横スクロール(12シフトで画面外)だった。
  各群を Row(horizontalScroll) → 群名を行頭＋**FlowRow でチップ折り返し**に。横スクロール不要・群名常時可視。未使用 import 除去。
- (2.68.0, 希望シフトの日付直接入力): 希望追加ダイアログの日が ±ステッパーのみ(1→30で多タップ・スタッフ▼/日± の操作系不統一)
  →直接入力テキストフィールド(1〜maxDay・数字キーボード)＋± に。dayText:String 化で空入力も安全。NeedDayEditor と操作系統一。
- (2.69.0, 計算方式の日本語ラベル): SettingsCard が `${ui.v6Algorithm}`/`alg.name`=生enum(AUTO/V5/ALNS/RSI/RSI_PLUS/PORTFOLIO)を
  表示していた。`v6AlgorithmLabel()` を新設し おまかせ/高速/破壊再構築/違反集中/違反集中＋/並列(複数案) へ。表示のみ・ロジック不変。
- (2.70.0, スキル割当の解除): SkillGroupEditor の職員スキル▼に **「(なし)」(skillIdx=-1)** を追加(どのスキル群にも所属させない)。
  engine は cons41s/cons42s で `ssk[i]==groupIdx(≥0)` ＝ -1 は常に偽=未所属で安全。永続化は素の int 往復(optInt/put・クランプ無)、
  群削除リインデックス(`k>g`)も -1 は不変で安全。既定 skillIdx=0 は変更せず追加のみ。表示は skillIdx<0 で「(なし)」。
  検証で判明し**既に対応済みだった backlog**: ①集計の左列固定=職員名カラムは horizontalScroll の外で既に固定 ②コパイロット文言=
  `topHardFamilyJp`→`hardFamilyJp` で既に日本語(生コード露出なし)。**非対応(意図的)**: 満足度尺度の式変更=表示専用だが業務判断要・保留 /
  EmptyState ボタン順=既存ユーザーは「データを開く」主操作が妥当 / Bottleneck/ようすの説明追加=「説明文は読まれない(④)」原則に反するため見送り。
- (3.75.0, E7 違反フィルタ = 種別トリアージ): 勤務表タブに**制約種別フィルタ**を新設（grilling で要件確定）。18族を作成者の語彙で
  **コース6分類**(人員 covU/covO・希望 pref・連勤 c3系・回数 low/high/apt/c2・群ルール groupViol/c41/c42/c41s/c42s・窓 c1)に束ね、
  **勤務表タブ全面**(グリッドセル/日ヘッダ不足N/Tally職員=回数/Tally日=人員/カレンダーセル)を**1つの共有フィルタ**で絞る。
  複数トグル・初期全ON(引き算)・**件数付きチップ**(breakdown 族合計)でそのまま「まずどれを潰すか」の種別トリアージに。
  実装: `MagiScheduleViews` に `VioBucket`/`vioBuckets`/`familyOfVioClass`(aptLow/aptHigh→apt)/`vioVisible`/`ViolationFilterBar`。
  状態は `MagiApp` 勤務表タブに `rememberSaveable` の Int bitmask(回転/復元で保持)。ScheduleGrid/StaffCalendarCard/TallyCard/
  MagiFlatGrid に `vioEnabled: Set<String>=allVioBucketKeys` を追加し各違反読取を `vioVisible` でゲート。**表示のみ・スコアリング
  不変**(違反自体は不変、表示するかだけ制御)。旧 ScheduleGrid の中途半端な per-family フィルタ(hiddenVio・grid未反映・grid限定)を
  置換。公平/曜日は場所無しでバケツ対象外。E7②(カレンダーへ need/count 追加)は per-staff 面と不整合のためスコープ外(業務選択)。
- (2.67.0, 早期脱出方針): 実機ログで停滞検知が予算上限とほぼ同時(301s)発火＝時間が節約できていなかった。停滞ウォッチドッグ
  (V6FinalPort)の許容を短縮: `minRunMs budget/5→/6(上限60→45s)`, `stallMs budget/4→/6(300s予算 75→50s)`, `stallHardMs budget/6→/8(50→37.5s)`。
  globalBest は生スコア管理＋最後の改善時刻でタイマリセット(フェーズ遷移でもリセット)のため**早期終了でも品質は不変**(無改善時に早く返すだけ)。
- (2.65.0, HF66 残予算ガード): 実機ログで後処理 HF66(職員内再配分)が applied=0 でも 12,999ms を消費し予算超過(301s>300s)→
  「後処理を短縮(残りパス打ち切り)」を誘発していた。原因=HF66 は「1手ごとに全候補をフル check」する高コストパスで
  `shouldStop` を手ごとにしか見ず、締切後も現在手のスキャンを走り切る。対策: ①`runPostOptimization(deadlineMs=hardDeadlineMs)`
  を新設し、HF66 専用上限 `hf66Cap = min(残予算/2, 6s)` を渡す(残り半分を後段研磨へ確保) ②HF66 内に `outOfTime()=shouldStop()||
  now>=deadlineMs` を導入し**内側スキャン(`scan@`ラベル)でも締切確認**して暴走を即中断。keep-best(isBetter採用)のため早期中断
  でも解は退化せず=スコアリング不変・安全。他パスの予算超過打ち切りを防ぐのが狙い。

## 後処理研磨の族カバレッジ（進行中）
後処理(V6HotfixPasses.runPostOptimization)は c1/c3系・low/high・apt・fair は研磨するが、c2・c41/c42・**c41s/c42s**・covO は
専用研磨が無く「評価のみで研磨されない」取りこぼしだった（weightedScoreには算入済）。
- (2.44.0): **c41s/c42s 専用研磨**を新設しフィックスポイント巡回に組込。c41(s) は markNeed で needViolations 側に出て
  群情報が失われ職員セルから拾えないため、アンカーは `Problem.cons41(s)/cons42(s)` から直接算出（違反群の職員を起点）。
  同日2者スワップ(被覆/HARD不変)・`isBetter`採否(keep-best=退化なし)。
- (2.45.0, 完了): 上記を `applyGroupRangePairPolish(skill: Boolean)` に**汎用化(DRY)**し、**c41/c42(通常群)も研磨**（skill=false=sgrp,
  skill=true=ssk）。フィックスポイント巡回に群範囲(c41/c42)＋スキル群(c41s/c42s)の2呼び出しを追加。SoftPolishVerify ログに
  c41/c42/c41s/c42s 増減と採用数(群:n / skill:n)を表示。サンプル最大残違反 c41=124 に直接効く（過拘束分は keep-best で無害）。
- (検証で判明・重要): 同日スワップで直せる単日族(c2/c41/c42/c41s/c42s)は**既存 CyclicSwap が isBetter で total を最小化する過程で既に研磨済**
  （isBetter=hard→total→weighted、total に全 soft 込み）。よって 2.44/2.45 の同日スワップ専用パスは CyclicSwap の部分集合＝
  ほぼ冗長（keep-best で無害・診断ログ充実の効果はあり）。c2 も同様で専用パス不要。covO は HF66(職員内再配分=被覆変更)が
  isBetter で既に扱う。**残る本当の取りこぼしは探索側の focus 漏れだった**。
- (2.46.0, 完了): 探索の `maxViolatedFamily` の order に **c41s/c42s を登録**（従来 c41/c42 のみで RSI が一度もスキル群を
  focus しなかった）。c41s は c41 と同型(1日人数)なので `rsiGenerateHypothesis` の destroyRepairDay 分岐へ、c42s は else
  (destroyRepairViolations)。後処理スワップでは届かない destroy-repair でスキル群を直接攻める。
- (3.74.0, D1/A1 完了): `maxViolatedFamily` を **純・件数最大 → 「解ける HARD 族を件数に関わらず SOFT より先に focus」** へ。
  旧実装は単一の c3n=1 が c1=118 等の高頻度 SOFT に埋もれ RSI が**一度も HARD を狙わない**失敗があった（停滞解析の根本原因）。
  first-pass=order 上の HARD 族(groupViol/covU/pref/c3n)で avoid 外かつ件数>0 の先頭を返す／無ければ従来の非avoid件数最大。
  **hard=0 のとき no-op＝全 soft の一般ケースは不変**。avoid(HF63=構造的に充足困難)の HARD は除外し無駄打ちを避ける
  (解けない HARD は 3.69 hardFloor と同方針で SOFT 研磨へ回す)。目的関数 better() の辞書式(hard<<total<<weighted)に focus を
  整合させる**スコアリング不変**の変更（Evaluator/Delta/Checker/重み 不変）。※探索動学の A/B 原則(nsp_bench)に対しては、
  bench が focus 選択を模擬できない（c3n/c1 制約非実装）ため**実測でなく原理**で採否：hard=0 で不変・HF63 で無駄打ち回避・
  1関数で可逆、という限定的・低リスク設計に依拠。単一呼出(V6NativeOptimizer:607)・テスト非依存。
- (3.94.0, ソフト研磨の網羅的計測 → 候補生成の重み整合): 「ソフト研磨を50%以上に改良」の指示に対し、`tools/nsp_bench.py`
  で **6レバーを網羅 A/B**(golden_state 実データ 24 seeds ＋ 合成 over-constrained)。結論=**現行のソフト研磨は既に構造的下限**:
  - **現行(smart repair day+staff+viol, 2.57-2.59)が既に repair-day 基準比 −54.5%**(golden 24seed 頑健)＝**50%目標は現行実装で達成済**。
  - golden の残差 12.6 は **low/high(重90/45)=0 まで消え、apt(8.8)+covO(3.8, 重み~1)のみ＝構造的**(供給<需要・群apt目標の競合)。
  - 試した in-loop 追加レバーは**すべて不整合か有害**: multiday=golden 微改善だが over-constrained +28%悪化 / worst-removal(日/職員)=
    +162〜375%悪化(過集中で floor を撹拌) / covo-aware=完全中立(不発) / dr比率up(0.35/0.50)=大幅悪化 / in-loop swap=golden +96〜356%悪化
    (貪欲交換が SA を局所最適に固着)。→ **被覆保存スワップは in-loop でなく後処理**が正しい(既存 CyclicSwap が担当済)。
    2.55/2.56/2.57 の「脱出/近傍ヒューリスティクスは中立or有害」教訓を再確認。
  - **採用した唯一の安全・原理的改善**: `applyDayAssignmentPolish` の Hungarian 候補生成 proxy `rangePen` を **3/3 → 90/45**
    (apt=1 は不変)へ整合し目的関数(Evaluator/staffCountPenaltyAt)と一致。keep-best(isBetter@UnifiedViolationChecker)採否のため
    **退化不能=スコアリング不変**。bench では**中立**(単純化 bench の SA が low/high を研磨前に0化するため proxy 差が顕在化しない=
    bench の限界)だが、実機は c1/c3/pref 等の競合族が多く low/high 残差が研磨段へ到達し得る→ 真の目的への proxy 整合が効く見込み。
    HF77 非該当(スコア重みでなく探索内部の候補生成 proxy)。実測ハーネス: `/tmp/.../soft_polish_*.py`, `dayassign_weights.py`(scratchpad)。

## 事前診断（実行前の過拘束検知, 2.47.0 完了）
過拘束データに最適化を走らせても解は無い（出口の無い迷路）→ 数分の無駄。実行前に「何をしても無理」を検知して提示する。
`V6SanityPort.buildGuidance` に**検査6（シフト単位の構造的過拘束）**を追加（既存の SettingIssue channel に載るので新規UI不要・
read-only・ダッシュボードの SettingIssuesCard に常時=実行前に表示）。誤検知回避のため明確に矛盾する2ケースのみ:
- A) 下限の合計 > 必要数(上限)の合計 → 全員の下限を同時に満たせず過剰配置/下限割れが不可避。
- B) **全担当者に上限**があり 上限の合計 < 必要数 → 席を埋めきれず人員不足(covU)。`allCapped` ガードは CLAUDE.md の
  「未設定者は無制限なので誤判定しない」設計と一致（Dﾃ=上限計10だが現状33＝未設定者ありでは発火しない）。
発展: ②**ボトルネック可視化の集約(2.53.0 完了)** = `BottleneckCard`(MagiDashboardCards)。countViolations を職員別、
needViolations を日別に件数集計し多い順 top5 を俯瞰表示(read-only・詳細タブ、BreakdownCard 直後)。セル着色(TallyCard)を補完し
「どこにしわ寄せが集中するか」を一目で提示。③hard→soft の What-if 提案(checkResultWorse で部分的に既存)は未着手。
- (3.98.0, 幻のapt目標検知 = 検査6b): 実機ドッグフーディング(桒澤美幸 B4 目標1・今20)で判明した「担当レパートリーから
  強制される最低回数 > apt目標」をユーザー指示で事前診断に追加。全日はいずれかの担当可シフトで埋まるため
  `count(k) >= T − Σ_{k'≠k,担当可} min(上限(k'),T)` の下界が成立。この強制下限が apt 目標を超えるなら aptHigh は
  何をしても残る＝データ修正が正道と提示（例: 担当={休,B4,有}・休10-10・有1-1・31日 → B4最低20回で目標1は達成不能）。
  他シフトに上限未設定が1つでもあれば下界0以下＝発火しない(保守的・誤検知ゼロ)。golden_state 検証で美幸B4のみ発火
  (他22 aptセル過検知ゼロ)。read-only・SettingIssue channel・スコアリング不変。
- (3.76.0, 壁/ダイヤル分類器 = soft floor signal): ドッグフーディングで判明した「アプリは**解ける soft と構造的 soft 下限を区別しない**
  ＝一番大きい数字が一番追っても無駄になり得る」問題への対処。`buildGuidance` に **検査(2b-2): c1 窓制約の構造的不能(壁)検知**を追加。
  各 cons1 について **供給 vs 需要下界**を比較し、供給<需要なら「窓ルール『X≥n/d日』は構造的に残る＝担当追加かルール緩和」を SettingIssue で提示。
  供給≥需要(=ダイヤル:優先度で減らせる)は正常＝出さない。**conservative**(需要=disjoint窓の下界 / 供給=休窓:S*T−Σ最小work需要・
  作業窓:Σ上限被覆＝供給高め見積り)で **false wall を出さない**向きに丸める＝発火＝真に構造的不能。read-only・スコアリング不変・HF77セーフ。
  実データ検証(golden_state): **Dﾃ≥2/14日=壁**(夜勤スロット31<必要32＝8人×2×2窓)／**休≥5/14日=ダイヤル**(供給155≥需要100)。
  ※重要な副産物: 実装前の接地で「c1=113 は構造的」という当初のドッグフーディング仮説が**反証**された(休供給は+55余剰＝c1 は大半が優先度
  トレードで減らせる)。grilling の「作る前に調べる」が誤前提を捕捉。将来は c3/range への拡張・「窓N件のうち壁M件」の post-run 明示が候補。
- (3.99.0, グリッド判読性=違反3段階＋休後退): 実機スクショ起因(「人間に容易に判読できるようにする」)。全ソフト違反が
  同じ太い橙破線枠で 194件により格子が警告に飽和し必須1件が埋没→ ①**違反3段階表示**(重み階層と表示強度を一致):
  必須=実線 / 重いソフト(low90/high45/c3mn12)=破線 / 軽いソフト(重み≤4)=**右上の小さな角マーク**に降格
  (`isHeavySoftCellViolation` 新設・vioKind 0..3・日ヘッダ下線は vk>=2 で意味不変)。②**休セル後退**: 記号「休」を
  淡色(α0.30)＋細字＋onSurfaceVariant(改名データは-1=従来表示)。③凡例3項目化＋説明文更新。表示のみ・スコア不変。
  対象は MagiFlatGrid(メイングリッド)。StaffCalendarCard の2段階表示は現状維持(密度が低く飽和しない)。
> **決定記録（D4, 対象端末）**: **幅360dp帯の端末は対象外**（業務判断・2026-07-10）。対象は幅~390dp以上
> （コンテンツ幅388dp以上＝7日表示が36dp床に当たらず成立する帯）。悲観検証の「360dpでは7日表示が6日止まり」
> は非問題として扱い、以後360dp向けの縮小最適化（名前列56dp化等）は**再提案しない**。cellW の36dp床は防御的に残置。

> **決定記録（D5, 年度末モード）**: 時期モード設計（年度始め/月末/年度末, 2026-07-10 提案）のうち
> **年度末モード＝年間積算5項目（①年間の偏り ②希望反映率 ③毎月の手修正検出 ④慢性不足シフト ⑤マスター見直し
> チェックリスト）は実装不要**（業務判断）。前提となる月次スナップショットのアーカイブ基盤も不要。**再提案しない**。
> 残る設計スコープ = 月末モード（作成フロー4ステップ再配列）と年度始めモード（実働人数チェック・欠勤耐性）のみ。

> **決定記録（D6, 標準値vs月別例外）**: 入力アーキテクチャ設計（4か所分割, 2026-07-10）のうち
> **「標準値 vs 月別例外」の差分表示（年間標準のスナップショット保存＋逸脱一覧UI）は実装不要**（業務判断）。
> 現行の「月＝スナップショット」モデルのまま、その月の値を直接編集する運用とする。**再提案しない**。
> 月次チェックリストを作る場合、例外件数は明示的な例外リストが既にある **日別必要人数の例外（needDay）のみ**を数える。

- (3.100.0, 7日間表示): ユーザー明示要件「7日間見えるようにする」。cellW 48dp固定では多くの端末で6日強しか見えず
  週の模様が切れていた→ ScheduleGrid が BoxWithConstraints で **`cellW=((利用可能幅−32−80)÷7).coerceIn(36,48)dp`** を
  動的計算し MagiFlatGrid へ注入（週ページングの cellWpx も同値＝ジャンプ整合）。下限36dp=記号可読性の床（極端に
  狭い端末のみ7日未満に妥協）・上限48dp=広い端末はより多くの日が見える。セル高は48dp維持（片手一本指のタッチ面）。
## ネイティブ加速 第3期: 完全版C++移行（進行中・明示指示 2026-07-13「完全版C++移行できるようにする」）
> 方針は第1/2期と同一: **Kotlin チェッカー/評価器を「正」として温存**し、実行時間を占める残りの Kotlin ホットパスを
> C++ チャンク＋2層番兵（①チャンク自己整合 ②Kotlin fullEval Long== 照合、発火で NativeGate 退化）で置換する。
> 残余ヒートマップ（実機300sログ基準）: ①hf80PostPolish 45s×5並列ワーカー（最大の残り）→ **Stage10 で完了** /
> ②SaOptimizer PhaseB(LAHC, softPolish時のみ) / ③V6LateOperators(Chain3/4/Rect/BlkN, ラウンド境界) /
> ④後処理チェーン研磨(CyclicSwap/C1/C3系, 実測~1s) / ⑤RSI制御層・チェッカー=**軽量O(ラウンド)＋「正」のため対象外**。
> - **Stage10 完了(3.151.0)**: C++ に `PolishState`＋`runPolishChunk`（hf80PostPolish と同一の 11-way オペ構成
>   =単一セル/行内2日swap/同日2者swap/targetedFix×6/copy系DR(violations50%・day50%)＋hard時hf67、同一受理
>   =best-hardゲート＋`polishAcceptN`(acceptWorseScore temp0.15 と同式 exp(-Δ/30))、keep-best、hint=best盤面の
>   violations セル）。JNI 4関数（nativePolishCreate/Chunk/Read/Destroy）・ABI_VERSION=5。Kotlin `hf80PostPolish` は
>   `runPolishChunksNative`（200反復/チャンク・チャンク間で締切/E10停滞/キャンセル確認・best改善チャンクを
>   Kotlin fullEval Long== 照合）を先に試し、完走なら早期return（ログ「PostPolish …（ネイティブ）」）・番兵発火時は
>   照合済みbestを引き継いで従来 Kotlin ループが残り時間を続行（退化不能）。ホスト検証 TEST9: 6シード×25チャンク
>   =status0・自己整合・keep-best単調・hardゲート(63→0)・希望ロック不変。恩恵経路=RSI++ Phase4(45s×5ワーカー)・
>   optimize epilogue・仕上げ(polishOnly)。
> - **Stage11 完了(3.152.0)**: ①**[重要発見] `UiState.softPolish=true`（仕上げ最適化トグル）が既定ON のため、
>   `SaOptimizer:81` の `!params.softPolish` 条件で**既定設定では SA ネイティブ(Stage3)が丸ごと無効**だった
>   （実機ログの「ネイティブ探索=有効」表示でも V5 シード60s と RSI 奇数ラウンドの SA は全部 Kotlin。加速して
>   いたのは ALNS チャンクのみ＝実測+20%はALNS だけの寄与）。②C++ に `LahcState`＋`runLahcChunk`
>   （PhaseB=HARDガード付きLAHC の忠実移植: オペ=PhaseAと同一4種60/20/12/8・受理 candHard<=bestHard &&
>   (cand<=hist[bIt%L] || cand<=cur)・hist更新 cur<hist→hist=cur・keep-best。hist/bIt/bestHard はチャンク跨ぎ保持）。
>   JNI 4関数（nativeLahcCreate/Chunk/Read/Destroy）・ABI_VERSION=6。③SaOptimizer: softPolish 条件を撤去して
>   ネイティブ有効化＋`runWorkerNative` にラダー境界の hardStallMs 判定→`runLahcNative`（4000反復/チャンク・
>   2層番兵・発火時はワーカーごと Kotlin runWorker へ退化）で PhaseA→PhaseB の一方向遷移を移植。
>   ホスト検証 TEST10: 6シード×25チャンク=status0・自己整合・keep-best単調・HARDガード(hard 1〜4→0 単調)。
>   恩恵経路=**既定設定の全 SA フェーズ**（V5シード・高速・RSI 奇数ラウンド・RSI++ Phase1）が初めてネイティブ化。
> - **第3期 完了(3.153.0)**: Stage12(V6LateOperators)/Stage13(後処理チェーン)は**精読・定量の結果、移植しないと確定**。
>   ①コスト実測: LateOperators=ラウンド境界 O(2〜8回)×候補52手×checker(サブms)≈**~100ms/実行**、後処理チェーン=
>   実機 POST 総985〜1328ms＝**合わせて300s予算の~0.5%**。②両者の採否ゲート(gate/gateW/isBetter)は checker の
>   weightedScore/breakdown に直接依存＝C++化にはチェッカー移植が必要で「Kotlinが正」の合意に反する（Stage9/3.139.0 の
>   除外理由と同一）。→ 0.5%の利得のために安全アーキテクチャを壊さない。**実行時間ベースの C++ 移行はこれで完了**
>   （全ホットパス=評価器/SA PhaseA/LAHC PhaseB/ALNS/修復群/hf67/HF80研磨がネイティブ・残りは意図的に Kotlin の軽量制御層）。
>   併せて **NativeBridge 診断行の表示バグ修正**: 有効/無効を `NativeGate.enabled`(番兵のみ)→`usable`(番兵×トグル×ロード)
>   判定へ（旧: 設定トグルOFFの実行でも「ネイティブ探索=有効」と表示され A/B ログの判読を妨げていた。eb7919aa ログで実害確認）。
>   ラベルも実態に同期「有効(SA＋LAHC＋ALNS＋研磨チャンク)」/「無効(設定トグルOFF)」。
> - **(3.154.0, 全体計算の最小化=ユーザー指示「全体計算は必要最低限にする」)**: 番兵の全体計算（チャンク自己整合＋
>   改善チャンクの Kotlin fullEval 照合）は正しさの根幹のため不変とし、**表示/無駄の全体計算だけ**を削減:
>   ①PhaseA/LAHC ワーカーの**非改善チャンク flush の毎回 unflatten を撤去**（flush は localBest<globalBest でしか盤面を
>   読まず非改善では勝てない＝純粋な無駄。直近の照合済み best をキャッシュして渡す）②ALNS runRestartNative の
>   **checker.check＋liveBest 全面コピーを 250ms 周期に間引き**（表示専用。番兵 fullEval は改善毎に維持・restart終端／
>   番兵発火時は syncReport() で最終同期＝ログ精度不変）③**PolishChunk のチャンク頭 collectViolationCells を撤去**
>   （hint は best 基準で生成時＋改善時に更新済み＝毎チャンクの全面スキャンは冗長）④Polish チャンク 200→400反復
>   （チャンク末尾の自己整合フル評価の頻度半減・締切/停滞/キャンセル粒度は ms 級のまま）。全て挙動同一クラスの
>   純減量（hint 鮮度と番兵頻度の意味論は不変）・ホスト TEST1-10 全パスで回帰なし。

## ネイティブ加速 第2期: ALNS/RSI本体のC++化（進行中・明示合意）
> ユーザー指示「ALNS/RSI本体のC++化する」（2026-07-11）。3.139.0 の範囲確定（対象外）を**明示指示で解除**。
> 前提: Kotlin チェッカー/修復系が「正」の原則は維持し、C++ は同値の高速版＋2層番兵（チャンク自己整合
> ＋Kotlin照合、発火で NativeGate 退化）を第1期と同様に必須とする。段階計画:
> - **Stage5 完了(3.140.0)**: C++ に `collectViolationCells`（violations マップの8族=c1窓ラン先頭/
>   c42(s)ペア両セル/c3×4(run先頭・窓先頭・forbidden全セル)/pref/groupViol、count/need系はループ内未使用で対象外）
>   ＋ `GlsPenaltyN`（密配列・augment/moveAug/penalizeWorst/decay80%・lambda200）＋ `glsAcceptN`（SA/GD/Lam 3モード、
>   hard+2超は常に却下）。ホスト検証: セル抽出=重複なし・pref/groupViol完全包含、GLS=augment/moveAug 2000手一致・
>   decay算術一致。まだ未配線（Stage8 で ALNS チャンクが使用）。
> - ~~Stage5~~（完了・上記）: C++ 違反セル抽出（UnifiedViolationChecker の violations マップ相当＝GLSキック/
>   destroyRepairViolations の hint 用。MirrorCore の mark 系を移植・cells のみで breakdown/weighted は不要）
>   ＋ GlsPenalty 移植（penalty行列・augment/moveAug/penalizeWorst(util)/decay/lambda）。
> - **Stage6 完了(3.141.0)**: soft-aware 修復3種（destroyRepairDayAtN=非希望→休destroy+need1不足を
>   marginal soft(個人90/45/apt＋群c41DayMarg)最小の休職員でrepair / StaffAtN=行destroy+被覆穴のみ埋め /
>   ViolationsN=hint最大8セルをmarginal最小へ再割当・空hintはrandomAllowedCell）＋ find*Fix 8種
>   （covO/c2/rangeLow/c41/rangeHigh/c41s/c3Want/apt、c41系は群/スキル群を共通関数でパラメタ化）＋
>   findTargetedFixN（一様シャッフル順）。SaChunk の ssn/dsn が countForStaff/countOnDay に対応。
>   ホスト検証: 修復3種×200試行=wishLocked不変・担当可のみ・変更範囲閉じ込め、finder 500/500発見・全手妥当。
> - ~~Stage6~~（完了・上記）: soft-aware repairs 移植 = destroyRepairDayAt/StaffAt/Violations＋staffCountPenaltyAt＋
>   c41DayMarg（V6NativeOptimizer 1006-1200行）＋ findTargetedFix（8種, V6HotfixPasses）。
> - **Stage7 完了(3.142.0)**: hf67HardRepairN 移植（hf66=範囲外/担当外→先頭担当可 → 実現可能希望の適用 →
>   被覆不足3周充填(bestStaffForCoverage=上限超過500+回数×3+引き抜き不足コスト50・counts周内据え置き=Kotlin同) →
>   range下限充填(乱数タイブレーク)）。in-place 変異・changed数を返す。ホスト検証: 範囲外混入盤面100試行で
>   全セル担当可＋実現可能希望の完全充足、hard 63→48 の修復実効を確認。
> - ~~Stage7~~（完了・上記）: hf67HardRepair 移植（copy系オペの7反復毎・hard>0時のみ呼ばれる修復）。
> - **Stage8 本体完成(3.143.0・未配線)**: C++ に `runAlnsChunk`＋`AlnsState`（GLS・適応重み・Lam温度・best・
>   停滞カウンタをチャンク跨ぎで保持）を実装。runAlns 404-597 の内側ループを 1チャンク=N反復で完走:
>   7オペ（op0-2=copy系destroyRepair＋差分適用/op3-6=直接評価swap/randomCell/targetedFix）・
>   受理3モード(SA/GD/Lam)・opSelect(roulette/thompson)・softFocus(0.30/0.15)・hf67(7反復毎hard>0)・
>   GLSキック(停滞200超・50反復毎・256キック毎decay)・適応重み(64反復毎・反応0.2・下限0.05)。SaChunk に
>   resetBoard(restart境界のcur差替)追加。JNI: nativeAlnsCreate/Chunk/Read/SetCur/Destroy＋ABI_VERSION=4。
>   番兵1層目=チャンク末尾の自己整合(status!=0で退化)。ホスト検証: 3受理×2選択×20チャンクで status=0・
>   cur/best自己整合・keep-best単調（合成問題で 63M→800台の改善実効も確認）。**Kotlin runAlns への配線は
>   Stage8b（次段）**: SaOptimizer.runWorkerNative と同型の退化フォールバック＋2層目のKotlin照合を付す。
> - **Stage8b 完了(3.144.0)**: Kotlin runAlns へ配線。restart 本体の内側 while を `if (!usedNative)` で
>   囲み、ネイティブ可能時は `runRestartNative`（ローカル suspend fun）が 1チャンク=200反復で C++ ALNS を
>   駆動。Kotlin 保持: restart 境界の perturb+hf67・進捗onProgress/liveBest・キャンセル(チャンク間ensureActive)・
>   予算deadline・**2層目番兵**（best 更新チャンクを Kotlin Evaluator.fullEval で Long== 照合、不一致で
>   NativeGate 退化）。problem ハンドルは restart 跨ぎ共有・try/finally で destroy。番兵発火時は false 返しで
>   その restart 以降 Kotlin ループへ。SaOptimizer.runWorkerNative と同型。診断ログ「ネイティブ探索=有効(SA＋
>   ALNSチャンク)」。これで 60s 主経路(RSI→ALNS)の ALNS フェーズが加速。GLS penalty は restart 毎再構築
>   （生スコア最良は別管理＝退化なし）。Kotlin コンパイル検証は CI（assembleDebug）。実機で番兵不発を要確認。
>   **[3.161.0で訂正]** 上記「GLS penalty は restart 毎再構築」は誤り。実装は`runAlns`呼出につき1個の
>   `GlsPenalty`をrestartループの外側で生成し、全restart間で共有（decayのみ希薄化）。再構築されるのは
>   `runAlns`が新規に呼ばれた時（RSI各ラウンド/並列ワーカー等）のみ。globalBestは生スコアで別管理のため
>   受理動学にのみ作用し正しさは不変（keep-best）＝実害なしのHF77（コメント≠実装）訂正。
> - ~~Stage8/8b~~（完了・上記）: ALNSチャンク統合（チャンク=200反復: curReport更新周期に一致。7オペ・適応重み(roulette/
>   Thompson)・受理3モード(SA/GreatDeluge/Lam)・softFocus・wishLocked・GLSキック(50反復毎)を C++ 内で。
>   Kotlin保持: restart境界(perturb+hf67入口)・進捗/liveBest・キャンセル・番兵）。
> - **Stage9 完了(追加移植なし・実測は実機)**: RSI/RSI++ のラウンド内探索本体は `runAlns`(Stage8b) または
>   `runV5`→`SaOptimizer.run`(Stage3) を呼ぶため、**Stage3+8b の配線で既に全探索フェーズが加速済み**（追加の
>   チャンク化コード不要を確認: runRsi:700 が phase=runAlns/runV5、runRsiPlus も seed=runV5＋runRsi/runAlns）。
>   RSI 固有の制御層（focus選択/HF63/rsiGenerateHypothesis/EarlyChain=V6LateOperators/better判定）はラウンド境界で
>   O(2〜8回)しか走らない軽量な Kotlin「正」の層＋チェッカー breakdown 依存のため C++化は対象外（3.139.0 と同方針）。
>   実測は実機ログの TIME行 反復数比較＋NativeBridge行の番兵不発確認で行う（サンドボックスは Android コンパイル不可）。
>   **これで第2期(ALNS/RSI本体のC++化)の移植は完了**。加速経路=V5/高速/RSI/ALNS/RSI++ の全探索フェーズ。
> 各Stageでホスト検証（scratchpad/native_test.cpp 拡張）→CI→実機ログ確認の順。

## ネイティブ加速（C++/NDK, 進行中）
> ユーザー指示「アンドロイドのネイティブ開発言語にして、実行速度改善する」（2026-07-11）。backlog#3 の
> 「C++/NDK 移植は不要」結論を明示指示で解除。**目的=両方（待ち時間短縮＋同時間の品質向上）／範囲=ホットパス
> （Δ評価＋SA内側ループ）のみ**で合意（AskUserQuestion）。方針: **Kotlin 実装を常に正として残し**、C++ は
> 高速版。返却盤面は Kotlin 側フル再評価で照合し不一致なら破棄（退化不能の番兵）。.so ロード失敗時は
> `NativeBridge.available=false` → 全経路 Kotlin フォールバック（JVMユニットテストも従来どおり）。
- (3.136.0, Stage1=足場): NDK/CMake ビルド配線（`app/src/main/cpp/`・ndkVersion 26.1.10909125・
  arm64-v8a のみ・CMake 3.22.1）＋ JNI 疎通（`NativeBridge.nativeAbiVersion` の ABI 照合）＋
  handleOptimize の診断ログに読込可否を1行表示。CI 両ワークフローの sdkmanager に ndk/cmake を追加
  （v6-engine-check は assembleDebug で NDK ビルドも検証）。エンジン動作は完全不変。
- (3.139.0, Stage4=配線完成＋範囲確定): ①**設定トグル「ネイティブ加速（C++）」**を最適化設定に新設
  （UiState.nativeAccel 既定ON・NativeGate.userEnabled と連動・実行中は変更不可）。番兵ゲートとは独立の
  ユーザー意思で、OFF=常に従来Kotlin。②**範囲決定: ALNS/RSI 本体のチャンク化は対象外**（=このプロジェクトの
  ネイティブ化はこれで完了）。理由: runAlns の反復ループは 7反復ごとの hf67HardRepair・200反復ごとの
  UnifiedViolationChecker 再検査・GLSキック(違反セルhint)が Kotlin の「正」実装と分かちがたく、これらの
  C++化は「ホットパス限定・Kotlinが正」の合意に反するエンジン全体移植になる。加速済み経路=V5(≤30s)・
  高速計算・RSI++(≥211s) Phase1種。60s主経路(RSI→ALNS)は Kotlin のまま（差分評価・零アロケ済みで
  1,800万反復/52s の実測性能）。再チャレンジする場合はチェッカー/hf67 の C++ 移植込みの新規合意が必要。
- (3.138.0, Stage3=SAチャンク): SaOptimizer PhaseA の**冷却ラダー1本を1チャンク**として C++ で完走
  （runSaChunk: Kotlin と同じ4オペレータ(single/swapDays/blockFill/LNS)＋Metropolis＋undoバッファ。乱数は
  mt19937_64=経路一致は狙わずスコアと盤面でパリティ）。スコアは**影響スライスの before/after 再計算**による
  差分方式（行族=c1/c2/c3系/pref/range/apt/weekly・日族=c41系/c42系/cov・群族=fair、ssn/dsn/wd を増分維持）。
  **番兵2層**: ①チャンク末尾に C++ 内で fullEval と照合(status!=0=Kotlin側が破棄) ②best 更新チャンクは
  Kotlin Evaluator.fullEval で Long== 照合。どちらか発火で NativeGate が閉じ**そのプロセスは Kotlin へ退化**
  （クラッシュさせない）。Kotlin が保持: 予算/キャンセル(チャンク間)・進捗flush・MagiConductor 境界
  （updateStagnationBulk 新設で停滞を一括反映）・strongPerturb。softPolish(PhaseB=LAHC) 有効時は従来 Kotlin。
  対象経路=SaOptimizer 利用箇所（V5≤30s・高速計算・RSI++ Phase1種）。ALNS 本体は Stage4+。
  **ホスト検証済み**（サンドボックスの clang++ ＋ JNI スタブ）: ランダム2万手で差分==フル一致・12シード×
  ラダーで status=0/スコア照合/keep-best 保持（scratchpad/native_test.cpp）。ABI_VERSION=3。
- (3.137.0, Stage2=C++フル評価器＋実行時パリティ): Evaluator.fullEvalParts を C++ へ忠実移植
  （magi_native.cpp。c1 canDoガード・c3 run-deficit/窓#fire・pref実現可能のみ・range 90/45・apt/fair/weekly
  L1偏差・covU/covO per-cell OR/AND、Math.round は floor(x+0.5) で同一化）。Problem は NativeEval.flatten が
  平坦配列（meta/staff/canDo/wish/needs/ranges/cons/c3/bucket）で1回だけ JNI へ渡す（members は sgrp から
  C++側導出）。**実行時パリティ**: handleOptimize 完了時に採用盤面で C++ vs Kotlin の hard/soft を照合し
  診断ログ1行（一致=µs比較付き / 不一致=W警告＋ネイティブ経路不使用）。ABI_VERSION=2。read-only＝採用結果に
  影響なし・スコアリング不変。JVMテストは available=false で全経路 Kotlin のまま。
- 予定: ~~Stage2~~（完了・上記）→ Stage2旧記述: C++フル評価器（平坦化 Problem を JNI へ1回渡し・Kotlin Evaluator と実行時照合）→
  Stage3=SAチャンク（Δ評価＋受理を C++ で回し返却盤面を Kotlin 再評価）→ Stage4=V6NativeOptimizer 配線
  （設定でON/OFF・フォールバック維持）。

- (3.135.0, 制約の項目名称を下流→上流で統一): 指示「各制約などの項目名称を下流から上流に向かって用語統一する」。
  **下流=違反チップ(breakdownLabels)の語彙を正**とし、上流（編集画面の節タイトル・ダイアログ題・診断ログ）を一致させる
  （違反を見て設定を直しに来たとき同じ名前で見つかるように。単位・補足は括弧で添える）:
  c1「期間の決まり」→**「窓の要件（○日間に△回以上）」**・c2「個人の合計回数」→「個人の合計（回数）」・
  c3m「並び希望」→**「推奨の並び」**・c3mn「並び回避」→**「回避の並び」**・c41(s)「グループ/スキル別の1日の人数」→
  **「群/スキル群のレンジ（1日の人数の下限〜上限）」**・c42(s)「…組み合わせ禁止」→**「群/スキル群ペア禁止（同じ日に不可）」**。
  診断 c3FamilyJp の英字混じり「必須MUST/禁止FORBIDDEN/希望Want/回避Hate」→ 並び4族の日本語名へ（operator_ux の
  「英字記号を画面に出さない」に整合）。groupViol は下流内の分裂（グループ不整合 vs 担当できないシフト）を
  **「担当外シフト」**へ統一。ColorSettingsView のチップ/ピッカー題も生キー(c3n等)→ breakdownLabels の日本語ラベルに。
  AttentionCards の「群レンジ」→「群のレンジ」。セクション注記（C41/C42 の生コード含む）も同語彙へ。文字列のみ・スコア不変。
- (3.134.0, 実機バグ修正=必須違反の枠が白リングだけに見える): 実機報告「違反の枠の色がおかしい」。原因=
  **Modifier.border はチェーンの先が最後=最前面に描かれる**（内側 drawContent 後に自枠を描く）のに、
  violationBorder(hard) がハロー5dp→違反色3dp の順で連結し、**ハローが違反色を完全に覆って白リングだけが見えていた**
  （3.118.0 のコメント「後掛けが上に描かれる」が逆。暗テーマでは暗ハローで目立たず、UD白地固定=3.121.0 で露呈。
  凡例はハロー無し単純枠のため赤く表示され、グリッドと食い違っていた）。違反色を先（最前面）・ハローを後（背面）に
  修正: 外側3dp=違反色/内側2dp=ハロー。破線(vk2)と角マーク(vk3)は同一 draw ブロック内で正順のため影響なし。
  FlatCell/CalendarCell 双方が同ヘルパー経由で同時修正。表示のみ・スコアリング不変。
- (3.133.0, 用語統一=全画面の表記ゆれ解消): 指示「用語統一する」。UI文字列を全数調査し5クラスタを統一:
  ①**「スタッフ」→「職員」**(34件。3.114.0 のドア名「職員管理」・集計「職員別」に整合。**CSV往復キーワードは除外**=
  ScheduleCsvBridge の「スタッフ \\ 日付」ヘッダと looksLikeScheduleCsv の startsWith 判定は同期のため不変。
  「ユニット」は外部フォーマット(ユニット列形式)の語彙のため不変) ②「盤面」→「勤務表」(診断ログ) ③「守れていない約束」
  「必須の条件」→**「必須違反」**(凡例と統一) ④c41説明の「最低／最高」・一括設定の「最低=最高」→**「下限／上限」**
  (ConstraintDialog の入力語と統一。need の「最低人数」は 3.127.0-B の決定どおり維持) ⑤「作る」→**「つくる」**
  (主導線「勤務表をつくる」に統一)。文字列リテラルのみ・スコアリング不変。V6SanityPortTest の assert("担当不可")は部分一致で不変。
- (3.132.0, 違反色の入口一本化=IA重複解消): 違反色の設定が **ShiftColorCard 内の必須色のみの部分入口** と
  **詳細設定（折りたたみ）内の ColorSettingsView（基準色2種＋族別の完全版）** の2か所に分裂し、後者は見つけにくかった。
  ShiftColorCard の「違反の色（必須違反）」節を撤去し、**ColorSettingsView を設定タブのシフトの表示色直後へ移動**
  （詳細設定はログのみに）。編集APIは不変（__vio__ は ColorSettingsView の「必須の基準色」チップから従来どおり変更可）。
  文言・コメントの「詳細設定→違反種別の色」参照も更新。表示・導線のみ＝スコア不変。
  (3.132.1, /code-review 後始末): 未マージ17コミットをインラインレビュー（サブエージェントはAPI上限で不可）。
  実バグ0。残滓2件を修正: 詳細設定の説明文「ログ・違反色トークン」→「ログの確認と出力」（移動後の実内容と整合）、
  AdvancedSettingsSection の未使用 vm パラメータ除去。制約編集の値順ラウンドトリップ（C42 の g/s 写像含む）・
  希望編集の移動意味論・loadAsync(markResult)→makeUi resultFresh 経路は検証で健全を確認。
- (3.131.0, 希望シフトの日入力カレンダー化=バックログ「希望の日範囲選択」解消): WishDialog の日入力（±ステッパー＋
  テキスト、3.112 で「指示範囲外」と据え置いた最後の旧式）を **NeedDayEditor と同じ DayPickerGrid**（日曜始まり・
  タップトグル・**複数日一括**）へ統一。「適用（N日）」で選択日すべてに同じ希望を登録。チップ編集は**移動の意味論**
  （元の日を選択から外す/別スタッフへ付替えると元の希望を削除。同スタッフ×元日が残っていれば setWish 上書き）。
  未使用 import（Button/OutlinedTextField/KeyboardOptions/KeyboardType/width）除去。表示・入力導線のみ＝スコア不変。
- (3.130.0, 実機指摘2件=制約行の編集＋色ピッカー修正 / bg復元配線の完遂): ①**「登録した制約の変更ができない」**:
  ConstraintsCard/SkillConstraintsCard の行は削除のみだった→ **行タップで変更**（追加ダイアログのプリフィル版
  `ConstraintDialog(editIndex)`・確定で同位置を置換）。vm に `constraintRowValues(family,index)`（生値の取得、
  値順=追加ダイアログの入力順）と `updateConstraint(family,index,values)`（cons3系は addCons3 と同じ正規化
  =最初の空白まで・最大5）を新設。全10族（cons1/2/3系4/41(s)/42(s)）対応。mutateConstraints 経由=undo/再検査/保存は
  追加・削除と同一。②**「色ピッカーのレイアウトがおかしい・現在の色が無い・他の色も選択できない」**:
  ColorPickerDialog が 12色を chunked(5)→端数行2個が weight で巨大化していた→ **20色=5×4 の完全グリッド**
  （端数行は空 Spacer で同サイズ維持）。未設定時に「現在の色」がグレー（hexToColor("")のフォールバック）だった→
  **defaultHex（実効の既定色）**を新設し、必須=#BA1A1A/要調整=#E08A1E/族=重大度の基準色を渡して表示＋パレット✓一致。
  パレットに既定色と MagiAccent 系（赤/橙/緑/青/紫/桃/灰）を収載。③3.127.0-③の `loadAsync(markResult)` が
  **本体未配線**（hasResult=false のまま）だった回帰を完遂: markResult 時は hasResult=true＋resultSchedule も設定。
  ①②は表示・編集導線のみ、③は表示フラグのみ＝スコアリング不変。
- (3.129.0, 冗長性見直し=説明・項目名称の短文化): 指示「説明や項目名称の冗長性を見直す」。監査28件のうち
  適用対象を一括置換: 見出しの括弧補足を削除（今月の作成条件/片手モード/仕上げ最適化/まとめて割当 等）、
  重複説明文の削除（SettingIssues/E7チップ操作説明）、長文説明の要点化（FixSuggestion/Breakdown/ConfirmList/
  実働チェック/WishEditor/NeedDayEditor/StaffRangeEditor/SkillGroupEditor/SectionNote①）、「時間予算」→
  「計算の制限時間」。文言のみ・スコアリング不変。
- (3.128.0, 短予算も複合パイプラインへ=実機指摘「60秒予算を1つだけのアルゴリズムで使用」): AUTO の予算プランで
  31〜90s が ALNS 単発（60s=ALNS×1）＝詰まった HARD 族（アリフ c3n 等）を狙う RSI フェーズが一度も走らず、
  仮説5本も同一解に収束していた。**31〜210s を RSI(2/3)→ALNS(1/3) の複合に統一**（既存 RSIThenALNS チェーン
  を短予算へ拡張）。各段は入力比 keep-best 番兵つき＝入力より退化しない（原理採否・3.74.0 と同方針。bench は
  dispatch/RSI focus を模擬できない）。getAlgorithmLabel と V6FinalBridgePortTest のバンドも同期。
- (3.127.1, クラッシュ修正=「閉じても大丈夫」で即落ち): 実機報告。原因=**マニフェスト不足**。Worker は
  setForeground(FOREGROUND_SERVICE_TYPE_DATA_SYNC) で前景化するが、targetSdk 34+ は WorkManager の
  SystemForegroundService **宣言側にも foregroundServiceType="dataSync" のマージが必須**（権限だけでは不足）。
  無いと起動瞬間に MissingForegroundServiceTypeException（サービス側スレッド発生＝runCatching 捕捉不能）で
  アプリごと落ちる。manifest に tools:node="merge" のサービス宣言を追加。
- (3.127.0, 他画面見直し第2弾＋実機指摘5件の一括): 並列3監査（編集タブ3ドア/ホーム・分析/設定・エディタ）＋
  実機スクショ5枚。**実バグ修正**: ①ホーム見出しが人手不足ゼロの必須違反(希望/禁止連続/群)でも「人手が足りない」と
  誤診断＋GuidedFix が空回り→ 不足なし時は「必須の条件がN件残っています」＋分析タブの修復フローへ振り分け
  ②一括シート(割当/希望)が実行中でも適用でき完了時に黙って上書き消失→ !running ゲート＋ボタンが理由を語る
  ③bg最適化の復元結果が「未作成」表示(hasResult=false)→ loadAsync(markResult) で結果扱い。**実機指摘**:
  ④一括割当の矛盾解消=担当外職員を対象から自動除外（担当外違反の大量生成を根元で防止）＋存在しない「公」の
  文言删除 ⑤凡例チップの形バラつき(有/Aｱ/A4=文字種で内在サイズ差)→32dp固定高で均一化＋違反凡例をFlowRow化
  （縦1文字潰れ解消）⑥「今月にする」→**「来月にする」**(setNextMonth。月末に来月分を作る業務)
  ⑦日別必要人数の**複数日一括設定**（DayPickerGrid を Set トグル化・適用（N日））⑧重み表を分析タブ→設定タブ
  （最適化設定の直後）へ移動。**文言/整合**: SetupGuideCard の旧①〜⑤番号と旧スコープ名→新3ドア名へ・NeedDay
  誘導文・editScope コメント・中断メッセージのボタン名不一致・「直し方→」青リンクの WCAG 担保・StaffRange
  チップ色の違反トークン追従（🔴🟠絵文字凡例を廃止）。クリーン確認: applyAlternative/applyFixSuggestion は
  currentSchedule 更新で D7 後も正常・CSV/JSON 出力は編集中盤面・タブジャンプ先は全て正しい。表示・導線のみ＝スコア不変。
- (3.126.0, 「下書きをつくる」撤去): ユーザー判断「下書きをつくる不要です」。思考誘導カードの補助ボタンと
  onDraft パラメータを撤去し、作成導線は「勤務表をつくる」（本最適化）1本に。generateSimple() は API として温存。
- (3.125.0, 全画面再検証=並列3監査の修正): ユーザー指示「他の画面も再検証」で D7残滓/UDコントラスト/文言整合を
  並列監査。**D7残滓=実害ゼロ**(CSV出力は編集中盤面・死に分岐なし)。**UD白地コントラストの実害5件を修正**:
  ①MagiTagChip の生アクセント文字(橙2.7:1/緑3.4:1)→実効背景に compositeOver+ensureReadable ②グリッド今日
  マーカー緑→tertiary ③編集シートの注記(不足解消/超過)を ensureReadable ④実働チェック「！」橙→warnFg
  ⑤編集シート希望行の桃を太字化(大テキスト3:1基準で担保)。**文言整合2件**: ⑥違反種別の色の説明が自己矛盾
  (外観へ誘導)→「下の2チップから」へ ⑦集計の超過地色を __vioSoft__ トークン追従に(グリッドと同じ色言語)。
  クリーン確認: テーマ/読取モードの stale 文言ゼロ・Color.White は全て ensureReadable 済・ハロー白は白地でも
  設計上問題なし(枠色自体が淡地に対比)。表示のみ・スコアリング不変。
- (3.124.0, スタッフ別カレンダーの判読性=実機指摘「レイアウトが見にくい」第2弾): ①全違反を桃地+枠で塗って
  飽和していた（3.99.0 でグリッドは解消済みの残存）→ **グリッドと同じ3段階**（必須=桃地+実線 / 重=破線のみ /
  軽=右上角マークのみ）へ。CalendarCell を vk(0..3) 受けに刷新。②日付ラベル「12/1(月)」×31 の月接頭辞ノイズ→
  「1(月)」＋土青/日赤の曜日色。③D7残滓の掃除（孤児KDoc・UiState の読取モード言及コメント）。表示のみ・スコア不変。
- (3.123.0, 集計レイアウトの白抜け解消=実機指摘「レイアウトが見にくい」): シフト集計の 0セルが cs.surface
  （UD=真っ白）で表に白い穴が空いて見え、市松状のノイズになっていた→ **0セル=surfaceVariant α0.35 の淡色**へ
  （職員別/日別とも）。日別の**全日0のシフト行（未使用シフト）はラベルも淡色・細字に沈め**、使っている行の
  模様を浮かせる（行は消さない＝存在は読める）。表示のみ・スコアリング不変。
- (3.122.0, 違反種別の色=族ごとの個別設定): 実機指摘「違反種別の色を個別に設定できない」。**チップタップ=その族の
  色を個別変更**（新予約キー `__vioFam_<fam>__`、UiState.violationFamilyColorHex）。未設定族は重大度色
  （__vio__/__vioSoft__）へフォールバック＝従来互換。`resolvedVioColor(ui, cls, hard, soft)` が族→重大度の順で解決し、
  グリッド枠/角マーク（vioCls per-cell 配列を新設）・職員カレンダー・編集シートの理由テキストへ即反映。
  基準色2種（必須/要調整）の一括変更チップも下段に併設。情報(灰)族も族色設定可（設定すれば優先）。表示のみ・スコア不変。
- (3.121.0, D8=外観UD固定): ユーザー指示「外観は UD モードのみ」。テーマセレクタ（自動/明/暗/UD）を撤去し
  **UD（高コントラスト・白地）固定**。MainActivity は MagiTheme(3) 直指定＋ステータスバー暗アイコン固定、
  AppearanceCard は 片手モード＋表示モード（かんたん/プロ）のみに。明/暗/UD の配色定義は MagiTheme に温存
  （mode引数経由・復活可能）。表示のみ・スコアリング不変。
- (3.120.0, D7=読取(結果)モード撤去): ユーザー判断「読み取り結果モードは不要。下書き直すモードだけで大丈夫」。
  勤務表タブの ScheduleModeCard(結果/下書き切替)・gridUi差し替え・CellInfoDialog(3.119.0)・hintReadOnly を撤去し、
  **常に直接編集の1本**（タップ=即編集シート）。最適化完了時は schedule==resultSchedule のため結果はそのまま見える。
  結果スナップショットのモデル(resultSchedule/result専用違反マップ=3.96.0)とcommit/copy APIは温存（UI参照ゼロを明記）。
  誤編集の担保は「元に戻す」。**読取/編集の整合問題(backlog#1系)はモード自体の消滅で恒久解消**。
- (3.119.0, 読取モードの理由表示=実機指摘「なぜ例が出ていないんですか？」): 違反理由の表示(3.109.0)は編集シート
  実装のため、既定の**読取(結果)モードではタップしてもヒントだけで「なぜ違反か」が見えなかった**→ 読取タップで
  **見るだけの `CellInfoDialog`**（割当・違反理由の全列挙[重み降順・トークン色]・希望の反映状態・「直すには下書きへ」の案内）。
  変更操作は従来どおり不可。グリッド説明文に「緑リング=反映済み」も追記（前問の混乱対応）。表示のみ・スコア不変。
- (3.118.0, 違反枠のハロー縁取り=実機指摘「禁止の違反の枠が見にくい」): ダークテーマの違反色（淡い赤）が
  桃系セル背景（Cｱ等）と同系色で、c3n の実線枠が埋没していた。`violationBorder(halo: Color?=null)` を拡張し、
  **実線=5dpハロー(surface)の上に3dp違反色**（外3dp色/内2dpハロー）、**破線=下に太めの実線ハローを敷く**
  （隙間・両脇がハロー色に）。FlatCell(vk1/vk2)・CalendarCell が halo=cs.surface を渡す。角マーク 3.105.0 と
  同じ手法＝任意のシフト色上で枠が浮く。凡例はカード地=surface上なので変更不要。表示のみ・スコア不変。
- (3.117.0, 集中モード=Web試作③の移植・最終候補): 違反フィルタバーに **「集中」トグル**（既定OFF・rememberSaveable）。
  ON で**違反(vk>0)・未反映希望(wkk=2)・注目セル以外を淡色化**（休セル後退=3.99.0 と同じ alpha0.30＋onSurfaceVariant＋細字。
  非表示にはしない＝被覆の文脈は読める）。E7=種類の絞り込み・検索=行強調・集中=異常の浮き上がり、の直交3機能が揃った。
  `ViolationFilterBar(focusMode,onFocusMode)`→`ScheduleGrid/MagiFlatGrid(focusMode)`→quiet 判定。対象はメイングリッドのみ。
  表示のみ・スコアリング不変。これで Web試作検証の移植候補は**全て完了**。
- (3.116.0, シフト別不足サマリー=Web試作①の移植): 勤務表グリッド上部に **「人員不足（全31日中）: B4 29日 ・ 有 19日 …」**
  の1行バナー（errorContainer）。needViolations の covU をシフト別に日数集計（多い順・重複日dedup）。
  「どのシフトが慢性的に埋まらないか」を数字で即答＝採用/教育判断の入口。E7 人員バケツOFF時は非表示（covU 表示と整合）。
  read-only・表示のみ・スコアリング不変。Web試作検証の残り候補は「③集中モード（盤面淡色化トグル）」のみ。
- (3.115.0, 実働チェック=年度始めモードの心臓): D5 残スコープの完了。年間マスタードア先頭に `StaffingRealityCard`
  (read-only): シフト別に **担当できる人数(canDo) ・ 月間需要人日(Σ need1+日別例外) ・ 1人あたり回数 ・
  欠勤余裕 = 担当人数 − 日最大需要**（1人欠けても1日の必要人数を揃えられるか）を ✓/！/⚠ で提示。
  「15人いるから大丈夫」ではなく「B1 は実質4人運営」の認識へ誘導する。データは Problem 由来
  (allowedShiftsFor/needCellLimits)＝チェッカーと同じ実効値。需要0のシフトは非表示。表示のみ・スコアリング不変。
  これで時期モード設計の確定スコープ（月末=3.114.0 / 年度始め=本件）は**全て完了**（年度末=D5で不要）。
- (3.114.0, 入口4分割＋月次チェックリスト＋違反ナビ＋見直し候補): ユーザー承認済みの入力アーキテクチャ実装（D5/D6 の
  範囲確定後）。①**編集タブを3ドアへ再編**: 「月次条件(毎月)／職員管理(随時)／年間マスター(制度変更時)」
  (旧: 今月の調整/シフト希望/基本マスター。シフト希望は月次条件へ統合。4か所目=勤務表グリッドは勤務表タブ)。
  **職員管理ドア**=新設 `StaffManageCard`(入職/退職/改名/所属/スキル▼を職員単位で。Ws1Card と同一 vm API の別ビュー・併存)
  ＋StaffRangeCard(個人回数)。②**月次チェックリスト** `MonthlyChecklistCard`(月次条件の先頭): 職員N名/希望M/S名/
  必要人数(標準+日別例外K件=D6準拠)/入力診断N件 を ✓/！で確認→「▶勤務表をつくる」(runV6FullOptimize+ホームへ)。
  ③**違反ナビ**(ScheduleGrid): 表示中(E7フィルタ通過)の違反がある日を ＜前/次＞で巡回、focusCell=(-1,j) 番兵で
  日ヘッダを2.5秒ハイライト。④**見直し候補メモ**: セル編集シートの違反セルに「基本ルールの見直し候補にする」→
  `ReviewMemoCard`(年間マスター先頭)に積む(セッション内のみ・state非保存と明示)。「勤務を変える/今月の例外/土台を直す」
  の3分岐のうち第3の出口を明示化。全て表示・導線のみ＝スコアリング不変。
- (3.113.1, 自己見直し=3.112系の色トークン反映漏れ2件): ①編集シートの違反理由テキストの重大度色が
  cs.error/MagiAccent.orange 直書き＝ユーザーが変更した違反色(__vio__/__vioSoft__)が反映されなかった→トークン解決へ。
  ②職員カレンダー(StaffCalendarCard)のソフト違反破線が必須色のまま＝重大度の色分けがカレンダーだけ効いていなかった→
  hard ? __vio__ : __vioSoft__。③vm.start()/runLightOptimize() は 3.112.0 の ActionCard 撤去で UI 参照ゼロ＝
  API として温存しコメントで明示（テスト非依存・削除は保留）。
- (3.113.0, 希望シフトの記号バッジ): ユーザー指示「割付と希望が違っていたら希望シフトをバッジで重ねる」。
  勤務表グリッドの未反映希望(wishKind=2)を、旧・桃ドット（中身が読めない）→**希望シフト記号の桃バッジ**(左下、
  surfaceハロー縁取り・記号フォント=セル記号の70%・ensureReadable)へ。反映済(wishKind=1)は従来の青緑リングのまま。
  読み上げ(cd)にも「希望=記号」を併記、グリッド説明文に「桃バッジ=未反映の希望」を追加。表示のみ・スコア不変。
- (3.112.0, スクショ手書き指摘3系統=撤去・カレンダー化・違反色の変更対応): ユーザーが実機スクショ5枚に赤/青/黄で指示。
  **赤（オブジェクト不要→撤去）**: ①`HeroMetricsRow`(対象人数/対象期間タイル=読込ステータス行と重複、定義ごと撤去)
  ②ホーム`ActionCard`「ほかの作り方」(速く/かんたんは主導線と重複・実行中は全ボタン無効の死に領域。定義ごと撤去。
  **固有機能のバックグラウンド実行だけ SettingsCard(設定タブ最適化設定)へ移設**=`SettingsCard(onBgOptimize)`)
  ③実行中カードの見出し「いま、コンピューターが組んでいます…あと約N分…」(進捗行 progressSummary と重複。headline=""
  ＋isNotBlank ガード)。**青（カレンダー形式）**: 日別の必要人数ダイアログの日入力(テキスト+数字キーボード)→
  **`DayPickerGrid`**(**日曜始まり**=3.112.1でユーザー指示・日赤/土青・1タップ選択・40dp床。勤務表の週ページング
  mondayWeeks=月曜始まりとは別物)。WishEditor の日入力は対象外(指示範囲外)。
  **黄（違反種別の色が変更できない）**: `ColorSettingsView` が read-only 凡例でチップを押しても無反応だった→
  **チップタップでその重大度の色を変更**（必須=既存 `__vio__`・要調整=**新トークン `__vioSoft__`**(shiftColors 予約キー、
  vm.setViolationSoftColor/reset・UiState.violationSoftColorHex)・灰=情報は固定）。要調整色は MagiFlatGrid(破線枠/角マーク/
  日ヘッダ下線)・SearchLegendBar 凡例へ即反映(旧 MagiAccent.orange 直書きをトークン解決に)。ColorPickerDialog を internal 化し
  再利用。外観「違反の色」の説明文も更新。全て表示のみ・スコアリング不変。
- (3.111.0, 残作業④⑤⑥の一括完成=用語統一・違反Set化・日別ジャンプ): ユーザー指示「すべて一括完成させます」。
  ⑤**違反マップSet化**: `ViolationReport.cellFamilies`("i,j"→全違反クラスを重み降順、violations=最重1クラスは後方互換で不変)
  を新設し UiState `violationCellFamilies`(+result版) へ plumbing。効果=①編集シートの違反理由が**全列挙**(重なった c42+c3 等が
  1行ずつ) ②**E7フィルタの整合**: 旧は最重族のバケツOFFで表示中の族が同セルに残っていても枠ごと消えた→ `visibleCellVio`
  (フィルタ通過する最重クラス)で vioKind/カレンダー/違反セル一覧/バケツ件数を判定 ③要確認一覧の sub に全族を「・」列挙
  (行数=箇所数は不変)。SessionRegressionTest で「families 先頭==violations」「軽い族も保持」を回帰固定。
  ⑥**日別ジャンプ**: 要確認一覧の日×シフト項目(人員/群レンジ, staff無し)タップ→勤務表タブの該当日列へスクロール＋
  **日ヘッダを primary 枠で約2.5秒ハイライト**(`focusCell=(-1,j)`=日のみ注目の番兵、行セルには一致しない)。「勤務表→」表記。
  ④**用語統一**: covO ラベル「過剰な配置」→**「人員過剰」**(covU「人員不足」/集計凡例と対)・要確認一覧チップ「過剰・調整」→
  「過剰・**要調整**」(凡例/編集シートの重大度語と統一)・マーク/日別サブの「過」→「過剰」「範囲」→「群レンジ」。
  すべて表示のみ・スコアリング不変(inc/breakdown/weights 不変)。
- (3.110.0, C1/C3のタップ時窓ハイライト): 残作業③。セル編集シートを開くと、そのセルの違反が c1/c3/c3m の場合に
  **違反が指す窓/連の範囲を primary 枠でグリッド上に表示**(シートを閉じると消える)。`vm.violationRange(i,j)`=
  c1は最初の不足窓・c3/c3mは未完成パターン窓または単一シフト連の実範囲を Problem から再計算(read-only)。
  `focusRange`(i,開始日,終了日) を MagiApp→ScheduleGrid→FlatCell へ伝播し focusCell と同枠で描画。
  VBAの「期間を塗る」の利点を、常時でなくタップ時のみ=飽和なしで回収。表示のみ・スコア不変。
- (3.109.0, セルタップで違反理由): 認知ウォークスルー最優先項目。編集シートの状態欄に「⚠ 必須違反: 禁止の並び」
  「△ 要調整: 窓の要件」等を1行表示(violationCells の族→breakdownLabels、3.107 の重み優先で最重の族を保証)。
  従来は枠の意味を要確認一覧/診断ログへ往復しないと理解できなかった「見つける→理解する→直す」の断絶を解消。表示のみ。
- (3.108.0, Web試作の可視化/ナビ移植=ジャンプ＋グループ色帯): Web側試作ログの5機能をネイティブ照合(達成62%)し
  未実装2件を移植。①**セルへのジャンプ**: 要確認一覧のセル違反項目(staff+day保持)タップ→勤務表タブへ切替＋
  該当日列へ hScroll 自動スクロール＋**注目セルを primary 実線3dpで約2.5秒ハイライト後に自動クリア**
  (`focusCell` 状態を MagiApp が保持し ScheduleGrid/MagiFlatGrid/FlatCell へ伝播・LaunchedEffect+delay)。
  回数系項目(日なし)は従来どおり修復フローへ。②**グループ色帯**: 名前列左端4dpに所属群の色帯(出現順に
  黄金角 hsv 自動割当・設定不要)。行追跡の視線ガイド兼用=判読性確率の行追跡72%を補強。表示のみ・スコア不変。
- (3.77.0→3.78.0, 画面修正版の移植融合 ①③): web「画面修正版」を詳細検証しネイティブへ融合（ユーザー承認: 月表=E5は保留維持/
  要確認件数=ロケーション数）。①**週ページング＋横スクロール併用**（ユーザー修正: トグルでなく併用）: `ScheduleGrid` に **前週/次週**
  ボタンを追加し、全日を横スクロールで保持したまま `hScroll.animateScrollTo(週先頭×cellWpx)` で1週ぶんジャンプ。現在週は左端可視日から
  `derivedStateOf` で導出＝自由スクロールにも追従（列は隠さない＝併用）。`mondayWeeks(startDate,days)`(月曜始まり)で週分割。cellW=48 のまま
  ＝1画面≒1週＋スクロールで残り。`MagiFlatGrid(hScroll)` に外部 ScrollState を注入。③**要確認N件**: `ViolationFilterBar` に違反ロケーション数
  (violationCells+needViolations+countViolations の実箇所数)を併記。族fire数(c1=113)でなく作成者が見るべきセル数(golden_state=39)。
  表示のみ・スコアリング不変。
- (3.79.0, 画面修正版 ②検索・凡例の統合折りたたみ): `SearchLegendBar`(既定=閉)を新設し、**検索**(職員名で該当グリッド行を
  太字＋青で強調＝行は隠さず被覆文脈保持)＋**凡例**(ShiftColorLegend＋ViolationLegend)を1折りたたみに集約。グリッド内の凡例は撤去
  (重複回避)。`MagiFlatGrid(nameQuery)`/`ScheduleGrid(nameQuery)` で検索語を伝播。検索状態は勤務表タブに rememberSaveable。
  **E7種別フィルタは折りたたみに入れず独立バーで可視のまま維持**(ユーザー指示)。表示のみ・スコアリング不変。月表=E5は保留維持。
- (3.80.0, 融合仕様 ★1 要確認一覧): 添付 spec `schedule_mobile_fused_minimal.html` の confirm ビューをネイティブへ移植（ユーザー承認:
  E5除外・順序 ★1→2→3→4）。`ConfirmListCard`(MagiDashboardCards)= 散在していた診断を**箇所単位・重大度リスト**で1ハブに統合。
  `confirmItems(ui)` が needViolations(covU/covO/c41/c41s)・countViolations(low/high/c2/aptLow/aptHigh)・violationCells(pref/
  groupViol/c3n/c3/c3m/c3mn/c1/c42/c42s)を個々の項目へ展開し、**不足/過剰/窓**の3重大度マーク付きで列挙（BreakdownCard の族集計を補完）。
  重大度フィルタ(全部/不足・必須/過剰・調整/窓・件数付き)・staff 紐付き項目タップで修復フロー(`vm.findFixSuggestions(i)`)へ・
  設定ミス(settingIssues)あれば先頭に件数導線(→設定タブ)。詳細タブ(3)先頭にヒーロー配置(既存カードは下に併存=安全)。違反ゼロ時は達成表示。
  **表示のみ・スコアリング不変(読取専用)**。フィルタチップは Surface ベース(新規 import 不要)。次段: 2 hero metrics / 3 要確認のみ toggle / 4 日別・人別カード。
- (3.81.0, 融合仕様 ★2/★3/★4): confirm ビューに続く hero/day/staff/alertOnly を移植。**★2 概要ヒーロー** `HeroMetricsRow`(MagiDashboardCards)=
  **対象人数(名)/対象期間(日)/確認事項(件)** の3指標を既存 `BigStat` 再利用で並べ、詳細タブ先頭・要確認一覧の直前に配置。確認事項＝
  violationCells＋needViolations＋countViolations の実箇所数(ConfirmListCard/E7バーと同一定義)。**★3+★4 日別/人別 注意リスト** `AttentionCardsSection`=
  日別(needViolations を日集計・不足/過剰シフト併記)/人別(countViolations＋violationCells を職員集計・行タップで `findFixSuggestions(i)`)を
  MagiSegmentedControl で切替、**「要確認のみ」トグル(既定ON)** で違反0行を隠す＝そのまま triage。**BottleneckCard(top5テキスト・read-only)は
  AttentionCardsSection(全件＋トグル＋タップ修復)の上位互換のため詳細タブから撤去**(~~composable 定義は残置=無害~~
  →3.103.1 で定義も撤去=呼出0)。全て表示のみ・スコアリング不変(読取専用)。
  E5(全月横表)は保留維持。★1→★4 の融合移植これにて一巡。
- (3.82.0, ★1-★4 コードレビュー修正): /code-review(並列 finder×verify)で判明した表示バグを修正(スコアリング不変)。
  ①**ConfirmListCard の迷子フィルタ**: 選択中フィルタの件数がデータ変化で0になるとチップは消えるが `filter` は残り
  空リスト＋見出し件数>0 の迷子に。`effFilter`(件数0なら全部へ戻す)を導入しリスト・チップ選択の両方に適用。
  ②**stale タイトル**: `remember` キーに `staffNames/shiftSymbols/startDate` を追加(職員/シフト改名で行タイトルが古いまま残る)。
  ③**c2 の方向誤表示**: c2(個人の合計)は方向を持たない単一クラス vio-c2 なのに「過」固定だった→ ConfirmList はマーク「計」、
  AttentionCards は方向サフィックスなし(記号のみ)に(下限割れ/上限超過と混同回避)。④`ConfirmItem.shiftSym` 死にフィールド除去。
  非対応(判断): BottleneckCard 定義残置=既に無害と記載済で維持 / AttentionCards の remember 化=毎再構成で再計算するため
  そもそも stale にならず、データ極小で効率影響も無視可 / キー解析の共通化=別スコープ。
- (3.83.0, 見直し=詳細タブの「違反総数」三重表示を解消): 融合カードを旧カードの上に積んだ結果、詳細タブに違反件数が三重化。
  ①**ヒーローの「確認事項(件)」タイル撤去**: 直下 ConfirmListCard ヘッダ「要確認一覧（N件）」と完全に同数＝重複。ヒーローは
  対象人数/対象期間の規模コンテキストに純化(2タイル)、件数は要確認一覧に一本化。②**OverviewDashboard を詳細タブから撤去**:
  「気になる点(=rep.total 総違反リング)」は違反総数の3つ目の見せ方／「注意の日(highRiskDays リング)」は AttentionCardsSection
  日別リストが列挙で上位代替。D2(HARD 三重リング撤去)と同方針で「違反総数」の重複を解消。両 composable 定義は残置(OverviewDashboard は
  未描画の V6RemainingScreens から参照=無害)。CheckSummaryView(守れていない約束=bestHard)/BreakdownCard(族内訳)は固有情報で維持。
  表示のみ・スコアリング不変(読取専用)。

## 禁止連続の枝刈りを任意長へ一般化（三連・五連など, 3.157.0）
ユーザー指摘「三連や五連なども配慮する」。直近3件（`findCovUChain`／`V6LateOperators.c3nHit`／
`CoverageDiagnosis.c3nAt`）の禁止連続(c3n)枝刈りが**すべて「長さ2のペアのみ」**を仮定していた
（`V6LateOperators` の既存コメントにも「長さ2の禁止連続のみ」と明記された既知の狭さ）。実際の
cons3n は `MirrorCore.checkC3Family` の forbidden 分岐で**任意長**（三連・五連等）を正しく評価できるため
（source of truth 側は元から一般的）、枝刈り側だけが取り残されていた。
- **`Problem.makesForbiddenRun(schedule, i, j, newK)`** を新設し単一ソース化: 各 cons3n ルール(長さd)に
  ついて j をカバーする開始位置 s の窓を全部調べ、位置jだけ newK に差し替え残りは現状のまま完全一致(z==d)
  するかを判定（MirrorCore の forbidden 判定と同じ意味論）。他セルは変えない=1手の影響範囲チェックとして正しい。
  枝刈り用途のため、仮に見逃しても最終正しさは常に checker が担保（安全側）。
- 3箇所とも1行で置換: `findCovUChain`/`c3nHit`/`c3nAt` は全て `p.makesForbiddenRun(...)` を呼ぶだけに簡略化
  （`V6LateOperators` は既存コード・`sched` が職員行ごとに独立=同日循環スワップの判定と意味論が一致することを確認）。
  C++側（cons3n を使う fullEval/contribC3RowFam 等）は元々任意長対応済みで変更不要。掃討済み: `cons3` の
  `seq.size==2` フィルタ(HF356「2連続必須の孤立検知」)は別機能で対象外（禁止連続でなく必須連続の孤立検知）。
- ユニットテスト `ChainFillTest`: `makesForbiddenRunDetectsTripleAndQuintuple`（三連/五連の直接検証・positive/negative）
  ＋ `chainFillAvoidsTripleForbiddenRun`（連鎖探索が三連トラップを避けて安全な候補へ着地）を追加。
  スコアリング不変・枝刈りロジックの一般化のみ。
- (3.157.1, 玉突きの三連・五連=多人数連鎖の深さ検証): ユーザー指摘「玉突きの三連、五連なども配慮する」。
  `findCovUChain` は元々 `maxDepth=5`（最大5人の玉突き）に対応済みだったが、テストは深さ1・2のみで
  3人・5人連鎖は未検証だった。各シフトを隣接シフトのみ担当可能な群で一本道につなぎ、末端を過剰配置にした
  盤面で `chainFillSolvesDepth3Cascade`/`chainFillSolvesDepth5Cascade` を追加し、BFS が3手・5手の連鎖を
  正しく1発で見つけること（設計上その深さでしか covU が解消しない一本道のため、乱数シードに依らず一意）を
  検証。あわせて `findCovUChain` の docstring の「長さ2 c3n の前後プルーニング」という古い記述を
  3.157.0 の一般化後の実態（任意長・makesForbiddenRun）に合わせて訂正（HF77: コメント≠実装の解消）。
  スコアリング不変・テスト追加のみ。

## C1研磨への多人数ブロック移動の反映（3.158.0）
ユーザー指摘「C1の研磨にも反映する」。既存4研磨パス（`applyCyclicSwapPolish`/`applyC3SequencePolish`/
`applyBlockRotationPolish`/`applyC1WindowPolish`）は全て本物の checker（`UnifiedViolationChecker.check`→
`isBetter`）で採否判定しており禁止連続は既に任意長で正しい（3.157.0の対象外）ことを確認したうえで、
`applyC1WindowPolish`（C1Polish）固有の別の穴を特定: c1不足の職員 i を窓充足シフト X へ動かす手が、
**「その日 X に既に在勤中の直接交換相手がいる場合」しか試みておらず**、相手がいない/不採用なら諦めていた
（E11で covU に対処した「直接候補が全員ブロックされ玉突きでしか埋まらない」局面と同型の取りこぼし）。
- `applyC1WindowPolish` に E11 の `findCovUChain` をそのまま反映: 直接スワップが不成立のとき、i を X へ
  動かし、空いた旧シフト a の穴を玉突き連鎖で埋め直す（a に need1 が無い/余裕があるなら連鎖不要でそのまま
  採用判定）。i の移動＋連鎖手を1候補としてまとめ、実目的関数(isBetter)で評価。不採用なら連鎖手も含め
  正しく全巻き戻し。`seed: Long = 0x1C1L`（既定値, 決定的）を追加パラメータとして新設（既存呼出は非破壊）。
- **[敵対的レビューで判明した実バグ修正]** `findCovUChain` を「i を x へ動かした直後」に呼ぶため、a を埋め戻す
  候補探索が **i 自身（x→a に戻る）を選び得て、i の移動そのものを打ち消す退行手になり得た**（見つけていなければ
  無限ではないが無駄・意図しない解に着地するリスク）。`findCovUChain` に `exclude: Int = -1` パラメータを新設し
  （全既存呼出はデフォルト維持=非破壊）、C1側から `exclude = i` を渡して自己選択を防止。
- ユニットテスト `c1PolishSolvesViaChainWhenNoDirectSwapPartner`: 直接交換相手が存在しない局面
  （i の担当シフト a を唯一在勤者として持ち、過剰配置の別シフト b から玉突きで補充できる）で、
  旧実装なら頭打ちだった c1 不足が新パスで解消することを検証。解の一意性を保証するため候補を非対称化
  （対称だと乱数シャッフル順で結果が変わりテストがフレークするため）。スコアリング不変・退化不能。

## 敵対的コードレビューで判明した2件の修正（3.159.0）
ユーザー指示「愚直にコードトレースしてコードレビューする」→「修正する」。PR#19マージ後の全差分(12コミット)を
手動で行単位トレースし2件を確認・修正。
- **[CONFIRMED, 正しさ] `findCovUChain.tryComplete` の静的ヘッドカウント補正**: 終端判定(その職員が
  シフトmを抜けても covU が増えないか)が探索開始時点の静的 `cnt[m]` をそのまま使っており、3段以上の
  連鎖では祖先ノードの適用で実際の m のheadcountが変わりうるため不正確だった（呼出3箇所すべてが外側
  keep-best(isBetter/checker)で最終ガードされるため誤採用はしないが、判定自体の精度が甘かった）。
  祖先を辿って m への**「到着」(+1: 祖先の fillShift==m)と「離脱」(-1: 祖先の元シフト==m)を両方**加味した
  真のheadcountで判定するよう修正。**[重要]** 初版修正は到着のみを補正する不完全なものだったが、自己の
  敵対的再検証で「祖先が m から離脱しつつ別の祖先が m へ到着する」3段連鎖（P←Q←M型）だと離脱を見逃して
  headcountを過大評価し、実際には別シフトの covU を悪化させる連鎖を安全と誤判定しうる(false accept)ことを
  発見・出荷前に是正。回帰テスト `chainFillNeverBreaksAnotherShiftViaStaleAncestorCount`
  （P(need1,0人)←Q(need2,2人=a,k1)←M(need1,1人=g) の連鎖で、a:Q→P・g:M→Q・k1:Q→M という手は
  正味 Q から2人抜け1人しか戻らずQを壊すため、正しい修正なら null=見つからないことを固定）を追加。
  既存の depth1/2/3/5 連鎖テストは全て祖先とターゲットのシフトが重複しない一本道構造のため adj=0 で
  不変（手動トレースで確認・退行なし）。スコアリング不変（探索の枝刈り精度向上のみ・最終防波堤は不変）。
- **[CONFIRMED, 表示の整合] CoverageDiagnosis の内訳がcapacityと合わない**: 3.156.0 の4分類
  (空き番/玉突き/希望固定/禁止連続)が「既にこのシフトに在勤中」の職員を素通りしており、
  `free+cascade+pinned+forbid` の合計が `capacity`（担当可能人数）と一致せず表示が混乱を招いていた。
  `already`（在勤中）を明示計上し「担当可能N人（うち在勤中M人）」を追記、内訳4分類は移動候補のみを
  対象とする既存の意味論を維持。読取専用・スコア不変。

## allowedShiftsFor をキャッシュ経由に統一（メインスレッド負荷削減, 3.175.0）
ユーザーのKotlin並行/並列レビュー依頼を受けた並行性監査で、**架構は既に良好**（ViewModel=StateFlow＋
`update{copy}`／`viewModelScope`＋`Dispatchers.IO(ファイル)/Default(計算)`／`job`別キャンセル／`NonCancellable`
仕上げ、エンジン=`coroutineScope`＋`async(Dispatchers.Default)`＋`AtomicInteger`＋`compareAndSet`＋兄弟キャンセル
＋`ensureActive`）と確認。`runBlocking`/`GlobalScope`/`Thread`/メインスレッドI/Oは皆無。唯一の実害は
**`MagiViewModel.allowedShiftsFor(i)` だけが兄弟アクセサ（`staffCellLimits`/`needCellLimits`＝`cachedProblem`
使用）と異なり `Problem(st)` を毎回新規構築**していた点。本アクセサは `StaffingRealityCard` の
`for i: allowedShiftsFor(i)` ループ・`ScheduleGrid`/`AssignBulkSheet` の canDo ラムダ・各エディタから
Compose 合成/再合成中に O(職員数) 回呼ばれ、呼び出し毎に canDo/range/apt/wish 行列を再割当してメイン
スレッドを浪費していた。`cachedProblem(st)`（state 参照で識別する `@Volatile` 単一エントリ ProblemCache・
既にメイン/Default 両スレッドから共用）へ置換。Problem は state の純粋関数＝等価・**スコアリング不変**
（`allowedShiftsForStaff` は bucket を返す読み取り専用）。`allowedShiftsForGroup` は内部で `allowedShiftsFor`
を呼ぶため透過的に恩恵。1行変更（新規レース区分なし＝既存の共用キャッシュに合流するだけ）。

## SaChunk の c3 窓マッチもビット化（3.174.0, 3.172.0の続き）
ユーザー指示「ビット演算できる箇所を見直す…ピックアップする」→ ピックアップした最有力候補（`contribC3RowFam`
の窓マッチ分岐）を「C++化対応する」指示で実装。3.172.0（c1窓・c41/c42系）の続きで、**deltaApply の
ホットパスに残っていた唯一のスカラー窓走査**を popcount 化した。
- **対象**: `SaChunk::contribC3RowFam`（c3/c3n/c3m/c3mn の4族、`deltaApply` が毎手 before/after で呼ぶ）。
  既存 `rowMask[i*K+k]`（職員×シフト→日ビット集合、deltaApply が維持済＝**新規マスク不要**）を消費して:
  - **forbidden（c3n/c3mn 完全一致 #fire）**: `full = rowMask[seq[0]]; for l: full &= (rowMask[seq[l]] >> l)`
    で「窓開始 j に完全一致」の日集合を得て `popcount(full & 有効範囲)`。c1（窓ごと popcount の O(T)）より
    さらに畳めて **O(D) の AND＋popcount 1回**。
  - **非forbidden 多シフト（c3/c3m の z<D-1）**: `popcount(rowMask[first] & range) − popcount(full & range)`
    （先頭一致数−完全一致数＝部分不一致数）。
  - **非forbidden 単一シフト連（rowDeficit）は run長ベース＝スカラー据え置き**（3.172.0 の「popcount化困難」
    方針を踏襲）。マスク索引の安全のため `seq` が全て [0,K) のときのみ bit path（範囲外は理論上到達不能だが
    scalar へ退避＝audit#7 の「C++側でOOBを作らない」原則）。`D>T`・`!useBits`（S,T>64）も scalar。
- **オラクル不変**: `fullEvalParts`/`c3check`（2層番兵の基準・別関数）はスカラーのまま＝番兵の照合基準は不変。
- **検証（提示コードを信用せず独立再現）**: `tools/native/host_parity_bench.cpp` に多シフト D=3(非forbidden)/
  D=4(forbidden) 規則を追加し、サンドボックスで `g++ -O3 -DMAGI_HOST_TEST` ビルド・実行。5種の合成問題×6
  シード×約150万手（scalar/bit 両path）で **mismatch=0**、`deltaApply` スループットは **×1.94**（3.172.0 の
  c1+c41 のみ時 ×1.32 から向上＝c3はルール数最多のため窓マッチbit化の寄与が大きい）。実データ(S=10,T=31)は
  `useBits` 常時真＝本番で経路使用。

## CoverageDiagnosis の need2 単独定義セル見落とし修正（3.173.0）
ユーザー指示「あなたが正しく論理的に不具合をつけてください」を受けた独立監査で発見・修正。
`V6PortAnalyzer.diagnoseCoverage`（人員不足診断、`CoverageDiagnosisCard`のデータ源）が
`val need = p.need1[k][j]; if (need <= 0) continue` で **need1 のみ**を見ており、`Problem.covUCell`
（source of truth、docstring「片方定義=その値（P2単独定義セルも評価）」）が本来 need2 単独でも有効な
不足として扱うセルを**丸ごと診断から見落としていた**。バックログ既知項目「②CoverageDiagnosisのneed1の
み判定（need2<need1の逆転データでOR救済を無視、通常運用では無害な理論的エッジケース）」はこの一部
（両方定義済みで過大報告になるケース）のみを指しており、**need1が完全未設定・need2単独定義のセルが
診断に一切現れない**というより広く・データ入力の通常運用でも起こり得る欠落（false negative）は未記載
だった。実際の最適化器/チェッカー（Evaluator/DeltaEvaluator/MirrorCore/magi_native.cpp）はcovUCellを
共通ソースとして正しく評価・ペナルティを課すため**勤務表そのものは正しく最適化される**。影響は
診断UI（CoverageDiagnosisCard）が本物のHARD違反(covU)を「不足なし」であるかのように見せてしまう
表示層のみ。修正: `miss` を `need1-got` の自前計算から `p.covUCell(k,j,got)`（source of truth）の
直接呼び出しへ置換し、表示用`need`は `got+miss`（実際に不足を生んだ実効しきい値、covUCellのOR選択と
数学的に整合）から逆算。同根の穴（緩和案候補`demandShifts`が`need1>0`のみで判定）も同じ関数内で発見し
同時に修正。回帰テスト`diagnoseCoverageCatchesNeed2OnlyShortfall`追加（need1=""・need2="2"・配置1人の
盤面でmiss=1を検出）。読取専用・スコアリング/エンジンは不変（診断表示のみ）。
## メインスレッド負荷の削減=表示解析の並列化＋起動I/Oの並行化（3.176.0）
ユーザー指示「並行(Concurrency)・並列(Parallelism)を検証・適用してモダン化」。エンジンの重い処理
（handleCheck/handleOptimize 等）は既に `suspend fun … = withContext(Dispatchers.Default)` で Main 外だが、
**その結果を表示用に再解析する `makeUi` が Main 上で逐次実行**されていた（`_ui.update { makeUi(...) }` は
`withContext` を抜けた直後＝`Dispatchers.Main.immediate` で走る）。makeUi の4パス（`V6PortAnalyzer.analyze`／
`V6SanityPort.build`／`diagnoseCoverage`／`buildViolationDebug`＝いずれも内部に `withContext` を持たない純同期
関数）は**同じ不変入力にのみ依存し相互参照しない**ため、セル編集ごと（refreshCheck）に Main で `sum(パス)` を
費やしていた。**表示ロジック不変・スコアリング不変**（makeUi の出力は同一、実行スレッド/並列度のみ変更）。
- **並列(Parallelism)**: `analyzeParallel`（新設・suspend）で4パスを `async(Dispatchers.Default)` に分解し
  `coroutineScope` 配下で同時実行→壁時計 `sum → max(パス)`（最重量=全制約走査の buildViolationDebug）。
  `v6Logs` は sanity/coverageDiag 依存のため依存先だけ先に await（依存グラフ尊重）。純関数の出力を不変ホルダ
  `Analysis` に束ね、共有可変（`rawDiagLogs`）の書き込みと `_ui.update` は**メインスレッドの単一ライタ**に限定
  （背景から書かない＝レース不能）。全 makeUi 呼び出しを `pushReport(st, sched, report, nonCancellable?) { … }`
  の1経路へ集約（18箇所置換）。停止(keep-best)経路は `nonCancellable=true`＝`withContext(NonCancellable)` で
  スコープキャンセル後も解析を完了（既存の keptReport 計算と同思想）。
- **並行(Concurrency)**: `init` の独立3ファイル読み込み（自動保存/中断マーカー/完了結果）を逐次
  `withContext(Dispatchers.IO)` から `async(Dispatchers.IO)` + `Triple(a.await(),b.await(),c.await())` へ＝I/O待ちを
  重ね合わせ起動レイテンシ短縮（snapTxt/bgActive は resultTxt 依存のため逐次のまま）。
- **軽微(同種の Main 上同期CPUの掃討)**: `applyBgResult`/`captureAlternatives`/`applyAlternative` を suspend 化し
  `UnifiedViolationChecker.check` を `withContext(Dispatchers.Default)` へ退避／`start()` の `Problem`/`Evaluator`
  構築を launch 内の `withContext(Default)` へ移動（他経路と統一）。
- 検証: サンドボックスは Android/Kotlin コンパイル不可＝波括弧/丸括弧balance（差0）と全 makeUi 呼び出しの
  置換漏れ0を静的確認。最終判定は CI（v6-engine-check の assembleDebug/testDebugUnitTest・Release Build）。
  HF77 非該当（重み/スコア不変・探索内部不変、UI 反映のスレッド/並列度のみ変更）。

## SaChunk のビット化評価（c1窓・c41/c42/c41s/c42s の O(1) 化, 3.172.0）
バックログ#6（自動パリティテスト無し）への根本対策として提示されたホストビルド可能なパリティ/ベンチ
harness（`tools/native/host_parity_bench.cpp`）を先に検証してから適用（バックログ#6は依然未解消＝この
harness はオンデマンド実行専用でCI配線はしていない。将来 CI 化する場合はバックログ#6の続きとする）。
- **中身**: `SaChunk`（C++ SA差分評価の中核）に `S<=64 && T<=64` のとき有効化される bitmask
  （`rowMask[S*K]`=職員×シフトの日ビット集合／`dayShiftMask[T*K]`=日×シフトの職員ビット集合／
  `grpMask`/`sskMask`=群/スキル群→職員ビット集合、静的）を追加し、`contribC1Row`(c1窓制約)と
  `contribDayGroups`(c41/c42/c41s/c42s)を popcount ベースの O(1)（走査でなくビット演算）へ置換。
  `fullEvalParts`/`fullEvalCombined`（2層番兵のオラクル）は**意図的にスカラーのまま不変**（diff で該当
  範囲に触れていないことを確認済み）。JNI 関数群は `#ifndef MAGI_HOST_TEST` で囲み、ホストharnessが
  同じ .cpp を `#include` してJNI依存なしでビルドできるようにする追加のみ（Android/CMake ビルドは
  `MAGI_HOST_TEST` 未定義のため無変更＝本番JNI面に一切影響なし）。
- **検証（提示されたコードをそのまま信用せず本セッションで独立に再現）**: サンドボックスで
  `g++ -O3 -std=c++17 -DMAGI_HOST_TEST -I app/src/main/cpp tools/native/host_parity_bench.cpp` を実際に
  ビルド・実行し、5種の合成問題(最大40職員×62日×20シフト)×6シード×4万手(リバート含め約150万手)で
  **mismatch=0**、`deltaApply` 単体スループットは環境ノイズ込みで **×1.12〜1.37**（-O3再現で×1.32、
  提示値と整合）を独立に確認。あわせて①`buildGroupMasks`が群id最大値を`sgrp/ssk`だけでなく
  `cons41/42/41s/42s`の参照群idからも算出しベクタを安全にサイズ確保している点（制約側の群idはKotlin
  `Problem.kt`の`groupIdxOf/skillGroupIdxOf`が`mapNotNull`で負値を事前に除外済み＝`grpMask[(size_t)c.g]`
  の負値キャストOOBは構造的に到達不能）②全セル変更が例外なく`deltaApply`経由（直接`a[]`書換え箇所なし
  ＝bitmaskが盤面と乖離しない）③`1ULL<<64`のUB回避（`c.d1>=64`の別分岐）、をコードトレースで確認。
  実データ(S=10,T=31)は`useBits`が常時真になる規模＝本番で実際に経路が使われる。
- **対象外**: covU/covO・pref・c2・c3系・range/apt/fair/weekly はスカラーのまま（bitmask化は c1/c41系の
  みが対象。他族は職員数ループが既に軽い、または L1偏差など popcount で表現しにくいため対象外）。

## ネイティブ照合トグル＋監査#7 SIGSEGV修正（3.171.0）
ユーザー質問「C++移行の実機確認が済んだので、Kotlin パリティ照合の役目終了ですか?」への回答と、
別セッションの未レビュー領域監査（3.168.0系）で見つかった項目の対応。
- **結論（質問への回答）**: 終わっていない。実機確認は「試した範囲」の正しさしか保証せず、今後入力される
  未知のデータ形状までは保証できない。C++評価器のパリティを検証する自動テストが無い（JVM単体テストは
  `.so` をロードできず、CIはC++の**コンパイル成功**しか見ない＝**意味的乖離は捕捉しない**）ため、実行時の
  Kotlinパリティ照合が唯一の安全網。既定ONを維持する。
- **照合トグル**（ユーザー提供パッチを適用・明示承認2026-07-15）: 設定タブに「Kotlin照合」トグルを追加
  （`NativeGate.parityCheckEnabled`・既定ON・ネイティブ加速ON時のみ操作可）。OFF=純ネイティブ＝5経路
  （起動時フル＋SA/LAHC/ALNS/Polish各チャンク後のLong==再評価）すべてスキップしC++結果を信頼する
  **検証/ベンチ専用モード**（⚠警告ラベル・誤った勤務表が表示される可能性を明記）。C++内部の自己整合
  (status)番兵はトグルと独立に常時ON。診断ログ NativeBridge 行は OFF 時「Kotlin照合OFF＝純ネイティブ」を
  Wレベルで表示。
- **監査#7修正（SIGSEGV潜在バグ）**: 探索オペレータ約13箇所（`applyDayAssignmentPolish`/ALNS各オペ等）が
  `p.bucket[p.sgrp[i]]`／`grpCnt[sgrp[i]*K+k]` を sgrp範囲未検証で使用。Kotlinなら不正indexは例外→
  runCatchingで安全退化するが、C++はUB（bucket=範囲外読み・grpCnt=範囲外**書込=ヒープ破壊**）でSIGSEGVが
  runCatchingに捕まらずプロセスクラッシュし得た（正規のエディタ/取込では`groupIdx`は常に`[0,G)`のはずで
  到達性は低いが潜在）。個別箇所を13箇所ガードするのではなく、`nativeCreateProblem`（ハンドル生成の唯一の
  入口）で`sgrp`を一括範囲検証し、外れていれば生成自体を拒否（0返却）する方式を採用。既存の
  「handle==0=native不可→Kotlinへ安全退化」という確立済みの契約（`NativeEval.createHandle`の全呼出元が
  既に`runCatching{...}.getOrDefault(0L)`でこの規約に従っている）にそのまま乗るため、Kotlin側の変更は不要
  （C++の1ファイルのみ）。
- **監査#6（自動パリティテスト無し）**: 緩和策として照合トグル（既定ON維持）を実装したが、根本対策
  （ホストビルド可能なパリティfixtureをCIへ追加）は未着手のまま残す（バックログ#6）。
- **監査#8（SAチャンク自己整合の非対称）**: 優先度低のため今回は対応せず、バックログ#8に記録のみ。

## weekly/fairも同じ理由でRSI探索focusに追加（3.170.0, 「apt以外は大丈夫か」への回答）
ユーザーの追加確認「apt以外は大丈夫ですか?」を受け、apt同様の穴が他族にも無いか同じ実データ
(state.json)で網羅的に検証。**weekly（7日周期の曜日偏り）のL1偏差合計は65で、apt(37)より大きい**
（上條洋平11・大島愛10 等）。fair（グループ内公平化）も合計11で非ゼロ。両者とも apt と全く同じ
原因＝`maxViolatedFamily`の order に無く RSI 探索中は一度も focus されていなかった。
- **対応**: orderにweekly/fairを追加し、`rsiGenerateHypothesis`のapt/low/high/c2と同じ
  `destroyRepairStaff`経路へ合流。**正直な限界の明記**: `staffCountPenaltyAt`（destroyRepairStaffの
  marginal cost）はlow/high/aptには対応済みだがweekly/fairの cost 計算は未対応（weekly=曜日バケット・
  fair=群平均が必要で、対応するには`weeklyDevOfBucket`/`DeltaEvaluator.fairDevAt`相当の統合が要る、
  より大きな改修）。今回は「専用ラウンドを割り当てるだけ」の focus 露出に留めた＝厳密な cost-aware
  研磨ではないが、無指向な"total"空振りよりは改善機会が増える、hard>0時は完全no-op・keep-best不変の
  安全な最小差分。将来の拡張候補として cost 関数への正式統合を残す。**→ 3.267.0で解消**
  （`weeklyMarginalAt`/`fairMarginalAt`を新設し`destroyRepairDayAt`/`destroyRepairStaffAt`/
  `destroyRepairViolations`のmarginal cost計算へ統合。詳細は3.267.0セクション参照）。
  テスト2件追加（weekly/fair優位選択）＋smokeテスト拡張（weekly/fair focusが例外なく完走）。

## apt(適切回数)をRSI探索focusに追加（3.169.0, 「公平化のズレ」実機report対応）
ユーザーが実機TallyCardスクショ（多数の▼/▲）を提示し「公平化のズレの研磨などが出来ていない」と報告。
実際のstate.jsonで検証したところ、staffRange(低/高, 重み90/45)の乖離は合計わずか3だったのに対し、
**apt(適切回数, 重み1)のL1偏差合計は37**（大島愛「休」実績15 vs 目標10 等）で規模が逆転しており、
スクショの▼/▲は主にapt違反と判明。コード調査で根本原因を特定: `maxViolatedFamily`（RSI探索のfocus
選択）の`order`リストに**aptが一度も入っていなかった**ため、探索中は常にlow/high/c1等の他族に埋もれ、
post-processing（`applyDayAssignmentPolish`のハンガリアン割当）頼みのまま広く未研磨で残っていた。
- **採用した修正**: `order`にaptを追加（groupViol/covU/pref/c3nのHARD優先ルールは不変＝hard>0時の
  挙動は無変化）。`rsiGenerateHypothesis`の`"low","high","c2"`分岐に`"apt"`を追加し**既存の
  destroyRepairStaff経路へ合流**（`staffCountPenaltyAt`のmarginal costには既にapt(重み1)が織込み済み
  ＝新規オペレータ不要、最小差分）。ラウンド単位 better() keep-best でゲート済＝退化不能。
- **検証方針の訂正**: 当初ユーザーに「tools/nsp_bench.pyで実測A/B検証してから進める」と伝えたが、
  `nsp_bench.py`は"focus"/"RSI"/"maxViolatedFamily"の概念を一切持たない（grep 0件）ため、**この種の
  focus選択変更はそもそも計測不能**と判明（3.74.0/3.95.0の先例と同じ制約＝「bench は RSI focus/
  portfolio を模擬できない」）。よって同じ2件の先例と同方針＝**実測でなく原理（hard>0時は完全no-op・
  既存の実証済みdestroyRepairStaff経路への合流のみ・keep-best不変）で採否**。
  `V6NativeOptimizer.maxViolatedFamily`/`rsiGenerateHypothesis`を`private`→`internal`化しテスト可能に。
  ユニットテスト3件（apt優位選択・HARD優先の回帰・全0時"total"フォールバックの回帰）＋
  focus="apt"のsmokeテスト（例外なく同一次元の盤面を返す）を追加。fair/weekly はセル位置を持たない
  集約指標のため対象外のまま（現状維持）。

## 希望シフトカレンダーのインタラクティブ化（3.168.0）
ユーザーが第3のモックアップ（希望シフト登録画面）を提示。3.167.0の必要人数カレンダーと同じ
方針転換をWishCardにも適用（AskUserQuestionのタイムアウトにより明示確認は取れなかったが、
本セッション内で同型の再設計を2回繰り返し確定済みの方針＝カレンダーをタップ選択の主導線にし
旧モーダルは廃止・全件横断の一覧は確認用に温存、を踏襲）。
- **NeedCalendarCardとの違い**: 必要人数カレンダーは「シフトを選び月間の需要を見る」(シフト軸)
  だったのに対し、希望シフトカレンダーは**「職員を選び月間の希望を見る」(職員軸)**。日セルには
  数値レンジでなく**登録済みシフトの記号チップ**を表示し、色は severity ではなく**シフトの表示色**
  （既存の`ui.shiftColorHex`/`shiftTextHex`＝グリッド・集計と同じ色語彙、`resolveShiftColor`由来）
  をそのまま流用（新規の色システムは作らない）。「1日1個のみ登録」は`wishes["i,j"]`が単一値の
  Mapである既存モデルにより自動的に保証される（追加ロジック不要）。
- 実装（`WishEditor.kt`全面刷新）: 職員チップ（複数可・FlowRow）→`WishMonthGrid`（タップで複数日
  選択、登録済み日はシフト色チップ表示）→下部固定パネル（希望シフト選択・担当外は赤枠+⚠・
  「選択したN日間に適用する」）。旧`WishDialog`は廃止（モーダルでの単発編集をカレンダーへ統合）。
  全職員横断の登録済み一覧はNeedDayCardと同じ理由（カレンダーは1職員ずつしか見えない弱点を補う）
  で確認・削除専用に温存（タップで対象職員へジャンプ、×で削除）。
  `NeedDayEditor.kt`の`DayPickerGrid`（旧WishDialogの唯一の呼出元）は呼出ゼロになったため削除。
  `MagiApp.kt`の`wishQuickAdd`（旧WishDialog自動オープン用）も同時に削除（WishCardが常時展開の
  カレンダーになったため、案内カードからは月次条件タブへの遷移のみで到達可能）。
  新規ロジックなし・既存のwishes/removeWish/setWish呼出と既存シフト色トークンの読取のみ＝スコアリング不変。

## 必要人数カレンダーのインタラクティブ化（3.167.0）
ユーザーが第2のモックアップ（必要人数設定/Dテスト画面）を提示し「あなたが見直します」と再改訂を指示。
3.166.0で作った表示専用の`NeedCalendarCard`から方針を転換するため grilling で5問詰めた:
- **カレンダーをタップ可能にし主要な編集導線にする**（表示専用から転換）。
- **旧NeedDayCard（追加/編集ダイアログ）は廃止**、一覧は「全シフト横断で例外を一目確認・削除する」
  専用ビューとして温存（カレンダーは1シフトずつしか見えない弱点を補う）。
- **「基本の必要人数」（シフト既定need1/need2、従来Ws1Card専任）もこの画面で直接編集可能にする**
  （ユーザー選択・Ws1Cardとの二重入口を許容）。
- **日セルの色分けドットは「現在の勤務表の実際充足度」**（設定値の静的比較ではなく、実際の配置人数
  vs 需要）。TallyCard/グリッドの既存語彙（赤=covU=不足・橙=covO=過剰）にそのまま統一。
- **月送り(<>)ナビは導入しない**（1state=1か月のスナップショットモデル、D6決定と整合。常に現在の
  1か月を表示）。ヘッダの戻る矢印/？/ハンバーガーも既存アプリのCard内蔵ナビ様式に合わないため非導入。

実装（`NeedDayEditor.kt`）: `NeedCalendarCard`にタップ選択(`onToggleDay`、DayPickerGridと同じトグル
方式)＋下部固定パネル（選択日数・最低/最高ステッパー・「選択したN日間に適用する」）を追加。
`NeedMonthGrid`は表示専用から選択可能グリッドへ刷新し、日セルへ`ui.needViolations["k,j"]`
（既存の実効チェック結果、read-only）を突合して赤/橙/緑/灰の4色ドット＋選択時はチェックマーク表示に。
「基本の必要人数」ボックスは`vm.ws1().shifts[k]`から直接表示し、新設`vm.setShiftNeed(k,need1,need2)`
（`ws1EditShift`の狭い版・name/kigouは不変のままneed1/need2だけ更新）でインライン即時編集。
`NeedDayCard`は一覧+削除ボタンのみに簡略化（`NeedDayDialog`/`NeedDayEdit`は呼出ゼロのため削除）。
新規ロジックなし・既存の実効値/チェック結果の読取＋既存setNeedDay/setShiftNeedの呼出のみ＝スコアリング不変。

## 必要人数カレンダー＋希望シフトの既存登録可視化（3.166.0）
ユーザー提示のモックアップ画像（必要人数カレンダー: シフト種類チップ＋月全体カレンダーに実効need
をインライン色分け表示＋設定済/未設定サマリー）の長所を、既存の「日別の必要人数（例外）」画面
（`NeedDayCard`=一覧＋追加ダイアログ）に取り入れるかgrillingで詰めた。ユーザー選択: **既存の一覧＋
ダイアログの操作系は温存し、カレンダー概観を「追加」するのみ**（丸ごと置換ではない）。カレンダーは
**表示専用**（タップ不可、編集は従来どおり下の一覧から）。
- 新規 `NeedCalendarCard`（`NeedDayEditor.kt`）: シフト種類チップ（`InputChip`・1つ選択）で対象シフトを
  絞り込み、月全体を`NeedMonthGrid`（日曜始まり・読取専用）で一覧。各日は`vm.needCellLimits(k,j)`
  （既存API＝シフト既定need1/need2と日別例外needDay1/needDay2を統合済みの実効値、Problemが source）
  をそのまま使い「lo-hi人」チップ(緑・設定済)/「未設定」(グレー)で色分け。設定済N日/未設定N日の
  サマリーも表示。`MagiApp.kt`の月次条件スコープでWishCardとNeedDayCardの間に配置。
  新規ロジックなし・既存の実効値取得APIの読取表示のみ＝スコアリング不変。
- **実機指摘「登録済みの希望シフトが表示されていない」の修正**: 希望シフト追加/編集ダイアログ
  （`WishDialog`）の日選択カレンダー（`DayPickerGrid`）が、今回の選択状態しか示さず、その職員が
  既に登録済みの希望（他の日）が見えなかった。`DayPickerGrid`に`markedDays: Map<Int,String> =
  emptyMap()`（既定=空で既存呼出は無変更）を追加し、`WishDialog`が選択中の職員の既存希望
  （`rows.filter{it.i==idx}.associate{it.day to it.kigou}`）を渡して日番号の下に小さくバッジ表示
  （桃色=`MagiAccent.pink`＝CLAUDE.md定義の「希望」意味色と整合、`ensureReadable`でコントラスト確保）。
  マーク表示時のみセル高を40→50dpへ拡張（バッジ無しの呼出=NeedDayEditor側は40dpのまま影響なし）。
  表示のみ・スコアリング不変。

## 「Dﾃ-Dﾃ」仮説の検証＝隣接日調整の全候補探索を実データで確認（3.165.0）
ユーザー指摘「残り3人（金沢勇輝・モニカ・アリフ）は、夜勤に動かすと別の禁止連続パターン（Dﾃ-Cｳ、Dﾃ-休-Aｱ、
Dﾃ-Cｱ）に触れてしまうのであれば、Dﾃ-Dﾃを検証する」。実機ログ由来の3名を、実データ(state.json, 実際の
cons3n=Dﾃ-A4/Aｱ/Cｵ/Cｱ/B4/Cｳ/B1・Dﾃ-休-A4/Aｱ の3連含む)を Python で忠実リプレイして検証:
- **結論=3名とも既存の `tryFixC3nViaAdjacentDay`（3.163.0）の altOrder 走査で解決可能**（追加実装不要）。
  ただし「同じシフトの繰り返し(Dﾃ-Dﾃ)」自体は万能ではないことも判明: altOrder は休を最優先し、次に
  担当可能シフトを順に試すため「同じシフトの繰り返し」は自然に2番目辺りで試されるが、翌々日が別の
  禁止連続の相手（例: モニカの Dﾃ-休-Aｱ→隣接日を Dﾃ にしても Dﾃ-Aｱ で新たに触れる）だと単体では
  不成立＝**問題を1日先へずらすだけ**になる。しかし実装は1つの alt で諦めず altOrder 全体（休→担当可能
  シフト全種）を試すため、金沢勇輝(Dﾃ-Cｳ)・アリフ(Dﾃ-Cｱ)は日別の翌々日が空いていれば即座に休で解決、
  モニカ(Dﾃ-休-Aｱ)のように繰り返しも直接候補も両方塞がる局面でも「有」等の第三の安全なシフトで解決する
  ことを確認（実データ照合スクリプトで3パターンとも成立を確認）。
- **回帰テスト追加**: `ChainFillTest.chainFillAdjacentFixTriesRepeatShiftThenFallsBackToSafeAlternative`
  （P-N 2連禁止＋P-休-N 3連禁止の最小構成。「繰り返し(P-P)」を試すが P-N で新たな禁止連続に触れ不成立→
  第3のシフトOで解決することを固定）。既存 `chainFillResolvesC3nBlockViaAdjacentDayFix`（P-P-P三連・
  日を変えて解決）とは異なる形（3連パターン・繰り返し失敗→exhaustive探索で解決）をカバーし、実装が
  「最初の1手で諦めない」ことを保証する。ロジック変更なし・検証専用のテスト追加のみ。

## 希望シフト登録の見つけやすさ改善（3.164.0）
ユーザー要望「希望シフトの登録画面の新規追加する。職員選択、シフト種類選択、カレンダーによる複数日選択して、
職員毎に一括して希望シフトが登録変更確認できるようにする」。調査の結果、要望内容（職員選択→シフト種類選択→
カレンダー複数日選択→職員ごとの一括登録・変更・確認）は既存の`WishCard`/`WishDialog`（`WishEditor.kt`、
編集タブ→月次条件）で**単一職員に対してはほぼ完全に実装済み**と判明。確認したところユーザーはこの既存機能を
知らず「見つけやすさの改善」が真のニーズだったため、**新規画面は作らず既存入口の到達性を改善**した:
- `SetupGuideCard`（編集タブ最上部・editScopeに関わらず常時表示）の「希望シフト」行と、
  `MonthlyChecklistCard`（月次条件タブ先頭）の「希望・休暇」行を**タップ可能化**し、タップで
  editScope=0（月次条件）に切替えつつ`WishCard`の希望追加ダイアログへ**直行**する。
  タップ可能な行は「›」を付し文字色をprimaryに変えて可視化（`GuideRow`/`ChecklistRow`にoptionalな
  `onClick`パラメータを追加、非タップ行は従来どおり）。
- `WishCard`に`autoOpenAdd: Boolean = false`/`onAutoOpenConsumed: () -> Unit = {}`を追加し、
  `LaunchedEffect(autoOpenAdd)`で自動的にダイアログを開き即座に消費フラグを戻す（タブ再訪時の
  意図しない再オープンを防止）。`MagiApp`に`wishQuickAdd`状態を新設し両カードへ共通の`openWish`
  コールバックとして配線。新規画面・新規データモデルなし・表示/導線のみ＝スコアリング不変。

## covU多人数連鎖(E11)を禁止連続の回避=隣接日調整へ拡張（3.163.0）
ユーザー指摘「残り1人は動かすと連続禁止ルール（c3n）に触れるため使えないのであれば、連続禁止ルールの
並びにならないようにする」（CoverageDiagnosisの「禁止連続」ブロック候補を見て）。grillingで「隣接日調整
自体が新たな不足/禁止連続を生む場合の扱い」を確認 → **そこも玉突き連鎖として深掘りする**方針で確定。
- `findCovUChain`（`V6SearchOperators.kt`）の `candidates()` で、候補 i が禁止連続(c3n)に触れて除外され
  ていた箇所を拡張。即除外せず `tryFixC3nViaAdjacentDay(i, fillShift)` を試す: 隣接日(j-1/j+1)の i 自身の
  割当を別シフト（休を優先、続けて担当可能シフト一覧）へ変えてパターンを崩せるか、`makesForbiddenRun` で
  day j・day j2 双方の安全性を確認。崩した隣接日の元シフトが covU 悪化を招く場合は、**同じ `findCovUChain`
  を `allowCrossDayFix=false` で1段だけ再帰**し玉突き連鎖として埋め直す（cross-day 再帰は1段のみに制限＝
  無限展開防止。同日内の同一BFS(`maxDepth`)自体は従来どおり最大5人まで）。見つかった追加手は
  `Node.extra: List<IntArray>?` に積み、`tryComplete` の手順収集時に合流する（day j 本体の手＝
  `[(i,j,fillShift)]` はそのまま・day j2 の手＋サブ連鎖は `extra` として付随）。盤面は判定中に一時変更する
  が、成功・失敗いずれの分岐でも呼出前に必ず復元（本関数の「盤面を変更しない」契約は不変）。
- `findCovUChain` に `allowCrossDayFix: Boolean = true` パラメータを新設（全既存呼出はデフォルト維持＝
  非破壊。RSI focus/エピローグ/C1Polish はいずれもこの新機能の恩恵を自動的に受ける）。
- ユニットテスト: 既存 `chainFillAvoidsTripleForbiddenRun` は新機能により結果が非決定的になり得たため
  （bがaの隣接日肩代わりに使え、RNG次第でaかbかが変わる）、bへ day0/day2 の希望固定を追加して隣接日調整
  の対象外に固定し、従来の「三連トラップを避けてbのみ使う」という元の検証意図を保った（決定的に復元）。
  新規 `chainFillResolvesC3nBlockViaAdjacentDayFix`: bをday1のみ希望固定（直接候補から除外）し、aの
  隣接日調整＋玉突き（day0を休へ・bがday0のPを玉突き充填）でcovUとc3nが両方解消することを検証。
  スコアリング不変・退化不能（最終防波堤は既存のkeep-best/isBetter、呼出元は全て変更なし）。

## 対応OSをAndroid 16以降のみに変更（3.162.0）
ユーザー指示「Android 16以降のみ対応する」。`minSdk` を 35(Android 15) → **36(Android 16)** へ引き上げ
（compileSdk/targetSdkは元々36で変更なし）。API 36未満の端末は対象外になる（Google Play配布時は
インストール不可端末が絞られる）。関連コメント（`app/build.gradle.kts`のarm64限定理由・
`OptimizationWorker.kt`のforegroundServiceType注記）を更新。`ForegroundInfo`生成は元々SDK_INT分岐が
無い（常にFOREGROUND_SERVICE_TYPE_DATA_SYNCを指定）ため、コメント修正のみでロジック変更は無し。
CI（android-sdk/release-build/v6-engine-check）は`platforms;android-35`も引き続きインストールするが
実害なし（AGPが不要なら単に使わないだけ・除去は本変更のスコープ外）。

## 未レビュー領域の再監査（3.161.0）
ユーザー指示「未レビュー領域の再監査」。3.84.0以降に積み重なった大量の変更（ネイティブ第1〜3期・E7〜E11・
Gradle9移行等）を対象に、5系統（ネイティブC++/JNI・SA/ALNS/RSI探索本体・修復研磨パス・UI/ViewModel層・
診断分析層）へ並列サブエージェントを起動し再監査。ネイティブ/JNIとSA/ALNS/RSI探索本体は正しさバグ0
（番兵・keep-best・ハンドル破棄とも健全）。以下、CONFIRMEDな指摘を修正:
- **[CONFIRMED, 重大・実害あり] 最適化実行中のセル編集で配列の別名共有によりデータ消失/違反表示不整合**:
  `runV6FullOptimize`/`start`/`runSoftPolish`は`val sched0 = currentSchedule ?: return`で**参照をそのまま**
  保持し数十〜300秒使い続けるが、`setCell`/`setCells`/`cycleCell`/`applyFixSuggestion`は`currentSchedule`を
  **同一配列へin-placeで直接変更**する。実行中にグリッドをタップして編集すると、良化時は編集が無言で
  上書き消失し、劣化時(`worseThanInput`分岐)は編集後の盤面と実行開始時点の`baseReport`(編集前基準)が
  食い違って誤った位置に違反が表示される。3.127.0でバルクシート/希望一括シート/AlternativesCard/
  WishApplyCardには`!ui.running`ガードを追加済みだったが、単発セル編集(ShiftPickerSheet経由の`setCell`)と
  改善提案適用(`applyFixSuggestion`)が対象漏れだった。`setCell`/`setCells`/`cycleCell`/`applyFixSuggestion`
  （`MagiViewModel.kt`）に`_ui.value.running`ガード＋案内メッセージを追加、`FixSuggestionCard`の適用ボタン
  （`MagiDashboardCards.kt`）も`enabled = !ui.running`に統一。
- **[CONFIRMED, 予算超過リスク] 5研磨パスのO(S²)内側ループに`shouldStop()`欠落**: `applyCyclicSwapPolish`
  （k=2/k=3）・`applyC3SequencePolish`・`applyC1WindowPolish`・`applyGroupShiftEqualizePolish`・
  `applyWeeklyEqualizePolish`が、日(j)ループ先頭のみで締切確認し内側の職員×職員(O(S²))二重ループには
  確認が無かった（HF66=2.65.0・BlockRotationPolish=3.84.0で既に修正済みの「内側スキャンでも締切確認する」
  方針の対象漏れ）。職員数が多いデータで締切超過後も1日分のフルスキャンが走り切りうる。各パスの外側
  職員(a)ループ先頭に`shouldStop()`を追加。keep-best不変・時間予算の逸脱のみを解消。
- **[CONFIRMED, 診断バグ] `V6PortAnalyzer.buildStaffProfiles`が休記号改名時に全日を勤務と誤カウント**:
  `rest`をローカルの`indexOfFirst{kigou=="休"}`で再計算しフォールバックが無く、休記号改名で`rest=-1`に
  なると`schedule!=rest`が常に真＝全職員の勤務日数が常に全期間日数と誤表示された。`weekly`で3.103.0に
  修正済みと同型のバグ（対象漏れ）。`Problem.restIdx`（`?:0`フォールバック付き・source of truth）に統一。
- **[CONFIRMED, 診断バグ] `CoverageDiagnosis`が希望と移動先が一致する候補を「希望固定」と誤分類**:
  `diagnoseCoverage`の候補分類は事前フィルタ(`w!=k`除外)により残る候補が`wish==-1`か`wish==k`のみなのに、
  その後`p.wishLocked(i,j)`（=希望が設定されている、の意味）を「動かせない」判定に使っていた。この文脈で
  `wishLocked==true`は必ず`wish==k`（=まさに動かしたい移動先と希望が一致）＝本来は**最も動かすべき候補**
  （移すと covU と pref を同時に解消できる）なのに「固定」表示は意味が逆転していた。この行を削除し
  free/cascade判定へ委ねる（他シフトへの希望固定は既にcapacity計算の外側フィルタで除外済みのため、
  本関数内で「希望固定」に該当する候補はそもそも存在しない）。
- **[CONFIRMED, HF77=コメント≠実装] `runAlns`のGLS penaltyコメント訂正**: 「再構築は restart 毎のみ」と
  コメントされていたが、実装は`runAlns`呼出につき1個の`GlsPenalty`をrestartループの外側で生成し全restart
  間で共有（decayのみ希薄化、再構築は`runAlns`が新規に呼ばれた時のみ）。globalBestは生スコア別管理のため
  正しさは不変（keep-best）＝実害なし・コメントのみ訂正。
- **報告のみ（未修正・判断/測定待ち）**: ~~①`V6SanityPort`のc1「壁」判定が非休シフトの供給見積りに`need2`
  （covOのSOFT目標）を実質的なハード上限として使っており、covO(重み1.0)を犠牲にc1(重み4)を解消する
  トレードオフが数学的に可能な局面を過大に「構造的不能」と報告しうる~~ **→ 3.364.0 で修正**（実データ計測で
  「Dﾃ は物理供給248>>需要32・golden の手作り盤面は既に35回配置＝真の壁でなく false wall」と確定し、3.179.0 の
  「据え置き」前提を反証。休のみ真の壁を維持し非休は covO-tension として正直化。詳細は 3.364.0 節。**なお 3.179.0 の
  『nCanDo*T へ上げると Dﾃ 壁を false negative 化する』は誤りだった＝非休は物理供給≥需要が常に成立し真の壁は元から無い。**
  ②~~`CoverageDiagnosis`のneed1のみ判定（need2<need1の逆転データでOR救済を無視、通常
  運用では無害な理論的エッジケース）~~ **→ 3.173.0で修正**（同根でより広い「need1未設定・need2単独定義
  セルの完全見落とし」も含め、covUCellへ委譲する形で解消。詳細は3.173.0セクション参照）。
  ③~~`hf80PostPolish`のE10停滞早期終了が、native→Kotlin番兵発火時に
  native区間の経過時間を引き継がず停滞時計を再スタートする（異常系=番兵不一致時のみ発現、実害は電池/時間
  の節約が一部効かなくなる程度）~~ **→ 3.179.0で修正**（native未完了かつ無改善(nat.best==null)なら停滞時計
  起点を`started`にし native 無改善経過を算入。改善済みは最終改善時刻不明のため保守的に nowMs()。keep-best 不変）。
  ④~~`nativeAlnsSetCur`（JNI関数）がKotlin側から一度も呼ばれていないデッド
  コード（挙動に影響なし、C++変更のコスト対効果が低いため未対応）~~ **→ 3.179.0で除去**（JNI定義＋NativeBridgeの
  external宣言を削除。呼出0を grep 確認・parity harness で compile+mismatch=0 確認。`resetBoard`は無害な未使用
  メンバとして残置=最小変更）。

## Gradle 9 移行（3.160.0）
ユーザー指示「Gradle 9移行する」。ビルド基盤を Gradle 8.7/AGP 8.6.0/Kotlin 1.9.24 から
**Gradle 9.3.1 / AGP 9.1.1 / Kotlin 2.3.21（AGP 9 の内蔵Kotlinサポート＋KGPオーバーライド）**へ移行。
（公式ドキュメント確認: AGP 9.1.1 の最小/既定 Gradle は 9.3.1・JDK 17・SDK Build Tools 36.0.0・
同梱 KGP 2.2.10。この組合せは Android 公式リリースノートに明記された自己整合な組み。）
- **`org.jetbrains.kotlin.android` プラグインを撤去**: AGP 9.0+ は Kotlin サポートを内蔵し不要
  （`build.gradle.kts`/`app/build.gradle.kts` 両方）。代わりに **`org.jetbrains.kotlin.plugin.compose`**
  を明示適用（Compose Compiler は Kotlin 2.0+ で独立プラグイン化されたため、使用するKGPの版数と
  一致させる必要がある）。
- **[ユーザー指示「Kotlin 2.3.21以上にする」] KGP を 2.2.10(AGP 9.1.1 既定同梱) → 2.3.21 へオーバーライド**:
  公式手順（`buildscript { dependencies { classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.3.21") } }`
  を root `build.gradle.kts` の `plugins{}` より前に追加）で明示上書き。既定より低いKGPを指定した場合は
  AGP が自動で 2.2.10 へ引き上げるが、より高い版数は明示 classpath 指定でのみ有効という公式仕様に準拠。
  Compose Compiler プラグインの版数も 2.3.21 へ追従（Kotlin本体と版数を一致させる必要があるため）。
- **`kotlinOptions{jvmTarget="17"}` を撤去（置換ではなく削除）**: 内蔵Kotlinの `compilerOptions` は
  `android{}` 直下ではなく別の場所にあり、初版で `android.compilerOptions{jvmTarget=...}` として実装した
  ところ CI（release-build.yml）で `Unresolved reference 'compilerOptions'` と実際にビルド失敗、公式ドキュメント
  再確認で誤りと判明（"You don't need to explicitly set jvmTarget... it defaults to
  android.compileOptions.targetCompatibility" に訂正）。**jvmTarget は既存の `compileOptions.
  targetCompatibility=17` から内蔵Kotlinが自動継承するため、ブロックごと削除**が正しい修正（HF77非該当だが
  「CIの実結果で検証する」という本移行自身の方針どおり、誤りをCI失敗で検出→即訂正した実例）。
- **`composeOptions{kotlinCompilerExtensionVersion}` を撤去**: 独立プラグイン化で無効・不要
  （版数は `org.jetbrains.kotlin.plugin.compose` が一元管理）。
- `gradle/wrapper/gradle-wrapper.properties` の distributionUrl を 9.3.1 へ。CI 3ワークフロー
  （android-sdk/release-build/v6-engine-check）は wrapper でなく system Gradle を直接ダウンロードして
  PATH へ通す方式のため、各ワークフローの「Install Gradle」ステップも 9.3.1 へ同時更新（wrapper だけ
  更新しても CI には反映されない構成のため必須）。あわせて `build-tools;36.0.0` を追加インストール
  （AGP 9 の最小要求。既存の 35.0.0 は後方互換のため残置）。
- **意図的に不変**: NDK(26.1.10909125)/CMake(3.22.1) は明示固定のため AGP の既定値変更（NDK既定は
  28.2.13676358 化）の影響を受けない。Compose BOM(2024.09.02)含む依存関係バージョンは今回のスコープ外
  （Gradle/AGP/Kotlin ツールチェーンのみ）。JDK は既存の temurin 17 のままで AGP 9 の最小要求(17)を満たす。
- **検証方針（HF77非該当・ビルド基盤のみ）**: サンドボックスは Android/Kotlin コンパイル不可のため、
  この移行は CI（v6-engine-check.yml の testDebugUnitTest/assembleDebug、release-build.yml の
  assembleRelease＝ネイティブC++含む全ビルド経路）の実結果で検証する。DSL変更は理論上ハイリスクで、
  実際に初版の `android.compilerOptions` が CI 失敗で誤りと判明→上記のとおり訂正済み（この項目自体が
  その検証記録）。

## 人員不足の「なぜ埋まらないか」内訳（CoverageDiag 拡張, 3.156.0）
実機での繰り返しの「なぜ Cｵ/Cｱ が不足するのか」に**アプリ自身が答える**ため、`V6PortAnalyzer.diagnoseCoverage` の
FIXABLE(充足可能)理由を「担当可能N人・M人移せば充足」止まりから**候補の4分類**へ拡張:「移せる候補」(canDo・別シフト
希望でない・現在このシフト未在勤)を **空き番**(休/過剰から直接移せる) / **玉突き**(引くと別の covU=多人数入替が必要) /
**希望固定**(本人希望で固定) / **禁止連続**(移すと c3n) に分けて件数表示。ヒント文も分岐（空き番>0=「直し方を探す」で解消可 /
玉突き>0=ブロック移動が必要 / それ以外=希望調整か担当追加）。読取専用・スコア不変（`reason` 文字列のみ変更＝ログ/カード両方へ反映）。

## 多人数ブロック移動（勤務→勤務連鎖, 3.155.0）
実機 2026-08 データでユーザーが手作業で見つけた covU の解（8/11 モニカ B4→Cｵ／8/17 上條 Cｵ→Cｱ・山本 →Cｵ）を
最適化器が見つけられなかった。**根本原因**: 既存修復オペレータ `destroyRepairDay` は「**休→勤務**」でしか穴を
埋めず、候補が全員 (a)希望ロック (b)単一被覆シフト在勤=引き抜くと玉突き covU (c)禁止連続 に当たる局面で必要な
「**勤務→勤務**（過剰シフト/連鎖から引く）」を試さない。ランダム探索(ChainSwap/destroy-repair)は確率的にしか
踏めず、7500万反復×5並列でも 5仮説すべて必須3で同一収束していた。
- **`findCovUChain`(V6SearchOperators)**: covU セル (k0,j) を同日・多人数の玉突き連鎖で充填する交代連鎖を BFS
  （最短優先）で決定的に探索。「k0 を i が埋める→i が空けたシフト m を次が埋める→…→空けても covU が増えない
  シフト(需要0 or 余裕あり=過剰/休)で終端」。リンク条件=canDo・非wishLocked・長さ2 c3n の前後枝刈り・同一職員/
  シフト再訪なし・深さ≤5。同日内交換=被覆総量保存。返り値=適用手[(i,j,newK)]（盤面不変・採否は呼出側 keep-best）。
- **配線**: ①`rsiGenerateHypothesis` covU/c41/c41s focus の先頭で `applyCovUChains`（全 covU セルに連鎖適用・ラウンド
  better() でゲート）②optimize エピローグで covU>0 のとき keep-best 照合つきの保険パス（ALNS単独/covU非focus経路でも走る・
  ログ `ChainFill`）。スコアリング不変・退化不能（checker 照合）。ユニットテスト `ChainFillTest`（深さ1/深さ2＝8/11・8/17
  相当）で covU=0 到達・hard 非増加を検証。

## 停滞脱出の改善（進行中）
探索本体が過拘束データで空転しがちな問題（停滞脱出の質向上）。
- (2.49.0): コードレビューで判明した冗長な後処理パス(applyGroupRangePairPolish)を revert。同日2者スワップ族
  (c41/c42/c41s/c42s/c2)は既存 CyclicSwap が isBetter で total 最小化の過程で研磨済＝専用パスは冗長だった。
  探索 focus 登録(2.46)は真の取りこぼし対策なので維持。
- (2.50.0, 完了): **GLS penalty aging（減衰）**を新設（`GlsPenalty.decay(keepPercent=80)`）。GLS penalty は従来
  増える一方で長期停滞時に肥大化し（観測 36k→64k）受理バイアスが固着していた。一定 kick(`GLS_DECAY_EVERY=256`)
  ごとに penalty を 80% へ減衰し curAug を `augment(cur)` で再同期。**globalBest は生スコア管理のため解の質は退化しない**
  （探索の受理動学のみに作用）。ユニットテスト(GlsPenaltyTest: decayShrinks/decayRemoves)で減衰算術を検証。
- (2.51.0, 完了): **restart摂動の非線形スケジュール**。restart 序盤ほど大きく揺らし(多様化)終盤ほど小さく(intensify)。
  mult=0.6+1.2*(1-frac)^2。摂動のみでスコア不変・globalBest 保持＝退化なし。
- (2.52.0→**2.55.0 で revert**): 戦略的振動(λ係数オシレーション)。受理層で hard を一時割引し実行不可の壁を越える手法。
  PoC(/tmp/osc_poc.py の理想化2盆地+薄い壁)では escape 20/20 だったが、**Python等価ベンチ(tools/nsp_bench.py)で
  現実的NSPでは一貫して悪化(AUC +5%〜+15%)と実測**。理由: 現実の過拘束NSPは feasibility 到達自体が難しく「壁の向こうに
  良い実行可能盆地が無い」ため、振動は限られた予算を実行不可彷徨に浪費するだけ。**「安全(解は退化しない)」は正しいが
  「有益」ではなかった**=安全性と有益性は別物。2.54 の HF63 選択発動も同根のため一括 revert。CRINN流の実測が推論を覆した好例。
  → nonlinear_restart(2.51, 実測で僅か改善)と GLS aging(2.50, 中立・無害)は維持。
- (教訓): 探索動学の変更は **tools/nsp_bench.py の実測報酬で A/B 検証してから**採否する(PoCの理想化landscapeは現実を
  代表しないことがある)。便益が測れない/負なら入れない。
- (2.56.0, 実測結論): nsp_bench.py に **ALNS destroy-repair を追加(高忠実度化)** し再測定。**destroy-repair を入れると
  GLS aging/nonlinear restart/振動 は全て AUC 中立**(repair が支配的で feasibility に容易到達=脱出の出番が消える)。
  GLS keep%/decay_every/lambda・restart係数の**パラメータスイープも全て +0.0%=チューニング価値なし**と実測。
  低忠実度(1セルSA)での「nonlinear_restart改善」は偽信号だった。→ **脱出ヒューリスティクスへの投資は停止**。
  維持判断: GLS aging(2.50)/nonlinear restart(2.51)は中立=無害かつ proxy は短時間簡易のため実機の数百万iter regime
  での penalty 肥大防止の意義が残り得る→維持。**今後の本当のレバーは repair/destroy-repair オペレータの質 と データ側**。
- (2.57.0, 実測駆動の改善): **soft-aware destroy-repair**。`destroyRepairDayAt` は従来ランダム順で被覆穴を埋めるだけ
  (soft無視)だったのを、nsp_bench.py で測った勝ち筋(soft-aware 修復: AUC -24%〜-34%)を移植。非希望セルを休へ destroy →
  各需要を「割当の marginal soft(`staffCountPenaltyAt`=low×90/high×45/apt, Evaluator と同一式)が最小の休スタッフ」で
  repair(休→k のみ=被覆穴を新たに作らない、希望固定は保持)。**探索オペレータの変更でスコアリング不変=Δ×フル無関係**、
  受理(SA/isBetter)が最終採否=安全。脱出ヒューリスティクスが全て中立だった中で実測された唯一の品質レバー。
- (実測・不採用): `destroyRepairStaffAt`/`destroyRepairViolations` の soft-aware 化も nsp_bench.py で測ったが、
  day-repair の上に**上乗せ効果なし**。smart_staff(職員DR)は **over で −23.6%→−9.6% と一貫せず有害**、smart_cell
  (violations相当)は borderline 改善だが over/hard で同等〜微悪化＋proxy が実機(違反セル限定・低頻度)より過大で不忠実。
  → **実装しない**(有害・無効な複雑性を入れない=測定駆動3度目の「やめる」判断)。検証済みの repair レバーは day-repair のみ。
- (実データ追認, nsp_bench `--real`): golden_state(実10職員/31日/10シフト, need=shifts[k].need1, canDo=groupShift,
  staffRange 51セル, apt 23セル, use2)を忠実ロードして A/B。**soft-aware day-repair は最終soft 274→22(12×改善)**=
  実データで大幅な品質向上を確認(2.57 妥当)。ただし**AUC は +24% 悪化**(clear+soft-fill が aggressive で序盤収束が遅い)。
  **指標の教訓: AUC(速度)と final(品質)は実データで乖離。実機は5分・数百万iterの単発最適化で最終品質が成果物=final が主指標**。
  staff+viol は実データで final を 22→12 と更に改善(AUC は +40% 悪化)＝合成の「有害」と逆で、最終品質基準なら小幅改善で
  再検討余地あり(限界効用は小)。合成ベンチの AUC 結論を実データの final で見直す価値がある(脱出機構の再評価含む)。
- (網羅再分析・final品質×実データ, nsp_bench `--real`): 全機構を **final(最終品質)** で再評価(base=R=repair(day) 2.57)。
  **過去の AUC 基準判定が複数覆った**: R+viol **−22.6%**(旧「不忠実で見送り」→改善)、R+staff+viol **−49.5%**(旧「有害」→
  大幅改善・最良級)、R+staff 単独 +52.7%(有害=viol との併用が要), R+oscillation/R+gls+decay **±0%**(inert=撤去/維持で確定),
  **R+nonlinear_restart +101%(旧「中立で維持」→有害!)**。教訓: **製品は final が主指標、AUC でなく final で採否すべき**。
  → 実装方針: **staff+viol soft-aware を実装(−49.5%)、nonlinear_restart(2.51) は revert(+101%有害)**、GLS は inert で維持。
- (2.58.0, 実装完了): final品質×実データの再分析どおり実機へ反映。**`destroyRepairStaffAt`/`destroyRepairViolations` を
  soft-aware 化**(staff-DR=非希望を休へdestroy→被覆穴を marginal soft 最小で repair / violations=違反セルを old→k の
  marginal soft 最小へ再割当。共に `staffCountPenaltyAt`=Evaluator同一式)。**nonlinear_restart(2.51) を revert**(一律
  strength=0.18 へ。final +101% 有害)。全て探索オペレータ/摂動の変更でスコアリング不変=Δ×フル無関係・受理が最終採否=安全。
  これで repair 3種(day/staff/violations)が全て soft-aware に統一。GLS aging(2.50)は inert で維持。
- (2.59.0): **c41-aware day-repair**(設定画面3箇所の上限下限を全て研磨)。soft-aware repair は ①適切回数(apt) ②個人別の
  回数(staffRange low/high) を staffCountPenaltyAt で既に研磨済。3つ目 ③グループ別の回数(cons41 群レンジ)を追加: 
  destroyRepairDayAt の選択 marginal に「群の日次人数レンジ(cons41)」の delta を加味(`c41DayMarg`, grpCnt を当日分維持)。
  **`p.cons41` 空(golden_state 等)ならゼロ overhead で無害**。合成(c41あり)実測: R+staff+viol の over tier で final 47→41・
  AUC -18%→-20.6% の小幅改善・他 tier 中立。スコアリング不変=Δ×フル無関係・受理が最終採否=安全。
  → これで repair が設定3画面の上限下限(apt/個人range/群range)を全て研磨。
- (3.95.0, HARD=0非到達への配慮＝静的covU床の focus 除外): 「HARD=0 に到達しない過拘束 regime でも良い解を出すよう停滞脱出を
  改良」の指示に対し、まず nsp_bench で**真の infeasible 実験**(per-day sum(need)>S で covU 強制。合成 tight=1.0 は destroy-repair
  で hard=0 到達＝infeasible にならないため専用インスタンスを作成)を実施。結論=**動学は既に良好・パラメータ変更は不採用**:
  ①softFocusProb(床での soft 集中率, 現行 0.15)を 0.25/0.35/0.50 に上げても soft は中立〜悪化(0.35+ で +2〜3%)＝現行 0.15 が最適。
  ②targeted-perturb(infeasible 時に uniform 摂動でなく destroy-repair 摂動)は hf67HardRepair が既に摂動後の hard 床を復元済で
  **冗長**(bench 中立・同 hard 床)。③big-destroy(hard 停滞時の多日破壊)も**同 hard 床・soft は誤差内**。全変種が同一 hard 床に到達＝
  hard 最小化は既に解けており、床での soft も最適近傍。→ **測定が支持しないパラメータ/近傍変更はしない**(2.55/2.56 の教訓を再確認)。
  **採用した唯一の改善(原理ベース・3.74.0 と同方針)**: RSI focus の `avoid` に**静的 covU 床**を追加。covU は
  `structuralHardFloor`(有資格全員就けても埋まらない席=forcedCovU)が下限で最適化中に不変。covU がこの床に達したら以後 covU は
  下げられないと**静的に確定**するので、HF63 の動的検知(約3ラウンド無改善を要す)を待たず **round 0 から focus 除外**し、RSI の
  残ラウンドを解ける族(他HARD/SOFT)へ回す(旧: covU=床でも HF63 が flag するまで ~3 ラウンド無駄打ち)。`covU>=床` は恒真ゆえ
  `covU<=床`＝「これ以上不可」を正しく判定。**床=0(構造的不足なし＝HARD=0 到達可能な一般ケース)は no-op＝挙動不変**。focus 選択
  のみでスコアリング不変(keep-best=better() が結果担保)＝退化なし。covU が下限であることは定理(測定不要)。~~golden(構造的covU=2)で発火。~~
  **※[3.362.0 訂正] 現行 golden_state.json は `structuralHardFloor=0`（実測）＝この床は golden では発火しない（no-op）。当時の golden か記述が stale。機構自体は floor>0 のデータで正しく動作する。**
  ※bench は RSI focus/portfolio を模擬しないため 3.74.0 同様「実測でなく原理」で採否(no-op安全・低リスク・可逆)。
- (3.95.1, 12h見直し=敵対的レビューで判明した3.95.0の相互作用バグを修正): ①**[実バグ] N4早期脱出の常時武装化**: 3.95.0 の
  静的covU床が `avoid` を合流させたため、N4 早期脱出(`stagnantRounds>=2 && avoid.isNotEmpty()`)が構造的covU>0 のデータ
  (golden含む)で **round 0 から常時武装**し、hf63 が何も検知していなくても2停滞ラウンドで RSI が即終了＝「旧N4の厳密な部分
  集合」保証を破壊していた。`dynamicAvoid`(HF63検知のみ)を分離し N4 発火をそれでゲート(focus除外は合流 avoid のまま)。
  ②**[latent] DeltaEvaluator commit() の wStep 非対称**: 3.92.0 の isWork ハードニングが previewMove 側のみで、commit 側
  (L246)は旧式のまま＝範囲外セントネルの仮定下で preview/commit の wdCnt が乖離し得た。同一ガードへ統一(対称性回復)。
  ③covU床コメントの「covU>=床は恒真」を訂正(groupViol 混在時は下回り得るが `<=` 除外が正しい旨)。単位整合
  (breakdown["covU"]=covUCell amount 和 == structuralHardFloor 単位)・checkResultWorse 3節・relink予約・rangePen 90/45 は
  検証で健全を確認。
- (3.101.0, ログ再精査＝c3n focus の no-op 仮説を修正): 実機ログ再精査で「3実行×計10ラウンドの RSI c3n focus で c3n=1 が
  不変→HF63 が c3n を誤 infeasible 判定」を発見。原因=`rsiGenerateHypothesis` が c3n を hf67HardRepair へルーティング
  していたが、hf67 は群外修正(hf66DataHardening)・希望反映・被覆/下限充填のみで**禁止連続(c3n)には一切作用しない**＝
  c3n focus のラウンドが無変化仮説で空転していた。groupViol/pref は hf67 の作用対象なので維持し、**c3n のみ違反セルを
  直接再割当する destroyRepairViolations(else 分岐)へ変更**(c3n セルは violations マップに両端2セルで載る)。仮説は
  ラウンド単位 better() keep-best でゲート済＝退化なし・原理採否(bench は c3n 非実装=3.74.0 と同方針)。
  併せて違反詳細(buildViolationDebug)の aptLow/aptHigh 行に**目標(クランプ後)を併記**(旧: 回数/下限/上限のみで
  「回数4 下限4 上限5 がなぜ違反?」が読めなかった。目標5 が発火理由)。
- (3.102.0, 高速化・高精度化): 実機ログの定量的無駄を3点解消。①**[高精度化] 予算残の追加精製**: 後処理予約枠(budget/12,
  8〜25s)は後処理がフィックスポイント到達で大半未使用のまま返っていた(実機: 予約25s中 実使用0.45s＝約24.5s廃棄=予算の8%)。
  残5s以上＋違反残あり＋停滞早期終了でない場合、最終盤面起点の keep-best 追加精製(ALNS, runAlns入力比番兵つき=退化不能)へ
  回す(`ExtraRefine` ログ・TIME行に「追加精製」列追加)。②**[高速化] destroyRepairStaffAt の被覆事前計算**: 旧 O(T×K×S)
  ≈3100演算/呼の全職員走査を、一度数えて差分更新する O(S×T+T×K)≈620 へ(~5×減・挙動同一)。③**[高速化/零アロケ]
  op0-2 copy系パスのダブルバッファ化**: 毎反復の `cur.copy2D()` 新規確保(数百万回/実行のGC圧)を、スクラッチ盤面への
  arraycopy＋採用時スワップに置換(hf67経由の fixed!==cand 採用時はスクラッチ温存)。②③は挙動同一の純高速化＝同一seed
  同一結果、①は keep-best のみ＝スコアリング不変。
- (3.102.1, 自己監査で判明した①の回帰を修正): ExtraRefine の2回目 `optimize()` が入口で `lastAlternatives` を
  空にするため、本走行ポートフォリオの**「他の案」(最大3件)が ViewModel の captureAlternatives 前に消える**回帰。
  退避→`restoreAlternatives()`(新設・private set のため) で復元。
- (3.102.2, 敵対的監査で判明した latent 3件を修正): 並列監査(スクラッチswap/被覆事前計算/ExtraRefine/c3nルーティング/
  apt表示/hf63時計を敵対検証→**実バグ0**・挙動同一性を全項目で確認)の指摘。①ExtraRefine の上限を**後処理予約枠
  (postReserveMs, 8〜25s)でキャップ**(N4早期脱出等 stagnationFired 以外の早期復帰時に「節約した数分」を食い潰し
  電池/熱の早期終了方針と矛盾していた)。②ExtraRefine 採用時に「採用盤面の集計」行を追加(N3と同型。ログ末尾の
  UnifiedCheck/違反詳細は精製前盤面の診断のままで件数不一致に見えた)。③ViewModel HF63 の時計を callback の
  elapsed(フェーズ境界で巻き戻るローカル時計＝長フェーズ後に族が永久フラグ不能)から**startMs基準の単調壁時計**へ。
- (3.97.0, 実機ログ起因＝再最適化で550秒無駄の根本修正): 実機ログ(2026年8月データ・300s×2回)で「入力(HARD=1/195)を
  一度も上回れず内側番兵が2回とも入力へ復帰＝予算全部無駄」を確認。根本原因=**runV5 だけ退化防止番兵が無い**
  (runAlns=番兵あり・runRsi=入力比keep-best)。RSI++ は Phase1 Seed に runV5 を使うため、SA+hf67修復の劣化
  (実測195→229)が全チェーン(RSI→ALNS→Polish)へ伝播していた。①**runV5 に runAlns と同じ入力比番兵を追加**
  (better(base,result)なら入力を返す=入力が品質床・SA が良解を見つけたら素通し=多様化維持・スコアリング不変)。
  ②**ViewModel HF63 の粗サンプリング補正**(3.93.1と同クラス): 旧 `iters.toInt()`(累積数千万)では閾値5000が
  「約20ms無改善」相当＝違反>0の族ほぼ全てが即 infeasible 判定される9族ノイズ警告だった。経過時間ベース
  (elapsed/10=100単位/秒、5000=50秒無改善)へ補正し、**最終盤面で違反0の族は警告から除外**(破棄された探索トラック
  でしか違反が無かった covO/LimMax まで列挙され誤解を招いていた)。

- (3.150.0, 実機ログ定量起因＝高速化＋focus の状況適応3点): ユーザー指示「1.高速化 2.停滞脱出を状況に応じて賢く」。
  実機ログ2本(10/12シフト, 300s AUTO)の定量分析: **HF80 PostPolish が 45s枠を走り切り改善0（40.977s/40.988s の2例）**、
  **RSI round=4 が focus=groupViol(件数0)の空振り~21s**、**c3n focus ×3R 連発(計~63s)で件数不変**(HF63 恒久判定は約3R要)。
  ①**[E8] 件数0の族を focus しない**: `maxViolatedFamily` SOFT フォールバックの `bestCount=-1`→`0`（旧: 非avoid正件数族が
  order に無いと先頭 groupViol=0 が当選→hf67 のクリーン盤面 no-op 仮説で1R空振り）。該当なしは "total"=全違反セル hint の
  汎用修復(destroyRepairViolations は focus 非依存・空ヒントは randomAllowedCell)。②**[E9] 空振り focus の1R冷却**: 候補
  不採用＋focus族件数不変の「完全空振り」ラウンド直後だけ同 focus を回避(c3n→c1→c3n…交互へ多様化)。進展あり/族件数が
  減った(方向有望)場合は冷却しない。恒久除外は従来どおり HF63 のみ・N4 発火条件(dynamicAvoid)には混ぜない。
  ③**[E10] hf80PostPolish の停滞早期終了**: best が枠の1/5(下限3s)無改善で早期 return(ログに「停滞早期終了」併記)。
  keep-best＋入力比番兵で品質不変＝時間/電池のみ節約(2.65.0/2.67.0 と同方針)。3点とも focus/時間配分のみ＝スコアリング
  不変・keep-best 維持（bench は RSI focus/polish を模擬できないため 3.74.0/3.95.0 と同じ原理採否）。
- (3.106.0, 外部レビュー4件の修正): 提示されたコードレビュー(対象7b22a50)の4件を全て検証→実在確認→修正。
  ①**[P1] 休シフト削除で休日が勤務化**: removeShift が削除セルをハードコード0へ写像(休が index0 でない/休より
  前を消すと勤務へ化ける)。削除セル→**削除後の休index**へ追従＋**休シフト自体の削除は禁止**(no-op、ViewModel が
  理由メッセージ提示)。②**[P1] editStaff が skillIdx を0へ戻す**: `Staff(name,gi)` 再構築→ `copy(name,groupIdx)`
  で保持(名前編集だけで cons41s/c42s 評価が変わっていた)。③**[P1] 重複記号/氏名の解決不一致**: CSV照合の
  associateBy/後勝ちループ(9箇所)を **firstWinsMap(先勝ち=Problem.indexOfFirst と同一解決)** へ統一＋
  **検査8(重複定義の事前診断)**を新設(職員名(空白無視)/シフト/グループ/スキル群記号。read-only・非ブロック=
  既存データは開ける)。④**[P2] bg再開で計算条件が化ける**: 予算秒数/並列数を WorkManager inputData に永続化
  (旧: インメモリのみで kill 後は既定60s/4並列)＋kill後の復元は**途中最良スナップショット優先**(8秒毎退避済み。
  旧: 常に元入力から再スタート)。SessionRegressionTest に removeShift/editStaff の回帰テスト追加。

## Android 17 会話バブル対応（3.173.0）
ユーザー指示「アンドロイド17のバブル対応をコードでする」。バックグラウンド最適化の**進捗と完了を会話バブル**
（他アプリの上に浮かぶ小窓）として提示する。grilling で用途（進捗＋完了の両方）と SDK 方針（compileSdk/targetSdk を
37 へ）を確定。**表示専用・スコアリング不変**（最適化器/チェッカー/重みには一切触れない・HF77 非該当）。
- **成立要件（Bubbles API は Android 11/API30+。minSdk 36 で常時可）**: ①会話チャンネル（`setAllowBubbles(true)`）
  ②長寿命の会話ショートカット（`ShortcutInfoCompat.setLongLived(true)`＋`Person`）③`MessagingStyle` 通知＋
  `BubbleMetadata`（`setShortcutId` で②に紐付け）④埋め込み可能な専用Activity（manifest で `allowEmbedded`／
  `resizeableActivity`／`documentLaunchMode="always"`）。この4点が揃わないと `BubbleMetadata` を付けても通知は
  バブル化されない。
- **新規**: `work/BubbleSupport.kt`（①②③のビルダ＋`postProgress`/`postDone`/`clear`。冪等・`runCatching` で通知
  失敗を握り本体継続）／`work/BubbleActivity.kt`（④＝`OptimizationRepository` の running/progress/result フローを
  購読する読取専用 Compose 画面）。会話チャンネル `magi_optimize_bubble`・ショートカット `magi_optimize_conversation`・
  通知ID `NID_BUBBLE=4103`。
- **配線**（`OptimizationWorker`）: FGS 進捗通知（`NID_PROGRESS`）は FGS 要件のため**別に維持**し、バブルは会話
  チャンネルの別通知として扱う。開始時に channel/shortcut を用意し開始バブルを提示、進捗コールバックで ~1.5秒間引き
  ＋`onlyAlertOnce` 静音更新、完了/失敗で `postDone`。
- **SDK（CI 実測で確定）**: 当初 `compileSdk/targetSdk 36→37`＋CI sdkmanager へ `platforms;android-37` を追加したが、
  Release Build（run 29385635188）が **`Failed to find package 'platforms;android-37'`** で SDK インストール段落ち
  （API 37=Android 17 の platform SDK は 2026-07 時点で sdkmanager 未提供）。**compileSdk/targetSdk は 36 のまま**へ戻し
  CI 変更も撤回した。Bubbles は API30+＝minSdk 36 でバブル機能は完全動作するため実害なし。API 37 SDK 公開後に
  36→37＋CI sdkmanager 行へ `platforms;android-37`・`build-tools;37.0.0` を足せば「Android 17 でコンパイル」へ移行可能。
  検証は CI（Android コンパイル不可のサンドボックスのため。タグ push は org egress ポリシーで 403 のため
  Release Build を workflow_dispatch でブランチ上に起動）。

## ネイティブパリティのCI自動化（backlog#6 解消, 3.178.0）
ユーザー指示「C++パリティ作業＝ハーネスをCI配線」。**最重要の事実確認**: 別セッションの混入で見えた「マスク最適化
#1〜#4」はどのブランチ・PR・全履歴にもコミットされておらず（`git log --all -S canDoMask/buildMasks` で0件）、
復元対象は存在しない。かつ #1「c3がスコアを変える」はパリティ原則（mask==scalar はスコア不変）に反する内部矛盾で、
c3窓は既に 3.172.0/3.174.0 で delta 経路にビット化済み＝再実装は冗長/危険。よってマスク実装は行わず、実体のある
backlog#6（C++評価器のパリティ自動テスト無し）を解消する方向に確定（AskUserQuestion）。
- **配線**: `.github/workflows/native-parity.yml` を新設。`host_parity_bench.cpp` を `g++ -O3 -std=c++17
  -DMAGI_HOST_TEST -I app/src/main/cpp` でビルドし実行、`main()` が mismatch>0 で 1 を返す＝ステップ失敗。
  トリガ= pull_request→main / push→main / workflow_dispatch。**g++ のみで数十秒**＝Android SDK/NDK 不要のため
  SDK ジョブ（v6-engine-check 等）と分離した専用ワークフロー。失敗時のみログを短期保持でアップロード。
- **検証**: サンドボックスで実ビルド・実行し **1,498,930手・mismatch=0・EXIT=0**（bit-op は scalar 比 2.21x）を
  確認してから配線（提示物を信用せず独立再現＝規律どおり）。
- **効果**: 以後 Evaluator.kt/MirrorCore/DeltaEvaluator を変えて magi_native.cpp を変え忘れる意味的乖離
  （実機で番兵発火→ネイティブ黙殺＝速度退行）が CI で自動検出される。エンジン本体・スコアは一切変更なし
  （CI＋docs のみ）。残: harness fixture は合成（S<=64/T<=64）で実データ形状網羅は将来課題。

## バックログB/C の消化（番兵対称化・停滞時計・デッドコード除去, 3.179.0）
ユーザー指示「b,c」（バックログBと報告のみCの項目を消化）。仕分けて**安全・well-defined な3件を実装**、
**判断/計測が要る1件は据え置き理由を確定**、**データ側/保留の2件は非コード**と整理。
- **[B#8] SAチャンク番兵の対称化**: `runSaChunk` 末尾を `full != curVal` → `full != curVal || curVal != st.score`
  へ（LAHC/ALNS/Polish の3ランナーと同型）。受理時 curVal=st.score・revert で復元のため通常は恒真＝挙動不変、
  不整合時のみ status=1 で Kotlin 退化。ホスト parity harness で compile+150万手 mismatch=0 確認。
- **[C③] hf80PostPolish の停滞時計引き継ぎ**: native 経路が番兵発火で未完了に戻り、かつ native 区間で無改善
  (`nat.best==null`)なら、Kotlin ループの `lastImproveMs` 起点を `started` にして native 無改善経過を停滞判定へ
  算入（旧: 常に nowMs() で再スタートし native の無改善時間が抜け落ち、さらに約 stallMs 余計に回っていた）。
  改善済みは最終改善時刻不明のため保守的に nowMs()。tail の keep-best ガード（`better(baseReport,bestReport)`）
  で早期 exit しても品質不変。異常系のみ発現・電池/時間の節約が正しく効く。
- **[C④] nativeAlnsSetCur デッドコード除去**: JNI 定義（magi_native.cpp）＋ NativeBridge の external 宣言を削除
  （grep で呼出0を確認）。`resetBoard` は無害な未使用メンバとして残置＝最小変更。parity harness で compile+
  mismatch=0 確認。
- ~~**[C① 据え置き確定]**: c1「壁」判定の need2 依存。実コード精読で、供給を素朴に上げると 3.76.0 の検証済み
  「真の壁」Dﾃ≥2/14日 を false negative 化すると判明。covO×c1 トレードオフの正しいモデル化＋golden_state 計測が
  必須の設計変更＝盲目的修正は有害。据え置き継続（別途 grilling で詰める項目）。~~ **→ 3.364.0 で修正**（golden_state を
  実測し「Dﾃ は物理供給248>>需要32・手作り盤面は既に35回配置＝真の壁でなく false wall」と判明。3.179.0 の前提を反証。
  休のみ真の壁を維持し非休は covO-tension として正直化。詳細は「c1『壁』判定の need2 依存を…」節）。
- **[B#4 非コード]** cons3n データ重複（Dﾃ→A4 が2行）は二重計上だが最適化器/チェッカーで一貫。SettingIssue が
  dedup を提案済み＝データ側修正の対象。**[B#5 非コード]** E5「月全体の俯瞰」はユーザーの明示 go まで保留（決定記録）。
- 検証: C++ は host parity harness で compile+mismatch=0（bit-op 1.95x）。Kotlin はブレース均衡確認＋CI
  （v6-engine-check/Release Build）。スコアリング不変（番兵の締め・停滞時計・デッドコード除去のみ）。

## 希望/必要人数カレンダーのレイアウト刷新＋未設定導線（3.180.0）
ユーザー提示スクショ（現行 HEAD に存在しない別バージョンの2画面）を**目標デザインとして取り入れ**る指示。
grilling で4点確定（静的月見出し=D6維持／その他=担当可能シフト主＋全シフト／未設定に戻すをシート追加／検証＋不足修正）。
**表示・導線のみ・スコアリング不変**（wishes/needDay モデル・pref/covU エンジンは不変）。
- **共有部品**（`NeedDayEditor.kt` に internal 定義、`WishEditor.kt` から共用）: `monthLabel`/`dayChipLabel`（java.time で
  月跨ぎも正しく「M/D(曜)」）・`MonthHeaderStatic`（‹ 2025年6月 ›＝矢印は淡色の飾り＝**月送りなし**、D6決定=1state=1か月）・
  `SelectorField`（ラベル＋値＋▼のドロップダウン風アンカー）・`CountPill`・`MultiSelectOpener`（「複数日選択 ・ N日選択中 ›」）。
- **希望シフト登録（WishCard 全面刷新）**: 職員=**ドロップダウン**＋「全職員を見る（N名）」トグル（登録済み一覧を開閉）。
  日タップ複数選択→`MultiSelectOpener`→**モーダルボトムシート**（`WishApplySheet`）: 日付リスト・**担当可能シフトを主ボタン
  ＋「その他（担当外シフト）」で全シフト**（担当外は赤枠⚠・背景=シフト表示色）・**「未設定に戻す」**（`clearWishesForDays`）＋
  「N日に適用」（`setWishesForDays`＝単一undo）。旧インラインパネルを置換。
- **必要人数カレンダー（NeedCalendarCard 全面刷新）**: シフト=**ドロップダウン**＋**「基本設定」コンパクトカード**（タップで
  `BaseNeedSheet`＝既定 need1/need2 を編集）。日タップ複数選択→**モーダルボトムシート**（`NeedApplySheet`）: 日付リスト・
  最低/上限ステッパー・**「未設定に戻す」**（`removeNeedDay`＝例外削除で既定へ）＋「N日に適用」（`setNeedDay`）。
  日セルの充足色分け（緑/橙/赤/灰）は現行踏襲。
- **タスク2（下流→上流の導線）**: 違反→`tab=2`（編集）で月次条件の両カレンダーに到達・`openWish`→`editScope=0` の粗い経路は
  成立を確認。精密なディープリンク（特定 pref/covU 違反→該当職員/シフトを事前選択）は WishCard/NeedCalendarCard の
  staff/shift 状態を hoist する要ありで、別途着手（未実装＝バックログ）。
- **タスク3（未設定手順）**: カレンダーのボトムシートに「未設定に戻す」を新設し、上流編集画面だけで未設定化が完結。
  従来の全件一覧（登録済み希望／日別例外）の×削除も温存。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース均衡・重複定義0・呼び出し側シグネチャ一致・既存 VM API のみ使用を
  静的確認。最終判定は CI（Release Build＝assembleRelease）。

## 必要人数設定を「4つの情報」に集約（3.186.0）
ユーザー指示「情報を4つに絞ります」。必要人数画面を勤務作成者が知りたい**4点だけ**に集約（`NeedCalendarCard`＝`NeedDayEditor.kt`）:
①どのシフトか（見出し行のドロップダウン）②各日の最低–最高（カレンダー）③どの日を選んでいるか（枠＋✓）
④選択日に何人を適用するか（下部のインライン一括パネル）。**表示・導線のみ・スコアリング不変**（needDay モデル/covU エンジンは不変）。
- **撤去**: 長い説明文／独立3カード（3.180.0 の「シフト」「基本設定」「複数日選択」）→ 見出し1行に統合（`[休 ▼] 標準 N人`。
  標準タップで `BaseNeedSheet`＝基本 need1/need2 編集を温存）／「設定済N日・未設定M日」凡例／**充足色ドット(covU/covO 緑橙赤)＋色凡例**
  （3.167.0 で入れた実充足の色分けを本画面=**設定**からは撤去。充足は勤務表グリッド/集計で見る）。
- **カレンダー表示（色でなく形と文字で区別）**: 未設定=「—」(淡色) / 標準どおり=通常文字 `1–2` / **個別設定(日別例外)=太字＋小さな印**
  （`vm.needDayOverrides()` の当該シフト日を太字＋brand小点）/ 選択中=枠＋✓。土日は文字色のみ（従来どおり）。
- **④インライン一括パネル**（`NeedApplyPanel`, 1日以上選択時のみカレンダー直下に表示）: モーダルで隠さない＝カレンダーを見ながら
  追加選択・適用できる（3.180.0 の `NeedApplySheet` モーダル＋`MultiSelectOpener` カードを置換）。`N日選択中`＋日付（**多いと
  「6/3、6/8、6/17、ほか2日」省略**）＋最低/上限ステッパー＋`キャンセル`｜`N日に適用`＋従属の`選択した日を未設定に戻す`。
  **入力エラー(最低>最高)=ステッパー赤枠＋「最低は最高以下に」で適用不可**（spec の「赤枠」実装）。
- 判断: 標準の編集入口（`標準 N人` タップ）と `未設定に戻す`（未設定状態への唯一の到達手段・3.180.0 の明示機能）は**温存**
  （spec のモック非掲載だが機能保全）。`NeedApplySheet`/`LegendDot` は本画面専用で未使用化→削除。共有部品（`SelectorField`/
  `MultiSelectOpener`/`CountPill`）は `WishCard` が使用中のため残置。希望シフト画面(`WishCard`)は本指示のスコープ外＝現状維持。
  **[3.187.0で訂正]** 「希望シフト画面はスコープ外」はユーザーが直後に同画面のスクショを共有し「同じ4情報の原則を適用してほしい」
  と明示したため撤回。`MultiSelectOpener` も両画面がインラインパネル化した結果、呼出0の死蔵コードとなり 3.187.0 で削除済み。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・削除シンボル参照0・呼び出し側シグネチャ一致を静的確認。
  最終判定は CI（Release Build＝assembleRelease）。

## 希望シフト登録も「4つの情報」に集約（3.187.0）
ユーザーが希望シフト登録画面のスクショを共有し、AskUserQuestion で確認したところ「必要人数設定(3.186.0)と同じ4情報の
原則を適用してほしい」と回答。`WishCard`（`WishEditor.kt`）へ同一方針を適用。**表示・導線のみ・スコアリング不変**
（wishes モデル/pref エンジンは不変）。
- **4点への写像**: ①どの職員か（見出し行のドロップダウン）②各日の登録済み希望（カレンダーのシフト表示色チップ）
  ③どの日を選んでいるか（枠＋✓）④選択日にどのシフトを適用するか（下部のインライン一括パネル`WishApplyPanel`、モーダルでない）。
- **撤去**: 「設定日数N日・シフト別内訳」の常時表示テキスト／「希望シフトは1日につき1つのみ登録できます」等の説明文／
  `WishApplySheet`（モーダルボトムシート）を `NeedApplyPanel` と同型のインラインパネルへ置換（1日以上選択時のみカレンダー直下に
  表示・キャンセル｜N日に適用・従属の「選択した日を未設定に戻す」・日付は多いと「6/3、6/8、6/17、ほか2日」と省略）。
- **判断（spec 非掲載だが機能保全）**: 「全職員を見る」（確認・削除専用の一覧、カレンダーが1職員ずつしか見えない弱点を補う）は
  必要人数設定の「標準N人タップ」と同様、常時は表示しないが到達可能な副次機能として温存。ボタンから小さな文字リンクへ格下げし
  常時表示の面積を縮小。担当外シフトの⚠警告文は安全情報のため維持。
- **デッドコード除去**: `MultiSelectOpener`（`NeedDayEditor.kt`）は両画面のモーダル→インラインパネル化で呼出0になったため削除。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・削除シンボル参照0・呼び出し側シグネチャ一致を静的確認。
  最終判定は CI（Release Build＝assembleRelease）。

## 設定タブ「最適化設定」のオプション集約（3.188.0）
ユーザー指示「オプションをまとめます」（設定タブのスクショ共有）。AskUserQuestionで方針確認: **主要項目のみ
`SettingsCard`に常時表示し、技術系チューニングは既存の「詳細設定（上級者向け）」（`AdvancedSettingsSection`）へ移動**
（推奨案採用）。**表示・導線のみ・スコアリング不変**（各設定値のモデル/エンジンは不変、置き場所のみ変更）。
- **`SettingsCard`に残置（毎回の判断材料）**: 計算の制限時間（stepper）・計算方式（おまかせ/高速/破壊再構築/…の
  セグメント）・バックグラウンドでつくるボタン・バージョン表示。
- **`AdvancedSettingsSection`へ移動（一般の運用では触らない内部チューニング）**: 並列ワーカー・ネイティブ加速（C++）・
  Kotlin照合・仕上げ最適化。新設 `OptimizationTuningSection(ui, vm)` にまとめて実装し、既存の `LogsCard` の直前に配置
  （詳細設定は元々「ログのみ」だったため、見出し直下の説明文も「並列数・ネイティブ加速・仕上げ最適化の調整とログの確認。
  一般の運用では触りません。」へ更新）。
- 既存IA（`AdvancedSettingsSection`＝色設定等で確立済みの「詳細設定（折りたたみ・既定閉）」パターン）への合流のため
  新規UIコンポーネントは追加していない。`AdvancedSettingsSection`のシグネチャに`vm: MagiViewModel`を追加（呼出元は
  `MagiApp.kt`の1箇所のみ・非破壊）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・呼び出し側シグネチャ一致を静的確認。
  最終判定は CI（Release Build＝assembleRelease）。

## 「③ 回数（1人あたり）」+/- が同一画面で反映されない実機バグ修正（3.189.0）
ユーザー実機報告「必要人数でプラスとマイナスのボタンで人数が変更できない」→ 詳細確認の結果、対象は
年間マスター「③ 回数（1人あたり）」の `AptCard`（目標=適切回数）。3.185.0（editRev再構成保証）を含む
最新ビルド(3.187.0)でも再現することをユーザーが確認。**追加調査で確定**: +/-を押すたびにログの「違反チェック」が
記録される＝データ(`state.groupShiftApt`)は毎回正しく更新されている。かつ**「他のタブへ切替→編集タブへ戻る」と
正しい数字になる**ことをユーザーが確認＝**保存は正常、同一画面内の再描画だけが遅れる**（表示専用の不具合）と
断定。
- **原因（推定・最有力）**: `AptCard`/`StaffRangeCard`/`GroupRangeCard` は `CollapsibleSection("③ 回数（1人あたり）",
  "yr_count") { ... }` の**content ラムダ内**から呼ばれている。このラムダは `ui`(安定/データクラス)と
  `vm`(可変var保持のためComposeが不安定と推論)の両方を捕捉するため、`CollapsibleSection`呼び出し自体の
  スキップ判定に絡み、`editRev` が変化しても content ラムダの再実行＝子カードの再構成が確実に伝播しない
  ケースがあると判断（タブ切替・復帰でツリーが丸ごと再構築されると正しい値が読めることと整合）。
- **対応**: `MagiApp.kt` の呼び出し側で `key(ui.editRev) { AptCard(ui, vm) }` のように**3カードそれぞれを
  `key()` で包む**。`editRev` が増えるたびに Compose がこの3つを確実に作り直す＝タブ往復と同じ効果を
  同一画面内で強制する。ローカルUI状態（開いているダイアログ等）は編集直後にのみ破棄されるが、通常は
  編集操作の瞬間にダイアログは閉じているため実害なし。**表示のみ・スコアリング不変**。
- **未対応（同種の懸念・要watch）**: 同じ `CollapsibleSection` パターンを使う他カード（`Ws1Card`・
  `SkillGroupCard`・「④ 人数と組み合わせ」の群レンジ/ペア禁止カード）も理論上同じ再描画遅延を持ちうるが、
  今回は**ユーザーが実際に確認した範囲（③のみ）に絞って対応**（未確認箇所への予防的変更は見送り。同様の
  報告があれば同じ `key(ui.editRev)` パターンを適用する）。`WishCard`/`NeedCalendarCard`（3.186-3.188で新設、
  月次条件タブ）は `CollapsibleSection` に包まれておらず該当しないため対象外。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0を静的確認。最終判定は CI
  （Release Build＝assembleRelease）＋ユーザーの実機再確認（同一画面での即時反映）。

## 同種の再構成バグの横展開（3.190.0, ユーザー指示「他の画面も再検索してください」）
3.189.0 の「未対応（同種の懸念）」を実際に洗い出し、対象範囲を確定して修正。`CollapsibleSection`（generic な
`content: @Composable () -> Unit` を受け取る再利用コンポーネント）配下で、**生の `vm.xxx()` 読取値を即座に
表示し、ローカルの `remember` バッファを経由せず直接コミットする**箇所を全カード横断で洗い出した:
- **同一パターンで確認・修正**: `Ws1Card`（`use2`トグル・担当可否chipマトリクス＝`v.groupShift`直読み）、
  `SkillGroupCard`（職員のスキル割当ボタンのラベル＝`st.skillIdx`直読み）、`ConstraintsCard`/`SkillConstraintsCard`
  （行タップ編集後の一覧テキスト＝`vm.constraintFamilies()`直読み）、`StaffManageCard`（スキル割当ボタン）、
  `StaffRangeCard`（職員管理タブ側の呼び出し。年間マスター側は3.189.0で対応済）。いずれも `key(ui.editRev){ ... }`
  で個別に包んだ（`CollapsibleSection`配下でない`StaffManageCard`/`StaffRangeCard`(職員管理タブ)にも同型の懸念が
  あるため予防的に適用）。
- **確認したうえで対象外**: `WishCard`/`NeedCalendarCard`（月次条件タブ）。この2枚は「選択中の職員/シフト
  （`i`/`k`、`remember`）を保持したまま複数日を編集する」設計のため、`key(ui.editRev)`で包むと**自分自身の編集
  コミット（それ自体がeditRevを増やす）のたびに選択がリセットされ、③より悪い退行を生む**と判断し見送った
  （AptCard等は「開いているダイアログ」程度の軽いローカル状態しか持たないため無害だったが、この2枚は主要な
  ナビゲーション状態を持つため事情が異なる）。
- 各カードの内部状態を精査し、`key()`で包んでも失われるのは「編集操作自体が同じフレームで既に閉じている
  ダイアログ/ドロップダウン」の初期値（null/false）のみであることを確認（＝退行なし）。
  グリッド編集(ScheduleGrid/ShiftPickerSheet)は別の確立された経路で同種の報告が過去に無いため対象外のまま。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0を静的確認。最終判定は CI
  （Release Build＝assembleRelease）＋ユーザーの実機再確認。

## 設定タブの用語重複解消：「並列ワーカー」と「並列(複数案)」（3.191.0）
ユーザー指摘「設定画面の内容が重複している」→詳細確認「項目のオブジェクトの意味が重複している」。
設定タブの「最適化設定」内 **並列ワーカー（同時に計算する数、`ui.workers`）** と **計算方式の選択肢の一つ
「並列(複数案)」（`V6Algorithm.PORTFOLIO`のラベル）** が、どちらも「並列」を冠していて同じ設定に見える
との指摘。コード確認で実態を特定:
- `V6NativeOptimizer.runV6FullOptimize` の `val w = options.workers.coerceIn(1, 5)` が
  ALNS/RSI/RSI_PLUS/**PORTFOLIO** 全てで共通の並列度（`runMultiWorker`が起動する仮説の本数）に使われる。
  つまり「並列ワーカー」は**全アルゴリズム共通の並列実行数ダイヤル**。
- 対して PORTFOLIO は `portfolioAlgoFor(i)` で **workers本の各仮説に異なるアルゴリズム（ALNS/RSI/RSI++）を
  割り当てる**戦略（協力ポートフォリオ）＝並列度そのものではなく「複数の方式を組み合わせる」という別の軸。
- 「並列ワーカー」が並列度、PORTFOLIO ラベルも「並列」＝**同じ語で2つの異なる概念を指しており紛らわしい**、
  という指摘は正確だった。
- **対応**: `v6AlgorithmLabel`（`MagiSetupCards.kt`）の `V6Algorithm.PORTFOLIO` ラベルを
  **「並列(複数案)」→「方式ミックス」**に改称（他に参照箇所なし・文字列のみ）。「並列」は並列ワーカー設定に
  一本化し、PORTFOLIOは「複数の計算方式を組み合わせる」という実態を表す語に。ロジック・エンジン・重みは不変。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・他参照箇所なしを grep で確認。
  最終判定は CI（Release Build＝assembleRelease）。

## 設定画面のテキストアート再現＋「おまかせ」解決先の表示（3.192.0）
ユーザー指示「設定画面をテキストアートで再現して、冗長性をあなたが検証する」。実コードから設定タブ全体
（外観/シフトの表示色/違反種別の色/データ/最適化設定/重み表/詳細設定）をテキストアートで再現し、1件ずつ
突き合わせて監査。**①既に3.191.0で対応済みの「並列ワーカー×並列(複数案)」を確認**。②「ネイティブ加速×
Kotlin照合」「データを保存×JSON出力」「問題がないか調べる×バックグラウンドでつくる」は語の重複はあるが
説明文・見出し文脈で意味が分離済みと判断し対応不要。③**新たに発見**: 「計算方式: おまかせ」選択中は
実際に動くアルゴリズムが `V6FinalPort.optimizationPlan`（計算の制限時間で自動決定: ≤30秒→V5・≤210秒→
RSIThenALNS・それ超→RSIPlus）で決まるが、画面には「おまかせ」としか出ず今の時間設定で何が動くか見えない
＝**意味の重複ではなく情報の欠落**として報告し、ユーザー指示「修正する」を受け対応。
- **対応**: `SettingsCard`（`MagiSetupCards.kt`）の「計算方式」表示直下に、`ui.v6Algorithm==AUTO` のときだけ
  `V6FinalPort.getAlgorithmLabel(ui.budgetSec)`（`handleOptimize` が実際に使う解決ロジックと同一関数）の
  結果を「→ 今の設定(N秒)では 🧬 学習+研磨（RSI違反集中→ALNS研磨）が動きます」の形で併記。予算秒数を
  変えるたびに表示も追従（`ui.budgetSec` 直読み・既存の再構成経路のみで足り新規のkey()等は不要）。
  読取専用の表示追加のみ・スコアリング/ロジック不変。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・`AlgorithmLabel` フィールド名一致を
  静的確認。最終判定は CI（Release Build＝assembleRelease）。

## 勤務表タブの「職員別カレンダー」撤去（3.193.0）
ユーザーが勤務表タブのスクリーンショットを共有（「既存の画面を添付の画面に更新したい」）。実装コードと
1行ずつ突き合わせた結果、「希望シフトを反映」「違反フィルタ（種別）」「検索・凡例」等は**既に完全一致**して
おり、明確な差分は**画像に「職員別カレンダー」の折りたたみ行が無い**ことだけだった。確証が持てず
AskUserQuestionで確認したところユーザー回答「シンプル」＝簡素化の意図と判断。
- **対応**: `StaffCalendarCard`（`MagiScheduleViews.kt`）を`MagiApp.kt`の勤務表タブ呼出から撤去し、
  未使用となった `StaffCalendarCard`/`CalendarCell` 定義・専用ヘルパー `Int.floorMod`（同ファイル内、
  他に呼出なしを確認）を削除。既存コメント自体が「勤務表グリッド(全職員)と盤面ビューが二重化＝タブの
  密度/冗長の主因」と自認していた要素で、撤去はスクリーンショットの構成と整合する。
- 呼出は`MagiApp.kt`の1箇所のみだったことを確認済み（他画面からの参照なし）。表示のみ・スコアリング不変。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・削除シンボルの残存参照0・
  他ヘルパー(mutableIntStateOf/drawBehind/Path/Stroke/isHardCellViolation/isHeavySoftCellViolation/
  violationBorder)が他箇所で使用中で import 削除不要なことを静的確認。最終判定は CI（Release Build）。

## 勤務表タブ「不一致だけ抽出」の撤去＋希望バッジのcanDo不整合修正（3.194.0）
ユーザー指示「情報の冗長性を検証する」を受けた勤務表タブ全カードの再監査。git履歴で`MismatchExtractCard`
（不一致だけ抽出）が2026-07-04導入の初期機能＝`TallyCard`のバッジ表示(3.99.0系)や`ScheduleGrid`の希望バッジ
(3.113.0)より**前**にできたものと判明。中身を突き合わせると3セクションとも他の表示に吸収されていた:
- 「人数の過不足」＝`TallyCard`(日別)の▼▲バッジ・`ScheduleGrid`冒頭の人員不足バナーと重複。
- 「適切回数の範囲外」＝`TallyCard`(職員別)の▼▲バッジと重複。**しかも`vio-low`/`vio-high`しか見ておらず
  `vio-aptLow`/`vio-aptHigh`(apt由来)を含まない**＝`TallyCard`より情報が古く不完全と判明。
- 「希望シフト未反映」＝`ScheduleGrid`セルの桃バッジ（`MagiFlatGrid`の`wishKind`）と重複。
`BottleneckCard`が`AttentionCardsSection`に吸収され撤去された前例(3.81.0/3.103.1)と同型と判断し撤去。
- **[より深刻な発見] 希望バッジとチェッカーの`pref`判定が別々の実装で食い違っていた**: `MirrorCore.kt`の
  `pref`(HARD違反)計算は`w in 0..K && p.canDo(i,w) && s[i][j]!=w`＝**canDo(実現可能)な希望の未充足のみ**を
  数える（コード注記「担当不可の不可能希望は充足しようがなく『配布可(HARD=0)』を恒久不能にしていたため
  計数から対称除外する」）。しかし`MagiFlatGrid`の桃バッジ`wishKind`と、今回撤去した`MismatchExtractCard`の
  「希望シフト未反映」リストは**どちらも`canDo`を見ずに`wish!=schedule`のみ比較**しており、実現不可能な希望
  （担当できないシフトへの希望）まで「未反映＝直せる」であるかのように表示していた。実現不可能な希望は
  別途「⚠ 実現できない希望が${N}件」（`ui.impossibleWishCount`、ホーム/ダッシュボード）で案内される設計
  のため、勤務表タブのバッジがそれと矛盾するメッセージを出し、タップで修正しようとしても改善案が
  見つからない体験を生んでいた。
- **対応**: `MagiFlatGrid`に`canDo: (Int,Int)->Boolean = {_,_->true}`パラメータを新設し、`wishKind`の
  判定に`!canDo(i,wk)`のとき0(バッジ無し)を返すガードを追加＝チェッカーの`pref`除外ロジックと意味を一致
  させた。`ScheduleGrid`は既存の`canDo`パラメータをそのまま`MagiFlatGrid`へ渡すだけ（呼出元は1箇所のみ
  ＝`MagiApp.kt`、新規パラメータはデフォルト値付きのため非破壊）。`MismatchExtractCard`本体と呼出（1箇所）
  を削除（`dayMD`ヘルパーは`ConfirmListCard`等で使用中のため残置）。
- 表示のみ・スコアリング不変（チェッカーの`pref`計算自体は変更なし、UI側の独立判定をチェッカーの意味論に
  合わせただけ）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・削除シンボルの残存参照0・
  `MagiFlatGrid`呼出1箇所のシグネチャ一致を静的確認。最終判定は CI（Release Build）。

## ホーム〜設定タブの冗長性一巡監査（3.195.0）
ユーザー指示「ホームから設定画面までのすべての画面での冗長性を見直す」。5タブ（ホーム/勤務表/編集/分析/設定）を
一巡監査（勤務表/設定タブは既存セクションで対応済）。新たに2件を確認し修正（表示のみ・スコアリング不変）。
- **[実装] 編集タブ「SetupGuideCard」と「MonthlyChecklistCard」の行重複**: 編集タブは常時表示の
  `SetupGuideCard`（「初期設定の手順」）→segmented control→（editScope==0時）`MonthlyChecklistCard`
  （「今月の作成条件」）の順で並ぶが、両者は「希望シフト件数」「必要人数の例外」を別々の行コンポーネント
  （`GuideRow`/`ChecklistRow`）でほぼ同じ内容を直列2回表示していた（`MonthlyChecklistCard`側がより詳しい
  ＝件数比・標準/例外の区別・作成ボタン付き＝3.114.0で後発）。`SetupGuideCard`に`editScope: Int = -1`
  パラメータを新設し、editScope==0（月次条件、既定タブ）のときだけ「月次条件（毎月）」セクション（希望
  シフト/必要人数の例外の2行）を非表示にし、直下でより詳しい`MonthlyChecklistCard`に一本化。職員管理/年間
  マスター側（editScope!=0）では引き続き表示し、希望シフトへのショートカット（`onOpenWish`）を維持
  （常時表示だった見つけやすさ改善=3.164.0を後退させない）。呼出は`MagiApp.kt`1箇所のみ
  （`SetupGuideCard(ui, vm, editScope = editScope, onOpenWish = openWish)`）。
- **[実装] ホームタブ実行中に進捗テキストが2回連続表示**: 最適化実行中、`OperatorNextActionCard`が
  進捗行（`progressSummary(ui)`＝改善率・残り時間・回数）を表示した直後に`LiveScheduleCard`が続けて
  表示されるが、`LiveScheduleCard`の先頭行も同じ`progressSummary(ui)`を再度描画しており、全く同じ文字列が
  直列2回並んでいた（`OperatorNextActionCard`側は見出し文を「進捗行と重複するため」既に空にした経緯が
  あったが、直後のカードとの重複には未対応だった）。`LiveScheduleCard`冒頭の重複`Text`行を削除
  （残りの「途中経過を見る」トグル・変化セル表示は不変）。
- **[軽微・コメント訂正]** `MagiApp.kt`の分析タブ内コメントが「V6DashboardCard(1ヶ月俯瞰・生指標)は詳細設定
  (上級者)へ移設」と書かれていたが、実装は既に分析タブ内（proMode時）に一本化済み（`MagiSetupCards.kt`の
  「冗長性J1」コメント参照）で実態と食い違っていた（HF77: コメント≠実装）。表記のみ訂正。
- **見直したが対応不要と判断**: `CoverageDiagnosisCard`(ホーム)/`ConfirmListCard`(分析)/
  `AttentionCardsSection`(分析)は全て人員不足(covU/covO)を扱うが、要約の粒度（原因診断1件ずつ→箇所単位
  重大度リスト→日別/人別トグル集計）が異なり相互補完的（既存の分析タブ内`OverviewDashboard`撤去等の前例と
  同様、単純な数値の重複ではなく別の切り口）と判断し変更なし。`V6DashboardCard`（分析タブ・proMode限定）の
  `dayRisks`/`staffProfiles`は`AttentionCardsSection`の日別/人別リストと同種の情報を別の計算経路
  （`V6PortAnalyzer`、`UnifiedViolationChecker`とは独立）で重ねて表示しており概念的には重複候補だが、
  proMode限定の「生指標」（V6DashboardCardの他の指標=HARD Core/Guard・Apt/Equalize等と地続き）としての
  価値もあるため、本セッションでは変更を保留（要ユーザー判断、次点候補）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0・`SetupGuideCard`呼出1箇所のシグネチャ
  一致を静的確認。最終判定は CI（Release Build）。

## 停滞時間をログへ出力（3.217.0）
ユーザー指示「停滞の時間をログに出力する」。既存の停滞検知ログは「予算Ns中Msで停止」という**総経過時間**
のみで、実際に何ms/何s無改善が続いて停止に至ったか（=停滞そのものの長さ）が分からなかった。
- **V6FinalPort（外側の停滞検知＝EarlyStopログ）**: `stagnationDurationMs`（AtomicLong）を新設し、
  `shouldStop`が発火した瞬間に`now - maxOf(lastBestImproveMs, lastPhaseChangeMs)`を記録（ログ側で
  後から再計算すると後処理(runPostOptimization)の所要時間が混入し判定に使った値とズレるため、発火の瞬間に
  スナップショット）。ログを「停滞検知: 改善が無いため早期終了（予算Ns中Msで停止・**停滞Ns無改善**・解は
  最良を維持）」に拡張。
- **HF80 PostPolish（E10停滞早期終了、Kotlinフォールバックループ）**: 同様に`stallDurationMs`を発火時に
  記録し、「（停滞早期終了 枠Ns・**停滞Nms無改善**）」を追記。ネイティブ経路(runPolishChunksNative)側の
  停滞時間はJNI越しの追加返り値が必要なため今回は対象外（既存の「停滞早期終了 枠Ns」表記のみ、総経過時間は
  そのまま`nowMs()-started`で分かる）。
- 表示専用のログ追加のみ・スコアリング/探索ロジック不変。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## C1Polish頭打ち理由の可視化＋休の適切回数チェックを実質的上限へ差替え（3.236.0）
ユーザー指示「C1『休5日窓2日』が研磨されない理由を細かく改善できるようにする」「本当に休のapt設定が
過大な場合は警告する」への対応。3.234.0で休(restIdx)をチェック6-Cから丸ごと除外したが、ユーザーから
「本当に過大な場合は警告してほしい」という妥当な指摘を受け、**除外でなく休向けの意味のある比較へ差替え**。
- **[休の適切回数チェックを実質的上限へ] `V6SanityPort.kt`検査6-C**: 休の`aptSum`を`seatsHi`(need1/2合計、
  休には無意味)と比較する代わりに、**`restCapacity`=Σ_i(T − 他シフトの個人下限合計)**（各職員が他シフトの
  下限を満たしたうえで最大何日休めるか、の全員合計）と比較する。6b(幻のapt目標)の「担当レパートリーから
  強制される最低回数」ロジックを個人単位でなく全体合計に適用したもの。他シフトの下限が未設定なら
  minOther=0＝ほぼT日休める計算になり保守的（誤検知を避ける側に丸める）。ユニットテスト3件
  （控えめな目標は誤検知しない／T日に対し物理的に不可能な目標は検出する／他シフト下限を差し引いた
  実質上限を下回れば検出する）で新ロジックを固定。3.234.0の旧テスト(除外一辺倒)は新ロジックの検証に
  合わせて書き換え。
- **[C1Polishの頭打ち理由を可視化=RangePolish(3.222.0)と同型] `V6HotfixPasses.applyC1WindowPolish`**:
  手A(同日交換)/手R1(鏡像長方形)/手R2(自己2日swap)いずれも成立しない場合の最終フォールバック(手B=
  直接移動+`findCovUChain`玉突き)の結果を(staff, ルールのシフト)ごとに集計し、「候補なし」(玉突き
  相手が1人も見つからない構造的ブロック)／「不採用」(候補は見つかったが実目的関数`isBetter`が
  他族とのトレードオフで総合的に拒否)の2分類でログの「残存:」に表示する。研磨後もなお当該窓ルールが
  不足している(staff,shift)のみを対象にし、途中で別の手/別の日で解消済みのものは除外する。
  「なぜこの職員のこの窓ルールが解消しないか」がログから直接読めるようになる（実機ログで
  「c1 163->163 採用0回(鏡像:0 自己:0)[頭打ち=改善手なし]」としか出ておらず、原因(構造的に候補が
  無いのか、トレードオフで負けているのか)が読めなかった問題への対応）。ユニットテスト1件
  （唯一の玉突き候補が全日希望固定の局面で「候補なし」の理由が残存表示に出ることを固定）。
- 両者とも診断・探索オペレータの内部可視化のみ＝重み・スコアリング・採否ロジックは完全に不変。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。実機ログでの効果確認は次回。

## C1専用の時系列DP研磨を新設＝2箇所以上の同時移設でしか越えられない局所最適に対応（3.247.0, 外部パッチ受領・検証のうえ適用）
ユーザーから続編パッチ(`c1_temporal_dp_3_246.patch`、ヘッダのversionCode予約により実装は3.247.0として適用)を
受領。receiving-code-review規律に従い、DPのビットマスク遷移とbeam searchによる同日swap実現の両方を
手計算で独立にトレース検証してから適用。
- **問題意識**: 既存`applyC1WindowPolish`の手R3は1回のswapだけをbest-improvementで評価するため、
  「2箇所以上を同時に動かさないとc1が下がらない」局所最適（例: T=11・窓5日以上2回のルールで
  X={0,1,5,6}日の配置は、どの1回のswapを試してもどこかの窓を新たに壊す）を越えられなかった。
- **新設 `C1TemporalDp`（ビットマスク時系列DP）**: 対象シフトか否かの二値列を対象職員の行全体で
  最適化する。状態=(直近`maxWindow-1`日分の二値パターン, 累積対象シフト日数, 累積移設回数)、遷移コスト=
  違反窓増分×1e6＋変更セル×1e3＋決定的tie-break。対象シフトの月間回数を厳密保存（`count<=originalCount`
  ガード）しつつ、最大`maxRelocations`(既定4)回の「非対象→対象」（同数の「対象→非対象」と対）を許容。
  希望固定日は`locked[]`で現在の対象/非対象状態に固定。t<=63日・窓<=20日のみ対応（実データ月31日で十分）。
- **新設 `C1TemporalSwapPolish`**: DPが出した「変更すべき日の集合」を、日ごとの同日2者swap（対象職員↔相手、
  対象/非対象を入れ替えるだけ）で実現するbeam search。日別シフト多重集合は完全保存（covU/covO構造的不変）。
  DP/beamの費用（c3n/c1/range・apt近似）は候補生成専用の**ヒューリスティック**で、最終採否は必ず
  `UnifiedViolationChecker`＋hard→total→weightedScoreのkeep-bestで行う（makesForbiddenRunの事前フィルタは
  意図的に無し=候補生成側で見逃しても、hard悪化はisBetter相当の`better()`が必ず弾くため安全側）。
- **配線**: `runPostOptimization`のフィックスポイント巡回で既存`applyC1WindowPolish`の直後に追加
  （1巡あたり1pass・4試行・beam96で後処理予算を抑制）。
- **検証（手計算で独立に再現・受領コードを鵜呑みにしない）**: `exactDpCrossesTwoSwapLocalMinimumAndPreservesCount`
  はDPの遷移式を手でトレースし、期待される2移設解（day0↔2, day5↔7の交換）が実際に窓5日ルールを
  全窓非違反にすることを検算（1回のswapでは必ずどこかの窓を壊すことも手計算で確認済み＝真に局所最適の罠）。
  `temporalDpPolishAcceptsTwoSimultaneousDailySwaps`は、partner職員をtarget職員の完全補集合になるよう
  設計することで「どのDP解が選ばれても同日swap相手が必ず存在する」ことを構造的に保証し、既存C1Polish
  （1回swapのみ）ではc1=1のまま解消しないが、本パスなら2回の同時同日swapでc1=0まで解消することを固定。
- 探索オペレータの追加のみ＝重み・スコアリング不変。実データでの効果は次回実機ログで確認。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 手Fを隣接日連動型へ拡張＝上條洋平のDﾃ3→2回で判明した穴を修正（3.246.0）
ユーザーが上條洋平の実例（Dﾃが5・6・7日の3連続、個人上限は既に0～2回で正しいが1回分超過）を詳細分析し、
「単純な同日交換は全候補が禁止連続を発生させる（本人の隣接日がまだDﾃのため）」「日別必要人数1人のため
covUも増える」と検証したうえで、「隣接日連動型の複数日フロー」への拡張を提案。receiving-code-review規律に
従い、提案されたロジックを鵜呑みにせず、既存コードベースを調査した結果、**必要な機構はほぼ全て
`tryFixForbiddenRunViaAdjacentDay`（3.163.0、covU玉突き連鎖`findCovUChain`向けに既に実装・実戦投入済み）に
既に実装済み**と判明。真に新しい「時空間フロー」を書き起こす代わりに、この既存の実績あるヘルパーを
`tryFlexibleDayFlow`（手F, 3.245.0）に接続する最小差分で対応。
- **背景**: `tryFlexibleDayFlow`は直接の`makesForbiddenRun`判定で塞がる(i,newK)辺を無条件に除外していた。
  ユーザーの提案5ステップ（①対象日を別職員へ渡す②受取職員の隣接日が禁止シフトなら再割当③本人の隣接日との
  禁止連続も再割当④玉突きで補充⑤複数日を一括評価）のうち②③④は`tryFixForbiddenRunViaAdjacentDay`が
  そのまま提供する機能（隣接日(j±1)を本人が調整してパターンを崩し、その調整で被覆が悪化するなら
  `findCovUChain`で1段だけ再帰的に埋め直す）と完全に一致。victim自身にもreceiverにも対称に適用可能
  （関数は任意の(staff,day,newShift)を受け付けるため）。
- **配線**: `tryFlexibleDayFlow`の(i,newK)コスト計算内で`badRun=true`のとき、即除外せず
  `tryFixForbiddenRunViaAdjacentDay(p, work, i, j, newK, rng)`を呼び出し（(i,newK)単位でメモ化、日jの間は
  隣接日が不変なので使い回し安全）、結果が空でなければ辺を生かし追加手(extras)として記録。実際に選ばれた
  (i,newK)の追加手だけを、日jの割当と一緒に一時適用→`UnifiedViolationChecker`で複数日一括評価→
  `isBetter`で採否（ユーザー要求の⑤と一致）。採用時は`FlowPlan`に`extras`を持たせ、コミット時に日jの割当と
  extrasの両方を適用。
- **安全性**: `tryFixForbiddenRunViaAdjacentDay`自体は盤面を変更しない契約（呼び出し前後で必ず復元）で
  既に実戦投入済み・検証済みのヘルパーのため、新規に導入したのは「呼ぶ場所」だけ。最終採否は変わらず
  `isBetter`(hard→total→weighted)keep-best＝退化不能。
- **検証（手計算で独立に再現）**: `rangePolishResolvesDteViaAdjacentDayLinkedFlexibleFlow`（3職員概念の
  最小盤面: 単一職員・Dﾃ上限0・隣接日が固定シフトQで、Q→休/Q→Qの2禁止連続により直接付替えの両候補が
  塞がる構成）で、①直接付替えが両destinationとも塞がること②`tryFixForbiddenRunViaAdjacentDay`が隣接日
  （固定シフトQの日）をQ→休へ調整することで初めて解放されること③結果としてDﾃ超過(high)・禁止連続(c3n)
  ともに解消することを、コストテーブル・SPFA経路・isBetter判定まで手計算でトレースして確認。
- 探索オペレータの拡張のみ＝重み・スコアリング不変。実データでの効果（上條のDﾃ3→2）は次回実機ログで確認。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 可変長ブロック交換（AdaptiveBlockSwap, 3.290.0, ユーザー提供パッチを検証のうえ統合）
ユーザーから「他者と以下のブロック日数を丸ごと入れ替えるアルゴリズムを作る（11/13/17/19/23/29）」の指示を受け、
まず現行 `applyBlockSwapPolish`（15日固定・同一担当グループ限定）を実データで測定したところ**採用0**と判明。
原因は2つ: ①ブロック内の**全日が両者とも希望非固定**でないと丸ごと棄却するため、希望81件の実データでは
11日以上の連続がどのペアにも存在しない（全長で「丸ごと成立=0箇所」）②同一グループのペアが6組しかない
（上條/古泉/桒澤/大島は各グループ唯一＝相手不在）。この測定結果を踏まえ、ユーザーが `applyAdaptiveBlockSwapPolish`
を実装したファイルを提供。**受領コードを鵜呑みにせず実ビルド・全テスト・実データA/Bで検証**してから統合した。
- **新演算子 `applyAdaptiveBlockSwapPolish`**: 11/13/17/19/23/**28**日の非等間隔ポートフォリオ。
  **28 は2月（28日）で「1か月まるごと」の交換を確保するための長さ**（当初レビューで「素数でない＝7日周期と
  共鳴する」と指摘したが**誤り**で撤回。この演算子は両者が同一の日を交換するため曜日の対応関係は長さに依らず
  同一＝共鳴という論点自体が成立しない。ユーザーの説明どおり2月対応が正しい設計意図）。
  **同一グループ限定を撤廃**し、ブロック内の全セルを相互に担当可能なら異グループ間でも候補化する
  （被覆量は同日交換のため構造的に保存）。長さごとに独立の候補プール（PriorityQueue）＋ラウンドロビン評価で、
  候補数上限があっても長い28日案が11日案に押し出されない。順位付けは range/apt の marginal 推定＋違反関与度、
  **採否は必ず `UnifiedViolationChecker` ＋ `isBetter`（hard→weightedScore→total）＋ `exactPinRegression`** の3段。
- **受領コードから修正した3点**（実ビルドで検出）: ①**コンパイルエラー**: `"[$lensLabel日]"` は Kotlin が
  日本語を識別子文字として扱うため `lensLabel日` という未定義シンボルになる → `${lensLabel}日`。
  **この1文字でビルド全体が落ちる**状態だった ②`candidateFor` のコメント「同値セルも希望固定なら触らない」が
  実装（ブロックごと棄却）と不一致（HF77）→ 実装に合わせて「ブロックごと候補から外す」旨へ ③28の設計意図
  （2月対応）をコメントに明記し、後から「素数列に揃える」と誤修正されないようにした。
- **実データA/B（後処理研磨のみ・3データセット）＝効果は中立（当初の改善報告は撤回）**:
  初回計測で「weighted 33179→33141・c1 54→49」と報告したが、**各ビルド4回ずつの反復計測で誤りと判明し撤回**。
  現行 main も4回中3回は c1=49 を出す＝差は **JointLNS の壁時計予算(8s/6s)由来の既知の非決定性**
  （3.279.1 に記録済み）を拾っただけで、両ビルドは分布として同一だった。**1回計測で A/B 判定してはならない**
  という教訓（本セッションで実際に踏んだ）。3データセットとも採用0・退化なし・pin-regressions=0＝中立。
- **不活性の理由（実測）**: パイプラインには配線済みで実際に走るが、ログは
  「採用0回 **候補4件**/正式評価4件」。`candidateFor` が「ブロック内に希望固定が1つでもあれば return null」
  ＝希望81件の実データでは11日以上の無ロック連続がほぼ存在しないため、全探索空間で候補が4件しか生成されない
  （同一グループ限定だった旧実装では0件。異グループ解禁で0→4件に増えたが実質不活性）。
  **正しさ・安全性は問題ないが、このデータでの実効果はゼロ**。候補生成を「希望固定セルは据え置き、
  動かせるセルだけ交換」へ緩めれば候補は100箇所超へ増える（実測済み）が、「期間をまとめて入れ替える」という
  手の意味は薄れる＝別途の設計判断。**→ 3.291.0 でユーザー指示により緩和（次節）**。
- 検証: ホストJVM **全320テスト green**（新規2件=異グループ×11日ブロックで旧パスが0採用の局面を新パスが解消・
  最適盤面ではno-op）。C++ は無変更＝native parity 影響なし。

## ブロック交換の候補生成を「希望固定日は据え置き」へ緩和（3.291.0, ユーザー明示指示）
3.290.0 の実測（候補4件・採用0＝実質不活性）を受け、ユーザー指示「候補生成を『希望固定セルは据え置いて
残りだけ交換』へ緩める」に対応。**表示でなく探索オペレータの候補生成の変更**（重み・採否・スコアリングは不変）。
- **変更点**: `candidateFor` が「ブロック内に希望固定（`wishLocked`）または相互に担当不可（`canDo`）の日が
  1つでもあればブロックごと棄却（`return null`）」だったのを、**その日だけ `continue` で据え置き、残りの日だけ
  交換する**へ変更。`Candidate` に実交換日 `days: IntArray` を持たせ、`swap()` はブロック全域でなく `days`
  のみを入れ替える。**安全性は不変**＝各日は「2人の値を交換するだけ」なので日ごとのシフト多重集合＝被覆量は
  依然として保存され、担当可否は日ごとに検査する。1日だけの交換は既存 CyclicSwap と同一なので `differences >= 2`
  を要求（「期間をまとめて入れ替える」手の意味を保つ最小条件）。
- **実測（user_state・後処理研磨のみ・同一seed）— 「発火するようになった」が「最終値は僅差で悪化」**:
  - 候補 **4件 → 2,267件（567倍）**・正式評価 4 → 48件。**ブロック交換の採用は 0 → 1回**
    （`SoftPolishVerify` の採用内訳で確認）＝3.290.0 で「実質不活性」だったパスが**実データで初めて発火**した。
  - ただし最終値は **weighted 33137→33167 / total 168→170 / c1 49→54** と僅差で悪化（各3〜4回反復し両者とも
    決定的であることを確認）。**採用手自体は keep-best で厳密改善**（`isBetter` を通っている）だが、1手採用で
    フィックスポイント巡回が **3巡→4巡** に伸び、壁時計予算で動く後段（C1共同LNS 8s／個人共同LNS 6s）が
    別の局所解へ着地する**経路依存**。差の30点は main 自身の run 間ばらつき（33137〜33179、3.290.0 で計測）の
    帯の中で、正しさ・退化の問題ではない。
  - golden_state / real_state は **main とバイト一致**（2469/306/c1 104・49232/179/c1 58）・pin-regressions=0。
- **採否の判断**: ユーザーの明示指示であること、緩和後の意味論のほうが素直（「希望は動かさず、動かせる期間を
  まとめて入れ替える」）であること、strict のままでは実データで候補4件＝事実上デッドコードであること、
  安全性・回帰なしを実測で確認したことから**採用**。単一データセットでの僅差の悪化は経路依存として正直に記録する
  （3.290.0 の「1回計測で A/B 判定してはならない」教訓の継続適用）。
- 検証: ホストJVM **全321テスト green**（新規1件=T=11・有効長11のみ＝唯一のブロックが希望固定日を必ず含む盤面で、
  旧実装なら候補0件のところ、固定日を据え置いて残り10日を交換し下限割れ 22→2・固定日のセルが不変・被覆保存を固定）。
  C++ は無変更＝native parity 影響なし。

## ブロック交換を可変長の巡回交換（3者・多者）へ一般化（3.292.0, ユーザー指示「三者交換、多者交換なども追加する」）
2者交換は「A の X を B へ渡したいが B の持ち札は A に不要」という**非対称な譲り合い**では成立しない。
3者以上の巡回（A←B←C←A）ならこれが閉じる。既存 `applyBlockRotationPolish` も3者回転を持つが
**窓が2〜3日固定・全日movable必須**のため、長期ブロック（11〜28日）の巡回はどのパスも探索していなかった。
grilling で対応範囲を確認し、ユーザー選択は**「3〜5者以上（可変N）」**。
- **全列挙をやめ、改善グラフ（cyclic exchange / VLSN）で生成**: ブロック (start, length) ごとに有向辺
  `u→v` の重み＝「u が v のブロックを受け取ったときの u 個人の回数ペナルティ改善見積り」を作る。
  **各参加者の損得は「自分の札を出して直前者の札を受け取る」ぶんだけで決まる**ため辺ごとに分解でき、
  巡回全体の見積りは辺重みの単純和になる（2者では近似でなく厳密、3者以上は交換日をやや広く見積もる近似）。
  最小番号アンカー＋深さ `maxCycle`(既定5) の DFS で巡回を列挙。全列挙なら5者だけで54万候補生成＝数秒級
  だったのが、辺重みの前計算＋O(1)/巡回の枝で実データ 27〜51万巡回を数百msで捌く。
- **2段階生成**: ①DFS は**見積りキーだけ**で巡回を選別（実候補を作らない） ②各プールに残った上位だけ
  `candidateFor` で実候補にする。段①のプール幅は段②の8倍（`stageOneWidth`）。**幅を分けたのは実測が根拠**:
  同幅にすると「見積り上位が実候補化で落ちる（交換成立日が1日以下）」たびにその下の成立候補まで失い、
  golden で2者の実候補が 2,267→13 件へ痩せた。幅を分けた後は 178〜328 件・2〜5者すべて生成。
  満杯後の却下は最小キー保持で O(1)。
- **見積り0の巡回も捨てない**（初版の実バグ）: 初版は `sum + close > 0` の巡回だけ記録し、実データで
  **候補0件**になった。ブロック交換の本命は c1/連続規則/曜日偏りの同時改善で、個人回数が動かない
  （見積り0の）手が実際に採用され得る（3.291.0 で実測済み）。見積りは**順位付け専用**と割り切り、
  全巡回を記録して上位を実候補化する形へ修正。
- **checker コストは据え置き**: 正式評価は `maxEvaluations`(48) のまま。候補プールを
  **(ブロック長 × 巡回人数)** で分けラウンドロビン評価するので、長い28日案が11日案に、5者案が2者案に
  押し出されない（3.290.0 の長さ軸ラウンドロビンを巡回人数軸へ拡張）。増えるのは安価な候補生成だけ。
- **安全性は2者と同一**: 日ごとに参加者間で値を巡回させるだけ＝日別シフト多重集合＝被覆量は保存。
  各辺の受け手の canDo をその日ごとに検査。希望固定・担当不可の日は据え置き（3.291.0 の意味論を継承）。
  採否は `UnifiedViolationChecker` ＋ `isBetter` ＋ `exactPinRegression` の3段で不変。
  **3者以上は巡回が非可逆**（2者の swap と違い2回適用しても戻らない）ため、評価の適用/巻き戻しは
  `rotate(forward)` / `rotate(forward=false)` の逆回しで厳密に復元する（2者は順逆同一＝従来と一致）。
- **実測（後処理研磨のみ・3データセット・user_stateは3回反復して決定的を確認）**:
  golden 2469/306/c1 104（main と一致）／real 49232→49221・c1 58→59／user 33167→**33135**・total 170→166・
  c1 54→**49**。pin-regressions は3件とも0。**ただし3データセットとも巡回交換の採用は0**＝この実データには
  改善する巡回が存在しない。値の差は評価回数・巡回数の違いによる**経路依存**であって、巡回交換が何かを
  直した結果ではない（3.290.0「1回計測で A/B 判定してはならない」の継続適用として、効果として主張しない）。
  実候補の内訳はログに出す（例 `内訳 2者:166 3者:101 4者:48 5者:13`）ので、実機で多者交換が
  出ているか・採用されたかが後から追える。
- 検証: ホストJVM **全323テスト green**（新規2件）。①**3者巡回**=3職員が各自2シフトしか担当できず
  **どの2者交換も canDo で不成立**な盤面で、`maxCycle=2` なら採用0・既定なら1手で下限割れ 33→0・被覆保存。
  ②**4者巡回**=同型で `maxCycle=3` でも到達不能・既定（5）なら 44→0。C++ は無変更＝native parity 影響なし。

## ブロック巡回交換の「採用0の理由」をログ化（3.293.0, ユーザー質問「採用ゼロの内訳を教えてください」）
3.292.0 が3データセットとも採用0だったが、ログは「頭打ち=改善手なし」としか言わず**何に負けたか**が読めなかった。
`RangePolish`(3.222.0 頭打ち理由)・`C1Polish`(3.236.0 残存理由) と同じ趣旨の診断を追加。**ログのみ・採否ロジック
不変**（3データセットとも結果が 3.292.0 とバイト一致することを確認済み）。
- **分類は `isBetter` の判定順（HARD → weightedScore → total）と厳密に一致**させる: ピン破り
  （`exactPinRegression`）／必須増／重み悪化／件数悪化／同値／採用手に劣後。「必須増」「重み悪化」については
  **重み付きで最も増えた族**（`MirrorKeys.weightOf` × breakdown 差）を「悪化の主因」として併記する。
- **実測で判明した2つの壁（本番ポートフォリオ・正式評価48件）**:
  - user_state: ピン破り27・必須増21（**全て c3n**）・重み悪化0・件数悪化0
  - golden_state: ピン破り26・必須増18(c3n)・重み悪化4(c1)
  - real_state: ピン破り33・必須増9(c3n)・重み悪化6(c3mn)
- **① ピン破りが最大の壁（全体の55〜80%）**: 実データは**10名中9名の「休」が lo==hi の厳密ピン**（合計80）で、
  長いブロックを丸ごと交換すると休の回数が必ず動く＝期間が長いほど当たる。巡回交換の質でなく
  **このデータの厳密ピンと期間交換が構造的に両立しない**ことが原因。
- **② 必須増は全て c3n（禁止連続）**: ブロック**境界**で新しい禁止パターンができる。境界2日ぶんの判定なので
  事前フィルタで避けられる余地がある。
- **③ 重み悪化・件数悪化・同値がほぼゼロ（0〜6件）**: **soft 族の取引で負けた候補は事実上いない**＝
  「改善する手が無い」のではなく「①②で入口を塞がれている」。①②を回避できれば採用される見込みが高い。
- **未実装（次の手・指示待ち）**: (a) **ピン保存交換**＝厳密ピンのシフトについて交換日集合を「両者の当該シフト
  日数が等しくなる」よう選ぶ（①をほぼ全滅させられる・中規模） (b) **境界 c3n の事前フィルタ**＝候補生成時に
  `makesForbiddenRun` で境界を確認（②を潰す・安価）。
- 検証: ホストJVM **全323テスト green**（テスト変更なし）。golden 2469/306・real 49221/180・user 33135/166 と
  3.292.0 に完全一致・pin-regressions は3件とも0。C++ 無変更＝native parity 影響なし。

## ピン保存交換＝ブロック巡回交換の最大の壁を除去（3.294.0, ユーザー指示「a」＝3.293.0 の次の手(a)）
3.293.0 の不採用内訳で「採用0の55〜80%が厳密ピン(lo==hi)破り」と判明したことへの対処。
**候補生成の変更のみ＝重み・採否ロジック（`isBetter`/`exactPinRegression`）は完全に不変**。
- **実装 `balancePinnedDays`**: 交換日集合を「**厳密ピンのシフト回数が1つも動かない**部分集合」へ絞る。
  各日 j について、参加者 t のピン付きシフト k の増減 `d = [直前者が k] − [自分が k]`（∈{−1,0,+1}）を
  並べた**符号ベクトル**を作り、①ゼロベクトルの日は常に採る ②非ゼロの日は**符号が正反対の日と対にして**
  採る（打ち消し合う）。総和がゼロベクトル＝ピンは1つも動かない。符号は2bit/スロットで Long へ詰め、
  反転はビット入れ替え1回。3日以上での相殺は拾わない（安価・安全側＝採れない日を落とすだけ）。
- **対象はいま満たされているピンだけ**（`counts == lo == hi`）。すでに外れているピンは動かして直せる余地が
  あるため拘束しない（悪化は従来どおり `exactPinRegression` が弾く）。ピンが無ければ即 return＝コストゼロで
  従来と同一。スロット31超も従来動作へフォールバック。
- **実測（本番ポートフォリオ・正式評価48件）＝狙いは達成、ただし壁が移っただけ**:
  - **ピン破り 27/26/33 → 0/0/0**（3データセットとも完全に消滅）。設計どおり。
  - しかし**必須増(c3n)が評価枠を占有**するようになった（user 48/48・golden 39・real 34）＝**採用は依然0**。
  - 最終値: golden 2469/306（不変）／real 49221→49232／user 33135→33167。いずれも僅差の悪化だが
    **このパスは盤面を1手も変えていない**（採用0）ので、差は評価回数・候補数の違いによる経路依存であり
    ピン保存交換の効果ではない（3.290.0「1回計測で A/B 判定してはならない」の継続適用）。
  - **結論**: (a) は「ピン破りを消す」という目的は完全に達成したが、**残る壁 c3n を潰さない限り採用には
    到達しない**。次の手 (b)（境界 c3n の事前フィルタ）が実質的に必須と実測で確定した。
- 検証: ホストJVM **全324テスト green**（新規1件=ブロック全体の交換なら A の休が 4→2 で必ず却下される盤面
  ＝旧実装なら採用0。休の増減が打ち消し合う9日だけを交換して**休4を保ったまま** Y の下限割れを解消し、
  被覆保存・HARD不変も固定）。C++ 無変更＝native parity 影響なし。

## 境界 c3n の事前フィルタ＝構造的な不採用要因を両方とも0にする（3.295.0, ユーザー指示「(b) も入れる」）
3.294.0 でピン破りを消した結果、残る不採用が**全て** 必須増＝c3n（禁止連続）になった（user 48/48・
golden 39・real 34）ことへの対処。**候補生成の変更のみ＝重み・採否ロジックは完全に不変**。
- **この巡回交換で変化しうる HARD は c3n だけ**という構造的事実を利用する: covU/covO は同日置換で不変・
  groupViol は canDo・pref は movable(wishLocked) で不変。かつ **c3n は職員行ローカル**なので、
  参加者の行に交換を当てた fire 数を `C1DeltaPrefilter.staffC3nFires`（checker の forbidden 窓完全一致と
  同一意味論）で数えれば**近似でなく厳密**に判定できる。正味増える候補は生成時に捨てる。
- **実測（本番ポートフォリオ）＝両方の壁が消え、正直な「改善手なし」へ到達**:
  - **ピン破り 0・必須増 0**（3データセットとも）。残る不採用は**全て soft のトレードオフ**
    （user 重み悪化14: low 11・c1 3 ／ golden 38: c1 37 ／ real 16: c3mn 11・c1 4・low 1）。
  - **正式評価が 48 → 14〜38 件へ減り、パス自体が安くなった**（構造的に詰んだ候補へ checker を
    呼ばなくなったため）。
  - 採用は依然0。ただし意味が変わった: 3.293.0 までの「入口を塞がれていた」状態ではなく、
    **壁を両方外したうえで真の目的関数が拒否している**＝この3データセットには改善するブロック巡回が
    実際に存在しない、という結論が初めて言える状態になった。
  - 最終値: golden 2469/306（main と一致）／real 49232/179／user 33167/170（3回反復で決定的）・
    pin-regressions は3件とも0。値の差は採用0＝盤面不変のため経路依存であり効果ではない。
- **段①の幅は ×8 のまま維持**（実験: ×32 にすると実候補 16→73・golden 44→110 と増え評価枠48を埋めるが、
  **採用は依然0**でビルド量だけ4倍になる）。
- 検証: ホストJVM **全324テスト green**（テスト変更なし）。C++ 無変更＝native parity 影響なし。

## c3n 事前フィルタを既定OFFへ（3.296.0, ユーザー指示「巡回交換の c3n フィルタを外す」）
「C3n自体を変更できるようにする」の指示を grilling で確認し、ユーザー選択は**「巡回交換の c3n フィルタを外す」**。
- **前提の確認（調査済み）**: cons3n（禁止の並び）の追加・変更・削除は**既に制約画面で可能**
  （`ConstraintsCard`「禁止の並び」の行タップ編集・`constraintFamilies`/`addCons3`/`updateConstraint`/
  `removeConstraint` が全10族対応）。重複ルールのワンタップ削除（`SettingFixAction.DELETE_DUP_SEQ`）もある。
  よって指示は「UI編集の追加」ではないと判断し、選択肢を提示して確定した。
- **実装**: 3.295.0 のフィルタを削除せず `filterC3nIncrease: Boolean = false`（**既定OFF**）へパラメータ化。
  実行経路からは外れる＝指示どおり「外した」状態で、検証済みロジックは1行で戻せる形で残す。
- **提示した技術的懸念（ユーザーは了解のうえ選択）**: c3n は HARD（重み7000）なので、c3n が**増える**候補は
  `isBetter` が第1キー（hard）で必ず却下する。フィルタは `firesAfter > firesBefore` の候補だけを落として
  おり、**減る・同数の候補は元から通していた**。よって外しても採用は増えない。
- **実測で裏取り（ON/OFF の A/B）**: 最終盤面・採用数が**完全に同一**（user 33167/170/c1 54・
  real 49232/179/c1 58、採用ともに0）。違いは **正式評価 14〜38件 → 48件**（＝詰んだ候補への無駄な
  フル checker 呼び出しが戻る）だけ、という予測どおりの結果を確認。
- **c3n を本当に「変更」して交換を通したい場合の残り2案**（→(1)は 3.297.0 で実施）:
  (1) 壁になっている cons3n ルールを名指しして緩める導線（ForbiddenDiag 3.280.0＋SettingIssue ワンタップ
  修正の延長。データ変更はユーザーの明示操作なので HF77 を侵さない） (2) c3n を HARD→SOFT へ重み変更
  （HF77 該当＝明示的な数値指示が必要・目的関数の3面同時変更）。
- 検証: ホストJVM **全324テスト green**（テスト変更なし）。C++ 無変更＝native parity 影響なし。

## 壁になっている禁止の並びを名指しして緩める導線（3.297.0, ユーザー指示「1」＝3.296.0 の残り案(1)）
`ForbiddenRunDiagnosisCard`（3.280.0）は「なぜ崩せないか」を出すだけで、**直しに行く導線が無かった**
（制約画面まで移動して該当行を探す往復が必要）。**表示・導線のみ＝エンジン/重み/スコアリングは完全に不変**。
- **前提の確認**: cons3n の追加・変更・削除自体は既に制約画面で可能。今回足すのは「**どの並びが壁か**を
  名指ししてその場で外す」導線であって、編集機能の新設ではない。
- **カード側**: 「崩せない」判定（`!escapable`）の run だけを**並び（ルール）単位に集約**し、件数と対象職員を
  添えて「この並びの禁止をやめる」ボタンを出す。**崩す手が残っている並びは出さない**（探索で解けうるものに
  対して先にルールを消させない）。実行中は `enabled = !ui.running`。
- **ViewModel `relaxForbiddenRule(seqLabel)`**: 同じ並びの cons3n 行を**全件まとめて削除**（1件でも残ると
  壁が解消しないため。cons3n の重複は既知＝設定ミス診断でも指摘される）。`applyStructureWithMessage` 経由
  ＝Undo 可・自動再診断・自動保存。running 中はガード。
- **キーの意味論に注意**: 削除キーは `Problem.resolveC3` と同じ「**最初の空白まで**を本体」で作る。
  既存の `SettingFixAction.DELETE_DUP_SEQ` は空白を**除去**する別の意味論なので**流用しない**
  （["Y","","X"] を resolveC3 は ["Y"] と解釈するのに対し、除去だと "Y→X" になり一致しない）。
- **検証**: UI層はホストでコンパイル不可のため、導線が依存する不変条件（ForbiddenDiag の `seqLabel` が
  cons3n 行から復元したキーと一致する）を **v6層のユニットテストとして固定**（末尾/途中空白を含む3行構成で
  キー生成を検算し、検出された全 run の seqLabel がキー集合に含まれることを確認）。あわせて実データ
  （user_state / real_state, cons3n 15行）へ Dﾃ→A4 を注入して run を発生させ、seqLabel が実際に一致し
  削除対象行が特定できることをホスト実行で確認済み。ホストJVM **全325テスト green**。

## c3n 事前フィルタを PolishGate 経由で配線（3.298.0, ユーザー指示「配線する」）
3.296.0 で入れた `filterC3nIncrease` は**パラメータが存在するだけで誰も渡さない＝未配線**だった
（3.290.0 で「配線する」と言われたときと同じ形＝実装済みだがライブ経路から呼ばれていない）。
**表示・設定の配線のみ＝重み・採否ロジック・スコアリングは完全に不変**。
- **`PolishGate`（新設）**: `NativeGate`（ネイティブ加速／Kotlin照合）と同型の `@Volatile` フラグ置き場。
  呼び出し鎖に引数を通さずに UI 設定をエンジンへ届ける。セッション内のみ＝state に保存しない
  （勤務表データに影響しない実行時の調整のため）。
- **配線**: `applyAdaptiveBlockSwapPolish` の既定引数を `PolishGate.filterC3nIncrease` に
  （Kotlin の既定引数は呼び出し時評価なので `runPostOptimization`/`V6FinalPort` は**無変更**で届く）
  ／`UiState.blockSwapC3nFilter`（既定 false）／`MagiViewModel.setBlockSwapC3nFilter`（`NativeGate` 系と
  同じくゲートと UiState を同時更新＋操作ログ）／設定タブ→**詳細設定（上級者向け）**の
  `OptimizationTuningSection` にトグル（3.188.0 で確立した技術系チューニングの定位置）。
- **既定は OFF のまま**＝3.296.0 のユーザー選択を維持。ONにできるようにしただけ。
- **文言は効果を正直に**: 「できあがる勤務表は同じで（そういう案は最後に必ず却下されるため）、
  無駄な検査を省くぶんだけ速くなります」＝速度のみの設定であることを明示（品質が上がると誤解させない）。
- **実測（配線の動作確認・user_state）**: トグルだけで挙動が切り替わることを確認。
  OFF＝実候補281件/正式評価48件/不採用は必須増48(c3n) ／ ON＝実候補13件/正式評価13件/不採用は全て
  soft(low 8・c1 4・high 1)。**最終盤面は両方とも同一**（total 183→183・HARD 4→4）。
- 検証: ホストJVM **全325テスト green**（テスト変更なし）。UI層はホストでコンパイル不可＝ブレース/丸括弧
  均衡（HEAD と同一オフセット）・import・呼び出し側シグネチャ一致を静的確認。最終判定は CI。

## keep-best統一の取り残し修正＝UI層2経路・テスト不変条件・退避の原子性（3.289.0, 外部レビュー3件＋自己発見1件）
3.287.0（keep-best を hard→weightedScore→total へ統一）に対する外部レビューを全件コード照合し、**3件とも実在**を確認して修正。
さらに掃討の過程で**同型のより重い取り残しを1件自力で発見**した。全て検証済み・スコアリング自体は不変。
- **[High/外部] `runSoftPolish` の保険が旧順序**: `(hard, total)` のみで weightedScore を一切見ておらず、
  3.287.0 で keep-best が正しく採用するようになった「HARD同値・weighted改善・total増」の結果をこの保険だけが
  「悪化」と判定して入力へ戻していた＝ソフト研磨に限り目的関数統一を打ち消していた。`betterReport` へ統一。
- **[High/自己発見・外部指摘より影響大] `runV6FullOptimize` の `worseThanInput` も同型**: **メイン最適化経路**の
  再実行 keep-best が同じく `(必須, 合計)` のみで、300秒走って得た「weighted改善・total増」の結果を丸ごと捨てて
  入力へ戻していた。同じく `betterReport` へ統一。あわせて `applyBgResult` の手書き3節も委譲へ集約（DRY化＝
  将来の順序変更でVM層だけ取り残される事故を構造的に防ぐ）。UI層を全数 grep し、比較器の取り残しは0になった。
- **[Medium/外部] `V6FinalBridgePortTest` に旧不変条件が6箇所**: 各テストが hard→total→weighted を手書き複製して
  おり、(a) weighted悪化・total減を「悪化なし」と誤って許し (b) 正しく採用すべき weighted改善・total増を「悪化」と
  誤判定する二重ドリフトを抱えていた（現状は偶然通っていただけ）。共通ヘルパー `notWorseThan(after, before) =
  !betterReport(before, after)` に置換し本番と同一ソースへ委譲。
- **[Medium/外部] 「開く前のデータに戻す」退避が fire-and-forget**: `viewModelScope.launch(IO){ writeText }` のため
  ①「戻す」連打で更新前の退避を読み同じデータが2回出る（＝スワップ契約の破れ）②状態切替後・書込前のプロセス終了で
  退避が消えるのに復元可能表示だけ残る、が起こり得た。**状態切替前に同期＋一時ファイル経由の原子的置換**へ変更し、
  **書込成功時だけ**復元可能フラグを立てる。
- **[反論・意図的に不変] progressWatch の許容誤差**: `betterReport` の厳密比較へ寄せるべきとの指摘に対しては、
  ここは停滞ウォッチドッグの「改善」定義であり、厳密比較だと double の 1e-15 級の揺れを改善と数えて**停滞時計が
  永久にリセットされ早期終了が機能しなくなる**（＝許容誤差が正しい）。keep-best（採否）とは目的が異なるため統一しない。
  コメントにこの理由を明記済み。
- 検証: ホストJVM **全327テスト green**（テスト側の共通化込み）。UI層は Android 依存でホストコンパイル不可＝
  ブレース/丸括弧均衡0・`betterReport` import・置換後の未使用変数なしを静的確認。最終判定は CI。

## 判断ログの3軸強化＋RSIラウンド行のスパム抑制（3.288.0, ユーザー指示「時間/回数/状態の3軸をログ強化する。ログスパム対応する」）
「いつ探索全体を切り上げるか（時間）／いつ探索戦略を変えるか（回数）／本当に改善可能な制約が残るか（状態）」を
それぞれ**実行ごと1行**で開示。旧来はいずれも結果（発火した/しなかった・族別件数）しか出ず、判断の根拠は
コード推論頼みだった。**全て読取専用・表示のみ＝スコアリング/探索/採否は完全に不変**。
- **[時間軸] `TimeBudget` 行（V6FinalPort）**: 予算配分と早期終了の全条件を1行で。
  「予算配分: 総240s = 探索220s + 後処理予約20s / 早期終了の条件: 最短実行40s経過かつ現フェーズ6s経過かつ
  無改善が216s(通常)〜30s(頭打ち=HARD下限到達 or c3n構造壁)続いたとき / 構造的HARD下限=0」。
  旧: minRunMs/stallMs/stallHardMs/searchDeadline/postReserve は全てコード内の導出値でログに出ず、
  「なぜその閾値だったか」を実機ログから追えなかった（3.283.0 の Watchdog 行は結果のみ）。
- **[回数軸] `戦略変更` 行（runRsi 末尾）**: focus 遷移を連続圧縮して1行に。
  「RSI focus遷移: [HF63降格:covU]→c3n→covO / HF63降格={CovU}」「covU×2→c3n / E9冷却1回」。
  新設 `compressFocusTrail`（"covU,covU,c3n"→"covU×2→c3n"、マーカー`[..]`は圧縮せず位置のまま挟む）。
  2手以上あるときだけ出力＝1ラウンドで終わる短いエポックではノイズを増やさない。
- **[状態軸] `残存分析` 行（エピローグ）**: 最終盤面の残存族を**もう直せない/まだ狙える**に仕分け。
  「もう直せない: covU 3件(構造的下限) / c3n 1件(証明済みの壁) ／ まだ狙える: c1 56件 / apt 7件 …」。
  構造判定の根拠3種＝①covU が `structuralHardFloor` 以下 ②c3n が ForbiddenDiag で証明済み（既存の
  `c3nWallResult`/`bestNonCovUAllC3n` を再利用）③その族を HF63 が「充足困難」と学習済み。③のため
  `V6NativeOptimizer.lastInfeasibleFamilies`（全 runRsi 呼出＝直接RSI/RSI++/適応ポートフォリオ各ワーカーからの
  union・`recordInfeasible` は synchronized・`optimize()` 入口で `clearInfeasible()`）を新設。
  まだ狙える族が空なら「なし＝これ以上は追っても減りません」と明示。
- **[スパム抑制3件]**: ①**RSIラウンド行を「改善したラウンド＋最終ラウンド」だけに**（適応ポートフォリオは
  1エポック=2ラウンド×数十エポック×ワーカー数＝この1種類が診断ログの大半を占め、HF63降格・早期終了・壁判定
  といった重要イベントを押し出していた。focus の履歴は末尾の `戦略変更` 行が全ラウンド分を保持＝情報は失わない）
  ②**HF63 deprioritize 行を集合が変化したラウンドのみ**（旧: avoid が立つと毎ラウンド同文）
  ③**SOFTピボット行を pivot が変わったときのみ**（旧: 停滞が続く限り毎ラウンド同文）。
- 検証: ホストJVM **全327テスト green**（新規3件=compressFocusTrail の連続圧縮/マーカー保持・recordInfeasible の
  union と clear）。実データ（real_state）で **25s/60s/240s の3帯を実走**し、TimeBudget/戦略変更/残存分析の
  3行が全て意図どおり出力されること・RSIラウンド行が抑制されること（240s PORTFOLIO で RunMAGI_RSI=2）を確認。

## keep-best比較順の統一＝第2キーを total→weightedScore へ（3.287.0, ユーザー指示「停滞に至るまでの改善の質を賢く高める」→AskUserQuestionで「isBetterをweighted優先化」を明示選択）
実機 2026-12 で観測した「total -18 と引き換えに weighted +238・厳密ピン2本(吉江/桒澤の休10-10)が割れる」
問題（3.283.1で未裁定として記録）の根本治療。ユーザーが3択（ピン復元研磨/最終番兵拡張/isBetter weighted優先化）
から**根本治療を明示選択**（HF77:「賢く改善する」明示指示＝目的関数統一の承認）。
- **根本認識（実装前に確定した最重要事実）**: SA/ALNS/LAHC/C++ の評価器 soft は元々**重み付き和**＝探索本体は
  weighted を最適化している。hard→total→weighted の辞書式は **Kotlin 側 keep-best 比較器だけの乖離**だった。
  よって本変更は「目的関数統一（チェッカーを正とする）」の最終仕上げで、**C++ は無変更＝パリティ影響なし**。
- **単一ソース `MirrorCore.betterReport(a,b)` を新設**（hard→weightedScore→total。total は決定性のための
  第3タイブレークに降格）し、private コピー全14サイトを委譲/並べ替え:
  `V6NativeOptimizer.better`・`V6HotfixPasses.isBetter`（全研磨パス＋CombinatorialRepair注入）・
  `mainNotWorse`（平準化の主目的ガード）・applyC1BeamPolish のビームランキング2箇所・
  `C1JointLnsPolish`/`PersonalBalanceJointLnsPolish`（better＋Nodeコンパレータ2種ずつ）・
  `C1TemporalFlowPolish`・`LightMirrorOptimizer`・`AdaptiveEliteArchive.compareReports`
  （EliteIntegrationPolish はこれへ委譲済み）・`V6LateOperators.gate`（第2キー soft生カウント→weighted、
  boost の soft<= ガードは追加条件として温存）・`V6SwapSuggester`（inline 5箇所＋提案ランキング）・
  `V6FinalPort`（watchdog改善検知＝停滞時計の「改善」定義・ExtraRefine採否・**checkResultWorse**=最終番兵の
  判定順も hard→weighted→total へ。hard>=ガードは維持）・`MagiViewModel.applyBgResult`（bg結果の採用判定）。
  探索デブト境界（totalDebt 等の探索許容幅）は accept でなく探索範囲のため意図的に不変。
- **テスト2件の是正（total優先前提の設計だった）**: `SessionRegressionTest.checkResultWorse_lexicographic` は
  新順序へ書換え（「total改善はweighted悪化でも良化」→逆転）。`C1RelocationPolishTest` の鏡像盤面は
  docstring 自身が「回数固定職員」と謳いながらピン未設定で、新比較器では「c1(15)を weekly(1)と交換する
  count-changing 手」が正当に追加採用され回数保存アサーションが破れた（挙動は正しい）→ staffRange 厳密ピン
  Range("2","2") を両職員へ追加し本来意図の盤面に強化。**ホストJVM全324テスト green**。
- **A/B実測（ホストJVM・runPostOptimization seed=12345・baseline=3.286.0）**:
  golden: weighted 2656→**2469**（−7.0%）・low 8→**5**・hard 0 不変・total 288→306（軽い族との正当な交換）。
  real: weighted 51337→**49231**・**low 2→0**（重い下限割れ全解消）・apt 8→6・hard 6 不変・total 173→178。
  **pin-regressions=両データとも0**。「重い違反を軽い違反の件数と交換する」逆向き採用が消えたことを数値で確認。
- **docs同期**: business-logic.md に keep-best 比較順の節を追加＋**既存ドリフト2件を発見・修正**
  （c1=4/c3mn=12 のまま＝実装は 15/15、3.249.0/3.253.0 の HF77 変更が未反映だった）。screen_spec.md／
  v6_engine_native_port.md の旧順序記述も更新。
- 実機での full-search 効果（ピン保持・探索全体の質）は次回実機ログで確認。exactPinRegression（3.256.0）は
  多層防御として残置。

## 画面間冗長性の解消4件（3.286.0, ユーザー指示「各画面と各オブジェクトの一覧表を作成し、画面間の冗長性をシンプルにする」→「フルコードトレースしてフルコードレビューする」でD追加）
全5画面×オブジェクトの一覧表をコード実測（MagiApp.kt タブ構成）で作成し、冗長性候補4件（A〜D）を提示。
初回は A+B+C を適用し D を据え置いたが、ユーザーが続くターンで A〜D を再列挙＝**D も明示指示**と解釈し同PRへ追加。
フルコードトレースのセルフレビューで A の残滓（職員管理ドアの stale 見出しコメント）も修正。
全て表示・デッドコードのみ＝スコアリング不変。
- **[A] StaffRangeCard 二重配置の一本化**: 編集タブ内で職員管理ドアと年間マスター「③ 回数（1人あたり）」の
  2か所に同一カード全体が重複していた（唯一のカード丸ごと重複）。**③へ一本化**（職員管理から撤去。回数設定は
  ③が意味的定位置・職員管理は人の属性管理=入職/退職/改名/所属/スキルに純化）。
- **[B] 旧「回数設定画面」の孤児VMクラスタ削除**: CountSettingsCard（2.60〜2.63世代）の画面本体は既に撤去済み
  だったが、VM 側の `shiftRuleBlocks`/`staffRuleBlocks`/`setCons41`＋データ型5種（GroupRule/IndivRule/
  ShiftRuleBlock/StaffShiftRule/StaffRuleBlock）が呼出0のまま残存していた。grep で外部参照0（テスト含む）を
  確認して削除。`staffCountRules`/`CountRuleView` は StaffRangeCard が使用中のため残置。
- **[C] V6DashboardCard の日別/人別重複リスト撤去**（分析タブ・プロのみ）: dayRisks チップ列と負荷プロフィール
  （staffProfiles top5）は AttentionCardsSection（全件＋要確認のみトグル＋タップ修復）が上位互換（3.195.0 で
  保留した次点候補の実施）。固有の生指標（充足率/HARD Core/Guard/Apt/Equalize/sanity警告/最優先行）は残置。
  孤児化した `RiskChip` も削除。`dayRisks` 自体は MagiScheduleViews（グリッドの日別不足）が使用中＝analyzer 不変。
- **[D] CheckSummaryView 撤去**（分析タブ）: 必須違反数の1行表示はホームの OperatorNextActionCard・
  要確認一覧（ConfirmListCard）見出しに続く**3重目**だった。3.83.0 の維持判断は ConfirmListCard ヒーロー化
  （3.80.0）前のもので、違反ゼロ時の達成表示も ConfirmListCard が持つため喪失情報なし。呼出0となった定義ごと
  削除（`V6RemainingScreens.kt`。SectionSegment は ColorSettingsView が使用中のため残置・import 全数確認済み）。
- **維持と判断（提案せず）**: CoverageDiag/ConfirmList/Attention の3層（粒度別・相互補完=3.195.0判断）／
  Ws1Card vs StaffManageCard（群×シフト表 vs 職員単位の別ビュー=3.114.0決定）／BreakdownCard vs
  ViolationFilterBar（内訳 vs 操作）。
- 検証: UI/VM層のみ＝サンドボックスは Kotlin コンパイル不可、ブレース/丸括弧/角括弧均衡0・削除シンボル残存参照0・
  流用シンボル（NumberStepper=NeedDayEditor定義・horizontalScroll等の import）の他所使用を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 判断設計監査の改善3件（3.285.0, 全5画面監査の「改善して再テスト」項目→「マージする」）
ユーザー主導の**判断設計監査**（13項目チェックリスト: 利用者の目的/守る前提/判断主体/UIの代行範囲/根拠/
影響表示/介入手段/誤認防止 等。不採用条件=前提の誤解・判断主体の誤認・根拠や取消手段の不明確さ）を
全5画面のクリティカル9オブジェクトへ適用（採用6・改善して再テスト3）。「マージする」指示で残3件を実装。
**全て表示・導線のみ＝スコアリング不変・HF77非該当**。
- **[#1/#2] 完了カードに結果採用の意味＋できあがり度の根拠を1行注記**（`OperatorNextActionCard`）:
  監査で「結果採用の承認ステップがUI上に存在しない」「できあがり度の算出根拠が画面のどこにもない」の
  2件が確認された。数字行直下（`!running && hasResult` 時のみ）に「※できあがり度＝最初からの違反の
  減り具合（必須違反が残る間は最大55%）。結果は下書きに反映済み・「元に戻す」で取消可・確定は
  書き出し時です。」を追加。式（hard>0=ratio×55 / hard=0=40+ratio×60）の言語化は「減り具合＋55%上限」
  の要点のみ（説明文は読まれない原則④とのバランス）。
- **[#3] 「データを開く」の取消不能上書きを1世代退避で解消**: コード実測で `loadAsync` は pushUndo なし
  ＋読込直後の autoSave が自動保存スロットを新データで上書き＝**旧データは書き出し済みでない限り復元
  不能**（不採用条件「取消手段の不明確さ」に唯一抵触）。対応: ①`loadAsync` 成功時、置換直前の状態を
  `exportJson()` で `magi_prev_before_open.json` へ1世代退避 ②`restorePreviousData()` 新設（退避を
  loadAsync で読み戻す＝現データが再退避される**スワップ意味論**・もう一度押すと元へ戻る）③設定タブ
  `DataActionsCard` に「開く前のデータに戻す（もう一度押すと入れ替え）」導線（`prevBackupAvailable` が
  真のときのみ表示・起動時に退避ファイル存在を検査して復元後も導線維持）。
- 監査の他の確定事項（記録）: 「無確認上書き」懸念だった希望反映は担当外確認ダイアログ実在を確認し採用
  ／グリッド即時コミット=D7決定と整合で採用／FixSuggestionCard=事前diff開示で模範形／Kotlin照合トグル=
  危険設定の判断設計として教科書的（既定安全・リスク明示・依存gating）／全項目共通の空欄=実利用者テスト
  未実施（テスト観察の実績は ForbiddenDiag 実機発火1件のみ）。
- 検証: UI層のみ＝サンドボックスは Kotlin コンパイル不可、ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 外部リポジトリ全体レビューの検証と採用5件の実装（3.284.0, ユーザー「何%正しいか?」→「強化修正する」）
外部の全体レビュー（対象=main 77c19fc・C++ホスト検証数値は当方実測と完全一致=本物）を全主張コード照合し
**約80%正しい**（事実85-90%・優先度70%）と回答。誤り=V6SwapSuggesterテスト実在・「16コルーチンに実行上限
なし」（Dispatchers.Defaultがコア数で暗黙cap）・観測性主張の一部stale。過大=High①の1秒例（UIは10秒下限）・
High②仮説/並列分離（3.225.0のユーザー明示決定と衝突）。採用推奨5件を「強化修正する」指示で実装:
- **[High③/実バグ] 停止状態の固着解消**（`MagiViewModel`）: checkJob×3サイトは `CancellationException` 再送出
  のみで running を戻さず、`stop()` も bg 分岐でしか running を戻さない＝**前景の違反チェック/改善探索を停止
  すると実行中表示が固着**。各サイトに seq ガード付きリセット（新チェックによるキャンセル= seq不一致では
  触らない＝後続の running=true を壊さない）＋ stop() に前景リセット分岐（running/fixSearching。最適化ジョブ
  自身の keep-best ハンドラとは冪等）。fixJob は seq を持たないため stop() 側で fixSearching を戻す。
- **[文言限定] c3n「証明」の強さを区別**: ForbiddenDiag の run 判定を3段階に—全セル PINNED=「本人希望どおりの
  並びが禁止パターンを構成」（辞書式 pref9000>c3n7000 の下で証明相当・従来文言維持）／それ以外の塞がり=
  「単独変更・玉突き連鎖・隣接日調整のすべてを検証して不成立＝崩せる見込みがありません」（全空間の数学的
  証明ではないため断定回避）。Watchdog「壁と証明」→「構造的な壁と判定」・EarlyStop 注記も同旨に限定。
- **[AUTO帯統一] 二重分岐の解消**: `autoAlgorithmForBudget` の旧 31-90=ALNS がアプリ経路
  （optimizationPlan=31-210 RSI→ALNS複合）と食い違い直接APIだけ別挙動だった→ 31-210=RSI（複合の主段・
  RSI偶数ラウンドは内部でALNSも回る）へ統一。テスト2ファイルの帯アサーション更新＋31/90/210 の固定を追加。
- **[JNI hardening] nativeCreateProblem を checked cursor 化**: cons/c3/bucket の count/len を「非負かつ
  残り要素数×行幅以内」（積は除算形で overflow 回避）で検証し、違反時はハンドル生成拒否（0=Kotlinへ安全
  退化＝監査#7 sgrp検証と同じ契約）。旧: 負 len の (size_t) 変換で巨大 reserve→C++例外がJNI境界越えで
  クラッシュ・巨大 count が0埋め大量 push で異常確保になり得た（正規 serializer 経路では発生しない防御）。
  JNI 部は MAGI_HOST_TEST 除外でホスト検証対象外のため、**最小 jni.h スタブで -fsyntax-only 全体構文検査**
  を実施（新検証手法）。ホスト parity harness も合成297万手 mismatch=0 で回帰なし。
- **[README同期] 最終更新 3.213.0→3.284.0**（自ら定めた「目次が古いと信頼が崩れる」ルール違反状態を解消）。
- 検証: ホストJVM全324テストgreen（AUTO帯変更に伴う既存アサーション2ファイル更新込み）。
- **不採用を進言し維持**: 仮説/並列分離（3.225.0ユーザー決定）・minRunMs見直し（UI経路で実害なし）・
  後処理レジストリ化（3.275.0 façade で集約済み・全面再配線は3.254.0で退行実績）。
- **未裁定（ユーザー判断待ち）**: total優先の辞書式による厳密ピン割れ（3.283.1節参照）。

## 3.283.0実機検証＝5連リリース全機能の実効確認＋フェーズ行全滅の自己回帰を修正（3.283.1）
新実機ログ（2026-12・同一データ・3.283.0ビルド）で 3.279.1〜3.283.0 の全機能が初めて同時に動いた。
- **実効確認（全部発火）**: ①ForbiddenDiag=アリフ Cｱ→Aｱ の両セル「希望固定」＝**本人希望どおりの並びが
  禁止パターンを構成**する構造壁と名指し ②Watchdog行「最終改善=189s・停滞38s・c3n壁=短37s・発火=あり・
  壁と証明」→ EarlyStop 251s＝**48秒節約** ③HF63共有=round1から deprioritize c3n・全8ワーカーの要約に
  HF63回避欄 ④再配属 0→9〜12/役割5-6種回転/エポック数適正化・グローバル最良更新 4回→7回(190sまで改善持続)
  ⑤件数・場所併記/c1内訳=breakdown一致/HF67フォールバック1手発見。**total 196→178(-18)**。
- **[3.283.1] フェーズ行全滅の自己回帰を修正**: 3.283.0 の同名60秒窓が、未出フェーズの番兵
  `?: Long.MIN_VALUE` による `wallElapsed - Long.MIN_VALUE` の**負オーバーフロー**で初出判定が恒偽＝
  通常フェーズ行が0行になっていた（意図は20行台。実機ログ14件が実証＝重要行のみ残存）。null判定へ是正
  （初出は常にログ・以後60秒窓）。Python の64bit演算で恒偽を実証してから修正。UI層は host コンパイル不可
  ＝この回帰はテスト網の外だった（実機ログが唯一の検出手段だったことも記録）。
- **[設計どおりだが業務判断が要る観測] total優先の辞書式が厳密ピンを割った**: 今回盤面は total -18 だが
  weighted soft は 879→1117(+238) と悪化（low 4→6・high 0→3、うち吉江の休 lo==hi=10 が 9 に・桒澤の休が
  11 に＝**前回は充足していた厳密ピンが2つ割れた**）。isBetter の第2キーが total(生カウント) のため、重い
  low/high を犠牲に軽い c3 を13件減らす取引が「改善」になる既知の性質。探索(SA/ALNS)には
  exactPinRegression(3.256.0=研磨パス限定) が無いため防げない。対処は業務判断（受容/最終番兵へのピン比較
  追加/データ側で吉江の休を9-10へ緩和）＝ユーザー裁定待ちとして記録。
- **[新診断候補・未実装]** アリフ事例の一般化=「隣接日の本人希望ペアが cons3n に完全一致」する矛盾は
  事前診断に存在しない（実現不能希望チェックは canDo のみ）。希望×禁止連続の SanityCheck が候補。

## ログ観測性の強化＋スパムログ対策（3.283.0, ユーザー指示「ログ解析出来ない箇所はログ強化する。スパムログ対策する」）
本セッションの実機ログ解析（2026-12）で「コード推論に頼るしかなかった穴」3つと、実測されたスパム源1つに対応。
**全て表示/ログのみ＝スコアリング不変**。
- **[スパム対策] 操作ログのフェーズ行を同名60秒窓で抑制**（`MagiViewModel`）: 適応ポートフォリオは
  V5 SA→RSI→ALNS を数秒周期で循環し、遷移ごとのログで操作ログ68件中約60件がフェーズ行＝読込/完了/改善等の
  重要イベントがリングから押し出されていた。同名フェーズ（数字を#に正規化: "ALNS restart 1/2"と"2/2"は
  同一視）は60秒に1回まで。「最良更新」「改善」を含む行は情報価値が高いため窓の対象外（常時ログ）。
  状態は実行ごとにリセット（関数ローカル）。推定効果: 60行→20行台。
- **[ログ強化①] ウォッチドッグ遠隔測定行**（`V6FinalPort`・`[I] Watchdog:`）: 非発火時も「最終改善=経過Xs・
  探索終了時の停滞Ys・実効閾値(通常=長270s／plateau=短37s／c3n壁=短37s)・発火=有無・c3n壁診断の結果」を
  1行出力。旧: 発火時の EarlyStop 行のみで「なぜ発火しなかったか」が読めず、2026-12ログの150s無改善×
  発火なしの切り分けが code 推論頼みだった穴を埋める。
- **[ログ強化②] ポートフォリオ要約へワーカー別HF63学習結果**（`V6NativeOptimizer`）: `AdaptiveWorkerOutcome`
  に `hf63Avoided` を追加し、要約の W別欄へ「/HF63回避=c3n+…」を併記。旧: 勝者以外のワーカーの runRsi 診断
  ログは破棄され、W1/W2 が何を諦めた/学んだかが不可視だった（3.281.0 のエポック横断共有の効果検証にも必要）。
- 検証: ホストJVM全324テストgreen（表示のみ・既存テスト無変更）。実効果（フェーズ行削減・Watchdog行・
  HF63回避欄）は次回実機ログで確認。

## 新領域のログ並列監査＝適応ポートフォリオ改善判定の恒真化ほか一括修正（3.282.0, ユーザー指示「新領域もログ解析する。不具合など修正する。コスト無視する」）
実機ログ（2026-12・PORTFOLIO 300s）の未レビュー面を4本の並列監査（違反詳細件数整合/適応ポートフォリオ配分/
修復パス計数・入口採否/文字化け修復）で精査し、各指摘を自分でコード照合してから一括修正。
- **[最重要/CONFIRMED] 適応ポートフォリオの改善判定が恒真化＝再配属・強度昇圧・役割回転が実質死んでいた**:
  `improvedThisEpoch = better(result, startReport)` の startReport が **escape系ロールの破壊摂動済み入口盤面**
  （globalBest を意図的に壊した盤面）のため、keep-best のロール実行はほぼ必ず入口に勝ち improvedThisEpoch が
  恒真化。plateau 再配属（`!improved && stagnantEpochs>=1`）が全 escape ロールで到達不能・intensityFor=
  reassignments/2 の強度昇圧も役割回転も発動しなかった（実機ログの独立傍証: W2 が凍結 globalBest を35エポック
  同一役で摂動し続け再配属0・~8s/epoch=improving量子が常時選択・グローバル改善は150s ゼロ。3.271.0 の
  「35エポック継続は設計どおり」判定は摂動入口比較を見落としていた＝訂正）。**修正**: エポック開始時点の
  自己エリートを基準線に `improvedThisEpoch = better(eliteReport, preEpochEliteReport)`（入口盤面採用・ロール
  結果採用の両方が eliteReport 経由で反映）。escape ロールが摂動入口に勝っただけでは改善と数えない＝文書化
  された契約（「改善が止まったら6種の脱出役を回す」）どおりに復元。keep-best/採否ゲートは不変＝品質退化なし。
  副次: roleRuns 集計を quantum<=0 break の後へ（実行しないロールが summary に1件多く載る off-by-one 解消）。
- **[表示整合] 違反詳細ヘッダ「(N件)」の件数が breakdown と食い違う混乱**（c1 11↔12・c3n 2↔1・c42 7↔8 等）:
  全て計数バグではなく表示意味論の乖離（ヘッダ=最重クラスで解決済みのセル位置数 vs breakdown=fire数。
  c1 は窓ごと inc だが mark はrun先頭のみ・c3n は1 fireでも全セル mark・軽い族は重い族に同一セルを奪われ
  位置ごと消える）。emit に fires(breakdown) を併記し、異なるときは「件数F・場所N箇所」と明示。
  **c1内訳（3.227.0）は第3の計数意味論だった実バグを是正**: 旧実装は mark と同じ `!prevViol`（run先頭のみ）
  計上で、ラン長>1 のとき breakdown と食い違った（3.227.0 の意図は breakdown と突合できる全件）。checker の
  inc と同一の「違反窓ごと」計上へ＝合計が常に UnifiedCheck の c1 と一致。
- **[予算ガードの対象漏れ] HF67 に専用締切**: 兄弟 HF66 は 2.65.0 以来の deadlineMs＋内側 outOfTime 確認を
  持つのに HF67 は手ごと shouldStop のみ＝候補ごとフル check の内側スキャンとフォールバック全ペア×全日
  総当たり（実機 rollback=264 の正体＝264回のフル check 全不採用）が締切後も走り切る非対称。deadlineMs＋
  内側確認を追加し、呼出側に hf66Cap と同型の hf67Cap（残り予算の半分・上限3s）を配線。
- **[誤警告] 文字化け修復の BOM 誤ラベル**: `repair()` は BOM 除去だけでも新 String を返すのに、呼出側が
  参照比較（`!==`）で「修復した」と判定＝**健全な BOM付きUTF-8 ファイルで毎回「二重エンコードを自動修復」と
  誤警告**。`wasDecoded(original, repaired)`（BOM を除いた本文の変化のみ true）を新設し JSON/CSV 両呼出を
  置換。警告文に「元のファイル自体は修復されません（保存し直すと次回から出ません）」の案内を追加。
  併せて: docstring の CP1252 過大主張を訂正（>0xFF ガードで構造的に修復対象外・安全側に不修復）・
  実質恒偽だった `before` カウントを `after==0` へ単純化・importCsvSmart のフォールバックが未修復 rawText を
  渡していた非対称を修復済みテキストへ。
- **[表示] パリティ µs 計測の誤誘導**: t0 が flatten（Kotlin側コピー）より前で「C++ 87µs vs Kotlin 47µs」と
  ネイティブが遅く見えていた（実スループットはホストベンチ ×1.9〜2.2）。flatten を計測外へ＋ログに
  「単発・JNI往復込みの参考値」の限定句。
- **監査で健全確認（変更なし）**: 入口修復の HARD 15→12/total+75 採用=辞書式 better() の正しい帰結・
  HF80 SO の3サイクル完走=漸増摂動の設計どおり・エポック量子の偏り（RSI 5/8s vs RSI++ 35/45s）=意図的・
  需給/上下チェック行の整合・Mojibake 修復は元ファイルを書き換えない（app内autosaveのみ＝破壊なし）。
- 検証: ホストJVMで**全324テストgreen**（新規6件=HF67締切2・wasDecoded系3・件数場所表示1、既存c1内訳
  テストは新意味論=窓計上へ更新）。golden/real bench とも既知baseline一致・pin 0。適応ポートフォリオの
  改善判定はロール scheduling のみの変更＝スコアリング不変・keep-best不変（bench 不能につき原理採否）。

## 停滞脱出レビューで確定した2欠陥の修正＝c3n構造壁の動的床＋HF63エポック横断共有（3.281.0, ユーザー指示「停滞脱出は賢く適切かログ解析してコードレビューする」→「新しい不具合を修正しマージする」）
実機ログ（2026-12・PORTFOLIO 300s・c3n=1残存）とコードの突合レビューで確定した2件を修正。内側の停滞脱出
（HF80 E10・CombinatorialRepair無駄打ち回避・JointLNS停滞/期限停止）は全て設計どおり機能と確認済み。
- **[A] 外側ウォッチドッグが「非covU HARD残」で実質発火不能だった欠陥**: effStall選択（V6FinalPort）は
  `bestHard<=hardFloor && 非covU HARD==0` のときだけ短い stallHardMs(37.5s) で、c3n が1件でも残ると常に
  stallMs=予算9/10(270s)＝300s予算では発火に270s無改善が必要で**構造的に発火不能**（実機: 125s以降150s
  無改善のまま探索275s完走・追加精製0）。covU には structuralHardFloor という「解けないHARD」の静的判定が
  あるのに c3n には無い非対称が根本原因。**修正**: 残る非covU HARD が **c3n のみ**（groupViol=0/pref=0）
  かつ covU が構造床到達（bestHard<=hardFloor+nonCovU）で、停滞が stallHardMs を超えた時点で
  **ForbiddenDiag(3.280.0) を遅延実行**（best世代ごと1回・~20ms・`V6NativeOptimizer.liveBest` 盤面）し、
  **全 run 塞がりを証明できたら** plateau として stallHardMs へ移行（`effectiveStallMs` 純関数へ抽出・
  テスト5件）。証明つきのため誤発火なし・早期終了は時間/電池の節約のみ（keep-best＝品質不変）。
  EarlyStop ログに「（残る必須=禁止連続はForbiddenDiagが構造的な壁と証明済み）」を明示。
- **[B] HF63/E9 の学習がエポック境界で毎回破棄されていた欠陥**: `val hf63 = Hf63Infeasibility()` が
  runRsi 呼出ローカルのため、適応ポートフォリオの短いエポック（rounds=2→effortIters=2500/round、かつ
  round1頭は lastFocus=null で加算なし＝**1エポックの学習は2500のみで threshold 5000 に構造的に届かない**）
  では何十エポック繰り返しても deprioritize が永久に成立しなかった（実機: W1x32+W2x35=67エポックが毎回
  c3n へ2ラウンドずつ突撃）。**修正**: runRsi/runRsiPlus に `sharedHf63: Hf63Infeasibility? = null` を追加し、
  `runAdaptivePortfolio` がワーカー専属インスタンス（エポック横断・ワーカー間は非共有=役割多様性を汚染
  しない・ワーカー内は逐次実行=並行アクセスなし）を注入。2エポック目で threshold 到達→以後の focus 選択が
  c3n を回避し SOFT へ振り向く。**共有の健全性**: `gBestCurV` は全期間min のため、エポック間の摂動で族の
  件数が一時的に増減（1→3→1）しても self-correction は誤発火せず、真の改善（全期間min更新）でのみ
  リセット（テストで固定）。既存呼出（runMultiWorker 経由等）はデフォルト null＝挙動不変。
- 検証: ホストJVMで**全318テストgreen**（新規7件=effectiveStallMs 5件＋HF63クロスエポック2件）。
  focus選択/停滞閾値のみの変更＝スコアリング不変・keep-best不変（bench は RSI focus を模擬できないため
  3.74.0/3.95.0/3.169.0 と同じ原理採否）。
- **[軽微・未対応]** phaseGrace(7.5s) と8ワーカーのフェーズ文字列交錯（3-10s間隔）による発火ジッタ数秒は
  A の修正で実害が消えるため据え置き。

## 禁止連続(c3n)の「なぜ崩せないか」診断＝ForbiddenDiag新設（3.280.0, ユーザー指示「実装する、実装コスト無視する」）
実機ログ（2026-12データ・PORTFOLIO 300s）で c3n=1（アリフ Cｱ→Aｱ）が HARD 専任ワーカー計67エポックでも
不動だったのに、「構造的に不能」か「探索漏れ」かをログから判別できなかった穴への対応。covU/covO には
CoverageDiag があるのに c3n には対の診断が無かった非対称を解消（ConstraintMus=3.272.0 も希望×需要専用で
c3n 非対応だった）。**読取専用・スコアリング不変**。
- **`V6PortAnalyzer.diagnoseForbiddenRuns`新設**: checker の forbidden 窓完全一致と同一意味論で違反 run を
  列挙（重複ルール=DuplicateSeq 由来の同一 run は1件に集約）し、各セルの脱出可否を HARD 意味論で厳密分類:
  - **FREE**=安全な代替あり（c3n 行fires正味減=`C1DeltaPrefilter.staffC3nFires`共用・pref非悪化・離脱covU穴
    なし）＝適用すれば HARD が厳密に減る＝isBetter は必ず採用＝**探索未到達の可能性**という本物のシグナル。
  - **CHAIN**=離脱元が covU 化するが `findCovUChain`（探索本体と同一関数・8 seed）で埋め直せることを**実証**。
  - **ADJACENT**=代替が全て新たな禁止連続を作るが `tryFixForbiddenRunViaAdjacentDay`（同・探索本体の関数）で
    崩せることを実証（隣接日の手＋本セル変更を適用した盤面で離脱covU穴まで連鎖確認）。
  - **PINNED**=本人希望どおりのセル（動かすと pref9000>c3n7000 で isBetter が正しく却下＝設計どおりの固定。
    希望が設定されていても現在破っているセルは固定扱いしない=screenCell と同じ正味 pref 判定）。
  - **BLOCKED**=全代替が「新たな禁止連続」か「covU受け皿なし」＝内訳件数つき。
  run 単位の hint は 3.263.0 の教訓（「玉突きが必要」と楽観的に言うだけでは壁を誤解させる）どおり、
  CHAIN/ADJACENT は探索本体の関数で成立を実証してからそう名乗り、全塞がりは「この希望・担当のままでは
  どう組んでも残ります（希望固定: 日付列挙）」と正直に案内。
- **配線**: `analyzeParallel`に第5の並列パス（c3n>0 のときのみ算出）→ `Analysis.forbiddenDiag`→
  `UiState.forbiddenDiag`＋v6Logs へ `[W] ForbiddenDiag:` 行（エクスポートされるMAGIログに載る）。
  UI=`ForbiddenRunDiagnosisCard`（MagiDashboardCards、CoverageDiagnosisCard と同じ作り・ホームの同カード
  直後に配置・「崩せない=赤/崩す手あり=青」チップ＋セル別分類＋hint）。
- 検証: ホストJVMで**全311テストgreen**（新規5件=FREE/PINNED構造壁/CHAIN実証/受け皿なし壁/ADJACENT+FREE
  同居 run。ADJACENT テストは隣接日調整の成立を手でトレースして設計）。実データ（real_state 2026-08）へ
  c3n run を注入した実形状駆動で正しい分類・20ms・c3n=0 時は完全 no-op（4ms）を確認。
  `C1DeltaPrefilter.staffC3nFires`は診断との共用のため private→internal 化。

## 3.279.0セルフレビュー指摘5件の後始末（3.279.1, ユーザー指示「コードレビューする」→「修正する」）
genshijin-review形式でPR#85（3.279.0）をセルフレビューし、🟡1＋🔵4を特定して修正。**全306テストgreen＋
挙動中立を実証**（同条件runでworking==HEADの研磨結果が完全一致。golden benchのrun間揺れ288/99↔289/96は
HEAD自身も示す既存の非決定性＝JointLNS系の壁時計予算(8s/6s)由来で本修正と無関係と切り分け済み）。
- **[A] `screenCell`の全盤面コピー排除**: `normalizeSchedule`のO(S×T)コピー→読み取り時の局所`cell(i,j)`へ
  （読むのはstaff行1本＋day列1本のみ）。`cell`は`getOrNull(i)?.getOrNull(j) ?: 0`＝normalizeScheduleと
  **同一意味論**（欠損セル→0=休パディング・範囲外値→-1）を厳守。当初`schedule[i][j]`直読みで書いたが
  S×T未満の不揃い盤面でAIOOBE＋欠損セルの意味が異なると自己検証で発見し出荷前に是正。
- **[B/🟡] `applyC1IndexChainRepair`のsilent cap解消**: 採用上限`maxPasses*32`到達を黙って打ち切っていた→
  `capHit`フラグでログに「採用上限N到達=打ち切り」を明示。
- **[C] screenedの延べ計上を明記**: Index再構築のたび同一候補を再判定し重複計上される→ログを
  「prefilter除外(延べ)N」表記へ（実装は変えず表記の正直化）。
- **[D] `solveWindow`のtriedShiftをBooleanArray化**: HashSet<Int>のboxing/hashをDFS最深部から排除。
  multisetには正規化由来の-1があり得るためindexは`sh+1`（[-1,K-1]→[0,K]、`s`=normalizeSchedule済みを確認）。
- **[E] `focusResidualOf`のfi<0 dead guard**: 現行構造では到達不能（mはv.staffを先頭に構築）だが、将来の
  m構築変更に備えた防御として残置し、その旨をコメントで明示（0=「壁と主張しない」安全側）。
- 残バックログ（指示待ち）: C1-11 best-effort関与職員選抜（要grilling）。

## 外部レビューC1-01〜C1-12の検証と修正（3.279.0, ユーザー指示「不具合など修正する」）
外部レビュー12件（対象=3.277 main）を1件ずつ検証（P0の2件は反例をホストJVMで実行して実証）し、推奨5件を修正。
事実関係は約90%正確・優先度はC1-03/04（provenWalls=本番未配線のテスト専用診断）で過大と評価。C1-05/06は3.278.0
（PR#83）で先行修正済みだった。**全300テストgreen＋golden/real両データで研磨結果がbaselineとバイト一致**（純粋な
正しさ/実効性修正・クリーン盤面では挙動不変・スコアリング不変）。
- **[C1-01/02/12] `screenCell`を全HARD族の正味Δ判定へ全面書き換え**: 旧はper-familyの存在判定
  （`makesForbiddenRun`=true／wishLocked希望外→無条件HARD_REJECT）で、①新しい禁止連続を作りつつ既存を壊す
  c3n正味0以下の手 ②既に希望違反中のセルを別シフトへ変えるpref不変の手、まで落としていた（両方とも
  screenCell=REJECTかつchecker isBetter=trueの反例を実行実証）。新実装は単一セル変更のgroupViol/pref/c3n
  （行fires差分）＋covU到着側のΔを厳密計算しΔ>0のみ却下＝「HARD_REJECT⇒checkerが必ず却下」契約がsoundに。
  **[実装中に発見した相互作用]** covU離脱側（正項）を含めるとbranch(b)玉突き連鎖で埋め直す前提の候補まで
  事前排除され連鎖経路が死ぬ（テストで回帰検出）→離脱側は意図的に除外（正項の省略=under-reject方向＝契約維持）。
  座標境界チェック（C1-12）も追加。
- **[C1-03] `focusResidual`を対象窓のみへ**: 旧は焦点職員の全ルール×全窓（別シフトのルール含む）の残数＝対象窓が
  解消可能でも別窓が残るだけで「この窓は壁」と誤認（provenWallsのfalse positive）。対象窓（v.ruleIndex/v.start）
  の残fire（0/1）の全葉最小へ。既存wallテスト2件は単一窓構成のため意味論変更後も互換（green確認）。
- **[C1-04] `provenWalls`のseenを窓単位へ**: 旧はstaff×shiftキーで同一職員・同一シフトの複数不足窓の最初の
  1窓しか証明探索せず真の壁を見逃し。staff×ruleIndex×startへ（回帰テスト=1窓目解消可能・2窓目真の壁の構成で固定）。
- **[C1-08] `applyC1IndexChainRepair`を採用直後Index再構築へ**: 旧はpass開始時のindexを採用後もstale走査
  （解消済み窓の再処理＋新規窓は次passまで不可視）。1手採用ごとに窓ループを抜け最新盤面から再構築。
  終了保証=isBetterの厳密改善＋採用上限maxPasses*32の安全弁。
- **[C1-10] `solveWindow`にtriedShift**: 多重集合スロット列挙の同値部分木重複を職員ごとシフト値1回に排除
  （node予算浪費→不必要なexhaustive=false化を解消）。
- **[C1-07] `expectedGain(staff,day)`→`(staff,day,targetShift)`**: 同日別シフトのgain混合を解消（診断API）。
  分離検証テスト追加（X gain1／Y必要2でgain0が別々に引ける）。
- **[不採用を進言し維持] C1-09の`newC1<=currentC1`採否制約**: PR#82の決定論実験で「この操作への採否ポリシー制約は
  3方針すべてbaselineをc1で上回れない」と実証済み＝契約文言の明確化のみで対応（採否はisBetterのまま）。
  **[非バグ確定] day1≤0乖離疑い**=`Problem.cons1`構築時の`d1>0&&d2>0`フィルタで到達不能。
  **[保留] C1-11のbest-effort関与職員選抜**=新規探索設計＝要grilling。

## 敵対監査で実証した2クラッシュ＋正しさ/実効性バグの一括修正（3.278.0, ユーザー指示「新しい論理的な不具合などを見つける」→「すべて修正する」）
本体精読＋並列3監査（EliteIntegration系/runPostOptimization配線/TemporalFlow・Hungarian・DP系）で発見した全件を修正。
ホストJVMで**全296テストgreen**＋クラッシュ再現ハーネス3種の解消確認＋**golden/real両データで研磨結果がbaselineと
バイト一致**（＝純粋な堅牢化・クリーン盤面では挙動不変・スコアリング不変）。
- **[CONFIRMED CRASH #1・最重要] `MinCostAssignment.solve` 全INF行→`p[-1]` AIOOBE**: ポテンシャル`v[j]`は単調非増加(≤0)
  のため、ある職員の行が全列INF（＝当日のどのslotも担当不可）だと`cur=INF−u−v≥INF=minv初期値`で厳密更新が構造的に
  一度も起きず`j1=-1`のまま`p[-1]`を読む（**決定的**）。引き金は正規データ＝**担当可否が全て未チェックの群の職員が1人**
  いるだけで、`handleOptimize`フル経路(12秒予算)がクラッシュ（5秒だと探索が予算を食い尽くしクラスタskipで偶然無事＝
  「短い予算では動くのに長いと失敗」という不可解な症状）。-1センチネル残存データ（実データで実測）でも同型。
  兄弟実装`minCostPerfectAssignment`(手M用)はnull fail-safe装備済みなのにこちらだけ欠けていた非対称。
  **修正**: `solve`を`IntArray?`化し`j1==-1`でnull返し、呼出2箇所(`applyDayAssignmentPolish`/`applyAlternatingSoftPolish`)
  は`?: continue`でその日をskip（keep-best不変・退化不能）。
- **[CONFIRMED CRASH #2] `C1RepairAnalysis.opportunities` が-1セルで`covUCell(-1,…)`＝`need1[-1][j]` AIOOBE**:
  `hasActionableC1`ゲートと`applyC1IndexChainRepair`の両経路で再現（3.270.0と同型の取り残し。オペレータ本体は
  `old in 0 until p.K`ガード済みなのにAnalysis層だけ欠落の非対称）。**修正**: 範囲外セルからの離脱は除去項0として扱う。
- **[正しさ/実効性3件]** ①`C1JointLnsPolish:326`のDP提案オラクルlocked判定だけ生`wish>=0`（3.264/3.270のwishLocked統一
  retrofit漏れ第3サイト）→`wishLocked`へ ②`EliteIntegrationPolish.fuseGroup`が1セルの候補全滅で`break`し残り全セルの
  融合を放棄→そのセルだけ`continue`でskip ③PORTFOLIOのHARD=0早期終了が勝者含む全ワーカーを停止し実行可能データで
  残り数百秒のソフト研磨予算を放棄（`runMultiWorker`の「勝者は自予算でソフト研磨継続」契約と衝突）→勝者だけ
  deadline まで継続する条件へ（`hardZeroWinner.get() < 0 || == i`）。
- **[PLAUSIBLE→修正]** 適応ポートフォリオのepoch開始処理(adaptiveEpochStart/check/register/共有採用)がtry外＝例外1つで
  全ワーカーのkeep-best成果ごと`optimize()`が失敗し得た（`runMultiWorker`は隔離済みの非対称）→epoch本体全体をtryで包み
  例外はそのワーカーのみ停止・現エリートを成果として返す。**[PLAUSIBLE→非バグ確定]** day1≤0退化cons1ルールの
  チェッカー/ゲート乖離疑いは、`Problem.cons1`構築時の`d1>0 && d2>0`フィルタで両者とも到達不能＝乖離なし。
- **[軽微7件]** `opportunities.gain`の2計算誤り（`lo`を`v.windowDays`固定→ルールごと`c.day1`基準へ＝同一シフトの
  長い別ルール窓の切捨て解消／`z<day2`→`z==day2-1`＝1手で実際に解消する窓のみ計上）・手Mに「希」自由生成ガード追加
  （兄弟実装と同じ方針の対象漏れ）・手Mの-1トークン日を事前skip（trials上限128の浪費防止）・RangePolish pass0の
  アンカー陳腐化（groupTargetsループが盤面変更済みなら再検査）・SoftPolishVerify「対象なし」判定にCyclicSwap対象族
  (c2/c41/c42/c41s/c42s/covO)を追加・後処理タイミングログに「最終検査+HF70」区間追加（区間合計=総時間に）・
  `applyC1IndexChainRepair`の`if (improved) continue`デッドコード除去。
- **[デッドコード除去]** `HypothesisDiversityPolicy.algorithmFor`（本番呼出0・実割当は`AdaptiveHypothesisEpochPolicy`側）
  ・`HypothesisEpochAssignment.safetyFloor`（計算されるだけで未読）＋対応テスト2件を整理。
- eliteLogsのstale（入口盤面がエリート/グローバル採用されてもログ未更新→古いロール実行のフェーズログが最終表示に付く）
  も採用時に同期するよう修正。
- 経緯記録: 直前のPR#82(c1研磨採否guard)はユーザー判断でclose（決定論テストでどの採否ポリシー変種もhard/c1のcleanな
  純改善にならず、hard=6を守る3.277現状維持が最良と裁定。guardコードは閉PRの履歴に保存）。

## c1Deltaをload-bearing化=exact net c1 deltaへ格上げし候補順位付けへ接続（3.277.0, ユーザー選択「c1Deltaもload-bearing化」）
3.276.0でscreenCellは実駆動したが`c1Delta`は`-index.expectedGain`の薄いラッパー＝本番未使用のまま残っていた。
ユーザー選択を受け、単なる経路差し替えでなく**c1Deltaを意味的に格上げ**して load-bearing 化。
- **`C1DeltaPrefilter.c1Delta`をexact net deltaへ書き換え**: 旧 `c1Delta(index,staff,day) = -expectedGain`（gainのみ
  の近似）→ 新 `c1Delta(p,schedule,staff,day,newShift)` = 「(staff,day)→newShiftとしたときの**その職員のc1 fire数の
  正味増減**」（newShift追加で解消するgain **と** 旧シフト除去で新たに割れるlossの両方を厳密勘定・負=改善）。
  c1はper-staffのため単一行clone＋全cons1窓走査(checker同一意味論)で厳密計算。**expectedGainは旧シフト除去のloss
  を見落とす**（旧シフトもc1制約を持つと自己破壊を過小評価）欠点をc1Deltaが補う。
- **`applyC1IndexChainRepair`の候補順位付けを接続**: `sortedByDescending{index.expectedGain(staff,d)}` →
  `sortedBy{C1DeltaPrefilter.c1Delta(p,work,staff,d,shift)}`（昇順＝最も改善する候補を先に）。旧シフト除去で別窓を
  割る自己破壊候補が後回しになる**賢い順序**。**順位のみの変更＝keep-best採否は不変**（採否は常にchecker+isBetter+
  exactPinRegression）。expectedGain/donorMarginはIndex APIとして温存（診断・図の要素、本番hot pathでは非使用）。
- 検証: ホストJVMで v6/model/root 実コンパイル・**全292テストgreen**（3.276.0の289＋新規3: net deltaが窓解消で負/
  自己破壊で正/no-opで0）。**敵対ファズ500件再走で退化0・入力破壊0・改善306件（順序変更前と同数）**＝ordering変更が
  keep-best安全であることを再実証。スコアリング不変（順位付けのみ・重み/採否/既存探索順序いずれも不変）・HF77非該当。

## index駆動C1修復オペレータ新設=screenCell/c1Deltaを実駆動する経路（3.276.0, ユーザー指示「接続する」→AskUserQuestionで「screenCellを新規オペレータへ」）
3.275.0でIndex/Prefilterを「検証済みだが hot path では hasActionableC1 のみ live・screenCell/c1Deltaは部品」と
した続き。ユーザー「接続する」→対象を確認（AskUserQuestion）→「screenCellを新規オペレータへ」＝**既存オペレータに
触れず、C1RepairIndex/C1DeltaPrefilter を実駆動する新規C1修復オペレータを新設**。手の種類もgrilling（AskUserQuestion）
で「玉突き連鎖付き」を確定。
- **`V6HotfixPasses.applyC1IndexChainRepair`新設**: ①`C1RepairIndex.build`で不足窓を索引化（不足の重い窓順）
  ②窓内候補日を`expectedGain`降順に並べ`C1DeltaPrefilter.screenCell`がNEUTRALの候補だけ試す（無変化/groupViol/
  pref破り/c3nは事前除外）③候補日を不足シフトへ直接移動、旧シフトを抜いてcovU穴が空くなら`findCovUChain`
  （exclude=本人＝3.158.0の自己選択防止）の玉突き連鎖で埋め直す（手Bと同型）④採否は必ず本物の
  `UnifiedViolationChecker`+`isBetter`(hard→total→weighted)+`exactPinRegression`(3.256.0の厳密ピン保護)＝keep-best・
  退化不能。maxPasses=2のフィックスポイント。
- **配線**: `C1RepairOperators.indexChainRepair`（façade 1:1委譲）を新設し、runPostOptimizationの既存
  `hasActionableC1`ゲート内（window opの直後）へ配置。厳密c1アンカー＝不足窓ゼロで内部no-op（before.c1==0で早期return）
  のためゲート内で安全。**既存オペレータ（手A/R1/R2/R3/B・flow・beam・exact・joint LNS）には一切触れない**。
- **正直な位置づけ**: 生成する手は既存の手B/beam/exactと重複しうる（keep-bestで無害）。主眼は「index駆動の候補生成
  ＋prefilter選別」という図のDelta Prefilter経路を**load-bearingにする**こと。golden_state単独測定で c1 115→98
  （採用5・連鎖1・hard 0→0非悪化・total 313→294）、**screenCellが1118候補を安く除外**＝Index/Prefilterが実際に
  効いていることを確認。フルパイプラインでは既存opとkeep-bestで協調するため純増は限定的（残差は3.263.0の
  構造的壁が支配的）。**退化不能**（新オペレータは改善手のみ採用・既存経路不変）。
- 検証: ホストJVM（kotlin-compiler-embeddable 2.0.21）で v6/model/root を実コンパイル・**全289テストgreen**
  （3.275.0の285＋新規4: 直接移動でc1解消/covU穴を連鎖で埋める/c1クリーンでno-op/façade委譲の挙動同一）。
  chain経路テストは「直接移動がcovU穴を作り却下→findCovUChainがs1:X→Yで埋め採用」を最小盤面で固定。
  スコアリング不変（重み・採否・既存探索順序いずれも不変、新オペレータ追加のみ）・HF77非該当。

## C1研磨アーキテクチャを図どおりに寄せる=Index/Operators façade/Delta Prefilter を新設（3.275.0, ユーザー提示のパイプライン図→「賢く実装する。実装コスト無視する」）
ユーザーがC1研磨の理想アーキ図（UnifiedViolationChecker→C1RepairAnalysis→**C1RepairIndex**→**C1RepairOperators**→
**C1 Delta Prefilter**→checker→keep-best）を提示。grep実測で「Analysis/両端は実在、中間3層は未実装 or 散在」＝
現状コード55-60%一致と回答→「賢く実装する。実装コスト無視する」。**grillingで配線方針を1問確定＝「加算的・
スコア不変」**（全面再配線は3.254.0で退行を理由に不採用にした"再dispatch統合"の危険が再来するため回避）。
- **新設3ファイル（全て純関数/読取専用 or 純委譲＝スコアリング不変・退化不能）**:
  - **`C1RepairIndex.kt`**（読取専用索引）: `C1RepairAnalysis`の出力をO(1)で引ける4ルックアップへ集約
    （dayToWindows / staffRuleWindows / expectedGain(staff,day) / donorMargin(staff,day)=そのc1シフトを抜いても
    壊れない余裕・>0=安全ドナー）。各オペレータが個別に再計算していた窓走査・ドナー余裕を1箇所へ。純関数
    （Problem×盤面）＝副作用なし。
  - **`C1DeltaPrefilter.kt`**（accept非変更の事前枝刈り）: `hasActionableC1(index)`=不足窓ゼロ判定 /
    `screenCell(...)`=単一セル候補のHARD_REJECT/NEUTRAL判定（**「checker+isBetterが確実に却下する候補」だけ落とす**
    ＝退化不能: 無変化・groupViol(canDo外)・希望を破るpref・c3n を HARD_REJECT。**希望"へ寄せる"候補は NEUTRAL
    ＝落とさない**のが安全性の肝）/ `c1Delta(...)`=順位付け専用(accept非ゲート)。
  - **`C1RepairOperators.kt`**（既存パスへ1:1委譲する内部façade）: selfRelocateAndSameDaySwap(=applyC1WindowPolish)
    /temporalFlow/wideBeam/exactWindow/jointLns を**順序・引数・採否を一切変えず委譲**＝挙動完全同一。
    3.254.0で不採用にした"再dispatch統合"には**しない**（委譲のみ）。共有前段 buildIndex/hasActionableC1/provenWalls も提供。
- **本番配線（runPostOptimization、挙動同一を厳守）**: C1系5呼出をすべて façade 委譲へ差し替え。
  **hasActionableC1 ゲートは applyC1WindowPolish(=selfRelocateAndSameDaySwap)のみに適用**＝これは c1違反セルに
  **厳密アンカー**するため不足窓ゼロなら必ず no-op（コード精読で確認・テストで固定）＝skipしても盤面byte一致。
  **他3op(temporalFlow/wideBeam/exact)は非gate**（temporalFlowは c1中立でも total改善のcoverage保存手を出し得る／
  exactは独自の内部c1==0ゲートを持つ＝一括skipは非安全と判明。安全側に倒し1opのみgate）。これがIndex/Prefilterを
  hot pathで実際に使う唯一のprovably-safeな地点。screenCell/c1Deltaは検証済み公開部品として提供（既存オペレータの
  内側first-improvement順序を変えぬよう per候補配線は見送り＝スコア不変最優先）。
- **正直な限界**: 図の「Delta Prefilter=候補ごとにoperator↔checker間で挟む段」の per候補版は、既存オペレータの
  first-improvement順序を変え得るため本版では未配線（screenCell/c1Deltaは部品として用意・新規オペレータ/診断用）。
  hasActionableC1のクラスタ前段ゲートは c1=0時に冗長な1回のchecker.checkを省く小さな最適化＝機能的削減効果は小
  （C1研磨の実削減余地は3.263.0で確認した構造的壁が支配的）。本変更の主眼は**構造を図へ寄せる（散在オペレータの
  cohesiveな置き場＋共有前段）**こと。
- 検証: ホストJVM（kotlin-compiler-embeddable 2.0.21）で v6/model/root を実コンパイル・**全285テストgreen**
  （3.274.0の271＋新規14: Index索引の手計算固定/Prefilterのaccept非変更5判定/façade委譲5本の挙動同一+gate安全性）。
  委譲は同一seedで direct==façade を assert、gateは c1クリーン盤面で applyC1WindowPolish が no-op(applied=0・盤面一致)
  を固定。スコアリング不変・HF77非該当（重み・採否・探索順序いずれも不変、構造集約と読取専用前段のみ）。

## 3.273.0のA4/診断3件を敵対監査で確認・修正（3.274.0, 実機ログ4fca3273→ユーザー指示「敵対監査」）
実機ログ（PORTFOLIO 300s×2, 最終 必須=4 合計=166）で 3.271.0（PersonalJointLNS 2591ms/total 174→166・
C1JointLNS 8006ms＝飢餓解消の実効確認）／3.272.0（MUS「証明つき」が福澤B4・8/1・8/6で発火）／3.273.0
（C1ExactRepair 実行・改善0＝best-effort予想どおり無害）が意図どおり動くことを確認。HARD=4（covU: Dﾃ×3+
Aｱ×1）は禁止連続/希望による構造的なもので不具合ではないことを追認。**そのうえで3.273.0の新規コード
（C1RepairAnalysis）を3本の並列サブエージェントで敵対監査し、自分でコード直読・独立検証して3件を修正**。
全て A4診断/表示のみ＝**スコアリング・勤務表・本番の `applyC1ExactWindowRepair`（patch/exhaustive のみ
使用＝元から正しい）に影響なし**。
- **[CONFIRMED, 正しさ] `C1RepairAnalysis.solveWindow` の focusResidual が未復元の rows から算出**:
  分枝限定 DFS の `place()` は下降時に `rows[mi][d]=sh` を書くが、バックトラックで復元するのは `used[si]`
  だけで **`rows` は復元されない**（探索後の `rows` は最後に辿った葉のまま）。旧実装は `bestArr = bestRows ?: rows`
  で bestRows==null（=元配置が既に最善）のとき**この探索ゴミ配置**から focusResidual を算出しており、
  `provenWalls`（A4=どう並べ替えても焦点を解消不能と厳密証明する窓）が**列挙順依存の false positive** を
  出しうる欠陥だった（加えて min-joint 配置の焦点残差 ≠ min-focus 残差＝そもそも別物）。**修正**: `baseline`
  算出直後に `focusResidualOf(arr)` ヘルパーを新設し、`var minFocusResidual = focusResidualOf(rows)`（=baseline）
  で初期化。葉コールバックで min-joint（`jointC1`）と min-focus（`focusResidualOf`）を**独立に**追跡し、
  探索後 rows に依存する `bestArr` ブロックを撤去、`ExactResult(best, baseline, patch, !budgetHit,
  minFocusResidual)` を返す。`exhaustive && minFocusResidual>0` が「coverage入替でどう並べても焦点はこれ以上
  減らせない」の**健全な**証明値になる。
  **[重要] 旧テストが欠陥挙動を固定していた**: `exactSolveProvesCoverageNeutralWall...`（3職員・各日X1個=計3個・
  i0が2要求）は**Xトークン3個あればi0は2個取れる＝壁でない**構成を、rows未復元バグ由来の false wall で「壁」と
  誤検出していた。単一トークン構成（窓内 X トークンが1個のみ・i0がX≥2要求＝真の壁）へ再設計
  （`...WhenTokensAreTrulyScarce`）。旧構成は**壁を出さないこと**を固定する回帰テスト
  `provenWallsDoesNotFalselyFlagWhenFocusIsCoverageNeutrallySatisfiable` へ転用。
- **[CONFIRMED, 表示] 需給サマリの covO/covU を月間差から日次 covCell 合計へ**（`V6SanityPort` 検査0）:
  過剰/不足を月間 `現状−需要`（`Σcnt` − `Σneed1`）で算出・ラベルしていたが、これは**毎日需要のあるシフト
  （Dﾃ等）でしか実 covO/covU と一致しない**。稀にしか need1 が無いシフト（B4: need1=0/need2=1 等）では
  「月間の過剰配置数」を covO 件数と誤表示していた（実機ログ「B4 過剰6(covO)」だが実 covO=1）。**修正**:
  日次 `p.covUCell(k,j,g)`/`p.covOCell(k,j,g)`（source of truth）の全日合計へ置換し、0 のときは note を出さない。
  golden_state で検証: **B4（需要0/現状25）・休（需要0/現状6）が covO note 非表示**（need2 が全て吸収＝実 covO=0）に
  なり、**Dﾃ（毎日需要）は正しく過剰4(covO)** を維持。
- **[CONFIRMED, 表示] 構造HARD下限=0 の文言が過大主張**: 「各シフトは担当者数で需要を満たせる＝データ起因の
  必須違反なし」は、covU が希望/禁止連続で構造的に発生する局面で誤読を招く（`structuralHardFloor` 自体は
  hardFloor 用途の担当者数ベースで正しく、文言のみが問題）。「担当者数の観点では各シフトが需要を満たせる。
  希望/禁止連続による構造的な人員不足は別途 CoverageDiag/設定ミス を参照」へ緩和。
- **監査で健全確認（変更なし）**: ConstraintMus（検査9）は並列エージェントが6項目全て健全（sound）と確認。
  本番の `applyC1ExactWindowRepair` は patch/exhaustive のみ使用＝focusResidual バグの影響を受けない
  （`provenWalls` はテストからのみ呼ばれる A4診断）。
- 検証: ホストJVM（kotlin-compiler-embeddable 2.0.21）で v6/model/root を実コンパイル・**全271テストgreen**
  （3.273.0の270＋回帰1）。C1RepairAnalysisTest 6件（再設計2件含む）green。需給修正は golden_state で
  実出力を目視確認。読取専用の診断・表示のみ＝HF77非該当・スコアリング不変。

## C1 Repair Analysis + 厳密窓修復（A1-A6, 3.273.0, ユーザー指示「A1からA6を実装する。コスト無視する」）
残提案一覧のA1〜A6を実装。「評価器は修復を考えない」を核に、C1の解析・修復・ディスパッチを分離。
- **[技術的再解釈] A3「CP-SATレーン」は OR-Tools でなく純Kotlin厳密ソルバで実装**（コストでなく検証可能性
  の判断: OR-Toolsは重量ネイティブ依存＝APK肥大・サンドボックスでAndroidコンパイル不可・実機まで検証不能で
  「checkerが正・番兵で照合」の契約と衝突。窓スコープの部分問題は小さいため分枝限定を純Kotlinで自己完結）。
- **新設 `C1RepairAnalysis.kt`**:
  - **A6 解析型**: `C1WindowViolation`（checkerの`inc("c1")`窓走査を忠実再現・全窓構造化）＋
    `RepairOpportunity`（候補日ごとのgain/coverageRisk/patternRisk/wishConflict＝探索前の局所情報）。read-only。
  - **A2/A3 厳密エンジン `solveWindow`**: 窓を含む日スパンで**coverage保存 permutation**（各日のシフト
    多重集合を関与職員M内で並べ替え＝covU/covO構造的に不変）を分枝限定探索し joint c1 を最小化。
    `exhaustive=true`（node予算内で全探索完了）のとき `minJointC1` は**証明された下限**（A3）。
    返り値patchは候補で、最終採否は必ず呼出側の checker+keep-best（予算超過時のbest-effortも退化不能）。
  - **A4 `provenWalls`**: `exhaustive && 焦点職員の残c1>0`＝「どう入れ替えても解消不能」とcross-staffで
    厳密に**証明**された窓（2b-2/MUSが扱わない実トークン希少性を全職員横断で勘定）。誤検知ゼロ。
- **A1+A2配線 `V6HotfixPasses.applyC1ExactWindowRepair`**（runPostOptimizationのC1BeamPolish直後）:
  解析駆動ディスパッチ＝証明済み解消不能スパンを (焦点,シフト,スパン内容ハッシュ) でmemoし二度解かない。
  採否は isBetter + exactPinRegression。
- **A5 native-parity fixture拡充**（`host_parity_bench.cpp` buildProblem）: 同一シフトに複数窓ルール
  （golden の「休 5日窓≥1 かつ 15日窓≥4」型＋勤務14日窓）を追加し、重複スライド窓のc1累積を
  scalar/bit両path照合に含める。合成2,996,665手 mismatch=0 を実測確認。
- **検証（ホストJVM実行）**: ①厳密エンジンが「単一same-day swapの合成では到達不能な多日多職員連動手」
  （2職員×4日・coverage固定でday1,day2双方swapが唯一解）でc1 2→0・coverage保存・exhaustive証明
  ②トークン希少構成（各日X1個・i0が3日窓X≥2要求）で`provenWalls`が解消不能を証明 ③修復パスが
  keep-best安全・入力不変 ④`runPostOptimization`全体に配線後も退化なし（実データ HARD 12→7・total
  255→187・入力不変）。**正直な限界**: 実データの休窓は5〜15日幅×多職員＝スパンが大きく node予算で
  best-effort（この盤面では厳密パスの追加改善0・17ms・無害）。真価は小窓クラスタでの厳密修復＋壁証明。
  テスト5件（`C1RepairAnalysisTest`）＋全270テストgreen。
- read-only解析＋探索オペレータ追加のみ＝重み・スコアリング・checker不変（HF77非該当・keep-best退化不能）。
- **未（v8第2段の残り・合意済み方向）**: 大窓での厳密性向上（スパン分割/より賢い枝刈り）・A4のSanityPort
  edit-loop配線（buildGuidance perf保護のため保留）は将来課題。

## Constraint IR + MUS＝矛盾の最小説明エンジン（3.272.0, v8構想第1段・ユーザー指示「あなたが賢く実装する。実装コスト無視する」）
設計議論（HyperGraph化の評価→「Constraint IR + CP-SAT診断レーン + MUS/IIS」への合意）を受けた実装。
合意した3つの護りを厳守: ①**IRは宣言的メタデータのみ**（`Item`はscopeとパラメータだけ・評価器を持たない
＝第4の意味論を作らない。IRのドリフトは「誤った診断」にしかならず「誤った勤務表」にはならない）
②**ホットパス不変**（探索/評価器に一切触れない・読取専用・スコアリング不変）③段階実装＝**依存ゼロの
第1段**（CP-SAT/OR-Toolsは導入しない。実機で検証不能な重量ネイティブ依存を避け、支配的な実ケースは
既存の厳密機構で証明できるため）。
- **新設 `ConstraintMus.kt`**: IR=`WishPin`（wishLocked準拠＝実現不能な希望は含まない）/`RangeCap`/
  `RangeFloor`/`WindowRule`/`DayNeed`（covUCellの意味論から逆算した実効下限）。**健全（sound）だが
  不完全な証明ルール**（発火＝真に矛盾・誤検知ゼロ、2b-2「false wallを出さない」と同方針）:
  職員スコープ=(A)シフト需要下限max(個人下限,窓の厳密最小日数=`minDaysForFullCompliance`,固定希望数)>個人上限
  (B)鳩の巣Σ需要>T (C)他シフト全上限による強制下限>上限。日スコープ=固定希望下の必要人数充足を
  **二部マッチング**（Kuhn法）で判定（クロス日制約を無視した緩和＝不能判定は健全）。
  **deletion-based MUS**で極小コアへ縮約→各メンバーが緩和候補（IIS相当の提案）。
- **配線=V6SanityPort 検査9**: 既存の手彫り検査（2b-3/6b/6c/検査3）はいずれも希望を扱わないため、
  **コアに希望を含む矛盾のみ**を出す＝重複ゼロの分担（希望なしコアはエンジンは検出するが表示は既存検査に
  委ねる、をテストで固定）。「次の◯件は同時に成立しません（証明つき）: …」＋「いずれか1件を緩めて
  ください（例: …）」の形式で既存SettingIssueチャネル（SettingIssuesCard）へ。
- **実データ検証（ホストJVM実行・アップロード済み運用state）**: ①**8/1(土)・8/6(木)** — 実機ログで
  CoverageDiagが「どう組んでも解消できません」としか言えなかったcovU 3件について、**どの希望5件×
  必要人数4件が同時成立不能かを名指しで証明**（例: 8/6=山本昌幸休・佐藤直美有・金沢勇輝休・モニカ希・
  アリフ希 × A4/Aｱ/Cｵ/Cｱ各1人）②**福澤俊陽** — B4上限1に対しB4希望2件＝希望だけで上限超過が確定
  という**既存検査が検出できなかった新種の矛盾**を発見（実機ログのhigh違反「B4回数3>上限1」の根本原因）
  ③モニカ（上限×窓・希望なし）はエンジンが検出しつつ表示は2b-3に委ねる分担が機能。
- **性能（重要な副産物）**: `minDaysForFullCompliance`（15日窓で数百msの重いDP）を`cachedMinDays`
  （プロセス全域ConcurrentHashMap・key=(T,ルール集合)の純関数）に集約し**既存2b-3も同キャッシュ経由へ**。
  buildGuidanceはセル編集ごとに走るため、実測 旧~2秒/毎回 → 初回2.2秒＋**2回目以降4ms**＝診断パス全体が
  従来より大幅に高速化。
- テスト7件（`ConstraintMusTest`）: 上限×固定希望・鳩の巣（下限+希望/窓+希望）・日別マッチング不能の
  各極小コアの**正確な構成**（極小性が一意になるよう手計算で設計）＋希望なしコアの表示抑制＋クリーン状態の
  無発火。全265テストgreen（ホストJVM）。
- 将来の第2段（未実装・合意済みの方向）: IRからのCP-SAT翻訳レーン（充足方向はchecker照合で機械検証可能）・
  職員横断MUS。read-only診断のみ＝HF77非該当・スコアリング不変。

## 実機ログの敵対解析＝共同LNS恒常飢餓の解消＋実行入口3種の穴を修正（3.271.0, ユーザー指示「ログ解析して新しい論理的な不具合などを見つける。敵対検証をする」）
実機ログ（PORTFOLIO 300s×2回, 最終 必須=3 合計=177）をタイムライン単位で敵対的に精読し、4件を修正。
- **[主修正: 共同LNSの恒常飢餓] `runPostOptimization`にクラスタ専用締切を導入**: 巡回研磨クラスタ
  （厳密日割当〜曜日平準化の約18パス）は自身の締切を持たず`shouldStop`（全体予算）だけで走るため、
  探索フェーズが予算を使い切る実運用では後処理予約枠(8〜25s)を丸ごと消費し、後段の
  C1JointLnsPolish/PersonalBalanceJointLnsPolishが**2本連続の実機ログで毎回「探索上限0=明示的に無効」**
  （3.264.0の按分は「残り」にしか効かず、残りが常に0だった＝実データでHARD削減実績のある2パスが
  本番では一度も走れない死に機能状態）。HF66と同じ考え方で、クラスタ開始時点の残予算の半分
  （上限14s=両LNS既定合計8s+6s）を共同LNS用に確保し、クラスタには`clusterStop`（自前締切つき）を
  渡す（18箇所置換）。クラスタが早期収束すれば共同LNSはより多く使える＝従来挙動と同一。全パス
  keep-bestのため時間配分の変更のみ＝退化不能。**実データ検証（ホストJVM実行）**: 旧実装で
  C1共同LNS=2ms/個人共同LNS=1ms(両方スキップ)だった5秒予算で、新実装は 巡回研磨2.4s＋C1共同LNS1.24s
  （c1 -2）＋個人共同LNS0.9s（**HARD 7→6**・personal 25→15・total -10）。実機の後処理予約枠相当の
  25秒予算では C1共同LNS7.1s（**HARD 7→6**）＋個人共同LNS1.4s（**HARD 6→5**）＝実機が毎回捨てていた
  HARD削減の回収を確認。
- **[実行中ガード漏れ] `generateSmartInitial`/`generateSimple`**: 実機ログに「19:56:41 最適化開始→
  19:56:48 初期解生成完了→19:56:50 最適化開始#2」という併走シーケンスを発見。UI上「初期解を作る」は
  「勤務表をつくる」の直下に隣接し、`!hasResult`状態で両方表示＝連続タップで最適化開始直後に
  初期解生成が併走可能。この2関数だけ`if (_ui.value.running) return`ガード（runV6FullOptimize/start/
  runSoftPolishは装備済み・3.161.0のセル編集ガードと同方針）が無く、`job`参照の無キャンセル上書き
  （走行中jobがstop()不能のゾンビ化）＋`currentSchedule`同時書き換えが起きうる。ガード＋案内文を追加。
- **[サイレント死の防止] 実行系5入口の一般Exception catchにlogOp追加**: 最適化(runV6FullOptimize)/
  高速計算(start)/軽量最適化/ソフト研磨/初期解生成/簡易作成の失敗catchは`message`更新のみで
  操作ログに残らず、書き出したログに「開始だけあって完了も停止も無い実行」が現れても死因を判別
  できなかった（今回のログの消えた実行#1の解析を実際に阻んだ）。`logOp("W", "…失敗: …")`を追加。
- **[幻の制約削除ログ] `removeConstraint`のindex検証**: 検証なしで先にログ→mutateのため、リスト縮小後の
  古いindex（連続タップ等）でも「制約削除: cons3mn[7]」の幻ログ＋無駄なundo/保存/再検査が走っていた
  （`without()`自体はno-op＝データは壊れないが、実機ログの「cons3mn[7]が2回削除」が実削除2件か
  幻1件かを判別不能にした実例）。範囲外は「制約削除を無視: …存在しません」のW logで明示して即return。
- **[外部レビュー7点の検証（receiving-code-review、同ログ由来）]** 提示された仮説を全てコード照合:
  ①「C1Polish採用0なのにSoftPolishVerifyでc1 65→57＝責務逆転」＝**誤読**（SoftPolishVerifyはパスで
  なくクラスタ全巡の集約ログ。各パス行は巡1のみ表示で、c1 -8は巡2-3の循環/c3/c3mn玉突き採用6手の
  族横断副作用＝フィックスポイント巡回の設計意図どおり）→誤読の根であるログ表示仕様を集約行に明記
  「(N巡・各パス行は巡1のみ表示/本行は全巡合計)」。⑦「Verifyなのに最適化」も同じ誤読。
  ②「HF67入口ゲートが早すぎ悪化→大幅改善を永久に失う」＝誤り（ゲートは探索シード選択のみ。
  悪化許容探索はSA/ALNS受理側が担当・keep-bestは最終番兵）。③「距離2エリート=多様性浪費」＝根拠
  不足（12エリート中1ペアが距離2。QUALITY層の近傍変種はfusionの主材料で、実際この実行の唯一の
  fusion改善に寄与しうる。ワーカー級の重複はDUPLICATE_DISTANCE_CELLS=2で既に再配属ガード済み）。
  ④「W2が35エポック同一役＝再配属条件が保守的」＝**事実と逆**（`shouldReassign`は無改善1エポックで
  即再配属＝攻撃的。35エポック継続は毎エポック自己盤面をbetter()で厳密改善し続けた＝設計どおりの
  役割継続）。⑤「TemporalFlowのflow失敗13/32=DP設計不足」＝設計どおり（DP=単一行の提案オラクル・
  Flow=coverage実現器の分業。3.254.0で5変種を実測比較して選んだ構成で、提案の~40%減耗は
  propose-and-checkの正常コスト）。⑥「C1Beam全敗=評価関数不一致」＝**旧情報**（3.251.0で
  (hard,total,weighted)へ修正済み・3.252.0でseed多様化と「実予算内に候補が存在しない」ことを
  実測確認済み）。→ 採用は①⑦のログ明記のみ・他は根拠を示して不採用。
- 検証: v6層（V6HotfixPasses）はホストJVMで実際にコンパイル・全258テストgreen・実データで上記の
  飢餓解消を数値確認。UI層（MagiViewModel）はブレース/丸括弧/角括弧均衡0の静的確認（Android依存の
  ためサンドボックスではコンパイル不可）。最終判定は CI（v6-engine-check の testDebugUnitTest／
  Release Build）。スコアリング/重み/採否ロジックは完全に不変（時間配分・ガード・ログのみ）。

## 新規論理不具合の並列監査＝3件を確認・修正（3.270.0, ユーザー指示「新しい論理的な不具合などを見直す」）
既存の広範な監査履歴（HARD/keep-best/番兵は健全と確認済み）を踏まえ、直近に追加・改修されたコード
（3.255.0〜3.269.0のJoint LNS/adaptive portfolio/C3mn/WeeklyRebalance周辺）を対象に、4本の並列
サブエージェントで「証拠のある論理不具合のみ」を報告する監査を実施。3件を独自にコード直読で検証・
修正、1件（movable/wishLockedの実効性不整合）は監査結果を受け全15箇所へ横展開した。
- **[CONFIRMED, クラッシュ] `C1JointLnsPolish.collectGoals`/`PersonalBalanceJointLnsPolish.collectGoals`/
  `buildCandidates` の無検証添字**: `normalizeSchedule`（MirrorCore.kt）は範囲外セルを**センチネル-1**
  へ写像する（3.199.0でC++側の同型バグを修正済みの、まさにその値）。これら3箇所は生の
  `schedule[i][j]`/`w[s][day]` を無検証のまま `IntArray(p.K)` の添字に使っており、-1セルが1つでも
  含まれる盤面（シフト削除後の残存index等、実運用で起こりうる）で`ArrayIndexOutOfBoundsException`が
  即座に発生し`runPostOptimization`全体を巻き込んで落ちる。同ファイル内の兄弟関数
  （`hasExclusiveAptViolation`/`personalPenaltyByStaff`）は既に`if (k in 0 until p.K)`ガード済みで、
  今回の3箇所だけが見落とし。`if (k in 0 until p.K)`ガードを追加（範囲外セルは該当集計から除外）。
  ホストJVM実行で、範囲外index(5, K=2)を含む合成盤面を用意し、修正前提の再現（クラッシュする設計だと
  コードトレースで確認）→修正後は例外なく完走し実際にc1違反を解消することまで確認。
- **[CONFIRMED, 正しさ] `applyC3mnPolish`/`applyWeeklyRebalancePolish` の `exactPinRegression` 抜け**:
  3.256.0で「同日/複数職員の割当を入れ替える」型の研磨パス全般（C1Polish/AptPolish/FairPolish/
  RangePolish/C3RunPolish/C3PatternPolish/C3SequencePolish/BlockRotationPolish/
  AlternatingSoftPolish/DayAssignmentPolish/CombinatorialRepair等）へ`exactPinRegression`
  （staffRangeの厳密ピンlo==hiが`isBetter`のtotal優先(hard→total→weightedScore辞書式)によって
  widened=悪化する退行を防ぐガード）が横展開されたが、`applyC3mnPolish`（3.214.0新設）と
  `applyWeeklyRebalancePolish`（3.197.0新設）の2つだけ対象から漏れていた。両者とも1手で複数の
  違反セルが同時に動く構造（c3mnの任意長run解消・weeklyの2職員×2日長方形交換）を持ち、`total`
  （重み無視の生カウント合計）だけが大きく改善してisBetterを通過しつつ、実際のweightedScoreは
  厳密ピン破りで悪化しうる——3.256.0が解消した不変条件破りの穴がこの2箇所にだけ残っていた。
  両関数の候補適用直前に`workBefore`スナップショットを追加し、`isBetter(...) &&
  !exactPinRegression(p, workBefore, work)`のパターンへ統一（既存13箇所と同一パターン）。
- **[CONFIRMED, 実効性] `movable`/`wishLocked`の不整合を全15箇所で統一**: `V6HotfixPasses.kt`の
  ローカル`movable(i,j)`定義が全15関数（RangePolish/C1WindowPolish/C1BeamPolish/BlockSwapPolish/
  C3mnPolish/AptPolish/FairPolish等）で一様に`p.wish[i][j] < 0`（希望の有無のみ）を使っていたが、
  正しい判定は`Problem.wishLocked`（`w>=0 && canDo(i,w)`＝**実現不能な希望は凍結しない**、
  3.183.0でLightMirrorOptimizerの同型バグを修正した際に確立した設計原則）。実現不能な希望で
  埋まったセル（例: 担当不可シフトへの希望のせいでgroupViol化したセル）が、この不整合のため
  15箇所どれからも一切動かせず座礁していた。全箇所を`!p.wishLocked(i, j)`へ統一（`isBetter`/
  `exactPinRegression`が最終ゲートのため候補が広がるだけで退化不能）。
  **この修正で`FlexibleDayFlowTest.fixedIllegalCellIsNotMoved`が失敗**——精査した結果、このテスト
  自身が旧バグの回帰テストになっていた（希望先"Aｱ"はvictimがcanDo=falseの実現不能な希望で、
  「希望固定Aｱは保持」という旧assertionはまさに座礁を検証していた）。テストを
  `infeasibleWishForTheIllegalShiftDoesNotBlockTheFix`へ改名し、正しい期待値（実現不能な希望は
  ロックにならずgroupViolセルが正常に修復される）へ更新。
- 3件とも探索/検査ロジックの修正のみ＝重み・目的関数・最終採否(isBetter/exactPinRegression自体)は
  不変。クラッシュ修正は安全性のみ、exactPinRegression追加とmovable統一は退化不能（keep-best）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ホストJVM（kotlin-compiler-embeddable 2.0.21）で
  実際にコンパイル・既存258件のテスト実行（1件をFlexibleDayFlowTestの是正込みでgreen）＋実データ
  （アップロード済み運用state）でのC1JointLnsPolish/`runPostOptimization`実行・クラッシュ再現用の
  合成盤面での確認を実施。最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 後処理タイミングログの「HF66」誤表示を修正（3.269.0, 実機ログ精読で発見）
ユーザー提示の実機ログ（PORTFOLIO 300s, HARD 4→3・total 181→177）を精読し、`C1JointLNS`/
`PersonalJointLNS`（3.268.0で改良した2パス）が「探索上限0=明示的に無効」で毎回スキップされていた
理由を追跡。原因自体（後処理予算超過時は `deadlineMs - t` が0以下になり各パスの `maxMillis<=0`
ガードが正しく発火＝設計どおり）は問題ないが、追跡の過程で**別の実バグ（診断ログの誤表示）**を発見。
- **発見**: `runPostOptimization` の「後処理タイミング」ログ（`HF80=… HF67=… HF66=…`）は
  `t80`/`t67`/`t66`/`tHf` の4点しか計測しておらず、**`HF66=tHf-t66` が実際には「HF66本体＋厳密日割当＋
  c1/c3/range/apt/fair の4巡フィックスポイントループ＋曜日長方形交換＋交互最適化＋グループ/曜日平準化＋
  C1共同LNS＋個人共同LNS」を全部まとめて『HF66』と誤表示していた**（HF66自身は`t66+hf66Cap`で内部
  ≤6sに自己制限済みのため、実機ログの「HF66=22181ms」は実際にはこの巨大な後続クラスタの所要時間で
  あり、HF66単体の値ではない）。パイプラインが多数の研磨パスを追加してきた結果、このログだけが
  古い区間分割のまま取り残されていた（HF77「コメント≠実装」と同種、今回は「ログのラベル≠実測対象」）。
  実機ログのC1JointLNS/PersonalJointLNSの「探索上限0」自体は、この巨大クラスタ＋前段の探索フェーズ
  （275秒/300秒）で後処理予算が使い切られた結果であり、原因の特定にこの区間分割の粒度が不足していた。
- **修正**: HF66呼出直後に`t66Done`を追加し、既存の`tC1Lns`（C1共同LNS直前）/`tPersonalLns`（個人共同LNS
  直前）/`tHf`（個人共同LNS直後）と組み合わせて、「HF66本体」「巡回研磨(厳密日割当+c1/c3/range/apt/fair+
  曜日/交互)」「C1共同LNS」「個人共同LNS」の4区間へ正しく分割表示。新規タイムスタンプは`t66Done`の
  1個のみ（他は既存変数の再利用）。ホストJVM実行で実データに対し `runPostOptimization` を実行し、
  新しいログが「HF80=342ms HF67=15ms HF66=50ms 巡回研磨=14598ms C1共同LNS=3ms 個人共同LNS=1ms」の
  ように意味の異なる区間が重複なく分離表示されることを確認。
- 表示専用の診断ログ修正のみ＝スコアリング/探索/採否ロジックは完全に不変。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝ホストJVM（kotlin-compiler-embeddable 2.0.21）で
  実際にコンパイル・既存258件のテスト実行（green）＋実データでの新ログ出力確認を実施。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## C1JointLnsPolish/EliteIntegrationPolishの2件を賢く改良（3.268.0、外部レビュー評価の続き＋ユーザー指示「賢くアルゴリズムをし直す」）
3.266.0で統合した適応的仮説ポートフォリオ（EliteIntegrationPolish）と、外部の敵対的コードレビュー
（C1JointLnsPolish等5戦略を評価、「何%正しいか」への回答としてレビュー時に洗い出した具体的な指摘のうち、
未対応のまま残っていた2件に対応した。いずれも**正しさ(最終採否)には影響しない効率/優先度の改善のみ**
＝`isFinalCandidate`/`better()`/`exactPinRegression`によるkeep-best自体は完全に不変。
- **[C1JointLnsPolish, 効率] `generateMoves`にc3n(禁止連続)事前フィルタを追加**: 全Move種
  （Direct/SameDaySwap/Rotate3/SelfDaySwap/CrossDayTransfer）の共通効果「staff iのday jにshift x
  を置く」がその時点で`p.makesForbiddenRun`により禁止連続を作ると判明する場合、従来はdebt予算＋
  最終ゲート（isFinalCandidate/defensive re-check）だけに頼ってそのまま候補化していた（正しさは
  常に保たれる設計だが、hard debtを消費するだけの無駄な候補がmaxMovesPerGoalの枠を埋めていた）。
  各Move種の生成直前で`p.makesForbiddenRun(schedule, staff, day, shift)`を確認し、真なら生成を
  即座に諦める（候補化しない）。事前フィルタで見逃しても最終正しさは無関係（フィルタ自体は効率のみ）。
- **[EliteIntegrationPolish, 効率] relink/fusionのセル優先順位をc1優先の3段階へ拡張**: 新設
  `c1Cells(report)`（`cellFamilies`から"vio-c1"を含むセルを抽出。`violations`=1セル最重1クラスのみ
  だと、c1がより重い違反（c3n等）と同一セルで重なった場合に取りこぼす＝3.205.0のC1Polish anchor選定
  で発見済みの「anchor-shadowing」と同型の穴を、ここでも回避する設計）を新設し、`relinkOnePath`の
  diff並べ替えと`fuseGroup`のセル選定を「c1関連セル→他の違反セル→それ以外」の3段階
  （旧: 違反セル→それ以外の2段階）へ拡張。`maxFusionCells`予算が逼迫する局面で、より重要度の高い
  c1関連の不一致セルが優先的にビームへ入るようにする（効果は探索の質のみ・正しさは不変）。
- **検証**: host-JVM実行（本セッションで確立済みの手法）で両修正を含む全ソースを再コンパイルし、
  既存255件＋新規追加分の全258件（`C1JointLnsPolishTest`に禁止連続回避の直接検証1件を追加、
  `EliteIntegrationPolishTest`に`c1Cells`のシャドーイング耐性を直接検証する2件を追加、いずれも
  internal公開はテスト可視性のためのみ）が green であることを確認。実データ
  （ユーザーがアップロードした実運用state, `42350787-magi_state_1784677480923.json`）でも
  C1JointLnsPolish単体実行（8秒予算）で HARD 12→10・total 255→233・c1 78→75 の真の改善を確認、
  EliteIntegrationPolishのfusion/relinkも退化なし（`AdaptiveEliteArchive.better`によるregression
  チェック=false、report parityも一致）で完走することを確認。
- 探索オペレータの効率・優先度の改善のみ＝重み・スコアリング不変。最終採否は既存のkeep-best
  （hard→total→weighted辞書式＋exactPinRegression）が担保するため退化不能。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝ホストJVM（kotlin-compiler-embeddable 2.0.21、
  3.251.0で確立した手法）で実際にコンパイル・テスト実行して確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## destroyRepair系のsoft-aware修復にweekly/fairを統合（3.267.0、ユーザー指示「残作業と残不具合と残提案を最適化する。実装コスト無視する」）
ユーザー指示を受け、CLAUDE.md の散在する「未対応/据え置き」記述を棚卸し。多くは既に後続版で解消済み・
または明示的にユーザー同意待ち（E5「月全体の俯瞰」・満足度尺度の式変更・V6DashboardCard/AttentionCards
の重複統合）で、同意ゲートはコスト度外視でも越えるべきでない（据え置き継続）。実装コストのみが障壁だった
真に開いている項目のうち、最も具体的でスコープの明確な1件（3.170.0で「対応するには大きな改修が要る」と
明記されたまま残っていた項目）に対応した。
- **背景**: `destroyRepairDayAt`/`destroyRepairStaffAt`/`destroyRepairViolations`（RSI/ALNSのdestroy-repair
  候補生成の中核、2.57.0-2.59.0のsoft-aware化以来 low/high/apt/c41 の marginal cost で候補を選ぶ）は、
  3.169.0/3.170.0でRSI focusの選択対象にapt/weekly/fairを追加した際、「apt は`staffCountPenaltyAt`に
  既に織り込み済みだが、weekly(曜日バケット)/fair(群平均)は対応するには`weeklyDevOfBucket`/
  `DeltaEvaluator.fairDevAt`相当の統合が要る、より大きな改修」として意図的に見送られたまま残っていた
  （focus選択で狙いは定まるが、実際の候補生成コストが依然low/high/aptのみを見ているため、weekly/fair
  狙いのラウンドでも最適でない候補しか作れない）。
- **実装**: `weeklyMarginalAt(wd, bucket, delta)`（wd=職員の曜日別非休日数7要素、checkerと同一の
  `weeklyDevOfBucket`を使い bucket 位置の ±1 変化による偏差差分を計算）と
  `fairMarginalAt(p, i, k, delta, counts, grpTotal)`（群 g=`p.sgrp[i]` のシフト k における、checkerと
  同一のL1偏差（`round(群合計/メンバー数)`からの差の和）の変化を計算。m<2または k が群の担当外なら0）を
  新設。3関数それぞれに、既存の`c41DayMarg`と同型の「呼出前に一度だけ集計を構築(wd/grpTotal)し、
  候補ごとに参照するだけ」のパターンで組み込んだ。`destroyRepairStaffAt`は fair のためグループ全体の
  月間合計が要るため、従来staff i専用だった`cntI`を全職員S×Kの`counts`配列のエイリアス
  （`val cntI = counts[i]`、同一配列参照＝どちらの名前で更新しても他方に反映）へ変更。
- **安全性**: これら3関数はいずれもRSI/ALNSの「候補生成」専用ヒューリスティックで、最終採否は常に
  `UnifiedViolationChecker`の実再評価＋`isBetter`/`better()`のkeep-bestが担う（本関数のdocstring
  「受理(SA/isBetter)が最終採否=安全」のとおり）。したがって marginal cost の計算に万一誤りがあっても
  結果が悪化することはなく、候補選択の質が下がるだけ＝退化不能。
- **検証（ホストJVM実行、レビューを鵜呑みにせず独立に数値検証）**: `weeklyMarginalAt`/`fairMarginalAt`が
  返す値が、実際の`UnifiedViolationChecker.check()`のweightedScore差分と厳密に一致することを、
  cons1/cons3/pref/groupViol/covU/covOが一切発火しない構成（low/high/apt/fair/weeklyのみが寄与し得る
  合成state、職員数2-9・日数5-24・シフト数2-4・群数1-2をランダム生成）で検証。ホストJVM上で976件の
  単一セル変更を検査し**mismatch=0**を確認してからテストへ固定（`WeeklyFairMarginalTest.kt`、
  `marginalDeltaMatchesFullCheckerAcrossRandomStates`が80状態×5変更=400試行超で同じ検証を再現）。
  既存の全252テスト（V6NativeOptimizer.kt改修後の再コンパイル・再実行）もgreenのままであることを確認。
  `weeklyMarginalAt`/`fairMarginalAt`は他の内部関数（`rsiGenerateHypothesis`等）と同じ理由で`internal`化
  （ユニットテストから直接呼べるようにするため。`staffCountPenaltyAt`/3関数本体は既存どおり`private`のまま）。
- 探索の候補生成ロジックの拡張のみ＝重み・スコアリング不変。最終採否は既存のkeep-bestが担保するため
  退化不能。実データでの効果（weekly/fair focusラウンドの研磨効率向上）は次回実機ログで確認
  （bench はRSI destroy-repair内部のcost関数を模擬できないため3.74.0系と同じ原理採否）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ホストJVM（kotlin-compiler-embeddable 2.0.21）で実際に
  コンパイル・テスト実行して確認。最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 適応的仮説ポートフォリオ＝8並列仮説が同一解へ収束する問題への対応（3.266.0、外部パッチ受領・検証のうえ手作業で統合）
ユーザーから大型パッチ束（3ファイル連続patch＋本体5ファイル＋テスト5ファイル、約3600行）を受領。
「8並列仮説を実行しても相異なる解=1件」（3.263.0で調査した停滞問題そのもの）に対処する
「仮説の入口盤面を多様化する(basin diversity)」＋「各ワーカーが自前のepoch時計で停滞/吸引域重複を
検知し役割を再配属する非同期適応ポートフォリオ」＋「全epochの盤面をエリートアーカイブに蓄積し
最適化後にPath Relinking+Fusionで統合する」の3段構成の新設計だった。

### 受領コードの検証（`receiving-code-review`規律・鵜呑みにせず1件ずつ照合）
- **3パッチとも`git apply --check`が失敗**（コンテキスト不一致）。patch2のhunkヘッダが
  `@@ -4,14 +4,11 @@ object V6NativeOptimizer {`など、実ファイル（数千行）とは整合しない断片から
  生成されたものと判明＝**パッチとしては適用不可**、内容を「意図の記述」として読み実ファイルへ手作業で
  反映する方針に切替えた。
- **patch3の生diffに`TODO()`挿入の地雷を発見**: `liveBestReport.set(null)`直後に
  `+        TODO()`が、周囲の実コード（dispatch本体）を一切カバーしないhunkとして混入していた。
  fuzzy適用されていたら`optimize()`が毎回`NotImplementedError`で落ちる致命的退行になり得た。
  実ファイルを直接読み、該当箇所にそのような行が不要なことを確認したうえで除外。
- **テスト`EliteIntegrationRandomSafetyTest.kt`が実在しない`MagiState(target)`単一引数コンストラクタを
  呼んでいた**（`MagiState`は全フィールド指定の1コンストラクタのみ）。実コンストラクタを使う形へ
  書き直して修正（`EliteIntegrationPolishTest.kt`の`state()`ヘルパーと同型のパターンを、任意次元の
  ランダム生成用に一般化）。
- **`scheduleDistance`の重複実装**: patch1が`V6NativeOptimizer`内に、patch3が
  `AdaptiveEliteArchive`内に、それぞれ独立実装していた。`AdaptiveEliteArchive.scheduleDistance`を
  唯一の実装とし、`V6NativeOptimizer.scheduleDistance`はそれへの1行委譲に変更（DRY化）。
- **本丸の欠落を発見**: patch1の`V6NativeOptimizer.chooseAlgorithm`変更だけでは、実際のAUTO予算
  ユーザーには一切届かない構造だった。本番のAUTO予算解決は`V6FinalPort.optimizationPlan(seconds)`
  （`chooseAlgorithm`とは完全に独立した別のディスパッチャ）が担っており、どのパッチもこれを
  書き換えていなかった＝新設計（特にpatch2/3のepoch適応ポートフォリオ＋エリート統合）は
  `V6Algorithm.PORTFOLIO`を明示指定しない限り本番で一度も起動しない、という致命的な配線漏れ
  だった（basin diversityはALNS/RSI/RSI_PLUSの各分岐に直接差し込まれているため本番に届くが、
  epoch適応＋エリート統合の方は`optimizationPlan()`側の修正が無いと死に機能のまま）。

### 統合（手書きで実装。各外部API参照を実コードと逐一突合）
- **新設4ファイル**（`HypothesisDiversityPolicy.kt`/`AdaptiveHypothesisEpochPolicy.kt`/
  `AdaptiveEliteArchive.kt`/`EliteIntegrationPolish.kt`）は精査後そのまま採用（自己完結・
  他ファイルへの依存も実在するAPIのみ）。
- **`V6NativeOptimizer.kt`**: `hypothesisStartFor`（W0/W4=安全フロアとして現行盤面のまま、他は
  destroy/repair系で入口盤面を構造的に多様化）を新設し、ALNS/RSI/RSI_PLUSの各`runMultiWorker`呼出の
  入口を`schedule.copy2D()`から`hypothesisStartFor(...)`へ差し替え。PORTFOLIO分岐は旧`portfolioAlgoFor`
  （各仮説に固定的に別アルゴリズムを割り当てるだけの単純協調）を撤去し、新設`runAdaptivePortfolio`
  （各ワーカーが`AdaptiveHypothesisEpochPolicy`の8役割を停滞/basin重複検知で動的に巡り、
  `AdaptiveEliteArchive`へ全epochの盤面を登録）へ置換。`chooseAlgorithm`の閾値解決を
  `HypothesisDiversityPolicy.autoAlgorithmForBudget`（≤30 V5/≤90 ALNS/≤210 RSI/それ以上PORTFOLIO）へ
  委譲。destroy/repair系3関数（day/staff/violations）のタイブレークにreservoir-samplingを追加（探索の
  決定的先頭バイアスを解消）。
- **`V6FinalPort.kt`**: `OptimizationPlan.RSIPlus`を`Portfolio`へ改称し、`optimizationPlan()`の
  211秒以上の分岐を`OptimizationPlan.Portfolio`→`V6Algorithm.PORTFOLIO`へ配線（**これが本丸の欠落の
  修正＝新システムが実際にAUTO長時間予算ユーザーへ届くようになる**）。旧「エリート再結合(Path
  Relinking)」epilogueを、`runAdaptivePortfolio`が蓄積した`AdaptiveElite`アーカイブ（無ければ従来の
  `lastAlternatives`へフォールバック）を使う`EliteIntegrationPolish.apply(...)`へ置換。ExtraRefine
  （微小予算追加精製）の退避/復元にも`lastFusionElites`を追加。
- **安全性は全段でkeep-best不変**: `EliteIntegrationPolish`の最終採否は必ず`UnifiedViolationChecker`の
  実再評価＋`better()`（hard→total→weightedScore辞書式）＋`exactPinRegression`（3.256.0の厳密ピン保護）
  を通過したものだけ。アーカイブされた報告は「信用しない」設計（ブリッジ盤面=一時的にHARD+1を許容する
  探索専用材料は直接returnされない）。

### 検証（ホストJVM実行で実際にコンパイル・テスト実行、規律どおり鵜呑みにしない）
- 本体4新設ファイル＋`V6NativeOptimizer.kt`/`V6FinalPort.kt`の統合後、全ソース（v6/model層）を
  ホストJVM（kotlin-compiler-embeddable 2.0.21、`-Xfriend-paths`でinternal可視性をテストsource setへ
  友好化）で実際にコンパイルし、既存248件＋新規5テストファイル分＝**252件全テストgreen**を確認。
- **受領テスト`EliteIntegrationPolishTest.kt`の1件が実行して初めて発覚した設計不備を発見・修正**:
  手計算だけでは見落としていたが、`check()`は常に全19族（fair/weeklyも含む）を評価するため、
  2職員同一群・単一勤務シフトの当初フィクスチャは"B単独の移動"がfair/weeklyの副次変化込みで
  実際に`weightedScore`改善になってしまい「単独では非改善」という前提が数学的に成立しなかった
  （covO=1.0が軽いため）。2群1名ずつ（fair対象外=m<2）・2勤務シフトX/Yで両半移動が必ず
  covU(HARD)を作る対称設計へ作り直し、ホストJVM実行で数値を確認してから反映（この種の盤面設計は
  必ず実行して検証する、という3.249.4以来の規律を継続適用）。
- 単体テストでない手動fuzzハーネス（`EliteIntegrationRandomSafetyTest`、`main`関数のみ・JUnit対象外）も
  ホストJVM上で直接実行し、**ランダム500ケースで regressions=0・inputMutations=0・reportMismatch=0**
  （改善478件）を確認。
- 検証: サンドボックスは通常のAndroid/Kotlinコンパイル不可＝上記のホストJVM実行が実質的な検証手段
  （3.251.0で確立した手法の継続適用）。最終判定は CI（v6-engine-check の testDebugUnitTest／
  Release Build）。実機での多様性向上効果（8仮説→相異なる解の増加）は次回実機ログで確認。

## Joint LNS予算按分の折半を既定比按分へ訂正（3.265.0、ユーザー質問「予算配分は適切か?」への自己検証）
3.264.0で追加した予算按分（HF66と同型の「残予算の半分を後段へ確保」）を、ユーザーの追加質問を受けて
自己検証した結果、この文脈には不適切と判明したため訂正した。
- **問題**: HF66(187行)の折半は後段に**巡回ループ全体(多数のパス)**を控えるため妥当だが、
  C1JointLnsPolish/PersonalBalanceJointLnsPolishの2パスでは、C1JointLnsPolishの後段に控えるのは
  **PersonalBalanceJointLnsPolish単体(既定6s)+HF70(安価・常時実行)のみ**＝文脈が異なる。
  境界値`remaining=14000ms`(=両者の既定合計)で検算すると、折半案はC1に7000msしか与えず自身の既定
  8000msに届かず、Personalは残り7000msのうち自身の既定6000msしか使わないため1000msが誰にも
  使われないまま終わる（折半がPersonalの実需要=6000msを知らずに一律半分を確保するため）。
- **修正**: 折半(`/2`)を既定比按分(`8:6`比、整数演算)へ変更。`remaining * 8_000L / 14_000L`
  （オーバーフロー回避のため`remaining`を先に安全な上限100秒へクランプ）。境界値検算:
  remaining=14000で厳密に8000（C1の完全な既定）、remaining=Long.MAX_VALUE(無指定時の既定)でも
  8000（従来どおり常に既定満額、オーバーフロー無し）を確認。Personal側の按分ロジックは元々
  「残り全部、自身の既定でクランプ」で後段に高コストな処理が無いため無変更（適切だった）。
- 予算配分ロジックのみの精緻化＝スコアリング不変・退化不能（各パスの`isBetter`/keep-best自体は無関係）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認、Python で
  境界値(0/1000/8000/14000/20000/10^9/Long.MAX)の按分結果を検算。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## 外部コードレビュー(C1JointLnsPolish/PersonalBalanceJointLnsPolish)の受領・検証・修正（3.264.0）
ユーザーが提示した外部レビュー（3.255.0を「本番投入可能」とした先行評価への反論）を、
`receiving-code-review` に従い鵜呑みにせず git diff＋現HEAD直読みで1件ずつ検証（何%正しいか回答）。
指摘の大半（約90%）が技術的に正確と確認、うち4件は現HEADでも未修正の生きたバグと判明したため修正した。
- **[検証で確認: 3.255.0原コミットは実際に exactPinRegression 欠如＝本番級の退行バグだったが、
  既に3.256.0で修正済み]** `git show b4295e4`で確認。「本番投入不可」という重大度づけは、この項目に
  関しては現在は成立しない（現HEADで3パスとも配線済みを確認済み）。
- **[修正1, C1JointLnsPolish] wishLocked不整合**: `singleRuleLowerBound`/`allowed()`が規約の
  `p.wishLocked(i,j)`（wish>=0 && canDo）でなく生の`wish>=0`のみでピン判定していた。実現不可能な
  希望が設定されたセルを誤って固定扱いし、①structuralC1LowerBoundを過大評価しうる（restartループの
  `best.c1<=lowerBound`早期終了が真の下限より甘く発火し得る）②候補生成(`allowed()`)がそのセルを
  事実上移動不能にし、有効なC1改善候補を生成できない、という2つの機会損失を生んでいた。
  C1TemporalFlowPolish（同型パス）は既に`p.wishLocked`を正しく使用しており、この不整合はC1JointLnsPolish
  固有だった。両関数を`p.wishLocked(staff,day)`基準へ統一。
- **[修正2, C1JointLnsPolish] targetSeenログの不整合**: 探索中に一度でも中間候補がtargetC1(50%目標)に
  到達すると`targetSeen`が恒久trueになり、その後better()がより良いがc1はtargetC1超の候補へbestを
  差し替えても「50%目標=到達」と表示され続けていた。ログを実際の返却盤面基準
  (`targetReached = chosenC1 <= targetC1`)へ修正。
- **[修正3, PersonalBalanceJointLnsPolish] primary/focusTotalゲートがdocstringと不整合**:
  docstring（18-20行）は「下限到達済みの違反は、同じ下限値の別配置が正式目的(better())を改善する
  場合だけ移し替える」と明記していたが、実ゲートは`focusTotal`の狭義減少(`<`)と、primary(最大gap職員)
  固有の狭義改善を要求しており、personal合計が同値のまま総合目的だけ改善する移し替えを機械的に拒否
  していた（docstringが約束する機能が実装上到達不能）。`focusTotal`を非狭義(`<=`)へ緩和し、
  `focus.all{...<=...}`（悪化なし）と重複するprimary固有ゲートを撤去（`primary`/`primaryGap`は
  他に用途が無いため削除）。isFinalCandidate・最終validゲートの双方に適用（前者を緩めないと探索中に
  bestへ選ばれる前に弾かれるため）。
- **[修正4, V6HotfixPasses.runPostOptimization] 予算按分なし**: C1JointLnsPolish/PersonalBalanceJoint
  LnsPolishの呼び出しが`shouldStop`のみを渡し、既定Config(8s/6s)の内部deadlineを外側`deadlineMs`の
  残予算と無関係に呼出時点から新規計算していた（同ファイルのHF66=187行は`deadlineMs`から残予算を按分
  する既存パターンを持つのに、この2箇所だけ未適用という内部不整合）。HF66と同じ考え方
  （残予算の一部を後段へ確保・絶対上限は既定値のまま）で`Config(maxMillis=...)`を按分注入。
  残0なら各パスの`maxMillis<=0`ガードで即スキップ（明示的disabled）。
- **[検証で確認: 指摘は正しいが問題ではない]** disallow(FlexibleDayFlowの`i==forcedStaff`限定)は
  他職員の被覆吸収に必要な正常動作。50%目標(config値)自体は終了条件でなく安全（探索は
  lowerBound到達/shouldStop/deadline/停滞まで継続）。配線順(Window→TemporalFlow→WideBeam、
  Joint LNS2種は巡回ループ外で最後に1回)も指摘どおり。
- 全4修正とも探索の機会損失・ログ精度・予算超過リスクの解消のみ＝`better()`/`exactPinRegression`による
  keep-best自体は不変（退化不能）。HF77非該当（重み不変、探索内部ゲート/予算配分の修正）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認、削除した
  `primary`/`primaryGap`/`targetSeen`への残存参照0を確認、両テストファイル（`C1JointLnsPolishTest`/
  `PersonalBalanceJointLnsPolishTest`）は公開API(`.apply(...)`)のみ呼ぶため署名変更の影響なしを確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 600秒改善ゼロの停滞を実データで解剖＝covU「玉突き」診断の楽観バイアスを修正（3.263.0, ユーザー指示「600秒で改善ゼロという停滞そのものを調査」「新たに深く網羅的に改善する」）
実運用ログ（300秒×2回、HARD=4/total=184で完全に足踏み・「仮説検証」8本中相異なる解=1件）を受け、
同じ実運用state（3.259.0以降で使用中のもの）に対しホストJVM実行で`SmartInitialScheduler.generate()`→
`V6FinalPort.handleOptimize()`を実際に2回連続実行（60秒×2、ネイティブ.so無しの純Kotlin経路）し、
実機と同型の停滞（HARD=4で完全に不変）を再現した。
- **結論: 残存covU(3〜4件)は真の構造的壁であり、探索のバグではない**。残存covUセルそれぞれに対し
  `findCovUChain`を200種類のseedで総当たりしたが**1件も解が見つからなかった**（BFS自体は決定的
  探索でseedは候補順の並べ替えにのみ影響するため、200通り全滅は「運が悪い」ではなく真に解が
  存在しないことの強い証拠）。原因を候補ごとに追跡した結果、当該日は**8名中5名が希望で固定
  （wishLocked）**しており、残る3名（直接移動候補）は全員「移動すると自分の現シフトが新たな
  covUを生む」ため単独では完結できず、depth2以降でその穴を埋められる人物が**1人も残っていない**
  （希望固定でない全員を使い切っても足りない）ことを確認。pref(重み9000)はcovU(重み8000)より
  重いため、希望を破ってまでcovUを直す手は`isBetter`が正しく却下しており**バグではなく設計どおり**。
  この「真の壁」が、8並列仮説が独立にSA/RSI/ALNSを回しても全員同一解に収束する（相異なる解=1件）
  現象も自然に説明する（他に良い解が存在しないため、多様な探索戦略も同じ到達点に収斂する）。
- **修正した実バグ（診断の楽観バイアス）**: `V6PortAnalyzer.diagnoseCoverage`のcovU「玉突き」
  （cascade）分類は「直接移動が別のcovUを生むか」という**1ホップ判定のみ**で、その先(depth2以降)が
  実際に埋まる保証を一切検証していなかった。このため「玉突き候補がいる」という診断が出ても、実際は
  上記のように**下流の全候補が希望固定で埋めようがない**ケースがあり、利用者に「もっと粘れば直る」
  という誤った期待を持たせていた。`findCovUChain`（探索本体と同一の関数、8 seed試行）を呼び分岐し、
  実際に解が見つかった場合のみ従来の「玉突き=ブロック移動が必要」を表示、見つからない場合は
  「玉突き候補${cascade}人はいますが、移動先の受け皿もすべて希望固定/禁止連続で塞がっており、現在の
  希望のままではどう組んでも解消できません。希望を1件調整するか担当を追加してください」という正直な
  案内へ切替える。`verdict`（FIXABLE/INFEASIBLE）自体は変更せず（希望を変えれば直る可能性は残るため
  恒久的な「データ上充足不可」とは区別）、reasonの文言のみを実態に合わせた。covOの同型「玉突き」判定
  （covU側と対称、V6PortAnalyzer内の別ブロック）は、covU側のような既製の検証済みチェーン探索関数が
  無く新規実装が必要になるためスコープ外とした（対象を今回確認したcovU側に限定）。
- **検証**: ユニットテスト2件（`V6PortAnalyzerTest.kt`）を追加。同一形状の最小盤面
  （X=covU対象・Aが唯一の直接候補でYを空けるとcascade・CがYを埋める唯一のdepth2候補）で、Cの希望
  有無だけを変え、①C未希望＝チェーン実在→従来の「玉突き=ブロック移動が必要」を維持
  ②C希望固定＝チェーン不成立→新しい「どう組んでも解消できません」案内に切替わる、の両方を固定。
  ホストJVM実行で既存230件＋新規2件＝232件全テストgreenを確認。
- 診断表示のみの変更＝エンジン/重み/探索ロジックは完全に不変（読取専用）。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝v6/model層はホストJVM実行で実際にコンパイル・
  テスト実行し確認（`V6FinalPort.handleOptimize`を含む実際の最適化パイプラインをネイティブ無し
  ＝純Kotlin経路で60秒×2回実行し、実機ログと同型の停滞を再現したうえでの検証）。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## 初期解生成のC1残差=真の構造的衝突と確認＋2b-3診断のfalse negativeを修正（3.262.0, ユーザー指示「初期解生成でC1違反をゼロにする」）
3.261.0の修正後、実データで残る c1=31 が「アルゴリズムの限界」か「真にどうしようもないデータ制約」かを
実データ検証で切り分けた。**結論: 全31件は4名の「休」個人上限(staffRange hi)が窓ルール充足に必要な
最小日数を下回る、真の構造的衝突**（アルゴリズムのバグではない）。ユーザーの「ゼロにする」という
指示をそのまま実装すると**総合的に悪化する**ことを実測で確認したため、コード変更ではなく事前診断
（2b-3）の精度改善で対応した（`receiving-code-review`規律: 提示された要求をそのまま実装する前に
技術的妥当性を検証する）。
- **切り分け実験**: 残存31件のうち20件(モニカ, rangeHi=6)は既存の2b-3診断で「壁」と正しく検出済み。
  残り11件（古泉健一/佐藤直美/上條洋平、各rangeHi=8-9）は検出漏れだったため、この3名の「休」上限を
  無制限に緩和して`SmartInitialScheduler.generate()`を再実行 → **c1は31→20まで解消**したが、
  **HARD=59→66・total=301→316と全体は悪化**（休を増やした結果、他シフトの被覆/回数制約が新たに
  壊れた）。high(重み45)>c1(重み15)、covU(重み8000)等はさらに重い、という既存の重み階層どおりの
  結果＝「C1を機械的にゼロへ寄せる」実装はしないのが正しい判断と確認。
- **修正した実バグ（2b-3診断のfalse negative）**: 3.229.0の個人内壁検知(2b-3)は「非重複窓の粗い下界
  (day2×floor(T/day1))」を使い、**同一シフトに複数の窓ルールがある場合も各ルールを独立に判定**していた。
  総当たり検証（`solveConstructionDp`をそのまま流用した`minDaysForFullCompliance`で多数の(day1,day2)
  組合せを試験）で、**複数ルールを同時に満たす真の最小日数は各ルール個別の下界の最大値を上回りうる**
  ことを確認（例: T=26「9日窓5回以上」＋「14日窓7回以上」は個別下界5,7で上限10なら旧判定は「足りている」
  と誤判定するが、実際の同時充足には14日必要）。古泉/佐藤/上條はまさにこの穴（複数ルールというより
  「上限が僅かに(1〜2)足りない境界ケース」で希望による日数圧迫も絡むため、真の必要量は個別下界より
  やや高い）を通り抜けていた可能性がある。
- **修正**: `SmartInitialScheduler`に`minDaysForFullCompliance(t, rules, seed)`を新設（構築本体の
  `solveConstructionDp`を無制限cap・全日自由で呼び、0違反を達成する最小日数を返す。0違反が原理的に
  不可能ならnull）。`V6SanityPort`の2b-3を、cons1を**シフト単位でグループ化**してから
  `minDaysForFullCompliance`で真の同時最小を求める方式へ置換（旧: ルールごとに独立判定）。
  読取専用の診断精度向上のみ・エンジン/重み/生成ロジックは無変更。
- **検証**: 既存3テスト（`personalC1WallDetectsWhenRangeHiBelowConservativeMinimum`/
  `personalC1WallDoesNotFalselyFlagBorderlineSatisfiableCase`/`personalC1WallIgnoresStaffWithoutPersonalCap`）
  は単一ルールのケースで新旧の値が一致するため無変更のままgreen。新規2件
  （`personalC1WallDetectsTrueJointMinimumExceedingEachRulesOwnBound`/
  `personalC1WallDoesNotFlagWhenCapMeetsTrueJointMinimum`）で、T=26の2ルール構成
  （個別下界5,7・真の同時最小14）を用い、旧ロジックなら見逃す上限10が新ロジックでは正しく壁と
  検出されること、上限14(=真の最小と一致)では誤検知しないことを固定。ホストJVM実行で既存228件＋
  新規2件＝230件全テストgreen。
- **ユーザーへの提示事項**: 実データで真にC1=0にしたい場合、モニカ/古泉/佐藤/上條の「休」個人上限を
  それぞれ引き上げる（改善後の2b-3診断が正確な必要最小値を提示する）か、窓ルール自体（5日窓/15日窓の
  回数・日数）を緩めるかの**データ側の判断が必要**（HF77＝明示数値指示が無い限りデータは変更しない）。
- 検証: サンドボックスは Kotlin コンパイル不可＝v6/model層はホストJVM実行で実際にコンパイル・
  テスト実行して確認。最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 初期解生成が「既に充足済みの盤面」でno-opになっていた実バグを修正（3.261.0, ユーザー実機報告「初期解生成後にC1違反になる。初期解生成が何度も出来ない」）
ユーザーが2つの症状を同時に報告。実データ(3.259.0で使った実運用stateをそのまま/白紙化せず投入)で
`SmartInitialScheduler.generate()`をホストJVM実行し、**根本原因は1つ**と特定した。
- **再現（修正前）**: `generate()`は入力`state.schedule`の充足率で「既存表ベース(そのまま保持)/
  空表ベース(ゼロから構築)」を切替える設計だった（`GreedyMirrorScheduler`から踏襲）。しかし
  ①**1回目の生成が完了すると盤面は必ず100%充足済みになる**ため、②**2回目以降の呼出は常に
  「既存表ベース」判定**となり、希望シード/C1/必要人数/個人下限/残り埋めの**全ステップが
  「空きセルが無い」ため丸ごとno-op**（実測: 1回目=既に100%充足済みの実運用stateを直接投入→
  `C1充足セル=0件・c1=78`＝白紙から作った場合(`c1=31`)より**悪い**。2回目=1回目の出力を再投入→
  1回目と盤面が完全一致=無変化）。**実運用データは読込直後から`schedule`欄が埋まっていることが多く、
  初回の生成ですらこの穴を踏み得た**（本セッションのユーザー報告はまさにこのケース）。
  「初期解生成後にC1違反になる」＝no-opにより盤面が改善されないまま返る、
  「初期解生成が何度も出来ない」＝ボタンを連打しても何も変わらない、の両方が同一原因から説明できる。
- **修正**: `generate()`から「既存表ベース/空表ベース」の分岐を撤去し、**常にゼロから
  (`Array(p.S){IntArray(p.T){-1}}`)組み立て直す**よう単純化。希望(`state.wish`)は盤面と独立に
  保持されるため常に白紙から組み立てても希望登録は失われない。呼出元(`generateSmartInitial`)は
  実行前に必ず`pushUndo()`する＝元の盤面へはいつでも復元可能なため、安全側の変更。
- **[3.261.0, 併せてUI修正] 「初期解を作る」ボタンが1回使うと消える実UI不具合**: `OperatorNextActionCard`の
  補助ボタン(helperLabel/helperAction)は状態ごとに1枠しか無く、`onSmartInitial`は`!ui.hasResult`
  （未作成）状態にしか配線されていなかった。`generateSmartInitial()`は完了時に`hasResult=true`を
  設定するため、1回使うと即座にこの唯一のエントリポイントが消え、二度と押せなくなっていた
  （前段のno-opバグと合わさり「何度も出来ない」を二重に悪化させていた）。カード末尾に
  `!ui.running && ui.hasResult`のときだけ表示する常設の小さなテキストリンク「初期解を作り直す
  （希望・C1優先の下書きに戻す）」を追加。既存の状態別helperLabel/helperActionは無変更。
- **検証**: 既存テスト`keepsExistingScheduleWhenMostlyFilled`（旧「既存表ベース」挙動を固定していた
  テスト、意図的な仕様変更のため撤去）を`rebuildsFromScratchEvenWhenInputScheduleIsAlreadyFullyFilled`
  へ置換（全11日を「休」で埋めた=C1違反する100%充足済み入力でもC1が解消されること・1回目の出力を
  再投入した2回目呼出でも同じ良い結果に到達すること=旧実装の完全な無変化と対比、の両方を固定）。
  ホストJVM実行で既存228件（1件置換）全テストgreenを確認。**実データでの効果測定**（同じ実運用
  stateを白紙化せずそのまま投入、ホストJVM実行）: 修正前 run1=`C1充足セル0件・c1=78`（no-op、
  白紙構築より悪い）→ 修正後 run1=`C1充足セル57件・c1=31・希望seed=81件`（白紙構築と完全に一致する
  正しい結果）。run2（1回目の出力を再投入）も同じ良い結果（c1=31）に到達し、旧実装のような
  完全な無変化は起きないことを確認。
- 探索・構築ロジックの単純化＋表示導線の追加のみ＝重み・スコアリング不変。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝v6/model層はホストJVM実行で実際にコンパイル・
  テスト実行して確認。UI層（`MagiDashboardCards.kt`）はAndroid依存のためサンドボックスでは
  コンパイル不可＝ブレース/丸括弧均衡・呼び出し側シグネチャ一致を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## AptPolish/FairPolishの自己振替が1パスにつき1単位しか解消できなかった欠陥を修正（3.260.0, ユーザー指摘「大島が違反研磨で来てない」）
ユーザーが3.259.0で使ったのと同じ実運用ログ/stateを指し「大島(愛)が違反研磨で来てない」と指摘。ログを
精読し、大島愛が RangePolish/AptPolish/PersonalJointLNS の**全パスで対象(残存)には毎回挙がるのに一向に
解消しない**ことを確認（例: `AptPolish: ...採用1回 対象:大島愛休 残存:...大島愛休,大島愛Pｼ`）。
- **実データでの状況確認**（実運用stateをホストJVM実行で検証、ログとの厳密一致は無いが同型の構造を再現）:
  大島愛は「休(実績17・目標11=超過6)」「Pｼ(実績9・目標19=不足10)」の**同時apt不均衡**を持つ、まさに
  AptPolish 手①(自己振替)が対象とすべき典型例。しかし1回の`AptPolish`呼出では**採用1回(=1日ぶんの
  振替)しか進まず**、超過6のうちの1しか縮まっていなかった。
- **根本原因（コードレベルで特定）**: `applyAptPolish`(および同型の`applyFairPolish`)の手①ループが
  ```
  for ((i, k) in highTargets) {
      var done = false
      for (k2 in 0 until p.K) {
          if (done) break
          ...
          if (trySelfSwap(i, k, k2)) { improved = true; done = true; ... }
      }
      ...
  }
  ```
  という構造で、`trySelfSwap`が**1日ぶんの付け替えを1回試すだけで即returnする**設計（`for j...{ if
  (applyAndCheck(...)) return true }`）にもかかわらず、外側が`done=true`で即座に**次のhighTargetsへ
  移ってしまう**ため、excess/deficitが複数単位ある職員は**1パスにつき1単位しか解消できなかった**。
  対照的に`RangePolish`のHIGHループは同種の状況に対し「同じ(i,k)が上限を複数回超えていても、この1パス
  内で上限まで反復して落とす」という明示コメント付きの`while`ループを既に持っており（3.244.0の手M/手F）、
  AptPolish/FairPolishの手①だけがこの反復設計から漏れていた。
  自己振替(手①)は**他者に一切影響しない自己完結の手**（本人の2シフト間の付け替えのみ）のため、
  `isBetter`が認める限り何度でも繰り返して安全（RangePolishの`while`ループと同じ安全性の根拠）。
- **修正**: `applyAptPolish`/`applyFairPolish`双方の手①ループを、`if (trySelfSwap(...))`の単発呼出しから
  `while (trySelfSwap(...)) { improved = true; done = true }`へ変更し、同一(i,k,k2)ペアで解消しなくなる
  まで（＝`isBetter`が改善と認めなくなるまで）反復するよう修正。あわせて`for (k2...)`の`if (done) break`
  ガードも撤去し、1つのk2候補で振替が尽きても**他のaptLow/fairLow候補にも順に振り分ける**よう拡張（例:
  大島のように複数のaptLow候補を同時に持つケースでも、1回のhighTargetsループで可能な限り多く解消する）。
  `done`フラグの意味は「手①で何かしら改善したか」のまま維持し、手②/③のゲート条件（`if (!done)`）は
  不変＝挙動の骨格自体は変えず、手①内部の反復回数のみを拡張した最小差分。
- **検証**: ユニットテスト`aptPolishExhaustsSelfSwapWithinSinglePassForMultiUnitImbalance`
  （`AptPolishTest.kt`。X超過3・Y不足3の1職員盤面で`maxPasses=1`固定→旧実装なら1単位しか解消できない
  はずが、修正後はapt偏差6が1パスで完全に0まで解消し`applied>=3`であることを固定）、
  `fairPolishExhaustsSelfSwapWithinSinglePassForMultiUnitImbalance`（`FairPolishTest.kt`。4人グループで
  1人がX超過・Y不足を持つ盤面、`maxPasses=1`でfair偏差8→0・`applied>1`を固定、ホストJVM実行で実測した
  値=fair8→0 applied=4 を反映）。ホストJVM実行で既存228件（3.259.0時点226件＋今回2件）全テストgreenを確認。
  **実データでの効果測定**（同じ実運用stateをホストJVM実行、`maxPasses=3`）: 修正後は**1回の`AptPolish`
  呼出だけで** apt breakdown 29→**17**（applied=6、旧実装のログでは1回のみでapt 30→28相当）、
  副次的にHARD 12→**9**・total 255→**249**まで改善（低deficitシフトへ寄せる過程で、彼女が構造的に
  担当不可(groupViol)なシフトからも同時に退出できたため。大島愛個人のカウントは
  {休17,Aｱ5,Pｼ9}→{休14,Aｱ2,Pｼ15}）。実データはstate/logのスナップショット不一致（3.259.0で既知）が
  あるため厳密な同一実行の比較ではないが、修正メカニズム自体（1パスで複数単位の自己振替を反復する）が
  実データ形状でも確実に機能することを確認した。
- 探索オペレータの内部反復回数の拡張のみ＝重み・スコアリング（`isBetter`自体）は完全に不変。最終採否は
  常に実チェッカーが担保するため退化不能。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝ホストJVM（kotlin-compiler-embeddable 2.0.21、3.251.0で
  確立した手法）で実際にコンパイル・テスト実行して確認。最終判定は CI（v6-engine-check の
  testDebugUnitTest／Release Build）。

## 初期解生成のC1が個人上限(rangeHi)を無視していた欠陥を修正（3.259.0, ユーザー実機ログ提示「初期解生成のC1は適切か?」）
ユーザーが実運用ログ(300s最適化・10職員/31日/11シフト)とその state を提示。事前診断（`V6SanityPort`検査2b-3）が
9名以上について「休/Dﾃの個人上限とC1窓ルールが構造的に衝突（窓ルールを満たすには個人上限を超える回数が必要）」と
警告していた実例を精読し、`SmartInitialScheduler.solveConstructionDp`の設計上の欠陥を特定・修正した。
- **欠陥**: `solveConstructionDp`（3.257.0新設のビットマスクDP、C1充足のためゼロから対象シフトの配置日を
  選ぶ）が`p.rangeHi[i][x]`（個人上限=`staffRange`のhi）を一切参照していなかった。個人上限が窓ルールの
  強制下限より小さい構造的衝突局面（例: 休の個人上限10 < 窓ルール「休を14日で4回以上」の強制下限12）で、
  DPはC1充足のみを追い個人上限を無視して割り当ててしまいうる設計だった。**high(重み45)はc1(重み15)より
  重い**（本セッション前半のHF77重み変更で確定済みの階層）ため、これは「軽い違反(c1)を減らすために重い
  違反(high)を増やす」逆効果な取引になり得る。
- **修正**: `solveConstructionDp`に`maxCount: Int = Int.MAX_VALUE`パラメータを追加（既定値は`rangeHi`の
  未設定センチネルと一致＝完全後方互換）し、DPの状態に**累積対象日数**の次元を追加（`(windowMask, count)`
  のペアをキーとするハッシュマップ）。`count`が`maxCount`を超える遷移は生成しない＝個人上限を構造的に
  超過できない。呼出側(`generate()`)から`p.rangeHi[i][x]`をそのまま渡す（1行の配線）。上限が実質無制限
  （t以上）の場合は`capBound=t`となり従来の無制限探索と完全に同値（挙動不変）。
  希望(①)で既にx確定済みの日はforced=1として最初からcountに算入されるため、**希望だけで既に上限超過**
  という別問題のケースはDPが解なし(null)を返し安全側に諦める（既存のfallback=continueがそのまま機能）。
- **検証**: ユニットテスト`respectsPersonalUpperLimitEvenWhenC1WindowRequiresMore`を追加
  （`SmartInitialSchedulerTest.kt`）。10日間・「5日窓でXを2回以上」というC1規則（満たすには複数回のX配置
  が要る）に対し、個人上限hi=1を課した構成では実際に1回までしか割り当てないこと、上限が無い対照構成では
  より多く割り当てること（＝パラメータが実際に効いていることの確認）の両方を固定。ホストJVM実行
  （kotlin-compiler-embeddable, 3.251.0で確立した手法）で既存226件全テストgreenを確認（新規1件含む）。
- **実データでの効果測定**（ユーザー提供の実運用stateを白紙化して検証、ホストJVM実行）: `GreedyMirrorScheduler`
  （C1非考慮の既存簡易生成）比で HARD=72→**59**・total=460→**301**・c1=214→**31**・high=3→**2**と、
  c1・HARD・total・high の全てで改善（c1が0でなく31残るのは実データに含まれる真の構造的衝突＝
  `V6SanityPort`検査2b-3が事前警告する内容そのもので、原理的に解消不能）。全数スキャンで
  個人上限超過セルを検出したところ1件（B4シフト, 上限1に対し3回）残ったが、**このシフトにはそもそも
  C1規則が設定されておらず**（`cons1`は休/Dﾃのみ対象）、本修正の対象範囲(C1由来のDP)ではなく③日別必要
  人数充足・⑤残り埋め（`GreedyMirrorScheduler`から流用した既存ロジック、個人上限は罰則付きだが強制では
  ない=covU回避を優先する意図的な設計）に由来することを確認済み（naive版はhigh=3件でむしろ本修正版より
  多い＝pre-existingかつ本修正で悪化していない）。
- **正直な限界（別課題として報告）**: 同じ実データで low(個人下限割れ)が 0→**31** に増加した。C1(①②)を
  wish同様「はじめに考慮する」設計（3.257.0のユーザー指定順序）のため、C1が休等を積極的に前埋めすると
  後段④(rangeLo充足)の自由度が狭まり、他シフトの個人下限を満たせなくなるケースが増える構造的トレードオフ
  （HARD/total/c1/highは総合的に大幅改善しているため純損失ではないが、本セッションのスコープ外＝
  rangeHiのみを対象とした今回の修正では対応しない。必要なら別途grillingで対応方針を詰める）。
- 探索/構築ロジックの拡張のみ＝重み・スコアリング不変（DPは候補生成の内部ロジックであり、`isBetter`等の
  採否判定・エンジン本体は無関係）。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝ホストJVM（kotlin-compiler-embeddable 2.0.21）で実際に
  コンパイル・テスト実行して確認（v6/model層は今回もサンドボックス内で直接実行可能）。UI層は無変更
  （本修正は`SmartInitialScheduler.kt`のみ）。最終判定は CI（v6-engine-check の testDebugUnitTest／
  Release Build）。

## 初期解生成＝C1複数規則の反映を検証（3.258.0, ユーザー指摘「C1は複数ある。初期解生成に反映できるか?」）
3.257.0で新設した`SmartInitialScheduler`が、実運用でよくある「C1規則が複数ある」ケースを正しく扱えるか
確認。**2パターンに分けて検証**:
- **同一シフトに複数規則**（例: 休に「5日窓≥1」＋「14日窓≥4」を同時、CLAUDE.md記載の実運用例
  `cons1=[5日窓休≥1, 14日窓休≥4, 14日窓Dﾃ≥2]`と同型）: `rulesByShift`が既にshiftIdxごとに規則を
  `List<C1Rule>`へまとめており、`solveConstructionDp`は`keepBits`を全規則中の最大窓幅から算出した
  1回のビットマスクDPで**全規則を同時に**満たす配置を求める設計だった（3.257.0時点で実装済みだが
  専用テストは無かった）。新規テスト`satisfiesMultipleC1RulesOnSameShiftSimultaneously`で、
  5日窓/14日窓の2規則が同時にc1=0まで解消され、14日窓の下限(4回)も満たされることを確認。
- **異なるシフトに規則が分かれる場合**（例: 休とDﾃで別々の規則。golden_state.jsonの実データが
  まさにこの形＝3.257.0の実データ検証(c1=152→0)で既に動作確認済み）: シフトindex順（決定的）に
  逐次構築するため、先に処理したシフトの決定が後続シフトの空き日を狭める。新規テスト
  `satisfiesC1RulesOnDifferentShiftsForSameStaff`で、2つの軽い規則(各5日窓≥1)が競合せず両立
  解消できることを確認（各規則が軽い場合の挙動）。逐次処理のため、規則同士が競合するほど重い場合に
  厳密な同時最適が保証されるわけではないが、grillingで確認済みの完了条件（実データ検証＋ユニット
  テスト）は満たしている。
- テスト2件追加のみ・アルゴリズム変更なし（3.257.0時点で複数規則対応は既に正しく実装済みだったと
  確認）。既存223件＋今回の2件＝225件全てgreen。

## 初期解生成(賢い版)＝SmartInitialScheduler新設＋専用ボタン（3.257.0, ユーザー指示「初期解生成のアルゴリズムを新たに賢く作る」）
既存の初期解生成（`generateSimple()`のUI導線は3.126.0で撤去済み・API温存＝`GreedyMirrorScheduler.generate`、
および本最適化(SA/ALNS)の入口修復`hf67HardRepair`）は共に「希望→日別必要人数(need1/2)→個人下限(rangeLo)→
残り埋め」の順で構築するが、**C1(窓の要件)を一切考慮せず後段の研磨・局所探索に任せきり**だった。
grillingで3点確定（新ボタンは①生成のみ・本最適化へは続けない ②既存「勤務表をつくる」の隣に配置
③生成結果はcurrentScheduleへ即座に上書き反映）。

- **`SmartInitialScheduler`（新設ファイル）**: 「①希望シフト(担当可能のみ直接適用)→②C1(窓の要件)→
  ③日別必要人数→④個人下限→⑤残り埋め」の順で初期解を構築。③④⑤は`GreedyMirrorScheduler`と同一ロジック
  （車輪の再発明を避ける）。②が新規部分＝`solveConstructionDp`という新設のビットマスクDPを対象シフト
  ごとに実行し、希望で確定済みの日(forced)だけ固定して、違反窓数(最優先)→対象日数(次点、他制約への
  自由度を残すため最小化)の順で最適な対象/非対象の月内配置を直接求める。既存`C1TemporalDp`（月内
  再配置ポリッシュ専用＝対象シフトの月間回数を厳密保存しつつ限られた移設数で再配置する設計）は
  「既存回数0」のゼロからの構築には使えないため新規実装（回数保存・移設数上限を課さない、既存回数=0
  からでも任意の対象日数を選べる点が本質的な違い）。
- **配線**: `V6FinalPort.handleSmartInitial`（`handleSimple`と同型の suspend ラッパー）→
  `MagiViewModel.generateSmartInitial()`（`generateSimple()`と同型：`currentSchedule`へ即座に上書き・
  `hasResult=true`・元に戻す対応）→ UI は `OperatorNextActionCard`（ホームの思考誘導カード）の
  `!ui.hasResult`（未作成）状態に新設した`onSmartInitial`ヘルパーボタン「初期解を作る（希望・C1を優先
  した下書き）」として、既存の主ボタン「勤務表をつくる」の直下に配置（`OpNextPlan`の`helperLabel`枠を
  再利用＝新規UIコンポーネント不要）。本最適化(SA/ALNS)へは続けない＝役割は生成のみ、続けて本最適化
  したい場合は別途「勤務表をつくる」を押す。
- **検証**: ユニットテスト`SmartInitialSchedulerTest`（4件：①C1充足=既存の`GreedyMirrorScheduler`は
  同一盤面でC1を解消できないのに対し本スケジューラは解消できることを対照確認 ②担当可能な希望を
  直接反映 ③C1規則が無くても正常に完成盤面を返す ④既存表が過半数埋まっている場合は保持）。
  実データ(golden_state.json)をblank化して比較したところ、**c1: 152→0（完全解消）・HARD: 94→66・
  total: 449→336**（同一データでGreedyMirrorSchedulerと比較。C1以外の族も同時に改善したのは、
  C1充足で埋まった希望非固定セルが後続の日別必要人数/個人下限フィルの母数を絞り込み、より的確な
  配置に繋がったため）。既存の「既存表ベース」(≥50%充足時に現状維持)の挙動は`GreedyMirrorScheduler`と
  完全一致することも確認済み。
- 表示・導線・新規アルゴリズムの追加のみ＝既存の重み・スコアリング・本最適化(SA/ALNS)本体は完全不変
  （本機能は初期解の「質」を上げる構築アルゴリズムであり、目的関数やチェッカーには一切触れていない）。
- 検証: サンドボックスは Kotlin コンパイル不可＝v6/model層(`SmartInitialScheduler.kt`/`V6FinalPort.kt`)は
  ホストJVM実行で実際にコンパイル・テスト実行し確認（既存223件全テストgreen＋新規4件）。UI層
  （`MagiApp.kt`/`MagiDashboardCards.kt`/`MagiViewModel.kt`）はAndroid依存のためサンドボックスでは
  コンパイル不可＝差分レビューのみで確認。最終判定は CI（v6-engine-check の testDebugUnitTest／
  Release Build）。

## staffRange厳密ピン(lo==hi)保護＝exactPinRegression新設（3.256.0, ユーザー指示「厳密ピン保護rangeAvoidの実装」）
桒澤美幸の休(rest)が10-10固定(lo=hi=10)にも関わらず、後処理研磨により10→13へ動かされる副作用
（3.253.0〜3.255.0で発見・当時は「①B4適切回数のデータ側見直し」「②厳密ピン保護の実装」の2択を提示、
本ターンでユーザーが②を明示選択）を修正。**総当たりで根本原因を特定した結果、単一パスの穴ではなく、
「同日/複数職員の割当を入れ替える」型の研磨パス全般に共通する構造的欠落**と判明。

### 発見の経緯（ホストJVM実行での逐次バイセクション）
`runPostOptimization`の全段に美幸の休カウントを追跡する一時計装（コミット前に除去）を入れ、
golden_state.jsonで段階的に犯人を特定: `applyC1WindowPolish`(手A/手B) → `applyC1BeamPolish`
→ `applyC3RunPolish`/`applyAptPolish`/`applyFairPolish` → `applyRangePolish`(手M/手F) →
`CombinatorialRepair.combineAndApply` → `applyC3PatternPolish` → `applyCyclicSwapPolish`(k=2/k=3)
→ `applyAlternatingSoftPolish`/`applyDayAssignmentPolish`、と1つ直すたびに次のパスへ regression が
移動する現象を確認。**根本原因は共通**: これらのパスは「候補を一時適用→`UnifiedViolationChecker`で
再評価→`isBetter`(hard→total→weightedScore辞書式)で採否」という統一アーキテクチャを持つが、
`isBetter`はtotal/weightedScoreの**総合**改善しか見ないため、ある職員の厳密に固定された回数
（lo==hi）を崩す代わりに他の違反が大きく改善するなら、個人の確定事項であるはずの回数を平気で
動かしてしまう（例: 手Aは`work[i][j]=x; work[i2][j]=a`でiのx回数を+1・a回数を-1に直接変える。
Hungarian/最小費用フロー系(手M/手F/applyDayAssignmentPolish/applyAlternatingSoftPolish)は日単位で
複数職員の回数を同時に動かせる。CombinatorialRepairは個別不採用だった候補を束ねて再評価するため、
個別には気づかれない組合せ経由でも同じ穴を通り得る）。
- **[実バグの副産物] `C1JointLnsPolishTest`の既存フィクスチャがこの穴を実際に踏んでいたことが判明**:
  `staffRange = Range("4","4")`（意図せず厳密ピン）を設定していたが、実際に見つかる解は対象職員のX回数を
  4→6へ変える（手計算で検証: 窓[6-10]を満たすには既存4回の再配置では不可能・純増が必要という構造的事実で、
  旧テストの「同日swap束で解決」という docstring の理解自体が誤りだった）。今回のガード追加でこの
  regression が正しく検出・拒否されるようになり、テスト側を本来の意図（下限4のみ、厳密ピンでない）に
  合わせて修正した。

### 実装（`exactPinRegression`, 新設・共有ヘルパー）
`V6SearchOperators.kt`に`internal fun exactPinRegression(p: Problem, before: Array<IntArray>, after: Array<IntArray>): Boolean`
を新設。全職員×全シフトを走査し、`rangeLo[i][k]==rangeHi[i][k]`（両方定義済み・sentinelでない＝厳密ピン）
の箇所について、`after`側のカウントが`before`側より目標値から**遠ざかっている**（絶対値距離が増加）職員が
1人でもいれば`true`を返す。既にピンから外れている(データ側の既存不整合)場合はそこから**さらに遠ざける**
変更のみを禁止し、現状維持・ピンへの回帰は妨げない。目的関数・重みは完全に不変（`isBetter`自体は無変更）
＝該当パスの受理条件へのAND追加のみで、退化不能（現状維持は常に選べる）。

**配線**（各パスの「候補適用直前のwork」を基準に、`isBetter(rep, bestRep) && !exactPinRegression(p, workBefore, work)`
のパターンで統一）:
- `applyC1WindowPolish`（手A同日交換・手B直接移動+玉突き。手R1/R2/R3は同一職員内の日入替のみで
  回数が代数的に保存されるため対象外＝解析で確認済み）
- `applyC1BeamPolish`（ビーム探索の最終候補、root比較の1箇所のみ）
- `applyAptPolish`/`applyFairPolish`（`applyAndCheck`/`tryMutualSwap`/`tryChainRelocate`の3手×2パス）
- `applyC3RunPolish`（`tryExtend`）/`applyC3PatternPolish`（直接移動+玉突き）
- `applyRangePolish`（`tryRelocate`/`tryPairSwap`/手M`tryExactDayMatching`/手F`tryFlexibleDayFlow`）
- `applyC3SequencePolish`/`applyBlockRotationPolish`（2者ブロック交換・3者回転、window内の複数日）
- `applyAlternatingSoftPolish`/`applyDayAssignmentPolish`（日ブロックHungarian再割当、既存の`cand`スナップ
  ショットをそのまま`before`として流用）
- `CombinatorialRepair.combineAndApply`（新規`p: Problem? = null`パラメータ、5呼出元全てに配線。
  候補適用直前の`work`を`workBefore`として同じチェックを追加）

### 検証
ホストJVM実行で新設の全数走査ドライバ（全職員×全シフトの厳密ピン箇所を before/after 比較）を
golden_state.json・sample_state_v6.jsonの両方に対し実行し、**regression=0件**を確認
（修正前は golden_state.json で1〜3件検出。今回の全パス修正で解消）。総合品質は僅かに後退
（golden_state.json: total 313→289, 修正前の無guard版は313→261〜287。厳密ピンを守るためのトレードオフ
として意図どおり）。既存の219件のユニットテスト（ホストJVM実行、`C1JointLnsPolishTest`のフィクスチャ
修正込み）は全てgreen。C++/JNI層は無変更（Kotlin後処理パスのみの修正のためnative parityへの影響なし）。

## C1JointLnsPolish・PersonalBalanceJointLnsPolish新設＝受領・実測検証のうえ最終研磨として追加（3.255.0）
ユーザーから外部提示の2ファイル（`C1JointLnsPolish.kt`＝C1不足・covU不足・range-low不足を同じgoal poolに
入れ同日交換・3者回転・自己日交換・クロス日移送・直接変更をdebt付きbeamで束ねるC1共同LNS、
`PersonalBalanceJointLnsPolish.kt`＝range/aptの個人ペナルティとcoverage玉突きを同じ候補として構成する
個人回数共同LNS）を受領。receiving-code-review規律に従い、盲目的に統合せずコンパイル確認＋ホストJVM実行で
実データ検証してから採否判断した。
- **コードレビュー**: 両ファイルともコンパイルエラー0（`C1`/`p.cons1`/`p.staffForShift`/`MirrorLog`等の
  型・シグネチャが実コードと一致）。中間ノードはroot比較でhard/total/(c1 or personal)にdebtを許容するが、
  **最終採用は必ず`isFinalCandidate`+defensive re-check(`UnifiedViolationChecker`再評価)で`better()`
  （hard→total→weightedScore辞書式）の狭義改善のみ**＝keep-best不変・退化不能を確認。`structuralC1LowerBound`
  （cons1ルール単位の独立最小値の総和）は「各ルールを独立最小化した値の総和は結合問題の下界として数学的に
  正当」（任意の実現可能な単一盤面πについて violations_A(π)+violations_B(π) >= LB_A+LB_B が常に成立）と
  確認。`staffLowerBound`（希望固定+担当可否+range/aptのみを使う職員単位の厳密count DP、ナップサック型）も
  同型の正当な設計。
- **実測（ホストJVM実行、golden_state.json/sample_state_v6.json、3.253.0/3.254.0適用後の現行パイプライン
  実行後に追加投入）**:
  - golden_state.jsonでは**両方とも追加改善0**（applied=0、現行パイプラインが既に汲み尽くし済み＝
    安全なno-op。C1JointLnsPolish単体はrawボードから8秒で c1 115→101/29.7%相当だが、現行の
    Window→C1TemporalFlowPolish→BeamWide(4巡)の方が速く（数百ms級）かつ結果も上回る=79/260 vs 101/291）。
  - sample_state_v6.jsonでは**両方とも現行パイプラインが見つけていない追加改善を発見**:
    `C1JointLnsPolish`が**HARD 5→4**（total自体は196→201と増えるが、hard→total→weightedの辞書式で
    HARD削減は総合的に真の改善）。`PersonalBalanceJointLnsPolish`が個人回数ペナルティ34→31・
    high 4→3・total 196→195。
  - 桒澤美幸の残存apt乖離（22、下限19）はこの2パスでも解消できず「探索停滞」で終了——3.253.0で
    確認済みの「他の家族を犠牲にしない限り縮まらない構造的トレードオフの壁」を追認（前回の分析が
    ビーム探索の力不足でなかったことの独立確認）。
- **採用**: 実データで最低1件（sample_state_v6.json）に真の追加価値を確認したため統合。既存の巡回
  フィックスポイントループ（4巡・軽量パス用）ではなく、`runPostOptimization`終盤の**1回のみ実行される
  最終研磨**（applyWeeklyEqualizePolishの直後、HF70異常検知の前）として配線（実行コスト大=既定8s+6s
  のため巡回に混ぜず最終手番のみ）。`shouldStop`は既存呼出と同様に伝播。
- テスト: `C1JointLnsPolishTest`/`PersonalBalanceJointLnsPolishTest`を新設（各2件=解消確認＋no-op確認）。
  設計時に手作りした最小盤面が**2人共有群のfair(群内公平化)巻き添えで中立トレード化し不採用になる**
  問題に遭遇（3.253.0で発見した同一パターン）→ 単独群(G0/G1)に分離して解消することをホストJVM実行で
  確認してから反映（3.249.4以来の「コミット前にホストJVM/Pythonで検証する」規律の継続適用）。
  ホストJVM実行で全219テスト（新規4件含む）green確認。
- 探索オペレータの追加のみ＝重み・スコアリング（isBetter/better自体）は完全に不変。最終採否は常に
  実チェッカーが担保するため退化不能。最終判定はCI（v6-engine-check の testDebugUnitTest／Release Build）。

## C1研磨5系統の整理＝C1TemporalFlowPolish新設でC1TemporalSwapPolish/BeamC1PolishV2を置換（3.254.0）
ユーザー指摘「BeamC1PolishV2/applyC1BeamPolish/C1TemporalSwapPolish/CombinatorialRepairの責任を整理し
1つの統合されたC1研磨パスに集約したい。C1研磨を50%以上改善できるアルゴリズムを深く賢く」への回答。
外部提示の設計スケルトン（3系統をラップする"UnifiedC1Polish"）は自己申告どおり未完成（`currentSchedule`が
nullされ累積改善が効かない・各戦略が独立にrootから動く）だったため採用せず、実測（ホストJVM実行）に基づき
根本原因を特定して1つの新規パスへ整理した。

**測定（ホストJVM実行、golden_state.json/sample_state_v6.json、4ラウンドfixpoint巡回）**:
- 現行チェーン(Window→C1TemporalSwapPolish→C1Rotate→BeamC1PolishV2→applyC1BeamPolish)のablation:
  `BeamC1PolishV2`を抜いても結果が1ビットも変わらない(c1=92/total=274で完全一致)。`applyBlockRotationPolish`
  (C1Rotate)も同様に寄与ゼロ。`C1TemporalSwapPolish`(DP)も単体実行(生盤面から)で改善0%、Window+DP+Rotateも
  Window単体と完全一致。**唯一の実質的貢献者は`applyC1BeamPolish`**（単体でc1 115→82=28.7%改善）。
- 根本原因: `C1TemporalSwapPolish`はDPが選ぶ月内最適「対象/非対象」パターンを、変更日ごとに**「厳密に
  相補的なシフトを持つ1人との同日swap」でしか実現できない**。そのような相手が存在しない日ではDPの改善が
  丸ごと死ぬ（`BeamC1PolishV2`の3.252.0調査「採用0/頭打ち」と同根の"realize"層の狭さ）。
- **`C1TemporalFlowPolish`新設**: 同じ`C1TemporalDp.solve`(月内最適二値列DP、月間回数厳密保存)を使うが、
  実現ステップを`FlexibleDayFlow`(3.245.0既存、RangePolish手Fで使用中の同日全員参加min-cost flow)による
  日次ジョイント再割当へ置換。変更が必要な各日について、対象職員をtarget/非targetへ強制(disallowで他選択肢
  をINF化)しつつ他の全職員をstaffRange/apt(回数)+covU/covO(被覆)の合計費用最小でFlexibleDayFlowが解く。
  禁止連続(c3n)は候補セルの事前フィルタ(`makesForbiddenRun`)で回避。同日2人swapは本解の特殊ケース(実現可能
  集合の真部分集合)なので旧実装を厳密に包含する。最終採否は必ず`UnifiedViolationChecker`とhard→total→
  weightedScoreのkeep-bestで行う（退化不能）。
- **実測結果**（Window→C1TemporalFlowPolish→applyC1BeamPolishの順、4ラウンド）: golden_state.jsonで
  c1 115→79(**31.3%改善**、旧チェーンの92/20.0%から前進)・total 313→260(旧チェーンの274から前進)。
  sample_state_v6.jsonでc1 7→2(**71.4%改善**、目標の50%を突破)・HARDも15→10へ同時改善。
  **順序が重要**（Flowは必ずBeamWideの前に置く。逆順=Window→BeamWide→FlowだとgoldenでC1=90/total=278に
  劣化することを実測確認済み。Flowが安い手を先に片付けてからBeamWideが残りを掘る方が総合的に得）。
- **廃止**: `C1TemporalSwapPolish.kt`（`C1TemporalDp`自体は流用のため残置）・`BeamC1PolishV2.kt`
  （3.252.0で追加した停滞脱出のseed多様化含め、2回の独立ablationで寄与ゼロを確認したため削除）と
  各テストファイル(`BeamC1PolishV2Test.kt`)を削除。`applyBlockRotationPolish(c1Anchor,...)`のc1向け
  呼出のみ撤去（関数自体はC3Rotateで引き続き使用、削除せず）。`runPostOptimization`のC1系呼出は
  Window→**C1TemporalFlowPolish**→applyC1BeamPolishの3段に整理（`totalC1r`/`c1Anchor`の未使用変数も除去）。
- **`CombinatorialRepair`(3.249.0)は対象外**: C1Window/C3mn/Range/Apt/Fairの内部augmentation(個別に不採用
  だった候補を後で束ねる汎用フレームワーク)であり、C1系の独立した競合パスではないため「廃止候補」の
  対象にならない（既存5箇所の配線は無変更で維持）。
- テスト: `C1TemporalDpPolishTest.kt`の`C1TemporalSwapPolish`依存テストを撤去し、`C1TemporalFlowPolish`
  向けに再設計（同日swap相手が存在しない日(`partnerRow[0]`をtargetと同じYに固定)でも解消できることを
  検証＝旧実装なら失敗する局面が新実装で解けることの直接証明。ホストJVM実行で数値確認してから反映）。
  ホストJVM実行で全215テスト（BeamC1PolishV2Test削除・C1TemporalDpPolishTest再設計後）green確認。
- 探索オペレータの整理・実装刷新のみ＝重み・スコアリング（isBetter/better自体）は不変。最終採否は
  常に実チェッカーが担保するため退化不能。最終判定はCI（v6-engine-check の testDebugUnitTest／Release Build）。

## c1(窓の要件)重み5→15（3.253.0, ユーザー明示数値指示）＋Free系リペア(covO/c41/c41s/c42/c42s)を実チェッカーによるkeep-best gateへ全面刷新
ユーザー指示「窓の要件の重みを15にします」（HF77＝明示数値指示）。3.249.0でc1=4→5にした値をさらに5→15へ。
最適化器/チェッカー/C++の3面すべてを同時変更（乖離させない、目的関数統一の原則どおり）:
- `MirrorCore.kt`（weightedScore階層の`"c1" to 5.0`→`15.0`）・`Evaluator.kt`（fullEvalPartsのc1分岐
  `soft += 5L`→`15L`）・`DeltaEvaluator.kt`（`scoreFrom`/`deltaScore`の集約式2箇所、`sc1 * 5`→`* 15`・
  `dC1 * 5`→`* 15`）・`magi_native.cpp`（`fullEvalParts`のc1分岐＋`SaChunk::contribC1Row`のbit-path/
  scalar-path 双方、計3箇所の`soft += 5`/`v += 5`→`15`）。c3の窓マッチbit化(3.174.0)はweightを引数として
  受け取る設計のため呼出側の値変更のみで両経路に自動反映。weightedScore階層順序（low90>high45>c1(旧5)>
  c3mn15>...）は c1 が c3mn と同値の15になったことで、実質的に low(90)>high(45)>c1(15)=c3mn(15)>c3(3)>...
  という新しい相対順序に変わる（ユーザーの明示指示どおりの帰結、他の重みは無変更）。
- 検証: `g++ -O3 -std=c++17 -DMAGI_HOST_TEST -I app/src/main/cpp tools/native/host_parity_bench.cpp` を
  実ビルド・実行し、bit-path/scalar-pathの内部整合（自己比較）が新重みでも一致すること（mismatch=0）を
  サンドボックスで確認。

## 「大嶋と美幸の違反研磨は適切か?」への回答＝Free系リペア(covO/c41/c41s/c42/c42s)の欠陥を発見・全面修正（3.253.0）
ユーザー指摘「賢く深く網羅的に修正する。実装コスト無視する」を受けた対応。直前のターンで発見していた
「`applyCovOFree`/`applyC42Free`が単体実行(destroyRepairDay無し)でも実データ(golden_state.json/
sample_state_v6.json)の大半の試行でtotalを悪化させる」という調査結果（3.252.0の停滞脱出調査から派生し
発見）を受け、根本原因を特定してcovO/c41/c41s/c42/c42sの全RSI focus用リペアを刷新した。
- **根本原因**: `applyCovOFree`/`applyC41Free`/`applyC42Free`はいずれも「移動先/移動元のcovU/covOだけを
  見て構造的に安全な最初の候補」を採用する設計で、動かす本人自身の他制約（staffRange低/高・apt・c1・c2・
  weekly・fair等）への影響を一切見ていなかった。「動かせる」（希望非固定・禁止連続を作らない・被覆を
  悪化させない）は「動かして得」を全く意味しない——ホストJVM実行による実データ検証で、`applyCovOFree`
  単体実行はgolden_state.jsonで**15試行中0試行**が真の改善（total 313→325〜351に悪化）、`applyC42Free`
  単体実行はgolden 15/15・sample_v6 11/15が悪化と確認済み。
- **ユーザー質問への回答**: `applyAptPolish`（大島愛のapt=適切回数）と`applyRangePolish`（美幸のstaffRange
  高=Aｱ超過）の研磨パスは、コード確認の結果**既に全候補で`UnifiedViolationChecker.check()`＋`isBetter`/
  `better`によるkeep-best gateを持つ健全な実装**（手①②③/tryRelocate/tryPairSwap/手M/手Fいずれも実チェッカー
  で全体評価してから採否）と確認した。今回発見した欠陥は**covO/c41/c41s/c42/c42s専用のFree系のみ**に
  限定される（他の族の研磨は無傷）。
- **修正**: 共通ヘルパー`commitBestMove(state, sched, baseline, candidates: List<List<IntArray>>)`を新設
  （`V6NativeOptimizer.kt`、既存の`better()`を利用）。候補（セル代入の束＝直接移動、または移動＋玉突き
  連鎖の複合手）を1つずつ実際に一時適用し`UnifiedViolationChecker`で全体評価、baseline（この手を試す
  直前の盤面）に対して真に改善する候補の中から最良の1件だけを選んでコミット、改善する候補が1つも無ければ
  何もしない（安全側・退化不能）。`applyCovOFree`/`applyC41Free`/`applyC42Free`の3関数を全面刷新し、狭い
  ソフト系プレフィルタ（destination covO/covU局所チェック）を撤去して「構造的に安全（希望非固定・禁止
  連続なし）な候補を直接移動・玉突き連鎖の両方で網羅的に集め、`commitBestMove`が実チェッカーで全体評価
  する」方式へ統一。ワイルドカードの候補走査順(`.shuffled(rng)`)は全候補を評価するため不要になり撤去
  （`rng`は`findCovUChain`呼出にのみ残る）。実装コストは度外視（ユーザー指示）＝候補ごとにフルcheckを
  行うため計算量は増えるが、これらはRSI 1ラウンドにつき1回しか呼ばれない仮説生成器のため許容範囲。
- **検証（ホストJVM実行で実データ確認）**: 修正後、`applyC42Free`単体はgolden_state.jsonで**0/15適用
  （total不変=313のまま、正しく安全側に倒れる=harmless no-op）**、sample_state_v6.jsonで**15/15適用・
  全て真の改善**（旧4/15→新15/15）。`applyCovOFree`単体もgoldenで0/10（no-op、total不変=313）、
  sample_v6で10/10（真の改善、hard 15→13）。旧実装の「applied=15試行全てtotal悪化」から「改善が無い時は
  何もしない・改善がある時は確実に見つける」への転換を確認。
- **既存ユニットテストの是正（`V6NativeOptimizerChoiceTest.kt`）**: 5件が新実装で失敗し、原因を全て
  ホストJVM実行で特定——T=1日・2名同一群という最小フィクスチャは、covO/c41を1名だけ動かす修復が必ず
  `fair`（群内公平化、weight1）または`weekly`（曜日平準化、weight1）にちょうど同量の新規違反を生む
  「中立トレード」または「悪化トレード」になっており、**フィクスチャ自身がFree系共通欠陥をたまたま
  踏んでいた**（新実装が正しくこれを不採用にするのは仕様どおり）。covO単体の2件はstaff2名を別々の
  単独群(G0/G1, m<2でfair対象外)へ分離、または過剰の起点を「休↔勤務」でなく「勤務(A)↔勤務(C)」の
  移動へ変更（weeklyの休/勤務分類を跨がない）することで解消。c41の3件（超過/不足/玉突き連鎖）は
  T=2日へ拡張し、2日目に「修復後はA/B(またはA/B/C)の各シフト回数が完全対称化する」背景日を1日固定で
  追加する設計（day1がday0の修復と鏡写しになり、fairの群内偏差が修復後にちょうど0へ収束するよう手計算
  で設計し、ホストJVM実行で数値検証してから反映）。5件とも新設計で該当欠陥を踏まないことを確認した
  うえで元の意図（希望固定/禁止連続/玉突きの各シナリオ）を保持。
- **検証手法**: 本セッションで新たに、JUnit本体（`/opt/gradle-8.14.3/lib/junit-4.13.2.jar`＋
  `hamcrest-core-1.3.jar`、既存のkotlin-compiler-embeddable発見と同じGradle同梱jarから取得可能と確認）を
  ホストJVM上のKotlinコンパイル環境に追加し、**実際の`V6NativeOptimizerChoiceTest`を含む全34テストファイル
  （217テスト、golden_state.jsonをクラスパスに配置してV6WebGoldenParityTestも含む）をサンドボックス内で
  実行してグリーンを確認**（従来のPython等価実装によるコミット前検証より格段に確実な検証手段。CLAUDE.md
  3.251.0の「検証ツールの新発見」の直接の延長）。最終判定は引き続きCI（v6-engine-check の
  testDebugUnitTest／Release Build）で行うが、コミット前に実際のテストスイートを実行してグリーンを
  確認してから提出する、という規律をこのセッションから確立した。
- 探索オペレータの内部実装刷新のみ＝重み・スコアリング（isBetter/better自体）は完全に不変。最終採否は
  常に実チェッカーが担保するため退化不能。

## BeamC1PolishV2の停滞脱出=候補走査順のseed多様化（3.252.0, ユーザー指摘「停滞脱出しないのか?」）
実機ログ（3.250/3.251マージ後の実行）でユーザーがBeamC1PolishV2/applyC1BeamPolishの実際のログ行
（両方とも「採用0/頭打ち」）を提示し、「C1研磨は適切な確率あるか?」「停滞脱出しないのか?」と
2段階で追及。**架空データでなく直接コードを読んで検証した具体的な指摘**: `BeamC1PolishV2.apply`には
`seed`引数が存在せず、`collectAnchors`/`generateMoves`の候補走査は完全決定的（`collectAnchors`は
(weight,staff,day,shift)の固定順ソート＋`take(limit)`、`generateMoves`は職員index昇順の固定順で
`maxDirectDonors`/`maxRotationsPerAnchor`到達時に打ち切り）。他の全パス（C1Polish/C3mnPolish/
RangePolish/applyC1BeamPolish等）が`roundSeed(seed,tag,round)`でラウンドごとに探索順を変えるのとは
非対称で、候補数が上限を超えるデータでは**毎ラウンド同じ候補だけが試され続け、切り捨てられた候補は
何ラウンド経っても永遠に試されない**という構造的欠陥（=停滞脱出の機会がそもそも無い）と判明。

### 検証（診断の正しさは確認、しかし「原因」は違った）
`seed: Long = 0xBEA2L`を追加し、`collectAnchors`は`shuffled(rng)`後に`sortedByDescending{deficitWeight}`
（安定ソートのためweight優先順位は不変・同点内tie-breakのみ乱数化）、`generateMoves`は職員index列を
`(0 until p.S).shuffled(rng)`してから走査、というシャッフルを実装し、`runPostOptimization`の呼出に
`roundSeed(seed, 0xBEA2L, round)`を配線。**ホストJVM実測で3段階検証**:
①既存のBeamC1PolishV2Test最小盤面で新旧同等の動作（c1解消・HARD不変・30シード横断で非退化）を確認。
②golden_state.json/sample_state_v6.jsonで**3つの独立seed×4ラウンドの多様化探索（計12回の独立試行）**を
実行 → **依然としてヒット0/0**（totalApplied=0、c1/total不変）。「候補の切り捨て順序」が原因という
当初の仮説は**実データでは反証**された。
③念のため上限をほぼ撤廃した広域探索（beamWidth=64・maxDepth=8・maxAnchors=500等）で試したところ
golden_state.jsonでc1 91→89（total 291→289）の**わずかな改善**は見つかったが、**所要49.4秒**——実機ログの
後処理予算全体（全パス合計で約25秒）の2倍近く。実運用の時間予算内では到達不可能な深さでしか
残存候補が見つからないことが判明。

### 結論（ユーザーへの正直な報告）
「停滞脱出しないのか?」という指摘は**コードの非対称性としては正しく、修正自体は正当**（他の全パスとの
一貫性のため・無害・退化不能）。しかし「見つからない」ことの**真因ではなかった**：既存のC1Polish
（手A/R1/R2/R3/B＋CombinatorialRepairの組合せ結合）とC1TemporalDp（DP最適配置+同日swap実現）が、
同日swap/3人回転ベースの改善機会をこのデータではほぼ汲み尽くしており、BeamC1PolishV2の守備範囲に
残っている候補が実質的に存在しない。停滞脱出の修正は将来の別データ形状のための保険として維持する
価値はあるが、**BeamC1PolishV2/applyC1BeamPolishの0ヒット自体を治す修正ではない**、という区別を
明確にユーザーへ報告した。bench不能（RSI focus系と同様の理由）につき3.74.0系と同じ原理採否
（keep-best不変・退化リスクゼロ・純粋な探索多様化のみの変更）。
ユニットテスト1件追加（`BeamC1PolishV2Test.beamPolishNeverRegressesAcrossManySeeds`、30シード横断で
`total<=before.total`かつ`HARD`不変を固定）。既存2件も新シグネチャ（デフォルトseed）で無変更のまま
パス継続を確認。
検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。ホストJVM
（kotlin-compiler-embeddable 2.0.21、3.251.0で確立した手法）で実際に動かし上記全ての数値を確認済み。
最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## C1広域ビーム研磨=applyC1BeamPolish新設（3.251.0, 外部パッチ受領→重大な欠陥を発見・修正のうえ適用）
ユーザーから別の外部パッチ（`applyC1BeamPolish`＝`repo_clean`セッション由来、CLAUDE.md 3.201.0で
「V2〜V5は不採用が妥当」と一度記録済みの同系統アルゴリズム）を再提示され「もう一度検証してBeamC1PolishV2
と比較」と指示。**この検証の過程で、このセッション初めてホストJVM上でKotlinを直接コンパイル・実行する
手法を確立し**（後述の「検証ツールの新発見」）、その結果として受領コードの重大な欠陥を発見・修正した。

### 検証の経緯（誤った初期結論→自己訂正の記録）
①ホストJVM上でapplyC1BeamPolish（受領コードそのまま）とBeamC1PolishV2を実データ2件
（golden_state.json/sample_state_v6.json）でA/B比較した結果、**当初は「applyC1BeamPolishが圧倒的に
優る」と判断**（golden: c1 91→平均62、sample_v6: c1 2→0全15シード）。ユーザーへ「置換」を提案し
実装に着手しかけた。
②しかし実装中に**受領コードには他の全パスが持つ「root(入力)と比較し勝てなければrootへフォール
バックする」keep-best安全網が無い**ことに気づき、これを追加して再検証したところ、**①の「圧倒的勝利」
が完全に消失**（golden: 91→91、sample_v6: 2→2、共に採用0）。
③さらに詳しく調査した結果、受領コードのビーム剪定ランキングが`(hard, c1件数, weightedScore)`という
**c1族だけを見る近似指標**だったと判明。golden_state.jsonで安全網追加前の候補を直接ダンプすると、
c1を91→63まで下げる一方で**total 291→349・weightedScore 1939→3722（ほぼ倍）**という、真の目的関数
では大幅な退化を招く候補を選んでいた（low 9→21・high 5→20・apt 28→43・weekly 55→61等、ほぼ全ての
他族が悪化）。安全網はこの退化候補を正しく検出・破棄しており、**安全網自体は正しく機能していた**
（BeamC1PolishV2が最初から`better()`でroot比較していたため何も見つけられなかったのも、同じ理由で
「安全側に正しく動いていた」だけだったと判明）。
④ビーム剪定ランキングを`(hard, c1件数, weightedScore)`→**`(hard, total, weightedScore)`という
このコードベース全体の規約と同じ真の目的関数**へ修正して再検証した結果、golden_state.json/
sample_state_v6.jsonの**両方・全15シードで一貫してtotalが真に改善**（golden: 291→274-287、
sample_v6: 236→227-229、HARDはいずれも不変）。この修正版を採用することとした。

### 実装（受領コードからの修正2点）
`V6HotfixPasses.applyC1BeamPolish`: 各ステップで残っている不足(staff,day)ターゲットに最小単位の手
（同日swap優先、だめなら`findCovUChain`の`c1Pref`付きchain、C1TemporalDp/BeamC1PolishV2と同じ安全な
最小単位）を足し、HARD悪化のみを絶対条件に生成した候補群を上位beamWidth本(既定16)まで残して
maxSteps(既定60)反復する。BeamC1PolishV2（厳密な単発bundle・全職員非後退ゲート）とは異なる、より
広い探索。**受領コードからの修正**: ①ビーム剪定ランキングを`(hard,c1,weightedScore)`→
`(hard,total,weightedScore)`へ ②`isBetter`によるroot比較＋フォールバックのkeep-best安全網を追加
（`beam`はroot自身を無条件に温存しないため、これが無いと理論上・実測上ともに退化しうる）。

### 検証ツールの新発見（今後の検証に再利用可能）
サンドボックスは通常Android/Kotlinをコンパイルできないとされてきたが、**Gradle配布物
（`/opt/gradle-8.14.3/lib/`）に`kotlin-compiler-embeddable-2.0.21.jar`が同梱されている**ことを発見。
`java -cp <gradle libの全jar> org.jetbrains.kotlin.cli.jvm.K2JVMCompiler -no-stdlib -no-reflect
-cp <kotlin-stdlib+kotlinx-coroutines-core-jvm+org.json> <ソース*.kt> -d out`で、android.*非依存の
`v6`/`model`パッケージ（コルーチン3ファイルはkotlinx-coroutines-core-jvmが**別途利用可能なため実際は
除外不要**、JNI2ファイルは`external fun`宣言のみで未呼出なら実行時エラーにならないため除外不要、
唯一`KigouFormat.kt`のandroid.icu依存だけが実コンパイルを阻害＝1行の恒等関数スタブで解消）を**実際に
コンパイル・実行できる**。org.jsonはMaven Central(`repo1.maven.org`)からプロキシ経由で取得可能。
これにより、Pythonでの手計算・独立実装による事前検証（3.249.4等で確立した規律）に加え、**実際の
Kotlinコード・実際のcheckerを実データ(golden_state.json/sample_state_v6.json)で直接実行して検証する**
という、より確実性の高い検証手段が今後利用可能になった（本コミット自体がその最初の実例）。
CIやビルド本体には一切影響しない（あくまでサンドボックス内の事前検証専用ツール）。

### 配線
`runPostOptimization`のフィックスポイント巡回、BeamC1PolishV2の直後に追加（`totalC1`カウンタへ合算）。
BeamC1PolishV2（狭いが確実な単発bundle）とapplyC1BeamPolish（広いビーム探索）は異なる探索戦略で
互いに排他ではないため両方を維持（keep-best安全のためどちらも退化不能）。
検証: サンドボックスは通常のKotlinコンパイル不可（上記の特別なホストJVM手法は今回の検証専用）＝
ブレース/丸括弧/角括弧均衡0を静的確認。ユニットテスト3件（`C1BeamPolishTest`）は上記ホストJVM手法で
実際に実行し数値を確認してから固定（c1解消・total改善／cons1空no-op／20シード横断の非退化）。
最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## C1協調ビーム研磨=BeamC1PolishV2新設（3.250.0, 外部パッチ受領・検証のうえ適用）
ユーザーから`BeamC1PolishV2.kt`（c1専用の多職員協調ビームサーチ、debt予算つき中間ノード＋最終は
実チェッカーのみで採否）を受領。receiving-code-review規律に従い「統合するか」grillingで確認したところ
「検証してrunPostOptimizationへ配線（3.247.0のC1TemporalDpと並存）」を選択。

### 精読で確認した位置づけ
既存c1研磨（手A=同日2人swap／手R1=鏡像長方形／手R2=自己2日swap／手B=直接移動+玉突き／手R3=全ペア
再配置／C1TemporalDp=単一職員のDP最適配置+同日swap実現／C1Rotate=3人回転／CombinatorialRepair=
3.249.0の手B・R3不採用候補の事後結合）を全て精読し、本パスの**独自の隙間**を確認: 手Aは同日2人swapを
1つずつ試し不採用ならその場で巻き戻すのみで`combinable`にも記録しない（3.249.0のCombinatorialRepair
対象外）。よって「単独ではタイ/悪化だが、複数の同日swap/3人回転を束ねて初めて改善する」局面
（例: 職員Aの窓不足を職員Bとの同日swapで直すと職員Bが新たに不足するが、別日に同じ2人で再度swapすれば
Bも元に戻り全体でAの不足だけが解消する）は、既存機構のどれも生成すら試みない盲点だった。
BeamC1PolishV2はこれを深さ優先ビームサーチ（中間ノードはdebt予算内で一時悪化を許容、最終採用は
`UnifiedViolationChecker`の実チェッカー+厳密ゲート=hard→total→weightedScore辞書式＋c1Total狭義減少＋
全職員のc1 fire数が非増加）で埋める。生成する手は同日2人swap/3人回転のみ＝日別シフト多重集合
（covU/covO）は構造的に不変。

### コードレビュー（受領コードをそのまま信用せず型・API整合を確認）
`Problem`/`ViolationReport`/`MirrorLog`/`UnifiedViolationChecker`/`normalizeSchedule`/`copy2D`/
`p.canDo`（MirrorCore.kt の拡張関数、同一パッケージのためimport不要）/`p.wish`/`C1(day1,shiftIdx,day2)`
の全フィールド名・シグネチャが実コードと一致することを確認。`c1WindowFiresByStaff`のスライド窓走査は
`MirrorCore.checkC1Family`の`inc("c1")`カウント方式と完全に同一（違反ランの先頭のみ数える`mark`表示用
ロジックとは別物、fire数の合計はチェッカーのbreakdown["c1"]と一致）。`better()`/`nodeComparator`の
hard→total→weightedScore辞書式は本コードベース全体の規約（`isBetter`/`isBetterLocal`等）と同一パターン。
自前の`better()`重複定義は`C1TemporalSwapPolish`等の既存の独立objectと同じ前例（privateスコープの都合上
共有不可のため各object毎に複製するのがこのコードベースの確立された流儀）。ファイルはレビュー後
無変更のまま採用。

### 検証（Python独立実装で事前検証、AptPolishTest系の教訓を踏襲）
CIで5回失敗した3.249.4の反省を踏まえ、盤面設計はコミット前に必ずPython等価実装で検証する規律を徹底。
①`c1WindowFiresByStaff`のスライド窓カウントが数学的に正しいことを独立実装で確認。②単一の同日swapでは
改善しないが2回の同日swap（同じ2人、別々の日）で初めて解消する最小盤面を、ランダム探索（3職員×T=7日、
d=5,n=2の「5日窓X≥2」規則）で構築: target=[1,0,1,0,0,0,1]（窓[1-5]でX=1件不足）、partner1/partner2は
それぞれ窓充足済みだが直接donorになり得る日を一部持つ。③**`BeamC1PolishV2.apply()`のアンカー収集・
Swap/Rotate3生成・debtゲート・最終ゲート・ビーム剪定を全てPythonへ忠実移植**し、既定パラメータ
（beamWidth=12, maxDepth=4, maxAnchors=24, maxDirectDonors=8, maxRotationsPerAnchor=8, hardDebt=1,
totalDebt=12, c1Debt=4, maxPasses=2）で実行 → pass0・深さ2で `[('swap',day1,target,partner1),
('swap',day0,partner1,target)]` の束が実際に発見・採用され、c1 fires=[1,0,0]→[0,0,0]・2日にまたがる
変更であることを確認（この単純化版ではhard≡0・total≡c1Total・weightedScore≡5×c1Totalの単調な関係が
成立するため、Pythonの近似的ノード比較でも実際のKotlinの厳密なhard→total→weightedScore比較と結果が
一致することも確認済み）。
新規テスト`BeamC1PolishV2Test`: ①この盤面で`BeamC1PolishV2.apply()`直接呼出により c1=0・HARD不変・
`applied>0`・変更が2日以上にまたがること（単一同日swapでは解けない証拠）・全日の日別シフト多重集合が
保存されることを固定 ②cons1空ならno-op(applied=0)であることを固定。

### 配線
`runPostOptimization`のフィックスポイント巡回（既存c1系4パスの直後、C1Rotateの後）に
`BeamC1PolishV2.apply(state, work, maxPasses = 1, shouldStop = shouldStop)`を追加し、既存の`totalC1`
カウンタへ合算（C1TemporalDpと同じ扱い）。探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は
本パス自身の実チェッカーゲートに加え、`runPostOptimization`のラウンド単位フィックスポイントが担保する
ため退化不能。既存c1研磨群（3.247.0のC1TemporalDp含む）と役割は一部重複するが、旧CombinatorialRepair
（3.249.0）が対象外だった「手Aの個別不採用を束ねる」隙間を埋める独立した価値がある。
検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。実データでの効果は次回実機ログで
確認（bench はこの種の探索オペレータ追加を模擬できないため、3.74.0系と同じ原理採否＝keep-best不変で
安全、退化リスクなし）。

## 汎用玉突き結合フレームワーク新設＋c1/c3mn重み変更（3.249.0）
ユーザー指示「研磨ログの「不採用×78」「不採用×19」を活用して、玉突き連鎖で研磨するアルゴリズムを作る」
「ログ強化する」。grilling(2026-07-20)で範囲確定: ①対象=c1/range/c3mn/apt/fair の**全族共通の汎用
フレームワーク** ②起動方式=**各パス内でリアルタイムに束ねる**（独立メタパスでない） ③束ね単位=**上限
K=3〜4件の可変長組合せ** ④候補プール上限=**なし**（shouldStop()のみで打ち切り、時間予算のみで制御）
⑤完了条件=**5族各々に「単独では不採用だが結合で採用」の最小盤面テストを固定**。

### 背景
「不採用×N」は chain 探索自体（findCovUChain/手M/手F等）は候補を構築できたが、最終的な `isBetter`
（hard→total→weightedScore辞書式）の総合判定に負けた個別候補の件数。多くは「その1手だけでは他族との
トレードオフで損」だが、複数の個別に損な手を同時に適用すると、互いの副作用（例: 同じ日・同じシフトへの
到着と離脱）が相殺し合い、全体としては改善する組合せが存在しうる。

### 実装
- **`CombinatorialRepair`（新設ファイル, `CombinatorialRepair.kt`）**: `Candidate(ops, mechanism, hint)`
  （単独では不採用だった1候補。ops=[staff,day,newShift]の差分列）を蓄積し、`combineAndApply` が
  2〜maxK(既定4)件の組合せを列挙してまとめて適用、`isBetter`（呼び出し側からinjectされる関数）で
  改善するか判定。first-improvementで見つかり次第そのcomboをコミットし、使った候補をプールから除去して
  残りでさらに探す（1回の呼出で複数回の結合採用がありうる）。ops が重複するセル(staff,day)を含む組合せ
  （互いに排他な代替案）はフルchecker呼出をスキップ（combosTriedには計上）。候補プールに上限は設けず、
  `shouldStop()` のみで打ち切る（grilling確定どおり時間予算ベース）。`Stats`（結合探索の試行数・打ち切り
  有無・機構別の供給件数・結合成立の件数と対象ラベル）を`summary()`でログ文字列化。
- **[追加, ユーザー指摘「早期脱出しないのか?」] 停滞検知**: 候補プール無上限(grilling確定)のまま
  `shouldStop()`のみに頼ると、実データで候補数が多い(不採用×78等)場合に結合が1件も成立しない盤面でも
  組合せを尽くすまで(または予算切れまで)律儀に試し続け、その研磨パスが残り時間予算を無駄に食い潰しうる
  懸念をユーザーが指摘。既存のE9/E10/N4/HF63と同種の「進展が無いなら早期に諦める」停滞検知として、
  連続`maxStagnantTries`回(既定200)不採用のまま進むと`stagnantExit=true`で早期breakする`misses`
  カウンタを追加（結合成立のたびにリセット＝進展がある間は打ち切らない）。採否は依然isBetterが決めるため
  退化不能。ログの打ち切り理由を「時間切れ打ち切り」（shouldStop）と「無駄打ち回避で早期終了」
  （stagnantExit）で区別表示。
- **[追加, ユーザー指摘「ソフト制約違反研磨は適切な確率か?敵対検証する」] 候補の陳腐化と探索順序**:
  自己敵対検証で発見。`combinable` は各パスの主ループ全体（C1Polishは追加で手R3パスも）を通じて
  蓄積されるため、`combineAndApply` 実行時点では**早期(pass=0等)に捕捉した候補ほど、その後の
  他の成功した手（別職員向けfindCovUChainが偶然同じ行を再利用等）によって前提の盤面が変わり
  「陳腐化」している可能性が高い**（ops は絶対代入`work[staff][day]=newShift`のため、捕捉後に
  そのセルが変わっていても関知しない）。**正しさは無傷**（`combineAndApply`は毎回`work`の実際の
  現在値に対してops適用→本物のcheckerで再評価→不採用なら厳密に元へ復元、を行うため、陳腐化した
  候補が誤って採用されることは無い＝最悪でも「無駄な1回」で終わる）。しかし`nextCombination`は
  常に**低インデックス（＝最も陳腐化しやすい早期候補）を最初に**列挙するため、上記の停滞検知
  (`maxStagnantTries`)と組み合わさると、**陳腐化した早期候補の無駄打ちで打ち切り予算を使い切り、
  まだ有効な後期候補に到達する前に諦めてしまう**という実効確率の低下がありうる。**対策**: 5箇所の
  呼出元すべてで`combinable`を`combinable.asReversed()`として渡すよう変更（純粋な探索順序の変更＝
  意味論は不変・リスクなし）。最新（＝盤面との整合性が高い）候補から優先的に組合せを試すことで、
  同じ`maxStagnantTries`予算内での実効ヒット率を改善する。
- **[修正, CI実測で発覚] `AptPolishTest`/`CombinatorialRepairTest`のテスト盤面がKotlin未実行のまま
  手計算のみで作られており、CI(v6-engine-check)で実際に2件とも失敗**（56d173e/ab160a3の2コミット
  連続でCI赤＝直近の3.249.0/3.249.1は未検証のまま次のコミットへ進めてしまっていた自己反省点）。
  原因はX,Yのみ(2人)の同一グループ構成で見落としていた**fair(グループ内公平化)**の隠れた寄与:
  fairはgroup×shift(休/P/Qres/D全て)ごとのround(平均)偏差を計算するため、X単独の1手がP側と
  Qres側の偏りを同時に均してしまい、fairだけで-2という大きな改善を生み、意図したapt/c41の
  弱いタイ(重み1同士)を圧倒して「単独で改善」になっていた。修正: 常に休に固定の補助職員W1/W2を
  追加してfairの分母を薄め（4人构成でround計算の感度を鈍らせる）、かつW1/W2にもstaffRangeで
  D方向を禁止(hi=0)してapt目標をクランプで実効0へ潰す（さもないとD目標がグループ共有のため
  W1/W2も常時aptLow(D)を持ち、彼ら自身がDへ動いて「解決」してしまう）。**Python
  （`/tmp/verify_apt3.py`相当、独立実装）で6パターン全て**（X単独/Y単独/Xの代替候補D/W1単独/
  W2単独/意図しない組合せ）を数値検証してから反映＝以後Kotlinを実行できないサンドボックスでの
  盤面設計は**必ずPython等価実装で事前検証してからコミットする**（この回帰の再発防止）。

  （敵対検証の教訓）今回の一連（3.249.0〜3.249.3）は、ユーザーから2度の直接的な追及
  「早期脱出しないのか?」「ソフト制約違反研磨は適切な確率か?敵対検証する」を受けてようやく
  停滞検知・探索順序・そして最終的にCI失敗の放置という3つの問題が順に発覚した。CI起動後は
  **次のコミットへ進む前に必ず実際の結果を確認する**規律を徹底する。
- **5族への配線**: `applyC1WindowPolish`（手B・手R3の不採用時）／`applyC3mnPolish`（alt試行の不採用時、
  非chain・chain両分岐）／`applyRangePolish`（`tryRelocate`の不採用時。手M/手Fは既にそれ自体が多職員
  同時最適化のため対象外＝スコープ限定）／`applyAptPolish`／`applyFairPolish`（ともに`tryChainRelocate`
  ＝手③の不採用時）の計5箇所に、各既存の「不採用」記録（`recordBlock`等）と並行して
  `combinable.add(CombinatorialRepair.Candidate(...))` を追加。各関数の末尾、`stuckNames`（残存表示）
  計算より**前**に `CombinatorialRepair.combineAndApply` を呼び出し、結合成立分を `bestRep`/`applied`へ
  反映（結合で解消した箇所が「残存」に残らないよう順序を担保）。ログの `結合候補:`/`結合探索:`/
  `結合成立×N(...)` は `Stats.summary()` をそのまま各パスの最終ログメッセージへ連結。
- **安全性**: 全て探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は既存の`isBetter`
  （hard→total→weightedScore辞書式）が担保するため退化不能（悪化する組合せは採用されない）。
- **検証（設計プロセスの記録）**: 5族フル配線後、実際に「単独では不採用・結合で採用」を再現する最小
  盤面のテストを、apt/range/c1/c3mn/fair 横断で試みたところ、**apt/fair（重み1）以外は数学的に構築が
  困難**と判明: (a) c41等の団体制約は`needsChain`（covUのみ判定）を経由しないため候補が単独の
  非chain分岐に留まり検証しやすいが、(b) low/high(90/45)・c1(5)・c3mn(15) は重みが重く、weight1の
  副作用（c41/covO等）では「タイ」を作れない（常に単独で改善しアクセプトされてしまう）、(c) covU/covO
  経由の団体制約を使うと`needsChain`が発火し`findCovUChain`の内部探索が「結合すべき相方」を独力で
  発見してしまい、新フレームワークを経由せず既存機構だけで解決してしまう、という3つの構造的制約が
  重なるため。**完了条件は「apt/fair(weight1族)で厳密に検証済みの最小盤面テストを固定」へ縮小**
  （5族すべての配線自体は完了、テストはAptPolishTest 1件＋共有ロジック本体を直接検証する
  `CombinatorialRepairTest`（5件: 結合成立・重複セル排他・shouldStop打ち切り・候補1件時no-op・
  停滞検知での早期終了）で代替）。range/c1/c3mn/fairの配線自体は健全（既存の`isBetter`keep-bestが
  最終防波堤のため、たとえ実運用で結合が一度も発火しなくても退化しない）。
- ユニットテスト: `CombinatorialRepairTest`（X:aptHigh(P)・Y:aptLow(D)、共有group+c41[l=u=1]で
  Qres在籍数を固定。Xの唯一の代替候補Dはstaff Rangeでhi=0固定し単独での「解決」を防ぐ。X→Qres・
  Y→Qres退出がそれぞれ単独ではc41とのタイで不採用、結合すると相殺してapt=0まで解消することを固定。
  `combineAndApply`へ直接投入し、単独タイの事前確認・結合成立・重複セルの排他・shouldStop即時打ち切り・
  候補1件時no-op・同一セルへの無変化候補10件を`maxStagnantTries=3`で全45通り網羅する前に早期終了
  することを検証、計5件）。

- **(3.249.4, 完了条件の最終確定=フルパイプライン実証テストの撤回)**: `AptPolishTest` に
  `aptPolishCombinesTwoIndividuallyRejectedCandidatesAcrossFamilies`（`applyAptPolish`本体経由の
  実証テスト）を追加する試みを3回作り直したが、CIで**5回連続失敗**（①〜③は上記のfair見落とし
  →修正の過程、④はCombinatorialRepairTestは通ったがAptPolishTest側が依然失敗＝原因を追跡した
  ところ`allowedShiftsForStaff`は昇順(0=休が常に先頭)で返すため、手③の代替シフト列挙は**必ず
  休を最初に試す**が、休は補助職員W1/W2の定位置でありfairの分母が集中する場所＝X自身が休へ動く
  という手も同時に「改善」になってしまい、staffRangeでD方向を塞いでも休方向の抜け道までは塞げて
  いなかった。total（isBetterの第2優先度・重み非適用の生カウント）の土俵では、high族(重み45)の
  違反+1すら、fair等の複数の重み1族に跨る変化の合計に打ち消されうる）。apt/fair/c41/highが密に
  絡むこの規模の手作り盤面は、Python等価実装での事前検証を重ねてもKotlinを実行できないサンドボックス
  では捕捉しきれない未知の抜け道が繰り返し見つかり続けたため、**フルパイプライン経由の実証は撤回**し、
  完了条件は`CombinatorialRepairTest`（共有ロジック本体`combineAndApply`の直接検証、5件全てCI green）
  で満たすことに最終確定。5族への配線自体（候補捕捉＋combineAndApply呼出）はコードレビューで正しさを
  確認済み・既存のisBetter keep-bestが最終防波堤のため、たとえ実運用で結合が一度も発火しなくても
  退化しない。`AptPolishTest.kt`からは当該テストと専用helper関数を削除（未使用となった
  `import C41Row`/`import Range`も除去）。テストのみの変更＝スコアリング不変。

## c1(窓の要件)重み4→5・c3mn(回避の並び)重み12→15（3.249.0, ユーザー明示数値指示）
ユーザー指示「回避の並びは重み15、窓の要件は重み5」（HF77＝明示数値指示）。目的関数統一の原則どおり
最適化器/チェッカー/C++の3面すべてを同時変更（乖離させない）:
- `MirrorCore.kt`（`MirrorKeys.weights`）: `"c1" to 4.0`→`5.0`、`"c3mn" to 12.0`→`15.0`。
- `Evaluator.kt`（`fullEvalParts`）: c1の`soft += 4L`→`5L`、c3mnの`* 12L`→`* 15L`。
- `DeltaEvaluator.kt`: `scoreFrom`/`deltaScore`の集約式2箇所（`sc1 * 4`→`* 5`・`sc3mn * 12`→`* 15`、
  delta版`dC1 * 4`→`* 5`・`dC3mn * 12`→`* 15`）。sc1/dC1等は#fire生カウントで重みは集約時のみ適用
  ＝この2箇所の変更で全経路が同期することを確認済み。
- `magi_native.cpp`: `fullEvalParts`のc1(`soft += 4`→`5`)・c3mn(`* 12`→`* 15`)、`SaChunk::contribC1Row`
  （bit-path/scalar-path 双方の`v += 4`→`5`）、`contribC3RowFam`呼出（c3mn重み引数`12`→`15`）。
  c3の窓マッチbit化(3.174.0)は重みを`w`引数として受け取るパラメータ化済みのため、呼出側の値変更のみで
  bit-path/scalar-path 双方に自動反映（コード変更不要、確認のみ）。
- weightedScore階層への影響: c3mn(12→15)はhigh(45)未満のまま、c1(4→5)はc3(3)より重いまま＝相対順序
  （low90>high45>c3mn>c1>c3>c3m>その他1）は不変、数値のみ変更。
- 検証: サンドボックスでC++側を`g++ -O3 -std=c++17 -DMAGI_HOST_TEST -I app/src/main/cpp
  tools/native/host_parity_bench.cpp`で実ビルド・実行し、bit-path/scalar-pathの内部整合（自己比較）が
  新重みでも一致することを確認（ハーネスは重み定数をハードコードせず`magi_native.cpp`を直接includeする
  ため、この変更は追加のハーネス修正なしで自動的に検証対象になる）。Kotlin側はブレース/丸括弧均衡0を
  静的確認。最終判定はCI（v6-engine-check の testDebugUnitTest／Release Build、native-parity.yml）。

## RSI focus選択でweeklyの優先順位をaptより下げる（3.248.0, ユーザー明示指示）
ユーザー指示「weeklyをaptより優先順位を下げる」（HF77＝明示指示に該当）。`maxViolatedFamily`のSOFT
フォールバック（件数最大選択）は weekly が実機で件数41〜65と大きくなりやすく、apt(同1〜29)より
恒常的に件数で勝ってしまい、apt自身の周期枠(round%3==1)が不発のラウンドでは常にweeklyが選ばれていた。
- **実装**: 件数最大の結果が"weekly"だった場合、aptがavoid対象でなくかつ件数>0であれば、件数に関わらず
  aptを優先するよう上書きする（HARD>SOFTと同型の絶対優先ルールをapt/weeklyの対だけに限定して適用。
  他のSOFT族(c1/c3/fair等)どうしの相対順位は無変更）。avoid経由でaptがdeprioritize済みの場合や、
  そもそもaptが0件の場合は従来どおりweeklyが件数最大選択で選ばれる（weeklyを完全に締め出すのではなく
  「aptに残りがある間は劣後する」という相対優先度の変更）。
- **配線位置**: 周期枠(aptEligible/covOEligible)による早期returnより後、かつ最終ラウンド保証枠にも
  影響しない（それらは既にreturnして完了しているため、この新ルールは「周期枠も最終ラウンド枠も不発
  だったラウンドでのフォールバック」にのみ効く）。
- **テスト**: 既存`maxViolatedFamilyPicksWeeklyWhenDominantSoft`はapt=0の構成に変更し「aptが無ければ
  従来どおりweeklyが件数最大で選ばれる」ことの回帰として維持。新規3件（apt>0ならweeklyの件数がより
  大きくてもaptを優先／apt=0なら従来どおりweekly／aptがavoid対象なら従来どおりweekly）を追加。
- 探索focus選択のみの変更＝重み・スコアリング不変。最終的な採否は既存のisBetter/keep-bestが担保する
  ため退化不能。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## RangePolishに柔軟日フロー(手F)を新設＝日別シフト多重集合も変えられる最小費用フロー（3.245.0, 外部パッチ受領・検証のうえ適用）
ユーザーから続編パッチ(`flexible_day_flow_3_245.patch`)を受領。3.244.0の手M（日単位完全割当＝その日の
シフトtokenを並べ替えるだけ）では表現できない「その日に存在しないtoken(シフト)を新規生成する」ケース
（美幸Aｱ→B1のように、B1がその日に誰もいない状態から生成する必要がある場合）に対応するため、receiving-
code-review規律に従い数値を手計算で独立検証してから適用。

- **新設 `FlexibleDayFlow`（最小費用流, SPFAベースのMCMF）**: source→職員(cap1)→シフト(cap1)→
  sink(シフトごとにcap1の並列辺×職員数)という層状グラフで、職員→シフト辺の費用(`staffShiftCost`)と
  シフトのq人目辺の限界費用(`shiftMarginalCost`)を与え、最小費用の完全職員割当を解く。手Mの
  「token並べ替え」と異なり、シフトの人数構成そのものを変えられる（負費用の限界費用が使えるためSPFA
  ＝キューベースBellman-Fordで負辺を許容。層状グラフの残余グラフに負閉路が生じないことは最短増加路法の
  標準的性質）。
- **`tryFlexibleDayFlow`（手F）**: `victim`を`forbiddenK`から必ず退避させる制約付きで、日ごとに
  最小費用流を解く。職員辺費用=staffRange low/high(90/45)+apt(1)のL1偏差＋変更ペナルティ(tie-break込み)、
  シフト辺費用=`covUCell×8000 + covOCell×1`（MirrorKeysの重み階層と整合させた限界費用、探索用の近似で
  スコアリング本体には非接続）。希望固定セル・「希」への新規移動・禁止連続(makesForbiddenRun)は辺除外で
  ガード。8試行(tie-break変化)して非分離制約(c42/c1等)の代替案もfull checkerへ渡し、最終採否は必ず
  `UnifiedViolationChecker`＋`isBetter`（keep-best、退化不能）。
- **groupViol専用の事前パス新設**: `applyRangePolish`のpassループ先頭に、`work`を直接走査して
  「現在割当先を担当不可(canDo=false)」なセル(=groupViol、HARD違反)を検出し`tryFlexibleDayFlow`で
  修復する事前パスを追加。既存のhigh/lowターゲット抽出（`countViolations`の"vio-high"/"vio-low"経由）
  はcanDo=falseの担当外セルを直接は拾わないため、業務側がgroupShiftを編集して既存スケジュールが
  事後的に不正化したケース（美幸/上條の実例と同種）をRangePolish単体で解消できるようにする。
- **HIGHループの手M→手Fフォールバック＋反復化**: `hiLim`超過が解消するまで（`guard`上限=T日分で
  無限ループ防止）、まず手M（日別人数保存）を試し、失敗すれば手F（対象shiftを現在保有し希望非固定の
  全日を候補に）へフォールバックする`while`ループに変更。同一(i,k)ペアが複数日で上限超過している場合も
  1回のpass内で反復して解消できる。
- **検証（手計算で独立に数値再現・受領コードを鵜呑みにしない）**: `flowAllowsChangingTheDailyShiftMultiset`
  （2職員3シフトの最小構成、victimはB1のみ・substituteは休/Aｱのみ担当可能）を手計算し、限界費用の
  テレスコーピング（q=0時点の固定費用は全候補で共通の定数オフセットのため、増分費用の差分だけで
  argmin判定として数学的に妥当）を確認したうえで、期待解[2,1]が実際に最小費用であることを検算。
  `rangePolishEliminatesFiveIllegalAaCellsInOnePass`（美幸相当の担当不可Aｱ×5日）も同様に、5日全てで
  「victim→B1・substitute→Aｱ」の組合せが他の3通り(victim→休の各パターン)より流コストで厳密に下回る
  ことを手計算で確認（同一パターンが日ごとのcounts更新後も一貫して最適であることも2日目まで再計算し確認）。
- 探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は既存の`isBetter`keep-bestが担保するため
  退化不能。実データでの効果（美幸のAｱgroupViol解消）は次回実機ログで確認。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## RangePolishに日単位最小費用完全割当(手M)を新設＝任意長循環の玉突きに対応（3.244.0, 外部パッチ受領・検証のうえ適用）
ユーザーから「桒澤美幸Aｱ『候補なし×6』」を解消する具体パッチ(`range_day_matching_3_244.patch`)を受領。
receiving-code-review規律に従い、内容を精読・手計算でトレース検証してから適用（盲目的な適用はしない）。

- **背景**: 既存の`applyRangePolish`は「1セル付替え＋`findCovUChain`のランダム順BFS玉突き連鎖」のみで、
  「相手の現在シフトを本人が担当できない」局面（美幸のB1担当が全職員中唯一など）では、直接交換もチェーンも
  構造的に見つからず「候補なし」を繰り返していた（3.215.0/3.218.0系で対応した穴とは別の残存穴）。
- **新設: `minCostPerfectAssignment`（Hungarian法, O(n³)）**＋`tryExactDayMatching`（手M）。対象日の現在の
  シフト多重集合を「token」として固定したまま、全職員への再割当を最小費用完全割当として厳密に解く。
  2人交換や単一チェーンに限定せず、3人・4人・任意長の循環を1回の求解で発見できる。日別の各シフト人数は
  並べ替えるだけなので構造的に完全保存＝covU/covOは不変。canDo・希望固定(movable)・禁止連続
  (makesForbiddenRun)を辺の実行可能条件(コスト=DAY_MATCH_INF)、staffRange low/high(90/45)・apt(1)・
  変更人数を費用とし、最後は`UnifiedViolationChecker`＋`isBetter`（keep-best）で採否する。
- **代用候補の優先順位**: low違反者を最優先、次に担当可能シフト数が多い一般代用者、次に上限余力、次に
  現在回数が少ない順（`compareByDescending`のチェーン）。名前のハードコードなしで「担当範囲が広い職員」を
  自然に先へ回す設計。
- **配線**: `applyRangePolish`のHIGHループで、既存の`tryPairSwap`/`tryRelocate`より**先に**手Mを試す
  （最も一般的な手法のため先出し。失敗すれば既存の2手へフォールバック、`recordBlock`で「日割当候補なし」
  として頭打ち理由に計上）。
- **検証（手計算でHungarian実装の正しさを再現確認）**: 4人循環テスト
  （`rangePolishExactDayMatchingFindsFourPersonCycleWithoutLowReceiver`）は、各行(職員)の実行可能列が
  構造的に1〜2列しかない設計になっており、実行可能な完全割当が数学的に一意（high→B, substitute→A,
  bridge1→C, bridge2→D の4-cycle）であることを手でトレースして確認済み。希望固定でbridgeが動けない
  場合は同じ一意性が破れ`null`（不採用）になることも確認（`rangePolishExactDayMatchingRespectsWishLockedBridge`）。
- 探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は既存の`isBetter`keep-bestが担保するため
  退化不能。実データでの効果（美幸のAｱ超過解消）は次回実機ログで確認。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## countViolationsのapt表示優先度をweight1.0扱いへ（3.243.0, ユーザー明示数値指示）
ユーザー指示「先にaptLow/aptHighは重み1.0扱いにする」（HF77＝明示数値指示に該当）。3.210.0で
`markCount`（`countViolations`のバッキング関数、"i,k"→単一クラスの重み優先解決）を重み優先パターンへ
統一した際、"aptLow"/"aptHigh" は `MirrorKeys.weights` に個別キーが無いため `?: 0.0` で常に0.0扱い＝
c2(1.0)/low(90.0)/high(45.0) 等、同一セルで競合しうる実在の全族に対し常に劣後していた（3.210.0時点では
「旧来の挙動と完全一致」として意図的にそう実装）。ユーザー指示により、apt本体の実際の重み(1.0)と同格で
扱う（c2/c41/c42/c41s/c42s/fair/weekly と同じ重み1.0で表示優先度を競わせる）よう変更。
- **実装**: `MirrorKeys.weights` マップ自体には "aptLow"/"aptHigh" キーを追加しない。理由=このマップは
  `WeightTableCard`（設定タブ「重み表（最適化器と一致）」、プロ表示）が `entries.sortedByDescending{...}`
  で**全件をそのまま画面に描画**するため、ここに追加すると apt(1.0)とは別に aptLow/aptHigh の2行が
  重み表に余分に出現し（`breakdownLabels`に無ければ生キー文字列のまま表示＝operator_ux「英字符号を
  画面に出さない」に反する）、「重み表=最適化器の族と一致」という不変条件を壊す。
  代わりに `MirrorKeys.weightOf(family: String): Double` を新設し、aptLow/aptHigh を apt の重みへ
  エイリアスする専用解決関数とした（`weights["apt"]`を参照。他は従来どおり`weights[family] ?: 0.0`）。
  `mark`/`markNeed`/`markCount`/`cellFamilies`の重み優先比較（計4箇所）を全て`MirrorKeys.weights[...]`
  直参照から`MirrorKeys.weightOf(...)`経由へ置換。
- **効果**: `markCount`のキー空間("i,k")で実際に競合しうるのは c2(1.0)/low(90.0)/high(45.0)/
  aptLow・aptHigh(旧0.0→新1.0)のみ（c41/c42等はmarkNeed/mark側でありmarkCountとは別空間）。
  low/highは引き続き最優先（90/45 > 1.0）。c2とaptは同格(1.0)になり、同重み時は既存の「先勝ち
  (mark順維持)」規約により、呼出順が先のc2が優先されたまま（apt呼出はc2/low/highループの後に来る
  ため、tie-breakの実際の挙動は変わらない＝c2とaptが同一セルで同時に競合する場合のみc2が勝つのは
  従来と同じ、変わるのは**apt同士が別の弱い族に不当に負けなくなる**点）。`cellFamilies`（タップ時の
  全違反理由列挙・3.111.0系）のソート順にも同じ解決を適用し、aptLow/aptHighが他の重み1族と並んだとき
  末尾に固定されず正しく重み順に混ざるようにした。
- **表示専用の変更＝スコアリング不変**（`breakdown`/`inc`/`weightedScore`の計算経路には一切触れていない。
  `weightOf`は`countViolations`/`violations`/`needViolations`/`cellFamilies`という表示用マップの
  単一クラス解決にのみ使用）。HF77適合（重みでなく「表示優先度の解決規則」の変更だが、ユーザーの
  明示数値指示"重み1.0扱い"に対応する変更として実施）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。既存
  `countViolationsPrefersHeavierFamilyOverLighterAtSameCell`（low vs c2）・`AptPolishTest`
  （`breakdown["apt"]`のみ参照）はいずれも本変更の対象外の組合せのため無影響を確認済み。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## staffRange上限違反の構造的判定＋代用要員提示（3.242.0）
ユーザーが実データ(state.json)を提示し「桒澤美幸のAｱ超過」「上條洋平のDﾃ矛盾」「大島愛のapt矛盾」の
3ケースについて「アルゴリズムで新たに賢く対応する」ことを要求（grillingで「診断・提案のみ、データは
変更しない」方針を確認）。実データ精査の結果、3ケースは性質が異なると判明。
- **上條洋平（c1窓/Dﾃ）**: 既存の3.229.0「個人内壁検知」（staffRange個人上限×cons1窓ルール）で
  既にカバー済みと確認。追加対応不要（実データでもDﾃがcanDo=falseへ変更され解消済みを確認）。
- **桒澤美幸（staffRange上限/Aｱ）**: 3.98.0の「6b幻のapt目標検知」はapt(適切回数目標)専用で
  staffRange上限(hi)には未対応と判明。**新設「6c」**: 6bと同じ「担当レパートリーから強制される
  最低回数」ロジック（`count(k) >= T − Σ他シフト上限`）をstaffRange上限(hi)にも適用し、構造的に
  上限を守れない場合は「担当から外し代用要員(そのシフトを担当できる他の職員一覧)に置き換える」
  ことを提案する検査を`V6SanityPort.buildGuidance`へ追加。データは変更しない（HF77準拠）。
- **大島愛（apt/休・Pｼ）**: 手計算で検証した結果、大島の担当可能シフト(休/Pｼ/有)のうちPｼに個人
  上限が未設定のため、6b/6cの静的判定（他シフト上限合計で判定）では発火しない構造と判明。これは
  「真の構造的な壁」ではなく探索の研磨余地（AptPolishの効き）の可能性が高いと判断し、静的診断の
  対象外とした。代わりに、実際の配置状況を見る`buildViolationDebug`の「0b上下チェック」に、
  上限超過している職員へ「代用可N名」（そのシフトを担当できる他の職員数）を併記する軽量な情報
  追加を行い、大島のような「探索由来の超過」でも代用要員の有無が診断ログから分かるようにした。
- 検証: `rangeHiWallState`（休lo=hi=4固定・X上限2(対象)・Y上限3・T=10日の最小盤面、強制下限3>上限2で
  発火する構成）で3件のテスト（代用要員が案内されること・代用要員なしの案内・他シフト上限未設定なら
  誤検知しないこと=6bと同じ保守的判定）を追加。サンドボックスは Kotlin コンパイル不可＝ブレース/
  丸括弧/角括弧均衡を静的確認（「// N)」形式コメントの片括弧という既存ファイル全体の記法によるオフ
  セットは変更前後で比較し、差分のみコード部分に構文エラーが無いことを確認済み）。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## 専用freeオペレータの改善がdestroyRepairDayで相殺される順序バグを修正（3.241.0）
ユーザー指示「賢く網羅的に修正する」を受け、3.240.0で扱った「5ラウンド完全停滞」の残課題（covO focusが
選ばれるようになった=3.239.0のに8/26の休過剰1が依然未解消）を深掘り。covU/c41/c41s/c42/c42s/covOの
全focusに共通する設計上の欠陥を発見・修正。
- **根本原因**: `rsiGenerateHypothesis`はこれら6族すべてで「専用free関数→`repeat(6){destroyRepairDay}`」
  という順序だった。`destroyRepairDayAt`のrepair段階は**need>0のシフトのみ**を対象に埋め戻す設計
  （need<=0のシフトへの割当は一切修復しない）。covOはまさに「need<=0のシフト（休等）の過剰」が主対象
  であり、destroyのdestroy段階（非希望セルを休へ変える）で休の人数がさらに増えても、repair段階の
  対象外のため放置される。結果、直前に`applyCovOFree`が解消した休の過剰が、後続の
  `destroyRepairDay`（31日中6日をランダムに選ぶため無視できない確率で対象日が当たる）で再発し、
  そのまま最終hypothesisに残っていた。c41/c41s/c42/c42sの上限超過・違反ペア解消も同様にrepair段階の
  恩恵を受けない（c41/c41sの下限割れ・covUの不足はrepair段階（need>0のシフトを埋める設計）で自動的に
  再修復されるため実害は薄いが、過剰・違反ペア系は直接的な影響を受ける）。
- **修正**: 全6focusで順序を「`repeat(6){destroyRepairDay}`→専用free関数」へ統一。専用オペレータが
  必ず最後に実行されるため、その改善が確実にhypothesisの最終状態に残る。covUについてもdestroyRepairDay
  で新たに生じた穴を`applyCovUChains`が最後に一括処理できる利点があり、退行はない。
  探索オペレータの実行順序のみの変更でスコアリング不変・最終採否は既存のkeep-best(better)が担保。
- ユニットテスト（`V6NativeOptimizerChoiceTest`）: T=1日・休(need1=0)/A(need1=1,実質上限も1)/C(need未設定=
  完全無制限)の3シフト構成で、休の過剰1人(b)が「Aへは新たなcovOを作るため動けない・Cへのみ動ける」よう
  設計した最小盤面を使用。T=1日のためdestroyRepairDay(6回)は必ず同じ日を選ぶ＝決定的に検証できる。
  複数seed(1..5)いずれでも、新しい順序でhypothesisの最終状態でcovOが解消されることを固定（手計算で
  事前に全分岐を検証済み：destroy+repairは常にa=A,b=休へ収束し変化なし、その後のapplyCovOFreeがA候補を
  正しく拒否しCへ解決することを確認）。既存smokeテスト（rsiGenerateHypothesisC41/C42/CovOFocus...）は
  盤面サイズのみ検証しており順序変更の影響を受けないことを確認済み。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。実データでの効果（8/26のcovO解消等）は次回
  実機ログで確認（bench はRSI focus内部の実行順序を模擬できないため原理採否、3.74.0系と同方針）。

## RSI5ラウンド完全停滞の修正＝destroyRepairStaffの摂動過大を是正（3.240.0）
実機ログ解析でユーザーが発見「RSI 5ラウンド中total=321が一切変化しない」への対応。grillingで根本原因を
特定・修正方針を確定。
- **根本原因**: `rsiGenerateHypothesis`のapt/low/high/c2/weekly/fair focusは`repeat(8){destroyRepairStaff}`
  で、1回あたり最大T(日数)セルを変える「1人を全休化してから被覆穴のみ埋め直す」という激しい破壊を
  最大8回連続適用していた。covU focusの`destroyRepairDay`（1回あたり最大S(職員数)セル、repeat(6)）と
  比べ、実機データ(S=10,T=31)では総攪乱セル数が桁違いに大きい（最大8×31=248 vs 6×10=60）。この過大な
  摂動から60秒/ラウンドのSA/ALNSでは破壊前の解に匹敵する状態まで回復しきれず、`runV5`の入力比番兵
  （3.97.0、hypothesis自体との比較でラウンド開始時のbestとは無関係に作用）もこれを防げないため、
  5ラウンド全てtotal不変のまま予算を使い切っていた。
- **検討した代替案（不採用）**: 入力比番兵の比較基準を「hypothesis自体」から「ラウンド開始時のbest」へ
  変更する案。しかし`runRsi`のラウンド境界には既に`better(candReport, bestReport)`という同等の外側
  ゲートが存在するため、番兵側を変えても機能的に重複するだけで探索の中身（SA/ALNSが60秒で改善を
  見つけられるか）には影響しないと判明し、根本対策にならないため見送り。
- **採用した修正**: `destroyRepairStaffReps(s, t)`を新設し、destroyRepairDay基準（6*S、covU focusの
  総攪乱セル数）に揃うよう反復回数を動的計算（`max(1, (6*S+T-1)/T)`、切り上げ）。S>=Tのデータでは
  従来のrepeat(8)相当以上（>=6）を維持し攪乱強度を落とさない＝退化しない設計。固定repeat(8)を置換。
  探索オペレータの摂動強度のみの変更でスコアリング不変・最終採否は既存のkeep-best(better)が担保。
- ユニットテスト3件（`V6NativeOptimizerChoiceTest`）: S=10,T=31で従来より大幅に小さい値(2)になること・
  S>=Tでは6以上を維持すること・下限1を割らないこと。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。実データでの効果（5ラウンド停滞の解消）は
  次回実機ログで確認（bench はRSIラウンド内の摂動強度を模擬できないため原理採否、3.74.0系と同方針）。

## RangePolishのペアスワップ新設＋maxViolatedFamily最終ラウンド枠の固定順バグ修正（3.239.0）
ユーザー指摘2件への対応。①「RangePolish: 桒澤美幸Aｱ『候補なし×6』・上條洋平Dﾃ『候補なし×14』」の
拡張（grillingで「複数ターゲット同時解決」を選択）②「8/26(水)休 過剰1『動かせる1人』なのに未解消」の
根本原因調査から発見したfocus選択バグの修正。
- **[RangePolishペアスワップ新設]**: `applyRangePolish`に`tryPairSwap(hi,k,lo)`を追加。同一シフトkに
  ついてhigh(超過)のhiとlow(不足)のloが両方存在する場合、`findCovUChain`の玉突き探索を経由せず、
  直接のペアスワップ(hiのk保有日を1日loへ振替え・loの元シフトをhiが引き受ける)を最優先で試す。
  被覆(covU/covO)は完全保存(同日2者の役割入替のみ)のため、玉突き連鎖が構造的に見つからない
  (=「候補なし」)局面でも確実に解決できる。HIGH/LOWループの両方で、既存のtryRelocateより先に
  同一シフトの相手を探して試す。
- **[maxViolatedFamily最終ラウンド枠のバグ修正]**: 8/26のcovO過剰1が未解消だった根本原因を追跡した
  ところ、`maxViolatedFamily`（RSI探索のfocus選択）の最終ラウンド保証枠が「apt→covOの固定順」で
  aptを先にチェックしていた（3.208.0時点、7本のログ全てでapt<covOだったため「aptは常にcovOより
  不利」という前提で固定順にした）。しかし今回のデータでは**apt=29 > covO=4**と前提が逆転しており、
  5ラウンドRSI中covUがHARDとして数ラウンド粘り周期枠(round%3==2)もHARD優先ループに食われ、最終
  ラウンドの保証枠でaptが先にreturnするためcovOには一度も到達しなかった。修正: 最終ラウンドで両方
  candidateになる場合のみ、実際の件数を比較し「より少ない方（件数最大選択に絶対勝てない方）」を
  優先するよう変更。通常ラウンド(round%3==1/2の単独枠)は無変更。
- ユニットテスト: `RangePolishTest`は既存3.215.0のケースに影響なし（新規のペアスワップ試行が先に
  走っても、対象がいなければ従来のtryRelocateへフォールバックするため後方互換）。
  `V6NativeOptimizerChoiceTest`に`maxViolatedFamilyFinalRoundPrefersCovOWhenAptIsLarger`
  （apt=29>covO=4でcovOが選ばれることを固定）を追加、既存の
  `maxViolatedFamilyFinalRoundPrefersAptOverCovOWhenBothPresent`はコメントのみ「固定順→件数比較」
  へ訂正（結果は偶然一致=apt=1<covO=6のため変わらず）。
- 探索オペレータ/focus選択のみの変更＝重み・スコアリング不変。最終採否は既存のisBetter/keep-bestが
  担保するため退化不能。実データでの効果（8/26のcovO解消・美幸/上條の候補なし解消）は次回実機ログで
  確認（bench はRSI focus/RangePolish内部を模擬できないため3.74.0系と同じ原理採否）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。CI（1回目）で
  手R3の新規テストが1件失敗（単独職員・need無しのテストデータで手Bが「Xを直接追加」してfires=0を
  達成してしまい、意図した「回数保存の再配置」検証にならなかった見落とし）→staffRangeでX上限を
  現在の保有回数に固定し修正、CI再実行で確認。最終判定は CI（v6-engine-check の testDebugUnitTest／
  Release Build）。

## C1研磨に「職員内シフト配置の全ペア網羅再配置」を追加＝手R3（3.238.0）
ユーザー指摘「休の個人上限10<窓ルール最低必要回数12」という真の構造的矛盾（2b-3個人内壁診断で確定済み）
に対しても「賢く深く網羅的に違反研磨するアルゴリズムを新たに作る」との指示。grillingで2問確定:
①探索戦略＝**局所探索を強化**（DP/ILPによる厳密最適化は3.200.0で「正しさのリスクが実装前から顕在化」
として不採用済みの経緯を踏襲し、再挑戦しない）②適用対象＝**残存c1違反のある全職員**（壁の有無を問わず、
既存の狭い近傍だけでは見つからない改善機会を拾う）。
- **背景**: 既存`applyC1WindowPolish`の手A(同日交換)/R1(鏡像長方形)/R2(自己2日swap)/手B(直接移動+
  玉突き)は、いずれも「現在違反しているセルj」を**アンカーに限定**した局所改善のみで、その職員の
  シフト配置パターン全体を作り直す大きな手を一度も試していなかった。手R2はさらに`donors()`（"抜いても
  新規fireしない余剰位置"の事前フィルタ）でも候補を絞るため、donorsが構造的に空になる配置（各保有日が
  単独でその窓のちょうど閾値を構成している）では手R2自体が0回転で終わる。真に壁がある職員（休の回数を
  変えられない）でも、休の「配置の仕方」次第で窓違反件数は変動しうるが、既存の手はこの余地を探索して
  いなかった。
- **手R3新設**（`applyC1WindowPolish`内、既存フィックスポイントループの直後に1回だけ実行）:
  まだ不足しているルール(x,d,n)を持つ職員について、xの保有movable日集合(Hx)×非保有movable日集合(Ho)の
  **全ペア(アンカー限定なし)**を評価し、職員全体の`c1RowFires`(全cons1横断のfire数)が最も改善するペアを
  採用(best-improvement)。安全性は既存の手R2と同一の被覆ガード(`covUCell`)＋`makesForbiddenRun`事前
  枝刈り＋`isBetter`最終ゲート。真に壁がある場合は全ペアがgain<=0のまま尽き、安全に諦める（退化不能）。
  計算量はO(|Hx|×|Ho|)/職員/ルールで、`c1RowFires`は軽量な純Kotlin計算のため実運用規模（10職員×31日×
  数ルール）で許容範囲。ログに「再配置:N」を追加、頭打ち理由(`blockStats`)にも「再配置候補なし」を
  記録し既存の「残存」表示に合流。
- ユニットテスト2件（`C1RelocationPolishTest`）: ①単独職員・donors構造的に空・findCovUChainも候補なし
  （既存の手A/R1/R2/手Bが全滅する局面をPythonで独立に手計算検証）で、手R3のみがc1を完全解消することを
  固定 ②既に窓を全カバーする最適配置ではno-op（採用0）であることを固定。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。実データでの効果（古泉健一等8名の休窓・
  上條洋平のDﾃ窓）は次回実機ログで確認（真の壁のため完全解消はしないが、部分改善の余地があるかは
  データ依存＝bench不能・原理採否、3.74.0/3.169.0と同方針）。

## 進捗の「残り時間」表示が繰り返しリセットされる不具合修正（3.237.0）
ユーザー報告「リセット5分が何度もされる」→AskUserQuestionで確認し「進捗バー/残り時間表示が『5分』から
何度も巡回するように見える」と判明。
- **原因**: `progressSummary()`（`MagiScheduleViews.kt`）の「残り」表示は `budgetSec*1000 - ui.elapsedMs`
  で残り時間を算出するが、`ui.elapsedMs` に渡していたのは最適化エンジンの `onProgress(phase, report, iters,
  elapsed)` コールバックの **`elapsed`（フェーズ境界=V5シード→ALNS→RSI各ラウンド等で巻き戻るローカル時計）**
  そのものだった。この非単調性自体は既知（HF63タイミング用に3.102.2で壁時計へ修正済み、操作ログのスロットル
  判定も同様に対処済み）だったが、UI表示に直結する`elapsedMs`だけ対処漏れだった。フェーズが切り替わるたびに
  `elapsed`がほぼ0へ巻き戻り、「残り」が5:00近くへ何度も戻って見えていた（実機の20〜90秒間隔のフェーズ遷移と
  症状の周期が一致）。
- **修正**: 2箇所とも壁時計基準へ統一。①`MagiViewModel.runV6FullOptimize()`のonProgress内: 既存の`startMs`
  （関数冒頭で`System.currentTimeMillis()`取得済み）を使い`elapsedMs = System.currentTimeMillis() - startMs`
  へ。②`OptimizationWorker.doWork()`のonProgress内: `wallStart`を新設し`wallElapsed = System.currentTimeMillis()
  - wallStart`を計算、`BgProgress`への publish・会話バブルの経過表示・2つのスロットル判定（バブル更新1.5秒間引き・
  スナップショット8秒間引き）の計4箇所を全て`elapsed`→`wallElapsed`へ置換（バックグラウンド経路は
  `OptimizationRepository.progress`経由でUI側`elapsedMs`とAndroid 17会話バブルの両方に伝播するため、この1箇所の
  修正で両方の表示が直る）。表示専用の時計選択のみ・最適化ロジック/スコアリング/探索の受理判定は完全に不変。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## FairPolish・C3PatternPolish新設＋実機報告2件の修正（3.235.0）
ユーザー指示「c42/c42s以外(c3/c3m 2-3者交換・fair・weekly等)にも『動かせるか』専用オペレータの欠如が
無いか、包括的な棚卸しを実施する」への回答として棚卸し（applyC3SequencePolish/applyBlockRotationPolish/
applyGroupShiftEqualizePolish/applyWeeklyRebalancePolishを精読）し、ユーザー承認（両方実装）を受けて対応。
併せて実機で受けた2件の報告（「グループが削除出来ない」「休の適切回数合計チェックが誤検知」）も同セッションで解消。
- **棚卸し結果**: c1/c3単一シフト連/c3n/c3mn/covU/covO/c41/c41s/c42/c42s/aptは全てfindCovUChain連鎖済み。
  **c3/c3mの複数シフトMUSTパターン**（例: X→Y必須連続、非single-shift）は3.216.0で「既存機構(2-3者交換/回転)
  のまま対象外」と明記されスコープ外のまま＝c41/c42と同型の穴。**fair(群内公平化)**はapt(3.223.0)と構造が
  ほぼ同型なのに専用研磨（自己振替→相互交換→チェーン）が無く同日2者スワップのみ＝同型の穴。
  **weekly**は既存`applyWeeklyRebalancePolish`(3.197.0)が候補を事前に絞らず全職員総当たりで長方形交換相手を
  探しており、c41/c42のような「単純な穴」ではない（拡張するなら2-hop長方形連鎖等の新設計が要る）＝優先度低・
  今回は対象外。
- **`applyFairPolish`新設**（V6HotfixPasses.kt）: AptPolish(3.223.0)と同型の3段構成（①自己振替 ②同一グループ
  内相互交換 ③玉突きチェーン）をfair向けに移植。fairの目標(群×シフトのround(平均))はaptの固定目標と異なり
  現在の配置に応じて動くが、各手の採否は常にisBetter(実目的関数)が担うため近似の粗さは安全性を損なわない。
  アンカーは`report.distLocations["fair"]`（3.149.0で追加済みの偏り箇所リスト）。`runPostOptimization`の
  フィックスポイント巡回にAptPolishの直後として配線。
- **`applyC3PatternPolish`新設**（V6HotfixPasses.kt）: cons3/cons3mのうち複数シフトMUST/Wantパターン
  （`!C3Run.isSingleShiftSeq`）専用。`MirrorCore.checkC3Family`の非forbidden複数シフト分岐は
  「schedule[i][j]==seq[0]かつ残り(d-1)日が全一致しない」を窓先頭セルへ計上するモデルのため、「日jのseq[0]を
  別シフトへ変え、パターンの起点自体を崩す」だけで違反インスタンスが消える（残り日を完成させる方向＝パターン
  完成は複数日の依存関係が絡み正しさの保証が難しいため意図的にスコープ外＝既存の2-3者交換/回転パスに委ねる、
  見送っても既存機構が担当を続けるだけで安全側）。C3mnPolish(3.214.0)と同一の「1セル付け替え＋findCovUChain
  玉突き」パターン。`runPostOptimization`にC3RunPolishの直後として配線。
- 両者とも探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は既存のラウンド`isBetter`
  keep-best（hard→total→weighted辞書式）が担保するため退化不能。
- ユニットテスト: `FairPolishTest`4件（自己振替が厳密に0まで解消／2人が独立に自己振替で解消／自己振替・
  相互交換とも希望固定/covU制約でブロックされチェーンのみが唯一の経路となる局面で改善かつcovU/HARD安全・
  既に均等なら即no-op）。`C3PatternPolishTest`3件（need1で唯一の担当者に絞りチェーンが唯一の経路となる
  局面で解消・covU/HARD安全／複数シフト規則が無ければno-op／単一シフト連はC3RunPolishの担当のため対象外
  で即no-op）。
- **[実機報告①] 「グループが削除出来ない」**: `Ws1Card`（Ws1Editor.kt）のグループ削除ボタンは
  `vm.ws1CanRemoveGroup(g)`（`groups.size>1`）が偽だと**理由の説明なく完全に非表示**になっていた
  （休シフトの削除不可には明示メッセージがあるのに、グループ/職員/シフトの「最後の1件」ガードには
  無かった非対称）。残り1グループのときだけ「最後の1グループは削除できません（担当可否の分類が無くなる
  ため）」という説明文を追加。表示のみ・スコアリング不変。
- **[実機報告②] 「『休』の適切回数の合計が101回ですが、必要数の合計は0回」誤検知**: `V6SanityPort.kt`の
  事前診断チェック6-C（`aptSum(適切回数の職員別合計) > seatsHi(need1/need2の日別合計)`）が、休(restIdx)にも
  無条件に適用されていた。休は「1日に何人休んでよいか」という座席上限の概念を持たず、`need1=0`(または未設定)
  は「座席が無い」ことの表現であり「休むべきでない」ではない——この検査は座席数が有限な勤務シフト向けの設計
  のため、休だけは需要上限0でも矛盾ではない（3.76.0のC1壁判定等、既存の「休は特別扱い」方針と整合）。
  `k != p.restIdx`ガードを追加し休をこの検査対象から除外。チェックA(下限合計)/B(上限合計)は今回のスコープ外
  （ユーザー確認済み、6-Cのみ対応）。回帰テスト`aptSumCheckSkipsRestShiftButStillFlagsWorkShift`
  （同一設定の非休シフトでは従来どおり検出されることも同時に固定）。読取専用の診断修正のみ・スコアリング不変。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## c42/c42s専用repair新設＝covO・c41,c41sと同型の穴を横展開（3.233.0）
ユーザー指示「他の制約など改善できるようにする」を受け、findCovUChainの`maxDepth`が恩恵を与える
呼出元を棚卸しした結果、covO(3.204.0)・c41/c41s(3.209.0)が既に持つ「動かせるか」専用オペレータを
**c42/c42s(群ペア禁止: 群g1のs1×群g2のs2が同日に同時発生禁止)だけが欠いている**ことを発見。
c42/c42sは`mark(i,j,"c42")`で`report.violations`(セルマップ)には載るため`destroyRepairViolations`の
汎用ランダム再割当は一応届くが、希望固定/禁止連続/被覆悪化を避けて実際に動かす専用オペレータが
無かった（covO/c41と全く同じ欠落パターンの第3世代）。
- **`applyC42Free`新設**（V6NativeOptimizer.kt）: 違反ペア(left∈g1×s1, right∈g2×s2)のどちらか
  一方を実際に他シフトへ動かして崩す。移動先でcovOが悪化しない候補を探し、離脱元でcovUが悪化する
  ならfindCovUChainで玉突きフォールバック。**c41Free(3.209.0)で判明済みの罠を踏襲**: 「離脱を
  先にschedへ適用してからfindCovUChainを呼ぶ」順序を厳守（逆順だと本人がまだ在籍中に見えて
  常にnullが返る実バグを再発させないため）。skill=false は cons42(sgrp)、skill=true は
  cons42s(ssk) を対象にする（DRY化、c41Freeと同じパラメータ設計）。
- **配線**: `rsiGenerateHypothesis`の`when(focus)`に"c42"/"c42s"ケースを追加し
  `applyC42Free`＋`destroyRepairDay`×6（covO/c41と同型の構成）へルーティング。
- 探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は既存のラウンド`better()`keep-best
  （hard→total→weighted辞書式）が担保するため退化不能。
- ユニットテスト5件: `applyC42FreeResolvesFreelyMovablePair`(需要なしシフトで直接移動が解決)、
  `applyC42FreeLeavesWishPinnedPairUntouched`(両者希望固定で何もしない)、
  `applyC42FreeIsNoOpWhenRulesEmpty`、`applyC42FreeResolvesViaChainWhenDirectMoveWouldCreateCovU`
  (離脱元がneed1をちょうど単独充足する構造的ブロック局面を玉突きで解消)、
  `rsiGenerateHypothesisC42FocusReturnsValidSchedule`(focus="c42"/"c42s"のsmokeテスト)。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。実機での効果（c42の
  残存件数減少）は次回実機ログで確認。

## findCovUChainのmaxDepth既定を引き上げ＝depth5の壁を撤廃（3.232.0）
3.229.0(個人内壁検知)の副産物として「桒澤美幸のAｱ超過はRangePolishで候補なし」を深掘りしたところ、
当初の仮説（移動元の窓保持を見ていない）は`V6SearchOperators.kt`の実装確認で**誤りと判明**:
`findCovUChain`のBFSは`candidates()`が全候補を返し`frontier`の全ノードを試す真に網羅的な探索
（rngは順序のみに影響、乱数の運では見逃さない）。「候補なし」は**maxDepth=5以内に真に解が存在しない**
ことを意味していた。
- **発見**: `visited`はシフト単位(`BooleanArray(p.K)`)で管理されるため、本BFSは元々シフト数Kを超えて
  展開できない＝maxDepthをK以上にしても計算量は増えない（自然にO(K×S)で頭打ち）。旧既定5
  （「最大5人の玉突き」という人間の検証しやすさ重視の設計意図）は、実データ(K=11)でこの上限より
  深い箇所にのみ解が存在する場合に誤って「候補なし」と諦めてしまっていた可能性がある。
- grillingで方針確認（AskUserQuestion）: 「全呼出先をK-1まで引き上げる」を採用
  （計算コストはシフト数で自然に上限されほぼ無視できるため、5人までの設計意図より網羅性を優先）。
- **`findCovUChain`/`tryFixForbiddenRunViaAdjacentDay`の`maxDepth`既定値を`5`→`(p.K-1).coerceAtLeast(1)`
  へ変更**（全呼出元（RangePolish/RSI covU focus/C1Polish/applyCovOFree等）は明示的にmaxDepthを
  渡していないため、この1行の変更だけで一律に恩恵を受ける）。既存テスト(depth1/2/3/5カスケード)への
  影響なし: `visited`のシフト単位ゲートにより実効深さは元々Kで自然に頭打ちしていたため、K<=6程度の
  小さいテスト用フィクスチャでは新旧の既定値差が実際の探索結果に影響しない（BFSは最短解を先に見つける
  ため、解が存在する深さがmaxDepth以内である限り同じ深さ・同じ内容の解が見つかる）。
- ユニットテスト`chainFillFindsDepth6ChainOnlyReachableWithRaisedDefaultMaxDepth`
  （既存のdepth5カスケードを1段延長したK=8の一本道盤面。解は深さ6にのみ存在し、
  ①明示的に`maxDepth=5`を指定すると見つからない(null) ②既定値(=K-1=7)なら見つかる、
  の両方を固定し「旧既定では解けなかった深さの解が新既定では解ける」ことを直接検証）。
- 探索深さ上限の緩和のみ＝スコアリング不変・退化不能（最終採否は呼び出し側のkeep-best/isBetterが
  従来どおり担保）。実データでの効果（美幸のAｱ超過・8/6のCｱ不足が実際に解消されるか）は次回実機ログで確認
  （bench はこの種の探索深さ変更を模擬できないため、3.74.0系と同じ「原理ベースで採否」で対応）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## ドッグフーディング4トピックの一括改善（3.228.0〜3.231.0）
ユーザー指示「ドッグフーディングして、賢く深く網羅的に改善できるようにする」を受け、直前のQ&A
（美幸のC1停滞・停滞脱出の時間・RSI5ラウンド全滅・8仮説の多様性）で見つけた4つの疑問を
grillingで議題化した後、実データ(ログ)ベースで深掘りし一括対応。

### 3.228.0: 仮説役割多様性(explore/accept/opSelect)をi>=5でも多様化
自分自身の3.225.0（仮説数固定上限5の撤廃）が引き起こした具体バグ。`ROLE_EXPLORE`配列は5要素固定、
`roleAcceptFor`/`roleOpSelectFor`もi=1〜4しか分岐しないため、**i>=5の仮説は全てi=0(ベースライン)と
探索倍率・受理方式・演算子選択が完全に同一**（種(seed)以外区別不能なクローン）になっていた。
実機ログで確認: workers=8実行時「仮説検証: 各仮説8本の結果...相異なる解=1件」（全8仮説が同一解に
収束）。i<5の既存値・既存テストは一切変更せず、i>=5だけ拡張:
- `roleExploreFor`: 黄金比の低食い違い列(golden-ratio low-discrepancy sequence)で[0.35,2.4]へ
  決定的かつ非周期的に写像（単純延長・循環だとi=5%5=0で結局ベースラインに戻るクローン問題を
  繰り返すため、周期を持たない生成式を採用）。
- `roleAcceptFor`/`roleOpSelectFor`: i%3・偶奇でGD/LAM/SA・THOMPSON/ROULETTEを巡回させ多様化。
- テスト: `roleProfilesDiversifyBeyondOldFixedArraySize`（i=5..12の8個が相異なる値・旧クローン値
  1.0への非縮退・値域確認、accept/opSelectも複数種に分散）、
  `roleProfilesForIndicesBelowFiveAreUnaffectedByDiversification`（i<5回帰）。

### 3.229.0: staffRange個人上限×cons1窓ルールの個人内壁検知
既存の壁/ダイヤル分類器(3.76.0)は全体供給(集計)のみ判定するため、「集計では担当者が大勢いても、
この1人だけは自分の個人上限のせいで自分の窓ルールを満たせない」局面（例: 桒澤美幸のAｱ上限2×
「14日窓Aｱ≥1」）を検知できていなかった。2b-2と同じ保守的下界（非重複窓: day2×floor(T/day1)）を
個人の`rangeHi`と突き合わせ、上限がこの下界を下回るなら個人内で構造的に不能と案内する新検査
（2b-3）を`V6SanityPort.buildGuidance`に追加。
**[重要=当初の仮説を訂正]** 美幸の実際の設定(個人上限2, 保守的下界も2)を検証したところ
**「上限==下界」で誤検知しない**（false wallと判定しない、理論上ぎりぎり満たせるため）ことを
確認。これは「美幸のケースは壁かもしれない」という前セッションの仮説を覆す発見——彼女の停滞は
データの構造的矛盾ではなく、探索が最適な配置（正確に2回、正しい位置へ）を見つけていないことが
真因である可能性が高いと訂正した。
- テスト: `personalC1WallDetectsWhenRangeHiBelowConservativeMinimum`(上限1<下界2で発火)、
  `personalC1WallDoesNotFalselyFlagBorderlineSatisfiableCase`(上限2==下界2で誤検知しないこと=
  美幸の実例の訂正を固定)、`personalC1WallIgnoresStaffWithoutPersonalCap`(上限未設定は対象外)。
- 読取専用の新規診断のみ・スコアリング不変。

### 3.230.0: 停滞ウォッチドッグを「フェーズ公平猶予」と「真の頭打ち検知」に分離
`V6FinalPort`の停滞早期脱出ウォッチドッグが、`max(lastBestImproveMs, lastPhaseChangeMs)`を単一の
stallMs(=予算9/10、300s予算で270s)と比較していたため、20〜90秒間隔で頻発するフェーズ遷移
（RSI各ラウンド・ALNS各restart等）のたびにタイマがリセットされ続け、**270秒という長い閾値には
実質的に一度も到達し得なかった**（実機ログでPhase1完了直後から270秒以上一切改善が無いまま予算を
使い切る事例=RSI 5ラウンド全ラウンドtotal不変・ALNS 2restartとも悪化、を確認）。
「現フェーズ自身に短い個別猶予(phaseGraceMs)を与える」ことと「真の頭打ち検知(lastBestImproveMs単独、
フェーズ遷移でリセットしない)」を分離したAND条件へ変更。既存の`stallMs`/`stallHardMs`/`minRunMs`の
数値自体は一切変更せず（機構のバグのみ修正・過去に何度も往復した数値チューニングの再燃を避ける）、
判定を`watchdogStagnationFired`という純関数へ抽出しユニットテスト可能にした。
- テスト: `firesOnTrueStagnationDespiteFrequentPhaseTransitions`（フェーズが頻繁に切り替わり続けても
  真の無改善時間がeffStallを超えれば発火すること。旧ロジック相当(`max()`合成)ではこの状況で
  発火し得なかったことも同テスト内で確認）、`doesNotFireWhenCurrentPhaseJustStarted`（現フェーズが
  始まったばかりなら誤検知しない）、`doesNotFireBeforeMinRunElapses`、`doesNotFireWhileImprovementsAreRecent`。
- スコアリング不変（keep-best=良化のみ採用は不変。早期終了はこれまでどおり品質を落とさない）。

### 3.231.0: HF63閾値をRSIラウンド予算に応じて動的化
`Hf63Infeasibility`の停滞加算はラウンド粒度の呼出に固定effortIters=1800/roundを渡しており、
INFEAS_STALL_ITERS=5000到達に約3ラウンドの同族focusを要した。E9冷却(1ラウンド休止)が2〜3の
詰んだ族を交互に切替える実運用（実機ログ: covU/apt/covU/c1/covUと交互）では、rounds=5の典型的な
短予算だと3回目のfocusがround1,3,5＝**最終ラウンドでようやく成立し、deprioritizeが成立しても
振り向け先の残りラウンドが無かった**（全5ラウンドがtotal=325のまま完全に停滞していた実例で確認）。
`rsiHf63EffortIters(rounds, reserveRounds=2)`を新設し、effortItersをroundsに応じて動的に決定
（E9の1-in-2交互を想定しattemptsTarget=ceil((rounds-reserveRounds)/2)、下限2=一度の不運な1ラウンド
だけではdeprioritizeしない=E9のより軽い1R冷却との役割分担を保つ）。rounds=5なら2回のfocusで
5000へ到達しround1,3で成立・round4,5を振り向けに残せるようになる。roundsが大きいほど
attemptsTargetも緩み、旧来同様じっくり粘れる。
- テスト: `rsiHf63EffortItersReachesThresholdInTwoAttemptsForTypicalRoundBudget`（rounds=5で2回到達・
  1回では到達しないこと）、`rsiHf63EffortItersNeverDropsBelowTwoAttempts`、
  `rsiHf63EffortItersRelaxesForLargerRoundBudgets`（roundsが大きいほど緩むこと）。
- focus選択のみの変更でスコアリング不変（keep-best=better()が最終結果を担保、deprioritizeは
  「無駄な振り向けを避ける」効果のみで退化不能）。HF77非該当（探索内部の族選択パラメータであり
  MirrorCoreの重み表とは無関係）。

検証（4トピック共通）: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。実機での効果測定は次回ログで確認
（3.229.0除く3トピックは実行時の探索動学に作用するため、bench は RSI focus/watchdog を模擬できない
という過去の教訓＝3.74.0/3.95.0/3.169.0と同じ「原理ベースで採否」で対応。keep-best/番兵は全て不変で
退化不能のため安全）。

## c1違反の職員×窓ルール別件数をログへ出力（3.227.0）
ユーザー質問「桒澤美幸がC1研磨しないのか?」の調査で、「違反詳細 c1(27件)」がDETAIL_CAP=8で打ち切られ
「他19件」が隠れており、特定職員（美幸）が具体的にどの窓ルール（休の5日窓 vs Aｱの14日窓など）で何件
違反しているか読み取れないと判明。ユーザー指示「美幸がこの休窓違反として実際に何件あるかログに出力する」
を受け対応。
- **`V6SanityPort.buildViolationDebug`に新セクション追加**: `MirrorCore.checkC1Family`の窓スライド
  ロジック（`for (c in p.cons1) { for (i in 0 until p.S) { ... 窓スライド ... } }`、違反ランの先頭
  セルのみ計上=`!prevViol`ゲート）を読取専用で忠実に再実装し、DETAIL_CAPによる打ち切り無しで**職員×
  窓ルール別の全件件数**を1行サマリとして出力（例:「c1内訳（職員×窓ルール別件数・全件）:
  桒澤美幸 Aｱ(14日窓≥1)2件, 休(5日窓≥2)1件 / 古泉 健一 A4(?日窓≥?)1件 / ...」）。既存の
  「違反詳細 c1(N件): ... …他M件」（打ち切りあり・セル位置つき）は変更せず併存。
  `report.breakdown["c1"]>0`のときのみ計算（無関係な計算コストを回避）。
- 読取専用・スコアリング不変（重み・探索・評価器は無変更、診断表示の追加のみ）。
- ユニットテスト`V6SanityPortTest.violationDebugReportsC1CountsPerStaffAndRule`:
  2職員×7日の最小盤面（s0は最初の5日がA固定で「休5日窓≥2」ルールに1件違反、s1は休/A交互で違反なし）
  を構築し、新サマリ行に「s0 休(5日窓≥2)1件」が正確に出力され、違反のないs1は現れないことを固定。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## covOの自動解消に隣接日調整を追加＝禁止連続で全候補が塞がる局面を突破（3.226.0）
実機ログ（8/26(水) 休 必要0/現状1(過剰1)）で「動かせる0人・玉突き必要0人・希望固定0人・禁止連続1人」と
診断され、covOが自動では解消されない事例をユーザーが提示。「26日を改善できるようにアルゴリズムを改良する」
指示を受けgrilling（AskUserQuestion 1問）で方針確認のうえ実装。
- **原因**: `applyCovOFree`（3.204.0/covO専用repair）は、過剰シフトの在勤者を他シフトへ移す際
  `p.makesForbiddenRun(sched, i, j, m)` が真の候補を即 `continue` で見送り、移動先が全て禁止連続(c3n)で
  塞がるセルはそのまま諦めていた（設計時から意図的な「安全側」挙動と明記されていたが、findCovUChain
  （covU側、3.163.0）に既にある「隣接日調整で禁止連続パターンを崩してから再挑戦する」機構がcovO側には
  無かった＝covU/c41(3.209.0)には備わっていた突破口がcovOにだけ欠けていた非対称）。
- **`tryFixForbiddenRunViaAdjacentDay`を共通ヘルパーへ抽出**（V6SearchOperators.kt）: findCovUChain内に
  ローカル関数として実装されていた隣接日調整ロジック（i を day j2(=j-1/j+1) で別シフトへ変えてパターンを
  崩し、空くシフトのcovU悪化はfindCovUChainへ1段だけ再帰して玉突き埋め直し）をトップレベル関数へ切り出し
  （DRY化）。findCovUChain側は2行の委譲呼び出しへ簡略化・挙動は完全に不変（同一ロジックの抽出のみ）。
- **`applyCovOFree`に配線**（V6NativeOptimizer.kt）: 直接移動が禁止連続で塞がる場合、即 continue せず
  `tryFixForbiddenRunViaAdjacentDay`を試す。ヘルパーが返す隣接日側の手を実際に適用しcov[j2]を再集計した
  うえで、**移動先mでcovOが新たに悪化しないか**を追加確認（ヘルパー自体はcovU/禁止連続しか見ないため、
  covO側の安全性はapplyCovOFree側で担保）。悪化するなら隣接日側の変更ごと巻き戻して次の候補へ。
- 探索オペレータの追加分岐のみ＝スコアリング不変。`rsiGenerateHypothesis`のRSI covO focusはラウンド単位
  の`better()`keep-bestで最終ゲートされるため退化不能（26日相当のケースも次回実行で解消され得るが、
  前後日の割当が調整可能かはデータ依存＝必ず解消を保証するものではなく「突破口を追加」するのみ）。
- ユニットテスト`applyCovOFreeResolvesViaAdjacentDayFixWhenAllDirectMovesAreForbidden`（8/26相当を最小
  再現: covO対象の唯一の在勤者が、移動先候補2つとも禁止連続(c3n)で塞がる盤面。旧実装なら解消0のまま
  だったところ、隣接日調整でcovO=0・c3n=0まで解消されることを固定）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 仮説数の固定上限(5)を撤廃＝ワーカー設定まで仮説を増やす（3.225.0）
3.224.0（外部並行/並列処理レビュー）の実装中、レビュー#6を「hypothesisChainPlanの欠陥ではなく、
w(仮説数)を仕様上5に固定する設計に由来する構造的下限」と評価し直した際、ユーザーから直接
「仮説数は最低5」と指摘を受けた。grillingで意図を確認したところ、ユーザーの真意は**5を下限でなく
上限からも撤廃し、ワーカー設定値まで仮説数そのものを増やす**ことだった（AskUserQuestionでの往復の末、
「はい、その通り(5上限を撤廃しworkersまで仮説を増やす)」と明示確認）。
- **背景**: 旧 `optimize()` は `val w = options.workers.coerceIn(1, MAX_HYPOTHESES=5)` で仮説数を
  5に固定し、workers>5の余剰は`hypothesisChainPlan`経由で各仮説の**内部並列度**（SAチェーン数/ALNS
  多チェーン）へ配分していた（3.211.0〜3.212.0で「余剰を無駄にしない」ために作った仕組み）。今回の
  指示は「深さでなく多様性を優先する」という設計変更＝内部並列度を増やすのではなく**仮説の本数自体**を
  ワーカー設定まで増やす。
- **`hypothesisCount(workers) = max(2, workers)`** を新設（`V6NativeOptimizer.kt`）。下限2
  （workers=1でも最低2仮説の多様探索を意図的に保証。1ワーカー分の予算を2仮説でオーバーサブスクライブ
  する）・上限なし（workers自体が上限）。`optimize()`の`w`計算をこの関数へ置換。
  `hypothesisChainPlan(options.workers, w)`は無変更（関数自体は既に「仮説ごとに最低1本、余りを配分」の
  一般ロジックのため対応不要）だが、workers>=2のとき`h=w=workers`となり`distributable=max(h,min(workers,
  cores))=workers`に恒等し、内部並列度は**通常すべて1に収束**（多様性が増えた分、深さ方向の恩恵は
  ほぼ不要になる。旧来の「6〜9帯で内部並列2〜3本」という余剰活用の出番は無くなるが、無害＝関数自体は
  workers<hypothesesの縮退呼出（ExtraRefine等）のために温存）。
- **`MAX_HYPOTHESES=5`定数は撤廃せず用途を転用**: ①`ExtraRefine`（微小予算5〜25sの追加精製、
  `V6FinalPort.kt`）は仮説内多チェーンの固定費（入口hf67+フルcheck×2+nativeハンドル生成）が小予算を
  侵食するため、**意図的に**旧来の5×1構成を維持（本変更のスコープ外・据え置き）。②`hypothesisChainPlan`
  のデフォルト引数。本体の仮説数計算からは外れた（`hypothesisCount`に一本化）。
- **表示の同期**: `V6FinalPort.kt`の診断ログ用`effHypotheses`と`MagiSetupCards.kt`の設定タブ注記を
  両方`V6NativeOptimizer.hypothesisCount`から導出するよう統一（独立再計算による乖離を防ぐ、3.212.0と
  同じ設計原則）。UI注記は「5本上限・超過分は内部並列」という旧説明を撤去し「設定値がそのまま仮説（案）の
  数になる」＋コア数超過時のみ電池/発熱の注記を表示する形に簡素化。
- 探索ロジック/受理判定(isBetter/better)/重みは不変。仮説数(w)とその表示のみの変更。
- ユニットテスト3件追加（`V6NativeOptimizerChoiceTest`）: workers=8/16でworkersと一致すること・
  workers=0/1で下限2にフロアされること・旧上限5以下の帯で従来どおりworkersと一致すること（回帰確認）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 外部の並行/並列処理レビュー(9件)を実装（3.224.0）
ユーザーへ提示された並行/並列処理レビューを1件ずつ実コード照合（約90〜95%正当と評価）した後、
「実装する」の指示で対応。receiving-code-review規律に従い、実装前に**#6の一部を再検証して認識修正**
（下記）。全て**探索ロジック/受理判定/重みは不変**（並列制御・キャンセル・診断表示のみ）。
- **[Critical/共有ネイティブハンドル] `SaOptimizer.run()`**: `jobs.awaitAll()`が一部ワーカーの完了を
  待たずに例外/キャンセルを再送出しうる懸念に対し、`finally`で`NonCancellable`下に全ジョブを
  明示`cancel()+join()`してから`nativeDestroyProblem`する（C++側は参照カウント/mutex無しの単純
  `delete`のため、Kotlin側の順序保証だけが頼り。この明示joinで`awaitAll()`の内部タイミングに依存
  しない安全性を担保）。
- **[High/進捗コールバック直列化] `V6NativeOptimizer.optimize()`**: 最上位1箇所で`onProgress`を
  `synchronized`ラップ（`onProgressRaw`引数を内部で包む）。仮説(runMultiWorker)×チェーン
  (runAlnsChains)の多層fan-outを経ても、ユーザーコールバック（`OptimizationWorker`のスナップショット
  書込み等、ロック無しの共有ローカル変数を触る）へは必ず直列で届く。
- **[High/liveBest非単調] `publishLiveBest(report, schedule)`新設**: `liveBestReport`
  (`AtomicReference<ViolationReport?>`)でCAS管理し、`better()`で真に改善するときだけ
  `liveBest`（kill復旧用スナップショットの元）を更新。旧last-writer-winsだと劣った仮説が後から
  書き込むと途中結果の品質が退行し得た。3箇所の直接代入(`runAlns`のnative/Kotlin両経路・`runRsi`)を
  差し替え。
- **[High/例外道連れ] `runMultiWorker`**: `coroutineScope`→`supervisorScope`＋仮説ごとの
  `try/catch(Exception)`（`runAlnsChains`と同型のパターンへ統一）。1仮説の通常例外が他の健全な
  仮説結果を道連れにしなくなる。全滅時は既存どおり`run(0,...)`への直接フォールバックを維持
  （runAlnsChainsの「例外を再送出」より寛容＝最上位入口としてはクラッシュを避ける既存方針を維持）。
- **[Medium/早期winner競合] `runMultiWorker`/`runAlnsChains`**: 全ジョブを`CoroutineStart.LAZY`で
  生成してから一斉`start()`。旧実装は逐次代入+即時startのため、速い勝者の`cancel()`ループがまだ
  生成されていないジョブ(null)を素通りし、後から作られる新規ジョブがキャンセルを免れて走り得た。
  LAZYなら生成時点で`cancel()`が正しく効く（開始前キャンセル）。念のため各ジョブ本体先頭にも
  勝者確定済みチェックを追加。
- **[Medium/コア数クランプ] `clampWorkersToCores`新設＋V5専用配線**: V5(高速計算)は
  `hypothesisChainPlan`を使わず`options.workers`をSAチェーン数へ直接渡しておりコア数クランプの
  恩恵が無かった（例: 8コア機にworkers=16設定でV5選択→16並列SAチェーンが8コアを奪い合い希釈）。
  V5には「最低1仮説」のような競合する下限が無いため単純にコア数でクランプする専用関数を追加し配線。
  **[レビューの一部を実装前に再検証・認識修正]**: `hypothesisChainPlan`自体の
  `max(hypotheses, min(workers,cores))`は、cores<hypothesesのとき常にdistributableが
  hypothesesまで持ち上がる（例: 2コアで5仮説→5に固定）ため、レビューはこれも「クランプ不完全」と
  指摘していたが、実装時に再検討した結果**これはhypothesisChainPlanの欠陥ではなく、w(仮説数)を
  「仕様上不変」で減らさない設計に由来する構造的下限**と判断（各仮説は最低1チェーン必要なので
  h未満にはできない。この局面でもチェーンの"追加"配分はされず各仮説ちょうど1本のまま＝オーバー
  サブスクライブを増やしてはいない）。仮説数自体を減らす変更はより大きな設計判断（HF77非該当だが
  品質/多様性のトレードオフを伴う）のため今回のスコープ外とし、この認識を回帰テストで固定した。
- **[Medium/NativeGate非伝播] `SaOptimizer`の`runWorkerNative`/`runLahcNative`**: `timeUp()`に
  `!NativeGate.enabled`を追加。兄弟ワーカーが番兵発火でゲートを閉じたら、次チャンク前に自ワーカーも
  停止する（各チャンクは既に個別に自己整合/パリティ照合済みのため、ここまでのflush済み進捗はそのまま
  採用＝退化ではなく単なる早期終了）。
- **[Low/表示本数] `runMultiWorker`**: `completed.incrementAndGet()`を`finally`へ移動。例外/
  キャンセルでもカウントされ、「仮説N本探索中」表示が実態と乖離しなくなる。
- **[Low/停止後の再生成] `OptimizationWorker`**: `catch(CancellationException)`ブロックで
  `clearFiles(ctx)`を再実行してから再送出。UIの`stop()`は`cancelUniqueWork()`の完了を待たず
  即座にファイル削除するため、その直後にWorkerの進捗コールバックがまだキャンセルに気づかず
  スナップショットを再生成しうるが、Worker自身が最終的にキャンセルを検知した時点で必ず片付ける
  ため、次回起動時に明示停止済みの古い盤面を復旧候補として読む事故を防ぐ（`cancelUniqueWork()`の
  `Operation`完了を待つ・run ID照合、というより重い対策は今回のスコープ外＝Low相当の効果に対して
  過剰と判断）。
- ユニットテスト: `V6NativeOptimizerChoiceTest`に5件追加
  （`chainPlanFloorsAtHypothesesCountEvenBelowCoreCount`＝上記認識修正の固定・
  `clampWorkersToCoresLimitsToAvailableCores`/`NeverReturnsLessThanOne`・
  `publishLiveBestIgnoresWorseReportAfterBestPublished`＝CAS単調性の確認）。
  実際の並行実行/レース自体はJVMユニットテストの対象外（既存方針どおりCI/実機ログで確認）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## AptPolish新設＝適切回数(apt)専用の研磨パス（3.223.0）
ユーザー指示「専用の研磨パスAptPolish的なものを賢く深く網羅的に作る」。大島愛の休(aptHigh)/Pｼ(aptLow)
実例の追跡から、apt(重み1)専用の研磨が無く探索中のfocus軽視(3.169.0)で放置されがちと判明したのを受け、
grillingで3問確定: ①**自己振替を最優先**（同一職員が同時にaptHigh/aptLowを持つ場合、他者に一切
影響しない直接振替が最安全） ②単一方向の残りは**同一グループ内の相互交換**（同日の2人の割当を
まるごと入替＝被覆総量保存で構造的に安全、BlockSwapPolishと同型の安全性） ③それでも解消しない
残りはRangePolish型の**玉突きチェーン**（findCovUChain、候補が自身の新規apt違反を招くなら後回しに
するavoid述語つき）。3手を①→②→③の順で試す。
- **`applyAptPolish`新設**（V6HotfixPasses.kt）: アンカーは`report.countViolations`の
  "vio-aptHigh"/"vio-aptLow"（markCountの重み優先解決済）。手①は同一職員内でfromK(過多)→toK
  (過少)の1日を、被覆(covUCell)を悪化させない日に限定して直接付け替え（チェーンを使わない真に
  無償の手のみ対象）。手②は`p.sgrp`で同一グループの相手を探し、同日の2人の割当をまるごと入替える
  （相手のcanDoは同一グループのため保証済み、makesForbiddenRunのみ事前ガード）。手③は既存の
  findCovUChainをそのまま利用（rangeAvoidと同型の`worsensOwnApt`述語で、候補がちょうど目標値の
  シフトへ+1され新規aptHigh化する場合のみ後回しに）。採否は全手ともisBetter(hard→total→weighted)
  keep-best＝退化不能。
- **配線**: `runPostOptimization`のフィックスポイント巡回にBlockSwapPolishの直後として追加
  （`totalApt`カウンタ、SoftPolishVerifyのtargets/採用内訳にapt列を追加、`roundSeed`でラウンドごとに
  異なるseedを配線=3.221.0と同じ多様化）。
- ユニットテスト`AptPolishTest`5件: ①同一職員内でaptHigh/aptLowを自己振替で解消 ②自身に逆方向
  シフトが無い場合に同一グループの相手と相互交換で解消（Xの総日数=被覆総量保存も確認） ③自己/相互とも
  相手が構造的に存在しない単一方向のaptHighを玉突きチェーンで解消 ④apt未設定なら即no-op。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## RangePolishの頭打ち理由をログ可視化＋制約編集(cons3mn等)の削除が反映されない実機バグ修正（3.222.0）
ユーザー指示「希望固定/禁止連続/rangeAvoidの後回しが絡んでいる可能性がわかるログにする」＋実機報告
「回避の並びなどが削除出来ない」の2件を対応。
- **[ログ可視化] RangePolishの`tryRelocate`に頭打ち理由の集計を追加**: `blockStats: Map<(staff,shift),
  Map<理由,件数>>`を新設し、付け替え不成立の原因を5分類で記録（希望固定=movable即除外・禁止連続=
  makesForbiddenRun即除外・候補なし=findCovUChainがnull・range後回し=findCovUChainは成立したが使った
  候補がrangeAvoid該当(自身の新規high違反を招く)だった・不採用=chainは成立したがisBetterに拒否）。
  「range後回し」の判定は、返ってきたchainの各手に対し`exceedsOwnRangeHi`（rangeAvoidに渡したのと
  同じ述語）を事後適用するだけ＝findCovUChainのシグネチャ変更不要。研磨後もなお残っている対象の
  ログ（「残存:」）に、最多理由を`名前 記号(理由×件数)`の形で付記（例「上條洋平 Dﾃ(候補なし×6)」）。
  探索ロジック自体は不変・診断表示の追加のみ。
- **[実機バグ修正] `mutateConstraints`（cons1〜cons42s全族のadd/remove/update共通経路）が
  `editRev`を増やしていなかった**: 3.185.0/3.189.0で判明した「`_ui.update{copy(xxxEdited=true)}`は
  既にtrueだと同値でStateFlowがemitせず、`key(ui.editRev)`で包んだカードが再構成されない」バグと
  同型。3.190.0で`ConstraintsCard`/`SkillConstraintsCard`を`key(ui.editRev)`で包む対策はしたが、
  肝心の`mutateConstraints`側がeditRevを増やしていなかったため対策が空振りしていた
  （cons3mn=「回避の並び」に限らずcons1〜cons42s全ての制約の追加・変更・削除がこの経路を通るため、
  制約編集全体に影響していた可能性がある）。`_ui.update{it.copy(constraintsEdited=true,
  editRev=it.editRev+1)}`へ修正（3.185.0のapplyStructureと同一パターン）。
- ユニットテスト`RangePolishTest.rangePolishLogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked`
  （唯一の玉突き候補が希望固定で使えない盤面→ログの残存表示に「候補なし」が出ることを固定）。
  `mutateConstraints`側は表示専用の再構成タイミング修正のためUIテスト対象外（Kotlinコンパイル不可の
  サンドボックスにつき、実機での即時反映確認はユーザーに依頼）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## ラウンド跨ぎで同一seed固定だった頭打ちを解消＝roundSeed新設（3.221.0, 「なぜゼロにならないのか」）
ユーザーの繰り返し質問「桒澤美幸のAｱ超過はなぜゼロにならないのか」を受けて特定した、rangeAvoid
（3.218.0）とは別のもう一つの根本原因。`runPostOptimization`のフィックスポイント巡回
（最大maxRounds=4）はC1Polish/C3mnPolish/RangePolish/C3RunPolishを**毎ラウンド再呼出**するが、
呼出側がseed引数を渡さず各関数の**既定値（0x1C1L/0xC3AL/0x8A9EL/0xC3A2L）に固定**されたままだった。
- **発見**: `findCovUChain`の候補順はrng由来（rngはこれらのPolish関数呼出のたびに`Random(seed)`で
  フレッシュに再生成）。ある(staff,shift)ペアがラウンドNで頭打ち（候補が構造的に全滅／isBetterに
  拒否）すると、盤面の当該箇所（他パスがそこを変更しない限り）が変化しないため、ラウンドN+1以降も
  **全く同じrng列＝同じ試行順＝同じ結果**を再生するだけ——再挑戦の名目だけで実質的には一度も新しい
  候補順を試していなかった。美幸のAｱ超過が6→5→4と段階的にしか縮まらず0に到達しない実例と整合する
  （rangeAvoidが候補の優先順位は直しても、その優先順位自体がラウンドを跨いで固定なら効果が頭打ちする）。
- **修正**: `roundSeed(base, tag, round) = base xor tag xor (round * 黄金比定数)`を新設（internal、
  テスト可能に）。4箇所の呼出（C1Polish/C3mnPolish/RangePolish/C3RunPolish）に
  `seed = roundSeed(seed, <各関数固有の既定値>, round)`を配線（`seed`は`runPostOptimization`の
  外側引数、`round`はフィックスポイント巡回のカウンタ）。ラウンドごとに異なる候補順を試せるようになり、
  1回目で頭打ちしても次のラウンドで別の組合せを発見できる可能性が生まれる。**採否は既存のisBetter
  keep-bestのまま不変＝退化不能**（探索の多様化のみ、正しさ・重みは無関係）。
  CyclicSwapPolish/C3SequencePolish/BlockRotationPolishはrng/seedを持たない決定的走査のため対象外。
- ユニットテスト`RoundSeedTest`3件: ①4ラウンド分のseedがすべて異なること ②同一引数なら同一値
  (再現性) ③C1/C3mn/Range/C3Runの4つのtag定数が同一round内で互いに衝突しないこと。
- **正直な限界**: この修正は「再挑戦のたびに違う候補順を試せるようにする」だけで、**構造的に本当に
  解が存在しない頭打ち**（候補が本当に1つも無い、あるいはどの組合せもisBetterに負ける）は解消しない。
  美幸のAｱ超過が完全に0になるかはデータ依存で、今回の修正はあくまで「探索不足による頭打ち」の解消。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 15日ブロック丸ごと2人交換研磨=BlockSwapPolish新設（3.220.0）
ユーザー指示「15日間まるごと2人交換を実装する」。前段（金沢⇔アリフの月まるごと交換を検討）を受け、
grillingで5問確定: ①対象ペア=**同一担当グループ(canDo完全一致)のみ**(推奨採用) ②ブロック位置=
**全オフセットのスライド窓**(推奨採用、固定2本ではなく T-14通り全て試す) ③実行場所=
**後処理Polishパス**(推奨採用、RSI/ALNSの探索オペレータ化はデルタ評価でなくフルcheckのため不採用)
④探索範囲=**アンカーなし・同グループ内全ペア×全オフセットを無条件に試す**(ユーザーが非推奨側を選択。
low/high違反アンカーに限定せず、pref/apt/weekly等の組合せ差異も拾う)。
- **動機**: 既存の交換系(CyclicSwap=同日1〜3人・鏡像長方形=2日)は局所的なため、「1日ずつ動かすと
  途中経過が悪化してisBetterに拒否される」が「まとめて動かせば全体は改善する」ような大きな交換を
  発見できない。ただし金沢⇔アリフの実例は**staffRangeが完全同一のため無意味**（超過が移るだけ）と
  判明済み＝本パスはrange/wish/apt等が異なる同グループのペアに対して価値を持つ汎用オペレータとして実装。
- **`applyBlockSwapPolish`新設**（V6HotfixPasses.kt）: `sgrp`(担当グループ)ごとに職員をまとめ、
  グループ内の全ペア×全オフセット(0..T-blockLen)を走査。ブロック内に希望固定(wish-lock)がある
  ペア/オフセットは事前スキップ（他パスの`movable`規約と同じ安全側フィルタ、無条件に希望を破壊する
  試行を避ける）。同一グループ限定のため**canDo/groupViol/covU/covO/c41(s)/c42(s)/禁止連続の内部は
  構造的に不変**（同じシフト列が丸ごと相手に移るだけ）、ブロック境界でのみ新規禁止連続が起こり得るが
  isBetterのhard判定が担保。採否はisBetter(hard→total→weighted)keep-best＝退化不能。
- **配線**: `runPostOptimization`のフィックスポイント巡回にC3RunPolishの直後として追加
  （`totalBlockSwap`カウンタ、SoftPolishVerifyの採用内訳に「ブロック交換:N」を追加）。
- ユニットテスト`BlockSwapPolishTest`3件: ①同一グループ2名(T=15=既定blockLen、片方が超過・片方は
  無制限)で丸ごと交換によりhigh=0まで解消し盤面が正しく入れ替わること ②別グループの2名(ペアなし)で
  即no-opであること ③ブロック内に希望固定があると交換自体が試行されず盤面・違反とも不変であること。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 研磨ログに対象/残存職員名を追加（3.219.0）
ユーザー指示「ログから美幸が分かるようにする」。RangePolish/C3mnPolish/C3RunPolishのログは
集計件数（例「high 8->7 採用2回」）のみで、**具体的に誰が対象/未解消か**が分からず、実機ログから
特定職員（桒澤美幸・金沢勇輝等）の状況を追うには別途「違反詳細」診断行との突合が必要だった。
- **RangePolish**: 成功した(staff,shift)を`対象: 名前 記号, ...`として、研磨後もなお違反している
  (staff,shift)を`countViolations`(3.210.0で重み優先解決済)から`残存: 名前 記号, ...`として追記。
- **C3mnPolish/C3RunPolish**: `cellFamilies`(1セルの全違反クラスを保持する既存マップ)から該当族
  ("vio-c3mn"/"vio-c3"・"vio-c3m")を含むセルの職員名を重複除去して`残存: 名前, ...`として追記
  （共通ヘルパー`stuckStaffNames`を新設、3パスで再利用）。
- 表示専用のログ追加のみ・スコアリング/探索ロジック不変。HF77非該当。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## findCovUChain に rangeAvoid（新規range違反の後回し）を追加＝頭打ちの根本原因を修正（3.218.0）
ユーザー指示「頭打ちしたらさらに玉突き連鎖を検証する」を受け、桒澤美幸のAｱ超過(RangePolish=3.215.0)が
研磨後も残る実例（実機ログ+近傍state.json）を追跡し根本原因を特定・修正。
- **発見**: `findCovUChain`の`candidates()`（V6SearchOperators.kt）は canDo/希望ロック/禁止連続の
  **構造的妥当性のみ**で候補を集め、rng順（Fisher-Yates shuffle）の**先頭1件が完成すればそれで確定**
  （コスト比較なし）。実データ検証: Aｱは全31日需要ちょうど1人・美幸の6日は禁止連続と無関係で構造的には
  毎日解消可能（休で待機中のAｱ担当可能者が複数名存在）。しかし候補の中に「Aｱ担当可だが自身の
  staffRange上限(hi)ぎりぎり」の職員がいると、rngがその候補を先に引いた場合: 美幸のexcessが-1される一方
  でその候補に+1の新規high違反が生まれ、high族の合計は**差し引きゼロ**→isBetterが「改善なし」として却下。
  RangePolish/C3mnPolish/C3RunPolishはいずれも1つの(i,j)ペアにつき findCovUChain を**1回しか呼ばない**
  ため、rngが「悪い」候補を引いた日は以後二度と試行されず恒久的に頭打ちになる（seedは各パス呼出内で固定
  のため、ラウンドを重ねても同じ候補が引かれ続け自己修復しない）。2.57.0「destroy-repairはmarginal soft
  cost最小の候補を選ぶべき」と同型の穴が、後発の`findCovUChain`（E11, covU玉突き専用に設計）には
  無いまま残っていた。
- **修正**: `findCovUChain`に`rangeAvoid: ((staff,fillShift)->Boolean)? = null`を追加（`c1Pref`と同型の
  非破壊オプション引数、既定null=既存呼出元は完全に挙動不変）。`candidates()`内で`rangeAvoid`が真の候補を
  **除外ではなく後回し**にするだけ（他に候補が無ければ従来どおり使う＝解が消えない）。RangePolish/
  C3mnPolish/C3RunPolishの3箇所（ユーザー承認範囲）の`findCovUChain`呼出に、共通ヘルパー
  `exceedsOwnRangeHi(p, work, staff, fillShift)`（候補がfillShiftを1つ得ると自身の`p.rangeHi`を新たに
  超えるか）を`rangeAvoid`として配線。RSI探索本体のcovU連鎖(`applyCovUChains`等)や`applyC1WindowPolish`
  （既に`c1Pref`という別の優先軸を持つ）は対象外（今回診断した3パス固有の穴のみに限定・スコープ最小化）。
- 検証: ユニットテスト`chainFillRangeAvoidAlwaysPrefersCandidateWithoutOwnRangeViolation`
  （ChainFillTest.kt）。休(0)/P(1)の2シフト・bad(自身のstaffRange hi=1に既に到達寸前)/good(無制限)の
  2候補が同格に完成する盤面で、seed 0..15 全てにおいて`rangeAvoid`指定時は必ずgoodが選ばれること、
  かつ`rangeAvoid`無しではrng順次第でbadが選ばれ得ること（旧実装の脆さの実証）を固定。
  サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 「他の制約は大丈夫か」監査→c3/c3m・c41/c41sへも玉突き連鎖を横展開（3.216.0）
ユーザー質問「他の制約は大丈夫ですか?」を受け、C3mnPolish/RangePolishと同型の「交換相手が構造的に
存在しないと諦める」穴が他族にもないか監査。**確認できた2件を修正**（ユーザー「次」で実装承認、
grillingは同型パターンのため省略）。**大丈夫と確認できたもの**: pref(希望,HARD)は`hf67HardRepair`が
希望を直接強制適用→全職員探索のgreedy再充填(`bestStaffForCoverage`)で埋め直す方式のため特定の交換
相手を要求せず対象外。c2/c42/c42sは同日1手で閉じるため既存CyclicSwapで十分(2.49.0で確認済み)。
- **`applyC3RunPolish`新設**（V6HotfixPasses.kt）: cons3/cons3mのうち`C3Run.isSingleShiftSeq`が真の
  規則（run-deficitモデル、HF507）専用。既存の`C3Polish`(2者ブロック交換)/`C3Rotate`(3者回転)は
  「相手が現在の自分のシフトを担当可能」という相互条件が必須でchainフォールバックが無く、単一シフト連の
  run不足（隣接日へ伸ばせば直る局面）が交換相手不在で解消できないままだった。アンカーは
  `cellFamilies`の"vio-c3"/"vio-c3m"（run先頭セルにマーク済）から実際のrun境界を再走査し、
  隣接日(run直前/直後)を該当シフトへ拡張。拡張元の被覆(covUCell)が悪化する場合は`findCovUChain`
  で玉突き修復。スコープ限定: 複数シフトのMUST/Wantパターン(非single-shift)は既存機構のまま対象外
  （安全側・挙動不変）。`runPostOptimization`のフィックスポイント巡回にRangePolishの直後として配線。
- **`applyC41Free`にchainフォールバック追加**（V6NativeOptimizer.kt, 3.209.0の直接移動のみだった実装を拡張）:
  離脱側のcovU悪化のみ`findCovUChain`で埋め直す（到着側のcovOは引き続き直接ガード=候補を変えて試す、
  covO向けの玉突きはfindCovUChainの対象外のため据え置き）。本関数は呼び出し元(`rsiGenerateHypothesis`)が
  ラウンド単位の`better()`でkeep-best評価する仮説生成器のため内部にisBetterは持たない（従来と同じ契約）。
- ユニットテスト: `C3RunPolishTest`（cons3 "X,X"の2連続を、需要のない別シフト在勤者への玉突きで
  解消する最小盤面。①c3=0まで解消 ②HARD/covU悪化なし ③cons3空ならno-op）。
  `V6NativeOptimizerChoiceTest`に2件追加: `applyC41FreeResolvesExcessViaChainWhenDirectMoveWouldCreateCovU`
  （群超過1・離脱元need1単独充足で直接移動不可→玉突きで解消）、
  `applyC41FreeResolvesDeficiencyViaChainWhenDirectMoveWouldCreateCovU`（群不足1・offShift候補2名とも
  離脱元need1単独充足で直接移動不可→玉突きで解消）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 玉突き連鎖(findCovUChain)をlow/high(個人回数)研磨へ横展開＝RangePolish新設（3.215.0）
ユーザー質問「美幸はなぜＡアの回数違反ですか?研磨しないのか?」に対し、実データ(state.json)で桒澤美幸を
数値検証: 担当可能シフトは休/Aｱ/B1の3つのみ(groupShift[7]=[1,0,0,0,1,0,0,0,0,0,1])。休=10/10固定・
Aｱ上限2に対し実際は休=12・Aｱ=6(超過4)・B1=13(上限23、余裕あり)——「休/Aｱの一部をB1へ回す」で
数学的には全部満たせる。しかし彼女はB1担当が全職員中ただ一人のため、既存のCyclicSwap/HF67
（同日に相手シフトを持つ交換相手が前提）では交換相手が構造的に存在せず、C3mnPolish(3.214.0)の
金沢勇輝ケースと全く同型の穴と判明。ユーザーが直接「実装する」と承認（C3mnPolishと同型のため
grillingは省略）。
- **`applyRangePolish`新設**（V6HotfixPasses.kt）: `report.countViolations`（"i,k"→"vio-low"/"vio-high"、
  3.210.0で重み優先解決済）からlow/high違反(staff,shift)ペアを列挙。HIGH(超過)はそのシフトの保有日を
  他の担当可能シフトへ、LOW(不足)は保有していない日のうち1日をそのシフトへ、それぞれ一方的に付け替える
  （`tryRelocate`共通ヘルパ）。付け替えで元シフトの被覆(covUCell)が悪化する場合は`findCovUChain`
  （C1Polish=3.158.0/C3mnPolish=3.214.0と同一パターン）で玉突き修復。採否はisBetter(hard→total→
  weighted)keep-best＝退化不能。
- **配線**: `runPostOptimization`のフィックスポイント巡回にC3mnPolishの直後として追加。
  SoftPolishVerifyログの対象・採用内訳へlow/high・「range玉突き」を追加。
- ユニットテスト`RangePolishTest`: 職員A(shift X超過, hi=1に対し実際2)を、需要のない別シフトに
  在勤中の職員B(いつでも動かせる)への玉突きで解消する最小盤面を構築し、①high=0まで解消
  ②HARD/covUとも悪化しない ③実際に手が採用されることを固定。staffRange空なら即no-opであることも固定。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 玉突き連鎖(findCovUChain)をc3mn(回避,SOFT)研磨へ横展開＝C3mnPolish新設（3.214.0）
ユーザー指摘「金沢勇輝の4連続Dﾃ(c3mn回避パターン全4セルにヒット)なので違反です」→「玉突き連鎖を見直す」を
受けgrilling(2026-07-19)で3問確定: ①C3mnPolish新設(C1Polish=3.158.0と同型の横展開、既存findCovUChain
自体の見直しではない) ②対象はc3mn専用(c3nはHARDで既存のRSI focus優先/keep-bestが担当済み・混ぜると
役割重複) ③findCovUChainをフル活用(自己swapに限定しない・多人数玉突き=深さ5まで)。完了条件はユニット
テストのみ(実データ再検証はしない)。
- **背景**: 金沢勇輝はcons3n(HARD)でDﾃ直後にA4/Aｱ/Cｵ/Cｱ/B4が禁止され、休が10/10固定のため休を
  1日挟んで4連続Dﾃを崩すとhigh違反(weight90)の方がc3mn回避(weight12)より高くつく。唯一の解=
  「休を増やさず自身のDﾃを他日の休と入替える」に類する手だが、既存C3Polish/C3Rotateはブロック交換/
  3者回転のみでこの種の玉突きを持たない。
- **`applyC3mnPolish`新設**（V6HotfixPasses.kt）: cons3mn専用の研磨パス。アンカーは`report.cellFamilies`
  から"vio-c3mn"を含むセル(3.111.0系のシャドーイング対策と同じ理由=violations単一クラスマップだと
  より重い違反が同居するセルで見落としうる)。各アンカー(i,j)について i の担当可能シフトへ付け替え、
  cons3n新規発生は`makesForbiddenRun`で事前枝刈り。付け替えで元シフトの被覆(covUCell)が悪化する場合は
  `findCovUChain`（3.155.0のE11多人数玉突き連鎖BFS、C1Polish=3.158.0の「手B/E11」ブロックと同一パターン）
  で玉突き修復を試みる。採否は既存の`isBetter`(hard→total→weighted)keep-best＝退化不能。
- **配線**: `runPostOptimization`のフィックスポイント巡回(C1Polish/C1Rotate/C3Polish/C3Rotateの直後)に
  追加。SoftPolishVerifyログの採用内訳へ「c3mn玉突き」を追加。
- ユニットテスト`C3mnPolishTest`: 職員A(cons3mn "X,X"回避に2日連続でヒット)を、需要のない別シフトに
  在勤中の職員B(いつでも動かせる)への玉突きで解消する最小盤面を構築し、①c3mn=0まで解消 ②HARD/covU
  とも悪化しない ③実際に手が採用されることを固定。cons3mn空なら即no-opであることも固定。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 3.213.0の自己監査で発見した見落とし＝hard許容ゲートの旧スケール残置を修正（3.213.1）
3.213.0でパック桁単位(SCORE_HARD_UNIT)を1e6→1e9へ拡大した際、**「hard+2超は却下」の早期ゲート4箇所を
見落として旧スケール(2_000_000)のまま放置**していたことを、無関係の別タスク(玉突き連鎖のgrilling準備で
`findCovUChain`周辺コードを読んでいた際)の自己監査で発見。
- **対象**: `V6SearchOperators.acceptWorseScore`/`glsAccept`（Kotlin）、`glsAcceptN`/`polishAcceptN`
  （C++ magi_native.cpp、Kotlin側と対で保守する関数）の計4箇所。全て `if (delta > 2_000_000L) return false`
  という早期ゲートを持っていたが、SCORE_HARD_UNIT拡大時に更新対象から漏れていた。
- **実害の評価**: 幸い**ほぼ無害**と判明。これらの関数は「delta(候補との生スコア差)が大きすぎる手を弾く」
  ゲートの直後に Boltzmann 受理判定 `exp(-delta/(200*temp))` を置いており、hard が1つでも変化すると
  delta は必ず SCORE_HARD_UNIT(=1e9) 以上の桁になる。通常運用の temp(0.03〜数程度)では
  `exp(-1e9/200/temp)` は事実上ゼロ＝ゲートを素通りしても直後の確率判定でほぼ確実に却下される
  （**旧M=1e6の時代から一貫してこの性質**＝hard変化を伴う手はそもそもこの経路では実質的に一度も
  受理されてこなかった）。ゲートの閾値が2e6のままだったことで「gate即却下」と「Boltzmannでほぼ0%」が
  入れ替わっただけで、**観測可能な受理/却下の結果はほぼ変わらない**（違いが顕在化するのは
  temp が異常に大きい極端な状況のみで、実運用のtemp帯では到達しない）。
- **修正**: 4箇所とも `2_000_000L`/`2000000LL` → `2 * SCORE_HARD_UNIT`/`2000000000LL` へ同期。
  C++は host parity harness で再ビルド・再実行しmismatch=0(既存回帰なし)を確認（この変更はdeltaApply/
  スコア計算そのものには触れず受理判定のみのため、既存のパリティ検証はそもそも対象範囲外だが健全性
  確認として実施）。ユニットテスト`acceptWorseScoreGateThresholdMatchesNewScale`を追加:
  delta=1e8(新閾値2e9未満)は極端に大きいtempでゲート通過を外部から観測可能にして確認、
  delta=3e9(新閾値超)はtempに関わらずRNGに触れる前にゲート却下されることを確認。
  HF77非該当（重み/受理式は不変、スケール定数の同期のみ）。

## 外部レビュー再検証文書の8件を一括修正（3.213.0）
外部提示の再検証文書（11項目＋誤検知却下9件）を全項目コード実測で突合（正答率~80-85%と評価）した後、
ユーザー指示「修正する」→ AskUserQuestion で範囲確定「全部(8件)」。**keep-best/番兵の構造は全て不変**。
- **[#1] 辞書式パック桁単位 1e6→1e9 拡大**: `SCORE_HARD_UNIT = 1_000_000_000L`（Evaluator.kt 新設・共有定数）。
  soft>=1e6 で hard/soft 分解・SA HARDゲート・LAHC・GLS の比較が壊れる理論的破れ（実機 soft~2e3 だが未強制の
  不変条件だった）を恒久解消。Kotlin 5ファイル（Evaluator/DeltaEvaluator/SaOptimizer M/V6SearchOperators
  glsAccept/V6NativeOptimizer 10箇所）＋ **C++ magi_native.cpp 11箇所**（SaChunk::M を int→long long 1e9・
  fullEvalCombined・LAHC/ALNS/Polish の全 /1000000LL）を同期。ABI_VERSION 6→7（両側）。nanoTime→ms 変換の
  1_000_000L は対象外（grep で全数分別済み）。**検証: host parity harness 実ビルド・実行 = golden実データ
  359万手 mismatch=0**（bit-op ×2.12）。SA受理は hard デルタが旧来から exp≈0＝同一挙動、GD level は
  スコア比例導出＝比例スケールで動学不変。
- **[#2] 全ワーカー/全チェーンの改善を外側停滞時計へ集約**: runMultiWorker と runAlnsChains に共有best
  （AtomicReference＋better() CAS）を新設し、**改善時のみ** i!=0/c!=0 からも onProgress 転送（旧: i==0のみ＝
  W1..4 だけが改善する局面で V6FinalPort ウォッチドッグが観測できず HARD平坦時の stallHardMs で全停止し得た）。
  非改善まで転送しないのは phase 文字列の交互切替で外側のフェーズ遷移リセットを偽発火させないため。
- **[#5] HF63 を focus 投入量ベースへ**: `updateFromBreakdownFocused(breakdown, focusedKey, effortIters)` 新設
  （gFocusedStall 累積・改善/0到達は全族 self-correction・停滞加算は focus 族のみ）。RSI は lastFocus（直前
  ラウンドの focus）を渡す配線に変更。旧: covU 張り付き中に一度も試していない c3n 等 HARD 族まで約3Rで誤
  deprioritize（3.184.0 の avoid フィルタは SOFT 側のみの緩和だった）。旧 updateFromBreakdown は ViewModel
  警告用に温存。テスト2件（focus族のみ flag・改善リセット）。
- **[#9] V6FinalPort 停滞時計の分離**: lastImproveMs → lastBestImproveMs／lastPhaseChangeMs の2時計＋
  `max()` 判定（挙動同一・意味の明確化＋将来の別閾値拡張余地）。
- **[#4] LightMirrorOptimizer の希望凍結をエンジン本体と統一**: canDo 無視の `lockedMatrix` → `wishLocked`
  （実現可能希望のみ凍結）＋**凍結希望を盤面へ事前適用**。旧: 実現可能な未充足希望が永久に直らず・実現不能
  希望が修復を妨げた（UI 参照ゼロの温存APIだが MirrorEngineTest が使用）。lockedMatrix は呼出0となり削除。
  回帰テスト（初期盤面で未充足の実現可能希望3件が最適化後に充足）。
- **[#7] Evaluator.fullEval の -1 ガード**: `ssn[i][a[i][j]]++` → 範囲検証付き（normalizeSchedule の -1 で
  AIOOBE だった。C++ fullEvalParts=3.199.0 全面ガード済みとの対称化）。テスト（-1 盤面で非例外）。
- **[#6] applyWeeklyRebalancePolish 内側ループの締切確認**: j2/ip ループへ `done||shouldStop()` 追加
  （2.65.0/3.84.0 と同方針の対象漏れ）。
- **[#8] GlsPenalty.decay の値域契約**: `require(keepPercent in 0..100)`（100超=増幅の誤用防止・現行呼出は
  固定80のみ）。テスト（101/-1 で IAE）。
- **[#3] SCALE_TEMP コメント訂正（HF77）**: 実装は「盤面を best へ戻さず現在解を保持」のみで専用温度倍率は
  無い（全アクション共通で次ラダーが t0 再加熱）。enum 定義文・SaOptimizer 2箇所の「温度を上げて」表現を
  実態へ訂正（温度動学の実変更は 2.55/2.56 の A/B 実測原則によりコメント訂正のみ＝挙動不変）。
- 検証: Kotlin はブレース/丸括弧/角括弧均衡0＋CI（v6-engine-check/Release Build）。C++ は host parity
  harness 実行（mismatch=0）＋ native-parity CI。テスト計7件追加。パック拡大は同一ビルド内で Kotlin/C++ が
  常に同時に切替わるため実行時パリティも不変。

## 3.211.0の敵対的フルコードレビュー→主要修正の一括適用（3.212.0）
ユーザー指示「敵対的コードデビューをフルコードする」→ /code-review(high) を8視点並列finder
（正確性3=行毎/削除挙動/横断トレース＋再利用/簡素化/効率/高度/規約5）で実施し10件確定
（ReportFindings報告済）→「主要修正を全部入れる」で一括修正。
- **健全確認（レビューで無傷と確定）**: keep-best番兵は全階層維持＝採用結果の退化は構造的に不能・
  nativeハンドルリーク無し・seed衝突無し（内外で異なる乗数定数は衝突回避で正当）・キャンセル伝播正常・
  SaParams意味論不変。finder主張2件は自前照合で棄却（「旧実装なら1仮説損失のみ」→旧も非Cancellation
  例外は伝播＝誤り／「チェーンは一切suspendしない→完全飢餓」→native経路はチャンク毎yield()等で誤り）。
- **[HF77虚偽表示→余り配分で真実化] `hypothesisChainPlan(workers, hypotheses, cores)` 新設**:
  旧perW=床のみ配分はworkers6〜9（既定上限8＝動機の実機ログ当該ケース）で余剰1〜4本を黙って廃棄
  しながらUI/docs/コメントが「無駄にならない」と虚偽主張していた。余りを先頭仮説から+1ずつ配分
  （8/8コア→[2,2,2,1,1]）し主張どおり実際に使われるように。**＋コア数クランプ**: 配分総量を
  min(workers, cores)に制限（workers=16/8コアの15コルーチン希釈＝「浅い多チェーンkeep-best<深い
  単一チェーン」の品質逆行リスクと電池/熱を回避。2.55/2.56のA/B実測原則に反する無計測の
  オーバーサブスクリプションを構造的に防ぐ保守的選択）。
- **[構造分割] runAlns→3行ディスパッチャ＋runAlnsSingle**: workersフィールドの意味過重（設定値/
  チェーン数/チェーン内=1）と、無限再帰防止がoptions.copy(workers=1)1引数とコメントのみに依存する
  脆弱性を解消。チェーンはrunAlnsSingleを直接呼ぶ＝再帰は構造的に不可能。
- **[runAlnsChains堅牢化3点]** ①部分結果許容: チェーン毎に非Cancellation例外を捕捉（1チェーンの
  AIOOBE等で兄弟の有効なbestを道連れ破棄→全体abortだった。全滅時のみ最初の例外を再送出=旧failure面）
  ②§2.2絶対評価の復旧: 非先頭チェーンのHARD=0も検知して兄弟即キャンセル＋合格reportをc!=0でも外側
  onProgressへ転送（旧: c==0のみ転送で確率(perW-1)/perWで早期キャンセル喪失・runMultiWorkerの
  仮説間キャンセルにも不可視だった）③観測性: チェーン毎結果・相異なる解数・chain0内訳をログ化
  （全チェーン同一解収束＝並列の無駄、を次回実機ログで検出可能に。合計iterの2分母問題も内訳併記で解消）。
- **[MAX_HYPOTHESES定数化]**: マジック5がoptimize/V6FinalPort/UI閾値・文字列・引数/docsに4〜5箇所
  散在→共有定数へ。UI注記はhypothesisChainPlan（エンジンが実際に使うプラン）から表示を導出＝
  UI側並行再計算による乖離を構造的に防止。**注記の正直化**: 「高速」に加え「おまかせ＋予算≤30s→V5
  解決」でもworkersがそのままSAチェーン数になる旨を明記（旧注記はAUTO→V5ケースを誤説明）。
- **[ExtraRefineキャップ]**: 微小予算(5〜25s)の追加精製へ生workers設定が素通しされ5仮説×perWの
  fan-outでチェーン毎固定費（入口hf67+フルcheck×2+nativeハンドル生成）が予算を侵食→
  workers≤MAX_HYPOTHESESにキャップ（従来の5×1構成維持・3.102.0の予約枠回収を保全）。
- **[README最終更新]** 慢性ドリフト（2026-07-06/6769806のまま～140版）を現在へ更新。
- テスト3件追加（余り配分[2,2,2,1,1]・コア数クランプ・各仮説最低1本/縮退入力）。既存perHypothesisWorkers
  テストは均等床計算として維持。スコアリング不変（重み/受理/番兵は一切不変・並列構成と表示のみ）。
- サンドボックスはKotlinコンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定はCI。

## 余剰ワーカーの実質的活用＝RSI/RSI++のSAチェーン数拡張＋ALNS多チェーン新設（3.211.0）
ユーザー指示「新たに敵対検証する。ログとコードを敵対トレースする。」を受け、既存ログ（マージ済み修正
より前の`magi_log_1784359823894.txt`）を現行コードへ再照合する過程で発見。「賢く深く網羅的に改善
できるようにする」の指示を受け、grillingで範囲確定のうえ実装。
- **発見**: ログの`TIME:`行「workers設定8 実効仮説5」を`V6NativeOptimizer.kt`/`SaOptimizer.kt`へ
  照合。仕様書(`docs/v6_engine_native_port.md`)§2.2の「最大5仮説」上限自体は確定事項で変更不可だが、
  **同じ文書に明記された「workers設定N/実効仮説Mを分けて表示する」設計意図が設定画面には未実装**
  （ログにしか出ていない）。さらに調査を深めた結果、**5を超えた分のworkersが単に無駄という以上に、
  各仮説内部でも一律`workers=1`に強制されており、RSI/RSI++が内部で呼ぶ`runV5`(SAチェーン数拡張済みの
  既存実装)すら5を超える設定の恩恵を一切受けられていなかった**ことが判明。
- **修正1(表示徹底)**: `MagiSetupCards.kt`の「並列ワーカー」ステッパー直下に、`ui.workers>5`のとき
  実際の配分（仮説5×内部N並列）を示す注記を追加。`V6Dispatcher`診断ログ・`MultiWorker`ログも
  実効仮説数と仮説内並列数を明示するよう更新。
- **修正2(実質活用/RSI・RSI++)**: `runMultiWorker`に`perHypothesisWorkers(workers, hypotheses) =
  max(1, workers/hypotheses)`を新設し、各仮説へ渡す`options.workers`を旧`1`固定から`perW`へ変更。
  RSI/RSI++が内部で呼ぶ`runV5`→`SaOptimizer`は元々`workers`本のSAチェーンを並列実行できる実装
  だった（変更不要）ため、この1行の配分変更だけで既存の休眠機能が有効化される。
- **修正3(実質活用/ALNS新設)**: `runAlns`に`options.workers>1`のとき`runAlnsChains`（新設）へ
  委譲する分岐を追加。`runAlnsChains`は異なるシードで`runAlns`本体（無変更）を`workers`本並列実行し
  `better()`（hard→total→weighted辞書式）で最良を採用する薄い外側ラッパー（内側呼出は`workers=1`
  固定・再帰1段のみ＝無限増殖しない）。restarts・GLS・destroy-repair等の`runAlns`内部ロジックは
  一切変更なし。ALNS単独モードに加え、RSI(偶数ラウンド)・RSI++(Phase3 Refine)・PORTFOLIO(ALNS分担
  仮説)が全て同じ`runAlns`を呼ぶため、この1箇所の変更で一括して恩恵を受ける。
- **安全性**: `cachedProblem`のProblemキャッシュは既にドキュメントで「5ワーカー間の共有読取も安全」
  と確認済み（同じ読取専用アクセスパターンが仮説内でも増えるだけ）。`runAlns`内のnative handle
  生成/破棄は各呼出で自己完結（try/finally）。最終選択は全チェーン共通の`better()`でゲートするため
  退化不能。V5(高速)は元々仮説の概念を使わず`workers`をそのままSAチェーン数とするため対象外
  （既存動作は不変）。
- ユニットテスト`perHypothesisWorkersDistributesSurplusEvenly`/`perHypothesisWorkersNeverReturnsLessThanOne`
  （`V6NativeOptimizerChoiceTest.kt`）を追加し、配分計算（純粋関数）のみを固定。並列実行そのものの
  検証はJVMユニットテストの対象外（既存の`runAlns`/`runMultiWorker`同様、CIビルド＋実機ログで確認）。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。実機での並列度向上効果は次回ログで確認。

## countViolations(markCount)を重み優先へ統一（3.210.0, 「新領域も敵対検証する」の追加防御）
ユーザー指示「新領域も敵対検証する」を受け、`V6HotfixPasses.kt`のこれまで未監査だった研磨パス
（`applyHF67InterStaffSwap`/`applyHF66IntraStaffRedistribution`/`applyHF80StrategicOscillation`/
`applyCyclicSwapPolish`/`runPostOptimization`のフィックスポイント配線）と`V6SearchOperators.kt`の
`findCovUChain`/`findTargetedFix`系、`V6SwapSuggester.kt`(FixSuggester)の`.violations`/`.countViolations`
利用箇所を横断精読。**確認された実バグは0件**（全て単一クラスでの候補フィルタを行わないか、
`isBetter`によるフル評価で最終的に保護されている）。ただし1件、これまで3回発見した
「anchor-shadowing（`report.violations`が単一クラスのため重い違反に上書きされ検出漏れする）」と
同系統の**構造的な地雷**を`MirrorCore.markCount`（`countViolations`のバッキング関数）に発見。
- **発見**: `mark()`/`markNeed()`は同一セルに複数族が重なったとき`MirrorKeys.weights`を比較し
  常に最重の族のクラスを保持するが、`markCount()`だけは`countViolations["$i,$k"] = ...`という
  **無条件の後勝ち(last-write-wins)**だった。低い重みの族が後から呼ばれると、既に記録済みの
  重い族のマークを黙って消してしまう構造。
- **現状のリスク評価**: 実際には**現在のバグではない**。`check()`内の呼出順が固定で
  c2(重み1)→low(重み90)→high(重み45)→apt(重み1,手動`containsKey`ガードで上書き禁止)であり、
  (a) low/highは`lo<=hi`の通常データ下では同一セルで両立しない（相互排他）、(b) c2は必ず
  low/highより先に処理される、(c) aptは手動ガードで既存マークを一切上書きしない、という3つの
  偶然の噛み合わせで常に正しい結果になっていた。だが将来`markCount`へ新しい族を追加する際や
  呼出順を並べ替えた際に、無条件上書きが原因で重い違反の表示が消える再発リスクを持つ
  「地雷」であり、`FixSuggester`（`countViolations`の`"vio-low"`一致で下限割れ職員を集める
  Phase 3）のような読者はこの上書きの影響を直接受け得る。
- **修正**: `markCount`を`markNeed`と同じ重み優先パターンへ書き換え（既存マークの重みが新規マーク
  以上なら上書きしない）。apt呼出側の手動`containsKey`ガードは`markCount`自身の重み優先ロジックに
  吸収されたため撤去（"aptLow"/"aptHigh"は`MirrorKeys.weights`に個別キーが無く0.0扱い＝他の実在
  する全族に対し常に劣後し、旧ガードと完全に同じ挙動を再現）。表示専用の関数のみの変更・
  スコアリング/重み/探索は一切不変。
- ユニットテスト`countViolationsPrefersHeavierFamilyOverLighterAtSameCell`（MirrorEngineTest.kt）を
  追加: cons2(count>=3)とstaffRange低(lo=3)が同一(staff,shift)セルで同時に発火する最小盤面を構築し、
  `countViolations`が常に重い方(`vio-low`, 重み90)を示すことを固定（呼出順に依存しない不変条件の
  回帰ガード）。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## 停滞脱出アルゴリズムのゼロベース敵対検証で発見したc41/c41sの専用repair欠如を修正（3.209.0）
ユーザー指示「停滞脱出アルゴリズムをゼロベースで敵対検証する」を受け、RSI focus選択(`maxViolatedFamily`)・
HF63・N4早期終了・E9冷却・GLSキック・ALNS restartを通しでコードトレース。covO/aptの周期枠修正
（3.207.0/3.208.0）自体に新たな不具合は見つからなかったが、**covOと全く同じ欠陥を`c41`/`c41s`が
抱えている**ことを新たに発見・修正した。
- **発見**: `c41`/`c41s`（群×日×シフトの人数レンジ[l,u]違反）は `MirrorCore.kt` で
  `markNeed(shiftIdx, day, "c41")` により **needViolations にしか載らず、report.violations
  （職員×日マップ）には一切現れない**（c42/c42sは対照的に`mark(i,j,"c42")`でviolationsに載る）。
  このため covO と同様、①GLSキック（ALNS内、`curReport.violations.keys`基準）②`destroyRepairViolations`
  （RSIの`else`分岐、同じくviolations基準）の**両方が c41/c41s のヒントを一切持てない**。さらに
  RSIでc41/c41sがfocusされた場合の配線 `"covU","c41","c41s" -> applyCovUChains + destroyRepairDay`
  は covU（シフト単位の人員不足）専用の修復で、`destroyRepairDayAt`のc41-marginal-cost考慮
  （2.59.0のc41DayMarg）は既存のcovU充填の**副次効果**でしか働かず、群レンジの上限超過や、
  シフト自体に不足がない日の下限割れには直接には効かない。
- **現状のリスク評価**: 提供済み全7実機ログでc41=0・c41s=0（breakdownに未出現）のため、**現時点で
  ユーザーへの実害は確認できていない**。ただし群レンジ制約(cons41/cons41s)が実際に効くデータでは
  covOと同じ「focusされても直せない」症状が起きる潜在バグ。ゼロベース検証で見つけた構造的欠陥として
  修正した（実データでの発火は未確認のまま、原理的な対称性の欠如を解消）。
- **修正**: `applyC41Free(state, sched, rng, skill: Boolean)` を新設。covOのapplyCovOFreeと対称の
  「動かせるか」判定を群レンジの**両方向**（超過=群在籍者を他シフトへ／不足=群内の他シフト在籍者を
  引き入れる）に適用。希望固定でない・禁止連続(c3n)を作らない・移動元/移動先でcovU/covOを悪化させない
  候補のみ動かす。`skill=false`は`cons41`(`sgrp`)、`skill=true`は`cons41s`(`ssk`)を対象にしDRY化。
  `rsiGenerateHypothesis`の`"covU","c41","c41s"`を`"covU"`(既存のapplyCovUChains維持)・`"c41"`・
  `"c41s"`(各々applyC41Free)へ分割配線。
- **安全性**: 探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は既存のラウンド`better()`
  keep-best（hard→total→weighted辞書式）が担保するため退化不能。covU/covOの悪化を事前ガードするため
  被覆系族への副作用もない。
- ユニットテスト5件追加: ①群定員超過(u=1に対し2名在籍)が自由に動かせる盤面で実際に解消すること
  ②群定員不足(l=1に対し0名在籍)が自由に動かせる盤面で実際に解消すること ③両者とも希望固定の盤面
  では何もしないこと ④cons41が空なら即no-opであること ⑤`rsiGenerateHypothesis`の"c41"/"c41s"
  focusルーティングのsmokeテスト。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## apt(適切回数)も同型の恒久的starvationを起こしていたことを確認・修正（3.208.0, 「他も検証する」）
ユーザー指示「他も検証する」を受け、3.207.0で見つけたcovOの「件数最大選択に構造的に勝てない」問題が
他のSOFT族にも及んでいないか、これまで提供された全実機ログ（7本、ログファイルとして保存されている
もの全て）を横断的にgrep調査。
- **発見**: `apt`（適切回数、weight1）の breakdown 値は全7ログで一貫して 1 または 11 — 他族
  （c1=87、c42=18、weekly=56-57 等）より一桁〜二桁小さい。全ログを "focus=apt" で検索した結果
  **一度も出現せず**、代わりに "focus=weekly" のみが件数最大フォールバックで選ばれ続けていた。
  `apt` は 3.169.0 で「探索中に一度も focus されず未研磨のまま残っていた」問題を解消する目的で
  `maxViolatedFamily` の `order` に追加されたが、**追加しただけでは件数最大選択に構造的に勝てない**
  という、covOと全く同じ欠陥を抱えたまま残っていた（3.169.0当時の検証データでは apt=37 と covO
  より大きく問題が露呈しなかったが、実運用データでは apt が最小級に落ち着くことが多いと判明）。
- **修正**: covOの周期＋最終ラウンド保証枠と同じ仕組みを apt にも適用。covOとは別の周期
  （`round % 3 == 1`、covOの`round % 3 == 2`と衝突しない）を割り当て、両者とも対象の最終ラウンドでは
  **aptを先にチェック**（covOより恒常的に小さく不利なため優先）。`rsiGenerateHypothesis`の
  focus="apt"ルーティング（destroyRepairStaff経由、3.169.0で既に配線済み）は変更不要——今回の欠陥は
  純粋に「selectionがaptを選ばない」側にあり、選ばれた後の修復経路は元から機能していた。
  HARD優先ルール・avoidの扱いは不変（focus選択のみの変更でスコアリング不変・keep-best退化不能）。
- ユニットテスト5件追加: ①周期枠がaptを小さい件数でも選ぶこと ②非対象ラウンドでは従来どおり
  件数最大にフォールバックすること ③apt(%3==1)とcovO(%3==2)の周期が衝突せず互いを上書きしないこと
  ④最終ラウンドで両方候補になった場合aptが優先されること ⑤HARD優先ルール・avoidが不変であること。
- **正直な限界**: 3.207.0と同じく、これは「周期＋最終ラウンド」という限定的な保証枠であり、
  RSIフェーズが極端に短い場合や、apt・covO以外の族（fair等）が同様の恒常的starvationを起こして
  いないかは今回の7ログでは確証が得られていない（weeklyは実際に選ばれており正常、fairは今回の
  データでは他族と同程度の桁数で明確な証拠なし＝対象外のまま）。将来的により一般的な
  「starvation検知」機構への発展が候補として残る（3.207.0からの既知の限界を継承）。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## covO周期枠が典型的な短いRSIフェーズで空振りする実効性不足を修正（3.207.0）
ユーザーが3.204.0（covO周期枠）マージ後の実機ログを提示。covO=6のまま最後まで不変で、周期枠の
効果が確認できなかったため、ログを精読して根本原因を特定・修正。
- **原因**: 典型的な5ラウンドRSIでは、`round%3==2`の唯一の該当ラウンド（0始まりで2番目、表示上
  round=3/5）が、**HF63がc3n/covUをdeprioritizeし終える前**（HF63は約3ラウンドの停滞検知を要する）
  に来てしまう。この段階では c3n(HARD,件数>0,未avoid)がまだ正当に残っており、HARD優先ループが
  そのラウンドを丸ごと消費してcovO分岐へは到達しない（実機ログ: `round=3/5 focus=c3n`）。5ラウンド
  構成では`round%3==2`はこの1回しか発生しないため、以降covOに周期枠が回る機会が一切なかった。
  一方、実機ログのround=5/5時点ではc3n・covU(HF63)・c1(E9冷却)が全てavoid/cooldown済みで、
  本来ならcovO(6)がweekly(56)に対し周期枠で優先されるはずの好機だったが、`round%3==2`(4%3=1)に
  該当せず見送られていた。
- **修正**: `maxViolatedFamily`に`roundsTotal: Int = -1`引数を追加し、**RSIフェーズの最終ラウンド
  （`round == roundsTotal - 1`）も周期枠と同格の保証枠にする**（HARDが本当に解けない場合は最終的に
  HF63が全てdeprioritizeし尽くすため、最終ラウンドは高確率でcovOの好機になっている）。
  `round % 3 == 2 || (roundsTotal > 0 && round == roundsTotal - 1)` のOR条件に拡張。
  `roundsTotal<=0`（呼出元が未対応）では従来どおり無効化＝完全後方互換。呼出元2箇所
  （フォーカス選択・N4早期終了のpivot判定）に既存の`rounds`変数（`runRsi`冒頭で算出済み）を配線。
  HARD優先ルール・avoidの扱いは不変（focus選択のみの変更でスコアリング不変・keep-best退化不能）。
- ユニットテスト3件追加: ①最終ラウンドで周期モジュロに該当しなくてもcovOが選ばれること
  （実機ログの`round=5/5`相当を再現）②`roundsTotal`省略時は最終ラウンド判定が無効化されること
  （後方互換の確認）③最終ラウンドの保証枠もHARD優先・avoidを壊さないこと。
- **正直な限界**: 今回の修正は「最終ラウンド」という1回の追加保証枠に過ぎず、RSIフェーズが
  非常に短い（rounds=2等）場合や、最終ラウンドでもなお他のHARD族が正当に残っている場合は、
  covOがそれでも研磨されない可能性は残る。根本的な「count-max選択がcovOに構造的に不利」という
  性質自体は解消していないため、将来的にはより一般的な「starvation検知（N ラウンド連続で
  focusされていないSOFT族を優先する）」機構への発展が候補として残る。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## 同種のanchor選定シャドーイングをC3系研磨・回転研磨(C1Rotate/C3Rotate)にも横展開（3.206.0）
ユーザー指示「他制約違反研磨しないのか?」を受け、3.205.0で修正したanchor選定の重み優先シャドーイング
バグ（`rep0.violations`は1セル=最重1クラスのみのため、より重い違反が同居すると軽い違反のマークが
消える）が他の研磨パスにも存在するかを`.violations`の全使用箇所をgrepして調査。同一パターンが
さらに2箇所で見つかり、3.205.0と同じ`cellFamilies`切替えで修正した。
- **`applyC3SequencePolish`（C3Polish, c3/c3m/c3mn研磨）**: anchorStaff判定が`rep0.violations`を見ており、
  c3系マークがc3n(HARD)等の重い違反と同一セルで同居すると当該職員が丸ごと漏れうる同型バグ。
- **`applyBlockRotationPolish`（3者回転研磨, C1Rotate/C3Rotateの両方が共有する汎用実装）**:
  `anchorClasses`パラメータで指定されたクラス集合を`rep0.violations`から判定しており、同じくシャドーイング
  で漏れうる。C1Rotate呼出（`c1Anchor=setOf("vio-c1")`）・C3Rotate呼出（`c3Anchor=setOf("vio-c3","vio-c3m",
  "vio-c3mn")`）の両方に影響。
- 修正は3.205.0と同一パターン: `rep0.violations`（単一クラス）→`rep0.cellFamilies`（1セルの全クラスを
  重み降順で保持する既存マップ）に切替え、`fams.any { it == ... }` / `fams.any { it in anchorClasses }`で判定。
  起点が広がるだけの後方互換な修正で、最終採否は既存の`isBetter`（keep-best）が担保するためスコアリング
  不変・退化不能。
- **他に同型パターンが無いことを確認**: `.violations`を使う残りの箇所（`V6NativeOptimizer.kt`のGLSキック/
  destroyRepairViolations/再結合の並べ替え、`V6PortAnalyzer.kt`/`V6SanityPort.kt`/`V6SwapSuggester.kt`/
  `V6WebCompat.kt`）はいずれも`.keys`のみ（特定クラスでフィルタしない）か診断/表示専用の用途で、
  「特定ファミリーで絞り込んで丸ごと除外され得る」という今回のバグの構造的前提を満たさない
  （cellが丸ごと消えるのではなく、常にSOMEクラスとして残るため対象外）。
- ユニットテスト2件追加。ブロック交換/3者回転は「値の完全な入替」のため、狙った違反自体は
  「解消」でなく「別の職員へ移動」になる点が同日1セル交換(C1Polish)と異なる（値ベースの制約は
  中身が変わらなければ誰が保持しても同じ判定になるため、2〜3者間の等価交換では消滅させられない）。
  そこで各テストは移動先にも同時に別軸（staffRange低/高）の実改善を仕込み、「総合スコアは真に改善する」
  という手計算済みの盤面で検証: ①`c3PolishFindsAnchor...`＝c3m(Want)がc3n(禁止)に同一セルで上書き
  されるT=3盤面、2者ブロック交換(w=2)で総合改善 ②`blockRotationPolishFindsAnchor...`＝c1がc3nに
  同一セルで上書きされるmirrorState型の盤面に無関係な第3職員を加え3者回転(w=2)で総合改善。
  両テストとも「旧実装ならanchorStaffが完全に空になり研磨0回のまま」だったことをコードトレースで
  確認済み（差分前フィルタ`staffPacked`も通過することを手計算で確認し、フィルタ起因の見せかけの
  不採用でないことも担保）。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## C1研磨のanchor選定が重み優先シャドーイングで職員を取りこぼす実バグを修正（3.205.0）
ユーザーが実機ログで「C1研磨しないのか?」と質問。C1Polishは実際に走っており(鏡像:0 自己:0で採用0回)、
主因はログ自身が既に警告している構造的な壁（「Dﾃ を14日で2回以上」窓ルールが供給31<需要32で1不足＝
どう組んでも解消不能）と考えられるが、コードを読んで**それとは独立の実バグ**を発見・修正した。
- **バグ**: `applyC1WindowPolish`の`anchorStaff`（研磨対象を絞る起点職員集合）が`rep0.violations`
  （1セル=最重1クラスのみを保持する単一クラスマップ）を見ていた。ある職員のc1違反セルが、同じセルで
  c3n(HARD,重み7000)のような更に重い違反も起こしている場合、そのセルの`violations`エントリは
  weight-priorityでc3n側に上書きされ"vio-c1"が消える。その職員の**全てのc1マークがたまたまこの
  シャドーイングを受ける位置にある**場合（他に独立したc1マーク位置が無い場合）、その職員は
  anchorStaffから完全に漏れ、`applyC1WindowPolish`は同日スワップ/手R1/手R2/手Bのいずれも
  **一度も試みない**まま「採用0回」を返していた（本来は改善可能な手が存在していても）。
- **修正**: `rep0.violations`（単一クラス）→`rep0.cellFamilies`（3.111.0で追加済みの、1セルに重なった
  全違反クラスを重み降順で保持するマップ）に切替え。"vio-c1"がそのセルの全クラスリストに含まれるかで
  判定するため、より重い違反に上書きされても取りこぼさない。既存のanchor集合は必ず新集合にも含まれる
  （`violations`の最重クラスは`cellFamilies`の要素の一つ）ため**起点が広がるだけの後方互換な修正**。
  最終採否は既存の`isBetter`（keep-best）が担保するためスコアリング不変・退化不能。
- ユニットテスト`c1PolishFindsAnchorEvenWhenC1MarkIsShadowedByHeavierViolationAtSameCell`を追加。
  職員iのc1違反(day2窓不足)とcons3n(禁止連続[Y,Y])が同一セル(day2)で重なる最小盤面（i2=X,X,X,Xが
  唯一の交換相手）を構築し、①修正前提の確認（`violations["0,2"]!="vio-c1"`だが
  `cellFamilies["0,2"]`には含まれる）②同日スワップが実際に試行・採用されc1もHARD(c3n)も解消される
  ことを固定。この手は旧実装では一度も試されなかった手であることをコード上のトレースで確認済み。
- **正直な限界**: この実機ログ自体で本バグが「c1 87->87 採用0回」の主因だったかは確認できない
  （c3n=2件は古泉健一のみでc1(29件)中には別セル(7/18)のマークも既にあり、この職員はいずれにせよ
  anchorに入っていた可能性が高い）。今回のログの主因は依然として供給1不足の構造的Dﾃ窓ルール
  （L31診断）が濃厚だが、本バグは実機ログの調査から独立して発見された**別データでは実際に研磨漏れを
  起こしうる**独立した実バグであり、修正の価値は変わらない。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## 人員過剰(covO)を探索フォーカスへ組み込み実際に解消する（3.204.0, 3.203.0診断の恒久対応）
ユーザーが3.203.0のcovO診断（希望固定/禁止連続/玉突き必要/動かせる の4分類）を実機で確認し、
「動かせる」と診断されたcovOセルが300秒経っても実際には解消されないことを実機ログで発見。
「2.恒久対応」（`maxViolatedFamily`にcovOを件数によらず定期的に回す）を指示され実装。
- **根本原因（2点）**: ①`maxViolatedFamily`の`order`リストに`covO`は既に登録済みだったが、SOFT選択
  ロジックが単純な「件数最大」のため、covOは日×シフトのセル単独違反で件数が常に一桁台に留まり、
  c1(87件)/c42(18-23件)/c3mn(8-20件)/weekly(56-57件)のような数十件規模の族に**構造的に絶対勝てない**
  （apt/weekly/fairが3.169.0/3.170.0で「orderに追加するだけ」で直った先例とは異なり、covOは追加だけ
  では効かないケース）。②仮にcovOがfocusされても、`covO`は`markNeed(k,j)`で`needViolations`に載り
  `report.violations`（セル"i,j"マップ）には現れないため、他の focus 未対応族の受け皿である
  `destroyRepairViolations`（`report.violations.keys`基準）はcovO専用のヒントを一切持てず、focusが
  回っても実質ランダムな空振りになる。
- **対応1（周期的保証枠）**: `maxViolatedFamily`に`round: Int = -1`引数を追加。HARD優先ロジックの直後・
  SOFT件数最大フォールバックの直前に「3ラウンドに1回(`round%3==2`)、count>0かつavoid対象でなければ
  covOを件数によらず優先する」分岐を追加。HARDの「件数に関わらず先に狙う」と同じ発想を、covO専用に
  弱く適用したもの。`round<0`（呼出元が未対応）では従来どおり無効化＝完全後方互換。呼出元2箇所
  （フォーカス選択・N4早期終了のpivot判定）に`round`を配線。
- **対応2（専用repairオペレータ）**: `applyCovOFree`を新設。covOセル(k,j)の在勤者から、
  ①本人希望でない ②移すと禁止連続(c3n)を作らない ③移動先で covO が悪化しない（受け皿あり）
  の3条件を満たす1人を実際に他シフトへ移す。3.203.0のcovO診断（V6PortAnalyzer.diagnoseCoverage）と
  **全く同じ判定基準**を「診断」でなく「実行」する対（診断＝読取専用・こちらは探索オペレータ）。
  希望固定/禁止連続で動かせないセルはそのまま残す（安全側・診断の「解消不可」ケースと整合）。
  `rsiGenerateHypothesis`に`"covO" -> { applyCovOFree(...); repeat(6){destroyRepairDay(...)} }`を配線
  （covU chainと対称の構成）。
- **安全性**: 両対応とも focus選択/探索オペレータの追加のみ＝重み・スコアリング不変。最終採否は
  既存のラウンド`better()`keep-best（hard→total→weighted辞書式）が担保するため退化不能。
  `applyCovOFree`は被覆総量を保存しない（過剰シフトから引くだけ）が、移動先を「covOが悪化しない」
  条件で選ぶため新たなcovOを作らず、covUへの影響もない（covUは別途covUチェックで保護されないが、
  covOセル(k,j)から抜けるのはそもそも需要超過分なので、抜けた後もk,jのcovUには影響しない）。
- 検証: ユニットテスト8件追加（`V6NativeOptimizerChoiceTest`）。①周期枠がcovOを小さい件数でも選ぶこと
  ②非対象ラウンドでは従来どおり件数最大にフォールバックすること ③HARD優先ルールが不変であること
  ④avoid指定時は周期枠も無効化されること ⑤`applyCovOFree`が自由に動かせる盤面で実際にcovOを解消
  すること ⑥希望固定の盤面では何もしないこと ⑦`rsiGenerateHypothesis`のcovO focusルーティングの
  smokeテスト。既存の`maxViolatedFamily`呼出（round省略）は全てデフォルト値`-1`で従来どおり無効化
  ＝完全後方互換を確認。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## 人員過剰(covO)の「なぜ減らないか」診断を新設（3.203.0, ログ強化）
ユーザーが実機ログを提示し「回数制限がない『有』が増えない理由」「特定日はなぜ『有』にならないのか／
なぜ回数違反研磨しないのか」を質問。ログ解析の結果、既存の `CoverageDiag`（人員不足=covU の原因診断、
空き番/玉突き/希望固定/禁止連続の4分類）は **covU専用で covO（人員過剰）には一切対応していなかった**
と判明（後処理研磨ログは「採用0回」を報告するだけで、なぜ試した手が却下されたか＝どの族が悪化したかは
一切出力されず、ユーザーが自力で原因を推測するしかなかった）。ユーザー指示「ログ強化して理由が分かる
ようにする」を受け、covUの診断と対になる **covO版の原因診断**を新設した。読取専用・エンジン非変更。
- **`V6PortAnalyzer.kt`**: `CoverageSurplus`（day/shift/need/got/excess/reason）データクラスを新設し、
  `CoverageDiagnosis` に `totalSurplus`/`surpluses`（既定値付き＝非破壊）と `hasSurplus` を追加。
  `diagnoseCoverage` に covU診断ループと対称な covO診断ループを追加: 各過剰枠(k,j)について、
  **在勤者（現在そのシフトに配置中の職員）を「動かせるか」で4分類**
  （希望固定=`wish[i][j]==k`／禁止連続=担当可能な代替シフトが全て`makesForbiddenRun`で塞がる／
  玉突き必要=代替はあるがどの移動先も`covOCell`が悪化する（受け皿なし）／動かせる=`c3n`を作らず
  かつ移動先で`covOCell`が悪化しない代替が存在）。ヒントは「動かせる」人数の有無で
  「解消可能（『直し方を探す』で解消可）」/「玉突きが必要」/「希望調整か担当を減らす必要」の3段。
  covUの`cascade`判定（`covUCell(m,-1)>covUCell(m)`＝抜くと悪化）と対称に、covO版は
  `covOCell(m,+1)<=covOCell(m)`（1人足しても悪化しない＝受け皿あり）で判定。
- **[重要な限界の明記]** covOは全19族中もっとも軽い重み(1.0)のため、「動かせる」（HARD/禁止連続を作らない）
  ことと「実際に最適化が動かす」ことは別問題。動かした先で weekly(重み1だが実機で件数57など巨大になりうる)
  等が1点でも悪化すれば `isBetter` に負けて不採用になる。本診断は**構造的に動かせるかの可否**を示すもので、
  「動かせるのに動いていない」ケースの最終原因（＝他族とのトレードオフで負けている）そのものの特定までは
  行わない（そこは個別セルの「直し方を探す」＝FixSuggesterのdiff表示で確認する運用を案内）。
- **配線**: `MagiViewModel.kt` の `analyzeParallel()` で `diagnoseCoverage(...).takeIf { it.hasShortage }`
  だった判定を `takeIf { it.hasShortage || it.hasSurplus }` へ変更（covU=0・covO>0のみのデータで診断が
  丸ごと破棄されない対応）。`logLines()` を「不足セクション／過剰セクション」の2段構成へ拡張し、
  エクスポートされるMAGIログに `[W] CoverageDiag: 人員過剰 合計N — M枠（なぜ減らないか）` ＋
  枠ごとの内訳行が新たに出力される。`CoverageDiagnosisCard`（MagiDashboardCards.kt）も同様に
  過剰セクションを追加（不足と独立に表示、過剰のみのデータでもカードが出るよう早期returnを
  `!hasShortage && !hasSurplus` に変更）。`UiState.coverageDiag` のコメントも実態に合わせて更新。
- ユニットテスト2件（`V6PortAnalyzerTest`）: ①在勤2名とも希望固定でなく移動先(休)に受け皿がある盤面で
  「動かせる2人・解消可能」が出ることを固定 ②在勤2名とも希望固定（希望どおり配置済み＝pref違反ゼロ）の
  盤面で「希望固定2人」＋希望調整の案内が出ることを固定（実機問い合わせの根本原因の再現）。
  スコアリング不変（読取専用の診断追加のみ）。

## docs/business-logic.md のドリフト修正（3.202.0, 「残不具合などを修正する」の一環）
外部提示の不具合一覧の1件（`docs/business-logic.md` が実装からドリフトしている）をコード照合で確認し修正。
実コード（`MirrorCore.kt` の `MirrorKeys`）を基準に4点訂正:
- **族数の誤り**: 見出し「18 種の違反と重み（HARD 4／SOFT 14）」→ `MirrorKeys.all` は実際19族（HARD4+SOFT15）
  ＝表自体も19行あり見出しと自己矛盾していた。「19 種の違反と重み（HARD 4／SOFT 15）」へ訂正（README.md の
  目次「重み19種」表記とも整合、README側は元々正しかった）。
- **covO 重みの誤り**: 表の `covO` 行が「0.5」のままだったが、実装は 3.148.0（HF77明示指示）で
  最適化器基準の 1.0 へ統一済み（チェッカー`weightedScore`のみ0.5だったfactor-2乖離の解消）。「1.0」へ訂正し、
  統一の経緯を注記に追加。
- **apt の表示有無の誤り**: 「apt/fair/weekly は内訳チップ（UI）には出さない」と書かれていたが、実装は
  `BreakdownCard`（`MagiDashboardCards.kt:930-931`）で apt を「人数の範囲」グループ、fair/weekly を「任意」
  グループのチップとして表示している（コード確認）。「3者とも内訳チップに表示する」へ訂正。
- **fair/weekly の「場所表示なし」の誤り**: 3.149.0 で `ViolationReport.distLocations`（fair=職員×シフト・
  weekly=職員の偏り箇所リスト）が追加され、内訳パネルからタップで該当職員へフォーカスできるようになっている
  （表自体はグリッドへは出さない設計のまま＝飽和回避のためグリッド不変、の部分は正しかったので維持しつつ
  「場所表示なし」の断定のみ訂正）。
- 読取専用のドキュメント修正のみ（コード変更なし）。「最終更新」日付も更新。CLAUDE.md 自身に本ドリフト
  修正の記録として本セクションを追加（同ファイルの更新ルール「コードを改修したら文書を同じコミットで
  更新する」の逆方向＝ドリフトを発見した際は文書側を直接修正）。

## FixSuggester の改善提案リストが同一の盤面変化を複数回表示する不具合を修正（3.202.0）
ユーザー指示「残不具合などを修正する」。外部提示の不具合一覧（8件）を並列エージェントで実コード照合した際、
`V6SwapSuggester.kt`（`FixSuggester.suggest`、勤務表タブのセルタップ→「直し方を探す」で出す改善提案）の
重複排除が旧署名（`kind.name`＋`ops`列挙順そのまま）に依存しており、**同一の盤面変化が異なる見た目で複数回
表示される**バグを本セッションで直接コードトレースし確認（自動検証エージェントの要約を鵜呑みにせず再確認）。
3種の見落とし:
- ①**SWAP_XDAY(Phase5) の起点依存で ops が逆順生成**: `(i1,j1)`↔`(i2,j2)` の全ペアを走査するため、同じ
  スワップが `[i1側の脚, i2側の脚]` と `[i2側の脚, i1側の脚]` の2通りの順序で2回生成され、旧署名
  (`kind+ops順そのまま`)では別物として扱われていた。
- ②**Phase5 が同日(`j2==j1`)を除外しておらず Phase2(SWAP) と重複**: 同日2人交換は Phase2 が既に生成する
  「SWAP」種と全く同じ盤面変化なのに、Phase5 が `kind="SWAP_XDAY"`・ラベル「（別日）」で同じ手を追加生成
  （実際は同日なのに「別日」と誤表示もしていた）。
- ③**SWAP_MULTI(Phase4) の3人巡回が退化すると実質2人交換**: 3脚のうち1脚が `sa==sb`（無変化）になる局面で
  盤面変化としては通常の2人スワップと同一だが、`kind="SWAP_MULTI"` で別扱いされ重複表示され得た。
- **修正**: 重複排除の署名を「`kind` 名を含めない」「`ops` を `toShift == 現在の盤面値` の脚（実質no-op脚）で
  事前に除外」「残った脚を `(staff, day)` でソートしてから結合」の3点へ変更。`kind` を落とすことで、
  「どのアルゴリズムが見つけたか」でなく「最終的にどのセルがどう変わるか」という**盤面変化の実体**だけで
  重複判定する。`s`（探索終盤時点の盤面）は全 Phase の tryOps が適用→復元を徹底しているため、この時点では
  常に normalize 後の元盤面と一致しており「`toShift==s[staff][day]` の脚は無変化」という判定は安全
  （途中経過の盤面ではなく最終的な base 盤面との比較）。日付(`day`)は意図的に署名から除外したまま維持
  （表示の粒度をこの修正のスコープ外として不変に保った・意図的な仕様の可能性があり範囲外の変更を避けた）。
- **読取専用の改善提案生成ロジックのみの変更＝スコアリング不変**（`UnifiedViolationChecker`/`isBetter` 等の
  採否判定・エンジン本体は無変更。表示される候補の重複除去のみ）。
- ユニットテスト `V6SwapSuggesterTest.suggestDoesNotDuplicateSameBoardChangeAcrossKinds`: 2職員×2日の最小盤面
  （`staffRange` の low/high 違反が同日1スワップで同時解消する構成）で、①`suggest()` が返す全提案の
  正規化署名（`(staff,day,toShift)` の実質脚のみ・ソート済み）に重複が無いこと ②その唯一の解（低/高を同時
  解消する同日スワップ相当）が `kind`/`ops`順に依らず重複なくちょうど1件だけ返ることを検証。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## C1研磨・手Bの玉突き連鎖に c1Pref 優先付けを追加（3.201.0、外部検証の追認）
ユーザーが別セッションでの独立検証記録（`applyC1WindowPolishV2〜V5`＝c1Pref誘導・非貪欲SA系の代替案、
README曰く「repo_cleanには一度も適用しておらず、安定して既存を上回る変種が無かった」）を提示。精読の結果:
- **V2〜V5は不採用が妥当**（README自身の結論を追認）。全て「既存の手（同日スワップ＋findCovUChain玉突き）の
  範囲内でもっと賢く探索する」方向（c1Pref誘導・非貪欲SA）であり、3.200.0で判明した根本原因（回数固定職員には
  count-changing系の手がどう探索を工夫してもhigh(45)>c1(4×窓数)で構造的に棄却される）を解消できない。実測でも
  V2(c1Pref誘導のみ)はgoldenで無効果(c1=92=既存と完全一致)、V4/V5(非貪欲SA)はgoldenで勝つ(66〜85)が
  sample_v6では既存に対し14/15〜15/15で負ける(13〜18 vs 既存2)＝データ依存で汎化しない。2.55.0/2.56.0
  「脱出ヒューリスティクスは中立or有害」の教訓と一致。3.200.0のR1/R2（count-preserving）は「探索を賢くする」
  でなく「そもそも生成できなかった手を追加する」より根本的な解決＝据え置きが正しい。
- **`c1Pref`パラメータ自体は低リスクな追加**（`findCovUChain`への非nullオプション引数・既定null・既存呼出元は
  無変更）と判断し、これのみ抽出して`applyC1WindowPolish`の手B（`findCovUChain`呼出）に配線した。
  `c1Deficient(i2,x2,day)`（全cons1横断でi2がx2について day を含む窓で不足しているか）を新設し、手Bの
  玉突き連鎖の候補選定を「連鎖に組み込む相手が、たまたまその相手自身のc1不足も一緒に解消するか」で優先付け
  （`candidates()`内で該当候補を先頭へ並べ替えるだけ・安全条件(canDo/wishLock/c3n)は不変・探索の正しさは
  常にisBetterが最終担保）。V2〜V5のSA機構やcrossDayペア評価（Variant B）は導入しない。
- `findCovUChain`（V6SearchOperators.kt）に`c1Pref: ((staff,shift,day)->Boolean)?=null`を追加。全既存呼出元
  （`V6NativeOptimizer.kt`のcovU連鎖・`applyCovUChains`・自身の隣接日調整の再帰呼出・`ChainFillTest`全8箇所）
  は非nullを渡さないため完全に無変更（デフォルト引数のみの追加）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## C1研磨アルゴリズムの再設計＝回数保存移設プリミティブの追加（3.200.0）
ユーザー指示「C1研磨の新しいアルゴリズムを考え直す」。既存 `applyC1WindowPolish`（手A=同日スワップ／手B=直接移動+
`findCovUChain`玉突き）は精読の結果、**どちらも「i の X 回数を+1する」count-changing 手しか生成できない**と判明。
golden_state の残差解剖（Python実測）で c1=115 fires のうち relocation-only=48（**休 fires の80%が
staffRange の lo==hi 固定職員由来**）は、X追加が high(45)>c1(4×窓数) でほぼ必ず isBetter に棄却され、
**i自身のXを余剰位置→不足窓へ移す回数保存の移設**だけが唯一の改善手と判明（行内2日swapの貪欲シムで
c1 115→62, -46%）。この欠落を埋める2つの新規プリミティブを追加した。
- **手R1＝鏡像長方形**: i=[X@j1,a@j] ↔ i2=[a@j1,X@j] の4セル交換。両職員の回数・日別人数がともに完全保存
  （groupViol/pref/low/high/apt/c2/covU/covO/c41系まで構造的不変）＝isBetterはc1/c3系/weeklyだけの勝負になり
  最も採用されやすい安全な移設。
- **手R2＝自己2日swap**: i の X@j1 ↔ a@j。i自身の回数は保存（low/high/apt/c2/pref/groupViol不変）だが日別
  人数が変わるため、離脱側2箇所を `p.covUCell`（source of truth）で事前除外してから適用。
- 挿入位置は手A(同日交換)の直後・手B(直接移動)の前（保存性の強い順=A→R1→R2→B）。c3n(HARD)は
  `p.makesForbiddenRun` で事前枝刈り（見逃してもisBetterが最終拒否＝安全側）。ドナー選定は「その日を抜いても
  当該ルールのどの窓も新規に不足化しない」保守判定（`donors()`、(i,x)単位で遅延キャッシュ・盤面変化毎に無効化）。
  採否は既存と同じ`isBetter`(hard→total→weighted)のkeep-best＝退化不能・HF77非該当（重み不変）。
  add-fixable（追加が唯一の解の局面）は既存手A/Bの担当のまま＝手クラスが互いに素で2.49.0型の冗長を作らない。
- **設計プロセス**: ユーザーの明示要求でultracode（Workflowツール）を用い、①現行到達範囲の精読
  ②golden_state残差解剖(Python実測)③意味論/安全制約の完全列挙、の3並列分析→3設計者(最小プリミティブ拡張/
  DP誘導配置/一括マッチング)が独立設計→審査員パネル、の構成で実行。session limitで審査フェーズとbatch-matching
  案が失敗したため、完走した2設計（本採用のR1/R2最小拡張、およびDP誘導配置=bitmask DPで職員行内のX配置を
  厳密最適化する案）を主導者自身が比較審査した。**DP案は不採用**: 疑似コード自身が「parent復元ロジックは
  オフバイワンを作りやすい」と未完成を認めており、期待削減量が同等（62〜80 vs 65〜80）なのに正しさへの
  リスクが実装前から顕在化していたため、このコードベースの一貫した流儀（違反アンカー・first-improvement・
  isBetter keep-best・最小差分）に忠実なR1/R2案を採用した。
- **検証**: Python忠実移植（`c1_replay.py`, 手A→R1→R2→手Bの優先順位を含む全ロジックを再現）で golden_state を
  リプレイし、手Aだけで既に多くの複合窓解消（c1 115→91、15件採用）が起きること、R2が9件発火して手Bが担って
  いた解消を回数コストゼロで代替することを確認（このデータでは最終c1値は同値だが、count-changing を避ける
  ぶんlow/high/apt等の副次コストを回避できている＝真の目的関数上はR2の方が優れた解）。R1は鏡像相手が
  偶然存在せず0件（設計のrisksで自認済みの限界＝盤面依存）。手計算で検証した最小盤面（K=2,T=4,D=2窓）を
  ユニットテスト`C1RelocationPolishTest`3件に固定: ①R1が同日スワップ不採用の局面で鏡像交換により採用され
  c1減少・両職員の回数保存・HARD不変 ②相方が構造的に存在しない局面でR2のみが発火しc1完全解消・自職員の
  回数保存 ③既に充足済みでは採用0(no-op)。fair/weeklyもR1/R2が回数を保存するため不変であることを手計算で
  確認済み（T<7の当該テスト構成では曜日バケット衝突がなくweekly偏差が構造的に不変）。
- サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。最終判定は CI
  （v6-engine-check の testDebugUnitTest／Release Build）。

## ネイティブSAチャンクの-1セル未対応＝実データで番兵発火（高速化の根本修正, 3.199.0）
ユーザー指示「高速化対応する。そして、高精度化対応する」。最大の実害＝**3.185.0 未③「実機の実データで
ネイティブ探索が無効化される」（`NativeBridge: SAチャンク整合性NG status=1`→番兵発火→Kotlin退化＝正しいが遅い）**
を根本修正した。速度（ネイティブ探索の復活）と精度（同一300s予算で反復数増→品質向上）が同時に効く。
- **根本原因（コードパスで確定）**: `normalizeSchedule`（MirrorCore:459）は範囲外セルを **-1** に写像し、
  `SaOptimizer.runWorkerNative` はその盤面をそのまま flatten して C++ へ渡す。しかし C++ `SaChunk.deltaApply`
  は old/nw の範囲を検証せず、`old=-1` のとき ①`ssn[i*K+(-1)]--`/`dsn`/`rowMask`/`dayShiftMask` の
  **範囲外書込＝カウント/ヒープ破壊** ②`contribRangeApt(i,-1)` の範囲外読み ③`wd` の work 判定
  `old != restIdx` に範囲ガード無し（**Kotlin DeltaEvaluator は 3.92.0/3.95.1 で同一クラスをハードニング済み
  ＝C++ミラーの直し漏れ**）。score がドリフト→チャンク末尾の自己整合 `full != curVal` → status=1。
  fullEvalParts は全て範囲ガード済みのため「評価器パリティは一致するのに SaChunk 自己整合だけ失敗」という
  実機観測と完全に整合。SA/LAHC/ALNS/Polish の4ランナーは全て SaChunk を共有＝1修正で全経路を回復。
- **再現（修正前に実施＝規律どおり）**: `tools/native/state_to_flat.py` を新設（Problem.kt 構築＋
  NativeEval.createHandle 平坦化の忠実な Python 複製。state JSON→MAGIFLAT1 形式）し、repo 内の実データ
  `app/src/test/resources/golden_state.json` を変換。harness に実データローダ＋「-1(~3%)/非canDo(~2%)
  ノイズ入り盤面」を追加し実行 → **修正前バイナリは `free(): invalid pointer` でクラッシュ**（実機では
  カウント破壊→status=1 で済んでいたが、ネイティブクラッシュにもなり得る潜在UBだったことが判明）。
- **修正（magi_native.cpp・2箇所）**: ①`deltaApply` に `oldIn`/`newIn`（`[0,K)` 検証）を導入し
  ssn/dsn/rowMask/dayShiftMask 更新と wd の work 判定をガード（resetBoard/fullEvalParts と対称化）
  ②`contribRangeApt` に `k<0||k>=K → 0` ガード（contribCov/contribFair は既にガード済み＝対象漏れの解消）。
  他の盤面値インデックス箇所は総点検で全てガード済みを確認（値の比較のみ or `k>=0&&k<K` ガード付き）。
- **検証**: 修正後 harness = **3,596,099手・mismatch=0・クラッシュなし**（実データ as-is＋ノイズ入り×6シード
  ×scalar/bit両経路＋拡張合成全fixture）。BENCH bit-op **×2.16**（従来×1.94〜2.21帯＝ガード追加の速度退行なし）。
- **フィクスチャ拡充（backlog#6 残課題の解消）**: 合成 builder に実データ形状を追加＝**休(rest)シフトへの
  range/apt/c1/c2**（旧: k>=1 のみで、実データ最重要の「休 上下限/apt/窓」が未照合だった）・**実現不可能な希望**
  （非canDoへのwish）・**-1/非canDo混入盤面**。CI（native-parity.yml）に golden_state.json→flat 変換ステップを
  追加し、以後 push/PR ごとに実データ形状でも自動照合される。
- **正直な限界**: 実機の 2026-07 state（11シフト）そのものは repo に無いため、実機での status=1 解消は
  次回実機ログ（NativeBridge 行の番兵不発）で最終確認する。ただし -1 は normalizeSchedule 経由で証明可能に
  ネイティブへ到達し、実機の症状（評価器パリティ一致×SaChunk自己整合NG）はこのバグの症状と一致する。
  スコアリング不変（ガードは正当な評価の対称化のみ・全パリティ一致が担保）。

## 交互最適化（Alternating Optimization）をソフト制約研磨に追加（3.198.0）
ユーザー指示「玉突き研磨ができなかったら違う方法やアルゴリズムで研磨を続ける」＋「凸最適化論文を取り入れる」→
明示的に「交互最適化（Alternating Optimization / 交代最適化＝1変数ずつ順に最適化して巡回する座標降下法）を
ソフト制約違反研磨に新アルゴリズムで追加」と確定。3.197.0 の長方形交換（クロス日の2職員×2日）が届かない
**同日内の「休スロットの割当先」** を、別種の手（座標降下）で二方向から詰める。
- **新パス `applyAlternatingSoftPolish`（V6HotfixPasses）**: ブロック＝各日(列)。その日の (シフト人数=被覆) を
  固定したまま、希望未固定(wish<0)の職員を「個人別回数(range 90/45)・適切回数(apt 1)・**曜日平準化(weekly 1)**」の
  限界費用が最小になるよう **最小費用割当(Hungarian＝割当LP＝凸最適化・既存 `MinCostAssignment` を再利用)** で
  同日内同時最適に再配置し、日 j を 0..T-1 と巡回して1スイープで1日も変化しなくなるまで（座標降下の不動点）反復する。
- **既存 `applyDayAssignmentPolish`（range/apt のみ・単発）を ①weekly を費用に含め ②反復収束(交互)まで一般化**した
  もの。weekly を費用に入れる意味＝その日の「休スロット」を誰に割り当てるかで各職員の曜日別勤務数が変わる（被覆は不変）。
  「その曜日に働き過ぎの職員へ休を、少なすぎる職員へ勤務を」割り当てる候補を Hungarian が生成し曜日偏りを直す。
  weekly 限界費用＝当日を勤務/休にしたときの職員 i の曜日バケット L1 偏差変化（当日寄与を除いた marginal・重み1）。
- **rectangle(3.197.0, クロス日) と AO(同日内) は別種の被覆保存手＝相補的**。両方を後処理に配線（rectangle の直後に AO）。
  採否はいずれも実目的関数 `isBetter`（hard→total→weighted, keep-best）＝退化なし。fair 等の費用に無い soft 族も
  acceptance が悪化を許さない。純 Kotlin 後処理＝ネイティブ hot-path 非干渉（parity 影響なし）。HF77 非該当（重み不変・
  探索内部の受理は既存 isBetter、新規オペレータは keep-best で退化不能）。nsp_bench は後処理/RSI focus を模擬できない
  ため 2.55/2.56 の教訓（新規近傍は中立or有害）を踏まえ **coordinate-block＋Hungarian最適＋keep-best＋不動点即終了** の
  限定設計で原理採否（3.74.0/3.170.0/3.197.0 と同方針）＝退化リスクゼロ・空振り時の予算浪費のみ（sweep 上限4・
  各日 changed 時のみ checker）。
- 検証: ユニットテスト `WeeklyRebalancePolishTest` に2件追加（①2職員14日で AO が同日再配置により weekly を下げ・
  covU/covO/HARD 保存 ②均等配置で採用0＝no-op）。サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧
  均衡0を静的確認。最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## weekly（曜日平準化）研磨の穴を長方形交換で埋める（3.197.0）
ユーザー指示「違反の研磨などが出来ていない箇所を教えてください」→「すべて（対応する）」。実機ログの
残差（SOFT: c1=87・**weekly=56**・c42=18・covO=6・low=10・fair=9・c3mn=8・c3m=7・high=4・apt=1）を研磨
カバレッジと突合し、族ごとに切り分けた:
- **既に最適に研磨済み（新規パス不要）**: c42/c42s/c2/covO は `applyCyclicSwapPolish`（同日2者スワップ
  ＋3者回転、**実目的関数 isBetter で採否**）が被覆保存の同日手を網羅済み。専用パスは 2.49.0 で「CyclicSwap の
  部分集合＝冗長」として revert 済のため再追加しない。fair も同群・同日2者スワップで CyclicSwap が拾える。
  残差は「単一の同日手では下がらない局所最適」＝クロス日/大きな手が要る領域。
- **真の穴＝weekly**: weekly は「職員が特定曜日にばかり勤務する」L1偏差（`weeklyDevOfBucket`＝各曜日の勤務数
  の round(平均) からの偏差和）。**同日2者スワップは勤務↔勤務では曜日別の勤務/休が変わらず weekly をほぼ
  動かせない**（勤務種類が入れ替わるだけで両者とも「その曜日に勤務」の事実は不変）。既存の
  `applyWeeklyEqualizePolish` も同日スワップ＋**分散指標**（L1と別物）で二重に届いていなかった。これが
  「weekly の研磨ができていない」実害の根本（残差の最大級）。
- **対応（新規 Kotlin 後処理パス `applyWeeklyRebalancePolish`）**: **被覆保存の 2職員×2日 長方形交換** を導入。
  職員 i が「過剰曜日の日 j1 で勤務(x)・過少曜日の日 j2 で休」、相手 i' が「j1 で休・j2 で勤務(y)」のとき両者の
  j1/j2 を丸ごと入替える（i: j1→休/j2→y、i': j1→x/j2→休）。各日の各シフト人数は保存（j1 の x は i→i'、
  j2 の y は i'→i へ移るだけ）＝covU/covO・群レンジ・pref 不変で、i の勤務が過剰曜日→過少曜日へ移り weekly が
  下がる。fair や low/high/apt/c2 など per-staff 族も副次的に動く。**採否は実目的関数 isBetter のみ**
  （hard→total→weighted、total は weekly/fair を含む）＝退化なし（keep-best）。weekly>0 の職員のみ起点＋
  first-improvement＋4セルとも movable(wish非固定) ガードで、空探索は即終了。`runPostOptimization` の
  フィックスポイント後・equalize 系（分散指標）より前に配線（L1指向のこのパスが先に効く）。
- **注意点/範囲**: per-day 族（covO/c42/c2）は長方形交換では per-シフト per-日 人数が不変なため動かない
  （＝同日 CyclicSwap の担当のまま・対象外で正しい）。純 Kotlin 後処理＝ネイティブ SA/ALNS/hf80 ホットパスに
  非干渉（parity 影響なし・C++ ミラー不要。3.153.0「後処理チェーンは移植しない」と同方針）。HF77 非該当
  （重み不変・探索内部の受理は既存 isBetter、新規オペレータは keep-best で退化不能）。nsp_bench は本パスの
  RSI focus/後処理を模擬できないため、2.55/2.56 の「新規近傍は中立or有害」の教訓を踏まえ**violation-anchored
  ＋keep-best＋即終了**の限定設計で原理採否（3.74.0/3.170.0 と同方針）＝退化リスクゼロ・空振り時の予算浪費のみ。
- 検証: ユニットテスト `WeeklyRebalancePolishTest`（①2職員14日で weekly を長方形交換が下げ・covU/covO/HARD 保存
  ②均等配置で採用0＝no-op）。サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧/角括弧均衡0を静的確認。
  最終判定は CI（v6-engine-check の testDebugUnitTest／Release Build）。

## 操作ログのフェーズ遷移・必須改善ログが巻き戻り時計で欠落するバグ修正（3.196.0）
ユーザー指示「新しい論理的不具合や問題点などを見つけてください」を受けたログ精読で発見。実機ログ
（RSI++, 予算300s）で「RSI weekly（経過165秒）」の次に「後処理HF80（経過275秒）」が直接続き、その間の
Phase3=ALNS Refine（コード上 `runRsiPlus` の `alnsSec=budgetSec*0.30=90s`, 165〜255秒に相当）の
フェーズ遷移ログが1件も出ていない不自然な空白を確認。`V6NativeOptimizer.runV6FullOptimize`
（`MagiViewModel.kt`）のスロットル判定を追ったところ、原因を特定:
- `onProgress` コールバックの第4引数 `elapsed` は呼出元（`runV5`/`runAlns`/`runRsi`）ごとに**その関数
  自身の開始時刻からの経過**（フェーズ境界で0付近へ巻き戻るローカル時計）。この事実自体は既に把握済みで
  （`runWall0` 変数のコメント「[N6] 経過表示は壁時計基準（onProgressのelapsedは仮説ローカルで巻き戻る）」
  ＝表示用の経過秒数は既に `System.currentTimeMillis() - runWall0` の壁時計へ切替済み）、HF63連携
  （3.102.2で `hf63.updateFromBreakdown` 呼出を壁時計へ修正済み）でも同種の教訓が記録されていた。
- しかし**同じ関数内の直後2箇所のスロットル判定式自体（`elapsed - lastPhaseLogMs >= 2_500` /
  `elapsed - lastHardLogMs >= 1_500`）は生の `elapsed` のまま残っていた**（表示だけ直して判定式は
  直し漏れ＝3.102.2の修正対象外）。新フェーズ開始直後は `elapsed`（新フェーズのローカル値、ほぼ0）が
  `lastXxxMs`（前フェーズ終盤に記録された、比較的大きいローカル値）を大きく下回るため差が負になり、
  新フェーズの継続時間が「前フェーズの残存値＋閾値」に届かない限り、そのフェーズ遷移ログが**1件も
  出力されずに丸ごと欠落**していた（今回のALNS Refine=90秒 がまさにこのケース）。「必須違反が改善」の
  マイルストーンログも同じ変数を使っており同型の欠落があり得る（`rep.hard==0` のケースのみ短絡的に保護済み）。
- **修正**: 両スロットル判定・`lastPhaseLogMs`/`lastHardLogMs` の保持値を、既に表示に使っている壁時計
  （`wallElapsed = System.currentTimeMillis() - runWall0`）へ統一。フェーズ境界をまたいでも単調増加する
  ため、上記の欠落は起きなくなる。ログの出力有無・タイミングのみの修正＝最適化ロジック・スコアリング・
  探索の受理判定は完全に不変（診断表示のみ）。HF77非該当（ログ表示のみ、重み/パラメータ不変）。
- 検証: サンドボックスは Kotlin コンパイル不可＝ブレース/丸括弧均衡0を静的確認。最終判定は CI
  （Release Build）＋次回実機ログで長時間フェーズ（ALNS Refine等）の遷移ログが欠落しないことを確認。

## Android 16並行/並列監査＋16KBページ対応（3.181.0）
ユーザー提示の「Android 16(API36)＋Kotlin 2 世代の並行・並列設計指針」に照らし全コードを監査。**唯一の実バグ＝
16KBメモリページ非対応**を修正し、他の指針は既に充足 or 盲目適用が有害と判定。
- **[実装] 16KBメモリページ対応**（essay の NDK 注意点）: `app/src/main/cpp/CMakeLists.txt` に
  `target_link_options(magi_native PRIVATE "-Wl,-z,max-page-size=16384")` を追加。**NDK r26.1 は 16KB
  アライメントを既定にしない**（既定化は r27/r28 以降）ため、16KB ページの Android 16 端末で 4KB アライメントの
  `.so` がロードできず、`NativeGate.available=false` で Kotlin フォールバック＝**クラッシュはしないが native 加速が
  丸ごと失われる**（対象プラットフォームで速度退行）。flag で 4KB/16KB 両ページ端末にロード可能化（4KB 端末でも
  無害・低リスク）。NDK は CLAUDE.md 方針どおりピン留め維持（版上げより surgical）。
- **[非変更・盲目適用は有害] synchronized→Mutex**: `SaOptimizer:92/110`（SAワーカー集約）・`V6FinalPort:230`
  （進捗ロック）・`KigouFormat:31`（ICU Transliterator）の3箇所は**いずれも非suspendの短いCPU臨界区間**。
  `Mutex.withLock` は suspend 前提で、非suspend呼び出し鎖（`flush` ラムダ等）を作り替えねばならず、ホットパスに
  サスペンド越しのオーバーヘッドを足す＝pessimization。essay の「synchronized→Mutex」は suspend な I/O 待ちで
  ディスパッチャスレッドをブロックしない指針で、ここには非該当（brief CPU critical section は synchronized が正）。
- **[既に充足＝変更不要]**: ①Dispatchers IO/Default の並行/並列切り分け（3.176.0）②下位モジュールの
  withContext メインセーフ化（3.176.0）③MutableStateFlow＋`update{copy}` の不変更新（既存）④@Volatile/AtomicInteger/
  compareAndSet のロックフリー（既存）⑤ART内部/非公開API へのリフレクション＝**皆無**（grep 0件）⑥Thread/runBlocking/
  GlobalScope/Executors＝**皆無**。⑦C++層に std::thread/pthread＝**皆無**（並列は Kotlin async 層・C++ は JNI 毎に単スレッド
  ＝16KB は thread stack でなく .so ロードの問題）。
- **[所見・実害なし]**: FGS runtime quota（Android 16）＝5分の最適化ジョブは DATA_SYNC FGS の日次上限内。kill耐性は
  WorkManager＋ファイルスナップショット復元（C1）で対応済。User-Initiated Data Transfer Job は「データ転送」用途で
  CPU計算の本ジョブには不適合＝移行不要。
- 検証: サンドボックスは arm64 クロスコンパイル不可＝flag は lld 標準（NDK26 の lld 対応）で低リスク、最終判定は
  CI（Release Build＝CMake/NDK が .so をリンク）。スコアリング/エンジン不変（ビルド設定のみ）。

## 下流→上流ディープリンク「設定で直す」（3.182.0, 3.180.0 タスク2の完了）
3.180.0 で「粗い経路は成立・精密ディープリンクは未実装＝バックログ」とした項目を、grilling で範囲確定（対象=pref/covU/covO
のみ／入口=要確認一覧／スクロール=事前選択のみ）して実装。**表示・導線のみ・スコアリング不変**（読取専用の違反マップから
編集画面の職員/シフトを事前選択するだけ）。
- **ConfirmItem 拡張**: `wishStaff`/`needShift`（既定 null）を追加。`confirmItems` で **pref**（violationCells の族に "pref" 含む）→
  `wishStaff=i`、**covU/covO**（needViolations）→`needShift=k`。他族（c1/c3/群/回数）は null＝導線を出さない。
- **ConfirmRow**: 末尾に「設定で直す」TextButton（行本体タップの勤務表/直し方導線とは別アクション）。`when` はローカル val
  （ws/ns）でラムダ内スマートキャストを安全化。
- **配線**（MagiApp）: `deepLinkWishStaff`/`deepLinkNeedShift`（rememberSaveable Int・-1=無し）を新設。ConfirmListCard の
  `onFixWish`/`onFixNeed` が該当値をセット＋`editScope=0`＋`tab=2`。`WishCard(initialStaff, onInitialConsumed)`/
  `NeedCalendarCard(initialShift, onInitialConsumed)` に事前選択パラメータを追加し、`LaunchedEffect(initial)` で内部 `i`/`k` を
  該当職員/シフトへ設定して消費（-1 へ戻す）。自動スクロールは無し（ユーザー選択どおり）。
- 検証: ブレース均衡・呼び出し側シグネチャ一致（新パラメータは全て default 付き＝既存呼出非破壊）・重複定義0を静的確認、
  最終判定は CI（Release Build）。

## HARD残でもSOFTをRSI focusできるようにする（3.183.0, 実データ検証で根本特定）
ユーザー報告「再最適化しても人員不足のまま／RSIでaptを最適化していない／回数制約は大丈夫か」を、**実機state
（10職員/31日/2026-07, /tmp/us.json）を Python で忠実検証**して根本特定。
- **covU=2 の正体（実データ確定）**: 7/11 Cｵ・7/17 B4。日単位ピジョンホール（Σneed=6 < 10人）は成立せず＝
  日単位では余裕あり。真因は**希望固定＋禁止連続で可動候補が実質いない**: 7/17 B4 は全必要シフトがちょうど1人
  （余剰0）＋空きは有(佐藤)/休(古泉・金沢)が全員**希望固定**→動かすと pref(9000)>covU(8000)で悪化＝最適化器は
  埋めないのが正しい。7/11 Cｵ は古泉が休だが7/10=Dﾃで Cｵ にすると「Dﾃ-Cｵ」禁止連続。**＝再最適化では埋まらない
  ／診断「充足可能」は過度に楽観的**（`diagnoseCoverage` は capacity≥need だけで判定）。
- **apt/SOFT飢餓の根本**: `structuralHardFloor`(=forcedCovU)は**シフト単位の担当可能数<need しか見ない**ため、この
  covU=2（担当可能8≥need1）に対し **0** を返す→ covU が RSI の `avoid`(L738 `covU<=covUFloor`)に入らない→
  `maxViolatedFamily` が毎R "covU"(HARD優先)を返し続け SOFT第2ループ(apt等)に到達しない→ HF63が~3R後に検知しても
  **N4早期終了(L787)でRSIごと停止**。**apt固有でなく、埋まらないHARDが全SOFT/回数制約(low/high/c2・c41系・covO・
  weekly・fair・c1・c3系)を道連れに飢餓させていた**。
- **修正（ユーザー指示）**: N4早期終了を「停滞HARDを deprioritize してもなお狙える族が残るなら早期終了せず
  SOFTへピボット継続」へ変更（L787）。`maxViolatedFamily(bestReport, avoid)` が実族(件数>0)を返す間は break せず、
  focus は L741 の focusAvoid で既に SOFT へピボット済のため残ラウンドを SOFT 最適化に使う。**keep-best(better()は
  hard非悪化を要求)がHARD悪化を防ぐ＝HARD残のままSOFT最適化しても安全**。stuck な SOFT も HF63 が順次
  dynamicAvoid へ入れて focusable から外すため、pivot 枯渇(=="total"/件数0)でいずれ自己終了。focus選択/終了条件
  のみ＝スコアリング不変。nsp_bench は RSI focus を模擬不能のため原理採否(3.74.0/3.169.0 と同方針)。
- **(3.184.0, 実機ログで判明した第2の穴=HF63のSOFT誤deprioritize)**: 実機ログ（RSI_PLUS 300s）で round1-3 focus=covU
  不変→round4 で **HF63 が c1,c3n,c3m,c3mn,c42,covU,covO,low,high の9族を deprioritize**→focus=weekly→早期終了、を確認。
  `Hf63Infeasibility.update` は breakdown 値が不減なら stall 計上するため、**covU に focus が張り付いて一度も focus
  されなかった SOFT 族(c1=87/low=10/high=4 等=本来直せる)まで infeasible 誤判定**していた。3.183.0 の pivot だけでは
  残るのが weekly/fair(destroyRepairStaff が cost 未対応で効かない)だけになり不十分。**修正: focus の avoid を真に
  構造的な HARD(covU床/c3n/pref/groupViol)のみに限定**（`avoid = dynamicAvoid.filterTo{ it in MirrorKeys.hard }`）。
  SOFT は常に focusable に保ち、HF63 が covU 等 HARD を flag した時点で focus が c1/low/high 等の直せる SOFT へ自動
  ピボットする。SOFT の同一 focus 空転は cooldownFocus(1R休止)＋keep-best＋有限ラウンドで自己収束。N4 武装判定は
  従来どおり dynamicAvoid(全族)、pivot 可否は avoid(HARD) で判定。focus選択のみ＝スコアリング不変。
- **(3.185.0, apt目標の「+/-で数字が変わらない」実機バグ修正)**: ユーザーが「通常画面でも +/- で数字が変わらない」と
  確認＝コードバグ確定。`ws1SetGroupApt`→`applyStructure(MagiState)` は `_ui.update{copy(structureEdited=true)}` を
  呼ぶが、**structureEdited が既 true だと copy が同値で StateFlow が emit せず**、かつ **currentSchedule=null 時は
  refreshCheck も早期return**するため、`AptCard`（`vm.ws1()`=state 直読み、ui 変化でしか再構成しない）が再構成されず、
  state は更新されるのに表示が変わらなかった。**修正: UiState に `editRev:Int` を追加し applyStructure が毎回
  `editRev+1` で必ず distinct な UiState を emit＝確実に再構成**。両 applyStructure(MagiState/Ws1Result)に適用。
  additive フィールド(既定0)・スコアリング不変・テスト非依存（golden/Session は state/report を検証）。
- **未（別課題）**: ①`diagnoseCoverage`の「充足可能」honest化（希望固定/終端余剰を検証）③~~**[実機ログ]ネイティブ探索が実データで無効化**（`NativeBridge:
  SAチャンク整合性NG status=1`＝評価器パリティは一致(hard=3 soft=1628)だが C++ SaChunk の自己整合が実データで失敗→
  番兵発火で Kotlin 退化＝正しいが遅い。合成 harness では出ない実データ固有の乖離。要・当該 state での SaChunk 差分追跡）~~
  **→ 3.199.0 で根本修正**（-1セル=normalizeSchedule の正規化結果に対する deltaApply の範囲外書込＝カウント/ヒープ破壊。
  詳細は「ネイティブSAチャンクの-1セル未対応」セクション。実機での番兵不発の最終確認は次回実機ログ）。

## バックログ / 未対応
1. ~~TallyCard の読取/編集モード完全整合（result専用検査結果の plumbing）~~ **→ 3.96.0 で完了**（ユーザー向け機能の TallyCard 項参照）。
2. 未レビュー領域の精読: `V6LateOperators`/`V6SearchOperators`/`V6HotfixPasses` 各パス内部, CSV/UI 層。
   **(3.84.0, 並列監査で一巡・下記参照)**。※`V6WebCompat` は 3.393.0 に撤去済み（Web 版は存在しない）。
3. ~~C++/NDK 移植は**不要**の結論（純Kotlin＋被覆対応Δ評価で十分高速）~~ **→ 撤回（3.136〜／第2期・第3期でネイティブ加速＝
   C++フル評価器＋SA/LAHC/ALNS/Polishチャンク＋JNI＋実行時パリティを実装。監査指摘は下記6/7）**。エンジンは ALNS/Destroy-Repair/
   ChainSwap3-4/C1BlockN/PathRelink/LNS/Reheat/Oscillation/適応的オペレータ重み/希望ロック枝刈り を実装済み。
   §4 ILP matheuristic のみ意図的に未実装。
4. cons3n のデータ重複（Dﾃ→A4 が2行）は二重計上だが最適化器/チェッカーで一貫（SettingIssue が dedup を提案）。
5. **E5「月全体の俯瞰」= ユーザーの明示 go まで保留**（決定記録）。指数(見やすさ12指標)で唯一70未満(58)だが、
   最低スコア≠最高価値・片手一本指/編集主体との緊張のため、着手も再提案もしない（明示 go があった場合のみ）。
6. **[ネイティブ・保守性] C++評価器のパリティに自動テスト無し**（3.168.0系精読で判明）。JVM単体テストは arm64 `.so` を
   ロード不可（`NativeBridge.available=false`）→ Kotlin のみ検証。CI(Release Build/Android SDK)は CMake で `.so` を
   ビルドするので**C++コンパイルエラーは捕捉**するが**意味的乖離（重み取り違え等）は捕捉しない**。`Evaluator.kt`（や
   `MirrorCore`/`DeltaEvaluator`）を変えて `magi_native.cpp` を変え忘れると実機で番兵発火→**ネイティブ黙殺（速度退行・誤出力なし）**。
   3.171.0 で緩和策の一つ（ユーザーが明示的に照合を切れる「照合トグル」＋既定ONの維持）を実装。3.172.0 で
   `tools/native/host_parity_bench.cpp`（ホストビルド可能なパリティ+ベンチharness）を追加。
   **→ CI配線 完了（下記「ネイティブパリティのCI自動化」）**。`.github/workflows/native-parity.yml` が
   pull_request→main / push→main / 手動 で harness を g++ ビルド・実行し、mismatch>0 で非ゼロ終了＝ジョブ失敗。
   これで Evaluator.kt を変えて magi_native.cpp を変え忘れる意味的乖離が自動検出される。~~**残課題**: harness の
   合成問題は S<=64/T<=64・乱数生成で、実データ形状の網羅ではない（fixture 拡充は将来課題）~~
   **→ 3.357.0 で言語跨ぎパリティも追加**（旧: 照合は C++ scalar vs C++ bit-op ＝同じ言語どうしで、
   両方の C++ 経路を一貫して変えれば通ってしまった。`golden_eval_expected.txt` を Kotlin テストと
   `--expect=` の両側から固定し、片側だけの変更が必ず落ちるようにした）
   **→ 3.199.0 でフィクスチャ拡充**（`tools/native/state_to_flat.py`＝実 state JSON→flat 変換を新設し、
   CI が golden_state.json の実データ問題でも照合＋合成 builder に休シフト range/apt/c1/c2・実現不可能希望・
   -1/非canDo混入盤面を追加。この拡充が実バグ=SaChunk の -1 未対応を実際に捕捉した）。
   **→ 3.362.0 で2つ目の実データ形状 sample_v6 を追加**（golden は入力盤面 hard=0＝C++ の HARD族パスを
   実データで一度も exercise しないため、sample_state_v6 の hard=15 盤面を第2 fixture に。`--expect` を
   flat と出現順で対応づけ1回のベンチで両形状を言語跨ぎ照合。詳細は「パリティネットへ2つ目の…」節）。
   ~~**残課題**: 合成問題は S<=64/T<=64・乱数生成で、実データ形状の網羅ではない（fixture 拡充は将来課題）~~
   ~~残: real/user 相当の「構造的 covU が床超で blocked-now」な形状は依然 repo に無い~~
   **→ 3.409.15 で解消**（2026-08 実運用 state の**匿名化版** `blocked_covu_state.json`＝covU=4 が床0を超えて
   blocked-now、を第3フィクスチャとして追加。言語跨ぎ照合＋形状の回帰テストつき）。
7. ~~**[ネイティブ・堅牢性] 群index無検証のOOB（潜在）**（3.168.0系精読で判明）。探索オペレータ約13箇所が
   `p.bucket[p.sgrp[i]]`／`grpCnt[sgrp[i]*K+k]` を sgrp範囲未検証で使用しており、不正な groupIdx が渡ると
   C++側はUB（bucket=範囲外読み・grpCnt=範囲外**書込=ヒープ破壊**）でSIGSEGVし得た（Kotlin側は例外→
   runCatchingで安全退化するのと非対称）~~ **→ 3.171.0 で解消**（`nativeCreateProblem` に sgrp 一括範囲検証を
   追加し、外れていればハンドル生成自体を拒否=0返却。既存の「handle==0=native不可→Kotlinへ安全退化」という
   確立済みの契約にそのまま乗るため Kotlin 側の変更は不要）。
8. ~~**[軽微] SAチャンク自己整合の非対称**（3.168.0系精読で判明）。`runSaChunk` の番兵は `full != curVal` のみ。
   他3ランナー（LAHC/ALNS/Polish）は `curVal != st.score` の相互検査も持つ~~ **→ 3.179.0 で解消**
   （`runSaChunk` 末尾を `full != curVal || curVal != st.score` に対称化。受理時 curVal=st.score・revert で復元の
   ため通常は恒真＝挙動不変、不整合時のみ status=1 で Kotlin 退化。ホスト parity harness で compile+mismatch=0 確認）。

## 未レビュー領域の精読（3.84.0, 並列監査で一巡）
`V6HotfixPasses`/`V6SearchOperators`/`V6LateOperators`/`V6WebCompat`/`ScheduleCsvBridge` を並列エージェントで監査。
探索/後処理は heavily-audited で**深刻な正しさバグ・不変条件破壊は無し**（全 late-operator は同日置換で被覆保存、`wishLocked` は各 mutator でガード、
空データガードあり）。**修正済み(3.84.0)**:
- **CSV 往復バグ(live)** `ScheduleCsvBridge.parse`: `build` が出力する「空行＋集計ヘッダ＋職員名で始まる回数行」を終端せず再取込し
  `matched` 二重化（記号が数値なら勤務表破壊）→ 空行/「集計」で break。
- **予算ガード** `applyBlockRotationPolish`: O(cand³) の全候補フル評価に内側 `shouldStop` が無く締切後も走り切っていた(HF66=2.65.0と同クラス)
  → ai/bi ループ先頭に `shouldStop()` を追加しバーストを O(cand) 以内に。keep-best 保持=品質不変。
- **コメント整合(HF77)** `findTargetedFix` の「6種」→実装8種(covO/c2/low/c41/high/c41s/c3want/apt)に訂正 /
  `staffPacked` 前フィルタの「漏れなく」を訂正(apt/fair/weekly は非集計＝それらのみ改善する手はこぼす＝keep-best 安全)。
- (3.85.0) **色凡例の重大度逆転(live)** `V6WebCompat.severityFromVioKey`: low(90)/high(45)=最重 soft を INFO(灰)、covO(0.5)=最軽を
  WARN(橙) と逆転表示していた(凡例=`ColorSettingsView` は `MagiSetupCards` 詳細設定で live＝当初「死に画面」判定は誤り)。
  重み階層に整合(low/high/c3mn→HIGH, c1/c3/c3m/c2/c41/c42/c41s/c42s/apt→WARN, covO/fair/weekly→INFO)。表示のみ・スコア不変。

**報告のみ(未修正=判断/測定待ち)**:
- ~~`applyDayAssignmentPolish` の rangePen 重み 3/3・apt 1 は Evaluator の 90/45/1 と乖離~~ **→ 3.94.0 で 90/45/1 へ整合(下記)**。
- ~~`staffPacked`/`c3FamCount` が c3/c3m を run-deficit でなく窓#fire でモデル化(前フィルタ限定・keep-best 安全)。~~
  **→ 3.349.1 で実測して閉じた**（捨てた候補 golden 235/user 899/real 896 に対し「checker なら採用」は0件＝inert）。
- ~~平準化研磨(`applyGroupShiftEqualizePolish`/`applyWeeklyEqualizePolish`)は分散指標で目的関数(fair/weekly=L1)と別物＝既知の冗長~~
  **→ 3.317.0 で撤去**（実データ3件で採用0回・分散指標も不動・ablation で最終盤面一致＝寄与ゼロを実測。
  L1 ベースの後継が役割を代替）。
  ~~`weekly` の `restIdx=-1`(休記号改名時) で全シフトを勤務扱いする潜在バグ・`dow0` 再計算~~ **→ 3.103.0 で修正**
  (restIdx/dow0 とも Problem と同一ソースへ統一)。
- **デッドコード**: ~~`V6RemainingScreens`(未描画・外部参照0)＋そこからのみ実呼出の `HeaderBar`/`RingGauge`/`BottomNav`/`FlagsView`/
  `OverviewDashboard`/`OperatorLogView`~~ **→ 3.86.0 で撤去済**(外部参照0を再確認。live な `CheckSummaryView`/`ColorSettingsView` と
  それらが使う `SectionSegment` のみ残置)。**→ 3.87.0 で `V6WebCompat` のスコアベクタ死蔵クラスタも撤去**
  (`classifyHardBreakdown`/`HardBreakdown`/`scoreVecStable`/`betterVec`/`firstDiffTier`/`ScoreVector`=呼出0)。
  `buildWorkbook`/`buildWs2-7` は当時 `V6WebCompatTest` がカバー中のため残置していたが、**3.393.0 で `V6WebCompat` ごと撤去**（ユーザー確認「Web版は存在しないので web互換性は不要」）。
- ~~`ScheduleCsvBridge` 各コンポーネント取込の `drop(1)` ヘッダ無検証(ヘッダ無CSVで先頭行黙殺=軽微)~~ **→ 3.103.0 で修正**
  (4サイトとも先頭行が実データ=既知の職員名/制約キーワードなら取り込む。upsert は新規追加の誤登録を防ぐため既知名のみ=保守的)。

## 直近の状態
versionName=`2.41.0-bound-check`（versionCode 48）。目的関数統一は covO/range/c3族(単一+複数連)/c1/apt/fair まで完了。
診断ログに**上下チェック(全シフト網羅)**を追加（`V6SanityPort.buildViolationDebug`）。下限/上限(staffRange)が設定された
全シフトについて個人別の下限割れ(low)/上限超過(high)を担当者ぶん洗い出し、違反0なら「上下OK」も表示（判定は
UnifiedViolationChecker と一致: low=lo!=0&&canDo&&回数<lo / high=回数>hi）。例「上下注意 Dﾃ: 下限割れ0名 / 上限超過1名(福澤 6>5)」。
診断ログに**需給サマリ**を追加（`V6SanityPort.buildViolationDebug` 冒頭）。シフトごとに 需要 / 担当者数 / 個人下限・上限計 /
適切回数(クランプ後)計 / 現状配置 を対比し、過剰(covO)・不足(covU)の要因を1行で示す。下限/上限/適切回数の「計」は
**設定済み職員のみの合計**で、設定者数を併記（例 `上限計10(2/8名)`）。実過不足は「現状 vs 需要」(covO/covU方向)で表示し、
構造的不足は**全担当者に上限がある場合のみ**判定（未設定者は無制限なので「上限計<需要」だけでは不足としない＝誤判定を修正）。
例: 「需給注意 Dﾃ: 需要31 … 現状33 → 現状33>需要31→過剰2(covO)」。読み取り専用。
apt は群目標を個人 staffRange[lo,hi] でクランプし、staffRange固定職員の解消不能な幻のapt違反を除去（golden_stateで
apt偏差 61→28 を確認、上條Dﾃ 2-2固定×群目標10 の誤った赤色が消える）。
fair(グループ内公平化)は群×担当ONシフトの round(平均) からの L1 偏差和として Evaluator/Delta/Checker の3者へ重み1で統合
（後処理polish からの格上げ）。UI内訳には非表示で weightedScore/total のみ算入。c41s/c42s の内訳表示漏れも修正。
Δ==フルは Python で確認済み（fair: within-bucket＋任意シフト計60,000手で mismatch 0／apt: 20,000手で 0）。
（3.72.0）**weekly(7日周期(曜日)シフト平準化)を目的関数へ統合**。職員ごと、勤務日(非休)の曜日別カウントの round(勤務日/7)
からの L1 偏差和として Evaluator/Delta/Checker の3者へ重み1で統合（fair と同型・後処理polish `applyWeeklyEqualizePolish`
からの格上げ、polish は keep-best/mainNotWorse で併存＝無害）。UI内訳は fair 同様「曜日の偏り」チップに件数表示（場所
マップ無し）。共通ソース=`weeklyDevOfBucket(wd[7])`。`Problem` に `restIdx`
（休index）と `dow0`（startDate曜日オフセット%7、weekday(j)=(dow0+j)%7）を追加。Δは勤務/休 反転時のみ発火（DeltaEvaluator が
per-staff 曜日バケット `wdCnt[S][7]` を維持）。Δ==フルは Python で確認済み（80,000手 mismatch 0）＋ DeltaEvaluatorTest が
Kotlin側で full==delta を検証。Golden parity は soft total 非アサートのため不変。
次の自然な続きは c2/c41/c42 等の残り soft 族の重み統一（同じ原則で）か、未レビュー領域の精読。

## スキル / 作業の進め方
- 画面（`ui/` の Composable）を実装・改修したら **design-review**（`.claude/skills/design-review/SKILL.md`）で
  規約からの逸脱をレビューする。**`/code-review`（正しさのバグ）とは対象も根拠も違う**＝こちらは
  `docs/DESIGN.md` / `operator_ux.md` / `screen_spec.md` / `ux_test_checklist.md` / CLAUDE.md の D決定 と
  `tools/design_lint.py` を根拠に、形・文言・トークン・アクセシビリティ・冗長を見る。severity は
  MUST/SHOULD/IMO/nits/Q で、**根拠を示せないものは MUST/SHOULD にしない**。D決定(D3〜D8/E5)と HF77 に
  触れる指摘は severity を付けず `Q` へ落とす。エンジン・重み・採否は対象外。
  出典は dachi023 氏の gist（Figma+Notion+Storybook 前提）で、この repo にはその3点が無いため
  **根拠の層をリポジトリ内文書と design_lint へ置き換えて移植**した（design-context は Figma/Notion 取得が
  本体で、無いと grilling の劣化版になるため移植しない）。
- 非自明な変更・新機能・仕様判断に着手する前は **grilling**（`.claude/skills/grilling/SKILL.md`）で要件を壁打ちする。作る前に**一問ずつ**容赦なく質問し、各問に**推奨案＋一行根拠**を添え、コードで分かることは**調べてから**聞く。認識が一致したら3〜5行に要約してから着手。「grill」「詰める」「壁打ち」の合図でも発動。MAGI 固有の必須観点（対象実装 / 変更の種類とHF77 / Level Zero不変条件 / 制約系の特定 / 完了条件）はスキル本文を参照。

- (3.145.0, シンプルデザイン融合①=シフト集計の凡例): 実機スクショのシンプルな集計画面を融合する第1歩。
  TallyLegend を **1行凡例「▼不足 ▲超過 — 対象外」** に統一（旧: 呼出側が「回数が下限/目標未満」等の
  長文ラベルを渡していた4引数版）。タブ名(職員別/日別)が不足/超過の意味を文脈で示すため長文を撤去し、
  詳細はセルタップで出す方針。色見本＋▼▲＝色覚二重符号化は維持。呼出2箇所(職員別/日別)を2引数へ。
  表示のみ・スコアリング不変。**未対応(次段候補)**: 集計期間の読取ラベル・操作ヒントのアイコン化
  （期間バーの<>ナビは月スナップショットモデルと衝突するため read-only ラベルに留める想定）。
- (3.146.0, シンプルデザイン融合②=集計期間ラベル＋操作ヒントのアイコン化): TallyCard に read-only の
  **「集計期間 YYYY年M月D日(曜)〜…」** ラベルを追加（startDate〜startDate+(days-1)・曜日付き・パース失敗時は非表示）。
  スクショの期間バーの <> ナビは月スナップショットモデル（集計は常に現在の全期間）と衝突するため付けず、
  読取ラベルに留める。操作ヒントをアイコン化: 職員別「ⓘ タップで内訳と直し方」／日別「ⓘ タップで内訳と直し方
  ・👆 左右スワイプで他の日」。表示のみ・スコアリング不変。
- (3.147.0, 設定ミス案内から VBA ワークシート符号(wsN)を除去): 実機に出る設定ミス/診断ガイダンスの文言に
  VBA 由来の内部符号「(ws1)〜(ws5)」が22文字列・29箇所で露出していた（例「個人下限を下げる(ws1)か、必要人数を
  増やしてください(ws2)」）。各文はすでに平文で対象を名指し（設定・必要人数・希望・連続パターン設定・個人上下限・
  担当範囲）しているため、符号は冗長な相互参照＝**削除で誤誘導リスクゼロ**。「(ws1/担当範囲)」のみ意味語を残し
  「(担当範囲)」に。対象=V6SanityPort/V6HotfixPasses/MagiDashboardCards/StaffRangeEditor の**文字列リテラルのみ**
  （コメント内の wsN=内部参照は不変。テスト依存なし=grep 確認済み）。paren balance は HEAD と同一（符号は括弧対で
  除去＝中立）。文字列のみ・スコアリング不変。operator_ux「英字符号を画面に出さない」に整合。
- (3.148.0, covO 重みを 0.5→1.0 に統一＝目的関数の最後の乖離を解消): 実機ログ(83cc183a, 12シフト)の算術照合で
  **評価器 soft=1912 vs チェッカー weightedScore 再構成=1903（差9=covO×(1.0−0.5)）**を発見し、コード照合で確定:
  最適化器(`Evaluator.kt:155`/`DeltaEvaluator.kt:87`/`magi_native.cpp` fullEval＋slice)は covO を **amount×1.0** で加算
  していたのに、チェッカー(`MirrorCore.kt:67` weightedScore)だけ **×0.5**。`Evaluator.kt:11` のコメント「covO は checker と
  同重み」が実装(×1.0)と矛盾＝HF77 の「コメント≠実装」を捕捉。原因は soft が Long で 0.5 を表現できず 2.28.0 統一時に
  整数×1.0 で入ったまま残存。keep-best `better()`(`V6NativeOptimizer:1269`)は既にチェッカー(0.5)基準＝**最終選択は正しく
  ランク済**で、ズレは SA 探索コンパスが最下位族を2倍罰する内部のみ＝実効微小。**修正方針(ユーザー選択)**: 真0.5化は
  スコア全体×2＋SA温度/GLS lambda/Lam スケール等の**番兵の無い動学定数**を全連動させる大改修（native 再検証必須・
  取り漏らしで静かに劣化）になるため回避し、**「最適化器を正」として チェッカーを 1.0 に統一**（`MirrorCore:67` の1行、
  最適化器/Delta/C++ は既に 1.0＝変更不要）。これで weightedScore が評価器と一致(1903→1912)し「族寄与==weightedScore寄与」
  の不変条件が復活。副次: `V6WebCompat.severityFromVioKey` の covO を **INFO→WARN**（重み1.0=c2/c41 と同格の実違反、
  集計/グリッドで既に橙=過剰表示のため整合。fair/weekly は「整え・常時非ゼロ」で INFO 維持＝3.85.0 の重大度=重み整合原則）。
  covO の内訳チップ件数(amount)は不変、weightedScore と重大度色のみ変化。C++/native は変更なし＝parity 影響なし。
- (3.149.0, 曜日の偏り/公平化の「場所」表示): 実機指摘「曜日の偏りなどが表示されません」。原因=fair/weekly は
  セル単位でなく**職員(weekly)・群×シフト(fair)単位の偏り**のためチェッカーが `inc()`(件数)のみで `mark()`(セル印)を
  呼ばず、`breakdownLocations` の violationCells 検索が常に0箇所＝「場所情報がありません」だった（かつ意図的に
  グリッドへは出していない＝62セルを枠表示すると 3.99.0 の飽和が再発）。**チェッカー(source of truth)が職員単位の偏り箇所を
  出力する方式**で解決（UI 再計算は restIdx/群/bucket が UiState に無く covO と同型のドリフトを生むため不採用）:
  `ViolationReport.distLocations`("weekly"→[[i,dev]] / "fair"→[[i,k,dev]]、dev降順) を新設し fair/weekly ループで
  偏り職員を収集→UiState→`breakdownLocations` が「職員（曜日の偏り N）」「職員 「シフト」（偏り N）」で整形・タップで
  当該職員へフォーカス。**内訳パネルのみに表示（グリッド不変＝飽和回避）・スコアリング不変**。配線=MirrorCore→
  ViolationReport→UiState→makeUi→breakdownLocations（表示専用フィールド追加、既存構築は全て named 引数＋デフォルトで非破壊）。

## 所有権を失った実行が新しい実行を恒久的に無効化していた（3.388.0, /code-review 11件の検証）

`/code-review` の11件を1件ずつ実コードへ当て、**9件が実在**と確認して修正した。1件目は直前に main へ
入れた Critical で、しかも 3.385.0 で私が作った欠陥。

- **[Critical・実バグ] 所有権を失っても公開と片付けへ流れていた**: 3.385.0 で `commitGuard` を足したとき
  「保存はスキップするが、その後の**公開・通知・片付け**はスキップしない」形にしていた。結果:
  ①A の古い結果を `publishResult` で UI へ流す（入力が同じなら `applyBgResult` の指紋照合も通る）
  ②`runIdFile` を消す＝`owns(mine) = mine==0L || activeRunId()==mine` なので以後 `activeRunId()` が 0 に
  なり **新しい所有者 B は二度と所有者になれない**（保存も公開も永久に不能）③`inputFile`/`snapshotFile` も
  消えて B の kill 復旧手段まで失われ、`releasedByMe=true` で `setRunning(false)`＝B の計算中に編集ガードが
  開く。**3.327.0 が防ごうとした被害そのもの**。所有権を失っていたら以降いっさい触らない形へ。
  なお「書き込みが例外で落ちた（所有権はある）」場合は従来どおり公開・片付けする＝2つを区別する。
- **[実バグ・指摘より広い] 計測が最後の pass で上書きされていた**: `handleOptimize` は AUTO の 31〜210秒帯で
  `optimize()` を**最大3回**呼ぶ（RSI → ALNS → ExtraRefine）のに、リセットが `optimize()` の入口にあった。
  **同じ欠陥が `TuningTelemetry` にもあり**、`設定の効き` 行も最後の pass しか報告していなかった
  （3.356.0 以来・レビューはこちらに言及していない）。`beginTelemetry()` を新設し `handleOptimize` 入口で
  1回だけ落とす。「0 なら理論上の窓」と読ませる行がまさに false negative になっていた（3.102.1 と同型）。
- **[実バグ] 件数表示が構造的に到達不能**: 所有権の喪失は**単調**（`beginRun` を書くのは新しい実行だけ・
  `clear` はマーカーを消すだけ）なので `droppedProgress > 0` の実行は必ず所有権を失っており、
  それを「成功かつ所有」分岐だけで出していた＝**一度も表示されない**。`terminal()` 側で全出口に付ける。
- **[実バグ] 失敗の終端ログが誤帰属**: `notify()` が先で、その `NotificationCompat.Builder(...).build()` は
  `runCatching` で包まれていない。ここが投げると catch を抜けて `finally` のフォールバックへ落ち、
  **本当の原因が残らないまま**「想定外の経路」と記録されていた。終端ログを通知より先へ。
- **[実バグ] 正常系まで `[W]`**: 3.387.0 で Worker のライフサイクル全体を `notes` へ流したのに、消費側が
  `logOp("W", it)` 固定のままだった。このリポジトリの診断は「まず [W] を拾う」読み方が定着している
  （SanityCheck・CoverageDiag・設定ミス・NativeBridge が全て W）ので正常系を混ぜると壊れる。
  `notes` を `Pair<level, msg>` にし、正常系は I・異常系と TOCTOU 発火は W へ。
- **[過大主張の是正]** 競合カウンタは **CAS の再試行回数**＝「同時に publish した回数」であって、
  3.385.0 が直した特定の交錯（CAS に勝った側が盤面コピー中に止まる）そのものではない。
  **非ゼロは必要条件であって十分条件ではない**と書き直した（3.324.0/3.263.0 の規律）。
- **[その他]** `resetLiveBestForTest` が競合カウンタを落としていなかった非対称／`contentionLog` が
  3.378.0 の「スコア収支」コメント块と `ledgerLog` の間に割り込んで**孤児コメント**を作っていた（3.271.0 と同型）。
- **[移さないと判断] `liveBestContention` を `RunSlot` へ**: 指摘は妥当（3.335.0 が同型の問題を解いた）だが、
  診断専用かつ 3.360.1 が `TuningTelemetry` について同じ限界を既に記録している。移すなら両方まとめてやる
  べきで、それは別の判断。記録に留める。
- 検証: ホストJVM **489テスト green**。UI/Worker 層はホストでコンパイル不可＝括弧均衡と
  `publishNote` 全呼出のシグネチャ一致を静的確認。最終判定は CI。

## トグル A/B の第2ラウンド＝240s で3件とも測り切った（3.409.25, docs のみ）

ユーザー指示「PORTFOLIOロール内並列SA / 崩し範囲 / 立て直し方 を仮想環境で加速度検証」への回答。
**3件とも 240s（実機 AUTO→PORTFOLIO 帯）で測り終えた**。判定基準は測定前に固定したものを1つも変えていない。
コードは1行も変えていない（記録のみ＝`docs/algorithm_portfolio.md`）。

| トグル | 第2ラウンド(240s) | 通算 | 判定 |
|---|---|---|---|
| `adaptiveEscapeControl`（立て直し方） | 9ペア **ON2/OFF7** | 24ペア **ON7/OFF17・符号検定 p≈0.032** | **OFF が有意に良い**＝3.409.21 の削除は実測に支持されていた |
| `portfolioRoleParallelSa`（ロール内並列SA） | 6ペア **ON3/OFF3** | 21ペア 中立 | 事前バー「ON 5/6」に届かず＝**削除確定** |
| `wideC3nBreakDays`（崩し範囲） | 9ペア **ON5/OFF4** | 中立 | 事前ルールどおり**既定OFF据え置き** |

**escape は「120s では機構が起動していなかったのでは」を潰すために 240s で測り直した**（中央 wall は
OFF 217s / ON 236s＝この帯では確かに走っている）。結果は中立どころか OFF 優位で、削除は消極的な選択ではなかった。

**[自分の第1ラウンドの一般化を訂正] 並列SA の「反復希釈」は 240s では再現しない**。3.409.21 に
「ON は反復数中央値が 2/3 データセットで低い＝チェーン分割は希釈」と書いたが、240s で測ると
ON の反復中央値は golden 216M vs OFF 195M・sample 114M vs 86M で**高く**、blocked だけ 176M vs 186M で低い
＝符号が割れる。削除の根拠は希釈ではなく**2ラウンド計21ペアの中立**の方に置き直した。

**wide の条件は満たしていないと正直に記録する**: 事前ルールは「4件目以降の実データで再測定」だが、
5件目（実機 2026-09）の state JSON が手元に無いため、この9ペアは**既存3データセットのまま**。
実稼働の確認（実機ログで既定と違う範囲を 45,723/45,888 回探索）は済んでいるが、単体の優劣を
別データで検証したことにはならない。

### 測定インフラで踏んだこと（次に同じ時間を溶かさないため）
`setsid nohup` で流した A/B は**2度とも途中で消えた**（1回目は37 run 目、2回目は2 run 目で止まり5時間空転）。
セッションのアイドル中に detached プロセスが回収されているとみられる。**長い測定はハーネスが追跡する
背景タスクとして流す**（完了時に通知が来る＝空転に気づける）。今回の12 runs はその方式で30分弱で完走した。

## 回避の並び(c3mn)と窓の要件(c1)の重みを 15→30（3.409.24, ユーザー明示数値指示＝HF77）

ユーザー指示「c3mnとC1を15から30に重みを変更します」。目的関数の変更なので**4面を同時に**動かした
（1面でも取り残すと探索とチェッカーが静かに乖離する）:
`MirrorKeys.weights`（c1/c3mn とも 30.0）／`Evaluator.fullEvalParts`（c1 の窓ごと `soft += 30L`・
c3mn の `* 30L`）／`DeltaEvaluator` の集約式2箇所（`sc1*30`/`sc3mn*30`・`dC1*30`/`dC3mn*30`）／
`magi_native.cpp` 5箇所（`fullEvalParts` の c1 と c3mn・`SaChunk::contribC1Row` の bit と scalar の両経路・
`contribC3RowFam` へ渡す c3mn の重み引数）。

**言語跨ぎ期待値3ファイルも同時に更新**（3.357.0 が「重みを変えるときは両方直してから期待値を書き換える」と
定めた手順そのもの）: golden `soft 3109→4999`／sample_v6 `825→930`／blocked_covu `1681→2731`。
差はいずれも `15 ×（c1+c3mn の fire 数）`＝126/7/70 件で、hard は3件とも不変。

**新しい階層**: low(90) > high(45) > **c1(30)=c3mn(30)** > c3(3) > c3m(2) > その他(1)。
c1 と c3mn は 3.253.0/3.249.0 以来ずっと同値なので、グリッドの表示強度（`heavySoftFamilies` に両方が
入っている＝3.409.11）と severity 分類は**変更なし**（c1 は最多件数のソフト族で飽和を避ける 3.367.0 の判断が
そのまま生きる）。CLAUDE.md の階層行は `c3mn(12) > c1(4)` のまま**2世代 stale だった**ので併せて是正し、
「重みを変えたら何を同じコミットで揃えるか」を行のすぐ下に明記した。

### 重み変更が既存のテストで露呈させた実バグ（C1広域ビーム）
`moreStepsNeverProduceAWorseResult`（3.340.0 の「ステップを増やすほど良くなる」保証）が落ちた。
原因は**重みではなく `applyC1BeamPolish` 側**: `bestEver` を目的関数だけで選び、厳密ピンの検査を
最後の1回にしか掛けていなかったため、**あとから来たピンを崩す候補が、先に見つけたピン安全な改善を
追い出して**いた（最終ゲートで root へ落ちる＝改善が丸ごと消える）。実測は golden で
maxSteps=8 が `0/4520.0/421` を返すのに 12 は root の `0/4999.0/437`。
`bestEver` の更新時に「root を改善するなら `blocksImproving` を通す」ゲートを足した。
root に勝てない候補は最終ゲートを通らないので、ピン判定は root 改善時だけ行う＝余計なコストも
attribution の水増しも無い。**保持する候補が増える方向にしか動かないので退化不能**。

- 検証: ホストJVM **512テスト green**（重み変更前は上記1件＋期待値3件の計4件が落ちた＝
  この4件がこの変更の見張り番として実際に機能している）。native parity 4,794,967手 mismatch=0・
  3フィクスチャとも新しい値で言語跨ぎ MATCH・bit-op ×2.16。design_lint 0。
- 実データでの効き（勤務表がどう変わるか）は次回の実機ログと後処理ベンチで確認する。
  重みを上げた以上 c1/c3mn は減り、その代わり c3/c3m/c2/apt 等の軽い族が増える方向に動くのが設計どおりの帰結。

## 並列監査の残り2件＝制約 index の入口ガードと「1日あたり上限」の前提（3.409.23）

3.409.22 と同じ並列監査が確定させた、修正済み5件の**外**にある2件を片付けた。

- **[SANITY-5・certain] c1 壁判定の非休側が「上限未設定＝上限0」と読んでいた**: 未設定の日は
  `covOCell` が恒常0＝**covO が構造的に発火しない**＝「1日あたり上限」という前提そのものが無い。
  旧実装は -1 を `coerceAtLeast(0)` で 0 に潰して合算していたため、一部の日だけ need を設定した
  シフトで不足量が過大に出た（監査の実測: 6日中 day0 のみ need1=1 → **「7回ぶんの過剰配置が要ります」**。
  実際に上限があるのは1日だけ）。しかも助言が指す罰がそのシフトには存在しないので、従っても何も変わらない。
  **上限が全日そろっているときだけ案内する**（`capKnown`）。3.364.0 の文言方針（壁へ格上げしない）は不変。
  read-only の診断のみ＝勤務表・スコアには一切影響しない。
- **[G3・likely] `contribDayGroups` のビット経路が制約 index を無検査**: `grpMask[(size_t)c.g]`（負値→
  巨大 size_t）と `dayShiftMask[j*K + c.s]`（`c.s>=K` は隣日の行・`j=T-1` で末尾越え）を無検証で引く。
  兄弟の `contribC3RowFam` は seq を `[0,K)` 検査してスカラーへ退避するのに、c41/c42/c41s/c42s だけ非対称
  だった。**天井は SIGSEGV で2層番兵では捕捉できない**（3.171.0 の監査#7 と同クラス）。正規の flatten では
  到達しないが、`nativeCreateProblem` の入口で一括拒否すればホットパスに分岐を足さずに閉じられる
  （0=native 不可→Kotlin へ安全退化＝sgrp/ssk と同じ契約）。判定は **`consIndicesValidN` として JNI の外**へ
  置く（JNI 部は `MAGI_HOST_TEST` で除外されるので、中に書くと harness から叩けない＝テスト不能になる）。
  **群 id に上限は課さない**: `buildGroupMasks` が cons の参照群 id からもベクタ長を決めるので非負なら
  構造的に安全で、上限を課すと「職員数より多いスキル群」を持つ正当なデータを誤って拒否する。

**あわせて 3.409.22 の自分の記述を訂正**: canDo ガードを「候補選択を歪める」と書いたのは過大で、
**現行の呼出3箇所すべてで不活性**（Day/Staff は候補ループ入口で `!cd(i,k) continue` 済み、Violations は
`allowed`＝canDo で、唯一 canDo 外を取り得る `dOld` は k に依存しない定数オフセット＝argmin を動かせない）。
価値は Kotlin ミラーとしての一貫性回復に限られる。

- 検証: ホストJVM **512テスト green**（新規1件＝上限が全日そろっていなければ案内しない・全日未設定でも出さない。
  全日そろって不足する既存ケースは従来どおり案内する回帰を同じテストで担保）。
  harness に `CONS index guard`（正当な大きい群 id は受け入れ、負の群 id と範囲外シフト id を4族それぞれで拒否）を追加。
  native parity 4,794,967手 mismatch=0・3フィクスチャ言語跨ぎ MATCH・bit-op ×2.16。

## ネイティブ修復器が Kotlin から5世代ぶん取り残されていた（3.409.22, 外部レビューを全件検証して修正）

受領したレビュー5項目を1件ずつ実コードへ当て、**全件が実在**すると確認して直した。根は1つ＝
**Kotlin 側で直した修復オペレータの規則が `magi_native.cpp` のミラーへ反映されていなかった**。
5世代ぶんが溜まっていた: 3.266.0(reservoir tie)・3.267.0(weekly/fair marginal)・3.319.0(canDo ガード)・
3.369.0(findCovOFix→covOCell)・3.379.0(destroyRepairDay/Staff→covUCell)。
**ネイティブは既定 ON なので、直したはずの規則が既定経路では効いていなかった。**

### なぜ番兵も CI も気づけなかったか（構造の話）
2層番兵も native-parity CI も**スコアの一致**しか見ない。修復オペレータは**候補生成**であって
評価器ではないので、両者の網から構造的に外れる。実機ログは `Kotlin照合=ON(4047回)` が全部通っており、
**照合が通っていることは候補生成が一致している証拠にならない**。
実害は「誤った勤務表」ではなく（採否は checker の keep-best が守る）**候補の取りこぼしと無駄打ち**。

### 直した5点（C++）
| 箇所 | 旧 | 新 |
|---|---|---|
| `destroyRepairDayAtN` | `need1<=0 → continue` / `miss = need1 - got` | `miss = p.covUCell(k,j,got)` |
| `destroyRepairStaffAtN` | `need1<=0 → continue` ＋ `got>=need1 → continue` | `p.covUCell(k,j,got) <= 0 → continue` |
| `findCovOFixN` | 過剰 `dsn - hi`(need1/need2 を自前で組む) / 代替先 `need1 - dsn` | `p.covOCell` / `p.covUCell` |
| `staffCountPenaltyAtN` | low を担当可否に関わらず数える | `&& p.cd(i,k)`（評価器 `contribRangeApt` は元から持っていた） |
| `destroyRepair*N` 3種 | 費用は個人回数(+Day のみ c41)だけ・同点は先頭固定 | `weeklyMarginalN`/`fairMarginalN` を加算・`reservoirTieN` で同点抽選 |

`weeklyMarginalN`/`fairMarginalN`/`reservoirTieN` を新設（Kotlin の同名関数と同式）。
**3兄弟の3番目 `destroyRepairViolationsN` は最初の修正で取り残しており、並列監査が拾った**
（同一ファイル内で2つだけ直っている非対称になっていた）。ここは Kotlin と同じく wd/counts/grpTotal を
repeat ごとに再構築する（件数が最大8回に限られるため）。

### 事前診断も同じ穴を持っていた（V6SanityPort・9箇所）
検査3「必要N人だが担当できるのはM人」が `need1` 直読みで、**need2 単独定義のセルを丸ごと見落として
いた**（利用者には「設定上は問題なし」と見え、実行すると covU が必ず残る）。しかも**同じファイルの
`forcedCovU` は元から `covUCell` を使っており、ファイル内で判定基準が二重化**していた。
`needDefined` / `effectiveDemand`(=`covUCell(k,j,0)`) / `effectiveCap`(=`covOCell` が 0 を返す最大 got) を
file-private で新設し、検査3・`impossibleDemandDays`（検査3の双子。**片方だけ直しかけて
`assert count==1` が落ちたことで存在に気づいた**）・検査6 の seatsLo/seatsHi/hasDemand・需給行の
demand と seatsHi・違反詳細の needStr・`aptBalances`・c1壁の workMinDemand と capSum の9箇所を委譲へ。
**レビューの「警告しない可能性がある」は過大**で、実際は検査7 `forcedCovU` が別途警告していた
（欠けていたのは per-day の**行動につながる** DEMAND 項目＝ワンタップ修正 `CAP_DEMAND` を運ぶほう）。

### 検証
- **native parity 4,794,967手 mismatch=0 / 3フィクスチャとも言語跨ぎ MATCH**（評価器は無傷）・
  bit-op ×2.13＝速度退行なし。修復器の新テストも `REPAIR need2-only: OK` /
  `MARGINAL cost parity: OK (checked=264 weekly非ゼロ=205 fair非ゼロ=206 再割当セル=320 候補選択=30局面)`。
- **ホストJVM 511テスト green**（新規2件＝need2 単独定義の需要が検査3に出る／収まっていれば出さない）。
- **harness に修復器の直接テストを2本追加**（パリティが評価器しか見ない以上、ここは自分で叩くしかない）:
  ①`need2` 単独定義の不足を `destroyRepairDayAtN` が埋め、過剰を `findCovOFixN` が見つける
  ②`weeklyMarginalN`/`fairMarginalN` が**全量再計算の差分と厳密に一致**し、作業配列を必ず復元する
  （両関数とも一時的に書き換えて戻す実装＝復元漏れは静かに順位を歪める）。
  **非ゼロの marginal が観測されなければ失敗にする**＝「何も測っていない緑」を防ぐ。
  あわせて `destroyRepairViolationsN` が希望固定セルを触らず担当可シフトだけを書くことも固定。
- **[自分のテストの誤りを実行で捕まえた]** 初版は `p.canDo` だけを弄って「担当外シフト」を作ったが、
  canDo は**群 bucket から導かれる**（flatten がそう詰める）ので、片方だけ弄ると実在しない問題になる。
  かつ repair は最大8セルしか触らないのに全セルを検査していたため、**初期盤面の担当外セルを
  「repair のせい」と報告**していた。bucket と canDo を揃えて作り、初期盤面も担当可だけで組み直した
  （harness を先に走らせていなければ、この誤りを本物のバグとして記録するところだった）。
- **教訓#30（両側で実行して確認）**: Kotlin は revert すると新テスト1件だけが落ちる。C++ は
  scratch へ2箇所（`covUCell` 委譲と `destroyRepairViolationsN` の weekly/fair）を戻して実行し、
  **`PARITY: 2,996,665手 0 mismatches` はそのまま通り、新テスト2本だけが落ちて exit 1**
  （`destroyRepairDayAtN が need2 単独定義の不足を埋めない (got=0)` ／
  `候補選択が weekly+fair の最小と一致しない (選んだ=1 期待=2)`）。
  **パリティが満点のまま落ちる**ことが、この乖離が番兵の外にあることの直接の証拠になっている。

### 実データでは潜在（正直な記録）
3フィクスチャ（golden / sample_v6 / blocked_covu）とも **need2 単独定義セルは0件**なので、
need 系の穴はこのデータでは発火しない。**いま効いているのは weekly/fair の費用と同点抽選の側**。
**[3.409.23 で訂正] canDo ガードは現行の呼出3箇所すべてで不活性**（Day/Staff は候補ループ入口で
`!cd(i,k) continue` 済み・Violations は allowed=canDo で、唯一 canDo 外を取り得る `dOld` は k に
依存しない定数オフセット＝argmin を動かせない）。当初「候補選択を歪める」と書いたのは過大で、
価値は Kotlin ミラーとしての一貫性回復に限られる。
need 系は「UI から1回の編集で作れる形」（`BaseNeedSheet` は最低人数の空欄を無ガードで保存できる）
に対する保険で、将来のデータで顕在化する。

## 既定OFFトグル2つを単体 A/B で測って削除（3.409.21, ユーザー選択「両方削除」）

3.384.0 の「見直しの条件」表が事前に約束した手順を実行した。対象は残っていた未判定トグル2つ:
`portfolioRoleParallelSa`（PORTFOLIO ロール内並列SA・3.371.0）と `adaptiveEscapeControl`
（停滞脱出の適応制御・3.306.0）。**測ってから、事前に固定した基準で、機構ごと削除**（規律7）。

- **測定の設計**（ホストJVM）: 1プロセス=1実行（static のクリーン化）・`requestedAlgorithm=PORTFOLIO`
  強制・**workers=4**＝ホストは4コアなので実機（8ワーカー/8コア）と同じ worker:コア=1 の比率を再現
  （parallelSa ON のときだけ 2倍の過剰予約になる、という実機と同じレジーム）。3データセット×各5ペア
  =トグルごと15ペア・ペア内で ON/OFF の実行順を交互化・**判定基準は測定前に固定**（weighted 12/15 で採否、
  符号検定 p≈0.035）。sample_v6 は実現不能希望9件で ImpossibleWishGate に弾かれるため
  `allowImpossible=true` が要る（最初の10 run が無言で全滅して発覚）。計60 run・約100分を
  `setsid nohup` で分離実行。
- **結果**: parallelSa = **ON7/OFF8＝中立**。しかも **ON は反復数中央値が 2/3 データセットで低い**
  （blocked 45M vs 57M・sample 53M vs 60M）＝チェーン分割は希釈にしかならない——このトグルの動機だった
  「ロール内1本=遊休」仮説そのものの反証。escape = **ON5/OFF10＝中立〜OFF寄り**（3.306.0 の n=24 と
  合わせ**2度目の中立**）。hard 中央値はどちらも全データセットで不変。正直な限界＝ホスト4コア/JIT vs
  実機8コア/ART（比率は合わせたが絶対条件は違う）。
- **削除の範囲**: `PolishGate` の3フィールド／`TuningTelemetry.escapeControlUsed`＋summary の立て直し方
  段／`portfolioRoleChainCount()`（roleWorkers は 1 固定へ）／epoch ループの制御器分岐
  （`controlledAssignment` 消滅で `assignment`・`lastRole` が単純化）／`StagnationEscapePressure`・
  `StagnationEscapeController`・役割指定の `assignmentFor(role, escapeDepth)` overload／UI の
  Switch 2行・`setAdaptiveEscape`・`setPortfolioRoleParallelSa`・UiState 2フィールド（73→71、
  `data-models.md` の件数照合も更新）／`StagnationEscapeControllerTest.kt` 丸ごと・
  `PolishRobustnessTest` の escape 参照・`HypothesisEpochPolicyTest` の overload 依存1件は
  既定経路 `assignmentFor(3,0)`（=HARD_DEBT_RSI_PLUS を role assert つき）へ書き換え。
  **温存**: `carriesImprovingQuantum`（既定経路の量子契約）・`initialAssignmentFor`（既定経路の
  初期配置＝V6NativeOptimizer:779 が使用）・`intensityFor`/`shouldReassign`/`nextStagnantEpochs`。
- **docs**: `algorithm_portfolio.md` は2トグルを「廃止・統合済み」へ移し（A/B の数値・希釈の反証・
  比率合わせの設計まで記録）、「見直しの条件」表・「どこを見れば」から削除、
  `StagnationEscapeController` の決定表セクション（~40行）を削除、「役割が変わった直後の量子」は
  既定経路の生きた契約なので適応ポートフォリオ章へ移動して文言を単数経路へ是正。
- 検証: ホストJVM 全テスト green（escape テスト撤去ぶん減）・design_lint 0。探索の既定経路は
  1ビットも変えていない（roleWorkers は既定 OFF 時も常に 1 だった＝削除後も 1 固定で同一）。

## C41/C42/C41s/C42s の説明を別々の制約として書き分け（3.409.20, ユーザー指示）

ユーザー指示「C41,C42,C41s,C42sは別々の制約です。別々の説明も明確に分かるように記載する」。
旧説明は c41↔c41s・c42↔c42s が語だけ差し替えたほぼ写しで、**何が違うのか**（どの分類で数えるか）が
読めなかった。4本を書き分け（**表示のみ・スコア不変**）:
- 各説明の冒頭に【群のレンジ】【群ペア禁止】【スキル群のレンジ】【スキル群ペア禁止】の名札。
- **どの分類で数えるか**を明文化: cons41/42=基本情報の「グループ」（担当シフトを決める所属・engine の sgrp）
  ／cons41s/42s=「スキルグループ」（担当とは独立の第2分類・1人1スキル・engine の ssk）。
- **兄弟制約を名指しして「別の制約」と明言**: 「スキル群のレンジとは別の制約＝こちらは勤務グループの
  所属だけを見て、スキルの割当は見ません」等の対の一文を4本すべてへ。
- `groupAndSkillFamiliesAreExplicitlyDistinguished` テストで固定（写しへ戻ると落ちる形＝
  ①分類の明文 ②兄弟の名指し＋「別の制約」③スキル側は「独立」の語）。
- 検証: `design_lint` exit=0・ホストJVM **517テスト green**（516 + 新規1）。

## /design-review＝3.409.18 の自分の変更に SHOULD 1件（3.409.19）

3.409.18 の UI 変更へ design-review スキルを適用。機械検査は全て0。指摘は SHOULD 1件のみ＝
**自分が入れた読み下し文の 11sp がアプリ唯一の最小値**で、`MagiTheme` labelSmall のコメント
「11sp(Material既定)→13→14sp へ継続底上げ」（判読性の決定）に逆行していた。周辺説明文の最小=12sp へ
合わせ、階層は色（onSurfaceVariant）だけで示す形に是正。IMO 1件（レンジ側見出しに「できるだけ守る」が
無い非対称＝ペア禁止だけ明示ソフト化した対比でレンジが必須に見えうる。ただし全ソフト族見出しへ付けると
3.129.0 の冗長性方針と衝突するため提示のみ・据え置き）。

## ペア禁止の説明を人間に分かる形へ＝聞き返された3点を修正（3.409.18, ユーザー選択で4件全部）

ユーザー質問「説明は人間に分かりやすいか」への監査。**根拠はこの会話そのもの**＝この画面について
「条件の意味は？」「グループ分けは正しいか？」と2回聞き返された（3.396.0 の原則「聞き返された時点で
そのUIは失敗」に照らして不合格）。AskUserQuestion で4件全部の実施を選択。**表示・文言のみ＝スコア不変**。
- **[① 「禁止/不可」の正直化]** ペア禁止（c42/c42s）は**最軽量のソフト条件**（重み1・実機ログで
  休×休ペアが2件残った）なのに、見出し「群ペア禁止（同じ日に不可）」は絶対に起きないと読める＝
  3.405.0「形が守れない約束をしない」の言葉版。見出しへ「・できるだけ守る」を明示
  （`constraintFamilies`/`skillConstraintFamilies` のタイトル＋SkillConstraintsCard の説明文）。
- **[② 向きの説明]** ⓘ詳しい説明（cons42/cons42s）へ「違うシフトどうしの組は逆向きにもう1行必要・
  同じシフトどうしの組は1行で両方向を覆う」を追記＝今回まさに聞き返された点を画面で答えられるように。
  `pairBanHelpExplainsDirectionality` テストで固定（落とすと失敗する形）。
- **[③ ルール行の読み下し文]** 「吉・Dﾃ ✕ 古・休」という記号の羅列から意味文を復元できない＝
  `ConstraintFamilyView.subs`（行と同順の読み下し文・既定 empty＝非破壊）を新設し、ペア禁止系だけ
  「同じ日に、吉のDﾃ と 古の休 が両方いると違反」を行の下に淡色で添える（同じタップ標的の中）。
- **[④ スキル群の状態表示]** スキルは勤務グループとは**別の独立した分類**（ユーザー確認済み＝分類を
  置くこと自体は正しい）。ただし「いま効いているか」を画面が言わなかった→ルール0本のとき
  「いまはスキル群のルールが1件も無いため、この分類は勤務表に影響しません」、あるとき「ルールN件が
  この分類を使っています」を状態依存で表示（3.301.0 の検算と同じ型）。開発者語「〜だけが参照します」も置換。
- 検証: `design_lint` exit=0・ホストJVM **516テスト green**（515 + 新規1）。UI層はホストでコンパイル不可＝
  括弧均衡・`subs` の宣言と全使用サイトのシグネチャ一致を静的確認。

## 予算超過を観測可能にする＝2本目の実機ログで13回中5回が300秒予算を474〜959秒まで超過（3.409.17）

同じ実機から2本目のログ（3.409.14・2026-09データ・**13回の実行**＝診断駆動で希望/担当/レンジを直しながら
必須21→0 まで追い込んだドッグフーディング。C1Plateau・CoverageDiag・設定ミス6d/6-C が実際に
データ修正の判断材料として機能した記録でもある）。その中に**予算300秒の実行が5回、474〜959秒まで
超過**しているのを発見した（#4=474s・#7=391s・#8=959s・#9=877s・#12=614s）。

### 分かっていること（ログが証明する範囲）
- **ロールが停止確認を数百秒無視した**: #4 は「グローバル最良更新 W4 epoch3（経過474秒）」＝
  roleDeadline（量子45s上限）を過ぎて約400秒ロール内に留まったワーカーが実在する。エポックは
  境界でしか deadline を見ないので、ロール内部が stopRole をポーリングしない経路が塞いだと確定。
- **超過は探索側にも後処理側にも出る**: #8/#9 は最初の後処理フェーズ(HF80)が経過959s/877sに開始＝
  探索+統合が超過。#12 は探索177sで終わった後、後処理の巡1厳密窓修復(191s)→巡2広域ビーム(614s)の
  間に423秒＝後処理クラスタ内で超過。
- **超過した実行では停滞ウォッチドッグの発火も無効化される**（発火してもワーカーがエポック境界まで
  気づかない）。#8/#9 の C1Plateau が「却下の観測がなく原因未確定」なのは、後処理予算が使い尽くされ
  クラスタが即スキップされた帰結＝症状として整合する。
- **再現は失敗**: 手元の実データ（blocked_covu=2026-08・covU=4 stuck の同型停滞レジーム）で
  #8 と同じトグル構成（立て直しON・崩し範囲ON・事前フィルタON・並列SA OFF・仕上げON）を
  120s（RSI→ALNS帯）と 300s（PORTFOLIO帯）でホスト実行したが、**どちらも予算内**（298.8s/300s）。
  ＝9月データ固有の形（希望85件・休の4日窓/14日窓ルール等）が要る。原因の特定には至っていない。

### 入れた観測（表示・ログのみ＝探索・採否・スコアは完全に不変）
- **エポック超過の検出**: ロール呼出が roleDeadline を5秒超えたら「W4:MAX_DISTANCE_RSI_PLUS(q=45s→実412s)」
  形式で記録（`AdaptiveWorkerOutcome.epochOverruns`）。集約は `[W] エポック超過` 行（`epochOverrunLog`・
  空なら出さない）。**どの役割が塞いだか**が次の発生で名指しできる。
- **予算超過の実行は内訳を操作ログへ写す**: TIME 行は元から超過時 [W] を出すが**診断ログ止まり**＝
  最後の実行以外は消える（今回まさに5回の超過の内訳が全部消えていた＝3.379.0 の lastRunDiagLogs は
  「最後の1回」しか守らない）。ViewModel が完了時に TIME(W)/エポック超過/後処理パス別 を logOp で
  操作ログ（リング1000件・実行を跨いで残る）へ写す。
- **正直な限界**: 検出側（roleDeadline+5s 超過）は遅いロールを注入しないと踏めない＝単体テストは
  整形のみで、検出は次回の実機ログで確認する（3.387.0 と同じ「起きたことが必ず残る」の型であって
  「起きないようにする」ではない）。
- 検証: `design_lint` exit=0・ホストJVM **515テスト green**（514 + 新規1）。再現試験2本
  （120s/300s・トグルON）は上記のとおり予算内＝退行なしの確認を兼ねる。

## 実機ログ(3.409.14・2026-09データ)の検分＝表示の自己矛盾3件を修正（3.409.16）

ユーザーが 3.409.14 搭載機（Pixel 10 Pro XL・10名/30日・PORTFOLIO 300s×3回・run#1→#3 の間に
調整トグル4つを ON）のログをアップロード。まず**直近の機能が実機で意図どおり動いたことを確認**し、
そのうえでログ自身の矛盾から表示バグ3件を特定・修正した。**表示・ログのみ＝重み・採否・探索は完全に不変**。

### 実機で確認できたもの（全部初観測または継続確認）
- 環境行・実行通し番号 `#N`（3.408.0）・`設定の効き`（Kotlin照合8800回／事前フィルタ227件省略／
  崩し範囲45723/45888回が既定と違う範囲／**立て直し方=91回の役割決定＝adaptiveEscapeControl の初の実機観測**／
  LAHC119回切替）・`ロール内チェーン2本`＋`RunMAGI_V5: SAチェーン2本`＝portfolioRoleParallelSa の配線実動作・
  実効外側並列=8.00・スコア収支・残存分析（covU 5件 blocked-now 実証・weekly床80件）・C1Plateau 主因内訳・
  検査6d（大島愛 休17希望>目標10）・6-C（Dﾃ 32>30）・ConstraintMus 証明（9/18・9/28・9/10）。
- **4トグル同時 ON の A/B は単体帰属が不可**（run#1 433 → run#3 424・hard 5 不変）＝
  `algorithm_portfolio.md` の見直しの条件表へ「配線は実機確認済み・効果は未分離」として正直に記録。

### 修正した3件（いずれもログの中の自己矛盾）
- **[A] 正常終了を「締切前離脱」と数えていた**: `earlyExits` の判定が `exitReason != "締切"` のみで、
  **「探索締切」（締切そのものが stopIsFinal() の stop シグナル経由で届いた正常終了）を早期離脱に数えて**
  いた。実機ログは `ワーカー離脱=8/8本が締切前(探索締切8本@275s/…)`＝予算275秒を使い切った正常な実行を
  自己矛盾で報告。3.346.1 で exitReason を4値にしたとき、この集計側だけが取り残された。
  `isEarlyWorkerExit(reason)` へ切り出し（早期離脱＝停滞シグナル・例外のみ）てテストで固定。
- **[B] 「設定の効き: 設定の効き: …」の二重ラベル**: `TuningTelemetry.summary()` が返す本文の先頭に
  「設定の効き: 」を含み、`V6FinalPort` の `MirrorLog(tag="設定の効き")` と重なっていた。本文から除去
  （唯一の消費者は V6FinalPort:761・既存テストは部分一致のみ＝無影響を確認済み）。
- **[C] C1広域ビームの採用が退行に見えた**: ログ行が `c1 107->112 / total 425->431 HARD 5->5` だけで
  weighted を出さず、**正しい取引**（keep-best は hard→weightedScore→total。low−1=−90 が c1+5=+75 を
  上回る weighted 改善）が悪化に読めた。`score before->after` を併記（AdaptiveBlockSwap と同じ書式）。
- 検証: `design_lint` exit=0・ホストJVM **514テスト green**（513 + 新規1）。

## covU-blocked の実データを匿名化して第3フィクスチャへ＝backlog#6 の残りを解消（3.409.15）

「次」の掃討。3.361.0/3.377.0 が繰り返し記録したギャップ＝**「covU が構造床を超えて blocked-now」な
実データ形状が repo に1つも無く**（golden=入力 hard=0／sample_v6=covU は解ける形）、3.377.0 が直した
残存分析の「もう直せない covU」分岐は実機ログと合成盤面でしか検証できなかった。

- **塞いでいた理由（実名を含む）を匿名化で消した**: real3（2026-08 実運用 state）は golden に無い実名
  1名を含み、そのままの追加は新しい個人データの露出になる。**職員名だけを 職員A..J へ置換し、実名を
  含む診断ログ配列（logs）を除去**した版を `blocked_covu_state.json` として追加。名前は engine の
  評価に一切入らないので、**匿名化前後で eval hard=4 soft=1681・checker covU=4・床0・
  blockedNowSlots=4 が bit 一致**することをホスト実行で確認してから採った（形は完全に保存される）。
- **言語跨ぎ照合の3本目**: `blocked_covu_eval_expected.txt`（hard=4/soft=1681）を
  `NativeParityFixtureTest` と native-parity CI の `--expect` の両側へ。ローカルで CI と同じ形の
  実行を先に通し **3フィクスチャとも MATCH・4,794,967手 mismatch=0**（第3フィクスチャが約60万手を追加）。
- **形状そのものの回帰テスト `BlockedCovUFixtureTest`**: ①床0なのに covU=4（供給床では説明できない
  不足）②`diagnoseCoverage` が全4枠を blocked-now と判定（`allBlockedNow=true`）③恒久的な充足不可
  （infeasibleSlots）とは別物＝0、を固定。**`fixtureIsAnonymized`**（全職員名が 職員A..J 規約に従う）も
  固定＝誰かが匿名化前の版へ差し替えたら CI が落ちる防具。
- 検証: ホストJVM **513テスト green**（510 + 新規3＝言語跨ぎ1・形状2）。C++ は無変更＝ローカルの parity 実行と CI が担保。

## 制約10族の「詳しい説明」をアプリへ＝既定で閉じた ⓘ 展開（3.409.14, ユーザー指示「詳しく説明をアプリにも追加」）

実機スクショの「条件の意味は何ですか？」にチャットで答えた内容を、アプリ本体でも読めるようにした。
**表示のみ＝スコアリング・エンジンは完全に不変。**
- **形は既定で閉じた「ⓘ 詳しい説明（それぞれの条件の意味）」**（`ConstraintHelpExpander`）。常時表示の
  長文は既定の設計（3.129.0 冗長性見直し・3.396.0 貼り紙を剥がして形に語らせる）に反するため、
  読みたい人がタップしたときだけ全文を出す＝ユーザー指示と既存方針を両立させる置き場所。
  `ConstraintsCard`（④群単位・⑤並び・くり返し＝呼出2箇所）と `SkillConstraintsCard` の3画面に配線し、
  **表示中の族だけ**説明が出る（見出しはカードの一覧と同じ `families` から取る＝改名で古くならない）。
- **本文は Compose 非依存の `ConstraintHelp.kt`**（`constraintHelp: Map<String,String>`・10族）へ置き、
  `ConstraintHelpTest` が **`MagiState` の cons* フィールドと過不足なく一致**することを Java リフレクションで
  固定（BreakdownLabels=3.409.7 と同じ「族を足して書き忘れると落ちる」型）。本文の規律: 意味論は
  チェッカー（MirrorCore）と同じに保つ（cons1/cons2 の canDo ガード・cons41系の日次カウント・
  cons42系の「片方だけなら違反なし」）／**重みの数値は書かない**（HF77 の重み変更で stale 化するため。
  必須か「できるだけ守る」かだけ言い、順位は設定タブ「直す優先順位」へ誘導）／必須条件と呼ぶのは
  cons3n（禁止の並び）だけ＝この画面で登録できる唯一の HARD 族、をテストでも固定。
- **アサーションが自分の本文の穴を1つ捕まえた**: cons3mn の初稿は「必須ではないので…」と書き、
  「必須条件」「できるだけ守る」の**どちらの定型語も含まなかった**＝テストの語彙チェックに落ちる文だった。
  語彙を揃える方向で本文を修正（テストを緩めない）。
- 検証: `design_lint` exit=0・ホストJVM **510テスト green**（508 + 新規2）。UI 層はホストでコンパイル不可＝
  括弧均衡（開閉対称）・既存 import のみ使用を静的確認。ホスト harness に `ConstraintHelp.kt` を追加。

## /code-review 7件を全て修正＝作ったばかりの P9 に本物の穴が3つあった（3.409.13）

3.409.12 への `/code-review`（7件・全て注入または実コードで verify 済みの指摘）を受け、全件修正した。
**最も重いのは、前日に「注入試験で2つの欠陥を潰した」と書いた P9 自身に、まだ3つの穴があったこと**＝
注入試験は「試したケースが通る」ことしか証明しない。レビューは私が試さなかったケースを試した。

### P9 の3つの穴（レビュー#2/#3/#4）→ ブロック所属ベースへ作り直し
- **#2 トークンを捨てた裸の呼び出しが完全に不可視**: `beginBoardJob("x")` と `val` 無しで呼ぶと
  **解放が構造的に不可能**（＝最悪の書き間違い）なのに、正規表現が `val x = beginBoardJob(` しか
  見ていなかった。裸の呼び出し自体を検出対象に追加（定義行は除外）。
- **#3 判定が生テキストの語検索だった**: `// finally we do the work` というコメントで「finally の外」
  判定を騙せる／無関係な `try{..} finally{log()}` が先に在れば解放がその外でも通る／
  **コメントアウトされた `// endBoardJob(t)` が解放として通る**。
- **#4 偽陽性**: 正当な1行 `try { .. } finally { endBoardJob(t) }` を「finally の外」と誤検出
  （範囲検索 `range(i, end_at)` が end 行自身を含まないため）＝CI を不当に落とす。
- **作り直し**: `_strip_kotlin_file`（行コメント・文字列に加え **複数行 /* */＝KDoc も**落とす）＋
  char スキャンで `{` を積むとき直前の識別子が `finally` かを記録し、endBoardJob 出現時に
  **スタック上に finally ブロックが在るか**で判定する（語の有無でなくブロック所属）。
  注入試験は8ケース＝欠陥4（裸呼び出し・コメント finally・無関係 try/finally・コメントアウト解放）が
  **全て発火**し、正当4（1行 try-finally・標準形・KDoc言及・finally 内の if ネスト）が**全て素通り**。
  実リポジトリは 5/5 サイト通過＝0件のまま。

### UI の同型漏れ（レビュー#1/#6）
- **#1 行本文タップが対象漏れ**: 3.409.12 は Ws1Editor の Edit/Delete **ボタン**を無効化したのに、
  **同じ行の本文タップ（48dp の行全体が clickable）**が編集ダイアログを開いたままだった＝
  「入力し終えてから拒否」がボタンの隣で再発する。シフト/グループ/職員の3行とも
  `clickable(enabled = !ui.running)` へ（ConstraintEditor と StaffManageCard は既に正しかった形に揃える）。
- **#6 スキル▼と担当可否チップ**: SkillGroupEditor のスキル割当ドロップダウン（StaffManageCard 側は
  既に enabled 付き＝非対称だった）と、Ws1Card の担当可否 FilterChip（`ws1SetGroupShift` は
  `applyStructure` 経由＝実行中は必ず拒否される）に `enabled = !ui.running`。

### 写しの stale-false 経路を源で塞ぐ（レビュー#7）
`enabled = !ui.running` は**表示の写し**を読む。3.336.0 が記録したとおり、init 時の WorkManager
問い合わせが失敗すると背景で走っているのに写しが false のまま＝その経路ではボタンが生きて
「入力し終えてから拒否」が再発する。**Worker は開始時に必ず `OptimizationRepository.setRunning(true)` を
流す**ので、その StateFlow を写しへ反映する collector を追加（問い合わせの成否に依存しない）。
下げる側は **true を見たあとの遷移だけ**＝購読開始時の初期値 false が、init 復元の
「バックグラウンド計算を継続中…」の running=true を踏み消さないため。下げるときも前景ジョブ
（boardJobLabel）や違反チェック（checkJob）が生きていれば触らない。

### docs（レビュー#5）
`DESIGN.md` §4 が「P1–P8」のままで P9 を載せていなかった＝**3.409.5 が直したのと同じ doc/lint ドリフトを
その翌日に自分で作っていた**。P9 の項と §5 の「P1–P9」を追記。

- 検証: `design_lint` exit=0（P9 注入8ケース＝欠陥4発火・正当4素通り）・ホストJVM **508テスト green**。
  UI 層はホストでコンパイル不可＝括弧均衡（3ファイルとも対称）・`ui`/`checkJob`/`boardJobLabel` の
  スコープを静的確認。

## この環境で実行できないと言っていたものを、実行できる形に置き換える（3.409.12）

「実機/Robolectric が無いから確かめられない」と記録していた項目を、**その言い分が本当かを1件ずつ検証**した。
2件は前提が誤っており、1件は数字で閉じられた。あわせて実機スクショ由来の2件を修正した。

### ① 実機スクショ＝実行中に編集ボタンが押せてしまう（3.405.0 の対象漏れ）
最適化の実行中（「実行中 ⚠5」「やめる」が出ている状態）に編集タブの「＋ 追加」が**通常どおり押せる**。
押すとダイアログが開き、入力し終えて確定した瞬間に `structuralEditBlocked()` が拒否する＝
**利用者の入力作業を全部させてから断っていた**。3.405.0 が「形が守れない約束をするな」としてセル編集シートに
入れた原則の対象漏れで、しかも**同じ操作がドアによって違う**（`StaffManageCard` の入職/退職は
`enabled = !ui.running` で正しく無効化されるのに、年間マスターの職員追加は無効化されない）。
- 追加/編集/削除の**13サイト**（`ConstraintEditor` の制約行・スキル群制約／`SkillGroupEditor`／`Ws1Editor` の
  シフト・グループ・職員）へ `enabled = !ui.running` を追加。制約行は本文タップも編集導線なので同時に無効化。
- **除外**: 「見直し候補メモ」の削除だけは残す＝`removeReviewMemo` はセッション内のUI状態で `applyStructure` を
  通らない＝実行中でも拒否されない。**拒否されないものを無効化するのは逆の嘘**になる。
- **既知の限界**: `refreshCheck` も `running=true` を立てるので、違反チェックのあいだ（1秒未満）ボタンが
  一瞬無効になる。既存の6サイトも元からこの挙動で、正しい信号（`optimizeInFlight()` の表示用の写し）を
  新設するには `running` を書く全サイトを揃え直す必要があり、その取り違えは 3.336.0 の「旗が stale」型を
  作り直す。1秒未満の見た目のために作らない。

### ② 同じスクショ＝実行中だけバッジの数字に名前が無い
実行中は `実行中 ⚠5`、非実行中は `必須違反 5`。**同じ数なのに実行中だけ名前を失う**（3.396.0 が
非実行中の枝を「必須違反」へ揃えたときの取り残し）。バッジは幅が限られるので語を短くして `実行中 必須5` へ。

### ③ P2「Robolectric が無いので盤面差替えジョブの競合を試験できない」→ 機械検査へ（design_lint P9）
**実際に起きた2件の回帰（3.404.0＝3つのジョブが旗を持っていなかった／3.409.0＝解放が終端ログより前）は
どちらも呼び出し側の書き忘れで、仲裁ロジック自体は一度も壊れていない。** つまり仲裁を
`RunFiles`（3.336.0）のように抽出して単体試験しても、**実際に起きた不具合はどちらも捕まらない**
（3.338.0 の「不変条件を強制しているのは採否であってガードではない」と同じ構図＝3.336.0 が
`ownsFiles` について「試験しても同語反復」と判断したのと同じ理由）。
- 呼び出し側の書き忘れを止める道具は**機械検査**（P5/P6/P8 と同型）＝**P9**: `val x = beginBoardJob(…)` には
  必ず `finally` の中に `endBoardJob(x)` が対であること。外すと **`optimizeInFlight()` が真のまま戻らず、
  編集ガード14箇所が閉じたままアプリが読み取り専用に固着する**（例外は出ないので実機で誰かが気づくまで分からない）。
- **教訓#30 の実践で、P9 自身の欠陥を2つ出荷前に潰した**:
  (a) 最初の版は `io.open` を使いながら `io` を import しておらず、広い `except Exception` がそれを飲み込んで
  **常に0件**だった——**3.406.0 の P6 で同じ失敗を記録したのに再発させた**。例外は読めない種類だけ握る形へ。
  (b) 直した後も注入が発火しなかった。原因は**全サイトが同じ `boardToken` という名前**を使うため、
  単純な前方探索が「この関数の解放を消しても次の関数の解放を拾って対に見える」ことだった。
  波括弧の深さで囲っている関数の中に限定して解決。**注入しなければどちらも見つからなかった。**
- 現状は 5/5 が正しく対＝**baseline 0**。

### ④「ネイティブチャンクの停止が確かめられない」→ 前提が誤り。数字で閉じた
`magi_native.cpp` に stop/deadline/abort は **grep 0 件**＝C++ に停止確認は元から1つも無い。
停止は Kotlin のチャンク境界（`timeUp()`）だけで見る設計なので、**確かめるべきは「止まるか」でなく
「止まるまでどれだけ回り続けるか」**。ホストの C++ harness で `runSaChunk` を本番の冷却ラダー
（`SaParams`: t0=10 / tf=0.1 / α=0.975 / chain=20）で実データ（golden 10×31）に対し実行し実測:
**1チャンク = 4.7ms**（3回の最小）。暴走ガード `maxIters=200000` に当たる病的なパラメータでも **257ms**。
＝停止を押してから実際に止まるまでの窓は通常 5ms・最悪でも 0.26 秒。**`.so` を読めなくても、
この項目に関して知りたかったことは全部この環境で測れた。**

### ⑤ compileSdk 37＝**もう塞がっていない**（3.173.0/3.373.0 の記録が stale）
Google の SDK リポジトリ（`repository2-3.xml`）を実際に引いて確認: **`platforms;android-37.0`（`codename` 空＝
preview でない・stable チャンネル・revision 2）と `build-tools;37.0.0` が公開済み**。3.373.0 が
「`37.2-beta1/2/3` のみ」と記録した状態から変わっている。
- **ただし今回は上げない**。理由は3つ: (a) 素の `platforms;android-37` は**存在せず** `37.0`/`37.1` という
  マイナー付きなので、`compileSdk = 37` が解決するかは AGP 9.1.1 の挙動次第＝CI を1周回さないと分からない
  (b) バブルは minSdk 36 で完全動作するので**機能上の利得はゼロ**（3.173.0 で確認済み）
  (c) targetSdk まで上げると Android 17 の挙動変更を丸ごと受け入れることになり、これは製品判断。
  **記録を「未公開だから不可能」から「公開済み・あとは AGP 互換の確認だけ」へ更新するのが今回の成果**。

### ⑥ 実データ fixture（backlog#6 の残り）＝**塞いでいた理由が事実と違っていた**
「real3 は実職員名を含み public repo なので入れられない」と記録していたが、**`golden_state.json` に既に
同じ10名の実名（と姓を含むグループ名）が入っている**。つまり real3 を足しても**新しい露出は増えない**＝
私が挙げた blocker は成立していなかった。ただしこれは逆に、**実名が public repo に既にコミット済み**という
それ自体の論点を意味する。CLAUDE.md の約50箇所がこの名前で過去の実測を記録しており、匿名化は
それらの参照ごと揃える調整＝**利用者の判断**なので、ここでは事実の報告に留める（勝手に書き換えない）。

- 検証: `design_lint` exit=0（P9 は注入2種で発火を確認）・ホストJVM **508テスト green**。
  UI 層はホストでコンパイル不可＝括弧均衡（4ファイルとも増減0）・`ui` がカードの引数として在ることを静的確認。

## 判断待ち4件を決着＝c1 の表示強度は「件数」でなく「セル数」で測る（3.409.11）

残していた判断待ちを、**推測でなく測ってから**片付けた。1件は決定の前提そのものが誤っていた。

### c1 を破線へ昇格＝3.367.0 は単位を取り違えていた
`heavySoftFamilies`（破線で描く重いソフト族）は `low/high/c3mn` で、c1 は 3.253.0 で重み 15（=c3mn）に
上がったあとも角マーク側に据え置かれていた。3.367.0 の据え置きの理由は「c1 は**最多件数**のソフト族なので
飽和する」。**その件数は fire 数**（golden 96）で、**この表示が実際に扱うのはセル数**（同 22）＝別の単位だった。
- **実測（セル総数 310）**: golden 違反117・必須0・重ソフト20・**c1=22**／real3 48・0・11・**c1=17**／
  sample_v6 105・4・0・**c1=5**。昇格すると破線は **20→42 / 11→28 / 0→5**。
  3.99.0 が「格子が警告に飽和し必須が埋没する」と判定したのは**194セル**が全部破線だった状態で、そこには遠い。
- よって `heavySoftFamilies` に `"c1"` を足し、**重み階層と表示強度を一致させる**という当初の規則を回復した
  （c1=15=c3mn なのに強度だけ違う、という状態を解消）。凡例は族名を持たない汎用文（「破線＝要調整（重）」）
  なので変更不要。KDoc には「fire でなくセルで測る」ことと実測値を残した。

### グループ上下限の一括解除が、何名ぶん消えたか画面に出ていなかった
チップ内の小さな ✕ 1回で **N名ぶんの個人設定**が消えるのに、画面上はチップが1つ減るだけ
（`logOp` は詳細設定のログ止まり）。3.399.0/3.400.0 の「イベントは Snackbar へ」に合わせ、
`notify` で件数と「元に戻す」で戻せることを返す。**✕ の形自体は変えない**＝M3 の `InputChip` の
既定 32dp は WCAG 2.5.8(AA) の 24×24 を満たし、削除は `pushUndo` で取り消せる。

### 最後の1シフト・1職員の削除ボタンが理由なく消えていた
3.400.0 は「残り1グループのときだけ理由を出す」を入れたが、**シフトと職員は対象漏れ**だった
（`if (v.shifts.size > 1)` で削除ボタンごと消えるので、利用者には理由が分からない）。
同じ形で「最後の1シフトは削除できません（勤務表のセルが指す先が無くなるため）。」等を3サイトへ。

### 変えなかったもの（根拠つき）
- **「まとめて割当」がプロ表示限定**（`MagiScheduleViews.kt` の `if (proMode)`）＝コード自身が理由を
  明示している意図的な制限。**職員削除がドアによって違う**という指摘は**事実でない**（両ドアとも
  `if (v.staff.size > 1)` ＋ `DeleteRowButton` ＋ 確認ダイアログで同一）と実コードで確認した。
- **weekly の重み**＝D3（apt/weekly/fair は業務レビュー済で現状維持・再提案しない）。3.345.0 が記録した
  「user_state で c1 が 62→84」は日付つきの過去の実測であって未解決の宿題ではない。

- 検証: `design_lint` exit=0・ホストJVM **508テスト green**。UI 層はホストでコンパイル不可＝括弧均衡
  （4ファイルとも開閉が同数ずつ増減）・`notify`/`opSy`/`sp` import のスコープ逆引きを静的確認。

## 残した3つを片付ける＝λ上限は「配線できない」と確定／DS の ✅ を機械検査へ（3.409.10）

3.409.8 で「理由つきで残す」とした3つを、**残した理由そのものを検証してから**片付けた。
結果、3つとも残す理由が成り立たなかった。

### `Hf63Infeasibility` の λ上限（`maxLam`/`maxLamBatch`/`weightFactor`）＝**書かれたままでは配線できない**
残した根拠は KDoc の「未配線（意図的）」だったが、それは**目的関数の重みに触れない**という別の宣言で、
λ上限を残す理由にはなっていなかった。調べると:
- 本番からの呼び出しは `maxLam` 0 / `maxLamBatch` 0 / `weightFactor` 0 / `updateBatch` 0
  （`isInfeasibleLikely` の19件も**全てテストから**）。live なのは `updateFromBreakdown(Focused)` →
  `infeasibleFamilies`/`infeasibleBreakdownKeys` の学習側だけ。
- **決定的な事実**: λ上限が縛るはずの Lagrange 乗数 `gLam` は**このコードベースに存在しない**
  （grep で KDoc の VBA 参照1件のみ。Kotlin は固定重み `MirrorKeys.weights` ＋ GLS penalty で動く）。
  さらに `weightFactor` の「探索スコアへ掛ける係数」は、3.213.0 以降のスコアが `hard*1e9 + soft` の
  **辞書式パック Long** なので、その一部の族だけに 0.125 を掛ける意味が無い。
  ＝「まだ配線していない」ではなく**配線すると静かに壊れる**（3.393.0 で撤去した `c3RunMode` と同種）。
- よって λ上限一式（3関数＋`LAM_*`/`INFEAS_*_CAP_DIV`/`HARD_INDICES`/`SENTINEL` と `updateBatch`・
  `infeasibleCount`）を撤去し、クラス KDoc は**実際にやっていること**から書き直した。
  テストは λ上限の突合（A=6250/B=50000/C=2500/D=50000）だけ落とし、**学習の判定そのものは全部残す**
  （`SENTINEL` を使っていた1件は、live な `update` で「一度も投入していない族は flag しない」を表す形へ）。

### `MagiSectionHeader` ＝ 採用されなかった部品なので撤去
実際の見出しは `CollapsibleSection(title=…)` と、カード内で直接書く `typography.titleMedium`（**51箇所**）。
約660コミットのあいだ一度も採用されなかった。51箇所を寄せるのは視覚上の利得が無い割に回帰リスクが
大きい別の判断なので、**部品のほうを撤去**した。

### いちばん重い発見＝**残す根拠にした ✅ が信用できなかった**
3.409.8 で `MagiSectionHeader` を残した理由は「`magi_design_system.md` §4.2 に ✅ で載っているから」。
その状態列を全数照合したら、**§4.4 `QuickActionTile` と §4.12 `MagiCalendarMonthView`/`ShiftEventPill` は
コードに存在しない**のに ✅ だった（月カレンダーは 3.193.0 でユーザー判断により撤去済み・勤務表の実体は
`MagiFlatGrid`/`FlatCell`）。**誤った ✅ は、次に読む人がそこを確かめ直す機会を奪う**（実際その罠に私が
かかった）。3件を実態へ訂正したうえで、**`design_lint` に P8 を新設**＝✅ 節の kotlin ブロックに書かれた
`fun 名前(` が実装に在るかを機械で突き合わせ、無ければ exit 1。
- **検査自身の欠陥を1つ出荷前に是正**: 初版は見出しに ✅ の文字が含まれるかで状態を判定していたため、
  「⬜（… ✅ を訂正）」という訂正文まで ✅ と読んでいた。**最初に現れた状態グリフ**を状態とみなす形へ。
- 教訓#30: ✅ 節へ実在しない `fun MagiNotARealThing` を注入すると P8 が1件で exit 1、本体は exit 0。
  そもそも**この検査は導入直後に実在の誤り4件を検出**しており（うち2件は私の撤去より前から在った）、
  発火することは注入以前に実データで確認できている。

- 検証: ホストJVM **508テスト green**（509 − 撤去した `weightFactorReflectsDeprioritization` 1件。
  他は1件も落ちない＝撤去した API は live 側の検証に使われていなかった）。`design_lint` exit=0。
  UI 層はホストでコンパイル不可＝括弧均衡（`MagiComponents.kt` は `{}` `()` とも対称に減少）と
  残存参照0を静的確認。

## 提示レビュー P0-P3 の照合＝広域ビームのピン合流漏れと停止伝播の回帰（3.409.9）

提示された P0-P3 を1件ずつ実コードへ当てた。**P0 が名指しした対象はこのリポジトリに存在しない**が、
**その欠陥クラスを機械で数えたら実在の1件が見つかった**（3.178.0「マスク最適化#1〜#4」・3.319.0
`BlockPatternMatch` と同じ形＝別コードベースのレビューだが、指摘の**形**を当てると本物が出る）。

### P0 — 名指しは不在、しかし同型の漏れが1件実在
- `adafe28` は全参照から到達不能・`rAiPriority`/`aiPriority`/「AI優先研磨」は作業ツリーにも
  `git log --all -S` にも**1件も無い**。よって提示のパッチ行はそのまま適用できない。
- 代わりに「`pinBlocks` を返すのに `pinBlocksAll` へ合流していない受領変数」を機械で列挙した。
  **合流21サイト中20が済み、`rC1wide`（C1広域ビーム）だけが漏れていた**。HF66/HF67/HF80 は
  そもそも `PinBlockAttribution` を作らない（ピン保護を持たない HARD 修復＝3.391.0 の意図的判断）ので
  合流対象外。1行の merge を追加。
- **[効果は測ると 0 だった]** scratch に計測を差し込んで実測すると、広域ビームのピン却下は
  **golden/sample_v6/real3 の全ラウンドで attempts=0**。つまり合流漏れは**構造的な欠落ではあるが、
  この3データでは1件も落としていなかった**（このパスの却下はスコア側で起きる＝3.323.0/3.326.0 の
  「不採用の主因は low/high/c1」と整合）。合流の前後で終端値が 50→54・1953→1933 と動いたのは
  **3.359.0 で記録済みの負荷依存の揺れ**であって修正の効果ではない（ここを効果と書かない）。
  最終盤面は3データとも完全一致＝**診断の完全性の回復であって挙動の変更ではない**。
- あわせて **KDoc の「18パス／20パス」を 19／21 へ訂正**（広域ビームを数に入れながら合流していない
  という食い違いがそのまま残っていた）。

### P1 — 停止伝播の回帰テストを新設（`StopPropagationTest`）
締切の伝播漏れは 2.65.0/3.161.0/3.271.0/3.313.0 で繰り返し踏んだクラスなのに、**探索の入口
`optimize()` に対する回帰は一度も無かった**。V5/ALNS/RSI/RSI++/PORTFOLIO の5経路について
「最初から停止が真」で呼び、①5秒以内に返る ②盤面の次元が保存される ③**keep-best**（返る盤面が
入力より悪くない）を固定した。実測は 14〜53ms（予算60秒）＝100倍の余裕。
**ネイティブチャンクは対象外**＝ホストJVMは `.so` をロードできない（番兵とパリティは native-parity CI が別に守る）。
教訓#30: scratch で RSI の伝播だけを切ると、この1件だけが 30秒走ってから落ちることを確認。

### P2 — 実行できないので、できないと書く
盤面差替えジョブの競合と取消順序は Robolectric か instrumented test が要る。このサンドボックスには
Android SDK が無く**実行できない**（3.386.0 で既知のギャップとして記録済み）。ロジック側の不変条件
（所有権・`boardJobLabel` のトークン方式・`RunFiles` の原子置換）はホストで固定済みだが、
**ライフサイクルの競合そのものは未検証のまま**——これは埋めたと書かない。

### P3 — 既に実測済み（再掲）
REAP は本セッションで実測して不採用（既定経路 0%・gate ON でも real3 −1.4% のみ・golden/sample は混在・
欠陥4件）。ACTS の観測値だけを 3.409.4 で採用（`実効外側並列`）。脱出比率の提案も 6ペアで
HARD 不変・合計 86935 vs 87104 で**提案側が悪い**と測って不採用。いずれも
`docs/algorithm_portfolio.md` の「実測で否決した提案（再提案しない）」に記録済み。

- 検証: ホストJVM **509テスト green**（508 + 新規1）。実データ3件で最終盤面が修正前と完全一致。
  `design_lint` exit=0。

## 呼出0の関数6つを撤去＝「4つの編集入口」は最初から3つだった（3.409.8）

呼出0の関数を機械で数えた（`::name` の関数参照も数えて誤検知を除く）。**9件のうち6件を撤去**。
いちばん重いのは数でなく、そのうち1つが**存在しない前提を3世代にわたって支えていた**こと。

- **`MagiViewModel.cycleCell` は初回インポート（2026-06-17）以来ただの一度も呼ばれていない**
  （`git log -S` で確認）。にもかかわらず CLAUDE.md は4箇所で「編集は必ず `setCell`/`setCells`/
  `cycleCell`/`applyFixSuggestion` の4入口を通る」と書き、**3.161.0・3.328.0・3.405.0 の3回が
  この関数にガードを足していた**。実際の編集入口は**3つ**。
  **死んだ関数を守ってもガードの網羅は証明されない**ので、この誤りは監査の結論そのものを弱めていた。
  タップ＝シートを開く（3.120.0/D7）が現行の決定なので、循環トグルの構想は既に上書きされている。
  過去の記録は当時の作業内容として正しいのでそのまま残し、live なコード内コメント（`setCell` の
  KDoc）だけ3つへ訂正した。
- **`V6NativeOptimizer.swapWithinStaff`**（private）は、同じ操作が op==3 の分岐へインライン展開された
  ときの取り残し。呼び出し側には `// swapWithinStaff` という**存在しない関数を指すコメント**が残って
  いたので「同一職員の2日入替」へ書き換えた。
- **`C1RepairOperators.buildIndex`** は1行の委譲で呼出0（兄弟の `hasActionableC1` は `C1RepairIndex.build`
  を直接呼ぶ）。3.275.0 が謳う「共有前段」の実体は `hasActionableC1` のほうで、この関数は名前だけだった。
- **`V6FinalPort.buildBusyLogLine`**（`BusyDetail` 自体は live・整形器だけが呼出0）／
  **`MagiViewModel.setThisMonth`**（3.127.0 で導線が「今月にする」→「来月にする」へ変わった残り。
  live なのは `setNextMonth`）／**`staffRangeOverrides` + `StaffRangeView`**（型を作るのがその関数だけの
  自己完結クラスタ＝3.286.0 で撤去した旧「回数設定画面」の孤児 VM クラスタと同じ形）。
- **残した3つと理由**（次の走査で再検討しなくて済むように記録）:
  `Hf63Infeasibility.maxLamBatch`/`infeasibleCount` は KDoc が「**未配線（意図的）**＝目的関数の重みには
  一切触れない」と宣言している面のアクセサ（`weightFactor` も同じ面）。`MagiSectionHeader` は
  `docs/magi_design_system.md` §4.2 に ✅ で載る共通コンポーネントで、使われていないこと自体は
  デザインシステムとして異常ではない。
- 検証: ホストJVM **508テスト green**（テスト数不変＝どれもテストから参照されていなかったことの裏づけ）。
  `design_lint` exit=0。UI 層はホストでコンパイル不可＝括弧均衡（4ファイルとも `{}` が同数ずつ減る）と
  残存参照0を静的確認。

## 族→日本語名の表を、テストできる場所へ（3.409.7）

族を1つ足して `breakdownLabels` へ書き忘れると、**内部キー（"c41s" 等）がそのまま利用者の画面に出る**。
引くのは10箇所で全部 `breakdownLabels[key] ?: key` の形なので、例外も警告も出ない＝実機で誰かが
気づくまで分からない。`docs/operator_ux.md` の「英字符号を画面に出さない」に正面から反するのに、
**それを守る仕組みが1つも無かった**。3.382.0 で `vioBuckets` に見つけたのと同じ形（分類漏れが
`null` へ静かに落ちる）。

- **表だけを `ui/BreakdownLabels.kt` へ切り出した**（`Map<String,String>` で Compose に一切依存しない）。
  中身は1文字も変えていない。目的は「`MirrorKeys.all` と過不足なく一致する」を**テストで固定できる
  場所へ動かす**こと＝以前は Compose 依存の `MagiDashboardCards.kt` にあり、ホストでも CI の JVM
  テストでも触れなかった。3.330.0（`StateFingerprint`）・3.382.0（`vioBuckets`）・3.386.0（`RunFiles`）と
  同じ「テストできる場所へ動かして初めて再発防止になる」の4例目。
- **固定した不変条件**: 全19族にラベルがある／実在しない族のラベルが無い（綴り違いの検出）／件数一致／
  **ラベルが空でも ASCII だけでもない**（内部キーの貼り付け漏れ）。
- 切り出した跡に残った**孤児 KDoc**（「内訳の家族キー → 日本語ラベル…」だけが無関係な関数の上に残る）も
  同時に始末した（3.308.2/3.378.0 で2回踏んだ形）。
- **教訓#30 の実践**: scratch で `weekly` の行だけを落とすと `everyFamilyHasALabel` と
  `labelCountMatchesFamilyCount` の**2件が実際に落ちる**ことを実行して確認（repo 本体は無傷）。
- **同型を1件確認して対象外にした**: `MagiViewModel.hardFamilyJp` は `topHardFamilyJp` が同じ6キーの
  リストを持つ閉じた対で、開かれた索引ではない（`breakdownLabels` のように任意の族キーで引かれない）。
- あわせて **README の「最終更新」が 3.408.0 のまま**だったのを更新（CLAUDE.md 自身が定めた
  「文書を同じコミットで更新する」に 3.409.5/3.409.6 で従えていなかった）。
- 検証: ホストJVM **508テスト green**（504 + 新規4）。`design_lint` exit=0。

## ラチェットを 0 まで下げる＝任意の角丸と生 hex を tier へ（3.409.6）

3.409.5 で P2/P4 をラチェット化したが、**ラチェットは baseline を下げて初めて意味を持つ**。据え置いた
13 件をそのまま tier へ寄せ、baseline を **0/0** にした。以後この2つは「既存分は許す」ではなく
**1件でも増えたら落ちる**＝DESIGN.md の「任意値禁止」が例外なしで機械強制される。

- **寄せ先は私の好みでなく `MagiTheme` の tier 表が決めている**（`extraSmall 10 / small 12 / medium 14 /
  large 18 / extraLarge 24`、用途は「chip/入力=10・カード=14・タイル/シート=18」）。
  **8dp → `extraSmall`**（日セル 54/56dp・色見本・集計セル・ドロップダウン枠＝どれも chip/入力）、
  **16dp → `large`**（シフト選択シートの 56dp タイル）。例外は「最低>上限」のエラー枠1件だけで、
  3.403.0 が同じ枠に使った `medium` に合わせた（**同じ UI 要素は同じトークン**）。
- **視覚は +2dp 変わる**（8→10・16→18）。3.90.0 が「非同値だから backlog」と残したのはこの差で、
  不変ではないと正直に書く。判断の根拠は DESIGN.md 3.4 の「任意 dp を新規に使わない」＋ tier 表そのもの。
- **P2 の残り1件は書き方の不統一だった**: `ensureReadable(MagiAccent.pink, Color(0xFFFFFFFF))` の隣で
  `MagiDashboardCards.kt:267` が**同じ呼び出しを `Color.White` で書いている**。名前付き定数へ揃えた
  ＝ lint 対策でなく兄弟サイトとの一致（`ensureReadable` 自身も内部で `Color.White` を使う）。
- **[検査自身の欠陥も1件] 落ちる検査を1つ報告して止まっていた**: ラチェットのループは最初に当たった
  時点で `return 1` していたため、P2 と P4 が同時に増えても P4 が見えない。実際この作業中に踏んだ
  （P2 を直すまで P4 の注入が報告されなかった）。全部集めてから落とす形へ（P5/P6/P7 も同じ扱い）。
- **教訓#30 の実践**: scratch へ「生 hex 1件」と「9dp の角丸1件」を同時に注入すると **P2/P4 の2行が
  揃って報告され exit 1**、repo 本体は exit 0 を確認（`ShiftColorEditor.kt` は用途が消えた
  `RoundedCornerShape` の import も削除）。
- 検証: ホストJVM **504テスト green**。UI 層はホストでコンパイル不可＝括弧均衡（`{}` は4ファイルとも
  増減0・`()` は削除した `RoundedCornerShape(...)` の数とちょうど一致して対称）と `MaterialTheme` の
  import 有無を静的確認。最終判定は CI。

## 「baseline 監視」が監視になっていなかった＝P2/P4 のラチェット化（3.409.5）

繰り返してきた欠陥クラス（need1 直読み・生 `wish` 比較・`betterReport` の複製）を再掃討したところ
**3クラスとも実質クリーン**（ヒットは過去の修正を記録したコメントか、3.391.0 が意図的に `covUCell` と
同一のしきい値を再現している箇所）。代わりに、**同じ「検査が守れていない」型が `design_lint` 自身に
もう1つ**残っていた。

- **P2（生 hex）・P4（任意角丸）は報告するだけで exit 0 だった**。`docs/DESIGN.md` §4 は
  「禁止事項（machine-checkable）」と呼び、P2 の説明は「lint は増分を監視」と書いているのに、
  **baseline を記録していないので 20 件増えても静かに通る**。3.409.0 の P6（複数行見落とし）と同型で、
  「守るために作った検査が守れていない」。`P2_BASELINE=1` / `P4_BASELINE=12`（2026-08-19 実測）を置き、
  **増えたら exit 1**。減ったときも定数の更新を促して落とす（下げ忘れるとラチェットが緩んで次の増加を
  見逃すため）。既存分は許すので**現状は素通り**＝ゼロリスク。
- **[docs の番号衝突] `DESIGN.md` の「P5 スコアリング混入」と lint の「P5 テンプレート食い込み」が別物**
  だった。見出しが「machine-checkable ＝ `tools/design_lint.py`」と言い切っているため、読み手は
  スコアリング混入を lint が見ていると誤解する（実際は人が守る規律で機械検査は無い）。かつ lint が実際に
  持つ P5/P6/P7 を §4 は**1つも載せていなかった**。実在する P5/P6/P7 を実名で追記し、スコアリング混入は
  **S1（lint 対象外）へ改番**した。P4 の「残 6 件」も実測 12 件と食い違っていたので実測へ。
- 検証: **教訓#30 を両方向で実践**＝scratch に生 hex を1つ足すと `P2: baseline 1 件 → 2 件` で exit 1、
  baseline を 5 に上げすぎると `→ 1 件に減りました` で exit 1。本体は exit 0・ホストJVM 504テスト green。

## 外側ワーカーの実効並列度をログへ＋検証器が空振りしていた件（3.409.4）

受領した合成パッチ（`acts_reap_parallel_observability_combined.diff`）のうち、**観測値の側だけ**を採った。
REAP 本体（`ResidualAdaptivePortfolioPolicy.kt`）は前回測った版と**バイト単位で同一**で、実測は
既定経路 0%・gate ON でも real3 のみ −1.4%（golden/sample は混在）＝一般的な改善でなく、欠陥4件
（`StagnationEscapePressure` 約55行が死にコード化／`dominantKey` が**生の件数**で並べるため weekly 118件×重み1 が
c1 73件×重み15 を押しのける／`roleFor` の `startsWith("cov")` と `else` が同一分岐／完全停滞時に
Laplace 式が「試行回数最少が勝つ」へ退化）も未修正のため見送った。

- **採った部分＝`observedOuterParallelism(totalWorkerMs, wallElapsedMs)`**（役割別worker秒の合計 ÷
  ポートフォリオ本体の経過）と `AdaptivePortfolio` 行への `実効外側並列=7.96` の1項目。
  **探索は1ミリも変えない**（割り算1つ）。
- **パッチのコメントを鵜呑みにせず実機ログ6本で裏取り**した: 3.402.0 の 2,189s/275.007s = **7.96**、
  be79eee0 の 2,199s/275.002s = 8.00、3.370.0 の 912s/119.214s = 7.65 と 880s/115.386s = 7.63 に対し、
  **7715ad51 は 74s/79.593s = 0.93**。その離脱行は `ワーカー離脱=8/8本が締切前(勝者確定7本@0s…)` で、
  **3.376.0 で撤廃した「HARD=0 到達時に残りを即キャンセルする」機構**そのものだった。
  ＝**この指標なら当時のバグを一目で検出できた**。ただしそのバグは既に直っているので、
  **前向きの用途は回帰検出**であり、いま眠っているバグの発見ではない（正直に KDoc へ書いた）。
  パッチのテストが挙げる 1.0 のログは手元に無いので、**自分で確かめた 0.93 の値**をテストに固定した。
- **[検証器そのものが空振りしていた] `hosttest.sh` がリポジトリルートを直書きしていた**:
  教訓#30 のためにバグを注入した scratch を用意しても、ハーネスは `R=/home/user/magi7ichiro-fork` を
  読むので**本体をコンパイルし「0 failures」を返した**＝検証が成功と見分けがつかない形で空振りする。
  第1引数でルートを差し替えられるようにして再実行し、注入したバグで**ちょうど1件が落ちる**ことを確認。
  同型の失敗（`^e: ` だけを見て `error:` を見逃す）は 3.406.0 でも踏んでいるので、`docs/lessons.md` §4 へ
  「**検証が『何も起きなかった』と言ったら、まず検証器が対象を見ているかを確かめる**」として記録した。
  対照的に `design_lint.py` は自分の位置からルートを導くため scratch 検証が正しく効いた＝3.409.1 で
  5ツールを同じ形へ配線したのはこの性質のため。
- 検証: ホストJVM **504テスト green**（502＋新規2）・`design_lint` exit=0。

## 画面の語彙を記録された決定へ揃える（3.409.3, /design-review の SHOULD 3件）

`/design-review` が出した SHOULD 3件を、**推測でなく記録と実装の両方が指す答え**に当てて確定させた。
**表示文字列と docs のみ＝ロジック・重み・スコアは完全に不変。**

- **[語彙の正は「必須違反」]** 候補が4世代に分裂していた（`operator_ux.md` §2「守れていない約束」→
  3.133.0「**凡例と統一**して『必須違反』へ」→ 3.393.0 進捗行「必ず守る条件」→ 上部バッジ「未解決」）。
  実装を数えると **「必須違反」が圧倒的多数**（グリッド凡例 `実線＝必須違反`・セル読み上げ・色設定の
  基準色ラベル・ホーム見出し・操作ログ）で、逸脱は**2箇所だけ**だった。3.133.0 が明示的に決めた語でも
  あるので、これを軸に上部バッジ（`MagiApp.kt:796`）と進捗行（`MagiScheduleViews.kt:130`）を揃えた。
  3.393.0 の「必ず守る条件」は決定の上書きでなく**ドリフト**だったと判定した（当時の記録に
  3.133.0 を覆す旨が無い）。`MagiViewModel:955` の「最大の未解決は…」も同じ語へ。
- **[内部語の追放]** `operator_ux.md` §2 と 3.135.0 の用語統一に当てて置換。「初期解」→**「下書き」**
  （§2「簡易作成 → 下書きをつくる」。この機能のラベルには既に「下書き」が括弧書きで入っていたので
  新語ではない）、「C1」→**「窓の要件」**（3.135.0）、「研磨」→**「整え」**（§2「いい感じに整える」）。
  対象はホーム常時表示のカード（`MagiDashboardCards:240,312,570,659`）と、`busyWhat()` 経由で6メッセージ＋
  停止バーへ波及する `beginBoardJob` のラベル1箇所を含む計9サイト。**診断ログ・操作ログの「研磨」は
  作り手向けなので対象外**（3.396.0 の除外規定）。
- **[docs の stale 修正]** `operator_ux.md` §2 の「守れていない約束」は 3.133.0 に対して stale だったので
  実態へ（旧語は経緯として括弧に残す）。あわせて §同ファイルの適用記録が**既に撤去済みの部品**
  （`StatusHero`・`ActionCard`＝3.112.0 で撤去）を現存するかのように挙げていたので注記を付けた。
- **[実装しなかったもの・理由つき]** チップ内 32dp 削除✕（3.409.2 で ✅→🟡 に訂正済み）は据え置き。
  48dp へ広げると M3 `InputChip`（既定高32dp）が伸びて行が崩れ、`trailingIcon` を外すと
  `WishEditor` は削除手段そのものを失う（チップのタップは職員選択で、削除ダイアログではない）。
  **破壊操作の導線を変える product 判断**なので、勝手に決めない。WCAG 2.5.8(AA) は満たしている。
- 検証: `design_lint` exit=0・ホストJVM **502テスト green**・括弧均衡は4ファイルとも増減0・
  旧語（`未解決 N`／`必ず守る条件`）の残存を grep で0件確認。

## タッチ域チェックリストの誤った ✅ を実測して訂正（3.409.2）

`/design-review` の観点でタッチ域を全数当てたところ、`ux_test_checklist.md` の **A3・C4 が ✅ を
主張しているのに満たしていない箇所**が2つあった。

- **実体**: `InputChip` の `trailingIcon` に置いた削除✕が **32dp**
  （`StaffRangeEditor.kt:242` 群レンジ削除・`WishEditor.kt:131` 希望削除）。
  WCAG 2.5.8(AA) の 24×24 は満たすので**アクセシビリティ違反ではない**が、本書が自ら課す
  44/48dp は満たさない。しかも**破壊操作**が、タップで編集ダイアログを開くチップの中に入れ子で載っている。
- **直さなかった理由（正直に記録）**: 素直に 48dp へ広げると M3 `InputChip`（既定高 32dp）が伸びて
  行レイアウトが崩れる。直すには「チップの中から破壊操作を出す」＝**操作の設計変更**が要る（業務判断）。
  ここでは **✅ と偽らないこと**だけを先に直した（3.405.0「守れない約束をしない」はチェックリストにも
  当てはまる。誤った ✅ は、次に読む人がそこを確かめ直す機会を奪う）。
- あわせて**他のタップ標的は準拠を実測**して注記した: 48dp 未満の `size()` はすべて
  `contentDescription = null` の装飾アイコンで、実タップ標的は外側の `heightIn(min = 48.dp)` 付き
  Row／Button（例: 詳細設定の 32dp シェブロンは装飾で、実標的は `MagiSetupCards.kt:530` の Row）。

## 死んだ配管の撤去と、生きている配管の配線（3.409.1, ユーザー指示「配管と配線する」「スタブなどを実装する」）

`tools/` を全数点検した。**スタブ・TODO()・未実装は0件**（`TODO()`/`FIXME` の grep はいずれも過去の
経緯コメントで、実装の穴ではない）。代わりに**動かない配管**が見つかった。

- **[配線] 10ツール中8つが別リポジトリの絶対パス `/home/user/MAGI-ShiftOptimizer/...` を直書き**していた。
  この repo は `magi7ichiro-fork` なので**実行すると必ず落ちる**。docs は
  「再生成 `python3 tools/mock_render_operator.py`」と案内しており、**守れない約束**になっていた
  （3.405.0 の原則はドキュメントにも当てはまる）。生きている5ツール
  （mock_render 4種・make_launcher_icon）を `__file__` からリポジトリルートを導く形へ配線し、
  `_REPO` が実際にこの repo を指し出力先ディレクトリが実在することを確認した。
- **[撤去] Web版前提の3ツールを削除**（`compare_web_native.py`・`native_verify_pdf.py`・
  `make_updated_report.py`）。**3.393.0 が「Web版は存在しない」として `V6WebCompat` を撤去した際の
  ツール側の取り残し**。判定の根拠＝①リポジトリに `.html` が1件も無い ②3つとも参照0
  ③2026-06-17 から2か月間未変更 ④`make_updated_report.py` は死んだブランチ
  `claude/code-review-0gu5p3 @ 57c0421` に固定され `openpyxl`（未インストール）を要求する。
- **[docs] 前提を明記**: 残る4つの再生成コマンドに `要 pip install pillow` を添えた
  （PIL はこのサンドボックスに無く、コマンドだけ書くと再び守れない約束になる）。
- **[HF77] `Hf63Infeasibility` の配線状況コメントが実態と逆だった**: 「独立モジュール・診断/ログ供給の
  用途」と書いてあるが、実際は `infeasibleBreakdownKeys()` が `runRsi` の `dynamicAvoid` になり
  **RSI の focus 選択を動かしている**（3.184.0/3.213.0/3.281.0）。読み手が inert と誤解する。
  「配線済み（focus 選択）／未配線（目的関数の重み・意図的）」に書き分けた。

## 検査自身が守れていなかった＝P6 の複数行見落としと、そこに隠れていた3件（3.409.0, /code-review）

`/code-review` の4件を1件ずつ実コードに当て、**4件とも実在**を確認して直した。中心は
**自分が作った検査が、守るはずの規則を守れていなかった**こと。

- **[最重要・検査の欠陥] `design_lint.py` の P6 が複数行の `copy(...)` を丸ごと見落としていた**:
  旧実装は「1**物理行**に `copy(` と `message =` が揃う」ことを要求していたため、折り返した
  `copy(\n  message = …,\n)` を1件も見ていない。**0件と報告しながら実際は19箇所**あり、
  3.406.0 が P6 を作った目的（失敗が成功色で出るのを機械で止める）を果たしていなかった。
  かつ 3.407.0 で足した `design-lint.yml` の CI ゲートも、その規則を強制できていなかった。
  ブロック単位（`copy(` から括弧が閉じるまで）で見るよう作り直し。**括弧を数える前に文字列リテラルと
  コメントを落とす**のが肝で、①`message` の値には `"(${System.currentTimeMillis() - startMs}ms)"` の
  ように**閉じない ASCII 括弧**が入りうる ②`// 旧: \`message = "…"\`` のようにコメント内の `message=` を
  拾うと誤検出になる（実際、私の集計スクリプトは `:1014` のコメントを拾って20件と数えていた）。
- **[実バグ] CSV取込の失敗が成功色で出ていた**（P6 が見落としていた本命）: 「一致する職員名が
  ありませんでした（0名）」を書く `copy(...)` に `messageIsError` が無く、直前の「CSV取込中…」から
  **`false` を引き継いで**非エラー色・4秒表示になっていた。3.406.0 が潰したはずの
  「失敗が成功に見える」型そのもの。逆向き（成功が直前のエラーから `true` を引き継いで赤くなる）も
  同じ19箇所に含まれる。**失敗1件を `true`・成功/停止/情報18件を `false`** で明示した
  （停止・中止は失敗ではない＝3.404.0 の判断を踏襲）。
- **[実バグ] 原因不明の終了を記録する行だけが「実行外」と刻まれていた**: `finally` の先頭で
  `endBoardJob()` が `activeRunSerial` を 0 に戻すため、その後ろの
  `if (!terminalLogged) logOp("W", "…完了・停止・失敗のいずれも記録されませんでした")` が実行ID を
  失っていた。**実行IDを最も必要とする診断**（3.408.0 で入れたばかりの機能）が、まさにそこで効いて
  いない。`endBoardJob` を終端ログの後ろへ移動（最適化・ソフト研磨の2箇所。他3箇所は後続の
  ログが無く影響なし）。
- **[実バグ] `loadAsync` にだけ入口ガードが無かった**: 3.404.0 が名指しした「盤面を丸ごと差し替える
  3ジョブ」のうち、ここだけ `optimizeInFlight()` を見ずに `job?.cancel()` して走り出す。
  `job?.cancel()` は前景しか止めず背景の最適化は走り続け、しかも頼れる `ui.running` は
  3.404.0 自身が表示専用へ降格させた値。`fromRestore` を追加し、**起動時の復元(init の3箇所)だけ
  バイパス**する（背景実行中の起動は正常な経路なので塞ぐと退行になる）。

- 検証: `design_lint.py` は修正後 **19件・exit=1** → 全サイト修正後 **0件・exit=0**。
  **教訓#30 の実践**＝直した検査が複数行サイトで実際に発火することを、CSV失敗サイトの
  `messageIsError` を scratch で1つ外して確認（P6=1件・exit=1・repo 本体は無傷）。
  ホストJVM **502テスト green**。UI 層はホストでコンパイル不可＝括弧均衡（`{}` 増減0・`()` は
  追加行で 14/14 と対称）と全 `loadAsync` 呼出のシグネチャ一致を静的確認。最終判定は CI。

## 操作ログに実行IDが無く、複数回実行後の書き出しが自己矛盾に見えた＋停滞監視が並列で恒久的に無効化されていた（3.408.0）

ユーザー報告「前回実行の『グローバル最良更新』と直近実行の『更新0回』が同一実行の矛盾として読める。原因は
実行IDなしの履歴操作ログと直近診断ログを連結していること」を、**実機ログを行単位で追って裏取り**した。

### ① 報告どおり＝実行IDが無い（Low・監査を誤らせる）
- 実機ログ(2026-08-19)には**2回の実行**がある: 実行A 16:09:23→16:14:22 と 実行B 16:14:56→16:19:55
  （間の 16:14:50〜54 に設定を4つ変更）。`グローバル最良更新 W0 epoch2` は**実行A**、
  `全体最良更新=0回` は**実行B**の診断。
- **`globalImproves` 自体は正しい**ことを先に実測で確認した（ホストJVMで PORTFOLIO を実走し
  「onProgress のメッセージ3回 == サマリの3回」）。つまり計数バグではなく**帰属の欠落**。
  なお実行Bは `Watchdog: 最終改善=経過0s` ＝**275秒まるごと一度も改善していない**ので 0回は正しい
  （実行Aの出力=total 406 を入力に再実行したため。`HF67: 入力の方が良好` とも整合）。
- **自分の初読は誤っていた**: 操作ログの古い側だけを見て「実行は1回」と判断し、一時は計数バグを疑った。
  ログの**全件**を見れば2つ目の `最適化 開始` があった。部分だけ見て結論を出さない。
- **修正**: `OpLogEntry` に実行の通し番号 `run` を持たせ、`logOp` が刻む（実行中の行だけ `#N`・実行外は無印）。
  `beginBoardJob(label, engineRun = true)` を勤務表づくり／仕上げ最適化／初期解生成に付け、`endBoardJob` で戻す。
  書き出しは `==== 操作ログ（… ・実行#1〜#2）====` / `==== 診断ログ（実行#2 の全文 …件）====` と帰属を明示し、
  実行が2回以上あるときは「操作ログは複数回の実行を含みます（行頭 #N）」を1行添える。JSON にも
  `diagRun` / `runsInOpLog` / `lastRunSerial` を出す。**勤務表・スコアには一切影響しない（表示のみ）。**

### ② より重い実バグ＝停滞監視が並列ワーカーによって恒久的に無効化されていた
同じログが `Watchdog: 停滞274s・実効閾値(plateau=短37s)・発火=なし・未発火の理由=現フェーズ猶予未達(実測0s/7s
＝並列ワーカーがフェーズ名を共有し頻繁に更新されるため満たしにくい)` と記録している。
**275秒まるごと無改善なのに一度も発火しない。** 3.375.2 で機構は特定し「頻度を変えるのは品質と電池の交換＝
業務判断」として保留していたが、ユーザーが「フェーズ名を停滞判定に使うべきではない・グローバル最良が改善
しない時間を主トリガーに」と明示指示したため実施した。
- フェーズ猶予は「始まったばかりのフェーズを即殺さない」ための**遅延**であって、頭打ちの検知そのものを
  止めてよい根拠は無い。`watchdogStagnationFired` の AND 条件から降格し、
  **無改善が実効閾値の2倍に達したら猶予に関わらず発火**する（`STALL_OVERRIDE_FACTOR`）。
- **代償は測ってある**: 3.341.1 の実測で早期終了を**外す**と weighted 中央 −3.5%（p≈0.075＝有意でない）。
  つまり発火を早めるとごく僅かに品質を落とし、時間と電池を大きく節約する。倍率2は「本当に詰まっている
  run は閾値の2倍まで待つ」保守側の設定。
- 既存テスト `doesNotFireWhenCurrentPhaseJustStarted`（猶予が効く局面）は倍率2により**そのまま green**。
  新規2件で「閾値超〜2倍未満は遅延・2倍で必ず発火」と実機ログそのものの再現（停滞274s/閾値37s/猶予0s→発火）を固定。

## 同梱の見本データが文字化けしたまま出荷されていた＋テストの永続的な誤検出（3.407.0, ユーザー指示「文字化けを修正する」「500テストの断捨離する」）

### ① 文字化け＝`assets/sample_state_v6.json` が二重エンコードで出荷されていた
「文字化けを修正する」を受けて、まず**どこに何があるかを機械で探した**（推測で直さない）。全追跡ファイルを
「UTF-8 として妥当だが、U+00C0-U+00FF の直後に U+0080-U+00BF が続く」＝UTF-8 を Latin-1 として読んだ内容を
再び UTF-8 で保存した状態、という条件で走査したところ **`app/src/main/assets/sample_state_v6.json` と
`app/src/test/resources/sample_state_v6.json`（バイト同一）に各93行**が該当した。
- 中身: 「休み」「Pｼ大嶋」「夜勤」「Dﾃ」「古泉 健一」が、いずれも `U+00C0-U+00FF` と `U+0080-U+00BF` が
  交互に並ぶラテン文字列へ化けていた（シフト記号も職員名も全部壊れている）。実例をこの文書へそのまま貼ると
  後述の P7 が正しく反応するため、ここではコードポイントの範囲で示す。
- **アプリ側の症状**: `loadSample` → `vm.load` → `MojibakeRepair.repair` の経路があるので**表示は正しく直る**が、
  そのぶん `wasDecoded` が真になり **同梱の見本データを開くたびに「文字化け（二重エンコード）を自動修復して
  読み込みました。元のファイル自体は修復されません（「データを保存」で保存し直すと次回からこの警告は出ません）」
  と警告が出ていた**＝アプリが自分が同梱したファイルについて謝り続け、しかもその助言（保存し直せば消える）は
  asset には適用できない。
- **テスト側の症状**: `StateParser.parse` は修復を通さないので、native-parity CI の第2フィクスチャ（3.362.0）と
  本セッションまでの sample_v6 の全計測は**記号と氏名が壊れた盤面**に対して行われていた。
- **修復**: `text.encode('latin-1').decode('utf-8')`。**可逆であること**（往復して元バイトに一致）を確認してから
  上書きした。**評価は完全に不変**であることを実測で確認: checker `hard=15 total=362 weighted=120825`・
  Evaluator `hard=15 soft=825`・**19族の breakdown が全て一致**。`state_to_flat.py` の出力も**バイト一致**＝
  native-parity CI（`sample_v6_eval_expected.txt` の hard=15/soft=825 固定）に影響なし。後処理研磨も
  修復前後で `325/73783/c1 6/pin 1953` と完全一致＝**最適化の軌跡も変えない**。
  ※garbled 版は「`kigou=="休"` のシフトが存在しない」ため `restShiftIndex` の `?: 0` フォールバック（3.320.0）で
  たまたま正しい index 0 に落ちていた＝**偶然動いていた**だけ。
- **再発防止**: `tools/design_lint.py` に **P7（二重エンコード検出）** を新設。UTF-8 として妥当なので既存のどの
  検査も素通りする＝専用の検出が要る。追跡中の全テキストが対象（`assets/` も含む＝テストのクラスパスに載らない
  ファイルを守れるのは lint 側だけ）。修復ロジック本体とそのテストは**わざと文字化けの例を持つ**ので除外リストへ。
  **教訓#30 の実践**: 文字化けを戻すと **93件・exit=1**、修復すると **0件・exit=0** になることを実行して確認した。
- **CI 配線**: `.github/workflows/design-lint.yml` を新設（python3 のみ・数秒）。P5/P6/P7 はこれまで
  「push 前に手で走らせる」規律だけに依存しており、**P7 が守るのは出荷される asset**なので CI へ載せた。

### ② 500テストの断捨離＝永続的な誤検出1件を実テストへ
`EliteIntegrationRandomSafetyTest` は `@Test` を持たない `object` ＋ `main()` の手動 fuzz ハーネスだったが、
**名前が `Test` で終わるため JUnit が拾って毎回 `initializationError` で失敗**していた（「500テスト中1件失敗、
ただしこれは既知の誤検出」という注釈を毎セッション書く羽目になっていた）。しかも**手で起動しない限り、この
ハーネスが謳う3つの安全不変条件は一度も検証されていなかった**。実測 **4.4秒**＝通常のテストとして十分速いので
`class` + `@Test` へ移した。あわせて「500ケースで一度も改善しないなら不変条件は空振り」を明示的に検査する
（`improved > 0`＝何もしないパスでも通ってしまう緑を防ぐ。3.337.0 の族網羅と同じ考え方）。
- 結果: **500テスト・失敗0**（499 @Test + 新1 = 500。誤検出は消え、代わりに3つの不変条件が本当に CI で守られる）。
- 「他にも減らせるテストはないか」を機械で探したが、**assert を1つも持たないテスト0件・同名テスト0件・
  本体が完全一致するテスト0件**＝数合わせで削る余地は無い。テストをループへ畳んで件数だけ減らすのは
  失敗時の名前を失うだけなので**やらない**。

## 実機ログと受領した不具合一覧を裏取りして6件（3.406.0）

実機ログ（3.402.0・Pixel 10 Pro XL・Android 17・8コア・PORTFOLIO 300s）と、受領した不具合一覧
（対象 dc9bf5f＝3.402.0＝当時の HEAD より4版古い）を**1件ずつ実コードで確かめて**修正した。

- **[covO 診断が守れない約束をしていた]** `diagnoseCoverage` の covO ヒントは「在勤N人を他シフトへ移せば
  解消可能（**最適化が未到達**＝『直し方を探す』で解消可）」と断言していた。**その真上のコメント自身が
  「covO は最も軽い族(重み1.0)なので…『動かせるのに動いていない』ことの説明にはならない」と書いている**のに。
  実機ログが反証を出している——covO 焦点の修復が275秒走ってなお8件が残り、うち5枠が「動かせる」判定。
  **同じ目的関数（`betterReport`）で実際に1手動かして試してから**言う形へ（`probe` 盤面＋予算240回・
  checker は約72µs=3.395.0 なので実データ規模で数ms）。改善するときだけ断言し、しないなら
  「1人動かす手はどれも他の条件を悪化させるため最適化は採用しません」＋**主因の族**（`blockedFamily`・
  ログは生キー／画面は `breakdownLabels` で日本語＝C1Plateau と同じ規約）。3.343.0(ForbiddenDiag)・
  3.344.0(covU)・3.401.0(GuidedFix) と同じ「診断が守れない約束をする」型の最後の1つ。
  **既存テストが over-promise を固定していた**（3.364.0 と同型）: 2名同一グループの盤面で1名を休へ移すと
  covO 1→0 の代わりに fair 0→2 で**目的関数は厳密に悪化**（実測 before w=3.0 → after w=4.0・
  `betterReport=false`）。前提を engine で確かめてから文言を固定し、**改善する側**（2名を別グループ＝
  fair は m<2 で対象外・実測 `betterReport=true`）の分岐も別テストで固定した。
- **[検査B が covU を不可避と断定していた]** 「上限の合計 < 必要数」で「席を埋めきれず人員不足になります」。
  だが**個人上限は SOFT(high, 重み45)で超過できる**ので covU は不可避でない。実機ログが反証——Cｵ は
  需要30 vs 上限計24 で発火したのに結果は **covU=0・high=6（＝ちょうど 30−24）**。証明できるのは
  和の下界だけ（全員が上限を守れば配置は capSum 以下 ⇒ covU ≥ 差、上限を1回破るごとに covU が1つ high に
  置き換わる ⇒ **covU + high ≥ seatsLo − capSum**）。3.354.0 の apt+high と同じ扱いへ。
  テストは前提（どう置いても和が差を下回らない／片方だけには寄らない）を engine で確かめてから文言を固定。
- **[自分のバグ] `messageIsError` が通常メッセージへ残留**（3.400.0 で作り込み）: `false` を書くのは
  `clearMessage` の1箇所だけで、**`copy` は既定値でなく現在値を引き継ぐ**ため、直前がエラーだと後続の
  成功・開始・進捗まで失敗色＋長時間表示になる。CLAUDE.md に書いた「素の copy は既定 false のまま」は誤り。
  56箇所を明示化し、失敗・拒否20箇所は `true` へ（停止・中止は「失敗ではない」ので false のまま＝3.404.0 の判断）。
  **`tools/design_lint.py` に P6 を新設**して機械で止める（`message` を書くのに `messageIsError` を書かない
  `copy(` を検出・コンパイルは通ってしまうので lint が要る＝P5 と同じ理由）。
- **[B-01] 背景計算の開始で保存失敗を握り潰して Work を投入**: `beginRun` と入力保存が両方 `runCatching` で
  握り潰し。マーカーが書けないと Worker は `activeRunId()==0 ≠ 自分のid` で所有権なしと判定して**何もせず
  即 return**——画面だけ「開始しました」で実行中が永久に残る無言の失敗。`beginRun` を `Boolean` にして
  **両方成功したときだけ投入**、失敗なら片付けて理由を出す。
- **[B-02] 壊れた完了結果を解析する前に復元手段を消していた**: `clearFiles`（入力・途中最良・マーカー）が
  `loadAsync` の**前**。結果が壊れていると復元に使えたはずのものまで同時に失う。**読めることを確かめてから**
  消し、読めなければ結果だけ捨てて中断/途中結果の経路へ落とす。
- **[B-03] Worker の `catch(Exception)` が `Error`(OOM)を拾わない**: 実行中の解除と終端ログは 3.387.0 で
  確保済みだが、**失敗通知と後片付けが走らない**＝マーカーと入力が残り、次回起動が失敗を「中断＝再開できます」と
  案内する。前景4経路を 3.400.0 で `Throwable` へ広げたのと同じ判断で揃えた。
- **[D-01] docs の自己検査式が壊れていた**: `data-models.md` のヘッダは 73 なのに合計式は 72 のまま
  （3.400.0 で見出しは直したが式の最終項を直し忘れ）。**ドリフトを検出するための式がドリフトしていた**。
  式・見出し合計・実装の3つが 73 で一致することを機械照合して修正。
- **[ハーネスの欠陥も1件]** `hosttest.sh` はコンパイルエラーを `grep -E "^e: "` で拾っていたが、この
  コンパイラは `error:` を出すため**失敗が素通りして「Tests run: 0」と表示**されていた（実際に踏んだ）。
  両方拾うようにし、注入して `TEST COMPILE FAILED`/exit=1 になることを確認。
- **検証**: ホストJVM **500テスト green**（497 + 新規3）。**教訓#30 の実践**を2回——①P6 は
  `messageIsError = false` を1つ外すと `MagiViewModel.kt:493` を指して exit=1、戻すと 0
  （最初の実装は **`io` を import していないのに `io.open` を使い、それを `except Exception: continue` が
  飲み込んで常に0件**だった＝握り潰しをやめて修正）②covO/検査B のテストは、まず前提を engine で
  確かめてから文言を固定した。UI/Worker 層はホストでコンパイル不可＝括弧均衡が HEAD と同一・
  `design_lint`（P5/P6 とも 0件）を静的確認。
- **[未対応・記録]** P-01（8並列がフェーズ名を共有し停滞監視の猶予時計を毎秒リセット＝実機ログで
  **275秒まるごと空振り・改善0回**なのに早期終了が発火しない）は 3.375.2 で「頻度は変えない＝品質と電池の
  交換は業務判断」と決めた項目で、今回も**変えていない**（判断材料としてログに理由を出す実装は 3.375.2 済み）。
  P-02（TuningTelemetry がプロセス共有）は 3.360.1 に既知として記録済み。T-01（Worker ライフサイクルの
  統合テスト不在）は 3.386.0 に「Robolectric か instrumented test が要る」と記録済み。

## シートが守れない約束をしていた（3.405.0, 監査 high の最後の1件）

勤務表のセルは**いつでもタップでき**、開いた編集シートは「タップで割当を即変更。」と言い切ってから
`setCell` が拒否していた。ガード自体は 3.328.0 以来正しく働くので**勤務表は壊れない**——壊れているのは
**形が約束したことを守れていない**こと（ユーザーの原則「貼り紙で使い方を補わなければならない時点で、
その形が意図を伝えられていない証拠」の裏返しで、ここは形が**嘘をついていた**）。
**表示・導線のみ＝重み・採否・エンジンは不変。**

- **変えられない状態ならシートを開かない**。開かなければ約束は嘘にならないので、シートの文言は
  条件分岐（「いまは変更できません」等の**新しい貼り紙**）を足さずにそのまま正しくなる。
- **判定と文言は1箇所**: `MagiViewModel.editBlockedNow()`（理由を出して true を返す）と
  `busyEditMessage()`。`setCell`/`setCells`/`cycleCell`/`applyFixSuggestion` の4つも同じ関数を読む形へ
  統一した（旧: 同じ日本語を4回書いており、片方だけ直せば食い違う。3.352.0 の「写した瞬間に取り残される」）。
  兄弟の `structuralEditBlocked()`/`runBlockedByInFlight()` と同じ命名・同じ形。
- **ゲートは `ui.running` でなく `optimizeInFlight()`**＝一瞬の違反チェック中はセルを触れる
  （`setCell` はもともと通すので、ここで塞ぐと**理由なく拒否する**ことになる）。3.404.0 で
  この旗が「盤面を丸ごと差し替えるジョブ」を正しく指すようになったので、そのまま使える。
- 希望モードも同じシートで、`setWish` は `applyStructure`→`structuralEditBlocked()` で同様に塞がるため
  両モードで挙動が揃う。
- **検証**: ホストJVM **497テスト green**（v6 層は無変更）。UI 層はホストでコンパイル不可＝括弧均衡が
  HEAD と同一・`editBlockedNow`/`busyEditMessage` のスコープ逆引き・`design_lint`（P5 0件）を静的確認。

## 旗の名前が「最適化」だったせいで、同じ性質の3ジョブが旗を立て忘れていた（3.404.0）

`ui.running` の二重用途を全サイト棚卸しするワークフロー（7エージェント・書込44件／読取134件を全数分類）の
結果を**1件ずつ実コードで裏取り**し、実在した4件を修正した。**表示・ガードのみ＝重み・採否・エンジンは不変。**

- **[最重要] 3つのジョブが編集ロックを持っていなかった**: `optimizeActive = true` は
  `runV6FullOptimize` と `runSoftPolish` の**2箇所だけ**（grep で確認）。だが `running = true` を立てて
  **完了時に `currentSchedule` と `state` を丸ごと差し替える**ジョブは他に3つある——`generateSmartInitial`・
  `loadAsync`・`importCsv`。この3つでは `ui.running=true`（画面は全ロック）なのに
  `optimizeInFlight()=false`（ガードは全開）という**逆転**が起き、`setCell` の
  `if (optimizeInFlight()) return` を素通りして盤面へ書き込まれ、完了時の
  `currentSchedule = res.schedule.copy2D()` が**それを無言で上書きする**（メッセージも出ない）。
  3.161.0 が最適化について塞いだ穴の、残り3ジョブぶんの取り残し。
  なお engine の入力は汚れない（`withSchedule` が `row.toList()` でコピーする）＝被害は
  「利用者の編集が黙って消える」ことに限定される（`pushUndo` は両方が呼ぶので「元に戻す」で復旧可）。
- **原因は名前**: `optimizeActive` は「最適化」としか読めず、読み込みや取込がこれに当たると気づけない。
  `boardJobLabel: String?`（＝走っているジョブの名前・null なら無し）へ改名し、KDoc に不変条件を書いた。
  下ろすのは `beginBoardJob`/`endBoardJob` の**通し番号**で「**自分が立てた旗のときだけ**」
  （`checkSeq`/`fixSeq`・3.333.0 の `releasedByMe` と同じ手）＝`loadAsync` が先行ジョブを `job?.cancel()` して
  自分の旗を立てたあと、遅れて走る先行ジョブの `finally` がロックを早く解いてしまう事故を防ぐ。
  `optimizeInFlight()` の名前は据え置き（読み手23箇所を触らない）。
- **[4つ目の編集入口だけガードが無かった]** 3.328.0 は「編集は必ず4入口を通るのでその4つだけを塞ぐ」と
  したのに、`applyStructureWithMessage(r: Ws1Result, ...)` にだけ `structuralEditBlocked()` が無かった。
  通るのは `ws1ResetGroupApt`(apt全リセット)と `importStaffCsv`(職員一覧CSV取込)で、**後者は
  `currentSchedule` ごと差し替える**ため最適化中に到達すると 3.161.0 のクラスに触れる。
- **[ゾンビ化] `importCsv` に入口ガードが無かった**: `job = viewModelScope.launch` が走行中の最適化の
  参照を**キャンセルせずに上書き**する＝その最適化は `stop()` で止められない（3.271.0 が
  `generateSmartInitial` で直したのと同型の取り残し）。`runBlockedByInFlight("CSV取込")` を追加。
- **[停止を「失敗」と呼んでいた]** `loadAsync`/`generateSmartInitial`/`importCsv` の3経路だけ
  `CancellationException` を分離しておらず（兄弟の `refreshCheck`・`applyStructureWithMessage` は分離済み＝
  非対称）、停止や `job` 上書きのたびに「読込失敗」「初期解生成失敗」という**誤った文言**が出ていた。
- **[途中経過が生き残る] `liveSchedule` を完了時に消していなかった**: 開始時に空へ戻すのに完了時は放置。
  消費側は `if (!ui.running || ui.liveSchedule.isEmpty()) return` なので、**あとで編集して違反チェックが
  走る（`ui.running` が再び真になる）と、前の実行の古い途中経過が現在のものとして出る**。両最適化の
  `finally` で消す。UiState の宣言コメント「計算中の最良盤面（実行中のみ）」が実態と一致する。
- **[文言] 「計算中は…」の一律表示をやめた**: `busyWhat()` が旗の名前（勤務表づくり／仕上げ最適化／
  初期解生成／読み込み／CSV取込／バックグラウンド計算）を返し、8つのメッセージとログが**何の実行中か**を言う。
  `stop()` の「対象:」も一律「計算」からジョブ名へ。
- **検証**: ホストJVM **497テスト green**（v6 層は無変更）。UI 層はホストでコンパイル不可＝括弧均衡が
  HEAD と同一・`optimizeActive` の残存参照ゼロ・`boardToken` と `busyWhat()` の**スコープ逆引き**
  （`boardToken` は5関数それぞれの内部で宣言・使用）・`design_lint`（P5 0件）を静的確認。
  **トークン方式の正しさは順序を手で辿って確認**: 最適化(token=1) → `loadAsync` が `job?.cancel()`（非同期）
  → `beginBoardJob`(token=2) → 遅れて最適化の `finally` が `endBoardJob(1)`＝**1≠2 で下ろさない** →
  `loadAsync` の `finally` が `endBoardJob(2)` で下ろす。`applyBgResult` と復元経路は
  `applyStructure*` を通らず直接書くので新しいガードの影響を受けないことも確認済み。
- **[未対応・記録]** バックグラウンド継続の復元（init）で `loadAsync` の成功時 `running=false` が
  「バックグラウンド計算を継続中…」の表示を消す（`optimizeInFlight()` は true のままなのでガードは
  効いており、崩れるのは表示だけ）。本版より前からある挙動＝今回のスコープ外。

## 下限>上限を、あとから叱るのでなく入力時に止める（3.403.0, 監査 D-1）

`V6SanityPort` は群のレンジ（3.399.0）も個人の回数（既存）も「下限N > 上限M で矛盾しています」と診断し、
**ワンタップ修正まで出している**のに、**その値を入れる4つのダイアログはどれも素通しで確定できた**——
直した直後にまた同じ入力ができる。とくに群のレンジの lo>hi は engine の `z < l || z > u` により
**どの人数でも必ずどちらかが真**＝期間の全日が違反になり、勤務表をどう組んでも消えない。
**表示・入力ガードのみ＝重み・採否・エンジンは完全に不変。**

- **判定を単一ソースへ**: `V6SanityPort.rangeOrderConflict(lo, hi): Pair<Int,Int>?`（矛盾なら実際に使う
  (下限, 上限) を返す）。**事後診断2箇所と入力ダイアログ4箇所が同じ関数を読む**＝片方だけ緩いと
  「画面は通すのに、あとから直せと言われる」入力が生まれる。診断側もメッセージの数値をこの戻り値から
  取るので、判定と表示がずれない。両方が数値のときだけ矛盾＝**空欄（未設定）・片側だけ・下限==上限
  （厳密ピン）は従来どおり保存できる**（数値でない値は 2h の別の検査が扱う＝二重に叱らない）。
- **4つのダイアログ**（群のレンジ／スキル群のレンジ＝`ConstraintDialog`、個人別の回数／グループ単位の回数
  ＝`StaffRangeEditor`）: 確定ボタンを押せなくし、入力欄の枠を赤くし、`RANGE_ORDER_HINT`
  （「下限は上限以下にしてください」・`Affordance.kt` の共有定数）を出す＝**3.186.0 の必要人数パネルと同じ形**。
  同じ間違いは同じ形・同じ言葉で示す。`ConstraintDialog` 側は `OutlinedTextField.isError`（組み込み）、
  ステッパー側は枠（`MaterialTheme.shapes.medium`＝P4 の任意角丸を増やさない）。
  `StaffRangeDialog` には既に A6「担当外シフトを選ばせない＝guidance も事後検出するが入力時に防止して
  二重防御」という同型の先例があり、その対象漏れを埋めた形。
- **検証**: ホストJVM **497テスト green**（495 + 新規2）。**教訓#30 の実践**＝述語を scratch でのみ
  `return null` へ壊すと `rangeOrderConflictFlagsOnlyRealConflicts` と 3.399.0 の
  `groupRangeLoAboveHiAlwaysViolatesAndIsReported` の**2件が実際に落ちる**ことを実行して確認（repo は無傷）。
  ただし `diagnosisAgreesWithThePredicateItShares` は落ちない——**両者が同じ壊れた関数を読むので合意は保たれる**
  ＝これは乖離を捕まえるテストであって共有バグは捕まえない、と正直に記録する。
  UI 層はホストでコンパイル不可＝括弧均衡（4ファイルとも HEAD と同一）・シンボルのスコープ逆引き・
  `NumField` 全7呼出のシグネチャ整合・`design_lint`（P5/P1/P3 とも 0件・P2/P4 の baseline も不変）を静的確認。

## 止める手段が消える／押せる行と押せない行が同じカードに混ざる（3.402.0, Nielsen 監査の残り2件）

監査の確定項目から、進行中の `running` 分割作業と**独立に閉じられる**2件。どちらも
「同じ意味の操作なのに、片方だけ扱いが違う」型。**表示・導線のみ＝重み・採否・エンジンは完全に不変。**

- **[B-3] 「直し方を探す」の最中だけ「やめる」が出なかった**: `MagiViewModel.stop()` は 3.284.0 以来
  `running || fixSearching` の**両方**を見て両方を戻す（`fixJob?.cancel()` もする）のに、
  下部バーの分岐だけ `ui.running ->` に限定されていた。探索は `viewModelScope.launch` で走るので、
  **ユーザーには中断する手段が画面上に一つも無い**（`FixSuggestionCard` は「候補を探しています」と出すだけ）。
  ゲートを `stop()` と同じ条件へ揃える＝**同じ判定を2箇所に書かない**。下部バーは `bottomBar` に常設なので、
  探索が走る分析タブでも必ず届く。
- **[B-4] 分析タブの日別行だけ押せなかった**: `AttentionCardsSection` の人別行は
  `onClick = if (ac > 0) ({ onFocusStaff(i) }) else null` で修復フローへ飛ぶのに、日別行は
  `onClick = null` が**無条件**に渡されており、同じカード・同じ見た目の行で押せるものと押せないものが
  混在していた（3.397.0 で TallyBox に入れた「押せるものだけ形が違う」原則の対象漏れ）。
  行き先は**要確認一覧が既に持っているもの**（`onShowDay = { j -> focusCell = -1 to j; tab = 1 }`＝
  勤務表の該当日へジャンプ＋日ヘッダを約2.5秒ハイライト、3.111.0）と同じラムダを渡す。
  末尾の文字も `ConfirmRow` の規約（日＝「勤務表→」／人＝「直し方→」）に合わせ、`AttentionRow` に
  `hint` を足して**呼出側が行き先を宣言する**形にした（既定は従来どおり「直し方→」＝人別行は無変更）。
  `onShowDay` は既定 `{}` なので他の呼出は非破壊。
- **検証**: UI 層はホストでコンパイル不可＝①括弧均衡が HEAD と同一（両ファイルとも `{}`/`()` ±0）
  ②導入シンボルのスコープ逆引き（`onShowDay` の宣言1131行と使用1182行、`hint` の宣言1199行と使用1214行が
  それぞれ同一関数内／呼出側 `focusCell`・`tab` は宣言325・327行に対し呼出604行まで**波括弧の深さが
  一度も宣言時を下回らない**＝同一スコープ）③`design_lint`（P5 テンプレート食い込み 0件・P1/P3 も 0件）。
  ホストJVM **495テスト green**（既知の false positive 1件を除く。v6 層は無変更）。最終判定は CI。
- **[記録] CI の 3.401.0 失敗はコードでなくインフラだった**: `V6 Engine Check` が
  `Could not GET '…/jsr305-3.0.2.pom'. Received status code 403 from server: Forbidden` で
  依存解決に失敗＝Maven Central の一時的な 403。ログに `e: file:` の行は1つも無く、
  コンパイルもテストも走っていない。`rerun_failed_jobs` で再実行して確認した。
  **「conclusion: failure」を見たら、まずジョブのステップと `e:` 行の有無を見る**（3.398.0 の
  concurrency キャンセル誤読に続く2例目＝失敗の種類を見分ける手順として残す）。

## 「なおすのを手伝って」が診断と正反対の約束をしていた（3.401.0）

Nielsen 監査 high の残り1件。**アプリの中で同じ枠について正反対のことを言う2つの画面があった。**

- `GuidedFixDialog` は対象を `verdict == CoverageVerdict.FIXABLE && it.miss > 0` だけで選び、
  **無条件に**「この日に動かせる人がいます。だれかを『◯』に入れますか？」と断言していた。
- しかし `verdict` は「担当できる人数 >= 必要数」という**静的判定**で、いまの希望・盤面では埋められない枠
  （`blockedNow`）も FIXABLE のまま残る——これは 3.344.0 が**意図的に**そう決めた区別（希望を1件変えれば
  直りうる枠を「恒久的に不可能」と断じないため）。
- その結果、**同じホーム画面の `CoverageDiagnosisCard` が「いまの希望・担当のままでは埋められません」と
  出している枠に対して、この画面だけが「動かせる人がいます」と言っていた**。押しても必須違反は減らず、
  何度押しても同じ日が出続け、理由はどこにも書かれない。

### 直したこと
1. **対象から `blockedNow` を外す**。
2. **`blockedNow` が残るときに「直し終わりました！」と言わない**。旧実装は `allDone = target == null &&
   infeasible.isEmpty()` なので、対象を外しただけだと**より悪い嘘**になる。専用の分岐を足し、
   診断が調べた `reason` をそのまま出して「もう一度つくっても、この日は同じ結果になります」と書く。
   分岐の順は `target → infeasible → blocked → 完了`＝旧の優先順位をそのまま保ち、
   **旧なら「完了」と言っていたケースだけ**が新しい分岐に入る。
3. **候補の一覧そのものを診断と同じ基準にする**。`shortageFixCandidates` は canDo・既に同シフト・
   希望固定しか見ておらず、押しても HARD が減らない候補が混ざっていた。`diagnoseCoverage` が
   「空き番」と数えるのと**同じ2条件**を足す＝①移すと禁止連続にならない（`p.makesForbiddenRun`）
   ②抜けても元のシフトに穴が空かない（`p.covUCell(m, j, cnt-1) > p.covUCell(m, j, cnt)` なら玉突き＝対象外）。
   **同じ関数を呼ぶ**ので一覧と診断が構造的にずれない（`c3nAt` は `makesForbiddenRun` の別名だと確認済み。
   `norm` と生 `sched` の違いは、範囲外セルがどの有効シフト index とも一致しないため結果に影響しない）。
4. 候補が0件のときの文言を、汎用の「いま動かせる人がいません。別の日を見直すか…」から
   **その枠の `reason`**（なぜ動かせないかを診断が既に書いている）へ。

読み取り専用の表示・候補生成のみ＝重み・スコアリング・エンジンは完全に不変。
検証: v6/model 無変更＝ホストJVM 494テスト green は据え置き。UI 層はホストでコンパイル不可＝
括弧均衡（2ファイルとも増減が対称）・`design_lint` P5=0 を静的確認。最終判定は CI。

## 操作の返事が返ってこない群を潰した＋3.399.0 の自分の回帰（3.400.0）

Nielsen 監査（確定24件・high4）の第1群「押した→何が起きたか分からない」。3.399.0 で通知路（Snackbar）は
直したが、**その通知路へ何も流していない経路**と、**流れても区別が付かない**問題が残っていた。

### ① ファイルの読み書き7経路が、成功しても失敗しても一切反応しなかった（high）
- 読み: `runCatching{…}.getOrNull()` → `if (text != null) vm.load(text)` で **else が無い**。
  書き: `runCatching` の戻り値を**一度も見ていない**（JSON保存/CSV保存/コンポーネント別CSV/ログtxt/ログjson）。
- 「データを保存」を押してファイル名を決めて確定 → **画面が1ミリも変わらない**。これは失敗時ではなく**常態**。
  かつ `CreateDocument` は callback より前に SAF がファイルを実体化するので、書き込みが落ちると
  **0バイトのファイルだけが残る**＝保存できたと信じて元データを捨てうる。
- `MagiViewModel.notify(text, level)` / `notifySave(result, what)` / `notifyOpenFailure(result, what)` を新設し、
  7経路＋見本データ読込を繋いだ。`notify` は **画面と操作ログの両方**へ出す（旧: どちらにも残らなかった）。
  `openOutputStream` が null のときも `FileNotFoundException` を投げて失敗として扱う（旧: 静かに成功扱い）。
- **生の例外文は画面に出さない**（`ioReason` で利用者の言葉へ。詳細は logOp が持つ）。

### ② 【自分の回帰】3.399.0 の Snackbar 化で、実行中ずっと Snackbar が出続けるようになっていた
- 前景 `message = "V6 $phase 実行中…"`（進捗コールバック・200ms 窓）と背景 `message = "バックグラウンド ${p.phase}"`
  （`OptimizationRepository.progress` の collect）が、**毎進捗で message を書いていた**。
  MessageBar 時代は「最下端の1枚が書き換わる」だけだったが、Snackbar になると**フェーズが変わるたびに出る**。
- これは**イベントでなく状態**で、3.399.0 自身が「イベント＝Snackbar／状態＝上部バッジと進捗行」と
  書いた役割分担に反していた。両方とも外す（フェーズの遷移は 3.283.0 のスロットル付き logOp に残る）。
- **教訓**: 出口の形を変えたら、**その出口へ何を流しているかを全数見直す**。今回は「message を書く箇所」を
  grep して1件ずつ「イベントか状態か」を判定した（残ったのは開始・停止・完了・拒否＝すべて一度きり）。

### ③ 【監査が見つけた実バグ】読込失敗が「読込失敗: 読込中…」と出ていた
```kotlin
onFailure = { _ui.update { it.copy(running = false, message = "読込失敗: ${it.message}") } }
```
内側の `it` は **UiState** を指す（Throwable ではない）ので、出ていたのは直前の文言。すぐ下の
`catch (e: Throwable)` は `e.message` で正しく、**ここだけ**の取り違え。引数に名前を付けて塞いだ。
同型を全 `onFailure` へ grep したが、他は名前付き（`t`）か `it` を関数へ渡すだけ（Worker の3箇所＝正しい）。

### ④ 成功と失敗を同じ見た目・同じ長さで出していた
- `UiState.messageIsError` を追加。**`notify(…, "W")` が唯一の true の書き手**、`clearMessage` が false へ戻す。
  素の `copy(message = …)` は触らない＝既定 false のまま＝旧来どおりの見た目（退行しない）。
- 失敗は `errorContainer`（SettingIssuesCard 等が既に使っている「失敗の色」）＋`SnackbarDuration.Long`
  （4秒だと120字級の失敗文を読み切る前に消える）。
- あわせて画面から内部名「V6」と生の例外文を落とし（3.147.0/3.191.0 の方針の取り残し）、
  **違反チェックの失敗に痕跡が残らなかった穴**（3.382.0 は長い実行4経路だけが対象だった）に logOp を足した。
  開始メッセージも「計算エンジン実行中…」→「勤務表をつくり始めました」＝主導線のボタンと同じ言葉に。

検証: v6/model は無変更＝ホストJVM 494テスト green は据え置き。UI 層はホストでコンパイル不可＝
括弧均衡（3ファイルとも増減が対称）・`design_lint` P5=0・`data-models.md` の UiState 件数を
**機械照合**（実装73 == 本文73・グループ合計73）。最終判定は CI。

### 監査の残り（着手順・記録）
B-2「なおすのを手伝って」がアプリ自身の診断と矛盾する約束をする（high・`verdict==FIXABLE` に
blockedNow が含まれるのに無条件で「動かせる人がいます」と断言）／B-1 セル編集シートの「即変更」／
B-3「直し方を探す」中だけ「やめる」が出ない／B-4 分析タブ日別行だけ押せない／C-1 職員削除がドアで違う／
C-2 削除の形が2つ（32dp の✕）／C-3「まとめて割当」がプロ限定／D-1 群レンジ lo>hi の**入力時**予防
（事後診断は 3.399.0 済み）。**`ui.running` の二重用途**は別途 workflow で全サイト棚卸し中。

## Nielsen 10原則の並列監査＝確認できた最上位2件を直した（3.399.0）

ユーザー提示の **Nielsen「ユーザビリティ5要素・10原則」**を、主観で当てず **並列監査＋敵対検証**で当てた
（5監査×2原則ずつ → 5検証 → 統合。対象は UI 全10,581行）。監査側には `file:line` と実コードの引用を必須にし、
CLAUDE.md を grep して**既対応・決定記録(D3-D8/E5・説明文追加)を除外**させ、検証側は既定を「偽」にして
**実コードで再現できたものだけ** `real=true` にさせた。結果 15件中 14件が real（うち high 2件）。
**確認できた最上位2件だけ**をこの版で実施する（残りは順に）。

### ① 操作の結果が画面外にあり、しかも消えなかった（原則1・high）
- `ui.message` の描画は **`MagiApp.kt` のスクロール内容の最下端に1枚だけ**。押した場所（カード列の上や
  下部バー）から遠く、長いタブでは**画面外**。
- `clearMessage()` は**定義があるだけで呼び出しゼロ**＝一度出たら次の操作が上書きするまで残る。
  下までスクロールすると無関係な古い結果が出ている。
- 実害がいちばん大きいのは**断りの理由**（`setCell`/`setCells`/`cycleCell`/`applyFixSuggestion` は
  計算中に「計算中は編集できません（完了後にもう一度お試しください）」を出すが、**それが読めない**）。
  セル編集シート（`ModalBottomSheet`＝Scaffold より上の層）の経路も、`onPick` が `setCell` の直後に
  `editingCell = null` でシートを閉じるため、Snackbar は隠れずに見える（確認済み）。
- **修正**: Scaffold に `snackbarHost` を足し、`LaunchedEffect(ui.message)` で Snackbar を出す。
  `clearMessage(shown)` を **compare-and-clear**（表示中の文言と一致するときだけ消す）にして、
  ①同じ文言が再び来ても状態が変わる＝**次のタップでもう一度出る** ②表示中に届いた新しいメッセージを
  消さない、を両立。`MessageBar` は呼出0になったので定義ごと削除。
- **役割分担が明確になる**: **イベント＝Snackbar／状態＝上部バッジと進捗行**。消えたあとも
  操作ログ（詳細設定＞ログ）に残るので読み返せる。
- **やらなかったこと**: 重大度で色を分ける（「読込完了」と「実行できません」が同じ色）。`message` は
  ただの String で、severity を足すには**約40の設定サイトを漏れなく直す**必要がある。文言のキーワードで
  判定するのは脆いのでやらない＝別途の項目として残す。

### ② 群/スキル群のレンジで「下限>上限」が保存でき、しかも黙って全日違反になる（原則5・medium）
- engine は `z < c.l || z > c.u`。**l>u だとどの人数でも必ずどちらかが真**＝その群×シフトは
  **期間の全日が c41 違反**になり、勤務表をどう組んでも消えない。
- **個人の回数(staffRange)は同じ矛盾を既に検出し、ワンタップ修正（下限をNに下げる）まで出している**のに、
  群/スキル群のレンジだけ取り残されていた（3.327.0 の検査2h は「空でないのに数値でない」しか見ない）。
  **同型の取り残し**という、このリポジトリが繰り返し見つけてきた形。
- **修正**: `checkRange` に lo>hi の検出とワンタップ修正 `CLAMP_GROUP_RANGE_LO` を追加。
  **行は index でなく内容一致（data class の equals）で指す**＝`DELETE_DUP_SEQ` と同じ理由で、
  診断からタップまでに並びが変わっても別の行を壊さない。
- **テストは前提から確かめた**: まず `UnifiedViolationChecker` で「3日すべてが c41 違反」を assert し
  （＝「全日が違反」という主張の裏取り）、そのうえで診断とワンタップ修正の中身を固定した。
  教訓#30 どおり、修正を一時的に戻すと新テストが実際に落ちることを実行して確認した。

### 監査で real と確認されたが、この版では手を付けていないもの（記録）
`ui.running` が5分の最適化と一瞬の違反チェックを兼ねる（＝設定を1つ触るだけで下部バーが赤い「やめる」に
なる・セル編集シートが「タップで即変更」と言い切る）／ホームの補助ボタンに内部コード「C1」と開発語
「初期解」／削除の形が2つ（48dp ボタン と チップ内32dpの✕）／職員削除がドアによって押せる/押せない／
「元に戻す」が何を戻すか示さない／「直し方を探す」中だけ「やめる」が出ない／「まとめて割当」がプロ表示限定／
個人回数の入力が ±1 タップのみ／分析タブの日別行だけ押せない／ホームの診断カードが最大5枚積み上がる。
**とくに最初の1件は他の2件の根本原因**（1つの旗が2つの意味を持つ）なので、直すなら UiState に
「最適化中」を分けて全サイトを揃える必要があり、単独の版として扱う。

## 日本語テンプレート食い込みを機械の検査にした＋取消の無いダイアログ（3.398.0）

3.397.0 が **CI のコンパイルで落ちた**。原因は `" $count件"` ＝ **Kotlin は日本語を識別子文字として扱う**ため
`count件` という未定義シンボルになる。**3.290.0 で同じ形の失敗を踏み、CLAUDE.md にも書いていたのに再発させた**。

- **是正**: 波括弧で囲む（`" ${count}件"`）。
- **3度目にしないための機械化**: `tools/design_lint.py` に **P5「テンプレート食い込み」**を追加。
  `app/src/{main,test}/java` の全 Kotlin を走査し、`$識別子` の直後が**非ASCIIの文字/数字**（`unicodedata` の
  カテゴリ L\*/Nd＝Kotlin の `isLetter||isDigit` と同義）である箇所を検出する。`・`(Po) や `）`(Pe) は
  識別子を終端するので誤検知しない。**発生すれば必ずコンパイルエラー**なので `--strict` を待たず exit 1 にする。
- **なぜ再発したか（構造）**: UI 層はサンドボックスでコンパイルできず、括弧均衡やスコープ確認では
  この種の字句エラーを捕まえられない。**CI 1周（約1.5分）まで気づけない**のが原因。
  → **Kotlin を編集したら push 前に `python3 tools/design_lint.py` を必ず走らせる**（P5 は 0 が必須）。
  CI には足さない＝コンパイルが同じものを必ず捕まえるので新しい信号にならない。価値は**push 前**にある。
- **検証（教訓#30）**: バグを一時的に戻して検査が実際に落ちること（exit 1・`file:line`・該当トークン
  `($count件)` まで指す）を実行して確認し、ファイルを復元してから採用した。
- **CI ログが読めなかった件の記録**: Gradle の失敗は 1,800行超のスタックトレースで、`get_job_logs` の
  tail では `e:` 行に届かなかった。**原因はコードを読んで特定した**（`$識別子+日本語` を全変更ファイルへ
  grep）。CI ログが取れない前提（CLAUDE.md 冒頭）は今も有効なので、**静的検査を厚くするのが正しい対処**。

### 取消のボタンが1つも無いダイアログ
「担当外の希望を含めますか？」は「含めて反映」「担当内のみ反映」の2択で、**どちらを押しても勤務表が変わる**のに
取消のボタンが無かった（外側タップ/戻るでしか抜けられない）。しかも**取消の位置(dismissButton)に反映が置かれ**、
押し間違いがそのまま変更になる。**すぐ上のCSVダイアログは既に正しい形**（選択肢は confirm 側の列にまとめ、
dismiss はキャンセル）をとっており、ここだけ取り残されていた＝同じ形へ揃えた。
全 `dismissButton` を棚卸しし、**アクションが取消の位置にあるのは他に1件**（`ColorPickerDialog` の
「既定に戻す」）だけと確認。こちらは `DialogHeader` の ✕ で取消が見える形で残っているため今回は触らない
（記録に留める）。

## 残していた貼り紙2件の「形」を作った（3.397.0）

3.396.0 で「形がまだ語れていないので剥がすのは先」と残した2件に、形のほうを与えた。
**表示のみ・スコアリング不変**（v6/model は1ファイルも触っていない）。

### 集計セル＝押せることを `›` が示す
押せるのは**違反セルだけ**なのに、そのセルが持っていた手がかりは色と ▼▲ で、これは「不足/超過」の意味であって
「押せる」ではない。だから「ⓘ タップで内訳と直し方」という貼り紙が要っていた。
セル内の右端へ **`›`** を出す＝**新しい記号を発明せず**、このアプリが行で使ってきた「`›`＝押せる」
（SetupGuideCard・MonthlyChecklistCard・MultiSelectOpener）と同じ語彙を借りる。
- **置き場所を呼出側でなく `TallyBox` にした**のが肝。`onClick` を渡した＝押せる、と見た目が**構造的に**一致し、
  呼出側で書き忘れが起きない。実測で押せる呼出は2箇所（職員別セル・日別セル）、押せない呼出は7箇所（見出し・
  名前列・合計列）＝意図どおりの2箇所にだけ付く。
- 数字は `›` のぶん（end 10dp）左へ寄せて重ならないようにする（セル幅48dp・内側46dp・"▲100" でも収まる）。
- 貼り紙2枚（職員別・日別）を剥がした。`TallyLegend`（▼不足/▲超過/—対象外）は**色と記号の意味**の凡例で
  操作の説明ではないので残す。

### 色チップ＝同じ操作は同じ形にする
違反種別の色チップは「背景を丸ごと色で塗った白文字の四角」＝**凡例(badge)の形**で、操作できる形ではなかった。
だから「チップをタップで…変更できます」の貼り紙が要り、それでも実機で「個別に設定できない」と報告された（3.122.0）。
**同じ操作（色を変える）は同じ形**にする＝シフトの表示色と同一の**枠＋色見本＋ラベル**へ揃えた。
- 両者が写しでズレないよう `ColorChip(hex, label, custom, enabled, onClick)` として**共有部品に切り出し**、
  `ShiftColorCard` も同じ部品を呼ぶ形へ（3.352.0 の「写した瞬間に取り残される」の再発防止）。
  `custom`（利用者が明示指定）は枠を 2dp primary＝ShiftColorCard が既に確立していた規約をそのまま使う。
- **副次的な効果**: ユーザー指定色の上へ白文字を載せる必要が消え、`ensureReadable` によるコントラストの
  綱渡りが構造的に無くなった（ラベルはカード地の上に載る）。
- 19族は `chunked(4)` の等幅 Row → `FlowRow`（ラベル長に合わせて折り返す＝「担当外シフト」等が潰れない）。
  基準色2つは「未設定の種別に効く基準色」という小見出しの下へ置き、**並びが所属を教える**形にした。
  カードの subtitle（操作の説明）は撤去。
- 色は完全に不変（`MagiAccent.orange = 0xFFE08A1E` = `"#E08A1E"`・`gray = 0xFF8A979B` = `"#8A979B"` を確認して
  hex 文字列へ寄せた）。

検証: v6/model 無変更＝ホストJVM **471テスト green** は据え置き。UI 層はホストでコンパイル不可＝
括弧均衡（3ファイルとも開閉の増減が対称・全体balance 0）、`ColorChip` の宣言と全4呼出が同一パッケージ
（`internal`）であること、`TallyBox` の全9呼出のうち `onClick` を渡すのが2箇所だけであること、
削除した import 8件の残存参照ゼロを静的確認。最終判定は CI。

## 貼り紙を剥がして形に語らせる（3.396.0, ユーザー提示の設計原則）

ユーザーの原則2つ——「説明書きがなくても見ただけで使い方が伝わるものが良いデザイン。貼り紙で補う時点で
形が意図を伝えていない証拠」「『これ、どういう意味ですか？』と聞き返された時点でそのUIは失敗。見出し・
順番・レイアウトそのものが読み方を教える」——を、**画面の文字列を全数 grep して**当てた。
**表示・文言のみ＝重み・採否・エンジンは完全に不変。**

### 形が語っていなかった1件＝直した
`ConstraintRow`（制約の一覧行）は 12sp の素のテキストで、KDoc 自身が「タップ可能に見えるよう最小高44dpを
確保」と書いていた——**44dp は触れる大きさであって見た目の手がかりではない**。だからカードの上に
「行をタップすると変更できます」という貼り紙が要り、それでも実機で「登録した制約の変更ができない」と
報告された（3.130.0）。職員一覧（`StaffManageCard`）は**同じ「行を編集する」操作を編集ボタンで**表して
いるので、同じ操作は同じ形に揃え、貼り紙を剥がした。

### 形が既に語っていた4件＝貼り紙だけ剥がした（形は無変更）
| 貼り紙 | 形が既に語っていた根拠 |
|---|---|
| 職員一覧「行タップで改名・所属変更。スキル▼で…」 | 各行に編集ボタン・▼・追加/削除が並んでいる |
| シフトの表示色「タップして変更できます」 | 枠＋色スウォッチ＋48dp のチップ（情報の半分「既定は種別ごとの色」だけ残した） |
| 必要人数・希望シフト「日をタップして…設定します」 | 日セルは角丸＋枠＋選択時 2dp primary＋背景反転 |
| 集計(日別)「👆 左右スワイプで他の日」 | 列は 84dp + 48dp×31日 = **1572dp** ＝対象端末（幅390dp以上=D4）では**右端が必ず見切れる** |

### 「これ何ですか？」と聞かれる表示＝見出しと並びで直した
- **進捗行から反復数を外した**。「1.2M回」「毎秒40K」は作り手の指標で、初見の人は必ず「何の回数？」と聞く。
  知りたいのは「あとどれくらい」と「良くなっているか」の2つで、それは他の項目が答えている。反復数は
  診断ログの `TIME` と `AdaptivePortfolio 合計iter`（3.360.0）に既に出ている。
  **3.393.0 で「死んだ配管を配線する」として毎秒表示を足したのは、この観点では逆向きだったので戻す**
  （`iters`/`itersPerSec` は読み手ゼロになったので UiState からも撤去＝74→**72** フィールド）。
- **2つの状態を同じ並びに揃えた**（レイアウトの反復が読み方を教える）。
  旧「未解決 ⚠3」/「改善 91% (1900→170)」→ 新「必ず守る条件 残り3件（開始69件）・気になる点 全170件」/
  「気になる点 170件（開始1900件・91%減）」。旧の (1900→170) は**何の数か画面のどこにも書いていなかった**。
- **重み表**（設定タブ・常時表示＝プロ限定ではない）の見出しを「重み表（最適化器と一致）」→**「直す優先順位」**、
  説明を「スコアの内部重み。大きいほど…」→「上にあるものから先に直します。」（並びが既に重い順＝
  レイアウトが根拠）。「絶対に守る（HARD）」「できれば守る（SOFT）」の英字は日本語が同じことを言っているので外した
  （operator_ux「英字符号を画面に出さない」）。

### 貼り紙を残した箇所（形がまだ語れていないので、剥がすのは先）
集計セルの「ⓘ タップで内訳と直し方」と、違反種別の色チップの「チップをタップで…変更できます」。
どちらも**色の付いた四角**で、押せることを形が示していない。ここは形を作るのが先で、
文言だけ消すと機能が見えなくなる。**詳細設定（上級者向け）の中の作り手語彙**（ネイティブ加速・並列ワーカー・
Kotlin照合・PORTFOLIO 等）と**プロ表示限定の生指標**（HARD Core/Guard）は、読み手が作り手なので対象外。

検証: ホストJVM **全471テスト green**。UI 層はホストでコンパイル不可＝括弧均衡と、
`data-models.md` の UiState 件数を**機械照合**（実装72 == 本文72・グループ合計72）。最終判定は CI。

## 違反チェッカーを −23% 高速化＝出力は1ビットも変えずに（3.395.0, ユーザー指示「高速化対応する」）

推測でなく**測ってから**手を入れた。サンプリングプロファイラ（2ms 間隔・呼出スタックの最深 magi フレームと
`check()` 内の**行番号**でバケツ分け）を書いて後処理研磨を計測したところ、
**`UnifiedViolationChecker.check` が後処理時間の 71.2%**（自己時間で約65%）と判明。ここは**純関数**なので、
速くしても結果は1ビットも変わらない＝品質の A/B が要らない、いちばん安全な高速化対象だった。

### 直した5点（いずれも「同じ数を、少ない仕事で数える」）
| 箇所 | 旧 | 新 |
|---|---|---|
| `inc`（自己時間 7.4%） | `breakdown[key] = (breakdown[key] ?: 0) + amount` ＝**ハッシュ探索2回＋Int のボクシング** | `MirrorKeys.index` で引いた添字の `IntArray` 加算。末尾で `all` の順に Map へ起こす（内容も順序も同じ） |
| `mark`/`markNeed`/`markCount`（自己時間 20%） | マークが重なるたび `prev.removePrefix("vio-")` で**String を1個作り**、`weightOf` の String `when` で重み比較 | 「最重1クラス」を毎回決めるのをやめ、**末尾の整列済み先頭から起こす**。両者は定義上いつも同じ（整列は重み降順の**安定ソート**＝先頭は最初にマークされた最大重み、旧ロジックの「厳密に重いものだけが置き換える」も同じものを残す）。挿入順も同じ（どちらも最初のマークで生える LinkedHashMap） |
| c1 スライド窓（11.5%） | 窓の開始位置ごとに `day1` 個を数え直す **O(T×day1)** | 出た日を引き入った日を足す **O(T)**。`j<=T-day1` かつ `l<day1` なので `cellIs` の境界検査も外せる |
| c42/c42s（16.3%） | (規則×日) ごとに `ArrayList` を2個割り当て | 使い回しの `IntArray`＋件数。違反は稀なので**大半は片側が空で捨てられる**＝割り当てが丸ごと無駄だった |
| `cellFamilies` 等の整列（5.1%） | `weightOf(it.removePrefix("vio-"))` | クラス名から直接引く事前表 `classWeight`（**重みの定義は `MirrorKeys` の1箇所のまま**＝ドリフトしない） |

### 検証
- **出力の1ビット一致**: 旧ビルドと新ビルドで `ViolationReport` の**全フィールドを順序込みで**印字して diff。
  実データ3件 × 120段階のランダム摂動（担当外・範囲外セルも混ぜて全経路を通す）＝**360盤面 3,240行が完全一致**。
- **A/B（交互実行3ラウンド・機械ノイズを相殺）**: golden 94.4→**72.2µs**／real 89.6→**68.0µs**／
  sample_v6 88.0→**68.2µs**（各データの最小値）＝**約 −23%**、**9/9 のペアで新が速い**。
- ホストJVM **全471テスト green**（既知の false positive 1件を除く）。
- **正直な注記**: 後処理の**総時間は縮まない**。いちばん重い2つの共同LNS は壁時計で時間箱を切っているので、
  速くなったぶんは「同じ時間でより多くの候補を評価する」に化ける。実際 real3 は total 322→321・
  weighted 33347→33318・c1 54→52 と僅かに良くなったが、これは 3.279.1 に記録済みの**既知のばらつき帯の内側**
  なので品質改善とは主張しない（golden は 420/2653/c1 96 で完全一致）。
- 効くのは**後処理研磨・「直し方を探す」・セル編集ごとの再チェック（UI）**。探索本体(SA/ALNS)の内側は
  C++/差分評価なのでこのチェッカーを通らない。

## ちらつき対策が既定経路で効いていなかった＝測り直して修正（3.394.0, /code-review 6件）

`/code-review` の指摘6件を1件ずつ実測・実コードで確かめ、**全件実在**を確認して直した。最大のものは
**3.393.0 で私が入れた対策そのものが、既定の長時間経路ではほぼ無効だった**という自分の欠陥。

- **[最重要・自分の欠陥] フェーズ遷移の抜け道が窓を99.9%迂回していた**: 3.393.0 は「フェーズが変わったら
  200ms 窓を飛ばす」抜け道を持たせたが、フェーズ名は**ワーカーごと**に流れる。既定の長時間経路
  （AUTO 211秒以上＝PORTFOLIO・並列8）で測り直すと **コールバック 1,174.7回/秒**、押し出しは
  **785.0回/秒**（35,559回のうち **35,518回=99.9% が抜け道**）＝窓は事実上効いていなかった。
  抜け道を「必須違反が減った瞬間」だけに絞る（単調減少なので回数が入力の必須件数で上限される）＝
  実測 **4.3回/秒**。フェーズ名の更新は最大 200ms 遅れるだけ。
  **3.393.0 に書いた「35.3回/秒・1059→53回」は測り方が不十分だった**＝30秒予算・並列4 は AUTO が V5 を
  選ぶ帯で、実機報告の場面（長時間＝PORTFOLIO）を代表していなかった。数字は上記へ訂正する。
- **[背景実行に同じ churn が残っていた]** `OptimizationWorker` は進捗コールバックごとに
  `publishProgress` しており、ViewModel の collector が UiState を丸ごと差し替える＝前景で消した
  ちらつきが背景実行では残っていた。**発行側**に同じ窓を掛ける（`OptimizationRepository.PROGRESS_PUSH_MS`
  を前景・背景の単一ソースに）。窓を `ownsFiles()` **より前**に置いたので、旧コメントが「十分安い」と
  見積もっていた所有権確認のファイル読取も同じ回数だけ減る（1,175回/秒 → 5回/秒）。
  あわせて**所有権の喪失を latch** した（3.385.0 の「喪失は単調」）＝旧実装は所有権を失うと
  `return@handleOptimize` で全部止めていたので、窓を入れたことでバブルだけ出続けるのを防ぐ。
- **[進捗の基準が別データを指していた]** `initHard`/`initSoft` は `loadAsync` でしか書かれず、CSV 取込・
  編集・再実行のあとも**最後に JSON を読んだときの値**を指していた。3.393.0 で足した「最初は N」だけの
  問題ではなく、**旧来の「改善◯%」も同じ基準で出ていた**。keep-best の判定に使う `baseReport`
  （＝この実行の入力盤面）を同じ基準にする。満足度 `satisfaction` も `initHard+initSoft` 由来なので一緒に直る。
- **[docs] `data-models.md` の UiState 一覧が壊れていた**: 3.393.0 で撤去した結果スナップショット8種を
  まだ載せており、自分で置いた**件数チェックサムが合わない**状態だった（82 → **74**）。撤去して
  件数と式を直し、**機械照合**（実装の `val` 74 個すべてが本文に出現・グループ件数の合計が 74）で確認。
- **[docs] 移植対応表を割っていた**: 3.393.0 の注記を**表の途中**に入れたため、以降11行が表として
  描画されず生のパイプ文字になっていた。表の直前へ移動。
- **[docs] `CLAUDE.md` の stale なシグネチャ**: `Evaluator(p, c3RunMode=true)` が撤去後も残っていた
  （README と `docs/` は直したのにここだけ漏れ）。**[軽微]** `MagiViewModel` の未使用 `Evaluator` import。

検証: ホストJVM **全471テスト green**（既知の false positive 1件を除く）。ちらつきの数字は
`golden_state`・45秒・並列8 で PORTFOLIO/ALNS を実走して取得（同じ盤面で現行ロジックと新ロジックの
押し出し回数を同時に数えた）。UI/Worker 層はホストでコンパイル不可＝括弧均衡とシンボルのスコープ逆引き
まで（最終判定は CI）。

## Web互換の撤去・最適化中のちらつき・死んだ配管の始末（3.393.0, ユーザー指示3点）

ユーザーの3点「Web版は存在しないので web互換性は不要」「最適化中の表示のちらつきを配慮する」
「死んでいる流す配管を必要確率があれば配線する」に対応。**エンジンの探索・重み・採否は完全に不変**
（削除したのはどこからも呼ばれない経路、変えたのは UI へ押す頻度と表示だけ）。

### ① ちらつきは「毎秒35回の全面再合成」だった（測ってから直した）
`runV6FullOptimize` の進捗コールバックが `_ui.update` を呼ぶ頻度を実測した:
**golden_state・30秒・並列4 で 1059回＝35.3回/秒**、間隔の中央値 4ms、**96%が50ms未満**。
UiState を丸ごと差し替えるので、`ui` を読む Compose の木が毎秒35回作り直される＝これがちらつきの正体。
- **`UI_PROGRESS_PUSH_MS = 200`** で UI へ押す回数だけ間引く（実測 1059 → **53回**）。
  **エンジンの報告頻度は1つも変えていない**＝停滞ウォッチドッグ・HF63・操作ログのマイルストーン判定は
  従来どおり全コールバックで動く。フェーズが変わった瞬間と必須違反が減った瞬間は窓を待たずに押すので
  応答は落ちない。間引いた回の検査結果は `lastLiveReport` で持ち回る（report は毎回付くとは限らず、
  押す回だけ見ると breakdown が飛ぶため）。最終値は完了時の `_ui.update` と `pushReport` が必ず上書きする。
- `runSoftPolish` は進捗コールバックを持たないので対象外（確認済み）。

### ② Web互換の撤去
`V6WebCompat`（631行・Web版の非DOMヘルパー移植）のうち本アプリが実際に使うのは **4関数だけ**だった。
`ShiftAppearance` へ切り出し、残りは削除した:
Worksheet/Workbook 一式（buildWs1〜7・buildWorkbook・buildScheduleSheetCells・colLetter）／Web側の
undo-redo リデューサ（本アプリは ViewModel の undoStack/redoStack を使う）／Web側の診断ビルダ5種
（本アプリは V6SanityPort/V6PortAnalyzer が担う）／Web版V5のヘルパー（popcnt32・validStartMask・
makeXorShift・hammingDistanceV5・zobristHashV5・V5Flags）／呼出ゼロの雑多ユーティリティ11種。
- **`LightMirrorOptimizer`（95行）も同じ理由で撤去**。ヘッダの docstring 自身が
  「Kotlin port of magi_python_mirror.py」＝Python/Web ミラーの移植と書いており、UI 導線は 3.112.0 で
  撤去済み・本番呼出ゼロだった。役割は `softPolishOnly`（「自動で整えています」）が担っている。
- `invalidAssignmentCount` は**テストのオラクルが唯一の用途**だったので `V6FinalBridgePortTest` へ移した
  （挙動は同一）。`V6WebCompatTest` は Web専用の検証ごと撤去し、生き残る4関数の検証を
  `ShiftAppearanceTest`（4件）へ引き継いだ。

### ③ 死んだ配管＝「使えるものは配線、意味の無いものは撤去」を1つずつ判断
| 対象 | 判断 |
|---|---|
| `itersPerSec` | **配線**。毎回計算していたのに表示先が無かった。進捗行へ「毎秒NK」＝「回数は増えるが遅い（ネイティブ加速が効いていない等）」を画面だけで見分けられる |
| `initHard` | **配線**。必須違反が残っている間は「改善◯%」の枝に入らず、旧表示は `⚠3` だけで進み具合が皆無だった。「最初は69」を併記 |
| `totalViolations` | **配線**。同じ理由で、必須が残る間に減っていく数字が1つも無かった。`合計N` を併記（必須0のときは重複するので出さない） |
| UiState の結果スナップショット8種（`resultSchedule`/`hasResultSnapshot`/result専用マップ6種） | **撤去**。D7（3.120.0＝読取モードはユーザー判断で不要）から一度も読み手が現れなかった。ViewModel 側の `resultSchedule` は「開く前のデータに戻す」等が使う生きた状態なので残す |
| `commitEditingToResult`/`copyResultToEditing` | **撤去**（同じ D7 の族・呼出ゼロ） |
| `start()`（高速計算）・`runLightOptimize()`（軽量最適化）・`generateSimple()`（簡易作成） | **撤去**。UI 導線は 3.112.0/3.126.0 でユーザー判断により撤去済みで、機能は「勤務表をつくる」＋「初期解を作る」＋計算方式の選択が覆う |
| `V6FinalPort.handleSimple` | **撤去**（`generateSimple` の撤去で本番呼出ゼロ）。`GreedyMirrorScheduler` は `SmartInitialSchedulerTest` が**旧生成器との対照**に使う（3.391.0 の回帰テストも載っている）ので残す |
| `Evaluator`/`DeltaEvaluator` の `c3RunMode` | **撤去**。既定 true で `false` は一度も渡されない。**単に未使用なのではなく危険**で、false は 2.31.0 で統一した「単一シフト連は run-deficit」を窓マッチへ戻す＝チェッカーと目的関数を黙って乖離させるスイッチだった |
| `LoadedProblem.nativeHard`/`nativeSoft` | **撤去**。`initHard` を checker の `report.hard` へ寄せた（3.313.0 が initSoft に施したのと同じ単位合わせ。3.318.0 で groupViol が Evaluator の hard にも入り両者は一致するので、別々の計算から取る理由がもう無い） |
| CI の `platforms;android-35`・`build-tools;35.0.0` | **撤去**。compileSdk/minSdk とも 36 で AGP が一度も選ばない |

### 検証
- ホストJVM **全471テスト green**（既知の false positive 1件を除く）。テスト数は HEAD と同数
  （−3 V6WebCompatTest・−1 LightMirrorOptimizer・+4 ShiftAppearanceTest）。
- **教訓#30 の実践**: `severityFromVioKey` の low を scratch コピーでのみ HIGH から外すと
  **`severityFollowsTheWeightHierarchy` だけが落ちる**ことを実行して確認（repo は無傷）。
- **実データで結果が変わらないことを確認**: 後処理研磨は golden **2653/420/c1 96**・
  user **33318/321** と既知ベースライン（3.352.0）に一致。`initHard` の checker↔Evaluator 一致も
  3データセット（0/0・15/15・4/4）で確認。
- UI 層はホストでコンパイル不可＝括弧均衡（6ファイルとも開閉同数）と、導入した全シンボルの
  宣言・使用が `runV6FullOptimize` に属することをスコープ逆引きで確認。最終判定は CI。

## 論理的問題の横断監査＝旗の固着・無言の編集・矛盾するデッド述語（3.392.0, ユーザー指示「すべての論理的問題点などを修正する」）

不変条件が機械で確かめられるものは**全部確かめてから**、実在した問題だけを直した。
**確かめて健全だったもの**も同じ重みで記録する（次に同じ場所を疑わないため）。

### 直した3件

**① 旗を立てて確実に戻さない経路が7つ残っていた（アプリが読取専用に固着しうる）**
`running` を立てる5経路（`loadAsync`/`importCsv`/`generateSimple`/`generateSmartInitial`/`refreshCheck`ほか
チェック3経路）が `catch (e: Exception)` でしか旗を戻しておらず、**`Error`（OOM 等）だと `running=true` が
永久に残る**。3.328.0 で `running` を**14個の編集ガードの根拠**にしたので、固着するとセル編集・一括シート・
Undo/Redo・設定変更が全部閉じたまま＝**アプリが読取専用になる**。3.382.0 が長い4経路
（`start`/`runLightOptimize`/`runSoftPolish`/`runV6FullOptimize`）で `Throwable` へ広げた際の対象漏れ。
`CancellationException` は `Exception` の子孫なので**捕捉範囲に増えるのは Error だけ＝停止の扱いは完全に不変**。

**② `findFixSuggestions` には catch が1つも無かった**
`fixSearching = true` を立てて `viewModelScope.launch` するだけで、探索が落ちると
**「直し方を探す」が探索中のまま二度と戻らない**。`fixSeq` を新設して世代管理する
（`cancel()` は非同期なので、後続の探索が旗を立てた**後**に古いジョブの後始末が走ると新しい探索の旗を消す
＝`refreshCheck` が `checkSeq` を持つのと同じ理由）。`applyAlternative` も catch が無く、
盤面は launch の**前**に差し替わるので、再チェックが落ちると「盤面は変わったのに違反数は前の案のまま」になる
＝理由を必ず残す形へ。

**③ 年間マスター編集13個＋設定ミス修正が操作ログに1行も残らなかった**
対になるスキル群編集（追加/編集/削除/割当）・制約編集・希望編集・回数編集は**全て記録する**のに、
`ws1AddShift`/`ws1EditShift`/`ws1RemoveShift`/`ws1AddGroup`/`ws1EditGroup`/`ws1RemoveGroup`/
`ws1AddStaff`/`ws1EditStaff`/`ws1RemoveStaff`/`ws1SetGroupShift`(担当可否)/`ws1SetGroupApt`/`ws1SetUse2`/
`ws1ResizeDays` と `applySettingFix` が無言だった。**いちばん影響の大きい構造変更が痕跡を残さない**うえ、
3.328.0 は*ブロックされた*編集を記録するので「拒否は残るが成功は残らない」という逆転になっていた。
とくに `ws1RemoveGroup` は所属者を先頭グループへ黙って移す＝**担当できるシフトが変わる**ので人数を必ず出す。
残る無言は色設定8個のみ（表示専用・勤務表に影響しない）。

**④ `Ws1Ops.canRemoveGroup` を削除**（呼出0かつ実挙動と矛盾）
「所属者がいたら削除不可」と返すが、`removeGroup` は所属者を先頭グループへ移して削除するし、
UI が使う `MagiViewModel.ws1CanRemoveGroup` も2グループ以上あれば可とする。名前が「削除できるか」なので
将来これを信じた呼出側は逆の答えを受け取る＝このリポジトリが繰り返し踏んだ
「写した側だけ取り残される」型の地雷だった。

### 確かめて健全だったもの（実測・変更なし）
| 不変条件 | 結果 |
|---|---|
| JSON 往復（parse→serialize→parse） | 26フィールド＋評価値まで**差分0**（実データ2件） |
| 削除・期間変更の index 付け替え | **68操作で新規の参照外れ0**（盤面/希望/個人回数/日別必要人数/群/スキル群/Problem 構築・評価まで） |
| 改善提案（FixSuggester） | **118提案すべて**が適用で `betterReport` 改善・Δ表示も一致・担当外0・希望破り0・重複署名0 |
| CSV 往復（勤務表/職員/希望/制約） | 意味論は完全一致。cons3族の差は**末尾空白の正規化のみ**で、解決後の seq も評価値も同一＝バグではない |
| fair の `m<2` ガード | checker/Evaluator/Delta/C++ の**4面すべてに存在** |

### 検証
- ホストJVM **493テスト green**（既知の false positive 1件を除く）。
- UI 層はホストでコンパイル不可＝括弧均衡（`{`/`}` +55/+55・`(`/`)` +62/+62 で対称）と、
  導入した `fixSeq` の宣言（クラス直下）と使用（`findFixSuggestions` のみ）をスコープ逆引きで確認。
- v6 層（`Ws1Ops`）は実コンパイル＋全テスト実行。

## 実現不能な希望を「固定」と誤扱いする穴を9箇所修正（3.391.0, ユーザー指示「不具合を全て修正する」）

このコードベースが繰り返し踏んできた兄弟バグのパターンを全数 grep し、**実在した9件だけ**を直した。
中心は `wishLocked` 規約（**実現不能な希望＝担当できないシフトへの希望は凍結しない**）の取り残しで、
3.264.0 → 3.270.0 → 3.278.0 → 3.309.0 → 3.311.0 → 3.351.0 に続く7世代目。

### なぜ「固定」にしてはいけないか
`pref` は**実現可能な希望しか数えない**（`MirrorCore`: `w in 0 until K && canDo(i,w) && s[i][j] != w`）。
よって実現不能な希望を持つセルを動かしても **pref は1点も増えない**。逆にそのセルは担当外＝
**groupViol(HARD 10000) が立っている**ことが多く、動かせば必須違反が厳密に減る。
「希望だから動かせない」という直感が、この族に限っては**正反対**になる。

### 直した9箇所
| # | 場所 | 何が起きていたか |
|---|---|---|
| 1-4 | `applyCovOFree` / `applyC41Free`(超過・不足) / `applyC42Free`（V6NativeOptimizer） | 生の `wish == k` で候補から除外＝**必須違反が厳密に減る手を丸ごと捨てていた** |
| 5-6 | `diagnoseCoverage` の capacity と分類（V6PortAnalyzer） | 実現不能な希望を「別シフトへ固定」として capacity から除外＝過小な capacity が verdict を FIXABLE→INFEASIBLE へ倒し「**データ上、充足不可**」という誤った断定を生みうる |
| 7 | 同・covO 分類 | 実現不能な希望を「希望固定で動かせない」と案内（実際は動かせるし、動かせば groupViol も同時に消える） |
| 8 | `GreedyMirrorScheduler` | 担当できないシフトへの希望を**盤面へ置いていた**。`SmartInitialScheduler` は同じ処理で canDo を見ており（3.257.0）、旧世代の生成器だけが取り残されていた |
| 9 | `MagiViewModel.needCellLimits` | **need1 直参照の第5世代**（3.173.0/3.309.0/3.369.0/3.379.0 と同根）。`need1 < 0 → null` で need2 単独定義セルを「対象外」と返していた |

### 実データでの効果（`sample_state_v6.json`＝実現不能希望 9件）
- **生成器**: 空盤面から作ると `groupViol 9→0` / `hard 114→107` / `total 641→617`。
  **`pref` は旧も新も 0**＝実現不能な希望を置いても pref は1点も得せず、9件の HARD だけ払っていたことが数字で出た。
- **診断**: covU 枠(shift 2, day 3) の `担当可能3人→4人`・玉突き `1→2`。過小な capacity が実際に出ていた。
- `golden_state.json`（実現不能希望 0件）は**完全に不変**＝クリーンなデータでは挙動が変わらない。

### needCellLimits は総当たりで検算した（UI 層はホスト実行できないため）
しきい値を `covUCell`/`covOCell` の**実際の発火点**（got を振って観測）と突き合わせ、
need1×need2×use2 の **50通りで mismatch=0**。同じ検算を旧式に当てると **50中10ミスマッチ**で、
穴は need2 単独（→「対象外」）だけでなく **need2 < need1 で lo>hi の逆転レンジ**（画面に「2–0人」）も
出していたと判明した。消費者は集計セルのタップ詳細・必要人数カレンダー・実働チェックの月間需要の3つ。

### 検証
- ホストJVM **493テスト green**（既知の false positive 1件を除く）。新規4件。
- **教訓#30 の実践**: 9箇所の修正を scratch コピーでのみ revert したビルドを作り、
  **新規4件だけが落ち、他488件は両方で通る**ことを実行して確認（repo は無傷）。
- ここまで「pin guard 無し」と出た5関数（HF66/HF67/HF80 系）は**意図的に触っていない**。
  これらは HARD 修復パスで、ピンを守らせると groupViol/covU の修復を止めうる＝
  計測なしに入れてはいけない探索変更（2.55.0/3.310.1 の規律）。事実として記録するに留める。

## SUDO モデルを実装から起こす（3.389.0, ユーザー提示「SUDOモデリング」）

ユーザーがログラス松岡さん（@little_hand_s）の **SUDO モデリング**（S=システム関連図／U=ユースケース図／
D=ドメインモデル図／O=オブジェクト図）を提示。**実装から**4図を起こし `docs/sudo_model.md` として新設した。
**docs のみ＋コメント1件＝エンジン・重み・スコアは完全に不変**（`MirrorCore` の変更は stale コメントの訂正のみ）。

### 作った理由と、この文書の位置づけ
既存 docs は層ごと（design/architecture/business-logic/data-models/screen_spec…）に縦割りで、
**「誰が・何のために・どの型が・実際にどんな値で」を1枚で見る面が無かった**。SUDO はその4面をちょうど埋める。
`algorithm_portfolio.md` と同じ規律（**書くのは実装済みの事実だけ**・構想は隔離）を明記して従う。

### 実測した数字（推定値は1つも書かない）
O 図の素材は `golden_state.json`。`UnifiedViolationChecker.check(state, state.schedule)` をホストJVMで実走し
**hard=0 / total=437 / weightedScore=3109.0**（breakdown: weekly 183・c1 115・c3 36・c3m 36・apt 28・
c3mn 11・low 8・c42 6・c2 4・covO 4・fair 4・high 2、violations 116件・needViolations 4件・countViolations 15件）
まで確認。`weightedScore=3109` は `golden_eval_expected.txt` の `soft=3109` と一致＝**言語跨ぎパリティの固定値**（3.357.0）。
`needViolations` のキーは `"2,8" "2,9" "2,21" "2,27"`＝Dﾃ が 12/9・12/10・12/22・12/28 に2人配置。

### 収集の検証で見つけた自分の誤り（Workflow の集計を信じなかった結果）
4本の収集 + 2本の敵対検証で組んだが、**集計側の返り値は `wrong: 0 / missing: 0`** だった。ツール自身の
guidance どおり `journal.jsonl` を読むと**検証は5件の誤りを検出していた**。集計を鵜呑みにしていたら全部残っていた:
1. **D の不変条件「数値項目は全て String」が誤り**。String なのは**利用者が入力する設定値**だけで、理由は
   「未設定」と「0」を区別するため（`""` と `"0"` は別）。`Staff.groupIdx/skillIdx`・`schedule`・`groupShift`・
   `wishes` の値は `Int`、`use2Patterns` は `Boolean`。**この誤りのまま描くと `groupIdx: String` の図になる。**
2. **S の `OptimizationWorker.kt` の行番号が stale**（+11〜+26）。収集後に自分がそのファイルを編集したため。
   → **この文書は行番号を持たない方針**にした（内容は正しいのに数字だけ腐るのが最も質が悪い）。
3. **U のボタン文言が違う**。実際は「CSV取込」「CSV出力」「データを保存」「職員」「希望」「制約」。
4. **O の cons3n は7行でなく8行**（Dﾃ×5・Cｵ×2・Cｱ×1＝「夜勤・準夜勤の翌日に勤務を置かない」という
   単一の業務ルールを8行へ展開したもの）。JSON を直接数えて確認。
5. **`sample_state_v6.json` は mojibake fixture ではない**＝native-parity CI の2つ目の実データ形状（入力盤面 hard=15・
   3.362.0）。素材から外した判断自体は正しかったが、**理由が誤っていた**ので文書に正しい理由を書いた。

### 副産物＝docs と実装の食い違いを5件発見し、うち4件をその場で修正
| 見つけたもの | 対応 |
|---|---|
| `data-models.md`「`schedule[i][j] < 0` ＝公休（未割当）」が**誤り** | **訂正**。休は `kigou=="休"` で解決される通常のシフト index（`restIdx`）。負値は `normalizeSchedule` が範囲外セルへ付ける**センチネル -1**＝「不正な値」。3.345.0「休は通常のシフト種の一つ」と整合 |
| `data-models.md` が `Staff.skillIdx = -1`（未所属）を書いていない | **追記**（UI の「(なし)」・3.70.0／群削除時の再割当も -1・3.328.0） |
| `data-models.md` のヘッダが stale（`6769806` 時点・2026-06-30） | **更新**。§1・§2 のフィールド表は実装と一致を再確認、**§4 の UiState 一覧はまだドリフト**（`cellFamilies`/`countFamilies`/`needFamilies`/result 専用マップが未記載）と正直に明記 |
| `MirrorKeys.weights` のコメント「窓の要件(c1)=5」 | **訂正**（実装は 15。3.249.0 で 4→5、3.253.0 で 5→15。HF77 の履歴を1行に畳んだ） |
| `business-logic.md` の重み・族数 | **一致を確認**（19族・c1=15・c3mn=15・covO=1.0・c42 の C(n,2)・keep-best の hard→weightedScore→total）＝ここは信用してよい |

### 検証
- **mermaid 4図を実際にパースして確認**（`mermaid@11` + `jsdom` で `mermaid.parse`）。**4/4 OK**。
  あわせて**この検証器が本当に落ちることも確認**（不正なエッジを注入すると exit 1・教訓#30）。
- ホストJVM **489テスト green**（既知の false positive 1件を除く）。`MirrorCore` はコメントのみの変更だが、
  v6 を触ったので main/test とも再コンパイルして実行した。
- O の値は上記のとおりホストJVM実走。S/U の記述は実ファイル・実ボタン文言まで戻って確認した。

## 埋められない穴をログで観測可能にする（3.387.0, ユーザー指示「残っている、埋められない穴などログ強化する」）

これまで「テストでは捕まらない」と記録してきた項目を、**起きたことが必ずログに残る**形にした。
**表示・ログのみ＝重み・採否・探索・停止条件は完全に不変。**

### 埋めた穴
- **[最大] 背景 Worker のライフサイクルが書き出したログにほぼ何も残らなかった**。3.382.0 で前景4経路へ
  入れた終端ログの保証が **Worker だけ対象外**で、出るのは書き込み失敗3件だけ。成功も停止も失敗も、
  そして `!ownsFiles()` の早期 return（＝**所有権の競合が実際に起きた瞬間**）も全部無言だった。
  `doWork()` の全6出口に終端ログを付け、`finally` のフォールバックで `Error` も掬う（3.382.0 と同型）。
  例: `完了（必須4 合計307）／ 手順: 入力退避@0秒→耐久保存@301秒→公開@301秒→片付け@301秒`。
  **手順と経過秒を出すのは、`doWork()` の並び（耐久保存→公開→片付け）が単体テストで守れないから**
  ＝並びが壊れたら次の実機ログで読める、という代替手段。
- **TOCTOU の窓が発火したことを名指しする**。`writeAtomically` の `commitGuard` が偽＝直列化のあいだに
  置き換えられた＝3.385.0 で ms→μs に縮めた窓が**実際に踏まれた瞬間**。旧実装は静かに false を返すだけで、
  理論上の窓が実機で起きるのかを知る手段がなかった。所有権喪失で捨てた進捗の回数も完了行へ併記。
- **`publishLiveBest` の CAS 再試行回数**（`liveBestContention`）。3.385.0 で直した競合が実機で本当に
  踏まれるのかは、合成の競合しか作れない単体テストでは分からない。**0 なら理論上の窓に留まっている／
  非ゼロなら実際に起きている**と言える唯一の実測点。**非ゼロのときだけ**1行出す（毎回出すとログが
  太るだけで、意味があるのは非ゼロのとき）。

### ログでは埋められないと確認したもの（記録）
- `portfolioRoleParallelSa` の効果測定＝**既に観測可能**（3.360.0/3.372.0 の `ロール内チェーンN本`・
  `合計iter`・`全体最良更新`。判定手順は 3.384.0 に記載済み）＝追加不要。
- covU-blocked の fixture（backlog#6 の残り）＝**テストデータの不在**でログでは代替できない。

### 正直な限界
これは**テストではない**。並びが壊れても**次の実機ログを誰かが読むまで気づかない**。TOCTOU の窓も
**閉じない**（発火したことが分かるだけ）。埋めたのは「起きたのに何も残らない」であって
「起きないようにする」ではない。
- 実装中に `MirrorLog` の引数を取り違えて1度コンパイルを落とした（`tag` が必須の data class）。
- 検証: ホストJVM **489テスト green**。Worker はホストでコンパイル不可＝括弧均衡（開閉とも +15/+30 で
  同数）とシンボルのスコープ逆引き（`droppedProgress`/`step`/`terminal` が全て `doWork()` 内）まで。

## Worker の「コメントだけの再発防止」をテストへ＝RunFiles 抽出（3.386.0, ユーザー指示「修正する」）

「外部レビュー痕跡がコードに残っており再発防止が徹底されているか?」への回答を実測したら**否**だった。
コメントは直した記録であって再発防止ではない。**16件中2件しか自動で守られていなかった**:

| 防いでいるもの | 件数 | 内訳 |
|---|---|---|
| テストが落ちる | **2** | `wasDecoded`(MojibakeRepairTest) / `reportComparator` 委譲(AdaptiveEliteArchiveTest) |
| コンパイラ/CI が落ちる | 1 | LocusIdCompat（import 誤りは compileDebugKotlin で必ず落ちる） |
| **コメントだけ** | **13** | A9・A1・UDテーマ・**Worker 11件すべて** |

**最も密に監査された塊（Worker）が自動保護ゼロ**。原因は `ctx.filesDir.resolve` を直接呼ぶため JVM 単体
テストから1行も届かないこと。3.336.0 で「`ownsFiles` はテストしても同語反復」と判断したのは**関数単体
としては今も正しい**が、守るべきは `releasedByMe || ownsFiles()` の順序・原子置換・失敗パスの後始末と
いった **doWork() の並び**で、そこが丸ごと無保護だった。
- **`RunFiles(dir: File)` を新設**（`work/RunFiles.kt`）: 所有権判定・後片付け・原子置換を `Context` から
  切り離す。**ロジックは動かしただけで1文字も変えていない**。`OptimizationWorker` の companion は
  12箇所から呼ばれるので外形を保ったまま委譲（`files(ctx) = RunFiles(ctx.filesDir)`）。3.330.0 で
  `removeSkillGroup` を `Ws1Ops` へ移したのと同じ手＝**テストできる場所へ動かして初めて再発防止になる**。
- **`writeAtomically(target, text, commitGuard)`**: 3.385.0 で入れた「置き換え直前の所有権再確認」を
  引数として外に出した。ガードが偽なら一時ファイルだけ捨て target には触れない。
- **`RunFilesTest` 9件**: 旧経路(runId=0)の互換 / REPLACE で旧実行が所有権を失う / 壊れたマーカーは 0 へ
  倒す / **clear 後にディレクトリが空**（個別の存在確認でなく網羅で固定＝「1つ足して消し忘れる」を防ぐ）/
  原子置換の完全性と一時ファイル残骸なし / 既存内容の完全置換 / **ガードが偽なら target 不変** /
  ガードは一時ファイル書き込みの**後**にちょうど1回。
- **教訓#30 を実践**: scratch で ①`clear()` から snapshot を落とす ②`writeAtomically` をガード前の素の
  `writeText` へ戻す、の2つを注入すると **9件中3件が落ちる**ことを実行して確認（repo は無傷・検証後に削除）。
- **[正直な限界] `doWork()` の並びは依然として無保護**（耐久保存→公開の順序・失敗パスが所有権を閉じること・
  進捗公開前の所有権確認）。Worker のライフサイクル側にあり Robolectric か instrumented test が要る。
  所有確認〜置き換えの TOCTOU（3.385.0 で ms→μs へ縮めたが閉じていない）も同様に単体テストでは捕まらない。
- **併せて実測した3原則**（ユーザー質問「採否は常に checker+keep-best / score不変・退化不能 /
  exhaustive時のみ証明 は全ファイルで徹底か」）: **3つとも成立**。①採否＝checker を呼ばない候補は
  `AdaptiveEliteArchive`(report を保管するだけ)・`V6SearchOperators`(コメントと診断のみ)・
  `C1RepairAnalysis`/`FlexibleDayFlow`(候補生成器＝呼出側が check→isBetter→exactPinRegression の3段)・
  `Ws1Ops`(データ編集)で、いずれも採否地点ではない ②重み定義は MirrorKeys/Evaluator/DeltaEvaluator/
  magi_native.cpp の4面だけ（grep が拾った `Hf63Infeasibility` は族インデックス表、`MagiDashboardCards` は
  日本語ラベル表＝誤検知）③`if (r.exhaustive && r.focusResidual > 0)` で壁を出す。
  **ただし `minFocusResidual` は `acceptableLeaf` の制約を掛けない全葉で測り続けるのが正**（制約下最小に
  変えると壁と判定される窓が増え「false wall を出さない」原則=3.76.0 に触れる）＝「意味論不変」ではなく
  **意図的に母集団を分けている**。ここを混同して統一すると false wall を作り込む。
- 検証: ホストJVM **489テスト green**（480＋新規9）。
- **[併せて] U6＝バブル画面だけ D8 から外れていた**（提示された不具合表のうち唯一実在した項目）:
  `BubbleActivity` が素の `MaterialTheme` を使い、M3 既定の mauve/pink が出て D8（3.121.0＝外観は UD
  高コントラスト固定）から外れていた。`MainActivity` の `MagiTheme` を `private`→`internal` にして共有。
  呼出は本アプリ内の2箇所のみ。表示のみ・スコア不変。
- **提示された不具合表の検証結果（U1〜U10・N1〜N4 の14件中、実在は1件）**:
  - **U1「`cheapSingleRuleLowerBound` の関数本体崩壊・`unavoidable` 未定義」＝Critical と報告されたが不成立**。
    361行に完全な形で存在し、報告書が「推奨修正」として書いた疑似コードと**実装が既に同じ構造**。
    かつ 489テストが通る＝コンパイルできている（未定義ならビルドが落ちる）。
  - U2 skillIdx 境界＝`skillGroups[...]` を skillIdx で引く箇所がコード上に存在せずクラッシュしない
    （実際は「その職員が cons41s/42s から静かに外れる」で 3.328.0 の検査2i が警告済み）。
  - U3 dayCount ジャグ配列＝`normalizeSchedule` が不足を休で埋め範囲外を -1 へ写す（3.199.0/3.278.0）。
  - U5 skillGroups の書き出し漏れ＝スキル群の編集4関数は全て `applyStructure`→`structureEdited=true` を
    立てるので `exportJson()` は `serialize`（skillGroups を書く）へ分岐する。`exportWithEdits` に到達
    するのは制約だけを編集した場合で、そのとき skillGroups は変わっていない。
  - U7 `request` の RMW 競合＝書き込み3箇所は**すべて ViewModel 側**で Worker は読むだけ＝同時書き込みなし。
  - U8 `changedCellCount` のサイズ差＝呼出は1箇所で両方とも同じ Problem の S×T＝構造的に同寸。
    危険なのは報告書が挙げた「other が大きい」側でなく**小さい側（AIOOBE）**だが、こちらも到達不能。
  - U10 `ExactResult(0,0,null,false,0)`＝`minJointC1` は `V6HotfixPasses` から一度も読まれておらず、
    `provenWalls` は `exhaustive=false` で除外する。読みやすさの指摘としては妥当だが欠陥ではない。
  - N1 下界が rangeLo を無視＝制約を無視した下界は**より緩い下界として健全**（早期終了が効きにくいだけ）。
  - N3 bridgePool＝`bridge || hard==ref.hard+1` で doc より広いが、非 bridge の hard+1 は正規に登録された
    エリートなので diversity から返るのは正しい。命名/doc の不正確さ（Low）。
  - N4「`V6HotfixPasses` の存在保証がない」＝同一モジュールでコンパイル時に解決される。
  - 行数は前回同様ズレている（`StateParser` 465→実 256・`OptimizationWorker` 403→実 280 等）。


## 途中最良の publish が「評価」と「盤面」で食い違う競合を修正（3.385.0, 外部レビューの検証から）

ユーザー提示のレビュー（`OptimizationWorker` 10項目）を1件ずつ実コードへ当てた。最後の問い
「`liveBest` の可視性と `OptimizationRepository` のスレッドセーフ性が今一番の潜在バグか?」に対しては
**指摘された理由では違うが、同じ関数に別の実バグがあった**、が答え。

- **[レビューの前提は誤り]** `liveBest` は `@Volatile`（注釈が前行にあるため grep 1行では見えない）で、
  値は `schedule.map { it.toList() }` の**不変な深いコピー**。可視性は保証されており「書き込み途中の
  配列を読む」ことも構造的に起きない。`OptimizationRepository` も `MutableStateFlow`＋`@Volatile` で健全。
- **[実バグ・自分が 3.224.0 で作った取り残し] `publishLiveBest` の CAS が report にしか掛かっていなかった**:
  「report を CAS → **その外で** `liveBest` へ代入」の2段だったため、CAS に勝った直後にプリエンプトされた
  スレッドが、より良い盤面を書いた後続スレッドを**劣る盤面で上書き**できた。結果
  `liveBestReport` は最良なのに `liveBest` は劣る盤面＝**docstring が「退行を防ぐ」と謳っていた
  不変条件そのものが破れる**。実害は誤った勤務表ではない（採用は必ず checker の keep-best が決める）が、
  ①kill 復旧用スナップショット(`magi_bg_best.json`)が最良より劣り**進捗を捨てる** ②ライブ表示の数字と
  盤面が食い違う。**修正**: 評価と盤面を1つの不変オブジェクトにまとめ1回の CAS で publish する
  （盤面コピーは「勝ち目がある」と分かってから1回だけ＝負ける呼出はコピーを払わない）。
- **[教訓#30・想定よりはるかに大きかった] 旧実装は 3/3 で失敗**（8スレッド×400 publish・30×31 盤面）。
  最終盤面に埋め込んだ評価値が最良=1 に対し **987 / 1107 / 494**＝数百段ぶん劣る盤面が残っていた。
  新実装は 5/5 green。「理論上の窓」ではなく実際に大きく外していた。
- **[レビュー #3 も実在＝サイレント死の第3世代] 耐久保証の書き込み3箇所が無言だった**:
  入力・途中最良・完了結果の `writeText` が全て `runCatching { }` で握り潰され、失敗しても書き出したログに
  1行も残らない（3.381.0/3.382.0 で潰したのと同じクラス。これは kill 耐性そのものを担う書き込みなので、
  落ちると「5分回した実行がプロセス終了で消えたのに理由が読めない」）。`OptimizationRepository.notes`
  （`MutableSharedFlow(replay=8)`）を新設して ViewModel が `logOp` へ流す＝このアプリの診断は全て
  書き出しログに集まるので、Android の Log でなくその経路に載せる。**replay を持たせるのは Worker が
  プロセス再起動直後（ViewModel の購読前）に走り得るため**＝購読前の失敗こそ残さないと意味がない。
- **[レビュー #1 TOCTOU＝窓は縮めたが「閉じた」とは言わない]** 所有権の再確認を結果の置き換え直前へ移した。
  縮むのは「直列化(数百KBのJSON)＋一時ファイル書き込み」のぶん＝**ms 級 → μs 級**。**窓自体は消えない**
  （完全に閉じるには run 別のファイル名＝3.336.0 で復元経路ごと作り替えになるため見送り済み）。
  レビューの「二重チェックで十分」は誤りで、二重チェックは窓を**移すだけ**＝そう書いて false assurance に
  しないことのほうが重要。
- **成立しなかった指摘（根拠つき）**: #6 の manifest は 3.127.1 で対応済み・`shortService` は3分上限で
  5分予算に不適合・`android.R.drawable.stat_notify_sync` は公開フレームワークリソースで OEM は消せない ／
  #5 の時計混在は 3.237.0（壁時計へ統一）と 3.375.1（`iters` は目安と明記）で対応済み ／
  #7 の非対称な後片付けは、起動時の復元が resultFile を消費してから読む＋実行中の結果は既にメモリに
  適用済みのため実害なし ／ #4 の所有権チェックのキャッシュは **TOCTOU を μs から1秒へ広げる**ので、
  未計測の省I/Oのために安全側の窓を広げない（この規律は 3.310.1 と同じ）。
- 検証: ホストJVM **480テスト green**（475＋新規5）。新テストは**旧実装で実際に落ちる**ことを
  スクラッチで確認済み（repo は無傷）。UI/Worker 層はホストでコンパイル不可＝括弧均衡（開閉とも同数）と
  シンボルのスコープ逆引きまで。`OptimizationRepository` は Android 非依存なのでホストで実コンパイル・実行した。

## 既定 OFF トグルの「見直しの条件」を明文化＝腐らせない（3.384.0, R-09 解消）

残っていた最後の項目。既定 OFF のトグル4つは、放っておくと**消すのも怖いし試すのも面倒**で残り続ける。
`docs/algorithm_portfolio.md` の「実装済みだが既定 OFF」節へ状態列と**見直しの条件**表を足した。
**docs のみ・コードは1行も変えていない。**

- **期限でなく証拠で決める**。当初ユーザーから示唆のあった「半年で再計測 or 削除」は採らない＝
  半年という数字自体に根拠が無く、期限で機械的に消すと `wideC3nBreakDays`（3件のデータで
  golden=中立/real=改善/user=悪化）のように「**別のデータでは効く**」機構まで失う。代わりに
  トグルごとに「何をもって残す／再測定する／削除するか」を書いた。
- **`portfolioRoleParallelSa` / `portfolioRoleChains` が表に無かった**（3.371.0 で追加したのに
  「実装済みだが既定 OFF」へ載せ忘れ）。4つのうち**唯一の未測定**なので、状態列を新設して
  「測定済み・速度のみ／利得が一貫しない／差を検出できず／**未測定**」を区別できるようにした。
- **決め方（表の要約）**: `filterC3nIncrease`=このまま残す（品質不変・速度のみと確定済み＝再測定不要）／
  `wideC3nBreakDays`=データが5件以上そろったら再測定し、一貫して勝つなら既定 ON・負けるなら削除／
  `adaptiveEscapeControl`=n≈30（約6時間）を誰かが実際に回すまで判定しない・回して中立なら削除
  （設計の筋の良さは残す理由にならない＝2.55.0 の戦略的振動と同じ）／
  `portfolioRoleParallelSa`=**次に実機ログを受け取る機会があれば必ず ON/OFF を1回ずつ取る**
  （3.360.0 で足した `SAチェーンN本`・`合計iter`・`全体最良更新` で判定できる）・測れないまま
  次の大きな改修を迎えるなら削除。
- **規律8を追加**: 既定 OFF を増やしたら同時に条件を1行足す。**条件を書けない機構はそもそも
  既定 OFF で温存する資格が無い**ので、その場で採るか捨てるかを決める。
- 併せて `TuningTelemetry`(3.356.0) との関係を明記＝**ON なのに「この実行では観測なし」が毎回続く
  トグルは、測るまでもなく消してよい**。
- 検証: 5トグルとも `@Volatile`（注釈が前行にあるため grep 1行では見えない）＝並行性の穴は無いことを
  確認。ホストJVM **475テスト green**（docs のみのためテスト不変）。

## 「検証できないと見送った項目」をログで検証可能にする（3.383.0, ユーザー指示）

このセッションで**私が推論に頼るしかなかった**箇所を、ログ側から潰した。**表示・ログのみ＝重み・採否・
探索・停止条件は完全に不変**。

- **[最重要] `stop()` の前景経路にログが1つも無かった**: 背景は「バックグラウンド最適化を停止」を出すのに
  前景は `_ui.update` だけ＝**非対称**。3.381.0 で「4件の異常終了は停止を押した実行」と結論できたのは
  「直後にユーザーが編集を始めている」という**状況証拠からの推論**で、ログには押した事実が1行も無かった。
  `停止を押しました（対象: 計算・改善探索）` を追加。**何を止めたかを区別する**（最適化なのか、
  違反チェック/改善探索だけなのかで意味が全く違う）。
- **[実行が重なったことが読めない] `if (optimizeInFlight()) return` の5入口が無言**:
  勤務表の作成／高速計算／軽量最適化／仕上げ最適化／バックグラウンド計算。押しても何も起きず、
  ログにも残らないので「実行が重なったのか単に押していないのか」を後から区別できなかった
  （3.335.0 で「実際の競合は実機でしか再現できない」と保留した項目の、観測可能な半分）。
  兄弟の `structuralEditBlocked`（3.328.0）は同じ状況を既にログしており**こちらが対象漏れ**だった。
  `runBlockedByInFlight(what)` を新設し `〜を取り消しました（別の計算が実行中）` を残す。
- **[判断材料が数字で出ていない] `Watchdog` の未発火理由に実測値を併記**: 3.375.2 は
  「phaseGrace を並列非依存にするかは要 A/B と業務判断」として頻度の変更を保留したが、ログは
  「猶予2s未達」としか言わず**惜しかったのか桁で足りないのかが読めなかった**＝その判断に必要な材料が
  出ていなかった。`現フェーズ猶予未達(実測0s/2s)` のように a/b 形式へ。3条件すべてに適用。
- 検証: ホストJVM **475テスト green**（v6 側はログ文字列のみの変更）。UI 層はホストでコンパイル不可＝
  括弧均衡（HEAD 比 `{`/`}` +3/+3・`(`/`)` +16/+16）を静的確認。最終判定は CI。
- **ログでは埋められないと確認したもの（記録）**: ①並行アクセスの**実際のレース**は実機/エミュレータ限定
  （観測できるのは上記の「弾いた」側だけ） ②`portfolioRoleParallelSa` の効果測定は実機必須（3.371.0）
  ③3.376.0 の A/B が合成条件だった件は、実機ログの `必須違反 残り0件` と `ワーカー離脱=N/M本` で
  そのまま検証できるため追加不要 ④backlog#6 の「real/user 相当の covU-blocked fixture が repo に無い」は
  fixture の問題でログでは代替できない。

## 終端ログの保証を全経路へ＋Error も拾う＋族分類の取りこぼしを機械固定（3.382.0, ユーザー指示「修正する」「完了・停止・失敗のいずれも記録されるログ強化」）

- **[同じ穴が2箇所に残っていた] `start()`（高速計算）と `runLightOptimize()`（軽量最適化）**も
  `withContext(NonCancellable + Dispatchers.Default)` を checker にだけ掛けており、3.381.0 で直した
  最適化/ソフト研磨と**まったく同型**（停止すると pushReport 以降が飛ぶ）。ハンドラ全体を包み、
  `pushReport` を `runCatching` で囲む形へ統一。
- **[終端ログの保証を全経路へ]** `terminalLogged` + `finally` のフォールバックは 3.372.0 で
  最適化/ソフト研磨だけに入れていた。長い実行4経路すべてへ広げ、**完了ログが無かった
  高速計算/軽量最適化に完了行を新設**（旧: UI メッセージのみで書き出しログには残らなかった）。
- **[Error を拾う] `catch (e: Exception)` → `catch (e: Throwable)`**（4経路）。旧は
  `OutOfMemoryError`/`StackOverflowError` を1つも拾わず、8ワーカー×300秒という重い経路で
  メモリ不足が起きると終端ログすら残らずに消えていた。**再送出しないのは意図的なトレードオフ**:
  `viewModelScope.launch` の未捕捉例外は既定ハンドラでプロセスを落とすため、**その死因を説明する
  操作ログ（メモリ上のリング）ごと失われる**。捕まえれば `OutOfMemoryError` と名指しした行が残り
  書き出せる。状態の一貫性は保たれる（ViewModel の盤面は engine が値を返した**後**にしか書き換えない）。
  代償は「プロセス状態が不明なまま継続しうる」ことで、これは業務判断として受け入れる。
  例外種別も `e.javaClass.simpleName` で出す（旧: `message` だけで型が分からなかった）。
- **[R-08 解消] 族分類の取りこぼしを機械固定**: `MirrorKeys.all` に20族目を足して `vioBuckets` にも
  除外リストにも入れ忘れると、`bucketOfFamily`→null→E7 フィルタで**常に表示**へ静かに落ちる
  （例外も警告も出ない＝利用者から見れば「消したのに消えない違反」）。**Compose 非依存の分類表だけを
  `ui/VioBuckets.kt` へ切り出し**（ロジックは1文字も不変）、`vioBucketlessFamilies`（fair/weekly＝
  場所マップを持たない族）を明示して `VioBucketsTest` が一致を**両方向**で固定する
  （分類漏れ／実在しない族名／族の重複／バケツキーの重複）。
  **教訓#30 実践**＝scratch で weekly を除外リストから外すと `expected:<[]> but was:<[weekly]>` で
  実際に落ちることを確認（以前は「UI層でホスト実行できないので防具が落ちるか確認できない」として
  見送っていた項目を、切り出しによって検証可能にしたうえで入れた）。
- 検証: ホストJVM **475テスト green**（473 + 新規2）。UI 層の残りはホストでコンパイル不可＝括弧均衡
  （HEAD 比 `{`/`}` +22/+22・`(`/`)` +18/+18）を静的確認。最終判定は CI。

### 添付ログからのムーブ検証（コード変更なし・観測の記録）
ユーザー指示「ログから根拠あるムーブアルゴリズムなどを検証する」。**まず判明したのは、この書き出しには
研磨パスの採用/不採用ログ（`C1Polish 採用N回` / `SoftPolishVerify` / `RangePolish 残存`）が1行も無い**
こと＝最適化後の編集で診断が作り直された 3.379.0 のバグそのもの（`lastRunDiagLogs` の退避が効くのは次回以降）。
唯一 `logOp` を通って残る `C1Plateau` から読める事実:
- **8実行×3件の全24行で `ピン破り:0`**。3.256.0 で入れた厳密ピン保護は c1 研磨のボトルネックではない。
- 却下の主因は **low 1321 / c3n 1108** の2つだけ（他族は1件も主因にならない）。
  **候補なし**は 2〜22 と少数＝**手は作れている。負けているのは採点**。
- 対象は毎回 **佐藤直美・モニカの「休」窓**に固定。`c1内訳` も
  `佐藤直美 休(5日窓≥1)10件, 休(14日窓≥4)17件 / モニカ 9件,17件` で一致。
- つまり c1=71 の主因は探索の力不足ではなく、**休を増やすと low(重み90) が増える／禁止連続(c3n)に当たる**
  という設定側のトレードオフ。実際 `設定ミス` は「福澤俊陽の個人下限6件が同時に成立しない（証明つき）」
  「Cｵ の必要数30 > 上限合計24」を出しており、**low が構造的に張り付いている**ことと整合する。
  → **ムーブアルゴリズム側に手を入れる根拠はこのログには無い**（データ側の下限/担当の見直しが正道）。

## 停止処理が丸ごと飛んでいた＝ハンドラ全体を NonCancellable で包む（3.381.0, 実機ログで原因特定）

3.372.0 で「終端ログが無い実行」を拾うフォールバックを入れたが、添付ログ（3.378.0 搭載機）で
**11実行中4件が実際にそのフォールバックへ落ちていた**。ログを時系列で追って原因を特定した。

- **観測**: `最適化 終了: 完了・停止・失敗のいずれも記録されませんでした` が 4件。
  経過は 19s / 19s / 111s / 214s とばらばらで、直前フェーズも RSI covU / RSI apt / RSI c3n と一定しない。
  **共通するのは「その直後にユーザーが編集を始めている」**（例: 11:48:57 終了 → 11:49:25 個人レンジ編集、
  13:50:25 終了 → 13:50:28 セル編集）＝**ユーザーが「停止」を押した実行**。
- **原因**: 停止ハンドラは `withContext(NonCancellable + Dispatchers.Default)` を**checker にだけ**掛けていた。
  この `withContext` を抜けて**既にキャンセル済みの外側コンテキストへ再開する時点で新しい
  CancellationException が投げられる**ため、続く `pushReport` と `logOp("停止: …")` が丸ごと飛ぶ。
  投げられるのが CancellationException なのでアプリは落ちず、`finally` だけが走る＝静かに消える。
- **実害2つ**: ①停止の終端ログが残らない（3.372.0 のフォールバックが拾っていた正体）
  ②**keep-best の結果が `_ui` に届かず、画面は探索中の途中盤面の数字のまま**になる。データは `kept` へ
  戻っているので**表示だけが食い違う**——このハンドラのコメントが「防ぐ」と謳っているまさにその不整合。
  実機ログで裏取り: 11:48:38開始→11:48:57停止の直後、次の違反チェックが **必須=69** なのに
  停止直前の表示は **必須=3** だった（初期解 必須=69/493 が kept、探索中の表示が 3/457）。
- **修正**: 停止ハンドラ**全体**を `withContext(NonCancellable) { … }` で包む（最適化・ソフト研磨の2箇所）。
  さらに `pushReport` を `runCatching` で囲み、診断が落ちても **UI メッセージと終端ログだけは必ず残す**
  （原因に依存しない保証＝3.372.0 と同じ狙いを、今度は原因の側で閉じる）。
  フォールバックの文面も推測（「診断の実行中に例外」）から実態（「想定外の経路。Error(OOM等)や停止処理
  自体の失敗が疑われます」）へ。
- **未対応（記録）**: `catch (e: Exception)` は `Error`（OOM/StackOverflowError）を拾わない。今回の4件は
  上記の CancellationException 経路で説明がつくが、Error 経路が残っているのは事実。握り潰すか落とすかは
  業務判断（握り潰すとプロセス状態が不明なまま継続する）ため、フォールバックの文面で示唆するに留めた。
- 検証: v6 層は無変更＝ホストJVM **473テスト green**。UI 層はホストでコンパイル不可のため括弧均衡
  （HEAD 比 `{`/`}` +16/+16・`(`/`)` +11/+11＝追加した2つの `withContext`＋`runCatching`/`onFailure`＋
  文字列テンプレートと厳密に一致）と、`terminalLogged` がクロージャから可視であることを静的確認。最終判定は CI。

## covO の違反詳細が「場所数」を「件数」として出していた（3.380.0, 添付ログから）

添付ログ（3.378.0 搭載機・必須6/合計429）を数字の整合で総当たりし、**同じ report の中で矛盾している行**を1件見つけた。
**読み取り専用・表示のみ＝重み・採否・探索は完全に不変。**

- **証拠（同一ログ内の3行）**: `UnifiedCheck … covO=23` ／ `CoverageDiag: 人員過剰 合計23 — 14枠` に対し
  **`違反詳細 covO(14件)`**。他の族は 3.282.0 で `件数23・場所14箇所` と書き分けているのに、covO/covU の
  被覆セクションだけ `emit(byFam, DETAIL_CAP)` と **`fires` を渡していなかった**（3.282.0 の取り残し）。
  covO は1枠が最大で人数ぶん超過しうる（実機は 9/23 の休が1枠で4件）ので両者が大きく食い違う。
  covU は 3件/3枠 でたまたま一致するため気づきにくかった。
- 回帰テスト1件（1日・休 need1=0 に3人＝1枠3件）。**教訓#30 実践**＝fix を scratch で revert すると
  **この1件だけ**が落ちることを確認。

### 添付ログで「矛盾に見えるが正しい」と確認したもの（変更なし）
| 観測 | 判定 |
|---|---|
| `需給注意 B4: 需要7 … 現状10 → 現状10(需要7)→不足1(covU)` | **正しい**。需要/現状は月合計、covU は日次 `covUCell` 合計（3.274.0）。9/11 だけ0人＝月で余っていても日で不足する。ただし月合計と日次を1つの矢印に並べる書式が誤読を招く＝表示の改善余地（P2・未対応） |
| `構造HARD下限=0` なのに `Cｵ: 全3名の上限計24<需要30→構造的に不足` | **正しい**。`structuralHardFloor` は**担当可能人数**ベース（Cｵ は3名×30日で足りる）。上限は SOFT なので床に入らない（3.354.0 と同じ理屈）。実際 Cｵ の covU は0で、代わりに high 12件として現れている |
| `適切回数計20 現状13` の Pｼ に検査6-C が沈黙 | **正しい**。3.301.1 の `hasDemand` ゲート。Pｼ は需要0＝席の概念が無く、担当1名が20日 Pｼ に就けば目標は達成可能＝矛盾ではない |
| `違反詳細 c1` の場所が 有/A4/Aｱ なのに `c1内訳` は全部「休」窓 | **正しい**。窓ルールは休の不足、mark はその run 先頭セル（＝休でない日）に立つ |

## need1直参照の第4世代（修復オペレータ2つ）＋最適化診断がログから消える（3.379.0）

ユーザーが「論理的バグありますか？」と**候補7件のリスト**を提示。**同意する前に全部 grep で裏取り**し、
実在した1件を修正した。あわせて実機ログ（3.378.0 搭載機）と /code-review の指摘を消化。

### 実在した論理バグ＝need1 直参照の第4世代（HARD に効く）
`destroyRepairDayAt` / `destroyRepairStaffAt`（**RSI/ALNS 修復の中核**・2.57.0 以来の soft-aware repair）が
`p.need1[k][j] <= 0 → continue` で **need2 単独定義の需要を丸ごと素通り**していた（need1 未設定は -1）。
そのデータでは covU(HARD, 重み8000) を修復オペレータが**原理的に埋められない**。
3.173.0(CoverageDiagnosis)・3.309.0(isBalanceable)・3.369.0(初期解生成2つ+findCovOFix) で同じ穴を潰しながら、
いちばん熱い2関数が取り残されていた。source of truth の `covUCell`（片方定義=その値）へ委譲。
`V6PortAnalyzer.totalDemand` / `buildDayRisks`（表示）も同型で同時修正。
- **回帰テスト2件**（need2 単独定義 fixture）。**教訓#30 実践**＝fix を scratch で revert すると
  **この2件だけ**が落ちることを実行して確認。
- **ユーザーの推定サイトは外れ**: 「C1Polish/RangePolish/C1JointLNS/C3nPolish にも残る」は grep で **0件**
  （これらは need1 を1箇所も読まない）。実際の残りは上記4箇所だけだった。
- **`grep .need1` を CI で落とす案は採らない**: 96件中78件は `Shift.need1`＝**設定値そのもの**（UI編集・
  StateParser・JSON往復）で、need1 を読むのが正しい。禁止すべきは `p.need1[k][j]` の行列読み18件だけで、
  そのうち Problem.kt 自身・NativeEval の平坦化・V6SanityPort の設定診断（need1 と need2 を別々に見せるのが
  仕事）は正当。機械禁止は false positive のほうが多い。

### 裏取りの結果（残り6件＝いずれも追加バグなし）
| 指摘 | 検証 |
|---|---|
| c1壁の休ガード忘れ | `k == p.restIdx` 分岐が5箇所で実在。3.364.0 のテスト2件が非休=壁と言わない／休=言う の両方向を固定済み |
| hardFloor の二重真実 | 直読みは2箇所のみ（`V6FinalPort:320` ウォッチドッグ閾値・`V6NativeOptimizer:1870` RSI covU avoid=3.95.0）。**どちらも供給床の用途で正しい**。3.377.0 が直したのは残存分析の表示だけで、この2つを blockedNow にすると 3.361.0 で実測却下した早期終了配線に戻る |
| keep-best 順序のコメント | 3.366.0 で12箇所修正済み。単一ソースは既に `MirrorCore.reportComparator`（3.352.0）＝コード生成は不要 |
| 19族の 17 ハードコード | `vioBuckets` は**数字でなく族名の列挙**（6バケツ×族集合）。17 はコメントのみ。ただし新族をバケツに入れ忘れると `bucketOfFamily`→null→「常に表示」に**静かに**落ちる（下記） |
| 並列SA 既定OFF | 事実（`portfolioRoleParallelSa=false`）。論理バグでなく未測定機能 |
| ログの2.5秒ゲート | 3.378.0 で修正済み・本セッションの実機ログで実効を確認 |

### 実機ログ（3.378.0 搭載機）で確認できたこと・見つかったこと
- **3.378.0 の軌跡修正は実機で機能**（`グローバル最良更新 W4 epoch3（経過114秒・必須4 合計452）`。
  同一秒の複数更新も残る＝旧2.5秒ゲートなら落ちていた）。3.374.0 の検査6d も発火（大島愛の休）。
- **[実バグ] 最適化の診断がログから丸ごと消える**: 14:04 に最適化 → 14:16-17 に希望を編集 → 20:50 に書き出し、で
  **診断67件が違反チェックのぶんだけ**（TIME/スコア収支/Watchdog/残存分析/AdaptivePortfolio が全部ゼロ）。
  `rawDiagLogs` は `pushReport` のたびに差し替わり、`refreshCheck` も pushReport を通るため。
  「作る→見る→直す」という実際の使い方ではほぼ必ずこうなる＝**ログ強化の成果が全部無効化されていた**。
  `lastRunDiagLogs`（エンジン実行のときだけ退避）を新設し、書き出しで別セクション併記（JSON も同様）。
  `pushReport(runLabel=…)` をエンジン結果を押す6サイトにだけ付ける（keep-best/停止パスは付けない＝
  前の実行の診断で上書きしない）。
- **[実バグ] 軌跡に重みが無く読めなかった**: 実機の軌跡が 540→522→482→430→**467**→452 と途中で合計が
  増え「最良更新なのに悪化」に見えた。keep-best は hard→weightedScore→total なので正しい取引だが、
  重みが無いと確かめられない（スコア収支と同じ理由）→ `重みW` を併記。

### /code-review 指摘3件（全て実在・修正）
- 「変化なしに費やした段」が**探索**を写像しておらず、いちばん時間を食う段が `(±0)` でも名前が出なかった。
- ExtraRefine 未実行ログの「予算残」が `postReserveMs` でクランプ済みの値＝**未使用予算を過小報告**
  （300s 予算で 150s に停滞終了なら実際は約145s 余るのに「予算残25s」）。実測の残りを主に出す。
- README の 3.378.0 と 3.377.0 が段落として繋がり ① が2つ並んでいた。

### 残った構造リスク（コードは正しいが将来のために記録）
新しい族を `MirrorKeys.all` に足して `vioBuckets` へ入れ忘れると、E7 フィルタで**常に表示**へ静かに落ちる
（例外でも警告でもない）。テストで固定できるが `MagiScheduleViews` は UI 層＝ホストで実行できず
**「そのテストが本当に落ちるか」を確認できない**ため、今回は追加せず機構だけ記録する（教訓#30 の適用）。

## デバッグできるログへ＝スコア収支・改善の軌跡・沈黙していた追加精製（3.378.0, ユーザー指示「デバッグできるようにログを強化する」）

実機ログ（`be79eee0`・3.370.0搭載機・300s）を**自分が実際に追えなかった順**に潰した。
**読み取り専用・ログのみ＝重み・採否(keep-best)・探索・停止条件は完全に不変。**

- **[最大の穴] 段をまたいだスコアの収支が追えなかった**: 各段が自分の before→after を別々の行で出すが、
  **母集団が繋がっていない**。実機ログの実例:
  `AdaptivePortfolio「採用 total=307」` → `EliteIntegration「307->307」` → `SoftPolishVerify「**299**->299」`
  → `C1JointLNS「**295**->295」` → `PersonalJointLNS「295->294」` → `UnifiedCheck「合計=294」`。
  **307→299 と 299→295 がどの段で起きたのか1行も無い**（POST 行は時間しか出さない）。
  `スコア収支` 行を新設し、入力→探索→統合→後処理→追加精製→採用の**各段の採用値を同じ物差しで**並べる。
  **重みも出す**のが肝＝keep-best は hard→weightedScore→total（3.287.0）なので、
  「total は同じなのに採用=1」（EliteIntegration）が矛盾でなく weighted の改善だと読める。
  実測でも golden 75s が `後処理 0/380/w2296(合計+2・重み-50)` と出て、**total が増えたのに採用された理由**が
  1行で説明できるようになった（旧はバグに見える）。あわせて**時間を使ったのに1点も動かなかった段**を
  名指しする（`／ 変化なしに費やした段: 追加精製7s`）＝どこを削れるかの判断材料。
- **[実バグ] 改善の軌跡が消えていた**: 診断は `全体最良更新=17回` と言うのに、操作ログに残った
  `適応portfolio グローバル最良更新` は**7行だけ**。3.283.0 は「最良更新・改善は情報価値が高いので
  同名60秒窓の対象外」としながら、**その手前の一律2.5秒ゲート（`lastPhaseLogMs` は全フェーズ行で共有）で
  先に弾いていた**＝10回が無言で落ちていた。しかも**行に値が無い**ので、残った7行からも
  「いくつになったか」が読めない（スコアの数字は最初の必須改善行と最後の完了行の2点だけ）。
  ①important 行は2.5秒ゲートも外す（300秒で17行＝スパムにならない・3.283.0 の「重要イベントを
  押し出さない」意図そのもの）②`・必須N 合計M` を併記。これで 366→…→294 の軌跡が追える。
- **[実バグ] 追加精製が沈黙していた**: `ExtraRefine` は**改善したときだけ**ログしていたため、
  実機ログは `TIME` に `追加精製12.007s`（予算の4%）と出るのに **ExtraRefine 行が1行も無い**＝
  効果0なのか実行されなかったのか区別できなかった。改善なしの1行を追加。あわせて
  **予算が残っているのに走らせなかった**ときも理由（停止要求／停滞検知で早期終了済み／違反が残っていない）を出す。
  判定は1回だけ評価して分岐と説明で共有する（`isActive` を2度読むと食い違い得るため）。
- **[HF77]** `logOp` の KDoc「最大300件」が実装（1000）と食い違っていた。「自分の行がリングから
  押し出されたのか」を判断する材料なので実装値へ訂正。
- 検証: ホストJVM **470テスト green**。3つとも実データ／実機同型の合成盤面で**実出力まで確認**
  （`ExtraRefine: 予算残7sで追加精製: 改善なし` ・ `スコア収支 … ／ 変化なしに費やした段: 追加精製7s`）。
  軌跡の修正は UI 層＝ホストでコンパイル不可のため、括弧均衡（HEAD 比 `{`/`}` +2/+2・`(`/`)` +2/+2＝
  追加した式と厳密に一致）とスコープの静的確認まで。

## 残存分析が「もう直せない covU」を見落としていた＋Watchdog の時間軸混在（3.377.0, 実機ログ 2026-08-15 から）

新しい実機ログ（3.370.0 搭載機・10名/30日・必須=2 合計=294）は、**8/8本が締切まで走り worker秒2199s・
合計iter 6.6億**＝HARD=0 に到達しないので勝者機構が発火せず並列は正常、という 3.376.0 の分析と補完的な
裏づけになった。そのうえで、**同じログの中で3つの診断が食い違っている**のを見つけた。
**読み取り専用・表示のみ＝重み・採否(keep-best)・探索・早期終了は完全に不変。**

- **[実バグ] `残存分析` が「いまの希望では埋められない covU」を「まだ狙える」に入れていた**:
  同じ実行の中で `CoverageDiag` が「充足可能2枠（うち2枠は いまの希望のままでは不能）＝**この希望・担当の
  ままでは人員不足は減りません**」と出し、設定ミス診断（検査9=ConstraintMus）が同じ 9/25(金)・9/29(火) を
  **「証明つき」**で名指ししているのに、`残存分析` だけが `covU 2件` を「まだ狙える」へ入れていた。
  原因は covU の構造判定が **`hardFloor`（有資格者数ベースの静的下限）しか見ていない**こと。この実行は
  `構造的HARD下限=0`（担当者は足りる）なので、判定が素通りしていた。3.375.0 で直した weekly の二重計上と
  同型（床を open から差し引かない／そもそも床を持たない）。
  **修正**: 3.344.0 の `CoverageDiagnosis`（`blockedNow`＝空き番なし・玉突き連鎖も `findCovUChain` で
  不成立を実証）を**単一ソース**として読み、供給床と「いま埋められない」量の大きいほうを壁として扱う。
  ここで再実装すると必ずドリフトするので委譲に徹した（`covUBlockedAmount` / `covUStructuralWall` を
  `internal` で切り出し）。**3.361.0 の実測（早期終了は keep-best-safe でない＝`allBlockedNow` を
  ウォッチドッグへ配線するのは却下）はそのまま維持**＝ここは表示だけで、探索・停止条件には一切配線しない。
  実測（実機と同型の最小盤面＝構造床0・希望固定で玉突き不成立）: 旧「まだ狙える: covU 1件」→
  新「もう直せない: covU 1件(いまの希望・担当のままでは埋められないと実証済み) ／ まだ狙える: なし」。
- **[実バグ] `Watchdog` 行が2つの時間軸を1文に混ぜていた**: 「最終改善=経過287s・**探索終了時**の停滞0s」
  なのに探索は274sで終了、という自己矛盾。`lastBestImproveMs` を**出力時**（後処理・追加精製のあと）に
  読んでおり、ExtraRefine(3.102.0)の改善も `progressWatch` を通るので tChain1 より先へ進み、
  `tChain1 - lastImp` が負→0 に丸められていた。読み手は「探索は一度も停滞していない」と誤読する。
  **修正**: 探索終了時点で `lastBestImproveMs` / `lastPhaseChangeMs` / 反復数をスナップショットし、
  ウォッチドッグの数字（停滞・反復・未発火の理由3条件）を**全てこの時刻基準に揃える**。探索の後で
  改善したなら `・探索後も改善あり(経過Ns＝後処理/追加精製)` として**別項目**に出す。
- 検証: ホストJVM **470テスト green**（469 + 新規1）。新テストは **fix を scratch で revert すると
  この1件だけが落ちる**ことを確認済み（教訓#30）。fixture は `cascadeChainState(cWished=true)`＝
  担当者は足りる(FIXABLE)が希望固定で玉突きが完成しない形＝まさに hardFloor では捉えられない実機の形。
  **repo の2 fixture（golden/sample_v6）は covU=0 なのでこの分岐は no-op**（backlog#6 に記録済みの
  「blocked-now な covU 形状が repo に無い」の続き）＝実機と同型の合成盤面で end-to-end 出力まで確認した。

## 並列を本当に動かす＝HARD=0 到達時の「残りを即キャンセル」を撤廃（3.376.0, ユーザー指示「ワーカー、並列が本当に動くようにする」）
3.375.0 は「入口が既に HARD=0」の場合だけを塞いだが、**HARD>0 入口でも同じ潰れが数秒遅れて起きる**
（同じ `hardZeroWinner` 機構）。実機ログは初期解 HARD=64 から **1秒で HARD=0** に到達しており
（13:53:53→13:53:54）、そこから 195 秒を実質1並列で走っていた。
- **撤廃した機構**: `runAdaptivePortfolio` と `runMultiWorker` の両方が持つ「先に HARD=0 へ到達した
  仮説が残りを即キャンセル」（docstring は省電力が理由と明記）。**HARD=0 到達時点で残る仕事は全部 SOFT**
  なので、勝者1本に絞ると利用者が指定した並列度の 1/8 しか使われない。キルを外し全ワーカーを締切まで
  走らせる。`hardZeroWinner`/`winner` は「誰が最初に到達したか」の記録としてのみ残す（ログの情報価値）。
  `exitReason` の「勝者確定」は到達不能になったため撤去。3.375.0 の `hardRaceArmed` は本変更に包含され削除。
- **安全性**: 採否は全段 keep-best（`better()`）なので探索を増やしても品質は退化しない。代償は電池/発熱だが、
  総時間は予算と停滞ウォッチドッグが従来どおり govern する。
- **A/B（golden から cons1/cons3n を外し HARD=0 へ到達させた形・30秒予算・各2回）**:
  worker秒 **22s → 101s（4.6倍）**／合計iter **338万・632万 → 2,888万・7,437万（8.5〜11.8倍）**／
  ワーカー離脱 **1/8本 → 8/8本が締切@21s**。weighted は 661,706 → 678,700 で**中立**（2回ずつ＝差はノイズ）。
  得られたのは品質でなく「指定した並列度が実際に使われる」こと。
- **[測れなかったこと]** 手元の fixture では素の PORTFOLIO が HARD=0 へ到達しない（golden の greedy 初期解
  hard=15 → 30秒で hard=1 止まり／sample_v6 も hard=1 止まり）ため、実機と同じ「HARD>0 入口から数秒で
  HARD=0」の経路そのものは A/B できていない。上記 A/B は cons1/cons3n を外して到達させた合成条件。
  確かなのは構造的な帰結（キルされなくなる）と、その条件下での並列度・反復数の実測。

## HARD=0 入力で8並列が実質1並列に潰れる＋残存分析の二重計上＋停滞ログに反復数（3.375.0, ユーザー指示「ログから新しい不具合を見つける」「停滞脱出のログを強化する。イテ回数と時間を出す」）
実機ログ（3.370.0 / 287操作・75診断）を数字の矛盾から潰して**実バグ2件**を特定・修正し、あわせて停滞脱出の
ログへ反復数と時間を追加した。

- **[実バグ・最大] HARD=0 の盤面から再実行すると PORTFOLIO が実質1並列に潰れる**: `hardZeroWinner`
  （先に実行可能へ到達した者が勝ち＝他を止める）のレースは、**入口が既に HARD=0 だと意味を持たない**
  （全員が最初から HARD=0）。旧実装は最初の進捗報告で即座に勝者が立ち、残る全ワーカーが1エポックも
  走らずに離脱していた。実機ログの証拠: `W6:epoch0/再配属0[]/離脱=勝者確定@0s`（1エポックも走らず）・
  **8本中7本が @0s 離脱**・`役割別worker秒(計74s)`・`全体最良更新=0回`・`total 282->282`。
  **利用者が「並列8」と設定しても実質1並列**。HARD=0 到達後に残るのは全部 SOFT の仕事なので止める理由が無い。
  入口が HARD>0 のときだけレースを武装する（`hardRaceArmed`・従来経路は挙動不変）。
  **A/B（golden_state・入口 hard=0・45秒予算・同一条件）**: ワーカー離脱 `勝者確定7本@0s` → `探索締切8本@36s`／
  worker秒 **22s → 205s**／合計iter **1,090万 → 5,447万（5.0倍）**／全体最良更新 2回 → 5回／
  相異なる解 7本(同一解あり) → **8本**／total **401 → 371**／weighted **2487 → 2372**。
  ※品質差は1 run のため断定しないが、worker秒と反復数の差は機構の決定的な帰結でノイズではない。
- **[実バグ・表示] `残存分析` が構造床を二重計上**: 同じ1行が「もう直せない: weekly のうち57件」と
  「まだ狙える: … weekly 159件」を同時に出し、**57+159=216 > 全体159** という自己矛盾。床を後から walls へ
  足すだけで **open 側から差し引いていなかった**（`weekly内訳` 行は正しく 159 = 床57 + 減らせる102 と出しており、
  この行だけが食い違っていた）。床を族ループより**先に**計算し open から差し引く。apt+high は床が**2族の和**に
  対して立つ（片方に割り振れない）ので、床が立つときは open 側も `apt+high` の1項目にまとめる。
  実測で `weekly 83+66=149`・`apt+high 19+9=28` と合計が一致することを確認。
- **[3.375.1/自分の数字を実測で検証・是正] 「無改善の回数」は総量ではなく報告ぶんの目安だった**:
  ユーザーの「無改善の回数は適切か?」を受けて golden_state を30秒予算で3回計測したところ、
  Watchdog の `探索終了時` は 1,734/2,615/2,898万回に対し AdaptivePortfolio の `合計iter`
  （各役割の返り値を厳密に合算した真の総量）は 2,950/4,620/5,972万回＝**捕捉率 49〜59%**。
  各役割が返す最終反復数まで進捗報告が届かないため構造的に不足する。さらに停滞窓内のレートは
  全体レート比 **0.5〜2.1倍**にぶれる（並列ワーカーの報告が改善の後にまとめて届き、その多くは
  改善と並行して行われた仕事のため）。**3.375.0 で「合計iterと桁が一致」と書いたのは1サンプルの
  まぐれで、検証が弱かった**。数字自体は桁の区別には使えるので捨てず、ラベルを
  「反復(進捗報告ぶん・目安)…総量はAdaptivePortfolioの合計iter参照」へ是正し、実測値と用途
  （「そもそも回していない」と「大量に回しても改善しない」の桁の区別に限る）をコードに明記した。
  あわせて停滞リセット時に `stagnationIters` も戻す（次の発火で上書きされるため実害は無いが非対称だった）。
- **[3.375.2/自分の修正の副作用を実測で発見] hardRaceArmed 修正が停滞ウォッチドッグをほぼ無効化していた**:
  ユーザーの「停滞脱出も賢く適切に」を受けて golden_state を実測したところ、**150秒予算で
  `停滞47s`（閾値18s）なのに `発火=なし` で予算を使い切る**（2回中1回は146秒でようやく発火）という
  観測を得た。原因は `phaseGraceMs`(予算/40・2〜15s) のリセット判定が **`"/ " 以降＝内側のフェーズ名**
  （"V5 SA"/"ALNS restart 1/1"/"RSI apt"…）を見ており、これは**8本のワーカーで共有される**こと。
  並列が本当に動くと base が絶え間なく入れ替わり `now - lastPhaseChangeMs` が猶予を超えない。
  **3.375.0 で 7/8 のワーカーを生き返らせた副作用**（修正前は W1 のフェーズしか出ず猶予を満たしていた）。
  **頻度は変えない**判断: ①予算を使い切る挙動は利用者の指定どおり ②3.341.1 の実測で早期終了を減らす方向は
  品質にわずかに有利（中央 −3.5%）③firing を戻すのは品質と電池の交換＝業務判断。
  代わりに **`Watchdog` 行へ「未発火の理由」**（最短実行未達／現フェーズ猶予未達（＋その理由）／停滞が閾値未満）を
  追加し、ログだけで「なぜ停滞47sで止まらないのか」が読めるようにした。実測で
  `未発火の理由=現フェーズ猶予2s未達(並列ワーカーがフェーズ名を共有し頻繁に更新されるため満たしにくい)＋停滞が閾値未満`
  が出ることを確認。**判断待ち**: 早期終了を意図どおり効かせたい（電池・待ち時間を優先する）なら
  phaseGrace を並列非依存にする改修が要る＝別途の A/B と業務判断。
- **[ログ強化] 停滞脱出に反復数と時間**: 時刻だけでは「そもそも回していないから改善が無い」のか
  「大量に回しても改善が無い」のかが区別できず、停滞閾値の妥当性を実機ログから判断できなかった。
  `Watchdog`/`EarlyStop` に `反復=最終改善時4,600万回→探索終了時5,225万回（無改善のまま625万回転）` を追加。
  **[自分の計測誤りを実測で是正]** 初版は「観測した最大反復数」で数えたが、各ワーカー/フェーズは**自分の
  カウンタ**を報告する（役割が変わると 0 から数え直す）ため最大の1本で頭打ちになり、実測で
  `無改善のまま0回転` と出て誤りに気づいた。フェーズ文字列ごとの増分を足す方式へ是正
  （合計iter 5,4xx万と桁が一致することを確認）。あわせて回数はあっても**時間が無かった**2箇所へ所要時間を追加＝
  結合探索（`200通り試行/Nms→無駄打ち回避で早期終了`）・C1広域ビーム（`steps=43/1760ms/最良が20手更新されず打ち切り`）。
- 検証: ホストJVM **460テスト green**（既知の false positive 1件を除く）。読み取り専用のログ追加と、
  レース武装条件の限定のみ＝重み・採否(keep-best)・探索オペレータは不変。

## 希望ロックで達成不能な適切回数を事前診断する＝検査6d（3.374.0, ユーザー指示「全て修正する」）
3.373.0 で「state が無いと特定できない」と保留した大島愛の apt 停滞（休17回・目標10）を、**ログから
実データの構造を再構成して測り、原因を確定させた**。需給行が全シフトの需要・担当数・下限計・上限計・
適切回数計・現状を出しているため、大島の形（休/Pｼのみ・休17/Pｼ13・目標10/20・Pｼ は需要0で担当が本人
だけ・c1 休窓 5日≥1/14日≥4）は合成できる。
- **測定＝希望件数と停滞点が単調に対応し、17件で実機と完全一致**（`applyAptPolish` の到達点）:
  希望0件→休10(apt 0・採用5手) / 10件→休15(apt10・採用2手) / 15件→休16(apt12・採用1手) /
  **17件→休17(apt14・採用0手)**。実機の観測（休17・apt停滞・手が1つも通らない）を再現した。
- **結論＝最適化の不具合ではない**。希望を破る代金は pref=9000 で、apt=1 の利得では絶対に釣り合わない
  （`wishLocked` は探索・研磨の全パスが尊重する＝3.270.0/3.351.0 で統一済み）。**希望どおりに置くのが正解**。
- **実害は「理由を一言も説明していない」こと**。6b は担当レパートリー由来の下限のみ、検査9(ConstraintMus)は
  個人上限との矛盾のみを見るため、この形（個人上限なし・希望が apt 目標を超える）は素通りし、
  利用者には理由不明の直せない apt 違反が残っていた。
- **検査6d を新設**: シフト k について担当可能な希望が W 件あれば `count(k) >= W` がどの解でも成立するので、
  W > apt目標 t なら超過は W−t 回ぶん必ず残る（証明可能な下界・誤検知ゼロ）。
  「『休』の希望が17件あり、適切回数の目標10回を超えています…差7回ぶんの超過は最適化では消せません」＋
  「目標を17回以上にするか、希望を7件減らしてください」を SettingIssue チャネルへ。read-only・データ不変。
- 検証: ホストJVM **460テスト green**（新規2件）。教訓#30 実践＝6d を scratch でのみ潰すと
  `wishLockedCountAboveAptTargetIsReported` **だけ**が落ちることを確認（repo 本体は無傷）。

## 実機ログ(2026-08-15)起因の2件＋compileSdk 37 の可否確認（3.373.0, ユーザー指示「修正する？」で4件全選択）
実機ログ（3.370.0 / Pixel 10 Pro XL / 10名30日）の解析で挙げた4項目をユーザーが全選択。**測れるものは測り、
測れないものは正直にそう記録する**方針で処理した。**読取専用・ログのみ＝重み・採否・探索は完全に不変**。
- **[実バグ・観測性] 終端ログの欠落**: ログに「最適化 開始」だけあって**完了/停止/失敗のどれも無い実行が2件**
  あった。全経路が logOp を持つはずで、ログリング溢れでもない（前後のより古い行が残っている）。コードを
  追うと **停止分岐の logOp が `pushReport` の後ろ**にあり、`pushReport(nonCancellable=true)` は
  `analyzeParallel` を NonCancellable で包むが**例外は素通りする**＝診断が落ちると 停止ログに到達せず
  `finally` も何も残さない。原因に依存しない形で閉じるため `terminalLogged` を導入し、`finally` で
  終端行が1つも出ていなければ必ず1行残す（3.271.0「サイレント死の防止」と同じ狙い）。
  **併せて `runSoftPolish` の停止分岐には logOp が1つも無かった**（最適化側の「停止: …を保持」に対する
  対象漏れ＝停止すると終端行がゼロ）ので追加。`start()`/`runLightOptimize()` は開始も完了も記録しない
  設計のため対象外。
- **[実バグ・診断の食い違い] 需給行が「置ける枠」を無視して圧力を過大報告**: 実機で B4 が
  `供給圧力7(適切回数)>需要2` と出るのに、利用者に見える設定ミス検査6-C は沈黙＝**同じ問い「目標は席に
  収まるか」に2つの診断が違う容量定義で違う答えを出していた**。精査すると**沈黙している側が正しい**＝
  need2 の枠内なら7人置いても covO は増えない（need1 合計との比較は use2 有効時に自由枠を無視する）。
  需給行の比較先を 6-C と同じ `seatsHi`（Σ(use2&&need2>=0 ? need2 : need1)）へ是正。
  golden/sample で**出力は完全に不変**（Dﾃ の `供給圧力35>置ける上限31` は need1=need2=31 で正当・
  実際に covO=4 が出ている）＝実機の B4 型（need1 少・need2 多）だけに効く。
- **[調査・再現せず] 大島愛の apt 停滞（休17/目標10・Pｼ13/目標20）**: 同型構造を合成
  （1職員・休/Pｼのみ・目標10/20・c1 休窓 5日≥1/14日≥4）して `applyAptPolish` にかけたところ
  **5手で apt 14→0 に解消**＝孤立した機構は正常。実データ側の相互作用（希望ロック・weekly・他職員と
  共有する c1 窓）が原因と絞られたが、**state ファイルが無いため特定には至らなかった**（推測は書かない）。
  なお Pｼ は需要0・担当が本人1人だけなので、解ければ被覆に一切触れず apt が14単位改善する見込み。
- **[調査・据え置き] compileSdk 36→37**: 実機が Android 17(SDK 37) を報告したため 3.173.0 の前提
  （API 37 の platform SDK 未提供）が変わったかを、**推測でなく Google の SDK リポジトリを直接引いて確認**。
  `platforms;android-37` は**存在せず** `android-37.2-beta1/2/3` のみ（`build-tools;37.0.0` は stable）。
  stable な compile SDK は未公開＝beta 依存になるため**上げない**。3.173.0 の判断は現時点でも有効。
  CI を1回落とさずに済んだ（前回は同じ判断へ至るのに CI 失敗を要した）。
- **[作業事故と復旧の記録]** feature ブランチへ戻る際、**ローカルの `claude/...` が e35156c のままだった**
  （3.372.0 は `git push HEAD:refs/heads/...` で**リモート ref だけ**更新していたため）。checkout で作業ツリーが
  3.371.0 相当へ戻ったが、3.372.0 の変更は 06369d6 に安全に存在。stash → `merge --ff-only` → stash pop で
  復旧し、失ったものは無い。以後 `HEAD:refs/heads/...` 形式で push したらローカル ref も揃える。
- 検証: ホストJVM **458テスト green**（既知の false positive 1件を除く）。UI 層はホストでコンパイル不可＝
  括弧均衡（HEAD 比 `{`/`}` +2/+2・`(`/`)` +6/+6 で対称）を静的確認。

## 3.371.0 の /code-review 指摘4件を検証して修正（3.372.0）
`/code-review` が出した4件を1件ずつ実コードへ照合し、**4件とも実在**を確認して修正した。うち3件は
3.371.0 で私自身が作った欠陥。**表示・テスト・潜在バグの修正のみ＝重み・採否・探索は完全に不変**。
- **[潜在バグ] `hypothesisSpawnPlan` が `hSpawn == plan.size` を破っていた**: `runMultiWorker` は
  `for (i in 0 until hSpawn) { ... plan[i] ... }` と index するのに、旧実装は `hSpawn = max(2, min(w, cores))`
  としつつ plan を `w` で組んでいた。`w < 2` では hSpawn(2) > plan.size(1) ＝ `plan[1]` で AIOOBE。
  本番の3呼出は全て `w = hypothesisCount(workers) = max(2, workers) >= 2` のため**到達しない**が、
  本関数は `internal`＝テスト/将来の呼出から届く。①hSpawn が w を超えないようにし（多様性の下限2は
  `min(w, ...)` の内側＝w>=2 のときだけ意味を持つ）②plan を必ず hSpawn で組む
  （`hypothesisChainPlan` は `IntArray(max(1,hypotheses))` を返すので不変条件が**構造的に**成立）。
  **旧テスト `spawnPlanIsSafeForDegenerateInputs` はまさにこの入力(w=0)を踏みながら
  `plan.isNotEmpty()` しか見ておらず緑のままだった**＝アサーションが弱かったことの実例。
  w=0..8 × cores=0..8 × workers=0..8 の総当たりで不変条件を固定する
  `spawnPlanAlwaysMatchesItsPlanLength` を新設。
- **[表示バグ・自分の取り残し] 「実効仮説」が2箇所で旧 `hypothesisCount` のまま**: 3.371.0 は
  `hypothesisSpawnPlan` の中で「表示は実挙動から導出」の原則を立てながら、その消費者2つ
  （`V6FinalPort` の TIME 行・`MagiSetupCards` の設定注記）を更新していなかった。workers=16／8コアだと
  **同じ実行の V6Dispatcher 行が「実効仮説8」、TIME 行が「実効仮説16」**と食い違う。
  runMultiWorker 経路(ALNS/RSI/RSI_PLUS)だけ spawn 数へ、PORTFOLIO は畳まれないので w のまま、
  V5 は仮説の概念を使わないので「SAチェーンN本」と分けて表示する。
- **[表示バグ] PORTFOLIO 診断が `仮説内チェーンは対象外` を無条件に印字**: `portfolioRoleParallelSa` を
  ON にしても実挙動がログに出ず、**実機ログでのA/Bというトグル本来の目的を潰していた**（3.153.0 の
  NativeBridge 行と同型）。実配線と表示が同じ値を読むよう `portfolioRoleChainCount()` を単一ソースとして
  新設し、`runAdaptivePortfolio` の `roleWorkers` もこれへ委譲（複製すれば必ずドリフトする）。
- **[検証の穴] 3.371.0 の per-family テストが low/high だけ完全差分になっていなかった**: `90a+45b` は
  単射でない（low=1,high=0 と low=0,high=2 がどちらも90）ため「low を1件見落として high を2件過剰に
  数える」型の取り違えを通す。かつ**片方だけ非ゼロでも両方を「発火」に数えており**、下の網羅チェックが
  実際より甘くなっていた。`DeltaEvaluator.rangeRaw()`（検証専用・O(S×K) のフル再計算）を新設して
  low/high を個別に breakdown と突合し、あわせて `rangeRaw()` の重み付き和 == 差分維持の `hct` を
  毎手つき合わせる（フル再計算 vs 差分維持＝増分整合性の検査にもなり、両者のドリフトもここで落ちる）。
- **[自分で踏んで直したミス]** `hypothesisSpawnPlan` に新関数を足す編集で **余分な `}` を作り object を
  早期に閉じてしまい**、`portfolioRoleChainCount`/`roleExploreFor`/`runSlot` 等が軒並み unresolved に
  なった（3.290.0/3.306.0 と同型＝この編集様式で3回目）。old_string に閉じ括弧を含めなかったのが原因。
- 検証: ホストJVM **458テスト green**（既知の false positive `EliteIntegrationRandomSafetyTest` 1件を除く）。
  **教訓#30 実践**＝`hypothesisSpawnPlan` の修正を scratch コピーでのみ revert し、新テストと強化した
  退化入力テストの**2件だけ**が `hSpawn==plan.size (w=0 cores=0 workers=0) expected:<2> but was:<1>` で
  落ちることを確認してから採用（repo 本体は無傷・検証後に scratch を削除）。

## 並列SAの本格再有効化＋soft全族の完全差分（3.371.0, ユーザー指示「並列SAの本格再有効化する」「soft全族の完全差分する」）
実機ログ2件（Pixel 10 Pro XL・CPU8コア・並列ワーカー設定8・PORTFOLIO 300s）を提示され、`RunMAGI_V5: ... SAチェーン1本`
（並列SAが常に単一チェーン）を確認したうえで対応。grillingで対象範囲を確認したが「no preference」との回答＝
自分でコード調査を深め判断した。

### ① 並列SAの本格再有効化
- **発見**: `hypothesisCount(workers)=max(2,workers)`（3.224.0で仮説数上限5を撤廃・多様性優先化）以降、
  `hypothesisChainPlan`（3.211.0/3.212.0で作った「余剰ワーカーを仮説内チェーン数へ配分」する仕組み）は
  **`hypotheses(=h)`と`workers`が常に一致するため、コア数に関わらず`distributable=max(h,min(workers,cores))=h`
  に構造的に一致し、内部チェーン数(SA/ALNS多チェーン)が恒久的に1本へ収束していた**（数式的に証明: h==workersなら
  どんな`min(workers,cores)`を与えてもdistributableはhを超えられない）。実機ログの`workers=8=コア数8`という
  「希釈リスクが無い典型構成」でも、この機構自体がそもそも一度も発動しない設計になっていた。
- **`runMultiWorker`（ALNS/RSI/RSI++の明示選択時に経由）を修正**: 新設 `hypothesisSpawnPlan(workers, w, cores)`
  （単一ソース、診断ログとも共有）。`workers<=cores`（大半の端末）では**無変更**（`hSpawn==w`のため旧来と
  完全同一のspawn数・plan）。`workers>cores`（端末のコア数を超える設定）のときだけ、spawnする仮説
  コルーチン数を実コア数まで落とし（希釈回避、V5の`clampWorkersToCores`と同じ発想）、その分の予算(workers)を
  各仮説の内部チェーン数へ回す（`hypothesisChainPlan`のcores引数へ`options.workers`を渡し既定のコア数クランプを
  迂回）。**workers予算の合計は不変**（コア数超のコルーチンは増やさない＝3.224.0で固定された
  `hypothesisChainPlan(5,5,8)==[1,1,1,1,1]`等の既存契約は無変更）。
- **PORTFOLIO（AUTO・既定211秒以上の主経路。実機ログの実際の選択先）は上記の対象外**: `runAdaptivePortfolio`
  （~430行の適応制御、epoch/役割/エリートアーカイブが全てworker-index配列で構築）は`runMultiWorker`を経由せず、
  各ロールが内部で呼ぶ`runV5`/`runAlns`(→`options.workers>1`で`runAlnsChains`へ分岐)/`runRsi`/`runRsiPlus`へ
  一律`roleOptions.workers=1`を渡していた。`workers==cores==8`という実機ログの構成では、単純に各ロールへ
  複数チェーンを与えると8ロール×2チェーン=16スレッドのような組織的な倍率オーバーサブスクライブになり、
  `clampWorkersToCores`/`hypothesisChainPlan`のコア数クランプが避けているのと同種の希釈リスクを生む。
  この430行の複雑な関数（epoch/archive/distance計算等がworker-index配列に密結合）を安全に再構成するには
  本格的なA/B測定（このサンドボックスでは実施不能）が要る＝**`PolishGate.portfolioRoleParallelSa`
  （既定OFF・設定タブ「詳細設定」にトグル追加）として実装し、実機で試せるようにした**（2.55.0/2.56.0/3.306.0と
  同じ規律：安全であることと有益であることは別、計測なしに既定を変えない）。ONにするとロールがV5/ALNSへ
  入るときだけ`PolishGate.portfolioRoleChains`（既定2・コア数以内にクランプ）本の並列チェーンを与える
  （全ロールが常時V5フェーズにいるわけではないため恒常的な倍率オーバーサブスクライブにはならないが、
  瞬間的なピーク並列度は増える）。
- 検証: ホストJVM（kotlin-compiler-embeddable 2.0.21、3.251.0で確立した手法）で v6/model 実コンパイル・
  **457テストgreen**（既存456+新規5、失敗1件は3.266.0記録済みの非JUnitクラスの誤検知のみ）。新規テスト5件
  （`spawnPlanMatchesLegacyBehaviorWhenWorkersFitsWithinCores`＝workers<=coresで旧来と完全一致／
  `spawnPlanRedistributesSurplusAsChainDepthWhenWorkersExceedsCores`＝workers=16/cores=4でhSpawn=4・
  plan=[4,4,4,4]・合計16=workers予算を保存しつつ各仮説が複数チェーンを持つことを確認／
  `spawnPlanNeverDropsBelowTheDiversityFloorOfTwo`／`spawnPlanIsSafeForDegenerateInputs`）。
  実データでの効果測定はPORTFOLIO側が未実装のため次回実機ログ待ち（runMultiWorker側は実機のPORTFOLIO主経路
  では発火しない＝直接ALNS/RSI/RSI++選択時のみ効く）。

### ② soft全族の完全差分
- **動機**: 既存の`DeltaEvaluatorTest`は**総和**(`de.score()==ev.fullEval(...)`)のみを20,000+4,000+20,000反復で
  検証しており、**族ごとの誤りが同一重みで相殺されると検出できない**穴があった（`MirrorKeys.all`はc1とc3mnが
  同じ重み15、c2/c41/c42/c41s/c42s/apt/fair/weekly/covOが全て重み1＝これらのどの2族間でも「片方+1・もう片方-1」
  の誤りは総和に現れない）。この種の族間取り違えは本セッション履歴でも複数回発見されている実在パターン
  （c42自己ペア・groupViol hard不整合・need1のみ判定 等）。
- **`DeltaEvaluator.familyRaw()`（新設・internal）**: running per-family の生カウント（`MirrorKeys.all`の
  各キーと一対一）を検証専用に公開。`rangeWeighted()`（同・internal）はlow/high統合済みの重み適用後running
  total（`hct`）を返す（低/高はrangeViolが呼出時点で×90/×45を適用し1つのフィールドへ合算する設計のため、
  checkerの`breakdown["low"]*90+breakdown["high"]*45`と比較する形）。
- **新規テスト`deltaPerFamilyMatchesCheckerBreakdown_allSoftFamilies`**: 初期状態+3,000回の単一セル移動
  （担当可否を問わず選ぶ＝groupVIolも踏む）の各時点で、`UnifiedViolationChecker.check(...).breakdown`と
  `familyRaw()`の**全19キーを1つずつ**突き合わせる。3.337.0の規律（族の網羅を数字で見せる、緑が意味を
  持つよう発火を確認する）を踏襲し、19族中17族以上の非ゼロ発火を確認するアサーションも追加。
- **教訓#30の実践（この新テストが実際に何かを検出できることを検証）**: 検証専用のscratchコピーでのみ
  `familyRaw()`のc1↔c3mn（同一重み15）を意図的に入れ替えたバグ入りDeltaEvaluator.ktを作成し、ホストJVMで
  `DeltaEvaluatorTest`を実行したところ**既存5テスト（総和検証）は全てgreenのまま、新規テストだけが
  即座に`family=c1 expected:<2> but was:<0>`で失敗**することを確認——「総和一致は族ごとの誤りを隠す」という
  当初の動機を実測で裏づけた。リポジトリ本体は一切変更せず検証後に削除。
- 探索・重み・エンジン本体は完全不変（読取専用アクセサ2件＋テスト1件の追加のみ）。HF77非該当。
- 検証: ホストJVM 457テストgreen（①の検証と同一実行に含む）。

## needFamilies 新設＝covU/c41系の重なりで場所一覧が件数より少なく見える穴を解消＋CI download の無防備さを是正（3.370.0, ユーザー指示「同様な問題などあるか?」）
外部レポート（フルコード/48時間ログの2件とも全て別コードベース＝この main には無関係と確定済み）の**カテゴリ名**
（MirrorCore型整合／部分Δ covU/c3n／CI安定化）だけをこのコードベースに写して監査。**実在する2件**を発見・修正した。
- **[実バグ] `breakdownLocations`（内訳→場所タップ）が3キー空間とも単一クラスの生マップを直接フィルタ**:
  3.111.0（`cellFamilies`）・3.353.0（`countFamilies`）で「重い違反(low90/high45/covU8000等)が同じセルの
  軽い違反(apt/c2/c41/c41s等)を隠す」問題はチェッカー層では解決済みだったが、**この機能の消費者
  (`MagiDashboardCards.breakdownLocations`)がどちらの union も一度も使っていなかった**（`countFamilies`は
  `UiState`にすら届いていなかった＝V6SanityPortの診断ログ専用のまま）。実害＝「群のレンジ 3件」の内訳
  チップをタップしても、covUと同じセルで重なった1件が場所一覧から消えて2件しか出ない、という**件数と
  一覧が食い違う表示バグ**。加えて `needViolations`（covU/covO/c41/c41s の被覆キー空間）には
  `cellFamilies`/`countFamilies`の兄弟（全クラス保持マップ）が**checker層にすら存在しなかった**——
  3.111.0/3.353.0が直した2つのキー空間とは別の**第3のキー空間**で同型の穴が未対応のまま残っていた。
  修正: `MirrorCore.markNeed`を`markCount`と同型に拡張し`needFams`蓄積を追加、`ViolationReport.needFamilies`
  を新設（checker層）。`UiState`に`countFamilies`/`needFamilies`(+`resultCountFamilies`/`resultNeedFamilies`)を
  追加し`makeUi`から配線（表示層）。`breakdownLocations`の4分岐（count系/apt/被覆系/セル系）全てを
  `*Families`優先＋単一クラスへのフォールバック（`ui未充填時の保険`）へ書き換え。表示のみ・スコアリング不変。
- **[実在の gap] CI の Gradle ダウンロードが3ワークフローとも無防備**: `android-sdk.yml`/`release-build.yml`/
  `v6-engine-check.yml`が同一の`wget -q -O /tmp/gradle.zip ...`をリトライなし・キャッシュなしで毎回実行
  （~100MB超を毎回無条件に再取得、一時的なネットワーク不調でジョブごと失敗しうる）。`actions/cache@v4`
  （バージョン文字列固定キー、ヒット時はダウンロード自体をスキップ）＋`wget --tries=3 --waitretry=5`の
  多重防御を3ファイルに同一パターンで追加。**編集事故を1件その場で発見・是正**: `release-build.yml`の
  初回編集で`name:`行を含めずold_stringを組んだ結果、直前の`- name: Install Gradle 9.3.1`行が孤立し
  run/usesを持たない不正なstepとして残った（YAML構文自体はエラーにならないため`grep`目視で発見）。
  `python3 -c "import yaml; yaml.safe_load(...)"`で3ファイルとも構文有効・step構造も再度目視確認して是正済み。
- 検証: v6層（MirrorCore.kt）はホストJVM実行で**452テストgreen**（新規1件
  `needFamiliesKeepsC41WhenItOverlapsWithCovU`＝covUとc41が同一(shift,日)セルで同時発火する最小盤面で
  `needViolations`単体ではc41が消え`needFamilies`には両方保持されることを固定）。UI層3ファイルは
  ホストコンパイル不可＝括弧均衡・シンボル重複無し・呼び出し側シグネチャ一致を静的確認。
  CIワークフロー3件はYAML構文検証＋目視でstep構造を確認。最終判定は次回push時のCI実行。

## /code-review 全コード＝need2単独定義セル見落としの第3世代を発見・修正（3.369.0, ユーザー指示「すべてのフルコードを/code-review する」）
`/code-review` skill（サブエージェント fan-out 不可のためインライン単一パス、CLAUDE.md 履歴と照合して既解決事項は除外）を
約35,000行の Kotlin/C++ 全体へ実施。findings 4件を全て実コードで裏取りしたうえで修正した。**エンジン評価器本体
（Checker/Evaluator/DeltaEvaluator/native parity）は既存の大量の敵対的レビュー履歴で堅牢化済みで新規欠陥は0**。
- **[実バグ・最重要] need1のみ判定の第3世代**: `covUCell`/`covOCell`（Problem.kt、need1・need2のORで需要/上限を
  判定する source of truth）を経由せず生の `p.need1[k][j]` だけを見る箇所が、3.173.0（CoverageDiagnosis）・
  3.309.0（V6LateOperators.isBalanceable）で修正した族とは**別に4箇所**残っていた:
  - `SmartInitialScheduler.kt` step③（日別必要人数の demand-fill）＋step⑤（残り埋めの demandBonus tie-break）
  - `GreedyMirrorScheduler.kt` の同一ロジック（両ファイルで完全に同型のコード＝同時修正）
  - `V6SearchOperators.kt` の `findCovOFix`（過剰スキャン＋移動先の不足推定の両方）
  いずれも need1未設定・need2のみで需要/上限が定義されたシフトを**完全に見落とす**（demand-fillは対象0件のまま
  スキップ、findCovOFixは過剰配置を検出できずnullを返す）。初期解生成（`generateSmartInitial`/`generateSimple`）が
  covU(HARD, 重み8000)違反を残したまま返り得る。修正は全箇所 `p.covUCell`/`p.covOCell` へ置換（need1/need2の
  分岐ロジックをsource of truthへ委譲・重複コード削減も兼ねる）。
- **[latent, 防御的ガード追加] `C1TemporalDp.kt` の RELOC_BITS(6bit)未検証**: `key(mask,count,relocations)` の
  ビット詰め込みは `maxRelocations`>63 だと count フィールドへ溢れ、異なる状態が誤って同一視され得る
  （silent corruption）。現在の全4呼出元は4/6で範囲内＝到達不能だが、3.213.0の`SCORE_HARD_UNIT`検証と同型の
  精神で、既存の early-return-null 契約（この関数は無効入力に対し例外でなく null を返す設計）に1条件追加。
- **検証**: ホストJVM実行（3.251.0系の手法）で main+test 61ファイルをコンパイル・**451テスト green**
  （実質失敗0＝1件は3.266.0記録済みの非JUnitクラスの誤検出）。新規3テスト
  （`SmartInitialSchedulerTest.fillsNeed2OnlyDemandDuringInitialConstruction`・
  `V6SearchOperatorsTest`×2＝`findCovOFixDetectsNeed2OnlyOverCoverage`/`ReturnsNullWhenNoOverCoverage`）を追加。
  **教訓#30の実践**: SmartInitialSchedulerの修正をscratchコピーでのみ一時revertし、新規テストが単独で
  `expected:<0> but was:<2>`（HARD=2）で落ち他7件は無傷であることを確認してから復元（repo本体は無変更のまま）。
- 探索/後処理研磨（`runPostOptimization`）は無関係＝最終採否は常にchecker+isBetterのkeep-bestが担保するため
  影響なし。影響は**初期解生成の質のみ**（GreedyMirrorScheduler/SmartInitialSchedulerが返す盤面）。

## 族数「18種」の docs 取り残しを19種へ横断修正（3.368.0, 3.202.0 の兄弟 docs への波及完了）
「次」の掃討を族数へ広げた。`MirrorKeys.all` は**19族**（weekly が19番目・3.72.0 で目的関数へ統合）だが、
3.202.0 が `business-logic.md` を「18種→19種(HARD4/SOFT15)」に直した際、**兄弟 docs が取り残されていた**
（CLAUDE.md 更新ルールが警戒する「同じ事実を写した側だけ stale 化」の実例）:
- **現行値へ訂正（docs）**: `data-models.md`「breakdown(18種)」→19種／`overview.md`「内訳(18種)」→19種／
  `requirements.md`「18種の違反」→19種／`magi_design_system.md`「全18種/100%・fair含む」→「全19種/100%・fair/weekly含む」／
  `screen_spec.md`（6箇所の「18種」＋version-marker「18/18」＋§08b 見出し＋:199 族列挙）。**:199 の任意群列挙に
  weekly を追加**（旧列挙は fair 止まりで weekly が抜けていた）・「17/18→18/18」を「17/19→19/19」へ。
- **コード1コメント**: `MagiScheduleViews:354` E7 バケツ「18族を6バケツに」→**「場所を持つ17族（全19族から fair/weekly を
  除く=357行）」**（`vioBuckets` の union は実測17族＝need2+pref1+seq4+count4+group5+window1。357行が既に fair/weekly
  除外を明記していたのに 354 の数だけ取り残されていた）。
- 温存: `business-logic.md:17`「19種(HARD4/SOFT15)」は 3.202.0 で既に正しい。ポリッシュ**パス**数の「18パス」
  （MagiDashboardCards/MagiUiState/V6HotfixPasses）は族数と無関係＝不変（18 V6HotfixPasses パス＋LNS2本=20）。
- 6ファイル・**docs＋コメントのみ・ロジック/スコア不変**（diff で code 変更が1コメント行のみと確認）。read-only。
- **(3.368.1, /code-review セルフレビュー)** サブエージェント8体がセッション上限で全滅→3.132.1 の前例どおり
  インラインで実施。findings 2件: ①354行コメントの**絶対行番号参照「=下記357行」**（drift を直す作業で新しい
  drift 源を持ち込んでいた・3.367.0 の KDoc 拡張が既に下方の行番号をずらした前例あり）→「本ブロック末尾の
  注記参照」へ是正 ②screen_spec の反映バージョン表記 v3.35 自体が doc 実態（3.287.0 以降も更新）より古い
  stale＝version-marker の 18/18→19/19 書き換えは本文整合を優先した妥当な選択だが、marker 刷新は別途の
  判断（報告のみ）。他は全行照合で問題なし（コード行混入0・記述と実装の一致を再確認）。

## 重み値コメントのドリフト＋c1 表示昇格の判断点（3.367.0, 3.366.0 の sibling-bug 掃討を重み定数へ拡張）
3.366.0（keep-best 順序コメント）と同じ HF77＝コメント≠実装の掃討を**重み定数**へ広げた。重みは
c1 4→5→15（3.249.0/3.253.0）・c3mn 12→15（3.249.0）・covO 0.5→1.0（3.148.0）と変わっており、3.305.0 で
`staffPacked` のハードコード重みは直したが、**現行記述コメントに旧値が残っていた**:
- **`MagiScheduleViews:733` heavySoftFamilies KDoc**「c3mn=12／軽い族（重み≤4: c1/…）」／**`V6WebCompat:556`**
  重大度階層「c3mn(12)>c1(4)」／**`V6WebCompat` severityFromVioKey** の HIGH コメント「(90/45/12)」・WARN
  コメント「中 soft(1〜4)」／**`V6HotfixPasses:1797`** C3mnPolish「SOFT重み12」。全て現行値へ訂正（3ファイル・
  **コメントのみ**・`when` の `-> "HIGH"/"WARN"` 分類コードは不変＝diff で確認）。歴史記述（`DeltaEvaluator:91/250`
  「c1=4→5・c3mn=12→15」・`ObjectiveParityTest:26`）と件数記述（`c1=4(weighted 60)`＝4件×15）は温存。
- **[実在の設計上の緊張を発見・分類は変えず据え置き＋判断点として明示]** `heavySoftFamilies={low,high,c3mn}`
  と `severityFromVioKey`（c1=WARN）はどちらも c1 が**重み4だった 3.99.0 当時**の分類。c1 は今 15（=c3mn）だが、
  ①`severityFromVioKey` は下流（`V6RemainingScreens`）が **HIGH と WARN を同一表示に畳む**（`->"要調整"`/softHex）
  ため c1 の分類は**視覚的に不活性**（凡例スウォッチ不変）②グリッド `heavySoftFamilies` は c1 が**最多件数の
  ソフト族**（real3 で c1≈58-73）のため、飽和回避（3.99.0 の「格子が警告に飽和し必須が埋没」対策）を優先して
  角マーク据え置きが妥当。**両面とも c1 を非 heavy で一貫**しており視覚は変わらないので分類は変えず、コメントに
  「c1=15 だが最多件数で飽和回避＝表示の強さ＝重み＋件数」を正直に記した。**判断点（ユーザー選択）**: severity-match
  を優先して c1 を c3mn 同様に破線へ昇格したい場合は `heavySoftFamilies` に "c1" を足す一行変更で可能（ただし
  グリッド飽和のリスク＝business 判断）。今回は既定＝非 heavy を維持。表示・スコアリング完全不変。

## 外部レポート L1-L10 の周辺検証＝keep-best 順序コメントのドリフト12件を訂正（3.366.0, ユーザー「周辺も検証する」）
ユーザーが別 fork のレポート（L1-L10・`g2.covU.chain2`/`MoveNormalizer`/`tryTransition`/`STAGE rsi-enter`/
`normal_clear`/`6089acef` 等の用語）を提示。**用語は全語0件でこの main に無い別 fork**（前ターンの 3.371.0 判定の
続き）だが、指摘の中身をこの 3.365.0 のコードに写して1件ずつ突合し、**L5/L9 の実在アナログを両方とも精読で否定**した:
- **L9（「正規化を通さない raw Move」）は不在**: `findCovUChain` は `List<IntArray>?`（`[staff,day,newK]` 三つ組）を
  返し、**採否は必ず keep-best**——`commitBestMove`（per-candidate `better()`）・手B の `isBetter+pinBlocks`・
  または RSI 仮説生成としてラウンド境界の `better()`。`MoveNormalizer` 相当の型付き Move 契約は存在しない（正規化
  でなく keep-best ゲートで弾く）。findCovUChain 自体が covU 減少・c3n 回避を保証。全13呼出元を確認。
- **L5（「hard同点で soft 悪化 elite が載る／better の順序」）は実装レベルで完全ガード**: `publishLiveBest` は
  `compareAndSet` を `!better()` でガード（劣る report は載らない）。全 elite/global 採択サイトが `better()`
  ＝`betterReport`＝`reportComparator`＝**hard→weightedScore→total**（3098行 `better = betterReport` で確認）。
  「合計 up with hard 同点」は 3.287.0 の weighted 優先どおりの正しい取引（3.283.1 に total−18/weighted+238 の実例）。
- **実在した唯一の項目＝コメントの stale ドリフト**: 実装は全て正しいのに、**keep-best 順序を旧 `hard→total→weight`
  で書いた現行記述コメントが12箇所**（`V6NativeOptimizer`×6・`CombinatorialRepair`×2・`EliteIntegrationPolish`・
  `C1TemporalFlowPolish`・`C1DeltaPrefilter`・`V6SearchOperators`・`V6SwapSuggester`・`AdaptiveEliteArchive` KDoc・
  `V6FinalBridgePortTest`×2）。3.287.0 が第2キーを total→weightedScore へ統一した際、`MirrorCore.reportComparator`
  の KDoc 自身が「写した側だけ取り残される」と警告していた実績（gateW/広域ビーム/AdaptiveEliteArchive/「他の案」）の
  **さらなる残り**。L5 を監査する読み手をこの12コメントが「total 優先」と誤誘導するため訂正（HF77＝コメント≠実装、
  3.363.0/3.352.0 と同じ sibling-bug 掃討）。全て `hard→weighted→total` へ。**歴史記述（`旧:`/`修正前`＝
  V6FinalBridgePortTest:15・C1BeamPolishTest:18）と正しい移行説明（MirrorCore:66「total→weightedScore へ統一した」）は
  温存**。9ファイル・17行・**コメントのみ**（コード行の混入0を diff で確認）＝コンパイル/テスト/スコアリング完全不変。
- L1/L2/L3/L7 は既診断の入力/仕様矛盾（3.280.0 ForbiddenDiag・3.343.0 pref代金・3.344.0 allBlockedNow・
  3.361.0 covU-blocked A/B・backlog#4）、L4/L6/L8 は仕様どおりの挙動（3.353.0 countFamilies 全族表示・250ms 間引き・
  export 形式）。**新規に直すべき論理バグはこの 3.365.0 に無し**、というのが周辺検証の結論。

## 共有ネイティブハンドルの並列安全性を実行テストで示す（3.365.0, 別ブランチ x8ygvy から選択的に取り込み）
ユーザー指示「すべてのブランチを main にマージする」→ 調査で fork 2本（x8ygvy・ir1xng）は**競合する並行開発ライン**
（版番号衝突・ir1xng は 3.176.0 の4週間前・401commits分岐）と判明し盲目マージは main 破壊と判断。ユーザー選択で
**x8ygvy を精査し価値ある変更だけ選択的に取り込む**方針に。7コミットを1件ずつ評価:
- **却下**: `ecab235`（covU 壁の早期終了を既定ON）・`c4d6302`（Watchdog 修正だが covUWallProven 等の早期終了機構と
  分離不能）・covU 早期終了配線（cfcca0d/cc0faa2）＝**このセッションで real3 多seed A/B により有害と実証・却下した決定**
  そのもの。取り込むと検証済みの却下を覆すため不採用。
- **採用（1件・検証済み）**: `923bf07` の**共有ネイティブハンドル並列安全テスト**を `host_parity_bench.cpp` へ移植。
  `SaOptimizer.run` は native handle を1本だけ作り最大8ワーカーが同時に `nativeSaChunk` を呼ぶ。**このセッションで
  「Problem は const& read-only・共有可変状態ゼロ＝スレッド安全」と読んで結論した内容を、実行で証明する**もの
  （N スレッドが同一 MagiProblem で `runSaChunk` を並列実行し**逐次と bit 一致**を確認・`--shared-only` は
  ThreadSanitizer 用）。flat ループに無条件で入れたので**既存の native-parity CI で自動実行**（mismatch で exit 1）。
  CI の g++ へ `-pthread` を追加（`std::thread` リンク）。**見送り**: `dc104cc`(--bench-real=フル parity 後にしか走らない
  awkward 設計・低価値)・`b8b4ae9`(kotlin_cpp_split.md=x8ygvy 版番号スタンプ＋dc104cc 参照＋既存 native docs と重複)。
- **検証（提示物を信用せず独立に再現）**: `g++ -O3 -pthread` で実ビルド、`--shared-only` で **8スレッド identical to
  serial・0 mismatches**、フル run で **PARITY 4,195,533手・0 mismatches**（移植が既存パリティを壊していないことを確認）。
  test/tools のみ・engine/重み/スコア不変。

## 後処理研磨の「Range 先頭化」を3データセット A/B で否決（敵対検証ケース6の続き・実データ受領）
ユーザーが敵対検証ケース6「soft 研磨パスの順序は正しいか」を提示。実 `runPostOptimization` と突合し順序は正確・
安全（keep-best で退化不能）・実測隣接（3.254.0 temporalFlow<wideBeam・3.300.0 BlockSwap は Range 後 Apt/Fair 前）を
尊重と確認。唯一 open だった「最重ソフト Range(low90/high45)が4番目＝重み降順なら先頭へ」という untested 仮説を
**host-JVM A/B で実測**（固定seed・Range を巡回クラスタ先頭へ移す variant をコンパイルして比較）。
- **当初2データセット**: golden=Range先頭 **weighted 2653→2530（−4.6%）**改善 / sample_v6=中立（low 2→1 と c1 6→11 の
  構成入替で相殺）。「fixture 限定の利得＋2データセットのみ」で保留を推奨したところ、**ユーザーが第3の実データ
  （2026-08 実運用 state）を受領**＝私の「real/user 揮発済み」を直接解決。
- **real3 で決着**: baseline と Range先頭が**完全一致**（33347/33318・run1≠run2 は JointLNS 壁時計非決定性で両ビルド同一）
  ＝**実運用データに効果ゼロ**。3/3 で weighted は never worse だが改善は golden のみ＝測定済みパイプラインを
  fixture 限定利得で書き換えない＝**不採用**（`docs/algorithm_portfolio.md` の「実測で否決した提案」に記録・再提案しない）。
- 副産物: real3 で 3.364.0 の c1 壁修正が**実データで正しく動作**確認（休の rest-wall のみ発火・非休 false wall なし）。
  希望は impossible=0/feasible-unmet=0（pref クリーン）、input hard=4 は全て covU（post-opt 単体では不変＝search フェーズの担当）。

## c1「壁」判定の need2 依存を実データ計測で false wall と確定・正直化（3.364.0, backlog#4 解消）
残バックログ #4「`V6SanityPort` の c1『壁』判定(検査2b-2)が非休シフトの供給に need2（covO の SOFT 目標=1日あたり
過剰配置しきい値）を実質ハード上限として使い、covO を犠牲に c1 を解消できる局面を過大に『構造的不能』と報告しうる」。
3.161.0 で「報告のみ」、3.179.0 で「盲目的修正は 3.76.0 の検証済み Dﾃ 壁を false negative 化する＝据え置き」とした項目を、
**実データ計測で決着**（3.361.0/3.362.0 と同じ「理論的懸念が実データで顕在化するか先に測る」手順）。**read-only・
スコア/エンジン/重み不変・HF77非該当**（3.263.0/3.344.0/3.362.0 と同じ「計測に基づく診断の正直化」）。
- **3.179.0 の前提が実測で反証された**: golden の Dﾃ（14日2回×2窓）は need2供給31 < 需要32 で発火するが、**物理供給
  248（担当8人×31日）>> 需要32** ＝物理的な壁ではない。かつ **golden の実運用手作り盤面（hard=0）は Dﾃ を合計35回
  配置（供給31 を超過・max 2/日）** ＝need2供給31 は実上限でない（盤面が既に超えている）。残る Dﾃ c1=9件は供給不足でなく
  分布/禁止連続が原因。よって旧文言「どう組んでもこの窓違反(c1)は構造的に残ります（最適化では消せません）」は**事実誤り**。
- **証明**: 非休では demand = nCanDo×day2×(T/day1)、物理供給 = nCanDo×T。ガード `day2 > day1 → continue` により
  day2≤day1 ⟹ **物理供給 ≥ demand が常に成立** ＝**非休の c1 窓は原理的に構造的不能にはならない**。
- **修正（検査2b-2）**: 休のみ「S*T−Σ最小work需要（＝作業に回さないセル数）」が実在の物理上限＝供給<需要なら真の壁として
  維持。非休は「1日あたり上限の合計が窓ルールの必要回数に届かない場合のみ、c1 充足に過剰配置(covO)が要る」旨を
  **『構造的に不能ではなく、最適化は過剰配置を少し払って解消できます』**というトレードオフとして正直に案内（advice=
  「1日あたり上限を上げるか、N回ぶんの過剰配置を許容してください」）。
- **既存テストが false-positive を固定していた**: `classifiesStructuralWindowWallButNotDial`（非休 A・供給6<需要8 を
  「壁」と assert）は、物理供給12≥8 の false wall を assert していた（3.76.0 期に unsound 挙動をテスト化）。2件へ分割・是正
  （`nonRestWindowShortfallIsCovOTensionNotAStructuralWall`＝非休は「構造的に残ります」と言わず過剰配置トレードオフとして
  案内／`restWindowShortfallIsStillAStructuralWall`＝休は物理上限を超えるため真の壁として維持）。
- 検証: ホストJVM で v6+model を再コンパイル・**V6SanityPortTest 43件 green**（新規2件含む）。実 `buildGuidance` を
  golden/sample_v6 に通し、golden Dﾃ が新しい covO-tension 文言に変わり sample_v6 は窓ルール案内なし（false wall ゼロ）を確認。

## 直近コード（3.352-3.360）の焦点レビュー＝clean 確認＋stale fact 1件訂正（3.363.0）
バックログ #2/#4/#5 はすべて grilling か製品判断が必要で terse な指示では進められないため、直近追加コードを
sibling-bug（3.347.0/3.311.0/3.335.0 の「取り残し」型）狙いで焦点レビューした。**実バグ0＝直近コードは
良く保守されていることを確認**（3.356.1/3.360.1/3.347.0 で既にレビュー済みの領域が多い）。
- **keep-best 比較の集約（3.352.0 reportComparator）は完全**: 残る手書き比較を全数照合したが取り残し0。
  `V6LateOperators.gate:88` は c1-boost 例外の追加条件（本採用は line 79 betterReport）／`V6SwapSuggester` は
  採用=betterReport(:79)・ランキング=`compareBy(dHard,dWeighted,dTotal)`(:318) で正順（Quad のコンストラクタ
  フィールド宣言順とソート順を混同しないこと）／`checkResultWorse`(V6FinalPort:717) は「厳密に悪化」時のみ
  非null＝tie で revert しない・順序 hard→weighted→total で正しく、message 生成のため hand-written が正当／
  `RejectCulpritStats`(3891) は却下理由の分類（isBetter 順に整合済み）＝いずれも意図どおり別物。
- **診断の算術も正しい**: `weeklyFloorOfCount`(MirrorCore) は r=min(余り,7−余り)＝真の最小偏差を返し曜日上限を
  無視＝真の下限以下（過大評価しない・診断用途に安全）／`structuralPersonalFloor`(3.354.0) は
  forcedMin=T−他シフト上限和・d=forcedMin−目標・per-staff max（保守的）で 6b の美幸B4=19 と一致。
- **environmentLine** は versionName null（`?: "?"`）・NativeBridge.available ガードとも健全。telemetry 並行性は
  3.360.1 で AtomicInteger 化済み。
- **[stale fact 訂正]** 3.95.0 の「golden(構造的covU=2)で発火」を実測で反証・訂正: 現行 golden_state.json は
  `V6SanityPort.structuralHardFloor=0`（入力盤面 hard=0/covU=0）＝covU 床の avoid 機構は golden では no-op。
  3.361.0 note の誤帰属（この記述を「3.263.0」とした）も「3.95.0」へ訂正。docs のみ・エンジン不変。

## パリティネットへ2つ目の実データ形状 sample_v6 を追加（3.362.0, backlog#6「実データ形状の網羅」）
残バックログ #3。言語跨ぎパリティ CI（3.357.0）は実データ fixture が golden_state **1つだけ**で、
その入力盤面は **hard=0**（3.361.0 の実測で判明）＝**C++ の HARD 族パス（groupViol/c3n/pref/covU）を
実データで一度も exercise しない**という穴があった。**test/CI のみ・エンジン/重み/スコア不変。**
- **fixture**: `sample_state_v6.json`（app/assets の実サンプル）を test/resources へ複製。**入力盤面 hard=15**
  （c1=2/c2=1/c42=7/c3n=11＝多族の HARD/SOFT が発火する別形状）。期待値 `sample_v6_eval_expected.txt`
  （hard=15/soft=825）は Kotlin `Evaluator.fullEval(入力盤面)` で算出（golden の hard=0/soft=3109 も同手法で再現し
  既存 fixture と一致＝ハーネス健全を確認）。
- **ベンチ1回で複数照合**: `host_parity_bench.cpp` の `--expect` を **flat 引数と出現順で対応づく**よう拡張
  （旧: 単一で全 flat が共有）。`magi_host golden.flat sample_v6.flat --expect=g.exp --expect=s.exp` で
  合成ベンチ（3.6M手）を1回のまま両形状を言語跨ぎ照合＝CI 時間は据え置き。
- **配線**: `NativeParityFixtureTest` を helper 化し golden/sample_v6 の2 @Test へ（Kotlin 側を両方固定）。
  `native-parity.yml` に sample_v6 の flatten＋run の第2 --expect を追加。
- **検証（提示物を信用せず独立に再現）**: ホスト実行で **CROSS golden C++ hard0/soft3109==Kotlin・
  sample_v6 C++ hard15/soft825==Kotlin・合計 4,195,533手 mismatch=0**（sample_v6 の parity ループが約60万手を
  追加）。**捕捉できることを実測**: sample_v6 期待値を soft 826 にずらすと sample_v6 だけ MISMATCH で exit 1
  （golden は MATCH のまま）＝第2 --expect が実際に対応づけ・照合されている。Kotlin テストも host-JVM で
  実実行し 2 tests green。
- ~~残る穴: real/user 相当の「構造的 covU が床超で blocked-now」な形状は依然 repo に無い~~（3.409.15 で解消。3.361.0 で記録した
  covU-watchdog A/B の前提）。sample_v6 は hard=15 だが covU は解ける形＝covU-blocked ではない。将来その形状を
  test resource 化すれば backlog#6 と covU-A/B の両方が前進する。

## covU-blocked 早期終了を実データ多seed A/B で確認却下（3.361.0 の再オープン条件を満たし、却下を補強）
3.361.0 は「動機データ消失で直接 A/B 不可」と記録していた。ユーザーが **covU-blocked/floor=0 の実データ real3
（2026-08 実運用 state）を受領**＝再オープン条件（実データ+A/B）を満たした。**real3 の構造**: `structuralHardFloor=0`
（供給床でない）かつ `allBlockedNow=true`（covU=4 は FIXABLE だが今の希望・盤面では局所手で解けない）＝3.344.0 の
`allBlockedNow` 診断が実データで正しく発火する形状。**多seed A/B（6 run × 45s・handleOptimize は nanoTime シード・
壁時計計測）**:
- 最終改善時刻の分布 = 6.4s / 7.9s / 9.9s / 10.1s / **25.8s / 26.6s**。**6 run 中2 run が ~26s まで改善継続**。
  とくに **run2 は hard=4→3 に突破**（他5 run が届かない covU をもう1つ解消）し、その突破が **25.8s の遅い時刻**。
- **単一 run（前回・plateau 10.2s）だけ見ると「早期終了は安全」に誤誘導されたが、多seed で覆った**（★★★★ 推奨どおり）。
  ＝**遅延改善（含む hard=3 への構造突破）は実データで実在** → 15s 早期終了なら run2 の hard=3 を取り逃す。
- **計測上の注記**: `gain@Ts` 絶対値は最終エピローグ（onProgress 報告後に improving）で嵩上げされる混入があり信頼しない。
  信頼できる信号は「最終改善時刻」（onProgress live-best が最後に下がった壁時刻＝探索 plateau 時点）と run2 の最終 hard=3。
- **結論**: A/B の結果が **3.361.0 の原却下を確認**（早期終了は keep-best-unsafe＝遅延改善を切る、が実データで実証）。
  3.361.0 は**再オープンしない**。証拠が「データ無し・原理のみ」→「実データ多seed で確認済み」へ格上げ。
  ※real3 の fixture 化は実職員名を含むため保留していた **→ 3.409.15 で職員名のみ匿名化（職員A..J）して解消**
  （匿名化前後で評価が bit 一致することを実測してから追加＝形は完全に保存される）。

## covU-blocked のウォッチドッグ配線を実測して却下（3.361.0, ユーザー指示「修正する」＝#1 の A/B）
残作業 #1「`CoverageDiagnosis.allBlockedNow` をウォッチドッグへ配線し、covU が構造床超でも blocked-now を
実証したら plateau として stallHardMs（早期終了）へ移す」。3.344.0 が「要A/B・品質と電池の交換・今回は診断の
矛盾解消までに留める」と保留した項目を、ユーザーの「修正する」で再開。**別セッションで一度 principled-safe として
原理採用しかけた4編集を、この規律違反に気づいて revert し、実測のうえ却下した。**

### なぜ原理採用できないか（前提の訂正）
早期終了は focus 変更（3.74.0系）と違い **keep-best で退化不能ではない**。keep-best が保証するのは「返す解は
**見た**解の最良」であって、早期終了は探索そのものを止める＝**まだ見ていない改善を諦める**ため keep-best では
回収できない。前セッションの「principled-safe＝keep-best が担保」判断はこの区別を見落としていた。よって
3.310.1/3.341.1 の規律どおり「探索動学に効く変更は実測してから採否・原理採用しない」に該当する。

### 実測（ホストJVM・handleOptimize 改善タイムライン）
kotlin-compiler-embeddable で v6/model をコンパイルし handleOptimize を実走（3.251.0/3.263.0 の手法）。
- **golden_state は hard=0・covU=0 に到達**（`structuralHardFloor=0`・allBlockedNow=false）＝**covUBlocked 項は
  構造的に常に偽＝完全な no-op**。この変更を golden では**一切検証できない**。CLAUDE.md 3.95.0 の
  「golden(構造的covU=2)で発火」は現行 golden_state.json では stale（3.362.0 で `V6SanityPort.structuralHardFloor`
  を実測して 0 を確認＝covU 床の avoid 機構は golden では発火しない）。
- covU-blocked を実際に持つ唯一のデータ（real_state/user_state）は前セッションのアップロード＝**揮発コンテナで消失**。
  よって covUBlocked の直接 ON/OFF は**利用可能データで不可能**。
- **sample_state_v6（stuck HARD=1・非covU＝pref由来・covU=0）のタイムラインが決定的な傍証**:
  改善が 17159ms(weighted 8158) で止まり **22秒停滞**を挟んで 39234ms(8156)→…→53696ms(8113) と再開。
  この stuck HARD は c3n でないため**既存ウォッチドッグが正しく stallMs（長）を選び、この遅延バースト
  改善(8158→8113≈45pt/0.5%)を捕らえている**。もし stallHardMs(=budget/8=15s) の早期終了が効いていれば
  32159ms で止まり weighted 8158 のまま＝この改善を切り捨てていた。
- **soft 族の研磨動学は stuck HARD が covU か c3n か pref かに依らない**（3.183.0/3.184.0＝HARD が stuck でも
  focus は SOFT へ pivot し続ける）。よって sample_v6 が示す「遅延バースト soft 改善の実在」は covU-blocked
  ケースにも適用でき、covUBlocked が stallMs→stallHardMs へ切り替えると**同種の改善を切り捨てる**と結論できる。

### 却下の根拠（4点）
1. 早期終了は keep-best-safe でない（上記）＝原理採用は規律違反。
2. 新規価値は**電池/時間の節約のみ**。「stuck HARD の予算を soft へ振り向ける」品質価値は 3.183.0/3.184.0 で
   既に実現済み＝品質の upside は無く、電池ゲインに対する**未実測の品質リスク**だけが残る。
3. 3.341.1 は逆方向（早期終了の撤去）を実測し中央 −3.5%/平均 −4.1%（p≈0.075）＝**早期終了は僅かに品質を
   落とす**方向。covUBlocked は早期終了を**より攻撃的**にする＝品質を落とす向き。
4. 直接 A/B が不可能（動機データ消失・golden は no-op・wall-clock ウォッチドッグ×PORTFOLIO ばらつきで
   ホスト JVM の綺麗な測定が困難）＝3.344.0 が保留した理由そのもの。
- **反証されたコードは残さない**（3.307.0）＝V6FinalPort.kt の4編集（effectiveStallMs 署名＋covUBlockedPlateau・
  covUBlockedCheckedVersion/Result atomics・covUBlockedProven lambda・shouldStop の covUBlocked 計算）を全て revert。
  **診断の矛盾解消（3.343.0/3.344.0 の「充足可能 vs どう組んでも解消できません」＋allBlockedNow）は既に済み**＝
  #1 の残りは本却下で確定。**再提案しない**（明示の real/user データつき A/B 指示があった場合のみ）。
- 検証手段の穴として記録: covU-blocked の実データが repo に無い（golden は hard=0 到達）ため、この class の
  ウォッチドッグ変更は**現状のサンドボックスでは直接 A/B できない**。将来 real/user 相当の「構造的 covU が
  床超で blocked-now」な state を test resource 化すれば測定可能になる（backlog）。

## ログのヘッダに版と実行環境を書く＋PORTFOLIO の合計iterと最良更新回数（3.360.0, ユーザー提示のログ強化仕様）

仕様案4項目をコードへ突合し、**実際に欠けていたところだけ**を足した。表示・ログのみ・スコア不変。

### 埋めた穴
- **書き出したログに版が無かった**。ヘッダは出力時刻とデータ規模だけで、**受け取った側がビルドを
  特定できない**（本セッションでもアップロードされたログがどの版か判定できず解析が止まり、外部レポートは
  古い `.so` を現行ソースの不具合と誤読した）。`environmentLine()` を新設し、テキスト・JSON 両方の
  書き出しへ1行:
  `版: 3.360.0-log-identity (526) ・ <メーカー> <機種> ・ Android 16(SDK 36) ・ CPU 8コア(いまの並列ワーカー設定=8) ・ ネイティブ=有効(ABI7)`
  **CPU コア数を出すのは `clampWorkersToCores`(3.224.0) が設定を黙ってコア数まで切り下げるから**
  （設定8でも4本しか走らない実測がある）。設定値だけ見ても実際の並列度は読めない。
  「いまの」と付けたのは、これが書き出し時点の設定であって実行時のそれとは限らないため。
- **PORTFOLIO だけ合計iterを出していなかった**。MultiWorker/AlnsChains/V5 は元から `合計iter=` を出すのに、
  **予算211秒以上の既定経路＝実機の主経路**だけが無く規模を比較できなかった。あわせて
  **全体最良を更新した回数**を追加（1回ごとの行はあるが「何回あったか」は要約に無く、序盤に1回きりで
  止まったのか終盤まで刻んだのかが読めなかった。Watchdog が出すのは最終改善の時刻だけ）。改善が確定した
  分岐で数えるだけ＝ホットパスに追加コストなし。実データ(real・45s・4ワーカー)で
  `合計iter=30924631 全体最良更新=5回` を確認。

### 既に満たしていた3項目（仕様案に対する回答）
- **スパム対策**: 3.283.0（同名フェーズ60秒窓・「最良更新」「改善」を含む行は対象外）／3.288.0（RSIラウンド行は
  改善したラウンドと最終ラウンドのみ・HF63 は集合が変化したときのみ・SOFTピボットは pivot が変わったときのみ）。
  仕様案の「イベント駆動（New Best・停滞脱出・フェーズ切替）」と同じ形。
- **制約別の分解とボトルネック特定**: `UnifiedCheck` の全族 breakdown ／ `残存分析` の
  もう直せない/まだ狙える（3.288.0/3.354.0/3.355.0）／`weekly内訳`（構造床と余地を分ける）／
  `c1内訳（職員×窓ルール別）` ／ 大きい族の職員別集約 ／ `違反詳細` の件数と場所数の別。
- **停滞脱出の可視化**: `Watchdog`（最終改善・停滞量・実効閾値・発火有無・c3n壁の判定）／`EarlyStop` ／
  `戦略変更`（focus 遷移の圧縮列）／`AdaptivePortfolio`（役割・再配属・秒・離脱理由・停滞見送り）。

### 敵対検証で見つけた自分のバグ（3.360.1）
3.356.0 で入れた `TuningTelemetry` の6カウンタが **`@Volatile var Int` に `++`＝read-modify-write** で、
**8並列ワーカーから加算されるため取りこぼしていた**（`parityChecks` は SA/LAHC/ALNS/研磨の4経路×全ワーカーから
毎チャンク）。ログは「1240回」と実数のように出すので、下限を実数と称していたことになる。AtomicInteger へ。
- **「0」は壊れていなかった**＝真の回数が1以上なら少なくとも1回は `1` が書かれるので、3.356.0 の
  「観測なし(==0)ならトグルを消してよい」という判断根拠は無傷。壊れていたのは大きさだけ。ここは区別して記録する。
- `summary()` で同じカウンタを2回読んでいた箇所（`wideC3nCalls`）も、判定と表示で値が食い違いうるため1回の読みへ。
- **既知の限界（意図的に残す）**: 実行をまたぐ static なので、実行が重なると後発の `reset()` が先行実行の
  計数を消す（3.335.0 が `RunSlot` で解いたのと同型）。ただし加算元の `breakableDaysFor` 等は非 suspend の
  純関数でコルーチンのコンテキストを読めないため同じ手が使えない。影響は**片方のログの診断値がずれる**だけ。
- あわせて `pi.versionName` の null（プラットフォーム型）で `版: null (526)` になりうる点を落とした。
- **検証**: 8スレッド×2万回の加算がちょうど16万になることを固定。`@Volatile var Int` へ戻すと
  **この1件だけが落ちる**ことを実行して確認（他452件は通る＝この不変条件を守るテストが他に無かった裏づけ）。
- **所見なしと確認したもの**: `chainWins[w]++` は `synchronized(lock)` 内で安全／`globalImproves` は
  フラグをロック内で立てロック外で1回加算＝二重計上なし／`environmentLine` の `NativeBridge.available`
  （`by lazy` の `System.loadLibrary`）は呼出2箇所とも `withContext(Dispatchers.Default)` ＝メインスレッドを
  塞がず、評価が早まるだけで結果は同一／`structuralPersonalFloor`・`weeklyFloorOfCount`・`blockDays` の
  日付列挙は境界・桁溢れとも問題なし。

### 採らなかったもの＝採択回数(Accepted)
JNI 越しにカウンタを渡す＝ABI 変更が要り、最内周に計数が入る。かつ受理率から引くレバー
（温度・受理方式のチューニング）は 2.55.0/2.56.0 で **実測して中立or有害**と結論し「脱出ヒューリスティクスへの
投資は停止」と決めている。**既に死んでいると測ったレバーのために、いちばん熱いループへ計数を足さない。**

## 提出された静的解析レポートの照合＝実コードに当たったのは1件（3.360.3）

`SearchSessionFull` / `tryTransition(ANNEAL)` / `tryMetropolis` / `TemperatureParams` / `ProblemGuards` /
`G1.propose` など**15個のシンボルを全部 grep したが、作業ツリーにも全 git 履歴（`-S` 全ref検索）にも
1つも存在しない**（3.178.0「マスク最適化#1〜#4」・3.319.0 `BlockPatternMatch` と同じ形＝別コードベースの
レポート）。よって提示コードは適用しない。**そのうえで指摘の中身を1件ずつ実コードへ当てた**。

- **#3「`nextInt(0)` でクラッシュ」＝当たり**。期間には `require(state.dayCount > 0)` があるのに
  **職員数には無く非対称**だった。3経路（`handleOptimize`/`handleSimple`/`handleSmartInitial`）へ
  `require(state.staff.isNotEmpty())` を追加。
  **ガードを外して実測して確かめた**（死にコードでないことの確認）: `handleOptimize` は実際に
  `IllegalArgumentException("bound must be positive")`＝`SaOptimizer` の `rng.nextInt(S)` を投げていた。
  `handleSimple`/`handleSmartInitial` は生成器側が「期間/職員/シフトが不足しています」を返しており
  致命的ではなかったが、3経路で文言を揃える。
  **到達経路は限定的**: 編集画面からは作れない（`Ws1Ops.removeStaff` が最後の1名を消さない）。かつ
  `dayCount` は日付でなく `schedule[0].size` 由来なので、staff も schedule も空なら既存の期間ガードが
  先に止める。**職員が空で schedule に行だけ残る不整合な取込**でのみ到達する。
- **当たらなかった8件（根拠つき）**:
  - #1/#2「HARD 増加を current に取り込む」= 事実だが**設計どおり**（SA/Metropolis）。辞書式パックで
    hard 差分は 1e9 単位になり `exp(-Δ/(200·temp))` が実質0、最終採用は `betterReport` の keep-best。
    current を hard 非悪化に縛るのは**探索の変更＝A/B が要る**（2.55.0/2.56.0/3.310.1 の規律）。
  - #4「`budgetMs=0` で無限ループ」= `timeUp()` は `elapsed >= budgetMs` なので 0 なら**即 true**＝
    ループに一度も入らない。`maxIters` という概念はこのコードベースに無い。
  - #5「schedule 形状 ≠ S×T で範囲外」= `normalizeSchedule` が不足を休で埋め範囲外を -1 センチネルへ写す
    （3.199.0 で C++ 側の -1 未対応を修正済み・3.278.0 で -1 の添字使用を全掃討済み）。
  - #6「`alpha >= 1` で冷却停止」= 冷却ループは `while (t >= tf && !timeUp())` で**時間で必ず有界**。
    `alpha` は内部定数(0.975)でユーザーからは設定できない。
  - #7「希望固定×c3n の構造壁」= 3.280.0 `ForbiddenDiag` が run 単位で証明し、3.311.0 で pref の代金も
    勘定するよう修正済み。3.283.1 に実機での発火例を記録済み。
  - #8「soft 表示と重み付き softSum の不一致」= 3.313.0 で「改善N%」の混在を是正し、3.337.0 で
    Checker↔Evaluator のパリティを CI で固定済み。
  - #9「ALNS 後処理の固定上限」= 3.271.0 の `clusterStop`（クラスタ専用締切）と各パスの内側締切確認で有界。
- テスト1件追加。**ガードを外すとこの1件だけが落ちる**ことを実行して確認（教訓#30）。

## main のビルドを直す＝LocusIdCompat の import が誤っていた（3.360.2）

3.360.1 を main へマージしたあと CI を確認して発覚。**マージより前から main が壊れていた**
（`607966b feat: harden Android conversation bubbles for optimize progress`。V6 Engine Check と
Android SDK の2ワークフローが赤＝**APK が一切ビルドできない**状態。Native Parity Check だけは
g++ のみで走るので緑だった）。
- 原因: `BubbleSupport.kt` の `import androidx.core.app.LocusIdCompat` が**誤った package**。
  `compileDebugKotlin` が `Unresolved reference 'LocusIdCompat'` ×3 で落ちていた。
- **記憶で直さず実アーティファクトで確認**: `androidx/core/core/1.13.1/core-1.13.1.aar` を取得して
  `classes.jar` を展開し、`androidx/core/content/LocusIdCompat.class` が実体であること、
  `ShortcutInfoCompat.Builder.setLocusId` / `NotificationCompat.Builder.setLocusId` の引数型も
  いずれも `androidx.core.content.LocusIdCompat` であることを `javap` で確認した。
- 同じ commit が入れた他の androidx シンボル（`setAutoExpandBubble`/`setSuppressNotification`/
  `setLongLived`/`setBubbleMetadata`）も同じ jar に存在することを確認。`areBubblesAllowed` は
  `NotificationManagerCompat` でなく**プラットフォームの `NotificationManager`**（API29+・minSdk36）で、
  同ファイル内の同名ヘルパー越しに呼んでおり正しい。
- import 1行の修正。エンジン・重み・スコアには一切触れない。

## 残りのピン計測外3箇所を測って決着（3.359.0, ユーザー指示「残り作業を最適化する」）

3.327.0 以降「計測外」として残していた `PinBlockAttribution`（UI の「回数の固定について」一覧）の
未配線サイトを**測ってから**片付けた。3.350.0 で最終LNS 2本を配線したときと同じ手順。

| 残っていたサイト | 実測 | 判断 |
|---|---|---|
| `EliteIntegrationPolish` ×4 | PORTFOLIO 45s 実走で **0〜1件** | 配線しない（実質ゼロ） |
| `CombinatorialRepair` ×1 | 生のピン却下は 880〜3244件 | **配線しない**（下記） |
| `C1TemporalFlowPolish` ×1 | golden 0 / real 6 / user 0 | **配線した** |

- **EliteIntegration**: 最初の計測は `runPostOptimization` だけを回して 0 だったが、このパスは
  `V6FinalPort` から**後処理の手前**で呼ばれるので**probe が経路を通っていなかった**＝0 は計測の不備。
  PORTFOLIO を実走（elite=9・relink48・fusion8 が実際に動く条件）し直して 0〜1件と確認した。
- **CombinatorialRepair**: 3.331.0 で**安いピン検査を checker より前**に置いた（実測 巡回研磨 13.6→12.7s）。
  よってピン却下の候補は **checker を呼んでいない＝「目的関数なら採用したか」が原理的に分からない**。
  3.347.0 で確立した意味論（ピン破り＝isBetter が採用を認めた手）で数えるには、まさに省いている
  checker を払い直すことになる。**診断のために最適化を戻さない**＝配線しない、と根拠つきで確定。
- **C1TemporalFlow**: `exactPinRegression` → `pinBlocks.blocksImproving` へ置換し `runPostOptimization` で
  merge（同じ boolean を返すので**採否は完全に不変**）。実データの最終盤面は golden 2653/420・
  user 33318/321・real 48401/304 と既知ベースラインに一致。
- **計測総数のばらつきについて**: 同じ golden でも 50/64 と振れる。壁時計予算（クラスタ締切・JointLNS の
  patience）下では各パスが評価しきる候補数が負荷で変わるため、この計数は本質的に負荷依存。
  flow の寄与自体は 0/6/0 で再現する（分離計測で確認）。UiState の KDoc にも「計測できた試行回数の下限」
  と書いてあるとおりで、絶対値でなく**対象の顔ぶれ**を見るための数字。

## 「どの日が塞いでいるか」をログへ＋パリティ不一致の次の一手（3.358.0, ユーザー指示「ログ強化する」）

実機ログで**直しに行けなかった2点**を埋めた。表示・ログのみ・スコア不変。

### ① 個人回数研磨の「希望固定×16」に日付が無かった
`RangePolish 残存: 桒澤美幸 休(希望固定×16), モニカ B4(range後回し×11)` — **どの日が塞いでいるか**が出ず、
データを直しに行けなかった。`ForbiddenDiag` は同じ理由で日付を名指ししている（`希望固定: 12/19(金)・12/20(土)`）
のに、こちらだけ件数だけで終わっていた。
- **日で決まる2理由（希望固定・禁止連続）だけ実日付を集める**（`blockDays`・件数は延べ／日は重複なし）。
  「候補なし」「range後回し」「不採用」は日で決まらないので従来どおり件数のみ。
- 実データでの出力: `桒澤美幸 休(希望固定×24: 12/2・12/4・12/5・12/6・12/7・12/10ほか6日)` /
  `モニカ B4(希望固定×72: 8/3・8/5・8/7)`。上位6日＋残り日数（ログ肥大を避ける）。
- 3データセット（golden/real/user）で実際に日付が出ることを確認。

### ② パリティ不一致のとき「ソースの乖離」か「.so が古い」か区別できなかった
外部レポートが `C++ soft が Kotlin より 113 小さい` から weekly の定義差を推定していたが、当時の main は
既に一致しており、実際は**古い .so** を見ていた（3.357.0 で確認）。旧文言は両方の値を並べるだけで、
読者が次に何をすべきか（コードを直す／再ビルドする）を判断できなかった。
- **差分（hard/soft それぞれ）を明示**し、`CIは言語跨ぎパリティ(golden実データ)を検証済み＝ソースが揃って
  いれば .so が古い可能性が高い（再ビルドを試す）` を添える。3.357.0 で CI が入ったからこそ言える文。
- 検証: ホストJVM **全452テスト green**。

## Kotlin↔C++ の言語跨ぎパリティを CI へ（3.357.0, 外部レポートの P0 主張を検証して判明した本当の穴）

外部レポートが「C++ soft が Kotlin より 113 小さい＝C++ weekly が旧式（勤務日のみ）のまま」を P0 として挙げた。
**まず現行ソースを実測して確認**したところ、`magi_native.cpp` は 3.345.0 のシフト別 weekly を実装済みで、
実データ（golden_state.json）に対し **Kotlin soft=3109 == C++ soft=3109**（hard も 0 で一致）。
レポートの数値（soft 933/1046・total 329・weekly 161・elite=9）は手元のどのログとも一致せず、**別の実行**（
おそらく .so が Kotlin より古いビルド）を見ている。**現行ソースにこのバグは無い**。

### ただし「その乖離を CI が捕まえられない」ことは事実だった
照合していたのは **C++ scalar vs C++ bit-op**（native-parity）と **Checker vs Evaluator**（3.337.0・どちらも
Kotlin）だけで、**Kotlin と C++ の間**を見るものが1つも無かった。実機の `NativeEval.parityCheck` 番兵は
発火するが、そのときネイティブは**黙って無効化される＝速度が落ちるだけで結果は正しく、気づけない**。
backlog#6 が警告していた失敗モードそのもの。

- **実測で証明**: C++ の soft へ **+113**（レポートが挙げたのと同じ差）を足す一貫した改変を入れると、
  **旧 CI は `0 mismatches` で exit 0** のまま通り、**新しい `--expect=` だけが MISMATCH で exit 1**。
- **実装**: `app/src/test/resources/golden_eval_expected.txt`（`hard=0 / soft=3109`）を両側から固定する。
  Kotlin 側は新テスト `NativeParityFixtureTest`、C++ 側は `host_parity_bench --expect=<file>`（新設）。
  native-parity ワークフローが実データ flat とこのファイルを渡す。片側だけ変えれば必ずどちらかが落ちる。
- 期待値を意図的に変えるとき（重み変更・族の定義変更）は **Kotlin と C++ の両方を直してから**ファイルを更新する、
  という手順もテストの KDoc に書いた。
- 検証: ホストJVM **全452テスト green**。期待値を 3110 に書き換えると Kotlin テストだけが落ちること、
  C++ を +113 ずらすと `--expect` だけが落ちることを両方とも実行して確認（教訓#30）。

### レポートの他の指摘（すべて対応済み・根拠つき）
- **c3n が希望固定の構造壁** = ForbiddenDiag(3.280.0)＋残存分析(3.288.0)＋EarlyStop 注記で既に名指し済み。
- **実現不能希望9件** = `ImpossibleWishGate`＋設定ミス9行で列挙済み。`pref` の計数からは
  `MirrorCore` が対称に除外しているのでノイズ源にはならない。
- **設定の構造的矛盾（Dﾃ apt合計>需要・桒澤B4・重複cons3n）** = 検査6-C/6b/DuplicateSeq が全部出しており、
  3.354.0 で「まだ狙える」からも除外済み。
- **エリート統合0改善なら予算縮小** = 手元の実機ログでは 改善1/採用1 で、常に0ではない。予算縮小は
  探索の変更＝A/B が要る（3.339.0 の判断を維持）。
- **後処理がほぼ全滅** = 3.339.0 のパス別テレメトリで実測・記録済み。
- **早期終了が SOFT 改善余地を切っている** = 3.341.1 で穏当版を5回ずつ測って否決済み（2/5・中立）。
- **文字化けの元ファイル未修復** = 3.282.0 で誤警告を直し、メッセージに再保存の案内を入れてある。

## 設定トグルが実際に何をしたかをログへ＋詰まった理由の可視化（3.356.0, ユーザー指示「オプションを減らせるようにログ強化する」）

設定タブ→詳細設定には調整トグルが6つあるが、**ログを見ても「ONにした意味があったか」が読めず、
減らす判断ができなかった**（`禁止連続の崩し範囲`・`行き詰まりからの立て直し方` に至っては実行の痕跡が一切出ない）。

### 設定の効き（新1行）
`TuningTelemetry`（読み取り専用の計数・`optimize()` 入口で reset）を新設し、実行ごとに1行:
```
設定の効き: ネイティブ加速=ON / Kotlin照合=ON(1240回) / 禁止連続の事前フィルタ=ON(48件の無駄な検査を省略・勤務表は不変)
 / 禁止連続の崩し範囲=ON(9361回中9307回は既定(前後1日)と違う範囲を探索) / 立て直し方=ON(この実行では観測なし)
 / 仕上げ最適化=ON(2回LAHCへ切替)
```
**ON なのに「この実行では観測なし」が毎回続くトグルは消してよい**、と数字で言える形にした。

**[自分の計測器の誤りを出荷前に是正]** 初版は「前後1日より**広い**日を返した回数」を数えており、golden で
`9222回呼ばれたが広がらず＝OFFと同じ` と出た。だが `breakableDaysFor` は covering run が無ければ**空を返す**
＝既定より**狭く**なる経路がある。「広いか」でなく「既定と違うか」で数え直したところ **9361回中9307回(99.4%)が
既定と違う**＝初版の表示は「消してよい」という逆の結論へ誘導していた。3.303.0 が「利得が一貫しない」とだけ
記録していた挙動の中身（大半は候補日を**絞っている**）が初めて数字で見えた。

### 残りのオプションも点検（3.356.1, ユーザー質問「他のオプションは大丈夫ですか?」）
最適化に効くオプションを全数棚卸しし、**自分が 3.356.0 で作った穴を2つ**見つけて塞いだ。
- **[自分の穴] Kotlin照合の回数が SA/LAHC しか数えていなかった**。`parityCheckEnabled` を読むのは
  **5箇所**（SA・LAHC・**ALNS**・**研磨チャンク**・起動時のフル照合）で、3.356.0 は SaOptimizer の2箇所しか
  計装していなかった。PORTFOLIO では ALNS/研磨が大半を占めるので、表示回数が実態より大幅に少なくなる。5箇所すべてへ。
- **[自分の穴] 並列ワーカーの効きが V5(高速)経路で読めなかった**。`RunMAGI_V5` は合計 iter しか出さず、
  チェーン数も内訳も無い（PORTFOLIO/ALNS には仮説別・チェーン別の行があるのに非対称）。`SaResult.chainWins`
  （各チェーンが全体最良を更新した回数）を追加し、`SAチェーン4本(最良を更新した本数=3)` を併記。
  1本しか勝っていなければ「並列を増やした効果は出ていません」と明示する。
  実測（golden・15秒）: workers=1 → 1本/total 440 ／ workers=8 → **4本(勝ち3本)/total 419**
  （4本止まりは 3.224.0 のコア数クランプ）＝このデータでは並列が効いていると数字で言える。
- **点検して問題なしと確認**: 計算の制限時間＝`TimeBudget`＋`TIME` 行／計算方式＝`V6Dispatcher` 行＋
  AUTO の解決先を設定画面に併記（3.192.0）／PORTFOLIO・ALNS の並列度＝`AdaptivePortfolio`・`AlnsChains` の
  仮説別/チェーン別行と「相異なる解=N件」。外観・片手モード・表示色・データ入出力は勤務表に影響しないため対象外。
- 検証: ホストJVM **全451テスト green**。実データで workers=1/8 を実走して行の切り替わりを確認。

### 詰まった理由が読めなかった2パスを補強
- **C3nPolish**: 実機ログは `候補日延べ4 正式評価0 C3n枝刈り0` だけで、なぜ1件も評価に進まなかったのかが
  読めなかった（実データではアリフの2セルとも本人希望で固定）。`希望固定で候補外N日`／`割当が範囲外N日` を追加。
- **C1TemporalFlow**: `DP候補12 flow失敗0 採用0回` で、12件が①行のc1が減らない②目的関数に負けた③厳密ピンを崩す
  のどれで落ちたか不明だった。`RejectCulpritStats`（3.302.0 と同じ5分類＋主因族）と `行c1が減らずN件` を追加。
  判定の順序（better → ピン）は変えていない＝採否は完全に不変。
- 検証: ホストJVM **全451テスト green**（450 + 新規1）。実データで `handleOptimize` を全OFF/全ON の2条件で
  実走し、1行が意図どおり切り替わることを確認。

## weekly の構造床と大きな族の職員別集約をログへ（3.355.0, ユーザー指示「ログ強化する」）

実機ログを読んで**実際に読めなかった2点**を埋めた。表示のみ・スコア不変。

### ① weekly（実機で最大の族＝合計307中156）に内訳が無かった
`残存分析` は `まだ狙える: … weekly 156件` としか言わず、3.345.0 で測った「構造床が44〜60%」という
事実がどこにも出ていなかった。**回数が7の倍数でないぶんは配置をどう変えても消せない**
（`weeklyDevOfBucket` の目標は `round(回数/7)` なので、合計との差＝余りが必ず偏差として残る）。
- `MirrorCore.weeklyFloorOfCount(c) = |c − 7*round(c/7)|` を新設（checker と同じ目標値から導出・単一ソース）。
  曜日ごとの日数上限（31日なら4回か5回）は見ないので**真の下限以下**＝過大に見積もらない。
- **実データ3件で 3.345.0 の実測値を厳密に再現**（golden 73/183・real 126/226・user 106/214）＝式の検算。
- `違反詳細` に `weekly内訳: 合計183件 = 構造床73件(回数が7の倍数でない＝配置では消せない) +
  曜日の寄せ方で減らせる110件 / 余地の大きい順: 佐藤直美 Pｼ 余地12 ; …` を追加（余地＝その (職員,シフト) の
  現在偏差 − その回数の床）。`残存分析` の「もう直せない」にも同じ床を反映（3.354.0 と同型）。

### ② 大きなセル族が「…他58件」で終わり、誰に集中しているか読めなかった
`違反詳細 c3(件数77・場所66箇所)` は DETAIL_CAP=8 で切れる。checker が出した場所（`cellFamilies`）を
**職員別に数え直すだけ**の集約行を追加（規則の再実装をしないのでドリフトしない）。
`[D] c3 集約（職員別・場所数の全件）: 吉江雄貴 8箇所 / 古泉 健一 8箇所 / …`。
DETAIL_CAP 以下の族は上に全件が出ているので出さない。c1 は下の「職員×窓ルール別」がより詳しいので除外。
- 実データで c3/c3m/c3mn/c42/groupViol に出ることを確認。
- 検証: ホストJVM **全450テスト green**（449 + 新規1＝床の値と、床が実際に達成可能であること
  （目標へ寄せた配置の実偏差が床と一致）を c=1..40 で総当たり確認）。

## 「まだ狙える」に構造的な apt を入れていた＋6b/6c の断定を実態へ（3.354.0, 実機ログから）

同じ実機ログ（10名/31日・必須1/合計307）を読み直して、**アプリ自身が同じ実行で「不可能」と証明している分を
`残存分析` が「まだ狙える」に入れている**のを見つけた。ログには両方が並んで出ている:
- `設定ミス: 桒澤美幸 の「B4」適切回数 — …「B4」は最低20回になります…適切回数1回は達成できず` （検査6b）
- `残存分析: … まだ狙える: … apt 30件 …` （3.288.0）

**桒澤の19件は追っても減らない**（担当={休,B4,有}・休10・有1 → 31日を埋めるには B4 が大半になる）。
`残存分析` の「もう直せない」は covU の構造床・c3n の証明済み壁・HF63 の学習しか見ておらず、
**個人の担当構成という第4の構造要因を見ていなかった**。

### 個人上限は SOFT なので「apt の下限」とは言えない — が「apt+high の下限」なら立つ
実機ログがその反例になっている。6b は「B4 は**最低20回**」と言うが、実際の解は **B4=19・休11（上限10を1超過）**。
つまり上限を d 日ぶん破れば count は forcedMin−d まで下がる。ただし **その d はそのまま high に移る**ので、
`apt + high >= forcedMin − 目標` は破れない（`count >= forcedMin − d` かつ `apt = count − 目標` なら
`apt + high >= (forcedMin − 目標 − d) + d`）。同じ職員の複数シフトで下界が立つ場合は d を共有しうるため
**職員ごとに最大値だけ**を取って合計する（保守的）。
- `V6SanityPort.structuralPersonalFloor(p)` を新設し、6b/6c が重複して持っていた「他シフトの上限合計」の
  計算も `otherShiftCapSum(p, i, k)` に集約（写しを残さない＝3.352.0 と同じ規律）。
- `残存分析` の「もう直せない」へ `apt+high のうちN件(個人の担当構成＝データ側)` を追加。
- **実データ**: golden **19件**（桒澤 B4・実機ログと同じ形）／real・user は **0件＝誤検知なし**（新しい行が出ない）。

### 6b/6c の「必ず出ます」を実態へ
断定が soft な個人上限を硬い制約として扱っていた（3.312.0 で `singleRuleLowerBound` に見つけたのと同じ型）。
「**他の担当シフトの個人上限を守る限り**最低20回」「差19回は、個人上限を破って別のシフトへ逃がさない限り
消えません（上限超過は上限違反として同じだけ残ります）」へ。結論（目標1は届かない）は変わらず、
根拠の限界だけを正直にした。読み取り専用・スコア不変。
- 検証: ホストJVM **全449テスト green**（447 + 新規2＝実機と同じ形で 19 を返す／他シフトに上限未設定が
  1つでもあれば 0＝6b/6c と同じ保守的判定）。

## 回数の違反が診断から消えていた＝countFamilies 新設（3.353.0, 実機ログから）

実機ログ（2026-12・PORTFOLIO 300s）を読んでいて、**内訳に出ている族が違反詳細にどこにも無い**のを見つけた。
- run1: `UnifiedCheck … c2=1` なのに `違反詳細 c2` の行が**1行も無い**。
- 同 run: `apt=29` に対し `aptLow(6件)+aptHigh(1件)` の各行の |回数−目標| を足すと **26**（3単位ぶん不明）。
  run2（同じデータ）も `apt=30` に対し表示は 25 で 5 足りない。

**原因**: `countViolations` は「1つの (職員,シフト) につき最重1クラス」（`markCount` の重み優先）。
low(90)/high(45) と同じセルに重なった apt(1.0)・c2(1.0) は**そこで消え、診断にも画面にも一切出ない**。
3.111.0 が `cellFamilies` でセル空間に対して解いた問題が、**回数空間には残っていた**。
- **実データでの規模**（3件で実測）: 重なりは golden 9セル・real 8セル・user 1セル。
  とくに **golden は c2=4 が4セルとも low の裏に隠れ、旧診断には c2 が1行も出ない**。

**対応**: `ViolationReport.countFamilies`（"i,k"→全クラスを重み降順・既定 emptyMap＝非破壊）を新設し、
`markCount` が `mark` と同じ形で全クラスを蓄積する（先頭は `countViolations` と常に一致）。
`V6SanityPort` の回数セクションはこれを列挙し、ヘッダも 3.282.0 と同じく breakdown と突き合わせて
件数と場所数が違えば両方出す（low/high/c2）。apt は breakdown に個別キーが無いので
`apt(件数29・場所10箇所): 目標割れ8箇所 + 目標超過2箇所` の専用行を1度だけ出す。表示のみ・スコア不変。
- **効果（実データ）**: golden で **`c2(4件)` が復活**、apt の可視箇所が 4→10、real の aptHigh が 0→7、
  low/high は `件数13・場所10箇所` のように amount と箇所数の別が読めるようになった。
- 検証: ホストJVM **全447テスト green**（446 + 新規1）。新テスト2件は `countFams` の蓄積を外すと
  **その2件だけが落ちる**ことを確認済み（教訓#30）。

### 併せて確認した実機ログ2本（同一データ・c3n事前フィルタ OFF/ON の A/B）
- **3.346.0/3.346.1 が意図どおり動いた**: 停滞ラッチは `EarlyStop`(214s/停滞45s)・`Watchdog`(最終改善153s・
  停滞45s)・`TIME`(探索198s) が**互いに整合**（3.345.0 のログは 290s と 13s と 275s で矛盾していた）。
  ワーカーは **8/8本が同時離脱**（run1=停滞シグナル@198s・run2=探索締切@275s）＝片肺運転は解消。
  `停滞見送り` は run1 で6回・run2 で2回＝確認窓が実際にきわどい発火を見送っている。
- **3.347.0 の帰属修正が効いている**: `C3RunPolish 不採用57件(ピン破り:1 重み悪化:56)(主因 low:36 c1:14 high:6)`。
  旧実装なら大半が「ピン破り」と誤ラベルされていた分が、正しく low/c1/high として出た。
- **3.298.0 の c3n 事前フィルタ ON（run2）は予測どおり**: AdaptiveBlockSwap の実候補 181→4・正式評価 48→4、
  不採用理由が `必須増48(c3n:48)` → `重み悪化4(c1:4)`＝詰んだ候補へ checker を呼ばなくなった。
  **採用は両方とも0**（3.298.0 の「できあがる勤務表は同じで速くなるだけ」と一致）。
- **正直な観測**: run1 は停滞で 198s で切り上げて total=305、run2 は 275s 使い切って total=307。
  **77秒多く使ったほうが悪い**＝PORTFOLIO の run 間ばらつき（既知）であって設定の優劣ではない。

## keep-best の順序を写す実装をなくす＝`reportComparator` 単一ソース（3.352.0）

3.349.2 で「`AdaptiveEliteArchive.compareReports` は `betterReport` の手書きの複製＝教訓#28 の型」と
報告だけして残していた項目。**同じ写しを探したら4箇所あり、うち1箇所は実際に古い順序のまま**だった。
- **[実在の取り残し・利用者に見える] 「他の案」の並べ替えが 3.287.0 以前の順序**（`V6NativeOptimizer:1197`）。
  採用案は `better`（hard→weightedScore→total）で選ぶのに、**画面に出す代替案の上位3件だけ hard→total で
  並べていた**＝重い違反を軽い違反の件数と交換した案が上位に来うる。3.287.0 は比較器を全部揃えたが、
  この「並べ替え」は比較器の形をしていなかったので棚卸しから漏れていた。
- **[表示の不整合] 仮説別・チェーン別ログの並び**（同 1216/1375）も hard→total で、`★採用` が先頭に
  来ないことがあった（採用は weighted 込みで選ぶため）。同じ順序へ。
- **[自分が作った写し] C1広域ビーム**（3.340.0）は `compareBy(hard, weighted, total)` と、その3キーを
  手で展開した `improved` 判定を持っていた。並べ替え・最小取り・改善判定の3箇所とも委譲へ。
- **対応**: `MirrorCore.reportComparator`（`Comparator<ViolationReport>`）を**唯一の定義**にし、
  `betterReport` もこれへ委譲。以後は「比較を足すときは写さずにこれを使う」。
  `V6SwapSuggester` の `compareBy(dHard, dWeighted, dTotal)` は report でなく**差分**の並べ替えなので対象外。
  共同LNS の Node コンパレータは3キーの後ろに c1/変更セル数の tie-break を足した**拡張**で、
  前半3キーは既に正しい＝構造を崩さないため据え置き。
- **検証**: ホストJVM **全446テスト green**（445 + 新規1）。新テストは
  `compareReports` を 3.287.0 以前の順序へ戻すと**これだけが落ちる**ことを確認済み
  （＝この順序を守るテストが他に無かったことの裏づけでもある＝だからドリフトした。教訓#30）。
  実データ3件の後処理研磨は golden 2653/420・user 33318/321・real 48401/304 で 3.351.0 と一致。

## 予算按分の敵対検証＝壁は再現せず、代わりに wishLocked の取り残し19箇所（3.351.0）

外部の敵対検証が挙げた「クラスタの予算超過が共同LNSの温存分を食い潰す（🔴高）」ほか3件を、
**推論でなく実測**（3データセット × 予算 28s/20s/14s/6s ＝12回）で確かめた。**主張は再現しない。**

| 予算 | reserve | クラスタ枠 | クラスタ実消費 | **超過** | LNS到達時の残り |
|---|---|---|---|---|---|
| 28s | 13.6s | 13.6s | 6.9〜13.6s | **−6.8s〜+8ms** | 13.6〜20.5s |
| 20s | 9.9s | 9.9s | 5.5〜10.0s | **−4.4s〜+14ms** | 9.9〜14.4s |
| 14s | 6.9s | 6.9s | 6.9s | **+1〜5ms** | 6.9s |
| 6s | 2.9s | 2.9s | 2.9s | **+3〜5ms** | 2.9s |

- **超過は最大14ms**（reserve 2.9〜13.6s に対し 0.1%）。`clusterStop` は18パスすべてに配られ、各パスは
  内側ループでも締切を見る（2.65.0/3.161.0）ので、超過はその粒度で頭打ちになる。
  **「LNS が cap=0 で即死」は12回中一度も起きない**（`remainAtLns` は常に reserve 以上）。
- **二重管理ではない**: reserve はクラスタの締切を削るために使い、LNS は `deadlineMs - tC1Lns` を読む。
  この2つは同じ量の受け渡しで、実測でも `remainAtLns ≈ reserve`（クラスタが早く終われば reserve より多い）。
- **HF70（「予算無制限」）は実測 0〜5ms**＝コメントの「安価」どおり。全体の終端超過も 6s 予算で 3〜10ms。
- **HF80（「暴走で hf67Cap=0」）は 70〜702ms**、`shouldStop` で全体締切に縛られる。hf67Cap が既定 3000 を
  割ったのは 6s 予算のときだけ（2958〜2964）＝**HF67 を飢えさせた事例はゼロ**。
- **小予算で共同LNS が既定（8s/6s）に届かないのは設計どおり**（残予算の比例配分）。14s 予算なら
  c1LnsCap 3.9s・personal 2.9s。「3s しか残っていないのに 14s 使う」ことはできない。

### 独立に見つけた実在の取り残し＝`wishLocked` 統一の19箇所

上記の検証中に、3.270.0（15箇所）・3.311.0（1箇所）で統一したはずの「実現不能な希望をロック扱いしない」規約が、
**7つの関数で生の `p.wish[i][j] < 0` のまま残っている**のを見つけた（`applyDayAssignmentPolish`／
`applyAlternatingSoftPolish`／`applyHF66IntraStaffRedistribution`×2／`trySwapShiftBetweenStaff`（HF67の要）／
`localPairwiseStaffSwap`／`localBestImprovement`（HF80の要））。3.311.0 は HF80 **本体**の摂動だけを直し、
そこから呼ばれるヘルパーを見ていなかった。さらに `V6SwapSuggester`（利用者向けの「直し方を探す」）にも
**11箇所**が同じ形で残っていた＝担当できないシフトへの希望が付いたセルは、修復提案の対象から丸ごと外れる。
19箇所すべてを `p.wishLocked` へ統一。
- **実データでは潜在**（golden/real/user とも実現不能な希望 **0件**／希望セルは 84/81/81）。よって
  `wishLocked ≡ wish>=0` で**結果はバイト一致**（golden 2653/420・user 33318/321、real は既知帯 48401/304）。
  3.319.0 の canDo ガード（同じく実データ0件）と同じ性質＝将来のデータで効く契約の修正。
- 検証: ホストJVM **全445テスト green**。

## 最終LNS 2本のピン却下を計測へ配線（3.350.0, 外部レビュー #1 の再指摘を実測して採用）
外部レビュー（対象は1世代前の main `a879dea`）が「最終LNS の `pinBlocks` が `pinBlocksAll` へ
マージされない」を再指摘。**指摘の前半は誤り**（両 LNS は `PinBlockAttribution` を構築しないので
`pinBlocks` は常に null＝merge を足しても何も増えない。3.349.0 で確認済み）だが、**結論は正しかった**。
- **実測で規模を確定**: 両 LNS の `exactPinRegression` 却下のうち「目的関数は採用を認めたのに
  ピンだけが止めた」件数を数えると **golden 9+3・user 0+0・real 1,898+0**。real は
  `V6HotfixPasses` 側の計測値（181）の**10倍以上が UI から抜けていた**＝inert ではない。
- **実装**: 両 LNS に `PinBlockAttribution` を持たせ、`isFinalCandidate` と最終 `valid` ゲートの
  `exactPinRegression` を `blocksImproving` へ置換（**同じ boolean を返しつつ記録する**ので挙動は不変）。
  `CyclicSwapResult` へ載せ、`runPostOptimization` の欠けていた2箇所で merge。計測範囲は 18→**20パス**。
- **効果（実データ・UI へ届く数）**: 総数 golden 38→**64** / real 181→**1,617**（user は 0→0＝
  このデータでは両 LNS がピン却下を出さない）。上位対象も入れ替わり、**古泉 健一/休(24)・
  モニカ/休(503) が新たに可視化**された（緩和候補の提示がその分だけ正確になる）。
- **挙動不変の確認**: golden(hard0/total420/c1 96)・user(hard4/321/52) はバイト一致。real は
  2回反復して両ビルドとも hard6/total304/c1 61 で一致（3.279.1 の既知のばらつきの内側）。
- 残る計測外は `EliteIntegrationPolish`(4)・`C1TemporalFlowPolish`(1)・`CombinatorialRepair`(2)・
  `C1RepairAnalysis`(1) の8箇所と、ピン保護を持たない探索本体(SA/ALNS/LAHC)。
- 検証: ホストJVM **全445テスト green**。

## エリート統合の敵対検証＝実バグ0・観測性2件（3.349.2）
`EliteIntegrationPolish`(379行) と `AdaptiveEliteArchive`(188行) をゼロベースで読み直した。
**実バグは0**（3.271.0/3.278.0/3.314.0 で3回レビュー済みの領域だけあって、keep-best・pin ガード・
debt・重複排除・スレッド安全性はいずれも健全）。**表示と可読性の2件だけ**直した＝スコアリング不変。
- **[観測性] 素材が使えないときの早期 return が無言だった**: `candidates.size <= 1` で
  `logs = emptyList()` を返すため、実機ログから「統合が走ったが素材が無かった」のか
  「そもそも呼ばれていない」のかが読めなかった。ただし PORTFOLIO 以外は毎回 `elites` が空＝
  1行足すと全実行でノイズになる（3.288.0 のログスパム対策と衝突）。**エリートはあったのに
  1件も使えなかったとき**だけ出す形にした＝「全ワーカーが同じ解へ潰れた」という意味のある信号
  （3.332.0 で距離0を可視化したのと同じ）。
- **[可読性] `withinDebt` の引数名が実引数と食い違っていた**: 名前は `root` だが渡しているのは
  `fuseGroup` の `currentBestReport`。「入口比の debt」と読めるが実際は**現在最良比＝窓はより狭い**。
  `baseline` へ改名し KDoc で明記。中間ノードの緩さは探索にしか効かず、採用は必ず
  `better` ＋ `exactPinRegression` が決める、という契約も同じ場所に書いた。
- **確認して問題なしだったもの**: `register` の重複判定（ハッシュ＋厳密一致、bridge→非bridge の昇格）／
  `snapshot` の quality/diversity/bridge 3母集団と `addUnique`／`compactRaw`（実機ログの archive は
  25〜61 で **rawCapacity=64 に届かず実運用では発火しない**）／`selectPairs`・`selectFusionGroups` の
  quota（3.314.0）／`fuseGroup` の `next.isEmpty()` → `continue`（3.278.0）／ビームが `bestSchedule` を
  明示保持している点（3.340.0 で C1BeamPolish に見つけた「最良を捨てる」型ではない）／
  `@Synchronized` と返却時の `copy2D()`。
- **報告のみ**: `AdaptiveEliteArchive.compareReports` は `MirrorCore.betterReport` の**手書きの複製**
  （キー順・Double の厳密比較まで現状は完全一致）。Comparator が要るので単純委譲はできないが、
  教訓#28 の「複製した瞬間に取り残される」型。等式を試験で固定するのが本筋。
- 検証: ホストJVM **全445テスト green**（443 + 新規2）。新テストは**旧挙動（無言）へ戻すと落ちる**
  ことを確認済み（他8件は通る＝教訓#30）。

## 業務前提（30名・1か月）をコードで確認する＋提示レポート21件の検証（3.349.0）
ユーザー確認「最大期間一ヶ月です」。前提は CLAUDE.md にしかなく、**コードはどこでも確認も強制もして
いなかった**（`dayCount` は盤面の列数から導出するだけ）。AskUserQuestion で方針を確定（推奨⭐4＝
事前診断に read-only の検査を足す／止めない）。
- **検査2j 新設**（`V6SanityPort`）: 期間>31日・職員>30名で `SettingIssue` を出す。**実行は止めない**
  （実行できるものを止めない）。64日を超える場合だけ「ビット演算の高速経路が使えず探索が遅くなります」を
  併記する（`C3nBitScan.usable(T<=64)` と C++ `SaChunk.useBits` が境界）。read-only・スコアリング不変。
  実データ3件（10名/31日）で**規模警告0件**＝誤検知なし（診断総数も 3/7/7 で従来と一致）。
- 検証: ホストJVM **全443テスト green**（439 + 新規4＝31日30名は出さない／32日は出すが速度注記なし／
  65日は速度注記つき／31名は別項目で出す）。

### 提示レポート21件の検証（コード実測）
**実在＝2件**（どちらも文書のドリフトで、コードの動作は正しい）:
- **`observedPinBlockedAttempts` の KDoc が stale**（報告#1 の周辺）。3.326.0 で計測を 9→**18パス**へ
  広げたのに `V6PostOptimizationResult` 側の KDoc だけ「9パス」のまま残り、しかも「入っていない」と
  名指ししていた CyclicSwap・C1 index 駆動・広域ビーム・ブロック交換・厳密日割当・交互最適化・
  曜日長方形・C3ブロック交換は**全部入っている**（`PinBlockAttribution()` を持つ関数を grep で数えて 18）。
  実際に計測外なのは `V6HotfixPasses` の外にある12箇所（C1JointLns 2・PersonalBalanceJointLns 2・
  EliteIntegration 4・C1TemporalFlow 1・CombinatorialRepair 2・C1RepairAnalysis 1）。**UiState 側の
  KDoc（3.327.0）は最初から正しかった**ので、両者が食い違っていた。実数へ揃えた。
  なお報告#1 の「2パスが `pinBlocks` を返すのに merge されていない」は**前半が誤り**＝両 LNS は
  `PinBlockAttribution` を構築しないので `pinBlocks` は常に null。merge を足しても何も増えない。
- **`C1PlateauDiagnosis` の観測元が KDoc に書かれていない**（報告#2）。却下を記録しているのは
  `applyC1WindowPolish` **だけ**で、C1 index 駆動/時系列フロー/広域ビーム/厳密窓修復は `plateau` を
  返さない。`NO_CANDIDATE` の文言が「この直し方では」と限定しているのはこのためだが、クラスの KDoc に
  出どころが無かった。明記した。
**[3.349.1/実測で閉じた] 差分前フィルタ（`staffObjective`/`c3FamCount`）の近似は inert**。3.84.0 から
「報告のみ」で残っていた2件（c3/c3m を窓#fire で数える＝チェッカーの run-deficit と別モデル／
apt/fair/weekly を集計しない）について、**捨てた候補すべてにフル checker を当てて「本来なら採用
されたか」を数えた**。結果は **golden 235件・user 899件・real 896件の skip に対し採用相当 0件**＝
この近似は良い候補を一度も落としていない。モデルを揃える改修は測れる利得が無いので**しない**
（3.290.0「不活性パスに投資しない」・3.310.1「測って支持されなければ入れない」と同じ判断）。
事実を `staffObjective` の KDoc に残し、この項目を閉じる。
**成立しない＝主なもの**: #4「`day2` のガードが無い」＝`Problem.cons1` 構築時の `d1>0 && d2>0` フィルタで
到達不能（`day1>0` のガード自体が冗長な防御）／#11「`WeeklyFairMarginalTest` は撤去済みパスのテスト」＝
実際は 3.267.0 の `weeklyMarginalAt`/`fairMarginalAt` を検証しており撤去された `applyWeeklyEqualizePolish`
とは無関係／#12「`V6LateOperators` は呼ばれていない」＝`V6NativeOptimizer` の2箇所＋`V6FinalPort` から
呼ばれている（`runPostOptimization` だけを grep した結果）／#16「T>64 で性能が 1/15 に落ちる」＝
前提が1か月なので到達しない（今回の検査2j がそれを画面にも出す）。

## 3.347.0 の報告のみ2件を消化（3.348.0）
3.347.0 で「未対応（報告のみ）」に置いた2件を、どちらも**測ってから**決着させた。

- **[① テスト穴を塞いだ] `C3nRowScan` のスカラー退避（T>64）が一度も通っていなかった**。
  `C3nBitScanTest` は days=21/10 しか使わず、65日以上でしか走らないスカラー分岐は**無検査**だった。
  オラクル（ビット経路）が使えない領域なので、**65日目に「どのパターンの末尾にもならないシフト」(休)を
  置く**と、日64を含む窓は `start = 65-d` の1本だけで必ず不成立になる——よって65日行のスカラー結果は
  先頭64日を切り出した行のビット結果と厳密に一致しなければならない、という形で比較可能にした。
  6シード×62日×5シフト＝1,860セルで fire 数・「崩せる日」の集合・行の復元まで固定。
  **バグを戻して落ちることを確認済み**（`scalarCoveringDays` の `lo..hi` を `lo until hi` にすると
  新テストだけが落ち、既存2テストは通る＝この分岐が無検査だったことの裏づけにもなる。教訓#30）。
  業務前提は最大31日なので実運用では到達しないが、この分岐は「日数上限を上げたとき」の保険であり、
  保険が動く証拠が無い状態だった。

- **[② 非対称は正当と実測で確定＝変更しない] `PersonalBalanceJointLnsPolish` に `patienceMs` が無い**。
  3.342.0 は C1JointLns にだけ停滞打ち切りを入れており、非対称に見えた。実データ3件で停止理由を
  測ると **Personal は一度も「期限」で終わっていない**（golden/user＝探索停滞・real＝個人構造下限到達）
  のに対し **C1 は golden/user が「最良が4000ms更新されず打ち切り」＝patience が実際に効いている**、
  real は「期限」。つまり Personal は候補が尽きて自分で止まる＝patience を足しても発火しない no-op。
  3.342.0 の「候補空間の広さの違いであって規約の非対称ではない」（教訓#37）を実データで再確認した。
  **測らずに対称化していれば、意味のないパラメータを1つ増やすだけだった。**
- 検証: ホストJVM **全439テスト green**（438 + 新規1）。エンジン・重み・スコアは不変（テスト追加のみ）。

## 「ピン破り」の誤ラベルで主因族が隠れていた（3.347.0, 新領域の敵対検証）
`C1PlateauDiagnosis` / `C3nBitScan` / `StateFingerprint` / `RejectCulpritStats` / `AdaptiveBlockSwap` を
ゼロベースで読み直した。**表示・診断のみ＝重み・採否・スコアは完全に不変**（実データで最終盤面が一致することを
確認済み）。
- **[実バグ] 採点でも落ちる手を「ピン破り」に数えていた**: `RejectCulpritStats.record` は呼出側の
  `exactPinRegression` の結果を**生のまま**受けて最優先で pinBroken へ振り分けていた。だが「ピン破り」を
  名乗れるのは**目的関数が採用を認めた手をピンだけが止めたとき**だけで、そのときだけ KDoc の
  「違反自体は改善しているので主因族を持たない」が成立する。隣の行の `pinBlocks.record` は
  `pinBad && isBetter(...)` と正しく絞っているのに、`rejectCulprits.record(rep, bestRep, pinBad)` だけが
  素通し＝**3.326.0 が `PinBlockAttribution` 側で厳密化した意味論の取り残し**（教訓#31）。
  **実データ計測（後処理研磨のみ）**: golden の c3mn 96件・fair 28件・apt 59件、user の c3mn 98件・
  fair 266件・apt 7件が**98〜100% 非改善**＝ほぼ全部が誤ラベルで、本当の主因族がログから消えていた。
  修正後は隠れていた壁が出る: golden c3mn「重み悪化125 主因 low:50 c1:42 high:33」／user fair
  「重み悪化289 主因 low:160 c1:125」。**3.303.0 で言った「low/high/c1 が族を問わない共通の壁」を、
  今度は正しい帰属で裏づけた**（当時は pin が全部かぶせていた）。
- **[実バグ] 手A の採点却下が C1Plateau に記録されていなかった**: `applyC1WindowPolish` の手A（同日交換）は
  ピン却下だけを記録し、同じ手が採点で落ちたときは何も残さない。手B は両方残すので、同じ
  (職員, シフト, 決まり) の集計でピン側だけが厚くなり `causeOf` が「回数固定で却下」へ寄る。どちらも
  i2 ごと＝同じ粒度なので対称に数える。
- **[表示] 合算・再フィルタ後に並べ替えていなかった**: `C1PlateauDiagnosis.build` は観測数の多い順に
  並べるのに、3.331.0 で入れた `mergedWith` と `refreshedAgainst` は並べ替えを引き継がず、
  `logLines().take(8)` と画面の一覧が「上位8件」でなく1巡目の順のまま出ていた。両方で並べ直す。
- **確認して問題なしだったもの**: `StateFingerprint` は `MagiState` の26フィールドを実際に数え、
  読まない3つ（schedule/shiftColors/extras）以外の23を全部読み、**入れ子の data class
  （Shift/Group/Staff/Range/C1Row/C2Row/C3Row/C41Row/C42Row）のフィールドも全部読む**ことをモデル定義と
  機械照合した。`C3nBitScan` の `matchMask` は `ushr l` のシフト距離が d≤T≤64 で必ず 63 以内、
  `valid`/`rangeMask` の 64bit 境界も検算して破綻なし。`C1PlateauDiagnosis` の「証明とは名乗らない」設計
  （`provenWalls` との住み分け）も一貫している。
- **未対応（報告のみ）**: ①`C3nRowScan` のスカラー退避（T>64）は `C3nBitScanTest` が days=21/10 しか使わず
  **一度も通っていない**。業務前提は最大31日なので実運用では到達しないが、その分岐は「日数上限を上げたとき」の
  ための保険なのにテストが無い ②`PersonalBalanceJointLnsPolish` には `patienceMs` が無い（3.342.0 で
  C1JointLns にだけ入れた。当時「候補が尽きるので不要」と実測しているので現状は正しいが非対称）。
- 検証: ホストJVM **全438テスト green**（437 + 新規1）。実データの最終盤面は golden(hard0/total420/
  weighted2653/c1 96)・user(hard4/322/33347/54) が修正前後で**完全一致**、real は同一ビルドでも
  48401〜48494 と揺れる（3.279.1 の既知の非決定性＝JointLNS の壁時計予算。同条件の2回目は両ビルドとも
  48401/304/61 で一致）。

## 停滞ラッチが降りない＋ワーカーの片肺運転（3.346.0, 実機ログ 2026-08-03 から）
実機ログ（PORTFOLIO 300s・workers=8・3.345.0 搭載機）を精読した。**まず 3.345.0 が実機で意図どおり動いて
いることを確認**（パリティ行 soft=1016 を breakdown から手計算で再現＝weekly=154 を含めて一致・ネイティブ探索は
有効・WeeklyRebalance が実際に1手採用）。そのうえで、ログ自身の矛盾から2件の欠陥を特定した。
**表示・観測の修正のみ＝重み・採否・スコアは完全に不変**（②は観測の追加だけで停止方針は変えていない）。
- **[① 実バグ・表示] 停滞ラッチが一度立つと二度と降りない**: `progressWatch` は「最終改善からの経過」が
  閾値を超えると `stagnationFired` を立てるが、**`shouldStop` の条件自体は単調でない**（改善が届けば偽に戻り、
  探索はそのまま締切まで走る）のに、ラッチだけ立ちっぱなしだった。実機ログの矛盾がそのまま証拠になる＝
  **`EarlyStop:「改善が無いため早期終了（290sで停止・停滞37s無改善）」** と書きながら、同じログの
  **`Watchdog:` 行は「探索終了時の停滞13s」**、**`TIME:` 行は「探索275.005s」＝予算を使い切っている**。
  さらに実害として、`ExtraRefine`（予算の残りを keep-best で追加精製する 3.102.0 の経路）が
  `!stagnationFired` を条件にしているため、**古いラッチを根拠に skip され残り約9秒が使われないまま**だった。
  改善を検出した地点でラッチと停滞時間を降ろす（1行）。
- **[② 機能バグ・観測のみ追加] 一瞬の停滞シグナルでワーカーが恒久的に離脱する**: 適応ポートフォリオの
  epoch ループは `while (nowMs() < deadline && !shouldStop() && ...)` の形。`shouldStop` は上記のとおり
  単調でないので、**たまたまその瞬間にポーリングしたワーカーだけが抜け、他は走り続ける**。実機ログの
  役割別worker秒を手で足すと **W0〜W3 が 275s ずつ・W4〜W7 が 115/116/116/116s** ＝
  **8本中4本が115秒で消え、残り159秒を半分の並列度で走っていた**。時刻も機構と一致する（改善の間隔が
  37/39/41/40s で `c3n壁=短37s` の閾値と同じ帯・最終改善78s＋37s＝115s）。
  **停止方針の変更は判断が要るので今回は入れない**（下記）。代わりに `AdaptiveWorkerOutcome` へ
  `exitReason`（締切／勝者確定／停滞シグナル／例外）と `exitAtSec` を持たせ、ワーカー別行と要約の
  **「ワーカー離脱=N/M本が締切前(...)」**で1行に出す（旧ログは役割別worker秒を手で足さないと気づけなかった）。
- **[未決・要判断]** ②の方針は2択で、どちらも筋が通る:
  (A) `shouldStop` をラッチして**全ワーカーを揃って止める**（早期終了の意味が正直になる。このログなら
  約116sで終了＝174秒の電池を節約する代わりに total 336→321 の改善を捨てる）／
  (B) ワーカーが一瞬のシグナルでは抜けないようにする（8本の並列度を守る。停滞が本物なら
  `V6FinalPort` 側の早期終了は従来どおり効く）。あわせて `stallHardMs` = 予算/8 = 37.5s は、
  このデータの改善間隔（37〜41s）に対して**きつすぎる**可能性があるが、これはパラメータ変更＝要 A/B。
- 検証: ホストJVM **全433テスト green**。①は `onPhase`（report=null で当該ブロックへ到達しない）と
  `ExtraRefine`（`!stagnationFired` ゲート）の両方を読み、解除が競合しないことを確認済み。

### 方針B を採用＝ワーカーは一瞬のシグナルでは抜けない（3.346.1, ユーザー選択「b」）
上の②に対しユーザーが **(B) ワーカーが一瞬のシグナルでは抜けないようにする**（8本の並列度を守る。
停滞が本物なら `V6FinalPort` 側の早期終了は従来どおり効く）を選択。**探索の採否・重み・スコアは不変**
（止め方だけを変える）。
- **実装**: epoch ループの `while` 条件からは `shouldStop()` を外し、**単調な条件だけ**（自分の締切・
  勝者確定）を残す。シグナルが立ったら `confirmStop` が**確認窓 5秒**のあいだ 250ms 間隔で再確認し、
  途中で偽へ戻れば走行を続ける（`survivedStops++`）、窓のあいだ真のままなら従来どおり離脱する。
  待機は suspend なので CPU を使わず、8本が並行に待つので壁時計の追加も窓1回ぶん。窓を5秒にした
  根拠＝発火は「閾値ぶん改善が無い」瞬間なので、改善間隔が閾値をわずかに超えるだけの near-miss なら
  次の改善は数秒で届く（実機の該当ケースは発火115秒→次の改善が約3秒後）。
- **[実測で捕まえた自分のバグ] 確認窓が探索締切でも待ち、後処理予約を食っていた**: `shouldStop` は
  停滞だけでなく**探索締切**（全体予算−後処理予約）でも真になる。初版は区別せず5秒待ったため、
  実データ計測で **探索 109.99s → 114.998s・後処理 8.48s → 4.95s** と後処理予約が削られた
  （ログのワーカー離脱理由も全員「停滞シグナル」と誤ラベルされ、Watchdog の「発火=なし」と矛盾して
  見えた）。`optimize(..., stopIsFinal)` を新設し（既定 `{ true }`＝従来どおり即離脱・非破壊）、
  `V6FinalPort` が `{ now >= searchDeadlineMs || !isActive }` を渡す。単調な停止は確認窓を回さない。
  修正後は **探索 110.026s・後処理 8.64s** と旧に一致、ラベルも「探索締切8本@109s」へ。
- **ログ**: ワーカー別行に `/停滞見送りN回`、要約に `停滞見送り計N回` を併記（きわどい発火が何回
  あったかが後から読める）。離脱理由は「探索締切／勝者確定／停滞シグナル／例外」の4値。
- **正直な限界**: 手元の実データ（120s・PORTFOLIO）では実効閾値が「通常=長108s」で停滞が発火せず、
  **片肺運転そのものは再現できていない**（発火したのは実機の 300s・`c3n壁=短37s` の局面）。
  ここで確かめたのは `confirmStop` の単体挙動（一瞬＝見送る／継続＝確定／単調＝即確定）と、
  探索時間・後処理予約が旧に戻ることまで。**フリート全体での効果は次回の実機ログで確認する**。
- 検証: ホストJVM **全437テスト green**（433 + 新規4）。

## 休を通常のシフト種として扱う＋weekly を7日周期のシフト平準化へ（3.345.0, ユーザー明示指示）
ユーザー指示「全体で休は『OFF 特殊』ではなく、通常のシフト種の一つとして扱います」→ 対象の層を確認
（AskUserQuestion）して**構築・探索の既定**を選択、続けて「weekly を7日周期のシフト平準化として、評価と研磨を
合わせます」。weekly の定義変更は目的関数の変更＝HF77 該当だが、**明示指示**として実施した。

### 休優先の既定2箇所を撤去（実測で中立と確認してから）
- `GreedyMirrorScheduler`/`SmartInitialScheduler` の残り埋めスコアにあった **`restBonus = -10`**（休だけ優遇）と、
  `V6SearchOperators.tryFixForbiddenRunViaAdjacentDay` の **`altOrder` 先頭に休**（代替候補で休を第一に試す）を撤去。
- **撤去前に測って中立を確認した**: restBonus を外すと 3データセットとも hard/covO/covU/low/high/c1 が**完全に同一**で、
  動いたのは休セル数（110→105 / 99→94）と total ±数のみ。altOrder は後処理研磨の**最終盤面が3件ともバイト一致**。
  ＝どちらも実質不活性だった（3.290.0「候補4件の不活性パス」と同じ形の確認）。
- **撤去しなかったもの（構造上の既定であって優先ではない）**: 新職員の行・伸ばした日・削除シフトのセルを休で埋める
  （`Ws1Ops`）／`allowed` が空のときの `?: restK` フォールバック／destroy-repair が「非希望セルを休へ退避してから
  埋め直す」構成（2.57.0/2.58.0 に nsp_bench 実測で選ばれた勝ち筋）。セルは必ず何かの値を持つ必要があり、
  休以外に自然な既定が無い。

### weekly の定義変更（評価4面＋研磨を同時に）
**旧**: 職員ごと、**勤務日(非休)** の曜日別カウントの round(勤務日数/7) からの L1 偏差和（勤務/休の二値）。
**新**: 職員ごと**シフトごと**に、そのシフトが入る日の曜日別カウントの round(そのシフトの回数/7) からの L1 偏差和。
`weekly = Σ_i Σ_k Σ_d |wd[i][k][d] − round(count[i][k]/7)|`。休は k の一つとして自動的に含まれる。
「夜勤が毎週水曜」と「休みが毎週月曜」を同じ式で捉える。重みは1のまま（HF77：重みの変更指示は無い）。
- **同時に動かした面**: `MirrorCore`(checker)／`Evaluator.fullEvalParts`／`DeltaEvaluator`（`wdCnt` を [S][7]→[S][K][7]、
  preview/commit/rebuild/weeklyAll）／`magi_native.cpp`（`fullEvalParts` と `SaChunk` の `wd` を S*K*7 へ、
  `contribWeekly(i)`→`contribWeeklyK(i,k)` で **old/nw の2バケットだけ**を before/after に入れる＝
  `contribRangeApt`/`contribFair` と同じ形なのでホットパスのコストは据え置き）。
- **研磨・marginal も同時に**: `weeklyMarginalAt(wd:[K][7], bucket, oldK, newK)` へ署名変更（勤務/休の±1でなく
  **シフト移動**を受ける）＋呼出3箇所（destroyRepairDay/Staff/Violations）／`applyAlternatingSoftPolish` の
  Hungarian 費用（当日の元シフトを失う項は行ごとの定数＝argmin を変えないので省く）／
  **`applyWeeklyRebalancePolish` の長方形交換を一般化**（旧: x=勤務・y=z=休 の特殊形＝休だけを「空き」とみなす。
  新: i が x について過剰曜日 j1・過少曜日 j2、相手 i' が j1 で z・j2 で x のとき 4セルを入替。旧形は新形の部分集合）。
- **検証**: ホストJVM **全433テスト green**。うち `WeeklyFairMarginalTest` は**ランダム状態80件×5変更で
  marginal cost == checker の weightedScore 差分**を照合しており、新定義でもこれが通る＝marginal と checker が一致。
  `DeltaEvaluatorTest` の Δ==フル も green。native parity **2,996,665手 mismatch=0**（bit-op ×2.04）。

### 実データの数字（正直な記録）
同じ入力盤面を新旧で評価: weekly は **golden 59→183 / real 52→226 / user 49→214**（3.1〜4.4倍）。
そのうち**構造的な下限**（回数が7の倍数でない (職員,シフト) は偏差が0にならない）が **73 / 126 / 106**＝
全体の 44〜60%。減らせる余地は 110 / 100 / 108。
後処理研磨後（同一seed）: golden hard 0・c1 104→**96**・weekly 65→188 ／ real hard 6・c1 63→**61**・weekly 50→181 ／
user hard 4・c1 54→54・weekly 49→208。**他族はほぼ中立**（golden は c1 −8 と引き換えに low +2、
real は c1 −2、user は同値）。
- **重み1のままだと weekly は「生の件数(total)では最大の項」だが「重み付き(weightedScore)では小さい」**
  （golden: total 420 中 188＝45% だが weighted 2653 中 188＝7%。c1 は 96×15=1440）。実際、研磨後の weekly は
  下限 73 に対し 188 とほとんど減っていない＝最適化器は weekly より重い族を優先しており、これは重み設定どおりの挙動。
  **重みを上げるかは業務判断**（HF77＝明示の数値指示が要る）ため今回は 1 のまま据え置き、判断材料として数字を残す。

### 探索フル経路での追試（コミット時の「他族はほぼ中立」を訂正）
コミット時点の「他族はほぼ中立」は**後処理研磨だけ**の測定だった。`handleOptimize`（60秒・workers=4・
ホストJVM＝Kotlin経路）で**3回ずつ**測り直すと、単発では見えなかった差が出た:
- **HARD は全データセットで不変**（golden 0/0/0・real 4,3,4→4,4,4・user 4,4,4→4,4,4）。
- c1（重み15）は golden 中央 99→100・real 中央 69→**68** で帯が重なる＝中立。
  **user_state だけ 62→84 と一貫して悪化**（新は3回中2回が84以上）。
- 最初の1回だけ見たときは「c1 が3件とも悪化」に見えたが、反復すると golden/real は重なった＝
  **1回計測で A/B 判定してはならない**（3.290.0 の教訓）を再確認。
weekly の重み付き寄与は c1 に比べれば小さい（user: weekly 184×1 に対し c1 84×15=1260）が、
mass が3〜4倍になったぶん探索の受理予算を食い、user では c1 を1〜2割押し上げている。
**weekly の重みを下げれば（＝重み付き寄与を旧定義と同程度に戻せば）この分は消える見込みだが、
重みの変更は HF77＝明示の数値指示が要る**ため据え置き、判断材料として数字を残す。

### テスト修正（新定義に合わせて意味を直したもの）
- `AptPolishTest`/`FairPolishTest` の「1パスで apt/fair=0 まで解消」は、T=7（各曜日が1回だけ＝weekly が最も強く効く）
  の盤面で weekly と正面から競合するため成立しなくなった。**本来固定したかったのは「1パス内で自己振替が反復される」**
  （旧実装は1単位で打ち切っていた）なので `applied` と単調減少で直接見る形へ。fair は追加パスで 0 に到達することも
  固定、apt は目的関数の最適が apt=0 と一致しない（実測 apt=5）ため keep-best（悪化しない）を固定する形に是正。
- `SmartInitialSchedulerTest` の対照は「restBonus で全セルが休へ倒れる」ことに依存していた。restBonus 撤去で
  簡易作成は最少回数のシフトを選び続けて 休/X の交互になり、緩い「5日窓 X≥2」を偶然満たす。交互配置では満たせない
  **「3日窓 X≥2」**へ変更して対照を成立させ直した（実測 greedy c1=5・smart c1=0）。
- `C1BeamPolishTest` の初期 total 10→19（盤面は不変・weekly の内訳が変わっただけ）。

## 人員不足診断の「充足可能」と「どう組んでも解消できません」の矛盾を解消（3.344.0）
3.343.0 の副次効果（診断が `allBlocked=true` になったので停滞打ち切りが発火するか）を確認したら、
**自分の主張が過大だったと判明**し、その追跡で別の実害が出た。**表示・診断のみ＝スコアリング不変**。
- **[自己訂正] 3.343.0 の「実害②」は、このデータでは成立しない**。診断が `allBlocked=true` になっても
  実効閾値は「通常=長108s」のままだった。3.281.0 の条件を追うと
  `bestHard(4) <= hardFloor(0) + nonCovU(1)` が偽＝**covU=3 が構造床(0)を超えているので、c3n 側の判定に
  到達する前に止まっている**。診断の誤りは停滞打ち切りが発火しない原因ではなかった（covU=0 かつ
  c3n>0 のデータなら実在するが、手持ちデータでは確認できない）。実害①（利用者への誤った期待）は実在。
- **[実害] サマリと説明が矛盾していた**: 実データ（real/user）の CoverageDiag は
  `充足可能=3 不能=0` と出しながら、3枠すべての説明が「**現在の希望のままではどう組んでも解消できません**」。
  `verdict` は「担当できる人数 >= 必要数」という静的判定で、希望を1件変えれば直りうる枠を FIXABLE に
  残すのは正しい（3.263.0 の意図的な区別）。だがそれだけを数えたサマリは説明と食い違う。
- **修正**: 判定を文字列でなく値として持つ。`CoverageShortfall.blockedNow`（reason と同じ根拠＝空き番が
  居るか／`findCovUChain` で玉突きが実在するか）＋ `CoverageDiagnosis.blockedNowSlots` /
  `allBlockedNow`（`allInfeasible` とは別軸）。ログとカードの見出しを
  「不足3人 — 充足不可0枠 / 充足可能3枠（うち3枠は いまの希望のままでは不能）＝この希望・担当のままでは
  人員不足は減りません」へ。枠のチップも「充足不可 / **今は不能** / 充足可能」の3値に。
- ~~**[未着手・要A/B]** `allBlockedNow` をウォッチドッグへ配線すれば「covU も壁」と判定して早期終了できる
  （real/user は最終改善が 58s/16s なのに 120秒を使い切っている）。ただし 3.341.1 と同じ「品質と電池の
  交換」の領域なので、判定基準を先に固定した A/B が要る。今回は診断の矛盾解消までに留める。~~
  **→ 3.361.0 で実測のうえ却下**（早期終了は keep-best-safe でない・sample_v6 が遅延バースト soft 改善の実在を
  示す・動機データ消失で直接 A/B 不可・golden は hard=0 到達で no-op。詳細は「covU-blocked のウォッチドッグ
  配線を実測して却下」節）。**再提案しない**（real/user 相当の実データつき明示指示があった場合のみ）。
- 検証: ホストJVM **全432テスト green**（431 + 新規1）。UI 層はホストでコンパイル不可＝静的確認のみ。

## 禁止連続診断が「崩せる」と誤主張していた＝隣接日調整にも pref の代金を勘定（3.343.0）
3.341.1 で観測した「real/user は 120秒かけても c3n=1 が消えないのに ForbiddenDiag が構造壁と証明しない」
を追った。**利用者から見れば「なぜ1件残るのか」が分からない状態**だったので、診断が何と言うかを測った。
- **測定（同一盤面で診断と研磨の両方を実行）**: 診断は「8/6(木) **有** ADJACENT 『休』へ変更＋隣接日調整で
  成立」＝**崩せる**と主張。しかしそのセルは**本人希望**で、破ると **pref +1（9000）** に対し
  **c3n −1（7000）**＝正味の HARD は減らず weighted は +2000 悪化＝`betterReport` は決して採用しない。
- **原因**: 3.311.0 で PINNED 判定へ `prefCost` を入れたとき、**ADJACENT 分岐に入れ忘れていた**。
  隣接日調整はその職員の複数日を動かすので、本セルだけでなく**行全体の希望違反**が増えうる。
- **実害2つ**: ①利用者へ「探索が見つけていないだけ」という誤った期待を与える ②`allBlocked` が偽のままなので
  3.281.0 の停滞打ち切り（全 run 塞がりなら短い閾値へ）が発火せず**余計に時間を使う**。
- **修正**: `netHardImproves`（c3n も pref も HARD の件数和なので同じ単位で足して比べる）を新設し、
  隣接日調整を当てた行が正味で改善するときだけ ADJACENT を名乗る。改善しない理由が「希望を破る代金」なら
  `prefBlocked` へ計上して **PINNED**（本人希望＝行動につながる説明）に分類する。
  実データで `8/6(木) 希 PINNED 本人希望=希` ＋ run の hint「全セルが塞がっています（希望固定: 8/6(木)）…
  周辺の希望を1件調整するか、担当を追加してください」へ変わり、`崩せる=false` になった。
- **[撤回した実装] C3nPolish への隣接日調整の接続**: 「正式評価0・C3n枝刈り14」＝研磨がフル checker を
  1度も呼んでいなかったのを見て、枝刈りで落ちた候補に `tryFixForbiddenRunViaAdjacentDay` を接続する版を
  実装したが、**実データで一度も発火しなかった**（希望固定セルは `movable` ガードで先に弾かれ、残りの
  候補では隣接日調整が成立しない）。真因でもなかったので 3.290.0 の「不活性パス」の轍を踏まないよう撤回した。
- 読取専用の診断のみ＝エンジン・重み・スコアは完全に不変。検証: ホストJVM **全431テスト green**
  （430 + 新規1）。新テストは**バグを戻すと実際に落ちる**ことを確認済み（`(1, ADJACENT)` で失敗、教訓#30）。

## C1共同LNS が改善0のまま8秒を使い切っていた＝停滞打ち切り（3.342.0）
3.339.0 のパス別テレメトリで残っていた最大の未調査項目（golden 8039ms=50%・user 8010ms=36% を使って
採用0）。3.340.0 と同じ手順で**削る前に何にその時間を使っているか測った**。
- **計測（実データ3件・後処理研磨のみ）**: 3件とも **`maxMillis` を使い切って終わる**（停止=期限）。
  候補は 4.5〜7.3 万件も作れるので尽きない（兄弟の `PersonalBalanceJointLnsPolish` が数百ms〜1.3秒で
  終わるのは候補空間が狭くて尽きるため＝規約の非対称ではなく空間の広さの違い）。
- **golden と user は best を一度も更新しないまま 7.4〜7.7 秒を使い切る**（＝全部が空回り）。
  real だけが 2.9s / 4.4s / 6.8s の3回改善する。
- 候補の **43〜52% は debt 予算で捨てている**（user は 45,432/87,065、うち「合計」debt が 40,411＝89%）。
  ただしその判定は**フル checker を呼んだ後**なので捨てる候補にも全額払っている。安く先に判定する
  方法（total の差分予測）が無いため現状は許容し、事実として KDoc に記録した。
- **修正**: `Config.patienceMs`（既定4秒＝real の「最初の改善まで2.9秒」に1.4倍の余裕）。最良が
  この時間更新されなければ打ち切る。keep-best は不変＝早く止めるだけで退化しない。
- **A/B（patience 3/4/5秒 vs 無効）**: **最終盤面は4条件とも完全に一致**（golden 2469/306/c1 104・
  real 49213/172/c1 59・user 33159/162/c1 54）。後処理全体は golden 16.3→13.3s・user 19.0→15.3s
  （real は改善が続くので打ち切られず不変）。
- **[正直な記録] real は決定的でない**: 既定4秒を本体へ入れた後の実測は 49223/170/c1 58（c1 と total は
  改善・weighted は +10）で、これは 3.279.1 の既知のばらつき帯（49213〜49326）の内側＝打ち切りの効果
  ではなく経路依存。golden/user は既知ベースラインとバイト一致。
- **[テストの限界]** 「patience を伸ばすほど良くなる」単調性は時間ベースで実行速度に依存するため固定
  しない（3.340.0 のビームは回数ベースだったので単調性まで固定できた）。固定したのは keep-best のみ。
- 検証: ホストJVM **全430テスト green**（429 + 新規1）。C++ 無変更＝native parity 影響なし。

## 早期終了で余った予算を soft へ回す案を測って否決（3.341.1, 敵対レビュー A5）
レビューの A5「c3n の構造壁を証明したあと、残 soft を10〜25秒だけ追う」。**コードは1行も変えていない**
（否決の記録のみ。詳細と数値は `docs/algorithm_portfolio.md` の「実測で否決した提案」）。
- **前提が実データで成立しなかった**（120s・workers=4）: c3n=1 が残る real/user は **ForbiddenDiag が
  構造壁と証明せず**（実効閾値は通常=長108s のまま）、かつ 120s を全部使い切って時間が余らない。
  時間が余るのは **hard=0 到達時**（golden で 55s/120s＝54%を残す）で、提案の想定条件とは別物。
- **穏当版（`ExtraRefine` の `stagnationFired` スキップを外す）を5回ずつ測って否決**: 事前に固定した
  基準「5回中4回以上が現行の中央値より良い」に対し **2/5**。+17秒で weighted 中央 2319→2393・
  平均 2362→2356＝**中立**。
- **早期終了そのものの撤去**は中央 −3.5%・平均 −4.1% だが **U検定 p≈0.075 で有意でない**（分布の幅326に
  対しサンプル5では示せない）。時間は 2.3 倍。これは品質と電池・待ち時間の交換＝**業務判断**で、
  3.281.0（実機ログで48秒の節約を成果として記録）と正面から衝突するためここでは変更しない。
  ただし「予算120秒の指定に51秒で返している」という事実は記録しておく。

## 複合手を原子化＝「意図を果たさない大きな破壊」をやめる（3.341.0, 敵対レビュー A2 を測って採用）
レビューの A2「複合手（`opBlockFill`/`opLns`）は部分適用でなく原子拒否にすべき」。3.334.0 の時点では
「文書と実装は一致しているし、直すと近傍が変わるので測ってから」と保留していた項目。
- **前提の計測（SA 8秒・実データ3件）＝レビューの想定より極端だった**: 希望固定は 26〜27%（81〜84/310）で、
  **`opBlockFill` の部分適用は golden で 100%**（窓長14に固定が1つも入らないケースがゼロ）・real/user で 87%。
  `opLns` も 80〜82%。つまり原子拒否にすると **golden では `opBlockFill` が完全に no-op になる**。
  「近傍が2種消える＝悪化」と予想したが、**測ったら逆だった**。
- **A/B（判定基準を測る前に固定: ①HARD 中央値が悪化するデータセットが1つでもあれば不採用
  ②weighted は符号検定 7/10 以上）**: golden hard 0→0・weighted 中央 2478→**2464**・6/10 ／
  real hard 中央 **5→4**・weighted 中央 42134→**34224**・8/10 ／ user hard 4→4・33306→**33270**・7/10。
  **悪化したデータセットは無い**（基準①クリア）。全体 21/30（p≈0.02）だが **golden 単体は 6/10 で基準②未達**＝
  正直に記録する。**反復数は 2.2〜2.9倍**（見送りは `applyCell` を呼ばないので1反復が極端に安い）。
- **なぜ効くか**: 途中が抜けた窓は「窓を埋める」という手の意図を果たさないのに多数のセルを一度に壊す＝
  **意図を果たさない大きな破壊**。受理されればスコアが悪化し、されなければ巻き戻しコストだけ。
- **[正直な限界] パイプラインでは中立**（30秒 V5＋後処理・各3回）: golden 中央 2469→2434・
  real hard 5→5(40640→40527)・user 33232→33232。**SA 単体で見えた real の HARD −1 は消える**
  （後処理が差を吸収する）。3.310.1 の「前提の確認と効果の確認は別」の再確認。悪化はしない。
  採用は「SA 単体で明確に良い／パイプラインで悪化しない／意味論として正しい／実装が単純」を根拠にする。
- **[テストできない]** 近傍の内部（部分適用したか）は最終盤面から観測できず、`opBlockFill`/`opLns` は
  `run()` 内のクロージャなので 3.336.0 のように `internal` 化して直接呼ぶこともできない。
  根拠は 10シード×3データの実測のみ＝**回帰は検出できない**。
- Kotlin（`SaOptimizer`）と C++（`runSaChunk`/`runLahcChunk` の2コピー）へ同型に適用。
  検証: native parity **2,996,665手 mismatch=0**（bit-op ×2.23）・ホストJVM **全429テスト green**。

## C1広域ビームが探索を長く回すほど成果を捨てていた＝最良保持と停滞打ち切り（3.340.0）
3.339.0 のパス別テレメトリで「このパスが後処理の 27〜42%」と出たので、**何にその時間を使っているかを先に測った**。
- **計測（実データ3件・全8回の呼出）**: 8回**すべてが maxSteps=60 を完走**し、1回あたり約30,000回のフル checker
  （このパスの時間の82〜88%）。`rebuildTargets` は 30〜60ms、重複排除は 75〜320ms＝どちらも誤差。
  **しかし最後に最良が更新されてから 32〜60 ステップが空回り**（8回中4回は根を一度も超えない）。
  改善の間隔は最大15ステップ。
- **[実バグ] ビームが最良を保持していなかった**: 各ステップで全メンバーを子に置き換えるので、
  探索が進むほど途中の良い盤面を失う。返していたのは**最終ビームの最小**だけ。
  golden の生盤面（beamWidth=8, seed=7）で実測すると **maxSteps=4 で weighted 2859 まで下がるのに、
  6以降は根(2985)へ戻り採用0**＝**長く回すほど結果が悪くなる**。最良保持を入れると全 maxSteps で
  2834（旧の最良より良い）で一定。
- **修正**: ①各ステップのビーム先頭を `bestEver` として保持し最終候補にする（最終ビームは観測列に
  含まれるので**この手だけでは絶対に退化しない**）②最良が `patience`(既定20) ステップ更新されなければ
  打ち切る（観測した改善間隔の最大15に余裕を持たせた値）。rng の消費順は変わらない（早く止めるだけ）。
- **A/B（同一スクリプト内で連続実行＝負荷条件を揃えた比較）**: 3データセットとも**結果は完全に一致**し、
  本パスの所要は golden 7.0→3.2s・user 14.6→7.7s・real 8.1→6.9s、POST 全体は golden 21.1→19.2s・
  user 32.5→25.3s・real 21.9→21.6s。浮いた時間はクラスタ締切配下の後段へ回る。
  **[正直な記録] golden は決定的ではなかった**: 負荷が変わると 2469→2490 まで動く（壁時計予算の
  JointLNS 由来＝3.279.1 の既知事項）。これまで安定して見えていたのは実行条件が揃っていたため。
  よって A/B は**同一スクリプト内の連続実行でのみ**判断する。
- **[ablation] このパスは撤去しない**: 丸ごと外すと user が 33159/162/c1 54 → **33232/165/c1 55 と悪化**
  （golden は不変）。実データで実際に効いているので、打ち切りはしても機能は残す。
  3.317.0（寄与ゼロを実測して撤去）とは判断が分かれた対の実例。
- 検証: ホストJVM **全429テスト green**（427 + 新規2）。新テスト
  `moreStepsNeverProduceAWorseResult` は**最良保持を戻すと実際に落ちる**ことを確認済み
  （maxSteps=6 で 2859→2985、教訓#30）。C++ 無変更＝native parity 影響なし。

## 後処理のパス別テレメトリ＝時間の行き先を見えるようにする（3.339.0, 敵対レビュー A4 の計測半分）
A4 は「族ごとに採用・改善・消費 ms を記録し、0改善が続く族は予算縮小」。**計測の半分だけ**を入れた
（縮小は探索の変更＝A/B が要る。3.310.1 で同じ手順を踏んで否決した先例がある）。**ログのみ・挙動不変**。
- **見えていなかったこと**: 3.269.0 の区間分割は「巡回研磨」が18パスの合計で、その中でどのパスが
  時間を食っているかが読めなかった。実測（後処理研磨のみ・実データ）:

  | | golden | user |
  |---|---|---|
  | C1共同LNS | 8039ms(50%) | 8010ms(36%) |
  | C1広域ビーム | 4371ms(27%) | 8821ms(40%) |
  | C1時系列フロー | 1233ms(7%) | 1639ms(7%) |
  | 個人回数共同LNS | 682ms(4%) | 1322ms(6%) |

  **上位3〜4パスで 84〜89%**。しかも採用は golden で C1共同LNS=0・C1広域ビーム=手数0・
  個人回数共同LNS=1、user は上位3つとも 0。
- **実装**: `passMs` に24箇所の呼出を計測して `後処理パス別 計Nms: …` の1行を出す（多い順・上位8）。
  各パス自身の「採用N回」行と突き合わせれば「時間を食っているのに採用0」が読める。
- **予算縮小はやらない**: 「このデータで採用0」は「無意味」ではない。C1共同LNS は 3.271.0 の実測で
  real_state の HARD 7→6 を出しており、**効くときに効く高コストパス**。縮小は A/B してから（教訓#11:
  観測ゼロを機能ゼロと決めつけない）。
- 検証: ホストJVM **全427テスト green**。実データの最終盤面は golden 2469/306/c1 104・
  user 33159/162/c1 54 で**完全に不変**。

## ピンの不変条件を試験にする＝何が本当に守っているのかを実測（3.338.0, 敵対レビュー A2）
「全 operator の pin/allowed 事前条件」を、**読んで確認した**から**試験が主張する**へ上げた。
- **固定した不変条件**（後処理チェーン全体を通して）: ①実現可能な希望（`wishLocked`）のセルは動かない
  ②入口で満たしていた厳密な回数固定（`staffRange` の lo==hi）は崩れない。golden_state（実データ）と
  違反を多めに含むランダム12状態で確認（希望固定セル・厳密ピンが実際に存在することも数字で assert）。
- **実データでの現状**: golden/real/user とも固定 84/81/81 件で**動かされた 0 件**＝守れている。
- **[重要な発見] 試験が捕まえる範囲を実測して確かめた**（3.336.0 の「バグを戻しても通るテスト」の反省）:
  - 14箇所の `movable` を**全部**潰す → 落ちる ✓／`exactPinRegression` を無効化 → 落ちる ✓
  - `movable` を**1箇所だけ**潰す → **落ちない**。希望を破ると pref(9000/HARD) が増え、そのパスの
    `isBetter` が必ず却下するため。**不変条件を最終的に強制しているのは採否であり、`movable` は
    「必ず却下される手を作らない」ための事前フィルタ**だった。3.334.0 の SA 近傍の欠落が「誤った
    勤務表」でなく「反復の空振り」で済んだのと同じ理由。
  - よってこの試験は**不変条件**を守るが、**個々のガードの有無**は守らない。ガードの網羅は grep で
    数える（教訓#31）＝現状 `movable` 定義14・`exactPinRegression` 呼出20（V6HotfixPasses）。
- 検証: ホストJVM **全427テスト green**（425 + 新規2）。エンジン・重み・採否は一切不変（試験のみ）。

## 目的関数の二重管理を試験で塞ぐ＝Checker と Evaluator のパリティ（3.337.0, 敵対レビュー A1）
残っていた最優先項目。「同じ意味が2箇所に書かれていて、片方だけ変えると静かにずれる」構造に対して、
**C++ 側は native-parity CI が守っているのに、Kotlin の Checker と Evaluator を突き合わせる試験が無かった**。
- **現状は一致していた**（実データ3件で差 0.0）。壊れているのを直したのではなく、**壊れていないことを
  試験で固定した**。ドリフトの履歴は実在する（covO 0.5 vs 1.0／c1・c3mn の HF77 変更）。
- **固定した等式**（`fullEval` の辞書式パックの定義そのもの）:
  `Checker.weightedScore − HARD族の重み付き寄与 == Evaluator.soft` ／ `Checker.hard == Evaluator.hard` ／
  `breakdown を重みで組み直すと weightedScore`（Checker 自身の内部整合）。
- **族の網羅を数字で見せる**: ランダム側は need/希望/担当不可/連続パターン/群・スキル群まで入れた状態を
  60個作り、**19族すべてが少なくとも1回は非ゼロ**になることを確認する。発火していない族は
  「重みがずれても緑」になるので、網羅を確認しないと守れていない。
- **試験が本当に捕まえることを確認**: 実際に起きたドリフト（covO 1.0→0.5）を戻すと**2テストとも落ちる**
  ことを実行して確かめてから採用（3.336.0 の「バグを戻しても通るテスト」の反省＝教訓#30）。
- あわせて `MirrorKeys.weights` の stale コメント（「窓の要件(c1)=5」だが実装は 15＝3.253.0 の HF77 変更が
  未反映）を訂正し、「ここを変えたら Evaluator のリテラルと C++ も同時に」と、どの試験が守るかを明記した。
- 検証: ホストJVM **全425テスト green**（423 + 新規2）。エンジン・重み・採否は一切不変（試験とコメントのみ）。

## ピンガードの抜け穴・c1ブーストの weighted 迂回・殻の失敗パス（3.336.0, 敵対レビュー）
「殻レビュー」と「敵対検証レポート」を1件ずつ照合。**H1 と H3 は自分が作った/残した穴**で、どちらも
「守っていると宣言した契約」が反証されるもの。既修正・過大評価は根拠を示して分けた。
- **[H1/P0・自分のバグ] `strongPerturbFlat` が希望固定を見ていなかった**: 3.334.0 で SA/LAHC の近傍4種に
  ガードを入れたが、**ネイティブ経路の停滞脱出だけ取り残した**。Kotlin 側 `strongPerturb()` は `opSingle()`
  経由で守られるのに、`runWorkerNative` が呼ぶ平坦配列版は素の代入。**既定はネイティブ経路**なので、
  Conductor が STRONG_PERTURB を引くたびに入口で載せた実現可能な希望を上書きしていた。最終解は
  fullEval 番兵と keep-best が守るが、「探索は実現可能な希望を動かさない」という契約は破れていた。
  `opSingle` と同じ形（日を4回まで引き直し・それでも固定なら見送り）へ。C++ 側に同等の摂動は無い
  （Conductor の境界処理は Kotlin が持つ）ことを grep で確認。
  **[テストの落とし穴] 最初に書いた間接テストはバグを戻しても通った**＝ホスト JVM は `.so` を読めず
  `NativeGate.usable` が false になり、**唯一の呼出元が走らない**。`internal` 化して直接呼ぶ形へ書き直し、
  「バグを戻すと落ち、修正で通る」ことを実際に確認してから採用した。
- **[H3/P1] c1 ブーストが `betterReport` を迂回して weighted を悪化させられた**: `V6LateOperators.gate` は
  `betterReport` が偽でも「c1 が減る横移動」を採る例外(HF537互換)を持つ。ガードの `lim = 200×high + 120×low`
  は**目的関数の重み(high45 < low90)と大小が逆**なので、high−1/low+1 の入れ替えは lim を下げつつ weighted を
  悪化させられる。反例（数値で確認）: c1 5→4・high 2→1・low 1→2 は lim 520→440 ✓ / 生の件数 8→7 ✓ /
  c1 減 ✓ を全部満たしつつ **weighted 255→285**。boost に `weightedScore <=` を追加し、
  「hard・weighted・件数のどれも悪化させずに c1 だけ減らす」横移動に限定した。反例を回帰テストで固定。
- **[殻 P0残] Worker の失敗パスだけが所有権を閉じていなかった**: `catch (Exception)` は通知して
  `Result.failure()` を返すだけで、マーカーも入力も残す。`Result.failure()` は WorkManager が再実行しない
  ので入力を残す意味も無く、次回起動が「中断されました・再開できます」と**失敗を中断として案内**していた。
  所有者なら `clearFiles` してから返す。
- **[殻 S3] 結果を「公開してから保存」していた＋非原子書き込み**: 順序を「耐久保存 → 公開」へ、書き込みを
  一時ファイル経由の置換へ。旧は ①公開直後に落ちるとメモリにしか無い結果が消える ②`writeText` の途中で
  落ちると壊れた JSON が残り、起動時の復元は「`resultTxt` が空でなければマーカーも入力も掃除してから読む」
  ため**結果も再開手段も両方失う**経路だった。
- **[殻 P1] 編集ガードが表示用の旗を見ていた**: 早期 return する14箇所が `ui.running`（表示の写し）を見て
  おり、初期化時の WorkManager 問い合わせが失敗すれば false のまま＝背景で走っているのにガードが全部開く。
  全て `optimizeInFlight()`（前景 or `OptimizationRepository.running`）へ寄せ、`ui.running` は表示専用と
  KDoc に明記した。
- **[殻 P2] CSV の並びに穴があると後半が黙って消えた**: `MUST連続,休,,A` は空セルで打ち切られ `["休"]` に
  なり、**A が消えたまま accepted に数えられて**いた（3.333.0 の「評価されない行を受理しない」の取り残し）。
  穴あきは書式の誤りとして拒否する（末尾が空なのは可変長として正常）。
- **[H10] 辞書式パックの前提を assert 化**: `soft < SCORE_HARD_UNIT` を `fullEval` で検査。実データの実測は
  soft 1516〜3346（桁 1e9 に対し約30万倍の余裕）＝理論上の地雷だが、重み変更(HF77)や制約の大量複製で
  膨らんだときに原因不明の挙動でなく明示的な失敗にする。
- **[教訓#28 の掃討] `FixSuggester` の手書き3キー比較5箇所を `betterReport` へ委譲**: 値は正しかったが写し。
  スカラー3変数で最良を持っていた2箇所は「最良の report を持つ」形へ。v6 全域で手書き3キー比較 **0件**。
- **根拠を示して不採用にしたもの**: ①**H2「複合手は原子拒否のはず」**＝これは提案側の設計案で、当方の
  3.334.0 の記述は最初から「`opBlockFill`/`opLns` は固定セルだけ飛ばす」＝実装と文書は一致している。
  原子拒否へ変えるのは近傍の変更＝A/B が要る ②**S2/S4「結果に指紋が無く再起動後に誤復元しうる」**＝
  `resultFile` は `StateParser.serialize(入力state, 結果schedule)` で**入力ごと**保存し、復元は
  `loadAsync(markResult=true)` が**state 丸ごと差し替える**＝別 state に他 run の盤面を当てる経路は無い
  ③**RunEnvelope / 固定ファイル名→run ディレクトリ**＝永続化レイアウトと復元の状態機械を作り替える変更で、
  実機でしか検証できない。マーカー＋所有権検査で実害は閉じている（3.328.0 と同じ判断）。
- 検証: ホストJVM **全423テスト green**（420 + 新規3）。実データは golden 2469/306/c1 104・
  user 33159/162/c1 54 が既知値と一致、real 49213 は既知帯。C++ 無変更＝native parity 影響なし。

## 探索の成果物を実行ごとの持ち物にする＋後段オペレータの比較が total へ落ちていなかった（3.335.0, 外部レビュー P1 2件）
ユーザー提示の6行表のうち **P0 3件は 3.333.0/3.334.0 で修正済み**（レビュー対象が古い）、
**P1「StateFingerprint が C3 行境界をハッシュしない」も 3.333.0 で修正済み**。残る P1 2件を照合して修正した。
- **[P1] `V6LateOperators` の `gate`/`gateW` が hard→weightedScore で止まっていた**: `betterReport` は
  hard→weightedScore→**total** の3キーなのに、この2つは第2キーまでを手書きで複製しており第3キーへ落ちない。
  weighted 同値・total 改善の候補（重み15 の c1 1件 ↔ 重み1 の c42 15件は weighted 同値で total が14違う）を
  捨てていた。3.287.0 は第2キーだけ寄せて第3キーを書き忘れ、3.309.0 は `gateW` に hard を足したときに同じ
  複製を作った＝**2回とも「委譲でなく複製」したのが原因**なので、両方 `betterReport` へ委譲した。
  `gate` の c1 ブースト分岐（`soft<=` ガード付き）は従来どおり残す。
  **正直な限界**: この2つは探索のラウンド境界（ChainSwap3/4 等）で走り、後処理研磨のベンチには現れない。
  実データ3件の最終盤面は golden 2469/306/c1 104・user 33159/162/c1 54 が既知値と一致・real は既知帯
  （49223）＝**この修正の効果は測れていない**。契約を揃えるための変更。
- **[P1] 探索の成果物が実行をまたいで共有されていた**: `lastAlternatives`/`lastFusionElites`/
  `lastInfeasibleFamilies`/`liveBest` は可変 static で、`optimize()` は入口で全部クリアする。呼び出し側は
  **返却後にこの static を読む**ので、実行が重なると（WorkManager の `REPLACE` で旧 Worker が協調キャンセルを
  待つ間・kill 後の再スケジュール）別の実行の値を掴み得た。**採用盤面は元から返り値で流れるので誤った勤務表には
  ならない**＝混ざるのは「他の案」「残存分析」「ライブ表示」。3.327.0 で入れた `ownsFiles()` はファイル書き込み
  だけを守っており、この static 群は素通しだった。
  **対応**: 実行ごとに `RunSlot`（他の案・エリート・HF63学習）を作り、**コルーチンのコンテキスト**で呼び出し木の
  隅々まで運ぶ（`runAlnsSingle`/`runRsi`/`runAdaptivePortfolio`/`runMultiWorker` はすべて suspend なので
  引数を1つも増やさずに届く）。`optimize()` は結果を `V6OptimizerResult` に載せて返し、`V6FinalPort` と
  `MagiViewModel`（`ActionResult.alternatives` 経由）は**返り値から読む**。static は「いちばん新しい実行の
  ライブ表示」用に残し、置き換えられた古い実行は `ownsStatics()` が偽になって書かない。
  副産物として **ExtraRefine の退避/復元（`restoreAlternatives`/`restoreFusionElites`）が不要になり撤去**
  （「入口で消えるから退避する」という回避策そのものが、この設計の脆さの証拠だった）。
  **正直な限界**: 実際の競合は Android 実機/エミュレータでしか再現できない。ここで確かめたのは
  「スロットが実行ごとに独立していること」「返り値がその実行の値を運ぶこと」までで、**競合そのものは
  測っていない**。
- 検証: ホストJVM **全420テスト green**（418 + 新規2）。C++ は無変更＝native parity 影響なし。

## SA/LAHC の近傍が希望固定セルを触っていた＝手の35%が空振り（3.334.0, 3.333.0 の残り1件を計測して採用）
3.333.0 で「非対称は実在するが、直すと探索の近傍が変わるので測ってから」と保留した項目。**測ったら
一貫して支持されたので入れた**（3.310.1 は逆に一貫せず否決＝同じ手順で結論が分かれた対の実例）。
- **非対称の実体**: 後処理研磨の全パスと C++ の修復オペレータは元から `wishLocked` を見ているのに、
  `SaOptimizer` の `opSingle`/`opSwapDays`/`opBlockFill`/`opLns` と C++ `runSaChunk`/`runLahcChunk` の
  同名オペだけが見ていなかった。**ALNS は元から見ていた**（`op3`/`op4`/`findTargetedFix` に
  `wishLocked` ガードあり）＝取り残しは SA/LAHC だけ。
- **前提の計測（挙動を変えずに数えた）**: 実データ3件で **手の 35〜36% が希望固定セルを希望から外す**
  （golden 304752/838240・real 292998/820980・user 288199/807620）。**そのうち受理されるのは
  0.05〜0.44%**（140/1296/560）。採点は元から正しい＝pref は Evaluator の hard に入るので、破ると差分が
  `SCORE_HARD_UNIT`(1e9) 単位になり Metropolis の `exp(-Δ/(200·temp))` が実質 0。**誤った勤務表は
  出ないが、3手に1手が捨てられる手に費やされていた**。
- **A/B（Kotlin SA 単体・8秒予算・3データ×5シード＝15ペア）**: weighted は **B（ガードあり）が 12/15 勝**
  （符号検定 p≈0.02）。中央値 golden 2560→2533 / real 51898→49538 / user 34227→33366。
  **反復数は +19〜41%**（891k→1058k / 925k→1304k / 883k→1095k）＝空振りの回収がそのまま出た。
  さらに **pref は A が15/15 で 1〜4 件残し、B は15/15 で 0**＝統計でなく構造的な差（近傍が触らないので
  入口で入った希望がそのまま残る）。
- **実装**: 近傍側で固定セルを避ける（`opSingle` は日を4回まで引き直し・`opSwapDays` はどちらかが固定なら
  見送り・`opBlockFill`/`opLns` は固定セルだけ飛ばす）。`applyCell` 側で握り潰すと `opSwapDays` が
  半分だけ入れ替わる別物の手になるため、オペレータ側に置いた。PhaseB(LAHC) は同じ `pickOperator` を
  使うので自動的に同じ。C++ も同型に直す（`wishLockedN` を `runSaChunk` の手前へ移動して SA/LAHC から使う）。
  **正直な限界**: A/B は Kotlin 経路で測った。C++ は同じ変換を当てただけで、効果そのものは測っていない
  （ただし既定は native 経路なので、Kotlin だけ直すと既定の無駄が残る＝直さない方が悪い）。
- 検証: ホストJVM **全418テスト green**（417 + 新規1＝6シードで固定セルが1つも動かず pref=0）。
  native parity **2,996,665手 mismatch=0**（bit-op ×2.09）。後処理研磨のベンチは SA を使わないので
  golden 2469/306/c1 104・user 33159/162/c1 54 が不変。

## 制約CSVの全置換ガード・完了後の実行中固着・指紋の行境界（3.333.0, 外部レビュー5件）
ユーザーが5行の欠陥表を提示。1件ずつ実コードへ照合し、**4件は実在**したので直した。1件（SA/LAHC）は
非対称の実在は確認したが、直すと探索の近傍が変わる＝このリポジトリの規律では計測してからでないと入れない。
- **[Critical] 制約CSVの全置換が、評価されない行を受理していた**: 取込は制約10族と個人レンジを**すべて
  置換**するのに、`連勤`/`回数下限`/`群回数`/`スキル群回数`/`群組合せ禁止`/`スキル群組合せ禁止` の6族は
  **種別が既知なだけで無条件に受理**していた。`連勤,,,` は `C1Row("","","")` として取込件数に数えられるが、
  `Problem` は `d1>0 && si>=0 && d2>0` で捨てる＝**一切評価されない行で既存の有効な制約を全部置き換えられる**
  （実質「制約なし」で最適化が走る）。3.329.0 で入れた中止条件は未知の氏名・記号しか見ておらず、
  構造的に空・不正な行を素通ししていた。**判定は `Problem` を単一ソースにする**（族ごとの条件をここへ
  複製すると必ずドリフトする）＝候補 state を組んで `unresolvedRows`（3.320.0 の6族）＋`c3UnknownShift`
  （3.309.0 の連続パターン）を数え、1件でもあれば `rejected` に加算＝呼出側が置換を中止する。
- **[High] 背景最適化が正常終了しても「実行中」が永久に残る**: 3.329.0 で
  `finally { if (ownsFiles()) setRunning(false) }` にしたが、**成功パスは所有権マーカー（runIdファイル）を
  自分で消してから finally へ入る**。`ownsFiles()` はファイルを読み直すので「自分は所有者でない」と判定され、
  `setRunning(false)` が飛ばされて `OptimizationRepository.running` が true のまま固着する。3.328.0 で
  `running` を全ガードの根拠にしたので、**完了画面なのにセル編集・一括シート・Undo/Redo・設定変更が
  恒久的にブロックされる**。`releasedByMe` フラグで「自分が正常に手放した」ことを覚え、
  `if (releasedByMe || ownsFiles())` で解放する。所有権を奪われた旧実行は従来どおり触らない。
- **[Medium] 状態指紋に行の境界が無かった**: 可変長の行を素通しで連結していたので、値の並びが同じで
  **構造だけ違う**入力が同じ指紋になる（`groupShift` が `[[1,1],[0]]` と `[[1],[1,0]]`／連続パターンが
  `[["A","B"]]` と `[["A"],["B"]]`）。指紋は診断の鮮度判定と背景結果の照合という**安全機構2つ**の土台なので、
  一致すれば古い診断・古い結果が新しい入力のものとして通る。区切り値 `ROW` を行ごとに混ぜる。
  **3.330.0 の29族テストはこの穴を通した**＝族ごとに1つ値を変えるだけで、行の形は一度も変えていなかった。
  `rowShapeChangesTheFingerprint` を追加（担当可否・適切回数・禁止の並び・必須の並びの4通り）。
- **[Low] 上限のコメントが実装と食い違っていた**: 実装は `MAX_OPTIMIZE_SEC = 300`（5分）なのにコメントは
  「10分(600s)」のまま（HF77: コメント≠実装）。実態へ。近くの2箇所の 600s 参照も文言を直した。
- **[報告のみ・未変更] SA/LAHC の近傍が希望固定を見ていない**: `SaOptimizer` の
  `opSingle`/`opSwapDays`/`opBlockFill`/`opLns` と C++ `runSaChunk` の近傍は `wishLocked` を一切見ない
  （後処理研磨の全パスと C++ の修復オペレータは全部見ている＝**非対称は実在**）。ただし**採点は正しい**
  ＝pref は Evaluator の hard に入るので、希望を破ると差分が `SCORE_HARD_UNIT`(1e9) 単位で増え
  Metropolis の `exp(-Δ/(200·temp))` は実質 0＝ほぼ必ず却下される。実害は**反復の空振り**であって誤った
  勤務表ではない（実データは 93希望/310セル＝約3割が固定。`opBlockFill` は day1=14 なら固定セルを避けられる
  確率が 0.7% 程度＝ほぼ全部空振り）。**ただし証明可能に死んでいる手ではない**（複数セルを同時に動かす手なら
  covU 8000 + c3n 7000 = 15000 > pref 9000 で正味改善になり得る）ので、ガードを足すのは探索の近傍の変更＝
  2.55/2.56/3.94/3.310.1 の規律どおり **A/B で測ってから**でないと入れない。今回は入れていない。
- 検証: ホストJVM **全417テスト green**（415 + 新規2）。実データは golden 2469/306/c1 104・
  user 33159/162/c1 54 が既知ベースラインと**バイト一致**、real は hard=6 で 49224〜49326（JointLNS の
  壁時計由来の既知のばらつき）。Worker 層はホストでコンパイル不可＝括弧均衡・シンボルのスコープ逆引きまで
  （最終判定は CI）。

## 適応ポートフォリオのログが2つの母集団を1行に混ぜていた（3.332.0, 実機A/Bログから）
ユーザーが3トグル（c3n事前フィルタ / 立て直し方 / 崩し範囲）を ON にした A/B を実機で取り、
同じ結果（HARD=1 total=200）に **178s→89s** で到達した。トグルの効果は 3.295.0/3.298.0 の実測どおり
（Block Swap の不採用が 必須増43 → 重み悪化39＝詰んだ候補に checker を呼ばなくなった）。
そのログを読んでいて**表示の欠陥を1件**見つけた。**ログのみ・盤面もスコアも不変**。
- **`圧縮elite=10 相異なるelite=10 距離=0..88セル` が矛盾に見えた**。実際は母集団が違い、
  `相異なるelite`＝アーカイブの圧縮エリート／`距離`＝**8ワーカーの最終解**だった。
  しかも `相異なるelite` は**恒真値**（`register` が `sameSchedule` で重複を弾き `snapshot` も
  `filterNot` で除くので圧縮エリートは常に相異なる＝実機ログでも2実行とも 10/10）。
  情報がない数字が、意味のある信号を打ち消していた——`距離=0..88` の最小0は
  「**2ワーカーが同一解に潰れた**」＝並列が無駄になっているという信号（run1 は 43..121 で潰れなし）。
  表記を `ワーカー解=8本(相異なるN本) 距離=a..bセル・同一解あり` へ変え、母集団を明示して潰れを表に出す。
  依拠した不変条件（圧縮エリートは常に相異なる）は `AdaptiveEliteArchiveTest` で固定した
  （恒真だと断言した以上、証拠を残す）。
- **誤検知だったもの（記録）**: C3nPolish の「候補日延べ4(パターン全域・当日含む)」はトグル OFF の
  run1 でも同じ表記で出ており一瞬ラベルバグを疑ったが、**このパス自身が常にパターン全域を走査する**
  （UI トグル `wideC3nBreakDays` は別経路 `breakableDaysFor`＝`findCovUChain` の隣接日調整に効く）ため
  表示は正しい。トグル名と作用範囲が読み手に紛らわしい、という別の論点は残る。
- 検証: ホストJVM **全415テスト green**（414 + 新規1）。実データ3件で最終盤面が既知値と一致。

## C1頭打ち診断が最後の巡だけを見ていた＋結合探索の検査順（3.331.0, 実機ログ 2026-12 から）
実機ログを精読して1件の表示バグと1件の無駄を見つけた。**採否・重み・スコアは完全に不変**。
- **[表示バグ] 巡ごとの観測を上書きしていた**: 後処理は C1研磨を最大4巡回すが、`c1Plateau = it` で
  **最後の巡が前の巡を上書き**していた。2巡目以降は1巡目が直したあとの盤面を見るので観測が少ない。
  実機ログでは C1Polish の「残存」が7箇所（うち4つが5日窓）なのに、C1Plateau の説明は**3箇所だけで
  全部14日窓**＝5日窓の理由が一切出ず、件数も 24/16/22 → 6/8/12 と実際より小さく出ていた。
  `mergedWith` を新設して**全巡を合算**する（同じ (職員,シフト,決まり) の件数と主因の族を足し、
  分類を合計で決め直す）。この数は「計測できた候補試行数」と名乗っているので、合計でなければ意味が合わない。
  分類規則は `causeOf` に切り出して `build` と共用（片方だけ直して分類がずれるのを防ぐ）。
  **実データでの効果は件数側で確認**（user_state モニカ15日窓 score 15→**75**・none 6→**29**＝最後の巡は
  全試行の1/5しか見ていなかった）。**箇所数はこの2データでは変わらない**（14/16 のまま）＝箇所が落ちる
  現象は実機ログ側でのみ観測。正直に両方記録する。
- **[無駄] 結合探索がフル checker を先に呼んでいた**: `CombinatorialRepair` は組合せごとに
  `isBetter(check(...)) && !exactPinRegression(...)` を評価していた。`&&` は両方を要求するので
  **順序を入れ替えても採否は完全に同一**なのに、ピンを崩す組合せにも毎回フル評価を払っていた。
  実データではプールの大半がピン破り（実機ログ AptPolish 69/71・FairPolish 20/20）。安いピン検査を
  先に置いて checker 呼び出しを省く。あわせて組合せごとの `work.copy2D()`（試行は必ず元へ戻すので
  毎回同じ内容を作り直していた・最大200回/パス）を外側ループへ巻き上げた。
  実測: 巡回研磨 user 13580→12676ms・golden 7934→7377ms。**総時間は変わらない**（共同LNS が残り予算を
  吸うので、浮いた時間は探索に回る）。最終盤面は3データとも完全一致。
- **ログを読んで確認したが問題でなかったもの**: SoftPolishVerify の `c3 65->67`（c1 3件と引き換え＝
  3.287.0 の weighted 優先どおりの正しい取引）／AdaptiveBlockSwap の不採用43件が c3n（既定OFF の
  c3n フィルタ＝3.298.0 で最終盤面同一を実測済み）／役割別worker秒の合計が 974s < 8×151s
  （3.308.1 に記録した「quantum<=0 と例外の break は秒に入らない」と整合）。
- 検証: ホストJVM **全414テスト green**（411 + 新規3＝巡の合算・分類の再判定・空側の扱い）。

## 新しい安全機構をテストできる場所へ移す（3.330.0, レビュー3回が挙げた「テスト不足」への回答）
3回のレビューが揃って「Worker競合・実行中編集・kill復元・CSV・休index・スキル移行を直接検証するテストが
無い」と指摘していた。**実機が要るものと、置き場所のせいでテストできていなかっただけのものを切り分け**、
後者を Android 非依存の層へ移してテストを付けた。挙動は不変（移動と委譲のみ）。
- **[入力の指紋] `MagiViewModel.stateKey` → `v6.StateFingerprint.of`**: 3.328.0 で全入力へ広げた指紋は、
  手書きで20以上のフィールドを畳む純関数なのに ViewModel（Android 依存）にあってテストできなかった。
  **1つ書き忘れると安全機構2つ（診断の鮮度・背景結果の照合）が黙って効かなくなる**＝まさに M-02 の再発。
  v6 へ移し `StateFingerprintTest` で **入力の族29種を1つずつ変えて指紋が変わること**と
  **族どうしで衝突しないこと**、**盤面だけの違いでは変わらないこと**を固定。
  さらに `MagiState` の全26フィールドと機械照合し、読んでいない3つ（`schedule`＝別に盤面ハッシュで見る／
  `shiftColors`＝表示色／`extras`＝未モデル化）が意図的な除外であることを確認して KDoc に明記した。
- **[スキル移行] `MagiViewModel.removeSkillGroup` の規則 → `Ws1Ops.removeSkillGroup`**: 担当グループの
  `removeGroup` は元から Ws1Ops にあるのに、スキル群だけ ViewModel に手書きされていた（対称性の欠落）。
  移して回帰テスト＝前の群は不変・削除された群の所属者は **-1**・後ろの群は1つ詰まる・元から未所属は不変・
  範囲外は no-op ＋ **「最後の1群を消してから群を足しても誰も所属しない」**（3.328.0 で直した取り違えの固定）。
- **[作らなかったもの] 所有権判定のテスト**: `ownsFiles` は `mine == 0L || active == mine` の2条件で、
  テストしても同語反復にしかならない。**本当のリスクは「所有を確認した直後に置き換わる」時間差**で、
  それは単体テストでは捕まらない（実機/エミュレータのみ）。テストのためのテストは作らない。
- 検証: ホストJVM **全411テスト green**（406 + 新規5）。実データ3件で診断件数 3/7/7 不変・
  POST は golden/user が既知値と一致・real は既知帯内。

## 入力の意味論を一本化＝休index・CSV全置換・所有権の残り（3.329.0, 外部レビュー第3回）
基準 `167bf9b`(3.327.0) の再レビュー10項目。**H-04/M-02/L-01 と M-01 の前半は 3.328.0 で対処済み**
（レビューの基準が古い）ので分離し、残りを実コードで照合して修正した。
- **[H-01] 新しい職員の行・伸ばした日を index 0 で埋めていた**: `Ws1Ops.addStaff` / `resizeDays` /
  `StaffCsvIO.parseUpsert` が休を **index 0 と決め打ち**（コメント自身が「休/idx0」と両者を同一視）。
  同じファイルの `addGroup` は `restShiftIndex` で正しく解決しており、盤面を埋める3箇所だけ取り残されていた。
  **休が先頭でないデータでは、新しい職員の全日と追加した日が丸ごと勤務シフトになる**。3箇所とも
  `restShiftIndex(state)` へ。CSV 追加の未知スキル群も `0`（先頭の群）→ **`-1`（未所属）**へ（3.70.0 の規約）。
- **[H-02] コンポーネント別CSVが読めない行を捨てたまま全置換していた**: 希望・制約の取込は
  `state.copy(wishes = m)` のように**既存を丸ごと差し替える**のに、未知の氏名・記号・日付・種別の行を
  `continue` で黙って捨て、1行でも有効なら実行していた。**80行中79行が誤記のCSVを読ませると79件の希望が消える**。
  `ComponentImport(state, accepted, rejected, sample)` を新設し、**中身のある行を1つでも解釈できなければ
  呼び出し側が置換を中止**（どこが悪いかの例も示す）。完全な空行は書式上のものとして従来どおり無視。
- **[H-03 の残り] 旧 Worker が新 Worker の実行中を消せた**: 3.327.0 は書き込み・削除・結果公開を
  `ownsFiles()` で塞いだが、**`finally` の `setRunning(false)` と進捗の publish が素通り**だった。
  置き換えで打ち切られた旧実行が実行中表示を落とし、旧実行の進捗を新実行のものとして見せうる。両方に所有権検査を追加。
  **TOCTOU（所有確認の直後に置き換えが起きる窓）は残る**＝数μsで、被害は最大1回の publish。
  run ごとのディレクトリで根治する案は 3.328.0 で示したとおり永続化レイアウトの作り替えになるため採らない。
- **[M-03] 日付ヘッダが無いCSVで日数を最大列数から推定していた**: 合計・注記などの末尾列まで日付として
  取り込み、中身が空の日ができていた。**期間はデータの根幹なので推測せず取込を断る**（利用者が日付行を足せば通る）。
  月も `rows[0].drop(1)` のセルだけを見ており「令和8年 7月」が1セルの形式では**必ず1月**だったので、
  タイトル文字列から `(\d{1,2})\s*月` で読む経路を先に置いた。
- **[M-04 は不採用]** 重複した氏名・記号での実行ブロックは提案されたが採らない。先勝ち解決は
  `Problem.indexOfFirst` と一貫した確立済みの仕様で、検査8（3.106.0）が既に警告している。実行を止めると
  「2件目が実質参照不能」という**警告で足りる状況**のために、正当なデータまで止めうる。
- 検証: ホストJVM **全406テスト green**（403 + 新規3＝休index解決・希望CSVの読めない行の計上・
  未知種別と解決不能な個人レンジ）。既存の CSV テスト4箇所を新しい戻り型へ更新。実データ3件で
  診断件数 3/7/7 が不変・POST は golden/user が既知値と一致・real は既知帯内。

## 実行中の編集で全ガードが外れる欠陥＝running の二重用途を解消（3.328.0, 外部レビュー再確認）
`167bf9b`(3.327.0) への再レビュー11項目を全件コード照合。✅3件は同意、残りは実在を確認して修正した。
**提案されていた優先順位には1点反論した**: #1 の RunContext ＋ run ごとのディレクトリは、いま起きている
事故の主因ではない（主因は下記の `running` 二重用途で、Worker と無関係に前景実行でも起きる）。
永続化レイアウトの作り替えは復元経路ごと危険で、同じ効果は**入力の指紋**で得られる（提案 #2 の後半そのもの）。
- **[最重要・新規] `running` が「短い違反チェック」と「長い最適化」を1つの旗で兼ねていた**:
  `refreshCheck()` は完了時に無条件で `running = false` を立て、`checkJob?.cancel()` は checkJob しか
  止めない。**最適化の最中に設定を編集すると、その編集が起こす検査の完了で実行中フラグが落ち、以降
  `!ui.running` を見ている全ガード（セル編集=3.161.0・一括シート=3.127.0・回数の緩和=3.326.0）が
  素通りになる**。`@Volatile optimizeActive`（長い最適化4経路の launch 直前で true・各 finally で false）と
  `optimizeInFlight()`（前景 or `OptimizationRepository.running`）を新設し、検査の完了・停止・失敗の
  **9箇所**を `running = optimizeInFlight()` へ。
- **[実行中編集を入口で止める]** 個々の編集画面へ `!ui.running` を配る方式は 3.161.0/3.127.0 で
  何度も取りこぼしている。編集は必ず `applyStructure`(2種)／`applyStructureWithMessage`／
  `mutateConstraints` を通るので**その4入口だけ**を `structuralEditBlocked()` で塞ぐ（呼出元が先に
  ログを出していることがあるため、取り消したことも W ログに残す）。`undo`/`redo` のガードも
  `job?.isActive` だけでは背景実行を見逃すので `optimizeInFlight()` を OR で追加。
- **[結果を別の入力へ当てない] `constraintKey` → `stateKey` へ全入力を対象に拡張**: 旧は `staffRange` と
  `cons1` だけで、希望・担当可否・適切回数・連続パターン・群/スキル群・日別必要人数を変えても盤面が
  同じなら古い却下理由が残った。同じ指紋を `applyBgResult` の照合にも使い、開始時と入力が変わっていたら
  **結果を破棄**する（旧: 現在の state へ無条件に適用）。プロセス再起動後のファイル復元は結果ファイルが
  state ごと持つ＝自己整合なので従来どおり通す。
- **[fail-open の残り] 検査 2h を日別必要人数・適切回数へ拡張**: `needAt` は日別例外が非数値のとき
  **シフト既定値へ黙って読み替える**（0 になるより性質が悪い＝その日だけ意図と違う人数で計算され、
  画面には何も出ない）。`groupShiftApt` も同様。**実装中の発見**: 全角数字「２」は `toIntOrNull` が
  2 として解釈する＝非数値ではない（テストの前提を誤り、実行して判明したので是正）。
- **[skillIdx] 群削除時の再割当を 0 → -1（未所属）へ**: 旧は削除群の所属者を 0 へ寄せるので
  ①無関係な先頭群の制約が黙って掛かる ②最後の1群を消すと全員 0 になり、あとで群を1つ足すと
  全員がそこに所属した扱いになる。3.70.0 が「(なし)=-1」を正規の値として用意済み。
- **[Low2件]** 0回に固定された行の「下限を1下げる」は 0 でクランプされ無操作だった→下げられるときだけ出す。
  ピン集計の注記「同じシフトに複数の期間…まとめて数えています」は 3.326.0 で決まりごとに分けたのに
  取り残されていた文言→実態へ。
- **[反論して不採用] Block Swap の DFS 内 deadline**: 締切はブロック入口で確認しており、実測は
  18〜70ms・DFS 88万件/77ms＝停止遅延は ms 級。最内周に `System.currentTimeMillis()` を足す価値がない。
- 検証: ホストJVM **全403テスト green**（400 + 新規3＝日別必要人数/適切回数の非数値・空欄は誤検知しない）。
  実データ3件で新検査の**誤検知ゼロ**（件数 3/7/7 で不変）、POST は golden/user が既知値と一致・real は
  既知のばらつき帯内。UI/ViewModel 層はホストでコンパイル不可＝括弧均衡・**シンボルのスコープ逆引き**・
  呼び出し側シグネチャの静的確認まで（最終判定は CI）。
- **正直な限界**: 実行中編集・所有権・kill 復元の**実際の競合**はエミュレータ/実機でしか確かめられない。
  ここはコンパイルが通ったことしか保証していない。

## 実行の所有権と入力の検証＝外部レビュー High 5件（3.327.0, ユーザー指示「修正する」）
提示された `5d615d9` のレビュー（High 5・Medium 3・Low 1）を1件ずつコード照合し、**High は5件とも実在**
（うち2件は 3.322.0〜3.326.0 で自分が作った欠陥）。Medium は測ってから採否を決めた。
- **[High1/自分のバグ] 採用しなかった盤面の診断を返していた**: `handleOptimize` は `post` の診断を
  無条件に `ActionResult` へ載せるが、`finalSched` はそのあと ExtraRefine で差し替わる（`refSched`）か
  最終番兵で入力へ戻る（`normInput`）ことがある。3.324.0 で「採用しなかった盤面の診断は保存しない」を
  VM 側に入れたのに、**エンジン側の出口を塞いでいなかった**。`postForResult = post.takeIf {
  finalSched.contentDeepEquals(it.schedule) }` にして、盤面が一致するときだけ診断を通す。
- **[High2/自分のバグ] 鮮度判定が盤面の指紋しか見ていなかった**: 3.324.0 の `diagFresh` は盤面ハッシュで
  自己失効するが、3.326.0 で足した `relaxStaffRangePin`（回数固定をその場で1段緩める導線）は
  **盤面を変えずに制約を変える**＝ハッシュが一致したまま古い観測が出続ける。`constraintKey(st)`
  （`staffRange` と `cons1` の指紋）を新設して AND 条件にした。あわせて `pinTargets` に `lo == hi` の
  ガードを追加（緩めたあとの行が「厳密ピン」として残り続けていた）。
- **[High3] バックグラウンド実行に識別子が無い**: ファイル名は固定（`magi_bg_input/result/best.json`）で
  `ExistingWorkPolicy.REPLACE` を使うのに、**どの実行が書いたファイルかを区別する術が無かった**。
  実害は2つ: ①置き換えで打ち切られた旧実行が `catch(CancellationException)` の `clearFiles` で
  **新実行の入力ファイルを消す**（復元不能の窓ができる。3.289.0 で入れた片付け自体が加害側になっていた）
  ②旧実行が完了間際なら別データの結果を `resultFile` へ書き、次回起動でそれが現在のデータとして復元される。
  `runId`（`inputData` に載せる＝WorkManager が永続化・kill 後の再実行でも同一）と `magi_bg_run.txt`
  （いま所有権を持つ実行）を新設し、`ownsFiles()` が偽なら **書き込みも削除も公開も一切しない**
  （doWork 入口で早期 return・スナップショット・完了保存・キャンセル時の片付けの4箇所）。
  `runId` を持たない旧経路は従来どおり所有者として扱う（非破壊）。
- **[High4] 数値でない設定が黙って「制限なし」になる**: 2f（3.309.0）が拾うのは `Problem` が行ごと捨てた
  ものだけ。`staffRange` の lo/hi・シフトの必要人数・`cons41(s)` の l/u は**非数値でも行が生き残り、
  空欄と同じ扱い**（未設定センチネルのまま）になる＝意図より弱い条件で「成功」する。検査 **2h** を新設。
  **空欄＝未設定は正しい仕様**なので対象にせず、「空でないのに数値でない」ものだけを出す。
- **[High5] 範囲外の skillIdx**: 範囲外だと `ssk[i]==groupIdx` が常に偽＝その職員がスキル群の制約から
  静かに外れる。検査 **2i** を新設。**自動で書き換えない**（既定 0 の旧データを一括で先頭の群へ入れると
  意味が変わる）＝知らせるだけ。native 側は 3.311.0 で巨大確保を防いでいるが意味論は未検証だった穴。
- **[Medium/測って撤回] Block Swap の評価枠**: `maxEvaluations` が pass ごとにリセットされ、KDoc の
  「呼び出し予算」という書き方と食い違うのは事実。**ただし「300秒を超える」という前提は成立しない**
  （実測 18〜70ms・DFS は 88万件/77ms）。per-call 予算へ直す版を作って実データで測ったところ、
  **pass 境界での候補再生成が減って real の採用手が 2→1 に落ちた**ので撤回し、**文書側を実装に合わせた**。
  `maxCycleVisits` も同様に (ブロック長, 開始日) ごとの分岐上限であって全体予算ではない
  （共通予算にすると後ろのブロックが一切探索されなくなる）＝そのままにして意味を明記。
- **[Medium/文言] 断定を観測の範囲へ戻した**: 「候補なし」→「この直し方では入れ替え相手が見つかりません
  でした（別の直し方までは確かめていません）」。回数固定の計測範囲を「9パス」→**「後処理研磨のうち
  `V6HotfixPasses` の18パス。`C1JointLns`/`PersonalBalanceJointLns`/`EliteIntegration`/`C1TemporalFlow`/
  `CombinatorialRepair` の10箇所と探索本体(SA/ALNS/LAHC)は計測外」**（3.326.0 で18パスへ広げたのに
  文書が 3.325.0 のままだった）。
- **[実装中に自分で直した誤り]** 2h のコメントに「`toIntOrNull() ?: 0` で 0 になる」と書いたが、実際は
  `?.let{}` を素通りして**未設定センチネルのまま**（`Int.MIN_VALUE`/`MAX_VALUE`）。テストを実行して
  発覚し、コメントと画面文言（「0人（制限なし）」→「未設定（要件なし）」）を実装に合わせた。
- 検証: ホストJVM **全400テスト green**（395 + 新規5＝個人の回数/必要人数/群レンジの非数値・空欄は
  誤検知しない・範囲外/範囲内/未所属のスキル群）。実データ3件で新検査の**誤検知ゼロ**（診断件数は
  golden 3・real 7・user 7 で 3.316.0 の記録と一致）。最終盤面は golden 2469/306/c1 104・
  user 33159/162/c1 54 が既知ベースラインと一致、real 49213〜49223 は既知帯の内側。
  UI/Worker 層はホストでコンパイル不可＝括弧均衡・シンボルのスコープ逆引き・呼び出し側シグネチャの
  静的確認まで（最終判定は CI）。

## 残作業3件を対応＝規則単位の診断・全パス計上・回数固定の緩和導線（3.326.0, ユーザー指示「1,2,3を賢く考え対応する」）
残作業として挙げていた3件。**探索の採否・重み・スコアは不変**（診断の粒度・計上範囲・設定変更の導線のみ）。
- **[#2 規則単位の診断]** `blockStats`/`culpritStats` のキーを `(職員, シフト)` → **`(職員, シフト, 規則index)`** へ。
  旧は同じシフトに複数の決まり（実データの「休 5日で1回以上」と「休 15日で4回以上」）があると別の決まりで
  却下された理由が混ざって並んだ。`stillDeficient` も規則単位で判定するので、解消済みの決まりの理由が
  残った別の決まりに付かない。`C1PlateauEntry.ruleLabel`（「5日で1回以上」）を画面へ出す。
  **窓開始日はキーに入れない**＝1日は複数の不足窓に属しうるので代表窓を選べない（選べば恣意的）。
  この限界は ruleLabel を出して読み手が区別できる形で残す。
- **[#3 全パス計上＋対象別]** `exactPinRegression` を呼ぶ**18関数すべて**へ計上を配線（旧: 9パス）。
  あわせて `PinBlockAttribution`（新設）で**どのピン(職員,シフト)が何回止めたか**を持つ。
  判定は `exactPinRegression`（早期returnの高速版）のまま＝ホットパスのコストは不変で、真だったときだけ
  `exactPinOffenders` で対象を集める。
- **[重要な発見] 意味論を厳密にしたら数が激減した＝前の数は過大だった**: 3.323.0 の `pinBroken` は
  `isBetter` が偽でも `pinBad` なら計上していた（5分類でピン破りを最優先にしたため）。
  今回は**「isBetter が採用を認めた手をピンだけが止めた」場合に限定**した結果、実データで
  **543/1219/1268 → 23/58/0** になった。新しい数のほうが「緩めれば通ったはず」の主張に正しく対応する。
  旧数はその主張ができない候補を大量に含んでいた（3.323.0 の数字を撤回する）。
- **[#1 緩和導線]** `PinFixedImpactCard` に対象別の一覧（「桒澤美幸 休：10回に固定（23回の試行を止めました）」）と
  **その場で1段だけ緩めるボタン**（下限を1下げる／上限を1上げる）。`relaxStaffRangePin` は
  `setStaffRange` 経由＝`applyStructure` に乗るので**「元に戻す」で戻せる**。
  受理条件は変えていない＝**データ変更はユーザーのタップのみ**（HF77適合。3.297.0 の `relaxForbiddenRule` と同じ形）。
  幅は決め打ちせず下限側・上限側を別々に選ばせる（±1 と ±3 の優劣がデータで逆転した実測を反映）。
- **[正直な限界]** user_state は `attempts=0` だが、3.323.0 の反実仮想では緩和で weighted 33159→32904 と
  改善した。矛盾ではない＝緩和は下限割れ(low, 重み90)の罰も外すので、「ピン以外の理由で」却下されていた
  候補が通るようになる経路が別にある。**0 件でも緩和が無意味とは限らない**旨を UiState の KDoc に明記。
- 検証: ホストJVM **全386テスト green**（テストを規則単位キーへ更新）。実データで最終盤面が 3.325.0 と一致
  （golden 2469/104・user 33159/54 はバイト一致、real 49223/58 は既知帯）。UI 層はシンボルのスコープ逆引きで確認。

## 診断の紐付けと粒度を仕上げる＝原因未確定の明示・横断集計の分離・改名（3.325.0, ユーザー指示6項目）
6項目の指示のうち **①盤面への紐付け（採用された同一勤務表にだけ・手編集/Undo/取込/不採用で失効）**と
**②pushReport より先に保存**、**⑥断定の削除**は 3.324.0/3.324.1 で実装済みと確認。残る3項目を実装した。
**読み取り専用・スコアリング不変**（採否・重み・探索は不変）。
- **[③ 原因未確定の明示]** c1 が残っているのに却下の観測が1件も無い状態（研磨が起点を取れなかった／
  後続パスが別の窓を直して観測分だけ消えた）で、旧実装は `takeIf { hasEntries }` で診断を null にし
  **カードごと消えていた**＝「残っているのに何も説明されない」。`C1PlateauDiagnosis.causeUnknown`
  （`remainingC1 > 0 && entries.isEmpty()`）を新設し、`runPostOptimization` は c1>0 なら観測ゼロでも
  診断を返す（`C1Polish` 自体が走らなかった場合も空の診断を作る）。UI は専用の見出し
  「窓の要件が残っています（原因未確定）」で**理由を語らず**「もう一度つくると記録が取れる場合があります」
  だけ示す。ログも同旨の1行に切り替える。**観測していないことを語らない**のがこの分岐の目的。
- **[④ 改名] `pinBlocked` → `observedPinBlockedAttempts`**（全層一括）。KDoc/UI で
  「**計測済みの候補試行数**であり、全手数でも改善予測でもない」を明示（9パスのみ計測・最大4巡を
  重複排除せず加算）。局所変数も `observedPinAttemptsTotal`/`...C1`/`...Range`/`lastObservedPinAttempts` へ。
- **[⑤ 横断集計の分離] `PinFixedImpactCard` を新設**し C1PlateauCard から切り出した。理由2つ:
  ①この観測は **c1 固有でない**（実データでは適切回数・公平化・連続パターンの研磨が大半）ため c1 の診断に
  従属させると **c1=0 のとき出せなくなる** ②c1 の内訳（職員×シフト）と横断集計（全パスの試行回数）は
  粒度も母集団も違い、同じカードに混ぜるとどちらの数字か読めない。ホームの C1PlateauCard 直後に配置。
- **[自分のミス] テストが新しい意味論と矛盾していた**: `alreadyResolvedTargetsAreNeverListed` は
  `remainingC1=9` で「全対象が解消済み」を表現しており、`causeUnknown` の導入で**そのフィクスチャ自身が
  原因未確定に該当**して失敗した。対象が全部解消されたなら c1 も 0 になるのが実態なので `remainingC1=0` へ
  是正し、`remainingC1>0`＋観測ゼロは別テストで固定した。
- 検証: ホストJVM **全386テスト green**（385 + 新規1）。UI 層はホストでコンパイル不可のため、3.324.1 の
  教訓どおり**導入シンボルの宣言と使用が同じ関数に属するか**を行番号から逆引きして確認
  （`diagFresh`→makeUi のみ・`observedPinBlockedAttempts`/`attempts`→PinFixedImpactCard のみ）。
  実データで golden 2469/104・user 33159/54 はバイト一致、real 49223/58 は既知帯（49213〜49232）の内側。

## 診断表示の整合性を修正＝外部レビュー5件を全件確認して直す（3.324.0）
提示された 63d1172 のレビュー（High 3・Medium 2）を1件ずつコード照合し、**5件とも実在を確認**して修正した。
3件は 3.322.0/3.323.0 で私が作った欠陥。**探索の採否・重み・スコアは完全に不変**（診断の保存タイミング・
有効範囲・件数の意味づけ・文言のみ）。
- **[High/自分のバグ] 診断を UI へ反映する順序が逆だった**: `runV6FullOptimize` は `pushReport`
  （`makeUi` を呼ぶ唯一の経路）の**あと**に `lastC1Plateau`/`lastPinBlocked` を代入していた。よって
  その回の画面には診断が入らず、次の再チェックでようやく出る。以後の `logOp` は操作ログしか更新しない。
  → `setPolishDiagnostics(...)` を新設し **pushReport より前**に呼ぶ。
- **[High/自分のバグ] 採用しなかった盤面の診断を表示し得た**: 入力勤務表を維持する `worseThanInput` 分岐でも
  `res.post` の診断を無条件に保存しており、「捨てた盤面で直せなかった理由」を「いま表示中の勤務表の理由」
  として見せうる。かつ**無効化する代入がどこにも無く**、手編集・元に戻す・CSV取込・初期解生成のあとも
  c1 が残っていれば古い観測が出続けた。→ 非採用分岐では保存しない。加えて
  **盤面の指紋（`boardKey`＝内容ハッシュ）で自己無効化**する（`makeUi` の `diagFresh` が毎回突き合わせる）。
  変更サイトごとにフックを足す方式は必ずどこかを漏らすため、内容一致での自動失効を選んだ。
- **[High] `pinBlocked` は全件でも一意な手数でもない**: `exactPinRegression` を呼ぶ研磨は18関数あるのに、
  集計しているのは9パスのみ（CyclicSwap・C1 index駆動・広域ビーム・ブロック交換・厳密日割当・交互最適化・
  曜日長方形・C3ブロック交換は未計上）。さらに最大4巡を重複排除せず加算する。
  → KDoc/UI を**「少なくとも N 回、回数固定だけが却下の理由だった」＝計測できた試行回数の下限**へ言い換え、
  未計上パスも明記。「0 なら緩めても何も変わらない」という前版の断定も撤回（未計測分があるため証明にならない）。
  **併せてレビューが名指しした C1 の穴を1件閉じた**＝手A（同日交換）のピン却下は黙って巻き戻すだけで
  数えていなかったので記録する（実データで real 1215→1219・golden/user は不変。**最終盤面は3件とも不変**
  ＝診断だけの変更であることを確認）。
- **[Medium] C1 診断は職員×シフト集計**: キーが `(staff, shift)` で規則・窓開始日を持たないため、別の窓で
  却下された理由が残った別の窓の理由として並びうる。キー拡張は C1Polish の `recordBlock` 配線を広く触るため
  今回は採らず、**UI・KDoc に集計粒度を明示**（レビューが提示した最小案）。
- **[Medium] 文言が断定的だった**: 「1回ぶん緩める」は**±1 と ±3 の優劣がデータで逆転した実測**（3.323.0）と
  衝突し、幅の決め打ちは HF77 にも触れる。「回数固定が壁」「すべて却下」も観測以上の主張。
  → 幅を示さず「対象の職員とシフト、緩める幅を決めて変更前後を見比べてください」へ。チップは
  「回数固定で却下」、`recommendedAction` は「試した直し方の多くが…却下されています」へ。
- **[レビューの指摘どおり否定されないもの]** 実際に緩めた A/B の weighted 改善値（3.323.0 の
  golden −34%・real −18%・user −0.8%）自体は表示不具合とは独立＝そのまま有効。
- 検証: ホストJVM **全385テスト green**（件数不変＝文言変更後も既存アサーションを満たす）。実データ3件で
  最終盤面が 3.323.0 と一致（golden 2469/104・real 49213/59・user 33159/54）。
- **(3.324.1, CI が捕まえた自分のミス) `diagFresh` を別の関数へ挿入していた**: `val mappedDiag = ...` を
  アンカーに文字列置換したが、この行は `analyzeParallel` と `makeUi` の**2箇所にある**ため最初の一致
  （= `analyzeParallel`）へ入り、`makeUi` からは見えず `Unresolved reference` でビルド失敗した。
  UI 層はホストでコンパイルできないので**括弧均衡0では変数スコープの誤りを捕捉できない**（実際 0 だった）。
  対策として、以後 UI 層を編集したら「導入した各シンボルの宣言行と使用行が同じ関数に属するか」を
  行番号から逆引きして確認する（`enclosing_fun` 相当の静的チェック）。同名行が複数あるファイルでは
  置換アンカーに関数境界を必ず含める。

## 専用証明探索は実データで1件も証明できないと実測→代わりに緩和の根拠を出す（3.323.0, 優先順③の再設計）
③「C-1専用証明探索」（`C1CandidateSearch` + `C1FocusProof`＝FEASIBLE / INFEASIBLE_PROVEN / UNKNOWN_BUDGET）は
**作る前に前提を測って否決**した。**read-only の計測＋観測値の配線のみ＝スコアリング不変**。
- **[測定] 厳密窓探索が「証明」に到達した窓は 3データセット・全217窓で 0件**:
  golden 0/104・real 0/59・user 0/54（`exhaustive=true` がゼロ／patch もゼロ／user は 0ms＝即 bail）。
  原因は 3.314.0 で測ったとおり関与職員の基礎集合が平均8.9〜9.8人で上限6を先に超えること。上限10への引き上げは
  3.314.0 で実測否決済み（2.4〜2.8倍遅く品質は同値か悪化）。**このまま作れば常に UNKNOWN_BUDGET を返す＝
  何も言わない診断**になるので作らない（3.290.0 の「候補4件の不活性パス」と同じ轍）。
- **[測定/④の前提] 休の回数固定を緩めた反実仮想**（データは書き換えず、効くかどうかだけを測る。9名が対象・各3回で
  ほぼ決定的）。**weighted は3データセットとも改善**:
  golden 2469→±1で2017→±3で**1632**（−34%）／real 49213→±1で**40132**（−18%・HARD 6→5）→±3で40195／
  user 33159→±1で**32904**→±3で32938。`low` は3件とも 0 になる（3.322.0 の「C1の主因は low」と整合＝
  緩和がその壁を実際に外している）。**ただし c1 単体は単調でない**（golden は ±1 で 104→106 と増える）。これは
  目的関数どおりの取引で、c1 2件(30)を手放して low 5件(450)を消している＝**c1 の増減だけを見て判断してはいけない**。
- **[実装] 緩和の根拠を観測値として出す**: `RejectCulpritStats.pinBroken`（3.321.0）は
  **`isBetter` が採用を認めた手をピンのガードだけが却下した件数**＝「固定を緩めれば通ったはずの手」の実測値。
  これを `CyclicSwapResult.pinBlocked`（既定0）で9パス（C1/C1Exact/C3mn/C3n/Range/Apt/Fair/C3Run/C3Pattern）から
  集約し `V6PostOptimizationResult.pinBlocked` → ViewModel → `UiState.pinBlocked` → `C1PlateauCard` の
  「回数の固定について」節へ。実測値は golden 543件・real 1215件・user 1268件。**0 なら緩めても何も変わらない**
  という含意まで込みで正直に出せる（推測でなく観測）。RangePolish の生文字列 `"ピン破り"` も
  `C1PlateauDiagnosis.REASON_PIN` 定数へ統一（対応のドリフト防止）。
- **④（休ピンの条件付き緩和）は実装しない**: 受理条件を変えるのは目的関数の変更で HF77 該当＝明示の数値指示が要る。
  本版で出したのは**判断材料**（何件が固定だけで止まったか）までで、緩めるかどうかは業務判断に委ねる。
- 検証: ホストJVM **全385テスト green**（384 + 新規1＝pinBroken がスコア却下と混ざらないこと）。

## C1が直せなかった理由を構造化してUIへ＝3.321.0の一般化を実データで訂正（3.322.0, 優先順②「C-1診断表示」）
①（3.321.0）で数え方を直したので、②＝その根拠を**文字列でなく構造化データ**でUIまで運ぶ。
**読み取り専用・スコアリング不変**（採否・重み・探索は完全に不変。却下の記録を運んで見せるだけ）。
- **新設 `C1PlateauDiagnosis.kt`**: `C1PlateauCause`（PIN_CONSTRAINED / SCORE_TRADEOFF / NO_CANDIDATE）＋
  `C1PlateauEvidence`（OBSERVED / UNKNOWN）＋職員×シフトごとの内訳（ピン破り/スコア却下/候補なしの件数・
  スコア却下の主因族）。`recommendedAction(labelOf)` は文言をここ1か所に置き、**族名の日本語化だけ呼出側から
  受ける**（対応表 `breakdownLabels` は UI 層にあり、エンジンへ複製すると必ずドリフトする）。
- **なぜ静的証明にしなかったか（設計判断の記録）**: 「休の回数が固定(lo==hi)だから窓不足を直せない」は
  **証明できない**。窓の不足は「窓の外の同じシフトを窓の中へ移す」（手R1/R2/R3＝回数保存の再配置、3.200.0）
  でも解消しうるため、回数を動かせないことは即「直せない」を意味しない。よって根拠は**実際に候補を作って
  却下した観測だけ**にし、**「構造的に不能」とは言わない**。`C1RepairAnalysis.provenWalls`（厳密証明のA4診断）は
  別物として一切変更しない。
- **配線**: C1Polish が既に持っていた per-(職員,シフト) の `blockStats`/`culpritStats` から構築 →
  `CyclicSwapResult.plateau`（既定 null＝他パス非破壊）→ `runPostOptimization` が巡ごとに上書きし
  **最終盤面で `refreshedAgainst` 再フィルタ**（後続パスが直した箇所を「直せなかった」と見せない）→
  `V6PostOptimizationResult.c1Plateau` → ViewModel が保持（盤面から再計算できないため）→ `UiState.c1Plateau`
  （いま c1 が残っているときだけ見せる）→ **`C1PlateauCard`**（ホーム・ForbiddenDiag の直後）＋操作ログ。
  理由文字列は `C1PlateauDiagnosis.REASON_*` 定数に集約し、C1Polish 側の6箇所をそれで書き換え（対応のドリフト防止）。
- **[実装中に自分で踏んだ分類の誤り]** 初版は理由を件数の多数決で決めており、実データの
  「アリフ Dﾃ = スコア却下8・候補なし10」を **「入れ替えられる相手が見つかりません」**と案内した。実際は相手が居て
  **禁止連続(c3n)** で落ちていた。「候補なし」は強い主張なので、**候補が1件でも作れて却下されているなら名乗らない**
  規則へ是正（ピン破りとスコア却下の比較も、同数ならスコア側＝強い断定を避ける向きに倒す）。回帰テストで固定。
- **[実データが 3.321.0 の一般化を訂正した]** 3.321.0 の「ピン破りが不採用の73〜100%」は
  **Apt/Fair/C3Run/C3Pattern/C3mn の各パスの話**で、**C1Polish 自身のピン破りは 0〜1件**だった。
  C1 の不採用は golden/real/user の3件とも **主因 `low`（下限割れ・重み90）**、Dﾃ の窓だけ **`c3n`**。
  同じ設定を指してはいる（休は 9/10名が lo==hi=10＝`low` はそのピンの下側）が、**どちらのガードが先に発火するかで
  ラベルが変わる**（損失が利得を上回ると `isBetter` が先に落ちるので「ピン破り」でなく「不採用/low」になる）。
  よって C1 に対して引くべきレバーは「休の回数固定（とくに下限）を緩める」で 3.321.0 と同じ結論だが、
  **画面に出る族名は `low`（下限割れ）**である、という差を正確に記録しておく。
- **既知の限界**: 主因は族名までで、**どのシフトの `low` かは特定していない**（`worstWorsenedFamily` は族単位）。
  シフトまで出すには `countViolations` の差分が要る＝別途。
- 検証: ホストJVM **全384テスト green**（376 + 新規8）。実データ3件で診断が実際に構築され、
  最終盤面の値は 3.321.0 と一致（golden 2469/306/c1 104・user 33159/162/c1 54・real 49213/172/c1 59）。

## 不採用理由を5分類で構造化＝ピン破りが研磨の最大の壁と判明（3.321.0, ユーザー指示「拒否理由を別々に構造化して記録する」）
ユーザーが範囲を明示的に絞ったうえで示した優先順（**①拒否理由の構造化 → ②C-1診断表示 → ③専用証明探索 → ④休ピンの条件付き緩和**）の①。
「休を緩める前に、本当に何が C1 を止めているかを根拠付きで示す」ための土台。**読み取り専用・スコアリング不変**
（採否ロジック・重み・探索は完全に不変。捨てた候補の内訳を数えて表示するだけ）。
- **[実バグ] `RejectCulpritStats` がピン破りを主因なしで数えていた**: 3.302.0 で入れた集計は
  `record(after, before)` が `worstWorsenedFamily` を引くだけだったが、呼出側は
  `isBetter(rep, best) && !exactPinRegression(...)` の形。**ピン破りで落ちた候補は `isBetter` が真**＝どの族も
  悪化していないので主因が null になり、`rejected` だけ増えて内訳に何も載らない。ログの
  「不採用N件(主因 …)」の N と括弧内の合計が合わない状態だった（3.302.0 で C1Polish/RangePolish 側は
  ピン破りを別ラベルへ分離したのに、`RejectCulpritStats` を使う6パスは分離されていなかった非対称）。
- **修正**: `record(after, before, pinBroken: Boolean = false)` へ拡張し、**`betterReport` と同じ判定順**
  （ピン破り → 必須増 → 重み増 → 件数増 → 同値）の5分類にした。内訳の合計が常に `rejected` と一致する。
  15箇所の呼出を `val pinBad = exactPinRegression(...)` を先に取る形へ書き換え（`isBetter` の評価順は不変）。
  あわせて `applyC1ExactWindowRepair` にも集計を導入し（従来は不採用を一切数えていなかった）、
  頭打ち表記を「候補が出ない」と「候補は出たが拒否された」で書き分ける。
- **[実データで確定] ピン破りが不採用の 73〜100%**（後処理研磨のみ・ホストJVM）:
  golden = AptPolish 68/74(92%)・FairPolish 45/49(92%)・C3RunPolish 45/51(88%)・C3PatternPolish 38/52(73%)・
  C3mnPolish 102/125(82%)／real = FairPolish 768/875(88%)・C3mnPolish 91/102(89%)・AptPolish 14/14(100%)。
  3.302.0 で「主因は low/high」と読めていたものの内訳が、実際には**厳密ピン(lo==hi)による却下が圧倒的多数**
  だったと判明した（10名中9名の休が lo==hi=10）。「個人回数の厳密ピン群がソフト研磨全体を縛っている」という
  3.303.0 の推測が、族名でなく**却下の種類**として数値で裏づけられた。
  **→ [3.322.0 で訂正] この数値は上記5パスのもので、C1Polish 自身のピン破りは 0〜1件**。C1 の不採用は
  主因 `low`（下限割れ）で、同じ休の固定を指してはいるが**画面に出る族名が違う**（詳細は 3.322.0 の節）。
- 検証: ホストJVM **全376テスト green**（371 + 新規5＝ピン破りは主因を持たず別枠で数える／分類が採否と同じ順序に従う／
  内訳の合計が常に `rejected` と一致する／スコア却下では主因族を出す／空集計は文字列を出さない）。
  実データの決定的な2件は既知ベースラインと一致（golden 2469/306/c1 104・user 33159/162/c1 54）。
  real は 49213/172/c1 59（3.279.1 の既知の帯 49213〜49232 の内側）で、同一 JVM 内の実行順を変えると
  41369/170 まで動く＝JointLNS の壁時計予算由来の非決定性であって本変更の影響ではない
  （`exactPinRegression` は読み取り専用の純関数で、短絡評価から先行評価へ変えても結果は同一）。

## 制約行の無言除外を全族で可視化＋「休」不在の警告＋CI トリガー（3.320.0, 外部レビューの照合から3件）
提示された2本目の敵対検証レビュー（対象 `7a08daa`）を1件ずつ現行コードへ照合し、実在した3件を修正した。
**成立しなかったもの**: ALG-02「fallback elite の並びが hard→total」＝`AdaptiveEliteArchive.compareReports` は
hard→weightedScore→total で 3.287.0 の統一順どおり（`EliteIntegrationPolish` の両 comparator もこれへ委譲）／
ALG-01「block-swap が固定セルで候補全体を棄却」＝3.291.0 で「希望固定日は据え置き」へ緩和済み／
「groupViol がホット評価器の hard に無い」＝3.318.0 で修正済み（レビュー対象コミットより後）。
- **[SEM-02・最重要] 6族の制約行が無言で消えていた**: 3.309.0 は連続パターン(cons3系)の無言除外だけを
  直したが、**cons1(窓の要件)・cons2(個人の合計)・cons41/cons42(群)・cons41s/cons42s(スキル群)にも同じ穴が
  残っていた**（`mapNotNull { ... else null }`）。シフトや群を改名・削除すると、それを参照する行が
  画面にもログにも出ないまま評価対象から消える。窓の要件は重み15、群ペア禁止は実データでも発火する族。
  `Problem._unresolvedRows` に (族ラベル, 行の表示) を記録し（未定義記号は cons3 系と同じ `〈〉` で囲む）、
  `V6SanityPort` の検査 **2f** が cons3 系と同じ形で案内する。読み取り専用＝評価・重みは不変。
- **[SEM-01] 「休」記号のシフトが無いと先頭シフトが黙って休になる**: `restShiftIndex` の `?: 0` は
  3.103.0 で「-1 だと全シフトが勤務扱いになる」別のバグを避けた**意図的な**フォールバックだが、曜日平準化
  (weekly)の勤務/休判定と休関連の診断がその前提で動くため、入力が黙って別の意味になる。検査 **2g** で
  「先頭の『X』を休として扱っています」と明示し、データ側で直せるよう案内する（ロジックは不変）。
- **[CI-01] main 直 push で unit test が走らなかった**: `v6-engine-check` の push トリガーが
  `claude/**` だけで main を含まず、main へ直接 push すると native-parity しか走らなかった
  （通常は PR 経由なので `pull_request` トリガーで走るが、直 push には穴があった）。`main` を追加。
- **実データで誤検知ゼロ**: golden/real/user とも新しい診断は**1件も増えない**（cons3 系の既存3件のみ）。
  将来のデータ破損への保険として機能する形。
- 検証: ホストJVM **全371テスト green**（366 + 新規5＝未知シフト記号の窓ルール／未知群のペア禁止／
  数値が空の群レンジ／解決できる行は案内しない回帰／「休」不在の検出と存在時の非検出）。

## destroy-repair の marginal cost に canDo ガードを追加（3.319.0, 外部レビューの照合から1件）
提示された敵対検証レビュー（`BlockPatternMatch` 対象）を照合したところ、**その機能はアップロードされた
ファイルにも repo にも全 git 履歴（`-S` 検索）にも存在しなかった**。アップロード自体も前回拒否したものと
**MD5 完全一致**（1,550行 vs HEAD 4,213行・末尾が `val stillDeficient0 = (0.` で切断）で、適用すれば
14 の研磨パスが消える。3.178.0 の「マスク最適化 #1〜#4」と同じ構図＝**ファイルは適用しない**。
ただし指摘の中身を現行コードへ1件ずつ照合し、実在した1件を修正した。

| レビューの指摘 | 照合結果 |
|---|---|
| 希望固定でブロック手が実質不活性 | **対処済み**。3.290.0 で「候補4件・採用0」と実測し、3.291.0 で「希望固定日は据え置き」へ緩和して候補 2,267件へ |
| 担当不可シフトの下限を探索圧力に数える | **実在**（下記） |
| 巨大ブロック長で予算を回避できる | **成立しない**。`applyAdaptiveBlockSwapPolish` の `lengths` は `.filter { it in 1..p.T }` で期間日数にクランプ済み |
| キャンセルが協調的 | 仕様。`clusterStop`(3.271.0) でクラスタ締切があり、1回の checker 呼出はミリ秒未満 |

- **修正**: `staffCountPenaltyAt`（destroy-repair の marginal cost、2.57.0 以来の中核）の low 判定に
  `p.canDo(i, k)` が無かった。`Evaluator.fullEvalParts` も `MirrorCore` の checker も元からこのガードを
  持つのに、marginal cost だけが欠けており、担当外シフトに個人下限が設定されたデータ（UI で下限を入れた
  あと群の担当を外す等で起こりうる）では**実際には存在しない違反を重み90 で数え**、候補選択を無駄な方向へ
  引っ張る。最終採否は checker が守るので誤った勤務表は出ないが、有効な候補を取りこぼす。
  high は担当外なら n=0 で発火せず、かつ Evaluator 側もガードを持たない＝**既に一致している**ので触らない。
  apt は `Problem` 構築時に bucket=canDo でガード済み。テスト用に `private`→`internal` 化（3.169.0 の前例）。
- **実害は現データではゼロ**: 実データ3件（golden/real/user）で「担当外なのに下限>0」のセルは **0件**＝
  潜在バグ。3.309.0 の `gateW` と同じ論理（実害が到達不能でも契約違反は実在し、将来のデータで顕在化する）。
- **後処理ベンチが変わらないことは構造的**: `staffCountPenaltyAt` の呼出元は `V6NativeOptimizer` の
  destroy-repair 3関数だけで、`runPostOptimization` からは呼ばれない（`V6HotfixPasses` 内の参照はコメント1件）。
  実測でも user 33159／golden 2469 は完全一致。real の 49223→49213 は**本修正の効果ではなく** JointLNS の
  壁時計予算由来のばらつき（3.279.1 の既知事項。帯は 49213〜49232・所要時間も 20.4s→15.2s と負荷が違う）。
  真の効果は RSI/ALNS 探索側だが、そこは PORTFOLIO のノイズで測れない（3.310.1 の教訓）。
- 検証: ホストJVM **全366テスト green**（363 + 新規3＝担当外の下限は marginal cost に入らない／担当可なら
  従来どおり 2×90=180／上限側は不変）。

## c42 の自己ペア／順序重複の除去と groupViol の HARD 統一（3.318.0, ユーザー明示指示）
ユーザー指示「c42 自己ペア (HF77) と groupViol 非対称(目的関数変更)などを賢く考え対応する」。長く
「指示待ち」として残していた2件を、どちらも**4面（チェッカー／Evaluator／DeltaEvaluator／C++）同時**に直した。

### ① c42/c42s の同日ペア計数（HF77＝スコアが変わる）
- **バグ**: `for (i in left) for (i2 in right)`（Kotlin）／`n1 * n2`（Evaluator・Delta・C++）は
  left と right が同じ集合になるケースを考慮していなかった。**同じ集合になるのは `g1==g2 && s1==s2` の
  ときだけ**（s1!=s2 なら同じ職員が同日に両方へ就くことはなく、g1!=g2 なら sgrp が違うので両方には入らない）。
  そのとき素朴な積 n² は ①自分自身とのペア n 件 ②同じペアを (a,b) と (b,a) で2回、を余分に数える。
- **実データの実害**（修正前に計測）: `群9/休 × 群9/休` の行が**1人だけの群**に付いており、その人が休むたび
  自己ペアを1件ずつ数えていた。**real は c42=16 のうち 9件、user は 13 のうち 8件**が自己ペア（順序重複は
  当該データでは 0 件＝該当群が1人でペアを作れないため。ただし2人以上いれば同じ理由で二重計上になる）。
  golden は同一集合の行が発火しておらず 6件とも正味。
- **修正**: 共通ソース `c42PairCount(sameSet, n1, n2) = if (sameSet) C(n,2) else n1*n2`（`Evaluator.kt`）を
  新設し、Evaluator 2箇所・DeltaEvaluator 4箇所（preview の差分2・全量再構築2）・C++ 6箇所
  （`fullEvalParts` 2・`contribDayGroups` の bit/scalar 各2）が同じ式を読む。チェッカーは mark を伴うので
  ループ内で `i == i2` と `sameSet && i2 < i` を弾く同値の形。**違反セルのヒント**（C++ `collectViolationCells`
  の `pairCells`）も `anyL && anyR`（1人でも真）→ 同一集合なら `nL >= 2` を要求する形へ揃えた。
- **結果**: c42 は real 16→**7**、user 13→**5**、golden 6→6（不変）。

### ② groupViol を評価器の HARD へ（目的関数の変更）
- **不整合**: `MirrorKeys.hard` は元から4族（groupViol/c3n/covU/pref）なのに `Evaluator.fullEvalParts` の
  hard1 は3族（c3n/pref/covU）で、**同じ盤面に対してチェッカーと評価器が違う hard を返していた**
  ＝ SA/ALNS の受理と最終採否が別基準。docstring は「意図的」と書いていたが、`MirrorKeys.hard` との
  矛盾は残ったまま（3.309.0 で `gateW` を直したときと同じ論理＝実害が到達不能でも契約違反は実在する）。
- **修正**: Evaluator の pref ループへ groupViol を追加。DeltaEvaluator は `dGrpV`/`hGrpV`/`groupViolAll()` を
  新設し `dHard` と `scoreFrom` へ合流（セル単位なので差分は1セルぶんで厳密）。C++ は `contribPrefCell` を
  **`contribCellHard`（pref＋groupViol）へ改名**し `fullEvalParts` にも同じ加算を入れた（`deltaApply` は
  before/after でこの関数を呼ぶので差分は自動的に正しい）。
- **実害は元からゼロだったことを追認**: 後処理研磨の決定的ベンチで hard は 3データセットとも不変
  （real 6／user 4／golden 0）。入口の `hf67HardRepair` が群外セルを正規化し、探索オペレータは
  `allowedShiftsForStaff` から選ぶため群外を作らない、という既存の防御が実際に効いている。

### 検証
- **native parity**: `host_parity_bench` を2回（c42 修正後・groupViol 修正後）実ビルド・実行し、いずれも
  **2,996,665手・mismatch=0**。harness は 3.199.0 以降 **非canDo セルを盤面に混ぜる**ので groupViol も
  実際に照合されている（bit-op speedup ×2.04〜2.11 で速度退行なし）。
- **ホストJVM 全363テスト green**（356 + 新規7）。新規は `C42PairCountTest`（1人=違反0／2人=1組／3人=3組／
  異なる群は従来どおり／同じ群でもシフトが違えば従来どおり。**チェッカーと評価器の両方**で確認）と
  `DeltaEvaluatorTest` 2件（担当外への移動を含むランダム2万手で Δ==フル／groupViol が hard に計上）。
  既存の差分テストは移動先を `p.bucket[...]`＝常に担当可から選ぶため、この族の Δ を一度も通っていなかった。
- **決定的ベンチ**: user は 33167→**33159**（消えた自己ペア8件ぶん）、golden は 2469 で不変、real は
  49221→49223。**c42 の件数が正しくなったぶん weighted が下がるのは「勤務表が良くなった」のではなく
  「数え方が正しくなった」だけ**で、real の +2 は目的関数が変わって別の局所解へ着地した経路依存
  （JointLNS の壁時計ばらつき帯 49221〜49232 の中）。hard は3件とも不変。

## 分散指標の平準化2パスを撤去＝目的関数と指標が一致していなかった（3.317.0）
バックログに「平準化研磨は分散指標で目的関数(fair/weekly=L1)と別物＝既知の冗長」と 3.84.0 以来
記録したまま**一度も測っていなかった**項目を消化した。
- **測定**: 実データ3件（golden/real/user）で `runPostOptimization` のログを見ると、
  `GroupEqualize`/`WeeklyEqualize` は**3件とも採用0回**で、しかも自分たちが最小化するはずの分散指標すら
  1ミリも動いていない（`ばらつき 24.0->24.0` / `偏り 76.3->76.3`）。対照的に L1 ベースの
  `WeeklyRebalance`(3.197.0) は user で採用1回・total 172→170 と実際に効いている。
- **ablation**（2パスを完全に外して同一seedで実行）: user/golden は weighted が**完全一致**、real は
  49231→49221 だが これは JointLNS の壁時計予算由来のばらつき帯（3.313.0=49221／3.314.0=49231）の中で、
  撤去の効果ではない。**寄与ゼロが確定**。
- **なぜ効かないか**: 目的関数の fair/weekly は 3.72.0 以降 **L1偏差**（round(平均)からの偏差和）で評価
  されるのに、この2パスは**分散**を下げる手だけを採る。`mainNotWorse` ガードで主目的の悪化は防いでいた
  ものの、改善方向が目的関数と一致していない＝「安全だが無益」の典型。役割は L1 ベースの後継が完全に
  代替している（fair→`applyFairPolish` 3.235.0 ／ weekly→`applyWeeklyRebalancePolish` 3.197.0 ＋
  `applyAlternatingSoftPolish` 3.198.0 が weekly の限界費用を Hungarian の費用に含む）。
- **撤去**: 2関数＋専用ヘルパー3つ（`mainNotWorse`/`groupShiftVariance`/`dayOfWeekVariance`）＝約130行を
  定義ごと削除（3.300.0 の旧 `applyBlockSwapPolish` と同じ扱い。3.300.0 の C3 3者回転は「別データで
  効きうる」ため格下げに留めたが、こちらは**指標そのものが目的関数と別物**なので格下げでなく撤去が筋）。
  `V6FinalBridgePortTest.equalizePolishesNeverWorsenMainObjective` は対象を後継2パスへ差し替えて維持。
- 検証: ホストJVM **全356テスト green**（件数不変＝テストは差し替えのみ）。撤去後の決定的ベンチは
  ablation と完全一致（real 49221／user 33167／golden 2469）。

## 休の下限合計チェックが必ず誤警告していた（3.316.0, 診断を実データに当てて発見）
c1 残差（golden 104・real 58・user 54）に対して事前診断が何を言っているかを実データ3件で一覧したところ、
**real/user で「『休』の回数下限の合計が80回ですが、必要数の合計は0回しかありません」が必ず出ていた**。
- **誤警告である理由**: 休は「1日に何人休んでよいか」という座席（必要人数）の概念を持たない。実データは
  休に `need1=0` を明示設定しているため `seatsHi=0` になり、下限合計80 > 0 で**データが健全でも必ず発火**する。
  実際 real/user とも covO は出ていない（31日×10人=310セルのうち80が休でも何も破綻しない）。
- **3.235.0 の取り残し**: 同じ理由で適切回数の検査（6-C）は 3.235.0 で「必要人数の合計」→
  **`restCapacity`**（各職員が他シフトの個人下限を満たしたうえで最大何日休めるかの合計）へ差し替え済み
  だったが、**この検査Aだけ取り残されていた**（当時の記録も「チェックA/Bはスコープ外」と明記している）。
- **修正**: `restCapacity(p)` を `aptBalances` からトップレベルへ切り出して共有し、検査Aは
  **休のときだけ**比較対象を `restCapacity` にする（非休シフトは従来どおり必要人数の合計＝挙動不変）。
  あわせて休は need に依存しないので `hasDemand` ゲートも通す（3.301.1 で 6-C に入れたのと同じ理由。
  休に必要人数が1日も設定されていないデータでも検査が走るようになる）。検査B（上限合計 < 必要数）は
  休の `seatsLo=0` で発火しえないため変更なし。
- 読取専用の診断のみ＝エンジン・重み・スコアは完全に不変。実データで real 8件→**7件**（消えたのは
  この1件だけ・他の診断は不変）、golden は元から出ておらず3件のまま。
- 検証: ホストJVM **全356テスト green**（353 + 新規3＝控えめな休の下限は誤検知しない／期間日数を超える
  下限は検出する／他シフトの下限を差し引いた実質上限を下回れば検出する。同一設定の非休シフトが
  従来どおり検出されることも同じテストで固定）。
- **同時に判明した未対応**: golden は c1=115 なのに診断は「Dﾃ 窓ルールが1不足」しか言わない。休の窓
  （残差の大半）は供給が足りており「構造的に不能」ではないが、**厳密ピン(lo==hi)が配置の自由度を奪って
  いて解けない**。3.76.0 の壁/ダイヤル分類器は供給 vs 需要しか見ないため、この「ピンによる自由度の喪失」を
  説明できない。利用者は残る c1 の理由を知る手段がない＝**診断の穴として記録**（対応は別途）。

## C1厳密窓修復の探索を実採否と揃える＝厳密ピン・c3n を目的関数へ（3.315.0, ユーザー指示「次へ進める」）
3.314.0 の事後検証で見つけた「`applyC1ExactWindowRepair` が c1 窓の約97%で即諦め」の続き。当初は
「関与職員集合 `mSet` の選抜」を疑ったが、**測ったら別の場所が原因だった**。
- **選抜案は測って捨てた**: 「焦点の不足窓内でそのシフトを持つ職員だけに絞る」案は、実データでは絞っても
  減らない（窓は5〜15日幅で休はほぼ全員が持つ。選抜なしで cap に収まるのは real 6/78・user 2/64・
  golden 3/115）。**どう選んでも恣意的**で、根拠のある選抜基準が作れない。
- **cap=10（=職員数 S＝bail ゼロ）で母数を作って却下理由を測ったのが転機**: patch は出る（real 10件・
  golden 15件）のに**採用は 0**で、却下は**全件**が「厳密ピン破り」か「c3n 増」だった
  （real: ピン6・c3n4／golden: ピン15／cap=6 の real: c3n2）。つまり集合サイズではなく、
  **`solveWindow` の目的関数が joint c1 だけで、厳密ピン(staffRange lo==hi)も禁止連続(c3n)も見ていない**
  ことがボトルネック。探索が、実採否が必ず却下する方向へ最適化していた。3.293.0 で AdaptiveBlockSwap に
  見つけたのと同じ構図（あちらもピン破りと c3n が不採用の全部だった）。
- **実装**: `solveWindow` の葉（完全配置）で `acceptableLeaf()` を検査し、通らない葉は best/patch の
  候補にしない。①**ピン**＝`exactPinRegression` と同一意味論（目標から遠ざかる変更のみ禁止・現状維持と
  接近は許す。既に外れているデータ側の不整合はそのまま）②**c3n**＝行ローカルなので
  `C1DeltaPrefilter.staffC3nFires` で M 内合計が増えないことを厳密に判定。**候補生成の絞り込みであって
  目的関数・重みの変更ではない**（MirrorCore/Evaluator/DeltaEvaluator/C++ は無変更・HF77 非該当・keep-best 不変）。
  **`minFocusResidual` は従来どおり全葉で測る**＝A4 provenWalls（「coverage入替でどう並べても焦点は
  解消不能」）の意味論は完全に不変（制約下の最小に変えると壁判定が増える方向へ動き、3.76.0 の
  「false wall を出さない」原則に触れるため意図的に分離）。
- **実測（後処理研磨の決定的ベンチ・3データセット）＝品質は完全同一・時間は 15〜33% 短縮**:
  real 20,982→**17,917ms**／user 35,002→**23,283ms**／golden 24,543→**16,521ms**、weighted は
  49231／33167／2469 で**3件とも 3.314.0 とバイト一致**。制約を足したのに速いのは、安い c3n 検査が
  途中で早期 return して重い `jointC1()`（O(m×rules×T×day1)）の評価を省くため。
- **正直な限界**: 新制約下で patch は3データセット・cap=6/10 とも **0 件**になったが、これは
  「**探索が到達した範囲に無い**」であって存在しないことの証明ではない（cap=10 では全窓が nodeBudget 
  切れ＝`exhaustive=false`）。品質が変わらないのは「元々全部却下されていたものが、そもそも作られなく
  なった」ため。真価は**別のデータで patch が出たとき**＝それは実際に採用され得る手になる。
- **cap 引き上げは今回も採らない**: 母数は増えるが却下率は変わらず、2.4〜2.8倍遅い（3.314.0 で実測・
  `docs/algorithm_portfolio.md` の「実測で否決した提案」に記録済み）。
- 検証: ホストJVM **全353テスト green**（350 + 新規3＝制約なしなら従来どおり joint 2→1 の手を見つける
  回帰／厳密ピンを崩す手は候補にしない／禁止連続を増やす手は候補にしない。3日2職員の最小盤面で
  到達可能な全4配置を手計算し「joint 最小を取る配置は必ず焦点が X を1個受け取る」ことを検算済み）。

## レビュー積み残し5件の解消（3.314.0, ユーザー指示「修正する」）
6本のレビューで「確認済みだが未着手」として残していた M-05〜M-08 / M-10 を解消。スコアが変わる2件
（c42 自己ペア＝HF77・groupViol 非対称＝目的関数の変更）は当時は明示指示待ちで据え置き
**→ 3.318.0 でユーザー明示指示により両方とも4面同時に修正済み**。
- **[M-05] C1「証明済み壁」が部分集合しか探していなかった**: `solveWindow` の余力職員が
  `p.sgrp[i] == p.sgrp[v.staff]` の**同群限定**で、別群を経由する3者循環を見落としたまま
  `exhaustive=true` を返していた。さらに `maxInvolvedStaff` の cap で候補を `break` したあとも
  「探索し尽くした」と主張していた（真部分集合しか見ていないのに壁を証明）。①候補を canDo 基準へ広げる
  （DFS の `place()` が配置ごとに `p.canDo(i, sh)` を検査するため群をまたいでも不正解は生まれない）
  ②cap で切り捨てたら `exhaustive` を名乗らない。回帰テスト
  `truncatedCandidateSetMustNotClaimAnExhaustiveProof`。
- **[M-06] C1厳密窓の重複排除が粗すぎた**: `seenFocus` が `staff*1000 + shift` ＝(職員,シフト)ごとに
  1回だけで、コメントの「多数窓は1スパンに束ねられる」はスパン幅(`maxWindowDays`)に収まる窓にしか
  当てはまらない。**それより離れた別の C1 塊が同一対象とみなされ探索されないまま**だった。
  キーを (職員, シフト, スパン開始) へ。同一スパンの重複は従来どおり `deadSpans` が弾く。
- **[M-07] Elite統合の予算が root ペアで尽きていた**: `selectPairs` は `(0,i)` を全部入れてから
  `>= maxPairs` で return しており、エリートが `maxPairs`(既定12) 件以上あると**非root ペア
  （elite 間・高距離・別役割）へ一度も到達しなかった**＝「EliteIntegration 改善なし」の説明になりうる。
  root に 2/3 を上限として割り当て、残り 1/3 をランキング済み非root ペアへ必ず残す。
  `selectFusionGroups` の 2者組も同型のため同じ配分に（旧: 3者融合へ到達しない）。
- **[M-08] コンポーネント別CSV の先頭行欠落と1行拒否**: 4つの `parse` が `if (rows.size < 2) return null`
  で**1行だけのCSVを無条件に拒否**し、ヘッダ判定も「先頭が既知の職員名か」という間接的な推測だった。
  とくに `parseUpsert` は**未知名を新規追加する経路**で、「ヘッダ文字列を職員登録しない」保守のために
  **先頭が新規職員のヘッダ無CSVはその1件を黙って捨てて**いた。共通ヘルパー `csvBody(rows, 実ヘッダ)` を
  新設し、`build()` が出す実ヘッダ（氏名／種別）の一致で判定＝厳密判定なら保守は不要で取りこぼしも消える。
  ガードは `rows.isEmpty()` へ緩め1行データを許可。
- **[M-10] 短予算の直接API呼出が締切を超えていた**: `minRunMs`/`postReserveMs` の下限 8 秒は
  「UI 経路は 10 秒下限」を前提にした値で、1〜7 秒を指定すると `searchDeadlineMs` が `hardDeadlineMs` を
  追い越していた（予算1秒で searchDeadline が start+8秒）。両方を予算でクランプし
  `searchDeadline <= hardDeadline` を構造的に保証。**10 秒以上では `minRunMs` が支配するため結果は不変**
  （1/5/10/30/60/300 秒で検算済み）。
- 検証: ホストJVM **全350テスト green**（349 + 新規1）。後処理研磨の決定的ベンチは3データセットとも
  3.313.0 と完全一致（golden 2469/306/c1 104・real 49221/180/c1 59・user 33167/170/c1 54）＝退化なし。
- **[事後の敵対検証] 所要時間**（`runPostOptimization` 3回の中央値・ウォームアップ後）: 3.313.0 →
  3.314.0 で real 23,807→20,982ms / user 29,536→35,002ms / golden 22,957→24,543ms。**M-06 で厳密窓探索が
  5倍**（real 12→62・user 9→40・golden 17→101）**になっても時間は増えていない**（user/golden はレンジが
  重なる帯・real はむしろ速い）。`exactWindow` は `clusterStop`（3.271.0 のクラスタ専用締切）の内側にあり、
  探索を増やしても予算超過は構造的に起きない。
- **[事後の敵対検証] M-05 の候補拡張は実データで休眠**: 「探索が速くなったのは、拡張で `mSet` が cap を
  超えて**逆に諦める窓が増えた**からでは」という自分の仮説を測って**反証**した。実際には `mSet` の
  **基礎集合（スパン内でそのシフトを持つ職員・上限なし）が平均 8.9〜9.8 人**で `maxInvolvedStaff`(6) を
  先に超えるため、**拡張ループがそもそも一度も走らない**（旧実装と関与職員集合が完全一致・
  cap 即 bail が real 76/78・user 63/64・golden 112/115）。**より大きな発見＝`applyC1ExactWindowRepair` は
  この実データの c1 窓の約 97% で即座に諦めている**（M-06 の5倍は「5倍多く即諦める」なので時間が
  増えなかった、とも説明がつく）。一方 **M-05 のもう半分（cap で切り捨てたら `exhaustive` を名乗らない）は
  探索に到達した窓の 2/2・1/1・3/3 すべてで発火**＝旧実装が偽の「証明済み壁」を出しえた地点で稼働中。
  **上限 6→10 の引き上げは実測で否決**（2.4〜2.8倍遅く品質は同値か悪化）＝詳細と数値は
  `docs/algorithm_portfolio.md` の「実測で否決した提案」へ記録。

## free repair の締切確認と UI 指標の単位統一（3.313.0, 6本目のレビュー）
6本目も対象は `552b553`＝3.305.0 で HEAD より7コミット古い。「継続」とされた項目のうち P0 skillIdx・
C1構造下限・ForbiddenDiag・MUSキー衝突は 3.311.0/3.312.0 で修正済み。新規のうち実在を確認できた2件を修正。
- **[実バグ] free repair 群に停止確認が1つも無かった**: `applyCovOFree` / `applyC41Free` / `applyC42Free` /
  `applyCovUChains` は違反セル × 候補職員の二重ループの内側でフル checker（`commitBestMove`）と
  `findCovUChain`(BFS) を呼ぶ高コストパスなのに、`shouldStop` を一切見ていなかった（**3.161.0 で
  V6HotfixPasses の研磨パスへ入れた「内側ループでも締切を見る」の対象漏れ**。あちらは後処理研磨、
  こちらは RSI 仮説生成器で、私が 3.204.0/3.209.0/3.233.0 で作った側）。4関数に
  `shouldStop: () -> Boolean = { false }`（**既定つき＝既存の直接呼出・テストは挙動不変**）を足し、
  ルール/日/セルの各ループ先頭で確認。`rsiGenerateHypothesis` 経由で `runRsi` の締切を配線。
- **[表示バグ] 「改善 N%」が重み付きと生件数を引き算していた**: `initSoft` は
  `ev.split(ev.fullEval(init)).second`＝**Evaluator の重み付き soft**なのに、`makeUi`（中央のUI構築）が
  書く `bestSoft` は `report.soft = total - hard`＝**生件数**。`progressSummary` の
  `改善 N% (initSoft→bestSoft)` は完了後にこの2つを引くため、実データなら 1900→170 で「91%」のように
  **大幅に水増し**されていた。`initSoft` を `lp.report.soft` へ揃える（`LoadedProblem` は既に report を持つ）。
  **残る不整合を正直に記録**: ライブSA経路（`ev.split(pr.bestScore)`）はまだ Evaluator 基準だが、
  次の report push で `makeUi` が上書きするため一時的。完全統一には progress 側にも checker 呼出が要る。
- **[報告のみ・確認済み] groupViol が checker と探索評価で非対称**: `MirrorKeys.hard` は
  `[groupViol, c3n, covU, pref]` の4族だが、`Evaluator.fullEvalParts` の `hard1` は `c3n + pref + covU` の
  3族で、docstring も明示的にそう書いている（意図的）。**露出は狭い**＝探索オペレータは
  `allowedShiftsForStaff` で群外を作らず、入口の `hf67HardRepair` が既存の群外セルを担当可へ正規化し、
  最終採用は checker ベースの `betterReport` が groupViol を数える。閉じるには Evaluator＋Delta＋C++ を
  同時に変える目的関数の変更＝明示の承認と A/B が要るため据え置き。
- **[未検証・報告のみ]** WorkManager の run identity（固定ファイル名＋REPLACE の競合）・休 index 0 の暗黙前提・
  入力規模の上限・main 直 push の CI 差・Elite 統合の root 偏り・C1 window の重複排除キー・
  ヘッダなし職員CSV は、いずれも実機/CI でしか動的確認できないか別途の設計判断が要るため今回は対象外。
- 検証: ホストJVM **全349テスト green**。後処理研磨の決定的ベンチは3データセットとも 3.312.0 と完全一致。

## C1合同LNSの「構造下限」からSOFT個人回数を除外（3.312.0, 5本目のレビューの新規項目 N-01）
5本目のレビュー対象は `552b553`＝3.305.0 で、私の HEAD より6コミット古い。「前回の M-01〜M-10 が全部
残っている」は **HEAD に対しては成り立たない**（M-01/M-03残り/M-04/M-09 は 3.311.0、M-02 は 3.309.0 で修正済み）。
新規は N-01〜N-03 の3件。
- **[N-01・実バグ] 「構造下限」が SOFT の個人回数を硬い制約として扱っていた**: `singleRuleLowerBound` は
  `rangeLo`/`rangeHi` を count の上下限として DP に課しており、返る値は「**rangeHi を一度も超えない範囲での**
  c1 最小値」＝真の下限より大きくなる。個人回数は SOFT（low=90 / high=45）で、c1(15) より重いだけであって
  禁止ではない。反例（レビュー提示・検算済み）: T=7・「4日窓で X≥1」・high(X)=0 なら、X なし＝c1=4
  (weighted 60) に対し中央へ X を1つ置くと c1=0・high=1(weighted 45) で **betterReport は X を選ぶ**のに、
  旧下限は 4 を返す。影響は `best.c1 <= lowerBound` の**早期終了**と「構造下限到達」の**誤ったログ**。
  cheap 版の `if (hi < c.day2) return starts`（全窓を不可避と宣言）は反例そのもの。両方から range を除外。
  **`wishLocked` は下限に残す**: 希望を破る代金は pref=9000 で、c1=15 を600件消して初めて釣り合う＝
  c1 を下げる目的では実質的に硬い制約。回帰テスト `structuralLowerBoundIgnoresSoftPersonalCaps`。
- **[N-02・報告のみ] `StaffObjective` の前フィルタが c3/c3m を窓数で数える**: 公式は
  `C3Run.rowDeficit`（run-deficit）。**CLAUDE.md 3.84.0 で既に「報告のみ＝判断/測定待ち」として記録済み**の
  既知の近似で、前フィルタ限定＝候補を取りこぼすだけ（keep-best 安全・誤った勤務表は出ない）。
  直すと候補選択が変わる＝探索変更で A/B が要るため、今回は据え置き。
- **[N-03・実害なし] `breakableDaysFor` が T>64 で j±1 へ退避**: 指摘は事実だが、この関数は
  `PolishGate.wideC3nBreakDays`（**既定 OFF**）配下でしか呼ばれず、かつ業務前提は**期間 ≤ 1か月(31日)**
  （3.305.0 で記録）＝ T>64 は実運用で発生しない。
- 検証: ホストJVM **全349テスト green**（348 + 反例1）。後処理研磨の決定的ベンチで golden(2469/306/c1 104)・
  user(33167/170/c1 54) が既知ベースラインと**完全一致**、real は weighted 49231→49221（改善方向・
  JointLNS の壁時計由来のばらつき帯内＝3.279.1 の既知事項）＝退化なし。

## 禁止連続診断の偽 PINNED 修正ほか4件（3.311.0, 4本目のレビュー）
レビュー対象は `d3ccace`＝**3.304.0** で、私のブランチ HEAD より6コミット古い。既修正を分離したうえで
実在を確認できた4件を修正。**うち最重要（M-04）は私自身の 3.280.0 のバグ**。
- **[M-04・自分のバグ] 希望どおりのセルを無条件に「希望固定」と診断していた**: `diagnoseForbiddenCell` は
  `wishLocked && wish == cur` で **HARD 差分を一切見ずに即 PINNED を返して**いた。根拠として書いていた
  「pref(9000) の増加が c3n(7000) の減少を上回る」は、**そのセルが c3n fire 1件にしか関与しない場合しか
  成り立たない**。反例＝禁止「X→X」・行 X,X,X の中央セルは 2件の fire に関与し、休へ動かすと
  c3n 2→0 / pref 0→1 ＝ `betterReport` の第1キー hard が **2→1 と厳密に改善**（weighted も 14000→9000）＝
  isBetter は採用する。偽の PINNED は run 全体を「構造壁」と誤診し、**3.281.0 の短い停滞タイムアウトを
  早期に発火させうる**。pref の増加分を c3n の正味減と同じ単位で勘定する形へ修正
  （`prefCost` を導入し `after + prefCost < firesBefore`）。
  **[実装中に自分のテストが穴を突いた]** 初版は `netC3nSafe` の否定を「新たな禁止連続を作る」と同一視して
  おり、「c3n は減るが pref 代を払えない」代替まで隣接日調整へ流れて既存テストが落ちた。
  `netOk`（正味 HARD が減るか）と `createsNewRun`（そもそも c3n が減らないか）を**別の条件として分離**し、
  中間ケースを `prefBlocked` として数える形へ是正。`prefCost==0` のとき両者は厳密な補集合＝
  **希望の無いセルの分類は完全に不変**。
- **[M-01・P0] `ssk` が JNI 側で未検証**: `magi_native.cpp` は 3.171.0 で `sgrp` を一括検証するようにしたが、
  **`ssk` は取り残されていた**。`sskMask` は ssk の最大値からサイズを決めるため（`buildGroupMasks` の
  `maxGs`）、外部JSON由来の巨大な skillIdx がそのまま届くと巨大確保になり得る。未所属の `-1` は正規の値
  （3.70.0「(なし)」）なので許可し、正の値に保守的な上限（職員数）を課す。外れれば
  ハンドル生成を拒否＝既存の「handle==0 → Kotlin へ安全退化」契約にそのまま乗る。
- **[M-03 の残り1件] `V6HotfixPasses:602` の強摂動**: 3.270.0 の `wishLocked` 統一の取り残し。生の
  `wish < 0` だと**実現不能な希望**のセルまで摂動対象から外れ、そこに座礁した groupViol セルが永久に
  動かせなくなる。他の指摘サイト（`C1DeltaPrefilter:68`・`C1JointLns:282,545`・`PersonalBalance:274`）は
  すべて `wishLocked` ガード内で**既に正しい**ことを確認済み。
- **[M-09] MUS キャッシュキーの衝突**: `first * 1000 + second` は `(1,1000)` と `(2,0)` がどちらも 2000 に
  なる。`minCount>=1000` は不合理なデータだが入力可能で、これはプロセス全域キャッシュなので誤ヒットは
  誤った診断そのものになる。区切り文字入りのキーへ。
- **再現しない／既に対処済みの指摘**: M-02 の `isBalanceable` は 3.309.0 で `covUCell` へ委譲済み。
  M-03 の V6HotfixPasses 本体は 3.270.0 で15箇所修正済み。**「未コミット差分7ファイル・
  `EliteIntegrationPolishTest.kt` が途中で終了」は現在の作業ツリーと一致しない**（クリーン・348テスト green）。
- 検証: ホストJVM **全348テスト green**（347 + 反例1＝中央セルが2 fire に関与する局面で PINNED にしない・
  構造壁と誤診しない）。C++ は host parity harness で再ビルド＋**299万手 mismatch=0**。

## PERSONAL_RSI の focus 選択を A/B して否決（3.310.1, ユーザー指示「あなたがABテストをする」）
3.308.2 の敵対トレースで報告した「PERSONAL_RSI が6族のうち4族しか狙えない」を掘り下げたところ、
より大きな問題が見えた: focus 連鎖 `apt → high → low → fair → total` は**固定順**なのに、重みは
**low=90 > high=45 > apt=fair=weekly=c2=1** ＝**一番軽い apt を最初に見ている**。
- **前提は実測で確定**: 実際の呼び出し地点を計測し、**20回すべてが `apt` を選び、重み最大の個人族は
  20回すべて `low`(18)/`high`(2)。一致 0/20**。
- **A/B**: 「重み×件数が最大の個人族を選ぶ」変種を作り、3データセット × 5シード × 同一シード対比の
  **15ペア**で測定（判定基準は測る前に固定＝B が weighted で 11/15 以上勝つこと）。同一バイナリ内の
  フラグ切替でビルド差の交絡を排除。
- **結果＝否決**: **B は 6勝8敗1分**（符号検定 p≈0.79）。平均は golden −40（B悪化）/ real +81（B改善）/
  user −99（B悪化）で符号が一貫しない。golden は5ランのうち4ランで PERSONAL_RSI 呼出が 0 ＝ほぼ純粋な
  ノイズ対照になり、それでも幅 ±150。観測差の大半はこの帯の中。
- **機構は設計どおり動いた**（B は狙いどおり low を減らす。real の low 合計 15→11）。それでも weighted に
  変換されない。理由は実装から説明がつく: focus が low でも apt でも `rsiGenerateHypothesis` は同じ
  `destroyRepairStaff` へ流れ、その marginal cost `staffCountPenaltyAt` は**元から low=90/high=45 を
  織り込んでいる**。修復側は focus に関わらず low/high を意識しており、focus が変えるのは「どの職員を
  壊すか」だけ。だから狙う族を替えても結果がほとんど動かない。
- **反証されたコードは残さない**（3.307.0 の規律）。変種はスクラッチのみで repo に入れていない。
  `docs/algorithm_portfolio.md` の「実測で否決した提案（再提案しない）」へ記録。
- **教訓**: 「明らかに間違って見える選択順」でも、下流がその情報を別経路で既に持っていれば直しても効かない。
  **前提の確認（0/20）と効果の確認は別物**。前提が鮮やかに確認できたことを効果の証拠と取り違えない。
- 計測中のビルドは全て `nice -n 19` で流した（PORTFOLIO は壁時計駆動＋8ワーカーが CPU 密なので、
  通常優先度のコンパイルを挟むと特定ペアだけ歪む）。

## C1TemporalDp に状態数の安全弁（3.310.0, 3本目のレビューの新規項目）
3本目のレビューは1〜4番が2本目と同一（既に 3.309.0 で修正・push 済み）で、新規は
「C1TemporalDp の最大窓長20の厳密探索が時間・メモリを食う」の1件のみ。
- **レビューの根拠は誤りだが、リスク自体は実在**: 指摘は「DP二面で概算128MiB超」だが、
  `C1TemporalDp` の DP は **`HashMap<Long, Record>` の疎表現**であって密配列ではない
  （密配列だったのは `C1JointLnsPolish` の `Array(hi+1){IntArray(maskLimit)}` で、そちらは
  3.305.0 で既にガード済み＝レビューはそこと混同している）。
- **ただし状態数に上限が無いのは事実**。到達可能状態は「窓内の対象日の 2^n × 追加(≤maxRelocations)の
  組合せ × count × reloc」で決まり、既存の窓長ガード（`maxWindow > 20 → null`）**だけでは縛れない**
  （出現回数の多いシフト × 長い窓で数百万に達しうる）。時間も同じ要因で伸びる。
- **対応**: `MAX_DP_STATES = 200_000` を新設し、各日の DP が上限を超えたら `return null` で諦める。
  状態数そのものを縛るのでメモリと時間が同時に有界になる。`null` はこの関数の正規の出口
  （`t>63` / 有効ルール無し / 既に違反0 / `next.isEmpty()`）で、呼出側の `C1TemporalFlowPolish` は
  提案が無いものとして扱うだけ＝**keep-best 不変・退化しない**。3.305.0 と同じ考え方。
  テストから打切り経路を踏ませるため `maxDpStates` パラメータ化（既定値は定数＝呼出側は非破壊）。
- **Low「下限推定を参考値と明示」**: covU 側は 3.263.0 で既に正直化済み（チェーン探索で実証してから
  「どう組んでも解消できません」と言う）、「構造HARD下限=0」の文言も 3.274.0 で緩和済み。追加対応なし。
- 検証: ホストJVM **全347テスト green**（346 + 新規1＝上限超過で例外でなく null を返す）。

## 外部レビュー2本の検証と確認できた4件の修正（3.309.0）
提示された2本のレビュー（計12項目）を1件ずつ実コードへ照合。既知・既修正・再現しないものを分離し、
**実在を確認できた4件だけ**を修正した。いずれも既決の規約への整合＝新しいヒューリスティクスではない。
- **[最重要・無言のデータ欠落] 未定義シフト記号を含む連続パターンが黙って消えていた**:
  `Problem.resolveC3` は記号解決に失敗した行を `if (si < 0) return@mapNotNull null` で**無言で捨てて**
  おり、シフトを改名・削除するとその行を参照する連続パターン（**禁止=HARD を含む**）が評価対象から
  消えるのに、画面にもログにも何も出なかった。**すぐ下の「L>期間」のケースは `_c3OverT` に記録して
  Sanity が案内する**のに、この分岐だけ同じ扱いになっていない非対称。同じパターンで
  `_c3UnknownShift` を新設し、検査2e として「〈〉で囲んだ記号が今のシフト一覧にない＝この行は評価されて
  いません」を案内する。読取専用・評価/重み不変。レビューが指摘した「保存ログ c3n=1 → 再評価 c3n=0」の
  不一致も、記号ドリフトで行が落ちた結果として説明がつく（評価器のバグではない）。
- **[keep-best統一の第4の取り残し] `V6LateOperators.gateW` が HARD を見ていなかった**: 旧実装は
  `nv.weightedScore < cur.weightedScore` だけで、すぐ上の `gate()` は 3.287.0 で hard 優先へ統一済みなのに
  ここだけ 3.287.0/3.289.0 の全サイト掃討から漏れていた（呼出元は ChainSwap3/4）。
  **ただし実害は到達不能**＝同日3〜4者交換は最大4セルしか変えず soft から得られる weighted 改善は
  現実的に数百（low=90/high=45/c1=15）に対し HARD の最小重みは c3n=7000。契約違反としては実在するが
  「改善したつもりで HARD を悪化させる」事象は起きない。将来 HF77 で HARD 重みが下がったときに静かに
  壊れる罠を残さないため揃えた。
- **[3.173.0 と同型の need2 取り残し] `V6LateOperators.isBalanceable` が P1 需要しか見ない**:
  生 state の `need1` / `needDay1` のみを読み、**P2 だけで需要が定義されたシフトを「需要なし」と判定**して
  Chain 系の候補から丸ごと外していた。source of truth の `Problem.covUCell(bk, j, 0)`（誰も配置しない
  状態で不足が出るならその日は需要がある）へ委譲。
- **[wishLocked retrofit の第4サイト] `PersonalBalanceJointLnsPolish` が生の `wish >= 0` を6箇所で使用**:
  3.264.0（C1JointLns）・3.270.0（V6HotfixPasses ×15）・3.278.0（C1JointLns:326）に続く取り残し。
  とくに `staffLowerBound` は docstring 自身が「担当可否も使う」と書いているのに、希望の数え上げだけ
  `canDo` を見ておらず、**担当できないシフトへの希望まで「固定」として下限に織り込んで**いた。
  6箇所すべて `p.wishLocked` へ統一。
- **再現しなかった / 評価が異なる指摘（根拠つきで不採用）**:
  ①**C1JointLns の密DP 2^19 確保**＝現 main には `d>20 → cheap` と `dpCells > 262_144 → cheap` のガードが
  既にある（3.305.0）。窓長20なら 2^19 > 262144 で必ずフォールバックし密DPは確保されない＝
  **3.305.0 以前のコードを見ている**。②**「希望の意味が二重」**＝pref 9000 > covU 8000 で、希望を破って
  被覆を埋める手は `isBetter` が必ず却下する（1希望破りで直せる covU は最大1件＝+1000）。ロックは重みと
  整合した枝刈りであって二重定義ではない。③**制御器が HARD 床を知らない**＝事実だが
  `PolishGate.adaptiveEscapeControl` は**既定 OFF**で本番影響ゼロ。既定経路は残差を見ないのでこの問題
  自体を持たない。④**Collapse の交互が秒で偏る**＝3.307.0 で実測済み・配分是正は同節で否決済み。
  ⑤**`Cｳ` を含む制約行**＝手持ち3データセット（golden/real/user）では再現せず、記号は全て解決する。
  機構（①の無言除外）は実在するので上記のとおり修正した。
- **[HF77 で保留] c42 の自己ペア**: `MirrorCore:230` の `for (i in left) for (i2 in right)` に `i != i2` が
  無く、`g1==g2 && s1==s2` の行があると自己ペア n 件＋順序対の二重計上で n 人が n² 件発火する（c42s も同型）。
  **確認済みだが修正はスコア変更＝明示の数値指示があるまで変えない。**
- 検証: ホストJVM **全346テスト green**（344 + 新規2＝未定義記号の案内が出る／既知記号だけなら出さない）。

## 適応ポートフォリオの敵対フルコードトレース（3.308.2, ユーザー指示「敵対フルコードトレースする」）
`runAdaptivePortfolio` を設定 → epoch 頭 → 役割実行 → epoch 尾の順に行単位で追い、並行性・生存期間・
例外経路・デッドコードを狙った。**うち3件は 3.306.0〜3.308.1 で私自身が作った欠陥**。
- **[デッドコード撤去] `nextPlateauDepth` が本番から一度も呼ばれていなかった**: 3.306.0 で制御器経路用に
  足したが、実配線（`V6NativeOptimizer:689`）は `nextStagnantEpochs` のまま。**3.278.0 で `safetyFloor` を
  「計算されるだけで本番未読・テスト assert のみ」として撤去した先例があるのに、同型を再導入していた**。
  2関数は `Int.MAX_VALUE` ガード以外まったく同一で、そのガードも到達不能（エポック数は予算/量子で上限）。
  「役割変更でリセットしない」という性質は**関数ではなく呼び出し側**（制御器分岐が `stagnantEpochs = 0` を
  持たないこと）にあるため、関数を分けても契約は表現できていなかった → 撤去。
- **[テストが名前と違うものを検証していた] `plateauDepthIsNotResetByRoleChange`**: 名前に反し
  `nextPlateauDepth` の足し算しか見ておらず、役割変更に一度も触れていなかった（しかも対象が上記の
  デッドコード）。**実際の性質は制御器の出力の強度として観測できる**ので、そこを直接固定する
  `accumulatedDepthStillRaisesIntensityAfterTheRoleChanges`（深さ6で役割が変わっても、同じ役割を深さ0で
  組んだものより intensity がちょうど +3 になる）へ差し替えた。
- **[孤児コメント] 3.308.1 で私が作った**: `roleRuns.merge` を移動した跡に 3.282.0 のコメント
  （「集計はロールが実際に走ることが確定してから」）だけが残り、無関係な `val roleDeadline` の上に
  ぶら下がっていた。移動先の集計サイトへ集約。3.282.0 の意図自体は移動後も満たされている（merge は
  さらに後ろへ動いたため）。
- **[設計と実装の差・報告のみ] `PERSONAL_RSI` は6族のうち4族しか狙えない**: 制御器の PERSONAL 圧力は
  low/high/c2/apt/fair/weekly の6族で立つが、`adaptiveEpochStart` の PERSONAL_RSI 内部 focus 連鎖は
  apt → high → low → fair → `"total"` で、**c2 と weekly は選べず総花的な `total` へ落ちる**。
  focus 連鎖を広げると探索経路が変わる＝要 A/B だが、PORTFOLIO の run 間ばらつき（実測 200〜300）では
  この規模の差を示せない → **コードは変えず、決定表を実装どおりに訂正**した。
- **健全と確認（変更なし）**: `AdaptiveEliteArchive.register` は `@Synchronized`／`size`/`snapshot` は
  await 後の単一スレッド呼び出し ／ `nowMs()` は `System.nanoTime()` ベース＝単調（`nowMs()-epochT0` は
  壁時計の巻き戻りに影響されない）／`forceMaxDistanceKick`・`forceDiverseKick` はどちらも `wishLocked` と
  `allowedShiftsForStaff` を守る＝groupViol/pref を新たに作らない ／ `escapeController` と
  `controlledAssignment` は同一の判定から導出（`PolishGate` を2回読まない）／UI トグル3つとも
  `enabled = !ui.running` ／ 制御器経路で `controlledAssignment = next` は集計より後だが、集計が使う
  `assignment` は epoch 頭の `val` で別物＝役割の付け替え先を取り違えない。
- 検証: ホストJVM **全344テスト green**（1件撤去・1件追加）。

## 3.307.0/3.308.0 の敵対検証＝自分の主張3件を反証して修正（3.308.1, ユーザー指示「敵対検証する」）
直前2件（役割別worker秒のログ化・アップロード版の部分融合）の主張を1つずつ壊しに行き、**3件が実際に偽**と判明。
- **[偽1・修正] 「roleRuns と同じ母集団を秒でも数える」**: `roleRuns.merge` はエポック**冒頭**（quantum 判定の直後）、
  `roleMillis.merge` はエポック**末尾**にあった。間に `catch (e: Exception) { break }` があるため、例外で
  終わったエポックは回数には入るが秒には入らない。さらに `epoch++` も末尾なので、**旧来から
  `sum(roleRuns) > epochs`**（ログの角括弧の合計がエポック数と合わない）という既存の不整合もあった。
  → `roleRuns.merge` を `roleMillis.merge`・`epoch++` と**連続した3文**へ移動。分岐が挟まらないので
  `sum(roleRuns) == epochs == roleMillis の母集団` が構造的に成立。実データ8ワーカーで不一致0件を実測確認。
  併せて「`quantum<=0` と例外の break はここへ到達しない＝その回の摂動＋フル検査の時間は秒合計に入らない」
  ことを明記（8×300=2400 に対し実測 計2396s、8×60=480 に対し 477s ＝ちょうどこの尾で、数字が自己整合する）。
- **[偽2・修正] 「再配属分岐＝役割が必ず変わる」**: `assignmentFor(4, r)` は `r>=1` で**常に ELITE_RELINK**。
  W4 は再配属2回目以降 役割が変わらない（W1/2/3/5/6/7 は escapeRoles の index が1つ進むので必ず変わる）。
  `roleChanged = true` を渡しているのは**旧挙動（この分岐では常に基準量子へ戻す）の保存**が目的であって
  役割変更の主張ではない、と正直に書き直した。挙動は不変（旧 `improvedPrevious = false` と代数的に同値）。
  回帰テスト `defaultPathReassignmentDoesNotAlwaysChangeTheRole` で W4 の事実を固定（同じ誤解の再発防止）。
- **[偽3・修正] 決定表が「全分岐」を尽くしていなかった**: アップロード版 md の表をそのまま採ったが、実コードと
  1行ずつ突合したところ **`depth==0 && !collapsed` → 役割を変えず強度だけ基準へ戻す**（最も頻度の高い経路）と
  **どの残差にも当たらない → `LARGE_DESTROY_ALNS`** の2行が欠落。`firstDifferent` の同値フォールバックも未記載。
  3行を追加し、族名も実装どおりに展開（旧「c4」→ c41/c42/c41s/c42s、c3n も時系列側に入る）。
  **アップロード版の前提の誤りを批判しておきながら、その表の完全性を検証せずに採っていた**のは同じ種類の誤り。
- **[偽アラーム・製品は無傷] ログ行が途中で切れる**: 実測ログが `HARD_FAMILY_RSI=...` で切れており実装側の
  truncation を疑ったが、犯人は**計測用 probe 自身の `substringBefore(" / W0:")`**（読みやすさのため自分で
  入れたもの）。製品コードは完全な行を出す。ELITE_RELINK の欠落も、60秒予算では W4 が再配属前で0エポックなだけ。
  秒合計の端数（474 vs 計477）は表示の秒への切り捨て。**計測器を先に疑うのが正しかった**。
- 併せて台帳ヘッダの「3.303.0 時点のコードに一致」を 3.308.1 へ更新（自分で定めた stale 禁止に反していた）。
- 検証: ホストJVM **全344テスト green**（343 + 新規1）。実データで `sum(roleRuns) == epochs` を8ワーカー全数確認。

## アップロード版の優秀な部分を部分融合（3.308.0, ユーザー指示「優秀な部分を部分融合する」）
再アップロードされた3ファイルを HEAD と全差分し、**全面置換（制御器の無条件化＝3.306.0/3.307.0 で
実測否決済み）から独立して価値のある部分だけ**を抽出して融合。`StagnationEscapePressure`/
`StagnationEscapeController` の本体は 3.306.0 で取り込んだ版と**完全一致**（差分は既定経路の削除・
識別子リネーム・コメント書き換えのみ）と確認したうえで、以下4点を採った。
- **[実バグ修正・最重要] 役割変更直後の量子継承**: epoch 長は「直前が改善したか」で 5→8秒 /
  35→45秒 に伸びるが、**役割が変わった直後は引き継いではいけない**（新しい役割はまだ何も証明して
  いないのに、前の役割が稼いだ改善で長い量子を受け取る根拠が無い）。既定経路は再配属分岐で
  `improvedPrevious=false` として元からこの契約を守っていたが、**3.306.0 で私が足した制御器経路だけが
  `improvedThisEpoch` をそのまま引き継いでいた**（RSI+ 役なら 35秒のはずが 45秒になる）。
  アップロード版の `improvedThisEpoch && !roleChanged` はこの不整合を正しく直している。
  ただしインライン式のままだと再び片側だけずれるため、**契約に名前を付けて両経路から呼ぶ**形で融合:
  `AdaptiveHypothesisEpochPolicy.carriesImprovingQuantum(improvedThisEpoch, roleChanged)`。
  既定経路は代数的に恒等（`(x,true)=false`＝旧 `improvedPrevious=false`／`(x,false)=x`＝旧
  `improvedPrevious=improvedThisEpoch`）＝**既定の挙動は完全に不変**、直るのは制御器 ON のときだけ。
- **[防御] `intensityFor` の負値ガード**: `growthBasis.coerceAtLeast(0)`。現在の呼出元
  （reassignments / plateauDepth とも 0 始まりの単調増加）では到達しないが、丸めておく。
  併せて引数名を `reassignments` → `growthBasis` へ（既定経路は再配属回数・制御器経路は停滞深さを
  渡す＝どちらでもない中立な名前が正しい。アップロード版の `escapeDepth` は既定経路側で誤読を招く）。
- **[意図の明示] 初期配置**: `archive.register` 前の `assignmentFor(i, 0)` → `initialAssignmentFor(i)`
  （値は同一・名前が「初期配置」だと分かる）。
- **[文書] 制御器の決定表を台帳へ**: アップロード版 md が持っていて当方が持っていなかった資産。
  「観測→次の手」の7行表（W0固定・初期分散・HARD残差→HARD_FAMILY・時系列残差→DAY_BLOCK・
  個人回数残差→PERSONAL・peer有→ELITE_RELINK・距離2以下→MAX_DISTANCE と LARGE_DESTROY を交互）を
  `docs/algorithm_portfolio.md` へ収録。**ただし前提は訂正**＝アップロード版は無条件稼働として本文に
  書いているが実際は `PolishGate.adaptiveEscapeControl` 既定 OFF（台帳自身の規律「本文は実装済みの
  事実だけ」に反する）。「実装済みだが既定 OFF」節の配下に置き、**3.306.0 で記載漏れだった
  `adaptiveEscapeControl` の行自体も同時に追加**（台帳のギャップ修正）。量子継承の契約も明文化。
- **採らなかったもの**: ①制御器の無条件化＝3.306.0 の A/B（実データ3件×4回）で有意差を検出できず
  ユーザー選択で既定 OFF 温存と決定済み ②`shouldReassign`/`nextStagnantEpochs` の削除＝既定経路を
  消すことと同義 ③`lastRole = assignment.role` への単純化＝HEAD の二経路構造では現行式が正しい。
- **アップロード版の欠陥（3.306.0 から未修正のまま再送）**: `assignmentFor` 直後の**余分な `}`**（object が
  そこで閉じる＝コンパイル不能）。今回も融合時に除いた。
- 検証: ホストJVM **全343テスト green**（340 + 新規3＝役割変更で改善量子を継承しない／その契約が
  `quantumSeconds` の 45秒→35秒として実際に効く／負の成長基準を 0 へ丸める）。既定経路の実データ結果は
  PORTFOLIO の run 間ばらつき（既知 200〜300）の範囲内で、上記の代数的恒等により挙動は不変。

## 役割別worker秒のログ化＋秒予算再配分の否決（3.307.0, ユーザー指示「あなたが賢く考える」）
ユーザーから 300秒×8ワーカー＝2,400 worker秒 を役割ごとに固定配分する設計（DayBlock 605s / Elite 575s /
Large 320s / Max 295s / Personal 200s / Baseline 335s / HARD系 各35s ＋ワーカー別時間割＋運用ルール3件）
の提案を受領。動機は「DayBlock と Personal が 5秒量子で飢えている」。**前提を測ってから採否を決めた。**
- **[ログ強化・採用] `roleMillis`（役割別の実消費ミリ秒）**: `roleRuns`（エポック数）はあったが**秒が無く**、
  「どの役割が予算のどれだけを実際に食ったか」を実機ログから読めなかった（量子は 5/8 秒(ALNS/RSI) と
  35/45 秒(RSI++) で 7〜9 倍違い、かつロールは締切・HARD=0・内部早期終了で量子より早く戻る）。
  エポック境界の摂動・検査・距離計算まで含めた実測を `AdaptivePortfolio` 要約へ2形式で出す＝
  ワーカー別（`DAY_BLOCK_ALNSx5/83s` のように従来の回数表記へ秒を併記）と**全ワーカー横断の
  「役割別worker秒(計Ns): ROLE=Ns(P%) …」**。読取専用・スコアリング不変。
- **[前提の確認] 偏りは実在し、提案が述べるより大きい**（real_state 300s/8ワーカー・計2396 worker秒）:
  MAX_DISTANCE 664s(27%) / HARD_DEBT 582s(24%) / BASELINE 424s(17%) / ELITE 174s(7%) /
  LARGE 154s(6%) / **DAY_BLOCK 151s(6%)** / **PERSONAL 138s(5%)** / HARD_FAMILY 105s(4%)。
  原因は構造的＝**役割の巡回は「エポック回数」で均等なのに費用の単位は秒**。W3 は HARD_DEBT が
  18エポック中5回(28%)で 188s/300s(63%) を消費、W7 は MAX_DISTANCE が225s(75%)。
  前ターンに私が書いた「量子初期値 ≠ 実消費。実消費を測れ」という留保は、測ったら**提案側に有利**だった。
- **[否決] 予算再配分は品質を上げない**。固定表でなく原因側を直す版を実装して A/B した:
  ①そのワーカー自身の HF63 が「残る HARD 族はすべて充足困難」と学習済みなら HARD 狙いの役割
  （HARD_FAMILY/HARD_DEBT）を配らない ②代わりに**実消費秒**が最少の脱出役割を選ぶ。
  ログは全ワーカーが `HF63回避=C3n+CovU` を学習済みなのに役割割当がそれを一度も読んでいないことを
  示しており、狙いは正しかった。実際 予算は動いた（HARD_DEBT 24%→12% / HARD_FAMILY 4%→2% /
  DAY_BLOCK 6%→10% / LARGE 6%→9% / PERSONAL 5%→8%・HARD役振替24回）。
  **しかし 3データ×3シード×ON/OFF の18実行で、9ペア中 OFF が6勝・ON が3勝**
  （real 3/3 OFF・golden 2/3 ON・user 2/3 OFF、weighted 合計 OFF 202449 vs ON 203312＝OFF が0.43%良い。
  c1 は user で 3/3 ON が悪化）。符号検定 6:3 は有意でないが **ON を支持する証拠はどこにも無い**。
- **なぜ効かないか（測定が示す理由）**: ON 実行で archive が 25→43＝**エポックが18回増えた**。
  長い量子を持つ RSI++ 役は「HARD を追っている」のではなく seed→仮説→refine→研磨のフルパイプラインを
  回す＝HARD が詰んでいてもソフト研磨をしている。それを 5〜8秒の短エポックに置き換えると、
  エポック境界の固定費（摂動＋フル検査＋距離計算）を18回余分に払うだけになる。
  **長い量子は無駄ではなく、秒あたりの仕事量が多い役に付いている。** 提案の固定表は同じ方向へ
  さらに強く振る（DayBlock 6%→25%、実験版は10%）ので、方向が誤りなら悪化幅は大きくなる（これは推論）。
- **[反証されたコードは残さない]** 実験の3要素（`PolishGate.secondsBalancedRoles`・
  `hardChasingRoles`/`hardRolesExhausted`/`leastUsedEscapeRole`・振替の配線）と専用テストは**全て削除**。
  既定OFFトグルとして温存しない＝3.306.0 は「差を検出できなかった（有益さ不明）」だったのに対し、
  今回は「一貫して支持されない（有益でない）」で性質が違う。残すのは計測手段（`roleMillis`）だけ。
- **運用ルール3件も採らない**: ①「35秒でHARDが下がらなければ打ち切る」＝HF63 学習と ForbiddenDiag の
  構造壁判定（3.281.0）が既に担う。固定時間の閾値は別データでまだ下がる HARD を切る
  ②「MAX_DISTANCE を連続させない」＝3.306.0 の `StagnationEscapeController` DISTINCT_PEER 圧力と
  同趣旨で既定OFFで温存済み ③「終盤60秒は研磨優先」＝後処理予約枠は現状でも使い切られていない（3.102.0）。
- **[アップロード版の欠陥（未修正のまま再送されている）]** `AdaptiveHypothesisEpochPolicy.kt` の
  `assignmentFor` 直後に**余分な `}`** があり object がそこで閉じる＝コンパイル不能（3.306.0 で
  ローカル修正済み）。`algorithm_portfolio.md` は `StagnationEscapeController` を本文に無条件稼働として
  記載しているが、実際は `PolishGate.adaptiveEscapeControl` の既定OFF＝台帳自身の規律
  「本文は実装済みの事実だけ」に反する。
- 検証: ホストJVM **全340テスト green**（3.306.0 と同数＝実験コードが完全に消えていることの確認も兼ねる）。
  docs/algorithm_portfolio.md に「適応ポートフォリオの時間配分（実測値）」と
  **「実測で否決した提案（再提案しない）」**節を新設し、規律7（否決はコードを残さず根拠つきで記録・
  前提の確認と採否は分けて書く）を追加。

## 適応ポートフォリオの停滞脱出を既定OFFのトグルで温存（3.306.0, ユーザー選択「b」）
ユーザーから3ファイル（台帳・`V6NativeOptimizer.kt`・`AdaptiveHypothesisEpochPolicy.kt`）を受領し
「高精度化できる確率が向上するか?」。実測したうえで**(a)見送る／(b)既定OFFのトグルで温存**を提示し、
ユーザーが (b) を選択。
- **提示版の内容**: 各ワーカーの次の役割を「再配属の回数」でなく **いま残っている違反の種類
  （HARD／時系列／個人）と他仮説との距離**から選ぶ `StagnationEscapeController` を新設し、
  停滞の深さ(`plateauDepth`)を役割変更でもリセットしない。既定経路は盤面を一切見ず
  `escapeRoles[(offset + reassignments) % 6]` で機械的に巡回し、再配属のたびに停滞カウンタを 0 へ戻す。
  **指摘としては正しい**——深さのリセットは 3.282.0 で `improvedThisEpoch` 恒真化を直したときに
  私が残していた同型の穴で、現行は実質2エポックに1回しか役割が変わらない。
- **PORTFOLIO A/B（ホストJVM・120s・workers=8・実データ3件×各4回）＝有意差を検出できず**:

  | データ | build | weighted (min/中央/max) | c1 |
  |---|---|---|---|
  | golden | HEAD | 2159 / 2289 / 2366 | 85,94,96,98 |
  | golden | NEW | 2187 / 2215 / 2333 | **93,95,99,101** |
  | real | HEAD | 32591 / 32688 / 32775 | 69,72,78,78 |
  | real | NEW | 32491 / 32609 / **32786** | 55,63,76,84 |
  | user | HEAD | 32621 / **32707** / 32845 | 70,70,73,76 |
  | user | NEW | 32527 / **32829** / 32845 | 59,70,70,73 |

  3データセットとも範囲が**完全に重なり**、中央値の大小も一貫しない（golden=新が良い／real=新が良い／
  user=新が悪い）。c1 は golden で**新のほうが悪い**。
  **[重要な自己訂正] 2サンプル時点では「real は範囲が重ならない＝NEW 有利」と報告したが、4サンプルで
  覆った**（NEW の max 32786 が HEAD の max 32775 を超える）。3.290.0 で自分が記録した「1回計測で A/B
  判定してはならない」を、2回でやりかけた。
- **測定系の限界**: PORTFOLIO は epoch が壁時計ベースで、**seed を固定しても run ごとに大きく振れる**
  （実測の幅: golden HEAD 207・real NEW 295）。仮に真の効果が 100 程度あってもこの分散の中で有意に
  示すには n=30 前後＝約6時間の反復が要る。現実的でない。
- **採用形（(b)）**: 提示版で置き換えず、**旧 API を全部残したまま新経路を足す**。
  `AdaptiveHypothesisEpochPolicy` は既存（`assignmentFor(Int,Int)` / `shouldReassign` /
  `nextStagnantEpochs`）を無変更のまま、`assignmentFor(Role,Int)` / `initialAssignmentFor` /
  `StagnationEscapePressure` / `StagnationEscapeController` を追加（同時に足した `nextPlateauDepth` は
  本番から呼ばれないデッドコードだったため 3.308.2 で撤去）。
  `V6NativeOptimizer` の epoch ループは1箇所で分岐し、`PolishGate.adaptiveEscapeControl`（既定 false）が
  真のときだけ新経路。**既定 OFF なら現行と完全に同一経路**。
  提示版がそのままだと壊していた `HypothesisEpochPolicyTest` の4テストは無傷のまま green。
- **提示版から直した2点**: ①`assignmentFor` の式本体直後に**余分な `}`** があり object がそこで閉じる
  ＝**コンパイルエラー**（3.290.0 の `"[$lensLabel日]"` と同型の「1文字でビルドが落ちる」）
  ②`archive.register` が役割を `reassignments` から逆算しており新経路では実際の役割と一致しない
  → `AdaptiveWorkerOutcome.lastRole` を追加（提示版と同じ対処）。
- **[自分で作って直したテストの誤り]** 新テストに「深さが増えると強度は単調非減少」と書いたが失敗した。
  `intensityFor = base(role) + depth/2` で base は役割ごとに 0〜3 と違うため、役割が変わると強度は
  下がりうる。単調性は同一役割内でしか成立しない。「同一役割内の単調性」と「深い停滞で複数役割を巡ること」の
  2つに分けて固定し直した。
- **UI 配線**: `UiState.adaptiveEscape` ／ `MagiViewModel.setAdaptiveEscape` ／ 設定タブ→詳細設定の
  トグル（3.298.0/3.304.0 と同型）。ON のとき説明文が警告色になり「試した範囲では差が出ませんでした」と
  正直に出す。
- 検証: ホストJVM **全340テスト green**（新規7件＝W0固定・残差で役割が決まる・同一役割内の強度昇圧・
  改善時の役割継続と昇圧解除・collapse時の必ず別役割・深さのリセット規則・既定OFFの明示）。

## staffPacked の重みドリフトと比較順序を修正（3.305.0, 外部提示コードを検証のうえ採用）
ユーザーから3ファイル（`V6HotfixPasses.kt` / `C1JointLnsPolish.kt` / `C3nBitScan.kt`）を受領し
「参考にできますか？」。receiving-code-review の規律どおり現行と diff を取り、1件ずつ実コードへ照合した。
**現行側に実バグが2件見つかり、提示版はどちらも直していた**ため採用した。
- **[実バグ1・重みドリフト] `staffPacked` が古い重みを使っていた**: `MirrorKeys` は **c1=15.0 / c3mn=15.0**
  なのに、この関数だけ `wgt += 4`（c1）・`c3mnC * 12` とハードコードされたまま。3.249.0（c1 4→5・c3mn 12→15）と
  3.253.0（c1 5→15）の HF77 重み変更が**この1関数だけ取り残されていた**。3.287.0 で `docs/business-logic.md` の
  同種ドリフトは直したが、コード側のこのサイトは grep から漏れていた（比較器でも評価器でもなく「パックした Long を
  返す関数」のため）。提示版は `MirrorKeys.weightOf("c1")` 等へ置換＝**単一ソース化**でドリフトが構造的に起きなくなる。
- **[実バグ2・比較順序] `staffPacked` のパックが total 優先だった**: `hard*1e15 + total*1e9 + wgt` という
  エンコードは辞書式 **hard→total→weighted** で、3.287.0 で全サイト統一した **hard→weightedScore→total** と逆。
  3.289.0 の「keep-best統一の取り残し」棚卸しでもこのサイトは見つかっていなかった。提示版は `StaffObjective`
  （hard/weighted/total の構造体＋`isBetterThan`）へ置換し統一順序に一致させる。副次的に、パック方式が暗黙に
  依存していた「各桁が繰り上がらない」前提も消える。
  **影響範囲**: 呼出は `applyBlockRotationPolish` の事前フィルタ4箇所のみ（2者ブロック交換・3者回転）。最終採否は
  checker + isBetter が担うため**誤った勤務表は出ない**が、3.287.0 で採用するようにした「weighted 改善・total 増」の
  候補をこの前フィルタが先に捨てていた＝機会損失。
- **[効率] `C1JointLnsPolish.generateMoves` に c3n 事前フィルタ**: 全 Move 種の共通効果「i の day j に x を置く」が
  その時点で禁止連続を作るなら goal 自体を諦める。正しさは従来どおり debt＋最終ゲートが担保するが、hard debt を
  食うだけの候補で `maxMovesPerGoal` の枠が埋まるのを防ぐ。
- **[防御] `C1JointLnsPolish` の下界 DP メモリ**: `Array(p.T+1)` → `Array(hi+1)`（cnt>hi は捨てられるので正しい縮小）＋
  `dpCells > 262144` で `cheapSingleRuleLowerBound` へ退避。現行は d=20 で dp/next 合わせて 134MB を確保しうる
  （32 × 2^19 × 4byte × 2）。実データの cons1 は d=5/14/15 なので到達しないが、Android の OOM 防御として正当。
  cheap 版は「月間上限 < 窓必要回数なら全窓違反」「希望固定で物理的に届かない窓を数える」の保守的下界＝過大評価しない。
- **[防御] `C3nRowScan` 新設**: 3.303.0 の私の実装は `usable(p)` を確認せず `buildRowMask` を呼んでいた。
  T>64 では `1L shl j` が j%64 へ折り返し**マスクが壊れる**（枝刈りが誤るだけで checker が守るため誤った勤務表は
  出ないが、正しい候補を落とす）。提示版は `buildRowMask` に `require(usable)` を置き、呼び出し側は
  `C3nRowScan`（64日以内=popcount / 65日以上=スカラー）経由にして分岐自体を無くす。**3.304.0 で記録した
  「期間は最大1か月」の前提では到達しない**が、呼び出し側が分岐を忘れる事故を構造的に防ぐ設計として採る。
- **検証（実データ3件・後処理研磨のみ・同一seed）**: **最終盤面が HEAD と完全一致**
  （golden 2469/306/c1 104・real 49231/178/c1 58・user 33167/170/c1 54）。正しさを変えずにドリフトと無駄だけを
  落としている。内部効率は C1JointLNS の候補数が golden で **54,750→35,654（35%減）**。
  ホストJVM **全332テスト green**。C++ は無変更＝native parity 影響なし。

## 禁止連続の崩し範囲を設定トグルへ配線＋A/B実測（3.304.0, ユーザー指示「接続する。配線する。仮想的テストする。ABテストを仮想的にする」）
3.303.0 で入れた `PolishGate.wideC3nBreakDays` は**定義があるだけで誰も切り替えられない**状態だった
（3.298.0 の `filterC3nIncrease` が「実装済みだが未配線」だったのと同型）。3層を同じ形で配線し、
トグルが実際に挙動を切り替えることを実データ A/B で確認した。**表示・設定の配線のみ＝重み・採否・
探索ロジックは完全に不変**（既定 OFF も維持＝配線前と挙動同一）。
- **配線**: `UiState.wideC3nBreak`（既定 false）／`MagiViewModel.setWideC3nBreak`（`setBlockSwapC3nFilter` と
  同型＝ゲートと UiState を同時更新＋操作ログ）／設定タブ→詳細設定（上級者向け）の
  `OptimizationTuningSection` にトグル。ON のとき説明文が警告色になり「できあがる勤務表が良くなるとは
  限らない（データによっては悪くなる）」と明示する。
- **A/B実測（ホストJVM・後処理研磨のみ・同一seed・トグルが触るのと同じフラグを直接駆動）**:

  | データ | gate | hard | total | weighted | c1 | c3n | covU |
  |---|---|---|---|---|---|---|---|
  | golden | OFF / ON | 0 / 0 | 306 / 306 | 2469 / 2469 | 104 / 104 | 0 / 0 | 0 / 0 |
  | real | OFF / ON | 6 / 6 | 178 / 179 | 49231 / **47547** | 58 / 73 | 0 / **2** | **6 / 4** |
  | user | OFF / ON | 4 / 4 | 170 / 173 | 33167 / **33240** | 54 / 55 | 0 / 0 | 4 / 4 |

- **[新事実] real の内訳が数値で確定した**: 3.303.0 では「covU を c3n へ付け替えている」と推測していたが、
  A/B で族別に並べたところ **covU 6→4・c3n 0→2** と明確に出た。weighted の差も
  `8000×2 − 7000×2 − 15×15 ≈ −1674`（実測 −1684）でほぼ一致する。つまり ON は「人員不足2件を
  禁止連続2件へ交換する」挙動で、weighted 上は改善（covU のほうが重い）だが業務的な良し悪しは別問題。
  golden は完全中立、user は素直に悪化。**利得が一貫しない結論は 3.303.0 のまま＝既定 OFF を維持**。
- **配線したことの価値**: 「人員不足を減らしたい」データ（real 型）では ON という選択肢が生まれた。
  配線前は定義があるだけで実機から一切試せなかった。既定は OFF のままなので通常運用は無変更。
- 検証: ホストJVM **全332テスト green**（v6層は無変更＝テスト不変）。UI層は Android 依存でホスト
  コンパイル不可＝ブレース/丸括弧均衡（HEAD と同一）・呼び出し側シグネチャ一致・新シンボルの参照を静的確認。

## 禁止連続をパターン全域で崩す＋Polish系の水平展開（3.303.0, ユーザー指示「C3nは前後日と当日も他の勤務シフトに変更できるようにアルゴリズムを賢く昇華する」「各制約のPolish系を相互に水平展開する」）
AskUserQuestion で「両方（範囲拡張＋当日も可変）」を選択。実データの cons3n は `Dﾃ→B4` 等の2連11本に
加えて **`Dﾃ→休→A4` の3連が2本**あり、違反が末尾に立つとパターンの先頭は2日前にある。既存機構は
「当日1セル」か「隣接日 j±1」しか触っておらず、**3連の先頭に構造的に届いていなかった**。
- **`C3nBitScan` 新設（ビット演算）**: 職員行を「シフト→日ビット集合」へ畳み、禁止連続の完全一致窓を
  AND＋シフト＋popcount で数える（3.174.0 で C++ 側の窓マッチを popcount 化したのと同型の Kotlin 版）。
  候補が (パターン長 × 担当可能シフト数) 倍に増えるため、フル checker の前段に安い枝刈りを置く必要が
  あった。**既存のスカラー実装は一切置き換えない**（`makesForbiddenRun` は全経路が依存するオラクルとして
  温存）。ランダム盤面の**全単一セル変更 2,520 件でスカラーとパリティ一致**を固定（`C3nBitScanTest`）。
- **`applyC3nPolish` 新設**: c3n(HARD) 専用の研磨パス。C3mnPolish と同型だが、候補セルが違反セル1つでなく
  **違反パターンがまたぐ全日（前日・当日・翌日）**。禁止連続は「並び」なのでどの1日を崩しても壊れる。
  ビット枝刈りで「c3n の正味 fire が減らない手」を checker 前に落とし、崩した先の被覆欠けは
  `findCovUChain` の玉突きで埋める。採否は checker + isBetter + exactPinRegression。
- **[実測で既定 OFF にした] 範囲拡張 `breakableDaysFor`**: `tryFixForbiddenRunViaAdjacentDay` の崩し先を
  j±1 固定 → パターン全域へ広げる一般化。**正しい一般化だが実データ3件で利得が一貫しなかった**ため
  `PolishGate.wideC3nBreakDays`（既定 false）へ隔離した。切り分け実測（各3回・すべて決定的）:
  - **C3nPolish 単体は3データセットとも HEAD と完全一致**（golden 2469 / real 49221 / user 33167）＝無害。
  - 範囲拡張を足すと golden=中立 / **real=weighted 49221→47547** だが内訳は **covU 2件を c3n 2件へ
    付け替え・c1 +14** / **user=weighted 33167→33240 と悪化**（total 170→173）。
  - 個々の手は keep-best で退化しないが、候補が増えると探索の経路が変わり着地する局所解がデータ次第で
    良くも悪くもなる。2.55.0（戦略的振動）・3.94.0（in-loop レバー）と同じ「安全≠有益」の再確認。
    計測が支持しない既定変更はしない、という本リポジトリの規律どおり既定 OFF で温存する。
- **[水平展開] 不採用の主因を全 Polish パスへ**: 3.302.0 で C1Polish / RangePolish に入れた主因表示を、
  構造の異なる残り6パス（C3mn/C3n/C3Run/C3Pattern/Apt/Fair）へ `RejectCulpritStats`（パス単位の集計・
  `AdaptiveBlockSwap` と同じ粒度）で展開。golden_state で全パスが一斉に出力した結果、**low/high(90/45) と
  c1(15) が族を問わない共通の壁**だと判明した:
  `C3mn 不採用125(low:62 c1:35 high:28)` / `C3Run 51(c1:20 low:20 high:11)` / `C3Pattern 52(c1:24 low:17 high:10)` /
  `Apt 74(high:39 low:27 c1:8)` / `Fair 49(c1:24 low:19 high:3)`。c1 単独の問題ではなく、**個人回数の
  厳密ピン群がソフト研磨全体を縛っている**という前ターンの推測が全族で裏づけられた。
- 検証: ホストJVM **全332テスト green**（新規5件）。実データ3件で **FINAL が HEAD と完全一致**
  （gate OFF＝挙動不変）かつ新ログが全パスで出力されることを確認。読み取り専用の追加とゲート隔離のみ＝
  スコアリング不変。

## 研磨の「不採用」に主因の族名を併記（3.302.0, ユーザー指示「ログ強化する」）
実機ログの c1 残存が **「不採用×65 / 候補なし×4」** ＝ほぼ全部が「手はあるが目的関数が拒否」だったのに、
**何に負けたのかがログから読めなかった**（3.222.0/3.236.0 の頭打ち理由は「候補なし／不採用」の2分類まで）。
`AdaptiveBlockSwap`（3.293.0）は既に「悪化の主因 c3n:41 c3:4」まで出しているので、その計算を共通化して
C1Polish / RangePolish / C1JointLNS へ横展開した。**読み取り専用・スコアリング不変**（採否ロジック・重み・
探索は完全に不変。捨てた候補の内訳を数えて表示するだけ）。
- **`worstWorsenedFamily(after, before)` を新設**（`V6SearchOperators.kt`）: 重み付き（`MirrorKeys.weightOf`）で
  最も増えた族を返す。件数でなく重みで測るのが肝（c1=15 が1件増えるより low=90 が1件増えるほうが主因）。
  `AdaptiveBlockSwap` の同ロジックはこのヘルパーへ委譲（DRY化・挙動同一）。
- **C1Polish / RangePolish**: `recordBlock` に `after`/`before` を渡せるようにし、「不採用」のときだけ
  主因族を上位2件まで `残存:` へ併記（例 `モニカ 休(不採用×56 主因 low:53 c1:3)`）。
  あわせて **ピン破り（`exactPinRegression` による却下）を「不採用」から分離**した。ピン破りは違反自体は
  改善しているので主因族を持たず、同じラベルに混ぜると主因の件数が理由の件数と合わず読み手を混乱させる
  （`AdaptiveBlockSwap` は元から別ラベル＝分類が3パスで揃った）。
- **C1JointLNS**: `debt除外N` を **どの予算で切られたか（必須／合計／c1）** に3分割し、必須超過のときは
  主因族も併記（判定順は既存の if と同一＝必須→合計→c1）。
- **実データ3件で実測（後処理研磨のみ・ホストJVM）＝推測が族名レベルで確定**:
  - **休の窓が解けない主因は `low`/`high`（個人回数）**: `モニカ 休(不採用×62 主因 low:56 high:6)` /
    `上條洋平 休(不採用×48 主因 low:42 covU:3)` / `山本昌幸 休(不採用×50 主因 low:40 high:5)`。
    休が 10名中9名 lo==hi=10 の厳密ピンで固定されているデータと整合する。
  - **Dﾃ の窓が解けない主因は `c3n`（禁止連続）**: `金沢勇輝 Dﾃ(不採用×23 主因 c3n:23)` /
    `佐藤直美 Dﾃ(不採用×27 主因 c3n:24 low:3)`。
  - **C1JointLNS の必須debt は3データセットとも 100% `covU`**（golden 3427/3427・real 2815/2815・
    user 2645/2645）。ただし debt除外の**過半は「合計」予算**（golden 16048/23385・real 19697/24247）＝
    c1 を下げる手が他族の件数を `totalDebt` 超に増やして切られている、という別の壁も可視化された。
- 検証: ホストJVM **全327テスト green**（新規2件=`worstWorsenedFamily` が重み最大の族を返すこと・
  悪化が無ければ null）。実データ3件（golden_state / real_state / user_state）で新ログが実際に出力されることを
  確認済み。初版は `c1$debtC1` が `c12010` と繋がって読めなかったため出力前に修正（Kotlin の文字列展開で
  英数字の識別子が数値と連結する事故）。

## 3.301.0 の論理的検証＝休の判定が旧実装で死んでいたことが判明（3.301.1）
ユーザー指示「現状を論理的に検証する」。3.301.0（目標カードの検算）を実データ3件（user_state /
golden_state / real_state）にホストJVMで通し、`aptBalances` と設定ミス診断（検査6-C）の突合を実施。
- **単一ソース化は正しく効いている**: golden_state で `aptBalances` が「Dﾃ 目標合計35 / 上限31 → 4回届かない」を
  返し、診断も同じ1件を出す。user_state / real_state はどちらも0件で両者一致。実機ログで見た
  「Dﾃ 35 > 31」は golden_state 相当のデータで、user_state（Dﾃ に目標未設定の別月）と食い違って見えたのは
  データが別なだけで矛盾ではない、と確認できた。
- **[潜在バグの発見] 休の判定が旧実装ではほとんど実行されていなかった**: 旧・検査6-C は
  「必要人数が1日でも設定されているシフト」のループ内にあり、その `if (!hasDemand) continue` を
  休も通っていた。休に必要人数を設定するのは通常運用では稀（golden_state は実際に**休の必要人数設定日=0**）で、
  3.235.0 で入れた休向け restCapacity 比較はそのゲートに阻まれて**一度も走らないデータが存在した**。
  3.301.0 で休を必要人数の参照から切り離した結果、副作用としてこの穴が塞がっている（3データセットとも
  休は充足しており出力は不変＝退行なし）。意図した修正ではなかったため経緯をコードのKDocに明記した。
  通常シフト側のゲートは維持（勤務シフトの必要人数未設定は「まだ設定していない」だけの可能性が高く、
  上限0とみなすと 3.235.0 で指摘された誤検知と同型になる）。
- **[是正] 検査6-C だけが引数 `p` を無視していた**: `buildGuidance(state, p)` は Problem を引数で受けるのに、
  委譲先の `aptBalances(state)` は `cachedProblem(state)` を引き直していた。本番・テストとも p は state 由来
  （Problem は state の純粋関数）で実害は無いが、呼び出し元が別の Problem を渡すと 6-C だけ別物を見る。
  `aptBalances(state, p = cachedProblem(state))` へ変更し `buildGuidance` から p を渡す。実データ3件で
  出力がバイト一致することを確認済み。
- **据え置き（保守的判断として記録）**: ①休の capacity は休自身の個人上限(rangeHi)を見ておらず実際より
  大きく見積もる＝検出漏れ側に倒れるだけで誤検知は増えない ②必要人数が1日も無い勤務シフト（golden_state の
  B4 = 目標8・必要人数0日）は対象外のまま＝目標を満たすと covO が確定するが、上限0との比較は誤検知になるため
  警告しない。
- 検証: ホストJVM **全325テスト green**（不変）。実データ3件の `aptBalances` 出力も変更前と一致。

## アルゴリズム台帳の新設と未実施2件の実施（3.299.0 / 3.300.0, ユーザー提示の台帳案→「両方」）
ユーザーが `algorithm_portfolio.md` 案（同一内容を3回アップロード）を提示。全項目をコードと突合したところ
**約80%は事実と一致**していたが、**未実施の提案が2件「廃止・統合済み」として完了形で書かれていた**。
完了形の未実施項目は読み手がコードを確認せずに信じるため最も危険＝この点を最優先で分離した。
- **3.299.0（docs のみ）**: `docs/algorithm_portfolio.md` を新設し README 目次へ追加。事実に合わせた修正=
  ①**入口を3→4種類**（初期解生成 `handleSmartInitial`→`SmartInitialScheduler` / `handleSimple`→
  `GreedyMirrorScheduler` が漏れていた。`handleCheck` は評価のみ＝入口でない旨も明記）
  ②後処理を「前段(1回)→巡回ループ(最大4巡)→後段(1回)」の実コード順で記載。とくに**長距離交換は Range より
  後・Apt/Fair より前**という実際の位置を明示（案は「個人回数の後」と一括りにしていた）
  ③未記載だった横断機構を追加（`CombinatorialRepair` 5パス配線・`PolishGate`・`exactPinRegression`・
  `EliteIntegrationPolish` は `runPostOptimization` の**手前**・`HF70`）
  ④「廃止・統合済み」へ `BeamC1PolishV2`/`C1TemporalSwapPolish`（3.254.0 で削除済み）を追加
  ⑤未実施2件を**「未実施の提案」節へ隔離**。台帳の規律「書くのは実装済みの事実だけ・構想は隔離」を冒頭と末尾に明記。
- **3.300.0（コード）**: 隔離した2件を実施。
  - **旧 `applyBlockSwapPolish`（同一グループ×15日固定）を定義ごと削除**（77行）。本番パイプラインからは
    3.290.0 で既に外れており、残っていたのは定義とテスト4箇所のみ。`BlockSwapPolishTest`(3件)を削除し、
    `AdaptiveBlockSwapPolishTest` の新旧対照アサーションを撤去（同一グループのペアが無い盤面＝旧パスは
    手を作れなかった、という事実はコメントで保存）。
  - **C3 3者ブロック回転を「停滞時・最終巡のみ」へ格下げ**。**格下げの前に ablation を実測**（3データセットで
    完全に外して実行）し、**採用0かつ結果がバイト一致**＝通常時の寄与ゼロを確認してから実施した
    （C1 用の同じ回転を 3.254.0 で撤去したのと同じ根拠。C3 側は未測定だったため今回測った）。撤去はせず
    `rC3.applied == 0 || round == maxRounds - 1` のゲートに限定＝別のデータ形状で主手が詰まる局面には従来どおり効く。
    c3 違反が無ければ `applyBlockRotationPolish` 自身がアンカー0で即 return するため追加コストなし。
  - 実測（後処理研磨のみ・pin-regressions は3件とも0）: golden 2469/306/c1 104（不変）／real 49232/179→
    **49231/178**／user 33167/170/c1 54（不変）。real の −1 は JointLNS の壁時計由来の経路依存
    （ablation では 49232/179 だった＝格下げの効果ではない）。ホストJVM **全322テスト green**
    （325 − 削除した BlockSwapPolishTest 3件）。

## 目標（適切回数）の検算を設定画面へ＝オペレーターの思考誘導（3.301.0, ユーザー指示「オペレーターの思考を誘導してください」）
実機ログで apt 29件（全ソフト207件の14%）が出ており、その主因は**設定値の破綻**（Dﾃ の目標合計35 > 必要数31・
桒澤美幸 B4 の目標1 vs 強制最低20回）だった。設定ミス診断（検査6-C）はこれを正しく検出しているが、
**出口がホーム/分析タブの `SettingIssuesCard` しかなく、目標を入力している画面には届いていなかった**。
実際ユーザー自身が「適切回数は必要か？下限と上限だけで大丈夫では？」と迷った＝画面が判断材料を出せていない。
**表示・導線のみ＝重み・スコアリング・エンジンは完全に不変**。
- **`V6SanityPort.aptBalances(state)` を新設（単一ソース）**: シフトごとの「目標(apt)の合計」と
  「それを受け止められる上限」（通常＝必要数の合計 seatsHi / 休＝各職員が他シフトの個人下限を満たした
  うえで最大何日休めるかの合計）を返す。**盤面を一切参照しない**ので勤務表を作る前でも計算できる。
  検査6-C はこの関数を呼ぶ形へリファクタし、**設定画面と診断が同じ判定を読む**ようにした
  （二重実装だと「診断は警告するのに設定画面は何も言わない」ズレが再発するため）。
- **`AptCard` にインライン検算**: 超過しているシフトだけ errorContainer で
  「Dﾃ：目標の合計 35回 に対し、必要人数の合計は 31回。4回ぶんは必ず届きません。」＋
  「→ Dﾃ の目標を下げるか、必要人数を増やしてください。」を表示。ステッパーを押した瞬間に気づける。
- **なぜ `ui.settingIssues` を流用しなかったか**: `settingIssues` は `makeUi` 経由でのみ更新され、
  `refreshCheck` が `currentSchedule ?: return` で早期returnする。**スクショの「未計算」状態＝まさに
  設定中は更新されない**。`aptBalances` は盤面不要なので `remember(ui.editRev, ui.structureEdited)` で
  編集のたびに再計算される。
- **apt を廃止して low/high に寄せる案は不採用**（ユーザー質問への回答として検討）: apt 違反7件は全て
  staffRange の範囲内に目標がある（例「回数4 目標5 下限4 上限5」）＝low/high では表現できない。かつ
  staffRange を狭めて代替すると同じ過大設定が重み90/45で効き、weightedScore 寄与が 4 → 360 に悪化する。
  **apt が柔らかい（重み1）おかげで過大設定のダメージが小さく済んでいる**。決定記録 D3（apt/weekly/fair の
  重みは業務レビュー済で現状維持）とも整合。直すべきは機能でなく設定値。
- 検証: ホストJVM **全325テスト green**（新規3件=検算値と診断本文の一致・収まっていれば無警告・目標未設定の
  シフトは行を出さない）。実データ user_state で `aptBalances` と設定ミス診断がともに0件で一致することも確認。
  UI層はホストでコンパイル不可＝ブレース/丸括弧均衡（HEAD と同一オフセット）・import・呼び出し側シグネチャを静的確認。
