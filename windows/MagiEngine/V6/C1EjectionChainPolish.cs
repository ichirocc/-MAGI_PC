using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>[Kotlin 原本 <c>C1EjectionChainPolish</c>] 違反の起点（C1／ALL／HARD／SOFT）に手を置き、その歪みを直す手を継ぎ足す深さ優先探索（途中の HARD 増は可・
/// 差分評価の apply/undo）。連鎖全体を正式 checker の採用ゲート（betterReport＋厳密ピン）で 1 回だけ採否する。<c>indexOnly</c>・<c>collect</c> は盤面を変えない。
/// C# では <see cref="EjectionChainPipeline"/> からだけ呼ぶ（従来の常時フルの置き場所は持たない）。</summary>
internal static class C1EjectionChainPolish
{
    public sealed record Config(
        int? MaxDepth = null,
        /// <summary>深さごとの分岐数（尽きたら最後の値）。null＝[<see cref="DefaultNarrow"/>] なら 2,1、そうでなければ 4,3,2,2,1。</summary>
        int[]? Branching = null,
        /// <summary>途中の盤面で許す HARD の増分。</summary>
        int HardSlack = 2,
        long MaxMillis = 3_000L,
        /// <summary>0 以下＝無制限。決定的モードではこちらで止める。</summary>
        long MaxEvaluations = 0L,
        int MaxRounds = 4,
        /// <summary>true＝v2（族越え）。null＝[<see cref="DefaultCrossFamily"/>]。</summary>
        bool? CrossFamily = null,
        /// <summary>v2: Δ評価の上位から族別差分まで調べる候補数。</summary>
        int FamilyProbe = 48,
        Origin Origin = Origin.C1,
        /// <summary>ALL: 違反 1 箇所あたり連鎖を始める初手の数（Δ評価の良い順）。</summary>
        int SeedBranch = 2,
        /// <summary>ALL: 1 巡で起点にする違反箇所の族ごとの上限（巡ごとにずらす）。</summary>
        int UnitsPerFamily = 6,
        /// <summary>起点 1 つの連鎖に使う評価数の上限（0 以下＝無制限）。null＝[<see cref="DefaultNarrow"/>] なら 0、そうでなければ 30,000。</summary>
        long? PerSeedEvaluations = null,
        /// <summary>族越えで、同日の 2 人・同じ職員の 2 日の入れ替えも 1 手として探す。</summary>
        bool SwapMoves = true,
        /// <summary>0 以下＝無制限。重複・HARD 超過で捨てた候補も数える。</summary>
        long MaxCandidates = 0L,
        /// <summary>2 手目以降の候補を動かしたセルの「穴」に絞る。null＝[<see cref="DefaultHoleFocus"/>]。</summary>
        bool? HoleFocus = null,
        int HoleRadius = 7,
        /// <summary>穴を必須以外の族の起点だけに使う。null＝[<see cref="DefaultHoleSoftOnly"/>]。</summary>
        bool? HoleSoftOnly = null,
        /// <summary>起点をこの並びに限る（null＝巡ごとに生成）。パイプラインの索引が作る。</summary>
        IReadOnlyList<SeedKey>? SeedList = null,
        /// <summary>SOFT・ALL の起点に c1 不足窓の起点を入れない（パイプラインの BOTH の SOFT 側）。</summary>
        bool SkipC1Seeds = false,
        /// <summary>評価回数の上限があっても時間の上限を併せて効かせる（実時間の予察）。</summary>
        bool TimeWithEvaluations = false,
        /// <summary>族ごとの起点を先へずらす巡の数（パイプラインの 2 巡目以降の索引。0＝従来）。</summary>
        int RoundOffset = 0);

    public enum Origin { C1, ALL, HARD, SOFT }

    /// <summary>起点（族・職員・日・置くシフト）。</summary>
    public sealed record SeedKey(string Family, int I, int J, int K);

    /// <summary><c>collect</c> に渡す手順。Report は開始盤面にこの手順を当てた正式評価（採用ゲートを通ったものだけ）。Depth は手数。</summary>
    public sealed class PathCandidate
    {
        public PathCandidate(SeedKey seed, IReadOnlyList<int[]> path, ViolationReport report, int depth) { Seed = seed; Path = path; Report = report; Depth = depth; }
        public SeedKey Seed { get; }
        public IReadOnlyList<int[]> Path { get; }
        public ViolationReport Report { get; }
        public int Depth { get; }
    }

