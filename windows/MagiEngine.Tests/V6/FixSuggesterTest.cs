using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース8] <c>V6SwapSuggesterTest.kt</c>の唯一のテストを移植。
///
/// [重複排除の頑健化] <c>FixSuggester.Suggest</c> が実質同一の盤面変化を複数回返さないことを検証する。
/// 旧署名(kind名+ops列挙順)は①SwapXDayが起点(i1,j1)/(i2,j2)どちらから見るかでopsが逆順生成され別署名化
/// ②Phase5(SwapXDay)がj2==j1(同日)を除外していないためPhase2(Swap)と同じ盤面変化を別kindで重複生成、
/// の2種で同一の手を複数回表示していた。両方の違反(low/high)を1回のスワップで同時解消できる最小盤面を
/// 用意し、その唯一の解が重複なく1件だけ返ることを確認する。
/// </summary>
public class FixSuggesterTest
{
    private static IReadOnlyList<Shift> Shifts() =>
        new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") };

    [Fact]
    public void SuggestDoesNotDuplicateSameBoardChangeAcrossKinds()
    {
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("s0", 0), new("s1", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 0, 0 },   // s0: Y,Y（Xのlo=1を満たさない＝low違反）
            new List<int> { 1, 0 },   // s1: X,Y（Xのhi=0を超える＝high違反）
        };
        var st = new MagiState(
            StartDate: "2026-01-01",
            EndDate: "2026-01-02",
            Shifts: Shifts(),
            Groups: groups,
            StaffList: staff,
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: schedule,
            Wishes: new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range>
            {
                ["0,1"] = new Range(Lo: "1", Hi: ""),
                ["1,1"] = new Range(Lo: "", Hi: "0"),
            },
            NeedDay1: new Dictionary<string, string>(),
            NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(),
            Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(),
            Cons3m: new List<C3Row>(),
            Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(),
            Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(),
            Cons41s: new List<C41Row>(),
            Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());

        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, before.Hard);
        Assert.True((before.Breakdown.GetValueOrDefault("low", 0) + before.Breakdown.GetValueOrDefault("high", 0)) > 0,
            "初期 low+high 違反あり");

        var results = FixSuggester.Suggest(st, sched, maxResults: 20, deadlineMs: 8000L);

        // day を含めた正規化署名（本体のsigとは独立に、テスト側で盤面変化の実体を数える）。
        string NormSig(FixSuggestion sug)
        {
            var real = sug.Ops.Where(op => op.ToShift != sched[op.Staff][op.Day]).ToList();
            return string.Join("|",
                real.OrderBy(op => op.Staff).ThenBy(op => op.Day).Select(op => $"{op.Staff}.{op.Day}.{op.ToShift}"));
        }
        var sigs = results.Select(NormSig).ToList();
        Assert.Equal(sigs.Count, sigs.ToHashSet().Count);

        // 低/高を同時に解消する「同日スワップ」に相当する提案が、kind(Swap/SwapXDay)やops順に依らず
        // 重複なくちょうど1件だけ含まれること。
        var fullFix = results.Where(sug =>
        {
            var real = sug.Ops.Where(op => op.ToShift != sched[op.Staff][op.Day]).ToList();
            return real.Count == 2
                && real.Any(op => op.Staff == 0 && op.Day == 0 && op.ToShift == 1)
                && real.Any(op => op.Staff == 1 && op.Day == 0 && op.ToShift == 0);
        }).ToList();
        Assert.Single(fullFix);
    }
}
