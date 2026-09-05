using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[E7] 違反 種別フィルタの 6 分類。Kotlin原本 <c>ui/VioBuckets.kt</c>（3.382.0）の逐語移植。</summary>
public sealed record VioBucket(string Key, string Label, IReadOnlySet<string> Families);

/// <summary>
/// 場所を持つ族を作成者の語彙で 6 バケツに束ね、勤務表タブ全面（セル／日ヘッダ／集計）を 1 つの共有フィルタで絞る。
/// 表示のみ・スコアリング不変。<see cref="Buckets"/> と <see cref="BucketlessFamilies"/> の和は
/// <see cref="MirrorKeys.All"/> と一致していなければならない（<c>VioBucketsTest</c> が両方向で固定）。
/// </summary>
public static class VioBuckets
{
    public static readonly IReadOnlyList<VioBucket> Buckets = new[]
    {
        new VioBucket("need", "人員", new HashSet<string> { "covU", "covO" }),
        new VioBucket("pref", "希望", new HashSet<string> { "pref" }),
        new VioBucket("seq", "連勤", new HashSet<string> { "c3", "c3n", "c3m", "c3mn" }),
        new VioBucket("count", "回数", new HashSet<string> { "low", "high", "apt", "c2" }),
        new VioBucket("group", "群ルール", new HashSet<string> { "groupViol", "c41", "c42", "c41s", "c42s" }),
        new VioBucket("window", "窓", new HashSet<string> { "c1" }),
    };

    /// <summary>セル/日の場所マップを持たない族＝絞り込みの対象外（常に表示）。</summary>
    public static readonly IReadOnlySet<string> BucketlessFamilies = new HashSet<string> { "fair", "weekly" };

    public static readonly IReadOnlySet<string> AllKeys = Buckets.Select(b => b.Key).ToHashSet();

    /// <summary>vio-class（"vio-covU"/"vio-aptLow" 等）→ 族キー。aptLow/aptHigh は apt に畳む。</summary>
    public static string FamilyOfVioClass(string cls)
    {
        var f = cls.StartsWith("vio-", StringComparison.Ordinal) ? cls["vio-".Length..] : cls;
        return f is "aptLow" or "aptHigh" ? "apt" : f;
    }

    /// <summary>族キー → バケツキー（対象外＝null）。</summary>
    public static string? BucketOfFamily(string fam) => Buckets.FirstOrDefault(b => b.Families.Contains(fam))?.Key;

    /// <summary>この違反クラスが現在のフィルタ(enabled=表示中バケツ集合)で表示されるか。バケツ対象外の族は常に表示。</summary>
    public static bool VioVisible(string? cls, IReadOnlySet<string> enabled)
    {
        if (cls is null) return false;
        var b = BucketOfFamily(FamilyOfVioClass(cls));
        return b is null || enabled.Contains(b);
    }

    /// <summary>セル("i,j")の全違反クラス（重み降順）。families 未充填の経路では最重 1 クラスへフォールバック。</summary>
    public static IReadOnlyList<string> CellVioClasses(UiState ui, string key)
    {
        if (ui.ViolationCellFamilies.TryGetValue(key, out var fams)) return fams;
        return ui.ViolationCells.TryGetValue(key, out var one) ? new[] { one } : Array.Empty<string>();
    }

    /// <summary>フィルタを通過する最重の違反クラス（最重族を OFF にしても、表示中の族が同セルに残れば枠は残る）。</summary>
    public static string? VisibleCellVio(UiState ui, string key, IReadOnlySet<string> enabled) =>
        CellVioClasses(ui, key).FirstOrDefault(c => VioVisible(c, enabled));

    /// <summary>各バケツの「違反ロケーション数」（セル/エントリ件数＝見出し『要確認 Nか所』と同単位）。</summary>
    public static IReadOnlyDictionary<string, int> BucketLocCounts(UiState ui)
    {
        var counts = new Dictionary<string, int>();
        void Tally(string b) => counts[b] = counts.GetValueOrDefault(b) + 1;
        foreach (var key in ui.ViolationCells.Keys)
        {
            foreach (var b in CellVioClasses(ui, key).Select(c => BucketOfFamily(FamilyOfVioClass(c))).OfType<string>().Distinct())
                Tally(b);
        }
        foreach (var cls in ui.NeedViolations.Values) if (BucketOfFamily(FamilyOfVioClass(cls)) is { } b) Tally(b);
        foreach (var cls in ui.CountViolations.Values) if (BucketOfFamily(FamilyOfVioClass(cls)) is { } b) Tally(b);
        return counts;
    }
}
