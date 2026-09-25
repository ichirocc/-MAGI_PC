using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// 表示色 <c>ShiftColors</c> は記号がキー＝改名は付け替え、削除は取り除く（色が既定へ戻る・孤児キーが残る・
/// 同じ記号で作り直したシフトが黙って引き継ぐ、を防ぐ）。<c>Ws1OpsShiftColorTest.kt</c> の移植。
/// </summary>
public class Ws1OpsShiftColorTest
{
    private static MagiState Load() => StateJsonSerializer.Parse(FixtureLoader.ReadRaw("golden_state.json"));

    private static int[][] Grid(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();

    private static MagiState Rename(MagiState st, string from, string to)
    {
        var k = st.Shifts.ToList().FindIndex(s => s.Kigou == from);
        var sh = st.Shifts[k];
        return Ws1Ops.EditShift(st, k, sh.Name, to, sh.Need1, sh.Need2, sh.Role == ShiftRole.Rest);
    }

    private static Dictionary<string, string> Without(IReadOnlyDictionary<string, string> m, string key) =>
        m.Where(kv => kv.Key != key).ToDictionary(kv => kv.Key, kv => kv.Value);

    [Fact]
    public void RenameMovesTheCustomColour()
    {
        var st = Load();
        var c = st.ShiftColors["Dﾃ"];
        var after = Rename(st, "Dﾃ", "D");
        Assert.Equal(c, after.ShiftColors["D"]);
        Assert.False(after.ShiftColors.ContainsKey("Dﾃ"));   // 旧記号の孤児キーを残さない
        Assert.Equal(Without(st.ShiftColors, "Dﾃ"), Without(after.ShiftColors, "D"));   // 他のシフトの色は不変
    }

    [Fact]
    public void RenameDropsAnOrphanColourUnderTheNewSymbol()
    {
        var st0 = Load();
        var st = st0 with { ShiftColors = new Dictionary<string, string>(Without(st0.ShiftColors, "Pﾅ")) { ["X"] = "#123456" } };
        var after = Rename(st, "Pﾅ", "X");
        Assert.False(after.ShiftColors.ContainsKey("X"));   // 色の無いシフトが孤児の色を引き継がない
    }

    /// <summary>Kotlin は状態の等価で固定するが、C# の record はリストを参照で比べる＝画面の no-op 判定
    /// （<c>MagiViewModel.Ws1EditShift</c>）はシフトの値で見る。ここでは色とシフトの値が変わらないことを固定する。</summary>
    [Fact]
    public void UnchangedEditKeepsColoursAndShifts()
    {
        var st = Load();
        var after = Rename(st, "Dﾃ", "Dﾃ");
        Assert.Same(st.ShiftColors, after.ShiftColors);
        Assert.Equal(st.Shifts, after.Shifts);
    }

    [Fact]
    public void RemoveShiftDropsItsColour()
    {
        var st = Load();
        var k = st.Shifts.ToList().FindIndex(s => s.Kigou == "A4");
        var after = Ws1Ops.RemoveShift(st, Grid(st), k).State;
        Assert.False(after.ShiftColors.ContainsKey("A4"));
        Assert.Equal(Without(st.ShiftColors, "A4"), after.ShiftColors);
    }
}
