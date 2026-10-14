using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [3.645.0/仕様 5.3・UX-05] 「相談してから決める」判断。対象（だれの・どの日の・何）と検討内容を持ち、セッション内だけ
/// （state 非保存＝見直し候補と同じ）。出力も判定も止めない＝確認・承認を必須にする範囲は職場の運用で決める（ユーザー決定 2026-10-09）。
/// 対象は氏名と実日付でも持つ＝職員の削除・並び替えや月の移動のあとに別の人・別の日を開かない（3.646.0、外部レビュー B01/B02）。
/// <paramref name="Staff"/>/<paramref name="Day"/> は積んだときの位置（氏名・日付が一致するときの近道）。<paramref name="RosterKey"/> は積んだときの
/// 職員の並び（<see cref="ConsultList.RosterKeyOf"/>、0＝不明）＝同じ名前の職員が複数いるとき、並びが変わっていなければ位置を信じる（3.650.0）。
/// <paramref name="StaffId"/> は積んだときの職員 ID（<see cref="MagiEngine.Model.Staff.Id"/>）＝あれば対象はこれだけで引く（並び替え・改名・同名でも元の人。
/// 見つからなければ開かない）。氏名・<paramref name="RosterKey"/> での引き直しは ID の無い相談だけの後備え。
/// <paramref name="StaffGone"/> は名簿の変更で ID が一度消えた印＝あとで同じ番号が別の人に振られても結ばない（削除した人が最大の ID だと次の追加で再び使われる）。
/// Kotlin <c>ui/ConsultList.kt</c> と 1 対 1。
/// </summary>
public sealed record ConsultItem(
    string Subject, string Note, int? Staff = null, int? Day = null,
    string? StaffName = null, string? Date = null,
    ChainTarget? Chain = null,
    bool Held = false,
    int RosterKey = 0,
    int? StaffId = null,
    bool StaffGone = false,
    ConsultAnswer Answer = ConsultAnswer.Pending);

/// <summary>相談の回答（Kotlin <c>ConsultAnswer</c>、3.665.0 利用者決定「セッション内で回答を記録」）。記録だけ＝了承・不可のどちらでも勤務表は変えない。</summary>
public enum ConsultAnswer { Pending, Agreed, Declined }

/// <summary>複数人の入れ替えの相談が指す枠。日付とシフト記号で持つ＝月の移動・シフトの並び替えのあとも今の勤務表で案を探し直せる。
/// 枠を持たない案は、その案を出したときの盤面と設定の指紋 <paramref name="BoardKey"/>/<paramref name="StateKey"/> を持つ（3.650.0）。指紋が今と違えば当てない。
/// 枠を持たない案（ホーム・分析の「この手を使う」から）は <paramref name="Suggestion"/> を持ち、一覧をもう一度出す（当てるときの照合は適用の門が行う）。</summary>
public sealed record ChainTarget(string? Date, string? ShiftSymbol, string Label, FixSuggestion? Suggestion = null, long BoardKey = 0L, long StateKey = 0L);

public static class ConsultList
{
    /// <summary>回答の表示語。</summary>
    public static string AnswerLabel(ConsultAnswer a) => a switch { ConsultAnswer.Agreed => "了承", ConsultAnswer.Declined => "不可", _ => "回答待ち" };

    /// <summary>押すたびに 回答待ち→了承→不可→回答待ち。</summary>
    public static ConsultAnswer NextAnswer(ConsultAnswer a) => (ConsultAnswer)(((int)a + 1) % 3);

    /// <summary>
    /// いま有効な回答（Kotlin <c>consultAnswerOf</c>）。入れ替えの相談は、回答のあとで勤務表か設定が変わると案の中身が変わる＝以前の回答を流用せず
    /// 「回答待ち」に戻す（仕様 §7）。希望・1 セルの相談は対象が変わらないので記録どおり。
    /// </summary>
    public static ConsultAnswer AnswerOf(ConsultItem c, long boardKey, long stateKey)
    {
        var t = c.Chain;
        if (t is null) return c.Answer;
        return t.BoardKey == 0L || t.BoardKey != boardKey || t.StateKey != stateKey ? ConsultAnswer.Pending : c.Answer;
    }

    /// <summary>回答の 1 行。記録と有効な回答が違えば（案が変わった）その理由を添える。</summary>
    public static string AnswerLine(ConsultItem c, ConsultAnswer effective) =>
        $"回答: {AnswerLabel(effective)}" + (effective != c.Answer ? $"（回答のあとで勤務表か設定が変わり案の中身が変わるため、以前の「{AnswerLabel(c.Answer)}」は使いません）" : "");

