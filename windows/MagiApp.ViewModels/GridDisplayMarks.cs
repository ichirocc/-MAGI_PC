using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>勤務表の行末の回数の印（under=▼ 不足 / over=▲ 超過）。</summary>
public sealed record CountBadge(bool Under, bool Over)
{
    public string Glyph => (Under ? "▼" : "") + (Over ? "▲" : "");
}

/// <summary>日ヘッダの人員の印 1 つ＝シフトと向き（under=▼ 人員不足 / ▲ 人員過剰）。</summary>
public sealed record CoverageMark(int Shift, bool Under);

/// <summary>
/// 回数の過不足の 1 枚（例「Dﾃ  +2回 (6/4)」）。Ref は目標（apt）か下限・上限、RefLabel は下限・上限のときだけ付ける。
/// Under＝不足側の色（下限割れ・適切回数の不足・個人の合計）。
/// </summary>
public sealed record CountChip(string Shift, int Count, int? Ref, string RefLabel, bool Under)
{
    public int? Delta => Ref is { } r ? Count - r : null;
    public string Text => $"{Shift}  " + (Delta is { } d ? (d > 0 ? "+" : "") + $"{d}回 " : "") +
        $"({Count}/" + (Ref is { } r ? RefLabel + r : "—") + ")";
}

/// <summary>行末の印のシートの中身。Weekly は 1 シフト 1 行、Fair は「差 N回 : シフト, …」を差の大きい順。</summary>
public sealed record StaffCountSheet(IReadOnlyList<CountChip> Chips, IReadOnlyList<string> Weekly, IReadOnlyList<string> Fair, IReadOnlyList<string> C1)
{
    public bool IsEmpty => C1.Count == 0 && Chips.Count == 0 && Weekly.Count == 0 && Fair.Count == 0;
}

/// <summary>
/// 勤務表の表示専用の印。Kotlin <c>MagiViewState.kt</c> の同名関数の移植。チェッカーの場所マップ
/// （Violations/CountViolations/NeedViolations）は探索の手掛かりも読むので変えず、報告から別に組み立てる。
/// 族名の日本語化は View 層（<c>AnalysisView.BreakdownLabels</c>）にあるので labelOf で受ける。
/// </summary>
public static class GridDisplayMarks
{
    private static int? First(string key) => int.TryParse(key.AsSpan(0, Math.Max(0, key.IndexOf(','))), out var v) ? v : null;
    private static int? Second(string key) => int.TryParse(key.AsSpan(key.IndexOf(',') + 1), out var v) ? v : null;

    /// <summary>c1 の表示専用の印（セルキー）。不足窓の中で、いま そのシフトでなく そのシフトに変えられる日だけ。</summary>
    public static IReadOnlySet<string> C1DisplayMarks(UiState ui) =>
        ui.C1Shortages.SelectMany(sh => sh.Marks.Select(d => $"{sh.Staff},{d}")).ToHashSet();

    /// <summary>c1Band[i][j] = 職員 i の行の j 日の下に期間の制約の帯を引くか（「期間の制約」チップが OFF なら引かない）。</summary>
    public static bool[][] C1Band(UiState ui, IReadOnlySet<string> enabled, int staffCount, int dayCount)
    {
        var b = Enumerable.Range(0, staffCount).Select(_ => new bool[dayCount]).ToArray();
        if (!VioBuckets.VioVisible("vio-c1", enabled)) return b;
        foreach (var sh in ui.C1Shortages)
            if (sh.Band && sh.Staff < staffCount)
                for (var d = sh.From; d <= Math.Min(sh.To, dayCount - 1); d++) b[sh.Staff][d] = true;
        return b;
    }

    /// <summary>勤務表だけでは期間の制約を満たせない職員（行末の内訳を開けるようにする）。</summary>
    public static IReadOnlySet<int> C1Stuck(UiState ui) => ui.C1Shortages.Where(x => x.Stuck).Select(x => x.Staff).ToHashSet();

    /// <summary>上限0のセルの表示クラス。族は high（回数チップに従う）、枠は破線（どのセルが超過か一意なので）。</summary>
    public const string ZeroCapClass = "vio-high0";

