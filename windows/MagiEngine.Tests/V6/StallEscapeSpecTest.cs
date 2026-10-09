using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[Kotlin原本 <c>StallEscapeSpecTest</c> 1:1] Android <c>docs/stall_escape.md</c> の規則と数値をそのまま固定する。
/// 既存の <c>V6FinalPortWatchdogTest</c>・<c>AdaptiveHypothesisEpochPolicyTest</c>・RSI テストが持たない表の値と境界だけを扱う。</summary>
public class StallEscapeSpecTest
{
    [Fact]
    public void BudgetTableFor300Seconds()
    {
        var b = V6FinalPort.WatchdogBudgetOf(300_000L, startMs: 0L, hardDeadlineMs: 300_000L, fraction: 0.9);
        Assert.Equal(45_000L, b.MinRunMs);
        Assert.Equal(25_000L, b.PostReserveMs);
        Assert.Equal(275_000L, b.SearchDeadlineMs);
        Assert.Equal(275_000L, b.SearchWindowMs);
        Assert.Equal(270_000L, b.StallMs);
        Assert.Equal(37_500L, b.StallHardMs);
        Assert.Equal(7_500L, b.PhaseGraceMs);
    }

    [Fact]
    public void BudgetTableFor60SecondsFallsBackToWindowFraction()
    {
        var b = V6FinalPort.WatchdogBudgetOf(60_000L, startMs: 0L, hardDeadlineMs: 60_000L, fraction: 0.9);
        Assert.Equal(10_000L, b.MinRunMs);
        Assert.Equal(8_000L, b.PostReserveMs);
        Assert.Equal(52_000L, b.SearchWindowMs);
        Assert.Equal(46_800L, b.StallMs);
        Assert.Equal(15_000L, b.StallHardMs);
        Assert.Equal(2_000L, b.PhaseGraceMs);
    }

    [Fact]
    public void TwentySecondBudgetCannotFireTheNormalWatchdog()
    {
        var b = V6FinalPort.WatchdogBudgetOf(20_000L, startMs: 0L, hardDeadlineMs: 20_000L, fraction: 0.9);
        Assert.Equal(8_000L, b.MinRunMs);
        Assert.Equal(8_000L, b.PostReserveMs);
        Assert.Equal(12_000L, b.SearchWindowMs);
        Assert.True(b.StallMs >= b.SearchWindowMs);
        Assert.True(b.StallHardMs >= b.SearchWindowMs);
    }

    [Fact]
    public void BudgetUsesStartAndHardDeadlineNotZero()
    {
        var b = V6FinalPort.WatchdogBudgetOf(300_000L, startMs: 1_000_000L, hardDeadlineMs: 1_300_000L, fraction: 0.9);
        Assert.Equal(1_275_000L, b.SearchDeadlineMs);
        Assert.Equal(275_000L, b.SearchWindowMs);
    }

