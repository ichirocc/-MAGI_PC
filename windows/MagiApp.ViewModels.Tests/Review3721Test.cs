namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3721Test</c> の 1 対 1 移植。3.721.0: 再作成しても同じ結果になる間は、下部の「再作成」を外枠にして塗りの主を変更案へ譲る。
/// </summary>
public class Review3721Test
{
    [Fact]
    public void DemotedOnlyWhenFreshAndAllHardWithinFloor()
    {
        Assert.True(AnalysisTriage.RerunDemoted(hard: 2, floor: 2, fresh: true));
        Assert.True(AnalysisTriage.RerunDemoted(hard: 1, floor: 3, fresh: true));
        Assert.False(AnalysisTriage.RerunDemoted(hard: 3, floor: 2, fresh: true));
        Assert.False(AnalysisTriage.RerunDemoted(hard: 2, floor: 2, fresh: false));
        Assert.False(AnalysisTriage.RerunDemoted(hard: 0, floor: 0, fresh: true));
    }

    [Fact]
    public void FooterKeepsExactlyOneFilledPrimary()
    {
        Assert.Equal(RerunFooter.Filled, AnalysisTriage.RerunFooterOf(demoted: false, primaryElsewhere: false, noFixFound: false));
        Assert.Equal(RerunFooter.Outlined, AnalysisTriage.RerunFooterOf(demoted: true, primaryElsewhere: true, noFixFound: false));
        Assert.Equal(RerunFooter.Outlined, AnalysisTriage.RerunFooterOf(demoted: false, primaryElsewhere: true, noFixFound: true));
        Assert.Equal(RerunFooter.FixSearch, AnalysisTriage.RerunFooterOf(demoted: true, primaryElsewhere: false, noFixFound: false));
        Assert.Equal(RerunFooter.ProblemList, AnalysisTriage.RerunFooterOf(demoted: true, primaryElsewhere: false, noFixFound: true));
    }
}
