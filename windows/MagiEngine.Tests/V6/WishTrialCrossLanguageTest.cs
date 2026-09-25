using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [S5 T8] 希望取り消し試算の言語跨ぎ契約（Android <c>WishTrialCrossLanguageTest</c>）。
/// <c>wish_trial_expected.txt</c> は Android の <c>app/src/test/resources</c> と同一の複製（Kotlin で生成）。
/// </summary>
public class WishTrialCrossLanguageTest
{
    private static readonly string[] Files = { "sample_state_v6.json", "blocked_covu_state.json" };

    private static IEnumerable<string> ActualLines()
    {
        foreach (var f in Files)
        {
            var st = StateJsonSerializer.Parse(FixtureLoader.ReadRaw(f));
            var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
            var ctl = ((WishTrial.ControlOutcome)WishTrial.ControlOf(st, sched)).Control;
            yield return $"{f}|control -> {ctl.H0},{ctl.Rk}";
            var keys = WishTrial.LockedWishKeys(st)
                .Select(k => k.Split(',').Select(s => int.Parse(s.Trim())).ToArray())
                .OrderBy(k => k[0]).ThenBy(k => k[1]).Take(3);
            foreach (var k in keys)
            {
                var r = (WishTrial.Result)WishTrial.Trial(st, sched, k[0], k[1], ctl)!;
                yield return $"{f}|{k[0]},{k[1]} -> {r.H0},{r.Hx},{r.Rk},{r.Rr},{r.A},{r.Att},{r.APrime},{r.B},{r.PKeep},{r.PCancel}";
            }
        }
    }

    [Fact]
    public void wishTrialMatchesTheSharedCrossLanguageExpectation()
    {
        var expected = FixtureLoader.ReadRaw("wish_trial_expected.txt").Split('\n')
            .Select(l => l.Trim()).Where(l => !l.StartsWith("#") && l.Contains(" -> "));
        Assert.Equal(string.Join("\n", expected), string.Join("\n", ActualLines()));
    }
}
