using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// 個人回数(range)と適切回数(apt)を同時に直す focused LNS。
///
/// 単独の RangePolish/AptPolish は、本人の1セル変更で生じる同日coverage不足や他職員の
/// range/apt副作用を修復する前に候補を捨てやすい。本パスは、個人ペナルティが下がる割当変更と
/// 同日coverage玉突き(<see cref="V6SearchOperators.FindCovUChain"/>)を一つの候補として構成し、
/// 必要なら複数候補をdebt付きbeamで束ねる。
///
/// 特に、専門シフトのapt不足と別シフトのrange/apt超過が同一職員に共存するケースを優先する。
/// 例: 大島愛  Aｱ過剰 + 休過剰 + Pｼ不足。Aｱ→Pｼで本人の3族を同時改善し、空いたAｱは
/// 同日玉突きで補充する。
///
/// 目標値の総和が月日数を超える等、apt違反が構造的に不可避な職員については、希望固定と
/// range/aptだけを用いた厳密count-DP下限を計算する。下限到達済みの違反を無駄に追わず、
/// 同じ下限値の別配置が正式目的を改善する場合だけ移し替える。
/// </summary>
internal static class PersonalBalanceJointLnsPolish
{
    public sealed record Config(
        int BeamWidth = 16,
        int MaxDepth = 4,
        int MaxRestarts = 3,
        int MaxFocusStaff = 6,
        int MaxGoals = 28,
        int MaxVariantsPerGoal = 8,
        int HardDebt = 1,
        int TotalDebt = 16,
        int PersonalDebt = 4,
        long MaxMillis = 6_000L);

    private sealed record Goal(int Staff, int Day, int Target, int Marginal, int Weight, string Reason);

    private sealed record CellOp(int Staff, int Day, int Shift);

    private sealed record Candidate(int[][] Schedule, IReadOnlyList<CellOp> Ops, string Label);

    private sealed record Node(
        int[][] Schedule,
        ViolationReport Report,
        int[] Personal,
        int FocusTotal,
        IReadOnlyList<string> Path,
        int ChangedCells);

    private sealed record Seen(int[][] Schedule);

