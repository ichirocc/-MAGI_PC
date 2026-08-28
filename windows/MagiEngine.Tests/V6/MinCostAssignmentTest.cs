using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 6: <see cref="MinCostAssignment"/> (Hungarian / Kuhn–Munkres). No Kotlin-side unit test
/// exists for this file (it was previously exercised only indirectly via <c>V6HotfixPasses.kt</c>'s
/// own polish-pass tests), so this suite verifies the algorithm directly: minimum-cost correctness
/// (checked by hand against all 3!=6 permutations, not just greedy/identity assignment), the n=0 and
/// non-square edge cases, and — most importantly — the 3.278.0 crash-fix regression (an all-<see
/// cref="MinCostAssignment.Inf"/> row must return <c>null</c>, not throw/crash).
/// </summary>
public class MinCostAssignmentTest
{
    [Fact]
    public void SolveFindsTheGloballyMinimalAssignmentNotJustAGreedyOne()
    {
        // All 6 permutations of a 3x3 matrix, hand-computed:
        //  (0,1,2)=9+4+1=14 (0,2,1)=9+3+8=20 (1,0,2)=2+6+1=9  <- minimum
        //  (1,2,0)=2+3+5=10 (2,0,1)=7+6+8=21 (2,1,0)=7+4+5=16
        long[][] cost =
        {
            new long[] { 9, 2, 7 },
            new long[] { 6, 4, 3 },
            new long[] { 5, 8, 1 },
        };
        var assign = MinCostAssignment.Solve(cost);
        Assert.NotNull(assign);
        Assert.Equal(new[] { 1, 0, 2 }, assign);
        long total = 0;
        for (int i = 0; i < 3; i++) total += cost[i][assign![i]];
        Assert.Equal(9, total);
    }

    [Fact]
    public void SolvePrefersTheDiagonalWhenItIsCheapest()
    {
        long[][] cost = { new long[] { 1, 2 }, new long[] { 2, 1 } };
        Assert.Equal(new[] { 0, 1 }, MinCostAssignment.Solve(cost));
    }

    [Fact]
    public void SolveReturnsEmptyForAnEmptyMatrix()
    {
        Assert.Equal(Array.Empty<int>(), MinCostAssignment.Solve(Array.Empty<long[]>()));
    }

    [Fact]
    public void SolveRejectsANonSquareMatrix()
    {
        long[][] cost = { new long[] { 1, 2 }, new long[] { 1, 2, 3 } };
        Assert.Throws<ArgumentException>(() => MinCostAssignment.Solve(cost));
    }

    // [Kotlin 3.278.0/監査で実証されたクラッシュの修正] All-INF row (staff member cannot legally take
    // any slot that day) must degrade to "no feasible complete assignment" (null), never index p[-1].
    [Fact]
    public void SolveReturnsNullWhenARowIsEntirelyInfeasible()
    {
        long[][] cost =
        {
            new[] { MinCostAssignment.Inf, MinCostAssignment.Inf },
            new long[] { 1, 2 },
        };
        Assert.Null(MinCostAssignment.Solve(cost));
    }

    [Fact]
    public void SolveReturnsNullWhenAnyRowAmongOthersIsEntirelyInfeasible()
    {
        // Feasible rows around it don't rescue an infeasible row — the whole assignment is infeasible.
        long[][] cost =
        {
            new long[] { 1, 2, 3 },
            new[] { MinCostAssignment.Inf, MinCostAssignment.Inf, MinCostAssignment.Inf },
            new long[] { 3, 2, 1 },
        };
        Assert.Null(MinCostAssignment.Solve(cost));
    }
}
