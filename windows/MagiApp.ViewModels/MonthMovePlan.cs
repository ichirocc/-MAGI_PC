using System.Globalization;
using MagiEngine.Model;

namespace MagiApp.ViewModels;

/// <summary>対象の月を移すときに「引き継ぐもの」と「消えるもの」（Kotlin <c>MonthMovePlan.kt</c>、3.643.0。<c>Ws1Ops.ResizeDays</c> と同じ規則で先に数える）。</summary>
public sealed record MonthMovePlan(
    int Year, int Month, int Days,
    int CarriedWishes, int CarriedNeedExceptions, int CarriedPins,
    int DroppedExtWishDays, int DroppedExtWishes, int DroppedPins, int DroppedNeedExceptions)
{
    /// <summary>何も引き継がず何も消えないときだけ確認を省く。</summary>
    public bool NeedsConfirm => CarriedWishes + CarriedNeedExceptions + CarriedPins + DroppedExtWishDays + DroppedPins + DroppedNeedExceptions > 0;

    public static MonthMovePlan Of(MagiState state, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var t = DateTime.DaysInMonth(year, month);
        static int DayOf(string key) { var p = key.Split(','); return p.Length > 1 && int.TryParse(p[1], out var d) ? d : -1; }
        var needKeys = state.NeedDay1.Keys.Concat(state.NeedDay2.Keys).ToList();
        var pins = state.ManualPins ?? Array.Empty<ManualPin>();   // 読込元によって null（未設定）がある
        int extDaysDropped = 0, extDropped = 0;
        foreach (var e in state.ExtWishes ?? Array.Empty<ExtWish>())
        {
            var kept = e.Days.Count(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var pd)
                && pd.DayNumber - first.DayNumber is >= 0 and var j && j < t);
            extDaysDropped += e.Days.Count - kept;
            if (kept == 0 && e.Days.Count > 0) extDropped++;
        }
        return new MonthMovePlan(
            year, month, t,
            CarriedWishes: state.Wishes.Keys.Count(k => DayOf(k) is >= 0 and var d && d < t),
            CarriedNeedExceptions: needKeys.Count(k => DayOf(k) is >= 0 and var d && d < t),
            CarriedPins: pins.Count(p => p.Day >= 0 && p.Day < t),
            DroppedExtWishDays: extDaysDropped, DroppedExtWishes: extDropped,
            DroppedPins: pins.Count(p => p.Day < 0 || p.Day >= t),
            DroppedNeedExceptions: needKeys.Count(k => !(DayOf(k) is >= 0 and var d && d < t)));
    }

    /// <summary>確認ダイアログの本文。引き継ぐ側は「同じ日番号に残る」と明記する（前月 5 日の希望は翌月 5 日に載る）。</summary>
    public IReadOnlyList<string> Lines()
    {
        var carry = new List<string> { "勤務表の中身（同じ日番号に残ります）" };
        if (CarriedWishes > 0) carry.Add($"通常希望 {CarriedWishes} 件（同じ日番号に残ります＝前の月の希望です）");
        if (CarriedNeedExceptions > 0) carry.Add($"必要人数の例外 {CarriedNeedExceptions} 件（同じ日番号）");
        if (CarriedPins > 0) carry.Add($"手動固定 {CarriedPins} 件（同じ日番号）");
        var drop = new List<string>();
        if (DroppedExtWishDays > 0) drop.Add($"期間の外の拡張希望 {DroppedExtWishDays} 日分（日付で持つため{(DroppedExtWishes > 0 ? $"・{DroppedExtWishes} 件は丸ごと" : "")}）");
        if (DroppedPins > 0) drop.Add($"期間の外の手動固定 {DroppedPins} 件");
        if (DroppedNeedExceptions > 0) drop.Add($"期間の外の必要人数の例外 {DroppedNeedExceptions} 件");
        var lines = new List<string> { "引き継ぐ: " + string.Join("・", carry) };
        if (drop.Count > 0) lines.Add("消える: " + string.Join("・", drop));
        return lines;
    }
}