    /// <summary>個人の上限0（休を除く）のシフトが入っているセル。上限0なら入っている日はどれも超過なのでセルに印を付けられる
    /// （上限1以上の超過はどの日が余分か決まらないので名前の横の ▲ だけ）。</summary>
    public static IReadOnlySet<string> ZeroCapCells(Problem p, int[][] s)
    {
        var o = new HashSet<string>();
        for (var i = 0; i < Math.Min(p.S, s.Length); i++)
            for (var j = 0; j < s[i].Length; j++)
            {
                var k = s[i][j];
                if (k >= 0 && k < p.K && k != p.RestIdx && p.RangeHi[i][k] == 0) o.Add($"{i},{j}");
            }
        return o;
    }

    /// <summary>許容0の超過のセルの表示クラス（族→クラス）。人員の上限0・グループの上限0・適切回数0は、入っているセルが
    /// どれも超過なので一意に印を付けられる（上限1以上の超過はどのセルが余分か決まらないので日付/名前の印だけ）。</summary>
    public static readonly IReadOnlyDictionary<string, string> ZeroAllowClass = new Dictionary<string, string>
    {
        ["covO"] = "vio-covO0", ["c41"] = "vio-c410", ["c41s"] = "vio-c41s0", ["apt"] = "vio-apt0",
    };

    /// <summary>許容0の超過のセル → 表示クラス。covO は人員の上限（CovOCell の基準）が0の日、c41/c41s は上限0の日、
    /// apt は実効目標0（個人設定のある組は -1 で対象外）。</summary>
    public static IReadOnlyDictionary<string, string> ZeroAllowCells(Problem p, int[][] s)
    {
        var o = new Dictionary<string, string>();
        var nS = Math.Min(p.S, s.Length);
        int At(int i, int j) => j < s[i].Length ? s[i][j] : -1;
        for (var j = 0; j < p.T; j++)
            for (var k = 0; k < p.K; k++)
            {
                var on = Enumerable.Range(0, nS).Where(i => At(i, j) == k).ToList();
                if (on.Count > 0 && p.CovOCell(k, j, on.Count) == on.Count) foreach (var i in on) o.TryAdd($"{i},{j}", ZeroAllowClass["covO"]);
            }
        void GroupDay(IReadOnlyList<C41> rows, int[] grp, string fam)
        {
            foreach (var c in rows)
                if (c.U == 0)
                    for (var j = 0; j < p.T; j++)
                        for (var i = 0; i < nS; i++)
                            if (grp[i] == c.GroupIdx && At(i, j) == c.ShiftIdx) o.TryAdd($"{i},{j}", ZeroAllowClass[fam]);
        }
        GroupDay(p.Cons41, p.Sgrp, "c41");
        GroupDay(p.Cons41s, p.Ssk, "c41s");
        for (var i = 0; i < nS; i++)
            for (var j = 0; j < s[i].Length; j++)
            {
                var k = s[i][j];
                if (k >= 0 && k < p.K && p.Apt[i][k] == 0) o.TryAdd($"{i},{j}", ZeroAllowClass["apt"]);
            }
        return o;
    }

    /// <summary>画面に出すセルの違反クラス（重み降順）。チェッカーの c1（ランの先頭）は描かず、表示専用の印に置き換える。
    /// 上限0のセルには表示専用の <see cref="ZeroCapClass"/>、許容0の超過のセルには <see cref="ZeroAllowCells"/> のクラスを足す。</summary>
    public static IReadOnlyList<string> DisplayCellClasses(UiState ui, string key, IReadOnlySet<string> c1Marks)
    {
        var bas = VioBuckets.CellVioClasses(ui, key).Where(c => c != "vio-c1").ToList();
        var extra = new List<string>();
        if (c1Marks.Contains(key)) extra.Add("vio-c1");
        if (ui.ZeroCapCells.Contains(key)) extra.Add(ZeroCapClass);
        if (ui.ZeroAllowCells.TryGetValue(key, out var za)) extra.Add(za);
        if (extra.Count == 0) return bas;
        return bas.Concat(extra).OrderByDescending(c => MirrorKeys.WeightOf(VioBuckets.FamilyOfVioClass(c))).ToList();
    }

