using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.255.0, 受領・検証のうえ適用] <see cref="C1JointLnsPolish"/> 単体の検証。実データ
/// (golden_state.json/sample_state_v6.json、ホストJVM実行)で、既存パイプライン
/// (Window+C1TemporalFlowPolish+BeamWide等)適用後にも追加で改善を見つけること
/// (sample_state_v6.jsonでHARD 5->4)を確認済み。
/// </summary>
public class C1JointLnsPolishTest
{
    [Fact]
    public void ResolvesSimpleC1DeficiencyWithSameDaySwapPartner()
    {
        var shifts = new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G", "G") };
        var staffList = new List<Staff> { new("target", 0), new("partner", 0) };
        var target = new List<int> { 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0 };
        var partner = target.Select(v => v == 1 ? 0 : 1).ToList();
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-11",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { target, partner },
            wishes: new Dictionary<string, int>(),
            // [3.256.0, 厳密ピン保護追加に伴う訂正] 実際に見つかる同日swap束は target の X 回数を
            //   4→6 へ変える（窓[6-10]の充足には既存4回の再配置でなく純増が必要と判明・手計算で確認済み）。
            //   旧 Range("4","4")（意図せぬ厳密ピン）は新設の exactPinRegression 保護に正しく拒否される
            //   ため、本テストの主旨（同日swap束によるc1解消）に無関係な下限4のみへ緩和。
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("4", "") },
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row> { new("5", "X", "2") },
            cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c1", 0));
        Assert.Equal(0, before.Hard);

