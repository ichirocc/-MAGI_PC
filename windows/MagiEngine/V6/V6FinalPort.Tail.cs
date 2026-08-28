using System.Globalization;
using System.Linq;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース17] <c>V6FinalPort.kt</c> の残る小ヘルパー4件（995-1033行、<c>handleOptimize</c>
/// より後ろにテキスト上置かれるが本体には依存しない）: <see cref="CovUBlockedAmount"/>/
/// <see cref="CovUStructuralWall"/>/<see cref="FmtIter"/>/<see cref="CheckResultWorse"/>。
/// これで <c>handleOptimize</c> 自身（252-994行・piece 18/19）を除く <c>V6FinalPort.kt</c> の
/// 全メンバが移植済みとなる。
///
/// <see cref="CheckResultWorse"/> は本ファイル唯一の <c>public</c> メンバ（Kotlin原本も同様に
/// 唯一 <c>internal</c> でない=デフォルト可視性のメンバ）。keep-best の比較順（hard→weightedScore→
/// total）は <c>MirrorCore.betterReport</c>/<c>reportComparator</c> 相当（このC#移植では
/// <c>ViolationChecker.cs</c>、フェーズ3）と同じ順序で、3.287.0のコメントが明記するとおり
/// 「weighted改善・total悪化の正当な取引」を誤って悪化判定しないための順序（hard>=ガードは両clauseで維持）。
/// </summary>
public static partial class V6FinalPort
{
    /// <summary>
    /// [3.377.0] 残存 covU のうち「充足不可能」または「いまの希望・盤面のままでは埋められないと実証済み」の合計。
    /// </summary>
    internal static int CovUBlockedAmount(CoverageDiagnosis diag) =>
        diag.Shortfalls
            .Where(s => s.Verdict == CoverageVerdict.Infeasible || s.BlockedNow)
            .Sum(s => s.Miss);

    /// <summary>
    /// [3.377.0] 残存 covU のうち「もう直せない」ぶん。
    ///
    /// 旧実装は <c>hardFloor</c>（有資格者数ベースの静的下限）しか見ておらず、実データでいちばん多い
    /// 「担当者は足りるが**いまの希望・禁止連続では埋められない**」枠を丸ごと「まだ狙える」へ入れていた。
    /// 供給不足（floor）と いま埋められない（blocked）の**どちらか大きいほう**を壁として扱う
    /// （blocked は verdict=Infeasible も含むので通常は floor を包含する。両方0なら壁なし＝従来どおり全部 open）。
    /// </summary>
    internal static int CovUStructuralWall(int covUNow, int hardFloor, int blockedMiss)
    {
        if (covUNow <= 0) return 0;
        var floorPart = hardFloor > 0 && covUNow <= hardFloor ? covUNow : 0;
        return Math.Min(covUNow, Math.Max(floorPart, Math.Max(blockedMiss, 0)));
    }

    /// <summary>[3.375.0] 反復数を読める形に（例: 54476513 → 5,447万）。停滞ログで桁を一目で掴むため。</summary>
    internal static string FmtIter(long n) => n switch
    {
        < 0 => "?",
        < 10_000 => $"{n}回",
        < 100_000_000 => $"{(n / 10_000).ToString("N0", CultureInfo.InvariantCulture)}万回",
        _ => $"{(n / 100_000_000).ToString("N0", CultureInfo.InvariantCulture)}億回",
    };

    /// <summary>
    /// [3.287.0 keep-best統一] 判定順を hard→weightedScore→total へ（<c>betterReport</c> と同順）。
    /// 旧: total が第2キーで、weighted改善・total悪化の正当な結果（重い族を直し軽い族を差し出す取引）まで
    /// 「違反総数が悪化」として入力へ復帰させ得た。weighted を第2キーに昇格し、total は weighted 非改善時のみ判定。
    /// hard>= ガード（HARD改善結果を誤って悪化判定しない）は両clauseで維持。
    /// </summary>
    public static string? CheckResultWorse(ViolationReport? before, ViolationReport after)
    {
        if (before is null) return null;
        if (after.Hard > before.Hard) return $"HARDが悪化しました: {before.Hard} -> {after.Hard}";
        if (after.Hard >= before.Hard && after.WeightedScore > before.WeightedScore)
            return $"重み付きスコアが悪化しました: {(long)before.WeightedScore} -> {(long)after.WeightedScore}";
        if (after.Hard >= before.Hard && after.WeightedScore >= before.WeightedScore && after.Total > before.Total)
            return $"違反総数が悪化しました: {before.Total} -> {after.Total}";
        return null;
    }
}
