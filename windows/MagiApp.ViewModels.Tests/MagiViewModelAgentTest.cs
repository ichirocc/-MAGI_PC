using MagiApp.ViewModels.Work;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>代行役（AX）の VM: 作成の直後だけ回り、当てた手は元に戻す 1 回で戻り、同じ盤面では回し直さない（C# の配線の確認）。</summary>
[Collection("OptimizationRepositoryState")]
public class MagiViewModelAgentTest : IDisposable
{
    private const int REST = 0, A = 1;
    private readonly List<string> _dirs = new();

    public MagiViewModelAgentTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    public void Dispose()
    {
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>1 日目は A が 0 人（不足 1）、2 日目は A が 2 人（過剰 1）。s0 の 2 日を入れ替えれば両方消える。</summary>
    private static MagiState State() => new(
        StartDate: "2026-01-01", EndDate: "2026-01-02",
        Shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "1") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        Use2Patterns: true,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { REST, A }, new List<int> { REST, A } },
        Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, MagiEngine.Model.Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    private MagiViewModel NewVm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "magi-vm-agent-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        var st = State();
        var vm = new MagiViewModel { DataDir = dir, _state = st, _currentSchedule = st.Schedule.Select(r => r.ToArray()).ToArray() };
        vm._hydrated = true;
        vm.Ui.HasResult = true;
        vm.Ui.EngineRan = true;
        vm.Ui.BestHard = 1;
        return vm;
    }

    [Fact]
    public async Task AppliesSafeMovesAfterCreationAndUndoesInOneStep()
    {
        var vm = NewVm();
        vm.MaybeRunAgent();
        Assert.True(vm.Ui.AgentRunning);
        await vm.LastAgentTask!;
        Assert.False(vm.Ui.AgentRunning);
        var rec = vm.Ui.AgentRecord!;
        Assert.NotEmpty(rec.Steps);
        Assert.Equal(1, rec.HardBefore);
        Assert.Equal(0, rec.HardAfter);
        Assert.Equal(0, UnifiedViolationChecker.Check(vm._state!, vm._currentSchedule!).Hard);
        Assert.Equal(AgentText.Notice(rec), vm.Ui.OpNotice?.Text);   // [Android 3.727.1] 通知はカードの文を繰り返さない
        vm.UndoAgent();
        Assert.Null(vm.Ui.AgentRecord);
        Assert.Equal(new[] { REST, A }, vm._currentSchedule![0]);
    }

    [Fact]
    public void DoesNotRunWhenOffNotAfterCreationOrSeenBefore()
    {
        var off = NewVm(); off.Ui.AgentAuto = false;
        off.MaybeRunAgent();
        Assert.Null(off.LastAgentTask);
        var notRan = NewVm(); notRan.Ui.EngineRan = false;
        notRan.MaybeRunAgent();
        Assert.Null(notRan.LastAgentTask);
        notRan.AgentDecisionApproved();   // 承認のあとは作成を待たずに回る
        notRan.MaybeRunAgent();
        Assert.NotNull(notRan.LastAgentTask);
    }

    [Fact]
    public async Task SameBoardIsNotRunTwice()
    {
        var vm = NewVm();
        vm.MaybeRunAgent();
        await vm.LastAgentTask!;
        vm.Undo();
        vm.Ui.BestHard = 1;
        var first = vm.LastAgentTask;
        vm.MaybeRunAgent();
        Assert.Same(first, vm.LastAgentTask);
    }
}
