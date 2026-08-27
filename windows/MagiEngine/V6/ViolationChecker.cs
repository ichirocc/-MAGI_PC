using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>Faithful port of Kotlin's <c>MirrorLog</c> data class.</summary>
public sealed record MirrorLog
{
    public long Ts { get; init; }
    public long Iter { get; init; }
    public string Level { get; init; }
    public string Tag { get; init; }
    public string Message { get; init; }

    public MirrorLog(string tag, string message, long iter = 0, string level = "I", long? ts = null)
    {
        Tag = tag;
        Message = message;
        Iter = iter;
        Level = level;
        Ts = ts ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

/// <summary>
/// Faithful port of Kotlin's <c>ViolationReport</c> data class — the return type of
/// <see cref="UnifiedViolationChecker.Check"/>.
///
/// Parameter order here (required fields first, then the ones with a default value) differs
/// from the Kotlin declaration's textual order (which freely interleaves defaulted and
/// non-defaulted parameters, something Kotlin allows but C# does not for a single constructor
/// signature) — every field's name, type, and default value is otherwise identical; this is a
/// pure declaration-order accommodation, not a behavioral change. The one production call site
/// (<see cref="UnifiedViolationChecker.Check"/>) supplies every field explicitly regardless.
/// </summary>
public sealed record ViolationReport(
    IReadOnlyDictionary<string, string> Violations,
    IReadOnlyDictionary<string, string> NeedViolations,
    IReadOnlyDictionary<string, string> CountViolations,
    IReadOnlyDictionary<string, int> Breakdown,
    int Total,
    int Hard,
    int Soft,
    double WeightedScore,
    // [Set化 移植元] セル("i,j")に重なった全違反クラスを重み降順で保持（Violations は最重1クラス＝
    //   後方互換）。先頭は常に Violations[key] と一致する（不変条件）。
    IReadOnlyDictionary<string, IReadOnlyList<string>>? CellFamilies = null,
    // [3.353.0 移植元] 回数キー("i,k")版。
    IReadOnlyDictionary<string, IReadOnlyList<string>>? CountFamilies = null,
    // [/code-review 移植元] 被覆キー("k,j")版。
    IReadOnlyDictionary<string, IReadOnlyList<string>>? NeedFamilies = null,
    // [場所表示 移植元] fair/weekly はセル単位でなく職員/群×シフト単位の偏りのため violations(mark)
    //   に出せない。"weekly" -> [[staffIdx, dev], ...] / "fair" -> [[staffIdx, shiftIdx, dev], ...]（dev降順）。
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>>? DistLocations = null,
    IReadOnlyList<MirrorLog>? Logs = null)
{
    // Kotlin's emptyMap()/emptyList() defaults, realized as non-null accessors (records can't
    // default a reference-typed positional parameter to a *shared* non-null instance without
    // this null-coalescing indirection, since default parameter values must be compile-time
    // constants in C#).
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CellFamilies { get; init; } =
        CellFamilies ?? EmptyFamilies;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> CountFamilies { get; init; } =
        CountFamilies ?? EmptyFamilies;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> NeedFamilies { get; init; } =
        NeedFamilies ?? EmptyFamilies;
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>> DistLocations { get; init; } =
        DistLocations ?? EmptyLocations;
    public IReadOnlyList<MirrorLog> Logs { get; init; } = Logs ?? Array.Empty<MirrorLog>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyFamilies =
        new Dictionary<string, IReadOnlyList<string>>();
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>> EmptyLocations =
        new Dictionary<string, IReadOnlyList<IReadOnlyList<int>>>();
}

/// <summary>Faithful port of Kotlin's <c>UnifiedViolationChecker</c> object.</summary>
public static class UnifiedViolationChecker
{
    // [3.395.0/高速化 移植元] mark 系の重み優先比較のための事前表。VioClass を先に確定させてから
    // ClassWeight を導出する（Kotlin の `by lazy` は宣言順ではなく初回アクセス順で安全だったが、
    // C# の静的フィールド初期化は宣言順で確定的に走るため lazy にする必要が無い）。
    private static readonly Dictionary<string, string> VioClass = new()
    {
        ["c1"] = "vio-c1", ["c2"] = "vio-c2", ["c3"] = "vio-c3", ["c3n"] = "vio-c3n",
        ["c3m"] = "vio-c3m", ["c3mn"] = "vio-c3mn", ["c41"] = "vio-c41", ["c42"] = "vio-c42",
        ["c41s"] = "vio-c41s", ["c42s"] = "vio-c42s",
        ["covU"] = "vio-covU", ["covO"] = "vio-covO", ["pref"] = "vio-pref",
        ["low"] = "vio-low", ["high"] = "vio-high", ["groupViol"] = "vio-groupViol",
        ["aptLow"] = "vio-aptLow", ["aptHigh"] = "vio-aptHigh",
    };

    private static readonly IReadOnlyDictionary<string, double> ClassWeight =
        VioClass.ToDictionary(kv => kv.Value, kv => MirrorKeys.WeightOf(kv.Key));

    /// <summary>
    /// [3.287.0 keep-best統一 移植元] 全 keep-best 比較器の単一ソース。順序は
    /// hard → weightedScore → total。比較を足すときは写さずこれを使う。
    /// </summary>
    public static readonly IComparer<ViolationReport> ReportComparer = Comparer<ViolationReport>.Create((a, b) =>
    {
        if (a.Hard != b.Hard) return a.Hard.CompareTo(b.Hard);
        if (a.WeightedScore != b.WeightedScore) return a.WeightedScore.CompareTo(b.WeightedScore);
        return a.Total.CompareTo(b.Total);
    });

    public static bool BetterReport(ViolationReport a, ViolationReport b) => ReportComparer.Compare(a, b) < 0;

    public static ViolationReport Check(MagiState state, int[][]? schedule = null)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var p = ScheduleUtil.CachedProblem(state);
        var s = ScheduleUtil.NormalizeSchedule(schedule ?? state.Schedule.ToIntArray2D(), p);

        // [3.395.0/高速化 移植元] 集計は添字加算の int[] で行い、最後に MirrorKeys.All の順で
        // Dictionary へ起こす（内容も順序も従来と同じ）。
        var bd = new int[MirrorKeys.All.Count];
        void Inc(string key, int amount = 1) => bd[MirrorKeys.Index[key]] += amount;

        // [判読性/レビュー指摘 移植元] 重なった全クラスを蓄積し、末尾で重み降順に整列する
        // （安定ソート＝同重みはマーク順維持 → 先頭は「最重1クラス」と常に一致）。
        //
        // これらは Dictionary（挿入順保持は .NET の公開契約ではないが、削除を一切しない使い方の下では
        // 現行実装が確実に保つ挙動）で持つ。MirrorKeys.Weights の合計順（Double のビット結果を左右する）
        // とは異なり、ここでの列挙順は表示の一覧順にしか影響せず、正しさ（hard/soft/breakdown/
        // weightedScore）には影響しない。
        var cellFams = new Dictionary<string, List<string>>();
        var countFams = new Dictionary<string, List<string>>();
        var needFams = new Dictionary<string, List<string>>();

        void Mark(int i, int j, string family)
        {
            var cls = VioClass.TryGetValue(family, out var c) ? c : family;
            var key = $"{i},{j}";
            if (!cellFams.TryGetValue(key, out var fams)) cellFams[key] = fams = new List<string>(2);
            if (!fams.Contains(cls)) fams.Add(cls);
        }

        void MarkNeed(int k, int j, string family)
        {
            var cls = VioClass.TryGetValue(family, out var c) ? c : family;
            var key = $"{k},{j}";
            if (!needFams.TryGetValue(key, out var fams)) needFams[key] = fams = new List<string>(2);
            if (!fams.Contains(cls)) fams.Add(cls);
        }

        void MarkCount(int i, int k, string family)
        {
            var cls = VioClass.TryGetValue(family, out var c) ? c : family;
            var key = $"{i},{k}";
            if (!countFams.TryGetValue(key, out var fams)) countFams[key] = fams = new List<string>(2);
            if (!fams.Contains(cls)) fams.Add(cls);
        }

        bool CellIs(int i, int j, int k) => i >= 0 && i < p.S && j >= 0 && j < p.T && s[i][j] == k;

        // ---- c1: window requirement --------------------------------------------------
        foreach (var c in p.Cons1)
        {
            for (int i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, c.ShiftIdx)) continue;
                int j = 0;
                bool prevViol = false;
                // [3.412.0/P-04 と同型] c.Day1 が i に依存しない判定だが、Kotlin 原本の位置
                // （canDo ガードの後、i ループの内側）をそのまま保つ。
                if (c.Day1 > p.T) continue;
                var row = s[i];
                int z = 0;
                for (int l = 0; l < c.Day1; l++) if (row[l] == c.ShiftIdx) z++;
                // [3.395.0/高速化 移植元] 窓は1日ずつ滑るので「出た日を引き、入った日を足す」だけ
                // （O(T)）。数える値は再スキャンと同じ＝結果は不変。
                while (j <= p.T - c.Day1)
                {
                    if (j > 0)
                    {
                        if (row[j - 1] == c.ShiftIdx) z--;
                        if (row[j + c.Day1 - 1] == c.ShiftIdx) z++;
                    }
                    bool viol = z < c.Day2;
                    if (viol)
                    {
                        Inc("c1");
                        if (!prevViol) Mark(i, j, "c1");
                    }
                    prevViol = viol;
                    j++;
                }
            }
        }

