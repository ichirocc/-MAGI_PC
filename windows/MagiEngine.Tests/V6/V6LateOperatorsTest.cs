using System.Text.Json;
using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range collides by simple name with MagiEngine.Model.Range (see MinimalState.cs's own
// note on the same issue) — alias it explicitly since this file constructs a StaffRange dictionary.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Faithful port of Kotlin's <c>V6LateOperatorsTest</c>.
///
/// [HF528/541移植] RectSwap2 / C1BlockN の不変条件:
///  (1) 同日内交換のみ → 日×シフトの被覆カウントは常に保存
///  (2) HF537ゲート → 採用後の HARD は開始時以下
///  (3) Improve() は入力 schedule を変更しない(コピーに適用)
/// </summary>
public class V6LateOperatorsTest
{
    private static MagiState St() => MinimalState.Build(
        startDate: "2026-06-01", endDate: "2026-06-05",
        shifts: new List<Shift>
        {
            new("日勤A", "A", "2", ""),
            new("日勤B", "B", "1", ""),
            new("休み", "休", "", ""),
        },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0), new("s2", 0), new("s3", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 2, 1, 2, 1 }, // s0: A不足(=range違反者) → Rect(D)/BlkN の標的
            new List<int> { 0, 0, 0, 0, 0 },
            new List<int> { 0, 0, 0, 0, 0 },
            new List<int> { 0, 1, 0, 1, 0 },
        },
        staffRange: new Dictionary<string, Range> { ["0,0"] = new Range("4", "") }, // s0 の A 下限4(現0)
        cons1: new List<C1Row> { new("3", "A", "3") } // 3日窓にA3回
    );

    private static int[][] Coverage(int[][] sched, int k, int t)
    {
        var c = new int[k][];
        for (var kk = 0; kk < k; kk++) c[kk] = new int[t];
        for (var i = 0; i < sched.Length; i++)
        {
            for (var j = 0; j < t; j++)
            {
                var shiftK = sched[i][j];
                if (shiftK is >= 0 && shiftK < k) c[shiftK][j]++;
            }
        }
        return c;
    }

    [Fact]
    public void CoveragePreservedAndHardNeverWorse()
    {
        var state = St();
        var sched = state.Schedule.ToIntArray2D();
        var snapshot = sched.Copy2D();
        var pre = UnifiedViolationChecker.Check(state, sched);
        var covPre = Coverage(sched, state.ShiftCount, state.DayCount);

        var res = V6LateOperators.Improve(
            state, sched, pre, new JavaRandom(7),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3000, rectTry: 60, blkTry: 60);

        // (3) 入力不変
        for (var i = 0; i < sched.Length; i++) Assert.True(sched[i].SequenceEqual(snapshot[i]));
        // (1) 被覆保存
        var covPost = Coverage(res.Schedule, state.ShiftCount, state.DayCount);
        for (var k = 0; k < covPre.Length; k++)
            Assert.True(covPre[k].SequenceEqual(covPost[k]), $"coverage k={k}");
        // (2) HARD 非悪化
        Assert.True(res.Report.Hard <= pre.Hard, $"hard {res.Report.Hard} <= {pre.Hard}");
        // 採用件数とログ件数の一致
        Assert.Equal(res.Rect + res.BlkN, res.Logs.Count);
        // 採用ゼロなら schedule/report は実質入力同等
        if (res.Rect + res.BlkN == 0)
        {
            Assert.Equal(pre.Hard, res.Report.Hard);
            Assert.Equal(pre.Soft, res.Report.Soft);
        }
    }

    [Fact]
    public void OptFlagBoolReadsExtras()
    {
        var baseState = St();
        Assert.True(V6LateOperators.OptFlagBool(baseState, "rectSwap", true)); // 未設定→既定
        // [C#移植メモ] Kotlin原本の注記「JVM単体テストでは android.jar の org.json がスタブのため
        //   Map形のみ検証(JSONObject分岐は実機経路)」は、C#側にそもそも当てはまらない
        //   （MagiState.Extras は常に JsonElement 一種類・org.json/Map の二形が無い）ので、
        //   JsonElement(Object) を直接組み立てて OptFlagBool の実経路をそのまま検証する。
        var mapOn = baseState with { Extras = ExtrasWithOptFlag("rectSwap", true) };
        var mapOff = baseState with { Extras = ExtrasWithOptFlag("rectSwap", false) };
        Assert.True(V6LateOperators.OptFlagBool(mapOn, "rectSwap", false));
        Assert.True(!V6LateOperators.OptFlagBool(mapOff, "rectSwap", true));
    }

    private static IReadOnlyDictionary<string, JsonElement> ExtrasWithOptFlag(string name, bool value)
    {
        var json = $"{{\"{name}\":{(value ? "true" : "false")}}}";
        // JsonDocument.Parse の RootElement は文書が生きている間しか有効でないため、Clone() で
        // 文書のライフタイムから切り離した独立コピーを保持する（.NET の標準イディオム）。
        using var doc = JsonDocument.Parse(json);
        return new Dictionary<string, JsonElement> { ["optFlags"] = doc.RootElement.Clone() };
    }
}
