namespace MagiEngine.V6;

/// <summary>
/// **Phase 5c minimal slice** of Kotlin's <c>AdaptiveEliteArchive.kt</c> (185 lines total — the
/// elite-schedule archive used by <c>runAdaptivePortfolio</c>). Only
/// <see cref="ScheduleDistance(int[][], int[][])"/> is ported here: it is a direct dependency of
/// <see cref="V6NativeOptimizer"/>'s phase-5c driver functions (<c>RunMultiWorker</c>'s "distinct
/// solutions" diagnostic, and the <c>ScheduleDistance</c> delegate documented alongside
/// <c>RoleExploreFor</c>/<c>RoleAcceptFor</c>/<c>RoleOpSelectFor</c>). The remainder of the Kotlin
/// class (the elite pool itself, registration, snapshotting, path-relinking/fusion selection) is
/// phase-5d/5e scope (<c>runAdaptivePortfolio</c>/<c>EliteIntegrationPolish</c>) and is deliberately
/// not ported here — the sibling helpers <c>compareReports</c>/<c>better</c>/<c>sameObjective</c>
/// the Kotlin companion object also defines are one-line redundant wrappers over
/// <see cref="UnifiedViolationChecker.ReportComparer"/>/<see cref="UnifiedViolationChecker.BetterReport"/>,
/// already fully available here without duplication.
/// </summary>
public static class AdaptiveEliteArchive
{
    /// <summary>
    /// [3.266.0/hypothesis basin diversity, Kotlin原本] 変更セル数。差分幅（差分セル数＋行長の
    /// 食い違い＋どちらかにしか無い行の全セル）を数える距離関数。
    /// </summary>
    public static int ScheduleDistance(int[][] a, int[][] b)
    {
        int d = 0;
        int rows = Math.Min(a.Length, b.Length);
        for (int i = 0; i < rows; i++)
        {
            int cols = Math.Min(a[i].Length, b[i].Length);
            for (int j = 0; j < cols; j++) if (a[i][j] != b[i][j]) d++;
            d += Math.Abs(a[i].Length - b[i].Length);
        }
        for (int i = rows; i < a.Length; i++) d += a[i].Length;
        for (int i = rows; i < b.Length; i++) d += b[i].Length;
        return d;
    }
}
