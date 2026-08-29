namespace MagiEngine.V6;

/// <summary>
/// [フェーズ9] シフト記号 → 表示色 と 違反キー → 重大度 の唯一の解決元
/// （<c>ShiftAppearance.kt</c> の逐語移植）。
///
/// [3.393.0の由来をそのまま記録] Kotlin側は旧 <c>V6WebCompat</c>（存在しない Web 版の非DOM
/// ヘルパーを移植したもの）から、実際に使う4関数だけを切り出した経緯を持つ。C#移植では最初から
/// この4関数のみを対象とする（Web版相当・呼出ゼロだったヘルパー群は移植対象外）。
/// </summary>
public static class ShiftAppearance
{
    /// <summary>
    /// [フェーズ9] <c>MagiTokens.kt</c> の <c>ensureReadable(bg, preferred, minRatio)</c> の逐語移植。
    ///
    /// 前景色 <paramref name="preferredHex"/> が背景 <paramref name="bgHex"/> に対し
    /// <paramref name="minRatio"/>（既定 4.5＝通常テキストのWCAG基準）以上のコントラストを持てば
    /// そのまま採用。不足する場合のみ、純白(#FFFFFF)/純黒(#000000)のうちコントラストが高い方へ
    /// フォールバックする（<see cref="PickTextColor"/> が使う生成り/濃色の2色とは別物＝混同しない）。
    /// 描画時のみの補正で、保存済みの色データを書き換える意図の関数ではない（HF77 セーフ）。
    /// </summary>
    public static string EnsureReadable(string bgHex, string preferredHex, double minRatio = 4.5)
    {
        var bg = ParseHex(bgHex);
        // bg が解釈できなければコントラストを判定できないため、指定色をそのまま返す
        // （Kotlin原本は Color 型を受けるためこの経路は無いが、C#移植は文字列を受けるため必要な保険）。
        if (bg is null) return preferredHex;
        double bgLum = RelLum(bg.Value.r, bg.Value.g, bg.Value.b);

        var preferred = ParseHex(preferredHex);
        if (preferred is not null)
        {
            double prefLum = RelLum(preferred.Value.r, preferred.Value.g, preferred.Value.b);
            if (Contrast(bgLum, prefLum) >= minRatio) return preferredHex;
        }

        double whiteLum = RelLum(0xFF, 0xFF, 0xFF);
        double blackLum = RelLum(0x00, 0x00, 0x00);
        return Contrast(bgLum, whiteLum) >= Contrast(bgLum, blackLum) ? "#FFFFFF" : "#000000";
    }

    /// <summary>背景色 <paramref name="bgHex"/> の上に載せる文字色を、コントラストが大きい方（黒 or 生成り）で選ぶ。</summary>
    public static string PickTextColor(string bgHex)
    {
        var rgb = ParseHex(bgHex);
        if (rgb is null) return "#14110d";
        double lum = RelLum(rgb.Value.r, rgb.Value.g, rgb.Value.b);
        double dark = RelLum(0x14, 0x11, 0x0d);
        double light = RelLum(0xfb, 0xf4, 0xe8);
        return Contrast(lum, dark) >= Contrast(lum, light) ? "#14110d" : "#fbf4e8";
    }

    // [判別性パレット] シフトが同色に潰れないよう、並び順(index)で色相を十分に離した既定色を
    //   割り当てる。隣接シフトのコントラストを保つため暖色/寒色を交互配置。
    private static readonly string[] ShiftWorkPalette =
    {
        "#E59B96", // coral
        "#74BEB0", // teal
        "#E0B968", // amber
        "#93A9E0", // periwinkle
        "#A6C77E", // lime
        "#D7A0D0", // orchid
        "#84C4DC", // sky
        "#E0A0B4", // rose
        "#7FC59B", // mint
        "#B79CE0", // lavender
        "#CFC56A", // gold
        "#C2A98A", // taupe
        "#BBC58A", // olive
        "#E0B0A0", // peach
        "#9AC0C8", // dusty cyan
        "#C8A0C0", // mauve
    };

    /// <summary>
    /// 表示色は「利用者が設定した色」→「一覧上の位置」の順で決める。
    ///
    /// [3.417.0の由来をそのまま記録] 旧実装は記号・名称に「休/off/明」「夜/night/深」「早」「遅」
    /// 「日/勤」が含まれるかでカテゴリを推測し、rest だけは index パレットより優先してスレート固定に
    /// していた。これは外部データに無い意味を記号の字面から作り出す推測で、当てにならない規則だった。
    /// 利用者が色を決めたいシフトは <c>shiftColors</c> の明示色（第1優先）で指定できる。
    /// </summary>
    public static string ResolveShiftColor(string? explicitHex = null, int index = -1)
    {
        if (!string.IsNullOrWhiteSpace(explicitHex)) return explicitHex!;
        if (index >= 0) return ShiftWorkPalette[index % ShiftWorkPalette.Length];
        return NeutralShiftColor;
    }

    /// <summary>位置が不明なときの色。どのシフトでも同じ＝記号による優劣を持たない。</summary>
    public const string NeutralShiftColor = "#84C4DC";

    public static string SeverityFromVioKey(string key)
    {
        var k = key.StartsWith("vio-", StringComparison.Ordinal) ? key.Substring(4) : key;
        return k switch
        {
            "groupViol" or "covU" or "pref" or "c3n" => "CRITICAL", // HARD
            "low" or "high" or "c3mn" => "HIGH",                    // 重い soft(90/45/30)
            "c1" or "c3" or "c3m" or "c2" or "c41" or "c42" or "c41s" or "c42s" or "apt" or "covO" => "WARN",
            // c1=30 は最多件数で飽和回避(3.367.0)・他は1〜3/過剰配置。下流(V6RemainingScreens)はHIGH/WARNを同一表示に畳む
            "fair" or "weekly" => "INFO", // 整え(常時非ゼロ)
            _ => "INFO",
        };
    }

    private static (int r, int g, int b)? ParseHex(string hex)
    {
        // Kotlin's removePrefix("#") strips only ONE leading '#' (not TrimStart's "all of them").
        var s = hex.Trim();
        if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
        string full = s.Length switch
        {
            3 => $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}",
            6 => s,
            _ => "",
        };
        if (full.Length != 6) return null;
        try
        {
            int r = Convert.ToInt32(full.Substring(0, 2), 16);
            int g = Convert.ToInt32(full.Substring(2, 2), 16);
            int b = Convert.ToInt32(full.Substring(4, 2), 16);
            return (r, g, b);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double RelLum(int r, int g, int b)
    {
        static double Ch(int v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(b);
    }

    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
}
