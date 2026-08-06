# Kotlin と C++ の棲み分け

> 最終更新: 3.367.0 / 対象コミット: `dc104cc` 時点のコードと実測値。
>
> **この文書に書くのは実装済みの事実と実測値だけ。** 構想・提案は末尾の「移植しないと決めた項目」か
> `docs/algorithm_portfolio.md` の「未実施の提案」へ置く。ここが実装からずれると
> 「C++ に無い関数を C++ にあると思って直す」事故が起きる。

---

## 1. 判断規則（新しいコードをどちらに置くか）

1つの問いで決まる。**そのコードは `UnifiedViolationChecker` の `weightedScore` / `breakdown` を読むか。**

- **読む** → **Kotlin**。例外なし。チェッカーが正（source of truth）であり、C++ へ複製すると
  第2の意味論ができて必ずドリフトする。研磨パス・診断・採否ゲートはすべてここ。
- **読まない**、かつ **反復回数が百万回オーダー** → **C++ の候補**。ただし2層番兵（§5）を必ず付ける。
- **読まない**が反復が数回〜数千回（ラウンド境界の制御層・後処理チェーン） → **Kotlin**。
  移す価値が実測で出ない（§4）。

補足: 「差分評価を速くしたいから C++ へ」は**理由にならない**。同じ仕事なら Kotlin と C++ scalar は
実測で同速で、差はビット化から来る（§4）。

---

## 2. いま C++ にあるもの（実測インベントリ）

`app/src/main/cpp/magi_native.cpp` **2,285行**（C++ ソースはこの1ファイルのみ）。
対して Kotlin の `v6` パッケージは **21,932行**。C++ はエンジン全体の約10%にあたる最内周だけ。

### JNI 境界＝`NativeBridge.kt` の `external fun` **17個**（`ABI_VERSION = 7`）

| 群 | 関数 | 役割 |
|---|---|---|
| 疎通 | `nativeAbiVersion` | Kotlin 側の `ABI_VERSION` と照合。不一致なら `available=false` |
| 問題 | `nativeCreateProblem` / `nativeDestroyProblem` | 平坦化した `Problem` を1回だけ渡してハンドル化。読み取り専用・全ワーカーで共有 |
| 評価 | `nativeFullEval` | フル評価（`hard`/`soft` の2値）。実行時パリティ照合に使う |
| SA | `nativeSaChunk` | 冷却ラダー1本＝1チャンク |
| ALNS | `nativeAlnsCreate` / `Chunk` / `Read` / `Destroy` | GLS・適応重み・温度をチャンク跨ぎで保持 |
| 研磨 | `nativePolishCreate` / `Chunk` / `Read` / `Destroy` | HF80 相当の11オペ |
| LAHC | `nativeLahcCreate` / `Chunk` / `Read` / `Destroy` | PhaseB（履歴受理＋HARDガード） |

### C++ の内部構造

- `MagiProblem` — 平坦配列で受けた制約データ。**`mutable` メンバ0・`const_cast` 0・関数内 `static` 0・
  可変グローバル0**（3.364.0 で静的に確認し、8スレッド同時実行が逐次とビット一致することと
  ThreadSanitizer 警告0で実行検証済み）。
- `fullEvalParts` — フル評価。**スカラーのまま**（番兵のオラクルなのでビット化しない）。
- `SaChunk` — 差分評価の中核。`ssn`/`dsn`/`wd`/`rowMask`/`dayShiftMask` を増分維持。
  `S<=64 && T<=64` のとき c1窓・c41/c42系・c3窓マッチを popcount 化（3.172.0/3.174.0）。
  業務前提は30名・31日なのでこの経路が常用。
- `runSaChunk` / `runLahcChunk` / `runAlnsChunk` / `runPolishChunk` — 4つのランナー。
  いずれも同じ `SaChunk` を使う＝差分評価の実装は1つ。
- 修復系 — `destroyRepairDayAtN` / `StaffAtN` / `ViolationsN` / `hf67HardRepairN` / `findTargetedFixN` /
  `collectViolationCells` / `GlsPenaltyN`。**チェッカーではなく Evaluator と同じ重みの
  marginal cost** で候補を選ぶ（候補生成であって採否ではない）。

