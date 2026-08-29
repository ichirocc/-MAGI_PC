using System.Text;

namespace MagiEngine.Model;

/// <summary>
/// [フェーズ9 ピース7] model/MojibakeRepair.kt の逐語移植。
///
/// 二重エンコード文字化け（UTF-8 のバイト列を ISO-8859-1(Latin-1) として読み、再び UTF-8 で保存した
/// いわゆる二重化け型）の自動修復。Kotlin原本のKDocが記録する経緯どおり、CP1252/CP932 型の文字化けは
/// 対象外（0x80-0x9F 帯を誤読すると U+00FF 超の文字になり下の「本物の多バイト文字」ガードで必ず弾かれる
/// ＝構造的に修復対象外。安全側に不修復で返るだけで破壊はしない）。
///
/// MAGI 自身の読み書きは UTF-8 固定なので化けは作らないが、外部ツールや旧 Web 書き出しで
/// 二重エンコードされた JSON/CSV を読み込んだ場合に、表示が文字化けする。これを安全に元へ戻す。
///
/// 安全策（誤変換を避ける、Kotlin原本のKDocと同一）:
///  - 既に正しい多バイト文字(U+00FF 超＝本来の日本語)が含まれる → 化けではない → そのまま。
///  - 0x80..0xFF の拡張ラテン文字が無い(ASCII のみ) → 変換不要 → そのまま。
///  - 変換結果に U+FFFD(置換文字)が1つでも出る（＝不正な UTF-8 並び＝本物の Latin-1）→ 失敗とみなし元を返す
///    （all-or-nothing。部分修復は起きない）。
/// これらにより、二重エンコード UTF-8 のときだけ復元され、本物の Latin-1 テキストは保護される。
/// </summary>
public static class MojibakeRepair
{
    // 特殊文字は数値キャストで表現する（ソースへ非表示/制御文字を直接埋め込まない）。
    private const char Bom = (char)0xFEFF;              // UTF-8 BOM
    private const char ReplacementChar = (char)0xFFFD;   // 不正なUTF-8バイト列の既定置換文字
    private const char MaxLatin1 = (char)0x00FF;         // Latin-1 (ISO-8859-1) が表現できる最大コードポイント
    private const char MinExtendedLatin1 = (char)0x0080; // 拡張ラテン帯の下限（これ未満はASCII）

    public static string Repair(string s)
    {
        if (s.Length == 0) return s;
        var t = StripBom(s); // 先頭 UTF-8 BOM は常に除去（Trim()でも消えず、ヘッダ判定やcontainsを壊すため）
        if (t.Any(c => c > MaxLatin1)) return t;                                    // 本物の日本語等が既にある
        if (!t.Any(c => c >= MinExtendedLatin1 && c <= MaxLatin1)) return t;         // ASCII のみ
        try
        {
            var decoded = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(t));
            // [3.282.0の記録どおり] before(元の文字化け候補中の置換文字数)は上の >0xFF ガードで既に0が
            // 確定しているため、実態どおり after==0 のみを見る（全バイト列が正しいUTF-8として復元できた
            // 場合のみ採用）。
            var after = decoded.Count(c => c == ReplacementChar);
            return after == 0 && decoded.Any(c => c > MaxLatin1) ? decoded : t;
        }
        catch
        {
            // Kotlin原本の `catch (_: Exception)`。.NET の既定 UTF8 デコーダは不正列を例外でなく
            // 置換文字へ置換するため通常はここへ到達しないが、防御としてそのまま残す。
            return t;
        }
    }

    /// <summary>修復が必要(=変化する)かの判定。ログ表示用。</summary>
    public static bool LooksMojibake(string s) => Repair(s) != s;

    /// <summary>
    /// [3.282.0] 「本当に二重エンコードを復号したか」の判定。Repair() は BOM 除去だけでも
    /// 新しい文字列を返すため、呼出側の参照比較では「健全な BOM付きUTF-8 ファイル」でも毎回
    /// 『文字化けを自動修復』という誤った警告が出ていた（BOM は Windows 系エディタ由来で正常なファイル）。
    /// BOM を除いた本文が変化したときだけ true。
    /// </summary>
    public static bool WasDecoded(string original, string repaired) => repaired != StripBom(original);

    private static string StripBom(string s) => s.Length > 0 && s[0] == Bom ? s[1..] : s;
}
