namespace MagiEngine.V6;

/// <summary>
/// 違反の同一性（Kotlin <c>ViolationIdentity.kt</c>、3.663.0、違反セルの半自動修正仕様 §9）。セルの違反＝種類＋職員＋日付、
/// 被覆の違反＝種類＋シフト＋日付、回数の違反＝種類＋職員＋シフト。族の件数だけでは、A さんの違反を解消して B さんに同じ種類の
/// 違反を作る手を見分けられない＝場所ごとの鍵で前後を比べる。
/// </summary>
public sealed record ViolationKey(string Family, ViolationKey.Kind Space, int A, int B)
{
    /// <summary>Cell＝(職員, 日)、Need＝(シフト, 日)、Count＝(職員, シフト)。</summary>
    public enum Kind { Cell, Need, Count }
    public bool Hard => MirrorKeys.Hard.Contains(Family);
}

public static class ViolationIdentity
{
    /// <summary>報告に載っている違反の鍵（重なった族も全部。aptLow/aptHigh は apt）。</summary>
    public static HashSet<ViolationKey> Keys(ViolationReport r, bool hardOnly = false)
    {
        var outSet = new HashSet<ViolationKey>();
        void Add(IReadOnlyDictionary<string, IReadOnlyList<string>>? m, ViolationKey.Kind space)
        {
            if (m is null) return;
            foreach (var (pos, classes) in m)
            {
                var comma = pos.IndexOf(',');
                if (comma < 0 || !int.TryParse(pos[..comma], out var a) || !int.TryParse(pos[(comma + 1)..], out var b)) continue;
                foreach (var cls in classes)
                {
                    var fam = cls.StartsWith("vio-", StringComparison.Ordinal) ? cls[4..] : cls;
                    if (fam is "aptLow" or "aptHigh") fam = "apt";
                    var k = new ViolationKey(fam, space, a, b);
                    if (!hardOnly || k.Hard) outSet.Add(k);
                }
            }
        }
        Add(r.CellFamilies, ViolationKey.Kind.Cell);
        Add(r.NeedFamilies, ViolationKey.Kind.Need);
        Add(r.CountFamilies, ViolationKey.Kind.Count);
        return outSet;
    }

    /// <summary>前後の差。Resolved＝前にあって後に無い、Appeared＝後で新たに生まれた。</summary>
    public sealed record Diff(IReadOnlySet<ViolationKey> Resolved, IReadOnlySet<ViolationKey> Appeared);

    public static Diff Of(ViolationReport before, ViolationReport after)
    {
        var b = Keys(before); var a = Keys(after);
        return new Diff(b.Except(a).ToHashSet(), a.Except(b).ToHashSet());
    }

    /// <summary>後で新たに生まれた必須違反の鍵（件数が減っていても、別の職員・日・枠に生まれたものは数える）。</summary>
    public static HashSet<ViolationKey> NewHard(ViolationReport before, ViolationReport after)
    {
        var a = Keys(after, hardOnly: true);
        a.ExceptWith(Keys(before, hardOnly: true));
        return a;
    }

    /// <summary>利用者に見せる 1 手（改善案）の境界の規則: 必須の族の件数が増えるか、別の場所に必須違反が新たに生まれる手は通さない。</summary>
    public static bool IntroducesHardViolation(ViolationReport before, ViolationReport after) =>
        UnifiedViolationChecker.NewHardFamilyViolation(before, after) != null || NewHard(before, after).Count > 0;
}
