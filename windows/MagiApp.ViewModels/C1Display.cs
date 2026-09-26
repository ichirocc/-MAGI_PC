using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// 期間の制約（c1）の表示専用の不足区間。Kotlin <c>ui/C1Display.kt</c> の移植。職員 Staff × 規則（Shift を Day1 日のなかに Day2 日）の
/// 続けて不足した窓の和 From〜To。Marks は不足窓の中で、いま Shift でなく Shift に変えられる日（変えられる日が 1 つも無い窓の日は含めない）。
/// Stuck は変えられる日が 1 つも無い不足窓があること。チェッカーの c1 の印（ランの先頭）は探索が読むので残し、画面には描かない。
/// </summary>
public sealed record C1Shortage(int Staff, int Shift, int Day1, int Day2, int From, int To, int Windows, IReadOnlyList<int> Marks, bool Stuck)
{
    /// <summary>行の下に帯を引くか（窓が重なって続く＝1 窓より長い不足）。</summary>
    public bool Band => Windows > 1;
}

public static class C1Display
{
    /// <summary>勤務表だけでは届かないときの 1 文（セルシート・職員の内訳で共有）。</summary>
    public const string StuckText = "希望や担当の都合で、勤務表だけでは期間の約束を満たせません。";

    /// <summary>セル (i,d) を k に変えられるか。最適化器と同じ基準（希望固定なら希望どおりだけ、それ以外は MayPlace）。</summary>
    public static bool Changeable(Problem p, int i, int d, int k) =>
        p.WishLocked(i, d) ? p.LockTo(i, d) == k : p.MayPlace(i, k);

    /// <summary>盤面 s の期間の制約の不足区間。窓の数え方はチェッカー（担当不可の職員は対象外）と同じ。</summary>
    public static IReadOnlyList<C1Shortage> Shortages(Problem p, int[][] s)
    {
        var outList = new List<C1Shortage>();
        foreach (var c in p.Cons1)
        {
            if (c.Day1 <= 0 || c.Day1 > p.T) continue;
            for (var i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, c.ShiftIdx)) continue;
                var row = s[i];
                var cand = new bool[p.T];
                for (var d = 0; d < p.T; d++) cand[d] = row[d] != c.ShiftIdx && Changeable(p, i, d, c.ShiftIdx);
                int runStart = -1, n = 0; var stuck = false;
                var marks = new SortedSet<int>();
                void Close()
                {
                    if (runStart >= 0) outList.Add(new C1Shortage(i, c.ShiftIdx, c.Day1, c.Day2, runStart, runStart + n - 1 + c.Day1 - 1, n, marks.ToList(), stuck));
                    runStart = -1; n = 0; stuck = false; marks.Clear();
                }
                for (var j = 0; j <= p.T - c.Day1; j++)
                {
                    var z = 0;
                    for (var d = j; d < j + c.Day1; d++) if (row[d] == c.ShiftIdx) z++;
                    if (z >= c.Day2) { Close(); continue; }
                    if (runStart < 0) runStart = j;
                    n++;
                    var any = false;
                    for (var d = j; d < j + c.Day1; d++) if (cand[d]) { marks.Add(d); any = true; }
                    if (!any) stuck = true;
                }
                Close();
            }
        }
        return outList;
    }

    /// <summary>セル (i,j) に掛かる不足区間の説明文（無ければ null）。</summary>
    public static string? CellText(IReadOnlyList<C1Shortage> shortages, int[][] s, int i, int j, Func<int, string> sym, Func<int, string> day)
    {
        var sh = shortages.FirstOrDefault(x => x.Staff == i && j >= x.From && j <= x.To);
        if (sh is null) return null;
        var k = sym(sh.Shift);
        var head = $"期間の約束: {sh.Day1}日のなかに「{k}」が{sh.Day2}日必要です。";
        var body = sh.Marks.Count == 0 ? StuckText
            : $"いま足りない期間（{day(sh.From)}〜{day(sh.To)}）があり、印の日を{k}にすると届く見込みです。" + (sh.Stuck ? StuckText : "");
        var held = i < s.Length && j < s[i].Length && s[i][j] == sh.Shift ? $"（この日の{k}はすでに数に入っています）" : "";
        return head + body + held;
    }
}
