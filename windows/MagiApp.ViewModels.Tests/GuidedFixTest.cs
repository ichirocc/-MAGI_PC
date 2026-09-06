using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[外部レビュー第3/4段] 「なおすのを手伝って」の判断と候補の有効/無効を UI から切り離して固定する。</summary>
public class GuidedFixTest
{
    private static CoverageShortfall Sf(int day, CoverageVerdict v, int miss = 1, bool blockedNow = false) =>
        new(day, $"8/{day + 1}", 1, "A", 1, 0, miss, 2, v, "理由", blockedNow);

    private static CoverageDiagnosis Diag(params CoverageShortfall[] sfs) =>
        new(sfs.Sum(s => s.Miss), sfs.Count(s => s.Verdict == CoverageVerdict.Infeasible), sfs.Count(s => s.Verdict == CoverageVerdict.Fixable),
            sfs, Array.Empty<string>(), 0, Array.Empty<CoverageSurplus>());

    [Fact]
    public void PlanPicksTheFirstFixableNotBlockedNowSlotAndNeverSaysDoneWhileBlockedOrInfeasibleRemain()
    {
        var plan = GuidedFixPlan.Build(Diag(Sf(0, CoverageVerdict.Fixable, blockedNow: true), Sf(1, CoverageVerdict.Infeasible), Sf(2, CoverageVerdict.Fixable)));
        Assert.Equal(2, plan.Target!.DayIndex);
        Assert.Single(plan.Blocked);
        Assert.Single(plan.Infeasible);
        Assert.False(plan.AllDone);

        var onlyBlocked = GuidedFixPlan.Build(Diag(Sf(0, CoverageVerdict.Fixable, blockedNow: true)));
        Assert.Null(onlyBlocked.Target);
        Assert.False(onlyBlocked.AllDone); // 旧実装はここで「直し終わりました」と言っていた

        Assert.True(GuidedFixPlan.Build(null).AllDone);
        Assert.Equal("直し終わりました！", GuidedFixPlan.Build(Diag()).Title);
    }

    [Fact]
    public void FlowKeepsCandidatesDisabledUntilACheckNewerThanThePressIsReflected()
    {
        var f = new GuidedFixFlow();
        Assert.True(f.CandidatesEnabled);

        f.Press(checkRev: 5);
        Assert.False(f.CandidatesEnabled);
        Assert.True(f.OnScheduleChanged());      // 盤面だけ変わっても
        Assert.False(f.CandidatesEnabled);       // 無効のまま
        Assert.True(f.OnCheckReflected(5));      // 押下前の世代の反映でも
        Assert.False(f.CandidatesEnabled);       // 無効のまま
        Assert.True(f.OnCheckReflected(6));      // 押下後の再検査が反映されて
        Assert.True(f.CandidatesEnabled);        // 再有効化
    }

    [Fact]
    public void FlowIgnoresEveryNotificationAfterClose()
    {
        var f = new GuidedFixFlow();
        f.Press(1);
        f.Close();
        Assert.False(f.OnScheduleChanged());
        Assert.False(f.OnCheckReflected(99));
        Assert.False(f.CandidatesEnabled);
        f.Press(2); // 閉じた後の押下は無視
        Assert.True(f.Closed);
    }
}