    public static V6HotfixPasses.CyclicSwapResult Apply(
        MagiState state,
        int[][] schedule,
        Config? config = null,
        Func<bool>? shouldStop = null,
        long seed = 0xA97B4L)
    {
        var cfg = config ?? new Config();
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var rootSchedule = ScheduleUtil.NormalizeSchedule(schedule, p);
        var rootReport = UnifiedViolationChecker.Check(state, rootSchedule);
        if (p.S <= 0 || p.T <= 0 || p.K <= 0) return NoOp(rootSchedule, rootReport, "対象なし");
        if (cfg.BeamWidth <= 0 || cfg.MaxDepth <= 0 || cfg.MaxRestarts <= 0 ||
            cfg.MaxFocusStaff <= 0 || cfg.MaxGoals <= 0 || cfg.MaxVariantsPerGoal <= 0 ||
            cfg.MaxMillis <= 0L)
        {
            return NoOp(rootSchedule, rootReport, "探索上限0=明示的に無効");
        }

        var rootPersonal = PersonalPenaltyByStaff(p, rootSchedule);
        var lower = new int[p.S];
        for (int i = 0; i < p.S; i++) lower[i] = StaffLowerBound(p, i);
        var focus = ChooseFocusStaff(p, rootSchedule, rootPersonal, lower, cfg.MaxFocusStaff);
        if (focus.Length == 0) return NoOp(rootSchedule, rootReport, "range/apt対象なし");

        int rootFocus = focus.Sum(i => rootPersonal[i]);
        long budgetMillis = Math.Min(cfg.MaxMillis, 60_000L);

        // [System.nanoTime() 移植, C1JointLnsPolish と同型] 絶対ナノ秒へ変換せず、ティック単位のまま
        //   deadline を計算する（周波数が高い環境・長時間稼働で long オーバーフローしないため）。
        //   本パスには C1JointLnsPolish の patience/Stalled のような無改善打ち切りは無い
        //   （docstring 冒頭のとおり候補空間が狭く自然に尽きるため元から不要）。
        long TicksFromMillis(long ms) => ms <= 0L ? 0L : ms * System.Diagnostics.Stopwatch.Frequency / 1000L;
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + TicksFromMillis(budgetMillis);
        bool Stopped() => stop() || System.Diagnostics.Stopwatch.GetTimestamp() >= deadline;

        var root = new Node(rootSchedule.Copy2D(), rootReport, rootPersonal, rootFocus, new List<string>(), 0);
        var best = root;
        int generated = 0;
        int expanded = 0;
        int debtRejected = 0;
        int duplicateRejected = 0;
        int restartsDone = 0;
        // [3.350.0/敵対検証] 「目的関数は採用を認めたのにピンだけが止めた」件数を対象別に記録する。
        //   旧: このパスも C1JointLns と同じく exactPinRegression で却下するだけで数えておらず、
        //   UI の observedPinBlockedAttempts / pinTargets から抜けていた。
        var pinBlocks = new PinBlockAttribution();

        for (int restart = 0; restart < cfg.MaxRestarts; restart++)
        {
            if (Stopped() || focus.All(i => best.Personal[i] <= lower[i])) break;
            restartsDone++;
            var rng = new JavaRandom(seed ^ ((long)restart * -0x61c8864680b583ebL));
            List<Node> beam = ReferenceEquals(best, root) ? new List<Node> { root } : new List<Node> { root, best };
            var seen = new Dictionary<long, List<Seen>>();
            foreach (var n in beam) Remember(seen, n.Schedule);

            for (int depth = 0; depth < cfg.MaxDepth; depth++)
            {
                if (Stopped()) break;
                var children = new List<Node>();
                foreach (var parent in beam)
                {
                    if (Stopped()) break;
                    expanded++;
                    var goals = CollectGoals(p, parent.Schedule, focus, lower, cfg.MaxGoals, rng);
                    foreach (var goal in goals)
                    {
                        if (Stopped()) break;
                        var variants = BuildCandidates(p, parent.Schedule, goal, cfg.MaxVariantsPerGoal, rng);
                        foreach (var candidate in variants)
                        {
                            if (Stopped()) break;
                            generated++;
                            var report = UnifiedViolationChecker.Check(state, candidate.Schedule);
                            var personal = PersonalPenaltyByStaff(p, candidate.Schedule);
                            int focusTotal = focus.Sum(i => personal[i]);
                            if (report.Hard > rootReport.Hard + Math.Max(cfg.HardDebt, 0) ||
                                report.Total > rootReport.Total + Math.Max(cfg.TotalDebt, 0) ||
                                focusTotal > rootFocus + Math.Max(cfg.PersonalDebt, 0))
                            {
                                debtRejected++;
                                continue;
                            }
                            if (!Remember(seen, candidate.Schedule))
                            {
                                duplicateRejected++;
                                continue;
                            }
                            var childPath = new List<string>(parent.Path) { candidate.Label };
                            var child = new Node(
                                candidate.Schedule, report, personal, focusTotal,
                                childPath, ChangedCellCount(rootSchedule, candidate.Schedule));
                            children.Add(child);
                            if (IsFinalCandidate(p, child, root, focus, pinBlocks))
                            {
                                if (ReferenceEquals(best, root) || BetterFinal(child, best, focus, lower)) best = child;
                            }
                        }
                    }
                }
                if (children.Count == 0) break;
                beam = SelectBeam(children, rootReport, focus, lower, cfg.BeamWidth, rng);
            }
        }

        var checkedReport = UnifiedViolationChecker.Check(state, best.Schedule);
        var checkedPersonal = PersonalPenaltyByStaff(p, best.Schedule);
        // [receiving-code-review] focusTotal は「悪化させない(<=)」まで緩和。以前は狭義減少(<)を
        // 要求しており、クラスの doc comment が明記する「下限到達済みの違反は、同じ下限値の別配置が
        // 正式目的(Better())を改善する場合だけ移し替える」ケース（personal合計は不変・total等は改善）を
        // 機械的に拒否していた。best自体は Better() で選ばれた真に改善する解であり、focus側は
        // 「その改善の副作用でfocus対象が悪化していないか」だけを見れば十分（primary固有の狭義
        // 改善要求は focus.All の悪化なしチェックと重複するため撤去）。
        bool valid = !ReferenceEquals(best, root) && Better(checkedReport, rootReport) &&
            focus.Sum(i => checkedPersonal[i]) <= rootFocus &&
            focus.All(i => checkedPersonal[i] <= rootPersonal[i]) &&
            !pinBlocks.BlocksImproving(p, rootSchedule, best.Schedule);

        var chosen = valid ? best.Schedule.Copy2D() : rootSchedule.Copy2D();
        var chosenReport = valid ? checkedReport : rootReport;
        var chosenPersonal = valid ? checkedPersonal : rootPersonal;
        string focusText = string.Join(", ", focus.Select(i =>
        {
            string name = i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
            string suffix = chosenPersonal[i] <= lower[i] ? "=下限" : "";
            return $"{name} {rootPersonal[i]}->{chosenPersonal[i]}(下限{lower[i]}{suffix})";
        }));
        string reason = valid && focus.All(i => chosenPersonal[i] <= lower[i]) ? "個人構造下限到達"
            : stop() ? "外部停止"
            : System.Diagnostics.Stopwatch.GetTimestamp() >= deadline ? "期限"
            : "探索停滞";
        var log = new MirrorLog(
            tag: "PersonalJointLNS",
            message: $"個人回数/apt共同LNS: personal {rootFocus}->{focus.Sum(i => chosenPersonal[i])}" +
                $" / low {rootReport.Breakdown.GetValueOrDefault("low", 0)}->{chosenReport.Breakdown.GetValueOrDefault("low", 0)}" +
                $" high {rootReport.Breakdown.GetValueOrDefault("high", 0)}->{chosenReport.Breakdown.GetValueOrDefault("high", 0)}" +
                $" apt {rootReport.Breakdown.GetValueOrDefault("apt", 0)}->{chosenReport.Breakdown.GetValueOrDefault("apt", 0)}" +
                $" / total {rootReport.Total}->{chosenReport.Total} HARD {rootReport.Hard}->{chosenReport.Hard}" +
                $" 採用{(valid ? 1 : 0)}束 手数{(valid ? best.Path.Count : 0)}" +
                $" restart{restartsDone} 展開{expanded} 候補{generated} debt除外{debtRejected} 重複除外{duplicateRejected}" +
                $" 停止={reason} 対象: {focusText}" +
                (valid ? $" 経路: {string.Join("+", best.Path)}" : " [頭打ち=正式目的を改善する個人違反減少束なし]"));
        return new V6HotfixPasses.CyclicSwapResult(
            chosen, rootReport.Total, chosenReport.Total, valid ? 1 : 0, new[] { log },
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }

    private static V6HotfixPasses.CyclicSwapResult NoOp(
        int[][] schedule, ViolationReport report, string reason) => new V6HotfixPasses.CyclicSwapResult(
            schedule.Copy2D(), report.Total, report.Total, 0,
            new[] { new MirrorLog(tag: "PersonalJointLNS", message: reason) });

    private static int[] ChooseFocusStaff(
        Problem p, int[][] schedule, int[] current, int[] lower, int limit)
    {
        var improving = Enumerable.Range(0, p.S)
            .Where(i => current[i] > lower[i])
            .OrderByDescending(i => current[i] - lower[i])
            .ThenByDescending(i => current[i])
            .ThenBy(i => i);
        var unavoidableExclusive = Enumerable.Range(0, p.S)
            .Where(i => current[i] > 0 && current[i] <= lower[i] && HasExclusiveAptViolation(p, schedule, i))
            .OrderByDescending(i => current[i])
            .ThenBy(i => i);
        return improving.Concat(unavoidableExclusive).Distinct().Take(limit).ToArray();
    }

    private static bool HasExclusiveAptViolation(Problem p, int[][] schedule, int staff)
    {
        var counts = new int[p.K];
        for (int j = 0; j < p.T; j++)
        {
            int k = schedule[staff][j];
            if (k >= 0 && k < p.K) counts[k]++;
        }
        for (int k = 0; k < p.K; k++)
        {
            int target = p.Apt[staff][k];
            if (target >= 0 && counts[k] != target && p.StaffForShift[k].Length == 1) return true;
        }
        return false;
    }

    internal static int[] PersonalPenaltyByStaff(Problem p, int[][] schedule)
    {
        var counts = new int[p.S][];
        for (int i = 0; i < p.S; i++) counts[i] = new int[p.K];
        for (int i = 0; i < p.S; i++)
            for (int j = 0; j < p.T; j++)
            {
                int k = schedule[i][j];
                if (k >= 0 && k < p.K) counts[i][k]++;
            }
        var result = new int[p.S];
        for (int i = 0; i < p.S; i++) result[i] = CountPenalty(p, i, counts[i]);
        return result;
    }

    private static int CountPenalty(Problem p, int staff, int[] counts)
    {
        int total = 0;
        for (int k = 0; k < p.K; k++)
        {
            int c = counts[k];
            int lo = p.RangeLo[staff][k];
            int hi = p.RangeHi[staff][k];
            if (lo != int.MinValue && c < lo) total += lo - c;
            if (hi != int.MaxValue && c > hi) total += c - hi;
            int target = p.Apt[staff][k];
            if (target >= 0) total += Math.Abs(c - target);
        }
        return total;
    }

    /// <summary>希望固定・担当可否・range/aptだけを使う、職員単位の厳密count下限。</summary>
    internal static int StaffLowerBound(Problem p, int staff)
    {
        var allowed = Enumerable.Range(0, p.K).Where(k => p.MayPlace(staff, k)).ToList();
        if (allowed.Count == 0) return 0;
        var forced = new int[p.K];
        int fixedCount = 0;
        for (int j = 0; j < p.T; j++)
        {
            // [3.309.0] 旧実装は生の `w >= 0` で数えており、担当できないシフトへの希望まで
            //   「固定」として下限に織り込んでいた（このメソッドの doc comment 自身が「担当可否も使う」と
            //   書いているのに、この分岐だけ CanDo を見ていなかった）。規約は Problem.WishLocked
            //   ＝実現可能な希望だけが凍結される（3.264.0 / 3.270.0 / 3.278.0 と同じ retrofit）。
            if (!p.WishLocked(staff, j)) continue;
            int w = p.Wish[staff][j];
            if (w >= 0 && w < p.K)
            {
                forced[w]++;
                fixedCount++;
            }
        }
        int free = Math.Max(p.T - fixedCount, 0);
        const int inf = 1_000_000;
        var dp = new int[free + 1];
        Array.Fill(dp, inf);
        dp[0] = 0;
        foreach (int k in allowed)
        {
            var next = new int[free + 1];
            Array.Fill(next, inf);
            for (int used = 0; used <= free; used++)
            {
                int baseVal = dp[used];
                if (baseVal >= inf) continue;
                for (int extra = 0; extra <= free - used; extra++)
                {
                    // [HF77適用/翻訳の逐語性] Kotlin原本は counts[k] を書いて即読み返すだけの
                    //   （singleShiftPenalty(p,staff,k,forced[k]+extra) と等価な）無駄な配列を
                    //   毎反復で確保している。「それっぽく」最適化せず、意図的に原本のまま移植する。
                    var counts = new int[p.K];
                    counts[k] = forced[k] + extra;
                    int cost = SingleShiftPenalty(p, staff, k, counts[k]);
                    int v = baseVal + cost;
                    if (v < next[used + extra]) next[used + extra] = v;
                }
            }
            dp = next;
        }
        // Penalties on non-allowed but wished shifts are not optimizable; include their fixed cost.
        int fixedOther = 0;
        for (int k = 0; k < p.K; k++)
            if (!allowed.Contains(k) && forced[k] > 0)
                fixedOther += SingleShiftPenalty(p, staff, k, forced[k]);
        return dp[free] >= inf ? 0 : dp[free] + fixedOther;
    }

    private static int SingleShiftPenalty(Problem p, int staff, int shift, int count)
    {
        int v = 0;
        int lo = p.RangeLo[staff][shift];
        int hi = p.RangeHi[staff][shift];
        if (lo != int.MinValue && count < lo) v += lo - count;
        if (hi != int.MaxValue && count > hi) v += count - hi;
        int target = p.Apt[staff][shift];
        if (target >= 0) v += Math.Abs(count - target);
        return v;
    }

    private static List<Goal> CollectGoals(
        Problem p, int[][] schedule, int[] focus, int[] lower, int limit, JavaRandom rng)
    {
        // [監査で発見・3.270.0] NormalizeSchedule はセンチネル -1 を作りうる（削除済シフトの残存index等）
        //   ため、生の schedule[i][j] を無検証で配列添字に使うとAIOOBEになりうる。ガード追加
        //   （同ファイル内 HasExclusiveAptViolation/PersonalPenaltyByStaff は既に同種のガード付き）。
        var counts = new int[p.S][];
        for (int i = 0; i < p.S; i++) counts[i] = new int[p.K];
        for (int i = 0; i < p.S; i++)
            for (int j = 0; j < p.T; j++)
            {
                int k = schedule[i][j];
                if (k >= 0 && k < p.K) counts[i][k]++;
            }
        var groups = new List<List<Goal>>();
        foreach (int i in focus)
        {
            int before = CountPenalty(p, i, counts[i]);
            var list = new List<Goal>();
            for (int j = 0; j < p.T; j++)
            {
                if (p.WishLocked(i, j)) continue;
                int old = schedule[i][j];
                if (old < 0 || old >= p.K) continue;
                for (int target = 0; target < p.K; target++)
                {
                    if (target == old || !p.MayPlace(i, target)) continue;
                    counts[i][old]--;
                    counts[i][target]++;
                    int after = CountPenalty(p, i, counts[i]);
                    counts[i][target]--;
                    counts[i][old]++;
                    int marginal = after - before;
                    int targetDef = TargetDeficit(p, i, target, counts[i][target]);
                    int sourceExcess = SourceExcess(p, i, old, counts[i][old]);
                    int exclusive = p.StaffForShift[target].Length == 1 ? 40 : 0;
                    bool atLowerBound = before <= lower[i];
                    // 改善手を主対象。下限到達済みは、違反の置き場所を変えて他制約を改善できる
                    // marginal=0 の手だけ残す。一時+1はbeam debtで協調解を作るため少量許可。
                    if (marginal > 1) continue;
                    if (marginal == 1 && targetDef == 0 && sourceExcess == 0) continue;
                    if (marginal == 0 && !atLowerBound && targetDef == 0 && sourceExcess == 0) continue;
                    string reason = marginal switch
                    {
                        < 0 => $"個人{-marginal}改善",
                        0 => "下限内移替",
                        _ => "一時debt",
                    };
                    int weight = -marginal * 100 + targetDef * 30 + sourceExcess * 30 + exclusive;
                    list.Add(new Goal(i, j, target, marginal, weight, reason));
                }
            }
            if (list.Count > 0)
                groups.Add(list.Shuffled(rng).OrderByDescending(g => g.Weight).ToList());
        }
        var outGoals = new List<Goal>();
        while (outGoals.Count < limit && groups.Any(g => g.Count > 0))
        {
            foreach (var g in groups)
            {
                if (g.Count > 0)
                {
                    outGoals.Add(g[0]);
                    g.RemoveAt(0);
                }
                if (outGoals.Count >= limit) break;
            }
        }
        return outGoals;
    }

    private static int TargetDeficit(Problem p, int staff, int shift, int count)
    {
        int v = 0;
        int lo = p.RangeLo[staff][shift];
        if (lo != int.MinValue && count < lo) v += lo - count;
        int target = p.Apt[staff][shift];
        if (target >= 0 && count < target) v += target - count;
        return v;
    }

    private static int SourceExcess(Problem p, int staff, int shift, int count)
    {
        int v = 0;
        int hi = p.RangeHi[staff][shift];
        if (hi != int.MaxValue && count > hi) v += count - hi;
        int target = p.Apt[staff][shift];
        if (target >= 0 && count > target) v += count - target;
        return v;
    }

    private static List<Candidate> BuildCandidates(
        Problem p, int[][] baseSchedule, Goal goal, int limit, JavaRandom rng)
    {
        int i = goal.Staff;
        int j = goal.Day;
        int target = goal.Target;
        int old = baseSchedule[i][j];
        if (old == target || p.WishLocked(i, j) || !p.MayPlace(i, target)) return new List<Candidate>();
        var outCandidates = new List<Candidate>();

        // 同日1対1交換。coverageを完全保存するため最優先。
        var donors = Enumerable.Range(0, p.S).Shuffled(rng);
        foreach (int d in donors)
        {
            if (d == i || baseSchedule[d][j] != target || p.WishLocked(d, j) || !p.MayPlace(d, old)) continue;
            var w = baseSchedule.Copy2D();
            w[i][j] = target;
            w[d][j] = old;
            if (p.MakesForbiddenRun(baseSchedule, i, j, target) || p.MakesForbiddenRun(baseSchedule, d, j, old)) continue;
            outCandidates.Add(new Candidate(
                w, new List<CellOp> { new CellOp(i, j, target), new CellOp(d, j, old) }, $"{goal.Reason}:同日交換"));
            if (outCandidates.Count >= Math.Max(1, limit / 3)) break;
        }

        // 本人を直接 target へ移し、空いたoldがcovUを悪化させる場合だけ既存BFS玉突きで同日補充。
        {
            var w = baseSchedule.Copy2D();
            if (!p.MakesForbiddenRun(baseSchedule, i, j, target))
            {
                int beforeOld = Enumerable.Range(0, p.S).Count(s => baseSchedule[s][j] == old);
                w[i][j] = target;
                bool needsRepair = p.CovUCell(old, j, beforeOld - 1) > p.CovUCell(old, j, beforeOld);
                var ops = new List<CellOp> { new CellOp(i, j, target) };
                bool ok = true;
                if (needsRepair)
                {
                    // [監査で発見・3.270.0] w は盤面全体のコピー。goal のセル(i,j)自体はCollectGoalsで
                    //   -1(センチネル)を除外済みだが、他のセル(s,day)は無関係な位置で-1が残っている
                    //   可能性がある。無検証添字はAIOOBEになりうるためガード。
                    var counts = new int[p.S][];
                    for (int s = 0; s < p.S; s++)
                    {
                        counts[s] = new int[p.K];
                        for (int day = 0; day < p.T; day++)
                        {
                            int k = w[s][day];
                            if (k >= 0 && k < p.K) counts[s][k]++;
                        }
                    }
                    var chain = V6SearchOperators.FindCovUChain(
                        p, w, old, j, rng, exclude: i,
                        rangeAvoid: (s, fill) =>
                        {
                            int hi = p.RangeHi[s][fill];
                            return (hi != int.MaxValue && counts[s][fill] >= hi) ||
                                (p.Apt[s][fill] >= 0 && counts[s][fill] > p.Apt[s][fill]);
                        });
                    if (chain == null) ok = false;
                    else
                    {
                        foreach (var mv in chain)
                        {
                            w[mv[0]][mv[1]] = mv[2];
                            ops.Add(new CellOp(mv[0], mv[1], mv[2]));
                        }
                    }
                }
                if (ok) outCandidates.Add(new Candidate(w, ops, $"{goal.Reason}:直接+coverage連鎖"));
            }
        }

        // 本人の別日targetと自己交換。月間回数は不変だが、下限内移替やc1/c3/weeklyの副作用改善に使う。
        foreach (int d2 in Enumerable.Range(0, p.T).Shuffled(rng))
        {
            if (d2 == j || baseSchedule[i][d2] != target || p.WishLocked(i, d2) || !p.MayPlace(i, old)) continue;
            var w = baseSchedule.Copy2D();
            w[i][j] = target;
            w[i][d2] = old;
            if (p.MakesForbiddenRun(baseSchedule, i, j, target) || p.MakesForbiddenRun(baseSchedule, i, d2, old)) continue;
            outCandidates.Add(new Candidate(
                w, new List<CellOp> { new CellOp(i, j, target), new CellOp(i, d2, old) }, $"{goal.Reason}:自己日交換"));
            if (outCandidates.Count >= limit) break;
        }

        // クロス日token移送。本人の回数を改善しながら全体の月間shift総量を保存する。
        if (outCandidates.Count < limit)
        {
            foreach (int d in donors)
            {
                foreach (int d2 in Enumerable.Range(0, p.T).Shuffled(rng))
                {
                    if (d == i && d2 == j) continue;
                    if (baseSchedule[d][d2] != target || p.WishLocked(d, d2) || !p.MayPlace(d, old)) continue;
                    var w = baseSchedule.Copy2D();
                    w[i][j] = target;
                    w[d][d2] = old;
                    if (p.MakesForbiddenRun(baseSchedule, i, j, target) || p.MakesForbiddenRun(baseSchedule, d, d2, old)) continue;
                    outCandidates.Add(new Candidate(
                        w, new List<CellOp> { new CellOp(i, j, target), new CellOp(d, d2, old) }, $"{goal.Reason}:クロス日移送"));
                    if (outCandidates.Count >= limit) goto CrossDayTransferDone;
                }
            }
            CrossDayTransferDone: ;
        }
        return outCandidates.DistinctBy(c => CandidateKey(c.Ops)).Take(limit).ToList();
    }

    private static string CandidateKey(IReadOnlyList<CellOp> ops) => string.Join(";",
        ops.OrderBy(o => o.Staff).ThenBy(o => o.Day).ThenBy(o => o.Shift)
            .Select(o => $"{o.Staff},{o.Day},{o.Shift}"));

    /// <summary>
    /// [3.350.0] <paramref name="pinBlocks"/> を渡すと「目的関数は採用を認めたのにピンだけが止めた」件数を
    /// 記録する。ここへ到達した時点で <see cref="Better"/> と focus の悪化なしは確定済み＝
    /// <see cref="PinBlockAttribution"/> の契約（採用が認められた手だけを数える）を満たす。
    /// </summary>
    private static bool IsFinalCandidate(
        Problem p, Node node, Node root, int[] focus, PinBlockAttribution? pinBlocks = null)
    {
        if (!Better(node.Report, root.Report)) return false;
        // focusTotal は「悪化させない(<=)」まで緩和。狭義減少(<)のみを許すと、クラスの doc comment が
        // 明記する「下限到達済みの職員は、personal合計が同じ別配置でも Better() が改善するなら移し替える」
        // ケースを機械的に拒否してしまう（primary固有の狭義改善要求は次行の悪化なしチェックと
        // 重複するため撤去済み）。
        if (node.FocusTotal > root.FocusTotal) return false;
        if (focus.Any(i => node.Personal[i] > root.Personal[i])) return false;
        // [厳密ピン保護] focus外の職員(coverage連鎖の donor/receiver等)がstaffRange厳密ピンから
        // 遠ざかる副作用も拒否する（focusのみを見る上記チェックでは対象外のため追加）。
        bool pinBad = pinBlocks?.BlocksImproving(p, root.Schedule, node.Schedule)
            ?? V6SearchOperators.ExactPinRegression(p, root.Schedule, node.Schedule);
        if (pinBad) return false;
        return true;
    }

    private static bool BetterFinal(Node a, Node b, int[] focus, int[] lower)
    {
        // 採用候補間でも正式順序を最優先する。個人下限gapは同一report時のtie-breakだけ。
        if (Better(a.Report, b.Report)) return true;
        if (Better(b.Report, a.Report)) return false;
        int ag = focus.Sum(i => Math.Max(a.Personal[i] - lower[i], 0));
        int bg = focus.Sum(i => Math.Max(b.Personal[i] - lower[i], 0));
        if (ag != bg) return ag < bg;
        return a.ChangedCells < b.ChangedCells;
    }

    // [3.287.0 keep-best統一] hard→weighted→total（MirrorCore.betterReport）
    private static bool Better(ViolationReport a, ViolationReport b) => UnifiedViolationChecker.BetterReport(a, b);

    private static List<Node> SelectBeam(
        List<Node> children, ViolationReport root, int[] focus, int[] lower, int width, JavaRandom rng)
    {
        var official = children
            .OrderBy(n => n.Report.Hard)
            .ThenBy(n => n.Report.WeightedScore)
            .ThenBy(n => n.Report.Total)
            .ThenBy(n => n.FocusTotal)
            .ThenBy(n => n.ChangedCells)
            .Take(Math.Max(1, width / 2))
            .ToList();

        var personal = children.Shuffled(rng)
            .OrderBy(n => Math.Max(n.Report.Hard - root.Hard, 0))
            .ThenBy(n => focus.Sum(i => Math.Max(n.Personal[i] - lower[i], 0)))
            .ThenBy(n => Math.Max(n.Report.Total - root.Total, 0))
            .ThenBy(n => n.Report.Hard)
            .ThenBy(n => n.Report.WeightedScore)
            .ThenBy(n => n.Report.Total)
            .ThenBy(n => n.ChangedCells)
            .Take(Math.Max(1, width - official.Count))
            .ToList();

        var outNodes = new List<Node>();
        foreach (var n in official.Concat(personal))
            if (!outNodes.Any(o => SameSchedule(o.Schedule, n.Schedule)))
                outNodes.Add(n);
        return outNodes.Take(width).ToList();
    }

    private static bool Remember(Dictionary<long, List<Seen>> seen, int[][] schedule)
    {
        long h = ScheduleHash(schedule);
        if (!seen.TryGetValue(h, out var bucket))
        {
            bucket = new List<Seen>();
            seen[h] = bucket;
        }
        if (bucket.Any(s => SameSchedule(s.Schedule, schedule))) return false;
        bucket.Add(new Seen(schedule.Copy2D()));
        return true;
    }

    private static long ScheduleHash(int[][] schedule)
    {
        long h = -0x340d631b7bdddcdbL;
        foreach (var row in schedule)
            foreach (var v in row)
            {
                h ^= (long)v;
                h *= 0x100000001b3L;
            }
        return h;
    }

    private static bool SameSchedule(int[][] a, int[][] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!a[i].SequenceEqual(b[i])) return false;
        return true;
    }

    private static int ChangedCellCount(int[][] root, int[][] other)
    {
        int n = 0;
        for (int i = 0; i < root.Length; i++)
            for (int j = 0; j < root[i].Length; j++)
                if (root[i][j] != other[i][j]) n++;
        return n;
    }
}
