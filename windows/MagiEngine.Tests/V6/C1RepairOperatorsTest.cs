using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.275.0/3.276.0 移植元・フェーズ6ピース29] <see cref="C1RepairOperators"/>（façade）が既存実装へ
/// <b>1:1 委譲</b>（挙動完全同一）であることと、<see cref="C1RepairOperators.HasActionableC1"/> ゲートが
/// c1不足ゼロ盤面で<b>provably-safe</b>（<see cref="V6HotfixPasses.ApplyC1WindowPolish"/> が no-op）で
/// あることを固定する。
///
/// 3件の正しさテスト（<c>indexChainRepairReducesC1ViaDirectMove</c>/<c>FillsHoleViaChain</c>/
/// <c>IsNoOpWhenNoC1</c>）は <see cref="V6HotfixPasses.ApplyC1IndexChainRepair"/> 自体を直接検証する
/// 内容のため、フェーズ6ピース28で <c>V6HotfixPassesC1IndexChainTest.cs</c> へ先行して移植済み。
/// </summary>
public class C1RepairOperatorsTest
{
    private static MagiState St(
        int days, int staff, IReadOnlyList<IReadOnlyList<int>> sched, IReadOnlyList<C1Row> cons1) =>
        MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-" + days.ToString("D2"),
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, staff).Select(i => new Staff($"s{i}", 0)).ToList(),
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: sched, cons1: cons1);

    // c1不足を含む盤面（各オペレータが実際に手を試す）。
    private static MagiState DeficientState() =>
        St(4, 2, new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2 }, new List<int> { 2, 2, 1, 1 } },
            new List<C1Row> { new("2", "X", "1") });

    [Fact]
    public void WindowPolishDelegatesIdentically()
    {
        var s = DeficientState();
        var sc = s.Schedule.ToIntArray2D();
        var direct = V6HotfixPasses.ApplyC1WindowPolish(s, sc, maxPasses: 3, seed: 0x1C1L);
        var viaFacade = C1RepairOperators.SelfRelocateAndSameDaySwap(s, s.Schedule.ToIntArray2D(), maxPasses: 3, seed: 0x1C1L);
        for (var i = 0; i < direct.NewSchedule.Length; i++) Assert.Equal(direct.NewSchedule[i], viaFacade.NewSchedule[i]);
        Assert.Equal(direct.Applied, viaFacade.Applied);
        Assert.Equal(direct.AfterTotal, viaFacade.AfterTotal);
    }

    [Fact]
    public void TemporalFlowDelegatesIdentically()
    {
        var s = DeficientState();
        var direct = C1TemporalFlowPolish.Apply(s, s.Schedule.ToIntArray2D(), seed: 0xC1F10FL);
        var viaFacade = C1RepairOperators.TemporalFlow(s, s.Schedule.ToIntArray2D(), seed: 0xC1F10FL);
        for (var i = 0; i < direct.NewSchedule.Length; i++) Assert.Equal(direct.NewSchedule[i], viaFacade.NewSchedule[i]);
        Assert.Equal(direct.AfterTotal, viaFacade.AfterTotal);
    }

    [Fact]
    public void WideBeamDelegatesIdentically()
    {
        var s = DeficientState();
        var direct = V6HotfixPasses.ApplyC1BeamPolish(s, s.Schedule.ToIntArray2D(), seed: 0x1CBEAL);
        var viaFacade = C1RepairOperators.WideBeam(s, s.Schedule.ToIntArray2D(), seed: 0x1CBEAL);
        for (var i = 0; i < direct.NewSchedule.Length; i++) Assert.Equal(direct.NewSchedule[i], viaFacade.NewSchedule[i]);
        Assert.Equal(direct.AfterTotal, viaFacade.AfterTotal);
    }

    [Fact]
    public void ExactWindowDelegatesIdentically()
    {
        var s = DeficientState();
        var direct = V6HotfixPasses.ApplyC1ExactWindowRepair(s, s.Schedule.ToIntArray2D());
        var viaFacade = C1RepairOperators.ExactWindow(s, s.Schedule.ToIntArray2D());
        for (var i = 0; i < direct.NewSchedule.Length; i++) Assert.Equal(direct.NewSchedule[i], viaFacade.NewSchedule[i]);
        Assert.Equal(direct.AfterTotal, viaFacade.AfterTotal);
    }

    [Fact]
    public void JointLnsDelegatesIdentically()
    {
        var s = DeficientState();
        var direct = C1JointLnsPolish.Apply(s, s.Schedule.ToIntArray2D());
        var viaFacade = C1RepairOperators.JointLns(s, s.Schedule.ToIntArray2D());
        for (var i = 0; i < direct.NewSchedule.Length; i++) Assert.Equal(direct.NewSchedule[i], viaFacade.NewSchedule[i]);
        Assert.Equal(direct.AfterTotal, viaFacade.AfterTotal);
    }

    // ---- 3.276.0: index駆動C1修復オペレータ ----

    [Fact]
    public void IndexChainRepairDelegatesIdentically()
    {
        var s = DeficientState();
        var direct = V6HotfixPasses.ApplyC1IndexChainRepair(s, s.Schedule.ToIntArray2D(), seed: 0x1C1D2L);
        var viaFacade = C1RepairOperators.IndexChainRepair(s, s.Schedule.ToIntArray2D(), seed: 0x1C1D2L);
        for (var i = 0; i < direct.NewSchedule.Length; i++) Assert.Equal(direct.NewSchedule[i], viaFacade.NewSchedule[i]);
        Assert.Equal(direct.Applied, viaFacade.Applied);
        Assert.Equal(direct.AfterTotal, viaFacade.AfterTotal);
    }

    [Fact]
    public void GateIsSafeOnC1CleanBoard()
    {
        // c1不足ゼロ盤面: hasActionableC1=false かつ applyC1WindowPolish は正真正銘の no-op。
        var s = St(3, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.False(C1RepairOperators.HasActionableC1(p, sc));
        var res = V6HotfixPasses.ApplyC1WindowPolish(s, sc, maxPasses: 3);
        Assert.Equal(0, res.Applied); // gateがskipする窓研磨は採用0
        var norm = ScheduleUtil.NormalizeSchedule(sc, p);
        for (var i = 0; i < res.NewSchedule.Length; i++) Assert.Equal(norm[i], res.NewSchedule[i]); // 盤面は不変(normalize済み入力と一致)
    }
}
