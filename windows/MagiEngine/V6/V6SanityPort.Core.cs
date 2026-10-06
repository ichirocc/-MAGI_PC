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
            var cells = 0;
            var amount = 0;
            for (var j = 0; j < p.T; j++)
            {
                var u = p.CovUCell(k, j, PlaceableFor(p, k, j));   // [3.507.5] 置ける人数（上限 0 は除外、希望固定は含む）
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
    /// <summary>希望どうしの衝突が生む HARD の区間（report.Hard の単位＝cons3n は行ごと・窓ごとに 1、c3w はセルごとに 1）。
    /// c3n＝窓の全セルが希望で固定され禁止の並びそのもの、c3w＝希望 Y で固定したセル（翌日の希望 X が禁じる）。</summary>
    private sealed record WishHardSpan(int Staff, int From, int To, string Family);

    private static List<WishHardSpan> WishHardSpans(Problem p)
    {
        var result = new List<WishHardSpan>();
        for (var i = 0; i < p.S; i++)
        {
            foreach (var c in p.Cons3n)
            {
                var d = c.Seq.Length;
                if (d == 0 || d > p.T) continue;
                for (var j = 0; j <= p.T - d; j++)
                {
                    var all = true;
                    for (var l = 0; l < d && all; l++) all = p.WishFixed(i, j + l) && p.Wish[i][j + l] == c.Seq[l];
                    if (all) result.Add(new WishHardSpan(i, j, j + d - 1, "c3n"));
                }
            }
            if (p.C3wBan != null)
                for (var j = 0; j < p.T; j++)
                    if (p.WishFixed(i, j) && p.C3wBanned(i, j, p.Wish[i][j])) result.Add(new WishHardSpan(i, j, j, "c3w"));
        }
        return result;
    }

    /// <summary>「希望と禁止の衝突」の件数（HF70・残存分析・E0 の到達判定が共有する単一ソース、report.Hard と同じ単位）。
    /// c3n/c3w＝盤面で成立している衝突の窓/セル、pref＝衝突の区間にかかる希望を崩したセル。</summary>
    public static IReadOnlyDictionary<string, int> WishConflictHard(Problem p, int[][] schedule)
    {
        var result = new Dictionary<string, int>();
        var prefCells = new HashSet<(int, int)>();
        foreach (var sp in WishHardSpans(p))
        {
            if (sp.Staff < 0 || sp.Staff >= schedule.Length) continue;
            var row = schedule[sp.Staff];
            var held = true;
            for (var j = sp.From; j <= sp.To; j++)
                if (j >= row.Length || row[j] != p.Wish[sp.Staff][j]) { held = false; prefCells.Add((sp.Staff, j)); }
            if (held) result[sp.Family] = result.GetValueOrDefault(sp.Family, 0) + 1;
        }
        if (prefCells.Count > 0) result["pref"] = prefCells.Count;
        return result;
    }

    /// <summary>必須 <paramref name="hard"/> 件のうち希望どうしのぶつかり（<see cref="WishConflictHard"/>）の件数。族ごとに報告の内訳で頭打ちにする（必須内訳ログ・ホームが共有）。</summary>
    public static int WishConflictHardShare(IReadOnlyDictionary<string, int> selfConflict, IReadOnlyDictionary<string, int> breakdown, int hard) =>
        Math.Clamp(selfConflict.Sum(kv => Math.Min(kv.Value, breakdown.GetValueOrDefault(kv.Key))), 0, Math.Max(hard, 0));

    /// <summary>[E0] 希望衝突の床（report.Hard と同単位、<see cref="StructuralHardFloor"/> とは別に扱う）＝衝突の最小 HARD＋日の証明の日数。</summary>
    public static int WishConflictHardFloor(Problem p)
    {
        var (c, d) = WishConflictFloorParts(p);
        return c + d;
    }

    /// <summary><see cref="WishConflictHardFloor"/> の内訳（衝突の最小 HARD, 日の証明の日数）。衝突は職員ごとに「崩すセル数＋崩れずに残る区間数」の最小を
    /// 区間 DP で出す（どの盤面でも <see cref="WishConflictHard"/> の合計以下）。日の証明は衝突の区間の日と構造的 covU の日を数えない。
    /// <paramref name="zeroCapBinding"/>＝上限 0 に頼る日の証明も数える（ログの参考値 N_mayPlace。MayPlace を守る盤面でだけ健全＝頭打ちには使わない）。</summary>
    public static (int Conflict, int Days) WishConflictFloorParts(Problem p, bool zeroCapBinding = false)
    {
        const int Inf = int.MaxValue / 2;
        var spans = WishHardSpans(p);
        var conflict = 0;
        var conflictDays = new HashSet<int>();
        foreach (var ss in spans.GroupBy(s => s.Staff))
        {
            foreach (var sp in ss) for (var j = sp.From; j <= sp.To; j++) conflictDays.Add(j);
            var byEnd = ss.ToLookup(s => s.To);
            // f[b+1]＝最後に崩したセルが b（-1＝まだ無し）のときの最小費用。
            var f = new int[p.T + 1];
            for (var b = 1; b <= p.T; b++) f[b] = Inf;
            for (var t = 0; t < p.T; t++)
            {
                var g = new int[p.T + 1];
                Array.Fill(g, Inf);
                for (var b = 0; b <= p.T; b++)
                {
                    if (f[b] >= Inf) continue;
                    g[b] = Math.Min(g[b], f[b]);
                    g[t + 1] = Math.Min(g[t + 1], f[b] + 1);
                }
                foreach (var sp in byEnd[t]) for (var b = 0; b <= p.T; b++) if (b - 1 < sp.From) g[b] += 1;
                f = g;
            }
            conflict += f.Min();
        }
        var days = 0;
        var proofDays = zeroCapBinding ? ConstraintMus.AnalyzeDayConflicts(p).Select(c => c.Day).ToList() : ConstraintMus.DayProofsWithoutZeroCap(p);
        foreach (var j in proofDays)
        {
            if (conflictDays.Contains(j)) continue;
            var forced = false;
            for (var k = 0; k < p.K && !forced; k++) forced = p.CovUCell(k, j, PlaceableFor(p, k, j)) > 0;
            if (forced) continue;
            days++;
        }
        return (conflict, days);
    }

    /// <summary>[E0] 希望どうしの c3w の件数（c3w がこの件数までなら、pref==0 のとき全て希望由来）。</summary>
    public static int WishConflictC3wCount(Problem p) => WishHardSpans(p).Count(s => s.Family == "c3w");

    /// <summary>[E0] 盤面の HARD が全て希望由来か（衝突の窓・セル・崩した希望＋日の証明の日数までの残り）。</summary>
    public static bool HardAllWishOrigin(Problem p, int[][] schedule, ViolationReport report, int dayProofs)
    {
        var w = WishConflictHard(p, schedule);
        if (w.GetValueOrDefault("c3n", 0) > report.Breakdown.GetValueOrDefault("c3n", 0)
            || w.GetValueOrDefault("c3w", 0) > report.Breakdown.GetValueOrDefault("c3w", 0)) return false;
        var rest = report.Hard - w.Values.Sum();
        return rest >= 0 && rest <= dayProofs;
    }

    public const string ZeroCapShortfallNote = "個人の上限0（入れない指定）が関係しています。見直すときは設定で変えてください";

    /// <summary>上限0を数えなければ（CanDo で数えると）不足が減る＝この配布不可に個人の上限0（入れない指定）が絡む。</summary>
    public static bool ZeroCapInShortfall(Problem p, ForcedCovU f)
    {
        var k = f.ShiftIndex; var sum = 0;
        for (var j = 0; j < p.T; j++)
        {
            var n = 0;
            for (var i = 0; i < p.S; i++) if (p.CanDo(i, k) || (p.WishFixed(i, j) && p.Wish[i][j] == k)) n++;
            sum += Math.Max(0, p.CovUCell(k, j, n));
        }
        return sum < f.Amount;
    }

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
        // [backlog#24] 休が無ければ「休の上限」という概念自体が無い＝0（呼び出し元は p.restIdx==k のときだけ呼ぶ）。
        if (p.RestIdx is not int k) return 0;
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
                var a = p.AptRaw[i][k];   // [Android 3.508.0] 設定した目標を検算する（実効目標 Apt は到達範囲へ丸め済み）
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
                var capKnown = true;
                for (var j = 0; j < p.T; j++)
                {
                    if (!NeedDefined(p, k, j)) { capKnown = false; continue; }
                    hasDemand = true;
                    seatsHi += Math.Max(EffectiveCap(p, k, j), 0);
                }
                if (!hasDemand) continue;
                // 未定義の日は「席0」でなく「上限なし」＝1日でも未定義なら seatsHi は上限として成立しない。
                if (!capKnown) continue;
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

    /// <summary>[Android 3.507.5 同期] その日にシフト k を実際に置ける人数＝最適化器が置ける（MayPlace）職員＋その日の希望でそのシフトに固定された職員。
    /// 3.507.0 で個人上限 0 の職員は最適化器が置かなくなったので、「担当できる人数」を CanDo で数えると人員不足の必然を見落とす。</summary>
    private static int PlaceableFor(Problem p, int k, int j)
    {
        var n = 0;
        for (var i = 0; i < p.S; i++) if (p.MayPlace(i, k) || (p.WishFixed(i, j) && p.Wish[i][j] == k)) n++;
        return n;
    }

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
