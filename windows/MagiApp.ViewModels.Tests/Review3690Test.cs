using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3690Test</c> の 1 対 1 移植。3.690.0: 適用経路の強化（族名の網羅・指紋なしは断る・ホームの主操作）。</summary>
public class Review3690Test
{
    [Fact]
    public void EveryHardFamilyHasItsOwnLabel()
    {
        foreach (var fam in MirrorKeys.Hard)
        {
            var l = FixApplyGate.FamLabel(fam);
            Assert.NotEqual("必須条件", l);
            Assert.NotEqual(fam, l);
        }
        Assert.Equal("必須条件", FixApplyGate.FamLabel("unknownFamily"));
    }

    [Fact]
    public void PreviewApplyWithoutOriginIsRefused()
    {
        Assert.Equal(CellSheetLogic.FixOriginUnknownMessage, CellSheetLogic.FixOriginRefusal(0L, fromPreview: true));
        Assert.Null(CellSheetLogic.FixOriginRefusal(123L, fromPreview: true));
        Assert.Null(CellSheetLogic.FixOriginRefusal(0L, fromPreview: false));
    }

    [Fact]
    public void NullTargetNeedsExplicitGlobalPolish()
    {
        Assert.Equal(CellSheetLogic.FixTargetUnknownMessage, CellSheetLogic.FixTargetRefusal(null, globalPolish: false));
        Assert.Null(CellSheetLogic.FixTargetRefusal(null, globalPolish: true));
        Assert.Null(CellSheetLogic.FixTargetRefusal(new FixTarget(null, 1, 0, 2, true, "必要人数"), globalPolish: false));
    }

    [Fact]
    public void HomePlanHasExactlyOnePrimaryAction()
    {
        foreach (var running in new[] { false, true })
        foreach (var hasResult in new[] { false, true })
        foreach (var hard0 in new[] { false, true })
        foreach (var saveFailed in new[] { false, true })
        foreach (var consults in new[] { 0, 2 })
        {
            var p = CellSheetLogic.HomePlanOf(running, hasResult, hard0, saveFailed: saveFailed, consults: consults);
            var expected = running ? HomePlanKind.Running : !hasResult ? HomePlanKind.Make : hard0 ? HomePlanKind.PreDist : HomePlanKind.List;
            Assert.Equal(expected, p.Kind);
            if (running) { Assert.Equal(HomeAction.None, p.Primary); Assert.False(p.PrimaryEnabled); }
            else { Assert.NotEqual(HomeAction.None, p.Primary); Assert.True(p.PrimaryEnabled); }
            Assert.NotEqual(p.Primary, p.Helper);
        }
    }

    [Fact]
    public void HomePlanOrderOfUnfinishedStages()
    {
        Assert.Equal(HomePlanKind.WishInfeasible, CellSheetLogic.HomePlanOf(false, true, false, infeasible: true, hasPinned: true).Kind);
        Assert.Equal(HomePlanKind.DataInfeasible, CellSheetLogic.HomePlanOf(false, true, false, infeasible: true).Kind);
        Assert.Equal(HomePlanKind.GuidedFix, CellSheetLogic.HomePlanOf(false, true, false, guided: true, hasMove: true).Kind);
        Assert.Equal(HomePlanKind.ApplyMove, CellSheetLogic.HomePlanOf(false, true, false, hasMove: true).Kind);
        Assert.Equal(HomePlanKind.Searching, CellSheetLogic.HomePlanOf(false, true, false, hasMove: true, focusBlank: false, fixSearching: true).Kind);
        Assert.Equal(HomePlanKind.Relax, CellSheetLogic.HomePlanOf(false, true, false, fixSearched: true, relax: true, wishes: true).Kind);
        Assert.Equal(HomePlanKind.WishFloor, CellSheetLogic.HomePlanOf(false, true, false, fixSearched: true, stalled: true, wishes: true).Kind);
        Assert.Equal(HomePlanKind.Wishes, CellSheetLogic.HomePlanOf(false, true, false, wishes: true).Kind);
    }
}
