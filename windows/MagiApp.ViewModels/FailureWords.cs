namespace MagiApp.ViewModels;

/// <summary>失敗の場面。Load＝ファイルの読込・取込、Save＝書き出し、Engine＝探索・チェック・下書き（Kotlin <c>FailureKind</c>）。</summary>
public enum FailureKind { Load, Save, Engine }

/// <summary>失敗を利用者の言葉へ（Kotlin <c>ui/FailureWords.kt</c>。3.147.0/3.191.0/3.400.0 の方針: 例外のクラス名と生の例外文を画面に出さない）。
/// 詳しい原因は呼び出し側が LogOp へ残す。純関数＝WinUI から切り離してテストする。</summary>
public static class FailureWords
{
    public static string Of(Exception? e, FailureKind kind) => e switch
    {
        null => kind switch { FailureKind.Save => "書き出す内容がありませんでした", FailureKind.Load => "ファイルの中身を読めませんでした", _ => "原因不明" },
        OutOfMemoryException or InsufficientMemoryException => "メモリが足りませんでした",
        StackOverflowException or AccessViolationException => "重大なエラー",
        UnauthorizedAccessException => "アクセスが許可されていません",
        FileNotFoundException => "ファイルが見つからないか、アクセスが許可されていません",
        _ when e.Message.Contains("space", StringComparison.OrdinalIgnoreCase) => "保存先の空き容量が足りません",
        IOException io when io.Message.StartsWith("ファイルが大きすぎます", StringComparison.Ordinal) => io.Message,   // 取込サイズ上限の文はそのまま（利用者の言葉）
        IOException => kind switch { FailureKind.Save => "書き込みに失敗しました", FailureKind.Load => "読み込みに失敗しました", _ => "読み書きに失敗しました" },
        _ => kind switch { FailureKind.Save => "書き込みに失敗しました", FailureKind.Load => "ファイルの形式が違うか、壊れています", _ => "内部エラー" },
    };
}
