using MagiApp.ViewModels.Work;

namespace MagiApp.ViewModels.Tests.Work;

/// <summary>
/// [フェーズ9 ピース7] <see cref="AtomicFileWrite.WriteFileAtomically"/>（work/RunFiles.kt の
/// トップレベル関数 <c>writeFileAtomically</c> の移植）の検証。Kotlin原本には専用テストが無いため、
/// C#移植で新規に固定する。
///
/// 各テストは独立した一時ディレクトリ（<see cref="FreshTempDir"/>）を使う——並列実行される他の
/// テストクラスとファイルパスが衝突しないため、このクラスは共有 static 状態（
/// <see cref="Work.OptimizationRepository"/>）に触れず <c>[Collection]</c> 不要（他クラスと並列に走ってよい）。
/// </summary>
public class AtomicFileWriteTest : IDisposable
{
    private readonly List<string> _dirs = new();

    private string FreshTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "magi-atomicwrite-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void WritesTheFileAndReturnsTrue()
    {
        var target = Path.Combine(FreshTempDir(), "out.json");

        var ok = AtomicFileWrite.WriteFileAtomically(target, "hello world");

        Assert.True(ok);
        Assert.Equal("hello world", File.ReadAllText(target));
    }

    [Fact]
    public void CreatesTheParentDirectoryWhenMissing()
    {
        var dir = FreshTempDir();
        var target = Path.Combine(dir, "nested", "deep", "out.json");

        var ok = AtomicFileWrite.WriteFileAtomically(target, "content");

        Assert.True(ok);
        Assert.True(File.Exists(target));
        Assert.Equal("content", File.ReadAllText(target));
    }

    [Fact]
    public void OverwritesExistingContent()
    {
        var target = Path.Combine(FreshTempDir(), "out.json");
        AtomicFileWrite.WriteFileAtomically(target, "first");

        var ok = AtomicFileWrite.WriteFileAtomically(target, "second");

        Assert.True(ok);
        Assert.Equal("second", File.ReadAllText(target));
    }

    [Fact]
    public void LeavesNoLeftoverTempFileAfterASuccessfulWrite()
    {
        var dir = FreshTempDir();
        var target = Path.Combine(dir, "out.json");

        AtomicFileWrite.WriteFileAtomically(target, "content");

        var remaining = Directory.GetFiles(dir);
        Assert.Single(remaining);
        Assert.Equal(target, remaining[0]);
    }

    /// <summary>
    /// commitGuard が false を返した場合、一時ファイルへは既に書き終えているが対象ファイルへは
    /// 一切触れない（既存内容がそのまま残る）ことを固定する。
    /// </summary>
    [Fact]
    public void CommitGuardFalseAbortsWithoutTouchingTheTargetOrLeavingATempFile()
    {
        var dir = FreshTempDir();
        var target = Path.Combine(dir, "out.json");
        File.WriteAllText(target, "original");

        var ok = AtomicFileWrite.WriteFileAtomically(target, "new content", commitGuard: () => false);

        Assert.False(ok);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(dir)); // only the (untouched) target — no .tmp leftover
    }

    /// <summary>
    /// [レビュー指摘 2026-09-04] rename に失敗して直接書きへ落ちるとき、所有権（commitGuard）をもう一度確認する。
    /// 旧: 最初の確認のあと所有権が移っていても古い writer が target を書けた。
    /// </summary>
    [Fact]
    public void CommitGuardIsReEvaluatedBeforeTheNonAtomicFallbackWrites()
    {
        var dir = FreshTempDir();
        var target = Path.Combine(dir, "out.json");
        File.WriteAllText(target, "original");
        var calls = 0;
        var nonAtomic = false;

        var ok = AtomicFileWrite.WriteFileAtomically(target, "new content",
            onNonAtomic: () => nonAtomic = true,
            commitGuard: () => ++calls == 1,                       // 1回目=所有、2回目=所有権を失った
            move: (_, _) => throw new IOException("rename unavailable"));

        Assert.False(ok);
        Assert.Equal(2, calls);
        Assert.False(nonAtomic);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(dir));

        // 所有権が続いていれば従来どおり直接書きへ落ち、諦めたことを知らせる。
        calls = 0;
        Assert.True(AtomicFileWrite.WriteFileAtomically(target, "newer",
            onNonAtomic: () => nonAtomic = true,
            commitGuard: () => { calls++; return true; },
            move: (_, _) => throw new IOException("rename unavailable")));
        Assert.True(nonAtomic);
        Assert.Equal("newer", File.ReadAllText(target));
    }

    /// <summary>
    /// commitGuard は一時ファイルへの書込みが完了した**後**、対象への置換の**前**に呼ばれる
    /// （書込み中の内容を見てから採否を決められる、という契約）。
    /// </summary>
    [Fact]
    public void CommitGuardIsCalledAfterTheTempFileIsAlreadyWritten()
    {
        var dir = FreshTempDir();
        var target = Path.Combine(dir, "out.json");
        var sawTempFileDuringGuard = false;
        var sawTargetDuringGuard = true; // asserted false below — target must not exist yet at guard time

        AtomicFileWrite.WriteFileAtomically(target, "content", commitGuard: () =>
        {
            sawTempFileDuringGuard = Directory.GetFiles(dir).Any(f => f.EndsWith(".tmp"));
            sawTargetDuringGuard = File.Exists(target);
            return true;
        });

        Assert.True(sawTempFileDuringGuard);
        Assert.False(sawTargetDuringGuard);
    }

    /// <summary>
    /// 正常系（同一ファイルシステム内・rename が使える環境）では onNonAtomic は一度も呼ばれない
    /// （最善努力フォールバックの経路は rename が失敗した場合専用）。
    /// </summary>
    [Fact]
    public void OnNonAtomicIsNotInvokedOnANormalSuccessfulWrite()
    {
        var target = Path.Combine(FreshTempDir(), "out.json");
        var invoked = false;

        AtomicFileWrite.WriteFileAtomically(target, "content", onNonAtomic: () => invoked = true);

        Assert.False(invoked);
    }

    /// <summary>
    /// [KDocが記録する動機の直接検証] 呼出ごとに一意な一時ファイル名を使うため、同じ対象ファイルへ
    /// 複数の書込みが本当に重なっても（固定名のときのように）一時ファイルを奪い合わない。
    /// 全呼出しが例外なく成功し、書込み後に一時ファイルの残骸が一切残らず、最終的な対象ファイルの
    /// 内容は競合したいずれかの呼出しの内容と完全に一致する（破損・混在しない）ことを確認する。
    /// </summary>
    [Fact]
    public async Task ConcurrentWritesToTheSameTargetNeverCorruptOrLeaveTempFileLeftovers()
    {
        var dir = FreshTempDir();
        var target = Path.Combine(dir, "out.json");
        const int n = 16;
        var contents = Enumerable.Range(0, n).Select(i => $"payload-{i}").ToArray();

        var results = await Task.WhenAll(contents.Select(c =>
            Task.Run(() => AtomicFileWrite.WriteFileAtomically(target, c))));

        Assert.All(results, r => Assert.True(r));
        var remaining = Directory.GetFiles(dir);
        Assert.Single(remaining); // only the target — no ".tN.tmp" leftovers from any racing writer
        Assert.Equal(target, remaining[0]);
        var finalContent = File.ReadAllText(target);
        Assert.Contains(finalContent, contents); // last-writer-wins, but never a corrupted/interleaved blend
    }
}
