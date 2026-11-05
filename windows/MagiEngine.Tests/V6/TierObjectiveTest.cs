using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>TierObjectiveTest</c> の 1 対 1 移植: 段の鍵と職員ごとの量が、チェッカーの Breakdown と同じ数え方であること。</summary>
public class TierObjectiveTest
{
    private static List<int[][]> Boards(Problem p, int[][] input)
    {
        var rng = new Random(7);
        var list = new List<int[][]> { input };
        for (var b = 0; b < 3; b++)
        {
            var x = input.Copy2D();
            for (var r = 0; r < p.S * p.T / 3; r++) x[rng.Next(p.S)][rng.Next(p.T)] = rng.Next(p.K);
            list.Add(x);
        }
        return list;
    }

    [Fact]
    public void PerStaffAmountsAddUpToTheCheckerBreakdown()
    {
        foreach (var name in CheckerSnapshotTest.Fixtures)
        {
            var st = CheckerSnapshotTest.Load(name);
            var p = new Problem(st);
            var boards = Boards(p, st.Schedule.ToIntArray2D());
            for (var b = 0; b < boards.Count; b++)
            {
                var board = boards[b];
                var rep = UnifiedViolationChecker.Check(st, board);
                foreach (var (fam, arr) in TierObjective.T1PerStaff(p, board)) Assert.True(rep.Breakdown.GetValueOrDefault(fam, 0) == arr.Sum(), $"{name} #{b} {fam}");
                Assert.True(rep.Breakdown.GetValueOrDefault("fair", 0) == TierObjective.FairDeviations(p, board).Sum(), $"{name} #{b} fair");
                var k = TierObjective.Key(p, board, rep);
                Assert.Equal(rep.Total, k.Excess.Sum());
                Assert.Equal(rep.Hard, k.Excess[0]);
                Assert.Equal(k.Dissatisfaction.OrderByDescending(v => v), k.Dissatisfaction);
                Assert.All(k.Dissatisfaction, v => Assert.True(v > 0));
            }
        }
    }

    [Fact]
    public void EveryFamilyIsInExactlyOneTier()
    {
        var all = TierObjective.Tiers.SelectMany(t => t).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(MirrorKeys.All.OrderBy(x => x, StringComparer.Ordinal), all.OrderBy(x => x, StringComparer.Ordinal));
    }
}
