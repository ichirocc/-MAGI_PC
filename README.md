# MAGI Native (Kotlin/Android)

MAGI shift optimizer, native Android port.

This project contains a Kotlin/Jetpack Compose Android app that ports the MAGI web shift optimizer engine into native Kotlin.

## ドキュメント目次（AI はここから読む）

> 設計・仕様は下記の Markdown に分かれています。**まずこの表で当たりをつけて**から目的の文書を読んでください。事実が変わりやすい順に独立させており、とくに `business-logic.md` / `data-models.md` を最新に保つことでハルシネーションの大半を抑えます。

| ファイル | 何が書いてあるか |
|---|---|
| [`docs/overview.md`](./docs/overview.md) | 機能の目的と主要機能の粗い要約（まず全体像） |
| [`docs/requirements.md`](./docs/requirements.md) | 要件定義。ユーザーストーリーと受け入れ条件（なぜ存在するか） |
| [`docs/design.md`](./docs/design.md) | 設計。主要インタフェースと処理フロー（どう作られているか） |
| [`docs/architecture.md`](./docs/architecture.md) | レイヤー構成・依存関係・どのファイルが何を担当するか（地図） |
| [`docs/business-logic.md`](./docs/business-logic.md) | 判定条件・計算（重み19種）・エラー方針（**業務ルールの正解**） |
| [`docs/data-models.md`](./docs/data-models.md) | エンティティ定義・項目名と型（**存在しない項目を創作しない**） |
| [`docs/screen_spec.md`](./docs/screen_spec.md) | 画面仕様（挙動・実寸・違反/希望の表示） |
| [`docs/magi_design_system.md`](./docs/magi_design_system.md) | デザイン基盤（色/余白/タイポ/部品） |
| [`docs/v6_engine_native_port.md`](./docs/v6_engine_native_port.md) | エンジン（v6）の移植 |
| [`CLAUDE.md`](./CLAUDE.md) | 引き継ぎ・直近の状態・作業の進め方（grilling 等） |

**最終更新**：2026-07-27（3.288.0 判断ログの3軸強化＝時間(予算配分と早期終了条件)・回数(focus遷移サマリ)・状態(残存分析＝もう直せない/まだ狙える)＋RSIラウンド行のスパム抑制。3.214.0〜3.287.0 の詳細は `CLAUDE.md` の各節に記録）／ **コード基準コミット**：main HEAD（この目次が古いと他が正しくても信頼が崩れるため、改修時は対象文書と本目次を必ず更新）。

## Status

- Engine core: Kotlin-native greedy + SA optimizer
- V6 web bridge compatibility: partially ported
- Constraint fidelity: Level Zero preserved for top-level constraints
- Input: JSON state via editor/sample assets
- Output: optimized assignments and diagnostics

## Build

```bash
./gradlew assembleDebug
```

## Run tests

```bash
./gradlew test
```

## Notes

This is a generated p11 project snapshot.
