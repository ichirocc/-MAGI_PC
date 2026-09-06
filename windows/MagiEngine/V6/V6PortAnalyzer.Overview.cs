using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース9] Faithful port of Kotlin's <c>V6DayRisk</c> data class
/// (<c>V6PortAnalyzer.kt:11-16</c>).
/// </summary>
public sealed record V6DayRisk(int DayIndex, string Label, int Shortage, string Detail);

/// <summary>
/// [フェーズ7ピース9] Faithful port of Kotlin's <c>V6StaffProfile</c> data class
/// (<c>V6PortAnalyzer.kt:18-25</c>).
/// </summary>
public sealed record V6StaffProfile(
    int StaffIndex,
    string Name,
    string GroupSymbol,
    int WorkCount,
    int ViolationCount,
    string WorkloadText);

/// <summary>
/// [フェーズ7ピース9] Faithful port of Kotlin's <c>V6PortReport</c> data class
/// (<c>V6PortAnalyzer.kt:27-46</c>) — the return type of <see cref="V6PortAnalyzer.Analyze"/>.
/// </summary>
public sealed record V6PortReport(
    int? CoveragePct,
    int Demand,
    int CovU,
    int HardCore,
    int HardGuard,
    int SoftCore,
    int TopRiskDay,
    string TopRiskLabel,
    int TopRiskShortage,
    IReadOnlyList<V6DayRisk> DayRisks,
    IReadOnlyList<V6StaffProfile> StaffProfiles,
    double AptPenalty,
    double EquPenalty,
    IReadOnlyList<string> SanityWarnings,
    IReadOnlyList<string> SanityNotes)
{
    /// <summary>Days that still have a coverage shortage (referenced by the V6 RISK gauge).</summary>
    public int HighRiskDays => DayRisks.Count(r => r.Shortage > 0);
}

public static partial class V6PortAnalyzer
{
    /// <summary>
    /// [フェーズ7ピース9] Faithful port of Kotlin's <c>V6PortAnalyzer.analyze(...)</c>
    /// (<c>V6PortAnalyzer.kt:635-684</c>) — the "V6 Web overview / risk / load analysis layer",
    /// UI向け診断（勤務表・エンジンには一切影響しない read-only 集計）。
    ///
    /// [デフォルト引数の解決順] Kotlin原本は <c>report</c> の既定式が同じシグネチャの
    /// （既に解決済みの）<c>schedule</c> パラメータを参照する（呼び出し側が <c>schedule</c> だけ
    /// 明示して <c>report</c> を省略した場合、その明示された <c>schedule</c> に対する検査結果が
    /// デフォルトになる。呼び出しごとに再度 <c>toIntArray2D()</c> し直した別物ではない）。
    /// C#は他パラメータを参照するデフォルト式を許さないため、<see cref="V6PortAnalyzer.DiagnoseCoverage"/>
    /// （<c>V6PortAnalyzer.Coverage.cs</c>）で確立した規約と同じ形——両パラメータを nullable にして
    /// 既定 <c>null</c> とし、本体冒頭で <c>schedule ?? 既定式</c>→<c>report ?? (その sched を使う既定式)</c>
    /// の順に逐次解決する——で同じ意味論を再現する。
    /// </summary>
    public static V6PortReport Analyze(
        MagiState state,
        int[][]? schedule = null,
        ViolationReport? report = null)
    {
        var sched = schedule ?? state.Schedule.ToIntArray2D();
        var rep = report ?? UnifiedViolationChecker.Check(state, sched);
        var p = ScheduleUtil.CachedProblem(state);
        var normalized = ScheduleUtil.NormalizeSchedule(sched, p);
        var cov = ScheduleUtil.Coverage(p, normalized);
        var counts = ScheduleUtil.CountMatrix(p, normalized);
        var dayRisks = BuildDayRisks(state, p, cov);
        var demand = TotalDemand(p);
        var covU = rep.Breakdown.GetValueOrDefault("covU", 0);
        // Kotlin: (((demand - covU).coerceAtLeast(0).toDouble() / demand.toDouble()) * 100.0).roundToInt()
        int? coveragePct = demand > 0
            ? (int)KotlinInterop.MathRound(Math.Max(0, demand - covU) / (double)demand * 100.0)
            : null;

        var topRiskDay = -1;
        var topRiskLabel = "-";
        var topRiskShortage = 0;
        foreach (var risk in dayRisks)
        {
            if (risk.Shortage > topRiskShortage)
            {
                topRiskDay = risk.DayIndex;
                topRiskLabel = risk.Label;
                topRiskShortage = risk.Shortage;
            }
        }

        var hardGuard = rep.Breakdown.GetValueOrDefault("groupViol", 0);
        var hardCore = rep.Breakdown.GetValueOrDefault("c3n", 0)
            + rep.Breakdown.GetValueOrDefault("covU", 0)
            + rep.Breakdown.GetValueOrDefault("pref", 0);
        var softCore = Math.Max(0, rep.Total - hardGuard - hardCore);
        var staffViol = StaffViolationCounts(p, rep);

        return new V6PortReport(
            CoveragePct: coveragePct,
            Demand: demand,
            CovU: covU,
            HardCore: hardCore,
            HardGuard: hardGuard,
            SoftCore: softCore,
            TopRiskDay: topRiskDay,
            TopRiskLabel: topRiskLabel,
            TopRiskShortage: topRiskShortage,
            DayRisks: dayRisks,
            StaffProfiles: BuildStaffProfiles(state, p, normalized, counts, staffViol),
            AptPenalty: AptPenalty(state, p, counts),
            EquPenalty: EqualizationPenalty(state, p, normalized, counts, rep.Breakdown),
            SanityWarnings: SanityWarnings(state, p, normalized),
            SanityNotes: SanityNotes(state));
    }

