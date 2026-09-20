using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>C3nMarginLnsPolish</c> object.
///
/// [C3n(禁止連続)の前後余白込みLNS, Kotlin原本] <c>ApplyC3nPolish</c>（<see cref="V6HotfixPasses.ApplyC3nPolish"/>）は
/// 違反パターンがまたぐ日だけを1セルずつ独立に付け替えるため、余白日（前後の非違反日）を含めた複数セル
/// 同時destroy-rebuildでしか解けない局面に構造的に届かない。評価式・重みは一切変更しない＝新しい探索候補
/// 生成パスのみで、最終採否は既存の <see cref="UnifiedViolationChecker"/> + <see cref="V6SearchOperators.AdoptionGate"/>
/// （keep-best）に委ねる。
///
/// [測定, Kotlin原本] backlog#26の対象腕のうち専用合成ケースが作れず未計測のまま既定OFFで残っている。
/// C#側もこの結論を踏襲し、パス本体は Kotlin と同値の忠実な移植にとどめる（探索動学は変えない）。
/// </summary>
internal static class C3nMarginLnsPolish
{
    public static V6HotfixPasses.CyclicSwapResult Apply(
        MagiState state, int[][] schedule,
        int marginDays = 2, int maxRestartsPerAnchor = 6, int maxEvaluations = 3_000, int maxPasses = 3,
        Func<bool>? shouldStop = null, long seed = 0xC3E9L, bool quantitativeRangeEval = false)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        var bestRep = before;
        var applied = 0;
        const string tag = "C3nMarginLNS";
        if (p.Cons3n.Count == 0)
        {
            return new V6HotfixPasses.CyclicSwapResult(work, before.Total, bestRep.Total, 0,
                new[] { new MirrorLog(tag: tag, message: "cons3nなし=スキップ") });
        }
        var rng = new JavaRandom(seed);
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        var rejectCulprits = new RejectCulpritStats();
        var evaluated = 0;
        var skippedSmall = 0; // destroy集合が2未満(=1セル経路と重複)でスキップした回数
        var pass = 0;
        while (pass < maxPasses && evaluated < maxEvaluations)
        {
            if (stop()) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
            var anchors = new List<(int I, int J)>();
            foreach (var (key, fams) in rep0.CellFamilies)
            {
                if (!fams.Contains("vio-c3n")) continue;
                var parts = key.Split(',');
                var i = KotlinInterop.ToIntOrNull(parts.Length > 0 ? parts[0] : null);
                if (i == null) continue;
                var j = KotlinInterop.ToIntOrNull(parts.Length > 1 ? parts[1] : null);
                if (j == null) continue;
                anchors.Add((i.Value, j.Value));
            }
            if (anchors.Count == 0) break;
            foreach (var (i, j) in anchors)
            {
                if (stop() || evaluated >= maxEvaluations) break;
                if (i < 0 || i >= p.S || j < 0 || j >= p.T) continue;
                var alts = p.AllowedShiftsForStaff(i);
                if (alts.Length == 0) continue;
                var patternDays = new C3nRowScan(p, work[i]).CoveringDays(j);
                if (patternDays.Length == 0) continue;
                var destroySet = new SortedSet<int>();
                foreach (var d in patternDays)
                {
                    var lo = Math.Max(d - marginDays, 0);
                    var hi = Math.Min(d + marginDays, p.T - 1);
                    for (var dd = lo; dd <= hi; dd++) destroySet.Add(dd);
                }
                destroySet.RemoveWhere(d => !Movable(i, d));
                if (destroySet.Count < 2) { skippedSmall++; continue; }
                var destroyDays = destroySet.ToArray();

                var acceptedHere = false;
                for (var restart = 0; restart < maxRestartsPerAnchor; restart++)
                {
                    if (stop() || evaluated >= maxEvaluations || acceptedHere) break;
                    var order = (int[])destroyDays.Clone();
                    for (var x = order.Length - 1; x >= 1; x--) { var y = rng.NextInt(x + 1); (order[x], order[y]) = (order[y], order[x]); }
                    // [貪欲再構築, Kotlin原本] 未確定日はまだ現在値のまま、確定済みの日は前段の選択を反映した
                    //   行で firesAfterSet を最小化する代替を選ぶ。現在値も候補に含め、同点は rng でタイブレーク。
                    var tentative = (int[])work[i].Clone();
                    foreach (var day in order)
                    {
                        var scan = new C3nRowScan(p, tentative);
                        var bestFires = int.MaxValue;
                        var bestAlts = new List<int>();
                        var curAtDay = tentative[day];
                        foreach (var alt in alts)
                        {
                            var f = scan.FiresAfterSet(day, alt);
                            if (f < bestFires) { bestFires = f; bestAlts.Clear(); bestAlts.Add(alt); }
                            else if (f == bestFires) bestAlts.Add(alt);
                        }
                        var f0 = scan.FiresAfterSet(day, curAtDay);
                        if (f0 < bestFires) { bestFires = f0; bestAlts.Clear(); bestAlts.Add(curAtDay); }
                        else if (f0 == bestFires && !bestAlts.Contains(curAtDay)) bestAlts.Add(curAtDay);
                        tentative[day] = bestAlts[rng.NextInt(bestAlts.Count)];
                    }

                    // 被覆判定は行変更を確定させる前(i がまだ旧シフトに残っている状態)で行う
                    //   （既存パスの needsChain 判定と同じ規約: cnt は i を含む現在人数）。
                    var needsChainDay = new bool[destroyDays.Length];
                    for (var idx = 0; idx < destroyDays.Length; idx++)
                    {
                        var day = destroyDays[idx];
                        var oldK = work[i][day];
                        var newK = tentative[day];
                        if (oldK == newK || oldK < 0 || oldK >= p.K) continue;
                        var cnt = 0;
                        for (var s = 0; s < p.S; s++) if (work[s][day] == oldK) cnt++;
                        needsChainDay[idx] = p.CovUCell(oldK, day, cnt - 1) > p.CovUCell(oldK, day, cnt);
                    }
                    var workBefore = work.Copy2D();
                    foreach (var idx in Enumerable.Range(0, destroyDays.Length)) work[i][destroyDays[idx]] = tentative[destroyDays[idx]];
                    var chainOk = true;
                    for (var idx = 0; idx < destroyDays.Length; idx++)
                    {
                        if (!needsChainDay[idx]) continue;
                        var day = destroyDays[idx];
                        var oldK = workBefore[i][day];
                        var chain = V6SearchOperators.FindCovUChain(p, work, oldK, day, rng, exclude: i,
                            rangeAvoid: (st, fk) => V6HotfixPasses.ExceedsOwnRangeHi(p, work, st, fk));
                        if (chain == null) { chainOk = false; break; }
                        foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
                    }
                    if (!chainOk)
                    {
                        for (var s = 0; s < p.S; s++) work[s] = (int[])workBefore[s].Clone();
                        continue;
                    }
                    evaluated++;
                    var rep = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
                    var gate = V6SearchOperators.AdoptionGate(p, workBefore, work, rep, bestRep, pinBlocks);
                    if (gate.Accepted)
                    {
                        bestRep = rep; applied++; improved = true; acceptedHere = true;
                    }
                    else
                    {
                        rejectCulprits.Record(rep, bestRep, gate.PinBad);
                        for (var s = 0; s < p.S; s++) work[s] = (int[])workBefore[s].Clone();
                    }
                }
            }
            pass++;
            if (!improved) break;
        }
        var stuckNames = V6HotfixPasses.StuckStaffNames(state, bestRep.CellFamilies, "vio-c3n");
        var c3nBefore = before.Breakdown.GetValueOrDefault("c3n", 0);
        var c3nAfter = bestRep.Breakdown.GetValueOrDefault("c3n", 0);
        var msg = $"c3n禁止連続(前後余白込みLNS)研磨: c3n {c3nBefore}->{c3nAfter} / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回" +
            $" 正式評価{evaluated} destroy集合2未満で対象外{skippedSmall}" +
            (applied == 0 && c3nBefore > 0 ? " [頭打ち=改善手なし]" : "") +
            rejectCulprits.Summary() +
            (stuckNames.Count > 0 ? $" 残存: {string.Join(", ", stuckNames)}" : "");
        var logs = new[] { new MirrorLog(tag: tag, message: msg) };
        return new V6HotfixPasses.CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
