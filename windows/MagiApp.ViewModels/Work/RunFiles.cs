namespace MagiApp.ViewModels.Work;

/// <summary>
/// [Phase 10] <c>work/RunFiles.kt</c> の <c>internal class RunFiles</c> の移植——バックグラウンド
/// 最適化が使う**共有ファイルの所有権と原子的な書き込み**。Kotlin原本のKDocが記録するとおり、この
/// クラスは <c>Context</c> ではなく**ディレクトリ1つ**だけを受け取る（「所有権の判定」「後片付けの
/// 網羅」「原子置換」はディレクトリだけで表現できる＝テストできる場所へ動かして初めて再発防止になる）。
/// したがって Android 固有APIへの依存がゼロで、この移植も**逐語**で済む。
///
/// [プラットフォーム置換] ①<c>java.io.File</c> → 絶対パス文字列（<c>MagiViewModel.DataDir</c> と
/// 同じ表現。<c>MagiViewModel.Persistence.cs</c> の <c>AutosaveFile</c>/<c>PrevBackupFile</c> の
/// 先例に合わせる）②ディレクトリ自体は Android の <c>filesDir</c> と違い**存在が保証されない**ため、
/// 書き込み側（<see cref="BeginRun"/>）で <c>Directory.CreateDirectory</c> する
/// （<see cref="AtomicFileWrite.WriteFileAtomically"/> が既に同じことをしているのと同じ理由）。
/// 失敗時の扱い（false を返す／握り潰す）は Kotlin原本の <c>runCatching{}.getOrDefault(...)</c> と同一。
///
/// [Kotlin原本との微差（直さずに記録する＝HF77）] <see cref="Clear"/> の存在判定に使う
/// <c>File.Exists</c> は**ディレクトリに対して false を返す**（Java の <c>File.exists()</c> は true）。
/// 4つのパスはいずれもファイルとしてしか作られないので実挙動は同じだが、もし何かがそのパスに
/// ディレクトリを作ってしまった場合、Kotlin原本は「消せなかった」と報告するのに対しこの移植は
/// 「消すものが無かった」と扱う。逐語移植の範囲を超えるので直さず、ここに残す。
///
/// [Kotlin原本のKDocが「ここでは守れない」と明記している範囲] Worker のライフサイクル
/// （耐久保存→公開の順序・失敗パスが所有権を閉じること）と、所有確認〜置き換えの間の TOCTOU は
/// このクラスの外側の性質なので、単体テストでは捕まらない。この移植でも同じ（そもそも Windows 側の
/// バックグラウンド実行機構＝<c>OptimizationWorker.kt</c> 相当は未実装）。
///
/// [現時点で実際に使われるのはどれか（正直に）] このC#移植では <see cref="RunId"/>（所有権マーカー）と
/// <see cref="Clear"/>（後片付け）は <c>MagiViewModel</c> から使われるが、<see cref="Input"/>/
/// <see cref="Result"/>/<see cref="Snapshot"/> へ**書く**主体（バックグラウンド実行タスク）はまだ無い。
/// 起動時の復元（<c>MagiViewModel.RestoreOnStartup</c>）は読む側だけを移植してある——
/// 書き手が現れたときに読む側が無い、という取り残しを作らないため。
/// </summary>
public sealed class RunFiles
{
    private readonly string _dir;

    /// <param name="dir">共有ファイルを置くディレクトリ（Kotlin原本の <c>ctx.filesDir</c> 相当。
    /// このC#移植では <c>MagiViewModel.DataDir</c>）。</param>
    public RunFiles(string dir) => _dir = dir;

    /// <summary>kill 後の再起動でここから復元する入力。</summary>
    public string Input => Path.Combine(_dir, "magi_bg_input.json");

    /// <summary>完了結果。UI 不在で完走しても次回起動で反映できるように残す。</summary>
    public string Result => Path.Combine(_dir, "magi_bg_result.json");

    /// <summary>途中最良のスナップショット（8秒ごと退避＝実質の途中再開）。</summary>
    public string Snapshot => Path.Combine(_dir, "magi_bg_best.json");

    /// <summary>
    /// いま所有権を持つ実行の ID。ファイル名は固定・置き換え（<c>ExistingWorkPolicy.REPLACE</c>）で
    /// 入れ替わるため、**どの実行が書いたファイルか**を区別する術がこれしかない（3.327.0）。
    /// </summary>
    public string RunId => Path.Combine(_dir, "magi_bg_run.txt");

