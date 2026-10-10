using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>C1EjectionChainPolishTest</c> の移植。<c>gateDefaultsOff</c>・<c>allFamilyGateDefaultsOff</c> は C# に無い従来の常時フルのフラグの既定を見るので写さない。</summary>
public class C1EjectionChainPolishTest
{
    private static MagiState State(IReadOnlyList<IReadOnlyList<int>>? schedule = null) => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-03",
        shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("日勤", "D", "1", ""), new("夜勤", "N", "1", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0) }, use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: schedule ?? new List<IReadOnlyList<int>> { new List<int> { 2, 2, 2 }, new List<int> { 1, 1, 1 } },
        wishes: new Dictionary<string, int>(), staffRange: new Dictionary<string, MagiEngine.Model.Range>(),
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row> { new("3", "D", "1"), new("3", "N", "1") }, cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(), cons41: new List<C41Row>(), cons42: new List<C42Row>());

    private static int[][] Work(MagiState s) => s.Schedule.Select(r => r.ToArray()).ToArray();
    private static int[][] Copy(int[][] b) => b.Select(r => (int[])r.Clone()).ToArray();
    private static bool Same(int[][] a, int[][] b) => a.Length == b.Length && a.Zip(b).All(t => t.First.SequenceEqual(t.Second));
    private static int Bd(ViolationReport r, string f) => r.Breakdown.TryGetValue(f, out var v) ? v : 0;

    private static (MagiState, int[][]) Sept()
    {
        var st = StateJsonSerializer.Parse(FixtureLoader.ReadRaw("sept2026_state.json"));
        return (st, ScheduleUtil.NormalizeSchedule(Work(st), new Problem(st)));
    }

    [Fact]
    public void NoSingleCellMoveImprovesButChainDoes()
    {
        var st = State();
        var s0 = Work(st);
        var rep0 = UnifiedViolationChecker.Check(st, s0);
        Assert.Equal(0, rep0.Hard);
        Assert.True(Bd(rep0, "c1") > 0);
        for (var i = 0; i < 2; i++) for (var j = 0; j < 3; j++) for (var k = 0; k < 3; k++)
        {
            if (k == s0[i][j]) continue;
            var w = Copy(s0); w[i][j] = k;
            Assert.False(UnifiedViolationChecker.BetterReport(UnifiedViolationChecker.Check(st, w), rep0));
        }
        var r = C1EjectionChainPolish.Apply(st, s0);
        Assert.Equal(0, r.Report!.Hard);
        Assert.True(Bd(r.Report!, "c1") < Bd(rep0, "c1"));
        Assert.True(UnifiedViolationChecker.BetterReport(r.Report!, rep0));
    }

    /// <summary>連鎖が子評価に使う差分評価の族別内訳・必須数が、実データ上で正式評価と一致する。</summary>
    [Fact]
    public void DeltaFamiliesMatchCheckerOnRealData()
    {
        var (st, s0) = Sept();
        var p = new Problem(st);
        var work = Copy(s0);
        var de = new DeltaEvaluator(p); de.Reset(work);
        var rng = new Random(7);
        for (var n = 0; n < 300; n++)
        {
            var i = rng.Next(p.S); var j = rng.Next(p.T);
            if (!p.WishLocked(i, j)) { var ks = p.AllowedShiftsForStaff(i); var k = ks[rng.Next(ks.Length)]; work[i][j] = k; de.Apply(i, j, k); }
            if (n % 30 == 0)
            {
                var rep = UnifiedViolationChecker.Check(st, work);
                foreach (var (f, raw) in de.FamilyRaw()) Assert.True(Bd(rep, f) == raw, $"family={f}");
                var (lo, hi) = de.RangeRaw();
                Assert.Equal(Bd(rep, "low"), lo);
                Assert.Equal(Bd(rep, "high"), hi);
                Assert.Equal(rep.Hard, de.Score() / Evaluator.SCORE_HARD_UNIT);
            }
        }
    }

    /// <summary>評価上限で打ち切っても、正式評価で採った盤面か起点だけを返す（同点・悪化を返さない）。</summary>
    [Fact]
    public void EvaluationCapReturnsStartOrStrictlyBetter()
    {
        var (st, s0) = Sept();
        var rep0 = UnifiedViolationChecker.Check(st, s0);
        foreach (var cap in new[] { 1L, 500L })
        {
            var r = C1EjectionChainPolish.Apply(st, Copy(s0), new C1EjectionChainPolish.Config(MaxEvaluations: cap));
            var rep = UnifiedViolationChecker.Check(st, r.NewSchedule);
            Assert.True(rep.Hard <= rep0.Hard);
            Assert.True(Same(r.NewSchedule, s0) || UnifiedViolationChecker.BetterReport(rep, rep0));
        }
    }

    /// <summary>全族起点: 固定の評価上限で、起点か正式評価で厳密に良い盤面だけを返し、希望・手動固定・上限0を守る。同じ入力なら同じ盤面。</summary>
    [Fact]
    public void AllFamilyOriginKeepsProtectionAndIsDeterministic()
    {
        var (st, s0) = Sept();
        var p = new Problem(st);
        var rep0 = UnifiedViolationChecker.Check(st, s0);
        var cfg = new C1EjectionChainPolish.Config(Origin: C1EjectionChainPolish.Origin.ALL, MaxEvaluations: 200_000L);
        var stats = new C1EjectionChainPolish.Stats();
        var r1 = C1EjectionChainPolish.Apply(st, Copy(s0), cfg, stats: stats);
        var r2 = C1EjectionChainPolish.Apply(st, Copy(s0), cfg);
        Assert.True(Same(r1.NewSchedule, r2.NewSchedule));
        var rep = UnifiedViolationChecker.Check(st, r1.NewSchedule);
        Assert.True(rep.Hard <= rep0.Hard);
        Assert.True(Same(r1.NewSchedule, s0) || UnifiedViolationChecker.BetterReport(rep, rep0));
        for (var i = 0; i < p.S; i++) for (var j = 0; j < p.T; j++)
        {
            if (r1.NewSchedule[i][j] == s0[i][j]) continue;
            Assert.False(p.WishLocked(i, j));
            Assert.True(p.MayPlace(i, r1.NewSchedule[i][j]));
        }
        Assert.True(stats.ByFamily.Count > 1);   // c1 以外の族からも起点を出している
        Assert.Null(stats.Mismatch);
    }

    /// <summary>生成上限でも途中の盤面を返さない。</summary>
    [Fact]
    public void CandidateCapReturnsStartOrStrictlyBetter()
    {
        var (st, s0) = Sept();
        var rep0 = UnifiedViolationChecker.Check(st, s0);
        foreach (var cap in new[] { 1L, 3_000L })
        {
            var stats = new C1EjectionChainPolish.Stats();
            var r = C1EjectionChainPolish.Apply(st, Copy(s0), new C1EjectionChainPolish.Config(Origin: C1EjectionChainPolish.Origin.ALL, MaxCandidates: cap), stats: stats);
            var rep = UnifiedViolationChecker.Check(st, r.NewSchedule);
            Assert.True(Same(r.NewSchedule, s0) || UnifiedViolationChecker.BetterReport(rep, rep0));
            Assert.Equal("生成上限", stats.EndReason);
        }
    }

    /// <summary>評価上限は起点づくりの評価も含めて守る。</summary>
    [Fact]
    public void EvaluationCapIncludesSeedGeneration()
    {
        var (st, s0) = Sept();
        foreach (var cap in new[] { 1L, 5L })
        {
            var stats = new C1EjectionChainPolish.Stats();
            C1EjectionChainPolish.Apply(st, Copy(s0), new C1EjectionChainPolish.Config(Origin: C1EjectionChainPolish.Origin.ALL, MaxEvaluations: cap), stats: stats);
            Assert.True(stats.Evaluations <= cap, $"評価 {stats.Evaluations} > 上限 {cap}");
            Assert.Equal("評価上限", stats.EndReason);
        }
    }

    /// <summary>差分評価の不一致検出は名前で突き合わせ、一致なら null、ずれたら族名を返す。</summary>
    [Fact]
    public void DeltaMismatchDetectsDrift()
    {
        var (st, s0) = Sept();
        var p = new Problem(st);
        var de = new DeltaEvaluator(p); de.Reset(s0);
        Assert.Null(C1EjectionChainPolish.DeltaMismatch(de, UnifiedViolationChecker.Check(st, s0)));
        var w = Copy(s0);
        var mv = Enumerable.Range(0, p.S).SelectMany(i => Enumerable.Range(0, p.T).SelectMany(j => p.AllowedShiftsForStaff(i).Select(k => (i, j, k))))
            .First(t => !p.WishLocked(t.i, t.j) && t.k != w[t.i][t.j] && de.PreviewMove(t.i, t.j, t.k) != de.Score());
        w[mv.i][mv.j] = mv.k;   // 差分評価には反映しない＝食い違いを作る
        Assert.NotNull(C1EjectionChainPolish.DeltaMismatch(de, UnifiedViolationChecker.Check(st, w)));
    }

    /// <summary>[3.655.0] 起点が無いまま終わったら「起点なし」。</summary>
    [Fact]
    public void EndReasonSaysNoSeedsInsteadOfDone()
    {
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-02",
            shifts: new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") }, groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("s0", 0) }, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } }, groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0 } },
            wishes: new Dictionary<string, int> { ["0,0"] = 0, ["0,1"] = 0 },   // 2 日とも Y の希望＝2 日窓の X は置けない
            staffRange: new Dictionary<string, MagiEngine.Model.Range>(), needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row> { new("2", "X", "1") },
            cons2: new List<C2Row>(), cons3: new List<C3Row>(), cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var stats = new C1EjectionChainPolish.Stats();
        C1EjectionChainPolish.Apply(st, Work(st), stats: stats);
        Assert.Equal("起点なし", stats.EndReason);
        Assert.Equal(0, stats.SeedCapped);
    }

    /// <summary>[玉突きパイプライン] 収集モードは盤面を変えず、採用ゲートを通る手順とその正式評価だけを渡す。</summary>
    [Fact]
    public void CollectModeKeepsTheBoardAndReportsGatePassingPaths()
    {
        var st = State();
        var s0 = Work(st);
        var rep0 = UnifiedViolationChecker.Check(st, s0);
        var got = new List<C1EjectionChainPolish.PathCandidate>();
        var stats = new C1EjectionChainPolish.Stats();
        var r = C1EjectionChainPolish.Apply(st, s0, stats: stats, collect: c => got.Add(c));
        Assert.True(Same(r.NewSchedule, s0));
        Assert.Equal(0, r.Applied);
        Assert.NotEmpty(got);
        Assert.Equal(got.Select(c => c.Seed).ToHashSet(), stats.HitSeeds.ToHashSet());
        foreach (var c in got)
        {
            var w = Copy(s0);
            foreach (var m in c.Path) { Assert.Equal(w[m[0]][m[1]], m[2]); w[m[0]][m[1]] = m[3]; }
            var rep = UnifiedViolationChecker.Check(st, w);
            Assert.Equal(rep.WeightedScore, c.Report.WeightedScore);
            Assert.True(UnifiedViolationChecker.BetterReport(rep, rep0));
        }
    }

    /// <summary>[玉突きパイプライン] 索引は盤面を変えず、起点を初手だけの評価の良い順に返す。</summary>
    [Fact]
    public void IndexOnlyListsSeedsInFirstMoveOrder()
    {
        var st = State();
        var s0 = Work(st);
        IReadOnlyList<C1EjectionChainPolish.SeedKey> seeds = Array.Empty<C1EjectionChainPolish.SeedKey>();
        var r = C1EjectionChainPolish.Apply(st, s0, indexOnly: s => seeds = s);
        Assert.True(Same(r.NewSchedule, s0));
        Assert.NotEmpty(seeds);
        var p = new Problem(st);
        var de = new DeltaEvaluator(p); de.Reset(ScheduleUtil.NormalizeSchedule(s0, p));
        var scores = seeds.Select(s => de.PreviewMove(s.I, s.J, s.K)).ToList();
        Assert.Equal(scores.OrderBy(x => x).ToList(), scores);
    }

    /// <summary>[玉突きパイプライン] SOFT 起点は必須の族の違反を起点にしない。</summary>
    [Fact]
    public void SoftOriginSkipsHardFamilySeeds()
    {
        var st = State(new List<IReadOnlyList<int>> { new List<int> { 2, 2, 0 }, new List<int> { 1, 1, 0 } });   // 3 日目は誰もいない＝人員不足（必須）と c1
        var rep0 = UnifiedViolationChecker.Check(st, Work(st));
        Assert.True(rep0.Hard > 0);
        IReadOnlyList<C1EjectionChainPolish.SeedKey> seeds = Array.Empty<C1EjectionChainPolish.SeedKey>();
        C1EjectionChainPolish.Apply(st, Work(st), new C1EjectionChainPolish.Config(Origin: C1EjectionChainPolish.Origin.SOFT), indexOnly: s => seeds = s);
        Assert.NotEmpty(seeds);
        Assert.DoesNotContain(seeds, s => MirrorKeys.Hard.Contains(s.Family));
        IReadOnlyList<C1EjectionChainPolish.SeedKey> all = Array.Empty<C1EjectionChainPolish.SeedKey>();
        C1EjectionChainPolish.Apply(st, Work(st), new C1EjectionChainPolish.Config(Origin: C1EjectionChainPolish.Origin.ALL), indexOnly: s => all = s);
        Assert.Contains(all, s => MirrorKeys.Hard.Contains(s.Family));
    }
}
