using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, ピース26] <see cref="V6HotfixPasses.ApplyHF80StrategicOscillation"/> の検証。
///
/// 移植元Kotlinテスト無し（<c>grep -rln "applyHF80StrategicOscillation\|HF80Result\|StrategicOscillation"</c>
/// および広域の "hf80" 大小文字無視検索、ファイル名検索がいずれも0件）。この関数は乱択（強摂動→局所改善）
/// なので、他ピース同様「keep-best（退化しない）」という普遍的な不変条件と、<c>maxCycles=0</c>／
/// <c>shouldStop</c>即時真という決定的な自明ケースを固定する。
/// </summary>
public class V6HotfixPassesHF80Test
{
    // 単一職員s0のA過多(下限0上限1に対し6)・B不足(下限1上限1に対し0)。keep-best不変条件の
    // 決定的でない(乱択)テストで、実際に改善余地がある盤面として使う。
    private static MagiState St() => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-08",
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1, 1, 1, 0 } },
        staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new Range("0", "1"),
            ["0,2"] = new Range("1", "1"),
        });

    [Fact]
    public void MaxCyclesZeroIsANoOpAndEchoesTheNormalizedInput()
    {
        var s = MinimalState.Build();
        var sched = s.Schedule.ToIntArray2D();
        var r = V6HotfixPasses.ApplyHF80StrategicOscillation(s, sched, maxCycles: 0);
        Assert.Equal(0, r.Cycles);
        Assert.False(r.Applied);
        Assert.Equal("no improving oscillation", r.Reason);
        var norm = ScheduleUtil.NormalizeSchedule(sched, new Problem(s));
        for (var i = 0; i < r.NewSchedule.Length; i++)
        {
            Assert.Equal(norm[i], r.NewSchedule[i]);
        }
    }

    [Fact]
    public void ShouldStopTrueFromTheStartReturnsInputUnchangedWithoutOscillating()
    {
        var s = St();
        var sched = s.Schedule.ToIntArray2D();
        var r = V6HotfixPasses.ApplyHF80StrategicOscillation(s, sched, maxCycles: 3, seed: 1L, shouldStop: () => true);
        Assert.Equal(0, r.Cycles); // ループ先頭のshouldStop確認で1サイクルも回らない
        Assert.False(r.Applied);
        var norm = ScheduleUtil.NormalizeSchedule(sched, new Problem(s));
        for (var i = 0; i < r.NewSchedule.Length; i++)
        {
            Assert.Equal(norm[i], r.NewSchedule[i]);
        }
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(42L)]
    [InlineData(999L)]
    [InlineData(123456789L)]
    public void NeverProducesAResultWorseThanTheInput(long seed)
    {
        var s = St();
        var sched = s.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(s, sched);
        var r = V6HotfixPasses.ApplyHF80StrategicOscillation(s, sched, maxCycles: 3, seed: seed);
        var after = UnifiedViolationChecker.Check(s, r.NewSchedule);
        // keep-best: どのサイクルも isBetter を満たさなければ直前のbestを持ち越す＝
        // 最終盤面が入力より真に悪化することは無い(bestReportがbeforeより劣後することは無い)。
        Assert.False(UnifiedViolationChecker.BetterReport(before, after));
        Assert.Equal(before.Hard, r.BeforeHard);
        Assert.Equal(before.WeightedScore, r.BeforeScore);
        Assert.Equal(after.Hard, r.AfterHard);
        Assert.Equal(after.WeightedScore, r.AfterScore);
        Assert.InRange(r.Cycles, 0, 3);
        // Applied と Reason は1対1で対応する。
        Assert.Equal(r.Applied ? "strategic oscillation accepted" : "no improving oscillation", r.Reason);
        // Applied==true なら、その時点のbestは入力より真に改善している。
        if (r.Applied) Assert.True(UnifiedViolationChecker.BetterReport(after, before));
    }
}
