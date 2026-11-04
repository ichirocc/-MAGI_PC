using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [Android 3.727.19 同期] 膜の探索（細胞 LNS、Android <c>docs/membrane_search.md</c> §10・§11）。頭打ちの盤面で、細胞＝職員の集合（個人の違反が重い職員＋同じグループの職員）×全期間を
/// 自由にし、細胞の中だけで同日の入れ替えと 1 セルの置き換えを焼きなましで回す。細胞の最良が盤面より良ければ（<see cref="UnifiedViolationChecker.BetterReport"/>）書き戻す。
/// 希望固定・手動固定（<c>WishLocked</c>）と置けないシフト（<c>MayPlaceAt</c>）は動かさず、書き戻す前に <c>ExactPinRegression</c> も確かめる。
/// </summary>
public static class MembraneCellLns
{
    public sealed record Params(
        long MaxMillis = 8_000L,
        int CellStaff = 5,
        int ItersPerCell = 60_000,
        int MaxCells = 64,
        long Seed = 1L,
        double TempStart = 40.0,
        double TempEnd = 0.5,
        /// <summary>回数で止める（ベンチ・再現用）。true なら MaxMillis を見ない。</summary>
        bool Deterministic = false);

    public sealed record Result(int[][] NewSchedule, int Applied, int CellsTried, IReadOnlyList<MirrorLog> Logs);

    public static Result Apply(MagiState state, int[][] init, Params? prm = null, Func<bool>? shouldStop = null)
    {
        var p0 = prm ?? new Params();
        var stop = shouldStop ?? (() => false);
        var p = ScheduleUtil.CachedProblem(state);
        var t0 = EngineClock.NowMs();
        bool Out() => stop() || (!p0.Deterministic && EngineClock.NowMs() - t0 >= p0.MaxMillis);
        var best = init.Copy2D();
        var bestRep = UnifiedViolationChecker.Check(state, best);
        var rng = new JavaRandom(p0.Seed);
        var de = new DeltaEvaluator(p);
        int applied = 0, tried = 0;
        var startRep = bestRep;

        while (tried < p0.MaxCells && !Out())
        {
            var order = StaffOrder(p, bestRep);
            if (order.Count == 0) break;   // 職員 0 人では tried が進まない
            foreach (var seedStaff in order)
            {
                if (tried >= p0.MaxCells || Out()) break;
                tried++;
                var cell = CellOf(p, seedStaff, order, p0.CellStaff);
                var cand = SolveCell(p, de, best, cell, p0, rng, Out);
                if (cand is null) continue;
                if (V6SearchOperators.ExactPinRegression(p, best, cand)) continue;
                var rep = UnifiedViolationChecker.Check(state, cand);
                if (UnifiedViolationChecker.BetterReport(rep, bestRep))
                {
                    best = cand; bestRep = rep; applied++;
                }
            }
        }
        var logs = new List<MirrorLog>
        {
            new(tag: "MembraneCellLns", message:
                $"膜の細胞 LNS: 必須 {startRep.Hard}->{bestRep.Hard} 合計 {startRep.Total}->{bestRep.Total} 重み {(long)startRep.WeightedScore}->{(long)bestRep.WeightedScore} 採用{applied} 細胞{tried} {EngineClock.NowMs() - t0}ms"),
        };
        return new Result(best, applied, tried, logs);
    }

    /// <summary>OWA の順: 個人の違反（盤面の印の数）が多い職員から。同数は添字順。</summary>
    private static List<int> StaffOrder(Problem p, ViolationReport rep)
    {
        var load = new int[p.S];
        // Kotlin の `violations.keys + countViolations.keys`（集合の和＝同じ文字列の鍵は 1 回）と同じ数え方。
        foreach (var key in rep.Violations.Keys.Union(rep.CountViolations.Keys))
        {
            var c = key.IndexOf(',');
            if (int.TryParse(c < 0 ? key : key[..c], out var i) && i >= 0 && i < p.S) load[i]++;
        }
        return Enumerable.Range(0, p.S).OrderBy(i => -load[i]).ThenBy(i => i).ToList();
    }

    /// <summary>細胞の職員: 起点の職員＋同じグループの職員を OWA 順に、足りなければ他のグループから。</summary>
    private static int[] CellOf(Problem p, int seed, List<int> order, int size)
    {
        var g = p.Sgrp[seed];
        var same = order.Where(i => i != seed && p.Sgrp[i] == g);
        var other = order.Where(i => i != seed && p.Sgrp[i] != g);
        return new[] { seed }.Concat(same).Concat(other).Take(Math.Min(size, p.S)).ToArray();
    }

    /// <summary>細胞の中の焼きなまし。手は (a) 同じ日の細胞内 2 名の入れ替え（日の人数を保つ）、(b) 1 セルを置ける別のシフトへ。改善が無ければ null。</summary>
    private static int[][]? SolveCell(Problem p, DeltaEvaluator de, int[][] board, int[] cell, Params prm, JavaRandom rng, Func<bool> outFn)
    {
        int T = p.T, K = p.K;
        var work = board.Copy2D();
        de.Reset(work);
        var start = de.Score();
        var cur = start;
        var bestScore = start;
        int[][]? bestBoard = null;
        var free = new List<(int I, int J)>();
        foreach (var i in cell) for (var j = 0; j < T; j++) if (!p.WishLocked(i, j)) free.Add((i, j));
        if (free.Count < 2) return null;
        const long unit = Evaluator.SCORE_HARD_UNIT;
        var iters = prm.ItersPerCell;
        var temp = prm.TempStart;
        var cool = Math.Pow(prm.TempEnd / prm.TempStart, 1.0 / Math.Max(iters, 1));
        for (var it = 0; it < iters; it++)
        {
            if ((it & 1023) == 0 && outFn()) break;
            temp *= cool;
            if (rng.NextInt(4) != 0)
            {
                var j = rng.NextInt(T);
                var a = cell[rng.NextInt(cell.Length)]; var b = cell[rng.NextInt(cell.Length)];
                if (a == b || p.WishLocked(a, j) || p.WishLocked(b, j)) continue;
                var ka = work[a][j]; var kb = work[b][j];
                if (ka == kb || !p.MayPlaceAt(a, j, kb) || !p.MayPlaceAt(b, j, ka)) continue;
                de.Apply(a, j, kb);
                var s = de.Apply(b, j, ka);
                if (Accept(s - cur, temp, unit, rng)) { work[a][j] = kb; work[b][j] = ka; cur = s; }
                else { de.Apply(b, j, kb); cur = de.Apply(a, j, ka); }
            }
            else
            {
                var (i, j) = free[rng.NextInt(free.Count)];
                var k = rng.NextInt(K); var old = work[i][j];
                if (k == old || !p.MayPlaceAt(i, j, k)) continue;
                var s = de.Apply(i, j, k);
                if (Accept(s - cur, temp, unit, rng)) { work[i][j] = k; cur = s; } else cur = de.Apply(i, j, old);
            }
            if (cur < bestScore)
            {
                bestScore = cur;
                // 細胞の外の行は盤面のまま＝2 回目からは細胞の行だけ写す（改善のたびに盤面全体を作らない）。
                bestBoard ??= work.Copy2D();
                foreach (var i in cell) Array.Copy(work[i], bestBoard[i], T);
            }
        }
        return bestBoard;
    }

    private static bool Accept(long delta, double temp, long unit, JavaRandom rng)
    {
        if (delta <= 0) return true;
        if (delta >= unit) return false;   // 必須が増える手は取らない
        return rng.NextDouble() < Math.Exp(-delta / temp);
    }
}
