using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>正式チェッカーの並びの窓の照合: ビット経路（AND＋popcount）とスカラーの走査で、数・場所・順が同じ。</summary>
public class C3FamilyBitTest
{
    private static (ViolationReport Bit, ViolationReport Scalar) Both(MagiState st, int[][] s)
    {
        var bit = UnifiedViolationChecker.Check(st, s.Copy2D());
        UnifiedViolationChecker.ForceScalarC3 = true;
        try { return (bit, UnifiedViolationChecker.Check(st, s.Copy2D())); }
        finally { UnifiedViolationChecker.ForceScalarC3 = false; }
    }

    private static void AssertSame(string tag, ViolationReport a, ViolationReport b)
    {
        Assert.True(b.Breakdown.OrderBy(kv => kv.Key, StringComparer.Ordinal).SequenceEqual(a.Breakdown.OrderBy(kv => kv.Key, StringComparer.Ordinal)), $"{tag} breakdown");
        Assert.True(b.Violations.ToList().SequenceEqual(a.Violations.ToList()), $"{tag} violations");
        Assert.True(b.CellFamilies.Select(kv => $"{kv.Key}={string.Join(",", kv.Value)}").SequenceEqual(a.CellFamilies.Select(kv => $"{kv.Key}={string.Join(",", kv.Value)}")), $"{tag} cellFamilies");
        Assert.True(b.WeightedScore == a.WeightedScore, $"{tag} weighted");
    }

    [Fact]
    public void BitAndScalarAgreeOnFixturesAndRandomBoards()
    {
        var rng = new JavaRandom(7);
        foreach (var name in CheckerSnapshotTest.Fixtures)
        {
            var st = CheckerSnapshotTest.Load(name);
            var bas = st.Schedule.ToIntArray2D();
            var (a, b) = Both(st, bas);
            AssertSame(name, a, b);
            for (var t = 0; t < 20; t++)
            {
                var s = bas.Copy2D();
                // 盤面の 3 割をランダムに書き換える（未割当 -1 も混ぜる）。
                for (var i = 0; i < s.Length; i++)
                    for (var j = 0; j < s[i].Length; j++)
                        if (rng.NextInt(10) < 3) s[i][j] = rng.NextInt(50) == 0 ? -1 : rng.NextInt(st.ShiftCount);
                var (x, y) = Both(st, s);
                AssertSame($"{name}#{t}", x, y);
            }
        }
    }
}
