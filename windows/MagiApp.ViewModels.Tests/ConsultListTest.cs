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
        Assert.Equal(new ConsultItem("甲 10/12 の希望「夜」", "取り消すか勤務を変えるか: 禁止の並びに当たる", 0, 11, StaffName: "甲"), ConsultList.Wish("甲", "10/12", "夜", "禁止の並びに当たる", 0, 11));
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
        Assert.True(ConsultList.IsConsulted(l, new ConsultItem("x", "y")));
        Assert.False(ConsultList.IsConsulted(l, new ConsultItem("x", "z")));
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

    /// <summary>[3.646.0 B01/B02] 対象は氏名と実日付で引き直す＝職員の並び替え・削除、月の移動のあとに別のセルを開かない。</summary>
    [Fact]
    public void TargetIsResolvedByNameAndDate()
    {
        var c = ConsultList.Wish("乙", "10/12", "夜", "r", 1, 11, "2026-10-12");
        Assert.Equal((1, 11), ConsultList.ConsultCell(c, "2026-10-01", new[] { "甲", "乙" }, 31));
        Assert.Equal((0, 11), ConsultList.ConsultCell(c, "2026-10-01", new[] { "乙", "甲" }, 31));          // 並び替え: 氏名で引き直す
        Assert.Null(ConsultList.ConsultCell(c, "2026-10-01", new[] { "甲" }, 31));                            // 削除: 開けない
        Assert.Null(ConsultList.ConsultCell(c, "2026-11-01", new[] { "甲", "乙" }, 30));                      // 月の移動: 日付が期間の外
        // 同名は位置が一致すればそれ、違えば先頭（Kotlin の実装と同じ。Kotlin のテストは「0 to 11」と書くが実装は位置 1 を返す）。
        Assert.Equal((1, 11), ConsultList.ConsultCell(c, "2026-10-01", new[] { "乙", "乙" }, 31));
        Assert.Equal((0, 11), ConsultList.ConsultCell(c, "2026-10-01", new[] { "乙", "甲", "乙" }, 31));
        Assert.Equal("いまの職員一覧にいません（乙）", ConsultList.ConsultTargetNote(c, "2026-10-01", new[] { "甲" }, Array.Empty<string>(), 31));
        Assert.Equal("いまの期間にない日です（10/12）", ConsultList.ConsultTargetNote(c, "2026-11-01", new[] { "甲", "乙" }, Array.Empty<string>(), 30));
        Assert.Null(ConsultList.ConsultTargetNote(c, "2026-10-01", new[] { "甲", "乙" }, Array.Empty<string>(), 31));
        Assert.Null(ConsultList.ConsultTargetNote(ConsultList.Shortage("10/12", "夜", Array.Empty<string>()), "2026-10-01", new[] { "甲" }, Array.Empty<string>(), 31));   // 対象を持たない相談は何も言わない
    }

    [Fact]
    public void LegacyItemsWithoutNameOrDateFallBackToTheirIndexes()
    {
        var c = new ConsultItem("x", "y", 1, 5);
        Assert.Equal((1, 5), ConsultList.ConsultCell(c, "2026-10-01", new[] { "甲", "乙" }, 31));
        Assert.Null(ConsultList.ConsultCell(c, "2026-10-01", new[] { "甲" }, 31));
        Assert.Null(ConsultList.ConsultCell(c, "2026-10-01", new[] { "甲", "乙" }, 5));
    }

    /// <summary>[3.646.0 L02] 入れ替えの相談は枠（日付・記号）か案そのものを持ち、今の勤務表で見直せる。</summary>
    [Fact]
    public void ChainTargetIsResolvedByDateAndSymbol()
    {
        var t = new ChainTarget("2026-10-03", "夜", "lbl");
        Assert.Equal((2, 2), ConsultList.ConsultChainTarget(t, "2026-10-01", new[] { "休", "日", "夜" }, 31));
        Assert.Equal((2, 0), ConsultList.ConsultChainTarget(t, "2026-10-01", new[] { "夜", "日", "休" }, 31));   // シフトの並び替え: 記号で引き直す
        Assert.Null(ConsultList.ConsultChainTarget(t, "2026-11-01", new[] { "休", "日", "夜" }, 30));
        Assert.Null(ConsultList.ConsultChainTarget(t, "2026-10-01", new[] { "休", "日" }, 31));
        var s = new FixSuggestion(FixKind.Chain, new[] { new FixCell(0, 2, 2) }, "lbl", -1, 0, Array.Empty<(string, int)>());
        Assert.True(ConsultList.ConsultChainResumable(new ChainTarget(null, null, "lbl", Suggestion: s), "2026-10-01", new[] { "休" }, 31));
        Assert.False(ConsultList.ConsultChainResumable(t, "2026-11-01", new[] { "休", "日", "夜" }, 30));
        var c = new ConsultItem("lbl", "n", Chain: t);
        Assert.Equal("いまの期間・シフトにない枠です（10/3 の「夜」）", ConsultList.ConsultTargetNote(c, "2026-11-01", new[] { "甲" }, new[] { "休", "日", "夜" }, 30));
        Assert.Null(ConsultList.ConsultTargetNote(c, "2026-10-01", new[] { "甲" }, new[] { "休", "日", "夜" }, 31));
        Assert.Equal("2026-10-12", ConsultList.IsoDate("2026-10-01", 11));
        Assert.Equal(11, ConsultList.DayIndexOf("2026-10-01", "2026-10-12", 31));
        Assert.Null(ConsultList.DayIndexOf("2026-10-01", "2026-11-12", 31));
        Assert.Null(ConsultList.IsoDate("", 3));
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
