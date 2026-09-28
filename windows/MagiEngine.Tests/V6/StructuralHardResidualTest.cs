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

    // [E0] 希望衝突の床に届いた HARD は解けない残り。c3w は希望どうしの衝突で証明された件数までだけ c3n と同列。
    [Fact]
    public void WishConflictFloorAndProvenC3wAreStructural()
    {
        Assert.True(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 3), ("c3w", 2)), hardFloor: 0, () => false, wishReached: true));
        Assert.False(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 3), ("c3w", 2)), hardFloor: 0, () => false));
        Assert.True(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 3), ("c3w", 2)), hardFloor: 0, () => true, c3wProven: 2));
        Assert.False(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 3), ("c3w", 2)), hardFloor: 0, () => true, c3wProven: 1));
        Assert.False(V6FinalPort.IsStructuralHardResidual(Rep(("c3n", 3), ("c3w", 2)), hardFloor: 0, () => true));
    }
}
