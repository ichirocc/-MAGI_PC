using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Android <c>IdentitySwapLogTest.kt</c> の写し（MAGI-021/022）。採否は変えず、件数で見えない必須の入れ替わりをログに出す。</summary>
public class IdentitySwapLogTest
{
    private static MagiState State() => StateJsonSerializer.Parse(FixtureLoader.ReadRaw("sample_state_v6.json"));

    [Fact]
    public void NoLogWhenNothingNewAppears()
    {
        var st = State();
        var b = st.Schedule.Select(r => r.ToArray()).ToArray();
        var r = UnifiedViolationChecker.Check(st, b);
        Assert.Empty(V6FinalPort.IdentitySwapLog(st, r, r));
    }

    [Fact]
    public void CountsTheNewHardViolationsByIdentity()
    {
        var st = State();
        var p = new Problem(st);
        var b0 = st.Schedule.Select(r => r.ToArray()).ToArray();
        var r0 = UnifiedViolationChecker.Check(st, b0);
        var b1 = b0.Copy2D();
        var rng = new Random(5);
        for (var n = 0; n < 40; n++) b1[rng.Next(p.S)][rng.Next(p.T)] = rng.Next(p.K);
        var r1 = UnifiedViolationChecker.Check(st, b1);
        var born = ViolationIdentity.NewHard(r0, r1);
        Assert.NotEmpty(born);
        var log = V6FinalPort.IdentitySwapLog(st, r0, r1);
        Assert.Single(log);
        Assert.StartsWith($"入力に無かった必須違反 {born.Count}件", log[0].Message);
        Assert.Equal("W", log[0].Level);
    }
}