    /// <summary>最重の族に隠れた 2 番目の族のクラス（セルの小さな点。無ければ null）。</summary>
    public static string? SecondVisibleClass(IReadOnlyList<string> classes, IReadOnlySet<string> enabled)
    {
        var visible = classes.Where(c => VioBuckets.VioVisible(c, enabled)).ToList();
        if (visible.Count == 0) return null;
        var top = VioBuckets.FamilyOfVioClass(visible[0]);
        return visible.FirstOrDefault(c => VioBuckets.FamilyOfVioClass(c) != top);
    }

    /// <summary>回数キーのクラスが不足側(▼)か。c2 は職員別合計の下限なので不足側。</summary>
    public static bool IsUnderCountClass(string cls) => cls is "vio-low" or "vio-aptLow" or "vio-c2";

    private static IEnumerable<string> CountKeys(UiState ui) => ui.CountFamilies.Keys.Union(ui.CountViolations.Keys);

    private static IReadOnlyList<string> CountClasses(UiState ui, string key) =>
        ui.CountFamilies.TryGetValue(key, out var f) ? f
        : ui.CountViolations.TryGetValue(key, out var one) ? new[] { one } : Array.Empty<string>();

    /// <summary>回数の族（low/high/apt/c2）が 1 つでもある職員 → 印。「回数」チップが OFF なら空。</summary>
    public static IReadOnlyDictionary<int, CountBadge> CountBadges(UiState ui, IReadOnlySet<string> enabled)
    {
        var under = new HashSet<int>(); var over = new HashSet<int>();
        foreach (var key in CountKeys(ui))
        {
            if (First(key) is not { } i) continue;
            foreach (var cls in CountClasses(ui, key))
            {
                if (!VioBuckets.VioVisible(cls, enabled)) continue;
                if (IsUnderCountClass(cls)) under.Add(i); else over.Add(i);
            }
        }
        return under.Union(over).ToDictionary(i => i, i => new CountBadge(under.Contains(i), over.Contains(i)));
    }

    /// <summary>日ごとの人員の印（不足を先、シフト順）。「人員」チップが OFF なら全日空。</summary>
    public static IReadOnlyList<IReadOnlyList<CoverageMark>> CoverageHeaderMarks(UiState ui, IReadOnlySet<string> enabled, int dayCount)
    {
        var outList = Enumerable.Range(0, dayCount).Select(_ => new List<CoverageMark>()).ToList();
        var keys = ui.NeedFamilies.Count > 0 ? ui.NeedFamilies.Keys : ui.NeedViolations.Keys;
        foreach (var key in keys)
        {
            if (First(key) is not { } k || Second(key) is not { } d || d < 0 || d >= dayCount) continue;
            var classes = ui.NeedFamilies.TryGetValue(key, out var f) ? f
                : ui.NeedViolations.TryGetValue(key, out var one) ? new[] { one } : Array.Empty<string>();
            foreach (var cls in classes)
            {
                if (!VioBuckets.VioVisible(cls, enabled)) continue;
                switch (VioBuckets.FamilyOfVioClass(cls))
                {
                    case "covU": outList[d].Add(new CoverageMark(k, true)); break;
                    case "covO": outList[d].Add(new CoverageMark(k, false)); break;
                }
            }
        }
        return outList.Select(m => (IReadOnlyList<CoverageMark>)m.Distinct().OrderBy(x => !x.Under).ThenBy(x => x.Shift).ToList()).ToList();
    }

    /// <summary>日ヘッダに出す短い字面（例「休▲」、2 つ目以降は「+N」）。</summary>
    public static string CoverageHeaderLabel(UiState ui, IReadOnlyList<CoverageMark> marks)
    {
        if (marks.Count == 0) return "";
        var m = marks[0];
        var sym = m.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[m.Shift] : "?";
        return sym + (m.Under ? "▼" : "▲") + (marks.Count > 1 ? $"+{marks.Count - 1}" : "");
    }

