using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// A shift's static (schedule-independent) forced people-shortage floor: however the board is
/// arranged, at least <see cref="Amount"/> covU violations on shift <see cref="ShiftIndex"/> are
/// unavoidable, because too few staff can even take it. See <see cref="V6SanityPort.ForcedCovU"/>.
/// </summary>
public sealed record ForcedCovU(int ShiftIndex, string ShiftSymbol, int Cells, int Amount);

/// <summary>
/// A shift's "target (apt) total vs. what it can actually hold" comparison — see
/// <see cref="V6SanityPort.AptBalances"/>. <see cref="Overloaded"/>/<see cref="Shortfall"/> are
/// computed properties (not stored fields), faithfully mirroring the Kotlin source's <c>val
/// overloaded: Boolean get() = ...</c>/<c>val shortfall: Int get() = ...</c> property accessors.
/// </summary>
public sealed record AptBalance(int ShiftIdx, string Kigou, int AptSum, int Capacity, bool IsRest)
{
    public bool Overloaded => AptSum > Capacity;
    public int Shortfall => Math.Max(AptSum - Capacity, 0);
}

/// <summary>
/// [フェーズ7ピース2] Port of the schedule-independent structural-diagnostic slice of
/// <c>V6SanityPort.kt</c>: <c>forcedCovU</c>/<c>structuralHardFloor</c> (this fills in the
/// <c>NotImplementedException</c> stub <c>V6SanityPort.cs</c> carried since phase 5c —
/// <c>V6NativeOptimizer.RunRsi</c>'s call site needed zero changes, exactly as that stub's own
/// doc comment predicted, since it already wraps the call in a try/catch defaulting to 0),
/// <c>otherShiftCapSum</c>/<c>structuralPersonalFloor</c> (the "forced repertoire minimum" a
/// staff member's OTHER capped shifts leave for one under-target shift),
/// <c>AptBalance</c>/<c>aptBalances</c> (the apt-target-vs-capacity comparison that also backs
/// setting-mistake check 6-C, ported here as a pure function — <c>buildGuidance</c> itself, which
/// turns an overloaded balance into a <c>SettingIssue</c>, lives in
/// <c>V6SanityPort.Guidance.cs</c>, phase-7 piece 14/15), and three
/// small schedule-independent helpers: <c>restCapacity</c>, <c>rangeOrderConflict</c>, and
/// <c>safeDayLabel</c> (the last of which is also used by the <c>build()</c> capstone's
/// schedule-dependent helpers in <c>V6SanityPort.Build.cs</c>, phase-7 piece 16).
///
/// Three genuine Kotlin/.NET divergences were confirmed EMPIRICALLY (real Kotlin execution, and
/// for the third one also a real C# console-app execution) before writing this file, not assumed:
///
/// 1. <see cref="AptBalances"/> is the one sibling in this whole diagnostic family whose default
///    <c>Problem</c> parameter is <c>cachedProblem(state)</c> (the process-wide memoized cache),
///    not the fresh <c>Problem(state)</c> every OTHER function here (and every function ported in
///    phase 5c) defaults to. Preserved verbatim — see <c>ScheduleUtil.CachedProblem</c>.
/// 2. <see cref="SafeDayLabel"/> indexes "月火水木金土日" **Monday-first**
///    (<c>d.dayOfWeek.value - 1</c>, where <c>DayOfWeek.value</c> is Monday=1..Sunday=7) — the
///    OPPOSITE convention from the already-ported <see cref="ScheduleUtil.FormatDay"/>, whose
///    "日月火水木金土" is Sunday-first. These are two genuinely different Kotlin source functions
///    with two genuinely different weekday conventions; they are NOT unified here.
/// 3. <see cref="SafeDayLabel"/>'s date parse is <c>java.time.LocalDate.parse</c> — STRICT
///    (rejects unpadded month/day, any leading/trailing content, out-of-range fields with no
///    calendar-rollover carry arithmetic, and short years) — a completely different leniency
///    profile from <c>FormatDay</c>'s lenient <c>SimpleDateFormat</c>/<c>Calendar</c> combo (which
///    accepts all of those). Empirically confirmed (an 18-case real-Kotlin harness, cross-checked
///    against a 17-case <c>DateOnly.TryParseExact</c> C# harness) that .NET's
///    <c>DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
///    DateTimeStyles.None, out _)</c> matches real Kotlin's strictness exactly for every tested
///    case — so, unlike <c>FormatDay</c>'s hand-rolled <c>TryParseLenientYmd</c> tokenizer, this
///    port needs no custom leniency logic at all; a direct <c>DateOnly.TryParseExact</c> call
///    suffices.
///
/// <see cref="OtherShiftCapSum"/>/<see cref="RestCapacity"/> are ported as <c>internal</c>
/// (matching the source's own <c>internal fun</c> modifier) so they remain directly unit-testable
/// via the <c>InternalsVisibleTo("MagiEngine.Tests")</c> attribute already declared for this
/// assembly. <see cref="SafeDayLabel"/> is likewise <c>internal</c> — it is <c>private</c> in the
/// Kotlin source and has zero direct Kotlin-side test coverage, but is exercised so heavily by
/// <c>buildViolationDebug</c>/<c>buildGuidance</c> (both later pieces) that giving it dedicated
/// direct C#-authored coverage here, while its two confirmed divergence dimensions are freshly
/// verified, is worth the small accessibility widening. <c>needDefined</c>/<c>effectiveDemand</c>/
/// <c>effectiveCap</c> stay <c>private</c> (no direct Kotlin-side test exercises them either, and
/// no new speculative coverage is invented for them here — their real coverage arrives
/// transitively with pieces 12/14/16, exactly as it does in the Kotlin test suite today).
/// </summary>
public static partial class V6SanityPort
{
    /// <summary>
    /// Faithful port of Kotlin's <c>forcedCovU</c>: for every shift, counts how many qualified
    /// staff can take it (<see cref="Problem.CanDo"/>) and, holding that headcount fixed, sums
    /// <see cref="Problem.CovUCell"/> across every day — the covU shortfall that persists no
    /// matter how the schedule is arranged, because too few people are even eligible. Only shifts
    /// with a non-zero forced shortfall are returned.
    /// </summary>
    public static List<ForcedCovU> ForcedCovU(MagiState state, Problem? p = null)
    {
        p ??= new Problem(state);
        var result = new List<ForcedCovU>();
        for (var k = 0; k < p.K; k++)
        {
            var capable = 0;
            for (var i = 0; i < p.S; i++)
                if (p.CanDo(i, k)) capable++;

            var cells = 0;
            var amount = 0;
            for (var j = 0; j < p.T; j++)
            {
                var u = p.CovUCell(k, j, capable);
                if (u > 0) { cells++; amount += u; }
            }
            if (amount > 0)
            {
                var sym = k < state.Shifts.Count
                    ? KigouFormat.ToHankakuKigou(state.Shifts[k].Kigou)
                    : k.ToString();
                result.Add(new ForcedCovU(k, sym, cells, amount));
            }
        }
        return result;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>structuralHardFloor</c> — the sum of every shift's forced
    /// covU floor (<see cref="ForcedCovU"/>). Fills in the phase-5c stub this method used to be;
    /// every existing call site (<c>V6NativeOptimizer.RunRsi</c>'s <c>avoid</c>-set computation)
    /// already wraps the call in a try/catch defaulting to 0, so this real implementation slots in
    /// with no caller changes.
    /// </summary>
    public static int StructuralHardFloor(MagiState state, Problem? p = null)
    {
        p ??= new Problem(state);
        return ForcedCovU(state, p).Sum(x => x.Amount);
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>otherShiftCapSum</c>: for staff <paramref name="i"/>, the sum
    /// of upper bounds (<see cref="Problem.RangeHi"/>, clamped to <c>[0, T]</c>, uncapped ⇒ full
    /// <see cref="Problem.T"/>) across every OTHER shift they can take — i.e. the most days they
    /// could possibly spend on shifts other than <paramref name="k"/> while still respecting every
    /// individual upper bound. Short-circuits once the running sum already reaches
    /// <see cref="Problem.T"/> (adding more can only saturate, never matter further).
    /// </summary>
    internal static int OtherShiftCapSum(Problem p, int i, int k)
    {
        var sum = 0;
        for (var k2 = 0; k2 < p.K; k2++)
        {
            if (k2 == k || !p.CanDo(i, k2)) continue;
            var hi = p.RangeHi[i][k2];
            sum += hi == int.MaxValue ? p.T : Math.Min(Math.Max(hi, 0), p.T);
            if (sum >= p.T) return sum;
        }
        return sum;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>structuralPersonalFloor</c>: for every staff member, the
    /// largest "forced repertoire minimum" across their under-target (<see cref="Problem.Apt"/>)
    /// shifts — how many days of shift <c>k</c> they are forced onto once every OTHER shift they
    /// can take is filled to its individual cap (<see cref="OtherShiftCapSum"/>), minus their
    /// target for <c>k</c> itself. Summed across all staff. A positive per-staff contribution means
    /// that staff member's own repertoire makes their apt/high combined shortfall for that shift
    /// structurally unavoidable, independent of any particular schedule.
    /// </summary>
    public static int StructuralPersonalFloor(Problem p)
    {
        var floor = 0;
        for (var i = 0; i < p.S; i++)
        {
            var best = 0;
            for (var k = 0; k < p.K; k++)
            {
                var t = p.Apt[i][k];
                if (t < 0 || !p.CanDo(i, k)) continue;
                var d = (p.T - OtherShiftCapSum(p, i, k)) - t;
                if (d > best) best = d;
            }
            floor += best;
        }
        return floor;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>restCapacity</c>: the total number of days, summed across
    /// every staff member who can take the rest shift, that they could spend resting once every
    /// OTHER shift's individual LOWER bound (<see cref="Problem.RangeLo"/>, when positive) is
    /// satisfied first. Used by <see cref="AptBalances"/> in place of a seat-count comparison for
    /// the rest shift specifically, since rest has no meaningful "how many seats" concept.
    /// </summary>
    internal static int RestCapacity(Problem p)
    {
        var k = p.RestIdx;
        var cap = 0;
        for (var i = 0; i < p.S; i++)
        {
            if (!p.CanDo(i, k)) continue;
            var minOther = 0;
            for (var k2 = 0; k2 < p.K; k2++)
            {
                if (k2 == k || !p.CanDo(i, k2)) continue;
                var lo2 = p.RangeLo[i][k2];
                if (lo2 != int.MinValue && lo2 > 0) minOther += lo2;
            }
            cap += Math.Max(0, p.T - minOther);
        }
        return cap;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>aptBalances</c>: for every shift with at least one staff
    /// member's apt target set, compares the summed target (<see cref="AptBalance.AptSum"/>)
    /// against what the shift can actually hold (<see cref="AptBalance.Capacity"/>) — the rest
    /// shift uses <see cref="RestCapacity"/>, every other shift sums its per-day effective upper
    /// bound (<see cref="EffectiveCap"/>) across the days it has any demand defined at all
    /// (<see cref="NeedDefined"/>), skipping the shift entirely if it has no demand-defined days.
    /// Note the default parameter's divergence from every OTHER function in this file: this one
    /// defaults to <see cref="ScheduleUtil.CachedProblem"/> (the memoized cache), not a fresh
    /// <c>Problem(state)</c> — confirmed against the Kotlin source, not assumed.
    /// </summary>
    public static IReadOnlyList<AptBalance> AptBalances(MagiState state, Problem? p = null)
    {
        p ??= ScheduleUtil.CachedProblem(state);
        var result = new List<AptBalance>();
        for (var k = 0; k < p.K; k++)
        {
            var aptSum = 0;
            var anyApt = false;
            for (var i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, k)) continue;
                var a = p.Apt[i][k];
                if (a >= 0) { aptSum += a; anyApt = true; }
            }
            if (!anyApt) continue;

            // NOTE: unlike ForcedCovU above, aptBalances does NOT half-width-convert the symbol
            // (no KigouFormat.ToHankakuKigou call) — confirmed against the Kotlin source, which
            // reads `state.shifts.getOrNull(k)?.kigou ?: k.toString()` here with no `toHankakuKigou`
            // wrapper, a deliberate asymmetry with `forcedCovU`'s symbol resolution preserved as-is.
            var sym = k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();

            if (k == p.RestIdx)
            {
                result.Add(new AptBalance(k, sym, aptSum, RestCapacity(p), IsRest: true));
            }
            else
            {
                var seatsHi = 0;
                var hasDemand = false;
                for (var j = 0; j < p.T; j++)
                {
                    if (!NeedDefined(p, k, j)) continue;
                    hasDemand = true;
                    seatsHi += Math.Max(EffectiveCap(p, k, j), 0);
                }
                if (!hasDemand) continue;
                result.Add(new AptBalance(k, sym, aptSum, seatsHi, IsRest: false));
            }
        }
        return result;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>rangeOrderConflict</c>: parses both bounds (trimmed;
    /// non-numeric or blank ⇒ no conflict to report, matching how an unset bound is represented as
    /// blank string throughout this app's data model) and reports a conflict only when the lower
    /// bound strictly exceeds the upper one. Returns the two PARSED values (not the raw strings),
    /// matching the Kotlin source's <c>Pair&lt;Int, Int&gt;</c> return type.
    /// </summary>
    public static (int Lo, int Hi)? RangeOrderConflict(string? lo, string? hi)
    {
        var l = KotlinInterop.ToIntOrNull(lo?.Trim());
        var h = KotlinInterop.ToIntOrNull(hi?.Trim());
        if (l is null || h is null) return null;
        return l.Value > h.Value ? (l.Value, h.Value) : null;
    }

    /// <summary>Faithful port of Kotlin's private <c>needDefined</c>.</summary>
    private static bool NeedDefined(Problem p, int k, int j) =>
        p.Need1[k][j] >= 0 || (p.Use2 && p.Need2[k][j] >= 0);

    /// <summary>Faithful port of Kotlin's private <c>effectiveDemand</c>.</summary>
    private static int EffectiveDemand(Problem p, int k, int j) => p.CovUCell(k, j, 0);

    /// <summary>Faithful port of Kotlin's private <c>effectiveCap</c>.</summary>
    private static int EffectiveCap(Problem p, int k, int j)
    {
        if (!NeedDefined(p, k, j)) return -1;
        var h = 0;
        while (h < p.S && p.CovOCell(k, j, h + 1) == 0) h++;
        return h;
    }

    /// <summary>
    /// Faithful port of Kotlin's private <c>safeDayLabel</c>. Made <c>internal</c> (not
    /// <c>private</c>) so it can be exercised directly by <c>MagiEngine.Tests</c> — see this
    /// file's own class-level doc comment for the two confirmed divergences from the
    /// already-ported <see cref="ScheduleUtil.FormatDay"/> (Monday-first weekday indexing; strict
    /// rather than lenient date parsing) that make this a genuinely distinct function, not a
    /// duplicate to be unified with it.
    /// </summary>
    internal static string SafeDayLabel(string startDate, int offset)
    {
        try
        {
            if (offset < 0)
                throw new ArgumentException("offset must be non-negative");
            if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                throw new FormatException($"'{startDate}' is not a valid yyyy-MM-dd date");
            var d = parsed.AddDays(offset);
            var weekday = "月火水木金土日"[((int)d.DayOfWeek + 6) % 7];
            return $"{d.Month}/{d.Day}({weekday})";
        }
        catch (Exception)
        {
            return $"{offset + 1}日";
        }
    }
}