        // ---- c2: per-staff total --------------------------------------------------------
        var counts = ScheduleUtil.CountMatrix(p, s);
        foreach (var c in p.Cons2)
        {
            for (int i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, c.ShiftIdx)) continue;
                if (counts[i][c.ShiftIdx] < c.Count)
                {
                    Inc("c2");
                    MarkCount(i, c.ShiftIdx, "c2");
                }
            }
        }

        // ---- c41: group/day range --------------------------------------------------------
        foreach (var c in p.Cons41)
        {
            for (int j = 0; j < p.T; j++)
            {
                int z = 0;
                for (int i = 0; i < p.S; i++) if (p.Sgrp[i] == c.GroupIdx && CellIs(i, j, c.ShiftIdx)) z++;
                if (z < c.L || z > c.U)
                {
                    Inc("c41");
                    MarkNeed(c.ShiftIdx, j, "c41");
                }
            }
        }

        // ---- c42: group pair --------------------------------------------------------------
        // [3.395.0/高速化 移植元] 使い回しの int[] ＋件数（違反が出るのは稀＝大半は片側が空）。
        var pairL = new int[p.S];
        var pairR = new int[p.S];
        foreach (var c in p.Cons42)
        {
            for (int j = 0; j < p.T; j++)
            {
                int nL = 0, nR = 0;
                for (int i = 0; i < p.S; i++)
                {
                    if (p.Sgrp[i] == c.G1 && CellIs(i, j, c.S1)) pairL[nL++] = i;
                    if (p.Sgrp[i] == c.G2 && CellIs(i, j, c.S2)) pairR[nR++] = i;
                }
                if (nL == 0 || nR == 0) continue;
                // [3.318.0 移植元] 自己ペア／同一集合の順序重複を数えない。
                bool sameSet = c.G1 == c.G2 && c.S1 == c.S2;
                for (int a = 0; a < nL; a++)
                {
                    for (int b = 0; b < nR; b++)
                    {
                        int i = pairL[a];
                        int i2 = pairR[b];
                        if (i == i2) continue;
                        if (sameSet && i2 < i) continue;
                        Inc("c42");
                        Mark(i, j, "c42");
                        Mark(i2, j, "c42");
                    }
                }
            }
        }

        // ---- c41s / c42s: skill-group variants ---------------------------------------------
        foreach (var c in p.Cons41s)
        {
            for (int j = 0; j < p.T; j++)
            {
                int z = 0;
                for (int i = 0; i < p.S; i++) if (p.Ssk[i] == c.GroupIdx && CellIs(i, j, c.ShiftIdx)) z++;
                if (z < c.L || z > c.U) { Inc("c41s"); MarkNeed(c.ShiftIdx, j, "c41s"); }
            }
        }
        foreach (var c in p.Cons42s)
        {
            for (int j = 0; j < p.T; j++)
            {
                int nL = 0, nR = 0;
                for (int i = 0; i < p.S; i++)
                {
                    if (p.Ssk[i] == c.G1 && CellIs(i, j, c.S1)) pairL[nL++] = i;
                    if (p.Ssk[i] == c.G2 && CellIs(i, j, c.S2)) pairR[nR++] = i;
                }
                if (nL == 0 || nR == 0) continue;
                bool sameSet = c.G1 == c.G2 && c.S1 == c.S2;
                for (int a = 0; a < nL; a++)
                {
                    for (int b = 0; b < nR; b++)
                    {
                        int i = pairL[a];
                        int i2 = pairR[b];
                        if (i == i2) continue;
                        if (sameSet && i2 < i) continue;
                        Inc("c42s"); Mark(i, j, "c42s"); Mark(i2, j, "c42s");
                    }
                }
            }
        }

        // ---- c3 family (want / forbidden, x2 pattern variants) -----------------------------
        // Kotlin の `{ key, amt -> inc(key, amt) }` と同様に、Inc の既定引数(amount=1)は delegate
        // 変換では引き継がれない（C# の仕様上）ため、両引数を明示するラッパを渡す。Mark は既定引数を
        // 持たないので直接 delegate へ変換できる（Kotlin の `::mark` と同じ理由）。
        CheckC3Family(p, s, p.Cons3, "c3", forbidden: false, (key, amt) => Inc(key, amt), Mark);
        CheckC3Family(p, s, p.Cons3n, "c3n", forbidden: true, (key, amt) => Inc(key, amt), Mark);
        CheckC3Family(p, s, p.Cons3m, "c3m", forbidden: false, (key, amt) => Inc(key, amt), Mark);
        CheckC3Family(p, s, p.Cons3mn, "c3mn", forbidden: true, (key, amt) => Inc(key, amt), Mark);

        // ---- pref: wished cell not honored ---------------------------------------------------
        for (int i = 0; i < p.S; i++)
        {
            for (int j = 0; j < p.T; j++)
            {
                int w = p.Wish[i][j];
                // [監査#11② 移植元] 実現可能な希望の未充足のみ HARD(pref) 計上・着色。
                if (w >= 0 && w < p.K && p.CanDo(i, w) && s[i][j] != w)
                {
                    Inc("pref");
                    Mark(i, j, "pref");
                }
            }
        }

        // ---- range (low/high) + apt --------------------------------------------------------
        for (int i = 0; i < p.S; i++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int lo = p.RangeLo[i][k];
                int hi = p.RangeHi[i][k];
                int n = counts[i][k];
                if (lo != int.MinValue && lo != 0 && p.CanDo(i, k) && n < lo)
                {
                    Inc("low", lo - n);
                    MarkCount(i, k, "low");
                }
                if (hi != int.MaxValue && n > hi)
                {
                    Inc("high", n - hi);
                    MarkCount(i, k, "high");
                }
                // [統一apt 移植元] 適切回数(群単位の双方向目標)。SOFT・重み1・L1偏差|n-t|。
                int t = p.Apt[i][k];
                if (t >= 0 && n != t)
                {
                    Inc("apt", Math.Abs(n - t));
                    MarkCount(i, k, n > t ? "aptHigh" : "aptLow");
                }
            }
        }

        // ---- fair: within-group equalization --------------------------------------------------
        var fairLocs = new List<List<int>>();
        for (int g = 0; g < p.G; g++)
        {
            var mem = p.GroupMembers[g];
            int m = mem.Length;
            if (m < 2) continue;
            foreach (var k in p.Bucket[g])
            {
                int sum = 0;
                foreach (var x in mem) sum += counts[x][k];
                int tgt = (int)KotlinInterop.MathRound(sum / (double)m);
                int d = 0;
                foreach (var x in mem)
                {
                    int dx = Math.Abs(counts[x][k] - tgt);
                    d += dx;
                    if (dx > 0) fairLocs.Add(new List<int> { x, k, dx });
                }
                if (d > 0) Inc("fair", d);
            }
        }

        // ---- weekly: 7-day-cycle shift equalization ---------------------------------------------
        var weeklyLocs = new List<List<int>>();
        for (int i = 0; i < p.S; i++)
        {
            var wd = new int[p.K][];
            for (int k = 0; k < p.K; k++) wd[k] = new int[7];
            for (int j = 0; j < p.T; j++)
            {
                int k = s[i][j];
                if (k >= 0 && k < p.K) wd[k][(p.Dow0 + j) % 7]++;
            }
            for (int k = 0; k < p.K; k++)
            {
                int d = ScheduleUtil.WeeklyDevOfBucket(wd[k]);
                if (d > 0) { Inc("weekly", d); weeklyLocs.Add(new List<int> { i, k, d }); }
            }
        }
        var distLocations = new Dictionary<string, IReadOnlyList<IReadOnlyList<int>>>
        {
            ["weekly"] = weeklyLocs.OrderByDescending(x => x[2]).ToList(),
            ["fair"] = fairLocs.OrderByDescending(x => x[2]).ToList(),
        };

        // ---- covU / covO --------------------------------------------------------------------
        var cov = ScheduleUtil.Coverage(p, s);
        for (int j = 0; j < p.T; j++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int got = cov[j][k];
                int u = p.CovUCell(k, j, got);
                if (u > 0) { Inc("covU", u); MarkNeed(k, j, "covU"); }
                int o = p.CovOCell(k, j, got);
                if (o > 0) { Inc("covO", o); MarkNeed(k, j, "covO"); }
            }
        }

        // ---- groupViol: assigned to a shift the staff cannot take -----------------------------
        for (int i = 0; i < p.S; i++)
        {
            for (int j = 0; j < p.T; j++)
            {
                int k = s[i][j];
                if (k >= 0 && k < p.K && !p.CanDo(i, k))
                {
                    Inc("groupViol");
                    Mark(i, j, "groupViol");
                }
            }
        }

        // ---- aggregate ------------------------------------------------------------------------
        var breakdown = new Dictionary<string, int>();
        for (int bi = 0; bi < MirrorKeys.All.Count; bi++) breakdown[MirrorKeys.All[bi]] = bd[bi];

        int total = 0;
        foreach (var v in breakdown.Values) total += v;
        int hard = 0;
        foreach (var key0 in MirrorKeys.Hard) hard += breakdown.TryGetValue(key0, out var hv) ? hv : 0;
        int soft = total - hard;
        long elapsedMs = (long)(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);

        var hardParts = new List<string>();
        foreach (var key0 in MirrorKeys.Hard)
            hardParts.Add($"{key0}={(breakdown.TryGetValue(key0, out var hv2) ? hv2 : 0)}");
        var hardStr = string.Join(" ", hardParts);

        var softParts = new List<string>();
        foreach (var key0 in MirrorKeys.Soft)
        {
            int n = breakdown.TryGetValue(key0, out var sv) ? sv : 0;
            if (n > 0) softParts.Add($"{key0}={n}");
        }
        var softStr = string.Join(" ", softParts);

        string msg = total == 0
            ? "違反なし"
            : $"合計={total} | HARD={hard} [{hardStr}]" + (soft > 0 ? $" | SOFT={soft} [{softStr}]" : "");
        string level = total == 0 ? "I" : "W";

        var cellFamilies = BuildFamilyMaps(cellFams, out var violations);
        var countFamilies = BuildFamilyMaps(countFams, out var countViolations);
        var needFamilies = BuildFamilyMaps(needFams, out var needViolations);

        return new ViolationReport(
            Violations: violations,
            NeedViolations: needViolations,
            CountViolations: countViolations,
            Breakdown: breakdown,
            Total: total,
            Hard: hard,
            Soft: soft,
            WeightedScore: WeightedScore(breakdown))
        {
            CellFamilies = cellFamilies,
            CountFamilies = countFamilies,
            NeedFamilies = needFamilies,
            DistLocations = distLocations,
            Logs = new[] { new MirrorLog("UnifiedCheck", $"{msg} ({elapsedMs}ms)", iter: 0, level: level) },
        };
    }

    /// <summary>
    /// [Set化 移植元] 重なった全クラスを重み降順に整列した族マップと、その先頭（最重1クラス）だけの
    /// 単一クラスマップを同時に作る。両方が同じ元データから同時に生成されるため、
    /// 「先頭は単一クラスマップの値と常に一致する」不変条件が構造的に保たれる。
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildFamilyMaps(
        Dictionary<string, List<string>> fams, out IReadOnlyDictionary<string, string> singleFamily)
    {
        var families = new Dictionary<string, IReadOnlyList<string>>(fams.Count);
        var single = new Dictionary<string, string>(fams.Count);
        foreach (var (ck, cv) in fams)
        {
            var sorted = cv.Count <= 1 ? cv : cv.OrderByDescending(x => ClassWeight.TryGetValue(x, out var w) ? w : 0.0).ToList();
            families[ck] = sorted;
            single[ck] = sorted[0];
        }
        singleFamily = single;
        return families;
    }

    private static void CheckC3Family(
        Problem p, int[][] schedule, IReadOnlyList<C3> list, string key, bool forbidden,
        Action<string, int> inc, Action<int, int, string> mark)
    {
        foreach (var c in list)
        {
            var seq = c.Seq;
            int d = seq.Length;
            if (d == 0 || d > p.T) continue;
            // [統一: Evaluator の HF507 と一致 移植元] 非forbidden の単一シフト連は run-deficit で評価する。
            if (!forbidden && C3Run.IsSingleShiftSeq(seq))
            {
                int first = seq[0];
                for (int i = 0; i < p.S; i++)
                {
                    var row = schedule[i];
                    int t = row.Length;
                    int runStart = -1;
                    int r = 0;
                    int j = 0;
                    while (j <= t)
                    {
                        bool on = j < t && row[j] == first;
                        if (on)
                        {
                            if (r == 0) runStart = j;
                            r++;
                        }
                        else if (r > 0)
                        {
                            int deficit = d - r;
                            if (deficit > 0)
                            {
                                inc(key, deficit);
                                mark(i, runStart, key);
                            }
                            r = 0; runStart = -1;
                        }
                        j++;
                    }
                }
                continue;
            }
            for (int i = 0; i < p.S; i++)
            {
                int j = 0;
                while (j <= p.T - d)
                {
                    if (schedule[i][j] == seq[0])
                    {
                        int z = 0;
                        for (int l = 1; l < d; l++) if (schedule[i][j + l] == seq[l]) z++;
                        bool fire = forbidden ? (z == d - 1) : (z < d - 1);
                        if (fire)
                        {
                            inc(key, 1);
                            if (forbidden) { for (int l = 0; l < d; l++) mark(i, j + l, key); }
                            else mark(i, j, key);
                        }
                    }
                    j++;
                }
            }
        }
    }

    private static double WeightedScore(IReadOnlyDictionary<string, int> b)
    {
        // [N2/⛏11 移植元] 重みは MirrorKeys.Weights を単一の真実として参照。列挙順を保持しているため
        // 加算順は Kotlin と同一＝Double 結果は不変。
        double outVal = 0.0;
        foreach (var (key, weight) in MirrorKeys.Weights)
            outVal += (b.TryGetValue(key, out var v) ? v : 0) * weight;
        return outVal;
    }
}