        var outp = C1JointLnsPolish.Apply(
            st, sched,
            new C1JointLnsPolish.Config(MaxMillis: 2000L, MaxRestarts: 2, MaxDepth: 3));
        var after = UnifiedViolationChecker.Check(st, outp.NewSchedule);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c1", -1));
        Assert.Equal(0, after.Hard);
        Assert.True(outp.Applied > 0, "何らかの手が採用されている");
        Assert.True(after.Total < before.Total, "totalが真に改善する");
    }

    [Fact]
    public void NeverProposesAMoveThatWouldCreateAForbiddenRunAndStillFixesTheReachableWindow()
    {
        // [賢く再構成] generateMovesにc3n(禁止連続)事前フィルタを追加。day0=Y固定(希望)・day1にX
        // を置くと「Y,X」禁止連続になる構成で、①day1へは絶対にXが置かれない(HARD不変・c3n=0のまま)
        // ②同時に別窓(day1-2/day2-3)はday2へXを置くことで正しく解消できる、の両方を確認する。
        // 事前フィルタが無くても最終正しさ(isFinalCandidate+defensive re-check)は保たれる設計だが、
        // これは「事前に弾いても解ける能力を失っていない」ことの回帰ガード。
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "", "") };
        var groups = new List<Group> { new("G", "G") };
        var staffList = new List<Staff> { new("target", 0) };
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-04",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 2, 0, 0, 0 } },
            wishes: new Dictionary<string, int> { ["0,0"] = 2 },
            staffRange: new Dictionary<string, Range>(),
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row> { new("2", "X", "1") },
            cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row> { new(new List<string> { "Y", "X" }) },
            cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c1", 0) > 0, "開始時にc1違反があること");
        Assert.Equal(0, before.Breakdown.GetValueOrDefault("c3n", 0));

        var outp = C1JointLnsPolish.Apply(
            st, sched,
            new C1JointLnsPolish.Config(MaxMillis: 3000L, MaxRestarts: 3, MaxDepth: 3));
        var after = UnifiedViolationChecker.Check(st, outp.NewSchedule);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n", 0)); // "c3nは一切発生しないこと"
        Assert.Equal(0, after.Hard);
        // "day1はXへ絶対に置かれない(禁止連続を作るため)"
        Assert.Equal(0, outp.NewSchedule[0][1]);
        Assert.True(
            after.Breakdown.GetValueOrDefault("c1", 0) < before.Breakdown.GetValueOrDefault("c1", 0),
            "day1-2/day2-3窓はday2のXで解消され、c1違反が減ること");
    }

    [Fact]
    public void IsNoOpWhenNoCons1Rules()
    {
        var shifts = new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G", "G") };
        var staffList = new List<Staff> { new("a", 0) };
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-03",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1, 0 } },
            wishes: new Dictionary<string, int>(), staffRange: new Dictionary<string, Range>(),
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var sched = st.Schedule.ToIntArray2D();
        var outp = C1JointLnsPolish.Apply(st, sched);
        Assert.Equal(0, outp.Applied);
    }

    /// <summary>
    /// [3.312.0] 「構造下限」は c1 の**真の**下限でなければならない。個人回数(low/high)は SOFT で、
    /// c1(15) より重いだけであって禁止ではない。旧実装は <c>rangeHi</c> を count の硬い上限として DP に
    /// 課しており、「rangeHi を超えない範囲での c1 最小値」を返していた＝真の下限より大きくなり、
    /// <c>best.c1 &lt;= lowerBound</c> の早期終了と「構造下限到達」のログを誤って発火させていた。
    ///
    /// 反例: T=7・「4日窓で X を1回以上」・X の個人上限 0。X を1つも置かなければ 4窓すべて違反で
    /// c1=4（weighted 60）。中央に X を1つ置けば c1=0・high=1（weighted 45）＝betterReport は
    /// こちらを選ぶ。したがって c1 の下限は 0 であって 4 ではない。
    /// </summary>
    [Fact]
    public void StructuralLowerBoundIgnoresSoftPersonalCaps()
    {
        var shifts = new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") };
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-07",
            shifts: shifts, groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("s0", 0) }, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 7).ToList() },
            wishes: new Dictionary<string, int>(),
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("", "0") }, // X の個人上限 0（SOFT）
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row> { new("4", "X", "1") },
            cons2: new List<C2Row>(), cons3: new List<C3Row>(), cons3n: new List<C3Row>(),
            cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var p = new Problem(st);
        Assert.Equal(
            0, C1JointLnsPolish.StructuralC1LowerBound(p)); // "SOFT の個人上限は c1 の構造下限を押し上げてはいけない"
    }

    /// <summary>
    /// [3.342.0] 停滞打ち切り（<c>PatienceMs</c>）を入れても keep-best は壊れない。
    ///
    /// 打ち切りは「最良が更新されないまま時間が過ぎたら止める」だけなので、最悪でも root がそのまま
    /// 返る。**単調性（patience を伸ばすほど良くなる）は時間ベースで実行速度に依存するため固定しない**
    /// （3.340.0 のビームは回数ベースだったので単調性まで固定できたが、ここは別）。
    /// </summary>
    [Fact]
    public void PatienceNeverProducesAResultWorseThanTheInput()
    {
        var shifts = new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") };
        var groups = new List<Group> { new("G", "G") };
        var staffList = new List<Staff> { new("target", 0), new("partner", 0) };
        var target = new List<int> { 1, 1, 0, 0, 0, 1, 1, 0, 0, 0, 0 };
        var partner = target.Select(v => v == 1 ? 0 : 1).ToList();
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-11",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { target, partner },
            wishes: new Dictionary<string, int>(),
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("4", "") },
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row> { new("5", "X", "2") },
            cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row>(), cons42: new List<C42Row>());
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        foreach (long patience in new long[] { 1L, 50L, 2_000L, 0L })
        {
            var outp = C1JointLnsPolish.Apply(
                st, sched,
                new C1JointLnsPolish.Config(
                    MaxMillis: 2000L, MaxRestarts: 2, MaxDepth: 3, PatienceMs: patience));
            var after = UnifiedViolationChecker.Check(st, outp.NewSchedule);
            Assert.True(
                !UnifiedViolationChecker.BetterReport(before, after),
                $"patience={patience} で入力より悪化した: " +
                    $"{before.Hard}/{before.WeightedScore}/{before.Total} -> " +
                    $"{after.Hard}/{after.WeightedScore}/{after.Total}");
        }
    }
}
