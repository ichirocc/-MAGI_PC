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
/// [フェーズ7ピース10] Kotlin原本 <c>FlatRosterCsvImport</c>（<c>ScheduleCsvBridge.kt</c> 184〜324行）の移植。
///
/// 「ユニット列形式」の勤務表CSVを <see cref="MagiState"/> として取り込む（凡例ブロックなし版）。
///
/// 構成（添付サンプル）:
///  - ヘッダ行: 「ユニット,No,役職,氏名,1,2,…,31」（日番号は氏名列の右隣から）
///  - 曜日行(任意): 「,,,曜日,水,木,金,…」
///  - スタッフ行: 「&lt;ユニット&gt;,&lt;No&gt;,&lt;役職&gt;,&lt;氏名&gt;,&lt;31日分のシフト記号&gt;」
///
/// <see cref="RosterCsvImport"/> との違い: ユニットが「列(idx0)」/ 氏名は見出し「氏名」の列 / 凡例ブロックが無い。
/// シフト記号は本表セルから収集する。担当可否・apt・制約・需要は無し（全可・空）で取り込み、
/// 期間は曜日行から推定（不可なら当年1月）。空セルは「休」。利用者が後から調整できる。
/// </summary>
public static class FlatRosterCsvImport
{
    private const string Rest = "休";

    private static readonly IReadOnlyDictionary<string, int> WeekdayJp = new Dictionary<string, int>
    {
        ["月"] = 1, ["火"] = 2, ["水"] = 3, ["木"] = 4, ["金"] = 5, ["土"] = 6, ["日"] = 7,
    };

    /// <summary>ヘッダ行 idx0=="ユニット" かつ 見出し「氏名」を含むか（軽量判定）。</summary>
    public static bool Detect(string text)
    {
        var rows = CsvUtil.ParseCsvRows(text);
        return rows.Any(r => r.Count > 0 && r[0].Trim() == "ユニット" && r.Any(c => c.Trim() == "氏名"));
    }

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

        // ヘッダ行（idx0="ユニット" かつ 見出し「氏名」を含む）と、氏名列・日付開始列を特定。
        var headerIdx = -1;
        for (var r = 0; r < rows.Count; r++)
        {
            if (Cell(rows[r], 0) == "ユニット" && rows[r].Any(c => c.Trim() == "氏名")) { headerIdx = r; break; }
        }
        if (headerIdx < 0) return null;
        var header = rows[headerIdx];
        var nameCol = -1;
        for (var c = 0; c < header.Count; c++)
        {
            if (header[c].Trim() == "氏名") { nameCol = c; break; }
        }
        if (nameCol < 0) return null;
        var dayCol0 = nameCol + 1;
        // 日数T: ヘッダの dayCol0 以降の連番(1,2,3…)の長さ。
        // [3.329.0/外部レビュー M-03] 連番ヘッダが無いときの「最大列数からの推定」を**やめる**。
        //   合計・注記などの末尾列まで日付として取り込み、期間が伸びて中身が空の日ができていた。
        //   期間はデータの根幹なので、推測せず取込を断る（利用者が日付行を足せば通る）。
        var t = 0;
        while (dayCol0 + t < header.Count && KotlinInterop.ToIntOrNull(Cell(header, dayCol0 + t)) == t + 1) t++;
        if (t < 1) return null;

        // 曜日行（任意）: ヘッダ直後で氏名列が「曜日」。
        IReadOnlyList<string>? youbiRow = null;
        if (headerIdx + 1 < rows.Count)
        {
            var candidate = rows[headerIdx + 1];
            if (Cell(candidate, nameCol) == "曜日") youbiRow = candidate;
        }

