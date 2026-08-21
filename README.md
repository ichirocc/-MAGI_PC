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
| [`docs/sudo_model.md`](./docs/sudo_model.md) | **SUDO モデル**（S 関連図／U ユースケース／D ドメイン／O オブジェクト。実装から起こした全体像。D の不変条件と O の実測値つき） |
| [`docs/lessons.md`](./docs/lessons.md) | **教訓メモ**（修正した点↔機能した点・作る前にやめた判断・測り方・検証手段の穴。新規作成せず更新する） |
| [`CLAUDE.md`](./CLAUDE.md) | 引き継ぎ・直近の状態・作業の進め方（grilling 等） |

**最終更新**：2026-08-21（3.409.25 トグル A/B の第2ラウンドを 240s で測り切った——立て直し方は
24ペア通算 ON7/OFF17（p≈0.032）で **OFF が有意に良い**＝削除は実測に支持されていた／ロール内並列SA は
6ペア ON3/OFF3 で事前バーに届かず削除確定（ただし第1ラウンドの「反復希釈」は 240s では再現せず、根拠を
中立の方へ置き直した）／崩し範囲は 9ペア ON5/OFF4 で既定OFF据え置き。3.409.24 回避の並び(c3mn)と窓の要件(c1)の重みを 15→30（HF77 明示指示）——
`MirrorKeys`/`Evaluator`/`DeltaEvaluator`/`magi_native.cpp` の4面と言語跨ぎ期待値3ファイルを同時更新
（golden soft 3109→4999・sample_v6 825→930・blocked_covu 1681→2731・hard は不変）。
併せて重み変更が露呈させた C1広域ビームの実バグを修正——`bestEver` にピン検査が無く、あとから来た
ピンを崩す候補が先に見つけたピン安全な改善を追い出していた。3.409.23 並列監査の残り2件——c1 壁判定の非休側が「上限未設定＝上限0」と読み、
一部の日だけ need を設定したシフトで不足量が過大に出ていた（前提が崩れているので案内しない形へ）／
`contribDayGroups` のビット経路が制約 index を無検査で、天井は2層番兵で捕捉できない SIGSEGV＝
`nativeCreateProblem` の入口で一括拒否（判定は harness から叩ける `consIndicesValidN` として JNI の外へ）。
3.409.22 ネイティブ修復器が Kotlin から5世代ぶん取り残されていた——
`magi_native.cpp` の destroy-repair / findCovOFix が 3.266.0(同点抽選)・3.267.0(weekly/fair 費用)・
3.319.0(canDo ガード)・3.369.0/3.379.0(`covUCell`/`covOCell` への委譲) を1つも反映しておらず、
**ネイティブ既定 ON なので直したはずの規則が既定経路では効いていなかった**。2層番兵も
native-parity CI も**スコアの一致**しか見ないため、候補生成の乖離は構造的に網から外れる
（harness に修復器の直接テストを2本追加してこの穴を塞いだ）。事前診断(V6SanityPort)の9箇所も
同じ `need1` 直読みを実効値へ委譲。3.409.21 既定OFFトグル2つを単体 A/B で測って削除——
`portfolioRoleParallelSa`（ロール内並列SA）は 15ペアで ON7/OFF8＝中立、しかも ON は反復数中央値が
2/3データセットで**低い**＝チェーン分割が希釈になっていた（動機だった「1本=遊休」仮説の反証）。
`adaptiveEscapeControl`（停滞脱出の適応制御）は ON5/OFF10＝3.306.0 に続く2度目の中立。
「見直しの条件」（3.384.0）の事前約束どおり**機構ごと削除**（規律7＝反証されたコードは残さない）。
経緯と数値は `docs/algorithm_portfolio.md`「廃止・統合済み」。3.409.20 C41/C42/C41s/C42s の説明を
別々の制約として書き分け（分類の明文化・兄弟の名指し・テストで固定）。3.409.19 /design-review で
自分の 11sp を 12sp へ是正。3.409.18 ペア禁止の説明を人間に分かる形へ——「禁止/不可」が実態（最軽量の
ソフト条件）と逆の約束をしていたのを見出しで正直化・ⓘへ「向き」（違うシフトの組は鏡の2行が必要）を追記・
ルール行に読み下し文・スキル群が「いま効いていない」ことの状態表示。根拠は実機で2回聞き返されたこと自体。
3.409.17 2本目の実機ログで**予算300秒の実行が13回中5回、474〜959秒まで超過**
しているのを発見。W4 epoch3 の最良更新が経過474sに出た＝ロールが停止確認を数百秒無視した証拠。手元の
実データでは再現せず（120s/300s とも予算内）＝9月データ固有。原因特定の代わりに観測を入れた——
エポック超過の役割名つき検出 `[W] エポック超過` と、予算超過の実行の内訳(TIME/エポック超過/後処理パス別)を
**操作ログへ写す**（診断ログは最後の実行しか残らず、今回まさに5回分の内訳が全部消えていた）。
3.409.16 実機ログ(3.409.14搭載機)の検分＝表示の自己矛盾3件を修正——
「探索締切」の正常終了を「締切前離脱」と数える集計／「設定の効き:」の二重ラベル／C1広域ビームの
採用ログに weighted が無く正しい取引が退行に見える。実機では adaptiveEscapeControl の初観測(91回)・
portfolioRoleParallelSa の配線実動作ほかを確認。以下は前回まで: 3.409.5〜3.409.7 デザイン lint のラチェットを実際に効かせる。3.409.5 で
P2「生 hex」/P4「任意角丸」が**baseline を記録しておらず、増えても exit 0 で静かに通っていた**のを
ラチェット化（`docs/DESIGN.md` §4 が「禁止事項（machine-checkable）」と呼ぶのに強制されていなかった）。
3.409.6 で据え置いていた13件を `MagiTheme` の tier へ寄せて **baseline を 0/0**＝以後は1件でも増えたら
落ちる（8dp→`extraSmall`・16dp→`large`。**視覚は +2dp 変わる**）。あわせて「落ちる検査を1つ報告して
止まる」欠陥も修正。3.409.7 で `breakdownLabels`（族→日本語名）を Compose 非依存ファイルへ切り出し、
`MirrorKeys.all` と過不足なく一致することをテストで固定＝族を足して書き忘れると**生の英字キーが画面に
出る**のを機械で止める（3.382.0 の `vioBuckets` と同じ形）。3.409.8 で呼出0の関数6つを撤去し、
**`cycleCell` が初回インポート以来一度も呼ばれていない**＝CLAUDE.md が3世代にわたり書いてきた
「編集の4入口」は実際には**3つ**だったと確定（死んだ関数を守ってもガードの網羅は証明されない）。
3.409.9 で提示レビュー P0-P3 を照合＝**P0 の名指し（`adafe28`/`rAiPriority`）はこの
リポジトリに存在しない**が、同じ欠陥クラスを機械で数えて**広域ビームのピン却下が終端集計へ
合流していない**実在の1件を発見・修正（ただし実測すると全ラウンド attempts=0＝構造的な欠落で
あって、この3データでは1件も落としていなかった）。P1 として停止伝播の回帰テストを新設。
3.409.10 で 3.409.8 が「理由つきで残した」3つを、**残した理由そのものを検証してから**片付けた
（λ上限は `gLam` 不在＋辞書式パックにより**書かれたままでは配線できない**と確定して撤去／
未採用の `MagiSectionHeader` を撤去／**残す根拠にしたデザインシステムの ✅ が実在と食い違っていた**ため
3件を訂正し `design_lint` P8 で機械検査化）。3.409.11 で判断待ち4件を決着＝**c1 を破線側へ昇格**
（3.367.0 の「最多件数だから飽和する」判断は **fire 数**で見ており、この表示が扱うのは**セル数**＝
単位が違った。実測でセル総数310に対し破線は 20→42／11→28／0→5 で 3.99.0 の飽和 194 には遠い）。
グループ上下限の一括解除が**何名ぶん消えたか画面に出ていなかった**のを Snackbar へ、最後の1シフト・
1職員の削除ボタンが**理由なく消える**のを 3.400.0 のグループと同じ形で説明に。3.409.12 で「この環境では
実行できない」と記録していた項目を検証＝**2件は前提が誤っていた**。①実機スクショ起因: 最適化の実行中でも
編集タブの追加/編集/削除が押せ、**入力し終えてから拒否**していた（3.405.0 の対象漏れ・13サイトを無効化）
②C++ に停止確認は元から**1つも無い**（grep 0件）＝確かめるべきは「止まるか」でなく「止まるまで回り続ける
長さ」で、実測 **1チャンク4.7ms・最悪257ms** ③P2 の Robolectric 試験は、実際に起きた回帰2件がどちらも
呼び出し側の書き忘れなので**機械検査 P9**（begin と finally の end が対）へ置換——注入試験で **P9 自身の
欠陥を2つ**（`io` 未 import を広い except が飲み込む／同名変数で隣の関数の解放を拾う）出荷前に発見・修正
④compileSdk 37 は **stable 公開済み**と確認＝3.173.0/3.373.0 の「未公開」は解消（AGP 互換の確認は残る）。
3.409.14 で実機の「条件の意味は？」を受け、制約10族の**詳しい説明を既定で閉じた ⓘ 展開**としてアプリへ
（本文は Compose 非依存の表＝MagiState の cons* と過不足なく一致することをテストで固定・重みの数値は
書かない＝HF77 で stale 化しないため）。3.409.15 で backlog#6 の残り＝「covU が構造床を超えて
blocked-now」な実データ形状を、**職員名だけ匿名化**（評価は bit 一致を実測）して第3フィクスチャへ
（言語跨ぎ照合3本目＋形状の回帰テスト＋匿名化規約を守らせる防具つき）。
3.409.13 で /code-review 7件を全修正＝**前日「注入で2欠陥を潰した」と書いた P9 自身にまだ3つの穴**
（裸呼び出し不可視・判定が生テキストの語検索・1行 try-finally の偽陽性）→ ブロック所属ベースへ作り直し
（注入8ケース＝欠陥4発火・正当4素通り）。行本文タップ・スキル▼・担当可否チップの実行中ガード漏れと、
`ui.running` の stale-false 経路（背景 StateFlow を写しへ反映）も修正。ホストJVM 508テスト green。）

