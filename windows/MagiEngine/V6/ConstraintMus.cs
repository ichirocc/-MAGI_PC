using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース13] Faithful port of Kotlin's <c>ConstraintMus</c> (<c>ConstraintMus.kt</c>,
/// 242 lines) — a minimal-explanation ("MUS", minimally unsatisfiable subset) engine for
/// contradictions among a schedule's declared constraints.
///
/// v8 concept (Constraint IR + CP-SAT diagnostic lane + MUS/IIS), stage 1 — dependency-free.
/// Design principles carried over verbatim from the Kotlin source:
///  - The IR (<see cref="Item"/> and its subtypes) is declarative metadata only — no evaluator.
///    Runtime semantics stay fixed at the checker/Evaluator/C++ three-way parity; this is
///    deliberately NOT a fourth semantics. A drift in the IR can only produce a wrong
///    *diagnosis* (visible, harmless), never a wrong *schedule*.
///  - Infeasibility is judged by sound-but-incomplete proof rules only: a positive result means
///    a genuine contradiction. The proof techniques are (a) the exact DP
///    <see cref="SmartInitialScheduler.MinDaysForFullCompliance"/> (the true minimum days needed
///    to satisfy a window rule), (b) a pigeonhole argument (one staff member's total shift demand
///    exceeds the period length), (c) bipartite matching (whether a day's staffing need can be met
///    under fixed wishes — a relaxation that ignores cross-day constraints, so an "infeasible"
///    verdict from it stays sound: reality is only ever more constrained). Missed contradictions
///    (incompleteness) are the safe side — zero false positives, matching the "never claim a false
///    wall" design already established for check 2b-2.
///  - Deletion-based MUS: if a full item set is provably infeasible, items are removed one at a
///    time (whenever the proof still holds without it) until removing anything further breaks the
///    proof — leaving a minimal "these can't all be satisfied at once" set. Each surviving member
///    is a candidate the user could relax (IIS-style suggestion).
///  - Hot-path invariant, read-only, no scoring impact. Output feeds the existing SettingIssue
///    channel (V6SanityPort check 9).
///
/// Division of labour with the existing hand-written checks (2b-3 = single-shift cap × window,
/// 6b/6c = forced lower bound from capped shifts, check 3 = lower-bound sum): this engine owns
/// contradictions that involve a wish (<c>WishLocked</c>) — none of the existing checks look at
/// wishes. The presenter (<see cref="V6SanityPort"/>) keeps overlap at zero by only surfacing
/// cores that contain a wish.
/// </summary>
public static class ConstraintMus
{
    /// <summary>IR: a constraint the user can actually relax one at a time. Carries no evaluator (design principle).</summary>
    public abstract record Item;

    /// <summary>A fixed assignment from a realizable wish (per <c>Problem.WishLocked</c> — unrealizable wishes are never included).</summary>
    public sealed record WishPin(int Staff, int Day, int Shift) : Item;

    /// <summary>A personal upper bound (staffRange hi, defined entries only).</summary>
    public sealed record RangeCap(int Staff, int Shift, int Hi) : Item;

    /// <summary>A personal lower bound (staffRange lo, lo &gt; 0 only).</summary>
    public sealed record RangeFloor(int Staff, int Shift, int Lo) : Item;

    /// <summary>A window rule (cons1, one row = one entry; in the staff-scope analysis this only applies to staff who <c>CanDo</c> it).</summary>
    public sealed record WindowRule(int Shift, int WindowDays, int MinCount) : Item;

    /// <summary>The effective lower bound for a day's staffing need, derived from <c>covUCell</c>'s semantics (the smallest headcount that produces no shortfall).</summary>
    public sealed record DayNeed(int Day, int Shift, int Need) : Item;

    public sealed record StaffConflict(int Staff, IReadOnlyList<Item> Core);

    public sealed record DayConflict(int Day, IReadOnlyList<Item> Core);

    /// <summary>
    /// [performance] Process-wide cache for <see cref="SmartInitialScheduler.MinDaysForFullCompliance"/>
    /// (can take hundreds of ms for a 15-day window). Key = (T, rule subset) is a pure function of
    /// the input, so caching is safe. <c>BuildGuidance</c> runs on every cell edit (via
    /// <c>MakeUi</c>'s <c>AnalyzeParallel</c>), so the DP is paid once and subsequent calls are
    /// near-0ms. Rule sets rarely change and the subset space is tiny (at most 2^(rules per shift)).
    /// A null result (computation impossible) is kept as the <see cref="NullSentinel"/> marker.
    ///
    /// The cache key is unbounded only in the sense that it grows with T/rule edits over a session
    /// — but this is a pure memo, so discarding it never affects correctness; once the entry count
    /// hits <see cref="MaxCacheEntries"/> the whole cache is dropped (paying only a recompute cost).
    /// Real usage never comes close to this ceiling.
    /// </summary>
    private const int MaxCacheEntries = 4096;

