namespace MagiApp.ViewModels.Tests;

/// <summary>[Android 3.643.0 同期] 直したあとの 1 行は、同じ問題について「解消したか」と「ほかに何件残るか」を言う（Kotlin <c>FixOutcomeTextTest</c> と 1 対 1）。</summary>
public class FixOutcomeTextTest
{
    [Fact]
    public void AppliedMoveSaysWhatChangedAndWhatRemains()
    {
        Assert.Equal("山本 10/12「夜」→「休」 を当てました。必須違反 3→2。ほかの必須違反は 2 件残っています。", FixOutcomeText.Applied("山本 10/12「夜」→「休」", 3, 2, 40, 38));
        Assert.Equal("入替 を当てました。必須違反はなくなりました（合計 40→38）。", FixOutcomeText.Applied("入替", 1, 0, 40, 38));
        Assert.Equal("入替 を当てました。必須違反は変わらず 2 件（合計 40→39）。", FixOutcomeText.Applied("入替", 2, 2, 40, 39));
    }

    [Fact]
    public void GuidedFixSaysResolvedOrStillShort()
    {
        Assert.Equal("10/12(土) の「夜」の人員不足を解消しました。ほかの必須違反は 3 件残っています。", FixOutcomeText.Guided("10/12(土)", "夜", null, 3));
        Assert.Equal("10/12(土) の「夜」の人員不足を解消しました。必須違反はなくなりました。", FixOutcomeText.Guided("10/12(土)", "夜", null, 0));
        Assert.Equal("10/12(土) の「夜」はまだ 1人 足りません。必須違反は 4 件です。", FixOutcomeText.Guided("10/12(土)", "夜", 1, 4));
    }
}
