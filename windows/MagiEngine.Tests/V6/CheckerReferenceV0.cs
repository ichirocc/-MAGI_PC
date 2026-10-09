using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// 2026-10-03 の check 高速化（c41/c42 の前計算・ログの遅延生成）より前の <see cref="UnifiedViolationChecker"/> の写し。
/// <see cref="CheckerEquivalenceTest"/> が新旧の報告を全フィールドで突き合わせるための基準＝ここは直さない。
/// </summary>
internal static class CheckerReferenceV0
{
    private static readonly Dictionary<string, string> VioClass = new()
    {
        ["c1"] = "vio-c1", ["c2"] = "vio-c2", ["c3"] = "vio-c3", ["c3n"] = "vio-c3n", ["c3w"] = "vio-c3w",
        ["c3m"] = "vio-c3m", ["c3mn"] = "vio-c3mn", ["c41"] = "vio-c41", ["c42"] = "vio-c42",
        ["c41s"] = "vio-c41s", ["c42s"] = "vio-c42s",
        ["covU"] = "vio-covU", ["covO"] = "vio-covO", ["pref"] = "vio-pref",
        ["low"] = "vio-low", ["high"] = "vio-high", ["groupViol"] = "vio-groupViol",
        ["aptLow"] = "vio-aptLow", ["aptHigh"] = "vio-aptHigh",
        ["extWish"] = "vio-extWish",
    };


    private static readonly IReadOnlyDictionary<string, double> ClassWeight =
        VioClass.ToDictionary(kv => kv.Value, kv => MirrorKeys.WeightOf(kv.Key));

