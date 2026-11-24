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
        // 1 人だけのグループ（s3）は目標を持たないが、別のグループとの入れ替えの相手にはなる＝日ごとの人数だけ確かめる
    }

    [Fact]
    public void onlyPickedShiftsAreTargeted()
    {
        var sched = new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(1, 14).ToList(), Enumerable.Repeat(0, 14).ToList(), Enumerable.Repeat(2, 14).ToList(), Enumerable.Repeat(0, 14).ToList(),
        };
        var st = State(sched);
        var b = sched.Select(r => r.ToArray()).ToArray();
        var onlyD = GroupCountEqualizer.Apply(st, b, new HashSet<int> { 1 });
        var onlyA = GroupCountEqualizer.Apply(st, b, new HashSet<int> { 2 });
        var d = Enumerable.Range(0, 3).Select(i => onlyD.Schedule[i].Count(x => x == 1)).ToList();
        Assert.True(d.Max() - d.Min() <= 1);
        var a = Enumerable.Range(0, 3).Select(i => onlyA.Schedule[i].Count(x => x == 2)).ToList();
        Assert.True(a.Max() - a.Min() <= 1);
        Assert.Equal(0, GroupCountEqualizer.Apply(st, b, new HashSet<int>()).Swaps);
    }

    [Fact]
    public void crossGroupPartnerMovesCountsTheOwnGroupCannot()
    {
        // 休・D・A・B。G0＝s0・s1・s2、G1＝s3。毎日 s0=D・s1=A・s2=A・s3=B。s1・s2 は B を置けず（上限 0）、s0 は A を置けない＝
        // G0 の中ではどう入れ替えても D は s0 から動かない。G1 の s3 を介した入れ替えだけが D を渡せる（Android 3.727.122）。
        var sched = new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(1, 14).ToList(), Enumerable.Repeat(2, 14).ToList(), Enumerable.Repeat(2, 14).ToList(), Enumerable.Repeat(3, 14).ToList(),
        };
        var st = MinimalState.Build(
            startDate: "2026-12-01", endDate: "2026-12-14",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("D", "D", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: Enumerable.Range(0, 4).Select(i => new Staff($"s{i}", i < 3 ? 0 : 1)).ToList(),
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 }, new List<int> { 1, 1, 1, 1 } },
            schedule: sched,
            staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,2"] = new("0", "0"), ["1,3"] = new("0", "0"), ["2,3"] = new("0", "0") });
        var b = sched.Select(r => r.ToArray()).ToArray();
        Assert.Equal(0, GroupCountEqualizer.Apply(st, b, new HashSet<int> { 1 }, annealIters: 0).Swaps);
        var r = GroupCountEqualizer.Apply(st, b, new HashSet<int> { 1 });
        var d = Enumerable.Range(0, 3).Select(i => r.Schedule[i].Count(x => x == 1)).ToList();
        Assert.True(d.Max() - d.Min() < 14, $"G0 の D が 1 人に偏らない: {string.Join(",", d)}");
        Assert.Equal(DayCounts(b, 14), DayCounts(r.Schedule, 14));
        for (var j = 0; j < 14; j++) Assert.True(r.Schedule[0][j] != 2 && r.Schedule[1][j] != 3 && r.Schedule[2][j] != 3);
    }

    [Fact]
    public void threePersonRotationWhenEveryPairSwapIsBlocked()
    {
        // 休・D・A・B の 4 種。上限 0 で s0 は B 不可・s1 は D 不可・s2 は A 不可。毎日 s0=D・s1=A・s2=B
        // → どの 2 人の入れ替えも誰かに置けないシフトが入る。3 人の回し（s0←A・s1←B・s2←D）だけが D を s0 から s2 へ渡せる。
        var sched = new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(1, 14).ToList(), Enumerable.Repeat(2, 14).ToList(), Enumerable.Repeat(3, 14).ToList(),
        };
        var st = MinimalState.Build(
            startDate: "2026-12-01", endDate: "2026-12-14",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("D", "D", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G0", "G0") },
            staffList: Enumerable.Range(0, 3).Select(i => new Staff($"s{i}", 0)).ToList(),
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } },
            schedule: sched,
            staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,3"] = new("0", "0"), ["1,1"] = new("0", "0"), ["2,2"] = new("0", "0") });
        var b = sched.Select(r => r.ToArray()).ToArray();
        Assert.Equal(0, GroupCountEqualizer.Apply(st, b, new HashSet<int> { 1 }, annealIters: 0).Swaps);
        var r = GroupCountEqualizer.Apply(st, b, new HashSet<int> { 1 });
        var d = new[] { 0, 2 }.Select(i => r.Schedule[i].Count(x => x == 1)).ToList();
        Assert.True(Math.Abs(d[0] - d[1]) <= 1, $"D の差が 1 以内: {string.Join(",", d)}");
        for (var j = 0; j < 14; j++)
        {
            Assert.Equal(new HashSet<int> { 1, 2, 3 }, Enumerable.Range(0, 3).Select(i => r.Schedule[i][j]).ToHashSet());   // 日ごとの人数は不変
            Assert.True(r.Schedule[0][j] != 3 && r.Schedule[1][j] != 1 && r.Schedule[2][j] != 2);   // 置けないシフトは入らない
        }
    }

    [Fact]
    public void monthWideChainWhenSingleSwapsAreBlocked()
    {
        // 休は 2 人とも 7 回固定。偶数日の D↔休 は 1 組では休の範囲を外れ、奇数日の 休↔A は D が動かない
        // → 偶数日と奇数日の 2 組を合わせて初めて D がそろう（焼きなまし）。s2 は D 不可・A 14 回固定で動かない。
        var sched = new List<IReadOnlyList<int>>
        {
            Enumerable.Range(0, 14).Select(j => j % 2 == 0 ? 1 : 0).ToList(),
            Enumerable.Range(0, 14).Select(j => j % 2 == 0 ? 0 : 2).ToList(),
            Enumerable.Repeat(2, 14).ToList(),
        };
        var range = new Dictionary<string, MagiEngine.Model.Range>
        {
            ["0,0"] = new("7", "7"), ["1,0"] = new("7", "7"), ["2,1"] = new("0", "0"), ["2,2"] = new("14", "14"),
        };
        var st = State(sched, range);
        var b = sched.Select(r => r.ToArray()).ToArray();
        Assert.Equal(0, GroupCountEqualizer.Apply(st, b, new HashSet<int> { 1 }, annealIters: 0).Swaps);
        var r = GroupCountEqualizer.Apply(st, b, new HashSet<int> { 1 });
        var d = Enumerable.Range(0, 2).Select(i => r.Schedule[i].Count(x => x == 1)).ToList();
        Assert.True(Math.Abs(d[0] - d[1]) <= 1, $"D の差が 1 以内: {string.Join(",", d)}");
        Assert.Equal(new[] { 7, 7 }, Enumerable.Range(0, 2).Select(i => r.Schedule[i].Count(x => x == 0)).ToArray());
        Assert.Equal(DayCounts(b, 14), DayCounts(r.Schedule, 14));
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

    [Fact]
    public void lowersCheckerFairOnRealData()
    {
        // ずれは採点の fair と同じ物差し＝押したあとチェッカーの fair が増えない・必須が増えない（Android 3.727.125）
        foreach (var name in new[] { "golden_state.json", "oct2026_grid_state.json" })
        {
            var st = MagiEngine.Model.StateJsonSerializer.Parse(MagiEngine.Tests.Fixtures.FixtureLoader.ReadRaw(name));
            var b = ScheduleUtil.NormalizeSchedule(st.Schedule.Select(r => r.ToArray()).ToArray(), new Problem(st));
            var before = UnifiedViolationChecker.Check(st, b);
            var r = GroupCountEqualizer.Apply(st, b);
            var after = UnifiedViolationChecker.Check(st, r.Schedule);
            Assert.True(after.Breakdown.GetValueOrDefault("fair") <= before.Breakdown.GetValueOrDefault("fair"), name);
            Assert.True(after.Hard <= before.Hard, name);
        }
    }
}
