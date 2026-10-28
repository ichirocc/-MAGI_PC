
namespace MagiApp.ViewModels.Tests;

/// <summary>[UI-S2] 勤務表の絞り込み札は 3 つ（人員・希望・回数）＋「その他」。外している札は閉じていても見せる（Kotlin <c>VioBucketsVisibleTest</c>）。</summary>
public class VioBucketsVisibleTest
{
    [Fact]
    public void ThreeCommonByDefaultAllWhenMoreIsOpen()
    {
        Assert.Equal(new[] { "need", "pref", "count" }, VioBuckets.VisibleBuckets(false, VioBuckets.AllKeys).Select(b => b.Key));
        Assert.Equal(VioBuckets.AllKeys.Count, VioBuckets.VisibleBuckets(true, VioBuckets.AllKeys).Count());
    }

    [Fact]
    public void AFilteredOutBucketStaysVisibleWhileClosed()
    {
        var enabled = VioBuckets.AllKeys.Where(k => k != "window").ToHashSet();
        Assert.Equal(new[] { "need", "pref", "count", "window" }, VioBuckets.VisibleBuckets(false, enabled).Select(b => b.Key));
    }
}
