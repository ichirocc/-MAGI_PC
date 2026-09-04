using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [3.493.0 移植元/ユーザー指示「夜勤を他職員と交換する違反研磨」] 夜勤連交換研磨。
    ///
    /// 対象＝挟まれセル: 前日が「後続に禁止の並びを持つシフト」（Cons3n の先頭要素＝典型は夜勤）で、翌日が本人希望で
    /// 固定されているセル。禁止の並びのせいでこの日に置けるシフトが 休 か上限0のシフトしか残らず、回数違反が
    /// この日に押し込まれる。1セルの付替えや同日の入替では前日の夜勤が動かないので既存パスは全部却下される。
    ///
    /// 手: 前日を含む夜勤の連（最大 run）R1 を、同じ長さの他職員の連 R2 と窓ごと丸ごと交換する（日別人数と両者の
    /// 夜勤回数を保存）。交換のみ／交換＋挟まれ日を担当可能な別シフトへ付替え、の両方を候補にし、正式チェッカーの
    /// keep-best（BetterReport＝hard→weighted→total、厳密ピン保護つき）で採用＝退化不能。交換後の窓の境界に
    /// 禁止の並びができる組は正式評価前に落とす。正式評価は <paramref name="maxEvaluations"/> で上限。
    /// 実データ（2026-10）では改善手0＝ユーザー判断で「効かないデータでは採用0・無害」前提に導入。
    /// </summary>
    public static CyclicSwapResult ApplyNightRunSwapPolish(
        MagiState state, int[][] schedule, int maxPasses = 2, int maxEvaluations = 400, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var nightShifts = new HashSet<int>();
        foreach (var c in p.Cons3n) if (c.Seq.Length > 0 && c.Seq[0] >= 0 && c.Seq[0] < p.K) nightShifts.Add(c.Seq[0]);
        if (nightShifts.Count == 0)
        {
            return new CyclicSwapResult(work, before.Total, bestRep.Total, 0,
                new[] { new MirrorLog(tag: "NightRunSwapPolish", message: "夜勤連交換研磨: 後続禁止の並びを持つシフトなし=スキップ") });
        }
        var rejectCulprits = new RejectCulpritStats();
        int anchorsSeen = 0, candidates = 0, evaluations = 0, lockedOut = 0, prunedC3n = 0;
        var stuck = new List<string>();
        string NameOf(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        void AddStuck(int i) { var n = NameOf(i); if (!stuck.Contains(n)) stuck.Add(n); }

        (int First, int Last) RunOf(int i, int j, int n)
        {
            int a = j, b = j;
            while (a > 0 && work[i][a - 1] == n) a--;
            while (b < p.T - 1 && work[i][b + 1] == n) b++;
            return (a, b);
        }
        void SwapWindows(int i, int o, (int First, int Last) r1, (int First, int Last) r2)
        {
            for (var t = r1.First; t <= r1.Last; t++) { var a = work[i][t]; work[i][t] = work[o][t]; work[o][t] = a; }
            for (var t = r2.First; t <= r2.Last; t++) { var a = work[i][t]; work[i][t] = work[o][t]; work[o][t] = a; }
        }
        bool WindowsExchangeable(int i, int o, (int First, int Last) r)
        {
            for (var t = r.First; t <= r.Last; t++)
                if (p.WishLocked(i, t) || p.WishLocked(o, t) || !p.CanDo(o, work[i][t]) || !p.CanDo(i, work[o][t])) return false;
            return true;
        }
        bool BoundaryForbidden(int i, int o, (int First, int Last) r1, (int First, int Last) r2)
        {
            foreach (var st in new[] { i, o })
                foreach (var r in new[] { r1, r2 })
                    foreach (var t in new[] { r.First - 1, r.First, r.Last, r.Last + 1 })
                    {
                        if (t < 0 || t >= p.T) continue;
                        var v = work[st][t];
                        if (v >= 0 && v < p.K && p.MakesForbiddenRun(work, st, t, v)) return true;
                    }
            return false;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop() || evaluations >= maxEvaluations) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var anchors = new List<(int I, int J)>();
            for (var i = 0; i < p.S; i++)
                for (var j = 1; j < p.T - 1; j++)
                {
                    var k = work[i][j];
                    if (k < 0 || k >= p.K || nightShifts.Contains(k)) continue;
                    if (!nightShifts.Contains(work[i][j - 1]) || !p.WishLocked(i, j + 1)) continue;
                    var vio = rep0.CountViolations.ContainsKey($"{i},{k}") || rep0.CellFamilies.ContainsKey($"{i},{j}");
                    if (vio) anchors.Add((i, j));
                }
            if (anchors.Count == 0) break;
            foreach (var (i, j) in anchors)
            {
                if (stop() || evaluations >= maxEvaluations) break;
                anchorsSeen++;
                var n = work[i][j - 1];
                var r1 = RunOf(i, j - 1, n);
                var locked = false;
                for (var t = r1.First; t <= r1.Last; t++) if (p.WishLocked(i, t)) { locked = true; break; }
                if (locked) { lockedOut++; AddStuck(i); continue; }
                var k = work[i][j];
                var alts = p.AllowedShiftsForStaff(i).Where(a => a != k).ToArray();
                var done = false;
                for (var o = 0; o < p.S && !done; o++)
                {
                    if (o == i || !p.CanDo(o, n)) continue;
                    var d = 0;
                    while (d < p.T && !done)
                    {
                        if (work[o][d] != n) { d++; continue; }
                        var r2 = RunOf(o, d, n); d = r2.Last + 1;
                        if (r2.Last - r2.First != r1.Last - r1.First) continue;
                        if (r2.First <= r1.Last && r1.First <= r2.Last) continue;
                        if (!WindowsExchangeable(i, o, r1) || !WindowsExchangeable(i, o, r2)) continue;
                        var snapshot = work.Copy2D();
                        SwapWindows(i, o, r1, r2);
                        var pruned = BoundaryForbidden(i, o, r1, r2);
                        SwapWindows(i, o, r1, r2);
                        if (pruned) { prunedC3n++; continue; }
                        var altList = new List<int?> { null };
                        foreach (var a in alts) altList.Add(a);
                        foreach (var alt in altList)
                        {
                            if (stop() || evaluations >= maxEvaluations) break;
                            SwapWindows(i, o, r1, r2);
                            if (alt is int altK)
                            {
                                if (p.MakesForbiddenRun(work, i, j, altK)) { SwapWindows(i, o, r1, r2); continue; }
                                work[i][j] = altK;
                            }
                            candidates++; evaluations++;
                            var rep = UnifiedViolationChecker.Check(state, work);
                            var pinBad = V6SearchOperators.ExactPinRegression(p, snapshot, work);
                            if (pinBad && UnifiedViolationChecker.BetterReport(rep, bestRep)) pinBlocks.Record(p, snapshot, work);
                            if (UnifiedViolationChecker.BetterReport(rep, bestRep) && !pinBad) { bestRep = rep; applied++; improved = true; done = true; break; }
                            rejectCulprits.Record(rep, bestRep, pinBad);
                            for (var s = 0; s < p.S; s++) Array.Copy(snapshot[s], work[s], p.T);
                        }
                    }
                }
                if (!done) AddStuck(i);
            }
            pass++;
            if (!improved) break;
        }
        var msg = $"夜勤連交換研磨(前日夜勤×翌日希望固定の挟まれセル): 対象{anchorsSeen}件 候補{candidates}手 正式評価{evaluations}" +
            $" / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回" +
            (applied == 0 && anchorsSeen > 0 ? " [頭打ち=改善手なし]" : "") +
            (lockedOut > 0 ? $" 夜勤連が希望固定で交換不可{lockedOut}件" : "") +
            (prunedC3n > 0 ? $" 境界の禁止の並びで枝刈り{prunedC3n}組" : "") +
            rejectCulprits.Summary() +
            (stuck.Count > 0 ? $" 残存: {string.Join(", ", stuck)}" : "");
        var logs = new[] { new MirrorLog(tag: "NightRunSwapPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
