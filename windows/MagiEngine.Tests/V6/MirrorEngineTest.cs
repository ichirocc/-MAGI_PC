using MagiEngine;
using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース10 + ピース3/4/5/6の穴埋め] Kotlin原本 <c>MirrorEngineTest.kt</c>（256行・10テスト）の
/// 1:1移植。
///
/// この10件は <see cref="ScheduleCsvBridge"/>（ピース10）のテスト調査中に発見された。
/// <see cref="CsvRoundTripKeepsScheduleSymbols"/> だけがピース10自身のスコープで、残り9件は既存の
/// C# 実装（<see cref="ViolationChecker"/>/<see cref="Evaluator"/> = ピース3、
/// <see cref="GreedyMirrorScheduler"/> = ピース4、<see cref="V6SearchOperators.AcceptWorseScore"/> =
/// ピース5、<see cref="ScheduleUtil.WeeklyFloorOfCount"/>/<see cref="ScheduleUtil.WeeklyDevOfBucket"/> =
/// ピース6）に対する回帰テストで、grep で確認したところ既存のC#テストスイート
/// （<c>ParityTest</c>/<c>ScheduleUtilTest</c>/<c>V6SearchOperatorsTest</c>/
/// <c>SmartInitialSchedulerTest</c> 等）はいずれもこれらの具体的な不変条件（族の重み優先解決・
/// AIOOBE安全性・辞書式パックの桁溢れ・受理ゲートの閾値スケール・weekly偏差の下限）を運動させて
/// いなかった。既に完了扱いのピースでも、発見した穴は埋める。
/// </summary>
public class MirrorEngineTest
{
    private static MagiState BuildState()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("A", "A", "1", "2"),
            new("B", "B", "1", "1"),
            new("C", "C", "1", ""),
        };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var staff = new List<Staff> { new("s0", 0), new("s1", 0), new("s2", 1), new("s3", 1) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 0, 1, 2, 0, 1, 0, 2 },
            new List<int> { 1, 0, 0, 2, 1, 2, 0 },
            new List<int> { 0, 2, 3, 0, 2, 0, 3 },
            new List<int> { 3, 0, 2, 3, 0, 2, 0 },
        };
        return new MagiState(
            StartDate: "2025-01-01",
            EndDate: "2025-01-07",
            Shifts: shifts,
            Groups: groups,
            StaffList: staff,
            Use2Patterns: true,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0 }, new List<int> { 1, 0, 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>>
            {
                new List<string> { "", "", "", "" },
                new List<string> { "", "", "", "" },
            },
            Schedule: schedule,
            Wishes: new Dictionary<string, int> { ["0,0"] = 0, ["1,4"] = 1, ["2,2"] = 3 },
            StaffRange: new Dictionary<string, Range>
            {
                ["0,1"] = new("2", "4"),
                ["1,2"] = new("", "3"),
                ["3,3"] = new("1", ""),
            },
            NeedDay1: new Dictionary<string, string> { ["1,0"] = "2" },
            NeedDay2: new Dictionary<string, string> { ["2,5"] = "2" },
            Cons1: new List<C1Row> { new("3", "A", "1") },
            Cons2: new List<C2Row> { new("B", "2") },
            Cons3: new List<C3Row> { new(new List<string> { "A", "B" }) },
            Cons3n: new List<C3Row> { new(new List<string> { "C", "C" }) },
            Cons3m: new List<C3Row> { new(new List<string> { "B", "A" }) },
            Cons3mn: new List<C3Row> { new(new List<string> { "A", "A" }) },
            Cons41: new List<C41Row> { new("G0", "A", "", "2") },
            Cons42: new List<C42Row> { new("G0", "G1", "A", "C") },
            SkillGroups: new List<Group>(),
            Cons41s: new List<C41Row>(),
            Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    [Fact]
    public void UnifiedCheckReturnsCompleteBreakdown()
    {
        var st = BuildState();
        var report = UnifiedViolationChecker.Check(st);
        Assert.Equal(new HashSet<string>(MirrorKeys.All), new HashSet<string>(report.Breakdown.Keys));
        Assert.Equal(report.Total, report.Breakdown.Values.Sum());
        Assert.Equal(report.Hard, MirrorKeys.Hard.Sum(k => report.Breakdown.TryGetValue(k, out var v) ? v : 0));
    }

    [Fact]
    public void CsvRoundTripKeepsScheduleSymbols()
    {
        var st = BuildState();
        var csv = ScheduleCsvBridge.Build(st, st.Schedule.ToIntArray2D());
        var blank = new int[st.StaffCount][];
        for (var i = 0; i < st.StaffCount; i++) blank[i] = new int[st.DayCount];
        var parsed = ScheduleCsvBridge.Parse(csv, st, blank);
        Assert.Equal(st.Schedule, parsed.Schedule.Select(row => (IReadOnlyList<int>)row.ToList()).ToList());
        Assert.Contains("staff一致", parsed.Report.Logs.First().Message);
    }

    [Fact]
    public void GreedySchedulerProducesValidDimensions()
    {
        var st = BuildState();
        var generated = GreedyMirrorScheduler.Generate(st);
        Assert.Equal(st.StaffCount, generated.Schedule.Length);
        Assert.Equal(st.DayCount, generated.Schedule[0].Length);
    }

    // [防御的統一/敵対的監査 移植元] markCount(countViolations) が mark/markNeed と同じ重み優先で解決することを
    // 固定する。旧・無条件上書き実装は c2(重み1)→low(重み90) の呼出順に依存して偶然に正しかった
    // （呼出順は現状のソースでは固定だがそれ自体が地雷＝将来の族追加/並べ替えで壊れうる）。この回帰
    // テストは「同一セルで複数族が重なったとき常に最重の族が表示される」という不変条件を固定する。
    [Fact]
    public void CountViolationsPrefersHeavierFamilyOverLighterAtSameCell()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("s0", 0) };
        // X を1回しか勤務していない: cons2(count>=3)とstaffRange低(lo=3)の両方が同一セル(0,1=staff0,shift X)で発火。
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 0, 0, 0 } };
        var st = new MagiState(
            StartDate: "2025-01-01", EndDate: "2025-01-04",
            Shifts: shifts, Groups: groups, StaffList: staff,
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: schedule,
            Wishes: new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range> { ["0,1"] = new("3", "") },
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(),
            Cons2: new List<C2Row> { new("X", "3") },
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var report = UnifiedViolationChecker.Check(st);
        Assert.Equal(1, report.Breakdown["c2"]);
        Assert.Equal(2, report.Breakdown["low"]);   // lo(3) - got(1) = 2
        Assert.Equal("vio-low", report.CountViolations["0,1"]);   // 重い族(low=90)が軽い族(c2=1)を上書きしない/されない
        // [3.353.0 移植元] countViolations は最重1クラスなので、この盤面では c2 が**どこにも現れない**。
        //   実機ログでも「内訳 c2=1 なのに違反詳細に c2 行が無い」として観測された。countFamilies は
        //   重なった全クラスを重み降順で保持する（先頭は countViolations と常に一致）。
        Assert.Equal(new List<string> { "vio-low", "vio-c2" }, report.CountFamilies["0,1"]);
        Assert.DoesNotContain("vio-c2", report.CountViolations.Values);
    }

    /// <summary>
    /// [3.353.0 移植元] apt(重み1.0)が low(90)/high(45)と同じ (職員,シフト) に重なると countViolations から
    /// 消える。実データ3件でも golden 5件・real 8件・user 1件がこの形で隠れていた。
    /// </summary>
    [Fact]
    public void CountFamiliesKeepsAptWhenItOverlapsWithHeavierRangeViolation()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("s0", 0) };
        // X を1回だけ勤務: 個人下限3(low)と 適切回数目標3(aptLow) が同じ (staff0, X) で同時に発火する。
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 0, 0, 0 } };
        var st = new MagiState(
            StartDate: "2025-01-01", EndDate: "2025-01-04",
            Shifts: shifts, Groups: groups, StaffList: staff,
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "3" } },
            Schedule: schedule,
            Wishes: new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range> { ["0,1"] = new("3", "") },
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var report = UnifiedViolationChecker.Check(st);
        Assert.Equal(2, report.Breakdown["low"]);      // lo(3) - got(1)
        Assert.Equal(2, report.Breakdown["apt"]);      // |1 - 3|
        Assert.Equal("vio-low", report.CountViolations["0,1"]);
        Assert.Equal(new List<string> { "vio-low", "vio-aptLow" }, report.CountFamilies["0,1"]);
    }

    /// <summary>
    /// [/code-review 移植元, 3.111.0/3.353.0と同根の第3キー空間] covU(重み8000)がc41(重み1)と同じ(シフト,日)に
    /// 重なると needViolations から c41 が消える（breakdownLocations の「群のレンジ」タップ→場所一覧が
    /// 内訳件数より少なく見える）。needFamilies は重なった全クラスを重み降順で保持する。
    /// </summary>
    [Fact]
    public void NeedFamiliesKeepsC41WhenItOverlapsWithCovU()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "3", "") };
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("s0", 0) };
        // day0: s0のみXへ配置＝need1(3)に対しcovU(不足2)、かつG0のXレンジ[2,5]に対してもc41(不足)が同時発火。
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 0, 0, 0 } };
        var st = new MagiState(
            StartDate: "2025-01-01", EndDate: "2025-01-04",
            Shifts: shifts, Groups: groups, StaffList: staff,
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: schedule,
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row> { new("G0", "X", "2", "5") }, Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var report = UnifiedViolationChecker.Check(st);
        Assert.True((report.Breakdown.TryGetValue("covU", out var covU) ? covU : 0) > 0, "前提: covUが発火していること");
        Assert.True((report.Breakdown.TryGetValue("c41", out var c41) ? c41 : 0) > 0, "前提: c41が発火していること");
        Assert.Equal("vio-covU", report.NeedViolations["1,0"]);
        Assert.DoesNotContain("vio-c41", report.NeedViolations.Values);
        Assert.Equal(new List<string> { "vio-covU", "vio-c41" }, report.NeedFamilies["1,0"]);
    }

    // [レビュー#7 3.213.0 移植元] normalizeSchedule が生成する -1 セルで Evaluator.FullEvalParts が
    //   AIOOBE 相当の例外を投げない（C++ fullEvalParts と同じスキップ意味論への対称化）。
    [Fact]
    public void EvaluatorFullEvalIsSafeOnNormalizedMinusOneCells()
    {
        var st = BuildState();
        var p = new Problem(st);
        var raw = st.Schedule.ToIntArray2D();
        raw[0][0] = 99;   // 範囲外 → NormalizeSchedule が -1 に写像
        var norm = ScheduleUtil.NormalizeSchedule(raw, p);
        Assert.Equal(-1, norm[0][0]);
        var v = new Evaluator(p).FullEvalParts(norm);   // 旧実装はここで AIOOBE
        Assert.True(v[0] >= 0 && v[1] >= 0);
    }

    // [レビュー#1 3.213.0 移植元] パック桁単位 1e9 拡大の回帰: soft が旧上限(1e6)を超えても hard/soft 分解が壊れない。
    [Fact]
    public void PackedScoreSplitSurvivesSoftBeyondMillion()
    {
        var ev = new Evaluator(new Problem(BuildState()));
        const long hard = 3L;
        const long soft = 5_000_000L;   // 旧 1e6 パックでは hard=8/soft=0 に化けていた領域
        var (h, sft) = ev.Split(hard * Evaluator.SCORE_HARD_UNIT + soft);
        Assert.Equal(hard, h);
        Assert.Equal(soft, sft);
    }

    // [3.213.0見落とし修正の回帰 移植元] AcceptWorseScore の早期ゲート("delta > 2*SCORE_HARD_UNIT は却下")が
    //   SCORE_HARD_UNIT 拡大(1e6→1e9)後も正しく2e9基準になっていることを固定。旧バグは閾値が
    //   2_000_000のまま残っており、delta=1e8(=1億、新旧いずれの1ハード単位(1e6/1e9)よりずっと小さい
    //   純粋なsoft差程度の値)ですら旧閾値(2e6)を超えるため即ゲート却下されていた。
    [Fact]
    public void AcceptWorseScoreGateThresholdMatchesNewScale()
    {
        const long baseScore = 1000L;
        // delta=1e8: 新閾値(2e9)未満→ゲート通過。極端に大きいtempでBoltzmann項をほぼ1にし
        // (通常運用tempでは delta/(200*temp) が大きすぎ確率がほぼ0になり外部から観測できないため)、
        // ゲートを通過した事実を外部から観測可能にする。旧閾値(2e6)ならここで即false=ゲート却下。
        var candWithinNewGate = baseScore + 100_000_000L;
        Assert.True(V6SearchOperators.AcceptWorseScore(candWithinNewGate, baseScore, 1.0e9, new JavaRandom(1)));
        // delta=3e9: 新閾値(2e9)超なのでtempに関わらずゲートで即却下(RNGに触れる前にreturn falseする)。
        var candBeyondNewGate = baseScore + 3L * Evaluator.SCORE_HARD_UNIT;
        Assert.False(V6SearchOperators.AcceptWorseScore(candBeyondNewGate, baseScore, 1.0e9, new JavaRandom(1)));
    }

    /// <summary>
    /// [3.355.0 移植元] 回数を7曜日へどう配っても消せない weekly 偏差の下限。目標は round(回数/7) なので、
    /// 合計との差（余り）は必ず残る。実データ3件で checker の weekly と突き合わせて 73/126/106 を再現した式。
    /// </summary>
    [Fact]
    public void WeeklyFloorIsTheRemainderAgainstSevenTimesTheRoundedTarget()
    {
        Assert.Equal(0, ScheduleUtil.WeeklyFloorOfCount(0));
        Assert.Equal(0, ScheduleUtil.WeeklyFloorOfCount(7));    // 目標1 × 7 = 7
        Assert.Equal(0, ScheduleUtil.WeeklyFloorOfCount(14));
        Assert.Equal(1, ScheduleUtil.WeeklyFloorOfCount(8));    // 目標1 → 7、余り1
        Assert.Equal(3, ScheduleUtil.WeeklyFloorOfCount(31));   // 目標4 → 28、余り3
        Assert.Equal(3, ScheduleUtil.WeeklyFloorOfCount(11));   // 目標2 → 14、差 -3
        // 床は必ず達成可能: 回数 c を目標値に寄せた配置の実偏差が床と一致する。
        for (var c = 1; c <= 40; c++)
        {
            var tgt = (int)KotlinInterop.MathRound(c / 7.0);
            var wd = new int[7];
            for (var i = 0; i < 7; i++) wd[i] = tgt;
            var rest = c - 7 * tgt;
            var d = 0;
            while (rest > 0) { wd[d % 7]++; rest--; d++; }
            while (rest < 0) { wd[d % 7]--; rest++; d++; }
            Assert.Equal(ScheduleUtil.WeeklyFloorOfCount(c), ScheduleUtil.WeeklyDevOfBucket(wd));
        }
    }
}
