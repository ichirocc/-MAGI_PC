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
     元に戻す/やり直す・違反ハイライト/希望バッジ・**シフト集計(職員別/日別、Kotlin原本TallyCardの
     最小移植=`RenderStaffTally`/`RenderDayTally`。生カウントは`Schedule`から都度計算・セル枠は
     `CountViolations`/`NeedViolations`で色分け)**まで実装。編集タブは月次条件(希望/日別必要人数の
     一覧・追加・削除)・職員管理(追加/改名/削除、削除確認ダイアログ付き)・年間マスター
     (グループ/シフトの追加/改名/削除に加え、制約(ルール)10族=cons1/cons2/cons3系4/cons41(s)/
     cons42(s)の追加/変更/削除・**スキル区分の追加/改名/削除(`AddSkillGroup`等)・群×シフトの
     担当可否/適切回数マトリクス(`Ws1SetGroupShift`/`Ws1SetGroupApt`/`Ws1ResetGroupApt`、
     `EditView.xaml.cs`の`BuildGroupShiftMatrix`)・上限人数(2パターン目)の使用可否(`Ws1SetUse2`)**
     まで実装。種類ごとに入力欄の構成が異なる=`EditView.xaml.cs` の`ConstraintFamilyMetas` 参照。
     職員管理では職員ごとのスキル区分割当(`SetStaffSkill`)も追加/改名アクションに続けて書く。分析タブは診断一覧＋「直し方を探す」＋**違反の場所**
     （セル単位の違反=`UiState.ViolationCells`に載る族のみ。タップで勤務表タブへ切替＋該当セルへ
     スクロール＋約2.5秒ハイライト=`MainWindow.JumpToCell`/`ScheduleView.FocusCell`）。設定タブは
     最適化設定＋データ入出力(JSON開く/保存・CSV取込/書出)。デザイントークン（ブランド色）も
     `Styles/MagiTheme.xaml` へ移植済み（余白/角丸/タイポグラフィスケールの全面移植は未着手）。
     未対応（意図的にスコープ外）：`MagiScheduleViews.kt`の残り（週ページング・横スクロール併用・
     ItemsRepeaterベース化・違反種別フィルタ・検索/凡例折りたたみ等）・covU/covO/c41系(日単位)や
     low/high/apt/c2(職員単位)の違反箇所ジャンプ（単一セルを指さないため対象外、上記「違反の場所」参照）。
   - **色設定UI＋データ入出力のエラーハンドリング（2026-09-02）**: 設定タブに「表示色」節を追加
     （`SettingsView.RenderShiftColors`/`RenderViolationColors`）。シフト記号の表示色（`ShiftColorList()`/
     `SetShiftColor`/`ResetShiftColor`）・違反の基準色2種（必須/要調整、`SetViolationColor`/
     `SetViolationSoftColor`）・族別の個別色（19族、`SetViolationFamilyColor`）を、簡易カラーピッカー
     （既存7色パレット`MagiAccent.All`のスウォッチ＋16進テキスト入力の2択、フライアウト）で編集できる。
     **これらのViewModel APIは元々実装済みだったが、勤務表グリッド（`ScheduleView`）側が一切参照して
     おらず、設定を変えても見た目が変わらない「配線されていない箱」だった**——同じタイミングで
     `ScheduleView.ResolveVioBrush`（新設、`ColorHex`経由でシフト背景色/違反枠色を実際に解決）を配線し、
     セル背景（シフト色）・違反枠（族別→基準色→既定色の優先順位、Kotlin原本`resolvedVioColor`と同じ順）
     の両方に反映されるようにした（メイングリッド・シフト集計の両方が共有）。データ入出力の4ハンドラ
     （`OnOpenDataClick`等）は`FileOpenPicker`/`FileIO`の例外を素通りさせ`async void`ハンドラの
     未処理例外でアプリごとクラッシュしうる欠陥があったため、try/catchで`NotifySave`/`NotifyOpenFailure`
     （既存API・呼び出し口が無かった）へ受け止めるよう修正。
   - **群×シフトのcanDo/適切回数マトリクス＋スキル区分CRUD（2026-09-02）**: 「実装済みViewModel APIの
     呼び出し口を全数点検する」作業で発見した、色設定と同種の「配線されていない箱」——
     `Ws1SetGroupShift`/`Ws1SetGroupApt`/`Ws1ResetGroupApt`/`Ws1SetUse2`と`SkillGroups`/
     `AddSkillGroup`/`EditSkillGroup`/`RemoveSkillGroup`/`SetStaffSkill`はいずれもフェーズ9で
     移植・テスト済みだったが、この画面から一度も呼ばれていなかった。**群がどのシフトを担当できるか
     (canDo)を設定する手段がこれしか無く**、新規データでは誰も何のシフトも担当できないまま
     何も割り当てられない状態だった。年間マスターに、群(行)×シフト(列)のチェックボックス(canDo)＋
     テキスト欄(適切回数目標)のマトリクス（`EditView.BuildGroupShiftMatrix`、群/シフト数が変わらない
     限り既存コントロールを使い回してフォーカスを保つ）と、スキル区分の追加/改名/削除、上限人数
     (2パターン目)使用トグルを追加。職員管理にはスキル区分の割当欄(`StaffSkillCombo`)を追加
     （`Ws1AddStaff`/`Ws1EditStaff`自体はSkillIdxを受け取らないAPIのため、追加・改名の直後に
     `SetStaffSkill`を続けて呼ぶ）。
   - **ホーム3機能＋希望一括操作＋即時保存の配線（2026-09-02）**: 同じ全数点検の続き。
     `GenerateSmartInitial`（初期解生成・賢い版）/`RunSoftPolish`（仕上げ最適化のみ・破壊なし）/
     `ApplyAlternative`（Portfolio探索の「他の案」適用、`Ui.Alternatives`が0件のときは節を隠す）を
     ホームタブへボタン3つで追加。`ApplyWishes`/`ClearAllWishes`（登録済み希望の一括反映/一括削除）を
     月次条件へ追加（担当外の希望が混じる場合は「含めて反映/除いて反映/キャンセル」の3択ダイアログ）。
     **`SaveNow`（デバウンス無し即時同期保存、`MagiViewModel.SaveNow`のKDoc「autoSaveの1200msデバウンス
     中にプロセスが破棄されても編集が失われないための保険」）がどこからも呼ばれておらず、直近の編集から
     1200ms以内にウィンドウを閉じると自動保存に間に合わず編集が消えうる欠陥**を発見・修正——
     `MainWindow.OnAppWindowClosing`の実行中でない通常終了経路で必ず呼ぶ。`RestorePreviousData`
     （「データを開く」直前の状態へ戻す）も設定タブへボタンを追加（`Ui.PrevBackupAvailable`が
     falseの間はボタンごと隠す）。
   - **CIビルド失敗の修正＋種類別CSV/ログ書出/新規作成の配線（2026-09-02）**: 直前2コミット
     （色設定UI・群×シフトマトリクス）がいずれもWindows CIでビルド失敗していた
     （`ScheduleView.xaml.cs`が素の`Color`型=`Windows.UI.Color`を使うのに`using Windows.UI;`が
     無くCS0246。このサンドボックスはWindows専用プロジェクトをビルドできずCIでしか検出できない
     既知の制約で、2回連続で見落とした）。`using`を1行追加して解消・CI緑化を確認。続けて同じ
     全数点検の最後のまとまり: 種類別CSV（`ImportStaffCsv`/`ExportStaffCsv`・`ImportWishesCsv`/
     `ExportWishesCsv`・`ImportConstraintsCsv`/`ExportConstraintsCsv`、氏名一致で既存データへ
     追加/更新）・名簿CSVの新規取込（`ImportRosterAs`、「勤務表として/希望として」をダイアログで
     選べる——`ImportCsvSmart`には無い選択肢）・操作ログ書出（`ExportLogs`/`ExportLogsJson`）・
     新規作成（`InitBlankState`、最小構成から作り直す。`Load()`経路のため現在のデータは
     `RestorePreviousData`で復元可能）を設定タブへ配線。
   - **禁止の並び診断＋回数固定の緩和ボタンを分析タブへ配線（2026-09-02）**: `RelaxStaffRangePin`
     （「回数の固定で止まった手」一覧に「±1 緩める」ボタンを追加）と、これまで画面が一度も
     読んでいなかった`UiState.ForbiddenDiag`（禁止の並び(c3n)が「このデータ・希望のままでは
     崩せない」と判定した箇所）＋`RelaxForbiddenRule`（新設の「禁止の並びで止まっている箇所」節、
     崩せないと判定された行だけに「緩める（削除）」ボタン）を配線。前者は診断だけ見えて直す手段が
     無く、後者は診断結果自体が全く表示されていなかった。
   - **個人別の回数（下限/上限）編集UIを職員管理へ配線（2026-09-02）**: `SetStaffRange`/
     `RemoveStaffRange`（フェーズ9で移植・テスト済み）は、色設定・群×シフトマトリクスと同じ
     「実装済みだが呼び出し口が無い箱」の中でも特に基本的な欠落だった——個々の職員の回数上下限を
     設定する手段がアプリのどこにも無かった（`RelaxStaffRangePin`の±1調整は既存値の微調整のみで
     新規設定はできない）。職員管理の「対象の職員」選択を共有し、`StaffCountRules`（個人別レンジと
     適切回数(apt)の実効目標を統合したビュー）から一覧表示＋シフト選択＋下限/上限入力で設定できる。
   - **グループ単位の回数（一括設定）を年間マスターへ配線（2026-09-02）**: `GroupRangeSummary`/
     `SetGroupRange`/`ClearGroupRange`（フェーズ9で移植・テスト済み）も未配線だった。個人別
     （`SetStaffRange`）は1人ずつしか設定できないのに対し、こちらはグループ全員へ一括で下限/上限を
     書く（既に個人別で設定済みの職員はスキップ・保持）＋下限=上限のときは同じシフトの適切回数(apt)
     も同時に設定する——Kotlin原本コメントの言う「Excelのws1 C→ws5展開を1操作で再現」。
   - **見直し候補メモ＋人員不足の代用候補フライアウトを配線、未配線APIの全数点検を完了（2026-09-02）**:
     `AddReviewMemo`/`RemoveReviewMemo`（セッション内のみ・state非保存の軽量メモ）を年間マスター先頭に
     一覧＋手動追加欄で追加、加えて勤務表タブのセル編集フライアウトに「この違反を見直し候補にする」
     項目（違反セルのみ表示）を新設——追加口が無ければ一覧だけあっても意味が無いため両方必要だった。
     `ShortageFixCandidates`（担当可能・希望固定でない・禁止連続にならない・抜けても穴が空かない
     「動かせる人」だけを返す候補探索）も未配線だったため、シフト集計(日別)の人員不足(covU)セルを
     ボタン化しタップで候補フライアウト→選択でワンタップ割当を配線（`AddTallyCell`に`onClick`を追加）。
     `GroupKigouList`/`SkillGroupKigouList`（既存記号の一覧）は年間マスターのグループ/スキル区分
     追加ヒント文に「使用中の記号」として追加——記号衝突を`SymbolTaken`の事後エラーでなく事前に防ぐ。
     残り2件（`AllowedShiftsForGroup`＝群×シフトのcanDoマトリクスと数学的に同じ結果を返す・
     `GroupMemberCount`＝既に配線済みの`Ws1GroupMemberCount`と実装が完全に同一）は精査の結果、
     既存UIと重複するため新規UIを追加しないと判断した（コード自体は削除せず温存）。これで
     この移植のViewModel公開APIは全数、意味のあるUI導線を持つか、その理由が明記された状態になった。
   - **[訂正] 上記「全数点検を完了」は誤りだった＋未配線API9件を追加配線（2026-09-02）**:
     公開メソッド名を再抽出してWinUI呼び出し箇所と再度突き合わせたところ、`SetWishesForDays`/
     `ClearWishesForDays`（希望シフトのカレンダー複数選択）・`ShiftMonth`（前月/次月ボタン）・
     `Ws1ResizeDays`（期間日数の直接変更）・`RefreshCheck`（「問題がないか調べる」単独ボタン）・
     `EditBlockedNow`（セル編集ガードの無言Running判定を置換）・`ClearMessage`（シェル共通の
     `InfoBar`通知バーを新設）・`Notify`（CSV/ログ書出しで対象が無い時の警告）・`ShiftKigouList`
     （制約編集のシフト記号欄を自由入力→選択式コンボボックスへ）・`AptBalances`（適切回数マトリクス
     直下への入力その場警告）・`SetCells`（「まとめて割当」ダイアログ、Kotlin原本の
     `AssignBulkSheet`と同じフィルタ式選択＝ドラッグ不可制約に整合）が未配線のまま残っていた。
     いずれもKotlin原本のUIと突き合わせ、実在するギャップのみ対応（`SetMonth`/`SetShiftNeed`は
     内部ヘルパーまたは既存導線で充足、`SetNativeAccel`/`SetNativeParity`はこの移植にネイティブ層が
     存在しないため意図的に無効のまま維持、`EnvironmentLine`は`ExportLogs`経由で既に使用済み、と
     判断した理由を明記）。あわせて`MagiTheme.xaml`の余白/角丸/タイポグラフィスケール（Kotlin原本の
     `MagiTokens.kt`/`MainActivity.kt`のスケールを1:1移植）も追加——**トークン定義のみで、各Viewの
     ハードコード値(Thickness/CornerRadius/FontSizeが計60箇所超)からの置換は未着手のまま残っている**
     （次の全数点検で拾うべき既知の残課題として明記）。実装の教訓: 「全数点検を完了」と明言した
     直後でも、コードは変わり続けるため定期的な再走査が要る＝一度の宣言を恒久の事実として扱わない。
   - **MagiThemeトークンをThickness/CornerRadiusへ適用（2026-09-02）**: 上記の残課題のうち
     Thickness/CornerRadius（計35箇所）を対象に、EditView/ScheduleView/SettingsView.xaml.csを
     並列で精査。**トークン値と厳密一致する8箇所のみ`Application.Current.Resources["MagiSpacingXX"]`へ
     置換**（見た目は不変）し、残り27箇所（密な勤務表グリッドのセル余白6,4/罫線1dp/違反枠2-3dp/
     角丸0など、7段階スケール(4/8/12/16/20)に一致しない意図的な微調整値）は**据え置き＋その場に
     理由コメントを追加**——数値を無理にトークンへ寄せてグリッドの見た目を変えることより、
     デザインシステムに素直に繋がる箇所だけ繋ぐことを優先した。FontSize（31箇所）は`Style`
     オブジェクト全体の割当が絡み判断がより難しいため今回は対象外のまま（次の残課題）。
   - **MagiThemeのTypography(FontSize)トークンを各Viewへ適用（2026-09-02）**: 上記の残課題を消化。
     全View（EditView/ScheduleView/SettingsView/HomeView/AnalysisView/MainWindow.xaml.cs、計39箇所）の
     `FontSize`をMagiTheme.xamlのタイポスケール（`MagiBodySmallTextStyle`等11種、最小14pt〜）と
     突き合わせた結果、**実際の値は10/11/12/13/14の5種類しかなく、厳密一致するのはMainWindow.xaml.cs
     の未使用タブ向けプレースホルダ1箇所（`FontSize=14`・FontWeight未指定→`MagiBodySmallTextStyle`
     =14/Normal と完全一致）だけ**だった。この1箇所のみ`Style = (Style)Application.Current
     .Resources["MagiBodySmallTextStyle"]`へ置換し（実質未到達コードのため影響ゼロ）、残り38箇所は
     2種類の理由で意図的に据え置いた（各ファイルの最初の該当箇所に理由コメントを追加）:
     - **型として不可能**（Button/TextBoxのFontSizeが約15箇所）: トークンは`TargetType="TextBlock"`の
       `Style`のため、`Button.Style`/`TextBox.Style`へ代入すると型不一致で実行時例外になる。
     - **値が一致しない**（TextBlockのFontSize=10/11/12/13が約23箇所）: このアプリの一覧行・
       密グリッド・ダイアログ本文はスケール最小値(14)より小さい値を使っており、Thickness/
       CornerRadiusパスと同じ理由（無理に14へ引き上げると一覧行が全画面で目に見えて大きくなる）で
       据え置いた。**タイポグラフィスケールは元々「読み物用の大きい文字」向けで、この移植の密な
       業務UIとは前提が異なる**ことが今回の調査で判明した（Thickness/CornerRadiusより一致率が低い）。
     これで「デザイントークン全面移植」の残課題（Thickness/CornerRadius/FontSize）はすべて着手・
     判断済み。据え置いた箇所は全てその場に理由コメントがあり、無言のハードコードは残っていない。
   - **群×シフトの担当可否を2次元マトリックスへ再設計（2026-09-02, ユーザー提示案・Android と同時対応）**:
     旧: チェックボックス＋適切回数の数字欄を同じセルに重ねた表で、担当可否だけを一目で見比べられなかった。
     新: 行=群・列=シフトの✓/—マトリクス。**左列（群名）は横スクロールの外に固定**（`GroupShiftNameColumn`、
     行高44で右側と揃える）、右側（シフト名ヘッダ＋セル）だけ横スクロール。**セルは全面がタップ標的**の
     Button（44×44、ON=主色地＋白✓ / OFF=薄い地＋—。色だけに依存しない）。**行ヘッダ（群名）タップで
     その群の全シフトを一括、列ヘッダ（シフト名）タップでそのシフトを全群へ一括**（1つでもOFFがあれば全ON、
     全ONなら全OFF）。適切回数は別マトリクス（`GroupAptMatrixHost`）へ分離（Android の③回数分離と同じ構成）。
     エンジン: `Ws1Ops.SetGroupShiftRow/SetGroupShiftColumn`（Kotlin 原本と同値）を追加。**行OFFでも「休」は
     残し、休の列はOFFにできない**（担当可能シフトが無い群を作ると validate が拒否し職員が行ごと groupViol に
     なるため＝3.418.0/3.442.0 と同じ理由。列OFFの拒否は VM が `Notify` で案内）。テスト2件追加（`Ws1OpsTest`）。
   - **並行処理・決定性バグの監査（2026-08-28実施分）を再検証＋横断スイープ（2026-09-02）**:
     過去の監査で見つかった3件（`V6NativeOptimizer.Portfolio.cs`のCancellationToken未観測は監査当日中に
     既に修正済みと確認・対応不要／`SaOptimizer.Run`の`Task.WhenAll`が兄弟ワーカーの障害時に
     フェイルファストしない／`ViolationChecker.cs`の`CountViolations`等6種のマップがKotlin原本の
     `LinkedHashMap`と異なりC#の`Dictionary`で列挙順が契約上保証されない）を最新コードで再検証し、
     後の2件が依然として存在することを確認して修正（前者は`CancellationTokenSource`の連結で兄弟を
     フェイルファスト化、後者は新設の`InsertionOrderDictionary<TKey,TValue>`で挿入順を保証）。
     同種のバグを他領域へも横断的に探索し、`MagiViewModel.Diagnostics.cs`の`AnalyzeParallelAsync`
     （5つの診断`Task.Run`が未観測例外を起こしうる）と、CSV取込・違反詳細ログの「未知の記号/職員別
     集計」が同型の列挙順問題を抱えていたことを新規発見・修正。いずれもkeep-best判定が最終採否を
     担うため無効な解には至らない（`IsBetter`ゲート）＝並行処理のフェイルファスト化と列挙順の
     決定性のみの改善で、スコアリング・重みロジックは不変。
   - **OneDrive対応（2026-09-01, ユーザー確認）**: データ入出力(`SettingsView`)は
     `FileOpenPicker`/`FileSavePicker`（実ファイルパスを指す `StorageFile`）経由で読み書きするため、
     OneDrive同期フォルダ内のファイルも特別な対応なしにそのまま開く/保存できる（クラウドのみの
     プレースホルダーファイルも、ピッカー選択時にシェルが自動的に実体化する。アンパッケージ
     Win32アプリなので `CachedFileManager.CompleteUpdatesAsync` 等のブローカー越し更新通知も不要）。
     追加実装は無し（既定保存先の変更・自動保存のOneDrive化・Graph API直接連携は明示的に不要と確認済み）。
   - **残画面の ralph-loop（2026-09-05〜06, 完了）**: `docs/phase9/queue.md` の 24 行を 1 周 1 行で移植。結果＝**済 24／済(既存) 0／保留 0／対象外 0**、
     CI（`windows-app-build.yml`）は全行緑。仕様は `docs/phase9/specs/1.md`〜`24.md`、判断は `docs/phase9/blockers.md`（判断待ち 0・保留 0・
     気づき 6 件＝#2 処方箋の暫定導線、#5 空状態とフィクスチャ自動読込、#9 日別不足 ▼N、#17 重み表の実在（#22 で解消）、#18 族名 c1 の
     Android とのずれ、#23 ホーム大ボタンと下部バーの作成導線の重複）。主な追加: ホーム（被覆・次の一手・スマートアクション・進捗・副操縦・空状態）、
     勤務表（週ナビ・違反フィルタ・検索・祝日色・不足サマリー・集計詳細）、編集（職員×シフト回数マトリクス・必要人数/希望のカレンダー一括・
     月次チェックリスト・実働チェック・制約ヘルプ）、分析（要確認トリアージ `AnalysisTriage`・設定の見直し・C1 頭打ち／回数固定の影響）、
     設定（36 色ピッカー・重み表）、シェル（状態バッジ・下部コマンドバー）、ホームの「なおすのを手伝って」。
     VM 追加は各行のテストつき（MagiApp.ViewModels.Tests 416 緑）。
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

## Windows 11 での入れ方・起動しないとき（2026-09-04）

### 入れ方（証明書不要・管理者不要。SHA-256 を照合してから起動する）

[Releases](https://github.com/ichirocc/-MAGI_PC/releases/latest) の本文にある **SHA-256** を控え、PowerShell（スタートで「powershell」）に
次を貼り付けて `<SHA-256>` を置き換えて Enter。ハッシュが一致したときだけインストーラのウィザードが開く（不一致なら止まる）。
Release 本文にはバージョン固定 URL つきの同じ 1 行が SHA-256 込みで載っているので、そちらを貼るのが早い。

```powershell
$expected="<SHA-256>"; $f="$env:TEMP\MagiShiftOptimizer-Setup.exe"; curl.exe -L -o $f https://github.com/ichirocc/-MAGI_PC/releases/latest/download/MagiShiftOptimizer-Setup-x64.exe; if ((Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "SHA-256 mismatch" }; Unblock-File $f -ErrorAction SilentlyContinue; & $f
```

`Unblock-File` は SmartScreen の印を外す＝ダウンロード物を無条件に信用する操作なので、**照合の後**にだけ行う（コード署名は未導入。
`installer/sign-files.ps1` は証明書がある場合の手順）。公開済みバージョンの実体は差し替えない（同じタグを別コミットから再実行すると
Actions が失敗する）。

なぜ警告が出ないか: SmartScreen は「インターネットから来た印（Mark of the Web＝`Zone.Identifier` 代替ストリーム）」が
付いたファイルを**起動した瞬間**に評価する。ブラウザや Explorer の zip 展開はこの印を付けるが、`curl.exe`（Windows 10 1803
以降に同梱）は付けない。念のため `Unblock-File` で外してから起動するので、SmartScreen の評価自体が走らない。
インストーラが書き出すファイルにも印は付かないので、以後の起動でも出ない。インストールは per-user
（`%LOCALAPPDATA%\Programs\MAGI ShiftOptimizer`）で UAC 昇格なし。無人インストールは末尾を `& $f /VERYSILENT` にする。

`releases/latest/download/...` はタグ `win-vX.Y.Z` を push したときに Actions「Windows Installer」が作る GitHub Release の
固定名添付を指す（常に最新版）。更新の出し方は [`installer/README.md`](./installer/README.md)。

### ブラウザや Actions の Artifacts から落とした場合

- zip を展開する**前**に、zip を右クリック→「プロパティ」→ 下の「ブロックの解除」にチェック→ OK（または
  `Unblock-File .\magi-windows-setup-exe.zip`）。展開後の setup.exe に印が継承されないので SmartScreen は出ない。
- 先に展開してしまった／setup.exe を直接落とした場合は、SmartScreen の「Windows によって PC が保護されました」で
  **「詳細情報」→「実行」**。または setup.exe を右クリック→プロパティ→「ブロックの解除」でも同じ。
- MSIX（`magi-windows-msix`）は署名用 Secrets を入れていない限り未署名で、Windows は「証明書」「セキュリティ」を理由に
  インストールを拒否する＝通常は使わない。

### 「スマート アプリ コントロールによってブロックされました」と出る場合

Windows 11 をクリーンインストールした PC は「スマート アプリ コントロール（SAC）」が有効（または評価モード）のことがあり、
CA 発行の証明書で署名されていないアプリを**例外なく**止める（「詳細情報→実行」の逃げ道が無い）。証明書を買わない前提では
SAC を切るしかない: Windows セキュリティ →「アプリとブラウザー コントロール」→「スマート アプリ コントロールの設定」→
**オフ**。一度オフにすると Windows を再インストールするまで再有効化できない（Microsoft の仕様）。

### 証明書で何が変わるか（早見表）

| 手段 | SmartScreen | SAC | MSIX のダブルクリック | 費用 |
|---|---|---|---|---|
| MotW を付けない／外す（上の1行・ブロックの解除） | **出ない** | 効かない | ― | 0 |
| 自己署名証明書で署名 | 消えない（「不明な発行元」のまま） | 効かない | ○（配布先で1回、管理者で信頼が必要） | 0 |
| CA 発行のコード署名証明書（OV） | 評価が貯まるまで出る | ○ | ○ | 年数万円 |
| EV 証明書／Azure Trusted Signing | ほぼ出ない | ○ | ○ | EV は高額、Trusted Signing は月額少額（個人利用の本人確認と提供地域は要確認） |
| SignPath Foundation | OV 相当 | ○ | ○ | OSS 公開リポジトリなら無料（審査あり） |

証明書を入手したら Secrets（`WINDOWS_CERTIFICATE_BASE64` / `WINDOWS_CERTIFICATE_PASSWORD`）に登録するだけで、
既存のワークフローが中身の exe と setup.exe を二重署名する（[`installer/README.md`](./installer/README.md)）。

### 起動しても画面が出ないとき

`%LOCALAPPDATA%\Magi\startup_error.log` を見る（起動時に必ず1行書く。失敗時は例外と原因の要約を MessageBox でも表示する）。
2026-09-04 以前の setup.exe は publish に `resources.pri` と VC++ ランタイム（vcruntime140/vcruntime140_1/msvcp140）が
入っておらず、この症状で無言終了していた。同日以降に生成した setup.exe（run 33899674768 以降）を入れ直すこと。

## レビュー対応の記録

- 2026-09-06 外部レビュー第3・4段（対象 70c9d1d）への対応:
  - **[高] 案内付き修正の連打防止が Schedule 通知で解除される** → `UiState.CheckRev`（`MakeUi` ごとに増える検査世代）を追加し、
    押下後は押下時より新しい世代が反映されるまで全候補を無効（「再検査中…」表示）。判断（対象枠/BlockedNow/Infeasible/AllDone）を
    `GuidedFixPlan`、有効/無効を `GuidedFixFlow` として ViewModels へ切り出し、`GuidedFixTest` 3 件で「Schedule だけでは解除しない・
    押下前の世代では解除しない・閉じた後は無視」を固定。
  - **[高] 公開済みバージョンのインストーラーを差し替えられる** → `windows-installer.yml`: 既存タグのコミットが `TARGET_SHA` と違えば失敗
    （バージョンを上げることを要求）。同一コミットの再実行ではバージョン付き資産を `--clobber` しない（固定名 latest のみ更新）。
  - **[高] 検証なしで SmartScreen 保護を外して実行** → Release 本文と README の 1 行を「SHA-256 照合→不一致なら throw→Unblock-File→起動」に。
    Release 本文はバージョン固定 URL＋その版の SHA-256 を埋め込む。「警告なし」を主目的にした見出し・文言を改めた（コード署名は未導入と明記）。
  - **[高] 範囲外シフトを勤務表へ保存できる** → `SetCell`/`SetCells` に共通ガード `RejectUnknownShift`（-1＝未割当は可、0..Shifts.Count-1）。
    拒否時は Undo・自動保存・再検査を発生させない。`ApplyFixSuggestion` は前回の上限検査を維持。テスト +5。
  - **[中] 制約読取 API が負インデックスで例外** → `ConstraintRowValues` の入口で `index < 0` を null に。テスト +1（10 族）。
  - **[中] 制約の数値欄へ任意文字列を登録できる** → `MagiViewModel.ConstraintInputError`（非負整数・cons1 は 1≤回数≤日数≤期間・
    cons41 系は空欄可＋下限≤上限・記号は現在の定義に存在）を追加し、`EditView` の追加/変更の両方で先に通す。テスト +1（14 ケース）。
  - MagiApp.ViewModels.Tests 427 緑。Android 側: `setCell`/`setCells` の上限検査と `constraintRowValues` の負添字を 3.500.2 で同期
    （案内付き修正の連打防止は Android も `remember(ui.schedule)` で同型＝バックログ #10）。

- 2026-09-06 外部レビュー（2段・対象 968478b）への対応:
  - **[高] 改善提案の古い結果を適用できる** → Kotlin 3.475.0 の指紋照合を移植（`_fixBoardKey`/`_fixStateKey`＝探索時の盤面/設定、適用時に不一致なら拒否して再探索を促す）
    ＋ `ToShift >= Shifts.Count` を拒否。テスト +2（`ApplyFixSuggestion_RejectsWhenTheBoardChangedSinceTheSearch` ほか）。
  - **[高] Undo/Redo が結果メタデータを復元しない** → Undo/Redo で `EngineRan=false`（手操作）・`FixSuggestions` を空に・`_resultSchedule=null`。
    背景実行の keep-best は Kotlin 3.475.0 と同じく「この実行に渡した入力」（`_bgInput`）と比較するよう修正（旧: 前回の結果と比較＝手編集や
    元に戻した盤面が黙って巻き戻され得た）。診断（C1Plateau/PinTargets）は盤面指紋で自動的に無効化される（`Diagnostics.cs` の diagFresh）。
    テスト +1（`UndoAndRedoDropEngineRanAndPendingSuggestions`）。
  - **[中] 下部バーが改善探索中の Undo/Redo を許可する** → 表示条件は Android と同じ（`canUndo && !running`）のまま。VM 側は上記の指紋照合で
    古い提案の適用を拒否する（Android 3.475.0 の設計＝探索を止めるのではなく適用で照合）。
  - **[中] 担当外希望の一括削除が古い診断を使う** → `ClearOutOfScopeWishes` を現在の state（`Problem.CanDo`）から判定する形に変更。
    テストを差し替え（古い診断が名指ししても担当可能なら消さない）。Android 側は `settingIssues` 由来のまま＝同じ指摘を Android バックログへ。
  - **[中] EngineRan と Undo の契約が未テスト** → 上記テストで最適化相当→Undo→Redo を固定。
  - **[中] GroupShiftApt 正規化の一部が到達不能** → Kotlin も同順（validate→normalize、3.491.0 で「列不足は validate が先に拒否」と注記済み）。
    C# の `NormalizeGroupShiftApt` の説明を同じ契約に訂正（動作は不変）。
  - **[低] XML doc の付与先** → `AptBalances` の summary を正しい位置へ。
  - **[高] 希望島で窓・両翼・巡回が評価されない／禁止連続を減らす候補まで枝刈り／[中] ビームが列挙順に依存** → いずれも C# は Kotlin
    `WishIslandPolish.kt`（3.496.0 の確定仕様）と同一の挙動。探索動学の変更は Kotlin を正として Android 側で計測（PostProbe/`nsp_bench`）つきで
    決め、その後に同期する方針＝Android のバックログに登録。C# 単独では変えない（パリティ維持）。
  - **[中] Android 同等実装が存在しない** → 事実誤認。`ichirocc/magi7ichiro-fork` main（009e780）に `WishIslandPolish.kt`・
    `AnchoredWindowSwapTest.kt`・`WishIslandPolishTest.kt`・`applyStrictWholeWindow` が存在する（upstream `ichirocc/magi7ichiro` を見た疑い）。
  - MagiApp.ViewModels.Tests 416 緑。

- 2026-09-06 3.500.0 と同時: `RunPostOptimization` を Android と同型に再構成（`PostChain` ランナー＝採用・ピン帰属合流・ログ・計時の 1 経路、
  `PostOptimizationParams`（既定値は従来値）・`SeedTag`、`RunPolishCluster`/`SoftPolishVerifyLog`/`FinalC1Plateau` に分割）。
  退化した予算（LNS 0ms）のゼロ除算をガード。テスト +2（`V6PostOptimizationParamsTest`）: MagiEngine.Tests 760 緑。
- 2026-09-05 3.499.0 と同時: `AnchoredWindowSwap` の重複排除を方向フィルタの後へ、range 重みの出典を `MirrorKeys.WeightOf` に。テスト +1: 758 緑。

### 2026-09-05 可変長ブロック交換の自律レビュー第2段（Android 3.499.0 と同時）
- `V6HotfixPasses.AnchoredWindowSwap.cs`: 厳密窓交換の重複排除を方向フィルタの後へ（旧: 回数超過アンカーが捨てた窓を
  別アンカーが二度と作れなかった）。Android 側の実データ計測では盤面・採用は不変、実候補が各条件 +1〜+9 件。
- `PersonalPenaltyOf` / `PersonalBalancePenalty` の range 重み 90/45 の手書きを `MirrorKeys.WeightOf("low"/"high")` に。
- テスト `DegenerateWindowLengthsDoNotCrashAndNeverWorsenTheBoard` 追加。MagiEngine.Tests 758 緑。
- Android 側の Session 化（CyclicSession/StrictSession・KeepBest/RejectStats 共通化）は C# へ未鏡映（既に 2 ファイル分割済み。
  鏡映の要否は人間判断）。

- **2026-09-05（Android 3.498.0 と同時）** 希望島研磨 `ApplyWishIslandPolish` を Kotlin 側の自律レビュー結果どおり再構成:
  `WishIslandSession` クラス化、候補の遅延生成（`IEnumerable`、評価順は旧実装の安定ソートと同一）、`WishIslandParams` record
  （既定値不変、負値は下限へ丸める）、前の島の採用で不活性になった島の評価スキップ。テスト 2 件追加（755→757 緑）。
  採否の意味論と既定値は不変。
- **2026-09-05（ユーザー提示の記事「ルールではなく skill に指示を書くことで、Claude のコメントを減らせた」）** 追加コメントの
  点検を手順化した `comment-check` スキル（`.claude/skills/comment-check/SKILL.md`）と `tools/comment_ratio.py` を Android 側と
  同じものとして導入し、最初の実走として 2026-09-04 の起動修正（08cb3aa..521151a）で追加したコメント行を1行ずつ判定した:
  39→22 行、比率 17.2%→10.5%、4行以上の塊 5→2（最大 9→5 行）。消したのは run ID・実機報告の引用・「旧: …」の履歴で、
  すべてこの記録に既にある。コードは不変（コメントのみ）。
- **2026-09-04（ユーザー指示「これは署名証明書が無い限り残るので賢く解決する」）** SmartScreen は Mark of the Web 付きファイルの
  起動時にしか評価しない事実を使い、証明書なしで警告ゼロにした: ① タグ `win-vX.Y.Z` の push で setup.exe を GitHub Release へ
  添付（固定名 `MagiShiftOptimizer-Setup-x64.exe` も付け `releases/latest/download/…` を常に最新に）、② README に
  `curl.exe`＋`Unblock-File` の1行インストール（MotW が付かない→SmartScreen が走らない）と、zip の「ブロックの解除」手順、
  ③ スマート アプリ コントロール（署名必須・逃げ道なし）の見分け方と対処、④ 自己署名は SmartScreen に効かない等の早見表。
  版はタグから取る（`Resolve version` がタグ優先）。ワークフローの `release` ジョブは ubuntu ランナー・`contents: write`。
  この環境（Claude Code リモート）は git のタグ push が 403 で通らないため、`workflow_dispatch` の `publish_release` 入力
  からも作れるようにした（`gh release create --target` がタグを打つ）。**実走 run 33903452253 で緑**: Release
  <https://github.com/ichirocc/-MAGI_PC/releases/tag/win-v1.0.0> に版付き＋固定名の setup.exe（46,097,340 バイト、
  SHA-256 `3eb81779…c0b2`）が添付され、`releases/latest/download/MagiShiftOptimizer-Setup-x64.exe` から実際に
  落とせて先頭が `MZ`・SHA-256 一致を確認した。
- **2026-09-04（実機報告「Windows11版が起動出来ない。セキュリティが不足。画面でない」）** run 33885296148 の publish 出力を
  数え直して原因を特定: ① `resources.pri` が publish フォルダに無い（MakePri は bin 側へ書き、msbuild /t:Publish は写さない）
  ② VC++ ランタイム DLL が同梱されていない（WindowsAppSDKSelfContained は WinAppSDK 自身しか同梱しない）→ どちらも
  unpackaged WinUI 3 の起動に必須で、無いとウィンドウを出す前に無言終了する。`windows-installer.yml` に両方を publish へ
  写して検証するステップを追加（欠けていればジョブを赤に）。「セキュリティ」は未署名 setup.exe の SmartScreen／未署名 MSIX の
  拒否＝入れ方を README 冒頭に明記。あわせて `Program.cs`（手書き Main・DISABLE_XAML_GENERATED_MAIN）と
  `StartupDiagnostics`（`%LOCALAPPDATA%\Magi\startup_error.log`＋Win32 MessageBox）で起動失敗を必ず見えるようにした。
  **実走確認**: run 33897078757＝`Program.cs` の CS0029（ラムダ引数名 `_`）で赤→修正。run 33897806456＝MakePri の実出力名が
  `MagiApp.WinUI.pri`（`resources.pri` ではない）で bundle 手順が赤→アプリ PRI を検出して `resources.pri` の名で写すよう修正。
  **run 33898775401 で緑**（publish 264→275 ファイル、`resources.pri`＋VC ランタイム 9 DLL を Inno が同梱、setup.exe 生成）。
  その run の VC ランタイムは `onecore\x64` 変種が選ばれていたため、デスクトップ用 `<ver>\x64\Microsoft.VC1xx.CRT` を
  優先する選択へ直し再実行 → **run 33899674768 で緑**（`VC CRT <- …\14.51.36231\x64\Microsoft.VC145.CRT`、
  `resources.pri <- …\win-x64\MagiApp.WinUI.pri`、publish 275 ファイル、成果物 `magi-windows-setup-exe` 45.5MB）。
  これが実機で試してもらう最初の版。起動しなければ `%LOCALAPPDATA%\Magi\startup_error.log` の内容が次の手がかり。
- **2026-09-04（Android 3.496.0 と同時）** 希望島研磨 `ApplyWishIslandPolish`（`V6HotfixPasses.WishIsland.cs`）を移植。
  実現可能な希望日を固定アンカーに、影響範囲が重なる希望を島へ統合、周辺に違反がある島だけ起動。同日→窓→両翼→必要時のみ
  3者巡回、希望周辺も全体も改善する手だけ採用、停滞時のみ短いビーム。テスト `V6HotfixPassesWishIslandTest`（3件）。
- **2026-09-04（Android 3.495.0 と同時）** ユーザー提示の設計「違反アンカー型・可変長ウィンドウ交換」を
  `ApplyAdaptiveBlockSwapPolish(mode: WindowMode.StrictWholeWindow)`（`V6HotfixPasses.AnchoredWindowSwap.cs`）として移植。
  3.494.0 の `ApplyRunSwapPolish` は削除。同じ日付範囲の一括交換（部分交換しない）・回数不足の逆引き・安価な優先度・
  pass ごとに最良1手。テスト `V6HotfixPassesAnchoredWindowSwapTest`（3件）。
- **2026-09-04（Android 3.492.0 と同時・エンジンのみ）** `CoverageSurplus.PinnedStaff`（人員過剰の枠を本人希望で固定している
  在勤者）を追加。Android は集計ダイアログ／ホームの過剰診断カードから「◯◯ の希望を取り消す」導線を出す。WinUI 側の
  表示はフェーズ9の残作業。
- **2026-09-04（main 012960f への外部レビュー第7弾＋Android 側の自己見直しで見つかった同型、全件実在→修正）**
  ① `RunMultiWorker` の各仮説入口の「既に勝者がいれば何もせず抜ける」事前チェック→ 撤去（3.376.0 相当の全本継続
  の仕様に反し、起動順で本数が変わっていた。`V6NativeOptimizerMultiWorkerTest` の「バグではない」コメントは
  矛盾した挙動の仕様化だったので履歴注記へ置換し、`AllHypothesesRunEvenWhenHypothesisZeroReportsHardZeroImmediately`
  を追加）。② workers=1 の短絡（`hSpawn <= 1`）と `HandleOptimize` の終端が停止を正常終了で返していた→
  `ThrowIfCancellationRequested()`（ViewModel は `OperationCanceledException` で「直前の勤務表を保持」へ分岐する
  設計なので、正常終了は途中盤面を「完了」として採用してしまう）。旧テスト
  `SingleHypothesisPathWithPreCancelledTokenReturnsCleanlyWithoutThrowing` はこの非対称を固定していたので
  `…ThrowsOperationCanceled` へ置換。③ `RunSlot.NowMs`（Stopwatch）と `EngineClock`（TickCount64）の
  2つの単調時計が `V6LateOperators.Improve` の締切で混ざっていた→ `EngineClock` に一本化。
  ④ 読込時の正規化（EndDate/GroupShiftApt）が `_originalJson` に反映されず、自動保存・「データを保存」が補正前の値を
  書き戻していた→ 正規化したときは `StateJsonSerializer.Serialize` の結果を原本に
  （`LoadAsyncPersistsTheEndDateCorrectionIntoTheExportedJson`）。⑤ `_saveGen` を `Interlocked.Increment`、
  `SettingsView` の二重 `<summary>` を整理。
- **2026-09-04（main 53f60aa への外部レビュー3件、全件実在→修正）**
  ① `Ws1Ops.SetGroupShift`（単一セル）に休の OFF 拒否が無く、行/列一括だけが保護していた→ 列一括と同じ
  「同じ state を返す」契約で拒否し、`Ws1SetGroupShift` が `ReferenceEquals` で検知して同じ案内を出す
  （`Ws1OpsTest.SetGroupShift_SingleCell_RefusesTurningRestOff`）。Kotlin 原本にも同じ穴があり、
  `magi7ichiro-fork` 3.484.0 で同時に修正。
  ② `EditView` の適切回数セルが `GroupShiftApt[g]` の行の存在を確認せず、`Validate` が許容する行数不足
  （空配列・旧形式）の state で `IndexOutOfRangeException`→ 行の存在も確認（読込時の G×K 正規化は
  保存ファイルの内容が変わるため採らず、読む側で守る＝エンジン側の既存の読み方と同じ）。
  ③ `android-sdk.yml` のビルド失敗ログ保存が `if: failure()` で、ビルドステップが `set +e` で成功扱いの
  ため一度も動いていなかった→ ステップ出力 `steps.build.outputs.code` で判定。
  レビューの「.NET SDK が無い」は誤り（`dotnet` 8.0.424 あり）。`MagiEngine.Tests` 全件と
  `MagiApp.ViewModels` のビルドをローカルで確認。
- **2026-09-04（第2弾・3件、全件実在→修正）**
  ① `windows-installer.yml` が `workflow_dispatch` の `version` 入力を PowerShell 本文へ式展開していた
  （引用符や改行で任意コマンド。後続ステップは署名証明書を扱う）→ 入力は `env:` で受け取り `X.Y.Z` だけ通す。
  ISCC への版も `steps.ver.outputs` の式展開をやめ環境変数→引数へ。
  ② 自動保存の**世代逆転**（`_saveCts.Cancel()` は始まった書き込みを止められず、古い自動保存が
  `SaveNow()` の後に完了すると古い状態へ戻る）→ main で採番した世代を `WriteAutosaveIfLatest` がロック下で
  比較し、古い世代を捨てる（`MagiViewModelPersistenceTest.StaleAutosaveGenerationNeverOverwritesANewerOne`）。
  Kotlin 原本も同じ穴＝`magi7ichiro-fork` 3.485.0（`SaveGate`）で同時修正。
  ③ 取込ファイルのサイズ上限が無かった（Android は 32MiB）→ `SettingsView.ReadImportBytesAsync` が
  サイズ属性で先に拒否し、ストリーム側でも読み切らずに中断。JSON／勤務表CSV／種類別CSV／名簿CSV の4入口
  すべてが通る。`IoReason` が上限超過の理由をそのまま表示。
- **2026-09-04（第3弾・総括12件）** 1〜6 は上記で対処済み。新規で実在した2件を修正:
  ⑦ Android Lint が CI で一度も走っていなかった（`abortOnError=false`/`checkReleaseBuilds=false` のため assemble では
  実行されず、lint レポートのアップロードは生成元の無い空振り）→ `android-sdk.yml` に `lintDebug` の報告専用ステップ
  （赤くはしない＝閾値は `build.gradle.kts` の lint ブロックで決める）。
  ⑫ `EndDate` と日数の矛盾が構造検証を通り抜けていた→ 読込時に `Ws1Ops.NormalizeEndDate`（StartDate＋日数−1）で
  揃え、補正したときは警告ログ（`Ws1OpsTest.NormalizeEndDate_*`）。Kotlin 原本も 3.486.0 で同時修正。
  ⑧〜⑪（`catch (Throwable)` で OOM も捕捉／`largeHeap`／リリース APK のデバッグ鍵／成果物の毎日全削除）は
  **記録された意図的判断**（マニフェスト・build.gradle.kts・cleanup-artifacts.yml のコメント参照）のため変更せず、
  判断材料として本体リポジトリの履歴に整理。
- **2026-09-04（第4弾・新規2件）** ① `AtomicFileWrite` が rename 失敗で直接書きへ落ちる前に `commitGuard`
  （所有権）を再確認（旧: 稀な経路で古い writer が target を書けた。`move` を注入点にしてテストで rename 失敗を再現）。
  ② `cleanup-artifacts.yml` は1件も消せなければ `core.setFailed`（旧: 警告だけで緑）。Actions の SHA 固定は
  更新運用とセットの方針事項のため記録のみ（署名・リリース系から先に固定する案）。Kotlin 原本は 3.487.0。
- **2026-09-04（第5弾・新規2件＋境界の一本化）** ① `MainWindow` の確認ダイアログ経由の終了で `SaveNow()` が呼ばれず
  （2回目の Closing は `_closeConfirmed` で即 return）、デバウンス待ちの編集が消えていた→ `Stop()` の直後に `SaveNow()`。
  ② 読めない `StartDate` を受理して `Problem.Dow0` が黙って日曜（0）へ落ちていた→ `Ws1Ops.StartDateError` を `Validate` の
  先頭で（`yyyy-MM-dd` 厳密）。③ `GroupShiftApt` の行不足は読む側（EditView／診断／修復）が個別に添字を守っていたが、
  境界を1か所に寄せて `LoadAsync` が検証後に `Ws1Ops.NormalizeGroupShiftApt`（G×K・空欄）で揃える。
  ④ 背景計算のボタン文言に「（ウィンドウを閉じると中断）」＝プロセス内 Task のみという設計判断を画面で明示。
  `Ws1OpsTest`／`MagiViewModelPersistenceTest` に回帰テスト。`skillIdx` の範囲外は `Ssk[i]==groupIdx` の比較で
  無害（未所属と同じ）＝変更なし。Kotlin 原本は 3.488.0。
- **2026-09-04（実機報告「個人の下限をゼロに出来ない」）** `StaffCellLimits` が `RangeLo == 0` を未設定扱いにして
  いたため、下限 0 を適用しても再表示が「なし」へ戻り、表も「〜0」になっていた（エンジンは lo=0 を保持）。
  `int.MinValue` のみ未設定に。回帰テスト追加。Kotlin 原本は 3.489.0。
- **2026-09-04（第6弾・新規2件）** ① **タブを一度離れると状態更新を受け取らない**（High・実在）: タブはキャッシュされ
  再利用されるのに、5画面ともコンストラクタで購読・Unloaded で解除だけ＝戻っても再購読されず、最適化の完了・停止・
  設定変更後も表示とボタンの活性が古いままだった。`UiSubscription`（MagiApp.ViewModels・WinUI 非依存・多重購読しない）を
  新設し、Loaded で `Attach()`（新規購読なら `Render()` で見えていなかった間の変化をまとめて描く）、Unloaded で `Detach()`。
  `UiSubscriptionTest` でタブA→B→A の再購読・多重購読なし・解除中は届かない、を固定。
  ② **締切に壁時計**（Medium・実在）: `EngineClock.NowMs()`（`Environment.TickCount64`＝単調）を新設し、エンジン内の
  締切・経過・停滞判定 11 箇所（HandleOptimize／RunPostOptimization／HF66／HF67／C1Beam／EliteIntegration／
  LateOperators／FixSuggester／CombinatorialRepair）とテストの `deadlineMs` 生成を統一。ログの実時刻（`MirrorLog.Ts`・
  `StartedAt`）・乱数シード・CSV の年・ViewModel の経過表示は壁時計のまま（相対値のみ／表示専用）。
  Kotlin 原本は 3.490.0（`EngineClock.nowMs()`＝`System.nanoTime()`）。

## スコープ外

`app/src/main/cpp/magi_native.cpp`（JNI経由のC++高速化ミラー）はこの移植の対象外
（Android/ARM上のJNIオーバーヘッド対策であり、Windows デスクトップでは純粋なマネージドC#で
十分な可能性が高い。ネイティブ層の要否はフェーズ5終盤の粗いタイミング計測後、証拠が出てから
プロファイラで検討する）。
