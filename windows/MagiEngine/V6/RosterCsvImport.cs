using System.Globalization;
using System.Text.RegularExpressions;
using MagiEngine.Model;
// System.Text.RegularExpressions.Group (capture-group result) and System.Range (built-in C# 8+
// slice type) both collide by simple name with MagiEngine.Model types once RegularExpressions is
// in scope alongside Model.
using Group = MagiEngine.Model.Group;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース10] Kotlin原本 <c>RosterCsvImport</c>（<c>ScheduleCsvBridge.kt</c> 15〜182行）の移植。
///
/// 病院などで広く使われる「勤務表テンプレCSV」(CP932/Excel由来) を、完全な <see cref="MagiState"/> として
/// 取り込む。
///
/// 添付サンプル (令和8年7月) の構成:
///  - 先頭: 年月タイトル（例「令和8年 7月」）
///  - ユニット(=グループ)ごとのブロック:
///      行: 「ユニット名：,,&lt;ユニット名&gt;,,1,2,…,31,…」（日番号）
///      行: 「№,,氏 名,,水,木,金,…」（曜日）
///      行: 「&lt;№&gt;,&lt;役割&gt;,&lt;氏名&gt;,予定,&lt;31日分のシフト記号&gt;,…,&lt;シフト別集計&gt;」
///  - 凡例ブロック:
///      行: 「,記号,時刻/時間,休憩時間,&lt;曜日…&gt;」
///      行: 「,&lt;記号&gt;,&lt;時刻範囲 or 説明&gt;,&lt;休憩&gt;,&lt;日別の必要人数 31列&gt;」
///
/// 列位置: 氏名=idx2 / シフト記号は idx4 から T 列 / 凡例は 記号=idx1, 時刻=idx2, 必要数=idx4 から。
/// 空セルは「休」に割り当てる（＝勤務指定の無い日＝公休扱い）。担当可否情報は無いため groupShift は
/// 全シフト可(permissive)で取り込み、利用者が後から調整できるようにする。
/// </summary>
public static class RosterCsvImport
{
    private const string Rest = "休";

    private static readonly Regex LegendTimeRangePattern = new(@"\d{1,2}[:：]\d{2}\s*[~～]");
    private static readonly Regex ReiwaYearPattern = new(@"令和(\d+)");
    private static readonly Regex MonthPattern = new(@"(\d{1,2})\s*月");

    /// <summary>このテキストが勤務表テンプレ形式かを軽量判定。</summary>
    public static bool Detect(string text)
    {
        if (text.Contains("ユニット名")) return true;
        // 「氏 名」見出し＋時刻範囲(例 8:30～17:30 / 8：30～17：30)の両方があればテンプレとみなす。
        return text.Contains("氏 名") && LegendTimeRangePattern.IsMatch(text);
    }

