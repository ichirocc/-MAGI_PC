using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [3.471.0 / phase9 #18] 分析タブのトリアージ分類を固定する（Kotlin原本 <c>AnalysisTriageTest</c> の移植）。
/// 一番大事な不変条件は「c1 を族の名前だけで『自動で直る』側へ入れない」こと（3.263.0/3.322.0/3.344.0 の逆戻り防止）。
/// 表示名は下流の語彙（AnalysisView.BreakdownLabels）を UI 層から受けるので、ここでは同じ語彙の写しを渡す。
/// </summary>
public class AnalysisTriageTest
{
    private static string L(string f) => f switch
    {
        "c1" => "期間の制約", "c3" => "守るとよい並び", "weekly" => "曜日の偏り",
        "covU" => "人員不足", "c3n" => "禁止の並び", "covO" => "人員過剰", _ => f,
    };

    private static UiState Ui(
        IReadOnlyDictionary<string, int>? breakdown = null, bool hasResult = false,
        IReadOnlyList<SettingIssue>? issues = null, C1PlateauDiagnosis? c1Plateau = null, CoverageDiagnosis? coverage = null) => new()
    {
        Breakdown = breakdown ?? new Dictionary<string, int>(),
        HasResult = hasResult, EngineRan = hasResult,
        SettingIssues = issues ?? Array.Empty<SettingIssue>(),
        C1Plateau = c1Plateau, CoverageDiag = coverage,
    };

    private static C1PlateauDiagnosis Plateau(bool observed) => new(
        RemainingC1: 6,
        Entries: !observed ? Array.Empty<C1PlateauEntry>() : new[]
        {
            new C1PlateauEntry(
                Staff: 0, Shift: 1, RuleIndex: 0, StaffName: "古泉 健一", ShiftKigou: "Dﾃ", RuleLabel: "14日で2回以上",
                Cause: C1PlateauCause.PinConstrained, Evidence: C1PlateauEvidence.Observed,
                RejectedByPin: 12, RejectedByScore: 0, NoCandidate: 0, TopScoreCulprits: Array.Empty<(string, int)>()),
        });

    private static Dictionary<string, int> B(params (string, int)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void BeforeRunSoftFamiliesStayInTheSearchBand()
    {
        var t = AnalysisTriage.Build(Ui(B(("c1", 6), ("c3", 97), ("weekly", 186))), L);
        Assert.False(t.Computed);
        Assert.Empty(t.Blockers);
        Assert.Equal(new HashSet<string> { "期間の制約", "守るとよい並び", "曜日の偏り" }, t.Searching.Select(r => r.Label).ToHashSet());
        Assert.Contains("最適化後も残る場合があります", t.SearchNote);
    }

    [Fact]
    public void C1MovesToBlockersOnlyWhenThePlateauDiagnosisObservedSomething()
    {
        var observed = AnalysisTriage.Build(Ui(B(("c1", 6)), hasResult: true, c1Plateau: Plateau(true)), L);
        Assert.Equal(new[] { "期間の制約" }, observed.Blockers.Select(r => r.Label));
        Assert.True(observed.Blockers.Single().Promoted);
        Assert.Contains("回数を固定", observed.Blockers.Single().Detail);
        Assert.Empty(observed.Searching);

        var unknown = AnalysisTriage.Build(Ui(B(("c1", 6)), hasResult: true, c1Plateau: Plateau(false)), L);
        Assert.Empty(unknown.Blockers);
        Assert.Equal(new[] { "期間の制約" }, unknown.Searching.Select(r => r.Label));
    }

    [Fact]
    public void HardFamiliesAreAlwaysBlockersAndClaimNothingWithoutADiagnosis()
    {
        var t = AnalysisTriage.Build(Ui(B(("c3n", 1), ("covU", 3)), hasResult: true), L);
        Assert.Equal(new[] { "人員不足", "禁止の並び" }, t.Blockers.Select(r => r.Label)); // 重み順 covU(8000) > c3n(7000)
        Assert.All(t.Blockers, r => Assert.Equal("", r.Detail));
    }

    [Fact]
    public void SettingIssuesAreAggregatedByKind()
    {
        var issues = new[]
        {
            new SettingIssue(IssueKind.Range, "古泉 健一 のB4", "…", "…"),
            new SettingIssue(IssueKind.Range, "山本 昌幸 のB4", "…", "…"),
            new SettingIssue(IssueKind.Range, "佐藤 直美 のB4", "…", "…"),
            new SettingIssue(IssueKind.Demand, "Dﾃ の必要人数", "…", "…"),
        };
        var t = AnalysisTriage.Build(Ui(issues: issues), L);
        Assert.Equal(new[] { ("回数の設定", 3), ("必要人数の設定", 1) }, t.Issues.Select(r => (r.Label, r.Count)));
        Assert.Contains("ほか", t.Issues[0].Detail);
    }

    [Fact]
    public void DistributionFamiliesUsePointsNotCounts()
    {
        var t = AnalysisTriage.Build(Ui(B(("weekly", 186), ("c1", 6))), L);
        Assert.Equal("pt", t.Searching.First(r => r.Label == "曜日の偏り").Unit);
        Assert.Equal("件", t.Searching.First(r => r.Label == "期間の制約").Unit);
    }

    [Fact]
    public void ZeroCountFamiliesGoToTheCollapsedSummary()
    {
        var t = AnalysisTriage.Build(Ui(B(("c1", 6))), L);
        Assert.Equal(MirrorKeys.All.Count, t.OkFamilies.Count + t.BusyFamilies.Count);
        Assert.Equal(new[] { "期間の制約" }, t.BusyFamilies);
        Assert.Contains("人員不足", t.OkFamilies);
    }

    [Fact]
    public void SurplusIsPromotedOnlyWhenTheDiagnosisNamesTheFamilyThatBlocksIt()
    {
        static CoverageDiagnosis Cov(string? blockedBy) => new(
            TotalShortfall: 0, InfeasibleSlots: 0, FixableSlots: 0, Shortfalls: Array.Empty<CoverageShortfall>(),
            Relaxations: Array.Empty<string>(), TotalSurplus: 1,
            Surpluses: new[] { new CoverageSurplus(0, "8/1(土)", 1, "休", 0, 1, 1, blockedBy, "…") });
        var blocked = AnalysisTriage.Build(Ui(B(("covO", 1)), hasResult: true, coverage: Cov("weekly")), L);
        Assert.Equal(new[] { "人員過剰" }, blocked.Blockers.Select(r => r.Label));
        Assert.Contains("曜日の偏り", blocked.Blockers.Single().Detail);

        var free = AnalysisTriage.Build(Ui(B(("covO", 1)), hasResult: true, coverage: Cov(null)), L);
        Assert.Empty(free.Blockers);
        Assert.Equal(new[] { "人員過剰" }, free.Searching.Select(r => r.Label));
    }

    [Fact]
    public void EngineRanNotHasResultDecidesComputed()
    {
        // 手編集だけで HasResult=true になっても「計算後に残っている項目」とは語らない。
        var ui = Ui(B(("c1", 6)), hasResult: true);
        ui.EngineRan = false;
        Assert.False(AnalysisTriage.Build(ui, L).Computed);
    }

    /// <summary>[Android dashboard-10] ホームの残り: 必須0のときの要調整は件数の族だけ（公平化・曜日の偏りの pt を件として足さない）。</summary>
    [Fact]
    public void HomeRemainingLabelCountsOnlyCountFamilies()
    {
        Assert.Equal("必須 残り2件", AnalysisTriage.HomeRemainingLabel(2L, 0, B(("covU", 2), ("c1", 5))));
        Assert.Equal("残り1日", AnalysisTriage.HomeRemainingLabel(0L, 1, B()));
        Assert.Equal("必須は解消・要調整 5件", AnalysisTriage.HomeRemainingLabel(0L, 0, B(("apt", 5), ("fair", 80), ("weekly", 60))));
        Assert.Equal("必須は解消・残りは偏りのみ", AnalysisTriage.HomeRemainingLabel(0L, 0, B(("fair", 3))));   // pt だけなら解消済みとは言わない
        Assert.Equal("解消済み", AnalysisTriage.HomeRemainingLabel(0L, 0, B(("fair", 0))));
    }

    /// <summary>[Android schedule-5] 最終週が 7 日に満たない月でも、右端まで送れば最終週になる（左端の日だけだと最後から 2 番目で止まる）。</summary>
    [Fact]
    public void CurrentWeekReachesAPartialLastWeekAtTheRightEdge()
    {
        IReadOnlyList<int> R(int a, int b) => Enumerable.Range(a, b - a + 1).ToList();
        var weeks = new List<IReadOnlyList<int>> { R(0, 6), R(7, 13), R(14, 20), R(21, 27), R(28, 30) };
        Assert.Equal(0, ScheduleNav.CurrentWeekIndex(weeks, leftDay: 0, atEnd: false));
        Assert.Equal(2, ScheduleNav.CurrentWeekIndex(weeks, leftDay: 14, atEnd: false));
        Assert.Equal(3, ScheduleNav.CurrentWeekIndex(weeks, leftDay: 24, atEnd: false));   // 31日・7日表示の右端は左端が 24日目
        Assert.Equal(4, ScheduleNav.CurrentWeekIndex(weeks, leftDay: 24, atEnd: true));
        Assert.Equal(0, ScheduleNav.CurrentWeekIndex(new List<IReadOnlyList<int>> { R(0, 6) }, leftDay: 0, atEnd: true));
        Assert.Equal(0, ScheduleNav.CurrentWeekIndex(new List<IReadOnlyList<int>>(), leftDay: 3, atEnd: false));
    }
}
