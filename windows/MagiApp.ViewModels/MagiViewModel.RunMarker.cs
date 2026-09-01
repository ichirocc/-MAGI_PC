using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;

namespace MagiApp.ViewModels;

/// <summary>
/// [Phase 10 の第1段] <c>MagiViewModel.kt</c> の「プロセス強制終了の耐性: 実行中マーカー（中断検知）」
/// 節（行190-233）と、<c>init{}</c> の起動時復元（行298-370）の移植。
///
/// 実行開始時にマーカーを書き、正常終了で消す。プロセスが kill されるとマーカーが残るので、次回起動時に
/// 「前回の計算は中断された（入力は自動保存済み）」と気づかせ、再実行へ導く。共有ファイル4種
/// （入力・完了結果・途中最良・所有権マーカー）の後片付けは <see cref="Work.RunFiles"/> が担う。
///
/// [Kotlin原本との差 ①WorkManager] Kotlin の起動時復元には「WorkManager に未完了の Work が残って
/// いれば、それは中断ではなく**継続中**」という分岐（<c>bgActive</c>、行334-343）がある。Windows
/// デスクトップには WorkManager 相当（プロセス死を跨いで OS が再開させるジョブスケジューラ）が無く、
/// このC#移植にはバックグラウンド実行機構そのものが未実装なので、この分岐は**移植せず省略する**
/// （false 固定と同じ挙動＝マーカーが残っていれば必ず「中断」として案内する）。無いものを有るかのように
/// 見せない＝HF77 の精神。バックグラウンド実行を Windows 向けに設計した時点で、その機構が
/// 「まだ生きているか」を問い合わせる術をここへ足す。
///
/// [Kotlin原本との差 ②起動フックの形] Kotlin は <c>init{}</c> で <c>viewModelScope.launch</c> する
/// ＝ViewModel が生成された瞬間に自動で走る。この移植では <see cref="RestoreOnStartup"/> という
/// **公開メソッド**へ切り出し、シェル（<c>MagiApp.WinUI</c>、別アセンブリ）が起動時に明示的に呼ぶ形に
/// する。理由: ①コンストラクタからファイルI/Oを伴う非同期処理を起動すると、既存の 368 件のテストが
/// すべて実ホームディレクトリを読みに行くことになる（<see cref="DataDir"/> を注入する前に走ってしまう）
/// ②別アセンブリから呼べる必要があるため <c>internal</c> にはできない。挙動そのもの
/// （fire-and-forget＋復元順序）は Kotlin原本と同一。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>
    /// バックグラウンド実行の共有ファイル4種（入力・完了結果・途中最良・所有権マーカー）。
    /// Kotlin原本の <c>OptimizationWorker.files(ctx)</c>（<c>RunFiles(ctx.filesDir)</c>）相当。
    /// <see cref="DataDir"/> は差し替え可能なので、毎回組み立てる（Kotlin原本の <c>get()</c> と同じ）。
    /// </summary>
    private RunFiles BgFiles => new(DataDir);

    /// <summary>
    /// 実行中マーカー。<see cref="Work.RunFiles"/> の4ファイルとは別（あちらは背景実行の共有ファイル、
    /// これは前景・背景を問わず「実行中に落ちた」ことだけを示す）。
    /// </summary>
    private string RunMarkerFile => Path.Combine(DataDir, "magi_run_marker.json");

    /// <summary>
    /// この実行のマーカーを書く。<paramref name="mode"/> は "fg"（前景）か "bg"（背景）。
    /// Kotlin原本と同じく**原子置換を使わない**（<c>runMarkerFile.writeText</c>）。壊れて読めなければ
    /// 起動時の復元が既定文へフォールバックするだけで、失うものが無いため
    /// （<see cref="AtomicFileWrite"/> を使うのは、壊れると復元手段ごと失う入力/結果/途中最良の側）。
    /// 失敗は握り潰す（Kotlin原本の <c>runCatching{}</c> と同一）。
    /// </summary>
    private void WriteRunMarker(string mode)
    {
        try
        {
            Directory.CreateDirectory(DataDir);   // [置換] Android の filesDir と違い存在が保証されない
            var o = new JsonObject
            {
                ["startedAt"] = NowMs(),
                ["mode"] = mode,   // "fg" | "bg"
                ["budgetSec"] = Ui.BudgetSec,
                ["workers"] = Ui.Workers,
                // [置換] Kotlin の `v6Algorithm.name`（enum の宣言名）に相当する C# の表現は ToString()。
                //   綴りは Kotlin("AUTO") と C#("Auto") で異なるが、この値を読む経路は存在しない
                //   （起動時の復元が読むのは "mode" だけ）＝診断用の記録に留まる。
                ["algorithm"] = Ui.V6Algorithm.ToString(),
            };
            File.WriteAllText(RunMarkerFile, o.ToJsonString());
        }
        catch
        {
            // Kotlin原本と同じく握り潰す（マーカーが書けないこと自体では実行を止めない）。
        }
    }

    private void ClearRunMarker()
    {
        try
        {
            if (File.Exists(RunMarkerFile)) File.Delete(RunMarkerFile);
        }
        catch
        {
            // 同上（best-effort）。
        }
    }

    /// <summary>
    /// [3.428.0/#14] 背景実行の共有ファイルを消し、**消し残った名前を必ず記録する**。
    ///
    /// 3.410.0/B-06 で <see cref="Work.RunFiles.Clear"/> が消し残りを返すようにしたのに、その返り値を
    /// 読んでいたのは「背景計算の開始直前」の1箇所だけで、**残り9箇所は捨てていた**（自分で書いた契約の
    /// 取り残し）。消し残ると次回起動が入力・途中最良・マーカーを掴んで「中断されました・再開できます」と
    /// **失敗や停止を中断として誤案内**するのに、痕跡がどこにも残らない。消せないこと自体はここでは
    /// 直せないので、せめて後から読めるようにする。
    /// </summary>
    /// <param name="where">どの経路の掃除か（ログを読むときに原因を切り分けるため）。</param>
    /// <param name="keepRunId">所有権マーカーを残す（<see cref="Work.RunFiles.Clear"/> 参照）。</param>
    private void ClearBgFiles(string where, bool keepRunId = false)
    {
        IReadOnlyList<string> stuck;
        try
        {
            stuck = BgFiles.Clear(keepRunId);
        }
        catch (Exception e)
        {
            LogOp("W", $"{where}: 途中状態ファイルの削除に失敗しました（{e.GetType().Name}）");
            return;
        }
        if (stuck.Count > 0)
        {
            LogOp("W", $"{where}: 途中状態ファイルを削除できませんでした: {string.Join("・", stuck)}" +
                "（次回起動が古い状態を「中断」として掴む可能性があります）");
        }
    }

    // ===== 起動時の復元（Kotlin原本 init{} 行298-370） =====

    /// <summary>[テスト可視性のための追加] 直近の <see cref="RestoreOnStartup"/> 呼出しが背後で走らせる
    /// Task（<c>MagiViewModel.Persistence.cs</c> クラスKDocの「LastXxxTask」規約と同じ）。</summary>
    internal Task? LastRestoreOnStartupTask { get; private set; }

    /// <summary>
    /// 起動時の復元。前回の自動保存・中断マーカー・バックグラウンドの完了結果を読み、
    /// ①完了結果が読めればそれを反映 ②読めなければ中断バナー（<see cref="UiState.InterruptedRun"/>/
    /// <see cref="UiState.InterruptedInfo"/>）を立て、途中最良か自動保存から復元する。
    /// 最後に <c>_hydrated</c> を立てて自動保存を解禁する（復元前に空のドラフトで上書きしないため）。
    ///
    /// シェル（<c>MagiApp.WinUI</c>）が起動時に一度だけ呼ぶ。Kotlin原本の <c>init{}</c> と同じく
    /// fire-and-forget（完了を待たない）。
    /// </summary>
    public void RestoreOnStartup()
    {
        LastRestoreOnStartupTask = RestoreOnStartupCoreAsync();
    }

    private static string? ReadTextOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task RestoreOnStartupCoreAsync()
    {
        var files = BgFiles;
        // [並行I/O] 独立した3つのファイル読み込み（自動保存・中断マーカー・完了結果）は互いに依存しない
        //   ＝並行に走らせて待ち時間を重ね合わせ起動レイテンシを縮める（snapTxt は resultTxt に依存する
        //   ため後段で逐次に読む）。marker=中断検知（LoadAsync より先にフラグを立てる）、
        //   resultTxt=[C1] bg最適化の完了結果（UI不在でも完走済み＝最優先で採用）。
        var readAutosave = Task.Run(() => ReadTextOrNull(AutosaveFile));
        var readMarker = Task.Run(() => ReadTextOrNull(RunMarkerFile));
        var readResult = Task.Run(() => ReadTextOrNull(files.Result));
        await Task.WhenAll(readAutosave, readMarker, readResult);
        var txt = readAutosave.Result;
        var marker = readMarker.Result;
        var resultTxt = readResult.Result;

        // [判断設計監査 #3] 前回「データを開く」時の退避があれば復元導線（設定タブ）を有効化。
        var hasPrev = await Task.Run(() =>
        {
            try { return File.Exists(PrevBackupFile); } catch { return false; }
        });
        if (hasPrev) Ui.PrevBackupAvailable = true;

        // [3.406.0/B-02] **読めることを確かめてから**共有ファイルを消す。旧: 解析前に clearFiles して
        //   いたため、完了結果が壊れていると復元に使えたはずの入力・途中最良・マーカーまで同時に失い、
        //   利用者は何も取り戻せなかった。解析できなければ結果だけ捨てて、下の中断/途中結果の経路へ落とす。
        var resultUsable = !string.IsNullOrWhiteSpace(resultTxt) && await Task.Run(() =>
        {
            try { StateJsonSerializer.Parse(resultTxt!); return true; } catch { return false; }
        });
        if (!string.IsNullOrWhiteSpace(resultTxt) && !resultUsable)
        {
            LogOp("W", "前回のバックグラウンド最適化の完了結果が壊れていて読めませんでした（入力と途中結果は残してあります）");
            try { File.Delete(files.Result); } catch { /* best-effort */ }
        }
        if (resultUsable)
        {
            ClearRunMarker();
            ClearBgFiles("前回の完了結果を反映");
            // initialAssignment が state.schedule を返すため結果が復元される
            if (_state is null) LoadAsync(resultTxt!, markResult: true, fromRestore: true);
            LogOp("I", "前回のバックグラウンド最適化の結果を反映しました");
        }
        else
        {
            // [#4/C1] 中断時、途中最良解のスナップショットがあれば「途中結果から再開」する。
            var snapTxt = await Task.Run(() => ReadTextOrNull(files.Snapshot));
            // [Kotlin原本の bgActive 分岐は移植しない] クラスKDoc「Kotlin原本との差 ①WorkManager」参照。
            if (marker is not null)
            {
                var hasSnap = !string.IsNullOrWhiteSpace(snapTxt);
                string info;
                if (hasSnap)
                {
                    info = "前回の計算は中断されましたが、途中までの最良の勤務表から再開できます。『もう一度実行』で仕上げられます。";
                }
                else
                {
                    string? parsed = null;
                    try
                    {
                        var mode = JsonNode.Parse(marker)?["mode"]?.GetValue<string>() ?? "";
                        var modeJp = mode == "bg" ? "バックグラウンド" : "";
                        parsed = $"前回の{modeJp}計算は完了前に中断されました。入力は自動保存済みです。もう一度実行できます。";
                    }
                    catch
                    {
                        // 壊れたマーカーは既定文へフォールバック（Kotlin原本の getOrNull() ?: と同じ）。
                    }
                    info = parsed ?? "前回の計算は完了前に中断されました。入力は自動保存済みです。";
                }
                Ui.InterruptedRun = true;
                Ui.InterruptedInfo = info;
                ClearRunMarker();
                LogOp("W", hasSnap
                    ? "前回の中断を検知（途中結果あり＝再開可）"
                    : "前回の計算の中断を検知しました（入力は復元済み）");
            }
            if (_state is null)
            {
                // 途中最良解を優先して復元（無ければ自動保存の入力）。
                var resumeTxt = !string.IsNullOrWhiteSpace(snapTxt) ? snapTxt : txt;
                if (!string.IsNullOrWhiteSpace(resumeTxt)) LoadAsync(resumeTxt!, fromRestore: true);
                if (!string.IsNullOrWhiteSpace(snapTxt)) ClearBgFiles("途中結果の復元後");   // 消費後は掃除
            }
        }
        _hydrated = true;
    }
}
