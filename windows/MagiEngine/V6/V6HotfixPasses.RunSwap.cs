using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [3.494.0 移植元/ユーザー指示「汎用性を重視する。特定のシフトを特別扱いしない」] 連交換研磨。
    /// 3.493.0 の夜勤連交換研磨（Cons3n の先頭要素＝夜勤・翌日が希望固定、という前提つき）を置き換える汎用版。
    ///
    /// アンカー＝あらゆる違反: セル違反 (i,j) はその前日・当日・翌日を含む同一シフトの最大連（シフトの種類を問わない）、
    /// 回数違反 (i,k) は職員 i の行の全ての最大連。手＝連 R1 を同じシフト・同じ長さ・日が重ならない他職員の連 R2 と
    /// 窓ごと丸ごと交換（日別人数と、その連のシフトの両者の回数を保存）。交換のみ／交換＋違反セルの付替えを候補にし、
    /// 正式チェッカーの keep-best（BetterReport・厳密ピン保護）で採用＝退化不能。窓の境界に禁止の並びができる組は
    /// 正式評価前に落とし、付替えは希望固定でなく元シフトの被覆を欠かさないセルだけ。正式評価は
    /// <paramref name="maxEvaluations"/> で上限。
    /// </summary>
    public static CyclicSwapResult ApplyRunSwapPolish(
        MagiState state, int[][] schedule, int maxPasses = 2, int maxEvaluations = 600, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var rejectCulprits = new RejectCulpritStats();
        int anchorsSeen = 0, candidates = 0, evaluations = 0, prunedC3n = 0, noPartner = 0;
        var stuck = new List<string>();
        string NameOf(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        void AddStuck(int i) { var n = NameOf(i); if (!stuck.Contains(n)) stuck.Add(n); }

        (int First, int Last) RunOf(int i, int j)
        {
            var n = work[i][j]; int a = j, b = j;
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
        bool InRange(int t, (int First, int Last) r) => t >= r.First && t <= r.Last;

        bool TryExchange(int i, (int First, int Last) r1, IReadOnlyList<int> reassign)
        {
            var n = work[i][r1.First];
            if (n < 0 || n >= p.K) return false;
            for (var t = r1.First; t <= r1.Last; t++) if (p.WishLocked(i, t)) return false;
            var partners = 0;
            for (var o = 0; o < p.S; o++)
            {
                if (o == i || !p.CanDo(o, n)) continue;
                var d = 0;
                while (d < p.T)
                {
                    if (stop() || evaluations >= maxEvaluations) return false;
                    if (work[o][d] != n) { d++; continue; }
                    var r2 = RunOf(o, d); d = r2.Last + 1;
                    if (r2.Last - r2.First != r1.Last - r1.First) continue;
                    if (r2.First <= r1.Last && r1.First <= r2.Last) continue;
                    if (!WindowsExchangeable(i, o, r1) || !WindowsExchangeable(i, o, r2)) continue;
                    partners++;
                    var snapshot = work.Copy2D();
                    SwapWindows(i, o, r1, r2);
                    var pruned = BoundaryForbidden(i, o, r1, r2);
                    SwapWindows(i, o, r1, r2);
                    if (pruned) { prunedC3n++; continue; }
                    var moves = new List<(int J, int Alt)?> { null };
                    foreach (var j in reassign)
                    {
                        if (InRange(j, r1) || InRange(j, r2) || p.WishLocked(i, j)) continue;
                        var fromK = work[i][j]; if (fromK < 0 || fromK >= p.K) continue;
                        var cnt = 0; for (var s = 0; s < p.S; s++) if (work[s][j] == fromK) cnt++;
                        if (p.CovUCell(fromK, j, cnt - 1) > p.CovUCell(fromK, j, cnt)) continue;
                        foreach (var alt in p.AllowedShiftsForStaff(i)) if (alt != fromK) moves.Add((j, alt));
                    }
                    foreach (var mv in moves)
                    {
                        if (stop() || evaluations >= maxEvaluations) return false;
                        SwapWindows(i, o, r1, r2);
                        if (mv is { } m)
                        {
                            if (p.MakesForbiddenRun(work, i, m.J, m.Alt)) { SwapWindows(i, o, r1, r2); continue; }
                            work[i][m.J] = m.Alt;
                        }
                        candidates++; evaluations++;
                        var rep = UnifiedViolationChecker.Check(state, work);
                        var pinBad = V6SearchOperators.ExactPinRegression(p, snapshot, work);
                        if (pinBad && UnifiedViolationChecker.BetterReport(rep, bestRep)) pinBlocks.Record(p, snapshot, work);
                        if (UnifiedViolationChecker.BetterReport(rep, bestRep) && !pinBad) { bestRep = rep; applied++; return true; }
                        rejectCulprits.Record(rep, bestRep, pinBad);
                        for (var s = 0; s < p.S; s++) Array.Copy(snapshot[s], work[s], p.T);
                    }
                }
            }
            if (partners == 0) noPartner++;
            return false;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop() || evaluations >= maxEvaluations) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var anchors = new List<(int I, List<(int First, int Last)> Runs, List<int> Reassign)>();
            foreach (var key in rep0.CellFamilies.Keys)
            {
                var parts = key.Split(',');
                if (parts.Length < 2 || !int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var j)) continue;
                if (i < 0 || i >= p.S || j < 0 || j >= p.T) continue;
                var runs = new List<(int First, int Last)>();
                foreach (var t in new[] { j - 1, j, j + 1 })
                {
                    if (t < 0 || t >= p.T || work[i][t] < 0 || work[i][t] >= p.K) continue;
                    var r = RunOf(i, t); if (!runs.Contains(r)) runs.Add(r);
                }
                anchors.Add((i, runs, new List<int> { j }));
            }
            foreach (var (key, cls) in rep0.CountViolations)
            {
                var parts = key.Split(',');
                if (parts.Length < 2 || !int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var k)) continue;
                if (i < 0 || i >= p.S) continue;
                var runs = new List<(int First, int Last)>(); var d = 0;
                while (d < p.T) { if (work[i][d] < 0 || work[i][d] >= p.K) { d++; continue; } var r = RunOf(i, d); runs.Add(r); d = r.Last + 1; }
                var reassign = new List<int>();
                if (cls == "vio-high" || cls == "vio-aptHigh") for (var t = 0; t < p.T; t++) if (work[i][t] == k) reassign.Add(t);
                anchors.Add((i, runs, reassign));
            }
            if (anchors.Count == 0) break;
            foreach (var a in anchors)
            {
                if (stop() || evaluations >= maxEvaluations) break;
                anchorsSeen++;
                var done = false;
                foreach (var r in a.Runs) { if (done) break; if (TryExchange(a.I, r, a.Reassign)) { done = true; improved = true; } }
                if (!done) AddStuck(a.I);
            }
            pass++;
            if (!improved) break;
        }
        var stuckTxt = stuck.Count == 0 ? "" :
            $" 残存: {string.Join(", ", stuck.Take(8))}{(stuck.Count > 8 ? $" ほか{stuck.Count - 8}名" : "")}";
        var msg = $"連交換研磨(違反に隣接する同一シフトの連を他職員の同じ長さの連と窓ごと交換): 対象{anchorsSeen}件 候補{candidates}手 正式評価{evaluations}" +
            $" / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回" +
            (applied == 0 && anchorsSeen > 0 ? " [頭打ち=改善手なし]" : "") +
            (evaluations >= maxEvaluations ? $" 評価上限{maxEvaluations}到達" : "") +
            (noPartner > 0 ? $" 交換相手なし{noPartner}連" : "") +
            (prunedC3n > 0 ? $" 境界の禁止の並びで枝刈り{prunedC3n}組" : "") +
            rejectCulprits.Summary() + stuckTxt;
        var logs = new[] { new MirrorLog(tag: "RunSwapPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
