using System.Threading;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Phase 5d (piece 4): the direct prerequisites of <c>RunAdaptivePortfolio</c> itself (not yet
/// ported — the single highest-risk remaining piece of the whole migration, per the plan's explicit
/// ordering note: "5d runAdaptivePortfolio（最もリスクの高い coroutines→TPL 変換、最後に着手）").
///
/// Contains, in dependency order: <see cref="HypothesisStartFor"/> (+ its private helper
/// <see cref="ForceDiverseKick"/>) — how each of the eight parallel hypotheses obtains its initial
/// board; <see cref="ForceMaxDistanceKick"/> — the diversity-forcing perturbation used by the
/// MAX_DISTANCE_RSI_PLUS epoch role; <see cref="ElitePathRelink"/> — Path Relinking between the
/// current best and archived alternatives, used by the ELITE_RELINK epoch role; the
/// <see cref="AdaptiveWorkerOutcome"/> record and <see cref="ConfirmStop"/> — the per-worker result
/// shape and stagnation-signal debounce that <c>RunAdaptivePortfolio</c>'s epoch loop will consume;
/// and <see cref="AdaptiveEpochStart"/> — the role dispatcher that produces one epoch's starting
/// board from <see cref="HypothesisEpochAssignment.Role"/>.
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>
    /// Faithful port of Kotlin's <c>hypothesisStartFor</c>. Each of the (up to eight) parallel
    /// hypotheses starts from a structurally different destroy/repair basin (per
    /// <see cref="HypothesisDiversityPolicy.StartPlanFor"/>), except W0/W4 which keep the original
    /// board as precision/safety baselines. If the perturbation collapsed back onto the original
    /// board (can happen for small boards/light intensities), <see cref="ForceDiverseKick"/>
    /// guarantees at least one genuinely different cell.
    /// </summary>
    internal static int[][] HypothesisStartFor(MagiState state, int[][] baseSched, int index, long seed)
    {
        var outSched = baseSched.Copy2D();
        var plan = HypothesisDiversityPolicy.StartPlanFor(index);
        if (plan.Mode == HypothesisStartMode.Baseline) return outSched;
        var p = ScheduleUtil.CachedProblem(state);
        var rng = new JavaRandom(ActualSeed(seed) ^ 0xD1A5EEDL ^ ((long)index * -0x61c8864680b583ebL));
        for (var n = 0; n < plan.Intensity; n++)
        {
            switch (plan.Mode)
            {
                case HypothesisStartMode.DayRepair:
                    if (p.T > 0) DestroyRepairDayAt(state, outSched, rng.NextInt(p.T), rng);
                    break;
                case HypothesisStartMode.StaffRepair:
                    if (p.S > 0) DestroyRepairStaffAt(state, outSched, rng.NextInt(p.S), rng);
                    break;
                case HypothesisStartMode.MixedRepair:
                    if (p.T > 0) DestroyRepairDayAt(state, outSched, rng.NextInt(p.T), rng);
                    if (p.S > 0) DestroyRepairStaffAt(state, outSched, rng.NextInt(p.S), rng);
                    break;
                case HypothesisStartMode.Baseline:
                    break;
            }
        }
        if (AdaptiveEliteArchive.ScheduleDistance(baseSched, outSched) == 0)
            ForceDiverseKick(p, outSched, rng, Math.Max(1, plan.Intensity));
        return outSched;
    }

    /// <summary>
    /// Faithful port of Kotlin's private <c>forceDiverseKick</c>. Cheap fallback for when the
    /// destroy/repair basin collapsed onto the original board: pick up to <paramref name="target"/>
    /// distinct, unlocked (non-wish-locked) cells at random and move each to a different allowed
    /// shift. <c>touched</c> deduplicates cell picks across the bounded number of attempts (does
    /// NOT need to survive across calls — a fresh set per invocation, exactly like Kotlin's local
    /// <c>HashSet</c>).
    ///
    /// [C#移植上の判断・可視性] Kotlin 原本は <c>private fun</c>。<c>RunRsi</c>/<c>RunRsiPlus</c> で
    /// 確立済みの前例（<c>InternalsVisibleTo("MagiEngine.Tests")</c> 経由の直接単体テスト）にならい、
    /// wish-locked セルを一切触れないこと・許容シフトが無いセルをスキップすること・タイブレークが
    /// reservoir sampling に従うこと、という非自明な振る舞いを直接検証するため <c>internal</c> へ
    /// 格上げする（無条件に全 private を昇格するわけではなく、単純な計算専用ヘルパー——例えば
    /// <c>BestStaffForCoverage</c>/<c>CoverageShortageCost</c>——は private のまま据え置く）。
    /// </summary>
    internal static void ForceDiverseKick(Problem p, int[][] outSched, JavaRandom rng, int target)
    {
        if (p.S == 0 || p.T == 0) return;
        var touched = new HashSet<long>();
        var changed = 0;
        var attempts = 0;
        var maxAttempts = Math.Max(32, p.S * p.T * 4);
        while (changed < target && attempts++ < maxAttempts)
        {
            var i = rng.NextInt(p.S);
            var j = rng.NextInt(p.T);
            var key = (long)i * Math.Max(1, p.T) + j;
            if (!touched.Add(key) || p.WishLocked(i, j)) continue;
            var old = outSched[i][j];
            var alternatives = p.AllowedShiftsForStaff(i).Where(k => k != old).ToArray();
            if (alternatives.Length == 0) continue;
            outSched[i][j] = alternatives[rng.NextInt(alternatives.Length)];
            changed++;
        }
    }

    /// <summary>
    /// Faithful port of Kotlin's private <c>forceMaxDistanceKick</c>. Like
    /// <see cref="ForceDiverseKick"/>, but the replacement shift for each chosen cell is picked to
    /// be the LEAST-frequent choice among the current peer boards at that same (i, j) — actively
    /// steering this board away from where the other parallel workers currently sit, rather than
    /// merely picking any different shift. Ties are broken by reservoir sampling
    /// (<see cref="HypothesisDiversityPolicy.TakeReservoirTie"/>) so every tied candidate has equal
    /// probability, exactly like Kotlin's <c>bits - val + (bound - 1)</c>-free reservoir idiom used
    /// elsewhere in this port.
    ///
    /// [C#移植上の判断・可視性] <see cref="ForceDiverseKick"/> と同じ理由で <c>internal</c> へ格上げ
    /// （タイブレークの reservoir sampling・least-frequent選択という非自明な振る舞いを直接検証するため）。
    /// </summary>
    internal static void ForceMaxDistanceKick(Problem p, int[][] outSched, int[][][] peers, JavaRandom rng, int target)
    {
        if (p.S == 0 || p.T == 0) return;
        var changed = 0;
        var attempts = 0;
        var touched = new HashSet<long>();
        while (changed < target && attempts++ < Math.Max(64, p.S * p.T * 6))
        {
            var i = rng.NextInt(p.S);
            var j = rng.NextInt(p.T);
            var key = (long)i * Math.Max(1, p.T) + j;
            if (!touched.Add(key) || p.WishLocked(i, j)) continue;
            var old = outSched[i][j];
            var allowed = p.AllowedShiftsForStaff(i).Where(k => k != old).ToArray();
            if (allowed.Length == 0) continue;
            var bestK = -1;
            var bestFreq = int.MaxValue;
            var tied = 0;
            foreach (var k in allowed)
            {
                var freq = peers.Count(peer =>
                {
                    if (i >= peer.Length) return false;
                    var row = peer[i];
                    return j < row.Length && row[j] == k;
                });
                if (freq < bestFreq) { bestFreq = freq; bestK = k; tied = 1; }
                else if (freq == bestFreq)
                {
                    tied++;
                    if (HypothesisDiversityPolicy.TakeReservoirTie(tied, rng)) bestK = k;
                }
            }
            if (bestK >= 0) { outSched[i][j] = bestK; changed++; }
        }
    }

    /// <summary>
    /// Faithful port of Kotlin's public <c>elitePathRelink</c> (Glover, Laguna &amp; Martí 2000 /
    /// Scatter Search). Force-marches <paramref name="best"/> toward each of
    /// <paramref name="alternatives"/> one differing cell at a time (violation cells first, so the
    /// highest-impact recombinations are tried earliest), keeping the best intermediate board seen
    /// along any of the marches. Always evaluates from the current-best origin for each alternative
    /// (never chains one march's endpoint into the next), so this can never regress below
    /// <paramref name="best"/>'s own report.
    /// </summary>
    public static (int[][] Schedule, ViolationReport Report) ElitePathRelink(
        MagiState state,
        int[][] best,
        IReadOnlyList<int[][]> alternatives,
        Func<bool> shouldStop)
    {
        var bestSched = best.Copy2D();
        var bestRep = UnifiedViolationChecker.Check(state, bestSched);
        if (alternatives.Count == 0) return (bestSched, bestRep);
        foreach (var alt in alternatives)
        {
            if (shouldStop()) break;
            var cur = bestSched.Copy2D(); // always re-march from the current best — no regression.
            var curRep = UnifiedViolationChecker.Check(state, cur);
            var diffs = new List<(int I, int J)>();
            for (var i = 0; i < cur.Length; i++)
            {
                for (var j = 0; j < cur[i].Length; j++)
                {
                    if (i < alt.Length && j < alt[i].Length && cur[i][j] != alt[i][j]) diffs.Add((i, j));
                }
            }
            if (diffs.Count == 0) continue;
            // Move violation cells first (highest-impact recombinations up front).
            var vcells = new HashSet<(int I, int J)>();
            foreach (var vkey in curRep.Violations.Keys)
            {
                var parts = vkey.Split(',');
                var ci = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
                var cj = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
                if (ci is int civ && cj is int cjv) vcells.Add((civ, cjv));
            }
            // Enumerable.OrderBy is a documented stable sort, matching Kotlin's sortBy exactly
            // (List<T>.Sort is NOT guaranteed stable and would be the wrong translation here).
            diffs = diffs.OrderBy(d => vcells.Contains(d) ? 0 : 1).ToList();
            foreach (var (i, j) in diffs)
            {
                if (shouldStop()) break;
                cur[i][j] = alt[i][j]; // forced march toward alt
                curRep = UnifiedViolationChecker.Check(state, cur);
                if (UnifiedViolationChecker.BetterReport(curRep, bestRep)) { bestSched = cur.Copy2D(); bestRep = curRep; }
            }
        }
        return (bestSched, bestRep);
    }

    /// <summary>
    /// [3.346.1] The window over which a stagnation signal must stay true before
    /// <see cref="ConfirmStop"/> treats it as real (see that method's doc comment for the full
    /// rationale — a real-machine log showed 4/8 workers exiting permanently at the exact moment
    /// they happened to poll a signal that flipped back false 3 seconds later).
    /// </summary>
    internal const long StopConfirmMs = 5_000L;

    private const long StopConfirmPollMs = 250L;

    /// <summary>
    /// Faithful port of Kotlin's private <c>AdaptiveWorkerOutcome</c> data class — the per-worker
    /// result <c>RunAdaptivePortfolio</c>'s epoch loop (not yet ported) accumulates into and returns
    /// from each parallel worker task.
    ///
    /// [C#移植上の判断] この record は Kotlin 原本と同じく <c>private</c> かつ単一の構築箇所
    /// （<c>RunAdaptivePortfolio</c> 内、未移植）からのみ生成される想定のため、
    /// <see cref="ViolationReport"/> が要した「公開 record・複数呼出元・テストからの直接構築」を
    /// 前提にした nullable-positional-parameter-with-init-override パターンをそのまま踏襲しつつも、
    /// 後続の <c>SurvivedStops</c>/<c>Hf63Avoided</c>/<c>EpochOverruns</c>（Kotlin 側で既定値を持つ
    /// 末尾3フィールド）についてのみ同型の対応を用意する（Kotlin の各構築サイトが必ずしも全フィールドを
    /// 指定しないという原本の設計をそのまま保つため）。
    /// </summary>
    private sealed record AdaptiveWorkerOutcome(
        int[][] Elite,
        ViolationReport Report,
        IReadOnlyList<MirrorLog> Logs,
        long Iterations,
        int Epochs,
        int Reassignments,
        IReadOnlyDictionary<HypothesisEpochRole, int> RoleRuns,
        // [3.307.0/ログ強化 移植元] 役割ごとの実消費ミリ秒。量子(5/8/35/45秒)は要求値であって消費値
        // ではないため、予算配分を論じるにはこちらが要る。
        IReadOnlyDictionary<HypothesisEpochRole, long> RoleMillis,
        // [3.306.0 移植元] ワーカーが epoch ループを抜けた時点の役割。エリート登録の分類に使う
        // （再配属回数からの逆算では、残差ベース経路のとき実際の役割と一致しないため）。
        HypothesisEpochRole LastRole,
        // [3.346.0/実機ログ 移植元] ワーカーが epoch ループを抜けた理由と、その時点の経過秒。
        string ExitReason,
        long ExitAtSec,
        int SurvivedStops = 0,
        IReadOnlyList<string>? Hf63Avoided = null,
        IReadOnlyList<string>? EpochOverruns = null)
    {
        public IReadOnlyList<string> Hf63Avoided { get; init; } = Hf63Avoided ?? Array.Empty<string>();
        public IReadOnlyList<string> EpochOverruns { get; init; } = EpochOverruns ?? Array.Empty<string>();
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>confirmStop</c> ([3.346.1/方針B]).
    ///
    /// <paramref name="shouldStop"/> is NOT monotonic: deadline/cancellation stay true forever once
    /// tripped, but a stagnation signal ("elapsed since last improvement &gt; threshold") flips back
    /// false the instant ANY other worker reports an improvement. The old implementation checked
    /// this directly in the epoch loop's <c>while</c> condition, so whichever worker happened to be
    /// polling at that exact instant exited permanently — a real-machine log from 2026-08-03 showed
    /// 4 of 8 workers exiting at 115-116s and the remaining 4 finishing the run at half the intended
    /// parallelism (threshold 37.5s vs. an actual improvement cadence of 37-41s: a near-miss almost
    /// every time).
    ///
    /// This re-confirms the signal over a short window: if it flips back false partway through,
    /// returns <c>false</c> (a transient blip — keep running); if it stays true for the whole
    /// window, returns <c>true</c> (a genuine stall — exit as before). <see cref="StopConfirmMs"/>
    /// is sized so that a near-miss firing gets one more chance at the next improvement (observed
    /// real-machine cadence: next improvement ~3s after a near-miss firing). A genuine stall is
    /// delayed by at most this window; the wait is a suspended await (no CPU spend), and all workers
    /// wait it out in parallel, so the added wall-clock cost is at most one window, once.
    ///
    /// [C#移植上の判断・CancellationToken] Kotlin 原本はアンビエントな
    /// <c>coroutineContext.isActive</c> のみに依拠し、明示的なキャンセルトークン引数を一切持たない。
    /// この移植の他の全 suspend 関数は一貫して明示的な <see cref="CancellationToken"/> 引数を
    /// 使ってきたため（アンビエントな依存は導入していない）、ここでも同じ規約に揃える:
    /// <c>!coroutineContext.isActive</c> → <c>cancellationToken.IsCancellationRequested</c>、
    /// <c>delay(...)</c> を包む <c>try/catch (CancellationException) { return true }</c> →
    /// <c>await Task.Delay(...)</c> を包む <c>try/catch (OperationCanceledException) { return true; }</c>
    /// （<c>Task.Delay</c> がスローするのは <c>TaskCanceledException</c>――
    /// <c>OperationCanceledException</c> の派生型のため、基底クラスで捕捉する）。これは Kotlin 原本
    /// 自身が既に採る設計（<c>confirmStop</c> 内でキャンセル例外を捕捉し戻り値へ変換する。呼出元へは
    /// 伝播させない）をそのまま踏襲するだけであり、新しい方針の導入ではない。
    ///
    /// <paramref name="deadline"/> must use the same clock as <see cref="NowMs"/> (a monotonic
    /// <c>System.nanoTime()</c>-style clock) — mixing in wall-clock time would change the origin and
    /// break deadline detection, exactly as the Kotlin doc comment warns.
    /// </summary>
    internal static async Task<bool> ConfirmStop(
        Func<bool> shouldStop,
        long deadline,
        Func<bool>? stopIsFinal = null,
        CancellationToken cancellationToken = default)
    {
        stopIsFinal ??= () => true;
        if (stopIsFinal()) return true;
        var until = NowMs() + StopConfirmMs;
        while (NowMs() < until)
        {
            if (NowMs() >= deadline || stopIsFinal()) return true;
            if (cancellationToken.IsCancellationRequested) return true;
            try
            {
                await Task.Delay((int)StopConfirmPollMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is monotonic -> genuine. Mirror the old behaviour (shouldStop true
                // exits the loop normally) so accumulated elites are still returned as a result.
                return true;
            }
            if (!shouldStop()) return false;
        }
        return true;
    }

    /// <summary>
    /// Faithful port of Kotlin's private <c>adaptiveEpochStart</c>. Dispatches on
    /// <paramref name="assignment"/>.Role to produce one epoch's starting board — the material each
    /// role then searches from for the rest of its quantum (searching itself happens inside
    /// <c>RunAdaptivePortfolio</c>, not this dispatcher).
    ///
    /// [C#移植上の判断・可視性] Kotlin 原本は <c>private fun</c>。role→挙動の配線そのもの（例:
    /// EliteRelink が実際に <see cref="ElitePathRelink"/> を呼び、再結合が動かなければ
    /// <see cref="HypothesisStartFor"/> へフォールバックすること、HardFamilyRsi/PersonalRsi の
    /// focus優先順位）は、これから移植する最大リスク箇所 <c>RunAdaptivePortfolio</c>（並行処理）に
    /// 埋め込まれる前に単独で検証しておく価値が高いため、<see cref="ForceDiverseKick"/> と同じ理由で
    /// <c>internal</c> へ格上げする。
    /// </summary>
    internal static int[][] AdaptiveEpochStart(
        MagiState state,
        int[][] globalBest,
        int[][] localTrajectory,
        int[][][] peers,
        HypothesisEpochAssignment assignment,
        long seed,
        Func<bool> shouldStop)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var rng = new JavaRandom(seed);
        var n = Math.Max(1, assignment.Intensity);
        switch (assignment.Role)
        {
            case HypothesisEpochRole.BaselineRefine:
                return localTrajectory.Copy2D();

            case HypothesisEpochRole.EliteRelink:
            {
                var alternatives = peers
                    .Where(peer => AdaptiveEliteArchive.ScheduleDistance(globalBest, peer) > 0)
                    .OrderByDescending(peer => AdaptiveEliteArchive.ScheduleDistance(globalBest, peer))
                    .Take(3)
                    .Select(peer => peer.Copy2D())
                    .ToList();
                var (relinked, _) = ElitePathRelink(state, globalBest, alternatives, shouldStop);
                return AdaptiveEliteArchive.ScheduleDistance(globalBest, relinked) > 0
                    ? relinked
                    : HypothesisStartFor(state, globalBest, 7, seed);
            }

            case HypothesisEpochRole.DayBlockAlns:
            {
                var outSched = globalBest.Copy2D();
                if (p.T > 0)
                {
                    var first = rng.NextInt(p.T);
                    for (var x = 0; x < n * 2; x++) DestroyRepairDayAt(state, outSched, (first + x) % p.T, rng);
                }
                return outSched;
            }

            case HypothesisEpochRole.HardFamilyRsi:
            {
                var outSched = globalBest.Copy2D();
                for (var r = 0; r < n; r++)
                {
                    var rep = UnifiedViolationChecker.Check(state, outSched);
                    var focus = rep.Breakdown.GetValueOrDefault("covU", 0) > 0 ? "covU"
                        : rep.Breakdown.GetValueOrDefault("c3n", 0) > 0 ? "c3n"
                        : MaxViolatedFamily(rep);
                    outSched = RsiGenerateHypothesis(state, outSched, rep, focus, rng);
                }
                return outSched;
            }

            case HypothesisEpochRole.HardDebtRsiPlus:
            {
                var outSched = globalBest.Copy2D();
                ForceDiverseKick(p, outSched, rng, 2 + n);
                return outSched;
            }

            case HypothesisEpochRole.LargeDestroyAlns:
            {
                var outSched = globalBest.Copy2D();
                for (var r = 0; r < n * 2; r++)
                {
                    if (p.T > 0) DestroyRepairDayAt(state, outSched, rng.NextInt(p.T), rng);
                    if (p.S > 0) DestroyRepairStaffAt(state, outSched, rng.NextInt(p.S), rng);
                }
                return outSched;
            }

            case HypothesisEpochRole.PersonalRsi:
            {
                var outSched = globalBest.Copy2D();
                for (var r = 0; r < n; r++)
                {
                    var rep = UnifiedViolationChecker.Check(state, outSched);
                    var focus = rep.Breakdown.GetValueOrDefault("apt", 0) > 0 ? "apt"
                        : rep.Breakdown.GetValueOrDefault("high", 0) > 0 ? "high"
                        : rep.Breakdown.GetValueOrDefault("low", 0) > 0 ? "low"
                        : rep.Breakdown.GetValueOrDefault("fair", 0) > 0 ? "fair"
                        : "total";
                    outSched = RsiGenerateHypothesis(state, outSched, rep, focus, rng);
                }
                return outSched;
            }

            case HypothesisEpochRole.MaxDistanceRsiPlus:
            {
                var outSched = globalBest.Copy2D();
                ForceMaxDistanceKick(p, outSched, peers, rng, 3 + n);
                return outSched;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(assignment), assignment.Role, "unknown HypothesisEpochRole");
        }
    }

    /// <summary>
    /// Faithful port of Kotlin's private <c>runAdaptivePortfolio</c> — the highest-risk piece of this
    /// migration (per the plan's explicit ordering note), the async W0..W7 island-model coordinator
    /// underlying the PORTFOLIO algorithm. Each of <see cref="PortfolioWorkerCount"/> workers runs an
    /// independent sequence of epochs, each epoch picking a role (via
    /// <see cref="AdaptiveHypothesisEpochPolicy.AssignmentFor"/>), building a starting board (via
    /// <see cref="AdaptiveEpochStart"/>), then searching from it for that epoch's quantum using
    /// whichever of <see cref="RunAlns"/>/<see cref="RunRsi"/>/<see cref="RunRsiPlus"/> the role's
    /// algorithm dictates. Improvements flow both into the worker's own elite trajectory and (via a
    /// shared lock) into the cross-worker <c>globalBest</c>; every intermediate board is also
    /// registered into <see cref="AdaptiveEliteArchive"/> for later path-relinking/fusion.
    ///
    /// [C#移植上の判断・coroutines→TPL] Kotlin 原本は <c>supervisorScope</c> ＋
    /// <c>async(Dispatchers.Default)</c>（各ワーカー = 1 コルーチン）＋
    /// <c>jobs.map { it.await() }</c>（例外を無条件伝播＝<c>RunMultiWorker</c> の
    /// per-task try/catch とは異なる設計 — 各ワーカー自身が2段の try/catch で
    /// <c>OperationCanceledException</c> 以外を全て捕捉・<see cref="AdaptiveWorkerOutcome"/> へ変換
    /// して正常終了するため、この <c>await</c> がスローされるのは真にキャンセルされた場合のみ）。
    /// C# 側は <c>Task.Run(async () =&gt; { ... })</c>（<c>Dispatchers.Default</c> と同じ役割）＋
    /// <c>Task.WhenAll(jobs)</c>（<c>SaOptimizer.Run</c>/<c>RunMultiWorker</c>で確立済みの単一ループ
    /// eager 起動パターン、<see cref="RunMultiWorker"/> の doc comment 参照）＋直後の
    /// <c>cancellationToken.ThrowIfCancellationRequested()</c>（<c>ensureActive()</c> の等価物、
    /// <see cref="RunMultiWorker"/> で確立済み）で翻訳する。<c>AtomicInteger</c>/<c>AtomicReference</c>
    /// は <see cref="Interlocked"/> 経由のプレーンフィールドへ（<c>SaOptimizer.Run</c>/
    /// <c>RunMultiWorker</c> で確立済み）、複数フィールドをまとめて更新する
    /// <c>synchronized(lock)</c> ブロック（<c>globalBest</c>/<c>globalReport</c>/<c>globalLogs</c>/
    /// <c>sharedTrajectories</c> の一貫性を保つ5箇所）は <c>lock (lockObj) { ... }</c>
    /// （<c>SaOptimizer.Run</c>/<see cref="AdaptiveEliteArchive"/> で確立済み）へ。
    /// </summary>
    internal static async Task<V6OptimizerResult> RunAdaptivePortfolio(
        MagiState state,
        int[][] entry,
        int w,
        V6OptimizerOptions options,
        int budgetSec,
        Func<bool> shouldStop,
        Func<bool> stopIsFinal,
        Action<string, ViolationReport?, long, long> onProgress,
        CancellationToken cancellationToken = default)
    {
        var started = NowMs();
        var deadline = started + Math.Max(budgetSec, 1) * 1000L;
        var baseSeed = ActualSeed(options.Seed);
        var workers = PortfolioWorkerCount(w);
        var lockObj = new object();
        Exception? firstError = null;
        var hardZeroWinner = -1;
        var globalImproves = 0;
        var archive = new AdaptiveEliteArchive();

        var sharedTrajectories = new int[workers][][];
        for (var i = 0; i < workers; i++) sharedTrajectories[i] = HypothesisStartFor(state, entry, i, baseSeed);
        var initialReports = new ViolationReport[workers];
        for (var i = 0; i < workers; i++) initialReports[i] = UnifiedViolationChecker.Check(state, sharedTrajectories[i]);

        var globalBest = entry.Copy2D();
        var globalReport = UnifiedViolationChecker.Check(state, globalBest);
        IReadOnlyList<MirrorLog> globalLogs = Array.Empty<MirrorLog>();
        archive.Register(entry, globalReport, HypothesisEpochRole.BaselineRefine, worker: 0, epoch: 0, bridge: false);
        for (var i = 0; i < workers; i++)
        {
            // [3.308.0, Kotlin原本] 初期配置であることを名前で示す（値は AssignmentFor(i, 0) と同じ）。
            var assignment0 = AdaptiveHypothesisEpochPolicy.InitialAssignmentFor(i);
            archive.Register(sharedTrajectories[i], initialReports[i], assignment0.Role, i, 0,
                bridge: initialReports[i].Hard == globalReport.Hard + 1);
            if (Better(initialReports[i], globalReport))
            {
                globalBest = sharedTrajectories[i].Copy2D();
                globalReport = initialReports[i];
            }
        }

        // [3.376.0, Kotlin原本] hardZeroWinner は「先に HARD=0 へ到達した者が勝ち＝残りを即キャンセル」
        //   していた省電力機構の名残。HARD=0 到達後に残る仕事は全部 SOFT なので、勝者1本だけが担うと
        //   利用者が指定した並列度が使われない＝キル自体を撤廃し、記録専用フィールドとしてのみ残す。
        var jobs = new Task<AdaptiveWorkerOutcome>[workers];
        for (var wi = 0; wi < workers; wi++)
        {
            var i = wi; // capture-by-value for the loop variable, matching Kotlin's `Array(workers) { i -> ... }`.
            jobs[i] = Task.Run(async () =>
            {
                int[][] trajectory;
                lock (lockObj) { trajectory = sharedTrajectories[i].Copy2D(); }
                var elite = trajectory.Copy2D();
                var eliteReport = initialReports[i];
                IReadOnlyList<MirrorLog> eliteLogs = Array.Empty<MirrorLog>();
                var reassignments = 0;
                var stagnantEpochs = 0;
                var improvedPrevious = false;
                var epoch = 0;
                var iterations = 0L;
                var exitReason = "";   // [3.346.0, Kotlin原本] epoch ループの離脱理由（下で確定）
                var survivedStops = 0; // [3.346.1, Kotlin原本] 一瞬の停滞シグナルを見送った回数
                // [C#移植上の判断・LinkedHashMap] Kotlin の LinkedHashMap（挿入順保持）を素の
                //   Dictionary へ。ここは accumulate-only（削除なし）＝.NET の Dictionary は実装上
                //   挿入順を保つ（保証はされないが本ポート全体でこの前提を踏襲）。影響は roleNote の
                //   ログ表示順のみ（roleTotals の集計側は明示的に値降順ソートするため無関係）。
                var roleRuns = new Dictionary<HypothesisEpochRole, int>();
                var roleMillis = new Dictionary<HypothesisEpochRole, long>();
                // [3.409.17, Kotlin原本] ロール呼出が roleDeadline を5秒超えて走った事実を役割名つきで記録。
                var epochOverrunNotes = new List<string>();
                // [3.281.0, Kotlin原本] ワーカー専属のHF63をエポック横断で共有（ワーカー内は逐次実行＝
                //   並行アクセスなし。ワーカー間では共有しない＝役割多様性を汚染しないため）。
                var workerHf63 = new Hf63Infeasibility();

                // [3.346.1/方針B, Kotlin原本] 停滞シグナルは単調でない（改善が届けば偽に戻る）ので、
                //   while 条件には単調な締切・勝者確定だけを置き、シグナルは ConfirmStop で確認窓ぶん
                //   再確認してから離脱する。一瞬のシグナルで片肺運転にならない。
                while (NowMs() < deadline)
                {
                    if (shouldStop())
                    {
                        if (await ConfirmStop(shouldStop, deadline, stopIsFinal, cancellationToken).ConfigureAwait(false))
                        {
                            exitReason = stopIsFinal() ? "探索締切" : "停滞シグナル";
                            break;
                        }
                        survivedStops++;
                        continue;
                    }
                    try
                    {
                        var assignment = AdaptiveHypothesisEpochPolicy.AssignmentFor(i, reassignments);
                        var epochT0 = NowMs();
                        var roleSeed = AdaptiveHypothesisEpochPolicy.EpochSeed(baseSeed, i, epoch, reassignments);
                        // [3.282.0, Kotlin原本] エポック改善の基準線＝エポック開始時点の自己エリート。
                        var preEpochEliteReport = eliteReport;
                        int[][] snapGlobalBest;
                        ViolationReport snapGlobalReport;
                        int[][][] snapPeers;
                        lock (lockObj)
                        {
                            snapGlobalBest = globalBest.Copy2D();
                            snapGlobalReport = globalReport;
                            snapPeers = new int[workers][][];
                            for (var x = 0; x < workers; x++) snapPeers[x] = sharedTrajectories[x].Copy2D();
                        }
                        var start = AdaptiveEpochStart(
                            state: state,
                            globalBest: snapGlobalBest,
                            localTrajectory: trajectory,
                            peers: snapPeers,
                            assignment: assignment,
                            seed: roleSeed,
                            shouldStop: shouldStop);
                        var startReport = UnifiedViolationChecker.Check(state, start);
                        archive.Register(start, startReport, assignment.Role, i, epoch,
                            bridge: startReport.Hard == snapGlobalReport.Hard + 1);
                        trajectory = start;
                        if (Better(startReport, eliteReport))
                        {
                            elite = start.Copy2D();
                            eliteReport = startReport;
                            // [3.278.0, Kotlin原本] 旧: eliteLogs 未更新＝この入口盤面が最終勝者になると、
                            //   採用盤面を生成していない古いロール実行のフェーズログが表示されていた。
                            eliteLogs = new[] { new MirrorLog(tag: "AdaptivePortfolio",
                                message: $"W{i} epoch{epoch + 1} 入口盤面({AdaptiveHypothesisEpochPolicy.RoleLabel(assignment)})をエリート採用 HARD={startReport.Hard} total={startReport.Total}") };
                        }
                        var startImprovedGlobal = false;
                        lock (lockObj)
                        {
                            sharedTrajectories[i] = start.Copy2D();
                            if (Better(startReport, globalReport))
                            {
                                globalBest = start.Copy2D();
                                globalReport = startReport;
                                globalLogs = eliteLogs;   // [3.278.0] 同上: グローバル側の stale ログも同期
                                startImprovedGlobal = true;
                            }
                        }
                        if (startImprovedGlobal)
                        {
                            Interlocked.Increment(ref globalImproves);
                            onProgress($"適応portfolio W{i} {AdaptiveHypothesisEpochPolicy.RoleLabel(assignment)} 入口改善",
                                startReport, iterations, NowMs() - started);
                        }

                        var remainingSec = (int)Math.Max((deadline - NowMs() + 999L) / 1000L, 0L);
                        var quantum = AdaptiveHypothesisEpochPolicy.QuantumSeconds(assignment, improvedPrevious, remainingSec);
                        if (quantum <= 0) break;
                        var roleDeadline = Math.Min(deadline, NowMs() + quantum * 1000L);
                        var roleIndex = i + reassignments * 8;
                        // [3.409.21, Kotlin原本] ロール1本=内部チェーン1本（希釈回避。複数チェーン化
                        //   =portfolioRoleParallelSa は単体 A/B で中立＝削除済み）。
                        var roleOptions = options with
                        {
                            Workers = 1,
                            Seed = roleSeed,
                            Explore = assignment.Role is HypothesisEpochRole.HardDebtRsiPlus
                                or HypothesisEpochRole.LargeDestroyAlns or HypothesisEpochRole.MaxDistanceRsiPlus
                                ? Math.Max(2.0, RoleExploreFor(roleIndex))
                                : RoleExploreFor(roleIndex),
                            Accept = RoleAcceptFor(roleIndex),
                            OpSelect = RoleOpSelectFor(roleIndex),
                            Tabu = assignment.Role != HypothesisEpochRole.BaselineRefine,
                        };
                        bool StopRole() => shouldStop() || NowMs() >= roleDeadline;
                        var roleT0 = NowMs();
                        V6OptimizerResult? result;
                        try
                        {
                            void Progress(string phase, ViolationReport? rep, long iters, long elapsed)
                            {
                                if (rep?.Hard == 0) Interlocked.CompareExchange(ref hardZeroWinner, i, -1); // 記録のみ（キルしない）
                                if (i == 0 || rep?.Hard == 0)
                                {
                                    onProgress($"適応portfolio W{i} epoch{epoch + 1} {AdaptiveHypothesisEpochPolicy.RoleLabel(assignment)} / {phase}",
                                        rep, iters, elapsed);
                                }
                            }
                            result = assignment.Algorithm switch
                            {
                                V6Algorithm.Alns => await RunAlns(state, start.Copy2D(), roleOptions, quantum, StopRole, Progress, cancellationToken).ConfigureAwait(false),
                                V6Algorithm.Rsi => await RunRsi(state, start.Copy2D(), roleOptions, quantum, StopRole, Progress, workerHf63, cancellationToken).ConfigureAwait(false),
                                _ => await RunRsiPlus(state, start.Copy2D(), roleOptions, quantum, StopRole, Progress, workerHf63, cancellationToken).ConfigureAwait(false),
                            };
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception e)
                        {
                            Interlocked.CompareExchange(ref firstError, e, null);
                            result = null;
                        }

                        // [3.409.17, Kotlin原本] roleDeadline を5秒超えたロールを役割名つきで記録。
                        if (NowMs() - roleDeadline > 5_000L)
                        {
                            epochOverrunNotes.Add($"W{i}:{AdaptiveHypothesisEpochPolicy.RoleName(assignment.Role)}(q={quantum}s→実{(NowMs() - roleT0) / 1000}s)");
                        }

                        if (result != null)
                        {
                            if (result.Report.Hard == 0) Interlocked.CompareExchange(ref hardZeroWinner, i, -1); // 記録のみ
                            iterations += result.Iterations;
                            archive.Register(result.Schedule, result.Report, assignment.Role, i, epoch,
                                bridge: result.Report.Hard == snapGlobalReport.Hard + 1);
                            trajectory = result.Schedule.Copy2D();
                            if (Better(result.Report, eliteReport))
                            {
                                elite = result.Schedule.Copy2D();
                                eliteReport = result.Report;
                                eliteLogs = result.PhaseLogs;
                            }
                            var improvedGlobal = false;
                            lock (lockObj)
                            {
                                sharedTrajectories[i] = result.Schedule.Copy2D();
                                if (Better(result.Report, globalReport))
                                {
                                    globalBest = result.Schedule.Copy2D();
                                    globalReport = result.Report;
                                    globalLogs = result.PhaseLogs;
                                    improvedGlobal = true;
                                }
                            }
                            if (improvedGlobal)
                            {
                                Interlocked.Increment(ref globalImproves);
                                onProgress($"適応portfolio グローバル最良更新 W{i} epoch{epoch + 1}",
                                    result.Report, iterations, NowMs() - started);
                            }
                        }

                        // [3.282.0, Kotlin原本] エポック改善＝自己エリートの前進（入口盤面の採用・ロール
                        //   結果の採用いずれも eliteReport 更新経由でここに反映される）。
                        var improvedThisEpoch = Better(eliteReport, preEpochEliteReport);
                        stagnantEpochs = AdaptiveHypothesisEpochPolicy.NextStagnantEpochs(stagnantEpochs, improvedThisEpoch);
                        int nearest;
                        lock (lockObj)
                        {
                            var d = int.MaxValue;
                            for (var x = 0; x < workers; x++)
                                if (x != i)
                                    d = Math.Min(d, ScheduleDistance(trajectory, sharedTrajectories[x]));
                            nearest = d;
                        }
                        if (AdaptiveHypothesisEpochPolicy.ShouldReassign(
                                index: i, improvedThisEpoch: improvedThisEpoch,
                                stagnantEpochs: stagnantEpochs, nearestOtherDistance: nearest))
                        {
                            reassignments++;
                            stagnantEpochs = 0;
                            // [3.308.1/敵対検証, Kotlin原本] この分岐は常に基準量子へ戻す（旧
                            //   improvedPrevious = false）。roleChanged=true はその挙動を保つための
                            //   引数であって「役割が必ず変わる」という主張ではない。
                            improvedPrevious = AdaptiveHypothesisEpochPolicy.CarriesImprovingQuantum(improvedThisEpoch, roleChanged: true);
                        }
                        else
                        {
                            improvedPrevious = AdaptiveHypothesisEpochPolicy.CarriesImprovingQuantum(improvedThisEpoch, roleChanged: false);
                        }
                        // [3.282.0/3.308.1, Kotlin原本] 集計はロールが実際に走ることが確定してから。
                        //   回数と秒をここで同時に数える（quantum<=0 break と例外の break はここへ
                        //   到達しないため、その回の摂動＋フル検査の時間は秒合計に入らない）。
                        roleRuns[assignment.Role] = roleRuns.GetValueOrDefault(assignment.Role) + 1;
                        roleMillis[assignment.Role] = roleMillis.GetValueOrDefault(assignment.Role) + (NowMs() - epochT0);
                        epoch++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e)
                    {
                        // [3.278.0, Kotlin原本] epoch単位の隔離: このワーカーだけ停止し、蓄積済み
                        //   エリートを成果として返す。
                        Interlocked.CompareExchange(ref firstError, e, null);
                        exitReason = "例外";
                        break;
                    }
                }
                // [3.346.0, Kotlin原本] while 条件のどれで抜けたかを確定
                //   （例外と確認済み停滞シグナルは上で確定済み。ここは単調な2条件のみ）。
                if (exitReason.Length == 0)
                {
                    exitReason = "締切";   // [3.376.0] 勝者キル撤廃により、単調な離脱条件は締切のみ
                }
                var exitAtSec = (NowMs() - started) / 1000;

                return new AdaptiveWorkerOutcome(
                    Elite: elite,
                    Report: eliteReport,
                    Logs: eliteLogs,
                    Iterations: iterations,
                    Epochs: epoch,
                    Reassignments: reassignments,
                    RoleRuns: roleRuns,
                    RoleMillis: roleMillis,
                    LastRole: AdaptiveHypothesisEpochPolicy.AssignmentFor(i, reassignments).Role,
                    ExitReason: exitReason,
                    ExitAtSec: exitAtSec,
                    SurvivedStops: survivedStops,
                    Hf63Avoided: workerHf63.InfeasibleFamilies(),
                    EpochOverruns: epochOverrunNotes);
            });
        }

        var outcomes = await Task.WhenAll(jobs).ConfigureAwait(false);
        // 兄弟キャンセル(自己)とユーザー停止(外部)を区別: 外部停止ならここで伝播させる
        // （Kotlin原本の ensureActive() と同じ役割、RunMultiWorker で確立済みのパターン）。
        cancellationToken.ThrowIfCancellationRequested();

        for (var index = 0; index < outcomes.Length; index++)
        {
            var o = outcomes[index];
            archive.Register(o.Elite, o.Report, o.LastRole, index, o.Epochs, bridge: o.Report.Hard == globalReport.Hard + 1);
            if (Better(o.Report, globalReport))
            {
                globalBest = o.Elite.Copy2D();
                globalReport = o.Report;
                globalLogs = o.Logs;
            }
        }
        var compressedElites = archive.Snapshot(globalBest, globalReport);
        var alts = compressedElites
            .Where(e => !e.Bridge)
            .Where(e => !AdaptiveEliteArchive.SameSchedule(e.Schedule, globalBest))
            .Select(e => e.Schedule.Copy2D())
            .Take(3)
            .ToList();
        // [3.335.0, Kotlin原本] まずこの実行のスロットへ。static は新しい実行が勝つライブ表示用なので所有時のみ。
        var slot = GetRunSlot();
        if (slot != null) { slot.FusionElites = compressedElites; slot.Alternatives = alts; }
        if (OwnsStatics(slot)) { _lastFusionElites = compressedElites; _lastAlternatives = alts; }

        // [3.332.0/実機ログで判明, Kotlin原本] 「圧縮elite=N 相異なるelite=M 距離=a..b」は M と 距離 の
        //   母集団が違うため矛盾に見えた。意味があるのは**ワーカー解が潰れているか**（同一解に収束＝
        //   並列の無駄）なので、そちらを出す。
        var distinctWorkers = outcomes
            .Select(o => string.Join("|", o.Elite.Select(row => string.Join(",", row))))
            .Distinct().Count();
        var pairDistances = new List<int>();
        for (var a = 0; a < outcomes.Length; a++)
            for (var b = a + 1; b < outcomes.Length; b++)
                pairDistances.Add(ScheduleDistance(outcomes[a].Elite, outcomes[b].Elite));
        var distanceNote = pairDistances.Count == 0
            ? "対象外"
            : $"{pairDistances.Min()}..{pairDistances.Max()}セル" + (distinctWorkers < outcomes.Length ? "・同一解あり" : "");
        var roleNote = string.Join(" | ", Enumerable.Range(0, outcomes.Length).Select(i =>
        {
            var o = outcomes[i];
            var used = string.Join(",", o.RoleRuns.Select(e =>
            {
                var sec = o.RoleMillis.GetValueOrDefault(e.Key, 0L) / 1000.0;
                return $"{AdaptiveHypothesisEpochPolicy.RoleName(e.Key)}x{e.Value}/{sec:F0}s";
            }));
            var avoided = o.Hf63Avoided.Count == 0 ? "" : $"/HF63回避={string.Join("+", o.Hf63Avoided)}";
            var survived = o.SurvivedStops == 0 ? "" : $"/停滞見送り{o.SurvivedStops}回";
            return $"W{i}:epoch{o.Epochs}/再配属{o.Reassignments}[{used}]{avoided}/離脱={o.ExitReason}@{o.ExitAtSec}s{survived}";
        }));
        // [3.307.0/ログ強化, Kotlin原本] 全ワーカー横断の役割別 worker秒。予算配分を論じるときに見るのはここ。
        var roleTotals = new Dictionary<HypothesisEpochRole, long>();
        foreach (var o in outcomes)
            foreach (var (r, ms) in o.RoleMillis)
                roleTotals[r] = roleTotals.GetValueOrDefault(r) + ms;
        var totalWorkerMs = Math.Max(roleTotals.Values.Sum(), 1L);
        // [3.409.4, Kotlin原本] 外側ワーカーの実効並列度。片肺化を数字1つで検出する。
        var outerParallelism = ObservedOuterParallelism(totalWorkerMs, NowMs() - started);
        var budgetNote = string.Join(" ", roleTotals.OrderByDescending(e => e.Value).Select(e =>
            $"{AdaptiveHypothesisEpochPolicy.RoleName(e.Key)}={e.Value / 1000}s({e.Value * 100 / totalWorkerMs}%)"));
        // [3.346.0/3.346.1/3.409.16, Kotlin原本] 締切前に離脱したワーカーを1行で明示する。
        var earlyExits = outcomes.Where(o => IsEarlyWorkerExit(o.ExitReason)).ToList();
        var survivedTotal = outcomes.Sum(o => o.SurvivedStops);
        var survivedNote = survivedTotal == 0 ? "" : $" 停滞見送り計{survivedTotal}回";
        var exitNote = (earlyExits.Count == 0
            ? "ワーカー離脱=全て締切まで実行"
            : $"ワーカー離脱={earlyExits.Count}/{outcomes.Length}本が締切前(" +
                string.Join(",", earlyExits.GroupBy(o => o.ExitReason).Select(g =>
                    $"{g.Key}{g.Count()}本@{string.Join("/", g.Select(o => $"{o.ExitAtSec}s"))}")) + ")")
            + survivedNote;
        var summary = new MirrorLog(
            tag: "AdaptivePortfolio",
            // [3.360.0, Kotlin原本] 合計iter と 最良更新回数 を併記。
            message: $"合計iter={outcomes.Sum(o => o.Iterations)} 全体最良更新={Volatile.Read(ref globalImproves)}回 / " +
                $"非同期適応仮説 archive={archive.Size()} 圧縮elite={compressedElites.Count} " +
                $"ワーカー解={outcomes.Length}本(相異なる{distinctWorkers}本) 距離={distanceNote} / {exitNote} / " +
                $"役割別worker秒(計{totalWorkerMs / 1000}s・実効外側並列={outerParallelism:F2}): {budgetNote} / {roleNote}" +
                (firstError != null ? $" / 一部例外={firstError.Message}" : "") +
                $" / 採用 HARD={globalReport.Hard} total={globalReport.Total}");
        // [3.409.17, Kotlin原本] エポック超過（役割名つき）は専用の [W] 行で出す。
        var overrunLog = EpochOverrunLog(outcomes.SelectMany(o => o.EpochOverruns).ToList());
        var logs = globalLogs
            .Concat(overrunLog != null ? new[] { overrunLog } : Array.Empty<MirrorLog>())
            .Append(summary)
            .ToList();
        return new V6OptimizerResult(
            globalBest,
            globalReport with { Logs = logs.Concat(globalReport.Logs).ToList() },
            V6Algorithm.Portfolio,
            logs,
            outcomes.Sum(o => o.Iterations),
            NowMs() - started);
    }
}
