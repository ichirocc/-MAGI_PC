using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>AltTradeTest</c> の 1 対 1 移植（docs/seesaw.md §6.4 の S4）。</summary>
public class AltTradeTest
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["c1"] = "期間の制約", ["high"] = "上限超過", ["weekly"] = "曜日の偏り", ["c3"] = "守るとよい並び", ["c3m"] = "推奨の並び", ["low"] = "下限割れ",
    };
    private static string L(string f) => Labels.TryGetValue(f, out var v) ? v : f;
    private static readonly Dictionary<string, int> Cur = new() { ["c1"] = 5, ["high"] = 2, ["weekly"] = 10 };
    private static Dictionary<string, int> With(params (string F, int V)[] xs) { var d = new Dictionary<string, int>(Cur); foreach (var (f, v) in xs) d[f] = v; return d; }

    [Fact]
    public void kinds()
    {
        Assert.Equal(CellSheetLogic.AltTrade.TRADE, CellSheetLogic.AltTradeOf(Cur, With(("c1", 3), ("high", 3))));
        Assert.Equal(CellSheetLogic.AltTrade.DOMINATES, CellSheetLogic.AltTradeOf(Cur, With(("weekly", 8))));
        Assert.Equal(CellSheetLogic.AltTrade.DOMINATED, CellSheetLogic.AltTradeOf(Cur, With(("high", 4))));
        Assert.Equal(CellSheetLogic.AltTrade.SAME, CellSheetLogic.AltTradeOf(Cur, Cur));
    }

    [Fact]
    public void tradeLineSaysWhatImprovesFirstInUnits()
    {
        Assert.Equal("期間の制約が 2件減り、上限超過が 1回増えます", CellSheetLogic.AltTradeLine(Cur, With(("c1", 3), ("high", 3)), L));
        Assert.Equal("どの項目も今と同じか少なく、曜日の偏りが 2pt減ります", CellSheetLogic.AltTradeLine(Cur, With(("weekly", 8)), L));
        Assert.Equal("どの項目も今と同じか多い案です", CellSheetLogic.AltTradeLine(Cur, With(("high", 4)), L));
        Assert.Null(CellSheetLogic.AltTradeLine(Cur, Cur, L));
    }

    [Fact]
    public void manyFamiliesAreCappedAtTwo()
    {
        var alt = new Dictionary<string, int> { ["c1"] = 2, ["high"] = 1, ["weekly"] = 9, ["c3"] = 1, ["c3m"] = 1, ["low"] = 1 };
        Assert.Equal("期間の制約が 3件・上限超過が 1回（ほか 1 項目）減り、守るとよい並びが 1件・推奨の並びが 1件（ほか 1 項目）増えます", CellSheetLogic.AltTradeLine(Cur, alt, L));
    }

    [Fact]
    public void summaryKeepsOldTextWithoutTrade()
    {
        Assert.Equal("案1：必須違反 1件（今と同じ）", CellSheetLogic.AlternativeSummary(1, "1件", 1, 1, worse: false, better: false));
        Assert.Equal("案1：必須違反 1件（今と同じ）。期間の制約が 2件減り、上限超過が 1回増えます",
            CellSheetLogic.AlternativeSummary(1, "1件", 1, 1, worse: false, better: false, trade: "期間の制約が 2件減り、上限超過が 1回増えます"));
    }
}
