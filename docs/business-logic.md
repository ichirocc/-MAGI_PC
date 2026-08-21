# business-logic.md — 業務ルール（判定条件・計算・エラー方針の正解）

> **このファイルの役割**：制約の判定条件・スコア計算・エラーハンドリング方針の**唯一の正解**。最もハルシネーションが起きやすい業務ルールをここに集約する。「上限はいくつか」「違反時にどう振る舞うか」はここを見る。
> **コード基準**：`v6/MirrorCore.kt`（`MirrorKeys.weights` ＝重みの単一の真実）／`v6/Evaluator.kt`。
> **最終更新**：2026-08-02（3.345.0 weekly をシフト別へ＝休を通常のシフト種として扱う）

---

## 1. スコアの計算

`weightedScore = Σ (breakdown[key] × weights[key])`。重みは `MirrorKeys.weights`（`linkedMapOf`）が**唯一の真実**で、UI の重み表も最適化器もこのマップを参照する（ドリフト防止）。挿入順 ＝ 加算順（Double 結果を不変に保つため）。

数値が小さいほど良い。**HARD が 0 になれば「配布可（＝完成フェーズ）」**。

---

## 2. 19 種の違反と重み（HARD 4／SOFT 15）

> **keep-best の比較順（3.287.0 統一）**: 候補どうしの優劣は **hard → weightedScore → total** の辞書式
> （単一ソース `MirrorCore.betterReport`）。旧来は第2キーが total（重み無視の生カウント）で、この重み表の
> 階層（low90 > high45 > … > 軽い族1）に反して「重い違反を軽い違反の件数と交換する」採用を許していた。
> SA/ALNS/C++ の評価器 soft は元々この重み表による重み付き和のため、本統一で探索と keep-best が一致した。

| key | 重み | 区分 | 内容（判定） | 場所キー |
|---|---:|---|---|---|
| `groupViol` | 10000 | HARD | 群が就けないシフトを割当（`groupShift` マスク違反） | セル `i,j` |
| `pref` | 9000 | HARD | 希望（`wishes`）に反していない＝未反映 | セル `i,j` |
| `covU` | 8000 | HARD | 人員不足（必要数 lo に対し配置 got が不足、`lo-got`） | 被覆 `k,j` |
| `c3n` | 7000 | HARD | 禁止の連勤パターン（FORBIDDEN）に一致 | セル `i,j` |
| `low` | 90 | SOFT | 個人下限割れ（`staffRange.lo`、LimMin） | 回数 `i,k` |
| `high` | 45 | SOFT | 個人上限超過（`staffRange.hi`、LimMax） | 回数 `i,k` |
| `c3mn` | 30 | SOFT | 回避（Hate）の連勤パターンに一致（12→15→30、2026-08-21 HF77明示指示） | セル `i,j` |
| `c1` | 30 | SOFT | 窓ルール不足（C1）（4→5→15→30、2026-08-21 HF77明示指示） | セル `i,j` |
| `c3` | 3 | SOFT | 必須（MUST）の連勤パターン未充足 | セル `i,j` |
| `c3m` | 2 | SOFT | 推奨（Want）の連勤パターン未充足 | セル `i,j` |
| `c2` | 1 | SOFT | 個人合計の目標差（C2） | 回数 `i,k` |
| `c41` | 1 | SOFT | 群レンジ違反（1日 [l,u] 外） | 被覆/日 |
| `c42` | 1 | SOFT | 群ペア同日併存（**異なる2人のペア数**。同一集合 `g1==g2 && s1==s2` は C(n,2)） | セル/日 |
| `c41s` | 1 | SOFT | スキル群レンジ違反 | 被覆/日 |
| `c42s` | 1 | SOFT | スキル群ペア同日併存（c42 と同じ数え方） | セル/日 |
| `apt` | 1 | SOFT | 適切回数からの L1 偏差 `|n-t|`（群単位の双方向目標） | 回数 `i,k`（aptLow/aptHigh） |
| `fair` | 1 | SOFT | グループ内公平化：群×担当ONシフトで round(平均) からの L1 偏差和 | 職員×シフト（`distLocations["fair"]`） |
| `weekly` | 1 | SOFT | 7日周期のシフト平準化：職員×シフト×曜日で round(そのシフトの回数/7) からの L1 偏差和 | 職員×シフト（`distLocations["weekly"]`） |
| `covO` | 1.0 | SOFT | 過剰な配置（上限 hi 超過、`got-hi`） | 被覆 `k,j` |

> **HARD = {groupViol, c3n, covU, pref}**、それ以外は SOFT。この4族はチェッカー（`MirrorKeys.hard`）と
> 最適化器（`Evaluator.fullEvalParts` の hard1・`DeltaEvaluator`・`magi_native.cpp`）で一致する
> （3.318.0 以前は評価器側だけ groupViol を欠いた3族で、同じ盤面に対して両者の hard が食い違っていた）。`covO` は 2026-07-13（HF77 明示指示）に 0.5→1.0 へ統一済み
> （最適化器 Evaluator/Delta/C++ は元々 1.0、チェッカー `weightedScore` のみ 0.5 だった factor-2 乖離を最適化器基準に解消）。
> `apt` は内訳チップ（`BreakdownCard`「人数の範囲」グループ、`countViolations` の vio-aptLow/vio-aptHigh）に表示、
> `fair`/`weekly` も内訳チップ（「任意」グループ）に件数表示し、いずれもタップで違反箇所（fair=職員×シフト／
> weekly=職員×シフト）へフォーカスできる（`distLocations`、3.149.0／weekly の粒度は 3.345.0 でシフト別へ）。
> 3者とも `weightedScore`/total には常に算入する。

