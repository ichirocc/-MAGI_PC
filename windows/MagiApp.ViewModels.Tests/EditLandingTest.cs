using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.644.0] つくる前の確認の、セルを持たない行の着地先（Kotlin <c>EditLandingTest</c> と 1 対 1）。</summary>
public class EditLandingTest
{
    [Fact]
    public void DayNeedLandsOnTheNeedCalendarOfThatShift()
    {
        var core = new ConstraintMus.Item[] { new ConstraintMus.DayNeed(11, 2, 3), new ConstraintMus.RangeCap(0, 2, 4) };
        var l = GuidedFixRules.LandingForProofCore(core, zeroCap: false);
        Assert.Equal(new EditLanding(0, null, NeedShift: 2), l);
        Assert.Equal("必要人数を見直す", GuidedFixRules.LandingButtonLabel(l));
    }

    [Fact]
    public void ZeroCapWinsWhenTheProofIsTaggedZeroCap()
    {
        var core = new ConstraintMus.Item[] { new ConstraintMus.DayNeed(11, 2, 3), new ConstraintMus.RangeCap(5, 2, 0) };
        var l = GuidedFixRules.LandingForProofCore(core, zeroCap: true);
        Assert.Equal(new EditLanding(2, "yr_count", CountStaff: 5, CountShift: 2, Label: GuidedFixRules.LandingZeroCap), l);
        Assert.Equal(GuidedFixRules.LandingZeroCap, GuidedFixRules.LandingButtonLabel(l));
        Assert.Equal(1, GuidedFixRules.DoorFor(l!));
    }

    [Fact]
    public void RangeAndWindowRulesLandOnCountsAndConstraints()
    {
        Assert.Equal(new EditLanding(2, "yr_count", CountStaff: 3, CountShift: 1), GuidedFixRules.LandingForProofCore(new ConstraintMus.Item[] { new ConstraintMus.RangeFloor(3, 1, 2) }, zeroCap: false));
        Assert.Equal("回数の下限・上限を見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(2, "yr_count", CountStaff: 3, CountShift: 1)));
        Assert.Equal(new EditLanding(2, "yr_cons", Label: GuidedFixRules.LandingWindow), GuidedFixRules.LandingForProofCore(new ConstraintMus.Item[] { new ConstraintMus.WindowRule(1, 7, 2) }, zeroCap: false));
        Assert.Null(GuidedFixRules.LandingForProofCore(new ConstraintMus.Item[] { new ConstraintMus.WishPin(0, 1, 1) }, zeroCap: false));
    }

    [Fact]
    public void ExistingLabelsAndDoorsAreUnchanged()
    {
        Assert.Equal("希望を見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(0, null, 3)));
        Assert.Equal("禁止の並びを見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(2, "yr_cons")));
        Assert.Equal("担当を見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(2, "yr_ws1")));
        Assert.Equal("データを見直す", GuidedFixRules.LandingButtonLabel(null));
        Assert.Equal(2, GuidedFixRules.DoorFor(new EditLanding(2, "yr_ws1")));
        Assert.Equal(0, GuidedFixRules.DoorFor(new EditLanding(0, null, 3)));
    }
}
