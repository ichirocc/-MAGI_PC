using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.580.0/backlog#26, Kotlin原本 <c>ArmReactivationTest</c>] <see cref="V6HotfixPasses.TargetFamiliesRemain"/>
/// の検証。既定OFFの専用修復腕（C2Polish/C42FlowPolish/C1成分修復/C3nMarginLnsPolish/CountChainPolish）を
/// 「対象違反が残っている局面でだけ」再活性化するための判定材料が、正式チェッカーの breakdown 生値を
/// 正しく見ることを固定する（腕自身の自己申告カウンタに頼らないという backlog#26 の要件そのもの）。
/// </summary>
public class ArmReactivationTest
{
    /// <summary>3職員×31日。X は毎日1人(need1=1)、A の X 上限超過1件のみを持つ。</summary>
    private static MagiState HighOnlyState()
    {
        var shifts = new List<Shift> { new("休み", "休", "", ""), new("X", "X", "1", ""), new("Y", "Y", "", "") };
        var groups = new List<Group> { new("G", "G") };
        const int t = 31;
        var a = Enumerable.Repeat(2, t).ToArray();
        var b = Enumerable.Repeat(2, t).ToArray();
        var c = Enumerable.Repeat(2, t).ToArray();
        for (var j = 0; j < t; j++) c[j] = 1;
        foreach (var j in new[] { 0, 10, 20 }) { a[j] = 1; c[j] = 2; }
        foreach (var j in new[] { 5, 15 }) { b[j] = 1; c[j] = 2; }
        return MinimalState.Build(
            startDate: "2026-10-01", endDate: "2026-10-31",
            shifts: shifts, groups: groups,
            staffList: new List<Staff> { new("A", 0), new("B", 0), new("C", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: new List<IReadOnlyList<int>> { a, b, c },
            staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,1"] = new("", "2"), ["1,1"] = new("", "2") });
    }

    [Fact]
    public void TargetFamiliesRemain_TrueWhenTheFamilyHasNonZeroBreakdown()
    {
        var st = HighOnlyState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        Assert.True(V6HotfixPasses.TargetFamiliesRemain(st, sched, false, "high"));
    }

    [Fact]
    public void TargetFamiliesRemain_FalseWhenTheFamilyIsAbsent()
    {
        var st = HighOnlyState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        Assert.False(V6HotfixPasses.TargetFamiliesRemain(st, sched, false, "c2"));
        Assert.False(V6HotfixPasses.TargetFamiliesRemain(st, sched, false, "c3n"));
        Assert.False(V6HotfixPasses.TargetFamiliesRemain(st, sched, false, "covU"));
    }

    [Fact]
    public void TargetFamiliesRemain_IsOrAcrossMultipleFamilies()
    {
        var st = HighOnlyState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        Assert.True(V6HotfixPasses.TargetFamiliesRemain(st, sched, false, "covU", "high"));
        Assert.False(V6HotfixPasses.TargetFamiliesRemain(st, sched, false, "covU", "c2"));
    }

    [Fact]
    public void TargetFamiliesRemain_FalseOnceTheViolationIsResolved()
    {
        var st = HighOnlyState();
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var fixedSched = CountChainPolish.ApplyCountChainPolish(st, sched).NewSchedule;
        Assert.False(V6HotfixPasses.TargetFamiliesRemain(st, fixedSched, false, "high"));
    }
}
