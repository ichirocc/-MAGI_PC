namespace MagiEngine.V6;

/// <summary>
/// [Android 3.727.36、#50 P1、docs/weightless_objective.md §4] 重みを使わない目的関数の比べ方を、今の採否と並べて出す（ログだけ。採否には使わない＝出力不変）。
/// 段の鍵（ライン 0）・T1 の職員ごとの不満（§4.4b）・fair の leximin（§4.4）。職員ごとの量はチェッカーと同じ数え方で、合計が
/// <c>Breakdown</c> と一致することを <c>TierObjectiveTest</c> が固定する。Kotlin <c>TierObjective</c> の 1 対 1 移植。
/// </summary>
public static class TierObjective
{
    /// <summary>段と族（§4.1。c1 は T1）。</summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> Tiers = new[]
    {
        new[] { "groupViol", "covU", "c3n", "c3w", "pref", "extWish" },
        new[] { "low", "c1", "high", "c3mn" },
        new[] { "c3", "c3m", "c41", "c42", "c41s", "c42s" },
        new[] { "apt", "c2", "covO", "weekly", "fair" },
    };

    public sealed record TierKey(IReadOnlyList<int> Excess, IReadOnlyList<IReadOnlyList<int>> Lex, IReadOnlyList<int> Dissatisfaction, IReadOnlyList<int> FairLeximin)
    {
        public int MaxDissatisfaction => Dissatisfaction.Count > 0 ? Dissatisfaction[0] : 0;
        public int SumDissatisfaction => Dissatisfaction.Sum();
    }

    public static TierKey Key(Problem p, int[][] schedule, ViolationReport report)
    {
        var excess = Tiers.Select(t => t.Sum(f => report.Breakdown.GetValueOrDefault(f, 0))).ToList();
        var lex = Tiers.Select(t => (IReadOnlyList<int>)t.Select(f => report.Breakdown.GetValueOrDefault(f, 0)).ToList()).ToList();
        var perStaff = T1PerStaff(p, schedule);
        var dis = Enumerable.Range(0, p.S).Select(i => perStaff.Values.Sum(a => a[i])).Where(v => v > 0).OrderByDescending(v => v).ToList();
        return new TierKey(excess, lex, dis, FairDeviations(p, schedule).OrderByDescending(v => v).ToList());
    }

    /// <summary>T1 の族ごとの職員ごとの量（チェッカーの low/high＝amount・c1／c3mn＝窓の数と同じ数え方）。</summary>
    public static IReadOnlyDictionary<string, int[]> T1PerStaff(Problem p, int[][] schedule)
    {
        var low = new int[p.S]; var high = new int[p.S]; var c1 = new int[p.S]; var c3mn = new int[p.S];
        var counts = ScheduleUtil.CountMatrix(p, schedule);
        for (var i = 0; i < p.S; i++)
            for (var k = 0; k < p.K; k++)
            {
                int lo = p.RangeLo[i][k], hi = p.RangeHi[i][k], n = counts[i][k];
                if (lo != int.MinValue && lo != 0 && p.CanDo(i, k) && n < lo) low[i] += lo - n;
                if (hi != int.MaxValue && n > hi) high[i] += n - hi;
            }
        foreach (var c in p.Cons1)
        {
            if (c.Day1 <= 0 || c.Day1 > p.T) continue;
            for (var i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, c.ShiftIdx)) continue;
                var row = schedule[i];
                for (var w = 0; w <= p.T - c.Day1; w++)
                {
                    var z = 0;
                    for (var l = 0; l < c.Day1; l++) if (row[w + l] == c.ShiftIdx) z++;
                    if (z < c.Day2) c1[i]++;
                }
            }
        }
        foreach (var c in p.Cons3mn)
        {
            var seq = c.Seq; var d = seq.Length;
            if (d == 0 || d > p.T) continue;
            for (var i = 0; i < p.S; i++)
                for (var j = 0; j <= p.T - d; j++)
                {
                    var all = true;
                    for (var l = 0; l < d && all; l++) all = schedule[i][j + l] == seq[l];
                    if (all) c3mn[i]++;
                }
        }
        return new Dictionary<string, int[]> { ["low"] = low, ["c1"] = c1, ["high"] = high, ["c3mn"] = c3mn };
    }

    /// <summary>fair の職員ごとの偏差（チェッカーと同じ範囲、偏差 &gt; 0 のものだけ）。合計は <c>Breakdown["fair"]</c>。</summary>
    public static List<int> FairDeviations(Problem p, int[][] schedule)
    {
        var counts = ScheduleUtil.CountMatrix(p, schedule);
        var outList = new List<int>();
        for (var g = 0; g < p.G; g++)
        {
            if (p.GroupMembers[g].Length < 2) continue;
            foreach (var k in p.Bucket[g])
                foreach (var (_, dev) in p.FairDevOfBucket(g, k, x => counts[x][k]).PerMember) outList.Add(dev);
        }
        return outList;
    }

    public static string LogLine(TierKey k) =>
        "段の鍵（重みなし・ライン0、採否には使わない）: " + string.Join(" ", k.Excess.Select((e, t) => $"T{t}={e}")) +
        $" ／ 職員の不満 最大{k.MaxDissatisfaction}・合計{k.SumDissatisfaction}（{k.Dissatisfaction.Count}人）" +
        " ／ fair の上位 " + (k.FairLeximin.Count == 0 ? "なし" : string.Join(",", k.FairLeximin.Take(5)));
}
