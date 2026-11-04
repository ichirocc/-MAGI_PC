using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [Android 同名、docs/seesaw.md §6.3 の S3] 後処理の結果（本命）から「他の案」の各盤面へ、違うセルを違反セルから先に 1 つずつ写して歩く
/// Path Relinking。途中の盤面が <see cref="UnifiedViolationChecker.BetterReport"/> で本命より良く固定（厳密ピン）も破らなければ本命を差し替え、
/// 必須の件数が同じで本命に優越されない盤面は交換の候補として残す（S4 の「他の案」）。既定 OFF（<see cref="PolishGate.SeesawRelink"/>）。
/// </summary>
public static class SeesawRelink
{
    public sealed record Result(int[][] Schedule, ViolationReport Report, int Applied, IReadOnlyList<int[][]> Tradeoffs, IReadOnlyList<MirrorLog> Logs);

    public const int MaxTradeoffs = 3;

    /// <summary>a が b に優越する（21 族すべて a ≤ b、どれかで &lt;）。重みを使わない比較。</summary>
    public static bool Dominates(ViolationReport a, ViolationReport b)
    {
        bool strict = false;
        foreach (var f in MirrorKeys.All)
        {
            int x = a.Breakdown.GetValueOrDefault(f), y = b.Breakdown.GetValueOrDefault(f);
            if (x > y) return false;
            if (x < y) strict = true;
        }
        return strict;
    }

    static bool SameBreakdown(ViolationReport a, ViolationReport b) =>
        MirrorKeys.All.All(f => a.Breakdown.GetValueOrDefault(f) == b.Breakdown.GetValueOrDefault(f));

    public static Result Apply(MagiState state, int[][] best0, IReadOnlyList<int[][]> alternatives, long maxMillis = 3_000L, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = ScheduleUtil.CachedProblem(state);
        var t0 = EngineClock.NowMs();
        bool Out() => stop() || EngineClock.NowMs() - t0 >= maxMillis;
        var best = best0.Copy2D();
        var bestRep = UnifiedViolationChecker.Check(state, best);
        int applied = 0;
        var pool = new List<(int[][] Board, ViolationReport Rep)>();
        foreach (var alt in alternatives)
        {
            if (Out()) break;
            if (alt.Length != best.Length || alt.Any(r => r.Length != p.T)) continue;
            var start = best.Copy2D();
            var cur = best.Copy2D();
            var vcells = new HashSet<long>();
            foreach (var k in bestRep.Violations.Keys)
            {
                var parts = k.Split(',');
                if (parts.Length >= 2 && int.TryParse(parts[0], out var vi) && int.TryParse(parts[1], out var vj)) vcells.Add((long)vi * p.T + vj);
            }
            var diffs = new List<(int I, int J)>();
            for (int i = 0; i < cur.Length; i++) for (int j = 0; j < cur[i].Length; j++) if (cur[i][j] != alt[i][j]) diffs.Add((i, j));
            diffs = diffs.OrderBy(d => vcells.Contains((long)d.I * p.T + d.J) ? 0 : 1).ToList();
            foreach (var (i, j) in diffs)
            {
                if (Out()) break;
                if (p.WishLocked(i, j) && alt[i][j] != p.LockTo(i, j)) continue;
                if (p.ExtBanned(i, j, alt[i][j])) continue;
                cur[i][j] = alt[i][j];
                var rep = UnifiedViolationChecker.Check(state, cur);
                if (V6SearchOperators.ExactPinRegression(p, start, cur)) continue;
                if (UnifiedViolationChecker.BetterReport(rep, bestRep)) { best = cur.Copy2D(); bestRep = rep; applied++; }
                else if (rep.Hard == bestRep.Hard && !Dominates(bestRep, rep) && !SameBreakdown(rep, bestRep)) pool.Add((cur.Copy2D(), rep));
            }
        }
        var tradeoffs = PickTradeoffs(bestRep, pool);
        var logs = new List<MirrorLog> { new(tag: "SeesawRelink",
            message: $"シーソーの Path Relinking: 案 {alternatives.Count}・採用 {applied}・交換の候補 {tradeoffs.Count}（集めた {pool.Count}）・重み {(long)bestRep.WeightedScore}") };
        return new Result(best, bestRep, applied, tradeoffs.Select(t => t.Board).ToList(), logs);
    }

    /// <summary>交換の候補を選ぶ（Android の pickTradeoffs と同じ順・同じ規則）。</summary>
    internal static List<(int[][] Board, ViolationReport Rep)> PickTradeoffs(ViolationReport best, IReadOnlyList<(int[][] Board, ViolationReport Rep)> pool)
    {
        var ok = pool.Where(c => c.Rep.Hard == best.Hard && !Dominates(best, c.Rep) && !SameBreakdown(c.Rep, best)).ToList();
        var nd0 = ok.Where(c => !ok.Any(o => !ReferenceEquals(o.Rep, c.Rep) && Dominates(o.Rep, c.Rep))).ToList();
        var seenVec = new HashSet<string>();
        var nd = new List<(int[][] Board, ViolationReport Rep)>();
        foreach (var c in nd0)
            if (seenVec.Add(string.Join(",", MirrorKeys.All.Select(f => c.Rep.Breakdown.GetValueOrDefault(f))))) nd.Add(c);
        nd = nd.OrderBy(c => c.Rep.WeightedScore).ToList();
        var picked = new List<(int[][] Board, ViolationReport Rep)>();
        var seen = new HashSet<string>();
        foreach (var c in nd)
        {
            var better = string.Join(",", MirrorKeys.All.Where(f => c.Rep.Breakdown.GetValueOrDefault(f) < best.Breakdown.GetValueOrDefault(f)));
            if (seen.Add(better)) picked.Add(c);
            if (picked.Count >= MaxTradeoffs) break;
        }
        foreach (var c in nd) { if (picked.Count >= MaxTradeoffs) break; if (!picked.Contains(c)) picked.Add(c); }
        return picked;
    }
}
