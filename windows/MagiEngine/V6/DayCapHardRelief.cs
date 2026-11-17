using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [Android 3.727.95] 上限 0 の日を守るせいで残る必須違反を、その日に置いて減らす最終段（Kotlin <c>DayCapHardRelief</c> の写し）。
/// 上限 0 の日のセルを 1 つその値へ変える手と、そのセルの元の値を同じ日の別の職員へ渡す手を試し、必須の件数が減るときだけ採る。
/// </summary>
public static class DayCapHardRelief
{
    public sealed record Result(int[][] Schedule, ViolationReport Report, List<(int I, int J, int K)> Placed);

    public static Result Apply(MagiState state, int[][] schedule, ViolationReport report, long deadlineMs, Func<long>? now = null)
    {
        now ??= EngineClock.NowMs;
        var p = ScheduleUtil.CachedProblem(state);
        var caps = p.DayCapZero;
        if (report.Hard <= 0 || caps is null) return new Result(schedule, report, new());
        var cur = schedule.Copy2D();
        var best = report;
        var placed = new List<(int I, int J, int K)>();
        bool Free(int i, int j) => !p.WishLocked(i, j) && !p.Pinned(i, j);
        while (best.Hard > 0 && now() < deadlineMs)
        {
            (int[][] Sched, ViolationReport Rep)? pick = null;
            (int I, int J, int K) pickCell = default;
            for (var j = 0; j < p.T; j++)
                for (var i = 0; i < p.S; i++)
                {
                    if (now() >= deadlineMs || !Free(i, j)) continue;
                    var old = cur[i][j];
                    foreach (var k in p.AllowedShiftsForStaff(i))
                    {
                        if (k == old || !caps[j * p.K + k] || p.ExtWishBanned(i, j, k)) continue;
                        var tries = new List<int[][]>();
                        var one = cur.Copy2D(); one[i][j] = k; tries.Add(one);
                        for (var i2 = 0; i2 < p.S; i2++)
                        {
                            if (i2 == i || !Free(i2, j) || cur[i2][j] == old || !p.MayPlaceAt(i2, j, old)) continue;
                            var two = cur.Copy2D(); two[i][j] = k; two[i2][j] = old; tries.Add(two);
                        }
                        foreach (var cand in tries)
                        {
                            var r = UnifiedViolationChecker.Check(state, cand);
                            if (r.Hard < best.Hard && (pick is null || UnifiedViolationChecker.BetterReport(r, pick.Value.Rep))) { pick = (cand, r); pickCell = (i, j, k); }
                        }
                    }
                }
            if (pick is null) break;
            cur = pick.Value.Sched; best = pick.Value.Rep; placed.Add(pickCell);
        }
        return new Result(cur, best, placed);
    }
}
