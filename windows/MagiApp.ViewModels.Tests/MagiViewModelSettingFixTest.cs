using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;
// MagiEngine.Model.Range vs System.Range — same alias as MinimalState.cs, for the same reason.
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース8 追補] <c>MagiViewModel.kt</c> の
/// <c>applySettingFix(issue: SettingIssue)</c>（行1946-2015）の検証
/// （<c>MagiViewModel.Editing.cs</c> のクラスKDoc参照）。Kotlin原本には専用テストが無い
/// （<c>MagiViewModelEditingTest</c> と同じ経緯）。
/// [2026-09-01] <c>dismissInterrupted()</c>（中断バナーの破棄）はクラッシュ復旧機構全撤去に伴い
/// 削除済み——このファイルにあった対応テストも削除した。
///
/// <see cref="MagiViewModel.ApplySettingFix"/> は <c>OptimizeInFlight</c> ガード付きの
/// <see cref="MagiViewModel.ApplyStructure(MagiState)"/> を経由するため、
/// <see cref="MagiViewModelEditingTest"/> と同じ直列コレクションに属する。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelSettingFixTest
{
    public MagiViewModelSettingFixTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    // ===================================================================
    // ApplySettingFix — RemoveWish
    // ===================================================================

    [Fact]
    public void ApplySettingFixRemoveWishDeletesTheKey()
    {
        var st = MinimalState.Build(wishes: new Dictionary<string, int> { ["0,1"] = 1 });
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Wish, "職員A 2日目", "実現できない希望です", "希望を削除してください",
            Action: SettingFixAction.RemoveWish, WishKey: "0,1");

        vm.ApplySettingFix(issue);

        Assert.False(vm._state!.Wishes.ContainsKey("0,1"));
    }

    [Fact]
    public void ApplySettingFixRemoveWishNoOpWhenKeyAbsent()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Wish, "職員A 2日目", "実現できない希望です", "希望を削除してください",
            Action: SettingFixAction.RemoveWish, WishKey: "0,1");

        vm.ApplySettingFix(issue);

        Assert.Same(st, vm._state); // 何も変わらない＝ApplyStructure は呼ばれない
    }

    // ===================================================================
    // ApplySettingFix — None
    // ===================================================================

    [Fact]
    public void ApplySettingFixNoneActionIsANoOp()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Demand, "どこか", "問題", "直し方",
            Action: SettingFixAction.None);

        vm.ApplySettingFix(issue);

        Assert.Same(st, vm._state);
    }

    // ===================================================================
    // ApplySettingFix — CapDemand
    // ===================================================================

    [Fact]
    public void ApplySettingFixCapDemandClampsBothNeedsWhenOverCap()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("A", "A", "10", "12"),
        };
        var st = MinimalState.Build(shifts: shifts);
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Demand, "A", "必要人数が担当可能人数を超えています", "必要人数を下げてください",
            Action: SettingFixAction.CapDemand, DemandShiftIdx: 1, DemandCap: 5);

        vm.ApplySettingFix(issue);

        Assert.Equal("5", vm._state!.Shifts[1].Need1);
        Assert.Equal("5", vm._state!.Shifts[1].Need2);
    }

    [Fact]
    public void ApplySettingFixCapDemandOnlyClampsTheSideOverCap()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("A", "A", "3", "12"),
        };
        var st = MinimalState.Build(shifts: shifts);
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Demand, "A", "必要人数が担当可能人数を超えています", "必要人数を下げてください",
            Action: SettingFixAction.CapDemand, DemandShiftIdx: 1, DemandCap: 5);

        vm.ApplySettingFix(issue);

        Assert.Equal("3", vm._state!.Shifts[1].Need1); // 下限は元々cap以下なので不変
        Assert.Equal("5", vm._state!.Shifts[1].Need2);
    }

    [Fact]
    public void ApplySettingFixCapDemandNoOpWhenNeitherExceedsCap()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("A", "A", "3", "4"),
        };
        var st = MinimalState.Build(shifts: shifts);
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Demand, "A", "必要人数が担当可能人数を超えています", "必要人数を下げてください",
            Action: SettingFixAction.CapDemand, DemandShiftIdx: 1, DemandCap: 5);

        vm.ApplySettingFix(issue);

        Assert.Same(st, vm._state);
    }

    // ===================================================================
    // ApplySettingFix — ZeroRangeLo / ClampRangeLo
    // ===================================================================

    [Fact]
    public void ApplySettingFixClampRangeLoUpdatesLoOnly()
    {
        var st = MinimalState.Build(staffRange: new Dictionary<string, Range> { ["0,1"] = new("10", "8") });
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Range, "職員A / A", "下限が上限を超えています", "下限を下げてください",
            Action: SettingFixAction.ClampRangeLo, RangeKey: "0,1", NewLo: "8");

        vm.ApplySettingFix(issue);

        var r = vm._state!.StaffRange["0,1"];
        Assert.Equal("8", r.Lo);
        Assert.Equal("8", r.Hi); // 上限は不変
    }

    [Fact]
    public void ApplySettingFixZeroRangeLoDefaultsToEmptyRangeWhenKeyAbsent()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Range, "職員A / A", "下限が負です", "下限を0にしてください",
            Action: SettingFixAction.ZeroRangeLo, RangeKey: "0,1", NewLo: "0");

        vm.ApplySettingFix(issue);

        var r = vm._state!.StaffRange["0,1"];
        Assert.Equal("0", r.Lo);
        Assert.Equal("", r.Hi);
    }

    // ===================================================================
    // ApplySettingFix — DeleteDupSeq
    // ===================================================================

    [Fact]
    public void ApplySettingFixDeleteDupSeqRemovesFirstMatchingRow()
    {
        var st = MinimalState.Build(cons3n: new List<C3Row>
        {
            new(new[] { "A", "休" }),
            new(new[] { "A", "休" }),
        });
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Constraint, "連続パターン「A→休」(c3n)", "重複した制約です", "片方を削除してください",
            Action: SettingFixAction.DeleteDupSeq, SeqFamily: "c3n", SeqKey: "A→休");

        vm.ApplySettingFix(issue);

        Assert.Single(vm._state!.Cons3n); // 先頭1件のみ削除・もう1件は残る
    }

    // ===================================================================
    // ApplySettingFix — ClampGroupRangeLo
    // ===================================================================

    [Fact]
    public void ApplySettingFixClampGroupRangeLoReplacesMatchingRowByValue()
    {
        var target = new C41Row("G0", "A", "10", "8");
        var st = MinimalState.Build(cons41: new List<C41Row> { target });
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Constraint, "G0 / A", "下限が上限を超えています", "下限を下げてください",
            Action: SettingFixAction.ClampGroupRangeLo, GroupRangeFamily: "c41", GroupRangeRow: target, NewLo: "8");

        vm.ApplySettingFix(issue);

        Assert.Equal("8", vm._state!.Cons41[0].L);
        Assert.Equal("8", vm._state!.Cons41[0].U); // 上限は不変
    }

    [Fact]
    public void ApplySettingFixClampGroupRangeLoNoOpWhenRowNotFound()
    {
        var target = new C41Row("G0", "A", "10", "8");
        var other = new C41Row("G0", "B", "1", "2");
        var st = MinimalState.Build(cons41: new List<C41Row> { other });
        var vm = new MagiViewModel { _state = st };
        var issue = new SettingIssue(
            IssueKind.Constraint, "G0 / A", "下限が上限を超えています", "下限を下げてください",
            Action: SettingFixAction.ClampGroupRangeLo, GroupRangeFamily: "c41", GroupRangeRow: target, NewLo: "8");

        vm.ApplySettingFix(issue);

        Assert.Equal("1", vm._state!.Cons41[0].L); // 内容一致しないので変わらない
        Assert.Equal("2", vm._state!.Cons41[0].U);
    }
}
