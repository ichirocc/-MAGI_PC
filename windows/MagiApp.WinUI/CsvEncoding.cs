using System.Text;

namespace MagiApp.WinUI;

/// <summary>
/// [Kotlin原本 <c>decodeCsvBytes</c>（<c>MagiApp.kt</c> 140-149行）の移植] CSV取込の文字コード
/// 自動判定。この機能は <c>MagiViewModel.kt</c>（ViewModel層）ではなく Compose 側の
/// ファイル選択コード（<c>ui/MagiApp.kt</c>）にあった——バイト列→文字列のデコードは Android の
/// <c>ContentResolver</c> を介したファイルI/Oの一部で、プラットフォーム層の責務だったため。
/// この移植でも同じ理由で <c>MagiApp.ViewModels</c>（プラットフォーム非依存）ではなく
/// <c>MagiApp.WinUI</c>（このファイル）に置く——<c>MagiViewModel.ImportCsvSmart</c> 等が受け取るのは
/// 既にデコード済みの <c>string</c> であり、そこに至る前の生バイトの扱いはこの移植でも呼出元
/// （ファイルピッカーで読んだ側）の責務のまま。
///
/// 病院などの「勤務表テンプレCSV」は Excel 由来で CP932（Shift-JIS 系）で保存されることが多い。
/// 手順: ①UTF-8 として**厳密**デコードを試す（不正なバイト列があれば失敗とみなす）
/// ②失敗すれば MS932（Windows-31J、日本語Windows既定）でデコード（レガシーコードページの
/// デコードは通常失敗しない＝不正バイトは既定の置換文字に落ちるだけ）③それも失敗すれば
/// 緩いUTF-8（不正バイト列を置換文字化）にフォールバック（Kotlin原本の <c>runCatching{}.getOrElse{}</c>
/// と同じ最終防御）。先頭のUTF-8 BOMは常に取り除く。
/// </summary>
public static class CsvEncoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static Encoding? _ms932;

    private static Encoding? Ms932()
    {
        if (_ms932 is not null) return _ms932;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _ms932 = Encoding.GetEncoding(932);
        }
        catch
        {
            _ms932 = null;
        }
        return _ms932;
    }

    public static string DecodeCsvBytes(byte[] bytes)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                text = (Ms932() ?? Encoding.UTF8).GetString(bytes);
            }
            catch
            {
                text = Encoding.UTF8.GetString(bytes);
            }
        }
        return text.TrimStart(Bom);
    }

    private const char Bom = (char)0xFEFF; // UTF-8 BOM（MojibakeRepair.cs と同じ数値キャスト表現）
}
