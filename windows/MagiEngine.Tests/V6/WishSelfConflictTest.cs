using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// 希望どうしの衝突（<see cref="V6SanityPort.WishSelfConflicts(Problem)"/>）。<c>WishSelfConflictTest.kt</c> の逐語移植。
/// 実データ（11 名×31 日・必須 3＝下限）の 3 組を最小の形で写す:
/// 大島・古泉＝休の希望 3 連日×禁止「休→休→休」（c3n）、福澤＝Dﾃ の希望の翌日に休の希望×「休の希望の前日は Dﾃ 禁止」（c3w）。
/// </summary>
public class WishSelfConflictTest
{
    private const int Rest = 0, D = 1, A = 2;

    private static List<IReadOnlyList<int>> Rows(params int[][] rows) => rows.Select(r => (IReadOnlyList<int>)r.ToList()).ToList();

    private static MagiState State(
        Dictionary<string, int> wishes,
        List<IReadOnlyList<int>>? schedule = null,
        IReadOnlyList<C3Row>? cons3n = null,
        IReadOnlyList<C3wRow>? cons3w = null) =>
        MinimalState.Build(
            startDate: "2026-10-01", endDate: "2026-10-07",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("Dﾃ", "Dﾃ", "", ""), new("A", "A", "", "") },
            groups: new List<Group> { new("G", "G") }, staffList: new List<Staff> { new("大島", 0), new("福澤", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }, groupShiftApt: Array.Empty<IReadOnlyList<string>>(),
            schedule: schedule ?? Rows(Enumerable.Repeat(A, 7).ToArray(), Enumerable.Repeat(A, 7).ToArray()),
            wishes: wishes,
            cons3n: cons3n ?? new List<C3Row> { new(new List<string> { "休", "休", "休" }) })
        with { Cons3w = cons3w ?? Array.Empty<C3wRow>() };

    private static Dictionary<string, int> RestWindow(params (string Key, int Shift)[] more)
    {
        var w = new Dictionary<string, int> { ["0,2"] = Rest, ["0,3"] = Rest, ["0,4"] = Rest };
        foreach (var (k, v) in more) w[k] = v;
        return w;
    }

    [Fact]
    public void ForbiddenWindowFullyWishedIsOneGroupWithAllDays()
    {
        var gs = V6SanityPort.WishSelfConflicts(State(RestWindow(("1,2", Rest), ("1,3", Rest))));
        var g = Assert.Single(gs);
        Assert.Equal(0, g.Staff); Assert.Equal("c3n", g.Family);
        Assert.Equal(new[] { 2, 3, 4 }, g.Days); Assert.Equal(new[] { Rest, Rest, Rest }, g.Shifts);
        Assert.Equal(new[] { "0,2", "0,3", "0,4" }, g.WishKeys);
    }

    [Fact]
    public void OverlappingWindowsAreSeparateAndDuplicateRowsCollapse()
    {
        var row = new C3Row(new List<string> { "休", "休", "休" });
        var st = State(RestWindow(("0,5", Rest)), cons3n: new List<C3Row> { row, row });
        var days = V6SanityPort.WishSelfConflicts(st).Select(g => g.Days.ToArray()).ToList();
        Assert.Equal(new[] { new[] { 2, 3, 4 }, new[] { 3, 4, 5 } }, days);
    }

    [Fact]
    public void DayBeforeBanPairIsListedAndSanity1bKeepsItsText()
    {
        var st = State(new Dictionary<string, int> { ["1,0"] = D, ["1,1"] = Rest },
            cons3n: Array.Empty<C3Row>(), cons3w: new List<C3wRow> { new("休", "Dﾃ") });
        var g = Assert.Single(V6SanityPort.WishSelfConflicts(st));
        Assert.Equal("c3w", g.Family); Assert.Equal(new[] { 0, 1 }, g.Days); Assert.Equal(new[] { D, Rest }, g.Shifts);
        var issue = Assert.Single(V6SanityPort.BuildGuidance(st), it => it.Action == SettingFixAction.RemoveWish);
        Assert.Equal("福澤 10/1(木) 希望「Dﾃ」→ 10/2(金) 希望「休」", issue.Where);
        Assert.Equal("1,0", issue.WishKey);
    }

