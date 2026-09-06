using MagiApp.ViewModels.Tests.TestSupport;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [phase9 #17] <see cref="ConstraintHelp"/> の表が制約10族のキーと過不足なく一致することを固定する
/// （Kotlin原本 <c>ConstraintHelpTest</c> と同じ狙い＝族を足して説明を書き忘れる／消した族の説明が残る、を機械的に止める）。
/// </summary>
public class ConstraintHelpTest
{
    [Fact]
    public void BodiesCoverExactlyTheTenConstraintFamilies()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        var keys = vm.ConstraintFamilies().Concat(vm.SkillConstraintFamilies()).Select(f => f.Key).ToList();

        Assert.Equal(10, keys.Count);
        Assert.Equal(keys.OrderBy(k => k), ConstraintHelp.Bodies.Keys.OrderBy(k => k));
    }

    [Fact]
    public void BodiesNeverQuoteWeightNumbersAndFooterPointsToThePriorityTable()
    {
        // HF77: 重みの数値は本文に書かない（変更で stale 化する）。「必須」「できるだけ守る」の言い方だけ。
        foreach (var (key, body) in ConstraintHelp.Bodies)
        {
            Assert.False(body.Contains("重み"), key);
            Assert.True(body.Contains("必須条件") || body.Contains("できるだけ守る"), key);
        }
        Assert.Contains("直す優先順位", ConstraintHelp.Footer);
    }
}
