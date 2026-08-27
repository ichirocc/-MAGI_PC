using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5c gate: <see cref="V6NativeOptimizer.Hf80PostPolish"/> / <see cref="V6NativeOptimizer.SoftPolishOnly"/>.
///
/// Unlike <see cref="SaOptimizerTest"/>/<see cref="V6NativeOptimizerAlnsTest"/>, this polish pass
/// only ever accepts a move when it does not worsen HARD relative to the running best
/// (<c>ns/SCORE_HARD_UNIT &lt;= bestHard</c> guards every acceptance branch) — so, unlike SA/ALNS,
/// its final result is not merely "no worse than input" but is additionally guaranteed to never
/// have a HARD count above the input's HARD count. <c>PolishResult</c> (like its Kotlin original)
/// carries only the schedule/logs/iteration count, not a report, so these tests re-derive the
/// report from the returned schedule via the checker exactly as production code would.
/// </summary>
public class V6NativeOptimizerPolishTest
{
    private static MagiState LoadFixture(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static void AssertValidShape(Problem p, int[][] schedule)
    {
        Assert.Equal(p.S, schedule.Length);
        for (int i = 0; i < p.S; i++)
        {
            Assert.Equal(p.T, schedule[i].Length);
            for (int j = 0; j < p.T; j++)
                Assert.InRange(schedule[i][j], 0, p.K - 1);
        }
    }

    [Fact]
    public void Hf80PostPolish_NeverWorsensInputAndNeverExceedsInputHard()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var baseReport = UnifiedViolationChecker.Check(state, initial);

        var result = V6NativeOptimizer.Hf80PostPolish(state, initial, seconds: 1, seed: 42L);
        var resultReport = UnifiedViolationChecker.Check(state, result.Schedule);

        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            "Result must never be worse than input.");
        Assert.True(resultReport.Hard <= baseReport.Hard,
            $"Polish must never accept a move that raises HARD above the input's (input={baseReport.Hard}, result={resultReport.Hard}).");
        AssertValidShape(p, result.Schedule);
        Assert.Contains(result.Logs, l => l.Tag == "HF80");
        Assert.True(result.Iterations > 0, "A 1s budget on a tiny fixture should complete at least one polish iteration.");
    }

    [Fact]
    public void Hf80PostPolish_ShouldStopTrueReturnsQuicklyWithInputReportUnchanged()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var baseReport = UnifiedViolationChecker.Check(state, initial);

        var sw = Stopwatch.StartNew();
        var result = V6NativeOptimizer.Hf80PostPolish(state, initial, seconds: 10, seed: 1L, shouldStop: () => true);
        sw.Stop();
        var resultReport = UnifiedViolationChecker.Check(state, result.Schedule);

        Assert.True(sw.ElapsedMilliseconds < 3_000,
            $"ShouldStop=true must break out of the loop at its very first check (took {sw.ElapsedMilliseconds}ms).");
        Assert.Equal(baseReport.Hard, resultReport.Hard);
        Assert.Equal(baseReport.Total, resultReport.Total);
        Assert.Equal(baseReport.WeightedScore, resultReport.WeightedScore);
        Assert.Equal(0L, result.Iterations);
        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public void Hf80PostPolish_PreCancelledTokenReturnsQuicklyWithoutThrowing()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        // Matches the RunAlnsSingle/RunV5 precedent: shouldStop() and CancellationToken are unified
        // into one non-throwing poll — cancellation must short-circuit cleanly, not throw.
        var result = V6NativeOptimizer.Hf80PostPolish(state, initial, seconds: 10, seed: 1L, cancellationToken: cts.Token);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000, $"Pre-cancelled token must short-circuit (took {sw.ElapsedMilliseconds}ms).");
        AssertValidShape(p, result.Schedule);
    }

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public void Hf80PostPolish_CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var baseReport = UnifiedViolationChecker.Check(state, initial);

        var sw = Stopwatch.StartNew();
        var result = V6NativeOptimizer.Hf80PostPolish(state, initial, seconds: 1, seed: 7L);
        sw.Stop();
        var resultReport = UnifiedViolationChecker.Check(state, result.Schedule);

        Assert.True(sw.ElapsedMilliseconds < 4_000,
            $"[{fixtureName}] Hf80PostPolish should complete within a few seconds of its 1s budget (took {sw.ElapsedMilliseconds}ms).");
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            $"[{fixtureName}] Result must never be worse than input.");
        Assert.True(resultReport.Hard <= baseReport.Hard,
            $"[{fixtureName}] Polish must never raise HARD above the input's (input={baseReport.Hard}, result={resultReport.Hard}).");
        AssertValidShape(p, result.Schedule);
    }

    // ================================ SoftPolishOnly ================================

    [Fact]
    public async Task SoftPolishOnly_ReturnsAValidScheduleNoWorseThanInput()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var baseReport = UnifiedViolationChecker.Check(state, initial);

        var schedule = await V6NativeOptimizer.SoftPolishOnly(state, initial, seconds: 1, seed: 42L);

        var resultReport = UnifiedViolationChecker.Check(state, schedule);
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport));
        AssertValidShape(p, schedule);
    }

    [Fact]
    public async Task SoftPolishOnly_ClampsSecondsToAtLeastOne()
    {
        // Kotlin: `hf80PostPolish(state, schedule, max(1, seconds), ...)` — a caller-supplied 0 (or
        // negative) must not degenerate into a zero-length/never-runs budget.
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var schedule = await V6NativeOptimizer.SoftPolishOnly(state, initial, seconds: 0, seed: 1L);

        AssertValidShape(p, schedule);
    }
}
