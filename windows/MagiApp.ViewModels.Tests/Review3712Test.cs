using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// Kotlin <c>Review3712Test</c> の 1 対 1 移植。3.712.0: 診断カードの行は事実と札だけで、定型の説明と内訳は「詳しく」に 1 回。
/// カードの開閉は画面の動きなので CI と実機で確かめる。
/// </summary>
public class Review3712Test
{
    [Fact]
    public void ShortageRowsAreOneFactAndATag()
    {
        Assert.Equal("10/10(土) Cｵ 不足1", CellSheetLogic.ShortageRowLine("10/10(土)", "Cｵ", 1));
        Assert.Equal("担当が足りない", CellSheetLogic.ShortageRowTag(infeasible: true, blockedNow: false));
        Assert.Equal("今の希望では埋まらない", CellSheetLogic.ShortageRowTag(infeasible: false, blockedNow: true));
        Assert.Equal("動かせば埋まる", CellSheetLogic.ShortageRowTag(infeasible: false, blockedNow: false));
        Assert.Equal("休みや余裕のある勤務から移せます", CellSheetLogic.ShortageRowNote(infeasible: false, blockedNow: false));
        Assert.Null(CellSheetLogic.ShortageRowNote(infeasible: true, blockedNow: false));
        Assert.Equal("足りない日3 · 動かせる枠あり", CellSheetLogic.ShortageCardLine(3, true));
        Assert.Equal("足りない日1 · 動かせる枠なし", CellSheetLogic.ShortageCardLine(1, false));
    }

    [Fact]
    public void SurplusAndForbiddenRowsDropTheBreakdown()
    {
        Assert.Equal("移動先はどこも定員一杯", CellSheetLogic.SurplusShortReason("移動先はどこも定員一杯。内訳: Aの3人のうち 動かせる0人・玉突き必要3人・本人の希望0人・禁止の並び0人"));
        Assert.Equal("崩せる", CellSheetLogic.ForbiddenRunTag(true));
        Assert.Equal("崩せない", CellSheetLogic.ForbiddenRunTag(false));
        Assert.Equal("10/9 Dﾃ · 10/10 Aｱ", CellSheetLogic.ForbiddenRunCellsLine(new[] { ("10/9", "Dﾃ"), ("10/10", "Aｱ") }));
        Assert.Equal("福澤俊陽 · 10/1", CellSheetLogic.WishRowHead("福澤俊陽", "10/1"));
    }

    /// <summary>行の文を作る関数は、定型の長文（玉突き・日の証明・内訳）を出さない。</summary>
    [Fact]
    public void RowBuildersNeverCarryTheBoilerplate()
    {
        var rows = new[]
        {
            CellSheetLogic.ShortageRowLine("10/10(土)", "Cｵ", 1), CellSheetLogic.ShortageRowTag(true, false), CellSheetLogic.ShortageRowTag(false, true),
            CellSheetLogic.ShortageRowTag(false, false), CellSheetLogic.ShortageRowNote(false, false) ?? "", CellSheetLogic.ShortageCardLine(2, true),
            CellSheetLogic.ForbiddenRunTag(true), CellSheetLogic.ForbiddenRunTag(false), CellSheetLogic.ForbiddenRunCellsLine(new[] { ("10/9", "Dﾃ") }),
            CellSheetLogic.WishRowHead("福澤", "10/1"), CellSheetLogic.SurplusShortReason("理由。内訳: Aの1人のうち 動かせる1人"),
        };
        foreach (var r in rows)
        {
            Assert.DoesNotContain(V6PortAnalyzer.ForbiddenChainHint, r);
            Assert.DoesNotContain(CellSheetLogic.ForbiddenRetryNote, r);
            Assert.DoesNotContain(V6PortAnalyzer.DayProofReason, r);
            Assert.DoesNotContain("内訳", r);
            Assert.DoesNotContain("玉突き", r);
            Assert.True(r.Length <= 22, r);
        }
    }
}
