using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

public class StructuralHardResidualTest
{
    private static ViolationReport Rep(params (string Family, int Count)[] kv)
    {
        var b = kv.ToDictionary(f => f.Family, f => f.Count);
        var h = b.Values.Sum();
        return new ViolationReport(
            Violations: new Dictionary<string, string>(), NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(), Breakdown: b,
            Total: h, Hard: h, Soft: 0, WeightedScore: 0.0);
    }

    // 外部レビュー R5: c3n 壁でも covU が床を超えて残るなら、解ける HARD がある＝追加精製を省略しない。
    [Fact]
    public void C3nWallWithRepairableCovUIsNotStructural()
    {
        Assert.False(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 1), ("covU", 2)), hardFloor: 0, () => true));
    }

    [Fact]
    public void C3nWallWithCovUAtFloorIsStructural()
    {
        Assert.True(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 1), ("covU", 2)), hardFloor: 2, () => true));
        Assert.True(V6FinalPort.IsStructuralHardResidual(Rep(("covU", 2)), hardFloor: 2, () => false));
        Assert.False(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 1)), hardFloor: 0, () => false));
        Assert.False(V6FinalPort.IsStructuralHardResidual(Rep(("pref", 1)), hardFloor: 5, () => true));
    }
}