    /// <summary>Faithful port of Kotlin's <c>private fun totalDemand(p: Problem): Int</c>.</summary>
    private static int TotalDemand(Problem p)
    {
        var total = 0;
        for (var j = 0; j < p.T; j++)
            for (var k = 0; k < p.K; k++)
                // [3.379.0 移植元] need2 単独定義の需要も数える（covUCell(k,j,0) = 誰も置かないときの
                //   不足＝実効需要）。
                total += p.CovUCell(k, j, 0);
        return total;
    }

    /// <summary>Faithful port of Kotlin's <c>private fun buildDayRisks(...)</c>.</summary>
    private static IReadOnlyList<V6DayRisk> BuildDayRisks(MagiState state, Problem p, int[][] cov)
    {
        var result = new List<V6DayRisk>(p.T);
        for (var j = 0; j < p.T; j++)
        {
            var shortfall = 0;
            var parts = new List<string>();
            for (var k = 0; k < p.K; k++)
            {
                // [3.379.0 移植元] 同上。need1 だけ見ると need2 単独定義シフトの不足が日別リスクに
                //   出なかった。
                var miss = p.CovUCell(k, j, cov[j][k]);
                var need = p.CovUCell(k, j, 0);
                if (need <= 0) continue;
                shortfall += miss;
                if (miss > 0)
                {
                    var sym = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
                    parts.Add($"{sym}×{miss}");
                }
            }
            result.Add(new V6DayRisk(j, DayLabel(state.StartDate, j), shortfall, string.Join(" ", parts)));
        }
        return result;
    }

    /// <summary>Faithful port of Kotlin's <c>private fun staffViolationCounts(...)</c>.</summary>
    private static int[] StaffViolationCounts(Problem p, ViolationReport report)
    {
        var result = new int[p.S];
        void AddCellKey(string key)
        {
            var i = KotlinInterop.ToIntOrNull(key.Split(',')[0]);
            if (i is null) return;
            if (i.Value >= 0 && i.Value < p.S) result[i.Value]++;
        }
        foreach (var key in report.Violations.Keys) AddCellKey(key);
        foreach (var key in report.CountViolations.Keys) AddCellKey(key);
        return result;
    }

