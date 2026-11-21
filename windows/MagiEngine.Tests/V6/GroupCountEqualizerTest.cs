using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>グループ内の回数をそろえる（Kotlin <c>GroupCountEqualizerTest.kt</c>、Android 3.727.115）。</summary>
public class GroupCountEqualizerTest
{
    private const int K = 3;   // 0 休・1 D・2 A

    private static MagiState State(IReadOnlyList<IReadOnlyList<int>> sched, IReadOnlyDictionary<string, MagiEngine.Model.Range>? range = null, IReadOnlyDictionary<string, int>? wishes = null) =>
        MinimalState.Build(
            startDate: "2026-12-01", endDate: "2026-12-14",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("D", "D", "", ""), new("A", "A", "", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: Enumerable.Range(0, sched.Count).Select(i => new Staff($"s{i}", i < 3 ? 0 : 1)).ToList(),
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
            schedule: sched, staffRange: range, wishes: wishes);

    private static List<Dictionary<int, int>> DayCounts(int[][] s, int t) =>
        Enumerable.Range(0, t).Select(j => s.GroupBy(r => r[j]).ToDictionary(g => g.Key, g => g.Count())).ToList();

    [Fact]
    public void equalizesWithinGroupAndKeepsEachDay()
    {
        var sched = new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(1, 14).ToList(), Enumerable.Range(0, 14).Select(j => j % 2 == 0 ? 1 : 0).ToList(),
            Enumerable.Repeat(0, 14).ToList(), Enumerable.Repeat(2, 14).ToList(),
        };
        var st = State(sched);
        var b = sched.Select(r => r.ToArray()).ToArray();
        var r = GroupCountEqualizer.Apply(st, b);
        Assert.True(r.Swaps > 0);
        Assert.True(r.SpreadAfter < r.SpreadBefore);
        Assert.Equal(DayCounts(b, 14), DayCounts(r.Schedule, 14));
        var d = Enumerable.Range(0, 3).Select(i => r.Schedule[i].Count(x => x == 1)).ToList();
        Assert.True(d.Max() - d.Min() <= 1);
        Assert.Equal(sched[3], r.Schedule[3]);
    }

    [Fact]
    public void keepsWishesRangesAndNeverAddsHard()
    {
        var rng = new Random(11);
        for (var n = 0; n < 30; n++)
        {
            var sched = Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<int>)Enumerable.Range(0, 14).Select(_ => rng.Next(K)).ToList()).ToList();
            var wishes = new Dictionary<string, int> { ["0,0"] = 1, ["1,3"] = 0 };
            var range = new Dictionary<string, MagiEngine.Model.Range> { ["1,1"] = new("2", "4") };
            var st = State(sched, range, wishes);
            var b = sched.Select(r => r.ToArray()).ToArray();
            var before = UnifiedViolationChecker.Check(st, b);
            var r = GroupCountEqualizer.Apply(st, b);
            var after = UnifiedViolationChecker.Check(st, r.Schedule);
            Assert.True(after.Hard <= before.Hard);
            Assert.True(r.SpreadAfter <= r.SpreadBefore);
            if (b[0][0] == 1) Assert.Equal(1, r.Schedule[0][0]);
            if (b[1][3] == 0) Assert.Equal(0, r.Schedule[1][3]);
            int Out(int[][] s) { var c = s[1].Count(x => x == 1); return Math.Max(0, 2 - c) + Math.Max(0, c - 4); }
            Assert.True(Out(r.Schedule) <= Out(b));
            Assert.Equal(DayCounts(b, 14), DayCounts(r.Schedule, 14));
        }
    }
}
