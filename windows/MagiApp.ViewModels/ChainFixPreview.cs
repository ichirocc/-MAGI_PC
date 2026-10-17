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
/// <param name="FromList">案の一覧から開いた＝［別案を見る］を出す（閉じるボタンは［今は変更しない］のまま。WinUI は本文のリンクで案のパネルを開き直す）。</param>
/// <param name="Rows">変わるセル（職員ごとに日付・変更前・変更後の表にする）。</param>
/// <param name="CountNotes">職員ごとの回数の変化（2 か所以上を変える理由の事実）。</param>
/// <param name="FocusKey">案の一覧のどの依頼から来たか（適用後に同じ依頼の画面へ結果を返す）。</param>
/// <param name="FixTarget">見ている問題（適用後の結果の行で、解消して印が消えたあとも前後を言う）。</param>
/// <param name="NewHard">別の場所に新しく生まれる必須違反の鍵（正式な検査の前後。null＝評価していない）。Kotlin は名前の一覧を持つが、族名の表は
/// WinUI にあるので鍵で持ち、画面が <see cref="CellSheetLogic.ViolationKeyLabel"/> で名を付ける。</param>
/// <param name="Summary">前後の 1 行（<see cref="PreviewSummaryLine"/>、ApplyError の次に太字）。null＝まだ組み立てていない。</param>
public sealed record ChainFixPreview(FixSuggestion Suggestion, IReadOnlyList<string> Changes, ChainTarget? Target = null,
    string? Purpose = null, IReadOnlyList<string>? Effect = null, bool FromList = false, IReadOnlyList<ChangeRow>? Rows = null,
    IReadOnlyList<string>? CountNotes = null, string? FocusKey = null, FixTarget? FixTarget = null, IReadOnlyList<ViolationKey>? NewHard = null,
    string? ApplyError = null, bool GlobalPolish = false, string? Summary = null)
{
    public IReadOnlyList<string> CountNotes { get; init; } = CountNotes ?? Array.Empty<string>();
    public IReadOnlyList<string> Effect { get; init; } = Effect ?? Array.Empty<string>();
    public IReadOnlyList<ChangeRow> Rows { get; init; } = Rows ?? Array.Empty<ChangeRow>();

    /// <summary>変更を当てるボタン（違反セルの半自動修正仕様 §5: 押すと検証してから全変更を反映する。Kotlin <c>APPLY_CHANGE_BUTTON</c>）。</summary>
    public const string ApplyChangeButton = "この変更を適用する";
    /// <summary>[3.687.0/R09] 確認の画面の門の説明（FixApplyGate と同じ規則: 必須を増やさない・別の場所に必須を生まない・希望の固定を崩さない。要調整だけの改善は通る）。</summary>
    public const string PreviewGateNote = "適用の直前に最新の勤務表でもう一度検査し、必須違反を増やす・別の職員や日に必須違反を生む・希望の固定を崩す変更は適用しません。適用したあとは「元に戻す」で取り消せます。";
    /// <summary>当てずに閉じるボタン（今の勤務表のまま）。</summary>
    public const string KeepCurrentButton = "今は変更しない";
    /// <summary>変更を当てる前の確認の見出し（1 手でも入替でも同じ）。</summary>
    public const string PreviewTitle = "変更の確認";

    /// <summary>変更の確認の 1 行と、変わるセルの 1 行（「山本  10/15  休 → 遅番」）。</summary>
    public static string PreviewCountLine(int n) => $"{n}か所変わります";
    public static string PreviewRowLine(string staff, string date, string before, string after) => $"{staff}  {date}  {before} → {after}";

    /// <param name="shiftNames">登録された勤務名（記号と違えば「記号（勤務名）」で併記）。</param>
    /// <param name="wishes">希望 "i,j"→シフト（あれば行ごとに希望どおりか添える）。</param>
    public static ChainFixPreview Of(FixSuggestion s, int[][] snapshot, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, string startDate,
        IReadOnlyList<string>? shiftNames = null, IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyList<int>? staffIds = null, IReadOnlyList<string>? staffGroups = null)
    {
        var dupNames = staffNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        string Name(int i)
        {
            if (i < 0 || i >= staffNames.Count) return $"職員{i + 1}";
            var n = staffNames[i];
            if (!dupNames.Contains(n)) return n;
            var g = staffGroups is not null && i < staffGroups.Count ? staffGroups[i] : null;
            return n + (!string.IsNullOrWhiteSpace(g) ? $"（{g}）" : $"（{i + 1}番目）");
        }
        int Key(int i) => staffIds is not null && i >= 0 && i < staffIds.Count && staffIds[i] > 0 ? staffIds[i] : i;
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
            return new ChangeRow(Name(op.Staff), DayText.Short(startDate, op.Day), inRange ? Shift(snapshot[op.Staff][op.Day]) : "?", Shift(op.ToShift), wish, impact, Key(op.Staff));
        }).ToList();
        var changes = rows.Select(r => $"{r.Staff} {r.Date} {r.Before} → {r.After}" + (r.Wish.Length > 0 ? $"・{r.Wish}" : "")).ToList();
        return new ChainFixPreview(s, changes, Rows: rows, CountNotes: CellSheetLogic.StaffCountNotes(board, s.Ops, staffNames, shiftSymbols));
    }

    /// <summary>対象の数の変化の 3 行（案の一覧・争点カード・確認の画面で共有。Kotlin <c>previewEffectLines</c>）。</summary>
    public static IReadOnlyList<string> PreviewEffectLines(string label, CountEffect e, string basis) =>
        new[] { $"{label}：{e.CountLine()}", $"今回の{e.GapLine()}", e.AfterNote(basis) };

    /// <summary>
    /// 確認の画面の前後の 1 行（D-2、Kotlin <c>previewSummaryLine</c>）「今回の不足 1人 → 0人（解消）・2か所を変更・新たな必須違反なし」。区切りごとに単位は 1 つ（INV-07）。
    /// <paramref name="effect"/>＝見ている問題の変化（null＝対象なし＝必須違反の増減の向きだけ言う）、<paramref name="newHard"/> null＝未評価（INV-10）。
    /// </summary>
    public static string PreviewSummaryLine(CountEffect? effect, int cells, int hardDelta, int? newHard) => string.Join("・", new[]
    {
        effect is not null ? $"今回の{effect.GapLine()}（{CellSheetLogic.AppliedStatusWord(effect)}）"
            : hardDelta < 0 ? "必須違反は減ります" : hardDelta > 0 ? "必須違反は増えます" : null,
        $"{cells}か所を変更",
        newHard is null ? "新たな必須違反は未評価（適用の直前に検査）" : newHard == 0 ? "新たな必須違反なし" : $"新たな必須違反 {newHard}件",
    }.OfType<string>());

    /// <summary>見ている問題の目的の 1 文（一覧から来ない経路で使う。Kotlin <c>previewPurpose</c>）「11/3のA4の不足を減らす」。</summary>
    public static string PreviewPurpose(FixTarget t, string label, string? staffName) =>
        (staffName is null ? "" : $"{staffName}さんの") + $"{label}の{t.GapWord}を減らす";

    /// <summary>
    /// 3 つの組み立て口（候補・玉突き・案の一覧）で共通の仕上げ（Kotlin <c>fillPreview</c>）: <see cref="FixTarget"/> があれば目的と対象の数の変化を埋め
    /// （呼び手が渡していればそのまま）、前後の 1 行を作る。
    /// </summary>
    public static ChainFixPreview FillPreview(ChainFixPreview p, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<string> shiftSymbols, string startDate, IReadOnlyList<string> staffNames)
    {
        var t = p.FixTarget;
        var e = t?.Effect(schedule, p.Suggestion.Ops);
        var label = t is null ? null : CellSheetLogic.FixTargetLabel(t, t.Shift >= 0 && t.Shift < shiftSymbols.Count ? shiftSymbols[t.Shift] : "", startDate);
        var hardDelta = p.Suggestion.Diff.Where(d => MirrorKeys.Hard.Contains(d.Family)).Sum(d => d.Delta);
        return p with
        {
            Purpose = p.Purpose ?? (t is not null && label is not null ? PreviewPurpose(t, label, t.Staff is { } si && si >= 0 && si < staffNames.Count ? staffNames[si] : null) : null),
            Effect = p.Effect.Count > 0 ? p.Effect : t is not null && e is not null && label is not null ? PreviewEffectLines(label, e, t.Basis) : Array.Empty<string>(),
            Summary = PreviewSummaryLine(e, p.Suggestion.Ops.Count, hardDelta, p.NewHard?.Count),
        };
    }
}

/// <summary>変わるセル 1 つ（Kotlin <c>ChangeRow</c>）。Wish＝希望があるときの注記（希望どおり／希望と異なる）、Impact＝その日の人数の変化。
/// StaffKey＝職員 ID（無ければ並び順）＝同名でも別の人として束ねる。Staff は表示名（同名の職員がいればグループ記号を添える）。</summary>
public sealed record ChangeRow(string Staff, string Date, string Before, string After, string Wish = "", string Impact = "", int StaffKey = -1);
