using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Integrates the elite archive after the adaptive islands stop.
///
/// The integration has two independent keep-best stages:
///  1. bidirectional path relinking between quality/diversity/bridge elites,
///  2. disagreement-region beam fusion using only values present in the elites.
///
/// Bridge schedules are never returned directly. Every adopted schedule is re-evaluated by the
/// official checker, must improve HARD -&gt; weightedScore -&gt; total, and must not regress exact pins.
/// </summary>
internal static class EliteIntegrationPolish
{
    public sealed record Config(
        int MaxElites = 12,
        int MaxPairs = 12,
        int PathOrdersPerDirection = 2,
        int MaxFusionGroups = 8,
        int MaxFusionCells = 14,
        int BeamWidth = 12,
        int HardDebt = 1,
        int TotalDebt = 24);

    public sealed record Result(
        int[][] Schedule,
        ViolationReport Report,
        IReadOnlyList<MirrorLog> Logs,
        int ElitesUsed,
        int RelinkPaths,
        int RelinkImprovements,
        int FusionGroups,
        int FusionImprovements);

    private sealed record Candidate(
        int[][] Schedule,
        ViolationReport Report,
        HypothesisEpochRole? Role,
        bool Bridge);

    private sealed record BeamNode(
        int[][] Schedule,
        ViolationReport Report,
        int Changed);

