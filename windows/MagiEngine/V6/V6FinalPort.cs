using System.Text.RegularExpressions;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Final bridge for App-level handlers (ported from Kotlin's <c>V6FinalPort</c> object).
///
/// V6 Web originally kept many behaviors inside React App methods instead of standalone worker
/// functions: <c>handleSmartInitial</c>, <c>handleCheck</c>, <c>handleOptimize</c>, busy-detail
/// construction, the impossible-wish gate, algorithm labels, and post-optimization HF80/HF67/
/// HF66/HF70 chaining. The Kotlin/Android port kept this as a single object so
/// ViewModel/Compose could call the same workflow without WebView; this C# port preserves that.
///
/// [phase 4 minimal slice] Only the entry points that do not depend on the search/polish engine
/// were ported first: <see cref="BuildBusyDetail"/>, <see cref="ConfirmDespiteImpossibleWishes"/>,
/// <see cref="HandleSmartInitial"/>, <see cref="HandleCheck"/> (this file).
///
/// [phase 7 piece 6, <c>V6FinalPort.AlgorithmPlan.cs</c>] <c>MAX_OPTIMIZE_SEC</c>/<c>AlgorithmLabel</c>/
/// <c>OptimizationPlan</c>/<c>optimizationPlan</c>/<c>getAlgorithmLabel</c> are now ported (this
/// class is <c>partial</c> to accommodate that file, and future pieces of this phase).
///
/// Deliberately still NOT ported: <c>watchdogStagnationFired</c>/<c>effectiveStallMs</c>/
/// <c>normalStallMs</c> (piece 7 — depend on nothing not yet ported, but are scoped as their own
/// piece since <c>V6FinalPortTest.kt</c> has 17 dedicated Kotlin tests for them with zero existing
/// C# coverage) and <c>handleOptimize</c> itself (piece 18 — the ~740-line core orchestration,
/// which depends on <c>V6NativeOptimizer</c> (phase 5, done) and
/// <c>V6HotfixPasses.runPostOptimization</c> (phase 6, done) plus several still-unported helper
/// pieces of this same file/phase).
///
/// [async 移植上の判断] Kotlin's <c>suspend fun ... = withContext(Dispatchers.Default) { ... }</c>
/// here is purely "run this CPU-bound work off the caller's (typically UI) thread" — it is not
/// genuine asynchronous I/O. <see cref="MagiEngine"/> has no async I/O at this phase, so
/// <see cref="HandleSmartInitial"/>/<see cref="HandleCheck"/> are plain synchronous methods; a
/// future UI layer (phase 9+) can wrap a call in <c>Task.Run(...)</c> if it needs to offload from
/// its own UI thread. This does not constrain phase 5's harder problem (translating the
/// *concurrent*, cancellable, multi-worker coroutines in <c>V6NativeOptimizer</c> to TPL), which
/// is a different concern from this "just move synchronous work off-thread" pattern.
/// </summary>
public static partial class V6FinalPort
{
    /// <summary>
    /// Faithful port of Kotlin's <c>BusyDetail</c> data class. <c>Base</c> and <c>StartedAt</c>
    /// mirror Kotlin defaults that reference another parameter / call a function
    /// (<c>val base: String = algorithm</c>, <c>val startedAt: Long = System.currentTimeMillis()</c>)
    /// — values C# cannot express as compile-time-constant default *parameters*, so they are
    /// declared as body-level <c>init</c> properties whose initializers run in the primary
    /// constructor and may reference its parameters (the same technique already used for
    /// <see cref="ViolationReport"/>'s <c>CellFamilies</c>/etc. null-coalescing defaults).
    /// </summary>
    public sealed record BusyDetail(
        string Algorithm,
        string ProblemSize,
        string ConstraintCount,
        string Subtitle = "",
        string PhaseDesc = "",
        string ExpectedSec = "",
        string EstimatedIter = "",
        bool UiFrozen = false)
    {
        public string Base { get; init; } = Algorithm;
        public long StartedAt { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Faithful port of Kotlin's <c>ImpossibleWishGate</c> data class.</summary>
    public sealed record ImpossibleWishGate(bool Allowed, int Count, string Message, IReadOnlyList<MirrorLog> Logs);

    /// <summary>
    /// Faithful port of Kotlin's <c>ActionResult</c> data class.
    ///
    /// <see cref="Post"/> stays untyped (<c>object?</c>) for now: Kotlin's field type is
    /// <c>V6PostOptimizationResult?</c>, a type owned by <c>V6HotfixPasses.kt</c> (phase 6) that
    /// does not exist yet in this port. No phase-4 call site ever constructs a non-null value for
    /// it, so this is honestly "not yet modeled" rather than a guessed-at stub shape; phase 6
    /// will widen this field's type to the real port of <c>V6PostOptimizationResult</c>.
    /// </summary>
    public sealed record ActionResult(
        int[][] Schedule,
        ViolationReport Report,
        string Phase,
        BusyDetail BusyDetail,
        IReadOnlyList<MirrorLog> Logs,
        object? Post = null,
        IReadOnlyList<int[][]>? Alternatives = null)
    {
        public IReadOnlyList<int[][]> Alternatives { get; init; } = Alternatives ?? Array.Empty<int[][]>();
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyOverrides = new Dictionary<string, string>();

    /// <summary>
    /// Faithful port of Kotlin's <c>buildBusyDetail</c>. Note the "HARD"/"SOFT" labels in
    /// <c>ConstraintCount</c> are display text, not a re-derivation of the real HARD/SOFT weight
    /// split (<see cref="MirrorKeys"/> is the single source of truth for that) — e.g. cons2/
    /// cons41/cons42 are actually SOFT-weighted families but are counted under the "HARD" label
    /// here. Ported verbatim per HF77 (don't "fix" apparent inconsistencies while translating).
    /// </summary>
    public static BusyDetail BuildBusyDetail(MagiState state, string algorithm, IReadOnlyDictionary<string, string>? overrides = null)
    {
        overrides ??= EmptyOverrides;
        int n = state.StaffCount;
        int t = state.DayCount;
        int k = state.ShiftCount;
        int totalHardCons = state.Cons1.Count + state.Cons2.Count + state.Cons3.Count
            + state.Cons3n.Count + state.Cons41.Count + state.Cons42.Count;
        int totalSoftCons = state.Cons3m.Count + state.Cons3mn.Count;
        int wishCount = state.Wishes.Count;
        return new BusyDetail(
            Algorithm: algorithm,
            ProblemSize: $"{n}名 × {t}日 × {k}シフト = {n * t * k} セル",
            ConstraintCount: $"HARD {totalHardCons}件 / SOFT {totalSoftCons}件 / 希望 {wishCount}件",
            Subtitle: overrides.GetValueOrDefault("subtitle", ""),
            PhaseDesc: overrides.GetValueOrDefault("phaseDesc", ""),
            ExpectedSec: overrides.GetValueOrDefault("expectedSec", ""),
            EstimatedIter: overrides.GetValueOrDefault("estimatedIter", ""),
            // Kotlin: overrides["uiFrozen"]?.toBooleanStrictOrNull() ?: false — strict parse
            // (only the exact literal "true" ever yields true; "false"/garbage/missing -> false).
            UiFrozen: overrides.TryGetValue("uiFrozen", out var uf) && uf == "true");
    }

    private static readonly Regex ImpossibleWishCountPattern = new(@"\d+件");

    /// <summary>
    /// Faithful port of Kotlin's <c>confirmDespiteImpossibleWishes</c>. When there is at least one
    /// impossible wish, the "…詳細はSanityCheckを確認" trailer is appended iff the total impossible
    /// count exceeds the sum of counts shown across the (at most 12) displayed staff-name groups —
    /// i.e. iff more than 12 distinct staff have impossible wishes and some had to be truncated
    /// from the summary. This is computed via a regex that re-extracts each line's "N件" figure
    /// (ported verbatim rather than "simplified" to a direct group-count sum, per HF77 — the two
    /// are mathematically equivalent for any real staff name, and diverge only if a staff name
    /// itself happened to contain digits immediately followed by "件", which no fixture exercises).
    /// </summary>
    public static ImpossibleWishGate ConfirmDespiteImpossibleWishes(MagiState state, bool allowImpossible = false)
    {
        var imp = V6SanityPort.DetectImpossibleWishes(state);
        if (imp.Count == 0) return new ImpossibleWishGate(true, 0, "不可能希望なし", Array.Empty<MirrorLog>());

        var lines = imp
            .GroupBy(w => w.StaffName)
            .Take(12)
            .Select(g => $"・{g.Key}: {g.Count()}件 (" +
                string.Join(", ", g.Take(3).Select(w => $"{w.DayIndex + 1}日={w.ShiftSymbol}")) + ")")
            .ToList();

        int shownSum = 0;
        foreach (var line in lines)
        {
            var m = ImpossibleWishCountPattern.Match(line);
            if (m.Success) shownSum += KotlinInterop.ToIntOrNull(m.Value[..^1]) ?? 0;
        }

        var msg = $"不可能希望が {imp.Count}件あります。担当範囲外シフトへの希望は永久に充足できません。\n"
            + string.Join("\n", lines)
            + (imp.Count > shownSum ? "\n…詳細はSanityCheckを確認" : "");

        string level = allowImpossible ? "W" : "E";
        var logs = new MirrorLog[] { new(tag: "ImpossibleWishGate", message: msg.Replace("\n", " / "), level: level) };
        return new ImpossibleWishGate(allowImpossible, imp.Count, msg, logs);
    }

    /// <summary>
    /// [初期解生成(賢い版)] 希望シフト→C1(窓の要件)→日別必要人数→個人下限→残り埋め の順で
    /// 初期解を組み立てる <see cref="SmartInitialScheduler"/> のポート。本最適化(SA/ALNS)へは続けず、
    /// 生成した下書きをそのまま返す（続けての本最適化は phase 5 移植後の別入口が担当）。
    /// </summary>
    public static ActionResult HandleSmartInitial(MagiState state, bool allowImpossible = false)
    {
        if (state.DayCount <= 0)
            throw new ArgumentException("対象期間が無効です。終了日を開始日より後の日付にしてください");
        // [3.360.3] 期間には T>0 のガードがあるのに職員数には無く、非対称だった。S=0 は編集画面からは
        //   作れない（Ws1Ops.removeStaff が最後の1名を消さない）が、JSON/CSV 取込で外部から入りうる。
        if (state.StaffList.Count == 0)
            throw new ArgumentException("職員が1人も登録されていません。職員管理で追加してください");

        var gate = ConfirmDespiteImpossibleWishes(state, allowImpossible);
        if (!gate.Allowed) throw new InvalidOperationException(gate.Message);

        var busy = BuildBusyDetail(state, "初期解を作成中", new Dictionary<string, string>
        {
            ["subtitle"] = "初期解を作成中",
            ["phaseDesc"] = "希望シフトとC1(窓の要件)を優先し、次に必要人数・個人下限を考慮しています",
            ["expectedSec"] = "< 1 秒",
            ["estimatedIter"] = "~800 回",
        });

        var res = SmartInitialScheduler.Generate(state);
        var logs = gate.Logs
            .Concat(res.Report.Logs)
            .Append(new MirrorLog(tag: "MAGI_GenerateInitial",
                message: $"初期解生成 完了 HARD={res.Report.Hard} total={res.Report.Total}"))
            .ToList();
        return new ActionResult(res.Schedule, res.Report with { Logs = logs }, "smart_initial", busy, logs);
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>handleCheck</c>: evaluate <paramref name="schedule"/> (or the
    /// state's own schedule, if omitted) against <paramref name="state"/>'s constraints without
    /// running any search/optimization.
    /// </summary>
    public static ActionResult HandleCheck(MagiState state, int[][]? schedule = null)
    {
        schedule ??= state.Schedule.ToIntArray2D();

        var busy = BuildBusyDetail(state, "違反チェック中", new Dictionary<string, string>
        {
            ["subtitle"] = "違反チェック",
            ["phaseDesc"] = "勤務表のすべての違反を確認しています（最適化結果は変更しません）",
            ["expectedSec"] = "< 0.1 秒",
            ["estimatedIter"] = "評価のみ （反復なし）",
        });

        var report = UnifiedViolationChecker.Check(state, schedule);
        int hc = report.Hard;
        int sc = report.Soft;
        var logs = report.Logs
            .Append(new MirrorLog(tag: "UnifiedCheck",
                message: report.Total == 0 ? "違反なし ✓" : $"HARD {hc}件・品質 {sc}件"))
            .ToList();
        return new ActionResult(schedule.Copy2D(), report with { Logs = logs }, "check", busy, logs);
    }
}
