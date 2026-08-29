using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7 ピース18/19] <c>V6FinalPort.HandleOptimize.cs</c> の移植テスト。
///
/// <see cref="EmptyStaffIsRejectedWithAnActionableMessage"/> は Kotlin側
/// <c>PolishRobustnessTest.kt</c> の <c>emptyStaffIsRejectedWithAnActionableMessage</c> の逐語移植——
/// <c>PolishRobustnessTest.cs</c>（フェーズ6ピース30）のクラスdocコメントが
/// 「<c>V6FinalPort.handleOptimize</c> に依存し、それは未移植（フェーズ7「V6FinalPort統括」の
/// スコープ、ピース18）」として明示的に対象外にしていた1件で、<c>HandleOptimize</c> が揃った
/// このピースが正しい着地点。<c>HandleSmartInitial</c> 側の同ガード（フェーズ7ピース1で既に移植済み・
/// <c>V6FinalPort.cs:171-181</c>）と対にして両方固定する。
///
/// 残り2本（<see cref="EndToEnd_GoldenStateNeverWorsensTheInput"/>／
/// <see cref="EndToEnd_ExplicitAlgorithmIsHonoredAndProducesAValidSchedule"/>）は Kotlin 原本に
/// 直接の対応物を持たない新規スモークテスト——このファイルが移植する ~740 行の中核オーケストレーション
/// （watchdog/ExtraRefine/EliteIntegration/RunPostOptimization/最終番兵/全ログ集約の配線）が実際に
/// 一気通貫で動くことを、既存の <see cref="V6NativeOptimizerDispatcherTest"/> と同じ
/// <c>AssertNeverWorsensInput</c>/<c>AssertValidShape</c> パターンで確認する（実データ
/// <c>golden_state.json</c> と <see cref="MinimalState"/> の両方）。
/// </summary>
public class V6FinalPortHandleOptimizeTest
{
    private static MagiState LoadFixture(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    /// <summary>Kotlin原本 <c>PolishRobustnessTest.emptyBucketState()</c> と同型の最小盤面。</summary>
    private static MagiState EmptyBucketState()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "1", "") };
        return new MagiState(
            StartDate: "2026-01-01", EndDate: "2026-01-03",
            Shifts: shifts, Groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            StaffList: new List<Staff> { new("s0", 0), new("s1", 1) }, Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0, 0 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" }, new List<string> { "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 0, 1 }, new List<int> { 0, 1, 0 } },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    /// <summary>
    /// [Kotlin 3.360.3 の逐語移植] 職員ゼロだが schedule に行が残っている＝取込で起こりうる不整合な入力。
    /// （staff も schedule も空なら dayCount==0 で既存の期間ガードが先に止まるので、この形でないと
    /// 職員ガードには到達しない——Kotlin原本のコメントと完全に同じ理由。）
    /// <c>HandleOptimize</c>/<c>HandleSmartInitial</c> の両方が、原因の読めない例外
    /// （C#移植では <c>Problem</c>/<c>SaOptimizer</c> 内の添字例外に相当）でなく、
    /// 「職員が1人も登録されていません。職員管理で追加してください」という直し方が読めるメッセージで
    /// 止まることを固定する。
    /// </summary>
    [Fact]
    public async Task EmptyStaffIsRejectedWithAnActionableMessage()
    {
        var s = EmptyBucketState() with
        {
            StaffList = new List<Staff>(),
            Schedule = new List<IReadOnlyList<int>> { new List<int> { 0, 1, 0 } },
        };

        var exOptimize = await Record.ExceptionAsync(
            () => V6FinalPort.HandleOptimize(s, 1, schedule: Array.Empty<int[]>()));
        Assert.NotNull(exOptimize);
        Assert.Contains("職員が1人も登録されていません", exOptimize!.Message);

        var exSmartInitial = Record.Exception(() => V6FinalPort.HandleSmartInitial(s));
        Assert.NotNull(exSmartInitial);
        Assert.Contains("職員が1人も登録されていません", exSmartInitial!.Message);
    }

    /// <summary>
    /// 期間が0日以下の入力も、同じ形で直し方が読めるメッセージで止まる。
    /// <c>MagiState.DayCount</c> は <c>StartDate</c>/<c>EndDate</c> の日付差ではなく
    /// <c>Schedule[0].Count</c>（Kotlin原本 <c>schedule.firstOrNull()?.size ?: 0</c> の忠実な移植）から
    /// 導出されるため、空の <c>Schedule</c> でこのガードへ到達させる。
    /// </summary>
    [Fact]
    public async Task ZeroDayPeriodIsRejectedWithAnActionableMessage()
    {
        var s = EmptyBucketState() with { Schedule = new List<IReadOnlyList<int>>() };
        var ex = await Record.ExceptionAsync(() => V6FinalPort.HandleOptimize(s, 1));
        Assert.NotNull(ex);
        Assert.Contains("対象期間が無効です", ex!.Message);
        Assert.Contains("基本情報で", ex.Message);   // [HF77] handleSmartInitial 側の文言と異なることの回帰
    }

    private static void NoOpProgress(string phase, ViolationReport? rep, long iters, long elapsed) { }

    private static void AssertValidShape(Problem p, int[][] schedule)
    {
        Assert.Equal(p.S, schedule.Length);
        foreach (var row in schedule) Assert.Equal(p.T, row.Length);
    }

    private static void AssertNeverWorsensInput(MagiState state, Problem p, int[][] initial, ViolationReport resultReport)
    {
        var baseSched = ScheduleUtil.NormalizeSchedule(initial, p);
        var baseReport = UnifiedViolationChecker.Check(state, baseSched);
        Assert.False(UnifiedViolationChecker.BetterReport(baseReport, resultReport),
            "HandleOptimize's output must never be strictly worse than its input (keep-best across the whole pipeline, incl. the final CheckResultWorse sentinel).");
    }

    /// <summary>
    /// 実データ(<c>golden_state.json</c>)を短い予算(AUTO→V5帯)で一気通貫させ、keep-best（最終番兵含む）が
    /// 保たれ、盤面の次元が正しく、既定ログタグ群（<c>TIME</c>/<c>TimeBudget</c>/<c>Watchdog</c>/
    /// <c>スコア収支</c>/<c>残存分析</c>/<c>設定の効き</c>）が全て出ることを確認する。
    /// </summary>
    [Fact]
    public async Task EndToEnd_GoldenStateNeverWorsensTheInput()
    {
        var state = LoadFixture("golden_state.json");
        var p = new Problem(state);
        var initial = state.Schedule.ToIntArray2D();

        var result = await V6FinalPort.HandleOptimize(
            state, secondsRaw: 1, workers: 2, onProgress: NoOpProgress);

        AssertValidShape(p, result.Schedule);
        AssertNeverWorsensInput(state, p, initial, result.Report);
        Assert.StartsWith("optimize:", result.Phase);

        // [ログ集約の骨格を確認] nativeLog は意図的に省略されているため3要素目は budgetPlanLog でなく
        //   tuningLog がその位置には来ない——タグの有無だけを確認し、位置は問わない。
        foreach (var tag in new[] { "TIME", "TimeBudget", "Watchdog", "スコア収支", "残存分析", "設定の効き" })
            Assert.Contains(result.Logs, l => l.Tag == tag);
    }

    /// <summary>
    /// AUTO でなく明示的に <see cref="V6Algorithm.V5"/> を指定した場合、その全予算が尊重され、
    /// <c>TIME</c> 行が "SAチェーンN本" 形式（V5専用の分岐、review #1 の逐語移植）で並列度を表示する。
    /// </summary>
    [Fact]
    public async Task EndToEnd_ExplicitAlgorithmIsHonoredAndProducesAValidSchedule()
    {
        var state = MinimalState.Build();
        var p = new Problem(state);
        var initial = p.InitialAssignment();

        var result = await V6FinalPort.HandleOptimize(
            state, secondsRaw: 1, workers: 2, requestedAlgorithm: V6Algorithm.V5, onProgress: NoOpProgress);

        AssertValidShape(p, result.Schedule);
        AssertNeverWorsensInput(state, p, initial, result.Report);
        var timeLog = Assert.Single(result.Logs, l => l.Tag == "TIME");
        Assert.Contains("SAチェーン2本", timeLog.Message);
    }
}
