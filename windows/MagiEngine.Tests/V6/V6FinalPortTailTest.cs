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
}
