using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [3.540.0/Android測定済み・既定OFF 移植元] 回数連鎖研磨: 個人上限/群目標の超過を起点に、同日の被覆保存巡回交換(k=2/3)を
/// 複数日で束ね、回数族＋連続系の負債が正味 ≤0 の連鎖だけを <see cref="UnifiedViolationChecker"/> で keep-best 採点する
/// （採用基準は増やさない）。1 日ずつ採否を見る既存パスが取れない「相手に渡して別の日で返す」迂回を狙う
/// （根拠＝厳密解との比較、Android docs/history 3.540.0）。[3.541.1] 人員過剰(covO)セルも起点に含める。
/// </summary>
internal static class CountChainPolish
{
    public sealed record Config(
        int MaxPasses = 8,
        /// <summary>連鎖の最大日数（＝巡回交換の数）。</summary>
        int MaxDepth = 6,
        /// <summary>途中で許す回数族(low/high/apt)の負債（重み付き、起点比）。超える枝は展開しない。</summary>
        double MaxDebt = 60.0,
        /// <summary>返済中の枝に限って許す負債の上限（休の厳密ピン 120 ＋ 相手の上限超過 25 程度を通す）。</summary>
        double MaxLoanDebt = 300.0,
        /// <summary>1 ノードで展開する子（巡回交換）の上限。open が減る順に採り、同点は seed で並べる。</summary>
        int MaxChildren = 32,
        int MaxEvaluations = 3000,
        int MaxNodes = 150_000,
        long MaxMillis = 8_000L,
        int MaxTargets = 12);

    /// <summary>(k, j) に必要数の指定が無い（need1/need2 とも空欄）＝1 セル増減しても covU/covO が動かない。</summary>
    private static bool FreeCoverage(Problem p, int k, int j) => p.Need1[k][j] < 0 && (!p.Use2 || p.Need2[k][j] < 0);

    /// <summary>同日 j の巡回交換。staff[t] が oldShift[t] → newShift[t] へ変わる（被覆は不変）。</summary>
    private sealed class Rotation
    {
        public readonly int Day;
        public readonly int[] Staff;
        public readonly int[] OldShift;
        public readonly int[] NewShift;
        public Rotation(int day, int[] staff, int[] oldShift, int[] newShift) { Day = day; Staff = staff; OldShift = oldShift; NewShift = newShift; }
    }

    /// <summary>起点。Day&gt;=0 なら人員過剰(covO)セル＝その日に staff が shift を手放す手から始める（回数超過の起点は Day=-1）。</summary>
    private sealed class Target
    {
        public readonly int Staff, Shift, Excess, Day;
        public readonly double Weight;
        public Target(int staff, int shift, int excess, double weight, int day = -1) { Staff = staff; Shift = shift; Excess = excess; Weight = weight; Day = day; }
    }

