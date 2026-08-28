using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// C1時系列DP研磨（<c>C1TemporalSwapPolish</c>）の実現ステップを、同日2人swapから
/// <see cref="FlexibleDayFlow"/>（3.245.0, RangePolish手Fで使用）による同日全員参加の最小費用再割当へ拡張する。
///
/// <see cref="C1TemporalDp"/> が求める「対象シフトか否かの月内最適二値列」自体は正しいが、旧<c>C1TemporalSwapPolish</c>
/// はその変更日を「厳密に相補的なシフトを持つ1人との同日swap」でしか実現できず、そのような相手が
/// 存在しない日ではDPの改善が丸ごと死んでいた（実測: golden_state.jsonでDP単体寄与0%を確認）。
/// 本パスは同じDP出力を使い、各変更日を「対象職員をtarget/非targetへ強制し、他の全職員は
/// <see cref="FlexibleDayFlow"/>が費用最小で再配置する」同日ジョイント再割当で実現する。covU/covO(被覆)は
/// <c>shiftMarginalCost</c>、staffRange/apt(回数)は<c>staffShiftCost</c>に組み込み済みのため、C1改善に伴う
/// 被覆・回数への副作用はこの日次ソルバー自身が最小化する。禁止連続(c3n)は候補セルの事前フィルタで回避。
///
/// 日ごとの独立最適化のため月全体での厳密最適解ではないが、既存の同日swap限定より確実に広い（同日swapは
/// この解の特殊ケース＝実現可能な集合の真部分集合）。最終採否は必ず<see cref="UnifiedViolationChecker"/>と
/// hard→weightedScore→totalのkeep-bestで行う（退化不能）。
/// </summary>
internal static class C1TemporalFlowPolish
{
    private sealed record Plan(
        int[][] Schedule,
        ViolationReport Report,
        int Staff,
        int Shift,
        int Relocations,
        int DaysTouched);

    public static V6HotfixPasses.CyclicSwapResult Apply(
        MagiState state,
        int[][] schedule,
        int maxPasses = 2,
        int maxRelocations = 4,
        int trials = 4,
        Func<bool>? shouldStop = null,
        long seed = 0xC1F10FL)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        int applied = 0;
        int rowNoGain = 0;                       // 行の c1 が減らず候補にならなかった数
        var rejects = new RejectCulpritStats();  // 目的関数/ピンで落ちた候補の内訳
        // [3.359.0] 「目的関数は採用を認めたのにピンだけが止めた」候補の**対象(職員,シフト)**を集める。
        //   実データでの規模は小さい（real で6件・他は0）が、UI の「回数の固定について」一覧の
        //   計測範囲を、このパスについても実装どおりにするための配線。
        var pinBlocks = new PinBlockAttribution();
        int dpCandidates = 0;
        int flowFailures = 0;
        var fixedLabels = new List<string>();

        if (p.Cons1.Count == 0)
        {
            return new V6HotfixPasses.CyclicSwapResult(
                work, before.Total, before.Total, 0,
                new[] { new MirrorLog(tag: "C1TemporalFlow", message: "cons1なし=スキップ") });
        }

        var rulesByShift = new Dictionary<int, List<C1TemporalDp.Rule>>();
        var rulesByShiftOrder = new List<int>();
        foreach (var c in p.Cons1)
        {
            if (c.ShiftIdx < 0 || c.ShiftIdx >= p.K || c.Day1 <= 0 || c.Day2 <= 0) continue;
            if (!rulesByShift.TryGetValue(c.ShiftIdx, out var list))
            {
                list = new List<C1TemporalDp.Rule>();
                rulesByShift[c.ShiftIdx] = list;
                rulesByShiftOrder.Add(c.ShiftIdx);
            }
            list.Add(new C1TemporalDp.Rule(c.Day1, c.Day2));
        }

        bool Better(ViolationReport a, ViolationReport b) =>
            UnifiedViolationChecker.BetterReport(a, b); // [3.287.0 keep-best統一] hard→weighted→total（MirrorCore.betterReport）

        int RowC1Fires(int[][] s, int i)
        {
            int outCount = 0;
            foreach (var c in p.Cons1)
            {
                int x = c.ShiftIdx; int d = c.Day1;
                if (x < 0 || x >= p.K || d <= 0 || d > p.T || !p.CanDo(i, x)) continue;
                int count = 0;
                for (int j = 0; j < d; j++) if (s[i][j] == x) count++;
                if (count < c.Day2) outCount++;
                int start = 1;
                while (start <= p.T - d)
                {
                    if (s[i][start - 1] == x) count--;
                    if (s[i][start + d - 1] == x) count++;
                    if (count < c.Day2) outCount++;
                    start++;
                }
            }
            return outCount;
        }

