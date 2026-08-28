using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [C3RunPolish・玉突き連鎖の横展開その3] cons3/cons3m のうち単一シフト連(run-deficit モデル,
    /// HF507/C3Run.rowDeficit)専用の研磨パス。C3mnPolish(3.214.0)/RangePolish(3.215.0)と同じ監査
    /// （ユーザー指摘「他の制約は大丈夫ですか?」）で発見: 既存のC3Polish(2者ブロック交換)/C3Rotate
    /// (3者回転)は「相手が現在の自分のシフトを担当可能」という相互条件を要求し、単一シフト連の
    /// run不足（既存runを隣接日へ伸ばせば直る局面）に対しては交換相手が構造的に存在しないと解消できない。
    ///
    /// スコープ限定（安全側）: 対象は<c>C3Run.IsSingleShiftSeq</c>が真の規則のみ（cons3/cons3mの大半を占める
    /// 典型ケース）。複数シフトのMUST/Wantパターン(非single-shift)は既存のC3Polish/C3Rotateのまま
    /// 対象外＝挙動不変（cellFamiliesの"vio-c3"/"vio-c3m"キーは両方のサブケースで共有されるため、
    /// アンカー自体は両方拾うが、対応するルールが見つからない/runが既に規定長以上のセルは単に
    /// スキップされ何もしない）。
    ///
    /// アンカー: <c>report.CellFamilies</c>から"vio-c3"/"vio-c3m"を含むセル。run-deficitモデルはrun先頭
    /// セルをマークするため、そこから実際の run 境界(runStart..runEnd)を再走査し、隣接日(runStart-1
    /// または runEnd+1)を該当シフトへ拡張する。拡張元シフトの被覆が悪化する場合は<c>FindCovUChain</c>
    /// （C1Polish/C3mnPolish/RangePolishと同一パターン）で玉突き修復。採否はisBetter keep-best＝退化不能。
    /// </summary>
    public static CyclicSwapResult ApplyC3RunPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0xC3A2L)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var rules = new List<(int K, int Len)>();
        foreach (var c in p.Cons3) if (C3Run.IsSingleShiftSeq(c.Seq)) rules.Add((c.Seq[0], c.Seq.Length));
        foreach (var c in p.Cons3m) if (C3Run.IsSingleShiftSeq(c.Seq)) rules.Add((c.Seq[0], c.Seq.Length));
        if (rules.Count == 0)
        {
            return new CyclicSwapResult(work, before.Total, bestRep.Total, 0,
                new[] { new MirrorLog(tag: "C3RunPolish", message: "対象規則(単一シフト連)なし=スキップ") });
        }
        var rng = new JavaRandom(seed);
        var rejectCulprits = new RejectCulpritStats();
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);

        bool TryExtend(int i, int extDay, int fromK, int toK)
        {
            if (!Movable(i, extDay) || p.MakesForbiddenRun(work, i, extDay, toK)) return false;
            var cnt = 0;
            for (var s = 0; s < p.S; s++) if (work[s][extDay] == fromK) cnt++;
            var needsChain = p.CovUCell(fromK, extDay, cnt - 1) > p.CovUCell(fromK, extDay, cnt);
            // [厳密ピン保護] i の fromK→toK 直接付替え(+チェーン)は自身の回数を変える唯一の手のため、
            //   staffRange厳密ピン(lo==hi)を崩す候補は不採用にする（keep-best/重みは不変）。
            var workBeforeExtend = work.Copy2D();
            work[i][extDay] = toK;
            if (!needsChain)
            {
                var rep = UnifiedViolationChecker.Check(state, work);
                var pinBad = V6SearchOperators.ExactPinRegression(p, workBeforeExtend, work);
                if (pinBad && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBeforeExtend, work);
                if (IsBetter(rep, bestRep) && !pinBad) { bestRep = rep; applied++; return true; }
                rejectCulprits.Record(rep, bestRep, pinBad);
                work[i][extDay] = fromK;
                return false;
            }
            var chain = V6SearchOperators.FindCovUChain(p, work, fromK, extDay, rng, exclude: i,
                rangeAvoid: (st, fk) => ExceedsOwnRangeHi(p, work, st, fk));
            if (chain == null) { work[i][extDay] = fromK; return false; }
            var oldVals = chain.Select(mv => work[mv[0]][mv[1]]).ToArray();
            foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
            var rep2 = UnifiedViolationChecker.Check(state, work);
            var pinBad2 = V6SearchOperators.ExactPinRegression(p, workBeforeExtend, work);
            if (pinBad2 && IsBetter(rep2, bestRep)) pinBlocks.Record(p, workBeforeExtend, work);
            if (IsBetter(rep2, bestRep) && !pinBad2) { bestRep = rep2; applied++; return true; }
            rejectCulprits.Record(rep2, bestRep, pinBad2);
            for (var idx = 0; idx < chain.Count; idx++) work[chain[idx][0]][chain[idx][1]] = oldVals[idx];
            work[i][extDay] = fromK;
            return false;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var anchors = new List<(int I, int J)>();
            foreach (var (key, fams) in rep0.CellFamilies)
            {
                if (!fams.Contains("vio-c3") && !fams.Contains("vio-c3m")) continue;
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
                if (stop()) break;
                var k = work[i][j];
                if (k < 0 || k >= p.K) continue;
                var ruleIdx = rules.FindIndex(r => r.K == k);
                if (ruleIdx < 0) continue;
                var rule = rules[ruleIdx];
                var s0 = j;
                while (s0 - 1 >= 0 && work[i][s0 - 1] == k) s0--;
                var e0 = j;
                while (e0 + 1 < p.T && work[i][e0 + 1] == k) e0++;
                if (e0 - s0 + 1 >= rule.Len) continue; // 既に規定長以上=スキップ(古いアンカー)
                var done = false;
                var extDays = new List<int>();
                if (s0 - 1 >= 0) extDays.Add(s0 - 1);
                if (e0 + 1 < p.T) extDays.Add(e0 + 1);
                foreach (var extDay in extDays)
                {
                    if (done || stop()) break;
                    var oldK = work[i][extDay];
                    if (oldK == k || oldK < 0 || oldK >= p.K) continue;
                    if (TryExtend(i, extDay, oldK, k)) { improved = true; done = true; }
                }
            }
            pass++;
            if (!improved) break;
        }
        var stuckNames = StuckStaffNames(state, bestRep.CellFamilies, "vio-c3")
            .Concat(StuckStaffNames(state, bestRep.CellFamilies, "vio-c3m"))
            .Distinct()
            .ToList();
        var c3Before = before.Breakdown.GetValueOrDefault("c3", 0);
        var c3After = bestRep.Breakdown.GetValueOrDefault("c3", 0);
        var c3mBefore = before.Breakdown.GetValueOrDefault("c3m", 0);
        var c3mAfter = bestRep.Breakdown.GetValueOrDefault("c3m", 0);
        var msg = $"連続規則(c3/c3m単一シフト連)玉突き研磨: c3 {c3Before}->{c3After} / c3m {c3mBefore}->{c3mAfter} " +
            $"/ total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回";
        if (applied == 0 && c3Before + c3mBefore > 0) msg += " [頭打ち=改善手なし]";
        msg += rejectCulprits.Summary();
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        var logs = new[] { new MirrorLog(tag: "C3RunPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
