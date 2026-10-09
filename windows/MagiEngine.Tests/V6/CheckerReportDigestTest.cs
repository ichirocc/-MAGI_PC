using System.Collections;
using System.Security.Cryptography;
using System.Text;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// <see cref="UnifiedViolationChecker.Check"/> の出力全体（Logs を除く）を、4 つの実データ fixture から作った乱択盤面 2000 枚で
/// 1 つのダイジェストに固定する。Kotlin <c>CheckerReportDigestTest.kt</c> の移植。乱数列が Kotlin と異なるため期待値は C# 独自で、
/// 割当削減の移植（be28bba）の前後で同じ値になることを確かめてある。
/// </summary>
public class CheckerReportDigestTest
{
    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static string Fmt(object? v) => v switch
    {
        null => "null",
        string s => s,
        IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(Fmt)) + "]",
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!,
    };

    private static void Map<TV>(StringBuilder sb, string tag, IReadOnlyDictionary<string, TV> m)
    {
        sb.Append(tag).Append('{');
        foreach (var (k, v) in m) sb.Append(k).Append('=').Append(Fmt(v)).Append(';');
        sb.Append('}');
    }

    private static string Canon(ViolationReport r)
    {
        var sb = new StringBuilder();
        Map(sb, "v", r.Violations); Map(sb, "n", r.NeedViolations); Map(sb, "c", r.CountViolations);
        Map(sb, "cf", r.CellFamilies); Map(sb, "kf", r.CountFamilies); Map(sb, "nf", r.NeedFamilies);
        Map(sb, "b", r.Breakdown); Map(sb, "d", r.DistLocations);
        sb.Append("t=").Append(r.Total).Append(";h=").Append(r.Hard).Append(";s=").Append(r.Soft);
        sb.Append(";w=").Append(BitConverter.DoubleToInt64Bits(r.WeightedScore)).Append(";r=").Append(Fmt(r.C1Runs)).Append('\n');
        return sb.ToString();
    }

    [Fact]
    public void CheckerReportDigestIsStable()
    {
        var fixtures = new[] { "oct2026_grid_state.json", "golden_state.json", "sept2026_state.json", "sample_state_v6.json" }.Select(Load).ToArray();
        var rnd = new Random(20260929);
        using var md = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var n = 0; n < 2000; n++)
        {
            var st = fixtures[n % fixtures.Length];
            var s = st.Schedule.ToIntArray2D();
            var k = st.Shifts.Count;
            var flips = n % 10 == 0 ? s.Length * (s.FirstOrDefault()?.Length ?? 0) : 1 + rnd.Next(40);
            for (var f = 0; f < flips; f++)
            {
                var i = rnd.Next(s.Length);
                var j = rnd.Next(s[i].Length);
                s[i][j] = rnd.Next(-1, k);
            }
            md.AppendData(Encoding.UTF8.GetBytes(Canon(UnifiedViolationChecker.Check(st, s, quantitativeRangeEval: n % 7 == 0))));
        }
        // 不一致なら全桁を出す（Assert.Equal の文字列差分は途中で切れて、期待値の更新に使えない）。
        var hex = Convert.ToHexString(md.GetHashAndReset()).ToLowerInvariant();
        Assert.True(hex == Expected, $"digest={hex}");
    }

    // weightedScore を含むので重みを変えると動く（3.647.0 fair 2→5 で更新。旧: 7936ccc7…）。Breakdown のキーも含むので族を足すと動く
    // （3.653.0 extWish 追加＝全報告に extWish=0 が増える。旧: 1a2feb00…）。
    private const string Expected = "ebf3caea3ebcc10f854b3666bc0df2c23fb6e1f2d4920560e444bbda766ec8bd";
}
