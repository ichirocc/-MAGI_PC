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
/// 勤務表の表示専用の印。Kotlin <c>MagiViewState.kt</c> の同名関数の移植。チェッカーの場所マップ
/// （Violations/CountViolations/NeedViolations）は探索の手掛かりも読むので変えず、報告から別に組み立てる。
/// 族名の日本語化は View 層（<c>AnalysisView.BreakdownLabels</c>）にあるので labelOf で受ける。
/// </summary>
public static class GridDisplayMarks
{
    private static int? First(string key) => int.TryParse(key.AsSpan(0, Math.Max(0, key.IndexOf(','))), out var v) ? v : null;
    private static int? Second(string key) => int.TryParse(key.AsSpan(key.IndexOf(',') + 1), out var v) ? v : null;

    /// <summary>c1 の表示アンカー（セルキー → そのランの違反窓数）。ランの先頭に加えて窓幅おきに置き、ランが覆う日の中に
    /// 収める＝どの違反窓にも少なくとも 1 つ入る。</summary>
    public static IReadOnlyDictionary<string, int> C1DisplayAnchors(UiState ui)
    {
        var outMap = new Dictionary<string, int>();
        foreach (var r in ui.C1Runs)
        {
            if (r.Count < 4) continue;
            int i = r[0], j0 = r[1], n = r[2], w = r[3];
            if (n <= 0 || w <= 0) continue;
            var last = j0 + n - 1 + w - 1;
            for (var a = j0; a <= last; a += w)
            {
                var key = $"{i},{a}";
                outMap[key] = Math.Max(outMap.GetValueOrDefault(key), n);
            }
        }
        return outMap;
    }

    /// <summary>画面に出すセルの違反クラス（重み降順）。チェッカーのクラスに c1 の表示アンカーを足したもの。</summary>
    public static IReadOnlyList<string> DisplayCellClasses(UiState ui, string key, IReadOnlyDictionary<string, int> c1Anchors)
    {
        var bas = VioBuckets.CellVioClasses(ui, key);
        if (!c1Anchors.ContainsKey(key) || bas.Contains("vio-c1")) return bas;
        return bas.Append("vio-c1").OrderByDescending(c => MirrorKeys.WeightOf(VioBuckets.FamilyOfVioClass(c))).ToList();
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

    private static string CountDetail(string cls, int count, int? lo, int? hi, int? apt)
    {
        var body = cls switch
        {
            "vio-low" => lo is { } l ? $"現在 {count}回・下限 {l}回" : null,
            "vio-high" => hi is { } h ? $"現在 {count}回・上限 {h}回" : null,
            "vio-aptLow" or "vio-aptHigh" => apt is { } a ? $"現在 {count}回・目標 {a}回" : null,
            _ => $"現在 {count}回",
        };
        return body is null ? "" : $"（{body}）";
    }

    /// <summary>職員 i の回数・偏りの一覧（行末の印のシートとセルの「この職員の回数・偏り」で共有）。
    /// 回数の族は報告の回数キー、公平化・曜日は <c>DistLocations</c> から。</summary>
    public static IReadOnlyList<string> StaffCountLines(UiState ui, int i, Func<string, string> labelOf,
        Func<int, int, (int? Lo, int? Hi, int? Apt)>? limits = null)
    {
        var outList = new List<string>();
        string Sym(int k) => k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
        foreach (var key in CountKeys(ui).Where(k => First(k) == i).OrderBy(k => Second(k) ?? 0))
        {
            if (Second(key) is not { } k) continue;
            var count = i < ui.Schedule.Count ? ui.Schedule[i].Count(v => v == k) : 0;
            var (lo, hi, apt) = limits?.Invoke(i, k) ?? (null, null, null);
            foreach (var cls in CountClasses(ui, key))
            {
                outList.Add((IsUnderCountClass(cls) ? "▼ " : "▲ ") + $"{Sym(k)}: {labelOf(VioBuckets.FamilyOfVioClass(cls))}" + CountDetail(cls, count, lo, hi, apt));
            }
        }
        if (ui.DistLocations.TryGetValue("fair", out var fair))
            foreach (var e in fair) if (e.Count >= 3 && e[0] == i) outList.Add($"・{Sym(e[1])}: {labelOf("fair")}（グループ内の差 {e[2]}回）");
        if (ui.DistLocations.TryGetValue("weekly", out var weekly))
            foreach (var e in weekly) if (e.Count >= 3 && e[0] == i) outList.Add($"・{Sym(e[1])}: {labelOf("weekly")}（偏り {e[2]}）");
        return outList;
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
        "実線: " + string.Join("・", new[] { "c3n", "c3w", "pref", "groupViol" }.Select(labelOf)) +
        "／破線: " + string.Join("・", new[] { "c1", "c3mn" }.Select(labelOf));
}

/// <summary>探す対象（Kotlin <c>FixFocus</c>）。Staff/Shift は <c>FixSuggester</c> の絞り込み、Day は理由の読み取りだけに使う。</summary>
public sealed record FixFocus(int? Staff, int? Shift, int? Day = null, int? ExceptStaff = null)
{
    public string Key => $"{Staff?.ToString() ?? "-"},{Shift?.ToString() ?? "-"},{Day?.ToString() ?? "-"}" + (ExceptStaff is { } x ? $",x{x}" : "");
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
            outList.Add($"このセル（{sd + 1}日の「{Sym(cur)}」）は本人の希望で固定されています。"); wish = true;
        }
        if (f.Staff is { } i && f.Shift is { } k)
        {
            var days = i < ui.Schedule.Count ? Enumerable.Range(0, ui.Schedule[i].Count).Where(j => ui.Schedule[i][j] == k).ToList() : new List<int>();
            var pinned = days.Where(j => WishIs(i, j, k)).ToList();
            if (days.Count > 0 && pinned.Count == days.Count) { outList.Add($"「{Sym(k)}」の {days.Count} 回はどれも本人の希望で固定されています。"); wish = true; }
            else if (pinned.Count > 0) { outList.Add($"「{Sym(k)}」の {days.Count} 回のうち {pinned.Count} 回は本人の希望で固定されています。"); wish = true; }
            var hi = limits?.Invoke(i, k).Hi;
            if (hi == 0 && days.Count > 0) outList.Add($"「{Sym(k)}」は上限 0（置かない設定）です。");
            var tight = days.Where(j => needLimits?.Invoke(k, j) is { } lim && Headcount(k, j) <= lim.Lo).ToList();
            if (tight.Count > 0) outList.Add(string.Join("・", tight.Select(j => $"{j + 1}日")) + $" は「{Sym(k)}」がその日の必要人数ぎりぎりで、抜けると人員不足になります。");
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
        if (f.Staff is null && f.Shift is { } dk && f.Day is { } dd)
        {
            var pinned = Enumerable.Range(0, ui.Schedule.Count).Where(s => CellAt(s, dd) == dk && WishIs(s, dd, dk)).ToList();
            if (pinned.Count > 0)
            {
                outList.Add($"{dd + 1}日の「{Sym(dk)}」のうち " + string.Join("・", pinned.Select(s => s < ui.StaffNames.Count ? ui.StaffNames[s] : $"#{s}")) + " は本人の希望で固定されています。");
                wish = true;
            }
        }
        outList.Add(NoFixScope);
        return new NoFixExplain(outList, wish, f.Staff is null && f.Shift is not null ? "yr_headcount" : "yr_count");
    }
}
