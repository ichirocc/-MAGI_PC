using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// 違反起点のトランザクション修復（Kotlin <c>ViolationComponentRepair.kt</c> 3.505.0 の忠実な移植。設計はユーザー提示）。
///
/// 各研磨パスが単独では不採用にした候補（<see cref="CombinatorialRepair.Candidate"/>）を後処理チェーン全体で共有し、
/// 違反（セル・回数・人数）を起点に「その違反を触る候補（主）＋主と職員か日を共有する候補（助）」の集合を作り、
/// その中で 2〜<see cref="Params.MaxK"/> 手のトランザクションをビームで探す。途中の手は一時悪化を許し
/// （<see cref="DeltaEvaluator"/> の推定値で枝を絞り、厳密ピンを新たに崩す枝は推定段階で落とす）、commit は正式チェッカーの
/// <see cref="UnifiedViolationChecker.BetterReport"/>（HARD→weighted→total）と厳密ピン検査で決める＝推定の誤差で退化しない。
/// </summary>
public static class ViolationComponentRepair
{
    /// <summary>計測専用の品質ベクトル（採用判定には使わない。採用基準の変更は独立した A/B 項目）。</summary>
    public sealed record QualityVector(int HardCount, double HardWeighted, double SoftWeighted, int WishMisses, int ChangedCells)
        : IComparable<QualityVector>
    {
        public int CompareTo(QualityVector? other)
        {
            if (other is null) return 1;
            if (HardCount != other.HardCount) return HardCount.CompareTo(other.HardCount);
            if (HardWeighted != other.HardWeighted) return HardWeighted.CompareTo(other.HardWeighted);
            if (SoftWeighted != other.SoftWeighted) return SoftWeighted.CompareTo(other.SoftWeighted);
            if (WishMisses != other.WishMisses) return WishMisses.CompareTo(other.WishMisses);
            return ChangedCells.CompareTo(other.ChangedCells);
        }

        public static QualityVector Of(ViolationReport report, int changedCells)
        {
            double hardW = 0, softW = 0;
            foreach (var (k, v) in report.Breakdown)
            {
                var c = v * MirrorKeys.WeightOf(k);
                if (MirrorKeys.Hard.Contains(k)) hardW += c; else softW += c;
            }
            return new QualityVector(report.Hard, hardW, softW, report.Breakdown.GetValueOrDefault("pref"), changedCells);
        }
    }

    /// <param name="MaxK">1 トランザクションに束ねる候補数の上限。</param>
    /// <param name="BeamWidth">ビームの幅（推定値で残す部分トランザクションの数）。</param>
    /// <param name="MaxEvaluations">正式チェッカー呼出の上限（1 回の呼出全体）。</param>
    /// <param name="MaxEstimates">推定（DeltaEvaluator）の上限。</param>
    /// <param name="MaxAnchors">起点にする違反の上限（HARD→回数→SOFT の順）。</param>
    /// <param name="MaxPatchesPerAnchor">1 起点あたり探索に入れる候補数（主候補を先に、助候補を後に詰める）。</param>
    public sealed record Params(int MaxK = 4, int BeamWidth = 8, int MaxEvaluations = 64, int MaxEstimates = 6_000,
        int MaxAnchors = 24, int MaxPatchesPerAnchor = 40);

    /// <summary>盤面差分。Ops は [職員, 日, 新シフト] の並び（<see cref="CombinatorialRepair.Candidate.Ops"/> と同じ形）。</summary>
    public sealed class Patch
    {
        public IReadOnlyList<int[]> Ops { get; }
        public string Mechanism { get; }
        public string Hint { get; }
        public int[] Staff { get; }
        public int[] Days { get; }
        public long[] CellKeys { get; }
        public string Signature { get; }

        public Patch(IReadOnlyList<int[]> ops, string mechanism, string hint)
        {
            Ops = ops; Mechanism = mechanism; Hint = hint;
            Staff = ops.Select(o => o[0]).Distinct().OrderBy(x => x).ToArray();
            Days = ops.Select(o => o[1]).Distinct().OrderBy(x => x).ToArray();
            CellKeys = ops.Select(o => o[0] * 100_000L + o[1]).Distinct().OrderBy(x => x).ToArray();
            Signature = string.Join(";", ops.Select(o => $"{o[0]},{o[1]},{o[2]}"));
        }

        public bool Overlaps(Patch o)
        {
            int a = 0, b = 0;
            while (a < CellKeys.Length && b < o.CellKeys.Length)
            {
                var d = CellKeys[a].CompareTo(o.CellKeys[b]);
                if (d == 0) return true;
                if (d < 0) a++; else b++;
            }
            return false;
        }
    }

