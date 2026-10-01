namespace MagiEngine.Model;

/// <summary>[#41] 手動固定の付け外し・手の編集への追従（Kotlin <c>MagiState.pinAt</c>/<c>withPinsFollowing</c>/<c>togglePin</c> の写し）。</summary>
public static class ManualPins
{
    public static IReadOnlyList<ManualPin> PinsOf(this MagiState s) => s.ManualPins ?? Array.Empty<ManualPin>();

    public static ManualPin? PinAt(this MagiState s, int i, int j) => s.PinsOf().FirstOrDefault(p => p.Staff == i && p.Day == j);

    /// <summary>手の編集で <paramref name="cells"/> が <paramref name="shift"/> になったとき、固定されたセルの値を追従させる（固定は残す）。固定に当たらなければ同じ state。<paramref name="shift"/> がシフト範囲外なら固定は外す。</summary>
    public static MagiState WithPinsFollowing(this MagiState s, IEnumerable<(int, int)> cells, int shift)
    {
        var pins = s.PinsOf();
        if (pins.Count == 0) return s;
        var set = cells.ToHashSet();
        if (!pins.Any(p => set.Contains((p.Staff, p.Day)) && p.Shift != shift)) return s;
        if (shift < 0 || shift >= s.ShiftCount) return s with { ManualPins = pins.Where(p => !set.Contains((p.Staff, p.Day))).ToList() };
        return s with { ManualPins = pins.Select(p => set.Contains((p.Staff, p.Day)) ? p with { Shift = shift } : p).ToList() };
    }

    /// <summary>盤面ごと置き換わった（CSV 取込）とき、<paramref name="old"/> から <paramref name="@new"/> で値が変わったセルの固定を新しい値へ追従させる。
    /// 新しい値が -1／シフト範囲外なら固定は外す（<see cref="WithPinsFollowing"/> と同じ規約）。変わらないセルの固定はそのまま。件数＝追従または外した固定の数（Kotlin <c>withPinsFollowingBoard</c>）。</summary>
    public static (MagiState State, int Count) WithPinsFollowingBoard(this MagiState s, int[][] old, int[][] @new)
    {
        var pins = s.PinsOf();
        if (pins.Count == 0) return (s, 0);
        var n = 0;
        var outPins = new List<ManualPin>(pins.Count);
        foreach (var m in pins)
        {
            int? was = m.Staff >= 0 && m.Staff < old.Length && m.Day >= 0 && m.Day < old[m.Staff].Length ? old[m.Staff][m.Day] : null;
            int? now = m.Staff >= 0 && m.Staff < @new.Length && m.Day >= 0 && m.Day < @new[m.Staff].Length ? @new[m.Staff][m.Day] : null;
            if (now is null || now == was) { outPins.Add(m); continue; }
            n++;
            if (now >= 0 && now < s.ShiftCount) outPins.Add(m with { Shift = now.Value });
        }
        return n == 0 ? (s, 0) : (s with { ManualPins = outPins }, n);
    }

    /// <summary>セル (i,j) を <paramref name="shift"/> で固定する／固定を外す（トグル）。</summary>
    public static MagiState TogglePin(this MagiState s, int i, int j, int shift) =>
        s.PinAt(i, j) != null
            ? s with { ManualPins = s.PinsOf().Where(p => !(p.Staff == i && p.Day == j)).ToList() }
            : s with { ManualPins = s.PinsOf().Append(new ManualPin(i, j, shift)).ToList() };
}
