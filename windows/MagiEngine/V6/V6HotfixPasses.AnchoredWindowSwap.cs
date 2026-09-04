using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>[3.495.0 移植元] ブロック交換の窓の扱い（Kotlin <c>WindowMode</c>）。</summary>
public enum WindowMode { PartialMovableDays, StrictWholeWindow }

/// <summary>回数違反の向き（Kotlin <c>CountDirection</c>）。</summary>
public enum CountDirection { High, Low }

/// <summary>[3.495.0 移植元] 違反アンカー（Kotlin <c>ViolationAnchor</c>）。窓の生成起点。</summary>
public sealed record ViolationAnchor(int Staff, int? Day, int? Shift, CountDirection? Direction, IReadOnlySet<string> Families);

public static partial class V6HotfixPasses
{
    private static long PersonalPenaltyOf(Problem p, int staff, int shift, int count)
    {
        long o = 0;
        var lo = p.RangeLo[staff][shift]; var hi = p.RangeHi[staff][shift];
        if (lo != int.MinValue && count < lo) o += (lo - count) * 90L;
        if (hi != int.MaxValue && count > hi) o += (count - hi) * 45L;
        var apt = p.Apt[staff][shift];
        if (apt >= 0) o += Math.Abs(count - apt);
        return o;
    }

    private sealed record StrictWindowCandidate(int A, int B, int Start, int Length, long Priority, int Changed, bool SameGroup);

