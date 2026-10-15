using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3683Test</c> の 1 対 1 移植。3.683.0 の UX 監査（文言と表示の純関数）。</summary>
public class Review3683Test
{
    [Fact]
    public void wishDayButtonNamesTheDay()
    {
        Assert.Equal("11/3 の希望を見る", CellSheetLogic.WishDayButton("11/3"));
        Assert.Equal("希望を見る", CellSheetLogic.WishDayButton(null));
    }

    [Fact]
    public void countSectionOnlyWithCountFamilies()
    {
        Assert.True(CellSheetLogic.ShowCountSection(new[] { "0,1" }, 0, new[] { "A 3回" }));
        Assert.False(CellSheetLogic.ShowCountSection(new[] { "1,1" }, 0, new[] { "A 3回" }));
        Assert.False(CellSheetLogic.ShowCountSection(new[] { "0,1" }, 0, Array.Empty<string>()));
    }

    [Fact]
    public void compactNoFixLines()
    {
        Assert.Equal("この場所を直す案は見つかりませんでした。理由A", CellSheetLogic.CompactNoFixLine(new[] { "理由A", "理由B" }, null, null, null));
        Assert.Equal("この場所を直す案は見つかりませんでした", CellSheetLogic.CompactNoFixLine(Array.Empty<string>(), null, null, null));
        Assert.Equal("このセルの禁止の並びを解消する案は見つかりませんでした。前後の日（11/2・11/4）を開いて確かめられます",
            CellSheetLogic.CompactNoFixLine(new[] { "理由A" }, "禁止の並び", "11/2", "11/4"));
    }

    [Fact]
    public void staffSheetPreselectsSingleRow()
    {
        Assert.Equal(2, CellSheetLogic.StaffSheetPreselect(new[] { 2, -1 }));
        Assert.Null(CellSheetLogic.StaffSheetPreselect(new[] { 1, 2 }));
    }

    [Fact]
    public void creationTopUsesStaffName()
    {
        Assert.Equal("福澤さん 禁止の並び", CellSheetLogic.CreationNextProblem("福澤", "禁止の並び"));
        Assert.Equal("福澤 11/3 禁止の並び", CellSheetLogic.CreationNextProblem("福澤", "福澤 11/3 禁止の並び"));
        Assert.Equal(new[] { CellSheetLogic.CreationDoneTitle, "次に確認する問題：X" }, CellSheetLogic.CreationTopLines("X"));
        Assert.Equal(KeptResultText.KeptTitle, Assert.Single(CellSheetLogic.CreationTopLines(null, kept: true)));
    }

    [Fact]
    public void changeSummaryUsesUnitsWithoutWeights()
    {
        var s = new ChangeSummary(12, 84, 42, 42, true, new Dictionary<string, int> { ["c1"] = -2, ["weekly"] = 3, ["covU"] = -1 });
        Assert.Equal("変更：12人・84か所／希望どおり 42/42件", s.Line());
        Assert.Equal("減った：covU 1人・c1 2件／増えた：weekly 3pt", s.FamilyLine(unit: AnalysisTriage.UnitOf));
    }

    [Fact]
    public void preRunRepeatHint()
    {
        Assert.Equal("前回と同じ組む時間・方式で再作成します。前回いちばん多い必須違反は禁止の並び（3件）です。同じ設定では同じ違反が残りやすいため、編集タブでこれを 1 つ緩めると減る見込みがあります。",
            CellSheetLogic.PreRunRepeatHintText("禁止の並び（3件）"));
    }

    [Fact]
    public void alternativesCompareWithNow()
    {
        Assert.Equal("案1：必須違反 2件（今より多い）", CellSheetLogic.AlternativeSummary(1, "2件", 1, 2, true, false));
        Assert.Equal("案2：必須違反 なし（今より少ない）", CellSheetLogic.AlternativeSummary(2, "なし", 1, 0, false, true));
        Assert.True(CellSheetLogic.AlternativesAllWorse(new[] { true, true }, 2));
        Assert.False(CellSheetLogic.AlternativesAllWorse(new[] { true }, 2));
    }

    [Fact]
    public void notificationHasNoPt()
    {
        Assert.Equal(new[] { "必須違反はありません", "要調整 2件。配布前の確認で中身を確かめてください" },
            CompletedText.NotifyDoneLines(0, new Dictionary<string, int> { ["c1"] = 2, ["weekly"] = 9 }));
    }

    [Fact]
    public void wordingConstants()
    {
        Assert.Equal("条件ごとの状況（守れている 10／残っている 3）", CellSheetLogic.ConditionStatusTitle(10, 3));
        Assert.Equal("担当外の希望 4件をまとめて外す（勤務表は変わりません・元に戻せます）", CellSheetLogic.BulkClearWishesLabel(4));
        Assert.Equal("変更案を見る", CellSheetLogic.ViewFixesButton);
    }
}
