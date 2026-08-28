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
}