    /// <summary>測定用の切替（Kotlin と同じ既定）。</summary>
    internal static volatile bool DefaultCrossFamily = true;
    internal static volatile bool DefaultHoleFocus = true;
    internal static volatile bool DefaultHoleSoftOnly = true;
    internal static volatile int DefaultMaxDepth = 8;
    internal static volatile bool DefaultNarrow = false;

    private static readonly HashSet<string> HardFamilies = new() { "c3n", "covU", "c3w", "pref", "groupViol", "extWish" };

    public sealed class Stats
    {
        public int Seeds; public long Candidates; public long Evaluations; public int ChainsTried; public int Accepted;
        public int AcceptedMaxDepth; public long Dedup; public int Timeouts; public string EndReason = "完了";
        public long Generated;
        /// <summary>起点ごとの評価上限で探索を打ち切った起点の数。</summary>
        public int SeedCapped;
        /// <summary>起点の族 → [起点数, 評価数, 採用数, 加重スコアの減少量]（挿入順）。</summary>
        public readonly List<KeyValuePair<string, long[]>> ByFamily = new();
        public string? Mismatch;
        /// <summary><c>collect</c> のとき、採用ゲートを通る手順が見つかった起点（挿入順）。</summary>
        public readonly List<SeedKey> HitSeeds = new();

        internal long[] Family(string f)
        {
            foreach (var kv in ByFamily) if (kv.Key == f) return kv.Value;
            var a = new long[4];
            ByFamily.Add(new KeyValuePair<string, long[]>(f, a));
            return a;
        }
    }

    private static readonly IComparer<long[]> CandOrder = Comparer<long[]>.Create((a, b) =>
    {
        for (var x = 0; x < 7; x++) { var c = a[x].CompareTo(b[x]); if (c != 0) return c; }
        return 0;
    });

    private sealed record Seed(string Family, int I, int J, int K);

    /// <summary>違反クラス名（<c>vio-aptLow</c> 等）→ 族名。</summary>
    private static string FamilyOfClass(string cls)
    {
        var s = cls.StartsWith("vio-", StringComparison.Ordinal) ? cls.Substring(4) : cls;
        return s == "aptLow" || s == "aptHigh" ? "apt" : s;
    }

    private static readonly string[] Families =
    {
        "c1", "c2", "c41", "c42", "c41s", "c42s", "c3", "c3n", "c3m", "c3mn",
        "pref", "groupViol", "c3w", "apt", "fair", "weekly", "covO", "covU", "extWish",
    };

    private static long[] FamilyWeighted(DeltaEvaluator de)
    {
        var raw = de.FamilyRaw();
        var outArr = new long[Families.Length + 2];
        for (var idx = 0; idx < Families.Length; idx++)
            outArr[idx] = (long)((raw.TryGetValue(Families[idx], out var v) ? v : 0L) * MirrorKeys.WeightOf(Families[idx]));
        var (lo, hi) = de.RangeRaw();
        outArr[Families.Length] = (long)(lo * MirrorKeys.WeightOf("low"));
        outArr[Families.Length + 1] = (long)(hi * MirrorKeys.WeightOf("high"));
        return outArr;
    }

    /// <summary>決定的な 64 bit の鍵（Zobrist）。値は訪問済みの判定にだけ使う＝乱数の系列が Kotlin と違っても結果は同じ。</summary>
    private static long[][][] Zobrist(Problem p, ulong seed)
    {
        var z = seed;
        long Next()
        {
            z += 0x9E3779B97F4A7C15UL;
            var x = z;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return (long)(x ^ (x >> 31));
        }
        var t = new long[p.S][][];
        for (var i = 0; i < p.S; i++)
        {
            t[i] = new long[p.T][];
            for (var j = 0; j < p.T; j++) { t[i][j] = new long[p.K]; for (var k = 0; k < p.K; k++) t[i][j][k] = Next(); }
        }
        return t;
    }