        // 日 j の全職員を同時再割当する。forcedStaff は disallow に含まれるシフトへは行けない
        // （target化なら disallow=対象外全部、非target化なら disallow={対象シフト}）。
        // 他の職員は staffRange/apt(回数)+covU/covO(被覆)の合計費用最小へ FlexibleDayFlow が解く。
        // board は判定基準の盤面（累積中のtrialWork）を明示的に受け取る（暗黙のwork捕捉を避ける）。
        int[]? SolveDay(int[][] board, int j, int forcedStaff, HashSet<int> disallow, long trialSeed)
        {
            var oldDay = new int[p.S];
            for (int i = 0; i < p.S; i++) oldDay[i] = board[i][j];
            var counts = new int[p.S][];
            for (int i = 0; i < p.S; i++) counts[i] = new int[p.K];
            for (int i = 0; i < p.S; i++)
            {
                for (int jj = 0; jj < p.T; jj++)
                {
                    int kk = board[i][jj];
                    if (kk >= 0 && kk < p.K) counts[i][kk]++;
                }
            }

            long RangeAndAptCost(int i, int oldK, int newK)
            {
                long outCost = 0L;
                for (int kk = 0; kk < p.K; kk++)
                {
                    int c = counts[i][kk];
                    if (newK != oldK)
                    {
                        if (kk == oldK) c--;
                        if (kk == newK) c++;
                    }
                    int lo = p.RangeLo[i][kk]; int hi = p.RangeHi[i][kk];
                    if (lo != int.MinValue && c < lo) outCost += (long)(lo - c) * 90L;
                    if (hi != int.MaxValue && c > hi) outCost += (long)(c - hi) * 45L;
                    int a = p.Apt[i][kk];
                    if (a >= 0) outCost += (long)Math.Abs(c - a);
                }
                if (newK != oldK) outCost += 2L;
                return outCost;
            }

            long DayPenalty(int k, int q) =>
                // [HF77明示指示 2026-08-27] covO 重み 1→5（V6HotfixPasses の同種箇所と同時に変更）。
                (long)p.CovUCell(k, j, q) * 8000L + (long)p.CovOCell(k, j, q) * 5L;

            var staffCost = new long[p.S][];
            for (int i = 0; i < p.S; i++)
            {
                staffCost[i] = new long[p.K];
                Array.Fill(staffCost[i], FlexibleDayFlow.INF);
            }
            for (int i = 0; i < p.S; i++)
            {
                int oldK = oldDay[i];
                for (int newK = 0; newK < p.K; newK++)
                {
                    if (i == forcedStaff && disallow.Contains(newK)) continue;
                    bool changed = newK != oldK;
                    if (changed)
                    {
                        // [3.417.0] 記号「希」を割当先から外すガードを撤去（詳細は V6HotfixPasses の同種箇所）。
                        if (p.WishLocked(i, j) || !p.CanDo(i, newK)) continue;
                        board[i][j] = newK;
                        bool bad = p.MakesForbiddenRun(board, i, j, newK);
                        board[i][j] = oldK;
                        if (bad) continue;
                    }
                    long primary = RangeAndAptCost(i, oldK, newK);
                    long tie = ((long)i * 131 + (long)newK * 31 + trialSeed) & 1023L;
                    staffCost[i][newK] = primary * 1024L + tie;
                }
            }
            bool hasFeasible = false;
            for (int k = 0; k < p.K; k++)
            {
                if (!disallow.Contains(k) && staffCost[forcedStaff][k] < FlexibleDayFlow.INF / 2) { hasFeasible = true; break; }
            }
            if (!hasFeasible) return null;   // forcedStaffに実現可能な行先が無い

            var marginal = new long[p.K][];
            for (int k = 0; k < p.K; k++)
            {
                marginal[k] = new long[p.S];
                for (int q0 = 0; q0 < p.S; q0++)
                {
                    int q = q0 + 1;
                    marginal[k][q0] = (DayPenalty(k, q) - DayPenalty(k, q - 1)) * 1024L;
                }
            }
            var solved = FlexibleDayFlow.Solve(staffCost, marginal);
            if (solved is null) return null;
            if (disallow.Contains(solved.Assignment[forcedStaff])) return null;
            return solved.Assignment;
        }

