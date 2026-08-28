using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [汎用玉突き結合フレームワーク, 3.249.0] <see cref="CombinatorialRepair.CombineAndApply"/> 自体の
/// 直接検証。c1/range/c3mn/apt/fair の5族はいずれもこの同一ヘルパへ「単独では不採用だった候補」を
/// 供給するだけ（各族固有の捕捉箇所は各Polishパス自身のテストで確認済み）。ここでは
/// 共有ロジック本体（組合せ列挙・重複セル排除・shouldStop 打ち切り・統計集計）を、
/// AptPolishTest と同一の検証済み最小盤面（<c>CombineTwoRejectedState</c> 相当）を用いて
/// パイプラインを経由せず直接検証する。
/// </summary>
public class CombinatorialRepairTest
{
    private static bool IsBetterLocal(ViolationReport a, ViolationReport b)
    {
        if (a.Hard != b.Hard) return a.Hard < b.Hard;
        if (a.Total != b.Total) return a.Total < b.Total;
        return a.WeightedScore < b.WeightedScore;
    }

    // shifts: 休(0) P(1) Qres(2) D(3)。staff: X(0) Y(1) W1(2) W2(3)。
    // X=P(aptHigh, 目標0)・Y=Qres(目標なし、Dへ動けばaptLow(目標1)を解消)。
    // Xの唯一の代替候補DはstaffRangeでhi=0固定＝Xが単独で「解決」する抜け道を塞ぐ。
    // G0のQres在籍数は常に1人固定(c41 l=u=1)。X到着(+1)とY退出(-1)が相殺すると
    // c41違反ゼロのままapt違反だけが2件解消する＝どちらも単独では不採用(タイ)。
    // W1/W2(常に休、動かない)は fair(グループ内公平化) の分母を薄める補助。X,Yのみ(2人)だと
    // X単独の1手がP側とQres側のfair偏りを同時に均してしまい、apt/c41の弱いタイをfairの大きな
    // 隠れ改善が圧倒してしまう（AptPolishTestの敵対検証=CI実測で発見・Pythonで独立再検証済み）。
    // W1/W2にもstaffRangeでDを禁止(hi=0)し、apt目標をクランプで実効0へ潰す（さもないとD目標=1が
    // グループ共有のためW1/W2も常時aptLow(D)を持ち、彼ら自身がDへ動いて「解決」してしまう）。
    private static MagiState CombineTwoRejectedState()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("P", "P", "", ""),
            new("Qres", "Qres", "", ""),
            new("D", "D", "", ""),
        };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } };
        var groupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "0", "", "1" } };
        var staffList = new List<Staff> { new("X", 0), new("Y", 0), new("W1", 0), new("W2", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1 }, new List<int> { 2 }, new List<int> { 0 }, new List<int> { 0 },
        };
        return MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts, groups: groups, staffList: staffList, use2Patterns: false,
            groupShift: groupShift, groupShiftApt: groupShiftApt,
            schedule: schedule, wishes: new Dictionary<string, int>(),
            staffRange: new Dictionary<string, Range>
            {
                ["0,3"] = new("", "0"), // X, D
                ["2,3"] = new("", "0"), // W1, D
                ["3,3"] = new("", "0"), // W2, D
            },
            needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
            cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
            cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
            cons41: new List<C41Row> { new("G0", "Qres", "1", "1") }, cons42: new List<C42Row>());
    }

    [Fact]
    public void CombineAndApplyAcceptsPairRejectedIndividuallyButImprovingTogether()
    {
        var st = CombineTwoRejectedState();
        var work = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, work);
        // apt=2: X(P超過1)+Y(D不足1)。W1/W2はstaffRangeのクランプで実効目標0=違反なし。
        Assert.Equal(2, before.Breakdown.GetValueOrDefault("apt", 0));
        Assert.Equal(0, before.Hard);

        var candX = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 2 } }, "test", "X");
        var candY = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 1, 0, 3 } }, "test", "Y");

        // [検証: 単独では不採用] それぞれ単独で適用するとタイ(改善なし)で不採用になることを確認
        //   （combineAndApply が実際に解くべき前提条件そのものを、まず自分で確かめる）。
        {
            var w2 = st.Schedule.ToIntArray2D();
            w2[0][0] = 2;
            var rep = UnifiedViolationChecker.Check(st, w2);
            Assert.False(IsBetterLocal(rep, before), "Xの単独移動は不採用(タイ)であるはず");
        }
        {
            var w2 = st.Schedule.ToIntArray2D();
            w2[1][0] = 3;
            var rep = UnifiedViolationChecker.Check(st, w2);
            Assert.False(IsBetterLocal(rep, before), "Yの単独移動は不採用(タイ)であるはず");
        }

        var stats = new CombinatorialRepair.Stats();
        var after = CombinatorialRepair.CombineAndApply(
            st, work, before, new List<CombinatorialRepair.Candidate> { candX, candY }, IsBetterLocal,
            stats: stats);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("apt", -1)); // 結合後はapt=0
        Assert.Equal(0, after.Hard); // HARDは悪化しない
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c41", -1)); // c41は相殺されゼロのまま
        Assert.Equal(2, work[0][0]);
        Assert.Equal(3, work[1][0]);
        Assert.Equal(1, stats.CombosAccepted);
        Assert.Equal(2, stats.MechanismCounts["test"]);
        Assert.NotEmpty(stats.AcceptedLabels);
    }

    [Fact]
    public void CombineAndApplySkipsCandidatesThatOverlapTheSameCell()
    {
        var st = CombineTwoRejectedState();
        var work = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, work);

        // 両方とも同一セル(staff0,day0)を触る＝互いに排他な代替案。組合せても意味を持たない
        // ため、フルchecker呼出をスキップして採用されないことを確認する。
        var candA = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 2 } }, "test");
        var candB = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 3 } }, "test");

        var stats = new CombinatorialRepair.Stats();
        var after = CombinatorialRepair.CombineAndApply(
            st, work, before, new List<CombinatorialRepair.Candidate> { candA, candB }, IsBetterLocal,
            stats: stats);

        Assert.Equal(0, stats.CombosAccepted); // 採用0件
        Assert.Equal(1, work[0][0]); // 盤面は不変
        Assert.Equal(before.Breakdown.GetValueOrDefault("apt", -1), after.Breakdown.GetValueOrDefault("apt", -1)); // 違反も不変
    }

    [Fact]
    public void CombineAndApplyStopsImmediatelyWhenShouldStopIsTrue()
    {
        var st = CombineTwoRejectedState();
        var work = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, work);

        var candX = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 2 } }, "test");
        var candY = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 1, 0, 3 } }, "test");

        var stats = new CombinatorialRepair.Stats();
        var after = CombinatorialRepair.CombineAndApply(
            st, work, before, new List<CombinatorialRepair.Candidate> { candX, candY }, IsBetterLocal,
            shouldStop: () => true, stats: stats);

        Assert.True(stats.Truncated, "打ち切りフラグが立つこと");
        Assert.Equal(0, stats.CombosAccepted); // 採用0件
        Assert.Equal(1, work[0][0]); // 盤面は不変
        Assert.Equal(before.Breakdown.GetValueOrDefault("apt", -1), after.Breakdown.GetValueOrDefault("apt", -1)); // 違反も不変
    }

    [Fact]
    public void CombineAndApplyIsNoOpWithFewerThanTwoCandidates()
    {
        var st = CombineTwoRejectedState();
        var work = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, work);

        var candX = new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 2 } }, "test");
        var stats = new CombinatorialRepair.Stats();
        var after = CombinatorialRepair.CombineAndApply(
            st, work, before, new List<CombinatorialRepair.Candidate> { candX }, IsBetterLocal, stats: stats);

        Assert.Equal(0, stats.CombosTried);
        Assert.Equal(0, stats.CombosAccepted);
        Assert.Equal(before, after);
    }

    // [停滞検知, ユーザー指示「早期脱出しないのか?」への対応] 全て同一セル(staff0,day0)への
    // 無変化(no-op)候補＝どの2件を組合せても必ず重複セルでスキップされ続ける。10件・C(10,2)=45通り
    // のうち、maxStagnantTries=3で全網羅する前に早期終了することを固定。
    [Fact]
    public void CombineAndApplyGivesUpEarlyAfterConsecutiveMisses()
    {
        var st = CombineTwoRejectedState();
        var work = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, work);

        var dupes = Enumerable.Range(0, 10)
            .Select(_ => new CombinatorialRepair.Candidate(new List<int[]> { new[] { 0, 0, 1 } }, "dup"))
            .ToList();

        var stats = new CombinatorialRepair.Stats();
        var after = CombinatorialRepair.CombineAndApply(
            st, work, before, dupes, IsBetterLocal, maxStagnantTries: 3, stats: stats);

        Assert.True(stats.StagnantExit, "停滞検知で早期終了");
        Assert.False(stats.Truncated, "時間切れではない");
        Assert.Equal(0, stats.CombosAccepted); // 採用0件
        Assert.Equal(3, stats.CombosTried); // maxStagnantTries通りで打ち切り(45通り網羅しない)
        Assert.Equal(before, after); // 盤面は不変
    }
}
