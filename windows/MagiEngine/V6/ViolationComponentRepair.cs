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
    /// <param name="GenerateFromAnchors">[Iteration 4] 起点から直接候補を作る（拒否候補に依存しない）。</param>
    public sealed record Params(int MaxK = 4, int BeamWidth = 8, int MaxEvaluations = 64, int MaxEstimates = 6_000,
        int MaxAnchors = 24, int MaxPatchesPerAnchor = 40,
        bool GenerateFromAnchors = true, int MaxGeneratedPerAnchor = 24, int MaxGenerated = 240);

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
        public int Shift { get; }
        public Anchor(bool hard, string family, int staff, int day, int shift = -1) { Hard = hard; Family = family; Staff = staff; Day = day; Shift = shift; }

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

    /// <summary>
    /// 起点の並び: HARD のセル・人数違反 → 回数違反 → SOFT のセル・人数違反（同種はキー順で決定的）。
    /// 構造的に埋められない人数不足 <paramref name="infeasible"/> は末尾＝起点の上限（MaxAnchors）を「解ける HARD」に使う。
    /// </summary>
    public static List<Anchor> Anchors(ViolationReport report, IReadOnlySet<long>? infeasible = null)
    {
        var cells = new List<Anchor>();
        foreach (var (k, cls) in report.Violations.OrderBy(e => e.Key, StringComparer.Ordinal))
            if (ParseKey(k) is var (i, j)) cells.Add(new Anchor(MirrorKeys.Hard.Contains(Fam(cls)), Fam(cls), i, j));
        var needs = new List<Anchor>();
        foreach (var (k, cls) in report.NeedViolations.OrderBy(e => e.Key, StringComparer.Ordinal))
            if (ParseKey(k) is var (sh, j)) needs.Add(new Anchor(MirrorKeys.Hard.Contains(Fam(cls)), Fam(cls), -1, j, sh));
        var counts = new List<Anchor>();
        foreach (var (k, cls) in report.CountViolations.OrderBy(e => e.Key, StringComparer.Ordinal))
            if (ParseKey(k) is var (i, sh)) counts.Add(new Anchor(false, Fam(cls), i, -1, sh));
        var cellsAndNeeds = cells.Concat(needs).ToList();
        var hardOnes = cellsAndNeeds.Where(a => a.Hard).ToList();
        bool Blocked(Anchor a) => a.Family == "covU" && infeasible != null && infeasible.Contains(a.Shift * 1000L + a.Day);
        return hardOnes.Where(a => !Blocked(a)).Concat(counts).Concat(cellsAndNeeds.Where(a => !a.Hard)).Concat(hardOnes.Where(Blocked)).ToList();
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
        if (pool.Count < 2 && !par.GenerateFromAnchors) return Done($"候補{pool.Count}件=スキップ");
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
        var poolCount = patches.Count;
        if (poolCount < 2 && !par.GenerateFromAnchors) return Done($"有効候補{poolCount}件=スキップ");

        var delta = new DeltaEvaluator(p);
        delta.Reset(work);
        // 厳密ピン（lo==hi）の (職員, シフト)。推定段階で「新たに崩す」枝を落とす（ExactPinRegression と同じ判定）。
        var pinned = new int[p.S][];
        for (var i = 0; i < p.S; i++)
            pinned[i] = Enumerable.Range(0, p.K).Where(k => p.RangeLo[i][k] != int.MinValue && p.RangeHi[i][k] != int.MaxValue && p.RangeLo[i][k] == p.RangeHi[i][k]).ToArray();
        // [Iteration 3] 構造的に埋められない人員不足の枠（担当できる人数 < 必要数）。起点の順位を下げるだけで、候補は除かない。
        IReadOnlySet<long> infeasibleSlots;
        try
        {
            infeasibleSlots = V6PortAnalyzer.DiagnoseCoverage(state, work, bestRep).Shortfalls
                .Where(sf => sf.Verdict == CoverageVerdict.Infeasible).Select(sf => sf.ShiftIndex * 1000L + sf.DayIndex).ToHashSet();
        }
        catch (Exception) { infeasibleSlots = new HashSet<long>(); }
        int estimates = 0, evaluations = 0, anchorsTried = 0, prunedPin = 0;
        var prunedLone = new HashSet<int>();   // 起点集合ごとに判定するので、同じ候補は 1 回だけ数える

        /// 候補が (職員, シフト) の回数をどれだけ動かすか（現盤面基準）。ピンを崩す候補と、それを戻せる相方の判定に使う。
        Dictionary<long, int> CountDelta(Patch pt)
        {
            var d = new Dictionary<long, int>();
            foreach (var op in pt.Ops)
            {
                var old = work[op[0]][op[1]];
                if (old == op[2]) continue;
                d[op[0] * 1000L + old] = d.GetValueOrDefault(op[0] * 1000L + old) - 1;
                d[op[0] * 1000L + op[2]] = d.GetValueOrDefault(op[0] * 1000L + op[2]) + 1;
            }
            return d;
        }
        var deltas = patches.Select(CountDelta).ToList();
        List<long> BrokenPins(int idx)
        {
            var result = new List<long>();
            foreach (var (key, dv) in deltas[idx])
            {
                var i = (int)(key / 1000L); var k = (int)(key % 1000L);
                if (Array.IndexOf(pinned[i], k) < 0) continue;
                var lo = p.RangeLo[i][k]; var bc = delta.CountForStaff(i, k);
                if (Math.Abs(bc + dv - lo) > Math.Abs(bc - lo)) result.Add(key);
            }
            return result;
        }
        // 単独で厳密ピンを崩す候補は、同じ集合に逆向きの相方（セルが重ならない）が無ければ外す＝相方が居れば束ねて戻せるので残す。
        List<int> DropLonePinBreakers(List<int> ids) => ids.Where(idx =>
        {
            var broken = BrokenPins(idx);
            var keep = broken.Count == 0 || broken.All(key =>
            {
                var sign = deltas[idx].GetValueOrDefault(key);
                return ids.Any(other => other != idx && deltas[other].GetValueOrDefault(key) * sign < 0 && !patches[other].Overlaps(patches[idx]));
            });
            if (!keep) prunedLone.Add(idx);
            return keep;
        }).ToList();
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

        // [Iteration 4] 起点から直接作る候補（半径 1）: セル違反＝そのセルの別シフトへの変更と同日 2 者交換、人数不足＝その日その
        //   シフトへの単セル変更（過剰は休へ）、回数違反＝その職員の日でシフトを足す／休へ戻す。担当可・希望固定外のみ。
        void GenerateFor(Anchor a, List<Patch> sink, HashSet<string> seenSig)
        {
            var made = 0;
            void Add(List<int[]> ops, string hint)
            {
                if (made >= par.MaxGeneratedPerAnchor) return;
                var pt = new Patch(ops, "起点生成", hint);
                if (seenSig.Add(pt.Signature)) { sink.Add(pt); made++; }
            }
            string StaffName(int i) => i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
            string Kig(int k) => k < state.Shifts.Count ? state.Shifts[k].Kigou : $"#{k}";
            void Single(int i, int j, int k2)
            {
                if (k2 < 0 || k2 >= p.K || k2 == work[i][j] || p.WishLocked(i, j) || !p.CanDo(i, k2)) return;
                Add(new List<int[]> { new[] { i, j, k2 } }, $"{StaffName(i)} {j + 1}日→{Kig(k2)}");
            }
            void Swap(int x, int y, int j)
            {
                if (x == y) return;
                var kx = work[x][j]; var ky = work[y][j];
                if (kx == ky || p.WishLocked(x, j) || p.WishLocked(y, j) || !p.CanDo(x, ky) || !p.CanDo(y, kx)) return;
                Add(new List<int[]> { new[] { x, j, ky }, new[] { y, j, kx } }, $"{StaffName(x)}↔{StaffName(y)} {j + 1}日");
            }
            if (a.Staff >= 0 && a.Day >= 0)
            {
                foreach (var k2 in p.AllowedShiftsForStaff(a.Staff)) Single(a.Staff, a.Day, k2);
                for (var b = 0; b < p.S; b++) Swap(a.Staff, b, a.Day);
            }
            else if (a.Staff < 0)
            {
                if (a.Family == "covU") for (var i = 0; i < p.S; i++) Single(i, a.Day, a.Shift);
                else if (a.Family == "covO") for (var i = 0; i < p.S; i++) if (work[i][a.Day] == a.Shift) Single(i, a.Day, p.RestIdx);
            }
            else if (a.Family.EndsWith("low", StringComparison.OrdinalIgnoreCase))
            {
                for (var j = 0; j < p.T; j++) Single(a.Staff, j, a.Shift);
            }
            else if (a.Family.EndsWith("high", StringComparison.OrdinalIgnoreCase))
            {
                for (var j = 0; j < p.T; j++) if (work[a.Staff][j] == a.Shift) Single(a.Staff, j, p.RestIdx);
            }
        }

        // 起点（違反）ごとに探索し、採用があれば盤面が変わるので起点（と起点生成の候補）を作り直す。1 周して採用が無ければ終わり。
        var used = new HashSet<int>();
        int anchorCount = 0, maxSet = 0, generatedTotal = 0;
        while (!stop() && evaluations < par.MaxEvaluations && estimates < par.MaxEstimates)
        {
            var currentAnchors = Anchors(bestRep, infeasibleSlots);
            if (patches.Count > poolCount) patches.RemoveRange(poolCount, patches.Count - poolCount);
            if (par.GenerateFromAnchors)
            {
                var sig = new HashSet<string>(patches.Select(pt => pt.Signature), StringComparer.Ordinal);
                foreach (var a in currentAnchors.Take(par.MaxAnchors)) { if (patches.Count - poolCount >= par.MaxGenerated) break; GenerateFor(a, patches, sig); }
                generatedTotal = Math.Max(generatedTotal, patches.Count - poolCount);
                deltas = patches.Select(CountDelta).ToList();
            }
            if (patches.Count < 1) break;
            var sets = AnchorSets(currentAnchors, patches, par.MaxPatchesPerAnchor)
                .Select(s => (s.Anchor, Ids: DropLonePinBreakers(s.Ids.Where(id => !used.Contains(id)).ToList())))
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
                bestRep = rep; applied++; used.UnionWith(chosen.Where(id => id < poolCount));   // 起点生成の候補は毎周作り直す
                acceptedLabels.Add(anchor.Label + ": " + string.Join("+", chosen.Select(id => string.IsNullOrWhiteSpace(patches[id].Hint) ? patches[id].Mechanism : patches[id].Hint)) + $"(k={chosen.Length})");
                committed = true;
                break;
            }
            if (!committed) break;
        }

        var mech = new List<string>(); var mechCount = new Dictionary<string, int>();
        foreach (var pt in patches.Take(poolCount)) { if (!mechCount.ContainsKey(pt.Mechanism)) mech.Add(pt.Mechanism); mechCount[pt.Mechanism] = mechCount.GetValueOrDefault(pt.Mechanism) + 1; }
        return Done(
            $"候補{poolCount}件(" + string.Join(" ", mech.Select(m => $"{m}×{mechCount[m]}")) + $")+起点生成{generatedTotal}件 起点{anchorCount}件(探索{anchorsTried}件・最大{maxSet}候補)" +
            $" 推定{estimates}回(ピン枝刈り{prunedPin}・相方なし除外{prunedLone.Count}) 正式評価{evaluations}回 採用{applied}件" +
            (acceptedLabels.Count > 0 ? "[" + string.Join(", ", acceptedLabels) + "]" : "") +
            (rejectReasons.Count > 0 ? " 不採用(" + string.Join(" ", rejectReasons.Select(r => $"{r.Key}:{r.Value}")) + ")" : "") +
            $" / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} score {(long)before.WeightedScore}->{(long)bestRep.WeightedScore}");
    }
}
