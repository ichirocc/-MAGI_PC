using System.Linq;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース11] Kotlin原本 <c>WishesCsvIO</c>（<c>ScheduleCsvBridge.kt</c> 676〜729行）の移植。
///
/// 希望シフトCSV「氏名,日,希望シフト」（1希望=1行）の往復。氏名一致で希望を全置換。
/// </summary>
public static class WishesCsvIO
{
    private static string Cell(IReadOnlyList<string> r, int idx) => (idx >= 0 && idx < r.Count ? r[idx] : "").Trim();

    public static string Build(MagiState state)
    {
        var sb = new System.Text.StringBuilder();
        CsvUtil.AppendCsvRow(sb, new List<string> { "氏名", "日", "希望シフト" });
        var entries = new List<(int I, int J, int K)>();
        foreach (var kv in state.Wishes)
        {
            var p = kv.Key.Split(',');
            var i = p.Length > 0 ? KotlinInterop.ToIntOrNull(p[0]) : null;
            var j = p.Length > 1 ? KotlinInterop.ToIntOrNull(p[1]) : null;
            if (i is null || j is null) continue;
            entries.Add((i.Value, j.Value, kv.Value));
        }
        foreach (var (i, j, k) in entries.OrderBy(e => e.I).ThenBy(e => e.J))
        {
            if (i < 0 || i >= state.StaffList.Count) continue;
            var name = state.StaffList[i].Name;
            if (k < 0 || k >= state.Shifts.Count) continue;
            var sym = state.Shifts[k].Kigou;
            CsvUtil.AppendCsvRow(sb, new List<string> { name, (j + 1).ToString(), sym });
        }
        return sb.ToString();
    }

    /// <returns>更新後stateと取込件数を持つ <see cref="ComponentImport"/>、または null（解析不能/0件）。</returns>
    public static ComponentImport? Parse(string text, MagiState state)
    {
        // [3.413.0/I-08] 引用符が閉じないCSVは残りの行が丸ごと消える＝**全置換の取込では
        //   「消えた」ことが取込結果からは分からない**。書式の誤りとして断る。
        var parsed0 = CsvUtil.ParseCsvFull(text);
        if (parsed0.UnclosedQuote) return null;
        var rows = parsed0.Rows;
        if (rows.Count == 0) return null;
        var nameToI = CsvUtil.FirstWinsMap(state.StaffList.Count, i => CsvUtil.NameMatchKey(state.StaffList[i].Name));
        var symToK = CsvUtil.FirstWinsMap(state.Shifts.Count, i => state.Shifts[i].Kigou.Trim());
        var m = new Dictionary<string, int>();
        var n = 0;
        // [3.314.0] ヘッダ判定を Build() が出す実ヘッダ「氏名」の一致へ。旧:「先頭が既知の職員名か」
        //   という間接的な推測で、**未知の職員名で始まるヘッダ無CSVの先頭行を黙って捨てて**いた。
        var body = CsvUtil.CsvBody(rows, "氏名");
        var bad = 0;
        var sample = "";
        foreach (var r in body)
        {
            var name = Cell(r, 0);
            var dayCell = Cell(r, 1);
            var day = KotlinInterop.ToIntOrNull(dayCell);
            var sym = Cell(r, 2);
            // 完全な空行は書式上のもの＝無視してよい。中身があるのに解釈できない行だけを数える。
            if (name.Length == 0 && sym.Length == 0 && dayCell.Length == 0) continue;
            var hasI = nameToI.TryGetValue(CsvUtil.NameMatchKey(name), out var i);
            var hasK = symToK.TryGetValue(sym, out var k);
            if (!hasI || !hasK || day is null || day < 1 || day > state.DayCount)
            {
                bad++;
                if (sample.Length == 0) sample = string.Join(",", r).Take(60);
                continue;
            }
            m[$"{i},{day - 1}"] = k;
            n++;
        }
        if (n == 0 && bad == 0) return null;
        return new ComponentImport(state with { Wishes = m }, n, bad, sample);
    }
}
