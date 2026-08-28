using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.275.0 移植元] C1RepairIndex（読取専用索引）の各ルックアップを、手計算で答えを設計した最小盤面で固定する。
/// </summary>
public class C1RepairIndexTest
{
    private static MagiState St(int days, int staff, IReadOnlyList<IReadOnlyList<int>> sched, IReadOnlyList<C1Row> cons1)
    {
        string end = "2026-01-" + days.ToString().PadLeft(2, '0');
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "", "") };
        return MinimalState.Build(
            startDate: "2026-01-01", endDate: end,
            shifts: shifts, groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, staff).Select(i => new Staff($"s{i}", 0)).ToList(),
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: sched, cons1: cons1);
    }

    [Fact]
    public void IndexEnumeratesWindowsDaysGainAndDonorMargin()
    {
        // s0: X X Y Y  s1: Y Y X X, ルール「X 2日窓≥1」(day1=2,day2=1)。
        //   不足窓: s0[2,3]=X 0個・s1[0,1]=X 0個 の計2窓。
        var s = St(
            4, 2,
            new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2 }, new List<int> { 2, 2, 1, 1 } },
            new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(s);
        var idx = C1RepairIndex.Build(p, s.Schedule.ToIntArray2D());

        Assert.True(idx.HasActionable);
        Assert.Equal(2, idx.Windows.Count);
        Assert.Equal(2, idx.DeficitTotal); // 不足合計=2

        // dayToWindows: s0窓は日2,3を含む / s1窓は日0,1を含む。
        Assert.Single(idx.WindowsCovering(0));
        Assert.All(idx.WindowsCovering(0), w => Assert.Equal(1, w.Staff));
        Assert.All(idx.WindowsCovering(2), w => Assert.Equal(0, w.Staff));
        Assert.All(idx.WindowsCovering(3), w => Assert.Equal(0, w.Staff));

        // staffRuleWindows: (staff, ruleIndex=0)
        Assert.Single(idx.ActiveWindows(0, 0));
        Assert.Single(idx.ActiveWindows(1, 0));

        // expectedGain: 不足窓のY候補日をXにすると各1窓解消。X既在日は候補でない=0。
        //   [3.279.0/C1-07] API は対象シフトを含む3引数へ（shift=1=X）。
        Assert.Equal(1, idx.ExpectedGain(0, 2, 1));
        Assert.Equal(1, idx.ExpectedGain(0, 3, 1));
        Assert.Equal(1, idx.ExpectedGain(1, 0, 1));
        Assert.Equal(0, idx.ExpectedGain(0, 0, 1)); // 日0は既にX=候補でない

        // donorMargin: s0 day0(X) は窓[0,1]のみ(z=2,余裕1)=安全。day1(X)は窓[1,2](z=1,余裕0)=危険。
        Assert.Equal(1, idx.DonorMargin(0, 0));
        Assert.Equal(0, idx.DonorMargin(0, 1));
        // Yセル(c1規則が依存しない)は無限大=常に安全。
        Assert.Equal(int.MaxValue, idx.DonorMargin(0, 2));
    }

    [Fact]
    public void IndexIsEmptyWhenNoDeficientWindow()
    {
        // 各日Xが存在し全窓充足=不足窓ゼロ。
        var s = St(3, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }, new List<C1Row> { new("2", "X", "1") });
        var idx = C1RepairIndex.Build(new Problem(s), s.Schedule.ToIntArray2D());
        Assert.False(idx.HasActionable);
        Assert.Empty(idx.Windows);
    }

    [Fact]
    public void ExpectedGainSeparatesTargetShifts()
    {
        // [3.279.0/C1-07 移植元] 同一職員・同一日に別シフトの不足窓が併存しても gain が混合されない。
        //   盤面 [休,休]・ルール「X 2日窓≥1」(1手で解消=gain1) と「Y 2日窓≥2」(1手では解消しない=gain0)。
        //   旧 API(staff,day) は max 混合で両問い合わせとも 1 になり判別不能だった。
        var s = St(
            2, 1, new List<IReadOnlyList<int>> { new List<int> { 0, 0 } },
            new List<C1Row> { new("2", "X", "1"), new("2", "Y", "2") });
        var idx = C1RepairIndex.Build(new Problem(s), s.Schedule.ToIntArray2D());
        Assert.Equal(1, idx.ExpectedGain(0, 0, 1)); // Xへの1手は窓を解消=gain1
        Assert.Equal(0, idx.ExpectedGain(0, 0, 2)); // Yは必要2で1手では解消しない=gain0
    }
}
