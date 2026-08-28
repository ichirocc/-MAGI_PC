using System.Text;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース10] Kotlin原本 <c>ScheduleCsvBridge</c>（<c>ScheduleCsvBridge.kt</c> 326〜415行）の移植。
///
/// 勤務表グリッド全体の CSV 往復（出力＝<see cref="Build"/>、取込＝<see cref="Parse"/>）。
/// </summary>
public static class ScheduleCsvBridge
{
    public static string Build(MagiState state, int[][] schedule)
    {
        var p = new Problem(state);
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        var outSb = new StringBuilder();
        var header = new List<string> { "スタッフ \\ 日付" };
        // [移植メモ] formatDay(MirrorCore.kt) と dayLabel(V6PortAnalyzer.kt) は Kotlin原本でも別名の
        //   同一計算（"M/D(曜)"、曜日文字の実装経路が違うだけで出力は常に一致）。C#側は
        //   V6PortAnalyzer.DayLabel(internal・アセンブリ内可視)を再利用し、重複実装を持たない。
        for (var j = 0; j < p.T; j++) header.Add(V6PortAnalyzer.DayLabel(state.StartDate, j));
        CsvUtil.AppendCsvRow(outSb, header);

        for (var i = 0; i < p.S; i++)
        {
            var line = new List<string> { state.StaffList[i].Name };
            for (var j = 0; j < p.T; j++)
            {
                var k = s[i][j];
                var symbol = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : "";
                line.Add(symbol);
            }
            CsvUtil.AppendCsvRow(outSb, line);
        }

        CsvUtil.AppendCsvRow(outSb, new List<string>());
        var sumHeader = new List<string> { "集計" };
        foreach (var shift in state.Shifts) sumHeader.Add(shift.Kigou);
        CsvUtil.AppendCsvRow(outSb, sumHeader);

        var counts = ScheduleUtil.CountMatrix(p, s);
        for (var i = 0; i < p.S; i++)
        {
            var row = new List<string> { state.StaffList[i].Name };
            for (var k = 0; k < p.K; k++) row.Add(counts[i][k].ToString());
            CsvUtil.AppendCsvRow(outSb, row);
        }
        return outSb.ToString();
    }

    public static ScheduleRunResult Parse(string text, MagiState state, int[][] baseSchedule)
    {
        // [3.413.0/I-08] 引用符が閉じないCSVは残りの行が丸ごと消える。ここは非nullを返す経路なので
        //   断れない代わりに旗を立て、呼出側が「一致が少ない」と「消えた」を区別できるようにする。
        var parsedAll = CsvUtil.ParseCsvFull(text);
        var rows = parsedAll.Rows;
        var p = new Problem(state);
        var schedule = ScheduleUtil.NormalizeSchedule(baseSchedule, p);
        // [P1修正/レビュー指摘] 重複した氏名/記号は「最初の1件」に解決する（Problem.ShiftIdxOf=IndexOfFirst と同じ）。
        //   旧: 後勝ちで、制約評価(最初)とCSV取込(最後)が同じ記号を別シフトとして扱っていた。
        var nameToI = CsvUtil.FirstWinsMap(state.StaffList.Count, i => CsvUtil.NameMatchKey(state.StaffList[i].Name));
        var kigouToK = CsvUtil.FirstWinsMap(state.Shifts.Count, i => state.Shifts[i].Kigou.Trim());
        var matched = 0;
        // [3.410.0/I-01] 未知記号を数える（旧: 黙って読み飛ばしていた）。
        var unknown = new Dictionary<string, int>();
        var rr = 1;
        while (rr < rows.Count)
        {
            var r = rows[rr];
            // Build() は勤務表の後に「空行＋『集計』ヘッダ＋職員名で始まる回数行」を出力する。ここで終端しないと
            // 回数行が名前一致で再取込され matched が二重化し、シフト記号が数値の場合は回数値が記号解決して勤務表を破壊する。
            if (r.Count == 0 || r.All(c => string.IsNullOrWhiteSpace(c))) break;
            if (r[0].Trim() == "集計") break;
            if (r[0].Trim().Length != 0)
            {
                if (nameToI.TryGetValue(CsvUtil.NameMatchKey(r[0]), out var staffIndex))
                {
                    matched++;
                    var last = Math.Min(p.T, r.Count - 1);
                    var j = 0;
                    while (j < last)
                    {
                        var sym = r[j + 1].Trim();
                        if (kigouToK.TryGetValue(sym, out var k)) schedule[staffIndex][j] = k;
                        else if (sym.Length != 0) unknown[sym] = (unknown.TryGetValue(sym, out var cur) ? cur : 0) + 1;
                        j++;
                    }
                }
            }
            rr++;
        }
        var report = UnifiedViolationChecker.Check(state, schedule);
        var unknownTotal = unknown.Values.Sum();
        var unknownTop = unknown.OrderByDescending(kv => kv.Value).Take(5)
            .Select(kv => $"{kv.Key}({kv.Value})").ToList();
        var message = $"CSV取込: staff一致 {matched}行" +
            (unknownTotal > 0 ? $" / 読めない記号 {unknownTotal}セル: {string.Join("・", unknownTop)}" : "");
        var log = new MirrorLog(tag: "CSVImport", message: message);
        var logs = new List<MirrorLog> { log };
        logs.AddRange(report.Logs);
        return new ScheduleRunResult(
            schedule, report with { Logs = logs }, Matched: matched,
            UnknownCells: unknownTotal, UnknownSymbols: unknownTop,
            UnclosedQuote: parsedAll.UnclosedQuote);
    }
}
