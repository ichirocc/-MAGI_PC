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
/// Kotlin <c>ui/ConsultList.kt</c> と 1 対 1。
/// </summary>
public sealed record ConsultItem(
    string Subject, string Note, int? Staff = null, int? Day = null,
    string? StaffName = null, string? Date = null,
    ChainTarget? Chain = null,
    int RosterKey = 0);

/// <summary>複数人の入れ替えの相談が指す枠。日付とシフト記号で持つ＝月の移動・シフトの並び替えのあとも今の勤務表で案を探し直せる。
/// 枠を持たない案は、その案を出したときの盤面と設定の指紋 <paramref name="BoardKey"/>/<paramref name="StateKey"/> を持つ（3.650.0）。指紋が今と違えば当てない。
/// 枠を持たない案（ホーム・分析の「この手を使う」から）は <paramref name="Suggestion"/> を持ち、一覧をもう一度出す（当てるときの照合は適用の門が行う）。</summary>
public sealed record ChainTarget(string? Date, string? ShiftSymbol, string Label, FixSuggestion? Suggestion = null, long BoardKey = 0L, long StateKey = 0L);

public static class ConsultList
{
    public const string Button = "相談してから決める";
    public const string Done = "相談中";
    public const string Added = "相談中に追加しました";
    public const string Duplicate = "すでに相談中にあります";
    public const string Open = "開く";
    public const string Resume = "案を見る";
    public const string Stale = "相談に積んだあとで勤務表か設定が変わったため、この案はそのままでは使えません。「直し方を探す」で探し直してください";

    /// <summary>ホームの主カードの 1 行。0 件なら出さない。</summary>
    public static string? Line(int n) => n > 0 ? $"未確認事項 {n} 件（相談中。下の一覧で確認してから配ってください）" : null;

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