---

## 3. 制約族の意味（cons*）

- **C1（窓）**：`C1Row(day1, shiftKigou, day2)` ＝「day1 日の窓で shiftKigou を day2 回」。SOFT(15)。
- **C2（個人合計）**：`C2Row(shiftKigou, count)` ＝個人の合計目標。SOFT(1)。
- **C3 族（連勤の列パターン）**：`C3Row(pattern: List<String>)`。
  - `cons3` ＝ **MUST**（必須・SOFT 3）／`cons3n` ＝ **FORBIDDEN**（禁止・**HARD 7000**）／`cons3m` ＝ **Want**（推奨・SOFT 2）／`cons3mn` ＝ **Hate**（回避・SOFT 15）。
  - 定義は WS4：MUST=r28c4 / Want=r28c13 / FORBIDDEN=r46c4 / Hate=r46c13。
  - **`ws3`（希望シフト＝`wishes`）と C3 族は別物**。混同しない（希望の採点は `pref`、連勤パターンは c3 系）。
- **被覆（covU/covO）**：シフト×日で必要数に対する過不足。`use2Patterns` 時の P1/P2 は **MIN=OR**（緩い方で充足）＝加算ではない（中間世代 MWS 由来の意図的設計）。被覆は同日のみ（夜勤の翌日繰越なし）。
- **群レンジ/ペア（C41/C42）**と**スキル群版（C41s/C42s）**：`C41Row(groupKigou, shiftKigou, l, u)` ＝1日に [l,u] 回／`C42Row(g1,g2,s1,s2)` ＝群g1のs1と群g2のs2が同日併存不可。
- **個人下限/上限（low/high）**：`staffRange["i,k"]={lo,hi}`。`low` は `lo!=0 && 担当可 && 回数<lo`、`high` は `回数>hi`。重み 90/45（被覆や c1 に負けない最低限の重み）。
- **適切回数（apt）**：群目標を個人 `staffRange[lo,hi]` でクランプし、固定職員の解消不能な幻 apt を除去。
- **グループ内公平化（fair）**：群×担当ONシフトで、メンバー回数の round(平均) からの L1 偏差和（m<2 の群は対象外）。同群職員間で各シフト回数を均す。
- **7日周期のシフト平準化（weekly）**：職員ごと**シフトごと**に、そのシフトが入る日の**曜日別カウント**の round(そのシフトの回数/7) からの L1 偏差和。`weekday(j)=(dow0+j)%7`（`Problem.dow0`）。「夜勤が毎週おなじ曜日」「休みが毎週おなじ曜日」を同じ式で均す。fair と同型・重み1。
  **休は通常のシフト種の一つ**として数える（3.345.0。旧定義は勤務日=非休の二値で、休を「特殊なOFF」として区別していた）。
  回数が7の倍数でない (職員,シフト) は偏差が0にならない＝構造的な下限を持つ（実データ3件で全体の44〜60%）。
- **希望（pref）/群マスク（groupViol）**：上表のとおり HARD。

---

## 4. エラー・不能・中断の扱い

- **構造的不能（infeasible）**：供給<需要などで covU=0 が物理的に到達不能な場合、配布前に数学的に診断する（`coverageDiag`）。UI は **「充足不可」**（枠自体が足りない）と **「充足可能」**（枠は足りるが最適化が未到達）を切り分けて表示。例：供給153 vs 需要155＝構造的不足 -2。
- **実現できない希望**：担当外シフトへの希望など。`impossibleWishCount` として配布前に見直しを促す。
- **中断復帰**：最適化中にプロセス kill 等で中断された場合 `interruptedRun` を立て、スナップショットから続きを復帰できる（前景サービス＋自動保存）。
- **HARD>0 のまま書き出し**は可能（「未充足のまま書き出す」）。判断は利用者に委ねる。

---

## 5. 変更規律（**最重要・HF77**）

> **重み・パラメータ・データ値は、利用者の明示的な数値指示が無い限り変更しない。**
> ツールやUIを作るのは可。値（weights/閾値/データ）の調整は HF77 の対象で、独自判断での変更・一括変更は禁止。変更は1件ずつ＋A/B検証。

過去の教訓（要点）：
- SOFT 重みの一括倍化は Strategic Oscillation 等の動的重み機構と相互作用して HARD を壊しうる（HF203/204）。重み変更は1件ずつ＋SO相互作用を考慮。
- `cons3mn`(Hate/SOFT) を `cons3n`(FORBIDDEN/HARD) に格上げすると、担当可否との衝突で HARD=0 が構造的に不能化しうる。FORBIDDEN 格上げ前にシフト可否との衝突を必ず確認。
- 専門職の偏在（特定シフトに就ける職員が少ない）は最適化では解決不能。`ws1` で担当可能職員を増やすクロストレーニングが根本解。

---

## 6. Level Zero 不変条件（壊さない）
ws1–ws7 レイアウト／`RW_N1=46`／`RW_N2=56`／モジュール構成は不変（VBA 系の正典）。Android はこの仕様を踏襲する。詳細は memory / VBA 仕様参照。

関連：データ定義は [`data-models.md`](./data-models.md)、設計は [`design.md`](./design.md)、画面挙動は [`screen_spec.md`](./screen_spec.md)。