/// <summary>
/// Faithful port of Kotlin's <c>ScheduleRunResult</c> data class (also declared in
/// <c>MirrorCore.kt</c>). Shared return type of the schedule generators
/// (<see cref="SmartInitialScheduler"/>, <see cref="GreedyMirrorScheduler"/>, phase 4) and later
/// (phase 7) of <c>ScheduleCsvBridge</c>'s CSV import path — hence the 4 CSV-only fields below
/// are ported now (with their Kotlin default values) even though nothing populates them until
/// phase 7, so this record's shape does not need to change again when that phase wires them up.
/// </summary>
public sealed record ScheduleRunResult(
    int[][] Schedule,
    ViolationReport Report,
    /// <summary>CSV取込で氏名が一致したスタッフ行数。最適化系の結果では未使用(-1)。</summary>
    int Matched = -1,
    /// <summary>CSV取込でシフト一覧に無い記号だったセルの数。</summary>
    int UnknownCells = 0,
    /// <summary>その未知記号（多い順・上位）。</summary>
    IReadOnlyList<string>? UnknownSymbols = null,
    /// <summary>引用符が閉じないまま入力が終わった（開いた引用符以降が1セルへ吸い込まれ、残りの行が丸ごと消えた）。</summary>
    bool UnclosedQuote = false)
{
    public IReadOnlyList<string> UnknownSymbols { get; init; } = UnknownSymbols ?? Array.Empty<string>();
}

// LightOptimizeResult (also declared in MirrorCore.kt) is deferred to phase 5 (SA optimizer
// results) — not needed by anything in phase 4's scope.
