using MagiApp.ViewModels.Tests.TestSupport;
using MagiEngine.Model;

namespace MagiApp.ViewModels.Tests;

/// <summary>拡張希望の入力（日とシフト index から追加・一覧・削除）と、拡張希望の日への希望設定の拒否。</summary>
public class ExtWishInputTest
{
    [Fact]
    public void AddListBlockAndRemove()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        Assert.True(vm.AddExtWishForDays(0, new[] { 1, 0 }, new[] { 1 }));
        var views = vm.ExtWishViews();
        Assert.Single(views);
        Assert.Equal(new[] { 1, 2 }, views[0].Days);
        Assert.Equal(new[] { "A" }, views[0].Kigou);
        vm.SetWish(0, 0, 1);
        Assert.False(vm._state!.Wishes.ContainsKey("0,0"));
        Assert.True(vm.Ui.MessageIsError);   // 黙って無視しない＝理由を失敗の色で出す
        Assert.Equal("職員A 12/1 は拡張希望の指定日なので、希望は入れられません", vm.Ui.Message);
        vm.SetWish(1, 0, 1);
        Assert.True(vm._state!.Wishes.ContainsKey("1,0"));
        vm.RemoveExtWish(0);
        Assert.Empty(vm.ExtWishViews());
    }

    [Fact]
    public void FullBanIsNotSaved()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };
        Assert.False(vm.AddExtWishForDays(0, new[] { 0 }, new[] { 0, 1 }));
        Assert.Empty(vm.ExtWishViews());
    }
}
