using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range —
// see the same alias pattern already established in TestSupport/MinimalState.cs.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ9] <c>StateFingerprintTest.kt</c> の逐語移植。指紋は2つの安全機構（診断の鮮度・背景結果の
/// 照合）の土台なので、**入力の族を1つでも書き忘れると黙って効かなくなる**。族ごとに「変えたら指紋も
/// 変わる」ことを固定する。
///
/// 3.327.0 は <c>staffRange</c> と <c>cons1</c> しか見ておらず、希望や担当可否を変えても古い診断が
/// 残った。その再発をここで止める。
/// </summary>
public class StateFingerprintTest
{
    private static MagiState Base() => new MagiState(
        StartDate: "2026-08-01", EndDate: "2026-08-03",
        Shifts: new List<Shift> { new("休", "休", "0", "1"), new("A", "A", "1", "2") },
        Groups: new List<Group> { new("G", "G"), new("H", "H") },
        StaffList: new List<Staff> { new("s0", 0, 0), new("s1", 1, 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 0 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "2" }, new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1, 0 }, new List<int> { 1, 0, 1 } },
        Wishes: new Dictionary<string, int> { ["0,0"] = 1 },
        StaffRange: new Dictionary<string, Range> { ["0,1"] = new Range("1", "2") },
        NeedDay1: new Dictionary<string, string> { ["1,0"] = "2" },
        NeedDay2: new Dictionary<string, string> { ["1,0"] = "3" },
        Cons1: new List<C1Row> { new("3", "休", "1") },
        Cons2: new List<C2Row> { new("A", "5") },
        Cons3: new List<C3Row> { new(new List<string> { "A", "A" }) },
        Cons3n: new List<C3Row> { new(new List<string> { "A", "休" }) },
        Cons3m: new List<C3Row> { new(new List<string> { "休", "A" }) },
        Cons3mn: new List<C3Row> { new(new List<string> { "休", "休" }) },
        Cons41: new List<C41Row> { new("G", "A", "0", "1") },
        Cons42: new List<C42Row> { new("G", "H", "A", "休") },
        SkillGroups: new List<Group> { new("S", "S") },
        Cons41s: new List<C41Row> { new("S", "A", "0", "1") },
        Cons42s: new List<C42Row> { new("S", "S", "A", "休") },
        ShiftColors: new Dictionary<string, string>(),
        Extras: MinimalState.NoExtras
    );

    /// <summary>Kotlin's <c>list.mapIndexed { i, s -> if (i == idx) f(s) else s }</c> for a single replaced element.</summary>
    private static List<T> ReplaceAt<T>(IReadOnlyList<T> list, int idx, T replacement)
    {
        var result = new List<T>(list);
        result[idx] = replacement;
        return result;
    }

    private static Dictionary<TK, TV> Plus<TK, TV>(IReadOnlyDictionary<TK, TV> map, TK key, TV value) where TK : notnull
    {
        var result = new Dictionary<TK, TV>(map);
        result[key] = value;
        return result;
    }

    /// <summary>各族を1つだけ変えた state を返す。名前は失敗時にどの族かが分かるようにする。</summary>
    private static IEnumerable<(string Label, MagiState State)> Variants(MagiState b)
    {
        yield return ("startDate", b with { StartDate = "2026-09-01" });
        yield return ("endDate", b with { EndDate = "2026-08-04" });
        yield return ("use2Patterns", b with { Use2Patterns = true });
        yield return ("シフト名", b with { Shifts = ReplaceAt(b.Shifts, 1, b.Shifts[1] with { Name = "A2" }) });
        yield return ("シフト記号", b with { Shifts = ReplaceAt(b.Shifts, 1, b.Shifts[1] with { Kigou = "B" }) });
        yield return ("必要人数(既定)", b with { Shifts = ReplaceAt(b.Shifts, 1, b.Shifts[1] with { Need1 = "2" }) });
        yield return ("上限人数(既定)", b with { Shifts = ReplaceAt(b.Shifts, 1, b.Shifts[1] with { Need2 = "9" }) });
        yield return ("群", b with { Groups = new List<Group>(b.Groups) { new("I", "I") } });
        yield return ("スキル群", b with { SkillGroups = new List<Group>(b.SkillGroups) { new("T", "T") } });
        yield return ("職員名", b with { StaffList = ReplaceAt(b.StaffList, 0, b.StaffList[0] with { Name = "x" }) });
        yield return ("職員の所属群", b with { StaffList = ReplaceAt(b.StaffList, 0, b.StaffList[0] with { GroupIdx = 1 }) });
        yield return ("職員のスキル群", b with { StaffList = ReplaceAt(b.StaffList, 0, b.StaffList[0] with { SkillIdx = -1 }) });
        yield return ("担当可否", b with { GroupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 0 }, new List<int> { 1, 0 } } });
        yield return ("適切回数", b with { GroupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "3" }, new List<string> { "", "" } } });
        yield return ("希望", b with { Wishes = new Dictionary<string, int> { ["0,0"] = 0 } });
        yield return ("希望(件数)", b with { Wishes = Plus(b.Wishes, "1,1", 1) });
        yield return ("個人の回数", b with { StaffRange = new Dictionary<string, Range> { ["0,1"] = new Range("1", "3") } });
        yield return ("日別の最低人数", b with { NeedDay1 = new Dictionary<string, string> { ["1,0"] = "1" } });
        yield return ("日別の上限人数", b with { NeedDay2 = new Dictionary<string, string> { ["1,0"] = "4" } });
        yield return ("窓の要件", b with { Cons1 = new List<C1Row> { new("4", "休", "1") } });
        yield return ("個人の合計", b with { Cons2 = new List<C2Row> { new("A", "6") } });
        yield return ("必須の並び", b with { Cons3 = new List<C3Row> { new(new List<string> { "A", "休" }) } });
        yield return ("禁止の並び", b with { Cons3n = new List<C3Row> { new(new List<string> { "A", "A" }) } });
        yield return ("推奨の並び", b with { Cons3m = new List<C3Row> { new(new List<string> { "A", "A" }) } });
        yield return ("回避の並び", b with { Cons3mn = new List<C3Row> { new(new List<string> { "A", "A" }) } });
        yield return ("群のレンジ", b with { Cons41 = new List<C41Row> { new("G", "A", "1", "1") } });
        yield return ("スキル群のレンジ", b with { Cons41s = new List<C41Row> { new("S", "A", "1", "1") } });
        yield return ("群ペア禁止", b with { Cons42 = new List<C42Row> { new("G", "H", "休", "A") } });
        yield return ("スキル群ペア禁止", b with { Cons42s = new List<C42Row> { new("S", "S", "休", "A") } });
    }

    [Fact]
    public void EveryInputFamilyChangesTheFingerprint()
    {
        var b = Base();
        long h0 = StateFingerprint.Of(b);
        foreach (var (label, v) in Variants(b))
        {
            Assert.True(h0 != StateFingerprint.Of(v),
                $"「{label}」を変えたのに指紋が変わらない（この族の変更を診断・結果照合が見逃す）");
        }
    }

    [Fact]
    public void EveryVariantIsDistinctFromEachOther()
    {
        // 族どうしの衝突も見る（別の族を変えたのに同じ値になると、片方の変更が実質無視される）。
        var b = Base();
        var seen = new Dictionary<long, string> { [StateFingerprint.Of(b)] = "変更なし" };
        foreach (var (label, v) in Variants(b))
        {
            long h = StateFingerprint.Of(v);
            bool collided = seen.TryGetValue(h, out var prev);
            Assert.False(collided, $"「{label}」と「{prev}」の指紋が衝突している");
            seen[h] = label;
        }
    }

    [Fact]
    public void ScheduleIsDeliberatelyExcluded()
    {
        // 盤面は別（boardKey）で見るので、ここに混ぜると結果の適用で必ず不一致になり使えなくなる。
        var b = Base();
        var moved = b with { Schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 0, 1 }, new List<int> { 0, 1, 0 } } };
        Assert.Equal(StateFingerprint.Of(b), StateFingerprint.Of(moved));
    }

    [Fact]
    public void RowShapeChangesTheFingerprint()
    {
        // [3.333.0/外部レビュー Medium] 族ごとに1つ値を変えるテスト（上の29件）は**行の区切り**を
        //   一度も試していなかった。可変長の行を素通しで連結すると、値の並びが同じで**構造だけ違う**
        //   入力が同じ指紋になる。担当可否は「群×シフト」、連続パターンは「1本の並び」なので、
        //   区切りが無いと別物が一致して古い診断・古い結果が新しい入力のものとして通る。
        var b = Base();
        var shape = new List<(string Label, MagiState A, MagiState C)>
        {
            ("担当可否の行の切れ目",
                b with { GroupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0 } } },
                b with { GroupShift = new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1, 0 } } }),
            ("適切回数の行の切れ目",
                b with { GroupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "1", "2" }, new List<string> { "3" } } },
                b with { GroupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "1" }, new List<string> { "2", "3" } } }),
            ("禁止の並びの切れ目",
                b with { Cons3n = new List<C3Row> { new(new List<string> { "A", "休" }) } },
                b with { Cons3n = new List<C3Row> { new(new List<string> { "A" }), new(new List<string> { "休" }) } }),
            ("必須の並びの切れ目",
                b with { Cons3 = new List<C3Row> { new(new List<string> { "A", "A", "休" }) } },
                b with { Cons3 = new List<C3Row> { new(new List<string> { "A", "A" }), new(new List<string> { "休" }) } }),
        };
        foreach (var (label, a, c) in shape)
        {
            Assert.True(StateFingerprint.Of(a) != StateFingerprint.Of(c),
                $"「{label}」が違うのに指紋が一致する（構造の違いを見逃す）");
        }
    }

    [Fact]
    public void SameStateGivesSameFingerprint()
    {
        Assert.Equal(StateFingerprint.Of(Base()), StateFingerprint.Of(Base()));
    }
}
