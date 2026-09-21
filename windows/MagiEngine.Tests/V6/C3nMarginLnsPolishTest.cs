using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [Kotlin原本 <c>C3nMarginLnsPolishTest</c>] <see cref="V6HotfixPasses.ApplyC3nPolish"/> はパターンが
/// またぐ日だけを1セルずつ独立に付け替えるため、単独では正味fireが減らず余白日を含めた複数セル同時変更
/// でしか解けない局面には届かない。
/// </summary>
public class C3nMarginLnsPolishTest
{
    // shift index: 0=休 1=A 2=B（3種のみ＝A の後に安全な逃げ先(Z等)を用意しない構成が本パスの前提）
    private const int Rest = 0;
    private const int AShift = 1;
    private const int BShift = 2;

    private static MagiState State(List<List<int>> schedule, IReadOnlyList<C3Row> cons3n, IReadOnlyDictionary<string, int>? wishes = null)
    {
        var kigou = new[] { "休", "A", "B" };
        return MinimalState.Build(
            startDate: "2026-12-01", endDate: "2026-12-12",
            shifts: kigou.Select((k, i) => new Shift(k, k, "", "", i == Rest ? ShiftRole.Rest : ShiftRole.None)).ToList(),
            groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, schedule.Count).Select(i => new Staff($"s{i}", 0)).ToList(),
            groupShift: new List<IReadOnlyList<int>> { Enumerable.Repeat(1, kigou.Length).ToList() },
            groupShiftApt: new List<IReadOnlyList<string>> { Enumerable.Repeat("", kigou.Length).ToList() },
            schedule: schedule.Select(r => (IReadOnlyList<int>)r).ToList(),
            wishes: wishes ?? new Dictionary<string, int>(),
            cons3n: cons3n);
    }

    /// <summary>
    /// 禁止連続2本[A,B]/[A,休]を隣接させた行(day4=A day5=A day6=B day7=休)。窓(5,6)のみ違反。
    /// day5/day6のどちらか1日だけ変えても隣接窓が別の禁止連続にはまり直すため単独では解消できない
    /// （A の直後は A 以外に進めない連鎖）。余白日 day4/day7 まで含めた同時変更で初めて 0 になる。
    /// </summary>
    private static MagiState ChainFixture(IReadOnlyDictionary<string, int>? wishes = null)
    {
        const int days = 12;
        var row = Enumerable.Repeat(Rest, days).ToList();
        row[4] = AShift; row[5] = AShift; row[6] = BShift; row[7] = Rest;
        return State(new List<List<int>> { row },
            new List<C3Row> { new(new List<string> { "A", "B" }), new(new List<string> { "A", "休" }) }, wishes);
    }

    [Fact]
    public void SingleCellPolishCannotResolveButMarginLnsDoes()
    {
        var st = ChainFixture();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c3n", 0));
        Assert.Equal(1, before.Hard);

        // 既存の1セル研磨は構造的に頭打ち(適用0・c3n不変)であることを固定する。
        var single = V6HotfixPasses.ApplyC3nPolish(st, sched.Select(r => (int[])r.Clone()).ToArray(), maxPasses: 3, seed: 7L);
        var afterSingle = UnifiedViolationChecker.Check(st, single.NewSchedule);
        Assert.Equal(0, single.Applied);
        Assert.Equal(1, afterSingle.Breakdown.GetValueOrDefault("c3n", 0));

        var result = C3nMarginLnsPolish.Apply(st, sched.Select(r => (int[])r.Clone()).ToArray(), marginDays: 1, maxPasses: 3, seed: 0xC3E9L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);
        Assert.True(result.Applied > 0);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n", -1));
        Assert.Equal(0, after.Hard);
        Assert.True(after.Total <= before.Total);
    }

    [Fact]
    public void IsNoOpWhenNoForbiddenRuleExists()
    {
        var st = ChainFixture() with { Cons3n = new List<C3Row>() };
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var result = C3nMarginLnsPolish.Apply(st, sched.Select(r => (int[])r.Clone()).ToArray());
        Assert.Equal(0, result.Applied);
        Assert.Equal(sched.Select(r => r.ToList()).ToList(), result.NewSchedule.Select(r => r.ToList()).ToList());
    }

    [Fact]
    public void DoesNotMoveWishLockedCells()
    {
        // day4(パターン先頭に隣接する余白日)を希望固定 → destroy集合から除外され不変のまま。
        var st = ChainFixture(new Dictionary<string, int> { ["0,4"] = AShift });
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);

        var result = C3nMarginLnsPolish.Apply(st, sched.Select(r => (int[])r.Clone()).ToArray(), marginDays: 1, maxPasses: 3, seed: 0xC3E9L);
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.Equal(AShift, result.NewSchedule[0][4]);
        Assert.True(after.Hard <= before.Hard);
    }

    [Fact]
    public void DoesNotRegressWithDefaultParams()
    {
        var st = ChainFixture();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(st, sched);

        var result = C3nMarginLnsPolish.Apply(st, sched.Select(r => (int[])r.Clone()).ToArray());
        var after = UnifiedViolationChecker.Check(st, result.NewSchedule);

        Assert.True(after.Hard <= before.Hard);
        Assert.True(after.Total <= before.Total);
    }

    [Fact]
    public void IsDeterministic()
    {
        var st = ChainFixture();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var r1 = C3nMarginLnsPolish.Apply(st, sched.Select(r => (int[])r.Clone()).ToArray(), marginDays: 1, maxPasses: 3, seed: 0xC3E9L);
        var r2 = C3nMarginLnsPolish.Apply(st, sched.Select(r => (int[])r.Clone()).ToArray(), marginDays: 1, maxPasses: 3, seed: 0xC3E9L);
        Assert.Equal(r1.Applied, r2.Applied);
        Assert.Equal(r1.NewSchedule.Select(r => r.ToList()).ToList(), r2.NewSchedule.Select(r => r.ToList()).ToList());
    }
}
