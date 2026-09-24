using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>HardDeltaPrefilterTest</c> の 1 対 1 移植: <see cref="HardDelta"/> が checker の HARD 差と厳密に一致し、
/// <see cref="PolishGate.HardDeltaPrefilter"/> の ON/OFF で盤面が変わらないこと。</summary>
public class HardDeltaPrefilterTest
{
    private static readonly string[] Fixtures = { "golden_state.json", "sample_state_v6.json", "blocked_covu_state.json", "sept2026_state.json" };

    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));
    private static int[][] Board(MagiState st, Problem p) =>
        ScheduleUtil.NormalizeSchedule(st.Schedule.Select(r => r.ToArray()).ToArray(), p);
    private static int Hard(MagiState st, int[][] b) => UnifiedViolationChecker.Check(st, b).Hard;

    [Fact]
    public void deltaEqualsCheckerHardDifferenceOnRandomChanges()
    {
        var rng = new JavaRandom(20260924L);
        foreach (var res in Fixtures)
        {
            var st = Load(res); var p = new Problem(st);
            var baseB = Board(st, p);
            for (int trial = 0; trial < 150; trial++)
            {
                var cand = baseB.Copy2D();
                int cells = trial % 3 == 0 ? 1 : 2 + rng.NextInt(6);
                for (int c = 0; c < cells; c++)
                    cand[rng.NextInt(p.S)][rng.NextInt(p.T)] = rng.NextInt(p.K + 1) - (rng.NextInt(10) == 0 ? 1 : 0);
                Assert.True(Hard(st, cand) - Hard(st, baseB) == HardDelta.Delta(p, baseB, cand), $"{res} trial={trial}");
                if (trial % 10 == 0) baseB = ScheduleUtil.NormalizeSchedule(cand, p);
            }
        }
    }

    [Fact]
    public void sameDayPermutationDeltaEqualsCheckerHardDifference()
    {
        var rng = new JavaRandom(7L);
        foreach (var res in Fixtures)
        {
            var st = Load(res); var p = new Problem(st);
            var work = Board(st, p);
            for (int trial = 0; trial < 200; trial++)
            {
                int j = rng.NextInt(p.T);
                int k = 2 + rng.NextInt(2);
                var staff = Enumerable.Range(0, p.S).Shuffled(rng).Take(k).ToArray();
                var old = staff.Select(i => work[i][j]).ToArray();
                var before = work.Copy2D();
                for (int t = 0; t < k; t++) work[staff[t]][j] = old[(t + 1) % k];
                int expected = Hard(st, work) - Hard(st, before);
                Assert.True(expected == HardDelta.SameDayPermutationDelta(p, work, j, staff, old), $"{res} trial={trial}");
                Assert.True(expected == HardDelta.Delta(p, before, work), $"{res} trial={trial}");
                for (int t = 0; t < k; t++) work[staff[t]][j] = old[t];
            }
        }
    }

    private static T WithPrefilter<T>(bool on, Func<T> block)
    {
        var saved = PolishGate.HardDeltaPrefilter;
        PolishGate.HardDeltaPrefilter = on;
        try { return block(); } finally { PolishGate.HardDeltaPrefilter = saved; }
    }

    [Fact]
    public void cyclicSwapAndC1BeamBoardsAreIdenticalWithPrefilterOnAndOff()
    {
        foreach (var res in Fixtures)
        {
            var st = Load(res); var p = new Problem(st);
            V6HotfixPasses.CyclicSwapResult Cyc() => V6HotfixPasses.ApplyCyclicSwapPolish(st, Board(st, p));
            V6HotfixPasses.CyclicSwapResult Beam() => V6HotfixPasses.ApplyC1BeamPolish(st, Board(st, p), beamWidth: 6, maxSteps: 6);
            var cOn = WithPrefilter(true, Cyc); var cOff = WithPrefilter(false, Cyc);
            Assert.True(ScheduleUtil.ContentDeepEquals(cOn.NewSchedule, cOff.NewSchedule), $"{res} cyclic");
            Assert.Equal(cOff.Applied, cOn.Applied);
            var bOn = WithPrefilter(true, Beam); var bOff = WithPrefilter(false, Beam);
            Assert.True(ScheduleUtil.ContentDeepEquals(bOn.NewSchedule, bOff.NewSchedule), $"{res} beam");
        }
    }
}
