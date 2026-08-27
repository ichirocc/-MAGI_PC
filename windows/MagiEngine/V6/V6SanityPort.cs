using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Deeper native port of V6 Web diagnostics (<c>V6SanityPort.kt</c>, 1,507 lines in the source
/// project). This file is a **phase 4 minimal slice**: only <see cref="ImpossibleWish"/> and
/// <see cref="V6SanityPort.DetectImpossibleWishes"/>, the single piece
/// <c>V6FinalPort.HandleSmartInitial</c>'s wish gate genuinely depends on. The remainder of
/// <c>V6SanityPort.kt</c> (load-data-bit summaries, shift-count diagnostics, duplicate-sequence
/// detection, the full <c>buildGuidance</c> settings-mistake advisor, etc.) is phase 7 scope
/// ("V6FinalPort統括・CSV・診断" in the migration plan) and is deliberately not ported here.
/// </summary>
public sealed record ImpossibleWish(
    int StaffIndex,
    int DayIndex,
    string StaffName,
    string GroupSymbol,
    string ShiftSymbol,
    string Reason);

public static class V6SanityPort
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

    /// <summary>
    /// **Phase 5c stub.** Faithful port target is Kotlin's <c>structuralHardFloor(state, p) =
    /// forcedCovU(state, p).sumOf {{ it.amount }}</c> — a static lower bound on unavoidable covU
    /// (people-shortage) violations, derived from qualified-staff headcounts alone (independent of
    /// any particular schedule). <c>forcedCovU</c> itself lives deeper in the still-unported,
    /// phase-7-scoped remainder of <c>V6SanityPort.kt</c> (1,507 lines total; see this class's own
    /// doc comment), so implementing this faithfully now would require pulling in more of that file
    /// than phase 5c's scope calls for.
    ///
    /// Every phase-5c call site in the Kotlin source (<c>runRsi</c>'s <c>avoid</c>-set computation)
    /// wraps this call in a <c>try {{ ... }} catch (_: Exception) {{ 0 }}</c> — i.e. the ported
    /// callers already tolerate this returning 0 (or throwing) as a degenerate case. This stub
    /// throws <see cref="NotImplementedException"/> so that <c>V6NativeOptimizer.RunRsi</c>'s
    /// equivalent try/catch-defaulting-to-0 wrapper produces byte-identical behavior to what it
    /// will once phase 7 implements this properly — no changes to <c>RunRsi</c> will be needed at
    /// that point, only this method's body.
    /// </summary>
    public static int StructuralHardFloor(MagiState state, Problem p) =>
        throw new NotImplementedException(
            "V6SanityPort.StructuralHardFloor is phase-7 scope (forcedCovU's full diagnostic " +
            "chain); callers must catch and default to 0, matching the Kotlin source's own " +
            "try/catch wrapper at every phase-5c call site.");
}