    [Fact]
    public void SanityNamesStaffAndEveryDayOfTheForbiddenWindow()
    {
        var issues = V6SanityPort.BuildGuidance(State(RestWindow())).Where(it => it.Problem.Contains("禁止の並び「休→休→休」に希望どうし")).ToList();
        var issue = Assert.Single(issues);
        Assert.Equal(IssueKind.Wish, issue.Kind);
        Assert.Equal("大島 10/3(土)・10/4(日)・10/5(月) 希望「休→休→休」", issue.Where);
        Assert.Equal(SettingFixAction.None, issue.Action);
    }

    [Fact]
    public void WindowWithOneFreeCellIsNotASelfConflict()
    {
        Assert.Empty(V6SanityPort.WishSelfConflicts(State(new Dictionary<string, int> { ["0,2"] = Rest, ["0,4"] = Rest })));
        Assert.Empty(V6SanityPort.WishSelfConflicts(State(new Dictionary<string, int> { ["0,2"] = Rest, ["0,3"] = A, ["0,4"] = Rest })));
    }

    [Fact]
    public void PrefCellFindsItsSiblingWishes()
    {
        // 古泉の形: 最適化器が 10/3 の希望を破って禁止の並びを避けた（pref 1）。10/4・10/5 の希望も取り消し候補。
        var sched = Rows(new[] { A, A, A, Rest, Rest, A, A }, Enumerable.Repeat(A, 7).ToArray());
        var st = State(RestWindow(), schedule: sched);
        var rep = UnifiedViolationChecker.Check(st);
        Assert.Equal(1, rep.Breakdown["pref"]); Assert.Equal(0, rep.Breakdown["c3n"]);
        var siblings = V6SanityPort.WishSelfConflicts(st).Where(g => g.WishKeys.Contains("0,2"))
            .SelectMany(g => g.WishKeys).ToHashSet();
        siblings.Remove("0,2");
        Assert.Equal(new HashSet<string> { "0,3", "0,4" }, siblings);
    }

    [Fact]
    public void SelfConflictHardCountsOnePerDisjointGroup()
    {
        var st = State(RestWindow(("0,5", Rest)));
        var p = ScheduleUtil.CachedProblem(st);
        var honored = new[] { new[] { A, A, Rest, Rest, Rest, Rest, A }, Enumerable.Repeat(A, 7).ToArray() };
        Assert.Equal(new[] { ("c3n", 1) }, V6SanityPort.WishSelfConflictHard(p, honored).Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(2, UnifiedViolationChecker.Check(st, honored).Breakdown["c3n"]);
        // 重なる 2 窓は真ん中の 1 件を破れば両方解ける＝下限は 1。
        var brokeMiddle = new[] { new[] { A, A, Rest, A, Rest, Rest, A }, Enumerable.Repeat(A, 7).ToArray() };
        Assert.Equal(new[] { ("pref", 1) }, V6SanityPort.WishSelfConflictHard(p, brokeMiddle).Select(kv => (kv.Key, kv.Value)));
    }

    [Fact]
    public void Hf70DoesNotCountSelfConflictAsHardOtherThanWishes()
    {
        var st = State(RestWindow(("1,0", D), ("1,1", Rest)), cons3w: new List<C3wRow> { new("休", "Dﾃ") });
        var sched = new[] { new[] { A, A, Rest, Rest, Rest, A, A }, new[] { D, Rest, A, A, A, A, A } };
        var rep = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, rep.Breakdown["c3n"]); Assert.Equal(1, rep.Breakdown["c3w"]);
        var msg = V6HotfixPasses.DetectHF70Anomalies(st, sched, "t", rep).Message;
        Assert.Contains("希望どうしの衝突 2 件", msg);
        Assert.DoesNotContain("希望以外HARD", msg);
    }

    [Fact]
    public async Task ResidualAnalysisPutsSelfConflictInWalls()
    {
        var st = State(RestWindow(), schedule: Rows(Enumerable.Repeat(A, 7).ToArray(), Enumerable.Repeat(A, 7).ToArray()));
        var res = await V6FinalPort.HandleOptimize(st, secondsRaw: 1, workers: 1, requestedAlgorithm: V6Algorithm.V5, allowImpossible: true,
            onProgress: (_, _, _, _) => { });
        Assert.Equal(1, res.Report.Hard);
        var line = Assert.Single(res.Logs, it => it.Tag == "残存分析").Message;
        Assert.Contains("希望どうしの衝突", line.Split('／')[0]);
        const string openMark = "まだ狙える: ";
        var at = line.IndexOf(openMark, StringComparison.Ordinal);
        var open = at < 0 ? line : line[(at + openMark.Length)..];
        Assert.False(open.Contains("c3n") || open.Contains("pref"), line);
    }
}
