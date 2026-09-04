using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    private enum WishMoveKind { SameDay, Window, Wings, Rotate3 }
    private static string WishMoveLabel(WishMoveKind k) => k switch { WishMoveKind.SameDay => "同日", WishMoveKind.Window => "窓", WishMoveKind.Wings => "両翼", _ => "巡回" };
    private sealed record WishMove(WishMoveKind Kind, int[] Cells, bool SameGroup, int Island);
    private sealed record WishIsland(int Staff, int[] WishDays, int ZoneFrom, int ZoneTo);

    /// <summary>
    /// [3.496.0 移植元/ユーザー提示の確定仕様] 希望島研磨。実現可能な希望日を固定アンカー（希望セルは不変）にし、影響範囲
    /// （c1 窓長・c3 系パターン長から決まる半径 R）が重なる希望を島へ統合、周辺に違反がある島だけ起動。
    /// 同日交換→可変長窓交換→両翼交換→必要時のみ3職員巡回。全セルで希望固定・担当可否を確認、同日交換優先、群違いは後順位。
    /// 採否は正式チェッカー（HARD→weighted→total）で、通常は希望周辺（局所重み）も全体も改善する手だけ。停滞時のみ短い
    /// ビームで中立手を許し、最終盤面が全体で改善したときだけ採用。最終結果は開始盤面より改善（keep-best）。0..T 内で完結。
    /// </summary>
    public static CyclicSwapResult ApplyWishIslandPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, int maxEvaluations = 120, int beamWidth = 4, int beamDepth = 3, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        int T = p.T, S = p.S, K = p.K;
        var reach = 1;
        foreach (var c in p.Cons1) reach = Math.Max(reach, c.Day1 - 1);
        foreach (var list in new[] { p.Cons3, p.Cons3n, p.Cons3m, p.Cons3mn }) foreach (var c in list) reach = Math.Max(reach, c.Seq.Length - 1);
        reach = Math.Min(reach, Math.Max(1, T - 1));
        string Name(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        bool SameGroup(int a, int b) => p.Sgrp[a] == p.Sgrp[b] && p.Ssk[a] == p.Ssk[b];
        bool Locked(int i, int d) => p.WishLocked(i, d);

        List<WishIsland> BuildIslands()
        {
            var o = new List<WishIsland>();
            for (var i = 0; i < S; i++)
            {
                var days = Enumerable.Range(0, T).Where(d => Locked(i, d)).ToList();
                if (days.Count == 0) continue;
                int from = days[0], to = days[0]; var cur = new List<int> { days[0] };
                void Flush() { o.Add(new WishIsland(i, cur.ToArray(), Math.Max(0, from - reach), Math.Min(T - 1, to + reach))); cur.Clear(); }
                for (var t = 1; t < days.Count; t++)
                {
                    var d = days[t];
                    if (d - reach <= to + reach) { to = d; cur.Add(d); } else { Flush(); from = d; to = d; cur.Add(d); }
                }
                Flush();
            }
            return o;
        }
        long LocalScore(ViolationReport rep, WishIsland isl)
        {
            long s = 0;
            for (var d = isl.ZoneFrom; d <= isl.ZoneTo; d++)
                if (rep.CellFamilies.TryGetValue($"{isl.Staff},{d}", out var fams))
                    foreach (var f in fams) s += Math.Max((long)MirrorKeys.WeightOf(f.StartsWith("vio-") ? f.Substring(4) : f), 1L);
            foreach (var (key, cls) in rep.CountViolations)
            {
                var comma = key.IndexOf(',');
                if (comma > 0 && int.TryParse(key.AsSpan(0, comma), out var i) && i == isl.Staff)
                    s += Math.Max((long)MirrorKeys.WeightOf(cls.StartsWith("vio-") ? cls.Substring(4) : cls), 1L);
            }
            return s;
        }
        bool Active(ViolationReport rep, WishIsland isl) => LocalScore(rep, isl) > 0;

        int[] Apply(WishMove m)
        {
            var old = new int[m.Cells.Length / 3];
            for (var t = 0; t < m.Cells.Length; t += 3) { var i = m.Cells[t]; var d = m.Cells[t + 1]; old[t / 3] = work[i][d]; work[i][d] = m.Cells[t + 2]; }
            return old;
        }
        void Undo(WishMove m, int[] old) { for (var t = 0; t < m.Cells.Length; t += 3) work[m.Cells[t]][m.Cells[t + 1]] = old[t / 3]; }
        var prunedC3n = 0;
        bool MakesForbidden(WishMove m)
        {
            if (p.Cons3n.Count == 0) return false;
            var old = Apply(m); var bad = false;
            for (var t = 0; t < m.Cells.Length && !bad; t += 3) { var i = m.Cells[t]; var d = m.Cells[t + 1]; if (p.MakesForbiddenRun(work, i, d, work[i][d])) bad = true; }
            Undo(m, old);
            return bad;
        }
        void GenSameDay(WishIsland isl, int ix, List<WishMove> o)
        {
            var a = isl.Staff;
            for (var d = isl.ZoneFrom; d <= isl.ZoneTo; d++)
            {
                if (Locked(a, d)) continue;
                var ka = work[a][d]; if (ka < 0 || ka >= K) continue;
                for (var b = 0; b < S; b++)
                {
                    if (b == a || Locked(b, d)) continue;
                    var kb = work[b][d]; if (kb < 0 || kb >= K || kb == ka) continue;
                    if (!p.CanDo(a, kb) || !p.CanDo(b, ka)) continue;
                    o.Add(new WishMove(WishMoveKind.SameDay, new[] { a, d, kb, b, d, ka }, SameGroup(a, b), ix));
                }
            }
        }
        bool WindowOk(int a, int b, int s0, int s1)
        {
            var changes = false;
            for (var d = s0; d <= s1; d++)
            {
                var ka = work[a][d]; var kb = work[b][d];
                if (ka < 0 || ka >= K || kb < 0 || kb >= K) return false;
                if (Locked(a, d) || Locked(b, d)) return false;
                if (!p.CanDo(a, kb) || !p.CanDo(b, ka)) return false;
                if (ka != kb) changes = true;
            }
            return changes;
        }
        void WindowCells(int a, int b, int s0, int s1, List<int> into)
        {
            for (var d = s0; d <= s1; d++) { into.Add(a); into.Add(d); into.Add(work[b][d]); into.Add(b); into.Add(d); into.Add(work[a][d]); }
        }
        void GenWindows(WishIsland isl, int ix, List<WishMove> o)
        {
            var a = isl.Staff; var zl = isl.ZoneTo - isl.ZoneFrom + 1;
            for (var b = 0; b < S; b++)
            {
                if (b == a) continue;
                for (var len = 2; len <= zl; len++)
                    for (var s0 = isl.ZoneFrom; s0 <= isl.ZoneTo - len + 1; s0++)
                    {
                        var s1 = s0 + len - 1;
                        if (!WindowOk(a, b, s0, s1)) continue;
                        var cells = new List<int>(); WindowCells(a, b, s0, s1, cells);
                        o.Add(new WishMove(WishMoveKind.Window, cells.ToArray(), SameGroup(a, b), ix));
                    }
            }
        }
        void GenWings(WishIsland isl, int ix, List<WishMove> o)
        {
            var a = isl.Staff; var first = isl.WishDays[0]; var last = isl.WishDays[^1];
            if (first <= isl.ZoneFrom || last >= isl.ZoneTo) return;
            for (var b = 0; b < S; b++)
            {
                if (b == a) continue;
                for (var l0 = isl.ZoneFrom; l0 < first; l0++)
                    for (var r1 = last + 1; r1 <= isl.ZoneTo; r1++)
                    {
                        if (!WindowOk(a, b, l0, first - 1) || !WindowOk(a, b, last + 1, r1)) continue;
                        var cells = new List<int>(); WindowCells(a, b, l0, first - 1, cells); WindowCells(a, b, last + 1, r1, cells);
                        o.Add(new WishMove(WishMoveKind.Wings, cells.ToArray(), SameGroup(a, b), ix));
                    }
            }
        }
        void GenRotate3(WishIsland isl, int ix, List<WishMove> o)
        {
            var a = isl.Staff;
            for (var d = isl.ZoneFrom; d <= isl.ZoneTo; d++)
            {
                if (Locked(a, d)) continue;
                var ka = work[a][d]; if (ka < 0 || ka >= K) continue;
                for (var b = 0; b < S; b++)
                {
                    if (b == a || Locked(b, d)) continue;
                    var kb = work[b][d]; if (kb < 0 || kb >= K || !p.CanDo(a, kb)) continue;
                    for (var c = 0; c < S; c++)
                    {
                        if (c == a || c == b || Locked(c, d)) continue;
                        var kc = work[c][d]; if (kc < 0 || kc >= K || !p.CanDo(b, kc) || !p.CanDo(c, ka)) continue;
                        if (ka == kb && kb == kc) continue;
                        o.Add(new WishMove(WishMoveKind.Rotate3, new[] { a, d, kb, b, d, kc, c, d, ka }, SameGroup(a, b) && SameGroup(b, c), ix));
                    }
                }
            }
        }
        void Order(List<WishMove> list) => list.Sort((x, y) =>
        {
            var c = ((int)x.Kind).CompareTo((int)y.Kind); if (c != 0) return c;
            c = (x.SameGroup ? 0 : 1).CompareTo(y.SameGroup ? 0 : 1); if (c != 0) return c;
            return x.Cells.Length.CompareTo(y.Cells.Length);
        });

        int applied = 0, evaluated = 0, beamRuns = 0, beamApplied = 0, wishCount = 0, islandCount = 0, activeCount = 0, candTotal = 0;
        var byKind = new Dictionary<string, int>();
        var rejectCulprits = new RejectCulpritStats();
        var stuck = new List<string>();
        var pass = 0;
        while (pass < maxPasses && !stop() && evaluated < maxEvaluations)
        {
            var islands = BuildIslands();
            wishCount = islands.Sum(x => x.WishDays.Length); islandCount = islands.Count;
            var activeIslands = islands.Select((isl, ix) => (isl, ix)).Where(t => Active(bestRep, t.isl)).ToList();
            activeCount = activeIslands.Count;
            if (activeIslands.Count == 0) break;
            var passApplied = 0;
            var islandBudget = Math.Max(8, (maxEvaluations - evaluated) / activeIslands.Count);
            foreach (var (isl, ix) in activeIslands)
            {
                if (stop() || evaluated >= maxEvaluations) break;
                var islandEvals = 0;
                var localBefore = LocalScore(bestRep, isl);
                var cands = new List<WishMove>();
                GenSameDay(isl, ix, cands); GenWindows(isl, ix, cands); GenWings(isl, ix, cands);
                Order(cands);
                WishMove? chosen = null; ViolationReport? chosenRep = null;
                void EvalList(List<WishMove> list)
                {
                    var baseWork = work.Copy2D();
                    foreach (var m in list)
                    {
                        if (stop() || evaluated >= maxEvaluations || islandEvals >= islandBudget) return;
                        if (MakesForbidden(m)) { prunedC3n++; continue; }
                        islandEvals++;
                        var old = Apply(m);
                        var rep = UnifiedViolationChecker.Check(state, work);
                        var pinBad = V6SearchOperators.ExactPinRegression(p, baseWork, work);
                        evaluated++; candTotal++;
                        var globalOk = UnifiedViolationChecker.BetterReport(rep, bestRep) && !pinBad;
                        var localOk = LocalScore(rep, isl) < localBefore;
                        if (pinBad && UnifiedViolationChecker.BetterReport(rep, bestRep)) pinBlocks.Record(p, baseWork, work);
                        Undo(m, old);
                        if (globalOk && localOk && (chosenRep is null || UnifiedViolationChecker.BetterReport(rep, chosenRep))) { chosen = m; chosenRep = rep; }
                        else rejectCulprits.Record(rep, bestRep, pinBad);
                    }
                }
                EvalList(cands);
                if (chosen is null && !stop() && evaluated < maxEvaluations)
                {
                    var rot = new List<WishMove>(); GenRotate3(isl, ix, rot); Order(rot); EvalList(rot);
                }
                if (chosen is not null && chosenRep is not null)
                {
                    Apply(chosen); bestRep = chosenRep; applied++; passApplied++;
                    var lb = WishMoveLabel(chosen.Kind); byKind[lb] = byKind.GetValueOrDefault(lb) + 1;
                }
                else if (!stuck.Contains(Name(isl.Staff))) stuck.Add(Name(isl.Staff));
            }
            if (passApplied == 0)
            {
                beamRuns++;
                var baseline = work.Copy2D();
                var frontier = new List<(int[][] Board, ViolationReport Rep)> { (work.Copy2D(), bestRep) };
                (int[][] Board, ViolationReport Rep)? bestNode = null;
                for (var depth = 0; depth < beamDepth; depth++)
                {
                    if (stop() || evaluated >= maxEvaluations) break;
                    var next = new List<(int[][] Board, ViolationReport Rep)>();
                    foreach (var node in frontier)
                    {
                        for (var s = 0; s < S; s++) Array.Copy(node.Board[s], work[s], T);
                        var isls = BuildIslands().Select((isl, ix) => (isl, ix)).Where(t => Active(node.Rep, t.isl)).ToList();
                        var cands = new List<WishMove>();
                        foreach (var (isl, ix) in isls) { GenSameDay(isl, ix, cands); GenWindows(isl, ix, cands); }
                        Order(cands);
                        foreach (var m in cands)
                        {
                            if (stop() || evaluated >= maxEvaluations) break;
                            if (MakesForbidden(m)) { prunedC3n++; continue; }
                            var old = Apply(m);
                            var rep = UnifiedViolationChecker.Check(state, work);
                            evaluated++;
                            var pinBad = V6SearchOperators.ExactPinRegression(p, node.Board, work);
                            if (!pinBad && !UnifiedViolationChecker.BetterReport(node.Rep, rep)) next.Add((work.Copy2D(), rep));
                            Undo(m, old);
                            if (next.Count >= beamWidth * 6) break;
                        }
                    }
                    if (next.Count == 0) break;
                    next.Sort((x, y) => UnifiedViolationChecker.ReportComparer.Compare(x.Rep, y.Rep));
                    frontier = next.Take(beamWidth).ToList();
                    var top = frontier[0];
                    if (UnifiedViolationChecker.BetterReport(top.Rep, bestRep) && (bestNode is null || UnifiedViolationChecker.BetterReport(top.Rep, bestNode.Value.Rep))) bestNode = top;
                }
                for (var s = 0; s < S; s++) Array.Copy(baseline[s], work[s], T);
                if (bestNode is { } bn && !V6SearchOperators.ExactPinRegression(p, baseline, bn.Board))
                {
                    for (var s = 0; s < S; s++) Array.Copy(bn.Board[s], work[s], T);
                    bestRep = bn.Rep; applied++; beamApplied++;
                }
                else break;
            }
            pass++;
        }
        var improved = UnifiedViolationChecker.BetterReport(bestRep, before);
        var finalSched = improved ? work : ScheduleUtil.NormalizeSchedule(schedule, p);
        var finalRep = improved ? bestRep : before;
        var msg = $"希望島研磨: 希望{wishCount}件→島{islandCount}件(起動{activeCount}件・影響半径{reach}日) 候補評価{candTotal} 正式評価{evaluated}" +
            $" / total {before.Total}->{finalRep.Total} HARD {before.Hard}->{finalRep.Hard} 採用{applied}回" +
            (byKind.Count > 0 ? "(" + string.Join(" ", byKind.Select(kv => $"{kv.Key}:{kv.Value}")) + ")" : "") +
            (beamRuns > 0 ? $" ビーム{beamRuns}回(採用{beamApplied})" : "") +
            (prunedC3n > 0 ? $" 禁止の並びで枝刈り{prunedC3n}" : "") +
            (applied == 0 && activeCount > 0 ? " [頭打ち=改善手なし]" : "") +
            rejectCulprits.Summary() +
            (stuck.Count > 0 ? $" 残存: {string.Join(", ", stuck.Take(8))}{(stuck.Count > 8 ? $" ほか{stuck.Count - 8}名" : "")}" : "");
        var logs = new[] { new MirrorLog(tag: "WishIslandPolish", message: msg) };
        return new CyclicSwapResult(finalSched, before.Total, finalRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