    public static Result Apply(
        MagiState state,
        int[][] rootSchedule,
        IReadOnlyList<AdaptiveElite> elites,
        Func<bool> shouldStop,
        long deadlineMs,
        Config? config = null)
    {
        config ??= new Config();
        var p = ScheduleUtil.CachedProblem(state);
        var root = rootSchedule.Copy2D();
        var rootReport = UnifiedViolationChecker.Check(state, root);
        var bestSchedule = root.Copy2D();
        var bestReport = rootReport;
        var relinkPaths = 0;
        var relinkImprovements = 0;
        var fusionGroups = 0;
        var fusionImprovements = 0;

        var candidates = new List<Candidate> { new(root.Copy2D(), rootReport, null, false) };
        foreach (var e in elites.Take(config.MaxElites))
        {
            if (AdaptiveEliteArchive.SameSchedule(root, e.Schedule)) continue;
            if (candidates.Any(c => AdaptiveEliteArchive.SameSchedule(c.Schedule, e.Schedule))) continue;
            candidates.Add(new Candidate(e.Schedule.Copy2D(), e.Report, e.Role, e.Bridge));
        }
        if (candidates.Count <= 1 || Stopped(shouldStop, deadlineMs))
        {
            // [3.349.2/敵対検証] 旧実装はここで**ログを1行も返さなかった**ので、実機ログから
            //   「統合が走ったが素材が無かった」のか「そもそも呼ばれていない」のかが読めなかった。
            //   ただし PORTFOLIO 以外は毎回 elites が空＝毎実行1行のノイズになるので、
            //   **エリートはあったのに1件も使えなかったとき**だけ出す。これは
            //   「全ワーカーが同じ解へ潰れた」という意味のある信号（3.332.0 の距離0と同じ）。
            var note = elites.Count == 0
                ? new List<MirrorLog>()
                : new List<MirrorLog>
                {
                    new("EliteIntegration",
                        $"エリート統合: 素材{elites.Count}件はすべて現在の勤務表と同一" +
                        (Stopped(shouldStop, deadlineMs) ? "／または締切" : "") + "＝統合の余地なし"),
                };
            return new Result(root, rootReport with { Logs = note.Concat(rootReport.Logs).ToList() }, note,
                candidates.Count - 1, 0, 0, 0, 0);
        }

        // Non-bridge endpoints are official return candidates in their own right. Re-check them
        // instead of trusting the archived report; bridge schedules remain search material only.
        foreach (var candidate in candidates.Skip(1))
        {
            if (candidate.Bridge || Stopped(shouldStop, deadlineMs)) continue;
            var checkedReport = UnifiedViolationChecker.Check(state, candidate.Schedule);
            if (Better(checkedReport, bestReport) &&
                !V6SearchOperators.ExactPinRegression(p, root, candidate.Schedule))
            {
                bestSchedule = candidate.Schedule.Copy2D();
                bestReport = checkedReport;
            }
        }

        var pairs = SelectPairs(candidates, config.MaxPairs);
        foreach (var (aIdx, bIdx) in pairs)
        {
            if (Stopped(shouldStop, deadlineMs)) break;
            var a = candidates[aIdx];
            var b = candidates[bIdx];
            var directions = new[] { (Source: a, Target: b), (Source: b, Target: a) };
            foreach (var (source, target) in directions)
            {
                if (Stopped(shouldStop, deadlineMs)) break;
                var orderCount = Math.Clamp(config.PathOrdersPerDirection, 1, 2);
                for (var variant = 0; variant < orderCount; variant++)
                {
                    if (Stopped(shouldStop, deadlineMs)) break;
                    relinkPaths++;
                    var improved = RelinkOnePath(
                        state, p, root, source, target, variant, shouldStop, deadlineMs, bestReport);
                    if (improved != null && Better(improved.Value.Report, bestReport))
                    {
                        bestSchedule = improved.Value.Schedule;
                        bestReport = improved.Value.Report;
                        relinkImprovements++;
                    }
                }
            }
        }

        var fusionCandidates = new List<Candidate> { new(bestSchedule.Copy2D(), bestReport, null, false) };
        foreach (var c in candidates.OrderBy(x => x, CandidateComparator))
        {
            if (fusionCandidates.Any(fc => AdaptiveEliteArchive.SameSchedule(fc.Schedule, c.Schedule))) continue;
            fusionCandidates.Add(c);
            if (fusionCandidates.Count >= Math.Min(config.MaxElites, 9)) break;
        }
        var groups = SelectFusionGroups(fusionCandidates, config.MaxFusionGroups);
        foreach (var group in groups)
        {
            if (Stopped(shouldStop, deadlineMs)) break;
            fusionGroups++;
            var improved = FuseGroup(
                state, p, root, bestSchedule, bestReport,
                group.Select(idx => fusionCandidates[idx]).ToList(),
                shouldStop, deadlineMs, config);
            if (improved != null && Better(improved.Value.Report, bestReport))
            {
                bestSchedule = improved.Value.Schedule;
                bestReport = improved.Value.Report;
                fusionImprovements++;
            }
        }

        var finalChecked = UnifiedViolationChecker.Check(state, bestSchedule);
        var valid = Better(finalChecked, rootReport) &&
            !V6SearchOperators.ExactPinRegression(p, root, bestSchedule);
        var chosen = valid ? bestSchedule.Copy2D() : root.Copy2D();
        var chosenReport = valid ? finalChecked : rootReport;
        var log = new MirrorLog(
            tag: "EliteIntegration",
            message: $"エリート統合: elite={candidates.Count - 1} relink={relinkPaths}(改善{relinkImprovements}) " +
                $"fusion={fusionGroups}(改善{fusionImprovements}) / HARD {rootReport.Hard}->{chosenReport.Hard} " +
                $"total {rootReport.Total}->{chosenReport.Total} 採用={(valid ? 1 : 0)}");
        return new Result(
            chosen,
            chosenReport with { Logs = new List<MirrorLog> { log }.Concat(chosenReport.Logs).ToList() },
            new List<MirrorLog> { log },
            candidates.Count - 1,
            relinkPaths,
            relinkImprovements,
            fusionGroups,
            fusionImprovements);
    }

