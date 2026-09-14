using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.542.0] 希望の前日に禁止（cons3w / 違反キー c3w, HARD 9000）。<c>C3wConstraintTest.kt</c> の逐語移植。
/// 意味: 希望(ws3)で固定された X の前日セルが Y なら違反。初日・希望でない X・実現不可能な希望は対象外。
/// チェッカー／Evaluator／DeltaEvaluator の3者一致、枝刈り（MakesForbiddenRun）、設定ミス診断、JSON/CSV 往復を固定する。
/// </summary>
public class C3wConstraintTest
{
    private const int Rest = 0, A = 1, B = 2;

    private static MagiState State(
        List<List<int>> schedule, IReadOnlyDictionary<string, int> wishes, IReadOnlyList<C3wRow> cons3w,
        List<List<int>>? groupShift = null)
    {
        groupShift ??= new List<List<int>> { new() { 1, 1, 1 } };
        return new MagiState(
            StartDate: "2026-01-01", EndDate: "2026-01-05",
            Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0), new("s1", 0) },
            Use2Patterns: false,
            GroupShift: groupShift.Select(row => (IReadOnlyList<int>)row).ToList(),
            GroupShiftApt: Array.Empty<IReadOnlyList<string>>(),
            Schedule: schedule.Select(row => (IReadOnlyList<int>)row).ToList(),
            Wishes: wishes,
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(),
            NeedDay2: new Dictionary<string, string>(),
            Cons1: Array.Empty<C1Row>(), Cons2: Array.Empty<C2Row>(),
            Cons3: Array.Empty<C3Row>(), Cons3n: Array.Empty<C3Row>(),
            Cons3m: Array.Empty<C3Row>(), Cons3mn: Array.Empty<C3Row>(),
            Cons41: Array.Empty<C41Row>(), Cons42: Array.Empty<C42Row>(),
            SkillGroups: Array.Empty<Group>(),
            Cons41s: Array.Empty<C41Row>(), Cons42s: Array.Empty<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: MinimalState.NoExtras,
            Cons3w: cons3w
        );
    }

    private static readonly List<int> Row1 = new() { Rest, Rest, Rest, Rest, Rest };

    [Fact]
    public void BannedCellCountsOnceInCheckerEvaluatorAndDelta()
    {
        // s0: 希望 (0,2)=A、前日 (0,1)=B → c3w=1（違反箇所は前日側のセル）
        var st = State(
            new List<List<int>> { new() { B, B, A, Rest, Rest }, Row1 },
            new Dictionary<string, int> { ["0,2"] = A },
            new List<C3wRow> { new("A", "B") });
        var sched = st.Schedule.ToIntArray2D();
        var rep = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, rep.Breakdown["c3w"]);
        Assert.Equal(1, rep.Hard);
        Assert.Equal("vio-c3w", rep.Violations["0,1"]);
        Assert.Equal(9000.0, MirrorKeys.WeightOf("c3w"));
        Assert.Contains("c3w", MirrorKeys.Hard);

        var p = new Problem(st);
        var parts = new Evaluator(p).FullEvalParts(sched);
        Assert.Equal(1L, parts[0]);

        var de = new DeltaEvaluator(p);
        Assert.Equal(1L, de.FamilyRaw()["c3w"]);
        de.Apply(0, 1, Rest);
        Assert.Equal(0L, de.FamilyRaw()["c3w"]);
        Assert.Equal(new Evaluator(p).FullEval(de.Snapshot()), de.Score());
        de.Apply(0, 1, B);
        Assert.Equal(1L, de.FamilyRaw()["c3w"]);
        Assert.Equal(new Evaluator(p).FullEval(de.Snapshot()), de.Score());
    }

    [Fact]
    public void FirstDayAndNonWishedXAreExempt()
    {
        // 希望は (0,0)=A だけ。day0 に前日は無く、day2/day4 の A は希望でない → 0
        var st = State(
            new List<List<int>> { new() { A, B, A, B, A }, Row1 },
            new Dictionary<string, int> { ["0,0"] = A },
            new List<C3wRow> { new("A", "B") });
        Assert.Equal(0, UnifiedViolationChecker.Check(st).Breakdown["c3w"]);
    }

    [Fact]
    public void ImpossibleWishDoesNotTrigger()
    {
        // 群が A を担当できない＝希望 A は固定されない（WishLocked=false）→ 前日の B は違反にしない
        var st = State(
            new List<List<int>> { new() { B, B, A, Rest, Rest }, Row1 },
            new Dictionary<string, int> { ["0,2"] = A },
            new List<C3wRow> { new("A", "B") },
            groupShift: new List<List<int>> { new() { 1, 0, 1 } });
        Assert.Equal(0, UnifiedViolationChecker.Check(st).Breakdown["c3w"]);
    }

    [Fact]
    public void MakesForbiddenRunPrunesTheBannedShift()
    {
        var st = State(
            new List<List<int>> { new() { Rest, Rest, A, Rest, Rest }, Row1 },
            new Dictionary<string, int> { ["0,2"] = A },
            new List<C3wRow> { new("A", "B") });
        var p = new Problem(st);
        var sched = st.Schedule.ToIntArray2D();
        Assert.True(p.C3wBanned(0, 1, B));
        Assert.True(p.MakesForbiddenRun(sched, 0, 1, B));
        Assert.False(p.MakesForbiddenRun(sched, 0, 1, Rest));
        Assert.False(p.MakesForbiddenRun(sched, 0, 0, B));   // 翌日 (0,1) は希望でない
        Assert.False(p.MakesForbiddenRun(sched, 1, 1, B));   // 別の職員
    }

    [Fact]
    public void WishOnBothDaysIsCountedAndDiagnosed()
    {
        var st = State(
            new List<List<int>> { new() { Rest, B, A, Rest, Rest }, Row1 },
            new Dictionary<string, int> { ["0,1"] = B, ["0,2"] = A },
            new List<C3wRow> { new("A", "B") });
        Assert.Equal(1, UnifiedViolationChecker.Check(st).Breakdown["c3w"]);
        var issue = V6SanityPort.BuildGuidance(st)
            .FirstOrDefault(i => i.WishKey == "0,1" && i.Action == SettingFixAction.RemoveWish);
        Assert.NotNull(issue);
        Assert.Contains("希望どうし", issue!.Problem);
    }

    [Fact]
    public void JsonAndCsvRoundTrip()
    {
        var st = State(
            new List<List<int>> { Row1, Row1 }, new Dictionary<string, int>(),
            new List<C3wRow> { new("A", "B"), new("休", "A") });
        var back = StateJsonSerializer.Parse(StateJsonSerializer.Serialize(st, st.Schedule.ToIntArray2D()));
        Assert.Equal(st.Cons3w, back.Cons3w);
        var csv = ConstraintsCsvIO.Parse(ConstraintsCsvIO.Build(st), st);
        Assert.Equal(st.Cons3w, csv!.State.Cons3w);
        Assert.Equal(2, csv.Accepted);
        // 既存 JSON（cons3w キー無し）は空として読む
        var noC3w = st with { Cons3w = null };
        Assert.Empty(StateJsonSerializer.Parse(StateJsonSerializer.Serialize(noC3w, st.Schedule.ToIntArray2D())).Cons3w ?? Array.Empty<C3wRow>());
    }
}
