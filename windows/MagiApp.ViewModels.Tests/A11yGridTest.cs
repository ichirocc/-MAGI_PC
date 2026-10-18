namespace MagiApp.ViewModels.Tests;

/// <summary>視覚弱点者対応 第2便（C# 側のみ）: セルの読み上げ文・矢印キーの移動・ハイコントラストの枠。</summary>
public class A11yGridTest
{
    [Fact]
    public void CellDescription_plainCell()
    {
        Assert.Equal("山田 10/1(水) 日、タップで変更", A11y.CellDescription("山田", "10/1(水)", "日", 0, false, 0, "", false, null, false));
        Assert.Equal("山田 10/1(水) なし、タップで変更", A11y.CellDescription("山田", "10/1(水)", " ", 0, false, 0, "", false, null, false));
    }

    [Fact]
    public void CellDescription_allMarksInKotlinOrder()
    {
        Assert.Equal("佐藤 10/2(木) 夜・必須・期間の制約の窓・希望未反映（希望=休）・手動固定・拡張希望の違反、タップで変更",
            A11y.CellDescription("佐藤", "10/2(木)", "夜", 1, true, 2, "休", true, new[] { "夜" }, true));
        Assert.Equal("佐藤 10/2(木) 日・要調整・希望・拡張希望（夜・準以外）、タップで変更",
            A11y.CellDescription("佐藤", "10/2(木)", "日", 3, false, 1, "", false, new[] { "夜", "準" }, false));
        Assert.Contains("希望未反映（希望=?）", A11y.CellDescription("a", "d", "日", 0, false, 2, "", false, null, false));
    }

    [Fact]
    public void MoveCell_clampsAtEdges_andStartsAtOrigin()
    {
        Assert.Equal((0, 0), A11y.MoveCell(null, 0, 1, 3, 5));
        Assert.Equal((1, 2), A11y.MoveCell((1, 1), 0, 1, 3, 5));
        Assert.Equal((0, 0), A11y.MoveCell((0, 0), -1, -1, 3, 5));
        Assert.Equal((2, 4), A11y.MoveCell((2, 4), 1, 1, 3, 5));
    }

    [Fact]
    public void CellBorderOf_priority_andHighContrastAlwaysOutlines()
    {
        Assert.Equal(A11y.CellBorder.Focus, A11y.CellBorderOf(true, true, true, true));
        Assert.Equal(A11y.CellBorder.Violation, A11y.CellBorderOf(false, true, false, true));
        Assert.Equal(A11y.CellBorder.Outline, A11y.CellBorderOf(false, false, false, true));
        Assert.Equal(A11y.CellBorder.Outline, A11y.CellBorderOf(false, false, true, false));
        Assert.Equal(A11y.CellBorder.None, A11y.CellBorderOf(false, false, false, false));
    }
}
