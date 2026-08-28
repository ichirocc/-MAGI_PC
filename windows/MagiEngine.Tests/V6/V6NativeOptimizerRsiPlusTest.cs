using System.Diagnostics;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Phase 5c gate (final piece): <see cref="V6NativeOptimizer.RunRsiPlus"/> — the RSI++ 4-phase
/// pipeline (Seed→Hypothesis→Refine→EarlyChain→Polish) composed entirely from already-ported and
/// already-tested building blocks (<see cref="V6NativeOptimizerAlnsTest"/> covers RunV5/RunAlns,
/// <see cref="V6NativeOptimizerRsiTest"/> covers RunRsi, <see cref="V6LateOperatorsTest"/> covers
/// V6LateOperators.Improve, <see cref="V6NativeOptimizerPolishTest"/> covers Hf80PostPolish).
/// This file therefore tests the GLUE unique to RunRsiPlus itself — phase-to-phase keep-best
/// promotion, budget-split floors, the shouldStop()-gated Phase2/3 skips, and the cancellation
/// flavor distinctive to this composition — rather than re-verifying each dependency's own internals.
///
/// [重要な性質・per-phase floors] Kotlin原本の budget-splitting は
/// <c>seedSec=max(10,budgetSec*0.20)</c> / <c>rsiSec=max(10,budgetSec*0.35)</c> /
/// <c>alnsSec=max(10,budgetSec*0.30)</c> / <c>polishSec=max(5,budgetSec-seedSec-rsiSec-alnsSec)</c>
/// — every phase has a hard FLOOR (10/10/10/5 seconds) regardless of how small the caller's overall
/// <c>budgetSec</c> is. Since each phase's underlying algorithm (SA/RSI/ALNS) only stops when its
/// own sub-budget elapses (or <c>shouldStop</c>/cancellation fires — there is no "converged, stop
/// early" signal for a tiny problem), an UNFORCED call with any <c>budgetSec</c> below ~35 still
/// costs ~35 real wall-clock seconds. Exactly one test below
/// (<see cref="RunRsiPlus_NeverWorsensInputAndReturnsValidSchedule"/>) accepts that cost to get
/// genuine non-zero <c>Iterations</c> coverage on the smallest possible fixture; every other test
/// uses <c>shouldStop: () =&gt; true</c> to stay fast while still exercising the real
/// Problem/Evaluator construction, Hf67HardRepair, and keep-best/skip-gating logic along the way.
///
/// [キャンセルの型・RunRsi単体との違い] <c>RunRsiPlus</c> itself never checks
/// <see cref="CancellationToken"/> directly (matching Kotlin: no <c>ensureActive()</c> call in the
/// original body at all) — cancellation surfaces purely through whichever child call first notices
/// it. Phase1 Seed (<see cref="V6NativeOptimizer.RunV5"/> → <see cref="SaOptimizer"/>, "flavor 1")
/// unifies <c>shouldStop()</c>/token into one non-throwing poll, so a pre-cancelled token does NOT
/// abort Phase1 early with an exception — it just makes Phase1's own internal loop exit almost
/// immediately, cleanly. Phase2 Hypothesis (<see cref="V6NativeOptimizer.RunRsi"/>, "flavor 3") DOES
/// throw <see cref="OperationCanceledException"/> at its very first round-boundary check. So, unlike
/// <c>RunRsi</c> tested in isolation (which throws immediately at round 0), a pre-cancelled token
/// passed to <c>RunRsiPlus</c> throws only after Phase1 has run (and returned) once — see
/// <see cref="RunRsiPlus_PreCancelledTokenThrowsAfterPhase1CompletesNormally"/>.
/// </summary>
public class V6NativeOptimizerRsiPlusTest
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

    [Fact]
    public async Task RunRsiPlus_NeverWorsensInputAndReturnsValidSchedule()
    {
        // [教訓#30に照らした注記] budgetSec=1 でも per-phase floors により実際は~35秒かかる
        // （クラス doc 参照）。ここは唯一「本物の反復が実際に起こる」ことを検証する費用として受け入れる。
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunRsiPlus(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 42L), budgetSec: 1);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 60_000,
            $"Sanity ceiling only (per-phase floors make this inherently slow) — took {sw.ElapsedMilliseconds}ms.");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.RsiPlus, result.Algorithm);
        Assert.Contains(result.PhaseLogs, l => l.Tag == "RSIPlus");
        Assert.True(result.Iterations > 0, "A 1s-nominal (≈35s real, per the floors) budget should run real search iterations.");
    }

    [Fact]
    public async Task RunRsiPlus_ShouldStopTrueReturnsQuicklyAndNeverWorsensInput()
    {
        // Unlike RunRsi's own equivalent test, we do NOT assert Iterations==0 here: Phase1's RunV5
        // does unconditional setup work (Hf67HardRepair, checker evaluation) even when shouldStop is
        // already true at t=0, and the *skip* branches for Phase2/Phase3 alias the same `seed` result
        // object into `rsi`/`refine` — so the final Iterations sum (seed+rsi+refine+polish) can
        // legitimately count `seed.Iterations` more than once. That aliasing is a faithful, literal
        // port of Kotlin's own `seed.iterations + rsi.iterations + refine.iterations + polish.iterations`
        // formula (HF77: not "fixed" here), not something whose exact value this test should pin.
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunRsiPlus(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, shouldStop: () => true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8_000,
            $"shouldStop=true must short-circuit every phase quickly (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.RsiPlus, result.Algorithm);
    }

    [Fact]
    public async Task RunRsiPlus_PreCancelledTokenThrowsAfterPhase1CompletesNormally()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            V6NativeOptimizer.RunRsiPlus(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, cancellationToken: cts.Token));
        sw.Stop();

        // Phase1 (RunV5→SaOptimizer, "flavor 1") sees the pre-cancelled token via TimeUp() and returns
        // cleanly/quickly (not a throw) — so even though the eventual throw happens one phase later
        // (in Phase2's RunRsi call), the whole thing should still complete fast because Phase1 itself
        // never gets to spend its full sub-budget either.
        Assert.True(sw.ElapsedMilliseconds < 8_000, $"Pre-cancelled token must short-circuit both phases (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task RunRsiPlus_ShouldStopTrueWithoutCancelledTokenDoesNotThrow()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        // No exception expected: shouldStop()=true is a non-throwing signal everywhere in this chain
        // (Phase1's SaOptimizer.TimeUp(), Phase2/3's own `if (shouldStop())` skip guards) — it is only
        // the *separate* CancellationToken mechanism (exercised above) that ever throws.
        var result = await V6NativeOptimizer.RunRsiPlus(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, shouldStop: () => true);

        AssertValidShape(p, result.Schedule);
    }

    [Fact]
    public async Task RunRsiPlus_SharedHf63IsAcceptedAndReusableAcrossCalls()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        var sharedHf63 = new Hf63Infeasibility();

        // [3.281.0/B, Kotlin原本] Phase2 RSI へ透過（エポック跨ぎのHF63学習持続）— two calls sharing
        // the same instance must not throw or corrupt state on reuse.
        var first = await V6NativeOptimizer.RunRsiPlus(
            state, initial, new V6OptimizerOptions(Workers: 1, Seed: 1L), budgetSec: 10, shouldStop: () => true, sharedHf63: sharedHf63);
        var second = await V6NativeOptimizer.RunRsiPlus(
            state, first.Schedule, new V6OptimizerOptions(Workers: 1, Seed: 2L), budgetSec: 10, shouldStop: () => true, sharedHf63: sharedHf63);

        AssertValidShape(p, first.Schedule);
        AssertValidShape(p, second.Schedule);
    }

    public static TheoryData<string> QualityFixtures => new() { "golden_state.json", "sample_state_v6.json" };

    [Theory]
    [MemberData(nameof(QualityFixtures))]
    public async Task RunRsiPlus_CompletesWithinBudgetAndNeverWorsensRealFixtures(string fixtureName)
    {
        var state = LoadFixture(fixtureName);
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var sw = Stopwatch.StartNew();
        var result = await V6NativeOptimizer.RunRsiPlus(state, initial, new V6OptimizerOptions(Workers: 1, Seed: 7L), budgetSec: 10, shouldStop: () => true);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8_000,
            $"[{fixtureName}] shouldStop=true must short-circuit every phase quickly even on a real-size fixture (took {sw.ElapsedMilliseconds}ms).");
        AssertNeverWorsensInput(state, p, initial, result.Report);
        AssertValidShape(p, result.Schedule);
        Assert.Equal(V6Algorithm.RsiPlus, result.Algorithm);
    }
}