---

## 3. Kotlin が持つもの（移さない理由つき）

### `UnifiedViolationChecker`（`MirrorCore.kt`）＝正

- Kotlin の呼び出しは **167箇所**。C++ に `weightedScore` / `breakdown` は **0箇所**。
- UI の違反表示・修復提案・全研磨パスの採否・診断がすべてこれを読む。
- **移さない理由**: ①チェッカーが正という設計そのものが番兵の前提（C++ にも同じものがあると
  「どちらが正か」が決まらない）②戻り値 `ViolationReport` は `violations` / `needViolations` /
  `countViolations` / `cellFamilies` / `distLocations` / `breakdown` の**マップ束**で、
  JNI 越しに毎回組み立てると実測5〜7%の CPU に対して整列コストのほうが大きい。

### 研磨パス — `V6HotfixPasses.kt` に **21本**、独立ファイルに **4本**

`applyC1WindowPolish` / `applyC1BeamPolish` / `applyC1ExactWindowRepair` / `applyC1IndexChainRepair` /
`applyRangePolish` / `applyAptPolish` / `applyFairPolish` / `applyC3RunPolish` / `applyC3PatternPolish` /
`applyC3mnPolish` / `applyC3nPolish` / `applyC3SequencePolish` / `applyCyclicSwapPolish` /
`applyBlockRotationPolish` / `applyAdaptiveBlockSwapPolish` / `applyWeeklyRebalancePolish` /
`applyDayAssignmentPolish` / `applyAlternatingSoftPolish` / `applyHF66IntraStaffRedistribution` /
`applyHF67InterStaffSwap` / `applyHF80StrategicOscillation`、および
`C1JointLnsPolish` / `PersonalBalanceJointLnsPolish` / `C1TemporalFlowPolish` / `EliteIntegrationPolish`。

すべて **候補を作る → チェッカーで実評価 → `betterReport` ＋ `exactPinRegression` で採否** の形。
採否がチェッカー依存なので定義上 Kotlin 側。

### 診断（すべて Kotlin のみ・C++ 実装0）

`ForbiddenRunDiagnosis`(3.280.0) / `CoverageDiagnosis` / `C1RepairAnalysis` / `ConstraintMus`(3.272.0) /
`V6SanityPort.buildGuidance`。読み取り専用で、実行時間に占める割合は無視できる。

### 制御層

`V6FinalPort`（予算配分・ウォッチドッグ・最終番兵）、`V6NativeOptimizer`（仮説ポートフォリオ・
RSI focus 選択・HF63 学習）、`V6LateOperators`（ラウンド境界の後段オペ）。
ラウンド境界で O(数回〜数千回) しか走らない。

---

## 4. 境界を支える実測値

### 差分評価のスループット（3.366.0・同一データ golden_state・同一マシン・20M手×各3回・`nice -n 19`）

| 実装 | 中央値 |
|---|---|
| C++ scalar | 0.60 M手/s |
| **Kotlin `DeltaEvaluator.apply`** | **0.58 M手/s** |
| C++ bit-op | 1.17 M手/s |

**Kotlin と C++ scalar は差3%＝ほぼ同等。2倍の差はビット化（popcount）から来る**（Kotlin 側に
`bitCount`/`rowMask`/`dayShiftMask` は0件）。つまり第3期ネイティブ移行の実質的な利得は
「C++ にしたこと」ではなく「ビット化＋番兵つきチャンク化」だった、という読み替えになる。

- **限界**: x86-64 ホストでの数字。実機は arm64 で ART は HotSpot C2 より最適化が弱いため、
  実機では C++ 有利へ振れる**はず**（推測）。JNI 往復は両者とも含まない。
- **C# は測っていない**（この環境に処理系が無い）。Android アプリなので選択肢でもない。

### チェッカーのコスト

実行時間の **5〜7%**。移植しても上限がこの範囲で、しかも §3 の整列コストが相殺する。

### 移植しなかった箇所のコスト（3.153.0 の実測）