    /// <summary>
    /// この実行の所有権マーカーを立てる。**書けたかどうかを返す**（3.406.0/B-01）。
    /// 旧: 失敗を握り潰していたため、書けなくても Work が投入され、Worker 側は
    /// <c>activeRunId()==0 ≠ 自分のid</c> で所有権なしと判定して**何もせず即 return**——
    /// 画面だけ「開始しました」のまま実行中が永久に残る、という無言の失敗になっていた。
    /// </summary>
    public bool BeginRun(long id)
    {
        try
        {
            Directory.CreateDirectory(_dir);   // [置換] Android の filesDir と違い存在が保証されない
            File.WriteAllText(RunId, id.ToString());
            return File.ReadAllText(RunId).Trim() == id.ToString();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>記録が無い・壊れているときは 0（＝誰も所有していない）。</summary>
    public long ActiveRunId()
    {
        try
        {
            return long.Parse(File.ReadAllText(RunId).Trim());
        }
        catch
        {
            return 0L;
        }
    }

    /// <summary>
    /// この実行が共有ファイルの所有者か。
    /// <list type="bullet">
    /// <item><c>mine == 0L</c>：runId を持たない旧経路 → 従来どおり所有者として扱う（非破壊）。</item>
    /// <item>置き換え（REPLACE）で新しい実行が <see cref="BeginRun"/> を書くと旧実行はここで false に
    /// なり、**書き込みも削除も一切しなくなる**。停止（<see cref="Clear"/> で runId 消去）も同様。</item>
    /// </list>
    /// </summary>
    public bool Owns(long mine) => mine == 0L || ActiveRunId() == mine;

    /// <summary>
    /// 4ファイルすべてを消す。**1つでも足すのを忘れると次回起動が古い状態を掴む**ので、
    /// <c>RunFilesTest</c> が「clear 後に dir が空」で網羅を固定している。
    ///
    /// [3.410.0/B-06] **消し残った名前を返す**。旧: <c>delete()</c> の戻り値も例外も捨てており、
    /// 消えなかったファイルがあっても呼出側に届かなかった＝**残ったファイルを次回起動が
    /// 「中断されました・再開できます」として掴む**。所有権マーカー(<see cref="RunId"/>)が消え残ると、
    /// なお悪く新しい実行が所有者になれない。返り値が空でないときは呼出側が記録する
    /// （消せないこと自体はここでは直せない）。
    /// </summary>
    /// <param name="keepRunId">所有権マーカーを残す。[3.410.0/U-02]「所有権を立ててから旧途中状態を
    /// 掃除する」順序では、ここで runId まで消すと**自分で立てたばかりの所有権を自分で捨てる**ことになる。</param>
    /// <returns>消し残ったファイル名（拡張子込みのファイル名のみ。Kotlin原本の <c>f.name</c> と同じ）。</returns>
    public IReadOnlyList<string> Clear(bool keepRunId = false)
    {
        var stuck = new List<string>();
        var targets = new List<string> { Input, Result, Snapshot };
        if (!keepRunId) targets.Add(RunId);
        foreach (var f in targets)
        {
            bool ok;
            try
            {
                if (File.Exists(f)) { File.Delete(f); ok = true; }
                else ok = true;
            }
            catch
            {
                ok = false;
            }
            if (!ok) stuck.Add(Path.GetFileName(f));
        }
        return stuck;
    }

    /// <summary>
    /// 一時ファイル経由の原子置換（3.336.0 S3）。素の <c>writeText</c> は非原子で、書き込み途中に落ちると
    /// **壊れた JSON が残る**。起動時の復元は「結果が空でなければマーカーも入力も掃除してから読む」ため、
    /// 壊れたファイルは「結果も再開手段も両方失う」経路になっていた。
    ///
    /// Kotlin原本の <c>RunFiles.writeAtomically</c> は同ファイルのトップレベル関数
    /// <c>writeFileAtomically</c> へ委譲するだけの薄いラッパー。その実体は
    /// <see cref="AtomicFileWrite.WriteFileAtomically"/> としてフェーズ9で先行移植済みなので、
    /// ここでも**委譲するだけ**にする（同じ処理を写すと必ず片方が取り残される＝Kotlin原本のKDocが
    /// ファイルレベルへ出した理由そのもの）。
    /// </summary>
    /// <param name="target">書込先。</param>
    /// <param name="text">書き込む内容。</param>
    /// <param name="onNonAtomic">rename が使えず**原子性を諦めて直接書いた**ときに呼ぶ（3.428.0/#7）。</param>
    /// <param name="commitGuard">置き換えの**直前**に呼ぶ（3.385.0）。false なら一時ファイルだけ捨て、
    /// <paramref name="target"/> には一切触れない。所有権の再確認をここへ置くと、直列化と一時ファイル
    /// 書き込みのぶん（ms 級）が TOCTOU の窓から外れる。**窓が消えるわけではない。**</param>
    /// <returns><paramref name="target"/> に <paramref name="text"/> が入ったなら true。</returns>
    public bool WriteAtomically(
        string target,
        string text,
        Action? onNonAtomic = null,
        Func<bool>? commitGuard = null) =>
        AtomicFileWrite.WriteFileAtomically(target, text, onNonAtomic, commitGuard);
}