    private static (int[][] Schedule, ViolationReport Report)? RelinkOnePath(
        MagiState state,
        Problem p,
        int[][] rootSchedule,
        Candidate source,
        Candidate target,
        int variant,
        Func<bool> shouldStop,
        long deadlineMs,
        ViolationReport incumbentReport)
    {
        var current = source.Schedule.Copy2D();
        var diffs = new List<(int I, int J)>();
        for (var i = 0; i < current.Length; i++)
        for (var j = 0; j < current[i].Length; j++)
        {
            if (i < target.Schedule.Length && j < target.Schedule[i].Length &&
                current[i][j] != target.Schedule[i][j])
            {
                diffs.Add((i, j));
            }
        }
        if (diffs.Count == 0) return null;

        // [賢く再構成] c1(期間要件の窓不足)の違反セルを最優先し、他族の違反セル・非違反セルの順で並べる。
        // 従来は「違反セルか否か」の2階層のみで、covU/pref等の件数が多いデータではc1セルが希釈され
        // 後回しにされ得た（C1JointLnsPolish/EliteIntegrationPolish双方でユーザー指摘の穴）。
        var c1Priority = C1Cells(source.Report);
        var violationCells = ViolationCells(source.Report);
        // [並べ替え=List<T>.Sort] diffs は互いに重複しない(i,j)セルの集合＝行(I)・列(J)キーが常に
        //   全要素を一意に確定するため、List<T>.Sort（不安定・in-place）でもタイは発生しない。
        diffs.Sort((x, y) =>
        {
            var rx = c1Priority.Contains(x) ? 0 : violationCells.Contains(x) ? 1 : 2;
            var ry = c1Priority.Contains(y) ? 0 : violationCells.Contains(y) ? 1 : 2;
            if (rx != ry) return rx.CompareTo(ry);
            if (x.I != y.I) return x.I.CompareTo(y.I);
            return x.J.CompareTo(y.J);
        });
        if (variant == 1) diffs.Reverse();

        int[][]? bestSchedule = null;
        var bestReport = incumbentReport;
        foreach (var (i, j) in diffs)
        {
            if (Stopped(shouldStop, deadlineMs)) break;
            var k = target.Schedule[i][j];
            if (p.WishLocked(i, j) && p.Wish[i][j] != k) continue;
            if (!p.CanDo(i, k)) continue;
            current[i][j] = k;
            var report = UnifiedViolationChecker.Check(state, current);
            if (Better(report, bestReport) && !V6SearchOperators.ExactPinRegression(p, rootSchedule, current))
            {
                bestSchedule = current.Copy2D();
                bestReport = report;
            }
        }
        return bestSchedule == null ? null : (bestSchedule, bestReport);
    }

