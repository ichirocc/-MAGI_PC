using System.Threading.Tasks;

namespace MagiApp.ViewModels;

/// <summary>
/// [Phase 10 → 2026-09-01 簡略化] 起動時の復元。旧 <c>MagiViewModel.RunMarker.cs</c>
/// （実行中マーカー・<c>RunFiles</c> ベースの背景実行スナップショット・起動時の
/// 「中断されました」検知）はここに一本化されていたが、ユーザー明示判断
/// 「クラッシュからの復旧はそこまで重視しない」（2026-09-01）により**全撤去**した
/// （経緯は <c>windows/README.md</c> フェーズ10節参照）。撤去したのは:
/// ①実行中マーカー（<c>magi_run_marker.json</c>・<c>MagiViewModel.Optimize.cs</c>/
/// <c>MagiViewModel.Background.cs</c> の開始/終了で書いていたもの）
/// ②<c>RunFiles</c>（背景実行専用の共有ファイル4種＝入力・完了結果・途中最良
/// スナップショット・所有権マーカー、8秒ごとの書込を含む）③起動時の「前回の計算は中断されました」
/// バナー（<c>UiState.InterruptedRun</c>/<c>InterruptedInfo</c>、両方とも撤去済み）。
///
/// **残したもの（クラッシュ復旧とは別の、通常運用のUX）**: ①自動保存（<c>magi_autosave.json</c>、
/// <c>MagiViewModel.Persistence.cs</c> の <see cref="AutoSave"/>/<see cref="SaveNow"/>）からの
/// 起動時復元——これは「編集のたびに継続的に保存され、次回起動時に前回の続きを開く」という
/// 通常運用の利便性で、クラッシュの有無に関係なく毎回使われる。撤去するとアプリを閉じて開き直す
/// たびに手動で「データを開く」が要る退行になるため対象外。②<c>PrevBackupAvailable</c>
/// （「データを開く」直前の退避＝<see cref="RestorePreviousData"/>）も別機能のため対象外。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>[テスト可視性のための追加] 直近の <see cref="RestoreOnStartup"/> 呼出しが背後で走らせる
    /// Task（<c>MagiViewModel.Persistence.cs</c> クラスKDocの「LastXxxTask」規約と同じ）。</summary>
    internal Task? LastRestoreOnStartupTask { get; private set; }

    /// <summary>
    /// 起動時の復元。自動保存があり、かつまだ何もロードされていなければそれを読み込む。
    /// 最後に <c>_hydrated</c> を立てて自動保存を解禁する（復元前に空のドラフトで上書きしないため）。
    ///
    /// シェル（<c>MagiApp.WinUI</c>）が起動時に一度だけ呼ぶ。ここで実際に読み込む
    /// <see cref="LoadAsync"/> 呼出し自体は fire-and-forget（完了を待たない——
    /// <see cref="UiState.Loaded"/>/<see cref="UiState.Running"/> の変化で画面が追従する）。
    /// </summary>
    public Task RestoreOnStartup()
    {
        var task = RestoreOnStartupCoreAsync();
        LastRestoreOnStartupTask = task;
        return task;
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

    /// <summary>
    /// [2026-09-01] <see cref="Work.AtomicFileWrite.WriteFileAtomically"/>（自動保存・開く前データの
    /// 退避が使う）は書込ごとに一意な名前の一時ファイル（<c>&lt;対象&gt;.t&lt;連番&gt;.tmp</c>）へ
    /// 書いてから対象へ rename する。通常は成功・失敗いずれの経路でも <c>finally</c> で即削除するが、
    /// 一時ファイル書込の**ごく短いウィンドウ中にプロセスがkillされた**場合だけ迷子として残り得る
    /// （ディスク容量を圧迫するほどの量にはならないが、放置する理由も無い）。<see cref="DataDir"/>
    /// 直下の <c>*.tmp</c> は現状この用途以外で作られないため、起動のたびに無条件で片付ける。
    /// </summary>
    private void CleanupStrayTempFiles()
    {
        string[] stray;
        try
        {
            stray = Directory.Exists(DataDir) ? Directory.GetFiles(DataDir, "*.tmp") : Array.Empty<string>();
        }
        catch
        {
            return; // best-effort; 列挙自体が失敗しても起動を止めない。
        }
        var removed = 0;
        foreach (var f in stray)
        {
            try { File.Delete(f); removed++; } catch { /* best-effort */ }
        }
        if (removed > 0)
            LogOp("I", $"起動時に迷子の一時ファイルを{removed}件片付けました（前回の書込中断の残骸）");
    }

    private async Task RestoreOnStartupCoreAsync()
    {
        CleanupStrayTempFiles();

        // [並行I/O] 独立した2つのファイル読み込み（自動保存・退避の有無）は互いに依存しない。
        var readAutosave = Task.Run(() => ReadTextOrNull(AutosaveFile));
        var readHasPrev = Task.Run(() =>
        {
            try { return File.Exists(PrevBackupFile); } catch { return false; }
        });
        await Task.WhenAll(readAutosave, readHasPrev);
        var txt = readAutosave.Result;

        // [判断設計監査 #3] 前回「データを開く」時の退避があれば復元導線（設定タブ）を有効化。
        if (readHasPrev.Result) Ui.PrevBackupAvailable = true;

        if (_state is null && !string.IsNullOrWhiteSpace(txt))
        {
            LoadAsync(txt!, fromRestore: true);
        }
        _hydrated = true;
    }
}
