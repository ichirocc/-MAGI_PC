using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[S6] 承認フロー R1〜R5 の純関数（Kotlin <c>RelaxApprovalTest</c> の 1 対 1）。</summary>
public class RelaxApprovalTest
{
    private static RelaxTrial.Result Result(int h0 = 3, int rr = 1) => new(0, 2, 0, 4, new List<RelaxTrial.Relax>(), new List<RelaxTrial.Relax> { new(0, 1, 1) },
        h0, h0, h0, rr, new List<RelaxTrial.Move> { new(0, 2, 0, 1) });
    private static RelaxApproval.Flow R1() => RelaxApproval.Start(Result(), true, "t1", "fp");
    private static RelaxApproval.Flow R3() => RelaxApproval.Next(RelaxApproval.Next(R1(), true), true);

    [Fact]
    public void ImprovingResultOpensReadOnlyR1()
    {
        var f = R1();
        Assert.Equal(RelaxApproval.Step.R1, f.Step);
        Assert.Equal(1, f.ExpectedHard);
    }

    [Fact]
    public void StaleGoesToR4AndChangesNothing()
    {
        Assert.Equal(RelaxApproval.EndReason.Stale, RelaxApproval.Start(Result(), false, "t", "f").End);
        var f = RelaxApproval.Next(R1(), false);
        Assert.Equal(RelaxApproval.Step.R4, f.Step);
        Assert.Equal(RelaxApproval.EndReason.Stale, f.End);
        Assert.Equal(RelaxApproval.EndReason.Stale, RelaxApproval.Finish(R3(), RelaxApproval.Verdict.Stale).End);
    }

    [Fact]
    public void NoWallStoppedUnavailableGoToR4WithDistinctReasons()
    {
        var a = RelaxApproval.Start(RelaxTrial.NoWall, true, "t", "f");
        var b = RelaxApproval.Start(RelaxTrial.Stopped, true, "t", "f");
        var c = RelaxApproval.Start(new RelaxTrial.Unavailable("未割当"), true, "t", "f");
        foreach (var x in new[] { a, b, c }) Assert.Equal(RelaxApproval.Step.R4, x.Step);
        Assert.Equal(3, new[] { a.End, b.End, c.End }.Distinct().Count());
        Assert.Equal("未割当", c.Detail);
        foreach (var e in new[] { a.End!.Value, b.End!.Value })
        {
            Assert.DoesNotContain("解消不能", RelaxApproval.TextOf(e));
            Assert.DoesNotContain("0件", RelaxApproval.TextOf(e));
        }
        Assert.Equal(RelaxApproval.EndReason.NoGain, RelaxApproval.Start(Result(rr: 3), true, "t", "f").End);
    }

    [Fact]
    public void BlankReasonDisablesApprove()
    {
        Assert.False(RelaxApproval.CanApprove("", Result(), true));
        Assert.False(RelaxApproval.CanApprove("  \n", Result(), true));
        Assert.True(RelaxApproval.CanApprove("師長と確認済み", Result(), true));
        Assert.False(RelaxApproval.CanApprove("理由", Result(), false));
        Assert.False(RelaxApproval.CanApprove("理由", RelaxTrial.NoWall, true));
    }

    [Fact]
    public void PostCheckMismatchRollsBack()
    {
        var p = RelaxApproval.Press(R3(), "d1")!;
        var bad = RelaxApproval.Finish(p, new RelaxApproval.Verdict.Applied(2));
        Assert.Equal(RelaxApproval.Step.R4, bad.Step);
        Assert.Equal(RelaxApproval.EndReason.RolledBack, bad.End);
        var ok = RelaxApproval.Finish(p, new RelaxApproval.Verdict.Applied(1));
        Assert.Equal(RelaxApproval.Step.R5, ok.Step);
        Assert.Equal("d1", ok.DecisionId);
        Assert.Equal(RelaxApproval.EndReason.SaveFailed, RelaxApproval.Finish(p, RelaxApproval.Verdict.SaveFailed).End);
    }

    [Fact]
    public void DoublePressIsNoOp()
    {
        var p = RelaxApproval.Press(R3(), "d1")!;
        Assert.Null(RelaxApproval.Press(p, "d1"));
        Assert.Null(RelaxApproval.Press(R1(), "d1"));
    }

    [Fact]
    public void DeclineEndsWithoutChange() =>
        Assert.Equal(RelaxApproval.EndReason.Declined, RelaxApproval.DeclineFlow(R3()).End);

    [Fact]
    public void DecisionIdIsDeterministic()
    {
        Assert.Equal(RelaxApproval.DecisionId("t1", 1000L), RelaxApproval.DecisionId("t1", 1000L));
        Assert.NotEqual(RelaxApproval.DecisionId("t1", 1000L), RelaxApproval.DecisionId("t1", 1001L));
        Assert.Equal(8, RelaxApproval.DecisionId("t1", 1000L).Length);
    }

    [Fact]
    public void LogLinesCarryDecisionAndResult()
    {
        var l = RelaxApproval.ApprovalLog("d1", "t1", "確認済み", new[] { "A 夜 上限0→1" });
        Assert.StartsWith("S6承認 decisionId=d1 trial=t1 理由=確認済み 前→後 A 夜 上限0→1", l);
        Assert.Equal("S6反映 結果=復旧 check=2", RelaxApproval.ApplyLog("復旧", 2));
    }

    [Fact]
    public void SearchingAndFailedTrialsGetTheirOwnReasonsNotStale()
    {
        var a = RelaxApproval.Start(null, true, "t", "f", searching: true);
        var b = RelaxApproval.Start(null, true, "t", "f", failed: true);
        Assert.Equal(RelaxApproval.EndReason.Searching, a.End);
        Assert.Equal(RelaxApproval.EndReason.Failed, b.End);
        Assert.All(new[] { a, b }, f => Assert.Equal(RelaxApproval.Step.R4, f.Step));
        Assert.Equal(RelaxApproval.EndReason.Stale, RelaxApproval.Start(null, false, "t", "f", searching: true, failed: true).End);
        Assert.Equal(RelaxApproval.Step.R1, RelaxApproval.Start(Result(), true, "t", "f", searching: true).Step);
    }
}
