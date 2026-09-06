using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [FairPolish・グループ内公平化(fair, 重み1)専用の研磨パス] ユーザー指示「c42/c42s以外にも
    /// 『動かせるか』専用オペレータの欠如が無いか棚卸しする」で発見（棚卸し結果はユーザー承認済み）。
    /// fair は群×担当ONシフトごとにメンバー回数の round(平均)からのL1偏差和で、apt(3.223.0)と
    /// ほぼ同型の違反構造。しかし当時の平準化パス（同日2者スワップ＋<b>分散</b>指標での山登り）はチェーン救済が
    /// 無く、交換相手が構造的に不在（希望固定/禁止連続/候補不足）だと頭打ちする、covO/c41/c41s/c42/c42s/apt と
    /// 同型の穴だった（その平準化パス自体は 3.317.0 で実測寄与ゼロを確認して撤去済み）。AptPolish(3.223.0)と同一の3段構成
    /// （①自己振替 ②同一グループ内相互交換 ③玉突きチェーン）をfair向けに移植する。
    ///
    /// fair の目標(tgt)は「その時点のグループ合計の round(平均)」で apt の固定目標と異なり、1日の
    /// 付け替えごとに動く。手①②③はいずれも候補選定のスナップショット近似（各手を試す時点で
    /// counts/tgt を再計算）でよく、最終的な採否は常に isBetter(実目的関数)が担うため、tgt の近似が
    /// ズレても安全性は損なわれない（見逃しても isBetter が拒否するだけ・過大選定しても isBetter が
    /// 拒否するだけ）。採否はisBetter(hard→weighted→total)keep-best＝退化不能。全手とも希望固定
    /// (movable)・禁止連続(makesForbiddenRun)を事前ガード。
    /// </summary>
    public static CyclicSwapResult ApplyFairPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0xFA12L)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var rng = new JavaRandom(seed);
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        string Label(int i, int k) =>
            $"{(i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}")} " +
            $"{(k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString())}";
        var fixedNames = new List<string>();
        // [汎用玉突き結合フレームワーク, 3.249.0] tryChainRelocate(手③)が単独では不採用だった候補を
        //   蓄積し末尾で束ねる。
        var combinable = new List<CombinatorialRepair.Candidate>();
        var rejectCulprits = new RejectCulpritStats();

        int FairTarget(int g, int k, int[][] counts)
        {
            if (g < 0 || g >= p.GroupMembers.Length) return 0;
            var mem = p.GroupMembers[g];
            if (mem.Length == 0) return 0;
            var sum = 0;
            foreach (var x in mem) sum += counts[x][k];
            return (int)KotlinInterop.MathRound(sum / (double)mem.Length);
        }

        // [玉突きチェーンのavoid述語] 候補がfillShiftを1つ得ると、候補自身の群目標(スナップショット近似)
        //   からちょうど新規に乖離するか（既に乖離済みなら中立扱い＝対象外）。
        bool WorsensOwnFair(int staff, int fillShift)
        {
            if (staff < 0 || staff >= p.Sgrp.Length) return false;
            var g = p.Sgrp[staff];
            if (g < 0 || g >= p.Bucket.Length || !p.Bucket[g].Contains(fillShift)) return false;
            var counts = ScheduleUtil.CountMatrix(p, work);
            var tgt = FairTarget(g, fillShift, counts);
            return counts[staff][fillShift] == tgt;
        }

        // [厳密ピン保護] 本パスの全手は i(・相手)の回数を直接変える(apt/fair研磨の本質)ため、staffRange
        //   厳密ピン(lo==hi)を新たに崩す候補だけは不採用にする（keep-best/重みは不変・追加ガードのみ）。
        bool ApplyAndCheck(int i, int j, int fromK, int toK)
        {
            var workBefore = work.Copy2D();
            work[i][j] = toK;
            var rep = UnifiedViolationChecker.Check(state, work);
            var pinBad = V6SearchOperators.ExactPinRegression(p, workBefore, work);
            if (pinBad && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBefore, work);
            if (IsBetter(rep, bestRep) && !pinBad) { bestRep = rep; applied++; return true; }
            rejectCulprits.Record(rep, bestRep, pinBad);
            work[i][j] = fromK;
            return false;
        }

        // 手①: 自身の中でfromK(過多)→toK(過少)への1日付け替え。被覆非悪化の日のみ候補にする。
        bool TrySelfSwap(int i, int fromK, int toK)
        {
            for (var j = 0; j < p.T; j++)
            {
                if (stop()) return false;
                if (work[i][j] != fromK || !Movable(i, j)) continue;
                if (p.MakesForbiddenRun(work, i, j, toK)) continue;
                var cntFrom = 0; var cntTo = 0;
                for (var s = 0; s < p.S; s++) { if (work[s][j] == fromK) cntFrom++; if (work[s][j] == toK) cntTo++; }
                if (p.CovUCell(fromK, j, cntFrom - 1) > p.CovUCell(fromK, j, cntFrom)) continue;
                if (p.CovUCell(toK, j, cntTo + 1) > p.CovUCell(toK, j, cntTo)) continue;
                if (ApplyAndCheck(i, j, fromK, toK)) return true;
            }
            return false;
        }

        // 手②: 同一グループ内で同日の2人の割当をまるごと入替（被覆総量保存＝安全）。
        bool TryMutualSwap(int i, int i2, int sharedK)
        {
            for (var j = 0; j < p.T; j++)
            {
                if (stop()) return false;
                var a = work[i][j]; var b = work[i2][j];
                if (a != sharedK || b == sharedK) continue;
                if (!Movable(i, j) || !Movable(i2, j)) continue;
                if (!p.CanDo(i, b) || !p.CanDo(i2, a)) continue;
                if (p.MakesForbiddenRun(work, i, j, b) || p.MakesForbiddenRun(work, i2, j, a)) continue;
                var workBefore = work.Copy2D();
                work[i][j] = b; work[i2][j] = a;
                var rep = UnifiedViolationChecker.Check(state, work);
                var pinBad = V6SearchOperators.ExactPinRegression(p, workBefore, work);
                if (pinBad && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBefore, work);
                if (IsBetter(rep, bestRep) && !pinBad) { bestRep = rep; applied++; return true; }
                rejectCulprits.Record(rep, bestRep, pinBad);
                work[i][j] = a; work[i2][j] = b;
            }
            return false;
        }

        // 手③: RangePolish/AptPolish型の玉突きチェーン。
        bool TryChainRelocate(int i, int j, int fromK, int toK)
        {
            if (!Movable(i, j) || p.MakesForbiddenRun(work, i, j, toK)) return false;
            var cnt = 0;
            for (var s = 0; s < p.S; s++) if (work[s][j] == fromK) cnt++;
            var needsChain = p.CovUCell(fromK, j, cnt - 1) > p.CovUCell(fromK, j, cnt);
            var workBeforeRelocate = work.Copy2D();
            work[i][j] = toK;
            if (!needsChain)
            {
                var rep = UnifiedViolationChecker.Check(state, work);
                var pinBad = V6SearchOperators.ExactPinRegression(p, workBeforeRelocate, work);
                if (pinBad && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBeforeRelocate, work);
                if (IsBetter(rep, bestRep) && !pinBad) { bestRep = rep; applied++; return true; }
                rejectCulprits.Record(rep, bestRep, pinBad);
                work[i][j] = fromK;
                combinable.Add(new CombinatorialRepair.Candidate(
                    new List<int[]> { new[] { i, j, toK } }, "FairChain", Label(i, fromK)));
                return false;
            }
            var chain = V6SearchOperators.FindCovUChain(p, work, fromK, j, rng, exclude: i,
                rangeAvoid: (st, fk) => WorsensOwnFair(st, fk));
            if (chain == null) { work[i][j] = fromK; return false; }
            var oldVals = chain.Select(mv => work[mv[0]][mv[1]]).ToArray();
            foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
            var rep2 = UnifiedViolationChecker.Check(state, work);
            var pinBad2 = V6SearchOperators.ExactPinRegression(p, workBeforeRelocate, work);
            if (pinBad2 && IsBetter(rep2, bestRep)) pinBlocks.Record(p, workBeforeRelocate, work);
            if (IsBetter(rep2, bestRep) && !pinBad2) { bestRep = rep2; applied++; return true; }
            rejectCulprits.Record(rep2, bestRep, pinBad2);
            for (var idx = 0; idx < chain.Count; idx++) work[chain[idx][0]][chain[idx][1]] = oldVals[idx];
            work[i][j] = fromK;
            combinable.Add(new CombinatorialRepair.Candidate(
                new List<int[]> { new[] { i, j, toK } }.Concat(chain).ToList(), "FairChain", Label(i, fromK)));
            return false;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var locs = rep0.DistLocations.TryGetValue("fair", out var l) ? l : Array.Empty<IReadOnlyList<int>>();
            if (locs.Count == 0) break;
            var counts = ScheduleUtil.CountMatrix(p, work);
            var highTargets = new List<(int, int)>(); // (staff, shift) 過多
            var lowTargets = new List<(int, int)>();  // (staff, shift) 過少
            foreach (var loc in locs)
            {
                if (loc.Count < 2) continue;
                var x = loc[0]; var k = loc[1];
                if (x < 0 || x >= p.S || k < 0 || k >= p.K) continue;
                if (x >= p.Sgrp.Length) continue;
                var g = p.Sgrp[x];
                if (g < 0 || g >= p.Bucket.Length) continue;
                var tgt = FairTarget(g, k, counts);
                if (counts[x][k] > tgt) highTargets.Add((x, k));
                else if (counts[x][k] < tgt) lowTargets.Add((x, k));
            }
            if (highTargets.Count == 0 && lowTargets.Count == 0) break;

            foreach (var (i, k) in highTargets)
            {
                if (stop()) break;
                var done = false;
                // 手①: 自身の別シフトでfairLow(逆方向)のものへ振替（AptPolishと同型に統一。同一
                //   (fromK,toK)ペアで解消するまで反復。isBetterが認める限り繰り返して安全）。
                for (var k2 = 0; k2 < p.K; k2++)
                {
                    if (stop()) break;
                    if (k2 == k || !p.CanDo(i, k2)) continue;
                    if (!lowTargets.Any(t => t.Item1 == i && t.Item2 == k2)) continue;
                    while (TrySelfSwap(i, k, k2)) { improved = true; done = true; }
                }
                if (done) fixedNames.Add(Label(i, k));
                // 手②: 同一グループで逆方向(fairLow)の相手と相互交換。
                if (!done)
                {
                    for (var i2 = 0; i2 < p.S; i2++)
                    {
                        if (done || stop()) break;
                        if (i2 == i || p.Sgrp[i2] != p.Sgrp[i]) continue;
                        if (!lowTargets.Any(t => t.Item1 == i2 && t.Item2 == k)) continue;
                        if (TryMutualSwap(i, i2, k)) { improved = true; done = true; fixedNames.Add(Label(i, k)); }
                    }
                }
                // 手③: 玉突きチェーンで任意の担当可能シフトへ。
                if (!done)
                {
                    for (var j = 0; j < p.T; j++)
                    {
                        if (done || stop()) break;
                        if (work[i][j] != k) continue;
                        foreach (var alt in p.AllowedShiftsForStaff(i))
                        {
                            if (done || stop()) break;
                            if (alt == k) continue;
                            if (TryChainRelocate(i, j, k, alt)) { improved = true; done = true; fixedNames.Add(Label(i, k)); }
                        }
                    }
                }
            }
            // 単独fairLow(自己振替/相互交換で解消しなかった残り)を玉突きチェーンで埋める。
            foreach (var (i, k) in lowTargets)
            {
                if (stop()) break;
                if (!p.CanDo(i, k)) continue;
                var done = false;
                for (var j = 0; j < p.T; j++)
                {
                    if (done || stop()) break;
                    var oldK = work[i][j];
                    if (oldK == k || oldK < 0 || oldK >= p.K) continue;
                    if (TryChainRelocate(i, j, oldK, k)) { improved = true; done = true; fixedNames.Add(Label(i, k)); }
                }
            }
            pass++;
            if (!improved) break;
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames(distLocations由来)より前に実行する。
        //   結合でwork/bestRepが変わってもdistLocationsはbestRep自身から再取得するため自動整合。
        var rejectedOut = new List<CombinatorialRepair.Candidate>();
        var fairCombStats = new CombinatorialRepair.Stats();
        bestRep = CombinatorialRepair.CombineAndApply(
            state, work, bestRep, Enumerable.Reverse(combinable).ToList(), IsBetter,
            shouldStop: stop, stats: fairCombStats, p: p, leftover: rejectedOut);
        applied += fairCombStats.CombosAccepted;
        // [AptPolishと同型] work は毎手の成功時のみコミットしbestRepと同期を保つ（失敗時は必ず巻き戻し）
        //   ため、bestRep.distLocations がそのまま最終盤面の残存箇所＝再チェック不要。
        var stuckLocs = bestRep.DistLocations.TryGetValue("fair", out var sl) ? sl : Array.Empty<IReadOnlyList<int>>();
        var stuckNames = stuckLocs
            .Where(loc => loc.Count >= 2)
            .Select(loc => Label(loc[0], loc[1]))
            .ToList();
        var fairCombSummary = fairCombStats.Summary();
        var fairBefore = before.Breakdown.GetValueOrDefault("fair", 0);
        var fairAfter = bestRep.Breakdown.GetValueOrDefault("fair", 0);
        var msg = $"グループ内公平化(fair)研磨: fair {fairBefore}->{fairAfter} / total {before.Total}->{bestRep.Total} " +
            $"HARD {before.Hard}->{bestRep.Hard} 採用{applied}回";
        if (applied == 0 && fairBefore > 0) msg += " [頭打ち=改善手なし]";
        if (fixedNames.Count > 0) msg += $" 対象: {string.Join(", ", fixedNames)}";
        msg += rejectCulprits.Summary();
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        if (fairCombSummary.Length > 0) msg += $" / {fairCombSummary}";
        var logs = new[] { new MirrorLog(tag: "FairPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks, RejectedCandidates: rejectedOut);
    }
}