    private static (int[][] Schedule, ViolationReport Report)? FuseGroup(
        MagiState state,
        Problem p,
        int[][] rootSchedule,
        int[][] currentBest,
        ViolationReport currentBestReport,
        List<Candidate> group,
        Func<bool> shouldStop,
        long deadlineMs,
        Config config)
    {
        if (group.Count < 2) return null;

        // [賢く再構成] relinkOnePathと同じ3階層(c1違反セル最優先)。maxFusionCellsの枠がc1改善に
        // 使われずcovU/pref等の件数優位な族に埋め尽くされる問題への対応。
        var c1Priority = C1Cells(currentBestReport);
        var priority = ViolationCells(currentBestReport);
        var cells = new List<(int I, int J)>();
        for (var i = 0; i < currentBest.Length; i++)
        for (var j = 0; j < currentBest[i].Length; j++)
        {
            var values = new HashSet<int> { currentBest[i][j] };
            foreach (var c in group)
            {
                if (i < c.Schedule.Length && j < c.Schedule[i].Length) values.Add(c.Schedule[i][j]);
            }
            if (values.Count > 1) cells.Add((i, j));
        }
        if (cells.Count == 0) return null;

        int Diversity((int I, int J) cell) =>
            group.Where(c => cell.I < c.Schedule.Length && cell.J < c.Schedule[cell.I].Length)
                .Select(c => c.Schedule[cell.I][cell.J])
                .Distinct()
                .Count();

        // [並べ替え=List<T>.Sort] cells も diffs と同じく重複しない(i,j)セルの集合＝末尾2キー(行・列)が
        //   常に全要素を一意に確定する（rank・diversityのタイはあり得るが、その先で必ず割れる）。
        cells.Sort((x, y) =>
        {
            var rx = c1Priority.Contains(x) ? 0 : priority.Contains(x) ? 1 : 2;
            var ry = c1Priority.Contains(y) ? 0 : priority.Contains(y) ? 1 : 2;
            if (rx != ry) return rx.CompareTo(ry);
            var dx = Diversity(x);
            var dy = Diversity(y);
            if (dx != dy) return dy.CompareTo(dx); // descending
            if (x.I != y.I) return x.I.CompareTo(y.I);
            return x.J.CompareTo(y.J);
        });
        var selected = cells.Take(config.MaxFusionCells).ToList();
        var beam = new List<BeamNode> { new(currentBest.Copy2D(), currentBestReport, 0) };
        int[][]? bestSchedule = null;
        var bestReport = currentBestReport;

        foreach (var (i, j) in selected)
        {
            if (Stopped(shouldStop, deadlineMs)) break;
            // [移植メモ] Kotlin の LinkedHashSet と同じ「挿入順を保持する集合」を、
            //   List(順序)+HashSet(O(1)判定)の組で明示的に再現する。
            var valuesList = new List<int>();
            var valuesSeen = new HashSet<int>();
            foreach (var node in beam)
            {
                if (valuesSeen.Add(node.Schedule[i][j])) valuesList.Add(node.Schedule[i][j]);
            }
            foreach (var c in group)
            {
                if (i < c.Schedule.Length && j < c.Schedule[i].Length && valuesSeen.Add(c.Schedule[i][j]))
                {
                    valuesList.Add(c.Schedule[i][j]);
                }
            }
            var next = new List<BeamNode>();
            var seen = new Dictionary<long, List<int[][]>>();
            foreach (var node in beam)
            {
                foreach (var k in valuesList)
                {
                    if (p.WishLocked(i, j) && p.Wish[i][j] != k) continue;
                    if (!p.CanDo(i, k)) continue;
                    var changed = node.Schedule[i][j] == k ? node.Changed : node.Changed + 1;
                    var schedule = node.Schedule.Copy2D();
                    schedule[i][j] = k;
                    var hash = AdaptiveEliteArchive.ScheduleHash(schedule);
                    if (!seen.TryGetValue(hash, out var bucket))
                    {
                        bucket = new List<int[][]>();
                        seen[hash] = bucket;
                    }
                    if (bucket.Any(b => AdaptiveEliteArchive.SameSchedule(b, schedule))) continue;
                    bucket.Add(schedule);
                    var report = UnifiedViolationChecker.Check(state, schedule);
                    if (!WithinDebt(report, currentBestReport, config)) continue;
                    var child = new BeamNode(schedule, report, changed);
                    next.Add(child);
                    if (Better(report, bestReport) &&
                        !V6SearchOperators.ExactPinRegression(p, rootSchedule, schedule))
                    {
                        bestSchedule = schedule.Copy2D();
                        bestReport = report;
                    }
                }
            }
            // [3.278.0/監査修正] 1セルの候補全滅（wishLocked で希望値が候補に無い・全候補が非canDo 等）で
            //   break すると**残り全セルの融合を放棄**していた。正しくはこのセルだけ skip して現ビームのまま継続。
            if (next.Count == 0) continue;
            beam = next.OrderBy(n => n, BeamComparator).Take(config.BeamWidth).ToList();
        }
        return bestSchedule == null ? null : (bestSchedule, bestReport);
    }

    /// <summary>
    /// ビーム中間ノードの許容幅。<paramref name="baseline"/> は**呼出時点の現在最良**（<see cref="FuseGroup"/>の
    /// <c>currentBestReport</c>）であって入口盤面ではない。[3.349.2] 引数名が <c>root</c> だったため
    /// 「入口比の debt」と読めたが、実際は現在最良比＝窓はより狭い。名前を実態へ合わせた。
    /// 中間ノードの緩さは探索にしか効かず、採用は必ず <see cref="Better"/> ＋ <c>ExactPinRegression</c> が決める。
    /// </summary>
    private static bool WithinDebt(ViolationReport report, ViolationReport baseline, Config config)
    {
        if (report.Hard < baseline.Hard) return true;
        if (report.Hard > baseline.Hard + config.HardDebt) return false;
        return report.Total <= baseline.Total + config.TotalDebt;
    }

