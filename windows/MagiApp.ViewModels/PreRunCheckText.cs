using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[つくる前の確認] シートの 1 行。Staff/Day があれば押すとそのセルへ移る（希望の行は希望のシートで開く）。Kotlin <c>PreRunRow</c>。</summary>
public sealed record PreRunRow(string Text, int? Staff = null, int? Day = null, bool Wish = false);

public sealed record PreRunSheetText(
    string? FloorHeader,
    IReadOnlyList<PreRunRow> FloorRows,
    string? RerunHeader,
    IReadOnlyList<PreRunRow> RerunRows,
    string? WallLine,
    bool HasWishRows,
    string? OverCapNote = null,
    IReadOnlyList<PreRunRow>? OverCapRows = null,
    string? ZeroCapNote = null);

/// <summary><see cref="PreRunCheck"/> の結果を行にする。Kotlin <c>preRunSheetText</c>（MagiViewState.kt）の移植。WinUI のシートは <c>MainWindow.ShowPreRunCheckAsync</c>。</summary>
public static class PreRunCheckText
{
    public const string ZeroCapTag = "（入れないシフトの指定が関係）";
    public const string ZeroCapNoteText = "「入れないシフトの指定が関係」の行は、個人の上限0（入れない指定）が原因で残ります。希望のせいではありません。見直すときは、設定で入れない指定を変えてください。";
    public const string FloorNote = "本人の希望は固定・必要人数は設定どおりなので、何度つくっても必須違反として残ります。";
    public const string OverCapHead = "設定上入れないシフトと希望（要調整）";
    public const string OverCapZero = "上限0のシフトに希望が載っています。上限0は意図した制限です。残るのは要調整です。希望を変えるか、入れない指定を設定で見直してください。";
    public const string OverCapOther = "個人の上限より多い希望が載っています。残るのは要調整です。希望を変えるか、設定で上限を見直してください。";
    public const string RerunNote = "今の勤務表に個人の上限（0回）のシフトが入っています。つくると外されます。";

    public static PreRunSheetText Of(PreRunCheck.Summary s, UiState ui)
    {
        string Name(int i) => i >= 0 && i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
        string Sym(int k) => k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : $"{k}";
        string Day(int j) => DayText.Short(ui.StartDate, j);
        ConstraintMus.WishPin? Pin(IReadOnlyList<ConstraintMus.Item> core) => core.OfType<ConstraintMus.WishPin>().FirstOrDefault();

        var floor = new List<PreRunRow>();
        foreach (var g in s.WishConflicts)
        {
            var pat = string.Join("→", g.Shifts.Select(Sym));
            var text = g.Family == "c3w"
                ? $"{Name(g.Staff)} {Day(g.Days[0])}「{Sym(g.Shifts[0])}」→ {Day(g.Days[1])}「{Sym(g.Shifts[1])}」 本人の希望どうしが希望の前日の禁止に当たっています"
                : $"{Name(g.Staff)} {string.Join("・", g.Days.Select(Day))} 本人の希望「{pat}」が禁止の並びに当たっています";
            floor.Add(new PreRunRow(text, g.Staff, g.Days[^1], true));
        }
        foreach (var w in s.ImpossibleWishes)
        {
            var cell = w.StaffIndex >= 0 && w.DayIndex >= 0;
            floor.Add(new PreRunRow($"{w.StaffName} {(w.DayIndex >= 0 ? Day(w.DayIndex) : "?")} 本人の希望「{w.ShiftSymbol}」は反映できません（{w.Reason}）",
                cell ? w.StaffIndex : null, cell ? w.DayIndex : null, cell));
        }
        foreach (var d in s.DayProofs)
        {
            var w = Pin(d.Core);
            floor.Add(new PreRunRow($"{DayText.Full(ui.StartDate, d.Day)} 必要人数と本人の希望の衝突（{d.Core.Count}件は同時に成立しません）{(s.ZeroCapDays.Contains(d.Day) ? ZeroCapTag : "")}", w?.Staff, w?.Day, w is not null));
        }
        foreach (var c in s.StaffProofs)
        {
            var w = Pin(c.Core);
            floor.Add(new PreRunRow($"{Name(c.Staff)} 本人の希望と条件の組合せ（{c.Core.Count}件は同時に成立しません）{(PreRunCheck.Summary.ZeroCapStaffProof(c) ? ZeroCapTag : "")}", w?.Staff, w?.Day, w is not null));
        }
        foreach (var f in s.ForcedShortfalls) floor.Add(new PreRunRow($"「{f.ShiftSymbol}」 {f.Cells}日で担当できる人より必要人数が多く、人員不足が合計{f.Amount}人残ります{(s.ZeroCapShorts.Contains(f.ShiftIndex) ? ZeroCapTag : "")}"));

        var rerun = s.RerunClears.Select(c => new PreRunRow($"{Name(c.Staff)} {Day(c.Day)} {Sym(c.Shift)}", c.Staff, c.Day)).ToList();
        var wall = s.Wall is { } h
            ? $"個人の上限0：{h.Pairs}組（{h.StaffCount}人）。入れないシフトの指定です。"
            : null;
        string? overNote = null;
        if (s.WishOverCaps.Count > 0)
        {
            var f = s.WishOverCaps[0];
            var who = $"{Name(f.Staff)}「{Sym(f.Shift)}」{(s.WishOverCaps.Count > 1 ? "など" : "")}";
            overNote = $"{who}：{(s.WishOverCaps.All(w => w.Hi == 0) ? OverCapZero : OverCapOther)}";
        }
        var overRows = s.WishOverCaps.Select(w => new PreRunRow($"{Name(w.Staff)}「{Sym(w.Shift)}」 本人の希望{w.Wished}件（個人の上限{w.Hi}回）")).ToList();
        return new PreRunSheetText(
            floor.Count == 0 ? null : $"何度つくっても残る（{floor.Count}件）", floor,
            rerun.Count == 0 ? null : $"もう一度つくると外れる（{rerun.Count}件）", rerun,
            wall, floor.Any(r => r.Wish), overNote, overRows,
            floor.Any(r => r.Text.EndsWith(ZeroCapTag)) ? ZeroCapNoteText : null);
    }
}
