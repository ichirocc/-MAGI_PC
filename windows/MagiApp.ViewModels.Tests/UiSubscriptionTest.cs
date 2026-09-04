using System.ComponentModel;

namespace MagiApp.ViewModels.Tests;

/// <summary>[レビュー指摘 2026-09-04] タブA→B→A で再購読される／多重購読しない／解除中は届かない。</summary>
public class UiSubscriptionTest
{
    private sealed class Src : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Fire() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("X"));
    }

    [Fact]
    public void AttachIsIdempotent_DetachStopsDelivery_ReattachResumes()
    {
        var src = new Src();
        var hits = 0;
        var sub = new UiSubscription(src, (_, _) => hits++);

        Assert.True(sub.Attach());
        Assert.False(sub.Attach());          // 同じタブを繰り返し表示しても多重購読しない
        src.Fire();
        Assert.Equal(1, hits);

        Assert.True(sub.Detach());           // タブを離れる
        Assert.False(sub.Detach());
        src.Fire();
        Assert.Equal(1, hits);               // 非表示中は届かない

        Assert.True(sub.Attach());           // タブへ戻る＝再購読（呼出側は true を見て再描画する）
        src.Fire();
        Assert.Equal(2, hits);
        Assert.True(sub.IsAttached);
    }
}
