using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>セルの印と一文（重さ・重い順の族・状態の 1 行）。セルのシートと同じ判定から作る。Kotlin <c>CellMark</c>。</summary>
public sealed record CellMark(CellSeverity Severity, IReadOnlyList<string> Families, string OneLine, string Cause = "");

/// <summary>
/// 表示の写し（統合仕様「印と一文を 1 か所で」、Kotlin <c>DisplaySnapshot</c>）。検査結果を反映するたびに ViewModel が 1 回だけ作り、
/// 上部の札・巡回の件数・セルの状態の 1 行はここを読む＝画面ごとに数え直して食い違わない。セッション内のみ（JSON に保存しない）。
/// Kotlin の <c>CellSheetRev</c>（検査世代・盤面・希望）を CheckRev/Schedule/Wishes で持つ（editRev は C# に無い）。今と違えば古い＝読む側は従来の計算へ戻る。
/// </summary>
public sealed record DisplaySnapshot(
    long CheckRev,
    IReadOnlyList<IReadOnlyList<int>> Schedule,
    IReadOnlyDictionary<string, int> Wishes,
    long BestHard,
    IReadOnlyDictionary<string, int> Breakdown,
    string HardText,
    string SoftText,
    IReadOnlyDictionary<(int I, int J), CellMark> CellMarks,
    IReadOnlyList<TourItem> TourItems,
    int CovUPeople,
    string? NextAction)
{
    private static readonly CellMark NoMark = new(CellSeverity.None, Array.Empty<string>(), "違反なし");

    public bool FreshFor(UiState ui) =>
        CheckRev == ui.CheckRev && BestHard == ui.BestHard && SameMap(Breakdown, ui.Breakdown) && SameMap(Wishes, ui.Wishes) &&
        Schedule.Count == ui.Schedule.Count && Schedule.Zip(ui.Schedule).All(p => p.First.SequenceEqual(p.Second));

    public CellMark CellMarkAt(int i, int j) => CellMarks.TryGetValue((i, j), out var m) ? m : NoMark;

    private static bool SameMap(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b) =>
        ReferenceEquals(a, b) || (a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value));
}

public static class DisplaySnapshots
{
    private static IReadOnlyList<string> FamiliesAt(IReadOnlyDictionary<string, IReadOnlyList<string>> m, string key) =>
        m.TryGetValue(key, out var v) ? v : Array.Empty<string>();

    /// <summary>セルのシートと同じ族（表示クラス＋期間の制約の区間＋当日・回数、足りない側を除く）。Kotlin <c>cellMarkFamilies</c>。</summary>
    public static IReadOnlyList<string> CellMarkFamilies(UiState ui, int i, int j, IReadOnlySet<string> c1Marks)
    {
        var current = i >= 0 && i < ui.Schedule.Count && j >= 0 && j < ui.Schedule[i].Count ? ui.Schedule[i][j] : -1;
        var needCls = current >= 0 ? FamiliesAt(ui.NeedFamilies, $"{current},{j}") : Array.Empty<string>();
        var countCls = current >= 0 ? FamiliesAt(ui.CountFamilies, $"{i},{current}") : Array.Empty<string>();
        var shortSide = CellSheetLogic.ShortSideFamilies(needCls, countCls);
        var inC1 = ui.C1Shortages.Any(c => c.Staff == i && j >= c.From && j <= c.To);
        return CellSheetLogic.StatusFamilies(
                CellSheetLogic.SheetCellClasses(GridDisplayMarks.DisplayCellClasses(ui, $"{i},{j}", c1Marks), inC1), needCls, countCls)
            .Where(f => !shortSide.Contains(f)).ToList();
    }

    /// <summary>Kotlin <c>buildDisplaySnapshot</c>。族名は labelOf で受ける（C# の状態の 1 行・巡回と同じ）。</summary>
    public static DisplaySnapshot Build(MagiState state, UiState ui, Func<string, string> labelOf)
    {
        var s = ui.Schedule.ToIntArray2D();
        var p = ui.Schedule.Count == 0 ? null : ScheduleUtil.CachedProblem(state);
        var c1Marks = GridDisplayMarks.C1DisplayMarks(ui);
        var marks = new Dictionary<(int I, int J), CellMark>();
        if (p is not null)
            for (var i = 0; i < s.Length; i++)
                for (var j = 0; j < s[i].Length; j++)
                {
                    var fams = CellMarkFamilies(ui, i, j, c1Marks);
                    if (fams.Count == 0) continue;
                    var st = CellSheetLogic.StatusLine(state, p, s, i, j, fams, labelOf);
                    marks[(i, j)] = new CellMark(st.Severity, fams, st.Text, st.Cause);
                }
        var tour = p is null ? Array.Empty<TourItem>() : CellSheetLogic.HardViolationItems(state, p, s, ui.ViolationCellFamilies, labelOf);
        var covU = ui.Breakdown.GetValueOrDefault("covU", 0);
        return new DisplaySnapshot(
            ui.CheckRev, ui.Schedule.Select(r => (IReadOnlyList<int>)r.ToList()).ToList(), new Dictionary<string, int>(ui.Wishes),
            ui.BestHard, new Dictionary<string, int>(ui.Breakdown),
            AnalysisTriage.HardText(ui.Breakdown, ui.BestHard),
            AnalysisTriage.UnitTotals(ui.Breakdown, MirrorKeys.Soft) ?? "なし",
            marks, tour, covU,
            tour.Count > 0 ? CellSheetLogic.TourHeading(tour, 0) : CellSheetLogic.TourCovULine(covU));
    }

    /// <summary>上部の札・一覧の必須違反の言い方。写しが今の検査に追いついていればそれを、でなければ同じ式で数える。</summary>
    public static string DisplayHardText(UiState ui) =>
        ui.Display is { } d && d.FreshFor(ui) ? d.HardText : AnalysisTriage.HardText(ui.Breakdown, ui.BestHard);
}
