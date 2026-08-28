using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [AptPolish・適切回数(apt, 重み1)専用の研磨パス] ユーザー指示「専用の研磨パスAptPolish的なものを
    /// 賢く深く網羅的に作る」（grillingで確定: ①自己振替最優先 ②同一グループ内の相互交換(同日1対1・
    /// 被覆総量保存で安全) ③RangePolish型の玉突きチェーン、の順で試す）。
    ///
    /// 動機（大島愛の実例）: 群目標(groupShiftApt)に対しaptHigh(超過)とaptLow(不足)が同一職員内に同時に
    /// 存在するケース（休=超過・Pｼ=不足）は、本人内で1日分を振替えるだけで両方が同時に改善する「タダの
    /// 交換」のはずだが、apt(重み1)はRSI探索中のfocus選択で軽視されやすく(3.169.0)、専用研磨が無いまま
    /// 残っていた。
    ///
    /// アンカー: <c>countViolations</c>（"i,k"→"vio-aptHigh"/"vio-aptLow"、markCountの重み優先解決済）
    /// から違反している(staff,shift)ペアを列挙。
    /// 手①自己振替: 同一職員が別のシフトでaptLow(逆方向)を持つ場合、その2シフト間で1日を直接付け替える
    ///   （他人に一切影響しない最安全な手）。付け替え元/先双方の被覆(covUCell)を悪化させない日のみ候補
    ///   にする（悪化するならチェーンを使わず単に見送り＝真に無償の手のみを対象にする）。
    /// 手②相互交換: 同一グループ(canDo完全一致)内に、同じシフトで逆方向のapt不均衡を持つ相手がいれば、
    ///   同日の2人の割当をまるごと入替える（同日swap＝被覆総量保存＝構造的に安全、BlockSwapPolishと
    ///   同型の安全性。相手のcanDoは同一グループのため保証済み）。
    /// 手③玉突きチェーン: 上記いずれでも解消しない残りは、RangePolishと同型のfindCovUChain（候補が
    ///   自身の新規apt違反を招くなら後回しにするavoid述語つき）で任意の担当可能シフトへ移す。
    /// 採否はisBetter(hard→weighted→total)keep-best＝退化不能。全手とも希望固定(movable)・禁止連続
    /// (makesForbiddenRun)を事前ガード。
    /// </summary>
    public static CyclicSwapResult ApplyAptPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0xA97L)
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

        // [玉突きチェーンのavoid述語] 候補がfillShiftを1つ得ると自身のapt目標からちょうど新規に
        //   乖離するか（既に乖離済みなら「まだ動いていない」ので中立扱い＝対象外）。
        bool WorsensOwnApt(int staff, int fillShift)
        {
            var t = p.Apt[staff][fillShift];
            if (t < 0) return false;
            var c = 0;
            for (var jj = 0; jj < p.T; jj++) if (work[staff][jj] == fillShift) c++;
            return c == t;
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

        // 手③: RangePolish型の玉突きチェーン。
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
                    new List<int[]> { new[] { i, j, toK } }, "AptChain", Label(i, fromK)));
                return false;
            }
            var chain = V6SearchOperators.FindCovUChain(p, work, fromK, j, rng, exclude: i,
                rangeAvoid: (st, fk) => WorsensOwnApt(st, fk));
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
                new List<int[]> { new[] { i, j, toK } }.Concat(chain).ToList(), "AptChain", Label(i, fromK)));
            return false;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var highTargets = new List<(int, int)>();
            var lowTargets = new List<(int, int)>();
            foreach (var (key, cls) in rep0.CountViolations)
            {
                var parts = key.Split(',');
                var i = KotlinInterop.ToIntOrNull(parts.Length > 0 ? parts[0] : null);
                if (i == null) continue;
                var k = KotlinInterop.ToIntOrNull(parts.Length > 1 ? parts[1] : null);
                if (k == null) continue;
                if (cls == "vio-aptHigh") highTargets.Add((i.Value, k.Value));
                else if (cls == "vio-aptLow") lowTargets.Add((i.Value, k.Value));
            }
            if (highTargets.Count == 0 && lowTargets.Count == 0) break;

            foreach (var (i, k) in highTargets)
            {
                if (stop()) break;
                var done = false;
                // 手①: 自身の別シフトでaptLowのものへ振替（同一(fromK,toK)ペアで解消するまで反復＝
                //   RangePolishの「上限まで反復して落とす」と同型に統一。他者に一切影響しない自己完結の
                //   手のためisBetterが認める限り繰り返して安全）。
                for (var k2 = 0; k2 < p.K; k2++)
                {
                    if (stop()) break;
                    if (k2 == k || !p.CanDo(i, k2)) continue;
                    if (!lowTargets.Any(t => t.Item1 == i && t.Item2 == k2)) continue;
                    while (TrySelfSwap(i, k, k2)) { improved = true; done = true; }
                }
                if (done) fixedNames.Add(Label(i, k));
                // 手②: 同一グループで逆方向(aptLow)の相手と相互交換。
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
            // 単独aptLow(自己振替/相互交換で解消しなかった残り)を玉突きチェーンで埋める。
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
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames より前に実行し、結合で解消した箇所が
        //   「残存」に残らないようにする。
        var aptCombStats = new CombinatorialRepair.Stats();
        bestRep = CombinatorialRepair.CombineAndApply(
            state, work, bestRep, Enumerable.Reverse(combinable).ToList(), IsBetter,
            shouldStop: stop, stats: aptCombStats, p: p);
        applied += aptCombStats.CombosAccepted;
        var stuckNames = bestRep.CountViolations
            .Where(kv => kv.Value == "vio-aptHigh" || kv.Value == "vio-aptLow")
            .Select(kv =>
            {
                var parts = kv.Key.Split(',');
                var i = KotlinInterop.ToIntOrNull(parts.Length > 0 ? parts[0] : null);
                if (i == null) return null;
                var k = KotlinInterop.ToIntOrNull(parts.Length > 1 ? parts[1] : null);
                if (k == null) return null;
                return Label(i.Value, k.Value);
            })
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();
        var aptCombSummary = aptCombStats.Summary();
        var aptBefore = before.Breakdown.GetValueOrDefault("apt", 0);
        var aptAfter = bestRep.Breakdown.GetValueOrDefault("apt", 0);
        var msg = $"適切回数(apt)研磨: apt {aptBefore}->{aptAfter} / total {before.Total}->{bestRep.Total} " +
            $"HARD {before.Hard}->{bestRep.Hard} 採用{applied}回";
        if (applied == 0 && aptBefore > 0) msg += " [頭打ち=改善手なし]";
        if (fixedNames.Count > 0) msg += $" 対象: {string.Join(", ", fixedNames)}";
        msg += rejectCulprits.Summary();
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        if (aptCombSummary.Length > 0) msg += $" / {aptCombSummary}";
        var logs = new[] { new MirrorLog(tag: "AptPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
