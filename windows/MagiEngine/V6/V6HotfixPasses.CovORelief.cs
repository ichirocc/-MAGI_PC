using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>Kotlin <c>CovOReliefPolish.Result</c> の移植。</summary>
    public sealed record CovOReliefResult(
        int[][] NewSchedule,
        int BeforeCovO,
        int AfterCovO,
        int Applied,
        IReadOnlyList<MirrorLog> Logs);

    /// <summary>
    /// [Kotlin原本 <c>CovOReliefPolish.apply</c>] 人員過剰(covO)の退避研磨。過剰セルの在勤者を、受け皿のある担当可シフト
    /// （B4 のような需要 0 のシフトを含む）へ 1 セルずつ動かし、正式チェッカーの keep-best（hard→weighted→total）で採る。
    /// 診断（<c>V6PortAnalyzer</c> の過剰診断）が「移すだけで良くなる」と見つける手と同じ探索を修復として行う。
    /// </summary>
    public static CovOReliefResult ApplyCovOReliefPolish(
        MagiState state, int[][] schedule, int maxMoves = 64, int maxEvaluations = 3_000, Func<bool>? shouldStop = null,
        bool? wishPinStrict = null)
    {
        var stop = shouldStop ?? (() => false);
        var strict = wishPinStrict ?? PolishGate.WishPinStrict;
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0; var evaluations = 0;
        var adopted = new List<string>();
        var famHits = new Dictionary<string, int>();
        var rejected = 0;
        var residual = new List<KeyValuePair<string, string>>();
        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
        string Name(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : i.ToString();
        var cov = new int[p.T][];
        for (var j = 0; j < p.T; j++) cov[j] = new int[p.K];
        void Recount()
        {
            for (var j = 0; j < p.T; j++)
            {
                Array.Clear(cov[j]);
                for (var i = 0; i < p.S; i++) { var k = work[i][j]; if (k >= 0 && k < p.K) cov[j][k]++; }
            }
        }
        Recount();
        var improved = true;
        while (improved && applied < maxMoves && !stop())
        {
            improved = false;
            residual.Clear();
            for (var j = 0; j < p.T; j++)
            for (var k = 0; k < p.K; k++)
            {
                if (stop() || applied >= maxMoves) break;
                if (p.CovOCell(k, j, cov[j][k]) <= 0) continue;
                int bestI = -1, bestM = -1; ViolationReport? bestAfter = null;
                int pinned = 0, noRoom = 0, worse = 0;
                for (var i = 0; i < p.S; i++)
                {
                    if (work[i][j] != k) continue;
                    if (p.WishLocked(i, j) && p.LockTo(i, j) == k) { pinned++; continue; }
                    var tried = false;
                    foreach (var m in p.AllowedShiftsForStaff(i))
                    {
                        if (m == k || p.MakesForbiddenRun(work, i, j, m)) continue;
                        if (!p.WishMoveAllowed(i, j, k, m, strict)) continue;   // 未反映の希望固定セルは希望へだけ
                        if (p.CovOCell(m, j, cov[j][m] + 1) > p.CovOCell(m, j, cov[j][m])) continue;   // 受け皿なし
                        if (evaluations >= maxEvaluations) break;
                        tried = true; evaluations++;
                        work[i][j] = m;
                        var after = UnifiedViolationChecker.Check(state, work);
                        work[i][j] = k;
                        if (UnifiedViolationChecker.BetterReport(after, bestAfter ?? bestRep)) { bestI = i; bestM = m; bestAfter = after; }
                        else
                        {
                            var fam = V6SearchOperators.WorstWorsenedFamily(after, bestRep);
                            if (fam != null) famHits[fam] = famHits.GetValueOrDefault(fam) + 1;
                        }
                    }
                    if (!tried) noRoom++; else if (bestI != i) worse++;
                }
                if (bestI >= 0 && bestAfter != null)
                {
                    adopted.Add($"{Name(bestI)} {j + 1}日 {Sym(k)}→{Sym(bestM)}");
                    work[bestI][j] = bestM; bestRep = bestAfter; applied++; improved = true;
                    Recount();
                }
                else
                {
                    rejected += worse;
                    var why = pinned > 0 && worse == 0 && noRoom == 0 ? "希望固定" : worse > 0 ? "重み悪化" : "受け皿なし";
                    residual.Add(new KeyValuePair<string, string>($"{Sym(k)}@{j + 1}", why));
                }
            }
        }
        var beforeCovO = before.Breakdown.GetValueOrDefault("covO");
        var afterCovO = bestRep.Breakdown.GetValueOrDefault("covO");
        var sb = new System.Text.StringBuilder();
        sb.Append($"人員過剰の退避: covO {beforeCovO}->{afterCovO} / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回");
        if (adopted.Count > 0) sb.Append(" 対象: " + string.Join(", ", adopted.Take(8)) + (adopted.Count > 8 ? $" ほか{adopted.Count - 8}件" : ""));
        if (rejected > 0) sb.Append($" 不採用{rejected}件(主因 " + string.Join(" ", famHits.OrderByDescending(e => e.Value).Take(3).Select(e => $"{e.Key}:{e.Value}")) + ")");
        if (residual.Count > 0) sb.Append(" 残存: " + string.Join(", ", residual.Take(8).Select(e => $"{e.Key}({e.Value})")));
        if (evaluations >= maxEvaluations) sb.Append(" [評価上限で打ち切り]");
        return new CovOReliefResult(work, beforeCovO, afterCovO, applied, new List<MirrorLog> { new MirrorLog("CovORelief", sb.ToString()) });
    }
}
