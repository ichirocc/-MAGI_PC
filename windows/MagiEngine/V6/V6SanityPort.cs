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
/// (piece 14/15, <c>V6SanityPort.Guidance.cs</c>) are now ported. The remainder of
/// <c>V6SanityPort.kt</c> (load-data-bit summaries, shift-count diagnostics, and the
/// <c>build()</c> capstone) belongs to phase-7 piece 16 and is not in any file yet.
/// </summary>
public sealed record ImpossibleWish(
    int StaffIndex,
    int DayIndex,
    string StaffName,
    string GroupSymbol,
    string ShiftSymbol,
    string Reason);

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
}
