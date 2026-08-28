using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [C1 Repair Operators (façade) / 3.275.0] 散在していた C1 修復オペレータを図の1層として集約する薄い門。
/// 各メソッドは既存実装へ<b>1:1 委譲</b>（順序・引数・採否を一切変えず＝挙動完全同一）。狙いは
/// 「どこに C1 オペレータがあるか」を1箇所へまとめ、<see cref="C1RepairIndex"/>/<see cref="C1DeltaPrefilter"/>
/// を共有の前段として噛ませること。最終採否は各オペレータ内の <see cref="UnifiedViolationChecker"/> +
/// <see cref="V6HotfixPasses.IsBetter"/> + keep-best のまま＝スコアリング不変・退化不能。
///
/// [3.254.0 の教訓との整合] 外部提示の「オペレータを再 dispatch する統合(UnifiedC1Polish)」は退行のため
///   不採用にした。本 façade は<b>再 dispatch しない</b>（委譲のみ）ため、その教訓に反しない。
///
/// 図の対応:
///   <see cref="SelfRelocateAndSameDaySwap"/> = 自己内移設 + 同日 coverage保存 swap/permutation（手A/R1/R2/R3）
///   <see cref="TemporalFlow"/>               = Temporal DP + FlexibleDayFlow
///   <see cref="WideBeam"/>                   = 広域時空間ビーム
///   <see cref="ExactWindow"/>                = 厳密窓修復（coverage保存 permutation 分枝限定）
///   <see cref="JointLns"/>                   = Joint LNS（c1 + covU/range-low を同一 goal pool で）
/// </summary>
internal static class C1RepairOperators
{
    // ---- 共有の読取専用前段（Index / Prefilter） ----

    /// <summary>盤面に c1修復の余地（不足窓）があるか。無ければ全 c1オペレータは no-op（安全にスキップ可）。</summary>
    public static bool HasActionableC1(Problem p, int[][] schedule) =>
        C1DeltaPrefilter.HasActionableC1(C1RepairIndex.Build(p, schedule));

    /// <summary>A4: coverage入替でも解消不能と証明された窓（診断）。</summary>
    public static List<CoverageNeutralWall> ProvenWalls(Problem p, int[][] schedule) =>
        C1RepairAnalysis.ProvenWalls(p, schedule);

    // ---- オペレータ（既存実装への 1:1 委譲） ----

    /// <summary>自己内移設 + 同日 coverage保存 swap/permutation（手A/R1/R2/R3）。</summary>
    public static V6HotfixPasses.CyclicSwapResult SelfRelocateAndSameDaySwap(
        MagiState state, int[][] schedule, int maxPasses = 3,
        Func<bool>? shouldStop = null, long seed = 0x1C1L) =>
        V6HotfixPasses.ApplyC1WindowPolish(state, schedule, maxPasses, shouldStop, seed);

    /// <summary>Temporal DP + FlexibleDayFlow。</summary>
    public static V6HotfixPasses.CyclicSwapResult TemporalFlow(
        MagiState state, int[][] schedule, int maxPasses = 2, int maxRelocations = 4,
        int trials = 4, Func<bool>? shouldStop = null, long seed = 0xC1F10FL) =>
        C1TemporalFlowPolish.Apply(state, schedule, maxPasses, maxRelocations, trials, shouldStop, seed);

    /// <summary>広域時空間ビーム。</summary>
    public static V6HotfixPasses.CyclicSwapResult WideBeam(
        MagiState state, int[][] schedule, int beamWidth = 16, int maxSteps = 60,
        Func<bool>? shouldStop = null, long seed = 0x1CBEAL) =>
        V6HotfixPasses.ApplyC1BeamPolish(state, schedule, beamWidth, maxSteps, shouldStop, seed);

    /// <summary>厳密窓修復（coverage保存 permutation の分枝限定探索）。</summary>
    public static V6HotfixPasses.CyclicSwapResult ExactWindow(
        MagiState state, int[][] schedule, Config? cfg = null, Func<bool>? shouldStop = null) =>
        V6HotfixPasses.ApplyC1ExactWindowRepair(state, schedule, cfg, shouldStop);

    /// <summary>
    /// [3.276.0] index駆動の候補生成＋prefilter選別＋玉突き連鎖のC1修復（Index/Prefilterを実駆動する経路）。
    /// </summary>
    public static V6HotfixPasses.CyclicSwapResult IndexChainRepair(
        MagiState state, int[][] schedule, int maxPasses = 2,
        Func<bool>? shouldStop = null, long seed = 0x1C1D2L) =>
        V6HotfixPasses.ApplyC1IndexChainRepair(state, schedule, maxPasses, shouldStop, seed);

    /// <summary>Joint LNS（c1 + covU/range-low を同一 goal pool で）。</summary>
    public static V6HotfixPasses.CyclicSwapResult JointLns(
        MagiState state, int[][] schedule, C1JointLnsPolish.Config? config = null,
        Func<bool>? shouldStop = null, long seed = 0xC1A11L) =>
        C1JointLnsPolish.Apply(state, schedule, config, shouldStop, seed);
}
