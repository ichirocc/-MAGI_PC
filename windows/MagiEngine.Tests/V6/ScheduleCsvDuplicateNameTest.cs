using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Xunit;

namespace MagiEngine.Tests.V6;

/// <summary>[Android 外部レビュー P2] 同じ名前の職員が複数いる勤務表CSVは、その名前の行を取り込まず警告する（先勝ちで別の職員を上書きしない）。</summary>
public class ScheduleCsvDuplicateNameTest
{
    private static MagiState BuildState() => MinimalState.Build(
        staffList: new List<Staff> { new("山田", 0), new("鈴木", 0), new("山田", 0) },
        schedule: Enumerable.Range(0, 3).Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, 7).ToList()).ToList());

    private static int[][] Base() => new[]
    {
        new[] { 1, 1, 1, 1, 1, 1, 1 },
        new[] { 0, 0, 0, 0, 0, 0, 0 },
        new[] { 0, 1, 0, 1, 0, 1, 0 },
    };

    private static int[][] Clone(int[][] a) => a.Select(r => (int[])r.Clone()).ToArray();

    [Fact]
    public void SameNameStaffRoundTripLeavesBoardUnchangedAndWarns()
    {
        var st = BuildState();
        var sched = Base();
        var r = ScheduleCsvBridge.Parse(ScheduleCsvBridge.Build(st, sched), st, Clone(sched));
        for (var i = 0; i < sched.Length; i++) Assert.Equal(sched[i], r.Schedule[i]);
        Assert.Equal(new[] { "山田" }, r.AmbiguousNames);
        Assert.Equal(1, r.Matched);
        Assert.Equal("同じ名前の職員が複数いるため取り込みませんでした: 山田", ScheduleCsvBridge.AmbiguousText(r.AmbiguousNames));
    }

    [Fact]
    public void AmbiguousRowsAreNotAppliedWhileOthersAre()
    {
        var st = BuildState();
        var b = Base();
        var r = ScheduleCsvBridge.Parse("山田,休,休,休,休,休,休,休\n鈴木,A,A,A,A,A,A,A\n", st, Clone(b));
        Assert.Equal(b[0], r.Schedule[0]);
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 1, 1 }, r.Schedule[1]);
        Assert.Equal(b[2], r.Schedule[2]);
    }

    [Fact]
    public void DuplicateRowsForUniqueNameKeepLastRowAndWarn()
    {
        var st = BuildState();
        var r = ScheduleCsvBridge.Parse("鈴木,A,A,A,A,A,A,A\n鈴木,休,A,休,A,休,A,休\n", st, Clone(Base()));
        Assert.Equal(new[] { 0, 1, 0, 1, 0, 1, 0 }, r.Schedule[1]);
        Assert.Equal(new[] { "鈴木" }, r.DuplicateRowNames);
        Assert.Empty(r.AmbiguousNames);
    }
}
