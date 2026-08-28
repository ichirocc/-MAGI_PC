using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース25] <see cref="V6HotfixPasses.DetectHF70Anomalies"/> の検証。
///
/// 移植元Kotlinテスト無し（<c>grep -rln "detectHF70Anomalies\|HF70Result"</c> および広域の
/// "hf70"/"HF70" 大小文字無視検索、ファイル名検索がいずれも0件。piece24(HF66)と同じ性質の欠落——
/// この診断関数の呼出は <c>runPostOptimization</c> 経由の統合検証のみで、その基盤
/// （<c>V6FinalBridgePortTest.kt</c>）は他ピースと同じ理由で未移植のまま据え置き）。
///
/// この関数はKotlin原本自体が単純な分岐の組合せ（3つの独立した検知項目の有無でメッセージを組み立てる
/// だけ）なので、その3分岐＋無異常ケースを直接カバーする自己導出テストを書く。
/// </summary>
public class V6HotfixPassesHF70Test
{
    [Fact]
    public void ReportsNoAnomaliesOnAValidCleanState()
    {
        // MinimalState.Build() の既定盤面(全員休のみ・希望なし・制約なし)は3分岐すべて非該当。
        var s = MinimalState.Build();
        var r = V6HotfixPasses.DetectHF70Anomalies(s, s.Schedule.ToIntArray2D(), "test");
        Assert.Equal(0, r.Anomalies);
        Assert.EndsWith("異常なし", r.Message);
        Assert.Equal("", r.Advice);
        Assert.Single(r.Logs);
        Assert.Equal("I", r.Logs[0].Level);
    }

    [Fact]
    public void FlagsInvalidAssignmentAndNonPrefHardTogetherWhenAStaffIsScheduledOutsideTheirGroup()
    {
        // G1は休のみ担当可能だが s1(G1) の day0 が A(担当外)に組まれている＝
        // InvalidAssignmentCount が拾う「担当不可配置」であり、同時に groupViol(HARD, pref族ではない)
        // も発火するため hardCore>0 の両方が1つの盤面で同時に立つ。
        var s = MinimalState.Build(
            startDate: "2026-06-01", endDate: "2026-06-02",
            shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: new List<Staff> { new("s0", 0), new("s1", 1) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 0 } },
            schedule: new List<IReadOnlyList<int>>
            {
                new List<int> { 0, 0 },
                new List<int> { 1, 0 },
            });
        var r = V6HotfixPasses.DetectHF70Anomalies(s, s.Schedule.ToIntArray2D(), "test");
        Assert.Equal(2, r.Anomalies);
        Assert.Contains("担当不可/範囲外配置", r.Message);
        Assert.Contains("希望以外HARD", r.Message);
        Assert.DoesNotContain("不可能希望", r.Message);
        Assert.NotEqual("", r.Advice);
        Assert.Equal("W", r.Logs[0].Level);
    }

    [Fact]
    public void FlagsOnlyImpossibleWishWhenTheScheduleItselfIsValid()
    {
        // 盤面自体は全員が担当可能なシフトのみ(異常なし)。ただし s1(G1, 休のみ担当可)が
        // A(担当外)を希望している＝実現不能希望として検出される。実現不能な希望は pref のHARD計上から
        // 対称除外されるため(3.311.0系の規約)、hardCore は0のまま＝この分岐だけが単独で立つ。
        var s = MinimalState.Build(
            startDate: "2026-06-01", endDate: "2026-06-02",
            shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: new List<Staff> { new("s0", 0), new("s1", 1) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 0 } },
            schedule: new List<IReadOnlyList<int>>
            {
                new List<int> { 0, 0 },
                new List<int> { 0, 0 },
            },
            wishes: new Dictionary<string, int> { ["1,0"] = 1 });
        var r = V6HotfixPasses.DetectHF70Anomalies(s, s.Schedule.ToIntArray2D(), "test");
        Assert.Equal(1, r.Anomalies);
        Assert.Contains("不可能希望", r.Message);
        Assert.DoesNotContain("担当不可/範囲外配置", r.Message);
        Assert.DoesNotContain("希望以外HARD", r.Message);
        Assert.NotEqual("", r.Advice);
        Assert.Equal("W", r.Logs[0].Level);
    }

    [Fact]
    public void ReusesTheSuppliedReportInsteadOfRecomputingIt()
    {
        // Kotlin原本の既定引数(report = check(state, schedule))はC#では非定数式のため、
        // report: ViolationReport? = null ＋ 本体内 null合体 として移した（本テストで確認）。
        var s = MinimalState.Build();
        var sched = s.Schedule.ToIntArray2D();
        var supplied = UnifiedViolationChecker.Check(s, sched);
        var r = V6HotfixPasses.DetectHF70Anomalies(s, sched, "test", supplied);
        Assert.Equal(0, r.Anomalies); // 明示的に渡したreportでも既定省略時と同じ結果になる
    }
}
