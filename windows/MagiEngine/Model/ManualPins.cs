namespace MagiEngine.Model;

/// <summary>[#41] 手動固定の付け外し・手の編集への追従（Kotlin <c>MagiState.pinAt</c>/<c>withPinsFollowing</c>/<c>togglePin</c> の写し）。</summary>
public static class ManualPins
{
    public static IReadOnlyList<ManualPin> PinsOf(this MagiState s) => s.ManualPins ?? Array.Empty<ManualPin>();

    public static ManualPin? PinAt(this MagiState s, int i, int j) => s.PinsOf().FirstOrDefault(p => p.Staff == i && p.Day == j);

    /// <summary>手の編集で <paramref name="cells"/> が <paramref name="shift"/> になったとき、固定されたセルの値を追従させる（固定は残す）。固定に当たらなければ同じ state。</summary>
    public static MagiState WithPinsFollowing(this MagiState s, IEnumerable<(int, int)> cells, int shift)
    {
        var pins = s.PinsOf();
        if (pins.Count == 0) return s;
        var set = cells.ToHashSet();
        if (!pins.Any(p => set.Contains((p.Staff, p.Day)) && p.Shift != shift)) return s;
        return s with { ManualPins = pins.Select(p => set.Contains((p.Staff, p.Day)) ? p with { Shift = shift } : p).ToList() };
    }

    /// <summary>セル (i,j) を <paramref name="shift"/> で固定する／固定を外す（トグル）。</summary>
    public static MagiState TogglePin(this MagiState s, int i, int j, int shift) =>
        s.PinAt(i, j) != null
            ? s with { ManualPins = s.PinsOf().Where(p => !(p.Staff == i && p.Day == j)).ToList() }
            : s with { ManualPins = s.PinsOf().Append(new ManualPin(i, j, shift)).ToList() };
}