（以下は過去分）3.377.0 実機ログ起因の診断バグ2件。①**`残存分析` が「もう直せない covU」を
「まだ狙える」に入れていた**＝covU の構造判定が `hardFloor`（有資格者数ベースの静的下限）しか見ておらず、
同じ実行の `CoverageDiag`（「いまの希望のままでは不能」）と設定ミス診断（同じ2日を「証明つき」で名指し）と
食い違っていた。3.344.0 の `CoverageDiagnosis.blockedNow` を単一ソースとして読む形へ（**3.361.0 の実測＝
早期終了への配線は却下、はそのまま維持**＝表示だけ）。②**`Watchdog` 行が2つの時間軸を混ぜていた**＝
「最終改善=経過287s・**探索終了時**の停滞0s」なのに探索は274sで終了。ExtraRefine の改善が
`lastBestImproveMs` を進めるため負→0 に丸められていた。探索終了時点のスナップショットへ統一し、
探索後の改善は別項目で出す。ホストJVM **470テスト green**（新規1・教訓#30 の scratch-revert 確認済み）。
読み取り専用・表示のみ＝重み・採否・探索・停止条件は完全に不変。3.371.0 ユーザー指示「並列SAの本格再有効化する」「soft全族の完全差分する」の2件。
①**並列SAの本格再有効化**＝`hypothesisChainPlan`は`hypotheses==workers`（3.224.0以降の常態）だと
`distributable=max(h,min(workers,cores))`が構造的に常に`h`へ収束し、`runMultiWorker`（ALNS/RSI/RSI_PLUS
明示選択の経路）の仮説内チェーン本数が実機コア数に関わらず常に1本に潰れることを数式で特定。新設
`hypothesisSpawnPlan(workers,w,cores)`が`workers>cores`のときだけ仮説本数を`max(2,min(w,cores))`へ
畳んで余剰をチェーン深さへ再配分（`workers`上限は`clampWorkersToCores`の先例どおり厳守）。**さらに実機ログ
2本（Pixel 10 Pro XL・8コア・workers=8）で PORTFOLIO（既定211秒以上の主経路）は`roleOptions.workers=1`が
無条件固定と確認**＝希釈リスクの無いworkers==coresでもSAチェーン1本のまま。430行の`runAdaptivePortfolio`を
under-testedなまま作り替えるリスクを避け、`PolishGate.portfolioRoleParallelSa`（既定OFF・未測定のため
実機検証用トグル、設定タブ「詳細設定」に配線）で明示オプトイン化。②**soft全族の完全差分**＝
`DeltaEvaluator`の既存検証はaggregate（`sc1+sc2+...`合算値）のみで、2族が同じ重み（c1=15=c3mn等）だと
片方の誤りをもう片方が相殺しうる盲点を発見。`familyRaw()`/`rangeWeighted()`を新設し、`MirrorKeys.all`の
19族すべてを個別にcheckerの`report.breakdown`と突合するテストを追加。**教訓#30を実践**＝scratch限定で
`sc1`/`sc3mn`を意図的に取り違えるバグを注入し、新テストだけが単独で落ちる(`family=c1 expected:<2> but
was:<0>`)ことを確認してから実コミットへ反映（scratch側は検証後に削除・repo本体は無傷）。ホストJVM実行で
main+test 47+61ファイルを再コンパイルし**457テストgreen**（新規4件、既知の false positive
`EliteIntegrationRandomSafetyTest`1件を除く）。エンジン重み・探索・keep-best採否ロジックは完全に不変
（並列度の配線とテストの追加のみ）。3.370.0 ユーザー指示「同様な問題などあるか?」＝外部レポート（別コードベース確定済み）の
カテゴリ名だけを写して監査し実在2件を発見・修正。**`breakdownLocations`（内訳→場所タップ）が3キー空間
（セル/回数/被覆）とも単一クラスの生マップを直接フィルタ**しており、重い違反(covU8000/low90等)と同じセルの
軽い違反(c41/apt/c2等)が場所一覧から消える表示バグ＝3.111.0/3.353.0の`cellFamilies`/`countFamilies`は
checker層で解決済みだったのに消費者(`breakdownLocations`)へ一度も配線されておらず、`needViolations`
（covU/covO/c41系の被覆キー空間）には兄弟union自体が存在しなかった（第3のキー空間で同型の穴が未対応）。
`MirrorCore`に`needFamilies`新設・`UiState`/`makeUi`へ配線・`breakdownLocations`4分岐を`*Families`優先へ
書き換え。ホストJVM実行で452テストgreen（新規1件）。あわせて**CIのGradleダウンロードが3ワークフローとも
リトライ・キャッシュなし**という実在の gap を発見し`actions/cache`+`wget --tries=3`の多重防御を追加
（編集中に`name:`行の孤立という自己ミスをYAML構文検証で発見・是正）。3.369.0 ユーザー指示「すべてのフルコードを/code-review する」＝約35,000行の
Kotlin/C++全体をインライン単一パス精査。**need2単独定義セル見落としの第3世代を発見・修正**＝`covUCell`/
`covOCell`（3.173.0/3.309.0で確立済みのsource of truth）を経由せず生の`need1`だけを見る箇所が
`SmartInitialScheduler.kt`(demand-fill/残り埋め)・`GreedyMirrorScheduler.kt`(同型)・
`V6SearchOperators.findCovOFix`の4箇所に残存＝初期解生成がneed2のみ定義の需要/上限を見落としcovU(HARD)
違反を残しうる不具合。全箇所をsource of truthへ統一。`C1TemporalDp`のRELOC_BITS未検証（現状到達不能だが
latent、3.213.0のSCORE_HARD_UNIT検証と同型の精神で防御的ガード追加）も修正。ホストJVM実行で main+test
61ファイルをコンパイル・**451テストgreen**（新規3件）。教訓#30実践＝SmartInitialSchedulerの修正をscratch
コピーでのみ一時revertし新規テストが単独で落ちる(expected:0 but was:2)ことを確認してから復元。エンジン
評価器本体(Checker/Evaluator/DeltaEvaluator/native parity)は既存の大量の敵対的レビュー履歴で堅牢化済みで
新規欠陥は0。3.368.0 族数「18種」の docs 取り残しを**19種**へ横断修正＝`MirrorKeys.all` は19族(weekly が19番目)なのに 3.202.0 が business-logic.md だけ直し data-models/overview/requirements/magi_design_system/screen_spec が18のまま取り残されていた（screen_spec:199 の族列挙は weekly が抜け）。コード1コメント（MagiScheduleViews:354 E7 バケツ=実17族＝fair/weekly 除外）も是正。docs＋コメントのみ・ロジック不変。3.367.0 sibling-bug 掃討を**重み定数コメント**へ拡張＝c3mn(12)/c1(4)/covO(0.5) の旧値が残る現行記述コメント（MagiScheduleViews/V6WebCompat/V6HotfixPasses）を現行値へ訂正（3ファイル/コメントのみ/分類コード不変）。あわせて c1 が重み 4→15 に上がったのに表示分類（グリッド heavySoftFamilies・凡例 severityFromVioKey）が非 heavy のままな**設計上の緊張**を発見＝どちらも c1 を非 heavy で一貫・視覚不変（凡例は HIGH/WARN を下流で同一表示に畳む・グリッドは c1=最多件数族で飽和回避）のため分類は据え置き、severity-match を優先した c1 破線昇格は一行変更で可能な**判断点**としてコメント化。3.366.0 外部 fork レポート L1-L10 の周辺検証＝L5「better の順序」・L9「正規化を通さない raw Move」の実在アナログを精読で両方否定（実装は全て `better()`=`reportComparator`=hard→weightedScore→total で正しい・chain の手は必ず keep-best ゲート）。実在した唯一の項目＝keep-best 順序を旧 `hard→total→weight` で書いた**現行記述コメント12箇所を訂正**（3.287.0 の統一取り残し・9ファイル/コメントのみ・コンパイル/テスト/スコア不変。歴史記述と正しい移行説明は温存）。3.365.0 別ブランチ x8ygvy を精査し**共有ネイティブハンドルの並列安全テスト**（923bf07）だけを選択的に移植＝8スレッドが同一 MagiProblem で逐次と bit 一致を実行で証明・既存 native-parity CI で自動実行（`-pthread` 追加）。covU 早期終了系（このセッションで real3 A/B により有害実証・却下）は取り込まず。3.364.0 c1「壁」判定(検査2b-2)の need2 依存を実データ計測で **false wall と確定**し正直化＝非休の c1 窓は物理供給≥需要が常に成立し構造的不能にならない（golden Dﾃ は物理供給248>>需要32・手作り盤面は既に35回配置）ため「構造的に残ります」を撤回し covO-tension として案内。休のみ真の壁を維持。read-only・スコア不変。backlog#4 解消・3.179.0 の据え置き前提を反証。3.363.0 直近コード（3.352-3.360）を sibling-bug 狙いで焦点レビュー＝**実バグ0**（keep-best 比較の集約は完全・診断の算術も正しい・environmentLine/telemetry も健全）。あわせて 3.95.0 の stale fact「golden 構造的covU=2」を実測（structuralHardFloor=0）で反証・訂正／docs のみ。3.362.0 パリティネット（言語跨ぎ・3.357.0）へ**2つ目の実データ形状 sample_v6**（入力盤面 hard=15＝groupViol/c3n/pref/covU 発火）を追加＝golden(hard=0)では未 exercise の C++ HARD族パスを実データで照合。`host_parity_bench.cpp` の `--expect` を flat と出現順で対応づけ1回のベンチで両形状を照合／test・CIのみ・エンジン不変（backlog#6）。3.361.0 残作業 #1「covU-blocked をウォッチドッグへ配線し早期終了」を**実測のうえ却下**＝早期終了は keep-best-safe でない（探索を止めると未見の改善を諦める・keep-best では回収不能）／sample_v6 が「22秒停滞後の soft バースト改善」の実在を示す／動機データ real/user 消失＋golden は hard=0 到達で no-op のため直接 A/B 不可＝反証されたコードは revert（3.307.0 の規律）。3.306.0 適応ポートフォリオの停滞脱出（残差ベースの役割選択＋深さ保持）を既定OFFのトグルで温存＝実データ3件×各4回のA/Bで有意差を検出できず。3.305.0 staffPacked の重みドリフト（c1=4/c3mn=12 のまま）と比較順序（total優先）を修正＝3.249.0/3.253.0/3.287.0 の取り残し。3.304.0 禁止連続の崩し範囲を設定トグルへ配線し実データA/Bで検証（既定OFF維持。ONは人員不足2件を禁止連続2件へ交換する挙動と判明）。3.303.0 禁止連続をパターン全域（前日・当日・翌日）で崩す C3nPolish 新設＋ビット走査＋不採用の主因を全 Polish パスへ水平展開（範囲拡張は実測で利得が一貫せず既定OFF）。3.302.0 研磨の「不採用」に主因の族名を併記＝何に負けて捨てたのかがログから読めるように（C1Polish/RangePolish/C1JointLNS）。3.301.1 3.301.0 の検算を実データ3件で論理検証し、休の判定が旧実装ではほとんど実行されていなかった潜在バグを確認／検査6-C が引数 Problem を無視していたのを是正。3.301.0 目標（適切回数）カードにその場の検算を追加＝「目標の合計 N回 ／ 必要人数 M回 → K回は必ず届きません」と直し方を入力中に表示。判定は設定ミス診断の検査6-C と同じ単一ソース（`V6SanityPort.aptBalances`・盤面不要）。3.214.0〜3.300.0 の詳細は `CLAUDE.md` の各節に記録）／ **コード基準コミット**：main HEAD（この目次が古いと他が正しくても信頼が崩れるため、改修時は対象文書と本目次を必ず更新）。

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
