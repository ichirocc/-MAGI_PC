using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [Kotlin 3.517.0/3.519.0同期] PERSON_SWAP_ILS の摂動本体 <see cref="V6NativeOptimizer.PersonSwapKick"/>
/// の固定テスト（Kotlin側 PersonSwapKickTest.kt と同型）。
/// </summary>
public class PersonSwapKickTest
{
    // 同群4名(a,b,c,d)。A(idx1)の回数: a=4,b=0,c=2,d=2 → 群平均2 → 負担 a=2,b=2,c=0,d=0。
    // a,b が唯一の最大負担ペアなので、乱数種によらず交換相手は決定的に a<->b になる。
    private static MagiState Fixture() => new(
        StartDate: "2026-08-01", EndDate: "2026-08-04",
        Shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "1", "") },
        Groups: new List<Group> { new("G0", "G0") },
        StaffList: new List<Staff> { new("a", 0), new("b", 0), new("c", 0), new("d", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 1 }, // a: A,A,A,A
            new List<int> { 0, 0, 0, 0 }, // b: 休,休,休,休
            new List<int> { 1, 0, 1, 0 }, // c: A,休,A,休
            new List<int> { 0, 1, 0, 1 }, // d: 休,A,休,A
        },
        Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
        Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    [Fact]
    public void SwapsTheHighestFairBurdenPairRegardlessOfSeed()
    {
        for (var seed = 1L; seed <= 5L; seed++)
        {
            var st = Fixture();
            var p = new Problem(st);
            var sched = st.Schedule.ToIntArray2D();
            V6NativeOptimizer.PersonSwapKick(p, sched, new JavaRandom(seed), pairs: 1);
            Assert.Equal(new[] { 0, 0, 0, 0 }, sched[0]);
            Assert.Equal(new[] { 1, 1, 1, 1 }, sched[1]);
            Assert.Equal(new[] { 1, 0, 1, 0 }, sched[2]);
            Assert.Equal(new[] { 0, 1, 0, 1 }, sched[3]);
        }
    }

    [Fact]
    public void FairIsInvariantUnderTheSwap()
    {
        var st = Fixture();
        var p = new Problem(st);
        var before = UnifiedViolationChecker.Check(st, st.Schedule.ToIntArray2D());
        var sched = st.Schedule.ToIntArray2D();
        V6NativeOptimizer.PersonSwapKick(p, sched, new JavaRandom(1), pairs: 1);
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(before.Breakdown.GetValueOrDefault("fair", 0), after.Breakdown.GetValueOrDefault("fair", 0));
        Assert.True(before.Breakdown.GetValueOrDefault("fair", 0) > 0);
    }
}
