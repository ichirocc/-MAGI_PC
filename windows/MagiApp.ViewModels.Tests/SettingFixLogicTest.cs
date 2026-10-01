using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

public class SettingFixLogicTest
{
    private static readonly IReadOnlyDictionary<string, string> NoStr = new Dictionary<string, string>();

    /// <summary>X は A が上限 0（入れない指定）、Y は制限なし。A の必要数は need1/need2。</summary>
    private static MagiState DemandState(
        int days, string need1 = "2", string need2 = "", bool use2 = false,
        Dictionary<string, int>? wishes = null, Dictionary<string, string>? nd1 = null, Dictionary<string, string>? nd2 = null) =>
        new(
            StartDate: "2026-08-01", EndDate: $"2026-08-{days:D2}",
            Shifts: new[] { new Shift("休", "休", "", "", ShiftRole.Rest), new Shift("A", "A", need1, need2) },
            Groups: new[] { new Group("G", "G") },
            StaffList: new[] { new Staff("X", 0), new Staff("Y", 0) }, Use2Patterns: use2,
            GroupShift: new[] { new[] { 1, 1 } }, GroupShiftApt: new[] { new[] { "", "" } },
            Schedule: new[] { Enumerable.Repeat(0, days).ToList(), Enumerable.Repeat(0, days).ToList() },
            Wishes: wishes ?? new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range> { ["0,1"] = new Range("0", "0") },
            NeedDay1: nd1 ?? new Dictionary<string, string>(), NeedDay2: nd2 ?? new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(),
            Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(), Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: NoStr, Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    private static List<SettingIssue> CapIssues(MagiState s) =>
        V6SanityPort.BuildGuidance(s).Where(i => i.Action == SettingFixAction.CapDemand).ToList();

    /// <summary>直して再診断、を繰り返して残りが 0 になるまで。</summary>
    private static MagiState FixAll(MagiState s0)
    {
        var s = s0;
        for (var n = 0; n < 40; n++)
        {
            var i = CapIssues(s).FirstOrDefault();
            if (i is null) return s;
            s = SettingFixLogic.Apply(s, i) ?? throw new InvalidOperationException($"fix was a no-op for {i.Where}");
        }
        throw new InvalidOperationException("did not converge");
    }

    [Fact]
    public void DateSpecificShortageWritesOnlyThatDaysException()
    {
        var s = DemandState(days: 2, wishes: new() { ["0,0"] = 1 });   // 0日目は X も A に固定＝足りる。1日目だけ不足
        var issues = CapIssues(s);
        Assert.Single(issues);
        Assert.Equal(1, issues[0].DemandDayIdx);
        var ns = SettingFixLogic.Apply(s, issues[0])!;
        Assert.Equal("2", ns.Shifts[1].Need1);   // 標準の必要数は他の日のために動かさない
        Assert.Equal("1", ns.NeedDay1["1,1"]);
        Assert.False(ns.NeedDay1.ContainsKey("1,0"));
        Assert.Empty(CapIssues(ns));
    }

    [Fact]
    public void ExistingExceptionIsLoweredInPlace()
    {
        var s = DemandState(days: 2, wishes: new() { ["0,0"] = 1 }, nd1: new() { ["1,1"] = "3" });
        var ns = SettingFixLogic.Apply(s, CapIssues(s).Single())!;
        Assert.Equal("2", ns.Shifts[1].Need1);
        Assert.Equal("1", ns.NeedDay1["1,1"]);
        Assert.Empty(CapIssues(ns));
    }

    [Fact]
    public void ShortageOnEveryDayIsOneMonthWideIssueThatLowersTheStandard()
    {
        var s = DemandState(days: 3);
        var issues = CapIssues(s);
        Assert.Single(issues);
        Assert.Null(issues[0].DemandDayIdx);
        var ns = SettingFixLogic.Apply(s, issues[0])!;
        Assert.Equal("1", ns.Shifts[1].Need1);
        Assert.Empty(ns.NeedDay1);
        Assert.Empty(CapIssues(ns));
    }

    [Fact]
    public void MonthWideIsNotUsedWhenADayHasItsOwnException()
    {
        var s = DemandState(days: 3, nd1: new() { ["1,1"] = "2" });
        var issues = CapIssues(s);
        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.NotNull(i.DemandDayIdx));
        Assert.Empty(CapIssues(FixAll(s)));
    }

