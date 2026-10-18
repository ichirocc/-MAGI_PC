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
        IReadOnlyList<C3Row>? cons3n = null,
        IReadOnlyList<ManualPin>? pins = null) =>
        MinimalState.Build(
            startDate: "2026-10-01", endDate: "2026-10-07",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "", "") },
            groups: new List<Group> { new("G", "G") }, staffList: new List<Staff> { new("P", 0), new("Q", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } }, groupShiftApt: Array.Empty<IReadOnlyList<string>>(),
            schedule: Enumerable.Range(0, 2).Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(Rest, 7).ToList()).ToList(),
            wishes: wishes, staffRange: staffRange ?? new Dictionary<string, Range>(), needDay1: needDay1 ?? new Dictionary<string, string>(),
            cons3n: cons3n ?? Array.Empty<C3Row>()) with { ManualPins = pins ?? Array.Empty<ManualPin>() };

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

    private static readonly string[] FixtureNames = { "golden_state.json", "sample_state_v6.json", "blocked_covu_state.json", "sept2026_state.json", "oct2026_grid_state.json" };

    private static int[][] Board(int[] p, int[] q) => new[] { p, q };
    private static int[] AllRest() => Enumerable.Repeat(Rest, 7).ToArray();

    [Fact]
    public void PinOnZeroCapShiftServesItsSeatInTheDayProof()
    {
        // P は A の上限 0 だが 10/1 を手で A に固定（最適化器は守る）。Q は 10/1 が休の希望。A の必要 1 人は P の固定が埋める＝日の証明は立たない。
        var st = State(new() { ["1,0"] = Rest }, needDay1: new() { ["1,0"] = "1" }, staffRange: new() { ["0,1"] = new Range("", "0") }, pins: new[] { new ManualPin(0, 0, 1) });
        var p = new Problem(st);
        Assert.False(p.MayPlace(0, 1));
        Assert.Empty(ConstraintMus.AnalyzeDayConflicts(p));
        Assert.Equal(0, V6SanityPort.WishConflictFloorParts(p, zeroCapBinding: true).Days);
        Assert.Equal(0, Floor(st));
        // 固定を守る盤面の HARD は 0（上限 0 は要調整）＝上限 0 に頼る床も超えない
        var b = Board(new[] { 1, Rest, Rest, Rest, Rest, Rest, Rest }, AllRest());
        Assert.Equal(0, UnifiedViolationChecker.Check(st, b).Hard);
        // 固定が無ければ上限 0 に頼る日の証明が立つ（従来どおり）
        Assert.Equal(1, V6SanityPort.WishConflictFloorParts(new Problem(st with { ManualPins = Array.Empty<ManualPin>() }), zeroCapBinding: true).Days);
    }

    [Fact]
    public void CoverageDiagCountsAPinnedSeatAsFilled()
    {
        // A の必要 2 人。P は上限 0 だが 10/1 を A に固定、Q は休＝不足 1 は Q を移せば埋まる（充足可能）。旧: P を数えず 1 人 < 2 で「充足不可」。
        var st = State(new(), needDay1: new() { ["1,0"] = "2" }, staffRange: new() { ["0,1"] = new Range("", "0") }, pins: new[] { new ManualPin(0, 0, 1) });
        var b = Board(new[] { 1, Rest, Rest, Rest, Rest, Rest, Rest }, AllRest());
        var s = Assert.Single(V6PortAnalyzer.DiagnoseCoverage(st, b).Shortfalls);
        Assert.Equal(CoverageVerdict.Fixable, s.Verdict);
        Assert.Equal(2, s.Capacity);
        Assert.Empty(ConstraintMus.DayShortProofs(new Problem(st)));
    }

    [Fact]
    public void PinOnAnAllowedShiftCountsLikeAWish()
    {
        // 上限の無いシフトへの固定は希望固定と同じ: 休に固定した Q は A の席に就けない＝希望の場合と同じ床 1、コアには手動固定が載る。
        var wished = State(new() { ["1,0"] = Rest }, needDay1: new() { ["1,0"] = "2" });
        var pinned = State(new(), needDay1: new() { ["1,0"] = "2" }, pins: new[] { new ManualPin(1, 0, Rest) });
        Assert.Equal(Floor(wished), Floor(pinned));
        Assert.Equal(1, Floor(pinned));
        var core = Assert.Single(ConstraintMus.AnalyzeDayConflicts(new Problem(pinned))).Core;
        Assert.Contains(core, it => it is ConstraintMus.WishPin { Manual: true, Staff: 1 });
        Assert.Contains(V6SanityPort.BuildGuidance(pinned), it => it.Problem.Contains("手動固定「Q 10/1(木)=休」"));
        // 席を埋める側の固定（A に固定、上限なし）は証明に影響しない
        Assert.Equal(0, Floor(State(new(), needDay1: new() { ["1,0"] = "1" }, pins: new[] { new ManualPin(0, 0, 1) })));
    }

    [Fact]
    public void MayPlaceFloorNeverExceedsHardOnBoardsThatRespectMayPlaceAndPins()
    {
        foreach (var name in FixtureNames)
        {
            var st0 = StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));
            var p0 = new Problem(st0);
            var rnd = new Random(13);
            // 上限 0 の (職員,シフト) があれば 3 日おきにそこへ手動固定（無ければ担当可のシフトへ）
            var pins = new List<ManualPin>();
            for (var i = 0; i < p0.S; i++)
            {
                var zero = Enumerable.Range(0, p0.K).Where(k => p0.CanDo(i, k) && !p0.MayPlace(i, k)).ToList();
                var ks = zero.Count == 0 ? Enumerable.Range(0, p0.K).Where(k => p0.CanDo(i, k)).ToList() : zero;
                if (ks.Count == 0) continue;
                for (var j = i % 3; j < p0.T; j += 3) pins.Add(new ManualPin(i, j, ks[rnd.Next(ks.Count)]));
            }
            var st = st0 with { ManualPins = pins };
            var p = new Problem(st);
            Assert.True(p.HasPins);
            var (c, d) = V6SanityPort.WishConflictFloorParts(p, zeroCapBinding: true);
            var f = c + d;
            var strict = V6SanityPort.WishConflictHardFloor(p);
            Assert.True(f >= strict);
            for (var n = 0; n < 60; n++)
            {
                var b = Enumerable.Range(0, p.S).Select(i => Enumerable.Range(0, p.T).Select(j =>
                {
                    if (p.Pinned(i, j)) return p.LockTo(i, j);
                    if (p.WishFixed(i, j) && rnd.Next(4) > 0) return p.Wish[i][j];
                    var ks = Enumerable.Range(0, p.K).Where(k => p.MayPlace(i, k)).ToList();
                    return ks.Count == 0 ? 0 : ks[rnd.Next(ks.Count)];
                }).ToArray()).ToArray();
                var h = UnifiedViolationChecker.Check(st, b).Hard;
                Assert.True(f <= h, $"{name} pins floor={f} hard={h}");
                Assert.True(strict <= h, $"{name} pins strict floor={strict} hard={h}");
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
        Assert.Contains(res.Logs, l => l.Tag == "Watchdog" && System.Text.RegularExpressions.Regex.IsMatch(l.Message, "入力超えの最終改善=(なし|経過\\d+s)・"));
        Assert.True(sw.ElapsedMilliseconds < 55_000, $"早く返す: {sw.ElapsedMilliseconds}ms");
    }
}
