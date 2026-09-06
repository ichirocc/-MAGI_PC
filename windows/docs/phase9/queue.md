# フェーズ9 残画面キュー（ralph-loop 用・1周に1行だけ進める）

凡例: 仕様=未/済 ／ 実装=未/済/済(既存)/差戻/保留/対象外 ／ CI=未/緑/赤。
「済(既存)」＝Android 原本を読んで突き合わせた結果、WinUI に同等の導線が既にあった行。
「保留」＝同じ行で2回続けて失敗、または人間の判断が要る行（blockers.md に論点を書く）。
順序は利用者価値と「README が残作業と明記した順」。1周で1行だけ動かし、必ず commit する。

| # | 画面 | 機能 | Android 原本（正） | WinUI 置き場 | 仕様 | 実装 | CI | メモ |
|---|---|---|---|---|---|---|---|---|
| 1 | ホーム | 人員過剰の「希望固定N人」を名指しして、その場で希望を取り消す（3.492.0） | `MagiDashboardCards.kt` `CoverageDiagnosisCard`（`CoverageSurplus.pinnedStaff`, `vm.removeWish`） | `HomeView` | 済 | 済 | 緑 | README「WinUI 側の表示はフェーズ9の残作業」→ `HomeView.RenderCoverage`（run 33995356094） |
| 2 | ホーム | 処方箋カード＝次の一手を1つ提示・できあがり度・重複ボタン排除（3.480.0） | `MagiDashboardCards.kt` `OperatorNextActionCard`/`SmartActionCard`, `MagiViewModel` の完成度計算 | `HomeView` | 済 | 済 | 緑 | `HomeView.RenderNextAction`/`RenderSmartAction`（run 33995759601）。書き出しは設定タブのピッカーへ委譲、「なおすのを手伝って」は勤務表タブへ（#24 まで） |
| 3 | ホーム | 実行中の進捗（改善%・残り時間・回数・HARD残） | `MagiScheduleViews.kt` `progressSummary`/`LiveScheduleCard` | `HomeView` | 済 | 済 | 緑 | run 33996008522。`HomeView.ProgressSummary`（処方箋カード内の進捗行）＋`RenderLive`（途中経過の色タイル） |
| 4 | ホーム | コパイロット（HARD 族の要約と導線） | `MagiDashboardCards.kt` `CopilotCard` | `HomeView` | 済 | 済 | 緑 | run 33996263185。`HomeView.RenderCopilot`。分析タブの文言だけの表示は既存、操作つきカードをホームへ |
| 5 | ホーム | データ無し時の導線（空状態・セットアップ案内） | `MagiApp.kt` `EmptyStateCard`, `MagiSetupCards.kt` `SetupGuideCard` | `HomeView` | 済 | 済 | 緑 | run 33996508158。`HomeView` EmptyStateCard（開く→設定タブのピッカー委譲／サンプル→LoadFixtureAsync／新規→InitBlankState+編集タブ）。SetupGuide の件数行は EditView に既存、「次の一手」だけ追加 |
| 6 | 勤務表 | 週送り・違反ジャンプの画面下固定バー＋行列クロスハイライト（3.444.0/3.481.0） | `MagiScheduleViews.kt` `ScheduleNavBar`, `rememberScheduleNavState` | `ScheduleView` | 済 | 済 | 緑 | run 33996772282。`ScheduleView` NavBar（`RenderNavBar`/`MondayWeeks`/`VioDays`/`MarkTapped`）。違反日は #7 のフィルタ導入までは全種別 |
| 7 | 勤務表 | 違反の種別フィルタ（6分類・件数チップ・集中モード, E7） | `MagiScheduleViews.kt` `ViolationFilterBar`/`ViolationBucketChips`, `VioBuckets.kt` | `ScheduleView` | 済 | 済 | 緑 | run 33997066886。`MagiApp.ViewModels/VioBuckets.cs`（＋`VioBucketsTest` 3件）、`ScheduleView.RenderFilterBar`。セル枠・集計枠・違反日がフィルタ経由、集中モードはセル淡色化 |
| 8 | 勤務表 | 職員名検索（行強調）＋凡例の折りたたみ | `MagiScheduleViews.kt` `SearchLegendBar`, `ViolationLegend`, `ShiftColorLegend` | `ScheduleView` | 済 | 済 | 緑 | run 33997304264。`ScheduleView.RenderSearchLegend`（検索語は名前ヘッダを青太字、凡例は WinUI の実線色分けに合わせた文言） |
| 9 | 勤務表 | 土日・祝日の色分け（祝日データは外部ファイル, 3.441.0） | `JapanHolidays.kt`, `MagiScheduleViews.kt` 日ヘッダ | `ScheduleView` | 済 | 済 | 緑 | run 34010421182。`Views/JapanHolidays.cs`（Assets/japan_holidays.json＝Android と同一データ）、日ヘッダに曜日・祝日/日曜=赤・土曜=青・今日=濃緑太字 |
| 10 | 勤務表 | シフト別不足サマリーの1行バナー（3.116.0） | `MagiScheduleViews.kt` `ScheduleGrid` 冒頭 | `ScheduleView` | 済 | 済 | 緑 | run 34010737411。`ScheduleView.RenderShortageBanner`（needViolations の covU 集計）。日ヘッダ「▼N」（Ui.V6.DayRisks）も同時に配線＝#9 の気づきを解消 |
| 11 | 勤務表 | 集計セルの内訳ダイアログ（現在/下限/上限/目標・直し方・希望取消） | `MagiScheduleViews.kt` `TallyCard` の `TallyDetailUi` | `ScheduleView` | 済 | 済 | 未 | `ShowStaffTallyDetail`/`ShowDayTallyDetail`/`ShowTallyDetailAsync`。不足(covU)は既存の候補フライアウトを維持、過剰(covO)と職員別の回数違反がダイアログ |
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
