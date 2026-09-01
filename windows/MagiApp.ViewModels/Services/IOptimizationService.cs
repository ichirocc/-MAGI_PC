using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Services;

/// <summary>
/// [フェーズ9, Services/UseCases/DI層] <see cref="MagiViewModel"/> から <c>MagiEngine</c> の最適化
/// エンジン呼び出しを切り離す境界。既定実装（<see cref="EngineOptimizationService"/>）は
/// <see cref="V6FinalPort.HandleOptimize"/>/<see cref="V6NativeOptimizer.SoftPolishOnly"/> をそのまま
/// 呼ぶだけの薄いラッパーで、エンジンの挙動・シグネチャは一切変えない（HF77＝逐語移植の対象外の
/// 純粋な配線層）。テストが実際の探索（数百ms〜数百秒）を待たずに <see cref="MagiViewModel"/> の
/// 呼出順序・keep-best判定・UI反映だけを検証できるよう、フェイク実装を注入できる形にするために導入した。
///
/// [このインターフェースの粒度] Kotlin原本 <c>MagiViewModel.kt</c> は <c>V6FinalPort</c>/
/// <c>V6NativeOptimizer</c> の静的関数を直接呼ぶ（サービス層が無い）。この移植でユーザーが明示的に
/// Services/UseCases/DI層の導入を選んだため、<c>runV6FullOptimize</c>/<c>runSoftPolish</c> が使う
/// 2つのエンジン入口だけをこの境界に切り出す——<c>HandleCheck</c>/<c>HandleSmartInitial</c> 等の
/// 他の入口は既存ピース（<c>RefreshCheck</c> 等、フェーズ9ピース7で移植済み）が既に直接呼んでおり、
/// 動いている経路を今回のリファクタで揺らさない（最小差分の原則）。
/// </summary>
public interface IOptimizationService
{
    /// <summary>
    /// <see cref="V6FinalPort.HandleOptimize"/> への薄い委譲。パラメータ・戻り値は完全に同一
    /// （このインターフェースはシグネチャを一切変えない）。
    /// </summary>
    Task<V6FinalPort.ActionResult> OptimizeAsync(
        MagiState state,
        int[][] schedule,
        int secondsRaw,
        int? workers,
        bool softPolish,
        V6Algorithm requestedAlgorithm,
        bool allowImpossible,
        Action<string, ViolationReport?, long, long>? onProgress,
        CancellationToken cancellationToken);

    /// <summary><see cref="V6NativeOptimizer.SoftPolishOnly"/> への薄い委譲。</summary>
    Task<int[][]> SoftPolishAsync(
        MagiState state,
        int[][] schedule,
        int seconds,
        CancellationToken cancellationToken);
}
