using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>C42FlowPolish</c> object.
///
/// [測定中, Kotlin原本] c42/c42s（群ペア禁止, SOFT, 重み9）専用の決定的 min-cost-flow 研磨パス
/// （backlog #12(b) 残課題）。<see cref="FlexibleDayFlow"/> を群内サブセットへ適用する点は C41 系と
/// 同じだが、c42 は2つの(群,シフト)ペアが同時に絡む（<see cref="Evaluator.C42PairCount"/>）ため片方の
/// 群だけを流すと相手側の目的値も変わる。片側固定のヤコビ近似＋対称2試行（sameSet 時は同じ変数なので
/// 1試行）で扱う＝<b>真の相互最適ではない</b>が、最終採否は必ず <see cref="UnifiedViolationChecker"/> の
/// フル評価＋<see cref="V6SearchOperators.AdoptionGate"/>（keep-best）で決まるため誘導が近似でも
/// 正しさは揺るがない。
///
/// c42 の真の評価 C42PairCount は真に凸（sameSet時 n1*(n1-1)/2、非sameSet時は n2 固定なら n1 について
/// 線形）なので、c41 で要る非凸回避のガイド費用置換は不要——真の限界費用の差分をそのまま渡せる。
///
/// [測定, Kotlin原本 3.583.0] backlog#26のtools/loopベンチ（<c>c42FlowPolishReactivate</c>）はゲート
/// 不合格＝既定OFF維持が確定している。C#側もこの結論を踏襲し、パス本体は Kotlin と同値の忠実な移植に
/// とどめる（探索動学は変えない）。
/// </summary>
internal static class C42FlowPolish
{
    private sealed record Candidate(int[][] Board, ViolationReport Report);

    public static V6HotfixPasses.CyclicSwapResult ApplyC42FlowPolish(
        MagiState state, int[][] schedule, int maxPasses = 3,
        Func<bool>? shouldStop = null, bool quantitativeRangeEval = false)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        var bestRep = before;
        var applied = 0;

        bool Movable(int i, int j) => !p.WishLocked(i, j);
        // [3.522.0, Kotlin原本]
        long DayPenalty(int k, int j, int q) => (long)p.CovUCell(k, j, q) * 10000L + (long)p.CovOCell(k, j, q) * 10L;

        // [groupOf] は c42=Sgrp／c42s=Ssk を渡す（判定式は同型、群の出所だけが違う）。
        // members側をtargetシフトへ寄せる誘導を1本作る。otherCountは固定側の人数(スナップショット)、
        // sameSetのときはtargetシフト内の人数(z)のみで完結しotherCountは使わない。
        Candidate? TryMove(int j, IReadOnlyList<int> members, int target, int otherCount, bool sameSet)
        {
            var movableMembers = members.Where(i => Movable(i, j)).ToList();
            if (movableMembers.Count == 0) return null;
            var baseCount = new int[p.K];
            for (var k = 0; k < p.K; k++)
            {
                var cnt = 0;
                for (var i = 0; i < p.S; i++) if (work[i][j] == k && !movableMembers.Contains(i)) cnt++;
                baseCount[k] = cnt;
            }
            var oldDay = movableMembers.Select(i => work[i][j]).ToArray();
            var staffCost = new long[movableMembers.Count][];
            for (var idx = 0; idx < movableMembers.Count; idx++)
            {
                var i = movableMembers[idx];
                var oldK = oldDay[idx];
                var row = new long[p.K];
                for (var newK = 0; newK < p.K; newK++)
                {
                    row[newK] = newK == oldK ? 0L
                        : !p.MayPlace(i, newK) || p.MakesForbiddenRun(work, i, j, newK) ? FlexibleDayFlow.INF
                        : 1L;
                }
                staffCost[idx] = row;
            }
            var marginal = new long[p.K][];
            for (var k = 0; k < p.K; k++)
            {
                var row = new long[movableMembers.Count];
                for (var q0 = 0; q0 < movableMembers.Count; q0++)
                {
                    var q = q0 + 1;
                    var m = DayPenalty(k, j, baseCount[k] + q) - DayPenalty(k, j, baseCount[k] + q - 1);
                    if (k == target)
                    {
                        var z0 = baseCount[k] + q - 1;
                        var z1 = baseCount[k] + q;
                        var pc = sameSet
                            ? Evaluator.C42PairCount(true, z1, z1) - Evaluator.C42PairCount(true, z0, z0)
                            : Evaluator.C42PairCount(false, z1, otherCount) - Evaluator.C42PairCount(false, z0, otherCount);
                        m += pc * 1000L;
                    }
                    row[q0] = m * 1024L;
                }
                marginal[k] = row;
            }
            var solved = FlexibleDayFlow.Solve(staffCost, marginal);
            if (solved is null) return null;
            var allSame = true;
            for (var idx = 0; idx < solved.Assignment.Length; idx++) if (solved.Assignment[idx] != oldDay[idx]) { allSame = false; break; }
            if (allSame) return null;
            var board = work.Copy2D();
            for (var idx = 0; idx < movableMembers.Count; idx++) board[movableMembers[idx]][j] = solved.Assignment[idx];
            return new Candidate(board, UnifiedViolationChecker.Check(state, board, quantitativeRangeEval));
        }

