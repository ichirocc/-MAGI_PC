using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>LoopFeatureRegressionTest</c>（3.507.2）の移植。自律改善ループ仕様 §4「機能同等性」の 14 機能を最小盤面で作り、
/// 後処理チェーンを旧腕（成分修復 OFF）と新腕（ON）の両方で走らせて不変条件を検査する。</summary>
public class LoopFeatureRegressionTest
{
    private static readonly Shift Rest = new("休", "休", "", "");
    private static Shift Sh(string k, string need = "") => new(k, k, need, "");

    private static MagiState St(
        IReadOnlyList<Shift> shifts, IReadOnlyList<Staff> staff, IReadOnlyList<IReadOnlyList<int>> groupShift, IReadOnlyList<IReadOnlyList<int>> schedule,
        IReadOnlyList<Group>? groups = null, IReadOnlyDictionary<string, int>? wishes = null, IReadOnlyDictionary<string, Range>? staffRange = null,
        IReadOnlyList<C1Row>? cons1 = null, IReadOnlyList<C3Row>? cons3n = null, IReadOnlyList<C3Row>? cons3mn = null,
        IReadOnlyList<C41Row>? cons41 = null, IReadOnlyList<Group>? skillGroups = null, IReadOnlyList<C41Row>? cons41s = null)
    {
        var t = schedule[0].Count;
        var g = groups ?? new List<Group> { new("G", "G") };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: $"2026-08-{t:00}",
            shifts: shifts, groups: g, staffList: staff, use2Patterns: false,
            groupShift: groupShift, groupShiftApt: g.Select(_ => (IReadOnlyList<string>)shifts.Select(_ => "").ToList()).ToList(),
            schedule: schedule, wishes: wishes ?? new Dictionary<string, int>(), staffRange: staffRange ?? new Dictionary<string, Range>(),
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: cons1 ?? new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(), cons3n: cons3n ?? new List<C3Row>(),
            cons3m: new List<C3Row>(), cons3mn: cons3mn ?? new List<C3Row>(), cons41: cons41 ?? new List<C41Row>(), cons42: new List<C42Row>(),
            skillGroups: skillGroups, cons41s: cons41s);
    }
    private static IReadOnlyList<IReadOnlyList<int>> Rows(params int[][] rows) => rows.Select(r => (IReadOnlyList<int>)r.ToList()).ToList();
    private static IReadOnlyList<IReadOnlyList<int>> GS(params int[][] rows) => Rows(rows);
    private static int[][] Sched(MagiState s) => s.Schedule.Select(r => r.ToArray()).ToArray();
    private static int Bd(V6HotfixPasses.V6PostOptimizationResult r, string key) => r.Report.Breakdown.TryGetValue(key, out var v) ? v : 0;

    private static void Both(MagiState s, Action<string, V6HotfixPasses.V6PostOptimizationResult> check, Func<bool>? shouldStop = null)
    {
        foreach (var (arm, on) in new[] { ("旧", false), ("新", true) })
        {
            var r = V6HotfixPasses.RunPostOptimization(s, Sched(s), "feature", seed: 11L, shouldStop: shouldStop,
                deadlineMs: EngineClock.NowMs() + 4000L, parameters: new V6HotfixPasses.PostOptimizationParams(ComponentRepairEnabled: on));
            check(arm, r);
        }
    }

    [Fact] public void F01_CoverageShortageAndSurplusAreRepaired()
    {
        var shortage = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }), Rows(new[] { 1, 0 }, new[] { 0, 0 }));
        Both(shortage, (arm, r) => Assert.Equal(0, r.Report.Hard));
        var surplus = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }), Rows(new[] { 1, 1 }, new[] { 1, 0 }));
        Both(surplus, (arm, r) => { Assert.Equal(0, Bd(r, "covO")); Assert.Equal(0, r.Report.Hard); });
    }

    [Fact] public void F02_PersonalCountBoundsAreRestored()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }),
            Rows(new[] { 1, 1, 0 }, new[] { 0, 0, 1 }), staffRange: new Dictionary<string, Range> { ["0,1"] = new("1", "1") });
        Both(s, (arm, r) => { Assert.Equal(0, Bd(r, "high")); Assert.Equal(0, Bd(r, "low")); Assert.Equal(0, r.Report.Hard); });
    }

    [Fact] public void F03_GroupCountConstraintIsSatisfied()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 1) }, GS(new[] { 1, 1 }, new[] { 1, 1 }), Rows(new[] { 1, 0 }, new[] { 0, 1 }),
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") }, cons41: new List<C41Row> { new("G0", "A", "0", "0") });
        Both(s, (arm, r) => { Assert.Equal(0, Bd(r, "c41")); Assert.Equal(0, r.Report.Hard); });
    }

    [Fact] public void F04_ForbiddenRunIsRemovedAndNoneCreated()
    {
        var s = St(new[] { Rest, Sh("A"), Sh("B") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1, 1 }),
            Rows(new[] { 1, 2, 0 }, new[] { 0, 0, 0 }), cons3n: new List<C3Row> { new(new List<string> { "A", "B", "", "", "" }) });
        Both(s, (arm, r) => { Assert.Equal(0, Bd(r, "c3n")); Assert.Equal(0, r.Report.Hard); });
    }

    [Fact] public void F05_WishCellsStayPinned()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }),
            Rows(new[] { 0, 1, 0 }, new[] { 1, 0, 1 }), wishes: new Dictionary<string, int> { ["0,1"] = 1, ["1,0"] = 1 });
        Both(s, (arm, r) => { Assert.Equal(1, r.Schedule[0][1]); Assert.Equal(1, r.Schedule[1][0]); Assert.Equal(0, Bd(r, "pref")); });
    }

    [Fact] public void F06_NeighboursOfWishDaysArePolished()
    {
        var s = St(new[] { Rest, Sh("A"), Sh("B") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1, 1 }),
            Rows(new[] { 1, 2, 0 }, new[] { 0, 0, 0 }), wishes: new Dictionary<string, int> { ["0,1"] = 2 }, cons3mn: new List<C3Row> { new(new List<string> { "A", "B", "", "", "" }) });
        Both(s, (arm, r) => { Assert.Equal(2, r.Schedule[0][1]); Assert.Equal(0, Bd(r, "c3mn")); });
    }

    [Fact] public void F07_SameLengthSegmentExchangeFixesWindows()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }),
            Rows(new[] { 1, 1, 0, 0 }, new[] { 0, 0, 1, 1 }), cons1: new List<C1Row> { new("2", "休", "1") });
        Both(s, (arm, r) => { Assert.Equal(0, Bd(r, "c1")); Assert.Equal(0, r.Report.Hard); });
    }

    [Fact] public void F08_MultiStaffCyclicExchangeResolvesConflicts()
    {
        var s = St(new[] { Rest, Sh("A", "1"), Sh("B", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0), new Staff("Z", 0) }, GS(new[] { 1, 1, 1 }),
            Rows(new[] { 1, 1 }, new[] { 2, 2 }, new[] { 0, 0 }), cons3n: new List<C3Row> { new(new List<string> { "A", "A", "", "", "" }) });
        Both(s, (arm, r) => Assert.Equal(0, r.Report.Hard));
    }

    [Fact] public void F09_OnlyAssignableShiftsAreUsed()
    {
        var s = St(new[] { Rest, Sh("A", "1"), Sh("B", "1") }, new[] { new Staff("X", 0), new Staff("Y", 1) }, GS(new[] { 1, 1, 0 }, new[] { 1, 0, 1 }),
            Rows(new[] { 0, 1 }, new[] { 2, 2 }), groups: new List<Group> { new("G0", "G0"), new("G1", "G1") });
        Both(s, (arm, r) =>
        {
            var p = ScheduleUtil.CachedProblem(s);
            for (var i = 0; i < p.S; i++) for (var j = 0; j < p.T; j++) Assert.True(p.CanDo(i, r.Schedule[i][j]), $"{arm} 担当外 ({i},{j})");
            Assert.Equal(0, r.Report.Hard);
        });
    }

    [Fact] public void F10_SkillGroupConstraintIsSatisfied()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0, 0), new Staff("Y", 1, 1) }, GS(new[] { 1, 1 }, new[] { 1, 1 }), Rows(new[] { 1, 0 }, new[] { 0, 1 }),
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") }, skillGroups: new List<Group> { new("S0", "S0"), new("S1", "S1") }, cons41s: new List<C41Row> { new("S0", "A", "0", "0") });
        Both(s, (arm, r) => { Assert.Equal(0, Bd(r, "c41s")); Assert.Equal(0, r.Report.Hard); });
    }

    [Fact] public void F11_InputIsLeftUntouchedForUndo()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }), Rows(new[] { 1, 0 }, new[] { 0, 0 }));
        foreach (var on in new[] { false, true })
        {
            var input = Sched(s); var copy = input.Select(r => r.ToArray()).ToArray();
            var r = V6HotfixPasses.RunPostOptimization(s, input, "feature", seed: 11L, deadlineMs: EngineClock.NowMs() + 4000L,
                parameters: new V6HotfixPasses.PostOptimizationParams(ComponentRepairEnabled: on));
            for (var i = 0; i < input.Length; i++) Assert.Equal(copy[i], input[i]);
            Assert.NotSame(input, r.Schedule);
        }
    }

    [Fact] public void F12_StopReturnsPromptlyWithoutWorsening()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }), Rows(new[] { 1, 0 }, new[] { 0, 0 }));
        var before = UnifiedViolationChecker.Check(s, Sched(s));
        var t0 = EngineClock.NowMs();
        Both(s, (arm, r) => Assert.True(r.Report.Hard <= before.Hard, $"{arm} 停止後も悪化しない"), shouldStop: () => true);
        Assert.True(EngineClock.NowMs() - t0 < 5000L, "停止は速やか");
    }

    [Fact] public void F13_MonthBoundariesAreProtected()
    {
        for (var t = 1; t <= 2; t++)
        {
            var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }),
                Rows(Enumerable.Repeat(1, t).ToArray(), Enumerable.Repeat(0, t).ToArray()),
                cons1: new List<C1Row> { new("7", "休", "2") }, cons3n: new List<C3Row> { new(new List<string> { "A", "A", "A", "", "" }) });
            var before = UnifiedViolationChecker.Check(s, Sched(s));
            var tt = t;
            Both(s, (arm, r) => { Assert.Equal(tt, r.Schedule[0].Length); Assert.True(r.Report.Hard <= before.Hard, $"{arm} T={tt} 悪化しない"); });
        }
    }

    [Fact] public void F14_ResultStaysWithinTheMonth()
    {
        var s = St(new[] { Rest, Sh("A", "1") }, new[] { new Staff("X", 0), new Staff("Y", 0) }, GS(new[] { 1, 1 }), Rows(new[] { 1, 0, 1 }, new[] { 0, 0, 0 }));
        Both(s, (arm, r) =>
        {
            Assert.Equal(2, r.Schedule.Length);
            foreach (var row in r.Schedule) { Assert.Equal(3, row.Length); foreach (var k in row) Assert.InRange(k, 0, 1); }
            foreach (var key in r.Report.NeedViolations.Keys) Assert.InRange(int.Parse(key.Split(',')[1]), 0, 2);
        });
    }
}
