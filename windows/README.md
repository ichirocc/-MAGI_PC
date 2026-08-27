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

0. ソリューション雛形・CI・WinUI3足場（**このフェーズ**）
1. `MagiState` データモデル + JSON往復（Android/Web版とのファイル互換を維持する方針）
2. `Problem`（解決済みビュー）
3. **パリティ三角形**（`ViolationChecker`/`Evaluator`/`DeltaEvaluator`）＝最重要フェーズ
4. 初期解生成＋薄い入口
5. 探索統括（SA→ALNS/RSI/RSI++→Portfolio）＝coroutines→TPL変換の最大リスク
6. 後処理研磨パス（`V6HotfixPasses.kt` 4,682行、C#では族ごとに複数ファイルへ分割）
7. `V6FinalPort` 統括・CSV・診断（この時点で MagiEngine は機能的に完結）
8. WinUI3縦断スライス（フェーズ4完了後に前倒し着手可）
9. UIシェル本体＋ViewModel
10. 背景実行
11. パッケージング/配布

## 変更規律（HF77 を移植作業自体にも適用）

移植中に見つけた「それっぽくない」数値・閾値・重みを、翻訳の都合で勝手に補正しない。
逐語的に移し、凍結したゴールデンフィクスチャの期待値で正しさを判定する。

## スコープ外

`app/src/main/cpp/magi_native.cpp`（JNI経由のC++高速化ミラー）はこの移植の対象外
（Android/ARM上のJNIオーバーヘッド対策であり、Windows デスクトップでは純粋なマネージドC#で
十分な可能性が高い。ネイティブ層の要否はフェーズ5終盤の粗いタイミング計測後、証拠が出てから
プロファイラで検討する）。
