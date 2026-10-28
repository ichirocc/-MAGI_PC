using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[つくる前の確認] シートの 1 行。Staff/Day があれば押すとそのセルへ移る（希望の行は希望のシートで開く）。セルを持たない行は Landing（入力箇所、3.644.0）へ。Kotlin <c>PreRunRow</c>。</summary>
public sealed record PreRunRow(string Text, int? Staff = null, int? Day = null, bool Wish = false, EditLanding? Landing = null);

public sealed record PreRunSheetText(
    string? FloorHeader,
    IReadOnlyList<PreRunRow> FloorRows,
    string? RerunHeader,
    IReadOnlyList<PreRunRow> RerunRows,
    string? WallLine,
    bool HasWishRows,
    string? OverCapNote = null,
    IReadOnlyList<PreRunRow>? OverCapRows = null,
    string? ZeroCapNote = null,
    EditLanding? WallLanding = null);

/// <summary><see cref="PreRunCheck"/> の結果を行にする。Kotlin <c>preRunSheetText</c>（MagiViewState.kt）の移植。WinUI のシートは <c>MainWindow.ShowPreRunCheckAsync</c>。</summary>
public static class PreRunCheckText
{
    public const string ZeroCapTag = "（入れないシフトの指定が関係）";
    public const string ZeroCapNoteText = "「入れないシフトの指定が関係」の行は、対象外（上限0）の人を数えないため残ります。意図した指定ならこのままで構いません。";
    public const string FloorNote = "希望と必要人数は固定なので、何度つくっても残ります。行を押すと直す場所を開けます。";
    public const string OverCapHead = "設定上入れないシフトと希望（要調整）";
    public const string OverCapZero = "対象外（上限0）のシフトに希望があります。意図した指定なら、残るのは要調整だけです。";
    public const string OverCapOther = "個人の上限より多い希望があります（残るのは要調整）。希望か上限を見直してください。";
    public const string RerunNote = "今の勤務表に個人の上限（0回）のシフトが入っています。つくると外されます。";

    /// <summary>証明の核に入っている固定の出どころの呼び名（希望だけ／手動固定だけ／両方）。</summary>
    public static string PinSourceWord(IReadOnlyList<ConstraintMus.Item> core)
    {
        var pins = core.OfType<ConstraintMus.WishPin>().ToList();
        var manual = pins.Any(it => it.Manual);
        var wish = pins.Any(it => !it.Manual);
        return manual && wish ? "希望と手で固定したマス" : manual ? "手で固定したマス" : "本人の希望";
    }

    public static PreRunSheetText Of(PreRunCheck.Summary s, UiState ui)
    {
        string Name(int i) => i >= 0 && i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
        string Sym(int k) => k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : $"{k}";
        string Day(int j) => DayText.Short(ui.StartDate, j);
        // 行を押して開くのは希望のセル（手動固定は勤務表で外すものなので飛ばさない）。
        ConstraintMus.WishPin? Pin(IReadOnlyList<ConstraintMus.Item> core) => core.OfType<ConstraintMus.WishPin>().FirstOrDefault(w => !w.Manual);

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
                cell ? w.StaffIndex : null, cell ? w.DayIndex : null, cell,
                Landing: !cell && w.StaffIndex >= 0 ? new EditLanding(0, null, w.StaffIndex) : null));
        }
        foreach (var d in s.DayProofs)
        {
            var w = Pin(d.Core);
            floor.Add(new PreRunRow($"{DayText.Full(ui.StartDate, d.Day)} 必要人数と{PinSourceWord(d.Core)}の衝突（{d.Core.Count}件は同時に成立しません）{(s.ZeroCapDays.Contains(d.Day) ? ZeroCapTag : "")}", w?.Staff, w?.Day, w is not null,
                Landing: w is null ? GuidedFixRules.LandingForProofCore(d.Core, s.ZeroCapDays.Contains(d.Day)) : null));
        }
        foreach (var c in s.StaffProofs)
        {
            var w = Pin(c.Core);
            floor.Add(new PreRunRow($"{Name(c.Staff)} {PinSourceWord(c.Core)}と条件の組合せ（{c.Core.Count}件は同時に成立しません）{(PreRunCheck.Summary.ZeroCapStaffProof(c) ? ZeroCapTag : "")}", w?.Staff, w?.Day, w is not null,
                Landing: w is null ? GuidedFixRules.LandingForProofCore(c.Core, PreRunCheck.Summary.ZeroCapStaffProof(c)) : null));
        }
        foreach (var f in s.ForcedShortfalls)
        {
            var zero = s.ZeroCapShorts.Contains(f.ShiftIndex);
            floor.Add(new PreRunRow($"「{f.ShiftSymbol}」 {f.Cells}日で担当できる人より必要人数が多く、人員不足が合計{f.Amount}人残ります{(zero ? ZeroCapTag : "")}",
                Landing: zero ? new EditLanding(2, "yr_count", Label: GuidedFixRules.LandingZeroCap) : new EditLanding(2, "yr_ws1")));
        }

        var rerun = s.RerunClears.Select(c => new PreRunRow($"{Name(c.Staff)} {Day(c.Day)} {Sym(c.Shift)}", c.Staff, c.Day)).ToList();
        var wall = s.Wall is { } h
            ? $"{SettingIssues.ZeroCapLabel}：{h.Pairs}組（{h.StaffCount}人）。パート等の意図した指定です。"
            : null;
        string? overNote = null;
        if (s.WishOverCaps.Count > 0)
        {
            var f = s.WishOverCaps[0];
            var who = $"{Name(f.Staff)}「{Sym(f.Shift)}」{(s.WishOverCaps.Count > 1 ? "など" : "")}";
            overNote = $"{who}：{(s.WishOverCaps.All(w => w.Hi == 0) ? OverCapZero : OverCapOther)}";
        }
        var overRows = s.WishOverCaps.Select(w => new PreRunRow($"{Name(w.Staff)}「{Sym(w.Shift)}」 本人の希望{w.Wished}件（個人の上限{w.Hi}回）",
            Landing: new EditLanding(2, "yr_count", CountStaff: w.Staff, CountShift: w.Shift, Label: w.Hi == 0 ? GuidedFixRules.LandingZeroCap : null))).ToList();
        return new PreRunSheetText(
            floor.Count == 0 ? null : $"何度つくっても残る（{floor.Count}件）", floor,
            rerun.Count == 0 ? null : $"再作成すると外れる（{rerun.Count}件）", rerun,
            wall, floor.Any(r => r.Wish), overNote, overRows,
            floor.Any(r => r.Text.EndsWith(ZeroCapTag)) ? ZeroCapNoteText : null,
            s.Wall is null ? null : new EditLanding(2, "yr_count", Label: GuidedFixRules.LandingZeroCap));
    }
}
