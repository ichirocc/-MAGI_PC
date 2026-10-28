namespace MagiApp.ViewModels;

/// <summary>日の詳細の 1 行の状態。必要人数の無いシフトは None（人数だけ出す）。</summary>
public enum DaySlotState { Under, Over, Ok, None }

/// <summary>
/// [Android 3.727.1] 日の詳細（日ヘッダのシート）のシフト 1 行（Kotlin <c>DaySlotRow</c>）: その日にそのシフトへ入っている職員と、必要人数との対比。
/// Need・Fit は必要人数と適正人数（<c>NeedCellLimits</c> と同じ値。人員の印の判定と同じ源）。
/// </summary>
public sealed record DaySlotRow(int Shift, string Symbol, IReadOnlyList<int> Staff, int? Need, int? Fit)
{
    public int Got => Staff.Count;

    public DaySlotState State =>
        Need is not { } n ? DaySlotState.None
        : Got < n ? DaySlotState.Under
        : Fit is { } f && Got > f ? DaySlotState.Over
        : DaySlotState.Ok;

    /// <summary>例「Pｼ　3/3人　充足」「Dﾃ　1/2人　1人不足」「Aｱ　3人（適正 2人）　1人多い」「休　4人」。</summary>
    public string Line => State switch
    {
        DaySlotState.Under => $"{Symbol}　{Got}/{Need}人　{Need - Got}人不足",
        DaySlotState.Over => $"{Symbol}　{Got}人（適正 {Fit}人）　{Got - Fit}人多い",
        DaySlotState.Ok => $"{Symbol}　{Got}/{Need}人　充足",
        _ => $"{Symbol}　{Got}人",
    };
}

/// <summary>日の詳細（Kotlin <c>DayDetail.kt</c>）。</summary>
public static class DayDetail
{
    /// <summary>日ヘッダのタップの読み上げ（どの日でも開く）。</summary>
    public const string DayDetailOpen = "この日の配置";

    /// <summary>日 <paramref name="day"/> のシフトごとの行。誰か入っているか必要人数があるシフトだけを、シフトの並び順に出す。</summary>
    public static IReadOnlyList<DaySlotRow> DaySlotRows(IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<string> symbols, int day,
        Func<int, int, (int Lo, int Hi)?> limits) =>
        Enumerable.Range(0, symbols.Count).Select(k =>
        {
            var staff = Enumerable.Range(0, schedule.Count).Where(i => day >= 0 && day < schedule[i].Count && schedule[i][day] == k).ToList();
            var lim = limits(k, day);
            return staff.Count == 0 && lim is null ? null : new DaySlotRow(k, symbols[k], staff, lim?.Lo, lim?.Hi);
        }).OfType<DaySlotRow>().ToList();

    /// <summary>シートの日送りの先（範囲外は null＝ボタンを出さない）。</summary>
    public static int? DayStep(int day, int delta, int days) => day + delta is var d && d >= 0 && d < days ? d : null;
}
