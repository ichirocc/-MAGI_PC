using MagiEngine.Model;

namespace MagiApp.ViewModels;

/// <summary>制約の入力値の検査と、取込で受理した期間超えの行の警告。Kotlin <c>MagiConstraintsView.kt</c> の該当関数の移植
/// （C# の制約ダイアログ全体の検査は <see cref="MagiViewModel.ConstraintInputError"/>）。</summary>
public static class MagiConstraintsView
{
    private static int? IntOrNull(string s) => int.TryParse(s.Trim(), out var n) ? n : null;

    /// <summary>ダイアログで確定できない理由（null＝確定可）。空欄は理由を出さない。<paramref name="dayCount"/>=0（勤務表なし）は期間の上限を見ない。</summary>
    public static string? Cons1InputError(string d1, string d2, int dayCount)
    {
        var days = IntOrNull(d1); var need = IntOrNull(d2);
        var maxDays = dayCount > 0 ? dayCount : int.MaxValue;
        if (days is { } dd && (dd < 1 || dd > maxDays))
            return dayCount > 0 ? $"「何日間」は 1〜{dayCount} で入れてください（勤務表は {dayCount} 日です）" : "「何日間」は 1 以上で入れてください";
        if (need is { } nn && nn < 1) return "「必要数」は 1 以上で入れてください";
        if (days is { } a && need is { } b && b > a) return "「必要数」は「何日間」以下にしてください（超えると必ず違反になります）";
        return null;
    }

    public static string? Cons2InputError(string count, int dayCount = 0)
    {
        if (IntOrNull(count) is not { } c) return null;
        if (c < 1) return "合計は 1 以上で入れてください";
        if (dayCount >= 1 && dayCount < c) return $"合計は 1〜{dayCount} で入れてください（勤務表は {dayCount} 日です）";
        return null;
    }

    /// <summary>[backlog #42(h)⑥] 取込（CSV・JSON）で受理した行のうち、期間を超える期間の制約（何日間）と個人の合計。
    /// 取込は断らず（データの互換）、エディタと同じ文言で警告だけ返す。</summary>
    public static List<string> PeriodOverflowWarnings(MagiState st)
    {
        var t = st.DayCount;
        var outList = new List<string>();
        if (t <= 0) return outList;
        foreach (var r in st.Cons1)
        {
            if (IntOrNull(r.Day1) is not { } d) continue;
            if (d > t) outList.Add($"期間の制約「{d}日間に{r.ShiftKigou} {r.Day2.Trim()}回以上」：" + Cons1InputError(r.Day1, "", t));
        }
        foreach (var r in st.Cons2)
        {
            if (Cons2InputError(r.Count, t) is not { } err) continue;
            if ((IntOrNull(r.Count) ?? 0) > t) outList.Add($"個人の合計「{r.ShiftKigou} {r.Count.Trim()}回」：{err}");
        }
        return outList;
    }

    /// <summary>取込の完了メッセージの末尾（警告なしは空）。3 件まで並べ、残りは件数。</summary>
    public static string PeriodOverflowNote(MagiState st)
    {
        var w = PeriodOverflowWarnings(st);
        if (w.Count == 0) return "";
        return $"｜⚠ 期間を超える行が{w.Count}件あります（取り込みましたが満たせません）: " + string.Join(" ／ ", w.Take(3)) + (w.Count > 3 ? " ほか" : "");
    }
}
