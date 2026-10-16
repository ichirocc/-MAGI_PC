using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3682Test</c> の 1 対 1 移植。3.682.0: 残りの UX 監査（足りない側のセル・確認の一文・反映の帯・回数の案・ホームと分析の見出し・画面の言葉）。</summary>
public class Review3682Test
{
    private static readonly MagiState St = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));
    private static readonly int[][] S = St.Schedule.ToIntArray2D();
    private static readonly List<string> Names = St.StaffList.Select(x => x.Name).ToList();
    private static readonly List<string> Syms = St.Shifts.Select(x => x.Kigou).ToList();
    private static string Label(string f) => f switch { "c3n" => "禁止の並び", "low" => "下限割れ", "c1" => "期間の制約", _ => f };

    [Fact]
    public void ShortSideCellIsNotTheCellsProblem()
    {
        Assert.Equal(new HashSet<string> { "covU", "low" }, CellSheetLogic.ShortSideFamilies(new[] { "vio-covU" }, new[] { "vio-low" }));
        Assert.Equal(new HashSet<string> { "apt" }, CellSheetLogic.ShortSideFamilies(new[] { "vio-covO" }, new[] { "vio-aptLow" }));
        Assert.Empty(CellSheetLogic.ShortSideFamilies(new[] { "vio-covO" }, new[] { "vio-high", "vio-aptHigh" }));
        Assert.Equal("福澤さんのA4は必要な勤務です（変えると 10/8 の A4 の不足が 2人に増えます）",
            CellSheetLogic.ShortSideCaution("福澤", "A4", "10/8", 2, 7, "下限", 8));
        Assert.Equal("福澤さんの休は必要な勤務です（変えると 休 が 7回になり、下限 8回をさらに下回ります）",
            CellSheetLogic.ShortSideCaution("福澤", "休", "10/8", null, 7, "下限", 8));
        Assert.Equal("10/8 の A4 に入れる人を探す", CellSheetLogic.ShortSideSearchButton("10/8", "A4"));
    }

    [Fact]
    public void ReviewVerdictFollowsTheChecker()
    {
        var rep = UnifiedViolationChecker.Check(St, S);
        var key = rep.CellFamilies!.First(e => e.Value.Contains("vio-c3n")).Key;
        var ij = key.Split(',').Select(int.Parse).ToArray();
        var (i, j) = (ij[0], ij[1]);
        for (var k = 0; k < St.ShiftCount; k++)
        {
            if (k == S[i][j]) continue;
            var r = CellSheetLogic.CellChangeReview(St, S, i, j, k, Names, Syms, null, null, false, Label);
            var trial = S.Select(row => row.ToArray()).ToArray();
            trial[i][j] = k;
            var after = UnifiedViolationChecker.Check(St, trial);
            if (after.Hard > rep.Hard) { Assert.True(r.VerdictBad); Assert.StartsWith("この変更で必須違反が増えます（", r.Verdict!); }
            else if (!(after.CellFamilies!.TryGetValue(key, out var f) && f.Contains("vio-c3n"))) { Assert.False(r.VerdictBad); Assert.Contains("禁止の並び", r.Verdict!); }
        }
        // 希望も手動固定も無いセルは、その節を出さない。
        var (a, b) = Enumerable.Range(0, St.StaffCount).SelectMany(x => Enumerable.Range(0, St.DayCount).Select(d => (x, d))).First(p => !St.Wishes.ContainsKey($"{p.x},{p.d}"));
        Assert.Empty(CellSheetLogic.CellChangeReview(St, S, a, b, (S[a][b] + 1) % St.ShiftCount, Names, Syms, null, null, false, Label).WishPin);
        Assert.Equal("新しく生じる・悪くなる問題", CellSheetLogic.ReviewAppearedHeading);
        Assert.Equal("今は変更しない", CellSheetLogic.KeepUnchangedButton);
        Assert.Equal("希望を外す（元に戻せます）", CellSheetLogic.RemoveWishButton);
    }

    [Fact]
    public void KeyGapMeasuresHeadcountAndCounts()
    {
        var board = new[] { new[] { 1, 0 }, new[] { 0, 0 } };
        (int Lo, int Hi)? Need(int k, int _) => k == 1 ? (2, 2) : null;
        (int? Lo, int? Hi, int? Apt) Lim(int _, int k) => k == 0 ? (3, 4, null) : (null, null, null);
        Assert.Equal(("不足", "人", 1), CellSheetLogic.ViolationKeyGap(new ViolationKey("covU", ViolationKey.Kind.Need, 1, 0), board, Lim, Need));
        Assert.Equal(("不足", "回", 2), CellSheetLogic.ViolationKeyGap(new ViolationKey("low", ViolationKey.Kind.Count, 0, 0), board, Lim, Need));
        Assert.Null(CellSheetLogic.ViolationKeyGap(new ViolationKey("c3n", ViolationKey.Kind.Cell, 0, 0), board, Lim, Need));
    }

    [Fact]
    public void CountTargetCardsNameOtherStaffAndMoves()
    {
        var sched = new IReadOnlyList<int>[] { new[] { 0, 0, 1 }, new[] { 1, 0, 0 } };
        var ops = new[] { new FixCell(0, 2, 0), new FixCell(1, 1, 1) };
        (int? Lo, int? Hi, int? Apt) Lim(int _, int k) => k == 0 ? (2, null, null) : (null, null, null);
        Assert.Equal("（下限 8回を下回ります）", CellSheetLogic.CountCrossNote(8, 7, 8, null));
        Assert.Equal("（上限 4回を超えます）", CellSheetLogic.CountCrossNote(4, 5, null, 4));
        Assert.Equal("", CellSheetLogic.CountCrossNote(9, 8, 8, null));
        Assert.Equal(new[] { "新たに生じる：下限割れ（ノアさんの休）" },
            CellSheetLogic.NewCountBreakLines(sched, ops, 0, Lim, new[] { "モニカ", "ノア" }, new[] { "休", "A" }, Label));
        Assert.Contains("ノアさんの「休」：2回 → 1回（下限 2回を下回ります）", CellSheetLogic.StaffCountNotes(sched, ops, new[] { "モニカ", "ノア" }, new[] { "休", "A" }, Lim, 0));
        var t = new FixTarget(0, 0, null, 3, true, "下限");
        Assert.True(CellSheetLogic.CountMovesOnly(new FixSuggestion(FixKind.Chain, ops, "x", 0, 0, new[] { ("low", 0) }), t));
        Assert.False(CellSheetLogic.CountMovesOnly(new FixSuggestion(FixKind.Chain, ops, "x", 0, 0, new[] { ("low", -1) }), t));
        Assert.False(CellSheetLogic.CountMovesOnly(new FixSuggestion(FixKind.Chain, ops, "x", 0, 0, new[] { ("low", 0) }), new FixTarget(null, 0, 1, 2, true, "必要人数")));
    }

    [Fact]
    public void HomeAndAnalysisWording()
    {
        Assert.Equal("必須違反 3件 を減らす手があります", CellSheetLogic.HomeFixHeadline("3件", Array.Empty<string>()));
        Assert.Equal("必須違反 3件 のうち 1件（禁止の並び：福澤さん 10/3）を減らす手があります", CellSheetLogic.HomeFixHeadline("3件", new[] { "禁止の並び：福澤さん 10/3" }));
        Assert.Contains("（a、b ほか）", CellSheetLogic.HomeFixHeadline("3件", new[] { "a", "b", "c" }));
        var rep = UnifiedViolationChecker.Check(St, S);
        var key = rep.CellFamilies!.First(e => e.Value.Contains("vio-c3n")).Key;
        var ij = key.Split(',').Select(int.Parse).ToArray();
        var (i, j) = (ij[0], ij[1]);
        var labels = Enumerable.Range(0, St.ShiftCount).Where(x => x != S[i][j])
            .SelectMany(x => CellSheetLogic.ResolvedHardLabels(St, S, new[] { new FixCell(i, j, x) }, Names, Syms, Label)).ToList();
        Assert.All(labels, l => Assert.Contains("：", l));
        var hardFix = new FixSuggestion(FixKind.Chain, Array.Empty<FixCell>(), "x", -1, -1, new[] { ("c3n", -1), ("c1", 1) });
        Assert.Equal("必須違反の変化：禁止の並び −1件", FixOutcomeText.FixBenefitLine(hardFix, Label));
        Assert.Equal("要調整の変化：期間の制約 −1件", FixOutcomeText.FixBenefitLine(new FixSuggestion(FixKind.Chain, Array.Empty<FixCell>(), "x", 0, -1, new[] { ("c1", -1) }), Label));
        Assert.Null(FixOutcomeText.FixBenefitLine(new FixSuggestion(FixKind.Chain, Array.Empty<FixCell>(), "x", 0, 0, Array.Empty<(string, int)>()), Label));
    }

    [Fact]
    public void UserFacingLabelsAndMessages()
    {
        Assert.Equal("3人の勤務を順に入れ替える（5か所）", FixOutcomeText.FixDisplayLabel("（玉突き連鎖）3人・5マスを順に入れ替え"));
        Assert.Equal("10/8 の 4人の勤務を組み直す", FixOutcomeText.FixDisplayLabel("（再最適化）10/8 の4名を最適割当"));
        Assert.Equal("福澤さんの A4 を 2回増やす", FixOutcomeText.FixDisplayLabel("（連鎖）福澤 の「A4」不足を2コマ補充"));
        Assert.Equal("入替", FixOutcomeText.FixDisplayLabel("入替"));
        Assert.Equal("自動で整えました。要調整 12件 → 9件（必須違反はなしのまま）", FixOutcomeText.PolishResultText(true, "12件", "9件", 0, "なし"));
        Assert.Equal("自動で整えましたが、要調整は減りませんでした（勤務表は変えていません）", FixOutcomeText.PolishResultText(false, "12件", "12件", 0, "なし"));
        Assert.Equal("休 8回 → 7回（下限 8回を割ります）", FixOutcomeText.GuidedCandidateImpact("休", 8, 8));
        Assert.Equal("休 9回 → 8回", FixOutcomeText.GuidedCandidateImpact("休", 9, 8));
        Assert.Null(FixOutcomeText.GuidedCandidateImpact(null, 0, null));
        Assert.Equal("ほかの必須違反（1件）が残っています", FixOutcomeText.OtherHardAfterShortage(new Dictionary<string, int> { ["covU"] = 2, ["c3n"] = 1 }));
        Assert.Null(FixOutcomeText.OtherHardAfterShortage(new Dictionary<string, int> { ["covU"] = 2 }));
    }
}
