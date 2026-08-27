using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5a gate: <see cref="SaOptimizer"/> (the pure-managed SA port — the async/cancellation
/// pattern this sub-phase exists to establish, per the migration plan).
///
/// <see cref="SaOptimizer.Run"/> is wall-clock-bounded at its outermost loop (absent a
/// <see cref="SaParams.SoftPolish"/>-driven PhaseB transition, the *only* exit from
/// <c>while(!TimeUp())</c> is budget expiry), so — even at <c>Workers=1</c> with a fixed seed —
/// bit-exact schedule reproducibility across runs is not a true property of the underlying
/// algorithm (system timing jitter changes how many cooling-ladder iterations land before the
/// deadline trips). Per the plan's own stated verification criteria for this phase, these tests
/// assert: (1) never-worse-than-input, (2) valid schedule shape, (3) graceful/near-instant return
/// under cooperative cancellation (both <see cref="SaParams.ShouldStop"/> and a pre-cancelled
/// <see cref="CancellationToken"/>) with the score left exactly at the input's evaluation, and
/// (4) bounded execution + a modest quality spot-check against a couple of the real fixtures —
/// not bit-exact reproducibility.
/// </summary>
public class SaOptimizerTest
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

    // ---- (1)+(2): never-worse-than-input, valid shape, on a hand-built minimal fixture --------

    [Fact]
    public async Task NeverWorsensTheInputAndReturnsAValidSchedule()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var opt = new SaOptimizer(p, ev);

        long inputScore = ev.FullEval(p.InitialAssignment());

        var result = await opt.Run(new SaParams(BudgetMs: 300, Workers: 2, Seed: 42L));

        Assert.True(result.Score <= inputScore,
            $"SA must never return a schedule worse than its input (input={inputScore}, result={result.Score}).");
        AssertValidShape(p, result.Schedule);
        Assert.True(result.TotalIters > 0, "A 300ms budget on a tiny fixture should complete at least one iteration.");
        Assert.Equal(2, result.ChainWins.Length);
    }

    // ---- (3): cooperative cancellation returns near-instantly, score unchanged from input -----

    [Fact]
    public async Task ShouldStopTrueReturnsImmediatelyWithInputScoreUnchanged()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var opt = new SaOptimizer(p, ev);
        long inputScore = ev.FullEval(p.InitialAssignment());

        var sw = Stopwatch.StartNew();
        // Budget is deliberately large (10s) — if ShouldStop weren't honoured, this test would hang.
        var result = await opt.Run(new SaParams(BudgetMs: 10_000, ShouldStop: () => true));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2_000,
            $"ShouldStop=true must short-circuit near-instantly, not ride out the 10s budget (took {sw.ElapsedMilliseconds}ms).");
        Assert.Equal(inputScore, result.Score);
        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public async Task PreCancelledTokenReturnsImmediatelyWithInputScoreUnchanged()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var opt = new SaOptimizer(p, ev);
        long inputScore = ev.FullEval(p.InitialAssignment());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        var result = await opt.Run(new SaParams(BudgetMs: 10_000), cancellationToken: cts.Token);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2_000,
            $"A pre-cancelled token must short-circuit near-instantly (took {sw.ElapsedMilliseconds}ms).");
        Assert.Equal(inputScore, result.Score);
        AssertValidShape(p, result.Schedule);
    }

    // ---- record-validation-at-consumption-site (SaParams doc comment's documented design) -----

    [Fact]
    public async Task RunRejectsLahcLenBelowOne()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var opt = new SaOptimizer(p, ev);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => opt.Run(new SaParams(LahcLen: 0)));
        Assert.Contains("lahcLen", ex.Message);
    }

    [Fact]
    public async Task RunRejectsChainBelowOne()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var opt = new SaOptimizer(p, ev);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => opt.Run(new SaParams(Chain: 0)));
        Assert.Contains("chain", ex.Message);
    }

    // ---- (4): bounded execution + modest quality spot-check on real fixtures -------------------

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public async Task CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var opt = new SaOptimizer(p, ev);
        long inputScore = ev.FullEval(p.InitialAssignment());

        var sw = Stopwatch.StartNew();
        var result = await opt.Run(new SaParams(BudgetMs: 1_500, Workers: 2, Seed: 7L));
        sw.Stop();

        // Budget is a soft target checked at chain/flush granularity, not a hard deadline — allow
        // generous headroom (this mirrors the Kotlin original's own lack of a hard preemption guarantee).
        Assert.True(sw.ElapsedMilliseconds < 5_000,
            $"[{fixtureName}] SA run should complete within a few seconds of its 1.5s budget (took {sw.ElapsedMilliseconds}ms).");
        Assert.True(result.Score <= inputScore,
            $"[{fixtureName}] SA must never return a schedule worse than its input (input={inputScore}, result={result.Score}).");
        AssertValidShape(p, result.Schedule);
    }

    // ---- SoftPolish (PhaseB LAHC) path: same never-worse contract, exercised explicitly --------

    [Fact]
    public async Task SoftPolishPathAlsoNeverWorsensTheInput()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var opt = new SaOptimizer(p, ev);
        long inputScore = ev.FullEval(p.InitialAssignment());

        // HardStallMs=0 forces an immediate PhaseA->PhaseB transition on the very first iteration
        // that doesn't improve HARD, so this budget reliably exercises the LAHC branch even on a
        // tiny fixture.
        var result = await opt.Run(new SaParams(BudgetMs: 300, SoftPolish: true, HardStallMs: 0, Workers: 1, Seed: 99L));

        Assert.True(result.Score <= inputScore,
            $"PhaseB (LAHC) must never return a schedule worse than its input (input={inputScore}, result={result.Score}).");
        AssertValidShape(p, result.Schedule);
    }
}
