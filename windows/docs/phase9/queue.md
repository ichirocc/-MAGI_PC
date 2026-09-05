# フェーズ9 残画面キュー（ralph-loop 用・1周に1行だけ進める）

凡例: 仕様=未/済 ／ 実装=未/済/済(既存)/差戻/保留/対象外 ／ CI=未/緑/赤。
「済(既存)」＝Android 原本を読んで突き合わせた結果、WinUI に同等の導線が既にあった行。
「保留」＝同じ行で2回続けて失敗、または人間の判断が要る行（blockers.md に論点を書く）。
順序は利用者価値と「README が残作業と明記した順」。1周で1行だけ動かし、必ず commit する。

| # | 画面 | 機能 | Android 原本（正） | WinUI 置き場 | 仕様 | 実装 | CI | メモ |
|---|---|---|---|---|---|---|---|---|
| 1 | ホーム | 人員過剰の「希望固定N人」を名指しして、その場で希望を取り消す（3.492.0） | `MagiDashboardCards.kt` `CoverageDiagnosisCard`（`CoverageSurplus.pinnedStaff`, `vm.removeWish`） | `HomeView` | 済 | 済 | 緑 | README「WinUI 側の表示はフェーズ9の残作業」→ `HomeView.RenderCoverage`（run 33995356094） |
| 2 | ホーム | 処方箋カード＝次の一手を1つ提示・できあがり度・重複ボタン排除（3.480.0） | `MagiDashboardCards.kt` `OperatorNextActionCard`/`SmartActionCard`, `MagiViewModel` の完成度計算 | `HomeView` | 済 | 済 | 未 | `HomeView.RenderNextAction`/`RenderSmartAction`。書き出しは設定タブのピッカーへ委譲、「なおすのを手伝って」は勤務表タブへ（#24 まで） |
| 3 | ホーム | 実行中の進捗（改善%・残り時間・回数・HARD残） | `MagiScheduleViews.kt` `progressSummary`/`LiveScheduleCard` | `HomeView` | 未 | 未 | 未 | |
| 4 | ホーム | コパイロット（HARD 族の要約と導線） | `MagiDashboardCards.kt` `CopilotCard` | `HomeView` | 未 | 未 | 未 | |
| 5 | ホーム | データ無し時の導線（空状態・セットアップ案内） | `MagiApp.kt` `EmptyStateCard`, `MagiSetupCards.kt` `SetupGuideCard` | `HomeView` | 未 | 未 | 未 | |
| 6 | 勤務表 | 週送り・違反ジャンプの画面下固定バー＋行列クロスハイライト（3.444.0/3.481.0） | `MagiScheduleViews.kt` `ScheduleNavBar`, `rememberScheduleNavState` | `ScheduleView` | 未 | 未 | 未 | README は「意図的にスコープ外」としていたが今回の指示で対象 |
| 7 | 勤務表 | 違反の種別フィルタ（6分類・件数チップ・集中モード, E7） | `MagiScheduleViews.kt` `ViolationFilterBar`/`ViolationBucketChips`, `VioBuckets.kt` | `ScheduleView` | 未 | 未 | 未 | `VioBuckets.kt` は Android 非依存＝ロジックはそのまま移植 |
| 8 | 勤務表 | 職員名検索（行強調）＋凡例の折りたたみ | `MagiScheduleViews.kt` `SearchLegendBar`, `ViolationLegend`, `ShiftColorLegend` | `ScheduleView` | 未 | 未 | 未 | |
| 9 | 勤務表 | 土日・祝日の色分け（祝日データは外部ファイル, 3.441.0） | `JapanHolidays.kt`, `MagiScheduleViews.kt` 日ヘッダ | `ScheduleView` | 未 | 未 | 未 | |
| 10 | 勤務表 | シフト別不足サマリーの1行バナー（3.116.0） | `MagiScheduleViews.kt` `ScheduleGrid` 冒頭 | `ScheduleView` | 未 | 未 | 未 | |
| 11 | 勤務表 | 集計セルの内訳ダイアログ（現在/下限/上限/目標・直し方・希望取消） | `MagiScheduleViews.kt` `TallyCard` の `TallyDetailUi` | `ScheduleView` | 未 | 未 | 未 | |
| 12 | 編集 | 職員×シフトのマトリクス（担当可否・目標・上下限・実績, 3.477.0） | `StaffShiftMatrix.kt` `StaffShiftMatrixCard`/`StaffShiftCellSheet` | `EditView` | 未 | 未 | 未 | |
| 13 | 編集 | 必要人数の日別例外カレンダー（複数日一括） | `NeedDayEditor.kt` `NeedMonthGrid`/`NeedApplyPanel` | `EditView` | 未 | 未 | 未 | 既存の EditView にカレンダー語あり＝要確認 |
| 14 | 編集 | 希望シフトのカレンダー（複数日一括・移動の意味論） | `WishEditor.kt` `WishMonthGrid`/`WishApplyPanel` | `EditView` | 未 | 未 | 未 | `SetWishesForDays` は配線済み＝UI の形を突き合わせる |
| 15 | 編集 | 月次チェックリスト（職員/希望/必要人数/診断→つくる） | `MagiSetupCards.kt` `MonthlyChecklistCard` | `EditView` | 未 | 未 | 未 | |
| 16 | 編集 | 実働チェック（シフト別 担当人数・需要・欠勤余裕） | `MagiSetupCards.kt` `StaffingRealityCard` | `EditView` | 未 | 未 | 未 | |
| 17 | 編集 | 制約10族の「詳しい説明」ⓘ展開（3.409.14） | `ConstraintHelp.kt`, `ConstraintEditor.kt` `ConstraintHelpExpander` | `EditView` | 未 | 未 | 未 | `ConstraintHelp.kt` は Android 非依存 |
| 18 | 分析 | 要確認一覧＋日別/人別の注意リスト＋内訳（統合カード, 3.459.0/3.471.0） | `MagiDashboardCards.kt` `ViolationHubCard`/`ConfirmListBody`/`AttentionBody`/`BreakdownBody`, `AnalysisTriage.kt` | `AnalysisView` | 未 | 未 | 未 | `AnalysisTriage.kt` は Android 非依存 |
| 19 | 分析 | 設定の見直し（件数集約・上位6件） | `MagiDashboardCards.kt` `SettingIssuesCard` | `AnalysisView` | 未 | 未 | 未 | |
| 20 | 分析 | C1 頭打ち診断・回数固定の影響（表示の突き合わせ） | `MagiDashboardCards.kt` `C1PlateauCard`/`PinFixedImpactCard` | `AnalysisView` | 未 | 未 | 未 | `RelaxStaffRangePin`/`ForbiddenDiag` は配線済み |
| 21 | 設定 | 色ピッカー 36色（6×6・淡いパステル・選択✓, 3.460.0） | `ShiftColorEditor.kt` `ColorPickerDialog` | `SettingsView` | 未 | 未 | 未 | 現状は7色＋16進 |
| 22 | 設定 | 重み表（族・重み・HARD/SOFT） | `MagiDashboardCards.kt` `WeightTableCard` | `SettingsView` | 未 | 未 | 未 | 分析タブに「重み」語あり＝要確認 |
| 23 | シェル | トップバーの状態バッジ（実行中/配布可/必須違反N/未計算）＋下部コマンドバー | `MagiApp.kt` `MagiTopBar`/`BottomCommandBar` | `MainWindow` | 未 | 未 | 未 | |
| 24 | ホーム | 「なおすのを手伝って」（不足セルの候補ピッカー） | `MagiDashboardCards.kt` `GuidedFixDialog` | `HomeView` | 未 | 未 | 未 | 集計セルの候補フライアウトで一部充足＝要確認 |