    /// <summary>違反の起点。セル違反は (Staff, Day)、回数違反は Staff、人数違反は Day を範囲に持つ。</summary>
    public sealed class Anchor
    {
        public bool Hard { get; }
        public string Family { get; }
        public int Staff { get; }
        public int Day { get; }
        public Anchor(bool hard, string family, int staff, int day) { Hard = hard; Family = family; Staff = staff; Day = day; }

        public bool Touches(Patch pt)
        {
            if (Staff >= 0 && Day >= 0) return Array.BinarySearch(pt.CellKeys, Staff * 100_000L + Day) >= 0;
            if (Staff >= 0) return Array.IndexOf(pt.Staff, Staff) >= 0;
            return Array.IndexOf(pt.Days, Day) >= 0;
        }

        public string Label => Family + (Staff >= 0 ? $" 職員{Staff}" : "") + (Day >= 0 ? $" {Day + 1}日" : "");
    }

    private static (int, int)? ParseKey(string key)
    {
        var c = key.IndexOf(',');
        if (c <= 0) return null;
        if (!int.TryParse(key.AsSpan(0, c), out var a) || !int.TryParse(key.AsSpan(c + 1), out var b)) return null;
        return (a, b);
    }

    private static string Fam(string cls) => cls.StartsWith("vio-", StringComparison.Ordinal) ? cls.Substring(4) : cls;

    /// <summary>起点の並び: HARD のセル・人数違反 → 回数違反 → SOFT のセル・人数違反（同種はキー順で決定的）。</summary>
    public static List<Anchor> Anchors(ViolationReport report)
    {
        var cells = new List<Anchor>();
        foreach (var (k, cls) in report.Violations.OrderBy(e => e.Key, StringComparer.Ordinal))
            if (ParseKey(k) is var (i, j)) cells.Add(new Anchor(MirrorKeys.Hard.Contains(Fam(cls)), Fam(cls), i, j));
        var needs = new List<Anchor>();
        foreach (var (k, cls) in report.NeedViolations.OrderBy(e => e.Key, StringComparer.Ordinal))
            if (ParseKey(k) is var (_, j)) needs.Add(new Anchor(MirrorKeys.Hard.Contains(Fam(cls)), Fam(cls), -1, j));
        var counts = new List<Anchor>();
        foreach (var (k, cls) in report.CountViolations.OrderBy(e => e.Key, StringComparer.Ordinal))
            if (ParseKey(k) is var (i, _)) counts.Add(new Anchor(false, Fam(cls), i, -1));
        var cellsAndNeeds = cells.Concat(needs).ToList();
        return cellsAndNeeds.Where(a => a.Hard).Concat(counts).Concat(cellsAndNeeds.Where(a => !a.Hard)).ToList();
    }

    /// <summary>起点ごとの探索集合＝主候補（起点を触る）＋助候補（主と職員か日を共有）。主候補が無い起点は除く。</summary>
    public static List<(Anchor Anchor, List<int> Ids)> AnchorSets(IReadOnlyList<Anchor> anchors, IReadOnlyList<Patch> patches, int cap)
    {
        var result = new List<(Anchor, List<int>)>();
        foreach (var a in anchors)
        {
            var primary = Enumerable.Range(0, patches.Count).Where(idx => a.Touches(patches[idx])).ToList();
            if (primary.Count == 0) continue;
            var staffSet = new HashSet<int>(); var daySet = new HashSet<int>();
            foreach (var idx in primary) { staffSet.UnionWith(patches[idx].Staff); daySet.UnionWith(patches[idx].Days); }
            var primarySet = new HashSet<int>(primary);
            var helpers = Enumerable.Range(0, patches.Count)
                .Where(idx => !primarySet.Contains(idx) && (patches[idx].Staff.Any(staffSet.Contains) || patches[idx].Days.Any(daySet.Contains)))
                .ToList();
            result.Add((a, primary.Concat(helpers).Take(cap).ToList()));
        }
        return result;
    }

    private sealed record Node(int[] Ids, long Est);