    /// <summary>Faithful port of Kotlin's <c>private fun buildStaffProfiles(...)</c>.</summary>
    private static IReadOnlyList<V6StaffProfile> BuildStaffProfiles(
        MagiState state,
        Problem p,
        int[][] schedule,
        int[][] counts,
        int[] staffViol)
    {
        // [監査(未レビュー領域再監査) 実バグ修正 移植元] 休記号改名時に rest=-1 となり
        //   「schedule!=-1」が常に真＝全職員を全日勤務と誤カウントしていた
        //   （3.103.0でweeklyに適用済みの p.restIdx フォールバックへ統一）。
        var rest = p.RestIdx;
        var profiles = new List<V6StaffProfile>(p.S);
        for (var i = 0; i < p.S; i++)
        {
            var work = 0;
            for (var j = 0; j < p.T; j++) if (schedule[i][j] != rest) work++;

            var pairs = new List<(int Shift, int Count)>();
            for (var k = 0; k < p.K; k++)
            {
                var n = counts[i][k];
                if (n > 0) pairs.Add((k, n));
            }
            // Kotlin's sortByDescending is stable; List<T>.Sort() is not — use OrderByDescending.
            pairs = pairs.OrderByDescending(pair => pair.Count).ToList();
            var parts = new List<string>();
            var limit = Math.Min(3, pairs.Count);
            for (var idx = 0; idx < limit; idx++)
            {
                var k = pairs[idx].Shift;
                var n = pairs[idx].Count;
                var sym = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
                parts.Add($"{sym}:{n}");
            }
            var staff = i >= 0 && i < state.StaffList.Count ? state.StaffList[i] : null;
            var g = staff?.GroupIdx ?? -1;
            profiles.Add(new V6StaffProfile(
                StaffIndex: i,
                Name: staff?.Name ?? $"#{i}",
                GroupSymbol: g >= 0 && g < state.Groups.Count ? state.Groups[g].Kigou : "",
                WorkCount: work,
                ViolationCount: i >= 0 && i < staffViol.Length ? staffViol[i] : 0,
                WorkloadText: string.Join(" / ", parts)));
        }
        // Kotlin's sortWith(compareByDescending{}.thenByDescending{}) is stable — OrderByDescending.ThenByDescending.
        profiles = profiles.OrderByDescending(x => x.ViolationCount).ThenByDescending(x => x.WorkCount).ToList();
        return profiles;
    }

    /// <summary>
    /// V6 CountApt port: sum((count - apt)^2 / apt^2), group aptitude expanded to staff.
    /// Faithful port of Kotlin's <c>private fun aptPenalty(...)</c>.
    /// </summary>
    private static double AptPenalty(MagiState state, Problem p, int[][] counts)
    {
        var total = 0.0;
        for (var i = 0; i < p.S; i++)
        {
            if (i >= p.Sgrp.Length) continue;
            var g = p.Sgrp[i];
            if (g < 0 || g >= state.GroupShiftApt.Count) continue;
            var row = state.GroupShiftApt[g];
            for (var k = 0; k < p.K; k++)
            {
                if (k < 0 || k >= row.Count) continue;
                var apt = KotlinInterop.ToDoubleOrNull(row[k].Trim());
                if (apt is null) continue;
                if (apt.Value <= 0.0) continue;
                var d = counts[i][k] - apt.Value;
                total += (d * d) / (apt.Value * apt.Value);
            }
        }
        return total;
    }

