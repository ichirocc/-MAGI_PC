namespace MagiEngine.V6;

/// <summary>
/// [3.727.109] シフトの間隔（族 gap、SOFT。Kotlin <c>GapRule.kt</c>）。行の中で x が続く日をひとかたまりとし、かたまりの最後の日から
/// 次のかたまりの最初の日までの日数差が n 未満なら、足りない日数（n − 差）を数える。月の最初のかたまりは前月を持たないので数えない。
/// </summary>
public static class GapRule
{
    public static int Deficit(int[] row, int x, int n)
    {
        int sum = 0, lastEnd = -1, j = 0, t = row.Length;
        while (j < t)
        {
            if (row[j] != x) { j++; continue; }
            if (lastEnd >= 0) { int g = j - lastEnd; if (g < n) sum += n - g; }
            while (j < t && row[j] == x) j++;
            lastEnd = j - 1;
        }
        return sum;
    }

    /// <summary>違反しているかたまりの最初の日（違反箇所の印）。</summary>
    public static void ViolatingStarts(int[] row, int x, int n, Action<int> output)
    {
        int lastEnd = -1, j = 0, t = row.Length;
        while (j < t)
        {
            if (row[j] != x) { j++; continue; }
            if (lastEnd >= 0 && j - lastEnd < n) output(j);
            while (j < t && row[j] == x) j++;
            lastEnd = j - 1;
        }
    }
}
