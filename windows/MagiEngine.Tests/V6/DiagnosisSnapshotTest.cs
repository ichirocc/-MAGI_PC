using System.Text;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// 全面改修 P0 の安全網（Android <c>docs/overhaul_design.md</c> §6.1 の 3）。6 fixture について <c>V6SanityPort.Build</c> の診断
/// （警告・注記・設定の案内・不可能な希望など）を Android が生成した基準版の写しと照合する。
/// <c>wishFingerprint</c> は言語ごとの値（<see cref="WishDiagnosis.Fingerprint"/>、保存しない）なので有無だけを比べる。
/// </summary>
public class DiagnosisSnapshotTest
{
    [Fact(Skip = "Android 3.723.1〜3.723.3 の診断（SettingIssue.ZeroCap・対象外（上限0）の文言・DEMAND の下限合計）の移植待ち")]
    public void DiagnosisMatchesTheBaselineSnapshotForEveryFixture()
    {
        foreach (var name in CheckerSnapshotTest.Fixtures)
        {
            var actual = Render(V6SanityPort.Build(CheckerSnapshotTest.Load(name)));
            var expected = MaskFingerprint(CheckerSnapshotTest.ReadSnapshot($"diagnosis_{name}.txt"));
            Assert.True(expected == MaskFingerprint(actual), $"診断が基準版と違う（{name}）\n{actual}");
        }
    }

    private static string MaskFingerprint(string s) => System.Text.RegularExpressions.Regex.Replace(s, "wishFingerprint=-?[0-9]+", "wishFingerprint=#");

    private static string Render(V6SanityReport r)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(s).Append('\n');
        Line($"ok={KotlinText.Of(r.Ok)}");
        foreach (var x in r.Warns) Line($"warn {x}");
        foreach (var x in r.Notes) Line($"note {x}");
        Line($"bits {r.LoadDataBitSummary}");
        foreach (var x in r.LoadDataBitDetails) Line($"bit {x}");
        foreach (var x in r.ShiftCountDiagnostics) Line($"shiftCount {KotlinText.Of(x)}");
        foreach (var x in r.ImpossibleWishes) Line($"impossible {KotlinText.Of(x)}");
        foreach (var x in r.DuplicateSeqConstraints) Line($"dupSeq {x}");
        foreach (var x in r.Guidance) Line($"guide {KotlinText.Of(x)}");
        return sb.ToString();
    }
}