        Plan? BuildPlan(int i, int x, C1TemporalDp.Candidate candidate, long trialSeed)
        {
            var changedDays = new List<int>();
            for (int j = 0; j < p.T; j++)
            {
                if (candidate.TargetDays[j] != (work[i][j] == x)) changedDays.Add(j);
            }
            if (changedDays.Count == 0) return null;
            var trialWork = work.Copy2D();
            foreach (var j in changedDays)
            {
                if (stop()) return null;
                bool wantsX = candidate.TargetDays[j];
                HashSet<int> disallow;
                if (wantsX)
                {
                    disallow = new HashSet<int>();
                    for (int k = 0; k < p.K; k++) if (k != x) disallow.Add(k);
                }
                else
                {
                    disallow = new HashSet<int> { x };
                }
                var assignment = SolveDay(trialWork, j, i, disallow, trialSeed ^ (long)j);
                if (assignment is null) { flowFailures++; return null; }
                var next = trialWork.Copy2D();
                for (int s = 0; s < p.S; s++) next[s][j] = assignment[s];
                trialWork = next;
            }
            int newRowFires = RowC1Fires(trialWork, i);
            if (newRowFires >= RowC1Fires(work, i)) { rowNoGain++; return null; }
            var rep = UnifiedViolationChecker.Check(state, trialWork);
            // [3.356.0/実機ログ起因] 旧ログは「DP候補12 flow失敗0 採用0回」までで、12件が
            //   ①行のc1が減らない ②目的関数に負けた ③厳密ピンを崩す のどれで落ちたかが読めなかった。
            //   判定の順序は変えず（better → ピン）、落ちた理由だけを数える。
            bool ok = Better(rep, bestRep);
            // [厳密ピン保護] 他職員のジョイント再割当(FlexibleDayFlow)がstaffRange厳密ピンを崩す
            // 副作用は、total/weightedScoreが改善してもここで拒否する（keep-best不変・追加ガードのみ）。
            bool pinBad = ok && pinBlocks.BlocksImproving(p, work, trialWork);
            if (!ok || pinBad)
            {
                rejects.Record(rep, bestRep, pinBad);
                return null;
            }
            return new Plan(trialWork, rep, i, x, candidate.Relocations, changedDays.Count);
        }

        int pass = 0;
        while (pass < maxPasses && !stop())
        {
            bool improved = false;
            for (int i = 0; i < p.S; i++)
            {
                if (stop()) break;
                if (RowC1Fires(work, i) == 0) continue;
                Plan? bestForStaff = null;
                foreach (var x in rulesByShiftOrder)
                {
                    if (stop()) break;
                    var rules = rulesByShift[x];
                    if (!p.CanDo(i, x)) continue;
                    int focusBefore = C1TemporalDp.CountFires(work[i], x, rules);
                    if (focusBefore == 0) continue;
                    var locked = new bool[p.T];
                    for (int j = 0; j < p.T; j++) locked[j] = p.WishLocked(i, j);
                    for (int trial = 0; trial < Math.Max(trials, 1); trial++)
                    {
                        if (stop()) break;
                        long trialSeed = seed ^ ((long)i << 32) ^ ((long)x << 16) ^
                            ((long)pass << 8) ^ (long)trial;
                        var cand = C1TemporalDp.Solve(
                            work[i], x, rules, locked, maxRelocations: maxRelocations, seed: trialSeed);
                        if (cand is null) continue;
                        dpCandidates++;
                        var plan = BuildPlan(i, x, cand, trialSeed);
                        if (plan is null) continue;
                        var old = bestForStaff;
                        if (old is null || Better(plan.Report, old.Report) ||
                            (plan.Report.Hard == old.Report.Hard && plan.Report.Total == old.Report.Total &&
                                Math.Abs(plan.Report.WeightedScore - old.Report.WeightedScore) <= 1e-9 &&
                                plan.DaysTouched < old.DaysTouched))
                        {
                            bestForStaff = plan;
                        }
                    }
                }
                var chosen = bestForStaff;
                if (chosen is null) continue;
                work = chosen.Schedule.Copy2D();
                bestRep = chosen.Report;
                applied++;
                improved = true;
                string name = chosen.Staff >= 0 && chosen.Staff < state.StaffList.Count
                    ? state.StaffList[chosen.Staff].Name
                    : $"#{chosen.Staff}";
                string symbol = chosen.Shift >= 0 && chosen.Shift < state.Shifts.Count
                    ? state.Shifts[chosen.Shift].Kigou
                    : chosen.Shift.ToString();
                fixedLabels.Add($"{name} {symbol}（{chosen.Relocations}移設/{chosen.DaysTouched}日ジョイント再割当）");
            }
            pass++;
            if (!improved) break;
        }

        var suffix = "";
        if (rowNoGain > 0) suffix += $" 行c1が減らず{rowNoGain}件";
        suffix += rejects.Summary();
        if (fixedLabels.Count > 0) suffix += $" 対象: {string.Join(", ", fixedLabels)}";
        if (applied == 0 && before.Breakdown.GetValueOrDefault("c1", 0) > 0) suffix += " [頭打ち=ジョイント再割当解なし]";

        var logs = new[]
        {
            new MirrorLog(
                tag: "C1TemporalFlow",
                message: $"期間要件(c1)時系列DP+ジョイント再割当研磨: c1 {before.Breakdown.GetValueOrDefault("c1", 0)}->" +
                    $"{bestRep.Breakdown.GetValueOrDefault("c1", 0)} / total {before.Total}->{bestRep.Total} " +
                    $"HARD {before.Hard}->{bestRep.Hard} 採用{applied}回 DP候補{dpCandidates} " +
                    $"flow失敗{flowFailures}" + suffix),
        };
        return new V6HotfixPasses.CyclicSwapResult(
            work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
