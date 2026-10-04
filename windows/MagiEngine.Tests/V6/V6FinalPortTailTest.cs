using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7 ピース17] <c>V6FinalPort.Tail.cs</c>（<c>CovUBlockedAmount</c>/<c>CovUStructuralWall</c>/
/// <c>FmtIter</c>/<c>CheckResultWorse</c>）の移植テスト。
///
/// このファイルが直接カバーするのは <see cref="V6FinalPort.CheckResultWorse"/>（Kotlin側
/// <c>SessionRegressionTest.kt</c> の <c>checkResultWorse_lexicographic</c> を逐語移植）のみ。
/// <c>CovUBlockedAmount</c>/<c>CovUStructuralWall</c> の唯一の Kotlin テスト
/// （<c>V6PortAnalyzerTest.kt</c> の <c>residualAnalysisTreatsWishBlockedCovUAsAWallEvenWhenSupplyFloorIsZero</c>）
/// は <c>V6PortAnalyzer.DiagnoseCoverage</c>（フェーズ7 ピース3）の <c>CascadeChainState</c> フィクスチャに
/// 依存するため、そちらと同じファイル（<c>V6PortAnalyzerCoverageTest.cs</c>）の
/// <c>ResidualAnalysisTreatsWishBlockedCovUAsAWallEvenWhenSupplyFloorIsZero</c> へ移植済み
/// （フィクスチャの重複を避けるため。3つの純粋計算のみの assertion もそこへ含めた）。
/// <c>FmtIter</c> は Kotlin 側に直接のユニットテストが無い（診断ログの整形専用ヘルパー）ため、
/// 本ファイルでは対象外のまま。
/// </summary>
public class V6FinalPortTailTest
{
    private static ViolationReport Rep(int hard, int total, double weighted) => new(
        Violations: new Dictionary<string, string>(),
        NeedViolations: new Dictionary<string, string>(),
        CountViolations: new Dictionary<string, string>(),
        Breakdown: new Dictionary<string, int>(),
        Total: total, Hard: hard, Soft: total - hard, WeightedScore: weighted);

    /// <summary>
    /// [3.92.0/3.287.0 keep-best統一の回帰] 判定順は hard→weightedScore→total（<c>betterReport</c> と
    /// 同順）。第2キーが weighted に昇格しているため、weighted改善・total悪化の正当な取引（重い族を
    /// 直し軽い族を差し出す）は「悪化」と判定しない。
    /// </summary>
    [Fact]
    public void CheckResultWorse_Lexicographic()
    {
        var baseRep = Rep(hard: 2, total: 10, weighted: 100.0);

        Assert.Null(V6FinalPort.CheckResultWorse(baseRep, Rep(1, 99, 9999.0)));
        Assert.Null(V6FinalPort.CheckResultWorse(baseRep, Rep(2, 999, 99.0)));
        Assert.Null(V6FinalPort.CheckResultWorse(baseRep, Rep(2, 10, 99.0)));
        Assert.Null(V6FinalPort.CheckResultWorse(baseRep, Rep(2, 9, 100.0)));
        Assert.Null(V6FinalPort.CheckResultWorse(baseRep, Rep(2, 10, 100.0)));
        Assert.Null(V6FinalPort.CheckResultWorse(baseRep, Rep(1, 10, 200.0)));

        Assert.NotNull(V6FinalPort.CheckResultWorse(baseRep, Rep(3, 1, 1.0)));
        Assert.NotNull(V6FinalPort.CheckResultWorse(baseRep, Rep(2, 9, 101.0)));
        Assert.NotNull(V6FinalPort.CheckResultWorse(baseRep, Rep(2, 11, 100.0)));

        Assert.Null(V6FinalPort.CheckResultWorse(null, Rep(9, 99, 999.0)));
    }

    /// <summary>
    /// [3.513.0/バグ修正・Kotlin原本 SessionRegressionTest.kt の sentinelSchedule_fallsBackToCappedInputNotRawInput
    /// を逐語移植] 番兵発火時は cappedInput（個人上限 0 のセルを外した入力）へ戻る。旧実装は
    /// 上限 0 を外す前の生入力（normInput相当）へ戻していたため、finalReport（cappedInput基準）と
    /// finalSched が食い違い得た。
    /// </summary>
    [Fact]
    public void SentinelSchedule_FallsBackToCappedInputNotRawInput()
    {
        var cappedInput = new[] { new[] { 0, 0, 0 } };
        var refSched = new[] { new[] { 1, 1, 1 } };

        Assert.Equal(new[] { 0, 0, 0 }, V6FinalPort.SentinelSchedule("HARDが悪化しました", cappedInput, refSched)[0]);
        Assert.Equal(new[] { 1, 1, 1 }, V6FinalPort.SentinelSchedule(null, cappedInput, refSched)[0]);
    }

    // ---- 最終番兵: 個人上限0（希望でない）のセルを含む段は外す。希望で固定した上限0のセルは外さない ----

    private static MagiState CapZeroState() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-31",
        shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("B4", "B4", "", ""), new("有", "有", "", "") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("美幸", 0) },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "1", "" } },
        schedule: new List<IReadOnlyList<int>> { new int[31] },
        wishes: new Dictionary<string, int> { ["0,3"] = 1 },
        staffRange: new Dictionary<string, MagiEngine.Model.Range> { ["0,1"] = new("", "0") });

    private static int[][] Board(params int[] b4Days)
    {
        var r = new int[31];
        foreach (var d in b4Days) r[d] = 1;
        return new[] { r };
    }

    [Fact]
    public void ExcludeCapZeroStages_DropsNonWishUpperZeroCellAndLogsIt()
    {
        var st = CapZeroState(); var p = ScheduleUtil.CachedProblem(st);
        var input = new V6FinalPort.StageCandidate("入力", Board(3), Rep(0, 0, 0.0));
        var bad = new V6FinalPort.StageCandidate("後処理", Board(3, 5), Rep(0, 0, 0.0));
        Assert.Equal(new[] { "入力" }, V6FinalPort.ExcludeCapZeroStages(p, new[] { input, bad }).Select(c => c.Label));
        var logs = V6FinalPort.CapZeroLogs(st, p, new[] { input, bad });
        Assert.Single(logs);
        Assert.Contains("後処理", logs[0].Message);
        Assert.Contains("美幸/6日目/B4", logs[0].Message);
    }

    [Fact]
    public void ExcludeCapZeroStages_KeepsUpperZeroCellLockedByWish()
    {
        var st = CapZeroState(); var p = ScheduleUtil.CachedProblem(st);
        var input = new V6FinalPort.StageCandidate("入力", Board(), Rep(0, 0, 0.0));
        var ok = new V6FinalPort.StageCandidate("後処理", Board(3), Rep(0, 0, 0.0));
        Assert.Equal(new[] { "入力", "後処理" }, V6FinalPort.ExcludeCapZeroStages(p, new[] { input, ok }).Select(c => c.Label));
        Assert.Empty(V6FinalPort.CapZeroLogs(st, p, new[] { input, ok }));
    }
}
