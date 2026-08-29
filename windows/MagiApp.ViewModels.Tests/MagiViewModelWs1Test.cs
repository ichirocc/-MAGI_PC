using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;
// MagiEngine.Model.Range vs System.Range — same alias as MinimalState.cs, for the same reason.
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース9] <c>MagiViewModel.kt</c> のうち「Ws1Result を介する年間マスター(ws1)の
/// 追加/改名/削除系一式」「スキルグループCRUD」「対象月のナビゲーション」「窓ハイライト
/// (violationRange)」「参照件数クエリ」の検証（<c>MagiViewModel.Ws1.cs</c> のクラスKDoc参照）。
/// Kotlin原本には専用テストが無い（他ピースと同じ経緯）。
///
/// このピースの全編集ガードも <see cref="MagiViewModel.OptimizeInFlight"/> 経由で
/// <see cref="OptimizationRepository.Running"/> を読むため、<see cref="MagiViewModelEditingTest"/>
/// と同じ直列コレクションに属する。ファイルI/Oを伴わないため <c>DataDir</c> は不要。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelWs1Test
{
    public MagiViewModelWs1Test()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    // ===================================================================
    // Ws1EditShift / SetShiftNeed
    // ===================================================================

    [Fact]
    public void Ws1EditShiftUpdatesNameKigouAndNeeds()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1EditShift(1, "夜勤", "夜", "2", "3");

        var sh = vm._state!.Shifts[1];
        Assert.Equal("夜勤", sh.Name);
        Assert.Equal("夜", sh.Kigou);
        Assert.Equal("2", sh.Need1);
        Assert.Equal("3", sh.Need2);
        Assert.Contains("シフト編集: A → 夜勤(夜) 最低2/上限3", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void Ws1EditShiftBlanksNeedsLogAsDash()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1EditShift(1, "A", "A", "", "  ");

        Assert.Contains("最低-/上限-", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void Ws1EditShiftRefusesWhenTheSymbolIsAlreadyTaken()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // shifts: 休(0), A(1)

        vm.Ws1EditShift(1, "休み扱い", "休", "", ""); // renaming "A" to "休" collides with shift 0

        Assert.Equal("A", vm._state!.Shifts[1].Kigou); // unchanged
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("記号「休」はすでに別のシフトで使われています", vm.Ui.Message);
    }

    [Fact]
    public void Ws1EditShiftAllowsKeepingTheSameSymbolOnItself()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1EditShift(0, "休み", "休", "", ""); // renaming shift 0 to its own current symbol

        Assert.Equal("休み", vm._state!.Shifts[0].Name);
    }

    [Fact]
    public void SetShiftNeedChangesOnlyTheNeedsAndKeepsNameKigou()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.SetShiftNeed(1, "4", "6");

        var sh = vm._state!.Shifts[1];
        Assert.Equal("A", sh.Name);
        Assert.Equal("A", sh.Kigou);
        Assert.Equal("4", sh.Need1);
        Assert.Equal("6", sh.Need2);
    }

    [Fact]
    public void SetShiftNeedIsNoOpForAnOutOfRangeIndex()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.SetShiftNeed(99, "1", "2");

        Assert.Same(st, vm._state);
    }

    // ===================================================================
    // Ws1EditGroup / Ws1EditStaff
    // ===================================================================

    [Fact]
    public void Ws1EditGroupUpdatesNameAndKigou()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1EditGroup(0, "夜勤班", "夜班");

        var g = vm._state!.Groups[0];
        Assert.Equal("夜勤班", g.Name);
        Assert.Equal("夜班", g.Kigou);
    }

    [Fact]
    public void Ws1EditGroupRefusesACollidingSymbol()
    {
        var st = MinimalState.Build(groups: new List<Group> { new("G0", "G0"), new("G1", "G1") });
        var vm = new MagiViewModel { _state = st };

        vm.Ws1EditGroup(1, "改名", "G0"); // collides with group 0's symbol

        Assert.Equal("G1", vm._state!.Groups[1].Kigou);
        Assert.True(vm.Ui.MessageIsError);
    }

    [Fact]
    public void Ws1EditStaffUpdatesNameAndGroup()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1EditStaff(0, "山田太郎", 0);

        var s = vm._state!.StaffList[0];
        Assert.Equal("山田太郎", s.Name);
        Assert.Equal(0, s.GroupIdx);
        Assert.Contains("職員編集: 職員A → 山田太郎 / グループ[0]", vm.Ui.OpLog[0]);
    }

    // ===================================================================
    // Ws1SetGroupShift / Ws1SetGroupApt / Ws1ResetGroupApt / Ws1SetUse2
    // ===================================================================

    [Fact]
    public void Ws1SetGroupShiftTogglesTheCanDoMask()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // G0 canDo [休,A] by default

        vm.Ws1SetGroupShift(0, 1, false);

        Assert.Equal(new[] { 1, 0 }, vm._state!.GroupShift[0]);
        Assert.Contains("担当可否: グループ[0] × A → 担当しない", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void Ws1SetGroupAptSetsTheTargetString()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1SetGroupApt(0, 1, "5");

        Assert.Equal("5", vm._state!.GroupShiftApt[0][1]);
    }

    [Fact]
    public void Ws1SetGroupAptLogsUnsetForABlankValue()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1SetGroupApt(0, 1, "   ");

        Assert.Equal("", vm._state!.GroupShiftApt[0][1]); // Ws1Ops.SetGroupApt trims to ""
        Assert.Contains("適切回数: グループ[0] × A → 未設定", vm.Ui.OpLog[0]);
    }

    [Fact]
    public async Task Ws1ResetGroupAptClearsAllCellsAndReportsTheClearedCount()
    {
        var st = MinimalState.Build(groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "3", "5" } });
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.Ws1ResetGroupApt();

        Assert.Contains("apt強制リセット: 適切回数を全空欄に（2 件クリア）", vm.Ui.OpLog[0]);
        Assert.NotNull(vm.LastApplyStructureWithMessageTask);
        await vm.LastApplyStructureWithMessageTask!;
        Assert.Equal(new[] { "", "" }, vm._state!.GroupShiftApt[0]);
        Assert.Contains("適切回数(apt)を全リセットしました（2 件 → 0）｜必須=", vm.Ui.Message);
    }

    [Fact]
    public void Ws1SetUse2TogglesThePattern2Flag()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(use2Patterns: false) };

        vm.Ws1SetUse2(true);

        Assert.True(vm._state!.Use2Patterns);
        Assert.Contains("設定変更: 上限人数(2パターン目) → 使う", vm.Ui.OpLog[0]);
    }

    // ===================================================================
    // Ws1AddShift / Ws1AddGroup
    // ===================================================================

    [Fact]
    public void Ws1AddShiftAppendsANewShift()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1AddShift("夜勤", "夜", "1", "2");

        Assert.Equal(3, vm._state!.Shifts.Count);
        var added = vm._state!.Shifts[2];
        Assert.Equal("夜勤", added.Name);
        Assert.Equal("夜", added.Kigou);
        Assert.Equal("1", added.Need1);
        Assert.Equal("2", added.Need2);
    }

    [Fact]
    public void Ws1AddShiftIsNoOpForABlankSymbol()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.Ws1AddShift("名無し", "   ", "", "");

        Assert.Same(st, vm._state);
    }

    [Fact]
    public void Ws1AddShiftRefusesACollidingSymbol()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.Ws1AddShift("休み2", "休", "", "");

        Assert.Same(st, vm._state);
        Assert.True(vm.Ui.MessageIsError);
    }

    [Fact]
    public void Ws1AddGroupAppendsANewGroup()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.Ws1AddGroup("夜勤班", "夜班");

        Assert.Equal(2, vm._state!.Groups.Count);
        Assert.Equal("夜班", vm._state!.Groups[1].Kigou);
    }

    [Fact]
    public void Ws1AddGroupIsNoOpForABlankSymbol()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.Ws1AddGroup("名無し", "");

        Assert.Same(st, vm._state);
    }

    // ===================================================================
    // Ws1AddStaff / Ws1ResizeDays
    // ===================================================================

    [Fact]
    public void Ws1AddStaffAppendsARowFilledWithTheRestShift()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.Ws1AddStaff("職員C", 0);

        Assert.Equal(3, vm._state!.StaffList.Count);
        Assert.Equal("職員C", vm._state!.StaffList[2].Name);
        Assert.Equal(3, vm._currentSchedule!.Length);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0 }, vm._currentSchedule![2]); // filled with rest("休"=0)
    }

    [Fact]
    public void Ws1AddStaffIsNoOpWithoutASchedule()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.Ws1AddStaff("職員C", 0);

        Assert.Same(st, vm._state);
    }

    [Fact]
    public void Ws1ResizeDaysChangesTheDayCount()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.Ws1ResizeDays(10);

        Assert.Equal(10, vm._currentSchedule![0].Length);
        Assert.Equal(10, vm._currentSchedule![1].Length);
        // OpLog[0] races with the background RefreshCheck's own LogOp call (schedule is non-null
        // here, so ApplyStructure(Ws1Result) -> RefreshCheck() actually dispatches); search the
        // whole list instead of assuming index 0 (see the note at the Ws1RemoveShift test below).
        Assert.Contains(vm.Ui.OpLog, line => line.Contains("期間変更: 7日 → 10日"));
    }

    [Fact]
    public void Ws1ResizeDaysIsNoOpWithoutASchedule()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.Ws1ResizeDays(10);

        Assert.Same(st, vm._state);
    }

    // ===================================================================
    // SetMonth / ShiftMonth / SetNextMonth
    // ===================================================================

    [Fact]
    public void SetMonthSetsTheFirstOfMonthAndResizesToItsDayCount()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetMonth(2026, 2); // 2026 is not a leap year -> 28 days

        Assert.Equal("2026-02-01", vm._state!.StartDate);
        Assert.Equal(28, vm._currentSchedule![0].Length);
    }

    [Fact]
    public void SetMonthHandlesLeapFebruary()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetMonth(2028, 2); // 2028 is a leap year -> 29 days

        Assert.Equal(29, vm._currentSchedule![0].Length);
    }

    [Fact]
    public void SetMonthIsNoOpForAnInvalidMonth()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };

        vm.SetMonth(2026, 13);

        Assert.Same(st, vm._state);
    }

    [Fact]
    public void SetMonthIsNoOpWithoutState()
    {
        var vm = new MagiViewModel();

        vm.SetMonth(2026, 2);

        Assert.Null(vm._state);
    }

    [Fact]
    public void ShiftMonthAdvancesFromTheCurrentStartDate()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.StartDate = "2026-01-15";

        vm.ShiftMonth(1);

        Assert.Equal("2026-02-01", vm._state!.StartDate);
    }

    [Fact]
    public void ShiftMonthGoesBackwardsAcrossAYearBoundary()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.StartDate = "2026-01-15";

        vm.ShiftMonth(-1);

        Assert.Equal("2025-12-01", vm._state!.StartDate);
    }

    [Fact]
    public void ShiftMonthFallsBackToTodayWhenTheStartDateIsUnparseable()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.StartDate = "not a date";
        var expected = DateOnly.FromDateTime(DateTime.Now);
        expected = new DateOnly(expected.Year, expected.Month, 1).AddMonths(1);

        vm.ShiftMonth(1);

        Assert.Equal(expected.ToString("yyyy-MM-dd"), vm._state!.StartDate);
    }

    [Fact]
    public void SetNextMonthUsesTodayPlusOneMonth()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        var expected = DateOnly.FromDateTime(DateTime.Now).AddMonths(1);

        vm.SetNextMonth();

        Assert.Equal(expected.Year, DateOnly.Parse(vm._state!.StartDate).Year);
        Assert.Equal(expected.Month, DateOnly.Parse(vm._state!.StartDate).Month);
        Assert.Equal(1, DateOnly.Parse(vm._state!.StartDate).Day);
    }

    // ===================================================================
    // SkillGroups / AddSkillGroup / EditSkillGroup / RemoveSkillGroup / SetStaffSkill
    // ===================================================================

    [Fact]
    public void SkillGroupsReturnsTheCurrentListOrEmptyWithoutState()
    {
        var vm1 = new MagiViewModel();
        Assert.Empty(vm1.SkillGroups());

        var vm2 = new MagiViewModel { _state = MinimalState.Build(skillGroups: new List<Group> { new("夜勤可", "SK0") }) };
        Assert.Equal(new[] { "SK0" }, vm2.SkillGroups().Select(g => g.Kigou));
    }

    [Fact]
    public void AddSkillGroupAppendsANewSkillGroup()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.AddSkillGroup("夜勤可", "SK0");

        Assert.Single(vm._state!.SkillGroups);
        Assert.Equal("SK0", vm._state!.SkillGroups[0].Kigou);
    }

    [Fact]
    public void AddSkillGroupIsNoOpForABlankSymbol()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.AddSkillGroup("夜勤可", "");

        Assert.Same(st, vm._state);
    }

    [Fact]
    public void AddSkillGroupRefusesACollidingSymbol()
    {
        var st = MinimalState.Build(skillGroups: new List<Group> { new("夜勤可", "SK0") });
        var vm = new MagiViewModel { _state = st };

        vm.AddSkillGroup("別スキル", "SK0");

        Assert.Single(vm._state!.SkillGroups);
    }

    [Fact]
    public void EditSkillGroupRenamesAndPropagatesTheSymbolIntoConstraints()
    {
        var st = MinimalState.Build(
            skillGroups: new List<Group> { new("夜勤可", "SK0") },
            cons41s: new List<C41Row> { new("SK0", "A", "1", "2") });
        var vm = new MagiViewModel { _state = st };

        vm.EditSkillGroup(0, "深夜専任", "SK1");

        Assert.Equal("深夜専任", vm._state!.SkillGroups[0].Name);
        Assert.Equal("SK1", vm._state!.SkillGroups[0].Kigou);
        // 記号変更の伝播: cons41s の参照も一括置換される（幽霊行防止）。
        Assert.Equal("SK1", vm._state!.Cons41s[0].GroupKigou);
    }

    [Fact]
    public void EditSkillGroupRefusesACollidingSymbol()
    {
        var st = MinimalState.Build(skillGroups: new List<Group> { new("A", "SK0"), new("B", "SK1") });
        var vm = new MagiViewModel { _state = st };

        vm.EditSkillGroup(1, "改名", "SK0");

        Assert.Equal("SK1", vm._state!.SkillGroups[1].Kigou);
    }

    [Fact]
    public void RemoveSkillGroupDeletesTheGroupAtTheGivenIndex()
    {
        var st = MinimalState.Build(skillGroups: new List<Group> { new("A", "SK0"), new("B", "SK1") });
        var vm = new MagiViewModel { _state = st };

        vm.RemoveSkillGroup(0);

        Assert.Single(vm._state!.SkillGroups);
        Assert.Equal("SK1", vm._state!.SkillGroups[0].Kigou);
    }

    [Fact]
    public void SetStaffSkillAssignsTheSkillIndex()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() };

        vm.SetStaffSkill(0, 2);

        Assert.Equal(2, vm._state!.StaffList[0].SkillIdx);
        Assert.Equal(0, vm._state!.StaffList[1].SkillIdx); // untouched
        Assert.Contains("スキル割当: 職員A → 区分[2]", vm.Ui.OpLog[0]);
    }

    // ===================================================================
    // Ws1CanRemoveGroup / Ws1GroupMemberCount / ref-count queries
    // ===================================================================

    [Fact]
    public void Ws1CanRemoveGroupIsFalseWithOnlyOneGroup()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // single group G0
        Assert.False(vm.Ws1CanRemoveGroup(0));
    }

    [Fact]
    public void Ws1CanRemoveGroupIsTrueWithMultipleGroups()
    {
        var st = MinimalState.Build(groups: new List<Group> { new("G0", "G0"), new("G1", "G1") });
        var vm = new MagiViewModel { _state = st };
        Assert.True(vm.Ws1CanRemoveGroup(0));
        Assert.False(vm.Ws1CanRemoveGroup(5)); // out of range
    }

    [Fact]
    public void Ws1CanRemoveGroupIsFalseWithoutState()
    {
        var vm = new MagiViewModel();
        Assert.False(vm.Ws1CanRemoveGroup(0));
    }

    [Fact]
    public void Ws1GroupMemberCountCountsStaffInTheGroup()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build() }; // both staff in group 0
        Assert.Equal(2, vm.Ws1GroupMemberCount(0));
        Assert.Equal(0, vm.Ws1GroupMemberCount(1));
        Assert.Equal(0, new MagiViewModel().Ws1GroupMemberCount(0));
    }

    [Fact]
    public void RefCountQueriesDelegateToWs1OpsAndCountReferencingConstraintRows()
    {
        var st = MinimalState.Build(
            groups: new List<Group> { new("G0", "G0") },
            skillGroups: new List<Group> { new("SK", "SK0") },
            cons41: new List<C41Row> { new("G0", "A", "1", "2") },
            cons41s: new List<C41Row> { new("SK0", "A", "1", "2") });
        var vm = new MagiViewModel { _state = st };

        Assert.Equal(2, vm.Ws1ShiftRefCount(1)); // shift "A" referenced by both cons41 and cons41s
        Assert.Equal(0, vm.Ws1ShiftRefCount(0)); // "休" not referenced anywhere
        Assert.Equal(1, vm.Ws1GroupRefCount(0)); // group "G0" referenced by cons41
        Assert.Equal(1, vm.Ws1SkillGroupRefCount(0)); // skill group "SK0" referenced by cons41s
        Assert.Equal(0, vm.Ws1ShiftRefCount(99)); // out-of-range index -> 0, no throw
        Assert.Equal(0, vm.Ws1GroupRefCount(99));
        Assert.Equal(0, vm.Ws1SkillGroupRefCount(99));
    }

    [Fact]
    public void RefCountQueriesReturnZeroWithoutState()
    {
        var vm = new MagiViewModel();
        Assert.Equal(0, vm.Ws1ShiftRefCount(0));
        Assert.Equal(0, vm.Ws1GroupRefCount(0));
        Assert.Equal(0, vm.Ws1SkillGroupRefCount(0));
    }

    // ===================================================================
    // ViolationRange
    // ===================================================================

    [Fact]
    public void ViolationRangeReturnsTheUnmetWindowForAC1Violation()
    {
        // A 3-day window needing at least 2 occurrences of shift "A" (index 1); the schedule is all
        // 休(0), so the very first window [0,3) is short (z=0 < 2) -> the violation range is (0,2).
        var st = MinimalState.Build(cons1: new List<C1Row> { new("3", "A", "2") });
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ViolationCells = new Dictionary<string, string> { ["0,0"] = "vio-c1" };

        Assert.Equal((0, 2), vm.ViolationRange(0, 0));
    }

    [Fact]
    public void ViolationRangeSkipsAC1RuleTheStaffCannotDo()
    {
        // Group G0 can only do 休(0), not "A"(1) -> the c1 rule on "A" is skipped entirely for staff 0
        // (!CanDo guard), so no window is ever found and the result is null.
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 0 } }; // G0: 休 only
        var st = MinimalState.Build(groupShift: groupShift, cons1: new List<C1Row> { new("3", "A", "2") });
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ViolationCells = new Dictionary<string, string> { ["0,0"] = "vio-c1" };

        Assert.Null(vm.ViolationRange(0, 0));
    }

    [Fact]
    public void ViolationRangeReturnsTheIncompletePatternWindowForAC3mViolation()
    {
        // cons3 has an irrelevant pattern (doesn't start with 休, never matches) to prove the
        // function reads Cons3m — not Cons3 — for the "vio-c3m" class. cons3m = [休,A]: day0="休"
        // matches seq[0], but day1="休" != "A" -> incomplete pattern -> range (0,1). If the code
        // mistakenly consulted Cons3 instead, it would fall through to the single-run fallback and
        // return (0,6) (the whole all-休 row), so this also discriminates the two lists.
        var st = MinimalState.Build(
            cons3: new List<C3Row> { new(new[] { "A", "A" }) },
            cons3m: new List<C3Row> { new(new[] { "休", "A" }) });
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ViolationCells = new Dictionary<string, string> { ["0,0"] = "vio-c3m" };

        Assert.Equal((0, 1), vm.ViolationRange(0, 0));
    }

    [Fact]
    public void ViolationRangeFallsBackToTheSingleShiftRunWhenNoPatternMatches()
    {
        // cons3 is empty, so the pattern-matching loop does nothing; the schedule is all 休(0) for
        // all 7 days, so the single-shift-run fallback extends across the whole row -> (0,6).
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ViolationCells = new Dictionary<string, string> { ["0,0"] = "vio-c3" };

        Assert.Equal((0, 6), vm.ViolationRange(0, 0));
    }

    [Fact]
    public void ViolationRangeReturnsNullWhenNeitherAPatternNorARunApplies()
    {
        var sched = new[] { new[] { 0, 1, 0, 0, 0, 0, 0 }, new[] { 0, 0, 0, 0, 0, 0, 0 } }; // day1 = "A", breaks the run
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = sched };
        vm.Ui.ViolationCells = new Dictionary<string, string> { ["0,0"] = "vio-c3" };

        Assert.Null(vm.ViolationRange(0, 0));
    }

    [Fact]
    public void ViolationRangeReturnsNullForAnUnrecognisedViolationClass()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ViolationCells = new Dictionary<string, string> { ["0,0"] = "vio-covU" };

        Assert.Null(vm.ViolationRange(0, 0));
    }

    [Fact]
    public void ViolationRangeReturnsNullWhenTheCellHasNoKnownViolation()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        Assert.Null(vm.ViolationRange(0, 0));
    }

    [Fact]
    public void ViolationRangeReturnsNullWithoutStateOrSchedule()
    {
        Assert.Null(new MagiViewModel().ViolationRange(0, 0));
        Assert.Null(new MagiViewModel { _state = MinimalState.Build() }.ViolationRange(0, 0)); // no schedule
    }

    [Fact]
    public void ViolationRangeReturnsNullWhenIndicesAreOutOfProblemBounds()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };
        vm.Ui.ViolationCells = new Dictionary<string, string> { ["99,0"] = "vio-c1" };

        Assert.Null(vm.ViolationRange(99, 0));
    }

    // ===================================================================
    // Ws1RemoveShift / Ws1RemoveStaff / Ws1RemoveGroup
    // ===================================================================

    [Fact]
    public void Ws1RemoveShiftDropsTheShiftAndRemapsCells()
    {
        var st = MinimalState.Build(
            shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            schedule: new List<IReadOnlyList<int>>
            {
                new List<int> { 1, 1, 1, 1, 1, 1, 1 },
                new List<int> { 0, 0, 0, 0, 0, 0, 0 },
            });
        var vm = new MagiViewModel { _state = st, _currentSchedule = st.Schedule.Select(r => r.ToArray()).ToArray() };

        vm.Ws1RemoveShift(1); // remove "A"

        Assert.Equal(2, vm._state!.Shifts.Count);
        Assert.DoesNotContain(vm._state!.Shifts, s => s.Kigou == "A");
        // [race] ApplyStructure(Ws1Result) sets a non-null _currentSchedule before calling
        // RefreshCheck(), which dispatches a background Task.Run that itself calls LogOp on
        // completion. That background log can beat this assertion to the front of the list
        // (thread-pool scheduling is nondeterministic for a fixture this cheap), so search the
        // whole list rather than assume this entry is still at index 0.
        Assert.Contains(vm.Ui.OpLog, line => line.Contains("シフト削除: A"));
    }

    [Fact]
    public void Ws1RemoveShiftIsNoOpWithoutASchedule()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.Ws1RemoveShift(1);

        Assert.Same(st, vm._state);
    }

    [Fact]
    public void Ws1RemoveStaffDropsTheStaffRow()
    {
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = MinimalState.BuildSchedule() };

        vm.Ws1RemoveStaff(0);

        Assert.Single(vm._state!.StaffList);
        Assert.Equal("職員B", vm._state!.StaffList[0].Name);
        Assert.Single(vm._currentSchedule!);
    }

    [Fact]
    public void Ws1RemoveStaffIsNoOpWithoutASchedule()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };

        vm.Ws1RemoveStaff(0);

        Assert.Same(st, vm._state);
    }

    [Fact]
    public void Ws1RemoveGroupMovesMembersToTheFirstGroupAndLogsTheCount()
    {
        var st = MinimalState.Build(
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: new List<Staff> { new("職員A", 0), new("職員B", 1) });
        var vm = new MagiViewModel { _state = st };

        vm.Ws1RemoveGroup(1);

        Assert.Single(vm._state!.Groups);
        Assert.Equal(0, vm._state!.StaffList[1].GroupIdx); // moved into the (now sole) first group
        Assert.Contains("グループ削除: [1]（所属1名は先頭グループへ移動＝担当できるシフトが変わります）", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void Ws1RemoveGroupLogsPlainlyWhenNobodyWasMoved()
    {
        var st = MinimalState.Build(
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: new List<Staff> { new("職員A", 0), new("職員B", 0) }); // nobody in group 1
        var vm = new MagiViewModel { _state = st };

        vm.Ws1RemoveGroup(1);

        Assert.Contains("グループ削除: [1]", vm.Ui.OpLog[0]);
        Assert.DoesNotContain("先頭グループへ移動", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void Ws1RemoveGroupIsNoOpWhenOnlyOneGroupRemains()
    {
        var st = MinimalState.Build(); // single group G0
        var vm = new MagiViewModel { _state = st };

        vm.Ws1RemoveGroup(0);

        Assert.Same(st, vm._state);
    }

    [Fact]
    public void Ws1RemoveGroupIsNoOpForAnOutOfRangeIndex()
    {
        var st = MinimalState.Build(groups: new List<Group> { new("G0", "G0"), new("G1", "G1") });
        var vm = new MagiViewModel { _state = st };

        vm.Ws1RemoveGroup(99);

        Assert.Same(st, vm._state);
    }

    // ===================================================================
    // ApplyStructureWithMessage(Ws1Result, string) — direct infra test
    // (its only Kotlin-side caller belongs to a not-yet-ported later piece; see the class KDoc
    // of MagiViewModel.Ws1.cs for why this is `internal` and tested directly here.)
    // ===================================================================

    [Fact]
    public async Task ApplyStructureWithMessageWs1ResultAppliesStateAndScheduleAndReportsOnCompletion()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = MinimalState.Build(staffList: new List<Staff> { new("旧", 0) }) };
        var newSched = MinimalState.BuildSchedule();
        var r = new Ws1Result(st, newSched);

        vm.ApplyStructureWithMessage(r, "テスト完了");

        Assert.Same(st, vm._state);
        Assert.Equal(newSched, vm._currentSchedule);
        Assert.Contains("テスト完了（違反チェック中…）", vm.Ui.Message);
        Assert.NotNull(vm.LastApplyStructureWithMessageTask);
        await vm.LastApplyStructureWithMessageTask!;
        Assert.Contains("テスト完了｜必須=", vm.Ui.Message);
        Assert.False(vm.Ui.MessageIsError);
    }

    [Fact]
    public void ApplyStructureWithMessageWs1ResultIsBlockedWhileOptimizeIsRunning()
    {
        var original = MinimalState.Build(staffList: new List<Staff> { new("元のまま", 0) });
        var vm = new MagiViewModel { _state = original };
        OptimizationRepository.SetRunning(true);

        vm.ApplyStructureWithMessage(new Ws1Result(MinimalState.Build(), MinimalState.BuildSchedule()), "適用");

        Assert.Same(original, vm._state);
        Assert.Null(vm.LastApplyStructureWithMessageTask);
        Assert.True(vm.Ui.MessageIsError);
    }

    // ===================================================================
    // Running-gate coverage (representative, applies uniformly via
    // StructuralEditBlocked -> ApplyStructure(MagiState)/ApplyStructure(Ws1Result))
    // ===================================================================

    [Fact]
    public void RunningGateBlocksAnAddEditThroughTheMagiStateOverload()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st };
        OptimizationRepository.SetRunning(true);

        vm.Ws1AddShift("夜勤", "夜", "", "");

        Assert.Same(st, vm._state);
        Assert.True(vm.Ui.MessageIsError);
        Assert.Contains("[W]", vm.Ui.OpLog[0]);
    }

    [Fact]
    public void RunningGateBlocksARemoveThroughTheWs1ResultOverload()
    {
        var st = MinimalState.Build();
        var vm = new MagiViewModel { _state = st, _currentSchedule = MinimalState.BuildSchedule() };
        OptimizationRepository.SetRunning(true);

        vm.Ws1RemoveStaff(0);

        Assert.Same(st, vm._state);
        Assert.True(vm.Ui.MessageIsError);
    }
}
