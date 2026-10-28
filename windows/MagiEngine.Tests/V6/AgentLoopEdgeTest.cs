using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>代行役の輪のエッジデータ: 空・最小・全固定・必須 0・壊れた研磨結果・最大規模でも入力より悪くせず、例外で落ちない。Kotlin <c>AgentLoopEdgeTest</c> の移植。</summary>
public class AgentLoopEdgeTest
{
    private const int REST = 0, A = 1;

    private static MagiState State(int staff, int days, IReadOnlyList<IReadOnlyList<int>> sched, IReadOnlyDictionary<string, int>? wishes = null) =>
        AgentLoopTest.State(staff, days, sched, wishes);
    private static IReadOnlyList<IReadOnlyList<int>> Rows(params int[][] rows) => rows;
    private static void AssertNotWorse(AgentLoop.Result r) =>
        Assert.True(!UnifiedViolationChecker.BetterReport(r.Before, r.After) || r.Steps.Count == 0 && r.Before.Total == r.After.Total, "入力より悪くしない");

    [Fact]
    public async Task OneStaffOneDay()
    {
        var st = State(1, 1, Rows(new[] { REST }));   // A が 1 人要るのに休＝不足 1
        var r = await AgentLoop.Run(st, AgentLoopTest.Sched(st), AgentLoopTest.Now() + 10_000L);
        Assert.Equal(0, r.After.Hard);
        AssertNotWorse(r);
    }

    [Fact]
    public async Task EveryCellWishLockedLeavesBoardAsIs()
    {
        // 2 人とも全日を休で希望固定＝不足は直せない。1 手の案も研磨の差分も希望のセルを触るのでゲートが断る。
        var w = new Dictionary<string, int> { ["0,0"] = REST, ["0,1"] = REST, ["1,0"] = REST, ["1,1"] = REST };
        var st = State(2, 2, Rows(new[] { REST, REST }, new[] { REST, REST }), w);
        var s = AgentLoopTest.Sched(st);
        var r = await AgentLoop.Run(st, s, long.MaxValue, now: () => 0L,
            suggest: (_, _, _) => new[] { new FixSuggestion(FixKind.Change, new[] { new FixCell(0, 0, A) }, "希望を壊す", -1, -1, Array.Empty<(string, int)>()) },
            widen: (_, b, _) => { b[1][1] = A; return Task.FromResult(b); });
        Assert.Empty(r.Steps);
        Assert.Equal(r.Before.Hard, r.After.Hard);
        Assert.Equal(REST, s[0][0]);
    }

    [Fact]
    public async Task HardZeroInputNeverWidens()
    {
        var st = State(2, 2, Rows(new[] { A, REST }, new[] { REST, A }));
        var calls = 0;
        var r = await AgentLoop.Run(st, AgentLoopTest.Sched(st), long.MaxValue, now: () => 0L, suggest: (_, _, _) => Array.Empty<FixSuggestion>(),
            widen: (_, b, _) => { calls++; return Task.FromResult(b); });
        Assert.Equal(0, r.Before.Hard);
        Assert.Equal(0, calls);
        Assert.Equal(AgentLoop.Stop.NoCandidate, r.StopReason);
    }

    [Fact]
    public async Task MalformedWidenResultsAreRejected()
    {
        var st = State(2, 2, Rows(new[] { REST, A }, new[] { REST, A }));
        var bad = new List<int[][]>
        {
            Array.Empty<int[]>(),                                   // 行が無い
            new[] { new[] { A } },                                  // 行も日も足りない（あるセルだけが差分）
            new[] { new[] { 99, A }, new[] { REST, A } },           // 範囲外のシフト
        };
        foreach (var b in bad)
        {
            var r = await AgentLoop.Run(st, AgentLoopTest.Sched(st), long.MaxValue, now: () => 0L, suggest: (_, _, _) => Array.Empty<FixSuggestion>(),
                widen: (_, _, _) => Task.FromResult(b));
            AssertNotWorse(r);
            foreach (var step in r.Steps)
                foreach (var op in step.Ops) Assert.True(op.ToShift is >= 0 and <= 1 && op.Staff is >= 0 and <= 1 && op.Day is >= 0 and <= 1);
        }
    }

    [Fact]
    public async Task WidenExceptionKeepsEarlierSteps()
    {
        var st = State(2, 2, Rows(new[] { REST, A }, new[] { REST, A }));
        var n = 0;
        // 1 回目は合格する手（s0 の 2 日を入れ替える）、2 回目からは案なし→研磨が例外。
        var r = await AgentLoop.Run(st, AgentLoopTest.Sched(st), long.MaxValue, now: () => 0L,
            suggest: (_, _, _) => n++ == 0
                ? new[] { new FixSuggestion(FixKind.Swap, new[] { new FixCell(0, 0, A), new FixCell(0, 1, REST) }, "入替", -1, -2, Array.Empty<(string, int)>()) }
                : Array.Empty<FixSuggestion>(),
            widen: (_, _, _) => throw new InvalidOperationException("研磨の失敗"));
        Assert.Single(r.Steps);
        Assert.Equal(0, r.After.Hard);
    }

    [Fact]
    public async Task ZeroRoundsAndEmptyStaff()
    {
        var st = State(2, 2, Rows(new[] { REST, A }, new[] { REST, A }));
        var r = await AgentLoop.Run(st, AgentLoopTest.Sched(st), long.MaxValue, maxRounds: 0);
        Assert.Equal(AgentLoop.Stop.RoundLimit, r.StopReason); Assert.Empty(r.Steps);
        var empty = State(0, 2, Rows());
        var e = await AgentLoop.Run(empty, Array.Empty<int[]>(), AgentLoopTest.Now() + 2_000L);
        Assert.Empty(e.Steps);
    }

    /// <summary>業務の上限（30 名・31 日）でも締切で止まり、結果は入力より悪くならない。</summary>
    [Fact]
    public async Task LargestBoardStopsByDeadline()
    {
        var sched = Enumerable.Range(0, 30).Select(i => (IReadOnlyList<int>)Enumerable.Range(0, 31).Select(j => (i + j) % 3 == 0 ? A : REST).ToList()).ToList();
        var st = State(30, 31, sched);
        var t0 = AgentLoopTest.Now();
        var r = await AgentLoop.Run(st, AgentLoopTest.Sched(st), t0 + 4_000L, searchMs: 1_000L);
        Assert.True(AgentLoopTest.Now() - t0 < 4_000L + 3_000L, "締切 4 秒＋探索 1 回ぶんで止まる");
        AssertNotWorse(r);
        Assert.Equal(r.After.Total, UnifiedViolationChecker.Check(st, r.Schedule).Total);
    }
}
