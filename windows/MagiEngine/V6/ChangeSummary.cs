using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>[Android 3.509.4/3.510.3 移植] 最適化・自動修正の前後比較（完了表示用）: 変更した職員数・セル数、希望固定の充足、個人回数が全員範囲内か、族別の増減。</summary>
public sealed record ChangeSummary(int ChangedStaff, int ChangedCells, int WishKept, int WishTotal, bool RangeAllOk, IReadOnlyDictionary<string, int> FamilyDeltas)
{
    /// <summary>完了カード 1 行目。例: 「変更 4人・7セル／希望 42/42／個人回数 全員範囲内」</summary>
    public string Line() => $"変更 {ChangedStaff}人・{ChangedCells}セル／希望 {WishKept}/{WishTotal}／個人回数 " + (RangeAllOk ? "全員範囲内" : "範囲外あり");

    /// <summary>完了カード 2 行目（族名は <paramref name="label"/> で表示語へ）。例: 「改善 回避の並び -2・期間の制約 -1（重み 90）／悪化 曜日の偏り +3（重み 3）」</summary>
    public string FamilyLine(Func<string, string>? label = null) => FamilyLine(FamilyDeltas, label);

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
            if (p.WishLocked(i, j)) { wishTotal++; if (i < after.Length && j < after[i].Length && after[i][j] == p.Wish[i][j]) wishKept++; }
        var rangeOk = report.Breakdown.GetValueOrDefault("low") == 0 && report.Breakdown.GetValueOrDefault("high") == 0;
        var deltas = DeltasOf(beforeReport ?? UnifiedViolationChecker.Check(state, before), report);
        return new ChangeSummary(staff, cells, wishKept, wishTotal, rangeOk, deltas);
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

    /// <summary>改善（減った族）と悪化（増えた族）を重み×増減の大きい順に並べ、それぞれ重み付き合計を添える。</summary>
    public static string FamilyLine(IReadOnlyDictionary<string, int> deltas, Func<string, string>? label = null)
    {
        label ??= k => k;
        string Part(string title, int sign)
        {
            var items = deltas.Where(kv => kv.Value * sign > 0)
                .OrderByDescending(kv => Math.Abs(kv.Value) * MirrorKeys.WeightOf(kv.Key)).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            if (items.Count == 0) return $"{title} なし";
            var weighted = items.Sum(kv => Math.Abs(kv.Value) * MirrorKeys.WeightOf(kv.Key));
            return $"{title} " + string.Join("・", items.Select(kv => $"{label(kv.Key)} {(kv.Value < 0 ? "-" : "+")}{Math.Abs(kv.Value)}")) + $"（重み {(long)weighted}）";
        }
        return Part("改善", -1) + "／" + Part("悪化", +1);
    }
}
