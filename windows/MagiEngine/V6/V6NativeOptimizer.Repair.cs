using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of the destroy/repair operator suite declared inside Kotlin's
/// <c>V6NativeOptimizer</c> object — <c>hf66DataHardening</c>/<c>hf67HardRepair</c> (structural HARD
/// repair, used by <c>RunV5</c>/<c>RunAlnsSingle</c> and, in phase 5c's remaining files,
/// <c>RunRsi</c>/<c>RunRsiPlus</c>), the soft-aware destroy-repair trio
/// (<c>DestroyRepairDayAt</c>/<c>DestroyRepairStaffAt</c>/<c>DestroyRepairViolations</c>, the core of
/// RSI/ALNS hypothesis generation), and <c>Perturb</c> (ALNS restart perturbation).
///
/// [Kotlin原本 3.379.0 のコメント、この移植でも維持] destroy-repair の marginal cost 計算は
/// <c>covUCell</c>（source of truth — need1/need2 の片方定義でも正しく需要を判定する）へ委譲する。
/// 旧世代のいくつかの箇所が <c>need1</c> を直接読んで need2 単独定義の需要を見落としていたバグは、
/// この移植では最初から <c>CovUCell</c> 経由で書くことで再発しない。
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>
    /// [3.428.0/#30] 「担当外セルを何で埋めるか」の規則は <see cref="ScheduleUtil.FillShiftIndex"/>
    /// の1箇所に置く。休が担当可なら休を選び、担当可能が空なら休へ倒す（記号から解決した rest index
    /// を使う＝Level Zero: 全シフト同等・番号非依存）。
    /// </summary>
    internal static int[][] Hf66DataHardening(MagiState state, int[][] schedule, string tag)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var outSched = ScheduleUtil.NormalizeSchedule(schedule, p);
        for (var i = 0; i < p.S; i++)
        {
            var allowed = p.AllowedShiftsForStaff(i);
            // [backlog#24] 休が無い設定は入口で止める（安全側フォールバックの黙認は不可）。
            var fallback = ScheduleUtil.FillShiftIndex(allowed, p.RestIdx ?? throw new ArgumentException("休みシフトが設定されていません"));
            for (var j = 0; j < p.T; j++)
            {
                var k = outSched[i][j];
                // [3.507.0] 個人上限 0 のセル（希望でそのシフトに固定されたものは除く）も入口で外す＝探索は置き直しから始める。
                var capped = k >= 0 && k < p.K && !p.MayPlace(i, k) && !(p.WishLocked(i, j) && p.Wish[i][j] == k);
                if (k < 0 || k >= p.K || !p.CanDo(i, k) || capped) outSched[i][j] = fallback;
            }
        }
        return outSched;
    }

    /// <summary>[3.507.0] 個人上限 0 のセル（希望固定を除く）だけを置けるシフトへ戻した盤面と、その件数。最終番兵の「入力」基準に使う
    /// （群外セルは触らない＝従来の基準のまま）。</summary>
    internal static (int[][] Schedule, int Count) ClearCappedCells(MagiState state, int[][] schedule)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var outSched = schedule.Select(r => r.ToArray()).ToArray();
        var n = 0;
        for (var i = 0; i < p.S; i++)
        {
            // [backlog#24] 同上（休が無い設定はここも入口で止める）。
            var fallback = ScheduleUtil.FillShiftIndex(p.AllowedShiftsForStaff(i), p.RestIdx ?? throw new ArgumentException("休みシフトが設定されていません"));
            for (var j = 0; j < p.T; j++)
            {
                var k = outSched[i][j];
                if (k >= 0 && k < p.K && p.CanDo(i, k) && !p.MayPlace(i, k) && !(p.WishLocked(i, j) && p.Wish[i][j] == k)) { outSched[i][j] = fallback; n++; }
            }
        }
        return (outSched, n);
    }

    internal sealed record RepairResult(int[][] Schedule, IReadOnlyList<MirrorLog> Logs);

    /// <summary>
    /// Structural HARD repair: apply feasible wishes, fill coverage shortages (3 passes,
    /// per-cell OR/AND demand via <see cref="Problem.CovUCell"/>), then fill personal range lower
    /// bounds without touching locked wishes.
    /// </summary>
    internal static RepairResult Hf67HardRepair(MagiState state, int[][] schedule, JavaRandom rng)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var outSched = Hf66DataHardening(state, schedule, "hf67");
        var logs = new List<MirrorLog>();
        var changed = 0;

        // Apply feasible wishes first; infeasible wishes are logged by Sanity, not forced.
        for (var i = 0; i < p.S; i++)
        {
            for (var j = 0; j < p.T; j++)
            {
                var w = p.Wish[i][j];
                if (w >= 0 && w < p.K && p.CanDo(i, w) && outSched[i][j] != w)
                {
                    outSched[i][j] = w;
                    changed++;
                }
            }
        }

        for (var pass = 0; pass < 3; pass++)
        {
            var cov = ScheduleUtil.Coverage(p, outSched);
            var counts = ScheduleUtil.CountMatrix(p, outSched);
            for (var j = 0; j < p.T; j++)
            {
                for (var k = 0; k < p.K; k++)
                {
                    // [N1a] 充填量は per-cell 実需要（#4b: OR/AND）。
                    var miss = p.CovUCell(k, j, cov[j][k]);
                    while (miss > 0)
                    {
                        var i = BestStaffForCoverage(p, outSched, counts, j, k);
                        if (i < 0) break;
                        var old = outSched[i][j];
                        if (old == k) break;
                        outSched[i][j] = k;
                        cov[j][k]++;
                        if (old >= 0 && old < p.K) cov[j][old]--;
                        changed++;
                        miss--;
                    }
                }
            }
        }

        // Range lower bounds: fill shortage where possible without touching locked wishes.
        {
            var counts = ScheduleUtil.CountMatrix(p, outSched);
            for (var i = 0; i < p.S; i++)
            {
                for (var k = 0; k < p.K; k++)
                {
                    var lo = p.RangeLo[i][k];
                    if (lo == int.MinValue || !p.MayPlace(i, k)) continue;
                    var need = lo - counts[i][k];
                    var guard = 0;
                    while (need > 0 && guard++ < p.T)
                    {
                        var bestJ = -1;
                        var bestScore = int.MaxValue;
                        for (var jj = 0; jj < p.T; jj++)
                        {
                            if (p.WishLocked(i, jj) || outSched[i][jj] == k) continue;
                            var score = CoverageShortageCost(p, outSched, jj, outSched[i][jj]) + rng.NextInt(3);
                            if (score < bestScore) { bestScore = score; bestJ = jj; }
                        }
                        if (bestJ < 0) break;
                        var j = bestJ;
                        var old = outSched[i][j];
                        outSched[i][j] = k;
                        if (old >= 0 && old < p.K) counts[i][old]--;
                        counts[i][k]++;
                        changed++;
                        need--;
                    }
                }
            }
        }

        if (changed > 0) logs.Add(new MirrorLog(tag: "HF67", message: $"HardRepair changed={changed}"));
        return new RepairResult(outSched, logs);
    }

    private static int BestStaffForCoverage(Problem p, int[][] schedule, int[][] counts, int j, int k)
    {
        var bestI = -1;
        var bestScore = int.MaxValue;
        for (var i = 0; i < p.S; i++)
        {
            if (!p.MayPlace(i, k)) continue;
            if (p.WishLocked(i, j) && p.Wish[i][j] != k) continue;
            var old = schedule[i][j];
            if (old == k) continue; // [監査#3] 既就業者はスキップ
            var hi = p.RangeHi[i][k];
            var over = hi != int.MaxValue && counts[i][k] >= hi ? 500 : 0;
            var oldNeedCost = CoverageShortageCost(p, schedule, j, old);
            // [監査#12] 引き抜きコストとして加算し、休・過剰被覆側を優先する。
            var score = over + counts[i][k] * 3 + oldNeedCost;
            if (score < bestScore) { bestScore = score; bestI = i; }
        }
        return bestI;
    }

    private static int CoverageShortageCost(Problem p, int[][] schedule, int j, int k)
    {
        if (k < 0 || k >= p.K) return 0;
        var cov = 0;
        for (var i = 0; i < p.S; i++) if (schedule[i][j] == k) cov++;
        // [N1a] 引き抜きで per-cell 実需要が増える＝不足を生む職員はコスト50。
        return p.CovUCell(k, j, cov - 1) > p.CovUCell(k, j, cov) ? 50 : 0;
    }

    private static void DestroyRepairDay(MagiState state, int[][] schedule, JavaRandom rng)
    {
        var p = ScheduleUtil.CachedProblem(state);
        if (p.T == 0) return;
        DestroyRepairDayAt(state, schedule, rng.NextInt(p.T), rng);
    }

    /// <summary>[soft-aware repair] 割当 i→shift k の per-staff soft(low/high/apt, checker と同一式)を count n で評価。</summary>
    internal static long StaffCountPenaltyAt(Problem p, int i, int k, int n)
    {
        var pen = 0L;
        var lo = p.RangeLo[i][k];
        var hi = p.RangeHi[i][k];
        // [3.319.0] low は担当できるシフトだけ。[3.522.0] low 90→120。
        if (lo != int.MinValue && lo != 0 && n < lo && p.CanDo(i, k)) pen += (lo - n) * 120L;
        if (hi != int.MaxValue && n > hi) pen += (n - hi) * 25L;
        var t = p.Apt[i][k];
        if (t >= 0) pen += Math.Abs(n - t) * 4L; // [3.522.0] apt 1→4
        return pen;
    }

    /// <summary>
    /// [3.267.0/weekly+fair統合] weekly(7日周期のシフト平準化)の marginal cost。wd は staff のシフト別
    /// 曜日カウント([K][7]、呼出元が維持)。[3.345.0] oldK→newK のシフト移動を受け、動くのは oldK と
    /// newK の2バケットだけ（oldK==newK は 0）。wd 自体は変更しない(コミットは呼出元)。[3.522.0] 重み1→2。
    /// </summary>
    internal static long WeeklyMarginalAt(int[][] wd, int bucket, int oldK, int newK)
    {
        if (oldK == newK) return 0L;
        var acc = 0L;
        if (oldK >= 0 && oldK < wd.Length)
        {
            var b = wd[oldK];
            var before = ScheduleUtil.WeeklyDevOfBucket(b);
            b[bucket]--;
            acc += ScheduleUtil.WeeklyDevOfBucket(b) - before;
            b[bucket]++;
        }
        if (newK >= 0 && newK < wd.Length)
        {
            var b = wd[newK];
            var before = ScheduleUtil.WeeklyDevOfBucket(b);
            b[bucket]++;
            acc += ScheduleUtil.WeeklyDevOfBucket(b) - before;
            b[bucket]--;
        }
        return acc * 2L; // [3.522.0] weekly 1→2
    }

    /// <summary>
    /// [3.538.0] fair(グループ内公平化)の marginal cost。staff i の shift k 保有回数が delta 変化した際の
    /// <see cref="ScheduleUtil.FairDevOfBucket"/>（群 g=p.Sgrp[i]・シフト k）の変化。m&lt;2・k が群の担当外
    /// なら 0。counts は呼出元が維持するS×K集計（達成率モードは個々人の回数が要るため、旧実装のG×K群合計
    /// grpTotal は不要）。
    /// </summary>
    internal static long FairMarginalAt(Problem p, int i, int k, int delta, int[][] counts)
    {
        if (delta == 0 || k < 0 || k >= p.K) return 0L;
        var g = p.Sgrp[i];
        if (p.GroupMembers[g].Length < 2 || !p.Bucket[g].Contains(k)) return 0L;
        var before = p.FairDevOfBucket(g, k, x => counts[x][k]).Total;
        counts[i][k] += delta;
        var after = p.FairDevOfBucket(g, k, x => counts[x][k]).Total;
        counts[i][k] -= delta;
        return (after - before) * 2L; // [3.522.0] fair 1→2（WeeklyMarginalAtと同型で内部適用）
    }

    /// <summary>
    /// [soft-aware destroy-repair] 非希望セルを休へ destroy → 各需要を「割当の marginal soft が最小の
    /// 休スタッフ」で repair。休→k のみ移すため被覆穴を新たに作らない。希望固定は保持。
    /// </summary>
    internal static void DestroyRepairDayAt(MagiState state, int[][] schedule, int j, JavaRandom rng)
    {
        var p = ScheduleUtil.CachedProblem(state);
        if (p.T == 0) return;
        // [backlog#24] 休シフト未設定なら休へ寄せるdestroy自体が無意味＝no-op
        if (ScheduleUtil.RestShiftIndex(state) is not int rest) return;
        var cnt = new int[p.S][];
        for (var i = 0; i < p.S; i++)
        {
            cnt[i] = new int[p.K];
            for (var jj = 0; jj < p.T; jj++) { var k = schedule[i][jj]; if (k >= 0 && k < p.K) cnt[i][k]++; }
        }
        // destroy: 非希望セルを休へ。休を担当できない職員は対象外（群外割当を作らない）。cnt も同期。
        for (var i = 0; i < p.S; i++)
        {
            if (p.WishLocked(i, j) || !p.MayPlace(i, rest)) continue;
            var old = schedule[i][j];
            if (old != rest && old >= 0 && old < p.K) { schedule[i][j] = rest; cnt[i][old]--; cnt[i][rest]++; }
        }
        var covJ = new int[p.K];
        for (var i = 0; i < p.S; i++) { var k = schedule[i][j]; if (k >= 0 && k < p.K) covJ[k]++; }

        // [c41-aware] 群の「日次人数レンジ(cons41)」も marginal に加味し、群レンジ(上下限)も同時に研磨する。
        var hasC41 = p.Cons41.Count > 0;
        var grpCnt = hasC41 ? new int[p.G][] : Array.Empty<int[]>();
        if (hasC41)
        {
            for (var g = 0; g < p.G; g++) grpCnt[g] = new int[p.K];
            for (var i = 0; i < p.S; i++) { var k = schedule[i][j]; if (k >= 0 && k < p.K) grpCnt[p.Sgrp[i]][k]++; }
        }

        long C41DayMarg(int g, int k)
        {
            if (!hasC41) return 0L;
            var d = 0L;
            foreach (var c in p.Cons41)
            {
                if (c.GroupIdx != g || c.ShiftIdx != k) continue;
                var z = grpCnt[g][k];
                var z1 = z + 1;
                var before = (z < c.L ? c.L - z : 0) + (z > c.U ? z - c.U : 0);
                var after = (z1 < c.L ? c.L - z1 : 0) + (z1 > c.U ? z1 - c.U : 0);
                d += after - before;
            }
            return d;
        }

        // [3.267.0/weekly統合] 職員別曜日バケット(weekly)を一度だけ構築（destroy後のschedule基準＝c41の
        // grpCntと同じ順序）。day j は固定のため bucket は全候補共通。
        var wd = new int[p.S][][];
        for (var s = 0; s < p.S; s++)
        {
            var a = new int[p.K][];
            for (var k = 0; k < p.K; k++) a[k] = new int[7];
            for (var jj = 0; jj < p.T; jj++) { var k2 = schedule[s][jj]; if (k2 >= 0 && k2 < p.K) a[k2][(p.Dow0 + jj) % 7]++; }
            wd[s] = a;
        }
        var bucket = (p.Dow0 + j) % 7;

        // repair: 各勤務シフトの需要を soft(個人 low/high/apt/weekly/fair ＋ 群レンジ c41)最小の休スタッフで満たす。
        for (var k = 0; k < p.K; k++)
        {
            if (k == rest) continue; // [監査#2] 休以外の全シフトを対象
            var miss = p.CovUCell(k, j, covJ[k]);
            if (miss <= 0) continue;
            while (miss > 0)
            {
                var bestI = -1;
                var bestDelta = long.MaxValue;
                var tied = 0;
                for (var i = 0; i < p.S; i++)
                {
                    if (schedule[i][j] != rest || p.WishLocked(i, j) || !p.MayPlace(i, k)) continue;
                    var delta = StaffCountPenaltyAt(p, i, k, cnt[i][k] + 1) - StaffCountPenaltyAt(p, i, k, cnt[i][k]) +
                        C41DayMarg(p.Sgrp[i], k) +
                        WeeklyMarginalAt(wd[i], bucket, rest, k) +
                        FairMarginalAt(p, i, rest, -1, cnt) +
                        FairMarginalAt(p, i, k, 1, cnt);
                    if (delta < bestDelta) { bestDelta = delta; bestI = i; tied = 1; }
                    else if (delta == bestDelta)
                    {
                        tied++;
                        if (HypothesisDiversityPolicy.TakeReservoirTie(tied, rng)) bestI = i;
                    }
                }
                if (bestI < 0) break;
                schedule[bestI][j] = k; cnt[bestI][k]++; cnt[bestI][rest]--; covJ[k]++; miss--;
                if (hasC41) grpCnt[p.Sgrp[bestI]][k]++;
                wd[bestI][rest][bucket]--; wd[bestI][k][bucket]++;
            }
        }
    }

    private static void DestroyRepairStaff(MagiState state, int[][] schedule, JavaRandom rng)
    {
        var p = ScheduleUtil.CachedProblem(state);
        if (p.S == 0) return;
        DestroyRepairStaffAt(state, schedule, rng.NextInt(p.S), rng);
    }

    /// <summary>
    /// [soft-aware staff-DR] 非希望セルを休へ destroy → 各日の被覆穴を「staff i の marginal soft
    /// 最小のシフト」で repair。被覆穴のみ埋める(過剰=covO を作らない)。希望固定は保持。
    /// </summary>
    internal static void DestroyRepairStaffAt(MagiState state, int[][] schedule, int i, JavaRandom rng)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var allowed = p.AllowedShiftsForStaff(i);
        if (allowed.Length == 0) return;
        // [backlog#24] 休シフト未設定ならno-op
        if (ScheduleUtil.RestShiftIndex(state) is not int rest) return;
        if (!p.MayPlace(i, rest)) return; // 休を担当できない職員は破壊修復の対象外

        var counts = new int[p.S][];
        for (var s = 0; s < p.S; s++)
        {
            var a = new int[p.K];
            for (var jj = 0; jj < p.T; jj++) { var k = schedule[s][jj]; if (k >= 0 && k < p.K) a[k]++; }
            counts[s] = a;
        }
        var cntI = counts[i];
        var wd = new int[p.K][];
        for (var k = 0; k < p.K; k++) wd[k] = new int[7];
        for (var jj = 0; jj < p.T; jj++) { var k2 = schedule[i][jj]; if (k2 >= 0 && k2 < p.K) wd[k2][(p.Dow0 + jj) % 7]++; }

        for (var j = 0; j < p.T; j++)
        {
            if (p.WishLocked(i, j)) continue;
            var old = schedule[i][j];
            if (old != rest && old >= 0 && old < p.K)
            {
                schedule[i][j] = rest;
                cntI[old]--; cntI[rest]++;
                wd[old][(p.Dow0 + j) % 7]--; wd[rest][(p.Dow0 + j) % 7]++;
            }
        }

        // [高速化] 被覆を一度だけ数え(O(S×T))、割当のたびに差分更新する(O(T×K))。
        var cov = new int[p.T][];
        for (var j = 0; j < p.T; j++) cov[j] = new int[p.K];
        for (var x = 0; x < p.S; x++) for (var j = 0; j < p.T; j++) { var k2 = schedule[x][j]; if (k2 >= 0 && k2 < p.K) cov[j][k2]++; }

        for (var j = 0; j < p.T; j++)
        {
            if (p.WishLocked(i, j) || schedule[i][j] != rest) continue;
            var bucket = (p.Dow0 + j) % 7;
            var bestK = -1;
            var bestDelta = long.MaxValue;
            var tied = 0;
            for (var k = 0; k < p.K; k++)
            {
                if (k == rest || !p.MayPlace(i, k)) continue;
                if (p.CovUCell(k, j, cov[j][k]) <= 0) continue;
                var delta = StaffCountPenaltyAt(p, i, k, cntI[k] + 1) - StaffCountPenaltyAt(p, i, k, cntI[k]) +
                    WeeklyMarginalAt(wd, bucket, rest, k) +
                    FairMarginalAt(p, i, rest, -1, counts) +
                    FairMarginalAt(p, i, k, 1, counts);
                if (delta < bestDelta) { bestDelta = delta; bestK = k; tied = 1; }
                else if (delta == bestDelta)
                {
                    tied++;
                    if (HypothesisDiversityPolicy.TakeReservoirTie(tied, rng)) bestK = k;
                }
            }
            if (bestK >= 0)
            {
                schedule[i][j] = bestK;
                cntI[bestK]++; cntI[rest]--;
                wd[rest][bucket]--; wd[bestK][bucket]++;
                cov[j][bestK]++; cov[j][rest]--;
            }
        }
    }

    /// <summary>
    /// [soft-aware violations] 違反セルを、staff i の現状回数で marginal soft(old→k)最小のシフトへ
    /// 再割当(従来はランダム)。件数は最大8回に限られ盤面規模も小さいため、毎回の再走査を許容する。
    /// </summary>
    private static void DestroyRepairViolations(MagiState state, int[][] schedule, ViolationReport report, JavaRandom rng)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var keys = report.Violations.Keys.ToList();
        if (keys.Count == 0) { RandomAllowedCell(state, schedule, rng); return; }
        var reps = Math.Min(8, keys.Count);
        for (var rep = 0; rep < reps; rep++)
        {
            var key = keys[rng.NextInt(keys.Count)];
            var parts = key.Split(',');
            var i = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
            var j = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
            if (i is null || j is null) continue;
            if (i.Value < 0 || i.Value >= p.S || j.Value < 0 || j.Value >= p.T || p.WishLocked(i.Value, j.Value)) continue;
            var allowed = p.AllowedShiftsForStaff(i.Value);
            if (allowed.Length == 0) continue;

            var cntI = new int[p.K];
            for (var jj = 0; jj < p.T; jj++) { var k = schedule[i.Value][jj]; if (k >= 0 && k < p.K) cntI[k]++; }
            var wd = new int[p.K][];
            for (var k = 0; k < p.K; k++) wd[k] = new int[7];
            for (var jj = 0; jj < p.T; jj++) { var k2 = schedule[i.Value][jj]; if (k2 >= 0 && k2 < p.K) wd[k2][(p.Dow0 + jj) % 7]++; }
            var counts = new int[p.S][];
            for (var s = 0; s < p.S; s++)
            {
                var a = new int[p.K];
                for (var jj = 0; jj < p.T; jj++) { var k = schedule[s][jj]; if (k >= 0 && k < p.K) a[k]++; }
                counts[s] = a;
            }
            var bucket = (p.Dow0 + j.Value) % 7;
            var old = schedule[i.Value][j.Value];
            var bestK = old;
            var bestDelta = long.MaxValue;
            var tied = 0;
            foreach (var k in allowed)
            {
                if (k == old) continue;
                var dOld = old >= 0 && old < p.K
                    ? StaffCountPenaltyAt(p, i.Value, old, cntI[old] - 1) - StaffCountPenaltyAt(p, i.Value, old, cntI[old])
                    : 0L;
                var dK = StaffCountPenaltyAt(p, i.Value, k, cntI[k] + 1) - StaffCountPenaltyAt(p, i.Value, k, cntI[k]);
                var dWeekly = WeeklyMarginalAt(wd, bucket, old, k);
                var dFair = (old >= 0 && old < p.K ? FairMarginalAt(p, i.Value, old, -1, counts) : 0L) +
                    FairMarginalAt(p, i.Value, k, 1, counts);
                var delta = dOld + dK + dWeekly + dFair;
                if (delta < bestDelta) { bestDelta = delta; bestK = k; tied = 1; }
                else if (delta == bestDelta)
                {
                    tied++;
                    if (HypothesisDiversityPolicy.TakeReservoirTie(tied, rng)) bestK = k;
                }
            }
            if (bestK != old) schedule[i.Value][j.Value] = bestK;
        }
    }

    private static void RandomAllowedCell(MagiState state, int[][] schedule, JavaRandom rng)
    {
        var p = ScheduleUtil.CachedProblem(state);
        if (p.S == 0 || p.T == 0) return;
        var i = rng.NextInt(p.S);
        var j = rng.NextInt(p.T);
        if (p.WishLocked(i, j)) return;
        var allowed = p.AllowedShiftsForStaff(i);
        if (allowed.Length > 0) schedule[i][j] = allowed[rng.NextInt(allowed.Length)];
    }

    private static int[][] Perturb(MagiState state, int[][] baseSched, JavaRandom rng, double strength)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var outSched = baseSched.Copy2D();
        var n = Math.Max(1, (int)(p.S * p.T * strength));
        for (var rep = 0; rep < n; rep++) RandomAllowedCell(state, outSched, rng);
        return outSched;
    }

    // DestroyRepairStaffReps は V6NativeOptimizer.Sizing.cs に定義済み（純粋な反復回数ヘルパー）。
}