    public static V6HotfixPasses.CyclicSwapResult Repair(
        MagiState state, int[][] schedule, IReadOnlyList<CombinatorialRepair.Candidate> pool,
        Params? prm = null, Func<bool>? shouldStop = null)
    {
        var p = new Problem(state);
        var stop = shouldStop ?? (() => false);
        var par = prm ?? new Params();
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var pinBlocks = new PinBlockAttribution();
        var applied = 0;
        V6HotfixPasses.CyclicSwapResult Done(string message) => new(
            work, before.Total, bestRep.Total, applied,
            new List<MirrorLog> { new(tag: "ComponentRepair", message: "違反連結成分修復: " + message) },
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
        if (pool.Count < 2) return Done($"候補{pool.Count}件=スキップ");
        if (work.Any(row => row.Any(v => v < 0 || v >= p.K))) return Done("未割当セルあり=スキップ");

        // 候補→差分。現盤面で no-op のもの・範囲外のもの・希望固定セルを触るもの・同一差分は落とす。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var patches = new List<Patch>();
        foreach (var c in pool)
        {
            if (c.Ops.Count == 0 || c.Ops.Any(o => o.Length < 3 || o[0] < 0 || o[0] >= p.S || o[1] < 0 || o[1] >= p.T || o[2] < 0 || o[2] >= p.K)) continue;
            if (c.Ops.All(o => work[o[0]][o[1]] == o[2])) continue;
            if (c.Ops.Any(o => p.WishLocked(o[0], o[1]))) continue;
            var pt = new Patch(c.Ops, c.Mechanism, c.Hint);
            if (seen.Add(pt.Signature)) patches.Add(pt);
        }
        if (patches.Count < 2) return Done($"有効候補{patches.Count}件=スキップ");

        var delta = new DeltaEvaluator(p);
        delta.Reset(work);
        // 厳密ピン（lo==hi）の (職員, シフト)。推定段階で「新たに崩す」枝を落とす（ExactPinRegression と同じ判定）。
        var pinned = new int[p.S][];
        for (var i = 0; i < p.S; i++)
            pinned[i] = Enumerable.Range(0, p.K).Where(k => p.RangeLo[i][k] != int.MinValue && p.RangeHi[i][k] != int.MaxValue && p.RangeLo[i][k] == p.RangeHi[i][k]).ToArray();
        int estimates = 0, evaluations = 0, anchorsTried = 0, prunedPin = 0;
        var acceptedLabels = new List<string>();
        var rejectReasons = new Dictionary<string, int>();

        long Estimate(int[] ids)
        {
            estimates++;
            var undo = new List<int[]>();
            var touched = new HashSet<int>();
            var beforeCnt = new Dictionary<long, int>();
            try
            {
                foreach (var id in ids) foreach (var i in patches[id].Staff) if (touched.Add(i)) foreach (var k in pinned[i]) beforeCnt[i * 1000L + k] = delta.CountForStaff(i, k);
                foreach (var id in ids) foreach (var op in patches[id].Ops)
                {
                    var old = delta.At(op[0], op[1]);
                    if (old != op[2]) { delta.Apply(op[0], op[1], op[2]); undo.Add(new[] { op[0], op[1], old }); }
                }
                foreach (var i in touched) foreach (var k in pinned[i])
                {
                    var lo = p.RangeLo[i][k];
                    if (!beforeCnt.TryGetValue(i * 1000L + k, out var bc)) continue;
                    if (Math.Abs(delta.CountForStaff(i, k) - lo) > Math.Abs(bc - lo)) { prunedPin++; return long.MaxValue; }
                }
                return delta.Score();
            }
            finally
            {
                for (var t = undo.Count - 1; t >= 0; t--) delta.Apply(undo[t][0], undo[t][1], undo[t][2]);
            }
        }

        bool OverlapsAny(int[] ids, int j) => ids.Any(id => patches[id].Overlaps(patches[j]));

        int NodeOrder(Node a, Node b)
        {
            var c = a.Est.CompareTo(b.Est); if (c != 0) return c;
            var n = Math.Min(a.Ids.Length, b.Ids.Length);
            for (var t = 0; t < n; t++) { var d = a.Ids[t].CompareTo(b.Ids[t]); if (d != 0) return d; }
            return a.Ids.Length.CompareTo(b.Ids.Length);
        }

        (int[] Ids, ViolationReport Rep)? Search(IReadOnlyList<int> remaining)
        {
            var baseEst = delta.Score();
            var baseWork = work.Copy2D();
            var frontier = remaining.Select(id => new Node(new[] { id }, Estimate(new[] { id }))).Where(n => n.Est != long.MaxValue).ToList();
            frontier.Sort(NodeOrder); frontier = frontier.Take(par.BeamWidth).ToList();
            var depth = 1;
            while (frontier.Count > 0)
            {
                (int[] Ids, ViolationReport Rep)? best = null;
                foreach (var node in frontier)
                {
                    if (stop() || evaluations >= par.MaxEvaluations) break;
                    if (node.Est > baseEst) continue;
                    if (depth == 1 && node.Est == baseEst) continue;   // 単独候補は各パスで既に正式評価済み＝推定が改善のときだけ再評価
                    evaluations++;
                    var ops = node.Ids.SelectMany(id => patches[id].Ops).ToList();
                    var saved = new int[ops.Count];
                    for (var t = 0; t < ops.Count; t++) saved[t] = work[ops[t][0]][ops[t][1]];
                    ViolationReport rep;
                    bool pinBad;
                    try
                    {
                        foreach (var op in ops) work[op[0]][op[1]] = op[2];
                        rep = UnifiedViolationChecker.Check(state, work);
                        var improves = UnifiedViolationChecker.BetterReport(rep, bestRep);
                        pinBad = improves && V6SearchOperators.ExactPinRegression(p, baseWork, work);
                        if (pinBad) pinBlocks.Record(p, baseWork, work);
                    }
                    finally
                    {
                        for (var t = 0; t < ops.Count; t++) work[ops[t][0]][ops[t][1]] = saved[t];
                    }
                    if (!pinBad && UnifiedViolationChecker.BetterReport(rep, bestRep))
                    {
                        if (best is null || UnifiedViolationChecker.BetterReport(rep, best.Value.Rep)) best = (node.Ids, rep);
                    }
                    else
                    {
                        var why = pinBad ? "ピン破り"
                            : rep.Hard > bestRep.Hard ? "必須増"
                            : rep.WeightedScore > bestRep.WeightedScore ? "重み悪化"
                            : rep.Total > bestRep.Total ? "件数悪化" : "同値";
                        rejectReasons[why] = rejectReasons.GetValueOrDefault(why) + 1;
                    }
                }
                if (best is not null) return best;
                if (depth >= par.MaxK || stop() || evaluations >= par.MaxEvaluations || estimates >= par.MaxEstimates) return null;
                var next = new List<Node>();
                foreach (var node in frontier)
                {
                    var last = node.Ids[^1];
                    foreach (var j in remaining)
                    {
                        if (j <= last || OverlapsAny(node.Ids, j)) continue;
                        if (estimates >= par.MaxEstimates) break;
                        var ids = node.Ids.Append(j).ToArray();
                        var est = Estimate(ids);
                        if (est != long.MaxValue) next.Add(new Node(ids, est));
                    }
                }
                next.Sort(NodeOrder);
                frontier = next.Take(par.BeamWidth).ToList();
                depth++;
            }
            return null;
        }

        // 起点（違反）ごとに探索し、採用があれば盤面が変わるので起点を作り直す。1 周して採用が無ければ終わり。
        var used = new HashSet<int>();
        int anchorCount = 0, maxSet = 0;
        while (!stop() && evaluations < par.MaxEvaluations && estimates < par.MaxEstimates)
        {
            var sets = AnchorSets(Anchors(bestRep), patches, par.MaxPatchesPerAnchor)
                .Select(s => (s.Anchor, Ids: s.Ids.Where(id => !used.Contains(id)).ToList()))
                .Where(s => s.Ids.Count > 0).ToList();
            anchorCount = sets.Count;
            var committed = false;
            foreach (var (anchor, ids) in sets.Take(par.MaxAnchors))
            {
                if (stop() || evaluations >= par.MaxEvaluations || estimates >= par.MaxEstimates) break;
                anchorsTried++; maxSet = Math.Max(maxSet, ids.Count);
                var found = Search(ids);
                if (found is null) continue;
                var (chosen, rep) = found.Value;
                foreach (var id in chosen) foreach (var op in patches[id].Ops)
                    if (work[op[0]][op[1]] != op[2]) { work[op[0]][op[1]] = op[2]; delta.Apply(op[0], op[1], op[2]); }
                bestRep = rep; applied++; used.UnionWith(chosen);
                acceptedLabels.Add(anchor.Label + ": " + string.Join("+", chosen.Select(id => string.IsNullOrWhiteSpace(patches[id].Hint) ? patches[id].Mechanism : patches[id].Hint)) + $"(k={chosen.Length})");
                committed = true;
                break;
            }
            if (!committed) break;
        }

        var mech = new List<string>(); var mechCount = new Dictionary<string, int>();
        foreach (var pt in patches) { if (!mechCount.ContainsKey(pt.Mechanism)) mech.Add(pt.Mechanism); mechCount[pt.Mechanism] = mechCount.GetValueOrDefault(pt.Mechanism) + 1; }
        return Done(
            $"候補{patches.Count}件(" + string.Join(" ", mech.Select(m => $"{m}×{mechCount[m]}")) + $") 起点{anchorCount}件(探索{anchorsTried}件・最大{maxSet}候補)" +
            $" 推定{estimates}回(ピン枝刈り{prunedPin}) 正式評価{evaluations}回 採用{applied}件" +
            (acceptedLabels.Count > 0 ? "[" + string.Join(", ", acceptedLabels) + "]" : "") +
            (rejectReasons.Count > 0 ? " 不採用(" + string.Join(" ", rejectReasons.Select(r => $"{r.Key}:{r.Value}")) + ")" : "") +
            $" / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} score {(long)before.WeightedScore}->{(long)bestRep.WeightedScore}");
    }
}
