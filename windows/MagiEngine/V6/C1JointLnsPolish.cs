using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// C1 共同 Large-Neighbourhood Search。
///
/// 従来の C1Window/Temporal/Rotate/Bundle/Wide は、各々が候補を作った直後に完全目的関数で
/// 採否するため、C1 改善に伴う coverage/range/c3 系の副作用を別の手で相殺する前に候補を失う。
/// 本パスは C1 不足セルに加え、covU と range-low の不足セルを同じ goal pool に入れ、
/// 同日交換・3者回転・自己日交換・クロス日移送・一時的な直接変更を一つの debt 付き beam で束ねる。
///
/// 中間ノードは root から hard/total/c1 の小さな debt を許す。最終採用は必ず
/// <see cref="UnifiedViolationChecker"/> の正式順序 hard -&gt; weightedScore -&gt; total で root より良く、
/// かつ C1 が狭義減少する状態だけ。root は常に別枠で保持し、engine へ共有配列を渡す方式も使わない。
///
/// 50% は「構造下限までの改善可能幅」に対する進捗目標であり、終了条件ではない。探索は C1=下限、
/// deadline、shouldStop、<see cref="Config.PatienceMs"/> の無改善、または全 restart の停滞まで継続する。
///
/// <b>[3.342.0] 停滞打ち切り</b>。3.339.0 のパス別テレメトリでこのパスが後処理の 43〜53% を占めると
/// 分かったので、何にその時間を使っているかを実データ3件で測った:
///  - 3件とも <b>maxMillis を使い切って終わる</b>（候補が 4.5〜7.3 万件も作れるので尽きない）。
///    兄弟の <c>PersonalBalanceJointLnsPolish</c> が数百ms〜1.3秒で終わるのは候補空間が狭くて尽きるため。
///  - <b>golden と user は best を一度も更新しないまま 7.4〜7.7 秒を使い切る</b>（＝全部が空回り）。
///    real だけが 2.9s / 4.4s / 6.8s の3回改善する。
///  - 候補の 43〜52% は debt 予算で捨てているが、その判定は<b>フル checker を呼んだ後</b>なので
///    捨てる候補にも全額払っている（安く先に判定する方法が無いため現状は許容する）。
/// → 最良が <see cref="Config.PatienceMs"/> 更新されなければ打ち切る。既定 4 秒は real の
///   「最初の改善まで 2.9 秒」に 1.4 倍の余裕を持たせた値。実データ3件で
///   <b>最終盤面は patience 3/4/5 秒とも現行と完全に一致</b>し、後処理全体は
///   golden 16.3→13.3s・user 19.0→15.3s（real は改善が続くので打ち切られず不変）。
///   keep-best は不変＝早く止めるだけで退化しない。
/// </summary>
internal static class C1JointLnsPolish
{
    /// <summary>
    /// 下界用 suffix-DP の状態セル上限。dp と next を同時に持つため、これを超える窓は
    /// 正確さより探索予算を守る安価な保守的下界へ退避する。
    /// </summary>
    private const long MaxExactLowerBoundCells = 262_144L;

    public sealed record Config(
        int TargetReductionPercent = 50,
        int BeamWidth = 24,
        int MaxDepth = 5,
        int MaxRestarts = 4,
        int MaxGoals = 36,
        int MaxMovesPerGoal = 24,
        int HardDebt = 1,
        int TotalDebt = 12,
        int C1Debt = 4,
        long MaxMillis = 8_000L,
        /// <summary>最良がこの時間更新されなければ打ち切る（0以下＝無効）。既定の根拠はクラスの doc comment。</summary>
        long PatienceMs = 4_000L);

    private enum GoalKind { C1, Temporal, Coverage, RangeLow }

    private sealed record Goal(int Staff, int Day, int TargetShift, int Weight, GoalKind Kind);

    private abstract record Move
    {
        private Move() { }

        public sealed record Direct(int Staff, int Day, int Target) : Move;
        public sealed record SameDaySwap(int A, int B, int Day) : Move;
        public sealed record Rotate3(int Receiver, int Donor, int Bridge, int Day) : Move;
        public sealed record SelfDaySwap(int Staff, int DayA, int DayB) : Move;
        public sealed record CrossDayTransfer(int Receiver, int ReceiveDay, int Donor, int DonateDay) : Move;
    }

    private sealed record Node(
        int[][] Schedule,
        ViolationReport Report,
        int C1,
        IReadOnlyList<Move> Path,
        int ChangedCells);