`V6LateOperators` ≈ 100ms/実行、後処理チェーン 985〜1,328ms。合わせて300秒予算の **約0.5%**。

---

## 5. 壊してはいけない不変条件

1. **2層番兵** — `NativeGate.disable(...)` の発火点は Kotlin 側 **11箇所**
   - ①**C++ 内の自己整合**（4箇所: SA / LAHC / ALNS / 研磨）。チャンク末尾に `fullEvalParts` と
     照合し、食い違えば `status != 0` を返す
   - ②**Kotlin 照合**（5箇所: 上記4チャンク＋起動時のフル評価）。`Evaluator.fullEval` で `Long ==` 比較
   - ③ハンドル生成失敗（2箇所: ALNS / LAHC 状態生成）
   - どれが発火しても `NativeGate.enabled = false` でそのプロセスは Kotlin へ退化する。
     **クラッシュさせず・誤った勤務表も出さず・遅くなるだけ**。
   - `NativeGate.parityCheckEnabled`（既定 ON）は②のチャンク側4箇所を切る検証用トグル。①③は常時 ON。

2. **言語跨ぎパリティを CI が守る**（3.357.0）
   `app/src/test/resources/golden_eval_expected.txt`（`hard=0 / soft=3109`）を
   Kotlin テスト `NativeParityFixtureTest` と C++ harness の `--expect=` の**両側から**固定する。
   片側だけ変えれば必ずどちらかが落ちる。
   重みや族の定義を意図して変えるときは **Kotlin と C++ の両方を直してから**このファイルを更新する。

3. **共有ハンドルは読み取り専用**（3.364.0）
   `SaOptimizer.run` は `nativeCreateProblem` を1本だけ作り最大8ワーカーで共有する。
   これが安全なのは `MagiProblem` に可変状態が無いから（§2）。**書き込み可能なメンバを足した瞬間に
   この設計が壊れる。** 破棄は 3.289.0 で「全ワーカーを cancel+join してから destroy」に修正済み。

4. **ホストビルド可能に保つ**
   JNI 部は `#ifndef MAGI_HOST_TEST` で囲み、`tools/native/host_parity_bench.cpp` が
   同じ `.cpp` を `#include` して g++ だけでビルド・実行できる状態を維持する
   （`.github/workflows/native-parity.yml` が PR ごとに走る）。

5. **`normalizeSchedule` の -1 センチネル**
   範囲外セルは -1 に写像される。**C++ 側で盤面値を配列添字に使う箇所は必ず範囲検証する**
   （3.199.0 で `deltaApply` の未検証がヒープ破壊を起こしていた）。

---

## 6. 移植しないと決めた項目（再提案しない）

| 対象 | 決定 | 根拠 |
|---|---|---|
| `UnifiedViolationChecker` | Kotlin のまま | チェッカーが正＝番兵の前提。実測5〜7%に対しマップ束の JNI 整列が相殺 |
| `V6LateOperators`（Stage12） | 移植しない（3.153.0） | 300秒予算の約0.5%。採否ゲートが `breakdown` 依存 |
| 後処理チェーン（Stage13） | 移植しない（3.153.0） | 同上。0.5%のために安全アーキテクチャを崩さない |
| 21+4本の研磨パス | Kotlin のまま | 採否がチェッカー依存 |
| 5つの診断 | Kotlin のまま | 読み取り専用・実行時間に占める割合が無視できる |
| `fullEvalParts` のビット化 | しない | 2層番兵のオラクル。速いほうを正にしない |

**逆に、まだ測っていない選択肢**（実施するなら A/B が要る）: Kotlin 側 `DeltaEvaluator` の
ビット化。`java.lang.Long.bitCount` は JIT が POPCNT へ落とすので同じ2倍が取れる**見込み**だが、
**測っていないので見込み**。既定経路はネイティブなので効くのは番兵発火後の退化経路だけになる。

---

## 関連

- `docs/algorithm_portfolio.md` — どの手がどこで走るかの台帳
- `docs/business-logic.md` — 重み19種と判定条件（**業務ルールの正解**）
- `CLAUDE.md` の「ネイティブ加速」各節 — Stage ごとの移植記録と実測値
