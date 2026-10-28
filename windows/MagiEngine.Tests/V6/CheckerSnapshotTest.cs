using System.Text;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// 全面改修 P0 の安全網（Android <c>docs/overhaul_design.md</c> §6.1 の 2）。6 fixture の入力盤面について、チェッカーの出力
/// （族別件数・hard/soft/total/weightedScore・セル/被覆/回数の各キー空間の違反と重なり）を基準版の写しと照合する。
/// 期待値は Android（Kotlin）が生成した <c>Fixtures/snapshots/checker_*.txt</c> の写し＝言語をまたいだ一致の検査でもある。
/// </summary>
public class CheckerSnapshotTest
{
    public static readonly string[] Fixtures = { "golden_state", "sample_state_v6", "blocked_covu_state", "sept2026_state", "oct2026_grid_state", "full_coverage_state" };

    public static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw($"{name}.json"));

    public static string ReadSnapshot(string file) => FixtureLoader.ReadRaw(Path.Combine("snapshots", file));

    [Fact]
    public void CheckerOutputMatchesTheBaselineSnapshotForEveryFixture()
    {
        foreach (var name in Fixtures)
        {
            var actual = Render(UnifiedViolationChecker.Check(Load(name)));
            Assert.True(ReadSnapshot($"checker_{name}.txt") == actual, $"チェッカーの出力が基準版と違う（{name}）\n{actual}");
        }
    }

    public static string Render(ViolationReport r)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append('\n');
        Line($"hard={r.Hard} soft={r.Soft} total={r.Total} weighted={KotlinText.Double(r.WeightedScore)}");
        foreach (var k in MirrorKeys.All) Line($"bd.{k}={r.Breakdown.GetValueOrDefault(k, 0)}");
        void Section<T>(string tag, IReadOnlyDictionary<string, T> m)
        {
            foreach (var key in m.Keys.OrderBy(x => x, StringComparer.Ordinal)) Line($"{tag} {key}={KotlinText.Of(m[key])}");
        }
        Section("cell", r.Violations);
        Section("need", r.NeedViolations);
        Section("count", r.CountViolations);
        Section("cellF", r.CellFamilies);
        Section("countF", r.CountFamilies);
        Section("needF", r.NeedFamilies);
        foreach (var c in r.ExtWishCells.OrderBy(x => x, StringComparer.Ordinal)) Line($"extWish {c}");
        return sb.ToString();
    }
}