    private static List<(int A, int B)> SelectPairs(List<Candidate> candidates, int maxPairs)
    {
        if (maxPairs <= 0) return new List<(int A, int B)>();
        var pairsList = new List<(int A, int B)>();
        var pairsSeen = new HashSet<(int A, int B)>();
        void AddPair(int a, int b)
        {
            if (pairsSeen.Add((a, b))) pairsList.Add((a, b));
        }

        // [3.314.0] root ペアで予算を食い尽くさない。旧実装は (0,i) を全部入れてから `>= maxPairs` で
        //   return しており、エリートが maxPairs 件以上あると**非root ペア（elite 間・高距離・別役割）へ
        //   一度も到達しなかった**＝「EliteIntegration 改善なし」の説明になりうる。root に 2/3 を上限として
        //   割り当て、残り 1/3 は下のランキング済み非root ペアへ必ず残す。
        var rootQuota = Math.Max(1, maxPairs * 2 / 3);
        for (var i = 1; i < candidates.Count; i++)
        {
            AddPair(0, i);
            if (pairsList.Count >= rootQuota) break;
        }
        var all = new List<(int I, int J, int Score)>();
        for (var i = 1; i < candidates.Count; i++)
        for (var j = i + 1; j < candidates.Count; j++)
        {
            var roleBonus = candidates[i].Role != candidates[j].Role ? 10_000 : 0;
            var distance = AdaptiveEliteArchive.ScheduleDistance(candidates[i].Schedule, candidates[j].Schedule);
            all.Add((i, j, roleBonus + distance));
        }
        foreach (var (i, j, _) in all.OrderByDescending(t => t.Score))
        {
            AddPair(i, j);
            if (pairsList.Count >= maxPairs) break;
        }
        return pairsList;
    }

    private static List<int[]> SelectFusionGroups(List<Candidate> candidates, int maxGroups)
    {
        if (candidates.Count < 2 || maxGroups <= 0) return new List<int[]>();
        var groups = new List<int[]>();
        // [3.314.0] selectPairs と同じ理由で 2者組が枠を食い尽くさないようにする（旧: 3者融合へ到達しない）。
        var pairQuota = Math.Max(1, maxGroups * 2 / 3);
        for (var i = 1; i < candidates.Count; i++)
        {
            groups.Add(new[] { 0, i });
            if (groups.Count >= pairQuota) break;
        }
        var far = Enumerable.Range(1, candidates.Count - 1)
            .OrderByDescending(idx =>
                AdaptiveEliteArchive.ScheduleDistance(candidates[0].Schedule, candidates[idx].Schedule))
            .ToList();
        for (var a = 0; a < far.Count; a++)
        for (var b = a + 1; b < far.Count; b++)
        {
            groups.Add(new[] { 0, far[a], far[b] });
            if (groups.Count >= maxGroups) return groups;
        }
        return groups;
    }

    private static HashSet<(int I, int J)> ViolationCells(ViolationReport report)
    {
        var result = new HashSet<(int I, int J)>();
        foreach (var key in report.Violations.Keys)
        {
            var parts = key.Split(',');
            var ci = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
            var cj = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
            if (ci is int civ && cj is int cjv) result.Add((civ, cjv));
        }
        return result;
    }

    /// <summary>[賢く再構成] c1(期間要件の窓不足)が重なっているセルだけを抽出。<c>CellFamilies</c>(1セルに
    /// 重なった全違反クラスを保持、<c>Violations</c>は最重1クラスのみ)を使うため、c1がより重い違反(例: c3n)に
    /// 上書きされて<c>Violations</c>の最重クラスから消えていても取りこぼさない（3.205.0のC1Polish
    /// anchor選定と同種の穴をここでも回避）。</summary>
    internal static HashSet<(int I, int J)> C1Cells(ViolationReport report)
    {
        var result = new HashSet<(int I, int J)>();
        foreach (var (key, fams) in report.CellFamilies)
        {
            if (!fams.Contains("vio-c1")) continue;
            var parts = key.Split(',');
            var ci = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
            var cj = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
            if (ci is int civ && cj is int cjv) result.Add((civ, cjv));
        }
        return result;
    }

    private static bool Stopped(Func<bool> shouldStop, long deadlineMs) =>
        shouldStop() || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= deadlineMs;

    private static bool Better(ViolationReport a, ViolationReport b) =>
        AdaptiveEliteArchive.Better(a, b);

    private static readonly IComparer<Candidate> CandidateComparator =
        Comparer<Candidate>.Create((a, b) => AdaptiveEliteArchive.CompareReports(a.Report, b.Report));

    private static readonly IComparer<BeamNode> BeamComparator = Comparer<BeamNode>.Create((a, b) =>
    {
        var q = AdaptiveEliteArchive.CompareReports(a.Report, b.Report);
        return q != 0 ? q : a.Changed.CompareTo(b.Changed);
    });
}
