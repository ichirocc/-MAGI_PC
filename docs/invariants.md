# 不変条件と検証の対応表（INV / T）

外部の統合仕様書 v2（2026-10-05、ユーザー提供）から、**現行コードと一致する部分だけ**を取り込んだ一覧。探索・研磨・S5・UI・保存のどの経路にも適用する。
PR・レビューでは、変更がどの INV に触れるかをこの表で確かめる。数値・重みの正は `MirrorKeys.weights` と `docs/business-logic.md`（この表は値を持たない）。

## 不変条件

| ID | 不変条件 | 主な実装 | 検証 |
|---|---|---|---|
| INV-01 | 希望を自動で取り消さない（取り消しは S5 の利用者操作だけ） | `Problem.wishLocked`、S5 確定 `cancelWishAndRebuild` | T1, T5 |
| INV-02 | 個人の上限0を自動で 1 以上にしない。S6 も画面に出さない（3.620.0 ユーザー決定） | `Problem.mayPlace`、`RELAX_TRIAL_ENABLED=false` | T2, T3 |
| INV-03 | 手動固定セルを自動で変更・解除しない | `Problem.wishLocked`（pin を含む）、`FixApplyGate` | T8 |
| INV-04 | 担当可能な希望の固定セル（`wishLocked`）を通常の探索・研磨で変えない | 各パスの `wishLocked` ガード、最終番兵 | T1 |
| INV-05 | 採否は `betterReport`＝必須件数 → weightedScore → total の辞書順だけ（HARD 内の族別比較は足さない） | `reportComparator` | T7 |
| INV-06 | 必須を減らすために設定値・希望・固定を黙って変えない | 入口 `clearCappedCells` は表示（CapZero 通知）つき | T2, T3 |
| INV-07 | 試算（S5）は state・盤面・Undo・保存へ副作用を持たない | `WishTrial` | T11 |
| INV-08 | S5 の確定は利用者の明示操作だけ。鮮度（state・盤面・希望値）を照合し、古い試算は拒否 | `WishTrialToken` の照合 | T4, T5 |
| INV-09 | 停止・失敗・キャンセルでも入力より悪い結果を採らない | keep-best、`pickBestStage`、`adoptionGate` | T7 |
| INV-10 | 変更セル・必須の前後・残る違反を確定前に説明する | S5 の行・1 手カード・つくる前の確認 | 画面仕様 |
| INV-11 | Kotlin／C++／C# で同じ fixture の評価と固定保護が一致する | native-parity CI、C# の言語跨ぎ期待値 | T12 |
| INV-12 | 重み・閾値・意味を明示の数値指示なしに変えない（HF77） | `MirrorKeys.weights` | T13 |

## 検証

| ID | 内容 | 主なテスト |
|---|---|---|
| T1 | 希望が操作なしに消えない・希望セルを探索が動かさない | `LoopFeatureRegressionTest`（希望固定）、各 Polish の wishLocked テスト |
| T2 | 上限0の (職員,シフト) に最適化器が新しく置かない | `ZeroCapExclusionTest`、`SessionRegressionTest.excludeCapZeroStages_*` |
| T3 | 「上限を緩める」主ボタン・確定が画面に出ない | `RELAX_TRIAL_ENABLED=false`（C# `RelaxTrialEnabled`） |
| T4 | S5 確定後、必須が残れば行き先を示す（完成と言わない） | `cancelWishAndRebuild` の文言（`docs/s5_wish_trial.md` §9） |
| T5 | 盤面・希望・設定が変わった後の古い S5 試算は確定できない | `WishTrial` の鮮度テスト（C# `V2_StaleToken…`） |
| T7 | 最良盤面の報告が正式評価と一致する | `LoopBench` の mismatch 列、`C1EjectionChainPolish.deltaMismatch` |
| T8 | 手動固定が探索で変わらない | `LoopFeatureRegressionTest`（Undo・固定） |
| T9/T10 | 一般画面に内部識別子（c3n・pref・HARD 等）と禁止語（証明つき・下限の見込み・必須の約束…）を出さない | `tools/design_lint.py`、言い換え表（`docs/operator_ux.md` §2） |
| T11 | 試算が state・盤面・Undo・保存を変えない | `WishTrial` の純粋性テスト（C# `V5_TrialDoesNotWriteState`） |
| T12 | 言語・実装間で評価が一致する | native-parity CI、`deltaFamiliesMatchCheckerOnRealData` |
| T13 | 重みが明示指示なしに変わっていない | 言語跨ぎ期待値 3 ファイル |

T6（取込失敗を完了と出さない）は CSV 取込の結果表示が担う（`docs/screen_spec.md` の取込の節）。

## 責務の分離（変更の置き場所）

| 責務 | 内容 | 正本 |
|---|---|---|
| 探索 | 候補を作る | 探索器・各 Polish・LNS |
| 評価 | 違反とスコアを返す（採否はしない） | `UnifiedViolationChecker`／`Evaluator`／`DeltaEvaluator`／C++ |
| 採否 | 採否・停止・固定保護 | `betterReport`・`adoptionGate`・最終番兵 |
| 表示 | 原因・変更・残る違反の説明 | ViewModel／UI（重み・判定を持たない） |
| 保存 | state・盤面・Undo・スナップショット | `RunFiles`・`StateParser`・WorkManager |

## 既知の差分（緑でも解消扱いにしない）

- golden の low が Web 9／Native 8（ホストテストの出力に毎回出る）。parity の比較対象外の経路で、原因は未記録。

## 仕様書のうち取り込まなかった点（現行と食い違う）

- §10 S6 の確定（上限 0→1）: 3.620.0 で画面から外した（INV-02）。
- §4.4・§15「C2Polish は撤去済み」: `C2Polish` は残っている（既定 OFF・`c2PolishEnabled`）。
- §7.1・§8・§15・§16 の DreamSearchEngine／ReplayWorld: この repo の main には無い（ユーザー提供の外部コード）。段契約・Replay の仕様は導入するときに取り込む。
- §11.1「狩猟（要確認）」: 3.480.0 で「未完成」へ改称済み。
- §11.3 S6 の状態（探索中・候補なし…）: S6 を出さないため対象外。