    private static readonly string[] WeekdayJp = { "日", "月", "火", "水", "木", "金", "土" };

    /// <summary>startDate の曜日（0=日）。<c>Problem.Dow0</c> と同じ式、読めなければ 0。</summary>
    public static int Dow0Of(string startDate) =>
        DateOnly.TryParseExact(startDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? (int)d.DayOfWeek : 0;

    /// <summary>
    /// 曜日別の回数 wd（0=日）の偏りを 1 句で言う。e=7×回数−合計（<c>WeeklyDevOfBucket</c> の各項）。
    /// 平均より半回以上多い（e≥4）曜日を「集中」、半回以上少ない（e≤−4）曜日を「少ない」として名指し、両側あれば「／」で並べる。
    /// どちらも無ければ最も外れた 1 日。各側の数値は round(その側の |e| の和 ÷7)。Σe=0 なので両側の和は等しく、
    /// 片側は採点 Σ|e|÷7 のおよそ半分になる。
    /// </summary>
    public static string? WeeklySkewPhrase(int[] wd)
    {
        var c = wd.Sum();
        var e = Enumerable.Range(0, 7).Select(d => 7 * wd[d] - c).ToArray();
        if (e.All(x => x == 0)) return null;
        var amount = (int)Math.Round(e.Where(x => x > 0).Sum() / 7.0, MidpointRounding.AwayFromZero);
        var over = Enumerable.Range(0, 7).Where(d => e[d] >= 4).ToList();
        var under = Enumerable.Range(0, 7).Where(d => e[d] <= -4).ToList();
        if (over.Count == 0 && under.Count == 0)
        {
            if (e.Max() >= -e.Min()) over.Add(Enumerable.Range(0, 7).MaxBy(d => e[d]));
            else under.Add(Enumerable.Range(0, 7).MinBy(d => e[d]));
        }
        string Names(List<int> ds) => string.Join("・", ds.Select(d => WeekdayJp[d]));
        var parts = new List<string>();
        if (over.Count > 0) parts.Add($"{Names(over)}に集中 (+{amount})");
        if (under.Count > 0) parts.Add($"{Names(under)}が少ない (-{amount})");
        return string.Join("／", parts);
    }

    /// <summary>
    /// 職員 i の回数・偏りのシート（行末の印・セルの「この職員の回数・偏り」で共有）。
    /// 回数の族は報告の回数キー、公平化・曜日は <c>DistLocations</c> から。limits は (職員,シフト) → (下限, 上限, 目標)。
    /// </summary>
    public static StaffCountSheet StaffCountSheetOf(UiState ui, int i, Func<string, string> labelOf,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits = null)
    {
        string Sym(int k) => k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
        IReadOnlyList<int> row = i < ui.Schedule.Count ? ui.Schedule[i] : Array.Empty<int>();
        var c1 = ui.C1Shortages.Where(x => x.Staff == i && x.Stuck).DistinctBy(x => x.Shift)
            .Select(sh => $"・{Sym(sh.Shift)}: {labelOf("c1")}（{sh.Day1}日に{sh.Day2}日）— {C1Display.StuckText}").ToList();
        var chips = new List<CountChip>();
        foreach (var key in CountKeys(ui).Where(k => First(k) == i).OrderBy(k => Second(k) ?? 0))
        {
            if (Second(key) is not { } k) continue;
            var count = row.Count(v => v == k);
            var (lo, hi, apt) = limits?.Invoke(i, k) ?? (null, null, null);
            foreach (var cls in CountClasses(ui, key))
            {
                chips.Add(cls switch
                {
                    "vio-low" => new CountChip(Sym(k), count, lo, "下限", true),
                    "vio-high" => new CountChip(Sym(k), count, hi, "上限", false),
                    "vio-aptLow" or "vio-aptHigh" => new CountChip(Sym(k), count, apt, "", cls == "vio-aptLow"),
                    _ => new CountChip(Sym(k), count, null, "", IsUnderCountClass(cls)),
                });
            }
        }
        var dow0 = Dow0Of(ui.StartDate);
        var weekly = (ui.DistLocations.GetValueOrDefault("weekly") ?? Array.Empty<IReadOnlyList<int>>())
            .Where(e => e.Count >= 3 && e[0] == i).OrderBy(e => e[1]).Select(e =>
            {
                var wd = new int[7];
                for (var j = 0; j < row.Count; j++) if (row[j] == e[1]) wd[(dow0 + j) % 7]++;
                return WeeklySkewPhrase(wd) is { } ph ? $"{Sym(e[1])} : {ph}" : null;
            }).OfType<string>().ToList();
        var fair = (ui.DistLocations.GetValueOrDefault("fair") ?? Array.Empty<IReadOnlyList<int>>())
            .Where(e => e.Count >= 3 && e[0] == i).GroupBy(e => e[2]).OrderByDescending(g => g.Key)
            .Select(g => $"差 {g.Key}回 : " + string.Join(", ", g.Select(e => e[1]).Distinct().Order().Select(Sym))).ToList();
        return new StaffCountSheet(chips, weekly, fair, c1);
    }

    /// <summary>セルの「この職員の回数・偏り」用の平文（シートと同じ中身を 1 行ずつ）。</summary>
    public static IReadOnlyList<string> StaffCountLines(UiState ui, int i, Func<string, string> labelOf,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits = null)
    {
        var sh = StaffCountSheetOf(ui, i, labelOf, limits);
        return sh.C1.Concat(sh.Chips.Select(c => (c.Under ? "▼ " : "▲ ") + c.Text))
            .Concat(sh.Weekly.Select(w => $"{labelOf("weekly")} {w}"))
            .Concat(sh.Fair.Select(f => $"{labelOf("fair")} {f}")).ToList();
    }

    /// <summary>日 j の人員の一覧（日ヘッダの印のシート）。</summary>
    public static IReadOnlyList<string> DayCoverageLines(UiState ui, int j, IReadOnlyList<CoverageMark> marks, Func<string, string> labelOf,
        Func<int, int, (int Lo, int Hi)?>? limits = null) =>
        marks.Select(m =>
        {
            var sym = m.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[m.Shift] : m.Shift.ToString();
            var now = ui.Schedule.Count(r => j < r.Count && r[j] == m.Shift);
            var lim = limits?.Invoke(m.Shift, j);
            return m.Under
                ? $"▼ {sym}: {labelOf("covU")}（現在 {now}人" + (lim is { } a ? $"・必要 {a.Lo}人" : "") + "）"
                : $"▲ {sym}: {labelOf("covO")}（現在 {now}人" + (lim is { } b ? $"・適正 {b.Hi}人" : "") + "）";
        }).ToList();

    /// <summary>凡例の「枠の形 → 族」の 1 行。セルに印を持つ族だけを名指す。</summary>
    public static string LegendShapeFamilies(Func<string, string> labelOf) =>
        "実線: " + string.Join("・", new[] { "c3n", "c3w", "pref", "extWish", "groupViol" }.Select(labelOf)) +
        "／破線: " + labelOf("c1") + "（この日を○○にすると近づく）・" + labelOf("c3mn");
}

/// <summary>探す対象（Kotlin <c>FixFocus</c>）。Staff/Shift は <c>FixSuggester</c> の絞り込み、Day は理由の読み取りだけに使う。</summary>
public sealed record FixFocus(int? Staff, int? Shift, int? Day = null, int? ExceptStaff = null)
{
    public string Key => $"{Staff?.ToString() ?? "-"},{Shift?.ToString() ?? "-"},{Day?.ToString() ?? "-"}" + (ExceptStaff is { } x ? $",x{x}" : "");
}

/// <summary>設定への行き先の名（節ごと。「設定を見直す」の一語で済ませない＝利用者決定 2026-09-27）。Kotlin <c>settingsLabelFor</c>。</summary>
public static class SettingsLabel
{
    public static string For(string section) => section switch
    {
        "yr_headcount" => "必要人数の設定を開く",
        "yr_cons" => "並び・期間の制約の設定を開く",
        _ => "回数の設定を開く",
    };
}

/// <summary>手が見つからなかったときの説明。Lines は確かめた事実だけ、WishRelated なら「希望を見る」を出す。</summary>
public sealed record NoFixExplain(IReadOnlyList<string> Lines, bool WishRelated, string SettingsSection);

/// <summary>シフト集計「計（期間）」の人員の印（Kotlin <c>ShiftCoverageTotal</c>）。</summary>
public sealed record ShiftCoverageTotal(IReadOnlyList<int> UnderDays, IReadOnlyList<int> OverDays)
{
    public string Glyph => (UnderDays.Count > 0 ? $"▼{UnderDays.Count}" : "") + (OverDays.Count > 0 ? $"▲{OverDays.Count}" : "");
    public IReadOnlyList<int> Days => UnderDays.Concat(OverDays).Distinct().OrderBy(d => d).ToList();
}

/// <summary>その場の直し方探しの純粋な部分（Kotlin <c>MagiViewState.kt</c> の <c>noFixReasons</c>／<c>shiftCoverageTotals</c>）。</summary>
public static class FixSearchText
{
    public const string NoFixScope = "1 セルの変更・2 人の入れ替え・玉突きの範囲では、必須を増やさずに違反を減らす手が見つかりませんでした。";

