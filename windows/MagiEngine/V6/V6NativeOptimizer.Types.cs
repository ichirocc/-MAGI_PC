using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>V6Algorithm</c> enum (declared in <c>V6NativeOptimizer.kt</c>).
/// Web V6 chooses V5 / ALNS / RSI / RSI++ by budget and then runs post-passes (HF66/HF67/HF80
/// family). AUTO chooses an algorithm by time budget, V5 is parallel SA, ALNS uses destroy/repair
/// multi-restart, RSI focuses on the currently most violated family, RSI++ chains
/// seed -&gt; hypothesis -&gt; refine -&gt; polish, and PORTFOLIO runs an adaptive heterogeneous
/// ensemble (phase 5d scope).
/// </summary>
public enum V6Algorithm { Auto, V5, Alns, Rsi, RsiPlus, Portfolio }

/// <summary>
/// Faithful port of Kotlin's <c>OpSelectMode</c> enum. ALNS の演算子選択方式。ROULETTE=重み比例
/// (従来) / THOMPSON=Thompson sampling(平滑報酬opWを事後平均、時間減衰ノイズで探索する確率的選択。
/// 停滞しにくく不確実性下で原理的)。
/// </summary>
public enum OpSelectMode { Roulette, Thompson }

/// <summary>Faithful port of Kotlin's <c>V6OptimizerOptions</c> data class.</summary>
public sealed record V6OptimizerOptions(
    V6Algorithm Algorithm = V6Algorithm.Auto,
    int TotalBudgetSec = 300,
    /// <summary>
    /// [computed default, Kotlin: <c>Runtime.getRuntime().availableProcessors().coerceIn(1, 8)</c>]
    /// Same nullable-parameter-plus-computed-property accommodation as <see cref="SaParams.Workers"/>/
    /// <see cref="SaParams.EffectiveWorkers"/> (C# record positional-parameter defaults must be
    /// compile-time constants, unlike Kotlin's freely-re-evaluated default expressions). Every
    /// Kotlin call site that reads <c>options.workers</c> is ported here as
    /// <see cref="EffectiveWorkers"/>, never this raw property.
    /// </summary>
    int? Workers = null,
    bool SoftPolish = true,
    int Restarts = 2,
    long Seed = 0L,
    /// <summary>[HF528/532移植] RectSwap2/C1BlockN を RSI 系へ伝播。Web optFlags.rectSwap 既定ON(HF532 恒久ON確定)。</summary>
    bool RectSwap = true,
    /// <summary>
    /// Run the final HF80 epilogue polish inside Optimize(). Set false when the caller
    /// (e.g. V6FinalPort.HandleOptimize) runs its own post-optimization chain, to avoid
    /// polishing twice. Direct callers keep the default so they still get a polish.
    /// </summary>
    bool PostPolish = true,
    /// <summary>
    /// [HF290 役割分担移植] 探索/精製の温度・摂動倍率。1.0=ベースライン(従来)。&gt;1=探索(高温/大摂動)、
    /// &lt;1=精製(低温)。並列仮説ごとに別の値を割当てて多様化（W0は常に1.0でベースライン保持＝退化防止）。
    /// </summary>
    double Explore = 1.0,
    /// <summary>ALNS の受理基準。並列仮説の一部に Great Deluge を割当てて受理戦略を多様化（W0は SA でベースライン保持）。</summary>
    AcceptMode Accept = AcceptMode.Sa,
    /// <summary>ALNS の演算子選択方式。並列仮説の一部に Thompson sampling を割当てて選択戦略を多様化（W0は Roulette でベースライン保持）。</summary>
    OpSelectMode OpSelect = OpSelectMode.Roulette,
    /// <summary>
    /// 局所移動に短期Tabu記憶を適用（直近変更セルの即時復帰を tenure 期間禁止。global最良更新時は
    /// アスピレーションで解禁）。並列仮説の一部にのみ割当て（W0はOFFでベースライン保持）。
    /// destroy/repair等の大近傍手は対象外。
    /// </summary>
    bool Tabu = false)
{
    public int EffectiveWorkers => Workers ?? Math.Clamp(Environment.ProcessorCount, 1, 8);
}

/// <summary>Faithful port of Kotlin's <c>V6OptimizerResult</c> data class.</summary>
public sealed record V6OptimizerResult(
    int[][] Schedule,
    ViolationReport Report,
    V6Algorithm Algorithm,
    IReadOnlyList<MirrorLog> PhaseLogs,
    long Iterations,
    long ElapsedMs,
    // [3.335.0/外部レビュー P1, Kotlin原本] 以下は**この実行の成果物**。旧実装は `lastAlternatives` 等の
    //   可変 static を呼び出し側が返却後に読んでいたため、実行が重なると別の実行の値を読み得た。
    //   採用盤面は元から返り値で流れるので**誤った勤務表にはならない**が、「他の案」「残存分析」
    //   「ライブ表示」が混ざり得た。
    IReadOnlyList<int[][]>? Alternatives = null,
    IReadOnlySet<string>? InfeasibleFamilies = null)
{
    public IReadOnlyList<int[][]> Alternatives { get; init; } = Alternatives ?? Array.Empty<int[][]>();
    public IReadOnlySet<string> InfeasibleFamilies { get; init; } = InfeasibleFamilies ?? new HashSet<string>();

    /// <summary>
    /// [Kotlin原本] 同上。<c>AdaptiveElite</c> は internal なので本体プロパティとして持つ
    /// （<c>copy()</c> は引き継がない＝作った側が明示的に載せる）— phase 5d/5e scope。C# の record
    /// の <c>with</c> 式は既定で全プロパティをコピーするため、Kotlin の「copy() は引き継がない」という
    /// 意図的な非対称は phase 5d で <c>AdaptiveElite</c> の実体を導入する際に、このプロパティを
    /// record の外側（mutable な通常プロパティのまま、<c>init</c> を付けない）に保つことで再現する。
    /// </summary>
    public object? FusionElites { get; set; }
}
