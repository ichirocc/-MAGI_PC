using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>同種の制約が複数あるとき、DeltaEvaluator がフル評価と一致すること（Kotlin <c>SameKindManyRulesTest.kt</c>。C# の Evaluator は族ごとの内訳を返さないので内訳の照合は Kotlin だけ）。</summary>
public class SameKindManyRulesTest
{
    private const int K = 4;

    private static MagiState RandomState(Random rng)
    {
        const int s = 6, t = 14;
        var staffRange = new Dictionary<string, MagiEngine.Model.Range>();
        for (int i = 0; i < s; i++) for (int x = 1; x < K; x++)
            switch (rng.Next(4))
            {
                case 0: staffRange[$"{i},{x}"] = new MagiEngine.Model.Range("", "0"); break;
                case 1: staffRange[$"{i},{x}"] = new MagiEngine.Model.Range(rng.Next(4).ToString(), (3 + rng.Next(5)).ToString()); break;
            }
        return MinimalState.Build(
            startDate: "2026-12-01", endDate: "2026-12-14",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "2"), new("B", "B", "1", ""), new("C", "C", "", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: Enumerable.Range(0, s).Select(i => new Staff($"s{i}", i % 2, i % 2)).ToList(),
            use2Patterns: true,
            groupShift: new List<IReadOnlyList<int>> { Enumerable.Repeat(1, K).ToList(), Enumerable.Repeat(1, K).ToList() },
            groupShiftApt: new List<IReadOnlyList<string>> { new[] { "", "3", "", "" }, new[] { "", "", "2", "" } },
            schedule: Enumerable.Range(0, s).Select(_ => (IReadOnlyList<int>)Enumerable.Range(0, t).Select(_ => rng.Next(K)).ToList()).ToList(),
            staffRange: staffRange,
            cons1: new List<C1Row> { new("7", "休", "2"), new("14", "休", "4"), new("5", "休", "1"), new("14", "A", "1"), new("7", "A", "2") },
            cons2: new List<C2Row> { new("B", "2"), new("B", "3"), new("C", "1") },
            cons3: new List<C3Row> { new(new[] { "A", "A", "休" }), new(new[] { "B", "B" }) },
            cons3n: new List<C3Row> { new(new[] { "A", "B" }), new(new[] { "A", "C" }), new(new[] { "A", "B" }) },
            cons3m: new List<C3Row> { new(new[] { "A", "A", "休", "休" }), new(new[] { "休", "休" }), new(new[] { "B", "休" }) },
            cons3mn: new List<C3Row> { new(new[] { "休", "A", "休" }), new(new[] { "A", "休", "A" }) },
            cons41: new List<C41Row> { new("G0", "A", "1", "2"), new("G0", "A", "", "1"), new("G1", "B", "1", "") },
            cons42: new List<C42Row> { new("G0", "G1", "A", "B"), new("G0", "G0", "A", "A") },
            skillGroups: new List<Group> { new("K0", "K0"), new("K1", "K1") },
            cons41s: new List<C41Row> { new("K0", "B", "", "1"), new("K0", "B", "1", "") },
            cons42s: new List<C42Row> { new("K0", "K1", "C", "C"), new("K1", "K1", "A", "B") });
    }

    [Fact]
    public void deltaMatchesFullWithManyRulesOfTheSameKind()
    {
        var rng = new Random(7);
        for (int n = 0; n < 20; n++)
        {
            var p = new Problem(RandomState(rng));
            var ev = new Evaluator(p);
            var de = new DeltaEvaluator(p);
            de.Reset(p.InitialAssignment());
            for (int m = 0; m < 2_000; m++)
            {
                int i = rng.Next(p.S), j = rng.Next(p.T);
                int old = de.At(i, j);
                de.Apply(i, j, rng.Next(K));
                Assert.Equal(ev.FullEval(de.Snapshot()), de.Score());
                if (rng.Next(2) == 0) { de.Apply(i, j, old); Assert.Equal(ev.FullEval(de.Snapshot()), de.Score()); }
            }
        }
    }
}
