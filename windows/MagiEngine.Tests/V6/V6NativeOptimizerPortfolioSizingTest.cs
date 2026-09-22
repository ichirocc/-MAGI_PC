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

    // [Kotlin原本 V6NativeOptimizerChoiceTest] 整形だけを固定（検出側は遅いロールを注入しないと踏めない）。
    [Fact]
    public void EpochOverrunLogKeepsRoleNamesAndStaysSilentWhenEmpty()
    {
        Assert.Null(V6NativeOptimizer.EpochOverrunLog(Array.Empty<string>()));
        var one = V6NativeOptimizer.EpochOverrunLog(new[] { "W4:MAX_DISTANCE_RSI_PLUS(q=45s→実412s)" })!;
        Assert.Equal("W", one.Level);
        Assert.Contains("W4:MAX_DISTANCE_RSI_PLUS(q=45s→実412s)", one.Message);
        var many = V6NativeOptimizer.EpochOverrunLog(Enumerable.Range(1, 10).Select(i => $"W{i}:ROLE(q=5s→実60s)").ToList())!;
        Assert.Contains("ほか2件", many.Message);
    }

    // 実機ログ（2026-09-22）の3回の超過は、全ロールが同じ秒数だけ超過＝プロセス凍結。ばらつくときだけ経路漏れと書く。
    [Fact]
    public void EpochOverrunLogDistinguishesProcessFreezeFromPerRoleLeak()
    {
        var freeze = V6NativeOptimizer.EpochOverrunLog(new[] {
            "W0:BASELINE_REFINE(q=35s→実8150s)", "W2:LARGE_DESTROY_ALNS(q=5s→実8138s)", "W5:MAX_DISTANCE_RSI_PLUS(q=45s→実8166s)" })!;
        Assert.Contains("プロセス全体が止まっていた", freeze.Message);
        Assert.DoesNotContain("締切を見ない経路", freeze.Message);
        var leak = V6NativeOptimizer.EpochOverrunLog(new[] {
            "W0:BASELINE_REFINE(q=35s→実40s)", "W5:MAX_DISTANCE_RSI_PLUS(q=45s→実412s)" })!;
        Assert.Contains("締切を見ない経路", leak.Message);
        var single = V6NativeOptimizer.EpochOverrunLog(new[] { "W4:MAX_DISTANCE_RSI_PLUS(q=45s→実412s)" })!;
        Assert.Contains("締切を見ない経路", single.Message);
    }
}