    private static readonly ConcurrentDictionary<string, int> MinDaysCache = new();
    private const int NullSentinel = int.MinValue;

    /// <summary>Cache-backed <c>MinDaysForFullCompliance</c>, shared between this engine and check 2b-3 (same pure function).</summary>
    public static int? CachedMinDays(int t, IReadOnlyList<(int Days, int Minimum)> rules)
    {
        if (rules.Count == 0) return 0;
        // Delimited key: an unseparated concatenation of "first*1000+second"-style keys would let
        // (1,1000) and (2,0) collide at 2000. minCount>=1000 is unusual but a legal input, and this
        // is a process-wide cache, so a false hit would BE the wrong diagnosis. A delimiter removes
        // the collision structurally.
        var key = t.ToString(CultureInfo.InvariantCulture) + "|" +
            string.Join(",", rules.Select(r => $"{r.Days}:{r.Minimum}").OrderBy(s => s, StringComparer.Ordinal));
        if (MinDaysCache.TryGetValue(key, out var cached)) return cached == NullSentinel ? null : cached;
        var v = SmartInitialScheduler.MinDaysForFullCompliance(t, rules);
        if (MinDaysCache.Count >= MaxCacheEntries) MinDaysCache.Clear();
        MinDaysCache[key] = v ?? NullSentinel;
        return v;
    }

    /// <summary>
    /// Detects a provably-infeasible constraint set within a single staff member's scope, for every
    /// staff member, shrinking each to a minimal core. Proof rules (using only the active items —
    /// this is what guarantees the MUS semantics):
    ///  A) Shift x's demand lower bound demand_x = max(floor, exact minimum window days, count of
    ///     fixed wishes for x) exceeds its cap cap_x.
    ///  B) Pigeonhole: sum_x demand_x exceeds T (each day is exactly one shift — only T days exist).
    ///  C) Forced floor: if every other assignable shift has a cap, count_x >= T − sum(other caps) exceeds cap_x.
    /// </summary>
    public static List<StaffConflict> AnalyzeStaffConflicts(Problem p)
    {
        var outList = new List<StaffConflict>();
        for (var i = 0; i < p.S; i++)
        {
            var allowed = p.AllowedShiftsForStaff(i);
            if (allowed.Length == 0) continue;
            var universe = new List<Item>();
            foreach (var c in p.Cons1)
            {
                if (c.ShiftIdx < 0 || c.ShiftIdx >= p.K || c.Day1 < 1 || c.Day1 > p.T || c.Day2 < 1 || c.Day2 > c.Day1) continue;
                if (!p.CanDo(i, c.ShiftIdx)) continue;
                universe.Add(new WindowRule(c.ShiftIdx, c.Day1, c.Day2));
            }
            foreach (var k in allowed)
            {
                var lo = p.RangeLo[i][k];
                if (lo != int.MinValue && lo > 0) universe.Add(new RangeFloor(i, k, lo));
                var hi = p.RangeHi[i][k];
                if (hi != int.MaxValue) universe.Add(new RangeCap(i, k, hi));
            }
            for (var j = 0; j < p.T; j++) if (p.WishLocked(i, j)) universe.Add(new WishPin(i, j, p.Wish[i][j]));
            if (universe.Count == 0) continue;
            if (!StaffProvablyInfeasible(p, allowed, universe)) continue;
            var core = Shrink(universe, items => StaffProvablyInfeasible(p, allowed, items));
            if (core.Count > 0) outList.Add(new StaffConflict(i, core));
        }
        return outList;
    }

    /// <summary>
    /// Detects a provably-infeasible constraint set within a single day's scope — the day's
    /// staffing need cannot be met by bipartite matching under fixed wishes. This ignores cross-day
    /// constraints (c3n, counts, etc.), so it's a relaxation: an "infeasible" verdict here stays
    /// sound (reality is only ever more constrained).
    /// </summary>
    public static List<DayConflict> AnalyzeDayConflicts(Problem p)
    {
        var outList = new List<DayConflict>();
        for (var j = 0; j < p.T; j++)
        {
            var universe = new List<Item>();
            for (var k = 0; k < p.K; k++)
            {
                var eff = EffectiveLowerBound(p, k, j);
                if (eff > 0) universe.Add(new DayNeed(j, k, eff));
            }
            if (!universe.Any(it => it is DayNeed)) continue;
            for (var i = 0; i < p.S; i++) if (p.WishLocked(i, j)) universe.Add(new WishPin(i, j, p.Wish[i][j]));
            if (!DayProvablyInfeasible(p, universe)) continue;
            var core = Shrink(universe, items => DayProvablyInfeasible(p, items));
            if (core.Count > 0) outList.Add(new DayConflict(j, core));
        }
        return outList;
    }

    /// <summary>The smallest headcount, derived from <c>covUCell</c> (source of truth), that produces no shortfall. Even S staff can still leave a shortfall, in which case this returns S+1.</summary>
    private static int EffectiveLowerBound(Problem p, int k, int j)
    {
        for (var g = 0; g <= p.S; g++) if (p.CovUCell(k, j, g) <= 0) return g;
        return p.S + 1;
    }

