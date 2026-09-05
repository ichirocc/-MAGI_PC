using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[3.495.0 移植元] 違反アンカー型・可変長ウィンドウ交換（<see cref="WindowMode.StrictWholeWindow"/>）の検証（Kotlin <c>AnchoredWindowSwapTest</c> の3件）。</summary>
public class V6HotfixPassesAnchoredWindowSwapTest
{
    private static MagiState Build(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyDictionary<string, int> wishes,
        IReadOnlyDictionary<string, MagiEngine.Model.Range> staffRange, IReadOnlyList<C3Row> cons3n) => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-06",
        shifts: new List<Shift> { new("休", "休", "", ""), new("N", "N", "", ""), new("E", "E", "", "") },
        groups: new List<Group> { new("A", "A") },
        staffList: new List<Staff> { new("甲", 0), new("乙", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: schedule, wishes: wishes, staffRange: staffRange, cons3n: cons3n);
    private static V6HotfixPasses.CyclicSwapResult Run(MagiState s) =>
        V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(s, s.Schedule.Select(r => r.ToArray()).ToArray(), maxPasses: 3, maxEvaluations: 48, mode: WindowMode.StrictWholeWindow);

    [Fact]
    public void WholeWindowExchangeFreesTheSandwichedDay()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0, 2, 2, 2 }, new List<int> { 2, 2, 2, 2, 1, 1 } },
            new Dictionary<string, int> { ["0,3"] = 2 }, new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new("0", "0") },
            new List<C3Row> { new(new List<string> { "N", "E", "", "", "" }) });
        var before = UnifiedViolationChecker.Check(s, s.Schedule.Select(r => r.ToArray()).ToArray());
        Assert.Equal(1, before.Breakdown["high"]);
        var r = Run(s);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(r.Applied >= 1, r.Logs[0].Message);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("high"));
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n"));
        Assert.Equal(2, r.NewSchedule[0][3]);
        Assert.True(UnifiedViolationChecker.BetterReport(after, before));
    }

    [Fact]
    public void ReverseLookupWindowFixesACountShortfallWithoutAnyForbiddenRule()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2, 2, 2 }, new List<int> { 0, 0, 1, 1, 2, 2 } },
            new Dictionary<string, int>(), new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new("2", "") }, new List<C3Row>());
        var before = UnifiedViolationChecker.Check(s, s.Schedule.Select(r => r.ToArray()).ToArray());
        Assert.Equal(2, before.Breakdown["low"]);
        var r = Run(s);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(r.Applied >= 1, r.Logs[0].Message);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("low"));
        Assert.Equal(2, r.NewSchedule[0].Count(v => v == 0));
        Assert.True(UnifiedViolationChecker.BetterReport(after, before));
    }

    [Fact]
    public void WindowsContainingAWishAreRejectedAsAWhole()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2, 2, 2 }, new List<int> { 0, 0, 1, 1, 2, 2 } },
            new Dictionary<string, int> { ["1,0"] = 0 }, new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new("2", "") }, new List<C3Row>());
        var r = Run(s);
        Assert.Equal(0, r.NewSchedule[1][0]);
        Assert.Equal(1, r.NewSchedule[0][0]);
    }

    /// <summary>[3.499.0] 退化した窓長（0・負）でも落ちず、keep-best で悪化しない（Kotlin <c>degenerateParamsDoNotCrashAndNeverWorsenTheBoard</c> の厳密窓側）。</summary>
    [Fact]
    public void DegenerateWindowLengthsDoNotCrashAndNeverWorsenTheBoard()
    {
        var s = Build(new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0, 2, 2, 2 }, new List<int> { 2, 2, 2, 2, 1, 1 } },
            new Dictionary<string, int> { ["0,3"] = 2 }, new Dictionary<string, MagiEngine.Model.Range> { ["0,0"] = new("0", "0") },
            new List<C3Row> { new(new List<string> { "N", "E", "", "", "" }) });
        var board = s.Schedule.Select(r => r.ToArray()).ToArray();
        var before = UnifiedViolationChecker.Check(s, board);
        var r = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(s, board, maxPasses: 1, maxEvaluations: 1, mode: WindowMode.StrictWholeWindow, strictMaxLen: 0, strictLongLen: -5);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        Assert.True(after.Hard <= before.Hard);
        Assert.True(after.WeightedScore <= before.WeightedScore);
        Assert.NotEmpty(r.Logs);
    }
}