    /// <summary>
    /// [3.495.0 移植元/ユーザー提示の設計「違反アンカー型・可変長ウィンドウ交換」] STRICT_WHOLE_WINDOW の本体。
    /// 職員 a,b・開始日 s・長さ L について同じ日付範囲を丸ごと交換（日別人数を完全保存）。1日でも交換不能なら窓全体を不成立。
    /// アンカー＝セル違反の日（連続規則は区間の両端も）／回数超過はそのシフトの全日／回数不足は他職員が持つ日の逆引き／
    /// 週偏りは長さ7の窓を全開始位置で。窓長は 1..Lmax(7)＋連の実長＋規則長＋7＋c1窓長（長い連続違反だけ 14 まで）。
    /// 安価な優先度（HARD 見積り 1e6・アンカー改善 1e5・回数改善 1e4・圧力 16・変更セル −10・群不一致ペナルティ）で絞り、
    /// 正式採否は既存と同一（BetterReport keep-best・ExactPinRegression）。pass ごとに最良1手を採用し再アンカー。
    /// </summary>
    private static CyclicSwapResult ApplyStrictWholeWindow(
        MagiState state, int[][] schedule, int maxPasses, int maxEvaluations, int maxLen, int longLen, Func<bool> stop)
    {
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        if (p.S < 2 || p.T < 1 || maxPasses <= 0 || maxEvaluations <= 0)
            return new CyclicSwapResult(work, before.Total, before.Total, 0,
                new[] { new MirrorLog(tag: "AnchoredWindowSwap", message: "違反アンカー窓交換: 職員ペアなし=スキップ") });
        var lMax = Math.Min(Math.Max(maxLen, 1), p.T);
        var lLong = Math.Min(Math.Max(longLen, lMax), p.T);
        var ruleLens = new HashSet<int>();
        foreach (var c in p.Cons1) if (c.Day1 >= 1 && c.Day1 <= lLong) ruleLens.Add(c.Day1);
        foreach (var list in new[] { p.Cons3, p.Cons3n, p.Cons3m, p.Cons3mn }) foreach (var c in list) if (c.Seq.Length >= 1 && c.Seq.Length <= lLong) ruleLens.Add(c.Seq.Length);
        if (7 <= p.T) ruleLens.Add(7);
        string Name(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        (int First, int Last) RunOf(int i, int j)
        {
            var n = work[i][j]; int a = j, b = j;
            while (a > 0 && work[i][a - 1] == n) a--;
            while (b < p.T - 1 && work[i][b + 1] == n) b++;
            return (a, b);
        }
        bool SameGroup(int a, int b) => p.Sgrp[a] == p.Sgrp[b] && p.Ssk[a] == p.Ssk[b];
        void Swap(StrictWindowCandidate c)
        {
            for (var d = c.Start; d < c.Start + c.Length; d++) { var t = work[c.A][d]; work[c.A][d] = work[c.B][d]; work[c.B][d] = t; }
        }
        int applied = 0, evaluated = 0, built = 0, windowsTried = 0, pinDropped = 0, anchorsTotal = 0;
        var rejectReasons = new Dictionary<string, int>();
        var rejectCulprits = new Dictionary<string, int>();
        var selectedLabels = new List<string>();
        var pass = 0;
        while (pass < maxPasses && !stop())
        {
            var rep = bestRep;
            var counts = ScheduleUtil.CountMatrix(p, work);
            var pressure = new long[p.S];
            void AddPressure(string key, string cls)
            {
                var comma = key.IndexOf(',');
                if (comma < 0 || !int.TryParse(key.AsSpan(0, comma), out var i) || i < 0 || i >= p.S) return;
                var fam = cls.StartsWith("vio-") ? cls.Substring(4) : cls;
                pressure[i] += Math.Max((long)MirrorKeys.WeightOf(fam), 1L);
            }
            foreach (var (k, c) in rep.Violations) AddPressure(k, c);
            foreach (var (k, c) in rep.CountViolations) AddPressure(k, c);
            foreach (var (fam, rows) in rep.DistLocations)
            {
                var w = Math.Max((long)MirrorKeys.WeightOf(fam), 1L);
                foreach (var r in rows) if (r.Count > 0 && r[0] >= 0 && r[0] < p.S) pressure[r[0]] += w;
            }

            var anchors = new List<ViolationAnchor>();
            foreach (var (key, fams) in rep.CellFamilies)
            {
                var parts = key.Split(',');
                if (parts.Length < 2 || !int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var j)) continue;
                if (i < 0 || i >= p.S || j < 0 || j >= p.T) continue;
                var fs = new HashSet<string>(fams);
                anchors.Add(new ViolationAnchor(i, j, null, null, fs));
                if (fs.Any(f => f.StartsWith("vio-c3") || f == "vio-c1") && work[i][j] >= 0 && work[i][j] < p.K)
                {
                    var r = RunOf(i, j);
                    if (r.First != j) anchors.Add(new ViolationAnchor(i, r.First, null, null, fs));
                    if (r.Last != j) anchors.Add(new ViolationAnchor(i, r.Last, null, null, fs));
                }
            }
            foreach (var (key, cls) in rep.CountViolations)
            {
                var parts = key.Split(',');
                if (parts.Length < 2 || !int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var k)) continue;
                if (i < 0 || i >= p.S || k < 0 || k >= p.K) continue;
                CountDirection? dir = cls switch { "vio-high" or "vio-aptHigh" => CountDirection.High, "vio-low" or "vio-aptLow" => CountDirection.Low, _ => null };
                if (dir is null) continue;
                var fs = new HashSet<string> { cls };
                if (dir == CountDirection.High)
                {
                    for (var d = 0; d < p.T; d++) if (work[i][d] == k) anchors.Add(new ViolationAnchor(i, d, k, dir, fs));
                }
                else
                {
                    for (var d = 0; d < p.T; d++)
                    {
                        if (work[i][d] == k || p.WishLocked(i, d)) continue;
                        var any = false;
                        for (var b = 0; b < p.S; b++) if (b != i && work[b][d] == k && !p.WishLocked(b, d)) { any = true; break; }
                        if (any) anchors.Add(new ViolationAnchor(i, d, k, dir, fs));
                    }
                }
            }
            if (rep.DistLocations.TryGetValue("weekly", out var weeklyRows))
                foreach (var row in weeklyRows) if (row.Count > 0 && row[0] >= 0 && row[0] < p.S && 7 <= p.T) anchors.Add(new ViolationAnchor(row[0], null, null, null, new HashSet<string> { "weekly" }));
            if (anchors.Count == 0) break;
            anchorsTotal += anchors.Count;

            var seen = new HashSet<long>();
            var candidates = new List<StrictWindowCandidate>();
            var delta = new int[p.K];
            void TryWindow(ViolationAnchor anc, int start, int length)
            {
                if (start < 0 || length < 1 || start + length > p.T) return;
                var a = anc.Staff; var k = anc.Shift;
                windowsTried++;
                for (var b = 0; b < p.S; b++)
                {
                    if (b == a) continue;
                    var key = ((long)Math.Min(a, b) << 48) | ((long)Math.Max(a, b) << 32) | ((long)start << 16) | (long)length;
                    if (!seen.Add(key)) continue;
                    if (k is int kk0 && anc.Direction is { } dir)
                    {
                        int ca = 0, cb = 0;
                        for (var d = start; d < start + length; d++) { if (work[a][d] == kk0) ca++; if (work[b][d] == kk0) cb++; }
                        if (dir == CountDirection.High && cb >= ca) continue;
                        if (dir == CountDirection.Low && cb <= ca) continue;
                    }
                    var ok = true; var changed = 0;
                    Array.Fill(delta, 0);
                    for (var d = start; d < start + length; d++)
                    {
                        var ka = work[a][d]; var kb = work[b][d];
                        if (ka < 0 || ka >= p.K || kb < 0 || kb >= p.K) { ok = false; break; }
                        if (p.WishLocked(a, d) || p.WishLocked(b, d)) { ok = false; break; }
                        if (!p.CanDo(a, kb) || !p.CanDo(b, ka)) { ok = false; break; }
                        if (ka != kb) { changed++; delta[kb]++; delta[ka]--; }
                    }
                    if (!ok || changed == 0) continue;
                    var pinned = false;
                    for (var kk = 0; kk < p.K && !pinned; kk++)
                    {
                        if (delta[kk] == 0) continue;
                        foreach (var st in new[] { a, b })
                        {
                            var lo = p.RangeLo[st][kk];
                            if (lo != int.MinValue && lo == p.RangeHi[st][kk] && counts[st][kk] == lo) { pinned = true; break; }
                        }
                    }
                    if (pinned) { pinDropped++; continue; }
                    long hardGain = 0;
                    if (p.Cons3n.Count > 0)
                    {
                        var ra = (int[])work[a].Clone(); var rb = (int[])work[b].Clone();
                        var beforeF = C1DeltaPrefilter.StaffC3nFires(p, ra) + C1DeltaPrefilter.StaffC3nFires(p, rb);
                        for (var d = start; d < start + length; d++) { var t = ra[d]; ra[d] = rb[d]; rb[d] = t; }
                        var afterF = C1DeltaPrefilter.StaffC3nFires(p, ra) + C1DeltaPrefilter.StaffC3nFires(p, rb);
                        hardGain = beforeF - afterF;
                    }
                    long anchorGain = 0;
                    if (k is int kk1 && anc.Direction is not null)
                    {
                        var b0 = PersonalPenaltyOf(p, a, kk1, counts[a][kk1]); var a0 = PersonalPenaltyOf(p, a, kk1, counts[a][kk1] + delta[kk1]);
                        anchorGain = a0 < b0 ? 1 : a0 > b0 ? -1 : 0;
                    }
                    else if (anc.Day is int ad && ad >= start && ad < start + length)
                        anchorGain = work[a][ad] != work[b][ad] ? 1 : 0;
                    long countGain = 0;
                    for (var kk = 0; kk < p.K; kk++)
                    {
                        if (delta[kk] == 0) continue;
                        var ga = PersonalPenaltyOf(p, a, kk, counts[a][kk]) - PersonalPenaltyOf(p, a, kk, counts[a][kk] + delta[kk]);
                        var gb = PersonalPenaltyOf(p, b, kk, counts[b][kk]) - PersonalPenaltyOf(p, b, kk, counts[b][kk] - delta[kk]);
                        countGain += Math.Sign(ga) + Math.Sign(gb);
                    }
                    var same = SameGroup(a, b);
                    var priority = hardGain * 1_000_000L + anchorGain * 100_000L + countGain * 10_000L +
                        (pressure[a] + pressure[b]) * 16L - changed * 10L - (same ? 0L : 20_000L);
                    candidates.Add(new StrictWindowCandidate(a, b, start, length, priority, changed, same));
                    built++;
                }
            }
            foreach (var anc in anchors)
            {
                if (stop()) break;
                var lens = new HashSet<int>();
                if (anc.Day is null) lens.Add(7);
                else
                {
                    for (var l = 1; l <= lMax; l++) lens.Add(l);
                    lens.UnionWith(ruleLens);
                    var day = anc.Day.Value;
                    if (work[anc.Staff][day] >= 0 && work[anc.Staff][day] < p.K)
                    {
                        var r = RunOf(anc.Staff, day); var rl = r.Last - r.First + 1;
                        if (rl <= lLong) lens.Add(rl);
                    }
                }
                foreach (var l in lens)
                {
                    if (l < 1 || l > p.T) continue;
                    if (anc.Day is null) { for (var st = 0; st <= p.T - l; st++) TryWindow(anc, st, l); continue; }
                    var j = anc.Day.Value;
                    for (var st = j - l + 1; st <= j; st++) TryWindow(anc, st, l);
                    TryWindow(anc, j - l, l);
                    TryWindow(anc, j + 1, l);
                }
            }
            if (candidates.Count == 0) break;
            candidates.Sort((x, y) =>
            {
                var c = y.Priority.CompareTo(x.Priority); if (c != 0) return c;
                c = y.Changed.CompareTo(x.Changed); if (c != 0) return c;
                c = x.Start.CompareTo(y.Start); if (c != 0) return c;
                c = x.A.CompareTo(y.A); if (c != 0) return c;
                return x.B.CompareTo(y.B);
            });

            var baseWork = work.Copy2D();
            StrictWindowCandidate? chosen = null;
            ViolationReport? chosenRep = null;
            var checkedThisPass = 0;
            foreach (var c in candidates)
            {
                if (stop() || checkedThisPass >= maxEvaluations) break;
                Swap(c);
                var r = UnifiedViolationChecker.Check(state, work);
                var pinRegression = V6SearchOperators.ExactPinRegression(p, baseWork, work);
                if (pinRegression && UnifiedViolationChecker.BetterReport(r, bestRep)) pinBlocks.Record(p, baseWork, work);
                Swap(c);
                checkedThisPass++; evaluated++;
                if (!pinRegression && UnifiedViolationChecker.BetterReport(r, bestRep) && (chosenRep is null || UnifiedViolationChecker.BetterReport(r, chosenRep)))
                {
                    chosen = c; chosenRep = r;
                }
                else
                {
                    var why = pinRegression ? "ピン破り"
                        : r.Hard > bestRep.Hard ? "必須増"
                        : r.Hard < bestRep.Hard ? "採用手に劣後"
                        : r.WeightedScore > bestRep.WeightedScore ? "重み悪化"
                        : r.WeightedScore < bestRep.WeightedScore ? "採用手に劣後"
                        : r.Total < bestRep.Total ? "採用手に劣後"
                        : r.Total > bestRep.Total ? "件数悪化" : "同値";
                    rejectReasons[why] = rejectReasons.GetValueOrDefault(why) + 1;
                    if (why == "重み悪化" || why == "必須増")
                    {
                        var fam = V6SearchOperators.WorstWorsenedFamily(r, bestRep);
                        if (fam is not null) rejectCulprits[fam] = rejectCulprits.GetValueOrDefault(fam) + 1;
                    }
                }
            }
            if (chosen is null || chosenRep is null) break;
            Swap(chosen);
            bestRep = chosenRep;
            applied++;
            selectedLabels.Add($"{chosen.Length}日:{Name(chosen.A)}↔{Name(chosen.B)} {chosen.Start + 1}〜{chosen.Start + chosen.Length}日({chosen.Changed}セル{(chosen.SameGroup ? "" : "・群違い")})");
            pass++;
        }
        var msg = $"違反アンカー窓交換[窓1〜{lMax}日(連続違反は〜{lLong})・窓全体を一括]: total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard}" +
            $" score {(long)before.WeightedScore}->{(long)bestRep.WeightedScore} 採用{applied}回" +
            $" アンカー{anchorsTotal}件 窓{windowsTried}件 実候補{built}件(厳密固定で除外{pinDropped})/正式評価{evaluated}件" +
            (applied == 0 && anchorsTotal > 0 ? " [頭打ち=改善手なし]" : "") +
            (rejectReasons.Count == 0 ? "" : " 不採用内訳: " + string.Join(" ", rejectReasons.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}{kv.Value}"))) +
            (rejectCulprits.Count == 0 ? "" : " (悪化の主因 " + string.Join(" ", rejectCulprits.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key}:{kv.Value}")) + ")") +
            (selectedLabels.Count > 0 ? $" 対象: {string.Join(", ", selectedLabels)}" : "");
        var logs = new[] { new MirrorLog(tag: "AnchoredWindowSwap", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
