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
        Assert.Equal($"必須違反（勤務表全体）：{AnalysisTriage.HardText(Rep.Breakdown, Rep.Hard)} → {AnalysisTriage.HardText(after.Breakdown, after.Hard)}", r.HardLine);
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
        // [3.678.0] 自動作成のあと手で変えた盤面は「未実行」と言わない。
        Assert.Equal("自動作成：済み（そのあと手で変更あり）", CellSheetLogic.BoardStatusLines(false, true, true, 0, 0, ranBeforeEdit: true).First);
        Assert.Equal("自動作成：未実行（手で編集）", CellSheetLogic.BoardStatusLines(false, true, true, 0, 0, ranBeforeEdit: false).First);
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

    /// <summary>選んだセルの問題が案で実際に消えるか（族の全体の件数ではなく、この場所の違反の同一性で判定する）。</summary>
    [Fact]
    public void ResolvesAtCellLooksAtThisPlaceOnly()
    {
        var key = Rep.CellFamilies!.First(e => e.Value.Contains("vio-c3n")).Key;
        var ij = key.Split(',').Select(int.Parse).ToArray();
        var (i, j) = (ij[0], ij[1]);
        for (var k = 0; k < St.ShiftCount; k++)
        {
            if (k == S[i][j]) continue;
            var ops = new[] { new FixCell(i, j, k) };
            var trial = S.Select(r => r.ToArray()).ToArray();
            trial[i][j] = k;
            var gone = !(UnifiedViolationChecker.Check(St, trial).CellFamilies!.TryGetValue(key, out var f) && f.Contains("vio-c3n"));
            Assert.Equal(gone, CellSheetLogic.ResolvesAtCell(St, S, Rep, ops, i, j, "c3n"));
        }
        // 別の職員の別の日だけを変える案は、このセルの問題を解消しない。
        var other = Enumerable.Range(0, St.StaffCount).First(x => x != i);
        var far = (j + 10) % St.DayCount;
        var k2 = Enumerable.Range(0, St.ShiftCount).First(x => x != S[other][far]);
        var farTrial = S.Select(r => r.ToArray()).ToArray();
        farTrial[other][far] = k2;
        var stillHere = UnifiedViolationChecker.Check(St, farTrial).CellFamilies!.TryGetValue(key, out var ff) && ff.Contains("vio-c3n");
        if (stillHere) Assert.False(CellSheetLogic.ResolvesAtCell(St, S, Rep, new[] { new FixCell(other, far, k2) }, i, j, "c3n"));
        // 問題の無いセルは解消しない（族を選ばなくても）。
        static bool Empty(IReadOnlyDictionary<string, IReadOnlyList<string>>? m, string k) => m is null || !m.TryGetValue(k, out var v) || v.Count == 0;
        var clean = Enumerable.Range(0, St.StaffCount).SelectMany(a => Enumerable.Range(0, St.DayCount).Select(b => (a, b))).First(c =>
            Empty(Rep.CellFamilies, $"{c.a},{c.b}") && Empty(Rep.CountFamilies, $"{c.a},{S[c.a][c.b]}") && Empty(Rep.NeedFamilies, $"{S[c.a][c.b]},{c.b}"));
        Assert.False(CellSheetLogic.ResolvesAtCell(St, S, Rep, new[] { new FixCell(clean.a, clean.b, S[clean.a][clean.b]) }, clean.a, clean.b, null));
    }

    [Fact]
    public void NewHardLabelsAreTheCheckersNewKeys()
    {
        var names = St.StaffList.Select(x => x.Name).ToList(); var syms = St.Shifts.Select(x => x.Kigou).ToList();
        var (i, j) = (0, 0);
        for (var k = 0; k < St.ShiftCount; k++)
        {
            var ops = new[] { new FixCell(i, j, k) };
            var trial = S.Select(r => r.ToArray()).ToArray();
            trial[i][j] = k;
            var expected = ViolationIdentity.NewHard(Rep, UnifiedViolationChecker.Check(St, trial)).Count;
            var got = CellSheetLogic.NewHardLabels(St, S, ops, names, syms, Label);
            Assert.Equal(expected, got.Count);
            Assert.All(got, x => Assert.StartsWith("必須・", x));
        }
    }

    [Fact]
    public void PreviewWishAndConsultLines()
    {
        var rows = new[] { new ChangeRow("福澤", "11/3", "休", "Aア", "希望（休）と異なる勤務になります"), new ChangeRow("金沢", "11/5", "A4", "休", "希望どおり") };
        Assert.Equal(("希望と異なる勤務になる：福澤さん 11/3", "必要な相談：福澤さん（希望と異なる勤務になる日）"), CellSheetLogic.PreviewWishLines(rows));
        Assert.Equal(("変更する勤務に希望はありません", "必要な相談：なし"), CellSheetLogic.PreviewWishLines(new[] { new ChangeRow("a", "1", "x", "y") }));
        Assert.Equal("変更箇所：2人・3セル", CellSheetLogic.ChangeSizeLine(new[] { new FixCell(0, 1, 1), new FixCell(0, 2, 1), new FixCell(3, 1, 0) }));
        var t = new FixTarget(null, 1, 2, 1, true, "必要人数");
        Assert.Equal(new[] { "11/3のAアの不足を減らす変更案を探しています", "ほかの日や職員の変更が必要な場合は、関連する変更もまとめて確認します。" }, CellSheetLogic.TargetSearchingLines(t, "11/3のAア"));
    }
}
