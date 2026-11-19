using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>シフトの間隔（gap）: かたまりの最後の日から次のかたまりの最初の日までの日数差が N 未満なら N − 差 を数える（Kotlin <c>GapRuleTest.kt</c>、Android 3.727.109）。</summary>
public class GapRuleTest
{
    private const int D = 1;
    private const int K = 4;   // 0 休・1 D・2 A・3 B

    [Fact]
    public void deficitCountsShortGapsBetweenBlocks()
    {
        Assert.Equal(0, GapRule.Deficit(new[] { 1, 0, 0, 0, 0, 1 }, D, 5));
        Assert.Equal(1, GapRule.Deficit(new[] { 1, 0, 0, 0, 1, 0 }, D, 5));
        Assert.Equal(3, GapRule.Deficit(new[] { 1, 0, 1, 0, 0, 0 }, D, 5));
        Assert.Equal(0, GapRule.Deficit(new[] { 1, 1, 0, 0, 0, 0, 1 }, D, 5));
        Assert.Equal(1, GapRule.Deficit(new[] { 0, 1, 1, 0, 0, 0, 1 }, D, 5));
        Assert.Equal(3 + 2, GapRule.Deficit(new[] { 1, 0, 1, 0, 0, 1 }, D, 5));
        Assert.Equal(0, GapRule.Deficit(new[] { 0, 1, 1, 1, 0, 0 }, D, 5));
    }

    [Fact]
    public void violatingStartsAreTheLaterBlocks()
    {
        var output = new List<int>();
        GapRule.ViolatingStarts(new[] { 1, 0, 1, 0, 0, 0, 0, 0, 1 }, D, 5, output.Add);
        Assert.Equal(new List<int> { 2 }, output);
    }

    private static MagiState RandomState(Random rng)
    {
        const int s = 6, t = 21;
        var schedule = Enumerable.Range(0, s).Select(_ => (IReadOnlyList<int>)Enumerable.Range(0, t).Select(_ => rng.Next(K)).ToList()).ToList();
        return MinimalState.Build(
            startDate: "2026-12-01", endDate: "2026-12-21",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("D", "D", "1", "1"), new("A", "A", "1", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: Enumerable.Range(0, s).Select(i => new Staff($"s{i}", i % 2)).ToList(),
            use2Patterns: true,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 }, new List<int> { 1, 0, 1, 1 } },
            schedule: schedule,
            staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,1"] = new MagiEngine.Model.Range("", "0") },
            cons1: new List<C1Row> { new("7", "休", "2") })
            with { ConsGap = new List<GapRow> { new("D", "5"), new("休", "3"), new("D", "3") } };
    }

    [Fact]
    public void checkerAndEvaluatorAgree()
    {
        var rng = new Random(20261119);
        bool sawGap = false;
        for (int n = 0; n < 300; n++)
        {
            var st = RandomState(rng);
            var p = new Problem(st);
            var b = st.Schedule.Select(r => r.ToArray()).ToArray();
            var r = UnifiedViolationChecker.Check(st, b);
            int manual = p.ConsGap.Sum(c => Enumerable.Range(0, p.S).Sum(i => p.C1Applies(i, c.ShiftIdx) ? GapRule.Deficit(b[i], c.ShiftIdx, c.Days) : 0));
            Assert.Equal(manual, r.Breakdown.GetValueOrDefault("gap"));
            var ev = new Evaluator(p);
            long without = 0;
            foreach (var c in p.ConsGap) for (int i = 0; i < p.S; i++) if (p.C1Applies(i, c.ShiftIdx)) without += GapRule.Deficit(b[i], c.ShiftIdx, c.Days);
            Assert.Equal(manual, without);
            if (manual > 0) sawGap = true;
        }
        Assert.True(sawGap);
    }

    [Fact]
    public void upperZeroAndCannotDoAreExcluded()
    {
        var st = RandomState(new Random(1)) with
        {
            Schedule = Enumerable.Range(0, 6).Select(i => (IReadOnlyList<int>)Enumerable.Range(0, 21).Select(j => i <= 1 && j % 2 == 0 ? 1 : 0).ToList()).ToList(),
            ConsGap = new List<GapRow> { new("D", "5") },
        };
        var r = UnifiedViolationChecker.Check(st, st.Schedule.Select(x => x.ToArray()).ToArray());
        Assert.Equal(0, r.Breakdown.GetValueOrDefault("gap"));
    }

    [Fact]
    public void deltaMatchesFull()
    {
        var rng = new Random(7);
        for (int n = 0; n < 20; n++)
        {
            var p = new Problem(RandomState(rng));
            var ev = new Evaluator(p);
            var de = new DeltaEvaluator(p);
            de.Reset(RandomState(rng).Schedule.Select(x => x.ToArray()).ToArray());
            for (int m = 0; m < 3_000; m++)
            {
                int i = rng.Next(p.S), j = rng.Next(p.T);
                int old = de.At(i, j);
                de.Apply(i, j, rng.Next(K));
                Assert.Equal(ev.FullEval(de.Snapshot()), de.Score());
                if (rng.Next(2) == 0) { de.Apply(i, j, old); Assert.Equal(ev.FullEval(de.Snapshot()), de.Score()); }
            }
        }
    }

    [Fact]
    public void csvRoundTripKeepsRows()
    {
        var st = RandomState(new Random(5));
        var r = ConstraintsCsvIO.Parse(ConstraintsCsvIO.Build(st), st)!;
        Assert.Equal(st.ConsGap, r.State.ConsGap);
        Assert.Equal(0, r.Rejected);
    }

    [Fact]
    public void jsonRoundTripKeepsRows()
    {
        var st = RandomState(new Random(3));
        var back = StateJsonSerializer.Parse(StateJsonSerializer.Serialize(st, st.Schedule.Select(x => x.ToArray()).ToArray()));
        Assert.Equal(st.ConsGap, back.ConsGap);
    }

    [Fact]
    public void shiftRefCountIncludesRows() => Assert.Equal(2, Ws1Ops.ShiftRefCount(RandomState(new Random(6)), "D"));
}
