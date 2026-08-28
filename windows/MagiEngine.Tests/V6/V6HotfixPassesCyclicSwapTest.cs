using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, 循環交換系] <see cref="V6HotfixPasses.ApplyCyclicSwapPolish"/>／
/// <see cref="V6HotfixPasses.ApplyC3SequencePolish"/> の検証。
///
/// [Kotlin原本] <c>V6FinalBridgePortTest.kt</c>の <c>cyclicSwapPolishNeverWorsens</c>
/// （<c>sampleState()</c> 固定盤面での退化しないことの確認）を <see cref="NeverWorsensTheGivenSchedule"/>
/// として、<c>c3SequencePolishNeverWorsens</c>（同じ<c>sampleState()</c>を使う同型テスト）を
/// <see cref="C3SequencePolishNeverWorsensTheGivenSchedule"/> として移植。それに加え、実際に
/// 同日2職員スワップ(k=2)や2日ブロック交換が採用され改善することを確認する
/// <see cref="ResolvesSymmetricLowDeficiencyViaSameDaySwap"/>／
/// <see cref="C3SequencePolishResolvesForbiddenPatternViaTwoDayBlockSwap"/> を新設（採否の実効性
/// そのものはKotlin側テストが直接検証していなかったため、手計算で設計した最小盤面で追加検証する）。
/// </summary>
public class V6HotfixPassesCyclicSwapTest
{
    // [Kotlin原本 sampleState()] 2日間・2シフト（日勤/休み）・1グループ・2職員。
    private static MagiState SampleState() => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-02",
        shifts: new List<Shift> { new("日勤", "日", "1", "1"), new("休み", "休", "", "") },
        groups: new List<Group> { new("A", "A") },
        staffList: new List<Staff> { new("s1", 0), new("s2", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1 }, new List<int> { 1, 0 } },
        wishes: new Dictionary<string, int>(),
        staffRange: new Dictionary<string, Range> { ["0,0"] = new("0", "2") },
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
        cons41: new List<C41Row>(), cons42: new List<C42Row>());

    [Fact]
    public void NeverWorsensTheGivenSchedule()
    {
        var st = SampleState();
        var raw = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, raw);
        var r = V6HotfixPasses.ApplyCyclicSwapPolish(st, raw);
        var after = UnifiedViolationChecker.Check(st, r.NewSchedule);
        // 退化しない: hard→weighted→total の辞書順で悪化しない（afterがbeforeより厳密に劣らない）。
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.Equal(0, ScheduleAssertions.InvalidAssignmentCount(st, r.NewSchedule)); // 被覆保存＝割当は常に妥当
    }

    // shifts: Y(0)/X(1)。staff: a(0)/b(1)、両者ともG0所属(担当可否は両シフトとも許可＝既定)。
    // schedule: a=[X,X]（Yを0回）・b=[Y,Y]（Xを0回）。staffRange で a に Y>=1・b に X>=1 を課す。
    // 手計算: day0のa↔bスワップだけで両者のlow違反(各1件)が同時に解消し、副次的にfair
    //   （群内公平化）もその1手で 4->0 に改善する（day1もスワップすると 0->4 へ悪化するため
    //   2巡目以降で不採用になり戻らないことも確認済み）。staff=2人のためk=3ローテーションは
    //   候補が存在しない（第3の職員が居ない）。
    private static MagiState SwapFixture() => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-02",
        shifts: new List<Shift> { new("Y", "Y", "", ""), new("X", "X", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("a", 0), new("b", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0, 0 } },
        wishes: new Dictionary<string, int>(),
        staffRange: new Dictionary<string, Range>
        {
            ["0,0"] = new("1", ""), // a, Y >= 1
            ["1,1"] = new("1", ""), // b, X >= 1
        },
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
        cons41: new List<C41Row>(), cons42: new List<C42Row>());

    [Fact]
    public void ResolvesSymmetricLowDeficiencyViaSameDaySwap()
    {
        var st = SwapFixture();
        var raw = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, raw);
        Assert.Equal(2, before.Breakdown.GetValueOrDefault("low", 0));
        Assert.Equal(0, before.Hard);

        var r = V6HotfixPasses.ApplyCyclicSwapPolish(st, raw);
        var after = UnifiedViolationChecker.Check(st, r.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("low", -1));
        Assert.Equal(0, after.Hard);
        Assert.True(after.Total < before.Total, "totalが真に改善する");
        Assert.Equal(1, r.Applied); // day0の1手のみ採用（day1側の追加スワップはfairを悪化させ不採用）
        Assert.Equal(0, r.NewSchedule[0][0]); // a: day0 = Y
        Assert.Equal(1, r.NewSchedule[0][1]); // a: day1 = X（不変）
        Assert.Equal(1, r.NewSchedule[1][0]); // b: day0 = X
        Assert.Equal(0, r.NewSchedule[1][1]); // b: day1 = Y（不変）
        Assert.Equal(0, ScheduleAssertions.InvalidAssignmentCount(st, r.NewSchedule));
    }

    [Fact]
    public void ShouldStopHaltsBeforeAnySwapIsAttempted()
    {
        var st = SwapFixture();
        var raw = st.Schedule.ToIntArray2D();
        var r = V6HotfixPasses.ApplyCyclicSwapPolish(st, raw, shouldStop: () => true);
        Assert.Equal(0, r.Applied);
        Assert.Equal(raw[0][0], r.NewSchedule[0][0]);
        Assert.Equal(raw[1][0], r.NewSchedule[1][0]);
    }

    // [Kotlin原本 c3SequencePolishNeverWorsens] sampleState() は cons3mn 等いずれも空＝
    // 起点(anchorStaff)が最初から空のため、この呼出自体が事実上no-op(1周目で即break)になる。
    // それでも「退化しない」不変条件そのものは正しく検証できる（Kotlin側テストもこの構成のまま）。
    [Fact]
    public void C3SequencePolishNeverWorsensTheGivenSchedule()
    {
        var st = SampleState();
        var raw = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, raw);
        var r = V6HotfixPasses.ApplyC3SequencePolish(st, raw);
        var after = UnifiedViolationChecker.Check(st, r.NewSchedule);
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.Equal(0, ScheduleAssertions.InvalidAssignmentCount(st, r.NewSchedule));
    }

    // shifts: X(0)/Y(1)/Z(2)。staff: a(G0単独)/b(G1単独)——別グループにすることで差分前フィルタ
    //   (canPre = 同sgrp&&同ssk)を素通しし、スワップ機構そのもの（適用→フル検査→採否）を直接検証する。
    //   単独メンバー群のため fair は m<2 で無関係（常に0）。
    // cons3mn = [X,Y]（回避パターン＝forbidden系。窓の完全一致だけが発火する＝c3FamCount/実チェッカー
    //   とも同一の式 fire = (z == d-1)）。
    // schedule: a=[Z,X,Y]・b=[Z,Z,Z]。
    //   手計算(実チェッカーの窓マッチ式で検算): aはj=1窓(day1-2=X,Y)がseq=[X,Y]に完全一致＝1回発火。
    //   j=0窓(day0-1=Z,X)はZ!=Xで不一致。bはXを一度も含まないため0回。before合計=1。
    //   2日ブロック交換(w=2,j=0, a<->b)適用後: a'=[Z,Z,Y]・b'=[Z,X,Z]。
    //   a'のどちらの窓もX始点が無く不一致、b'のj=1窓(X,Z)はseq[1]=Yに一致せずz=0<d-1=1で不一致。
    //   after合計=0＝改善1件。c3mn重み30に対しweekly等の副次変化(重み1)は遠く及ばないため必ず採用される。
    private static MagiState C3mnFixture() => MinimalState.Build(
        startDate: "2025-12-01", endDate: "2025-12-03",
        shifts: new List<Shift> { new("X", "X", "", ""), new("Y", "Y", "", ""), new("Z", "Z", "", "") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        staffList: new List<Staff> { new("a", 0), new("b", 1) },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 2, 0, 1 }, new List<int> { 2, 2, 2 } },
        cons3mn: new List<C3Row> { new(new List<string> { "X", "Y" }) });

    [Fact]
    public void C3SequencePolishResolvesForbiddenPatternViaTwoDayBlockSwap()
    {
        var st = C3mnFixture();
        var raw = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, raw);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c3mn", 0));
        Assert.Equal(0, before.Hard);

        var r = V6HotfixPasses.ApplyC3SequencePolish(st, raw);
        var after = UnifiedViolationChecker.Check(st, r.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3mn", 0));
        Assert.Equal(0, after.Hard);
        Assert.True(r.Applied >= 1, "少なくとも設計した2日ブロック交換(w=2,j=0)が採用される");
        Assert.Equal(0, ScheduleAssertions.InvalidAssignmentCount(st, r.NewSchedule));
    }

    [Fact]
    public void C3SequencePolishShouldStopHaltsBeforeAnySwapIsAttempted()
    {
        var st = C3mnFixture();
        var raw = st.Schedule.ToIntArray2D();
        var r = V6HotfixPasses.ApplyC3SequencePolish(st, raw, shouldStop: () => true);
        Assert.Equal(0, r.Applied);
        Assert.Equal(raw[0][0], r.NewSchedule[0][0]);
        Assert.Equal(raw[1][0], r.NewSchedule[1][0]);
    }
}