    /// <summary>
    /// Compact V6 equalization overview: member variance + day-of-week variance with HARD-aware
    /// psi. Faithful port of Kotlin's <c>private fun equalizationPenalty(...)</c>.
    /// </summary>
    private static double EqualizationPenalty(
        MagiState state,
        Problem p,
        int[][] schedule,
        int[][] counts,
        IReadOnlyDictionary<string, int> breakdown)
    {
        if (p.S == 0 || p.T == 0 || p.K == 0 || p.G == 0) return 0.0;
        var members = new List<int>[p.G];
        for (var g = 0; g < p.G; g++) members[g] = new List<int>();
        for (var i = 0; i < p.S; i++)
        {
            var g = p.Sgrp[i];
            if (g >= 0 && g < p.G) members[g].Add(i);
        }

        var raw = 0.0;
        for (var g = 0; g < p.G; g++)
        {
            if (g >= state.GroupShift.Count) continue;
            var gs = state.GroupShift[g];
            var mem = members[g];
            if (mem.Count == 0) continue;
            for (var k = 0; k < p.K; k++)
            {
                var gsK = k < gs.Count ? gs[k] : (int?)null;
                if (gsK != 1) continue;
                if (mem.Count == 1)
                {
                    var i = mem[0];
                    var explicitTgt = ExplicitTarget(p, i, k);
                    double? apt = g < state.GroupShiftApt.Count && k < state.GroupShiftApt[g].Count
                        ? KotlinInterop.ToDoubleOrNull(state.GroupShiftApt[g][k].Trim())
                        : null;
                    var target = explicitTgt ?? apt;
                    if (target is null) continue;
                    var dev = counts[i][k] - target.Value;
                    raw += dev * dev + Math.Abs(dev) * 2.0;
                }
                else
                {
                    raw += SpreadTerm(mem, i => counts[i][k]);
                }
            }
        }

        var startDow = StartDow(state.StartDate);
        var dowCnt = new int[p.S][][];
        for (var i = 0; i < p.S; i++)
        {
            dowCnt[i] = new int[7][];
            for (var d = 0; d < 7; d++) dowCnt[i][d] = new int[p.K];
        }
        for (var i = 0; i < p.S; i++)
        {
            for (var j = 0; j < p.T; j++)
            {
                var k = schedule[i][j];
                if (k >= 0 && k < p.K) dowCnt[i][(startDow + j) % 7][k]++;
            }
        }
        for (var g = 0; g < p.G; g++)
        {
            if (g >= state.GroupShift.Count) continue;
            var gs = state.GroupShift[g];
            var mem = members[g];
            if (mem.Count <= 1) continue;
            for (var dow = 0; dow < 7; dow++)
            {
                for (var k = 0; k < p.K; k++)
                {
                    var gsK = k < gs.Count ? gs[k] : (int?)null;
                    if (gsK != 1) continue;
                    raw += SpreadTerm(mem, i => dowCnt[i][dow][k]);
                }
            }
        }
        var hard = breakdown.GetValueOrDefault("groupViol", 0) + breakdown.GetValueOrDefault("c3n", 0)
            + breakdown.GetValueOrDefault("covU", 0) + breakdown.GetValueOrDefault("pref", 0);
        var psi = Math.Max(0.2, 1.0 / (1.0 + 10.0 * hard));
        return raw * psi;
    }

    /// <summary>Faithful port of Kotlin's <c>private fun explicitTarget(...)</c>.</summary>
    private static double? ExplicitTarget(Problem p, int i, int k)
    {
        var loSet = p.RangeLo[i][k] != int.MinValue;
        var hiSet = p.RangeHi[i][k] != int.MaxValue;
        if (loSet && hiSet) return (p.RangeLo[i][k] + p.RangeHi[i][k]) / 2.0;
        if (loSet) return p.RangeLo[i][k];
        if (hiSet) return p.RangeHi[i][k];
        return null;
    }

    /// <summary>Faithful port of Kotlin's <c>private fun sanityWarnings(...)</c>.</summary>
    /// <summary>群メンバー間の散らばり: 平均からの二乗偏差和＋最大絶対偏差×2（V6 equalization の項。[Android 3.503.0] 2 か所の複製を統合）。</summary>
    private static double SpreadTerm(List<int> mem, Func<int, int> value)
    {
        var sum = 0;
        foreach (var i in mem) sum += value(i);
        var mean = sum / (double)mem.Count;
        var varSum = 0.0;
        var maxDev = 0.0;
        foreach (var i in mem)
        {
            var d = value(i) - mean;
            varSum += d * d;
            maxDev = Math.Max(maxDev, Math.Abs(d));
        }
        return varSum + maxDev * 2.0;
    }

    /// <summary>"i,j" 形式のキーを 2 つの int? に分解する（欠け・非数は null。[Android 3.503.0] wishes/staffRange の重複を統合）。</summary>
    private static (int? A, int? B) ParseKeyPair(string key)
    {
        var parts = key.Split(',');
        return (parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null, parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null);
    }

