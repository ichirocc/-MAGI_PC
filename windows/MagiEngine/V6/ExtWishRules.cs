using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// 拡張希望（基本希望の否定形）の保存規則・禁止表・違反判定（Android <c>ExtWishRules.kt</c>）。正は <c>docs/business-logic.md</c> の「拡張希望」。
/// 違反は必須の族 <c>extWish</c>（重み 8000＝希望と同じ、3.653.0。旧: 採点外）。<c>ViolationReport.ExtWishCells</c> は同じ集合。
/// </summary>
public static class ExtWishRules
{
    /// <summary>保存・前処理の結果。Saved＝残った件（null＝件ごと保存しない）、Notices＝案内。</summary>
    public sealed record SaveResult(ExtWish? Saved, IReadOnlyList<string> Notices);

    public const string MsgWishDay = "希望のある日は、拡張希望に入れられない";
    public const string MsgExtDay = "この日は拡張希望の指定日なので、希望は入れられない";

    private static int? DayIndex(MagiState state, string iso)
    {
        if (!DateOnly.TryParseExact(state.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s0)) return null;
        if (!DateOnly.TryParseExact(iso.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
        var j = d.DayNumber - s0.DayNumber;
        return j >= 0 && j < state.DayCount ? j : null;
    }

    private static string DateOf(MagiState state, int j) =>
        DateOnly.ParseExact(state.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(j).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static HashSet<int> WishDays(MagiState state, int i)
    {
        var outS = new HashSet<int>();
        foreach (var k in state.Wishes.Keys)
        {
            var parts = k.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out var a) && a == i && int.TryParse(parts[1], out var j)) outS.Add(j);
        }
        return outS;
    }

    /// <summary>第 5 節: 期間外の日・存在しない記号・希望シフト日を落とし、空や置けるシフトが残らない件、同じ件の重複を拒む。</summary>
    public static SaveResult Sanitize(MagiState state, ExtWish input, IReadOnlyList<ExtWish>? existing = null)
    {
        existing ??= state.ExtWishes ?? Array.Empty<ExtWish>();
        var notes = new List<string>();
        var i = input.Staff;
        if (i < 0 || i >= state.StaffCount) return new SaveResult(null, new[] { "存在しない職員の拡張希望は保存しない" });
        var days = new List<int>();
        foreach (var d in input.Days)
        {
            var j = DayIndex(state, d);
            if (j is null) notes.Add($"期間外の日付 {d} を外した");
            else if (!days.Contains(j.Value)) days.Add(j.Value);
        }
        var kigou = state.Shifts.Select(s => s.Kigou).ToList();
        var shifts = new List<string>();
        foreach (var s in input.Shifts)
        {
            if (kigou.Contains(s)) { if (!shifts.Contains(s)) shifts.Add(s); }
            else notes.Add($"存在しないシフト記号 {s} を外した");
        }
        var wd = WishDays(state, i);
        if (days.Any(wd.Contains)) { days.RemoveAll(wd.Contains); notes.Add(MsgWishDay); }
        if (days.Count == 0 || shifts.Count == 0) return new SaveResult(null, notes.Append("日か禁止シフトが残らないので保存しない").ToList());
        var p = ScheduleUtil.CachedProblem(state);
        var banned = shifts.Select(x => kigou.IndexOf(x)).ToHashSet();
        // 置けるシフトは、同じ職員の既存の件と合わせた禁止（探索が使う和集合）で日ごとに見る。
        var existingBan = BuildBanTable(state with { ExtWishes = existing }, state.StaffCount, state.DayCount, state.ShiftCount);
        foreach (var j in days)
        {
            if (!Enumerable.Range(0, p.K).Any(k => !banned.Contains(k) && !existingBan.Banned(i, j, k) && p.MayPlace(i, k)))
                return new SaveResult(null, notes.Append($"{j + 1}日に置けるシフトが残らないので保存しない").ToList());
        }
        days.Sort();
        var outW = new ExtWish(i, days.Select(j => DateOf(state, j)).ToList(), kigou.Where(shifts.Contains).ToList());
        if (existing.Any(e => e.Staff == i && e.Days.ToHashSet().SetEquals(outW.Days) && e.Shifts.ToHashSet().SetEquals(outW.Shifts)))
            return new SaveResult(null, notes.Append("同じ日と同じ禁止シフトの拡張希望が既にある").ToList());
        return new SaveResult(outW, notes);
    }

    /// <summary>第 4 節: 基本希望を (i, j) に保存してよいか。だめなら案内を返す。</summary>
    public static string? WishBlockedBy(MagiState state, int i, int j) =>
        (state.ExtWishes ?? Array.Empty<ExtWish>()).Any(e => e.Staff == i && e.Days.Any(d => DayIndex(state, d) == j)) ? MsgExtDay : null;

    /// <summary>
    /// 第 6 節: 禁止表。Flat の [(i*T+j)*K+k] が true＝セル (i,j) に k を置くと違反。割当は見ない。
    /// 希望シフト日は空。拡張希望が 1 つも効かなければ Flat＝null（判定を無料にする）。読み込みで希望の日と重なっていた (i,j) は Overlaps。
    /// </summary>
    public sealed class BanTable
    {
        public int S { get; }
        public int T { get; }
        public int K { get; }
        public bool[]? Flat { get; }
        public IReadOnlyList<(int I, int J)> Overlaps { get; }

        public BanTable(int s, int t, int k, bool[]? flat, IReadOnlyList<(int, int)> overlaps)
        {
            S = s; T = t; K = k; Flat = flat; Overlaps = overlaps;
        }

        public bool IsEmpty => Flat is null;

        public bool Banned(int i, int j, int k)
        {
            var f = Flat;
            if (f is null || i < 0 || i >= S || j < 0 || j >= T || k < 0 || k >= K) return false;
            return f[(i * T + j) * K + k];
        }
    }

    public static BanTable BuildBanTable(MagiState state, int S, int T, int K)
    {
        var ws = state.ExtWishes ?? Array.Empty<ExtWish>();
        if (ws.Count == 0) return new BanTable(S, T, K, null, Array.Empty<(int, int)>());
        bool[]? flat = null;
        var overlaps = new List<(int, int)>();
        var kigou = state.Shifts.Select(s => s.Kigou).ToList();
        foreach (var e in ws)
        {
            var i = e.Staff;
            if (i < 0 || i >= S) continue;
            var ks = e.Shifts.Select(x => kigou.IndexOf(x)).Where(k => k >= 0 && k < K).ToList();
            if (ks.Count == 0) continue;
            var wd = WishDays(state, i);
            foreach (var d in e.Days)
            {
                var jn = DayIndex(state, d);
                if (jn is null || jn.Value >= T) continue;
                var j = jn.Value;
                if (wd.Contains(j)) { if (!overlaps.Contains((i, j))) overlaps.Add((i, j)); continue; }
                flat ??= new bool[S * T * K];
                foreach (var k in ks) flat[(i * T + j) * K + k] = true;
            }
        }
        return new BanTable(S, T, K, flat, overlaps);
    }

    /// <summary>表示用: セル "i,j" → その日に禁止のシフト index（希望シフト日は入らない）。拡張希望が無ければ空。</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<int>> BannedByCell(MagiState state)
    {
        var outD = new Dictionary<string, IReadOnlySet<int>>();
        if ((state.ExtWishes?.Count ?? 0) == 0) return outD;
        var t = BuildBanTable(state, state.StaffCount, state.DayCount, state.ShiftCount);
        if (t.Flat is null) return outD;
        for (var i = 0; i < t.S; i++)
            for (var j = 0; j < t.T; j++)
            {
                var ks = Enumerable.Range(0, t.K).Where(k => t.Flat[(i * t.T + j) * t.K + k]).ToHashSet();
                if (ks.Count > 0) outD[$"{i},{j}"] = ks;
            }
        return outD;
    }

    /// <summary>第 7 節: 違反セル（"i,j"）。未割当は数えない。</summary>
    public static IReadOnlyList<string> Violations(BanTable table, int[][] schedule, int K)
    {
        if (table.IsEmpty) return Array.Empty<string>();
        var outL = new List<string>();
        for (var i = 0; i < schedule.Length; i++)
            for (var j = 0; j < schedule[i].Length; j++)
            {
                var k = schedule[i][j];
                if (k >= 0 && k < K && table.Banned(i, j, k)) outL.Add($"{i},{j}");
            }
        return outL;
    }

    /// <summary>第 8 節: 1 セルを old→next に変えたときのこの件数の差。</summary>
    public static int Delta(BanTable table, int i, int j, int old, int next) =>
        (table.Banned(i, j, next) ? 1 : 0) - (table.Banned(i, j, old) ? 1 : 0);
}
