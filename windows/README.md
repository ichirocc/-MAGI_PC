# MAGI ShiftOptimizer — Windows 11 ネイティブ移植（C#/.NET + WinUI 3）

Android版（Kotlin + Jetpack Compose、`ichirocc/magi7ichiro-fork`）の Windows 11 ネイティブ移植。
JVM/Kotlin ランタイムに一切依存しない完全ネイティブな C# アプリを目指す。

移植の根幹決定（grilling で確定・再検討しない）：
- **UI/シェル**：C#/.NET + WinUI 3
- **エンジン**：`v6`/`model`（Kotlin, 22,583行・46ファイル）を C# へ全面手動移植（JVMバックエンドは残さない）

## ソリューション構成

```
windows/
  Magi.sln
  MagiEngine/           プラットフォーム非依存クラスライブラリ（net8.0）。
                         model/v6 の全内容（データモデル・checker/Evaluator/DeltaEvaluator・
                         探索統括・後処理研磨・CSV/JSON I/O・診断）。WinUI/Windows App SDK 参照なし。
  MagiEngine.Tests/      xUnit（net8.0）。ゴールデンフィクスチャ回帰・パリティ三角形テスト。
  MagiApp.WinUI/         WinUI 3 アプリ本体（net8.0-windows10.0.19041.0）。Windows専用ビルド。
  MagiEngine.GoldenGen/  使い捨てのオラクル生成コンソールツール（非配布）。
```

## ビルド・テスト（このリポジトリの開発サンドボックスから可能な範囲）

`MagiEngine`/`MagiEngine.Tests`/`MagiEngine.GoldenGen` はプラットフォーム非依存の `net8.0` で、
Linux 上でもビルド・実行できる（`MagiApp.WinUI` は Windows 専用）：

```bash
cd windows
dotnet build MagiEngine/MagiEngine.csproj
dotnet test MagiEngine.Tests/MagiEngine.Tests.csproj
dotnet run --project MagiEngine.GoldenGen/MagiEngine.GoldenGen.csproj
```

`MagiApp.WinUI` は Windows 11 実機（または `windows-latest` CI ランナー）でのみビルド・起動確認できる
（Windows App SDK の MSBuild ターゲットが Windows 専用のため）。CIは2つのワークフローに分離：
`.github/workflows/windows-engine-check.yml`（Linux, エンジン+テスト）／
`.github/workflows/windows-app-build.yml`（windows-latest, アプリのビルドのみ・起動確認は対象外）。

## 移植フェーズ

12フェーズに分割して段階的に進める（詳細は移植計画を参照。フェーズ完了ごとに区切りを置く）：

0. ✅ ソリューション雛形・CI・WinUI3足場
1. ✅ `MagiState` データモデル + JSON往復（Android/Web版とのファイル互換を維持する方針）
2. ✅ `Problem`（解決済みビュー）
3. ✅ **パリティ三角形**（`ViolationChecker`/`Evaluator`/`DeltaEvaluator`）＝最重要フェーズ
4. ✅ 初期解生成＋薄い入口
5. ✅ 探索統括（SA→ALNS/RSI/RSI++→Portfolio）＝coroutines→TPL変換の最大リスク
6. ✅ 後処理研磨パス（`V6HotfixPasses.kt` 4,682行、C#では族ごとに複数ファイルへ分割）
7. ✅ `V6FinalPort` 統括・CSV・診断（この時点で MagiEngine は機能的に完結）
8. ✅ WinUI3縦断スライス（フィクスチャ読込→検査→読取専用グリッド表示。DIコンテナで
   `MagiViewModel` を組み立て `MainWindow` へ注入する経路まで Windows CI でビルド実証済み）
