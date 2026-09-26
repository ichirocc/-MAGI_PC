using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Deeper native port of V6 Web diagnostics (<c>V6SanityPort.kt</c>, 1,507 lines in the source
/// project), split across multiple partial-class files (matching the ~18-file
/// <c>V6HotfixPasses.*.cs</c> house style established for other large multi-file Kotlin
/// sources):
///
/// - This file (phase 4 minimal slice): <see cref="ImpossibleWish"/> and
///   <see cref="V6SanityPort.DetectImpossibleWishes"/>, the single piece
///   <c>V6FinalPort.HandleSmartInitial</c>'s wish gate genuinely depended on before phase 7.
/// - <c>V6SanityPort.Core.cs</c> (phase 7 piece 2): the schedule-independent structural
///   diagnostics — <c>ForcedCovU</c>/<c>StructuralHardFloor</c> (the real implementation of what
///   used to be this file's <c>NotImplementedException</c> stub), <c>OtherShiftCapSum</c>/
///   <c>StructuralPersonalFloor</c>, <c>AptBalance</c>/<c>AptBalances</c>,
///   <c>RestCapacity</c>/<c>RangeOrderConflict</c>/<c>SafeDayLabel</c>.
/// - <c>V6SanityPort.ViolationDebug.cs</c> (phase 7 piece 12): <c>BuildViolationDebug</c>, the
///   schedule-dependent per-run diagnostic log (supply/demand summary, upper/lower-bound check,
///   coverage/count/cell violation detail, the c1-per-rule and weekly breakdowns). Depends only
///   on this file's <c>ForcedCovU</c>/<c>SafeDayLabel</c> plus <see cref="Problem"/>/
///   <c>ScheduleUtil</c>/<c>ViolationReport</c> — no dependency on <c>buildGuidance</c> or
///   <c>c3FamilyJp</c> (now ported in <c>V6SanityPort.Guidance.cs</c>, piece 14/15).
///
/// <c>ConstraintMus</c> (piece 13, <c>ConstraintMus.cs</c>) and the full <c>buildGuidance</c>
/// settings-mistake advisor plus <c>c3FamilyJp</c>/duplicate-sequence detection
/// (piece 14/15, <c>V6SanityPort.Guidance.cs</c>) are ported. <c>V6SanityPort.Build.cs</c>
/// (piece 16, the final piece of this split) ports the remainder: load-data-bit summaries,
/// shift-count diagnostics, and the <c>build()</c> capstone that assembles every piece above into
/// a single <c>V6SanityReport</c>. <c>V6SanityPort.kt</c> is now fully ported.
/// </summary>
public sealed record ImpossibleWish(
    int StaffIndex,
    int DayIndex,
    string StaffName,
    string GroupSymbol,
    string ShiftSymbol,
    string Reason);

/// <summary>希望どうしの衝突（<see cref="V6SanityPort.WishSelfConflicts(Problem)"/>、定義は docs/business-logic.md）。Family="c3n"＝Days は禁止の並びの窓、
/// "c3w"＝Days は [前日, 希望日]。Shifts は Days と同じ順の希望の勤務。</summary>
public sealed record WishSelfConflict(int Staff, string Family, IReadOnlyList<int> Days, IReadOnlyList<int> Shifts)
{
    public IReadOnlyList<string> WishKeys => Days.Select(d => $"{Staff},{d}").ToList();
}

public static partial class V6SanityPort
{
    /// <summary>
    /// Faithful port of Kotlin's <c>detectImpossibleWishes</c>: flags every entry in
    /// <see cref="MagiState.Wishes"/> that can never be honoured — a malformed "i,j" key, an
    /// out-of-range staff/day/shift index, or (the common real case) a wish for a shift the
    /// staff's group cannot take at all (<see cref="ScheduleUtil.CanDo"/> false). Sorted by
    /// (staffIndex, dayIndex) for a stable, deterministic result regardless of
    /// <see cref="MagiState.Wishes"/>'s iteration order.
    /// </summary>
    public static IReadOnlyList<ImpossibleWish> DetectImpossibleWishes(MagiState state, Problem? p = null)
    {
        p ??= new Problem(state);
        var result = new List<ImpossibleWish>();
        foreach (var (key, k) in state.Wishes)
        {
            var parts = key.Split(',');
            int? i = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
            int? j = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
            string? reason = i is null || j is null ? "希望キーが i,j 形式ではありません"
                : i.Value < 0 || i.Value >= p.S || j.Value < 0 || j.Value >= p.T ? "職員または日付が範囲外です"
                : k < 0 || k >= p.K ? "希望シフトが範囲外です"
                : !p.CanDo(i.Value, k) ? "職員のグループでは担当不可です"
                : null;
            if (reason is null) continue;

            int si = i is int iv && iv >= 0 && iv < p.S ? iv : -1;
            int gi = si >= 0 ? p.Sgrp[si] : -1;
            result.Add(new ImpossibleWish(
                StaffIndex: si,
                DayIndex: j ?? -1,
                StaffName: si >= 0 && si < state.StaffList.Count ? state.StaffList[si].Name : $"#{si}",
                GroupSymbol: gi >= 0 && gi < state.Groups.Count
                    ? KigouFormat.ToHankakuKigou(state.Groups[gi].Kigou) : "?",
                ShiftSymbol: k >= 0 && k < state.Shifts.Count
                    ? KigouFormat.ToHankakuKigou(state.Shifts[k].Kigou) : k.ToString(),
                Reason: reason));
        }
        return result.OrderBy(w => w.StaffIndex).ThenBy(w => w.DayIndex).ToList();
    }

    /// <summary>希望どうしの衝突を職員→先頭日→族の順に返す（盤面に依存しない）。同じ職員・同じ日の並びは 1 組（重複行は検査 2 が扱う）。</summary>
    public static IReadOnlyList<WishSelfConflict> WishSelfConflicts(MagiState state) => WishSelfConflicts(ScheduleUtil.CachedProblem(state));

    public static IReadOnlyList<WishSelfConflict> WishSelfConflicts(Problem p)
    {
        var result = new List<WishSelfConflict>();
        var seen = new HashSet<(int Staff, int Start, int Len)>();
        for (var i = 0; i < p.S; i++)
        {
            var mine = new List<WishSelfConflict>();
            foreach (var c in p.Cons3n)
            {
                var seq = c.Seq;
                var d = seq.Length;
                if (d == 0 || d > p.T) continue;
                for (var j = 0; j <= p.T - d; j++)
                {
                    var all = true;
                    for (var l = 0; l < d && all; l++) all = p.WishFixed(i, j + l) && p.Wish[i][j + l] == seq[l];
                    if (all && seen.Add((i, j, d)))
                        mine.Add(new WishSelfConflict(i, "c3n", Enumerable.Range(j, d).ToList(), seq.ToList()));
                }
            }
            if (p.C3wBan != null)
            {
                for (var j = 0; j < p.T - 1; j++)
                {
                    if (p.WishFixed(i, j) && p.C3wBanned(i, j, p.Wish[i][j]))
                        mine.Add(new WishSelfConflict(i, "c3w", new List<int> { j, j + 1 }, new List<int> { p.Wish[i][j], p.Wish[i][j + 1] }));
                }
            }
            result.AddRange(mine.OrderBy(g => g.Days[0]).ThenBy(g => g.Family, StringComparer.Ordinal));
        }
        return result;
    }

    /// <summary>盤面の HARD のうち希望どうしの衝突が必ず生む分の族別件数（下限）。セルを共有しない組ごとに成立なら c3n/c3w・崩れなら pref を 1 件
    /// （職員ごとに区間を終わりの早い順に取る＝最大個数）。ログの仕分け専用＝探索・採否には使わない。</summary>
    public static IReadOnlyDictionary<string, int> WishSelfConflictHard(Problem p, int[][] schedule, IReadOnlyList<WishSelfConflict>? groups = null)
    {
        groups ??= WishSelfConflicts(p);
        var result = new Dictionary<string, int>();
        static int? Cell(int[] row, int d) => d >= 0 && d < row.Length ? row[d] : null;
        foreach (var gs in groups.GroupBy(g => g.Staff))
        {
            var lastEnd = -1;
            foreach (var g in gs.OrderBy(g => g.Days[^1]))
            {
                if (g.Days[0] <= lastEnd) continue;
                lastEnd = g.Days[^1];
                if (g.Staff < 0 || g.Staff >= schedule.Length) continue;
                var row = schedule[g.Staff];
                var holds = g.Family switch
                {
                    "c3w" => Cell(row, g.Days[0]) == g.Shifts[0],
                    _ => Enumerable.Range(0, g.Days.Count).All(t => Cell(row, g.Days[t]) == g.Shifts[t]),
                };
                var key = holds ? g.Family : "pref";
                result[key] = result.GetValueOrDefault(key, 0) + 1;
            }
        }
        return result;
    }
}