    private sealed record SeenState(int[][] Schedule, Node Node);

    public static V6HotfixPasses.CyclicSwapResult Apply(
        MagiState state,
        int[][] schedule,
        Config? config = null,
        Func<bool>? shouldStop = null,
        long seed = 0xC1A11L)
    {
        var cfg = config ?? new Config();
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var rootSchedule = ScheduleUtil.NormalizeSchedule(schedule, p);
        var rootReport = UnifiedViolationChecker.Check(state, rootSchedule);
        int rootC1 = rootReport.Breakdown.GetValueOrDefault("c1", 0);
        if (p.Cons1.Count == 0 || rootC1 <= 0 || p.T <= 0 || p.S <= 0)
        {
            return new V6HotfixPasses.CyclicSwapResult(
                rootSchedule, rootReport.Total, rootReport.Total, 0,
                new[] { new MirrorLog(tag: "C1JointLNS", message: "期間要件(c1)対象なし=スキップ") });
        }

        // [3.350.0/敵対検証] 「目的関数は採用を認めたのにピンだけが止めた」件数を対象別に記録する。
        //   旧: このパスは exactPinRegression で却下するだけで一切数えておらず、UI の
        //   observedPinBlockedAttempts / pinTargets から丸ごと抜けていた（実データ real_state で
        //   1,898件＝V6HotfixPasses 側の計測値の30倍以上が見えていなかった）。
        var pinBlocks = new PinBlockAttribution();

        if (cfg.BeamWidth <= 0 || cfg.MaxDepth <= 0 || cfg.MaxRestarts <= 0 ||
            cfg.MaxGoals <= 0 || cfg.MaxMovesPerGoal <= 0 || cfg.MaxMillis <= 0L)
        {
            return new V6HotfixPasses.CyclicSwapResult(
                rootSchedule, rootReport.Total, rootReport.Total, 0,
                new[] { new MirrorLog(tag: "C1JointLNS", message: "探索上限0=明示的に無効") });
        }
        int width = cfg.BeamWidth;
        int depthLimit = cfg.MaxDepth;
        int restartLimit = cfg.MaxRestarts;
        int goalLimit = cfg.MaxGoals;
        int moveLimit = cfg.MaxMovesPerGoal;
        long budgetMillis = Math.Min(cfg.MaxMillis, 60_000L);

        // [System.nanoTime() 移植] Java/Kotlin の nanoTime は「任意原点・差分のみ意味を持つ」単調カウンタ。
        //   同じ契約を Stopwatch の生ティックで表現する（絶対ナノ秒へ変換しない＝
        //   ticks * 1_000_000_000L のような掛け算を先にすると、周波数が高い環境や稼働時間が長い
        //   プロセスで long をオーバーフローしうるため）。ミリ秒設定値だけを安全にティックへ変換する。
        long TicksFromMillis(long ms) => ms <= 0L ? 0L : ms * System.Diagnostics.Stopwatch.Frequency / 1000L;
        long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        long deadline = startTicks + TicksFromMillis(budgetMillis);
        // [3.342.0] 最良が patienceMs 更新されなければ打ち切る。keep-best は不変＝早く止めるだけ。
        long lastImproveTicks = startTicks;
        long patienceTicks = cfg.PatienceMs > 0L ? TicksFromMillis(cfg.PatienceMs) : long.MaxValue;
        bool Stalled() => patienceTicks != long.MaxValue &&
            System.Diagnostics.Stopwatch.GetTimestamp() - lastImproveTicks >= patienceTicks;
        bool Stopped() => stop() || System.Diagnostics.Stopwatch.GetTimestamp() >= deadline || Stalled();

        int lowerBound = StructuralC1LowerBound(p);
        int improvable = Math.Max(rootC1 - lowerBound, 0);
        int pct = Math.Clamp(cfg.TargetReductionPercent, 1, 100);
        int targetC1 = rootC1 - ((improvable * pct + 99) / 100);

        var root = new Node(rootSchedule.Copy2D(), rootReport, rootC1, new List<Move>(), 0);
        var best = root;
        int expanded = 0;
        int generated = 0;
        int debtRejected = 0;
        // [debt除外の内訳, 3.302.0] 候補の過半が debt 予算で落ちるため、どの予算（必須/合計/c1）で
        //   切られたのかと、必須超過のとき何を壊したのかをログへ出す（実機ログの「debt除外15689」だけでは
        //   c1 を下げる手が何と衝突しているのか読めなかった）。判定順＝下の if と同じく 必須→合計→c1。
        int debtHard = 0;
        int debtTotal = 0;
        int debtC1 = 0;
        var debtCulpritOrder = new List<string>();
        var debtCulprits = new Dictionary<string, int>();
        int duplicateRejected = 0;
        int restartsDone = 0;

        for (int restart = 0; restart < restartLimit; restart++)
        {
            if (Stopped() || best.C1 <= lowerBound) break;
            restartsDone++;
            var rng = new JavaRandom(seed ^ ((long)restart * -0x61c8864680b583ebL));
            List<Node> beam = ReferenceEquals(best, root) ? new List<Node> { root } : new List<Node> { root, best };
            var seen = new Dictionary<long, List<SeenState>>();
            foreach (var n in beam) Remember(seen, n);

            for (int depth = 0; depth < depthLimit; depth++)
            {
                if (Stopped()) break;
                var children = new List<Node>();
                foreach (var parent in beam)
                {
                    if (Stopped()) break;
                    expanded++;
                    var goals = CollectGoals(p, parent.Schedule, goalLimit, rng, includeTemporal: parent.Path.Count == 0);
                    foreach (var goal in goals)
                    {
                        if (Stopped()) break;
                        var moves = GenerateMoves(p, parent.Schedule, goal, moveLimit, rng);
                        foreach (var move in moves)
                        {
                            if (Stopped()) break;
                            var next = parent.Schedule.Copy2D();
                            if (!ApplyMove(next, move)) continue;
                            generated++;
                            var report = UnifiedViolationChecker.Check(state, next);
                            int c1 = report.Breakdown.GetValueOrDefault("c1", 0);
                            bool overHard = report.Hard > rootReport.Hard + Math.Max(cfg.HardDebt, 0);
                            bool overTotal = report.Total > rootReport.Total + Math.Max(cfg.TotalDebt, 0);
                            bool overC1 = c1 > rootC1 + Math.Max(cfg.C1Debt, 0);
                            if (overHard || overTotal || overC1)
                            {
                                debtRejected++;
                                if (overHard)
                                {
                                    debtHard++;
                                    var fam = V6SearchOperators.WorstWorsenedFamily(report, rootReport);
                                    if (fam != null)
                                    {
                                        if (!debtCulprits.ContainsKey(fam)) debtCulpritOrder.Add(fam);
                                        debtCulprits[fam] = debtCulprits.GetValueOrDefault(fam, 0) + 1;
                                    }
                                }
                                else if (overTotal) debtTotal++;
                                else debtC1++;
                                continue;
                            }
                            var childPath = new List<Move>(parent.Path.Count + 1);
                            childPath.AddRange(parent.Path);
                            childPath.Add(move);
                            var child = new Node(next, report, c1, childPath, ChangedCellCount(rootSchedule, next));
                            if (!Remember(seen, child))
                            {
                                duplicateRejected++;
                                continue;
                            }
                            children.Add(child);

                            bool finalCandidate = IsFinalCandidate(p, child, root, pinBlocks);
                            if (finalCandidate && Better(child.Report, best.Report))
                            {
                                best = child;
                                lastImproveTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                            }
                        }
                    }
                }
                if (children.Count == 0) break;
                beam = SelectBeam(children, rootReport, lowerBound, width, rng);
            }
        }

        // Defensive re-check. A shared-array bug or future operator mistake can never escape this gate.
        var finalReport = UnifiedViolationChecker.Check(state, best.Schedule);
        int finalC1 = finalReport.Breakdown.GetValueOrDefault("c1", 0);
        bool valid = !ReferenceEquals(best, root) && finalC1 < rootC1 && Better(finalReport, rootReport) &&
            !pinBlocks.BlocksImproving(p, rootSchedule, best.Schedule);
        var chosen = valid ? best.Schedule.Copy2D() : rootSchedule.Copy2D();
        var chosenReport = valid ? finalReport : rootReport;
        int chosenC1 = valid ? finalC1 : rootC1;
        int progress = improvable <= 0 ? 100 :
            Math.Clamp((Math.Max(rootC1 - chosenC1, 0) * 100) / improvable, 0, 100);
        // [receiving-code-review] 返却盤面(chosenC1)基準。以前は探索中に一度でもtargetC1へ届いた
        // 中間候補があれば恒久trueになる targetSeen フラグを表示しており、その後 better() がより
        // 良い(だがc1はtargetC1超の)候補へ best を差し替えても「到達」と表示され続けていた。
        bool targetReached = chosenC1 <= targetC1;

        string stopReason = true switch
        {
            _ when chosenC1 <= lowerBound => "構造下限到達",
            _ when stop() => "外部停止",
            _ when System.Diagnostics.Stopwatch.GetTimestamp() >= deadline => "期限",
            _ when Stalled() => $"最良が{cfg.PatienceMs}ms更新されず打ち切り",
            _ => "探索停滞",
        };
        string debtCulpritsTxt = debtCulpritOrder.Count == 0 ? "" :
            " 必須の主因 " + string.Join(" ",
                debtCulpritOrder.OrderByDescending(k => debtCulprits[k]).Take(2)
                    .Select(k => $"{k}:{debtCulprits[k]}"));
        string debtTxt = debtRejected == 0 ? "" : $"(必須{debtHard} 合計{debtTotal} c1 {debtC1}{debtCulpritsTxt})";
        var log = new MirrorLog(
            tag: "C1JointLNS",
            message: $"期間要件(c1)共同LNS: c1 {rootC1}->{chosenC1} (構造下限≥{lowerBound}, 改善可能幅進捗{progress}%, 50%目標={(targetReached ? "到達" : "未達")})" +
                $" / total {rootReport.Total}->{chosenReport.Total} HARD {rootReport.Hard}->{chosenReport.Hard}" +
                $" 採用{(valid ? 1 : 0)}束 手数{(valid ? best.Path.Count : 0)}" +
                $" restart{restartsDone} 展開{expanded} 候補{generated} debt除外{debtRejected}" +
                debtTxt +
                $" 重複除外{duplicateRejected} 停止={stopReason}" +
                (valid ? "" : " [頭打ち=正式目的を改善するC1減少束なし]"));
        return new V6HotfixPasses.CyclicSwapResult(
            chosen, rootReport.Total, chosenReport.Total, valid ? 1 : 0, new[] { log },
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }

    /// <summary>
    /// [3.350.0] <paramref name="pinBlocks"/> を渡すと「目的関数は採用を認めたのにピンだけが止めた」件数を
    /// 記録する。短絡評価により、<c>BlocksImproving</c> を呼ぶ時点で c1 減少と <c>Better</c> は確定済み＝
    /// <see cref="PinBlockAttribution"/> の契約（採用が認められた手だけを数える）を満たす。
    /// </summary>
    private static bool IsFinalCandidate(Problem p, Node node, Node root, PinBlockAttribution? pinBlocks = null) =>
        node.C1 < root.C1 && Better(node.Report, root.Report) &&
            !(pinBlocks?.BlocksImproving(p, root.Schedule, node.Schedule)
                ?? V6SearchOperators.ExactPinRegression(p, root.Schedule, node.Schedule));

    // [3.287.0 keep-best統一] hard→weighted→total（MirrorCore.betterReport）
    private static bool Better(ViolationReport a, ViolationReport b) => UnifiedViolationChecker.BetterReport(a, b);

    /// <summary>
    /// Optimistic C1 lower bound. Each staff/rule is minimized independently under wishes,
    /// capability and that shift's monthly range. Summing independent minima is still a valid
    /// lower bound for the combined C1 objective (it may be loose, never overstates feasibility).
    /// </summary>
    internal static int StructuralC1LowerBound(Problem p)
    {
        int total = 0;
        foreach (var c in p.Cons1)
        {
            if (c.Day1 <= 0 || c.Day1 > p.T || c.ShiftIdx < 0 || c.ShiftIdx >= p.K) continue;
            for (int i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, c.ShiftIdx)) continue;
                total += SingleRuleLowerBound(p, i, c);
            }
        }
        return total;
    }

    private static int SingleRuleLowerBound(Problem p, int staff, C1 c)
    {
        int d = c.Day1;
        // [3.312.0] 旧実装は rangeLo/rangeHi を count の硬い上下限として DP に課していた。
        //   しかし個人回数は SOFT（low=90 / high=45）で、c1(30) より重いだけであって禁止ではない。
        //   結果この値は「rangeHi を一度も超えない範囲での c1 最小値」＝真の下限より大きくなり、
        //   `best.c1 <= lowerBound` の早期終了と「構造下限到達」のログを誤って発火させていた。
        //   反例: T=7・「4日窓で X>=1」・high(X)=0 なら、X なし＝c1=4(weighted 60) に対し
        //   中央へ X を1つ置くと c1=0・high=1(weighted 45) で betterReport は X を選ぶのに、
        //   旧下限は 4 を返して探索を止めていた。
        //   wishLocked は下限に残す：希望を破る代金は pref=9000 で、c1=30 を 300 件消して初めて
        //   釣り合う＝c1 を下げる目的では実質的に硬い制約。
        int hi = p.T;

        // Exact suffix-mask DP is valuable for ordinary monthly windows, but its table is
        // O(min(T,hi) * 2^(d-1)). At d=20,T=31,hi=31 it allocates more than 100MB across dp/next and can
        // consume the whole LNS budget before a single candidate is explored. A local
        // impossibility scan is a weaker but still valid lower bound.
        if (d > 20) return CheapSingleRuleLowerBound(p, staff, c);
        int suffixBits = Math.Max(d - 1, 0);
        int maskLimit = suffixBits == 0 ? 1 : (1 << suffixBits);
        long dpCells = ((long)hi + 1L) * (long)maskLimit;
        if (dpCells > MaxExactLowerBoundCells) return CheapSingleRuleLowerBound(p, staff, c);
        int maskKeep = maskLimit - 1;
        const int inf = 1_000_000;
        var dp = new int[hi + 1][];
        for (int cc = 0; cc <= hi; cc++) { dp[cc] = new int[maskLimit]; Array.Fill(dp[cc], inf); }
        dp[0][0] = 0;
        for (int day = 0; day < p.T; day++)
        {
            var next = new int[hi + 1][];
            for (int cc = 0; cc <= hi; cc++) { next[cc] = new int[maskLimit]; Array.Fill(next[cc], inf); }
            int wished = p.Wish[staff][day];
            bool locked = p.WishLocked(staff, day);
            int minBit = locked ? (wished == c.ShiftIdx ? 1 : 0) : 0;
            int maxBit = locked ? minBit : 1;
            for (int cnt = 0; cnt <= Math.Min(day, hi); cnt++)
            {
                for (int mask = 0; mask < maskLimit; mask++)
                {
                    int baseVal = dp[cnt][mask];
                    if (baseVal >= inf) continue;
                    for (int bit = minBit; bit <= maxBit; bit++)
                    {
                        int nc = cnt + bit;
                        if (nc > hi) continue;
                        int windowPenalty = 0;
                        if (day + 1 >= d)
                        {
                            int ones = System.Numerics.BitOperations.PopCount((uint)mask) + bit;
                            windowPenalty = ones < c.Day2 ? 1 : 0;
                        }
                        int nm = suffixBits == 0 ? 0 : ((mask << 1) | bit) & maskKeep;
                        int v = baseVal + windowPenalty;
                        if (v < next[nc][nm]) next[nc][nm] = v;
                    }
                }
            }
            dp = next;
        }
        int best = inf;
        for (int cnt = 0; cnt <= hi; cnt++)
            for (int mask = 0; mask < maskLimit; mask++)
                best = Math.Min(best, dp[cnt][mask]);
        return best >= inf ? 0 : best;
    }

    /// <summary>
    /// DP を使えない長窓用の保守的下界。各窓について、<b>希望固定だけ</b>から見て物理的に必要回数へ
    /// 届かない場合を数える。窓間の相互作用は無視するため過大評価しない。
    ///
    /// [3.312.0] 個人上限(rangeHi)は見ない。SOFT を硬い上限として扱うと真の下限を上回り、
    /// 呼出側の早期終了を誤って発火させる（旧: <c>if (hi &lt; c.day2) return starts</c> ＝全窓を不可避と宣言）。
    /// </summary>
    private static int CheapSingleRuleLowerBound(Problem p, int staff, C1 c)
    {
        int d = c.Day1;
        if (d <= 0 || d > p.T || c.Day2 <= 0) return 0;
        int starts = p.T - d + 1;
        int unavoidable = 0;
        for (int start = 0; start < starts; start++)
        {
            int possible = 0;
            for (int day = start; day < start + d; day++)
                if (!p.WishLocked(staff, day) || p.Wish[staff][day] == c.ShiftIdx) possible++;
            if (possible < c.Day2) unavoidable++;
        }
        return unavoidable;
    }

    private static List<Goal> CollectGoals(
        Problem p, int[][] schedule, int limit, JavaRandom rng, bool includeTemporal)
    {
        var map = new Dictionary<(GoalKind Kind, int Staff, int Day, int Shift), Goal>();
        var goalOrder = new List<(GoalKind Kind, int Staff, int Day, int Shift)>();
        void Add(int i, int j, int x, int weight, GoalKind kind)
        {
            if (i < 0 || i >= p.S || j < 0 || j >= p.T || x < 0 || x >= p.K) return;
            if (schedule[i][j] == x || !Allowed(p, i, j, x)) return;
            var key = (kind, i, j, x);
            if (!map.TryGetValue(key, out var old) || weight > old.Weight)
            {
                if (old is null) goalOrder.Add(key);
                map[key] = new Goal(i, j, x, weight, kind);
            }
        }

        // C1 deficits. Weight counts how many deficient windows need this cell.
        var c1Weight = new Dictionary<(int Staff, int Day, int Shift), int>();
        foreach (var c in p.Cons1)
        {
            int d = c.Day1; int n = c.Day2; int x = c.ShiftIdx;
            if (d <= 0 || d > p.T || x < 0 || x >= p.K) continue;
            for (int i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, x)) continue;
                for (int start = 0; start <= p.T - d; start++)
                {
                    int count = 0;
                    for (int j = start; j < start + d; j++) if (schedule[i][j] == x) count++;
                    if (count >= n) continue;
                    for (int j = start; j < start + d; j++)
                    {
                        if (schedule[i][j] == x || !Allowed(p, i, j, x)) continue;
                        var key = (i, j, x);
                        c1Weight[key] = c1Weight.GetValueOrDefault(key, 0) + Math.Max(n - count, 1);
                    }
                }
            }
        }
        foreach (var (k, w) in c1Weight) Add(k.Staff, k.Day, k.Shift, 100 + w, GoalKind.C1);

        // Reuse the existing exact temporal DP as a proposal oracle, but only at root-like
        // nodes. The DP does not commit a schedule; its desired incoming target days become
        // goals inside this joint beam, where coverage/range/c3 side effects can be repaired.
        if (includeTemporal)
        {
            var rankedPairs = c1Weight
                .GroupBy(kv => (Staff: kv.Key.Staff, Shift: kv.Key.Shift))
                .Select(g => (Pair: g.Key, Weight: g.Sum(kv => kv.Value)))
                .OrderByDescending(t => t.Weight)
                .Take(4);
            foreach (var (pair, weight) in rankedPairs)
            {
                int i = pair.Staff; int x = pair.Shift;
                var rules = p.Cons1.Where(r => r.ShiftIdx == x)
                    .Select(r => new C1TemporalDp.Rule(r.Day1, r.Day2)).ToList();
                // [3.278.0/監査修正] 生 wish>=0 は実現不能な希望（担当外シフトへの希望）まで固定扱いし、
                //   DP 提案オラクルを過剰ロックしていた（同ファイル他2サイトは 3.264.0 で wishLocked へ統一済みの
                //   retrofit 漏れ第3サイト）。wishLocked = 実現可能な希望のみ凍結（規約どおり）。
                var locked = new bool[p.T];
                for (int day = 0; day < p.T; day++) locked[day] = p.WishLocked(i, day);
                var proposal = C1TemporalDp.Solve(
                    row: schedule[i], targetShift: x, rules: rules, locked: locked,
                    maxRelocations: 6, seed: rng.NextLong(), maxExactWindow: 20);
                if (proposal is null) continue;
                for (int day = 0; day < p.T; day++)
                    if (proposal.TargetDays[day] && schedule[i][day] != x)
                        Add(i, day, x, 150 + weight, GoalKind.Temporal);
            }
        }

        // Coverage shortages are HARD side effects that often block a C1 move. Include them in
        // the same beam so a C1 move and its coverage repair can be completed as one bundle.
        for (int j = 0; j < p.T; j++)
        {
            var got = new int[p.K];
            for (int i = 0; i < p.S; i++) { int k = schedule[i][j]; if (k >= 0 && k < p.K) got[k]++; }
            for (int x = 0; x < p.K; x++)
            {
                int shortage = p.CovUCell(x, j, got[x]);
                if (shortage <= 0) continue;
                for (int i = 0; i < p.S; i++) Add(i, j, x, 200 + shortage, GoalKind.Coverage);
            }
        }

        // Monthly lower-range shortages. They are SOFT but frequently counterbalance C1/range-high.
        // [監査で発見・3.270.0] normalizeSchedule はセンチネル -1 を作りうる（削除済シフトの残存index等）
        //   ため、生の schedule[i][j] を無検証で配列添字に使うとAIOOBEになりうる。ガード追加。
        var counts = new int[p.S][];
        for (int i = 0; i < p.S; i++) counts[i] = new int[p.K];
        for (int i = 0; i < p.S; i++)
            for (int j = 0; j < p.T; j++) { int k = schedule[i][j]; if (k >= 0 && k < p.K) counts[i][k]++; }
        for (int i = 0; i < p.S; i++)
            for (int x = 0; x < p.K; x++)
            {
                int lo = p.RangeLo[i][x];
                if (lo == int.MinValue || counts[i][x] >= lo) continue;
                int deficit = lo - counts[i][x];
                for (int j = 0; j < p.T; j++) Add(i, j, x, 50 + deficit, GoalKind.RangeLow);
            }

        // Stratified round-robin prevents one staff/rule from occupying every goal slot.
        var groups = goalOrder.Select(k => map[k])
            .GroupBy(g => (g.Kind, g.Staff, g.TargetShift))
            .Select(grp => grp.ToList().Shuffled(rng).OrderByDescending(g => g.Weight).ToList())
            .ToList()
            .Shuffled(rng);
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

    private static List<Move> GenerateMoves(Problem p, int[][] schedule, Goal goal, int limit, JavaRandom rng)
    {
        int i = goal.Staff; int j = goal.Day; int x = goal.TargetShift;
        int a = schedule[i][j];
        if (a == x || !Allowed(p, i, j, x)) return new List<Move>();
        // [賢く再構成] 全Move種の共通効果=「iのday jにxを置く」がこの時点で既に禁止連続(c3n)を
        // 作るなら、このgoal自体を即座に諦める(手を1つも生成しない)。従来はdebt+最終ゲート
        // (isFinalCandidate/defensive re-check)だけに頼っており、正しさは常に保たれていたが、
        // c3n を作るとhard debtを使い切る候補ばかり生成してしまい、maxMovesPerGoalの枠が
        // 無駄な候補で埋まっていた。事前に弾くのは効率のみの改善＝最終正しさは無関係(不変)。
        if (p.MakesForbiddenRun(schedule, i, j, x)) return new List<Move>();
        var scored = new List<(int Score, Move Move)>();

        // Elastic move. It may temporarily create coverage debt; later goals can repair it.
        scored.Add((20, new Move.Direct(i, j, x)));

        var staffOrder = Enumerable.Range(0, p.S).Shuffled(rng);
        foreach (int donor in staffOrder)
        {
            if (donor == i || schedule[donor][j] != x || !Allowed(p, donor, j, a)) continue;
            // [賢く再構成] donorがaを受け取る側の禁止連続も同様に事前に弾く。
            if (p.MakesForbiddenRun(schedule, donor, j, a)) continue;
            scored.Add((100, new Move.SameDaySwap(i, donor, j)));
        }

        foreach (int donor in staffOrder)
        {
            if (donor == i || schedule[donor][j] != x) continue;
            foreach (int bridge in staffOrder)
            {
                if (bridge == i || bridge == donor) continue;
                int y = schedule[bridge][j];
                if (y == x || y == a) continue;
                if (!Allowed(p, donor, j, y) || !Allowed(p, bridge, j, a)) continue;
                if (p.MakesForbiddenRun(schedule, donor, j, y) || p.MakesForbiddenRun(schedule, bridge, j, a)) continue;
                scored.Add((80, new Move.Rotate3(i, donor, bridge, j)));
            }
        }

        var dayOrder = Enumerable.Range(0, p.T).Shuffled(rng);
        foreach (int otherDay in dayOrder)
        {
            if (otherDay == j || schedule[i][otherDay] != x || !Allowed(p, i, otherDay, a)) continue;
            // [賢く再構成] iがotherDayでaに戻る側も事前チェック(同一職員の別日、元盤面基準の
            // 保守的近似＝jとotherDayが同一窓に入る稀なケースを見逃しても最終checkerが必ず拾う)。
            if (p.MakesForbiddenRun(schedule, i, otherDay, a)) continue;
            scored.Add((70, new Move.SelfDaySwap(i, j, otherDay)));
        }

        // Cross-day token transfer: receiver gets x on j, donor gives x on another day and gets a.
        // Global monthly shift totals stay fixed while per-day coverage can move, which the old
        // same-day-only bundle could not express.
        foreach (int donor in staffOrder)
            foreach (int otherDay in dayOrder)
            {
                if (donor == i && otherDay == j) continue;
                if (schedule[donor][otherDay] != x) continue;
                if (!Allowed(p, donor, otherDay, a)) continue;
                if (p.MakesForbiddenRun(schedule, donor, otherDay, a)) continue;
                scored.Add((60, new Move.CrossDayTransfer(i, j, donor, otherDay)));
            }

        return scored.Shuffled(rng)
            .OrderByDescending(t => t.Score)
            .Select(t => t.Move)
            .Distinct()
            .Take(limit)
            .ToList();
    }

    private static bool ApplyMove(int[][] schedule, Move move)
    {
        switch (move)
        {
            case Move.Direct d:
                if (schedule[d.Staff][d.Day] == d.Target) return false;
                schedule[d.Staff][d.Day] = d.Target;
                return true;
            case Move.SameDaySwap s:
            {
                int x = schedule[s.A][s.Day];
                int y = schedule[s.B][s.Day];
                if (x == y) return false;
                schedule[s.A][s.Day] = y;
                schedule[s.B][s.Day] = x;
                return true;
            }
            case Move.Rotate3 r:
            {
                int a = schedule[r.Receiver][r.Day];
                int x = schedule[r.Donor][r.Day];
                int y = schedule[r.Bridge][r.Day];
                if (a == x || x == y || y == a) return false;
                schedule[r.Receiver][r.Day] = x;
                schedule[r.Donor][r.Day] = y;
                schedule[r.Bridge][r.Day] = a;
                return true;
            }
            case Move.SelfDaySwap w:
            {
                int a = schedule[w.Staff][w.DayA];
                int b = schedule[w.Staff][w.DayB];
                if (a == b) return false;
                schedule[w.Staff][w.DayA] = b;
                schedule[w.Staff][w.DayB] = a;
                return true;
            }
            case Move.CrossDayTransfer c:
            {
                int a = schedule[c.Receiver][c.ReceiveDay];
                int x = schedule[c.Donor][c.DonateDay];
                if (a == x) return false;
                schedule[c.Receiver][c.ReceiveDay] = x;
                schedule[c.Donor][c.DonateDay] = a;
                return true;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(move), move, null);
        }
    }

    private static bool Allowed(Problem p, int staff, int day, int shift)
    {
        int wish = p.Wish[staff][day];
        return p.WishLocked(staff, day) ? wish == shift : p.MayPlace(staff, shift);
    }

    private static List<Node> SelectBeam(
        List<Node> children, ViolationReport root, int lowerBound, int width, JavaRandom rng)
    {
        var official = children
            .OrderBy(n => n.Report.Hard)
            .ThenBy(n => n.Report.WeightedScore)
            .ThenBy(n => n.Report.Total)
            .ThenBy(n => n.C1)
            .ThenBy(n => n.ChangedCells)
            .Take(Math.Max(1, width / 2))
            .ToList();

        var c1Front = children.Shuffled(rng)
            .OrderBy(n => Math.Max(n.Report.Hard - root.Hard, 0))
            .ThenBy(n => Math.Max(n.C1 - lowerBound, 0))
            .ThenBy(n => Math.Max(n.Report.Total - root.Total, 0))
            .ThenBy(n => n.Report.Hard)
            .ThenBy(n => n.Report.WeightedScore)
            .ThenBy(n => n.Report.Total)
            .ThenBy(n => n.ChangedCells)
            .Take(Math.Max(1, width - official.Count))
            .ToList();

        var outNodes = new List<Node>();
        foreach (var n in official.Concat(c1Front))
            if (!outNodes.Any(o => SameSchedule(o.Schedule, n.Schedule)))
                outNodes.Add(n);
        return outNodes.Take(width).ToList();
    }

    private static bool Remember(Dictionary<long, List<SeenState>> seen, Node node)
    {
        long h = ScheduleHash(node.Schedule);
        if (!seen.TryGetValue(h, out var bucket))
        {
            bucket = new List<SeenState>();
            seen[h] = bucket;
        }
        if (bucket.Any(s => SameSchedule(s.Schedule, node.Schedule))) return false;
        bucket.Add(new SeenState(node.Schedule.Copy2D(), node));
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
