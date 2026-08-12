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
| [`docs/algorithm_portfolio.md`](./docs/algorithm_portfolio.md) | 探索・研磨の**入口と責務の台帳**（どの手がどこで走るか・横断機構・既定OFF・廃止済み・未実施の提案） |
| [`docs/lessons.md`](./docs/lessons.md) | **教訓メモ**（修正した点↔機能した点・作る前にやめた判断・測り方・検証手段の穴。新規作成せず更新する） |
| [`CLAUDE.md`](./CLAUDE.md) | 引き継ぎ・直近の状態・作業の進め方（grilling 等） |

**最終更新**：2026-08-12（3.367.0 3.366.0で「設計トレードオフのため未対応」としていた4項目を実測してから対応。
TSANビルドの実コスト（~7秒+~0.05秒）を測ったうえで、通常ビルド検査を削らず**専用TSAN CIジョブを新設**して
`runSharedHandleConcurrency`のコメントが主張する検出力を実際にCIへ担保。あわせてflatフィクスチャごとの
重複実行を1回に統合・`--shared-only`+`--expect`同時指定時の無警告スキップを明示的な注記へ・exit code契約を
ファイル冒頭にコメント化（過剰な分割はせず現状の1/2二層を維持）・簡素化4件を適用。CI相当のフル実行で
両フィクスチャMATCH・4,195,533手・0 mismatchesを再確認。test/tools/CIのみ・engine/重み/スコア不変。
3.366.0 3.365.0（共有ネイティブハンドル並列安全テスト）に `/code-review`（8視点並列サブエージェント）を
実施し、正しさに関わる指摘2件（スレッド生成中の例外で`std::terminate()`しうる／`out[0]`(自己整合番兵)を独立検証せず
serial/parallelが同一に自己整合失敗した場合を見逃しうる）と規約違反2件（version bump漏れ・README最終更新の陳腐化）を
検証のうえ修正。CI相当のフル実行（golden.flat＋sample_v6.flat）で両方MATCH・4,195,533手・0 mismatchesを再確認。
test/toolsのみ・engine/重み/スコア不変。3.365.0 別ブランチ x8ygvy を精査し**共有ネイティブハンドルの並列安全テスト**（923bf07）だけを選択的に移植＝8スレッドが同一 MagiProblem で逐次と bit 一致を実行で証明・既存 native-parity CI で自動実行（`-pthread` 追加）。covU 早期終了系（このセッションで real3 A/B により有害実証・却下）は取り込まず。3.364.0 c1「壁」判定(検査2b-2)の need2 依存を実データ計測で **false wall と確定**し正直化＝非休の c1 窓は物理供給≥需要が常に成立し構造的不能にならない（golden Dﾃ は物理供給248>>需要32・手作り盤面は既に35回配置）ため「構造的に残ります」を撤回し covO-tension として案内。休のみ真の壁を維持。read-only・スコア不変。backlog#4 解消・3.179.0 の据え置き前提を反証。3.363.0 直近コード（3.352-3.360）を sibling-bug 狙いで焦点レビュー＝**実バグ0**（keep-best 比較の集約は完全・診断の算術も正しい・environmentLine/telemetry も健全）。あわせて 3.95.0 の stale fact「golden 構造的covU=2」を実測（structuralHardFloor=0）で反証・訂正／docs のみ。3.362.0 パリティネット（言語跨ぎ・3.357.0）へ**2つ目の実データ形状 sample_v6**（入力盤面 hard=15＝groupViol/c3n/pref/covU 発火）を追加＝golden(hard=0)では未 exercise の C++ HARD族パスを実データで照合。`host_parity_bench.cpp` の `--expect` を flat と出現順で対応づけ1回のベンチで両形状を照合／test・CIのみ・エンジン不変（backlog#6）。3.361.0 残作業 #1「covU-blocked をウォッチドッグへ配線し早期終了」を**実測のうえ却下**＝早期終了は keep-best-safe でない（探索を止めると未見の改善を諦める・keep-best では回収不能）／sample_v6 が「22秒停滞後の soft バースト改善」の実在を示す／動機データ real/user 消失＋golden は hard=0 到達で no-op のため直接 A/B 不可＝反証されたコードは revert（3.307.0 の規律）。3.306.0 適応ポートフォリオの停滞脱出（残差ベースの役割選択＋深さ保持）を既定OFFのトグルで温存＝実データ3件×各4回のA/Bで有意差を検出できず。3.305.0 staffPacked の重みドリフト（c1=4/c3mn=12 のまま）と比較順序（total優先）を修正＝3.249.0/3.253.0/3.287.0 の取り残し。3.304.0 禁止連続の崩し範囲を設定トグルへ配線し実データA/Bで検証（既定OFF維持。ONは人員不足2件を禁止連続2件へ交換する挙動と判明）。3.303.0 禁止連続をパターン全域（前日・当日・翌日）で崩す C3nPolish 新設＋ビット走査＋不採用の主因を全 Polish パスへ水平展開（範囲拡張は実測で利得が一貫せず既定OFF）。3.302.0 研磨の「不採用」に主因の族名を併記＝何に負けて捨てたのかがログから読めるように（C1Polish/RangePolish/C1JointLNS）。3.301.1 3.301.0 の検算を実データ3件で論理検証し、休の判定が旧実装ではほとんど実行されていなかった潜在バグを確認／検査6-C が引数 Problem を無視していたのを是正。3.301.0 目標（適切回数）カードにその場の検算を追加＝「目標の合計 N回 ／ 必要人数 M回 → K回は必ず届きません」と直し方を入力中に表示。判定は設定ミス診断の検査6-C と同じ単一ソース（`V6SanityPort.aptBalances`・盤面不要）。3.214.0〜3.300.0 の詳細は `CLAUDE.md` の各節に記録）／ **コード基準コミット**：main HEAD（この目次が古いと他が正しくても信頼が崩れるため、改修時は対象文書と本目次を必ず更新）。

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
