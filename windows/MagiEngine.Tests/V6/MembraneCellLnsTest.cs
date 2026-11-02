using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Android <c>MembraneCellLnsTest.kt</c> の写し。</summary>
public class MembraneCellLnsTest
{
    private static MagiState State() => StateJsonSerializer.Parse(FixtureLoader.ReadRaw("golden_state.json"));

    [Fact]
    public void NeverWorseThanInputAndKeepsLockedCells()
    {
        var state = State();
        var p = ScheduleUtil.CachedProblem(state);
        var init = state.Schedule.Select(r => r.ToArray()).ToArray();
        var r = MembraneCellLns.Apply(state, init, new MembraneCellLns.Params(Deterministic: true, MaxCells: 6, ItersPerCell: 20_000));
        var before = UnifiedViolationChecker.Check(state, init);
        var after = UnifiedViolationChecker.Check(state, r.NewSchedule);
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.True(after.Hard <= before.Hard);
        for (var i = 0; i < p.S; i++) for (var j = 0; j < p.T; j++) if (p.WishLocked(i, j)) Assert.Equal(init[i][j], r.NewSchedule[i][j]);
        Assert.False(V6SearchOperators.ExactPinRegression(p, init, r.NewSchedule));
    }

    [Fact]
    public void DeterministicModeIsReproducible()
    {
        var state = State();
        var init = state.Schedule.Select(r => r.ToArray()).ToArray();
        var prm = new MembraneCellLns.Params(Deterministic: true, MaxCells: 4, ItersPerCell: 10_000);
        var a = MembraneCellLns.Apply(state, init, prm).NewSchedule;
        var b = MembraneCellLns.Apply(state, init, prm).NewSchedule;
        Assert.True(a.Zip(b).All(t => t.First.SequenceEqual(t.Second)));
    }

    /// <summary>Kotlin と同じ盤面を出す（Android の同名テストと同じ指紋＝盤面を "a,b;c,d" に並べた文字列の Java hashCode）。</summary>
    [Fact]
    public void MatchesKotlinOnSampleFixture()
    {
        var state = StateJsonSerializer.Parse(FixtureLoader.ReadRaw("sample_state_v6.json"));
        var init = state.Schedule.Select(r => r.ToArray()).ToArray();
        var r = MembraneCellLns.Apply(state, init, new MembraneCellLns.Params(Deterministic: true, MaxCells: 30, ItersPerCell: 60_000));
        var text = string.Join(";", r.NewSchedule.Select(row => string.Join(",", row)));
        var h = 0;
        unchecked { foreach (var c in text) h = 31 * h + c; }
        Assert.Equal(8, r.Applied);
        Assert.Equal(1952902516, h);
    }

    [Fact]
    public void OnByDefault()
    {
        Assert.True(PolishGate.MembraneCellLns);
        Assert.True(new V6HotfixPasses.PostOptimizationParams().MembraneCellEnabled);
    }
}
