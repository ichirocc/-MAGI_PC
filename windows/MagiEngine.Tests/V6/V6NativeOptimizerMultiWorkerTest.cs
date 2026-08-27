using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5c gate: <see cref="V6NativeOptimizer.RunMultiWorker"/> — the shared multi-hypothesis
/// coordinator used by <c>RunRsi</c>/<c>RunRsiPlus</c> (not yet ported). Since this function is
/// generic over "how to run one hypothesis" (the <see cref="V6NativeOptimizer.RunOneHypothesis"/>
/// delegate), these tests bind it to the already-verified, synchronous
/// <see cref="V6NativeOptimizer.RunAlnsSingle"/> (wrapped in <see cref="Task.FromResult{TResult}"/>)
/// as a concrete, deterministic-enough "run one hypothesis" implementation — exercising the
/// coordinator's own spawn/select/log logic in isolation from any particular algorithm's internals.
///
/// Same cancellation-asymmetry family as <see cref="V6NativeOptimizerAlnsTest"/>: the single-
/// hypothesis short-circuit (<c>hSpawn &lt;= 1</c>, forced here via <c>w: 1</c>) returns whatever the
/// bound <c>run</c> delegate itself returns for cancellation (clean, non-throwing, since
/// RunAlnsSingle unifies cancellation into a non-throwing poll); the genuine multi-hypothesis path
/// (<c>hSpawn &gt; 1</c>) is an outer TPL coordinator and explicitly rethrows via
/// <c>cancellationToken.ThrowIfCancellationRequested()</c> once all hypotheses have been collected —
/// matching <see cref="V6NativeOptimizer.RunAlnsChains"/>'s precedent exactly.
/// </summary>
public class V6NativeOptimizerMultiWorkerTest
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

    private static void AssertNeverWorsensInput(MagiState state, Problem p, int[][] initial, ViolationReport resultReport)
    {
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            $"Result must never be worse than input (input hard={baseReport.Hard}/total={baseReport.Total}, " +
            $"result hard={resultReport.Hard}/total={resultReport.Total}).");
    }

    /// <summary>Binds <see cref="V6NativeOptimizer.RunOneHypothesis"/> to the already-verified
    /// synchronous <see cref="V6NativeOptimizer.RunAlnsSingle"/>, giving each hypothesis its own
    /// board copy (matching how a real caller — e.g. the not-yet-ported RunRsi — would close over
    /// <c>state</c>/<c>initial</c>/<c>budgetSec</c>).</summary>
    private static V6NativeOptimizer.RunOneHypothesis BindToAlnsSingle(MagiState state, int[][] initial, int budgetSec = 1) =>
        (i, opts, prog) => Task.FromResult(V6NativeOptimizer.RunAlnsSingle(state, initial.Copy2D(), opts, budgetSec, onProgress: prog));

    [Fact]
    public async Task NeverWorsensInputAndReturnsValidSchedule()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var result = await V6NativeOptimizer.RunMultiWorker(
            w: 3, new V6OptimizerOptions(Workers: 3, Seed: 42L), onProgress: null, BindToAlnsSingle(state, initial));

        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.True(result.Iterations > 0, "A 1s-per-hypothesis budget on a tiny fixture should complete at least one iteration.");
    }

    [Fact]
    public async Task MultiHypothesisPathLogsBothTheMultiWorkerSummaryAndTheVerificationLine()
    {
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();

        var result = await V6NativeOptimizer.RunMultiWorker(
            w: 3, new V6OptimizerOptions(Workers: 3, Seed: 42L), onProgress: null, BindToAlnsSingle(state, initial));

        var summary = Assert.Single(result.PhaseLogs, l => l.Tag == "MultiWorker");
        Assert.Contains("仮説", summary.Message);
        Assert.Contains("入口役割", summary.Message);
        var verify = Assert.Single(result.PhaseLogs, l => l.Tag == "仮説検証");
        Assert.Contains("相異なる解", verify.Message);
    }

    [Fact]
    public async Task SingleHypothesisPathSkipsTheCoordinatorLogsEntirely()
    {
        // w=1 forces HypothesisSpawnPlan to return hSpawn<=1, taking the direct pass-through branch
        // — no parallel spawn, no MultiWorker/仮説検証 log lines get appended.
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();

        var result = await V6NativeOptimizer.RunMultiWorker(
            w: 1, new V6OptimizerOptions(Workers: 1, Seed: 1L), onProgress: null, BindToAlnsSingle(state, initial));

        Assert.DoesNotContain(result.PhaseLogs, l => l.Tag == "MultiWorker");
        Assert.DoesNotContain(result.PhaseLogs, l => l.Tag == "仮説検証");
        // The single hypothesis is still whatever the bound run() delegate itself produces.
        Assert.Contains(result.PhaseLogs, l => l.Tag == "RunMAGI_ALNS");
    }

    [Fact]
    public async Task DiversifiesEachHypothesisRoleProfileAndKeepsHypothesisZeroAsTheBaseline()
    {
        // [race-avoidance] Deliberately does NOT bind run() to the real RunAlnsSingle. If it did,
        // hypothesis 0 could legitimately reach HARD=0 and set `winner` (via the coordinator's own
        // progress-callback wrapper) before later hypotheses' Task.Run bodies even start executing
        // under thread-pool contention — at which point they correctly bail via the "already-decided
        // winner" pre-check *without ever calling run()* (this is the faithfully-ported optimization
        // itself, not a bug — see RunMultiWorker's own doc comment). That race is exactly what made
        // this test flaky when it *was* bound to RunAlnsSingle. Since `winner` is only ever set from
        // inside the wrapper around the `prog` callback this test's fake run() never invokes, `winner`
        // stays -1 for the whole test, so the pre-check never triggers and every spawned hypothesis is
        // deterministically invoked regardless of scheduling order.
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();
        var seen = new ConcurrentDictionary<int, V6OptimizerOptions>();
        V6NativeOptimizer.RunOneHypothesis run = (i, opts, prog) =>
        {
            seen[i] = opts;
            var report = UnifiedViolationChecker.Check(state, initial);
            return Task.FromResult(new V6OptimizerResult(initial.Copy2D(), report, V6Algorithm.Alns, Array.Empty<MirrorLog>(), 1L, 1L));
        };

        await V6NativeOptimizer.RunMultiWorker(w: 4, new V6OptimizerOptions(Workers: 4, Seed: 1L), onProgress: null, run);

        Assert.True(seen.Count >= 2, "Multiple hypotheses must actually have been invoked by the coordinator.");
        var distinctSeeds = seen.Values.Select(o => o.Seed).Distinct().Count();
        Assert.Equal(seen.Count, distinctSeeds); // every spawned hypothesis gets a distinct seed
        // Hypothesis 0 always keeps the baseline role profile — matches RoleExploreFor/RoleAcceptFor/RoleOpSelectFor(0).
        Assert.Equal(1.0, seen[0].Explore);
        Assert.Equal(AcceptMode.Sa, seen[0].Accept);
        Assert.Equal(OpSelectMode.Roulette, seen[0].OpSelect);
    }

    [Fact]
    public async Task AllHypothesesFailingFallsBackToADirectSingleRunCall()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var options = new V6OptimizerOptions(Workers: 3, Seed: 1L);
        var (hSpawn, _) = V6NativeOptimizer.HypothesisSpawnPlan(options.EffectiveWorkers, w: 3);
        Assert.True(hSpawn > 1, "This test needs the multi-hypothesis path to actually spawn more than one hypothesis.");

        var callCount = 0;
        V6NativeOptimizer.RunOneHypothesis run = (i, opts, prog) =>
        {
            var n = Interlocked.Increment(ref callCount);
            // Every parallel-spawned hypothesis (the first hSpawn calls) fails; only a later
            // (fallback) call succeeds — exercising Kotlin's `if (results.isEmpty()) run(0, ...)`.
            if (n <= hSpawn) throw new InvalidOperationException("simulated hypothesis failure");
            return Task.FromResult(V6NativeOptimizer.RunAlnsSingle(state, initial.Copy2D(), opts, budgetSec: 1, onProgress: prog));
        };

        var result = await V6NativeOptimizer.RunMultiWorker(w: 3, options, onProgress: null, run);

        AssertValidShape(p, result.Schedule);
        Assert.True(callCount > hSpawn, "Every spawned hypothesis must fail before the direct fallback call succeeds.");
    }

    [Fact]
    public async Task PreCancelledTokenOnTheMultiHypothesisPathThrowsOperationCanceled()
    {
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Bind run() to a closure that itself observes the pre-cancelled token via RunAlnsSingle's
        // own established non-throwing contract — every spawned hypothesis returns cleanly, so the
        // coordinator's post-collection cancellationToken.ThrowIfCancellationRequested() is what
        // actually fires (matching RunAlnsChains's precedent).
        V6NativeOptimizer.RunOneHypothesis run = (i, opts, prog) =>
            Task.FromResult(V6NativeOptimizer.RunAlnsSingle(state, initial.Copy2D(), opts, budgetSec: 10, onProgress: prog, cancellationToken: cts.Token));

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            V6NativeOptimizer.RunMultiWorker(w: 3, new V6OptimizerOptions(Workers: 3, Seed: 1L), onProgress: null, run, cancellationToken: cts.Token));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000, $"Cancellation must propagate quickly, not ride out the 10s budget (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task SingleHypothesisPathWithPreCancelledTokenReturnsCleanlyWithoutThrowing()
    {
        // hSpawn<=1 bypasses the coordinator's own ThrowIfCancellationRequested() entirely — the
        // result is purely whatever the bound run() delegate returns for a cancelled token, which
        // (bound to RunAlnsSingle) is clean/non-throwing.
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        V6NativeOptimizer.RunOneHypothesis run = (i, opts, prog) =>
            Task.FromResult(V6NativeOptimizer.RunAlnsSingle(state, initial.Copy2D(), opts, budgetSec: 10, onProgress: prog, cancellationToken: cts.Token));

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunMultiWorker(w: 1, new V6OptimizerOptions(Workers: 1, Seed: 1L), onProgress: null, run, cancellationToken: cts.Token);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 3_000, $"Pre-cancelled token must short-circuit (took {sw.ElapsedMilliseconds}ms).");
        AssertValidShape(p, result.Schedule);
    }

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public async Task CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunMultiWorker(
            w: 3, new V6OptimizerOptions(Workers: 3, Seed: 7L), onProgress: null, BindToAlnsSingle(state, initial));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 6_000,
            $"[{fixtureName}] RunMultiWorker (3 hypotheses x 1s each, parallel) should complete within a few seconds (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
    }
}
