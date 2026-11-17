using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>日の上限人数が 0 の (日, シフト) には、最適化器が新しく置かない（Kotlin <c>DayCapZeroTest</c> の 1 対 1 移植。希望は優先・採点は covO のまま）。</summary>
[Collection("PolishGateGlobal")]
public class DayCapZeroTest
{
    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static MagiState CapZero(MagiState st, IEnumerable<(int K, int J)> cells)
    {
        var d1 = new Dictionary<string, string>(st.NeedDay1);
        var d2 = new Dictionary<string, string>(st.NeedDay2);
        foreach (var (k, j) in cells) { d1[$"{k},{j}"] = "0"; d2[$"{k},{j}"] = "0"; }
        return st with { NeedDay1 = d1, NeedDay2 = d2 };
    }

    /// <summary>最適化器が置いた、希望でも固定でもないセルの (シフト, 日) を 3 つ選ぶ（上限 0 にすると置けなくなる値）。</summary>
    private static List<(int K, int J)> Targets(MagiState st, int[][] outSched)
    {
        var p = new Problem(st);
        var seen = new List<(int K, int J)>();
        for (var j = 0; j < p.T; j++)
            for (var i = 0; i < p.S; i++)
            {
                var k = outSched[i][j];
                if (k < 0 || p.Wish[i][j] >= 0 || p.Pinned(i, j)) continue;
                if (Enumerable.Range(0, p.S).Any(x => p.Wish[x][j] == k)) continue;
                if (seen.Count < 3 && (j + k) % 3 == 0 && !seen.Contains((k, j))) seen.Add((k, j));
            }
        Assert.Equal(3, seen.Count);
        return seen;
    }

    [Fact]
    public void CapZeroBansOnlyNonWishCellsAndLeavesScoring()
    {
        var st0 = Load("sample_state_v6.json");
        var p0 = new Problem(st0);
        var i = Enumerable.Range(0, p0.S).First(x => p0.Wish[x][0] < 0);
        var k = p0.AllowedShiftsForStaff(i)[0];
        var p = new Problem(CapZero(st0, new[] { (k, 0) }));
        Assert.True(p.DayCapBanned(i, 0, k));
        Assert.True(p.ExtBanned(i, 0, k));
        Assert.False(p.ExtWishBanned(i, 0, k));
        Assert.False(p.MayPlaceAt(i, 0, k));
        Assert.False(p.DayCapBanned(i, 1, k));
        var withWish = Enumerable.Range(0, p.S).Where(x => p.Wish[x][0] >= 0).Cast<int?>().FirstOrDefault();
        if (withWish is int w) Assert.False(p.DayCapBanned(w, 0, p.Wish[w][0]));
    }

    [Fact]
    public async Task OptimizerDoesNotPlaceOnCapZeroDays()
    {
        var st0 = Load("sample_state_v6.json");
        var input = st0.Schedule.ToIntArray2D();
        var out0 = (await V6FinalPort.HandleOptimize(st0, secondsRaw: 2, schedule: input.Copy2D(), workers: 1,
            requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, seed: 3L)).Schedule;
        var st = CapZero(st0, Targets(st0, out0));
        var p = new Problem(st);
        var baseSched = V6NativeOptimizer.ClearCappedCells(st, p.WithManualPins(ScheduleUtil.NormalizeSchedule(input, p))).Schedule;
        foreach (var algo in new[] { V6Algorithm.V5, V6Algorithm.Portfolio })
        {
            var outSched = (await V6FinalPort.HandleOptimize(st, secondsRaw: 2, schedule: input.Copy2D(), workers: 2,
                requestedAlgorithm: algo, allowImpossible: true, seed: 5L)).Schedule;
            var fresh = p.ExtBanNewCells(baseSched, outSched).Where(c => p.DayCapBanned(c.I, c.J, outSched[c.I][c.J])).ToList();
            Assert.True(fresh.Count == 0, $"{algo}: 上限 0 の日に新しく置いた {fresh.Count}");
        }
    }
}
