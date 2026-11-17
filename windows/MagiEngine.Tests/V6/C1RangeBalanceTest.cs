using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>期間の制約に要る最少回数と、それを下回る・ちょうどの個人の上限の案内（Kotlin <c>C1RangeBalanceTest</c> の 1 対 1 移植）。</summary>
public class C1RangeBalanceTest
{
    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    [Fact]
    public void MinCountMatchesBruteForce()
    {
        for (var t = 1; t <= 14; t++)
            for (var w = 1; w <= t; w++)
                for (var r = 0; r <= w; r++)
                {
                    var best = int.MaxValue;
                    for (var mask = 0; mask < (1 << t); mask++)
                    {
                        var ok = Enumerable.Range(0, t - w + 1).All(s => Enumerable.Range(s, w).Count(j => ((mask >> j) & 1) == 1) >= r);
                        if (ok) best = Math.Min(best, System.Numerics.BitOperations.PopCount((uint)mask));
                    }
                    Assert.Equal(r == 0 ? 0 : best, V6SanityPort.C1MinCount(t, w, r));
                }
        Assert.Equal(10, V6SanityPort.C1MinCount(31, 14, 5));
        Assert.Equal(8, V6SanityPort.C1MinCount(31, 7, 2));
    }

    [Fact]
    public void GuidanceFlagsCapsBelowAndAtTheMinimum()
    {
        var st0 = Load("sample_state_v6.json");
        var p0 = new Problem(st0);
        var rest = p0.RestIdx!.Value;
        const int w = 14, r = 5;
        var need = V6SanityPort.C1MinCount(p0.T, w, r);
        var staff = Enumerable.Range(0, p0.S).Where(i => p0.CanDo(i, rest)).Take(2).ToList();
        var range = st0.StaffRange.ToDictionary(kv => kv.Key, kv => kv.Value);
        range[$"{staff[0]},{rest}"] = new MagiEngine.Model.Range("", (need - 1).ToString());
        range[$"{staff[1]},{rest}"] = new MagiEngine.Model.Range("", need.ToString());
        var st = st0 with { Cons1 = new[] { new C1Row(w.ToString(), st0.Shifts[rest].Kigou, r.ToString()) }, StaffRange = range };
        var issues = V6SanityPort.BuildGuidance(st).Where(i => i.Where.Contains($"期間の制約「{st0.Shifts[rest].Kigou} {w}日で{r}回以上」")).ToList();
        Assert.Contains(issues, i => !i.Neutral && i.Problem.Contains("必ず足りません") && i.Where.Contains(st0.StaffList[staff[0]].Name));
        Assert.Contains(issues, i => i.Neutral && i.Problem.Contains("等間隔") && i.Where.Contains(st0.StaffList[staff[1]].Name));
    }
}