    /// <summary>
    /// <paramref name="asWishes"/>: false=本表セルを「勤務表(初期割り当て)」として取り込む（既定）。
    /// true=本表セルを「希望シフト」として取り込む：埋まっているセルは wishes["i,j"]=記号 に、勤務表は
    /// 全て公休で開始する（最適化で希望を尊重しつつ必要数を満たす）。空セルは希望なし（自由）。
    /// ※元表の明示「休」セルは希望休として wishes に入り、空セル（通常の休み）と区別される。
    /// </summary>
    public static MagiState? Parse(string text, bool asWishes = false)
    {
        var parsed = CsvUtil.ParseCsvFull(text);
        // [3.413.0/I-08] 引用符が閉じていないCSVは、開いた引用符以降が1セルへ吸い込まれ**残りの行が
        //   丸ごと消える**。この経路は勤務表そのものを丸ごと差し替えるので、黙って一部だけ取り込むと
        //   「なぜこの人の勤務が消えたのか」が説明できない。書式の誤りとして取込を断る。
        if (parsed.UnclosedQuote) return null;
        var rows = parsed.Rows;
        if (rows.Count == 0) return null;

        static string Cell(IReadOnlyList<string> r, int idx) => (idx >= 0 && idx < r.Count ? r[idx] : "").Trim();
        static string NormName(string s) =>
            Regex.Replace(s.Replace('　', ' ').Trim(), @"\s+", " ");

        // --- 列レイアウト（テンプレ固定。Excel列名→0始まり列番号）---
        //   グループ名 = C列(=2)の各ユニット見出し（例 C2=柳・C13=桐）。氏名 = C列(=2)。
        //   勤務記号 = E列(=4)から右へ T 日分（最大 AI列=34、31日）。シフト記号 = 凡例の B列(=1, 行25〜40)。
        //   スタッフ行は各ユニット見出しの2行下から、空行/凡例/次ユニットの手前まで
        //   （添付サンプルでは 4〜11 行目＝柳・15〜22 行目＝桐。空欄№は自動スキップ）。
        //   ※必要人数(need1/need2)はこのCSVに存在しない。凡例の日別数値は現在表の人数集計(タリー)であり
        //     必要数ではない（休/有の人数も含む）ため、需要としては取り込まない。
        const int nameCol = 2;   // C
        const int dayCol0 = 4;   // E
        const int maxDayCol = 34; // AI（E..AI = 31日）

        // --- 日数 T: 最初のユニット見出しの日番号(1,2,3,…)の連続から求める（E列〜AI列で頭打ち） ---
        var unitHeaders = new List<int>();
        for (var r = 0; r < rows.Count; r++)
            if (Cell(rows[r], 0).StartsWith("ユニット名", StringComparison.Ordinal))
                unitHeaders.Add(r);
        if (unitHeaders.Count == 0) return null;
        var uh0 = rows[unitHeaders[0]];
        var t = 0;
        while (dayCol0 + t <= maxDayCol && KotlinInterop.ToIntOrNull(Cell(uh0, dayCol0 + t)) == t + 1) t++;
        if (t < 1) return null;

        // --- 凡例(B列25〜40): シフト記号＋時刻表記。必要人数は無い（need1/need2は空）。 ---
        var legendHeader = -1;
        for (var r = 0; r < rows.Count; r++)
        {
            if (Cell(rows[r], 1) == "記号" && (Cell(rows[r], 2) == "時刻" || Cell(rows[r], 2) == "時間"))
            {
                legendHeader = r;
                break;
            }
        }
        var shiftsOut = new List<Shift>();
        var symToK = new Dictionary<string, int>();
        if (legendHeader >= 0)
        {
            var r = legendHeader + 1;
            while (r < rows.Count)
            {
                var row = rows[r];
                var sym = Cell(row, 1);        // B列＝シフト記号
                if (sym.Length == 0) break;     // 凡例の終端（合計行「Ａ～Ｃ」等）
                if (!symToK.ContainsKey(sym))
                {
                    symToK[sym] = shiftsOut.Count;
                    var desc = Cell(row, 2);   // C列＝時刻/説明（表示名に使用）
                    shiftsOut.Add(new Shift(desc.Length == 0 ? sym : desc, sym, "", ""));
                }
                r++;
            }
        }
        // 休シフトは必須（解析・整列の基準）。凡例に無ければ補う。
        if (!symToK.ContainsKey(Rest))
        {
            symToK[Rest] = shiftsOut.Count;
            shiftsOut.Add(new Shift("公休", Rest, "", ""));
        }
        if (shiftsOut.Count == 0) return null;
        var restK = symToK[Rest];

        // --- ユニット(グループ)・スタッフ・勤務表グリッド ---
        var groupsOut = new List<Group>();
        var staffOut = new List<Staff>();
        var grid = new List<int[]>();
        var wishes = new Dictionary<string, int>();
        foreach (var uhIdx in unitHeaders)
        {
            var rawUnitName = NormName(Cell(rows[uhIdx], nameCol));
            var unitName = rawUnitName.Length == 0 ? $"G{groupsOut.Count + 1}" : rawUnitName;
            var g = groupsOut.Count;
            groupsOut.Add(new Group(unitName, unitName));
            var rr = uhIdx + 2; // ユニット見出し＋曜日見出しを飛ばす
            while (rr < rows.Count)
            {
                var row = rows[rr];
                if (Cell(row, 0).StartsWith("ユニット名", StringComparison.Ordinal)) break;
                if (Cell(row, 1) == "記号") break; // 凡例に到達
                var isStaffRow = Cell(row, 3) == "予定";
                if (isStaffRow)
                {
                    var name = NormName(Cell(row, nameCol));
                    if (name.Length != 0)
                    {
                        var i = staffOut.Count;
                        staffOut.Add(new Staff(name, g));
                        var days = new int[t];
                        for (var j = 0; j < t; j++) days[j] = restK;
                        for (var j = 0; j < t; j++)
                        {
                            var sym = Cell(row, dayCol0 + j);
                            if (sym.Length != 0 && symToK.TryGetValue(sym, out var k))
                            {
                                days[j] = k;
                                if (asWishes) wishes[$"{i},{j}"] = k;
                            }
                        }
                        // 希望取込時は勤務表を全公休で開始（最適化が希望を尊重して埋める）。
                        if (asWishes)
                        {
                            var allRest = new int[t];
                            for (var j = 0; j < t; j++) allRest[j] = restK;
                            grid.Add(allRest);
                        }
                        else
                        {
                            grid.Add(days);
                        }
                    }
                    rr++;
                    continue;
                }
                if (row.All(c => c.Trim().Length == 0)) break; // 空行＝ブロック終端
                rr++;
            }
        }
        if (staffOut.Count == 0 || groupsOut.Count == 0) return null;

        // --- 期間: タイトル「令和N年 M月」から ---
        var title = Cell(rows[0], 0);
        var reiwaMatch = ReiwaYearPattern.Match(title);
        int? reiwa = reiwaMatch.Success ? KotlinInterop.ToIntOrNull(reiwaMatch.Groups[1].Value) : null;
        var yr = reiwa.HasValue ? 2018 + reiwa.Value : DateTime.Now.Year;
        // [3.329.0/外部レビュー M-03] 月はまずタイトル文字列から読む。旧は `rows[0].drop(1)` の
        //   セルだけを見ており、「令和8年 7月」が1セルに入った形式では**必ず1月**になっていた。
        var monthMatch = MonthPattern.Match(title);
        int? moFromTitle = monthMatch.Success ? KotlinInterop.ToIntOrNull(monthMatch.Groups[1].Value) : null;
        if (moFromTitle is < 1 or > 12) moFromTitle = null;
        int? moFromCells = null;
        foreach (var c in rows[0].Skip(1))
        {
            var v = KotlinInterop.ToIntOrNull(c.Trim());
            if (v is >= 1 and <= 12) { moFromCells = v; break; }
        }
        var mo = moFromTitle ?? moFromCells ?? 1;
        var start = $"{yr:D4}-{mo:D2}-01";
        string end;
        try
        {
            end = DateOnly.ParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .AddDays(t - 1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            end = start;
        }

        var k2 = shiftsOut.Count;
        return new MagiState(
            StartDate: start,
            EndDate: end,
            Shifts: shiftsOut,
            Groups: groupsOut,
            StaffList: staffOut,
            Use2Patterns: false,
            GroupShift: groupsOut.Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(1, k2).ToList()).ToList(), // 担当可否不明→全可(後から調整)
            GroupShiftApt: groupsOut.Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("", k2).ToList()).ToList(),
            Schedule: grid.Select(row => (IReadOnlyList<int>)row.ToList()).ToList(),
            Wishes: wishes,
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), // 必要人数はCSVに無い（凡例の日別数値は集計＝需要ではない）
            NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(),
            Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(),
            Cons3m: new List<C3Row>(),
            Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(),
            Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(),
            Cons41s: new List<C41Row>(),
            Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }
}
