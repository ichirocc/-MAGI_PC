using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Schedule-related free functions ported from <c>MirrorCore.kt</c>. <see cref="RestShiftIndex"/>
/// and <see cref="FillShiftIndex"/> were ported ahead of the rest (phase 2), because
/// <see cref="Problem"/> itself depends on them directly; phase 3 (parity triangle) added the
/// remainder here rather than in a new file, per that earlier decision to consolidate.
/// [フェーズ7ピース1] <see cref="FormatDay"/> (Japanese weekday display formatting for CSV headers,
/// UI-facing, unrelated to evaluation correctness) added here.
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

    /// <summary>
    /// Whether staff <paramref name="staffI"/> can be assigned shift <paramref name="shiftK"/>
    /// (i.e. their group's <see cref="Problem.Bucket"/> allows it).
    /// </summary>
    public static bool CanDo(this Problem p, int staffI, int shiftK)
    {
        if (staffI < 0 || staffI >= p.S || shiftK < 0 || shiftK >= p.K) return false;
        int g = p.Sgrp[staffI];
        if (g < 0 || g >= p.Bucket.Length) return false;
        return Array.IndexOf(p.Bucket[g], shiftK) >= 0;
    }

    /// <summary>
    /// [監査#11①移植元] セル(i,j)の希望を「不可侵（凍結）」として扱うか。実現可能な希望のみ凍結する
    /// （担当不可の不可能希望は凍結しない＝セルを被覆等の最適化へ復帰させる）。
    /// </summary>
    public static bool WishLocked(this Problem p, int i, int j)
    {
        int w = p.Wish[i][j];
        return w >= 0 && p.CanDo(i, w);
    }

    /// <summary>The shift indices staff <paramref name="staffI"/> is allowed to take (their group's bucket, or empty).</summary>
    public static int[] AllowedShiftsForStaff(this Problem p, int staffI)
    {
        if (staffI < 0 || staffI >= p.Sgrp.Length) return Array.Empty<int>();
        int g = p.Sgrp[staffI];
        if (g < 0 || g >= p.Bucket.Length) return Array.Empty<int>();
        return p.Bucket[g];
    }

    /// <summary>
    /// Pads/truncates <paramref name="schedule"/> to exactly S×T, mapping any missing cell or any
    /// value outside [0,K) to the -1 sentinel ("out of range" / unassigned).
    /// </summary>
    public static int[][] NormalizeSchedule(int[][] schedule, Problem p)
    {
        var result = new int[p.S][];
        for (int i = 0; i < p.S; i++)
        {
            var row = new int[p.T];
            var srcRow = i < schedule.Length ? schedule[i] : null;
            for (int j = 0; j < p.T; j++)
            {
                int k = (srcRow is not null && j < srcRow.Length) ? srcRow[j] : 0;
                row[j] = (k >= 0 && k < p.K) ? k : -1;
            }
            result[i] = row;
        }
        return result;
    }

    /// <summary>
    /// [統一weekly 移植元] 回数 c を7曜日へどう配っても消せない weekly 偏差の下限
    /// （|c − 7*round(c/7)|、曜日ごとの日数上限は考慮しないので真の下限以下）。
    /// </summary>
    public static int WeeklyFloorOfCount(int c)
    {
        if (c <= 0) return 0;
        int tgt = (int)KotlinInterop.MathRound(c / 7.0);
        return Math.Abs(c - 7 * tgt);
    }

    /// <summary>
    /// [統一weekly 移植元] 曜日バケット(size 7)の平準化偏差 = round(平均) からの L1 偏差和。
    /// <see cref="ViolationChecker"/> / <see cref="Evaluator"/> / <see cref="DeltaEvaluator"/> の
    /// "weekly" 共通ソース（3面のドリフト防止）。
    /// </summary>
    public static int WeeklyDevOfBucket(int[] wd)
    {
        int sum = 0;
        foreach (var w in wd) sum += w;
        int tgt = (int)KotlinInterop.MathRound(sum / 7.0);
        int d = 0;
        foreach (var w in wd) d += Math.Abs(w - tgt);
        return d;
    }

    public static int[][] CountMatrix(Problem p, int[][] schedule)
    {
        var result = new int[p.S][];
        for (int i = 0; i < p.S; i++) result[i] = new int[p.K];
        for (int i = 0; i < p.S; i++)
            for (int j = 0; j < p.T; j++)
            {
                int k = schedule[i][j];
                if (k >= 0 && k < p.K) result[i][k]++;
            }
        return result;
    }

    public static int[][] Coverage(Problem p, int[][] schedule)
    {
        var result = new int[p.T][];
        for (int j = 0; j < p.T; j++) result[j] = new int[p.K];
        for (int i = 0; i < p.S; i++)
            for (int j = 0; j < p.T; j++)
            {
                int k = schedule[i][j];
                if (k >= 0 && k < p.K) result[j][k]++;
            }
        return result;
    }

    /// <summary>
    /// [移植元 ProblemCache/cachedProblem] 同一 <see cref="MagiState"/> 参照に対する
    /// <see cref="Problem"/> の単一エントリ・メモ化。<see cref="MagiState"/> は record（構造等価）
    /// だが、ここは Kotlin の <c>===</c>（参照同一性）を忠実に再現するため
    /// <see cref="object.ReferenceEquals(object?, object?)"/> で比較する（<c>==</c> は使わない —
    /// record が生成する構造等価の <c>Equals</c> を呼んでしまい、別内容の state を誤って
    /// 「同一」と判定しかねない）。
    ///
    /// スレッド安全性: key と value を1つの不変 <see cref="Entry"/> にまとめ、volatile 参照を1回
    /// だけ読む（Kotlin側コメントが警告する「key/valueを別々のVolatileに持つと、別スレッドが
    /// 新しいkeyだが古いvalueを読みうる」レースを構造的に防ぐ）。
    /// </summary>
    private static class ProblemCache
    {
        private sealed class Entry
        {
            public readonly MagiState Key;
            public readonly Problem Value;
            public Entry(MagiState key, Problem value) { Key = key; Value = value; }
        }

        private static volatile Entry? _entry;

        public static Problem Get(MagiState state)
        {
            var e = _entry;
            if (e is not null && ReferenceEquals(e.Key, state)) return e.Value;
            var np = new Problem(state);
            _entry = new Entry(state, np); // 単一参照の公開はアトミック。race時の重複生成は等価で無害。
            return np;
        }
    }

    public static Problem CachedProblem(MagiState state) => ProblemCache.Get(state);

    /// <summary>Converts <see cref="MagiState.Schedule"/>'s jagged read-only lists to a mutable jagged array.</summary>
    public static int[][] ToIntArray2D(this IReadOnlyList<IReadOnlyList<int>> rows)
    {
        var result = new int[rows.Count][];
        for (int i = 0; i < rows.Count; i++) result[i] = rows[i].ToArray();
        return result;
    }

    /// <summary>Deep-copies a jagged int array (each row cloned independently).</summary>
    public static int[][] Copy2D(this int[][] a)
    {
        var result = new int[a.Length][];
        for (int i = 0; i < a.Length; i++) result[i] = (int[])a[i].Clone();
        return result;
    }

    /// <summary>Returns a copy of <paramref name="state"/> with its schedule replaced.</summary>
    public static MagiState WithSchedule(this MagiState state, int[][] schedule)
    {
        var rows = new List<IReadOnlyList<int>>(schedule.Length);
        foreach (var row in schedule) rows.Add(row.ToList());
        return state with { Schedule = rows };
    }

    /// <summary>
    /// [フェーズ7ピース1] Faithful port of <c>MirrorCore.kt</c>'s <c>formatDay</c> — the day header
    /// used by CSV export (<c>ScheduleCsvBridge.kt:333</c>): <paramref name="startDate"/> + offset
    /// days, formatted as <c>"M/d(曜)"</c>, falling back to <c>"(offset+1)日"</c> on any failure.
    ///
    /// Kotlin's implementation parses via <c>SimpleDateFormat("yyyy-MM-dd")</c> +
    /// <c>java.util.Calendar</c> (NOT the strict <c>java.time.LocalDate.parse</c> used by the
    /// already-ported sibling <see cref="Problem.Dow0"/> for this SAME <c>state.StartDate</c>
    /// field — a genuine, deliberate divergence in the Kotlin source between these two functions,
    /// preserved rather than "fixed" per HF77). This is meaningfully more lenient than a strict
    /// exact-format parse, confirmed empirically against a real Kotlin runtime across ~24 malformed/
    /// edge-case inputs (see phase-7 verification notes) in three independent dimensions:
    ///
    ///  1. Numeric field widths are NOT fixed — "2026-6-1" (unpadded month/day) parses the same as
    ///     "2026-06-01". (The literal '-' separators are NOT lenient, though: "2026/06/01" fails to
    ///     parse in real Kotlin and hits the fallback branch — confirmed empirically.)
    ///  2. <c>DateFormat.parse(String)</c> only requires SOME progress from position 0, not a full
    ///     match — trailing content after the day field is silently ignored ("2026-06-01T00:00:00",
    ///     "2026-06-01T12:34:56.789Z", "2026-06-01x" all parse to June 1, 2026, ignoring everything
    ///     after the day field — confirmed empirically, including the fractional-seconds+'Z' form).
    ///  3. Out-of-range numeric field values roll over via Calendar's field-carry arithmetic rather
    ///     than failing to parse — e.g. month=13 carries into January of the next year, day=0
    ///     carries into the last day of the previous month (confirmed for all four directions:
    ///     month overflow/underflow, day overflow/underflow).
    ///
    /// Rather than hand-reimplementing Calendar's own field-normalization algorithm, dimension 3
    /// is reproduced by leaning on .NET's own correct <see cref="DateOnly.AddMonths"/>/
    /// <see cref="DateOnly.AddDays"/> carry arithmetic: constructing <c>new DateOnly(year, 1, 1)
    /// .AddMonths(month - 1).AddDays(day - 1)</c> is exactly equivalent to <c>new
    /// DateOnly(year, month, day)</c> for valid in-range values, and naturally degrades to the same
    /// carry semantics as Calendar for out-of-range ones (verified to reproduce all four rollover
    /// cases exactly).
    ///
    /// [Accepted, documented gaps — deliberately NOT replicated, both confirmed empirically and
    /// both firmly outside anything this app's own date picker could ever produce]
    ///  (a) Years before Java's Gregorian-calendar cutover (1582-10-15): <c>GregorianCalendar</c>
    ///      switches to JULIAN calendar day-of-week arithmetic for these ("26-06-01" -&gt; year 26
    ///      AD -&gt; real Kotlin shows Saturday), whereas <see cref="DateOnly"/> is always proleptic
    ///      GREGORIAN regardless of how far back the date goes (this port shows Monday for the same
    ///      input) — the two calendar systems' leap-year rules diverge over the centuries.
    ///  (b) Year 0000 (or, through <see cref="TryParseLenientYmd"/>'s unsigned-digit-run reader,
    ///      any input literally spelling a zero year field): Java's <c>Calendar.YEAR</c> field is
    ///      always non-negative, with a separate BC/AD <c>ERA</c> field — year 0 input becomes
    ///      "1 BC" and a real date IS produced. <see cref="DateOnly"/>'s valid range is 1-9999
    ///      (no BC/proleptic-negative-year support), so this throws and falls to the fallback
    ///      branch instead of a rolled date.
    /// </summary>
    public static string FormatDay(string startDate, int offset)
    {
        try
        {
            if (!TryParseLenientYmd(startDate, out var y, out var m, out var d))
                return $"{offset + 1}日";

            var date = new DateOnly(y, 1, 1).AddMonths(m - 1).AddDays(d - 1).AddDays(offset);
            char weekday = "日月火水木金土"[(int)date.DayOfWeek]; // Sunday=0..Saturday=6 in both languages
            return $"{date.Month}/{date.Day}({weekday})";
        }
        catch (Exception)
        {
            return $"{offset + 1}日";
        }
    }

    /// <summary>
    /// The lenient y/m/d tokenizer backing <see cref="FormatDay"/> (see its KDoc for the exact
    /// leniency dimensions replicated). Skips leading whitespace before each numeric field (matches
    /// an empirically-observed Java <c>NumberFormat</c>-derived leniency of <c>SimpleDateFormat</c>
    /// itself, not a general string-trim), reads a run of ASCII digits of any width for each of
    /// year/month/day, requires a literal '-' between each, and does NOT require consuming the rest
    /// of the string after the day field.
    /// </summary>
    private static bool TryParseLenientYmd(string s, out int year, out int month, out int day)
    {
        year = month = day = 0;
        int idx = 0;
        int n = s.Length;

        bool TryReadDigits(out int value)
        {
            while (idx < n && char.IsWhiteSpace(s[idx])) idx++;
            int start = idx;
            while (idx < n && s[idx] >= '0' && s[idx] <= '9') idx++;
            if (idx == start) { value = 0; return false; }
            value = int.Parse(s.AsSpan(start, idx - start), CultureInfo.InvariantCulture);
            return true;
        }

        if (!TryReadDigits(out year)) return false;
        if (idx >= n || s[idx] != '-') return false;
        idx++;
        if (!TryReadDigits(out month)) return false;
        if (idx >= n || s[idx] != '-') return false;
        idx++;
        return TryReadDigits(out day);
    }
}
