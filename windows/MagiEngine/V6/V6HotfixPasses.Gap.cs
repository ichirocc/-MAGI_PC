using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// シフトの間隔（gap）の研磨（Kotlin <c>GapPolish</c>、Android 3.727.110・3.727.127・3.727.129）。同日 2 者の交換は X を別の日へ動かせないので、
    /// 2 職員 i・i' の行を 2 つの区間で丸ごと入れ替える（各日の各シフト人数は不変）。区間 A は間隔の足りないかたまりを前後 0〜<see cref="GapExt"/> 日
    /// 広げたもの、区間 W は別の 1〜<see cref="GapMaxLen"/> 日（無しも可）。長方形交換は A・W とも長さ 1 の場合にあたる。
    /// 移動できる箇所を先に見つけてから動かす: 区間ごとに「入れ替えても禁止の並び・希望の前日の禁止を作らず、固定（希望）と置けないシフトに
    /// 触れない」かを先に判定し、合格した区間どうしの組だけを、i の間隔の不足が減るときに採点する。採否は他の研磨と同じ keep-best。
    /// 採点で落ちた組は、崩れた期間の制約（c1）の対象シフトを同じ日の 2 人の入れ替えで埋め直して 1 組で採点する（3.727.129）。
    /// C# には差分の採点（<c>DeltaScreen</c>）が無いのでチェッカーで採点する（採否は同じ）。
    /// </summary>
    public static CyclicSwapResult ApplyGapPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, bool quantitativeRangeEval = false)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        if (p.ConsGap.Count == 0 || before.Breakdown.GetValueOrDefault("gap") == 0)
            return new CyclicSwapResult(work, before.Total, before.Total, 0, Array.Empty<MirrorLog>(), Report: before);
        var pinBlocks = new PinBlockAttribution();
        var bestRep = before;
        int applied = 0, tried = 0, slots = 0, repairTried = 0, appliedRepair = 0;
        var c1Shifts = p.Cons1.Select(c => c.ShiftIdx).Where(k => k >= 0 && k < p.K).ToHashSet();
        // 引き分け（採用 0）の理由: 区間ごとの不合格の内訳・組が間隔を減らさない・採点で却下（主因）
        int slotLocked = 0, slotCannotPlace = 0, slotForbidden = 0, noGapDrop = 0, comboForbidden = 0;
        var culprits = new RejectCulpritStats();
        var diagnosed = 0;
        var maxLen = GapSegmentSwap ? GapMaxLen : 1;
        var maxExt = GapSegmentSwap ? GapExt : 0;
        ViolationReport Check(int[][] b) => UnifiedViolationChecker.Check(state, b, quantitativeRangeEval);
        ViolationReport? CheckIfBetter(int[][] b) { var r = Check(b); return UnifiedViolationChecker.BetterReport(r, bestRep) ? r : null; }

        void Swap(int i, int ip, int a, int b)
        {
            for (var j = a; j <= b; j++) (work[i][j], work[ip][j]) = (work[ip][j], work[i][j]);
        }

        // 入れ替えた後の行で、変わったマスが禁止の並び・希望の前日の禁止を作らないか（Swap 済みの盤面で見る）
        bool CleanAfterSwap(int i, int ip, int a, int b)
        {
            for (var j = a; j <= b; j++)
            {
                if (work[i][j] == work[ip][j]) continue;
                if (p.MakesForbiddenRun(work, i, j, work[i][j]) || p.MakesForbiddenRun(work, ip, j, work[ip][j])) return false;
            }
            return true;
        }

        // 区間 [a, b] で i と i' を入れ替えられるか（固定・置けないシフト・新しい禁止の並び）
        bool SlotOk(int i, int ip, int a, int b)
        {
            var differs = false;
            for (var j = a; j <= b; j++)
            {
                int u = work[i][j], v = work[ip][j];
                if (u == v) continue;
                differs = true;
                if (p.WishLocked(i, j) || p.WishLocked(ip, j)) { slotLocked++; return false; }
                if (!p.MayPlaceAt(i, j, v) || !p.MayPlaceAt(ip, j, u)) { slotCannotPlace++; return false; }
            }
            if (!differs) return false;
            Swap(i, ip, a, b);
            var ok = CleanAfterSwap(i, ip, a, b);
            Swap(i, ip, a, b);
            if (!ok) slotForbidden++;
            return ok;
        }

        // 間隔が足りないかたまりの組（前のかたまり＋後のかたまり）。各要素は [開始, 終了]
        List<int[]> ViolatingBlocks(int[] row, int x, int n)
        {
            var output = new List<int[]>();
            int prevStart = -1, prevEnd = -1;
            var j = 0;
            while (j < p.T)
            {
                if (row[j] != x) { j++; continue; }
                var s = j;
                while (j < p.T && row[j] == x) j++;
                var e = j - 1;
                if (prevEnd >= 0 && s - prevEnd < n)
                {
                    if (!output.Any(it => it[0] == prevStart)) output.Add(new[] { prevStart, prevEnd });
                    output.Add(new[] { s, e });
                }
                prevStart = s; prevEnd = e;
            }
            return output;
        }

        // [lo, hi] の日で、職員 i・i' のどちらかの c1 対象でない勤務を、同じ日に c1 対象のシフトの別の人と入れ替える。良くなれば採る
        bool RepairAt(int i, int ip, int lo, int hi, int[][] prev)
        {
            foreach (var s in new[] { i, ip })
                for (var jj = lo; jj <= hi; jj++)
                {
                    if (stop()) return false;
                    var u = work[s][jj];
                    if (c1Shifts.Contains(u) || p.WishLocked(s, jj)) continue;
                    foreach (var x in c1Shifts)
                    {
                        if (!p.MayPlaceAt(s, jj, x)) continue;
                        for (var q = 0; q < p.S; q++)
                        {
                            if (q == s || work[q][jj] != x || p.WishLocked(q, jj) || !p.MayPlaceAt(q, jj, u)) continue;
                            work[s][jj] = x; work[q][jj] = u;
                            if (!p.MakesForbiddenRun(work, s, jj, x) && !p.MakesForbiddenRun(work, q, jj, u))
                            {
                                var rep = CheckIfBetter(work);
                                if (rep is not null && V6SearchOperators.AdoptionGate(p, prev, work, rep, bestRep, pinBlocks).Accepted) { bestRep = rep; return true; }
                            }
                            work[s][jj] = u; work[q][jj] = x;
                        }
                    }
                }
            return false;
        }

        bool OnePass()
        {
            foreach (var c in p.ConsGap)
            {
                var x = c.ShiftIdx;
                for (var i = 0; i < p.S; i++)
                {
                    if (stop()) return false;
                    if (!p.C1Applies(i, x)) continue;
                    var deficit0 = GapRule.Deficit(work[i], x, c.Days);
                    if (deficit0 == 0) continue;
                    for (var ip = 0; ip < p.S; ip++)
                    {
                        if (ip == i) continue;
                        // 区間 A（違反のかたまりを含む）と区間 W（他の場所）の候補を先に見つける
                        var anchors = new List<int[]>();
                        foreach (var bl in ViolatingBlocks(work[i], x, c.Days))
                            for (var lx = 0; lx <= maxExt; lx++)
                            for (var rx = 0; rx <= maxExt; rx++)
                            {
                                int a = bl[0] - lx, b = bl[1] + rx;
                                if (a >= 0 && b < p.T && !anchors.Any(it => it[0] == a && it[1] == b) && SlotOk(i, ip, a, b)) anchors.Add(new[] { a, b });
                            }
                        if (anchors.Count == 0) continue;
                        var others = new List<int[]>();
                        for (var len = 1; len <= maxLen; len++)
                            for (var cs = 0; cs <= p.T - len; cs++)
                                if (SlotOk(i, ip, cs, cs + len - 1)) others.Add(new[] { cs, cs + len - 1 });
                        slots += anchors.Count + others.Count;
                        foreach (var aw in anchors)
                        {
                            int a = aw[0], b = aw[1];
                            foreach (var w in others.Where(it => it[1] < a - 1 || it[0] > b + 1).Append(null))
                            {
                                if (stop()) return false;
                                if (w is null && !GapSegmentSwap) continue;
                                Swap(i, ip, a, b); if (w is not null) Swap(i, ip, w[0], w[1]);
                                var gapDrops = GapRule.Deficit(work[i], x, c.Days) < deficit0;
                                var clean = gapDrops && (w is null || (CleanAfterSwap(i, ip, a, b) && CleanAfterSwap(i, ip, w[0], w[1])));
                                if (!gapDrops) noGapDrop++; else if (!clean) comboForbidden++;
                                if (clean)
                                {
                                    tried++;
                                    var prev = work.Copy2D();
                                    if (w is not null) for (var j = w[0]; j <= w[1]; j++) (prev[i][j], prev[ip][j]) = (prev[ip][j], prev[i][j]);
                                    for (var j = a; j <= b; j++) (prev[i][j], prev[ip][j]) = (prev[ip][j], prev[i][j]);
                                    var full = Check(work);
                                    var rep = UnifiedViolationChecker.BetterReport(full, bestRep) ? full : null;
                                    V6SearchOperators.Adoption? gate = rep is null ? null : V6SearchOperators.AdoptionGate(p, prev, work, rep, bestRep, pinBlocks);
                                    if (rep is not null && gate!.Value.Accepted)
                                    {
                                        bestRep = rep; applied++;
                                        return true;
                                    }
                                    // 1 組の手: 入れ替えで崩れた期間の制約の対象シフト（休など）を、同じ日の 2 人の入れ替えで埋め直してから採点する（各日の人数は不変）
                                    if (rep is null && GapCompoundRepair && c1Shifts.Count > 0 && full.Hard <= bestRep.Hard)
                                    {
                                        repairTried++;
                                        var lo = Math.Max(0, Math.Min(a, w?[0] ?? a) - GapRepairReach);
                                        var hi = Math.Min(p.T - 1, Math.Max(b, w?[1] ?? b) + GapRepairReach);
                                        if (RepairAt(i, ip, lo, hi, prev)) { applied++; appliedRepair++; return true; }
                                    }
                                    if (diagnosed < GapDiagCap)
                                    {
                                        diagnosed++;
                                        culprits.Record(rep ?? Check(work), bestRep, pinBroken: gate?.PinBad == true);
                                    }
                                }
                                if (w is not null) Swap(i, ip, w[0], w[1]);
                                Swap(i, ip, a, b);
                            }
                        }
                    }
                }
            }
            return false;
        }

        var pass = 0;
        while (pass < maxPasses && !stop())
        {
            var improved = false;
            while (!stop() && OnePass()) improved = true;
            pass++;
            if (!improved) break;
        }
        var residual = p.ConsGap.SelectMany(c => Enumerable.Range(0, p.S)
            .Where(i => p.C1Applies(i, c.ShiftIdx) && GapRule.Deficit(work[i], c.ShiftIdx, c.Days) > 0)
            .Select(i => $"{(i < state.StaffList.Count ? state.StaffList[i].Name : i.ToString())} {(c.ShiftIdx >= 0 && c.ShiftIdx < state.Shifts.Count ? state.Shifts[c.ShiftIdx].Kigou : c.ShiftIdx.ToString())}(不足{GapRule.Deficit(work[i], c.ShiftIdx, c.Days)})")).ToList();
        var summary = culprits.Summary();
        var why = bestRep.Breakdown.GetValueOrDefault("gap") == 0 ? "" :
            $" [残った理由: 区間の不合格 固定{slotLocked}・置けない{slotCannotPlace}・禁止の並び{slotForbidden} / 組 間隔が減らない{noGapDrop}・組で禁止の並び{comboForbidden} /{(summary.Length == 0 ? " 採点で却下0件" : summary)}]" +
            (residual.Count == 0 ? "" : $" 残存: {string.Join(", ", residual)}");
        var logs = new[]
        {
            new MirrorLog(tag: "GapPolish",
                message: $"シフトの間隔(gap)研磨(2人の区間入れ替え・移動先を先に探す): gap {before.Breakdown.GetValueOrDefault("gap")}->{bestRep.Breakdown.GetValueOrDefault("gap")} / total {before.Total}->{bestRep.Total} 採用{applied}回(埋め直しの組{appliedRepair}) 移動できる区間{slots} 採点{tried} 埋め直しを試した{repairTried}{why}"),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks, Report: bestRep);
    }

    private const int GapExt = 3;
    private const int GapMaxLen = 7;
    /// <summary>却下の主因を数えるためにチェッカーを全部回す手の上限（ログ用）。</summary>
    private const int GapDiagCap = 300;
    /// <summary>埋め直しを探す範囲＝動かした区間の前後この日数（c1 の窓は 7 日が多い）。</summary>
    private const int GapRepairReach = 6;
    /// <summary>false＝区間 W の長さを 1（長方形交換と同じ近傍）に絞る（測定の対照腕）。</summary>
    public static volatile bool GapSegmentSwap = true;
    /// <summary>入れ替えが採点で落ちたとき、崩れた期間の制約の対象シフトを同じ日の入れ替えで埋め直して 1 組で採点する（false＝測定の対照腕）。</summary>
    public static volatile bool GapCompoundRepair = true;
}
