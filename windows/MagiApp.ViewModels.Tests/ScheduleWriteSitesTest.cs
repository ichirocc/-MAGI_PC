using System.Text.RegularExpressions;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>ScheduleWriteSitesTest</c> の移植（3.690.0）。MagiApp.ViewModels の <c>_currentSchedule =</c>・<c>Ui.Schedule =</c>・
/// <c>Schedule = …</c> を含む行を囲むメソッド名で集め、許可した経路と照合する。新しい書き込みの場所はテストが落ちる＝案内からの修正は FixApplyGate を通す。</summary>
public class ScheduleWriteSitesTest
{
    private static readonly HashSet<string> Allowed = new()
    {
        "SetCell", "SetCells",                                           // 手動編集
        "Undo", "Redo", "RollbackBoardCommit",                           // 元に戻す・やり直す
        "LoadAsyncCoreAsync", "MakeUi", "ImportCsvCoreAsync",            // 読込・CSV 取込・UiState の組み立て
        "RunV6FullOptimizeCoreAsync", "RunSoftPolishCoreAsync", "RunInBackgroundCoreAsync", "ApplyBgResult",   // 最適化の採用
        "GenerateSmartInitialCoreAsync", "ApplyAlternative", "ApplyWishes",   // 下書き・別案・希望の反映
        "ApplyStructure", "ApplyStructureWithMessage",                   // 編集タブ
        "RelaxAndApply", "RestoreBeforeRelax", "Restore",                // 設定の緩和と、その事後確認・保存失敗の復旧（Restore＝RelaxRollback の控え）
        "ApplyFixSuggestion",                                            // 改善手（FixApplyGate を通る）
        "RunAgentCoreAsync",                                             // 代行役（AgentLoop の各手が FixApplyGate を通る）
    };
    private static readonly Regex Write = new(@"(\b_currentSchedule\s*=[^=>]|\bUi\.Schedule\s*=[^=>]|\bSchedule\s*=\s*[a-zA-Z])");
    private static readonly Regex Fn = new(@"^\s*(?:public|private|internal|protected)[^=;(]*?\b([A-Za-z0-9_]+)\s*(?:<[^>]*>)?\s*\(");

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
    public void EveryScheduleWriteIsOnAnAllowedPath()
    {
        var bad = new List<string>();
        var sites = 0;
        foreach (var f in Directory.GetFiles(SourceDir(), "*.cs").OrderBy(x => x, StringComparer.Ordinal))
        {
            var cur = "?";
            var n = 0;
            foreach (var line in File.ReadLines(f))
            {
                n++;
                if (Fn.Match(line) is { Success: true } m) cur = m.Groups[1].Value;
                if (line.TrimStart().StartsWith("//")) continue;
                if (!Write.IsMatch(line)) continue;
                sites++;
                if (!Allowed.Contains(cur)) bad.Add($"{Path.GetFileName(f)}:{n} ({cur})");
            }
        }
        Assert.True(sites > 10, "書き込みの場所が見つかりません（検索の式が古い）");
        Assert.True(bad.Count == 0, $"勤務表を書き換える新しい場所があります: {string.Join(", ", bad)}。案内からの修正は PreviewFixSuggestion → ApplyFixSuggestion（FixApplyGate）を通してください");
    }
}