        bool TryFamily(IReadOnlyList<C42> cons, Func<int, int> groupOf)
        {
            var improvedAny = false;
            foreach (var c in cons)
            {
                if (stop()) return improvedAny;
                var sameSet = c.G1 == c.G2 && c.S1 == c.S2;
                for (var j = 0; j < p.T; j++)
                {
                    if (stop()) return improvedAny;
                    int n1 = 0, n2 = 0;
                    for (var i = 0; i < p.S; i++)
                    {
                        if (groupOf(i) == c.G1 && work[i][j] == c.S1) n1++;
                        if (groupOf(i) == c.G2 && work[i][j] == c.S2) n2++;
                    }
                    if (Evaluator.C42PairCount(sameSet, n1, n2) == 0L) continue;
                    var members1 = Enumerable.Range(0, p.S).Where(i => groupOf(i) == c.G1).ToList();
                    var candidates = new List<Candidate>();
                    if (sameSet)
                    {
                        var cand = TryMove(j, members1, c.S1, 0, true);
                        if (cand is not null) candidates.Add(cand);
                    }
                    else
                    {
                        var members2 = Enumerable.Range(0, p.S).Where(i => groupOf(i) == c.G2).ToList();
                        var cand1 = TryMove(j, members1, c.S1, n2, false);
                        if (cand1 is not null) candidates.Add(cand1);
                        var cand2 = TryMove(j, members2, c.S2, n1, false);
                        if (cand2 is not null) candidates.Add(cand2);
                    }
                    Candidate? winner = null;
                    foreach (var cand in candidates)
                    {
                        var gate = V6SearchOperators.AdoptionGate(p, work, cand.Board, cand.Report, bestRep, pinBlocks);
                        if (gate.Accepted && (winner is null || UnifiedViolationChecker.BetterReport(cand.Report, winner.Report))) winner = cand;
                    }
                    if (winner is not null)
                    {
                        for (var i = 0; i < p.S; i++) work[i][j] = winner.Board[i][j];
                        bestRep = winner.Report; applied++; improvedAny = true;
                    }
                }
            }
            return improvedAny;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var a = TryFamily(p.Cons42, i => p.Sgrp[i]);
            var b = TryFamily(p.Cons42s, i => p.Ssk[i]);
            pass++;
            if (!a && !b) break;
        }

        var logs = new List<MirrorLog> { new(tag: "C42FlowPolish", message: $"群ペア禁止(c42/c42s)フロー研磨: total {before.Total}->{bestRep.Total} 採用{applied}回") };
        return new V6HotfixPasses.CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