    public const string Button = "相談事項に登録";
    public const string Done = "相談中";
    public const string HoldButton = "保留する";
    public const string HeldLabel = "保留中";
    public const string Added = "ホームの「相談中」に登録しました（記録だけで、本人への連絡や了承にはなりません）";
    public const string Duplicate = "この内容は、すでにホームの「相談中」に登録されています";
    public const string Open = "開く";
    public const string Resume = "案を見る";
    public const string Stale = "相談に登録した後で勤務表または設定が変わったため、この案はそのまま当てられません。「直し方を探す」から、もう一度確かめてください";

    /// <summary>ホームの主カードの 1 行。0 件なら出さない。</summary>
    public static string? Line(int n) => n > 0 ? $"未確認事項 {n} 件（相談中。下の一覧で確認してから配ってください）" : null;

    /// <summary>相談中と保留を分けて言う（どちらも未解決＝違反は減らさない）。</summary>
    public static string? Line(IReadOnlyList<ConsultItem> items)
    {
        if (items.Count == 0) return null;
        var held = items.Count(c => c.Held);
        var open = items.Count - held;
        var parts = string.Join("・", new[] { open > 0 ? $"相談中 {open}" : null, held > 0 ? $"保留 {held}" : null }.Where(x => x is not null));
        return $"未確認事項 {items.Count} 件（{parts}。下の一覧で確認してから配ってください）";
    }

