using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>CellChangeReviewTest</c> の 1 対 1 移植。セルを 1 つ変える前の確認（2026-10-14 利用者レビュー
/// 「問題を確認 → 変更方法を選ぶ → 影響を確認して適用」）を実データで固定する。</summary>
public class CellChangeReviewTest
{
    private static readonly MagiState St = StateJsonSerializer.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oct2026_grid_state.json")));
    private static readonly int[][] S = St.Schedule.ToIntArray2D();
    private static readonly ViolationReport Rep = UnifiedViolationChecker.Check(St, S);
    private static string Label(string f) => f == "c3n" ? "禁止の並び" : f;

    [Fact]
    public void ReviewIsTheCheckersBeforeAndAfter()
    {
        // 禁止の並びのあるセルを 1 つ選び、別の勤務にしたときの確認を作る。
        var key = Rep.CellFamilies!.First(e => e.Value.Contains("vio-c3n")).Key;
        var ij = key.Split(',').Select(int.Parse).ToArray();
        var (i, j) = (ij[0], ij[1]);
        var k = Enumerable.Range(0, St.ShiftCount).First(x => x != S[i][j]);
        var names = St.StaffList.Select(x => x.Name).ToList(); var syms = St.Shifts.Select(x => x.Kigou).ToList();
        var r = CellSheetLogic.CellChangeReview(St, S, i, j, k, names, syms, null, null, pinned: false, Label);
        var trial = S.Select(row => row.ToArray()).ToArray();
        trial[i][j] = k;
        var after = UnifiedViolationChecker.Check(St, trial);
        Assert.Equal($"必須違反（勤務表全体）：{Rep.Hard}件 → {after.Hard}件", r.HardLine);
        Assert.Equal($"{names[i]}さん　{DayText.Full(St.StartDate, j)} {syms[S[i][j]]} → {syms[k]}", r.Change);
        Assert.Contains($"{DayText.Short(St.StartDate, j)}：{syms[k]}", r.Neighbors);
        // このセルの禁止の並びは、変えた後の報告に同じ場所の鍵が残るかどうかで「解消／残る」を言う。
        var stillHere = after.CellFamilies!.TryGetValue(key, out var fams) && fams.Contains("vio-c3n");
        Assert.Contains(r.ThisCell, x => x == "禁止の並び：" + (stillHere ? "残ります" : "解消します"));
        Assert.Equal(r.Counts.Count, new[] { S[i][j], k }.Distinct().Count());
        Assert.All(r.Counts, x => Assert.StartsWith("本人の", x));
    }

    [Fact]
    public void WishAndPinLinesAndProblemSentences()
    {
        var (i, j) = (0, 0);
        var names = St.StaffList.Select(x => x.Name).ToList(); var syms = St.Shifts.Select(x => x.Kigou).ToList();
        var k = Enumerable.Range(0, St.ShiftCount).First(x => x != S[i][j]);
        var wished = St with { Wishes = new Dictionary<string, int>(St.Wishes) { [$"{i},{j}"] = k } };
        Assert.Equal($"希望（{syms[k]}）どおりになります", CellSheetLogic.CellChangeReview(wished, S, i, j, k, names, syms, null, null, false, Label).WishPin[0]);
        var noWish = new Dictionary<string, int>(St.Wishes);
        noWish.Remove($"{i},{j}");
        Assert.StartsWith("このセルは手動固定", CellSheetLogic.CellChangeReview(St with { Wishes = noWish }, S, i, j, k, names, syms, null, null, true, Label).WishPin[0]);
        Assert.Equal("前後の勤務との組み合わせが禁止条件に該当しています", CellSheetLogic.CellProblemSentence("c3n", Label));
        Assert.Equal("10/28(水) Dテ → 10/29(木) B4",
            CellSheetLogic.SequenceLine("2026-10-01", Enumerable.Range(0, 31).Select(x => x == 27 ? 0 : 1).ToList(), new[] { 28, 27 }, new[] { "Dテ", "B4" }));
        Assert.Null(CellSheetLogic.SequenceLine("2026-10-01", new[] { 0 }, new[] { 0 }, new[] { "A" }));
    }

    [Fact]
    public void BoardStatusSeparatesAutoCreateFromEvaluation()
    {
        Assert.Equal(("自動作成：未実行", "評価：未評価"), CellSheetLogic.BoardStatusLines(false, false, false, 0, 0));
        Assert.Equal(("自動作成：未実行（手で編集）", "評価：必須違反 3件"), CellSheetLogic.BoardStatusLines(false, true, true, 3, 5));
        Assert.Equal(("自動作成：済み", "評価：要調整 5件"), CellSheetLogic.BoardStatusLines(true, true, true, 0, 5));
        Assert.Equal(("自動作成：済み", "評価：違反なし"), CellSheetLogic.BoardStatusLines(true, true, true, 0, 0));
    }

    [Fact]
    public void CellCountLinesKeepTheTargetVisible()
    {
        var u = new UiState
        {
            ShiftSymbols = new[] { "休", "Cオ", "B4" }, Schedule = new IReadOnlyList<int>[] { new[] { 2, 1, 2, 1, 0 } },
            CountViolations = new Dictionary<string, string> { ["0,1"] = "vio-aptLow" },
        };
        (int? Lo, int? Hi, int? Apt) Limits(int i, int k) => k switch { 1 => (null, null, 6), 2 => (null, null, 5), _ => (null, null, null) };
        Assert.Equal(new[] { "B4：現在2回／目標5回", "Cオ：現在2回／目標6回" }, CellSheetLogic.CellCountLines(u, 0, 2, Limits));
    }
}
