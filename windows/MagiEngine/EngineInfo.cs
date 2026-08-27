namespace MagiEngine;

/// <summary>
/// フェーズ0の足場マーカー。フェーズ1以降、<c>Model/</c>（MagiState・JSON往復）と
/// <c>V6/</c>（Problem・ViolationChecker・Evaluator・DeltaEvaluator・探索統括・後処理研磨）
/// に実体が入る。ここは意図的にプラットフォーム非依存（WinUI/Windows App SDK 参照なし）に保つ。
/// </summary>
public static class EngineInfo
{
    /// <summary>
    /// 移植元（Kotlin, magi7ichiro-fork）のバージョン表記に対応するマーカー。
    /// 移植が進むごとに、対応する Kotlin 側バージョンをここへ記録する。
    /// </summary>
    public const string PortedFromVersion = "3.467.1";
}
