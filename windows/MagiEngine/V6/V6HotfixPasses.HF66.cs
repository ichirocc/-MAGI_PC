using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [フェーズ6, ピース24] Kotlin原本 <c>HF66Result</c>（top-level data class, 40-49行）の忠実な移植。
    /// 個人回数(low/high)の不足↔超過を**同一職員内**のシフト付け替えで埋める
    /// <see cref="ApplyHF66IntraStaffRedistribution"/> の戻り値。
    /// </summary>
    public sealed record HF66Result(
        int[][] NewSchedule,
        int BeforeTotal,
        int AfterTotal,
        int MovesApplied,
        int ShortageMoves,
        int CapacityMoves,
        int MovesRollback,
        IReadOnlyList<MirrorLog> Logs);

    /// <summary>同一職員<c>Staff</c>の<c>Day</c>のシフトを<c>FromShift</c>から<c>ToShift</c>へ付け替える1手。
    /// <see cref="ApplyHF66IntraStaffRedistribution"/>専用の内部候補型。</summary>
    private sealed record MoveCandidate(int Staff, int Day, int FromShift, int ToShift);

    /// <summary>
    /// [フェーズ6, ピース24] Kotlin原本 <c>applyHF66IntraStaffRedistribution</c>（<c>V6HotfixPasses.kt</c>
    /// 2.65.0/3.161.0/3.282.0 由来）の忠実な移植。個人回数(low=下限割れ/high=上限超過, 重み90/45)を、
    /// 各職員が自分の超過シフト(<c>give</c>)を不足シフト(<c>want</c>)へ付け替える手として探索する
    /// （HF67の職員間交換とは異なり、この職員1人だけで完結する手）。
    ///
    /// 主ループは各職員<c>i</c>を走査し「その職員でlow/highに該当するシフトの全組合せ×全日」から
    /// 最も report を改善する1手を選び採用する。1手も見つからなければ、希望非固定なランダムセルへ
    /// 担当可能な別シフトを試す貪欲フォールバック（seed=0x66固定）へ落ちる。
    ///
    /// [3.282.0/新領域ログ監査] 兄弟の HF67 と同型の専用締切(<paramref name="deadlineMs"/>)＋内側スキャンの
    /// <c>OutOfTime</c> 確認（2.65.0/3.161.0の確立方針）を持つ（HF66はこちらが先に確立された側）。
    /// </summary>
    public static HF66Result ApplyHF66IntraStaffRedistribution(
        MagiState state, int[][] schedule, int maxMoves = 30, Func<bool>? shouldStop = null, long deadlineMs = long.MaxValue)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var current = before;
        var moves = 0;
        var shortageMoves = 0;
        var capacityMoves = 0;
        var rollback = 0;
        bool OutOfTime() => stop() || EngineClock.NowMs() >= deadlineMs;

        while (moves < maxMoves)
        {
            if (OutOfTime()) break;
            var counts = ScheduleUtil.CountMatrix(p, work);
            MoveCandidate? bestMove = null;
            ViolationReport? bestReport = null;
            for (var i = 0; i < p.S; i++)
            {
                if (OutOfTime()) goto ScanDone;
                var lows = new List<int>();
                var highs = new List<int>();
                for (var k = 0; k < p.K; k++)
                {
                    if (p.CanDo(i, k) && p.RangeLo[i][k] != int.MinValue && counts[i][k] < p.RangeLo[i][k]) lows.Add(k);
                    if (counts[i][k] > EffectiveHi(p, i, k)) highs.Add(k);
                }
                foreach (var want in lows)
                {
                    foreach (var give in highs)
                    {
                        if (OutOfTime()) goto ScanDone;
                        for (var j = 0; j < p.T; j++)
                        {
                            if (work[i][j] != give || p.WishLocked(i, j)) continue;
                            var cand = work.Copy2D();
                            cand[i][j] = want;
                            var rep = UnifiedViolationChecker.Check(state, cand);
                            if (IsBetter(rep, bestReport ?? current))
                            {
                                bestMove = new MoveCandidate(i, j, give, want);
                                bestReport = rep;
                            }
                        }
                    }
                }
            }
            ScanDone:
            if (bestMove == null) break;
            var mv = bestMove;
            work[mv.Staff][mv.Day] = mv.ToShift;
            current = bestReport ?? UnifiedViolationChecker.Check(state, work);
            moves++;
            shortageMoves++;
            if (current.Soft < before.Soft) capacityMoves++;
        }
        if (moves == 0 && !OutOfTime())
        {
            var rng = new JavaRandom(0x66L);
            var t = 0;
            while (t < maxMoves)
            {
                if (OutOfTime()) break;
                if (p.S > 0 && p.T > 0)
                {
                    var cand = work.Copy2D();
                    var i = rng.NextInt(p.S);
                    var j = rng.NextInt(p.T);
                    if (!p.WishLocked(i, j))
                    {
                        var allowed = p.AllowedShiftsForStaff(i);
                        if (allowed.Length > 0)
                        {
                            var old = cand[i][j];
                            cand[i][j] = allowed[rng.NextInt(allowed.Length)];
                            if (cand[i][j] != old)
                            {
                                var rep = UnifiedViolationChecker.Check(state, cand);
                                if (IsBetter(rep, current))
                                {
                                    work = cand;
                                    current = rep;
                                    moves++;
                                    capacityMoves++;
                                }
                                else
                                {
                                    rollback++;
                                }
                            }
                        }
                    }
                }
                t++;
            }
        }
        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "HF66",
                message: $"intra-staff redistribution applied={moves} rollback={rollback} total {before.Total}->{current.Total}"),
        };
        return new HF66Result(work, before.Total, current.Total, moves, shortageMoves, capacityMoves, rollback, logs);
    }
}
