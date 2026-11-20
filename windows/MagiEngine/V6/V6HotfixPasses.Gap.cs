using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// シフトの間隔（gap）の研磨（Kotlin <c>GapPolish</c>、Android 3.727.110）。被覆保存の 2 職員×2 日 長方形交換で、
    /// 職員 i の日 d の X と相手 i' の日 d' の X を入れ替える（各日の各シフト人数と i・i' の X の回数は変わらない）。
    /// 起点は間隔が足りないかたまりの組のセル。採否は他の研磨と同じ keep-best。
    /// </summary>
    public static CyclicSwapResult ApplyGapPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, bool quantitativeRangeEval = false)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        if (p.ConsGap.Count == 0 || before.Breakdown.GetValueOrDefault("gap") == 0)
            return new CyclicSwapResult(work, before.Total, before.Total, 0, Array.Empty<MirrorLog>(), Report: before);
        var pinBlocks = new PinBlockAttribution();
        var bestRep = before;
        var applied = 0;
        var tried = 0;
        bool Movable(int i, int j) => !p.WishLocked(i, j);

        List<int> AnchorDays(int[] row, int x, int n)
        {
            var output = new List<int>();
            var seen = new HashSet<int>();
            int prevStart = -1, prevEnd = -1;
            var j = 0;
            while (j < p.T)
            {
                if (row[j] != x) { j++; continue; }
                var s = j;
                while (j < p.T && row[j] == x) j++;
                var e = j - 1;
                if (prevEnd >= 0 && s - prevEnd < n)
                {
                    for (var d = prevStart; d <= prevEnd; d++) if (seen.Add(d)) output.Add(d);
                    for (var d = s; d <= e; d++) if (seen.Add(d)) output.Add(d);
                }
                prevStart = s; prevEnd = e;
            }
            return output;
        }

        var pass = 0;
        while (pass < maxPasses && !stop())
        {
            var improved = false;
            foreach (var c in p.ConsGap)
            {
                var x = c.ShiftIdx;
                for (var i = 0; i < p.S; i++)
                {
                    if (stop()) break;
                    if (!p.C1Applies(i, x)) continue;
                    var done = false;
                    foreach (var d in AnchorDays(work[i], x, c.Days))
                    {
                        if (done || stop()) break;
                        if (work[i][d] != x || !Movable(i, d)) continue;
                        for (var dp = 0; dp < p.T; dp++)
                        {
                            if (done || stop()) break;
                            var y = work[i][dp];
                            if (dp == d || y == x || y < 0 || y >= p.K || !Movable(i, dp)) continue;
                            for (var ip = 0; ip < p.S; ip++)
                            {
                                if (ip == i || !Movable(ip, d) || !Movable(ip, dp) || work[ip][dp] != x) continue;
                                var z = work[ip][d];
                                if (z == x || z < 0 || z >= p.K) continue;
                                if (!p.MayPlace(i, z) || !p.MayPlace(ip, y)) continue;
                                if (p.ExtBanned(i, d, z) || p.ExtBanned(i, dp, x) || p.ExtBanned(ip, d, x) || p.ExtBanned(ip, dp, y)) continue;
                                tried++;
                                var prev = work.Copy2D();
                                work[i][d] = z; work[i][dp] = x; work[ip][d] = x; work[ip][dp] = y;
                                var rep = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
                                if (V6SearchOperators.AdoptionGate(p, prev, work, rep, bestRep, pinBlocks).Accepted)
                                {
                                    bestRep = rep; applied++; improved = true; done = true; break;
                                }
                                work[i][d] = x; work[i][dp] = y; work[ip][d] = z; work[ip][dp] = x;
                            }
                        }
                    }
                }
            }
            pass++;
            if (!improved) break;
        }
        var logs = new[]
        {
            new MirrorLog(tag: "GapPolish",
                message: $"シフトの間隔(gap)研磨(長方形交換): gap {before.Breakdown.GetValueOrDefault("gap")}->{bestRep.Breakdown.GetValueOrDefault("gap")} / total {before.Total}->{bestRep.Total} 採用{applied}回 試行{tried}"),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks, Report: bestRep);
    }
}
