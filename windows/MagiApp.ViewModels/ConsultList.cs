using System.Collections.Generic;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [3.645.0/仕様 5.3・UX-05] 「相談してから決める」判断。対象（だれの・どの日の・何）と検討内容を持ち、セッション内だけ
/// （state 非保存＝見直し候補と同じ）。出力も判定も止めない＝確認・承認を必須にする範囲は職場の運用で決める（ユーザー決定 2026-10-09）。
/// Kotlin <c>ui/ConsultList.kt</c> と 1 対 1。
/// </summary>
public sealed record ConsultItem(string Subject, string Note, int? Staff = null, int? Day = null);

public static class ConsultList
{
    public const string Button = "相談してから決める";
    public const string Added = "相談中に追加しました";
    public const string Duplicate = "すでに相談中にあります";

    /// <summary>ホームの主カードの 1 行。0 件なら出さない。</summary>
    public static string? Line(int n) => n > 0 ? $"未確認事項 {n} 件（相談中。下の一覧で確認してから配ってください）" : null;

    /// <summary>ぶつかっている希望の行から: 希望を取り消すか勤務を変えるかの判断。</summary>
    public static ConsultItem Wish(string name, string dayLabel, string? symbol, string reason, int staff, int day) =>
        new($"{name} {dayLabel} の希望" + (symbol is null ? "" : $"「{symbol}」"), $"取り消すか勤務を変えるか: {reason}", staff, day);

    /// <summary>なおし方（1 人を入れる）の枠から: だれを入れるかの判断。</summary>
    public static ConsultItem Shortage(string dayLabel, string symbol, IReadOnlyList<string> names) => new(
        $"{dayLabel} の「{symbol}」の人員不足",
        names.Count == 0 ? "入れる人を相談" : "だれかを入れる: " + string.Join("・", names.Take(5)) + (names.Count > 5 ? $" ほか{names.Count - 5}人" : ""));

    /// <summary>複数人の入替の一覧から: 変わる人と勤務（必須の増減は画面と同じ <see cref="NextActionGuide.FixImpactLines"/> の 1 行目）。</summary>
    public static ConsultItem Chain(ChainFixPreview p, string hardLine) => new(p.Suggestion.Label, string.Join("／", p.Changes) + $"（{hardLine}）");

    /// <summary>直す 1 手から。</summary>
    public static ConsultItem Fix(FixSuggestion s, string hardLine, string? caution) => new(s.Label, hardLine + (caution is null ? "" : $"・{caution}"));

    /// <summary>つくる前の確認の行から: 残る項目を希望か設定のどちらで解くか。</summary>
    public static ConsultItem PreRun(PreRunRow row) => new(row.Text, "何度つくっても残る項目。希望か設定のどちらを変えるかを相談", row.Staff, row.Day);

    /// <summary>同じ対象・内容は 2 度積まない（null＝重複）。</summary>
    public static IReadOnlyList<ConsultItem>? Add(IReadOnlyList<ConsultItem> list, ConsultItem item) =>
        list.Any(c => c.Subject == item.Subject && c.Note == item.Note) ? null : list.Append(item).ToList();
}
