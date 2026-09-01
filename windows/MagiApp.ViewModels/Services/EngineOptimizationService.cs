using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Services;

/// <summary>
/// <see cref="IOptimizationService"/> の既定実装。<c>MagiEngine</c> の実エンジンをそのまま呼ぶだけの
/// 薄いラッパー（<see cref="IOptimizationService"/> のクラスKDoc参照）。
/// </summary>
public sealed class EngineOptimizationService : IOptimizationService
{
    public Task<V6FinalPort.ActionResult> OptimizeAsync(
        MagiState state,
        int[][] schedule,
        int secondsRaw,
        int? workers,
        bool softPolish,
        V6Algorithm requestedAlgorithm,
        bool allowImpossible,
        Action<string, ViolationReport?, long, long>? onProgress,
        CancellationToken cancellationToken) =>
        V6FinalPort.HandleOptimize(
            state,
            secondsRaw,
            schedule,
            workers,
            softPolish,
            requestedAlgorithm,
            allowImpossible,
            onProgress,
            cancellationToken);

    public Task<int[][]> SoftPolishAsync(
        MagiState state,
        int[][] schedule,
        int seconds,
        CancellationToken cancellationToken) =>
        V6NativeOptimizer.SoftPolishOnly(state, schedule, seconds, cancellationToken: cancellationToken);
}
