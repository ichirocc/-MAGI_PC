using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.645.0/仕様 5.3] 相談してから決める判断（Kotlin <c>ConsultListTest</c> と 1 対 1）。</summary>
public class ConsultListTest
{
    [Fact]
    public void LineOnlyWhenSomethingIsPending()
    {
        Assert.Null(ConsultList.Line(0));
        Assert.Equal("未確認事項 2 件（相談中。下の一覧で確認してから配ってください）", ConsultList.Line(2));
    }

    [Fact]
    public void BuildersCarryTheTargetAndTheQuestion()
    {
        Assert.Equal(new ConsultItem("甲 10/12 の希望「夜」", "取り消すか勤務を変えるか: 禁止の並びに当たる", 0, 11), ConsultList.Wish("甲", "10/12", "夜", "禁止の並びに当たる", 0, 11));
        Assert.Equal("甲 10/12 の希望", ConsultList.Wish("甲", "10/12", null, "r", 0, 11).Subject);
        Assert.Equal("だれかを入れる: 甲・乙・丙・丁・戊 ほか1人", ConsultList.Shortage("10/12", "夜", new[] { "甲", "乙", "丙", "丁", "戊", "己" }).Note);
        Assert.Equal("入れる人を相談", ConsultList.Shortage("10/12", "夜", Array.Empty<string>()).Note);
        var row = new PreRunRow("「夜」 3日で担当できる人より必要人数が多く、人員不足が合計3人残ります", Landing: new EditLanding(2, "yr_ws1"));
        Assert.Equal(row.Text, ConsultList.PreRun(row).Subject);
        Assert.Null(ConsultList.PreRun(row).Staff);
    }

    [Fact]
    public void SameSubjectAndNoteIsNotAddedTwice()
    {
        var a = new ConsultItem("x", "y");
        var l = ConsultList.Add(Array.Empty<ConsultItem>(), a)!;
        Assert.Equal(new[] { a }, l);
        Assert.Null(ConsultList.Add(l, new ConsultItem("x", "y")));
        Assert.Equal(2, ConsultList.Add(l, new ConsultItem("x", "z"))!.Count);
    }

    [Fact]
    public void ChainAndFixConsultsSummarizeTheChange()
    {
        var s = new FixSuggestion(FixKind.Chain, new[] { new FixCell(0, 2, 2) }, "（玉突き）10/3 の「夜」を複数人の入替で埋める", -1, 0, new (string, int)[] { ("covU", -1) });
        var p = ChainFixPreview.Of(s, new[] { new[] { 0, 0, 1 } }, new[] { "甲" }, new[] { "休", "日", "夜" }, "2026-10-01");
        var (hard, caution) = NextActionGuide.FixImpactLines(s, f => f);
        Assert.Equal(new ConsultItem(s.Label, "甲 10/3 日 → 夜（必須違反: 1件減る）"), ConsultList.Chain(p, hard));
        Assert.Equal(new ConsultItem(s.Label, "必須違反: 1件減る"), ConsultList.Fix(s, hard, caution));
    }

    [Fact]
    public void ViewModelAddsOnceAndRemovesByIndex()
    {
        var vm = new MagiViewModel();
        vm.AddConsult(new ConsultItem("x", "y"));
        Assert.Equal(ConsultList.Added, vm.Ui.Message);
        vm.AddConsult(new ConsultItem("x", "y"));
        Assert.Equal(ConsultList.Duplicate, vm.Ui.Message);
        Assert.Single(vm.Ui.Consults);
        vm.RemoveConsult(5);
        Assert.Single(vm.Ui.Consults);
        vm.RemoveConsult(0);
        Assert.Empty(vm.Ui.Consults);
    }
}
