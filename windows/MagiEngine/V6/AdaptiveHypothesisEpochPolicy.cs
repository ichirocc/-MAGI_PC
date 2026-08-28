namespace MagiEngine.V6;

/// <summary>Role used by one hypothesis during one adaptive portfolio epoch.</summary>
public enum HypothesisEpochRole
{
    BaselineRefine,
    EliteRelink,
    DayBlockAlns,
    HardFamilyRsi,
    HardDebtRsiPlus,
    LargeDestroyAlns,
    PersonalRsi,
    MaxDistanceRsiPlus,
}

/// <summary>
/// Faithful port of Kotlin's <c>HypothesisEpochAssignment</c> data class.
///
/// [3.278.0/デッドコード除去, Kotlin原本] <c>safetyFloor</c> フィールドは計算されるだけで本番で一度も
/// 読まれなかった（テスト assert のみ）。W0/W4 の安全床という設計意図は
/// <see cref="AdaptiveHypothesisEpochPolicy.AssignmentFor"/> の role 分岐(slot==0/4)自体が担うため、
/// そちらは意図的に移植していない。
/// </summary>
public sealed record HypothesisEpochAssignment(HypothesisEpochRole Role, V6Algorithm Algorithm, int Intensity);

/// <summary>
/// Faithful port of Kotlin's <c>AdaptiveHypothesisEpochPolicy.kt</c> (161 lines, entirely
/// self-contained pure logic — no coroutines/MagiState/I-O). Convergence-aware role/quantum/seed
/// scheduling policy consumed by <c>V6NativeOptimizer.RunAdaptivePortfolio</c> (phase 5d).
///
/// W0 is the permanent safety floor. W4 starts as a second precision worker and changes to
/// elite path relinking after its first plateau. The other six workers rotate through six
/// genuinely different escape roles whenever they stop improving or collapse onto another
/// worker's basin.
/// </summary>
public static class AdaptiveHypothesisEpochPolicy
{
    public const int BASE_QUANTUM_SEC = 5;
    public const int IMPROVING_QUANTUM_SEC = 8;
    public const int RSI_PLUS_BASE_QUANTUM_SEC = 35;
    public const int RSI_PLUS_IMPROVING_QUANTUM_SEC = 45;
    public const int DUPLICATE_DISTANCE_CELLS = 2;

    private static readonly HypothesisEpochRole[] EscapeRoles =
    {
        HypothesisEpochRole.DayBlockAlns,
        HypothesisEpochRole.HardFamilyRsi,
        HypothesisEpochRole.HardDebtRsiPlus,
        HypothesisEpochRole.LargeDestroyAlns,
        HypothesisEpochRole.PersonalRsi,
        HypothesisEpochRole.MaxDistanceRsiPlus,
    };

    private static int BaseEscapeOffset(int index) => KotlinInterop.FloorMod(index, 8) switch
    {
        1 => 0,
        2 => 1,
        3 => 2,
        5 => 3,
        6 => 4,
        7 => 5,
        _ => 0,
    };

    public static HypothesisEpochAssignment AssignmentFor(int index, int reassignments)
    {
        var slot = KotlinInterop.FloorMod(index, 8);
        var role = slot switch
        {
            0 => HypothesisEpochRole.BaselineRefine,
            4 when reassignments == 0 => HypothesisEpochRole.BaselineRefine,
            4 => HypothesisEpochRole.EliteRelink,
            _ => EscapeRoles[KotlinInterop.FloorMod(BaseEscapeOffset(slot) + reassignments, EscapeRoles.Length)],
        };
        return new HypothesisEpochAssignment(role, AlgorithmFor(role), IntensityFor(role, reassignments));
    }

