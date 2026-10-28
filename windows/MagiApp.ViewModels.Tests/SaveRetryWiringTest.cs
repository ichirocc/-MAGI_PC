namespace MagiApp.ViewModels.Tests;

/// <summary>T-14（Android <c>docs/overhaul_design.md</c> §6.2）。VM の ReportAutoSave が適用する <see cref="SaveFailNotifier.Report"/> を、
/// 失敗→失敗の継続→やり直し→復旧の順で通す。Kotlin <c>SaveRetryWiringTest</c> の移植。</summary>
public class SaveRetryWiringTest
{
    [Fact]
    public void FailureRetryAndRecoveryFollowTheSameOutcomeTheViewModelApplies()
    {
        var n = new SaveFailNotifier();
        Assert.Equal(new SaveOutcome(null, SetDialog: true, Notice: null, RetryLater: false), n.Report(ok: true, retried: false));
        Assert.Equal(new SaveOutcome(SaveFailDialog.First, SetDialog: true, Notice: SaveNotice.Failed, RetryLater: true), n.Report(ok: false, retried: false));
        // 1.2 秒ごとの自動保存が失敗し続ける間は、ダイアログも通知も触らず、書き直しだけ仕掛ける。
        Assert.Equal(new SaveOutcome(null, SetDialog: false, Notice: null, RetryLater: true), n.Report(ok: false, retried: false));
        // 利用者の「もう一度保存する」も失敗＝「まだ保存できていません」。通知は重ねない。
        var retryFailed = n.Report(ok: false, retried: true);
        Assert.Equal(SaveFailDialog.Again, retryFailed.Dialog);
        Assert.True(retryFailed.SetDialog);
        Assert.Null(retryFailed.Notice);
        Assert.Equal(new SaveOutcome(null, SetDialog: true, Notice: SaveNotice.Recovered, RetryLater: false), n.Report(ok: true, retried: true));
        Assert.True(n.LastOk);
    }

    [Fact]
    public void SuccessAfterSuccessNeitherNotifiesNorRetries()
    {
        var n = new SaveFailNotifier();
        n.Report(ok: true, retried: false);
        var r = n.Report(ok: true, retried: false);
        Assert.Null(r.Notice);
        Assert.False(r.RetryLater);
    }
}