    [Fact]
    public void SecondPatternIsWrittenOnlyWhenUsedAndAboveCap()
    {
        var s = DemandState(days: 2, need1: "2", need2: "3", use2: true, wishes: new() { ["0,0"] = 1 });
        var ns = SettingFixLogic.Apply(s, CapIssues(s).Single())!;
        Assert.Equal("1", ns.NeedDay1["1,1"]);
        Assert.Equal("1", ns.NeedDay2["1,1"]);
        Assert.Equal("2", ns.Shifts[1].Need1);
        Assert.Equal("3", ns.Shifts[1].Need2);
        Assert.Empty(CapIssues(ns));
        var blank2 = DemandState(days: 2, need1: "2", need2: "", use2: true, wishes: new() { ["0,0"] = 1 });
        Assert.False(SettingFixLogic.Apply(blank2, CapIssues(blank2).Single())!.NeedDay2.ContainsKey("1,1"));
        var off = DemandState(days: 2, need1: "2", need2: "3", use2: false, wishes: new() { ["0,0"] = 1 });
        Assert.False(SettingFixLogic.Apply(off, CapIssues(off).Single())!.NeedDay2.ContainsKey("1,1"));
    }

    [Fact]
    public void FullWidthExceptionDigitsAreReadLikeKotlin()
    {
        var s = DemandState(days: 2, wishes: new() { ["0,0"] = 1 }, nd1: new() { ["1,1"] = "３" });
        var ns = SettingFixLogic.Apply(s, CapIssues(s).Single())!;
        Assert.Equal("1", ns.NeedDay1["1,1"]);
    }

    // ---- DELETE_DUP_SEQ ----
    private static MagiState SeqState(List<string[]> n3n) =>
        DemandState(days: 4) with
        {
            Shifts = new[] { new Shift("休", "休", "", "", ShiftRole.Rest), new Shift("A", "A", "", ""), new Shift("R", "R", "", "") },
            StaffList = new[] { new Staff("X", 0) },
            GroupShift = new[] { new[] { 1, 1, 1 } }, GroupShiftApt = new[] { new[] { "", "", "" } },
            Schedule = new[] { new[] { 0, 0, 0, 0 } as IReadOnlyList<int> },
            StaffRange = new Dictionary<string, Range>(),
            Cons3n = n3n.Select(p => new C3Row(p)).ToList(),
        };

    private static List<SettingIssue> DupIssues(MagiState s) =>
        V6SanityPort.BuildGuidance(s).Where(i => i.Action == SettingFixAction.DeleteDupSeq).ToList();

    [Fact]
    public void DuplicateRowsWithAGapAreDeleted()
    {
        var s = SeqState(new() { new[] { "A", "", "R" }, new[] { "A", "", "R" } });
        var issue = DupIssues(s).Single();
        Assert.Equal("A", issue.SeqKey);
        var ns = SettingFixLogic.Apply(s, issue)!;
        Assert.Single(ns.Cons3n);
        Assert.Empty(DupIssues(ns));
    }

    [Fact]
    public void GapRowIsNotCompactedIntoAnotherRule()
    {
        var s = SeqState(new() { new[] { "A", "", "R" }, new[] { "A", "R" }, new[] { "A", "R" } });
        var issue = DupIssues(s).Single();
        Assert.Equal("A→R", issue.SeqKey);
        var ns = SettingFixLogic.Apply(s, issue)!;
        Assert.Equal(new[] { new[] { "A", "", "R" }, new[] { "A", "R" } }, ns.Cons3n.Select(r => r.Pattern.ToArray()).ToArray());
        Assert.Empty(DupIssues(ns));
    }

    [Fact]
    public void DeleteIsNullWhenNothingMatches()
    {
        var s = SeqState(new() { new[] { "A", "R" } });
        var issue = V6SanityPort.BuildGuidance(SeqState(new() { new[] { "A", "R" }, new[] { "A", "R" } }))
            .Single(i => i.Action == SettingFixAction.DeleteDupSeq);
        var ns = SettingFixLogic.Apply(SeqState(new() { new[] { "A", "", "R" } }), issue with { SeqKey = "A→R" });
        Assert.Null(ns);
        Assert.NotNull(SettingFixLogic.Apply(s, issue));
    }

    [Fact]
    public void SequenceKeyTruncatesAtFirstBlank()
    {
        Assert.Equal("A", V6SanityPort.C3SeqKey(new[] { "A", "", "R" }));
        Assert.Equal("A→R", V6SanityPort.C3SeqKey(new[] { "A", "R" }));
        Assert.Equal("", V6SanityPort.C3SeqKey(new[] { "", "R" }));
    }
}
