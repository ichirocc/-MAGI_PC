using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [フェーズ6, ピース20] Kotlin原本 <c>applyC1WindowPolish</c> の次に位置する広域ビーム研磨
    /// <c>applyC1BeamPolish</c>（<c>V6HotfixPasses.kt</c> 3.340.0 由来）の忠実な移植。
    ///
    /// C1（窓の要件, 重み30）の不足セルを起点に、同日交換 or <see cref="V6SearchOperators.FindCovUChain"/>
    /// による玉突き連鎖のいずれかで1手ずつ埋める複数手の探索を、ビーム幅 <paramref name="beamWidth"/> で
    /// 保持しながら <paramref name="maxSteps"/> 回まで展開する（多段の C1 解消を単発の
    /// <c>applyC1WindowPolish</c> より広く探す位置づけ）。
    ///
    /// [3.409.24で是正] <c>bestEver</c> の更新は「目的関数上の改善」と「厳密ピンを崩さない」の両方を
    /// 満たすときのみ行う（<see cref="V6SearchOperators.PinBlockAttribution.BlocksImproving"/>）。
    /// [3.409.27でさらに是正] 停滞カウンタ <c>stagnant</c> はピン判定と切り離し、目的関数上の改善
    /// （<c>improved</c>）のみで判定する——ピンで弾いた回まで停滞に数えると、探索を短くするという
    /// 別の退化を持ち込むため（Kotlin原本コメント参照）。返る盤面は旧実装(=root)に対し常に非劣。
    /// </summary>
    public static CyclicSwapResult ApplyC1BeamPolish(
        MagiState state, int[][] schedule, int beamWidth = 16, int maxSteps = 60,
        Func<bool>? shouldStop = null, long seed = 0x1CBEAL, int patience = 20)
    {
        var stop = shouldStop ?? (() => false);
        long beamT0 = EngineClock.NowMs();
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work0 = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work0);

        if (p.Cons1.Count == 0)
        {
            return new CyclicSwapResult(work0, before.Total, before.Total, 0,
                new List<MirrorLog> { new MirrorLog(tag: "C1BeamPolish", message: "cons1なし=スキップ") });
        }

        var rng = new JavaRandom(seed);

        bool Movable(int i, int j) => !p.WishLocked(i, j);

        bool C1Deficient(int[][] work, int i2, int x, int day)
        {
            if (day < 0 || day >= p.T) return false;
            foreach (var c in p.Cons1)
            {
                if (c.ShiftIdx != x || c.Day1 <= 0) continue;
                if (InDeficientC1Window(p, work, i2, x, c.Day1, c.Day2, day)) return true;
            }
            return false;
        }

        List<(int Ci, int I, int J)> RebuildTargets(int[][] work)
        {
            var outList = new List<(int Ci, int I, int J)>();
            for (int ci = 0; ci < p.Cons1.Count; ci++)
            {
                var c = p.Cons1[ci];
                int x = c.ShiftIdx, d = c.Day1, n = c.Day2;
                if (x < 0 || x >= p.K || d <= 0) continue;
                for (int i = 0; i < p.S; i++)
                {
                    if (!p.CanDo(i, x)) continue;
                    for (int j = 0; j < p.T; j++)
                    {
                        if (work[i][j] == x || !Movable(i, j)) continue;
                        if (InDeficientC1Window(p, work, i, x, d, n, j)) outList.Add((ci, i, j));
                    }
                }
            }
            return outList;
        }

        int[][]? TryOneMove(int[][] baseWork, int i, int j, int x)
        {
            var w = baseWork.Copy2D();
            int a0 = w[i][j];
            for (int i2 = 0; i2 < p.S; i2++)
            {
                if (i2 == i || w[i2][j] != x || !Movable(i2, j) || !p.CanDo(i2, a0)) continue;
                w[i][j] = x; w[i2][j] = a0;
                return w;
            }
            w[i][j] = x;
            var chain = V6SearchOperators.FindCovUChain(p, w, a0, j, rng, exclude: i,
                c1Pref: (s2, sh, dy) => C1Deficient(w, s2, sh, dy));
            if (chain == null) return w;
            foreach (var mv in chain) w[mv[0]][mv[1]] = mv[2];
            return w;
        }

        var beam = new List<Beam> { new Beam(work0, before, 0) };
        Beam? bestEver = null;
        int stagnant = 0;
        int step = 0;
        while (step < maxSteps)
        {
            if (stop()) break;
            bool anyExpanded = false;
            var nextCandidates = new List<Beam>();
            foreach (var b in beam)
            {
                var targets = RebuildTargets(b.Work);
                if (targets.Count == 0) { nextCandidates.Add(b); continue; }
                var tryList = targets.Count <= beamWidth * 2
                    ? targets
                    : targets.Shuffled(rng).Take(beamWidth * 2).ToList();
                foreach (var (ci, i, j) in tryList)
                {
                    if (stop()) break;
                    int x = p.Cons1[ci].ShiftIdx;
                    var w2 = TryOneMove(b.Work, i, j, x);
                    if (w2 == null) continue;
                    var rep2 = UnifiedViolationChecker.Check(state, w2);
                    if (rep2.Hard > before.Hard) continue;
                    nextCandidates.Add(new Beam(w2, rep2, b.Applied + 1));
                    anyExpanded = true;
                }
            }
            if (!anyExpanded) break;
            beam = nextCandidates
                .Distinct(BeamScheduleComparer.Instance)
                .OrderBy(cand => cand.Rep, UnifiedViolationChecker.ReportComparer)
                .Take(beamWidth)
                .ToList();
            var top = beam.Count > 0 ? beam[0] : null;
            if (top != null)
            {
                var be = bestEver;
                bool improved = be == null || UnifiedViolationChecker.BetterReport(top.Rep, be.Rep);
                bool blocked = improved && IsBetter(top.Rep, before) &&
                    pinBlocks.BlocksImproving(p, work0, top.Work);
                if (improved && !blocked) bestEver = top;
                if (improved) stagnant = 0; else stagnant++;
            }
            step++;
            if (stagnant >= patience) break;
        }

        var candidate = bestEver
            ?? beam.OrderBy(b => b.Rep, UnifiedViolationChecker.ReportComparer).FirstOrDefault()
            ?? new Beam(work0, before, 0);
        var best = IsBetter(candidate.Rep, before) && !pinBlocks.BlocksImproving(p, work0, candidate.Work)
            ? candidate
            : new Beam(work0, before, 0);

        var c1Before = before.Breakdown.TryGetValue("c1", out var cb) ? cb : 0;
        var c1After = best.Rep.Breakdown.TryGetValue("c1", out var ca) ? ca : 0;
        var patienceNote = stagnant >= patience ? $"/最良が{patience}手更新されず打ち切り" : "";
        var discardNote = best.Applied == 0 && !ReferenceEquals(candidate, best) && candidate.Applied > 0
            ? " [探索結果が根に勝てず破棄]" : "";
        var elapsedMs = EngineClock.NowMs() - beamT0;
        var message = $"期間要件(c1)研磨[ビーム K={beamWidth} steps={step}/{elapsedMs}ms{patienceNote}]: " +
            $"c1 {c1Before}->{c1After} / total {before.Total}->{best.Rep.Total} " +
            $"score {(long)before.WeightedScore}->{(long)best.Rep.WeightedScore} " +
            $"HARD {before.Hard}->{best.Rep.Hard} 手数{best.Applied}{discardNote}";
        var logs = new List<MirrorLog> { new MirrorLog(tag: "C1BeamPolish", message: message) };

        return new CyclicSwapResult(best.Work, before.Total, best.Rep.Total, best.Applied, logs,
            PinBlocks: pinBlocks);
    }

    private sealed record Beam(int[][] Work, ViolationReport Rep, int Applied);

    /// <summary>ビーム各段の重複排除（盤面の同値は <see cref="ScheduleEqualityComparer"/>。Distinct は最初の出現順を保つ＝並びも不変）。</summary>
    private sealed class BeamScheduleComparer : IEqualityComparer<Beam>
    {
        public static readonly BeamScheduleComparer Instance = new();
        public bool Equals(Beam? x, Beam? y) => ReferenceEquals(x, y) || (x is not null && y is not null && ScheduleEqualityComparer.Instance.Equals(x.Work, y.Work));
        public int GetHashCode(Beam obj) => ScheduleEqualityComparer.Instance.GetHashCode(obj.Work);
    }
}
