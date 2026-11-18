using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>推奨の並び（複数シフト）で、一致した窓の中の 2 つ目以降のセルを書き出しと数えないこと（Kotlin <c>C3WantCoverTest.kt</c>、Android 3.727.102）。</summary>
public class C3WantCoverTest
{
    private static readonly int[] Ddrr = { 1, 1, 0, 0 };

    private static int Fires(int[] row, int[] seq) => Enumerable.Range(0, row.Length - seq.Length + 1).Count(j => C3Run.WantFires(row, j, seq));

    [Fact]
    public void completedPatternCountsNothing()
    {
        Assert.Equal(0, Fires(new[] { 1, 1, 0, 0, 2, 2 }, Ddrr));
        Assert.Equal(0, Fires(new[] { 2, 1, 1, 0, 0, 2 }, Ddrr));
    }

    [Fact]
    public void incompleteStartsStillCount()
    {
        Assert.Equal(1, Fires(new[] { 1, 0, 0, 2, 2, 2 }, Ddrr));
        Assert.Equal(1, Fires(new[] { 1, 1, 1, 0, 0, 2 }, Ddrr));
        Assert.Equal(3, Fires(new[] { 1, 1, 2, 1, 1, 2, 2 }, Ddrr));
    }

    [Fact]
    public void maskMatchesTheScalarRule()
    {
        var rng = new Random(7);
        for (int n = 0; n < 5_000; n++)
        {
            int t = 6 + rng.Next(20);
            var row = Enumerable.Range(0, t).Select(_ => rng.Next(3)).ToArray();
            long first = 0L, full = 0L;
            for (int j = 0; j <= t - Ddrr.Length; j++)
            {
                if (row[j] == Ddrr[0]) first |= 1L << j;
                if (C3Run.MatchAt(row, j, Ddrr)) full |= 1L << j;
            }
            long range = (1L << (t - Ddrr.Length + 1)) - 1;
            Assert.Equal(Fires(row, Ddrr), System.Numerics.BitOperations.PopCount((ulong)(C3Run.WantFireMask(first, full, Ddrr.Length) & range)));
        }
    }

    private static MagiState State()
    {
        var rng = new Random(3);
        return MinimalState.Build(
            startDate: "2025-01-01", endDate: "2025-01-14",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", ""), new("B", "B", "1", "") },
            groups: new List<Group> { new("G0", "G0") },
            staffList: Enumerable.Range(0, 4).Select(i => new Staff($"s{i}", 0)).ToList(),
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            schedule: Enumerable.Range(0, 4).Select(_ => (IReadOnlyList<int>)Enumerable.Range(0, 14).Select(_ => rng.Next(3)).ToList()).ToList(),
            cons3: new List<C3Row> { new(new[] { "A", "A", "B" }) },
            cons3m: new List<C3Row> { new(new[] { "A", "A", "休", "休" }) });
    }

    [Fact]
    public void checkerCountsMatchTheRule()
    {
        var st = State();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var r = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(sched.Sum(row => Fires(row, Ddrr)), r.Breakdown.GetValueOrDefault("c3m"));
        Assert.Equal(sched.Sum(row => Fires(row, new[] { 1, 1, 2 })), r.Breakdown.GetValueOrDefault("c3"));
    }

    [Fact]
    public void deltaMatchesFullAcrossRandomMoves()
    {
        var p = new Problem(State());
        var ev = new Evaluator(p);
        var de = new DeltaEvaluator(p);
        de.Reset(p.InitialAssignment());
        var rng = new Random(11);
        bool sawCover = false;
        for (int n = 0; n < 20_000; n++)
        {
            int i = rng.Next(p.S), j = rng.Next(p.T);
            int old = de.At(i, j);
            de.Apply(i, j, rng.Next(3));
            Assert.Equal(ev.FullEval(de.Snapshot()), de.Score());
            var row = de.Snapshot()[i];
            for (int s = 1; s <= row.Length - 4; s++) if (C3Run.MatchAt(row, s - 1, Ddrr) && row[s] == 1) sawCover = true;
            if (rng.Next(2) == 0) { de.Apply(i, j, old); Assert.Equal(ev.FullEval(de.Snapshot()), de.Score()); }
        }
        Assert.True(sawCover, "一致した窓の中に先頭と同じシフトが来る盤面を通っていない");
        Assert.False(C3Run.WantFires(new[] { 1, 1, 0, 0 }, 1, Ddrr));
    }
}