    public static V6HotfixPasses.CyclicSwapResult Apply(
        MagiState state, int[][] schedule, Config? config = null,
        Func<bool>? shouldStop = null, bool quantitativeRangeEval = false, Stats? stats = null,
        Action<IReadOnlyList<SeedKey>>? indexOnly = null, Action<PathCandidate>? collect = null)
    {
        var cfg = config ?? new Config();
        var st = stats ?? new Stats();
        var stop = shouldStop ?? (() => false);
        var maxDepth = cfg.MaxDepth ?? DefaultMaxDepth;
        var branching = cfg.Branching ?? (DefaultNarrow ? new[] { 2, 1 } : new[] { 4, 3, 2, 2, 1 });
        var perSeedEvaluations = cfg.PerSeedEvaluations ?? (DefaultNarrow ? 0L : 30_000L);
        var holeFocus = cfg.HoleFocus ?? DefaultHoleFocus;
        var holeSoftOnly = cfg.HoleSoftOnly ?? DefaultHoleSoftOnly;
        var t0 = EngineClock.NowMs();
        var p = new Problem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var rep = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        var before = rep;
        var all = cfg.Origin != Origin.C1;
        var hardOnly = cfg.Origin == Origin.HARD;
        var softOnly = cfg.Origin == Origin.SOFT;
        var crossFamily = all || (cfg.CrossFamily ?? DefaultCrossFamily);
        var hasUnassigned = work.Any(row => row.Any(v => v < 0 || v >= p.K));
        var c1Now = rep.Breakdown.TryGetValue("c1", out var c1v) ? c1v : 0;
        if (hasUnassigned || (hardOnly ? rep.Hard == 0 : softOnly ? rep.Total == rep.Hard : all ? rep.Total == 0 : p.Cons1.Count == 0 || c1Now == 0))
        {
            indexOnly?.Invoke(Array.Empty<SeedKey>());
            return new V6HotfixPasses.CyclicSwapResult(work, rep.Total, rep.Total, 0,
                new[] { new MirrorLog(tag: "C1EjectionChain", message: "対象なし=スキップ") }, Report: rep);
        }
        var de = new DeltaEvaluator(p);
        de.Reset(work);
        var zob = Zobrist(p, 0xE1EC7UL);
        // 2 本目の独立なハッシュ。1 本目だけが一致した別盤面（衝突）を既訪と誤認して枝を捨てないため。
        var zob2 = Zobrist(p, 0x5EC0DUL);
        long hash = 0L, hash2 = 0L;
        for (var i = 0; i < p.S; i++) for (var j = 0; j < p.T; j++) { hash ^= zob[i][j][work[i][j]]; hash2 ^= zob2[i][j][work[i][j]]; }
        var pinBlocks = new PinBlockAttribution();

        bool Out()
        {
            string why;
            if (stop()) why = "中断";
            else if (cfg.MaxEvaluations > 0 && st.Evaluations >= cfg.MaxEvaluations) why = "評価上限";
            else if (cfg.MaxCandidates > 0 && st.Generated >= cfg.MaxCandidates) why = "生成上限";
            else if ((cfg.TimeWithEvaluations || (cfg.MaxEvaluations <= 0 && cfg.MaxCandidates <= 0)) && EngineClock.NowMs() - t0 >= cfg.MaxMillis) why = "時間切れ";
            else return false;
            st.EndReason = why;
            return true;
        }

        static long HardOf(long score) => score / Evaluator.SCORE_HARD_UNIT;

        void Move(int i, int j, int k)
        {
            hash ^= zob[i][j][work[i][j]] ^ zob[i][j][k];
            hash2 ^= zob2[i][j][work[i][j]] ^ zob2[i][j][k];
            work[i][j] = k;
            de.Apply(i, j, k);
        }

        var applied = 0;
        var round = 0;
        var lastSeedCount = 0;
        var lastImproved = false;
        var unitsSkipped = 0;

        List<Seed> C1Seeds()
        {
            var seeds = new List<Seed>();
            foreach (var c in p.Cons1)
            {
                var x = c.ShiftIdx;
                if (x < 0 || x >= p.K || c.Day1 <= 0) continue;
                for (var i = 0; i < p.S; i++)
                {
                    if (!p.MayPlace(i, x)) continue;
                    for (var j = 0; j < p.T; j++)
                    {
                        if (work[i][j] == x || p.WishLocked(i, j) || p.ExtBanned(i, j, x)) continue;
                        if (V6HotfixPasses.InDeficientC1Window(p, work, i, x, c.Day1, c.Day2, j)) seeds.Add(new Seed("c1", i, j, x));
                    }
                }
            }
            return seeds;
        }

        List<Seed> AllSeeds()
        {
            unitsSkipped = 0;
            var perFamily = new List<KeyValuePair<string, List<List<int[]>>>>();   // 族 → 違反 1 箇所ごとの関与セル（挿入順）
            void AddUnit(string fam, List<int[]> cells)
            {
                if (hardOnly && !HardFamilies.Contains(fam)) return;
                if (softOnly && HardFamilies.Contains(fam)) return;
                foreach (var kv in perFamily) if (kv.Key == fam) { kv.Value.Add(cells); return; }
                perFamily.Add(new KeyValuePair<string, List<List<int[]>>>(fam, new List<List<int[]>> { cells }));
            }
            List<int[]> Row(int i) => Enumerable.Range(0, p.T).Select(d => new[] { i, d }).ToList();
            List<int[]> Col(int j) => Enumerable.Range(0, p.S).Select(s2 => new[] { s2, j }).ToList();
            foreach (var (key, classes) in rep.CellFamilies)
            {
                var parts = key.Split(',');
                var i = int.Parse(parts[0]); var j = int.Parse(parts[1]);
                foreach (var c in classes) { var f = FamilyOfClass(c); if (f != "c1") AddUnit(f, new List<int[]> { new[] { i, j } }); }
            }
            foreach (var (key, classes) in rep.CountFamilies)
            {
                var i = int.Parse(key.Split(',')[0]);
                foreach (var c in classes) AddUnit(FamilyOfClass(c), Row(i));
            }
            foreach (var (key, classes) in rep.NeedFamilies)
            {
                var j = int.Parse(key.Split(',')[1]);
                foreach (var c in classes) AddUnit(FamilyOfClass(c), Col(j));
            }
            foreach (var (f, locs) in rep.DistLocations) foreach (var l in locs) AddUnit(f, Row(l[0]));
            var lists = new List<(string Fam, List<Seed> Seeds)>();
            // HARD は必須の族だけを起点にする（c1 はソフト）。旧: c1 の起点も混ざり、必須の焦点が期間の制約を先に直していた。
            var c1 = cfg.SkipC1Seeds || hardOnly ? new List<Seed>() : C1Seeds();
            if (c1.Count > 0) lists.Add(("c1", c1));
            foreach (var (f, us0) in perFamily)
            {
                var outList = new List<Seed>();
                var off = ((round - 1 + cfg.RoundOffset) * cfg.UnitsPerFamily) % us0.Count;
                var us = us0.Skip(off).Concat(us0.Take(off)).Take(cfg.UnitsPerFamily).ToList();
                unitsSkipped += us0.Count - us.Count;
                foreach (var cells in us)
                {
                    if (Out()) break;
                    var cand = new List<long[]>();
                    foreach (var cell in cells)
                    {
                        var i = cell[0]; var j = cell[1];
                        if (p.WishLocked(i, j)) continue;
                        foreach (var k in p.AllowedShiftsForStaff(i))
                        {
                            if (k == work[i][j] || p.ExtBanned(i, j, k)) continue;
                            if (Out()) break;
                            var sc = de.PreviewMove(i, j, k); st.Evaluations++; st.Generated++;
                            if (HardOf(sc) > HardOf(de.Score()) + cfg.HardSlack) continue;
                            cand.Add(new[] { sc, i, j, (long)k });
                        }
                    }
                    var sorted = cand.OrderBy(c => c[0]).ThenBy(c => c[1]).ThenBy(c => c[2]).ThenBy(c => c[3]).ToList();
                    foreach (var c in sorted.Take(cfg.SeedBranch)) outList.Add(new Seed(f, (int)c[1], (int)c[2], (int)c[3]));
                }
                if (outList.Count > 0) lists.Add((f, outList.DistinctBy(s => (s.I, s.J, s.K)).ToList()));
            }
            var orderedLists = lists
                .OrderBy(l => MirrorKeys.Hard.Contains(l.Fam) ? 0 : 1)
                .ThenBy(l => -MirrorKeys.WeightOf(l.Fam))
                .ThenBy(l => l.Fam, StringComparer.Ordinal)
                .ToList();
            var merged = new List<Seed>();
            var n = 0;
            while (true)
            {
                var any = false;
                foreach (var (_, l) in orderedLists) if (n < l.Count) { merged.Add(l[n]); any = true; }
                if (!any) break;
                n++;
            }
            return merged;
        }

        while (round < cfg.MaxRounds && !Out())
        {
            round++;
            var improvedThisRound = false;
            var seeds = cfg.SeedList?.Select(s => new Seed(s.Family, s.I, s.J, s.K)).ToList() ?? (all ? AllSeeds() : C1Seeds());
            lastSeedCount = seeds.Count;
            if (indexOnly != null)
            {
                // 索引: 起点の初手だけの評価で並べる（同点は生成順）。盤面は変えない。
                var scored = new List<(long Score, int N)>();
                for (var n = 0; n < seeds.Count; n++)
                {
                    var sd = seeds[n];
                    if (work[sd.I][sd.J] == sd.K || Out()) continue;
                    scored.Add((de.PreviewMove(sd.I, sd.J, sd.K), n)); st.Evaluations++;
                }
                indexOnly(scored.OrderBy(x => x.Score).ThenBy(x => x.N).Select(x => { var sd = seeds[x.N]; return new SeedKey(sd.Family, sd.I, sd.J, sd.K); }).ToList());
                return new V6HotfixPasses.CyclicSwapResult(work, rep.Total, rep.Total, 0, Array.Empty<MirrorLog>(), Report: rep);
            }
            foreach (var seed in seeds)
            {
                if (Out()) break;
                var si = seed.I; var sj = seed.J; var sx = seed.K;
                if (work[si][sj] == sx) continue;
                if (!all && !p.Cons1.Any(c => c.ShiftIdx == sx && c.Day1 > 0 && V6HotfixPasses.InDeficientC1Window(p, work, si, sx, c.Day1, c.Day2, sj))) continue;
                st.Seeds++;
                var famStat = st.Family(seed.Family);
                famStat[0]++;
                var evalAtSeed = st.Evaluations;
                bool SeedSpent() => perSeedEvaluations > 0 && st.Evaluations - evalAtSeed >= perSeedEvaluations;
                var baseScore = de.Score();
                var baseHard = HardOf(baseScore);
                // 盤面ハッシュ→(2 本目のハッシュ, 到達した最浅の深さ)。より浅く再到達したときは残りの探索余地が増えるので再展開する。
                var visited = new Dictionary<long, long[]>();
                int? SeenDepth(long h, long h2) => visited.TryGetValue(h, out var v) && v[0] == h2 ? (int)v[1] : null;
                var path = new List<int[]>();   // (i, j, old, new)
                // 最良は正式比較と同じ辞書式で持つ。
                var baseKey = de.ReportKey();
                var bestKey = baseKey;
                List<int[]> bestPath = new();
                var bestDepth = 0;
                var touched = new HashSet<int>();
                var useHole = holeFocus && !(holeSoftOnly && HardFamilies.Contains(seed.Family));
                var famStart = crossFamily ? FamilyWeighted(de) : Array.Empty<long>();

                void Dfs(int depth, int li, int lj)
                {
                    var s = de.Score();
                    if (s / Evaluator.SCORE_HARD_UNIT <= baseHard + cfg.HardSlack)
                    {
                        var key = de.ReportKey();
                        if (DeltaEvaluator.CompareReportKey(key, bestKey) < 0) { bestKey = key; bestPath = path.Select(m => (int[])m.Clone()).ToList(); bestDepth = depth; }
                    }
                    if (depth >= maxDepth || Out() || SeedSpent()) return;
                    // 候補 = [Δ後スコア, i, j, k, i2, j2, k2]。i2<0 は 1 セルの変更、そうでなければ 2 セルの交換。
                    var cand = new List<long[]>();
                    bool Free(int i, int j) => !touched.Contains(i * p.T + j) && !p.WishLocked(i, j);
                    (long, long) KeyAfter(long[] c)
                    {
                        long h = hash, h2 = hash2;
                        var x = 1;
                        while (x < 7 && c[x] >= 0)
                        {
                            int i = (int)c[x], j = (int)c[x + 1], k = (int)c[x + 2];
                            h ^= zob[i][j][work[i][j]] ^ zob[i][j][k]; h2 ^= zob2[i][j][work[i][j]] ^ zob2[i][j][k];
                            x += 3;
                        }
                        return (h, h2);
                    }
                    void Admit(long[] c)
                    {
                        if (HardOf(c[0]) > baseHard + cfg.HardSlack) return;
                        var (h, h2) = KeyAfter(c);
                        var seen = SeenDepth(h, h2);
                        if (seen != null && seen <= depth + 1) { st.Dedup++; return; }
                        st.Candidates++;
                        cand.Add(c);
                    }
                    void Consider(int i, int j)
                    {
                        if (!Free(i, j)) return;
                        var cur = work[i][j];
                        foreach (var k in p.AllowedShiftsForStaff(i))
                        {
                            if (k == cur || p.ExtBanned(i, j, k)) continue;
                            if (Out() || SeedSpent()) return;
                            st.Generated++;
                            var sc = de.PreviewMove(i, j, k); st.Evaluations++;
                            Admit(new[] { sc, i, j, (long)k, -1, -1, -1 });
                        }
                    }
                    // 2 セルを入れ替える手（同日の 2 人／同じ職員の 2 日）。被覆や回数を保ったまま並びだけを動かせる。
                    void ConsiderSwap(int i, int j, int i2, int j2)
                    {
                        var x = work[i][j]; var y = work[i2][j2];
                        if (x == y || !Free(i, j) || !Free(i2, j2) || !p.MayPlace(i, y) || !p.MayPlace(i2, x)) return;
                        if (p.ExtBanned(i, j, y) || p.ExtBanned(i2, j2, x)) return;
                        if (Out() || SeedSpent()) return;
                        st.Generated++;
                        de.Apply(i, j, y); work[i][j] = y;
                        var sc = de.PreviewMove(i2, j2, x);
                        de.Apply(i, j, x); work[i][j] = x;
                        st.Evaluations++;
                        Admit(new[] { sc, i, j, (long)y, i2, j2, (long)x });
                    }
                    int[] ApplyCand(long[] c)
                    {
                        var olds = new[] { -1, -1 };
                        int x = 1, n = 0;
                        while (x < 7 && c[x] >= 0)
                        {
                            int i = (int)c[x], j = (int)c[x + 1], k = (int)c[x + 2];
                            olds[n++] = work[i][j];
                            path.Add(new[] { i, j, work[i][j], k }); touched.Add(i * p.T + j);
                            Move(i, j, k);
                            x += 3;
                        }
                        return olds;
                    }
                    void UndoCand(long[] c, int[] olds)
                    {
                        var x = c[4] >= 0 ? 4 : 1; var n = c[4] >= 0 ? 1 : 0;
                        while (x >= 1)
                        {
                            int i = (int)c[x], j = (int)c[x + 1];
                            Move(i, j, olds[n]); path.RemoveAt(path.Count - 1); touched.Remove(i * p.T + j);
                            x -= 3; n--;
                        }
                    }
                    if (crossFamily)
                    {
                        if (useHole)
                        {
                            // 穴＝動かしたセルの同じ日（被覆の受け皿）と同じ職員の前後の日（並び・期間の制約）。
                            var hole = new bool[p.S * p.T];
                            foreach (var m in path)
                            {
                                int mi = m[0], mj = m[1];
                                for (var s2 = 0; s2 < p.S; s2++) hole[s2 * p.T + mj] = true;
                                for (var d = Math.Max(0, mj - cfg.HoleRadius); d <= Math.Min(p.T - 1, mj + cfg.HoleRadius); d++) hole[mi * p.T + d] = true;
                            }
                            for (var c = 0; c < hole.Length; c++) if (hole[c]) Consider(c / p.T, c % p.T);
                            if (Out() || SeedSpent()) return;
                            if (cfg.SwapMoves)
                            {
                                for (var c = 0; c < hole.Length; c++)
                                {
                                    if (!hole[c]) continue;
                                    int ci = c / p.T, cj = c % p.T;
                                    // 相手も穴なら番号の小さい側からだけ数える（同じ組を二度評価しない）。
                                    for (var b = 0; b < p.S; b++) if (b != ci && (!hole[b * p.T + cj] || b > ci)) ConsiderSwap(ci, cj, b, cj);
                                    for (var d = 0; d < p.T; d++) if (d != cj && (!hole[ci * p.T + d] || d > cj)) ConsiderSwap(ci, cj, ci, d);
                                }
                            }
                        }
                        else
                        {
                            for (var i2 = 0; i2 < p.S; i2++) { for (var j2 = 0; j2 < p.T; j2++) Consider(i2, j2); if (Out() || SeedSpent()) return; }
                            if (cfg.SwapMoves)
                            {
                                for (var j2 = 0; j2 < p.T; j2++) { for (var a = 0; a < p.S; a++) for (var b = a + 1; b < p.S; b++) ConsiderSwap(a, j2, b, j2); if (Out() || SeedSpent()) return; }
                                for (var i2 = 0; i2 < p.S; i2++) { for (var a = 0; a < p.T; a++) for (var b = a + 1; b < p.T; b++) ConsiderSwap(i2, a, i2, b); if (Out() || SeedSpent()) return; }
                            }
                        }
                        cand = cand.OrderBy(c => c, CandOrder).ToList();
                        var famNow = FamilyWeighted(de);
                        var worse = new long[famNow.Length];
                        for (var x = 0; x < worse.Length; x++) worse[x] = Math.Max(0L, famNow[x] - famStart[x]);
                        if (worse.Any(v => v > 0))
                        {
                            // 残存負債が最大の族（同点は族の並び順）を第一に、負債全体の回収量を第二に並べる。
                            var focus = 0;
                            for (var x = 0; x < worse.Length; x++) if (worse[x] > worse[focus]) focus = x;
                            var top = cand.Take(cfg.FamilyProbe).ToList();
                            var gain = new Dictionary<long[], long>(ReferenceEqualityComparer.Instance);
                            var gainFocus = new Dictionary<long[], long>(ReferenceEqualityComparer.Instance);
                            foreach (var c in top)
                            {
                                var olds = ApplyCand(c);
                                var f2 = FamilyWeighted(de);
                                UndoCand(c, olds);
                                var g = 0L;
                                for (var x = 0; x < worse.Length; x++) if (worse[x] > 0) g += Math.Min(worse[x], Math.Max(0L, famNow[x] - f2[x]));
                                gain[c] = g;
                                gainFocus[c] = Math.Min(worse[focus], Math.Max(0L, famNow[focus] - f2[focus]));
                            }
                            cand = top.OrderBy(c => -gainFocus[c]).ThenBy(c => -gain[c]).ThenBy(c => c, CandOrder).ToList();
                        }
                    }
                    else
                    {
                        for (var j2 = 0; j2 < p.T; j2++) if (j2 != lj) Consider(li, j2);
                        for (var i2 = 0; i2 < p.S; i2++) if (i2 != li) Consider(i2, lj);
                        cand = cand.OrderBy(c => c, CandOrder).ToList();
                    }
                    // depth は起点を含む手数（起点＝1）。branching[0] は起点の次の 1 手ぶん。
                    var bLimit = branching[Math.Min(depth - 1, branching.Length - 1)];
                    var taken = 0;
                    foreach (var c in cand)
                    {
                        if (taken >= bLimit || Out() || SeedSpent()) break;
                        var (nh, nh2) = KeyAfter(c);
                        var seen = SeenDepth(nh, nh2);
                        if (seen != null && seen <= depth + 1) { st.Dedup++; continue; }
                        visited[nh] = new[] { nh2, depth + 1L };
                        taken++;
                        st.ChainsTried++;
                        var olds = ApplyCand(c);
                        Dfs(depth + 1, (int)c[1], (int)c[2]);
                        UndoCand(c, olds);
                    }
                }

                var old0 = work[si][sj];
                path.Add(new[] { si, sj, old0, sx }); touched.Add(si * p.T + sj);
                visited[hash] = new[] { hash2, 0L };
                Move(si, sj, sx);
                visited[hash] = new[] { hash2, 1L };
                Dfs(1, si, sj);
                if (SeedSpent()) st.SeedCapped++;
                Move(si, sj, old0);
                path.Clear();
                famStat[1] += st.Evaluations - evalAtSeed;

                if (bestPath.Count == 0 || DeltaEvaluator.CompareReportKey(bestKey, baseKey) >= 0) continue;
                var prev = work.Select(r => (int[])r.Clone()).ToArray();
                foreach (var m in bestPath) Move(m[0], m[1], m[3]);
                var rep2 = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
                var diff = DeltaMismatch(de, rep2);
                if (diff != null)
                {
                    // 差分評価が正式評価と食い違った＝この経路の判断は信用できない。採らずに止め、最後の正式評価済み盤面を返す。
                    st.Mismatch = $"起点={seed.Family}({si},{sj}->{sx}) 手順=" + string.Join(";", bestPath.Select(m => $"{m[0]},{m[1]}:{m[2]}->{m[3]}")) + " " + diff;
                    for (var x = bestPath.Count - 1; x >= 0; x--) Move(bestPath[x][0], bestPath[x][1], bestPath[x][2]);
                    st.EndReason = "差分不一致";
                    break;
                }
                var gate = V6SearchOperators.AdoptionGate(p, prev, work, rep2, rep, pinBlocks).Accepted;
                if (gate && collect != null)
                {
                    // 収集: 採用ゲートを通る手順を渡して元へ戻す（盤面は変えない＝採否はパイプラインの採用キューが決める）。
                    var key = new SeedKey(seed.Family, si, sj, sx);
                    collect(new PathCandidate(key, bestPath.Select(m => (int[])m.Clone()).ToList(), rep2, bestDepth));
                    if (!st.HitSeeds.Contains(key)) st.HitSeeds.Add(key);
                    famStat[2]++;
                    st.AcceptedMaxDepth = Math.Max(st.AcceptedMaxDepth, bestDepth);
                    for (var x = bestPath.Count - 1; x >= 0; x--) Move(bestPath[x][0], bestPath[x][1], bestPath[x][2]);
                }
                else if (gate)
                {
                    famStat[2]++; famStat[3] += (long)(rep.WeightedScore - rep2.WeightedScore);
                    rep = rep2; applied += bestPath.Count; st.Accepted++; improvedThisRound = true;
                    st.AcceptedMaxDepth = Math.Max(st.AcceptedMaxDepth, bestDepth);
                }
                else
                {
                    for (var x = bestPath.Count - 1; x >= 0; x--) Move(bestPath[x][0], bestPath[x][1], bestPath[x][2]);
                }
            }
            lastImproved = improvedThisRound;
            if (!improvedThisRound || st.Mismatch != null) break;
        }
        if (st.EndReason == "完了")
        {
            st.EndReason = lastSeedCount == 0 ? "起点なし"
                : lastImproved ? $"巡上限{cfg.MaxRounds}"
                : unitsSkipped > 0 ? $"改善なし・未巡回{unitsSkipped}箇所"
                : "改善なし";
        }
        if (st.EndReason == "時間切れ") st.Timeouts++;
        var label = softOnly ? "ソフト起点" : all ? "全族起点" : $"期間要件(c1)起点{(crossFamily ? "v2" : "v1")}";
        var famDiff = before.Breakdown.Keys.Concat(rep.Breakdown.Keys).Distinct().OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => { var d = (rep.Breakdown.TryGetValue(f, out var a) ? a : 0) - (before.Breakdown.TryGetValue(f, out var b) ? b : 0); return d != 0 ? $"{f}{(d > 0 ? "+" : "")}{d}" : null; })
            .Where(x => x != null);
        int C1Of(ViolationReport r) => r.Breakdown.TryGetValue("c1", out var v) ? v : 0;
        var logs = new[]
        {
            new MirrorLog(tag: "C1EjectionChain", message:
                $"{label}玉突き連鎖[起点{st.Seeds}/生成{st.Generated}/候補{st.Candidates}/評価{st.Evaluations}/試行{st.ChainsTried}/採用{st.Accepted}/起点上限{st.SeedCapped}/採用深さ最大{st.AcceptedMaxDepth}/重複除外{st.Dedup}/深さ{maxDepth}/終了{st.EndReason}/時間切れ{st.Timeouts}/{EngineClock.NowMs() - t0}ms]: " +
                $"c1 {C1Of(before)}->{C1Of(rep)} score {(long)before.WeightedScore}->{(long)rep.WeightedScore} HARD {before.Hard}->{rep.Hard} total {before.Total}->{rep.Total} 族差 " +
                string.Join(" ", famDiff) +
                " 起点族別[" + string.Join(" ", st.ByFamily.Select(kv => $"{kv.Key}:起点{kv.Value[0]}/評価{kv.Value[1]}/採用{kv.Value[2]}/減{kv.Value[3]}")) + "]" +
                (st.Mismatch != null ? $" 差分不一致: {st.Mismatch}" : "")),
        };
        return new V6HotfixPasses.CyclicSwapResult(work, before.Total, rep.Total, applied, logs, PinBlocks: pinBlocks, Report: rep);
    }

    /// <summary>差分評価と正式評価の全族・必須数の食い違い（一致なら null）。族は名前で突き合わせる。</summary>
    internal static string? DeltaMismatch(DeltaEvaluator de, ViolationReport rep)
    {
        var diffs = new List<string>();
        int Bd(string f) => rep.Breakdown.TryGetValue(f, out var v) ? v : 0;
        foreach (var (f, raw) in de.FamilyRaw()) if (Bd(f) != raw) diffs.Add($"{f} Δ={raw} 正式={Bd(f)}");
        var (lo, hi) = de.RangeRaw();
        if (Bd("low") != lo) diffs.Add($"low Δ={lo} 正式={Bd("low")}");
        if (Bd("high") != hi) diffs.Add($"high Δ={hi} 正式={Bd("high")}");
        if (de.Score() / Evaluator.SCORE_HARD_UNIT != rep.Hard) diffs.Add($"HARD Δ={de.Score() / Evaluator.SCORE_HARD_UNIT} 正式={rep.Hard}");
        return diffs.Count > 0 ? string.Join(", ", diffs) : null;
    }
}
