using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Schedule-related free functions ported from <c>MirrorCore.kt</c>. <see cref="RestShiftIndex"/>
/// and <see cref="FillShiftIndex"/> were ported ahead of the rest (phase 2), because
/// <see cref="Problem"/> itself depends on them directly; phase 3 (parity triangle) added the
/// remainder here rather than in a new file, per that earlier decision to consolidate.
///
/// Deliberately NOT ported here: <c>formatDay</c> (Japanese weekday display formatting — UI-facing,
/// unrelated to evaluation correctness; deferred to phase 7 alongside CSV/diagnostics).
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
}
