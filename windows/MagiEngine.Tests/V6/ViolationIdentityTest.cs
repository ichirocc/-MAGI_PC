using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>[3.663.0] 違反の同一性（種類＋職員＋日付、被覆は種類＋シフト＋日付）。件数が減っても別の場所に生まれた必須違反は見分ける（Kotlin <c>ViolationIdentityTest</c> と 1 対 1）。</summary>
public class ViolationIdentityTest
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> M(params (string Key, string[] Classes)[] e) =>
        e.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Classes);

    private static ViolationReport Rep(Dictionary<string, int> b, IReadOnlyDictionary<string, IReadOnlyList<string>>? cells = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? need = null, IReadOnlyDictionary<string, IReadOnlyList<string>>? count = null)
    {
        var hard = b.Where(kv => MirrorKeys.Hard.Contains(kv.Key)).Sum(kv => kv.Value);
        return new ViolationReport(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
            b, b.Values.Sum(), hard, b.Values.Sum() - hard, 0.0, CellFamilies: cells, CountFamilies: count, NeedFamilies: need);
    }

    [Fact]
    public void KeysSeparateFamilySpaceAndPlace()
    {
        var r = Rep(new() { ["c3n"] = 1, ["c1"] = 1, ["covU"] = 1, ["apt"] = 1 }, cells: M(("0,3", new[] { "vio-c3n", "vio-c1" })),
            need: M(("2,5", new[] { "vio-covU" })), count: M(("1,0", new[] { "vio-aptLow" })));
        Assert.Equal(new HashSet<ViolationKey> { new("c3n", ViolationKey.Kind.Cell, 0, 3), new("covU", ViolationKey.Kind.Need, 2, 5) },
            ViolationIdentity.Keys(r, hardOnly: true));
        Assert.Contains(new ViolationKey("apt", ViolationKey.Kind.Count, 1, 0), ViolationIdentity.Keys(r));
    }

    /// <summary>A さん（0 行）の禁止の並び 2 件を解消し、B さん（1 行）に 1 件を作る＝件数は減るが新たな必須違反。</summary>
    [Fact]
    public void SameFamilyMovedToAnotherStaffIsANewHardViolation()
    {
        var before = Rep(new() { ["c3n"] = 2 }, cells: M(("0,3", new[] { "vio-c3n" }), ("0,9", new[] { "vio-c3n" })));
        var after = Rep(new() { ["c3n"] = 1 }, cells: M(("1,4", new[] { "vio-c3n" })));
        Assert.Null(UnifiedViolationChecker.NewHardFamilyViolation(before, after));   // 族の件数だけでは通ってしまう
        Assert.Equal(new HashSet<ViolationKey> { new("c3n", ViolationKey.Kind.Cell, 1, 4) }, ViolationIdentity.NewHard(before, after));
        Assert.True(ViolationIdentity.IntroducesHardViolation(before, after));
    }

    [Fact]
    public void ResolvingWithoutNewPlacesPasses()
    {
        var before = Rep(new() { ["c3n"] = 1, ["covU"] = 1 }, cells: M(("0,3", new[] { "vio-c3n" })), need: M(("2,5", new[] { "vio-covU" })));
        var after = Rep(new() { ["covU"] = 1, ["c1"] = 1 }, cells: M(("1,1", new[] { "vio-c1" })), need: M(("2,5", new[] { "vio-covU" })));
        Assert.False(ViolationIdentity.IntroducesHardViolation(before, after));
        var d = ViolationIdentity.Of(before, after);
        Assert.Equal(new HashSet<ViolationKey> { new("c3n", ViolationKey.Kind.Cell, 0, 3) }, d.Resolved);
        Assert.Equal(new HashSet<ViolationKey> { new("c1", ViolationKey.Kind.Cell, 1, 1) }, d.Appeared);
    }
}
