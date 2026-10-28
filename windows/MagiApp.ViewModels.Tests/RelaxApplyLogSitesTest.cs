using System.Text.RegularExpressions;

namespace MagiApp.ViewModels.Tests;

/// <summary>S6 の確定は終わり方を問わず「S6反映 結果=…」を 1 行残す: RelaxAndApply の各 return の直前の区間に ApplyLog がちょうど 1 つ。Kotlin <c>RelaxApplyLogSitesTest</c> の移植。</summary>
public class RelaxApplyLogSitesTest
{
    private static string SourceDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "MagiApp.ViewModels");
            if (File.Exists(Path.Combine(c, "MagiApp.ViewModels.csproj"))) return c;
        }
        throw new DirectoryNotFoundException("MagiApp.ViewModels");
    }

    [Fact]
    public void EveryExitOfRelaxAndApplyLogsOneResultLine()
    {
        var src = File.ReadAllText(Path.Combine(SourceDir(), "MagiViewModel.RelaxTrial.cs")).Replace("\r\n", "\n");
        var start = src.IndexOf("public RelaxApproval.Verdict RelaxAndApply(", StringComparison.Ordinal);
        var end = src.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var segs = Regex.Split(src[start..end], @"return (?:new )?RelaxApproval\.Verdict\.").SkipLast(1).ToList();
        Assert.True(segs.Count >= 8, $"return は {segs.Count} か所");
        for (var i = 0; i < segs.Count; i++)
            Assert.True(Regex.Matches(segs[i], @"RelaxApproval\.ApplyLog\(").Count == 1, $"return #{i}");
    }
}
