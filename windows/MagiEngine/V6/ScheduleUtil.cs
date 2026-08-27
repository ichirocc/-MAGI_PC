using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Small schedule-related free functions ported ahead of the full <c>MirrorCore.kt</c> port
/// (phase 3), because <see cref="Problem"/> itself depends on them directly. When phase 3 ports
/// the rest of <c>MirrorCore.kt</c>'s free functions (normalizeSchedule, allowedShiftsForStaff,
/// weeklyFloorOfCount, weeklyDevOfBucket, countMatrix, coverage, cachedProblem, formatDay,
/// withSchedule), this file may be extended or consolidated with that port — not yet decided.
/// </summary>
public static class ScheduleUtil
{
    /// <summary>Index of the shift symbol "休" (rest), or 0 if none is defined.</summary>
    public static int RestShiftIndex(MagiState state)
    {
        for (int i = 0; i < state.Shifts.Count; i++)
            if (state.Shifts[i].Kigou == "休") return i;
        return 0;
    }

    /// <summary>
    /// The shift index to use for an empty/out-of-range/unassigned cell: <paramref name="rest"/>
    /// if the staff can actually take it, otherwise the first shift they can take, otherwise
    /// <paramref name="rest"/> anyway (so this never throws — an invalid input just leaves a
    /// pre-existing inconsistency, rather than crashing the edit operation that surfaced it).
    /// </summary>
    public static int FillShiftIndex(int[] allowed, int rest)
    {
        if (Array.IndexOf(allowed, rest) >= 0) return rest;
        return allowed.Length > 0 ? allowed[0] : rest;
    }
}
