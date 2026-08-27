using System.Globalization;
using System.Text.Json;

namespace MagiEngine.Model;

/// <summary>
/// org.json-style lenient JSON access helpers, used by <see cref="StateJsonSerializer"/> to
/// mirror the Kotlin/Android app's <c>StateParser.kt</c> (which uses <c>org.json</c> because
/// the schema mixes types freely — e.g. need fields are sometimes an integer and sometimes
/// the empty string ""). <see cref="System.Text.Json"/>'s <c>JsonElement</c> is strict by
/// default (reading a JSON number into a string-typed getter throws), so these helpers
/// replicate org.json's lenient <c>opt*</c> conversion semantics by hand.
/// </summary>
internal static class JsonHelpers
{
    /// <summary>Property lookup that treats "missing" and "present but JSON null" the same
    /// as org.json's <c>JSONObject.opt(name)</c> returning <c>null</c>.</summary>
    public static JsonElement? Opt(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return v;
        }
        return null;
    }

    public static string OptString(JsonElement obj, string name, string @default = "")
    {
        var v = Opt(obj, name);
        if (v is null) return @default;
        return v.Value.ValueKind switch
        {
            JsonValueKind.String => v.Value.GetString() ?? @default,
            JsonValueKind.Number => v.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => @default,
        };
    }

    public static int OptInt(JsonElement obj, string name, int @default = 0)
    {
        var v = Opt(obj, name);
        return v is null ? @default : OptIntAt(v.Value, @default);
    }

    public static bool OptBoolean(JsonElement obj, string name, bool @default = false)
    {
        var v = Opt(obj, name);
        if (v is null) return @default;
        return v.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.Value.GetString(), out var b) ? b : @default,
            _ => @default,
        };
    }

    /// <summary>Returns the property only if it is a JSON array; null otherwise (mirrors
    /// <c>JSONObject.optJSONArray</c>).</summary>
    public static JsonElement? OptArray(JsonElement obj, string name)
    {
        var v = Opt(obj, name);
        return v is not null && v.Value.ValueKind == JsonValueKind.Array ? v : null;
    }

    /// <summary>Returns the property only if it is a JSON object; null otherwise (mirrors
    /// <c>JSONObject.optJSONObject</c>).</summary>
    public static JsonElement? OptObject(JsonElement obj, string name)
    {
        var v = Opt(obj, name);
        return v is not null && v.Value.ValueKind == JsonValueKind.Object ? v : null;
    }

    /// <summary>Array-element variant of <see cref="OptInt"/> (mirrors
    /// <c>JSONArray.optInt(index, default)</c>): the element itself, not a property lookup.</summary>
    public static int OptIntAt(JsonElement item, int @default)
    {
        return item.ValueKind switch
        {
            JsonValueKind.Number => item.TryGetInt32(out var i) ? i : (int)item.GetDouble(),
            JsonValueKind.String => int.TryParse(item.GetString(), out var i) ? i : @default,
            _ => @default,
        };
    }

    /// <summary>
    /// Mirrors Kotlin's <c>StateParser.asStr</c>: converts any JSON value (or its absence) to
    /// a display string. Whole-valued numbers print without a decimal point (matching how
    /// org.json's <c>Double</c> values get stringified back to integer form in the Kotlin code),
    /// null/missing becomes "".
    /// </summary>
    public static string AsStr(JsonElement? v)
    {
        if (v is null) return "";
        var e = v.Value;
        return e.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            JsonValueKind.String => e.GetString() ?? "",
            JsonValueKind.Number => NumberToStr(e),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => e.GetRawText(),
        };
    }

    private static string NumberToStr(JsonElement e)
    {
        var raw = e.GetRawText();
        // Already an integer literal (int/long in org.json terms) — return verbatim,
        // digit-for-digit, same as Kotlin's Int.toString()/Long.toString().
        if (raw.IndexOfAny(NumFormatChars) < 0) return raw;

        // Had a '.'/'e'/'E' — org.json would have parsed this as a Double. Kotlin's asStr
        // collapses whole-valued doubles ("1.0") to integer string form ("1"), matching
        // `if (v == v.toLong().toDouble()) v.toLong().toString() else v.toString()`.
        var d = e.GetDouble();
        var truncated = Math.Truncate(d);
        if (d == truncated && Math.Abs(d) < 9.2233720368547758E18)
        {
            return ((long)truncated).ToString(CultureInfo.InvariantCulture);
        }
        return d.ToString(CultureInfo.InvariantCulture);
    }

    private static readonly char[] NumFormatChars = { '.', 'e', 'E' };

    public static List<string> StrList(JsonElement? array)
    {
        if (array is null) return new List<string>();
        var result = new List<string>();
        foreach (var item in array.Value.EnumerateArray()) result.Add(AsStr(item));
        return result;
    }

    public static Dictionary<string, string> StrMap(JsonElement? obj)
    {
        var result = new Dictionary<string, string>();
        if (obj is null) return result;
        foreach (var prop in obj.Value.EnumerateObject()) result[prop.Name] = AsStr(prop.Value);
        return result;
    }

    // [外部レビュー P2-02, 移植元 StateParser.kt] 要素がオブジェクト/配列でなければ黙って読み飛ばさない
    //   （null・数値・文字列の混入で staff/schedule 等が本来より短いリストへ静かに変わることを防ぐ）。
    //   インポート・復元データは職員名や勤務割当を含むため、読み飛ばしより明示的な失敗のほうが安全。
    public static List<T> MapObjects<T>(JsonElement? array, string name, Func<JsonElement, T> f)
    {
        var result = new List<T>();
        if (array is null) return result;
        int i = 0;
        foreach (var item in array.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"{name}[{i}] がオブジェクトではありません（データが壊れています）");
            result.Add(f(item));
            i++;
        }
        return result;
    }

    public static List<T> MapArrays<T>(JsonElement? array, string name, Func<JsonElement, T> f)
    {
        var result = new List<T>();
        if (array is null) return result;
        int i = 0;
        foreach (var item in array.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array)
                throw new ArgumentException($"{name}[{i}] が配列ではありません（データが壊れています）");
            result.Add(f(item));
            i++;
        }
        return result;
    }
}
