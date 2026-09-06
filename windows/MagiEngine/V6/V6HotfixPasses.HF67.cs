using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [フェーズ6, ピース23] Kotlin原本 <c>HF67Result</c>（top-level data class, 29行）の忠実な移植。
    /// 個人回数(low/high)の不足↔超過を職員間の同日シフト交換で埋める <see cref="ApplyHF67InterStaffSwap"/>
    /// の戻り値。
    /// </summary>
    public sealed record HF67Result(
        int[][] NewSchedule,
        int BeforeTotal,
        int AfterTotal,
        int SwapsApplied,
        int ShortageSwaps,
        int CapacitySwaps,
        int SwapsRollback,
        IReadOnlyList<MirrorLog> Logs);

    /// <summary>2職員間の同日シフト交換1手（<c>fromStaff</c>の<c>fromDay</c>を<c>toStaff</c>の<c>toDay</c>と
    /// 入れ替える）を表す。<see cref="ApplyHF67InterStaffSwap"/>専用の内部候補型。</summary>
    private sealed record SwapCandidate(int FromStaff, int FromDay, int ToStaff, int ToDay);

    /// <summary>
    /// <paramref name="from"/>職員の<paramref name="shift"/>在勤日の1つと、<paramref name="to"/>職員の
    /// 非在勤かつ入替可能な日の1つを見つけ次第、その1組で交換した盤面を返す（最良探索ではなく最初に
    /// 見つかった実行可能な組で即返す）。実行可能な組が無ければ <c>null</c>。
    /// </summary>
    private static (int[][] Schedule, SwapCandidate Swap)? TrySwapShiftBetweenStaff(
        Problem p, int[][] schedule, int from, int to, int shift)
    {
        var fromDays = new List<int>();
        var toDays = new List<int>();
        for (var j = 0; j < p.T; j++)
        {
            if (schedule[from][j] == shift && !p.WishLocked(from, j)) fromDays.Add(j);
            if (schedule[to][j] != shift && !p.WishLocked(to, j) && p.MayPlace(to, shift) && p.MayPlace(from, schedule[to][j]))
                toDays.Add(j);
        }
        foreach (var jf in fromDays)
        {
            foreach (var jt in toDays)
            {
                var cand = schedule.Copy2D();
                var tmp = cand[from][jf];
                cand[from][jf] = cand[to][jt];
                cand[to][jt] = tmp;
                return (cand, new SwapCandidate(from, jf, to, jt));
            }
        }
        return null;
    }

    /// <summary>
    /// <see cref="ApplyHF67InterStaffSwap"/>の本ループが1手も採用しなかった場合のフォールバック。
    /// 全職員ペア×全日を総当たりし、担当可能かつ希望非固定なセルどうしの単純な2者交換を貪欲に
    /// keep-best で採用していく（最も原始的だが確実な手）。
    /// </summary>
    private static (int[][] Schedule, int Applied, int Rollback) LocalPairwiseStaffSwap(
        MagiState state, int[][] schedule, int maxSwaps, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var work = schedule.Copy2D();
        var current = UnifiedViolationChecker.Check(state, work);
        var applied = 0;
        var rollback = 0;
        for (var i = 0; i < p.S; i++)
        {
            for (var i2 = i + 1; i2 < p.S; i2++)
            {
                for (var j = 0; j < p.T; j++)
                {
                    if (applied >= maxSwaps || stop()) goto LoopDone;
                    if (p.WishLocked(i, j) || p.WishLocked(i2, j)) continue;
                    var a = work[i][j];
                    var b = work[i2][j];
                    if (a == b || !p.MayPlace(i, b) || !p.MayPlace(i2, a)) continue;
                    var cand = work.Copy2D();
                    cand[i][j] = b;
                    cand[i2][j] = a;
                    var rep = UnifiedViolationChecker.Check(state, cand);
                    if (IsBetter(rep, current))
                    {
                        work = cand;
                        current = rep;
                        applied++;
                    }
                    else
                    {
                        rollback++;
                    }
                }
            }
        }
        LoopDone:
        return (work, applied, rollback);
    }

    /// <summary>
    /// [フェーズ6, ピース23] Kotlin原本 <c>applyHF67InterStaffSwap</c>（<c>V6HotfixPasses.kt</c> 3.282.0
    /// 由来）の忠実な移植。個人回数(low=下限割れ/high=上限超過, 重み90/45)の解消を、下限割れの職員
    /// (<c>to</c>)へ上限超過の職員(<c>from</c>)から同日シフトを譲る交換として探索する。
    ///
    /// 各ラウンド、全シフト<c>k</c>を走査し「その<c>k</c>でlow/highに該当する職員の全組合せ」から
    /// 最も report を改善する1手を選び採用する（ラウンドをまたいで running best を保持するのではなく、
    /// 各ラウンド開始時に <c>current</c> を基準にゼロから最良を探し直す）。1手も見つからなければ
    /// <see cref="LocalPairwiseStaffSwap"/> へフォールバックする。
    ///
    /// [3.282.0/新領域ログ監査] 兄弟の HF66 は専用上限(<paramref name="deadlineMs"/>)＋内側スキャンの
    /// <c>OutOfTime</c> 確認（2.65.0/3.161.0の確立方針）を持つのに、HF67 だけ手ごとの
    /// <paramref name="shouldStop"/> のみ＝候補ごとフル check の内側スキャン（k×lows×highs）と
    /// フォールバック（全ペア×全日の総当たり）が締切後も走り切る非対称だった。同型の締切確認を
    /// 追加（keep-best のため途中中断でも退化なし）。
    /// </summary>
    public static HF67Result ApplyHF67InterStaffSwap(
        MagiState state, int[][] schedule, int maxSwaps = 30, Func<bool>? shouldStop = null, long deadlineMs = long.MaxValue)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var current = before;
        var swaps = 0;
        var shortage = 0;
        var capacity = 0;
        var rollback = 0;
        bool OutOfTime() => stop() || EngineClock.NowMs() >= deadlineMs;

        while (swaps < maxSwaps)
        {
            if (OutOfTime()) break;
            var counts = ScheduleUtil.CountMatrix(p, work);
            SwapCandidate? best = null;
            ViolationReport? bestReport = null;
            for (var k = 0; k < p.K; k++)
            {
                if (OutOfTime()) goto ScanDone;
                var lows = new List<int>();
                var highs = new List<int>();
                for (var i = 0; i < p.S; i++)
                {
                    if (p.CanDo(i, k) && p.RangeLo[i][k] != int.MinValue && counts[i][k] < p.RangeLo[i][k]) lows.Add(i);
                    if (counts[i][k] > EffectiveHi(p, i, k)) highs.Add(i);
                }
                foreach (var to in lows)
                {
                    if (OutOfTime()) goto ScanDone;
                    foreach (var from in highs)
                    {
                        if (to == from) continue;
                        var cand = TrySwapShiftBetweenStaff(p, work, from, to, k);
                        if (cand == null) continue;
                        var rep = UnifiedViolationChecker.Check(state, cand.Value.Schedule);
                        var refRep = bestReport ?? current;
                        if (IsBetter(rep, refRep))
                        {
                            best = cand.Value.Swap;
                            bestReport = rep;
                        }
                    }
                }
            }
            ScanDone:
            if (best == null || bestReport == null) break;
            var b = best;
            var next = work.Copy2D();
            var tmp = next[b.FromStaff][b.FromDay];
            next[b.FromStaff][b.FromDay] = next[b.ToStaff][b.ToDay];
            next[b.ToStaff][b.ToDay] = tmp;
            work = next;
            current = bestReport;
            swaps++;
            shortage++;
            if (current.Soft < before.Soft) capacity++;
        }
        if (swaps == 0 && !OutOfTime())
        {
            var improved = LocalPairwiseStaffSwap(state, work, maxSwaps, OutOfTime);
            work = improved.Schedule;
            swaps = improved.Applied;
            rollback = improved.Rollback;
            current = UnifiedViolationChecker.Check(state, work);
            capacity = swaps;
        }
        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "HF67",
                message: $"inter-staff swap applied={swaps} rollback={rollback} total {before.Total}->{current.Total}"),
        };
        return new HF67Result(work, before.Total, current.Total, swaps, shortage, capacity, rollback, logs);
    }
}
