# フェーズ9 残画面の手順書（ralph-loop 用）

毎周この手順書を読み直してから動く。**1周に進めるのは queue.md の 1 行だけ**。進捗はファイルにあり、記憶に頼らない。

## 1. 行を選ぶ
`queue.md` を上から見て、実装が「未」または「差戻」の最初の行を選ぶ（「保留」「済」「済(既存)」「対象外」は飛ばす）。

## 2. 仕様を Android 原本から起こす
- 行の「Android 原本」に書かれた Kotlin ファイル・関数を**直接読む**（`/home/user/magi7ichiro-fork/app/src/main/java/com/magi/app/ui/`）。
  `docs/screen_port_map.md` は下調べ資料であって正ではない。
- 表示する情報・操作・状態（ロード前／実行中／結果あり）・文言を 3〜8 行にまとめ、`specs/<#>.md` に書く。
  `docs/history/INDEX.md`（Android）で版数を引き、決定記録（D7 読取モード撤去・D8 UD 固定・E5 月俯瞰は保留）に反する機能は移植しない。

## 3. 既存を突き合わせる
WinUI 側（`MagiApp.WinUI/Views/*.xaml(.cs)`・`MainWindow.xaml.cs`）を grep し、同等の導線が既にあれば
実装を「済(既存)」にして次の周へ（メモに根拠の関数名を書く）。部分的にある場合は差分だけ実装する。

## 4. 実装する
- ViewModel API が足りなければ `MagiApp.ViewModels` に追加し、`MagiApp.ViewModels.Tests` にテストを足す。
  エンジン（`MagiEngine`）の採否・重み・データ値は変えない（HF77）。
- 文言は日本語、Android と同じ語彙（`BreakdownLabels`／下流の語彙が正）。トークンは `Styles/MagiTheme.xaml`。
- 密なグリッドのハードコード値は既存の据え置き方針に従う。
- 変更は 1 行分に閉じる。ついでの改修はしない（見つけたら blockers.md の「気づき」に書く）。

## 5. 検証してコミットする
```
cd /home/user/-magi_pc/windows
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet test MagiApp.ViewModels.Tests/MagiApp.ViewModels.Tests.csproj -c Release --nologo -v q   # VM を触ったら必須
python3 ../tools/comment_ratio.py --staged                                                        # 追加コメントを 1 行ずつ判定
```
WinUI 本体はこのサンドボックスでコンパイルできない＝**CI（`windows-app-build.yml`、push から約 1.5 分）だけが判定**。
`using` 漏れ・型名は commit 前に既存コードの同種の書き方と突き合わせる。
commit message は「phase9 #N: <機能>」で始め、`queue.md` の該当行を「実装=済 CI=未」に更新して同じ commit に含める。push は `main`。

## 6. CI を待って行を閉じる
push 後は ScheduleWakeup で約 3 分後に起こし、`actions_list list_workflow_runs windows-app-build.yml` の最新 run を見る。
- 緑 → `queue.md` の CI を「緑」にして次の周へ（この更新は次の周の commit に同乗させてよい）。
- 赤 → `get_job_logs` でエラー行を読み、直して push（同じ行で 2 回まで）。2 回赤なら実装を「保留」にし、blockers.md に書く。

## 7. 逃げ道（止まらないため）
- 設計判断が要るときは止まらない。自分の推奨案で暫定実装し、blockers.md に「論点／採った案／却下した案／後から変える手間」を書く。
- 仕様が Android 側でも曖昧なときは Android の実装どおりに写す（解釈を足さない）。
- 同じ行で 2 回続けて失敗したら「保留」にして次の行へ。

## 8. 1ループの完了条件
`queue.md` のすべての行が「済／済(既存)／保留／対象外」になったときだけ、`<promise>PHASE9 QUEUE EMPTY</promise>` を出力し、
`windows/README.md` のフェーズ9の節に結果（済／保留の内訳と blockers の件数）を書いて commit する。
「未」「差戻」が 1 つでも残っているなら出力しない。疲れた・詰まった、を理由に出すのは禁止。詰まったものは §7 の「保留」へ落とす。