    [Fact]
    public void ProgressImprovedFollowsHardThenWeightedThenTotal()
    {
        Assert.True(V6FinalPort.ProgressImproved(h: 0, wgt: 1000.0, t: 50, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.True(V6FinalPort.ProgressImproved(h: 1, wgt: 9.0, t: 50, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.True(V6FinalPort.ProgressImproved(h: 1, wgt: 10.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.False(V6FinalPort.ProgressImproved(h: 1, wgt: 11.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.False(V6FinalPort.ProgressImproved(h: 2, wgt: 0.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
    }

    [Fact]
    public void ProgressImprovedIgnoresFloatingNoiseButBetterReportIsStrict()
    {
        Assert.False(V6FinalPort.ProgressImproved(h: 1, wgt: 10.0 - 1e-9, t: 1, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.True(V6FinalPort.ProgressImproved(h: 1, wgt: 10.0 + 1e-9, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
        Assert.False(V6FinalPort.ProgressImproved(h: 1, wgt: 11.0, t: 0, bh: 1, bWeighted: 10.0, bTotal: 1));
    }

    [Fact]
    public void WeightsAreIntegersSoToleranceNeverHidesARealDifference()
    {
        Assert.All(MirrorKeys.Weights, kv => Assert.True(kv.Weight == Math.Floor(kv.Weight) && kv.Weight >= 2.0));
        Assert.Equal(new HashSet<string> { "groupViol", "c3n", "covU", "pref", "c3w" }, MirrorKeys.Hard.ToHashSet());
    }

    [Fact]
    public void FiringBoundariesAreStrict()
    {
        const long start = 0L, minRun = 45_000L, grace = 7_500L, eff = 270_000L;
        bool Fired(long now, long lastImprove, long lastPhase) =>
            V6FinalPort.WatchdogStagnationFired(now, start, minRun, lastPhase, grace, lastImprove, eff);
        Assert.False(Fired(now: 300_000L, lastImprove: 30_000L, lastPhase: 0L));
        Assert.True(Fired(now: 300_001L, lastImprove: 30_000L, lastPhase: 0L));
        Assert.False(Fired(now: 540_000L, lastImprove: 0L, lastPhase: 540_000L));
        Assert.True(Fired(now: 540_001L, lastImprove: 0L, lastPhase: 540_001L));
        Assert.False(Fired(now: 45_000L, lastImprove: -300_000L, lastPhase: -100_000L));
    }

    [Fact]
    public void StopConfirmWindowIsFiveSeconds() => Assert.Equal(5_000L, V6NativeOptimizer.StopConfirmMs);

    [Fact]
    public void Hf63TracksExactlyThirteenFamilies()
    {
        Assert.Equal(
            new HashSet<string> { "c1", "c2", "c3", "c3n", "c3m", "c3mn", "c41", "c42", "covU", "covO", "pref", "low", "high" },
            Hf63Infeasibility.KeyToIndex.Keys.ToHashSet());
        foreach (var k in new[] { "groupViol", "c3w", "c41s", "c42s", "apt", "weekly", "fair" }) Assert.False(Hf63Infeasibility.KeyToIndex.ContainsKey(k), k);
    }

    [Fact]
    public void EffortItersFollowsTheAttemptsTargetFormula()
    {
        Assert.Equal(2_500, V6NativeOptimizer.RsiHf63EffortIters(2));
        Assert.Equal(2_500, V6NativeOptimizer.RsiHf63EffortIters(5));
        Assert.Equal(1_667, V6NativeOptimizer.RsiHf63EffortIters(8));
        Assert.Equal(1_250, V6NativeOptimizer.RsiHf63EffortIters(10));
    }

    [Fact]
    public void PortfolioQuantaAndDuplicateDistance()
    {
        Assert.Equal(5, AdaptiveHypothesisEpochPolicy.BASE_QUANTUM_SEC);
        Assert.Equal(8, AdaptiveHypothesisEpochPolicy.IMPROVING_QUANTUM_SEC);
        Assert.Equal(35, AdaptiveHypothesisEpochPolicy.RSI_PLUS_BASE_QUANTUM_SEC);
        Assert.Equal(45, AdaptiveHypothesisEpochPolicy.RSI_PLUS_IMPROVING_QUANTUM_SEC);
        Assert.Equal(2, AdaptiveHypothesisEpochPolicy.DUPLICATE_DISTANCE_CELLS);
        Assert.Equal(1, AdaptiveHypothesisEpochPolicy.NextStagnantEpochs(0, improvedThisEpoch: false));
        Assert.Equal(0, AdaptiveHypothesisEpochPolicy.NextStagnantEpochs(3, improvedThisEpoch: true));
    }

    private static ViolationReport Rep(int hard, double weighted, int total, params (string Family, int Count)[] fams)
    {
        var bd = fams.ToDictionary(f => f.Family, f => f.Count);
        return new ViolationReport(
            Violations: new Dictionary<string, string>(), NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(), Breakdown: bd,
            Total: total, Hard: hard, Soft: total - hard, WeightedScore: weighted);
    }

    [Fact]
    public void StagnationLatchClearsOnImprovementAndStaysOnNonImprovement()
    {
        var wd = new V6FinalPort.WatchdogBest(startMs: 0L);
        Assert.True(wd.Observe(Rep(1, 100.0, 5), nowMs: 1_000L, observedIters: 10L, beatsInput: () => true, wishC3wProven: 0));
        Assert.Equal(1_000L, wd.LastBeatInputMs);
        wd.Fire(nowMs: 300_000L, observedIters: 500L, byOverride: true);
        Assert.True(wd.StagnationFired); Assert.Equal(299_000L, wd.StagnationDurationMs); Assert.Equal(500L, wd.StagnationIters);
        Assert.True(wd.StagnationByOverride);
        Assert.False(wd.Observe(Rep(1, 101.0, 5), 301_000L, 600L, () => false, 0));
        Assert.True(wd.StagnationFired);
        Assert.True(wd.Observe(Rep(1, 99.0, 5), 302_000L, 700L, () => false, 0));
        Assert.False(wd.StagnationFired); Assert.Equal(-1L, wd.StagnationDurationMs); Assert.Equal(-1L, wd.StagnationIters);
        Assert.False(wd.StagnationByOverride);
        Assert.Equal(302_000L, wd.LastBestImproveMs); Assert.Equal(700L, wd.LastBestImproveIters);
        Assert.Equal(1_000L, wd.LastBeatInputMs);
        Assert.Equal(2, wd.BestVersion);
    }

    [Fact]
    public void ObserveTracksNonCovUHardAndC3nOnlyFlag()
    {
        var wd = new V6FinalPort.WatchdogBest(startMs: 0L);
        var beatsAsked = 0;
        Assert.False(wd.Observe(Rep(int.MaxValue, double.MaxValue, int.MaxValue), 1L, 0L, () => { beatsAsked++; return true; }, 0));
        Assert.Equal(0, beatsAsked);
        wd.Observe(Rep(3, 9000.0 * 3, 3, ("c3n", 2), ("covU", 1)), 10L, 1L, () => true, 0);
        Assert.Equal(2, wd.BestNonCovUHard); Assert.True(wd.BestNonCovUAllC3n);
        wd.Observe(Rep(2, 9000.0 * 2, 2, ("c3n", 1), ("pref", 1)), 20L, 2L, () => true, 0);
        Assert.Equal(2, wd.BestNonCovUHard); Assert.False(wd.BestNonCovUAllC3n);
        wd.Observe(Rep(1, 9000.0, 1, ("c3w", 1)), 30L, 3L, () => true, 1);
        Assert.False(wd.BestNonCovUAllC3n);
        Assert.Equal(3, wd.BestVersion);
    }

    [Fact]
    public void AvoidSetsKeepSoftFocusableAndSeparateCooldown()
    {
        var (avoid, focusAvoid) = V6NativeOptimizer.AvoidSets(new HashSet<string> { "c1", "low", "c3n", "covO" }, covU: 3, covUFloor: 0, cooldownFocus: null);
        Assert.Equal(new HashSet<string> { "c3n" }, avoid.ToHashSet()); Assert.Equal(new HashSet<string> { "c3n" }, focusAvoid.ToHashSet());
        var (a2, f2) = V6NativeOptimizer.AvoidSets(new HashSet<string> { "pref" }, covU: 2, covUFloor: 2, cooldownFocus: "c1");
        Assert.Equal(new HashSet<string> { "pref", "covU" }, a2.ToHashSet()); Assert.Equal(new HashSet<string> { "pref", "covU", "c1" }, f2.ToHashSet());
        var (a3, _) = V6NativeOptimizer.AvoidSets(new HashSet<string>(), covU: 3, covUFloor: 2, cooldownFocus: null);
        Assert.Empty(a3);
        var (a4, _) = V6NativeOptimizer.AvoidSets(new HashSet<string>(), covU: 0, covUFloor: 0, cooldownFocus: null);
        Assert.Empty(a4);
    }

    // Kotlin 原本の defaultsMatchTheSpec から、C# に無い StallPolishInjection（移植対象外）を除く。
    [Fact]
    public void DefaultsMatchTheSpec()
    {
        Assert.Equal(0.9, PolishGate.NormalStallFraction);
        Assert.False(PolishGate.PostChainRollbackCountsZero);
        Assert.Equal(WishFloorMode.Off, PolishGate.WishConflictFloorMode);
        Assert.True(PolishGate.C3nWallShortStall);
        Assert.False(PolishGate.C3nWallLegacy, "基準腕（HEAD の壁判定）は既定で使わない");
        Assert.True(PolishGate.LateOpStopPropagation, "後期演算は停止要求を見る（既定）");
        Assert.False(PolishGate.C3nWallDeepCheck, "1 手探索の反証は既定で使わない（測定中）");
        Assert.False(PolishGate.AdaptiveStall, "適応閾値は既定で使わない（測定中）");
        Assert.Equal(2, V6FinalPort.StallOverrideFactor);
        Assert.Equal((3, 3, 8), (V6FinalPort.AdaptiveStallFactor, V6FinalPort.AdaptiveStallMinGaps, V6FinalPort.AdaptiveStallWindow));
        Assert.Equal(5000, Hf63Infeasibility.INFEAS_STALL_ITERS);
    }

    // §5.8 C 適応閾値: 間隔 3 個未満は使わない、最大×3 を [短, 通常] に挟む、通常分岐だけを縮める（床・壁の短い閾値はそのまま）
    [Fact]
    public void AdaptiveStallNeedsThreeGapsAndClampsBetweenShortAndNormal()
    {
        Assert.Null(V6FinalPort.AdaptiveStallMs(new long[] { 5_000L, 6_000L }, 37_500L, 270_000L));
        Assert.Equal(60_000L, V6FinalPort.AdaptiveStallMs(new long[] { 5_000L, 20_000L, 6_000L }, 37_500L, 270_000L));
        Assert.Equal(37_500L, V6FinalPort.AdaptiveStallMs(new long[] { 1_000L, 2_000L, 3_000L }, 37_500L, 270_000L));
        Assert.Equal(270_000L, V6FinalPort.AdaptiveStallMs(new long[] { 100_000L, 100_000L, 100_000L }, 37_500L, 270_000L));
        Assert.Equal(9_000L, V6FinalPort.AdaptiveStallMs(new long[] { 1_000L, 1_000L, 1_000L }, 10_000L, 9_000L));
    }

    [Fact]
    public void AdaptiveOnlyShortensTheNormalBranch()
    {
        var normal = V6FinalPort.EffectiveStallMs(3, 0, 3, false, false, 37_500L, 270_000L);
        Assert.Equal(270_000L, normal);
        Assert.Equal(60_000L, V6FinalPort.EffectiveStallMs(3, 0, 3, false, false, 37_500L, 270_000L, adaptiveMs: 60_000L));
        Assert.Equal(37_500L, V6FinalPort.EffectiveStallMs(0, 0, 0, false, false, 37_500L, 270_000L, adaptiveMs: 60_000L));
        Assert.Equal(normal, V6FinalPort.EffectiveStallMs(3, 0, 3, false, false, 37_500L, 270_000L, adaptiveMs: null));
    }

    [Fact]
    public void ObserveRecordsGapsBetweenImprovementsOnlyAndKeepsTheLastEight()
    {
        var wd = new V6FinalPort.WatchdogBest(startMs: 0L);
        wd.Observe(Rep(5, 500.0, 5), 10_000L, 1L, () => true, 0);
        Assert.Empty(wd.RecentGaps());
        var w = 500.0;
        for (var k = 1; k <= 10; k++) { w -= 1.0; wd.Observe(Rep(5, w, 5), 10_000L + k * 1_000L * k, 1L + k, () => true, 0); }
        var gaps = wd.RecentGaps();
        Assert.Equal(8, gaps.Count);
        Assert.Equal(Enumerable.Range(3, 8).Select(k => (k * k - (k - 1) * (k - 1)) * 1_000L).ToArray(), gaps.ToArray());
        Assert.False(wd.Observe(Rep(5, w + 1, 5), 999_000L, 99L, () => true, 0));
        Assert.Equal(8, wd.RecentGaps().Count);
    }

    // §5.4 判定と発火の間に改善が割り込んだら発火しない（判定後の改善の順序を検査する）
    [Fact]
    public void FireIsRefusedWhenAnImprovementInterleavesBetweenDecisionAndFire()
    {
        var wd = new V6FinalPort.WatchdogBest(startMs: 0L);
        wd.Observe(Rep(1, 100.0, 5), nowMs: 1_000L, observedIters: 10L, beatsInput: () => true, wishC3wProven: 0);
        var gen = wd.BestVersion;   // 判定した世代
        wd.Observe(Rep(1, 99.0, 5), nowMs: 200_000L, observedIters: 20L, beatsInput: () => false, wishC3wProven: 0);
        Assert.False(wd.FireIfGeneration(gen, nowMs: 400_000L, observedIters: 30L, byOverride: false, wall: false),
            "判定後に改善が届いたら発火しない");
        Assert.False(wd.StagnationFired, "古い判定で停滞ラッチを立て直さない");
        var gen2 = wd.BestVersion;
        Assert.True(wd.FireIfGeneration(gen2, nowMs: 400_000L, observedIters: 30L, byOverride: false, wall: true),
            "世代が変わらなければ発火する");
        Assert.True(wd.StagnationFired);
        Assert.True(wd.StagnationWall);
    }

    // §5.4 一度確定した停滞ラッチは、同じ世代の後続の判定で時刻・反復数・壁の記録を上書きしない
    [Fact]
    public void RepeatedDecisionDoesNotOverwriteALatchedFire()
    {
        var wd = new V6FinalPort.WatchdogBest(startMs: 0L);
        wd.Observe(Rep(1, 100.0, 5), nowMs: 1_000L, observedIters: 10L, beatsInput: () => true, wishC3wProven: 0);
        var gen = wd.BestVersion;
        Assert.True(wd.FireIfGeneration(gen, nowMs: 400_000L, observedIters: 30L, byOverride: false, wall: false));
        var duration = wd.StagnationDurationMs;
        Assert.True(wd.FireIfGeneration(gen, nowMs: 900_000L, observedIters: 90L, byOverride: true, wall: true));
        Assert.Equal(duration, wd.StagnationDurationMs);   // 確定済みの発火は時刻を上書きしない
        Assert.Equal(30L, wd.StagnationIters);
        Assert.False(wd.StagnationWall, "確定済みの発火は壁の記録を上書きしない");
    }

    // §5.3 診断キャッシュは盤面の内容で鍵をとり、別盤面の結果を返さない
    [Fact]
    public void BoardKeyedFlagNeverReturnsAnotherBoardsResult()
    {
        var flag = new V6FinalPort.BoardKeyedFlag();
        var x = BoardOf(new[] { 1, 2 }, new[] { 3, 4 });
        var y = BoardOf(new[] { 9, 9 }, new[] { 9, 9 });
        var evals = 0;
        bool Eval(IReadOnlyList<IReadOnlyList<int>> b) { evals++; return SameRows(b, x); }
        Assert.True(flag.Get(x, Eval));
        Assert.Equal(1, evals);
        Assert.True(flag.Get(BoardOf(new[] { 1, 2 }, new[] { 3, 4 }), Eval), "内容が同じ盤面は再診断しない");
        Assert.Equal(1, evals);
        Assert.False(flag.Get(y, Eval), "別の盤面の結果は返さない");
        Assert.Equal(2, evals);
        Assert.True(flag.Get(x, Eval), "戻っても、その盤面自身の結果で判定する");
        Assert.Equal(3, evals);
    }

    // §5.3 壁の証拠は、生存盤面の報告が最良の報告と同じ参照のときだけ使う（値が等しいだけでは使わない）
    [Fact]
    public void C3nWallBindsOnlyToTheSameReportObject()
    {
        var a = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        var same = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        Assert.False(V6FinalPort.C3nWallSameReport(null, a));
        Assert.False(V6FinalPort.C3nWallSameReport(a, null));
        Assert.True(V6FinalPort.C3nWallSameReport(a, a));
        Assert.False(V6FinalPort.C3nWallSameReport(same, a), "値が同じでも別の報告は同じ盤面とみなさない");
    }

    // §5.3 既定の判定は、生存盤面の報告が最良と同じ参照のときだけ診断を使う（値が同じ別の報告では使わない）
    [Fact]
    public void C3nWallProofUsesOnlyTheLiveBoardThatIsTheBestReport()
    {
        var best = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        var sameValues = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        var board = BoardOf(new[] { 1, 2 }, new[] { 3, 4 });
        var diagCalls = 0;
        var live = new V6NativeOptimizer.LiveBestSnapshot(best, board);
        var proof = new V6FinalPort.C3nWallProof(
            bestVersion: () => 1, bestReport: () => best, liveSnapshot: () => live,
            diagnose: _ => { diagCalls++; return true; });
        Assert.True(proof.Bound(), "同じ参照の生存盤面は診断の結果を使う");
        Assert.Equal(1, diagCalls);
        live = new V6NativeOptimizer.LiveBestSnapshot(sameValues, board);
        Assert.False(new V6FinalPort.C3nWallProof(
            bestVersion: () => 1, bestReport: () => best, liveSnapshot: () => live,
            diagnose: _ => true).Bound(), "値が同じでも別の参照の生存盤面は使わない");
    }

    // §5.3 段の境界で生存盤面が空になっても、同じ最良版で成立した判定は持ち越す。版が変われば持ち越さない
    [Fact]
    public void C3nWallProofCarriesItsVerdictAcrossAStageBoundaryOnly()
    {
        var best = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        var board = BoardOf(new[] { 1, 2 }, new[] { 3, 4 });
        var version = 1;
        V6NativeOptimizer.LiveBestSnapshot? live = new V6NativeOptimizer.LiveBestSnapshot(best, board);
        var proof = new V6FinalPort.C3nWallProof(
            bestVersion: () => version, bestReport: () => best, liveSnapshot: () => live, diagnose: _ => true);
        Assert.True(proof.Bound());
        live = null;
        Assert.True(proof.Bound(), "同じ最良版なら持ち越す");
        version = 2;
        Assert.False(proof.Bound(), "版が変われば持ち越さない");
    }

    // §5.3 診断の間に生存盤面が入れ替わったら、その判定は使わない
    [Fact]
    public void C3nWallProofRefusesAVerdictWhenTheLiveBoardChangesDuringDiagnosis()
    {
        var best = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        var board = BoardOf(new[] { 1, 2 }, new[] { 3, 4 });
        V6NativeOptimizer.LiveBestSnapshot? live = new V6NativeOptimizer.LiveBestSnapshot(best, board);
        var proof = new V6FinalPort.C3nWallProof(
            bestVersion: () => 1, bestReport: () => best, liveSnapshot: () => live,
            diagnose: _ =>
            {
                live = new V6NativeOptimizer.LiveBestSnapshot(best, BoardOf(new[] { 9 }));
                return true;
            });
        Assert.False(proof.Bound(), "診断の間に入れ替わったら判定を使わない");
    }

    // 測定の基準腕は HEAD の判定: 報告の参照を見ず、生存盤面の盤面を版ごとに一度だけ診断する
    [Fact]
    public void C3nWallLegacyArmDiagnosesTheLiveBoardOncePerVersion()
    {
        var best = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        var other = Rep(2, 18000.0, 4, ("c3n", 1), ("pref", 1));
        var version = 1;
        var diagCalls = 0;
        var proof = new V6FinalPort.C3nWallProof(
            bestVersion: () => version, bestReport: () => best,
            liveSnapshot: () => new V6NativeOptimizer.LiveBestSnapshot(other, BoardOf(new[] { 1 })),
            diagnose: _ => { diagCalls++; return true; });
        Assert.True(proof.Legacy());
        Assert.True(proof.Legacy());
        Assert.Equal(1, diagCalls);   // 同じ版では一度だけ診断する
        version = 2;
        proof.Legacy();
        Assert.Equal(2, diagCalls);
    }

    // ログの件数は呼び出し回数ではなく、生存盤面の更新ごとに数える
    [Fact]
    public void C3nWallProofCountsEachLiveBoardUpdateOnceNotEachPoll()
    {
        var best = Rep(2, 18000.0, 4, ("c3n", 2), ("covU", 0));
        var other = Rep(2, 18000.0, 4, ("c3n", 1), ("pref", 1));
        var snap = new V6NativeOptimizer.LiveBestSnapshot(other, BoardOf(new[] { 1 }));
        var proof = new V6FinalPort.C3nWallProof(
            bestVersion: () => 1, bestReport: () => best, liveSnapshot: () => snap, diagnose: _ => true);
        for (var n = 0; n < 50; n++) proof.Bound();
        Assert.Equal(1, proof.Checks);     // 同じ生存盤面は一度だけ数える
        Assert.Equal(1, proof.Mismatch);   // 対応しなかった更新も一度だけ数える
    }

    private static IReadOnlyList<IReadOnlyList<int>> BoardOf(params int[][] rows) =>
        rows.Select(r => (IReadOnlyList<int>)r).ToList();

    private static bool SameRows(IReadOnlyList<IReadOnlyList<int>> a, IReadOnlyList<IReadOnlyList<int>> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second));
}
