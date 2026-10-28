using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// SOFT の桁あふれ（2026-10-24 の外部レビュー）: 個人の下限 1 億で SOFT が 10 億を超えると、差分評価が必須の数を取り違えていた
/// （hard=43 を返した）。実行前の検証が断り、差分評価もフル評価と同じく黙って誤った値を返さない。
/// </summary>
public class SoftOverflowGuardTest
{
    private static MagiState State(string lo, string c2 = "") => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-31",
        shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "1", "1") },
        groups: new List<Group> { new("G", "G") }, staffList: new List<Staff> { new("s0", 0), new("s1", 0) }, use2Patterns: true,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } }, groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        schedule: Enumerable.Range(0, 2).Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, 31).ToList()).ToList(),
        staffRange: new Dictionary<string, Range> { ["0,1"] = new(lo, "") },
        cons2: c2.Length == 0 ? new List<C2Row>() : new List<C2Row> { new("A", c2) });

    [Fact]
    public void RunValidationRefusesCountSettingsThatOverflowTheSoftDigit()
    {
        Assert.Null(Ws1Ops.CountSettingError(State("31")));
        Assert.Null(Ws1Ops.CountSettingError(State($"{Ws1Ops.COUNT_SETTING_MAX}")));
        Assert.NotNull(Ws1Ops.CountSettingError(State("100000000")));
        Assert.NotNull(Ws1Ops.CountSettingError(State("", c2: "100000000")));
    }

    [Fact]
    public void DeltaEvaluatorDoesNotSilentlyMisreportHard()
    {
        var st = State("100000000");
        var d = new DeltaEvaluator(new Problem(st));
        long? got = null;
        var ex = Record.Exception(() => { d.Reset(st.Schedule.ToIntArray2D()); got = d.Score(); });
        Assert.True(ex is not null, $"差分評価が桁あふれの値を返した: {got}");
    }

    [Fact]
    public void TheLimitItselfStaysWithinTheDigitForTheLargestShop()
    {
        // 30 名×12 シフトが全部上限の下限で 0 回＝最大の下限割れでも桁に収まる（上限値の根拠）。
        var worst = 30L * 12 * Ws1Ops.COUNT_SETTING_MAX * (long)MirrorKeys.WeightOf("low");
        Assert.True(worst < Evaluator.SCORE_HARD_UNIT, $"上限 {Ws1Ops.COUNT_SETTING_MAX} の最悪の SOFT {worst} が桁を超える");
    }
}