    /// <summary>日×シフトの印が人員不足・過剰のものか（直し方が無いときの行き先＝その日のそのシフトの必要人数。群のレンジは④のまま）。Kotlin <c>coverageFocus</c>。</summary>
    public static bool CoverageFocus(UiState ui, FixFocus f)
    {
        if (f.Staff is not null || f.Shift is null || f.Day is null) return false;
        var key = $"{f.Shift},{f.Day}";
        var classes = ui.NeedFamilies.TryGetValue(key, out var fams) ? fams
            : ui.NeedViolations.TryGetValue(key, out var one) ? new[] { one } : Array.Empty<string>();
        return classes.Any(c => VioBuckets.FamilyOfVioClass(c) is "covU" or "covO");
    }

    public static IReadOnlyDictionary<int, ShiftCoverageTotal> ShiftCoverageTotals(IReadOnlyList<IReadOnlyList<CoverageMark>> marks)
    {
        var under = new Dictionary<int, List<int>>(); var over = new Dictionary<int, List<int>>();
        for (var d = 0; d < marks.Count; d++)
            foreach (var m in marks[d])
            {
                var map = m.Under ? under : over;
                if (!map.TryGetValue(m.Shift, out var l)) map[m.Shift] = l = new List<int>();
                l.Add(d);
            }
        return under.Keys.Union(over.Keys).ToDictionary(k => k,
            k => new ShiftCoverageTotal(under.GetValueOrDefault(k) ?? new List<int>(), over.GetValueOrDefault(k) ?? new List<int>()));
    }

