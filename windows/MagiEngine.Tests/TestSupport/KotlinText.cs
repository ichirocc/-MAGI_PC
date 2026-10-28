using System.Globalization;
using System.Reflection;
using System.Text;

namespace MagiEngine.Tests.TestSupport;

/// <summary>
/// Kotlin 側で作ったスナップショット（<c>app/src/test/resources/snapshots/</c>）と同じ文字列を C# の値から起こす。
/// data class の <c>toString()</c>（<c>Name(a=1, b=null)</c>）・List の <c>[x, y]</c>・Double の <c>13159.0</c>・
/// enum の大文字の名前（<c>RemoveWish</c> → <c>REMOVE_WISH</c>）に揃える。
/// </summary>
internal static class KotlinText
{
    public static string Of(object? v) => v switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        double d => Double(d),
        Enum e => EnumName(e),
        System.Collections.IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(Of)) + "]",
        _ when v.GetType().IsPrimitive => Convert.ToString(v, CultureInfo.InvariantCulture)!,
        _ => Record(v),
    };

    /// <summary>Kotlin の <c>Double.toString()</c>（10^-3 以上 10^7 未満は小数点つきの十進、整数値は <c>.0</c>）。</summary>
    public static string Double(double d)
    {
        var a = Math.Abs(d);
        if (d == Math.Floor(d) && a < 1e7) return d.ToString("0", CultureInfo.InvariantCulture) + ".0";
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string EnumName(Enum e)
    {
        var sb = new StringBuilder();
        foreach (var ch in e.ToString())
        {
            if (char.IsUpper(ch) && sb.Length > 0) sb.Append('_');
            sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }

    private static string Record(object v)
    {
        var t = v.GetType();
        // 主コンストラクタの引数順＝Kotlin の data class のプロパティ順。
        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var parts = ctor.GetParameters().Select(prm =>
        {
            var prop = t.GetProperty(prm.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            return $"{char.ToLowerInvariant(prm.Name![0])}{prm.Name[1..]}={Of(prop.GetValue(v))}";
        });
        return $"{t.Name}({string.Join(", ", parts)})";
    }
}