    private static IReadOnlyList<string> SanityWarnings(MagiState state, Problem p, int[][] schedule)
    {
        var warns = new List<string>();
        var badAssign = 0;
        for (var i = 0; i < p.S; i++)
        {
            for (var j = 0; j < p.T; j++)
            {
                var k = schedule[i][j];
                if (k >= 0 && k < p.K && !p.CanDo(i, k)) badAssign++;
            }
        }
        if (badAssign > 0) warns.Add($"担当不可の配置が {badAssign} セルあります");

        var badWish = 0;
        foreach (var (key, k) in state.Wishes)
        {
            var (i, j) = ParseKeyPair(key);
            if (i is null || j is null || i.Value < 0 || i.Value >= p.S
                || j.Value < 0 || j.Value >= p.T || k < 0 || k >= p.K)
            {
                badWish++;
            }
            else if (!p.CanDo(i.Value, k))
            {
                badWish++;
            }
        }
        if (badWish > 0) warns.Add($"範囲外または担当外の希望シフトが {badWish} 件あります");

        var badRange = 0;
        foreach (var (key, r) in state.StaffRange)
        {
            var (i, k) = ParseKeyPair(key);
            var lo = KotlinInterop.ToIntOrNull(r.Lo.Trim());
            var hi = KotlinInterop.ToIntOrNull(r.Hi.Trim());
            if (i is null || k is null || i.Value < 0 || i.Value >= p.S || k.Value < 0 || k.Value >= p.K) badRange++;
            if (lo is not null && hi is not null && lo.Value > hi.Value) badRange++;
        }
        if (badRange > 0) warns.Add($"staffRange の範囲外キーまたは lo>hi が {badRange} 件あります");

        var dup = DuplicatePatternCount(state);
        if (dup > 0) warns.Add($"連続パターン制約の重複定義が {dup} 件あります");
        if (state.Cons41.Count == 0) warns.Add("cons41 が未設定です（グループ別人数範囲を使う場合は確認）");
        return warns;
    }

    /// <summary>Faithful port of Kotlin's <c>private fun sanityNotes(...)</c>.</summary>
    private static IReadOnlyList<string> SanityNotes(MagiState state)
    {
        var notes = new List<string>();
        var aptSet = 0;
        foreach (var row in state.GroupShiftApt)
            foreach (var cell in row)
                if (cell.Trim().Length > 0) aptSet++;
        notes.Add($"groupShiftApt 適切回数: {aptSet} 件設定");
        if (state.NeedDay1.Count == 0 && state.NeedDay2.Count == 0)
            notes.Add("needDay1/needDay2 は全空です（shift既定 need を使用）");
        notes.Add("V6 Native overview: リスクカレンダー / 負荷プロフィール / SanityCheck 有効");
        return notes;
    }

    /// <summary>Faithful port of Kotlin's <c>private fun duplicatePatternCount(...)</c>.</summary>
    private static int DuplicatePatternCount(MagiState state) =>
        CountDuplicatePatternRows(state.Cons3) +
        CountDuplicatePatternRows(state.Cons3n) +
        CountDuplicatePatternRows(state.Cons3m) +
        CountDuplicatePatternRows(state.Cons3mn);

    /// <summary>Faithful port of Kotlin's <c>private fun countDuplicatePatternRows(...)</c>.</summary>
    private static int CountDuplicatePatternRows(IReadOnlyList<C3Row> rows)
    {
        var seen = new HashSet<string>();
        var dup = 0;
        foreach (var row in rows)
        {
            var symbols = new List<string>();
            foreach (var s in row.Pattern)
            {
                if (string.IsNullOrWhiteSpace(s)) break;
                symbols.Add(s);
            }
            var key = string.Join("→", symbols);
            if (string.IsNullOrWhiteSpace(key)) continue;
            // Kotlin's MutableSet.add returns true when newly added (false when already present);
            // C#'s HashSet<T>.Add has the identical return contract, so !seen.Add(key) matches
            // !seen.add(key) exactly (true only when key was already present = a duplicate).
            if (!seen.Add(key)) dup++;
        }
        return dup;
    }

    /// <summary>
    /// Faithful port of Kotlin's file-scope <c>private fun startDow(startDate: String): Int</c>
    /// (<c>V6PortAnalyzer.kt:967-973</c>, used only by <see cref="EqualizationPenalty"/>).
    ///
    /// Kotlin computes <c>LocalDate.parse(startDate).dayOfWeek.value % 7</c>. java.time's
    /// <c>DayOfWeek.value</c> is ISO-8601 (Monday=1 .. Sunday=7), so <c>% 7</c> maps it to
    /// Sunday=0, Monday=1, ..., Saturday=6 — which is exactly .NET's <see cref="DayOfWeek"/>
    /// enum's own numeric values (Sunday=0 .. Saturday=6). No offset adjustment is needed, unlike
    /// <see cref="DayLabel"/>/<see cref="V6SanityPort.SafeDayLabel"/>'s <c>((int)d.DayOfWeek + 6) % 7</c>
    /// (which maps to a Monday=0-based Japanese-weekday-string index instead).
    /// </summary>
    private static int StartDow(string startDate)
    {
        try
        {
            if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                throw new FormatException($"'{startDate}' is not a valid yyyy-MM-dd date");
            return (int)parsed.DayOfWeek;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