9. 🚧 UIシェル本体＋ViewModel（**進行中**。画面マップは
   [`docs/screen_port_map.md`](docs/screen_port_map.md) を参照＝下調べ資料であり、
   実移植時は必ず元のKotlinソースを直接確認すること）
   - ViewModel層＝**移植完了**。Kotlin原本 `MagiViewModel.kt` の拡張関数86件・コアメンバ関数とも
     すべて対応物あり（`runInBackground`/`applyBgResult` も含め完了。詳細はフェーズ10）。
   - UI層＝5タブすべてに実体あり。勤務表タブはセル編集(タップ→担当可能シフト選択)・
     元に戻す/やり直す・違反ハイライト/希望バッジ・**シフト集計(職員別/日別、Kotlin原本TallyCardの
     最小移植=`RenderStaffTally`/`RenderDayTally`。生カウントは`Schedule`から都度計算・セル枠は
     `CountViolations`/`NeedViolations`で色分け)**まで実装。編集タブは月次条件(希望/日別必要人数の
     一覧・追加・削除)・職員管理(追加/改名/削除、削除確認ダイアログ付き)・年間マスター
     (グループ/シフトの追加/改名/削除に加え、制約(ルール)10族=cons1/cons2/cons3系4/cons41(s)/
     cons42(s)の追加/変更/削除・**スキル区分の追加/改名/削除(`AddSkillGroup`等)・群×シフトの
     担当可否/適切回数マトリクス(`Ws1SetGroupShift`/`Ws1SetGroupApt`/`Ws1ResetGroupApt`、
     `EditView.xaml.cs`の`BuildGroupShiftMatrix`)・上限人数(2パターン目)の使用可否(`Ws1SetUse2`)**
     まで実装。種類ごとに入力欄の構成が異なる=`EditView.xaml.cs` の`ConstraintFamilyMetas` 参照。
     職員管理では職員ごとのスキル区分割当(`SetStaffSkill`)も追加/改名アクションに続けて書く。分析タブは診断一覧＋「直し方を探す」＋**違反の場所**
     （セル単位の違反=`UiState.ViolationCells`に載る族のみ。タップで勤務表タブへ切替＋該当セルへ
     スクロール＋約2.5秒ハイライト=`MainWindow.JumpToCell`/`ScheduleView.FocusCell`）。設定タブは
     最適化設定＋データ入出力(JSON開く/保存・CSV取込/書出)。デザイントークン（ブランド色）も
     `Styles/MagiTheme.xaml` へ移植済み（余白/角丸/タイポグラフィスケールの全面移植は未着手）。
     未対応（意図的にスコープ外）：`MagiScheduleViews.kt`の残り（週ページング・横スクロール併用・
     ItemsRepeaterベース化・違反種別フィルタ・検索/凡例折りたたみ等）・covU/covO/c41系(日単位)や
     low/high/apt/c2(職員単位)の違反箇所ジャンプ（単一セルを指さないため対象外、上記「違反の場所」参照）。
   - **色設定UI＋データ入出力のエラーハンドリング（2026-09-02）**: 設定タブに「表示色」節を追加
     （`SettingsView.RenderShiftColors`/`RenderViolationColors`）。シフト記号の表示色（`ShiftColorList()`/
     `SetShiftColor`/`ResetShiftColor`）・違反の基準色2種（必須/要調整、`SetViolationColor`/
     `SetViolationSoftColor`）・族別の個別色（19族、`SetViolationFamilyColor`）を、簡易カラーピッカー
     （既存7色パレット`MagiAccent.All`のスウォッチ＋16進テキスト入力の2択、フライアウト）で編集できる。
     **これらのViewModel APIは元々実装済みだったが、勤務表グリッド（`ScheduleView`）側が一切参照して
     おらず、設定を変えても見た目が変わらない「配線されていない箱」だった**——同じタイミングで
     `ScheduleView.ResolveVioBrush`（新設、`ColorHex`経由でシフト背景色/違反枠色を実際に解決）を配線し、
     セル背景（シフト色）・違反枠（族別→基準色→既定色の優先順位、Kotlin原本`resolvedVioColor`と同じ順）
     の両方に反映されるようにした（メイングリッド・シフト集計の両方が共有）。データ入出力の4ハンドラ
     （`OnOpenDataClick`等）は`FileOpenPicker`/`FileIO`の例外を素通りさせ`async void`ハンドラの
     未処理例外でアプリごとクラッシュしうる欠陥があったため、try/catchで`NotifySave`/`NotifyOpenFailure`
     （既存API・呼び出し口が無かった）へ受け止めるよう修正。
   - **群×シフトのcanDo/適切回数マトリクス＋スキル区分CRUD（2026-09-02）**: 「実装済みViewModel APIの
     呼び出し口を全数点検する」作業で発見した、色設定と同種の「配線されていない箱」——
     `Ws1SetGroupShift`/`Ws1SetGroupApt`/`Ws1ResetGroupApt`/`Ws1SetUse2`と`SkillGroups`/
     `AddSkillGroup`/`EditSkillGroup`/`RemoveSkillGroup`/`SetStaffSkill`はいずれもフェーズ9で
     移植・テスト済みだったが、この画面から一度も呼ばれていなかった。**群がどのシフトを担当できるか
     (canDo)を設定する手段がこれしか無く**、新規データでは誰も何のシフトも担当できないまま
     何も割り当てられない状態だった。年間マスターに、群(行)×シフト(列)のチェックボックス(canDo)＋
     テキスト欄(適切回数目標)のマトリクス（`EditView.BuildGroupShiftMatrix`、群/シフト数が変わらない
     限り既存コントロールを使い回してフォーカスを保つ）と、スキル区分の追加/改名/削除、上限人数
     (2パターン目)使用トグルを追加。職員管理にはスキル区分の割当欄(`StaffSkillCombo`)を追加
     （`Ws1AddStaff`/`Ws1EditStaff`自体はSkillIdxを受け取らないAPIのため、追加・改名の直後に
     `SetStaffSkill`を続けて呼ぶ）。
   - **ホーム3機能＋希望一括操作＋即時保存の配線（2026-09-02）**: 同じ全数点検の続き。
     `GenerateSmartInitial`（初期解生成・賢い版）/`RunSoftPolish`（仕上げ最適化のみ・破壊なし）/
     `ApplyAlternative`（Portfolio探索の「他の案」適用、`Ui.Alternatives`が0件のときは節を隠す）を
     ホームタブへボタン3つで追加。`ApplyWishes`/`ClearAllWishes`（登録済み希望の一括反映/一括削除）を
     月次条件へ追加（担当外の希望が混じる場合は「含めて反映/除いて反映/キャンセル」の3択ダイアログ）。
     **`SaveNow`（デバウンス無し即時同期保存、`MagiViewModel.SaveNow`のKDoc「autoSaveの1200msデバウンス
     中にプロセスが破棄されても編集が失われないための保険」）がどこからも呼ばれておらず、直近の編集から
     1200ms以内にウィンドウを閉じると自動保存に間に合わず編集が消えうる欠陥**を発見・修正——
     `MainWindow.OnAppWindowClosing`の実行中でない通常終了経路で必ず呼ぶ。`RestorePreviousData`
     （「データを開く」直前の状態へ戻す）も設定タブへボタンを追加（`Ui.PrevBackupAvailable`が
     falseの間はボタンごと隠す）。
   - **CIビルド失敗の修正＋種類別CSV/ログ書出/新規作成の配線（2026-09-02）**: 直前2コミット
     （色設定UI・群×シフトマトリクス）がいずれもWindows CIでビルド失敗していた
     （`ScheduleView.xaml.cs`が素の`Color`型=`Windows.UI.Color`を使うのに`using Windows.UI;`が
     無くCS0246。このサンドボックスはWindows専用プロジェクトをビルドできずCIでしか検出できない
     既知の制約で、2回連続で見落とした）。`using`を1行追加して解消・CI緑化を確認。続けて同じ
     全数点検の最後のまとまり: 種類別CSV（`ImportStaffCsv`/`ExportStaffCsv`・`ImportWishesCsv`/
     `ExportWishesCsv`・`ImportConstraintsCsv`/`ExportConstraintsCsv`、氏名一致で既存データへ
     追加/更新）・名簿CSVの新規取込（`ImportRosterAs`、「勤務表として/希望として」をダイアログで
     選べる——`ImportCsvSmart`には無い選択肢）・操作ログ書出（`ExportLogs`/`ExportLogsJson`）・
     新規作成（`InitBlankState`、最小構成から作り直す。`Load()`経路のため現在のデータは
     `RestorePreviousData`で復元可能）を設定タブへ配線。
   - **禁止の並び診断＋回数固定の緩和ボタンを分析タブへ配線（2026-09-02）**: `RelaxStaffRangePin`
     （「回数の固定で止まった手」一覧に「±1 緩める」ボタンを追加）と、これまで画面が一度も
     読んでいなかった`UiState.ForbiddenDiag`（禁止の並び(c3n)が「このデータ・希望のままでは
     崩せない」と判定した箇所）＋`RelaxForbiddenRule`（新設の「禁止の並びで止まっている箇所」節、
     崩せないと判定された行だけに「緩める（削除）」ボタン）を配線。前者は診断だけ見えて直す手段が
     無く、後者は診断結果自体が全く表示されていなかった。
   - **個人別の回数（下限/上限）編集UIを職員管理へ配線（2026-09-02）**: `SetStaffRange`/
     `RemoveStaffRange`（フェーズ9で移植・テスト済み）は、色設定・群×シフトマトリクスと同じ
     「実装済みだが呼び出し口が無い箱」の中でも特に基本的な欠落だった——個々の職員の回数上下限を
     設定する手段がアプリのどこにも無かった（`RelaxStaffRangePin`の±1調整は既存値の微調整のみで
     新規設定はできない）。職員管理の「対象の職員」選択を共有し、`StaffCountRules`（個人別レンジと
     適切回数(apt)の実効目標を統合したビュー）から一覧表示＋シフト選択＋下限/上限入力で設定できる。
   - **OneDrive対応（2026-09-01, ユーザー確認）**: データ入出力(`SettingsView`)は
     `FileOpenPicker`/`FileSavePicker`（実ファイルパスを指す `StorageFile`）経由で読み書きするため、
     OneDrive同期フォルダ内のファイルも特別な対応なしにそのまま開く/保存できる（クラウドのみの
     プレースホルダーファイルも、ピッカー選択時にシェルが自動的に実体化する。アンパッケージ
     Win32アプリなので `CachedFileManager.CompleteUpdatesAsync` 等のブローカー越し更新通知も不要）。
     追加実装は無し（既定保存先の変更・自動保存のOneDrive化・Graph API直接連携は明示的に不要と確認済み）。
10. ✅ 背景実行（**完了**。Android の WorkManager に直接対応する Windows デスクトップの機構は
    無いため、`OptimizationRepository` が元々プロセス内 pub/sub として設計されていた点を活かし、
    同一プロセス内の `Task` として実装した——設計判断の詳細は
    `MagiApp.ViewModels/MagiViewModel.Background.cs` のクラスKDoc参照。ウィンドウを閉じてもプロセスを
    生かし続けるか（トレイ常駐等）は「生かし続けない・その代わり実行中は閉じる前に確認する」で
    決着（`MainWindow.OnAppWindowClosing` 参照。トレイアイコンはWin32相互運用か追加パッケージが
    要り、このサンドボックスでは実機検証できないリスクを避けた）。**2026-09-01、ユーザーが
    「Windows11版はトレイ常駐不要・ウィンドウを閉じてもプロセスを生かし続ける必要は無い」と
    明示的に再確認**＝上記の決着どおりで確定（再提案しない）。
    **kill耐性は撤去済み（2026-09-01, ユーザー明示判断「クラッシュからの復旧はそこまで重視しない」）**:
    当初は `RunFiles`（背景実行専用の共有ファイル4種＝入力・完了結果・8秒ごとの途中最良スナップショット・
    所有権マーカー）と実行中マーカー（`magi_run_marker.json`）で、プロセスがkillされても次回起動時に
    「前回の計算は中断されました」バナーから再開できる仕組みを実装していたが、全撤去した
    （`Work/RunFiles.cs`・`MagiViewModel.RunMarker.cs`・`UiState.InterruptedRun`/`InterruptedInfo`・
    `DismissInterrupted()` を削除、`MagiViewModel.RunMarker.cs`→`MagiViewModel.Restore.cs` へ縮小
    改名）。背景実行(`RunInBackground`)はディスクI/Oを一切行わない純粋なインメモリ処理になり、前景実行
    (`RunV6FullOptimize`)と同型になった。**残したもの**（クラッシュ復旧とは別の、通常運用のUX）:
    自動保存(`magi_autosave.json`)からの起動時復元（編集のたびに継続保存され、次回起動時に前回の続きを
    開く。クラッシュの有無に関係なく毎回使う）と、「データを開く」直前の退避(`PrevBackupAvailable`)。
    詳細・撤去理由は `MagiViewModel.Background.cs`/`MagiViewModel.Restore.cs` のクラスKDoc参照。
    自動保存等が使う原子置換（一時ファイル→rename）は書込のごく短いウィンドウ中にkillされると
    `*.tmp` を迷子で残し得るため、起動のたびに `DataDir` 直下の迷子 `*.tmp` を無条件で片付ける
    （`CleanupStrayTempFiles`。ディスク容量を脅かす量にはならないが放置しない、というだけの軽微な保険）。
11. 🚧 パッケージング/配布（**部分的に先行**。`windows-installer.yml` が Inno Setup で
    per-user の `setup.exe` を、msbuild で MSIX をそれぞれ生成し Artifacts へ保存する所まで
    実装済み。Authenticode 署名も Secrets 設定時のみ有効化される形で入っている。
    アイコン/ブランディングは `MagiApp.WinUI/Assets/`（Kotlin原本のlauncher iconの意匠を
    現行ブランド配色へ揃えて再構成）に用意済み。**残るのは実機での新規インストール確認のみ**
    （このサンドボックスでは Windows 実機/実インストールの検証ができないため未実施）。

## 変更規律（HF77 を移植作業自体にも適用）

移植中に見つけた「それっぽくない」数値・閾値・重みを、翻訳の都合で勝手に補正しない。
逐語的に移し、凍結したゴールデンフィクスチャの期待値で正しさを判定する。

## スコープ外

`app/src/main/cpp/magi_native.cpp`（JNI経由のC++高速化ミラー）はこの移植の対象外
（Android/ARM上のJNIオーバーヘッド対策であり、Windows デスクトップでは純粋なマネージドC#で
十分な可能性が高い。ネイティブ層の要否はフェーズ5終盤の粗いタイミング計測後、証拠が出てから
プロファイラで検討する）。
