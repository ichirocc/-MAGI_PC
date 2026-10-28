using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>代行役の輪: 合格した手だけを当て、入力を変えず、結果は入力より悪くならない。Kotlin <c>AgentLoopTest</c> の移植。</summary>
public class AgentLoopTest
{
    private const int REST = 0, A = 1;

    /// <summary>1 日目は A が 0 人（不足 1）、2 日目は A が 2 人（過剰 1）。s0 の 2 日を入れ替えれば両方消える。</summary>
    internal static MagiState State(int staff = 2, int days = 2, IReadOnlyList<IReadOnlyList<int>>? sched = null, IReadOnlyDictionary<string, int>? wishes = null) => new(
        StartDate: "2026-01-01", EndDate: $"2026-01-{Math.Max(days, 1):00}",
        Shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "1") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: Enumerable.Range(0, staff).Select(i => new Staff($"s{i}", 0)).ToList(),
        Use2Patterns: true,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: sched ?? new List<IReadOnlyList<int>> { new List<int> { REST, A }, new List<int> { REST, A } },
        Wishes: wishes ?? new Dictionary<string, int>(), StaffRange: new Dictionary<string, MagiEngine.Model.Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    internal static int[][] Sched(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();
    internal static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public async Task AppliesGatedMovesUntilNoCandidateAndKeepsInput()
    {
        var st = State(); var s = Sched(st);
        var r = await AgentLoop.Run(st, s, deadlineMs: Now() + 20_000L);
        Assert.Equal(1, r.Before.Hard);
        Assert.Equal(0, r.After.Hard);
        Assert.NotEmpty(r.Steps);
        Assert.Equal(AgentLoop.Stop.NoCandidate, r.StopReason);
        Assert.Equal(REST, s[0][0]); Assert.Equal(A, s[0][1]);
        for (var k = 1; k < r.Steps.Count; k++) Assert.Equal(r.Steps[k - 1].HardAfter, r.Steps[k].HardBefore);
        Assert.Equal(r.After.Total, UnifiedViolationChecker.Check(st, r.Schedule).Total);
    }

    [Fact]
    public async Task RejectedCandidatesAreNotApplied()
    {
        var st = State(); var s = Sched(st);
        // 何も変えない案（ゲートが「変更がありません」で断る）と、悪くする案だけを返す探索。
        var bad = new FixSuggestion(FixKind.Change, new[] { new FixCell(1, 1, REST) }, "悪くする", 1, 1, Array.Empty<(string, int)>());
        var none = new FixSuggestion(FixKind.Change, Array.Empty<FixCell>(), "空", 0, 0, Array.Empty<(string, int)>());
        var r = await AgentLoop.Run(st, s, deadlineMs: long.MaxValue, suggest: (_, _, _) => new[] { none, bad });
        Assert.Empty(r.Steps);
        Assert.Equal(AgentLoop.Stop.NoCandidate, r.StopReason);
        Assert.Equal(r.Before.Total, r.After.Total);
    }

    [Fact]
    public async Task StopsWhenCancelledOrOutOfTime()
    {
        var st = State(); var s = Sched(st);
        Assert.Equal(AgentLoop.Stop.Cancelled, (await AgentLoop.Run(st, s, long.MaxValue, active: () => false)).StopReason);
        Assert.Equal(AgentLoop.Stop.TimeLimit, (await AgentLoop.Run(st, s, deadlineMs: 0L, now: () => 1L)).StopReason);
    }

    /// <summary>1 手の案が尽きて必須が残れば広域の研磨を 1 回だけ試し、合格した差分だけを 1 手として当てる（#13(g)）。</summary>
    [Fact]
    public async Task WidensOnceWhenNoCandidateAndGatesTheResult()
    {
        var st = State(); var s = Sched(st);
        var calls = 0;
        var fixedBoard = new[] { new[] { A, REST }, new[] { REST, A } };
        var r = await AgentLoop.Run(st, s, long.MaxValue, now: () => 0L, suggest: (_, _, _) => Array.Empty<FixSuggestion>(),
            widen: (_, _, _) => { calls++; return Task.FromResult(fixedBoard); });
        Assert.Equal(1, calls);
        Assert.Single(r.Steps);
        Assert.Equal($"{AgentLoop.WidenLabel}（2セル）", r.Steps[0].Label);
        Assert.Equal(0, r.After.Hard);
        Assert.Equal(AgentLoop.Stop.NoCandidate, r.StopReason);
    }

    [Fact]
    public async Task WidenResultThatDoesNotImproveIsDropped()
    {
        var st = State(); var s = Sched(st);
        var calls = 0;
        var r = await AgentLoop.Run(st, s, long.MaxValue, now: () => 0L, suggest: (_, _, _) => Array.Empty<FixSuggestion>(),
            widen: (_, b, _) => { calls++; return Task.FromResult(b); });
        Assert.Equal(1, calls);
        Assert.Empty(r.Steps);
        Assert.Equal(r.Before.Total, r.After.Total);
    }
}
