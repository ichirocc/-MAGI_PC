using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>[Android 3.509.4 移植] 最適化・自動修正の前後比較（完了表示用）: 変更した職員数・セル数、希望固定の充足、個人回数が全員範囲内か。</summary>
public sealed record ChangeSummary(int ChangedStaff, int ChangedCells, int WishKept, int WishTotal, bool RangeAllOk)
{
    public string Line() => $"変更 {ChangedStaff}人・{ChangedCells}セル／希望 {WishKept}/{WishTotal}／個人回数 " + (RangeAllOk ? "全員範囲内" : "範囲外あり");

    public static ChangeSummary Of(MagiState state, int[][] before, int[][] after, ViolationReport report)
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
        return new ChangeSummary(staff, cells, wishKept, wishTotal, rangeOk);
    }
}
