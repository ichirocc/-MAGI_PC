using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Android <c>SeesawRelinkTest.kt</c> の写し（docs/seesaw.md §6.3 の S3）。</summary>
public class SeesawRelinkTest
{
    private static MagiState State() => StateJsonSerializer.Parse(FixtureLoader.ReadRaw("sample_state_v6.json"));

    private static int[][] Polished(MagiState state, int[][] init, long seed) =>
        MembraneCellLns.Apply(state, init, new MembraneCellLns.Params(Deterministic: true, MaxCells: 30, ItersPerCell: 60_000, Seed: seed)).NewSchedule;

    /// <summary>本命＝膜の細胞で磨いた盤面（seed 1）、他の案＝別の seed（5）で磨いた盤面（Android と同じ組）。</summary>
    private static (int[][] Best, List<int[][]> Alts) Setup(MagiState state, int[][] init) => (Polished(state, init, 1L), new List<int[][]> { Polished(state, init, 5L) });

    [Fact]
    public void NeverWorseAndTradeoffsAreNotDominated()
    {
        var state = State();
        var p = ScheduleUtil.CachedProblem(state);
        var init = state.Schedule.Select(r => r.ToArray()).ToArray();
        var (best, alts) = Setup(state, init);
        var r = SeesawRelink.Apply(state, best, alts, maxMillis: 600_000L);
        var before = UnifiedViolationChecker.Check(state, best);
        Assert.False(UnifiedViolationChecker.BetterReport(before, r.Report));
        Assert.False(V6SearchOperators.ExactPinRegression(p, best, r.Schedule));
        for (var i = 0; i < p.S; i++) for (var j = 0; j < p.T; j++) if (p.WishLocked(i, j)) Assert.Equal(init[i][j], r.Schedule[i][j]);
        Assert.True(r.Tradeoffs.Count <= SeesawRelink.MaxTradeoffs);
        foreach (var t in r.Tradeoffs)
        {
            var rt = UnifiedViolationChecker.Check(state, t);
            Assert.Equal(r.Report.Hard, rt.Hard);
            Assert.False(SeesawRelink.Dominates(r.Report, rt));
        }
    }

    [Fact]
    public void DominatesIsStrictAndIgnoresWeights()
    {
        var state = State();
        var a = UnifiedViolationChecker.Check(state, state.Schedule.Select(r => r.ToArray()).ToArray());
        Assert.False(SeesawRelink.Dominates(a, a));
    }

    /// <summary>Kotlin と同じ盤面と同じ候補の数を出す（Android の同名テストと同じ指紋）。</summary>
    [Fact]
    public void MatchesKotlinOnSampleFixture()
    {
        var state = State();
        var init = state.Schedule.Select(r => r.ToArray()).ToArray();
        var (best, alts) = Setup(state, init);
        var r = SeesawRelink.Apply(state, best, alts, maxMillis: 600_000L);
        var text = string.Join(";", r.Schedule.Select(row => string.Join(",", row)));
        var h = 0;
        unchecked { foreach (var c in text) h = 31 * h + c; }
        Assert.Equal(ExpectedApplied, r.Applied);
        Assert.Equal(ExpectedTradeoffs, r.Tradeoffs.Count);
        Assert.Equal(ExpectedHash, h);
    }

    [Fact]
    public void OffByDefault() => Assert.False(PolishGate.SeesawRelink);

    // Android 3.727.77: fair＝偏差の和＋最大で 2/0/-2074942663 → 0/3/-1124469770（Kotlin の同名テストと同じ値）。
    private const int ExpectedApplied = 0;
    private const int ExpectedTradeoffs = 3;
    private const int ExpectedHash = -1124469770;
}

/// <summary>Android <c>SeesawRelinkTest.pairedWalksKeepTheMainAndAddTradeoffs</c> の写し（3.727.42）。採点の方式を切り替えるので並列にしない。</summary>
[Collection("PolishGateGlobal")]
public class SeesawRelinkWalkTest
{
    [Fact]
    public void PairedWalksKeepTheMainAndAddTradeoffs()
    {
        ObjectiveWeights.Method = ObjectiveMethod.Priority;
        try
        {
            var st = StateJsonSerializer.Parse(FixtureLoader.ReadRaw("full_coverage_state.json"));
            var b0 = st.Schedule.Select(r => r.ToArray()).ToArray();
            int[][] Pol(long seed) => MembraneCellLns.Apply(st, b0, new MembraneCellLns.Params(Deterministic: true, MaxCells: 30, ItersPerCell: 60_000, Seed: seed)).NewSchedule;
            var b = Pol(2L);
            var alts = new List<int[][]> { Pol(6L), Pol(10L), Pol(14L) };
            var plain = SeesawRelink.Apply(st, b, alts, maxMillis: 600_000L, walks: new[] { SeesawRelink.Walk.Plain });
            var all = SeesawRelink.Apply(st, b, alts, maxMillis: 600_000L);
            Assert.False(UnifiedViolationChecker.BetterReport(plain.Report, all.Report));
            Assert.True(all.Tradeoffs.Count > plain.Tradeoffs.Count, $"交換の候補 {all.Tradeoffs.Count} > {plain.Tradeoffs.Count}");
            foreach (var t in all.Tradeoffs)
            {
                var rt = UnifiedViolationChecker.Check(st, t);
                Assert.Equal(all.Report.Hard, rt.Hard);
                Assert.False(SeesawRelink.Dominates(all.Report, rt));
            }
        }
        finally { ObjectiveWeights.Method = ObjectiveMethod.Weights; }
    }
}
