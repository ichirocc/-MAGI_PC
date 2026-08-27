namespace MagiEngine.Tests;

/// <summary>
/// フェーズ0の疎通確認用テスト。`dotnet test` がこのサンドボックス（Linux）上で
/// 実際にビルド・実行できることを固定する（WinUI3側は別途Windows実機/CIで確認）。
/// </summary>
public class EngineInfoTest
{
    [Fact]
    public void PortedFromVersion_IsSet()
    {
        Assert.False(string.IsNullOrWhiteSpace(EngineInfo.PortedFromVersion));
    }
}
