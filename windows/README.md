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
     元に戻す/やり直す・違反ハイライト/希望バッジまで実装。編集タブは月次条件(希望/日別必要人数の
     一覧・追加・削除)・職員管理(追加/改名/削除、削除確認ダイアログ付き)・年間マスター
     (グループの追加/改名/削除、シフト追加/改名/制約編集は未実装)。分析タブは診断一覧＋
     「直し方を探す」。設定タブは最適化設定＋データ入出力(JSON開く/保存・CSV取込/書出)。
     デザイントークン（ブランド色）も `Styles/MagiTheme.xaml` へ移植済み（余白/角丸/
     タイポグラフィスケールの全面移植は未着手）。
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
