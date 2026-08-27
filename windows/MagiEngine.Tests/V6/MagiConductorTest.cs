using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>1:1 port of Kotlin's <c>MagiConductorTest.kt</c> (stagnation firing + reward learning).</summary>
public class MagiConductorTest
{
    [Fact]
    public void StaysNoOpUntilStagnationThenSelectsEscape()
    {
        var c = new MagiConductor(stagThreshold: 100);
        // 停滞前は Noop
        Assert.Equal(ConductorAction.Noop, c.SelectAction());
        // 100反復以上 最良未更新 → Noop 以外の脱出戦略を選ぶ
        for (int i = 0; i < 150; i++) c.UpdateStagnation(false);
        Assert.NotEqual(ConductorAction.Noop, c.SelectAction());
    }

    [Fact]
    public void ImprovementResetsStagnation()
    {
        var c = new MagiConductor(stagThreshold: 10);
        for (int i = 0; i < 50; i++) c.UpdateStagnation(false);
        c.UpdateStagnation(true); // 最良更新でリセット
        Assert.Equal(ConductorAction.Noop, c.SelectAction());
    }

    [Fact]
    public void RewardLearningRaisesPreferredArm()
    {
        var c = new MagiConductor(stagThreshold: 1);
        c.UpdateReward(ConductorAction.Reheat, 0.5);
        Assert.True(c.ValueOf(ConductorAction.Reheat) > 0.0);
        // 高報酬の腕が UCB1 で選ばれやすくなる（値が他より高い）
        c.UpdateReward(ConductorAction.Reheat, 1.0);
        Assert.True(c.ValueOf(ConductorAction.Reheat) > c.ValueOf(ConductorAction.ScaleTemp));
    }
}
