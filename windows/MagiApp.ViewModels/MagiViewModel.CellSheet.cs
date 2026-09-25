using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>セル編集シートの材料（Android <c>CellEditSheet</c> が画面で組むものを VM に置く＝WinUI 側を薄く保つ）。</summary>
public sealed partial class MagiViewModel
{
    private static IReadOnlyList<string> FamiliesAt(IReadOnlyDictionary<string, IReadOnlyList<string>> m, string key) =>
        m.TryGetValue(key, out var v) ? v : Array.Empty<string>();

    /// <summary>1 行の状態（このセルの族＋c1 の表示アンカー、今のシフトの人員、この職員の今のシフトの回数）。</summary>
    public CellStatus CellStatusFor(int i, int j, Func<string, string> labelOf)
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null || i < 0 || i >= sched.Length || j < 0 || j >= sched[i].Length) return new CellStatus(CellSeverity.None, "違反なし");
        var cur = sched[i][j];
        var fams = CellSheetLogic.StatusFamilies(
            GridDisplayMarks.DisplayCellClasses(Ui, $"{i},{j}", GridDisplayMarks.C1DisplayAnchors(Ui)),
            cur >= 0 ? FamiliesAt(Ui.NeedFamilies, $"{cur},{j}") : Array.Empty<string>(),
            cur >= 0 ? FamiliesAt(Ui.CountFamilies, $"{i},{cur}") : Array.Empty<string>());
        return CellSheetLogic.StatusLine(st, ScheduleUtil.CachedProblem(st), sched, i, j, fams, labelOf);
    }

    /// <summary>「詳しく」のこのセルの違反（重なった族すべて、期間の制約は連続 N 区間つき）。</summary>
    public IReadOnlyList<string> CellDetailLinesFor(int i, int j, Func<string, string> labelOf)
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null || i < 0 || i >= sched.Length || j < 0 || j >= sched[i].Length) return Array.Empty<string>();
        var cur = sched[i][j];
        var anchors = GridDisplayMarks.C1DisplayAnchors(Ui);
        var fams = CellSheetLogic.StatusFamilies(
            GridDisplayMarks.DisplayCellClasses(Ui, $"{i},{j}", anchors),
            cur >= 0 ? FamiliesAt(Ui.NeedFamilies, $"{cur},{j}") : Array.Empty<string>(),
            cur >= 0 ? FamiliesAt(Ui.CountFamilies, $"{i},{cur}") : Array.Empty<string>());
        int? runs = anchors.TryGetValue($"{i},{j}", out var n) ? n : null;
        return CellSheetLogic.CellDetailLines(st, ScheduleUtil.CachedProblem(st), sched, i, j, fams, runs, labelOf);
    }

    /// <summary>ボタンに出すシフト（誰か 1 人でも担当できるもの）。</summary>
    public IReadOnlyList<int> SheetShifts() =>
        CellSheetLogic.SheetShifts(Ui.ShiftSymbols.Count, Enumerable.Range(0, Ui.StaffNames.Count).Select(AllowedShiftsFor));

    public string StaffCountShortFor(int i)
    {
        var st = _state;
        var sched = _currentSchedule;
        return st is null || sched is null ? "" : CellSheetLogic.StaffCountShort(st, ScheduleUtil.CachedProblem(st), sched, i, Ui.CountFamilies);
    }

    /// <summary>シフトボタンの印を背景で評価する（取り消しでも空を返す）。</summary>
    public Task<ShiftMarks> ShiftMarksForAsync(int i, int j, CellSeverity severity, CancellationToken ct)
    {
        var st = _state;
        var sched = _currentSchedule?.Copy2D();
        if (st is null || sched is null || i < 0 || i >= sched.Length) return Task.FromResult(ShiftMarks.Empty);
        var cands = AllowedShiftsFor(i);
        return Task.Run(() => CellSheetLogic.EvaluateShiftMarks(st, sched, i, j, severity, cands, () => !ct.IsCancellationRequested), ct);
    }
}