    private static DateOnly? Parse(string date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string? IsoDate(string startDate, int day) =>
        Parse(startDate)?.AddDays(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary><paramref name="date"/> が今の期間の何日目か（期間の外・読めない日付は null）。</summary>
    public static int? DayIndexOf(string startDate, string date, int days)
    {
        if (Parse(startDate) is not { } s || Parse(date) is not { } d) return null;
        var j = d.DayNumber - s.DayNumber;
        return j >= 0 && j < days ? j : null;
    }

    private static string ShortDate(string date) => Parse(date) is { } d ? $"{d.Month}/{d.Day}" : date;

    /// <summary>職員の並びの指紋（同じ名前の職員を位置で区別してよいかの判定に使う。0 は「不明」に取っておく）。Kotlin の
    /// <c>rosterKeyOf</c>＝<c>List.hashCode()</c> と同じ値（<see cref="EditPick.RosterOf"/>）。</summary>
    public static int RosterKeyOf(IReadOnlyList<string> staffNames) => EditPick.RosterOf(staffNames) is var h && h != 0 ? h : 1;

    /// <summary>相談の職員を今の一覧で探す。職員 ID があればそれだけで（今の一覧に無ければ null＝別の人に結ばない）。
    /// ID が無ければ氏名で（積んだときの位置が同じ氏名なら近道）、氏名も無ければ位置だけ。
    /// 同じ名前が複数いれば氏名では区別できない＝積んだときと並びが同じときだけ位置を信じ、それ以外は開かない（3.650.0）。</summary>
    public static int? ConsultStaff(ConsultItem c, IReadOnlyList<string> staffNames, IReadOnlyList<int>? staffIds = null)
    {
        if (c.StaffGone) return null;
        if (c.StaffId is { } id && staffIds is { Count: > 0 })
        {
            for (var i = 0; i < staffIds.Count; i++) if (staffIds[i] == id) return i;
            return null;
        }
        if (c.StaffName is not { } name) return c.Staff is { } s && s >= 0 && s < staffNames.Count ? s : null;
        if (staffNames.Count(x => x == name) > 1)
            return c.Staff is { } a && c.RosterKey != 0 && c.RosterKey == RosterKeyOf(staffNames) && a >= 0 && a < staffNames.Count && staffNames[a] == name ? a : null;
        if (c.Staff is { } at && at >= 0 && at < staffNames.Count && staffNames[at] == name) return at;
        for (var i = 0; i < staffNames.Count; i++) if (staffNames[i] == name) return i;
        return null;
    }

    /// <summary>相談の日を今の期間で探す。日付があればそれで、無ければ日番号だけ。</summary>
    public static int? ConsultDay(ConsultItem c, string startDate, int days)
    {
        if (c.Date is not { } date) return c.Day is { } d && d >= 0 && d < days ? d : null;
        return DayIndexOf(startDate, date, days);
    }

    /// <summary>相談の対象セル（職員, 日）。氏名が今の一覧になければ null、日付が今の期間の外なら null＝「開く」を出さない。</summary>
    public static (int I, int J)? ConsultCell(ConsultItem c, string startDate, IReadOnlyList<string> staffNames, int days, IReadOnlyList<int>? staffIds = null)
    {
        if (ConsultStaff(c, staffNames, staffIds) is not { } i) return null;
        if (ConsultDay(c, startDate, days) is not { } j) return null;
        return (i, j);
    }

    /// <summary>入れ替えの相談の枠（日, シフト）。日付が期間の外・記号が今のシフトに無ければ null。</summary>
    public static (int J, int K)? ConsultChainTarget(ChainTarget t, string startDate, IReadOnlyList<string> shiftSymbols, int days)
    {
        if (t.Date is not { } date || DayIndexOf(startDate, date, days) is not { } j) return null;
        if (t.ShiftSymbol is not { } sym) return null;
        for (var k = 0; k < shiftSymbols.Count; k++) if (shiftSymbols[k] == sym) return (j, k);
        return null;
    }

    /// <summary>積むときに対象の職員 ID を付ける（位置と氏名が今の一覧と合うときだけ。ID の無いデータ＝0 は付けない）。</summary>
    public static ConsultItem StampStaffId(ConsultItem c, IReadOnlyList<string> staffNames, IReadOnlyList<int> staffIds)
    {
        if (c.Staff is not { } at || c.StaffId is not null || c.Chain is not null) return c;
        if (c.StaffName is not null && (at < 0 || at >= staffNames.Count || staffNames[at] != c.StaffName)) return c;
        if (at < 0 || at >= staffIds.Count || staffIds[at] <= 0) return c;
        return c with { StaffId = staffIds[at] };
    }

    /// <summary>
    /// 名簿が変わったら相談の表示用の氏名と位置を今に合わせる。ID を持つ相談は ID で引き、見つかれば今の氏名・位置へ
    /// （削除で見つからなければ <see cref="ConsultItem.StaffGone"/>＝以後は開かず、元の氏名で案内する）。
    /// ID の無い相談だけ [3.651.0] の改名の後備え: 人数が同じで 1 か所だけ名前が変わった＝改名とみなし、改名前の名簿で引けた相談を
    /// 同じ位置の今の名前と今の名簿の指紋に書き換える（並び替えは 2 か所以上、追加・削除は人数が変わる＝氏名で引き直す）。
    /// </summary>
    public static IReadOnlyList<ConsultItem> FollowRenameInConsults(IReadOnlyList<ConsultItem> items, IReadOnlyList<string> before, IReadOnlyList<string> after, IReadOnlyList<int>? afterIds = null)
    {
        if (items.Count == 0) return items;
        var renamed = before.Count == after.Count && Enumerable.Range(0, before.Count).Count(i => before[i] != after[i]) == 1;
        var roster = RosterKeyOf(after);
        return items.Select(c =>
        {
            if (c.StaffGone) return c;
            if (c.StaffId is { } id && afterIds is { Count: > 0 })
            {
                var at = -1;
                for (var i = 0; i < afterIds.Count; i++) if (afterIds[i] == id) { at = i; break; }
                if (at < 0) return c with { StaffGone = true };
                return c.Staff == at && c.StaffName == after[at] ? c : c with { StaffName = after[at], Staff = at };
            }
            if (renamed && c.StaffName is not null && ConsultStaff(c, before) is { } bt)
                return c with { StaffName = after[bt], Staff = bt, RosterKey = roster };
            return c;
        }).ToList();
    }

    /// <summary>積んだ案が今の勤務表・設定で出したものと違うか（指紋が無い・違う＝当てずに探し直しを促す）。枠を持つ相談は探し直すので関係しない。</summary>
    public static bool ConsultChainStale(ChainTarget t, long boardKey, long stateKey) =>
        t.Suggestion is not null && (t.BoardKey == 0L || t.BoardKey != boardKey || t.StateKey != stateKey);

    /// <summary>入れ替えの相談を今の勤務表で見直せるか（枠を引き直せる、または案そのものを持つ）。</summary>
    public static bool ConsultChainResumable(ChainTarget t, string startDate, IReadOnlyList<string> shiftSymbols, int days) =>
        ConsultChainTarget(t, startDate, shiftSymbols, days) is not null || t.Suggestion is not null;

    /// <summary>「開く」「案を見る」を出せない理由（対象をもともと持たない相談は null＝何も出さない）。</summary>
    public static string? ConsultTargetNote(ConsultItem c, string startDate, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, int days, IReadOnlyList<int>? staffIds = null)
    {
        if (c.Chain is { } t)
        {
            if (ConsultChainResumable(t, startDate, shiftSymbols, days)) return null;
            return $"いまの期間・シフトにない枠なので、案を見られません（{(t.Date is { } d ? ShortDate(d) : "?")} の「{t.ShiftSymbol ?? "?"}」）";
        }
        if (c.Staff is null && c.StaffName is null && c.Day is null && c.Date is null) return null;
        if (ConsultCell(c, startDate, staffNames, days, staffIds) is not null) return null;
        if (ConsultStaff(c, staffNames, staffIds) is null)
        {
            var byId = c.StaffGone || (c.StaffId is not null && staffIds is { Count: > 0 });
            if (!byId && c.StaffName is { } dn && staffNames.Count(x => x == dn) > 1) return $"同じ名前の職員が複数いて、どの人か決められないので開けません（{dn}さん）";
            return "いまの職員一覧にいないので開けません" + (c.StaffName is { } n ? $"（{n}さん）" : "");
        }
        return "いまの期間にない日なので開けません" + (c.Date is { } dd ? $"（{ShortDate(dd)}）" : "");
    }

    /// <summary>ぶつかっている希望の行から: 希望を調整するか勤務を変えるかの判断。</summary>
    public static ConsultItem Wish(string name, string dayLabel, string? symbol, string reason, int staff, int day, string? date = null) =>
        new($"{name} {dayLabel} の希望" + (symbol is null ? "" : $"「{symbol}」"), $"希望を調整するか勤務を変えるか: {reason}", staff, day, StaffName: name, Date: date);

    /// <summary>なおし方（1 人を入れる）の枠から: だれを入れるかの判断。</summary>
    public static ConsultItem Shortage(string dayLabel, string symbol, IReadOnlyList<string> names) => new(
        $"{dayLabel} の「{symbol}」の人員不足",
        names.Count == 0 ? "入れる人を相談" : "だれかを入れる: " + string.Join("・", names.Take(5)) + (names.Count > 5 ? $" ほか{names.Count - 5}人" : ""));

    /// <summary>複数人の入れ替えの一覧から: 変わる人と勤務（必須の増減は画面と同じ <see cref="NextActionGuide.FixImpactLines"/> の 1 行目）。
    /// 枠を持てば、あとで今の勤務表で案を探し直せる。</summary>
    public static ConsultItem Chain(ChainFixPreview p, string hardLine) => new(p.Suggestion.Label, string.Join("／", p.Changes) + $"（{hardLine}）", Chain: p.Target);

    /// <summary>回数不足の相談事項（増やす案が見つからないとき。Kotlin <c>consultCountShortage</c>）。</summary>
    public static ConsultItem CountShortage(string name, string sym, string head, int staff) =>
        new($"{name}さんの「{sym}」の回数", $"{head}。条件を守って増やせる案が見つからない＝追加候補日と変更を妨げる条件を確認", staff, StaffName: name);

    /// <summary>セルの変更の確認の画面から（当てずに相談してから決める。Kotlin <c>consultCellChange</c>）。</summary>
    public static ConsultItem CellChange(CellChangeReview r, int staff, int day, string staffName, string? date) =>
        new(r.Change, string.Join("・", r.ThisCell.Concat(r.Appeared.Select(x => $"新しく生じる: {x}"))), staff, day, StaffName: staffName, Date: date);

    /// <summary>直す 1 手から。</summary>
    public static ConsultItem Fix(FixSuggestion s, string hardLine, string? caution) => new(s.Label, hardLine + (caution is null ? "" : $"・{caution}"));

    /// <summary>つくる前の確認の行から: 残る項目を希望か設定のどちらで解くか。</summary>
    public static ConsultItem PreRun(PreRunRow row, string? staffName = null, string? date = null) =>
        new(row.Text, "何度つくっても残る項目。希望か設定のどちらを変えるかを相談", row.Staff, row.Day, StaffName: staffName, Date: date);

    /// <summary>すでに一覧にあるか（ボタンを「相談中」にして形で返す）。</summary>
    public static bool IsConsulted(IReadOnlyList<ConsultItem> list, ConsultItem item) => list.Any(c => c.Subject == item.Subject && c.Note == item.Note);

    /// <summary>積んだ同じ項目（相談中か保留かを札に出す）。</summary>
    public static ConsultItem? Match(IReadOnlyList<ConsultItem> list, ConsultItem item) => list.FirstOrDefault(c => c.Subject == item.Subject && c.Note == item.Note);

    /// <summary>同じ対象・内容は 2 度積まない（null＝重複）。</summary>
    public static IReadOnlyList<ConsultItem>? Add(IReadOnlyList<ConsultItem> list, ConsultItem item) =>
        IsConsulted(list, item) ? null : list.Append(item).ToList();
}