    /// <summary>直せる手が 0 件のときの理由を盤面と設定から読む（推測は書かない）。</summary>
    public static NoFixExplain NoFixReasons(UiState ui, FixFocus f,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits = null, Func<int, int, (int Lo, int Hi)?>? needLimits = null)
    {
        var outList = new List<string>();
        var wish = false;
        string Sym(int k) => k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
        int? CellAt(int i, int j) => i >= 0 && i < ui.Schedule.Count && j >= 0 && j < ui.Schedule[i].Count ? ui.Schedule[i][j] : null;
        int Headcount(int k, int j) => ui.Schedule.Count(r => j < r.Count && r[j] == k);
        bool WishIs(int i, int j, int k) => ui.Wishes.TryGetValue($"{i},{j}", out var w) && w == k;
        if (f.Staff is { } si && f.Day is { } sd && CellAt(si, sd) is { } cur && WishIs(si, sd, cur))
        {
            outList.Add($"このセル（{DayText.Short(ui.StartDate, sd)} の「{Sym(cur)}」）は本人の希望で固定されています。"); wish = true;
        }
        IReadOnlyList<string> FamsAt(int i, int j) => ui.ViolationCellFamilies.TryGetValue($"{i},{j}", out var fs) ? fs : Array.Empty<string>();
        // 禁止の並びの相手が本人の希望のセル＝このセルを動かしても並びは希望側に残る。
        if (f.Staff is { } ci && f.Day is { } cd && FamsAt(ci, cd).Contains("vio-c3n"))
        {
            var near = new[] { cd - 1, cd + 1 }.Where(n => CellAt(ci, n) is { } v && WishIs(ci, n, v) && FamsAt(ci, n).Contains("vio-c3n")).ToList();
            if (near.Count > 0)
            {
                outList.Add("この並びには本人の希望（" + string.Join("・", near.Select(n => $"{DayText.Short(ui.StartDate, n)} の「{Sym(CellAt(ci, n)!.Value)}」")) + "）が入っています。");
                wish = true;
            }
        }
        if (f.Staff is { } i && f.Shift is { } k)
        {
            var days = i < ui.Schedule.Count ? Enumerable.Range(0, ui.Schedule[i].Count).Where(j => ui.Schedule[i][j] == k).ToList() : new List<int>();
            var pinned = days.Where(j => WishIs(i, j, k)).ToList();
            if (days.Count > 0 && pinned.Count == days.Count) { outList.Add($"「{Sym(k)}」の {days.Count} 回はどれも本人の希望で固定されています。"); wish = true; }
            else if (pinned.Count > 0) { outList.Add($"「{Sym(k)}」の {days.Count} 回のうち {pinned.Count} 回は本人の希望で固定されています。"); wish = true; }
            var hi = limits?.Invoke(i, k).Hi;
            if (hi == 0 && days.Count > 0) outList.Add($"「{Sym(k)}」は個人の上限が 0 回（入れない指定）です。");
            var tight = days.Where(j => needLimits?.Invoke(k, j) is { } lim && Headcount(k, j) <= lim.Lo).ToList();
            if (tight.Count > 0) outList.Add(string.Join("・", tight.Select(j => DayText.Short(ui.StartDate, j))) + $" は「{Sym(k)}」がその日の必要人数ぎりぎりで、抜けると人員不足になります。");
            if (limits is not null)
            {
                var n = Math.Max(ui.Shifts, ui.ShiftSymbols.Count);
                var fixedOthers = Enumerable.Range(0, n).Where(k2 =>
                {
                    if (k2 == k) return false;
                    var (lo2, hi2, _) = limits(i, k2);
                    return lo2 is { } l && l == hi2 && i < ui.Schedule.Count && ui.Schedule[i].Count(v => v == k2) == l;
                }).ToList();
                if (fixedOthers.Count > 0)
                    outList.Add("ほかの勤務は下限＝上限で固定です（" + string.Join("・", fixedOthers.Select(k2 => $"{Sym(k2)} {limits(i, k2).Lo}回")) + "）。");
            }
        }
        if (f.ExceptStaff is { } ex && f.Day is { } xd && ui.Wishes.TryGetValue($"{ex},{xd}", out var xw) && CellAt(ex, xd) == xw)
        {
            outList.Add($"本人の希望（{DayText.Short(ui.StartDate, xd)} の「{Sym(xw)}」）は守ったままです。"); wish = true;
        }
        if (f.Staff is null && f.Shift is { } dk && f.Day is { } dd)
        {
            var pinned = Enumerable.Range(0, ui.Schedule.Count).Where(s => CellAt(s, dd) == dk && WishIs(s, dd, dk)).ToList();
            if (pinned.Count > 0)
            {
                outList.Add($"{DayText.Short(ui.StartDate, dd)} の「{Sym(dk)}」のうち " + string.Join("・", pinned.Select(s => s < ui.StaffNames.Count ? ui.StaffNames[s] : $"#{s}")) + " は本人の希望で固定されています。");
                wish = true;
            }
        }
        outList.Add(NoFixScope);
        return new NoFixExplain(outList, wish, f.Staff is null && f.Shift is not null ? "yr_headcount" : "yr_count");
    }
}
