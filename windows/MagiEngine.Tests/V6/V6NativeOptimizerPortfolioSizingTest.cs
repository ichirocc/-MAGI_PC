using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5d (piece 3): <see cref="V6NativeOptimizer.PortfolioWorkerCount"/> — the pure worker-count
/// clamp <see cref="V6NativeOptimizer.RunAdaptivePortfolio"/> (next, higher-risk piece of phase 5d)
/// uses to size how many outer worker tasks it spawns. Faithful port of Kotlin's
/// <c>portfolioWorkerCount</c>.
/// </summary>
public class V6NativeOptimizerPortfolioSizingTest
{
    [Theory]
    [InlineData(8, 4, 4)]   // clamped down to cores when workers exceeds cores.
    [InlineData(1, 8, 1)]   // small worker count passes through unchanged.
    [InlineData(0, 8, 1)]   // floors at 1 even when the caller asks for 0.
    [InlineData(-5, 4, 1)]  // floors at 1 even for a negative request.
    [InlineData(8, 1, 2)]   // cores contribution floors at 2 even on a 1-core machine.
    [InlineData(3, 2, 2)]   // cores' own floor (2) still applies when it's the binding constraint.
    public void PortfolioWorkerCount_ClampsBetweenOneAndTheEffectiveCoreFloor(int w, int cores, int expected)
    {
        Assert.Equal(expected, V6NativeOptimizer.PortfolioWorkerCount(w, cores));
    }

    [Fact]
    public void PortfolioWorkerCount_DefaultCoresUsesProcessorCountAndStaysInRange()
    {
        var result = V6NativeOptimizer.PortfolioWorkerCount(8);
        Assert.InRange(result, 1, Math.Max(8, Environment.ProcessorCount));
    }
}