    public static V6Algorithm AlgorithmFor(HypothesisEpochRole role) => role switch
    {
        HypothesisEpochRole.DayBlockAlns or HypothesisEpochRole.LargeDestroyAlns => V6Algorithm.Alns,
        HypothesisEpochRole.HardFamilyRsi or HypothesisEpochRole.PersonalRsi => V6Algorithm.Rsi,
        HypothesisEpochRole.BaselineRefine or HypothesisEpochRole.EliteRelink
            or HypothesisEpochRole.HardDebtRsiPlus or HypothesisEpochRole.MaxDistanceRsiPlus => V6Algorithm.RsiPlus,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>
    /// [3.308.0] 次のエポックが「改善直後」の長い量子（8/45 秒）を受け取れるか。
    ///
    /// **役割が変わった直後は受け取れない**。新しい役割はまだ何も証明していないのに、前の役割が
    /// 稼いだ改善で 35→45 秒（RSI+）へ昇格するのは根拠が無い。契約に名前を付けて呼出側から使う
    /// （インライン式のままだと呼出サイトごとにずれる。3.306.0 の制御器経路が実際にずれた実績＝
    /// 経路自体は 3.409.21 で削除済み）。
    /// </summary>
    public static bool CarriesImprovingQuantum(bool improvedThisEpoch, bool roleChanged) =>
        improvedThisEpoch && !roleChanged;

    public static int IntensityFor(HypothesisEpochRole role, int growthBasis)
    {
        // 2回失敗してから強度を上げる。最初の停滞は「別の考え方を試す」段階であって、
        // いきなり最大近傍へ残予算を注ぐ段階ではない。負値は呼出側の想定外なので 0 へ丸める。
        var growth = Math.Min(Math.Max(growthBasis, 0) / 2, 3);
        var baseIntensity = role switch
        {
            HypothesisEpochRole.BaselineRefine => 0,
            HypothesisEpochRole.EliteRelink => 1,
            HypothesisEpochRole.DayBlockAlns or HypothesisEpochRole.HardFamilyRsi or HypothesisEpochRole.PersonalRsi => 1,
            HypothesisEpochRole.HardDebtRsiPlus => 2,
            HypothesisEpochRole.LargeDestroyAlns or HypothesisEpochRole.MaxDistanceRsiPlus => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        return baseIntensity + growth;
    }

    /// <summary>
    /// A plateau is not a stop condition. It requests another role. W0 never leaves the safety
    /// floor. W4 changes to path relinking after one plateau. Duplicate basins are reassigned even
    /// when their scalar score happened to improve, because diversity is a separate invariant.
    /// </summary>
    public static bool ShouldReassign(int index, bool improvedThisEpoch, int stagnantEpochs, int nearestOtherDistance)
    {
        var slot = KotlinInterop.FloorMod(index, 8);
        if (slot == 0) return false;
        if (nearestOtherDistance <= DUPLICATE_DISTANCE_CELLS) return true;
        return !improvedThisEpoch && stagnantEpochs >= 1;
    }

    public static int NextStagnantEpochs(int previous, bool improvedThisEpoch) =>
        improvedThisEpoch ? 0 : previous + 1;

    public static int QuantumSeconds(HypothesisEpochAssignment assignment, bool improvedPreviousEpoch, int remainingSeconds)
    {
        if (remainingSeconds <= 0) return 0;
        var requested = assignment.Algorithm == V6Algorithm.RsiPlus
            ? (improvedPreviousEpoch ? RSI_PLUS_IMPROVING_QUANTUM_SEC : RSI_PLUS_BASE_QUANTUM_SEC)
            : (improvedPreviousEpoch ? IMPROVING_QUANTUM_SEC : BASE_QUANTUM_SEC);
        return Math.Max(Math.Min(requested, remainingSeconds), 1);
    }

    public static long EpochSeed(long baseSeed, int index, int epoch, int reassignments) =>
        baseSeed
        ^ ((index + 1L) * -7046029254386353131L)
        ^ ((epoch + 1L) * 0x2545F4914F6CDD1DL)
        ^ ((reassignments + 1L) * 0x369DEA0F31A53F85L);

    /// <summary>
    /// [3.308.0] 役割巡回に入る前の**初期配置**であることを名前で示す（値は
    /// <c>AssignmentFor(index, 0)</c> と同値）。
    /// [3.409.21] かつては既定OFFの停滞脱出制御器（StagnationEscapeController・3.306.0）の入口も
    /// 兼ねたが、単体 A/B（15ペア ON5/OFF10＝2度目の中立）を根拠に制御器ごと削除された
    /// （Kotlin原本のコメントのみ・C#側にその制御器は元々存在しない）。
    /// </summary>
    public static HypothesisEpochAssignment InitialAssignmentFor(int index) => AssignmentFor(index, 0);

    public static string RoleLabel(HypothesisEpochAssignment assignment) =>
        $"{RoleName(assignment.Role)}/x{assignment.Intensity}";

    /// <summary>
    /// [C#移植上の判断] Kotlin の <c>role.name</c>（enum の UPPER_SNAKE_CASE 名そのもの）を、
    /// <see cref="RoleLabel"/>・<c>V6NativeOptimizer.RunAdaptivePortfolio</c> の役割別集計行・
    /// エポック超過ログが直接ログ本文へ埋め込む。C# 側は enum を PascalCase 化した（このコードベース
    /// 全体の規約: <c>V6Algorithm</c>/<c>AcceptMode</c>/<c>OpSelectMode</c>/<c>HypothesisStartMode</c>
    /// と同じ）ため、逆写像でログ文字列だけ Kotlin と一致させる（診断ログの grep 互換性を保つ）。
    /// </summary>
    public static string RoleName(HypothesisEpochRole role) => role switch
    {
        HypothesisEpochRole.BaselineRefine => "BASELINE_REFINE",
        HypothesisEpochRole.EliteRelink => "ELITE_RELINK",
        HypothesisEpochRole.DayBlockAlns => "DAY_BLOCK_ALNS",
        HypothesisEpochRole.HardFamilyRsi => "HARD_FAMILY_RSI",
        HypothesisEpochRole.HardDebtRsiPlus => "HARD_DEBT_RSI_PLUS",
        HypothesisEpochRole.LargeDestroyAlns => "LARGE_DESTROY_ALNS",
        HypothesisEpochRole.PersonalRsi => "PERSONAL_RSI",
        HypothesisEpochRole.MaxDistanceRsiPlus => "MAX_DISTANCE_RSI_PLUS",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };
}
