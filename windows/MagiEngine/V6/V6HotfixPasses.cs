namespace MagiEngine.V6;

/// <summary>
/// [V6HotfixPasses / フェーズ6 移植元・骨格] <c>V6HotfixPasses.kt</c>（4,682行・単一 Kotlin <c>object</c>）の
/// C# 移植先。
///
/// Kotlin原本は後処理研磨統括（<c>RunPostOptimization</c>、HF80→HF67→HF66→HF70 ＋族別 <c>Apply*Polish</c>
/// パス約20種）を単一 <c>object</c> に収めている。移植計画（フェーズ6）の方針どおり、C# 側は
/// <c>partial class</c> で族ごとに複数ファイルへ分割する（<c>V6NativeOptimizer</c> の
/// <c>.Alns.cs</c>/<c>.Portfolio.cs</c>/<c>.Repair.cs</c> 等と同じ手法）。
///
/// このファイルは<b>骨格のみ</b>: <see cref="C1TemporalFlowPolish"/> の戻り値型として必要な
/// <see cref="CyclicSwapResult"/> だけを先出しする。<c>Apply*Polish</c> 本体・<c>RunPostOptimization</c>・
/// <c>HF80Result</c>/<c>HF67Result</c>/<c>HF66Result</c>/<c>HF70Result</c>/<c>V6PostOptimizationResult</c>
/// （いずれも Kotlin原本ではファイル冒頭のトップレベル <c>data class</c>）は、対応する Apply* パスを
/// 移植する際に順次このファイル・関連 partial ファイルへ追加する。
/// </summary>
public static partial class V6HotfixPasses
{
    /// <summary>
    /// 同日/複数職員の割当を入れ替える系の研磨パス共通の戻り値。
    /// [Kotlin原本] <c>object V6HotfixPasses</c> の入れ子 <c>data class</c>。
    /// </summary>
    public sealed record CyclicSwapResult(
        int[][] NewSchedule,
        int BeforeTotal,
        int AfterTotal,
        int Applied,
        IReadOnlyList<MirrorLog> Logs,
        /// <summary>
        /// [C1 頭打ちの構造化診断, 3.322.0] <c>ApplyC1WindowPolish</c> だけが設定する。
        /// 他パスは null のまま（既定値つき＝既存の構築サイトは非破壊）。
        /// </summary>
        C1PlateauDiagnosis? Plateau = null,
        /// <summary>
        /// [3.323.0] 厳密ピン(lo==hi)を崩すため却下した候補の数。
        /// これらは<b><c>isBetter</c> が採用を認めた</b>手で、ピンのガードだけが止めている＝
        /// 「回数固定を緩めれば通ったはずの手」の実測値（推測ではない）。
        /// </summary>
        int ObservedPinBlockedAttempts = 0,
        /// <summary>[3.326.0] どのピン(職員,シフト)が何回止めたか。緩和対象の提示に使う。</summary>
        PinBlockAttribution? PinBlocks = null,
        /// <summary>[Iteration 2] このパスが単独では不採用にし、結合にも使わなかった候補（違反起点修復の材料）。</summary>
        IReadOnlyList<CombinatorialRepair.Candidate>? RejectedCandidates = null);
}