    public static ViolationReport Check(MagiState state, int[][]? schedule = null, bool quantitativeRangeEval = false)
    {
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var p = ScheduleUtil.CachedProblem(state, quantitativeRangeEval);
        var scratch = (t_checkScratch ??= new CheckScratch()).Fit(p);
        var s = scratch.Normalize(schedule ?? state.Schedule.ToIntArray2D(), p);

        // [3.395.0/高速化 移植元] 集計は添字加算の int[] で行い、最後に MirrorKeys.All の順で
        // Dictionary へ起こす（内容も順序も従来と同じ）。
        var bd = new int[MirrorKeys.All.Count];
        void Inc(string key, int amount = 1) => bd[MirrorKeys.Index[key]] += amount;

        // [判読性/レビュー指摘 移植元] 重なった全クラスを蓄積し、末尾で重み降順に整列する
        // （安定ソート＝同重みはマーク順維持 → 先頭は「最重1クラス」と常に一致）。
        //
        // [2026-09-02/監査是正 移植元] ここでの列挙順は表示の一覧順だけでなく、下流の貪欲修復パス
        // （Range/Apt/C3Run/C3n/C3mn）が候補を処理する順序＝keep-best が収束する具体的な局所最適にも
        // 効く（正しさそのもの＝hard/soft/breakdown/weightedScore には影響しない）。素の Dictionary は
        // 削除を一切しない使い方の下では現行 .NET 実装が挿入順を保つが、それは公開契約ではなく将来の
        // BCL 変更で静かに崩れうるため、契約として挿入順を保証する InsertionOrderDictionary で持つ。
        var cellFams = new InsertionOrderDictionary<string, IReadOnlyList<string>>();
        var countFams = new InsertionOrderDictionary<string, IReadOnlyList<string>>();
        var needFams = new InsertionOrderDictionary<string, IReadOnlyList<string>>();
        var c1Runs = new List<IReadOnlyList<int>>();

        void Mark(int i, int j, string family)
        {
            var cls = VioClass.TryGetValue(family, out var c) ? c : family;
            var key = PairKey(i, j);
            if (!cellFams.TryGetValue(key, out var fams0)) cellFams[key] = fams0 = new List<string>(2);
            var fams = (List<string>)fams0;
            if (!fams.Contains(cls)) fams.Add(cls);
        }

        void MarkNeed(int k, int j, string family)
        {
            var cls = VioClass.TryGetValue(family, out var c) ? c : family;
            var key = PairKey(k, j);
            if (!needFams.TryGetValue(key, out var fams0)) needFams[key] = fams0 = new List<string>(2);
            var fams = (List<string>)fams0;
            if (!fams.Contains(cls)) fams.Add(cls);
        }

        void MarkCount(int i, int k, string family)
        {
            var cls = VioClass.TryGetValue(family, out var c) ? c : family;
            var key = PairKey(i, k);
            if (!countFams.TryGetValue(key, out var fams0)) countFams[key] = fams0 = new List<string>(2);
            var fams = (List<string>)fams0;
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
                int runStart = 0;
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
                        if (!prevViol) { Mark(i, j, "c1"); runStart = j; }
                    }
                    else if (prevViol) c1Runs.Add(new[] { i, runStart, j - runStart, c.Day1 });
                    prevViol = viol;
                    j++;
                }
                if (prevViol) c1Runs.Add(new[] { i, runStart, j - runStart, c.Day1 });
            }
        }

        // ---- c2: per-staff total --------------------------------------------------------
        var counts = scratch.CountMatrix(s, p);
        foreach (var c in p.Cons2)
        {
            for (int i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, c.ShiftIdx)) continue;
                if (quantitativeRangeEval)
                {
                    var amt = Evaluator.C2Amount(counts[i][c.ShiftIdx], c.Count);
                    if (amt > 0) { Inc("c2", (int)amt); MarkCount(i, c.ShiftIdx, "c2"); }
                }
                else if (counts[i][c.ShiftIdx] < c.Count)
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
                if (quantitativeRangeEval)
                {
                    var amt = Evaluator.RangeDistance(z, c.L, c.U);
                    if (amt > 0) { Inc("c41", (int)amt); MarkNeed(c.ShiftIdx, j, "c41"); }
                }
                else if (z < c.L || z > c.U)
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
                if (quantitativeRangeEval)
                {
                    var amt = Evaluator.RangeDistance(z, c.L, c.U);
                    if (amt > 0) { Inc("c41s", (int)amt); MarkNeed(c.ShiftIdx, j, "c41s"); }
                }
                else if (z < c.L || z > c.U) { Inc("c41s"); MarkNeed(c.ShiftIdx, j, "c41s"); }
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

        // ---- c3w: 希望の前日に禁止（HARD, 3.542.0） ------------------------------------------
        // 前日側のセル（動かせる側）を違反箇所にする。静的表 Problem.C3wBan を引くだけ。
        if (p.C3wBan != null)
        {
            for (int i = 0; i < p.S; i++)
                for (int j = 0; j < p.T; j++)
                    if (p.C3wBanned(i, j, s[i][j])) { Inc("c3w"); Mark(i, j, "c3w"); }
        }
        // [3.653.0] 族の追加は高速化と別の意味の変更＝新旧の両方へ同じ位置で入れる（比較の対象は高速化だけ）。
        if (p.HasExtBan)
        {
            for (int i = 0; i < p.S; i++)
                for (int j = 0; j < p.T; j++)
                    if (p.ExtBanned(i, j, s[i][j])) { Inc("extWish"); Mark(i, j, "extWish"); }
        }

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
                // [統一apt 移植元] 適切回数(群単位の双方向目標)。SOFT・L1偏差|n-t|。
                int t = p.Apt[i][k];
                if (t >= 0 && n != t)
                {
                    Inc("apt", Math.Abs(n - t));
                    MarkCount(i, k, n > t ? "aptHigh" : "aptLow");
                }
            }
        }

        // ---- fair: within-group equalization --------------------------------------------------
        // [統一fair/3.538.0] グループ内公平化: 群×担当ONシフトごと、Problem.FairDevOfBucket（達成率モード、
        // 全員に基準が無ければ従来の生回数round(平均)方式）からのL1偏差和。SOFT。最適化器(Evaluator/Delta)と同一指標。
        var fairLocs = new List<List<int>>();
        for (int g = 0; g < p.G; g++)
        {
            var mem = p.GroupMembers[g];
            if (mem.Length < 2) continue;
            foreach (var k in p.Bucket[g])
            {
                var res = p.FairDevOfBucket(g, k, x => counts[x][k]);
                foreach (var (x, dx) in res.PerMember) fairLocs.Add(new List<int> { x, k, dx });
                if (res.Total > 0) Inc("fair", res.Total);
            }
        }

        // ---- weekly: 7-day-cycle shift equalization ---------------------------------------------
        var weeklyLocs = new List<List<int>>();
        var wd = scratch.Wd;
        for (int i = 0; i < p.S; i++)
        {
            foreach (var w in wd) Array.Clear(w);
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
        var cov = scratch.Coverage(s, p);
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
            C1Runs = c1Runs,
            Logs = new[] { new MirrorLog("UnifiedCheck", $"{msg} ({elapsedMs}ms)", iter: 0, level: level) },
        };
    }

    /// <summary>
    /// [Set化 移植元] 重なった全クラスを重み降順に整列した族マップと、その先頭（最重1クラス）だけの
    /// 単一クラスマップを同時に作る。両方が同じ元データから同時に生成されるため、
    /// 「先頭は単一クラスマップの値と常に一致する」不変条件が構造的に保たれる。
    /// [2026-09-02/監査是正 移植元] 戻り値の2マップも <see cref="InsertionOrderDictionary{TKey,TValue}"/>
    /// で持つ（<c>fams</c> の挿入順＝下流修復パスの処理順をここでも保つ理由は呼出元コメント参照）。
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildFamilyMaps(
        InsertionOrderDictionary<string, IReadOnlyList<string>> fams, out IReadOnlyDictionary<string, string> singleFamily)
    {
        var single = new InsertionOrderDictionary<string, string>();
        foreach (var ck in fams.Keys)
        {
            var cv = fams[ck];
            var sorted = cv.Count <= 1 ? cv : cv.OrderByDescending(x => ClassWeight.TryGetValue(x, out var w) ? w : 0.0).ToList();
            fams[ck] = sorted;
            single[ck] = sorted[0];
        }
        singleFamily = single;
        return fams;
    }

    /// <summary>Check() 内だけで使う作業配列（戻り値へ出ない）。1 スレッド 1 組を寸法が合う限り使い回す。</summary>
    private sealed class CheckScratch
    {
        private int[][] _s = Array.Empty<int[]>(), _counts = Array.Empty<int[]>(), _cov = Array.Empty<int[]>();
        public int[][] Wd = Array.Empty<int[]>();

        private static int[][] Fit(int[][] a, int rows, int cols)
        {
            if (a.Length == rows && (rows == 0 || a[0].Length == cols)) return a;
            var r = new int[rows][];
            for (int x = 0; x < rows; x++) r[x] = new int[cols];
            return r;
        }

        public CheckScratch Fit(Problem p)
        {
            _s = Fit(_s, p.S, p.T); _counts = Fit(_counts, p.S, p.K); _cov = Fit(_cov, p.T, p.K); Wd = Fit(Wd, p.K, 7);
            return this;
        }

        /// <summary><see cref="ScheduleUtil.NormalizeSchedule"/> と同じ値を書く。</summary>
        public int[][] Normalize(int[][] schedule, Problem p)
        {
            for (int i = 0; i < p.S; i++)
            {
                var src = i < schedule.Length ? schedule[i] : null;
                var row = _s[i];
                for (int j = 0; j < p.T; j++)
                {
                    int k = (src is not null && j < src.Length) ? src[j] : -1;
                    row[j] = (k >= 0 && k < p.K) ? k : -1;
                }
            }
            return _s;
        }

        /// <summary><see cref="ScheduleUtil.CountMatrix"/> と同じ値を書く。</summary>
        public int[][] CountMatrix(int[][] sc, Problem p)
        {
            foreach (var r in _counts) Array.Clear(r);
            for (int i = 0; i < p.S; i++)
                for (int j = 0; j < p.T; j++) { int k = sc[i][j]; if (k >= 0 && k < p.K) _counts[i][k]++; }
            return _counts;
        }

        /// <summary><see cref="ScheduleUtil.Coverage"/> と同じ値を書く。</summary>
        public int[][] Coverage(int[][] sc, Problem p)
        {
            foreach (var r in _cov) Array.Clear(r);
            for (int i = 0; i < p.S; i++)
                for (int j = 0; j < p.T; j++) { int k = sc[i][j]; if (k >= 0 && k < p.K) _cov[j][k]++; }
            return _cov;
        }
    }

    [ThreadStatic] private static CheckScratch? t_checkScratch;

    private const int PairKeyN = 64;
    private static readonly string[] PairKeys = BuildPairKeys();
    private static string[] BuildPairKeys()
    {
        var a = new string[PairKeyN * PairKeyN];
        for (int x = 0; x < a.Length; x++) a[x] = $"{x / PairKeyN},{x % PairKeyN}";
        return a;
    }

    /// <summary>違反マップのキー "a,b"。業務上限（30 名・31 日）の範囲は作り置きを返す。</summary>
    private static string PairKey(int a, int b) =>
        a >= 0 && a < PairKeyN && b >= 0 && b < PairKeyN ? PairKeys[a * PairKeyN + b] : $"{a},{b}";

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