    /// <summary>相談の職員を今の一覧で探す。氏名があればそれで（積んだときの位置が同じ氏名なら近道）、無ければ位置だけ。
    /// 同じ名前が複数いれば氏名では区別できない＝積んだときと並びが同じときだけ位置を信じ、それ以外は開かない（3.650.0）。</summary>
    public static int? ConsultStaff(ConsultItem c, IReadOnlyList<string> staffNames)
    {
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
    public static (int I, int J)? ConsultCell(ConsultItem c, string startDate, IReadOnlyList<string> staffNames, int days)
    {
        if (ConsultStaff(c, staffNames) is not { } i) return null;
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

    /// <summary>
    /// [3.651.0/外部レビュー] 職員の改名に相談の対象を追従させる（職員 ID はデータ項目に無いので、名簿が変わった時点で氏名を書き換える）。
    /// 人数が同じで 1 か所だけ名前が変わった＝改名とみなす（並び替えは 2 か所以上、追加・削除は人数が変わる＝氏名で引き直す既存の規則のまま）。
    /// 改名前の名簿で引けた相談は、同じ位置の今の名前と今の名簿の指紋に書き換える（改名で位置は動かない＝別の人の改名でも同名の区別を保つ）。
    /// </summary>
    public static IReadOnlyList<ConsultItem> FollowRenameInConsults(IReadOnlyList<ConsultItem> items, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        if (items.Count == 0 || before.Count != after.Count || Enumerable.Range(0, before.Count).Count(i => before[i] != after[i]) != 1) return items;
        var roster = RosterKeyOf(after);
        return items.Select(c => c.StaffName is not null && ConsultStaff(c, before) is { } at
            ? c with { StaffName = after[at], Staff = at, RosterKey = roster } : c).ToList();
    }

    /// <summary>積んだ案が今の勤務表・設定で出したものと違うか（指紋が無い・違う＝当てずに探し直しを促す）。枠を持つ相談は探し直すので関係しない。</summary>
    public static bool ConsultChainStale(ChainTarget t, long boardKey, long stateKey) =>
        t.Suggestion is not null && (t.BoardKey == 0L || t.BoardKey != boardKey || t.StateKey != stateKey);

    /// <summary>入れ替えの相談を今の勤務表で見直せるか（枠を引き直せる、または案そのものを持つ）。</summary>
    public static bool ConsultChainResumable(ChainTarget t, string startDate, IReadOnlyList<string> shiftSymbols, int days) =>
        ConsultChainTarget(t, startDate, shiftSymbols, days) is not null || t.Suggestion is not null;

    /// <summary>「開く」「案を見る」を出せない理由（対象をもともと持たない相談は null＝何も出さない）。</summary>
    public static string? ConsultTargetNote(ConsultItem c, string startDate, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, int days)
    {
        if (c.Chain is { } t)
        {
            if (ConsultChainResumable(t, startDate, shiftSymbols, days)) return null;
            return $"いまの期間・シフトにない枠です（{(t.Date is { } d ? ShortDate(d) : "?")} の「{t.ShiftSymbol ?? "?"}」）";
        }
        if (c.Staff is null && c.StaffName is null && c.Day is null && c.Date is null) return null;
        if (ConsultCell(c, startDate, staffNames, days) is not null) return null;
        if (ConsultStaff(c, staffNames) is null)
        {
            if (c.StaffName is { } dn && staffNames.Count(x => x == dn) > 1) return $"同じ名前の職員が複数いて、どの人か決められません（{dn}）";
            return "いまの職員一覧にいません" + (c.StaffName is { } n ? $"（{n}）" : "");
        }
        return "いまの期間にない日です" + (c.Date is { } dd ? $"（{ShortDate(dd)}）" : "");
    }

    /// <summary>ぶつかっている希望の行から: 希望を取り消すか勤務を変えるかの判断。</summary>
    public static ConsultItem Wish(string name, string dayLabel, string? symbol, string reason, int staff, int day, string? date = null) =>
        new($"{name} {dayLabel} の希望" + (symbol is null ? "" : $"「{symbol}」"), $"取り消すか勤務を変えるか: {reason}", staff, day, StaffName: name, Date: date);

    /// <summary>なおし方（1 人を入れる）の枠から: だれを入れるかの判断。</summary>
    public static ConsultItem Shortage(string dayLabel, string symbol, IReadOnlyList<string> names) => new(
        $"{dayLabel} の「{symbol}」の人員不足",
        names.Count == 0 ? "入れる人を相談" : "だれかを入れる: " + string.Join("・", names.Take(5)) + (names.Count > 5 ? $" ほか{names.Count - 5}人" : ""));

    /// <summary>複数人の入れ替えの一覧から: 変わる人と勤務（必須の増減は画面と同じ <see cref="NextActionGuide.FixImpactLines"/> の 1 行目）。
    /// 枠を持てば、あとで今の勤務表で案を探し直せる。</summary>
    public static ConsultItem Chain(ChainFixPreview p, string hardLine) => new(p.Suggestion.Label, string.Join("／", p.Changes) + $"（{hardLine}）", Chain: p.Target);

    /// <summary>直す 1 手から。</summary>
    public static ConsultItem Fix(FixSuggestion s, string hardLine, string? caution) => new(s.Label, hardLine + (caution is null ? "" : $"・{caution}"));

    /// <summary>つくる前の確認の行から: 残る項目を希望か設定のどちらで解くか。</summary>
    public static ConsultItem PreRun(PreRunRow row, string? staffName = null, string? date = null) =>
        new(row.Text, "何度つくっても残る項目。希望か設定のどちらを変えるかを相談", row.Staff, row.Day, StaffName: staffName, Date: date);

    /// <summary>すでに一覧にあるか（ボタンを「相談中」にして形で返す）。</summary>
    public static bool IsConsulted(IReadOnlyList<ConsultItem> list, ConsultItem item) => list.Any(c => c.Subject == item.Subject && c.Note == item.Note);

    /// <summary>同じ対象・内容は 2 度積まない（null＝重複）。</summary>
    public static IReadOnlyList<ConsultItem>? Add(IReadOnlyList<ConsultItem> list, ConsultItem item) =>
        IsConsulted(list, item) ? null : list.Append(item).ToList();
}
