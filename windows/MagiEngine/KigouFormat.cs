namespace MagiEngine;

/// <summary>
/// Faithful (best-effort) port of Kotlin's <c>toHankakuKigou</c>
/// (<c>app/src/main/java/com/magi/app/KigouFormat.kt</c>) — a **display-only** helper that
/// normalizes fullwidth shift/group symbols to halfwidth for on-screen presentation.
///
/// Example: 「Dテ」→「Dﾃ」 / 「Aア」→「Aｱ」 / 「Cオ」→「Cｵ」 / 「Ｂ４」→「B4」. Already-halfwidth
/// symbols (Dﾃ, B4, ...) and kanji (休, 有, ...) pass through unchanged (idempotent).
///
/// Important (mirrors the Kotlin doc comment verbatim): this is display-only. The underlying
/// data (shift/group kigou strings) is never rewritten by this helper, so it has zero effect on
/// CSV/engine evaluation/constraint matching/colour resolution — it only affects how a symbol is
/// *shown*.
///
/// The Kotlin original delegates to Android's <c>android.icu.text.Transliterator</c> with the
/// "Fullwidth-Halfwidth" transform ID, gracefully falling back to identity (return the input
/// unchanged) when that ICU transform is unavailable in the running environment (its own comment:
/// "ICU の変換器が利用できない環境では原文をそのまま返し、クラッシュさせない"). .NET/WinUI has no
/// bundled equivalent of that specific ICU transliterator ID, so this port implements the
/// mechanical Unicode mapping directly rather than pulling in an ICU dependency:
///
///  1. Fullwidth ASCII (U+FF01-FF5E) -&gt; ASCII (U+0021-007E): a well-defined constant offset
///     (0xFEE0), exact for the entire range — this part is bit-for-bit equivalent to ICU's rule.
///  2. Fullwidth katakana (unvoiced/seion only: ア..ン, small forms, the long vowel mark) -&gt;
///     halfwidth katakana: a direct lookup table. Voiced (dakuten: ガ...) and semi-voiced
///     (handakuten: パ...) katakana are intentionally NOT included — MAGI's actual shift/group
///     symbol vocabulary observed throughout this project's history is entirely unvoiced single
///     katakana codes (e.g. Aｱ, Cｵ, Dﾃ, Pｼ), and any symbol outside this table degrades gracefully
///     to being left as-is (same "don't crash, just don't transliterate" spirit as the Kotlin
///     original's ICU-unavailable fallback), rather than attempting a full byte-exact
///     reimplementation of ICU's dakuten-decomposition rules for a purely cosmetic helper that no
///     engine-correctness path (parity tests, constraint matching) ever reads.
/// </summary>
public static class KigouFormat
{
    private const int FullwidthAsciiOffset = 0xFEE0;

    private static readonly IReadOnlyDictionary<char, char> FullwidthKatakanaToHalf = new Dictionary<char, char>
    {
        ['ア'] = 'ｱ', ['イ'] = 'ｲ', ['ウ'] = 'ｳ', ['エ'] = 'ｴ', ['オ'] = 'ｵ',
        ['カ'] = 'ｶ', ['キ'] = 'ｷ', ['ク'] = 'ｸ', ['ケ'] = 'ｹ', ['コ'] = 'ｺ',
        ['サ'] = 'ｻ', ['シ'] = 'ｼ', ['ス'] = 'ｽ', ['セ'] = 'ｾ', ['ソ'] = 'ｿ',
        ['タ'] = 'ﾀ', ['チ'] = 'ﾁ', ['ツ'] = 'ﾂ', ['テ'] = 'ﾃ', ['ト'] = 'ﾄ',
        ['ナ'] = 'ﾅ', ['ニ'] = 'ﾆ', ['ヌ'] = 'ﾇ', ['ネ'] = 'ﾈ', ['ノ'] = 'ﾉ',
        ['ハ'] = 'ﾊ', ['ヒ'] = 'ﾋ', ['フ'] = 'ﾌ', ['ヘ'] = 'ﾍ', ['ホ'] = 'ﾎ',
        ['マ'] = 'ﾏ', ['ミ'] = 'ﾐ', ['ム'] = 'ﾑ', ['メ'] = 'ﾒ', ['モ'] = 'ﾓ',
        ['ヤ'] = 'ﾔ', ['ユ'] = 'ﾕ', ['ヨ'] = 'ﾖ',
        ['ラ'] = 'ﾗ', ['リ'] = 'ﾘ', ['ル'] = 'ﾙ', ['レ'] = 'ﾚ', ['ロ'] = 'ﾛ',
        ['ワ'] = 'ﾜ', ['ヲ'] = 'ｦ', ['ン'] = 'ﾝ',
        ['ァ'] = 'ｧ', ['ィ'] = 'ｨ', ['ゥ'] = 'ｩ', ['ェ'] = 'ｪ', ['ォ'] = 'ｫ',
        ['ッ'] = 'ｯ', ['ャ'] = 'ｬ', ['ュ'] = 'ｭ', ['ョ'] = 'ｮ',
        ['ー'] = 'ｰ',
    };

    /// <summary>Normalizes fullwidth ASCII and unvoiced fullwidth katakana in <paramref name="s"/>
    /// to halfwidth; every other character (including kanji, already-halfwidth text, and voiced
    /// katakana — see the class doc comment) passes through unchanged.</summary>
    public static string ToHankakuKigou(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        Span<char> buf = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '！' && c <= '～')
            {
                buf[i] = (char)(c - FullwidthAsciiOffset);
            }
            else if (FullwidthKatakanaToHalf.TryGetValue(c, out var half))
            {
                buf[i] = half;
            }
            else
            {
                buf[i] = c;
            }
        }
        return new string(buf);
    }
}
