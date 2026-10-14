using System.Collections.Generic;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [3.644.0/UX-03] 複数人の入替（玉突き）を当てる前の一覧＝だれの・どの日の・何を何に変えるか（Kotlin <c>ChainFixPreview.kt</c>）。
/// 必須違反の増減は画面が同じ提案から <see cref="NextActionGuide.FixImpactLines"/> で出す（族名の日本語表は WinUI 側にあるため、Kotlin が
/// 持つ hardLine/caution はここでは持たない）。
/// </summary>
/// <param name="Target">探した枠（日付・シフト記号・見出し）。相談に積んだあと、今の勤務表で案を探し直すために持つ。</param>
/// <param name="Purpose">今回の目的（案の一覧から来たとき。例「古泉さんのA4不足を減らす」）。null＝目的の行を出さない。</param>
/// <param name="Effect">対象の数の変化（例「A4：7回 → 9回」「適用後も、下限まで6回不足」）。</param>
/// <param name="FromList">案の一覧から開いた＝閉じるボタンは「案の一覧に戻る」。</param>
/// <param name="Rows">変わるセル（職員ごとに日付・変更前・変更後の表にする）。</param>
/// <param name="CountNotes">職員ごとの回数の変化（2 か所以上を変える理由の事実）。</param>
/// <param name="FocusKey">案の一覧のどの依頼から来たか（適用後に同じ依頼の画面へ結果を返す）。</param>
/// <param name="FixTarget">見ている問題（適用後の結果の行で、解消して印が消えたあとも前後を言う）。</param>
public sealed record ChainFixPreview(FixSuggestion Suggestion, string Title, IReadOnlyList<string> Changes, ChainTarget? Target = null,
    string? Purpose = null, IReadOnlyList<string>? Effect = null, bool FromList = false, IReadOnlyList<ChangeRow>? Rows = null,
    IReadOnlyList<string>? CountNotes = null, string? FocusKey = null, FixTarget? FixTarget = null)
{
    public IReadOnlyList<string> CountNotes { get; init; } = CountNotes ?? Array.Empty<string>();
    public IReadOnlyList<string> Effect { get; init; } = Effect ?? Array.Empty<string>();
    public IReadOnlyList<ChangeRow> Rows { get; init; } = Rows ?? Array.Empty<ChangeRow>();

    /// <summary>変更を当てるボタン（違反セルの半自動修正仕様 §5: 押すと検証してから全変更を反映する。Kotlin <c>APPLY_CHANGE_BUTTON</c>）。</summary>
    public const string ApplyChangeButton = "この変更を適用";
    /// <summary>当てずに閉じるボタン（今の勤務表のまま）。</summary>
    public const string KeepCurrentButton = "今は変更しない";
    /// <summary>誰が・いつ確認するか（仕様 §4）。担当者名や期限のデータは無い＝推測せず「未設定」と書く。</summary>
    public const string ChangeDutyNote = "対応する人: 勤務表の作成担当者（担当者名は未設定）／対応する時期: 変更を適用する前（期限は未設定）";

    /// <param name="shiftNames">登録された勤務名（記号と違えば「記号（勤務名）」で併記）。</param>
    /// <param name="wishes">希望 "i,j"→シフト（あれば行ごとに希望どおりか添える）。</param>
    public static ChainFixPreview Of(FixSuggestion s, int[][] snapshot, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, string startDate,
        IReadOnlyList<string>? shiftNames = null, IReadOnlyDictionary<string, int>? wishes = null)
    {
        string Name(int i) => i >= 0 && i < staffNames.Count ? staffNames[i] : $"職員{i + 1}";
        string Sym(int k) => k >= 0 && k < shiftSymbols.Count ? shiftSymbols[k] : $"{k}";
        string Shift(int k)
        {
            var nm = shiftNames is not null && k >= 0 && k < shiftNames.Count ? shiftNames[k] : null;
            return Sym(k) + (!string.IsNullOrWhiteSpace(nm) && nm != Sym(k) ? $"（{nm}）" : "");
        }
        var board = snapshot.Select(r => (IReadOnlyList<int>)r).ToList();
        var after = CellSheetLogic.BoardAfter(board, s.Ops);
        var rows = s.Ops.Select(op =>
        {
            var inRange = op.Staff >= 0 && op.Staff < snapshot.Length && op.Day >= 0 && op.Day < snapshot[op.Staff].Length;
            // 「希望との不一致なし」は本人の了承ではない＝希望があるときだけ、希望どおりか違うかを事実として添える（仕様 §4）。
            var wish = wishes is not null && wishes.TryGetValue($"{op.Staff},{op.Day}", out var w) && w >= 0
                ? (w == op.ToShift ? "希望どおり" : $"希望（{Sym(w)}）と異なる勤務になります") : "";
            // その日の人数（変更前の勤務と変更後の勤務）。休みを移す案は両日の休みの人数が並ぶ。
            var impact = string.Join("、", new[] { inRange ? snapshot[op.Staff][op.Day] : (int?)null, op.ToShift }.OfType<int>().Distinct().Select(k =>
                $"{Sym(k)} {snapshot.Count(r => op.Day >= 0 && op.Day < r.Length && r[op.Day] == k)}人 → {after.Count(r => op.Day >= 0 && op.Day < r.Count && r[op.Day] == k)}人"));
            return new ChangeRow(Name(op.Staff), DayText.Short(startDate, op.Day), inRange ? Shift(snapshot[op.Staff][op.Day]) : "?", Shift(op.ToShift), wish, impact);
        }).ToList();
        var changes = rows.Select(r => $"{r.Staff} {r.Date} {r.Before} → {r.After}" + (r.Wish.Length > 0 ? $"・{r.Wish}" : "")).ToList();
        // 人数・マス数は本文 1 行目（提案のラベル）が持つ（Android 3.658.0: 題に入れると途中で折り返した）。
        return new ChainFixPreview(s, "複数人の入れ替え", changes, Rows: rows, CountNotes: CellSheetLogic.StaffCountNotes(board, s.Ops, staffNames, shiftSymbols));
    }
}

/// <summary>変わるセル 1 つ（Kotlin <c>ChangeRow</c>）。Wish＝希望があるときの注記（希望どおり／希望と異なる）、Impact＝その日の人数の変化。</summary>
public sealed record ChangeRow(string Staff, string Date, string Before, string After, string Wish = "", string Impact = "");