        // スタッフ行を収集（ユニット空欄なら直前を継承＝Excel結合セル対策）。
        var staffRows = new List<(string Unit, string Name, IReadOnlyList<string> Shifts)>();
        // [移植メモ] Kotlin の LinkedHashSet と同じ「挿入順を保持する集合」を、List(順序)+HashSet(O(1)判定)の
        //   組で明示的に再現する（.NET の HashSet<T> の列挙順は未規定のため依存しない）。
        var symList = new List<string>();
        var symSeen = new HashSet<string>();
        var lastUnit = "";
        for (var rr = headerIdx + 1; rr < rows.Count; rr++)
        {
            var r = rows[rr];
            var u = Cell(r, 0);
            if (u.Length != 0) lastUnit = u;
            var name = NormName(Cell(r, nameCol));
            if (name.Length == 0 || name == "氏名" || name == "曜日") continue;
            if (lastUnit.Length == 0) continue;
            var shifts = new List<string>();
            for (var d = 0; d < t; d++) shifts.Add(Cell(r, dayCol0 + d));
            staffRows.Add((lastUnit, name, shifts));
            foreach (var s in shifts) if (s.Length != 0 && symSeen.Add(s)) symList.Add(s);
        }
        if (staffRows.Count == 0) return null;

        // シフト一覧（本表セルから収集、休を先頭）。
        var symbols = new List<string> { Rest };
        foreach (var s in symList) if (s != Rest) symbols.Add(s);
        var symToK = new Dictionary<string, int>();
        for (var i = 0; i < symbols.Count; i++) symToK[symbols[i]] = i;
        var shiftsOut = symbols.Select(s => new Shift(s, s, "", "")).ToList();
        var restK = symToK[Rest];

        // ユニット→グループ（出現順）。
        var groupOrderList = new List<string>();
        var groupOrder = new Dictionary<string, int>();
        foreach (var row in staffRows)
        {
            if (!groupOrder.ContainsKey(row.Unit))
            {
                groupOrder[row.Unit] = groupOrder.Count;
                groupOrderList.Add(row.Unit);
            }
        }
        var groupsOut = groupOrderList.Select(name => new Group(name, name)).ToList();

        // スタッフ・勤務表グリッド。
        var staffOut = new List<Staff>();
        var grid = new List<int[]>();
        var wishes = new Dictionary<string, int>();
        for (var i = 0; i < staffRows.Count; i++)
        {
            var row = staffRows[i];
            var g = groupOrder[row.Unit];
            staffOut.Add(new Staff(row.Name, g));
            var days = new int[t];
            for (var j = 0; j < t; j++) days[j] = restK;
            for (var j = 0; j < t; j++)
            {
                var sym = row.Shifts[j];
                if (sym.Length != 0 && symToK.TryGetValue(sym, out var k))
                {
                    days[j] = k;
                    if (asWishes) wishes[$"{i},{j}"] = k;
                }
            }
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

        // 期間: 曜日行の1日目の曜日から、当年で「1日がその曜日かつT日以上ある月」を推定。不可なら当年1月。
        var yr = DateTime.Now.Year;
        int? dow = null;
        if (youbiRow != null)
        {
            var wd = Cell(youbiRow, dayCol0);
            if (WeekdayJp.TryGetValue(wd, out var dv)) dow = dv;
        }
        var mo = 1;
        if (dow != null)
        {
            for (var m = 1; m <= 12; m++)
            {
                // ISO曜日(月=1..日=7)。.NET の DayOfWeek は日=0..土=6 なので変換する
                // （MirrorCore.kt の formatDay が使う java.util.Calendar の日=1..土=7 とは別の対応表＝
                //   ここは V6PortAnalyzer.kt の dayOfWeek.value(ISO) と同じ変換）。
                var d = new DateOnly(yr, m, 1);
                var isoDow = ((int)d.DayOfWeek + 6) % 7 + 1; // .NET 日=0..土=6 → ISO 月=1..日=7
                if (isoDow == dow.Value && DateTime.DaysInMonth(yr, m) >= t) { mo = m; break; }
            }
        }
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
            GroupShift: groupsOut.Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(1, k2).ToList()).ToList(),
            GroupShiftApt: groupsOut.Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("", k2).ToList()).ToList(),
            Schedule: grid.Select(row => (IReadOnlyList<int>)row.ToList()).ToList(),
            Wishes: wishes,
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(),
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
