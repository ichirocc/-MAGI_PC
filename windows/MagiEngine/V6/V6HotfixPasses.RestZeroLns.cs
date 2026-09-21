using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>Kotlin <c>RestZeroWindowLns.Result</c> の移植。</summary>
    public sealed record RestZeroLnsResult(int[][] NewSchedule, int Applied, IReadOnlyList<MirrorLog> Logs);

    /// <summary>Kotlin <c>RestZeroWindowLns.Config</c> の移植。</summary>
    public sealed record RestZeroLnsConfig(
        int Before = 4, int After = 2, int MaxWindow = 8, int BeamWidth = 32,
        int MaxChildren = 160, int MaxLeafChecks = 6, int MaxEvaluations = 60_000,
        int MaxNightSequences = 24, int MaxTransfers = 12, long Seed = 0x5E57L);

    private sealed class RestZeroNode
    {
        public int[][] Board; public int[][] Remain; public long Score;
        public RestZeroNode(int[][] board, int[][] remain, long score) { Board = board; Remain = remain; Score = score; }
    }

    private sealed record RestZeroTransfer(int A, int B, int X, IReadOnlyList<int> Outside);

    /// <summary>
    /// [Kotlin原本 <c>RestZeroWindowLns.apply</c>] 休の必要人数を明示的に定めた日に休が余るとき、その前後の窓を各職員の
    /// 「窓内シフトの並べ替え」で組み直す。外側で夜勤型（翌日が自分か休に限られるシフト）の担当列を列挙し、窓の外の同日交換で
    /// 夜勤を人から人へ移す候補（1〜3 日）も試し、内側で残りをビーム探索、完成盤面を正式チェッカーで keep-best 判定する。
    /// 子の並びの乱択は Kotlin と乱数列が異なるため、盤面はビットまで同一にはならない（採用基準は同じ）。
    /// </summary>
    public static RestZeroLnsResult ApplyRestZeroWindowLns(
        MagiState state, int[][] schedule, RestZeroLnsConfig? config = null, Func<bool>? shouldStop = null)
    {
        var cfg = config ?? new RestZeroLnsConfig();
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        // [backlog#24] 休シフト未設定ならno-op（このパス自体が「休が余る窓」を扱うので前提が成立しない）。
        if (p.RestIdx is not int rest)
            return new RestZeroLnsResult(ScheduleUtil.NormalizeSchedule(schedule, p), 0,
                new[] { Log("休0日の窓LNS: 休シフトなし=スキップ") });
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        // 途中盤面の推定は日内で決まる族だけ（Android 同式）: 未割当の日が旧値のため、境界をまたぐ禁止連続や回数系は子ごとに歪む。
        // C# の Evaluator は族別内訳を返さないので正式チェッカーの Breakdown（同じ生カウント）を使う＝結果は同一、速度は劣る。
        var estFams = new[] { "c1", "c3", "c3m", "c3mn", "c41", "c42", "c41s", "c42s", "covO", "covU" };
        long Estimate(int[][] board)
        {
            var bd = UnifiedViolationChecker.Check(state, board).Breakdown;
            long sum = 0;
            foreach (var f in estFams) sum += (long)(bd.GetValueOrDefault(f) * MirrorKeys.WeightOf(f));
            return sum;
        }
        var evaluations = 0; var applied = 0;
        var notes = new List<string>();
        MirrorLog Log(string m) => new MirrorLog("RestZeroLNS", m);
        if (rest < 0 || rest >= p.K) return new RestZeroLnsResult(work, 0, new[] { Log("休0日の窓LNS: 休シフトなし=スキップ") });

        int RestCount(int[][] board, int j) { var c = 0; for (var i = 0; i < p.S; i++) if (board[i][j] == rest) c++; return c; }
        bool ExplicitRestNeed(int j) => p.Need1[rest][j] >= 0 || (p.Use2 && p.Need2[rest][j] >= 0);
        var targets = new List<int>();
        for (var j = 0; j < p.T; j++)
        {
            if (!ExplicitRestNeed(j) || p.CovOCell(rest, j, RestCount(work, j)) <= 0) continue;
            var any = false;
            for (var i = 0; i < p.S; i++) if (work[i][j] == rest && !p.WishLocked(i, j)) { any = true; break; }
            if (any) targets.Add(j);
        }
        if (targets.Count == 0) return new RestZeroLnsResult(work, 0, new[] { Log("休0日の窓LNS: 対象日なし（休の必要人数が明示された日に非希望の休の過剰なし）") });

        // 「翌日が自分か休に限られる」シフト＝夜勤型（2 長の禁止連続 [k, m] から求める）。
        var nightLike = Enumerable.Range(0, p.K).Where(k => k != rest && Enumerable.Range(0, p.K).All(m =>
            m == k || m == rest || p.Cons3n.Any(c => c.Seq.Length == 2 && c.Seq[0] == k && c.Seq[1] == m))).ToList();

        bool BackwardForbidden(int[][] board, int i, int j, int k)
        {
            if (p.C3wBanned(i, j, k)) return true;
            foreach (var c in p.Cons3n)
            {
                var seq = c.Seq; var d = seq.Length;
                if (d == 0 || j - d + 1 < 0) continue;
                var z = 0;
                for (var l = 0; l < d; l++) { var v = l == d - 1 ? k : board[i][j - d + 1 + l]; if (v == seq[l]) z++; }
                if (z == d) return true;
            }
            return false;
        }

        var windows = new List<(int lo, int hi)>();
        foreach (var d in targets)
        {
            var lo = Math.Max(d - cfg.Before, 0); var hi = Math.Min(d + cfg.After, p.T - 1);
            if (windows.Count > 0 && lo <= windows[^1].hi + 1) windows[^1] = (windows[^1].lo, Math.Max(windows[^1].hi, hi));
            else windows.Add((lo, hi));
        }
        var trimmed = windows.Select(w => w.hi - w.lo + 1 > cfg.MaxWindow ? (w.hi - cfg.MaxWindow + 1, w.hi) : w).ToList();

        foreach (var w in trimmed)
        {
            if (stop()) break;
            var wLo = w.Item1; var wHi = w.Item2;
            var days = Enumerable.Range(wLo, wHi - wLo + 1).ToList();
            var rng = new Random((int)((cfg.Seed ^ (wLo * 131L + wHi)) & 0x7fffffff));
            var free0 = new List<int>[p.S];
            for (var i = 0; i < p.S; i++) free0[i] = days.Where(j => !p.WishLocked(i, j) && work[i][j] >= 0 && work[i][j] < p.K).ToList();
            var remainAll = new int[p.S][];
            for (var i = 0; i < p.S; i++) { remainAll[i] = new int[p.K]; foreach (var j in free0[i]) remainAll[i][work[i][j]]++; }
            var windowTargets = targets.Where(t => t >= wLo && t <= wHi).ToList();
            int Dist(int g) => Math.Min(Math.Abs(g - wLo), Math.Abs(g - wHi));

            List<int[]> NightSequences(int k, int[][] remainBase, int[][] boardBase)
            {
                var outSeqs = new List<(int[] seq, int forced)>();
                // 窓内の自由セルは未割当（-1）から始める＝同じ人の古い夜勤セルが「4 連」などの後ろ向き判定を誤らせないため。
                var seqBoard = boardBase.Copy2D();
                for (var i = 0; i < p.S; i++) foreach (var j in free0[i]) seqBoard[i][j] = -1;
                var remain = remainBase.Select(r => (int[])r.Clone()).ToArray();
                var holder = new int[days.Count]; Array.Fill(holder, -1);
                int NeedOn(int j) { var n = 0; while (n < p.S && p.CovUCell(k, j, n) > 0) n++; return n; }
                void Rec(int idx)
                {
                    if (outSeqs.Count >= 400) return;
                    if (idx == days.Count)
                    {
                        var forced = 0;
                        foreach (var t in windowTargets)
                        {
                            var ti = days.IndexOf(t); if (ti <= 0) continue;
                            var h = holder[ti - 1];
                            if (h >= 0 && holder[ti] != h) forced++;
                        }
                        outSeqs.Add(((int[])holder.Clone(), forced)); return;
                    }
                    var j = days[idx];
                    var fixedHolder = -1;
                    for (var i = 0; i < p.S; i++) if (!free0[i].Contains(j) && boardBase[i][j] == k) { fixedHolder = i; break; }
                    if (fixedHolder >= 0) { holder[idx] = fixedHolder; Rec(idx + 1); holder[idx] = -1; return; }
                    if (NeedOn(j) <= 0) { holder[idx] = -1; Rec(idx + 1); return; }
                    var prev = idx > 0 ? holder[idx - 1] : -1;
                    for (var i = 0; i < p.S; i++)
                    {
                        if (!free0[i].Contains(j) || remain[i][k] <= 0 || BackwardForbidden(seqBoard, i, j, k)) continue;
                        // 前日の担当者のブロックがここで終わるなら、その人はこの日に休が要る（持ち分に休が無ければ不成立）。
                        var restFor = prev >= 0 && prev != i && free0[prev].Contains(j) ? prev : -1;
                        if (restFor >= 0 && (remain[restFor][rest] <= 0 || BackwardForbidden(seqBoard, restFor, j, rest))) continue;
                        var old = seqBoard[i][j];
                        seqBoard[i][j] = k; remain[i][k]--; holder[idx] = i;
                        if (restFor >= 0) { seqBoard[restFor][j] = rest; remain[restFor][rest]--; }
                        Rec(idx + 1);
                        if (restFor >= 0) { seqBoard[restFor][j] = -1; remain[restFor][rest]++; }
                        seqBoard[i][j] = old; remain[i][k]++; holder[idx] = -1;
                    }
                }
                Rec(0);
                return outSeqs.OrderBy(e => e.forced)
                    .ThenBy(e => Enumerable.Range(0, days.Count).Count(t => e.seq[t] >= 0 && boardBase[e.seq[t]][days[t]] != k))
                    .Select(e => e.seq).ToList();
            }

            List<RestZeroNode> RunBeam(Dictionary<(int, int), int> fixedCells, int[][] remainBase, int[][] boardBase)
            {
                var start = boardBase.Copy2D();
                foreach (var (cell, k) in fixedCells) start[cell.Item1][cell.Item2] = k;
                var free = new List<int>[p.S];
                for (var i = 0; i < p.S; i++) free[i] = free0[i].Where(j => !fixedCells.ContainsKey((i, j))).ToList();
                var remain0 = remainBase.Select(r => (int[])r.Clone()).ToArray();
                foreach (var (cell, k) in fixedCells) remain0[cell.Item1][k]--;
                var beam = new List<RestZeroNode> { new(start, remain0, Estimate(start)) };
                foreach (var j in days)
                {
                    if (stop() || evaluations >= cfg.MaxEvaluations) break;
                    var persons = Enumerable.Range(0, p.S).Where(i => free[i].Contains(j)).ToList();
                    var need = new int[p.K];
                    for (var k = 0; k < p.K; k++)
                    {
                        var lo = 0;
                        while (lo < p.S && p.CovUCell(k, j, lo) > 0) lo++;
                        var fixedCnt = 0;
                        for (var i = 0; i < p.S; i++) if (!persons.Contains(i) && start[i][j] == k) fixedCnt++;
                        need[k] = Math.Max(lo - fixedCnt, 0);
                    }
                    var next = new List<RestZeroNode>();
                    foreach (var node in beam)
                    {
                        if (stop() || evaluations >= cfg.MaxEvaluations) break;
                        var board = node.Board;
                        var assign = new int[p.S]; Array.Fill(assign, -1);
                        var cnt = new int[p.K];
                        var children = 0;
                        bool FeasibleTail(int idx)
                        {
                            for (var k = 0; k < p.K; k++)
                            {
                                var shortfall = need[k] - cnt[k];
                                if (shortfall <= 0) continue;
                                var can = 0;
                                for (var q = idx; q < persons.Count; q++) if (node.Remain[persons[q]][k] > 0) can++;
                                if (can < shortfall) return false;
                            }
                            return true;
                        }
                        void Rec(int idx)
                        {
                            if (children >= cfg.MaxChildren || evaluations >= cfg.MaxEvaluations) return;
                            if (idx == persons.Count)
                            {
                                for (var k = 0; k < p.K; k++) if (cnt[k] < need[k]) return;
                                var nb = board.Copy2D();
                                var nr = node.Remain.Select(r => (int[])r.Clone()).ToArray();
                                foreach (var i in persons) { nb[i][j] = assign[i]; nr[i][assign[i]]--; }
                                evaluations++; children++;
                                next.Add(new RestZeroNode(nb, nr, Estimate(nb)));
                                return;
                            }
                            if (!FeasibleTail(idx)) return;
                            var pi = persons[idx];
                            // 子の上限で打ち切るので、選択肢の順は乱択（決定的 seed）＝先頭の人の第一候補ばかりを深掘りしない。
                            var options = Enumerable.Range(0, p.K).Where(k => node.Remain[pi][k] > 0).OrderBy(_ => rng.Next()).ToList();
                            foreach (var k in options)
                            {
                                if (BackwardForbidden(board, pi, j, k)) continue;
                                assign[pi] = k; cnt[k]++;
                                Rec(idx + 1);
                                cnt[k]--; assign[pi] = -1;
                                if (children >= cfg.MaxChildren) return;
                            }
                        }
                        Rec(0);
                    }
                    if (next.Count == 0) return new List<RestZeroNode>();
                    var seen = new HashSet<string>();
                    beam = next.OrderBy(n => n.Score)
                        .Where(n => seen.Add(string.Join(",", days.Select(d => string.Concat(Enumerable.Range(0, p.S).Select(i => n.Board[i][d] + "."))))))
                        .Take(cfg.BeamWidth).ToList();
                }
                return beam;
            }

            // 夜勤の人間移動: 窓の外の同じ日 g で A: x→k, B: k→x を n 日ぶん入れ替える（回数・被覆は不変）。
            var transfers = new List<RestZeroTransfer?> { null };
            if (nightLike.Count > 0)
            {
                var k = nightLike[0];
                var cands = new List<RestZeroTransfer>();
                for (var a = 0; a < p.S; a++)
                {
                    if (remainAll[a][k] <= 0) continue;
                    for (var b = 0; b < p.S; b++)
                    {
                        if (b == a || !p.CanDo(b, k) || free0[b].Count == 0) continue;
                        for (var x = 0; x < p.K; x++)
                        {
                            if (x == k || remainAll[b][x] <= 0 || !p.CanDo(a, x)) continue;
                            var gs = Enumerable.Range(0, p.T).Where(g => (g < wLo || g > wHi) && work[a][g] == x && work[b][g] == k && !p.WishLocked(a, g) && !p.WishLocked(b, g))
                                .OrderBy(Dist).ToList();
                            var maxN = Math.Min(Math.Min(3, remainAll[a][k]), Math.Min(remainAll[b][x], gs.Count));
                            for (var n = 1; n <= maxN; n++) cands.Add(new RestZeroTransfer(a, b, x, gs.Take(n).ToList()));
                        }
                    }
                }
                var perSize = Math.Max(cfg.MaxTransfers / 3, 1);
                for (var n = 1; n <= 3; n++)
                    transfers.AddRange(cands.Where(t => t.Outside.Count == n).OrderBy(t => t.Outside.Sum(Dist)).Take(perSize));
            }
            var adoptedHere = false; var checkedN = 0; var tried = 0; var transfersTried = 0;
            var bestEst = long.MaxValue;
            var baseEst = Estimate(work);
            ViolationReport? bestVerified = null; var bestVerifiedRests = "";
            foreach (var tr in transfers)
            {
                if (stop() || evaluations >= cfg.MaxEvaluations || adoptedHere) break;
                var boardBase = work.Copy2D();
                var remainBase = remainAll.Select(r => (int[])r.Clone()).ToArray();
                if (tr != null)
                {
                    var k = nightLike[0];
                    foreach (var g in tr.Outside) { boardBase[tr.A][g] = k; boardBase[tr.B][g] = tr.X; }
                    var n = tr.Outside.Count;
                    remainBase[tr.A][k] -= n; remainBase[tr.A][tr.X] += n;
                    remainBase[tr.B][k] += n; remainBase[tr.B][tr.X] -= n;
                    transfersTried++;
                }
                List<Dictionary<(int, int), int>> fixedSets;
                if (nightLike.Count == 0) fixedSets = new List<Dictionary<(int, int), int>> { new() };
                else
                {
                    var k = nightLike[0];
                    fixedSets = NightSequences(k, remainBase, boardBase).Take(cfg.MaxNightSequences).Select(seq =>
                    {
                        var m = new Dictionary<(int, int), int>();
                        for (var idx = 0; idx < days.Count; idx++) { var h = seq[idx]; if (h >= 0 && free0[h].Contains(days[idx])) m[(h, days[idx])] = k; }
                        return m;
                    }).ToList();
                }
                foreach (var fixedCells in fixedSets)
                {
                    if (stop() || evaluations >= cfg.MaxEvaluations) break;
                    tried++;
                    var checkedHere = 0;
                    foreach (var leaf in RunBeam(fixedCells, remainBase, boardBase).OrderBy(n => n.Score))
                    {
                        if (checkedHere >= cfg.MaxLeafChecks) break;
                        if (leaf.Score < bestEst) bestEst = leaf.Score;
                        if (leaf.Board.ContentDeepEquals(work)) continue;
                        checkedN++; checkedHere++;
                        var rep = UnifiedViolationChecker.Check(state, leaf.Board);
                        if (bestVerified == null || UnifiedViolationChecker.BetterReport(rep, bestVerified))
                        {
                            bestVerified = rep;
                            bestVerifiedRests = string.Join("/", windowTargets.Select(t => $"{t + 1}:{RestCount(leaf.Board, t)}"));
                        }
                        if (UnifiedViolationChecker.BetterReport(rep, bestRep))
                        {
                            for (var i = 0; i < p.S; i++) for (var d = 0; d < p.T; d++) work[i][d] = leaf.Board[i][d];
                            bestRep = rep; applied++; adoptedHere = true;
                            break;
                        }
                    }
                    if (adoptedHere) break;
                }
            }
            var restNow = string.Join("/", windowTargets.Select(t => $"{t + 1}:{RestCount(work, t)}"));
            if (adoptedHere) notes.Add($"窓{wLo + 1}-{wHi + 1}: 採用（休@対象日 {restNow}・夜勤列{tried}本・人間移動{transfersTried}組・正式{checkedN}件）");
            else
            {
                var est = bestEst == long.MaxValue ? "-" : (bestEst - baseEst).ToString();
                var verified = bestVerified == null ? "" : $"・正式最良 必須{bestVerified.Hard - bestRep.Hard:+#;-#;+0} 重み{(int)(bestVerified.WeightedScore - bestRep.WeightedScore):+#;-#;+0}（休@対象日 {bestVerifiedRests}）";
                notes.Add($"窓{wLo + 1}-{wHi + 1}: 改善なし（夜勤列{tried}本・人間移動{transfersTried}組・正式{checkedN}件・最良推定{est}{verified}）");
            }
        }
        var msg = $"休0日の窓LNS: covO {before.Breakdown.GetValueOrDefault("covO")}->{bestRep.Breakdown.GetValueOrDefault("covO")} / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}窓 対象日 "
            + string.Join("/", targets.Select(t => (t + 1).ToString()))
            + (nightLike.Count > 0 ? " 夜勤型=" + string.Join("/", nightLike.Select(k => state.Shifts[k].Kigou)) : "")
            + " " + string.Join(" | ", notes) + (evaluations >= cfg.MaxEvaluations ? " [評価上限]" : "");
        return new RestZeroLnsResult(work, applied, new[] { Log(msg) });
    }
}
