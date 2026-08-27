namespace MagiEngine.V6;

/// <summary>How a parallel hypothesis obtains its initial board.</summary>
public enum HypothesisStartMode { Baseline, DayRepair, StaffRepair, MixedRepair }

public sealed record HypothesisStartPlan(HypothesisStartMode Mode, int Intensity);

/// <summary>
/// Faithful port of Kotlin's <c>HypothesisDiversityPolicy.kt</c> (52 lines, entirely
/// self-contained). Deterministic role assignment for the parallel search portfolio.
///
/// W0 and W4 keep the original board as safety/precision baselines. The other roles start from
/// structurally different destroy/repair basins. Algorithm assignment is intentionally orthogonal
/// to the start-board assignment.
///
/// Deliberately NOT ported: <c>algorithmFor(index)</c> — the Kotlin source itself already removed
/// it as dead code (3.278.0: "本番呼出0だった。実際のアルゴリズム割当は
/// AdaptiveHypothesisEpochPolicy.algorithmFor が担う"), so there is nothing to translate.
/// </summary>
public static class HypothesisDiversityPolicy
{
    public static HypothesisStartPlan StartPlanFor(int index) => KotlinInterop.FloorMod(index, 8) switch
    {
        0 or 4 => new HypothesisStartPlan(HypothesisStartMode.Baseline, 0),
        1 => new HypothesisStartPlan(HypothesisStartMode.DayRepair, 1),
        2 => new HypothesisStartPlan(HypothesisStartMode.StaffRepair, 1),
        3 => new HypothesisStartPlan(HypothesisStartMode.MixedRepair, 1),
        5 => new HypothesisStartPlan(HypothesisStartMode.DayRepair, 2),
        6 => new HypothesisStartPlan(HypothesisStartMode.StaffRepair, 2),
        _ => new HypothesisStartPlan(HypothesisStartMode.MixedRepair, 2),
    };

    /// <summary>
    /// Long AUTO runs use an actual heterogeneous portfolio instead of eight RSI++ clones.
    /// [3.284.0/Kotlin原本] AUTO の二重分岐を解消: 旧 31-90秒帯は ALNS で、アプリ経路の
    /// V6FinalPort.optimizationPlan（31-210秒=RSI(2/3)→ALNS(1/3) の複合）と食い違い、直接APIだけ
    /// 別アルゴリズムになっていた。単一アルゴリズムしか表現できない本関数では複合プランの主段=RSI
    /// （偶数ラウンドで内部的に ALNS も回る）へ寄せ、帯を 31-210=RSI に統一する。
    /// </summary>
    public static V6Algorithm AutoAlgorithmForBudget(int budgetSec) => budgetSec switch
    {
        <= 30 => V6Algorithm.V5,
        <= 210 => V6Algorithm.Rsi,
        _ => V6Algorithm.Portfolio,
    };

    /// <summary>Reservoir-sampling tie break: every tied candidate has equal probability.</summary>
    public static bool TakeReservoirTie(int tieCount, JavaRandom rng) =>
        tieCount > 0 && rng.NextInt(tieCount) == 0;
}
