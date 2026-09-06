# CLAUDE.md — MAGI ShiftOptimizer Windows 版（-MAGI_PC）

Android 版（`ichirocc/magi7ichiro-fork`、Kotlin/Compose）の Windows 11 ネイティブ移植（C#/.NET 8 + WinUI 3）。
**Kotlin が正、C# は同値の忠実な移植**＝盤面の意味論・チェッカー・目的関数・後処理チェーンは Android と同じで、Android の変更を同日に同期する。
移植の経緯・構成・入れ方・レビュー対応の記録は `windows/README.md`。業務ルールとデータ項目は `docs/business-logic.md`／`docs/data-models.md`
（Android と同一の写し。存在しない項目を創作しない）。全体像は `docs/sudo_model.md`。
応答は簡潔・結論先出し・日本語。コード識別子は英語のまま。

## 環境の注意点（見ても分からない罠）
- `MagiEngine`／`MagiEngine.Tests`／`MagiEngine.GoldenGen` は `net8.0` で Linux 上でビルド・実行できる。`MagiApp.WinUI` は Windows 専用＝
  このサンドボックスではビルド不可で、CI（`.github/workflows/windows-app-build.yml`）だけが検証する。ViewModel 層は `MagiApp.ViewModels`
  （テスト可）に置き、WinUI 側は薄く保つ。
- .NET SDK はこの環境では `/root/.dotnet`（`export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1`）。エンジンテストは
  `cd windows && dotnet test MagiEngine.Tests/MagiEngine.Tests.csproj -c Release --nologo -v q`（約 2.5 分。背景で回してログへ）。
  `dotnet test ... | head` のようにパイプで切ると失敗を見落とす（赤いテストを push した前科あり）。
- シェルの cwd は呼び出しごとに Android 側の repo へ戻る＝このリポジトリの操作は**絶対パス**で行う（相対パスで Android 側に
  誤コミットした前科あり）。
- コミットは `main` に直接（作業ブランチ運用は Android 側だけ）。push 前に `tools/comment_ratio.py --staged` で追加コメントを 1 行ずつ判定する。
- インストーラー（Inno Setup）と MSIX は `windows-installer.yml`。公開 Release の署名必須化はリポジトリ変数 `REQUIRE_CODE_SIGNING=true` の opt-in
  （証明書は未導入）。

## 判断の基準
- 変更は Android（Kotlin）から降ろす。C# 単独で探索動学・重み・採用基準を変えない（パリティが崩れる）。C# 側だけの改善は
  「出力が同じ」もの（安全な巻き戻し・性能・型の契約）に限り、`windows/README.md` の「レビュー対応の記録」に理由を書く。
- Kotlin と同じ名前・同じ分割で移植する（`V6HotfixPasses.*.cs` の partial、`ViolationComponentRepair.cs` など）。テストも Kotlin の
  テストを 1 対 1 で写す（テスト名は英語のまま）。
- 表示語彙は Android の下流語彙が正（例: c1＝「期間の制約」）。
- 読みが分かれて成果物が変わるときだけ止まって聞く（AskUserQuestion、推奨度⭐つき）。それ以外は仮定を明記して進める。
- 周囲のコードのコメント密度・命名に合わせる。画面を触ったら `design-review`、非自明な変更の前は `grilling`。

## 実行前に確認を取る操作／変えない決定
- **HF77**: パラメータ・重み・データ値は業務担当者の明示数値指示＋1 件ずつ。重みの単一の真実は `MirrorKeys.WeightOf`（Android の
  `MirrorKeys.weights` と同値: groupViol 10000 > pref 9000 > covU 8000 > c3n 7000 > low 90 > high 45 > c3mn 30 = c1 30 > covO 5 > c3 3 > c3m 2 > 他 1）。
- Android の決定記録（D3〜D8・E5、ws8/ws9 等の実装不要）はこちらでも再提案しない。
- 公開済みバージョンのインストーラーは差し替えない（`windows-installer.yml` が同一タグ別コミットを失敗させる）。タグ `win-v*` の push と
  Release の公開は本人の操作。
- 不可逆な操作（履歴の書き換え・削除・外部公開）は確認を取る。

## 必要時に読むもの
- `windows/README.md`（ソリューション構成・移植フェーズ・入れ方・レビュー対応の記録・スコープ外）／ `windows/installer/README.md`（署名）／
  `windows/docs/phase9/`（ドッグフーディングのキューと仕様）。
- Android 側の恒久の事実と作業記録: `magi7ichiro-fork` の `CLAUDE.md`・`docs/history/`。この repo の `docs/archive/android_claude_md_snapshot.md` は
  移植時点（3.464.0 前後）の Android CLAUDE.md の写し＝当時の経緯を引くときだけ読む（12,000 行、常時読まない）。
