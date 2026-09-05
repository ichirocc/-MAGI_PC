using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [3.382.0/R-08 の移植] 族を追加したとき E7 フィルタが静かに壊れる（バケツ未登録の族は「常に表示」へ落ちる）のを防ぐ。
/// 固定するのは「分類表と <see cref="MirrorKeys.All"/> が過不足なく一致する」不変条件と、分類の意味論。
/// </summary>
public class VioBucketsTest
{
    [Fact]
    public void EveryViolationFamilyIsEitherBucketedOrExplicitlyBucketless()
    {
        var bucketed = VioBuckets.Buckets.SelectMany(b => b.Families).ToHashSet();
        var all = MirrorKeys.All.ToHashSet();

        Assert.Equal(VioBuckets.Buckets.Sum(b => b.Families.Count), bucketed.Count);
        Assert.Empty(all.Except(bucketed).Except(VioBuckets.BucketlessFamilies));
        Assert.Empty(bucketed.Except(all));
        Assert.Empty(VioBuckets.BucketlessFamilies.Except(all));
        Assert.Equal(VioBuckets.Buckets.Count, VioBuckets.AllKeys.Count);
    }

    [Fact]
    public void BucketlessFamiliesAreAlwaysVisibleAndAptVariantsFoldIntoApt()
    {
        foreach (var f in VioBuckets.BucketlessFamilies)
        {
            Assert.Null(VioBuckets.BucketOfFamily(f));
            Assert.True(VioBuckets.VioVisible("vio-" + f, new HashSet<string>()));
        }
        Assert.Equal("apt", VioBuckets.FamilyOfVioClass("vio-aptLow"));
        Assert.Equal("apt", VioBuckets.FamilyOfVioClass("vio-aptHigh"));
        Assert.Equal("count", VioBuckets.BucketOfFamily(VioBuckets.FamilyOfVioClass("vio-aptHigh")));
        Assert.True(VioBuckets.VioVisible("vio-covU", new HashSet<string> { "need" }));
        Assert.False(VioBuckets.VioVisible("vio-covU", new HashSet<string> { "pref" }));
    }

    [Fact]
    public void VisibleCellVioFallsThroughToALighterVisibleFamilyAndCountsEachBucketOncePerCell()
    {
        var ui = new UiState
        {
            ViolationCells = new Dictionary<string, string> { ["0,1"] = "vio-covU" },
            ViolationCellFamilies = new Dictionary<string, IReadOnlyList<string>> { ["0,1"] = new[] { "vio-covU", "vio-c1", "vio-covO" } },
            NeedViolations = new Dictionary<string, string> { ["2,1"] = "vio-covU" },
            CountViolations = new Dictionary<string, string> { ["0,3"] = "vio-aptHigh" },
        };
        Assert.Equal("vio-covU", VioBuckets.VisibleCellVio(ui, "0,1", VioBuckets.AllKeys));
        Assert.Equal("vio-c1", VioBuckets.VisibleCellVio(ui, "0,1", new HashSet<string> { "window" }));
        Assert.Null(VioBuckets.VisibleCellVio(ui, "0,1", new HashSet<string> { "pref" }));
        var counts = VioBuckets.BucketLocCounts(ui);
        Assert.Equal(2, counts["need"]);
        Assert.Equal(1, counts["window"]);
        Assert.Equal(1, counts["count"]);
    }
}
