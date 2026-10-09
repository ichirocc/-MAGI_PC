namespace MagiApp.ViewModels.Tests;

/// <summary>[Android 3.643.0 同期] 希望の行は登録の有無だけを数える: 拡張希望だけの職員も「入力あり」、未入力は希望なしと未確認を区別しない。</summary>
public class ChecklistLogicTest
{
    [Fact]
    public void CountsRegularAndExtendedOnlyAndNoInput()
    {
        var c = WishEntryCounts.Of(5, new[] { "0,3", "0,4", "2,1" }, new[] { "2,5", "3,0" });
        Assert.Equal(new WishEntryCounts(Entered: 3, ExtOnly: 1, NoInput: 2), c);
        Assert.Equal("入力あり 3名（拡張希望のみ 1名）・未入力 2名", c.Text());
        Assert.Equal("入力あり 0名・未入力 4名", WishEntryCounts.Of(4, Array.Empty<string>(), Array.Empty<string>()).Text());
    }

    [Fact]
    public void IgnoresKeysOutsideTheStaffRange() =>
        Assert.Equal(new WishEntryCounts(1, 0, 1), WishEntryCounts.Of(2, new[] { "1,0", "7,0", "x,0" }, Array.Empty<string>()));
}
