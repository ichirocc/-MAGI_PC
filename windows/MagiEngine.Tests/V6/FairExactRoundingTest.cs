using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>fair の偏差はちょうど半分を切り上げる（分数で計算、Android 3.727.107。Kotlin <c>FairExactRoundingTest.kt</c>）。</summary>
public class FairExactRoundingTest
{
    [Fact]
    public void halfDeviationRoundsUp()
    {
        var st = MinimalState.Build(
            startDate: "2026-12-01", endDate: "2026-12-14",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("X", "X", "", "") },
            groups: new List<Group> { new("G0", "G0") },
            staffList: new List<Staff> { new("A", 0), new("B", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new[] { "", "3" } },
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 14).ToList(), Enumerable.Range(0, 14).Select(j => j < 7 ? 1 : 0).ToList() },
            staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,1"] = new("0", "3") });
        var p = new Problem(st);
        Assert.Equal(3, p.Apt[1][1]);
        var counts = new[] { 0, 7 };
        Assert.Equal(4 + 4, p.FairDevOfBucket(0, 1, x => counts[x]).Total);
    }
}