    public static V6HotfixPasses.CyclicSwapResult ApplyCountChainPolish(
        MagiState state,
        int[][] schedule,
        Config? config = null,
        Func<bool>? shouldStop = null,
        long seed = 0xC0C4L,
        bool quantitativeRangeEval = false)
    {
        var cfg = config ?? new Config();
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        var bestRep = before;
        int applied = 0;
        int evaluations = 0;
        int nodes = 0;
        long deadline = DateTime.UtcNow.Ticks + cfg.MaxMillis * TimeSpan.TicksPerMillisecond;
        var rng = new JavaRandom(seed);
        var allowed = new bool[p.S][];
        for (int i = 0; i < p.S; i++)
        {
            var a = new bool[p.K];
            foreach (var k in p.AllowedShiftsForStaff(i)) if (k >= 0 && k < p.K) a[k] = true;
            allowed[i] = a;
        }
        bool Stopped() => stop() || DateTime.UtcNow.Ticks >= deadline || evaluations >= cfg.MaxEvaluations || nodes >= cfg.MaxNodes;
        bool Movable(int i, int j) => !p.WishLocked(i, j);

        List<Target> TargetsOf(int[][] counts)
        {
            var outList = new List<Target>();
            for (int i = 0; i < p.S; i++)
                for (int k = 0; k < p.K; k++)
                {
                    int c = counts[i][k];
                    int hi = p.RangeHi[i][k];
                    int apt = p.Apt[i][k];
                    if (hi != int.MaxValue && c > hi) outList.Add(new Target(i, k, c - hi, MirrorKeys.WeightOf("high") * (c - hi)));
                    else if (apt >= 0 && c > apt) outList.Add(new Target(i, k, c - apt, MirrorKeys.WeightOf("apt") * (c - apt)));
                }
            // 人員過剰(covO)セル: 過剰の日にそのシフトを持つ人（非希望）ごとに起点を立てる。
            double wCovO = MirrorKeys.WeightOf("covO");
            for (int j = 0; j < p.T; j++)
                for (int k = 0; k < p.K; k++)
                {
                    int got = 0;
                    for (int i = 0; i < p.S; i++) if (work[i][j] == k) got++;
                    int over = p.CovOCell(k, j, got);
                    if (over <= 0) continue;
                    for (int i = 0; i < p.S; i++) if (work[i][j] == k && Movable(i, j)) outList.Add(new Target(i, k, over, wCovO * over, day: j));
                }
            return outList.OrderByDescending(t => t.Weight).Take(cfg.MaxTargets).ToList();
        }

        double wLow = MirrorKeys.WeightOf("low"), wHigh = MirrorKeys.WeightOf("high"), wApt = MirrorKeys.WeightOf("apt");
        /// (s,k) の回数が c のときの回数族(low/high/apt)の重み付き罰則。連鎖の途中経過を採点する「回数の負債」の単位。
        double Pen(int s, int k, int c)
        {
            double v = 0.0;
            if (p.RangeLo[s][k] != int.MinValue && c < p.RangeLo[s][k]) v += wLow * (p.RangeLo[s][k] - c);
            if (p.RangeHi[s][k] != int.MaxValue && c > p.RangeHi[s][k]) v += wHigh * (c - p.RangeHi[s][k]);
            if (p.Apt[s][k] >= 0) v += wApt * Math.Abs(c - p.Apt[s][k]);
            return v;
        }

        double wC1 = MirrorKeys.WeightOf("c1"), wC3 = MirrorKeys.WeightOf("c3"), wC3m = MirrorKeys.WeightOf("c3m"), wC3mn = MirrorKeys.WeightOf("c3mn");
        /// 職員 s の行だけで決まる連続系(c1/c3/c3m/c3mn)の重み付き罰則。ViolationChecker と同じ数え方（窓・run-deficit）。
        double SeqPenRow(int s, int[] row)
        {
            double v = 0.0;
            foreach (var c in p.Cons1)
            {
                if (!p.CanDo(s, c.ShiftIdx) || c.Day1 > p.T) continue;
                int z = 0;
                for (int l = 0; l < c.Day1; l++) if (row[l] == c.ShiftIdx) z++;
                int j = 0;
                while (j <= p.T - c.Day1)
                {
                    if (j > 0) { if (row[j - 1] == c.ShiftIdx) z--; if (row[j + c.Day1 - 1] == c.ShiftIdx) z++; }
                    if (z < c.Day2) v += wC1;
                    j++;
                }
            }
            void Fam(IReadOnlyList<C3> list, bool forbidden, double w)
            {
                foreach (var c in list)
                {
                    var seq = c.Seq; int d = seq.Length;
                    if (d == 0 || d > p.T) continue;
                    if (!forbidden && C3Run.IsSingleShiftSeq(seq))
                    {
                        int r = 0, j2 = 0;
                        while (j2 <= p.T)
                        {
                            bool on = j2 < p.T && row[j2] == seq[0];
                            if (on) r++;
                            else if (r > 0) { if (d - r > 0) v += w * (d - r); r = 0; }
                            j2++;
                        }
                        continue;
                    }
                    int j = 0;
                    while (j <= p.T - d)
                    {
                        if (row[j] == seq[0])
                        {
                            int z = 0;
                            for (int l = 1; l < d; l++) if (row[j + l] == seq[l]) z++;
                            if (forbidden ? z == d - 1 : z < d - 1) v += w;
                        }
                        j++;
                    }
                }
            }
            Fam(p.Cons3, false, wC3); Fam(p.Cons3m, false, wC3m); Fam(p.Cons3mn, true, wC3mn);
            return v;
        }

        /// 起点 target について連鎖を探索し、見つかった最良盤面を返す（無ければ null）。
        (int[][] Board, ViolationReport Report)? SearchChain(Target target, int[][] counts)
        {
            var cur = work.Copy2D();
            var delta = new Dictionary<int, int>();
            var usedDay = new bool[p.T];
            int[][]? bestBoard = null;
            var bestChainRep = bestRep;
            int Key(int s, int k) => s * p.K + k;
            var seqBase = new double[p.S];
            for (int s0 = 0; s0 < p.S; s0++) seqBase[s0] = SeqPenRow(s0, cur[s0]);
            var seqCur = (double[])seqBase.Clone();
            double EntryDebt(int s, int k, int v) => Pen(s, k, counts[s][k] + v) - Pen(s, k, counts[s][k]);
            /// 回数族の負債 ＋ 連続系の負債（行単位で厳密）＋ covO 起点なら過剰セルを外した分の利得。fair/weekly/c42 系は最終採点で決まる。
            double Debt()
            {
                double d = 0.0;
                foreach (var (kk, v) in delta) if (v != 0) d += EntryDebt(kk / p.K, kk % p.K, v);
                for (int s = 0; s < p.S; s++) d += seqCur[s] - seqBase[s];
                if (target.Day >= 0 && cur[target.Staff][target.Day] != target.Shift) d -= MirrorKeys.WeightOf("covO");
                return d;
            }
            /// 次に直す (s,k)＝負債が最大の項。起点がまだ動いていなければ起点。
            (int, int)? WorstOpen()
            {
                if (!delta.TryGetValue(Key(target.Staff, target.Shift), out var v0) || v0 == 0) return (target.Staff, target.Shift);
                (int, int)? best = null; double bestD = 0.0;
                foreach (var (kk, v) in delta)
                {
                    if (v == 0) continue;
                    double d = EntryDebt(kk / p.K, kk % p.K, v);
                    if (d > bestD) { bestD = d; best = (kk / p.K, kk % p.K); }
                }
                return best;
            }
            void Apply(Rotation r, int sign)
            {
                for (int t = 0; t < r.Staff.Length; t++)
                {
                    int s = r.Staff[t];
                    int from = sign > 0 ? r.OldShift[t] : r.NewShift[t];
                    int to = sign > 0 ? r.NewShift[t] : r.OldShift[t];
                    cur[s][r.Day] = to;
                    delta[Key(s, from)] = delta.GetValueOrDefault(Key(s, from)) - 1;
                    delta[Key(s, to)] = delta.GetValueOrDefault(Key(s, to)) + 1;
                }
                usedDay[r.Day] = sign > 0;
                foreach (var s in r.Staff) seqCur[s] = SeqPenRow(s, cur[s]);
            }
            /// (s,k) の負債を減らす同日巡回交換を列挙する。give=true なら s が k を手放す、false なら受け取る。
            List<Rotation> Children(int s, int k, bool give, int onlyDay = -1)
            {
                var outList = new List<Rotation>();
                for (int j = 0; j < p.T; j++)
                {
                    if (onlyDay >= 0 && j != onlyDay) continue;
                    if (usedDay[j] || !Movable(s, j)) continue;
                    int sk = cur[s][j];
                    if (give != (sk == k)) continue;
                    // 被覆に必要数が無いシフト（B4/有など need 空欄の日）へは相手なしの 1 セル変換で手放せる／受け取れる。
                    for (int k2 = 0; k2 < p.K; k2++)
                    {
                        if (k2 == sk || !allowed[s][k2]) continue;
                        int to = give ? k2 : k;
                        int from = sk;
                        if (give && !FreeCoverage(p, to, j)) continue;
                        if (!give && !FreeCoverage(p, from, j)) continue;
                        if (!give && to != k) continue;
                        if (p.MakesForbiddenRun(cur, s, j, to)) continue;
                        outList.Add(new Rotation(j, new[] { s }, new[] { from }, new[] { to }));
                        if (!give) break;
                    }
                    for (int b = 0; b < p.S; b++)
                    {
                        if (b == s || !Movable(b, j)) continue;
                        int bk = cur[b][j];
                        if (bk == sk) continue;
                        if (!give && bk != k) continue;
                        if (allowed[s][bk] && allowed[b][sk] && !p.MakesForbiddenRun(cur, s, j, bk) && !p.MakesForbiddenRun(cur, b, j, sk))
                            outList.Add(new Rotation(j, new[] { s, b }, new[] { sk, bk }, new[] { bk, sk }));
                        if (!allowed[s][bk] || p.MakesForbiddenRun(cur, s, j, bk)) continue;
                        for (int c = 0; c < p.S; c++)
                        {
                            if (c == s || c == b || !Movable(c, j)) continue;
                            int ck = cur[c][j];
                            if (ck == sk || ck == bk) continue;
                            if (!allowed[b][ck] || !allowed[c][sk]) continue;
                            if (p.MakesForbiddenRun(cur, b, j, ck) || p.MakesForbiddenRun(cur, c, j, sk)) continue;
                            outList.Add(new Rotation(j, new[] { s, b, c }, new[] { sk, bk, ck }, new[] { bk, ck, sk }));
                        }
                    }
                }
                return outList;
            }
            int evalCap = evaluations + Math.Max(200, cfg.MaxEvaluations / Math.Max(1, cfg.MaxTargets));
            void Dfs(int depth, int maxDepth)
            {
                if (Stopped() || evaluations >= evalCap) return;
                nodes++;
                double d = Debt();
                // 回数族が正味で悪化していない連鎖だけ正式採点する（他族は checker が決める）。
                if (depth > 0 && d <= 0.0)
                {
                    evaluations++;
                    var rep = UnifiedViolationChecker.Check(state, cur, quantitativeRangeEval);
                    if (UnifiedViolationChecker.BetterReport(rep, bestChainRep))
                    {
                        if (V6SearchOperators.ExactPinRegression(p, work, cur)) pinBlocks.Record(p, work, cur);
                        else { bestChainRep = rep; bestBoard = cur.Copy2D(); }
                    }
                }
                if (depth >= maxDepth) return;
                var open = WorstOpen();
                if (open == null) return;
                var (s, k) = open.Value;
                bool give = delta.GetValueOrDefault(Key(s, k)) >= 0;
                bool atRoot = s == target.Staff && k == target.Shift && delta.GetValueOrDefault(Key(s, k)) == 0;
                var cands = Children(s, k, give, onlyDay: atRoot ? target.Day : -1);
                if (cands.Count == 0) return;
                // 負債が小さくなる順、同点は乱択（seed で決定的）。
                var scored = cands.Select(r => { Apply(r, +1); double nd = Debt(); Apply(r, -1); return (Nd: nd, Rand: rng.NextInt(int.MaxValue), R: r); })
                    .OrderBy(x => x.Nd).ThenBy(x => x.Rand).ToList();
                int taken = 0;
                foreach (var (nd, _, r) in scored)
                {
                    if (taken >= cfg.MaxChildren || Stopped() || evaluations >= evalCap) break;
                    // 負債の上限。超えていても「返済中」（負債が減る枝）だけは maxLoanDebt まで追う＝厳密ピン（休 lo=hi）の
                    //   日付け替えのように、一度大きく借りてすぐ返す 2 手を通すため。
                    if (nd > cfg.MaxLoanDebt || (nd > cfg.MaxDebt && nd >= d)) continue;
                    taken++;
                    Apply(r, +1);
                    Dfs(depth + 1, maxDepth);
                    Apply(r, -1);
                }
            }
            // 反復深化: 浅い連鎖から順に探し、見つかった深さで止める（深さ優先が深い枝で予算を使い切るのを防ぐ）。
            for (int dmax = 1; dmax <= cfg.MaxDepth; dmax++)
            {
                Dfs(0, dmax);
                if (bestBoard != null || Stopped() || evaluations >= evalCap) break;
            }
            return bestBoard == null ? null : (bestBoard, bestChainRep);
        }

        // 1 連鎖を採用するたびに回数が変わるので起点を取り直す。maxPasses は採用連鎖数の上限。
        while (applied < cfg.MaxPasses && !Stopped())
        {
            var counts = ScheduleUtil.CountMatrix(p, work);
            bool improved = false;
            foreach (var t in TargetsOf(counts))
            {
                if (Stopped()) break;
                var found = SearchChain(t, counts);
                if (found == null) continue;
                for (int i = 0; i < p.S; i++) Array.Copy(found.Value.Board[i], work[i], p.T);
                bestRep = found.Value.Report;
                applied++;
                improved = true;
                break;
            }
            if (!improved) break;
        }
        var logs = new[]
        {
            new MirrorLog(tag: "CountChainPolish", message: $"回数連鎖研磨: total {before.Total}->{bestRep.Total} 採用{applied}連鎖 " +
                $"high {before.Breakdown.GetValueOrDefault("high")}->{bestRep.Breakdown.GetValueOrDefault("high")} " +
                $"apt {before.Breakdown.GetValueOrDefault("apt")}->{bestRep.Breakdown.GetValueOrDefault("apt")} " +
                $"評価{evaluations} ノード{nodes}"),
        };
        return new V6HotfixPasses.CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
