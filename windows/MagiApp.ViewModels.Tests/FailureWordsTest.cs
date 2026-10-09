using System.Text.RegularExpressions;

namespace MagiApp.ViewModels.Tests;

/// <summary>[Android 3.643.0 同期] 失敗の一文は場面ごとに利用者の言葉で、例外のクラス名（英字）を含まない（Kotlin <c>FailureWordsTest</c> と 1 対 1）。</summary>
public class FailureWordsTest
{
    [Fact]
    public void EachBranchHasItsOwnWords()
    {
        Assert.Equal("書き出す内容がありませんでした", FailureWords.Of(null, FailureKind.Save));
        Assert.Equal("ファイルの中身を読めませんでした", FailureWords.Of(null, FailureKind.Load));
        Assert.Equal("メモリが足りませんでした", FailureWords.Of(new OutOfMemoryException(), FailureKind.Engine));
        Assert.Equal("アクセスが許可されていません", FailureWords.Of(new UnauthorizedAccessException("denied"), FailureKind.Load));
        Assert.Equal("ファイルが見つからないか、アクセスが許可されていません", FailureWords.Of(new FileNotFoundException("x"), FailureKind.Load));
        Assert.Equal("保存先の空き容量が足りません", FailureWords.Of(new IOException("No space left on device"), FailureKind.Save));
        Assert.Equal("ファイルが大きすぎます（上限 8 MB）", FailureWords.Of(new IOException("ファイルが大きすぎます（上限 8 MB）"), FailureKind.Load));
        Assert.Equal("書き込みに失敗しました", FailureWords.Of(new IOException("x"), FailureKind.Save));
        Assert.Equal("読み込みに失敗しました", FailureWords.Of(new IOException("x"), FailureKind.Load));
        Assert.Equal("ファイルの形式が違うか、壊れています", FailureWords.Of(new ArgumentException("bad json"), FailureKind.Load));
        Assert.Equal("内部エラー", FailureWords.Of(new InvalidOperationException("x"), FailureKind.Engine));
    }

    [Fact]
    public void NoClassNameOrAsciiLeaksToTheScreen()
    {
        var samples = new Exception?[] { null, new Exception("NullReferenceException at Foo.cs:12"), new NullReferenceException(), new InvalidOperationException(),
            new IOException("Stream closed"), new FileNotFoundException("/a/b.json"), new UnauthorizedAccessException(), new OutOfMemoryException(),
            new FormatException("Input string was not in a correct format."), new IndexOutOfRangeException(), new OperationCanceledException("cancel") };
        foreach (var e in samples)
            foreach (var k in Enum.GetValues<FailureKind>())
            {
                var w = FailureWords.Of(e, k);
                Assert.False(Regex.IsMatch(w, "[A-Za-z]"), $"英字を画面に出さない: {e?.GetType().Name} {k} → {w}");
            }
    }
}
