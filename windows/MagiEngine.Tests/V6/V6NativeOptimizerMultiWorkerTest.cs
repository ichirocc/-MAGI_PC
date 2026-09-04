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
/// Cancellation semantics ([レビュー第7弾 2026-09-04]): BOTH the single-hypothesis short-circuit
/// (<c>hSpawn &lt;= 1</c>, forced here via <c>w: 1</c>) and the genuine multi-hypothesis path
/// (<c>hSpawn &gt; 1</c>) surface a cancelled token as <see cref="OperationCanceledException"/>.
/// Before that fix the single path returned whatever the bound <c>run</c> delegate returned for a
/// cancelled token (clean, non-throwing) — and <see cref="SingleHypothesisPathWithPreCancelledTokenReturnsCleanlyWithoutThrowing"/>
/// pinned that asymmetry as if it were the spec. The ViewModel branches on the exception to keep the
/// previous schedule, so a normal return after a stop would have been applied as "完了".
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
        // Uses a fake run() so the role-profile assertions are deterministic. (Historical note:
        // this test was once flaky when bound to the real RunAlnsSingle because a since-removed
        // "already-decided winner" pre-check let hypotheses that had not started yet skip run()
        // entirely once hypothesis 0 reached HARD=0. That pre-check contradicted the 全本継続 spec
        // and was removed on 2026-09-04 — see AllHypothesesRunEvenWhenHypothesisZeroReportsHardZeroImmediately.)
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

        // [CI フレーク対応] 「即座に」の具体的な数字(旧3秒)は環境のスケジューリング遅延次第で恣意的に
        // 破れる（実測4410msを観測済み）。本質は「10秒予算を律儀に使い切っていない」ことなので、
        // しきい値を予算より十分小さい・かつ環境ノイズを吸収できる値へ緩める。
        Assert.True(sw.ElapsedMilliseconds < 8_000, $"Cancellation must propagate well before the 10s budget elapses (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task SingleHypothesisPathWithPreCancelledTokenThrowsOperationCanceled()
    {
        // [レビュー第7弾 2026-09-04] 旧テスト名 SingleHypothesisPathWithPreCancelledTokenReturnsCleanlyWithoutThrowing は
        // workers=1 経路だけが停止を正常終了で返す非対称を「仕様」として固定していた。いまは両経路とも
        // 停止を OperationCanceledException で返す（ViewModel の「直前の勤務表を保持」分岐に乗せるため）。
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        V6NativeOptimizer.RunOneHypothesis run = (i, opts, prog) =>
            Task.FromResult(V6NativeOptimizer.RunAlnsSingle(state, initial.Copy2D(), opts, budgetSec: 10, onProgress: prog, cancellationToken: cts.Token));

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            V6NativeOptimizer.RunMultiWorker(w: 1, new V6OptimizerOptions(Workers: 1, Seed: 1L), onProgress: null, run, cancellationToken: cts.Token));
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 8_000, $"Pre-cancelled token must short-circuit (took {sw.ElapsedMilliseconds}ms).");
    }

    [Fact]
    public async Task AllHypothesesRunEvenWhenHypothesisZeroReportsHardZeroImmediately()
    {
        // [レビュー第7弾 2026-09-04] 仮説0が起動と同時に HARD=0 を報告しても、残りの仮説は全部起動する
        // （全本継続）。旧: 「既に勝者がいれば何もせず抜ける」事前チェックが、まだ起動していない仮説を
        // スレッドプールの都合で黙って省いていた。
        var state = MinimalState.Build();
        var initial = new Problem(state).InitialAssignment();
        var report = UnifiedViolationChecker.Check(state, initial);
        var hardZero = report with { Hard = 0 };
        const int w = 4;
        var (hSpawn, _) = V6NativeOptimizer.HypothesisSpawnPlan(new V6OptimizerOptions(Workers: w, Seed: 1L).EffectiveWorkers, w);
        Assert.True(hSpawn > 1, "This test needs the multi-hypothesis path.");
        var invoked = new ConcurrentDictionary<int, byte>();
        // 競合を**決定的に**再現する: スレッドプールのワーカーを1本だけ残して他を塞ぐ。仮説0がその1本で
        //   先に走って winner を立て、残りはそのあと同じ1本で順に起動する＝旧実装の事前チェックなら仮説1以降が
        //   run() を呼ばずに抜ける（変異検証: 事前チェックを戻すと本テストが赤）。塞がないと全本がほぼ同時に
        //   起動し、競合窓が µs で緑になってしまう（旧テストのコメントが「フレーク」と呼んでいた現象）。
        ThreadPool.GetMinThreads(out var minWorkers, out _);
        var blockers = Enumerable.Range(1, Math.Max(0, minWorkers - 1))
            .Select(_ => Task.Run(() => Thread.Sleep(1_500))).ToArray();
        await Task.Delay(50);
        V6NativeOptimizer.RunOneHypothesis run = async (i, opts, prog) =>
        {
            invoked[i] = 1;
            if (i == 0) prog("test", hardZero, 1L, 0L);
            else await Task.Delay(150);
            return new V6OptimizerResult(initial.Copy2D(), i == 0 ? hardZero : report, V6Algorithm.Alns, Array.Empty<MirrorLog>(), 1L, 0L);
        };

        var result = await V6NativeOptimizer.RunMultiWorker(w: w, new V6OptimizerOptions(Workers: w, Seed: 1L), onProgress: null, run);
        await Task.WhenAll(blockers);

        Assert.Equal(Enumerable.Range(0, hSpawn).ToHashSet(), invoked.Keys.ToHashSet());
        Assert.Contains(result.PhaseLogs, l => l.Message.Contains("合格あり(全本継続)"));
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
