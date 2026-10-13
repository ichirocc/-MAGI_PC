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
public sealed record ChainFixPreview(FixSuggestion Suggestion, string Title, IReadOnlyList<string> Changes, ChainTarget? Target = null)
{
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
        var changes = s.Ops.Select(op =>
        {
            var inRange = op.Staff >= 0 && op.Staff < snapshot.Length && op.Day >= 0 && op.Day < snapshot[op.Staff].Length;
            // 「希望との不一致なし」は本人の了承ではない＝希望があるときだけ、希望どおりか違うかを事実として添える（仕様 §4）。
            var wish = wishes is not null && wishes.TryGetValue($"{op.Staff},{op.Day}", out var w) && w >= 0
                ? (w == op.ToShift ? "・希望どおり" : $"・希望（{Sym(w)}）と異なる勤務になります") : "";
            return $"{Name(op.Staff)} {DayText.Short(startDate, op.Day)} {(inRange ? Shift(snapshot[op.Staff][op.Day]) : "?")} → {Shift(op.ToShift)}{wish}";
        }).ToList();
        // 人数・マス数は本文 1 行目（提案のラベル）が持つ（Android 3.658.0: 題に入れると途中で折り返した）。
        return new ChainFixPreview(s, "複数人の入れ替え", changes);
    }
}
