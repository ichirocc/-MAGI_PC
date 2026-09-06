using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [phase9 #9] 日本の祝日の判定（Kotlin原本 <c>ui/JapanHolidays.kt</c>）。データは Android と同じ外部ファイル
/// <c>Assets/japan_holidays.json</c>（"YYYY-MM-DD" → 祝日名）。特定の日付をコードへ書かない＝再生成で期間を延ばせる。
/// 一度読めば以降は辞書引きのみ。読込に失敗しても空扱い（安全側）。
/// </summary>
internal static class JapanHolidays
{
    private static volatile Dictionary<string, string>? _cache;

    private static Dictionary<string, string> Load()
    {
        var c = _cache;
        if (c is not null) return c;
        Dictionary<string, string> loaded;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "japan_holidays.json");
            loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>();
        }
        catch (Exception)
        {
            loaded = new Dictionary<string, string>();
        }
        _cache = loaded;
        return loaded;
    }

    /// <summary>指定日が祝日なら祝日名（例「敬老の日」）、祝日でなければ null。</summary>
    public static string? NameOf(DateOnly date) => Load().TryGetValue(date.ToString("yyyy-MM-dd"), out var name) ? name : null;
}
