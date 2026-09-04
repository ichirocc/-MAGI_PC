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

## レビュー対応の記録

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

## スコープ外

`app/src/main/cpp/magi_native.cpp`（JNI経由のC++高速化ミラー）はこの移植の対象外
（Android/ARM上のJNIオーバーヘッド対策であり、Windows デスクトップでは純粋なマネージドC#で
十分な可能性が高い。ネイティブ層の要否はフェーズ5終盤の粗いタイミング計測後、証拠が出てから
プロファイラで検討する）。
