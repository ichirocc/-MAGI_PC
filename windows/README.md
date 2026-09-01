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
10. ✅ 背景実行（**完了**。Android の WorkManager に直接対応する Windows デスクトップの機構は
    無いため、`OptimizationRepository` が元々プロセス内 pub/sub として設計されていた点を活かし、
    同一プロセス内の `Task` として実装した——設計判断の詳細は
    `MagiApp.ViewModels/MagiViewModel.Background.cs` のクラスKDoc参照。kill耐性は `RunFiles`/
    `RunMarker` による途中最良スナップショット・起動時復元。ウィンドウを閉じてもプロセスを
    生かし続けるか（トレイ常駐等）は「生かし続けない・その代わり実行中は閉じる前に確認する」で
    決着（`MainWindow.OnAppWindowClosing` 参照。トレイアイコンはWin32相互運用か追加パッケージが
    要り、このサンドボックスでは実機検証できないリスクを避けた）。
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
