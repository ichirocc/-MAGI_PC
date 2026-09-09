using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [backlog #12(a)・実験段階] c2(不足量)/c41・c41s(距離)の量的評価モード（<see cref="Problem.QuantitativeRangeEval"/>）。
/// 既定(false)は全既存呼出元と挙動不変、true のときだけ Checker・Evaluator・DeltaEvaluator が
/// <see cref="Evaluator.C2Amount"/>/<see cref="Evaluator.RangeDistance"/> を使う。
/// Kotlin <c>QuantitativeRangeEvalTest.kt</c> の1対1移植（期待値も同一）。
/// </summary>
public class QuantitativeRangeEvalTest
{
    [Fact]
    public void C2AmountIsShortfallOnly()
    {
        Assert.Equal(0L, Evaluator.C2Amount(5, 3)); // 超過は0（c2は下限のみ）
        Assert.Equal(0L, Evaluator.C2Amount(3, 3)); // ちょうど= 0
        Assert.Equal(1L, Evaluator.C2Amount(2, 3));
        Assert.Equal(3L, Evaluator.C2Amount(0, 3));
    }

    [Fact]
    public void RangeDistanceIsZeroInsideRangeAndDistanceOutside()
    {
        Assert.Equal(0L, Evaluator.RangeDistance(2, 1, 3));
        Assert.Equal(0L, Evaluator.RangeDistance(1, 1, 3));
        Assert.Equal(0L, Evaluator.RangeDistance(3, 1, 3));
        Assert.Equal(1L, Evaluator.RangeDistance(0, 1, 3)); // lo側に1不足
        Assert.Equal(3L, Evaluator.RangeDistance(6, 1, 3)); // hi側に3超過
    }

    private static MagiState BuildState(IReadOnlyList<IReadOnlyList<int>> schedule) => MinimalState.Build(
        startDate: "2025-01-01", endDate: $"2025-01-0{schedule[0].Count}",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0), new("s2", 0), new("s3", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        schedule: schedule,
        cons2: new List<C2Row> { new("A", "3") },
        cons41: new List<C41Row> { new("G0", "B", "2", "2") });

    /// <summary>
    /// 全員 A が0回・B(=[2,2])が0人 → c2不足=3(目標)×4人、c41距離=2×日数（不足のみ）。
    /// fullEvalParts の合計には weekly 等の無関係な族も乗るため、量的/二値の**差分**（=このモードだけが
    /// 動かす分）を検証する: 差分 = c2の(不足量-件数)×4 + c41の(距離-件数)×5 = (12-4)+(10-5) = 13。
    /// </summary>
    [Fact]
    public void EvaluatorQuantitativeSumMatchesHandComputedAmount()
    {
        const int days = 5;
        var schedule = Enumerable.Range(0, 4)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, days).ToList()).ToList();
        var p = new Problem(BuildState(schedule), quantitativeRangeEval: true);
        var ev = new Evaluator(p);
        var parts = ev.FullEvalParts(p.InitialAssignment());

        var pBin = new Problem(BuildState(schedule)); // 既定=false
        Assert.False(pBin.QuantitativeRangeEval);
        var evBin = new Evaluator(pBin);
        var partsBin = evBin.FullEvalParts(pBin.InitialAssignment());

        Assert.Equal(13L, parts[1] - partsBin[1]);
    }

    [Fact]
    public void CheckerQuantitativeBreakdownMatchesEvaluator()
    {
        const int days = 5;
        var schedule = Enumerable.Range(0, 4)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, days).ToList()).ToList();
        var state = BuildState(schedule);
        var p = new Problem(state, quantitativeRangeEval: true);
        var ev = new Evaluator(p);
        var parts = ev.FullEvalParts(p.InitialAssignment());

        var report = UnifiedViolationChecker.Check(state, p.InitialAssignment(), quantitativeRangeEval: true);
        Assert.Equal(12, report.Breakdown["c2"]);
        Assert.Equal(10, report.Breakdown["c41"]);
        Assert.Equal(parts[1], (long)report.Soft);

        // 既定(false)は従来どおり二値件数のまま
        var reportBin = UnifiedViolationChecker.Check(state, p.InitialAssignment());
        Assert.Equal(4, reportBin.Breakdown["c2"]);
        Assert.Equal(5, reportBin.Breakdown["c41"]);
    }

    [Fact]
    public void DeltaMatchesFullEvalUnderQuantitativeMode()
    {
        const int days = 6;
        var schedule = Enumerable.Range(0, 4)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, days).ToList()).ToList();
        var p = new Problem(BuildState(schedule), quantitativeRangeEval: true);
        var ev = new Evaluator(p);
        var de = new DeltaEvaluator(p);
        de.Reset(p.InitialAssignment());
        Assert.Equal(ev.FullEval(p.InitialAssignment()), de.Score());
        var rng = new Random(999);
        for (int n = 0; n < 3_000; n++)
        {
            int i = rng.Next(p.S), j = rng.Next(p.T);
            int old = de.At(i, j);
            int nw = rng.Next(p.K);
            de.Apply(i, j, nw);
            Assert.Equal(ev.FullEval(de.Snapshot()), de.Score());
            if (rng.Next(2) == 0)
            {
                de.Apply(i, j, old);
                Assert.Equal(ev.FullEval(de.Snapshot()), de.Score());
            }
        }
    }

    /// <summary>[デフォルト不変条件] quantitativeRangeEval を指定しない全既存経路は二値のまま＝挙動不変。</summary>
    [Fact]
    public void DefaultProblemIsBinaryMode()
    {
        var schedule = Enumerable.Range(0, 4)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, 5).ToList()).ToList();
        var p = new Problem(BuildState(schedule));
        Assert.False(p.QuantitativeRangeEval);
        Assert.False(ScheduleUtil.CachedProblem(BuildState(schedule)).QuantitativeRangeEval);
    }
}