    /// <summary>Deletion-based MUS. Precondition: <c>infeasible(universe) == true</c>.</summary>
    private static List<Item> Shrink(List<Item> universe, Func<List<Item>, bool> infeasible)
    {
        var core = new List<Item>(universe);
        var i = 0;
        while (i < core.Count)
        {
            var removed = core[i];
            core.RemoveAt(i);
            if (!infeasible(core))
            {
                core.Insert(i, removed);
                i++;
            }
            // A successful removal leaves the next element at the same index i, so i does not advance.
        }
        return core;
    }

    private static bool StaffProvablyInfeasible(Problem p, int[] allowed, List<Item> items)
    {
        var caps = new Dictionary<int, int>();
        var floors = new Dictionary<int, int>();
        var rules = new Dictionary<int, List<WindowRule>>();
        var pins = new Dictionary<int, int>();
        foreach (var it in items)
        {
            switch (it)
            {
                case RangeCap rc:
                    caps[rc.Shift] = rc.Hi;
                    break;
                case RangeFloor rf:
                    floors[rf.Shift] = rf.Lo;
                    break;
                case WindowRule wr:
                    if (!rules.TryGetValue(wr.Shift, out var list)) rules[wr.Shift] = list = new List<WindowRule>();
                    list.Add(wr);
                    break;
                case WishPin wp:
                    pins[wp.Shift] = pins.GetValueOrDefault(wp.Shift, 0) + 1;
                    break;
                case DayNeed:
                    break;
            }
        }
        // The window rule's exact minimum days (memoized per rule subset). null (computation
        // impossible, e.g. t > 62) is treated as 0 — that weakens the lower bound, which stays
        // sound (never a false positive).
        int MinWin(int x)
        {
            if (!rules.TryGetValue(x, out var rs) || rs.Count == 0) return 0;
            return CachedMinDays(p.T, rs.Select(r => (r.WindowDays, r.MinCount)).ToList()) ?? 0;
        }
        int Demand(int x) => Math.Max(Math.Max(floors.GetValueOrDefault(x, 0), MinWin(x)), pins.GetValueOrDefault(x, 0));

        // A) demand lower bound > cap
        foreach (var x in allowed)
        {
            if (!caps.TryGetValue(x, out var cap)) continue;
            if (Demand(x) > cap) return true;
        }
        // B) pigeonhole: sum of demand lower bounds > T
        long sum = 0;
        foreach (var x in allowed) sum += Demand(x);
        if (sum > p.T) return true;
        // C) forced floor: only when every other assignable shift has a cap (undefined = unlimited, so this never fires there — conservative)
        foreach (var x in allowed)
        {
            var cap = caps.GetValueOrDefault(x, int.MaxValue);
            long otherCapSum = 0;
            var allCapped = true;
            foreach (var y in allowed)
            {
                if (y == x) continue;
                if (!caps.TryGetValue(y, out var cy)) { allCapped = false; break; }
                otherCapSum += Math.Min(cy, p.T);
            }
            if (!allCapped) continue;
            var forcedMin = p.T - otherCapSum;
            if (forcedMin > cap) return true;
        }
        return false;
    }

    private static bool DayProvablyInfeasible(Problem p, List<Item> items)
    {
        var pinned = new Dictionary<int, int>();
        var slots = new List<int>();
        foreach (var item in items)
        {
            switch (item)
            {
                case WishPin wp:
                    pinned[wp.Staff] = wp.Shift;
                    break;
                case DayNeed dn:
                    var s = dn.Shift;
                    for (var k = 0; k < Math.Min(dn.Need, p.S + 1); k++) slots.Add(s);
                    break;
            }
        }
        if (slots.Count == 0) return false;
        if (slots.Count > p.S) return true;   // seat count exceeds staff count: unfillable regardless of fixed wishes (sound)
        var staffMatch = new int[p.S];        // staff -> slot
        Array.Fill(staffMatch, -1);
        var slotMatch = new int[slots.Count]; // slot -> staff
        Array.Fill(slotMatch, -1);

        bool CanServe(int i, int shift)
        {
            if (!p.CanDo(i, shift)) return false;
            if (!pinned.TryGetValue(i, out var pin)) return true;
            return pin == shift;
        }

        bool TryAugment(int slot, bool[] visited)
        {
            for (var i = 0; i < p.S; i++)
            {
                if (visited[i] || !CanServe(i, slots[slot])) continue;
                visited[i] = true;
                var cur = staffMatch[i];
                if (cur == -1 || TryAugment(cur, visited))
                {
                    staffMatch[i] = slot;
                    slotMatch[slot] = i;
                    return true;
                }
            }
            return false;
        }

        var matched = 0;
        for (var s = 0; s < slots.Count; s++) if (TryAugment(s, new bool[p.S])) matched++;
        return matched < slots.Count;
    }
}
