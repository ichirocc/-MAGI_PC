using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>[Android 3.509.4/3.510.3 移植] 最適化・自動修正の前後比較（完了表示用）: 変更した職員数・セル数、希望固定の充足、個人回数が全員範囲内か、族別の増減。</summary>
/// <param name="RangeOutStaff">回数の下限・上限を外れた職員の人数（0＝数えられなかったか範囲内）。</param>
public sealed record ChangeSummary(int ChangedStaff, int ChangedCells, int WishKept, int WishTotal, bool RangeAllOk, IReadOnlyDictionary<string, int> FamilyDeltas, int RangeOutStaff = 0)
{
    /// <summary>完了カード 1 行目。例: 「変更：12人・84か所／希望どおり 42/42件」</summary>
    public string Line() => $"変更：{ChangedStaff}人・{ChangedCells}か所／希望どおり {WishKept}/{WishTotal}件";

    /// <summary>完了カード 2 行目（族名は <paramref name="label"/>、単位は <paramref name="unit"/> で表示語へ）。例: 「減った：回避の並び 2件・期間の制約 1件／増えた：曜日の偏り 3pt」</summary>
    public string FamilyLine(Func<string, string>? label = null, Func<string, string>? unit = null) => FamilyLine(FamilyDeltas, unit, label);

    public static ChangeSummary Of(MagiState state, int[][] before, int[][] after, ViolationReport report, ViolationReport? beforeReport = null)
    {
        var p = new Problem(state);
        int staff = 0, cells = 0;
        for (int i = 0; i < p.S; i++)
        {
            int c = 0;
            for (int j = 0; j < p.T; j++)
            {
                int? b = i < before.Length && j < before[i].Length ? before[i][j] : null;
                int? a = i < after.Length && j < after[i].Length ? after[i][j] : null;
                if (b != a) c++;
            }
            if (c > 0) { staff++; cells += c; }
        }
        int wishTotal = 0, wishKept = 0;
        for (int i = 0; i < p.S; i++) for (int j = 0; j < p.T; j++)
            if (p.WishFixed(i, j)) { wishTotal++; if (i < after.Length && j < after[i].Length && after[i][j] == p.Wish[i][j]) wishKept++; }
        var rangeOk = report.Breakdown.GetValueOrDefault("low") == 0 && report.Breakdown.GetValueOrDefault("high") == 0;
        var rangeOut = report.CountFamilies.Where(kv => kv.Value.Contains("vio-low") || kv.Value.Contains("vio-high"))
            .Select(kv => kv.Key.Split(',')[0]).ToHashSet().Count;
        var deltas = DeltasOf(beforeReport ?? UnifiedViolationChecker.Check(state, before), report);
        return new ChangeSummary(staff, cells, wishKept, wishTotal, rangeOk, deltas, rangeOut);
    }

    /// <summary>族別の件数増減（後 − 前。0 の族は含まない）。</summary>
    public static IReadOnlyDictionary<string, int> DeltasOf(ViolationReport before, ViolationReport after)
    {
        var result = new Dictionary<string, int>();
        foreach (var k in before.Breakdown.Keys.Concat(after.Breakdown.Keys).Distinct())
        {
            var d = after.Breakdown.GetValueOrDefault(k) - before.Breakdown.GetValueOrDefault(k);
            if (d != 0) result[k] = d;
        }
        return result;
    }

    /// <summary>減った族と増えた族を、重み×増減の大きい順に並べる（表示に重みは出さない。単位は族ごと）。</summary>
    public static string FamilyLine(IReadOnlyDictionary<string, int> deltas, Func<string, string>? unit = null, Func<string, string>? label = null)
    {
        label ??= k => k;
        unit ??= _ => "件";
        string Part(string title, int sign)
        {
            var items = deltas.Where(kv => kv.Value * sign > 0)
                .OrderByDescending(kv => Math.Abs(kv.Value) * MirrorKeys.WeightOf(kv.Key)).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            if (items.Count == 0) return $"{title}なし";
            return title + string.Join("・", items.Select(kv => $"{label(kv.Key)} {Math.Abs(kv.Value)}{unit(kv.Key)}"));
        }
        return Part("減った：", -1) + "／" + Part("増えた：", +1);
    }
}
