using System.Text.RegularExpressions;

namespace MagiApp.ViewModels;

/// <summary>
/// 代行役（AX）の文言（Kotlin <c>AgentText.kt</c>）。この枝では 3.727.1 で足された純関数だけを持つ（代行役の本体・<c>AgentRecord</c>・
/// 見出し・通知は 3.724.0 の移植側にある。合流するときはこのファイルの関数をそちらの <c>AgentText</c> へ足す）。
/// </summary>
public static partial class AgentText
{
    private static readonly Regex SoftToken = new(@"^(\d+)(\D+)$");

    /// <summary>
    /// [Android 3.727.1] 「147件・3人・80回」と「140件・4人・70回」から、値が動いた最初の単位を「147→140件」で（消えた単位は 0）。
    /// どれも動かなければ先頭の単位（Kotlin <c>softHeadDelta</c>。代行の見出しの要調整）。
    /// </summary>
    public static string SoftHeadDelta(string before, string after)
    {
        static List<(string Unit, string N)> Parse(string s) => s.Split('・')
            .Select(t => SoftToken.Match(t.Trim())).Where(m => m.Success).Select(m => (m.Groups[2].Value, m.Groups[1].Value)).ToList();
        var b = Parse(before);
        var a = new Dictionary<string, string>();
        foreach (var (u, n) in Parse(after)) a[u] = n;
        if (b.Count == 0) return $"{before}→{after}";
        var pick = b.FirstOrDefault(x => !a.TryGetValue(x.Unit, out var v) || v != x.N);
        if (pick == default) pick = b[0];
        return $"{pick.N}→{(a.TryGetValue(pick.Unit, out var now) ? now : "0")}{pick.Unit}";
    }
}
