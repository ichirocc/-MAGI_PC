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
            CellSheetLogic.SheetCellClasses(GridDisplayMarks.DisplayCellClasses(Ui, $"{i},{j}", GridDisplayMarks.C1DisplayMarks(Ui)), C1ShortageAt(i, j) is not null),
            cur >= 0 ? FamiliesAt(Ui.NeedFamilies, $"{cur},{j}") : Array.Empty<string>(),
            cur >= 0 ? FamiliesAt(Ui.CountFamilies, $"{i},{cur}") : Array.Empty<string>());
        return CellSheetLogic.StatusLine(st, ScheduleUtil.CachedProblem(st), sched, i, j, fams, labelOf);
    }

    /// <summary>同じ違反のもう一方のセルの日（板挟みで他の人の手が無いときの行き先。無ければ空）。</summary>
    public IReadOnlyList<int> ViolationPartnerDaysFor(int i, int j)
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null || i < 0 || i >= sched.Length || j < 0 || j >= sched[i].Length) return Array.Empty<int>();
        var fams = CellSheetLogic.StatusFamilies(FamiliesAt(Ui.ViolationCellFamilies, $"{i},{j}"), Array.Empty<string>(), Array.Empty<string>());
        return CellSheetLogic.ViolationPartnerDays(ScheduleUtil.CachedProblem(st), sched, i, j, fams, Ui.ViolationCellFamilies);
    }

    /// <summary>状態の下の「関連セル: …」（同じ違反のもう一方のセル。必須のセルだけ。無ければ null）。</summary>
    public string? RelatedCellsLineFor(int i, int j, IReadOnlyList<int> partners)
    {
        var st = _state;
        var sched = _currentSchedule;
        return st is null || sched is null ? null : CellSheetLogic.RelatedCellsLine(st, sched, i, partners);
    }

    /// <summary>全部 ⚠ の理由 1 行（無ければ null）。</summary>
    public string? AllRiskReasonFor(int i, int j, ShiftMarks marks)
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null || i < 0 || i >= sched.Length || j < 0 || j >= sched[i].Length) return null;
        return CellSheetLogic.AllRiskReason(st, ScheduleUtil.CachedProblem(st), sched, i, j, marks, AllowedShiftsFor(i).ToList());
    }

    /// <summary>希望タブの注記＝このセルの希望が必須違反の並びに掛かっているとき（無ければ null）。</summary>
    public string? WishTabInvolvedLineFor(int i, int j)
    {
        if (!Ui.Wishes.TryGetValue($"{i},{j}", out var w) || w < 0 || w >= Ui.ShiftSymbols.Count) return null;
        var fams = CellSheetLogic.StatusFamilies(FamiliesAt(Ui.ViolationCellFamilies, $"{i},{j}"), Array.Empty<string>(), Array.Empty<string>());
        return NextActionGuide.WishTabInvolvedLine(Ui.ShiftSymbols[w], fams);
    }

    /// <summary>セル (i,j) に掛かる期間の制約の不足区間（無ければ null）。</summary>
    public C1Shortage? C1ShortageAt(int i, int j) => Ui.C1Shortages.FirstOrDefault(x => x.Staff == i && j >= x.From && j <= x.To);

    /// <summary>「詳しく」のこのセルの違反（重なった族すべて）。</summary>
    public IReadOnlyList<string> CellDetailLinesFor(int i, int j, Func<string, string> labelOf)
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null || i < 0 || i >= sched.Length || j < 0 || j >= sched[i].Length) return Array.Empty<string>();
        var cur = sched[i][j];
        var fams = CellSheetLogic.StatusFamilies(
            CellSheetLogic.SheetCellClasses(GridDisplayMarks.DisplayCellClasses(Ui, $"{i},{j}", GridDisplayMarks.C1DisplayMarks(Ui)), C1ShortageAt(i, j) is not null),
            cur >= 0 ? FamiliesAt(Ui.NeedFamilies, $"{cur},{j}") : Array.Empty<string>(),
            cur >= 0 ? FamiliesAt(Ui.CountFamilies, $"{i},{cur}") : Array.Empty<string>());
        return CellSheetLogic.CellDetailLines(st, ScheduleUtil.CachedProblem(st), sched, i, j, fams, labelOf);
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
