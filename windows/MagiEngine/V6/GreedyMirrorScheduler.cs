using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>GreedyMirrorScheduler</c> object — the older, simpler ("mirror
/// app"-derived) schedule generator, kept as a test-only cross-check against
/// <see cref="SmartInitialScheduler"/> per the migration plan's explicit note ("後者もテスト用
/// クロスチェック生成器として移植する価値あり").
///
/// [重要・意図的な違い — 統一しない] Unlike <see cref="SmartInitialScheduler"/> (always builds
/// from a blank slate, per the 3.261.0 bugfix), this generator toggles between "既存表ベース"
/// (keep the input schedule as-is, normalized) and "空表ベース" (build from blank) depending on
/// how much of the input is already filled. This is deliberately preserved as-is — it is the
/// documented reason this generator produces a *different* (and, for an already-mostly-filled
/// input, worse) result than <see cref="SmartInitialScheduler"/>, which several ported unit
/// tests assert on directly (see <c>GreedyMirrorSchedulerTest</c>/<c>SmartInitialSchedulerTest</c>
/// port, phase 4). Also unlike <see cref="SmartInitialScheduler"/>, this generator does not
/// consider C1 (window requirement) constraints at all during construction — hence "greedy".
/// </summary>
public static class GreedyMirrorScheduler
{
    public static ScheduleRunResult Generate(MagiState state)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = new Problem(state);
        if (p.T <= 0 || p.S <= 0 || p.K <= 0)
            throw new ArgumentException("期間/職員/シフトが不足しています");
        int restK = ScheduleUtil.RestShiftIndex(state);
        var existing = state.Schedule.ToIntArray2D();
        int filled = 0;
        foreach (var row in existing) foreach (var v in row) if (v >= 0) filled++;

        int[][] schedule;
        string baseMode;
        int wishIn = 0, wishOut = 0;
        if (filled >= Math.Max(1, p.S * p.T / 2))
        {
            schedule = ScheduleUtil.NormalizeSchedule(existing, p);
            baseMode = "既存表ベース";
        }
        else
        {
            schedule = new int[p.S][];
            for (int i = 0; i < p.S; i++)
            {
                schedule[i] = new int[p.T];
                for (int j = 0; j < p.T; j++) schedule[i][j] = -1;
            }
            baseMode = "空表ベース";
            for (int i = 0; i < p.S; i++)
            {
                for (int j = 0; j < p.T; j++)
                {
                    int w = p.Wish[i][j];
                    if (w < 0 || w >= p.K) continue;
                    // [3.391.0/実バグ回帰] 旧実装は担当できないシフトへの希望まで盤面へ置いていた。
                    //   pref は実現可能な希望しか数えないため置いても得は無い一方、担当外セル＝
                    //   groupViol(HARD 10000)が確実に立つ＝純損。SmartInitialScheduler と同じくcanDoで守る。
                    if (p.CanDo(i, w)) { schedule[i][j] = w; wishIn++; } else wishOut++;
                }
            }
        }

        var counts = ScheduleUtil.CountMatrix(p, schedule);
        for (int i = 0; i < p.S; i++)
        {
            var allowed = p.AllowedShiftsForStaff(i);
            var free = new List<int>();
            for (int jj = 0; jj < p.T; jj++) if (schedule[i][jj] < 0) free.Add(jj);
            int pos = 0;
            foreach (int k in allowed)
            {
                int lo = p.RangeLo[i][k] != int.MinValue ? p.RangeLo[i][k] : 0;
                int need = Math.Max(0, lo - counts[i][k]);
                while (need > 0 && pos < free.Count)
                {
                    int j = free[pos++];
                    schedule[i][j] = k;
                    counts[i][k]++;
                    need--;
                }
            }
        }

        // [need2単独定義セル見落とし修正] need1のみでなくcovUCell(need1/need2のOR、source of truth)を使う
        //   （SmartInitialSchedulerと同一パターンで同時修正）。
        counts = ScheduleUtil.CountMatrix(p, schedule);
        var cov = ScheduleUtil.Coverage(p, schedule);
        for (int j = 0; j < p.T; j++)
        {
            var demandOrder = new List<(int Deficit, int K)>();
            for (int k = 0; k < p.K; k++)
            {
                int deficit = p.CovUCell(k, j, cov[j][k]);
                if (deficit > 0) demandOrder.Add((deficit, k));
            }
            demandOrder.Sort((a, b) =>
            {
                int d = b.Deficit.CompareTo(a.Deficit);
                return d != 0 ? d : a.K.CompareTo(b.K);
            });
            foreach (var (_, k) in demandOrder)
            {
                while (p.CovUCell(k, j, cov[j][k]) > 0)
                {
                    int bestI = -1, bestPenalty = int.MaxValue;
                    for (int i = 0; i < p.S; i++)
                    {
                        if (schedule[i][j] >= 0 || !p.CanDo(i, k)) continue;
                        int hi = p.RangeHi[i][k];
                        bool over = hi != int.MaxValue && counts[i][k] >= hi;
                        int penalty = (over ? 1000 : 0) + counts[i][k] * 2;
                        if (penalty < bestPenalty) { bestPenalty = penalty; bestI = i; }
                    }
                    if (bestI < 0) break;
                    schedule[bestI][j] = k;
                    counts[bestI][k]++;
                    cov[j][k]++;
                }
            }
        }

        counts = ScheduleUtil.CountMatrix(p, schedule);
        for (int i = 0; i < p.S; i++)
        {
            var allowed = p.AllowedShiftsForStaff(i);
            for (int j = 0; j < p.T; j++)
            {
                if (schedule[i][j] >= 0) continue;
                int bestK = allowed.Length > 0 ? allowed[0] : restK;
                int bestPenalty = int.MaxValue;
                foreach (int k in allowed)
                {
                    int hi = p.RangeHi[i][k];
                    bool over = hi != int.MaxValue && counts[i][k] >= hi;
                    int covNow = 0;
                    for (int ii = 0; ii < p.S; ii++) if (schedule[ii][j] == k) covNow++;
                    // [need2単独定義セル見落とし修正] SmartInitialSchedulerと同根・同時修正。
                    int demandBonus = p.CovUCell(k, j, covNow) > 0 ? -100 : 0;
                    // [3.345.0] 休は通常のシフト種の一つ＝残り埋めで優先しない（旧: 休だけ -10 のボーナス）。
                    //   実データ3件で hard/covO/covU/low/high/c1 が全て同一＝この優先は実質不活性だった。
                    int penalty = (over ? 1000 : 0) + counts[i][k] + demandBonus;
                    if (penalty < bestPenalty) { bestPenalty = penalty; bestK = k; }
                }
                schedule[i][j] = bestK;
                counts[i][bestK]++;
            }
        }

        var report = UnifiedViolationChecker.Check(state, schedule);
        long elapsedMs = sw.ElapsedMilliseconds;
        var log = new MirrorLog(
            tag: "GenerateInitial",
            message: $"簡易作成完了({baseMode}): HARD={report.Hard} total={report.Total} " +
                $"希望seed={wishIn}件/担当外={wishOut}件 ({elapsedMs}ms)");
        var logs = new List<MirrorLog> { log };
        logs.AddRange(report.Logs);
        return new ScheduleRunResult(schedule, report with { Logs = logs });
    }
}
