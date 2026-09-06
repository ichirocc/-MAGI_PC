using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// 希望島研磨の探索予算と打ち切り（Android 3.498.0 の <c>WishIslandPolish.Params</c> と同じ意味・同じ既定値）。
    /// </summary>
    /// <param name="MaxPasses">島を一巡する回数の上限。採用 0 の pass はビームへ進み、ビームでも改善しなければ終了する。</param>
    /// <param name="MaxEvaluations">正式評価（<c>UnifiedViolationChecker.Check</c>）の総数の上限＝研磨全体の時間予算。</param>
    /// <param name="BeamWidth">停滞時ビームの幅。</param>
    /// <param name="BeamDepth">停滞時ビームの深さ。3 は「両翼＋同日」程度の複合手を 1 本で表せる最小値。</param>
    /// <param name="MinIslandBudget">起動した島 1 つに保証する評価数。同日交換の候補を数手は試せる量として 8。</param>
    /// <param name="BeamBranchFactor">ビーム 1 段で保持する中立手の上限（幅の倍率。残り予算で頭打ち＝<see cref="WishBeamCandidateLimit"/>）。中立手は無数にあるので打ち切りが要る。</param>
    /// <param name="StuckNamesShown">ログに名前を出す残存職員の上限。</param>
    public sealed record WishIslandParams(
        int MaxPasses = 3, int MaxEvaluations = 120, int BeamWidth = 4, int BeamDepth = 3,
        int MinIslandBudget = 8, int BeamBranchFactor = 6, int StuckNamesShown = 8)
    {
        /// <summary>不正な設定でも研磨パスが落ちないように下限へ丸める（負の予算＝何もしない、幅 0 のビーム＝幅 1）。</summary>
        public WishIslandParams Normalized() => this with
        {
            MaxPasses = Math.Max(0, MaxPasses), MaxEvaluations = Math.Max(0, MaxEvaluations),
            BeamWidth = Math.Max(1, BeamWidth), BeamDepth = Math.Max(0, BeamDepth),
            MinIslandBudget = Math.Max(1, MinIslandBudget), BeamBranchFactor = Math.Max(1, BeamBranchFactor),
            StuckNamesShown = Math.Max(0, StuckNamesShown),
        };
    }

    /// <summary>
    /// 希望島研磨（ユーザー提示の確定仕様・Android 3.496.0、構造の見直し 3.498.0 の移植）。
    /// 実現可能な希望日（<c>WishLocked</c>）を固定アンカーにし、影響半径（c1 窓長・c3 系パターン長）が重なる希望を島へ統合、
    /// 周辺か本人の回数に違反がある島だけ起動。手は 同日→窓→両翼→必要時のみ巡回、同じ所属を先に。採否は HARD→weighted→total
    /// で、通常は希望周辺も全体も改善する手だけ。停滞時のみ短いビーム（途中は中立手可）。keep-best。0..T 内で完結。
    /// </summary>
    public static CyclicSwapResult ApplyWishIslandPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, int maxEvaluations = 120, int beamWidth = 4, int beamDepth = 3, Func<bool>? shouldStop = null)
        => ApplyWishIslandPolish(state, schedule, new WishIslandParams(maxPasses, maxEvaluations, beamWidth, beamDepth), shouldStop);

    public static CyclicSwapResult ApplyWishIslandPolish(MagiState state, int[][] schedule, WishIslandParams prm, Func<bool>? shouldStop = null)
        => new WishIslandSession(state, schedule, prm, shouldStop ?? (() => false)).Run();

    /// <summary>テスト用: 各島の通常候補（同日・窓・両翼）を (種類, セル列) で列挙する。月初・月末で両翼が出ないこと等を固定する。</summary>
    internal static IEnumerable<(string Kind, int[] Cells)> EnumerateWishMovesForTest(MagiState state, int[][] schedule)
        => new WishIslandSession(state, schedule, new WishIslandParams(), () => false).MovesForTest();

    /// <summary>ビーム 1 段で保持する候補数＝幅×分岐を残り予算で頭打ち（いずれも 1 以上に丸める）。</summary>
    internal static int WishBeamCandidateLimit(int width, int branchFactor, int remainingEvaluations)
    {
        var safeWidth = Math.Max(width, 1);
        var safeBranch = Math.Max(branchFactor, 1);
        var safeRemaining = Math.Max(remainingEvaluations, 1);
        return (int)Math.Min((long)safeWidth * safeBranch, safeRemaining);
    }

    private enum WishMoveKind { SameDay, Window, Wings, Rotate3 }
    private static string WishMoveLabel(WishMoveKind k) => k switch { WishMoveKind.SameDay => "同日", WishMoveKind.Window => "窓", WishMoveKind.Wings => "両翼", _ => "巡回" };

    /// <summary>1手＝(職員, 日, 新しい値) の三つ組の並び。適用と巻き戻しが同じ形でできる。</summary>
    private sealed record WishMove(WishMoveKind Kind, int[] Cells, bool SameGroup);

    /// <summary>職員 Staff の希望日と影響範囲 ZoneFrom..ZoneTo（当月内に切り詰め済み）。キーは局所スコア用に事前計算。</summary>
    private sealed class WishIsland
    {
        public readonly int Staff; public readonly int[] WishDays; public readonly int ZoneFrom; public readonly int ZoneTo;
        public readonly string[] ZoneKeys; public readonly string CountPrefix;
        public WishIsland(int staff, int[] wishDays, int zoneFrom, int zoneTo)
        {
            Staff = staff; WishDays = wishDays; ZoneFrom = zoneFrom; ZoneTo = zoneTo;
            ZoneKeys = Enumerable.Range(zoneFrom, zoneTo - zoneFrom + 1).Select(d => $"{staff},{d}").ToArray();
            CountPrefix = $"{staff},";
        }
    }

    private sealed record WishNode(int[][] Board, ViolationReport Rep);
    private sealed record WishChosen(WishMove Move, ViolationReport Rep);

    private sealed class WishIslandSession
    {
        private readonly MagiState state; private readonly int[][] input; private readonly WishIslandParams prm; private readonly Func<bool> stop;
        private readonly Problem p; private readonly int[][] work; private readonly ViolationReport before;
        private ViolationReport bestRep;
        private readonly int T, S, K, reach;
        /// <summary>島は希望（固定）だけで決まり盤面に依らないので 1 回だけ作る。</summary>
        private readonly List<WishIsland> islands;
        private readonly PinBlockAttribution pinBlocks = new();
        private readonly RejectCulpritStats rejectCulprits = new();
        private readonly List<string> stuck = new();
        private readonly Dictionary<string, int> byKind = new();
        private int applied, evaluated, beamEvaluated, beamRuns, beamApplied, prunedC3n, activeCount;
        /// <summary>今の島で使った評価数（通常候補と巡回候補で 1 つの枠を分け合う）。</summary>
        private int islandUsed;

        public WishIslandSession(MagiState state, int[][] schedule, WishIslandParams prm, Func<bool> stop)
        {
            this.state = state; input = schedule; this.prm = prm.Normalized(); this.stop = stop;
            p = new Problem(state);
            work = ScheduleUtil.NormalizeSchedule(schedule, p);
            before = UnifiedViolationChecker.Check(state, work);
            bestRep = before;
            T = p.T; S = p.S; K = p.K;
            reach = ComputeReach();
            islands = BuildIslands();
        }

        /// <summary>影響半径: c1 の窓長・c3 系パターン長の最大 −1（最低 1、最大 T−1）。</summary>
        private int ComputeReach()
        {
            var r = 1;
            foreach (var c in p.Cons1) r = Math.Max(r, c.Day1 - 1);
            foreach (var list in new[] { p.Cons3, p.Cons3n, p.Cons3m, p.Cons3mn }) foreach (var c in list) r = Math.Max(r, c.Seq.Length - 1);
            return Math.Min(r, Math.Max(1, T - 1));
        }

        private bool Locked(int i, int d) => p.WishLocked(i, d);
        private bool SameGroup(int a, int b) => p.Sgrp[a] == p.Sgrp[b] && p.Ssk[a] == p.Ssk[b];
        private string Name(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        private bool BudgetLeft() => !stop() && evaluated < prm.MaxEvaluations;
        private static readonly bool[] GroupOrder = { true, false };

        // ---- 希望島 ----
        private List<WishIsland> BuildIslands()
        {
            var o = new List<WishIsland>();
            for (var i = 0; i < S; i++)
            {
                var days = Enumerable.Range(0, T).Where(d => Locked(i, d)).ToList();
                if (days.Count > 0) MergeIntoIslands(i, days, o);
            }
            return o;
        }

        /// <summary>希望日をソート順に見て、影響範囲（±reach）が重なる限り同じ島へ入れる。</summary>
        private void MergeIntoIslands(int i, List<int> days, List<WishIsland> o)
        {
            int from = days[0], to = days[0]; var cur = new List<int> { days[0] };
            void Flush() { o.Add(new WishIsland(i, cur.ToArray(), Math.Max(0, from - reach), Math.Min(T - 1, to + reach))); cur.Clear(); }
            for (var t = 1; t < days.Count; t++)
            {
                var d = days[t];
                if (d - reach > to + reach) { Flush(); from = d; }
                to = d; cur.Add(d);
            }
            Flush();
        }

        /// <summary>島の周辺の違反重み。セル違反＝影響範囲内、回数違反＝当該職員の全部。重み 0 の族も 1 と数える。</summary>
        private long LocalScore(ViolationReport rep, WishIsland isl)
        {
            long s = 0;
            foreach (var key in isl.ZoneKeys)
                if (rep.CellFamilies.TryGetValue(key, out var fams)) foreach (var f in fams) s += WeightOfClass(f);
            foreach (var (key, cls) in rep.CountViolations) if (key.StartsWith(isl.CountPrefix, StringComparison.Ordinal)) s += WeightOfClass(cls);
            return s;
        }

        private static long WeightOfClass(string cls) => Math.Max((long)MirrorKeys.WeightOf(cls.StartsWith("vio-", StringComparison.Ordinal) ? cls.Substring(4) : cls), 1L);

        // ---- 手の適用・巻き戻し・事前枝刈り ----
        private int[] Apply(WishMove m)
        {
            var old = new int[m.Cells.Length / 3];
            for (var t = 0; t < m.Cells.Length; t += 3) { var i = m.Cells[t]; var d = m.Cells[t + 1]; old[t / 3] = work[i][d]; work[i][d] = m.Cells[t + 2]; }
            return old;
        }

        private void Undo(WishMove m, int[] old) { for (var t = 0; t < m.Cells.Length; t += 3) work[m.Cells[t]][m.Cells[t + 1]] = old[t / 3]; }

        /// <summary>
        /// 変更する職員の禁止の並び（cons3n）の件数が増えるなら正式評価の前に落とす（チェッカーが最終判定＝見逃しは無害）。
        /// [Android 3.501.0] 旧: 変更セルに禁止の並びが 1 つでも残れば落としていた＝「2件→1件」に減らす手まで正式評価へ届かなかった。
        /// <c>AdaptiveBlockSwap</c> の増分判定と同じ（<see cref="C1DeltaPrefilter.StaffC3nFires"/>＝チェッカーと同一意味論）。
        /// </summary>
        private bool IncreasesForbidden(WishMove m)
        {
            if (p.Cons3n.Count == 0) return false;
            int before = 0, after = 0;
            for (var t = 0; t < m.Cells.Length; t += 3) if (FirstCellOfStaff(m, t)) before += C1DeltaPrefilter.StaffC3nFires(p, work[m.Cells[t]]);
            var old = Apply(m);
            try
            {
                for (var t = 0; t < m.Cells.Length; t += 3) if (FirstCellOfStaff(m, t)) after += C1DeltaPrefilter.StaffC3nFires(p, work[m.Cells[t]]);
                return after > before;
            }
            finally { Undo(m, old); }
        }

        private static bool FirstCellOfStaff(WishMove m, int t)
        {
            for (var u = 0; u < t; u += 3) if (m.Cells[u] == m.Cells[t]) return false;
            return true;
        }

        // ---- 候補生成（遅延・評価順＝手の種類 → 同じ所属 → 小さい手） ----
        private bool Swappable(int a, int b, int d)
        {
            int ka = work[a][d], kb = work[b][d];
            if (ka < 0 || ka >= K || kb < 0 || kb >= K) return false;
            if (Locked(a, d) || Locked(b, d)) return false;
            return p.CanDo(a, kb) && p.CanDo(b, ka);
        }

        /// <summary>窓 s0..s1 を a と b で丸ごと交換できて、かつ何かが変わるとき真。</summary>
        private bool WindowOk(int a, int b, int s0, int s1)
        {
            var changes = false;
            for (var d = s0; d <= s1; d++)
            {
                if (!Swappable(a, b, d)) return false;
                if (work[a][d] != work[b][d]) changes = true;
            }
            return changes;
        }

        private void WindowCells(int a, int b, int s0, int s1, List<int> into)
        {
            for (var d = s0; d <= s1; d++) { into.Add(a); into.Add(d); into.Add(work[b][d]); into.Add(b); into.Add(d); into.Add(work[a][d]); }
        }

        private IEnumerable<int> Partners(int a, bool sg) => Enumerable.Range(0, S).Where(b => b != a && SameGroup(a, b) == sg);

        private IEnumerable<WishMove> SameDayMoves(WishIsland isl, bool sg)
        {
            var a = isl.Staff;
            for (var d = isl.ZoneFrom; d <= isl.ZoneTo; d++)
            {
                if (Locked(a, d) || work[a][d] < 0 || work[a][d] >= K) continue;
                foreach (var b in Partners(a, sg))
                {
                    if (work[b][d] == work[a][d] || !Swappable(a, b, d)) continue;
                    yield return new WishMove(WishMoveKind.SameDay, new[] { a, d, work[b][d], b, d, work[a][d] }, sg);
                }
            }
        }

        private IEnumerable<WishMove> WindowMoves(WishIsland isl, bool sg, int len)
        {
            var a = isl.Staff;
            foreach (var b in Partners(a, sg))
                for (var s0 = isl.ZoneFrom; s0 <= isl.ZoneTo - len + 1; s0++)
                {
                    var s1 = s0 + len - 1;
                    if (!WindowOk(a, b, s0, s1)) continue;
                    var cells = new List<int>(len * 6); WindowCells(a, b, s0, s1, cells);
                    yield return new WishMove(WishMoveKind.Window, cells.ToArray(), sg);
                }
        }

        private IEnumerable<WishMove> WindowMoves(WishIsland isl)
        {
            var zl = isl.ZoneTo - isl.ZoneFrom + 1;
            foreach (var sg in GroupOrder) for (var len = 2; len <= zl; len++) foreach (var m in WindowMoves(isl, sg, len)) yield return m;
        }

        /// <summary>両翼＝島の前の窓 l0..first-1 と後の窓 last+1..r1 を同じ相手と同時に交換。合計長 total のものだけ生成。</summary>
        private IEnumerable<WishMove> WingMoves(WishIsland isl, bool sg, int total)
        {
            var a = isl.Staff; var first = isl.WishDays[0]; var last = isl.WishDays[^1];
            foreach (var b in Partners(a, sg))
                for (var l0 = isl.ZoneFrom; l0 < first; l0++)
                {
                    var r1 = last + (total - (first - l0));
                    if (r1 < last + 1 || r1 > isl.ZoneTo) continue;
                    if (!WindowOk(a, b, l0, first - 1) || !WindowOk(a, b, last + 1, r1)) continue;
                    var cells = new List<int>(total * 6); WindowCells(a, b, l0, first - 1, cells); WindowCells(a, b, last + 1, r1, cells);
                    yield return new WishMove(WishMoveKind.Wings, cells.ToArray(), sg);
                }
        }

        private IEnumerable<WishMove> WingMoves(WishIsland isl)
        {
            var first = isl.WishDays[0]; var last = isl.WishDays[^1];
            if (first <= isl.ZoneFrom || last >= isl.ZoneTo) yield break;   // 月初・月末で片翼が無い＝両翼交換なし
            var maxTotal = (first - isl.ZoneFrom) + (isl.ZoneTo - last);
            foreach (var sg in GroupOrder) for (var total = 2; total <= maxTotal; total++) foreach (var m in WingMoves(isl, sg, total)) yield return m;
        }

        private IEnumerable<WishMove> Rotate3At(WishIsland isl, int d, bool sg)
        {
            var a = isl.Staff; var ka = work[a][d];
            for (var b = 0; b < S; b++)
            {
                if (b == a || Locked(b, d)) continue;
                var kb = work[b][d]; if (kb < 0 || kb >= K || !p.CanDo(a, kb)) continue;
                for (var c = 0; c < S; c++)
                {
                    if (c == a || c == b || Locked(c, d)) continue;
                    var kc = work[c][d]; if (kc < 0 || kc >= K || !p.CanDo(b, kc) || !p.CanDo(c, ka)) continue;
                    if (ka == kb && kb == kc) continue;
                    if ((SameGroup(a, b) && SameGroup(b, c)) != sg) continue;
                    yield return new WishMove(WishMoveKind.Rotate3, new[] { a, d, kb, b, d, kc, c, d, ka }, sg);
                }
            }
        }

        private IEnumerable<WishMove> Rotate3Moves(WishIsland isl)
        {
            foreach (var sg in GroupOrder)
                for (var d = isl.ZoneFrom; d <= isl.ZoneTo; d++)
                {
                    if (Locked(isl.Staff, d) || work[isl.Staff][d] < 0 || work[isl.Staff][d] >= K) continue;
                    foreach (var m in Rotate3At(isl, d, sg)) yield return m;
                }
        }

        private IEnumerable<WishMove> SameDayMoves(WishIsland isl) { foreach (var sg in GroupOrder) foreach (var m in SameDayMoves(isl, sg)) yield return m; }

        /// <summary>通常 pass の候補: 同日・窓・両翼を 1 手ずつ交互に（[Android 3.501.0] 旧: 連結順で同日候補が島の枠を使い切り窓・両翼が評価されなかった）。</summary>
        private IEnumerable<WishMove> IslandMoves(WishIsland isl) =>
            V6SearchOperators.RoundRobin(SameDayMoves(isl), WindowMoves(isl), WingMoves(isl));

        /// <summary>
        /// ビームの候補: 島ごとに（同日・窓・両翼）を 1 手ずつ交互に並べ、さらに島どうしも交互に巡回する
        /// （連結順だと先頭の島と同日候補が走査枠を独占し両翼が出ない。計測は Android docs/history 3.504.0）。
        /// </summary>
        private IEnumerable<WishMove> BeamMoves(List<WishIsland> active)
        {
            foreach (var move in V6SearchOperators.RoundRobin(active.Select(IslandMoves).ToArray())) yield return move;
        }

        public IEnumerable<(string Kind, int[] Cells)> MovesForTest()
        {
            foreach (var isl in islands) foreach (var m in IslandMoves(isl)) yield return (WishMoveLabel(m.Kind), m.Cells);
        }

        // ---- 評価 ----
        /// <summary>moves を順に正式評価し、全体も希望周辺も改善する手のうち最良のものを返す（島の枠 budget 手まで）。</summary>
        private WishChosen? PickBest(WishIsland isl, IEnumerable<WishMove> moves, int budget, long localBefore)
        {
            WishChosen? chosen = null;
            var baseWork = work.Copy2D();
            foreach (var m in moves)
            {
                if (!BudgetLeft() || islandUsed >= budget) break;
                if (IncreasesForbidden(m)) { prunedC3n++; continue; }
                islandUsed++; evaluated++;
                var old = Apply(m);
                ViolationReport rep;
                bool pinBad;
                bool accept;
                try
                {
                    rep = UnifiedViolationChecker.Check(state, work);
                    var improves = UnifiedViolationChecker.BetterReport(rep, bestRep);
                    pinBad = improves && V6SearchOperators.ExactPinRegression(p, baseWork, work);
                    if (pinBad) pinBlocks.Record(p, baseWork, work);
                    accept = improves && !pinBad && LocalScore(rep, isl) < localBefore;
                }
                finally { Undo(m, old); }   // 評価器・ピン検査のどこで例外になっても試行手を盤面に残さない。
                if (!accept) { rejectCulprits.Record(rep, bestRep, pinBad); continue; }
                if (chosen is null || UnifiedViolationChecker.BetterReport(rep, chosen.Rep)) chosen = new WishChosen(m, rep);
            }
            return chosen;
        }

        /// <summary>1 pass: 起動中の島を順に見て、島ごとに最良の 1 手を採用する。戻り値は採用数。</summary>
        private int RunPass(List<WishIsland> active)
        {
            var passApplied = 0;
            var islandBudget = Math.Max(prm.MinIslandBudget, (prm.MaxEvaluations - evaluated) / active.Count);
            foreach (var isl in active)
            {
                if (!BudgetLeft()) break;
                // 前の島の採用で周辺の違反が消えた島は、どの手も「希望周辺の改善」を満たせないので評価しない（枠の無駄）。
                var localBefore = LocalScore(bestRep, isl);
                if (localBefore == 0) continue;
                islandUsed = 0;
                // [Android 3.501.0] 島の枠の 25% を 3 職員巡回に確保する（通常候補は 75% まで）。巡回は採用 0 のときだけ（残り枠を全部使える）。
                var mainBudget = islandBudget - islandBudget / 4;
                var chosen = PickBest(isl, IslandMoves(isl), mainBudget, localBefore)
                             ?? PickBest(isl, Rotate3Moves(isl), islandBudget, localBefore);
                if (chosen is null) { if (!stuck.Contains(Name(isl.Staff))) stuck.Add(Name(isl.Staff)); continue; }
                Apply(chosen.Move); bestRep = chosen.Rep; applied++; passApplied++;
                var lb = WishMoveLabel(chosen.Move.Kind); byKind[lb] = byKind.GetValueOrDefault(lb) + 1;
            }
            return passApplied;
        }

        /// <summary>停滞時の短いビーム。途中は中立手（悪化しない手）を許し、最終盤面が全体で改善したときだけ採用する。</summary>
        private bool RunBeam()
        {
            beamRuns++;
            var baseline = work.Copy2D();
            var frontier = new List<WishNode> { new(work.Copy2D(), bestRep) };
            WishNode? bestNode = null;
            for (var depth = 0; depth < prm.BeamDepth; depth++)
            {
                if (!BudgetLeft()) break;
                var next = new List<WishNode>();
                // [Android 3.504.0] 段の保持数は残り予算で頭打ちにし、走査枠は frontier の各ノードへ均等に配る。
                var remaining = Math.Max(prm.MaxEvaluations - evaluated, 0);
                var depthLimit = WishBeamCandidateLimit(prm.BeamWidth, prm.BeamBranchFactor, remaining);
                var perNodeLimit = Math.Max(1, depthLimit / frontier.Count);
                var seenBoards = new HashSet<int[][]>(ScheduleEqualityComparer.Instance);   // 別の交換列から同じ盤面へ着いた候補で幅と予算を重複消費しない
                foreach (var node in frontier)
                {
                    if (!BudgetLeft()) break;
                    ExpandNode(node, next, perNodeLimit, depthLimit, seenBoards);
                }
                if (next.Count == 0) break;
                next.Sort((x, y) => UnifiedViolationChecker.ReportComparer.Compare(x.Rep, y.Rep));
                frontier = next.Take(prm.BeamWidth).ToList();
                var top = frontier[0];
                if (UnifiedViolationChecker.BetterReport(top.Rep, bestRep) && (bestNode is null || UnifiedViolationChecker.BetterReport(top.Rep, bestNode.Rep))) bestNode = top;
            }
            Restore(baseline);
            if (bestNode is null || V6SearchOperators.ExactPinRegression(p, baseline, bestNode.Board)) return false;
            Restore(bestNode.Board); bestRep = bestNode.Rep; applied++; beamApplied++;
            return true;
        }

        /// <summary>ビーム 1 段で走査する中立手の上限＝保持数の何倍か（Android 3.502.0）。評価予算はこれとは別に MaxEvaluations で頭打ち。</summary>
        private const int BeamScanFactor = 2;

        /// <summary>
        /// 1 ノードの展開: nodeLimit×BeamScanFactor 手まで正式評価し（枝刈りした手は数えない）、段全体で共有する next に
        /// 良い順で depthLimit 件だけ保持する（Android 3.502.0: 列挙順の先頭で打ち切らない／3.504.0: 走査枠はノードごと、保持数は段ごと）。
        /// </summary>
        private void ExpandNode(WishNode node, List<WishNode> next, int nodeLimit, int depthLimit, HashSet<int[][]> seenBoards)
        {
            Restore(node.Board);
            var active = islands.Where(isl => LocalScore(node.Rep, isl) > 0).ToList();
            var scanLimit = (int)Math.Min((long)nodeLimit * BeamScanFactor, int.MaxValue);
            var scanned = 0;
            foreach (var m in BeamMoves(active))
            {
                if (!BudgetLeft() || scanned >= scanLimit) break;
                if (IncreasesForbidden(m)) { prunedC3n++; continue; }
                var old = Apply(m);
                try
                {
                    var rep = UnifiedViolationChecker.Check(state, work);
                    evaluated++; beamEvaluated++; scanned++;
                    var neutral = !UnifiedViolationChecker.BetterReport(node.Rep, rep) && !V6SearchOperators.ExactPinRegression(p, node.Board, work);
                    if (neutral)
                    {
                        var board = work.Copy2D();
                        if (seenBoards.Add(board)) KeepBest(next, new WishNode(board, rep), depthLimit);
                    }
                }
                finally { Undo(m, old); }
            }
        }

        /// <summary>next を良い順に保ったまま node を挿入し、limit 件を超えた末尾（最も悪い手）を落とす。</summary>
        private static void KeepBest(List<WishNode> next, WishNode node, int limit)
        {
            var pos = next.Count;
            while (pos > 0 && UnifiedViolationChecker.ReportComparer.Compare(node.Rep, next[pos - 1].Rep) < 0) pos--;
            if (pos >= limit) return;
            next.Insert(pos, node);
            if (next.Count > limit) next.RemoveAt(next.Count - 1);
        }

        private void Restore(int[][] board) { for (var s = 0; s < S; s++) Array.Copy(board[s], work[s], T); }

        public CyclicSwapResult Run()
        {
            var pass = 0;
            while (pass < prm.MaxPasses && BudgetLeft())
            {
                var active = islands.Where(isl => LocalScore(bestRep, isl) > 0).ToList();
                activeCount = active.Count;
                if (active.Count == 0) break;
                if (RunPass(active) == 0 && !RunBeam()) break;   // 通常もビームも改善なし＝停滞で終了
                pass++;
            }
            var improved = UnifiedViolationChecker.BetterReport(bestRep, before);
            var finalSched = improved ? work : ScheduleUtil.NormalizeSchedule(input, p);
            var finalRep = improved ? bestRep : before;
            var logs = new[] { new MirrorLog(tag: "WishIslandPolish", message: Summary(finalRep)) };
            return new CyclicSwapResult(finalSched, before.Total, finalRep.Total, applied, logs, ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
        }

        private string Summary(ViolationReport finalRep)
        {
            var wishCount = islands.Sum(x => x.WishDays.Length);
            var sb = new System.Text.StringBuilder();
            sb.Append($"希望島研磨: 希望{wishCount}件→島{islands.Count}件(起動{activeCount}件・影響半径{reach}日) 正式評価{evaluated}");
            if (beamEvaluated > 0) sb.Append($"(うちビーム{beamEvaluated})");
            sb.Append($" / total {before.Total}->{finalRep.Total} HARD {before.Hard}->{finalRep.Hard} 採用{applied}回");
            if (byKind.Count > 0) sb.Append('(').Append(string.Join(" ", byKind.Select(kv => $"{kv.Key}:{kv.Value}"))).Append(')');
            if (beamRuns > 0) sb.Append($" ビーム{beamRuns}回(採用{beamApplied})");
            if (prunedC3n > 0) sb.Append($" 禁止の並びで枝刈り{prunedC3n}");
            if (applied == 0 && activeCount > 0) sb.Append(" [頭打ち=改善手なし]");
            sb.Append(rejectCulprits.Summary());
            if (stuck.Count > 0)
            {
                sb.Append(" 残存: ").Append(string.Join(", ", stuck.Take(prm.StuckNamesShown)));
                if (stuck.Count > prm.StuckNamesShown) sb.Append($" ほか{stuck.Count - prm.StuckNamesShown}名");
            }
            return sb.ToString();
        }
    }
}
