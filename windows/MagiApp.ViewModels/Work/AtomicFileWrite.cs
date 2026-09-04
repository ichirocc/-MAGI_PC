using System.Threading;

namespace MagiApp.ViewModels.Work;

/// <summary>
/// [フェーズ9 ピース7] work/RunFiles.kt のトップレベル関数
/// <c>writeFileAtomically(target, text, onNonAtomic, commitGuard)</c> の移植。
///
/// Kotlin原本のKDocが明記するとおり、この関数は「run ファイル以外（自動保存など）からも使えるように
/// ファイルレベルへ出してある」（<c>RunFiles.writeAtomically</c> インスタンスメソッドはこの関数へ
/// 委譲するだけの薄いラッパー）。<c>RunFiles</c> クラス本体（バックグラウンド実行タスクの所有権チェック等）は
/// Phase 10（背景実行）のスコープのままだが、この原子書込ユーティリティ自体は
/// <c>MagiViewModel</c> の自動保存（<c>AutoSave</c>/<c>SaveNow</c>）・退避（<c>LoadAsync</c>内の
/// 「開く前のデータ」退避）が今すぐ必要とするため先行移植する。
///
/// 一時ファイル経由の原子置換。呼出ごとに一意な名前の一時ファイルへ書いてから対象へ rename する
/// （固定名だと、対象ファイルへの書込が重なった場合に2つの writer が同じ一時ファイルを奪い合いうる——
/// Kotlin原本のKDocはこの事故が実際に4件のテスト失敗として捕まった経緯を記録している）。
/// rename が使えない環境（別ファイルシステム跨ぎ等）では原子性を諦めて直接書く（最善努力）。
/// </summary>
public static class AtomicFileWrite
{
    private static long _atomicWriteSeq;

    /// <param name="target">書込先の絶対パス。親ディレクトリが存在しなければ作成する。</param>
    /// <param name="text">書き込む内容。</param>
    /// <param name="onNonAtomic">rename が使えず原子性を諦めて直接書いたことを呼出側へ知らせるコールバック
    /// （Kotlin原本の <c>@Volatile</c> フラグ経由の通知に相当。この移植では単純な呼出しコールバックに
    /// 置き換える——ViewModel/UIスレッド境界の懸念はC#側では呼出元の責務とする）。</param>
    /// <param name="commitGuard">一時ファイルへ書き終えた直後、コミット（rename/直接書き）の直前に呼ぶ。
    /// false を返すと対象ファイルへは一切触れず false を返す（一時ファイルは finally で削除される）。</param>
    /// <returns>text が target に入ったなら true（commitGuard が false を返したときだけ false）。
    /// 一時ファイルへの書込自体が失敗した場合は例外がそのまま呼出元へ伝播する（Kotlin原本と同じく
    /// この関数自身は書込例外を握り潰さない——握り潰すのは呼出側の役割）。</returns>
    public static bool WriteFileAtomically(
        string target,
        string text,
        Action? onNonAtomic = null,
        Func<bool>? commitGuard = null,
        Action<string, string>? move = null)   // [レビュー指摘 2026-09-04] テストから rename 失敗を再現するための注入点
    {
        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path.Combine(
            string.IsNullOrEmpty(dir) ? "." : dir,
            Path.GetFileName(target) + ".t" + Interlocked.Increment(ref _atomicWriteSeq) + ".tmp");
        try
        {
            File.WriteAllText(tmp, text);
            if (commitGuard is not null && !commitGuard()) return false;
            try
            {
                // 成功時の rename 後は既に tmp が存在しない（下の finally で確認する）。
                if (move is null) File.Move(tmp, target, overwrite: true); else move(tmp, target);
            }
            catch
            {
                // rename が使えない環境（別ファイルシステム跨ぎ等）では原子性を諦めて直接書く＝最善努力。
                // この経路で書いている間にプロセスが落ちると壊れたファイルが残り、起動時の復元が
                // 「結果も再開手段も両方失う」形になりうる——原子置換を入れた動機そのものなので、
                // 諦めたこと自体は必ず呼出側へ知らせる。
                // [レビュー指摘 2026-09-04] 直接書きへ落ちる前に所有権（commitGuard）をもう一度確認する。
                //   旧: 最初の確認から直接書きまでの間に所有権が移っていても古い writer が target を書けた。
                if (commitGuard is not null && !commitGuard()) return false;
                onNonAtomic?.Invoke();
                File.WriteAllText(target, text);
            }
            return true;
        }
        finally
        {
            // 成功時のrename後は既に消えている。失敗・ガード偽・例外のいずれでも残骸を残さない
            // （best-effort：削除自体の失敗は無視する＝Kotlin原本の `runCatching { ... }` と同じ扱い）。
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
                // best-effort cleanup; 失敗しても呼出元の結果には影響させない。
            }
        }
    }
}
