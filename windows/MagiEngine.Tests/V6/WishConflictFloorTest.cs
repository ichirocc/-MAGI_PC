using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>[E0] 希望衝突の床（<see cref="V6SanityPort.WishConflictHardFloor"/>）。<c>WishConflictFloorTest.kt</c> の逐語移植。</summary>
public class WishConflictFloorTest
{
    private const int Rest = 0;

    private static MagiState State(
        Dictionary<string, int> wishes,
        Dictionary<string, string>? needDay1 = null,
        Dictionary<string, Range>? staffRange = null,
        IReadOnlyList<C3Row>? cons3n = null) =>
        MinimalState.Build(
            startDate: "2026-10-01", endDate: "2026-10-07",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "", "") },
            groups: new List<Group> { new("G", "G") }, staffList: new List<Staff> { new("P", 0), new("Q", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } }, groupShiftApt: Array.Empty<IReadOnlyList<string>>(),
            schedule: Enumerable.Range(0, 2).Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(Rest, 7).ToList()).ToList(),
            wishes: wishes, staffRange: staffRange ?? new Dictionary<string, Range>(), needDay1: needDay1 ?? new Dictionary<string, string>(),
            cons3n: cons3n ?? Array.Empty<C3Row>());

    private static int Floor(MagiState st) => V6SanityPort.WishConflictHardFloor(new Problem(st));

    private static readonly List<C3Row> RestRun = new() { new(new List<string> { "休", "休", "休" }) };

    [Fact]
    public void ForbiddenWindowFullyWishedCountsOne() =>
        Assert.Equal(1, Floor(State(new() { ["0,2"] = Rest, ["0,3"] = Rest, ["0,4"] = Rest }, cons3n: RestRun)));

    [Fact]
    public void DayWhoseNeedCannotBeMetUnderPinsCountsOne() =>
        Assert.Equal(1, Floor(State(new() { ["1,0"] = Rest }, needDay1: new() { ["1,0"] = "2" })));

    [Fact]
    public void DayProofThatReliesOnZeroCapIsNotCounted()
    {
        var st = State(new() { ["0,0"] = Rest }, needDay1: new() { ["1,0"] = "1" }, staffRange: new() { ["1,1"] = new Range("", "0") });
        var p = new Problem(st);
        Assert.NotEmpty(ConstraintMus.AnalyzeDayConflicts(p));
        Assert.Empty(ConstraintMus.DayProofsWithoutZeroCap(p));
        Assert.Equal(0, Floor(st));
    }

    [Fact]
    public void DayProofOnAConflictDayIsNotCountedTwice() =>
        Assert.Equal(1, Floor(State(new() { ["0,2"] = Rest, ["0,3"] = Rest, ["0,4"] = Rest, ["1,3"] = Rest },
            needDay1: new() { ["1,3"] = "1" }, cons3n: RestRun)));

    [Fact]
    public void FloorNeverExceedsHardOnRealFixtures()
    {
        foreach (var name in new[] { "golden_state.json", "sample_state_v6.json", "blocked_covu_state.json", "sept2026_state.json", "oct2026_grid_state.json" })
        {
            var st = StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));
            var p = new Problem(st);
            var f = V6SanityPort.WishConflictHardFloor(p);
            var rnd = new Random(7);
            var boards = new List<int[][]> { st.Schedule.ToIntArray2D() };
            for (var n = 0; n < 30; n++) boards.Add(Enumerable.Range(0, p.S).Select(_ => Enumerable.Range(0, p.T).Select(_ => rnd.Next(p.K)).ToArray()).ToArray());
            for (var n = 0; n < 30; n++) boards.Add(Enumerable.Range(0, p.S).Select(i => Enumerable.Range(0, p.T)
                .Select(j => p.WishFixed(i, j) && rnd.Next(4) > 0 ? p.Wish[i][j] : rnd.Next(p.K)).ToArray()).ToArray());
            foreach (var b in boards)
            {
                var r = UnifiedViolationChecker.Check(st, b);
                var h = r.Hard;
                Assert.True(f <= h, $"{name} floor={f} hard={h}");
                var w = V6SanityPort.WishConflictHard(p, b);
                Assert.True(w.GetValueOrDefault("c3n", 0) <= r.Breakdown.GetValueOrDefault("c3n", 0), $"{name} c3n");
                Assert.True(w.GetValueOrDefault("c3w", 0) <= r.Breakdown.GetValueOrDefault("c3w", 0), $"{name} c3w");
                Assert.True(w.GetValueOrDefault("c3n", 0) + w.GetValueOrDefault("c3w", 0) + w.GetValueOrDefault("pref", 0) >= V6SanityPort.WishConflictFloorParts(p).Conflict, $"{name} 衝突の件数は床以上");
                if (V6FinalPort.WishFloorReached(h, f, () => V6SanityPort.HardAllWishOrigin(p, b, r, V6SanityPort.WishConflictFloorParts(p).Days)))
                    Assert.Equal(f, h);
            }
        }
    }

    [Fact]
    public void MayPlaceFloorNeverExceedsHardOnBoardsThatRespectMayPlace()
    {
        foreach (var name in new[] { "golden_state.json", "sample_state_v6.json", "blocked_covu_state.json", "sept2026_state.json", "oct2026_grid_state.json" })
        {
            var st = StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));
            var p = new Problem(st);
            var (c, d) = V6SanityPort.WishConflictFloorParts(p, zeroCapBinding: true);
            var f = c + d;
            Assert.True(f >= V6SanityPort.WishConflictHardFloor(p));
            var rnd = new Random(11);
            for (var n = 0; n < 60; n++)
            {
                var b = Enumerable.Range(0, p.S).Select(i => Enumerable.Range(0, p.T).Select(j =>
                {
                    if (p.WishFixed(i, j) && rnd.Next(4) > 0) return p.Wish[i][j];
                    var ks = Enumerable.Range(0, p.K).Where(k => p.MayPlace(i, k)).ToList();
                    return ks.Count == 0 ? 0 : ks[rnd.Next(ks.Count)];
                }).ToArray()).ToArray();
                var h = UnifiedViolationChecker.Check(st, b).Hard;
                Assert.True(f <= h, $"{name} mp floor={f} hard={h}");
            }
        }
    }

    [Fact]
    public async Task BoardAtTheFloorTriggersE0bEarlyStop()
    {
        // 希望どうしの衝突 1 組だけの盤面＝床 1。探索はすぐ床に届き、E0B は短い閾値で止まり後処理の研磨を省く。
        var st = State(new() { ["0,2"] = Rest, ["0,3"] = Rest, ["0,4"] = Rest }, cons3n: RestRun);
        Assert.Equal(1, Floor(st));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await V6FinalPort.HandleOptimize(st, 60, workers: 2, allowImpossible: true, wishFloorMode: WishFloorMode.E0B);
        Assert.Equal(1, res.Report.Hard);
        Assert.Contains(res.Logs, l => l.Tag == "EarlyStop" && l.Message.Contains("希望衝突の床に到達＝E0B"));
        Assert.Contains(res.Logs, l => l.Tag == "Watchdog" && l.Message.Contains("希望衝突の床1=到達"));
        Assert.True(sw.ElapsedMilliseconds < 55_000, $"早く返す: {sw.ElapsedMilliseconds}ms");
    }
}
